using EdenClient.Net;

namespace EdenClient.Protocol;

/// <summary>
/// État partagé du sac du joueur (conteneur 0 + autres sacs) —
/// consommé par les fenêtres shop (vente), banque (dépôt) et le
/// compteur d'or du HUD.
///
/// Sources : 312 <see cref="NativeInventoryParser"/> (recharge
/// complète à l'entrée), puis deltas éprouvés serveur
/// (<c>InventoryHandlers.cs</c>) : 274 <see cref="ZonePutItem"/>
/// (nouvel item), 275 <see cref="ZoneDropItem"/> (slot vidé),
/// 291 <see cref="ZoneModifyItemSlot"/> (quantité/durabilité).
///
/// Threading : les variantes <c>*Data</c> mutent le store sous
/// verrou <b>sans lever d'événement</b> — les handlers réseau les
/// appellent directement et postent <see cref="RaiseChanged"/> sur
/// la file UI (prouvé live : la file « main thread » peut rester
/// bloquée ~10 s pendant une migration — chargement de map — et un
/// lecteur verrait alors un sac vide alors que le serveur l'a déjà
/// rempli). Les <c>Apply*</c> historiques = mutation + notification,
/// pour les appelants déjà sur le thread principal.
/// </summary>
public static class PlayerBag
{
    /// <summary>Émis à chaque mutation du contenu (312/274/275/291) —
    /// levé sur le thread principal via <see cref="RaiseChanged"/>.</summary>
    public static event Action? Changed;

    private static readonly object _gate = new();
    private static readonly List<InventorySlot> _items = new();

    /// <summary>Snapshot des items connus du sac (tous conteneurs) —
    /// copie sous verrou, lisible depuis n'importe quel thread.</summary>
    public static IReadOnlyList<InventorySlot> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }

    // ---------------- mutations (thread-safe) ----------------

    /// <summary>312 — recharge complète (entrée de zone), données
    /// seules : aucun événement — <see cref="RaiseChanged"/> doit
    /// être posté sur le thread principal par l'appelant.</summary>
    public static void ApplyEnterNodeData(IReadOnlyList<InventorySlot> items)
    {
        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(items);
        }
    }

    /// <summary>274 — item déposé (bag,slot remplacé), données
    /// seules, thread-safe.</summary>
    public static void ApplyPutItemData(ZonePutItem m)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.Bag == m.Bag && i.Slot == m.Slot);
            if (m.ItemId != 0)
                _items.Add(new InventorySlot(m.Bag, m.Slot, m.ItemId,
                    m.Durability, m.MaxDurability, m.Flags, m.Amount,
                    m.Strengthen));
        }
    }

    /// <summary>275 — slot vidé, données seules, thread-safe.</summary>
    public static void ApplyDropItemData(ZoneDropItem m)
    {
        lock (_gate)
            _items.RemoveAll(i =>
                i.Bag == m.ContainerIndex && i.Slot == m.Slot);
    }

    /// <summary>291 — quantité/durabilité d'un slot, données seules,
    /// thread-safe.</summary>
    public static void ApplyModifySlotData(ZoneModifyItemSlot m)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i =>
                i.Bag == m.ContainerIndex && i.Slot == m.Slot);
            if (idx < 0) return;
            if (m.Count <= 0)
            {
                _items.RemoveAt(idx);
            }
            else
            {
                var it = _items[idx];
                _items[idx] = it with
                {
                    Amount = m.Count, MaxDurability = m.MaxDurability,
                };
            }
        }
    }

    // ---------------- mutation + notification (thread principal) ----

    /// <summary>312 — recharge complète + notification (appelant déjà
    /// sur le thread principal).</summary>
    public static void ApplyEnterNode(IReadOnlyList<InventorySlot> items)
    {
        ApplyEnterNodeData(items);
        RaiseChanged();
    }

    /// <summary>274 — item déposé + notification.</summary>
    public static void ApplyPutItem(ZonePutItem m)
    {
        ApplyPutItemData(m);
        RaiseChanged();
    }

    /// <summary>275 — slot vidé + notification.</summary>
    public static void ApplyDropItem(ZoneDropItem m)
    {
        ApplyDropItemData(m);
        RaiseChanged();
    }

    /// <summary>291 — quantité/durabilité + notification.</summary>
    public static void ApplyModifySlot(ZoneModifyItemSlot m)
    {
        ApplyModifySlotData(m);
        RaiseChanged();
    }

    /// <summary>Lève <see cref="Changed"/> — à poster sur le thread
    /// principal par les handlers réseau (les abonnés sont des
    /// fenêtres Godot).</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>Vide le sac (déconnexion/sortie de zone).</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        RaiseChanged();
    }
}
