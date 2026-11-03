using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Eden.Core.Networking;

namespace Eden.Bots;

/// <summary>Session de banc : ticket frais → handshake zone → rejeu
/// d'entrée → écoute des spawns/actions. Ne prétend pas simuler un client
/// complet ; elle charge les chemins serveur déjà prouvés.</summary>
public sealed class BotSession
{
    private readonly BotOptions _options;
    private readonly BotCharacter _character;
    private readonly IReadOnlyList<byte[]> _entryFrames;
    private readonly BotResult _result;
    private BotTransport? _zone;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private long _entryStarted;
    private float _x = 332.187988f;
    private float _z = 298.277008f;
    private int _moveStep;

    public BotSession(BotOptions options, BotCharacter character,
        IReadOnlyList<byte[]> entryFrames)
    {
        _options = options;
        _character = character;
        _entryFrames = entryFrames;
        _result = new BotResult
        {
            CharacterId = character.Id,
            CharacterName = character.Name,
        };
    }

    public async Task<BotResult> RunAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        _entryStarted = started;
        try
        {
            var ticket = await GetTicketAsync(ct);
            _zone = await BotTransport.ConnectAsync(_options.Host,
                _options.ZonePort, ct, _options.Transport,
                _options.TlsThumbprint, _options.TlsInsecure);
            // Handshake de version (phase 4d — transport modern
            // seulement) : le gate zone exige VersionHello en première
            // trame ; --no-version simule le vieux client et attend le
            // rejet 0xFFFD.
            if (_options.Transport == "legacy")
            {
                // pas de gate en legacy — protocole natif direct.
            }
            else if (_options.NoVersion)
            {
                await SendAsync(ticket, ct);
                await ExpectVersionRejectAsync(ct);
                return _result;
            }
            else
            {
                var reply = VersionProtocol.ParseReply(
                    await _zone.VersionHandshakeAsync(
                        _options.ClientVersion,
                        _options.ManifestHashHex, ct));
                if (reply == null)
                    throw new InvalidDataException(
                        "première trame post-handshake n'est pas une " +
                        "réponse de version (gate absent ?)");
                if (!reply.Accepted)
                {
                    _result.VersionRejected = true;
                    _result.Error =
                        $"version rejetée ({reply.Reason})" +
                        (reply.MinClientVersion.Length > 0
                            ? $" — min {reply.MinClientVersion}" : "");
                    return _result;
                }
            }
            await SendAsync(ticket, ct);
            if (!_options.NoEntry)
                foreach (var frame in _entryFrames)
                    await SendAsync(frame, ct);
            Console.WriteLine($"[bot] ready char={_character.Id} " +
                $"name={_character.Name} scenario={_options.Scenario}");
            StartReceiveLoop(ct);
            await Task.Delay(TimeSpan.FromSeconds(
                _options.SpawnWaitSeconds), ct);
            _result.EntryMs = _result.EntryMs > 0
                ? _result.EntryMs
                : Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            await RunActionsAsync(ct);
            await StopReceiveLoopAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { throw; }
        catch (Exception ex)
        {
            _result.Error = ex.Message;
            try { await StopReceiveLoopAsync(); } catch { }
        }
        return _result;
    }

