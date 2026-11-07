using Eden.Core.Networking;

namespace Eden.Core.Game.Family;

/// <summary>
/// Codecs famille monde → client (groupe WorldServer). Formats prouvés par
/// désassemblage du binaire natif :
/// <list type="bullet">
///   <item><c>CNC_CW_ClientQueryFamilyInfo</c> (50) —
///   <c>Serialize @ world 0x80ceb10</c> + <c>Execute @ 0x80e7480</c> :
///   [u8 type], type=2 seul si famille absente ; type 5/6/7 → un champ puis
///   fin ; sinon [u16 world_id] puis bloc famille si type&lt;2, puis
///   [u16 n] + records si type=8, puis titres si type∈{0,3}, privilèges
///   si type∈{0,4} (u16 0 sinon).</item>
///   <item><c>CNC_CW_ClientUpdateFamilyMember</c> (46) —
///   <c>Serialize @ 0x80635d0</c>, remplissage
///   <c>CGlobalInfo::UpdateFamilyMemberProxy @ 0x8051860</c>.</item>
///   <item><c>CNC_CW_ClientFamilyOpException</c> (47) — [u8 op].</item>
///   <item><c>CNC_CW_ClientFamilyInvitation</c> (48) —
///   <c>CGlobalInfo::FamilyInvitation</c> : [i32 0][str inviter][str family].</item>
///   <item><c>CNC_CW_ClientFamilyQuit</c> (49) — vide.</item>
///   <item><c>CNC_CW_ClientFamilySchedule</c> (51) — ctor dans
///   <c>ServerQueryFamilyInfo::Execute</c> : [u8][u16] + champs agenda.</item>
///   <item><c>CNC_CW_ClientFamilyLogUpdate</c> (68) —
///   <c>Serialize @ 0x80cf9ac</c> : [s16][s16] puis 6 vecteurs
///   (s16, i32×3, str×2).</item>
///   <item><c>CNC_CW_ClientFamilyEmblem</c> (74) — réponse à
///   <c>CNE_CW_ServerQueryFamilyEmblem</c> (73) : [i32 family][i32][u16 len]
///   [u8×len blob].</item>
///   <item><c>CNC_CW_ClientQueryFamilyRecruitList</c> (80) — op 0x4F.</item>
/// </list>
/// </summary>
public static class FamilyPackets
{
    /// <summary>NCID monde→client du résumé famille.</summary>
    public const int NcidQueryFamilyInfo = 50;
    /// <summary>NCID monde→client du membre (ajout/maj/suppression
    /// signalée par <paramref name="delete"/>).</summary>
    public const int NcidUpdateFamilyMember = 46;
    /// <summary>NCID du code d'erreur d'opération.</summary>
    public const int NcidFamilyOpException = 47;
    /// <summary>NCID de l'invitation entrante.</summary>
    public const int NcidFamilyInvitation = 48;
    /// <summary>NCID de sortie de famille.</summary>
    public const int NcidFamilyQuit = 49;
    /// <summary>NCID de l'agenda.</summary>
    public const int NcidFamilySchedule = 51;
    /// <summary>NCID du journal.</summary>
    public const int NcidFamilyLogUpdate = 68;

    /// <summary>NCID 67 — <c>CNC_CW_ClientFamilyAchievementUpdate</c>
    /// (push succès famille aux membres).</summary>
    public const int NcidFamilyAchievementUpdate = 67;
    /// <summary>NCID du blob d'emblème.</summary>
    public const int NcidFamilyEmblem = 74;
    /// <summary>NCID de la liste de recrutement.</summary>
    public const int NcidQueryFamilyRecruitList = 80;
    /// <summary>NCID des données prestige perso
    /// (<c>CNC_CW_ClientUpdatePrestigeData</c> natif).</summary>
    public const int NcidClientUpdatePrestigeData = 60;

    /// <summary>Trame [u8] vide = sortie de famille (NCID 49).</summary>
    public static byte[] FamilyQuit() => [];

    /// <summary>Erreur d'opération — <c>ClientFamilyOpException</c>
    /// (47) : [u8 sub-op en échec].</summary>
    public static byte[] FamilyOpException(byte op) => [op];

