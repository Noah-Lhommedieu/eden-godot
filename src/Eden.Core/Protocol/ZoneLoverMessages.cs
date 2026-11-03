using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages « couple / mariage » — lot Vague 2.
//
// Sources de preuve :
//   - ZoneMessages.g.cs serveur : CNC_CZ_ClientLoverInvite (475),
//     ClientLoverUpdate (476), ClientLoverInfo (477),
//     ClientLoverBreak (478), ClientLoverPosition (479),
//     ClientMarryOperation (597) ; CNE_CZ_ZoneServerLoverInvite (154),
//     LoverMessage (155), QueryLoverInfo (156), LoverBreak (157),
//     MarryOperation (249), WeddingInfoQuery (251).
//   - $EDEN/server/src/Eden.Zone/Codecs/WeddingPackets.cs —
//     599 ClientWeddingsInfo, boucle réelle relue byte-à-byte
//     (CNC_ZMis_ZoneServerWeddingsInfo::Serialize mission @0x80db220,
//     CNE_ZMis Execute @0x833ff00) — la classe générée
//     « [u8][i32][str][str][u8][i32] » est la version aplatie.
//   - $EDEN/server/src/Eden.Zone/SocialHandlers.cs — handlers
//     154/155/156/157/249/251 (les réponses 475-479 ne sont pas
//     émises par le serveur C# actuel — lacune documentée ; 597
//     et 599 le sont).
//   - docs/generated/ncid_map.json pour les numéros NCID.

// ------------------------- S→C ------------------------------------

/// <summary>S→C NCID 475 — ClientLoverInvite : un joueur propose un
/// couple. <c>[i32 charId][str name][s16]</c> — popup d'acceptation
/// côté natif (l'acceptation renvoie le même 154 mutuellement).</summary>
public sealed class ZoneLoverInvite : NetMessage
{
    public override int Opcode => 475;
    public override string Name => "Zone.ClientLoverInvite";

    /// <summary>i32 — charId du demandeur.</summary>
    public int CharId { get; set; }
    /// <summary>str — nom du demandeur (renommé <c>PeerName</c> :
    /// <c>Name</c> est déjà le label du message dans
    /// <see cref="NetMessage"/>).</summary>
    public string PeerName { get; set; } = "";
    /// <summary>s16 — champ natif (type/flag).</summary>
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteString(PeerName);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        PeerName = s.ReadString();
        Field3 = s.ReadShort();
    }
}

/// <summary>S→C NCID 476 — ClientLoverUpdate : mise à jour du lien
/// couple. <c>[s16][i32][str]</c>.</summary>
public sealed class ZoneLoverUpdate : NetMessage
{
    public override int Opcode => 476;
    public override string Name => "Zone.ClientLoverUpdate";

    /// <summary>s16 — type d'update natif.</summary>
    public short Type { get; set; }
    /// <summary>i32 — valeur (charId/statut).</summary>
    public int Value { get; set; }
    /// <summary>str — texte associé (nom).</summary>
    public string Text { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Type);
        s.WriteInt(Value);
        s.WriteString(Text);
    }

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadShort();
        Value = s.ReadInt();
        Text = s.ReadString();
    }
}

/// <summary>S→C NCID 477 — ClientLoverInfo : fiche complète du
/// partenaire (14 champs natifs :
/// <c>[i32][i32][s16][str][str][i32][i32][s16][s16][s16][s16][str]
/// [s16][u8]</c> — charId, loveId, classe, noms, dates/affection et
/// drapeaux ; sémantique par champ non résolue côté serveur →
/// exposés bruts).</summary>
public sealed class ZoneLoverInfo : NetMessage
{
    public override int Opcode => 477;
    public override string Name => "Zone.ClientLoverInfo";

