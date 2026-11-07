using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Limitless.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace Limitless.Api.Auth;

/// <summary>Auth JWT + création de compte. Hash = MD5 hex —
/// identique au game-server (`LoginPacketHandlers` compare le mot
/// de passe en clair OU son Md5Hex : un client envoie le MD5).</summary>
public sealed class AuthService
{
    private readonly Db _db;
    private readonly string _jwtKey;
    public AuthService(Db db, IConfiguration cfg)
    {
        _db = db;
        _jwtKey = cfg["Jwt:Key"] ?? "limitless-dev-key-change-me-32+chars!!";
    }

    public static string Md5Hex(string s) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(s)));

    /// <summary>Valide (username,password) → accountId si le mot de
    /// passe matche (clair OU md5hex — mêmes règles que le login).</summary>
    /// <summary>Valide → (accountId, rôle lu depuis accounts.role,
    /// repli "User" si la colonne est absente).</summary>
    public async Task<(int Id, string Role)?> ValidateAsync(
        string user, string pass)
    {
        using var db = _db.Account();
        var row = await db.QueryFirstOrDefaultAsync<(int Id, string Pwd,
            string Role)>(
            "SELECT id AS Id, password AS Pwd, " +
            "COALESCE(role,'User') AS Role " +
            "FROM accounts WHERE username=@u", new { u = user });
        if (row.Id == 0) return null;
        return string.Equals(row.Pwd, pass, StringComparison.Ordinal) ||
               string.Equals(Md5Hex(row.Pwd), pass,
                   StringComparison.OrdinalIgnoreCase)
            ? (row.Id, string.IsNullOrEmpty(row.Role) ? "User" : row.Role)
            : null;
    }

    public record AccountInfo(int Id, string Name, string Role);

    /// <summary>Relit (username, rôle) en DB — source de vérité pour
    /// DbRoleClaimsTransformer (claims JWT rafraîchis par requête).
    /// Null si le compte n'existe plus.</summary>
    public async Task<AccountInfo?> GetAccountAsync(int id)
    {
        using var db = _db.Account();
        var row = await db.QueryFirstOrDefaultAsync<(int Id,
            string Name, string Role)>(
            "SELECT id AS Id, username AS Name, " +
            "COALESCE(role,'User') AS Role " +
            "FROM accounts WHERE id=@id", new { id });
        return row.Id == 0
            ? null
            : new AccountInfo(row.Id, row.Name,
                string.IsNullOrEmpty(row.Role) ? "User" : row.Role);
    }

    public string IssueToken(int accountId, string user, string role)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
                new Claim(ClaimTypes.Name, user),
                new Claim(ClaimTypes.Role, role),
            }, expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds));
    }
}
