using EdenClient.Data;
using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages zone d'inventaire — déplacements, équipement, split,
// usage d'items (lot S2 du plan de secours).
//
// Sources de preuve (toutes côté serveur, lecture seule) :
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs — layouts CNE_CZ_*
//     (7 SwitchItemLocation, 6 DropItem, 19 ItemUse, 84 DivideItem)
//     et CNC_CZ_* (277 ack, 275 drop, 291 durability, 303 use-ack) ;
//   - $EDEN/server/src/Eden.Zone/Handlers/InventoryHandlers.cs —
//     sémantique des conteneurs (désasm CPlayerFactory::
//     SwitchItemLocation @0x81237f0 / DropItem, CItemFactory::
//     DivideItem @0x809ff30) ;
//   - $EDEN/server/src/Eden.Zone/Handlers/ItemActionHandlers.cs —
//     sémantique ItemUse (désasm Execute @0x8260490, corps live 17B :
//     le str cible est préfixé u8, PAS u16) ;
//   - $EDEN/server/src/Eden.Core/Game/Inventory/NativeEquipmentSlots.cs
//     — table type→loc (désasm CItemFactory::Equip @0x80b5810) ;
//   - $EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs —
//     BuildPutItem (274, désasm PutIntoContainer @0x809ea00).

// ---------- C→S ----------

/// <summary>C→S NCID 7 — SwitchItemLocation : déplace/équipe/déséquipe
/// un item. Wire : `[s16 srcType][s16 srcSlot][s16 srcIdx]
/// [s16 dstType][s16 dstSlot][s16 dstIdx]`. Types natifs : 0 = sacs
/// (idx = n° de sac), 1 = équipement (idx = groupe, slot = loc) ;
/// −1 dans dstSlot/dstIdx = résolution serveur (premier libre ou loc
/// canonique de l'item). Réponse 277 <see cref="ZoneSwitchItemLocationOk"/>.
/// </summary>
public sealed class ZoneSwitchItemLocation : NetMessage
{
    public override int Opcode => 7;
    public override string Name => "Zone.ZoneServerSwitchItemLocation";
    /// <summary>s16 — type conteneur source (0 sac, 1 équipement).</summary>
    public short SrcType { get; set; }
    /// <summary>s16 — loc source dans le conteneur.</summary>
    public short SrcSlot { get; set; }
    /// <summary>s16 — index du conteneur source (n° de sac / groupe).</summary>
    public short SrcIndex { get; set; }
    /// <summary>s16 — type conteneur destination.</summary>
    public short DstType { get; set; }
    /// <summary>s16 — loc destination (−1 = auto).</summary>
    public short DstSlot { get; set; }
    /// <summary>s16 — index du conteneur destination (−1 = auto).</summary>
    public short DstIndex { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SrcType);         // s16 — type conteneur src
        s.WriteShort(SrcSlot);         // s16 — loc src
        s.WriteShort(SrcIndex);        // s16 — index conteneur src
        s.WriteShort(DstType);         // s16 — type conteneur dst
        s.WriteShort(DstSlot);         // s16 — loc dst (−1 auto)
        s.WriteShort(DstIndex);        // s16 — index conteneur dst
    }
    public override void Deserialize(BinReader s)
    {
        SrcType = s.ReadShort();
        SrcSlot = s.ReadShort();
        SrcIndex = s.ReadShort();
        DstType = s.ReadShort();
        DstSlot = s.ReadShort();
        DstIndex = s.ReadShort();
    }
}

/// <summary>C→S NCID 6 — DropItem : détruit l'item à (contType, loc,
/// contIdx) — <c>CItemFactory::EradicateItem</c> natif (désasm
/// CPlayerFactory::DropItem zone/decompiled.c:141315), PAS de drop
/// au sol. Wire : `[s16 contType][s16 loc][s16 contIdx]` ; routage
/// serveur identique à SwitchItemLocation (0 sacs, 1 équipement,
/// 4 stockage, 5 sacs spéciaux). Écho 275
/// <see cref="ZoneDropItem"/>.</summary>
public sealed class ZoneServerDropItem : NetMessage
{
    public override int Opcode => 6;
    public override string Name => "Zone.ZoneServerDropItem";
    /// <summary>s16 — type de conteneur (0 = sacs).</summary>
    public short ContainerType { get; set; }
    /// <summary>s16 — loc dans le conteneur.</summary>
    public short Slot { get; set; }
    /// <summary>s16 — index du conteneur (n° de sac).</summary>
    public short ContainerIndex { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ContainerType);      // s16 — contType
        s.WriteShort(Slot);               // s16 — loc
        s.WriteShort(ContainerIndex);     // s16 — contIdx (n° de sac)
    }
    public override void Deserialize(BinReader s)
    {
        ContainerType = s.ReadShort();
        Slot = s.ReadShort();
        ContainerIndex = s.ReadShort();
    }
}

