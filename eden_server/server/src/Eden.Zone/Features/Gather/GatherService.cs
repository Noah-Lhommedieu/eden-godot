using Eden.Core.Game.Events;
using System.Collections.Concurrent;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Gather;
using Eden.Core.Game.Modifiers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;

namespace Eden.Zone.Features.Gather;

/// <summary>Demande de démarrage d'une récolte continue.</summary>
public sealed record GatherStart(NetSession Session, int CharId, ItemEntity Tool,
    ItemParser.ItemDef Def, float X, float Z, bool InArea, bool IsVip);

/// <summary>
/// Récolte continue (pioche / gants / boule de cristal) — PLAN-ACTION-3 A3 :
/// canal de 3 s (déjà géré par le canal d'usage d'item) → statut « En cours de
/// minage… » → toutes les ~30 s UNE ressource tirée parmi celles de l'outil
/// (quantité selon la rareté), durabilité −1 / 10 s, annulation au mouvement.
/// Module autonome : ne dépend d'aucune classe de jeu privée ; la zone lui passe
/// des faits (position, aire valide) et les événements d'annulation.
/// </summary>
public sealed class GatherService
{
    private sealed record Active(GatherSession Session, NetSession Net,
        int Bag, int Slot, bool IsVip, CancellationTokenSource Cts);

    private readonly ZoneServices _s;
    private readonly IGameClock _clock;
    private readonly IRandom _rng;
    private readonly ModifierResolver _mods;
    private readonly Action<TimeSpan, Func<CancellationToken, Task>> _schedule;
    private readonly ConcurrentDictionary<int, Active> _active = new();

    public GatherService(ZoneServices s, IGameClock clock, IRandom rng,
        ModifierResolver mods, Action<TimeSpan, Func<CancellationToken, Task>> schedule)
    {
        _s = s; _clock = clock; _rng = rng; _mods = mods; _schedule = schedule;
    }

    public bool IsGathering(int charId) => _active.ContainsKey(charId);

    /// <summary>Démarre une récolte (après le canal de 3 s). Les refus sont TOUS
    /// signalés au joueur (plus de refus silencieux).</summary>
    public async Task<Result<bool>> StartAsync(GatherStart r, CancellationToken token)
    {
        if (!r.InArea)
            return await FailAsync(r, GameErrors.GatherOutOfArea, token);
        if (GatherKinds.FromAreaType(r.Def.UseAreaSubType) is not { } kind)
            return await FailAsync(r, GameErrors.GatherOutOfArea, token);
        if (_active.ContainsKey(r.CharId))
            return await FailAsync(r, GameErrors.GatherAlready, token);
        var collect = _s.GameData.WorkCollectById.Values
            .FirstOrDefault(w => w.CatalystItemId == r.Tool.ItemId);
        if (collect == null)
        {
            EdenLog.Write($"[Gather] char={r.CharId} outil={r.Tool.ItemId} : aucune ligne c_familycollect");
            return await FailAsync(r, GameErrors.GatherOutOfArea, token);
        }
        var pool = GatherRules.BuildPool(collect.Products.Select(p =>
            new GatherProduct(p.ItemId, p.Rate, p.Quantity)));
        var session = new GatherSession(r.Tool.ItemId, r.Tool.Id, kind, pool,
            r.X, r.Z, _clock.Now);
        var active = new Active(session, r.Session, r.Tool.BagId, r.Tool.Slot,
            r.IsVip, new CancellationTokenSource());
        if (!_active.TryAdd(r.CharId, active))
            return await FailAsync(r, GameErrors.GatherAlready, token);
        var tick = TickSeconds(active);
        await SafeSendAsync(active, GatherPackets.Build(GatherPackets.State.Start,
            (int)kind, (int)tick, r.Tool.ItemId), token);
        EdenLog.Write($"[Gather] début char={r.CharId} outil={r.Tool.ItemId} kind={kind} " +
            $"produits={pool.Count} tick={tick:0}s");
        ScheduleTick(r.CharId, active, tick);
        return Result<bool>.Ok(true);
    }

    /// <summary>Position reçue : si le joueur a bougé, la récolte est annulée.</summary>
    public Task OnMovedAsync(int charId, float x, float z)
    {
        if (_active.TryGetValue(charId, out var a) && a.Session.HasMoved(x, z))
            return StopAsync(charId, GatherStopReason.Moved);
        return Task.CompletedTask;
    }

    /// <summary>Arrêt (mouvement, sort brisé, dégâts, mort, migration, outil…).</summary>
    public async Task StopAsync(int charId, GatherStopReason reason)
    {
        if (!_active.TryRemove(charId, out var a)) return;
        a.Cts.Cancel();
        await SafeSendAsync(a, GatherPackets.Build(GatherPackets.State.Stop,
            (int)a.Session.Kind, 0, a.Session.ToolId, stopReason: (int)reason),
            CancellationToken.None);
        if (reason is GatherStopReason.Moved or GatherStopReason.ToolBroken
            or GatherStopReason.BagFull or GatherStopReason.LeftArea)
            await SendTextAsync(a.Net, GameError.Of(GatherRules.ErrorCodeFor(reason)));
        EdenLog.Write($"[Gather] arrêt char={charId} raison={reason} ticks={a.Session.TicksDone}");
    }

    private double TickSeconds(Active a) => GatherRules.DefaultTickSeconds *
        _mods.Resolve(ModifierKeys.GatherTickSeconds, new ModifierContext(IsVip: a.IsVip));