    /// <summary>Invitation — <c>ClientFamilyInvitation</c> (48) :
    /// [i32 0][str inviter_name][str family_name]
    /// (<c>CGlobalInfo::FamilyInvitation</c> : piVar[2]=0, +3=nom joueur,
    /// +4=nom famille).</summary>
    public static byte[] FamilyInvitation(string inviterName, string familyName)
    {
        var s = new OutBinStream();
        s.WriteInt(0);                 // i32 — toujours 0 natif (champ réservé)
        s.WriteString(inviterName);    // str — nom de l'inviteur
        s.WriteString(familyName);     // str — nom de la famille
        return s.ToArray();
    }

    /// <summary>Record membre partagé par les trames 46 et 50-type-8 —
    /// layout <c>SCharacter</c> du proxy natif.</summary>
    private static void WriteMemberRecord(OutBinStream s, FamilyMemberSummary m, bool queryMode)
    {
        s.WriteInt(m.CharacterId);            // i32 — char_id
        if (queryMode)
            s.WriteInt(m.AddFamilyTime);      // i32 — add_family_time (position 2 en type 8)
        s.WriteString(m.Name ?? "");                // str — given_name
        s.WriteShort(m.Level);                // s16 — level
        s.WriteShort(m.NodeId);               // s16 — node_id
        if (!queryMode)
            s.WriteShort(m.RaceId);           // s16 — race_id (46 seulement)
        s.WriteShort(m.ClassId);              // s16 — class_id
        s.WriteShort(m.ClassLevel);           // s16 — class_lv
        s.WriteByte((byte)(m.Online ? 1 : 0)); // u8 — online (SChar+0x18 && +0x80 != 0xff)
        s.WriteShort((short)m.Field1a);       // u16 — SChar+0x1a (non prouvé, 0)
        s.WriteByte(m.Gender);                // u8 — gender_id
        s.WriteShort((short)m.Field1c);       // u16 — SChar+0x1c (non prouvé, 0)
        s.WriteInt(m.Field20);                // i32 — SChar+0x20 (non prouvé, 0)
        s.WriteInt(m.LastSavingTime);         // i32 — last_saving_time
        s.WriteString(m.SelfComment ?? "");         // str — self_comment
        s.WriteString(m.FamilyComment ?? "");       // str — family_comment
        if (queryMode)
        {
            s.WriteInt(m.ContributionPoint);  // i32 — contribution (fin de record type 8)
        }
        else
        {
            s.WriteInt(m.AddFamilyTime);      // i32 — add_family_time (position 16 en 46)
            s.WriteInt(m.ContributionPoint);  // u32 — family_contribution_point
        }
    }

    /// <summary><c>CNC_CW_ClientUpdateFamilyMember</c> (46) — record
    /// complet d'un membre poussé à tous les membres en ligne
    /// (<c>UpdateFamilyMemberProxy</c>). Quand le membre part/est exclu,
    /// le natif envoie la même trame avec online=0 puis le retire.</summary>
    public static byte[] UpdateFamilyMember(FamilyMemberSummary m)
    {
        var s = new OutBinStream();
        WriteMemberRecord(s, m, queryMode: false);
        return s.ToArray();
    }

