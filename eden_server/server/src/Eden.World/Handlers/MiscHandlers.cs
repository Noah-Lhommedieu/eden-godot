using Eden.Core.Database.Repositories;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.World.Handlers;

/// <summary>
/// Commandes CW éparses côté monde (derniers entrants non couverts) :
/// <list type="bullet">
///   <item>19 <c>ServerQueryExploitRank</c> → 59
///   <c>ClientQueryExploitRank</c> — Serialize @ world 0x80ccd50 :
///   [u8][n×{s16 rank, str nom, s16 niveau, str famille, str}].</item>
///   <item>26 <c>ServerCaptchaReturn</c> — Execute natif = return
///   (anti-bot inerte) : no-op fidèle.</item>
///   <item>28 <c>ServerIllPluginInfo</c> — rapport anti-plugin
///   (10 champs) : natif purge une map interne, rien d'émis → log.</item>
///   <item>29 <c>ServerUpdateWorldServers</c> → 66
///   <c>ClientGetWorldServers</c> — Serialize @0x80cce00 :
///   [s16][map{s16 id,str nom,str ip,s16 port,i32,s16×5}][s16][u8] ;
///   données <c>worlds</c> edenfr_account (<c>GetWorldServerStates</c> :
///   SELECT id,name,ip,port,online_user,maxnum_user,state,
///   show_order).</item>
///   <item>30 <c>ServerCheckHasCharPassword</c> [i32 charId] → 75
///   [i32 flag] (<c>char_password</c> non vide).</item>
///   <item>31 <c>ServerCharPassword</c> [i32 charId][s16 op][str pw]
///   → 76 [s16 code] — <c>DoCharPassword</c> @0x8158c30 :
///   op 0 = vérifie (0 ok / -1 échec), op 3 = définit, autres →
///   code = op renvoyé.</item>
///   <item>8 <c>ServerLookingForMember</c> [s16×5 filtres] → 45
///   <c>ClientLookingForMemberResult</c> — Serialize @0x80cc5f0 :
///   [s16 n][n×{i32 id, str nom, s16 level(+0xc), s16 node_id(+0xe),
///   s16 race_id(+0x10), s16 class_id(+0x12)}] ; filtre natif
///   <c>LoadLFGCharacters</c> @0x8050ea0.</item>
///   <item>11 <c>ServerLookingForGroup</c> — 7 champs lus, l'Execute
///   n'appelle que <c>SetLFG(char, flag!=0)</c> @0x805bb00
///   (inscription/retrait de la map LFG).</item>
///   <item>20 <c>ServerChangeWorld</c> [i32 worldId] — recherche
///   <c>worlds</c>, <c>SaveLastWorld</c> si id%100≠0, ticket T_
///   → 65 <c>ClientReceiveTicketToWorld</c> (22 octets bruts —
///   Serialize @0x80cc570).</item>
///   <item>21 <c>ServerChangeWorldAck</c> — vide ; natif =
///   <c>AccountCrossLogoutAck</c> (billing gateway — inexistant ici :
///   le client coupe lui-même en migrant).</item>
/// </list>
/// </summary>
public static class MiscHandlers
{
    /// <summary>Entrée du registre LFG
    /// (<c>SCharacter</c> : id, nom, level, node_id, race_id,
    /// class_id).</summary>
    public sealed record LfgEntry(int CharId, string Name, short Level,
        short NodeId, short RaceId, short ClassId);

    /// <summary>Map LFG mondiale — équivalent de
    /// <c>CGlobalInfo::LFGCharacters</c> (rb_tree char_id→SCharacter*).
    /// In-memory comme le natif : l'inscription ne survit pas à la
    /// session (Unregister à la déconnexion).</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<int, LfgEntry>
        LfgCharacters { get; } = new();

    /// <summary>Filtre natif <c>CGlobalInfo::LoadLFGCharacters</c>
    /// @0x8050ea0 : node exact si ≠0, niveau dans [min,max], puis
    /// classe : cls2==0 → branche [cls1,cls1+100], sinon
    /// cls∈{cls1,cls2}. Tri par char_id (ordre rb_tree).</summary>
    public static IEnumerable<LfgEntry> FilterLfg(
        short nodeId, short lvlMin, short lvlMax, short cls1, short cls2)
    {
        foreach (var e in LfgCharacters.Values.OrderBy(e => e.CharId))
        {
            if (nodeId != 0 && e.NodeId != nodeId) continue;
            if (e.Level < lvlMin || e.Level > lvlMax) continue;
            if (cls1 != 0)
            {
                if (cls2 == 0)
                { if (e.ClassId < cls1 || e.ClassId > cls1 + 100) continue; }
                else if (e.ClassId != cls1 && e.ClassId != cls2) continue;
            }
            else if (cls2 != 0 && e.ClassId != cls2) continue;
            yield return e;
        }
    }

