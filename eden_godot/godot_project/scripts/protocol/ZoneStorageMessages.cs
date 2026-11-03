using EdenClient.Net;

namespace EdenClient.Protocol;

// Coffre de famille + extension de banque perso — compléments du
// lot banque (vague 2). Formats prouvés côté serveur :
//
//   - $EDEN/server/src/Eden.Zone/Codecs/FamilyStoragePackets.cs —
//     émissions 504/505/506/508 désassemblées
//     (`lapis::CNC_CZ_ClientFamilyStorage*`, Serialize @0x08200xxx,
//     emit CFamilyStorageShop::OpenShop @0x08579440). Le record item
//     famille est à LAYOUT FIXE (aucun mask u24, contrairement au
//     record inventaire de <see cref="NativeItemRecord"/>).
//   - $EDEN/server/src/Eden.Zone/Handlers/InventoryHandlers.cs —
//     sémantique 177/178 [s16 coffre][s16 loc][s16 sac][s16 slot]
//     (−1 = auto) et 69/373 expansion (cnt<2, prix S_Parameter).
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs —
//     CNE_CZ_ZoneServerFamilyStoragePutItem (177) / GetItem (178) /
//     QueryLog (179), CNE_CZ_ZoneServerStorageExpand (69),
//     CNC_CZ_ClientStorageExpandResult (373),
//     CNC_CZ_ClientEnterFamilyStorage (507).
//
// Ouverture : service NPC type 19 « family_storage » → 507 puis un
// 508 par coffre NON vide (OpenShop natif). Pas d'or dans le coffre
// famille — la trésorerie est un flux guilde distinct (NCID 159).

// ---------- C→S ----------

/// <summary>C→S NCID 69 — StorageExpand : demande d'extension du
/// coffre perso (+20 slots, max 2 — <c>ExpandStorage</c> natif).
/// Corps vide ; réponse 373 + texte indexé.</summary>
public sealed class ZoneStorageExpand : NetMessage
{
    public override int Opcode => 69;
    public override string Name => "Zone.ZoneServerStorageExpand";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 177 — FamilyStoragePutItem : dépose TOUTE la
/// ligne du sac dans le coffre famille (pas de quantité — le stack
/// entier est déplacé, InventoryHandlers.PutItem).
/// <c>[s16 coffre 0-2][s16 loc (−1=auto)][s16 sac][s16 slot]</c>.</summary>
public sealed class ZoneFamilyStoragePut : NetMessage
{
    public override int Opcode => 177;
    public override string Name => "Zone.ZoneServerFamilyStoragePutItem";
    /// <summary>s16 — index du coffre famille (0-2).</summary>
    public short StorageIndex { get; set; }
    /// <summary>s16 — loc du coffre (−1 = premier libre).</summary>
    public short Loc { get; set; } = -1;
    /// <summary>s16 — sac source (container_index inventaire).</summary>
    public short Bag { get; set; }
    /// <summary>s16 — slot dans le sac.</summary>
    public short Slot { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(StorageIndex);   // s16 — index coffre
        s.WriteShort(Loc);            // s16 — loc (−1 = auto)
        s.WriteShort(Bag);            // s16 — sac source
        s.WriteShort(Slot);           // s16 — slot source
    }
    public override void Deserialize(BinReader s)
    {
        StorageIndex = s.ReadShort();
        Loc = s.ReadShort();
        Bag = s.ReadShort();
        Slot = s.ReadShort();
    }
}

/// <summary>C→S NCID 178 — FamilyStorageGetItem : retire la ligne
/// <paramref name="Loc"/> du coffre vers le sac (stack entier).
/// <c>[s16 coffre][s16 loc][s16 sac (−1→0)][s16 slot (−1=auto)]</c>.</summary>
public sealed class ZoneFamilyStorageGet : NetMessage
{
    public override int Opcode => 178;
    public override string Name => "Zone.ZoneServerFamilyStorageGetItem";
    /// <summary>s16 — index du coffre famille (0-2).</summary>
    public short StorageIndex { get; set; }
    /// <summary>s16 — loc du coffre à retirer.</summary>
    public short Loc { get; set; }
    /// <summary>s16 — sac d'arrivée (−1 → sac 0).</summary>
    public short Bag { get; set; } = -1;
    /// <summary>s16 — slot d'arrivée (−1 = premier libre).</summary>
    public short Slot { get; set; } = -1;
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(StorageIndex);   // s16 — index coffre
        s.WriteShort(Loc);            // s16 — loc à retirer
        s.WriteShort(Bag);            // s16 — sac (−1 → 0)
        s.WriteShort(Slot);           // s16 — slot (−1 = auto)
    }
    public override void Deserialize(BinReader s)
    {
        StorageIndex = s.ReadShort();
        Loc = s.ReadShort();
        Bag = s.ReadShort();
        Slot = s.ReadShort();
    }
}

