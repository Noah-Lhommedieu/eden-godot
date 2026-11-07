using System;

namespace Eden.Core.Game.Work;

/// <summary>
/// Modèle runtime d'un ordre de travail (métiers 12-17) — miroir du
/// <c>lapis::CWork</c> natif (char+0xb20, 11 slots ×4B d'index) :
///   +0x08 work_num, +0x10 work_type, +0x14 work_id, +0x18 work_event,
///   +0x1c failure_rate_of_decline, +0x20 bouns_item_amount,
///   +0x24 fm_name, +0x28 fm_id, +0x2c building_item_id,
///   +0x30 command flag, +0x32 ext_success_rate, +0x34 ext_building_rate,
///   +0x36 running flag, +0x38/+0x5c/+0x80/+0xa4 = 4 SWorkItem.
/// Persisté dans `player_works` (mêmes colonnes natives + `finish_at`
/// ajouté pour rendre la deadline durable — le natif ne stockait que
/// l'enchant id volatil).
/// </summary>
public sealed class WorkSlot
{
    /// <summary>Nombre de slots natifs : 10 utilisés au démarrage
    /// (ToWork scanne +0xb20..+0xb44 = index 0-9), le slot 10 n'est
    /// choisi que si présent/libre ; WorkResultOK accepte 0..10.</summary>
    public const int MaxSlots = 11;

    /// <summary>Index du slot (work_num, 0-10).</summary>
    public int WorkNum { get; set; }
    /// <summary>Type de métier (+0x10) : 12 mining, 13 planting,
    /// 14 summon, 15 alchemy, 16 casting, 17 enchanting.
    /// = le type de service NPC / le shopType de l'opération.</summary>
    public int WorkType { get; set; }
    /// <summary>Id de recette (+0x14) — négatif = travail échoué
    /// (SaveWork(-id,0) natif), 0 = slot vide.</summary>
    public int WorkId { get; set; }
    /// <summary>work_event (+0x18, colonne DB) = id de commande
    /// d'événement post-travail (WorkResultOK → ClientPlayerEvent 499
    /// puis ToExecCmd au 2e claim). 0 pour les ordres NPC ordinaires.
    /// Ne pas confondre avec l'enchant armé work+4 (200+slot) qui
    /// n'est émis que dans le 497 et non persisté.</summary>
    public int WorkEvent { get; set; }
    /// <summary>failure_rate_of_decline (+0x1c) — modifie le taux de
    /// chaque produit : rate - rate×decline/100 (op+8 natif, 0 pour
    /// les ordres NPC).</summary>
    public int FailureRateOfDecline { get; set; }
    /// <summary>bouns_item_amount (+0x20) — ajouté à la quantité de
    /// chaque produit tiré (op+0xa natif, 0 pour NPC).</summary>
    public int BonusItemAmount { get; set; }
    /// <summary>Nom de famille affiché (+0x24) — vide hors ville de
    /// famille (CheckToCollect prend le nom de la famille visitée).</summary>
    public string FmName { get; set; } = string.Empty;
    /// <summary>fm_id (+0x28) — famille propriétaire (0 pour NPC).</summary>
    public int FmId { get; set; }
    /// <summary>building_item_id (+0x2c) — bâtiment de la ville de
    /// famille fournissant le service (0 pour NPC).</summary>
    public int BuildingItemId { get; set; }
    /// <summary>ext_success_rate (+0x32) — bonus de taux du booster
    /// (CChanceItem kind 0x1f45/0x1f47/0x1f48).</summary>
    public int ExtSuccessRate { get; set; }
    /// <summary>ext_building_rate (+0x34) — bonus bâtiment du booster
    /// (kind 0x1f44/0x1f48).</summary>
    public int ExtBuildingRate { get; set; }
    /// <summary>Flag de commande (+0x30) — posé par WorkResultOK après
    /// la remise des items quand work_event reste non nul : la
    /// prochaine revendication passe par ToExecCmd (événements —
    /// chez nous : termine le slot via 500).</summary>
    public bool EventPending { get; set; }
    /// <summary>Flag « en cours » (+0x36) — posé à l'armement et à la
    /// matérialisation.</summary>
    public bool Running { get; set; }
    /// <summary>Échéance absolue (epoch ms UTC) — remplace l'enchant
    /// natif pour survivre au restart/déconnexion. 0 = pas de timer
    /// (slot vide ou déjà matérialisé).</summary>
    public long FinishAtMs { get; set; }
    /// <summary>Produits tirés — les 4 SWorkItem natifs.</summary>
    public WorkProduct[] Products { get; } =
        { new(), new(), new(), new() };

