using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Combat;

namespace Eden.Zone;

/// <summary>
/// NCID 2 <c>CNE_CZ_ZoneServerWhoIs</c> — inspect/whois natif.
/// Désasm <c>CNE_CZ_ZoneServerWhoIs::Execute</c> zone @0x082a1340 :
/// le demandeur cible un charId sur son node ; si
/// <c>observer-&gt;CanSee(target)</c> →
/// <c>target-&gt;OnDisplayTo(observer, isGm)</c> @0x084187e0, qui
/// émet <b>263 ClientDisplayChar</b> (fiche complète — voir
/// <see cref="ClientDisplayCharCodec"/>), puis <b>301
/// ClientEnchantUpdate</b> si des enchants sont à jour, puis <b>326
/// ClientMovePrediction</b> quand le perso a bougé depuis sa
/// dernière annonce. Sinon → 410 ClientCommandFailReturn.
/// Le byte <c>IsGm</c> de la requête pilote <c>param_2</c> : à 0 le
/// natif n'envoie que le 326.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>NCID 2 [i32 charId][u8 flag] — inspect du perso
    /// visible ; la fiche n'est construite que si la cible est déjà
    /// dans la vue du demandeur (<c>st.Sent</c> = l'équivalent de la
    /// CanSee-list native).</summary>
    private async Task HandleWhoIsAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 5) return;
        var targetId = BitConverter.ToInt32(payload);
        var flag = payload[4];
        var target = _services.Sessions.FindByCharacter(targetId);
        if (target == null ||
            !_states.TryGetValue(target.SessionId, out var ost) ||
            !ost.Migrated || ost.CharId == 0 ||
            !st.Sent.ContainsKey((uint)ost.CharId))
        {
            // CanSee natif en échec → 410 ClientCommandFailReturn
            // [i32 ncid entrant = 2][i32 charId de la requête]
            // (ctor natif @0x082a13xx dans WhoIs::Execute).
            await session.SendAsync(new CNC_CZ_ClientCommandFailReturn
            { Field1 = 2, Field2 = targetId }, token);
            return;
        }
        if (flag != 0)
        {
            var body = await BuildDisplayCharBodyAsync(ost, token);
            await session.SendPacketAsync(263, body, token);
            await SendCostumePagesAsync(session, ost, token);
            var ench = BuildEnchantUpdate(ost);
            if (ench != null)
                await session.SendAsync(ench, token);
        }
        // 326 MovePrediction — le natif ne l'émet que si le perso a
        // bougé (dist² > speed²) ; on l'envoie comme au sight (le
        // client ignore une prédiction stationnaire).
        await session.SendAsync(BuildMovePrediction(
            (uint)ost.CharId, ost.PosX, ost.PosZ, ost.PosX, ost.PosZ,
            (byte)ost.MoveFlag, ost.Facing), token);
        EdenLog.Write($"[Zone] WhoIs char={st.CharId} -> {targetId} " +
            $"flag={flag} (263 émis)");
    }

    /// <summary>Assemble le snapshot de la cible et délègue au codec
    /// 263 — équipement lu en DB (containers 0/1 = les deux sets de
    /// la map char+0x4fc native).</summary>
    private async Task<byte[]> BuildDisplayCharBodyAsync(
        EntryState ost, CancellationToken token)
    {
        var atkLv = (byte)Math.Clamp(
            (ost.Combat?.EquipAttackSpeed ?? 0) * 10 / 15, 0, 255);
        var equips0 = await SlotEquipMapAsync((int)ost.CharId, 0, token);
        var equips1 = await SlotEquipMapAsync((int)ost.CharId, 1, token);
        var guild = _services.Guilds.GetGuildOf((int)ost.CharId);
        var rideKey = 0u;
        if (_services.RideStates.TryGetValue((int)ost.CharId,
                out var ride))
            rideKey = (uint)(_services.Enchants
                .GetRideInfo(ride.EnchantId)?.RideKey ?? 0);
        var snap = new ClientDisplayCharCodec.DisplayCharSnapshot(
            CharId: ost.CharId, Name: ost.CharName,
            X: ost.PosX, Z: ost.PosZ, Facing: ost.Facing,
            MoveSpeed: NativeCombatFormulas.BaseMovement
                * NativeCombatFormulas.TempMoveSpeedMul,
            AttackSpeedLevel: atkLv,
            Flags: ClientSeeCharCodec.LifeFlags(
                dead: ost.PlayerHp <= 0,
                mounted: _services.RideStates
                    .ContainsKey((int)ost.CharId),
                invincible: ost.MigrationInvincibleUntil
                    > DateTime.UtcNow),
            Level: ost.Level, ClassId: ost.ClassId,
            ClassLevel: ost.ClassLevel,
            Appearance32: ost.Appearance,
            Hp: ost.PlayerHp, MaxHp: ost.PlayerMaxHp,
            MaxMp: ost.PlayerMaxMp,
            Scale290: 100,                     // +0x290 — échelle (défaut)
            RideKey: rideKey,                  // +0x7f0 — clé monture
            TransformTpl: 0,                   // +0x28c — transform non porté
            Equips0: equips0, Equips1: equips1,
            ModelRev: _services.ModelVersions
                .GetValueOrDefault((int)ost.CharId),
            EnchantRev: 0,
            FamilyRev: (byte)_services.Guilds
                .GetFamilyRevision((int)ost.CharId),
            AppellationRev: ost.TitleSeq,
            DoRev: 0,
            FamilyId: (uint)(guild?.Id ?? 0),
            FamilyName: guild?.Name ?? string.Empty,
            CastingSpell: ost.CastingSpell,
            AppellationId: ost.ActiveTitle,
            Passengers: Array.Empty<uint>(),   // +0x7fc — co-ride non porté
            StallTitle: _services.Stalls
                .GetStall((int)ost.CharId)?.Title ?? string.Empty,
            TerritoryTeam: (short)ost.TerritoryCamp,
            MixElfId: ost.MixElfTemplateId,
            Versus: (short)ost.VersusCamp, VersusTeam: 0);
        return ClientDisplayCharCodec.Build(in snap);
    }

    /// <summary>A6.6 — le 263 ne décrit que les sets 0/1 : les pages
    /// costume (conteneurs 2-4, locs 1-5) sont poussées au demandeur
    /// sous forme de 276 ClientChangeEquip (même corps que le delta
    /// broadcasté à l'équipe) pour que le client distant les fusionne
    /// au rendu.</summary>
    private async Task SendCostumePagesAsync(NetSession session,
        EntryState ost, CancellationToken token)
    {
        var rev = _services.ModelVersions.GetValueOrDefault((int)ost.CharId);
        foreach (var container in CostumePageContainers)
        {
            var items = await _services.InventoryRepo
                .GetEquipmentEntitiesAsync((int)ost.CharId, container);
            foreach (var it in items)
            {
                if (!IsCostumeLoc(it.Slot)) continue;
                var body = Codecs.NativeItemCodec.BuildChangeEquip(
                    (int)ost.CharId, (ushort)it.Slot, (short)container,
                    rev, it);
                await session.SendPacketAsync(276, body, token);
            }
        }
    }

    internal static readonly int[] CostumePageContainers = { 2, 3, 4 };
    internal static bool IsCostumeLoc(int slot) => slot is >= 1 and <= 5;

    /// <summary>Container d'équipement → tableau par slot 0..10 (map
    /// char+0x4fc native, clé = container_index).</summary>
    private async Task<Eden.Core.Game.Inventory.ItemEntity?[]>
        SlotEquipMapAsync(int charId, int container,
            CancellationToken token)
    {
        var slots = new Eden.Core.Game.Inventory.ItemEntity?[11];
        var items = await _services.InventoryRepo
            .GetEquipmentEntitiesAsync(charId, container);
        token.ThrowIfCancellationRequested();
        foreach (var it in items)
            if (it.Slot is >= 0 and < 11) slots[it.Slot] = it;
        return slots;
    }

    /// <summary>301 ClientEnchantUpdate de la cible — le natif ne
    /// l'émet que si <c>GetUpdateEnchants</c> est non vide ; corps
    /// généré [i32 handle][u8 n][×n enchant][4B flags][u8 rev]
    /// (Serialize @0x0822fb70, ctor @0x08230xxx).</summary>
    private CNC_CZ_ClientEnchantUpdate? BuildEnchantUpdate(
        EntryState ost)
    {
        // Statuts à icône cachée (rest -9, talents AA…) : jamais vus des
        // témoins — même filtre que HandleEnchantUpdateAsync (NCID 18) et
        // que le gate natif du 299 (IsHideIcon).
        if (!ost.Statuses.Values.Any(s => !s.Hidden)) return null;
        var msg = new CNC_CZ_ClientEnchantUpdate
        {
            Field1 = (int)ost.CharId,
            Flags = BitConverter.ToInt32(
                ClientSeeCharCodec.LifeFlags(
                    dead: ost.PlayerHp <= 0,
                    mounted: _services.RideStates
                        .ContainsKey((int)ost.CharId),
                    invincible: ost.MigrationInvincibleUntil
                        > DateTime.UtcNow)),
            B280 = 0,
        };
        foreach (var status in ost.Statuses.Values)
        {
            if (status.Hidden) continue;
            msg.Entries.Add(new CNC_CZ_ClientEnchantUpdate.EnchantEntry
            {
                SpellId = (short)status.EffectId,
                CasterId = status.CasterId,
                RestTime = status.RestSeconds,
                Stacks = (short)status.Level,
            });
        }
        return msg;
    }
}