    /// <summary>i32 — champ 1 (charId partenaire).</summary>
    public int Field1 { get; set; }
    /// <summary>i32 — champ 2 (loveId).</summary>
    public int Field2 { get; set; }
    /// <summary>s16 — champ 3 (classe/race).</summary>
    public short Field3 { get; set; }
    /// <summary>str — champ 4 (nom partenaire).</summary>
    public string Field4 { get; set; } = "";
    /// <summary>str — champ 5 (titre/étiquette).</summary>
    public string Field5 { get; set; } = "";
    /// <summary>i32 — champ 6 (date/affection).</summary>
    public int Field6 { get; set; }
    /// <summary>i32 — champ 7 (date/affection).</summary>
    public int Field7 { get; set; }
    /// <summary>s16 — champ 8.</summary>
    public short Field8 { get; set; }
    /// <summary>s16 — champ 9.</summary>
    public short Field9 { get; set; }
    /// <summary>s16 — champ 10.</summary>
    public short Field10 { get; set; }
    /// <summary>s16 — champ 11.</summary>
    public short Field11 { get; set; }
    /// <summary>str — champ 12 (message couple).</summary>
    public string Field12 { get; set; } = "";
    /// <summary>s16 — champ 13.</summary>
    public short Field13 { get; set; }
    /// <summary>u8 — champ 14 (flag mariage/en ligne).</summary>
    public byte Field14 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteString(Field4);
        s.WriteString(Field5);
        s.WriteInt(Field6);
        s.WriteInt(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteString(Field12);
        s.WriteShort(Field13);
        s.WriteByte(Field14);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadString();
        Field5 = s.ReadString();
        Field6 = s.ReadInt();
        Field7 = s.ReadInt();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadString();
        Field13 = s.ReadShort();
        Field14 = s.ReadByte();
    }
}

/// <summary>S→C NCID 478 — ClientLoverBreak : fin du couple.
/// <c>[i32]</c>.</summary>
public sealed class ZoneLoverBreak : NetMessage
{
    public override int Opcode => 478;
    public override string Name => "Zone.ClientLoverBreak";

    /// <summary>i32 — valeur native (loveId/charId).</summary>
    public int Value { get; set; }

    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) =>
        Value = s.ReadInt();
}

/// <summary>S→C NCID 479 — ClientLoverPosition : position du
/// partenaire (marqueur). <c>[i32 charId][s16 sceneId][f32 x][f32 y]</c>.</summary>
public sealed class ZoneLoverPosition : NetMessage
{
    public override int Opcode => 479;
    public override string Name => "Zone.ClientLoverPosition";

    /// <summary>i32 — charId partenaire.</summary>
    public int CharId { get; set; }
    /// <summary>s16 — id de scène.</summary>
    public short SceneId { get; set; }
    /// <summary>f32 — position X monde.</summary>
    public float X { get; set; }
    /// <summary>f32 — position Y monde (Z dans le repère Eden).</summary>
    public float Y { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteShort(SceneId);
        s.WriteFloat(X);
        s.WriteFloat(Y);
    }

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        SceneId = s.ReadShort();
        X = s.ReadFloat();
        Y = s.ReadFloat();
    }
}

/// <summary>S→C NCID 597 — ClientMarryOperation : résultat d'une op
/// de mariage (249) — <c>[s16]</c> (op1 = cérémonie enregistrée).</summary>
public sealed class ZoneMarryOperation : NetMessage
{
    public override int Opcode => 597;
    public override string Name => "Zone.ClientMarryOperation";

    /// <summary>s16 — code résultat natif.</summary>
    public short Value { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(Value);
    public override void Deserialize(BinReader s) =>
        Value = s.ReadShort();
}

/// <summary>S→C NCID 599 — ClientWeddingsInfo : liste des cérémonies
/// programmées. Corps <c>[u8 n][n×(i32 loveId][str name1][str name2]
/// [u8 status][i32 leftTime)]</c> — leftTime = secondes restantes de
/// l'étape (remaining − (now − startTime), WeddingPackets.cs).</summary>
public sealed class ZoneWeddingsInfo : NetMessage
{
    public override int Opcode => 599;
    public override string Name => "Zone.ClientWeddingsInfo";

