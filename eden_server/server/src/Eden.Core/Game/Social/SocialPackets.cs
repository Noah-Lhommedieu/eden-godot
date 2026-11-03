using Eden.Core.Networking;

namespace Eden.Core.Game.Social;

/// <summary>
/// Codecs des listes sociales — formats fil reversés, pas les classes
/// générées (celles-ci sont « plates » : un seul record, sans le
/// count préfixé que le natif répète).
///
/// Preuves :
/// - Amis : <c>CNC_CW_ClientUpdateFriendMember::Serialize</c>
///   (re/ghidra/world/decompiled.c @0x80cc120) — NCID CW 52.
///   `[u16 worldServerId][s16 count][count × record]`, chunké à 10
///   par <c>CGlobalInfo::QueryFriendData</c> (mission @0x8090200).
/// - Recherche : <c>CNC_CZ_ClientSearchInfo::Serialize</c>
///   (zone @0x8134fc0) — NCID CZ 411. `[s16 header=0][s16 count]
///   [count × record]` ; header toujours 0 (piVar4[1]=0 dans
///   <c>CNE_ZMis_ZoneServerSearchInfo::Execute</c> @0x8334e40).
/// </summary>
public static class SocialPackets
{
    /// <summary>NCID monde → client de la liste d'amis.</summary>
    public const ushort NcidClientUpdateFriendMember = 52;
    /// <summary>NCID zone → client du résultat de recherche.</summary>
    public const ushort NcidClientSearchInfo = 411;
    /// <summary>NCID monde → client de l'invitation d'ami.</summary>
    public const ushort NcidClientFriendInvitation = 53;
    /// <summary>NCID monde → client du résultat « entraide »
    /// (<c>CNC_CW_ClientMutuallyHelpUpdate</c>, désasm @0x8160…).</summary>
    public const ushort NcidClientMutuallyHelpUpdate = 81;
    /// <summary>NCID monde → client de la blacklist
    /// (<c>CNC_CW_ClientUpdateBlackList</c>, désasm @0x80f…100921).</summary>
    public const ushort NcidClientUpdateBlackList = 61;

    /// <summary>Corps du paquet 61 — <c>[u8 flag][s16 n][n×{i32 id,
    /// str name}]</c> (Serialize natif : flag this+0x1c puis count de la
    /// map interne ; le monde le reçoit de la mission avec flag=1).</summary>
    public static byte[] BuildBlackList(
        IReadOnlyList<(int CharId, string Name)> entries)
    {
        var stream = new OutBinStream();
        stream.WriteByte(1);                      // u8 flag (ctor natif=true)
        stream.WriteShort((short)entries.Count);  // s16 count
        foreach (var (id, name) in entries)
        {
            stream.WriteInt(id);                  // i32 charId bloqué
            stream.WriteString(name);             // str given_name
        }
        return stream.ToArray();
    }

    /// <summary>Nombre max de records par paquet 52 — chunk natif
    /// (<c>9 &lt; count</c> → flush dans QueryFriendData).</summary>
    public const int FriendChunkSize = 10;
    /// <summary>Cap natif des résultats de recherche
    /// (<c>0x31 &lt; count → break</c> dans TC_SearchCondition).</summary>
    public const int SearchResultCap = 50;

    /// <summary>
    /// Un ami tel que sérialisé dans le record du paquet 52 —
    /// <b>13 champs</b> (Serialize natif : i32, str, 6×s16, str, u8,
    /// u16, u8, u8). Les champs « détail » (level…family) valent −1/""
    /// quand la relation n'est pas mutuelle ou que la carte est absente.
    /// </summary>
    public sealed record FriendEntry(
        int CharId,       // charId de l'ami (i32)
        string Name,      // given_name (str)
        short FriendType, // 0=blacklist 1=ajouté 2=m'a-ajouté 3=mutuel
        short Level,      // player_characters.level (exec+0x5c)
        short NodeId,     // player_characters.node_id (exec+0x30)
        short RaceId,     // player_characters.race_id (exec+0x3a)
        short ClassId,    // player_characters.class_id (exec+0x38)
        short ClassLv,    // player_characters.class_lv (exec+0x68)
        string FamilyName,// guilds.name via family_id (exec+0xb0→map)
        byte Online,      // state != 0xff → 1/0 (dérivé par le relais CW)
        ushort ServerId,  // exec+6 : serveur de l'ami, 0xffff offline
        byte State,       // exec+0x604 : état brut — 0xff absent/offline
        byte IsPublic);   // player_card.is_public (défaut 1)

    /// <summary>
    /// Une entrée du résultat de recherche (paquet 411).
    /// Ordre fil natif : charId, name, level, class_id (u16),
    /// class_lv, node_id, is_public.
    /// </summary>
    public sealed record SearchEntry(
        int CharId,      // exec+8 → charId (i32) — les joueurs sont
                         // adressés par charId, pas de sight-handle
        string Name,     // zone char+0x324 (str)
        short Level,     // zone char+0x470 (s16)
        ushort ClassId,  // zone char+0x47c (byte → écrit u16)
        short ClassLv,   // zone char+0x47e (s16)
        short NodeId,    // *(char+0x1c)+0x28 +4 = node courant (s16)
        byte IsPublic);  // *(char+0xb64)+0x18 = carte publique (u8)

