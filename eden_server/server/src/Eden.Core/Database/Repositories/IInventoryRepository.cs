using Eden.Core.Game.Inventory;

namespace Eden.Core.Database.Repositories;

public interface IInventoryRepository
{
    Task<int?> GetAvailableSlotAsync(int characterId, int bagId);
    /// <summary>Premier emplacement libre dans n'importe quel sac
    /// ouvert (0-4 par capacité) — natif GetContainerWithEmptySlot.</summary>
    Task<(int BagId, int Slot)?> GetAnyAvailableSlotAsync(int characterId);
    Task<ItemEntity?> GetItemAsync(int characterId, int bagId, int slot);
    /// <summary>Un item porté — conteneur type 1 natif (vue
    /// `equipment`, `group` = container_index, `loc` = slot
    /// d'équipement) — <c>GetCloneItemFromContainer</c> arbre
    /// +0x4fc.</summary>
    Task<ItemEntity?> GetEquipmentItemAsync(int characterId, int group,
        int loc);
    Task<IReadOnlyList<ItemEntity>> GetItemsByCharacterIdAsync(int characterId);
    Task AddItemAsync(ItemEntity item);
    Task<bool> RemoveItemAsync(int characterId, int bagId, int slot);
    Task<bool> UpdateItemAsync(ItemEntity item);
    /// <summary>Capacité (nb de slots) du sac `bagId` du personnage —
    /// `inventory_slots.bag_N_slots`, 0 si le sac n'existe pas.</summary>
    Task<int?> GetBagCapacity(int characterId, int bagId);
    /// <summary>Capacités de tous les sacs inv 0-4 (bag_N_slots de
    /// inventory_slots) — index = n° de sac (loc du porte-sacs natif,
    /// désasm CPlayerFactory::SwitchBagFromContainer).</summary>
    Task<IReadOnlyDictionary<int, int>> GetAllBagCapacitiesAsync(
        int characterId);
    /// <summary>Crée/resize un sac d'inventaire — AddNewContainer /
    /// UpdateContainerSize natifs (UPSERT inventory_slots).</summary>
    Task SetBagCapacityAsync(int characterId, int bagId, int slots);
    /// <summary>Sacs équipés du personnage (table `bags` native —
    /// holder container_index 0=inv/4=banque, loc = slot 1-4).</summary>
    Task<IReadOnlyList<EquippedBag>> GetEquippedBagsAsync(int characterId);
    /// <summary>Déplace un item d'inventaire vers le porte-sacs —
    /// INSERT INTO bags + DELETE de inventory1/2 (même serial id, comme
    /// SwitchContainerItem natif qui conserve le clone).</summary>
    Task<bool> MoveItemToBagHolderAsync(int characterId, int srcBagId,
        int srcSlot, int holderIndex, int holderLoc);
    /// <summary>Déplace un sac équipé vers l'inventaire — DELETE de
    /// bags + INSERT INTO inventory1 (serial conservé).</summary>
    Task<bool> MoveBagToInventoryAsync(int characterId, int holderIndex,
        int holderLoc, int dstBagId, int dstSlot);
    /// <summary>Récupère un item du porte-sacs.</summary>
    Task<EquippedBag?> GetEquippedBagAsync(int characterId,
        int holderIndex, int holderLoc);
    /// <summary>Échange les locs de deux sacs équipés dans le porte-sacs
    /// (SwitchBagToBag natif).</summary>
    Task SwapEquippedBagLocsAsync(int characterId, int holderIndex,
        int locA, int locB);
    /// <summary>Échange les conteneurs d'inventaire bagA↔bagB (items
    /// inclus) — SwitchContainersAndItems natif : les items suivent le
    /// numéro du porte-sacs.</summary>
    Task SwapInventoryBagsAsync(int characterId, int bagA, int bagB);
    /// <summary>Items portés — vue native `equipment` (loc, item_id).</summary>
    Task<IReadOnlyList<(int Loc, int ItemId)>> GetEquippedItemsAsync(int characterId);
    /// <summary>Items du sac 0 — vue native `inventory` (appel 312).</summary>
    Task<IReadOnlyList<ItemEntity>> GetLiveItemsAsync(int characterId);
    /// <summary>Suppression par clé `id` dans les buffers natifs.</summary>
    Task RemoveLiveItemAsync(int characterId, int itemRowId);
    /// <summary>Transfert de propriété d'une ligne item —
    /// <c>CItemFactory::SwitchItemLocation</c> natif de VendorBuy :
    /// la ligne (serial `id`) est ré-attribuée à un autre joueur et
    /// déplacée — le serial est préservé (historique de l'item). Les
    /// deux buffers inventory1/2 sont tentés.</summary>
    Task<bool> TransferItemAsync(int itemRowId, int newOwnerId,
        int newBagId, int newSlot);
    /// <summary>Persiste les champs Mana Infusion d'un item —
    /// `CItemDBO::ModifyItemTrain` natif (train_level/train_exp +
    /// 3 paires type/niveau de slot).</summary>
    Task UpdateItemTrainAsync(int itemUid, int level, long exp,
        int t1, int l1, int t2, int l2, int t3, int l3);
    /// <summary>Serial de l'item choisi pour la Mana Infusion
    /// (`player_characters.eqtrain_item_id`, char+0xd54 natif).</summary>
    Task<int> GetEquipTrainItemIdAsync(int characterId);
    /// <summary>Persiste `eqtrain_item_id` — `SavePlayerCharacter` de
    /// `SetEquipTrainItemID` @0x840b670.</summary>
    Task SetEquipTrainItemIdAsync(int characterId, int itemSerial);
    /// <summary>Résout un serial → (item, type conteneur natif
    /// 0=sac/1=équipé, clé de conteneur, index dans le vecteur trié
    /// par loc) — `FindEquipTrainItemLocation` @0x840b500 ; null si
    /// absent (le natif réinitialise alors eqtrain_item_id).</summary>
    Task<(ItemEntity Item, int ContainerType, int ContainerKey,
        int VectorIndex)?> FindItemBySerialAsync(int characterId,
        int serial);
}

/// <summary>Un sac équipé dans le porte-sacs (ligne de la table `bags`
/// native). `HolderIndex` = container_index (0 = porte-sacs inventaire,
/// 4 = porte-sacs banque), `Loc` = slot 1-4 = n° du sac d'inventaire
/// ouvert.</summary>
public sealed class EquippedBag
{
    public int SerialId { get; set; }
    public int ItemId { get; set; }
    public int HolderIndex { get; set; }
    public int Loc { get; set; }
    public string Maker { get; set; } = string.Empty;
    public int Durability { get; set; }
    public int Identify { get; set; }
    public int Bind { get; set; }
    public int DueDate { get; set; }
    /// <summary>Capacité en slots du sac (ItemDef.BagCapacity — col28,
    /// = clone+0xc natif d'un CBagItem). Remplie par l'appelant depuis
    /// le template, pas la colonne DB.</summary>
    public int Capacity { get; set; }
}
