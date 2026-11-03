using System.Buffers.Binary;
using System.Text;
using EdenClient.Vfs;

namespace EdenClient.World;

/// <summary>Type de transition KFM (<c>NiKFMTool::TransitionType</c>).
/// Valeurs 0..3 recoupées avec les défauts du header (sync = 1,
/// non-sync = 0) ; 4 et 5 = « utiliser le défaut » — hypothèse : le
/// corpus entier (1,8 M transitions) n'emploie que le type 5.</summary>
public enum KfmTransitionType
{
    Blend = 0, Morph = 1, Crossfade = 2, Chain = 3,
    DefaultSync = 4, DefaultNonSync = 5,
}

/// <summary>Paire de clés de synchronisation (blend/morph/crossfade).</summary>
public readonly record struct KfmKeyPair(int Id, string Key);

/// <summary>Clé texte d'enchaînement (type chaîne).</summary>
public readonly record struct KfmChainKey(float Time, string Key);

/// <summary>Transition d'une anim KFM vers une autre : <c>Target</c> est
/// un <b>id d'événement</b> (pas un index). Type 5 = défaut non-sync du
/// fichier, sans données de durée ; sinon durée + paires.</summary>
public sealed record KfmTransition(int Target, int Type, float Duration)
{
    /// <summary>Paires de synchronisation (types ≠ 5, absentes du corpus).</summary>
    public List<KfmKeyPair> SyncPairs { get; init; } = new();
    /// <summary>Clés de chaîne (types ≠ 5, absentes du corpus).</summary>
    public List<KfmChainKey> ChainKeys { get; init; } = new();

    public KfmTransitionType Kind => (KfmTransitionType)Type;
}

/// <summary>Une action KFM : id d'événement (101 = marche avant,
/// 1 = stand01…), fichier .kf et index de séquence dans ce fichier.</summary>
public sealed class KfmAnim
{
    public int Event;
    public string KfPath = "";
    public int Index;
    public List<KfmTransition> Transitions { get; } = new();

    /// <summary>Transition vers l'événement <paramref name="target"/>
    /// ou null (→ défauts du fichier).</summary>
    public KfmTransition? To(int target)
    {
        foreach (var t in Transitions)
            if (t.Target == target) return t;
        return null;
    }
}

/// <summary>
/// Lecteur <c>.kfm</c> (Gamebryo KFM 2.2.0.0b) : manifeste d'actions →
/// séquences + transitions. Port tolérant de <c>read_kfm</c>
/// (<c>tools/nif2gltf/convert.py</c>) : les KFM corpus ont des noms
/// légèrement corrompus → resynchronisation par décalages 0..2.
/// Mesuré sur les 856 KFM de fr-full : 0 tronqué, transitions toutes de
/// type 5 (matrice complète N×(N−1) sans durée) → seuls les défauts du
/// header comptent (sync = type 1 / non-sync = type 0, 0,1 s).
/// </summary>
public sealed class KfmFile
{
    public string Header = "";
    public string NifName = "";
    public string Master = "";
    /// <summary>Défauts du header : type et durée (s) des transitions
    /// sync / non-sync.</summary>
    public int DefaultSyncType, DefaultNonSyncType;
    public float DefaultSyncDuration, DefaultNonSyncDuration;
    /// <summary>Vrai si le fichier s'arrête avant le nombre déclaré.</summary>
    public bool Truncated;
    /// <summary>Diagnostics de lecture (resynchronisations, troncature).</summary>
    public List<string> Warnings { get; } = new();
    public List<KfmAnim> Anims { get; } = new();
    private readonly Dictionary<int, KfmAnim> _byEvent = new();

    /// <summary>Action par id d'événement (101..104, 1…) ou null.</summary>
    public KfmAnim? ByEvent(int ev) =>
        _byEvent.TryGetValue(ev, out var a) ? a : null;

    /// <summary>Ajoute une action (aussi pour fabriquer un KFM
    /// synthétique en test).</summary>
    public void Add(KfmAnim a)
    {
        Anims.Add(a);
        _byEvent.TryAdd(a.Event, a);
    }

    public static KfmFile Load(string path) => Parse(
        EdenVfs.ReadAllBytes(path) ?? throw new FileNotFoundException(path));

