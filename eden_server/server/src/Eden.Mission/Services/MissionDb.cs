using Dapper;
using Npgsql;

namespace Eden.Mission.Services;

/// <summary>
/// Accès DB du MissionServer C# — tables natives edenfr_db :
///   personal_mission_states(id=charUid, index=missionId, state)
///     → quêtes actives ; state = encodage C# (status | progress&lt;&lt;8).
///   player_mission_complete_num(player_id, mission_id, complete_num)
///     → quêtes TERMINÉES (If_FMS mid done les lit).
///   player_characters → level/gender (conditions), gold/exp (récompenses).
///   player_mail → opérations courrier DBA (live schema receiver_id…).
/// </summary>
public sealed class MissionDb
{
    private readonly NpgsqlDataSource _db;

    public MissionDb(NpgsqlDataSource db) => _db = db;

    private NpgsqlConnection Open() => _db.CreateConnection();
    /// <summary>Source Npgsql sous-jacente — pour les repositories
    /// partagés (battlefield) construits au même branchement.</summary>
    public NpgsqlDataSource DataSource => _db;

    // -------------------------------------------------------- quêtes --

    /// <summary>Charge les quêtes actives d'un perso :
    /// index=missionId (&lt; 1 000 000), state=packed
    /// (status | progress&lt;&lt;8). Les lignes index ≥ 1 000 000 sont
    /// des compteurs PMS natifs — voir LoadPmsAsync.</summary>
    public async Task<Dictionary<int, int>> LoadActiveAsync(int charUid)
    {
        const string sql = "SELECT index, state FROM personal_mission_states " +
            "WHERE id = @id AND index < 1000000";
        await using var c = Open();
        var rows = await c.QueryAsync<(int index, int state)>(sql, new { id = charUid });
        return rows.ToDictionary(r => r.index, r => r.state);
    }

    /// <summary>Compteurs de complétions par quête (mid → complete_num)
    /// — miroir de la map native exec+0x45c remplie par
    /// LoadAllMissionCompleteNum @0x08190d00. N'alimente PAS l'état
    /// « terminée » : le natif n'écrit complete_num que pour les quêtes
    /// à limite (col4&gt;0), jamais pour les quêtes ordinaires.</summary>
    public async Task<Dictionary<int, int>> LoadCompletionCountsAsync(int charUid)
    {
        const string sql = "SELECT mission_id, complete_num FROM " +
            "player_mission_complete_num WHERE player_id = @id";
        await using var c = Open();
        var rows = await c.QueryAsync<(int mission_id, int complete_num)>(
            sql, new { id = charUid });
        return rows.ToDictionary(r => r.mission_id, r => r.complete_num);
    }

    /// <summary>Fixe complete_num — équivalent de l'UPSERT natif
    /// AddMissionCompleteNum @0x08190ef0 (quêtes limitées bit2-set qui
    /// conservent la row jusqu'à la limite).</summary>
    public async Task SetCompleteNumAsync(int charUid, int missionId, int num)
    {
        const string sql = @"
            INSERT INTO player_mission_complete_num
                (player_id, mission_id, complete_num)
            VALUES (@id, @mid, @n)
            ON CONFLICT (player_id, mission_id)
            DO UPDATE SET complete_num = @n";
        await using var c = Open();
        await c.ExecuteAsync(sql, new { id = charUid, mid = missionId, n = num });
    }