/// <summary>C→S NCID 179 — FamilyStorageQueryLog : demande le
/// journal d'audit du coffre (réponse 506). Corps vide.</summary>
public sealed class ZoneFamilyStorageQueryLog : NetMessage
{
    public override int Opcode => 179;
    public override string Name => "Zone.ZoneServerFamilyStorageQueryLog";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

// ---------- S→C ----------

/// <summary>S→C NCID 373 — StorageExpandResult [s16 résultat] :
/// 0 = expansion faite, 1 = plafond (2 expansions max / table prix),
/// 2 = or insuffisant (le texte indexé détaille le motif).</summary>
public sealed class ZoneStorageExpandResult : NetMessage
{
    public override int Opcode => 373;
    public override string Name => "Zone.ClientStorageExpandResult";
    /// <summary>s16 — 0 succès, 1 cap refusé, 2 or insuffisant.</summary>
    public short Result { get; set; }
    public bool Ok => Result == 0;
    public override void Serialize(BinWriter s) => s.WriteShort(Result);
    public override void Deserialize(BinReader s) => Result = s.ReadShort();
}

/// <summary>S→C NCID 507 — EnterFamilyStorage [i32 npcUid] : ouvre
/// le coffre de famille ; les contenus suivent par des 508 (un par
/// coffre non vide — OpenShop natif).</summary>
public sealed class ZoneEnterFamilyStorage : NetMessage
{
    public override int Opcode => 507;
    public override string Name => "Zone.ClientEnterFamilyStorage";
    /// <summary>i32 — uid du NPC (service « family_storage »).</summary>
    public int NpcUid { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(NpcUid);
    public override void Deserialize(BinReader s) => NpcUid = s.ReadInt();
}

/// <summary>S→C NCID 504 — FamilyStorageDeleteItem
/// <c>[s16 index][s16 loc]</c> : l'item du slot <paramref name="Loc"/>
/// du coffre <paramref name="StorageIndex"/> a été retiré (push à
/// tous les membres en ligne — natif family+0x94).</summary>
public sealed class ZoneFamilyStorageDelete : NetMessage
{
    public override int Opcode => 504;
    public override string Name => "Zone.ClientFamilyStorageDeleteItem";
    /// <summary>s16 — index du coffre (0-2).</summary>
    public short StorageIndex { get; set; }
    /// <summary>s16 — loc vidée.</summary>
    public short Loc { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(StorageIndex);
        s.WriteShort(Loc);
    }
    public override void Deserialize(BinReader s)
    {
        StorageIndex = s.ReadShort();
        Loc = s.ReadShort();
    }
}

/// <summary>S→C NCID 505 — FamilyStoragePutItem
/// <c>[s16 index][s16 loc][record fixe]</c> : un item vient d'être
/// déposé dans le coffre (push membres).</summary>
public sealed class ZoneFamilyStoragePutNotify : NetMessage
{
    public override int Opcode => 505;
    public override string Name => "Zone.ClientFamilyStoragePutItem";
    /// <summary>s16 — index du coffre (0-2).</summary>
    public short StorageIndex { get; set; }
    /// <summary>s16 — loc du dépôt.</summary>
    public short Loc { get; set; }
    /// <summary>Item déposé (record fixe tronqué aux champs HUD).</summary>
    public InventorySlot? Item { get; private set; }
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        StorageIndex = s.ReadShort();
        Loc = s.ReadShort();
        if (FamilyStorageItemCodec.TryRead(s, out var rec))
            Item = rec;
    }
}

/// <summary>Entrée du journal famille (action 0 = dépôt, 1 = retrait
/// — <c>CLogFactory::FamilyStorageLog</c> natif).</summary>
public sealed record FamilyStorageLogEntry(short Action, string Detail);

/// <summary>S→C NCID 506 — FamilyStorageLog
/// <c>[u16 n]{[s16 action][str detail]}</c> : journal d'audit
/// (réponse au 179 + push à chaque dépôt/retrait).</summary>
public sealed class ZoneFamilyStorageLog : NetMessage
{
    public override int Opcode => 506;
    public override string Name => "Zone.ClientFamilyStorageLog";
    /// <summary>Lignes du journal (ordre récent → ancien natif).</summary>
    public List<FamilyStorageLogEntry> Entries { get; } = new();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        if (s.Remaining < 2) return;
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
        {
            var action = s.ReadShort();    // s16 — log_type
            var detail = s.ReadString();   // str — texte du journal
            Entries.Add(new FamilyStorageLogEntry(action, detail));
        }
    }
}