    /// <summary><c>CNC_CW_ClientQueryFamilyInfo</c> (50) — trame
    /// complète pour <paramref name="type"/> :
    /// <list>
    ///   <item>famille absente → [u8 2] seul (Serialize natif,
    ///   famille NULL entre Execute et flush).</item>
    ///   <item>type 5 → [u8 5][i32 treasury] ; type 6 → [u8 6][i32
    ///   elfking_tid] ; type 7 → [u8 7][s16 spell_point].</item>
    ///   <item>type≠8 → [u16 worldServerId] puis, si type&lt;2, le bloc
    ///   famille (19 champs dans l'ordre natif).</item>
    ///   <item>type=8 → [u16 n] + records membres (chunks de 30 faits
    ///   par l'appelant — <paramref name="members"/> = le chunk).</item>
    ///   <item>titres si type∈{0,3} (u16 0 sinon), privilèges si
    ///   type∈{0,4} (u16 0 sinon).</item>
    /// </list></summary>
    public static byte[] QueryFamilyInfo(Family? fam, byte type, ushort worldServerId, IReadOnlyList<FamilyMemberSummary>? members = null)
    {
        var s = new OutBinStream();
        if (fam is null)
        {
            s.WriteByte(2);            // u8 — type 2 = famille absente
            return s.ToArray();
        }
        s.WriteByte(type);             // u8 — type demandé
        switch (type)
        {
            case 5: s.WriteInt(fam.Treasury); return s.ToArray();      // fam+0x2c
            case 6: s.WriteInt(fam.ElfkingTid); return s.ToArray();    // fam+0x38
            case 7: s.WriteShort(fam.SpellPoint); return s.ToArray();  // fam+0x34
        }
        if (type != 8)
            s.WriteShort((short)worldServerId); // u16 — WorldServerID natif
        if (type < 2)
            WriteFamilyBlock(s, fam);
        s.WriteShort((short)(type == 8 ? (members?.Count ?? 0) : 0)); // u16 — nb membres
        if (type == 8 && members is not null)
            foreach (var m in members)
                WriteMemberRecord(s, m, queryMode: true);
        if (type == 0 || type == 3)
        {
            s.WriteShort((short)Family.TitleCount); // u16 — nb titres
            foreach (var t in fam.Titles)
                s.WriteString(t ?? "");           // str — member/leader/cadre_2..8
        }
        else s.WriteShort(0);                     // u16 0 — titres non demandés
        if (type == 0 || type == 4)
        {
            s.WriteShort((short)Family.PrivilegeCount); // u16 — nb privilèges
            foreach (var p in fam.Privileges)
                s.WriteInt(p);                    // i32 — *_privilege
        }
        else s.WriteShort(0);                     // u16 0 — privilèges non demandés
        return s.ToArray();
    }

    /// <summary>Bloc famille type&lt;2 — 19 champs dans l'ordre du
    /// Serialize natif (offsets fam : +4,+8,+0x10,+0x1c,+0xe4,+0x14,+0xc,
    /// +0x20,+0x24,+0x28,+0x2c,+0x30,+0x32,+0x38,+0x34,+0x4c,+0x3c,+0xe8,
    /// +0xec ; sémantique via CFamilyS wire + LoadFamily mission).</summary>
    private static void WriteFamilyBlock(OutBinStream s, Family f)
    {
        s.WriteInt(f.Id);                 // i32 — family id
        s.WriteInt(f.BeCreateTime);       // i32 — be_create_time
        s.WriteString(f.Name ?? "");            // str — name
        s.WriteByte(f.Level);             // u8 — lv
        s.WriteInt(f.Exp);                // i32 — exp
        s.WriteInt(f.LeaderId);           // i32 — leader_id
        s.WriteInt(f.Exploit);            // i32 — exploit
        s.WriteInt(f.Emblem);             // i32 — emblem
        s.WriteShort((short)f.Uniform);   // s16 — uniform
        s.WriteString(f.Info ?? "");            // str — info
        s.WriteInt(f.Treasury);           // i32 — treasury
        s.WriteShort(f.TaxRate);          // s16 — tax_rate
        s.WriteShort(f.BusinessTax);      // s16 — business_tax
        s.WriteInt(f.ElfkingTid);         // i32 — elfking_tid
        s.WriteShort(f.SpellPoint);       // s16 — spell_point
        s.WriteByte((byte)f.Recruit);     // u8 — état recrutement (fam+0x4c)
        s.WriteShort(f.HouseOpen);        // s16 — house_open
        s.WriteShort(f.Recruit);          // s16 — recruit
        s.WriteInt(f.Honor);              // i32 — honor
    }

