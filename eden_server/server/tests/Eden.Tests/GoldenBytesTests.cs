using System.Reflection;
using System.Text.Json;
using Eden.Core.Networking;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Golden bytes : rejoue les trames capturées (<c>captures/*.bin</c>,
/// découpées par <c>tools/gen_golden_data.py</c> dans
/// <c>Data/golden/ncid_*.json</c>) à travers les codecs générés.
/// Pour chaque corps capturé : <c>Deserialize → Serialize</c> doit
/// reproduire les octets à l'identique — écart = codec incomplet ou
/// mauvais ordre de champs (classe de bug du freeze NCID 271).
///
/// Les NCIDs dont le codec est connu pour être partiel vivent dans
/// <c>Data/known_incomplete.json</c> {ncid: raison} : leurs cas sont
/// ignorés par le test strict, mais un second test fait échouer la
/// suite si un codec "incomplet" se met à round-tripper — la liste ne
/// peut pas devenir un cimetière silencieux.
/// </summary>
public class GoldenBytesTests
{
    private static readonly string GoldenDir = Path.Combine(
        AppContext.BaseDirectory, "Data", "golden");

    /// <summary>opcode → type NetCommand (toutes les classes générées
    /// de la solution — plusieurs assemblées référencées).</summary>
    private static readonly IReadOnlyDictionary<int, Type> Codecs =
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => typeof(NetCommand).IsAssignableFrom(t) &&
                        !t.IsAbstract)
            .GroupBy(t => ((NetCommand)Activator.CreateInstance(t)!).Opcode)
            .ToDictionary(g => g.Key, g => g.First());

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        { return e.Types.Where(t => t != null)!; }
    }

    /// <summary>NCIDs marqués « codec incomplet » (voir
    /// known_incomplete.json) + NCIDs sans codec généré.</summary>
    private static readonly IReadOnlyDictionary<int, string> Incomplete =
        LoadIncomplete();

    private static IReadOnlyDictionary<int, string> LoadIncomplete()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "Data",
            "known_incomplete.json");
        if (!File.Exists(p)) return new Dictionary<int, string>();
        return JsonSerializer.Deserialize<Dictionary<int, string>>(
            File.ReadAllText(p))!;
    }

    /// <summary>(ncid, direction, indexCas, bodyHex) — cas stricts :
    /// codec présent ET non marqué incomplet.</summary>
    public static IEnumerable<object[]> StrictCases()
    {
        foreach (var c in AllCases())
            if (FindCodec(c.Ncid) != null && !Incomplete.ContainsKey(c.Ncid))
                yield return new object[] { c.Ncid, c.Dir, c.Index, c.Body };
    }

    private record Case(int Ncid, string Dir, int Index, byte[] Body);

    private static IEnumerable<Case> AllCases()
    {
        if (!Directory.Exists(GoldenDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(GoldenDir,
                     "ncid_*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var ncid = doc.RootElement.GetProperty("ncid").GetInt32();
            var i = 0;
            foreach (var c in doc.RootElement
                         .GetProperty("cases").EnumerateArray())
                yield return new Case(ncid,
                    c.GetProperty("dir").GetString()!,
                    i++, Convert.FromHexString(
                        c.GetProperty("body").GetString()!));
        }
    }

    /// <summary>Trouve la classe NetCommand d'un NCID câble.
    /// Le client EasyFun/Reforged décale les CNC_CW_* ≥38 de +1 :
    /// pour un NCID ≤83 émis on essaie aussi ncid-1.</summary>
    private static Type? FindCodec(int ncid)
    {
        if (Codecs.TryGetValue(ncid, out var t)) return t;
        if (ncid is >= 38 and <= 83 &&
            Codecs.TryGetValue(ncid - 1, out t))
            return t;
        return null;
    }

    /// <summary>Round-trip octet-par-octet sur les codecs complets.</summary>
    [Theory]
    [MemberData(nameof(StrictCases))]
    public void Codec_RoundTrips_CapturedBytes(int ncid, string dir,
        int index, byte[] body)
    {
        var type = FindCodec(ncid)!;
        var msg = (NetCommand)Activator.CreateInstance(type)!;
        Exception? error = null;
        try
        {
            msg.Deserialize(new InBinStream(body));
        }
        catch (Exception e) { error = e; }
        Assert.Null(error);
        var outp = new OutBinStream();
        msg.Serialize(outp);
        Assert.True(body.AsSpan().SequenceEqual(outp.WrittenSpan),
            $"NCID {ncid} ({type.Name}) cas {dir}#{index} : " +
            $"capturé {Convert.ToHexString(body)} ≠ " +
            $"resérialisé {Convert.ToHexString(outp.WrittenSpan)}");
    }

    /// <summary>Ratchet : si un codec marqué incomplet se met à
    /// round-tripper une trame capturée, ce test échoue pour forcer la
    /// sortie de la liste known_incomplete (Fact interne — MemberData
    /// vide lèverait « no data found »).</summary>
    [Fact]
    public void IncompleteCodec_StillIncomplete_OrPromote()
    {
        foreach (var c in AllCases())
        {
            if (!Incomplete.ContainsKey(c.Ncid)) continue;
            var type = FindCodec(c.Ncid);
            if (type == null) continue;
            var msg = (NetCommand)Activator.CreateInstance(type)!;
            try
            {
                msg.Deserialize(new InBinStream(c.Body));
                var outp = new OutBinStream();
                msg.Serialize(outp);
                if (c.Body.AsSpan().SequenceEqual(outp.WrittenSpan))
                    Assert.Fail(
                        $"NCID {c.Ncid} ({type.Name}) cas {c.Dir}#" +
                        $"{c.Index} round-tripe désormais — le retirer " +
                        $"de known_incomplete.json ({Incomplete[c.Ncid]})");
            }
            catch (Exception)
            {
                // Toujours incomplet — attendu.
            }
        }
    }
}
