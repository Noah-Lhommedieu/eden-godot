using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Système d'invocation (pets/totems) — partial de
/// ZoneEntryFlow. Modèle natif (zone/decompiled.c) :
/// - `CEC_SummonPet::Execute` @0x855c2xx et `CEC_SummonPetPlus::Execute`
///   @0x855c6xx : la cible doit être un personnage (vtype 7), l'ancien
///   pet du même slot est désinvoqué (RemoveEnchantByID sur
///   pet+0x3f0 = enchant source), puis `CNode::BornPet` (pet mobile)
///   ou `BornTotemPet` (stationnaire, flag byte de la commande),
///   l'enchant source est inscrit sur pet+0x3f0 et un
///   `ClientPlayEffect` (383) est broadcasté au can-see.
/// - Vars du bloc S_Enchant : var0 = chance/niveau, var1 = id monstre
///   début, var2 = id fin (si &lt; var1 → id direct), var4 = effect id
///   du 383 (0 → DefaultSummonEffect), var5 = flag totem.
/// - `ClientSummonPet` (438) — remplissage natif dans les boucles de
///   synchro pets (owner+0x7dc) : `[i32 maître+0x34][i32 pet+0x34]
///   [s16 template][i32 pet vfunc+e0][i32 pet+0x60][i32 type]` —
///   type=0 à l'ajout unitaire, 2 en synchro de masse.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Ids des commandes S_Enchant qui invoquent une entité
    /// (EnchantCommands : 2037/2052/2054/2073 = SummonPet,
    /// 2072 = SummonPetPlus).</summary>
    private static readonly HashSet<int> SummonCommandIds =
        new() { 2037, 2052, 2054, 2072, 2073 };

    /// <summary>Exécute les commandes d'invocation d'un enchant qui
    /// vient d'être posé sur le joueur — appelé par
    /// AddPlayerEnchantAsync, comme l'Execute natif au apply de
    /// l'effet. Chaque commande summon spawn un pet/totem après avoir
    /// retiré l'invocation existante du même slot.</summary>
    private async Task ExecuteSummonEnchantCommandsAsync(NetSession session,
        EntryState st, int effectId, CancellationToken token)
    {
        foreach (var c in _services.Enchants.GetUseCommands(effectId))
        {
            if (!SummonCommandIds.Contains(c.CommandId)) continue;
            var idStart = c.Args.Length > 1 ? (int)c.Args[1] : 0;
            var idEnd = c.Args.Length > 2 ? (int)c.Args[2] : 0;
            var monsterId = ResolveSummonTemplate(idStart, idEnd, st);
            if (monsterId <= 0)
            {
                EdenLog.Write($"[Summon] char={st.CharId} " +
                    $"enchant={effectId} : template [{idStart}-{idEnd}] " +
                    "introuvable dans S_Monster");
                continue;
            }
            var isTotem = c.Args.Length > 5 && c.Args[5] != 0;
            var effectVisual = c.Args.Length > 4 ? c.Args[4] : 0;

            // Remplacement natif : l'ancien pet du même slot (mobile ou
            // totem) est désinvoqué avant le nouveau BornPet.
            await DespawnPetsAsync(st, isTotem ? 1 : 0, -1, token);

            var ex = _services.Live.SpawnPet(st.NodeId, st.InstanceGroup, monsterId,
                (int)st.CharId, st.PosX + 1.5f, st.PosZ + 1.5f,
                _services.Live.GroundHeightAt(
                    _services.Scenes.GetScene($"S{st.NodeId:D3}"),
                    st.PosX, st.PosZ),
                st.Facing, isTotem, effectId);
            EdenLog.Write($"[Summon] char={st.CharId} " +
                $"enchant={effectId} → {(isTotem ? "totem" : "pet")} " +
                $"{monsterId} handle={ex.Handle:X8}");

            // 438 ClientSummonPet — corps 22B natif (Serialize
            // @0x81e9xxx + remplissage dans la boucle de synchro des
            // pets char+0x7dc) : pet+0x34 et vfunc+0xe0 donnent tous
            // deux le sight-handle ; pet+0x60 inutilisé ici → 0.
            var msg438 = new CNC_CZ_ClientSummonPet
            {
                Field1 = (int)st.CharId,          // i32 : maître (+0x34)
                Field2 = (int)ex.Handle,          // i32 : pet (+0x34)
                Field3 = (short)monsterId,        // s16 : template monstre
                Field4 = (int)ex.Handle,          // i32 : pet (vfunc+0xe0)
                Field5 = 0,                       // i32 : pet+0x60 natif
                Field6 = 0,                       // i32 : type (0 = ajout)
            };
            await session.SendAsync(msg438, token);
            // CanSee sur le handle du pet — le watcher ne connaît
            // l'invocation qu'une fois son SeePet reçu (prochaine
            // passe sight), le broadcast d'avant serait un fantôme.
            await BroadcastToNodeAsync(session, st, msg438, token,
                handle: ex.Handle);

            // 383 ClientPlayEffect — BroadCastToCanSee natif dans
            // l'Execute : position à 0 (l'effet suit le handle),
            // Field4 = effect id (var4, 0 → DefaultSummonEffect),
            // Field5 = pet+0x34, Field6 = scale (var5 s16→f32, 1.0).
            var body383 = Concat(
                BitConverter.GetBytes(0f),                  // f32 : x
                BitConverter.GetBytes(0f),                  // f32 : y
                BitConverter.GetBytes(0f),                  // f32 : z
                BitConverter.GetBytes(effectVisual),        // i32 : effect id
                BitConverter.GetBytes(ex.Handle),           // i32 : cible (pet+0x34)
                BitConverter.GetBytes(1f),                  // f32 : scale
                BitConverter.GetBytes((short)0),            // str : "" (s16 len)
                BitConverter.GetBytes(0),                   // i32 : champ +0x20
                new byte[] { 0 });                          // u8  : champ +0x24
            await session.SendPacketAsync(383, body383, token);
            await BroadcastToNodeAsync(session, st, 383, body383,
                token, handle: ex.Handle);
        }
    }

    /// <summary>Choix du template d'invocation — SummonPetPlus natif
    /// sélectionne dans [idStart..idEnd] selon le niveau du sort
    /// (GetSpellMasterBySpell) ; on prend le monstre de plus haut
    /// niveau ≤ niveau du perso, repli sur la borne basse.</summary>
    private int ResolveSummonTemplate(int idStart, int idEnd, EntryState st)
    {
        if (idStart <= 0) return 0;
        if (idEnd <= idStart) return idStart;
        var best = idStart;
        var bestLevel = -1;
        for (var id = idStart; id <= idEnd; id++)
        {
            if (!_services.Live.MonsterDefs.TryGetValue(id, out var def))
                continue;
            if (def.Level <= st.Level && def.Level > bestLevel)
            {
                best = id;
                bestLevel = def.Level;
            }
        }
        return best;
    }

    /// <summary>Désinvoque les pets du joueur — `isTotem` filtre le
    /// slot natif (-1 = les deux), `sourceEnchantId` filtre par enchant
    /// (-1 = tous). Chaque retrait émet 269 LifeEradicate aux sessions
    /// qui voient le pet (même paquet que la sortie de sight).</summary>
    private async Task DespawnPetsAsync(EntryState st, int totemFilter,
        int sourceEnchantId, CancellationToken token)
    {
        var gone = _services.Live.PetsOf((int)st.CharId)
            .Where(p => (totemFilter < 0 || p.IsTotem == (totemFilter == 1)) &&
                        (sourceEnchantId < 0 ||
                         p.SourceEnchantId == sourceEnchantId))
            .ToList();
        foreach (var pet in gone)
        {
            _services.Live.DespawnPet(pet.Handle);
            var (op, payload) = PreparePacket(
                new CNC_CZ_ClientLifeEradicate
                { Value = (int)pet.Handle });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    ost.NodeId != st.NodeId ||
                    ost.InstanceGroup != st.InstanceGroup ||
                    !ost.Sent.TryRemove(pet.Handle, out _))
                    continue;
                ost.EntityPos.TryRemove(pet.Handle, out _);
                await SendPreparedToAsync(s, op, payload, token);
            }
            EdenLog.Write($"[Summon] char={st.CharId} pet " +
                $"{pet.MobId} handle={pet.Handle:X8} désinvoqué");
        }
    }

    /// <summary>Re-invocation après migration de node — les enchants
    /// actifs porteurs de commandes summon re-créent leur pet sur la
    /// nouvelle map (le statut persiste sur le perso).</summary>
    private async Task RespawnPetsAfterMigrationAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        foreach (var eid in st.Statuses.Keys.ToList())
            if (_services.Enchants.GetUseCommands(eid)
                    .Any(c => SummonCommandIds.Contains(c.CommandId)))
                await ExecuteSummonEnchantCommandsAsync(session, st, eid,
                    token);
    }

    /// <summary>Tick pets — appelé par la boucle wander (300 ms,
    /// cadence native `CAI+0x56=300` de CAIFSMRoam) : suivi du maître
    /// et attaque des ennemis du maître. Totems : pas de déplacement.
    /// Le mouvement passe par le 326 (ClientMovePrediction), l'attaque
    /// par le 272 + 280 attr0.</summary>
    private async Task TickPetsAsync(int nodeId, int inst,
        IReadOnlyList<NetSession> watchers, DateTime now)
    {
        foreach (var pet in _services.Live.MobsOf(nodeId, inst)
                     .Where(m => m.OwnerCharId != 0 && m.Alive).ToList())
        {
            var owner = _states.Values.FirstOrDefault(ost =>
                ost.Migrated && ost.CharId == (uint)pet.OwnerCharId &&
                ost.NodeId == nodeId &&
                ost.InstanceGroup == inst);
            if (owner?.Session == null) continue;

            // Cible : d'abord la cible verrouillée (pet+0xd0 natif,
            // SetPetInstruction(100) via NCID 188 instr 1) ; sinon
            // assistance — UNIQUEMENT un mob engagé contre le maître
            // (hate-list du pet natif, alimentée par AddHateToLife
            // quand un mob combat le maître). JAMAIS la simple
            // sélection du joueur (char+0x4c0) — le natif ne s'en
            // sert que pour le battle-buff du sprite. Sans ce
            // garde-fou, un clic gauche de sélection faisait exécuter
            // le mob par le pet.
            ZoneLiveWorld.LiveMob? target = null;
            if (pet.ElfMode != 2)
            {
                if (pet.PetLockTarget != 0 &&
                    _services.Live.GetMob(pet.PetLockTarget) is
                        { Alive: true } locked)
                {
                    target = locked;
                }
                else
                {
                    pet.PetLockTarget = 0;   // cible morte/partie
                    target = _services.Live.MobsOf(nodeId, inst)
                        .Where(m => m.Alive && m.InCombat &&
                                    m.TargetCharId == pet.OwnerCharId &&
                                    m.OwnerCharId == 0 && !m.IsElfPet)
                        .OrderBy(m => Dist2(pet.X, pet.Y, m.X, m.Y))
                        .FirstOrDefault();
                }
            }
            else if (pet.PetLockTarget != 0)
            {
                pet.PetLockTarget = 0;
            }

            if (target != null)
            {
                var d2 = Dist2(pet.X, pet.Y, target.X, target.Y);
                if (d2 > 6.25f && !pet.IsTotem)
                {
                    // Hors portée de coup : le pet se rapproche (2,5 u).
                    MovePetToward(pet, target.X, target.Y, 2.5f, watchers,
                        now);
                }
                else if (pet.NextPetAttackAt <= now)
                {
                    pet.NextPetAttackAt = now +
                        TimeSpan.FromMilliseconds(1600);
                    await PetHitAsync(owner, pet, target, watchers);
                }
            }
            else if (!pet.IsTotem && pet.IsElfPet && pet.ElfMode == 2 &&
                     await TryPetLootAsync(owner, pet, nodeId, inst,
                         watchers, now))
            {
                // Mode loot (2) : le pet est parti ramasser / ramasse.
            }
            else if (!pet.IsTotem)
            {
                // Suivi natif `CAIFSMRoam::SetNewGoalPetFollow`
                // @0x084de9f0 : si dist²(pet,maître) > 9.0 (3 u),
                // goal = position maître + _DAT_087b2754 (1.0) sur un
                // axe — le pet se cale à ~1 u du maître.
                var d2 = Dist2(pet.X, pet.Y, owner.PosX, owner.PosZ);
                if (d2 > 9f)
                    MovePetToward(pet, owner.PosX, owner.PosZ + 1.0f,
                        0.3f, watchers, now);
            }
        }
    }

    /// <summary>Mode loot du familier (plan 18 §5) : va vers le
    /// trésor visible du maître le plus proche et le ramasse dans
    /// l'inventaire du maître. Retourne vrai si le pet est occupé par
    /// un trésor (le suivi normal est alors sauté).</summary>
    private async Task<bool> TryPetLootAsync(EntryState owner,
        ZoneLiveWorld.LiveMob pet, int nodeId, int inst,
        IReadOnlyList<NetSession> watchers, DateTime now)
    {
        if (pet.NextPetLootAt > now || owner.Session == null) return false;
        var tr = PetRules.PickLootTarget(
            _services.GroundTreasure.ListForNode(nodeId, inst),
            pet.X, pet.Y, owner.PosX, owner.PosZ, pet.OwnerCharId);
        if (tr == null) return false;
        var reach2 = PetRules.LootPickupReach * PetRules.LootPickupReach;
        if (Dist2(pet.X, pet.Y, tr.X, tr.Z) > reach2)
            MovePetToward(pet, tr.X, tr.Z, 1.0f, watchers, now);
        if (Dist2(pet.X, pet.Y, tr.X, tr.Z) > reach2) return true;
        var ok = await PickupTreasureCoreAsync(owner.Session, owner, tr,
            pet.X, pet.Y, byPet: true, CancellationToken.None);
        if (!ok) pet.NextPetLootAt = now + PetRules.LootRetryDelay;
        return true;
    }

    /// <summary>Déplace le pet vers un point d'arrêt à `stopAt` unités
    /// de la cible et émet le 326 à tous les témoins qui le voient.</summary>
    private void MovePetToward(ZoneLiveWorld.LiveMob pet, float tx,
        float ty, float stopAt, IReadOnlyList<NetSession> watchers,
        DateTime now)
    {
        var dx = tx - pet.X;
        var dy = ty - pet.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist <= stopAt + 0.1f) return;
        // Le pet parcourt au plus ~10 u par tick (suivi réaliste —
        // au-delà on le téléporte, comme un pet largué par un dash).
        var step = Math.Min(dist - stopAt, 10f);
        var nx = pet.X + dx / dist * step;
        var ny = pet.Y + dy / dist * step;
        var (mvOp, mvPayload) = PreparePacket(
            BuildMovePrediction(pet.Handle, pet.X, pet.Y, nx, ny));
        foreach (var s in watchers)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Sent.ContainsKey(pet.Handle))
                continue;
            // Fire-and-forget conservé : les erreurs de session morte
            // sont avalées par SendPreparedToAsync.
            _ = SendPreparedToAsync(s, mvOp, mvPayload,
                CancellationToken.None);
        }
        pet.X = nx;
        pet.Y = ny;
        pet.Facing = NativeDir(dx, dy);
    }

    /// <summary>Coup du pet sur la cible — `272 [pet][mob][dmg][1][kfId]`
    /// + `280 attr0` aux témoins (même paquets que les coups mob→joueur,
    /// rôles inversés). La mort passe par FinishMobDeathAsync avec le
    /// maître comme tueur (XP/loot/quêtes au propriétaire).</summary>
    private async Task PetHitAsync(EntryState owner,
        ZoneLiveWorld.LiveMob pet, ZoneLiveWorld.LiveMob target,
        IReadOnlyList<NetSession> watchers)
    {
        var kf = pet.AttackKfIds.Length > 0
            ? pet.AttackKfIds[Random.Shared.Next(pet.AttackKfIds.Length)]
            : MotionParser.DefaultAttackKfId;
        var dmg = Math.Max(1, (int)(pet.AttackPower *
            (0.9 + Random.Shared.NextDouble() * 0.2)));
        // Le pet elf est non-ciblable natif (IsMyEnemy=false pour les
        // 0x50b) : un mob frappé par l'ordre d'attaque engage son
        // riposte sur le maître.
        if (target.Alive && !target.InCombat && target.OwnerCharId == 0)
        {
            target.InCombat = true;
            target.TargetCharId = pet.OwnerCharId;
            target.AggroX = target.X; target.AggroY = target.Y;
        }
        target.Hp = Math.Max(0, target.Hp - dmg);
        var hit = new CNC_CZ_ClientMeleeAttack
        {
            Field1 = pet.Handle,                // u32 : attaquant
            Field2 = target.Handle,             // u32 : cible
            Field3 = dmg,                       // i32 : dégâts
            Field4 = 1,                         // i32 : type hit
            Field5 = kf,                        // i32 : kfId d'attaque
        };
        var hpBody = new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)target.Handle,        // u32 : handle cible
            Field2 = 0,                         // s16 : attr 0 = HP
            Field3 = target.Hp,                 // i32 : p1 = cur
            Field4 = target.Hp,                 // i64 : p2 = cur
        };
        var (hitOp, hitPayload) = PreparePacket(hit);
        var (hpOp, hpPayload) = PreparePacket(hpBody);
        foreach (var s in watchers)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Sent.ContainsKey(pet.Handle))
                continue;
            await SendPreparedToAsync(s, hitOp, hitPayload,
                CancellationToken.None);
            await SendPreparedToAsync(s, hpOp, hpPayload,
                CancellationToken.None);
        }
        if (target.Hp <= 0 && owner.Session != null)
        {
            target.MarkDead();
            target.DeadAt = DateTime.UtcNow;
            try
            {
                await FinishMobDeathAsync(owner.Session, owner, target,
                    false, CancellationToken.None);
            }
            catch { /* session morte */ }
        }
    }

    private static float Dist2(float ax, float ay, float bx, float by) =>
        (ax - bx) * (ax - bx) + (ay - by) * (ay - by);
}
