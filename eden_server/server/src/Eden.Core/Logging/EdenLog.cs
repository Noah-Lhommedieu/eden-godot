using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Eden.Core.Logging;

/// <summary>Point d'entrée de logging centralisé — remplace les
/// <c>Console.WriteLine</c> historiques par des catégories
/// <see cref="ILogger"/> dérivées du préfixe <c>[Tag]</c> des messages.
/// La factory est créée une fois par processus (SimpleConsole
/// mono-ligne horodaté) ; les loggers sont cachés par catégorie.</summary>
public static class EdenLog
{
    private static readonly Lazy<ILoggerFactory> Factory = new(() =>
        LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(ParseLevel())));

    private static readonly ConcurrentDictionary<string, ILogger> Loggers = new();

    /// <summary>Niveau global — <c>EDEN_LOG_LEVEL</c> (Debug, Info…),
    /// défaut Information.</summary>
    private static LogLevel ParseLevel() =>
        Enum.TryParse<LogLevel>(
            Environment.GetEnvironmentVariable("EDEN_LOG_LEVEL"),
            ignoreCase: true, out var l) ? l : LogLevel.Information;

    /// <summary>Logger d'une catégorie explicite (ex. <c>"Zone"</c>).</summary>
    public static ILogger For(string category) =>
        Loggers.GetOrAdd(category,
            c => Factory.Value.CreateLogger("Eden." + c));

    /// <summary>Écrit un message en inférant catégorie et niveau :
    /// <c>[Tag]</c> de tête → catégorie <c>Eden.Tag</c> ; le message
    /// passe en Warning s'il contient WARN/FAIL/ERROR.</summary>
    public static void Write(string message)
    {
        var category = "General";
        var rest = message.AsSpan();
        if (rest.Length > 2 && rest[0] == '[')
        {
            var end = rest.IndexOf(']');
            if (end > 1 && end < 40)
            {
                category = rest[1..end].ToString();
                rest = rest[(end + 1)..].TrimStart();
            }
        }
        var log = For(category);
        var text = message;
        if (rest.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            rest.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
            rest.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            log.LogWarning("{Message}", text);
        else
            log.LogInformation("{Message}", text);
    }

    /// <summary>Variante pour les exceptions : pile + message sous la
    /// catégorie dérivée du tag, niveau Error.</summary>
    public static void Write(Exception ex, string message)
    {
        var category = "General";
        if (message.Length > 2 && message[0] == '[')
        {
            var end = message.IndexOf(']');
            if (end > 1 && end < 40)
                category = message[1..end];
        }
        For(category).LogError(ex, "{Message}", message);
    }
}