/// <summary>S→C NCID 508 — FamilyStorageData : contenu complet d'un
/// coffre (un paquet par coffre NON vide à l'ouverture du 507).
/// <c>[s16 0][s16 index][u16 n]{[s16 loc][record fixe]}[s16 0]</c>
/// (emit natif <c>OpenShop</c> — les s16 de tête/queue sont
/// explicitement 0).</summary>
public sealed class ZoneFamilyStorageData : NetMessage
{
    public override int Opcode => 508;
    public override string Name => "Zone.ClientFamilyStorageData";
    /// <summary>s16 — index du coffre décrit (0-2).</summary>
    public short StorageIndex { get; set; }
    /// <summary>Items du coffre (clé = loc).</summary>
    public List<InventorySlot> Items { get; } = new();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Items.Clear();
        if (s.Remaining < 6) return;
        s.ReadShort();                    // s16 — tête (0 natif)
        StorageIndex = s.ReadShort();     // s16 — index coffre
        var n = s.ReadUShort();           // u16 — nb d'items
        for (var i = 0; i < n; i++)
        {
            if (s.Remaining < 2) return;
            var loc = s.ReadShort();      // s16 — loc dans le coffre
            if (!FamilyStorageItemCodec.TryRead(s, out var rec))
                return;
            Items.Add(rec with { Bag = StorageIndex, Slot = loc });
        }
        // Queue : s16 0 natif — lu s'il reste des octets.
        if (s.Remaining >= 2) s.ReadShort();
    }
}

// ---------- record fixe coffre ----------

/// <summary>Lecteur du record item « coffre famille » — layout FIXE
/// (tous les champs, pas de mask u24). Ordre exact de
/// <c>FamilyStoragePackets.WriteStorageItem</c> serveur, copie d'un
/// CCloneItem par <c>CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute
/// </c> @0x083188e0 :
/// <c>[u16 itemId][s16 dur][s16 maxDur][str maker][u8 flags]
/// [6×s16 embedded][s16 emb_amount][u32 create][u16 +0x1c]
/// [s16 combo][9×u16 combos][s16 strengthen][s16 plus_param]
/// [i64 due][u16 train_lvl][u32 train_exp][6×u16 train slots]
/// [u16 ec10][u16 ec11][u16 gold_ench][3×s16 mb_ench]
/// [u16 mb_energy][u16 mb_star]</c> — 93 octets + chaîne maker.
/// Seuls les champs utiles au HUD sont conservés.</summary>
public static class FamilyStorageItemCodec
{
    /// <summary>Lit un record fixe → <see cref="InventorySlot"/> ;
    /// <see langword="false"/> si le corps s'arrête au milieu
    /// (conservateur — jamais d'exception).</summary>
    public static bool TryRead(BinReader r, out InventorySlot item)
    {
        item = new InventorySlot(0, 0, 0, 0, 0, 0, 0, 0);
        if (r.Remaining < 7) return false;
        var itemId = r.ReadUShort();        // u16 — itemdata+4 (tid)
        var dur = r.ReadShort();            // s16 — clone+0xc (count)
        var maxDur = r.ReadShort();         // s16 — clone+0xe
        var makerLen = r.ReadUShort();      // str — clone+0x20 maker
        if (r.Remaining < makerLen + 1 + 86) return false;
        r.ReadBytes(makerLen);
        var flags = r.ReadByte();           // u8 — sockets|identify|bind
        r.ReadBytes(12);                    // 6×s16 — vec+0x40 embedded
        var amount = r.ReadShort();         // s16 — clone+0x4c emb_amount
        r.ReadInt();                        // u32 — clone+0x18 create
        r.ReadShort();                      // u16 — clone+0x1c (non mappé)
        r.ReadShort();                      // s16 — clone+0x10 combo_id
        r.ReadBytes(18);                    // 9×u16 — vec+0x34 ec1-9
        var strengthen = r.ReadShort();     // s16 — +0x2c strengthen
        r.ReadShort();                      // s16 — clone+0x2e plus_param
        r.ReadLong();                       // i64 — clone+0x50 due_date
        r.ReadShort();                      // u16 — attrs+0x58 train_lvl
        r.ReadInt();                        // u32 — attrs+0x5c train_exp
        r.ReadBytes(12);                    // 6×u16 — train slots t/l ×3
        r.ReadShort();                      // u16 — vec34[9] ec10
        r.ReadShort();                      // u16 — vec34[10] ec11
        r.ReadShort();                      // u16 — clone+0x6c gold_ench
        r.ReadBytes(6);                     // 3×s16 — vec+0x70 mb_ench
        r.ReadShort();                      // u16 — clone+0x7c mb_energy
        r.ReadShort();                      // u16 — clone+0x7e mb_star
        item = new InventorySlot(0, 0, itemId, dur, maxDur, flags,
            amount, strengthen);
        return true;
    }
}

