namespace Eden.Core.Game.Family;

/// <summary>
/// Modèle famille natif — table <c>family</c> de edenfr_db + membres issus
/// de <c>player_characters.family_id</c> / <c>family_cadre_members</c>.
/// Les noms de champs suivent les colonnes du SELECT natif
/// <c>CFamilyDBO::LoadFamily</c> (mission @ 0x8181b0) et la structure
/// <c>CFamily</c> monde (<c>CNE_DBA_ClientAllFamily::Execute</c>
/// @ world 0x81153d0).
/// </summary>
public sealed class Family
{
    /// <summary>Nombre de titres natifs : member + leader + cadre 2..8.</summary>
    public const int TitleCount = 9;
    /// <summary>Nombre de privilèges natifs (14 colonnes *_privilege).</summary>
    public const int PrivilegeCount = 14;
    /// <summary>Membres par paquet type-8 de ClientQueryFamilyInfo
    /// (Execute @ world 0x80e74a0 : flush à iVar22 &gt; 0x1d).</summary>
    public const int MemberChunkSize = 30;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int LeaderId { get; set; }
    public string LeaderName { get; set; } = "";
    public int Exploit { get; set; }
    public byte Level { get; set; } = 1;
    public int Exp { get; set; }
    public int Emblem { get; set; } = -1;
    public int Uniform { get; set; } = -1;
    public int Flag { get; set; } = -1;
    public string Info { get; set; } = "";
    public int Treasury { get; set; }
    public short TaxRate { get; set; }
    public short BusinessTax { get; set; }
    public short SpellPoint { get; set; }
    public int ElfkingTid { get; set; }
    public short HouseOpen { get; set; }
    public long FamilyFlag { get; set; }
    public int BeCreateTime { get; set; }
    public uint RecycledTime { get; set; }
    public short Recruit { get; set; }
    public string RecruitMessage { get; set; } = "";
    public int Honor { get; set; }
    public int MemberSize { get; set; }

    /// <summary>9 titres : [0]=member_title, [1]=leader_title,
    /// [2..8]=cadre_title_2..8 (vec&lt;str&gt; natif fam+0x80).</summary>
    public string[] Titles { get; } =
        Enumerable.Repeat("", TitleCount).ToArray();

    /// <summary>14 privilèges dans l'ordre du SELECT natif :
    /// channel, invite, kickout, editinfo, setcadre, invite25r,
    /// fixequip, msgboard, delmsg, treasury, repair,
    /// storage01..03 (vec&lt;i32&gt; natif fam+0x8c).</summary>
    public int[] Privileges { get; } = new int[PrivilegeCount];

    /// <summary>Membres en ligne de la famille (charId → résumé) —
    /// <c>map&lt;int, SCharacter*&gt;</c> natif fam+0x6c/0x74.</summary>
    public Dictionary<int, FamilyMemberSummary> Members { get; } = new();

    /// <summary>Messages du board <c>family_msgboard</c>
    /// (map&lt;message_id, SMessageBoard&gt; natif).</summary>
    public SortedDictionary<int, FamilyMessage> Messages { get; } = new();

    /// <summary>Succès famille <c>family_achievement</c> : clé =
    /// achievement_type natif (GroupKey du succès), valeur = points
    /// cumulés. Map <c>fam+0xb4</c> (monde) / <c>fam+0x14c</c> (mission).</summary>
    public SortedDictionary<short, uint> Achievements { get; } = new();

    /// <summary>Compteurs dual-key <c>fam+0x160</c> : chaque point
    /// gagné s'ajoute sous la clé GroupKey <b>et</b> sous la clé Type —
    /// les colonnes de <c>family_achievement_rank</c> en sont un
    /// snapshot (1, 5, 0x65-0x6c, 0x1f5-0x1fa).</summary>
    public Dictionary<int, long> Counters { get; } = new();

    /// <summary>Total succès famille — <c>fam+0x4c</c> natif
    /// (cap 999999999), snapshot <c>family_achievement_rank.total_point</c>.</summary>
    public uint AchievementTotal { get; set; }

    /// <summary>Journal <c>family_log</c>.</summary>
    public List<FamilyLogEntry> Logs { get; } = new();

    /// <summary>Blob d'emblème custom <c>family_emblem.emblem</c>
    /// (vec&lt;u8&gt; natif fam+0xd8), null si absent.</summary>
    public byte[]? EmblemData { get; set; }

    /// <summary>Timestamp <c>family_emblem.time</c>.</summary>
    public int EmblemTime { get; set; }
}

