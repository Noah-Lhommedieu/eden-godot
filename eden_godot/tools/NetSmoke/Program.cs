using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Session;

// NetSmoke — rejoue toute la séquence login → entrée zone contre le
// serveur live, avec trace au format zsniff :
//   [HH:MM:SS.mmm] D ch=netsmoke NCID=<n> len=<L> <hex>   (client→serveur)
//   [HH:MM:SS.mmm] E ch=netsmoke NCID=<n> len=<L> <hex>   (serveur→client)
// Usage : NetSmoke [--host H] [--account A] [--password P] [--world-id N]
//                  [--char-id N] [--zone NAME] [--listen SEC] [--out FILE]

static string Arg(string[] args, string name, string def)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
}
static int ArgInt(string[] args, string name, int def)
    => int.TryParse(Arg(args, name, ""), out var v) ? v : def;

var opt = new EdenSessionOptions
{
    Host = Arg(args, "--host", "127.0.0.1"),
    LoginPort = ArgInt(args, "--login-port", 6543),
    Account = Arg(args, "--account", "admin"),
    Password = Arg(args, "--password", "admin"),
    ClientVersion = Arg(args, "--version", "002.003.05.67"),
    ZoneName = Arg(args, "--zone", "3966"),
    EntryListenSeconds = ArgInt(args, "--listen", 20),
};
if (Arg(args, "--world-id", "") is { Length: > 0 } w && short.TryParse(w, out var wid))
    opt.WorldId = wid;
if (Arg(args, "--char-id", "") is { Length: > 0 } c && uint.TryParse(c, out var cid))
    opt.CharId = cid;

var outPath = Arg(args, "--out", "");
StreamWriter? file = string.IsNullOrEmpty(outPath) ? null : new StreamWriter(outPath);

void Log(string line)
{
    Console.WriteLine(line);
    file?.WriteLine(line);
    file?.Flush();
}

var session = new EdenSession(opt);
session.Log += Log;
session.Frame += (sent, ncid, payload) =>
{
    var dir = sent ? 'D' : 'E';
    var now = DateTime.Now;
    Log($"[{now:HH:mm:ss}.{now.Millisecond:000}] {dir} ch=netsmoke " +
        $"NCID={ncid} len={payload.Length} {Convert.ToHexString(payload).ToLowerInvariant()}");
};

