using Eden.Core;
using Eden.Core.Logging;
using Eden.Zone.Combat;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>MoveSelf — wire natif désassemblé
    /// (CNE_CZ_ZoneServerMoveSelf::Deserialize @0x826b…) :
    ///   `[s16 n][n×(f32 wpX,f32 wpZ)]   — chemin de waypoints
    ///    [f32 curX][f32 curZ][f32 tgtX][f32 tgtZ][f32 dir][s16 flag]`
    /// n=0 → body 24B (ZQSD, déplacement direct), n≥1 → clic-gauche
    /// pathfindé. Lire n en i32 ou oublier le vecteur décalait les 5
    /// floats → position fantôme (ex. clic-move → PosZ=162 → sight vide
    /// d'un coup, entités jamais ré-affichées).
    /// Execute : CheckCastBreak + NotifyClient(264) +
    /// BroadCastToCanSee(326 aux AUTRES). Le client pilote son
    /// déplacement — le serveur ne fait qu'accuser réception.</summary>
    private async Task HandleMoveSelfAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 24 || st.CharId == 0) return;
        var n = BitConverter.ToInt16(payload, 0);   // s16 : nb waypoints
        if (n < 0 || n > 64) return;
        var o = 2 + n * 8;                          // saute le chemin
        if (payload.Length < o + 22) return;
        var f = new float[5];
        for (var i = 0; i < 5; i++)
        { f[i] = BitConverter.ToSingle(payload, o); o += 4; }
        // f = curX, curZ, tgtX, tgtZ, dir — s16 flag = type de
        // mouvement (0/3/5/6/7/8 vus en live). Le natif le stocke
        // char+0x54 puis le recopie en u8 dans le 326 broadcast —
        // sans lui les observateurs voient toujours la course avant
        // (anim strafe/monture absente).
        st.MoveFlag = BitConverter.ToInt16(payload, o);

        // Refus de mouvement natif (MoveSelf @0x8111cb0 — virtuel
        // char+0x70 CanMove==0 ou speed==0, log "Can't Move!!") :
        // 270 ClientResetCoord [f32 x][f32 z] = position autoritaire
        // char+0x28/+0x2c au client + 326 MovePrediction de cette
        // même position broadcasté, puis return (pas de 264, pas de
        // prise de la position client). StateNoMove ↔ NoMove lock.
        if ((GetStateLocks(st) & StatusLocks.NoMove) != 0)
        {
            await session.SendAsync(new CNC_CZ_ClientResetCoord
            {
                Field1 = st.PosX, Field2 = st.PosZ,
            }, token);
            await BroadcastMoveAsync(session, st, st.PosX, st.PosZ,
                token);
            return;
        }

        // Anti-triche vitesse (CheatEngine/speedhack) : la distance
        // parcourue depuis la dernière position autoritaire ne peut
        // dépasser vitesse serveur × temps écoulé (+ marge de lag).
        // Dépassement → 270 ResetCoord vers la position serveur.
        var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (st.LastMoveTicks != 0)
        {
            var dt = Math.Min(
                (nowTicks - st.LastMoveTicks)
                    / (double)System.Diagnostics.Stopwatch.Frequency, 10.0);
            var maxSpeed = Math.Max(
                st.Combat != null
                    ? NativeCombatFormulas.Movement(st.Combat) : 0f,
                NativeCombatFormulas.BaseMovement
                    * NativeCombatFormulas.TempMoveSpeedMul);
            var allowed = maxSpeed * 1.5 * dt + 5.0;
            var dx = f[0] - st.PosX; var dz = f[1] - st.PosZ;
            if (Math.Sqrt(dx * dx + dz * dz) > allowed)
            {
                if (Eden.Core.Logging.LogThrottle.Allow($"move-{st.CharId}", 5)) EdenLog.Write($"[Move] char={st.CharId} vitesse refusée " +
                    $"d={Math.Sqrt(dx * dx + dz * dz):F1} max={allowed:F1}");
                st.LastMoveTicks = nowTicks;
                await session.SendAsync(new CNC_CZ_ClientResetCoord
                {
                    Field1 = st.PosX, Field2 = st.PosZ,
                }, token);
                await BroadcastMoveAsync(session, st, st.PosX, st.PosZ,
                    token);
                return;
            }
        }
        st.LastMoveTicks = nowTicks;

        // Murs invisibles natifs (sknodemap/Sxxx.smp) : même règle que le
        // client (PlayerController.ClipByNodeMap) — on refuse d'ENTRER
        // dans une case obstacle depuis une case libre ; déjà coincé
        // (spawn, //tele) = jamais bloqué. Coupe : EDEN_NODEMAP=0.
        if (EdenConfig.NodeMapEnforce)
        {
            var code = $"S{st.NodeId:D3}";
            if (_services.Scenes.IsNodeBlocked(code, f[0], f[1]) &&
                !_services.Scenes.IsNodeBlocked(code, st.PosX, st.PosZ))
            {
                EdenLog.Write($"[Move] char={st.CharId} mur invisible " +
                    $"{code} ({f[0]:F1},{f[1]:F1})");
                await session.SendAsync(new CNC_CZ_ClientResetCoord
                {
                    Field1 = st.PosX, Field2 = st.PosZ,
                }, token);
                await BroadcastMoveAsync(session, st, st.PosX, st.PosZ,
                    token);
                return;
            }
        }
        st.PosX = f[0]; st.PosZ = f[1];
        await Gather.OnMovedAsync((int)st.CharId, st.PosX, st.PosZ);
        st.Facing = f[4];
        // Tout déplacement lève la position assise (état 0xf natif
        // retiré par le mouvement) — sinon la regen resterait ×4.
        st.Sitting = false;

        // 264 ClientMoveChar [f32 x][f32 z] — l'unique accusé natif
        // (capture : un seul 264 par MoveSelf, jamais autre chose).
        await session.SendAsync(new CNC_CZ_ClientMoveChar
        {
            Field1 = f[0], Field2 = f[1],   // f32 x, f32 z
        }, token);

        // CAST MOBILE (mécanique custom demandée — non native) :
        // le joueur peut bouger pendant tout cast-timer (heal, dps,
        // buff, debuff). Le CheckCastBreak natif (287 au déplacement)
        // est donc volontairement désactivé ici : sans 287 le client
        // laisse la barre de cast ouverte pendant la marche et le
        // sort se résout à l'échéance. Seule l'annulation manuelle
        // (NCID 13), un nouveau cast, la mort ou la déconnexion
        // brisent le cast. Les mobs, eux, restent soumis au break
        // (MobCastSpellAsync gère son propre pipeline).
        // if (st.CastingSpell != 0)
        //     await BreakCastAsync(session, st, token);

        // BroadCastToCanSee natif : 326 MovePrediction vers les AUTRES
        // sessions du node — jamais vers soi-même.
        await BroadcastMoveAsync(session, st, f[2], f[3], token);

        // Franchissement d'event-areas (grille spatiale de MoveSelf
        // natif) : aires REVIVE → revive_area_id, NORMAL →
        // last_normal_area_id — tir à l'entrée uniquement.
        await CheckAreaEntryAsync(session, st, token);

        // Write-behind : la dernière position est marquée sale, le
        // flush périodique (EDEN_PERSIST_FLUSH_MS) la persiste — un
        // UPDATE par paquet de mouvement saturerait la base
        // (SaveCharacterLocation natif est périodique aussi).
        _writeBehind.MarkCoordinates((int)st.CharId, st.NodeId,
            st.PosX, st.PosZ, st.PosY, st.Facing);

        await UpdateSightAsync(session, st, token);
    }

    /// <summary>326 ClientMovePrediction broadcast aux autres sessions du
    /// node — format capture (15B) :
    /// `[i32 handle][u16 x*16][u16 z*16][u8 0x07][u16 dir*16384]
    /// [u16 dstX*16][u16 dstZ*16]`. Le handle = charId (le client affiche
    /// le perso distant par son id de personnage).</summary>
    /// <summary>326 MovePrediction joueur vers les AUTRES sessions —
    /// format natif désasm Serialize @0x81xxxxx :
    /// `[i32 h][u16 x×16][u16 z×16][u8 flag][u16 speed|bit0=hasDst]
    /// [u16 dx×16][u16 dz×16]` — dst présent UNIQUEMENT si bit0 du champ
    /// speed = 1. Écrire facing×16384 à cet offset laissait bit0
    /// aléatoire → le client lisait 11B au lieu de 15B → stream
    /// désynchronisé (anims de course fantômes constatées en live).</summary>
    private async Task BroadcastMoveAsync(NetSession session, EntryState st,
        float dstX, float dstZ, CancellationToken token)
    {
        var (op, payload) = PreparePacket(BuildMovePrediction(st.CharId,
            st.PosX, st.PosZ, dstX, dstZ, (byte)st.MoveFlag, st.Facing));
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                // BroadCastToCanSee natif : le 326 ne part qu'aux
                // sessions qui ont le perso en vue (265 reçu) —
                // sinon le client instancierait un modèle fantôme
                // sans données de présentation.
                !ost.Sent.ContainsKey((uint)st.CharId)) continue;
            SendCosmeticTo(s, op, payload, MathF.Abs(ost.PosX - st.PosX) + MathF.Abs(ost.PosZ - st.PosZ));
        }
    }

    /// <summary>326 MovePrediction d'une entité serveur (mob qui court)
    /// — même format 15B que le broadcast joueur. Utilisé par l'IA mob
    /// quand la cible bouge (équivalent du 267+rattrapage natif).</summary>
    internal static Task SendMobMoveAsync(NetSession session, uint handle,
        float x, float z, float dir, float dstX, float dstZ,
        CancellationToken token)
        => session.SendAsync(
            BuildMovePrediction(handle, x, z, dstX, dstZ, 0x07, dir),
            token);

    /// <summary>326 MovePrediction d'un mob broadcast à TOUS les
    /// témoins du node — BroadCastToCanSee natif. La poursuite n'est
    /// visible que par la cible sinon : les autres joueurs voient un
    /// mob figé qui se téléporte.</summary>
    private async Task BroadcastMobMoveAsync(EntryState st, uint handle,
        float curX, float curZ, float dstX, float dstZ,
        CancellationToken token)
    {
        var (op, payload) = PreparePacket(
            BuildMovePrediction(handle, curX, curZ, dstX, dstZ));
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                // BroadCastToCanSee natif : uniquement les watchers
                // qui ont ce handle en vue (sinon modèle fantôme).
                !ost.Sent.ContainsKey(handle)) continue;
            SendCosmeticTo(s, op, payload, MathF.Abs(ost.PosX - curX) + MathF.Abs(ost.PosZ - curZ));
        }
    }

    /// <summary>Encode un 326 MovePrediction — 11B si pas de
    /// déplacement, 15B avec destination. Le champ u16@0xe =
    /// <b>facing en demi-flottant</b> (char+0x58, bit-twiddle natif du
    /// ctor dans `CPlayerFactory::MoveSelf` : sign&lt;&lt;15 |
    /// (exp-0x70&amp;0x1f)&lt;&lt;10 | mantissa&gt;&gt;13, bit0 forcé 0) ORé
    /// avec 1 quand un dst suit — c'est ce champ qui oriente le corps
    /// de la monture en strafe chez l'observateur. Le byte flag =
    /// type de mouvement recopié du CNE1 (char+0x54 natif) ; les mobs
    /// restent à 0x07 (valeur capture — leur IA n'a pas de strafe).
    /// `facing` NaN → déduit de la direction du mouvement (mobs :
    /// le facing natif suit le déplacement).</summary>
    private static CNC_CZ_ClientMovePrediction BuildMovePrediction(
        uint handle, float x, float z, float dstX, float dstZ,
        byte flag = 0x07, float facing = float.NaN)
    {
        var moving = Math.Abs(dstX - x) > 0.01f ||
            Math.Abs(dstZ - z) > 0.01f;
        if (float.IsNaN(facing))
            facing = moving ? NativeDir(dstX - x, dstZ - z) : 0f;
        return new CNC_CZ_ClientMovePrediction
        {
            Handle = (int)handle,
            CurX = (ushort)(x * 16f), CurZ = (ushort)(z * 16f),
            Flag = flag,
            Facing = FacingToWire(facing, moving), // u16 : half|hasDst
            DstX = moving ? (ushort)(dstX * 16f) : null,
            DstZ = moving ? (ushort)(dstZ * 16f) : null,
        };
    }

    /// <summary>dir natif (entité+0x58) pour un déplacement (Δx, Δy) :
    /// <c>ILifeEntity::ChangeFaceDir</c> (zone @0x0847a3b0) =
    /// <c>3π/2 − atan2(Δy, Δx)</c> (constante @0x08877088).
    /// TOUS les facing émis sur le fil (298 float3, 326, 441…) suivent
    /// cette convention, pas atan2 brut.</summary>
    internal static float NativeDir(float dx, float dy)
    {
        var d = 3f * MathF.PI / 2f - MathF.Atan2(dy, dx);
        var t = 2f * MathF.PI;
        d %= t;
        return d < 0 ? d + t : d;
    }

    /// <summary>Cap monde (angle depuis +X) d'un dir natif.</summary>
    internal static float NativeHeading(float dir) =>
        3f * MathF.PI / 2f - dir;

    /// <summary>float facing → u16 du 326 — réplique exacte du
    /// bit-twiddle natif (`(bits&gt;&gt;31)&lt;&lt;15 |
    /// ((bits&gt;&gt;23)-0x70 &amp; 0x1f)&lt;&lt;10 |
    /// (mantissa&gt;&gt;13) &amp; 0xfffe`, puis `|1` si destination).</summary>
    private static ushort FacingToWire(float facing, bool hasDst)
    {
        ushort w = 0;
        if (facing != 0f)
        {
            var b = (uint)BitConverter.SingleToInt32Bits(facing);
            w = (ushort)((b >> 31 << 15) |
                (((b >> 23) - 0x70 & 0x1f) << 10) |
                ((b & 0x7fffff) >> 13 & 0xfffe));
        }
        return (ushort)(w | (hasDst ? 1 : 0));
    }

    /// <summary>290 ClientPlayerAction — écho serveur d'une action de
    /// posture (sit/stand/spawn…). Fidèle au natif SimpleAction
    /// (zone/decompiled.c ~0x8116xxx) : NotifyClient à l'acteur PUIS
    /// BroadCastToCanSee aux sessions du même node — l'absence d'écho
    /// figeait le client (pas d'anim, déconnexion).
    /// Corps 16B : [u32 charId][u8 action][f32 char+0x58][u8 0][u8 0]
    /// [i32 cible][u8 1]. Le f32 natif lit char+0x58 (état d'action
    /// courant de l'entité) — on émet le code d'action en flottant,
    /// valeur que le client re-utilise comme état/serial.</summary>
    private async Task SendPlayerActionAsync(NetSession session,
        EntryState st, byte action, int target, CancellationToken token)
    {
        // Broadcast partagé : corps sérialisé une fois, rejoué brut
        // par session (le flag natif 0x12 reste à 1).
        var msg = new CNC_CZ_ClientPlayerAction
        {
            Field1 = st.CharId, Field2 = action,
            Field3 = action,                        // f32 — char+0x58
            Field6 = target, Field7 = 1,
        };
        var stream = new OutBinStream();
        msg.Serialize(stream);
        var b = stream.ToArray();
        await session.SendPacketAsync(290, b, token);
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId) continue;
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup ||
                // CanSee natif : pas d'action pour un perso que le
                // watcher n'a pas en vue (265 jamais reçu).
                !ost.Sent.ContainsKey((uint)st.CharId)) continue;
            try { await s.SendPacketAsync(290, b, token); }
            catch { /* session morte */ }
        }
    }
}
