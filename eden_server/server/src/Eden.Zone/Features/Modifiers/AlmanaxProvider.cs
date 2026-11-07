using Eden.Core.Game.Calendar;
using Eden.Core.Game.Foundation;
using Eden.Core.Logging;

namespace Eden.Zone.Features.Modifiers;

/// <summary>
/// Calendrier d'éphéméride unique du serveur : modèles lus dans <c>content/rules/almanax.json</c> (ou le fichier de
/// <c>EDEN_ALMANAX_FILE</c>), repli sur les modèles par défaut. La graine vient de <c>EDEN_ALMANAX_SEED</c> (0 par défaut).
/// </summary>
public static class AlmanaxProvider
{
    private static readonly Lazy<AlmanaxCalendar> Instance = new(Load);
    public static AlmanaxCalendar Calendar => Instance.Value;

    private static AlmanaxCalendar Load()
    {
        var seed = long.TryParse(Environment.GetEnvironmentVariable("EDEN_ALMANAX_SEED"), out var s) ? s : 0;
        var path = Environment.GetEnvironmentVariable("EDEN_ALMANAX_FILE") ?? FindDefaultFile();
        if (path != null && File.Exists(path))
        {
            try
            {
                var t = AlmanaxCalendar.ParseJson(File.ReadAllText(path));
                EdenLog.Write($"[Almanax] {t.Count} modèles lus dans {path}");
                return new AlmanaxCalendar(t, seed);
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException or FormatException)
            {
                EdenLog.Write($"[Almanax] fichier invalide ({e.Message}) : modèles par défaut");
            }
        }
        return new AlmanaxCalendar(AlmanaxCalendar.DefaultTemplates, seed);
    }

    private static string? FindDefaultFile()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "content", "rules", "almanax.json");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    /// <summary>Résumé loggable de l'effet du jour (admin, connexion).</summary>
    public static string TodaySummary()
    {
        var day = Calendar.Day(GameTime.Clock.ResetDayNow());
        return $"{day.Template.NameFr}{(day.Forced ? " (forcé)" : "")}";
    }
}