/// <summary>
/// Résumé membre = <c>SCharacter</c> monde (chargé depuis
/// <c>player_characters</c> + <c>family_cadre_members</c>). Les champs
/// suivent les offsets natifs utilisés par
/// <c>CNC_CW_ClientQueryFamilyInfo::Serialize</c> (type 8) et
/// <c>CNC_CW_ClientUpdateFamilyMember</c> (46 — Serialize @ world
/// 0x80635d0 : id,str,lv,node,race,class,classlv,online,u16,gender,u16,
/// i32,last_saving,self_comment,family_comment,add_family,contribution).
/// </summary>
public sealed class FamilyMemberSummary
{
    public int CharacterId { get; set; }          // +0x00
    public string Name { get; set; } = "";        // +0x04 given_name
    public short Level { get; set; }              // +0x0c
    public short NodeId { get; set; }             // +0x0e
    public short RaceId { get; set; }             // +0x10
    public short ClassId { get; set; }            // +0x12
    public short ClassLevel { get; set; }         // +0x14 class_lv
    public short Privilege { get; set; }          // +0x16
    public bool Online { get; set; }              // +0x18
    public byte Gender { get; set; }              // +0x19 gender_id
    public ushort Field1a { get; set; }           // +0x1a (non prouvé)
    public ushort Field1c { get; set; }           // +0x1c (non prouvé)
    public int Field20 { get; set; }              // +0x20 (non prouvé)
    public int AddFamilyTime { get; set; }        // +0x60 add_family_time
    public int CreateTime { get; set; }           // +0x64 create_time
    public int LastSavingTime { get; set; }       // +0x68
    public string SelfComment { get; set; } = ""; // +0x6c self_comment
    public string FamilyComment { get; set; } = "";// +0x70 family_comment
    public int MsgBoardState { get; set; }        // +0x74
    public int ContributionPoint { get; set; }    // +0x78

    /// <summary>Rang dans la famille — 0 membre, 1..7 cadre
    /// (<c>family_cadre_members.rank</c>), le chef est
    /// <c>family.leader_id</c>.</summary>
    public short Rank { get; set; }
}

/// <summary>Entrée <c>family_msgboard</c> — le NCID 51
/// <c>ClientFamilySchedule</c> natif porte ces messages
/// (<c>family_schedule</c> est une table morte : aucun écrivain dans
/// les binaires).</summary>
public sealed class FamilyMessage
{
    public int MessageId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int CreateDate { get; set; }
}

/// <summary>Entrée <c>family_log</c> — record natif
/// {i32,s16,i32,i32,str,str} des 3 maps DBA.</summary>
public sealed class FamilyLogEntry
{
    public int LogId { get; set; }
    public short Type { get; set; }
    public int Date { get; set; }
    public int Para { get; set; }
    public string Str1 { get; set; } = "";
    public string Str2 { get; set; } = "";
}

/// <summary>Bâtiment de hall de famille — ligne
/// <c>family_building_container</c> native. Mapping prouvé par
/// <c>CNC_DBA_ClientFamilyContainerData::Serialize</c> (mission
/// @0x8113a00) apparié à <c>CNE_DBA_ClientFamilyContainerData::
/// Deserialize</c> (zone @0x82eda40) et <c>CFamilyDBO::
/// LoadFamilyBuilding</c> (mission @0x817c2b0) :
/// <list type="bullet">
/// <item>mission <c>CFamilyItem</c> : +8=id, +0xc=item_tid(u16),
/// +0xe=durability(s16), +0x30=due_date(u32), +0x84=tid(i32),
/// +0x88=item_type(s16), +0x8c/+0x90/+0x94=x/y/face_dir(f32),
/// +0x98=level(s16), +0x9c=exp(u32) ; clés = container_index/loc.</item>
/// <item>zone <c>CFamilyItem</c> (clone) : +4=id, +0xc=durability,
/// +0x18=due_date, +0x80=tid, +0x84=item_tid, +0x86=item_type,
/// +0x88/+0x8c/+0x90=x/y/face_dir, +0x94=level, +0x98=exp.</item>
/// </list></summary>
public sealed class FamilyBuildingItem
{
    /// <summary>Uid de l'item-bâtiment (colonne id — mission +8).</summary>
    public int Id { get; set; }
    /// <summary>Template d'item de l'objet placé (item_tid —
    /// mission +0xc u16 ; émis i32 dans le 487).</summary>
    public short ItemTid { get; set; }
    /// <summary>Template du bâtiment (tid — table S_Building ;
    /// mission +0x84 i32 ; clé de lookup <c>Ins+0x10c</c> natif).</summary>
    public int Tid { get; set; }
    /// <summary>Type d'item (item_type — mission +0x88 s16).</summary>
    public short ItemType { get; set; }
    /// <summary>Durabilité courante (mission +0xe s16).</summary>
    public short Durability { get; set; }
    /// <summary>Index de conteneur (0 = bâtiments du hall — clé de la
    /// map externe <c>SFamily+0x7c</c> natif).</summary>
    public short ContainerIndex { get; set; }
    /// <summary>Emplacement dans le conteneur (loc — clé de la map
    /// interne natif, émis s16 dans le 487).</summary>
    public short Loc { get; set; }
    /// <summary>Position X dans le hall (mission +0x8c f32).</summary>
    public float X { get; set; }
    /// <summary>Position Y dans le hall (mission +0x90 f32).</summary>
    public float Y { get; set; }
    /// <summary>Orientation (face_dir — mission +0x94 f32).</summary>
    public float FaceDir { get; set; }
    /// <summary>Niveau du bâtiment (mission +0x98 s16).</summary>
    public short Level { get; set; }
    /// <summary>Expérience cumulée du bâtiment (mission +0x9c u32).</summary>
    public int Exp { get; set; }
    /// <summary>Échéance epoch (due_date — mission +0x30 u32,
    /// 0 = permanent).</summary>
    public int DueDate { get; set; }
}
