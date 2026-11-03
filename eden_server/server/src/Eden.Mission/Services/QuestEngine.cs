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
public sealed partial class QuestEngine
{
    private readonly GameDataService _data;
    private readonly MissionDb _db;
    private readonly Dictionary<int, string> _missionTitles = new();

    /// <summary>État de quête actif d'un perso.
    /// Status : 1 = en cours, 2 = objectifs remplis, 0xFE = abandonnée
    /// (marqueur interne : empêche le re-grant automatique des quêtes
    /// tutoriel sans NPC donneur après un GiveUp).
    /// `Pms` = compteurs par objectif, indexés par la clé PMS native
    /// (désasm CMissionList::GetXxxPMS : mid×1e6 + type×1e4 + objet) —
    /// le client affiche « n/requis » du HUD sur ces clés.</summary>
    public sealed class ActiveQuest
    {
        public int Status = 1;
        public int Progress;            // compteur agrégé (persisté)
        public int Required;            // total requis (legacy)
        /// <summary>clé PMS → compteur courant.</summary>
        public readonly Dictionary<uint, int> Pms = new();
        public int Packed => Status | (Progress << 8);
    }

    /// <summary>Contexte runtime d'un perso connu de la mission.</summary>
    public sealed class CharContext
    {
        public int CharUid;
        public int NodeId;
        public int Level;
        public int Gender;
        public int ClassId;
        public MissionSession? Session;
        /// <summary>Miroir des compteurs d'enchants suivis par les quêtes
        /// (id → compteur, 0 = absent) alimenté par la commande DBA
        /// <c>CheckMissionEnchant</c> de la zone — équivalent de la map
        /// d'enchants que le natif tient dans l'exécuteur
        /// (UpdateQuestCheckBuffStatus @0x08094ef0).</summary>
        public readonly Dictionary<int, int> Buffs = new();
        public readonly Dictionary<int, ActiveQuest> Active = new();
        public HashSet<int> Completed = new();
        /// <summary>FMS natifs (mid → 0..7) — final_mission_state.
        /// 3/4 = en cours, 1 = terminée, 5 = checked, 6 = reprenable.</summary>
        public Dictionary<int, int> FMS = new();
        /// <summary>Compteurs de complétions session (mid → n) — map
        /// native exec+0x45c, remplie depuis player_mission_complete_num
        /// au login. Gate d'accept des quêtes limitées (col4, +0x224) :
        /// la row DB est supprimée à chaque complétion pour bit2-clair,
        /// donc ce compteur est de fait « par session de login ».</summary>
        public Dictionary<int, int> SessionCompletions = new();
        /// <summary>Compteurs journaliers (mid → n) — map native
        /// exec+0x474, vidée au changement de jour (ResetDailyMission
        /// @0x080752f0 compare exec+0x4dc à localtime()-&gt;tm_mday).
        /// Mémoire seule : le natif ne la persiste pas non plus.</summary>
        public readonly Dictionary<int, int> DailyCompletions = new();
        /// <summary>Jour du dernier reset — exec+0x4dc natif.</summary>
        public int DailyResetDay;
        /// <summary>selId → action, dernier 56 émis (le client écho selId).</summary>
        public readonly Dictionary<int, DialogAction> DialogMap = new();
        /// <summary>Talks/services du dernier 56 — indexés par
        /// DialogAction.Arg1 pour retrouver conditions/actions/failBody
        /// à la sélection (128).</summary>
        public List<TalkEntry> LastTalks = new();
        public List<Eden.Core.Data.Parsers.WorldParsers.NpcDef.NpcService>
            LastServices = new();
    }


    /// <summary>Catalogues gamedata (textes S/T_TextIndex inclus) —
    /// exposé pour la résolution de noms localisés (monnaies
    /// spéciales, chaînes DBA).</summary>
    public GameDataService Data => _data;