    /// <summary>Retrait à la déconnexion (<c>SetLFG(char,false)</c>
    /// natif quand le SCharacter meurt).</summary>
    public static void UnregisterLfg(int charId) =>
        LfgCharacters.TryRemove(charId, out _);

    /// <summary>Services injectés (caractères + comptes).</summary>
    public sealed class Services
    {
        /// <summary>Accès <c>player_characters</c>.</summary>
        public required CharacterRepository Characters { get; init; }
        /// <summary>Accès edenfr_account (<c>worlds</c>).</summary>
        public required AccountRepository Accounts { get; init; }
        /// <summary>Lien T_ (GetTicket pour la migration de monde) —
        /// peut être déconnecté : le natif n'émet alors rien.</summary>
        public TicketServerClient? Tickets { get; init; }
    }

    private static Services _s = null!;

    /// <summary>Enregistre les commandes.</summary>
    public static void Register(PacketDispatcher d, Services s)
    {
        _s = s;

        // 19 — classement exploit : le natif recopie sa map cachée
        // (alimentée périodiquement) ; on lit directement le top 50
        // total_exploit — même résultat observable.
        d.Register(() => new CNE_CW_ServerQueryExploitRank(),
            async (session, msg, token) =>
            {
                var rows = await _s.Characters.GetExploitRankAsync();
                var stream = new OutBinStream();
                stream.WriteByte(0);                      // u8 — flag/type natif
                short rank = 0;
                foreach (var r in rows)
                {
                    stream.WriteShort(++rank);            // s16 — rang
                    stream.WriteString(r.name ?? "");     // str — nom
                    stream.WriteShort((short)r.level);    // s16 — niveau
                    stream.WriteString(r.family_name ?? ""); // str — famille
                    stream.WriteString("");               // str — réservé natif
                }
                await session.SendPacketAsync(59, stream.ToArray(), token);
            });

        // 26 — Execute natif vide.
        d.Register(() => new CNE_CW_ServerCaptchaReturn(),
            (session, msg, token) => Task.CompletedTask);

        // 28 — rapport plugin : réception + log, rien d'émis (natif).
        d.Register(() => new CNE_CW_ServerIllPluginInfo(),
            (session, msg, token) =>
            {
                EdenLog.Write($"[World] IllPluginInfo char=" +
                    $"{session.CharacterId} : {msg.Field1} | {msg.Field2}");
                return Task.CompletedTask;
            });

        // 29 — refresh liste des mondes (table edenfr_account.worlds).
        d.Register(() => new CNE_CW_ServerUpdateWorldServers(),
            async (session, msg, token) =>
            {
                var worlds = await _s.Accounts.GetWorldServersAsync();
                var stream = new OutBinStream();
                stream.WriteShort(1);                     // s16 — world_id serveur
                foreach (var w in worlds)
                {
                    stream.WriteShort((short)w.id);       // s16 — world id
                    stream.WriteString(w.name ?? "");     // str — nom
                    stream.WriteString(w.ip ?? "");       // str — ip
                    stream.WriteShort((short)w.port);     // s16 — port
                    stream.WriteInt(w.online_user);       // i32 — online_user
                    stream.WriteShort((short)w.maxnum_user); // s16 — maxnum
                    stream.WriteShort((short)w.state);    // s16 — state
                    stream.WriteShort((short)w.show_order); // s16 — show_order
                    stream.WriteShort(0);                 // s16 — champ natif +0x1e
                    stream.WriteShort(0);                 // s16 — champ natif +0x20
                }
                stream.WriteShort(0);                     // s16 — queue natif
                stream.WriteByte(0);                      // u8 — flag natif
                await session.SendPacketAsync(66, stream.ToArray(), token);
            });

        // 30 — le client demande si le perso a un mot de passe.
        d.Register(() => new CNE_CW_ServerCheckHasCharPassword(),
            async (session, msg, token) =>
            {
                var pw = await _s.Characters.GetCharPasswordAsync(msg.Value);
                await session.SendAsync(
                    new CNC_CW_ClientCheckHasCharPassword
                    { Value = string.IsNullOrEmpty(pw) ? 0 : 1 }, token);
            });

        // 31 — opérations mot de passe (DoCharPassword natif).
        d.Register(() => new CNE_CW_ServerCharPassword(),
            async (session, msg, token) =>
            {
                short code = await DoCharPasswordAsync(msg.Field1,
                    msg.Field2, msg.Field3);
                await session.SendAsync(
                    new CNC_CW_ClientCharPassword { Value = code }, token);
            });

        // 8 — recherche LFG : [s16 node][s16 lvlMin][s16 lvlMax]
        // [s16 cls1][s16 cls2] → 45 (records filtrés).
        d.Register(() => new CNE_CW_ServerLookingForMember(),
            async (session, msg, token) =>
            {
                var stream = new OutBinStream();
                var entries = FilterLfg(msg.Field1, msg.Field2,
                    msg.Field3, msg.Field4, msg.Field5).ToList();
                stream.WriteShort((short)entries.Count);   // s16 — count
                foreach (var e in entries)
                {
                    stream.WriteInt(e.CharId);             // i32 — char_id
                    stream.WriteString(e.Name);            // str — given_name
                    stream.WriteShort(e.Level);            // s16 — level (+0xc)
                    stream.WriteShort(e.NodeId);           // s16 — node_id (+0xe)
                    stream.WriteShort(e.RaceId);           // s16 — race_id (+0x10)
                    stream.WriteShort(e.ClassId);          // s16 — class_id (+0x12)
                }
                await session.SendPacketAsync(45, stream.ToArray(), token);
            });

        // 11 — inscription/retrait LFG : Execute natif =
        // SetLFG(GetCharacter, Field7!=0) ; les 6 autres champs lus
        // sont ignorés côté serveur natif (même sémantique ici).
        d.Register(() => new CNE_CW_ServerLookingForGroup(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is not { } charId) return;
                if (msg.Field7 == 0) { UnregisterLfg(charId); return; }
                var row = await _s.Characters.GetLfgSummaryAsync(charId);
                if (row == null) return;
                LfgCharacters[charId] = new LfgEntry(row.id,
                    row.name ?? "", (short)row.level, (short)row.node_id,
                    (short)row.race_id, (short)row.class_id);
            });