    private async Task<byte[]> GetTicketAsync(CancellationToken ct)
    {
        await using var ticket = await BotTransport.ConnectAsync(
            _options.Host, _options.TicketPort, ct, _options.Transport,
            _options.TlsThumbprint, _options.TlsInsecure);
        var body = new byte[18];
        BinaryPrimitives.WriteInt32LittleEndian(body, _character.Id);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 0);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(16), 0);
        await ticket.SendAsync(0, body, ct);
        var response = await ticket.ReceiveAsync(ct)
            ?? throw new EndOfStreamException("TicketServer a fermé");
        if (response.Length < 28 ||
            BinaryPrimitives.ReadUInt16LittleEndian(response) != 2)
            throw new InvalidDataException(
                $"TicketServer réponse inattendue len={response?.Length}");
        var body22 = new byte[22];
        Buffer.BlockCopy(response, 2, body22, 0, 4);
        Buffer.BlockCopy(response, 10, body22, 4, 18);
        return BotPackets.ZoneTicket(body22);
    }

    /// <summary>Chemin <c>--no-version</c> : après le ticket envoyé à
    /// nu, la première trame doit être le rejet du gate
    /// (<c>0xFFFD reason=1 UpdateRequired</c>) — le serveur ferme
    /// ensuite la session.</summary>
    private async Task ExpectVersionRejectAsync(CancellationToken ct)
    {
        var first = await _zone!.ReceiveAsync(ct);
        if (first == null)
        {
            _result.Error = "connexion fermée sans trame de rejet " +
                "(gate inactif ?)";
            return;
        }
        var reply = VersionProtocol.ParseReply(first);
        if (reply is { Accepted: false })
        {
            _result.VersionRejected = true;
            _result.Error = $"version rejetée ({reply.Reason})";
            if (reply.Reason == VersionProtocol.RejectReason.UpdateRequired)
                _result.Error += " — attendu";
        }
        else
            _result.Error = "pas de rejet version (première trame NCID=" +
                $"{BinaryPrimitives.ReadUInt16LittleEndian(first)})";
    }

    private void StartReceiveLoop(CancellationToken ct)
    {
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _receiveCts.Token;
        _receiveTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var payload = await _zone!.ReceiveAsync(token);
                    if (payload == null) break;
                    ObserveInbound(payload);
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _result.Error ??= $"recv: {ex.Message}";
                    break;
                }
            }
        }, token);
    }

    private void ObserveInbound(byte[] payload)
    {
        _result.Received++;
        var ncid = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        _result.InboundNcids.AddOrUpdate(ncid, 1, (_, n) => n + 1);
        if (ncid == 473)
        {
            _result.Entered = true;
            _result.EntryMs = _result.EntryMs > 0
                ? _result.EntryMs
                : Stopwatch.GetElapsedTime(_entryStarted)
                    .TotalMilliseconds;
        }
        if (ncid == 298 && payload.Length >= 26)
        {
            // Spawn 298 natif : [u32 handle][s16 eid][u8][f32 x][f32 z]
            // [f32 facing][f32 movespeed][u8 kind=8 npc / 12 mob]…
            var handle = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(2));
            var spot = new EntitySpot(
                BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(9)),
                BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(13)));
            _result.Entities[handle] = spot;
            if (payload[25] == 12)
                _result.Mobs[handle] = spot;
        }
        else if (ncid == 326 && payload.Length >= 10)
        {
            // MovePrediction 326 : [i32 handle][u16 x×16][u16 z×16]…
            var handle = (uint)BinaryPrimitives.ReadInt32LittleEndian(
                payload.AsSpan(2));
            var spot = new EntitySpot(
                BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(6)) / 16f,
                BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(8)) / 16f);
            _result.Entities.TryAdd(handle, spot);
            if (_result.Mobs.ContainsKey(handle))
                _result.Mobs[handle] = spot;
        }
        else if (ncid == 318 && payload.Length >= 6 &&
            _options.Scenario == "team")
        {
            // Invite d'équipe entrante [i32 inviterId][str name] —
            // scénario « team » : accepter (30 [s16 1][i32 inviter]
            // [u8 1][s16 0]) pour prouver le flux invite live.
            var inviter = BinaryPrimitives.ReadInt32LittleEndian(
                payload.AsSpan(2));
            _ = SendAsync(BotPackets.TeamInviteAnswer(inviter),
                CancellationToken.None)
                .ContinueWith(t => _result.Error ??=
                    $"invite-answer: {t.Exception?.Message}",
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        else if (ncid == 329 && payload.Length >= 14)
            // SeeTreasure 329 : [u32 id][f32 x][f32 z]…
            _result.Treasures[
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2))] =
                new EntitySpot(
                    BinaryPrimitives.ReadSingleLittleEndian(
                        payload.AsSpan(6)),
                    BinaryPrimitives.ReadSingleLittleEndian(
                        payload.AsSpan(10)));
    }

    private async Task RunActionsAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(1.0 / _options.ActionsPerSecond);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(
            _options.DurationSeconds);
        var rng = Random.Shared;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            foreach (var packet in NextAction(rng))
                await SendAsync(packet, ct);
            await Task.Delay(interval, ct);
        }
    }

    private IEnumerable<byte[]> NextAction(Random rng)
    {
        var scenario = _options.Scenario;
        if (scenario == "entry") { _result.Echoes++;
            yield return BotPackets.Echo(); yield break; }
        if (scenario == "team") yield break;   // idle — répond aux 318
        // Ciblage par proximité : le natif (CanAttack) n'inflige des
        // dégâts qu'à portée réelle — on s'approche d'abord du mob le
        // plus proche, on ne frappe que ce qui est atteignable.
        var nearest = NearestMob();
        if (scenario is "move" or "mixed" or "aoe")
            yield return nearest is { } m && Dist(m) > AttackRange
                ? StepToward(m) : MovePacket(rng);
        if (scenario is "combat" or "spell" or "aoe" or "mixed")
            foreach (var h in PickMobs(rng, scenario == "aoe" ? 10 : 1))
            {
                if (scenario == "spell" ||
                    (scenario == "mixed" && rng.Next(2) == 0))
                { _result.Spells++; yield return BotPackets
                    .SpellCastToLife((ushort)_character.SpellId, h); }
                else
                { _result.Attacks++; yield return BotPackets
                    .MeleeAttack(h); }
            }
        if (scenario is "loot" or "mixed")
            foreach (var t in _result.Treasures.Keys.Take(1))
            { _result.LootAttempts++;
                yield return BotPackets.TreasurePickup(t); }
    }

    /// <summary>Portée d'attaque supposée côté bench (arme de poing
    /// des botchars) — au-delà, CanAttack natif ne produit aucun coup.</summary>
    private const float AttackRange = 3f;

    /// <summary>Mob vivant le plus proche de la position bot courante.</summary>
    private EntitySpot? NearestMob()
    {
        EntitySpot? best = null;
        var bestD = float.MaxValue;
        foreach (var (handle, spot) in _result.Mobs)
        {
            var dx = spot.X - _x; var dz = spot.Z - _z;
            var d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = spot; }
        }
        return best;
    }

    private float Dist(EntitySpot s)
    {
        var dx = s.X - _x; var dz = s.Z - _z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Un pas de MoveSelf droit vers la cible (pas de
    /// pathfinding — suffisant en champ ouvert).</summary>
    private byte[] StepToward(EntitySpot s)
    {
        _result.Moves++;
        var d = MathF.Max(Dist(s), 0.001f);
        var step = MathF.Min(6f, d - AttackRange * 0.5f);
        var targetX = _x + (s.X - _x) / d * step;
        var targetZ = _z + (s.Z - _z) / d * step;
        var facing = MathF.Atan2(targetZ - _z, targetX - _x);
        var packet = BotPackets.MoveSelf(_x, _z, targetX, targetZ,
            float.IsFinite(facing) ? facing : 0f);
        _x = targetX; _z = targetZ;
        _moveStep++;
        return packet;
    }

    private byte[] MovePacket(Random rng)
    {
        _result.Moves++;
        var targetX = _x + (rng.NextSingle() * 6f - 3f);
        var targetZ = _z + (rng.NextSingle() * 6f - 3f);
        var facing = MathF.Atan2(targetZ - _z, targetX - _x);
        var packet = BotPackets.MoveSelf(_x, _z, targetX, targetZ,
            float.IsFinite(facing) ? facing : 0f);
        _x = targetX; _z = targetZ;
        _moveStep++;
        return packet;
    }

    private IEnumerable<uint> PickMobs(Random rng, int count)
    {
        // Mobs à portée d'abord (tri par distance) — sinon n'importe
        // quelle entité vue, comme avant.
        var inRange = _result.Mobs
            .Where(kv => Dist(kv.Value) <= AttackRange)
            .OrderBy(kv => Dist(kv.Value))
            .Select(kv => kv.Key).Take(count).ToArray();
        if (inRange.Length > 0)
        {
            foreach (var h in inRange) yield return h;
            yield break;
        }
        var mobs = (_result.Mobs.IsEmpty
            ? _result.Entities.Keys
            : _result.Mobs.Keys).ToArray();
        for (var i = 0; i < Math.Min(count, mobs.Length); i++)
            yield return mobs[rng.Next(mobs.Length)];
    }

    private async Task SendAsync(byte[] payload, CancellationToken ct)
    {
        await _zone!.SendPayloadAsync(payload, ct);
        _result.Sent++;
    }

    private async Task StopReceiveLoopAsync()
    {
        if (_receiveCts == null) return;
        await _receiveCts.CancelAsync();
        if (_receiveTask != null)
            try { await _receiveTask; } catch { }
        _receiveCts.Dispose();
    }
}
