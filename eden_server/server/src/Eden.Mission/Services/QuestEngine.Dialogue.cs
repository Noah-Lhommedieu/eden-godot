using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Moteur de quêtes du MissionServer C# — reconstruit le comportement
/// natif observable : ListDialogue (56) fusionne les #:Talk du NPC
/// (S_Npc.ini) et les entrées de quête (S_Mission.ini : col11 = NPC
/// donneur, col16 = objectif, col17 = NPC de rendu, col20 = récompenses).
/// Le clic (128) renvoie un selId opaque que le moteur encode lui-même
/// (le client ne fait que l'écho) :
///   1..999        = ordinal du #:Talk du NPC → réponse 57 ctx=bodyId
///   1_000_000+    = quête : (selId/10)%100000 = missionId, selId%10 =
///                   action (0=mission-open, 1=mission-finish,
///                   2=mission-on-going) → 58 TransitionCommands
///                   "shop &lt;verb&gt; &lt;npcUid&gt; &lt;mid&gt;" (la zone
///                   traduit en 388 EnterMissionList, flux prouvé).
/// Les quêtes s'acceptent/rendent ensuite via MissionServiceOp (NCID 21
/// DBA relayé du client 87 MissionListOp : op 1=accept, 2=reward,
/// 3=on-going, 4=finish).
/// État persisté : personal_mission_states (index=mid,
/// state = status | progress&lt;&lt;8 ; status 1=active, 2=objectifs OK)
/// + player_mission_complete_num pour les quêtes terminées (If_FMS done).
/// </summary>
/// <summary>Partiel dialogue — parsing des dialogues NPC
/// (ParseNpcTalk, TalkEntry), carte des actions de dialogue,
/// envoi du livre de quêtes, sélections et actions de dialogue
/// (crédit d'objectifs NPC, transitions, réponse 57).</summary>
public sealed partial class QuestEngine
{
    /// <summary>Action associée à un selId de dialogue.</summary>
    public sealed record DialogAction(int Kind, int NpcUid, int Arg1, int Arg2);

    private readonly Dictionary<int, CharContext> _chars = new();
    /// <summary>NPC → nodes où il est placé (sections [npc] des
    /// scènes) — gate nodale de l'auto-accept.</summary>
    private readonly Dictionary<int, HashSet<int>> _npcNodes = new();

    public QuestEngine(GameDataService data, MissionDb db, string dataDir)
    {
        _data = data;
        _db = db;
        LoadMissionTitles(dataDir);
        LoadNpcNodeMap(dataDir);
    }

    /// <summary>Construit <see cref="_npcNodes"/> depuis les scènes
    /// (<c>Sxxx.ini → node xxx</c>). Ordre EDENFR : <c>SCENE_DIR</c>,
    /// sinon le corpus fr-full (<c>EDEN_REFORGED_DB</c>/../scene), sinon
    /// <c>dataDir/../scene</c> dé-lié.</summary>
    private void LoadNpcNodeMap(string dataDir)
    {
        var realDataDir = Directory.ResolveLinkTarget(dataDir, true)?.FullName
                          ?? dataDir;
        var frDb = ReforgedDataLocator.FindDbDir(realDataDir);
        var sceneDir = Eden.Core.EdenConfig.SceneDir
                       ?? (frDb != null
                           ? Path.Combine(frDb, "..", "scene") : null)
                       ?? Path.Combine(realDataDir, "..", "scene");
        if (!Directory.Exists(sceneDir)) return;
        foreach (var file in Directory.GetFiles(sceneDir, "*.ini"))
        {
            if (!int.TryParse(
                    Path.GetFileNameWithoutExtension(file)[1..],
                    out var node)) continue;
            foreach (var n in SceneParser.Parse(file).Npcs)
            {
                if (!_npcNodes.TryGetValue(n.NpcId, out var set))
                    _npcNodes[n.NpcId] = set = new HashSet<int>();
                set.Add(node);
            }
        }
        EdenLog.Write($"[Mission] carte npc→node : {_npcNodes.Count} PNJ");
    }
    /// <summary>Une option #:Talk du NPC — conditions `If_*`,
    /// actions exécutées à la sélection (`ReviveArea`, `InventoryItem`,
    /// `TPA`, `GiveBuff`, `Gold`, `Reputation`, `Service`,
    /// `Event_TriggerEvent`…) et `FailBody` = texte affiché quand les
    /// conditions échouent (segment `:NNNN` terminal — preuve :
    /// T_Dialogue 5012 « you didn't bring enough » derrière
    /// `If_InventoryItem 35480 >= 10`).</summary>
    public sealed record TalkEntry(int LabelId, int BodyId,
        IReadOnlyList<string> Conds, IReadOnlyList<string> Actions,
        int FailBody);

