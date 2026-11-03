using System.Text;

namespace EdenClient.Ui.P2;

/// <summary>Logique pure des petites fenêtres P2 (loterie, hôtel,
/// champ de bataille, inspection, services, jet de butin, barre
/// ultime, trait de héros, costumes) — portée de
/// <c>scripts/hud/*.cs</c> Godot, textes EDENFR (FR seul, aucune
/// traduction EN→FR au runtime).</summary>
public static class Lottery
{
    /// <summary>Autels (nom, TicketRequestId) — verbatim LotteryService.</summary>
    public static readonly (string Nom, int Ticket)[] Altars =
    {
        ("Autel normal", 0x9d31),
        ("Loterie en jeu", 0x66ac),
        ("Loterie diamant", 0x6c91),
    };

    public static string UpdateText(int state, int a, int b, int c, int d, int e) =>
        $"État {state} — {a}/{b}/{c} {d} {e}";

    public static string ResultText(int f1, int f2, int f3, int f4, int f5, int f6) =>
        $"Résultat : {f1} {f2} {f3} {f4} {f5} {f6}";

    public const string EndText = "Tirage terminé.";
}

/// <summary>Salles d'hôtel.</summary>
public static class Hotel
{
    public static string RoomLine(string name, int members, int max) =>
        $"{name} — {members}/{max}";

    public static string RoomInfo(int f1, int f2) => $"Chambre {f1} — {f2}…";

    /// <summary>Id de la salle sélectionnée, -1 si hors bornes.</summary>
    public static int RoomAt(IReadOnlyList<int> ids, int selected) =>
        selected >= 0 && selected < ids.Count ? ids[selected] : -1;
}

/// <summary>Champs de bataille.</summary>
public static class Battlefield
{
    public static string ListText(int f1, int f2, int f3, int f4, int f5, int f6) =>
        $"Champs : {f1} {f2} {f3} {f4} {f5} {f6}";

    public static string OfferText(int bf) => $"Entrée proposée (champ {bf})";

    public static string QueueText(int f1, int f2, int f3, int inst) =>
        $"File : {f1}/{f2}/{f3} inst={inst}";

    public static string ResultText(int outcome, int f1) =>
        $"Résultat : {outcome} ({f1})";

    public static string ScoreText(int f1, int f2, int f3, int f4) =>
        $"Score : {f1} {f2} {f3} {f4}";

    /// <summary>Parse « bfId » / « camp » saisis ; 0 si invalide.</summary>
    public static short ParseId(string? s) =>
        short.TryParse(s, out var v) ? v : (short)0;
}

/// <summary>Inspection d'équipement (ZoneClientLookEquip / PVP rank).</summary>
public static class Inspect
{
    public static string Title(int charId) => $"Équipement de {charId}";
    public static string PvpTitle(string name) => $"Équipement PvP — {name}";
    public static string ItemLine(int uid, string? name) =>
        name is { Length: > 0 } ? name : $"Objet n°{uid}";
}

/// <summary>Panneaux de service (Territoire, Colisée, Recrutement,
/// Tenues rapides, Classements).</summary>
public static class ServicePanels
{
    public const string TerritoryTitle = "Territoires";
    public const string ColosseumTitle = "Colisée";
    public const string RecruitTitle = "Recrutement d'instance";
    public const string FastDressTitle = "Tenues rapides";
    public const string RankTitle = "Classements";

    public static string Territory(int a, int b, int c, int d) =>
        $"Territoires : {a} {b} {c} {d}";
    public static string TerritoryInfo(int a, int b, int c, int d) =>
        $"Info : {a} {b} {c} {d}";
    public const string TerritoryEnd = "Guerre de territoire terminée.";
    public static string TerritoryKill(int v) => $"Frag territoire : {v}";

    public static string ColosseumEntry(int v) =>
        $"Entrée au Colisée ({v}) — rejoignez la file.";
    public static string ColosseumJoin(int v) => $"Colisée, inscription : {v}";
    public static string ColosseumTeam(int a, int b, int c) =>
        $"Équipe : {a} {b} {c}";
    public static string GladiatorTeam(int a, int b, int c) =>
        $"Gladiateurs : {a} {b} {c}";
    public const string GladiatorDisband = "Équipe de gladiateurs dissoute.";

