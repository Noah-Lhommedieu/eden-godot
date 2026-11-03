using Eden.Core.Networking;
using Eden.Core.Data;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Effets d'usage des items consommables — évaluation des
/// commandes S_Enchant dont la table numéro→nom est
/// <see cref="EnchantCommands"/> (extraite par objdump de
/// <c>CSpellDBO::DecodeEffectCommand</c> @0x8380ad0) et
/// <c>CEC_HP::Init/Execute</c> (heal = arg1 + rand % (|arg2-arg1|+1),
/// min/max auto-triés). Implémentés : restauration HP/MP directe et
/// en %, téléportation par item (ItemTransportArea = CEventRegenerate
/// node&lt;&lt;16|areaKey, désasm CEC_ItemTransportArea @0x853e6xx),
/// EnchantCancel (→ remove 300), chaînage EffectNextID, bons d'or
/// (GainForeverGold/GainGold/GainLifelongGold → attr 0x02 + persist).
/// Les commandes attribut/buff (cf. <see cref="IsBuffApplyCommand"/>)
/// envoient l'EnchantAdd 299 déjà éprouvé (montures) ; les inconnues
/// et les commandes système one-shot sont loguées, jamais spéculées.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Applique les commandes d'usage de l'enchant
    /// <paramref name="enchantId"/> sur l'état live de la session.
    /// Appelé par le handler ItemUse après consommation de l'item
    /// (CEC_ItemUse → EnchantMgr::Load côté natif).</summary>
    private async Task ApplyUseEnchantAsync(NetSession session,
        int charId, int enchantId, int buffSeconds,
        CancellationToken token)
    {
        if (!_states.TryGetValue(session.SessionId, out var st))
            return;
        await RunUseCommandsAsync(session, st, charId, enchantId, 0,
            buffSeconds, token);
    }

    /// <summary>Exécute les blocs de commandes d'un enchant (récursif
    /// pour EffectNextID, profondeur bornée à 3 comme garde-fou).
    /// <paramref name="buffSeconds"/> = durée d'effet de l'item
    /// (S_Item cols 24/25) — utilisé par les commandes de taux
    /// (ItemExpRateUp & co) qui posent un enchant temporisé réel.</summary>
    private async Task RunUseCommandsAsync(NetSession session,
        EntryState st, int charId, int enchantId, int depth,
        int buffSeconds, CancellationToken token)
    {
        if (depth > 3) return;
        // Statut posé une seule fois par enchant — un enchant à
        // plusieurs commandes « buff » ne doit pas empiler les
        // Counter du CEnchant (re-add natif = upsert, pas doublon).
        var statusApplied = false;
        var cmds = _services.Enchants.GetUseCommands(enchantId);
        // Trace du dispatch — chaque commande aboutit sur un case
        // nommé (GainCoin, GainForeverGold…) ou le default.
        EdenLog.Write($"[Use] char={charId} enchant={enchantId} " +
            $"depth={depth} cmds=[{string.Join(", ",
                cmds.Select(c => c.CommandId + "|" +
                    string.Join('|', c.Args)))}]");
        foreach (var c in cmds)
        {
            var name = EnchantCommands.Names.TryGetValue(c.CommandId,
                out var n) ? n : $"#{c.CommandId}";
            // Args doubles → int locaux : ce chemin (soin direct,
            // téléport, shop, gold…) ne consomme que des entiers.
            var a = c.Args.Select(v => (int)v).ToArray();
            switch (name)
            {
                // --- Restauration directe : HP/MP min..max ---
                // CEC_HP::Init range = |a2-a1|, Execute = min+rand(range+1).
                case "HP":
                // HealPlus <n> (ids 2039/2047 — sélection par arg non
                // nul dans DecodeEffectCommand) : soin direct sur les
                // args, même famille que CEC_HP.
                case "HealPlus":
                {
                    var heal = RandRange(a[0], a[1]);
                    st.PlayerHp = Math.Min(st.PlayerMaxHp,
                        st.PlayerHp + Math.Max(0, heal));
                    // 280 attr 0x11 = HP self (UpdateAttr natif).
                    await SendVitalHpAsync(session, (uint)st.CharId, st.PlayerHp, st.PlayerMaxHp, token);
                    break;
                }
                case "MP":
                {
                    var mp = RandRange(a[0], a[1]);
                    st.PlayerMp = Math.Min(st.PlayerMaxMp,
                        st.PlayerMp + Math.Max(0, mp));
                    // 280 attr 0x12 = MP self.
                    await SendVitalMpAsync(session, (uint)st.CharId, st.PlayerMp, st.PlayerMaxMp, token);
                    break;
                }

                // --- Restauration en % du max ---
                case "HPPlusRate":
                {
                    var heal = st.PlayerMaxHp * a[0] / 100;
                    st.PlayerHp = Math.Min(st.PlayerMaxHp,
                        st.PlayerHp + Math.Max(0, heal));
                    await SendVitalHpAsync(session, (uint)st.CharId, st.PlayerHp, st.PlayerMaxHp, token);
                    break;
                }
                case "MPPlusRate":
                {
                    var mp = st.PlayerMaxMp * a[0] / 100;
                    st.PlayerMp = Math.Min(st.PlayerMaxMp,
                        st.PlayerMp + Math.Max(0, mp));
                    await SendVitalMpAsync(session, (uint)st.CharId, st.PlayerMp, st.PlayerMaxMp, token);
                    break;
                }

                // --- ItemTransportArea <nodeId> <areaKey> ---
                // Natif : CEventRegenerate(char, "", arg1<<16|arg2) —
                // téléporte vers l'aire de regen du node cible.
                case "ItemTransportArea":
                {
                    var node = a[0];
                    var areaKey = a[1];
                    var dest = _services.Scenes.GetScene($"S{node:D3}");
                    if (dest == null || node == st.NodeId)
                    {
                        EdenLog.Write($"[Zone] ItemTransportArea " +
                            $"char={charId} -> node {node} key={areaKey} " +
                            $": scène absente ou même node — ignoré");
                        break;
                    }
                    // Aire d'arrivée : event_area d'id areaKey, sinon
                    // la GATEWAY paire, sinon le point de naissance.
                    var area = dest.EventAreas.FirstOrDefault(
                        a => a.Id == areaKey)
                        ?? dest.EventAreas.FirstOrDefault(
                            a => a.Type == "GATEWAY")
                        ?? dest.EventAreas.FirstOrDefault(
                            a => a.Type == "BORN");
                    float dx = 100, dy = 100, dz = 0;
                    if (area != null) (dx, dy, dz) = (area.X, area.Y, area.Z);
                    else if (dest.Positions.Count > 0)
                        (dx, dy, dz) = dest.Positions[0];
                    EdenLog.Write($"[Zone] ItemTransportArea " +
                        $"char={charId} node {st.NodeId} -> {node} " +
                        $"key={areaKey} ({dx:F1},{dy:F1})");
                    await MigrateToNodeAsync(session, st, node,
                        dx, dy, dz, token);
                    break;
                }

                // --- EffectNextID <enchantId> <chance%> : chaîne ---
                case "EffectNextID":
                    if (a[1] >= 100 ||
                        Random.Shared.Next(100) < a[1])
                        await RunUseCommandsAsync(session, st, charId,
                            a[0], depth + 1, buffSeconds, token);
                    break;

                // --- GainGold <n> — bon d'or classique :
                // CCharacter::GainGold @0x842a7f0 (char+0x4b0 borné
                // [0,999999999]) → 280 attr 0x02.
                case "GainGold":
                {
                    var gold = a[0];
                    if (gold != 0)
                    {
                        st.Gold = (int)await _services.Characters
                            .AddGoldAsync((int)st.CharId, gold);
                        await SendAttrAsync(session, (uint)st.CharId, 0x02,
                            st.Gold, 0, token);
                    }
                    break;
                }

                // --- GainForeverGold <n> — bons « Eternal Coins » :
                // CEC_GainFG → CCharacter::GainForeverGold @0x840c3f0
                // (char+0x4b4 = player_characters.forever_gold, cap
                // 999999999, refus si dépassement) → 280 attr=3 +
                // textes 0xdde « Received n Eternal Coin(s) » /
                // 0xddf « Paying n » / 0xde0 « max » / 0xddd
                // « not enough ».
                case "GainForeverGold":
                {
                    var fg = a[0];
                    if (fg == 0) break;
                    var cur = await _services.Characters
                        .GetForeverGoldAsync((int)st.CharId);
                    if (cur + fg > 999999999)
                    {   // cap dépassé — refus natif + texte 0xde0
                        await SendTextIndexArgsAsync(session, 0xde0, "",
                            token);
                        break;
                    }
                    if (cur + fg < 0)
                    {   // solde insuffisant — texte 0xddd
                        await SendTextIndexArgsAsync(session, 0xddd, "",
                            token);
                        break;
                    }
                    var total = await _services.Characters
                        .AddForeverGoldAsync((int)st.CharId, fg);
                    await SendAttrAsync(session, (uint)st.CharId, 0x03,
                        (int)Math.Min(int.MaxValue, total), 0, token);
                    await SendTextIndexArgsAsync(session,
                        (short)(fg > 0 ? 0xdde : 0xddf),
                        Math.Abs(fg).ToString(), token);
                    break;
                }

                // --- GainCoin (2942) — sélecteur multi-devises
                // (objdump @0x838621e → 0x838a14c : une commande
                // native est émise PAR var non nulle) :
                //   a[0] GainLifelongGold  → lifelong_gold
                //        (« Crystal Cross Medal », cap 9999999, 555)
                //   a[1] GainColosseumGold → colosseum_gold
                //        (« Honor Stars », cap 999999999, 525)
                //   a[2] GainWarCoin       → war_coin
                //        (« Battle Merit », DBA mission → 576)
                //   a[3] GainAlpacaCoin    → alpaca_coin
                //        (DBA mission → 580)
                //   a[4] GainKnightCoin    → special_coins type 10
                //        (« Temple Knight », DBA GMGainSpecialCoin)
                case "GainCoin":
                {
                    if (a[0] != 0)
                        await GainLifelongGoldAsync(session, st, a[0],
                            token);
                    if (a[1] != 0)
                        await GainColosseumGoldAsync(session, st, a[1],
                            token);
                    if (a[2] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"Update WarCoin Coin = {a[2]}", token);
                    if (a[3] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"Update AlpacaCoin Coin = {a[3]}", token);
                    if (a[4] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"GMGainSpecialCoin Type = 10 Money = {a[4]}",
                            token);
                    break;
                }

                // --- GainLifelongGold <n> — émis directement par les
                // versions « un seul var » si la donnée en produit
                // (le sélecteur 2942 émet la même chaîne pour var0).
                case "GainLifelongGold":
                    if (a[0] != 0)
                        await GainLifelongGoldAsync(session, st, a[0],
                            token);
                    break;

                // --- GainColosseumGold <n> — idem pour var1.
                case "GainColosseumGold":
                    if (a[0] != 0)
                        await GainColosseumGoldAsync(session, st, a[0],
                            token);
                    break;

                // --- GainWarCoin/GainAlpacaCoin <n> — chaînes
                // émises par le sélecteur pour var2/var3 si la
                // donnée les produit isolément.
                case "GainWarCoin":
                    if (a[0] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"Update WarCoin Coin = {a[0]}", token);
                    break;
                case "GainAlpacaCoin":
                    if (a[0] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"Update AlpacaCoin Coin = {a[0]}", token);
                    break;
                case "GainKnightCoin":
                    if (a[0] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"GMGainSpecialCoin Type = 10 Money = {a[0]}",
                            token);
                    break;

                // --- GainSpecialCoin <type> <money> (2943) —
                // CEC_GainSpecialGold → DBA « GMGainSpecialCoin »
                // (mission : special_coins.coin{type+1} clampé
                // 0..9999999, texte 0x1100/0x1101 + 78 au client).
                case "GainSpecialCoin":
                    if (a[1] != 0)
                        await _mission.SendAgentDbaCommandAsync(
                            st.CharId,
                            $"GMGainSpecialCoin Type = {a[0]} " +
                            $"Money = {a[1]}", token);
                    break;

                // --- DistanceGoto <dist> <flag> — dash natif le long
                // du regard (CEC_DistanceGoto @0x8523900 :
                // ChangeFaceDir → CNode::BlockBegin →
                // CheckLineOfMoveUntilBlock → nouveau point ; émet le
                // 264 MoveChar self + 326 broadcast comme un déplacement
                // normal). Args vus sur items/skills : 40|1, 20|1, 30|2.
                case "DistanceGoto":
                case "DistanceGotoEx":
                {
                    var dist = Math.Clamp(a[0], 0, 60);
                    if (dist <= 0) break;
                    var hd = NativeHeading(st.Facing);
                    var nx = st.PosX + (float)(Math.Cos(hd) * dist);
                    var nz = st.PosZ + (float)(Math.Sin(hd) * dist);
                    st.PosX = nx; st.PosZ = nz;
                    await session.SendAsync(new CNC_CZ_ClientMoveChar
                    {
                        Field1 = nx, Field2 = nz,     // f32 x, f32 z
                    }, token);
                    await BroadcastMoveAsync(session, st, nx, nz, token);
                    await CheckAreaEntryAsync(session, st, token);
                    EdenLog.Write($"[Zone] DistanceGoto char={charId} " +
                        $"dist={dist} -> ({nx:F1},{nz:F1})");
                    break;
                }

                // --- Revive / RevivePlus <hp%> <mp%> — résurrection par
                // item (CEC_Revive @0x854d000 : revit sur place avec les
                // % donnés — le parchemin de resurrection cible un mort ;
                // usage solo = se ressusciter soi-même en état Dead).
                case "Revive":
                case "RevivePlus":
                {
                    if (!st.Dead) break;
                    var hpPct = a[0] > 0 ? a[0] : 10;
                    var mpPct = a[1] > 0 ? a[1] : 10;
                    st.Dead = false;
                    st.PlayerHp = Math.Max(1,
                        (int)(st.PlayerMaxHp * (long)hpPct / 100));
                    st.PlayerMp = Math.Max(1,
                        st.PlayerMaxMp * mpPct / 100);
                    st.InCombat = false; st.AutoAttacking = false;
                    st.CastingSpell = 0; st.TargetHandle = 0;
                    foreach (var m in _services.Live.MobsOf(st.NodeId, st.InstanceGroup))
                        if (m.TargetCharId == charId)
                            m.CancelCombat();
                    await session.SendAsync(new CNC_CZ_ClientRevive
                    { Field1 = st.CharId, Field3 = 1 }, token);
                    await SendVitalHpAsync(session, (uint)st.CharId,
                        st.PlayerHp, st.PlayerMaxHp, token);
                    await SendVitalMpAsync(session, (uint)st.CharId,
                        st.PlayerMp, st.PlayerMaxMp, token);
                    EdenLog.Write($"[Zone] ItemRevive char={charId} " +
                        $"hp={hpPct}% mp={mpPct}%");
                    break;
                }

                // --- ItemTriggerEvent <texteDebut> <nbPages> —
                // livre/parchemin lisible : CEC_ItemTriggerEvent envoie
                // ListDialogue type=8 npcUid=(debut<<16|pages) —
                // la mission répond la liste des pages S_Dialogue.
                case "ItemTriggerEvent":
                    if (_mission.Connected)
                    {
                        var packed = (a[0] << 16) | (a[1] & 0xffff);
                        await _mission.SendListDialogueAsync(
                            (uint)st.CharId, st.NodeId, packed, 8, token);
                    }
                    break;

                // --- OpenShop <type> <ref> — boutique à distance
                // (parchemins). Table CEC_OpenShop @0x8539c90 :
                // 1=SpellShop(349) 2=EquipShop 4=FamilyCreation
                // 5=Storage 6=Mail 7=Transport 8=Strengthen
                // 9=Combine 10=Auction 11=ElfReturn(439)
                // 12=MessageBoard, tout le reste → ItemShop.
                case "OpenShop":
                    switch (a[0])
                    {
                        case 1: // SpellShop — 349 [i32][u16 a][u16 b]
                            await session.SendAsync(
                                new CNC_CZ_ClientEnterSpellShop
                                {
                                    Field1 = 0,
                                    Field2 = (ushort)a[1],
                                    Field3 = (ushort)a[2],
                                }, token);
                            break;
                        case 2: // boutique d'équipement
                            await RunNpcServiceAsync(session, st, 1,
                                a[1], a[2], token);
                            break;
                        case 4: // création de famille
                            await RunNpcServiceAsync(session, st, 5,
                                a[1], a[2], token);
                            break;
                        case 5: // coffre (EnterStorage)
                            await RunNpcServiceAsync(session, st, 6,
                                a[1], a[2], token);
                            break;
                        case 6: // boîte aux lettres
                            await RunNpcServiceAsync(session, st, 7,
                                a[1], a[2], token);
                            break;
                        case 7: // transport
                            await RunNpcServiceAsync(session, st, 8,
                                a[1], a[2], token);
                            break;
                        case 8: // renforcement — CEC_OpenShop natif :
                                // CStrengthenOp + 432 avec npcUid=0
                                // (TargetInTalkingRange(0) → toujours vrai,
                                // le parchemin s'utilise partout).
                            await OpenShopOpAsync(session, st, 9, 9,
                                0, 0, token);
                            break;
                        case 9: // combinaison — CCombineOp + 432 npcUid=0
                            await OpenShopOpAsync(session, st, 10, 10,
                                0, 0, token);
                            break;
                        case 10: // hôtel des ventes
                            await RunNpcServiceAsync(session, st, 18,
                                a[1], a[2], token);
                            break;
                        case 11: // retour d'elfe — 439 [i32 npcUid]
                            // (Serialize natif : un seul int — les 6B
                            // émis avant désynchronisaient la trame).
                            await session.SendAsync(
                                new CNC_CZ_ClientEnterElfReturn
                                { Value = st.DialogNpcHandle }, token);
                            break;
                        case 12: // panneau d'affichage
                            await RunNpcServiceAsync(session, st, 20,
                                a[1], a[2], token);
                            break;
                        default: // CItemShop — tout autre type
                            await RunNpcServiceAsync(session, st, 3,
                                a[1], a[2], token);
                            break;
                    }
                    break;

                // --- SummonInEventArea <mob> <n> <node> <posKey> <fx>
                // <fxScale> — CEC_SummonInEventArea @0x08559b70 : spawn
                // à la ligne [position] de clé posKey (node requis,
                // contrôlé en amont par CanExecute) ou à la position du
                // lanceur ; le mob prend le lanceur pour cible. Détail
                // dans <see cref="QuestItemRules"/> /
                // ZoneEntryFlow.QuestItems.cs.
                case "SummonInEventArea":
                    await SummonInEventAreaAsync(session, st, a, token);
                    break;

                // --- UseTwoWeapon <a> <b> <c> — licence double-arme
                // (CEC_UseTwoWeapon @0x855bc30) : char+0x77e=1, les 3
                // args s'ajoutent aux attrs dual 0x4fe/0x500/0x502,
                // Notify → attr 1000 (flag) + attr 0x35 (dégâts mêlée
                // recalculés) puis broadcast. Effet = enchant : au
                // RollBack (expiration) le flag retombe — on le garde
                // donc en état de session, pas en DB.
                case "UseTwoWeapon":
                {
                    st.TwoWeaponUnlocked = true;
                    // attr 1000 = flag dual-wield (Notify natif envoie
                    // ClientUpdateAttr(id=1000, val=(byte)0x77e)).
                    await SendAttrAsync(session, (uint)st.CharId, 1000,
                        1, 1, token);
                    // attr 0x35 = dégâts mêlée recalculés — rejoue le
                    // calcul profil courant (même format que le burst
                    // d'entrée, valeur brute courante).
                    await SendAttrAsync(session, (uint)st.CharId, 0x35,
                        0, 0, token);
                    await session.SendAsync(
                        new CNC_CZ_ClientEnchantAdd
                        {
                            Field1 = charId,            // i32 acteur
                            Field2 = (short)enchantId,  // s16 enchant
                            Field3 = 0, Field4 = 0, Field5 = 0,
                            Field6 = 0, Field7 = 0,
                        }, token);
                    EdenLog.Write($"[Zone] UseTwoWeapon char={charId} " +
                        "-> dual wield débloqué (attr 1000)");
                    break;
                }

                // --- EnchantCancel <enchantId> … : retire un buff ---
                // CEC_EnchantCancel natif → RemoveEnchantByID : le
                // statut réel est retiré (300 + DB + rollback stats).
                // L'ancien 300 brut retirait une icône sans purger le
                // statut — l'effet restait actif (bug live).
                case "EnchantCancel":
                    await RemovePlayerEnchantAsync(session, st, a[0],
                        token);
                    break;

                // --- Charmes de taux (EXP/CP/éveil/drop) —
                // CEC_Item*RateUp::Execute modifie les champs de taux
                // du perso (player+0x506/0x508…) avec RollBack à
                // l'expiration : enchant temporisé RÉEL dont la durée
                // vient de l'item (S_Item cols 24/25), pas une icône
                // factice. Les % sont cumulés par le consommateur sur
                // st.Statuses (GrantCharExpAsync / GrantClassPointAsync).
                case "ItemExpRateUp":
                case "ItemWakenNpRateUp":
                case "ItemDropRateUp":
                case "ItemDropCntUp":
                case "ItemDropCntUpTime":
                case "ItemDpRateUp":
                    await AddPlayerEnchantAsync(session, st, charId,
                        enchantId, buffSeconds > 0 ? buffSeconds : -1,
                        1, token);
                    break;

                // --- ResetAA — Memory Tome : CEC_ResetAA::Execute
                // @0x0854bad0 → CCharacter::ResetAA @0x0841fca0
                // (reset de l'arbre de talents de la classe courante
                // + refund aa_point). Enchant 55149 des items
                // 40413/41467 (S_ItemMallEnchant).
                case "ResetAA":
                    await ResetAdvancedAbilitiesAsync(session, st,
                        token);
                    break;

                // --- AttrGainCollectItem <toolItemId> <chance%> —
                // outils de récolte kind 44 (pioche/gants/cristal) :
                // CEC_AttrGainCollectItem @0x085061a0 — pour chaque
                // produit de S_FamilyCollect (clé = CatalystItemId
                // col2 = l'item outil) un jet < taux octroie la
                // quantité (GainCollectItem @0x08505930). Restriction
                // = S_Item col32 (nodes;areaType) + rejet texte 0xe1c
                // (CanExecute @0x085056b0, vfunc+300). La consommation
                // du ticket/outil est déjà faite par le pipeline
                // d'usage (arg2 = chance de consommer l'outil natif,
                // 100 partout dans les données → équivalent).
                case "AttrGainCollectItem":
                {
                    var toolId = a[0];
                    var collect = _services.GameData.WorkCollectById
                        .Values.FirstOrDefault(w =>
                            w.CatalystItemId == toolId);
                    if (collect == null)
                    {
                        EdenLog.Write($"[Zone] CollectItem char={charId} " +
                            $"tool={toolId} : pas de S_FamilyCollect");
                        break;
                    }
                    var toolDef = _services.GameData.Items
                        .FirstOrDefault(i => i.Id == toolId);
                    var areaOk = toolDef == null ||
                        (toolDef.UseAreaNodeIds.Length == 0 ||
                         toolDef.UseAreaNodeIds.Contains(st.NodeId));
                    if (areaOk && toolDef?.UseAreaSubType > 0)
                    {
                        var sc = _services.Scenes
                            .GetScene($"S{st.NodeId:D3}");
                        areaOk = sc != null && sc.Gateways.Any(g =>
                            g.KeyId == toolDef.UseAreaSubType &&
                            sc.EventAreas.Any(e => e.Id == g.EventAreaId &&
                                MathF.Sqrt((e.X - st.PosX) * (e.X - st.PosX)
                                    + (e.Y - st.PosZ) * (e.Y - st.PosZ))
                                    <= e.Radius + 2f));
                    }
                    if (!areaOk)
                    {
                        // Rejet natif : texte 0xe1c (hors zone/outil).
                        await SendTextIndexAsync(session, 0xe1c, token);
                        break;
                    }
                    var touched =
                        new List<(int BagId, int Slot, bool Merged)>();
                    foreach (var p in collect.Products)
                    {
                        if (p.ItemId <= 0 || p.Quantity <= 0 ||
                            !(Random.Shared.NextDouble() * 100 < p.Rate))
                            continue;
                        var add = await _services.Inventory.AddItemAsync(
                            charId, p.ItemId, p.Quantity, 0, 0, false,
                            touched);
                        if (!add.Success)
                            EdenLog.Write($"[Zone] CollectItem char=" +
                                $"{charId} item={p.ItemId} inventaire plein");
                    }
                    if (touched.Count > 0)
                        await Codecs.NativeItemCodec.EmitItemSlotsAsync(
                            session, _services.InventoryRepo, charId,
                            touched, token);
                    EdenLog.Write($"[Zone] CollectItem char={charId} " +
                        $"tool={toolId} node={st.NodeId} " +
                        $"slots={touched.Count}");
                    break;
                }

                // --- AttrGainPrestigeExp <id> <exp> <chance> <x> ---
                // CEC_AttrGainPrestigeExp natif (Init @0x08506260 :
                // arg1=prestige_id 1..99, arg2=exp ≠0, arg3=chance
                // ≤100, arg4 ≥1 ; Execute @0x08506fb0 :
                // random()%100 < chance → AddPrestigeExp + PlayEffect).
                // Le gain monte au monde (persist + 60 + miroir).
                case "AttrGainPrestigeExp":
                    if (a.Length >= 3
                        && Random.Shared.Next(100) < a[2])
                        await GainPrestigeExpAsync(st,
                            (short)a[0], a[1], token);
                    break;

                default:
                    // Commandes attribut/buff (Attr*, OnAttackBuff*,
                    // CostMP*, AttrGain*, Item*RateUp, gains Elf,
                    // checks/rates…) : le natif pose l'enchant sur
                    // l'acteur — statut RÉEL via AddPlayerEnchantAsync
                    // (299 + persistance + expiration + annulation).
                    // L'ancien 299 brut (rest=0) créait une icône
                    // fantôme : ni le tick ni le clic droit NCID 85
                    // ne pouvaient la retirer (bug live « statuts qui
                    // ne se retirent pas »).
                    if (IsBuffApplyCommand(name) && !statusApplied)
                    {
                        statusApplied = true;
                        await AddPlayerEnchantAsync(session, st, charId,
                            enchantId, buffSeconds > 0 ? buffSeconds : -1,
                            1, token);
                    }
                    EdenLog.Write($"[Zone] ItemUse cmd char={charId} " +
                        $"enchant={enchantId} {name} " +
                        $"args=[{string.Join(',', a)}]");
                    break;
            }
        }
    }

    /// <summary>Commandes d'usage dont l'effet natif est l'application
    /// de l'enchant au lanceur (EnchantAdd 299 — paquet éprouvé pour
    /// les montures et buffs items). Liste dressée par croisement
    /// S_Enchant×S_Item(col23) et noms du générateur natif ; les
    /// commandes système one-shot (UseTwoWeapon, RoomExtend, OpenShop,
    /// summons/pets…) ne sont PAS ici — elles restent loguées.</summary>
    private static bool IsBuffApplyCommand(string name) =>
        name.StartsWith("Attr") ||
        name.StartsWith("OnAttackBuff") ||
        name.StartsWith("CostMP") ||
        name.StartsWith("Damage2HP") ||
        name.StartsWith("ItemExp") ||
        name.StartsWith("ItemDrop") ||
        name.StartsWith("ItemDpRateUp") ||
        name.StartsWith("ItemWaken") ||
        name.StartsWith("Elf") ||
        name.StartsWith("EnchantCheck") ||
        name.StartsWith("ChangePet") ||
        name.StartsWith("DamageAdd") ||
        name.StartsWith("SpellEffectChange") ||
        name.StartsWith("OnBuffEnd") ||
        name.StartsWith("Damage2MP") ||
        name is "Effect2Target" or "MultiStroke" or "SpellToSpell"
            or "SpellAttack" or "ModelAlpha" or "TransformPlus"
            or "LifeFloat" or "MusouCheck" or "SlotFlicker"
            or "SpellCardEffect" or "RideSpell" or "EffectNextID2Other"
            or "Weak" or "EnchantIncrease" or "PetAttackGiveBuff"
            or "EnchantKills" or "EnchantKillRandom"
            or "EnchantKillRandomForce";

    /// <summary>Gain de « Crystal Cross Medal » par consommable —
    /// équivalent zone-side de la chaîne native CEC_GainLLG → DBA
    /// « GMGainLifelong » → <c>CGlobalInfo::GainLifelongGold</c>
    /// @0x807a230 : exec+0x75c = lifelong_gold borné [0,9999999],
    /// texte 0x1100/0x1101 « %d,%s » (nom S_TextIndex 0x11ac),
    /// HandleRASpecialPoint(char,2,delta) puis 555 au client.
    /// Chez nous la zone est seule écrivaine de lifelong_gold
    /// (st.LifelongGold live + LifelongRepo).</summary>
    private async Task GainLifelongGoldAsync(NetSession session,
        EntryState st, int n, CancellationToken token)
    {
        var old = st.LifelongGold;
        st.LifelongGold = await _services.LifelongRepo.AddGoldAsync(
            (int)st.CharId, n);
        var gained = st.LifelongGold - old;
        // 555 ClientUpdateLifelongGold [i32] : nouvel or total.
        await session.SendAsync(new CNC_CZ_ClientUpdateLifelongGold
        { Value = st.LifelongGold }, token);
        // Texte natif 0x1100/0x1101 « %d,%s » — nom résolu S_TextIndex
        // 4524 (« Crystal Cross Medal »), émis même à 0 (gain cappé).
        var medal = _services.GameData.Texts
            .TryGetValue(LifelongMedalTextId, out var n1)
            ? n1 : "Crystal Cross Medal";
        await SendTextIndexArgsAsync(session,
            (short)(n > 0 ? 0x1100 : 0x1101),
            $"{Math.Abs(n)},{medal}", token);
        // HandleRASpecialPoint(char,2,gained) — activités EventKind 9
        // / EventParam 2 progressent de l'or lifelong gagné.
        if (gained > 0)
            await BumpRecommendLifelongGoldAsync(session, st, gained,
                token);
    }

    /// <summary>Gain de « Honor Stars » par consommable —
    /// CEC_GainColosseumGold → <c>CCharacter::GainColosseumGold</c>
    /// @0x8418f30 : char+0xbbc = colosseum_gold, refus + texte 0x600
    /// si 999999999 serait dépassé ; émet le 525
    /// ClientGladiatorTeamInfo [i32 evalId(char+0xbb4)]
    /// [i32 point(char+0xbb8)][u32 gold][u8 flag(char+0xbb0)], le
    /// texte 0x156 « Acquired %s1 » / 0xa8 « Spent %s1 » avec le nom
    /// S_TextIndex 0xfec (« Honor Stars »), la progression
    /// recommandée EventKind 9 / EventParam 3 et la persistance.</summary>
    private async Task GainColosseumGoldAsync(NetSession session,
        EntryState st, int n, CancellationToken token)
    {
        var (point, cur) = await _services.EvaluateRepo
            .GetGladiatorCurrenciesAsync((int)st.CharId);
        if ((long)cur + n > 999999999)
        {   // cap natif : TextOutputIndex 0x600, aucun crédit.
            await SendTextIndexArgsAsync(session, 0x600, "", token);
            return;
        }
        var total = (int)await _services.Characters
            .AddColosseumGoldAsync((int)st.CharId, n);
        // 525 ClientGladiatorTeamInfo — flag = en trial (evalId≠0),
        // comme char+0xbb0 lu par le natif.
        await session.SendAsync(new CNC_CZ_ClientGladiatorTeamInfo
        {
            Field1 = st.EvalId,         // i32 : evalId (char+0xbb4)
            Field2 = point,             // i32 : colosseum_point
            Field3 = (uint)total,       // u32 : colosseum_gold
            Field4 = (byte)(st.EvalId != 0 ? 1 : 0), // u8 : flag
        }, token);
        var name = _services.GameData.Texts
            .TryGetValue(0xfec, out var n1) ? n1 : "Honor Stars";
        await SendTextIndexArgsAsync(session,
            (short)(n > 0 ? 0x156 : 0xa8),
            $"{Math.Abs(n)} {name}", token);
        // HandleRASpecialPoint équivalent param 3 (boucle
        // char+0xc90..c98 du natif : kind 9, param 3 → progression).
        if (n > 0)
            await BumpRecommendSpecialPointAsync(session, st, 3, n,
                token);
    }

    /// <summary>Tirage uniforme dans [a,b] (ordre indifférent —
    /// CEC_HP::Init trie min/max et range=|a-b|).</summary>
    private static int RandRange(int a, int b)
    {
        var lo = Math.Min(a, b);
        return lo + Random.Shared.Next(Math.Abs(b - a) + 1);
    }
}
