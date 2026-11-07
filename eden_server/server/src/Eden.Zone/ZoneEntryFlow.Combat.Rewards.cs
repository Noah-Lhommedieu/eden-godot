using Eden.Core.Game.Events;
using Eden.Zone.Features.Modifiers;
using Eden.Core.Game.Modifiers;
using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game;
using Eden.Core.Game.Party;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Distribution des drops après la mort du mob —
    /// équivalent CMonster::DropItem/GenerateDrop + GiveClassItemDrop :
    ///   1. Table normale S_DropItem indexée par mobId (roll pondéré,
    ///      les chances somment < 100 — le reste = rien) ;
    ///   2. Tables breakpoint/classe S_ClassDropItem référencées par le
    ///      mob (S_Monster cols 40-49 → ClassDropIds) — chaque entrée a
    ///      sa chance propre ; la "class preference" native est lue de
    ///      la table (drop supplémentaire réservé à la classe du tueur
    ///      quand la table la désigne) ;
    ///   3. Or — header S_DropItem du mob (chance%/centre/spread,
    ///      GiveGold @08497200) ;
    /// Chaque item est inséré via InventoryService.AddItemAsync (auto-
    /// stack + slot libre) puis notifié au client par 274
    /// ClientPutItemIntoInventory (corps 54B validé par capture).
    /// TODO: drops temporaires de quête — nécessitent la liste des
    /// missions actives du joueur (MissionService) pour n'émettre que
    /// les quest-items des quêtes en cours ; à raccorder ici.</summary>
    private async Task DistributeLootAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob)
    {
        try
        {
            var def = _services.Live.MonsterDefs
                .TryGetValue(mob.MobId, out var md) ? md : null;

            var loots = new List<LootService.LootResult>();
            // 1. Drop normal (S_DropItem[mobId]) — majoré par le
            // perk Limitless « drop_rate » du tueur s'il existe.
            var dropMult = _services.Limitless != null
                ? await _services.Limitless.GetPerkValueAsync(
                    (int)st.CharId, "drop_rate")
                : 0;
            var rateMult = (dropMult > 0 ? dropMult : 1.0) *
                Eden.Core.Game.ServerConfig.EffectiveDropRate;
            // GenerateDrop(0) : `RollCount` tirages du pool normal.
            loots.AddRange(_services.Loot.RollDropPool(mob.MobId,
                breakPool: false, rateMult));
            // GiveItem @0x08496590 : `if (mob+0x78 < 1) GenerateDrop(1)` —
            // 2e pool « break » de S_DropItem (trophées, armures…) quand
            // la patience du mob est vidée à la mort.
            if (BreakPoolDrops(mob))
                loots.AddRange(_services.Loot.RollDropPool(mob.MobId,
                    breakPool: true, rateMult));
            // 2. Class drops (S_ClassDropItem référencés) —
            // GiveClassItemDrop @0x0849b3a0 : NON conditionné par la
            // rupture (seul `mob+0x3b0` « peut dropper » le gate).
            if (def != null)
                foreach (var tableId in def.ClassDropIds)
                    loots.AddRange(_services.Loot.RollClassDrop(tableId));
            // Drops d'événements actifs (server.config.txt Event.*.DropItem).
            foreach (var ed in Eden.Core.Game.ServerConfig.ActiveEventDrops())
                if (Random.Shared.NextDouble() * 100 < ed.ChancePct)
                    loots.Add(new LootService.LootResult(ed.ItemId,
                        ed.Quantity, $"event:{ed.Event}"));

            // Attributaire selon la règle de loot de la team — le
            // receveur doit être connecté à CETTE zone (session
            // migrée) ; sinon repli sur le tueur. Un loot par
            // attributaire à chaque itération (chaque roll réévalue).
            // Bulk : les items d'un même receveur sont insérés en un
            // seul passage (2 SELECTs au lieu de ~5 par item — P0 N+1).
            var receiverLoots = loots
                .Select(l => (Loot: l, St: ResolveLootReceiver(st)))
                .GroupBy(t => t.St.CharId);
            foreach (var grp in receiverLoots)
            {
                var lootSt = grp.First().St;
                var lootSession = lootSt.Session ?? session;
                var list = grp.ToList();
                var results = await _services.Inventory.AddItemsAsync(
                    (int)lootSt.CharId,
                    list.Select(t => (t.Loot.ItemId, t.Loot.Quantity,
                        0, false)).ToList());
                for (var i = 0; i < list.Count; i++)
                {
                    var loot = list[i].Loot;
                    var res = results[i];
                    if (!res.Ok)
                    {
                        // Inventaire plein → drop au sol en trésor
                        // (fallback natif : AddCloneItemWorld==NULL →
                        // DropTreasureOnFloor — jamais de perte sèche) +
                        // texte 0x4f9 « sac plein » pour que le joueur
                        // voie POURQUOI l'item n'entre pas en sac.
                        await SendTextIndexAsync(lootSession, 0x4f9,
                            CancellationToken.None);
                        await SpawnGroundTreasureAsync(lootSt,
                            loot.ItemId, loot.Quantity);
                        continue;
                    }
                    // Merge → 291 (nouveau count), nouveau slot → 274 —
                    // émis depuis l'entité (pas de re-SELECT).
                    foreach (var (bag, slot, merged, ent) in res.Touched)
                        await Codecs.NativeItemCodec.EmitItemSlotAsync(
                            lootSession, bag, slot, merged, ent,
                            CancellationToken.None);
                    // Succès de collection — SetMonsterDropItemAchievement
                    // natif : grant au receveur si le succès item est
                    // absent de sa map (dédup native).
                    await TryGrantItemAchievementAsync(lootSession,
                        lootSt, loot.ItemId);
                    // 146 zone→mission : réévalue les ItemCollect sur
                    // cet item (miroir UpdateQuestItemStatus natif).
                    if (_mission.Connected)
                        _ = _mission.SendQuestItemChangedAsync(
                            lootSt.CharId, loot.ItemId);
                    EdenLog.Write(
                        $"[Loot] char={lootSt.CharId} mob={mob.MobId} " +
                        $"-> item {loot.ItemId} x{loot.Quantity} " +
                        $"({loot.ItemName}) slots={res.Touched.Count}");
                }
            }

            // 3. Or — GiveGold natif @08497200 : chance %, puis
            // or = centre − spread + rand(spread×2) borne haute
            // EXCLUE (preuve ItemLog : 56001 (80|2|1) → 1-2 or/kill,
            // notre +1 donnait 1-3). Team ≠ type 3 → part égal entre
            // les membres locaux vivants (GetLocalMembers) ; sinon
            // le tueur garde tout.
            var goldDrop = _services.Loot.GetDropTable(mob.MobId);
            if (goldDrop is { GoldChancePct: > 0, GoldCenter: > 0 } &&
                Random.Shared.Next(100) < goldDrop.GoldChancePct)
            {
                var spread = Math.Max(0, goldDrop.GoldSpread);
                var gold = Math.Max(0, goldDrop.GoldCenter - spread +
                    Random.Shared.Next(spread * 2));
                gold = (int)Math.Min(int.MaxValue, gold *
                    Eden.Core.Game.ServerConfig.EffectiveGoldRate *
                    DungeonRewardMult(st.NodeId));
                if (gold > 0)
                    await DistributeGoldAsync(session, st, gold);
            }

            // 4. Drops de quête dynamiques — objectifs
            // `MonsterTreasureGot mobId rate% itemId count` des quêtes
            // ACTIVES du tueur (personal_mission_states state&0xff==1).
            // Plusieurs quêtes/mobs éligibles → un roll par objectif.
            // La progression avance à l'obtention (op 145 mission), pas
            // au kill.
            await DistributeQuestDropsAsync(session, st, mob);
        }
        catch (Exception ex)
        {
            // Loot non bloquant — ne jamais casser la mort — mais un
            // échec doit rester visible (un throw avant les drops de
            // quête les sautait silencieusement).
            EdenLog.Write(
                $"[Loot] char={st.CharId} mob={mob.MobId} : {ex.Message}");
        }
    }

    /// <summary>Notification d'insertion d'items — par slot touché :
    /// merge de stack → 291 ClientModifyItemDurability (nouveau count)
    /// ; nouveau slot → 274 ClientPutItemIntoInventory (record complet
    /// relu). Le natif émet 291 dans ce cas (BuyItems @0x80aca40) —
    /// un 274 sur un slot déjà occupé fait matérialiser un clone au-
    /// dessus de l'existant et crashe le client (bugs live : achat
    /// boutique, loot sur stack, déséquipement vers slot occupé).</summary>
    private Task NotifyItemSlotsAsync(NetSession session,
        EntryState st,
        List<(int BagId, int Slot, bool Merged)> touched,
        CancellationToken token) =>
        Codecs.NativeItemCodec.EmitItemSlotsAsync(session,
            _services.InventoryRepo, (int)st.CharId, touched, token);

    /// <summary>Drop dynamique d'items de quête — `MonsterTreasureGot`
    /// `[mobId][rate%][itemId][count]` (S_Mission col16) : pour chaque
    /// quête active du joueur ciblant ce mob, roll rate% ; si l'item
    /// entre en sac → 274 au client + op 145 à la mission (crédit de
    /// collecte). Cap : pas de drop au-delà du count requis ni si le
    /// joueur porte déjà assez d'exemplaires de l'item.</summary>
    private async Task DistributeQuestDropsAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob)
    {
        var states = await _services.MissionRepo
            .GetPersonalMissionStatesAsync((int)st.CharId);
        foreach (var (mid, state) in states)
        {
            // Les lignes index ≥ 1 000 000 sont des compteurs PMS, pas
            // des quêtes — GetMission retournerait null de toute façon.
            if (mid >= 1_000_000 || (state & 0xff) != 1) continue;
            var m = _services.Missions.GetMission(mid);
            if (m == null) continue;
            foreach (var o in m.Actions)
            {
                if (o.Name != "MonsterTreasureGot" || o.Args.Count < 4 ||
                    !int.TryParse(o.Args[0], out var targetMob) ||
                    targetMob != mob.MobId ||
                    !int.TryParse(o.Args[1], out var rate) ||
                    !int.TryParse(o.Args[2], out var itemId) ||
                    !int.TryParse(o.Args[3], out var count))
                    continue;
                if (rate <= 0) continue;
                // Cap par stock : si le joueur porte déjà `count`
                // exemplaires, ne plus faire tomber l'item. Le packed
                // `state>>8` de personal_mission_states N'est PAS un
                // compteur d'objectif fiable (valeurs persistées par
                // d'anciens chemins : test1 avait progress=2 sur une
                // quête count=1 → jamais de drop). Le natif n'a aucun
                // gate équivalent : TC_MonsterDropTreasure roll rate%
                // puis MonsterDropQuestItem — le cap d'inventaire
                // suffit.
                var owned = (await _services.InventoryRepo
                        .GetItemsByCharacterIdAsync((int)st.CharId))
                    .Where(i => i.ItemId == itemId)
                    .Sum(i => Math.Max(1, i.Quantity));
                if (owned >= count) continue;
                if (Random.Shared.Next(100) >= rate) continue;

                var touched =
                    new List<(int BagId, int Slot, bool Merged)>();
                var (ok, _, _, _) = await _services.Inventory
                    .AddItemAsync((int)st.CharId, itemId, 1,
                        0, 0, false, touched);
                if (!ok)
                {
                    // Sac plein — même fallback que le loot normal :
                    // trésor au sol tagué par la mission (le pickup
                    // enverra l'op 145 et créditera l'objectif).
                    await SendTextIndexAsync(session, 0x4f9,
                        CancellationToken.None);
                    await SpawnGroundTreasureAsync(st, itemId, 1, mid);
                    continue;
                }
                // Merge → 291, nouveau slot → 274 (même règle que le
                // loot normal — cf. DistributeLootAsync).
                await NotifyItemSlotsAsync(session, st, touched,
                    CancellationToken.None);
                // Succès de collection — MonsterDropQuestItem natif
                // passe par le même SetItemAchievement que le loot.
                await TryGrantItemAchievementAsync(session, st, itemId);
                if (_mission.Connected)
                {
                    _ = _mission.SendQuestItemGotAsync(st.CharId, mid,
                        itemId);
                    // 146 : réévalue aussi les ItemCollect sur cet item.
                    _ = _mission.SendQuestItemChangedAsync(st.CharId,
                        itemId);
                }
                EdenLog.Write(
                    $"[Loot] char={st.CharId} quête {mid} : drop " +
                    $"quest-item {itemId} slots={touched.Count}");
            }
        }
    }

    /// <summary>Class EXP du kill — équivalent natif
    /// `GiveTeamNp → CCharacter::GainClassPoint` : le montant suit
    /// l'exp ajustée du kill (même échelle que GainExp ; les
    /// multiplicateurs ClassExpRate/Dice natifs sont ignorés —
    /// approximation documentée). Persiste `player_classlist`,
    /// gère les paliers de niveau de classe (S_Level col25 =
    /// level×10000, cap au niveau perso comme le natif), puis
    /// rafraîchit le client : 489 PlayerClassList complet (queue de
    /// GainClassPoint natif). Les quêtes n'y passent PAS — seuls les
    /// kills accordent de l'exp de classe (sauf RewardClassExp
    /// explicite, traité par la mission).</summary>
    private async Task GrantClassPointAsync(NetSession session,
        EntryState st, long amount)
    {
        if (amount <= 0 || st.ClassId <= 0) return;
        // Charmes de classe / d'éveil séparés + VIP : moteur de modificateurs.
        var cctx = PlayerModifierContext.For(st, MapKinds);
        amount = (long)(amount * _services.Modifiers().Resolve(ModifierKeys.ExpFor(cctx.ClassKind), cctx));
        var cp = await _services.Classes
            .GetClassProgressAsync((int)st.CharId, st.ClassId);
        if (cp == null) return;

        long exp = cp.ClassExp + amount;
        int lv = Math.Max(1, cp.ClassLevel);
        var startLv = lv;
        var leveled = false;
        // Paliers natifs : LoadClassLevelExp → table[classLv] =
        // S_Level col11 (struct+0x2c) — lv1→2 = 2000, lv2→3 = 7000…
        // Cap : le niveau de classe ne dépasse pas le niveau perso
        // (natif : seuil trouvé seulement si class_lv < char+0x470,
        // sinon exp clampée à seuil-1 — GainClassPoint @0x841c3xx).
        while (lv < st.Level)
        {
            var threshold = _services.GameData.Levels
                .FirstOrDefault(l => l.Level == lv + 1)?.ClassExpNeeded
                is > 0 and var t ? (long)t : -1L;
            if (threshold <= 0 || exp < threshold) break;
            exp -= threshold;
            lv++;
            leveled = true;
        }
        if (lv >= st.Level)
        {
            var cap = (_services.GameData.Levels
                .FirstOrDefault(l => l.Level == lv + 1)?.ClassExpNeeded
                is > 0 and var c ? (long)c : 0L) - 1;
            if (cap < 0) cap = 0;
            if (exp > cap) exp = cap;
        }
        await _services.Classes.SetClassProgressAsync(
            (int)st.CharId, st.ClassId, lv, exp);
        // GainNp : le natif accorde la MÊME valeur iVar6 aux skill
        // points (+0x488 → colonne `point`) — monnaie d'apprentissage
        // des sorts de classe (UseSkillPoint NCID 12). Sans ça le
        // joueur montait de niveau de classe sans jamais gagner de
        // point à dépenser ("pas de level-up de classe" live).
        // GiveNp natif (désasm @0x8491ac2) : le kill appelle
        // GainClassPoint(np) — barre d'exp de classe — PUIS
        // GainNp(np,false) — les CP dépensables (+0x488, colonne
        // `point`). Même montant pour les deux, exactement comme
        // demandé : 1 point de classe par point d'exp de classe.
        var cpTotal = await _services.Classes.AddClassPointsAsync(
            (int)st.CharId, st.ClassId, (int)Math.Min(int.MaxValue,
                amount));
        // ClientUpdateAttr attr=0x3ef = skill_point total (désasm
        // GainNp @0x840bac0 : piVar5[3]=*(this+0x488), attr u16=0x3ef).
        // Sans lui l'UI de montée de skills restait à 0 CP.
        await SendAttrAsync(session, st.CharId, 0x3ef, cpTotal, 0,
            CancellationToken.None);
        // Barre d'exp de classe du HUD : le natif termine TOUJOURS
        // GainClassPoint par un ClientUpdateAttr attr=6 portant le
        // class_point courant (char+0x484 — désasm @0841c24b :
        // local_50=6, local_4c=class_point). Sans lui le client ne
        // montre jamais l'exp de classe gagnée au kill.
        await SendAttrAsync(session, st.CharId, 6, (int)Math.Min(
            int.MaxValue, exp), 0, CancellationToken.None);
        // Feed chat natif (GainClassPoint @0x841c24b) : TextOutputIndex
        // 0x3c9 = 969 « Gained %s1 Class EXP » avec le montant — canal
        // « EXP Acquired » des chat settings.
        await session.SendAsync(
            new CNC_CZ_ClientTextOutputIndex
            { Field1 = 969, Field2 = amount.ToString() },
            CancellationToken.None);
        if (leveled)
        {
            st.ClassLevel = lv;
            // Level-up de classe natif (GainClassPoint @0x841c140) :
            //   · 284 ClientLevelUp flag u8=1 (variante CLASSE — le 0
            //     est réservé au level-up perso) + broadcast
            //   · GainAAPoint +1 (colonne aa_point)
            //   · AddClassFromLimit + GetClassSpell : spellmasters du
            //     nouveau palier appris → 286+316 par nouveau sort
            //   · RefreshEQsAttr (stats équipement) — le 489 + vitals
            //     ci-dessous couvrent l'essentiel visible.
            // u8=1 : variante CLASSE (le 0 = level-up perso).
            var lvUp = new CNC_CZ_ClientLevelUp
            {
                Field1 = st.CharId, Field2 = 1,
                Field3 = (short)lv,
                Field4 = st.PlayerMaxHp, Field5 = st.PlayerMaxMp,
            };
            await session.SendAsync(lvUp, CancellationToken.None);
            // GainClassPoint natif @0x841c140 : FastBroadCastToCanSee
            // — les observateurs jouent aussi l'effet de level-up.
            await BroadcastToNodeAsync(session, st, lvUp,
                CancellationToken.None);
            // GainAAPoint(this,1) natif @0x840c7f0 : +1 sur
            // player_characters.aa_point (cap 120) PAR NIVEAU gagné —
            // le natif ne monte qu'un niveau par GainClassPoint et
            // SetClassLevel (@0x841c9c0) boucle niveau par niveau ;
            // ici la boucle while peut franchir plusieurs paliers en
            // un appel (gros gain d'exp, //clevel) → accorder
            // lv-startLv points, pas 1 (sous-octroi live : pool AA
            // trop pauvre pour les paliers de l'arbre de talents).
            var aa = await _services.Classes.AddAaPointAsync(
                (int)st.CharId, lv - startLv);
            if (aa >= 0)
                await SendAttrAsync(session, st.CharId, 0x31, aa, aa,
                    CancellationToken.None);
            // Sorts du nouveau palier de classe (données
            // ClassSkillTable — aucune liste en dur).
            var cdef2 = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, st.RaceId, st.ClassId);
            var known = (await _services.Classes
                    .GetPlayerSpellsAsync((int)st.CharId))
                .Select(x => unchecked((ushort)x.Spell)).ToHashSet();
            var learnedAny = false;
            var freshSpells = new HashSet<ushort>();
            foreach (var sm in Eden.Core.Game.Classes.ClassSkillTable
                    .GrantableSpells(st.ClassId, lv,
                        _services.GameData.Spells, cdef2, st.RaceId))
                if (known.Add(unchecked((ushort)sm)))
                {
                    await _services.Classes.LearnSpellAsync(
                        (int)st.CharId, unchecked((short)sm));
                    _services.SpellLevelsDirty?.Invoke((int)st.CharId);
                    learnedAny = true;
                    freshSpells.Add(unchecked((ushort)sm));
                }
            if (learnedAny)
                await SendPlayerSpellsAsync(session, st,
                    CancellationToken.None, freshSpells);
            // Certificats des paliers franchis — GainClassPoint natif
            // @0x841c7xx : pour CHAQUE multiple de 10 atteint (pas
            // seulement le niveau final — un /clevel 5→25 doit
            // octroyer les certs de 10 ET 20, comme la boucle
            // SetClassLevel→GainClassPoint native), GetLicense(
            // certs[lv/10 - 1]) (vecteur CClassData+0x44 = S_Class
            // c22-29) puis RemoveLicense(CItemData+0x8c) de l'éventuel
            // tier inférieur. Chaque octroi émet un 509
            // ClientPlayerLicense {itemId, loc=0} — notification
            // native de GetLicense (@0x80fce80 : FastNotifyClient
            // avec un vecteur d'une entrée).
            if (cdef2 != null)
                for (var m = startLv + 1; m <= lv; m++)
                {
                    if (m % 10 != 0) continue;
                    var certIdx = m / 10 - 1;
                    if (certIdx < 0
                        || certIdx >= cdef2.CertificateIds.Length
                        || cdef2.CertificateIds[certIdx] <= 0)
                        continue;
                    var licId = unchecked(
                        (short)cdef2.CertificateIds[certIdx]);
                    await _services.Licenses.GrantLicenseAsync(
                        (int)st.CharId, licId);
                    // 509 : notification d'octroi natif {s16 id,u8 loc}
                    await session.SendAsync(new CNC_CZ_ClientPlayerLicense
                    {
                        Licenses = { (licId, 0) },    // {s16 id, u8 loc}
                    }, CancellationToken.None);
                }
            EdenLog.Write(
                $"[Combat] char={st.CharId} CLASS LEVEL UP " +
                $"class={st.ClassId} -> {lv}");
        }
        // Refresh client : le natif termine GainClassPoint par un
        // ClientPlayerClassList (489) + ClientUpdateAttr. On réutilise
        // le builder 489 existant (re-requête DB).
        await HandleQueryClassListAsync(session, st, CancellationToken.None);
        // RefreshJPAward natif : le palier de classe franchi peut
        // activer un enchant de bonus (classLv×0.2) — le 296 émis
        // recalcule la contribution cumulée de toutes les classes.
        if (leveled)
        {
            await ProfileStatEmitter.SendForCharacterAsync(session,
                _services, (int)st.CharId, CancellationToken.None);
            // AddClassFromLimit natif (GainClassPoint) — KB-03.
            await UnlockClassesFromLimitAsync(session, st,
                CancellationToken.None);
        }
    }

    /// <summary>Exp du kill — équivalent CCharacter::GainExp
    /// (zone/decompiled.c ~0x841d2b0). Émet 600 ClientUpdateAttrNewExp
    /// `[i32 charId][i64 exp]` — Serialize natif : 3 i32 (handle + exp
    /// bas/haut). Sur level-up : 284 ClientLevelUp
    /// `[u32 char][u8 0][s16 level][i64 maxHp][i32 maxMp]` (Serialize
    /// @0x81e97xx : u32+u8+s16+i64+i32) puis refresh des max vitals et
    /// attrs HP/MP comme le natif (UpdateAttr après stats).</summary>
    private async Task GrantKillExpAsync(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        try
        {
            // Taux serveur (config + événements) via le moteur de modificateurs ; gains de mobs seulement.
            var ctx = PlayerModifierContext.For(st, MapKinds);
            var mods = _services.Modifiers(); DomainEventBus.Shared.Publish(new MobKilled((int)st.CharId, mob.MobId, st.NodeId));
            var exp = (long)((_services.Live.MonsterDefs
                .TryGetValue(mob.MobId, out var md) ? md.ExpReward : 0)
                * mods.Resolve(ModifierKeys.ExpKillCharacter, ctx) * DungeonRewardMult(st.NodeId));
            // EXP de CLASSE : le kill nourrit la classe active avec le NP du mob (pas l'exp perso).
            var classExp = (long)((_services.Live.MonsterDefs
                .TryGetValue(mob.MobId, out var mdef) ? mdef.ClassExp : 0)
                * mods.Resolve(ModifierKeys.ExpKillFor(ctx.ClassKind), ctx) * DungeonRewardMult(st.NodeId));
            if (classExp > 0)
                await GrantClassPointAsync(session, st, classExp);
            // GiveDp/GiveTeamDp natif (@0x8491850/@0x8493fxx) : la
            // réserve DP de la route du destin se nourrit des kills —
            // indépendant de l'exp (GiveReward natif l'appelle à côté
            // de GiveExp/GiveNp, sans condition d'exp).
            await DistributeKillDpAsync(st, mob);
            if (exp <= 0) return;
            // GiveTeamExp natif (@0x849d1xx) : membres locaux de la
            // team (même node, vivants) — part ExpForTeam pour ceux à
            // ±10 du niveau max, ExpForTeamNoShare pour le tueur hors
            // fenêtre, 0 pour les autres. Solo/1 membre → part entière.
            // Exp Ultime (chantier 3 — LIMITLESS_SYSTEM_DESIGN) :
            // le tueur au cap de niveau gagne 1 point Limitless si
            // le mob est à ±5 niveaux ET qu'il a lancé un sort
            // dessus (pas de leech à l'auto-attaque). Le multiplicateur
            // de perk 'exp_ultime_mult' majore le point de base.
            if (_services.Limitless != null &&
                st.Level >= _services.Progression.MaxLevel &&
                Math.Abs(mob.Level - st.Level) <= 5 &&
                mob.SkillHitCharIds.Contains((int)st.CharId))
            {
                var mult = await _services.Limitless
                    .GetPerkValueAsync((int)st.CharId,
                        "exp_ultime_mult");
                var pts = Math.Max(1,
                    (int)Math.Round(1 * (mult > 0 ? mult : 1)));
                var g = await _services.Limitless.AddExpAsync(
                    (int)st.CharId, pts);
                await SendLimitlessAsync(session, st, CancellationToken.None);
                // Plan B du design §5.1 : en attendant le hook UI
                // client, le level-up Limitless s'affiche en chat
                // système (canal 6) — visible sur les deux clients.
                if (g.LeveledUp)
                    await session.SendAsync(
                        new Eden.Core.Networking.Messages.World
                            .ChatMessage
                        {
                            SenderName = "[Limitless]",
                            Channel = 6,
                            Message = $"Ultimate Level up! " +
                                $"Lv {g.Level} reached.",
                        }, CancellationToken.None);
            }
            var shares = ComputeTeamExpShares(st, mob.Level,
                (int)Math.Min(exp, int.MaxValue));
            foreach (var (ost, share) in shares)
            {
                if (share <= 0) continue;
                // AddLifelongPoint(m,3) natif — tout receveur d'exp
                // >0 incrémente son daily_monster_kill (554 type 3,
                // flush pending >9) avant le GainExp.
                await AddLifelongDailyPointAsync(
                    ost.Session ?? session, ost,
                    CancellationToken.None);
                await GrantMobExpToAsync(ost, share);
            }
            // Succès de kill natif : monsterdata+0x124 pointe la
            // CAchievementData dont l'id == template id du mob
            // (types 1/2 de S_Achievement) ; zone l'émet par receveur
            // d'exp quand le succès est absent de la map perso OU que
            // la donnée porte un point ≠ 0 (« Achievement
            // MonsterKill », Monster.cc @0x8499xxx).
            var achDef = _services.Achievements.Get(mob.MobId);
            if (achDef != null && achDef.Type is 1 or 2)
                foreach (var (ost, share) in shares)
                {
                    if (share <= 0) continue;
                    // Type 2 lifelong (monstre rare) : uniquement un
                    // succès Type==1 avec Point>0, et répétable — le
                    // natif re-crédite à chaque kill même si le succès
                    // est déjà enregistré (achAbsent || Point≠0).
                    if (achDef.Type == 1 && achDef.Point > 0)
                        await AddLifelongPointAndGoldAsync(
                            ost.Session ?? session, ost, 2,
                            achDef.Point, CancellationToken.None);
                    await GrantMobAchievementAsync(session, ost,
                        achDef);
                }
        }
        catch (Exception ex)
        {
            // Exp non bloquante pour la mort du mob, mais jamais
            // silencieuse — un échec ici était invisible en live.
            EdenLog.Write(
                $"[Combat] exp erreur char={st.CharId} mob={mob.MobId} : " +
                $"{ex.Message}");
        }
    }

    /// <summary>Résout l'attributaire d'un drop de kill : la règle de
    /// loot de la team du tueur (<see cref="TeamService.PickLootReceiver"/>)
    /// parmi les membres connectés à cette zone sur le même node —
    /// repli sur le tueur hors team ou sans éligible.</summary>
    private EntryState ResolveLootReceiver(EntryState st)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null) return st;
        EntryState? Find(int cid)
        {
            foreach (var ost in _states.Values)
                if ((int)ost.CharId == cid && ost.Migrated &&
                    ost.NodeId == st.NodeId && ost.Session != null &&
                    ost.InstanceGroup == st.InstanceGroup)
                    return ost;
            return null;
        }
        var cid = _services.Teams.PickLootReceiver(team.Id,
            (int)st.CharId, id => Find(id) != null);
        return cid is int id ? Find(id) ?? st : st;
    }

    /// <summary>Or de kill — branche team de <c>CMonster::GiveGold</c>
    /// @0x8497200 : si le tueur est en team dont le type n'est pas 3,
    /// l'or est jeté une fois puis divisé entre les membres locaux
    /// (même node, vivants) ; chacun reçoit sa part + attr 0x02 sur
    /// sa propre session. Solo/type-3 → le tueur garde tout.</summary>
    private async Task DistributeGoldAsync(NetSession session,
        EntryState st, int gold)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        var receivers = team == null || team.TeamType == 3
            ? new List<EntryState> { st }
            : LocalTeamMembers(st, team);
        if (receivers.Count == 0) receivers.Add(st);
        var share = gold / receivers.Count;
        if (share <= 0) return;
        foreach (var ost in receivers)
        {
            ost.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)ost.CharId, share);
            // 280 attr 0x02 = gold (GainGold→ClientUpdateAttr).
            var os = ost.Session ?? session;
            await SendAttrAsync(os, ost.CharId, 0x02,
                (int)Math.Min(ost.Gold, int.MaxValue), 0,
                CancellationToken.None);
        }
    }

    /// <summary>Membres de la team présents sur le même node —
    /// <c>CForceTeam::GetLocalMembers</c> : session migrée, vivante.</summary>
    private List<EntryState> LocalTeamMembers(EntryState st,
        TeamEntity team)
    {
        var locals = new List<EntryState>();
        foreach (var ost in _states.Values)
            if (ost.CharId != 0 && ost.Migrated &&
                ost.NodeId == st.NodeId && ost.PlayerHp > 0 &&
                ost.InstanceGroup == st.InstanceGroup &&
                ost.Session != null &&
                team.Members.Exists(m =>
                    m.CharacterId == (int)ost.CharId))
                locals.Add(ost);
        return locals;
    }

    /// <summary>Type de la map courante — `CNodeTemplate+0x2c`
    /// (S_Node col 3 : 1=normale, 5=instance, 7=cas team 6-10,
    /// 0xc=type-4 seule).</summary>
    private int NodeTypeOf(EntryState st)
        => _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;

    /// <summary>Calcule les parts d'exp de groupe — transcription de
    /// <c>CMonster::GiveTeamExp</c> @0x849d1xx :
    /// <list type="bullet">
    ///   <item>TeamType 3 (raid25, +0x64==3) → GiveExp solo ;</item>
    ///   <item>TeamType 4 → uniquement les maps type 0xc, sinon
    ///         aucun gain ;</item>
    ///   <item>capacité ∈ [6,10] → partage seulement en map type 7 ;</item>
    ///   <item>membres locaux (même node, vivants) ; ceux à ±10 du
    ///         niveau max reçoivent
    ///         <see cref="NativeCombatFormulas.ExpForTeam"/> sauf si
    ///         le flag no-share (+0x56) est levé ;</item>
    ///   <item>hors fenêtre : le tueur reçoit
    ///         <see cref="NativeCombatFormulas.ExpForTeamNoShare"/>,
    ///         les autres 0.</item>
    /// </list>
    /// Sans team ou 1 membre local → le tueur garde tout (GiveExp).</summary>
    private List<(EntryState st, int share)> ComputeTeamExpShares(
        EntryState st, int mobLevel, int baseExp)
    {
        var solo = new List<(EntryState, int)> { (st, baseExp) };
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null || team.TeamType == 3)
            return solo;
        // Le natif passe les team à capacité 6-10 (donjons/raids
        // spéciaux) uniquement en map de type 7 ; les types-4
        // uniquement en map 0xc — ailleurs aucun gain n'est émis.
        var nodeType = NodeTypeOf(st);
        if (team.TeamType == 4)
        {
            if (nodeType != 0xc)
                return new List<(EntryState, int)>();
        }
        else if (team.MaxSize is >= 6 and <= 10 && nodeType != 7)
            return solo;
        var locals = LocalTeamMembers(st, team);
        if (locals.Count <= 1)
            return solo;

        // Σniveaux / niveau max — les membres à >9 du max sont exclus
        // du décompte et de la somme (natif : sVar10, local_78).
        var maxLv = 0; var sumLv = 0;
        foreach (var ost in locals)
        {
            sumLv += ost.Level;
            if (ost.Level > maxLv) maxLv = ost.Level;
        }
        var n = locals.Count;
        foreach (var ost in locals)
            if (maxLv - ost.Level > 9) { n--; sumLv -= ost.Level; }

        var p = CombatParams();
        var scaled = (int)Math.Min(int.MaxValue,
            (long)Math.Round(baseExp
                * NativeCombatFormulas.ExpDeclineRate(maxLv, mobLevel, p)));
        var shares = new List<(EntryState, int)>();
        foreach (var ost in locals)
        {
            int share;
            if (maxLv - ost.Level < 10 && !team.ExpNoShare)
                share = NativeCombatFormulas.ExpForTeam(scaled, n,
                    ost.Level, sumLv, p);
            else if (ost == st)
                share = NativeCombatFormulas.ExpForTeamNoShare(baseExp,
                    locals.Count, ost.Level, mobLevel, p);
            else share = 0;
            if (share > 0) shares.Add((ost, share));
        }
        return shares;
    }

    /// <summary>Crédite une part d'exp à un membre — séquence native
    /// par membre : texte 248 (bonus rate), EXP d'engrenage
    /// (share × S_Parameter 496), GainExp (600 + level-up), puis part
    /// complète à l'elf invoqué (CKFElf::GainExp).</summary>
    private async Task GrantMobExpToAsync(EntryState ost, int share)
    {
        var session = ost.Session;
        if (session == null) return;
        await session.SendAsync(
            new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 248,
                Field2 = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1:0.0}", share, _services.ExpRate - 1.0),
            }, CancellationToken.None);
        await GrantGearExpAsync(session, ost, share);
        var gained = await GrantCharExpAsync(session, ost, share);
        await GrantElfExpAsync(session, ost, share,
            CancellationToken.None);
        if (gained > 0) { DomainEventBus.Shared.Publish(new LevelUp((int)ost.CharId, ost.Level));
            EdenLog.Write(
                $"[Combat] char={ost.CharId} LEVEL UP -> {ost.Level} " +
                $"(+{gained}) exp={ost.Experience}"); }
    }

    /// <summary>EXP de Mana Infusion au kill — pipeline natif
    /// <c>GiveTeamExp → GainEquipExp</c> @0x849d190/0x841b1d0 :
    /// <list type="bullet">
    ///   <item>gain = charExp × S_Parameter 496, validations natives
    ///         dans <c>EquipTrainService</c> ;</item>
    ///   <item>316 texte 5494 « Acquire %s1 gear EXP » (crédité =
    ///         gain − reliquat non bankable natif) ;</item>
    ///   <item>316 texte 5495 « Gear has upgraded to Lv %s1 » pour
    ///         CHAQUE palier franchi (boucle native) ;</item>
    ///   <item>369 record complet de l'item à chaque crédit (le natif
    ///         émet ClientModifyItemInfo en fin de GainEquipExp) ;</item>
    ///   <item>refresh des stats si palier (RefreshEQsAttr natif —
    ///         Backup/Reset HP-MP-SP).</item>
    /// </list></summary>
    private async Task GrantGearExpAsync(NetSession session,
        EntryState st, long charExp)
    {
        var gearExp = _services.EquipTrain.GearExpFor(charExp);
        if (gearExp <= 0) return;
        // Archétype de la classe active — CClassData+0x28 natif
        // (gate kind%3 sur les locs d'armure 1-5).
        var cls = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, st.RaceId, st.ClassId);
        var o = await _services.EquipTrain.GainEquipExpAsync(
            (int)st.CharId, st.Level, cls?.Archetype ?? 0, gearExp);
        if (!o.Changed || o.Item == null) return;
        // 5494 — crédité = param_1 − reliquat (local_38) natif.
        if (o.CreditedExp > 0)
            await session.SendAsync(
                new CNC_CZ_ClientTextOutputIndex
                { Field1 = 5494, Field2 = o.CreditedExp.ToString() },
                CancellationToken.None);
        // 5495 — un message par palier (TextOutputIndex dans la
        // boucle native, arg = nouveau niveau).
        for (var lv = o.LevelBefore + 1; lv <= o.Level; lv++)
            await session.SendAsync(
                new CNC_CZ_ClientTextOutputIndex
                { Field1 = 5495, Field2 = lv.ToString() },
                CancellationToken.None);
        // 369 — record complet après ModifyItemTrain (ctor natif
        // alimenté par le clone mis à jour).
        var row = await _services.InventoryRepo
            .GetItemFullRowAsync(o.Item.Id);
        if (row != null)
        {
            var def = _services.GameData.Items
                .FirstOrDefault(d => d.Id == o.Item.ItemId);
            await session.SendPacketAsync(369,
                Codecs.MagicBoxPackets.BuildModifyItemInfo(row,
                    o.ContainerType, o.ContainerKey, o.VecIndex,
                    (byte)(def != null && Eden.Core.Game.Inlay
                        .InlayService.IsSocketCapable(def) ? 1 : 0)),
                CancellationToken.None);
        }
        // RefreshEQsAttr natif — seulement quand un palier a changé
        // les contributions (bVar25 du désasm).
        if (o.Leveled)
            await ProfileStatEmitter.SendForCharacterAsync(session,
                _services, (int)st.CharId, CancellationToken.None);
    }

    /// <summary>Grant d'XP perso partagé (kill + récompense quête) :
    /// persiste via ProgressionService, émet 600
    /// ClientUpdateAttrNewExp `[i32 handle][i64 exp]` à chaque gain,
    /// puis 284 ClientLevelUp + refresh vitals si palier franchi.
    /// Retourne le nombre de niveaux gagnés.</summary>
    private async Task<int> GrantCharExpAsync(NetSession session,
        EntryState st, long exp)
    {
        // Charmes + VIP : moteur de modificateurs (bonus % additifs ; retombent à l'expiration).
        exp = (long)(exp * _services.Modifiers().Resolve(ModifierKeys.ExpCharacter, PlayerModifierContext.For(st, MapKinds)));
        var (newLevel, gained) = await _services.Progression
            .GrantExpAsync((int)st.CharId, exp, st.Level,
                st.Experience);
        // Même boucle que le service pour l'exp restante.
        var spent = 0L;
        for (var lv = st.Level; lv < newLevel; lv++)
            spent += _services.Progression.ExpForLevel(lv);
        st.Experience = newLevel >= _services.Progression.MaxLevel
            ? 0 : st.Experience + exp - spent;
        st.Level = newLevel;
        // L'UPDATE exp/level ci-dessus est direct (hors file ordonnée) :
        // un save périmé déjà enfilé le réécrirait par-dessus — la
        // marque le rend caduc sans attendre la prochaine sauvegarde.
        _ = NextSaveMark(st.CharId);
        // CCharacter::SetLevel natif — retire l'état « portail
        // gratuit » (-0x25c3 = 55869) dès que le niveau dépasse 0x50
        // (80) ; le sync idempotent couvre aussi le re-add sur
        // descente //level admin.
        if (gained > 0)
            await SyncBeginnerPortalStateAsync(session, st,
                CancellationToken.None);

        await session.SendAsync(new CNC_CZ_ClientUpdateAttrNewExp
        {
            Field1 = (int)st.CharId, Field2 = st.Experience,
        }, CancellationToken.None);
        // Feed chat natif (GainExp @0x841cb80) : TextOutputIndex
        // 0x3c8 = 968 « Gained %s1 Character EXP » émis pour tout gain
        // non nul — alimente le canal « EXP Acquired » du chat.
        await session.SendAsync(
            new CNC_CZ_ClientTextOutputIndex
            { Field1 = 968, Field2 = exp.ToString() },
            CancellationToken.None);

        if (gained <= 0) return 0;
        // RefreshEQsAttr natif (GainExp @0x0841d2xx — appelé AVANT
        // ClientLevelUp : UpdateAttr 0x11/0x12 puis 296/492/493) : le
        // palier franchi ré-indexe S_Level → les stats de base et les
        // totaux changent. Les vitals retournées incluent le pipeline
        // complet (équipement, buffs, licences) — le 284 porte les
        // totaux réels comme le GetMaxHP/GetMaxMP natif du corps.
        // Le courant n'est pas rempli (natif).
        var (upHp, upMp) = await ProfileStatEmitter.SendForCharacterAsync(
            session, _services, (int)st.CharId, CancellationToken.None);
        if (upHp > 0 && upMp > 0)
        {
            st.PlayerMaxHp = upHp;
            st.PlayerMaxMp = upMp;
        }
        var lvUp0 = new CNC_CZ_ClientLevelUp
        {
            Field1 = st.CharId, Field2 = 0,
            Field3 = (short)st.Level,
            Field4 = st.PlayerMaxHp, Field5 = st.PlayerMaxMp,
        };
        await session.SendAsync(lvUp0, CancellationToken.None);
        // GainExp natif @0x841cb80 : NotifyClient PUIS
        // BroadCastToCanSee — les témoins reçoivent aussi le 284.
        await BroadcastToNodeAsync(session, st, lvUp0,
            CancellationToken.None);
        // BUG corrigé : le max HP était passé comme PlayerMaxMp — le
        // client affichait un max HP = MP et le ratio vital était
        // corrompu après chaque level-up.
        await SendVitalHpAsync(session, st.CharId, st.PlayerHp, st.PlayerMaxHp, CancellationToken.None);
        await SendVitalMpAsync(session, st.CharId, st.PlayerMp, st.PlayerMaxMp, CancellationToken.None);
        // Skills généraux (couleurs) — le level-up perso octroie les
        // spellmasters multi-masque des classes possédées (natif :
        // GetCommonSpell gaté par char+0x470 = niveau perso).
        await GrantGeneralSpellsOnLevelAsync(session, st,
            CancellationToken.None);
        // AddClassFromLimit natif (GainExp) — KB-03.
        await UnlockClassesFromLimitAsync(session, st,
            CancellationToken.None);
        return gained;
    }
}