    /// <summary>Slot vide (work_id == 0, rien en cours ni à récupérer).</summary>
    public bool IsEmpty => WorkId == 0;
    /// <summary>Travail échoué (work_id &lt; 0 — SaveWork(-id,0)).</summary>
    public bool Failed => WorkId < 0;
    /// <summary>Produits prêts à récupérer (matérialisé — timer
    /// arrivé à terme et produits tirés, ou échec consommé).</summary>
    public bool Completed => WorkId != 0 && FinishAtMs == 0;

    /// <summary>Réinitialise le slot (CWork::ResetWork — écrit la
    /// ligne player_works à zéro hors player_id/work_num).</summary>
    public void Reset()
    {
        WorkType = 0; WorkId = 0; WorkEvent = 0;
        FailureRateOfDecline = 0; BonusItemAmount = 0;
        FmName = string.Empty; FmId = 0; BuildingItemId = 0;
        ExtSuccessRate = 0; ExtBuildingRate = 0;
        EventPending = false; Running = false; FinishAtMs = 0;
        foreach (var p in Products) p.Reset();
    }
}

/// <summary>
/// SWorkItem natif (stride 0x24 dans CWork) :
///   +0x00 item u32, +0x04 amount s16, +0x06 socket_amount s16,
///   +0x08 curse byte, +0x0a combo u16, +0x0c..+0x22 extra_combo u16 ×11
///   (la table player_works n'en persiste que 9 —
///   AddWrokItem @0x84d17f0 lit jusqu'à 11 mais le SELECT/UPDATE
///   natif n'en charge/écrit que 9).
/// </summary>
public sealed class WorkProduct
{
    /// <summary>Nombre d'extra_combos persistés (extra_comboN_1..9).</summary>
    public const int ExtraComboCount = 9;

    /// <summary>Slots extra_combo du SWorkItem en mémoire — le natif
    /// copie jusqu'à 11 u16 depuis le vecteur RandItemCombo
    /// (AddWrokItem @0x084d17f0, +0x44) mais n'en persiste que 9.</summary>
    public const int ExtraComboCapacity = 11;

    public int ItemId { get; set; }
    public int Amount { get; set; }
    /// <summary>Sockets ouvertes sur le produit (EndCombine : roll
    /// par socket de l'itemdef — ChangeEmbeddedAmount).</summary>
    public int SocketAmount { get; set; }
    /// <summary>Flag « maudit » (combine+0x3c — toujours 0 dans les
    /// données actuelles).</summary>
    public bool Curse { get; set; }
    /// <summary>combo_id principal (SelectWorkItemCombo).</summary>
    public int Combo { get; set; }
    /// <summary>extra_combo_id1..11 (SelectWorkItemCombo) — seuls les
    /// <see cref="ExtraComboCount"/> premiers sont persistés.</summary>
    public int[] ExtraCombos { get; } = new int[ExtraComboCapacity];

    public bool IsEmpty => ItemId == 0;

    public void Reset()
    {
        ItemId = 0; Amount = 0; SocketAmount = 0; Curse = false; Combo = 0;
        Array.Clear(ExtraCombos, 0, ExtraCombos.Length);
    }
}

/// <summary>ShopType natif → catégorie de recette
/// (CNE_CZ_ZoneServerPlayerWork::Execute @0x8287xxx :
/// 0x3d-0x41 → ToWork ; les op ids 0x3d..0x42 mappent vers les
/// types 12..17 via GetShopType — chez nous le type EST le service
/// NPC 12-17 directement).</summary>
public static class WorkTypes
{
    public const int Mining = 12;
    public const int Planting = 13;
    public const int Summon = 14;
    public const int Alchemy = 15;
    public const int Casting = 16;
    public const int Enchanting = 17;

    /// <summary>Types de collecte (CCollectData, S_FamilyCollect).</summary>
    public static bool IsCollect(int type) => type is >= 12 and <= 14;
    /// <summary>Types de combinaison (CCombineData, S_FamilyCombine).</summary>
    public static bool IsCombine(int type) => type is >= 15 and <= 17;
    public static bool IsValid(int type) => IsCollect(type) || IsCombine(type);

    /// <summary>Kind de catalyseur requis (S_Item col8) par type —
    /// CheckToCollect : 12→0x33, 13→0x32, 14→0x34 ;
    /// CheckToCombine : 15→0x35, 16→0x36, 17→0x37.</summary>
    public static int RequiredCatalystKind(int type) => type switch
    {
        Mining => 0x33, Planting => 0x32, Summon => 0x34,
        Alchemy => 0x35, Casting => 0x36, Enchanting => 0x37,
        _ => 0,
    };
}
