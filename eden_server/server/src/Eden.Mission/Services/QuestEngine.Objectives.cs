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
/// <summary>Partiel objectifs — conditions d'acceptation,
/// objectifs PMS/items, progression (CreditPms), acceptation,
/// récompenses et déclencheurs de groupes.</summary>
public sealed partial class QuestEngine
{
    /// <summary>Évalue les conditions d'acceptation col13 :
    /// If_FMS mid done, If_CharLevel &gt;= n, If_Gender … — les
    /// conditions inconnues passent (log) pour ne pas masquer de quêtes.</summary>
    private bool ConditionsPass(CharContext c, MissionParser.MissionDef m)
    {
        foreach (var cond in m.Conditions)
        {
            var a = cond.Args;
            switch (cond.Name)
            {
                case "If_FMS":
                    if (a.Count >= 2 && int.TryParse(a[0], out var fms))
                    {
                        var wantDone = a[1] == "done";
                        if (c.Completed.Contains(fms) != wantDone)
                            return false;
                    }
                    break;
                case "If_CharLevel":
                    if (a.Count >= 2 && a[0] == ">=" &&
                        int.TryParse(a[1], out var min) && c.Level < min)
                        return false;
                    break;
                case "PlayerLevel":
                    if (a.Count >= 2 && a[0] == ">=" &&
                        int.TryParse(a[1], out var pl) && c.Level < pl)
                        return false;
                    break;
                case "If_Gender":
                    // Genre requis : If_Gender n — le gender natif du
                    // perso (player_characters.gender) doit matcher.
                    if (a.Count >= 1 && int.TryParse(a[0], out var g) &&
                        c.Gender != g)
                        return false;
                    break;
                default:
                    // If_Gender / If_CheckBuff / If_InventorySlot /
                    // If_FamilyContributionPoint / If_MidNightLimit…
                    // HYPOTHESIS: non évaluées — assumées vraies pour
                    // ne pas masquer les quêtes accessibles.
                    break;
            }
        }
        return true;
    }
    /// <summary>Tâche centralisée de « objectif atteint » : marque le
    /// compteur PMS, le persiste (SavePMS natif), émet le 391 kind=1
    /// qui fait défiler le compteur du HUD client, puis recalcule le
    /// statut de quête (2 = tout rempli → FMS 4 + 391 kind=0).</summary>
    private async Task CreditPmsAsync(MissionSession s,
        CharContext c, ActiveQuest q, MissionParser.MissionDef m,
        uint pmsKey, int count)
    {
        q.Pms[pmsKey] = count;
        q.Progress = q.Pms.Values.Sum();
        await _db.SavePmsAsync(c.CharUid, (int)pmsKey, count);
        // 391 kind=1 : [u8 1][u32 pmsKey][u16 count][s16 0] — le HUD
        // « Defeat mob n/3 » se met à jour sur CE paquet (natif :
        // NotifyClientMissionState kind=1, key=PMS, u16=count).
        await SendPmsAsync(s, c.CharUid, pmsKey, count);
        if (q.Status != 1) return;
        if (AllPmsDone(q, m) && await AllItemsDoneAsync(c.CharUid, m))
        {
            q.Status = 2;
            c.FMS[m.Id] = 4;
            await _db.SetFMSAsync(c.CharUid, m.Id, 4);
            await _db.SaveActiveAsync(c.CharUid, m.Id, q.Packed);
            await SendStateAsync(s, c.CharUid, m.Id, ClientState(2),
                q.Progress);
            await PushNpcSymbolsAsync(s, c);
            EdenLog.Write($"[Mission] char={c.CharUid} {m.Id} " +
                "objectifs remplis");
        }
        else
        {
            await _db.SaveActiveAsync(c.CharUid, m.Id, q.Packed);
            await SendStateAsync(s, c.CharUid, m.Id, ClientState(q.Status),
                q.Progress);
        }
    }
    /// <summary>Total de kills requis par les objectifs MonsterKilled /
    /// MonsterTreasureGot du script col16.</summary>
    private static int KillRequired(MissionParser.MissionDef m)
    {
        var total = 0;
        foreach (var o in m.Actions)
            if ((o.Name is "MonsterKilled" or "MonsterTreasureGot") &&
                o.Args.Count >= 2 &&
                int.TryParse(o.Args[^1], out var n))
                total += n;
        return total;
    }
    /// <summary>Objectif non trivial encore à faire : kill/item/travel,
    /// ou un NPCTalk qui cible explicitement un autre NPC (ex. 1010
    /// "NPCTalk 50002" — il faut parler à Andrew avant de rendre).
    /// Un NPCTalk sans argument vise le NPC de rendu lui-même : il se
    /// résout à la simple ouverture du dialogue — non bloquant.</summary>
    private static bool HasBlockingObjective(MissionParser.MissionDef m) =>
        m.Actions.Any(o =>
            (o.Name is "MonsterKilled" or "MonsterTreasureGot"
                or "NPCItem" or "ItemCollect" or "ItemGot" or "ItemGiven"
                or "AcceptInventoryItem" or "AreaTravel" or "CheckBuff") ||
            (o.Name == "NPCTalk" && o.Args.Count >= 1));
    /// <summary>Clé PMS native d'un objectif — désasm
    /// `CMissionList::GetMonsterKilledPMS/GetMonsterTreasureGotPMS/
    /// GetItemGotPMS/GetItemGivePMS/GetNPCTalkPMS/GetNPCMultiTalkPMS`
    /// (mission/decompiled.c @0x80e1b30-0x80e2310) :
    ///   mid×1 000 000 + type×10 000 + objet, avec objet = id−base.
    /// Le client affiche « n/requis » du HUD sur ces clés via le paquet
    /// 391 kind=1 (IncPMS → NotifyClientMissionState).</summary>
    private static List<(uint Key, int Req)> PmsObjectives(
        MissionParser.MissionDef m)
    {
        var list = new List<(uint, int)>();
        var mid = (uint)m.Id * 1_000_000u;
        foreach (var o in m.Actions)
        {
            int A(int i) => i < o.Args.Count &&
                int.TryParse(o.Args[i], out var v) ? v : 0;
            switch (o.Name)
            {
                case "MonsterKilled":           // mob n
                    if (o.Args.Count >= 2)
                        list.Add((mid + 10000u + (uint)(A(0) - 55000),
                            Math.Max(1, A(1))));
                    break;
                case "MonsterTreasureGot":      // mob rate% item n
                    if (o.Args.Count >= 4)
                        list.Add((mid + 20000u + (uint)(A(0) - 55000),
                            Math.Max(1, A(3))));
                    break;
                case "ItemGot":                 // npc dlg item n
                    if (o.Args.Count >= 4)
                        list.Add((mid + 30000u + (uint)(A(0) - 50000),
                            Math.Max(1, A(3))));
                    break;
                case "ItemGiven":               // livraison npc dlg item n
                    if (o.Args.Count >= 4)
                        list.Add((mid + 40000u + (uint)(A(0) - 50000),
                            Math.Max(1, A(3))));
                    break;
                case "NPCTalk":                 // npc [dlg] — 0 arg = NPC rendu
                    if (o.Args.Count >= 1 && A(0) != 0)
                        list.Add((mid + 50000u + (uint)(A(0) - 50000), 1));
                    else if (Col(m, 17) != 0)
                        list.Add((mid + 50000u + (uint)(Col(m, 17) - 50000), 1));
                    break;
                case "NPCItem":                 // item remis PAR le NPC au
                    // dialogue (natif GetNPCItemPMS : mid×1e6 + 60000 +
                    // (submitNpc-50000) — MC_FinishNPCItem compile
                    // "InventoryItem item + n" + "PMS key + 1").
                    if (Col(m, 17) != 0)
                        list.Add((mid + 60000u + (uint)(Col(m, 17) - 50000), 1));
                    break;
                case "NPCMultiTalk":            // npc
                    if (o.Args.Count >= 1)
                        list.Add((mid + 90000u + (uint)(A(0) - 50000), 1));
                    break;
                case "AreaTravel":              // aire node*100+keyId
                    // Natif GetAreaTravelPMS @0x80e1e80 : clé =
                    // mid×1e6 + 70000 + ((area%100)−0x32) + (area/100)×10
                    // — les sous-aires valides sont 0x33..0x3A (51..58).
                    if (o.Args.Count >= 1 && A(0) > 0)
                        list.Add((mid + 70000u + (uint)(
                            (A(0) % 100 - 0x32) + (A(0) / 100) * 10), 1));
                    break;
                // ItemCollect n'est volontairement PAS ici : sa clé
                // mid×1e6+100000 est partagée par tous les items d'une
                // même mission (MC_FinishItemCollect client) — la
                // complétion s'évalue via ItemObjectives (comptage
                // inventaire) ; la clé n'est émise que pour rafraîchir
                // le tracker (OnQuestItemChangedAsync).
            }
        }
        return list;
    }
    /// <summary>Objectifs évalués sur le contenu de l'inventaire au
    /// moment du rendu (pas de compteur PMS natif — CheckItemInInventory)
    /// : ItemCollect itemId n, NPCItem itemId.</summary>
    private static List<(int ItemId, int Req)> ItemObjectives(
        MissionParser.MissionDef m)
    {
        var list = new List<(int, int)>();
        foreach (var o in m.Actions)
        {
            int A(int i) => i < o.Args.Count &&
                int.TryParse(o.Args[i], out var v) ? v : 0;
            if (o.Name is "ItemCollect" or "AcceptInventoryItem" &&
                o.Args.Count >= 2)
                list.Add((A(0), Math.Max(1, A(1))));
            // NPCItem n'est PAS un check d'inventaire : le natif donne
            // l'item au dialogue (InventoryItem + n) — voir la branche
            // dédiée dans OnNpcTalkAsync.
        }
        return list;
    }
    /// <summary>Items retirés du sac à la remise (RemoveFinishItem
    /// natif) : objectifs d'inventaire + items de quête droppés par
    /// MonsterTreasureGot (« mob rate% item n ») — ces derniers
    /// restaient dans le sac après la remise.</summary>
    private static List<(int ItemId, int Req)> FinishTakeItems(
        MissionParser.MissionDef m)
    {
        var list = ItemObjectives(m);
        foreach (var o in m.Actions)
            if (o.Name == "MonsterTreasureGot" && o.Args.Count >= 4 &&
                int.TryParse(o.Args[2], out var it) && it > 0 &&
                int.TryParse(o.Args[3], out var n) &&
                !list.Any(x => x.ItemId == it))
                list.Add((it, Math.Max(1, n)));
        return list;
    }
    /// <summary>Vrai si tous les objectifs PMS de la mission sont
    /// remplis. C'est la condition native de passage FMS 3→4
    /// (`FMSCheckReward` : chaque pms_id de la mission doit atteindre
    /// son requis) — le bug du donjon (rendu possible après le seul
    /// NPCTalk alors que les boss restaient) venait de l'absence de ce
    /// contrôle.</summary>
    private static bool AllPmsDone(ActiveQuest q,
        MissionParser.MissionDef m)
    {
        var objs = PmsObjectives(m);
        if (objs.Count == 0) return false;
        foreach (var (key, req) in objs)
            if (q.Pms.GetValueOrDefault(key) < req) return false;
        return true;
    }
    /// <summary>Vrai si tous les objectifs d'inventaire sont remplis
    /// (comptage live dans les sacs + équipement — le natif fait la
    /// même vérification au rendu via CheckItemInInventory).</summary>
    internal async Task<bool> AllItemsDoneAsync(int charUid,
        MissionParser.MissionDef m)
    {
        // CheckBuff : bit d'objectif natif (UpdateQuestCheckBuffStatus) —
        // évalué sur le miroir d'enchants du perso.
        if (_chars.TryGetValue(charUid, out var bc) &&
            !QuestBuffRules.AllSatisfied(m, bc.Buffs))
            return false;
        foreach (var (itemId, req) in ItemObjectives(m))
            if (await _db.CountItemAsync(charUid, itemId,
                    Stackable(itemId)) < req)
                return false;
        return true;
    }
    /// <summary>Vrai si l'item empile (S_Item MaxStack>1 ⇔ flag
    /// `CItemData+0xf0` bit0 natif) — `CheckItemNum` soustrait alors
    /// le count du stack, sinon 1 par slot.</summary>
    private bool Stackable(int itemId) =>
        _data.Items.FirstOrDefault(i => i.Id == itemId)
            ?.MaxStack > 1;
    /// <summary>Gate d'acceptation natif : quête à limite (col4&gt;0) →
    /// refus si le compteur atteint la limite — journalier exec+0x474
    /// pour les daily-type (flag col6 bit4, mission+0x226), session
    /// exec+0x45c sinon (mission+0x224 ; désasm case accept
    /// @0x0808e2xx + @0x08090xxx). Quête ordinaire (col4==0) →
    /// bloquée par Completed (FMS 1|5).</summary>
    private bool DoneBlocked(CharContext c, MissionParser.MissionDef m)
    {
        // Règle produit : seule une quête QUOTIDIENNE ou RÉPÉTABLE peut être reproposée ;
        // toute autre est une complétion unique, même avec une limite c_mission col4 > 0
        // (compteur de session natif, perdu à la reconnexion → quêtes faites reproposées).
        if (m.CompleteLimit <= 0 || !IsRepeatable(m))
            return c.Completed.Contains(m.Id) ||
                   c.FMS.GetValueOrDefault(m.Id) is 1 or 5 ||
                   c.SessionCompletions.GetValueOrDefault(m.Id) > 0;
        if (m.DailyType) return DailyCount(c, m.Id) >= m.CompleteLimit;
        return c.SessionCompletions.GetValueOrDefault(m.Id)
            >= m.CompleteLimit;
    }