/// <summary>C→S NCID 19 — ItemUse : utilise un item (consommable,
/// parchemin sur item cible, équipement au clic selon build). Wire
/// natif (corps capturé 17B) : `[s16 contType][s16 contIdx][s16 slot]
/// [i32 cible][u8len str param][s16 tgtCont][s16 tgtIdx][s16 tgtLoc]`.
/// contType : 0 = sacs, 1 = équipement. Les 3 derniers s16 portent la
/// cible (contType, contIdx, loc) pour les items use-type 3/5 —
/// −1/−1/−1 pour un usage simple. Field4 i32 = handle cible (−1 si
/// aucune). Réponse : 303 <see cref="ZoneClientItemUse"/> + effets.
/// </summary>
public sealed class ZoneItemUse : NetMessage
{
    public override int Opcode => 19;
    public override string Name => "Zone.ZoneServerItemUse";
    /// <summary>s16 — type de conteneur de l'item utilisé (0 sac).</summary>
    public short ContainerType { get; set; }
    /// <summary>s16 — index du conteneur (n° de sac).</summary>
    public short ContainerIndex { get; set; }
    /// <summary>s16 — loc dans le conteneur.</summary>
    public short Slot { get; set; }
    /// <summary>i32 — cible éventuelle (handle ; −1 = aucune).</summary>
    public int Target { get; set; } = -1;
    /// <summary>str préfixé u8 — paramètre (vide pour les potions).</summary>
    public string Param { get; set; } = "";
    /// <summary>s16 — conteneur de la cible item-sur-item (−1 sinon).</summary>
    public short TargetContainerType { get; set; } = -1;
    /// <summary>s16 — index conteneur cible (−1 sinon).</summary>
    public short TargetContainerIndex { get; set; } = -1;
    /// <summary>s16 — loc cible (−1 sinon).</summary>
    public short TargetLoc { get; set; } = -1;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ContainerType);      // s16 — contType
        s.WriteShort(ContainerIndex);     // s16 — contIdx (n° de sac)
        s.WriteShort(Slot);               // s16 — loc
        s.WriteInt(Target);               // i32 — cible handle
        // str param : préfixe u8 sur le wire live (corps 17B capturé —
        // un u16 désynchroniserait les 3 s16 de queue).
        var bytes = EdenEncoding.Big5.GetBytes(Param);
        s.WriteByte((byte)Math.Min(bytes.Length, 255));
        s.WriteBytes(bytes);
        s.WriteShort(TargetContainerType);   // s16 — cible contType
        s.WriteShort(TargetContainerIndex);  // s16 — cible contIdx
        s.WriteShort(TargetLoc);             // s16 — cible loc
    }
    public override void Deserialize(BinReader s)
    {
        ContainerType = s.ReadShort();
        ContainerIndex = s.ReadShort();
        Slot = s.ReadShort();
        Target = s.ReadInt();
        var len = s.ReadByte();
        Param = len == 0 ? "" : EdenEncoding.Big5.GetString(s.ReadBytes(len));
        TargetContainerType = s.ReadShort();
        TargetContainerIndex = s.ReadShort();
        TargetLoc = s.ReadShort();
    }
}

/// <summary>C→S NCID 84 — DivideItem : sépare un stack. Wire natif
/// (désasm CItemFactory::DivideItem @0x809ff30) :
/// `[s16 srcSlot][s16 srcSac][s16 dstSlot][s16 dstSac][s16 qté]
/// [s16 type]`. Notifications serveur : 291 (count source) + 274
/// (le nouveau stack). Field6 « type » inusité côté handler — 0.
/// </summary>
public sealed class ZoneDivideItem : NetMessage
{
    public override int Opcode => 84;
    public override string Name => "Zone.ZoneServerDivideItem";
    /// <summary>s16 — loc source dans le sac.</summary>
    public short SrcSlot { get; set; }
    /// <summary>s16 — sac source (container_index).</summary>
    public short SrcBag { get; set; }
    /// <summary>s16 — loc destination.</summary>
    public short DstSlot { get; set; }
    /// <summary>s16 — sac destination.</summary>
    public short DstBag { get; set; }
    /// <summary>s16 — quantité retirée du stack source.</summary>
    public short Count { get; set; }
    /// <summary>s16 — type (inusité côté handler — 0).</summary>
    public short Kind { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SrcSlot);
        s.WriteShort(SrcBag);
        s.WriteShort(DstSlot);
        s.WriteShort(DstBag);
        s.WriteShort(Count);
        s.WriteShort(Kind);
    }
    public override void Deserialize(BinReader s)
    {
        SrcSlot = s.ReadShort();
        SrcBag = s.ReadShort();
        DstSlot = s.ReadShort();
        DstBag = s.ReadShort();
        Count = s.ReadShort();
        Kind = s.ReadShort();
    }
}

