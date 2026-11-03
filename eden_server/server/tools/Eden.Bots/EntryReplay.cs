using System.Text.RegularExpressions;

namespace Eden.Bots;

/// <summary>Rejeu du burst C→Z capturé dans
/// `zsniff_cs_entry_success.bin` — seules les trames après le dernier
/// ticket NCID 0 du canal principal sont reprises, comme
/// `eden_session.py`.</summary>
public static class EntryReplay
{
    private static readonly Regex FrameRe = new(
        @"^\[[^\]]+\]\s+(?<dir>[EDS])\s+(?:ch=(?<ch>[0-9a-fA-F]+)\s+)?" +
        @"NCID=(?<ncid>\d+)\s+len=(?<len>\d+)\s+(?<hex>[0-9a-fA-F]+)",
        RegexOptions.Compiled);

    public static IReadOnlyList<byte[]> Load(string path)
    {
        var lines = File.ReadLines(path);
        var parsed = new List<(string? Channel, int Ncid, byte[] Payload)>();
        foreach (var line in lines)
        {
            var m = FrameRe.Match(line);
            if (!m.Success || m.Groups["dir"].Value != "D") continue;
            var payload = Convert.FromHexString(m.Groups["hex"].Value);
            if (payload.Length != int.Parse(m.Groups["len"].Value)) continue;
            parsed.Add((m.Groups["ch"].Success ? m.Groups["ch"].Value : null,
                int.Parse(m.Groups["ncid"].Value), payload));
        }
        var ticketIndex = parsed.FindLastIndex(p => p.Ncid == 0);
        if (ticketIndex < 0)
            throw new InvalidDataException($"Aucun ticket NCID 0 dans {path}");
        var ch = parsed[ticketIndex].Channel;
        return parsed.Skip(ticketIndex + 1)
            .Where(p => p.Channel == ch)
            .Select(p => p.Payload)
            .ToArray();
    }
}
