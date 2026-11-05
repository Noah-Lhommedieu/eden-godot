using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Combat.SpellEffects;

namespace Eden.Zone;

/// <summary>Registre d'enchants sur mobs — miroir du `CEnchantMgr` que
/// tout `ILifeEntity` porte côté natif (AddEnchant @0x847ea90 : le 299
/// n'est émis que si l'icône est visible, BroadCastToCanSee aux
/// témoins ; la capture de mort mob montre un `300` par enchant actif
/// avant le 305).
///
/// Différences avec la voie joueur (`Status.cs`) : pas de persistance
/// DB, pas d'équipe/ride/pets, ticks pouvant TUER la cible (le DoT
/// mob doit pouvoir vider la barre — attribution via CasterId).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Extrait les effets périodiques/verrous d'un enchant —
    /// même décodage que la voie joueur : 3001 args[2]&gt;0 = régén/s,
    /// 3002 args[0] signé = DoT, 2055 vars = verrous d'état
    /// (bit0 NoMove, bit1 NoAttack pour un mob).</summary>
    private void FillMobEnchantFields(ZoneLiveWorld.LiveMob.MobEnchant e,
        int eid)
    {
        e.HpPerTick = 0;
        e.LockFlags = 0;
        foreach (var c in _services.Enchants.GetUseCommands(eid))
        {
            switch (c.CommandId)
            {
                case 3001 when c.Args.Length > 2 && c.Args[2] > 0:
                    e.HpPerTick += (int)c.Args[2];
                    break;
                case 3002 when c.Args.Length > 0 && c.Args[0] != 0:
                    e.HpPerTick += (int)c.Args[0];
                    break;
                case 2055:
                    for (var i = 0; i < Math.Min(5, c.Args.Length); i++)
                        if (c.Args[i] != 0)
                            e.LockFlags |= 1 << i;
                    break;
            }
        }
    }

    /// <summary>Pose/rafraîchit un enchant sur un mob — miroir
    /// `CEnchantMgr::Add` : re-add du même id réarme la durée et le
    /// niveau (Reset @0x8568c10), stack borné à col59. Émet 299 aux
    /// sessions qui voient le mob (sauf icône cachée = dur -9).</summary>
    private async Task AddMobEnchantAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int casterId, int eid,
        int restSec, int level, bool hidden, CancellationToken token)
    {
        // Enchant purement instantané — même gate natif que la voie
        // joueur (EffectToLife `effet+0x12c == 0` @0x08060ec0) : ses
        // commandes s'exécutent une fois (dispels 1821-1824, pulls,
        // contre-sorts…) sans créer de CEnchant ni émettre d'icône
        // 299 — sinon un sort instantané « fuyait » en status du mob.
        if (IsInstantOnlyEnchant(eid))
        {
            var host = new MobSpellEffectHost(this, session, st, token);
            var ctx = new SpellEffectContext(casterId, st.PosX, st.PosZ,
                mob, eid);
            foreach (var c in _services.Enchants.GetUseCommands(eid))
                await SpellEffects.TryExecuteAsync(host, ctx, c);
            return;
        }
        // CEnchantMgr::Add @0x08569a50 — FindByType : même type
        // (col38) → rejet si l'existant domine, sinon remplacement
        // (300) — voir EnchantService.TypeAddAllowed. shortcut: la
        // partition flag-bits (param_4>>0x1a) du natif est ignorée —
        // group 0 supposé uniforme, <si une donnée exige des groupes
        // distincts>.
        if (!_services.Enchants.TypeAddAllowed(eid, level,
                mob.Enchants.ToDictionary(k => k.Key,
                    v => (int)v.Value.Level), out var replaced))
            return;
        foreach (var cid in replaced)
            await RemoveMobEnchantAsync(session, st, mob, cid, token);
        if (!mob.Enchants.TryGetValue(eid, out var en))
        {
            en = new ZoneLiveWorld.LiveMob.MobEnchant { EffectId = eid };
            mob.Enchants[eid] = en;
        }
        // CEnchantMgr::Add natif : un re-add réarme la durée et le
        // niveau mais ne touche PAS le compteur +0x30 (Reset @0x8568c10).
        en.CasterId = casterId;
        en.RestSeconds = restSec;
        en.Hidden = hidden;
        en.Level = (short)Math.Max(1, level);
        FillMobEnchantFields(en, eid);
        EdenLog.Write($"[Status] add mob={mob.Handle:x8} enchant={eid} " +
            $"rest={restSec} lvl={level} hidden={hidden}");
        if (hidden) return;
        // 299 ClientEnchantAdd — même corps que la voie joueur, handle
        // = sight-handle du mob (0xFFFF8xxx) — BroadcastToCanSee natif.
        var add = new CNC_CZ_ClientEnchantAdd
        {
            Field1 = (int)mob.Handle,       // i32 : handle entité
            Field2 = (short)eid,            // s16 : effect_id S_Enchant
            Field3 = en.RestSeconds,        // i32 : rest_time (s)
            Field4 = mob.NextAction(),      // u8  : serial entité (+0x280)
            Field5 = en.StackCount,         // s16 : cumul affiché (+0x30)
            Field6 = 0,                     // i32 : flagBits (sets>>0x1a)
            Field7 = en.Level,              // s16 : CEnchant+0x3c
            Field8 = 0,                     // i32 : CEnchant+0x34
        };
        await BroadcastToMobSeersAsync(mob, add, token);
    }

    /// <summary>Retire un enchant du registre mob + 300 aux témoins
    /// (le 300 natif ne part que si l'icône avait été émise).</summary>
    private async Task RemoveMobEnchantAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int eid,
        CancellationToken token)
    {
        if (!mob.Enchants.Remove(eid, out var en)) return;
        EdenLog.Write($"[Status] remove mob={mob.Handle:x8} " +
            $"enchant={eid}");
        if (en.Hidden) return;
        var rm = new CNC_CZ_ClientEnchantRemove
        {
            Field1 = (int)mob.Handle,       // i32 : handle entité
            Field2 = (short)eid,            // s16 : effect_id
            Field3 = mob.NextAction(),      // u8  : serial entité
            Field4 = 0, Field5 = 0,         // i32 ×2 : +0x10/+0x14 natifs
        };
        await BroadcastToMobSeersAsync(mob, rm, token);
    }

    /// <summary>Purge le registre à la mort du mob — la capture native
    /// montre un 300 par enchant actif AVANT le 305 (KillMobBroadcast),
    /// puis le CEnchantMgr est reset sans persistance. Appelé depuis
    /// <c>FinishMobDeathAsync</c>.</summary>
    private async Task PurgeMobEnchantsAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, CancellationToken token)
    {
        if (mob.Enchants.Count == 0) return;
        foreach (var eid in mob.Enchants.Keys.ToList())
            await RemoveMobEnchantAsync(session, st, mob, eid, token);
        mob.Enchants.Clear();
    }

    /// <summary>Mobs vivants du node+instance du lanceur dans un rayon
    /// — sélection ennemie d'<c>Effect2Target</c> (7003).</summary>
    private IEnumerable<ZoneLiveWorld.LiveMob> MobsNear(
        EntryState st, float x, float z, float radius)
    {
        var r2 = radius * radius;
        foreach (var m in _services.Live.MobsOf(st.NodeId,
                     st.InstanceGroup))
        {
            if (!m.Alive) continue;
            var dx = m.X - x; var dz = m.Y - z;
            if (dx * dx + dz * dz <= r2) yield return m;
        }
    }

    /// <summary>Applique un enchant à chaque joueur migré et vivant du
    /// node dans le rayon. Sélection d'<c>Effect2Target</c> (7003,
    /// modes 3/4/5) — le natif itère les entités vues via
    /// <c>IGameObj::CloseEnough</c> puis filtre par IsMyFriend/
    /// IsMyEnemy du lanceur : <paramref name="foes"/>=false (côté ami)
    /// exclut les joueurs hostiles à <paramref name="st"/> (lanceur
    /// inclus — distance 0, jamais ennemi de lui-même) ; =true (côté
    /// ennemi) ne retient QUE les joueurs hostiles (contexte PvP
    /// partagé — <see cref="IsPlayerEnemy"/>).</summary>
    private async Task<int> ApplyEnchantToPlayersNearAsync(
        NetSession session, EntryState st, float x, float z,
        float radius, int effectId, int restSec, int level,
        bool foes, CancellationToken token)
    {
        var n = 0;
        var r2 = radius * radius;
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.Dead || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup)
                continue;
            var dx = ost.PosX - x; var dz = ost.PosZ - z;
            if (dx * dx + dz * dz > r2) continue;
            if (IsPlayerEnemy(st, ost) != foes) continue;
            n++;
            await AddPlayerEnchantAsync(s, ost, (int)st.CharId,
                effectId, restSec, level, token);
        }
        return n;
    }

    /// <summary>2047 HealPlus — soin flat des vitals du lanceur
    /// (CEC_HealPlus::Execute : entity+0x8c = l'attaquant) — clampé
    /// [1,max]/[0,max], émis via les paquets vitals existants.</summary>
    private async Task HealCasterFromEffectAsync(NetSession session,
        EntryState st, int hpDelta, int mpDelta, CancellationToken token)
    {
        if (hpDelta != 0 && !st.Dead && st.PlayerHp > 0)
        {
            var next = Math.Clamp(st.PlayerHp + hpDelta, 1,
                st.PlayerMaxHp);
            if (next != st.PlayerHp)
            {
                st.PlayerHp = next;
                await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
                    st.PlayerMaxHp, token);
            }
        }
        if (mpDelta != 0)
        {
            var next = Math.Clamp(st.PlayerMp + mpDelta, 0,
                st.PlayerMaxMp);
            if (next != st.PlayerMp)
            {
                st.PlayerMp = next;
                await SendVitalMpAsync(session, st.CharId, st.PlayerMp,
                    st.PlayerMaxMp, token);
            }
        }
    }

    /// <summary>1913 PlayEffect — `CNC_CZ_ClientPlayEffect` natif :
    /// NotifyClient(lanceur) + BroadCastToCanSee(lanceur)
    /// (CEC_PlayEffect::Execute @0x0854b200 — Field5 = id du
    /// contrôleur = le lanceur, chaîne/i32/u8 de queue nuls).</summary>
    private async Task PlayCasterEffectAsync(NetSession session,
        EntryState st, float a0, float a1, float a2, int a3, float a4,
        uint handle, CancellationToken token)
    {
        var cmd = new CNC_CZ_ClientPlayEffect
        {
            Field1 = a0,                // f32 : arg0 (id effet)
            Field2 = a1,                // f32 : arg1
            Field3 = a2,                // f32 : arg2
            Field4 = a3,                // i32 : arg3
            // i32 : control id — lanceur par défaut, handle mob pour
            // les effets dont la source est la cible (1807 pull).
            Field5 = (int)(handle != 0 ? handle : st.CharId),
            Field6 = a4,                // f32 : arg4
            Field7 = "",                // str : vide (Execute natif)
            Field8 = 0,                 // i32 : 0
            Field9 = 0,                 // u8  : 0
        };
        await session.SendAsync(cmd, token);
        await BroadcastToNodeAsync(session, st, cmd, token);
    }

    /// <summary>1999 SpellAttack — le mob recaste `spellId` vers son
    /// attaquant via le pipeline de cast mob existant (wind-up
    /// `CastTimeMs` honoré, génération courante = pas de staleness).
    /// Le cast passe le même test de portée que la cible native
    /// (<c>CSpellToLife::CastTest</c> @0x084f04d0 : borne =
    /// portée + rayons + <see cref="Combat.SpellCastRouting.
    /// OuterRangeBonus"/>) — sans ça un proc d'enchant déclenché par
    /// un coup à distance faisait recaster le mob de très loin.
    /// Pas de condition « premier hit » ici : le contre-sort est un
    /// effet d'enchant déclenché par un coup REÇU, pas une décision
    /// d'IA de cast.</summary>
    private async Task MobCounterCastAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob, int spellId,
        CancellationToken token)
    {
        if (!mob.Alive) return;
        var sdef = _services.Spells.GetDefinition(spellId);
        if (sdef == null) return;
        var dx = mob.X - st.PosX; var dy = mob.Y - st.PosZ;
        var mdef = _services.Monsters.GetDefinition(mob.MobId);
        if (!MobSpellInCastRange(mdef, sdef, dx * dx + dy * dy,
                Combat.SpellCastRouting.OuterRangeBonus))
            return;
        await MobCastSpellAsync(session, st, mob, sdef,
            mob.CombatGeneration, token);
    }

    /// <summary>BroadCastToCanSee natif pour un paquet ciblant un mob :
    /// toute session migrée sur le même node+instance dont la vue
    /// contient déjà ce handle (<c>ost.Sent</c>) — l'émetteur inclus
    /// (il voit le mob).</summary>
    private async Task BroadcastToMobSeersAsync(
        ZoneLiveWorld.LiveMob mob, NetCommand command,
        CancellationToken token)
    {
        var (op, payload) = PreparePacket(command);
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != mob.NodeId ||
                ost.InstanceGroup != mob.InstanceKey ||
                !ost.Sent.ContainsKey(mob.Handle)) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Tick 1 s du registre mob — pendant de
    /// `StatusTickStepAsync` : HP/tick (DoT peut tuer — le natif tue
    /// par le tick, cf. 荊棘咒縛 -10/s) puis expiration → retrait.
    /// Appelé depuis <see cref="ZoneLoop"/>.SlowTick sur le fil
    /// unique.</summary>
    public async Task TickMobEnchantsAsync()
    {
        foreach (var mob in _services.Live.AllMobs)
        {
            if (!mob.Alive || mob.Enchants.Count == 0) continue;
            var delta = 0;
            foreach (var en in mob.Enchants.Values) delta += en.HpPerTick;
            if (delta != 0)
            {
                var next = mob.Hp + delta;
                if (next <= 0)
                {
                    // DoT mortel — attribution via le castreur de la
                    // source drainante si sa session vit encore ;
                    // sinon le coup est compté sans récompense.
                    var dot = System.Linq.Enumerable.FirstOrDefault(
                        mob.Enchants.Values, e => e.HpPerTick < 0);
                    var ks = dot != null
                        ? _services.Sessions.FindByCharacter(dot.CasterId)
                        : null;
                    if (ks != null && _states.TryGetValue(
                            ks.SessionId, out var kst))
                    {
                        await FinishMobDeathAsync(ks, kst, mob, true,
                            CancellationToken.None);
                    }
                    else
                    {
                        // Castreur parti — le mob reste à 1 HP au lieu
                        // d'une mort sans attribution (limite connue :
                        // le natif attribue par entité, pas par session).
                        mob.Hp = 1;
                    }
                    continue;
                }
                mob.Hp = Math.Min(mob.MaxHp, next);
                // Attr 0 broadcast — la barre de vie du mob descend
                // pour tous les témoins à chaque tick (ordre natif :
                // HP courant, pas de paquet « dégât » pour les DoT).
                await BroadcastToMobSeersAsync(mob,
                    new CNC_CZ_ClientUpdateAttr
                    {
                        Field1 = (int)mob.Handle, Field2 = 0,
                        Field3 = mob.Hp, Field4 = mob.Hp,
                    }, CancellationToken.None);
            }
            var expired = new List<int>();
            foreach (var kv in mob.Enchants)
                if (kv.Value.RestSeconds > 0 &&
                    --kv.Value.RestSeconds == 0)
                    expired.Add(kv.Key);
            foreach (var eid in expired)
            {
                // N'importe quelle session voyant le mob sert de
                // relais broadcast — la plus proche = le castreur.
                var sess = _services.Sessions.FindByCharacter(
                    mob.Enchants.GetValueOrDefault(eid)?.CasterId ?? 0)
                    ?? _services.Sessions.All.FirstOrDefault();
                var sst = sess != null &&
                    _states.TryGetValue(sess.SessionId, out var t)
                    ? t : null;
                if (sess == null || sst == null)
                {
                    mob.Enchants.Remove(eid);
                    continue;
                }
                await RemoveMobEnchantAsync(sess, sst, mob, eid,
                    CancellationToken.None);
            }
        }
    }

    /// <summary>NCID 18 `EnchantUpdate [i32 handle]` → 301 rempli des
    /// enchants visibles de l'entité — mob (registre) ou joueur
    /// (statuts de SA session). Le natif renvoie le CEnchantMgr de
    /// l'entité visée (golden ncid_301 : uid + n entrées 16B +
    /// flags + serial).</summary>
    private async Task HandleEnchantUpdateAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4) return;
        var handle = BitConverter.ToInt32(payload);
        var upd = new CNC_CZ_ClientEnchantUpdate { Field1 = handle };
        if (_services.Live.GetMob((uint)handle) is { } mob)
        {
            foreach (var e in mob.Enchants.Values)
            {
                if (e.Hidden) continue;
                upd.Entries.Add(new CNC_CZ_ClientEnchantUpdate.EnchantEntry
                {
                    SpellId = (short)e.EffectId,
                    CasterId = e.CasterId,
                    RestTime = e.RestSeconds,
                    Stacks = e.StackCount,
                    F4 = e.Level,
                    Note = 0,
                });
            }
            upd.B280 = mob.NextAction();
        }
        else if (_services.Sessions.FindByCharacter(handle) is { } ps &&
                 _states.TryGetValue(ps.SessionId, out var pst))
        {
            foreach (var s in pst.Statuses.Values)
            {
                if (s.Hidden) continue;
                upd.Entries.Add(new CNC_CZ_ClientEnchantUpdate.EnchantEntry
                {
                    SpellId = (short)s.EffectId,
                    CasterId = s.CasterId,
                    RestTime = s.RestSeconds,
                    Stacks = (short)s.StackCount,
                    F4 = (short)s.Level,
                    Note = 0,
                });
            }
            upd.B280 = pst.NextAction();
        }
        await session.SendAsync(upd, token);
    }
}