    /// <summary><c>CNC_CW_ClientFamilyLogUpdate</c> (68) — journal
    /// famille : [s16 type][s16 state] puis 6 vecteurs parallèles
    /// (s16 log_type, i32 log_id, i32 date, i32 para, str, str) —
    /// <c>Serialize @ 0x80cf9ac</c>, alimenté dans
    /// <c>ServerQueryFamilyInfo::Execute</c> depuis la map de logs.</summary>
    public static byte[] FamilyLogUpdate(short type, short state, IReadOnlyList<FamilyLogEntry> logs)
    {
        var s = new OutBinStream();
        s.WriteShort(type);                 // s16 — type de log
        s.WriteShort(state);                // s16 — état (msgboard_state)
        s.WriteShort((short)logs.Count);    // u16 + s16[] — log_type
        foreach (var l in logs) s.WriteShort(l.Type);
        s.WriteShort((short)logs.Count);    // u16 + i32[] — log_id
        foreach (var l in logs) s.WriteInt(l.LogId);
        s.WriteShort((short)logs.Count);    // u16 + i32[] — date
        foreach (var l in logs) s.WriteInt(l.Date);
        s.WriteShort((short)logs.Count);    // u16 + i32[] — paramètre
        foreach (var l in logs) s.WriteInt(l.Para);
        s.WriteShort((short)logs.Count);    // u16 + str[] — string_param_1
        foreach (var l in logs) s.WriteString(l.Str1 ?? "");
        s.WriteShort((short)logs.Count);    // u16 + str[] — string_param_2
        foreach (var l in logs) s.WriteString(l.Str2 ?? "");
        return s.ToArray();
    }

    /// <summary><c>CNC_CW_ClientFamilyAchievementUpdate</c> (67) —
    /// format variable prouvé par <c>::Serialize</c> monde @0x080cdfb0 :
    /// <code>
    ///  [u8 flag]
    ///  flag∈{2,3}  : [u32 total]                 (fam+0x48 — total_point)
    ///  flag==1    : [i32 0]  (operator<<(param_1, 0) — littéral int)
    ///  sinon      : [u16 n][n×{s16 type, u32 point, i32 charId, u32 channel}]
    ///               (flag=3 : charId/channel = 0, _M_fill_assign natif)
    ///  flag≠0     : [u16 m][m×{i32 pid, i32 date, i32 point, s16 achId}]
    ///               (log — family_achievement_log, table morte → m=0)
    /// </code>
    /// flag=0 = push d'une ligne (<c>CNE_DBA_ClientChangeFamily</c> op
    /// 0x14 monde) ; flag=3 = synchro complète au login
    /// (<c>CNE_CW_ServerQueryFamilyInfo::Execute</c> @0x080e6480).</summary>
    /// <param name="entries">Paires (type, point) de fam+0xb4 ; pour le
    /// push unitaire, une seule entrée avec charId/channel réels.</param>
    public static byte[] FamilyAchievementUpdate(byte flag, uint total,
        IReadOnlyList<(short Type, uint Point, int CharId, uint Channel)> entries)
    {
        var s = new OutBinStream();
        s.WriteByte(flag);                          // u8 — flag
        if ((byte)(flag - 2) < 2)
            s.WriteInt((int)total);                 // u32 — total_point (flag 2/3)
        if (flag == 1)
            s.WriteInt(0);                          // i32 0 — variante vide
                                                    // (operator<<(param_1, 0) : littéral int natif)
        else
        {
            s.WriteShort((short)entries.Count);     // u16 — nb entrées
            foreach (var e in entries)
            {
                s.WriteShort(e.Type);               // s16 — achievement_type (GroupKey)
                s.WriteInt((int)e.Point);           // u32 — points cumulés
                s.WriteInt(flag == 3 ? 0 : e.CharId);   // i32 — charId (0 en synchro)
                s.WriteInt((int)(flag == 3 ? 0 : e.Channel)); // u32 — channel (0 idem)
            }
        }
        if (flag != 0)
            s.WriteShort(0);                        // u16 0 — log (family_achievement_log morte)
        return s.ToArray();
    }

    /// <summary><c>CNC_CW_ClientFamilyEmblem</c> (74) — réponse à la
    /// requête 73 : [i32 family_id][i32][u16 len][blob].
    /// Le natif vérifie la présence puis envoie le blob
    /// <c>family_emblem.emblem</c> (Deserialize/Execute 73 monde).</summary>
    public static byte[] FamilyEmblem(int familyId, int field2, byte[]? blob)
    {
        var s = new OutBinStream();
        s.WriteInt(familyId);                        // i32 — family_id
        s.WriteInt(field2);                          // i32 — champ requête
        var data = blob ?? [];
        s.WriteShort((short)data.Length);            // u16 — taille blob
        s.WriteBytes(data);                          // u8×len — emblem data
        return s.ToArray();
    }

