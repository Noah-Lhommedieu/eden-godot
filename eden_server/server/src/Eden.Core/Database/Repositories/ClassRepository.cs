using Dapper;
using Npgsql;
using Eden.Core.Game.Classes;
using Eden.Core.Logging;

namespace Eden.Core.Database.Repositories;

public sealed class ClassRepository : IClassRepository
{
    private readonly NpgsqlDataSource _db;
    
    public ClassRepository(NpgsqlDataSource db)
    {
        _db = db;
    }
    
    public async Task<IReadOnlyList<ClassProgressEntity>> GetClassesByCharacterIdAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        // Table native player_classlist (pas player_classes — c'est
        // celle que remplit CreateCharacterNativeAsync et que lit le
        // client via le 489 : exp/point/aa_point sont ses colonnes).
        const string sql = @"
            SELECT player_id AS CharacterId, class AS ClassId,
                   level AS ClassLevel, exp AS ClassExp,
                   point AS ClassPoints, aa_point AS AaPoint,
                   style AS Style,
                   lic_1 AS Lic1, lic_2 AS Lic2, lic_3 AS Lic3,
                   lic_4 AS Lic4, lic_5 AS Lic5, lic_6 AS Lic6,
                   lic_7 AS Lic7
            FROM player_classlist
            WHERE player_id = @CharacterId";