        // 20 — changement de monde : lookup worlds → SaveLastWorld
        // (si id%100≠0) → ticket T_ → 65 (22 octets CTicket bruts).
        d.Register(() => new CNE_CW_ServerChangeWorld(),
            async (session, msg, token) =>
            {
                if (session.AccountId is not { } accountId) return;
                var world = (await _s.Accounts.GetWorldServersAsync())
                    .FirstOrDefault(w => w.id == msg.Value);
                if (world == null)
                {
                    EdenLog.Write($"[World] ChangeWorld : world " +
                        $"{msg.Value} inexistant");
                    return;
                }
                if (msg.Value % 100 != 0)
                    await _s.Accounts.SaveLastWorldAsync(accountId,
                        (short)world.id);
                if (_s.Tickets is not { Connected: true } tickets)
                {
                    EdenLog.Write("[World] ChangeWorld : lien T_ absent — " +
                        "ticket impossible (silence natif)");
                    return;
                }
                var worldIp = BitConverter.ToUInt32(
                    System.Net.IPAddress.Parse(world.ip ?? "127.0.0.1")
                        .GetAddressBytes());
                var ticket = await tickets.GetTicketAsync((uint)accountId,
                    RemoteIpv4(session), worldIp, (ushort)world.port, token);
                if (ticket == null) return;
                // CNC_CW_ClientReceiveTicketToWorld (65) : Serialize natif
                // = 22 octets CTicket bruts (pas de préfixe).
                await session.SendPacketAsync(65, ticket, token);
            });

        // 21 — ack de migration : natif = AccountCrossLogoutAck
        // (notification billing gateway, absente de notre stack ; le
        // client coupe la connexion lui-même lors de la migration).
        d.Register(() => new CNE_CW_ServerChangeWorldAck(),
            (session, msg, token) =>
            {
                EdenLog.Write($"[World] ChangeWorldAck " +
                    $"char={session.CharacterId}");
                return Task.CompletedTask;
            });
    }

    /// <summary>IPv4 distante du client en u32 (même helper que
    /// <c>Program.RemoteIpv4</c> — dupliqué pour rester local).</summary>
    private static uint RemoteIpv4(NetSession session)
    {
        var ep = session.RemoteEndPoint;
        var host = ep.Split(':')[0];
        return System.Net.IPAddress.TryParse(host, out var ip)
            ? BitConverter.ToUInt32(ip.GetAddressBytes()) : 0;
    }

    /// <summary>Machine d'état <c>CPlayerEntity::DoCharPassword</c>
    /// réduite à son observable : 0 = vérifie (0/-1), 3 = définit
    /// (3/-1), sinon code = op.</summary>
    private static async Task<short> DoCharPasswordAsync(
        int charId, short op, string password)
    {
        var stored = await _s.Characters.GetCharPasswordAsync(charId);
        switch (op)
        {
            case 0: // vérifie le mot de passe courant
                return !string.IsNullOrEmpty(stored) && stored == password
                    ? (short)0 : unchecked((short)0xffff);
            case 3: // définit un nouveau mot de passe
                await _s.Characters.SetCharPasswordAsync(charId, password);
                return 3;
            default:
                return op;
        }
    }
}