// --zone-entry : analyse les trames d'entités (spawn/move/despawn)
// reçues pendant la session — vérifie que nos messages entité se
// parsent sur le flux réel et comptabilise ce qui est résolu.
var zoneEntry = args.Contains("--zone-entry");
var stats = new ZoneEntityStats();
if (zoneEntry)
{
    void Hook<T>(T proto, Action<T> on) where T : NetMessage, new() =>
        session.OnZone(proto.Opcode, r =>
        {
            var m = new T();
            try { m.Deserialize(r); }
            catch (Exception ex)
            {
                stats.ParseErrors++;
                Log($"[zone-entry] ÉCHEC parse {m.Name} : {ex.Message}");
                return;
            }
            stats.Parsed++;
            on(m);
        });

    Hook(new ZoneEnterNodeNodeData(), m =>
        Log($"[zone-entry] NodeData node={m.Field1} times={m.Field2} " +
            $"pos=({m.X:F2},{m.Y:F2},{m.Z:F2})"));
    Hook(new ZoneEnterNodePlayerData(), m =>
        Log($"[zone-entry] PlayerData charId={m.CharId} " +
            $"name={m.CharName} rest={m.Rest.Length}B"));
    Hook(new ZoneSeeMob(), m =>
    {
        stats.SeeMob++;
        if (m.Kind == 8) stats.SeeNpc++; else if (m.Kind == 12) stats.SeeMon++;
        if (stats.SeeMob <= 5 || stats.SeeMob % 20 == 0)
            Log($"[zone-entry] SeeMob h=0x{m.Handle:x8} eid={m.EntityId & 0xFFFF} " +
                $"kind={m.Kind} pos=({m.X:F1},{m.Y:F1}) f3={m.FieldZ:F2} " +
                $"f4={m.FieldSpeed:F2} hp={m.Hp}/{m.MaxHp} «{m.DisplayName}»");
    });
    Hook(new ZoneSeeChar(), m =>
    {
        stats.SeeChar++;
        Log($"[zone-entry] SeeChar charId={m.CharId} lv={m.Level} " +
            $"pos=({m.X:F1},{m.Y:F1}) dir={m.Facing:F2} spd={m.MoveSpeed:F1}");
    });
    Hook(new ZoneMovePrediction(), m =>
    {
        stats.MovePred++;
        if (stats.MovePred <= 3)
            Log($"[zone-entry] MovePred h=0x{m.Handle:x8} " +
                $"cur=({m.CurPos.X:F1},{m.CurPos.Y:F1}) flag={m.Flag} " +
                $"dir={m.FacingFloat:F2} dst={m.DstPos}");
    });
    Hook(new ZoneMoveChar(), m =>
    {
        stats.MoveChar++;
        if (stats.MoveChar <= 3)
            Log($"[zone-entry] MoveChar self pos=({m.X:F1},{m.Y:F1})");
    });
    Hook(new ZoneLifeEradicate(), m =>
    {
        stats.Eradicate++;
        Log($"[zone-entry] LifeEradicate h=0x{m.Handle:x8}");
    });
    Hook(new ZoneLeaveNode(), m =>
        Log($"[zone-entry] LeaveNode h=0x{m.Value:x8}"));
    Hook(new ZoneClientGoto(), m =>
        Log($"[zone-entry] Goto h=0x{m.Handle:x8} ({m.X:F1},{m.Y:F1}) f={m.Flag}"));
    Hook(new ZoneUpdateAttr(), m => stats.UpdateAttr++);
    Hook(new ZoneDisplayMonster(), m =>
        Log($"[zone-entry] DisplayMonster h=0x{m.MobHandle:x8} " +
            $"owner=0x{m.OwnerHandle:x8}"));
    Hook(new ZoneMobTransform(), m =>
        Log($"[zone-entry] MobTransform h=0x{m.Handle:x8} → {m.NewTemplateId}"));
    Hook(new ZoneUpdateZoneMonster(), m =>
        Log($"[zone-entry] UpdateZoneMonster h=0x{m.Handle:x8}"));
    Hook(new ZoneSeeTreasure(), m =>
        Log($"[zone-entry] SeeTreasure h=0x{m.Handle:x8} item={m.ItemId}×{m.Count}"));
    Hook(new ZoneTreasureDisappear(), m =>
        Log($"[zone-entry] TreasureDisappear h=0x{m.Value:x8}"));
    Hook(new ZoneNodeExit(), m =>
        Log($"[zone-entry] NodeExit {m.Field1}/{m.Field2}"));
    Hook(new ZoneModelModify(), m =>
        Log($"[zone-entry] ModelModify h=0x{m.Handle:x8}"));
    Hook(new ZoneAttrRefresh(), m => stats.AttrRefresh++);
    Hook(new ZoneEnterNodeEquipAndSpell(), m =>
        Log($"[zone-entry] EquipAndSpell {m.Body.Length}B"));
    Hook(new ZoneEnterNodeInventory(), m =>
        Log($"[zone-entry] Inventory {m.Body.Length}B"));
}

