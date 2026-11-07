using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{

    /// <summary>Rayon de vue côté serveur (envoi SeeMob / retrait).</summary>
    /// <summary>Demi-côté du carré de vue — CanSee natif = ±48 par axe.
    /// SightGone ajoute une hystérésis pour éviter le flapping.</summary>
    private const float SightHalf = 46f, SightGone = 54f;

    /// <summary>Mémorise handle→templateId + position des SeeMob (298) rejoués
    /// (ils comptent comme déjà dans la vue du client).</summary>
    private static void IndexSeenEntity(EntryState st, string hex)
    {
        var p = Convert.FromHexString(hex);
        if (p.Length < 25 || BitConverter.ToUInt16(p, 0) != 298) return;
        var h = BitConverter.ToUInt32(p, 2);
        st.HandleToEntity[h] = BitConverter.ToInt16(p, 6) & 0xFFFF;
        st.Sent[h] = 0;
        st.EntityPos[h] = (BitConverter.ToSingle(p, 9), BitConverter.ToSingle(p, 13));
        st.Replayed[h] = p[2..]; // corps complet capturé — réémis au retour en vue
    }

    /// <summary>Sight aligné sur le CanSee natif : carré ±48 par axe. À la
    /// sortie (>56) on envoie LifeEradicate(269) puis on oublie le handle — le
    /// client décharge le modèle et le ré-instanciera au prochain 298.
    /// IMPORTANT : le rayon ne doit PAS dépasser la distance de cull client,
    /// sinon l'entité est enregistrée hors-écran et jamais ré-affichée.
    /// Throttle 350 ms : MoveSelf arrive à ~10 Hz et un scan + rafale de
    /// 298 à chaque paquet gelait le pipeline d'envoi (micro-coupures).</summary>
    private async Task UpdateSightAsync(NetSession session, EntryState st,
        CancellationToken token, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - st.LastSightAt < TimeSpan.FromMilliseconds(350))
            return;
        st.LastSightAt = now;
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var entities = _services.Live.EnsureNodePopulated(st.NodeId, st.InstanceGroup, scene);

        // SNAPSHOT — la liste du node est mutée concurremment
        // (SpawnNpc/DespawnNpcs/DespawnGroup/SpawnGroup font Add/
        // RemoveAll depuis le thread d'events). Sans la copie, le
        // foreach pouvait lever InvalidOperationException en pleine
        // passe → TOUTE la sight avortée → les entités n'étaient plus
        // jamais ré-annoncées en revenant (bug live : NPC d'instance
        // disparu jusqu'à la portal stone).
        var snapshot = entities.ToArray();
        var liveHandles = new HashSet<uint>();
        foreach (var e in snapshot)
        {
            liveHandles.Add(e.Handle);
            // Position LIVE du mob (il poursuit/patrouille) — SeenEntity
            // fige la position de spawn : un mob qui s'est déplacé était
            // évalué hors-vue au mauvais endroit et disparaissait sans
            // jamais revenir dans le rayon réel.
            float ex = e.X, ey = e.Y;
            var alive = true;
            var liveMob = e.IsMonster
                ? _services.Live.GetMob(e.Handle) : null;
            if (e.IsMonster)
            {
                if (liveMob == null) continue;
                ex = liveMob.X; ey = liveMob.Y;
                alive = liveMob.Alive;
            }
            var dx = Math.Abs(ex - st.PosX);
            var dy = Math.Abs(ey - st.PosZ);
            var inSight = dx <= SightHalf && dy <= SightHalf;
            var gone = dx > SightGone || dy > SightGone;
            if (!alive)
            {
                // Mob mort : purge silencieuse de la vue — le 269 a déjà
                // été émis par ScheduleCorpseDespawn. Sans cette purge le
                // handle restait "déjà vu" et le respawn du mob n'était
                // jamais ré-annoncé au client.
                st.Sent.TryRemove(e.Handle, out _);
                st.EntityPos.TryRemove(e.Handle, out _);
                st.HandleToEntity.TryRemove(e.Handle, out _);
                continue;
            }
            if (inSight && !st.Sent.ContainsKey(e.Handle))
            {
                // NPC déjà rejoué à même position (autre handle) — ne pas dupliquer.
                if (!e.IsMonster && st.Replayed.Any(kv =>
                        st.HandleToEntity.GetValueOrDefault(kv.Key) == e.EntityId &&
                        st.EntityPos.TryGetValue(kv.Key, out var rp) &&
                        (rp.X - e.X) * (rp.X - e.X) + (rp.Y - e.Y) * (rp.Y - e.Y) < 25))
                    continue;
                st.Sent[e.Handle] = 0;
                st.EntityPos[e.Handle] = (ex, ey);
                // Repeuple la table handle→templateId : elle est
                // vidée à chaque migration (Migration.cs) et n'était
                // remplie qu'au spawn initial → TalkToNpc « handle
                // inconnu » post-//transport_node (constat live).
                st.HandleToEntity[e.Handle] = e.EntityId;
                try
                {
                    var hp = liveMob?.Hp ?? 0;
                    var facing = e.IsMonster ? liveMob!.Facing : e.Facing;
                    // GetMovement natif (vtable+0xd8 → CCharFormula::
                    // Movement) : idle = RoamSpeed (template+0x1c) ;
                    // le pet elf lit le champ battle (template+0x18,
                    // CCharFormula::Movement(CElfPet)).
                    var speed = liveMob?.Def != null
                        ? (liveMob.IsElfPet ? liveMob.Def.BattleSpeed
                            : liveMob.Def.RoamSpeed)
                        : 0f;
                    var body = BuildSeeMob(e.Handle, e.EntityId, e.IsMonster,
                        ex, ey, e.Z, facing, hp, speed,
                        liveMob is { IsElfPet: true } ? (short)-2537 : null,
                        liveMob?.PetName,
                        liveMob is { IsElfPet: true }
                            ? liveMob.OwnerCharId : 0,
                        liveMob?.MaxPatience ?? 100);
                    await session.SendAsync(body, token);
                    // 280 attr0 = HP — le natif l'envoie dès l'entrée en
                    // vue/aggro (`cur=max=hpCourant`) ; sans lui la barre
                    // de vie reste vide jusqu'au premier coup.
                    if (e.IsMonster && hp > 0)
                        await SendAttrAsync(session, e.Handle, 0, hp, hp, token);
                    // Mob BP déjà entamé : patience courante (le 298 ne
                    // porte que le max) — extension de resynchro d'un
                    // observateur tardif (attr 0x13 natif).
                    if (liveMob is { Def.BpWeakMask: not 0 } bp &&
                        bp.Patience != bp.MaxPatience)
                        await SendAttrAsync(session, e.Handle, 0x13,
                            bp.Patience, 0, token);
                }
                catch { st.Sent.TryRemove(e.Handle, out _); }
                EdenLog.Write($"[Sight] +{(e.IsMonster ? "mob" : "npc")}" +
                    $" #{e.EntityId} h=0x{e.Handle:x8} ({ex:F0},{ey:F0})");
            }
            else if (!inSight && gone && st.Sent.TryRemove(e.Handle, out _))
            {
                // EntityPos aussi : sinon la map grossit d'une entrée
                // par mob/npc vu dans la session (le mob-mort et la
                // purge orphelins la vident déjà — la sortie de vue
                // vivante l'oubliait).
                st.EntityPos.TryRemove(e.Handle, out _);
                st.HandleToEntity.TryRemove(e.Handle, out _);
                await session.SendAsync(new CNC_CZ_ClientLifeEradicate
                { Value = (int)e.Handle }, token);
                EdenLog.Write($"[Sight] -{(e.IsMonster ? "mob" : "npc")}" +
                    $" #{e.EntityId} h=0x{e.Handle:x8}");
            }
        }

        // Joueurs du même (node,instance) — CNC_CZ_ClientSeeChar
        // (265) : handle = charId (char+0x34 natif), suivi du 326
        // MovePrediction comme le natif (paquets cachés char+0x7d8/
        // 0x7d4 poussés à l'entrée du CanSee). À la sortie de vue :
        // 269 LifeEradicate.
        var playerHandles = new HashSet<uint>();
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId ||
                !_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.CharId == 0 ||
                ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            var ph = (uint)ost.CharId;
            playerHandles.Add(ph);
            var dx = Math.Abs(ost.PosX - st.PosX);
            var dy = Math.Abs(ost.PosZ - st.PosZ);
            var inSight = dx <= SightHalf && dy <= SightHalf;
            var gone = dx > SightGone || dy > SightGone;
            if (inSight && !st.Sent.ContainsKey(ph))
            {
                st.Sent[ph] = 0;
                st.EntityPos[ph] = (ost.PosX, ost.PosZ);
                try
                {
                    await session.SendPacketAsync(265,
                        BuildSeeCharBody(ost), token);
                    await session.SendAsync(BuildMovePrediction(ph,
                        ost.PosX, ost.PosZ, ost.PosX, ost.PosZ,
                        (byte)ost.MoveFlag, ost.Facing), token);
                }
                catch { st.Sent.TryRemove(ph, out _); }
                EdenLog.Write($"[Sight] +char #{ost.CharId} " +
                    $"({ost.PosX:F0},{ost.PosZ:F0})");
            }
            else if (!inSight && gone && st.Sent.TryRemove(ph, out _))
            {
                st.EntityPos.TryRemove(ph, out _);
                await session.SendPacketAsync(269,
                    BitConverter.GetBytes(ph), token);
                EdenLog.Write($"[Sight] -char #{ost.CharId}");
            }
        }

        // OnSeen natif : je viens de bouger → je pousse MON 265+326
        // aux watchers qui m'ont désormais dans leur rayon (un joueur
        // immobile ne rejoue sa sight qu'à son propre mouvement —
        // sans ce push il ne me verrait jamais arriver).
        await AnnounceSelfToWatchersAsync(session, st, token);

        // NPCs du hall de famille visité — SFamily::InitFamilyNpc
        // natif : housekeeper + NPCs du conteneur 0 injectés comme
        // entités rejouées (propres au perso : le node est partagé
        // entre visiteurs de familles différentes).
        if (!st.FamilyNpcsLoaded && IsInFamilyInstanceNode(st)
            && st.VisitedFamilyInstanceId != 0)
            await SpawnFamilyHallNpcsAsync(session, st, token);

        // Entités rejouées (handles de la capture) — réémission du paquet brut.
        foreach (var (h, body) in st.Replayed)
        {
            if (!st.EntityPos.TryGetValue(h, out var pos)) continue;
            var dx = Math.Abs(pos.X - st.PosX);
            var dy = Math.Abs(pos.Y - st.PosZ);
            var inSight = dx <= SightHalf && dy <= SightHalf;
            var gone = dx > SightGone || dy > SightGone;
            if (inSight && st.Sent.TryAdd(h, 0))
            {
                // Repeuple handle→entityId : la sortie de vue le purge
                // (cf. branche ci-dessous) et IndexSeenEntity ne court
                // qu'à l'ingestion — TalkToNpc retombait en « handle
                // inconnu » sur un NPC rejoué revu après téléport.
                if (body.Length >= 6)
                    st.HandleToEntity[h] =
                        BitConverter.ToInt16(body, 4) & 0xFFFF;
                await session.SendPacketAsync(298, body, token);
                EdenLog.Write($"[Sight] +npc(replay) h=0x{h:x8} ({pos.X:F0},{pos.Y:F0})");
            }
            else if (!inSight && gone && st.Sent.TryRemove(h, out _))
            {
                // EntityPos conservé : c'est l'ancre de position de
                // l'entité rejouée — la boucle Replayed la relit pour
                // ré-annoncer le corps au retour en vue.
                st.HandleToEntity.TryRemove(h, out _);
                await session.SendPacketAsync(269,
                    BitConverter.GetBytes(h), token);
                EdenLog.Write($"[Sight] -npc(replay) h=0x{h:x8}");
            }
        }

        // Trésors au sol du node — CTreasure natif : visibles du
        // propriétaire dès le spawn (329 u8=0), de tous après la
        // fenêtre (u8=1 via TickTreasuresAsync). Retrait à >54u par
        // 330 TreasureDisappear — jamais 269 (type d'objet différent).
        var treasureHandles = new HashSet<uint>();
        foreach (var t in _services.GroundTreasure.ListForNode(st.NodeId, st.InstanceGroup))
        {
            if (!t.IsPublic && t.OwnerCharId != (int)st.CharId)
                continue; // fenêtre propriétaire — invisible aux autres
            treasureHandles.Add(t.Id);
            var dx = Math.Abs(t.X - st.PosX);
            var dy = Math.Abs(t.Z - st.PosZ);
            var inSight = dx <= SightHalf && dy <= SightHalf;
            var gone = dx > SightGone || dy > SightGone;
            if (inSight && st.Sent.TryAdd(t.Id, 0))
            {
                await session.SendAsync(new CNC_CZ_ClientSeeTreasure
                {
                    Field1 = t.Id, Field4 = t.X, Field5 = t.Z,
                    Field6 = (ushort)t.ItemId,
                    Field7 = (short)t.Count, Field8 = (short)t.Count,
                    Field11 = t.OwnerCharId,
                    Field12 = (byte)(t.IsPublic ? 1 : 0),
                }, token);
            }
            else if (!inSight && gone &&
                     st.Sent.TryRemove(t.Id, out _))
            {
                await session.SendAsync(new CNC_CZ_ClientTreasureDisappear
                { Value = t.Id }, token);
            }
        }

        // Purge des handles orphelins — une entité retirée du node
        // (Event_KillNpc, despawn de groupe, fin d'event) n'apparaît
        // plus dans le snapshot : le client doit l'éradiquer (269)
        // sinon elle reste un fantôme figé, et le handle polluerait
        // indéfiniment st.Sent (empêchant aussi toute ré-annonce).
        foreach (var h in st.Sent.Keys)
        {
            if (liveHandles.Contains(h) || st.Replayed.ContainsKey(h) ||
                treasureHandles.Contains(h) || playerHandles.Contains(h))
                continue;
            if (!st.Sent.TryRemove(h, out _)) continue;
            st.EntityPos.TryRemove(h, out _);
            st.HandleToEntity.TryRemove(h, out _);
            try
            {
                await session.SendPacketAsync(269,
                    BitConverter.GetBytes(h), token);
            }
            catch { /* session morte */ }
            EdenLog.Write($"[Sight] -orphan h=0x{h:x8}");
        }
    }

    /// <summary>Peuple le node (monde partagé), référence les handles, puis
    /// première passe de sight — les entités n'entrent en vue qu'en rayon.</summary>
    private async Task SpawnSceneEntitiesAsync(NetSession session, EntryState st,
        CancellationToken token)
    {
        // Injecte les définitions S_Monster (HP/attaque/niveau réels),
        // la table des kfIds d'attaque Motion.ini et les masques
        // Locate S_Npc — fait au boot avant le préchauffage ; ce bind
        // reste ici en filet de sécurité (tests sans boot complet).
        _services.Live.BindGameData(_services.GameData);

        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var entities = _services.Live.EnsureNodePopulated(st.NodeId, st.InstanceGroup, scene);
        // Snapshot — même mutation concurrente que UpdateSightAsync.
        foreach (var e in entities.ToArray())
            st.HandleToEntity[e.Handle] = e.EntityId;
        // force:true rejoue toute la vue — la passe joueurs incluse
        // dans UpdateSightAsync pousse aussi mon 265 aux watchers
        // (AnnounceSelfToWatchersAsync, garde ost.Sent anti-doublon).
        await UpdateSightAsync(session, st, token, force: true);
        EdenLog.Write($"[Zone-Entry] scene S{st.NodeId:D3}: " +
            $"{entities.Count} entités, {st.Sent.Count} en vue");
    }

    /// <summary>CNC_CZ_ClientSeeMob (298) — format désassemblé + suffixe
    /// capturé par type (NPC c2=8 / monstre c2=12). Les 4 floats du
    /// corps sont <b>x, y(ground), dir, moveSpeed</b> — PAS x,y,z,dir :
    /// le ctor natif (@0x849df90, appelé depuis CMonster::OnSeen
    /// @0x849e100) écrit +0x14 = entité+0x58 (dir) et +0x18 =
    /// GetMovement (vtable+0xd8). Capture : NPC 50260 scène
    /// (369.885,275.426,facing 5.49779) → floats
    /// [369.885,275.426,5.4978,0.0]. La hauteur n'est JAMAIS
    /// transmise — le client la déduit du terrain. <paramref name="z"/>
    /// est conservé pour la signature mais n'entre pas dans le paquet.
    /// Le suffixe monstre porte le HP courant/max du mob capturé
    /// (0xE2=226 aux offsets 2 et 11 du tail) : on y injecte le HP
    /// réel du LiveMob — c'est la valeur de base que le client
    /// utilise pour sa barre de vie avant les 280.</summary>
    internal static CNC_CZ_ClientSeeMob BuildSeeMob(uint handle, int entityId,
        bool isMonster,
        float x, float y, float z, float facing, int hp = 0,
        float moveSpeed = 0f,
        short? entityIdOverride = null, string? displayName = null,
        int ownerCharId = 0, int maxPatience = 100)
    {
        // Valeurs des champs optionnels issues des tails capturés :
        // NPC : hp=10, unk34=10, unk4c=100, unk44=-2, unk50=0.
        // Mob : hp=live, unk34=180, unk4c=100, unk44=-1 (unk50 reste
        // au défaut 1 → bit8 absent, capture 0x3C57 vs NPC 0x3D57).
        // Elf pet (OnSeen natif @0x0849e100, branche IsA==0x50b) :
        // +0x50 = viewer->IsMyEnemy = 0 TOUJOURS (CCharacter::
        // IsMyEnemy @0x0840e490 — le pet n'est jamais rouge ni
        // ciblable) ; +0x48 = GetBossID = charId du maître
        // (CPetController::GetBossID @0x084a5f40).
        var isPet = isMonster && entityIdOverride == -2537;
        return new CNC_CZ_ClientSeeMob
        {
            Handle = handle,
            // ElfPet natif : template fixe -2537 (CNode::BornElfPet
            // @0x084b5d30 — clé -0x9e9) + nom du pet (+0x414) — c'est
            // ce qui rend le pet non-sélectionnable côté client.
            EntityId = entityIdOverride ?? (short)entityId,
            DisplayName = displayName ?? "",
            // Wire float3 = dir (entité+0x58 natif), float4 =
            // GetMovement — le champ Z du .g.cs transporte la dir et
            // le champ Facing la vitesse (ordre natif du ctor
            // @0x849df90). Avant ce fix les NPC recevaient z=0 comme
            // dir → tous tournés pareil (bug live).
            X = x, Y = y, Z = facing, Facing = moveSpeed,
            Kind = isMonster ? (byte)12 : (byte)8,
            Hp = hp, MaxHp = hp,
            Unk34 = isMonster ? 180 : 10,
            Unk38 = isMonster ? 180 : 10,   // défaut = cascade Unk34
            // +0x4c = CCharFormula::MaxPatience (CMonster::OnSeen
            // @0x0849e100 l.~, ctor @0x0849df90) — 100 par défaut
            // (capture), patience S_Monster col 13 pour un mob BP.
            // 0 omis du fil (bit4 absent) = mob sans barre.
            Unk4C = isMonster ? maxPatience : 100,
            Unk44 = isMonster ? -1 : -2,
            Unk48 = isPet ? ownerCharId : 0,  // +0x48 = owner (pet)
            Unk50 = isMonster && !isPet ? (byte)1 : (byte)0,
            Unk58 = 0f, Unk5C = 0f, Unk60 = 0f,
            Unk64 = 0,
        };
    }
}
