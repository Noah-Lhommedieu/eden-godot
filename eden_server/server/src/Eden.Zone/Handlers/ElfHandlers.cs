using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Familiers (elfes) : invocation, renvoi, renommage, craft ElfCombine —
/// CEC_Elf* natif (S_Elf.ini : 957 recettes).
/// </summary>
public static class ElfHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // EnterNodeElfContainer (113) — demande du client du snapshot
        // des conteneurs d'équipement familiers → 424. Natif :
        // CNE_CZ_ZoneServerEnterNodeElfContainer::Execute @0x824ce80
        // construit CNC_CZ_ClientEnterNodeElfContainer depuis la map
        // char+0x540 (clé = loc elf → CElfContainer 4 slots).
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodeElfContainer(),
            async (session, msg, charId, token) =>
            {
                var elfs = s.ElfRepo != null
                    ? await s.ElfRepo.GetElfsAsync(charId)
                    : System.Array.Empty<Eden.Core.Database.Repositories
                        .ElfRepository.ElfRow>();
                var items = await s.InventoryRepo
                    .GetElfContainerItemsAsync(charId);
                // Un conteneur par elf existant (la map native est
                // peuplée par elf, même vide) — capacité fixe 4.
                var containers = elfs
                    .Select(e => (e.Loc,
                        (IReadOnlyList<Eden.Core.Game.Inventory
                            .ItemEntity>)items
                            .Where(i => i.BagId == e.Loc)
                            .OrderBy(i => i.Slot).ToList()))
                    .OrderBy(c => c.Loc).ToList();
                await session.SendPacketAsync(424,
                    Codecs.NativeElfCodec.BuildEnterNodeElfContainer(
                        containers), token);
            });

        // ElfCallElf (109) / ElfReturn (108) / PetModeChange (187) /
        // SendPetCmd (188) : interceptés par ZoneEntryFlow.TryHandleAsync
        // — le cycle de vie CKFElfManager natif (table `elf`, BornElfPet,
        // status 0/2, 412/413/418/513) vit dans ZoneEntryFlow.Elf.cs
        // parce qu'il requiert l'EntryState (position, node, sight).

        // ElfChangeName [short elfInstanceId, string name]
        // Natif : CKFElf::ChangeName puis NotifyClient — réémettre le
        // ClientChangeElf éprouvé (même paquet que l'invocation).
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfChangeName(),
            async (session, msg, charId, token) =>
            {
                if (s.Elfs.Rename(charId, msg.Field1, msg.Field2) &&
                    s.Elfs.GetSummoned(charId) is { } elf)
                {
                    await s.Sessions.BroadcastAllAsync(
                        new CNC_CZ_ClientChangeElf
                        {
                            Field1 = charId,
                            Field2 = elf.Definition.Id,
                            Field3 = elf.Name,
                        }, token);
                    EdenLog.Write($"[Zone] ElfRename char={charId} " +
                        $"elf={msg.Field1} -> '{msg.Field2}'");
                }
            });

        // ElfTalk [short elfId] — MORT EN NATIF : Execute @0x824axxx
        // = tail-call GetType() résultat ignoré (désasm : jmp *ecx
        // sur vtable+8). Le +1 friendship local est un ajout serveur
        // assumé (récompense d'interaction), pas une repro du natif.
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfTalk(),
            (session, msg, charId, token) =>
            {
                if (s.Elfs.GetSummoned(charId) is { } elf)
                    s.Elfs.AdjustFriendship(elf.InstanceId, 1);
                return Task.CompletedTask;
            });

        // ElfRevive [short elfInstanceId] — MORT EN NATIF : Execute
        // @0x824a1e9 = tail-call GetType() ignoré (objdump). Le revive
        // (Feed 100/50) est un ajout serveur assumé.
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfRevive(),
            (session, msg, charId, token) =>
            {
                if (s.Elfs.Get(msg.Value) is { OwnerCharacterId: var o } && o == charId)
                    s.Elfs.Feed(msg.Value, 100, 50);
                return Task.CompletedTask;
            });

        // ElfWork [s16 elfIndex][s16 mode] — dispatch natif
        // CKFElfManager::ToWork(char, elfIdx, mode) @0x807c780 :
        //   0=ToIdle, 1=ToMix (combine), 2=CallElf (invoquer),
        //   7=ToLottery, 8=ToDiamondLottery, 9=ToEndLottery.
        // elfIndex = index dans la liste des familiers possédés
        // (char+0x8ac table de CKFElf* — le client connaît l'ordre).
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfWork(),
            async (session, msg, charId, token) =>
            {
                var owned = s.Elfs.GetOwnedBy(charId);
                var elf = (msg.Field1 >= 0 && msg.Field1 < owned.Count)
                    ? owned[msg.Field1]
                    : s.Elfs.GetSummoned(charId);
                if (elf == null) return;
                switch (msg.Field2)
                {
                    case 0:  // ToIdle
                        elf.IsWorking = false;
                        EdenLog.Write($"[Zone] ElfWork char={charId} " +
                            $"elf={elf.InstanceId} -> idle");
                        break;
                    case 2:  // CallElf — invoquer ce familier
                        if (s.Elfs.Summon(charId, elf.InstanceId))
                            await s.Sessions.BroadcastAllAsync(
                                new CNC_CZ_ClientChangeElf
                                {
                                    Field1 = charId,
                                    Field2 = elf.Definition.Id,
                                    Field3 = elf.Name,
                                }, token);
                        break;
                    case 9:  // ToEndLottery — fin de travail
                        elf.IsWorking = false;
                        await session.SendAsync(
                            new CNC_CZ_ClientElfWorkResultEnd
                            {
                                Value = msg.Field1,   // s16 : index du familier
                            }, token);
                        EdenLog.Write($"[Zone] ElfWork char={charId} " +
                            $"elf={elf.InstanceId} -> end");
                        break;
                    default: // 1=ToMix, 7/8=loteries — démarrer le travail
                        elf.IsWorking =
                            s.Elfs.ConsumeEnergy(elf.InstanceId, 10);
                        EdenLog.Write($"[Zone] ElfWork char={charId} " +
                            $"elf={elf.InstanceId} mode={msg.Field2} -> " +
                            $"{(elf.IsWorking ? "démarré" : "énergie insuffisante")}");
                        break;
                }
            });

        // ElfWorkResultOK [s16 elfIndex][u8 ok] — le client confirme la
        // fenêtre de résultat. Le natif ne fait que logger le résultat
        // côté GM-tool (@0x824b560) : la récompense a déjà été produite
        // par EndWork->lottery quand le minuteur de travail s'achève.
        // Ici on termine le travail et on émet le paquet de fin natif
        // 416 ClientElfWorkResultEnd [s16 elfIndex] (format généré
        // depuis le désasm).
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfWorkResultOK(),
            async (session, msg, charId, token) =>
            {
                var owned = s.Elfs.GetOwnedBy(charId);
                var elf = (msg.Field1 >= 0 && msg.Field1 < owned.Count)
                    ? owned[msg.Field1]
                    : s.Elfs.GetSummoned(charId);
                if (elf is { IsWorking: true })
                {
                    elf.IsWorking = false;
                    // Effets de fin natifs : la série #cmd# de la
                    // recette S_ElfCombine applique "Friend Plus" /
                    // "Energy Minus" sur le familier (ConvertCmd de
                    // CKFElfManager). Le craft réussi augmente
                    // l'amitié — le coût énergie a été débité au
                    // démarrage du travail.
                    s.Elfs.AdjustFriendship(elf.InstanceId, 5);
                    // NOTE(verif): le grant d'item nécessite le sac de
                    // mix du familier (conteneur elf) — le NCID qui
                    // alimente les slots de mix n'a pas encore été
                    // localisé dans les captures ; à brancher quand le
                    // wire sera confirmé (cf. AllElfCombineCmd /
                    // CElfEventCmd natifs).
                    await session.SendAsync(
                        new CNC_CZ_ClientElfWorkResultEnd
                        {
                            Value = msg.Field1,     // s16 : index du familier
                        }, token);
                    EdenLog.Write($"[Zone] ElfWorkResult char={charId} " +
                        $"elf={elf.InstanceId} ok={msg.Field2} -> end");
                }
            });

        // ElfCheckData — MORT EN NATIF (Execute vide). Le log
        // d'état local est sans effet protocolaire.
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfCheckData(),
            (session, msg, charId, token) =>
            {
                var elf = s.Elfs.GetSummoned(charId);
                if (elf != null)
                    EdenLog.Write($"[Zone] ElfCheck char={charId} " +
                        $"mood={elf.Mood} energy={elf.Energy} friend={elf.Friendship}");
                return Task.CompletedTask;
            });

        // ElfMix (227) [s16 elf1][s16 elf2][s16 sac][s16 slot] —
        // intercepté par ZoneEntryFlow.TryHandleAsync : fusion
        // d'elfes complète (CPetUpgradeOp/ElfUpgrade/DoElfUpgrade
        // natifs, cast 4 s, 574/575/568). Nécessite EntryState →
        // pas d'enregistrement dispatcher ici.

        // ElfStoneUse — MORT EN NATIF : Execute @0x824a930 = corps
        // vide `return;` (désasm confirmé). Aucun traitement à porter.
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfStoneUse(),
            (session, msg, charId, token) => Task.CompletedTask);

        // ElfStoneUpdate — MORT EN NATIF : Execute @0x824a560 =
        // tail-call vers vtable+8 du perso (GetType, résultat ignoré —
        // objdump 0x824a560-0x824a57b). Aucun traitement à porter.
        d.RegisterChar(() => new CNE_CZ_ZoneServerElfStoneUpdate(),
            (session, msg, charId, token) => Task.CompletedTask);
    }
}