    private static readonly System.Text.RegularExpressions.Regex RepeatTitle = new(
        @"r[ée]p[ée]t|quotid|daily|hebdo|weekly|repeat", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Quête reproposable : type journalier (col6 bit4) ou titre répétable/quotidien.</summary>
    private bool IsRepeatable(MissionParser.MissionDef m) =>
        m.DailyType ||
        RepeatTitle.IsMatch(_missionTitles.GetValueOrDefault(m.Id, m.Name) ?? "") ||
        RepeatTitle.IsMatch(m.Name ?? "");

    /// <summary>Compteur journalier : ResetDailyMission natif vide la
    /// map exec+0x474 quand localtime()-&gt;tm_mday != exec+0x4dc —
    /// équivalent paresseux à la lecture.</summary>
    private static int DailyCount(CharContext c, int mid)
    {
        var day = DateTime.Now.Day;
        if (c.DailyResetDay != day)
        {
            c.DailyCompletions.Clear();
            c.DailyResetDay = day;
        }
        return c.DailyCompletions.GetValueOrDefault(mid);
    }

    /// <summary>Complétion d'une quête à limite (col4&gt;0) — chemin
    /// natif SetFMS case4 (@0x080962xx) + queue (@0x080960xx) : FMS=1
    /// est posée pour TOUTES les complétions, puis SetFMS(0) est
    /// rappelée quand le flag runtime bit2 est set ET que la limite
    /// +0x224 n'est pas atteinte — la quête revient « reprenable ».
    /// Retourne true quand le FMS final repasse à 0 (reprenable).</summary>
    private async Task<bool> TrackLimitedCompletionAsync(
        CharContext c, MissionParser.MissionDef m, int mid)
    {
        var charUid = c.CharUid;
        c.FMS[mid] = 1;
        await _db.SetFMSAsync(charUid, mid, 1);
        if (m.DailyType)
        {
            // Daily-type : +0x224==0 → SetFMS(0) systématique ; seul
            // le compteur journalier map+0x474 bloque le re-accept
            // (jour courant). Aucune écriture complete_num.
            c.DailyCompletions[mid] = DailyCount(c, mid) + 1;
            c.FMS.Remove(mid);
            await _db.SetFMSAsync(charUid, mid, 0);
            return true;
        }
        var n = c.SessionCompletions.GetValueOrDefault(mid) + 1;
        c.SessionCompletions[mid] = n;
        if (m.KeepLimitRow && n < m.CompleteLimit)
        {
            // Bit2-set, limite non atteinte : row conservée (persiste
            // entre logins) + FMS remise à 0 → reprenable.
            await _db.SetCompleteNumAsync(charUid, mid, n);
            c.FMS.Remove(mid);
            await _db.SetFMSAsync(charUid, mid, 0);
            return true;
        }
        // Bit2 clair (row Add+Delete immédiate = net zéro, jamais
        // écrite) ou limite atteinte (DeleteMissionCompleteNum) :
        // FMS=1 reste posée → la quête compte « terminée » pour
        // If_FMS, tout en restant reprenable via le compteur.
        if (m.KeepLimitRow)
            await _db.DeleteCompleteNumAsync(charUid, mid);
        c.Completed.Add(mid);
        return false;
    }

    /// <summary>Accepte une mission pour le personnage — extrait du
    /// case op=1 pour être réutilisé par TriggerGroup (auto-accept
    /// natif des quêtes du groupe, TriggerGroupMission @0x807fd20).</summary>
    private async Task AcceptMissionAsync(MissionSession s, CharContext c,
        MissionParser.MissionDef? m, int mid)
    {
        if (m == null || c.Active.ContainsKey(mid) ||
            DoneBlocked(c, m)) return;
        var q = new ActiveQuest { Status = 1 };
        q.Required = KillRequired(m);
        if (q.Required == 0 && !HasBlockingObjective(m))
            q.Status = 2; // objectif purement "talk"/niveau déjà rempli
        c.Active[mid] = q;
        // FMS natif : accept = état 3, objectifs déjà remplis = 4 ; la
        // quête s'ajoute au tracker HUD (trace slot).
        var fms = q.Status == 2 ? 4 : 3;
        c.FMS[mid] = fms;
        var charUid = c.CharUid;
        await _db.SaveActiveAsync(charUid, mid, q.Packed);
        await _db.SetFMSAsync(charUid, mid, fms);
        await _db.SetTraceAsync(charUid, mid);
        await SendStateAsync(s, charUid, mid, ClientState(q.Status));
        // Enregistre côté client toutes les clés PMS déclarées de la
        // mission (count courant, 0 par défaut) — le client applique
        // le 391 kind=1 UNIQUEMENT pour une clé connue de sa
        // mission_list (IncPMS natif : "pms_id does not exist in the
        // mission_list"). Sans ce seed, le compteur du tracker ne se
        // met jamais à jour tant qu'aucun objectif n'a été crédité.
        foreach (var (pk, _) in PmsObjectives(m))
            await SendPmsAsync(s, charUid, pk, q.Pms.GetValueOrDefault(pk));
        // Objectif CheckBuff déjà satisfait à l'acceptation (buff déjà
        // posé) : FMSDoAccept natif appelle CheckQuestBuffStatus.
        if (QuestBuffRules.Objectives(m).Count > 0)
            await ReevaluateCompletionAsync(s, c, q, m);
        await PushNpcSymbolsAsync(s, c);
        EdenLog.Write($"[Mission] char={charUid} accept {mid} " +
            $"status={q.Status} req={q.Required}");
    }

    /// <summary>Recalcule la transition FMS 3↔4 d'une quête active après
    /// un changement d'objectif non PMS (inventaire, buff) — natif :
    /// bit satisfait + FMSCheckReward → SetFMS(4) ; bit effacé →
    /// SetFMS(3). Les 391 kind=0 sont émis par SendStateAsync.</summary>
    private async Task ReevaluateCompletionAsync(MissionSession s,
        CharContext c, ActiveQuest q, MissionParser.MissionDef m)
    {
        var charUid = c.CharUid;
        var done = AllPmsDoneOrVacuous(q, m) &&
            await AllItemsDoneAsync(charUid, m);
        if (done && q.Status == 1)
        {
            q.Status = 2;
            c.FMS[m.Id] = 4;
            await _db.SetFMSAsync(charUid, m.Id, 4);
            await _db.SaveActiveAsync(charUid, m.Id, q.Packed);
            await SendStateAsync(s, charUid, m.Id, ClientState(2),
                q.Progress);
            await PushNpcSymbolsAsync(s, c);
            EdenLog.Write($"[Mission] char={charUid} {m.Id} " +
                "objectifs remplis (réévaluation)");
        }
        else if (!done && q.Status == 2)
        {
            q.Status = 1;
            c.FMS[m.Id] = 3;
            await _db.SetFMSAsync(charUid, m.Id, 3);
            await _db.SaveActiveAsync(charUid, m.Id, q.Packed);
            await SendStateAsync(s, charUid, m.Id, ClientState(1),
                q.Progress);
            await PushNpcSymbolsAsync(s, c);
            EdenLog.Write($"[Mission] char={charUid} {m.Id} " +
                "objectif perdu : retour on-going (réévaluation)");
        }
    }

    /// <summary>Commande DBA <c>CheckMissionEnchant &lt;enchant&gt;
    /// [compteur]</c> (TC_CheckMissionEnchant @0x081f2960) : la zone
    /// signale qu'un enchant suivi par une quête a été posé/retiré. Le
    /// natif relit le compteur dans son miroir d'enchants ; chez nous
    /// la zone le joint en 2e argument (0 = retiré). Met à jour le
    /// miroir puis réévalue les quêtes actives portant un CheckBuff sur
    /// cet enchant.</summary>
    public async Task OnCheckMissionEnchantAsync(MissionSession s,
        int charUid, int enchantId, int count)
    {
        if (!QuestBuffRules.IsTrackedId(enchantId) ||
            !_chars.TryGetValue(charUid, out var c)) return;
        if (count > 0) c.Buffs[enchantId] = count;
        else c.Buffs.Remove(enchantId);
        foreach (var m in _data.Missions)
        {
            if (!c.Active.TryGetValue(m.Id, out var q) ||
                q.Status == 0xFE) continue;
            if (!QuestBuffRules.Objectives(m)
                    .Any(o => o.EnchantId == enchantId)) continue;
            await ReevaluateCompletionAsync(s, c, q, m);
        }
    }
    /// <summary>Items de récompense que la remise de <paramref name="m"/>
    /// peut créer : tous les RewardItem + l'option choisie de
    /// RewardChooseItem (index du client, repli sur la première).</summary>
    private static List<(int ItemId, int Count)> RewardItemsFor(
        MissionParser.MissionDef m, int chooseIdx)
    {
        var fixedItems = new List<(int, int)>();
        var choose = new List<(int, int)>();
        foreach (var r in m.Rewards)
        {
            int A(int i) => i < r.Args.Count &&
                int.TryParse(r.Args[i], out var v) ? v : 0;
            if (r.Name == "RewardItem" && A(0) > 0 && A(1) > 0)
                fixedItems.Add((A(0), A(1)));
            else if (r.Name == "RewardChooseItem" && A(0) > 0)
                choose.Add((A(0), Math.Max(1, A(1))));
        }
        if (choose.Count > 0)
            fixedItems.Add(choose[chooseIdx >= 0 && chooseIdx < choose.Count
                ? chooseIdx : 0]);
        return fixedItems;
    }

    /// <summary>Le sac peut-il accueillir TOUTES les récompenses d'item
    /// (pire cas) ? Simulation : les items rendus par la quête
    /// (ItemCollect…) sont retirés d'abord et libèrent leurs
    /// emplacements ; chaque récompense comble d'abord les piles
    /// partielles du même item, le reste prend des emplacements
    /// neufs. Faux = la remise doit être refusée (le natif interdit de
    /// rendre une quête sac plein).</summary>
    private async Task<bool> HasRewardRoomAsync(int charUid,
        MissionParser.MissionDef m, int chooseIdx)
    {
        var rewards = RewardItemsFor(m, chooseIdx);
        var taken = FinishTakeItems(m);
        var ids = rewards.Select(r => r.ItemId)
            .Concat(taken.Select(t => t.ItemId)).Distinct().ToList();
        var stacks = await _db.GetBagStacksAsync(charUid, ids);
        var free = await _db.CountFreeSlotsAsync(charUid);
        int MaxOf(int id) => Math.Max(1,
            _data.Items.FirstOrDefault(i => i.Id == id)?.MaxStack ?? 1);
        foreach (var (itemId, req) in taken)
        {
            if (!stacks.TryGetValue(itemId, out var l)) continue;
            var left = req;
            l.Sort();
            for (var i = 0; i < l.Count && left > 0;)
            {
                var take = Math.Min(l[i], left);
                l[i] -= take; left -= take;
                if (l[i] <= 0) { l.RemoveAt(i); free++; }
                else i++;
            }
        }
        // Règle du jeu : sac plein ⇒ aucune remise (même sans item de
        // récompense) — au moins 1 emplacement libre après retrait.
        if (free <= 0) return false;
        foreach (var (itemId, cnt) in rewards)
        {
            var max = MaxOf(itemId);
            var left = cnt;
            if (max > 1 && stacks.TryGetValue(itemId, out var l))
                for (var i = 0; i < l.Count && left > 0; i++)
                {
                    var add = Math.Min(max - l[i], left);
                    if (add <= 0) continue;
                    l[i] += add; left -= add;
                }
            if (left <= 0) continue;
            var need = (left + max - 1) / max;
            free -= need;
            if (free < 0) return false;
            // Les nouvelles piles comptent pour les récompenses
            // suivantes du même item.
            if (max > 1)
            {
                if (!stacks.TryGetValue(itemId, out var l2))
                    stacks[itemId] = l2 = new List<int>();
                for (var k = 0; k < need; k++)
                    l2.Add(Math.Min(max, left - k * max));
            }
        }
        return true;
    }

    /// <summary>Application complète des récompenses col20 — équivalent
    /// du GainReward natif (@0x808da30) : or/exp/class-point/family-exp
    /// montants scalés + `clone_reward_item` par RewardItem +
    /// RewardChooseItem (index choisi par le client à la remise,
    /// u16 arg du MissionListOp op=2, 0xffff = pas de choix) + buffs +
    /// prestige (GainPrestigeExp) + special coins (GainSpecialCoin) +
    /// TriggerGroup (auto-accept des missions col25==gid).
    /// Les montants d'items partent en commandes zone
    /// (TransitionCommands — le natif fait de même : mission → zone
    /// `gain_*`/`clone_reward_item`).</summary>
    private async Task ApplyRewardsAsync(MissionSession s, CharContext c,
        MissionParser.MissionDef m, int chooseIdx)
    {
        var charUid = c.CharUid;
        // Scaling natif GainReward (@0x808da30) : lv_offset =
        // charLevel − missionLevel (mission+0x2c = S_Mission col3, le
        // "L0X" du titre col2 — vérifié : L03→col3=3). Montant final =
        // reward × 0.01 × K avec K choisi par seuils S_Parameter
        // 332..335 (4/6/8/10) → coeffs 336..340 (100/75/50/25/0 %) :
        // une quête trop basse par rapport au perso donne une fraction
        // — puis rien — de son exp/class-point/family-exp. L'or n'est
        // PAS scalé (pas de GainExp dans sa branche).
        var lvOffset = c.Level - Col(m, 3);
        long Scaled(long v)
        {
            if (v <= 0) return 0;
            var p = _data.Parameters;
            var k = lvOffset <= p.Get(332, 4) ? p.Get(336, 100)
                  : lvOffset <= p.Get(333, 6) ? p.Get(337, 75)
                  : lvOffset <= p.Get(334, 8) ? p.Get(338, 50)
                  : lvOffset <= p.Get(335, 10) ? p.Get(339, 25)
                  : p.Get(340, 0);
            return (long)(v * 0.01f * k);
        }
        long gold = 0, exp = 0;
        // Choix : la liste ordonnée des RewardChooseItem — le client
        // envoie l'index de l'option retenue dans l'u16 arg du claim
        // (natif : GainReward(mid, chooseIdx) valide que l'index est
        // dans la liste).
        var choose = new List<(int ItemId, int Count)>();
        foreach (var r in m.Rewards)
        {
            int A(int i) => i < r.Args.Count &&
                int.TryParse(r.Args[i], out var v) ? v : 0;
            switch (r.Name)
            {
                case "RewardGold":
                    gold += A(0);
                    break;
                case "RewardExp":
                    exp += Scaled(A(0)); // gain_exp scalé par lv_offset
                    break;
                case "RewardItem":
                    // clone_reward_item id n — la zone insère et notifie.
                    if (A(0) > 0 && A(1) > 0)
                        await SendTransitionAsync(s, c.NodeId, charUid,
                            $"eden-give-item {A(0)} {A(1)}");
                    break;
                case "RewardChooseItem":
                    choose.Add((A(0), Math.Max(1, A(1))));
                    break;
                case "RewardClassPoint":
                    // gain_class_point scalé (même lv_offset natif) —
                    // même chemin zone que le CP de kill
                    // (GrantClassPoint + GainNp).
                    {
                        var cpv = Scaled(A(0));
                        if (cpv > 0)
                            await SendTransitionAsync(s, c.NodeId, charUid,
                                $"eden-class-point {cpv}");
                    }
                    break;
                case "RewardBuff":
                    // RewardBuff enchantId durée — l'enchant est posé
                    // côté zone (CNC_CZ_ClientEnchantAdd 299).
                    if (A(0) > 0)
                        await SendTransitionAsync(s, c.NodeId, charUid,
                            $"eden-buff {A(0)} {A(1)}");
                    break;
                case "RewardSpecialCoin":
                    // GainSpecialCoin(chid, type, amount, 3) →
                    // special_coins.coinNN += amount. Le script porte le
                    // TYPE natif 0-11 → colonne coin{type+1} (types 6-8
                    // interdits par le natif, absents des scripts).
                    if (A(0) >= 0 && A(1) != 0)
                        await _db.AddSpecialCoinAsync(charUid,
                            A(0) + 1, A(1));
                    break;
                case "RewardReputation":
                    // "RewardReputation id + n" — le signe est un
                    // token séparé ; GainPrestigeExp(chid, id, ±n).
                    {
                        var sign = r.Args.Any(a2 => a2 == "-") ? -1 : 1;
                        var amt = r.Args.Select(a2 => int.TryParse(a2,
                            out var v) ? v : 0)
                            .Skip(1).FirstOrDefault(v => v != 0);
                        if (A(0) > 0 && amt != 0)
                            await _db.AddPrestigeExpAsync(charUid, A(0),
                                sign * amt);
                    }
                    break;
                case "RewardFamilyExp":
                    // gain_family_exp natif : crédite family.exp de
                    // la guilde du perso (AddFamilyExpAsync — même
                    // champ que TC_GainFamilyExp).
                    if (Scaled(A(0)) > 0)
                        await _db.AddFamilyExpAsync(charUid,
                            (int)Scaled(A(0)));
                    break;
                case "TriggerGroup":
                    // TriggerGroupMission natif : auto-accepte toutes
                    // les missions du groupe éligibles pour ce joueur.
                    if (A(0) > 0)
                        await FireTriggerGroupAsync(s, c, A(0));
                    break;
                case "MC":
                    // Sous-commandes natives (mission/decompiled.c) :
                    // add_appellation → INSERT player_appellation,
                    // AddClass → "add_class N" (TC_AddClass →
                    //   classlist + sorts de classe),
                    // ForeverGold → "GainForeverGold" (char+0x4b4),
                    // Announce → CNC_MW_WorldTextCommand "Announce N".
                    // Toutes finissent en commande zone/world —
                    // même routage 58 que le natif.
                    if (r.Args.Count >= 2)
                    {
                        var mcVerb = r.Args[0];
                        var mcArg = A(1);
                        var zoneCmd = mcVerb switch
                        {
                            "add_appellation" => $"eden-appellation {mcArg}",
                            "AddClass" => $"eden-add-class {mcArg}",
                            "ForeverGold" => $"eden-forever-gold {mcArg}",
                            "Announce" => $"eden-announce {mcArg}",
                            _ => null,
                        };
                        if (zoneCmd != null)
                            await SendTransitionAsync(s, c.NodeId,
                                charUid, zoneCmd);
                        else
                            EdenLog.Write($"[Mission] char={charUid} " +
                                $"{m.Id} : MC '{mcVerb}' ignoré");
                    }
                    break;
                default:
                    EdenLog.Write($"[Mission] char={charUid} " +
                        $"{m.Id} : reward '{r.Name}' ignoré");
                    break;
            }
        }
        // Choix d'item : l'index valide pointe dans la liste —
        // 0xffff/absent → première option (le client n'envoie un index
        // que si la quête a des choix ; quêtes sans choix → idx=-1).
        if (choose.Count > 0)
        {
            var idx = chooseIdx >= 0 && chooseIdx < choose.Count
                ? chooseIdx : 0;
            var (itemId, cnt) = choose[idx];
            if (itemId > 0)
                await SendTransitionAsync(s, c.NodeId, charUid,
                    $"eden-give-item {itemId} {cnt}");
        }
        if (gold > 0 || exp > 0)
        {
            await _db.AddGoldExpAsync(charUid, gold, exp);
            await SendTransitionAsync(s, c.NodeId, charUid,
                $"eden-reward {gold} {exp}");
        }
    }
    /// <summary>Auto-accepte toutes les missions dont col25 == groupId
    /// pour ce personnage — `TriggerGroupMission` natif : les missions
    /// du groupe dont le joueur remplit l'approbation (FMSCheckApproval)
    /// passent SetFMS(3) = acceptées, ce qui fait apparaître leurs
    /// objectifs/NPC cinématiques.</summary>
    private async Task FireTriggerGroupAsync(MissionSession s,
        CharContext c, int groupId)
    {
        // Sync du niveau/gender live (les conditions les consomment).
        var (lvl, gender, _) = await _db.GetLevelGenderNodeAsync(c.CharUid);
        c.Level = lvl; c.Gender = gender;
        foreach (var m2 in _data.Missions.Where(x => x.TriggerGroup == groupId))
        {
            // FMSCheckApproval natif : pas active, pas finie, FMS
            // vierge/reprenable, niveau col8 atteint, conditions col13.
            // Quête à limite : gate compteur seul (reprenable tant que
            // le compteur < limite — re-fire du groupe autorisé).
            var blocked = m2.CompleteLimit > 0
                ? DoneBlocked(c, m2)
                : c.Completed.Contains(m2.Id) ||
                  c.FMS.GetValueOrDefault(m2.Id) is not (0 or 6);
            if (c.Active.ContainsKey(m2.Id) || blocked)
                continue;
            var reqLevel = Col(m2, 8);
            if (reqLevel > 0 && lvl < reqLevel) continue;
            if (!ConditionsPass(c, m2)) continue;
            await AcceptMissionAsync(s, c, m2, m2.Id);
            EdenLog.Write($"[Mission] char={c.CharUid} trigger-group " +
                $"{groupId} : auto-accept {m2.Id}");
        }
    }
    /// <summary>Récompenses or/XP du script col20 (conservé pour les
    /// appels de résumé — l'application réelle passe par
    /// ApplyRewardsAsync).</summary>
    private static (long Gold, long Exp) RewardGoldExp(
        MissionParser.MissionDef m)
    {
        long gold = 0, exp = 0;
        foreach (var r in m.Rewards)
        {
            if (r.Name == "RewardGold" && r.Args.Count >= 1 &&
                long.TryParse(r.Args[0], out var g)) gold += g;
            if (r.Name == "RewardExp" && r.Args.Count >= 1 &&
                long.TryParse(r.Args[0], out var e)) exp += e;
        }
        return (gold, exp);
    }
}