    private void ScheduleTick(int charId, Active a, double seconds) =>
        _schedule(TimeSpan.FromSeconds(seconds), ct => TickAsync(charId, a, ct));

    private async Task TickAsync(int charId, Active a, CancellationToken ct)
    {
        if (a.Cts.IsCancellationRequested ||
            !_active.TryGetValue(charId, out var cur) || !ReferenceEquals(cur, a)) return;
        var item = await _s.InventoryRepo.GetItemAsync(charId, a.Bag, a.Slot);
        if (item == null || item.Id != a.Session.ToolSerial)
        {
            await StopAsync(charId, GatherStopReason.ToolChanged);
            return;
        }
        var mult = _mods.Resolve(ModifierKeys.GatherDurabilityCost,
            new ModifierContext(IsVip: a.IsVip));
        var result = a.Session.Tick(_clock.Now, _rng, item.Durability, mult);
        var stop = result.Stop;
        if (result.Drop is { } drop && !await AwardAsync(charId, a, drop))
            stop = GatherStopReason.BagFull;
        await WearToolAsync(charId, a, item, result.DurabilityCost);
        var delivered = await SafeSendAsync(a, GatherPackets.Build(GatherPackets.State.Tick,
            (int)a.Session.Kind, (int)TickSeconds(a), a.Session.ToolId,
            result.Drop?.ItemId ?? 0, result.Drop?.Quantity ?? 0), ct);
        if (!delivered) stop ??= GatherStopReason.Disconnected;
        EdenLog.Write($"[Gather] tick char={charId} produit={result.Drop?.ItemId} " +
            $"qté={result.Drop?.Quantity} durabilité−{result.DurabilityCost}");
        if (stop is { } why) { await StopAsync(charId, why); return; }
        ScheduleTick(charId, a, TickSeconds(a));
    }

    private async Task<bool> AwardAsync(int charId, Active a, GatherDrop drop)
    {
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var add = await _s.Inventory.AddItemAsync(charId, drop.ItemId, drop.Quantity,
            0, 0, false, touched);
        DomainEventBus.Shared.Publish(new GatherTick(charId, drop.ItemId, drop.Quantity));
        if (touched.Count > 0)
            await Codecs.NativeItemCodec.EmitItemSlotsAsync(a.Net, _s.InventoryRepo,
                charId, touched, CancellationToken.None);
        return add.Success;
    }

    /// <summary>Retire la durabilité consommée ; l'outil épuisé disparaît (natif).</summary>
    private async Task WearToolAsync(int charId, Active a, ItemEntity item, int cost)
    {
        if (cost <= 0) return;
        item.Durability = Math.Max(0, item.Durability - cost);
        if (item.Durability > 0)
        {
            await _s.InventoryRepo.UpdateItemAsync(item);
            await SafeSendAsync(a, null, CancellationToken.None, new CNC_CZ_ClientModifyItemDurability
            {
                Field1 = 0, Field2 = (short)item.Slot, Field3 = (short)item.Durability,
                Field4 = (short)item.MaxDurability, Field5 = (short)item.BagId, Field6 = 0,
            });
            return;
        }
        await _s.Inventory.RemoveItemAsync(charId, item.BagId, item.Slot, 1);
        await SafeSendAsync(a, null, CancellationToken.None, new CNC_CZ_ClientDropItem
        {
            Field1 = 0, Field2 = (short)item.Slot, Field3 = (short)item.BagId,
        });
    }

    /// <summary>Refus avant consommation de l'outil (hors aire…) : message FR au
    /// joueur — plus de refus silencieux. Renvoie toujours false (usage refusé).</summary>
    public async Task<bool> RefuseAsync(NetSession session, string code)
    {
        await SendTextAsync(session, GameError.Of(code));
        return false;
    }

    private async Task<Result<bool>> FailAsync(GatherStart r, string code, CancellationToken token)
    {
        var err = GameError.Of(code);
        EdenLog.Write($"[Gather] refus char={r.CharId} code={code}");
        await SendTextAsync(r.Session, err);
        return Result<bool>.Fail(err);
    }

    /// <summary>Notice au joueur : un numéro (9063 état 3) que le client traduit.
    /// Le texte FR de référence reste dans <c>errors.tsv</c> (journaux, tests).</summary>
    private Task SendTextAsync(NetSession session, GameError error) =>
        SafeCallAsync(() => session.SendPacketAsync(GatherPackets.Ncid,
            GatherPackets.Build(GatherPackets.State.Notice, 0, 0, 0,
                stopReason: GatherRules.NoticeIdFor(error.Code))));

    private Task<bool> SafeSendAsync(Active a, byte[]? payload, CancellationToken ct,
        NetCommand? command = null) => SafeCallAsync(() => payload != null
            ? a.Net.SendPacketAsync(GatherPackets.Ncid, payload, ct)
            : a.Net.SendAsync(command!, ct));

    /// <summary>Un envoi vers une session fermée n'est pas une erreur de jeu :
    /// renvoie false (l'appelant arrête alors la récolte).</summary>
    private static async Task<bool> SafeCallAsync(Func<Task> send)
    {
        try { await send(); return true; }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException
            or InvalidOperationException or OperationCanceledException)
        {
            EdenLog.Write($"[Gather] envoi impossible (session fermée) : {ex.GetType().Name}");
            return false;
        }
    }
}