    /// <summary><c>CNC_CW_ClientFamilySchedule</c> (51) — msgboard.
    /// <b>Wire maison Godot-first</b> : `[u8 type][u16 n][n×{s16 msg_id,
    /// i32 date, str title, str content, str player}][u8 0][s16 état]`.
    /// Le format natif (<c>::Serialize</c> monde @0x080cd…, décodé lot
    /// E5) est différent et par type : 1 = `[u8][s16 id][3×str]
    /// [i32 dest+0x74]` (ajout), 2 = `[u8][s16 id]` (suppression),
    /// 3 = `[u8][u16 n][n×records]` (refresh, sans queue),
    /// 0/4 = liste + `[i32 dest+0x74]`, 5 = `[u8]` seul (pas de famille).
    /// Le client Godot fait un clear+fill sur réception → on émet
    /// toujours la liste complète (type 0) ; les 3 octets de queue
    /// (u8 0 + s16 état msgboard_state) sont inertes côté client.
    /// `dest+0x74` natif (champ par-destinataire, sémantique non
    /// résolue) ne serait pas portable → divergence assumée.</summary>
    public static byte[] FamilySchedule(byte type, ushort flag, IReadOnlyList<FamilyMessage> schedule)
    {
        var s = new OutBinStream();
        s.WriteByte(type);                    // u8 — type (0 = liste)
        s.WriteShort((short)schedule.Count);  // u16 — nb entrées
        foreach (var m in schedule)
        {
            s.WriteShort((short)m.MessageId); // s16 — message_id
            s.WriteInt(m.CreateDate);         // i32 — create_date
            s.WriteString(m.Title ?? "");           // str — title
            s.WriteString(m.Content ?? "");         // str — content
            s.WriteString(m.PlayerName ?? "");      // str — player_name
        }
        s.WriteByte(0);                       // u8 — flag natif (+0xe = 0)
        s.WriteShort((short)flag);            // s16 — état
        return s.ToArray();
    }

    /// <summary><c>CNC_CW_ClientQueryFamilyRecruitList</c> (80) —
    /// liste des familles en recrutement (op 0x4F). Format prouvé par
    /// <c>CNC_CW_ClientQueryFamilyRecruitList::Serialize @0x8062d40</c>
    /// + remplissage <c>CGlobalInfo::FamilySendRecruitPage @0x8056200</c>
    /// (world) :
    /// <code>
    ///  s16 n ; n × { i32 fam+4 id, str fam+0x10 nom, str fam+0x18
    ///  leader, s16 fam+0x7c nb membres, s16 fam+0x1c niveau,
    ///  s16 fam+0x3c } ; puis s16 page réelle, s16 total recruteuses.
    /// </code>
    /// Le désérialiseur client (rf_game @0xcd9d90) lit exactement ce
    /// format — un ancien build inventé (2 strings + u8, sans queue)
    /// le faisait lire hors buffer → invalid-param → int3.</summary>
    public static byte[] QueryFamilyRecruitList(
        IReadOnlyList<Family> recruited, short page, short total)
    {
        var s = new OutBinStream();
        s.WriteShort((short)recruited.Count); // s16 — nb familles de la page
        foreach (var f in recruited)
        {
            s.WriteInt(f.Id);                        // i32 — fam+4 family id
            s.WriteString(f.Name ?? "");             // str — fam+0x10 nom
            s.WriteString(f.LeaderName ?? "");       // str — fam+0x18 leader
            s.WriteShort((short)f.Members.Count);    // s16 — fam+0x7c membres
            s.WriteShort(f.Level);                   // s16 — fam+0x1c (u8→s16) niveau
            s.WriteShort(f.Recruit);                 // s16 — fam+0x3c type recrutement (sem. non prouvée)
        }
        s.WriteShort(page);   // s16 — page effective (param_2 recalé, fam+0x13)
        s.WriteShort(total);  // s16 — total familles recruteuses (fam+0x4e)
        return s.ToArray();
    }

    /// <summary>Une famille des résultats de recherche de ville —
    /// champs scalaires du vecteur 0-7 du 502 + items du conteneur 0
    /// (vecteurs 9-16, <c>SFamily+0x7c[0]</c> natif).</summary>
    public sealed record FamilyTownQueryEntry(int FamilyId,
        short Level, string Name, string LeaderName, uint Exp,
        short BusinessTax, int MemberCount, short HouseOpen,
        IReadOnlyList<FamilyBuildingItem> Buildings);

