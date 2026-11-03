using Eden.Core.Database;

namespace Eden.Tests;

/// <summary>Chaînes de connexion des tests d'intégration DB — le mot
/// de passe vient de <c>POSTGRES_PASSWORD</c> (env) ou du
/// <c>.env</c> à la racine du projet (remontée depuis
/// <c>AppContext.BaseDirectory</c>). Jamais de secret en clair dans le
/// dépôt ; sans mot de passe la connexion échoue et les tests DB
/// s'auto-skippent via leurs checks <c>DbUp()</c>.</summary>
internal static class TestDb
{
    /// <summary>Chaîne vers la base unique <c>edenfr</c> (DB-2). Les anciens
    /// noms de base (<c>edenfr_db</c>/<c>edenfr_account</c>/<c>edenfr_member</c>)
    /// sont mappés vers le schéma correspondant via <c>SearchPath</c>.</summary>
    public static string ConnStr(string database) =>
        DbConnectionFactory.WithSearchPath(
            "Host=localhost;Port=5433;Username=postgres;" +
            $"Password={Password()};Database=edenfr",
            database switch
            {
                "edenfr_account" => DbRole.Account,
                "edenfr_member" => DbRole.Member,
                _ => DbRole.Game,
            });

    private static string Password()
    {
        var p = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (!string.IsNullOrEmpty(p)) return p;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var f = Path.Combine(dir.FullName, ".env");
            if (File.Exists(f))
            {
                foreach (var line in File.ReadLines(f))
                {
                    if (line.StartsWith("POSTGRES_PASSWORD=",
                            StringComparison.Ordinal))
                        return line["POSTGRES_PASSWORD=".Length..]
                            .Trim();
                }
            }
            dir = dir.Parent;
        }
        return "";
    }
}
