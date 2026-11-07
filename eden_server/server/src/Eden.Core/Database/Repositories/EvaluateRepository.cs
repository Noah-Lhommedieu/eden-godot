using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Dépôt des tables d'évaluation « gladiator trial » :
/// <c>player_evaluate</c> (meilleur record par joueur×nœud, PK
/// (player_id,node_id)) et <c>team_evaluate</c> (historique des runs
/// d'équipe — 1 INSERT par End, alimente le rang 600). Sémantique
/// des commandes DBA natives :
/// <c>UpdatePlayerEvaluateInfo</c> (mission @0x823fxxx) : INSERT si
/// absent, UPDATE si <c>score &gt; ancien || (score == ancien &amp;&amp;
/// time &lt; ancien)</c> ; <c>UpdateEvaluateRank</c> (@0x81976a0) :
/// INSERT systématique avec leader_id + team_mem_id1..10 (charIds
/// paddés '0'). <c>ClearPlayerEvaluate</c> (@0x81eb2c0) : mémoire
/// seule (+0x688 execData) — pas de DB.</summary>
public sealed class EvaluateRepository
{
    private readonly NpgsqlDataSource _db;

    public EvaluateRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Record perso (node_id → score/time/grade) — map
    /// native char+0xcd4-adjacent chargée au login par le SELECT
    /// <c>player_evaluate WHERE player_id</c> (mission @0x825f4xx)
    /// et émise en 608 <c>ClientUpdateEvaluateInfo</c> par entrée.</summary>
    public sealed record BestRecord(int NodeId, int Score, int Time,
        string Grade);

    /// <summary>Meilleurs records du joueur sur tous les nœuds
    /// d'évaluation (émission 608 à l'entrée en zone).</summary>
    public async Task<IReadOnlyList<BestRecord>> GetBestRecordsAsync(
        int characterId)
    {
        await using var c = _db.CreateConnection();
        var rows = await c.QueryAsync<BestRecord>(
            @"SELECT node_id AS NodeId, score AS Score,
                     time AS Time, evaluate AS Grade
              FROM player_evaluate WHERE player_id = @characterId",
            new { characterId });
        return rows.AsList();
    }

    /// <summary><c>UpdatePlayerEvaluateInfo</c> — upsert best-record :
    /// INSERT si absent, UPDATE seulement si meilleur (score &gt;, ou
    /// score = avec meilleur temps) — la clause WHERE reproduit la
    /// condition native.</summary>
    public async Task UpdatePlayerEvaluateAsync(int characterId,
        int nodeId, int score, int time, string grade)
    {
        await using var c = _db.CreateConnection();
        var updated = await c.ExecuteAsync(
            @"UPDATE player_evaluate
              SET score = @score, time = @time, evaluate = @grade
              WHERE player_id = @characterId AND node_id = @nodeId
                AND (score < @score
                     OR (score = @score AND time > @time))",
            new { characterId, nodeId, score, time, grade });
        if (updated == 0)
            await c.ExecuteAsync(
                @"INSERT INTO player_evaluate
                    (player_id, node_id, score, time, evaluate)
                  VALUES (@characterId, @nodeId, @score, @time,
                          @grade)
                  ON CONFLICT (player_id, node_id) DO NOTHING",
                new { characterId, nodeId, score, time, grade });
    }

