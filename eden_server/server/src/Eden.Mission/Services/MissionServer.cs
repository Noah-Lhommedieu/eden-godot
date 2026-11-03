using System.Text;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Noyau du MissionServer C# — registre des liens zone et dispatch des
/// NCIDs entrants (reversé depuis lapis::MissionServer :7654) :
///   53  IDReport          [s16 zoneId] — identifie le lien
///   55  NodeCreate        [u16 node][s16 type][u32 char][i32 parent]
///   142 MigrationDone     [i32 charUid] — perso en ligne, charge l'état
///   127 ListDialogue      [i32 char][s16 node][u8][i32 npc][i32][i32]
///   128 OnDialogueSelected (voir QuestEngine)
///   141 CloseMissionBrief [i32 charUid]
///   143 GiveUpMission     [i32 char][s16 node][s16 mid]
///   144 MobDieReport      [u16 mob][s16][s16][f32][f32][u16 n][n×i32]
///   21  AgentDBACommand   [i32 char][str cmd][str "command"] — verbes
///       Mail* + MissionServiceOp (relayé du client 87 MissionListOp).
/// </summary>
public sealed class MissionServer
{
    private readonly List<MissionSession> _sessions = new();
    private readonly QuestEngine _quests;
    private readonly MissionDb _db;
    private readonly WeddingService _weddings;
    private readonly RoomService _rooms;
    private readonly RankInfoService _ranks;
    private readonly BattlefieldService _battlefield;
    private readonly CrusadeService _crusade = new();

    /// <summary>charUid → session zone propriétaire (via NodeCreate).</summary>
    private readonly Dictionary<int, MissionSession> _charOwner = new();

