using Eden.Core.Game.Inventory;
using Eden.Core.Game.Vending;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Codecs;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Étal joueur (market booth) — NCIDs 71, 72, 74-78, 80. Portage du
/// sous-système <c>CStall</c>/<c>CVendorOp</c>/<c>CVendorViewOp</c>
/// natif (zone/decompiled.c : VendorSellItem @0x81145e0,
/// VendorBuy @0x80f9770, VendorClose @0x80f93xx,
/// AskVendorContainer/VendorContainer @0x80fb9xx).
///
/// Modèle : les items vendus RESTENT dans le sac du vendeur — le
/// record <see cref="StallService.StallGood"/> n'est que de la
/// métadonnée pointant sur le slot live ; l'identité est
/// re-vérifiée à l'achat (stale-checks natifs item_id + clone+0xe).
/// L'achat transfère la ligne (serial préservé) puis déplace l'or.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>71 <c>AskVendor</c> — Execute natif : gate IsNoTrade
    /// puis rootcmd « askvendor » inexistante dans les données =
    /// no-op effectif. Consommé pour ne pas tomber dans le
    /// dispatcher spéculatif.</summary>
    private static Task HandleVendorAskAsync(NetSession session,
        EntryState st, byte[] payload)
    {
        EdenLog.Write($"[Zone] VendorAsk char={st.CharId} — no-op natif");
        return Task.CompletedTask;
    }

    /// <summary>77 <c>VendorSellItem</c> — ouverture/re-remplissage
    /// de l'étal. Ordre natif : checks joueur (équipe 0xfd8, file
    /// arène 0xd20, file party 0x1160) → CheckBeginStallForPlayer
    /// (statuts 0x11/0x12/0x13, node S001, région 600-609, duel
    /// 0x64c) → CheckBeginStallForStallGoods (0x14 titre vide,
    /// 0x15 collision, 0x16 &gt;13 gratuits) → checks items
    /// silencieux → ClearVendorInfo + remplissage → statut [1][2] +
    /// PlayerAction 4 + SeeChar + CVendorOp.</summary>
    private async Task HandleVendorSellItemAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var req = VendorPackets.ParseSellItem(payload);
        if (req == null || st.CharId == 0) return;
        var stalls = _services.Stalls;
        var charId = (int)st.CharId;
        var now = DateTime.UtcNow;

        // --- Checks joueur (VendorSellItem début, avant l'étal) ---
        // char+0x284 != 0 (en équipe) → texte 0xfd8.
        if (_services.Teams.IsInTeam(charId))
        { await SendTextIndexAsync(session, 0xfd8, token); return; }
        // char+0xbb0 (file arène) / +0xc44..+0xc50 (file party) —
        // notre Matchmaking couvre les deux → 0x1160 (la file arène
        // n'a pas de sous-système dédié chez nous).
        if (_services.Matchmaking.IsQueued(charId))
        { await SendTextIndexAsync(session, 0x1160, token); return; }

        // --- CheckBeginStallForPlayer ---
        // Mort / déjà vendeur / op incompatible → [0][0x11] ("Fail 2").
        // CanDo(0xf) ≈ peut s'asseoir : assis auto-accepté, sinon on
        // exige l'absence de cast en cours (pas de CStateController).
        if (st.Dead || stalls.IsVending(charId) ||
            (st.ShopOpType != -1 &&
             st.ShopOpType != StallService.VendorOpType) ||
            (!st.Sitting && st.CastingSpell != 0))
        { await SendVendorStatusAsync(session, 0, 0x11, token); return; }
        // Rate-limit 1,5 s — le natif arme le timer DÈS la tentative.
        if (!stalls.CheckOpenCooldown(charId, now))
        { stalls.MarkOpenAttempt(charId, now);
          await SendVendorStatusAsync(session, 0, 0x12, token); return; }
        stalls.MarkOpenAttempt(charId, now);
        // CanDo(0xe) — même approximation d'état : uniquement un cast
        // en cours le bloque (déjà filtré) → check formel documenté.
        // CheckStallNode : le node courant doit être la map S001.
        if (st.NodeId != 1)
        { await SendVendorStatusAsync(session, 0, 0x13, token); return; }
        // CheckStallRegion : type 600-609, position de scène et
        // dist² ≤ rayon² (CheckStallRegion désassemblé).
        var scene = _services.Scenes.GetScene(
            $"S{st.NodeId:D3}");
        if (req.RegionType < StallService.StallRegionFirst ||
            req.RegionType > StallService.StallRegionLast ||
            scene == null ||
            !scene.StallRegions.TryGetValue(req.RegionType, out var reg))
            return;                                    // rejet silencieux natif
        {
            var dx = st.PosX - reg.X;
            var dz = st.PosZ - reg.Y;                  // col2 = axe Z monde
            if (dx * dx + dz * dz > reg.Radius * reg.Radius)
                return;                                // hors aire → silencieux
        }
        // char+0x95c (cible de duel) — pas de système de duels → -1.
        // IsNoTrade (compteur +0x8c/+0xc4) — pas d'équivalent → libre.

        // --- CheckBeginStallForStallGoods ---
        var entries = req.Entries;
        var check = StallService.CheckGoods(entries.Select(
            e => (e.Container, e.Index, e.Price)).ToList());
        switch (check)
        {
            case StallService.StallError.EmptyTitle:
            case StallService.StallError.BadVectors:
                await SendVendorStatusAsync(session, 0, 0x14, token); return;
            case StallService.StallError.GoodsCollision:
                await SendVendorStatusAsync(session, 0, 0x15, token); return;
            case StallService.StallError.TooManyFree:
                await SendVendorStatusAsync(session, 0, 0x16, token); return;
            case StallService.StallError.BadPrice:
                return;                                // prix → silencieux
        }
        // Titre : non vide (natif : length == 0 → [0][0x14]) et
        // < 91 octets (0x5b) — la longueur wire est en BYTES Big5.
        var titleBytes = Eden.Core.Data.EdenEncoding.Big5.GetByteCount(
            req.Title);
        if (req.Title.Length == 0)
        { await SendVendorStatusAsync(session, 0, 0x14, token); return; }
        if (titleBytes > StallService.MaxTitleBytes) return;

        // --- Remplissage du record (checks item silencieux natifs) ---
        var goods = new List<StallService.StallGood>(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var item = await _services.InventoryRepo.GetItemAsync(
                charId, e.Container, e.Index);
            if (item == null || item.Quantity <= 0) return;
            var def = _services.GameData.Items
                .FirstOrDefault(d => d.Id == item.ItemId);
            if (def == null) return;              // itemdata inconnu
            if ((def.Flags & 8) != 0) return;     // no-trade (itemdata
                                                  // +0xf0 bit2 natif)
            if (item.IsBound) return;             // clone+0x24 & 0x10
            if (item.IsEquipped) return;          // équipement exclu
            if (item.Quality == 1) return;        // rejet natif q==1
            goods.Add(new StallService.StallGood
            {
                Index = (byte)i,
                ItemId = item.ItemId,
                RequiredLevel = def.RequiredLevel,
                Container = e.Container,
                Slot = e.Index,
                LocIndex = item.Slot,             // clone+0xc
                LocExtra = 0,                     // clone+0xe — non
                                                  // équipé → 0 (le
                                                  // stale-check achat
                                                  // revérifie vs live)
                Durability = item.Durability,
                Combo = (short)item.Combo,        // clone+0x10
                FlagLow3 = (byte)(item.EmbeddedAmount & 7),
                EmbeddedIds = new[]
                {
                    (short)item.EmbeddedId1, (short)item.EmbeddedId2,
                    (short)item.EmbeddedId3, (short)item.EmbeddedId4,
                    (short)item.EmbeddedId5, (short)item.EmbeddedId6,
                },
                ExtraCombos = MapExtraCombos(item),
                Price = e.Price,
                Quantity = item.Quantity,
                Quality = (short)item.Quality,    // clone+0x2c
                Witchcraft = (short)item.Witchcraft, // clone+0x4c
                DueDate = unchecked((uint)item.DueDate), // clone+0x5c
                MagicBox = new[]
                {
                    (short)item.MagicBoxEnchant1,
                    (short)item.MagicBoxEnchant2,
                    (short)item.MagicBoxEnchant3,
                },
                MagicBoxEnergy = item.MagicBoxEnergy,   // clone+0x7c
                MagicBoxStar = item.MagicBoxStarLevel,  // clone+0x7e
            });
        }

        // --- Pose de l'étal (ClearVendorInfo natif : goods+titre
        // remplacés, viewers conservés) ---
        var stall = stalls.OpenOrUpdate(charId, req.Title, goods);
        st.ShopOpType = StallService.VendorOpType;    // CVendorOp 0x2c
        if (!st.Sitting)
        {
            st.Sitting = true;                      // Do(0xf) natif
            await SendPlayerActionAsync(session, st, 4, 0, token);
        }
        // Statut vendeur [1][2] — vtable native 0x87d6d8c.
        await SendVendorStatusAsync(session, 1, 2, token);
        // 265 ClientSeeChar BroadCastToCanSee : NON émis — le corps
        // (~0x60) est décodé mais ses champs d'apparence (vcalls
        // +0x114/+0xe0/+0xe4) ne sont pas vérifiables sans capture ;
        // un SeeChar mal formé risque un crash client (règle dure).
        EdenLog.Write($"[Zone] Stall ouvert char={charId} " +
            $"titre=\"{req.Title}\" goods={stall.Goods.Count}");
    }

    /// <summary>72 <c>AskVendorContainer</c> — un joueur ouvre la
    /// vue de l'étal : checks (vivant, cible, dist²≤100, vendeur
    /// actif, op) → pose <c>CVendorViewOp</c> (0x2d) → statut [1][2]
    /// + UpdateView complet.</summary>
    private async Task HandleAskVendorContainerAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var targetId = VendorPackets.ParseAskContainer(payload);
        if (targetId == null || st.Dead || st.CharId == 0) return;
        var seller = FindStateByCharId(targetId.Value);
        if (seller == null) return;                  // cible absente
        if (!StallService.InVendorRange(st.PosX, st.PosZ,
                seller.PosX, seller.PosZ))
        { await SendTextIndexAsync(session, 0x333, token); return; }
        var stall = _services.Stalls.GetStall(targetId.Value);
        if (stall == null) return;                   // pas vendeur
        // Déjà en vue du même étal (op 0x2d même cible) → renvoyer
        // la vue (le natif sort tôt, un resend est sans danger).
        if (st.ShopOpType == StallService.VendorViewOpType &&
            st.ShopOpNpcUid == targetId.Value)
        {
            await SendUpdateViewAsync(session, stall, token);
            return;
        }
        // Op incompatible → le natif exige le flag op-lockable puis
        // CancelOperation ; chez nous seul l'absence d'op bloque.
        if (st.ShopOpType != -1) return;
        st.ShopOpType = StallService.VendorViewOpType;
        st.ShopOpNpcUid = targetId.Value;
        _services.Stalls.AddViewer(targetId.Value, (int)st.CharId);
        await SendVendorStatusAsync(session, 1, 2, token);
        await SendUpdateViewAsync(session, stall, token);
    }

    /// <summary>76 <c>VendorBuy</c> — achat : par ligne, sous le
    /// verrou du vendeur (achats concurrents impossibles en natif),
    /// stale-checks → slot libre → or → transfert serial → paquets
    /// DropItem vendeur / PutItem acheteur / UpdateView viewers.</summary>
    private async Task HandleVendorBuyAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var req = VendorPackets.ParseBuy(payload);
        if (req == null || st.Dead || st.CharId == 0) return;
        var buyerId = (int)st.CharId;
        var seller = FindStateByCharId(req.SellerId);
        if (seller == null || seller.Session == null) return;
        if (!StallService.InVendorRange(st.PosX, st.PosZ,
                seller.PosX, seller.PosZ))
        { await SendTextIndexAsync(session, 0x333, token); return; }

        foreach (var line in req.Lines)
        {
            if (line.Quantity < 1) continue;          // qty<1 → skip
            await _services.Stalls.BuyLock(req.SellerId)
                .WaitAsync(token);
            try
            {
                var stall = _services.Stalls.GetStall(req.SellerId);
                if (stall == null ||
                    !stall.Goods.TryGetValue(line.Index, out var g))
                    continue;
                // Stale-checks natifs : l'item live doit exister au
                // slot enregistré et porter le même item_id — sinon
                // erase + UpdateView à tous les viewers.
                var item = await _services.InventoryRepo.GetItemAsync(
                    req.SellerId, g.Container, g.Slot);
                if (item == null || item.ItemId != g.ItemId)
                {
                    stall.Goods.Remove(line.Index);
                    await BroadcastUpdateViewAsync(stall, token);
                    continue;
                }
                // Le natif soustrait le prix du RECORD entier —
                // l'achat porte sur le lot complet (qty == count).
                if (line.Quantity != item.Quantity)
                {
                    EdenLog.Write($"[Zone] VendorBuy qty!=lot " +
                        $"{line.Quantity}/{item.Quantity} — rejeté");
                    continue;
                }
                if (st.Gold < g.Price) continue;      // or → silencieux
                if ((long)seller.Gold + g.Price >
                    StallService.GoldCap)
                {   // or vendeur > cap → texte 0xfd3 à l'acheteur
                    await SendTextIndexAsync(session, 0xfd3, token);
                    continue;
                }
                // Slot libre acheteur (GetEmptySlot natif — tous sacs).
                var any = await _services.InventoryRepo
                    .GetAnyAvailableSlotAsync(buyerId);
                if (any == null) continue;

                // Transfert + règlement de l'or en UNE transaction
                // (serial préservé, deltas gardés par le solde) —
                // avant, un crash entre le transfert et les deux
                // SetGold absolus dupliquait l'item ou l'or.
                var settled = await _services.Trade
                    .TryStallPurchaseAsync(item.Id, buyerId,
                        req.SellerId, any.Value.BagId, any.Value.Slot,
                        g.Price);
                if (settled is { Ok: true })
                {
                    st.Gold = (int)settled.Value.BuyerGold;
                    seller.Gold = (int)settled.Value.SellerGold;
                }
                else
                {
                    // Repo non-SQL (tests) ou échec tx : chemin legacy.
                    if (settled != null) continue; // tx refusée (solde
                                                   // ou ligne partie)
                    if (!await _services.InventoryRepo.TransferItemAsync(
                            item.Id, buyerId, any.Value.BagId,
                            any.Value.Slot))
                        continue;

                    // Or : acheteur -prix, vendeur +prix — deltas
                    // atomiques (cap 999999999 / garde solde en SQL).
                    st.Gold = (int)await _services.Characters
                        .AddGoldAsync(buyerId, -g.Price);
                    seller.Gold = (int)await _services.Characters
                        .AddGoldAsync(req.SellerId, g.Price);
                }

                // 275 ClientDropItem au vendeur — wire natif
                // [s16 contType][s16 loc][s16 contIdx] : pas de
                // quantité, le slot est vidé en entier.
                await seller.Session.SendAsync(
                    new Eden.Core.Networking.Messages.Generated
                        .CNC_CZ_ClientDropItem
                    {
                        Field1 = 0,                 // s16 — contType sac
                        Field2 = g.Slot,            // s16 — loc vidé
                        Field3 = g.Container,       // s16 — contIdx
                    }, token);
                // 274 ClientPutItemIntoInventory à l'acheteur —
                // entité relue chez l'acheteur (même serial transféré,
                // ordre natif [id][bag][slot][dur][maxdur]).
                var moved = await _services.InventoryRepo.GetItemAsync(
                    buyerId, any.Value.BagId, any.Value.Slot);
                if (moved != null)
                    await session.SendPacketAsync(274,
                        NativeItemCodec.BuildPutItem(moved), token);
                // Attr 0x02 (or) aux deux.
                await SendAttrAsync(session, st.CharId, 0x02, st.Gold,
                    0, token);
                await SendAttrAsync(seller.Session, seller.CharId,
                    0x02, seller.Gold, 0, token);

                stall.Goods.Remove(line.Index);       // erase natif
                await BroadcastUpdateViewAsync(stall, token);
                EdenLog.Write($"[Zone] VendorBuy vendeur=" +
                    $"{req.SellerId} acheteur={buyerId} item=" +
                    $"{g.ItemId} prix={g.Price}");
            }
            finally
            {
                _services.Stalls.BuyLock(req.SellerId).Release();
            }
        }
    }

    /// <summary>74 <c>VendorClose</c> — le vendeur ferme l'étal :
    /// <c>[u8 mode][u8 ignored]</c> → VendorClose(char, mode!=0) :
    /// viewers (reset op 0x2d + 376 + texte 0xf07), PlayerAction 5
    /// broadcast+self, 375 au vendeur.</summary>
    private async Task HandleVendorCloseAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        var mode = VendorPackets.ParseClose(payload);
        if (mode == null || st.CharId == 0) return;
        await CloseStallAsync(session, st, mode.Value, token);
    }

    /// <summary>75 <c>VendorCloseView</c> — le client ferme la vue :
    /// reset op seulement si op courante = CVendorViewOp (0x2d) ET
    /// op.target == sellerId (Execute natif).</summary>
    private Task HandleVendorCloseViewAsync(NetSession session,
        EntryState st, byte[] payload)
    {
        var sellerId = VendorPackets.ParseCloseView(payload);
        if (sellerId == null) return Task.CompletedTask;
        if (st.ShopOpType == StallService.VendorViewOpType &&
            st.ShopOpNpcUid == sellerId.Value)
        {
            st.ShopOpType = -1;
            st.ShopOpNpcUid = 0;
            _services.Stalls.RemoveViewer(sellerId.Value,
                (int)st.CharId);
        }
        return Task.CompletedTask;
    }

    /// <summary>Fermeture d'étal complète — partagée entre le
    /// paquet 74 et la déconnexion du vendeur. Miroir de
    /// <c>VendorClose</c> natif.</summary>
    private async Task CloseStallAsync(NetSession? session,
        EntryState st, byte mode, CancellationToken token)
    {
        var stall = _services.Stalls.Close((int)st.CharId);
        if (stall == null) return;
        // Viewers : reset op 0x2d → 376 CloseView + texte 0xf07
        // ("The seller has closed the stand.") — les deux paquets
        // sont identiques pour tous : une sérialisation chacun.
        var (cvOp, cvPayload) = PreparePacket(
            new CNC_CZ_ClientVendorCloseView { Value = stall.OwnerId });
        var (txtOp, txtPayload) = PreparePacket(
            new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0xf07, Field2 = "" });
        foreach (var viewerId in stall.Viewers)
        {
            var v = FindStateByCharId(viewerId);
            if (v?.Session == null) continue;
            if (v.ShopOpType == StallService.VendorViewOpType &&
                v.ShopOpNpcUid == stall.OwnerId)
            {
                v.ShopOpType = -1;
                v.ShopOpNpcUid = 0;
            }
            await SendPreparedToAsync(v.Session, cvOp, cvPayload, token);
            await SendPreparedToAsync(v.Session, txtOp, txtPayload, token);
        }
        if (st.ShopOpType == StallService.VendorOpType)
        {
            st.ShopOpType = -1;
            st.ShopOpNpcUid = 0;
        }
        if (session != null && st.Sitting)
        {
            st.Sitting = false;                    // RemoveDo(0xf)
            await SendPlayerActionAsync(session, st, 5, 0, token);
        }
        if (session != null)
            await session.SendAsync(new CNC_CZ_ClientVendorClose
            { Field1 = (int)st.CharId, Field2 = mode }, token);
    }

    /// <summary>Nettoyage à la déconnexion — le vendeur perd son
    /// étal (fermeture + notifications) ; un viewer est retiré des
    /// sets pour ne plus recevoir de 378.</summary>
    private void CleanupStallOnDisconnect(EntryState st)
    {
        var charId = (int)st.CharId;
        var stall = _services.Stalls.CleanupDisconnected(charId);
        if (stall == null) return;
        // Items : rien à restaurer — ils n'ont jamais quitté le sac
        // (les goods sont des métadonnées). On notifie juste les
        // viewers en fire-and-forget comme les autres nettoyages.
        ScheduleOnLoop(TimeSpan.Zero, async _ =>
        {
            try { await CloseStallAsync(null, st, 0,
                CancellationToken.None); }
            catch { /* best-effort */ }
        });
    }

    // ============================================================
    //  Helpers
    // ============================================================

    /// <summary>EntryState d'un charId connecté (lookup inverse de
    /// _states — petit graphe, itération simple).</summary>
    private EntryState? FindStateByCharId(int charId)
    {
        foreach (var ost in _states.Values)
            if (ost.CharId == (uint)charId) return ost;
        return null;
    }

    /// <summary>377 statut vendeur — <c>[u8 action][u8 detail]</c>
    /// (codes natifs : [0][0x11..0x16] échecs ouverture,
    /// [1][2] ouverture/vue).</summary>
    private static Task SendVendorStatusAsync(NetSession session,
        byte action, byte detail, CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientVendorStatus
        { Field1 = action, Field2 = detail }, token);

    /// <summary>378 UpdateView à UN client.</summary>
    private static Task SendUpdateViewAsync(NetSession session,
        StallService.Stall stall, CancellationToken token) =>
        session.SendPacketAsync(378, VendorPackets.BuildUpdateView(
            stall.OwnerId, stall.Title, stall.Goods.Values), token);

    /// <summary>378 au vendeur + tous les viewers — le natif émet
    /// ClientVendorUpdateView à chaque char du set viewers (et au
    /// vendeur via son op).</summary>
    private async Task BroadcastUpdateViewAsync(
        StallService.Stall stall, CancellationToken token)
    {
        var body = VendorPackets.BuildUpdateView(stall.OwnerId,
            stall.Title, stall.Goods.Values);
        var sent = new HashSet<int>();
        var seller = FindStateByCharId(stall.OwnerId);
        if (seller?.Session != null)
        {
            await seller.Session.SendPacketAsync(378, body, token);
            sent.Add(stall.OwnerId);
        }
        foreach (var viewerId in stall.Viewers)
        {
            if (!sent.Add(viewerId)) continue;
            var v = FindStateByCharId(viewerId);
            if (v?.Session == null) continue;
            try { await v.Session.SendPacketAsync(378, body, token); }
            catch { /* session morte */ }
        }
    }

    /// <summary>extra_combo_id1..9 du modèle DB → vecteur s16×11
    /// natif clone+0x34 (la DB n'en stocke que 9 — les 2 derniers
    /// restent à 0 comme un item sans extra combos).</summary>
    private static short[] MapExtraCombos(ItemEntity item)
    {
        var v = new short[11];
        for (var i = 0; i < item.ExtraCombos.Length && i < 11; i++)
            v[i] = (short)item.ExtraCombos[i];
        return v;
    }
}
