using System.Security.Cryptography;
using System.Text;
using Dapper;
using Limitless.Api.Auth;
using Limitless.Api.Data;
using Limitless.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Config ---
// fnaccount = comptes (auth) ; fndb1 = persos/jeu (wiki, rankings,
// progression). Même DSN que les services du game-server.
// Les DSN de dev vivent dans appsettings.json (mêmes identifiants que
// tools/start-local.sh — base docker locale, déjà versionnés). Les
// fallbacks ci-dessous servent uniquement si la config est absente.
var accountCs = builder.Configuration.GetConnectionString("Account")
    ?? "Host=127.0.0.1;Port=5432;Database=fnaccount;Username=postgres;Password=CHANGE_ME_USE_ENV";
var gameCs = builder.Configuration.GetConnectionString("Game")
    ?? "Host=127.0.0.1;Port=5432;Database=fndb1;Username=postgres;Password=CHANGE_ME_USE_ENV";

var contentCs = builder.Configuration.GetConnectionString("Content")
    ?? "Host=127.0.0.1;Port=5433;Database=eden_content;Username=postgres;Password=CHANGE_ME_USE_ENV";
var liveCs = builder.Configuration.GetConnectionString("Live")
    ?? "Host=127.0.0.1;Port=5433;Database=eden_live;Username=postgres;Password=CHANGE_ME_USE_ENV;SearchPath=guild,character,public";
builder.Services.AddSingleton(new Db(accountCs, gameCs, contentCs, liveCs));
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<GameDataStore>();
builder.Services.AddSingleton<RankingsService>();
builder.Services.AddSingleton<LimitlessService>();
builder.Services.AddMemoryCache();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "limitless-dev-key-change-me-32+chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
        };
    });
builder.Services.AddAuthorization();
// Claim rôle relu en DB à chaque requête (promotion Admin sans
// re-login — cf. DbRoleClaimsTransformer).
builder.Services.AddTransient<IClaimsTransformation,
    DbRoleClaimsTransformer>();
builder.Services.AddControllers();

// CORS — le frontend Next (dev :3000 / prod configurable) consomme
// l'API depuis un autre origin. Origins via Cors:Origins (; séparés).
var corsOrigins = (builder.Configuration["Cors:Origins"]
    ?? "http://localhost:3000").Split(';',
    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Migration idempotente : colonnes web sur accounts (rôle User/Admin
// pour la gate JWT, email pour l'inscription). Table `news` créée à
// la demande par AdminController.PostNews.
{
    try
    {
        var db = app.Services.GetRequiredService<Db>();
        using var conn = db.Account();
        conn.Execute(
            "ALTER TABLE accounts ADD COLUMN IF NOT EXISTS role VARCHAR(20) DEFAULT 'User'");
        conn.Execute(
            "ALTER TABLE accounts ADD COLUMN IF NOT EXISTS email VARCHAR(120) DEFAULT ''");
        // Mini-CMS guides (W4) — table web-owned dans fnaccount (la
        // base "portail" ; fndb1 appartient au game-server).
        conn.Execute(
            "CREATE TABLE IF NOT EXISTS wiki_articles (" +
            "id SERIAL PRIMARY KEY, slug VARCHAR(80) UNIQUE NOT NULL, " +
            "title VARCHAR(160) NOT NULL, category VARCHAR(40) " +
            "NOT NULL DEFAULT 'General', body TEXT NOT NULL, " +
            "sort_order INT NOT NULL DEFAULT 0, " +
            "published BOOLEAN NOT NULL DEFAULT false, " +
            "author VARCHAR(60) NOT NULL DEFAULT '', " +
            "created_at TIMESTAMPTZ NOT NULL DEFAULT now(), " +
            "updated_at TIMESTAMPTZ NOT NULL DEFAULT now())");
    }
    catch (Exception ex)
    {
        // DB injoignable au démarrage : l'API reste en ligne — le wiki
        // (fichiers JSON) continue de servir, les endpoints DB
        // répondront 500/500-degraded proprement.
        app.Logger.LogWarning(ex,
            "PostgreSQL unreachable at startup — DB-backed endpoints will fail until it comes back");
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => "Limitless API — OK");
app.Run();
