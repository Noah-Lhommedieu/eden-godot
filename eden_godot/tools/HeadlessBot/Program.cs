// Bot headless Eden Limitless — deuxième partie des preuves live
// multi-acteurs SANS lancer un 2e client Godot (règle du plan v2 :
// deux instances Godot = freeze du PC). Réutilise la pile réelle
// EdenSession (TLS moderne + varint) — côté serveur c'est un vrai
// client ; côté preuve il valide le flux protocole, pas le visuel.
//
// Usage : dotnet run --project tools/HeadlessBot -- duel-accept
//     [account] [password] [x] [y]
//
// duel-accept : login test123 → 1er perso → entrée zone →
//   //transport_node 1 + //tele x y (défaut marché 247 396), puis
//   répond 136 (accept) à chaque 451 reçu jusqu'au 453 (DuelBegin)
//   ou timeout. Le challenger GUI (--duel-test) fait le reste.

using EdenClient;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Session;

var mode = args.Length > 0 ? args[0] : "";
string account, password, teleX, teleY, mailSubject = "";
uint certCharId = 0; short certLicId = 0, certClassId = 1;
byte certSlot = 1;
if (mode == "mail-check")
{
    mailSubject = args.Length > 1 ? args[1] : "limitless-";
    account = args.Length > 2 ? args[2] : "admin";
    password = args.Length > 3 ? args[3] : "adminmdp";
    teleX = teleY = "";
}
else if (mode == "cert-test")
{
    // cert-test <charId> <licItemId> <slot> [classId] — preuve live
    // chaîne certificats → 296 : entre en zone sur le perso voulu,
    // pose la licence via 182 et capture le nouveau bloc stats.
    account = "test123"; password = "test123"; teleX = teleY = "";
    if (args.Length < 4 ||
        !uint.TryParse(args[1], out certCharId) ||
        !short.TryParse(args[2], out certLicId) ||
        !byte.TryParse(args[3], out certSlot))
    {
        Console.Error.WriteLine("usage : HeadlessBot cert-test " +
            "<charId> <licItemId> <slot> [classId]");
        return 2;
    }
    if (args.Length > 4) short.TryParse(args[4], out certClassId);
}
else
{
    account = args.Length > 1 ? args[1] : "test123";
    password = args.Length > 2 ? args[2] : "test123";
    teleX = args.Length > 3 ? args[3] : "247";
    teleY = args.Length > 4 ? args[4] : "396";
}

if (mode != "duel-accept" && mode != "mail-check" &&
    mode != "cert-test" && mode != "enchant-test")
{
    Console.Error.WriteLine("usage : HeadlessBot duel-accept " +
        "[account] [password] [x] [y]\n" +
        "       HeadlessBot mail-check <sujet> [account] [password]\n" +
        "       HeadlessBot cert-test <charId> <licItemId> <slot> " +
        "[classId]\n" +
        "       HeadlessBot enchant-test [account] [password]");
    return 2;
}

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
var ct = cts.Token;
// Même pinning TLS que le client GUI : relire client.cfg du projet.
var cfgText = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "godot_project", "gamedata", "client.cfg"));
var cfg = ClientConfig.Parse(cfgText);
var s = new EdenSession(new EdenSessionOptions
{
    Host = "127.0.0.1", Account = account, Password = password,
    TlsThumbprint = cfg.TlsThumbprint, TlsInsecure = cfg.TlsInsecure,
});
s.Log += m => Console.WriteLine(m);
s.Frame += (sent, ncid, payload) => Console.WriteLine(
    $"[wire] {(sent ? "D" : "E")} NCID={ncid} len={payload.Length} " +
    Convert.ToHexString(payload).ToLowerInvariant());

