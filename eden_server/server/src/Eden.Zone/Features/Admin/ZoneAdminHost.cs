using System.Text.Json;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Admin;
using Eden.Core.Game.Foundation;
using Eden.Core.Logging;
using Eden.Zone.Features.Modifiers;
using Npgsql;

namespace Eden.Zone.Features.Admin;

/// <summary>
/// Implémentation zone de <see cref="IAdminHost"/> : joueur connecté → boucle de zone ; hors ligne → base. Les sauvegardes
/// d'actions de masse sont écrites en JSON dans <c>EDEN_ADMIN_BACKUP_DIR</c> (défaut : <c>./admin-backups</c>) AVANT l'action.
/// </summary>
internal sealed class ZoneAdminHost : IAdminHost
{
    private readonly ZoneEntryFlow _flow; private readonly ZoneServices _s; private readonly NpgsqlDataSource _db;

    public ZoneAdminHost(ZoneEntryFlow flow, ZoneServices services, NpgsqlDataSource db) { _flow = flow; _s = services; _db = db; }

    public async Task<int?> ResolveCharAsync(string name)
    {
        await using var c = await _db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id FROM character.player_characters WHERE lower(given_name) = lower($1) LIMIT 1", c) { Parameters = { new() { Value = name } } };
        return await cmd.ExecuteScalarAsync() is int id ? id : null;
    }

    public IReadOnlyList<(int CharId, string Name)> Online() => _flow.AdminOnline();

    public async Task<bool> GiveAsync(int charId, int itemId, int quantity)
    {
        if (await _flow.AdminGiveOnlineAsync(charId, itemId, quantity)) return true;
        var (ok, _, _, _) = await _s.Inventory.AddItemAsync(charId, itemId, quantity, 0, 0, false, new List<(int, int, bool)>());
        return ok;
    }

    public async Task<bool> RemoveAsync(int charId, int itemId, int quantity)
    {
        if (await _flow.AdminRemoveOnlineAsync(charId, itemId, quantity) is { } online) return online;
        var owned = (await _s.InventoryRepo.GetItemsByCharacterIdAsync(charId)).Where(i => i.ItemId == itemId && !i.IsEquipped).ToList();
        if (owned.Sum(i => i.Quantity) < quantity) return false;
        for (var left = quantity; left > 0;)
        {
            var leaf = owned.First(i => i.Quantity > 0); var take = Math.Min(left, leaf.Quantity);
            if (!await _s.Inventory.RemoveItemAsync(charId, leaf.BagId, leaf.Slot, take)) return false;
            leaf.Quantity -= take; left -= take;
        }
        return true;
    }

    public async Task<string> GrantVipAsync(string account, DateTimeOffset? until)
    {
        var repo = VipDirectory.Repo ?? throw new InvalidOperationException("base VIP indisponible");
        var id = await repo.FindAccountIdAsync(account) ?? throw new InvalidOperationException($"compte inconnu : {account}");
        await repo.GrantAsync(id, until, "admin", null); await VipDirectory.RefreshAsync();
        return until == null ? $"vip : {account} permanent" : $"vip : {account} jusqu'au {until:yyyy-MM-dd HH:mm}";
    }

    public async Task<string> RevokeVipAsync(string account)
    {
        var repo = VipDirectory.Repo ?? throw new InvalidOperationException("base VIP indisponible");
        var id = await repo.FindAccountIdAsync(account) ?? throw new InvalidOperationException($"compte inconnu : {account}");
        await repo.RevokeAsync(id); await VipDirectory.RefreshAsync();
        return $"vip : {account} retiré";
    }

    public async Task<string> BackupMassActionAsync(string description, IReadOnlyList<int> charIds)
    {
        var dir = Environment.GetEnvironmentVariable("EDEN_ADMIN_BACKUP_DIR") ?? Path.Combine(AppContext.BaseDirectory, "admin-backups");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"mass-{GameTime.Clock.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { description, at = GameTime.Clock.Now, charIds }));
        EdenLog.Write($"[Admin] sauvegarde avant action de masse : {path}");
        return path;
    }
}
