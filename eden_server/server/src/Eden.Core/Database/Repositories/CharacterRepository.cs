using Dapper;
using Npgsql;
using Eden.Core.Database.Entities;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Logging;

namespace Eden.Core.Database.Repositories;

/// <summary>Ligne de personnage telle que la renvoie la liste native
/// (CNC_CW_ClientGetCharacters, section 1).</summary>
public sealed class CharacterListRow
{
    public int Id;
    public string Name = "";
    public short ClassId;
    /// <summary>class_lv natif → +0x14 de l'entrée (niveau de la classe
    /// courante affiché en sélection). Le nom « Feature » date d'une
    /// hypothèse erronée (create_flag) — désasm PGCharacterDBO :
    /// GetData("class_lv") → this+0x14.</summary>
    public short ClassLv;
    public short RaceId;
    public short NodeId;
    public short Level;
    public byte[] Appearance = new byte[32];
    /// <summary>Aperçu équipement : conteneur -> items [(loc, item_id)].</summary>
    public List<(ushort Container, ushort Loc, ushort ItemId)> PreviewItems { get; } = new();
}

/// <summary>
/// Implémentation PostgreSQL du dépôt des personnages joueurs via Dapper.
/// Reversé depuis lapis::CPlayerCharDBO et edenfr_db.player_characters.
/// </summary>
public sealed partial class CharacterRepository : ICharacterRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Store Redis des mutations chaudes (or/position/vitals) —
    /// <c>null</c> = tout en PostgreSQL direct (DB-3 désactivé).</summary>
    public Eden.Core.Hot.HotCharacterStore? Hot { get; }

    public CharacterRepository(NpgsqlDataSource db,
        Eden.Core.Hot.HotCharacterStore? hot = null)
    {
        _db = db;
        Hot = hot;
    }

    public async Task<IReadOnlyList<CharacterSummary>> GetCharacterSummariesByAccountIdAsync(int accountId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS CharacterId,
                   given_name AS Name,
                   level AS Level,
                   class_id AS ClassId,
                   race_id AS RaceId,
                   gender_id AS GenderId,
                   hair AS HairStyle,
                   face AS FaceStyle,
                   hair_color AS HairColor,
                   skin_color AS SkinColor,
                   node_id AS NodeId
            FROM player_characters
            WHERE account_id = @AccountId AND deleted_time = 0
            ORDER BY id ASC;";

        var results = await connection.QueryAsync<CharacterSummary>(sql, new { AccountId = accountId });
        return results.ToList();
    }

    /// <summary>Liste native : champs affichés + blob apparence + aperçu
    /// équipement (conteneur 0 = page visible, conteneur 1 = toujours vide).</summary>
    public async Task<IReadOnlyList<CharacterListRow>> GetCharactersForListAsync(int accountId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id, given_name, class_id, race_id, gender_id, node_id, level,
                   COALESCE(class_lv, 0) AS class_lv,
                   COALESCE(appearance_data, '\\x'::bytea) AS appearance_data
            FROM player_characters
            WHERE account_id = @AccountId AND deleted_time = 0
            ORDER BY id ASC;";

        var rows = (await connection.QueryAsync(sql, new { AccountId = accountId })).ToList();
        var list = new List<CharacterListRow>(rows.Count);
        foreach (var r in rows)
        {
            var app = (byte[]?)r.appearance_data;
            var row = new CharacterListRow
            {
                Id = (int)r.id,
                Name = (string)r.given_name,
                ClassId = (short)r.class_id,
                ClassLv = (short)r.class_lv,
                RaceId = (short)r.race_id,
                NodeId = (short)r.node_id,
                Level = (short)r.level,
            };
            if (app is { Length: > 0 })
                Array.Copy(app, row.Appearance, Math.Min(32, app.Length));
            list.Add(row);
        }

        if (list.Count == 0) return list;

        var ids = list.Select(x => x.Id).ToArray();
        // Aperçu natif : les items portés (conteneur 0, locs visibles 2/4/5/7)
        // remontés comme noeud clé=0 ; noeud clé=1 toujours vide.
        const string previewSql = @"
            SELECT player_id AS char_id, loc, item_id
            FROM equipment2
            WHERE player_id = ANY(@Ids) AND container_index = 0 AND loc IN (2, 4, 5, 7)
            ORDER BY player_id, loc;";
        try
        {
            var previews = await connection.QueryAsync(previewSql, new { Ids = ids });
            var byChar = list.ToDictionary(x => x.Id);
            foreach (var p in previews)
            {
                if (byChar.TryGetValue((int)p.char_id, out var row))
                    row.PreviewItems.Add((0, (ushort)(int)p.loc, (ushort)(int)p.item_id));
            }
        }
        catch
        {
            // Table equipment2 absente : l'aperçu reste vide, la liste reste valide.
        }

        return list;
    }

    /// <summary>Nombre de personnages vivants du compte (limite native = 3).</summary>
    public async Task<int> GetCharacterCountAsync(int accountId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM player_characters WHERE account_id = @AccountId AND deleted_time = 0",
            new { AccountId = accountId });
    }

    /// <summary>Transaction de création native :
    /// player_characters (INSERT natif exact) + player_classlist {1,2,3,4,5,19}
    /// + player_spells (23 spellmasters initiaux) + player_card + items de départ
    /// (equipment + inventory) + edenfr_account.character_number.</summary>
    public async Task<int> CreateCharacterNativeAsync(
        NpgsqlConnection edenfr_account, PlayerCharacterEntity c, byte createFlag)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await edenfr_account.OpenAsync();
        using var tx = await connection.BeginTransactionAsync();

        // UID natif : CUIDAllocator, plage observée 200000000+.
        var newId = await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(id), 200000000) + 1 FROM player_characters", transaction: tx);
        c.Id = newId;

        // hp/max_hp/mp/max_mp : valeurs lv1 calculées via la formule
        // native (S_Level 251/195 + CON/WIS×%classe) — les constantes
        // 1123/123/1069/69 capturées étaient les stats réelles d'un
        // perso test de plus haut niveau, pas des défauts lv1.
        // Compte edenfr_account.accounts.role = 'Admin' -> privilege GM 5 des la
        // creation (sinon //commandes refusees 0x5d0 : gate privilege > 0).
        var role = await edenfr_account.ExecuteScalarAsync<string?>(
            "SELECT role FROM accounts WHERE id = @A",
            new { A = c.AccountId });
        var privilege = string.Equals(role, "Admin",
            StringComparison.OrdinalIgnoreCase) ? 5 : 0;

        const string insert = @"
            INSERT INTO player_characters(
                id, given_name, node_id, x, y, z, face_dir,
                class_id, race_id, gender_id,
                hair, face, hair_color, skin_color, eyes_color,
                level, hp, max_hp, mp, max_mp,
                account_id, account_name, privilege, revive_area_id,
                aa_point, spell_card_attr, eyes_color2,
                appearance_data, create_flag)
            VALUES (
                @Id, @GivenName, @NodeId, @X, @Y, @Z, @FaceDir,
                @ClassId, @RaceId, @GenderId,
                @Hair, @Face, @HairColor, @SkinColor, @EyesColor,
                1, @Hp, @MaxHp, @Mp, @MaxMp,
                @AccountId, @AccountName, @Privilege, 131075,
                1, 255, @EyesColor2,
                @Appearance, @CreateFlag);";
        await connection.ExecuteAsync(insert, new
        {
            c.Id, c.GivenName, c.NodeId,
            X = c.PositionX, Y = c.PositionY, Z = c.PositionZ, FaceDir = c.FacingDirection,
            c.ClassId, c.RaceId, c.GenderId, c.AccountId, c.AccountName,
            Privilege = privilege,
            Hair = (long)c.HairStyle, Face = (long)c.FaceStyle,
            HairColor = (long)c.HairColor, SkinColor = (long)c.SkinColor,
            EyesColor = (long)c.EyesColor, EyesColor2 = (long)c.EyesColor2,
            Hp = c.MaxHp > 0 ? c.MaxHp : 311, MaxHp = c.MaxHp > 0 ? c.MaxHp : 311,
            Mp = c.MaxMp > 0 ? c.MaxMp : 261, MaxMp = c.MaxMp > 0 ? c.MaxMp : 261,
            Appearance = c.AppearanceData, CreateFlag = (short)createFlag,
        }, tx);

        // Classes initiales natives (capturées : {1,2,3,4,5,19}, lv1, aa_point=1).
        foreach (var cls in new[] { 1, 2, 3, 4, 5, 19 })
            await connection.ExecuteAsync(
                "INSERT INTO player_classlist (player_id, class, level, exp, point, aa_point) VALUES (@Id, @Cls, 1, 0, 0, 1)",
                new { Id = newId, Cls = cls }, tx);

        // Spellmasters initiaux natifs (23 lignes observées sur perso frais).
        foreach (var sm in NativeStarterSpells)
            await connection.ExecuteAsync(
                "INSERT INTO player_spells (player_id, spellmaster_id, spell_order) VALUES (@Id, @Sm, 1)",
                new { Id = newId, Sm = sm }, tx);

        await connection.ExecuteAsync(
            @"INSERT INTO player_card (player_id, gender, blood_type, birth_month, birth_day,
                player_sign, region, mood, like_1, like_2, like_3, hate_1, hate_2, introduct)
              VALUES (@Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '')",
            new { Id = newId }, tx);

        // Items de départ (classe 1 — capture native) : equipment + inventory.
        await InsertStarterItemsAsync(connection, tx, newId);

        await tx.CommitAsync();

        // edenfr_account.character_number : world_id natif = 1000.
        const string bump = @"
            INSERT INTO character_number (account_id, world_id, character) VALUES (@Acc, 1000, 1)
            ON CONFLICT (account_id, world_id)
            DO UPDATE SET character = character_number.character + 1;";
        try { await edenfr_account.ExecuteAsync(bump, new { Acc = c.AccountId }); }
        catch
        {
            await edenfr_account.ExecuteAsync(
                @"UPDATE character_number SET character = character + 1
                  WHERE account_id = @Acc AND world_id = 1000;
                  INSERT INTO character_number (account_id, world_id, character)
                  SELECT @Acc, 1000, 1 WHERE NOT EXISTS (
                    SELECT 1 FROM character_number WHERE account_id = @Acc AND world_id = 1000);",
                new { Acc = c.AccountId });
        }

        return newId;
    }

    /// <summary>Spellmasters initiaux natifs (23) — capturés sur un perso frais.</summary>
    private static readonly int[] NativeStarterSpells =
    {
        -15535, -15525, -15520, -15515, -15510, -15505, -15498,
        -15386, -15335, -15334, -15328, -15323, -15317, -15311,
        -15305, -15304, -15303, -15267, -15054, -15053, -14536,
        -6435, -6434,
    };

    /// <summary>(item_id, durabilité, loc, conteneur) — set de départ natif classe 1.</summary>
    private static readonly (int Item, int Dur, int Loc, int Container)[] NativeStarterEquipment =
    {
        (31899, 75, 7, 0), (31939, 120, 2, 0), (31940, 75, 4, 0), (31941, 75, 5, 0),
        (31989, 80, 2, 2), (31990, 50, 4, 2), (31991, 50, 5, 2),
        (23857, 80, 2, 3), (23858, 50, 4, 3), (23859, 50, 5, 3),
    };

    private static readonly (int Item, int Dur, int Loc)[] NativeStarterInventory =
    {
        (49004, 1, 0), (31926, 130, 1), (23860, 105, 2), (24170, 75, 3), (24170, 75, 4),
    };

    private async Task InsertStarterItemsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, int playerId)
    {
        try
        {
            var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            const string itemCols = @"id, item_id, durability, rank, identify,
                embedded_id1, embedded_id2, embedded_id3, embedded_id4,
                embedded_id5, embedded_id6, create_time, loc, container_index, player_id";
            // Serials via la séquence item_serial (migration 012) :
            // l'ancien GREATEST(max(equipment2), max(inventory2))+1 ne
            // lisait que 2 tables de l'espace serial — collisions avec
            // les ids alloués ailleurs (et race entre créations).
            foreach (var (item, dur, loc, cont) in NativeStarterEquipment)
            {
                var serial = await ItemSerialAllocator.NextAsync(connection, tx);
                await connection.ExecuteAsync(
                    $@"INSERT INTO equipment2 ({itemCols})
                      VALUES (@Iid, @Item, @Dur, 0, 1, -1, -1, -1, -1, -1, -1, @Now, @Loc, @Cont, @Pid)",
                    new { Iid = serial, Item = item, Dur = dur, Now = now, Loc = loc, Cont = cont, Pid = playerId }, tx);
            }
            foreach (var (item, dur, loc) in NativeStarterInventory)
            {
                var serial = await ItemSerialAllocator.NextAsync(connection, tx);
                await connection.ExecuteAsync(
                    $@"INSERT INTO inventory2 ({itemCols})
                      VALUES (@Iid, @Item, @Dur, 0, 1, -1, -1, -1, -1, -1, -1, @Now, @Loc, 0, @Pid)",
                    new { Iid = serial, Item = item, Dur = dur, Now = now, Loc = loc, Pid = playerId }, tx);
            }
        }
        catch (Exception ex)
        {
            // Le schéma item peut différer — la création du perso ne doit pas échouer pour ça.
            EdenLog.Write($"[World] Items de départ non insérés : {ex.Message}");
        }
    }

    public async Task<PlayerCharacterEntity?> GetCharacterByIdAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   given_name AS GivenName,
                   node_id AS NodeId,
                   x AS PositionX,
                   y AS PositionY,
                   z AS PositionZ,
                   face_dir AS FacingDirection,
                   class_id AS ClassId,
                   race_id AS RaceId,
                   gender_id AS GenderId,
                   hair AS HairStyle,
                   face AS FaceStyle,
                   hair_color AS HairColor,
                   skin_color AS SkinColor,
                   eyes_color AS EyesColor,
                   style AS Style,
                   level AS Level,
                   exp AS Experience,
                   skill_point AS SkillPoints,
                   hp AS CurrentHp,
                   max_hp AS MaxHp,
                   mp AS CurrentMp,
                   max_mp AS MaxMp,
                   combo_point AS ComboPoints,
                   gold::bigint AS Gold,
                   bind_gold AS BindGold,
                   account_id AS AccountId,
                   account_name AS AccountName,
                   privilege AS Privilege,
                   family_id AS FamilyId,
                   revive_area_id AS ReviveAreaId,
                   COALESCE(last_normal_area_id, 0) AS LastNormalAreaId,
                   class_lv AS ClassLevel,
                   class_point AS ClassPoints,
                   create_time AS CreateTime,
                   deleted_time AS DeletedTime,
                   COALESCE(appearance_data, '\\x'::bytea) AS AppearanceData,
                   COALESCE(create_flag, 0) AS CreationFlag,
                   COALESCE(life_skill, -1) AS LifeSkill,
                   COALESCE(visit_family_id, 0) AS VisitFamilyId,
                   COALESCE(cross_zone_node, 0) AS CrossZoneNode,
                   COALESCE(cross_zone_x, 0) AS CrossZoneX,
                   COALESCE(cross_zone_y, 0) AS CrossZoneY,
                   COALESCE(helmet_color, 0) AS HelmetColor,
                   COALESCE(clothes_color, 0) AS ClothesColor,
                   COALESCE(cloak_color, 0) AS CloakColor,
                   COALESCE(appellation, 0) AS Appellation
            FROM player_characters
            WHERE id = @CharacterId AND deleted_time = 0
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<PlayerCharacterEntity>(sql, new { CharacterId = characterId });
    }

    /// <summary>Carte publique d'un perso pour les listes sociales
    /// (amis, recherche) — colonnes du record
    /// <c>CNC_CW_ClientUpdateFriendMember</c> natif : level, node_id,
    /// race_id, class_id, class_lv + nom de guilde via family_id
    /// (mission <c>QueryFriendData</c>, exec+0xb0 → registre familles).</summary>
    public sealed record PlayerCard(
        int CharId, string Name, short Level, short NodeId,
        short RaceId, short ClassId, short ClassLv, string FamilyName);

    /// <summary>Mérites spéciaux du perso (table `special_coins`,
    /// chargée côté natif par CExecutionerDBO::LoadSpecialCoin puis
    /// poussée au client par SendSpecialCoinInfo → CW 78). Retourne
    /// les couples (type 0-11, total) — colonne coinNN ↔ type NN-1,
    /// types 6-8 absents du schéma (rejetés par le natif).</summary>
    public async Task<IReadOnlyList<(short Type, int Total)>>
        GetSpecialCoinsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QuerySingleOrDefaultAsync<dynamic?>(
            "SELECT * FROM special_coins WHERE player_id = @Id",
            new { Id = characterId });
        var list = new List<(short, int)>();
        if (row == null) return list;
        var dict = (IDictionary<string, object?>)row;
        for (var i = 1; i <= 12; i++)
            if (dict.TryGetValue($"coin{i:D2}", out var v) &&
                v != null && (int)Convert.ToInt32(v) != 0)
                list.Add(((short)(i - 1), Convert.ToInt32(v)));
        return list;
    }

    /// <summary>Charge les cartes de plusieurs persos en une requête
    /// (<c>deleted_time = 0</c> seulement).</summary>
    public async Task<IReadOnlyList<PlayerCard>> GetPlayerCardsAsync(
        IReadOnlyCollection<int> characterIds)
    {
        if (characterIds.Count == 0) return [];
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT c.id AS char_id, c.given_name, c.level, c.node_id,
                   c.race_id, c.class_id, COALESCE(c.class_lv, 0) AS class_lv,
                   COALESCE(g.name, '') AS family_name
            FROM player_characters c
            LEFT JOIN family g ON g.id = c.family_id
            WHERE c.id = ANY(@Ids) AND c.deleted_time = 0;";

        var rows = await connection.QueryAsync(sql, new { Ids = characterIds.ToArray() });
        return rows.Select(r => new PlayerCard(
            (int)r.char_id, (string)r.given_name, (short)r.level,
            (short)r.node_id, (short)r.race_id, (short)r.class_id,
            (short)r.class_lv, (string)r.family_name)).ToList();
    }

    public async Task<PlayerCharacterEntity?> GetCharacterByNameAsync(string characterName)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   given_name AS GivenName,
                   node_id AS NodeId,
                   x AS PositionX,
                   y AS PositionY,
                   z AS PositionZ,
                   face_dir AS FacingDirection,
                   class_id AS ClassId,
                   race_id AS RaceId,
                   gender_id AS GenderId,
                   hair AS HairStyle,
                   face AS FaceStyle,
                   hair_color AS HairColor,
                   skin_color AS SkinColor,
                   eyes_color AS EyesColor,
                   style AS Style,
                   level AS Level,
                   exp AS Experience,
                   skill_point AS SkillPoints,
                   hp AS CurrentHp,
                   max_hp AS MaxHp,
                   mp AS CurrentMp,
                   max_mp AS MaxMp,
                   combo_point AS ComboPoints,
                   gold::bigint AS Gold,
                   bind_gold AS BindGold,
                   account_id AS AccountId,
                   account_name AS AccountName,
                   privilege AS Privilege,
                   family_id AS FamilyId,
                   revive_area_id AS ReviveAreaId,
                   COALESCE(last_normal_area_id, 0) AS LastNormalAreaId,
                   class_lv AS ClassLevel,
                   class_point AS ClassPoints,
                   create_time AS CreateTime,
                   deleted_time AS DeletedTime,
                   COALESCE(appearance_data, '\\x'::bytea) AS AppearanceData,
                   COALESCE(create_flag, 0) AS CreationFlag
            FROM player_characters
            WHERE LOWER(given_name) = LOWER(@CharacterName) AND deleted_time = 0
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<PlayerCharacterEntity>(sql, new { CharacterName = characterName });
    }

    public async Task<int> CreateCharacterAsync(PlayerCharacterEntity character)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO player_characters (
                given_name, node_id, x, y, z, face_dir, class_id, race_id, gender_id,
                hair, face, hair_color, skin_color, eyes_color, style,
                level, exp, skill_point, hp, max_hp, mp, max_mp, combo_point,
                gold, bind_gold, account_id, account_name, privilege, family_id,
                revive_area_id, class_lv, class_point, create_time, deleted_time
            ) VALUES (
                @GivenName, @NodeId, @PositionX, @PositionY, @PositionZ, @FacingDirection, @ClassId, @RaceId, @GenderId,
                @HairStyle, @FaceStyle, @HairColor, @SkinColor, @EyesColor, @Style,
                @Level, @Experience, @SkillPoints, @CurrentHp, @MaxHp, @CurrentMp, @MaxMp, @ComboPoints,
                @Gold, @BindGold, @AccountId, @AccountName, @Privilege, @FamilyId,
                @ReviveAreaId, @ClassLevel, @ClassPoints, @CreateTime, 0
            ) RETURNING id;";

        return await connection.ExecuteScalarAsync<int>(sql, character);
    }

    public async Task<bool> DeleteCharacterAsync(int characterId, int accountId = 0)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var sql = accountId > 0
            ? @"UPDATE player_characters SET deleted_time = @DeletedTimestamp WHERE id = @CharacterId AND account_id = @AccountId;"
            : @"UPDATE player_characters SET deleted_time = @DeletedTimestamp WHERE id = @CharacterId;";

        var affected = await connection.ExecuteAsync(sql, new
        {
            DeletedTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            CharacterId = characterId,
            AccountId = accountId
        });

        return affected > 0;
    }

    public async Task UpdateExpAsync(int characterId, long exp, int level)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"UPDATE player_characters
            SET exp = @Exp, level = @Level WHERE id = @CharacterId";
        await connection.ExecuteAsync(sql,
            new { Exp = exp, Level = level, CharacterId = characterId });
    }

    public async Task<long> GetExpAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = "SELECT exp FROM player_characters WHERE id = @CharacterId";
        return await connection.ExecuteScalarAsync<long>(sql, new { CharacterId = characterId });
    }

    public Task<long> GetGoldAsync(int characterId)
        => Hot is null ? GetGoldPgAsync(characterId)
            : Hot.GetGoldAsync(characterId, () => GetGoldPgAsync(characterId));

    private async Task<long> GetGoldPgAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = "SELECT gold::bigint FROM player_characters WHERE id = @CharacterId";
        return await connection.ExecuteScalarAsync<long>(sql, new { CharacterId = characterId });
    }

    public async Task SetGoldAsync(int characterId, long gold)
    {
        if (Hot is not null)
        {
            await Hot.SetGoldAsync(characterId, gold, () => GetGoldPgAsync(characterId));
            return;
        }
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = "UPDATE player_characters SET gold = @Gold WHERE id = @CharacterId";
        await connection.ExecuteAsync(sql, new { Gold = gold, CharacterId = characterId });
    }

    /// <summary>Crédite l'or normal — natif `CCharacter::GainGold`
    /// @0x842a7f0 : `char+0x4b0 += n` borné à [0, 999999999] (le natif
    /// refuse le dépassement ; ici on clamp — usage GM). Retourne le
    /// nouveau total pour l'attr 2 du 280 émis ensuite.</summary>
    public async Task<long> AddGoldAsync(int characterId, long amount)
    {
        if (Hot is not null)
            return await Hot.AddGoldAsync(characterId, amount,
                () => GetGoldPgAsync(characterId));
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET gold = LEAST(999999999, GREATEST(0, gold + @Amount))
            WHERE id = @CharacterId
            RETURNING gold::bigint;";
        return await connection.ExecuteScalarAsync<long>(sql,
            new { Amount = amount, CharacterId = characterId });
    }

    /// <summary>Lit les 4 échéances de sacs banque
    /// `bank_duedate_1..4` (colonnes natives `player_characters`,
    /// écrites par `CPlayerFactory::SetBagDueDate` via le DBO
    /// `bank_duedate_%hd`). Retour int[4] unix, index 0 = sac 1.</summary>
    public async Task<int[]> GetBankDueDatesAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql =
            "SELECT bank_duedate_1 AS D1, bank_duedate_2 AS D2, " +
            "bank_duedate_3 AS D3, bank_duedate_4 AS D4 " +
            "FROM player_characters WHERE id = @Id";
        var r = await connection.QuerySingleAsync<BankDueDates>(
            sql, new { Id = characterId });
        return new[] { r.D1, r.D2, r.D3, r.D4 };
    }

    /// <summary>Persiste l'échéance du sac banque `bagIndex` (0-3) —
    /// natif `SavePlayerCharacter("bank_duedate_%hd", bag+1)`
    /// (SetBagDueDate @0x81133f0). Colonne interpolée : l'index est
    /// borné &lt;4 par l'appelant.</summary>
    public async Task SetBankDueDateAsync(int characterId, int bagIndex,
        int dueUnix)
    {
        if ((uint)bagIndex > 3) return;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var sql = $"UPDATE player_characters " +
                  $"SET bank_duedate_{bagIndex + 1} = @Due " +
                  "WHERE id = @Id";
        await connection.ExecuteAsync(sql,
            new { Due = dueUnix, Id = characterId });
    }

    private sealed class BankDueDates
    {
        public int D1 { get; set; }
        public int D2 { get; set; }
        public int D3 { get; set; }
        public int D4 { get; set; }
    }

    /// <summary>Timestamp du dernier refresh activités recommandées
    /// (`player_characters.recommend_activities_time`, colonne native
    /// lue dans le SELECT de chargement mission et mise à jour par
    /// GetRAUdateTimeUpdateSQLCmd lors du refresh 831).</summary>
    public async Task<int> GetRecommendActivitiesTimeAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql =
            "SELECT COALESCE(recommend_activities_time, 0) AS T " +
            "FROM player_characters WHERE id = @Id";
        return await connection.QuerySingleAsync<int>(sql,
            new { Id = characterId });
    }

    /// <summary>Lignes `recommend_activities` du perso — SELECT natif
    /// CExecutionerDBO::LoadRecommendActivities : « recommend_id,
    /// target_number, is_finished, is_tracing WHERE player_id ».</summary>
    public async Task<IReadOnlyList<(ushort Id, uint Progress,
        int Finished, int Tracing)>> GetRecommendActivitiesAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql =
            "SELECT recommend_id AS Id, target_number AS Progress, " +
            "is_finished AS Finished, is_tracing AS Tracing " +
            "FROM recommend_activities WHERE player_id = @Id";
        var rows = await connection.QueryAsync<RecommendRow>(sql,
            new { Id = characterId });
        return rows.Select(r =>
            ((ushort)r.Id, (uint)r.Progress, r.Finished, r.Tracing))
            .ToList();
    }

    private sealed class RecommendRow
    {
        public int Id { get; set; }
        public int Progress { get; set; }
        public int Finished { get; set; }
        public int Tracing { get; set; }
    }

    /// <summary>Ligne `player_card` native (12 stats + introduct +
    /// is_public) — mise à jour par `CPlayerFactory::PlayerCardUpdate`
    /// (op 1-12 = stat, 0xd = intro, 0xe = public).</summary>
    public sealed record CardRow(short[] Stats, string Intro,
        bool IsPublic)
    {
        /// <summary>Carte vierge (ligne absente — perso legacy).</summary>
        public static CardRow Empty => new(new short[12], "", false);
    }

    /// <summary>Lit `player_card` du perso — null si aucune ligne
    /// (le natif provisionne la carte à la création ; un legacy peut
    /// ne pas l'avoir).</summary>
    public async Task<CardRow?> GetCardRowAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql =
            "SELECT gender AS G, blood_type AS B, birth_month AS BM, " +
            "birth_day AS BD, player_sign AS PS, region AS RG, " +
            "mood AS MD, like_1 AS L1, like_2 AS L2, like_3 AS L3, " +
            "hate_1 AS H1, hate_2 AS H2, introduct AS Intro, " +
            "is_public AS Pub FROM player_card WHERE player_id = @Id";
        var r = await connection.QuerySingleOrDefaultAsync<CardRowDto>(
            sql, new { Id = characterId });
        if (r == null) return null;
        return new CardRow(
            new short[] { (short)r.G, (short)r.B, (short)r.BM,
                (short)r.BD, (short)r.PS, (short)r.RG, (short)r.MD,
                (short)r.L1, (short)r.L2, (short)r.L3, (short)r.H1,
                (short)r.H2 },
            r.Intro, r.Pub != 0);
    }

    private sealed class CardRowDto
    {
        public int G { get; set; }
        public int B { get; set; }
        public int BM { get; set; }
        public int BD { get; set; }
        public int PS { get; set; }
        public int RG { get; set; }
        public int MD { get; set; }
        public int L1 { get; set; }
        public int L2 { get; set; }
        public int L3 { get; set; }
        public int H1 { get; set; }
        public int H2 { get; set; }
        public string Intro { get; set; } = "";
        public int Pub { get; set; }
    }

    /// <summary>Persiste le groupe de métier `life_skill` — natif
    /// `CExecutionerDBO::CheckLifeSkill` : `UPDATE player_characters
    /// SET life_skill = %hd WHERE id = %d` (une seule fois, quand la
    /// colonne est à -1).</summary>
    public async Task SetLifeSkillAsync(int characterId, int lifeSkill)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = "UPDATE player_characters SET life_skill = @LifeSkill WHERE id = @CharacterId";
        await connection.ExecuteAsync(sql, new { LifeSkill = lifeSkill, CharacterId = characterId });
    }

    /// <summary>Enregistre un titre (appellation) — natif
    /// `CDBAgent::Command "Insert Appellation %d"` →
    /// `INSERT INTO player_appellation (player_id, appellation_id)`
    /// (zone/decompiled.c ~0x8103bxx, AddAppellation).</summary>
    public async Task AddAppellationAsync(int characterId, int appellationId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            INSERT INTO player_appellation (player_id, appellation_id)
            VALUES (@CharacterId, @AppellationId)
            ON CONFLICT DO NOTHING;";
        await connection.ExecuteAsync(sql, new
        { CharacterId = characterId, AppellationId = appellationId });
    }

    /// <summary>Titres débloqués du personnage — la table
    /// `player_appellation` remplie par `Insert Appellation %d` natif.
    /// Consommé par QueryAppellation (ClientUpdateAttr attr 0x2c =
    /// nombre de titres, désasm @0x82748c0).</summary>
    public async Task<IReadOnlyList<int>> GetAppellationsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            SELECT appellation_id AS AppellationId
            FROM player_appellation
            WHERE player_id = @CharacterId
            ORDER BY appellation_id;";
        var rows = await connection.QueryAsync<int>(sql,
            new { CharacterId = characterId });
        return rows.AsList();
    }

    /// <summary>Trait héroïque du perso — `player_characters.create_flag`
    /// (1..8 = passif natif 50000+n → enchant 52000+n ; 0 = aucun,
    /// persos créés avant le sélecteur). Lu par l'étape stats
    /// « herotrait ».</summary>
    public async Task<int> GetCreationFlagAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(create_flag, 0) FROM player_characters " +
            "WHERE id = @c", new { c = characterId });
    }

    /// <summary>Assigne le trait héroïque — <c>UPDATE
    /// player_characters SET create_flag</c> (commande GM
    /// <c>//trait</c> : persos créés avant le sélecteur).
    /// 0 = aucun, 1..8 = enchant 52000+n.</summary>
    public async Task SetCreationFlagAsync(int characterId, int flag)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_characters SET create_flag = @f " +
            "WHERE id = @c", new { f = flag, c = characterId });
    }

    /// <summary>Titre actif persisté — `player_characters.appellation`
    /// (char+0x6fc natif). Utilisé par l'étape stats « titles » :
    /// seul l'enchant du titre actif compte.</summary>
    public async Task<int> GetActiveAppellationAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(appellation, 0) FROM player_characters " +
            "WHERE id = @c", new { c = characterId });
    }

    /// <summary>Persiste le titre actif — natif
    /// `SavePlayerCharacter "appellation"` →
    /// `UPDATE player_characters SET appellation = %hd`
    /// (SetPresentAppellation @0x080f2400).</summary>
    public async Task SetAppellationAsync(int characterId, int appellationId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_characters SET appellation = @a " +
            "WHERE id = @c",
            new { a = appellationId, c = characterId });
    }

    /// <summary>Lit `forever_gold` (monnaie « Eternal Coins » —
    /// char+0x4b4 natif) pour le contrôle de cap de
    /// `GainForeverGold` @0x840c3f0.</summary>
    public async Task<long> GetForeverGoldAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql =
            "SELECT COALESCE(forever_gold, 0)::bigint " +
            "FROM player_characters WHERE id = @CharacterId";
        return await connection.ExecuteScalarAsync<long>(sql,
            new { CharacterId = characterId });
    }

    /// <summary>Crédite la monnaie boutique — natif
    /// `CCharacter::GainForeverGold` @0x840c3f0 : char+0x4b4 += n
    /// puis `SavePlayerCharacter("forever_gold")`. Clamp natif
    /// [0, 999999999] comme <see cref="AddGoldAsync"/>. Retourne le
    /// nouveau total pour l'attr 3 du 280 émis ensuite.</summary>
    public async Task<long> AddForeverGoldAsync(int characterId, int amount)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET forever_gold =
                LEAST(999999999, GREATEST(0, COALESCE(forever_gold, 0)
                    + @Amount))
            WHERE id = @CharacterId
            RETURNING forever_gold;";
        return await connection.ExecuteScalarAsync<long>(sql,
            new { Amount = amount, CharacterId = characterId });
    }

    /// <summary>Crédite les « Honor Stars » — natif
    /// `CCharacter::GainColosseumGold` @0x8418f30 : char+0xbbc += n
    /// borné [0, 999999999] puis `SavePlayerCharacter("colosseum_gold")`.
    /// Retourne le nouveau total pour le 525 ClientGladiatorTeamInfo
    /// émis ensuite.</summary>
    public async Task<long> AddColosseumGoldAsync(int characterId,
        int amount)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET colosseum_gold =
                LEAST(999999999, GREATEST(0, COALESCE(colosseum_gold, 0)
                    + @Amount))
            WHERE id = @CharacterId
            RETURNING colosseum_gold;";
        return await connection.ExecuteScalarAsync<long>(sql,
            new { Amount = amount, CharacterId = characterId });
    }

    /// <summary>Soldes des monnaies de fame shops portées par
    /// `player_characters` — `BuyItems` natif @0x080aca40 : colisée
    /// char+0xbbc, war coin char+0x874 (« Battle Merit »), alpaca
    /// coin char+0x878 (cf. `CGlobalInfo::GainWarCoin` @0x8081490 /
    /// `GainAlpacaCoin` @0x8081160 mission).</summary>
    public async Task<(long ColosseumGold, int WarCoin, int AlpacaCoin)>
        GetShopCoinBalancesAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QuerySingleOrDefaultAsync(
            "SELECT COALESCE(colosseum_gold,0) AS colosseum_gold, " +
            "COALESCE(war_coin,0) AS war_coin, " +
            "COALESCE(alpaca_coin,0) AS alpaca_coin " +
            "FROM player_characters WHERE id = @Id",
            new { Id = characterId });
        if (row == null) return (0, 0, 0);
        return ((long)row.colosseum_gold, (int)row.war_coin,
            (int)row.alpaca_coin);
    }

    /// <summary>« Battle Merit » — natif `CGlobalInfo::GainWarCoin`
    /// @0x8081160/0x8081490 : `war_coin` borné [0, 9999999],
    /// retourné pour le 576 ClientUpdateWarCoin émis ensuite.</summary>
    public async Task<int> AddWarCoinAsync(int characterId, int amount)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET war_coin =
                LEAST(9999999, GREATEST(0, COALESCE(war_coin, 0)
                    + @Amount))
            WHERE id = @CharacterId
            RETURNING war_coin;";
        return await connection.ExecuteScalarAsync<int>(sql,
            new { Amount = amount, CharacterId = characterId });
    }

    /// <summary>« Alpaca Coins » — natif `CGlobalInfo::GainAlpacaCoin`
    /// @0x8081160 : `alpaca_coin` borné [0, 9999999], retourné pour
    /// le 580 ClientUpdateAlpacaCoin émis ensuite.</summary>
    public async Task<int> AddAlpacaCoinAsync(int characterId,
        int amount)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET alpaca_coin =
                LEAST(9999999, GREATEST(0, COALESCE(alpaca_coin, 0)
                    + @Amount))
            WHERE id = @CharacterId
            RETURNING alpaca_coin;";
        return await connection.ExecuteScalarAsync<int>(sql,
            new { Amount = amount, CharacterId = characterId });
    }

    /// <summary>GainSpecialCoin natif (`CDBAgent::GainSpecialCoin`
    /// @0x80863f0 mission) : `special_coins.coinNN` += amount clampé
    /// [0, 9999999]. <paramref name="coinType"/> = type wire 0..11
    /// → colonne coin{type+1:D2} (miroir de
    /// <see cref="GetSpecialCoinsAsync"/>). La ligne est créée si
    /// absente. Retourne le nouveau total.</summary>
    public async Task<int> AddSpecialCoinAsync(int characterId,
        int coinType, int amount)
    {
        var idx = Math.Clamp(coinType, 0, 11) + 1;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO special_coins (player_id) VALUES (@Id) " +
            "ON CONFLICT (player_id) DO NOTHING",
            new { Id = characterId });
        return await connection.ExecuteScalarAsync<int>(
            $"UPDATE special_coins SET coin{idx:D2} = " +
            $"GREATEST(0, LEAST(9999999, coin{idx:D2} + @a)) " +
            $"WHERE player_id = @Id RETURNING coin{idx:D2}",
            new { Id = characterId, a = amount });
    }

    public async Task UpdateCoordinatesAsync(int characterId, int nodeId, double x, double y, double z, double dir)
    {
        // Garde-fou : une position non finie ou hors carte (|coord| > 20k)
        // est une corruption de parse/protocole — on refuse de la persister
        // sinon le prochain spawn téléporte le client dans le vide (écran
        // blanc). Cas réel : MoveSelf mal aligné → -3e30 en DB.
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)
            || !double.IsFinite(dir)
            || Math.Abs(x) > 20_000 || Math.Abs(y) > 20_000 || Math.Abs(z) > 20_000)
            return;

        // Chaud (DB-3) : Redis, flush PG par CharacterWriteBehind (~15 s).
        if (Hot is not null)
        {
            Hot.SetPosition(characterId, new(nodeId, x, y, z, dir));
            return;
        }

        await UpdateCoordinatesPgAsync(characterId, nodeId, x, y, z, dir);
    }

    /// <summary>Écriture position DURABLE (PG direct, contourne Redis) — à
    /// utiliser quand un autre process lit <c>player_characters.node_id</c>
    /// juste après (Mission lors d'une migration de zone).</summary>
    public async Task UpdateCoordinatesDurableAsync(int characterId, int nodeId,
        double x, double y, double z, double dir)
    {
        if (!IsSaneCoords(x, y, z, dir)) return;
        await UpdateCoordinatesPgAsync(characterId, nodeId, x, y, z, dir);
        if (Hot is not null) await Hot.DropPositionAndVitalsAsync(characterId);
    }

    private static bool IsSaneCoords(double x, double y, double z, double dir)
        => double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
           && double.IsFinite(dir)
           && Math.Abs(x) <= 20_000 && Math.Abs(y) <= 20_000 && Math.Abs(z) <= 20_000;

    private async Task UpdateCoordinatesPgAsync(int characterId, int nodeId,
        double x, double y, double z, double dir)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            UPDATE player_characters
            SET node_id = @NodeId, x = @X, y = @Y, z = @Z, face_dir = @Dir
            WHERE id = @CharacterId;";

        await connection.ExecuteAsync(sql, new
        {
            NodeId = nodeId,
            X = x,
            Y = y,
            Z = z,
            Dir = dir,
            CharacterId = characterId
        });
    }

    /// <summary>Persiste `revive_area_id` (composite
    /// `(node&lt;&lt;16)|eventAreaId`) — équivalent de
    /// `SavePlayerCharacter("revive_area_id")` appelé par
    /// `CCharacter::SetReviveAreaID` natif @0x840bc70.</summary>
    public async Task SetReviveAreaIdAsync(int characterId, int reviveAreaId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET revive_area_id = @ReviveAreaId WHERE id = @CharacterId;";
        await connection.ExecuteAsync(sql,
            new { ReviveAreaId = reviveAreaId, CharacterId = characterId });
    }

    /// <summary>Persiste `last_normal_area_id` — dernière aire NORMALE
    /// traversée (sauvegarde de repli « logout in cave » du natif).</summary>
    public async Task SetLastNormalAreaIdAsync(int characterId, int areaId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET last_normal_area_id = @AreaId WHERE id = @CharacterId;";
        await connection.ExecuteAsync(sql,
            new { AreaId = areaId, CharacterId = characterId });
    }

    /// <summary>Persiste la visite de hall familial et le point de
    /// retour d'instance — `visit_family_id` (exec+0x114 → char+0xaac)
    /// + `cross_zone_node/x/y` (exec+0xf0/+0xf4/+0xf8 →
    /// char+0xa98/+0xa9c/+0xaa0) natifs. Un seul UPDATE, appelé depuis
    /// la sauvegarde complète du perso.</summary>
    public async Task SaveInstanceVisitAsync(int characterId,
        int visitFamilyId, int crossZoneNode, double crossZoneX,
        double crossZoneY)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            UPDATE player_characters
            SET visit_family_id = @VisitFamilyId,
                cross_zone_node = @CzNode,
                cross_zone_x = @CzX, cross_zone_y = @CzY
            WHERE id = @CharacterId;";
        await connection.ExecuteAsync(sql, new
        {
            VisitFamilyId = visitFamilyId, CzNode = crossZoneNode,
            CzX = crossZoneX, CzY = crossZoneY,
            CharacterId = characterId
        });
    }

    /// <summary>Positionne les points de classe/talents
    /// (skill_point) — commande GM gain_skill_point.</summary>
    public async Task SetSkillPointAsync(int characterId, int skillPoint)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = "UPDATE player_characters " +
            "SET skill_point = @Pts WHERE id = @CharacterId";
        await connection.ExecuteAsync(sql,
            new { Pts = skillPoint, CharacterId = characterId });
    }

    public async Task UpdateStatsAsync(int characterId, int hp, int mp, long exp, int level)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            UPDATE player_characters
            SET hp = @Hp, mp = @Mp, exp = @Exp, level = @Level
            WHERE id = @CharacterId;";

        await connection.ExecuteAsync(sql, new
        {
            Hp = hp,
            Mp = mp,
            Exp = exp,
            Level = level,
            CharacterId = characterId
        });
    }

    public async Task UpdateCharacterLocationAndStatsAsync(
        int characterId, int nodeId, double x, double y, double z, double dir, int hp, int mp)
    {
        if (Hot is not null)
        {
            if (IsSaneCoords(x, y, z, dir))
                Hot.SetPosition(characterId, new(nodeId, x, y, z, dir));
            Hot.SetVitals(characterId, new(hp, mp));
            return;
        }
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            UPDATE player_characters
            SET node_id = @NodeId, x = @X, y = @Y, z = @Z, face_dir = @Dir, hp = @Hp, mp = @Mp
            WHERE id = @CharacterId;";

        await connection.ExecuteAsync(sql, new
        {
            NodeId = nodeId,
            X = x,
            Y = y,
            Z = z,
            Dir = dir,
            Hp = hp,
            Mp = mp,
            CharacterId = characterId
        });
    }

    /// <summary>Sauvegarde complète de l'état runtime du perso —
    /// SaveCharacterLocation + vitals natifs réunis : node, position,
    /// direction, hp, mp, exp, level et class_id en UN UPDATE. Appelée à
    /// la déconnexion et par l'autosave périodique de la zone. L'or n'y
    /// figure PAS : il est déjà persisté immédiatement par SetGoldAsync
    /// à chaque transaction — le réécrire ici écraserait une valeur
    /// fraîche par une valeur périmée. Les coordonnées non
    /// finies/hors carte sont refusées (même garde que
    /// UpdateCoordinatesAsync) mais le reste est quand même écrit.</summary>
    public async Task SaveFullStateAsync(
        int characterId, int nodeId, double x, double y, double z,
        double dir, int hp, int mp, long exp, int level, int classId,
        long gold = -1, int classLv = -1, int classPoint = -1,
        int maxHp = -1, int maxMp = -1)
    {
        var posOk = double.IsFinite(x) && double.IsFinite(y)
            && double.IsFinite(z) && double.IsFinite(dir)
            && Math.Abs(x) <= 20_000 && Math.Abs(y) <= 20_000
            && Math.Abs(z) <= 20_000;

        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // gold/class_lv/class_point : -1 = ne pas écraser (appelants
        // qui ne les connaissent pas). Le natif les persiste dans le
        // même UPDATE (SavePlayerCharacter "class_lv class_point").
        // max_hp/max_mp : -1 = ne pas écraser — sinon le max lv1 figé
        // en base survit aux level-up (bug live : MP max resté à 69).
        var extra = (gold >= 0 ? ", gold = @Gold" : "") +
            (classLv >= 0 ? ", class_lv = @ClassLv" : "") +
            (classPoint >= 0 ? ", class_point = @ClassPoint" : "") +
            (maxHp > 0 ? ", max_hp = @MaxHp" : "") +
            (maxMp > 0 ? ", max_mp = @MaxMp" : "");
        var sqlPos = @"
            UPDATE player_characters
            SET node_id = @NodeId, x = @X, y = @Y, z = @Z, face_dir = @Dir,
                hp = @Hp, mp = @Mp, exp = @Exp, level = @Level,
                class_id = @ClassId, last_saving_time = @Now" +
            extra + " WHERE id = @CharacterId;";
        var sqlNoPos = @"
            UPDATE player_characters
            SET node_id = @NodeId, hp = @Hp, mp = @Mp, exp = @Exp,
                level = @Level, class_id = @ClassId, last_saving_time = @Now" +
            extra + " WHERE id = @CharacterId;";

        await connection.ExecuteAsync(posOk ? sqlPos : sqlNoPos, new
        {
            NodeId = nodeId,
            X = x, Y = y, Z = z, Dir = dir,
            Hp = hp, Mp = mp,
            Exp = exp, Level = level,
            ClassId = classId,
            Gold = Math.Max(gold, 0),
            ClassLv = Math.Max(classLv, 0),
            ClassPoint = Math.Max(classPoint, 0),
            MaxHp = Math.Max(maxHp, 0),
            MaxMp = Math.Max(maxMp, 0),
            Now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            CharacterId = characterId
        });
        // La sauvegarde complète supplante position/vitals sales en Redis.
        if (Hot is not null) await Hot.DropPositionAndVitalsAsync(characterId);
    }
}

