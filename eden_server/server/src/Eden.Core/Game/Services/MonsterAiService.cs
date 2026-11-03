using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// IA des monstres — correspond a CAIFSM* + CMonster::RunAI.
/// Utilise les profils S_Ai.ini (707 entrees):
///   col 1 = portee d'aggro, col 2 = portee d'assistance,
///   col 4 = chance d'utiliser un sort par tick,
///   cols 5-9 = probabilites par type d'action,
///   cols 18-23 = timers/delais.
/// Gere la hate (aggro) par cible et le taunt.
/// </summary>
public sealed class MonsterAiService
{
    private readonly Dictionary<int, ProgressionParsers.AiProfile> _profiles;
    private readonly Dictionary<long, AggroTable> _aggro = new();

    /// <summary>Table de hate d'un monstre: cible → points de hate.</summary>
    public sealed class AggroTable
    {
        public Dictionary<int, int> Threat { get; } = new();
        public int TauntedBy { get; set; }
        public DateTime TauntExpires { get; set; }

        /// <summary>Cible prioritaire (taunt force, sinon hate max).</summary>
        public int CurrentTarget(DateTime now)
        {
            if (TauntedBy > 0 && now < TauntExpires)
                return TauntedBy;
            return Threat.Count == 0 ? 0
                : Threat.OrderByDescending(kv => kv.Value).First().Key;
        }
    }

    public MonsterAiService(
        IEnumerable<ProgressionParsers.AiProfile> profiles)
        => _profiles = profiles.ToDictionary(p => p.Id);

    public static MonsterAiService LoadFromDataDir(string decodedDir)
        => new(ProgressionParsers.ParseAiProfiles(
            Path.Combine(decodedDir, "S_Ai.ini")));

    public ProgressionParsers.AiProfile? GetProfile(int aiId)
        => _profiles.TryGetValue(aiId, out var p) ? p : null;

    /// <summary>
    /// Ajoute de la hate — param 43: degats = +10 hate par point.
    /// </summary>
    public void AddDamageThreat(long monsterInstanceId, int charId,
        int damage)
    {
        var t = TableFor(monsterInstanceId);
        t.Threat[charId] = t.Threat.GetValueOrDefault(charId) +
            damage * 10 / 10; // param 43 = 10
    }

    /// <summary>
    /// Ajoute de la hate par heal — param 44: heal = ×1.2 hate.
    /// </summary>
    public void AddHealThreat(long monsterInstanceId, int charId,
        int healAmount)
    {
        var t = TableFor(monsterInstanceId);
        t.Threat[charId] = t.Threat.GetValueOrDefault(charId) +
            (int)(healAmount * 1.2);
    }

    /// <summary>
    /// Taunt — force la cible pendant duration secondes.
    /// </summary>
    public void Taunt(long monsterInstanceId, int charId,
        int durationSeconds = 5)
    {
        var t = TableFor(monsterInstanceId);
        t.TauntedBy = charId;
        t.TauntExpires = DateTime.UtcNow.AddSeconds(durationSeconds);
    }

    /// <summary>Cible actuelle du monstre.</summary>
    public int CurrentTarget(long monsterInstanceId)
        => _aggro.TryGetValue(monsterInstanceId, out var t)
            ? t.CurrentTarget(DateTime.UtcNow) : 0;

    /// <summary>Roll natif de détection passive — `CAI::DoSeekEnemy`
    /// (zone @0x84d5xxx) : le natif fait `rand(100) &lt;= CAiData+0x05`,
    /// l'octet +5 étant S_Ai col2 (<see cref="ProgressionParsers.
    /// AiProfile.SeekChancePercent"/>). On porte en `&lt;` strict :
    /// la sémantique voulue d'une chance de 0 (ex. ai=3) est « jamais »
    /// — le `&lt;=` natif laissait 1 % d'aggro parasite sur roll==0.
    /// 100 (ai=2) cherche à chaque tick de ~2 s.</summary>
    public bool RollPassiveSeek(int aiId)
    {
        var profile = GetProfile(aiId);
        return profile != null &&
            Random.Shared.Next(100) < profile.SeekChancePercent;
    }

    /// <summary>
    /// Chance d'utiliser un sort a ce tick (col 4 du profil).
    /// </summary>
    public bool RollSkillUse(int aiId)
    {
        var profile = GetProfile(aiId);
        return profile != null &&
            Random.Shared.Next(100) < profile.SkillChance;
    }

    public void ClearThreat(long monsterInstanceId)
        => _aggro.Remove(monsterInstanceId);

    private AggroTable TableFor(long instanceId)
        => _aggro.TryGetValue(instanceId, out var t)
            ? t : _aggro[instanceId] = new AggroTable();
}
