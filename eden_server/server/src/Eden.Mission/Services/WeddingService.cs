using Eden.Core.Logging;
namespace Eden.Mission.Services;

/// <summary>
/// Fabrique de cérémonies de mariage — port de `CWeddingFactory` +
/// `CEventWedding` (mission @0x80da150-0x80db050, @0x81eaf10).
///
/// Les mariages sont des cérémonies planifiées en mémoire (pas de
/// table SQL côté natif : le vecteur `CWeddingFactory+4` vit et meurt
/// avec le processus mission). Max 5 simultanés (check `ReserveChurch`
/// @0x81ec6c0). Chaque transition de statut rediffuse la liste à tous
/// les persos abonnés (`Weddinginfo` @0x80da240 → `this+0x10` set de
/// charUid rempli par `WeddinginfoQuery flag=1`).
///
/// Machine à statuts (`CEventWedding` ctor + `OnEventTimeUp`) :
///   0 créé      → événement immédiat → 1
///   1 préparat. → +600 s → 2 (natif exige ≥2 amants présents sinon →3)
///   2 cérémonie → +7200 s (0x1c20) → 3
///   3 clôture   → +600 s → `CloseWedding` (suppression + broadcast)
/// `GetWeddingStepEndTime` @0x81dba10 : status 1/3 → step+600,
/// status 2 → step+7200.
/// </summary>
public sealed class WeddingService : IDisposable
{
    /// <summary>Item « carte d'invitation » requis par `ReserverChuch`
    /// (scan rb-tree items du natif, `(s16)0x667f`).</summary>
    public const int InvitationItemId = 0x667f;

    /// <summary>Plafond natif de cérémonies simultanées.</summary>
    public const int MaxWeddings = 5;

    // Textes natifs (TC_ReserveChurch/TC_CloseWedding, canal
    // TextOutputIndex flag 0x9b) :
    private const short TextFull = 0x1c61;        // salle pleine (≥5)
    private const short TextNoWedding = 0x1c64;   // pas de cérémonie
    private const short TextNotJoinable = 0x1c69; // status ≠ 1 à la close
    private const short TextReserved = 0x1c68;    // déjà une réservation
    private const short TextNoCard = 0x1c62;      // carte d'invitation absente
    private const short TextReservedOk = 0x1c63;  // réservation faite

    /// <summary>Un enregistrement `CWedding` natif (+4 loveId, +8 status,
    /// +0xc applicant, +0x10 tick du dernier statut, +0x1c/+0x20 noms).</summary>
    public sealed class Wedding
    {
        public int LoveId { get; init; }          // +4  : player_couples.id
        public byte Status { get; set; }          // +8  : EWeddingStatus u8
        public int ApplicantId { get; init; }     // +0xc : demandeur
        public long StatusSince { get; set; }     // +0x10: epoch du statut
        public string Name1 { get; init; } = "";  // +0x1c: nom du demandeur
        public string Name2 { get; init; } = "";  // +0x20: nom du partenaire
        public int Partner1Id { get; init; }      // couple.partner1_id
        public int Partner2Id { get; init; }      // couple.partner2_id
    }

    private readonly List<Wedding> _weddings = new();
    private readonly HashSet<int> _subscribers = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Func<int, bool> _isOnline;
    private readonly Func<int, Task>? _sendList;
    private readonly Func<int, short, Task>? _sendText;

