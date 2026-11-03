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
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Partiel combat — boucle des combats de mobs :
/// engagement/fin de combat, tick du registre sur la boucle de zone,
/// sorts de mob, mort du mob (broadcast, drops délégués à
/// Combat.Rewards), événements de groupe, respawn/corpse, mort et
/// rez du joueur.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Snapshot combat d'un mob — `FromMonster` sur la
    /// définition S_Monster si le spawn l'a retenue ; sinon repli sur
    /// les champs live (même squelette : IsMonster + level + attaque —
    /// variance/ratings à 0).</summary>
    private static CombatantSnapshot MobSnapshot(ZoneLiveWorld.LiveMob m)
        => m.Def != null
            ? CombatantSnapshot.FromMonster(m.Def)
            : new CombatantSnapshot
            {
                IsMonster = true,
                Level = Math.Max(m.Level, 1),
                MonsterAttack = m.AttackPower,
            };
    /// <summary>495 ClientPlayMonsterAction : `[u32 handle][s16 kfId]` —
    /// broadcast natif de CAI::SetEventAction (désasm @0x84d5120 :
    /// operator_new 0x14, champ+4=handle, champ+8=s16 action, puis
    /// BroadCastToCanSee). Joue une motion KF sur le modèle de l'entité
    /// côté client. Émis ici avec 0x3f7=1015 (hurt05 sur les modèles
    /// m0xx) quand le mob encaisse un coup — appel natif
    /// `SetEventAction(ai,4,0x3f7,0,0,true)` dans CCharacter::AttackResult
    /// (@0x8416030).</summary>
    private async Task BroadcastMonsterActionAsync(uint handle,
        int nodeId, int inst, short actionKfId)
    {
        var (op, payload) = PreparePacket(
            new CNC_CZ_ClientPlayMonsterAction
            { Field1 = (int)handle, Field2 = actionKfId });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Sent.ContainsKey(handle))
                continue;
            await SendPreparedToAsync(s, op, payload,
                CancellationToken.None);
        }
    }
    /// <summary>271 <c>CNC_CZ_ClientDisplayMonster</c> — corps natif
    /// EXACT 10B <c>[i32 mob][u8 nbEnchants=0][u8 ownerGen][i32 owner]</c>
    /// (Serialize @0x81e96d0, émis par <c>CAI::ChangeOwner</c> @0x84d3930
    /// via BroadCastToCanSee — le switch de cible AI du mob).
    /// Capture zsniff_native_entry.bin : <c>NCID=271 len=12</c>.
    /// Ne PAS confondre avec 444 ClientGetSelectLifeTarget (corps 9B,
    /// len=11) — les deux existent dans la séquence d'aggro native.
    /// CORRECTIF : la trame a un temps été émise à 9B — le u8
    /// génération manquant désynchronisait le stream RC4 (freeze
    /// client). Émis via la classe générée
    /// <see cref="CNC_CZ_ClientDisplayMonster"/>.
    /// </summary>
    private async Task BroadcastDisplayMonsterAsync(uint mobHandle,
        int nodeId, int inst, uint targetCharId,
        int ownerGeneration)
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientDisplayMonster
        {
            MobHandle = mobHandle,          // u32 : handle mob
            EnchantIds = [],                // u8 count + s16×0
            OwnerGeneration = (byte)ownerGeneration,
            OwnerHandle = (int)targetCharId,// i32 : owner
        });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != nodeId ||
                ost.InstanceGroup != inst ||
                // CanSee natif : le switch de cible ne concerne que
                // les watchers qui ont le mob en vue.
                !ost.Sent.ContainsKey(mobHandle))
                continue;
            await SendPreparedToAsync(s, op, payload,
                CancellationToken.None);
        }
    }
    /// <summary>Aggro du mob — capture : `0000 sel=4 op=0 val=1`.</summary>
    private Task SendMobAggroAsync(NetSession session,
        ZoneLiveWorld.LiveMob mob, CancellationToken token) =>
        SendFlagAsync(session, mob.Handle, 0x0000, SEL_COMBAT, 0,
            mob.NextAction(), token);

    /// <summary>Riposte du mob — capture : `0000 sel=14 op=0 val=N`
    /// à chaque action (windup, hit) — nouvel id à chaque fois.</summary>
    private Task SendMobAttackPhaseAsync(NetSession session,
        ZoneLiveWorld.LiveMob mob, CancellationToken token) =>
        SendFlagAsync(session, mob.Handle, 0x0000, SEL_PHASE, 0,
            mob.NextAction(), token);

    /// <summary>MeleeAttack [i32 handle][u8 type] — réponse capture native
    /// (zsniff 17:22:58 → 17:23:17, client→serveur `0500 <mob> 00`) :
    ///   par requête : `272 [char][mob][dmg][type][attackKfId]` immédiat ;
    ///   au 1er engagement : `267 mob→pos` + `444 [mob][0][0]` +
    ///   `271 [mob][0][1][char]` + `302 mob aggro(0000 sel4 op0)` +
    ///   `302 char engagé(0020 sel4 op1)` + `280 attr0/attr13` +
    ///   `272(-1)` + `444 lock [mob][0][char]` + `302 char (0000 sel4)` +
    ///   `328`. type=2/handle=0 = stop → `302 0000 sel4|sel0 op1`
    ///   (mot d'état complet : bit18 si encore en combat, base sinon).
    /// Le client pilote l'auto-attaque ET la poursuite : il renvoie 5 à
    /// chaque tick de swing — si la cible est hors de portée le natif
    /// (CPlayerFactory::MeleeAttack → CCharacter::CanAttack @0x813f2c0,
    /// code retour 0x333) ne répond AUCUN coup : il ne fait que
    /// StateInBattle(1) et laisse le client courir jusqu'à portée. Un 272
    /// émis à distance cassait la poursuite : anim jouée sur place à 50u.
    /// Le kfId du field5 dépend de l'arme équipée (S_Item col8 → pool
    /// CCharKfm::Integration) — le hardcode 1421 ne valait que pour l'arc.
    /// </summary>
    private async Task HandleMeleeAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 5 || st.CharId == 0 || !st.Migrated) return;
        var handle = BitConverter.ToUInt32(payload, 0);
        var type = payload[4];

        // Arrêt d'attaque demandé par le client. Le 302 transporte le
        // mot d'état COMPLET (entity+0x3a dans NodifyFlagChange @0x84e3c00)
        // : sel=4 = bit18 « en bataille » encore posé si des mobs nous
        // ciblent, sel=0 = mot réduit à la base quand plus rien ne nous
        // cible. JAMAIS sel=1 : le bit16 est un état du FSM que le
        // client ne purge pas tout seul → personnage + caméra gelés
        // jusqu'à la déco (bug live constaté à l'arrêt du swing).
        if (handle == 0 || type == 2)
        {
            st.AutoAttacking = false;
            st.ActionChain = false;
            // Cast en vol : le client EE arrête son swing d'auto-attaque
            // pendant la barre de cast, ce qui envoie ce type=2. Le mob
            // aggro dès le blast — une sortie de combat complète ici
            // produisait le clignotement "quitté combat / en combat" à
            // chaque skill (bug live). La bannière ne part que quand
            // rien ne nous cible ET qu'aucun cast n'est en cours.
            var stillTargeted = _services.Live.MobsOf(st.NodeId, st.InstanceGroup)
                .Any(m => m.Alive && m.InCombat &&
                    m.TargetCharId == (int)st.CharId);
            if (!stillTargeted && st.CastingSpell == 0)
                st.InCombat = false;
            // État courant complet : bit18 posé tant qu'on reste en
            // combat (mobs encore sur nous), sinon mot de base op=1.
            await SendFlagAsync(session, st.CharId, 0x0000,
                st.InCombat ? SEL_COMBAT : (byte)0, 1,
                st.NextAction(), token);
            // 444 clear [u32 0][u8 0][u32 0] — capture 17:23:02.490.
            await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget(), token);
            return;
        }
        if (st.PlayerHp <= 0) return;
        // StateNoAttack natif (cnt +0xba > 0) — attaque interdite
        // pendant stun/lien ; 410 débloque l'action client.
        if ((GetStateLocks(st) & StatusLocks.NoAttack) != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 5, Field2 = 0 }, token);
            return;
        }

        var mob = _services.Live.GetMob(handle);
        // Pet elf (CElfPet natif, IsA==0x50b) — `IsMyEnemy` retourne
        // toujours false (0x0840e490) : jamais une cible d'attaque.
        // Idem pour le cristal de bastion BLEU (allied, ai=164 +
        // sorts embarqués) : cible des légions, pas des joueurs.
        if (mob is { IsElfPet: true } ||
            mob?.Def?.IsAlliedCrystal == true)
        {
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 5, Field2 = 0 }, token);
            return;
        }
        if (mob is not { Alive: true })
        {
            // Cible joueur (handle = charId) — PvP même node ; sinon
            // cible morte/despawnée → 410 [i32 ncid][i32 0] pour que le
            // client libère son action en attente (sinon lockout).
            if (!await TryMeleePlayerAsync(session, st, handle, token))
                await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
                { Field1 = 5, Field2 = 0 }, token);
            return;
        }

        // Monture non-combat : engager un mob fait descendre le perso
        // — RemoveEnchantByState(9) natif @0x84f08c3.
        await TryAutoDismountForCombatAsync(session, st, token);

        // --- Portée d'attaque (CCharacter::CanAttack natif) ---
        // Hors portée : aucun 272 — le client EE poursuit la cible tout
        // seul (il connaît la portée via ses .ini) et renvoie le 5 au
        // prochain tick. On marque juste l'état de combat.
        if (!st.WeaponResolved) await ResolveWeaponAsync(st);
        var dxR = mob.X - st.PosX; var dzR = mob.Y - st.PosZ;
        if (dxR * dxR + dzR * dzR > st.AttackRange * st.AttackRange)
        {
            if (!st.InCombat)
            {
                st.InCombat = true;
                st.AutoAttacking = true;
                // Stance combat seule (StateInBattle) — le client affiche
                // l'engagement sans jouer de swing à distance.
                await SendFlagAsync(session, st.CharId, 0x0000,
                    SEL_COMBAT, 1, st.NextAction(), token);
            }
            return;
        }

        var first = !mob.InCombat ||
            mob.TargetCharId != (int)st.CharId;
        st.AutoAttacking = true;
        st.InCombat = true;

        // Pipeline natif `CCharFormula::CalculatePhysicalDamage` —
        // roll hit (rand%100 < rate, miss → type8 dmg0) → roll crit
        // (rand%10000 → type2, dégât ×PCritDmg% avec réduction par la
        // défense adverse) → dégât avec variance d'arme, offset de
        // niveau ±10 et résist élémentaire (désasm 0x83fd8c0/caller
        // 0x83c6xxx).
        var pres = NativeCombatFormulas.ResolvePhysicalAttack(
            PlayerSnapshot(st), MobSnapshot(mob), CombatParams(),
            Random.Shared);
        var dmg = pres.Damage;
        // type = mot-drapeau natif (1|2 hit/crit + 0x10 block +
        // 0x100 parry) ; type=3 = premier coup (bits de défense
        // conservés), type=8 = miss.
        var hitType = pres.Outcome == PhysicalOutcome.Miss ? 8
            : first ? 3 | (pres.Flags & ~3)
            : pres.Flags;
        mob.Hp = Math.Max(0, mob.Hp - dmg);
        mob.LastHitAt = DateTime.UtcNow;
        if (pres.Outcome != PhysicalOutcome.Miss)
        {
            await RegisterBpDamageAsync(session, st, mob, null, dmg);
            await TryInterruptMobCastAsync(mob);
        }

        if (pres.Outcome == PhysicalOutcome.Miss)
        {
            // Miss : 272 type8 dmg0 (l'anim de swing joue, le mob ne
            // reçoit pas de hurt et ne meurt pas — l'aggro reste
            // posée par l'engagement déjà émis ci-dessus).
            await SendMeleeAsync(session, st.CharId, mob.Handle, 0, 8,
                NextPlayerAttackKf(st), token);
            return;
        }

        if (mob.Hp <= 0)
        {
            // Coup fatal — capture 17:23:11.475→11.776 : le swing normal
            // `272 type=1..3 kfId dmg` part TOUJOURS d'abord (c'est lui
            // qui joue l'anim d'attaque), puis le twin fatal
            // `272 type=5 -1` n'arrive qu'à +300 ms avec le 305.
            // Sauter le swing = mob qui meurt sans anim (bug constaté).
            await SendMeleeAsync(session, st.CharId, mob.Handle, dmg,
                hitType, NextPlayerAttackKf(st), token);
            await BroadcastMonsterActionAsync(mob.Handle, st.NodeId, mob.InstanceKey,
                0x3f7);
            await KillMobAsync(session, st, mob, token, 0, dmg);
            return;
        }

        // Immédiat — capture premier contact (17:22:58.527) :
        //   272 [char][mob][dmg][type][attackKfId] — field5 = kfId de la
        //   motion d'attaque (1421 = attack12, GetAttackID), -1 réservé
        //   au twin ; puis 267 (le mob rue) + 444 [mob][0][0] (engage
        //   uniquement). Coups suivants : 272 seul. AUCUN 302 char en
        //   mêlée pure — le swing est client-side ; poser flag[0]=0x2000
        //   coinçait le state controller (plus d'anims, plus de casts)
        //   — bug corrigé.
        var kfId = NextPlayerAttackKf(st);
        await SendMeleeAsync(session, st.CharId, mob.Handle, dmg,
            hitType, kfId, token);
        // Procs d'équipement « attaque » — AttackTrigger natif
        // @0x80f5710 → OnAttackEnchant : roll col42 puis commandes
        // des slots col40 (MultiStroke, buffs…). Si un proc achève
        // le mob on s'arrête (le KillMobAsync du proc a déjà émis
        // la séquence de mort).
        await RunItemProcsAsync(session, st, mob, ProcHitEvent.Attack,
            dmg, token, hitType);
        if (!mob.Alive) return;
        // Réaction "hurt" du mob — natif CCharacter::AttackResult émet
        // 495 [mob][0x3f7] (SetEventAction kf hurt) à chaque coup reçu.
        await BroadcastMonsterActionAsync(mob.Handle, st.NodeId, mob.InstanceKey, 0x3f7);
        // Délai du coup porté = CEventDelayDamage natif : temps `hit`
        // de la motion d'attaque choisie (CKfm::GetMotionTime
        // @0x080e0b00) + vol du projectile pour les armes à distance
        // (dist/DefaultFlySpeed=40 u/s — kinds arc/fusil/canon).
        var hitDelayMs = KfHitDelayMs(st, kfId) +
            (IsRangedWeaponKind(st.WeaponKind)
                ? MeleeFlyDelayMs(st, mob) : 0);
        if (first)
        {
            BeginMobCombat(st, mob);
            // 302 char (0000 sel4 op1) — stance combat du joueur.
            // Capture d'engagement : `302 char engagé` + `302 char
            // (0000 sel4)`. C'est CE flag qui met le client en mode
            // combat — sans lui l'auto-attaque ne changeait jamais
            // l'état UI (bug live). flag[0] reste 0x0000 : poser
            // 0x2000 laissait le flag « action en cours » coincé
            // (plus d'anims, plus d'interactions).
            await SendFlagAsync(session, st.CharId, 0x0000,
                SEL_COMBAT, 1, st.NextAction(), token);
            await session.SendAsync(new CNC_CZ_ClientGoto
            {
                Field1 = (int)mob.Handle, Field2 = st.PosX,
                Field3 = st.PosZ, Field4 = 0x6a,
            }, token);
            // 326 MovePrediction — lance la marche visible du mob vers
            // le joueur (capture : le 326 suit immédiatement le 267).
            await SendMovePredictionAsync(session, mob.Handle,
                mob.X, mob.Y, st.PosX, st.PosZ, token);
            await session.SendAsync(new CNC_CZ_ClientGetSelectLifeTarget
            {
                Field1 = (int)mob.Handle, Field2 = 0, Field3 = 0,
            }, token);
            ScheduleOnLoop(TimeSpan.FromMilliseconds(hitDelayMs),
                _ => MeleeEngageDelayedAsync(session, st, mob, dmg,
                    mob.CombatGeneration));
        }
        else
            ScheduleOnLoop(TimeSpan.FromMilliseconds(hitDelayMs),
                _ => MeleeHitDelayedAsync(session, st, mob, dmg, 1,
                    mob.CombatGeneration));
    }
    /// <summary>Transition vers l'état combat du mob — pose les champs
    /// au premier contact : la "home" d'aggro native (`CAI+0x44/0x48`)
    /// = position du mob quand le combat COMMENCE (désasm AI msg 0x6a
    /// @0x84d7xxx : si home == spawn, home = pos courante) — c'est
    /// l'ancre du leash, pas le point de spawn.</summary>
    private static void BeginMobCombat(EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        mob.BeginCombat((int)st.CharId, mob.X, mob.Y);
        // Participant (récompense courrier des mutants).
        lock (mob.Attackers) mob.Attackers.Add((int)st.CharId);
    }
    /// <summary>Vrai si le joueur a quitté l'aire de garde du mob —
    /// `CAI::CheckOutOfGuardArea` natif (@0x84d4b20) : distance
    /// cible↔home-d'aggro > max_hunt_range (50u, voir <see
    /// cref="GuardRangeSq"/>). Le check est sauté sur les maps de
    /// type 4+ (instance — `entity+0x324→+0x12 == 4`) sauf pour les
    /// mobs élite/boss (MonsterType ≥ 2, S_Monster col6) qui
    /// conservent leur leash partout — y compris en donjon.</summary>
    private bool OutOfGuardArea(EntryState st, ZoneLiveWorld.LiveMob mob)
    {
        var nodeType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;
        var isInstance = nodeType >= 4;
        var elite = mob.Def?.IsEliteOrBoss == true;
        if (isInstance && !elite) return false;   // jamais de leash ici
        var hx = st.PosX - mob.AggroX;
        var hy = st.PosZ - mob.AggroY;
        return hx * hx + hy * hy > GuardRangeSq;
    }
    /// <summary>Boucle d'aggro : phases d'attaque (302 field 0x14) +
    /// 272 mob→joueur type8 + 280 HP joueur toutes les ~1.6 s.
    /// Le mob avance (267 + positions serveur) si hors de portée (~6u).
    /// Déaggro : leash distance joueur↔ancre-d'aggro 50u — partout
    /// pour les élites/boss, et sur map normale pour les trash ; en
    /// instance (node type≥4) les non-élites ne déaggro que s'ils
    /// n'ont pas pu toucher pendant 3 min. À la sortie : reset d'état
    /// du mob (302 sel=0 + 444) puis retour à la home de spawn +
    /// régénération complète (CAIFSMReturn natif).</summary>
    /// <summary>Combat actif piloté par la boucle de zone — remplace
    /// la tâche par mob à Task.Delay(100). Toutes les mutations de
    /// poursuite (mob.X/Y, st.EntityPos, st.PlayerHp, HP du mob)
    /// s'exécutent désormais sur le fil unique de <see cref="ZoneLoop"/>,
    /// sérialisées avec les autres mutations de combat.</summary>
    private sealed class ActiveMobCombat
    {
        public required NetSession Session { get; init; }
        public required EntryState St { get; init; }
        public required ZoneLiveWorld.LiveMob Mob { get; init; }
        public required int Generation { get; init; }
        public required bool InstanceNoLeash { get; init; }
    }
    private readonly ConcurrentDictionary<uint, ActiveMobCombat>
        _mobCombats = new();

    private void StartMobCombatLoop(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        var generation = mob.TryStartCombatLoop((int)st.CharId);
        if (!generation.HasValue) return;
        // Règles d'abandon d'aggro (prompt live + CheckOutOfGuardArea) :
        //  · instance (type≥4) NON-élite : JAMAIS de leash — le mob
        //    poursuit sans limite de distance ; il rend l'aggro
        //    uniquement s'il n'a pas pu toucher le joueur pendant
        //    plus de 3 min (LastDamageDealtAt).
        //  · partout ailleurs (map normale) ET pour tous les élites/
        //    boss quel que soit le lieu : leash distance seul —
        //    le mob rend l'aggro si le joueur s'éloigne de plus de
        //    50u de l'ancre d'aggro, puis retourne à son spawn.
        //    AUCUN timeout sur map normale : un mob qui rate ses
        //    coups continue d'attaquer indéfiniment (avant, le
        //    timeout de 10 s rendait l'aggro en plein combat →
        //    "leaving combat" pendant que le mob tapait — bug live).
        _mobCombats[mob.Handle] = new ActiveMobCombat
        {
            Session = session, St = st, Mob = mob,
            Generation = generation.Value,
            InstanceNoLeash =
                (_services.GameData.Nodes
                    .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1)
                    >= 4 && mob.Def?.IsEliteOrBoss != true,
        };
    }
    /// <summary>Un pas de poursuite/riposte par mob en combat — appelé
    /// par <see cref="ZoneLoop"/> à chaque tick (100 ms, la même cadence
    /// que l'ancien Task.Delay par mob) puis l'épilogue pour les
    /// combats terminés. Fil unique : le registre n'est muté que par
    /// StartMobCombatLoop (thread session) et ici.</summary>
    public async Task TickMobCombatsAsync()
    {
        foreach (var (handle, c) in _mobCombats)
        {
            var current = c.Mob.IsCombatLoopCurrent(c.Generation,
                (int)c.St.CharId) && c.St.Migrated && c.St.PlayerHp > 0;
            var keep = current && await MobCombatTickAsync(c);
            if (!keep && _mobCombats.TryRemove(handle, out var done))
                await FinishMobCombatAsync(done);
        }
    }
    /// <summary>Corps de l'ancien while(100ms) — un tick de
    /// chase/riposte. Retourne false quand le combat doit sortir de la
    /// boucle (deaggro, cible morte, session morte).</summary>
    private async Task<bool> MobCombatTickAsync(ActiveMobCombat c)
    {
        var session = c.Session; var st = c.St; var mob = c.Mob;
        var generation = c.Generation;
        try
        {
            // Stun (effet de sort à ResultSpellId, ex. Soul Leap) :
            // le mob reste aggro mais n'avance ni ne frappe. Même
            // blocage via les verrous 2055 du registre d'enchants
            // (NoMove empêche la marche, NoAttack la frappe).
            if (DateTime.UtcNow < mob.StunnedUntil) return true;
            // Verrou d'action d'événement (Event_MonsterAction —
            // CAI::SetEventAction : IA figée pendant « Time »).
            if (mob.EventActionLocked(DateTime.UtcNow)) return true;
            var moveLocked = mob.MoveLocked;
            // Cadence de riposte — la marche est à chaque tick ; la
            // frappe n'arrive que quand le cooldown est écoulé
            // (AttackSpeed S_Monster col17 ×100 ms, défaut ~1,6 s).
            var atkMs = mob.Def is { AttackSpeed: > 0 }
                ? mob.Def.AttackSpeed * 100 : 1600;
            var dx = mob.X - st.PosX; var dy = mob.Y - st.PosZ;
            var d2 = dx * dx + dy * dy;
            if (c.InstanceNoLeash
                    ? DateTime.UtcNow - mob.LastDamageDealtAt >
                        TimeSpan.FromMinutes(3)
                    : OutOfGuardArea(st, mob))
                return false;

            // Wind-up d'un sort temporisé en cours : le mob reste en
            // place sans frapper ni approcher jusqu'au blast (un stun
            // posé entre-temps annule au moment du blast — AttackLocked
            // est vérifié dans MobCastBlastAsync).
            if (DateTime.UtcNow < mob.CastingUntil)
                return mob.IsCombatLoopCurrent(generation,
                    (int)st.CharId);

            // --- Sorts du mob (S_Monster cols 40-49 → SpellIds
            // résolus contre S_Spell au chargement) — le natif
            // alterne auto-attaque et sorts selon son FSM
            // (CAIFSMCombat). ~35 % par tick d'attaque ; un sort à
            // distance peut être lancé sans approcher.
            var canRiposte = DateTime.UtcNow >= mob.NextRiposteAt &&
                !mob.AttackLocked;
            // NB : `_services.Monsters` (enrichi ResolveRefs), pas
            // `Live.MonsterDefs` — les defs brutes du parser ont
            // `SpellIds` vide, les mobs ne castaient jamais.
            if (canRiposte &&
                _services.Monsters.GetDefinition(mob.MobId) is { } mdef &&
                mdef.SpellIds.Count > 0 &&
                Random.Shared.Next(100) < 35)
            {
                var spellId = mdef.SpellIds[
                    Random.Shared.Next(mdef.SpellIds.Count)];
                var sdef = _services.Spells.GetDefinition(spellId);
                var sRange = sdef is { Range: > 0 }
                    ? sdef.Range * 1.5f + 4f : 25f;
                if (d2 <= sRange * sRange)
                {
                    mob.NextRiposteAt = DateTime.UtcNow +
                        TimeSpan.FromMilliseconds(atkMs);
                    await MobCastSpellAsync(session, st, mob, sdef,
                        generation, CancellationToken.None);
                    return mob.IsCombatLoopCurrent(generation,
                        (int)st.CharId);
                }
            }

            // Entités « décor » (S_Monster atk≤1 / sans portée / ai=164 —
            // coffres, œufs, cristaux de bastion, cibles de quête) :
            // JAMAIS d'approche, de riposte ni de sort — elles restent
            // attaquables mais ne font rien (les mobs-objets n'ont pas
            // d'état FSM de déplacement chez le natif).
            if (_services.Live.MonsterDefs
                    .TryGetValue(mob.MobId, out var sdef2)
                    && sdef2.IsStructure)
                return true;

            if (d2 > 36 && !moveLocked) // >6u : approche
            {
                // ~0,72 u par tick de 100 ms ≈ 7,2 u/s — la vitesse
                // annoncée par le 326 (0x4725 ≈ 7,16 u/s). Un step
                // plus lent faisait sur-interpoler le client puis
                // corriger à chaque tick = poursuite saccadée ; le
                // mob ne rattrapait jamais un joueur en course.
                var d = Math.Sqrt(d2);
                var step = Math.Min(0.72f, (float)d);
                mob.X -= (float)(dx / d) * step;
                mob.Y -= (float)(dy / d) * step;
                st.EntityPos[mob.Handle] = (mob.X, mob.Y);
                // 326 MovePrediction broadcast au node — le flux de
                // marche natif BroadCastToCanSee : position courante
                // + destination = le client joue l'anim de course
                // du mob pour TOUS les témoins (avant : la cible
                // seule le voyait bouger, les autres un mob figé).
                await BroadcastMobMoveAsync(st, mob.Handle,
                    mob.X, mob.Y, st.PosX, st.PosZ,
                    CancellationToken.None);
                return mob.IsCombatLoopCurrent(generation,
                    (int)st.CharId);
            }

            // Hors de portée et immobilisé : rester aggro sans bouger.
            if (d2 > 36) return true;

            // En portée mais cooldown pas écoulé — continuer le
            // suivi sans frapper (le mob "colle" le joueur).
            if (!canRiposte) return true;

            // Cycle de riposte du mob : phase 302, attrs puis 272.
            // Le 292/52022 de l'ancienne implémentation appliquait
            // inconditionnellement Perforation (T_Enchant: DoT/MP loss)
            // à chaque mob. Le natif ne l'envoie que pour un vrai
            // enchant actif; les procs de mob ne sont pas encore résolus.
            mob.NextRiposteAt = DateTime.UtcNow +
                TimeSpan.FromMilliseconds(atkMs);
            if (!mob.IsCombatLoopCurrent(generation, (int)st.CharId))
                return false;
            await SendMobAttackPhaseAsync(session, mob,
                CancellationToken.None);
            if (!mob.IsCombatLoopCurrent(generation, (int)st.CharId))
                return false;

            // Riposte du mob — `CalculatePhysicalDamage(CMonster)`
            // @0x83fdec0 : base col14, variance col15, offset de
            // niveau Δ×5 [0,500], résists du joueur vs élément
            // col13 ; miss → type8 dmg0, crit → type2.
            var mres = NativeCombatFormulas.ResolvePhysicalAttack(
                MobSnapshot(mob), PlayerSnapshot(st),
                CombatParams(), Random.Shared);
            if (!mob.IsCombatLoopCurrent(generation, (int)st.CharId))
                return false;
            var dmg = mres.Damage;
            var mobType = mres.Flags;
            // Invincibilité de migration/réapparition (char+0x3c
            // bit7 natif) : le mob ne peut ni toucher ni
            // engager — le coup est annulé côté serveur.
            var invincible = st.GodMode ||
                DateTime.UtcNow < st.MigrationInvincibleUntil;
            if (!invincible) st.PlayerHp -= dmg;
            else { /* god-mode/invincible — le coup n'inflige rien */ }
            // Coup réellement porté — horodatage pour la règle
            // d'aggro instance (3 min sans toucher → deaggro).
            if (dmg > 0) mob.LastDamageDealtAt = DateTime.UtcNow;
            await SendAttrAsync(session, mob.Handle, 0x21,
                0x2bc, 0, CancellationToken.None);
            if (!mob.IsCombatLoopCurrent(generation, (int)st.CharId))
                return false;
            await SendAttrAsync(session, st.CharId, 0,
                Math.Max(0, st.PlayerHp), Math.Max(0, st.PlayerHp),
                CancellationToken.None);
            // PAS d'attr 0x13 sur le joueur — dans la capture
            // native 0x13 n'arrive que sur les handles de mobs
            // (jamais sur 02c2eb0b). Envoyé au joueur, il le fige
            // côté client : plus de mouvement ni d'animations.
            await SendMobAttackPhaseAsync(session, mob,
                CancellationToken.None);
            if (!mob.IsCombatLoopCurrent(generation, (int)st.CharId))
                return false;
            // 272 mob→joueur — capture 17:23:01.480 : field5 =
            // kfId d'attaque du mob (fe03 = 1022 = attack02 de m025),
            // puis twin field5=-1 ~300 ms plus tard. Le kfId fait
            // jouer l'anim d'attaque du modèle au client.
            await SendMeleeAsync(session, mob.Handle, st.CharId,
                dmg, mobType,
                mob.NextAttackKfId(), CancellationToken.None);
            var tdmg = dmg;
            var mobRef = mob;
            ScheduleOnLoop(TimeSpan.FromMilliseconds(300), async _ =>
            {
                // Le twin appartient à cette génération d'aggro :
                // après mort/deaggro/re-ciblage il ne doit plus
                // réactiver l'état de combat client.
                if (!mobRef.IsCombatLoopCurrent(generation,
                        (int)st.CharId))
                    return;
                try { await SendMeleeAsync(session, mobRef.Handle,
                    st.CharId, tdmg, 1, -1, CancellationToken.None); }
                catch { }
            });
            await SendVitalHpAsync(session, st.CharId, Math.Max(0, st.PlayerHp), st.PlayerMaxHp,
                CancellationToken.None);
            // Procs d'équipement « coup reçu » — OnBeAttackEnchant
            // natif (events S_Enchant 3/4 : buff self / debuff
            // attaquant journalisé).
            await RunItemProcsAsync(session, st, mob,
                ProcHitEvent.Hurt, dmg, CancellationToken.None);
            // Procs de l'ATTAQUANT — OnAttackEnchant sur le registre
            // du mob (events 1/2 : self=mob, opponent=joueur).
            await RunMobAttackProcsAsync(session, st, mob, dmg,
                CancellationToken.None);

            if (st.PlayerHp <= 0)
            {
                // Mort du joueur — séquence CCharacter::Die native.
                await PlayerDeathAsync(session, st, mob,
                    CancellationToken.None);
                return false;
            }
            return true;
        }
        catch { /* session morte ou déconnectée */ return false; }
    }
    /// <summary>Épilogue de fin de combat — l'ancien code après le
    /// while : reset d'état du mob, sortie de combat du joueur,
    /// retour au spawn. Exécuté sur la boucle quand le combat sort du
    /// registre.</summary>
    private async Task FinishMobCombatAsync(ActiveMobCombat c)
    {
        var session = c.Session; var st = c.St; var mob = c.Mob;
        var released = mob.FinishCombatLoop(c.Generation);

        // Reset d'état du mob côté clients — le mob sort de son action
        // (LifeEntityRemoveDo natif : l'état "combat/return" est levé à
        // la fin du FSM). Sans ça le client gardait le mob flaggé
        // "en action" → impossible de le re-sélectionner au curseur
        // alors qu'il continue à frapper (bug live : clic muet, TAB ok).
        // `302 [mob] 0000 sel=0 op=0` — mot d'état vidé (les mobs
        // n'ont pas la base op=1 : leurs captures n'émettent que
        // sel=4/sel=14 op=0, jamais op=1) + `444 [mob][0][0]` délock —
        // broadcast à tous ceux qui le voient.
        if (mob.Alive)
        {
            var clr = new CNC_CZ_ClientFlagModify
            {
                Handle = mob.Handle,                    // u32 : handle
                Flags = 0,                              // u16 : flags16
                Selector = 0, Operation = 0,
                ActionId = mob.NextAction(),            // sel=0 op=0 act
            };
            var (clrOp, clrPayload) = PreparePacket(clr);
            // 444 délock : [i32 mob][u8 0][i32 0]
            var (delockOp, delockPayload) = PreparePacket(
                new CNC_CZ_ClientGetSelectLifeTarget
                {
                    Field1 = (int)mob.Handle, Field2 = 0, Field3 = 0,
                });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    ost.NodeId != mob.NodeId ||
                    ost.InstanceGroup != mob.InstanceKey ||
                    !ost.Sent.ContainsKey(mob.Handle))
                    continue;
                await SendPreparedToAsync(s, clrOp, clrPayload,
                    CancellationToken.None);
                await SendPreparedToAsync(s, delockOp, delockPayload,
                    CancellationToken.None);
            }
        }

        // Sortie de combat si plus aucun mob n'aggro ce joueur.
        if (st.InCombat && st.Session != null)
        {
            var still = _services.Live.MobsOf(st.NodeId, st.InstanceGroup)
                .Any(m => m.Alive && m.InCombat &&
                    m.TargetCharId == (int)st.CharId);
            if (!still)
            {
                st.InCombat = false;
                st.AutoAttacking = false;
                st.ActionChain = false;
                try
                {
                    await SendCombatExitAsync(st.Session, st.CharId,
                        st.NextAction(), CancellationToken.None);
                    await st.Session.SendAsync(
                        new CNC_CZ_ClientGetSelectLifeTarget(),
                        CancellationToken.None);
                }
                catch { }
            }
        }

        // Retour au spawn + régénération (CAIFSMReturn natif : le mob
        // relâché — deaggro OU cible morte — rentre à sa home de SPAWN
        // et soigne à plein). Les sorts de stun ne bloquent pas le
        // retour. Sans ça le mob restait figé là où le combat a cessé.
        if (released && mob.Alive)
            ScheduleOnLoop(TimeSpan.Zero,
                _ => ReturnHomeStepAsync(session, st, mob, 0));
    }
    /// <summary>Retour du mob à son point de spawn (CAIFSMReturn) :
    /// pas de ~6u/tick avec 326 MovePrediction jusqu'à la home
    /// (spawn — PAS l'ancre d'aggro : le retour vise le spawn natif),
    /// puis restauration complète des HP + broadcast attr 0.</summary>
    private async Task ReturnHomeStepAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int step)
    {
        try
        {
            // Re-aggro pendant le retour : BeginMobCombat a reposé
            // InCombat → cette marche est obsolète. L'avorter sinon
            // la boucle de combat ET le retour déplacent le mob en
            // parallèle → position client/serveur désynchronisée →
            // mob visible mais inclickable (bug live).
            if (!mob.Alive || mob.InCombat || step >= 40)
            {
                if (!mob.Alive || mob.InCombat) return;
            }
            else
            {
                var hx = mob.HomeX - mob.X;
                var hy = mob.HomeY - mob.Y;
                var d = Math.Sqrt(hx * hx + hy * hy);
                if (d > 1.0)
                {
                    var stp = Math.Min(6f, (float)d);
                    mob.X += (float)(hx / d) * stp;
                    mob.Y += (float)(hy / d) * stp;
                    st.EntityPos[mob.Handle] = (mob.X, mob.Y);
                    await SendMovePredictionAsync(session, mob.Handle,
                        mob.X, mob.Y, mob.HomeX, mob.HomeY,
                        CancellationToken.None);
                    // Pas suivant dans 1600 ms — schedulé sur la
                    // boucle (l'ancienne marche était un Task.Delay
                    // par mob hors du fil de jeu).
                    ScheduleOnLoop(TimeSpan.FromMilliseconds(1600),
                        _ => ReturnHomeStepAsync(session, st, mob,
                            step + 1));
                    return;
                }
            }
            mob.X = mob.HomeX; mob.Y = mob.HomeY;
            st.EntityPos[mob.Handle] = (mob.X, mob.Y);
            // Régénération complète à l'arrivée (règle native du retour).
            if (mob.Hp < mob.MaxHp)
            {
                mob.Hp = mob.MaxHp;
                await SendAttrAsync(session, mob.Handle, 0, mob.Hp,
                    mob.Hp, CancellationToken.None);
            }
            // CAIFSMReturn::Update @0x084dd1b0 : patience remise au max
            // (+ rupture effacée) et rediffusée (attr 0x13).
            await ResetMobPatienceAsync(mob);
        }
        catch { /* session morte */ }
    }
    /// <summary>Cast d'un sort par le mob sur le joueur : geste 495
    /// (SetEventAction natif — S_Spell idx3 référence les motions
    /// magic* du KFM) + phase 302 ; sort temporisé (col26) → wind-up
    /// <see cref="ZoneLiveWorld.LiveMob.CastingUntil"/> puis
    /// <see cref="MobCastBlastAsync"/> (289 Blast + dégâts → 281 Hit)
    /// à l'échéance, instant sinon. Pas de 279 au client victime —
    /// NotifyCastToSelf natif ne vise que la session du lanceur.</summary>
    private async Task MobCastSpellAsync(NetSession session, EntryState st,
        ZoneLiveWorld.LiveMob mob, SpellParser.SpellDef? sdef,
        int generation, CancellationToken token)
    {
        if (sdef == null || !mob.Alive || st.PlayerHp <= 0) return;
        var spellId = (ushort)sdef.Id;

        // Geste de cast du modèle (495 — broadcast CanSee natif) —
        // kf résolu : -1/0 ne jouent rien, on saute l'émission.
        // Chemin MOB : CMonsterKfm::GetBlastID @0x080dfed0 envoie la
        // col3 BRUTE (+0xa0, +5 si l'entité a un transform +0x7c) —
        // pas de résolution de pool comme CCharKfm.
        var mobBlastKf = sdef.BlastId;
        if (mobBlastKf > 0)
            await BroadcastMonsterActionAsync(mob.Handle, st.NodeId, mob.InstanceKey,
                (short)mobBlastKf);
        // Phase d'action du mob (302 sel=0x14 — même pulsation que les
        // auto-attaques : le client enchaîne l'action sur son act).
        await SendMobAttackPhaseAsync(session, mob, token);

        // PAS de 279 vers la victime — NotifyCastToSelf natif notifie
        // la session du LANCEUR (caster+0x24) ; un mob n'a pas de
        // session. L'ancien envoi faisait croire au client qu'il
        // castait (CastingSpell posé → ses propres sorts refusés,
        // anim de cast jouée sur le joueur).

        // Cast temporisé (S_Spell col26) : le mob reste en place
        // pendant le wind-up (CastingUntil gating dans le tick), le
        // blast part à l'échéance si l'engagement est encore courant
        // et que le mob n'a pas été stun entre-temps (annulation —
        // CEventSpellCasting natif + mort/stun → pas de blast).
        var castMs = sdef.CastTimeMs;
        if (castMs > 0)
        {
            // « Compétence chronométrée » des boss (plan 12, config).
            if (mob.Kind != Eden.Core.Game.WorldBoss.BossKind.None)
                await WarnBossCastAsync(mob, sdef);
            mob.CastingUntil = DateTime.UtcNow.AddMilliseconds(castMs);
            mob.CastingSpellId = spellId;
            // Numéro du cast : un coup du joueur peut le briser (2 %/coup
            // — GetHurt @0x0849ad40), le blast schedulé ne part alors pas.
            var castSerial = ++mob.CastSerial;
            ScheduleOnLoop(TimeSpan.FromMilliseconds(castMs), _ =>
                MobCastBlastAsync(session, st, mob, spellId, sdef,
                    mobBlastKf, generation, castSerial));
            return;
        }
        await MobCastBlastAsync(session, st, mob, spellId, sdef,
            mobBlastKf, generation);
    }

    /// <summary>Blast d'un sort de mob — phase 289+dégâts de
    /// <see cref="MobCastSpellAsync"/>, immédiate (castMs==0) ou
    /// schedulée à l'échéance du wind-up. Annulée si l'engagement a
    /// changé (mort/deaggro/retarget → génération) ou si le mob est
    /// stun/lock-attack entre-temps.</summary>
    private async Task MobCastBlastAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, ushort spellId,
        SpellParser.SpellDef? sdef, int mobBlastKf, int generation,
        int castSerial = -1)
    {
        var token = CancellationToken.None;
        // Cast interrompu (SpellBreak) ou remplacé depuis le schedule.
        if (castSerial >= 0 && mob.CastSerial != castSerial) return;
        mob.CastingSpellId = 0;
        if (sdef == null || st.PlayerHp <= 0 ||
            !mob.IsCombatLoopCurrent(generation, (int)st.CharId) ||
            mob.AttackLocked) return;

        // Sort de mob → le natif n'émet PAS de 288 BlastOK au joueur
        // (NotifyBlastToSelf notifie this+0x24 = le mob lanceur, sans
        // session). Seul le 289 ClientSpellBlastToLife part aux
        // observateurs : NotifyBlastToOthers @0x084f7e40 =
        // CanSee(lanceur)+CanSee(cible) — le joueur victime voit le
        // mob → il reçoit le 289 via le premier broadcast.
        // Corps (Serialize @0x081e8870) : [u32 serial][s16 spell]
        // [i32 hLanceur][f32 srcXYZ][i32 hCible][f32 dstXYZ]
        // [i32 blastId][i32 0].
        _blastSerial = (_blastSerial + 11) & 0x7fffffff;
        var blastToLife = new CNC_CZ_ClientSpellBlastToLife
        {
            Field1 = (uint)_blastSerial,
            Field2 = unchecked((short)spellId),
            Field3 = (int)mob.Handle,
            Field4 = mob.X, Field5 = mob.Y, Field6 = mob.Z,
            Field7 = (int)st.CharId,
            Field8 = st.PosX, Field9 = st.PosZ, Field10 = 0f,
            Field11 = mobBlastKf, Field12 = 0,
        };
        await session.SendAsync(blastToLife, token);
        await BroadcastToNodePeersAsync(session, st, blastToLife);

        // Dégâts du sort — pipeline natif `HitMagicSkill`/
        // `HitPhysicalSkill` : base M-ATK/mêlée + power col35×coef
        // (S_SpellReference palier 1 pour les mobs — col2 physique /
        // col3 magique), variance col37, résist élémentaire du joueur,
        // roll hit/crit (rand%10000, crit magique ×MCritDmg%).
        var coef = SpellRefCoef(sdef, 1);
        var sres = NativeCombatFormulas.ResolveSpell(
            MobSnapshot(mob), PlayerSnapshot(st), sdef, coef,
            CombatParams(), Random.Shared);
        var dmg = st.GodMode ||
            DateTime.UtcNow < st.MigrationInvincibleUntil
            ? 0 : sres.Damage;
        st.PlayerHp = Math.Max(0, st.PlayerHp - dmg);
        // Coup porté — horodatage pour le timeout d'aggro instance.
        if (dmg > 0) mob.LastDamageDealtAt = DateTime.UtcNow;
        // Fenêtre ~300 ms du CEventDelayDamage natif entre le twin
        // de cast et le coup (capture 09:59:13.136→13.454) — schedulée
        // sur la boucle : un Task.Delay ici suspendrait le fil de jeu.
        ScheduleOnLoop(TimeSpan.FromMilliseconds(300), _ =>
            MobCastSpellHitAsync(session, st, mob, spellId, sdef, sres,
                dmg));
    }
    /// <summary>Impact du sort mob à +300 ms — phase 2 de
    /// <see cref="MobCastSpellAsync"/> schedulée sur la boucle.
    /// Les dégâts sont déjà appliqués ; cette phase émet attrs + 281,
    /// applique l'enchant de debuff du sort (ResultSpellId — stun/
    /// DoT/slow des sorts de mobs, effect→AddEnchant natif sur le
    /// joueur) et résout la mort éventuelle du joueur.</summary>
    private async Task MobCastSpellHitAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int spellId,
        SpellParser.SpellDef? sdef, PhysicalResult sres, int dmg)
    {
        var token = CancellationToken.None;
        if (!mob.Alive || !st.Migrated) return;
        await SendAttrAsync(session, st.CharId, 0,
            st.PlayerHp, st.PlayerHp, token);
        await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
            st.PlayerMaxHp, token);
        // 281 Hit — flags natifs dynamiques (miss=8, crit=|0x200000).
        await SendSpellHitAsync(session, spellId, st.CharId, mob.Handle,
            dmg, NativeCombatFormulas.SpellHitFlagsOf(sres), token);

        // Debuff du sort (S_Spell c39 ResultSpellId, chance c40,
        // durée c41 Value) — gate EffectToLife natif : `dur != -1`
        // (-9 = durée propre de l'effet → enchant posé sans icône
        // 299) ; un miss n'applique rien. Caster = handle du mob.
        if (sdef is { ResultSpellId: > 0 } && sdef.Value != -1 &&
            sres.Outcome != PhysicalOutcome.Miss && st.PlayerHp > 0 &&
            (sdef.ResultSpellChance <= 0 ||
                Random.Shared.Next(100) < sdef.ResultSpellChance))
        {
            await AddPlayerEnchantAsync(session, st,
                unchecked((int)mob.Handle), sdef.ResultSpellId,
                sdef.Value, 1, token, hidden: sdef.Value == -9);
        }

        // Procs de l'attaquant (registre enchant du mob — events 1/2,
        // miss exclu : c'est OnMissEnchant côté natif).
        if (sres.Outcome != PhysicalOutcome.Miss)
            await RunMobAttackProcsAsync(session, st, mob, dmg, token,
                NativeCombatFormulas.SpellHitFlagsOf(sres));

        if (st.PlayerHp <= 0)
            await PlayerDeathAsync(session, st, mob, token);
    }
}