    /// <summary>T_Mission.ini : `id|titre|map|desc|` — titre affiché
    /// comme nom d'entrée de quête dans le 285 (champ E du 56).</summary>
    private void LoadMissionTitles(string dataDir)
    {
        foreach (var name in new[] { "T_Mission.ini", "S_Mission.ini" })
        {
            var path = Path.Combine(dataDir, name);
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path, EdenEncoding.Big5))
            {
                var cols = line.Split('|');
                if (cols.Length >= 3 && int.TryParse(cols[0], out var id)
                    && cols[1].Length > 0 && !_missionTitles.ContainsKey(id))
                    _missionTitles[id] = cols[1];
            }
        }
    }

    /// <summary>Enregistre un perso (142 MigrationDone) et charge son
    /// état de quêtes depuis la DB.</summary>
    public async Task OnMigrationDoneAsync(MissionSession s, int charUid,
        int nodeId)
    {
        var ctx = new CharContext { CharUid = charUid, NodeId = nodeId, Session = s };
        try
        {
            // Le modèle natif : final_mission_state (FMS packés 3 bits)
            // est la source d'état — personal_mission_states ne porte
            // que la progression (packed status|progress<<8).
            ctx.FMS = await _db.LoadFMSAsync(charUid);
            var active = await _db.LoadActiveAsync(charUid);
            foreach (var (mid, st) in active)
            {
                var q = new ActiveQuest
                { Status = st & 0xff, Progress = st >> 8 };
                // Required n'est PAS persisté — il se déduit du script
                // de mission (KillRequired). Sans cette restauration,
                // Required restait 0 après une relog et la condition
                // `q.Required > 0` de OnMobDie empêchait toute
                // complétion d'objectif Defeat/Collect.
                var def = _data.Missions.FirstOrDefault(m => m.Id == mid);
                if (def != null) q.Required = KillRequired(def);
                ctx.Active[mid] = q;
            }
            // Quêtes en cours d'après le FMS mais sans ligne PMS
            // (état natif pur) — reconnues comme actives.
            foreach (var (mid, f) in ctx.FMS)
                if (f is 3 or 4 && !ctx.Active.ContainsKey(mid))
                {
                    var q = new ActiveQuest { Status = f == 4 ? 2 : 1 };
                    var def = _data.Missions.FirstOrDefault(m => m.Id == mid);
                    if (def != null) q.Required = KillRequired(def);
                    ctx.Active[mid] = q;
                }
            // Compteurs PMS persistés (index = clé mid×1e6+type×1e4+obj)
            // — répartis dans la quête active correspondante pour que
            // le tracker affiche la vraie progression après relog.
            var pms = await _db.LoadPmsAsync(charUid);
            foreach (var (key, count) in pms)
            {
                var mid = key / 1000000;
                if (ctx.Active.TryGetValue(mid, out var pq))
                    pq.Pms[(uint)key] = count;
            }
            // complete_num alimente les compteurs de complétions des
            // quêtes LIMITÉES (map native +0x45c), pas l'état « finie »
            // — le natif n'écrit cette table que pour les quêtes
            // col4>0 (AddMissionCompleteNum @0x08190ef0).
            ctx.SessionCompletions =
                await _db.LoadCompletionCountsAsync(charUid);
            // FMS==1 = terminée (RemoveFinishItem+CompleteNum natif) ;
            // FMS==5 = "checked" — l'UI la grise, jamais ré-offerte.
            foreach (var (mid, f) in ctx.FMS)
                if (f == 1 || f == 5) ctx.Completed.Add(mid);
            var (level, gender) = await _db.GetLevelGenderAsync(charUid);
            ctx.Level = level; ctx.Gender = gender;
            ctx.ClassId = await _db.GetClassIdAsync(charUid);
        }
        catch (Exception ex)
        { EdenLog.Write($"[Mission] load quests char={charUid} : {ex.Message}"); }
        _chars[charUid] = ctx;
        // Purge des auto-quêtes accordées AVANT le filtre niveau :
        // type auto (col6==10), aucun donneur (col11==0) et niveau
        // requis (col8) non atteint → retirées de l'état, de la DB et
        // du FMS (elles seront re-accordées quand le niveau sera là).
        foreach (var (mid, q) in ctx.Active.ToList())
        {
            var def = _data.Missions.FirstOrDefault(m => m.Id == mid);
            if (def == null || Col(def, 6) != 10 || Col(def, 11) != 0)
                continue;
            var req = Col(def, 8);
            if (req <= 0 || ctx.Level >= req) continue;
            ctx.Active.Remove(mid);
            if (ctx.FMS.GetValueOrDefault(mid) is 3 or 4)
                ctx.FMS.Remove(mid);
            try
            {
                await _db.DeleteActiveAsync(charUid, mid);
                await _db.DeletePmsAsync(charUid, mid);
                await _db.SetFMSAsync(charUid, mid, 0);
            }
            catch (Exception ex)
            { EdenLog.Write($"[Mission] purge auto-quest {mid} : {ex.Message}"); }
            EdenLog.Write($"[Mission] purge auto-quest {mid} " +
                $"char={charUid} (req lv{req} > lv{ctx.Level})");
        }
        // Ré-enregistre les clés PMS déclarées de chaque quête active
        // (391 kind=1, compteur courant) — le client n'applique une
        // update kind=1 que pour une clé déjà connue de sa mission_list
        // (IncPMS natif rejette "pms_id does not exist"). Les quêtes
        // actives dont aucun objectif n'a encore été crédité n'ont
        // aucune ligne personal_mission_states → sans ce seed le
        // tracker reste à 0/x jusqu'au prochain login.
        foreach (var (mid, q) in ctx.Active)
        {
            if (q.Status is 2 or 0xFE) continue;
            var def = _data.Missions.FirstOrDefault(m => m.Id == mid);
            if (def == null) continue;
            foreach (var (pk, _) in PmsObjectives(def))
                await SendPmsAsync(s, charUid, pk,
                    q.Pms.GetValueOrDefault(pk));
        }
        // PAS d'auto-acceptation à l'entrée : le joueur accepte les
        // quêtes explicitement (dialogue NPC → MissionListOp op=1).
        // La boucle native col6==10 (auto-quêtes tutorielles) est
        // retirée à la demande du live — « le jeu prend des quêtes
        // sans que je le veuille » : plus aucune quête n'est injectée
        // dans Active/FMS au login.
        // Symboles « ! »/« ? » initiaux des PNJ (ZMis 65 → 345).
        await PushNpcSymbolsAsync(s, ctx);
        EdenLog.Write($"[Mission] char={charUid} migré node={nodeId} " +
            $"({ctx.Active.Count} actives, {ctx.Completed.Count} faites)");
    }

    /// <summary>Contexte d'un perso (créé vide s'il n'existe pas) —
    /// point d'entrée des tests unitaires du moteur, sans DB.</summary>
    internal CharContext ContextFor(int charUid, int level = 1,
        int nodeId = 1)
    {
        if (!_chars.TryGetValue(charUid, out var c))
            _chars[charUid] = c = new CharContext
            { CharUid = charUid, Level = level, NodeId = nodeId };
        return c;
    }

    /// <summary>Décharge le contexte à la déconnexion de la zone.</summary>
    public void RemoveBySession(MissionSession s)
    {
        foreach (var kv in _chars.Where(kv => kv.Value.Session == s).ToList())
            _chars.Remove(kv.Key);
    }

    // --------------------------------------------------- dialogues --


    /// <summary>Colonnes S_Mission.ini utilisées : [11] NPC donneur,
    /// [16] objectifs, [17] NPC de rendu, [13] conditions d'acceptation,
    /// [20] récompenses (Raw conservé par MissionParser).</summary>
    private static int Col(MissionParser.MissionDef m, int i) =>
        i < m.Raw.Length && int.TryParse(m.Raw[i], out var v) ? v : 0;


    /// <summary>Construit les entrées de quête du NPC pour ce perso :
    /// quêtes acceptables (giver col11 == npc, conditions, non
    /// prise/faite) et rendables (submit col17 == npc, active).</summary>
    private List<(int SelId, string Title, int Verb)>
        QuestEntries(CharContext c, int npcUid)
    {
        var list = new List<(int, string, int)>();
        foreach (var m in _data.Missions)
        {
            var giver = Col(m, 11);
            var submit = Col(m, 17);
            // Niveau requis : col8 (titres "L08"→8, "L54"→54) — col6
            // est le TYPE de quête, pas un niveau (le comparer au
            // level masquait/offrait les mauvaises quêtes).
            var reqLv = Col(m, 8);
            // Quête à limite (col4>0) : le gate est le compteur seul —
            // Completed/FMS ne s'appliquent pas (FMSClear natif à la
            // complétion ; guérit aussi les vieux FMS=1 persistés).
            var offerable = m.CompleteLimit > 0 && IsRepeatable(m)
                ? !DoneBlocked(c, m)
                : !DoneBlocked(c, m) &&
                  // Offerte seulement si FMS vierge ou reprenable —
                  // un FMS≠0/6 (terminée, checked, suspendue) la masque.
                  c.FMS.GetValueOrDefault(m.Id) is 0 or 6;
            if (giver == npcUid &&
                !c.Active.ContainsKey(m.Id) &&
                offerable &&
                (reqLv == 0 || reqLv <= c.Level) &&
                ConditionsPass(c, m))
                list.Add((SelOf(m.Id, 0),
                    _missionTitles.GetValueOrDefault(m.Id, m.Name), 0));
            else if (submit == npcUid &&
                     c.Active.TryGetValue(m.Id, out var q) &&
                     q.Status != 0xFE)
                list.Add((SelOf(m.Id, q.Status == 2 ? 1 : 2),
                    _missionTitles.GetValueOrDefault(m.Id, m.Name),
                    q.Status == 2 ? 1 : 2));
            else if (giver == npcUid &&
                     c.Active.TryGetValue(m.Id, out var g2) &&
                     g2.Status != 0xFE)
                list.Add((SelOf(m.Id, 2),
                    _missionTitles.GetValueOrDefault(m.Id, m.Name), 2));
            else if (c.Active.TryGetValue(m.Id, out var q3) &&
                     q3.Status == 1 && QuestTargetsNpc(m, npcUid))
                // NPC visé par une étape d'une quête EN COURS (NPCTalk
                // intermédiaire, livraison ItemGiven…) : la réponse
                // dynamique n'apparaît que si la quête est active.
                list.Add((SelOf(m.Id, 2),
                    _missionTitles.GetValueOrDefault(m.Id, m.Name), 2));
        }
        return list;
    }














    // ---------------------------------------------------- opérations --




    // --------------------------------------------------- objectifs PMS --
















    /// <summary>CheckTrigger (commande DBA zone→mission) — équivalent
    /// du <c>TC_CheckTrigger</c> natif @0x8203770 : la zone a validé le
    /// franchissement d'une aire déclencheur et transmet
    /// <c>NodeEventID</c> (composite node&lt;&lt;16|key) + SelectID.
    /// La mission crédite les objectifs AreaTravel (clés 50-59) puis
    /// renvoie <c>ClearTriggerAreaID ch_id=</c> à la zone — le reset
    /// natif de char+0x7a4 (anti-retrigger) qui autorise le joueur à
    /// réutiliser l'aire.</summary>
    public async Task OnCheckTriggerAsync(MissionSession s, int charUid,
        int nodeEventId, int selectId)
    {
        var node = (short)(nodeEventId >> 16);
        var key = (short)(nodeEventId & 0xffff);
        await CreditAreaTravelAsync(s, charUid, node, key);
        // ClearTriggerAreaID → CNC_MW_WorldTextCommand natif, relayé
        // chez nous en 58 TransitionCommands vers la zone du joueur.
        await SendTransitionAsync(s, node, charUid,
            $"ClearTriggerAreaID ch_id={charUid}");
        EdenLog.Write($"[Mission] char={charUid} CheckTrigger " +
            $"node={node} key={key} sel={selectId}");
    }

    /// <summary>Crédit des objectifs <c>AreaTravel</c> (code aire
    /// node×100+keyId) — la plage native des clés déclencheurs est
    /// 0x32..0x3B (Ins+0x5c4 indexée node×10 + (key−0x32) ; natif
    /// GetAreaTravelPMS : clé = mid×1e6+70000+((key−0x32)+node×10)).</summary>
    private async Task CreditAreaTravelAsync(MissionSession s, int charUid,
        int node, int key)
    {
        if (key < 0x32 || key > 0x3B ||
            !_chars.TryGetValue(charUid, out var c)) return;
        var areaCode = node * 100 + key;
        foreach (var m in _data.Missions)
        {
            if (!c.Active.TryGetValue(m.Id, out var q) ||
                q.Status != 1) continue;
            var mid = (uint)m.Id * 1_000_000u;
            foreach (var o in m.Actions)
            {
                if (o.Name != "AreaTravel" || o.Args.Count < 1 ||
                    !int.TryParse(o.Args[0], out var area) ||
                    area != areaCode) continue;
                var pmsKey = mid + 70000u + (uint)(
                    (key - 0x32) + node * 10);
                if (q.Pms.GetValueOrDefault(pmsKey) < 1)
                {
                    await CreditPmsAsync(s, c, q, m, pmsKey, 1);
                    EdenLog.Write($"[Mission] char={charUid} " +
                        $"{m.Id} travel {areaCode} : pms={pmsKey}");
                }
            }
        }
    }

    /// <summary>Tous les objectifs PMS déclarés sont remplis — vrai si
    /// la mission n'en déclare aucun (quêtes purement collecte : la
    /// complétion repose alors uniquement sur AllItemsDoneAsync).</summary>
    private static bool AllPmsDoneOrVacuous(ActiveQuest q,
        MissionParser.MissionDef m)
    {
        foreach (var (key, req) in PmsObjectives(m))
            if (q.Pms.GetValueOrDefault(key) < req) return false;
        return true;
    }
}
