using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Cycle de vie et présence du joueur : déconnexion sécurisée, emotes,
/// actions, orientation, ciblage, raccourcis, titres, drapeau PvP,
/// points de talent, résurrection.
/// </summary>
public static class PlayerHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Déconnexion ---
        // SafeQuitReq [uint ?, byte ?, int ?] — demande de quit différé
        d.RegisterChar(() => new CNE_CZ_ZoneServerSafeQuitReq(),
            async (session, msg, charId, token) =>
            {
                await session.SendAsync(new CNC_CZ_ClientSafeQuitResp
                {
                    Value = 30, // countdown 30 s (valeur native standard)
                }, token);
                EdenLog.Write($"[Zone] SafeQuitReq char={charId} countdown=30");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerSafeQuitCancel(),
            (session, msg, charId, token) =>
                session.SendAsync(new CNC_CZ_ClientSafeQuitCancel(), token));

        // SafeQuitCountDownOver [byte] — fin du compte à rebours → déco
        d.RegisterChar(() => new CNE_CZ_ZoneServerSafeQuitCountDownOver(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] SafeQuitOver char={charId} -> déconnexion");
                s.Sessions.Remove(session);
                s.Entities.Remove(charId);
                return Task.CompletedTask;
            });

        // LogoutReq → LogoutResp (retour sélection personnage)
        d.RegisterChar(() => new CNE_CZ_ZoneServerLogoutReq(),
            (session, msg, charId, token) =>
                session.SendAsync(new CNC_CZ_ClientLogoutResp(), token));

        // Echo — keepalive client
        d.Register(() => new CNE_CZ_ZoneServerEcho(),
            (session, msg, token) => Task.CompletedTask);

        // --- Présence / animations ---
        // PlayerEmotion [byte emotionId]
        d.RegisterChar(() => new CNE_CZ_ZoneServerPlayerEmotion(),
            (session, msg, charId, token) =>
                s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientPlayerEmotion
                {
                    Field1 = (uint)charId,
                    Field2 = msg.EmotionId,
                }, token));

        // PlayerAction [byte action, int, int, int]
        d.RegisterChar(() => new CNE_CZ_ZoneServerPlayerAction(),
            (session, msg, charId, token) =>
                s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientPlayerAction
                {
                    Field1 = (uint)charId,
                    Field2 = msg.Action,
                    Field6 = msg.Param3,
                }, token));

        // TurnSelf [float orientation]
        // Natif CNE_CZ_ZoneServerTurnSelf::Execute @0x829a380 : kind==7,
        // char+0x58 = orientation, puis BroadCastToCanSee d'un
        // CNC_CZ_ClientTurnChar (441) [i32 handle][f32 dir].
        d.RegisterChar(() => new CNE_CZ_ZoneServerTurnSelf(),
            async (session, msg, charId, token) =>
            {
                if (s.Entities.Find(charId) is { } e) e.Dir = msg.Value;
                await s.Sessions.BroadcastExceptAsync(session,
                    new CNC_CZ_ClientTurnChar
                    {
                        Field1 = charId,   // i32 handle joueur (= charId)
                        Field2 = msg.Value, // f32 orientation
                    }, token);
            });

        // PVPFlagModify [byte flag]
        // MORT EN NATIF : Execute = tail-call GetType() ignoré
        // (le flag PvP ne change pas via ce NCID côté serveur US).
        d.RegisterChar(() => new CNE_CZ_ZoneServerPVPFlagModify(),
            (session, msg, charId, token) =>
            {
                // Natif vérifié : CNE_CZ_ZoneServerPVPFlagModify::Execute
                // est VIDE (zone:481372 — la commande est ignorée par le
                // serveur officiel). Log informatif uniquement.
                EdenLog.Write($"[Zone] PVPFlag char={charId} -> {msg.Value} (ignoré, no-op natif)");
                return Task.CompletedTask;
            });

        // --- Ciblage ---
        // GetSelectLifeTarget [int targetEntityId] — info cible (PV, niveau)
        d.RegisterChar(() => new CNE_CZ_ZoneServerGetSelectLifeTarget(),
            async (session, msg, charId, token) =>
            {
                var e = s.Entities.Find(msg.Value);
                await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
                {
                    Field1 = msg.Value,
                    Field3 = e?.CurrentHp ?? 0,
                }, token);
            });

        // TargetUpdate [int entityId] — le joueur change de cible
        d.RegisterChar(() => new CNE_CZ_ZoneServerTargetUpdate(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Raccourcis ---
        // UpdateShortcut [s16 page][s16 slot][i32 field][i32 oldField]
        // — désasm CCharacter::UpdateShortcut @0x8421xxx :
        //   old_field == 0 → set direct slot=field ;
        //   old_field != 0 → déplacement : la valeur quittant son
        //   ancien slot est retirée là où elle était (sinon l'icône
        //   resterait dupliquée après un drag).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateShortcut(),
            async (session, msg, charId, token) =>
            {
                if (s.Social == null || msg.Field1 is < 0 or > 10 ||
                    msg.Field2 is < 0 or > 9)
                    return;
                if (msg.Field4 != 0)
                    await s.Social.ClearShortcutValueAsync(charId,
                        msg.Field4);
                await s.Social.SetShortcutAsync(charId, msg.Field1,
                    msg.Field2, 0, msg.Field3);
            });

        // GetShortcutOfPage [s16 page][u8 flags][s16 classId] —
        // Execute natif @0x825c0xx : DEUX comportements selon Field3.
        //   Field3 == 0 → répond 352 ClientReceiveShortcutOfPage.
        //   Field3 != 0 (ancienne classe, envoyé au changement) →
        //     AUCUNE réponse client : le natif n'envoie qu'une
        //     commande DBA "Update Class Shortcut OldClassID=%hd" à la
        //     mission pour flusher les raccourcis serveur vers la DB.
        //     Chez nous chaque 54 UpdateShortcut persiste déjà par
        //     classe (shortcut_menu.class) — rien à faire, et surtout
        //     ne PAS renvoyer un 352 non sollicité (le client n'en
        //     attend pas à ce point du changement de classe).
        // Format fil du 352 (Serialize @0x081bf9xx + Execute) :
        //   [s16 page][u16 mask][i32 par slot rempli][u16 nExtra]
        //   mask : bit(i+1) ↔ slot i rempli ; bit0 = 1 si barre vide.
        //   "rempli" natif = valeur != -1 ; notre DB stocke aussi 0
        //   pour vide → les deux sont filtrés.
        d.RegisterChar(() => new CNE_CZ_ZoneServerGetShortcutOfPage(),
            async (session, msg, charId, token) =>
            {
                // Branche native Field3 != 0 : flush DBA, sans réponse.
                if (msg.Field3 != 0) return;
                var slots = new int[10];
                if (s.Social != null)
                {
                    var rows = await s.Social.GetShortcutsAsync(charId);
                    foreach (var r in rows.Where(r => r.Page == msg.Field1))
                        if (r.Slot is >= 0 and < 10)
                            slots[r.Slot] = r.RefId;
                }
                // Mask natif (Execute @0x825c0xx) : bit0 = sentinelle
                // "page vide" (init 1, effacé `& ~1` dès qu'un slot est
                // rempli), bit(i+1) = slot i rempli — valeur != -1
                // natif ; notre DB stocke aussi 0 pour vide → les deux
                // sont filtrés.
                short mask = 0;
                var values = new List<int>();
                for (var i = 0; i < 10; i++)
                    if (slots[i] != 0 && slots[i] != -1)
                    {
                        mask |= (short)(1 << (i + 1));
                        values.Add(slots[i]);
                    }
                if (mask == 0) mask = 1;
                await session.SendAsync(
                    new CNC_CZ_ClientReceiveShortcutOfPage
                    { Page = msg.Field1, Mask = (ushort)mask,
                      Values = values.ToArray() }, token);
            });

        // --- Titres ---
        // NCIDs 93 (UpdateAppellation) et 94 (QueryAppellation) :
        // interceptés par ZoneEntryFlow (HandleSetAppellationAsync /
        // HandleQueryAppellationAsync — SetPresentAppellation natif
        // @0x080f2400 / QueryExecute @0x082748c0) car ils requièrent
        // l'EntryState (seq, vérif possession, broadcast voyants).
        // Jamais atteints ici.

        // --- Classe ---
        // NCID 166 QueryClassList : intercepté par ZoneEntryFlow
        // (HandleQueryClassListAsync, builder 489 capture-vérifié
        // avec lic_1..7 réels) — l'ancien handler ci-dessous émettait
        // un format différent (champs décalés, pas de licences) et
        // n'était de toute façon jamais atteint (le flow retourne
        // true avant le dispatcher). Supprimé pour éviter la
        // régression.

        // --- Skills ---
        // UseSkillPoint [s16 spellmasterId] — amélioration de sort.
        // Désasm `CNE_CZ_ZoneServerUseSkillPoint::Execute` @0x829d4xx +
        // `CSpellMasterFactory::SetSpellLevel` @0x8156280 :
        //   · le spellmaster doit être CONNU (player_spells)
        //   · niveau cible = spell_level + 1 ; sorts type 6/7 (classe)
        //     exigent class_lv >= cible, les autres char level >= cible
        //   · monnaie selon S_Spell col42 (SSpellData+0xcc) :
        //       1 → OR   : coût = col43 × S_SpellReference[col31] / 100
        //       9 → CP   : coût = col43 × S_SpellReference[col30] / 100
        //     (fixed16.16 natif : K1=65536.0, K2=0.01 → base×taux/100)
        //   · réponse : 286 ClientGetSpell
        //     `[s16 spellId][s16 reqId][s16 nvLevel][i32 CP restant]`
        //     (Serialize @0x8157xxx) + refresh de la monnaie (280 attr2
        //     pour l'or / attr 0x3ef pour les CP — GainNp natif).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUseSkillPoint(),
            async (session, msg, charId, token) =>
            {
                var smId = msg.SpellId;
                var r = await TrySpellLevelUpAsync(s, charId, smId, 1);
                if (r is null) return;
                await EmitSpellLevelResultAsync(session, s, charId, smId,
                    r.Value, token);
                EdenLog.Write($"[Zone] SkillUp char={charId} " +
                    $"spell={smId} lv={r.Value.NewLevel} " +
                    $"pay={r.Value.PayType}");
            });

        // NCID 623 `[s16 spellId]` — « Level Max » du client Reforged
        // (btn_max, d3d9.dll my_max_click_handler @0x6d1cff40 : même
        // payload que le 12, NCID réécrit à 0x26f). Le natif n'a pas
        // cet opcode — il monte UN niveau par paquet ; le client
        // Reforged demande ici « autant de niveaux que possible »
        // (gate natif par niveau : niveau requis + coût or/CP — la
        // boucle s'arrête dès qu'un niveau échoue, exactement comme
        // le ferait le spam de paquets 12).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUseSkillPointMax(),
            async (session, msg, charId, token) =>
            {
                var smId = msg.SpellId;
                EdenLog.Write($"[Zone] SkillMax RECU char={charId} " +
                    $"spell={smId}");
                var r = await TrySpellLevelUpAsync(s, charId, smId,
                    Eden.Core.Game.GameBalance.MaxSpellLevel);
                if (r is null)
                {
                    // Diagnostic détaillé — distinguer chaque gate natif.
                    var dbg = await s.Classes.GetPlayerSpellsAsync(charId);
                    var known = dbg.FirstOrDefault(x => x.Spell == smId);
                    var def = s.Spells.GetDefinition(unchecked((ushort)smId));
                    EdenLog.Write($"[Zone] SkillMax char={charId} " +
                        $"spell={smId} — REFUSÉ : " +
                        $"known={(known.Spell != 0)} lv={known.Level} " +
                        $"def={(def != null)} pay={def?.PayType} " +
                        $"fam={def?.Family}");
                    return;
                }
                await EmitSpellLevelResultAsync(session, s, charId, smId,
                    r.Value, token);
                EdenLog.Write($"[Zone] SkillMax char={charId} " +
                    $"spell={smId} lv={r.Value.NewLevel} " +
                    $"pay={r.Value.PayType} cost={r.Value.TotalCost}");
            });

        // Resurrect [byte mode] — résurrection (différent de Revive=item)
        d.RegisterChar(() => new CNE_CZ_ZoneServerResurrect(),
            async (session, msg, charId, token) =>
            {
                if (s.Entities.Find(charId) is { } e && e.IsDead)
                {
                    e.CurrentHp = e.MaxHp / 10; // 10 % PV à la résurrection
                    await s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientResurrect
                    {
                        Value = (uint)charId,
                    }, token);
                }
            });
    }

    /// <summary>Résultat d'une montée de sort : niveau atteint, type de
    /// paiement natif (1=or, 9=CP), coût total débité et solde CP
    /// restant (champ i32 du 286).</summary>
    private readonly record struct SpellLevelResult(
        short NewLevel, int PayType, long TotalCost, int CpRemaining);

    /// <summary>Monte un spellmaster de `maxLevels` niveaux en
    /// appliquant la logique native de <c>SetSpellLevel</c> par niveau
    /// (Execute @0x829d470 → SetSpellLevel @0x8156280) :
    /// gate famille 6/7 → class_lv, sinon niveau perso ; coût
    /// `base × S_SpellReference[col30/31] / 100` en or (pay=1) ou CP
    /// (pay=9). S'arrête au premier niveau impossible — exactement
    /// comme le spam de paquets 12 natif. Retourne null si aucun
    /// niveau n'a pu être appliqué.</summary>
    private static async Task<SpellLevelResult?> TrySpellLevelUpAsync(
        ZoneServices s, int charId, short smId, int maxLevels)
    {
        var spells = await s.Classes.GetPlayerSpellsAsync(charId);
        var known = spells.FirstOrDefault(x => x.Spell == smId);
        if (known.Spell == 0 && smId != 0) return null;
        var curLevel = Math.Max(1, (int)known.Level);

        var def = s.Spells.GetDefinition(unchecked((ushort)smId));
        if (def is null || def.PayType is not (1 or 9)) return null;

        var pc = await s.Characters.GetCharacterByIdAsync(charId);
        if (pc is null) return null;
        // Un sort réservé à d'autres classes ne se monte qu'avec SA classe active : sinon le
        // gate (class_lv) et les PC débités sont ceux de la classe courante (Guerrier niv 28,
        // 92 025 PC) alors que le client affiche la classe sélectionnée (Chasseur niv 1, 0 PC).
        if (def.ClassMask != 0 &&
            !Eden.Core.Game.Classes.ClassSkillTable.CanCast(pc.ClassId, def))
        {
            EdenLog.Write($"[Zone] SkillUp refusé char={charId} spell={smId} " +
                $"classe active={pc.ClassId} mask=0x{def.ClassMask:x}");
            return null;
        }
        var classProg = await s.Classes.GetClassProgressAsync(
            charId, pc.ClassId);

        var baseCost = Math.Max(0, def.UpgradeCostBase);
        var divisor = Eden.Core.Game.GameBalance.SkillCostDivisor;
        var maxLv = Eden.Core.Game.GameBalance.MaxSpellLevel;

        long totalCost = 0;
        var target = curLevel;
        long gold = 0;
        var cp = classProg?.ClassPoints ?? 0;
        if (def.PayType == 1)
            gold = await s.Characters.GetGoldAsync(charId);

        for (var i = 0; i < maxLevels; i++)
        {
            var next = target + 1;
            if (next > maxLv) break;
            // Gate de niveau natif : famille 6/7 (sorts de classe) →
            // class_lv >= cible ; sinon niveau perso >= cible.
            if (def.Family is 6 or 7)
            {
                if (classProg is null || classProg.ClassLevel < next)
                    break;
            }
            else if (pc.Level < next) break;

            var rate = s.Spells.GetUpgradeCost(next);
            var cost = (long)baseCost *
                (def.PayType == 1 ? rate?.GoldRate ?? 0
                                  : rate?.CpRate ?? 0) / divisor;
            if (def.PayType == 1)
            {
                if (gold - totalCost < cost) break;
            }
            else if (cp - totalCost < cost) break;
            totalCost += cost;
            target = next;
        }
        if (target == curLevel) return null;

        // Débit effectif — une seule écriture par montée.
        if (def.PayType == 1)
            await s.Characters.AddGoldAsync(charId, -totalCost);
        else
            cp = await s.Classes.AddClassPointsAsync(
                charId, pc.ClassId, -(int)totalCost);

        // Persiste le niveau final (miroir SavePlayerSpell natif).
        await s.Classes.SetSpellLevelAsync(charId, smId, (short)target);
        s.SpellLevelsDirty?.Invoke(charId);
        return new SpellLevelResult((short)target, def.PayType,
            totalCost, cp);
    }

    /// <summary>Émet la réponse native d'une montée de sort :
    /// refresh monnaie (280 attr2 or / attr 0x3ef CP — GainGold/
    /// GainNp natifs), 286 ClientGetSpell `[s16 spell][s16 req]
    /// [s16 nv][i32 CP]` (SetSpellLevel @0x8156280) puis 611
    /// ClientChangeSpell `[u16 n][u16 ids][u16 n][u16 spells]
    /// [u16 n][s16 levels]` que le natif pousse quand le sort est
    /// dans le livre (char+0x8c vectors +0x570/+0x57c/+0x588) —
    /// sans lui la fenêtre skill du client garde l'ancien niveau.
    /// </summary>
    private static async Task EmitSpellLevelResultAsync(
        NetSession session, ZoneServices s, int charId, short smId,
        SpellLevelResult r, CancellationToken token)
    {
        if (r.PayType == 1)
        {
            var gold = await s.Characters.GetGoldAsync(charId);
            // 280 attr2 = or courant (même émetteur que le shop).
            await session.SendAsync(new CNC_CZ_ClientUpdateAttr
            {
                Field1 = charId, Field2 = 2,
                Field3 = (int)gold, Field4 = 0,
            }, token);
        }
        else
        {
            // attr 0x3ef = skill_point total (désasm GainNp).
            await session.SendAsync(new CNC_CZ_ClientUpdateAttr
            {
                Field1 = charId, Field2 = 0x3ef,
                Field3 = r.CpRemaining, Field4 = 0,
            }, token);
        }
        // 286 ClientGetSpell — corps natif SetSpellLevel :
        // [s16 spellId][s16 spellId requis][s16 nv][i32 CP].
        await session.SendAsync(new CNC_CZ_ClientGetSpell
        {
            Field1 = smId,
            Field2 = smId,
            Field3 = r.NewLevel,
            Field4 = r.CpRemaining,
        }, token);
        await SendChangeSpellAsync(session, s, charId, token);
    }

    /// <summary>611 ClientChangeSpell — les 3 vecteurs parallèles du
    /// livre de sorts (natif char+0x8c : +0x570 ids spellmaster u16,
    /// +0x57c ids de sort u16, +0x588 niveaux s16 — Serialize natif
    /// @0x8157exx : `[u16 n][u16…][u16 n][u16…][u16 n][s16…]`). Dans
    /// notre modèle `player_spells.spellmaster_id` porte l'id utilisé
    /// par le client pour les deux — même paquet que SetSpellLevel
    /// pousse après chaque montée réussie. La classe générée
    /// `CNC_CZ_ClientChangeSpell` est aplatie (6 champs fixes) — corps
    /// construit à la main ici.</summary>
    private static async Task SendChangeSpellAsync(NetSession session,
        ZoneServices s, int charId, CancellationToken token)
    {
        var spells = await s.Classes.GetPlayerSpellsAsync(charId);
        var body = new byte[6 + spells.Count * 6];
        var n = (ushort)spells.Count;
        BitConverter.TryWriteBytes(body.AsSpan(0), n);
        BitConverter.TryWriteBytes(body.AsSpan(2 + n * 2), n);
        BitConverter.TryWriteBytes(body.AsSpan(4 + n * 4), n);
        for (var i = 0; i < spells.Count; i++)
        {
            var (spell, level) = spells[i];
            BitConverter.TryWriteBytes(body.AsSpan(2 + i * 2), spell);
            BitConverter.TryWriteBytes(
                body.AsSpan(2 + n * 2 + 2 + i * 2), spell);
            BitConverter.TryWriteBytes(
                body.AsSpan(4 + n * 4 + 2 + i * 2), level);
        }
        await session.SendPacketAsync(611, body, token);
    }
}
