using EdenClient.Net;

namespace EdenClient.Protocol;

// Extension Eden (hors protocole natif) — fenêtre « Statistiques
// avancées ». Wire défini côté serveur dans
// Eden.Zone/ZoneEntryFlow.AdvancedStats.cs (opcodes privés 32001/32002).

/// <summary>C→S 32001 — demande des stats avancées (corps vide).</summary>
public sealed class ZoneAdvStatsQuery : NetMessage
{
    public override int Opcode => 32001;
    public override string Name => "Eden.AdvStatsQuery";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C 32002 — formules (valeurs injectées) + sources de
/// multi-hit avec provenance.</summary>
public sealed class ZoneAdvStatsInfo : NetMessage
{
    public override int Opcode => 32002;
    public override string Name => "Eden.AdvStatsInfo";

    public sealed record Formula(string Key, string Expr, float Value,
        string Unit);
    /// <summary>Origin : 0 équipement, 1 statut actif (talent/certificat/
    /// buff), 2 proc déclenché (Gate = chance du proc).</summary>
    public sealed record Source(int EnchantId, byte Origin, int Stacks,
        float Gate, float C1, float F1, float C2, float F2);

    public byte MaxHits { get; set; }
    public float ChanceMult { get; set; } = 1;
    public float DamageMult { get; set; } = 1;
    public List<Formula> Formulas { get; } = new();
    public List<Source> Sources { get; } = new();

    public override void Serialize(BinWriter s) { }

    public override void Deserialize(BinReader s)
    {
        s.ReadByte();                       // version
        MaxHits = s.ReadByte();
        ChanceMult = s.ReadFloat();
        DamageMult = s.ReadFloat();
        var nf = s.ReadShort();
        for (var i = 0; i < nf; i++)
            Formulas.Add(new Formula(s.ReadString(), s.ReadString(),
                s.ReadFloat(), s.ReadString()));
        var ns = s.ReadShort();
        for (var i = 0; i < ns; i++)
            Sources.Add(new Source(s.ReadShort(), s.ReadByte(),
                s.ReadShort(), s.ReadFloat(), s.ReadFloat(), s.ReadFloat(),
                s.ReadFloat(), s.ReadFloat()));
    }
}