        var results = await connection.QueryAsync<ClassLicRow>(sql, new { CharacterId = characterId });
        return results.Select(r => r.ToEntity()).ToList();
    }

    public async Task<ClassProgressEntity?> GetClassProgressAsync(int characterId, int classId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT player_id AS CharacterId, class AS ClassId,
                   level AS ClassLevel, exp AS ClassExp,
                   point AS ClassPoints, aa_point AS AaPoint,
                   style AS Style,
                   lic_1 AS Lic1, lic_2 AS Lic2, lic_3 AS Lic3,
                   lic_4 AS Lic4, lic_5 AS Lic5, lic_6 AS Lic6,
                   lic_7 AS Lic7
            FROM player_classlist
            WHERE player_id = @CharacterId AND class = @ClassId
            LIMIT 1";

        var row = await connection.QueryFirstOrDefaultAsync<ClassLicRow>(
            sql, new { CharacterId = characterId, ClassId = classId });
        return row?.ToEntity();
    }

    /// <summary>Spellmasters appris (player_spells) — émis un par un par
    /// le natif en 286 ClientGetSpell `[s16 spell][s16 spell][s16 level]
    /// [i32 CP]` (désasm SetSpellLevel @0x8156280 : `local_2c = param_4`
    /// nouveau niveau, `local_28 = char+0x488` CP restant) + 316 nom
    /// après un changement de classe (capture 17:22:35.926).
    /// spellmaster_id = l'id du sort en s16 signé (50017 → -15535).
    /// </summary>
    public async Task<IReadOnlyList<(short Spell, short Level)>>
        GetPlayerSpellsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // Le vec sorts du 311 porte le NIVEAU du spellmaster
        // (spell_level — capture : lv=1 sur les 23 entrées natives).
        // `spell_order` = position de barre de raccourcis, sans rapport
        // avec le niveau (désasm SavePlayerSpell : ordre = index dans
        // le vecteur spellmaster de la classe).
        var rows = await connection.QueryAsync<(short Sm, int Level)>(
            "SELECT spellmaster_id AS Sm, spell_level AS Level " +
            "FROM player_spells WHERE player_id = @Id",
            new { Id = characterId });
        return rows.Select(r => (r.Sm, (short)r.Level)).ToList();
    }

    public async Task<int> AddClassAsync(int characterId, int classId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // INSERT idempotent : la ligne peut déjà exister (le handler
        // ChangeClass ré-insère les classes de base jamais jouées).
        // aa_point=1 : valeur observée nativement sur les classes non
        // actives à la création.
        const string sql = @"
            INSERT INTO player_classlist (player_id, class, level, exp, point, aa_point)
            VALUES (@CharacterId, @ClassId, 1, 0, 0, 1)
            ON CONFLICT (player_id, class) DO NOTHING";

        return await connection.ExecuteAsync(sql,
            new { CharacterId = characterId, ClassId = classId });
    }
    
    public async Task<bool> ChangeClassAsync(int characterId, int newClassId)
    {
        // Vérifier que le joueur a cette classe
        var progress = await GetClassProgressAsync(characterId, newClassId);
        if (progress == null) return false;
        
        // Mise à jour du personnage principal
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        // Schéma natif : player_characters n'a PAS de colonne class_lv
        // (le niveau de classe vit dans player_classlist.level) — un
        // `SET class_lv` throwait et class_id n'était jamais persisté :
        // au prochain login le perso revenait à sa classe d'origine
        // (bug live : thief serveur mais warrior affiché + sorts de
        // classe filtrés hors de l'UI).
        const string sql = @"
            UPDATE player_characters
            SET class_id = @ClassId
            WHERE id = @CharacterId";

        var affected = await connection.ExecuteAsync(sql,
            new { ClassId = newClassId, CharacterId = characterId });

        // Miroir dénormalisé natif : player_characters.class_lv/
        // class_point reflètent la classe ACTIVE (le client les lit à
        // l'entrée et dans la sélection de personnage — GetData
        // "class_lv" → entrée +0x14). Sans ce miroir, changer de classe
        // laissait le niveau de l'ancienne classe affiché en sélection
        // jusqu'au prochain level-up (bug live).
        try
        {
            await connection.ExecuteAsync(
                "UPDATE player_characters SET class_lv=@Level, " +
                "class_point=@Point WHERE id=@CharacterId",
                new { CharacterId = characterId,
                      Level = progress.ClassLevel,
                      Point = progress.ClassPoints });
        }
        catch { /* schéma sans class_lv — voir SetClassProgressAsync */ }

        return affected > 0;
    }

    /// <summary>Fixe le niveau de classe dans player_classlist —
    /// commande GM clevel. (Pas de colonne de niveau de classe dans
    /// player_characters : le niveau vit par classe dans
    /// player_classlist.level.)</summary>
    public async Task SetClassLevelAsync(int characterId, int classId,
        int level)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_classlist SET level=@Level " +
            "WHERE player_id=@CharacterId AND class=@ClassId",
            new { CharacterId = characterId, ClassId = classId, Level = level });
        try
        {
            await connection.ExecuteAsync(
                "UPDATE player_characters SET class_lv=@Level " +
                "WHERE id=@CharacterId AND class_id=@ClassId",
                new { CharacterId = characterId, ClassId = classId,
                      Level = level });
        }
        catch { /* schéma sans class_lv — voir SetClassProgressAsync */ }
    }

    /// <summary>Écrit level+exp absolus d'une classe —
    /// `CCharacterDBO::SavePlayerCharacter(...,"class_lv class_point")`
    /// du natif : UPDATE player_classlist SET level, exp + miroir de
    /// `player_characters.class_lv` pour la classe active (le client
    /// lit la copie dénormalisée à l'entrée — LoadPlayerCharacter
    /// +0x14 ; sans elle un level-up de classe affichait lv1 au relog
    /// alors que player_classlist.level persistait — bug live).
    /// Le miroir est toléré en échec : schémas sans la colonne
    /// class_lv (historique) ne doivent pas casser le level-up.</summary>
    public async Task SetClassProgressAsync(int characterId, int classId,
        int level, long exp)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_classlist SET level=@Level, exp=@Exp " +
            "WHERE player_id=@CharacterId AND class=@ClassId",
            new { CharacterId = characterId, ClassId = classId,
                  Level = level, Exp = exp });
        try
        {
            await connection.ExecuteAsync(
                "UPDATE player_characters SET class_lv=@Level " +
                "WHERE id=@CharacterId AND class_id=@ClassId",
                new { CharacterId = characterId, ClassId = classId,
                      Level = level });
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[ClassRepo] miroir class_lv ignoré : {ex.Message}");
        }
    }

    public async Task AddClassExpAsync(int characterId, int classId, long expGain)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE player_classlist
            SET exp = exp + @ExpGain
            WHERE player_id = @CharacterId AND class = @ClassId";
        
        await connection.ExecuteAsync(sql, 
            new { CharacterId = characterId, ClassId = classId, ExpGain = expGain });
    }
    
    /// <summary>AA point — `GainAAPoint(this,1)` du level-up de classe
    /// natif @0x840c7f0 : le pool de la classe COURANTE vit dans
    /// <c>player_characters.aa_point</c> (char+0xa1c), borné [0,120]
    /// (iVar3 = min(max(v,0),0x78)) — pas dans player_classlist. La
    /// ligne classlist de la classe active est un miroir
    /// resynchronisé au switch (PlayerFactory @0x8104xxx : la nouvelle
    /// classe recharge son pool depuis sa propre ligne). Retourne le
    /// nouveau total (-1 si perso absent) — le natif émet
    /// ClientUpdateAttr attr=0x31 avec la valeur absolue.</summary>
    public async Task<int> AddAaPointAsync(int characterId, int points)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var total = await connection.ExecuteScalarAsync<int?>(
            "UPDATE player_characters " +
            "SET aa_point = LEAST(GREATEST(aa_point + @P, 0), 120) " +
            "WHERE id=@CharacterId RETURNING aa_point",
            new { CharacterId = characterId, P = points });
        if (total == null) return -1;
        // Miroir : player_classlist[classe active].aa_point reflète le
        // pool — le 489 ClientPlayerClassList le réémet par classe.
        await connection.ExecuteAsync(
            "UPDATE player_classlist pc SET aa_point=@T " +
            "FROM player_characters p " +
            "WHERE pc.player_id = p.id AND p.id=@CharacterId " +
            "AND pc.class = p.class_id",
            new { CharacterId = characterId, T = total.Value });
        return total.Value;
    }

    /// <summary>Dépense atomique sur le pool d'une AUTRE classe —
    /// AddAAEffectPoint natif param_2!=0 : la ligne player_classlist
    /// de la classe ciblée porte son propre pool (SCharacterClass+0xc,
    /// map char+0xad8). Retourne le solde restant, -1 si insuffisant.
    /// </summary>
    public async Task<int> TrySpendAaPointAsync(int characterId,
        int classId, int cost)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.ExecuteScalarAsync<int?>(
            "UPDATE player_classlist SET aa_point = aa_point - @Cost " +
            "WHERE player_id=@CharacterId AND class=@ClassId " +
            "AND aa_point >= @Cost RETURNING aa_point",
            new { CharacterId = characterId, ClassId = classId,
                  Cost = cost });
        if (row == null) return -1;
        // Miroir : quand la classe ciblée EST la classe active, le
        // natif lit le MÊME pool (SCharacterClass+0xc de la classe
        // courante = char+0xa1c reflété). Sans cette synchro les deux
        // pools divergeaient : classlist[active] tombait à 0 pendant
        // que characters.aa_point (affiché via attr 0x31) restait
        // haut — talents refusés côté serveur alors que le client
        // affichait des points disponibles (Sacred Duty live).
        await connection.ExecuteAsync(
            "UPDATE player_characters SET aa_point=@R " +
            "WHERE id=@CharacterId AND class_id=@ClassId",
            new { CharacterId = characterId, R = row.Value,
                  ClassId = classId });
        return row.Value;
    }

    /// <summary>Dépense atomique sur le pool de la classe COURANTE —
    /// AddAAEffectPoint natif param_2==0 puise dans char+0xa1c =
    /// <c>player_characters.aa_point</c> (@0x841ebb0 :
    /// local_70=*(this+0xa1c), *(this+0xa1c)=local_70-cout). Retourne
    /// le solde restant, -1 si insuffisant.</summary>
    public async Task<int> TrySpendCurrentAaPointAsync(int characterId,
        int cost)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rest = await connection.ExecuteScalarAsync<int?>(
            "UPDATE player_characters SET aa_point = aa_point - @Cost " +
            "WHERE id=@CharacterId AND aa_point >= @Cost " +
            "RETURNING aa_point",
            new { CharacterId = characterId, Cost = cost });
        if (rest == null) return -1;
        // Miroir identique à AddAaPointAsync — la ligne classlist de
        // la classe active suit le pool.
        await connection.ExecuteAsync(
            "UPDATE player_classlist pc SET aa_point=@R " +
            "FROM player_characters p " +
            "WHERE pc.player_id = p.id AND p.id=@CharacterId " +
            "AND pc.class = p.class_id",
            new { CharacterId = characterId, R = rest.Value });
        return rest.Value;
    }

    /// <summary>Bascule du pool aa_point au changement de classe —
    /// switch natif PlayerFactory @0x8104xxx : le pool courant
    /// (char+0xa1c) est rangé dans SCharacterClass+0xc de l'ANCIENNE
    /// classe, puis rechargé depuis celle de la nouvelle. À appeler
    /// APRÈS l'UPDATE class_id (class_id pointe déjà la nouvelle
    /// classe). Retourne le pool de la nouvelle classe (-1 si
    /// absent).</summary>
    public async Task<int> SwapAaPoolOnClassChangeAsync(int characterId,
        int oldClassId, int newClassId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // Range le pool courant dans la ligne de l'ancienne classe
        // (pCVar15[0xc] = char+0xa1c natif).
        await connection.ExecuteAsync(
            "UPDATE player_classlist pc SET aa_point = p.aa_point " +
            "FROM player_characters p " +
            "WHERE pc.player_id = p.id AND p.id=@CharacterId " +
            "AND pc.class = @Old",
            new { CharacterId = characterId, Old = oldClassId });
        // Recharge le pool de la nouvelle classe
        // (char+0xa1c = pCVar15[0xc] natif).
        var pool = await connection.ExecuteScalarAsync<int?>(
            "UPDATE player_characters SET aa_point = COALESCE(" +
            "(SELECT aa_point FROM player_classlist " +
            "WHERE player_id=@CharacterId AND class=@New), 0) " +
            "WHERE id=@CharacterId RETURNING aa_point",
            new { CharacterId = characterId, New = newClassId });
        return pool ?? -1;
    }

    /// <summary>+N points de compétence de classe (`GainNp` natif
    /// @0x840bac0 — cap 99999999, save "skill_point"). Retourne le
    /// nouveau total : le natif émet ClientUpdateAttr attr=0x3ef avec
    /// la valeur ABSOLUE (this+0x488), pas le delta. Miroir
    /// `player_characters.class_point` pour la classe active — même
    /// raison que class_lv : le client relit la copie dénormalisée au
    /// login.</summary>
    public async Task<int> AddClassPointsAsync(int characterId,
        int classId, int points)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var total = await connection.ExecuteScalarAsync<int>(
            "UPDATE player_classlist " +
            "SET point = LEAST(point + @Points, 99999999) " +
            "WHERE player_id = @CharacterId AND class = @ClassId " +
            "RETURNING point",
            new { CharacterId = characterId, ClassId = classId,
                  Points = points });
        try
        {
            await connection.ExecuteAsync(
                "UPDATE player_characters " +
                "SET class_point = class_point + @Points " +
                "WHERE id=@CharacterId AND class_id=@ClassId",
                new { CharacterId = characterId, ClassId = classId,
                      Points = points });
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[ClassRepo] miroir class_point ignoré : {ex.Message}");
        }
        return total;
    }

    /// <summary>LearnSpell — SQL natif GetSpellMasterInsert/UpdateSQLCmd
    /// (désasm @0x863a790/0x863a7d0) : déjà appris → spell_level++ ;
    /// nouveau → spell_order = nb de spellmasters appris + 1.</summary>
    public async Task<int> LearnSpellAsync(int characterId,
        short spellmasterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var cur = await connection.ExecuteScalarAsync<int?>(
            "SELECT spell_level FROM player_spells " +
            "WHERE player_id=@C AND spellmaster_id=@S",
            new { C = characterId, S = spellmasterId });
        if (cur != null)
        {
            var next = cur.Value + 1;
            await connection.ExecuteAsync(
                "UPDATE player_spells SET spell_level=@L " +
                "WHERE player_id=@C AND spellmaster_id=@S",
                new { L = next, C = characterId, S = spellmasterId });
            return next;
        }
        var order = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM player_spells WHERE player_id=@C",
            new { C = characterId }) + 1;
        await connection.ExecuteAsync(
            "INSERT INTO player_spells " +
            "(player_id, spellmaster_id, spell_order, spell_level) " +
            "VALUES (@C, @S, @O, 1)",
            new { C = characterId, S = spellmasterId, O = order });
        return 1;
    }

    /// <inheritdoc/>
    public async Task LearnSpellsAsync(int characterId,
        IReadOnlyList<short> spellmasterIds)
    {
        if (spellmasterIds.Count == 0) return;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // Déjà appris → même incrément que LearnSpellAsync
        // (GetSpellMasterUpdateSQLCmd : spell_level + 1), groupé.
        var known = (await connection.QueryAsync<short>(
            "SELECT spellmaster_id FROM player_spells " +
            "WHERE player_id=@C AND spellmaster_id = ANY(@Ids)",
            new { C = characterId, Ids = spellmasterIds.ToArray() }))
            .ToHashSet();
        if (known.Count > 0)
            await connection.ExecuteAsync(
                "UPDATE player_spells SET spell_level = spell_level + 1 " +
                "WHERE player_id=@C AND spellmaster_id = ANY(@Ids)",
                new { C = characterId, Ids = known.ToArray() });
        var fresh = spellmasterIds.Where(s => !known.Contains(s))
            .ToArray();
        if (fresh.Length == 0) return;
        var order0 = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM player_spells WHERE player_id=@C",
            new { C = characterId });
        // INSERT multi-lignes : spell_order = nb appris + rang
        // (WITH ORDINALITY est 1-based — convention « count + 1 »
        // de GetSpellMasterInsertSQLCmd préservée par sort).
        await connection.ExecuteAsync(
            "INSERT INTO player_spells " +
            "(player_id, spellmaster_id, spell_order, spell_level) " +
            "SELECT @C, s, @O + ord, 1 FROM unnest(@Ids) " +
            "WITH ORDINALITY AS u(s, ord)",
            new { C = characterId, Ids = fresh, O = order0 });
    }

    /// <inheritdoc/>
    public async Task SetSpellLevelAsync(int characterId,
        short spellmasterId, short level)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_spells SET spell_level=@L " +
            "WHERE player_id=@C AND spellmaster_id=@S",
            new { L = level, C = characterId, S = spellmasterId });
    }

    /// <summary>Réécrit le layout de certificats d'une classe —
    /// `SetOtherClassLicense` natif (@0x82717f0) : lic_1..lic_7 =
    /// les 7 slots émis dans le 489. Le natif remplit tous les
    /// slots (0 = vide).</summary>
    public async Task SetClassLicensesAsync(int characterId,
        int classId, int[] licenses)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_classlist SET lic_1=@L1, lic_2=@L2, " +
            "lic_3=@L3, lic_4=@L4, lic_5=@L5, lic_6=@L6, lic_7=@L7 " +
            "WHERE player_id=@C AND class=@Cls",
            new { C = characterId, Cls = classId,
                  L1 = licenses.ElementAtOrDefault(0),
                  L2 = licenses.ElementAtOrDefault(1),
                  L3 = licenses.ElementAtOrDefault(2),
                  L4 = licenses.ElementAtOrDefault(3),
                  L5 = licenses.ElementAtOrDefault(4),
                  L6 = licenses.ElementAtOrDefault(5),
                  L7 = licenses.ElementAtOrDefault(6) });
    }

    /// <summary>DTO Dapper : les 7 colonnes lic_N séparées ne mappent
    /// <summary>Garde-robe du perso — table native
    /// <c>player_closet(player_id, class, index, helmet_color,
    /// clothes_color, cloak_color)</c>, lue par
    /// <c>CExecutionerDBO::LoadCloset</c> à l'entrée : hashtable
    /// class → vector&lt;SCharacterCloset&gt; ordonné par index.</summary>
    public async Task<SortedDictionary<byte, List<(ulong Helmet,
        ulong Clothes, ulong Cloak)>>> GetClosetsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<(int Class, int Index,
                long Helmet, long Clothes, long Cloak)>(
            "SELECT class AS Class, index AS Index, " +
            "helmet_color AS Helmet, clothes_color AS Clothes, " +
            "cloak_color AS Cloak FROM player_closet " +
            "WHERE player_id = @Id ORDER BY class, index",
            new { Id = characterId });
        var map = new SortedDictionary<byte,
            List<(ulong Helmet, ulong Clothes, ulong Cloak)>>();
        foreach (var r in rows)
        {
            // Les slots sont positionnels (l'index est la position du
            // vector natif) : on comble les trous avec des entrées
            // nulles pour conserver l'indexation du client.
            if (!map.TryGetValue((byte)r.Class, out var list))
                list = map[(byte)r.Class] = new();
            while (list.Count <= r.Index)
                list.Add((0UL, 0UL, 0UL));
            list[r.Index] = ((ulong)r.Helmet, (ulong)r.Clothes,
                (ulong)r.Cloak);
        }
        return map;
    }

    /// <summary>Sélection de costume — <c>SavePlayerCharacter("style
    /// helmet_color clothes_color cloak_color")</c> + mise à jour du
    /// champ style de <c>player_classlist</c> (SCharacterClass+0x1d)
    /// pour la classe active, en une transaction.</summary>
    public async Task SaveClosetSelectionAsync(int characterId,
        int classId, int closetIndex, ulong helmet, ulong clothes,
        ulong cloak)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();
        await connection.ExecuteAsync(
            "UPDATE player_characters SET style=@Idx, " +
            "helmet_color=@Helmet, clothes_color=@Clothes, " +
            "cloak_color=@Cloak WHERE id=@Id",
            new { Id = characterId, Idx = closetIndex,
                  Helmet = (long)helmet, Clothes = (long)clothes,
                  Cloak = (long)cloak }, tx);
        await connection.ExecuteAsync(
            "UPDATE player_classlist SET style=@Idx " +
            "WHERE player_id=@Id AND class=@Class",
            new { Id = characterId, Class = classId, Idx = closetIndex },
            tx);
        await tx.CommitAsync();
    }

    /// <summary>Enregistre le slot <c>index</c> de la classe —
    /// DELETE+INSERT natif de <c>TC_UpdateCloset</c>
    /// (GetClosetDeleteSQLCmd/GetClosetInsertSQLCmd).</summary>
    public async Task UpsertClosetSlotAsync(int characterId,
        int classId, int index, ulong helmet, ulong clothes,
        ulong cloak)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();
        await connection.ExecuteAsync(
            "DELETE FROM player_closet WHERE player_id=@Id " +
            "AND class=@Class AND index=@Index",
            new { Id = characterId, Class = classId, Index = index },
            tx);
        await connection.ExecuteAsync(
            "INSERT INTO player_closet (player_id, class, index, " +
            "helmet_color, clothes_color, cloak_color) VALUES " +
            "(@Id, @Class, @Index, @Helmet, @Clothes, @Cloak)",
            new { Id = characterId, Class = classId, Index = index,
                  Helmet = (long)helmet, Clothes = (long)clothes,
                  Cloak = (long)cloak }, tx);
        await tx.CommitAsync();
    }

    /// <summary>DTO Dapper : les 7 colonnes lic_N séparées ne mappent
    /// pas directement vers le tableau <c>Licenses</c> de l'entité —
    /// la conversion se fait ici.</summary>
    private sealed class ClassLicRow
    {
        public int CharacterId { get; set; }
        public int ClassId { get; set; }
        public int ClassLevel { get; set; }
        public long ClassExp { get; set; }
        public int ClassPoints { get; set; }
        public int AaPoint { get; set; }
        public int Style { get; set; }
        public int Lic1 { get; set; }
        public int Lic2 { get; set; }
        public int Lic3 { get; set; }
        public int Lic4 { get; set; }
        public int Lic5 { get; set; }
        public int Lic6 { get; set; }
        public int Lic7 { get; set; }

        public ClassProgressEntity ToEntity() => new()
        {
            CharacterId = CharacterId, ClassId = ClassId,
            ClassLevel = ClassLevel, ClassExp = ClassExp,
            ClassPoints = ClassPoints, AaPoint = AaPoint,
            Style = Style,
            Licenses = new[]
            { Lic1, Lic2, Lic3, Lic4, Lic5, Lic6, Lic7 },
        };
    }
}