    /// <summary>Une cérémonie programmée.</summary>
    public sealed class Entry
    {
        /// <summary>i32 — id du couple.</summary>
        public int LoveId { get; set; }
        /// <summary>str — nom du 1er partenaire.</summary>
        public string Name1 { get; set; } = "";
        /// <summary>str — nom du 2e partenaire.</summary>
        public string Name2 { get; set; } = "";
        /// <summary>u8 — étape de la cérémonie.</summary>
        public byte Status { get; set; }
        /// <summary>i32 — secondes restantes avant fin d'étape.</summary>
        public int LeftTime { get; set; }
    }

    /// <summary>Cérémonies (u8 n).</summary>
    public List<Entry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte((byte)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteInt(e.LoveId);
            s.WriteString(e.Name1);
            s.WriteString(e.Name2);
            s.WriteByte(e.Status);
            s.WriteInt(e.LeftTime);
        }
    }

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadByte();
        for (var i = 0; i < n; i++)
        {
            Entries.Add(new Entry
            {
                LoveId = s.ReadInt(),
                Name1 = s.ReadString(),
                Name2 = s.ReadString(),
                Status = s.ReadByte(),
                LeftTime = s.ReadInt(),
            });
        }
    }
}

// ------------------------- C→S ------------------------------------

/// <summary>C→S NCID 154 — ZoneServerLoverInvite : propose un couple
/// à un joueur (le couple se forme quand les deux se proposent
/// mutuellement — LoverService.Propose/AcceptAsync).
/// <c>[u32 charId][str name][s16]</c>.</summary>
public sealed class ZoneLoverInviteRequest : NetMessage
{
    public override int Opcode => 154;
    public override string Name => "Zone.ServerLoverInvite";

    /// <summary>u32 — charId du visé.</summary>
    public uint CharId { get; set; }
    /// <summary>str — nom du visé (<c>PeerName</c> — cf. 475).</summary>
    public string PeerName { get; set; } = "";
    /// <summary>s16 — champ natif (observé 0).</summary>
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteUInt(CharId);
        s.WriteString(PeerName);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        PeerName = s.ReadString();
        Field3 = s.ReadShort();
    }
}

/// <summary>C→S NCID 155 — ZoneServerLoverMessage : message/alias de
/// couple. <c>[str]</c>.</summary>
public sealed class ZoneLoverMessage : NetMessage
{
    public override int Opcode => 155;
    public override string Name => "Zone.ServerLoverMessage";

    /// <summary>str — texte du message.</summary>
    public string Value { get; set; } = "";

    public override void Serialize(BinWriter s) => s.WriteString(Value);
    public override void Deserialize(BinReader s) =>
        Value = s.ReadString();
}

/// <summary>C→S NCID 156 — ZoneServerQueryLoverInfo : requête la
/// fiche couple → 477 (jamais émise par le serveur C# actuel —
/// SocialHandlers log seulement). Corps vide.</summary>
public sealed class ZoneQueryLoverInfo : NetMessage
{
    public override int Opcode => 156;
    public override string Name => "Zone.ServerQueryLoverInfo";

    public override void Serialize(BinWriter s) { /* corps vide */ }
    public override void Deserialize(BinReader s) { /* corps vide */ }
}

/// <summary>C→S NCID 157 — ZoneServerLoverBreak : rompt le couple.
/// Corps vide (BreakupAsync).</summary>
public sealed class ZoneLoverBreakRequest : NetMessage
{
    public override int Opcode => 157;
    public override string Name => "Zone.ServerLoverBreak";

    public override void Serialize(BinWriter s) { /* corps vide */ }
    public override void Deserialize(BinReader s) { /* corps vide */ }
}

/// <summary>C→S NCID 249 — ZoneServerMarryOperation : opération de
/// mariage <c>[s16 op][s16]</c> — op1 = enregistrer une cérémonie
/// (le serveur répond 597 au succès).</summary>
public sealed class ZoneMarryOpRequest : NetMessage
{
    public override int Opcode => 249;
    public override string Name => "Zone.ServerMarryOperation";

    /// <summary>s16 — op (1 = demande de mariage).</summary>
    public short Op { get; set; }
    /// <summary>s16 — champ natif.</summary>
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Op);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Op = s.ReadShort();
        Field2 = s.ReadShort();
    }
}

