using Dapper;
using Npgsql;

namespace Eden.Core.Game.Family;

/// <summary>
/// Accès PostgreSQL aux tables famille natives : <c>family</c>,
/// <c>player_characters.family_id</c>, <c>family_cadre_members</c>,
/// <c>family_msgboard</c>, <c>family_schedule</c>, <c>family_log</c>,
/// <c>family_emblem</c>. Reproduit les <c>CFamilyDBO</c> mission
/// (LoadFamily @ 0x8181b0 — SELECT de 43 colonnes, LoadFamilyMessageBoard,
/// LoadFamilySchedule, LoadFamilyLog, LoadFamilyEmblem) et les TC_*
/// (TC_CreateFamily : <c>INSERT INTO family (id, name, leader_id,
/// be_create_time, lv)</c>).
/// </summary>
public sealed class FamilyRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Initialise l'accès aux tables famille.</summary>
    public FamilyRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>SELECT natif complet de <c>CFamilyDBO::LoadFamily</c>
    /// — ordre des colonnes strictement identique au sscanf natif.</summary>
    private const string FamilyColumns =
        "id, name, leader_id, exploit, lv, exp, emblem, uniform, flag, info, treasury, tax_rate, business_tax, " +
        "member_title, leader_title, cadre_title_2, cadre_title_3, cadre_title_4, cadre_title_5, cadre_title_6, cadre_title_7, cadre_title_8, " +
        "channel_privilege, invite_privilege, kickout_privilege, editinfo_privilege, setcadre_privilege, invite25r_privilege, " +
        "fixequip_privilege, msgboard_privilege, delmsg_privilege, treasury_privilege, repair_privilege, " +
        "storage01_privilege, storage02_privilege, storage03_privilege, be_create_time, spell_point, elfking_tid, house_open, " +
        "family_flag, recruit, recruit_message, honor, recycled_time";

    /// <summary>Chargement global au boot monde — reproduit
    /// <c>ClientAllFamily</c> (DBA 27 → monde) : familles non
    /// supprimées, membres (player_characters.family_id), rangs cadre,
    /// board, agenda, logs, emblèmes.</summary>
    public async Task<List<Family>> LoadAllAsync()
    {
        var map = new Dictionary<int, Family>();
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<FamilyRow>(
            $"SELECT {FamilyColumns} FROM family WHERE delete_time = 0");
        foreach (var r in rows)
            map[r.id] = ToFamily(r);

        // Membres — player_characters.family_id ≠ 0 (LoadSCharacter natif).
        var chars = await conn.QueryAsync<CharRow>(
            "SELECT id, given_name, level, node_id, race_id, class_id, class_lv, gender_id, privilege, family_id, " +
            // create_time est un timestamp PG (schéma réel) — le natif
            // transporte un unix i32 (SFamilyMember+0x64) : epoch cast.
            "add_family_time, CAST(EXTRACT(EPOCH FROM create_time) AS integer) AS create_time, " +
            "last_saving_time, self_comment, family_comment, family_msgboard_state, " +
            "family_contribution_point, deleted_time FROM player_characters WHERE family_id <> 0");
        foreach (var c in chars)
        {
            if (c.deleted_time != 0 || !map.TryGetValue(c.family_id, out var fam))
                continue;
            var m = ToMember(c);
            fam.Members[m.CharacterId] = m;
            if (m.CharacterId == fam.LeaderId) fam.LeaderName = m.Name;
        }

        // Rangs cadre — LoadFamilyMemberRank : rank != 0 hors leader.
        var cadres = await conn.QueryAsync(
            "SELECT family_id, player_id, rank FROM family_cadre_members");
        foreach (var c in cadres)
        {
            if (map.TryGetValue((int)c.family_id, out var fam)
                && fam.Members.TryGetValue((int)c.player_id, out var m)
                && (short)c.rank != 0 && m.CharacterId != fam.LeaderId)
                m.Rank = (short)c.rank;
        }

        await LoadMessagesAsync(conn, map);
        await LoadLogsAsync(conn, map);
        await LoadEmblemsAsync(conn, map);
        return new List<Family>(map.Values);
    }

    private static async Task LoadMessagesAsync(NpgsqlConnection conn, Dictionary<int, Family> map)
    {
        // LoadFamilyMessageBoard : SELECT family_id, message_id, title, content, player_name, create_date.
        var msgs = await conn.QueryAsync(
            "SELECT family_id, message_id, title, content, player_name, create_date FROM family_msgboard");
        foreach (var r in msgs)
            if (map.TryGetValue((int)r.family_id, out var fam))
                fam.Messages[(int)r.message_id] = ToMessage(r);

        // family_schedule : table morte native (zéro référence dans
        // zone/mission/world — DeleteAllFamilyScheduleMsg écrit
        // family_msgboard). Le NCID 51 ClientFamilySchedule ne porte
        // que les messages du board (fam+0x9c natif = family_msgboard).

        // family_achievement : clé = achievement_type (GroupKey
        // d'achievement natif), point cumulé.
        var achs = await conn.QueryAsync(
            "SELECT family_id, achievement_type, point FROM family_achievement");
        foreach (var r in achs)
            if (map.TryGetValue((int)r.family_id, out var fam))
                fam.Achievements[(short)r.achievement_type] = (uint)(int)r.point;

        // family_achievement_rank : snapshot périodique natif des
        // compteurs par catégorie (map dual-key Type+GroupKey) — les
        // colonnes restaurent le compteur de chaque clé.
        var ranks = await conn.QueryAsync(
            "SELECT family_id, total_point, monster_kill, family_instance, " +
            "lv01_instance_node, lv31_instance_node, lv46_instance_node, " +
            "lv61_instance_node, lv76_instance_node, lv91_instance_node, " +
            "family_boss, rare_monster, family_mine, family_plant, " +
            "family_summon, family_alchemy, family_casting, family_enchant " +
            "FROM family_achievement_rank");
        foreach (var r in ranks)
        {
            if (!map.TryGetValue((int)r.family_id, out var fam)) continue;
            fam.AchievementTotal = (uint)(int)r.total_point;
            fam.Counters[1] = (uint)(int)r.monster_kill;
            fam.Counters[5] = (uint)(int)r.family_instance;
            fam.Counters[0x65] = (uint)(int)r.lv01_instance_node;
            fam.Counters[0x66] = (uint)(int)r.lv31_instance_node;
            fam.Counters[0x67] = (uint)(int)r.lv46_instance_node;
            fam.Counters[0x68] = (uint)(int)r.lv61_instance_node;
            fam.Counters[0x69] = (uint)(int)r.lv76_instance_node;
            fam.Counters[0x6a] = (uint)(int)r.lv91_instance_node;
            fam.Counters[0x6b] = (uint)(int)r.family_boss;
            fam.Counters[0x6c] = (uint)(int)r.rare_monster;
            fam.Counters[0x1f5] = (uint)(int)r.family_mine;
            fam.Counters[0x1f6] = (uint)(int)r.family_plant;
            fam.Counters[0x1f7] = (uint)(int)r.family_summon;
            fam.Counters[0x1f8] = (uint)(int)r.family_alchemy;
            fam.Counters[0x1f9] = (uint)(int)r.family_casting;
            fam.Counters[0x1fa] = (uint)(int)r.family_enchant;
        }
    }

    private static FamilyMessage ToMessage(dynamic r) => new()
    {
        MessageId = (int)r.message_id, Title = (string)(r.title ?? ""),
        Content = (string)(r.content ?? ""), PlayerName = (string)(r.player_name ?? ""),
        CreateDate = (int)r.create_date,
    };

    private static async Task LoadLogsAsync(NpgsqlConnection conn, Dictionary<int, Family> map)
    {
        // LoadFamilyLog : colonnes du record SLog natif.
        var rows = await conn.QueryAsync(
            "SELECT id AS family_id, log_id, type, date, para1, str1, str2 " +
            "FROM family_log ORDER BY date DESC");
        foreach (var r in rows)
            if (map.TryGetValue((int)r.family_id, out var fam))
                fam.Logs.Add(new FamilyLogEntry
                {
                    LogId = (int)r.log_id, Type = (short)r.type, Date = (int)r.date,
                    Para = (int)r.para1, Str1 = (string)(r.str1 ?? ""),
                    Str2 = (string)(r.str2 ?? ""),
                });
    }

    private static async Task LoadEmblemsAsync(NpgsqlConnection conn, Dictionary<int, Family> map)
    {
        // LoadFamilyEmblem : SELECT family_id, emblem, time FROM family_emblem.
        var rows = await conn.QueryAsync("SELECT family_id, emblem, time FROM family_emblem");
        foreach (var r in rows)
            if (map.TryGetValue((int)r.family_id, out var fam))
            {
                fam.EmblemData = r.emblem is string eb && eb.Length > 0
                    ? Convert.FromBase64String(eb) : null;
                fam.EmblemTime = (int)r.time;
            }
    }

    /// <summary>Ouvre une connexion edenfr_db et exécute le travail —
    /// chaque opération est autonome (les handlers monde n'ont pas de
    /// file de persistance par personnage : l'ordre est assuré par le
    /// verrou <see cref="FamilyService"/>).</summary>
    private async Task RunAsync(Func<NpgsqlConnection, Task> work)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await work(conn);
    }

    /// <summary>Crée une famille — <c>TC_CreateFamily</c> mission :
    /// <c>INSERT INTO family (id, name, leader_id, be_create_time, lv)</c>.
    /// L'id est alloué par MAX(id)+1 comme le natif.</summary>
    public async Task<int> CreateFamilyAsync(string name, int leaderId, int createTime)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(id),0)+1 FROM family", transaction: tx);
        await conn.ExecuteAsync(
            "INSERT INTO family (id, name, leader_id, be_create_time, lv) VALUES (@id, @name, @leaderId, @createTime, 1)",
            new { id, name, leaderId, createTime }, tx);
        await tx.CommitAsync();
        return id;
    }

    /// <summary>Affecte un membre — UPDATE natif
    /// <c>player_characters SET family_id=%d, add_family_time=%d</c>.</summary>
    public async Task JoinAsync(int charId, int familyId)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE player_characters SET family_id = @familyId, add_family_time = extract(epoch from now())::int " +
            "WHERE id = @charId", new { charId, familyId });
        });

    /// <summary>Retire un membre — UPDATE natif <c>family_id=0</c> +
    /// retrait du rang cadre et des marqueurs famille.</summary>
    public async Task LeaveAsync(int charId)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE player_characters SET family_id = 0, family_comment = '', family_contribution_point = 0, " +
            "family_msgboard_state = 0 WHERE id = @charId", new { charId });
        await conn.ExecuteAsync(
            "DELETE FROM family_cadre_members WHERE player_id = @charId", new { charId });
        });

    /// <summary>Nouveau chef — <c>family SET leader_id</c> + retrait
    /// du rang cadre du nouveau chef (RemoveFamilyMemberRank natif).</summary>
    public async Task SetLeaderAsync(int familyId, int leaderId)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET leader_id = @leaderId WHERE id = @familyId", new { familyId, leaderId });
        await conn.ExecuteAsync(
            "DELETE FROM family_cadre_members WHERE player_id = @leaderId", new { leaderId });
        });

    /// <summary>Dissolution — <c>family.delete_time</c> + sortie de
    /// tous les membres + DROP cadres (TC_DismissFamily natif).</summary>
    /// <summary>Dissolution — port de <c>CFamily::Disband</c> (mission
    /// @0x081b3e70) : <c>delete_time</c> posé sur family, reset des
    /// membres dans <c>player_characters</c>, puis suppression des 10
    /// tables filles dans l'ordre natif (cadre, msgboard, achievement,
    /// achievement_log, achievement_rank, storage, storage_log,
    /// statues_scenes, building_container, family_log).</summary>
    public async Task DisbandAsync(int familyId)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET delete_time = extract(epoch from now())::int WHERE id = @familyId",
            new { familyId });
        await conn.ExecuteAsync(
            "UPDATE player_characters SET family_id = 0, family_comment = '', family_contribution_point = 0, " +
            "family_msgboard_state = 0 WHERE family_id = @familyId", new { familyId });
        foreach (var t in new[]
            {
                "family_cadre_members",     // DeleteAllFamilyCadre
                "family_msgboard",          // DeleteAllFamilyScheduleMsg
                "family_achievement",       // DeleteAllFamilyAchievement
                "family_achievement_log",   // DeleteAllFamilyAchievementLog
                "family_achievement_rank",  // DeleteFamilyAchievementRank
                "family_storage",           // DeleteAllFamilyStorage
                "family_storage_log",       // DeleteAllFamilyStorageLog
                "statues_scenes",           // DeleteAllStatues
                "family_building_container",// DeleteAllFamilyBuildingContainr
            })
            await conn.ExecuteAsync(
                $"DELETE FROM {t} WHERE family_id = @familyId",
                new { familyId });
        // family_log : la colonne famille s'appelle « id ».
        await conn.ExecuteAsync(
            "DELETE FROM family_log WHERE id = @familyId",
            new { familyId });
        });

    /// <summary>Édition d'info publique — op 0xA (flag/info).</summary>
    public async Task EditInfoAsync(int familyId, string info)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET info = @info WHERE id = @familyId", new { familyId, info });
        });

    /// <summary>Titre de cadre — op 0xB (index 2..8 → cadre_title_N).</summary>
    public async Task SetCadreTitleAsync(int familyId, int index, string title)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            $"UPDATE family SET cadre_title_{index} = @title WHERE id = @familyId", new { familyId, title });
        });

    /// <summary>Rang de cadre — op 0xC : INSERT/DELETE
    /// <c>family_cadre_members(family_id, player_id, rank)</c>.</summary>
    public async Task SetCadreAsync(int familyId, int memberId, short rank)
        => await RunAsync(async conn =>
        {
            if (rank == 0)
            await conn.ExecuteAsync(
                "DELETE FROM family_cadre_members WHERE family_id = @familyId AND player_id = @memberId",
                new { familyId, memberId });
        else
            await conn.ExecuteAsync(
                "INSERT INTO family_cadre_members (family_id, player_id, rank) VALUES (@familyId, @memberId, @rank) " +
                "ON CONFLICT (family_id, player_id) DO UPDATE SET rank = @rank",
                new { familyId, memberId, rank });
        });

    /// <summary>Privilège — op 0xE (index 1..14 dans l'ordre du SELECT natif).</summary>
    public async Task SetPrivilegeAsync(int familyId, int index, int value)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            $"UPDATE family SET {PrivilegeColumns[index]} = @value WHERE id = @familyId",
            new { familyId, value });
        });

    /// <summary>Colonnes de privilège dans l'ordre natif
    /// (<c>FamilySetPrivilege</c> : 1=channel, 2=invite, 3=kickout,
    /// 4=editinfo, 5=setcadre, 6=invite25r, 7=fixequip, 8=msgboard,
    /// 9=delmsg, 10=treasury, 11=repair, 12-14=storage01..03).</summary>
    private static readonly string[] PrivilegeColumns =
    [
        "", "channel_privilege", "invite_privilege", "kickout_privilege", "editinfo_privilege",
        "setcadre_privilege", "invite25r_privilege", "fixequip_privilege", "msgboard_privilege",
        "delmsg_privilege", "treasury_privilege", "repair_privilege",
        "storage01_privilege", "storage02_privilege", "storage03_privilege",
    ];

    /// <summary>Message board — op 0xF (INSERT, id = MAX+1 par famille).</summary>
    public async Task<FamilyMessage> AddMessageAsync(int familyId, string title, string content, string playerName)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var id = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(message_id),0)+1 FROM family_msgboard WHERE family_id = @familyId",
            new { familyId });
        var createDate = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await conn.ExecuteAsync(
            "INSERT INTO family_msgboard (family_id, message_id, title, content, player_name, create_date) " +
            "VALUES (@familyId, @id, @title, @content, @playerName, @createDate)",
            new { familyId, id, title, content, playerName, createDate });
        return new FamilyMessage { MessageId = id, Title = title, Content = content, PlayerName = playerName, CreateDate = createDate };
    }

    /// <summary>Supprime un message du board (op 0x11).</summary>
    public async Task DeleteMessageAsync(int familyId, int messageId)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "DELETE FROM family_msgboard WHERE family_id = @familyId AND message_id = @messageId",
            new { familyId, messageId });
        });

    /// <summary>Op 0x12 — état de lecture des messages
    /// (<c>player_characters.family_msgboard_state</c>).</summary>
    public async Task SetMsgBoardStateAsync(int charId, int state)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE player_characters SET family_msgboard_state = @state WHERE id = @charId",
            new { charId, state });
        });

    /// <summary>Op 0x13 — commentaire famille d'un membre
    /// (<c>player_characters.family_comment</c>).</summary>
    public async Task SetMemberCommentAsync(int charId, string comment)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE player_characters SET family_comment = @comment WHERE id = @charId",
            new { charId, comment });
        });

    /// <summary>Op 0x14 — taxe (colonne tax_rate).</summary>
    public async Task SetTaxRateAsync(int familyId, int taxRate)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET tax_rate = @taxRate WHERE id = @familyId",
            new { familyId, taxRate });
        });

    /// <summary>Op 0x15 — taxe commerciale (colonne business_tax).</summary>
    public async Task SetBusinessTaxAsync(int familyId, int businessTax)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET business_tax = @businessTax WHERE id = @familyId",
            new { familyId, businessTax });
        });

    /// <summary>Op 0x16 — niveau + exp de la famille (u8/i32).</summary>
    public async Task SetLevelAsync(int familyId, int level, int exp)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET lv = @level, exp = @exp WHERE id = @familyId",
            new { familyId, level, exp });
        });

    /// <summary>Op 0x4E — état de recrutement (colonne recruit).</summary>
    public async Task SetRecruitAsync(int familyId, int recruit)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET recruit = @recruit WHERE id = @familyId",
            new { familyId, recruit });
        });

    /// <summary>Op 0x50 — message de recrutement.</summary>
    public async Task SetRecruitMessageAsync(int familyId, string message)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET recruit_message = @message WHERE id = @familyId",
            new { familyId, message });
        });

    /// <summary>Op 0x46 — blob d'emblème
    /// <c>family_emblem(family_id, emblem, time)</c>. La colonne est
    /// <c>text</c> et la base est en UTF-8 : le blob binaire y est
    /// stocké en base64 (le natif insérait sa std::string brute dans
    /// une base en encodage single-byte — détail de stockage interne,
    /// le fil vers le client reste le blob binaire exact).</summary>
    public async Task SetEmblemDataAsync(int familyId, byte[] emblem)
        => await RunAsync(async conn =>
        {
            var text = Convert.ToBase64String(emblem);
            await conn.ExecuteAsync(
            "INSERT INTO family_emblem (family_id, emblem, time) VALUES (@familyId, @emblem, extract(epoch from now())::int) " +
            "ON CONFLICT (family_id) DO UPDATE SET emblem = @emblem, time = extract(epoch from now())::int",
            new { familyId, emblem = text });
        });

    /// <summary>Crédite la trésorerie — don natif zone 159
    /// (TC FamilyDonateMoney : <c>treasury += money</c>, plafond
    /// <c>SFamily::MaxTreasuryValue</c> = 0x3b9ac9ff vérifié zone).
    /// Retourne la nouvelle trésorerie.</summary>
    public async Task<int> AddTreasuryAsync(int familyId, int amount)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "UPDATE family SET treasury = LEAST(treasury + @amount, 999999999) " +
            "WHERE id = @familyId RETURNING treasury",
            new { familyId, amount });
    }

    /// <summary>Index d'emblème — <c>family.emblem</c> (set_family_emblem).</summary>
    public async Task SetEmblemIndexAsync(int familyId, int emblem)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET emblem = @emblem WHERE id = @familyId", new { familyId, emblem });
        });

    /// <summary>Succès famille — port de <c>CFamilyDBO::UpdateAchievement</c>/
    /// <c>AddAchievement</c> (mission @0x08179790/0x08179830) : la table
    /// native n'a pas de PK, le natif fait UPDATE puis INSERT si absent.</summary>
    public async Task UpsertAchievementAsync(int familyId, short achievementType, uint point)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var updated = await conn.ExecuteAsync(
            "UPDATE family_achievement SET point = @point " +
            "WHERE family_id = @familyId AND achievement_type = @achievementType",
            new { familyId, achievementType, point = (int)point });
        if (updated == 0)
            await conn.ExecuteAsync(
                "INSERT INTO family_achievement (family_id, achievement_type, point) " +
                "VALUES (@familyId, @achievementType, @point)",
                new { familyId, achievementType, point = (int)point });
    }

    /// <summary>Points de sort famille — <c>CFamilyDBO::UpdateSpellPoint</c>
    /// (mission @0x0817f0xx : <c>UPDATE family SET spell_point</c>).</summary>
    public async Task SetSpellPointAsync(int familyId, short spellPoint)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET spell_point = @spellPoint WHERE id = @familyId",
            new { familyId, spellPoint });
        });

    /// <summary>Snapshot <c>family_achievement_rank</c> — port de
    /// <c>CFamilyDBO::UpdateAchievementRankData</c> (mission @0x0817e870).
    /// Le natif flushait via un event ~20 min sur dirty flag ; chez nous
    /// l'écriture est synchrone à chaque grant (même observable, pas de
    /// boucle d'événement à porter). UPDATE puis INSERT si absent —
    /// <c>LoadFamilyAchievementRankData</c> fait de même.</summary>
    public async Task SaveAchievementRankAsync(Family fam)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        long C(int key) => fam.Counters.GetValueOrDefault(key);
        var p = new
        {
            famId = fam.Id,
            name = fam.Name ?? "",
            lv = (int)fam.Level,
            now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            total = (int)Math.Min(fam.AchievementTotal, int.MaxValue),
            size = fam.Members.Count,
            monsterKill = (int)Math.Min(C(1), int.MaxValue),
            familyInstance = (int)Math.Min(C(5), int.MaxValue),
            lv01 = (int)Math.Min(C(0x65), int.MaxValue),
            lv31 = (int)Math.Min(C(0x66), int.MaxValue),
            lv46 = (int)Math.Min(C(0x67), int.MaxValue),
            lv61 = (int)Math.Min(C(0x68), int.MaxValue),
            lv76 = (int)Math.Min(C(0x69), int.MaxValue),
            lv91 = (int)Math.Min(C(0x6a), int.MaxValue),
            boss = (int)Math.Min(C(0x6b), int.MaxValue),
            rare = (int)Math.Min(C(0x6c), int.MaxValue),
            mine = (int)Math.Min(C(0x1f5), int.MaxValue),
            plant = (int)Math.Min(C(0x1f6), int.MaxValue),
            summon = (int)Math.Min(C(0x1f7), int.MaxValue),
            alchemy = (int)Math.Min(C(0x1f8), int.MaxValue),
            casting = (int)Math.Min(C(0x1f9), int.MaxValue),
            enchant = (int)Math.Min(C(0x1fa), int.MaxValue),
        };
        var updated = await conn.ExecuteAsync(
            "UPDATE family_achievement_rank SET family_lv=@lv, " +
            "family_name=@name, update_time=@now, total_point=@total, " +
            "memmber_size=@size, monster_kill=@monsterKill, " +
            "family_instance=@familyInstance, lv01_instance_node=@lv01, " +
            "lv31_instance_node=@lv31, lv46_instance_node=@lv46, " +
            "lv61_instance_node=@lv61, lv76_instance_node=@lv76, " +
            "lv91_instance_node=@lv91, family_boss=@boss, " +
            "rare_monster=@rare, family_mine=@mine, family_plant=@plant, " +
            "family_summon=@summon, family_alchemy=@alchemy, " +
            "family_casting=@casting, family_enchant=@enchant " +
            "WHERE family_id=@famId", p);
        if (updated == 0)
            await conn.ExecuteAsync(
                "INSERT INTO family_achievement_rank (family_id, family_name, " +
                "family_lv, update_time, total_point, monster_kill, " +
                "family_instance, memmber_size, lv01_instance_node, " +
                "lv31_instance_node, lv46_instance_node, lv61_instance_node, " +
                "lv76_instance_node, lv91_instance_node, family_boss, " +
                "rare_monster, family_mine, family_plant, family_summon, " +
                "family_alchemy, family_casting, family_enchant) VALUES " +
                "(@famId, @name, @lv, @now, @total, @monsterKill, " +
                "@familyInstance, @size, @lv01, @lv31, @lv46, @lv61, " +
                "@lv76, @lv91, @boss, @rare, @mine, @plant, @summon, " +
                "@alchemy, @casting, @enchant)", p);
    }

    /// <summary>Ouverture du hall de famille — SQL natif exact
    /// <c>CFamilyDBO::UpdateInstanceNodeID</c> (mission @0x8179430) :
    /// <c>UPDATE family SET house_open = %hd WHERE id = %d</c>
    /// (<paramref name="node"/> = 501/502).</summary>
    public async Task SetHouseOpenAsync(int familyId, short node)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET house_open = @node WHERE id = @familyId", new { familyId, node });
        });

    /// <summary><c>CFamilyDBO::UpdateRecycleTime</c> natif — horodatage
    /// du recyclage de la ville (<c>family.recycled_time</c>).</summary>
    public async Task SetRecycledTimeAsync(int familyId, int unixTime)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "UPDATE family SET recycled_time = @unixTime WHERE id = @familyId",
            new { familyId, unixTime });
        });

    /// <summary>Nombre de villes de famille ouvertes — équivalent du
    /// compteur global natif <c>_DAT_085b41f0</c> (cap 49 dans
    /// <c>CFamily::InstanceCreate</c> : refus 0x59c au-delà).</summary>
    public async Task<int> CountOpenTownsAsync()
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM family WHERE delete_time = 0 AND house_open <> 0");
    }

    /// <summary>Journal famille — <c>family_log</c>
    /// (log_type : 1 membre, 2 storage, 3 achievement natif).</summary>
    public async Task AddLogAsync(int familyId, short type, int para, string s1, string s2)
        => await RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
            "INSERT INTO family_log (id, type, log_id, date, para1, str1, str2) " +
            "VALUES (@familyId, @type, (SELECT COALESCE(MAX(log_id),0)+1 FROM family_log WHERE id = @familyId), " +
            "extract(epoch from now())::int, @para, @s1, @s2)",
            new { familyId, type, para, s1, s2 });
        });

    /// <summary>Charge un résumé membre depuis <c>player_characters</c>
    /// — utilisé par les ops invitation (la cible n'est pas en cache
    /// famille tant qu'elle n'a pas rejoint).</summary>
    public async Task<FamilyMemberSummary?> LoadMemberSummaryAsync(int charId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var c = await conn.QuerySingleOrDefaultAsync<CharRow>(
            "SELECT id, given_name, level, node_id, race_id, class_id, class_lv, gender_id, privilege, family_id, " +
            "add_family_time, CAST(EXTRACT(EPOCH FROM create_time) AS integer) AS create_time, " +
            "last_saving_time, self_comment, family_comment, family_msgboard_state, " +
            "family_contribution_point, deleted_time FROM player_characters WHERE id = @charId",
            new { charId });
        return c is null || c.deleted_time != 0 ? null : ToMember(c);
    }

    /// <summary>family_id actuel d'un perso en base — pour l'accept
    /// d'invitation quand le cache n'a pas encore la cible.</summary>
    public async Task<int> FamilyIdOfAsync(int charId)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(family_id,0) FROM player_characters WHERE id = @charId",
            new { charId });
    }

    /// <summary>Résout un charId par nom — <c>GetCharacterIDByName</c>
    /// natif (<c>SELECT id FROM player_characters WHERE given_name</c>).</summary>
    public async Task<int?> CharacterIdByNameAsync(string name)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int?>(
            "SELECT id FROM player_characters WHERE given_name = @name AND deleted_time = 0", new { name });
    }

    private static Family ToFamily(FamilyRow r)
    {
        var f = new Family
        {
            Id = r.id, Name = r.name ?? "", LeaderId = r.leader_id,
            Exploit = r.exploit, Level = (byte)r.lv, Exp = r.exp,
            Emblem = r.emblem, Uniform = r.uniform, Flag = r.flag,
            Info = r.info ?? "", Treasury = r.treasury,
            TaxRate = (short)r.tax_rate, BusinessTax = (short)r.business_tax,
            SpellPoint = (short)r.spell_point, ElfkingTid = r.elfking_tid,
            HouseOpen = (short)r.house_open, FamilyFlag = r.family_flag,
            BeCreateTime = r.be_create_time, RecycledTime = (uint)r.recycled_time,
            Recruit = (short)r.recruit, RecruitMessage = r.recruit_message ?? "",
            Honor = r.honor,
        };
        f.Titles[0] = r.member_title ?? ""; f.Titles[1] = r.leader_title ?? "";
        f.Titles[2] = r.cadre_title_2 ?? ""; f.Titles[3] = r.cadre_title_3 ?? "";
        f.Titles[4] = r.cadre_title_4 ?? ""; f.Titles[5] = r.cadre_title_5 ?? "";
        f.Titles[6] = r.cadre_title_6 ?? ""; f.Titles[7] = r.cadre_title_7 ?? "";
        f.Titles[8] = r.cadre_title_8 ?? "";
        f.Privileges[0] = r.channel_privilege; f.Privileges[1] = r.invite_privilege;
        f.Privileges[2] = r.kickout_privilege; f.Privileges[3] = r.editinfo_privilege;
        f.Privileges[4] = r.setcadre_privilege; f.Privileges[5] = r.invite25r_privilege;
        f.Privileges[6] = r.fixequip_privilege; f.Privileges[7] = r.msgboard_privilege;
        f.Privileges[8] = r.delmsg_privilege; f.Privileges[9] = r.treasury_privilege;
        f.Privileges[10] = r.repair_privilege; f.Privileges[11] = r.storage01_privilege;
        f.Privileges[12] = r.storage02_privilege; f.Privileges[13] = r.storage03_privilege;
        return f;
    }

    private static FamilyMemberSummary ToMember(CharRow c) => new()
    {
        CharacterId = c.id, Name = c.given_name ?? "", Level = (short)c.level,
        NodeId = (short)c.node_id, RaceId = (short)c.race_id, ClassId = (short)c.class_id,
        ClassLevel = (short)c.class_lv, Privilege = (short)c.privilege, Gender = (byte)c.gender_id,
        AddFamilyTime = c.add_family_time, CreateTime = c.create_time,
        LastSavingTime = c.last_saving_time, SelfComment = c.self_comment ?? "",
        FamilyComment = c.family_comment ?? "", MsgBoardState = c.family_msgboard_state,
        ContributionPoint = c.family_contribution_point,
    };

    private sealed class FamilyRow
    {
        public int id { get; set; } public string? name { get; set; } public int leader_id { get; set; } public int exploit { get; set; }
        public short lv { get; set; } public int exp { get; set; } public int emblem { get; set; } public int uniform { get; set; } public int flag { get; set; }
        public string? info { get; set; } public int treasury { get; set; } public int tax_rate { get; set; } public int business_tax { get; set; }
        public string? member_title { get; set; } public string? leader_title { get; set; }
        public string? cadre_title_2 { get; set; } public string? cadre_title_3 { get; set; } public string? cadre_title_4 { get; set; }
        public string? cadre_title_5 { get; set; } public string? cadre_title_6 { get; set; } public string? cadre_title_7 { get; set; } public string? cadre_title_8 { get; set; }
        public int channel_privilege { get; set; } public int invite_privilege { get; set; } public int kickout_privilege { get; set; }
        public int editinfo_privilege { get; set; } public int setcadre_privilege { get; set; } public int invite25r_privilege { get; set; }
        public int fixequip_privilege { get; set; } public int msgboard_privilege { get; set; } public int delmsg_privilege { get; set; }
        public int treasury_privilege { get; set; } public int repair_privilege { get; set; }
        public int storage01_privilege { get; set; } public int storage02_privilege { get; set; } public int storage03_privilege { get; set; }
        public int be_create_time { get; set; } public int spell_point { get; set; } public int elfking_tid { get; set; } public int house_open { get; set; }
        public long family_flag { get; set; } public int recruit { get; set; } public string? recruit_message { get; set; } public int honor { get; set; } public int recycled_time { get; set; }
    }

    private sealed class CharRow
    {
        public int id { get; set; } public string? given_name { get; set; } public int level { get; set; } public int node_id { get; set; }
        public int race_id { get; set; } public int class_id { get; set; } public int class_lv { get; set; } public int gender_id { get; set; }
        public int privilege { get; set; } public int family_id { get; set; } public int add_family_time { get; set; } public int create_time { get; set; }
        public int last_saving_time { get; set; } public string? self_comment { get; set; } public string? family_comment { get; set; }
        public int family_msgboard_state { get; set; } public int family_contribution_point { get; set; } public int deleted_time { get; set; }
    }

}