    private static bool TryStr(byte[] raw, ref int c, out string s)
    {
        s = "";
        if (c < 0 || c + 4 > raw.Length) return false;
        var n = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(c, 4));
        if (n < 0 || c + 4 + n > raw.Length) return false;
        s = Encoding.Latin1.GetString(raw, c + 4, n);
        c += 4 + n;
        return true;
    }

    public static KfmFile Parse(byte[] raw)
    {
        var nl = Array.IndexOf<byte>(raw, (byte)'\n');
        var hdr = nl >= 0 ? Encoding.Latin1.GetString(raw, 0, nl) : "";
        if (!hdr.StartsWith(";Gamebryo KFM"))
            throw new InvalidDataException("Pas un KFM : " + hdr);
        var f = new KfmFile { Header = hdr };
        var cur = nl + 1;
        if (hdr.Contains(" 2.") || hdr.Contains(" 3."))
            cur++;
        if (!TryStr(raw, ref cur, out f.NifName))
            throw new InvalidDataException("nom du nif KFM invalide");
        var c2 = cur;
        var master = TryStr(raw, ref c2, out var m0) ? m0 : "";
        if (master.Length > 255 || master.Any(ch => ch > 127))
            for (var shift = 1; shift < 8; shift++)
            {
                var c3 = cur + shift;
                if (TryStr(raw, ref c3, out var m2) &&
                    m2.Length is > 0 and <= 255 &&
                    m2.All(ch => ch < 128) && !m2.StartsWith(".\\"))
                {
                    master = m2; c2 = c3;
                    f.Warnings.Add($"nom maître resynchronisé (+{shift})");
                    break;
                }
            }
        f.Master = master;
        cur = c2;
        if (cur + 20 > raw.Length)
            throw new InvalidDataException("header KFM tronqué");
        f.DefaultSyncType = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(cur, 4));
        f.DefaultNonSyncType = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(cur + 4, 4));
        f.DefaultSyncDuration = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(cur + 8, 4));
        f.DefaultNonSyncDuration = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(cur + 12, 4));
        cur += 16;
        var num = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(cur, 4));
        cur += 4;
        var hasName = hdr.Contains("1.2");
        for (var i = 0; i < Math.Min(num, 100000); i++)
        {
            KfmAnim? rec = null;
            for (var shift = 0; shift < 3 && rec == null; shift++)
            {
                var c = cur + shift;
                rec = TryParseAnim(raw, ref c, hasName);
                if (rec != null)
                {
                    cur = c;
                    if (shift > 0)
                        f.Warnings.Add($"anim {i} resynchronisée (+{shift})");
                }
            }
            if (rec == null)
            {
                f.Truncated = true;
                f.Warnings.Add($"anim {i}/{num} illisible : fichier tronqué");
                break;
            }
            f.Add(rec);
        }
        return f;
    }

    private static KfmAnim? TryParseAnim(byte[] raw, ref int c, bool hasName)
    {
        var p = c;
        bool I(out int v)
        {
            v = 0;
            if (p < 0 || p + 4 > raw.Length) return false;
            v = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p, 4)); p += 4;
            return true;
        }
        bool F(out float v)
        {
            v = 0;
            if (p < 0 || p + 4 > raw.Length) return false;
            v = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(p, 4)); p += 4;
            return true;
        }
        if (!I(out var ev)) return null;
        var a = new KfmAnim { Event = ev };
        if (hasName && !TryStr(raw, ref p, out _)) return null;
        if (!TryStr(raw, ref p, out var kf)) return null;
        if (!kf.EndsWith(".kf") && kf.Contains('\\') && p < raw.Length &&
            raw[p] == (byte)'f')
        {
            kf += "f"; p++;                 // len-1 corrompu, 'f' réel après
        }
        a.KfPath = kf;
        if (!I(out a.Index) || !I(out var nt)) return null;
        if (a.Index < 0 || a.Index > 0x100000 || nt < 0 || nt > 1000 ||
            (!kf.EndsWith(".kf") && kf.Contains('\\')))
            return null;                    // anim KFM implausible
        for (var t = 0; t < nt; t++)
        {
            if (!I(out var target) || !I(out var type)) return null;
            var dur = 0f;
            var pairs = new List<KfmKeyPair>();
            var chain = new List<KfmChainKey>();
            if (type != 5)
            {
                if (!F(out dur) || !I(out var nia)) return null;
                for (var k = 0; k < Math.Min(nia, 1000); k++)
                {
                    if (!I(out var id) || !TryStr(raw, ref p, out var key))
                        return null;
                    pairs.Add(new KfmKeyPair(id, key));
                }
                if (!I(out var ntk)) return null;
                for (var k = 0; k < Math.Min(ntk, 1000); k++)
                {
                    if (!F(out var tm) || !TryStr(raw, ref p, out var key))
                        return null;
                    chain.Add(new KfmChainKey(tm, key));
                }
            }
            a.Transitions.Add(new KfmTransition(target, type, dur)
            { SyncPairs = pairs, ChainKeys = chain });
        }
        c = p;
        return a;
    }
}
