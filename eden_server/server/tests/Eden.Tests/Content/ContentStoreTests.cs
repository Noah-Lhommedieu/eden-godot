using Eden.Core.Game.Content;
using Npgsql;

namespace Eden.Tests.Content;

/// <summary>Contenu statique : export de secours (toujours) et édition + annulation sur eden_content (si EDEN_CONTENT_CONN est posé).</summary>
public class ContentStoreTests
{
    [Fact]
    public async Task JsonlStore_ReadsExport()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eden-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "item.item.jsonl"),
            "{\"buy_price_silver\":5,\"icon_code\":\"UI012\",\"in_scope\":true,\"item_id\":7,\"item_kind\":1,\"max_level\":0,\"name_fr\":\"Épée\",\"required_level\":3}\n");
        var items = await new JsonlContentStore(dir).ItemsAsync();
        Assert.Equal(new ContentItem(7, "Épée", 1, 3, 0, 5, "UI012", true), Assert.Single(items));
    }

    [Fact]
    public async Task Edit_IsLoggedAndRevertible()
    {
        var conn = Environment.GetEnvironmentVariable("EDEN_CONTENT_CONN");
        if (conn == null) return;                                   // pas de base de contenu : test d'intégration ignoré
        await using var ds = NpgsqlDataSource.Create(conn);
        var store = new PgContentStore(ds);
        var before = (await store.ItemsAsync()).First(i => i.ItemId == 1);
        var change = await store.EditItemAsync(1, "required_level", "77", "test-admin", "test");
        Assert.Equal(77, (await store.ItemsAsync()).First(i => i.ItemId == 1).RequiredLevel);
        await store.RevertAsync(change, "test-admin");
        Assert.Equal(before.RequiredLevel, (await store.ItemsAsync()).First(i => i.ItemId == 1).RequiredLevel);
        await Assert.ThrowsAsync<ArgumentException>(() => store.EditItemAsync(1, "item_id", "2", "x", null));
    }

    [Fact]
    public async Task Factory_FallsBackToExport_WhenDatabaseIsDown()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eden-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "item.item.jsonl"),
            "{\"buy_price_silver\":1,\"icon_code\":\"UI001\",\"in_scope\":false,\"item_id\":9,\"item_kind\":0,\"max_level\":0,\"name_fr\":\"Test\",\"required_level\":0}\n");
        var (store, fallback) = await ContentStoreFactory.OpenAsync("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2", dir);
        Assert.True(fallback);
        Assert.Equal(9, Assert.Single(await store.ItemsAsync()).ItemId);
    }
}