    public static string InstanceDialog(int a, int b) =>
        $"Création d'instance : {a}/{b}";
    public static string Recruit(int a, int b) => $"Recrutement : {a}/{b}";

    public static string FastDress(int a, int b, int c, int d) =>
        $"Tenue {a}/{b} — {c} {d}";

    public const string ExploitRank = "Classement des exploits.";
    public static string Rank(int rank, int a, int c, int d) =>
        $"Rang : {rank} — {a}/{c} {d}";
    public static string ExploitRankQuery(int a, int b, int c) =>
        $"Rang d'exploit : {a} {b} {c}";

    public static string FamilyKills(
        IEnumerable<(int Rank, string Player, string Family, int Kills)> es)
    {
        var sb = new StringBuilder("Frags famille :\n");
        foreach (var e in es)
            sb.Append(e.Rank).Append(". ").Append(e.Player)
              .Append(" (").Append(e.Family).Append(") — ")
              .Append(e.Kills).Append('\n');
        return sb.ToString();
    }
}

/// <summary>Jet de butin (Need/Greed/Pass).</summary>
public sealed class RollState
{
    public uint TreasureId { get; private set; }
    public bool Open { get; private set; }

    public void Offer(uint id) { TreasureId = id; Open = true; }

    /// <summary>Choix 0 = passer, 1 = besoin, 2 = convoitise ; ferme.
    /// Retourne (treasureId, choix) à envoyer, null si rien d'ouvert.</summary>
    public (uint Id, short Choice)? Choose(short choice)
    {
        if (!Open) return null;
        Open = false;
        return (TreasureId, choice);
    }
}

/// <summary>Barre ultime (limitless).</summary>
public static class UltimateBar
{
    public static string Text(int level, long exp, long next, int perks) =>
        $"ULTIME  Niv {level}   EXP {exp}/{next}   Atouts {perks}";

    public const string Placeholder = "ULTIME  Niv --";

    public static double Fraction(long exp, long next) =>
        next > 0 ? Math.Clamp((double)exp / next, 0, 1) : 0;
}

/// <summary>Trait de héros (1..8, 0 = aucun).</summary>
public sealed class HeroTraitState
{
    public int Current { get; private set; }
    public event Action? Changed;

    public void Set(int trait)
    {
        trait = trait is >= 1 and <= 8 ? trait : 0;
        if (trait == Current) return;
        Current = trait;
        Changed?.Invoke();
    }

    /// <summary>Chemin d'icône uidata (null si aucun trait).</summary>
    public string? IconPath => Current == 0 ? null
        : $"ui/uiicon/NatrueIamge{Current:00}";
}

/// <summary>Arrivée de portail : le HUD attend l'arrivée (Begin → Arrived).</summary>
public sealed class PortalArrivalState
{
    public bool Pending { get; private set; }
    public int Version { get; private set; }
    public int CurrentNode { get; set; }

    public void Begin() => Pending = true;
    public void Arrived() { Version++; Pending = false; }
}

/// <summary>Costumes : 3 pages (conteneurs 2..4), 5 emplacements.</summary>
public static class Costume
{
    public static readonly (int Loc, string Label)[] Locs =
    {
        (1, "Tête"), (2, "Corps"), (3, "Jambes"), (4, "Mains"), (5, "Pieds"),
    };

    public const int FirstPage = 2, PageCount = 3;

    /// <summary>Type d'item accepté par la page (Kind % 3).</summary>
    public static int PageMod(int container) =>
        container == 2 ? 0 : container == 3 ? 2 : 1;

    /// <summary>L'item (emplacement équipement, Kind) peut-il aller sur
    /// (page, emplacement) ?</summary>
    public static bool Accepts(int page, int loc, int itemEquipLoc, int itemKind) =>
        itemEquipLoc == loc && itemKind % 3 == PageMod(page);
}