    /// <param name="isOnline">Présence du perso (proxy de « dans le node
    /// mariage » — le node natif n'est pas porté ; les deux amants
    /// connectés suffisent, voir Join).</param>
    /// <param name="sendList">Envoie le ZMis 193 (requester, records).</param>
    /// <param name="sendText">Envoie le TextOutputIndex 66.</param>
    public WeddingService(Func<int, bool> isOnline,
        Func<int, Task>? sendList = null,
        Func<int, short, Task>? sendText = null)
    {
        _isOnline = isOnline;
        _sendList = sendList;
        _sendText = sendText;
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>Copie défensive de la liste (mutée par le tick).</summary>
    public IReadOnlyList<Wedding> Weddings
    { get { lock (_weddings) return _weddings.ToArray(); } }

    /// <summary>Persos abonnés aux rediffusions (set natif +0x10).</summary>
    public IReadOnlyCollection<int> Subscribers
    { get { lock (_subscribers) return _subscribers.ToArray(); } }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Durée de l'étape courante en secondes —
    /// `GetWeddingStepEndTime` : 1 et 3 = +600, 2 = +0x1c20 (7200).</summary>
    public static int StepDuration(byte status) => status switch
    {
        1 or 3 => 600,
        2 => 0x1c20,
        _ => 0,
    };

    /// <summary>`WeddinginfoQuery(char, flag)` @0x80dac00 : flag≠0
    /// abonne le perso aux rediffusions, puis lui envoie la liste —
    /// chaque record porte `[loveId,name1,name2,status,now,
    /// stepEnd-now]` (le +0x14/+0x18 rafraîchis à l'émission).</summary>
    public async Task InfoQueryAsync(int charUid, bool subscribe)
    {
        lock (_subscribers)
        {
            if (subscribe) _subscribers.Add(charUid);
            else _subscribers.Remove(charUid);
        }
        await SendListAsync(charUid);
    }

    /// <summary>`TC_ReserveChurch` @0x81ec6c0 : validations natives
    /// dans l'ordre (plein → 0x1c61, déjà réservé → 0x1c68, carte
    /// manquante → 0x1c62), puis `CreateWedding(loveId, char)` —
    /// statut 0, l'événement immédiat le passe à 1 au prochain tick.</summary>
    public async Task ReserveAsync(int charUid, MissionDb db)
    {
        // Accès DB hors verrou (lent) ; validation+insertion atomique.
        var couple = await db.GetCoupleAsync(charUid);
        // Invitation : item unitaire (stackable=false → count lignes,
        // sémantique CheckItemNum non-stack).
        var hasCard = await db.CountItemAsync(
            charUid, InvitationItemId, false) > 0;
        var other = couple is { } c
            ? (c.Partner1Id == charUid ? c.Partner2Id : c.Partner1Id) : 0;
        var name1 = couple != null
            ? await db.GetCharacterNameAsync(charUid) ?? "" : "";
        var name2 = couple != null
            ? await db.GetCharacterNameAsync(other) ?? "" : "";
        short? text;
        lock (_weddings)
        {
            if (_weddings.Count >= MaxWeddings) text = TextFull;
            else if (couple == null) text = TextNoWedding;
            else if (_weddings.Any(w => w.LoveId == couple.Value.LoveId))
                text = TextReserved;
            // Scan natif : rb-tree des items du perso à la recherche
            // du s16 0x667f — COUNT sur la vue inventory (bags 1∪2).
            else if (!hasCard) text = TextNoCard;
            else
            {
                text = null;
                _weddings.Add(new Wedding
                {
                    LoveId = couple.Value.LoveId,
                    ApplicantId = charUid,
                    StatusSince = Now(),
                    Name1 = name1, Name2 = name2,
                    Partner1Id = couple.Value.Partner1Id,
                    Partner2Id = couple.Value.Partner2Id,
                });
            }
        }
        await TextAsync(charUid, text ?? TextReservedOk);
        if (text == null) await BroadcastAsync();
    }

    /// <summary>`CWeddingFactory::CreateWedding(loveId, char)` —
    /// enregistre une cérémonie statut 0 (événement immédiat → statut 1
    /// au tick suivant). Sans doublon sur `loveId` ; retourne false si
    /// le couple a déjà une réservation.</summary>
    public bool CreateWedding(int loveId, int applicantId,
        string name1, string name2, int partner1Id, int partner2Id)
    {
        lock (_weddings)
        {
            if (_weddings.Any(w => w.LoveId == loveId)) return false;
            _weddings.Add(new Wedding
            {
                LoveId = loveId, ApplicantId = applicantId,
                StatusSince = Now(),
                Name1 = name1, Name2 = name2,
                Partner1Id = partner1Id, Partner2Id = partner2Id,
            });
        }
        return true;
    }

    /// <summary>`TC_CloseWedding` @0x81ec900 : clôture forcée d'une
    /// réservation — n'autorisé qu'en statut 1 (préparatifs), sinon
    /// 0x1c64 (pas de cérémonie) ou 0x1c69 (statut invalide).</summary>
    public async Task CloseAsync(int charUid, MissionDb db)
    {
        var couple = await db.GetCoupleAsync(charUid);
        short? text;
        lock (_weddings)
        {
            var w = couple == null
                ? null
                : _weddings.FirstOrDefault(x => x.LoveId == couple.Value.LoveId);
            if (w == null) text = TextNoWedding;
            else if (w.Status != 1) text = TextNotJoinable;
            else
            {
                text = null;
                // Natif : OnEventTimeUp(id, 3) → CloseWedding.
                _weddings.Remove(w);
            }
        }
        if (text != null) await TextAsync(charUid, text.Value);
        else await BroadcastAsync(); // Weddinginfo natif post-close
    }

    /// <summary>`OnEventTimeUp(loveId, status)` @0x80da830 — appelé
    /// par le tick quand l'étape courante est échue : status&lt;3 →
    /// status+1, status 3 → suppression. La transition 1→2 exige les
    /// deux amants présents (natif : `wedding+0x50` rempli par
    /// `Join`/`VisitWedding` — le node mariage n'est pas porté :
    /// proxy = les deux amants connectés, sinon fermeture comme le
    /// natif). Publique pour permettre le pump manuel dans les tests.</summary>
    public async Task AdvanceAsync()
    {
        var now = Now();
        var changed = false;
        lock (_weddings)
        {
            for (var i = _weddings.Count - 1; i >= 0; i--)
            {
                var w = _weddings[i];
                var stepEnd = w.StatusSince + StepDuration(w.Status);
                if (w.Status == 0)
                {
                    // CEventWedding status 0 : `+8 = now` → feu au
                    // premier tick → SetStatus(1) (reset +0x10 = now).
                    w.Status = 1;
                    w.StatusSince = now;
                    changed = true;
                    continue;
                }
                if (now < stepEnd) continue;
                if (w.Status == 1 &&
                    !(_isOnline(w.Partner1Id) && _isOnline(w.Partner2Id)))
                {
                    // Préparatifs écoulés sans les deux amants dans
                    // le node → OnEventTimeUp(id,3) : fermeture directe.
                    _weddings.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (w.Status >= 3)
                {
                    _weddings.RemoveAt(i); // CloseWedding natif
                }
                else
                {
                    w.Status++;             // SetStatus → +0x10 = now
                    w.StatusSince = now;
                }
                changed = true;
            }
        }
        if (changed) await BroadcastAsync();
    }

    /// <summary>Envoie le ZMis 193 au requester (format
    /// `CNC_ZMis_ZoneServerWeddingsInfo::Serialize` @0x80db220).</summary>
    private Task SendListAsync(int charUid)
        => _sendList == null ? Task.CompletedTask : _sendList(charUid);

    /// <summary>`Weddinginfo()` @0x80da240 : rediffuse la liste à tous
    /// les abonnés encore routables.</summary>
    private async Task BroadcastAsync()
    {
        int[] uids;
        lock (_subscribers) uids = _subscribers.ToArray();
        foreach (var uid in uids) await SendListAsync(uid);
    }

    private Task TextAsync(int charUid, short textId)
        => _sendText == null ? Task.CompletedTask : _sendText(charUid, textId);

    /// <summary>Corps ZMis 193 — les champs temps sont rafraîchis à
    /// l'émission (`+0x14 = now`, `+0x18 = stepEnd - now`).</summary>
    public List<(int LoveId, string Name1, string Name2,
        byte Status, int StartTime, int Remaining)> Snapshot()
    {
        var now = Now();
        lock (_weddings)
            return _weddings.Select(w => (w.LoveId, w.Name1, w.Name2,
                w.Status, (int)now,
                (int)(w.StatusSince + StepDuration(w.Status) - now)))
                .ToList();
    }

    private async Task LoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                try { await AdvanceAsync(); }
                catch (Exception ex)
                { EdenLog.Write($"[Wedding] tick : {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