/// <summary>C→S NCID 251 — ZoneServerWeddingInfoQuery : requête la
/// liste des cérémonies → 599 (liste vide si mission déconnectée —
/// repli un-octet du serveur). Corps vide.</summary>
public sealed class ZoneWeddingInfoQuery : NetMessage
{
    public override int Opcode => 251;
    public override string Name => "Zone.ServerWeddingInfoQuery";

    public override void Serialize(BinWriter s) { /* corps vide */ }
    public override void Deserialize(BinReader s) { /* corps vide */ }
}

// ------------------------- état client ----------------------------

/// <summary>
/// État couple du joueur — alimenté par 475-479/597/599 et consommé
/// par <c>CoupleWindow</c>. Mutations thread principal uniquement.
/// </summary>
public sealed class LoverState
{
    /// <summary>Partenaire lié (0 = célibataire).</summary>
    public int PartnerCharId { get; private set; }
    /// <summary>Nom du partenaire (dernier 475/477).</summary>
    public string PartnerName { get; private set; } = "";
    /// <summary>LoveId du lien.</summary>
    public int LoveId { get; private set; }
    /// <summary>Marié (flag u8 du 477 / succès du 597 op1).</summary>
    public bool Married { get; private set; }
    /// <summary>Fiche brute du dernier 477 (sémantique par champ non
    /// résolue — affichage best-effort).</summary>
    public ZoneLoverInfo? Info { get; private set; }
    /// <summary>Dernier résultat d'op mariage (597).</summary>
    public int LastMarryResult { get; private set; } = -1;
    /// <summary>Cérémonies du dernier 599.</summary>
    public readonly List<ZoneWeddingsInfo.Entry> Weddings = new();
    /// <summary>Dernière position partenaire (479).</summary>
    public ZoneLoverPosition? PartnerPos { get; private set; }

    /// <summary>Émis après mutation (thread principal).</summary>
    public event Action? Changed;
    /// <summary>Émis à la réception d'un 475 (popup).</summary>
    public event Action<ZoneLoverInvite>? InviteReceived;

    /// <summary>475 : invite entrante → popup + trace nom.</summary>
    public void Apply(ZoneLoverInvite m)
    {
        InviteReceived?.Invoke(m);
        Changed?.Invoke();
    }

    /// <summary>476 : update du lien — sémantique du champ Type non
    /// résolue ; conserve la valeur+texte comme indice partenaire.</summary>
    public void Apply(ZoneLoverUpdate m)
    {
        if (m.Value != 0)
        {
            PartnerCharId = m.Value;
            if (m.Text.Length > 0) PartnerName = m.Text;
        }
        Changed?.Invoke();
    }

    /// <summary>477 : fiche complète du lien.</summary>
    public void Apply(ZoneLoverInfo m)
    {
        Info = m;
        PartnerCharId = m.Field1;
        LoveId = m.Field2;
        if (m.Field4.Length > 0) PartnerName = m.Field4;
        Married = m.Field14 != 0;
        Changed?.Invoke();
    }

    /// <summary>478 : rupture → état célibataire.</summary>
    public void Apply(ZoneLoverBreak m)
    {
        PartnerCharId = 0;
        PartnerName = "";
        LoveId = 0;
        Married = false;
        Info = null;
        PartnerPos = null;
        Changed?.Invoke();
    }

    /// <summary>479 : position partenaire (marqueur carte).</summary>
    public void Apply(ZoneLoverPosition m)
    {
        PartnerPos = m;
        Changed?.Invoke();
    }

    /// <summary>597 : résultat d'op mariage (1 = succès → marié).</summary>
    public void Apply(ZoneMarryOperation m)
    {
        LastMarryResult = m.Value;
        if (m.Value == 1) Married = true;
        Changed?.Invoke();
    }

    /// <summary>599 : liste des cérémonies programmées.</summary>
    public void Apply(ZoneWeddingsInfo m)
    {
        Weddings.Clear();
        Weddings.AddRange(m.Entries);
        Changed?.Invoke();
    }
}