    /// <summary>Un membre listé par le 503 — vecteurs
    /// <c>CNC_ZMis_ZoneServerFamilyTownMemberQuery</c> natifs.</summary>
    public sealed record FamilyTownMemberEntry(int CharacterId,
        string Name, short Level, byte Rank, short NodeId);

    /// <summary><c>CNC_CZ_ClientFamilyTownQuery</c> (502, zone→client)
    /// — réponse à la recherche de ville 174. Format prouvé par
    /// <c>CNC_CZ_ClientFamilyTownQuery::CNC_ ctor @0x8148b90</c> +
    /// <c>Serialize</c> (zone) : <b>17 vecteurs parallèles</b>, chacun
    /// <c>[u16 count][elems]</c>, dans l'ordre natif :
    /// <code>
    ///  0 i32 fam+4 id          1 s16 (char)fam+0x18 level
    ///  2 str fam+0xc name      3 str fam+0x14 leader_name
    ///  4 u32 fam+0x28 exp      5 s16 fam+0x32 business_tax
    ///  6 u32 fam+0xfc membres  7 s16 fam+0x3c house_open
    ///  8 u32 nb bâtiments/famille (conteneur 0)
    ///  9 s16 loc   10 i32 item_tid (u16 zext)  11 i32 tid
    /// 12 s16 item_type        13 s16 durability
    /// 14 s16 bld level        15 u32 bld exp    16 u32 due_date
    /// </code>
    /// Le ctor natif exclut les familles sans conteneur 0
    /// (<c>fam+0x7c</c> sans clé 0) — l'appelant filtre avant. La
    /// classe générée aplatit le format : codec dédié.</summary>
    public static byte[] FamilyTownQuery(
        IReadOnlyList<FamilyTownQueryEntry> entries)
    {
        var s = new OutBinStream();
        var n = entries.Count;
        s.WriteShort((short)n);            // u16 — vec0 : family ids
        foreach (var e in entries) s.WriteInt(e.FamilyId);
        s.WriteShort((short)n);            // u16 — vec1 : niveaux
        foreach (var e in entries) s.WriteShort(e.Level);
        s.WriteShort((short)n);            // u16 — vec2 : noms
        foreach (var e in entries) s.WriteString(e.Name ?? "");
        s.WriteShort((short)n);            // u16 — vec3 : noms de chef
        foreach (var e in entries) s.WriteString(e.LeaderName ?? "");
        s.WriteShort((short)n);            // u16 — vec4 : exp
        foreach (var e in entries) s.WriteInt((int)e.Exp);
        s.WriteShort((short)n);            // u16 — vec5 : business_tax
        foreach (var e in entries) s.WriteShort(e.BusinessTax);
        s.WriteShort((short)n);            // u16 — vec6 : nb membres
        foreach (var e in entries) s.WriteInt(e.MemberCount);
        s.WriteShort((short)n);            // u16 — vec7 : house_open
        foreach (var e in entries) s.WriteShort(e.HouseOpen);
        s.WriteShort((short)n);            // u16 — vec8 : nb bâtiments/fam
        foreach (var e in entries) s.WriteInt(e.Buildings.Count);
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteShort(b.Loc);           // s16 — vec9 : loc
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            // i32 — vec10 : item_tid — le natif lit (uint)*(ushort*)
            // (+0x84) : élargissement non signé.
            s.WriteInt(b.ItemTid & 0xffff);
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteInt(b.Tid);             // i32 — vec11 : tid template
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteShort(b.ItemType);      // s16 — vec12 : item_type
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteShort(b.Durability);    // s16 — vec13 : durability
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteShort(b.Level);         // s16 — vec14 : bld level
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteInt(b.Exp);             // u32 — vec15 : bld exp
        s.WriteShort((short)entries.Sum(e => e.Buildings.Count));
        foreach (var e in entries) foreach (var b in e.Buildings)
            s.WriteInt(b.DueDate);         // u32 — vec16 : due_date
        return s.ToArray();
    }

