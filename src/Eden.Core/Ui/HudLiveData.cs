using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Ui;

/// <summary>
/// Données réelles du HUD de zone — implémentation mutable de
/// <see cref="IHudData"/> alimentée par les trames réseau via
/// <c>HudLiveBridge</c> (Eden.Game) : identité/vitaux self (310, 280,
/// 296, 600 via <see cref="PlayerVitals"/>), nom de zone + bandeau
/// (313), chat (268/327/266/316/474 + monde 42/43), quêtes suivies
/// (348/391 — miroir allégé du QuestTracker de référence), buffs
/// (299/300/301/315/372/445/446 via <see cref="EntityBuffs"/>),
/// position minimap (313 puis <c>PlayerController</c>).
///
/// Mutations sur le main-thread uniquement (le bridge poste via
/// <c>EdenFlow.Post</c>) ; <see cref="Version"/> n'est incrémenté que
/// sur changement effectif — <see cref="HudHost"/> repolle.
///
/// Hors périmètre volontaire :
///   - <see cref="Target"/> : la sélection de cible appartient au
///     sous-système combat (NCID 444 + registre d'entités) ; la
///     propriété reste settable et notifie déjà Version.
///   - <see cref="Hotbar"/> : aucun décodeur de raccourcis en Core à
///     ce stade (EnterNodeShortcut non couvert) — liste vide.
/// </summary>
public sealed class LiveHudData : IHudData
{
    /// <summary>Cap de l'historique chat affiché (le natif borne le
    /// log — 100 lignes suffisent au panneau P0).</summary>
    public const int ChatCap = 100;

    private string _name = "";
    private int _level, _jobLevel;
    private readonly List<HudBuff> _buffs = new();
    private readonly List<HudQuestLine> _quests = new();
    private readonly List<HudChatLine> _chat = new();

    // --- suivi de quêtes (port minimal du QuestTracker référence) ---
    private readonly Dictionary<int, (string Title, string Goal)> _active = new();
    private readonly Dictionary<int, int> _fms = new();
    private readonly HashSet<int> _ready = new();
    private readonly Dictionary<uint, int> _counters = new();

    /// <summary>charId du personnage self (handle wire des buffs et
    /// du 280) — 0 avant réception du 310.</summary>
    public uint SelfCharId { get; private set; }

    public HudVitals? Vitals { get; private set; }

    private HudTarget? _target;

    /// <inheritdoc />
    public HudTarget? Target
    {
        get => _target;
        set
        {
            if (Equals(value, _target)) return;
            _target = value;
            Touch();
        }
    }

    /// <inheritdoc />
    public string ZoneName { get; private set; } = "";