try
{
    // --granular : rejoue le flux EN ÉTAPES comme les écrans du
    // client (chaque étape réutilise la même EdenSession) —
    // ConnectGateway → Login → SelectWorld → WorldLogin →
    // ReceiveCharacterList → SelectCharacter → GetZoneTicket →
    // ZoneConnect → ZoneEntry. Prouve que la session « attachée »
    // du flux UI fonctionne bout à bout.
    if (args.Contains("--granular"))
    {
        await session.ConnectGatewayAsync();
        var worlds = await session.LoginAsync();
        var wid2 = opt.WorldId
            ?? (worlds.LastWorldId != 0 ? worlds.LastWorldId
                : worlds.Worlds[0].Id);
        await session.SelectWorldAsync(wid2);
        await session.WorldLoginAsync();
        await session.ReceiveCharacterListAsync();
        if (args.Contains("--chars") || zoneEntry)
        {
            foreach (var ch in session.Characters)
            {
                var body = ch.Appearance is { Length: >= 29 }
                    ? ch.Appearance[28] >> 4 : -1;
                Log($"[netsmoke] char #{ch.Id} «{ch.Name}» " +
                    $"class={ch.ClassId} classLv={ch.ClassLv} " +
                    $"race={ch.RaceId} lv={ch.Level} node={ch.NodeId} " +
                    $"bodyCode={body} app=" +
                    $"{(ch.Appearance != null ? Convert.ToHexString(ch.Appearance).ToLowerInvariant() : "-")}");
            }
        }
        var cid2 = opt.CharId
            ?? (uint)session.Characters[0].Id;
        await session.SelectCharacterAsync(cid2);
        await session.GetZoneTicketAsync();
        await session.ZoneConnectAsync();
        await session.ZoneEntryAsync();
        Log($"[netsmoke] --granular : entrée zone OK char #{cid2}");
    }
    else
    {
        await session.RunFullAsync();
    }

    // --chars : dump de la liste persos (race/classe/code corps du
    // blob apparence) — validation du mapping modèle char/*.
    if (args.Contains("--chars") || zoneEntry)
    {
        foreach (var ch in session.Characters)
        {
            var appHex = ch.Appearance != null
                ? Convert.ToHexString(ch.Appearance).ToLowerInvariant()
                : "-";
            var body = ch.Appearance is { Length: >= 29 }
                ? ch.Appearance[28] >> 4 : -1;
            Log($"[netsmoke] char #{ch.Id} «{ch.Name}» class={ch.ClassId} " +
                $"classLv={ch.ClassLv} race={ch.RaceId} lv={ch.Level} " +
                $"node={ch.NodeId} bodyCode={body} app={appHex}");
        }
    }

    // --move-test : envoie quelques MoveSelf (NCID 1, layout capture)
    // autour de la position self pour vérifier l'acceptation serveur.
    if (args.Contains("--move-test"))
    {
        Log("[netsmoke] envoi de 3 MoveSelf de test…");
        for (var i = 0; i < 3; i++)
        {
            var m = new ZoneMoveSelf { MoveType = 7 };
            m.Tail[0] = 179.3f + i;   // x courant
            m.Tail[1] = 284.3f + i;   // y courant
            m.Tail[2] = 180.0f + i;   // dst x
            m.Tail[3] = 285.0f + i;   // dst y
            m.Tail[4] = 1.57f;        // dir
            await session.SendZoneAsync(m);
            await Task.Delay(300);
        }
    }

    var counts = await session.ListenZoneAsync(opt.EntryListenSeconds);
    Log("[netsmoke] NCIDs reçus en zone : " +
        string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}×{kv.Value}")));
    if (zoneEntry)
        Log($"[zone-entry] stats : parsés={stats.Parsed} " +
            $"erreurs={stats.ParseErrors} | SeeMob={stats.SeeMob} " +
            $"(npc={stats.SeeNpc} mob={stats.SeeMon}) " +
            $"SeeChar={stats.SeeChar} MovePred={stats.MovePred} " +
            $"MoveChar={stats.MoveChar} Eradicate={stats.Eradicate} " +
            $"UpdateAttr={stats.UpdateAttr} AttrRefresh={stats.AttrRefresh}");
    Log("[netsmoke] OK — entrée zone terminée");
    file?.Close();
    return 0;
}
catch (Exception ex)
{
    Log($"[netsmoke] ÉCHEC : {ex.GetType().Name} {ex.Message}");
    file?.Close();
    return 1;
}

/// <summary>Compteurs des trames d'entités zone parsées en --zone-entry.</summary>
sealed class ZoneEntityStats
{
    public int Parsed;
    public int ParseErrors;
    public int SeeMob;
    public int SeeNpc;
    public int SeeMon;
    public int SeeChar;
    public int MovePred;
    public int MoveChar;
    public int Eradicate;
    public int UpdateAttr;
    public int AttrRefresh;
}