public sealed partial class CharacterRepository
{
    /// <summary>Mot de passe de personnage —
    /// <c>player_characters.char_password</c> (NCID 30/31 natifs).</summary>
    public async Task<string?> GetCharPasswordAsync(int characterId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT char_password FROM player_characters " +
            "WHERE id = @characterId", new { characterId });
    }

    /// <summary>Définit/vide le mot de passe de personnage
    /// (<c>CCharacterDBO::SetCharPassword</c> natif).</summary>
    public async Task SetCharPasswordAsync(int characterId, string password)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE player_characters SET char_password = @password " +
            "WHERE id = @characterId", new { characterId, password });
    }

    /// <summary>Classement par exploit —
    /// <c>CGlobalInfo::AllExploitRank</c> natif : top 50 par
    /// <c>total_exploit</c> décroissant (rank, nom, niveau, famille).</summary>
    public async Task<IReadOnlyList<ExploitRankRow>> GetExploitRankAsync(int limit = 50)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<ExploitRankRow>(
            "SELECT c.given_name AS name, c.level, " +
            "COALESCE(f.name, '') AS family_name, c.total_exploit " +
            "FROM player_characters c " +
            "LEFT JOIN family f ON f.id = c.family_id " +
            "WHERE c.deleted_time = 0 AND c.total_exploit > 0 " +
            "ORDER BY c.total_exploit DESC LIMIT @limit",
            new { limit })).AsList();
    }

    /// <summary>Ligne du classement exploit.</summary>
    public sealed class ExploitRankRow
    {
        public string? name { get; set; }
        public int level { get; set; }
        public string? family_name { get; set; }
        public int total_exploit { get; set; }
    }

    /// <summary>Résumé LFG d'un perso —
    /// <c>SCharacter</c> natif : +0xc level, +0xe node_id,
    /// +0x10 race_id, +0x12 class_id (colonnes lues par
    /// <c>IsAccountHaveCharacter</c> @ 0x809fb50).</summary>
    public sealed class LfgSummaryRow
    {
        public int id { get; set; }
        public string? name { get; set; }
        public int level { get; set; }
        public int node_id { get; set; }
        public int race_id { get; set; }
        public int class_id { get; set; }
    }

    /// <summary>Résumé LFG d'un perso pour
    /// <c>CGlobalInfo::SetLFG</c>/<c>LoadLFGCharacters</c>.</summary>
    public async Task<LfgSummaryRow?> GetLfgSummaryAsync(int charId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<LfgSummaryRow>(
            "SELECT id, given_name AS name, level, node_id, race_id, " +
            "class_id FROM player_characters " +
            "WHERE id = @charId AND deleted_time = 0",
            new { charId });
    }
}