    /// <summary>Supprime la row — DeleteMissionCompleteNum natif
    /// @0x0818af60 (quête limitée arrivée à sa limite, ou complétion
    /// d'une limitée bit2-clair où la row est transitoire).</summary>
    public async Task DeleteCompleteNumAsync(int charUid, int missionId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM player_mission_complete_num " +
            "WHERE player_id = @id AND mission_id = @mid",
            new { id = charUid, mid = missionId });
    }

    /// <summary>Compteurs PMS natifs d'un perso :
    /// index = clé PMS (mid×1 000 000 + type×10 000 + objet) —
    /// désasm `CMissionList::GetXxxPMS` — state = compteur. C'est ces
    /// lignes que le client lit pour afficher les « n/requis » du HUD
    /// (UpdateMissionState kind=1, IncPMS @0x80969a0).</summary>
    public async Task<Dictionary<int, int>> LoadPmsAsync(int charUid)
    {
        const string sql = "SELECT index, state FROM personal_mission_states " +
            "WHERE id = @id AND index >= 1000000";
        await using var c = Open();
        var rows = await c.QueryAsync<(int index, int state)>(sql, new { id = charUid });
        return rows.ToDictionary(r => r.index, r => r.state);
    }

    /// <summary>Upsert d'un compteur PMS (natif SavePMS :
    /// INSERT personal_mission_states(id, index=pmsKey, state)).</summary>
    public async Task SavePmsAsync(int charUid, int pmsKey, int count)
        => await SaveActiveAsync(charUid, pmsKey, count);

    /// <summary>Supprime les compteurs PMS d'une mission (achèvement /
    /// abandon — RemovePMS natif purge les clés de la mission).</summary>
    public async Task DeletePmsAsync(int charUid, int missionId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM personal_mission_states WHERE id=@id AND " +
            "index >= @lo AND index < @hi",
            new { id = charUid, lo = missionId * 1000000,
                  hi = (missionId + 1) * 1000000 });
    }

    /// <summary>Compte d'un item détenu par le perso (sacs +
    /// équipement porté — la condition ItemCollect natif compte
    /// l'inventaire entier via CheckItemInInventory). Sémantique
    /// `IItemContainer::CheckItemNum` @0x84e8260 : item stackable
    /// (flag CItemData+0xf0 bit0 ⇔ S_Item MaxStack>1) → on soustrait
    /// `clone+0xc` = la colonne `durability` (count) ; sinon 1 par
    /// ligne. Sans ça un stack de N ne comptait que 1.</summary>
    /// <param name="stackable">`ItemDef.MaxStack &gt; 1`.</param>
    public async Task<int> CountItemAsync(int charUid, int itemId,
        bool stackable)
    {
        await using var c = Open();
        // `inventory` = vue native inventory1 ∪ inventory2 (sacs).
        var inv = await c.ExecuteScalarAsync<int>(
            stackable
                ? "SELECT COALESCE(SUM(GREATEST(durability,1)),0) " +
                  "FROM inventory WHERE player_id=@id AND item_id=@iid"
                : "SELECT count(*) FROM inventory " +
                  "WHERE player_id=@id AND item_id=@iid",
            new { id = charUid, iid = itemId });
        var eq = await c.ExecuteScalarAsync<int>(
            stackable
                ? "SELECT COALESCE(SUM(GREATEST(durability,1)),0) " +
                  "FROM equipment1 WHERE player_id=@id AND item_id=@iid"
                : "SELECT count(*) FROM equipment1 " +
                  "WHERE player_id=@id AND item_id=@iid",
            new { id = charUid, iid = itemId });
        return inv + eq;
    }

    /// <summary>Upsert d'un état de quête active.</summary>
    public async Task SaveActiveAsync(int charUid, int missionId, int state)
    {
        const string sql = @"
            INSERT INTO personal_mission_states (id, index, state)
            VALUES (@id, @mid, @st)
            ON CONFLICT (id, index) DO UPDATE SET state = @st";
        await using var c = Open();
        try { await c.ExecuteAsync(sql, new { id = charUid, mid = missionId, st = state }); }
        catch (PostgresException)
        {
            // Pas de contrainte unique sur (id,index) dans le schéma live :
            // retombe sur delete+insert.
            await c.ExecuteAsync(
                "DELETE FROM personal_mission_states WHERE id=@id AND index=@mid",
                new { id = charUid, mid = missionId });
            await c.ExecuteAsync(
                "INSERT INTO personal_mission_states (id,index,state) VALUES (@id,@mid,@st)",
                new { id = charUid, mid = missionId, st = state });
        }
    }

    /// <summary>Supprime un état actif (abandon ou achèvement).</summary>
    public async Task DeleteActiveAsync(int charUid, int missionId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM personal_mission_states WHERE id=@id AND index=@mid",
            new { id = charUid, mid = missionId });
    }

    /// <summary>FMS natifs (final_mission_state_v2 = vue UNION des
    /// buffers de save) : index=(mid-1)/10, k=(mid-1)%10,
    /// état=(state&gt;&gt;(k*3))&amp;7. Sémantique (SetFMS @0x8095ad0) :
    /// 1=terminée, 3=en cours, 4=objectifs remplis, 5=checked,
    /// 6=reprenable.</summary>
    public async Task<Dictionary<int, int>> LoadFMSAsync(int charUid)
    {
        const string sql = "SELECT index, state FROM final_mission_state_v2 " +
            "WHERE player_id = @id";
        await using var c = Open();
        var rows = await c.QueryAsync<(int index, int state)>(sql,
            new { id = charUid });
        var fms = new Dictionary<int, int>();
        foreach (var r in rows)
            for (var k = 0; k < 10; k++)
            {
                var v = (int)((uint)r.state >> (k * 3)) & 7;
                if (v != 0) fms[r.index * 10 + 1 + k] = v;
            }
        return fms;
    }

    /// <summary>Écrit un FMS dans le buffer actif (state1) —
    /// read-modify-write des 30 bits de la ligne index=(mid-1)/10.</summary>
    public async Task SetFMSAsync(int charUid, int mid, int fms)
    {
        var index = (mid - 1) / 10;
        var k = (mid - 1) % 10;
        await using var c = Open();
        var old = await c.ExecuteScalarAsync<long?>(
            "SELECT state FROM final_mission_state1 " +
            "WHERE player_id=@id AND index=@idx",
            new { id = charUid, idx = index }) ?? 0;
        var mask = 7L << (k * 3);
        var next = (old & ~mask) | ((long)fms << (k * 3));
        await c.ExecuteAsync(
            "INSERT INTO final_mission_state1 (player_id, index, state) " +
            "VALUES (@id, @idx, @st) " +
            "ON CONFLICT (player_id, index) DO UPDATE SET state=@st",
            new { id = charUid, idx = index, st = (int)next });
    }

    /// <summary>Place mid dans le premier slot libre de mission_trace
    /// (HUD tracker droit) — no-op si déjà tracké ou aucun slot libre.</summary>
    public async Task SetTraceAsync(int charUid, int mid)
    {
        await using var c = Open();
        var row = await c.QueryFirstOrDefaultAsync<int[]>(
            "SELECT ARRAY[mission_trace01,mission_trace02,mission_trace03," +
            "mission_trace04,mission_trace05,mission_trace06," +
            "mission_trace07,mission_trace08,mission_trace09," +
            "mission_trace10] FROM mission_trace WHERE id=@id",
            new { id = charUid });
        var slots = row ?? new int[10];
        if (slots.Contains(mid)) return;
        var free = Array.FindIndex(slots, s => s == 0);
        if (free < 0) return;
        if (row == null)
            await c.ExecuteAsync(
                "INSERT INTO mission_trace (id, mission_trace01, " +
                "mission_trace02, mission_trace03, mission_trace04, " +
                "mission_trace05, mission_trace06, mission_trace07, " +
                "mission_trace08, mission_trace09, mission_trace10) " +
                "VALUES (@id, @mid, 0,0,0,0,0,0,0,0,0)",
                new { id = charUid, mid });
        else
            await c.ExecuteAsync(
                $"UPDATE mission_trace SET mission_trace{free + 1:D2}=@mid " +
                "WHERE id=@id", new { id = charUid, mid });
    }

    /// <summary>SetMissionTrace natif : remplace les 10 slots par la liste
    /// envoyée par le client (TC_SetMissionTrace → UPDATE mission_trace
    /// SET trace01..10 WHERE id ; INSERT d'une ligne à zéro si absente,
    /// comme le natif au chargement).</summary>
    public async Task SetMissionTraceAsync(int charUid, int[] ids)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "INSERT INTO mission_trace (id, mission_trace01, " +
            "mission_trace02, mission_trace03, mission_trace04, " +
            "mission_trace05, mission_trace06, mission_trace07, " +
            "mission_trace08, mission_trace09, mission_trace10) " +
            "VALUES (@id, 0,0,0,0,0,0,0,0,0,0) ON CONFLICT (id) DO NOTHING",
            new { id = charUid });
        var set = string.Join(", ",
            Enumerable.Range(0, 10)
                .Select(i => $"mission_trace{i + 1:D2} = @t{i}"));
        var prm = new DynamicParameters();
        prm.Add("id", charUid);
        for (var i = 0; i < 10; i++)
            prm.Add($"t{i}", i < ids.Length ? ids[i] : 0);
        await c.ExecuteAsync(
            $"UPDATE mission_trace SET {set} WHERE id = @id", prm);
    }

    /// <summary>Retire mid des slots de mission_trace (abandon/fin).</summary>
    public async Task ClearTraceAsync(int charUid, int mid)
    {
        await using var c = Open();
        for (var i = 1; i <= 10; i++)
            await c.ExecuteAsync(
                $"UPDATE mission_trace SET mission_trace{i:D2}=0 " +
                "WHERE id=@id AND " +
                $"mission_trace{i:D2}=@mid", new { id = charUid, mid });
    }

    /// <summary>Marque une quête terminée — complete_num++ (quêtes
    /// répétables incrémentent, conforme au nom de la table).</summary>
    public async Task MarkCompletedAsync(int charUid, int missionId)
    {
        const string sql = @"
            INSERT INTO player_mission_complete_num
                (player_id, mission_id, complete_num)
            VALUES (@id, @mid, 1)
            ON CONFLICT (player_id, mission_id)
            DO UPDATE SET complete_num =
                player_mission_complete_num.complete_num + 1";
        await using var c = Open();
        try { await c.ExecuteAsync(sql, new { id = charUid, mid = missionId }); }
        catch (PostgresException)
        {
            await c.ExecuteAsync(
                "UPDATE player_mission_complete_num SET complete_num=complete_num+1 " +
                "WHERE player_id=@id AND mission_id=@mid",
                new { id = charUid, mid = missionId });
            var n = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM player_mission_complete_num " +
                "WHERE player_id=@id AND mission_id=@mid",
                new { id = charUid, mid = missionId });
            if (n == 0)
                await c.ExecuteAsync(
                    "INSERT INTO player_mission_complete_num " +
                    "(player_id,mission_id,complete_num) VALUES (@id,@mid,1)",
                    new { id = charUid, mid = missionId });
        }
    }

    /// <summary>Niveau + genre + node du perso (conditions
    /// If_CharLevel / If_Gender et AutoAcceptByNode — le natif lit le
    /// node en DB à MigrationDone, pas dans le paquet).</summary>
    public async Task<(int Level, int Gender, int Node)> GetLevelGenderNodeAsync(int charUid)
    {
        await using var c = Open();
        var row = await c.QuerySingleOrDefaultAsync<(int level, int gender_id, int node_id)>(
            "SELECT level, gender_id, node_id FROM player_characters WHERE id = @id",
            new { id = charUid });
        return (row.level, row.gender_id, row.node_id);
    }

    /// <summary>Nom du perso (textes de partage de quête « %s »).</summary>
    public async Task<string> GetCharNameAsync(int charUid)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<string>(
            "SELECT COALESCE(given_name,'') FROM player_characters WHERE id = @id",
            new { id = charUid }) ?? "";
    }

    /// <summary>Or du perso (condition `If_Gold >= n` des dialogues).</summary>
    public async Task<long> GetGoldAsync(int charUid)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<long>(
            "SELECT COALESCE(gold,0) FROM player_characters WHERE id = @id",
            new { id = charUid });
    }

    /// <summary>Classe courante du perso (`If_Class n` des dialogues).</summary>
    public async Task<int> GetClassIdAsync(int charUid)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(class_id,0) FROM player_characters WHERE id = @id",
            new { id = charUid });
    }

    /// <summary>Exp de prestige courante (`If_Reputation id >= n`).</summary>
    public async Task<int> GetPrestigeAsync(int charUid, int prestigeId)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(exp,0) FROM prestige WHERE player_id = @id " +
            "AND prestige_id = @pid", new { id = charUid, pid = prestigeId });
    }

    /// <summary>Slots libres du sac 0 — natif `If_InventorySlot`
    /// (@0x80dbc70) : `GetContainerTotalSize(0) − slots occupés ≥ n`.
    /// Le sac natif compte 64 slots (generate_series du repo inventaire).</summary>
    public async Task<int> CountFreeSlotsAsync(int charUid)
    {
        await using var c = Open();
        var used = await c.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM inventory WHERE player_id = @id " +
            "AND container_index = 0", new { id = charUid });
        return Math.Max(0, 64 - used);
    }

    /// <summary>Taille de chaque pile (colonne durability, min 1) du
    /// sac 0 pour les items demandés — sert à simuler la remise de
    /// quête (fusion dans les piles partielles).</summary>
    public async Task<Dictionary<int, List<int>>> GetBagStacksAsync(
        int charUid, IReadOnlyCollection<int> itemIds)
    {
        var res = new Dictionary<int, List<int>>();
        if (itemIds.Count == 0) return res;
        await using var c = Open();
        var rows = await c.QueryAsync<(int ItemId, int Cnt)>(
            "SELECT item_id AS ItemId, GREATEST(durability,1) AS Cnt " +
            "FROM inventory WHERE player_id = @id " +
            "AND container_index = 0 AND item_id = ANY(@ids)",
            new { id = charUid, ids = itemIds.ToArray() });
        foreach (var (itemId, cnt) in rows)
        {
            if (!res.TryGetValue(itemId, out var l))
                res[itemId] = l = new List<int>();
            l.Add(cnt);
        }
        return res;
    }

    /// <summary>Applique les récompenses or/XP d'une quête en DB.</summary>
    public async Task AddGoldExpAsync(int charUid, long gold, long exp)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_characters SET " +
            "gold = LEAST(999999999, GREATEST(0, gold + @g)), " +
            "exp = exp + @e " +
            "WHERE id = @id", new { id = charUid, g = gold, e = exp });
    }

    /// <summary>TC « FortuneBag Item= %hd Counter= %hd » natif
    /// (@0x081f8fxx) : UPDATE du compteur d'ouverture si la ligne
    /// (account_id,item_id) existe, INSERT sinon —
    /// `player_fortune_bag` clé = account_id du perso (compte).</summary>
    public async Task SetFortuneBagCounterAsync(int charUid, int itemId,
        int counter)
    {
        await using var c = Open();
        var accountId = await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(account_id,0) FROM player_characters " +
            "WHERE id = @id", new { id = charUid });
        if (accountId <= 0) return;
        await c.ExecuteAsync(@"
            INSERT INTO player_fortune_bag (account_id, item_id, counter)
            VALUES (@acc, @item, @cnt)
            ON CONFLICT (account_id, item_id)
            DO UPDATE SET counter = @cnt",
            new { acc = accountId, item = itemId, cnt = counter });
    }

    /// <summary>GainSpecialCoin natif (mission/decompiled.c
    /// CDBAgent::GainSpecialCoin @0x80863f0 + SQL
    /// GetSpecialCoinUpdateSQLCmd) : `special_coins.coinNN += amount`
    /// clampé à [0, 9999999] — NN = coinId 1..12 (le TYPE wire natif
    /// 0..11 mappe sur coin{type+1}). Retourne le nouveau total.</summary>
    public async Task<int> AddSpecialCoinAsync(int charUid, int coinId,
        int amount)
    {
        var idx = Math.Clamp(coinId, 1, 12);
        await using var c = Open();
        // Insère la ligne si absente (récompense reçue avant la
        // création de la ligne special_coins du compte).
        await c.ExecuteAsync(
            "INSERT INTO special_coins (player_id) VALUES (@id) " +
            "ON CONFLICT (player_id) DO NOTHING",
            new { id = charUid });
        // coinNN choisi dynamiquement — colonne blanche par clamp ;
        // GREATEST/LEAST reproduit le clamp natif 0..9999999.
        return await c.ExecuteScalarAsync<int>(
            $"UPDATE special_coins SET coin{idx:D2} = " +
            $"GREATEST(0, LEAST(9999999, coin{idx:D2} + @a)) " +
            $"WHERE player_id = @id RETURNING coin{idx:D2}",
            new { id = charUid, a = amount });
    }

    /// <summary>GainPrestigeExp natif (@0x0808c1b0) : ids valides
    /// 1-38/51-59/81-85, INSERT level=1/exp=0 au premier gain, exp
    /// clampée [0, S_Parameter 361 = 75000], level remis à 1. La
    /// notification client (60) + miroir zone (update_prestige) restent
    /// au monde — les gains initiés zone passent par ZW 636
    /// « gain_prestige ».</summary>
    public async Task AddPrestigeExpAsync(int charUid, int prestigeId,
        int exp)
    {
        if (!Eden.Core.Database.Repositories.PrestigeRepository
                .IsValidPrestigeId(prestigeId))
            return;
        await using var c = Open();
        await c.ExecuteAsync(
            "INSERT INTO prestige (player_id, prestige_id, level, exp) " +
            "VALUES (@id, @pid, 1, GREATEST(0, LEAST(@cap, @e))) " +
            "ON CONFLICT (player_id, prestige_id) " +
            "DO UPDATE SET level = 1, " +
            "  exp = GREATEST(0, LEAST(@cap, prestige.exp + @e))",
            new { id = charUid, pid = prestigeId, e = exp,
                cap = Eden.Core.Database.Repositories
                    .PrestigeRepository.MaxExp });
    }

    /// <summary>Crédite l'exp de famille — TC_GainFamilyExp natif
    /// (@0x81ef300 → CFamily::GainExp) : RewardFamilyExp des quêtes
    /// est relayé en gain_family_exp au mission natif. Chez nous la
    /// mission écrit directement family.exp de la guilde du perso et
    /// recalcule lv selon la formule du monde (1 + exp/1000,
    /// clamp 1..10 — FamilySetLevel d'Eden.World).</summary>
    public async Task AddFamilyExpAsync(int charUid, int amount)
    {
        if (amount == 0) return;
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE family SET exp = GREATEST(0, exp + @a), " +
            "lv = LEAST(10, GREATEST(1, (exp + @a) / 1000 + 1)) " +
            "WHERE id = (SELECT family_id FROM player_characters " +
            "WHERE id = @c)", new { a = amount, c = charUid });
    }

    // ---------------------------------------------------------- mail --

    /// <summary>Marque un courrier lu (player_mail.opened=1).</summary>
    public async Task MailMarkReadAsync(int charUid, int mailId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_mail SET opened = 1 WHERE id = @m AND receiver_id = @c",
            new { m = mailId, c = charUid });
    }

    /// <summary>Supprime un courrier.</summary>
    public async Task<bool> MailDeleteAsync(int charUid, int mailId)
    {
        await using var c = Open();
        return await c.ExecuteAsync(
            "DELETE FROM player_mail WHERE id = @m AND receiver_id = @c",
            new { m = mailId, c = charUid }) > 0;
    }

    /// <summary>Marque un courrier retourné.</summary>
    public async Task<bool> MailReturnAsync(int charUid, int mailId)
    {
        await using var c = Open();
        return await c.ExecuteAsync(
            "UPDATE player_mail SET returned = 1 " +
            "WHERE id = @m AND receiver_id = @c",
            new { m = mailId, c = charUid }) > 0;
    }

    /// <summary>Prélève l'or attaché d'un courrier ET crédite le
    /// personnage en UNE transaction : verrou de la ligne courrier
    /// (FOR UPDATE — un double MailGetGold concurrent ne peut pas
    /// relire le même montant), remise à zéro + marquage « pris »
    /// (returned, même convention que la pièce jointe), puis crédit
    /// <c>player_characters.gold</c> clampé au cap natif. Avant :
    /// l'UPDATE RETURNING renvoyait la valeur APRÈS remise à zéro
    /// (toujours 0 → jamais crédité) et le crédit partait sur une
    /// seconde connexion. Équivalent du `TC_MailGetGold` natif (DBA).
    /// Retourne le montant prélevé (0 = rien d'attaché ou pas le
    /// destinataire).</summary>
    public async Task<int> MailTakeGoldAsync(int charUid, int mailId)
    {
        await using var c = Open();
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            var gold = await c.ExecuteScalarAsync<int?>(
                "SELECT gold FROM player_mail " +
                "WHERE id = @m AND receiver_id = @c AND gold > 0 " +
                "FOR UPDATE",
                new { m = mailId, c = charUid }, tx);
            if (gold is not > 0)
            {
                await tx.RollbackAsync();
                return 0;
            }
            await c.ExecuteAsync(
                "UPDATE player_mail SET gold = 0, returned = 1 " +
                "WHERE id = @m AND receiver_id = @c",
                new { m = mailId, c = charUid }, tx);
            await c.ExecuteAsync(
                "UPDATE player_characters SET gold = " +
                "LEAST(999999999, GREATEST(0, gold + @g)) " +
                "WHERE id = @c",
                new { g = gold.Value, c = charUid }, tx);
            await tx.CommitAsync();
            return gold.Value;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Crédite l'or du personnage (player_characters.gold).</summary>
    public async Task AddGoldAsync(int charUid, int gold)
    {
        if (gold <= 0) return;
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_characters SET gold = " +
            "LEAST(999999999, GREATEST(0, gold + @g)) WHERE id = @c",
            new { g = gold, c = charUid });
    }

    /// <summary>Résultat du transfert mailitem→inventory1.</summary>
    public sealed record MailTakenItem(int ItemId, int Bag, int Slot,
        int Durability, int MaxDurability, int Bind, int Strengthen);

    /// <summary>Détache l'item d'un courrier vers l'inventaire — copie
    /// intégrale de la ligne mailitem dans inventory1 (schémas natifs
    /// identiques à mail_id↔loc près : maker, embedded_*, combos,
    /// strengthen, magicbox_*…), puis efface la pièce jointe.
    /// `conIndex`/`invLoc` viennent du client (cible préférée) ;
    /// slot libre du sac sinon. Retourne null sans pièce jointe ou
    /// inventaire plein.</summary>
    public async Task<MailTakenItem?> MailTakeItemAsync(int charUid,
        int mailId, int conIndex, int invLoc)
    {
        await using var c = Open();
        // Ouverture explicite : les appels Dapper rouvrent/referment
        // une connexion fermée, or BeginTransactionAsync exige une
        // connexion ouverte en continu.
        await c.OpenAsync();
        // Pièce jointe présente ? (mailitem lié par mail_id+player_id —
        // index unique natif mailitem_mailid_index).
        var itemId = await c.ExecuteScalarAsync<int?>(
            "SELECT item_id FROM mailitem " +
            "WHERE player_id = @c AND mail_id = @m",
            new { c = charUid, m = mailId });
        if (itemId == null) return null;

        // Slot cible : inv_loc/con_index du client si libre, sinon
        // premier loc libre du sac (container_index) demandé.
        var bag = conIndex >= 0 ? conIndex : 0;
        var slot = await c.ExecuteScalarAsync<int?>(
            "SELECT s.loc FROM generate_series(0, 255) s(loc) " +
            "WHERE s.loc = @loc AND NOT EXISTS (" +
            "  SELECT 1 FROM inventory1 i WHERE i.player_id = @c " +
            "  AND i.container_index = @bag AND i.loc = s.loc) " +
            "LIMIT 1",
            new { loc = invLoc, c = charUid, bag });
        slot ??= await c.ExecuteScalarAsync<int?>(
            "SELECT s.loc FROM generate_series(0, 255) s(loc) " +
            "WHERE NOT EXISTS (" +
            "  SELECT 1 FROM inventory1 i WHERE i.player_id = @c " +
            "  AND i.container_index = @bag AND i.loc = s.loc) " +
            "ORDER BY s.loc LIMIT 1",
            new { c = charUid, bag });
        if (slot == null) return null; // inventaire plein

        // Le serial de la ligne mailitem — alloué via item_serial à
        // l'ENVOI — est conservé pour la ligne inventaire (miroir du
        // SwitchContainerItem natif : l'item garde son identité à
        // travers le déplacement ; le DELETE de la ligne source dans
        // la même tx rend la réutilisation sûre). Aucun nextval ici :
        // ré-allouer un serial à la récupération doublait
        // l'identité de l'item sans nécessité.

        // Transaction : INSERT inventory1 (SELECT mailitem) + DELETE
        // mailitem + UPDATE player_mail atomiques — un crash
        // intermédiaire dupliquait l'item (mail + inventaire) ou le
        // perdait. Ordre : mailitem → inventory1 → player_mail.
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            // Copie complète mailitem→inventory1 : mêmes colonnes
            // natives sauf mail_id→loc (vérifié sur le schéma live —
            // durability, maker, embedded_1..6, combos, strengthen,
            // magicbox_*, train_*). `id` premier = serial préservé.
            const string moveSql = @"
                INSERT INTO inventory1
                    (id, item_id, durability, maker, rank, identify,
                     magic, embedded_amount, embedded_id1, embedded_id2,
                     embedded_id3, embedded_id4, embedded_id5,
                     embedded_id6, create_time, due_date,
                     container_index, combo_id, extra_combo_id1,
                     extra_combo_id2, extra_combo_id3, extra_combo_id4,
                     extra_combo_id5, extra_combo_id6, strengthen,
                     cur_maxdurability, bind, player_id, loc, witchcraft,
                     extra_combo_id7, extra_combo_id8, extra_combo_id9,
                     quality, plus_parameter, color, train_level,
                     train_exp, train_slot1_type, train_slot1_level,
                     train_slot2_type, train_slot2_level,
                     train_slot3_type, train_slot3_level,
                     extra_combo_id10, extra_combo_id11, gold_enchant,
                     magicbox_enchant1, magicbox_enchant2,
                     magicbox_enchant3, magicbox_energy,
                     magicbox_starlevel, unbind)
                SELECT id, item_id, durability, maker, rank,
                     identify, magic, embedded_amount, embedded_id1,
                     embedded_id2, embedded_id3, embedded_id4,
                     embedded_id5, embedded_id6, create_time, due_date,
                     @bag, combo_id, extra_combo_id1, extra_combo_id2,
                     extra_combo_id3, extra_combo_id4, extra_combo_id5,
                     extra_combo_id6, strengthen, cur_maxdurability,
                     bind, @c, @slot, witchcraft, extra_combo_id7,
                     extra_combo_id8, extra_combo_id9, quality,
                     plus_parameter, color, train_level, train_exp,
                     train_slot1_type, train_slot1_level,
                     train_slot2_type, train_slot2_level,
                     train_slot3_type, train_slot3_level,
                     extra_combo_id10, extra_combo_id11, gold_enchant,
                     magicbox_enchant1, magicbox_enchant2,
                     magicbox_enchant3, magicbox_energy,
                     magicbox_starlevel, unbind
                FROM mailitem WHERE player_id = @c AND mail_id = @m";
            var moved = await c.ExecuteAsync(moveSql,
                new { bag, slot, c = charUid, m = mailId }, tx);
            if (moved == 0)
            {
                await tx.RollbackAsync();
                return null;
            }

            // Nettoyage : ligne mailitem + flag pièce jointe du
            // courrier (item_id=0, returned=1 — marquage « pris »,
            // même convention que MarkAttachmentsClaimedAsync).
            await c.ExecuteAsync(
                "DELETE FROM mailitem WHERE player_id = @c " +
                "AND mail_id = @m",
                new { c = charUid, m = mailId }, tx);
            await c.ExecuteAsync(
                "UPDATE player_mail SET item_id = 0, returned = 1 " +
                "WHERE id = @m AND receiver_id = @c",
                new { m = mailId, c = charUid }, tx);

            var row = await c.QuerySingleAsync<MailTakenItemRow>(
                "SELECT item_id, durability, cur_maxdurability, bind, " +
                "strengthen FROM inventory1 " +
                "WHERE player_id = @c AND container_index = @bag " +
                "AND loc = @slot",
                new { c = charUid, bag, slot }, tx);
            await tx.CommitAsync();
            return new MailTakenItem(row.ItemId, bag, slot.Value,
                row.Durability,
                row.CurMaxDurability > 0 ? row.CurMaxDurability
                    : row.Durability,
                row.Bind, row.Strengthen);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private sealed class MailTakenItemRow
    {
        public int ItemId { get; set; }
        public int Durability { get; set; }
        public int CurMaxDurability { get; set; }
        public int Bind { get; set; }
        public int Strengthen { get; set; }
    }

    // ---------- Couples / mariages (WeddingService) ----------

    /// <summary>Record couple pour `ReserverChuch` — `love_id` natif
    /// = l'id de la ligne `player_couples` (le CWedding natif stocke
    /// `exec+0x600`, l'identifiant du CLover = couple id).</summary>
    public readonly record struct CoupleIds(
        int LoveId, int Partner1Id, int Partner2Id);

    /// <summary>Couple du perso (partner1 ou partner2), s'il existe.</summary>
    public async Task<CoupleIds?> GetCoupleAsync(int charUid)
    {
        await using var c = Open();
        return await c.QuerySingleOrDefaultAsync<CoupleIds>(
            "SELECT id AS LoveId, partner1_id AS Partner1Id, " +
            "partner2_id AS Partner2Id FROM player_couples " +
            "WHERE partner1_id = @c OR partner2_id = @c",
            new { c = charUid });
    }

    /// <summary>`given_name` du perso — les noms des deux amants sont
    /// embarqués dans le `CWedding` natif (+0x1c demandeur,
    /// +0x20 partenaire) et renvoyés au client 599.</summary>
    public async Task<string?> GetCharacterNameAsync(int charUid)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<string?>(
            "SELECT given_name FROM player_characters WHERE id = @c",
            new { c = charUid });
    }

    // ---------- Panneau d'affichage (message_board natif) ----------
    //
    // CExecutionerDBO::SaveMessage natif : INSERT INTO message_board
    // (message_id, title, content, player_name, create_date).
    // AllMessageData est plafonné ~100 entrées (deque, chunks de 25)
    // — MissionServerMessageWrite refuse au-delà.

    /// <summary>Ligne du panneau (CMessageData natif :
    /// +0 id, +4 titre, +8 contenu, +0xc nom, +0x10 create_date).</summary>
    public readonly record struct BoardMessage(
        int MessageId, string Title, string Content,
        string PlayerName, int CreateDate);

    /// <summary>Nombre de posts actifs — cap natif 100
    /// (MessageWrite::Execute @0x813e440).</summary>
    public async Task<int> BoardCountAsync()
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM message_board");
    }

    /// <summary>SaveMessage natif : INSERT avec id = max+1 (équivalent
    /// CUIDAllocator::MessageIDAllocator — l'id ne se réutilise pas
    /// dans une même session mais le compteur repart du max au
    /// redémarrage, comme le natif). Retourne l'id attribué.</summary>
    public async Task<int> BoardWriteAsync(string title, string content,
        string playerName)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "INSERT INTO message_board " +
            "(message_id, title, content, player_name, create_date) " +
            "SELECT COALESCE(MAX(message_id), 0) + 1, @t, @c, @n, @d " +
            "FROM message_board RETURNING message_id",
            new
            {
                t = title, c = content, n = playerName,
                d = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
    }

    /// <summary>Post par id (TC_MessageRead : recherche dans
    /// AllMessageData puis copie du champ contenu).</summary>
    public async Task<BoardMessage?> BoardReadAsync(int messageId)
    {
        await using var c = Open();
        var r = await c.QuerySingleOrDefaultAsync<BoardMessage?>(
            "SELECT message_id AS MessageId, title AS Title, " +
            "content AS Content, player_name AS PlayerName, " +
            "create_date AS CreateDate FROM message_board " +
            "WHERE message_id = @i", new { i = messageId });
        return r;
    }

    /// <summary>Posts récents — TC_MessageListQuery ne retient que
    /// create_date > now-0x15180 (24 h), plus récents en tête.</summary>
    public async Task<IReadOnlyList<BoardMessage>> BoardListAsync()
    {
        await using var c = Open();
        var since = (int)DateTimeOffset.UtcNow
            .ToUnixTimeSeconds() - 0x15180;
        var rows = await c.QueryAsync<BoardMessage>(
            "SELECT message_id AS MessageId, title AS Title, " +
            "content AS Content, player_name AS PlayerName, " +
            "create_date AS CreateDate FROM message_board " +
            "WHERE create_date > @s ORDER BY message_id DESC",
            new { s = since });
        return rows.AsList();
    }

    /// <summary>DeleteMessage natif : DELETE WHERE message_id.</summary>
    public async Task<bool> BoardDeleteAsync(int messageId)
    {
        await using var c = Open();
        return await c.ExecuteAsync(
            "DELETE FROM message_board WHERE message_id = @i",
            new { i = messageId }) > 0;
    }

    // ---------- Carte joueur (player_card natif) ----------
    //
    // TC_QueryPlayerCard : la mission assemble name/level/class/
    // classLv (exec +0x38=ClassID, +0x5c=Level, +0x68=ClassLv),
    // nom de guilde (family.name via player_characters.family_id),
    // les 12 stats de player_card + introduct + is_public.

    /// <summary>Carte complète pour le ZMis 104.</summary>
    public readonly record struct PlayerCardFull(
        string Name, short Level, short ClassId, short ClassLv,
        string FamilyName, short[] Stats, string Intro, bool IsPublic);

    /// <summary>Carte + données d'affichage du perso cible
    /// (LEFT JOINs : carte/classe/guilde absentes → défauts natifs).</summary>
    public async Task<PlayerCardFull?> GetPlayerCardFullAsync(int targetId)
    {
        await using var c = Open();
        var r = await c.QuerySingleOrDefaultAsync<CardRow>(
            "SELECT pc.given_name AS Name, pc.level AS Level, " +
            "pc.class_id AS ClassId, pc.gender_id AS Gender, " +
            "COALESCE(cl.level, 1) AS ClassLv, " +
            "COALESCE(f.name, '') AS FamilyName, " +
            "COALESCE(card.gender, 0) AS G, " +
            "COALESCE(card.blood_type, 0) AS B, " +
            "COALESCE(card.birth_month, 0) AS BM, " +
            "COALESCE(card.birth_day, 0) AS BD, " +
            "COALESCE(card.player_sign, 0) AS PS, " +
            "COALESCE(card.region, 0) AS RG, " +
            "COALESCE(card.mood, 0) AS MD, " +
            "COALESCE(card.like_1, 0) AS L1, " +
            "COALESCE(card.like_2, 0) AS L2, " +
            "COALESCE(card.like_3, 0) AS L3, " +
            "COALESCE(card.hate_1, 0) AS H1, " +
            "COALESCE(card.hate_2, 0) AS H2, " +
            "COALESCE(card.introduct, '') AS Intro, " +
            "COALESCE(card.is_public, 0) AS Pub " +
            "FROM player_characters pc " +
            "LEFT JOIN player_card card ON card.player_id = pc.id " +
            "LEFT JOIN player_classlist cl ON cl.player_id = pc.id " +
            "  AND cl.class = pc.class_id " +
            "LEFT JOIN family f ON f.id = pc.family_id " +
            "WHERE pc.id = @t", new { t = targetId });
        if (r == null) return null;
        return new PlayerCardFull(r.Name, (short)r.Level,
            (short)r.ClassId, (short)r.ClassLv, r.FamilyName,
            new short[] { (short)r.G, (short)r.B, (short)r.BM,
                (short)r.BD, (short)r.PS, (short)r.RG, (short)r.MD,
                (short)r.L1, (short)r.L2, (short)r.L3, (short)r.H1,
                (short)r.H2 },
            r.Intro, r.Pub != 0);
    }

    private sealed class CardRow
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int ClassId { get; set; }
        public int Gender { get; set; }
        public int ClassLv { get; set; }
        public string FamilyName { get; set; } = "";
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

    /// <summary>UPDATE player_card — GetPlayerCardUpdateSQLCmd natif :
    /// gender, blood_type, birth_month, birth_day, player_sign, region,
    /// mood, like_1-3, hate_1-2, introduct, is_public WHERE player_id.
    /// INSERT si absente (la ligne est provisionnée à la création en
    /// natif ; un perso legacy peut ne pas l'avoir).</summary>
    public async Task UpdatePlayerCardAsync(int charUid, short[] stats,
        string intro, bool isPublic)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "INSERT INTO player_card (player_id, gender, blood_type, " +
            "birth_month, birth_day, player_sign, region, mood, " +
            "like_1, like_2, like_3, hate_1, hate_2, introduct, " +
            "is_public) VALUES (@c, @g, @b, @bm, @bd, @ps, @rg, @md, " +
            "@l1, @l2, @l3, @h1, @h2, @i, @p) " +
            "ON CONFLICT (player_id) DO UPDATE SET " +
            "gender=@g, blood_type=@b, birth_month=@bm, " +
            "birth_day=@bd, player_sign=@ps, region=@rg, mood=@md, " +
            "like_1=@l1, like_2=@l2, like_3=@l3, hate_1=@h1, " +
            "hate_2=@h2, introduct=@i, is_public=@p",
            new
            {
                c = charUid, g = stats[0], b = stats[1], bm = stats[2],
                bd = stats[3], ps = stats[4], rg = stats[5],
                md = stats[6], l1 = stats[7], l2 = stats[8],
                l3 = stats[9], h1 = stats[10], h2 = stats[11],
                i = intro, p = isPublic ? 1 : 0,
            });
    }

    /// <summary>Lignes `recommend_activities` d'un perso — SELECT
    /// natif CExecutionerDBO::LoadRecommendActivities : « SELECT
    /// recommend_id, target_number, is_finished, is_tracing FROM
    /// recommend_activities WHERE player_id = %d ».</summary>
    public async Task<IReadOnlyList<(int Id, int Progress,
        int Finished, int Tracing)>> LoadRecommendActivitiesAsync(
        int charUid)
    {
        await using var c = Open();
        var rows = await c.QueryAsync<(int Id, int Progress,
            int Finished, int Tracing)>(
            "SELECT recommend_id AS Id, target_number AS Progress, " +
            "is_finished AS Finished, is_tracing AS Tracing " +
            "FROM recommend_activities WHERE player_id = @c",
            new { c = charUid });
        return rows.AsList();
    }

    /// <summary>SaveRecommendActivities natif : DELETE du perso puis
    /// INSERT batch « (player_id,recommend_id,target_number,
    /// is_finished) VALUES (...) » + UPDATE
    /// player_characters.recommend_activities_time
    /// (GetRAUdateTimeUpdateSQLCmd — appelé par le refresh 831).</summary>
    public async Task SaveRecommendActivitiesAsync(int charUid, int ts,
        IReadOnlyList<(int Id, int Progress, int Finished)> rows)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM recommend_activities WHERE player_id = @c",
            new { c = charUid });
        foreach (var (id, progress, finished) in rows)
            await c.ExecuteAsync(
                "INSERT INTO recommend_activities (player_id, " +
                "recommend_id, target_number, is_finished) VALUES " +
                "(@c, @i, @p, @f)",
                new { c = charUid, i = id, p = progress, f = finished });
        await c.ExecuteAsync(
            "UPDATE player_characters SET " +
            "recommend_activities_time = @t WHERE id = @c",
            new { t = ts, c = charUid });
    }

    /// <summary>GetRecommendActivityTracingSQLCmd natif :
    /// « UPDATE recommend_activities SET is_tracing = 1 WHERE
    /// player_id = %d AND recommend_id = %hu » (clear = même UPDATE
    /// à 0 sur tout le perso puis set des tracés — équivalent par
    /// ligne : is_tracing = @t sur le couple visé).</summary>
    public async Task SetRecommendActivityTracingAsync(int charUid,
        int activityId, int tracing)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE recommend_activities SET is_tracing = @t " +
            "WHERE player_id = @c AND recommend_id = @i",
            new { t = tracing, c = charUid, i = activityId });
    }

    /// <summary>« GMGainLifelong Money = %d » — lifelong_gold du perso
    /// (récompense recommend-activities + quêtes boutique). Natif
    /// <c>CGlobalInfo::GainLifelongGold</c> @0x807a230 : exec+0x75c
    /// borné [0, 9999999]. Retourne le nouveau total pour le ZMis 173
    /// renvoyé à la zone.</summary>
    public async Task<int> AddLifelongGoldAsync(int charUid, int amount)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "UPDATE player_characters SET lifelong_gold = " +
            "LEAST(9999999, GREATEST(0, COALESCE(lifelong_gold, 0) + @v)) " +
            "WHERE id = @c RETURNING lifelong_gold",
            new { v = amount, c = charUid });
    }

    /// <summary>« Update WarCoin Coin = %d » — natif
    /// <c>TC_GainWarCoin</c> → <c>CGlobalInfo::GainWarCoin</c>
    /// @0x8081490 : exec+0x874 = war_coin (« Battle Merit »,
    /// S_TextIndex 0x1665), borné [0, 9999999]. Retourne le nouveau
    /// total pour le « UpdateWarCoin ch_id = %d coin = %d » renvoyé
    /// aux zones (→ 576 ClientUpdateWarCoin).</summary>
    public async Task<int> AddWarCoinAsync(int charUid, int amount)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "UPDATE player_characters SET war_coin = " +
            "LEAST(9999999, GREATEST(0, COALESCE(war_coin, 0) + @v)) " +
            "WHERE id = @c RETURNING war_coin",
            new { v = amount, c = charUid });
    }

    /// <summary>« Update AlpacaCoin Coin = %d » — natif
    /// <c>TC_GainAlpacaCoin</c> → <c>CGlobalInfo::GainAlpacaCoin</c>
    /// @0x8081160 : exec+0x878 = alpaca_coin (« Alpaca Coins »,
    /// S_TextIndex 0x13c1), borné [0, 9999999]. Retourne le nouveau
    /// total pour le « UpdateAlpacaCoin ch_id = %d coin = %d »
    /// renvoyé aux zones (→ 580 ClientUpdateAlpacaCoin).</summary>
    public async Task<int> AddAlpacaCoinAsync(int charUid, int amount)
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<int>(
            "UPDATE player_characters SET alpaca_coin = " +
            "LEAST(9999999, GREATEST(0, COALESCE(alpaca_coin, 0) + @v)) " +
            "WHERE id = @c RETURNING alpaca_coin",
            new { v = amount, c = charUid });
    }

    /// <summary>TC_UpdateRecommendActivity natif : met à jour
    /// target_number + is_finished d'une ligne (mission ed →
    /// SaveRecommendActivities — ici UPDATE ciblée équivalente).</summary>
    public async Task UpdateRecommendActivityAsync(int charUid,
        int activityId, int progress, int finished)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE recommend_activities SET target_number = @p, " +
            "is_finished = @f " +
            "WHERE player_id = @c AND recommend_id = @i",
            new { p = progress, f = finished, c = charUid,
                i = activityId });
    }

    // ---- player_room (sous-système logement, mission ed) ----

    /// <summary>Une ligne `player_room` — ordre de colonnes du SELECT
    /// natif `CPlayerRoomDBA` (mission ed) : player_id, player_name,
    /// hotel_type, lover/friend/family/other_privilege, populity,
    /// extend, due_date, wallpaper, living_floor, bed_floor,
    /// party_time.</summary>
    public sealed class RoomRow
    {
        /// <summary>player_id = room_id (salle par perso).</summary>
        public int PlayerId { get; set; }
        /// <summary>Nom de salle = nom du perso.</summary>
        public string PlayerName { get; set; } = "";
        /// <summary>Type d'hôtel (1-3).</summary>
        public short HotelType { get; set; }
        /// <summary>Privilèges d'entrée par relation.</summary>
        public int LoverPrivilege { get; set; }
        public int FriendPrivilege { get; set; }
        public int FamilyPrivilege { get; set; }
        public int OtherPrivilege { get; set; }
        /// <summary>Popularité (compteur de visiteurs).</summary>
        public short Populity { get; set; }
        /// <summary>Nombre d'extensions achetées.</summary>
        public short Extend { get; set; }
        /// <summary>Échéance de location (epoch).</summary>
        public int DueDate { get; set; }
        /// <summary>Textures (item_id des clones consommés).</summary>
        public int Wallpaper { get; set; }
        public int LivingFloor { get; set; }
        public int BedFloor { get; set; }
        /// <summary>Epoch du dernier StartParty.</summary>
        public int PartyTime { get; set; }
        /// <summary>Statistiques runtime (CPlayerRoomDBA +0x28/+0x2c/
        /// +0x30) — compteurs de visites propagés dans la recherche
        /// (778) et le push 779. Non persistés en base (absents du
        /// SELECT natif) — remis à zéro à la location.</summary>
        public int Stat1 { get; set; }
        public int Stat2 { get; set; }
        public int Stat3 { get; set; }
        /// <summary>Mode d'édition runtime (Switch Room Setting
        /// Mode) — flag volatile du CPlayerRoomDBA natif.</summary>
        public short SettingMode { get; set; }
    }

    /// <summary>SELECT natif `Load PlayerRoom` (mission ed) — toutes
    /// les salles chargées au démarrage.</summary>
    public async Task<List<RoomRow>> LoadRoomsAsync()
    {
        await using var c = Open();
        return (await c.QueryAsync<RoomRow>(
            "SELECT player_id AS PlayerId, player_name AS PlayerName, " +
            "hotel_type AS HotelType, lover_privilege AS LoverPrivilege, " +
            "friend_privilege AS FriendPrivilege, " +
            "family_privilege AS FamilyPrivilege, " +
            "other_privilege AS OtherPrivilege, " +
            "populity AS Populity, extend AS Extend, " +
            "due_date AS DueDate, wallpaper AS Wallpaper, " +
            "living_floor AS LivingFloor, bed_floor AS BedFloor, " +
            "party_time AS PartyTime FROM player_room")).ToList();
    }

    /// <summary>INSERT natif `Rent PlayerRoom` (mission ed) —
    /// privilèges 1,1,1,1 et compteurs à zéro.</summary>
    public async Task InsertRoomAsync(int playerId, string name,
        short hotelType, int dueDate)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "INSERT INTO player_room (player_id, player_name, " +
            "hotel_type, lover_privilege, friend_privilege, " +
            "family_privilege, other_privilege, populity, extend, " +
            "due_date, wallpaper, living_floor, bed_floor, " +
            "party_time) VALUES (@id, @n, @t, 1, 1, 1, 1, 0, 0, " +
            "@d, 0, 0, 0, 0)",
            new { id = playerId, n = name, t = hotelType, d = dueDate });
    }

    /// <summary>UPDATE natif `Update RoomData` (mission ed) — tous
    /// les champs d'une salle.</summary>
    public async Task UpdateRoomAsync(RoomRow r)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_room SET player_name = @PlayerName, " +
            "hotel_type = @HotelType, lover_privilege = @LoverPrivilege, " +
            "friend_privilege = @FriendPrivilege, " +
            "family_privilege = @FamilyPrivilege, " +
            "other_privilege = @OtherPrivilege, populity = @Populity, " +
            "extend = @Extend, due_date = @DueDate, " +
            "wallpaper = @Wallpaper, living_floor = @LivingFloor, " +
            "bed_floor = @BedFloor, party_time = @PartyTime " +
            "WHERE player_id = @PlayerId", r);
    }

    /// <summary>DELETE natif `Remove RoomData` (mission ed).</summary>
    public async Task DeleteRoomAsync(int playerId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM player_room WHERE player_id = @i",
            new { i = playerId });
    }

    /// <summary>SELECT natif `Search PlayerRoom` (mission ed) — id
    /// des salles dont le nom matche (LIKE) et le type demandé.</summary>
    public async Task<List<int>> SearchRoomIdsAsync(string nameLike,
        short hotelType)
    {
        await using var c = Open();
        return (await c.QueryAsync<int>(
            "SELECT player_id FROM player_room WHERE " +
            "player_name LIKE @l AND hotel_type = @t " +
            "ORDER BY player_id",
            new { l = nameLike, t = hotelType })).ToList();
    }

    /// <summary>Recherche par nom uniquement (hotel_type = 0 = tous,
    /// cas natif « Search All »).</summary>
    public async Task<List<int>> SearchAllRoomIdsAsync(string nameLike)
    {
        await using var c = Open();
        return (await c.QueryAsync<int>(
            "SELECT player_id FROM player_room WHERE " +
            "player_name LIKE @l ORDER BY player_id",
            new { l = nameLike })).ToList();
    }

    /// <summary>« UPDATE player_room SET due_date = %u WHERE
    /// player_id = %d » — DBO::PayRent @0x81a8950.</summary>
    public async Task UpdateRoomDueDateAsync(int playerId, int dueDate)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_room SET due_date = @d WHERE player_id = @i",
            new { d = dueDate, i = playerId });
    }

    /// <summary>Re-location avec changement d'hotel_type (op3) :
    /// le natif vide tous les conteneurs de la salle
    /// (`piVar8 == NULL || loc==0` → delete des items dans
    /// TC_RentPlayerRoom).</summary>
    public async Task ClearRoomContainersAsync(int playerId)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "DELETE FROM player_room_container WHERE player_id = @i",
            new { i = playerId });
    }

    /// <summary>TC_ChangeRoomTexture : l'item-décor à
    /// (container_index, loc) est consommé — sa valeur `item_id`
    /// devient la texture. Retourne l'item_id ou 0.</summary>
    public async Task<int> ConsumeRoomTextureItemAsync(int playerId,
        int containerIndex, int loc)
    {
        await using var c = Open();
        var itemId = await c.ExecuteScalarAsync<int?>(
            "SELECT item_id FROM player_room_container " +
            "WHERE player_id = @i AND container_index = @c AND loc = @l",
            new { i = playerId, c = containerIndex, l = loc });
        if (itemId == null || itemId == 0) return 0;
        await c.ExecuteAsync(
            "DELETE FROM player_room_container WHERE player_id = @i " +
            "AND container_index = @c AND loc = @l",
            new { i = playerId, c = containerIndex, l = loc });
        return itemId.Value;
    }

    /// <summary>DBO::SaveDecorationToDB @0x81a8a40 — « Update
    /// player_room_container SET combo_id = %hu, extra_combo_id1..7
    /// = %hu WHERE player_id = %d AND container_index = %zd AND
    /// loc = %hd ». combo=is_decorate, extras=pos(3)+dir(3)+rotate.</summary>
    public async Task SaveRoomDecorationAsync(int playerId,
        int containerIndex, int loc, int combo,
        int e1, int e2, int e3, int e4, int e5, int e6, int e7)
    {
        await using var c = Open();
        await c.ExecuteAsync(
            "UPDATE player_room_container SET combo_id = @c0, " +
            "extra_combo_id1 = @e1, extra_combo_id2 = @e2, " +
            "extra_combo_id3 = @e3, extra_combo_id4 = @e4, " +
            "extra_combo_id5 = @e5, extra_combo_id6 = @e6, " +
            "extra_combo_id7 = @e7 " +
            "WHERE player_id = @i AND container_index = @ci " +
            "AND loc = @l",
            new
            {
                c0 = combo, e1, e2, e3, e4, e5, e6, e7,
                i = playerId, ci = containerIndex, l = loc,
            });
    }

    /// <summary>Données `CExecutionerData` requises par les TC room :
    /// <summary>Nom d'une guilde (family.name) — annonce du
    /// vainqueur de croisade ; chaîne vide si introuvable.</summary>
    public async Task<string> GetFamilyNameAsync(int familyId)
    {
        if (familyId <= 0) return "";
        await using var c = await _db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<string>(
            "SELECT COALESCE(name,'') FROM family WHERE id=@id",
            new { id = familyId }) ?? "";
    }

    /// <summary>family_id d'un perso (player_characters.family_id)
    /// — utilisé par CrusadeJoin pour filer la guilde.</summary>
    public async Task<int> GetFamilyIdAsync(int charUid)
    {
        await using var c = Open();
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(family_id,0) FROM player_characters " +
            "WHERE id=@i", new { i = charUid });
    }

    /// `+0x5c` niveau, `+0x8c` or, `+0xb0` family_id (+ nom pour
    /// player_room.player_name).</summary>
    public async Task<(int Level, int Gold, int FamilyId, string Name)?>
        GetRoomExecStatsAsync(int charUid)
    {
        await using var c = Open();
        var row = await c.QuerySingleOrDefaultAsync<
            (int level, int gold, int family_id, string name)?>(
            "SELECT level, COALESCE(gold,0) AS gold, " +
            "COALESCE(family_id,0) AS family_id, name " +
            "FROM player_characters WHERE id = @i",
            new { i = charUid });
        return row == null ? null
            : (row.Value.level, row.Value.gold, row.Value.family_id,
                row.Value.name);
    }

    /// <summary>Lignes brutes d'une requête de classement —
    /// équivalent `CPGDBResult::GetData` du natif : les valeurs sont
    /// retournées par ordre de colonne du SELECT (l'appelant fournit
    /// du SQL littéral prouvé — jamais d'entrée utilisateur).</summary>
    public async Task<List<object?[]>> QueryRankRowsAsync(string sql)
    {
        await using var c = Open();
        var rows = await c.QueryAsync(sql);
        return rows.Select(
            r => ((IDictionary<string, object?>)r).Values.ToArray())
            .ToList();
    }
}
