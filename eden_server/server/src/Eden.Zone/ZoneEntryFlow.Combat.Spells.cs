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
    /// <summary>SpellCastToLife — capture : 279 CastOK
    /// `[u16 spell][u32 0][u32 cible][u8 1]` → 288 Blast
    /// `[i32 slot][u32 cible][u16 spell][i32 power][f32 x][f32 z]
    /// [i32 0][i32 hpLeft][i32 0][u8 0]` → 280 attr0/13/0x21 →
    /// 281 Hit `[u16 spell][u32 mob][u32 char][i32 dmg][i32 1]`.
    /// Les sorts à cast-time passent par CastSequenceAsync (278).</summary>
    private async Task HandleSpellHitAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6 || st.CharId == 0) return;
        var spellId = BitConverter.ToUInt16(payload, 0);
        var handle = BitConverter.ToUInt32(payload, 2);
        if (!st.Migrated || st.PlayerHp <= 0)
        {
            // Jamais de silence : le client pose son verrou de cast à
            // l'envoi du 9 ; sans réponse (fenêtre de TP / mort) plus
            // aucun sort ne partait. 410 libère l'action.
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 9, Field2 = (int)spellId }, token);
            return;
        }
        var castDef = _services.Spells.GetDefinition(spellId);

        // StateNoUseSkill natif (NoUseMagic +0xbc / NoUseKungfu +0xbe
        // sur char+0x8c) — le cast est refusé sous silence/stun ; le
        // natif découpe kungfu/sorts par flag de spell (+0x94&4 vs
        // &0x68), nos debuffs posent les deux → refus global. 410
        // [ncid][spell] libère l'action client comme le refus natif.
        if ((GetStateLocks(st) &
                (StatusLocks.NoSkillMagic | StatusLocks.NoSkillKungfu))
                != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 9, Field2 = (int)spellId }, token);
            return;
        }

        // Down-time du sort (CSpellMaster natif : master+0x14 dernier
        // sort + +0x18/+0x1c expiration ; CheckInDownTime @0x84f3720).
        // Refus natif = ClientCommandFailReturn 410 [i32 ncid][i32
        // spellId] — cf. CNE_CZ_ZoneServerSpellCastToLife::Execute
        // @0x828a1xx quand CastToLife retourne 0.
        if (castDef != null &&
            _services.SpellCooldowns.IsCooling((int)st.CharId, spellId))
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 9, Field2 = (int)spellId }, token);
            return;
        }

        // `ISpell::CastTest` @0x084f04d0 : tout cast tenté sur une
        // monture NON-combat (ride-spells 2140 vides ET char+0x824==0)
        // déclenche RemoveEnchantByState(9) — dismount avant la
        // résolution, le cast se poursuit ensuite.
        await TryAutoDismountForCombatAsync(session, st, token);

        var mob = _services.Live.GetMob(handle);
        // Pet elf (IsA==0x50b) — IsMyEnemy false natif : le cast tombe
        // sur la branche cible-invalide (410) comme un mob mort.
        if (mob is not { Alive: true } || mob.IsElfPet)
        {
            // `CSpellFactory::CastToLife` natif résout TOUT ILifeEntity
            // — pas que les mobs : sorts de soin/buff du cleric visent
            // un joueur (handle = charId, self-cast = propre charId).
            // Sans cette branche ces casts recevaient un 410 sec (ou
            // pire : barre de cast jamais refermée côté client).
            var lifeTarget = ResolveLifeTarget(st, handle);
            if (lifeTarget is not null)
            {
                var ldef = castDef;
                var lifeCastMs = ldef?.CastTimeMs ?? 0;
                if (lifeCastMs > 0)
                {
                    // Cast en cours : le natif rejette la nouvelle
                    // requête — ISpell::OnCast LAB_084ef550 émet un
                    // 279 ClientSpellCastOK avec flag u8=0 (refus).
                    // Un silence laissait le client en attente →
                    // freeze perso constaté au spam d'un sort à cast.
                    if (st.CastingSpell != 0)
                    {
                        await session.SendAsync(new CNC_CZ_ClientSpellCastOK
                        {
                            Field1 = (short)spellId, Field2 = 0,
                            Field3 = (int)handle, Field4 = 0,
                        }, token);
                        return;
                    }
                    st.CastCts?.Cancel();
                    st.CastCts = new CancellationTokenSource();
                    st.CastingSpell = spellId;
                    st.CastingTarget = handle;
                    var seq = ++st.CastSeq;
                    // Awaité — les mutations d'état du cast restent
                    // sérialisées dans la commande de boucle courante.
                    await CastSequenceAsync(session, st, spellId, handle,
                        lifeCastMs, seq, st.CastCts.Token);
                    return;
                }
                if (st.CastingSpell != 0)
                {
                    // Idem pour un sort instantané demandé pendant un
                    // cast : 279 flag=0, le cast en cours continue.
                    await session.SendAsync(new CNC_CZ_ClientSpellCastOK
                    {
                        Field1 = (short)spellId, Field2 = 0,
                        Field3 = (int)handle, Field4 = 0,
                    }, token);
                    return;
                }
                await ExecuteSpellOnLifeAsync(session, st, spellId,
                    handle, lifeTarget, token, skipAck: false);
                return;
            }
            // Cible morte/despawnée : le natif répond 410
            // ClientCommandFailReturn [i32 ncid][i32 0] — SANS réponse
            // le client garde son cast en attente et refuse TOUTE
            // action suivante (plus de cast/attaque/dialogue — le
            // lockout constaté live après 2 combats).
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 9, Field2 = 0 }, token);
            return;
        }

        if (!st.WeaponResolved) await ResolveWeaponAsync(st);
        var def = castDef;

        // Sort offensif sur mob depuis une monture non-combat :
        // RemoveEnchantByState(9) natif — même règle que la mêlée.
        await TryAutoDismountForCombatAsync(session, st, token);

        // --- Portée du sort : AUCUN gate serveur (preuve native) ---
        // `CSpellFactory::CastToLife` @0x81545a0 + `CheckCanCast`
        // @0x81526e0 ne testent JAMAIS la distance — c'est le client qui
        // connaît la portée (S_Spell col28 → SSpellData+0x40) et qui
        // poursuit la cible avant d'envoyer le 9. Le natif exécute le
        // sort à réception. Notre ancien `return` silencieux avalait le
        // cast quand la position serveur avait un paquet de retard sur
        // la position client — en mouvement, le joueur se déplaçait
        // hors de la fenêtre serveur entre le clic et l'arrivée du 9 →
        // le client attendait une réponse qui ne venait jamais (cast
        // "impossible en bougeant" + attente bloquante constatée live).
        // Seule borne défensive restante : une distance absurde (cible
        // visiblement hors du jeu, paquet forgé ou état incohérent) →
        // 410 ClientCommandFailReturn, jamais de silence.
        var dxS = mob.X - st.PosX; var dzS = mob.Y - st.PosZ;
        if (dxS * dxS + dzS * dzS > 3600f) // >60u : incohérent
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 9, Field2 = 0 }, token);
            return;
        }

        var castMs = def?.CastTimeMs ?? 0;
        if (castMs > 0)
        {
            // Cast en cours (natif : le GCD caster+0x660 bloque, le
            // chemin refusé émet 279 flag=0 — LAB_084ef550). Répondre
            // est OBLIGATOIRE : un silence fige le client en attente.
            if (st.CastingSpell != 0)
            {
                await session.SendAsync(new CNC_CZ_ClientSpellCastOK
                {
                    Field1 = (short)spellId, Field2 = 0,
                    Field3 = (int)handle, Field4 = 0,
                }, token);
                return;
            }
            // Cast temporisé : 278 cast-bar, puis effet au bout du délai.
            // Le mouvement ne brise PAS le cast (mécanique cast mobile
            // demandée — le CheckCastBreak natif est désactivé dans
            // HandleMoveSelfAsync) ; seuls 13/mort/nouveau GCD cassent.
            st.CastCts?.Cancel();
            st.CastCts = new CancellationTokenSource();
            st.CastingSpell = spellId;
            st.CastingTarget = handle;
            var seq = ++st.CastSeq;
            // Awaité — les mutations d'état du cast restent
            // sérialisées dans la commande de boucle courante.
            await CastSequenceAsync(session, st, spellId, handle, castMs,
                seq, st.CastCts.Token);
            return;
        }

        // Sort instantané demandé pendant un cast en cours : refus
        // natif 279 flag=0 (même raison — le GCD est actif).
        if (st.CastingSpell != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = (int)handle, Field4 = 0,
            }, token);
            return;
        }

        // Throttle anti-retry : le client renvoie le 9 tant qu'il croit
        // la cible hors portée ; une fois en portée un doublon dans la
        // fenêtre du swing produirait deux résolutions en parallèle.
        if (st.LastSpellId == spellId && st.LastSpellTarget == handle &&
            DateTime.UtcNow - st.LastSpellAt <
                TimeSpan.FromMilliseconds(600))
            return;
        st.LastSpellId = spellId;
        st.LastSpellTarget = handle;
        st.LastSpellAt = DateTime.UtcNow;

        await ExecuteSpellHitAsync(session, st, spellId, mob, token);
    }

    /// <summary>Cast temporisé — ordre natif `ISpell::OnCast`
    /// (désasm 0x84ef270) : `NotifyCastToSelf` = 279
    /// `[u16 spell][i32 castTime décisecondes][u32 cible][u8 1]` —
    /// le i32 porte le cast time (GetCastTime, min 4) qui fait afficher
    /// la barre de cast par le client ; puis `NotifyCastToOthers` = 278
    /// `[u16 spell][u32 caster][u32 cible][u8][f32 x][f32 z]` broadcast ;
    /// puis `CEventSpellCasting` attend le délai avant le blast.
    /// Envoyer le 279 APRÈS le délai laissait le client sans barre
    /// pendant castMs = latence perçue de 1-2 s.</summary>
    private async Task CastSequenceAsync(NetSession session, EntryState st,
        ushort spellId, uint target, int castMs, int seq,
        CancellationToken token)
    {
        try
        {
            // 279 CastOK — i32 = cast time en décisecondes (natif
            // NotifyCastToSelf envoie GetCastTime(SSpellData+0x38)).
            // 279 CastOK — i32 = cast time en décisecondes (natif
            // NotifyCastToSelf envoie GetCastTime(SSpellData+0x38)).
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = castMs / 100,
                Field3 = (int)target, Field4 = 1,
            }, token);
            // 278 = NotifyCastToOthers natif → BroadCastToCanSee
            // (entity, cmd, false) : le lanceur ne reçoit JAMAIS son
            // propre 278 (la boucle natif saute param_1). Envoyé au
            // lanceur avec st.PosX/Z (position serveur en retard quand
            // il bouge), le client ré-ancrait le perso → micro-stop
            // au lancement d'un cast en déplacement (bug live).
            // Field4 = 1 — NotifyCastToOthers natif écrit
            // *(u8)(cmd+0x10) = 1 (désasm @0x084ee500).
            await BroadcastToNodePeersAsync(session, st,
                new CNC_CZ_ClientSpellCast
                {
                    Field1 = (short)spellId, Field2 = (int)st.CharId,
                    Field3 = (int)target, Field4 = 1,
                    Field5 = st.PosX, Field6 = st.PosZ,
                });
            // Flag « casting » — `ISpell::OnCast` natif fait
            // `LifeEntityDo(caster, 0x0d)` qui pose le bit 0x2000 puis
            // `NodifyFlagChange` émet le 302 = mot d'état COMPLET
            // (entity+0x3a) : sel = bit18 « en bataille » =
            // st.InCombat à l'émission — JAMAIS lié à l'ouverture de
            // chaîne (un sel=0 en combat affiche « leaving combat »
            // chez le client — bug live au cast sur un 2ᵉ mob).
            // chainStart ne décide que l'allocation d'un nouvel act.
            var chainStart = !st.ActionChain;
            if (chainStart)
            {
                st.AttackAct = st.NextAction();
                st.ActionChain = true;
            }
            await SendFlagAsync(session, st.CharId, 0x2000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct,
                token);
        }
        catch
        {
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            return;
        }
        // La résolution à l'échéance du cast est schedulée sur la
        // boucle de zone (CEventSpellCasting natif) — mutations sur le
        // fil unique, pas de tâche Task.Delay fire-and-forget.
        ScheduleOnLoop(TimeSpan.FromMilliseconds(castMs),
            t => ResolveCastAsync(session, st, spellId, target, seq, t),
            token);
    }

    /// <summary>Résolution d'un cast arrivé à terme — l'ancien code
    /// après Task.Delay(castMs). Annulation (mouvement/nouveau cast) :
    /// le travail schedulé vérifie le token avant de toucher l'état.</summary>
    private async Task ResolveCastAsync(NetSession session, EntryState st,
        ushort spellId, uint target, int seq, CancellationToken token)
    {
        // Release du flag « casting » dans TOUS les cas — c'est la
        // sortie d'état qui libère le personnage côté client.
        // Convention capture (idem NowhereCastFinishAsync) : en combat
        // `0000 sel=4` avec l'act de la chaîne (fin du pulse, la chaîne
        // reste ouverte) ; hors combat `0000 sel=0` ferme la chaîne.
        // Émettre `0000 sel=0` avec un act NEUF en plein fight — la
        // signature exacte de « sortie de combat » — faisait osciller
        // l'état du client à chaque cast (freeze constaté live).
        try
        {
            if (st.InCombat)
                await SendFlagAsync(session, st.CharId, 0x0000,
                    SEL_COMBAT, 1, st.AttackAct, CancellationToken.None);
            else
            {
                await SendFlagAsync(session, st.CharId, 0x0000, 0, 1,
                    st.AttackAct, CancellationToken.None);
                st.ActionChain = false;
            }
        }
        catch { }
        if (token.IsCancellationRequested)
        {
            // Cast brisé : BreakCastAsync a déjà purgé l'état et émis
            // le 287. Ne toucher à CastingSpell que si cette séquence
            // est encore courante — sinon on écraserait le cast qui
            // nous a remplacés (course au spam skill).
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            return;
        }
        try
        {
            // Ne purger l'état que si CE cast est encore courant —
            // un cast plus récent a pu s'installer pendant le délai.
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
            // Cast remplacé par un plus récent pendant le délai : ne pas
            // résoudre — le natif n'exécute qu'un ISpell à la fois
            // (caster+0x94). Sans ce gate, deux blasts partaient.
            if (st.CastSeq != seq) return;
            // Mort ou démigré pendant le wind-up : BreakCastAsync (via
            // PlayerDeathAsync) a déjà annulé le token et émis le 287 —
            // cette garde couvre tout chemin restant, jamais de blast
            // post-mortem.
            if (st.PlayerHp <= 0 || st.Dead || !st.Migrated) return;
            // La résolution tourne sous CancellationToken.None : un spam
            // de 9 annule `st.CastCts` pour le PROCHAIN cast — si le
            // token partagé tuait la résolution en vol, le client restait
            // avec sa barre de cast ouverte indéfiniment (freeze
            // "cast-time infini" constaté sur les skills cleric).
            var mob = _services.Live.GetMob(target);
            if (mob is { Alive: true, IsElfPet: false })
            {
                await ExecuteSpellHitAsync(session, st, spellId, mob,
                    CancellationToken.None, skipAck: true);
            }
            else if (ResolveLifeTarget(st, target) is { } lt)
            {
                await ExecuteSpellOnLifeAsync(session, st, spellId,
                    target, lt, CancellationToken.None, skipAck: true);
            }
            else
            {
                // Cible disparue pendant le cast : le natif échoue la
                // commande — le 410 referme la barre côté client.
                await session.SendPacketAsync(410, Concat(
                    BitConverter.GetBytes(9), BitConverter.GetBytes(0)),
                    CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Cast brisé : BreakCastAsync a déjà purgé l'état et émis
            // le 287. Ne toucher à CastingSpell que si cette séquence
            // est encore courante — sinon on écraserait le cast qui
            // nous a remplacés (course au spam skill).
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
        }
        catch
        {
            // Filet de sécurité : une exception en plein cast ne doit
            // pas laisser CastingSpell posé — sinon toute requête de
            // sort suivante prendrait le refus 279 flag=0 indéfiniment
            // (perso figé).
            if (st.CastSeq == seq)
            {
                st.CastingSpell = 0;
                st.CastingTarget = 0;
            }
        }
    }

    /// <summary>Résout un handle de cible "life" non-mob : soi-même
    /// (charId du caster) ou un autre joueur migré sur la même zone.
    /// Le handle joueur côté client = le charId (champ +0x34 de
    /// IControlObj natif). Retourne l'EntryState de la cible.</summary>
    private EntryState? ResolveLifeTarget(EntryState st, uint handle)
    {
        if (handle == 0 || handle == st.CharId) return st;
        foreach (var s in _states.Values)
            if (s.Migrated && s.CharId == handle && s.PlayerHp > 0)
                return s;
        return null;
    }

    /// <summary>Résolution d'un sort sur cible joueur (soin/buff
    /// cleric). Miroir de la branche non-hostile de CastToLife : le
    /// natif applique les commandes de l'enchant de résultat sur la
    /// cible puis émet `CNC_CZ_ClientEffectReport` (292, désasm
    /// `CEC_Heal::Execute` @0x8535xxx) au client + broadcast.
    /// Corps 292 natif (Serialize @0x81e69xx) :
    /// `[i32 caster][i32 cible][s16 enchantId][s16 0][s16 n][n×i32][i32 flags]`.
    /// `CCharFormula::HPCombo` @0x83fe420 : heal = args[type-1 clampé 4]
    /// × (rand%…+100-combo)/100 — combo=0 → valeur exacte.</summary>
    private async Task ExecuteSpellOnLifeAsync(NetSession session,
        EntryState st, ushort spellId, uint targetHandle,
        EntryState tst, CancellationToken token, bool skipAck)
    {
        var def = _services.Spells.GetDefinition(spellId);

        // Coût MP identique au chemin hostile (S_Spell col32 →
        // SSpellData+0xbc × coef palier col1 — CostMp @0x83f44d0).
        var mpCost = def is null
            ? 0 : SpellMpCost(def, await PlayerSpellLevelAsync(st, spellId),
                flat: (int)PlayerSnapshot(st).BonusMpCostFlat);
        if (mpCost > 0)
        {
            if (st.PlayerMp < mpCost)
            {
                await session.SendPacketAsync(410, Concat(
                    BitConverter.GetBytes(9), BitConverter.GetBytes(0)),
                    token);
                return;
            }
            // Déduction native au blast (ISpell::OnBlast @0x084f0be0
            // → 328 ModifyAttr) — partie dans LifeSpellReleaseAsync :
            // un 328 émis au cast tombait dans la 1ʳᵉ frame de
            // l'anim et faisait reset l'état d'action côté client.
        }

        // Down-time à la résolution réussie — mêmes lignes que le
        // chemin hostile (PutInDownTime(SSpellData) natif + 368).
        if (def is { CooldownDs: > 0 })
        {
            _services.SpellCooldowns.Arm((int)st.CharId, spellId,
                EffectiveCooldownDs(st, spellId, def.CooldownDs));
            await _services.SpellCooldowns.SendUpdateAsync(session,
                (int)st.CharId, _services.ItemCooldowns, token);
        }

        if (!skipAck)
        {
            // 279 CastOK instantané (même corps que la branche mob).
            await session.SendAsync(new CNC_CZ_ClientSpellCastOK
            {
                Field1 = (short)spellId, Field2 = 0,
                Field3 = (int)targetHandle, Field4 = 1,
            }, token);
            // 278 NotifyCastToOthers — émis par OnCast pour tout cast
            // réussi (instantané compris) ; flag u8=1 natif.
            await BroadcastToNodePeersAsync(session, st,
                new CNC_CZ_ClientSpellCast
                {
                    Field1 = (short)spellId, Field2 = (int)st.CharId,
                    Field3 = (int)targetHandle, Field4 = 1,
                    Field5 = st.PosX, Field6 = st.PosZ,
                });
        }

        // 302 flag « casting » (bit 0x2000) — LifeEntityDo(caster,
        // 0x0d) natif de ISpell::OnCast @0x084ef270 : inconditionnel,
        // y compris pour les casts INSTANTANÉS sur cible life — sans
        // lui le client n'entre jamais en état d'action → aucune
        // animation des sorts magiques instantanés (bug live). Même
        // convention que le chemin mob : sel = stance combat réelle.
        var lifeChainStart = !st.ActionChain;
        if (lifeChainStart)
        {
            st.AttackAct = st.NextAction();
            st.ActionChain = true;
        }
        await SendFlagAsync(session, st.CharId, 0x2000,
            st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct,
            token);

        // 288 Blast sur la cible joueur — l'anim d'impact du sort
        // (handle joueur = charId). Position = celle de la cible.
        _blastSerial = (_blastSerial + 11) & 0x7fffffff;
        var serial = (uint)_blastSerial;
        var blastId = BlastWireId(st, def);
        await session.SendAsync(new CNC_CZ_ClientSpellBlastOK
        {
            Serial = serial,
            VictimHandle = (int)targetHandle,
            SpellId = unchecked((short)spellId),
            Power = def?.Power ?? 20,
            X = tst.PosX, Y = tst.PosZ, Z = 0f,
            BlastId = blastId,
        }, token);
        // 289 ClientSpellBlastToLife aux observateurs —
        // NotifyBlastToOthers natif @0x084f7e40 (même corps que la
        // branche mob : [serial][spell][hLanceur][srcXYZ][hCible]
        // [dstXYZ][blastId][0]).
        var blastToLife = new CNC_CZ_ClientSpellBlastToLife
        {
            Field1 = serial, Field2 = unchecked((short)spellId),
            Field3 = (int)st.CharId,
            Field4 = st.PosX, Field5 = st.PosZ, Field6 = 0f,
            Field7 = (int)targetHandle,
            Field8 = tst.PosX, Field9 = tst.PosZ, Field10 = 0f,
            Field11 = blastId, Field12 = 0,
        };
        // NotifyBlastToOthers natif @0x084f7e40 : CanSee(lanceur) +
        // CanSee(cible) puis NotifyClient(cible) quand c'est un joueur
        // (target+4 ≠ 0 && +0x24 == 0). La cible life est TOUJOURS un
        // joueur → sa session reçoit le blast (self-cast = le lanceur,
        // fidèle au natif ; autre joueur → SA session, pas la nôtre).
        var blastTarget = targetHandle == st.CharId ? session
            : _services.Sessions.FindByCharacter((int)targetHandle);
        if (blastTarget != null)
            await blastTarget.SendAsync(blastToLife, token);
        await BroadcastToNodePeersAsync(session, st, blastToLife);

        // Application native au blast (ISpell::OnBlast → EffectToLife) :
        // TOUT l'effet (MP 328, soin 281/280, commandes 292, enchant
        // 299 + refresh 296/492/493) part au temps `hit` de la motion
        // — JAMAIS au cast. Les ~15 paquets émis dans la 1ʳᵉ frame de
        // l'anim faisaient reset l'état d'action côté client → anim
        // coupée dès son départ (bug live). Voir
        // ApplyLifeSpellBlastAsync + LifeSpellReleaseAsync.
        // Release de l'action + application des effets — schedulés au
        // temps `hit` de la motion (~600 ms, cadence CEventSpellBlasting
        // natif, cf. chemin mob) : le natif applique MP/heal/enchant au
        // BLAST, pas au cast — le burst immédiat coupait l'anim.
        ScheduleOnLoop(TimeSpan.FromMilliseconds(600), _ =>
            LifeSpellReleaseAsync(session, st, tst, spellId,
                targetHandle, def, mpCost));
    }

    /// <summary>Release du flag d'action d'un life-spell au temps
    /// `hit` de la motion — phase finale de
    /// <see cref="ExecuteSpellOnLifeAsync"/> schedulée sur la boucle.
    /// Ordre natif OnBlast : 302 `0000` (RemoveDo 0x0d) → coût MP (328)
    /// → effets (281/280, 292, 299 + refresh attr).</summary>
    private async Task LifeSpellReleaseAsync(NetSession session,
        EntryState st, EntryState tst, ushort spellId,
        uint targetHandle, SpellParser.SpellDef? def, int mpCost)
    {
        if (!st.Migrated) return;
        if (st.AttackAct == 0) st.AttackAct = st.NextAction();
        // sel=4 (stance combat) UNIQUEMENT si le joueur est réellement
        // en combat — un soin/buff allié ne doit jamais afficher la
        // stance « combat contre soi-même » (bug live Cure).
        await SendFlagAsync(session, st.CharId, 0x0000,
            st.InCombat ? SEL_COMBAT : (byte)0, 1, st.AttackAct,
            CancellationToken.None);
        // La chaîne d'action survit au soin en combat (act constant
        // jusqu'à la sortie — même règle que les casts offensifs) ;
        // hors combat seul le reset a un effet.
        if (!st.InCombat) st.ActionChain = false;
        // Déduction MP native au blast (ISpell::OnBlast @0x084f0be0 —
        // `char+0x70 -= CostMp` puis 328 ModifyAttr) — le CHECK reste
        // au cast (MP insuffisante → 410 avant tout paquet).
        if (mpCost > 0)
            await ApplyMpCostAsync(session, st, mpCost);
        var healed = await ApplyLifeSpellBlastAsync(session, st, tst,
            spellId, targetHandle, def);
        if (healed == 0 && st.Statuses.Count == 0)
            EdenLog.Write($"[Zone] life-spell {spellId} cible=" +
                $"{targetHandle} résolu sans effet HP");
    }

    /// <summary>Effets d'un life-spell appliqués au temps `hit` du
    /// blast (blocs extraits de <see cref="ExecuteSpellOnLifeAsync"/>) :
    /// soin HitRestoreSkill (281 + vitals), commandes de l'enchant de
    /// résultat (heal instantané 2033/2034 → 292 + 280, MP) puis
    /// enchant persistant (299 + refresh stats). Retourne les HP
    /// soignés pour le log.</summary>
    private async Task<int> ApplyLifeSpellBlastAsync(NetSession session,
        EntryState st, EntryState tst, ushort spellId,
        uint targetHandle, SpellParser.SpellDef? def)
    {
        var healed = 0;
        // --- Soin natif (HitRestoreSkill → CalculateMagicSkillHeal) ---
        // Type col18 ∈ {4,5} avec power col36 : `heal = power ×
        // refHeal%(col4)/100 × (100+2r−var)/100`, roll crit magique
        // (affichage VERT côté client via le flag 0x200 du 281).
        if (def is { HealPower: > 0 } &&
            NativeCombatFormulas.SpellHitKindOf(def) is
                SpellHitKind.Restore)
        {
            var spLvl = await PlayerSpellLevelAsync(st, spellId);
            var hres = NativeCombatFormulas.ResolveSpellHeal(
                PlayerSnapshot(st), PlayerSnapshot(tst), def,
                SpellRefCoef(def, spLvl), CombatParams(), Random.Shared);
            var real = Math.Min(hres.Amount,
                Math.Max(0, tst.PlayerMaxHp - tst.PlayerHp));
            if (real > 0) { tst.PlayerHp += real; healed += real; }
            var hFlags = SpellHitFlags.Restore |
                (hres.Critical && hres.Amount > 0
                    ? SpellHitFlags.Crit : 0);
            // 281 au lanceur + à la cible (si autre joueur) + broadcast
            // can-see — même séquence que le natif (NotifyClient cible +
            // BroadCastToCanSee).
            await SendSpellHitAsync(session, spellId, targetHandle,
                st.CharId, hres.Amount, hFlags, CancellationToken.None);
            var targetSession = targetHandle != st.CharId
                ? _services.Sessions.FindByCharacter((int)targetHandle)
                : null;
            if (targetSession is { } tsn)
                await SendSpellHitAsync(tsn, spellId, targetHandle,
                    st.CharId, hres.Amount, hFlags, CancellationToken.None);
            var (hitOp, hitPayload) = PreparePacket(
                new CNC_CZ_ClientSpellHitToLife
                {
                    Field1 = (short)spellId,      // s16 : id sort
                    Field2 = (int)targetHandle,   // i32 : soigné
                    Field3 = (int)st.CharId,      // i32 : soigneur
                    Field4 = hres.Amount,         // i32 : soin
                    Field5 = hFlags,              // i32 : 0x200|crit
                });
            foreach (var s in _services.Sessions.All)
            {
                if (s.SessionId == session.SessionId ||
                    s.SessionId == targetSession?.SessionId) continue;
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    !ost.Migrated || ost.NodeId != st.NodeId ||
                    ost.InstanceGroup != st.InstanceGroup) continue;
                await SendPreparedToAsync(s, hitOp, hitPayload,
                    CancellationToken.None);
            }
            // Vitals : le lanceur voit les HP de sa cible ; la cible
            // voit les siennes si c'est un autre joueur.
            await SendVitalHpAsync(session, targetHandle,
                tst.PlayerHp, tst.PlayerMaxHp, CancellationToken.None);
            if (targetSession is { } tsv)
                await SendVitalHpAsync(tsv, targetHandle,
                    tst.PlayerHp, tst.PlayerMaxHp, CancellationToken.None);
        }

        // Commandes de l'enchant de résultat sur le JOUEUR cible :
        // HP min max (1001/3002) → +HP borné au max, puis 292
        // EffectReport + 280 HP. 2033/2034 = DamageAddPlus (tag dont
        // Execute est un no-op @0x08520d30), PAS des soins.
        if (def is { ResultSpellId: > 0 })
        {
            foreach (var c in _services.Enchants.GetUseCommands(
                def.ResultSpellId))
            {
                var name = EnchantCommands.Names.TryGetValue(c.CommandId,
                    out var n) ? n : $"#{c.CommandId}";
                // int locaux — ces commandes (charge/knockback, heals
                // instantanés) ne consomment que des entiers.
                var iargs = c.Args.Select(v => (int)v).ToArray();
                switch (name)
                {
                    // CEC_Heal : args[idx élément] × combo% — sans
                    // champ élément résolu on prend args[0] (famille 1).
                    case "HP":
                    {
                        var amount = iargs.Length > 0 && iargs[0] > 0
                            ? iargs[0]
                            : (def.Power > 0 ? def.Power : 20);
                        var missing = tst.PlayerMaxHp - tst.PlayerHp;
                        var real = Math.Min(amount, Math.Max(0, missing));
                        if (real <= 0) break;
                        tst.PlayerHp += real;
                        healed += real;
                        // 292 ClientEffectReport — voir l'en-tête de
                        // méthode pour le corps natif exact.
                        await session.SendAsync(new CNC_CZ_ClientEffectReport
                        {
                            Field1 = (int)st.CharId,              // i32 : caster
                            Field2 = (int)targetHandle,           // i32 : cible
                            Field3 = (short)def.ResultSpellId,    // s16 : enchant
                            Field4 = 0,                           // s16 : 0 natif
                            Field5 = 1,                           // s16 : n=1 valeur
                            Field6 = real,                        // i32 : soin
                            Field7 = 1,                           // i32 : flags (heal>0)
                        }, CancellationToken.None);
                        // Si la cible est un autre joueur, sa session
                        // reçoit le report + sa nouvelle HP.
                        if (targetHandle != st.CharId &&
                            _services.Sessions.FindByCharacter(
                                (int)targetHandle) is { } ts)
                        {
                            // `tst` EST l'EntryState de cette session.
                            await ts.SendAsync(new CNC_CZ_ClientEffectReport
                            {
                                Field1 = (int)st.CharId,
                                Field2 = (int)targetHandle,
                                Field3 = (short)def.ResultSpellId,
                                Field4 = 0, Field5 = 1,
                                Field6 = real, Field7 = 1,
                            }, CancellationToken.None);
                            await SendVitalHpAsync(ts, targetHandle,
                                tst.PlayerHp, tst.PlayerMaxHp,
                                CancellationToken.None);
                        }
                        // 280 vital HP au caster (self-heal) / vu par lui.
                        await SendVitalHpAsync(session, targetHandle,
                            tst.PlayerHp, tst.PlayerMaxHp,
                            CancellationToken.None);
                        break;
                    }
                    // MP restore (CEC heal variante MP — mode 1).
                    case "MP":
                    {
                        var amount = iargs.Length > 0 && iargs[0] > 0
                            ? iargs[0] : 20;
                        var missing = tst.PlayerMaxMp - tst.PlayerMp;
                        var real = Math.Min(amount, Math.Max(0, missing));
                        if (real <= 0) break;
                        tst.PlayerMp += real;
                        await SendVitalMpAsync(session, targetHandle,
                            tst.PlayerMp, tst.PlayerMaxMp,
                            CancellationToken.None);
                        break;
                    }
                    default:
                        EdenLog.Write($"[Zone] life-spell cmd " +
                            $"enchant={def.ResultSpellId} {name} " +
                            $"args=[{string.Join(',', c.Args)}]");
                        break;
                }
            }
        }

        // Enchant persistant (buff/HoT) : S_Spell c41 → SSpellData+0x84
        // = durée en secondes lue par CCharFormula::SpellDuration
        // @0x83f9310 (cellule vide → -9). Gate EffectToLife natif
        // @0x8060ec0 : `dur != -1 && (effet+0x12c != 0 || dur == -9)`
        // → AddEnchant. -9 (« durée propre de l'effet ») crée donc
        // bien l'enchant — les commandes s'exécutent — mais
        // ILifeEntity::AddEnchant @0x847ea90 SUPPRIME le 299 quand
        // `param_3 == -9` (pas d'icône, stats actives via le statut).
        // -1 = exécution instantanée seule (buckets +0x20/+0x2c,
        // déjà couverte par la boucle de commandes ci-dessus).
        // c40 = chance % de déclenchement.
        if (def is { ResultSpellId: > 0 } && def.Value != -1 &&
            (def.ResultSpellChance <= 0 ||
                Random.Shared.Next(100) < def.ResultSpellChance))
        {
            var statusLevel = await PlayerSpellLevelAsync(st, spellId);
            // Le statut vit sur l'EntryState de la CIBLE — son 299 et
            // son tick passent par sa propre session (self → même).
            var statusSession = targetHandle != st.CharId
                ? _services.Sessions.FindByCharacter((int)targetHandle)
                    ?? session
                : session;
            await AddPlayerEnchantAsync(statusSession, tst,
                (int)st.CharId, def.ResultSpellId,
                def.Value + GetSpellAttrModify(st, 9, spellId),
                statusLevel, CancellationToken.None,
                hidden: def.Value == -9);
        }
        return healed;
    }

    /// <summary>Rupture de cast — appelé depuis MoveSelf.
    /// Désasm `CPlayerFactory::CheckCastBreak` @0x80f5790 : cast en
    /// cours (caster+0x94 ≠ 0) → break de l'ISpell (vtable+0x14,
    /// reason 0) → `ISpell::NotifyBreak` @0x84ee5d0 émet
    /// `CNC_CZ_ClientSpellBreak` (287) au client + BroadCastToCanSee.
    /// Corps 287 natif (Serialize @0x81e97f0) : `[i32 caster]
    /// [s16 spellId][u8 reason]` = 7 octets, reason=0 partout ici.
    /// (Un ancien envoi 287 à 5B `[i32][u8]` crashait le client —
    /// désync de flux, pas le bon paquet.) Le 408
    /// ClientUseItemTimeBreak ne concerne que la canalisation d'item
    /// (caster+0x90), pas le cast de sort.</summary>
    private async Task BreakCastAsync(NetSession session, EntryState st,
        CancellationToken token)
    {
        if (st.CastingSpell == 0) return;
        var spell = st.CastingSpell;
        st.CastCts?.Cancel();
        st.CastingSpell = 0;
        st.CastingTarget = 0;
        // Release du flag « casting » (bit 0x2000) — le RemoveDo
        // natif retire l'état 0x0d et NodifyFlagChange émet le 302.
        // Même convention que ResolveCastAsync : sel=4 en combat
        // (chaîne ouverte), sel=0 + fermeture sinon.
        try
        {
            if (st.InCombat)
                await SendFlagAsync(session, st.CharId, 0x0000,
                    SEL_COMBAT, 1, st.AttackAct, token);
            else
            {
                await SendFlagAsync(session, st.CharId, 0x0000, 0, 1,
                    st.AttackAct, token);
                st.ActionChain = false;
            }
        }
        catch { }
        var brk = new CNC_CZ_ClientSpellBreak
        {
            Field1 = (int)st.CharId,                    // i32 : caster
            Field2 = (short)spell,                      // s16 : sort cassé
            Field3 = 0,                                 // u8 : reason (0)
        };
        await session.SendAsync(brk, token);
        // BroadCastToCanSee natif : les témoins voient aussi le break.
        var (brkOp, brkPayload) = PreparePacket(brk);
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                // CanSee natif : le break ne concerne que ceux qui
                // ont le caster en vue.
                !ost.Sent.ContainsKey((uint)st.CharId)) continue;
            await SendPreparedToAsync(s, brkOp, brkPayload, token);
        }
    }









}
