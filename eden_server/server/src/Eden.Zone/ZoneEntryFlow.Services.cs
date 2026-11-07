using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Services PNJ : boutiques (304), transport (88→393/293),
/// changement de classe (166/168→489/510) et transactions buy/sell.
/// Formats vérifiés sur les serializers natifs désassemblés.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Transport (88) — CNE_CZ_ZoneServerTransport::Execute
    /// désassemblé : lit `[u16 transportId]` (0x12 de la commande) =
    /// l'aire destination (comparée à l'aire courante, bail si égale).
    /// Prérequis natifs : NPC transporteur en cours de dialogue à portée,
    /// coût S_Transport par palier (SNeedMoney {gold, itemTicket}),
    /// puis CZoneServer::TransportCharacterToArea = migration de node.
    /// On rejoue la séquence NodeExit (293/563/302/267/313).</summary>
    private async Task HandleTransportAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2 || st.CharId == 0 || !st.Migrated) return;
        var sel = BitConverter.ToUInt16(payload, 0);

        // Opération CTransportShop requise — désasm Execute @0x8299230 :
        // bail si l'opération courante n'est pas le shop transport
        // (vtbl type 0x25), et ResetOperation en sortie dans TOUS les
        // cas. Sans ce verrou, le second 88 tardif que le client réémet
        // (sel=1 = retour Aven) re-téléportait le joueur juste après
        // son arrivée — le bug "toujours tp à Aven".
        if (!st.TransportOpen)
        {
            EdenLog.Write($"[Zone-Entry] Transport char={st.CharId} " +
                $"sel={sel} sans fenêtre transport ouverte — ignoré");
            return;
        }
        st.TransportOpen = false;   // ResetOperation : one-shot

        // Résolution : clé route directe (GetTransportData natif) ; sinon
        // index dans la liste envoyée par le dernier 393 — certaines
        // versions de l'UI renvoient l'index de la ligne cliquée.
        var route = _services.GameData.TransportRoutes
            .FirstOrDefault(r => r.Id == sel);
        if (route == null && st.TransportRoutes.Count > 0 &&
            sel < st.TransportRoutes.Count)
            route = _services.GameData.TransportRoutes
                .FirstOrDefault(r => r.Id == st.TransportRoutes[sel]);
        if (route == null)
        {
            // Bail natif : sélection vide/route inconnue → SILENCE, pas
            // de téléportation (corrige le TP sauvage vers "Coming soon"
            // qui persistait ensuite le node bidon en DB).
            EdenLog.Write($"[Zone-Entry] Transport char={st.CharId} " +
                $"route {sel} inconnue — ignoré");
            return;
        }
        var destArea = (ushort)route.Id;   // T_Transport : id = aire dest
        if (destArea == st.NodeId) return; // déjà sur place (bail natif)

        var dest = _services.Scenes.GetScene($"S{destArea:D3}");
        if (dest == null)
        {
            EdenLog.Write($"[Zone-Entry] Transport char={st.CharId} " +
                $"route {sel} -> S{destArea:D3} sans scène — ignoré");
            return;
        }
        // Coût par palier de niveau (13 colonnes). SNeedMoney natif :
        // map<levelBracket, {gold, ticketItem}> — approximation palier
        // level/10 bornée à la table.
        var bracket = Math.Clamp(st.Level / 10, 0, 12);
        var cost = route.Costs[bracket];
        EdenLog.Write($"[Zone-Entry] Transport char={st.CharId} " +
            $"node={st.NodeId} -> {destArea} ({route.Name}) " +
            $"cost={cost} lvl={st.Level}");

        // Vérification d'or — désasm CNE_CZ_ZoneServerTransport::Execute
        // @0x8299230 : `cmp %edx,0x4b0(%esi)` (char+0x4b0 = gold) ;
        // si gold < coût → TextOutputIndex(562) + ResetOperation + bail,
        // JAMAIS de TransportCharacterToArea. Le bug live (TP aléatoire
        // en boucle sans or) venait de l'absence de ce garde-fou.
        var gold = await _services.Characters.GetGoldAsync((int)st.CharId);
        if (gold < cost)
        {
            EdenLog.Write($"[Zone-Entry] Transport char={st.CharId} " +
                $"gold={gold} < cost={cost} -> 316 idx 562, pas de TP");
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 562,   // s16 : "fonds insuffisants" (T_TextIndex)
                Field2 = "",    // str : arguments de format
            }, token);
            return;
        }
        // Débit de l'or + 280 ClientUpdateAttr attr=2 (natif
        // CCharacter::GainGold @0x842a890 émet cette attr pour
        // rafraîchir l'or affiché côté client).
        var newGold = await _services.Characters.AddGoldAsync(
            (int)st.CharId, -cost);
        await SendAttrAsync(session, st.CharId, 2, (int)newGold,
            0, token);

        // Arrivée : aux pieds du NPC transporteur de la scène cible
        // (FuncId==8, ex. 50360 "傳送水晶" sur S001 à 220,299). Natif :
        // TransportCharacterToArea @0x8174f80 résout l'aire de service
        // transport de la destination — jamais Positions[0] (point de
        // spawn initial, qui peut être dans une GATEWAY → re-TP chaîné,
        // le bug constaté : Aven -> map haut level).
        float dx = 100, dy = 100, dz = 0;
        var tpNpc = dest.Npcs.FirstOrDefault(sp =>
            _services.GameData.Npcs.FirstOrDefault(n => n.Id == sp.NpcId)
                ?.FuncId == 8);
        if (tpNpc != null)
            (dx, dy, dz) = (tpNpc.X, tpNpc.Y, tpNpc.Z);
        else if (dest.Positions.Count > 0)
            (dx, dy, dz) = dest.Positions[0];
        else if (dest.EventAreas.FirstOrDefault(
            a => a.Type == "GATEWAY") is { } ga)
            (dx, dy, dz) = (ga.X, ga.Y, ga.Z);

        // Migration identique à CheckNodeExit (séquence commune).
        await MigrateToNodeAsync(session, st, destArea, dx, dy, dz, token);
    }

    /// <summary>Ouvre une boutique PNJ — 304 EnterShop
    /// `[i32 npcHandle?][s16 storeId][u8][s16]`. La liste des articles
    /// est lue côté client dans S_Store.ini — le serveur n'a qu'à
    /// ouvrir la bonne boutique.</summary>
    private async Task SendEnterShopAsync(NetSession session, EntryState st,
        int storeId, CancellationToken token)
    {
        st.OpenShopId = storeId;
        // Synchronise l'état shop côté session — les handlers ShopHandlers
        // (BuyItems/SellItems) lisent CurrentStoreId ; sans ça l'achat
        // échouait silencieusement sur GetStore(0) → "Invalid store".
        session.CurrentStoreId = storeId;
        // Mémorisé pour le 304 de retour d'onglet (185 flag=0) —
        // le natif OpenShop émet toujours l'npcUid (local id u16 de
        // scène, CItemShop+0x4) en Field1 — pas le sight-handle.
        session.CurrentShopNpcHandle = st.DialogNpcUid;
        await session.SendAsync(new CNC_CZ_ClientEnterShop
        {
            Field1 = st.DialogNpcUid,
            Field2 = (short)storeId,
        }, token);
        EdenLog.Write($"[Zone-Entry] EnterShop store={storeId}");
    }

    // KB-19/D7 : le 489 natif ne véhicule QUE le vecteur
    // `SCharacterClass` possédé (les 6 lignes de la capture « toutes
    // accessibles » = les lignes insérées à la création — toutes
    // possédées). Les classes de base {1,2,3,4,5,19} arrivent
    // d'elles-mêmes par AddClassFromLimit (reqCharLv=1 — KB-03) ; une
    // ligne possédée hors de cet ensemble s'ajoute de la même façon.

    /// <summary>QueryClassList (166) → 489 PlayerClassList —
    /// format natif capturé (zsniff 17:22:16.260, 162B). Ligne 26B
    /// décodée sur la base `player_classlist` du perso capturé :
    ///   `[u8 classId][u8 aaPoint][u8 0][i32 classExp][i32 classPoint]
    ///    [u8 aaPoint][14×0x00]`
    ///   flag = aa_point (2 sur la classe principale, 1 ailleurs —
    ///   class1 lvl→exp=14/point=90/aa=2 sur test1) — PAS la classe
    ///   courante : après un switch natif le flag reste sur la classe
    ///   d'origine. La classe active passe par l'attr 0x2c (280) et le
    ///   283 ClientModelModify.</summary>
    private async Task HandleQueryClassListAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        // Lignes player_classlist préchargées par le pipeline
        // d'entrée (Phase 2) — valides pour la première émission
        // post-install : Emitted=false borne la fenêtre à avant le
        // premier MigrationDone et EntryDirty couvre les mutations
        // mi-session (changement/octroi de classe) → requête live
        // comme avant.
        var rows = st.Entry is { Emitted: false, ClassRows: not null }
                       ce && !st.EntryDirty
            ? ce.ClassRows
            : await _services.Classes
                .GetClassesByCharacterIdAsync((int)st.CharId);
        var byId = rows.ToDictionary(r => r.ClassId, r => r);
        // KB-19 : seules les classes POSSÉDÉES (ligne player_classlist)
        // sont émises — pas de ligne « lv0 » pour une base absente.
        var ids = rows.Select(r => r.ClassId).OrderBy(i => i).ToList();

        // Wire natif vérifié (Serialize CNC_CZ_ClientPlayerClassList
        // @0x813f2e0 + SCharacterClass::Serialize @0x83dxxxx + capture
        // `e901 ff00 0600` test3) : [u8][u8][u16 n] puis par classe
        // [u8 id][u8 lv][u8 style][i32 exp][i32 point][u8 aa_point]
        // [7×u16 lic]. Le 2e octet est le NIVEAU de classe (colonne
        // `level` de player_classlist), pas aa_point — écrire aa ici
        // affichait des classes niveau 1-2 au lieu du vrai niveau.
        var list = new CNC_CZ_ClientPlayerClassList
        {
            HeaderFlag1 = 0xff,                 // u8 : this[5] (0xff natif)
            HeaderFlag2 = 0,                    // u8 : this[4] (0 natif)
        };
        // Mapping positionnel du codec (noms générés trompeurs) :
        // NodeFlag=classId, ClassId=niveau, Unk1=style, ClassLevel=exp,
        // ClassExp=points, Flags=aa_point, U1..7=licences.
        foreach (var id in ids)
        {
            var owned = byId.TryGetValue(id, out var c);
            var e = new CNC_CZ_ClientPlayerClassList.Entry
            {
                NodeFlag = (byte)id,
                ClassId = (byte)(owned
                    ? Math.Clamp(c!.ClassLevel, 0, 255) : 0),
                Unk1 = (byte)(owned ? c!.Style : 0),
                ClassLevel = owned
                    ? (int)Math.Min(c!.ClassExp, int.MaxValue) : 0,
                ClassExp = owned ? c!.ClassPoints : 0,
                Flags = (byte)(owned
                    ? Math.Clamp(c!.AaPoint, 0, 255) : 1),
            };
            if (owned)
            {
                e.U1 = (ushort)c!.Licenses[0]; e.U2 = (ushort)c!.Licenses[1];
                e.U3 = (ushort)c!.Licenses[2]; e.U4 = (ushort)c!.Licenses[3];
                e.U5 = (ushort)c!.Licenses[4]; e.U6 = (ushort)c!.Licenses[5];
                e.U7 = (ushort)c!.Licenses[6];
            }
            list.Entries.Add(e);
        }
        await session.SendAsync(list, token);
        // 592 ClientPlayerClassCustomData flag=0 — TOUTES les cartes
        // de classe personnalisée (Execute QueryClassList @0x82767d0 :
        // vecteurs equip_type/weapon_type/sorts de char+0xd9c, slot
        // actif char+0xd98). Suivait le 489 dans le burst natif.
        if (st.CustomClassSlots.Count > 0)
            await session.SendPacketAsync(592,
                Eden.Core.Game.Classes.CustomClassPackets.BuildAllSlots(
                    (byte)st.ActiveCCIndex, st.CustomClassSlots), token);
        // 471 ClientUpdateAdvancedAbility — la map advanced_ability
        // persistée (S_Talent) suit la liste de classes : le client
        // affiche les points investis dans l'arbre de talents.
        await SendAdvancedAbilityMapAsync(session, st, token);
        // Mods AttrSpell* des talents de la classe courante — les
        // enchants AA sont cachés (hors player_enchants) : les mods
        // per-spell (cooldown, dégâts par sort…) sont rejoués ici
        // ×points, comme l'EffectToLife natif par point investi.
        await ApplyTalentSpellAttrsAsync(session, st, token);
        EdenLog.Write(
            $"[Zone-Entry] ClassList char={st.CharId} -> {ids.Count} entrées");
    }

    /// <summary>`SSpellData::CheckClass` natif : le mask de classes du
    /// sort (bit classId-1) doit contenir la classe ACTIVE ; mask 0 =
    /// sort libre. Sans ce garde-fou un sort d'une autre classe (ou
    /// d'un build modifié) se lançait quelle que soit la classe.</summary>
    private bool SpellAllowedForClass(EntryState st, byte[] payload)
    {
        if (payload.Length < 2) return true;
        var spellId = BitConverter.ToUInt16(payload, 0);
        var sp = _services.GameData.Spells.FirstOrDefault(x => x.Id == spellId);
        if (sp == null || sp.ClassMask == 0) return true;
        var cls = st.ClassId;
        if (Eden.Core.Game.Classes.ClassSkillTable.CanCast(cls, sp))
            return true;
        EdenLog.Write($"[Zone-Entry] cast refusé char={st.CharId} " +
            $"spell={spellId} class={cls} mask=0x{sp.ClassMask:x}");
        return false;
    }

    /// <summary>Refus d'un cast hors classe : 410
    /// ClientCommandFailReturn `[i32 opcode][i32 spellId]` (même
    /// format que les refus MP / down-time), pour que le client sorte
    /// de l'état « cast demandé » au lieu de rester sans réponse.
    /// </summary>
    private static Task RefuseSpellCastAsync(NetSession session,
        int opcode, byte[] payload, CancellationToken token)
        => session.SendAsync(new CNC_CZ_ClientCommandFailReturn
        {
            Field1 = opcode,                              // i32 : NCID du cast refusé
            Field2 = BitConverter.ToUInt16(payload, 0),   // i32 : spell id
        }, token);

    /// <summary>ChangeClass (168) — wire natif `[u8 classId]`
    /// (désasm Deserialize : un seul octet, pas [u8][u16]).
    /// Valide : la classe doit être POSSÉDÉE (ligne player_classlist —
    /// KB-19, le natif refuse les autres), puis persistance + 510
    /// ChangeClassShortcut (barres par classe) + rafraîchissement de la
    /// liste 489.</summary>
    private async Task HandleChangeClassAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 1 || st.CharId == 0) return;
        int newClass = payload[0];              // u8 : classId cible
        if (newClass == st.ClassId) return;
        var rows = await _services.Classes
            .GetClassesByCharacterIdAsync((int)st.CharId);
        // KB-19 : le switch n'accepte que les classes POSSÉDÉES — les
        // bases non encore octroyées le sont par AddClassFromLimit
        // (KB-03) ; le natif refuse tout le reste (pas d'AddClass ici).
        var owned = rows.Any(c => c.ClassId == newClass);
        if (!owned)
        {
            EdenLog.Write(
                $"[Zone-Entry] ChangeClass refusé char={st.CharId} " +
                $"class={newClass} (non possédée)");
            return;
        }
        var old = st.ClassId;
        var newRow = rows.FirstOrDefault(r => r.ClassId == newClass);
        var newLevel = Math.Max(newRow?.ClassLevel ?? 1, 1);
        var newClassExp = newRow?.ClassExp ?? 0;
        st.ClassId = newClass;
        st.ClassLevel = newLevel;
        await _services.Classes.ChangeClassAsync((int)st.CharId, newClass);
        // Bascule du pool AA — switch natif PlayerFactory : le pool de
        // la classe courante (char+0xa1c) est rangé dans la ligne
        // classlist de l'ancienne classe, puis le pool de la nouvelle
        // est chargé dans player_characters.aa_point.
        await _services.Classes.SwapAaPoolOnClassChangeAsync(
            (int)st.CharId, old, newClass);

        // Bascule des layouts de certificats — ChangeClass natif
        // réécrit player_license.loc depuis lic_1..7 de la nouvelle
        // classe (et sauvegarde l'ancien layout pour l'aller-retour).
        await SyncLicensesOnClassChangeAsync(st, old, newClass);

        // Octroi des class skills de la nouvelle classe — modèle natif
        // AddClass→SimpleGetSpell : vecteur S_Class + spellmasters à
        // mask mono-classe, filtrés req <= classLevel. Sans ça, passer
        // en Thief/Hunter/Cleric/Mage ne donnait AUCUN sort de classe
        // (le 286 ne rejouait que les spellmasters déjà connus).
        var classDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, st.RaceId, newClass);
        var known = (await _services.Classes
                .GetPlayerSpellsAsync((int)st.CharId))
            .Select(s => unchecked((ushort)s.Spell)).ToHashSet();
        foreach (var sm in Eden.Core.Game.Classes.ClassSkillTable
                .GrantableSpells(newClass, newLevel,
                    _services.GameData.Spells, classDef, st.RaceId))
            if (known.Add(unchecked((ushort)sm)))
                await _services.Classes.LearnSpellAsync(
                    (int)st.CharId, unchecked((short)sm));
                    _services.SpellLevelsDirty?.Invoke((int)st.CharId);

        // Skills généraux de la nouvelle couleur — GetCommonSpell
        // natif est appelé au changement de classe pour la classe
        // activée, gaté par le niveau perso (char+0x470).
        foreach (var sm in Eden.Core.Game.Classes.ClassSkillTable
                .GrantableGeneralSpells(newClass, st.Level,
                    _services.GameData.Spells, st.RaceId))
            if (known.Add(unchecked((ushort)sm)))
                await _services.Classes.LearnSpellAsync(
                    (int)st.CharId, unchecked((short)sm));
                    _services.SpellLevelsDirty?.Invoke((int)st.CharId);

        await SendClassChangeBurstAsync(session, st, newClass, newLevel,
            newClassExp, token);
        // Bundle d'entrée périmé — ClassRow, sorts et équipement du
        // 311 ne correspondent plus à la nouvelle classe (Phase 2).
        st.EntryDirty = true;
        EdenLog.Write(
            $"[Zone-Entry] ChangeClass char={st.CharId} {old}->{newClass}");
    }

    // L'octroi des sorts de classe à l'entrée vit désormais dans
    // EnsureEntrySpellsAsync (ZoneEntryFlow.EntryPipeline.Load.cs) —
    // même modèle S_Class + mask, batch LearnSpellsAsync hors-boucle.

    /// <summary>Octroie les skills généraux (multi-masque) de toutes
    /// les classes possédées quand le personnage monte de niveau —
    /// équivalent du sweep `GetCommonSpell` natif appelé après
    /// AddClassFromLimit (gating niveau perso +0x470). Envoie la
    /// liste des spellmasters (286) au client si des sorts ont été
    /// appris.</summary>
    private async Task GrantGeneralSpellsOnLevelAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        try
        {
            var owned = await _services.Classes
                .GetClassesByCharacterIdAsync((int)st.CharId);
            var known = (await _services.Classes
                    .GetPlayerSpellsAsync((int)st.CharId))
                .Select(s => unchecked((ushort)s.Spell)).ToHashSet();
            var learned = false;
            var fresh = new HashSet<ushort>();
            foreach (var cls in owned)
                foreach (var sm in Eden.Core.Game.Classes.ClassSkillTable
                        .GrantableGeneralSpells(cls.ClassId, st.Level,
                            _services.GameData.Spells, st.RaceId))
                    if (known.Add(unchecked((ushort)sm)))
                    {
                        await _services.Classes.LearnSpellAsync(
                            (int)st.CharId, unchecked((short)sm));
                            _services.SpellLevelsDirty?.Invoke((int)st.CharId);
                        learned = true;
                        fresh.Add(unchecked((ushort)sm));
                    }
            if (learned)
            {
                await SendPlayerSpellsAsync(session, st, token, fresh);
                EdenLog.Write(
                    $"[Zone-Entry] general skills char={st.CharId} " +
                    $"lvl={st.Level}");
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[Zone-Entry] GeneralSpells char={st.CharId}: {ex.Message}");
        }
    }

    /// <summary>Burst natif de changement de classe — capture zsniff
    /// 17:22:35.923→17:22:35.993 (client `168 [u8 classId]` → 39 paquets
    /// S&gt;C en ~70 ms). Ordre natif respecté :
    ///   refresh vitals (280) → blocs stats (296/492/493) → 488 Closets
    ///   → 489 liste → 280 attr 0x2c (classe active — LE champ qui fait
    ///   basculer l'UI client) → 283 ModelModify → attrs divers → 509
    ///   License → 286+316 par spellmaster appris → 510 shortcuts.
    /// Sans le 0x2c/283/286 le client gardait la classe affichée
    /// inchangée (bug live : "Transform!" sans effet).</summary>
    private async Task SendClassChangeBurstAsync(NetSession session,
        EntryState st, int newClass, int classLevel, long newClassExp,
        CancellationToken token)
    {
        st.WeaponResolved = false;
        await ResolveWeaponAsync(st);
        // --- Bloc 1 : refresh des vitals (capture 35.925) ---
        await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
            st.PlayerMaxHp, token);                 // attr 0x11 : HP self
        await SendAttrAsync(session, st.CharId, 0x00, st.PlayerHp,
            st.PlayerMaxHp, token);                 // attr 0 : HP broadcast
        await SendVitalMpAsync(session, st.CharId, st.PlayerMp,
            st.PlayerMaxMp, token);                 // attr 0x12 : MP
        await SendAttrAsync(session, st.CharId, 0x01,
            (int)Math.Min(st.Experience, int.MaxValue), 0, token);
                                                    // attr 1 : exp totale

        // --- Bloc 2 : blocs de stats 296/492/493 (capture 35.925) ---
        await SendStatBlocksAsync(session, st, newClass, classLevel, token);

        // --- Bloc 3 : 488 Closets + 489 liste (capture 35.926) ---
        await SendPlayerClosetsAsync(session, st, token);
        await HandleQueryClassListAsync(session, st, token);

        // --- Bloc 4 : bascule effective (attr 0x2c = classe active) ---
        await SendAttrAsync(session, st.CharId, 0x2c, newClass, 0, token);
        await SendAttrAsync(session, st.CharId, 0x07, 1, 0, token);
        await SendModelModifyAsync(session, st, token);
        // attr 6 = class_point de la NOUVELLE classe — envoyer 0 vidait
        // visuellement la barre d'XP de classe (la DB gardait la valeur,
        // d'où le "exp perdue au switch" rapporté live). La capture
        // montre l'attr 6 avec la vraie valeur après le switch.
        await SendAttrAsync(session, st.CharId, 0x06,
            (int)Math.Min(newClassExp, int.MaxValue), 0, token);
        await SendAttrAsync(session, st.CharId, 0x03ef, 0, 0, token);
        // attr 0x31 = aa_point de la classe devenue active — le
        // client grise les talents sans le vrai pool (le « 1 » en dur
        // bloquait toute validation au-delà du premier point, ex.
        // Sacred Duty du cleric qui exige 25 pts cumulés dans
        // l'arbre : refus SetAAPoint natif tant que le total n'est
        // pas atteint — la fenêtre lisait le pool tronqué).
        var newAa = (await _services.Classes
            .GetClassProgressAsync((int)st.CharId, newClass))?.AaPoint
            ?? 1;
        await SendAttrAsync(session, st.CharId, 0x31,
            Math.Clamp(newAa, 0, 255), 0, token);

        // --- Bloc 5 : 509 License + spellmasters (286+316) ---
        // 509 = liste complète des licences possédées {s16 id, u8 loc}
        // — le vecteur +0xaf0 natif après la bascule de classe. Un
        // corps vide (u16 0) cachait tous les certificats du joueur
        // dans l'interface classe (bug live).
        var licState = _services.Licenses.StateOf((int)st.CharId);
        var lic = new CNC_CZ_ClientPlayerLicense();
        foreach (var (itemId, loc) in licState.Owned)
            lic.Licenses.Add((itemId, loc));   // {s16 itemId, u8 loc 0-7}
        await session.SendAsync(lic, token);
        await SendPlayerSpellsAsync(session, st, token);

        // --- Bloc 6 : 510 shortcuts par classe ---
        await SendClassShortcutsAsync(session, st, token);
    }

    /// <summary>296/492/493 — blocs d'attributs rafraîchis après
    /// changement de classe. Formats prouvés par capture zsniff
    /// 17:22:35.925 (296=96B, 492=32B, 493=56B). Délègue à
    /// <see cref="ProfileStatEmitter"/> qui ajoute les bonus de tout
    /// l'équipement porté (RefreshEQsAttr natif).</summary>
    private async Task SendStatBlocksAsync(NetSession session, EntryState st,
        int classId, int classLevel, CancellationToken token)
    {
        // S_Level est indexée par le niveau PERSONNAGE (st.Level), pas
        // le niveau de classe — le lookup natif fait
        // CLevelQuery::Get(char+0x470=niveau perso).
        await ProfileStatEmitter.SendAsync(session, _services,
            (int)st.CharId, st.RaceId, classId, st.Level, token);
    }

    /// <summary>488 ClientPlayerClosets — wire réel décodé dans
    /// <c>CNC_CZ_ClientPlayerClosets::Serialize</c> :
    /// `[u8 type][u16 n][n×{u8 class][s16 count][count×24B closet}]`.
    /// Map vide → replay de la capture 30B (perso sans costume) en
    /// keyant le slot par défaut sur la classe COURANTE — le corps
    /// reste byte-identique quand class=5.</summary>
    private static Task SendPlayerClosetsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.Closets.Count == 0)
        {
            // `00 0100 <class> 01` + 24×00 — slot zéro de la classe
            // courante (comportement observé natif sur perso frais).
            var replay = new SortedDictionary<byte,
                List<(ulong, ulong, ulong)>>
            { [(byte)st.ClassId] = [(0UL, 0UL, 0UL)] };
            return session.SendPacketAsync(488,
                Codecs.ClosetPackets.Build(0, replay), token);
        }
        return session.SendPacketAsync(488,
            Codecs.ClosetPackets.Build(0, st.Closets), token);
    }

    /// <summary>Overlay d'état live émis après le dump de MigrationDone.
    /// Le burst rejoué contient les valeurs du perso CAPTURÉ (autre
    /// compte) — gold/HP/MP affichés étaient donc ceux du replay, pas du
    /// perso DB (bug live : gold=0). On réémet uniquement les attrs à
    /// corriger : la capture native d'entrée ne contient AUCUN attr
    /// 0x02/0x06/0x2c ni 283 — la classe vient du patch classId@23 du
    /// 310. Le 283 ModelModify (mask 0x5e00 = EModelTransition classe)
    /// rejoué ici provoquait l'animation "changement de classe" à
    /// CHAQUE entrée de map — retiré, ainsi que le 489 (le client le
    /// demande lui-même par QueryClassList 166).
    /// Attributs natifs (capture d'entrée, identifiés par valeur) :
    ///   0x00 = HP cur/max, 0x01 = MP cur/max, 0x02 = gold (i32, prouvé
    ///   par GainGold→ClientUpdateAttr attr 2 @zone/decompiled.c),
    ///   0x11 = HP self, 0x12 = MP self.</summary>
    private async Task SendEntryStateOverlayAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
            st.PlayerMaxHp, token);   // attr 0x11 : HP self cur/max
        await SendAttrAsync(session, st.CharId, 0x00, st.PlayerHp,
            st.PlayerMaxHp, token);   // attr 0x00 : HP broadcast
        await SendVitalMpAsync(session, st.CharId, st.PlayerMp,
            st.PlayerMaxMp, token);   // attr 0x12 : MP self
        await SendAttrAsync(session, st.CharId, 0x01, st.PlayerMp,
            st.PlayerMaxMp, token);   // attr 0x01 : MP broadcast
        await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
            token);                   // attr 0x02 : or courant
        // EXP perso : le 310 ne porte pas l'exp courante — sans ce 600
        // la barre d'EXP restait à 0 % à chaque connexion alors que la
        // DB (et les gains suivants, eux-mêmes en 600) avaient la valeur.
        await session.SendAsync(new CNC_CZ_ClientUpdateAttrNewExp
        {
            Field1 = (int)st.CharId, Field2 = st.Experience,
        }, token);
        // A4.5 : trait héroïque (create_flag) — paquet custom Reborn.
        await session.SendAsync(new Handlers.CNC_CZ_ClientHeroTrait
        {
            Trait = (byte)Math.Clamp(st.CreateFlag, 0, 255),
        }, token);
        // attr 0x31 = aa_point de la classe active — la fenêtre de
        // talents du client lit ce pool pour activer la validation
        // (la liste 489 ne suffit pas : l'attr self est le canal que
        // GainAAPoint natif met à jour en jeu). Ligne préchargée par
        // le pipeline d'entrée (Phase 2) — fallback live si périmée.
        var aaRow = st.Entry is { ClassRow: not null } ae
                  && !st.EntryDirty
            ? ae.ClassRow
            : await _services.Classes.GetClassProgressAsync(
                (int)st.CharId, st.ClassId);
        await SendAttrAsync(session, st.CharId, 0x31,
            Math.Clamp(aaRow?.AaPoint ?? 0, 0, 255), 0, token);
        // Relog en état mort : le client rouvre sa fenêtre de mort
        // (305 victime=soi + 531 aire de réapparition).
        if (st.Dead)
        {
            await session.SendAsync(new CNC_CZ_ClientDie
                { Field1 = st.CharId, Field2 = 0, Field3 = 0 }, token);
            await session.SendAsync(new CNC_CZ_ClientReviveInfo
                { Value = st.ReviveAreaId }, token);
        }
    }

    /// <summary>283 ClientModelModify — désasm ctor @0x813f2c0 +
    /// Serialize @0x813c230 : `[i32 handle][s8 champ char][i32 mask]
    /// [u16 n][n×u32][u16 m][m×u32]`. Le mask est un BITMASK
    /// EModelTransition (chaque bit lit un champ du perso) — le
    /// changement de classe natif = 0x5e00. Capture : liste de 8 u32
    /// (modèles d'équipement ; le perso capturé avait l'item 827 en
    /// slot 7) puis un vecteur d'1 handle (le perso lui-même).
    /// Émettre 8 zéros effaçait le visuel du perso — le "portrait"
    /// (tête/visage) disparaissait après chaque changement de classe.</summary>
    private async Task SendModelModifyAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Slots de modèle = items portés — vue native `equipment`
        // (loc = slot d'équipement, plusieurs lignes par loc = buffers
        // de save ; première ligne par loc suffit).
        var slots = new uint[8];
        var eq = await _services.InventoryRepo.GetEquippedItemsAsync(
            (int)st.CharId);
        foreach (var (loc, itemId) in eq)
            if (loc is >= 0 and < 8 && slots[loc] == 0)
                slots[loc] = (uint)itemId;
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(st.CharId));    // i32 : handle
        body.Add(0x0a);                                     // s8 : char byte
        body.AddRange(BitConverter.GetBytes(0x5e00));       // i32 : mask
        body.AddRange(BitConverter.GetBytes((short)8));     // u16 : n slots
        foreach (var s in slots)
            body.AddRange(BitConverter.GetBytes(s));        // u32 : equipement
        body.AddRange(BitConverter.GetBytes((short)1));     // u16 : m vec
        body.AddRange(BitConverter.GetBytes(st.CharId));    // u32 : handle
        await session.SendPacketAsync(283, body.ToArray(), token);
    }

    /// <summary>286 ClientGetSpell + 316 nom par spellmaster appris
    /// — capture zsniff 17:22:35.926 :
    ///   286 `[s16 spell][s16 spell][s16 order][i32 0]` (10B)
    ///   316 `[u16 0x05f4][u16 len][str T_Spell]` (nom localisé)
    /// spellmaster_id = id s16 signé du sort (50017 → -15535).
    /// IMPORTANT : le natif n'émet que les sorts de la CLASSE ACTIVE
    /// (3 spellmasters warrior dans la capture, pas les 30 de
    /// player_spells). Envoyer les sorts des autres classes indexe le
    /// tableau de sorts de la mauvaise classe côté client → alpaca
    /// crash au changement de classe (constaté live).</summary>
    private async Task SendPlayerSpellsAsync(NetSession session,
        EntryState st, CancellationToken token,
        IReadOnlySet<ushort>? announce = null)
    {
        var all = await _services.Classes
            .GetPlayerSpellsAsync((int)st.CharId);
        // Sorts de la classe active : le set GrantableSpells (vecteur
        // S_Class ∪ mask mono-classe, req <= classLevel) est exactement
        // ce que le client attend pour la classe courante — même liste
        // que l'octroi ChangeClass. Les sorts d'autres classes restent
        // en DB mais ne sont pas poussés.
        var classDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(_services.GameData.Classes, st.RaceId, st.ClassId);
        var grantable = Eden.Core.Game.Classes.ClassSkillTable
            .GrantableSpells(st.ClassId, Math.Max(st.ClassLevel, 1),
                _services.GameData.Spells, classDef, st.RaceId)
            .Select(id => unchecked((short)id)).ToHashSet();
        // Skills généraux/raciaux de la classe active (mask multi-bit
        // contenant son bit, niveau perso suffisant, race compatible).
        foreach (var id in Eden.Core.Game.Classes.ClassSkillTable
                     .GrantableGeneralSpells(st.ClassId, st.Level,
                         _services.GameData.Spells, st.RaceId))
            grantable.Add(unchecked((short)id));
        var spells = all.Where(s => grantable.Contains(s.Spell))
            .ToList();
        foreach (var (sm, level) in spells)
        {
            await session.SendAsync(new CNC_CZ_ClientGetSpell
            {
                Field1 = sm, Field2 = sm,             // s16 ×2 (doublé natif)
                Field3 = level, Field4 = 0,           // s16 niveau, i32 0
            }, token);

            // « Vous avez appris… » (0x5f4) seulement pour un sort
            // réellement nouveau (montée de niveau), jamais au
            // changement de classe ni au renvoi de la liste.
            if (announce == null
                || !announce.Contains(unchecked((ushort)sm))) continue;
            var name = _services.GameData.SpellNames
                .GetValueOrDefault(unchecked((ushort)sm), "");
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 0x05f4,                    // s16 : slot nom
                Field2 = name,                      // str : nom T_Spell
            }, token);
        }
    }

    /// <summary>510 ClientChangeClassShortcut — relais natif de
    /// `CNE_DBA_ClientShortcut` (désasm Execute @0x82f80xx : le DBA
    /// recharge `shortcut_menu` de la classe ACTIVE dans char+0x684
    /// = vector&lt;vector&lt;int&gt;&gt; indexé par PAGE, puis Zone
    /// relaie le 510). Format prouvé par la capture (zsniff 17:22:35
    /// `01 00000000` et 17:22:57 `02 0100 ff6f366c 0000 0000`) :
    ///   [u8 nPages][n × {u16 mask slots + i32 par bit levé}]
    ///   [u16 nExtra][i32…]
    /// Chaque entrée = UNE PAGE de la classe courante (mask bit(i) =
    /// slot i rempli, Serialize @0x8202200 : local_24=1 shifté par
    /// index). L'ancien code envoyait une entrée PAR CLASSE → le
    /// client lisait l'entrée 0 comme page 0 (toujours la barre
    /// warrior — bug live). Sans le u16 terminal le client lit le
    /// paquet suivant en désalignement → crash (constaté sur test2).
    /// </summary>
    private async Task SendClassShortcutsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        // Pages persistées de la classe ACTIVE uniquement (char+0x684
        // natif ne contient que la classe courante après le flush
        // "Update Class Shortcut OldClassID" + reload DBA).
        var pages = _services.Social != null
            ? await _services.Social.GetClassShortcutPagesAsync(
                (int)st.CharId, st.ClassId)
            : new Dictionary<short, int[]>();
        // Le vecteur natif est contigu : pages 0..max, trous = page
        // vide (GetShortcutOfPage indexe char+0x684 par page).
        var maxPage = pages.Count > 0 ? pages.Keys.Max() : (short)0;
        var sc = new CNC_CZ_ClientChangeClassShortcut();
        for (short p = 0; p <= maxPage; p++)
        {
            pages.TryGetValue(p, out var slots);
            var bar = new CNC_CZ_ClientEnterNodeShortcut.ShortcutBar();
            // Le codec exclut les slots -1 ET 0 (fantômes persistés) —
            // slots manquants = barre plus courte, complétée à -1.
            if (slots != null)
                bar.Slots.AddRange(slots);
            sc.Bars.Add(bar);
        }
        await session.SendAsync(sc, token);
    }
}