    /// <summary>
    /// Construit les corps des paquets 52 — un corps par tranche de
    /// <see cref="FriendChunkSize"/> amis, comme le natif.
    /// </summary>
    public static IEnumerable<byte[]> BuildFriendMemberPackets(
        ushort worldServerId, IReadOnlyList<FriendEntry> friends)
    {
        for (var offset = 0; offset < friends.Count || offset == 0;
             offset += FriendChunkSize)
        {
            var stream = new OutBinStream();
            // u16 : CGlobalInfo::WorldServerID (constante serveur, =1 ici)
            stream.WriteShort((short)worldServerId);
            var take = Math.Min(FriendChunkSize, friends.Count - offset);
            // s16 : nombre de records dans CE paquet
            stream.WriteShort((short)Math.Max(take, 0));
            for (var i = 0; i < take; i++)
                WriteFriendEntry(stream, friends[offset + i]);
            yield return stream.ToArray();
            if (offset + take >= friends.Count) yield break;
        }
    }

    /// <summary>
    /// Une entrée du paquet 81 entraide — ordre fil natif lu dans
    /// <c>CNC_CW_ClientMutuallyHelpUpdate::Serialize</c> (désasm world
    /// @0x8160…, 8 vecteurs parallèles) et les offsets de poussée dans
    /// <c>World_TEC_MutuallyHelpSearch</c> (mission @0x8251b90).
    /// </summary>
    public sealed record MutuallyHelpEntry(
        int CharId,      // exec+8  → u32 charId
        string Name,     // exec+0xc → str given_name
        short Level,     // exec+0x5c → s16 level (la clé de secteur)
        byte ClassId,    // exec+0x38 → u8 class_id (octet bas du s16)
        byte ClassLv,    // exec+0x68 → u8 class_lv
        short NodeId,    // exec+0x30 → s16 node courant (colonne lieu)
        short WorldId,   // exec+6   → s16 channel/monde (colonne 分流)
        byte IsPublic);  // *(exec+0x120)+0x18 → u8 carte publique

    /// <summary>Corps du paquet 81 : <c>[s16 count][count × record]</c>
    /// — Serialize natif dérive le count de la taille des vecteurs.</summary>
    public static byte[] BuildMutuallyHelpUpdate(
        IReadOnlyList<MutuallyHelpEntry> entries)
    {
        var stream = new OutBinStream();
        // s16 — nombre de records (taille du vecteur +0x10 natif).
        stream.WriteShort((short)entries.Count);
        foreach (var e in entries)
        {
            stream.WriteInt(e.CharId);        // u32 charId
            stream.WriteString(e.Name);       // str given_name
            stream.WriteShort(e.Level);       // s16 level
            stream.WriteByte(e.ClassId);      // u8 class_id
            stream.WriteByte(e.ClassLv);      // u8 class_lv
            stream.WriteShort(e.NodeId);      // s16 node_id
            stream.WriteShort(e.WorldId);     // s16 world/channel
            stream.WriteByte(e.IsPublic);     // u8 carte publique
        }
        return stream.ToArray();
    }

    /// <summary>Corps du paquet 411 : header s16 = 0 + count + records.</summary>
    public static byte[] BuildSearchInfo(IReadOnlyList<SearchEntry> entries)
    {
        var stream = new OutBinStream();
        stream.WriteShort(0);                          // s16 header : toujours 0 (piVar4[1]=0)
        stream.WriteShort((short)entries.Count);       // s16 count
        foreach (var e in entries)
        {
            stream.WriteInt(e.CharId);                 // i32 charId
            stream.WriteString(e.Name);                // str given_name
            stream.WriteShort(e.Level);                // s16 level
            stream.WriteShort((short)e.ClassId);       // u16 class_id
            stream.WriteShort(e.ClassLv);              // s16 class_lv
            stream.WriteShort(e.NodeId);               // s16 node_id
            stream.WriteByte(e.IsPublic);              // u8 is_public
        }
        return stream.ToArray();
    }

    private static void WriteFriendEntry(OutBinStream stream, FriendEntry e)
    {
        stream.WriteInt(e.CharId);          // i32 charId
        stream.WriteString(e.Name);         // str given_name
        stream.WriteShort(e.FriendType);    // s16 friend_type (3 mutuel)
        stream.WriteShort(e.Level);         // s16 level (−1 si non-visible)
        stream.WriteShort(e.NodeId);        // s16 node_id
        stream.WriteShort(e.RaceId);        // s16 race_id
        stream.WriteShort(e.ClassId);       // s16 class_id
        stream.WriteShort(e.ClassLv);       // s16 class_lv
        stream.WriteString(e.FamilyName);   // str nom de famille/guilde
        stream.WriteByte(e.Online);         // u8 online (dérivé state!=0xff)
        stream.WriteShort((short)e.ServerId);// u16 serverId, 0xffff offline
        // u8 state brut (exec+0x604 → u8) — le client l'utilise pour
        // l'icône d'état ; 0xff = absent/offline (natif).
        stream.WriteByte(e.State);
        stream.WriteByte(e.IsPublic);       // u8 is_public
    }
}
