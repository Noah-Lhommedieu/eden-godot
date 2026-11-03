using System.Collections.Concurrent;
using System.Diagnostics;
using Eden.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Eden.Core.Logging;

/// <summary>
/// Endpoint d'observabilité commun aux services Eden.
/// </summary>
public static class EdenObservability
{
    private static readonly ConcurrentBag<WebApplication> Started = new();
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    /// <summary>
    /// Démarre un mini-serveur HTTP <c>/metrics</c> + <c>/health</c>.
    /// Retourne <c>null</c> quand <c>EDEN_OBSERVABILITY_PORT</c> vaut 0.
    /// </summary>
    public static async Task<WebApplication?> StartAsync(
        string serviceName,
        NpgsqlDataSource? db = null,
        CancellationToken cancellationToken = default)
    {
        var port = EdenConfig.ObservabilityPort;
        if (port <= 0)
            return null;

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory,
            });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(
            $"{EdenConfig.ObservabilityBind}:{port}");

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .ConfigureResource(resource => resource
                    .AddService(serviceName))
                .AddMeter(EdenMetrics.Meter.Name, "Npgsql")
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint();
        app.MapGet("/health", async (CancellationToken ct) =>
        {
            var checks = new Dictionary<string, object?>
            {
                ["process"] = "ok",
            };
            var statusCode = StatusCodes.Status200OK;
            var status = "healthy";

            if (db != null)
            {
                try
                {
                    await using var conn = await db.OpenConnectionAsync(ct);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = 2;
                    await cmd.ExecuteScalarAsync(ct);
                    checks["postgres"] = "ok";
                }
                catch (Exception ex)
                {
                    status = "unhealthy";
                    statusCode = StatusCodes.Status503ServiceUnavailable;
                    checks["postgres"] = ex.Message;
                }
            }

            return Results.Json(new
            {
                service = serviceName,
                status,
                uptimeSeconds =
                    (long)(DateTime.UtcNow - StartedAt).TotalSeconds,
                checks,
            }, statusCode: statusCode);
        });

        await app.StartAsync(cancellationToken);
        Started.Add(app);
        EdenLog.Write(
            $"[Observability] {serviceName} : metrics+health sur " +
            $"{EdenConfig.ObservabilityBind}:{port}");
        return app;
    }
}