// ---------- S→C ----------

/// <summary>S→C NCID 277 — ClientSwitchItemLocationOK : réponse au 7.
/// `[s16 srcType][s16 srcSlot][s16 srcIdx][s16 dstType][s16 dstSlot]
/// [s16 dstIdx][u8 résultat]` — ATTENTION : les champs dst sont les
/// valeurs RÉSOLUES par le serveur (−1 jamais écho — écho brut =
/// crash natif documenté dans InventoryHandlers). Résultat 1 = ok.
/// </summary>
public sealed class ZoneSwitchItemLocationOk : NetMessage
{
    public override int Opcode => 277;
    public override string Name => "Zone.ClientSwitchItemLocationOK";
    /// <summary>s16 — type conteneur source (écho).</summary>
    public short SrcType { get; set; }
    /// <summary>s16 — loc source (écho).</summary>
    public short SrcSlot { get; set; }
    /// <summary>s16 — index conteneur source (écho).</summary>
    public short SrcIndex { get; set; }
    /// <summary>s16 — type conteneur destination (écho).</summary>
    public short DstType { get; set; }
    /// <summary>s16 — loc destination RÉSOLUE.</summary>
    public short DstSlot { get; set; }
    /// <summary>s16 — index conteneur destination RÉSOLU.</summary>
    public short DstIndex { get; set; }
    /// <summary>u8 — résultat (1 = succès).</summary>
    public byte Result { get; set; }
    /// <summary>Succès du déplacement.</summary>
    public bool Ok => Result == 1;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SrcType); s.WriteShort(SrcSlot);
        s.WriteShort(SrcIndex); s.WriteShort(DstType);
        s.WriteShort(DstSlot); s.WriteShort(DstIndex);
        s.WriteByte(Result);
    }
    public override void Deserialize(BinReader s)
    {
        SrcType = s.ReadShort(); SrcSlot = s.ReadShort();
        SrcIndex = s.ReadShort(); DstType = s.ReadShort();
        DstSlot = s.ReadShort(); DstIndex = s.ReadShort();
        Result = s.ReadByte();
    }
}

/// <summary>Item ajouté par le 274 (vue HUD minimale) — record
/// complet lu par <see cref="NativeItemRecord.TryReadPutItem"/>
/// (mask marché à l'ordre WriteItem natif). Le codec 274 consommé à
/// l'usage est <see cref="ZonePutItem"/> (ZoneCombatMessages.cs).</summary>
public sealed record PutItemResult(int ItemId, int Bag, int Slot,
    int Durability, int MaxDurability, byte Flags, int Amount,
    int Strengthen);

/// <summary>S→C NCID 303 — ClientItemUse : accusé d'usage.
/// `[i32 charId][i32 0][u16 itemId][s16 contType][s16 contIdx]
/// [s16 slot][s16 résultat]` (dernier = 3 dans les émissions natives
/// observées — SendItemUseAckAsync serveur).</summary>
public sealed class ZoneClientItemUse : NetMessage
{
    public override int Opcode => 303;
    public override string Name => "Zone.ClientItemUse";
    /// <summary>i32 — charId du perso.</summary>
    public int CharId { get; set; }
    /// <summary>i32 — 0 natif.</summary>
    public int Zero { get; set; }
    /// <summary>u16 — id de l'item utilisé (0 en refus).</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — type de conteneur (écho).</summary>
    public short ContainerType { get; set; }
    /// <summary>s16 — index du conteneur (écho).</summary>
    public short ContainerIndex { get; set; }
    /// <summary>s16 — loc (écho).</summary>
    public short Slot { get; set; }
    /// <summary>s16 — code résultat (3 natif = succès).</summary>
    public short Result { get; set; }
    /// <summary>Vrai si l'usage a été accepté (itemId non nul).</summary>
    public bool Accepted => ItemId != 0;

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId); s.WriteInt(Zero);
        s.WriteShort((short)ItemId);
        s.WriteShort(ContainerType); s.WriteShort(ContainerIndex);
        s.WriteShort(Slot); s.WriteShort(Result);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); Zero = s.ReadInt();
        ItemId = (ushort)s.ReadShort();
        ContainerType = s.ReadShort(); ContainerIndex = s.ReadShort();
        Slot = s.ReadShort(); Result = s.ReadShort();
    }
}

// ---------- sémantique des locs d'équipement ----------

