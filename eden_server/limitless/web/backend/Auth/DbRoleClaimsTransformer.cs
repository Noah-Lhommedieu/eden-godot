using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Limitless.Api.Auth;

/// <summary>Re-synchronise le claim rôle depuis la DB à chaque
/// requête authentifiée. Le JWT fige le rôle pour 12 h : un compte
/// promu Admin gardait « User » dans ses claims — le bouton/panneau
/// admin restait invisible et /api/auth/me renvoyait l'ancienne
/// valeur (retour joueur : « admin toujours pas accessible »).
/// Avec ce transformer, [Authorize(Roles=…)] ET /api/auth/me
/// reflètent accounts.role en ≤ 30 s, sans re-login.
/// Dégradé : si la DB est injoignable, les claims du token sont
/// conservés (comportement antérieur).</summary>
public sealed class DbRoleClaimsTransformer : IClaimsTransformation
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly AuthService _auth;
    private readonly IMemoryCache _cache;

    public DbRoleClaimsTransformer(AuthService auth, IMemoryCache cache)
    {
        _auth = auth;
        _cache = cache;
    }

    public async Task<ClaimsPrincipal> TransformAsync(
        ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity ||
            !identity.IsAuthenticated)
            return principal;
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(sub, out var accountId)) return principal;

        AuthService.AccountInfo? fresh;
        try
        {
            var key = "dbrole:" + accountId;
            if (!_cache.TryGetValue(key, out fresh))
            {
                fresh = await _auth.GetAccountAsync(accountId);
                // Un compte supprimé n'est pas mis en cache — on
                // retente la DB à la requête suivante.
                if (fresh is not null)
                    _cache.Set(key, fresh, CacheTtl);
            }
        }
        catch
        {
            return principal; // DB injoignable → claims du token
        }
        if (fresh is null) return principal;

        // Idempotent : TransformAsync peut être rappelé sur un
        // principal déjà transformé → sortie rapide si rien ne change.
        if (principal.Identity?.Name == fresh.Name &&
            principal.FindFirst(ClaimTypes.Role)?.Value == fresh.Role)
            return principal;

        var clone = new ClaimsIdentity(identity);
        foreach (var c in clone.FindAll(ClaimTypes.Role).ToList())
            clone.RemoveClaim(c);
        clone.AddClaim(new Claim(ClaimTypes.Role, fresh.Role));
        foreach (var c in clone.FindAll(ClaimTypes.Name).ToList())
            clone.RemoveClaim(c);
        clone.AddClaim(new Claim(ClaimTypes.Name, fresh.Name));
        return new ClaimsPrincipal(clone);
    }
}