var mailHit = false;
var last296 = "";
if (mode == "cert-test")
{
    // Décodage 296 (mapping ProfileStatEmitter) : str,int,agi,wis,lck
    // F1-5 ; résists F6-14 ; zéro F15 ; MaxMP F16 ; P-ATK F17 ;
    // M-ATK F18 ; DEF F21 ; ratings F22-29.
    s.OnZone(296, r =>
    {
        var m = new ZoneAttrRefresh();
        m.Deserialize(r);
        last296 = $"str={m.Fields1to14[0]} int={m.Fields1to14[1]} " +
            $"agi={m.Fields1to14[2]} wis={m.Fields1to14[3]} " +
            $"lck={m.Fields1to14[4]} mp={m.Fields15to18[1]} " +
            $"patk={m.Fields15to18[2]} matk={m.Fields15to18[3]} " +
            $"def={m.Field21} parry={m.Fields22to29[0]} " +
            $"block={m.Fields22to29[1]} hit={m.Fields22to29[2]} " +
            $"dodge={m.Fields22to29[3]} crit={m.Fields22to29[4]} " +
            $"critdmg={m.Fields22to29[5]} mcrit={m.Fields22to29[6]} " +
            $"mcritdmg={m.Fields22to29[7]}";
        Console.WriteLine($"[bot] <<< 296 {last296}");
    });
    s.OnZone(509, r =>
    {
        var m = new ZonePlayerLicense();
        m.Deserialize(r);
        Console.WriteLine("[bot] <<< 509 licences : " +
            string.Join(", ", m.Entries.Select(
                e => $"{(ushort)e.ItemId}:loc{e.Loc}")));
    });
}
else if (mode == "duel-accept")
{
    // Réponse au défi : 451 [i32 demandeur][str nom] → 136 [s16][i32].
    s.OnZone(451, r =>
    {
        var req = new ZoneClientDuelRequest();
        req.Deserialize(r);
        Console.WriteLine($"[bot] 451 défi de «{req.Field2}» " +
            $"({req.Field1:x8}) — 136 accept");
        _ = s.SendZoneAsync(new ZoneDuelRequestAnswer
            { Accept = 1, RequesterId = req.Field1 }, ct);
    });
}
else
{
    // mail-check : parse chaque liste 400, matche le préfixe sujet.
    s.OnZone(400, r =>
    {
        var l = new ZoneMailList();
        l.Deserialize(r);
        Console.WriteLine($"[bot] 400 liste {l.Entries.Count} mail(s)");
        foreach (var e in l.Entries)
        {
            var mark = e.Subject.StartsWith(mailSubject) ? " <<<" : "";
            if (mark.Length > 0) mailHit = true;
            Console.WriteLine($"[bot]   #{e.MailId} de «{e.SenderName}» " +
                $"sujet=«{e.Subject}» or={e.AttachGold}{mark}");
        }
    });
}

Console.WriteLine($"[bot] login {account}…");
await s.EnterZoneAsync(charId: mode == "cert-test" ? certCharId : null,
    ct: ct).ConfigureAwait(false);
Console.WriteLine($"[bot] en zone char#{s.SelectedCharId}");

// Pompe de réception : démarre AVANT les envois du mode pour ne
// rater aucune trame serveur (296/509 post-entrée compris).
var listen = s.ZoneListenLoopAsync(ct);

if (mode == "cert-test")
{
    // Attente de la fin du burst d'entrée (296/492/493 initiaux +
    // 509 déjà loggés ci-dessus), puis requête 181 + pose 182.
    await Task.Delay(3000, ct).ConfigureAwait(false);
    var before = last296;
    Console.WriteLine($"[bot] --- baseline 296 : {before}");
    await s.SendZoneAsync(new ZoneServerQueryLicense(), ct)
        .ConfigureAwait(false);
    await Task.Delay(800, ct).ConfigureAwait(false);
    var msg = new ZoneServerPlayerLicense { ClassId = certClassId };
    msg.Selections.Add((certLicId, certSlot));
    Console.WriteLine($"[bot] >>> 182 lic={certLicId} slot={certSlot} " +
        $"class={certClassId}");
    await s.SendZoneAsync(msg, ct).ConfigureAwait(false);
    await Task.Delay(4000, ct).ConfigureAwait(false);
    Console.WriteLine($"[bot] --- après 182 : {last296}");
    return last296 == before ? 1 : 0;   // 0 = stats changées
}
else if (mode == "duel-accept")
{
    // Même rendez-vous que le pilote GUI : node 1 + téléport marché.
    await s.SendZoneAsync(new ZoneTextCommand
        { Text = "//transport_node 1" }, ct).ConfigureAwait(false);
    await Task.Delay(6000, ct).ConfigureAwait(false);
    await s.SendZoneAsync(new ZoneTextCommand
        { Text = $"//tele {teleX} {teleY}" }, ct).ConfigureAwait(false);
    Console.WriteLine(
        $"[bot] //tele {teleX} {teleY} — en attente du 453");
}
else
{
    // Ouvre la boîte (95/7 → 399) puis poll la liste (96 → 400).
    await s.SendZoneAsync(new ZoneMailOpenBox { Op = 7 }, ct)
        .ConfigureAwait(false);
    await Task.Delay(3000, ct).ConfigureAwait(false);
    _ = Task.Run(async () =>
    {
        while (!mailHit && !ct.IsCancellationRequested)
        {
            try
            {
                await s.SendZoneAsync(new ZoneMailQuery(), ct)
                    .ConfigureAwait(false);
            }
            catch { break; }
            await Task.Delay(8000, ct).ConfigureAwait(false);
        }
    }, ct);
    Console.WriteLine(
        $"[bot] mail-check «{mailSubject}*» — poll 96 toutes les 8 s");
}

// Écoute jusqu'au verdict du mode ou fin du budget.
var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
{
    await Task.Delay(1000, ct).ConfigureAwait(false);
    if (mode == "mail-check" && mailHit) break;
    if (listen.IsCompleted) break;
}
Console.WriteLine($"[bot] fin — " +
    (mode == "mail-check" ? $"mailHit={mailHit}" : "écoute terminée"));
return mailHit || mode == "duel-accept" ? 0 : 1;