/// <summary>
/// Résolution EquipType → locs d'équipement — miroir client de
/// <c>$EDEN/server/src/Eden.Core/Game/Inventory/NativeEquipmentSlots.cs</c>
/// (désasm <c>CItemFactory::Equip</c> @0x80b5810 ;
/// <c>CCharacter::GetWeaponItem</c> @0x840cea0 — locs canoniques
/// 7 main / 8 off-hand / 10 distance).
/// EquipType = col9 de c_item.ini (<see cref="EdenClient.Data.Tables.
/// ItemTable.ItemDef.EquipLoc"/>).
/// </summary>
public static class ZoneItemActions
{
    /// <summary>Locs qu'un EquipType peut occuper — vide = non
    /// équipable. Table native exacte du switch Equip.</summary>
    public static int[] AllowedLocations(int equipType) =>
        equipType switch
        {
            >= 1 and <= 6 => [equipType],      // pièces d'armure
            7 or 19 => [7, 8],                 // 1H/dague, dual-wield
            8 => [8],                          // bouclier/off-hand
            9 => [7],                          // 2H → main
            10 => [11],                        // accessoire → loc 11
            11 or 20 => [12, 13],              // paire accessoire
            12 => [14, 15],                    // paire accessoire
            >= 13 and <= 18 => [equipType],
            35 => [10],                        // arc/arme longue
            _ => [],
        };

    /// <summary>Kinds équipables sur le personnage (conteneur 0) —
    /// miroir de <c>NativeEquipmentSlots.IsPlayerEquippableKind</c> :
    /// kinds 1-34 (armure/arme) et 37-40 (collier, anneau, boucle,
    /// licence).</summary>
    public static bool IsPlayerEquippableKind(int kind) =>
        kind is >= 1 and <= 34 or >= 37 and <= 40;

    /// <summary>Loc canonique que le client prévoit pour l'équipe
    /// (même priorité que le routage natif : premier loc autorisé
    /// libre, sinon le canonique). Utilisé pour le mask du C→S 8 et
    /// l'affichage optimiste.</summary>
    public static int PredictEquipLoc(int equipType,
        IReadOnlyDictionary<int, int>? worn)
    {
        var allowed = AllowedLocations(equipType);
        if (allowed.Length == 0) return -1;
        foreach (var loc in allowed)
            if (worn == null || !worn.ContainsKey(loc))
                return loc;
        return allowed[0];
    }

    /// <summary>Mask u32 des locs candidats (tous les locs autorisés —
    /// inclure les alternatives est sans danger : le 282 renvoie
    /// l'état complet de toute façon).</summary>
    public static uint LocMask(int equipType)
    {
        uint mask = 0;
        foreach (var loc in AllowedLocations(equipType))
            mask |= 1u << loc;
        return mask;
    }

    /// <summary>Construit la requête d'équipe (sac → équipement) :
    /// (0, slot, sac) → (1, locRésolu, groupe 0).</summary>
    public static ZoneSwitchItemLocation EquipRequest(int bag, int slot,
        int equipType, IReadOnlyDictionary<int, int>? worn)
    {
        var loc = PredictEquipLoc(equipType, worn);
        return new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = (short)slot, SrcIndex = (short)bag,
            DstType = 1, DstSlot = (short)loc, DstIndex = 0,
        };
    }

    /// <summary>Construit la requête de déséquipe (équipement → sac,
    /// premier emplacement libre : −1/−1 natif).</summary>
    public static ZoneSwitchItemLocation UnequipRequest(int loc,
        int group = 0)
    {
        return new ZoneSwitchItemLocation
        {
            SrcType = 1, SrcSlot = (short)loc, SrcIndex = (short)group,
            DstType = 0, DstSlot = -1, DstIndex = -1,
        };
    }

    /// <summary>Construit la requête de déplacement intra-sac.</summary>
    public static ZoneSwitchItemLocation MoveRequest(int srcBag,
        int srcSlot, int dstBag, int dstSlot)
    {
        return new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = (short)srcSlot, SrcIndex = (short)srcBag,
            DstType = 0, DstSlot = (short)dstSlot, DstIndex = (short)dstBag,
        };
    }

    /// <summary>Construit la requête « équiper un sac » (kind 43) :
    /// (0, slot, sac) → (5, loc 1-4, holder 0 = inventaire).</summary>
    public static ZoneSwitchItemLocation BagEquipRequest(int srcBag,
        int srcSlot, int holderLoc)
    {
        return new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = (short)srcSlot, SrcIndex = (short)srcBag,
            DstType = 5, DstSlot = (short)holderLoc, DstIndex = 0,
        };
    }

    /// <summary>Construit la requête « retirer un sac équipé » :
    /// (5, loc, holder 0) → (0, −1, −1 = premier emplacement libre).</summary>
    public static ZoneSwitchItemLocation BagUnequipRequest(int holderLoc)
    {
        return new ZoneSwitchItemLocation
        {
            SrcType = 5, SrcSlot = (short)holderLoc, SrcIndex = 0,
            DstType = 0, DstSlot = -1, DstIndex = -1,
        };
    }
}
