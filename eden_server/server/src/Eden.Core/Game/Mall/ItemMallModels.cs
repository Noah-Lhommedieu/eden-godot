namespace Eden.Core.Game.Mall;

/// <summary>
/// Entrée du catalogue item mall natif — équivalent C# du struct
/// rempli par <c>CAccountDBO::GetAllItemMallItem</c> (désasm world
/// @0x808c650) : <c>SELECT … FROM itemmall WHERE sell = 1</c> puis
/// validations natives (item_num 0→1, point 0 refusé hors groupe 99,
/// special_price &lt; point requis, item existant, item_num ≤ maxstack,
/// money_unit ∈ {1,2,3} hors groupe 99, fenêtres de dates
/// <c>YYYYMMDDHH</c> → epoch).
/// </summary>
public sealed class ItemMallEntry
{
    /// <summary>Item_id S_Item vendu (struct +0x2, u16 wire).</summary>
    public int ItemId { get; init; }
    /// <summary>Groupe catalogue (clé map externe native, s16 wire).</summary>
    public int Group { get; init; }
    /// <summary>Index dans le groupe (clé map interne, s16 wire).</summary>
    public int Index { get; init; }
    /// <summary>Quantité par paquet (item_num, 0→1 natif, u16 wire).</summary>
    public int ItemNum { get; init; }
    /// <summary>Monnaie : 1 = pvalues, 2 = bonus, 3 = temps de jeu
    /// (validé au chargement, JAMAIS émis au client — le wire n'a que
    /// 13 champs par record pour 14 colonnes).</summary>
    public int MoneyUnit { get; init; }
    /// <summary>Prix plein (point, u32 wire).</summary>
    public uint Point { get; init; }
    /// <summary>Prix promo (special_price, u32 wire ; &lt; point si ≠ 0).</summary>
    public uint SpecialPrice { get; init; }
    /// <summary>item_color (i64 wire — variantes teintées ; 0 partout
    /// dans les données natives).</summary>
    public long ItemColor { get; init; }
    /// <summary>Début de vente epoch (sell_date converti timelocal ;
    /// 0 si déjà passé — i32 wire, struct +0x30).</summary>
    public int SellDate { get; init; }
    /// <summary>Fin de vente epoch (not_sell_date converti ; l'entrée
    /// est sautée au chargement si déjà expirée — i32 wire, struct +0x1c).</summary>
    public int NotSellDate { get; init; }
    /// <summary>Limite d'achat par compte — renseigné uniquement pour le
    /// groupe 8 natif (u16 wire, struct +0x10, 0 sinon).</summary>
    public int NumLimit { get; init; }
    /// <summary>Chaîne fortune_bag (str wire, struct +0x20).</summary>
    public string FortuneBag { get; init; } = "";
    /// <summary>Note affichée (str wire, struct +0x24).</summary>
    public string Note { get; init; } = "";
    /// <summary>Niveau minimum requis (u16 wire, struct +0x2c ; achat
    /// refusé code 7 si niveau &lt; limite).</summary>
    public int LevelLimit { get; init; }
}

/// <summary>
/// Ligne <c>item_receivable</c> — item acheté en attente de réclamation
/// (native : state -1 = billing en cours, 1 = réclamable, 2 = livraison
/// en cours ; supprimée après insert dans item_receipt).
/// </summary>
public sealed class ItemReceivableRow
{
    /// <summary>Id de la ligne = "receipt id" échangé avec le client.</summary>
    public int Id { get; init; }
    /// <summary>Item vendu.</summary>
    public int ItemId { get; init; }
    /// <summary>Quantité.</summary>
    public int ItemQuantity { get; init; }
    /// <summary>Couleur/variante (i64 passée telle quelle au ZW 24).</summary>
    public long ItemColor { get; init; }
    /// <summary>Horodatage d'achat (renvoyé en string dans le 57).</summary>
    public DateTime PurchaseTime { get; init; }
    /// <summary>Coût payé (colonne point — audit).</summary>
    public int Point { get; init; }
    /// <summary>Quantité achetée (colonne amount — multiplicateur).</summary>
    public int Amount { get; init; }
    /// <summary>Monde d'achat (world_id).</summary>
    public int WorldId { get; init; }
    /// <summary>Nom du perso acheteur.</summary>
    public string PlayerName { get; init; } = "";
    /// <summary>Destinataire mail éventuel.</summary>
    public string MailName { get; init; } = "";
}

/// <summary>
/// Ligne <c>itemmall_limit_amount</c> — compteur d'achats par compte
/// pour les entrées à num_limit (groupe 8 natif).
/// </summary>
public sealed class ItemMallLimitRow
{
    public int ItemGroup { get; init; }
    public int ItemIndex { get; init; }
    public int ItemId { get; init; }
    public int ItemNum { get; init; }
    /// <summary>money_unit DB — la clé wire du 79 = money_unit*100 +
    /// item_group (GetAllItemMallLimit natif).</summary>
    public int MoneyUnit { get; init; }
    public int SellAmount { get; set; }
}

/// <summary>
/// Codes de résultat natifs du flux item mall — désasm
/// <c>CNE_CW_ServerItemMallReqBuyItem::Execute</c> (world @0x80dff00) et
/// <c>CNE_ZW_ZoneServerItemMallReceieveAttempt::Execute</c> (zone
/// @0x82ac7f0). Émis dans le 63 (achat), le 58 (claim) et le ZW 12
/// (résultat de livraison zone→monde).
/// </summary>
public static class ItemMallResult
{
    /// <summary>Succès.</summary>
    public const short Ok = 0;
    /// <summary>63 : points insuffisants (money_unit 1 → pvalues).</summary>
    public const short InsufficientPoints = 1;
    /// <summary>63 : catalogue périmé — le client redemande la liste
    /// (natif envoie alors 62+79 automatiquement).</summary>
    public const short VersionMismatch = 2;
    /// <summary>63 : points insuffisants (money_unit 2 → bonus).</summary>
    public const short InsufficientBonus = 3;
    /// <summary>63 : échec de lecture du compteur d'achats limités.</summary>
    public const short LimitCheckFailed = 5;
    /// <summary>63 : la limite d'achat du compte serait dépassée.</summary>
    public const short LimitExceeded = 6;
    /// <summary>63 : niveau du perso sous la level_limit de l'entrée.</summary>
    public const short LevelTooLow = 7;
    /// <summary>58 : ligne introuvable / pas en state 1 pour ce compte.</summary>
    public const short RecordNotFound = 0xb;
    /// <summary>58 : erreur DB sur la requête de claim.</summary>
    public const short RecordError = 0xc;
    /// <summary>ZW12 : perso absent de la zone (GetCharacterZone null).</summary>
    public const short CharNotInZone = 0x15;
    /// <summary>ZW12 : item_id absent de l'itemdata (désasm @0x82ac83e).</summary>
    public const short ItemDefMissing = 0x18;
    /// <summary>ZW12 : inventaire plein (GetEmptySlotNum &lt; 1).</summary>
    public const short InventoryFull = 0x19;
    /// <summary>Échec générique.</summary>
    public const short Failure = 99;
}