    /// <summary><c>UpdateEvaluateRank</c> — INSERT historique de la
    /// run d'équipe ; team_mem_idN = charIds sous forme texte
    /// (le natif sprintf %u dans les quotes).</summary>
    public async Task InsertTeamEvaluateAsync(int nodeId, int score,
        int time, string grade, int leaderId,
        IReadOnlyList<int> memberCharIds)
    {
        var mem = new string[10];
        for (var i = 0; i < 10; i++)
            mem[i] = i < memberCharIds.Count
                ? memberCharIds[i].ToString() : "0";
        await using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            @"INSERT INTO team_evaluate
                (leader_id, node_id, score, time, evaluate,
                 team_mem_id1, team_mem_id2, team_mem_id3,
                 team_mem_id4, team_mem_id5, team_mem_id6,
                 team_mem_id7, team_mem_id8, team_mem_id9,
                 team_mem_id10)
              VALUES (@leaderId, @nodeId, @score, @time, @grade,
                 @m0, @m1, @m2, @m3, @m4, @m5, @m6, @m7, @m8, @m9)",
            new
            {
                leaderId, nodeId, score, time, grade,
                m0 = mem[0], m1 = mem[1], m2 = mem[2], m3 = mem[3],
                m4 = mem[4], m5 = mem[5], m6 = mem[6], m7 = mem[7],
                m8 = mem[8], m9 = mem[9],
            });
    }

    /// <summary>Compteurs d'entrées journalières du joueur — map
    /// native char+0xcd0 (arbres de CNodeTimes) persistée en
    /// <c>player_node_times</c> par le DBA <c>UpdateNodeTimes</c>
    /// (AddNodeTimes zone @0x841e9a0).</summary>
    public async Task<Dictionary<int, int>> GetNodeTimesAsync(
        int characterId)
    {
        await using var c = _db.CreateConnection();
        var rows = await c.QueryAsync<(int NodeId, int Times)>(
            @"SELECT node_id AS NodeId, node_times AS Times
              FROM player_node_times WHERE player_id = @characterId",
            new { characterId });
        return rows.ToDictionary(r => r.NodeId, r => r.Times);
    }

    /// <summary><c>UpdateNodeTimes</c> natif — upsert du compteur
    /// (le natif envoie la paire par nœud à chaque AddNodeTimes ;
    /// le reset journalier est géré ailleurs — les compteurs
    /// s'accumulent sur la ligne tant qu'aucun reset n'existe).</summary>
    public async Task SetNodeTimesAsync(int characterId, int nodeId,
        int times)
    {
        await using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            @"INSERT INTO player_node_times
                (player_id, node_id, node_times)
              VALUES (@characterId, @nodeId, @times)
              ON CONFLICT (player_id, node_id)
              DO UPDATE SET node_times = @times",
            new { characterId, nodeId, times });
    }

    /// <summary>Entrées de donjon du jour pour un node (0 si le compteur
    /// date d'un autre jour).</summary>
    public async Task<int> GetDungeonEntriesAsync(int characterId,
        int nodeId, int today)
    {
        await using var c = _db.CreateConnection();
        return await c.ExecuteScalarAsync<int>(
            @"SELECT COALESCE(MAX(times), 0) FROM player_dungeon_entries
              WHERE player_id = @characterId AND node_id = @nodeId
                AND day = @today",
            new { characterId, nodeId, today });
    }

    /// <summary>+1 entrée du jour ; remet à 1 si le compteur est d'un
    /// jour passé. Retourne le nouveau total du jour.</summary>
    public async Task<int> AddDungeonEntryAsync(int characterId,
        int nodeId, int today)
    {
        await using var c = _db.CreateConnection();
        return await c.ExecuteScalarAsync<int>(
            @"INSERT INTO player_dungeon_entries
                (player_id, node_id, day, times)
              VALUES (@characterId, @nodeId, @today, 1)
              ON CONFLICT (player_id, node_id) DO UPDATE SET
                times = CASE WHEN player_dungeon_entries.day = @today
                             THEN player_dungeon_entries.times + 1
                             ELSE 1 END,
                day = @today
              RETURNING times",
            new { characterId, nodeId, today });
    }

    /// <summary>Devises colosseum du perso — execData+0x68c/+0x690
    /// natifs poussés dans le 525 ClientGladiatorTeamInfo à
    /// l'assignation d'un evalId.</summary>
    public async Task<(int Point, int Gold)> GetGladiatorCurrenciesAsync(
        int characterId)
    {
        await using var c = _db.CreateConnection();
        var row = await c.QueryFirstOrDefaultAsync<(int Point, int Gold)>(
            @"SELECT colosseum_point AS Point, colosseum_gold AS Gold
              FROM player_characters WHERE id = @characterId",
            new { characterId });
        return row;
    }

    /// <summary><c>CGlobalInfo::SendMail</c> natif (mission
    /// @0x8082880) pour les récompenses de trial : INSERT
    /// <c>player_mail</c> (sender 8148 « Shaxia Oracle », title 8146,
    /// body 8147) + une ligne <c>mailitem</c> complète par item —
    /// la contrainte UNIQUE (player_id, mail_id) impose un mail par
    /// item, comme le natif qui boucle SendRewardMail par reward.
    /// <paramref name="durability"/> porte la quantité (le natif
    /// écrit le count du SEVRewardItem dans durability).</summary>
    public async Task InsertRewardMailAsync(int characterId,
        int itemId, int durability, string sender, string title,
        string body)
    {
        await using var c = _db.CreateConnection();
        var mailId = await MailIdAllocator.NextAsync(_db);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await c.ExecuteAsync(
            @"INSERT INTO player_mail
                (id, receiver_id, sender_id, sender_name, send_time,
                 due_date, title, content, item_id, gold, crystal,
                 cod, opened, returned, authoritative)
              VALUES (@mailId, @characterId, 0, @sender, @now,
                 0, @title, @body, @itemId, 0, 0, 0, 0, 0, 0)",
            new { mailId, characterId, sender, now, title, body,
                  itemId });
        var serial = await ItemSerialAllocator.NextAsync(_db);
        await c.ExecuteAsync(
            @"INSERT INTO mailitem
                (id, item_id, durability, player_id, mail_id,
                 create_time)
              VALUES (@serial, @itemId, @durability, @characterId,
                      @mailId, @now)",
            new { serial, itemId, durability, characterId, mailId,
                  now });
    }
}
