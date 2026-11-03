using Eden.Core.Networking;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Cycle de vie des familiers de combat (elf pets / partners) —
/// partial de ZoneEntryFlow, miroir de <c>CKFElfManager</c> natif
/// (zone/decompiled.c @0x8072xxx-0x8075xxx) :
///
/// - <b>AddElf</b> : l'item "œuf" kind 60 (S_Item col8 = 60) utilisé
///   crée un CKFElf → INSERT elf + 413 ClientElfAdd. Le tableau
///   natif char+0x8ac a 10 slots ; le loc = premier slot libre.
///   Les stats de croissance viennent de S_Partner (fixe ou
///   RandomGrowInfo selon growType) et le HP de partner+0x60.
/// - <b>CallElf</b> (109 `[s16 loc][s16][s16][u8]`) : cooldown 60 s
///   (CKFElf+0xa0), qualité 7-9 = loterie interdite, renvoi du pet
///   courant puis <c>CNode::BornElfPet</c> — modèle "m%06d" du
///   partner, stats indexées par grow dans S_PartnerGrow[level].
///   Statut → 2, émission du 513 [u8=1][s16 loc] et du 418
///   [loc][2][hp][""] au propriétaire.
/// - <b>CallElfBack / ReturnElf</b> (108 `[s16 loc][u8][s16][s16]`) :
///   statut → 0, désinvoque le pet, émission du 513 [u8=0][s16 loc].
/// - <b>PetModeChange</b> (187 `[s16 mode]`) : mode du CElfPet
///   (pet+0x3ec) — 2 = passif (ne combat plus, suit seulement) ;
///   les autres valeurs = assiste le maître.
/// - <b>SendPetCmd</b> (188 `[s16 cmd][i32 cible][s16][i32]`) :
///   CElfPet::DoAction — action d'interaction, relayée au node en
///   470 ClientPlayElfPetAction `[i32 handle][s16 cmd]`.
/// - <b>110 EnterNodeElfInfo</b> → 412 complet (43 champs/record).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Slot d'elf du tableau natif char+0x8ac (10 slots) —
    /// fusion de la ligne DB `elf` + CPartnerData résolu.</summary>
    public sealed class ElfSlot
    {
        /// <summary>Clé primaire de la table elf (CKFElf+4).</summary>
        public int ElfId;
        /// <summary>Id de l'item œuf = clé S_Partner (CKFElf+0x10).</summary>
        public int TemplateId;
        /// <summary>Index de slot 0-9 (CKFElf+0x3c).</summary>
        public short Loc;
        /// <summary>0 = inactif, 2 = invoqué (CKFElf+0x14).</summary>
        public short Status;
        /// <summary>Surnom donné par le joueur (CKFElf+0x28).</summary>
        public string GivenName = "";
        public int Level = 1;
        public int Exp;
        /// <summary>HP courant (CKFElf+0x1c).</summary>
        public int Hp;
        /// <summary>HP max (CKFElf+0x20 = partner+0x60).</summary>
        public int MaxHp;
        /// <summary>Type de croissance de l'œuf (CKFElf+0x24 =
        /// CPartnerData+0x14 = S_Partner col5 = GrowType — détermine
        /// les points de croissance gagnés à la fusion, natif
        /// switch @0x8079c88 : 0→1, 2→2, 3→4, 4→3, 5→5).</summary>
        public short Quality;
        /// <summary>Index de croissance attack/hit/crit/atkspd
        /// (CKFElf+0x2c, 0-9 → S_PartnerGrow).</summary>
        public short[] Grow = new short[4];
        public short MixStar;
        /// <summary>Sorts de bataille (CKFElf+0x40, 5 slots).</summary>
        public int[] Battle = new int[5];
        /// <summary>Sorts intégrés (CKFElf+0x4c, 5 slots).</summary>
        public int[] Integrate = new int[5];
        /// <summary>Dernier renvoi unix — cooldown d'appel 60 s
        /// (CKFElf+0xa0, -1 = pas de cooldown).</summary>
        public int LastCallUnix = -1;
        /// <summary>CPartnerData résolue (S_Partner[TemplateId]).</summary>
        public PartnerParser.PartnerDef? Def;
    }

    /// <summary>Charge les elfs du perso depuis la table native
    /// `elf` — l'équivalent du SELECT elf au login (CExecutionerDBO
    /// natif remplit char+0x8ac avant l'entrée), puis applique la
    /// réconciliation <c>CKFElfManager::CallElfBackVer2</c> natif
    /// @0x80731c0 (appelée par <c>CCharacter::OnMigrationDone</c>) :
    /// <list type="bullet">
    /// <item>elf en mix (status 1) sur une map sans le flag prière
    /// (template+0x10 bit 0x40 — <see cref="PetMapFlags"/>) →
    /// SetElfStatus(0) + RemoveMixElf (purge des lignes
    /// player_enchants intégrées).</item>
    /// <item>elf invoqué (status 2) sur une map sans le flag combat
    /// (bit 0x20) → <c>CallElf(false,false)</c> : le pet renaît
    /// automatiquement comme compagnon (BornElfPet).</item>
    /// <item>elf status 2 sur une map 0x20 → le statut est conservé,
    /// le client invoque à la demande.</item>
    /// </list></summary>
    private async Task EnsureElfsLoadedAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.ElfsLoaded || st.CharId == 0 || _services.ElfRepo == null)
            return;
        st.ElfsLoaded = true;
        var petFlags = PetMapFlags(st);
        ElfSlot? autoSummon = null;
        // Lignes `elf` préchargées par le pipeline d'entrée (Phase 2)
        // — le snapshot de l'install est ce que le SELECT de
        // CExecutionerDBO aurait lu au login : la session n'écrit des
        // elfs qu'APRÈS cette porte (toutes les mutations passent par
        // EnsureElfsLoadedAsync). Fallback live si la session n'a
        // pas de bundle (miroir des fallbacks 310/311).
        var elfRows = st.Entry?.ElfRows
            ?? await _services.ElfRepo.GetElfsAsync((int)st.CharId);
        foreach (var row in elfRows)
        {
            var slot = new ElfSlot
            {
                ElfId = row.ElfId,
                TemplateId = row.TemplateId,
                Loc = row.Loc,
                Status = row.Status,
                GivenName = row.GivenName ?? "",
                Level = row.Level,
                Exp = row.Exp,
                Hp = row.Hp,
                Quality = 0,
                MixStar = row.MixStar,
                Grow =
                {
                    [0] = row.GrowAttack, [1] = row.GrowHit,
                    [2] = row.GrowCritical, [3] = row.GrowAttackSpeed
                },
            };
            for (var i = 0; i < 5; i++)
            {
                slot.Battle[i] = row.BattleSpells[i];
                slot.Integrate[i] = row.IntegrateSpells[i];
            }
            slot.Def = _services.Partners.GetValueOrDefault(
                row.TemplateId);
            slot.MaxHp = slot.Def?.MaxHp ?? Math.Max(1, row.Hp);
            // CKFElf+0x24 natif = CPartnerData+0x14 = col5 (GrowType).
            slot.Quality = (short)(slot.Def?.GrowType ?? 0);
            st.ElfSlots[slot.Loc] = slot;

            // CallElfBackVer2 : (flag 0x20 || status != 2) → branche
            // « rien/démixage » ; sinon → CallElf auto.
            if ((petFlags & 0x20) != 0 || slot.Status != 2)
            {
                if ((petFlags & 0x40) == 0 && slot.Status == 1)
                {
                    // Map sans prière : sort du slot mix, purge les
                    // enchants intégrés persistés.
                    slot.Status = 0;
                    var eid = slot.ElfId;
                    var ids = slot.Integrate.Where(i => i != 0)
                        .ToList();
                    await _persistence.EnqueueAsync((int)st.CharId,
                        _ => _services.ElfRepo!
                            .UpdateStatusAsync(eid, 0));
                    foreach (var id in ids)
                        await _services.PlayerEnchants
                            .DeleteEnchantAsync((int)st.CharId, id);
                }
                else if (slot.Status == 1)
                {
                    // Map avec prière : l'elf reste en mix —
                    // reconstitue le suivi runtime (enchants déjà
                    // persistés en player_enchants durée -2).
                    st.MixElfLoc = slot.Loc;
                    st.MixElfTemplateId = slot.TemplateId;
                    st.MixSpellEnchants.AddRange(
                        slot.Integrate.Where(i => i != 0));
                }
            }
            else
            {
                autoSummon = slot;
            }
        }
        // CallElf(false,false) natif : le pet status 2 renaît sur les
        // maps où l'invocation manuelle est interdite (compagnon).
        if (autoSummon != null)
            await CallElfCoreAsync(session, st, autoSummon, token);
    }

    /// <summary>110 EnterNodeElfInfo → 412 complet — le client envoie
    /// la requête au login pour peupler son panneau familiers
    /// (CNE Execute natif @0x824dxxx construit le paquet depuis le
    /// tableau char+0x8ac).</summary>
    private async Task HandleEnterNodeElfInfoAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        var recs = st.ElfSlots.Values.OrderBy(e => e.Loc)
            .Select(e => ToInfoRecord(e, (int)st.CharId)).ToList();
        await session.SendPacketAsync(412,
            NativeElfCodec.BuildEnterNodeElfInfo(recs), token);
    }

    /// <summary>109 ElfCallElf `[s16 loc][s16][s16][u8]` →
    /// CKFElfManager::CallElf natif : cooldown 60 s, renvoi de
    /// l'ancien pet, BornElfPet (modèle m%06d + stats grow),
    /// status=2, 513 + 418 au propriétaire.</summary>
    private async Task HandleElfCallElfAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        if (payload.Length < 2) return;
        var loc = BitConverter.ToInt16(payload, 0);   // s16 : slot elf
        if (!st.ElfSlots.TryGetValue(loc, out var elf) || elf.Def == null)
            return;
        // CallElf natif : elfs en loterie (CKFElf+0x58 status 7-9)
        // refusés — `2 < status-7` non signé.
        if ((uint)(elf.Status - 7) <= 2) return;
        // Cooldown d'appel 60 s temps réel (CKFElf+0xa0, param_3
        // =true — le chemin Ver2 le saute).
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (elf.LastCallUnix >= 0 && now - elf.LastCallUnix < 60)
            return;
        elf.LastCallUnix = -1;
        await CallElfCoreAsync(session, st, elf, token);
    }

    /// <summary>Corps de <c>CallElf</c> natif @0x8072c30 : renvoi du
    /// pet live éventuel, sortie du slot mix, <c>CNode::BornElfPet</c>
    /// (spawn du monstre modèle + stats grow), status=2 persisté,
    /// 513 + 418 au propriétaire, passifs famille 9 au maître.</summary>
    private async Task CallElfCoreAsync(NetSession session,
        EntryState st, ElfSlot elf, CancellationToken token)
    {
        var loc = elf.Loc;
        // Garde anti-doublon native (CallElf @0x08072c30) : un pet
        // déjà invoqué (char+0x7dc occupé, IsA==0x50b) → CallElfBack
        // puis RETURN — cliquer le raccourci = bascule off ; changer
        // de pet exige un rappel explicite. Sans ce `return` le
        // serveur respawnait par-dessus : doublons visuels live.
        if (st.ElfPetHandle != 0)
        {
            await CallElfBackAsync(session, st, notify: true, token);
            return;
        }
        // Renvoi du pet déjà invoqué (CallElfBack natif avant
        // BornElfPet quand char+0x7dc est occupé) — garde résiduelle
        // pour les chemins d'auto-respawn (handle déjà nul).
        await CallElfBackAsync(session, st, notify: false, token);
        // CallElf natif : un elf sorti du slot mix est renvoyé
        // (RemoveMixElf + notif 567) puis invoqué.
        if (elf.Status == 1)
            await RemoveMixElfAsync(session, st, notify: true, token);

        // BornElfPet : le monstre sous-jacent est résolu par le code
        // modèle du partner (le natif utilise un template fixe -2537
        // + modèle "m%06d" ; on prend le S_Monster de même modèle —
        // en préférant celui dont le nom coïncide avec le partner).
        var monsterId = elf.Def == null ? -1
            : ResolveElfMonsterId(elf.Def);
        if (monsterId <= 0)
        {
            EdenLog.Write($"[Elf] char={st.CharId} call loc={loc} " +
                $"tpl={elf.TemplateId} : aucun S_Monster pour " +
                $"{elf.Def?.ModelCode}");
            return;
        }
        // Position de spawn native `CNode::BornElfPet` @0x084b5d30 :
        // anneau aléatoire autour du maître (offset ±_DAT_087b2754=1.0
        // sur chaque axe, 11 essais), repli = position exacte du
        // maître si aucune case ne convient.
        float spawnX = st.PosX, spawnY = st.PosZ;
        for (var i = 0; i < 11; i++)
        {
            var ox = st.PosX + (float)(Random.Shared.NextDouble() * 2 - 1);
            var oy = st.PosZ + (float)(Random.Shared.NextDouble() * 2 - 1);
            if (ox == st.PosX && oy == st.PosZ) continue;
            spawnX = ox; spawnY = oy; break;
        }
        var ex = _services.Live.SpawnPet(st.NodeId, st.InstanceGroup, monsterId,
            (int)st.CharId, spawnX, spawnY,
            _services.Live.GroundHeightAt(
                _services.Scenes.GetScene($"S{st.NodeId:D3}"),
                spawnX, spawnY),
            st.Facing, false, 0);
        if (_services.Live.GetMob(ex.Handle) is { } pet)
        {
            pet.ElfLoc = loc;
            pet.ElfId = elf.ElfId;
            // Instruction initiale native pet+0x3ec = 2 (suivi passif)
            // — le mode assiste n'est actif que sur PetModeChange.
            pet.ElfMode = 2;
            pet.PetName = ElfDisplayName(elf);
            // Stats natives : attack/hit/crit/atkspd =
            // S_PartnerGrow[level].Stat(i, grow[i]).
            var grow = _services.PartnerGrows
                .GetValueOrDefault(Math.Max(1, elf.Level));
            // + stats du conteneur d'équipement (RefreshEQsAttr natif).
            var gear = await PetGearTotalsAsync(_services, (int)st.CharId,
                loc, elf.Level);
            if (grow != null)
            {
                pet.AttackPower = PetRules.AttackPower(
                    grow.Stat(0, elf.Grow[0]), gear.Atk);
            }
            pet.Level = elf.Level;
            pet.MaxHp = elf.Hp > 0 ? elf.Hp : elf.MaxHp;
            pet.Hp = pet.MaxHp;
            pet.RespawnSeconds = 0;
            st.ElfPetHandle = ex.Handle;
        }

        elf.Status = 2;
        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId;
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.UpdateStatusAsync(eid, 2));
        }
        // 513 ClientPetShortCut [u8=1][s16 loc] puis 418
        // ClientElfUpdateAttr [loc][2][hp][""] — NotifyClient natif
        // (ordre observé dans CallElf @0x8073xxx).
        await session.SendAsync(new CNC_CZ_ClientPetShortCut
        { Field1 = 1, Field2 = loc }, token);
        await session.SendAsync(new CNC_CZ_ClientElfUpdateAttr
        { Field1 = loc, Field2 = 2,
          Field3 = elf.Hp > 0 ? elf.Hp : elf.MaxHp }, token);
        // InvokePassivities natif : battle_spell_4/5 famille 9 →
        // enchants passifs permanents du maître (vecteur +0x8d0).
        await ApplyPetPassivesAsync(session, st, elf, token);
        EdenLog.Write($"[Elf] char={st.CharId} call loc={loc} " +
            $"elf={elf.ElfId} tpl={elf.TemplateId} mob={monsterId} " +
            $"handle={ex.Handle:X8}");
    }

    /// <summary>Totaux de stats du conteneur d'équipement du pet de
    /// loc <paramref name="loc"/> (4 slots kind 81).</summary>
    internal static async Task<(int Atk, int Hit, int Crit, int Extra)>
        PetGearTotalsAsync(ZoneServices s, int charId, short loc,
        int petLevel)
    {
        var items = await s.InventoryRepo.GetElfContainerItemsAsync(charId);
        return PetRules.GearTotals(items.Where(i => i.BagId == loc)
            .Select(i => s.GameData.Items.FirstOrDefault(
                x => x.Id == i.ItemId)), petLevel);
    }

    /// <summary>108 ElfReturn `[s16 loc][u8 toEgg][s16 c][s16 s]` →
    /// Execute natif @0x8249df0 : toEgg==0 → <c>ReturnElf</c>
    /// (libération définitive du familier) ; toEgg≠0 →
    /// <c>ReturnElfToEgg</c> (scellement — c/s = item sceau à
    /// consommer, -1/-1 = via l'interface pour 10 000 or).</summary>
    private async Task HandleElfReturnAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        if (payload.Length < 7) return;
        var loc = BitConverter.ToInt16(payload, 0);   // s16 — slot elf
        var toEgg = payload[2];                        // u8 — flag seal
        var sealBag = BitConverter.ToInt16(payload, 3); // s16 — conteneur sceau (-1 = or)
        var sealSlot = BitConverter.ToInt16(payload, 5);// s16 — slot sceau (-1 = or)
        if (toEgg == 0)
            await ReturnElfCoreAsync(session, st, loc, notify: true,
                token);
        else
            await ReturnElfToEggAsync(session, st, loc, sealBag,
                sealSlot, token);
    }

    /// <summary><c>CKFElfManager::ReturnElf</c> natif @0x8079540 :
    /// libération du familier — refus si l'elf est en transaction
    /// (mix/statut ≠ 0 natif +0x58) ou porte des items
    /// (elf_container), despawn du pet live, DELETE elf +
    /// elf_container, 414 ClientElfReturnOK `[loc][1]`.</summary>
    private async Task ReturnElfCoreAsync(NetSession session,
        EntryState st, short loc, bool notify,
        CancellationToken token)
    {
        if (!st.ElfSlots.TryGetValue(loc, out var elf)) return;
        // Refus natif : elf en transaction (+0x58≠0 — mix/loterie)
        // ou elf_container non vide → textes 0x1523/0x1470.
        if (elf.Status == 1)
        {
            await session.SendAsync(new Eden.Core.Networking.Messages
                .Generated.CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1523, Field2 = ElfDisplayName(elf) }, token);
            return;
        }
        if (_services.InventoryRepo != null)
        {
            var elfItems = await _services.InventoryRepo
                .GetElfContainerItemsAsync((int)st.CharId);
            if (elfItems.Any(i => i.BagId == loc))
            {
                await session.SendAsync(new Eden.Core.Networking
                    .Messages.Generated.CNC_CZ_ClientTextOutputIndex
                { Field1 = 0x1470, Field2 = "" }, token);
                return;
            }
        }
        // Despawn du pet live si c'est cet elf (CallElfBack natif).
        if (st.ElfPetHandle != 0 &&
            _services.Live.GetMob(st.ElfPetHandle) is { } pet &&
            pet.ElfLoc == loc)
        {
            await RemovePetPassivesAsync(session, st, token);
            await DespawnMobAtAsync(session, st, pet.Handle, token);
            st.ElfPetHandle = 0;
        }
        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId;
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.InventoryRepo!
                    .DeleteElfContainerAsync((int)st.CharId, loc));
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.DeleteElfAsync(eid));
        }
        st.ElfSlots.Remove(loc);
        if (notify)
            // Texte 0x1471 « %s » — libération confirmée (ReturnElf
            // natif, chaîne = nom du pet).
            await session.SendAsync(new Eden.Core.Networking.Messages
                .Generated.CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x1471, Field2 = ElfDisplayName(elf) }, token);
        await session.SendAsync(new CNC_CZ_ClientElfReturnOK
        { Field1 = loc, Field2 = 1 }, token);
        EdenLog.Write($"[Elf] char={st.CharId} release loc={loc} " +
            $"elf={elf.ElfId} tpl={elf.TemplateId}");
    }

    /// <summary><c>CKFElfManager::ReturnElfToEgg</c> natif
    /// @0x807ab10 : recrée l'item œuf du familier avec toutes ses
    /// données puis le supprime de la liste :
    /// <list type="bullet">
    /// <item>chemin (bag,slot)=(-1,-1) : coût 10 000 or, œuf
    ///   <b>lié</b> (+0x24|=0x10), nom (+0x20→maker) et hp
    ///   (+0x40[4]→embedded_id5) conservés ;</item>
    /// <item>chemin sceau : consomme l'item à (bag,slot) ×1,
    ///   œuf non lié (revente possible) ;</item>
    /// <item>dans les deux cas : level→train_level, exp→train_exp,
    ///   grow→embedded_id1-4, battle→extra_combo1-5,
    ///   integrate→extra_combo6-9 + embedded_id6,
    ///   mix_star→embedded_amount.</item>
    /// </list>
    /// Ensuite ReturnElf(loc, false) → 414 + DELETE elf.</summary>
    private async Task ReturnElfToEggAsync(NetSession session,
        EntryState st, short loc, short sealBag, short sealSlot,
        CancellationToken token)
    {
        if (!st.ElfSlots.TryGetValue(loc, out var elf)) return;
        if (elf.Status == 1 || _services.ElfRepo == null) return;
        var goldPath = sealBag == -1 && sealSlot == -1;
        // Slot libre pour l'œuf (GetContainerWithEmptySlot natif).
        var dest = await _services.InventoryRepo
            .GetAnyAvailableSlotAsync((int)st.CharId);
        if (dest == null) return;   // inventaire plein — refus silencieux natif

        if (goldPath)
        {
            // GainGold(-10000) natif — refus si pas assez d'or.
            if (st.Gold < 10000) return;
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, -10000);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
                token);
        }
        else if (!await ConsumeItemAtAsync(session, st, sealBag,
                     sealSlot, 1, token))
            return;                 // pas de sceau au slot demandé

        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var egg = new Eden.Core.Game.Inventory.ItemEntity
        {
            CharacterId = (int)st.CharId,
            BagId = dest.Value.BagId,
            Slot = dest.Value.Slot,
            ItemId = elf.TemplateId,
            Quantity = 1,
            Durability = 1,               // clone+0x10 = 1
            MaxDurability = 1,
            IsBound = goldPath,           // +0x24|=0x10 (chemin or)
            CreateTime = now,
            // Données du familier sur les champs génériques :
            // +0x58 level / +0x5c exp / +0x4c mix_star.
            TrainLevel = elf.Level,
            TrainExp = elf.Exp,
            EmbeddedAmount = elf.MixStar,
            Maker = goldPath ? elf.GivenName : string.Empty,
        };
        // +0x34[0..8] : battle 1-5 puis integrate 1-4.
        for (var i = 0; i < 5 && i < egg.ExtraCombos.Length; i++)
            egg.ExtraCombos[i] = elf.Battle[i];
        for (var i = 0; i < 4; i++)
            egg.ExtraCombos[5 + i] = elf.Integrate[i];
        // +0x40[0..5] : grow 4× puis hp (chemin or) puis integrate 5.
        for (var i = 0; i < 4; i++)
            egg.SetEmbeddedId(i, elf.Grow[i]);
        egg.SetEmbeddedId(4, goldPath ? elf.Hp : 0);
        egg.SetEmbeddedId(5, elf.Integrate[4]);

        await _services.InventoryRepo.AddItemAsync(egg);
        // 274 ClientPutItemIntoInventory — record complet natif.
        await session.SendPacketAsync(274,
            Codecs.NativeItemCodec.BuildPutItem(egg), token);
        // ReturnElf(loc, false) natif — suppression de l'elf.
        await ReturnElfCoreAsync(session, st, loc, notify: false,
            token);
        EdenLog.Write($"[Elf] char={st.CharId} toEgg loc={loc} " +
            $"elf={elf.ElfId} → œuf @{dest.Value.BagId}:" +
            $"{dest.Value.Slot} bound={goldPath}");
    }

    /// <summary>Nom affiché du familier — surnom si posé, sinon le
    /// nom du partner (chaîne des textes de notif ReturnElf).</summary>
    private static string ElfDisplayName(ElfSlot elf) =>
        string.IsNullOrEmpty(elf.GivenName)
            ? elf.Def?.Name ?? string.Empty : elf.GivenName;

    /// <summary>Renvoi du pet invoqué — CallElfBack natif : statut
    /// elf → 0 (persisté), hp courant sauvegardé, despawn live avec
    /// 269 aux témoins, 513 [u8=0][loc] au propriétaire si
    /// <paramref name="notify"/>.</summary>
    private async Task CallElfBackAsync(NetSession session,
        EntryState st, bool notify, CancellationToken token)
    {
        var loc = (short)-1;
        if (st.ElfPetHandle != 0 &&
            _services.Live.GetMob(st.ElfPetHandle) is { } pet)
        {
            loc = pet.ElfLoc;
            if (st.ElfSlots.TryGetValue(loc, out var elf))
            {
                // HP courant persisté au rappel (Update Elf natif).
                elf.Hp = pet.Hp;
                elf.Status = 0;
                if (_services.ElfRepo != null)
                {
                    var eid = elf.ElfId; var hp = elf.Hp;
                    // HP avant statut — l'ordre natif, désormais
                    // garanti par la file (fire-and-forget pouvait
                    // les exécuter dans le désordre).
                    await _persistence.EnqueueAsync((int)st.CharId,
                        _ => _services.ElfRepo!.UpdateHpAsync(eid, hp));
                    await _persistence.EnqueueAsync((int)st.CharId,
                        _ => _services.ElfRepo!.UpdateStatusAsync(eid, 0));
                }
            }
            pet.ElfLoc = -1;
            pet.ElfId = 0;
            // Dernier appel = début du cooldown 60 s (CKFElf+0xa0).
            if (st.ElfSlots.TryGetValue(loc, out var e2))
                e2.LastCallUnix =
                    (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await DespawnMobAtAsync(session, st, pet.Handle, token);
        }
        else
        {
            // Pas de pet live — retrouve le slot invoqué (status 2)
            // pour le repasser inactif.
            var active = st.ElfSlots.Values
                .FirstOrDefault(e => e.Status == 2);
            if (active != null)
            {
                loc = active.Loc;
                active.Status = 0;
                if (_services.ElfRepo != null)
                {
                    var elfId = active.ElfId;
                    await _persistence.EnqueueAsync((int)st.CharId,
                        _ => _services.ElfRepo.UpdateStatusAsync(elfId, 0));
                }
            }
        }
        st.ElfPetHandle = 0;
        // CallElfBack natif : purge le vecteur char+0x8d0 — enchants
        // passifs accordés au maître par le pet (RemoveEnchantByID).
        await RemovePetPassivesAsync(session, st, token);
        if (notify && loc >= 0)
            await session.SendAsync(new CNC_CZ_ClientPetShortCut
            { Field1 = 0, Field2 = loc }, token);
    }

    /// <summary>EXP du familier invoqué au kill —
    /// <c>CKFElf::GainExp</c> natif @0x8456ed0, appelé par
    /// <c>GiveTeamExp</c> avec la part complète du perso (même
    /// montant que le charExp) :
    /// <list type="bullet">
    ///   <item>seuil = <c>petExpVec[level]</c> où
    ///         <c>LoadPetExp</c> écrit <c>vec[key-1]</c> = S_Level
    ///         col24 → seuil = <see cref="LevelRow.PetExp"/> de la
    ///         ligne level+1 ;</item>
    ///   <item>palier franchi tant que exp ≥ seuil ET niveau elf &lt;
    ///         niveau perso (cap char+0x470) → <c>SetLevel</c> +
    ///         <c>CElfPet::NotifyRefreshAttr</c> : 418 types 8-11
    ///         (attack/hit/crit/atkspd du nouveau CPartnerGrowData) ;
    ///   </item>
    ///   <item>au cap : banque = seuil, crédité = seuil − exp
    ///         précédente (param_1 ajusté natif) + texte 5462 ;</item>
    ///   <item>418 <c>[loc][type=1][exp][""]</c> à chaque crédit +
    ///         texte 5458 <c>"%s,%d"</c> (nom, exp créditée) ;</item>
    ///   <item>persisté via <c>UpdateLevelExpAsync</c> (DBA
    ///         "Update Elf exp" natif).</item>
    /// </list></summary>
    private async Task GrantElfExpAsync(NetSession session,
        EntryState st, long charExp, CancellationToken token)
    {
        // Natif : seul l'elf INVOQUÉ (CKFElf status 2, vecteur
        // +0x8ac via pet+0x40c) reçoit l'exp ; level >= taille du
        // vecteur pet-exp ⇒ rien n'est crédité.
        var elf = st.ElfSlots.Values
            .FirstOrDefault(e => e.Status == 2);
        if (elf == null || elf.Def == null || charExp <= 0) return;
        // Off-by-one natif : LoadPetExp écrit vec[key-1] et GainExp
        // lit vec[level] → seuil = col24 de la ligne (level+1).
        var lvlRow = _services.GameData.Levels
            .FirstOrDefault(l => l.Level == elf.Level + 1);
        if (lvlRow == null) return;

        var prevExp = elf.Exp;
        var threshold = lvlRow.PetExp;
        elf.Exp += (int)Math.Min(int.MaxValue, charExp);
        var credited = elf.Exp - prevExp;
        var capped = false;
        if (elf.Exp >= threshold)
        {
            if (elf.Level < st.Level)
            {
                do
                {
                    elf.Level++;
                    // NotifyRefreshAttr natif de SetLevel : 418
                    // types 8/9/10/11 = attack/hit/crit/atkspd du
                    // CPartnerGrowData du nouveau niveau.
                    var grow = _services.PartnerGrows
                        .GetValueOrDefault(elf.Level);
                    if (grow != null)
                    {
                        await session.SendAsync(
                            new CNC_CZ_ClientElfUpdateAttr
                            {
                                Field1 = elf.Loc, Field2 = 8,
                                Field3 = grow.Stat(0, elf.Grow[0]),
                            }, token);
                        await session.SendAsync(
                            new CNC_CZ_ClientElfUpdateAttr
                            {
                                Field1 = elf.Loc, Field2 = 9,
                                Field3 = grow.Stat(1, elf.Grow[1]),
                            }, token);
                        await session.SendPacketAsync(418,
                            NativeElfCodec.BuildElfUpdateAttr(
                                elf.Loc, 10,
                                grow.Stat(2, elf.Grow[2])), token);
                        await session.SendPacketAsync(418,
                            NativeElfCodec.BuildElfUpdateAttr(
                                elf.Loc, 11,
                                grow.Stat(3, elf.Grow[3])), token);
                        // Le mob live hérite des nouvelles stats
                        // (CPartnerGrowData rechargé par SetLevel).
                        if (st.ElfPetHandle != 0 &&
                            _services.Live.GetMob(st.ElfPetHandle)
                                is { } pet)
                        {
                            pet.AttackPower = Math.Max(1,
                                grow.Stat(0, elf.Grow[0]));
                            pet.Level = elf.Level;
                        }
                    }
                    elf.Exp -= threshold;
                    var next = _services.GameData.Levels
                        .FirstOrDefault(l => l.Level == elf.Level + 1);
                    if (next == null) break;
                    threshold = next.PetExp;
                    if (elf.Exp < threshold) break;
                } while (elf.Level < st.Level);
            }
            if (elf.Level >= st.Level)
            {
                // Cap natif : banque = seuil, crédité = seuil −
                // ancienne banque (param_1 = local_30 − iVar2).
                elf.Exp = threshold;
                credited = Math.Max(0, threshold - prevExp);
                capped = true;
            }
        }
        if (elf.Exp < 0) elf.Exp = 0;

        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId; var lvl = elf.Level; var exp = elf.Exp;
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.UpdateLevelExpAsync(eid, lvl, exp));
        }
        // 418 [loc][type=1][exp][""] — ClientElfUpdateAttr natif
        // émis à chaque crédit (update du panneau familiers).
        await session.SendPacketAsync(418,
            NativeElfCodec.BuildElfUpdateAttr(elf.Loc, 1, elf.Exp),
            token);
        // Texte 5458 « %s1 has acquired %s2 EXP. » — args "nom,exp"
        // (nom = surnom si posé, sinon le nom du partner natif).
        var name = string.IsNullOrEmpty(elf.GivenName)
            ? elf.Def.Name : elf.GivenName;
        await session.SendAsync(
            new Eden.Core.Networking.Messages.Generated
                .CNC_CZ_ClientTextOutputIndex
                { Field1 = 5458,
                  Field2 = $"{name},{credited}" },
            token);
        if (capped)
            // 5462 « The item has reached its limit and cannot be
            // upgraded further. » — émis natif quand level >= niveau
            // perso ET banque == seuil (pas de placeholder).
            await session.SendAsync(
                new Eden.Core.Networking.Messages.Generated
                    .CNC_CZ_ClientTextOutputIndex
                    { Field1 = 5462, Field2 = "" },
                token);
    }

    /// <summary>107 ElfWork `[s16 loc][s16 mode]` →
    /// CKFElfManager::ToWork natif (Execute @0x824b0b0) : mode 2 =
    /// CallElf sur le slot demandé, mode 0 = renvoi (ToIdle). Les
    /// modes mix/loterie (1/7/8/9) ne sont pas interceptés ici —
    /// l'infrastructure CKFElfWork native n'est pas encore portée.</summary>
    private async Task HandleElfWorkAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4) return;
        var mode = BitConverter.ToInt16(payload, 2);    // s16 : mode
        if (mode == 2)
        {
            // ToWork(...,2) = CallElf — même chemin que le 109
            // (loc = field1, pas besoin de re-lire).
            await HandleElfCallElfAsync(session, st, payload, token);
        }
        else if (mode == 0)
        {
            // ToIdle natif @0x80732c0 : status 1 (mix) → status 0 +
            // RemoveMixElf(notify) ; status 2 → CallElfBack.
            var loc = BitConverter.ToInt16(payload, 0);
            if (st.ElfSlots.TryGetValue(loc, out var mixElf) &&
                mixElf.Status == 1)
            {
                await SetElfStatusAsync(st, mixElf, 0);
                await RemoveMixElfAsync(session, st, notify: true,
                    token);
            }
            else
            {
                await CallElfBackAsync(session, st, notify: true,
                    token);
            }
        }
    }

    /// <summary>228 ElfChangeName `[s16 loc][string nom]` → Execute
    /// natif @0x8248xxx : requiert le parchemin de renommage
    /// (CheckItemNum 0x9c98=40056 ou 0x9cdc=40156, 1 consommé),
    /// nom 2-16 octets, puis CKFElf::ChangeName (UPDATE elf
    /// given_name) + 418 `[loc][type=12][i32 0][str nom]`.</summary>
    private async Task HandleElfChangeNameAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        if (payload.Length < 4 || _services.ElfRepo == null) return;
        var loc = BitConverter.ToInt16(payload, 0);      // s16 : loc
        var nameLen = BitConverter.ToUInt16(payload, 2); // u16 : len
        if (payload.Length < 4 + nameLen) return;
        var name = EdenEncoding.Big5.GetString(
            payload, 4, nameLen);
        if (!st.ElfSlots.TryGetValue(loc, out var elf)) return;
        // Parchemin de renommage — CheckItemNum natif (0x9c98 puis
        // 0x9cdc), un exemplaire consommé (DropItemByItemID).
        var scroll = (await _services.InventoryRepo
                .GetLiveItemsAsync((int)st.CharId))
            .FirstOrDefault(i => i.ItemId is 40056 or 40156);
        if (scroll == null) return;
        await _services.InventoryRepo.RemoveLiveItemAsync(
            (int)st.CharId, scroll.Id);
        elf.GivenName = name;
        await _services.ElfRepo.UpdateNameAsync(elf.ElfId, name);
        // 418 ClientElfUpdateAttr [loc][type 12=nom][i32 0][str nom]
        // — corps vérifié dans ElfChangeName::Execute @0x824936a.
        await session.SendPacketAsync(418,
            NativeElfCodec.BuildElfUpdateAttr(loc, 12, 0, name), token);
        EdenLog.Write($"[Elf] char={st.CharId} rename loc={loc} " +
            $"elf={elf.ElfId} -> '{name}'");
    }

    /// <summary>187 PetModeChange `[s16 mode]` → Execute natif
    /// @0x082700f0 : l'elf pet est requis (vtype 0x50b +
    /// `CKFElf::GetHp &gt; 1`, sinon abandon silencieux après log 0xb86),
    /// `char+0x7e8 == 0` (pas de pet actif) force le mode à 2, puis
    /// `pet+0x3ec = mode`. mode==2 → texte 0xb30 « Loot Mode » +
    /// `UnhookAllFightingRelation` (pet+0xd0=0) ; sinon texte 0xb31
    /// « Combat Mode » + `BecomeFightingCandicate(pet, char+0xd0)`.
    /// `CKFElf::SetAttackState(elf,1, mode!=2)` ne fait que basculer
    /// le bit 0x1 de elf+0x3a et redemander une écriture DBA du
    /// status/loc déjà persistés — aucun paquet client dédié.</summary>
    private async Task HandlePetModeChangeAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2) return;
        var mode = BitConverter.ToInt16(payload, 0);   // s16 : mode
        // char+0x7e8 == 0 natif : sans pet actif le mode lu est
        // écrasé à 2 avant toute écriture.
        if (st.ElfPetHandle == 0) mode = 2;
        if (st.ElfPetHandle == 0 ||
            _services.Live.GetMob(st.ElfPetHandle) is not { } pet)
            return;
        // CKFElf::GetHp > 1 natif — un pet mort ne change pas de mode.
        if (pet.Hp <= 1) return;
        pet.ElfMode = mode;
        if (mode == 2)
        {
            // UnhookAllFightingRelation + pet+0xd0=0 : le pet lâche
            // sa cible verrouillée et sort du combat.
            pet.InCombat = false;
            pet.TargetCharId = 0;
            pet.PetLockTarget = 0;
            // Texte natif 0xb30 « You ordered the pet to use Loot
            // Mode. » (canal -0x69, pas de paramètre).
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0xb30, Field2 = "" }, token);
        }
        else
        {
            // Texte natif 0xb31 « Combat Mode » +
            // BecomeFightingCandicate(pet, cible du maître) — notre
            // TickPetsAsync choisit déjà les mobs engagés contre le
            // maître dès que ElfMode != 2.
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0xb31, Field2 = "" }, token);
        }
    }

    /// <summary>188 SendPetCmd `[s16 instr][i32 cible]` → Execute
    /// natif @0x08285e30 → `CElfPet::DoAction @0x08458270` : la cible
    /// est résolue par `GetLifeEntity(node)` puis le pet lui fait face
    /// et son SeeMob est rebroadcasté au CanSee. Dispatch natif :
    /// instr 1 → `SetPetInstruction(100, cible)` (verrouille la cible
    /// pet+0xd0, mode 2→1 + texte 0xb31) ; instrs 6/7/8 = emotes
    /// (CAI::SetEventAction/LifeEntityDo) ; instrs 101-105 =
    /// CastSpell du battle_spell[idx] sur la cible.</summary>
    private async Task HandleSendPetCmdAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6 || st.ElfPetHandle == 0 ||
            _services.Live.GetMob(st.ElfPetHandle) is not { } pet)
            return;
        var instr = BitConverter.ToInt16(payload, 0);     // s16 : instruction
        var targetId = BitConverter.ToInt32(payload, 2);  // i32 : id entité cible
        var target = targetId != 0
            ? _services.Live.GetMob((uint)targetId) : null;
        if (target != null)
        {
            // ChangeFaceDir natif : le pet se tourne vers la cible.
            pet.Facing = NativeDir(target.X - pet.X, target.Y - pet.Y);
        }
        switch (instr)
        {
            case 1:   // SetPetInstruction(100, cible) — ordre d'attaque
                if (target == null || !target.Alive) return;
                if (pet.ElfMode == 2)
                {
                    // Verrou de cible depuis le passif → Combat Mode
                    // + texte 0xb31 (SetPetInstruction natif).
                    pet.ElfMode = 1;
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0xb31, Field2 = "" }, token);
                }
                pet.PetLockTarget = (uint)targetId;
                break;
            case >= 101 and <= 105:
                // instr 101-105 → CastSpell(battle[idx], cible) natif
                // (DoAction @0x8458270 : gate IsMyEnemy vfunc+0x7c,
                // BecomeInterestLifeInMe puis CastSpell sur pet+0x468
                // +idx*8 avec le niveau elf+0x54+idx*4).
                if (target != null && target.Alive &&
                    !target.IsElfPet &&
                    st.ElfSlots.TryGetValue(pet.ElfLoc, out var elf))
                {
                    var spellId = elf.Battle[instr - 101];
                    if (spellId > 0)
                        await PetCastBattleSpellAsync(session, st, pet,
                            target, spellId, token);
                }
                break;
            // instrs 6/7/8 = emotes/do (SetEventAction 0x1fa/0x1f6,
            // LifeEntityDo 0xf natif — le relais 470 suffit, le pet
            // n'a pas de pipeline d'action côté serveur) ; le
            // relâchement de cible passe par le 187 mode 2.
        }
        // 470 ClientPlayElfPetAction `[i32 handle][s16 cmd]` relayé
        // aux témoins (emotes/ordres visibles) + au propriétaire.
        var msg = new CNC_CZ_ClientPlayElfPetAction
        {
            Field1 = (int)st.ElfPetHandle, Field2 = instr,
        };
        var stream = new OutBinStream();
        msg.Serialize(stream);
        var body = stream.ToArray();
        // CanSee sur le handle de l'elfe (entité propre), pas le
        // charId du maître.
        await BroadcastToNodeAsync(session, st, 470, body, token,
            handle: st.ElfPetHandle);
        await session.SendPacketAsync(470, body, token);
    }

    /// <summary>Cast d'un sort de combat par le pet (NCID 188 instr
    /// 101-105) — <c>CElfPet::DoAction @0x8458270</c> : le pet lance
    /// <c>battle_spell[instr-101]</c> sur la cible hostile. Le 289
    /// part avec le pet en lanceur ; la résolution suit la pipeline
    /// native palier 1 (S_SpellReference — les niveaux de sorts
    /// elf+0x54 ne sont pas portés dans notre table elf).</summary>
    private async Task PetCastBattleSpellAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob pet,
        ZoneLiveWorld.LiveMob target, int spellId,
        CancellationToken token)
    {
        var sdef = _services.Spells.GetDefinition(
            unchecked((ushort)spellId));
        if (sdef == null || !pet.Alive || !target.Alive) return;

        // Geste de cast du modèle (495 broadcast CanSee).
        if (sdef.BlastId > 0)
            await BroadcastMonsterActionAsync(pet.Handle, st.NodeId,
                pet.InstanceKey, (short)sdef.BlastId);

        // 279 CastOK — le pet n'a pas de session, le propriétaire
        // reçoit la confirmation (victime = la cible du sort).
        await session.SendAsync(new CNC_CZ_ClientSpellCastOK
        {
            Field1 = (short)spellId, Field2 = 0,
            Field3 = (int)target.Handle, Field4 = 1,
        }, token);

        // 289 BlastToLife pet→mob aux CanSee + propriétaire.
        _blastSerial = (_blastSerial + 11) & 0x7fffffff;
        var blast = new CNC_CZ_ClientSpellBlastToLife
        {
            Field1 = (uint)_blastSerial,
            Field2 = unchecked((short)spellId),
            Field3 = (int)pet.Handle,
            Field4 = pet.X, Field5 = pet.Y, Field6 = pet.Z,
            Field7 = (int)target.Handle,
            Field8 = target.X, Field9 = target.Y, Field10 = target.Z,
            Field11 = sdef.BlastId, Field12 = 0,
        };
        await session.SendAsync(blast, token);
        await BroadcastToNodePeersAsync(session, st, blast);

        // Résolution native : snapshot mob→mob, coef palier 1.
        var sres = NativeCombatFormulas.ResolveSpell(
            MobSnapshot(pet), MobSnapshot(target), sdef,
            SpellRefCoef(sdef, 1), CombatParams(), Random.Shared);
        var dmg = sres.Damage;
        target.Hp = Math.Max(0, target.Hp - dmg);
        // Le sort du pet compte comme action du maître (anti-leech
        // Limitless + fenêtre d'aggro) — natif BecomeInterestLifeInMe.
        target.SkillHitCharIds.Add((int)st.CharId);
        target.LastHitAt = DateTime.UtcNow;
        var first = !target.InCombat ||
            target.TargetCharId != (int)st.CharId;
        BeginMobCombat(st, target);
        await SendAttrAsync(session, target.Handle, 0, target.Hp,
            target.Hp, token);
        await SendSpellHitAsync(session, spellId, target.Handle,
            pet.Handle, dmg,
            NativeCombatFormulas.SpellHitFlagsOf(sres), token);

        if (target.Hp <= 0)
        {
            target.MarkDead();
            target.DeadAt = DateTime.UtcNow;
            await FinishMobDeathAsync(session, st, target, true, token);
        }
        else
        {
            if (sdef.ResultSpellId > 0 &&
                sres.Outcome != PhysicalOutcome.Miss &&
                (sdef.ResultSpellChance <= 0 ||
                    Random.Shared.Next(100) < sdef.ResultSpellChance))
            {
                await ApplySpellResultCommandsAsync(session, st,
                    spellId, sdef, target, token);
            }
            if (first)
                StartMobCombatLoop(session, st, target);
        }
    }

    /// <summary>Use d'un item œuf (S_Item kind 60) →
    /// CKFElfManager::AddElf natif @0x8073bf0 : premier slot libre
    /// (10 max), stats grow de S_Partner (ou RandomGrowInfo selon
    /// growType), INSERT elf + 413 ClientElfAdd.
    /// <para>L'œuf d'un pet re-scellé (ReturnElfToEgg) transporte les
    /// données du familier dans les champs génériques du clone —
    /// AddElf natif les relit quand item+0x58 ≠ 0 (train_level =
    /// niveau stocké) : +0x5c exp (train_exp), +0x20 nom (maker),
    /// +0x40 grow[0..3]/hp (embedded_id1-5), +0x34 battle[0..4] +
    /// integrate[0..3] (extra_combo_id1-9), +0x40[5] integrate[4]
    /// (embedded_id6), +0x4c mix_star (embedded_amount).</para></summary>
    private async Task<bool> AddElfFromItemAsync(NetSession session,
        EntryState st, Eden.Core.Game.Inventory.ItemEntity egg,
        CancellationToken token)
    {
        if (!_services.Partners.TryGetValue(egg.ItemId, out var def) ||
            _services.ElfRepo == null)
            return false;
        await EnsureElfsLoadedAsync(session, st, token);
        if (st.ElfSlots.Count >= 10)
        {
            EdenLog.Write($"[Elf] char={st.CharId} : 10 slots " +
                "pleins — AddElf refusé");
            return false;
        }
        // Premier slot libre (parité natif : slots 0-9).
        var loc = (short)0;
        while (st.ElfSlots.ContainsKey(loc)) loc++;

        // Œuf « chargé » (re-scellé) : clone+0x58 (train_level) ≠ 0
        // → AddElf restaure level/exp/nom/hp/grow/sorts/mix_star de
        // l'item au lieu des valeurs fraîches de S_Partner.
        var sealed_ = egg.TrainLevel > 0;
        var elfId = await _services.ElfRepo.NextElfIdAsync();
        var elf = new ElfSlot
        {
            ElfId = elfId,
            TemplateId = egg.ItemId,
            Loc = loc,
            Status = 0,
            Level = sealed_ ? egg.TrainLevel : 1,
            Exp = sealed_ ? (int)Math.Min(int.MaxValue, egg.TrainExp) : 0,
            GivenName = sealed_ && !string.IsNullOrEmpty(egg.Maker)
                ? egg.Maker : "",
            Hp = sealed_ && egg.EmbeddedId5 > 0
                ? egg.EmbeddedId5 : def.MaxHp,
            MaxHp = def.MaxHp,
            MixStar = sealed_ ? (short)egg.EmbeddedAmount : (short)0,
            Def = def,
        };
        if (sealed_)
        {
            // Données restaurées : grow embedded_id1-4, sorts
            // battle extra_combo1-5, integrate extra_combo6-9 +
            // embedded_id6.
            for (var i = 0; i < 4; i++)
                elf.Grow[i] = (short)egg.GetEmbeddedId(i);
            for (var i = 0; i < 5; i++)
            {
                elf.Battle[i] = egg.ExtraCombos[i];
                elf.Integrate[i] = i < 4 ? egg.ExtraCombos[5 + i]
                    : egg.EmbeddedId6;
            }
        }
        else
        {
            // Stats de croissance : valeurs fixes de S_Partner si non
            // nulles, sinon RandomGrowInfo(growType) natif.
            if (def.Grow.All(g => g == 0))
                RandomGrowInfo(def.GrowType, elf.Grow);
            else
                for (var i = 0; i < 4; i++)
                    elf.Grow[i] = (short)def.Grow[i];
            for (var i = 0; i < 5; i++)
            {
                elf.Battle[i] = def.BattleSpells[i];
                elf.Integrate[i] = def.IntegrateSpells[i];
            }
        }

        await _services.ElfRepo.InsertElfAsync(new ElfRepository.ElfRow
        {
            ElfId = elfId, PlayerId = (int)st.CharId,
            TemplateId = egg.ItemId, Status = 0,
            GivenName = elf.GivenName, Level = elf.Level,
            Exp = elf.Exp,
            Hp = elf.Hp,
            GrowAttack = elf.Grow[0], GrowHit = elf.Grow[1],
            GrowCritical = elf.Grow[2], GrowAttackSpeed = elf.Grow[3],
            Loc = loc, MixStar = elf.MixStar,
            BattleSpell1 = elf.Battle[0], BattleSpell2 = elf.Battle[1],
            BattleSpell3 = elf.Battle[2], BattleSpell4 = elf.Battle[3],
            BattleSpell5 = elf.Battle[4],
            IntegrateSpell1 = elf.Integrate[0],
            IntegrateSpell2 = elf.Integrate[1],
            IntegrateSpell3 = elf.Integrate[2],
            IntegrateSpell4 = elf.Integrate[3],
            IntegrateSpell5 = elf.Integrate[4],
        });
        st.ElfSlots[loc] = elf;
        await session.SendPacketAsync(413,
            NativeElfCodec.BuildElfAdd(
                ToInfoRecord(elf, (int)st.CharId)), token);
        EdenLog.Write($"[Elf] char={st.CharId} AddElf " +
            $"item={egg.ItemId} → elf={elfId} loc={loc} " +
            $"lvl={elf.Level} sealed={sealed_} " +
            $"grow=[{string.Join(',', elf.Grow)}]");
        return true;
    }

    /// <summary>RandomGrowInfo natif (ElfManager.cc @0x8073b00) :
    /// selon growType — 0→1, 2→3, 3→6, 4→5, 5→7 points répartis
    /// aléatoirement sur les 4 stats (cap 9/stat).</summary>
    private static void RandomGrowInfo(int growType, short[] grow)
    {
        var points = growType switch
        {
            0 => 1, 2 => 3, 3 => 6, 4 => 5, 5 => 7, _ => 0,
        };
        for (var p = 0; p < points; p++)
        {
            var i = Random.Shared.Next(4);
            // Si la stat est à 9 on tourne jusqu'à en trouver une
            // libre (le natif boucle en wrap-around).
            var start = i;
            while (grow[i] >= 9)
            {
                i = (i + 1) % 4;
                if (i == start) return;
            }
            grow[i]++;
        }
    }

    /// <summary>Résolution du template monstre du pet — le natif
    /// utilise un template fixe + le modèle "m%06d" du partner ; on
    /// prend le S_Monster de même ModelCode, en préférant celui dont
    /// le nom correspond au partner (ex. 21553 戰鬥靈兔 pour
    /// Combat Bunny).</summary>
    private int ResolveElfMonsterId(PartnerParser.PartnerDef def)
    {
        var code = $"M{def.ModelId:D6}";
        var candidates = _services.Live.MonsterDefs.Values
            .Where(m => string.Equals(m.ModelCode, code,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return 0;
        return candidates
            .FirstOrDefault(m => m.Name == def.Name)?.Id
            ?? candidates[0].Id;
    }

    /// <summary>Re-invocation du pet elfe après une migration de node —
    /// <c>CKFElfManager::CallElfBackVer2</c> natif @0x80731c0
    /// (OnMigrationDone) : DespawnPetsAsync a retiré l'entité live de
    /// l'ancien node ; un elf à status=2 re-born uniquement sur les
    /// maps sans le flag combat (template+0x10 bit 0x20 absent —
    /// le pet suit en compagnon là où l'invocation manuelle est
    /// interdite) ; un elf en mix (status 1) sort du slot si la map
    /// n'a pas le flag prière (bit 0x40).</summary>
    private async Task RespawnElfPetAfterMigrationAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // CallElfBackVer2 natif (OnMigrationDone) : un elf en mix
        // (status 1) sort du slot sur une map sans le flag prière
        // (0x40) ; un elf invoqué (status 2) ne renaît que sur les
        // maps sans le flag combat (0x20) — sur les maps 0x20 le
        // statut est conservé, le client ré-invoque à la demande.
        var petFlags = PetMapFlags(st);
        if ((petFlags & 0x40) == 0 &&
            st.ElfSlots.Values.FirstOrDefault(
                e => e.Status == 1) is { } mixed)
        {
            await SetElfStatusAsync(st, mixed, 0);
            await RemoveMixElfAsync(session, st, notify: true, token);
        }
        if (st.ElfPetHandle == 0) return;
        if ((petFlags & 0x20) != 0)
        {
            // Map « combat » : pas d'auto-respawn natif — le statut
            // 2 est conservé (le client ré-invoque à la demande) et
            // les enchants passifs restent attachés au maître.
            st.ElfPetHandle = 0;
            return;
        }
        var elf = st.ElfSlots.Values.FirstOrDefault(e => e.Status == 2);
        if (elf?.Def == null)
        {
            st.ElfPetHandle = 0;
            return;
        }
        var monsterId = ResolveElfMonsterId(elf.Def);
        if (monsterId <= 0)
        {
            st.ElfPetHandle = 0;
            return;
        }
        var ex = _services.Live.SpawnPet(st.NodeId, st.InstanceGroup, monsterId,
            (int)st.CharId, st.PosX, st.PosZ,
            _services.Live.GroundHeightAt(
                _services.Scenes.GetScene($"S{st.NodeId:D3}"),
                st.PosX, st.PosZ),
            st.Facing, false, 0);
        if (_services.Live.GetMob(ex.Handle) is { } pet)
        {
            pet.ElfLoc = elf.Loc;
            pet.ElfId = elf.ElfId;
            pet.ElfMode = 2;
            pet.PetName = ElfDisplayName(elf);
            var grow = _services.PartnerGrows
                .GetValueOrDefault(Math.Max(1, elf.Level));
            if (grow != null)
                pet.AttackPower = Math.Max(1,
                    grow.Stat(0, elf.Grow[0]));
            pet.Level = elf.Level;
            pet.MaxHp = elf.Hp > 0 ? elf.Hp : elf.MaxHp;
            pet.Hp = pet.MaxHp;
            pet.RespawnSeconds = 0;
            st.ElfPetHandle = ex.Handle;
            // InvokePassivities natif — CallElf réapplique les
            // passifs famille 9 au maître au re-born.
            await ApplyPetPassivesAsync(session, st, elf, token);
            EdenLog.Write($"[Elf] char={st.CharId} pet re-invoqué " +
                $"sur node {st.NodeId} handle={ex.Handle:X8}");
        }
        await Task.CompletedTask;
    }

    /// <summary>Mort du pet elfe en combat — appelée depuis
    /// FinishMobDeathAsync quand le LiveMob porte un ElfId : l'elf
    /// repasse inactif (status 0, hp planché à 1 — la réanimation
    /// passe par ElfRevive/items natifs) et le panneau du joueur est
    /// notifié (513 flag 0 + 418 type 2 avec le hp résiduel).</summary>
    private async Task NotifyElfPetDiedAsync(ZoneLiveWorld.LiveMob mob)
    {
        var ost = _states.Values.FirstOrDefault(o =>
            o.CharId == (uint)mob.OwnerCharId && o.Migrated);
        if (ost == null || ost.Session == null) return;
        if (!ost.ElfSlots.TryGetValue(mob.ElfLoc, out var elf))
            return;
        elf.Status = 0;
        elf.Hp = Math.Max(1, mob.Hp);
        ost.ElfPetHandle = 0;
        // RemovePassiveElfPet natif @0x80720f0 : la mort du pet purge
        // le vecteur char+0x8d0 — les enchants passifs famille 9
        // accordés au maître tombent avec le pet (sinon le buff
        // devenait permanent en DB et en mémoire).
        await RemovePetPassivesAsync(ost.Session, ost,
            CancellationToken.None);
        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId; var hp = elf.Hp;
            await _persistence.EnqueueAsync((int)ost.CharId,
                _ => _services.ElfRepo!.UpdateStatusAsync(eid, 0));
            await _persistence.EnqueueAsync((int)ost.CharId,
                _ => _services.ElfRepo!.UpdateHpAsync(eid, hp));
        }
        try
        {
            await ost.Session.SendAsync(new CNC_CZ_ClientPetShortCut
            { Field1 = 0, Field2 = elf.Loc }, CancellationToken.None);
            await ost.Session.SendAsync(new CNC_CZ_ClientElfUpdateAttr
            { Field1 = elf.Loc, Field2 = 2, Field3 = elf.Hp },
                CancellationToken.None);
        }
        catch { /* session morte */ }
    }

    /// <summary>Despawn live d'un mob précis avec 269 LeaveNode aux
    /// témoins (même trame que DespawnPetsAsync, sur un handle).</summary>
    private async Task DespawnMobAtAsync(NetSession session,
        EntryState st, uint handle, CancellationToken token)
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientLifeEradicate
        { Value = (int)handle });
        _services.Live.DespawnPet(handle);
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                !ost.Sent.TryRemove(handle, out _))
                continue;
            ost.EntityPos.TryRemove(handle, out _);
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Conversion ElfSlot → record 412/413 (champs
    /// inconnus de loterie à zéro — voir NativeElfCodec).</summary>
    private static NativeElfCodec.ElfInfoRecord ToInfoRecord(ElfSlot e,
        int ownerCharId)
        => new()
        {
            Loc = e.Loc,
            ElfId = e.ElfId,
            OwnerCharId = ownerCharId,
            TemplateId = e.TemplateId,
            Status = e.Status,
            Exp = e.Exp,
            Hp = e.Hp,
            Quality = e.Quality,
            Level = (short)e.Level,
            Name = e.GivenName,
            Grow = e.Grow,
            MixStar = e.MixStar,
            Battle = e.Battle,
            Integrate = e.Integrate,
        };
}