// ---------- état agrégé ----------

/// <summary>
/// Coffre de famille ouvert (507 + contenus 508 + deltas 504/505 +
/// journal 506) — lu par <see cref="Hud.FamilyStorageWindow"/>.
/// Trois coffres natifs (indices 0-2, 40 locs chacun —
/// <c>FamilyBankRepository.StorageCount/StorageSlotCapacity</c>),
/// verrouillés par privilèges famille côté serveur (index+11 →
/// niveaux ~3/5/6 — <c>CheckFamilyStoragePrivilege</c>).
/// Toutes les mutations sont appelées sur le thread principal via la
/// file <c>GameScene._pending</c> — lecture sans lock.
/// </summary>
public static class FamilyStorageState
{
    /// <summary>Nombre de coffres natifs.</summary>
    public const int StorageCount = 3;
    /// <summary>Locs par coffre (cap natif 0x28).</summary>
    public const int StorageCapacity = 40;

    /// <summary>Émis quand le coffre (ré)ouvre, qu'un 508 arrive ou
    /// qu'un delta 504/505/506 mute l'état.</summary>
    public static event Action? Changed;

    /// <summary>NPC ayant ouvert le coffre (uid dialogue).</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True tant que le coffre famille est ouvert.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Items par coffre : coffre → (loc → item).</summary>
    private static readonly Dictionary<int, InventorySlot>[] _chests =
    {
        new(), new(), new(),
    };

    /// <summary>Items du coffre <paramref name="index"/> (vue
    /// lecture — null si index hors plage).</summary>
    public static IReadOnlyDictionary<int, InventorySlot>? Chest(
        int index) =>
        index is >= 0 and < StorageCount ? _chests[index] : null;

    /// <summary>Journal d'audit (derniers 506 — réponse 179 ou
    /// push post-dépôt/retrait).</summary>
    private static readonly List<FamilyStorageLogEntry> _log = new();
    /// <summary>Entrées du journal (récentes d'abord).</summary>
    public static IReadOnlyList<FamilyStorageLogEntry> Log => _log;

    /// <summary>507 : ouvre le coffre (contenus en attente des 508 —
    /// les coffres gardent leur état précédent si pas de 508, comme
    /// le natif qui n'émet que les coffres non vides : on VIDE pour
    /// éviter l'affichage d'items obsolètes).</summary>
    public static void OnEnter(ZoneEnterFamilyStorage m)
    {
        NpcUid = m.NpcUid;
        IsOpen = true;
        foreach (var c in _chests) c.Clear();
        _log.Clear();
        Changed?.Invoke();
    }