    public MissionServer(QuestEngine quests, MissionDb db)
    {
        _quests = quests;
        _db = db;
        // CWeddingFactory natif : le service routre vers la session
        // zone propriétaire du char (_charOwner via NodeCreate).
        _weddings = new WeddingService(
            charUid => OwnerOf(charUid) != null,
            SendWeddingsInfoAsync,
            (c, t) => SendTextOutputIndexAsync(c, t));
        // CPlayerRoomDBA/DBO natif : broadcast zones pour le registre
        // room (779/781), réponses ciblées pour la recherche (778).
        _rooms = new RoomService(
            BroadcastToZonesAsync,
            async (c, n, b) =>
            {
                var owner = OwnerOf(c);
                if (owner != null) await owner.SendAsync(n, b);
            },
            (c, t) => SendTextOutputIndexAsync(c, t),
            BroadcastWorldTextAsync,
            id => _quests.Data.Parameters.Get(id),
            db.GetRoomExecStatsAsync);
        // CEventRankUpdate natif : rafraîchissement des classements
        // toutes les 2 h (heure paire) + broadcast 763 aux zones.
        _ranks = new RankInfoService(db, BroadcastToZonesAsync);
        // CBattlefieldFactory natif : file join → équilibrage → match
        // (V1, voir docs/limitless/BATTLEFIELD_DESIGN.md).
        _battlefield = new BattlefieldService(
            new Eden.Core.Database.Repositories
                .BattlefieldRepository(db.DataSource));
        // Match formé → broadcast 464 aux zones pour que chacune
        // entre ses joueurs dans l'instance bf (V1 : le client
        // gère l'invitation EnterBattlefield via sa zone).
        _battlefield.MatchEnded += (m, winner) =>
        {
            // [s16 bf][s16 winner][s16 nWinners][nWinners×i32 charId]
            // — la zone octroie le reward S_Battlefield aux gagnants
            // locaux.
            var body = new System.IO.MemoryStream();
            body.Write(BitConverter.GetBytes((short)m.BfId));
            body.Write(BitConverter.GetBytes((short)winner));
            var winners = (winner == 0 ? m.SideA :
                winner == 1 ? m.SideB : m.SideA.Concat(m.SideB))
                .ToList();
            body.Write(BitConverter.GetBytes((short)winners.Count));
            foreach (var cid in winners)
                body.Write(BitConverter.GetBytes(cid));
            _ = BroadcastToZonesAsync(465, body.ToArray());
        };
        // Fin par durée — tick périodique qui termine les matches
        // dont le timer a expiré (kill-target est checké dans
        // AddKill, la durée dans TickAsync).
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                try { await _battlefield.TickAsync(); }
                catch { }
            }
        });
        _battlefield.MatchFormed += m =>
        {
            var body = new System.IO.MemoryStream();
            body.Write(BitConverter.GetBytes((short)m.BfId));
            body.Write(BitConverter.GetBytes((short)m.SideA.Count));
            body.Write(BitConverter.GetBytes((short)m.SideB.Count));
            var all = m.SideA.Concat(m.SideB).ToList();
            body.Write(BitConverter.GetBytes((short)all.Count));
            foreach (var cid in all)
                body.Write(BitConverter.GetBytes(cid));
            _ = BroadcastToZonesAsync(464, body.ToArray());
        };
        // CBattleCrusadeFactory natif : le cycle checkin(600 s) →
        // bataille → clôture avance en tâche de fond ; les transitions
        // sont annoncées à l'écran sur toutes les zones. Au boot la
        // croisade 1 ouvre son checkin (Init natif : tpl+0x10≠0) puis
        // la clôture enchaîne la suivante (chaîne tpl+0xe : 1→2→3→4→1).
        _ = Task.Run(async () =>
        {
            var first = true;
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                try
                {
                    if (first)
                    {
                        first = false;
                        if (_crusade.Start(1) != null)
                        {
                            await BroadcastWorldTextAsync(
                                "BroadCastScreenMessage Type= 2 Msg= " +
                                "Crusade 1: check-in open!");
                            await BroadcastCrusadeStatusAsync(1,
                                CrusadeService.PhaseCheckin);
                        }
                    }
                    await TickCrusadesAsync();
                }
                catch { }
            }
        });
    }

    /// <summary>Broadcast 202 ZoneServerBattleCrusadeInfo natif —
    /// variante flag==0 du Serialize @0x804xxx mission :
    /// `[s16 0][i32 crusade][i32 status][i32 membres][i32 0][u8 0]`
    /// (poussé à toutes les zones ; la zone l'utilise pour remplir
    /// les sets du 617 OpenBattleCrusadeDialog — état+0x28 :
    /// 1 = checkin joinable, 2 = bataille).</summary>
    private Task BroadcastCrusadeStatusAsync(int crusadeId, int status)
    {
        var members = _crusade.Get(crusadeId)?.Members.Count ?? 0;
        return BroadcastToZonesAsync(202, Concat(
            BitConverter.GetBytes((short)0),          // s16 : flag 0 = update
            BitConverter.GetBytes(crusadeId),         // i32 : crusadeId
            BitConverter.GetBytes(status),            // i32 : état +0x28
            BitConverter.GetBytes(members),           // i32 : inscrits
            BitConverter.GetBytes(0),                 // i32 : +0x10
            new byte[] { 0 }));                       // u8  : +0x18
    }

    /// <summary>Tick du cycle crusade — checkin → bataille (annonce
    /// d'ouverture), bataille → clôture (annonce du vainqueur).
    /// Équivalent du changement d'état crusade+0x28 natif.</summary>
    private async Task TickCrusadesAsync()
    {
        foreach (var ev in _crusade.Tick())
            await AnnounceCrusadeEventAsync(ev);
    }

    /// <summary>Annonce + broadcasts d'une transition de cycle
    /// (tick naturel ou forçage admin CrusadeBattle) : message écran,
    /// 530 de clôture, 202 d'état, chaînage 1→2→3→4→1.</summary>
    private async Task AnnounceCrusadeEventAsync(
        CrusadeService.TickEvent ev)
    {
        var winnerName = ev.WinnerFamily > 0
            ? await _db.GetFamilyNameAsync(ev.WinnerFamily)
            : "";
        var msg = ev.NewPhase == CrusadeService.PhaseBattle
            ? $"Crusade {ev.CrusadeId}: battle started!"
            : $"Crusade {ev.CrusadeId} closed — " +
              (winnerName.Length > 0
                  ? $"{winnerName} captures the rift!"
                  : "no winner");
        await BroadcastWorldTextAsync(
            $"BroadCastScreenMessage Type= 2 Msg= {msg}");
        if (ev.NewPhase == 0)
        {
            // 530 crusade-close → les zones remettent
            // char+0xdfc (CrusadeId) à 0 : fin du PvP de siège.
            await BroadcastToZonesAsync(530,
                BitConverter.GetBytes((short)ev.CrusadeId));
            // 202 natif : état 0 = fermé (la croisade n'apparaît
            // plus ni en checkin ni en bataille côté client).
            await BroadcastCrusadeStatusAsync(ev.CrusadeId, 0);
            // Chaîne tpl+0xe native : la clôture d'un crusade
            // ouvre le checkin du suivant (1→2→3→4→1).
            var next = ev.CrusadeId % 4 + 1;
            if (_crusade.Start(next) != null)
            {
                await BroadcastWorldTextAsync(
                    "BroadCastScreenMessage Type= 2 Msg= " +
                    $"Crusade {next}: check-in open!");
                await BroadcastCrusadeStatusAsync(next,
                    CrusadeService.PhaseCheckin);
            }
        }
        else
            await BroadcastCrusadeStatusAsync(ev.CrusadeId,
                ev.NewPhase);
    }

    /// <summary>Boucle `CEventRankUpdate` : update + replanif à
    /// l'heure paire (lancée en tâche de fond par Program).</summary>
    public Task RunRankUpdatesAsync(CancellationToken ct)
        => _ranks.RunAsync(ct);

    /// <summary>Push des classements en cache vers une zone qui se
    /// connecte (substitut de la synchro `UpdateZoneRankInfo`).</summary>
    public Task PushRankInfoToAsync(MissionSession s)
        => _ranks.PushAllToAsync(b => s.SendAsync(763, b));

    /// <summary>Push de l'état courant des croisades (202) vers une
    /// zone qui se connecte — variante flag==1 « sync-all » du
    /// Serialize natif : `[s16 1][i32 n][n×(s16 crusade,s16 status)]`.
    /// Sinon son cache resterait vide jusqu'à la prochaine
    /// transition.</summary>
    public async Task PushCrusadeStatusToAsync(MissionSession s)
    {
        var ids = new[] { 1, 2, 3, 4 };
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes((short)1));  // s16 : flag 1 = sync-all
        body.AddRange(BitConverter.GetBytes(ids.Length)); // i32 : n entrées
        foreach (var cid in ids)
        {
            body.AddRange(BitConverter.GetBytes((short)cid));
            body.AddRange(BitConverter.GetBytes(
                (short)(_crusade.Get(cid)?.Phase ?? 0)));
        }
        await s.SendAsync(202, body.ToArray());
    }

    /// <summary>Broadcast d'une trame vers toutes les zones
    /// (équivalent `CMissionServer::BroadCastToZoneServer`).</summary>
    private async Task BroadcastToZonesAsync(ushort ncid, byte[] body)
    {
        List<MissionSession> targets;
        lock (_sessions) targets = _sessions.ToList();
        foreach (var z in targets)
            await z.SendAsync(ncid, body);
    }

    /// <summary>Charge les salles perso au démarrage — SELECT natif
    /// `Load PlayerRoom` (CPlayerRoomDBO boot).</summary>
    public Task LoadRoomsAsync() => _rooms.LoadAsync(_db);

    /// <summary>Push du registre room vers une zone qui se connecte
    /// (substitut de `CNE_DBA_ClientAllPlayerRoom` 699 natif).</summary>
    public Task PushRoomsToAsync(MissionSession s)
        => _rooms.PushAllToAsync(779, b => s.SendAsync(779, b));

    /// <summary>Enregistre un lien zone (appelé après handshake RSA).</summary>
    public void Register(MissionSession s)
    {
        lock (_sessions) _sessions.Add(s);
    }

    /// <summary>Retire le lien et les persos routés vers lui.</summary>
    public void Unregister(MissionSession s)
    {
        lock (_sessions) _sessions.Remove(s);
        lock (_charOwner)
            foreach (var kv in _charOwner.Where(k => k.Value == s).ToList())
                _charOwner.Remove(kv.Key);
        _quests.RemoveBySession(s);
    }

    /// <summary>Session zone propriétaire d'un perso — le lien direct
    /// par défaut (une seule zone) ; affiné par NodeCreate.</summary>
    private MissionSession? OwnerOf(int charUid)
    {
        lock (_charOwner)
            if (_charOwner.TryGetValue(charUid, out var s)) return s;
        lock (_sessions) return _sessions.FirstOrDefault();
    }

    /// <summary>Dispatch d'une trame entrante.</summary>
    public async Task DispatchAsync(MissionSession s, ushort ncid, byte[] b)
    {
        switch (ncid)
        {
            case 53: // IDReport [s16 zoneId]
                if (b.Length >= 2)
                {
                    s.ZoneId = BitConverter.ToInt16(b, 0);
                    EdenLog.Write($"[Mission] Zone Server [{s.ZoneId}] connected");
                }
                break;

            case 55: // NodeCreate [u16 node][s16 type][u32 char][i32 parent]
                if (b.Length >= 12)
                {
                    var node = BitConverter.ToUInt16(b, 0);
                    var charUid = BitConverter.ToInt32(b, 4);
                    if (charUid == 0) s.GlobalNodes.Add(node);
                    else lock (_charOwner) _charOwner[charUid] = s;
                }
                break;

            case 142: // MigrationDone [i32 charUid]
                if (b.Length >= 4)
                {
                    var charUid = BitConverter.ToInt32(b, 0);
                    lock (_charOwner) _charOwner[charUid] = s;
                    var node = s.GlobalNodes.FirstOrDefault();
                    await _quests.OnMigrationDoneAsync(s, charUid, node);
                }
                break;

            case 127: // ListDialogue
                await _quests.OnListDialogueAsync(s, b);
                break;

            case 128: // OnDialogueSelected
                await _quests.OnDialogueSelectedAsync(s, b);
                break;

            case 141: // CloseMissionBrief [i32 charUid] — panneau fermé
                await _quests.OnCloseBriefAsync(s, b);
                break;

            case 143: // GiveUpMission
                await _quests.OnGiveUpAsync(s, b);
                break;

            case 144: // MobDieReport
                await _quests.OnMobDieAsync(s, b);
                break;

            // 194 MissionServerVersusInstanceJoinRequest —
            // [i32 charUid][i32 flag→bool]. Execute natif @0x8145bf0 :
            // flag==0 → CColosseumFactory::LeaveJoin +
            // CVersusInstanceFactory::LeaveWait + CBattleRoyaleFactory::
            // LeaveWait ; flag!=0 → CVersusInstanceFactory::JoinWait.
            // Les factories PvP ne sont pas portées — la file est
            // journalisée ; la zone a déjà appliqué ses validations
            // (niveau 85, type de scène, enchant 54486).
            case 194:
                if (b.Length >= 8)
                {
                    var vuid = BitConverter.ToInt32(b, 0);
                    var vjoin = BitConverter.ToInt32(b, 4) > 0;
                    EdenLog.Write($"[Mission] VersusInstance " +
                        $"char={vuid} join={vjoin}");
                }
                break;

            case 450: // QuestItemGot [i32 charUid][s16 mid][u16 item]
                await _quests.OnQuestItemGotAsync(s, b);
                break;

            case 451: // QuestItemChanged [i32 charUid][u16 itemId]
                //  — notif interne zone→mission à chaque gain/perte
                // d'item (UpdateQuestItemStatus natif).
                await _quests.OnQuestItemChangedAsync(s, b);
                break;

            case 452: // AreaTravelNotify [i32 charUid][s16 node][s16 key]
                //  — aire déclencheur 51-59 franchie (TriggerArea natif).
                await _quests.OnAreaTravelAsync(s, b);
                break;

            case 21: // AgentDBACommand [i32 char][str cmd][str "command"]
                await OnDbaCommandAsync(s, b);
                break;

            // 802 CNC_ZMis_MissionServerMessageWrite
            // [i32 charUid][str titre][str contenu] — écriture panneau.
            // Natif @0x813e440 : cap ~100 (AllMessageData), id via
            // MessageIDAllocator, SaveMessage → message_board, réponse
            // 740 [i32 char][s16 node][i32 msgId][s16 res] (0=ok,1=fail).
            case 802:
                await OnBoardWriteAsync(s, b);
                break;

            // 703 CNC_ZMis_MissionServerUpdatePlayerCard
            // [i32 charUid][12×s16 stats][str (vide natif)][u8 public]
            // — Execute mission : GetPlayerCardUpdateSQLCmd →
            // UPDATE player_card (+ notif amis/amant si public change —
            // sous-systèmes non portés, DB seule).
            case 703:
                if (b.Length >= 28)
                {
                    var uid = BitConverter.ToInt32(b, 0);
                    var stats = new short[12];
                    for (var i = 0; i < 12; i++)
                        stats[i] = BitConverter.ToInt16(b, 4 + i * 2);
                    var slen = BitConverter.ToUInt16(b, 28);
                    var intro = 30 + slen <= b.Length
                        ? Eden.Core.Data.EdenEncoding.Big5
                            .GetString(b, 30, slen)
                        : "";
                    var pub = 30 + slen < b.Length && b[30 + slen] != 0;
                    await _db.UpdatePlayerCardAsync(uid, stats, intro,
                        pub);
                }
                break;

            // 831 CNC_ZMis_MissionServerRefreshRecommendActivities —
            // wire [i32 char][i32 ts][u16 n][n×(u16,u32,u8)]
            // [u16 m][m×u16] : RefreshRecommendActivities mission
            // @0x80780a0 → SaveRecommendActivities (DELETE+INSERT)
            // + recommend_activities_time.
            case 831:
                if (b.Length >= 12)
                {
                    var uid = BitConverter.ToInt32(b, 0);
                    var ts = BitConverter.ToInt32(b, 4);
                    var n = BitConverter.ToUInt16(b, 8);
                    var rows = new List<(int Id, int Progress,
                        int Finished)>();
                    var off = 10;
                    for (var i = 0; i < n && off + 7 <= b.Length; i++)
                    {
                        var rid = BitConverter.ToUInt16(b, off);
                        var cnt = BitConverter.ToInt32(b, off + 2);
                        var fl = b[off + 6];                 // u8 flag
                        // is_finished DB = réclamé (flag natif
                        // map2 bit16 : u8!=0 → terminée vue côté
                        // client ; la colonne porte « claimed »
                        // uniquement via Update RecommendActivity).
                        rows.Add((rid, cnt, fl));
                        off += 7;
                    }
                    await _db.SaveRecommendActivitiesAsync(uid, ts,
                        rows);
                }
                break;

            // 835 CNC_ZMis_MissionServerSetRecommendActivityTracing —
            // wire [i32 char][u16 id][u16 flag] : UPDATE is_tracing
            // (GetRecommendActivityTracingSQLCmd @0x832e750) puis
            // renvoi des lignes du perso en 832 — équivalent du
            // LoadRecommendActivities→UpdateRecommendActivities natif
            // qui repeuple la map char+0xc8c côté zone.
            case 835:
                if (b.Length >= 8)
                {
                    var uid = BitConverter.ToInt32(b, 0);
                    var rid = BitConverter.ToUInt16(b, 4);
                    var tracing = BitConverter.ToUInt16(b, 6);
                    await _db.SetRecommendActivityTracingAsync(uid,
                        rid, tracing);
                    var rows = await _db
                        .LoadRecommendActivitiesAsync(uid);
                    foreach (var r in rows)
                        await s.SendAsync(832, Concat(
                            BitConverter.GetBytes(uid),        // i32 char
                            BitConverter.GetBytes((ushort)r.Id),// u16 id
                            BitConverter.GetBytes(r.Progress)));// u32 cnt
                }
                break;

            case 149: // BattlefieldJoin [i32 char][s16 bf][s16 side]
                if (b.Length >= 8)
                {
                    var cid = BitConverter.ToInt32(b, 0);
                    var bf = BitConverter.ToInt16(b, 4);
                    var side = BitConverter.ToInt16(b, 6);
                    var e = _battlefield.Join(cid, bf, side);
                    // Accusé natif 91 BattlefieldJoinResult → zone :
                    // [i32 char][str name][s16 bf][s16 side][s16 r][s16]
                    var body = new System.IO.MemoryStream();
                    body.Write(BitConverter.GetBytes(cid));
                    var name = System.Text.Encoding.ASCII.GetBytes(
                        e.CharId.ToString());
                    body.Write(BitConverter.GetBytes((ushort)name.Length));
                    body.Write(name);
                    body.Write(BitConverter.GetBytes(bf));   // bfId
                    body.Write(BitConverter.GetBytes((short)e.Side)); // side
                    body.Write(BitConverter.GetBytes((short)0)); // résultat
                    body.Write(BitConverter.GetBytes((short)0));
                    await s.SendAsync(91, body.ToArray());
                }
                break;
            case 150: // BattlefieldEraseMember [i32 char] — quit
                if (b.Length >= 4)
                {
                    var cid = BitConverter.ToInt32(b, 0);
                    foreach (var bf in _battlefield.OpenList().Keys)
                        _battlefield.Leave(cid, bf);
                }
                break;
            case 151: // BattlefieldClose [s16 bf] — fin de match :
                // persiste les stats puis broadcast 465 Result aux
                // zones (retour ville + nettoyage BattlefieldId).
                if (b.Length >= 2)
                {
                    var bf = BitConverter.ToInt16(b, 0);
                    var winner = await _battlefield.EndAsync(bf);
                    // m est déjà déjà retiré du service — le
                    // broadcast MatchEnded (ci-dessus) a fait le
                    // résultat ; ce 151 n'est qu'un forçage.
                }
                break;
            case 152: // BattlefieldList [i32][s16][u16] — liste
                // bf ouverts (file+match) → réponse 463 zone.
                break;
            case 154: // BattlefieldKill [s16 bf][i32 killer][i32 dead]
                // — relay zone d'un kill PvP : score camp du tueur,
                // puis broadcast 466 UpdateScore aux zones.
                if (b.Length >= 10)
                {
                    var bf = BitConverter.ToInt16(b, 0);
                    _battlefield.AddKill(bf,
                        BitConverter.ToInt32(b, 2),
                        BitConverter.ToInt32(b, 6));
                    var m = _battlefield.GetMatch(bf);
                    if (m != null)
                    {
                        var sb = new System.IO.MemoryStream();
                        sb.Write(BitConverter.GetBytes(bf));
                        sb.Write(BitConverter.GetBytes(m.ScoreA));
                        sb.Write(BitConverter.GetBytes(m.ScoreB));
                        _ = BroadcastToZonesAsync(466,
                            sb.ToArray());
                    }
                }
                break;

            default:
                EdenLog.Write($"[Mission] NCID {ncid} non géré " +
                    $"len={b.Length}");
                break;
        }
    }

    /// <summary>21 AgentDBACommand — le zone relaie les requêtes client
    /// (mail + MissionServiceOp) comme texte de commande DBA :
    ///   "MailQuery page_number = %hd accept_only_friend_mail = %hd"
    ///   "MailRead mail_id = %d" / "MailDelete mail_id = %d"
    ///   "MailGetItem mail_id = %d inv_loc = %hd con_index = %hd …"
    ///   "MailGetGold mail_id = %d" / "MailReturn mail_id = %d"
    ///   "MissionServiceOp %hd %hd %hd" (mid, op, npcUid).</summary>
    private async Task OnDbaCommandAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 6) return;
        var charUid = BitConverter.ToInt32(b, 0);
        var len = BitConverter.ToUInt16(b, 4);
        if (6 + len > b.Length) return;
        // Big5 : le texte peut porter des noms d'items chinois
        // (BroadCastScreenMessage des autels) — Big5 ⊃ ASCII.
        var cmd = Eden.Core.Data.EdenEncoding.Big5
            .GetString(b, 6, len).TrimEnd('\0');
        EdenLog.Write($"[Mission] DBA char={charUid} '{cmd}'");
        var w = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return;
        // Les formats natifs mêlent "key= N" (CheckTrigger) et
        // "key = N" (Mail) — après la clé, on saute le token "="
        // puis on lit le premier entier. Sans ça Arg("mail_id")
        // tombait sur "=" et renvoyait 0 (delete/read/get no-op).
        int Arg(string key)
        {
            // Deux formes natives : "key = N" (tokens séparés) et
            // "key= N" (clé et '=' soudés, ex "crusade_id= 1").
            var i = Array.IndexOf(w, key);
            if (i < 0)
            {
                i = Array.FindIndex(w,
                    t => t.StartsWith(key + "=",
                        StringComparison.Ordinal));
                if (i < 0) return 0;
            }
            for (var j = i + 1; j < w.Length; j++)
            {
                if (int.TryParse(w[j], out var v)) return v;
                if (w[j] != "=") break;
            }
            return 0;
        }

        // Salles personnelles : « Rent Player Room », « PlayerRoom
        // Pay Rent », « Search/Set/Change/Switch/Update Room… »,
        // « RoomExtend », « Enter Player Room » (TC_*Room* natifs).
        if (await _rooms.TryHandleAsync(_db, charUid, cmd, w, Arg))
            return;

        switch (w[0])
        {
            // --- Courrier : réponses au format attendu par la zone ---
            case "MailQuery":
                // 68 → zone reconstruit le 400 depuis player_mail ;
                // corps minimal [i32 char][s16 0].
                await s.SendAsync(68, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0)));
                break;
            case "MailRead":
                await _db.MailMarkReadAsync(charUid, Arg("mail_id"));
                // 69 → 401 : [i32 char][s16][record] — record inconnu,
                // envoie s16 res (0=lu) — HYPOTHESIS.
                await s.SendAsync(69, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes((short)0)));
                break;
            case "MailDelete":
            {
                var ok = await _db.MailDeleteAsync(charUid, Arg("mail_id"));
                // 70 → 402 [s16 res][i32 id]
                await s.SendAsync(70, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes((short)(ok ? 1 : 0)),
                    BitConverter.GetBytes(Arg("mail_id"))));
                break;
            }
            case "MailGetGold":
            {
                // Prélève l'or attaché ET crédite le perso — une seule
                // transaction dans MailTakeGoldAsync (verrou FOR UPDATE
                // du courrier, gold=0+returned=1, delta clampé ; avant,
                // le RETURNING post-UPDATE rendait toujours 0 — jamais
                // crédité — et le crédit partait sur une 2e connexion).
                // 76 → 406 [i32 res] ; les i32 supplémentaires portent
                // le delta pour l'attr or (280).
                var gold = await _db.MailTakeGoldAsync(charUid,
                    Arg("mail_id"));
                await s.SendAsync(76, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes(gold > 0 ? 1 : 0),
                    BitConverter.GetBytes(gold),
                    BitConverter.GetBytes(0)));
                break;
            }
            case "MailGetItem":
            {
                // mailitem → inventory1 (copie intégrale des colonnes
                // natives). 75 → 405 [i32 res] ; les champs suivants
                // alimentent le 274 ClientPutItemIntoInventory zone.
                var t = await _db.MailTakeItemAsync(charUid,
                    Arg("mail_id"), Arg("con_index"), Arg("inv_loc"));
                await s.SendAsync(75, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes(t != null ? 1 : 0),
                    BitConverter.GetBytes(t?.ItemId ?? 0),
                    BitConverter.GetBytes(t?.Bag ?? 0),
                    BitConverter.GetBytes(t?.Slot ?? 0),
                    BitConverter.GetBytes(t?.Durability ?? 0),
                    BitConverter.GetBytes(t?.MaxDurability ?? 0),
                    BitConverter.GetBytes(t?.Bind ?? 0),
                    BitConverter.GetBytes(t?.Strengthen ?? 0)));
                break;
            }
            case "MailReturn":
            {
                var ok = await _db.MailReturnAsync(charUid, Arg("mail_id"));
                // 77 → 409 [s16 res]
                await s.SendAsync(77, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes((short)(ok ? 1 : 0))));
                break;
            }
            case "CrusadeJoin":
                // "CrusadeJoin crusade_id= %d" — relayé de la zone
                // (NCID 259). TC_BattleCrusadeJoin natif @0x81ebc40 :
                // family résolue DB, Join()==4 →
                // EnterBattleCrusadeAnswer [i32 char][s16 0]
                // [s16 crusade][s16 node] → la zone stocke
                // char+0xdfc=crusade et TransportCharacterToNode.
                {
                    var cid = Arg("crusade_id");
                    var fam = await _db.GetFamilyIdAsync(charUid);
                    // Siège guilde-vs-guilde : un char sans famille ne
                    // peut pas s'inscrire (gate du TC_ natif avant
                    // l'appel Join — ici renvoyé comme mauvais état).
                    var res = fam <= 0 ? CrusadeService.JoinBadState
                        : _crusade.Join(cid, charUid, fam);
                    var node = res == CrusadeService.JoinOk
                        ? CrusadeService.NodeOf(cid) : 0;
                    // 201 EnterBattleCrusadeAnswer natif :
                    // [i32 char][s16 res][s16 crusade][s16 node]
                    var ab = new System.IO.MemoryStream();
                    ab.Write(BitConverter.GetBytes(charUid));   // i32 : charUid
                    ab.Write(BitConverter.GetBytes((short)(res == CrusadeService.JoinOk ? 1 : res))); // s16 : résultat Join
                    ab.Write(BitConverter.GetBytes((short)cid));      // s16 : crusadeId
                    ab.Write(BitConverter.GetBytes((short)node));     // s16 : node de siège (310-313)
                    await s.SendAsync(201, ab.ToArray());
                    break;
                }
            case "CrusadeLeave":
                // "CrusadeLeave crusade_id= %d char_id= %d" — la zone
                // signale qu'un char a quitté le node de siège
                // (TC_BattleCrusadeLeave natif) → retrait du set.
                _crusade.Leave(Arg("char_id"));
                break;
            case "CrusadeKill":
                // "CrusadeKill crusade_id= %d killer= %d victim= %d" —
                // relayé par la zone sur kill PvP dans une map de
                // siège (pattern du relais battlefield 154).
                _crusade.OnKill(Arg("crusade_id"), Arg("killer"),
                    Arg("victim"));
                break;
            case "CrusadeStart":
                // "CrusadeStart crusade_id= %d" — déclenche le cycle
                // (commande admin ; le schedule natif lit S_Schedule).
                {
                    var nb = _crusade.Start(Arg("crusade_id"));
                    if (nb != null)
                    {
                        // Annonce écran via le canal 20 natif — la
                        // zone exécute BroadCastScreenMessage → 474.
                        await BroadcastWorldTextAsync(
                            "BroadCastScreenMessage Type= 2 Msg= " +
                            $"Crusade {nb.CrusadeId}: check-in open!");
                        await BroadcastCrusadeStatusAsync(nb.CrusadeId,
                            CrusadeService.PhaseCheckin);
                    }
                    break;
                }
            case "CrusadeBattle":
                // "CrusadeBattle crusade_id= %d" — force la transition
                // checkin → bataille (admin/test ; mêmes broadcasts
                // que le tick naturel).
                {
                    var cid2 = Arg("crusade_id");
                    if (_crusade.ForceBattle(cid2))
                        await AnnounceCrusadeEventAsync(
                            new CrusadeService.TickEvent(cid2,
                                CrusadeService.PhaseBattle, 0));
                    break;
                }
            case "CrusadeClose":
                // "CrusadeClose crusade_id= %d" — TC_BattleCrusadeClose
                // natif : force la clôture + annonce du vainqueur.
                {
                    var c = _crusade.Close(Arg("crusade_id"));
                    if (c != null)
                        await AnnounceCrusadeEventAsync(
                            new CrusadeService.TickEvent(
                                c.Value.CrusadeId, 0,
                                c.Value.WinnerFamily));
                    break;
                }
            case "MailSend":
                // 71 → 403 [s16 res] — envoi non implémenté : refus net.
                await s.SendAsync(71, Concat(
                    BitConverter.GetBytes(charUid),
                    BitConverter.GetBytes((short)0),
                    BitConverter.GetBytes((short)0)));
                break;

            // --- Trigger d'aire (TC_CheckTrigger natif @0x8203770) ---
            case "CheckTrigger":
                // "CheckTrigger ServerID= %d NodeEventID= %d SelectID= %hd"
                // — la zone a validé le franchissement ; la mission
                // crédite les objectifs liés et renvoie
                // ClearTriggerAreaID (reset de l'anti-retrigger).
                await _quests.OnCheckTriggerAsync(s, charUid,
                    Arg("NodeEventID="), Arg("SelectID="));
                break;

            // --- Annonce écran (autels, events) : la mission natif
            // re-diffuse la commande à TOUTES les zones via
            // CNC_MW_WorldTextCommand (20) ; chaque zone l'exécute
            // (Mission_TC_BroadCastScreenMessage → 474 aux clients).
            case "BroadCastScreenMessage":
                await BroadcastWorldTextAsync(cmd);
                break;

            // "CheckMissionEnchant <enchant> [compteur]" —
            // TC_CheckMissionEnchant @0x081f2960 (zone → mission) :
            // objectif de quête CheckBuff (escortes, buffs requis).
            case "CheckMissionEnchant":
                if (w.Length >= 2 && int.TryParse(w[1], out var ckEnh))
                    await _quests.OnCheckMissionEnchantAsync(s, charUid,
                        ckEnh, w.Length >= 3 &&
                        int.TryParse(w[2], out var ckCnt) ? ckCnt : 1);
                break;

            // --- Opérations quête (client 87 MissionListOp relayé) ---
            case "MissionServiceOp":
                // "MissionServiceOp %hd %hd %hd" = mid, op, npcUid
                if (w.Length >= 4 &&
                    int.TryParse(w[1], out var mid) &&
                    int.TryParse(w[2], out var op) &&
                    int.TryParse(w[3], out var npc))
                    await _quests.OnServiceOpAsync(s, charUid, mid, op, npc);
                break;

            // "SetMissionTrace mission_trace01 = %d … mission_trace10 = %d"
            // — TC_SetMissionTrace natif : 10 ids → char+0x3c0 puis
            // UPDATE mission_trace. Les paires sont `clé = valeur`.
            case "SetMissionTrace":
            {
                var ids = new int[10];
                for (var i = 0; i < w.Length - 2; i++)
                    if (w[i].StartsWith("mission_trace", StringComparison.Ordinal)
                        && w[i + 1] == "="
                        && int.TryParse(w[i][13..], out var slot)
                        && slot is >= 1 and <= 10
                        && int.TryParse(w[i + 2], out var mid0))
                        ids[slot - 1] = mid0;
                await _db.SetMissionTraceAsync(charUid, ids);
                break;
            }

            // --- Cérémonies de mariage (CWeddingFactory natif) ---
            // "WeddingInfoQuery <number>" (rodata 0x84ed4a2) : flag≠0
            // abonne le char aux rediffusions, puis liste → ZMis 193.
            case "WeddingInfoQuery":
                await _weddings.InfoQueryAsync(charUid,
                    w.Length > 1 && w[1] != "0");
                break;
            // "ReserverChuch" (faute de frappe native, rodata
            // 0x84ed494) : réservation de la salle — validations
            // natives + CreateWedding (carte d'invitation 0x667f).
            case "ReserverChuch":
                await _weddings.ReserveAsync(charUid, _db);
                break;
            // "CloseWedding" : clôture forcée d'une réservation
            // (statut 1 uniquement — TC_CloseWedding @0x81ec900).
            case "CloseWedding":
                await _weddings.CloseAsync(charUid, _db);
                break;
            // "VisitWeddingInstance LoverID= %d" — TC natif
            // @0x823ce30 : GetWedding(loverId) ; absent → TextOutput
            // 0x1c64 ; non ouvrable → 0x1c65 ; ouvert (status 3) →
            // TP dans l'instance (CWeddingFactory — sous-système
            // d'instance non porté : on couvre les deux réponses
            // textuelles fidèles).
            case "VisitWeddingInstance":
            {
                var loverId = Arg("LoverID");
                var wed = _weddings.Weddings
                    .FirstOrDefault(x => x.LoveId == loverId);
                if (wed == null)
                    await SendTextOutputIndexAsync(charUid, 0x1c64);
                else if (wed.Status != 3)
                    await SendTextOutputIndexAsync(charUid, 0x1c65);
                else
                    EdenLog.Write($"[Mission] VisitWeddingInstance " +
                        $"char={charUid} lover={loverId} : instance " +
                        "ouverte — TP non porté (CWeddingFactory).");
                break;
            }
            // "Update RecommendActivity ActivityID = %hu
            // Parameter = %u Finished = %hd" — TC natif @0x81f2800 :
            // ed maps (0x790/0x7a8) → SaveRecommendActivities ; chez
            // nous UPDATE ciblée (target_number + is_finished).
            case "Update":
                if (w.Length > 1 && w[1] == "RecommendActivity")
                    await _db.UpdateRecommendActivityAsync(charUid,
                        Arg("ActivityID"), Arg("Parameter"),
                        Arg("Finished"));
                // "Update WarCoin Coin = %d" — CEC_GainWarCoin (zone)
                // → TC_GainWarCoin → CGlobalInfo::GainWarCoin
                // @0x8081490 : exec+0x874 borné 9999999, texte
                // 0x1100/0x1101 « %d,%s » (nom S_TextIndex 0x1665
                // « Battle Merit ») puis broadcast
                // « UpdateWarCoin ch_id = %d coin = %d » → la zone
                // pousse 576 ClientUpdateWarCoin.
                else if (w.Length > 2 && w[1] == "WarCoin")
                {
                    var total = await _db.AddWarCoinAsync(charUid,
                        Arg("Coin"));
                    await SendCoinGainTextAsync(charUid,
                        Arg("Coin"), 0x1665);
                    await SendZoneTextCommandAsync(charUid,
                        $"UpdateWarCoin ch_id = {charUid} coin = {total}");
                }
                // "Update AlpacaCoin Coin = %d" — CEC_GainAlpacaCoin
                // → TC_GainAlpacaCoin → CGlobalInfo::GainAlpacaCoin
                // @0x8081160 : exec+0x878 borné 9999999, texte
                // 0x1100/0x1101 (nom S_TextIndex 0x13c1 « Alpaca
                // Coins ») puis « UpdateAlpacaCoin ch_id = %d
                // coin = %d » → 580 ClientUpdateAlpacaCoin.
                else if (w.Length > 2 && w[1] == "AlpacaCoin")
                {
                    var total = await _db.AddAlpacaCoinAsync(charUid,
                        Arg("Coin"));
                    await SendCoinGainTextAsync(charUid,
                        Arg("Coin"), 0x13c1);
                    await SendZoneTextCommandAsync(charUid,
                        $"UpdateAlpacaCoin ch_id = {charUid} coin = {total}");
                }
                break;
            // "GMGainLifelong Money = %d" — CEC_GainLLG (zone) +
            // récompense recommend (GiveRecommendActivitiesReward
            // @0x810ba80) → TC_GMGainLifelongGold →
            // CGlobalInfo::GainLifelongGold @0x807a230 : exec+0x75c
            // borné 9999999, texte 0x1100/0x1101 « %d,%s » (nom
            // S_TextIndex 0x11ac « Crystal Cross Medal ») puis
            // CNC_ZMis_ZoneServerUpdateLifelongGold [i32 char]
            // [i32 or] (ZMis 173) → la zone pousse 555.
            case "GMGainLifelong":
            {
                var money = Arg("Money");
                var total = await _db.AddLifelongGoldAsync(charUid,
                    money);
                await SendCoinGainTextAsync(charUid, money, 0x11ac);
                var owner = OwnerOf(charUid);
                if (owner != null)
                    await owner.SendAsync(173, Concat(
                        BitConverter.GetBytes(charUid),   // i32 : char
                        BitConverter.GetBytes(total)));   // i32 : or
                break;
            }

            // --- Panneau d'affichage (TC_Message* natifs) ---
            // "MessageRead id = %d" → 741 [i32 char][s16][i32 id]
            // [str contenu][u8] — zone → 458 [i32][str][u8].
            case "MessageRead":
            {
                var post = await _db.BoardReadAsync(Arg("id"));
                var content = Eden.Core.Data.EdenEncoding.Big5
                    .GetBytes(post?.Content ?? "");
                await s.SendAsync(741, Concat(
                    BitConverter.GetBytes(charUid),        // i32 : +4 char
                    BitConverter.GetBytes((short)0),       // s16 : +8 node
                    BitConverter.GetBytes(Arg("id")),      // i32 : +0x1c id
                    BitConverter.GetBytes((ushort)content.Length),
                    content,                               // str : +0x20 contenu
                    new byte[] { 0 }));                    // u8  : +0x24 flag
                break;
            }
            // "MessageListQuery page = %hd" → 739 : 4 vecteurs
            // (ids/titres/noms/dates) — la zone IGNORE le payload
            // (Execute @0x83295b0 → 456 vide), on émet quand même le
            // format natif complet (Serialize @0x8119d40).
            case "MessageListQuery":
            {
                var posts = await _db.BoardListAsync();
                var body = new List<byte>();
                body.AddRange(BitConverter.GetBytes(charUid));   // i32 : char
                body.AddRange(BitConverter.GetBytes((short)0));  // s16 : node
                body.AddRange(BitConverter.GetBytes((ushort)posts.Count));
                foreach (var p in posts)
                    body.AddRange(BitConverter.GetBytes(p.MessageId));
                body.AddRange(BitConverter.GetBytes((ushort)posts.Count));
                foreach (var p in posts)
                {
                    var t = Eden.Core.Data.EdenEncoding.Big5
                        .GetBytes(p.Title);
                    body.AddRange(BitConverter.GetBytes((ushort)t.Length));
                    body.AddRange(t);
                }
                body.AddRange(BitConverter.GetBytes((ushort)posts.Count));
                foreach (var p in posts)
                {
                    var n = Eden.Core.Data.EdenEncoding.Big5
                        .GetBytes(p.PlayerName);
                    body.AddRange(BitConverter.GetBytes((ushort)n.Length));
                    body.AddRange(n);
                }
                body.AddRange(BitConverter.GetBytes((ushort)posts.Count));
                foreach (var p in posts)
                    body.AddRange(BitConverter.GetBytes(p.CreateDate));
                body.AddRange(BitConverter.GetBytes((short)0)); // s16 page?
                body.AddRange(BitConverter.GetBytes((short)0));
                body.AddRange(BitConverter.GetBytes((short)0));
                await s.SendAsync(739, body.ToArray());
                break;
            }
            // "MessageDelete id = %d" → 742 [i32 char][s16][s16 res]
            // — zone → 459 [s16 res].
            case "MessageDelete":
            {
                var ok = await _db.BoardDeleteAsync(Arg("id"));
                await s.SendAsync(742, Concat(
                    BitConverter.GetBytes(charUid),        // i32 : +4 char
                    BitConverter.GetBytes((short)0),       // s16 : +8 node
                    BitConverter.GetBytes(
                        (short)(ok ? 1 : 0))));            // s16 : +0x1c res
                break;
            }

            // "Query PlayerCard player_id = %d" — TC_QueryPlayerCard
            // natif @0x82188d0 : assemble exec (ClassID +0x38, Level
            // +0x5c, ClassLv +0x68), family.name, 12 stats player_card
            // + intro + is_public → 104 CNC_ZMis_ZoneServerQueryPlayerCard.
            // Wire : [i32 requester][i32 target][str name][s16 level]
            // [s16 classId][s16 classLv][str guilde][12×s16 stats]
            // [str intro][u16 0][u16 0] — 2 vecteurs de collection
            // (bitmask) vides : sous-système non porté.
            case "Query":
                if (w.Length > 2 && w[1] == "PlayerCard")
                {
                    var target = Arg("player_id");
                    var card = await _db.GetPlayerCardFullAsync(target);
                    var body = new List<byte>();
                    body.AddRange(BitConverter.GetBytes(charUid));// i32 : +4 requester
                    body.AddRange(BitConverter.GetBytes(target)); // i32 : +8 target
                    void Str(string s2)
                    {
                        var e = Eden.Core.Data.EdenEncoding.Big5
                            .GetBytes(s2);
                        body.AddRange(BitConverter.GetBytes(
                            (ushort)e.Length));
                        body.AddRange(e);
                    }
                    if (card == null)
                    {
                        Str("");                            // str : +0xc nom
                        for (var i = 0; i < 3; i++)
                            body.AddRange(
                                BitConverter.GetBytes((short)0));
                        Str("");                            // str : guilde
                        for (var i = 0; i < 12; i++)
                            body.AddRange(
                                BitConverter.GetBytes((short)0));
                        Str("");                            // str : intro
                    }
                    else
                    {
                        Str(card.Value.Name);
                        body.AddRange(BitConverter.GetBytes(
                            card.Value.Level));             // s16 : +0x20 level
                        body.AddRange(BitConverter.GetBytes(
                            card.Value.ClassId));           // s16 : +0x22 class
                        body.AddRange(BitConverter.GetBytes(
                            card.Value.ClassLv));           // s16 : +0x24 classLv
                        Str(card.Value.FamilyName);         // str : +0x28 guilde
                        foreach (var st in card.Value.Stats)
                            body.AddRange(BitConverter.GetBytes(st));
                        Str(card.Value.Intro);              // str : +0x34 intro
                    }
                    body.AddRange(BitConverter.GetBytes((ushort)0));
                    body.AddRange(BitConverter.GetBytes((ushort)0));
                    await s.SendAsync(104, body.ToArray());
                }
                else
                    EdenLog.Write($"[Mission] DBA non gérée : '{cmd}'");
                break;

            // "GMGainSpecialCoin Type = %hd Money = %d" — commande GM
            // native relayée par la zone (rodata 0x87ced94) →
            // CGlobalInfo::GainSpecialCoin @0x80863f0 : types valides
            // 0-5,9-11 (6-8 et >11 rejetés), montant clampé
            // 0..9999999 dans special_coins.coin{type+1}, puis
            // TextOutputIndex 0x1100 (gain)/0x1101 (perte) avec
            // "%d,%s" (montant,nom) et broadcast du total au client
            // (natif → CNC_MW_WorldServerUpdateSpecialCoin au monde,
            // qui pousse CNC_CW_ClientUpdateSpecialCoin 78 ; chez nous
            // mission→zone(80)→monde→client).
            case "GMGainSpecialCoin":
            {
                var coinType = Arg("Type");
                var money = Arg("Money");
                if (coinType is < 0 or > 11 ||
                    (coinType >= 6 && coinType <= 8))
                    break;
                var total = await _db.AddSpecialCoinAsync(charUid,
                    coinType + 1, money);
                // Nom de la monnaie (S/T_TextIndex : type+0x95f pour
                // 0-8, ids dédiés 0x1a7f/0x1cfe/0x1dad pour 9/10/11) —
                // table GameData::GetTextIndex native.
                var nameId = coinType switch
                {
                    9 => 0x1a7f, 10 => 0x1cfe, 11 => 0x1dad,
                    _ => 0x95f + coinType,
                };
                _quests.Data.Texts.TryGetValue(nameId, out var coinName);
                await SendTextOutputIndexAsync(charUid,
                    (short)(money < 0 ? 0x1101 : 0x1100),
                    $"{Math.Abs(money)},{coinName ?? $"coin{coinType}"}");
                var owner = OwnerOf(charUid);
                if (owner != null)
                    await owner.SendAsync(80, Concat(
                        BitConverter.GetBytes(charUid),       // i32 : charUid
                        BitConverter.GetBytes((ushort)1),     // u16 : 1 entrée
                        BitConverter.GetBytes((short)coinType),// s16 : type
                        BitConverter.GetBytes(total)));       // i32 : total
                break;
            }

            // "GainExp Prestige %hd = %d" — CDBAgent::Command natif
            // émis par CPlayerFactory::AddPrestigeExp (zone) pour les
            // ops `Reputation id +/- n` des événements pay (NCID 253)
            // → TC_GainPrestigeExp → upsert prestige.exp.
            case "GainExp":
                if (w.Length > 1 && w[1] == "Prestige")
                    await _db.AddPrestigeExpAsync(charUid,
                        Arg("Prestige"), Arg("="));
                break;

            // "FortuneBag Item= %hd Counter= %hd" — compteur
            // d'ouverture de sac émis par la zone à la fin
            // d'OpenFortuneBag (TC natif : UPSERT
            // player_fortune_bag par account_id).
            case "FortuneBag":
                await _db.SetFortuneBagCounterAsync(charUid,
                    Arg("Item="), Arg("Counter="));
                break;

            default:
                EdenLog.Write($"[Mission] DBA non gérée : '{w[0]}'");
                break;
        }
    }

    /// <summary>802 MissionServerMessageWrite — Execute natif
    /// @0x813e440 : cap ~100 posts (AllMessageData), id via
    /// MessageIDAllocator (max+1), SaveMessage → message_board,
    /// réponse 740 `CNC_ZMis_ZoneServerMessageWriteResult`
    /// [i32 char][s16 node][i32 msgId][s16 res] (0=écrit, 1=refus).
    /// La zone consomme alors la carte 0x7ceb et PURGE le post
    /// (MessageDelete) — feature bridée nativement.</summary>
    private async Task OnBoardWriteAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 8) return;
        var o = 0;
        var charUid = BitConverter.ToInt32(b, o); o += 4;
        string ReadStr()
        {
            var len = BitConverter.ToUInt16(b, o); o += 2;
            var str = Eden.Core.Data.EdenEncoding.Big5
                .GetString(b, o, Math.Min(len, b.Length - o));
            o += len;
            return str;
        }
        var title = ReadStr();
        var content = ReadStr();
        if (await _db.BoardCountAsync() >= 100)
        {
            await s.SendAsync(740, Concat(
                BitConverter.GetBytes(charUid),
                BitConverter.GetBytes((short)0),
                BitConverter.GetBytes(0),
                BitConverter.GetBytes((short)1)));   // s16 : refus
            return;
        }
        var name = await _db.GetCharacterNameAsync(charUid) ?? "";
        var id = await _db.BoardWriteAsync(title, content, name);
        await s.SendAsync(740, Concat(
            BitConverter.GetBytes(charUid),            // i32 : +4 char
            BitConverter.GetBytes((short)0),           // s16 : +8 node
            BitConverter.GetBytes(id),                 // i32 : +0x1c msgId
            BitConverter.GetBytes((short)0)));         // s16 : +0x20 res
    }

    /// <summary>CNC_MW_WorldTextCommand (20) vers toutes les zones —
    /// wire natif `[u16 len][ascii cmd]` (Serialize @0x804b550 :
    /// une seule string). Chaque zone exécute la commande via son
    /// CMissionCmds (BroadCastScreenMessage → 474 broadcast).</summary>
    private async Task BroadcastWorldTextAsync(string cmd)
    {
        var bytes = Eden.Core.Data.EdenEncoding.Big5.GetBytes(cmd);
        var body = Concat(BitConverter.GetBytes((ushort)bytes.Length),
            bytes);
        List<MissionSession> targets;
        lock (_sessions) targets = _sessions.ToList();
        foreach (var z in targets)
            await z.SendAsync(20, body);
    }

    /// <summary>ZMis 193 `CNC_ZMis_ZoneServerWeddingsInfo` → la zone
    /// du requester. Wire (`Serialize` @0x80db220) :
    ///   [i32 requester][u8 n]
    ///   n × [i32 loveId][str name1][str name2][u8 status]
    ///       [i32 startTime][i32 remainingTime]
    /// La zone calcule `leftTime = remaining - (now - start)` puis
    /// émet 599 `[u8 n][n×{i32,str,str,u8,i32}]`.</summary>
    private async Task SendWeddingsInfoAsync(int charUid)
    {
        var owner = OwnerOf(charUid);
        if (owner == null) return;
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(charUid));       // i32 : +4 requester
        var recs = _weddings.Snapshot();
        body.Add((byte)recs.Count);                          // u8  : n mariages
        foreach (var r in recs)
        {
            body.AddRange(BitConverter.GetBytes(r.LoveId));  // i32 : +4 love_id
            var n1 = Eden.Core.Data.EdenEncoding.Big5.GetBytes(r.Name1);
            var n2 = Eden.Core.Data.EdenEncoding.Big5.GetBytes(r.Name2);
            body.AddRange(BitConverter.GetBytes((ushort)n1.Length));
            body.AddRange(n1);                               // str : +0x1c demandeur
            body.AddRange(BitConverter.GetBytes((ushort)n2.Length));
            body.AddRange(n2);                               // str : +0x20 partenaire
            body.Add(r.Status);                              // u8  : +8 EWeddingStatus
            body.AddRange(BitConverter.GetBytes(r.StartTime));   // i32 : +0x14 now
            body.AddRange(BitConverter.GetBytes(r.Remaining));   // i32 : +0x18 stepEnd-now
        }
        await owner.SendAsync(193, body.ToArray());
    }

    /// <summary>ZMis 66 `CNC_ZMis_ZoneServerTextOutputIndex` — erreurs
    /// mariage (0x1c61-0x1c69). Wire (`Serialize` @0x807a220 +
    /// `CGlobalInfo::TextOutputIndex` @0x807a440) :
    ///   [i32 char][s16 node][s16 textId][str ""][u8 flag][u8 0]
    /// La zone émet 316 avec body[4..] — node = contexte dialogue
    /// (exec+0x30, 0 faute de node courant).</summary>
    private async Task SendTextOutputIndexAsync(int charUid, short textId,
        string paramText = "")
    {
        var owner = OwnerOf(charUid);
        if (owner == null) return;
        var txt = Eden.Core.Data.EdenEncoding.Big5.GetBytes(paramText);
        await owner.SendAsync(66, Concat(
            BitConverter.GetBytes(charUid),         // i32 : +4 charUid
            BitConverter.GetBytes((short)0),        // s16 : +8 node (exec+0x30)
            BitConverter.GetBytes(textId),          // s16 : +0xa textId
            BitConverter.GetBytes((ushort)txt.Length), txt,
                                                    // str : +0x10 param
                                                    // ("%d,%s" pour les
                                                    // messages monétaires)
            new byte[] { 0x9b },                    // u8  : +0xc flag canal
            new byte[] { 0 }));                     // u8  : +0x14
    }

    /// <summary>Texte de gain/perte de monnaie — TextOutputIndex
    /// natif 0x1100 « Obtain %s1 unit(s) of %s2 » / 0x1101
    /// « Decrease %s1 unit(s) of %s2 » avec « %d,%s » (montant, nom
    /// S_TextIndex <paramref name="nameTextId"/>) — même moule que
    /// GMGainSpecialCoin.</summary>
    private async Task SendCoinGainTextAsync(int charUid, int amount,
        int nameTextId)
    {
        _quests.Data.Texts.TryGetValue(nameTextId, out var coinName);
        await SendTextOutputIndexAsync(charUid,
            (short)(amount < 0 ? 0x1101 : 0x1100),
            $"{Math.Abs(amount)},{coinName ?? $"coin{nameTextId}"}");
    }

    /// <summary>58 TransitionCommands vers la zone du perso — une
    /// commande texte unique ([s16 node][i32 char][u16 1][u16 len]
    /// [cmd]) : le canal natif « WorldTextCommand » mission→zone que
    /// la zone dispatche dans sa table de commandes texte
    /// (Mission_TC_UpdateWarCoin/UpdateAlpacaCoin).</summary>
    private async Task SendZoneTextCommandAsync(int charUid,
        string command)
    {
        var owner = OwnerOf(charUid);
        if (owner == null) return;
        var bytes = Encoding.ASCII.GetBytes(command);
        await owner.SendAsync(58, Concat(
            BitConverter.GetBytes((short)0),        // s16 : node
            BitConverter.GetBytes(charUid),         // i32 : charUid
            BitConverter.GetBytes((ushort)1),       // u16 : 1 commande
            BitConverter.GetBytes((ushort)bytes.Length),
            bytes));
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var b = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }
}