    /// <summary>Extrait ctx### (champ itemref du NPC) + les #:Talk
    /// du TalkScript.</summary>
    private static (int Ctx, List<TalkEntry> Talks) ParseNpcTalk(
        Eden.Core.Data.Parsers.WorldParsers.NpcDef? npc)
    {
        var talks = new List<TalkEntry>();
        var ctx = -1;
        if (npc == null) return (ctx, talks);
        // ItemRef "4001###" → texte ambient parlé par le NPC (vérifié :
        // 50001→4001, 50002→4002, 50003→4003, 50793→5314).
        var hashAt = npc.ItemRef.IndexOf('#');
        if (hashAt > 0 &&
            int.TryParse(npc.ItemRef[..hashAt], out var c))
            ctx = c;
        // Chaque segment « |…| » du script est une option
        // `#:Talk L B#:conds:#:actions:#:fail` — découpage partagé
        // WorldParsers.ParseDialogOption (grammaire InsertNPCTalkDB).
        foreach (var seg in npc.TalkScript.Split('|'))
        {
            if (!seg.Contains("#:Talk", StringComparison.Ordinal))
                continue;
            var opt = Eden.Core.Data.Parsers.WorldParsers
                .ParseDialogOption(seg);
            // `#:Talk L B` (label+corps) ET `#:Talk L` (le corps est
            // le label — tous les NPC téléport/classe : `Talk 4752
            // #:If_CharLevel…#:TPA 14 24`). L'exiger ≥3 tokens rendait
            // talks=0 → gossip local muet → TPA jamais exécuté.
            if (opt.Head.Length >= 2 && opt.Head[0] == "Talk" &&
                int.TryParse(opt.Head[1], out var label))
            {
                var body = opt.Head.Length >= 3 &&
                    int.TryParse(opt.Head[2], out var b2) ? b2 : label;
                talks.Add(new TalkEntry(label, body, opt.Conds,
                    opt.Actions, opt.FailBody));
            }
        }
        return (ctx, talks);
    }
    /// <summary>Vrai si une action de la quête cible ce NPC précis
    /// (NPCTalk &lt;npc&gt;, ItemGiven &lt;npc&gt; …) ou le submit NPC pour
    /// NPCItem — c'est ce qui rend la réponse quête visible chez lui.</summary>
    private static bool QuestTargetsNpc(MissionParser.MissionDef m,
        int npcUid)
    {
        foreach (var o in m.Actions)
            if (o.Args.Count >= 1 &&
                int.TryParse(o.Args[0], out var n) && n == npcUid &&
                o.Name is "NPCTalk" or "NPCMultiTalk" or "ItemGiven"
                    or "ItemGot")
                return true;
        return false;
    }
    /// <summary>Encodage selId quête : 1_000_000 + mid*10 + action.</summary>
    private static int SelOf(int mid, int action) => 1_000_000 + mid * 10 + action;
    /// <summary>127 → 56 : construit la réponse ListDialogue.
    /// Wire (miroir du parser zone) :
    ///   [i32 charUid][s16 nodeId][u8 type][i32 npcUid]
    ///   vec&lt;i32&gt; selId | vec&lt;s16&gt; a | vec&lt;s16&gt; statuts |
    ///   vec&lt;vec&lt;i32&gt;&gt; dispIds | vec&lt;vec&lt;Ss&gt;&gt; noms |
    ///   vec&lt;vec&lt;s16&gt;&gt; flags | vec&lt;vec&lt;s16&gt;&gt; b | [i32 ctx]</summary>
    public async Task OnListDialogueAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 19) return;
        var charUid = BitConverter.ToInt32(b, 0);    // i32 : uid perso
        var nodeId = BitConverter.ToInt16(b, 4);     // s16 : node
        var type = b[6];                             // u8  : type de vue
        var npcUid = BitConverter.ToInt32(b, 7);     // i32 : template NPC
        if (!_chars.TryGetValue(charUid, out var c)) return;
        c.Session = s;

        // type=8 : livre/parchemin (CEC_ItemTriggerEvent natif) —
        // npcUid = texteDebut<<16|nbPages ; chaque entrée affiche la
        // page S_Dialogue correspondante (résolue client via T_Dialogue).
        if (type == 8)
        {
            var first = (int)((uint)npcUid >> 16);
            var pages = npcUid & 0xffff;
            await SendBookAsync(s, c, charUid, nodeId, npcUid,
                first, Math.Max(1, pages));
            return;
        }

        var npc = _data.Npcs.FirstOrDefault(n => n.Id == npcUid);
        var (ctx, talks) = ParseNpcTalk(npc);
        var quests = QuestEntries(c, npcUid);

        // Aucun objectif n'est crédité au CLIC DROIT : le natif n'exécute
        // les actions d'étape (NPCTalk/ItemGiven/NPCItem…) qu'à la
        // SÉLECTION de la réponse quête dans la liste (op 128) — sinon
        // un simple contact complétait la quête sans que le joueur
        // choisisse l'entrée « L08. … » (bug live constaté).
        var sel = new List<int>();
        var aa = new List<short>();
        var stat = new List<short>();
        var disp = new List<List<int>>();
        var names = new List<List<string>>();
        var flgs = new List<List<short>>();
        var bb = new List<List<short>>();
        c.DialogMap.Clear();

        // Entrées quête — format natif DÉCODÉ de la capture
        // (zsniff 17:23:20.083, 285 après TalkToNPC 50004) :
        //   dispId = -3 (marqueur "entrée quête"), E = mid en TEXTE
        //   ("1012"), flag = FMS — le client résout titre/objectifs via
        //   SA table T_Mission. L'ancien displayId mid*10+4 affichait
        //   un texte T_Dialogue sans contexte quête → pas d'interface.
        // C stat porte le verbe (0=open,1=reward,2=on-going) — interne.
        // FLAG (F[i][j]) : le natif y pousse ReplyDialog._2_2_ =
        // GetFMS(exec, mid) — le FMS BRUT de la quête (mission
        // decompiled.c @0x81ac2xx) — c'est ce qui pilote l'icône du
        // choix (« ! » dispo / « ? » à rendre / grisé en cours).
        // Flag=0 constant affichait l'icône talk partout.
        //   open   → FMS réel (0 nouvelle, 6 reprenable)
        //   reward → 4 (objectifs remplis)
        //   en cours → 3
        foreach (var (selId, title, verb) in quests)
        {
            var mid = (selId - 1_000_000) / 10;
            var fmsFlag = verb == 1 ? 4
                : verb == 2 ? 3
                : c.FMS.GetValueOrDefault(mid);
            sel.Add(selId); aa.Add(0); stat.Add((short)verb);
            disp.Add(new List<int> { -3 });          // displayId -3 = quête
            names.Add(new List<string> { mid.ToString() }); // E = mid texte
            flgs.Add(new List<short> { (short)fmsFlag }); // s16 : FMS (icône)
            bb.Add(new List<short> { 0 });
            c.DialogMap[selId] = new DialogAction(2, npcUid, mid, selId % 10);
        }
        var ord = 0;
        c.LastTalks = talks;
        foreach (var t in talks)
        {
            var id = ++ord;                          // selId = ordinal 1-based
            sel.Add(id); aa.Add(0); stat.Add(0);
            disp.Add(new List<int> { t.LabelId });   // id du libellé
            names.Add(new List<string> { "" });      // texte résolu client
            flgs.Add(new List<short> { 0 });
            bb.Add(new List<short> { (short)t.BodyId });
            c.DialogMap[id] = new DialogAction(1, npcUid, ord - 1, t.BodyId);
        }
        // Options « #:Service » du NPC — la liste native les inclut
        // (DisplayId = dlgId, résolu via T_Dialogue). Sans elles tout
        // NPC mixte quête+service (banquier quête…) perdait ses options
        // quand la mission répondait. Kind 3 = service.
        c.LastServices = npc?.Services ??
            new List<Eden.Core.Data.Parsers.WorldParsers.NpcDef.NpcService>();
        foreach (var sv in c.LastServices)
        {
            var id = ++ord;
            sel.Add(id); aa.Add(0); stat.Add(0);
            disp.Add(new List<int> { sv.DialogId });
            names.Add(new List<string> { "" });
            flgs.Add(new List<short> { 0 });
            bb.Add(new List<short> { (short)sv.DialogId });
            c.DialogMap[id] = new DialogAction(3, npcUid, ord - 1 -
                talks.Count, sv.DialogId);
        }

        var outp = new List<byte>();
        void VecI32(List<int> v)
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var x in v) outp.AddRange(BitConverter.GetBytes(x));
        }
        void VecS16(List<short> v)
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var x in v) outp.AddRange(BitConverter.GetBytes(x));
        }
        outp.AddRange(BitConverter.GetBytes(charUid));   // i32 : charUid
        outp.AddRange(BitConverter.GetBytes(nodeId));    // s16 : nodeId
        outp.Add(type);                                  // u8  : type
        outp.AddRange(BitConverter.GetBytes(npcUid));    // i32 : npcUid
        VecI32(sel);                                     // A : selIds
        VecS16(aa);                                      // B : u16 a
        VecS16(stat);                                    // C : statuts
        outp.AddRange(BitConverter.GetBytes((ushort)disp.Count));
        foreach (var v in disp) VecI32(v);               // D : ids affichés
        outp.AddRange(BitConverter.GetBytes((ushort)names.Count));
        foreach (var v in names)                         // E : noms bruts
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var str in v)
            {
                // La zone décode E en GBK puis le ré-émet tel quel au
                // client — on écrit donc directement en GBK.
                var bytes = Encoding.GetEncoding("GBK").GetBytes(str);
                outp.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
                outp.AddRange(bytes);
            }
        }
        outp.AddRange(BitConverter.GetBytes((ushort)flgs.Count));
        foreach (var v in flgs) VecS16(v);               // F : flags
        outp.AddRange(BitConverter.GetBytes((ushort)bb.Count));
        foreach (var v in bb) VecS16(v);                 // G : u16 b
        outp.AddRange(BitConverter.GetBytes(ctx));       // i32 : ctx parlé

        await s.SendAsync(56, outp.ToArray());
        EdenLog.Write($"[Mission] 56 char={charUid} npc={npcUid} " +
            $"talks={talks.Count} quests={quests.Count} ctx={ctx}");
    }
    /// <summary>141 CloseMissionBrief — le client a fermé le panneau
    /// de dialogue/quêtes : purge de l'état dialogue ouvert (natif
    /// CGlobalInfo::CloseMissionList @0x080717c0 remet exec+0xcc/+0xce
    /// à 0 — un 128 ultérieur ne doit plus matcher d'entrée).</summary>
    public Task OnCloseBriefAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 4) return Task.CompletedTask;
        var charUid = BitConverter.ToInt32(b, 0);
        if (!_chars.TryGetValue(charUid, out var c))
            return Task.CompletedTask;
        c.DialogMap.Clear();
        c.LastTalks = new List<TalkEntry>();
        c.LastServices = new();
        return Task.CompletedTask;
    }
    /// <summary>Réponse 56 d'un livre/parchemin (ItemTriggerEvent) :
    /// une entrée par page, label+body = l'id du texte S_Dialogue
    /// (le client résout titre et corps via SA table T_Dialogue, même
    /// espace d'ids). DialogAction kind=4 → la sélection renvoie la
    /// page en 57.</summary>
    private async Task SendBookAsync(MissionSession s, CharContext c,
        int charUid, int nodeId, int npcUid, int first, int pages)
    {
        c.DialogMap.Clear();
        c.LastTalks = new List<TalkEntry>();
        c.LastServices = new();
        var sel = new List<int>();
        var aa = new List<short>();
        var stat = new List<short>();
        var disp = new List<List<int>>();
        var names = new List<List<string>>();
        var flgs = new List<List<short>>();
        var bb = new List<List<short>>();
        pages = Math.Min(pages, 200); // garde-fou paquet
        for (var i = 0; i < pages; i++)
        {
            var textId = first + i;
            var id = i + 1;
            sel.Add(id); aa.Add(0); stat.Add(0);
            disp.Add(new List<int> { textId });      // label = texte de la page
            names.Add(new List<string> { "" });
            flgs.Add(new List<short> { 0 });
            bb.Add(new List<short> { (short)textId });
            c.DialogMap[id] = new DialogAction(4, npcUid, 0, textId);
        }
        var outp = new List<byte>();
        void VecI32(List<int> v)
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var x in v) outp.AddRange(BitConverter.GetBytes(x));
        }
        void VecS16(List<short> v)
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var x in v) outp.AddRange(BitConverter.GetBytes(x));
        }
        outp.AddRange(BitConverter.GetBytes(charUid));   // i32 : charUid
        outp.AddRange(BitConverter.GetBytes(nodeId));    // s16 : nodeId
        outp.Add(8);                                     // u8  : type livre
        outp.AddRange(BitConverter.GetBytes(npcUid));    // i32 : clé packée
        VecI32(sel); VecS16(aa); VecS16(stat);
        outp.AddRange(BitConverter.GetBytes((ushort)disp.Count));
        foreach (var v in disp) VecI32(v);
        outp.AddRange(BitConverter.GetBytes((ushort)names.Count));
        foreach (var v in names)
        {
            outp.AddRange(BitConverter.GetBytes((ushort)v.Count));
            foreach (var str in v)
            {
                var bytes = Encoding.GetEncoding("GBK").GetBytes(str);
                outp.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
                outp.AddRange(bytes);
            }
        }
        outp.AddRange(BitConverter.GetBytes((ushort)flgs.Count));
        foreach (var v in flgs) VecS16(v);
        outp.AddRange(BitConverter.GetBytes((ushort)bb.Count));
        foreach (var v in bb) VecS16(v);
        outp.AddRange(BitConverter.GetBytes(0));         // i32 : ctx
        await s.SendAsync(56, outp.ToArray());
        EdenLog.Write($"[Mission] 56 livre char={charUid} " +
            $"pages {first}..{first + pages - 1}");
    }
    /// <summary>128 : sélection d'entrée — selId du DialogMap du perso
    /// (le client écho les champs stockés). Wire :
    /// [u16 n1][n1×i32][u16 n2][n2×s16][i32 dlgId][s16 a][s16 b]
    /// [i32 charUid][s16 nodeId].</summary>
    public async Task OnDialogueSelectedAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 4) return;
        var o = 0;
        var n1 = BitConverter.ToUInt16(b, o); o += 2 + n1 * 4;
        if (o + 2 > b.Length) return;
        var n2 = BitConverter.ToUInt16(b, o); o += 2 + n2 * 2;
        // 14 octets restants : selId i32 + a/b (s16+s16) + charUid i32
        // + nodeId s16 — le check à 18 droppait TOUS les clics (wire
        // réel = 18B au total, bug live : Bled sans réponse).
        if (o + 14 > b.Length) return;
        var dlgId = BitConverter.ToInt32(b, o); o += 4;   // i32 : selId
        o += 4;                                           // s16 a + s16 b
        var charUid = BitConverter.ToInt32(b, o); o += 4; // i32 : charUid
        var nodeId = BitConverter.ToInt16(b, o);          // s16 : nodeId
        if (!_chars.TryGetValue(charUid, out var c) ||
            !c.DialogMap.TryGetValue(dlgId, out var act))
        {
            EdenLog.Write($"[Mission] 128 char={charUid} " +
                $"dlg={dlgId} inconnu");
            return;
        }

        if (act.Kind == 1)
        {
            // Gossip : conditions évaluées à la sélection (natif :
            // scénario d'états) — réussite → actions + texte body ;
            // échec → texte FailBody (« you didn't bring enough »).
            var talk = act.Arg1 >= 0 && act.Arg1 < c.LastTalks.Count
                ? c.LastTalks[act.Arg1] : null;
            var ok = talk == null ||
                await TalkCondsPassAsync(c, talk.Conds);
            if (ok && talk != null)
                await RunDialogActionsAsync(s, c, nodeId, charUid,
                    act.NpcUid, talk.Actions);
            var bodyId = ok
                ? (talk?.BodyId ?? act.Arg2)
                : (talk?.FailBody > 0 ? talk.FailBody : act.Arg2);
            await SendReply57Async(s, charUid, nodeId, act.NpcUid, bodyId);
            return;
        }

        if (act.Kind == 4)
        {
            // Page de livre (ItemTriggerEvent) — le body est l'id du
            // texte S_Dialogue lui-même.
            await SendReply57Async(s, charUid, nodeId, act.NpcUid,
                act.Arg2);
            return;
        }

        if (act.Kind == 3)
        {
            // Option #:Service — TC_Service natif : conditions d'abord
            // (If_CharLevel…), sinon texte de refus.
            var sv = act.Arg1 >= 0 && act.Arg1 < c.LastServices.Count
                ? c.LastServices[act.Arg1] : null;
            if (sv == null) return;
            if (await TalkCondsPassAsync(c, sv.Conds))
                // `service <type> <ref> <extra>` — verbe numérique
                // zone → RunNpcServiceAsync (même table TC_Service).
                await SendTransitionAsync(s, nodeId, charUid,
                    $"service {sv.Type} {sv.RefId} {sv.Extra}");
            else if (sv.FailBody > 0)
                await SendReply57Async(s, charUid, nodeId, act.NpcUid,
                    sv.FailBody);
            return;
        }

        // Quête → crédit des objectifs qui ciblent CE NPC au moment où
        // la réponse est choisie (natif : l'étape NPCTalk/ItemGiven/
        // NPCItem s'exécute dans le dialogue déclenché par l'entrée,
        // jamais au simple clic droit). Puis 58 TransitionCommands
        // `shop <verb> <npcUid> <mid>` (flux prouvé : la mission pilote
        // l'UI quête via commandes zone). mission-finish n'arrive
        // qu'après le claim client (capture 17:23:20-22). Status 2
        // inclus : les quêtes talk-only sont marquées « remplies » à
        // l'acceptation (HasBlockingObjective exclut NPCTalk) mais le
        // contrôle strict AllPmsDone du rendu exige le PMS 50000+off
        // — sans ce crédit le finish est refusé en boucle.
        var mid = act.Arg1;
        if (c.Active.TryGetValue(mid, out var aq) &&
            aq.Status is 1 or 2 &&
            _data.Missions.FirstOrDefault(m => m.Id == mid) is { } qm)
            await CreditNpcObjectivesAsync(s, c, aq, qm, charUid,
                nodeId, act.NpcUid);
        var verb = act.Arg2 == 0 ? "mission-open"
            : aq?.Status == 2 ? "mission-reward"
            : act.Arg2 == 1 ? "mission-reward"
            : "mission-on-going";
        await SendTransitionAsync(s, nodeId, charUid,
            $"shop {verb} {act.NpcUid} {mid}");
        EdenLog.Write($"[Mission] 128 char={charUid} quest {mid} " +
            $"verb={verb}");
    }
    /// <summary>Évalue les conditions `If_*` d'une option de dialogue
    /// NPC — table décodée des verbes réellement présents dans
    /// c_npc.ini : If_CharLevel/If_InventoryItem/If_InventorySlot
    /// (slots libres du sac 0, désasm If_InventorySlot @0x80dbc70 =
    /// GetContainerTotalSize(0) − slots occupés ≥ n)/If_FMS/If_Gender/
    /// If_Class/If_Gold/If_Reputation. Les conditions sans état
    /// mission (If_CheckBuff, If_FamilyContributionPoint,
    /// If_MidNightLimit, If_FamilyCadre, If_Colosseum, If_Raid,
    /// If_Social…) passent en les journalisant — la politique « ne pas
    /// masquer » déjà retenue pour les conditions d'acceptation.</summary>
    private async Task<bool> TalkCondsPassAsync(CharContext c,
        IReadOnlyList<string> conds)
    {
        foreach (var raw in conds)
        {
            var t = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            int Num(int i) => i < t.Length &&
                int.TryParse(t[i], out var v) ? v : 0;
            // Comparateur texte (>= <= == != > <) — même table que les
            // conditions de quêtes.
            bool Cmp(int lhs, string op, int rhs) => op switch
            {
                ">=" => lhs >= rhs, "<=" => lhs <= rhs,
                "==" or "=" => lhs == rhs, "!=" or "<>" => lhs != rhs,
                ">" => lhs > rhs, "<" => lhs < rhs,
                _ => lhs >= rhs,
            };
            switch (t[0])
            {
                case "If_CharLevel":
                case "PlayerLevel":
                    if (t.Length >= 3 &&
                        !Cmp(c.Level, t[1], Num(2))) return false;
                    break;
                case "If_InventoryItem":
                    // `If_InventoryItem <id> >= <n>` — stock total.
                    if (t.Length >= 4 && !Cmp(
                            await _db.CountItemAsync(c.CharUid, Num(1),
                                Stackable(Num(1))),
                            t[2], Num(3))) return false;
                    break;
                case "If_InventorySlot":
                    // Désasm : slots LIBRES du sac 0 (capacité 64) ≥ n.
                    if (t.Length >= 3 && !Cmp(
                            await _db.CountFreeSlotsAsync(c.CharUid),
                            t[1], Num(2))) return false;
                    break;
                case "If_FMS":
                    // `If_FMS <mid> done|1..7` — état final_mission_state.
                    if (t.Length >= 3)
                    {
                        var fms = c.FMS.GetValueOrDefault(Num(1));
                        var ok = t[2] == "done" ? fms is 1 or 5
                            : int.TryParse(t[2], out var fs) && fms == fs;
                        if (!ok) return false;
                    }
                    break;
                case "If_Gender":
                    // Liste de genres acceptés (natif : boucle sur
                    // tous les args, `1<<arg & 1<<(genderByte>>4)` ≠ 0
                    // — le gender_id vit en high nibble du byte).
                    if (t.Length >= 2 &&
                        !t.Skip(1).Any(x =>
                            int.TryParse(x, out var g) && g == c.Gender))
                        return false;
                    break;
                case "If_Class":
                    if (t.Length >= 2 && c.ClassId != Num(1)) return false;
                    break;
                case "If_Gold":
                    if (t.Length >= 3 && !Cmp(
                            (int)Math.Min(int.MaxValue,
                                await _db.GetGoldAsync(c.CharUid)),
                            t[1], Num(2))) return false;
                    break;
                case "If_Reputation":
                    // `If_Reputation <id> >= <n>` — valeur prestige.
                    if (t.Length >= 4 && !Cmp(
                            await _db.GetPrestigeAsync(c.CharUid, Num(1)),
                            t[2], Num(3))) return false;
                    break;
                default:
                    EdenLog.Write($"[Mission] cond dialogue non " +
                        $"évaluée (pass) : {raw}");
                    break;
            }
        }
        return true;
    }
    /// <summary>Exécute les actions d'une option de dialogue NPC —
    /// traduction fidèle des TC_* natifs (mission/decompiled.c) en
    /// commandes zone TransitionCommands : ReviveArea→setra
    /// (TC_ReviveArea @0x80f05f0), TPA→tpa (@0x80f0910), InventoryItem
    /// ±n→clone_reward_item/drop_item (@0x80f0a10), Gold±n→gain_gold
    /// (@0x80f0e10), GiveBuff→effect_life dur×10 (@0x80ef520),
    /// ForeverGold→eden-forever-gold (@0x80f0c30), Service→service
    /// (TC_Service @0x80ef980), Reputation→GainPrestigeExp interne
    /// (@0x80ec940), Event_TriggerEvent→eden-scene-event. Les verbes
    /// sans système (TerritoryAward, Event_MarryOperate,
    /// Event_SetAchievementNPC) sont journalisés, jamais bloquants.</summary>
    private async Task RunDialogActionsAsync(MissionSession s, CharContext c,
        int nodeId, int charUid, int npcUid, IReadOnlyList<string> actions)
    {
        foreach (var raw in actions)
        {
            var t = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            int Num(int i) => i < t.Length &&
                int.TryParse(t[i], out var v) ? v : 0;
            string Zone(string cmd) => cmd;
            switch (t[0])
            {
                case "ReviveArea": // → setra <node> <areaId>
                    await SendTransitionAsync(s, nodeId, charUid,
                        Zone($"setra {Num(1)} {Num(2)}"));
                    break;
                case "TPA": // → tpa <posId> <areaId> (point de sauvegarde)
                    await SendTransitionAsync(s, nodeId, charUid,
                        Zone($"tpa {Num(1)} {Num(2)}"));
                    break;
                case "InventoryItem":
                    // `InventoryItem <id> + <n>` donne / `- <n>` retire.
                    if (t.Length >= 4)
                        await SendTransitionAsync(s, nodeId, charUid,
                            Zone(t[2] == "-"
                                ? $"eden-take-item {Num(1)} {Num(3)}"
                                : $"eden-give-item {Num(1)} {Num(3)}"));
                    break;
                case "Gold": // → gain_gold ±n
                    if (t.Length >= 2)
                    {
                        var n = Num(1);
                        if (t.Length >= 3 && t[1] is "+" or "-")
                            n = (t[1] == "-" ? -1 : 1) * Math.Abs(Num(2));
                        await SendTransitionAsync(s, nodeId, charUid,
                            Zone($"gain_gold {n}"));
                    }
                    break;
                case "ForeverGold":
                    if (t.Length >= 2)
                        await SendTransitionAsync(s, nodeId, charUid,
                            Zone($"eden-forever-gold {Num(1)}"));
                    break;
                case "GiveBuff": // → enchant joueur (dur×10 côté natif)
                    if (t.Length >= 3)
                        await SendTransitionAsync(s, nodeId, charUid,
                            Zone($"eden-buff {Num(1)} {Num(2)}"));
                    break;
                case "DeleteBuff":
                    if (t.Length >= 2)
                        await SendTransitionAsync(s, nodeId, charUid,
                            Zone($"eden-debuff {Num(1)}"));
                    break;
                case "Service": // Service <dlg> <type> <ref> [extra]
                    await SendTransitionAsync(s, nodeId, charUid,
                        Zone($"service {Num(2)} {Num(3)} {Num(4)}"));
                    break;
                case "Reputation": // → GainPrestigeExp interne
                    if (t.Length >= 3)
                    {
                        var n = Num(2);
                        if (t.Length >= 4 && t[2] is "+" or "-")
                            n = (t[2] == "-" ? -1 : 1) * Math.Abs(Num(3));
                        await _db.AddPrestigeExpAsync(charUid, Num(1), n);
                    }
                    break;
                case "Event_TriggerEvent": // → événement de scène du node
                    await SendTransitionAsync(s, nodeId, charUid,
                        Zone($"eden-scene-event {Num(1)}"));
                    break;
                // Event_SetAchievement(NPC) id point flag — natif
                // TC_Event_SetAchievement @0x080ee2b0 : émet la
                // transition zone `set_achievement id point (flag!=0xd)`
                // (flag 13 = solo). Le verbe NPC partage le même corps.
                case "Event_SetAchievement":
                case "Event_SetAchievementNPC":
                    await SendTransitionAsync(s, nodeId, charUid,
                        Zone($"set_achievement {Num(1)} {Num(2)} " +
                            $"{(Num(3) != 0xd ? 1 : 0)}"));
                    break;
                case "Talk":
                    // Chaîne de dialogues interne (`Talk L B` en action) —
                    // répond directement avec le corps B.
                    if (t.Length >= 3)
                        await SendReply57Async(s, charUid, nodeId, npcUid,
                            Num(2));
                    break;
                default:
                    // TerritoryAward, Event_MarryOperate,
                    // Event_SetAchievementNPC, MC …
                    EdenLog.Write($"[Mission] action dialogue non " +
                        $"gérée : {raw}");
                    break;
            }
        }
    }
    /// <summary>Crédite les objectifs de `m` qui ciblent `npcUid` au
    /// moment où la réponse quête est choisie (op 128) — natif :
    /// l'action compilée s'exécute dans le dialogue, pas au contact.
    /// NPCTalk→PMS 50000+off, NPCItem→item donné + PMS 60000+off,
    /// ItemGiven→item requis consommé + PMS 40000+off.</summary>
    private async Task CreditNpcObjectivesAsync(MissionSession s,
        CharContext c, ActiveQuest q, MissionParser.MissionDef m,
        int charUid, int nodeId, int npcUid)
    {
        var mid = (uint)m.Id * 1_000_000u;
        foreach (var obj in m.Actions)
        {
            if (obj.Name == "NPCTalk")
            {
                // 0 arg → NPC de rendu (col17) ; sinon arg0=npcUid.
                var target = obj.Args.Count >= 1 &&
                    int.TryParse(obj.Args[0], out var n) ? n
                    : Col(m, 17);
                if (target != npcUid) continue;
                var key = mid + 50000u + (uint)(npcUid - 50000);
                if (q.Pms.GetValueOrDefault(key) < 1)
                    await CreditPmsAsync(s, c, q, m, key, 1);
            }
            else if (obj.Name == "NPCItem" && obj.Args.Count >= 1)
            {
                // NPCItem <itemId> : le NPC de rendu remet l'item au
                // joueur (natif MC_FinishNPCItem → « InventoryItem id
                // + n » + « PMS key + 1 », key = mid×1e6+60000+off).
                if (Col(m, 17) != npcUid) continue;
                int.TryParse(obj.Args[0], out var nitemId);
                var key = mid + 60000u + (uint)(npcUid - 50000);
                if (nitemId > 0 && q.Pms.GetValueOrDefault(key) < 1)
                {
                    await SendTransitionAsync(s, nodeId, charUid,
                        $"eden-give-item {nitemId} 1");
                    await CreditPmsAsync(s, c, q, m, key, 1);
                }
            }
            else if (obj.Name == "ItemGot" && obj.Args.Count >= 4 &&
                int.TryParse(obj.Args[0], out var giver) &&
                giver == npcUid)
            {
                // ItemGot <npc> <texte> <item> <n> : le NPC arg0 remet
                // l'item arg2 × arg3 à la sélection de l'entrée quête
                // (natif MC_FinishItemGot → « InventoryItem id + n » +
                // « PMS key + 1 », key = mid×1e6+30000+(npc−50000)).
                int.TryParse(obj.Args[2], out var gitem);
                int.TryParse(obj.Args[3], out var gnum);
                var key = mid + 30000u + (uint)(npcUid - 50000);
                if (gitem > 0 && q.Pms.GetValueOrDefault(key) < 1)
                {
                    await SendTransitionAsync(s, nodeId, charUid,
                        $"eden-give-item {gitem} {Math.Max(1, gnum)}");
                    await CreditPmsAsync(s, c, q, m, key, 1);
                }
            }
            else if (obj.Name == "ItemGiven" && obj.Args.Count >= 4 &&
                int.TryParse(obj.Args[0], out var tgt) &&
                tgt == npcUid)
            {
                // ItemGiven <npc> <dlg> <item> <n> — MC_FinishItemGiven
                // natif @0x080e3ce0 : compile « InventoryItem id + n »,
                // c.-à-d. que le NPC arg0 REMET l'item arg2 × arg3 au
                // joueur (lettre de réponse, colis…) puis PMS
                // mid×1e6+40000+(npc−50000). Livré à l'envers avant
                // (exigeait l'item en inventaire puis le consommait).
                int.TryParse(obj.Args[2], out var gitem);
                int.TryParse(obj.Args[3], out var gnum);
                var key = mid + 40000u + (uint)(npcUid - 50000);
                if (gitem > 0 && q.Pms.GetValueOrDefault(key) < 1)
                {
                    await SendTransitionAsync(s, nodeId, charUid,
                        $"eden-give-item {gitem} {Math.Max(1, gnum)}");
                    await CreditPmsAsync(s, c, q, m, key, 1);
                }
            }
        }
    }
    /// <summary>57 ReplyDialogue : texte parlé, 0 option.
    /// Wire : [i32 charUid][s16 nodeId][i32 npcUid]
    /// vec&lt;s16&gt;|vec&lt;i32&gt;|vec&lt;i32&gt;|vec&lt;Ss&gt;|vec&lt;s16&gt;|
    /// vec&lt;s16&gt;|[i32 ctx].</summary>
    private static async Task SendReply57Async(MissionSession s, int charUid,
        int nodeId, int npcUid, int ctx)
    {
        var outp = new List<byte>();
        outp.AddRange(BitConverter.GetBytes(charUid));   // i32
        outp.AddRange(BitConverter.GetBytes((short)nodeId)); // s16
        outp.AddRange(BitConverter.GetBytes(npcUid));    // i32
        for (var i = 0; i < 6; i++)                      // 6 vecteurs vides
            outp.AddRange(BitConverter.GetBytes((ushort)0));
        outp.AddRange(BitConverter.GetBytes(ctx));       // i32 : texte
        await s.SendAsync(57, outp.ToArray());
    }
    /// <summary>58 TransitionCommands : [s16 nodeId][i32 charUid]
    /// [u16 n][n×(u16 len, cmd)] — la zone exécute chaque commande
    /// texte (Execute @0x833c600).</summary>
    private static async Task SendTransitionAsync(MissionSession s,
        int nodeId, int charUid, params string[] cmds)
    {
        var outp = new List<byte>();
        outp.AddRange(BitConverter.GetBytes((short)nodeId));
        outp.AddRange(BitConverter.GetBytes(charUid));
        outp.AddRange(BitConverter.GetBytes((ushort)cmds.Length));
        foreach (var cmd in cmds)
        {
            var bytes = Encoding.ASCII.GetBytes(cmd);
            outp.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
            outp.AddRange(bytes);
        }
        await s.SendAsync(58, outp.ToArray());
    }
}