    /// <summary><c>CNC_CZ_ClientFamilyTownMemberQuery</c> (503,
    /// zone→client) — membres d'une ville de famille. Format prouvé
    /// par <c>TC_FamilyTownMemberQuery</c> mission @0x820a670 +
    /// <c>CNC_ZMis_ZoneServerFamilyTownMemberQuery</c> (la zone recopie
    /// les 6 vecteurs dans le 503) : chacun <c>[u16 count][elems]</c> :
    /// <code>
    /// 0 i32 char_id (exec+8)     1 str name (exec+0xc)
    /// 2 s16 level (exec+0x5c)    3 u8 rank (1=leader, sinon cadre
    ///   fam+0x110→+4, sinon 0)   4 s16 node_id (exec+0x30)
    /// 5 str titres famille (vecteur fam+0x124 — niveau réponse,
    ///   PAS par membre)
    /// </code></summary>
    public static byte[] FamilyTownMemberQuery(
        IReadOnlyList<FamilyTownMemberEntry> members,
        IReadOnlyList<string> titles)
    {
        var s = new OutBinStream();
        s.WriteShort((short)members.Count);     // u16 — vec0 : char_ids
        foreach (var m in members) s.WriteInt(m.CharacterId);
        s.WriteShort((short)members.Count);     // u16 — vec1 : noms
        foreach (var m in members) s.WriteString(m.Name ?? "");
        s.WriteShort((short)members.Count);     // u16 — vec2 : niveaux
        foreach (var m in members) s.WriteShort(m.Level);
        s.WriteShort((short)members.Count);     // u16 — vec3 : rangs
        foreach (var m in members) s.WriteByte(m.Rank);
        s.WriteShort((short)members.Count);     // u16 — vec4 : node_ids
        foreach (var m in members) s.WriteShort(m.NodeId);
        s.WriteShort((short)titles.Count);      // u16 — vec5 : titres
        foreach (var t in titles) s.WriteString(t ?? "");
        return s.ToArray();
    }

    /// <summary><c>CNC_CZ_ClientFamilyContainerData</c> (484,
    /// zone→client) — contenu d'un conteneur de hall, émis après 483
    /// à l'ouverture de l'UI famille-bâtiment et après chaque mutation
    /// (130/131). Format prouvé par <c>Serialize @0x81e7b40</c> + ctor
    /// <c>@0x8578f20</c> (rempli par <c>CFamilyBuildingShop::OpenShop</c>
    /// @0x8578c90) :
    /// <code>
    /// [s16 type=1][s16 container_index][u16 n] puis par item :
    ///   [s16 loc][i32 item_tid (u16 zext du proto+4)][i32 tid]
    ///   [s16 item_type][s16 durability][s16 level][u32 exp]
    ///   [u32 due_date (+0x18)][u32 item+0xa0 (champ interne — 0)]
    /// </code>
    /// <paramref name="type"/> = param_1 du ctor (1 dans OpenShop).
    /// La classe générée aplatit le vecteur : codec dédié.</summary>
    public static byte[] FamilyContainerData(short type, short container,
        IReadOnlyList<FamilyBuildingItem> items)
    {
        var s = new OutBinStream();
        s.WriteShort(type);                // s16 — +4 : 1 (OpenShop)
        s.WriteShort(container);           // s16 — +6 : index conteneur
        s.WriteShort((short)items.Count);  // u16 — nb items
        foreach (var b in items)
        {
            s.WriteShort(b.Loc);           // s16 — clé de map (loc)
            // i32 — item_tid via proto+4 (u16 zext comme le 487).
            s.WriteInt(b.ItemTid & 0xffff);
            s.WriteInt(b.Tid);             // i32 — tid (item+0x80)
            s.WriteShort(b.ItemType);      // s16 — item_type (+0x86)
            s.WriteShort(b.Durability);    // s16 — durability (+0xc)
            s.WriteShort(b.Level);         // s16 — level (+0x94)
            s.WriteInt(b.Exp);             // u32 — exp (+0x98)
            s.WriteInt(b.DueDate);         // u32 — due_date (+0x18)
            s.WriteInt(0);                 // u32 — item+0xa0 : champ
            // interne non persisté (pas de colonne DB) — 0.
        }
        return s.ToArray();
    }

