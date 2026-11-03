using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;

namespace Eden.Zone;

/// <summary>
/// Visibilité joueur ↔ joueur — <c>CNC_CZ_ClientSeeChar</c> (265)
/// et son annonce. Natif : chaque <c>CCharacter</c> cache son paquet
/// de présentation (<c>char+0x7d8</c>, construit dans
/// <c>MoveSelf</c>/<c>Goto</c> @0x0844aae0/0x0844c010) et le pousse
/// aux entités qui entrent dans son CanSee, avec le 326
/// MovePrediction caché (<c>char+0x7d4</c>). Handle = <b>charId</b>
/// (<c>char+0x34</c> — log natif "Character ID = [%d]"). Le corps
/// est sérialisé par <see cref="ClientSeeCharCodec"/> (le .g.cs
/// aplati est inutilisable — PutBinary + i64 absents).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Construit le snapshot du perso et délègue au codec —
    /// mapping <c>EntryState</c> → champs natifs (la vitesse de buff
    /// n'est pas trackée côté EntryState : BaseMovement, et le niveau
    /// de vitesse d'attaque est ramené à l'échelle 0-10 du client
    /// via ×10/15 comme le natif).</summary>
    private byte[] BuildSeeCharBody(EntryState st)
    {
        var atkLv = (byte)Math.Clamp(
            (st.Combat?.EquipAttackSpeed ?? 0) * 10 / 15, 0, 255);
        var snap = new ClientSeeCharCodec.SeeCharSnapshot(
            CharId: (uint)st.CharId,
            X: st.PosX, Z: st.PosZ, Facing: st.Facing,
            MoveSpeed: NativeCombatFormulas.BaseMovement
                * NativeCombatFormulas.TempMoveSpeedMul,
            AttackSpeedLevel: atkLv,
            Flags: ClientSeeCharCodec.LifeFlags(
                dead: st.PlayerHp <= 0,
                mounted: _services.RideStates.ContainsKey((int)st.CharId),
                invincible: st.MigrationInvincibleUntil
                    > DateTime.UtcNow),
            Level: st.Level, ClassLevel: st.ClassLevel,
            Hp: st.PlayerHp, MaxHp: st.PlayerMaxHp,
            MaxMp: st.PlayerMaxMp,
            ModelRev: _services.ModelVersions
                .GetValueOrDefault((int)st.CharId),
            CastingSpell: (short)st.CastingSpell,
            StallTitle: _services.Stalls
                .GetStall((int)st.CharId)?.Title ?? string.Empty);
        return ClientSeeCharCodec.Build(in snap);
    }

    /// <summary>Pousse le 265+326 du perso aux observateurs en rayon
    /// du même (node,instance) — le natif fait l'aller-retour OnSeen :
    /// ici l'arrivant (ou le mobile) annonce sa présentation aux
    /// sessions qui l'ont dans leur carré de vue. <c>ost.Sent[charId]
    /// </c> est posé pour que leur propre UpdateSightAsync ne
    /// ré-annonce pas (et gère l'éradication 269 à la sortie de
    /// vue).</summary>
    private async Task AnnounceSelfToWatchersAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var body = BuildSeeCharBody(st);
        var (mpOp, mpPayload) = PreparePacket(
            BuildMovePrediction((uint)st.CharId, st.PosX, st.PosZ,
                st.PosX, st.PosZ, (byte)st.MoveFlag, st.Facing));
        var h = (uint)st.CharId;
        foreach (var s in _services.Sessions.All)
        {
            if (s.SessionId == session.SessionId ||
                !_states.TryGetValue(s.SessionId, out var ost) ||
                !ost.Migrated || ost.CharId == 0 ||
                ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            var dx = Math.Abs(ost.PosX - st.PosX);
            var dy = Math.Abs(ost.PosZ - st.PosZ);
            if (dx > SightHalf || dy > SightHalf ||
                ost.Sent.ContainsKey(h)) continue;
            ost.Sent[h] = 0;
            ost.EntityPos[h] = (st.PosX, st.PosZ);
            try
            {
                await s.SendPacketAsync(265, body, token);
                var op = mpOp;
                if (op >= s.OutboundNcidShiftAbove && op <= 83) op++;
                await s.SendPacketAsync(op, mpPayload, token);
            }
            catch { ost.Sent.TryRemove(h, out _); }
        }
    }
}