    /// <inheritdoc />
    public (float X, float Y) Position { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<HudSlot> Hotbar { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<HudBuff> Buffs => _buffs;

    /// <inheritdoc />
    public IReadOnlyList<HudQuestLine> Quests => _quests;

    /// <inheritdoc />
    public IReadOnlyList<HudChatLine> Chat => _chat;

    /// <summary>Texte du bandeau — effacé par <see cref="ClearBanner"/>
    /// (le bridge le fait après la durée d'affichage du contrôle).</summary>
    public string? Banner { get; private set; }

    /// <inheritdoc />
    public int Version { get; private set; } = 1;

    private void Touch() => Version++;

    // ------------------------- identité / vitaux ------------------

    /// <summary>Pose l'identité du personnage (résumé monde ou 310) —
    /// les vitaux sont reconstruits par <see cref="RefreshVitals"/>.</summary>
    public void SetSelf(uint charId, string name, int level, int jobLevel)
    {
        if (SelfCharId == charId && _name == name &&
            _level == level && _jobLevel == jobLevel) return;
        SelfCharId = charId;
        _name = name;
        _level = level;
        _jobLevel = jobLevel;
        RefreshVitals();
    }

    /// <summary>310 : identité + vitaux + niveau de classe depuis le
    /// bloc décodé <see cref="PlayerHudData"/>.</summary>
    public void ApplyPlayerData(uint charId, string name, PlayerHudData pd)
    {
        SetSelf(charId, name, pd.Level, pd.ClassLevel);
        PlayerVitals.SetVitals(pd.Hp, pd.MaxHp, pd.Mp, pd.MaxMp);
        RefreshVitals();
    }

    /// <summary>Reconstruit <see cref="Vitals"/> depuis l'identité et
    /// l'état <see cref="PlayerVitals"/> (muté par les 280/296/600) —
    /// ExpMax via <see cref="LevelTable.ExpForNext"/> (0 si table
    /// absente : barre d'XP neutralisée).</summary>
    public void RefreshVitals()
    {
        var v = new HudVitals(_name, _level, _jobLevel,
            PlayerVitals.Hp, PlayerVitals.MaxHp,
            PlayerVitals.Mp, PlayerVitals.MaxMp,
            PlayerVitals.Exp, LevelTable.ExpForNext(_level) ?? 0);
        if (v == Vitals) return;        // égalité de record = delta effectif
        Vitals = v;
        Touch();
    }

    // ------------------------- zone / bandeau / position ----------

    /// <summary>Nom de zone résolu via <see cref="TextResolver.NodeName"/>
    /// (t_node.ini FR ; repli « S%03d ») + bandeau affiché au
    /// changement — même comportement que SetZone de la référence.</summary>
    public void SetZone(int nodeId)
    {
        var name = TextResolver.NodeName(nodeId) ?? $"S{nodeId:D3}";
        if (name != ZoneName)
        {
            ZoneName = name;
            Touch();
        }
        ShowBanner(name);
    }

    /// <summary>Arme le bandeau (bandeau zone, message centré 474,
    /// annonce 1524). Le bridge l'efface après la durée d'affichage.</summary>
    public void ShowBanner(string text)
    {
        if (Banner == text) return;
        Banner = text;
        Touch();
    }

    /// <summary>Fin de vie du bandeau (appel périodique du bridge).</summary>
    public void ClearBanner()
    {
        if (Banner == null) return;
        Banner = null;
        Touch();
    }

    /// <summary>Coords Eden (x, y) — bump uniquement sur déplacement.</summary>
    public void SetPosition(float x, float y)
    {
        if (Position.X == x && Position.Y == y) return;
        Position = (x, y);
        Touch();
    }

    // ------------------------- chat -------------------------------

    /// <summary>Ajoute une ligne — <paramref name="channel"/> = id
    /// wire (0 say…6 buzz, ≥7 = système), traduit en libellé.</summary>
    public void AddChat(int channel, string sender, string text)
    {
        _chat.Add(new HudChatLine(ChannelLabel(channel), sender, text));
        while (_chat.Count > ChatCap) _chat.RemoveAt(0);
        Touch();
    }

    /// <summary>Libellé du canal — table de ChatPanel.cs de référence
    /// { Say, Peer, Party, Guild, Zone, World, Buzz, système }.</summary>
    public static string ChannelLabel(int ch) => ch switch
    {
        0 => UiTextEn.Translate("Say"),
        1 => UiTextEn.Translate("Peer"),
        2 => UiTextEn.Translate("Party"),
        3 => UiTextEn.Translate("Guild"),
        4 => UiTextEn.Translate("Zone"),
        5 => UiTextEn.Translate("World"),
        6 => UiTextEn.Translate("Buzz"),
        _ => UiTextEn.Translate("System"),
    };

    // ------------------------- buffs -------------------------------

    /// <summary>Reconstruit la liste self depuis
    /// <see cref="EntityBuffs.Snapshot"/> — noms via
    /// <see cref="EnchantTable.NameOf"/>, classification debuff via
    /// <c>IsBuff</c> (inconnu = buff, repli sûr). −1 s = permanent.</summary>
    public void RefreshBuffs(int selfHandle)
    {
        _buffs.Clear();
        foreach (var b in EntityBuffs.Snapshot(selfHandle))
        {
            var def = EnchantTable.Get(b.EffectId);
            var rem = b.Remaining;
            _buffs.Add(new HudBuff(
                EnchantTable.NameOf(b.EffectId), null,
                rem < 0 ? -1 : (int)Math.Ceiling(rem),
                !(def?.IsBuff ?? true)));
        }
        Touch();
    }

    // ------------------------- quêtes ------------------------------

    /// <summary>348 : registre complet — les slots Trace portent les
    /// quêtes suivies ; repli = quêtes en cours (FMS 3/4). Les quêtes
    /// terminées (FMS 1/5) ne s'affichent plus même si un slot
    /// traîne.</summary>
    public void SetMissions(ZoneMissionStates m)
    {
        _fms.Clear();
        foreach (var (mid, st) in m.Fms) _fms[mid] = st;
        _ready.Clear();
        foreach (var (mid, st) in m.Fms)
            if (st == 4) _ready.Add(mid);
        _active.Clear();
        bool Live(int mid) => _fms.GetValueOrDefault(mid) is not (1 or 5);
        foreach (var mid in m.Trace)
            if (mid > 0 && Live(mid)) _active[mid] = MissionText(mid);
        if (_active.Count == 0)
            foreach (var (mid, _) in m.InProgress)
                if (Live(mid)) _active[mid] = MissionText(mid);
        RefreshQuests();
    }

    /// <summary>391 : kind=0 = état FMS (Key=mid, MissionId=état) ;
    /// kind=1 = compteur d'objectif (Key=pmsKey, MissionId=compte).</summary>
    public void UpdateMission(ZoneUpdateMissionState m)
    {
        if (m.Op == 1)
        {
            _counters[m.Key] = m.MissionId;
            var (mid2, _, _) = MissionObjectives.DecodePmsKey(m.Key);
            // Chaque avancement d'objectif : bandeau centré (réf. HUD).
            if (MissionObjectives.ProgressLine(m.Key, m.MissionId)
                is { } pl)
                ShowBanner(pl);
            if (mid2 > 0 && !_active.ContainsKey(mid2))
                _active[mid2] = MissionText(mid2);
            RefreshQuests();
            return;
        }
        var mid = (int)m.Key;
        if (mid == 0) return;
        var fms = m.MissionId;
        if (fms == 0) _fms.Remove(mid); else _fms[mid] = fms;
        if (fms is 1 or 5 or 0)              // done / checked / reset
        {
            _ready.Remove(mid);
            _active.Remove(mid);
        }
        else
        {
            if (fms == 4) _ready.Add(mid);
            if (!_active.ContainsKey(mid))
                _active[mid] = MissionText(mid);
        }
        RefreshQuests();
    }

    /// <summary>Titre + objectif localisés (t_mission.ini ; repli
    /// « Quest #id » comme la référence).</summary>
    private static (string, string) MissionText(int mid) => (
        TextResolver.MissionTitle(mid) ?? UiTextEn.Translate($"Quest #{mid}"),
        TextResolver.MissionDescription(mid)
            ?? TextResolver.MissionZone(mid) ?? "");

    /// <summary>Ligne d'objectif unique du panneau P0 : premier
    /// objectif non rempli « label n/req » ; quête prête → ligne de
    /// rendu native ; sans objectif décodé → description/zone.</summary>
    private string ObjectiveLine(int mid, string goal)
    {
        var def = MissionTable.Get(mid);
        var objs = MissionObjectives.ForMission(mid);
        var prog = new List<(int P, MissionObjectives.Objective O)>(objs.Count);
        foreach (var o in objs)
        {
            var kind = MissionObjectives.DecodePmsKey(o.PmsKey).Type;
            // owned=0 — l'inventaire HUD n'est pas encore branché (P0).
            prog.Add((QuestObjectiveLogic.Progress(kind,
                _counters.GetValueOrDefault(o.PmsKey), o.ItemId, 0, o.Req), o));
        }
        var done = prog.Count > 0 && prog.TrueForAll(x => x.P >= x.O.Req);
        if (_ready.Contains(mid) || done)
            return MissionObjectives.ReturnLine(def?.SubmitNpc ?? 0);
        foreach (var (p, o) in prog)
        {
            if (p >= o.Req) continue;
            var kind = MissionObjectives.DecodePmsKey(o.PmsKey).Type;
            return QuestObjectiveLogic.ShowsCount(kind)
                ? $"{o.Label} {p}/{o.Req}" : o.Label;
        }
        return goal;
    }

    private void RefreshQuests()
    {
        _quests.Clear();
        foreach (var (mid, (title, goal)) in _active)
            _quests.Add(new HudQuestLine(title, ObjectiveLine(mid, goal)));
        Touch();
    }

    // ------------------------- session -----------------------------

    /// <summary>Remise à zéro à la déconnexion (la session repart
    /// d'un état vide — identité et file de chat incluses).</summary>
    public void Reset()
    {
        SelfCharId = 0;
        _name = ""; _level = 0; _jobLevel = 0;
        Vitals = null;
        _target = null;
        ZoneName = "";
        Position = (0, 0);
        Banner = null;
        _buffs.Clear();
        _chat.Clear();
        _quests.Clear();
        _active.Clear(); _fms.Clear(); _ready.Clear(); _counters.Clear();
        Touch();
    }
}