    /// <summary>508 : remplit le coffre <paramref name="m"/>.StorageIndex
    /// (remplace tout son contenu).</summary>
    public static void OnData(ZoneFamilyStorageData m)
    {
        if (m.StorageIndex is < 0 or >= StorageCount) return;
        var chest = _chests[m.StorageIndex];
        chest.Clear();
        foreach (var it in m.Items)
            chest[it.Slot] = it;
        Changed?.Invoke();
    }

    /// <summary>505 : ajoute/remplace l'item à (index, loc).</summary>
    public static void OnPut(ZoneFamilyStoragePutNotify m)
    {
        if (m.StorageIndex is < 0 or >= StorageCount || m.Item == null)
            return;
        _chests[m.StorageIndex][m.Loc] =
            m.Item with { Bag = m.StorageIndex, Slot = m.Loc };
        Changed?.Invoke();
    }

    /// <summary>504 : vide la loc (index, loc).</summary>
    public static void OnDelete(ZoneFamilyStorageDelete m)
    {
        if (m.StorageIndex is < 0 or >= StorageCount) return;
        _chests[m.StorageIndex].Remove(m.Loc);
        Changed?.Invoke();
    }

    /// <summary>506 : remplace le journal par les lignes reçues
    /// (réponse au 179 = jusqu'à 50 entrées ; les push post-action
    /// n'ont qu'une ligne — on l'INSÈRE en tête pour garder
    /// l'historique de session).</summary>
    public static void OnLog(ZoneFamilyStorageLog m)
    {
        if (m.Entries.Count == 1)
            _log.Insert(0, m.Entries[0]);
        else
        {
            _log.Clear();
            _log.AddRange(m.Entries);
        }
        Changed?.Invoke();
    }

    /// <summary>Ferme le coffre (sortie node / fermeture fenêtre).</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        NpcUid = 0;
        foreach (var c in _chests) c.Clear();
        _log.Clear();
        Changed?.Invoke();
    }
}

// ---------- helpers requêtes banque (NCID 7 type 4) ----------

/// <summary>
/// Requêtes <see cref="ZoneSwitchItemLocation"/> (NCID 7) impliquant
/// la banque perso — type de conteneur natif 4 (« STO », map
/// char+0x514 ; désasm <c>CPlayerFactory::SwitchItemLocation</c>
/// @0x81237f0). Le serveur route 0→4 (dépôt stack entier,
/// auto-merge), 4→0 (retrait stack entier), 4→4 (swap de locs —
/// les <c>loc</c> banque sont GLOBALES, l'onglet n'est qu'un
/// groupage container_index) puis renvoie 277 + refresh 358.
/// </summary>
public static class ZoneBankActions
{
    /// <summary>Type de conteneur banque sur le fil (SwitchItem).</summary>
    public const short BankContainerType = 4;

    /// <summary>Sac → banque : drag-dépôt — le serveur dépose la
    /// ligne entière et auto-merge ; dst slot/index ignorés (−1).</summary>
    public static ZoneSwitchItemLocation DepositRequest(int bag,
        int slot) => new()
    {
        SrcType = 0, SrcSlot = (short)slot, SrcIndex = (short)bag,
        DstType = BankContainerType, DstSlot = -1, DstIndex = -1,
    };

    /// <summary>Banque → sac : drag-retrait — la ligne entière revient
    /// au premier emplacement libre (dst −1/−1 résolu serveur).</summary>
    public static ZoneSwitchItemLocation WithdrawRequest(int loc,
        int container) => new()
    {
        SrcType = BankContainerType, SrcSlot = (short)loc,
        SrcIndex = (short)container,
        DstType = 0, DstSlot = -1, DstIndex = -1,
    };

    /// <summary>Banque → banque : swap de deux locs (même onglet ou
    /// onglets croisés — le serveur swap par loc globale).</summary>
    public static ZoneSwitchItemLocation MoveRequest(int srcLoc,
        int srcCont, int dstLoc, int dstCont) => new()
    {
        SrcType = BankContainerType, SrcSlot = (short)srcLoc,
        SrcIndex = (short)srcCont,
        DstType = BankContainerType, DstSlot = (short)dstLoc,
        DstIndex = (short)dstCont,
    };
}