    /// <summary><c>CNC_CZ_ClientFamilyInstanceData</c> (487, zone→client)
    /// — données du hall de famille visité. Format prouvé par
    /// <c>Serialize @0x81e82e0</c> + construction dans
    /// <c>CNE_CZ_ZoneServerFamilyInstanceGetData::Execute @0x8256ee0</c>
    /// (zone) :
    /// <code>
    /// [i32 family_id (SFamily+4)][str nom (SFamily+0xc)]
    /// [s16 level (SFamily+0x18)][s16 house_open (SFamily+0x3c)]
    /// [s16 container_index (clé de map = 0)]
    /// [u16 n] puis par bâtiment :
    ///   [s16 loc][i32 item_tid][i32 tid][s16 item_type]
    ///   [s16 durability][s16 level][u32 exp]
    /// </code>
    /// Le natif itère la map&lt;loc,CFamilyItem*&gt; du conteneur 0 —
    /// ordre croissant de <c>loc</c>. La classe générée aplatit le
    /// vecteur (known_incomplete) : codec dédié.</summary>
    /// <param name="familyId">SFamily+4 de l'instance visitée.</param>
    /// <param name="familyName">SFamily+0xc — le nom est émis, ce n'est
    /// PAS le singleton vide (assign natif sur this+8).</param>
    /// <param name="familyLevel">SFamily+0x18 (char → s16).</param>
    /// <param name="houseOpen">SFamily+0x3c — node du hall (501/502).</param>
    /// <param name="buildings">Items du conteneur 0 triés par loc.</param>
    public static byte[] FamilyInstanceData(int familyId,
        string familyName, short familyLevel, short houseOpen,
        IReadOnlyList<FamilyBuildingItem> buildings)
    {
        var s = new OutBinStream();
        s.WriteInt(familyId);              // i32 — SFamily+4 (family_id)
        s.WriteString(familyName ?? "");   // str — SFamily+0xc (nom)
        s.WriteShort(familyLevel);         // s16 — SFamily+0x18 (niveau)
        s.WriteShort(houseOpen);           // s16 — SFamily+0x3c (node hall)
        s.WriteShort(0);                   // s16 — clé conteneur (0 natif)
        s.WriteShort((short)buildings.Count); // u16 — nb bâtiments
        foreach (var b in buildings)
        {
            s.WriteShort(b.Loc);           // s16 — clé de map (loc)
            // i32 — item_tid : le natif pousse (uint)*(ushort*)(+0x84)
            // — élargissement NON signé (0x8001 → 32769, pas -32767).
            s.WriteInt(b.ItemTid & 0xffff);
            s.WriteInt(b.Tid);             // i32 — tid bâtiment (zone +0x80)
            s.WriteShort(b.ItemType);      // s16 — item_type (zone +0x86)
            s.WriteShort(b.Durability);    // s16 — durabilité (zone +0xc)
            s.WriteShort(b.Level);         // s16 — niveau (zone +0x94)
            s.WriteInt(b.Exp);             // u32 — exp (zone +0x98)
        }
        return s.ToArray();
    }
}

// -----------------------------------------------------------------------------
//  Prestige — CNC_CW_ClientUpdatePrestigeData (NCID 60).
//  Format natif (CNC_CW_ClientUpdatePrestigeData::Serialize monde
//  @0x0812c8b0) : [u16 n] puis n × [s16 prestige_id][s16 level][i32 exp].
//  Émis natif par CNE_MW_WorldServerUpdatePrestigeData::Execute
//  (@0x0812bff0) à chaque changement + en synchro complète via
//  CGlobalInfo::AllPrestigeData (mission @0x0808b5a0, déclenchée par
//  TC_QueryFriend à l'attach exec).
// -----------------------------------------------------------------------------
public static class PrestigePackets
{
    /// <summary>Corps du paquet 60 : [u16 n][n×(s16 id, s16 level,
    /// i32 exp)] — trié par prestige_id (ordre std::map natif).</summary>
    public static byte[] UpdatePrestigeData(
        IReadOnlyList<(short Id, short Level, int Exp)> entries)
    {
        var s = new OutBinStream();
        s.WriteShort((short)entries.Count);     // u16 — nb entrées
        foreach (var e in entries)
        {
            s.WriteShort(e.Id);                 // s16 — prestige_id
            s.WriteShort(e.Level);              // s16 — level (1 natif)
            s.WriteInt(e.Exp);                  // i32 — exp
        }
        return s.ToArray();
    }
}
