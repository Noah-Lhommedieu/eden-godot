namespace Eden.Core.Game.Inventory;

/// <summary>
/// Un emplacement de tenue rapide — miroir du `SFastDressInfo` natif
/// (8×i32, valeur des maps `char+0x58c`/`char+0x5a4`) persisté dans
/// `fast_dress` (player_id, id, item_id, container_index, from_loc,
/// from_index, page, tar_loc — PK player_id+page+tar_loc).
/// Wire 593/594 : les 6 champs `ItemId`..`TarLoc` (uid et Flag
/// internes — `CNC_CZ_ClientUpdateFastDressData::Serialize` zone
/// @0x8232e70).
/// </summary>
public sealed class FastDressRecord
{
    /// <summary>Serial de l'item (SFastDressInfo+0x14, colonne `id`) —
    /// 0 = emplacement vide.</summary>
    public int Uid;

    /// <summary>Type d'item (+0x18, colonne `item_id`).</summary>
    public int ItemId;

    /// <summary>Type de conteneur source (+0x1c, colonne
    /// `container_index`) : 0 = inventaire, 1 = équipement,
    /// 9 = collection (constantes `IItemContainer` natives).</summary>
    public int ContainerType;

    /// <summary>Emplacement dans le conteneur (+0x20, colonne
    /// `from_loc`) ; -1 = vide (seed natif).</summary>
    public int FromLoc = -1;

    /// <summary>Index du conteneur (+0x24, colonne `from_index`) :
    /// n° de sac pour type 0, groupe équipement pour type 1 ;
    /// -1 = vide.</summary>
    public int FromIndex = -1;

    /// <summary>Page de tenue (+0x28) — 0..2 natif.</summary>
    public int Page;

    /// <summary>Clé d'emplacement dress (+0x2c) — voir
    /// <see cref="FastDressSlots.EquipLocs"/>.</summary>
    public int TarLoc;

    /// <summary>Flag interne (+0x30, non wire/DB) — case à cocher
    /// du record (lu comme byte dans `LoadFastDress` zone).</summary>
    public int Flag;

    /// <summary>Record vide natif (seed : uid=0, item=0, con=0,
    /// loc=-1, idx=-1 — INSERT de `CExecutionerDBO::LoadFastDress`).</summary>
    public static FastDressRecord Empty(int page, int tarLoc) => new()
    {
        Uid = 0, ItemId = 0, ContainerType = 0,
        FromLoc = -1, FromIndex = -1,
        Page = page, TarLoc = tarLoc,
    };

    /// <summary>Vrai si le record ne pointe vers aucun item
    /// (from_loc/from_index == -1 natif — le test d'`ApplyFastDress`
    /// @0x8125dxx).</summary>
    public bool IsEmpty => Uid <= 0;

    /// <summary>Copie champ à champ (Save natif : les 8 i32 —
    /// `CItemFactory::SaveFastDressInfo` @0x8093axx).</summary>
    public void CopyFrom(FastDressRecord o)
    {
        Uid = o.Uid; ItemId = o.ItemId;
        ContainerType = o.ContainerType;
        FromLoc = o.FromLoc; FromIndex = o.FromIndex;
        Page = o.Page; TarLoc = o.TarLoc; Flag = o.Flag;
    }

    /// <summary>Reset natif `RemoveFastDressInfo` : uid/item/con →0,
    /// loc/index → -1 (zone @0x810b5xx).</summary>
    public void Clear()
    {
        Uid = 0; ItemId = 0; ContainerType = 0;
        FromLoc = -1; FromIndex = -1;
    }
}

/// <summary>
/// Les deux maps natives du personnage :
/// `Saved` = `char+0x58c` (données appliquées/persistées — Apply et
/// SaveFastDressInfo lisent ici, EnterNodeFastDress les envoie) ;
/// `Edited` = `char+0x5a4` (copie de travail — Record/Remove
/// écrivent ici, UpdateFastDressPage la restaure depuis Saved).
/// </summary>
public sealed class FastDressState
{
    /// <summary>Page → (tar_loc → record) — map live native.</summary>
    public readonly Dictionary<int, Dictionary<int, FastDressRecord>> Saved = new();

    /// <summary>Page → (tar_loc → record) — map working native.</summary>
    public readonly Dictionary<int, Dictionary<int, FastDressRecord>> Edited = new();

    /// <summary>Crée les 3 pages vides des deux maps (init de
    /// `CNE_DBA_ClientFastDress::LoadFastDress` zone @0x82efbxx —
    /// `iVar16` = 0..2).</summary>
    public static FastDressState Create()
    {
        var s = new FastDressState();
        for (var p = 0; p < FastDressSlots.PageCount; p++)
        {
            s.Saved[p] = new Dictionary<int, FastDressRecord>();
            s.Edited[p] = new Dictionary<int, FastDressRecord>();
        }
        return s;
    }
}
