using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;

namespace Eden.Core.Game.Inlay;

/// <summary>
/// Système de sockets / incrustation de gemmes — portage de
/// <c>lapis::CItemFactory::ChangeEmbeddedAmount</c> @0x809baf0
/// (ouverture probabiliste des sockets à la création d'un équipement)
/// et de <c>lapis::UseItemToItemControl</c> case
/// <c>GetTemplateType()==0x2d</c> @0x84ec120/@0x84ebxxx (gemme kind 45
/// glissée sur une arme/armure via le NCID 19 ItemUse).
///
/// Classes d'items natives (vtable GetType, désasm zone) :
///   CWeapon=0 → kinds 17-34  (CItemDBO::GetAllWeapons : kind-0x11<0x12)
///   CArmor=1  → kinds {1-16, 37-40, 78, 81@loc∈{10,34}}
///               (CItemDBO::GetAllArmors @0x835c980)
///   CAddAttrItem=6 → kinds {45,46,47,56,59,79}
///               (CItemDBO::GetAllAddAttrItems @0x835c3xx)
///
/// Modèle de données : `clone+0x24` bits 0-2 = nombre de sockets
/// ouverts (colonne `embedded_amount`), bit 3 = socket-capable
/// (lapis+0xf6, posé à 1 par les ctor CWeapon/CArmor) ; `clone+0x40`
/// = 6×s16 (`embedded_id1..6`) — 0 = socket vide, −1 = non ouverte.
/// </summary>
public sealed class InlayService
{
    private readonly Random _random;

    /// <summary>Ids des textes natifs T_TextIndex émis par la branche
    /// 0x2d (SendText canal -105, vfunc char+0x58).</summary>
    public const int TextSuccess = 0xc1;        // 193 « You have inlaid the Spellstone. »
    public const int TextCannotInlay = 0xc2;    // 194 « cannot be inlaid with Spellstones »
    public const int TextNoSlots = 0xc3;        // 195 « No available slots. »
    public const int TextLevelTooLow = 0xcce;   // 3278 « Equipment level too low »

    // Textes natifs de la branche enchant (CheckExecute cases
    // 0x38/0x4f — T_TextIndex.ini).
    /// <summary>0x9f6 = T_TextIndex 2550 « This item cannot be
    /// Enchanted » — cible non équipement / EquipLoc incompatible /
    /// même enchant déjà posé.</summary>
    public const int TextCannotEnchant = 0x9f6;
    /// <summary>0x9f7 = T_TextIndex 2551 « Enchantment added » —
    /// succès, émis avant le 369.</summary>
    public const int TextEnchantAdded = 0x9f7;
    /// <summary>0x9f8 = T_TextIndex 2552 « Equipment level too low to
    /// use Enchantments » — niveau insuffisant.</summary>
    public const int TextEnchantLevel = 0x9f8;

    public InlayService(Random? random = null)
        => _random = random ?? new Random();

    /// <summary>GetType()==0 natif : kinds 17-34 (filtre
    /// GetAllWeapons `kind-0x11 &lt; 0x12`).</summary>
    public static bool IsWeaponKind(int kind) => kind is >= 17 and <= 34;

    /// <summary>GetType()==1 natif : kinds {1-16, 37-40, 78} + 81
    /// quand equipLoc ∈ {10, 0x22=34} (filtre GetAllArmors).</summary>
    public static bool IsArmorKind(int kind, int equipLoc)
        => kind is >= 1 and <= 16 or >= 37 and <= 40 or 78
           || (kind == 81 && (equipLoc == 10 || equipLoc == 34));

    /// <summary>GetType()==6 natif : kinds add-attr (gemmes et
    /// assimilés) — 45 = Spellstone traitée par la branche 0x2d.</summary>
    public static bool IsAddAttrKind(int kind)
        => kind is 45 or 46 or 47 or 56 or 59 or 79;

    /// <summary>Cible d'inlay valide : `target.GetType() ∈ {0,1}`
    /// (arme ou armure) — tout le reste reçoit le texte 0xc2.</summary>
    public static bool IsInlayTargetKind(int kind, int equipLoc)
        => IsWeaponKind(kind) || IsArmorKind(kind, equipLoc);

    /// <summary>Item socket-capable côté wire : bit 3 de clone+0x24 =
    /// `lapis+0xf6 != 0`, posé à 1 par les ctor CWeapon/CArmor
    /// (désasm init @0x835c7xx/@0x835ccxx) — arme ou armure.</summary>
    public static bool IsSocketCapable(ItemParser.ItemDef def)
        => IsInlayTargetKind(def.Kind, def.EquipLoc);

    /// <summary>Ouverture probabiliste des sockets à la création —
    /// <c>CItemFactory::ChangeEmbeddedAmount</c> @0x809baf0 :
    /// armes toujours éligibles, armures seulement kinds 1-16
    /// (`GetTemplateType` ∈ [1,0x10] dans la branche GetType==1),
    /// autres classes ignorées. Lance `random()%100 &lt; rate` par
    /// socket possible EN SÉQUENCE et s'arrête au premier échec ;
    /// chaque socket ouverte initialise `embedded[i]=0` (le reste
    /// reste −1). `max` est masqué &7 puis clampé 7→6.</summary>
    /// <returns>(count, ids[6]) — count = sockets ouvertes,
    /// ids = vecteur embedded_id1..6 (0 ouverte, −1 scellée).</returns>
    public (int Count, int[] Ids) RollSockets(ItemParser.ItemDef def)
    {
        var ids = new[] { -1, -1, -1, -1, -1, -1 };
        // Gating natif : arme → toujours ; armure → kinds 1-16
        // uniquement (les kinds 37-40/78/81 ont GetType()==1 mais
        // l'early-return kind les exclut du roll).
        var eligible = IsWeaponKind(def.Kind)
            || (IsArmorKind(def.Kind, def.EquipLoc)
                && def.Kind is >= 1 and <= 16);
        if (!eligible)
            return (0, ids);

        var max = def.MaxSocket & 7;          // bVar4 & 7 natif
        if (max == 7) max = 6;                // clamp natif 7→6
        if (max == 0 || def.MaxSocket == 0)
            return (0, ids);

        var rate = def.SocketRate;
        var count = 0;
        for (var i = 0; i < max; i++)
        {
            // Natif : `if (rate <= random()%100) stop` — le jet i+1
            // échoue → count = i.
            if (_random.Next(100) >= rate)
                break;
            ids[i] = 0;                        // socket ouverte, vide
            count++;
        }
        return (count, ids);
    }

    /// <summary>Validation de l'incrustation —
    /// <c>UseItemToItemControl::CheckExecute</c> case 0x2d : la cible
    /// doit être une arme/armure (0xc2), le niveau de la gemme ≤ niveau
    /// requis de la cible (0xcce), la cible doit avoir des sockets
    /// ouverts ; le scan choisit le PREMIER embedded_id == 0 dans
    /// [0..count) et s'arrête au premier −1 (0xc3).</summary>
    /// <param name="gemDef">Def de la gemme (kind 45 attendu).</param>
    /// <param name="target">Item cible résolu (embedded_* chargés).</param>
    /// <param name="targetDef">Def de la cible.</param>
    /// <param name="charLevel">Niveau du perso — utilisé quand la cible
    /// est un équipement évolutif (`item+0x114` natif ≠ 0 : le niveau
    /// requis est alors le niveau du perso).</param>
    /// <param name="targetIsGrowEquip">true si la cible a un grow-equip
    /// natif (`item+0x114` ≠ 0). Aucune table grow-equip chargée pour
    /// l'instant → false.</param>
    public InlayValidation ValidateInlay(ItemParser.ItemDef gemDef,
        ItemEntity target, ItemParser.ItemDef targetDef,
        int charLevel, bool targetIsGrowEquip = false)
    {
        if (target == null ||
            !IsInlayTargetKind(targetDef.Kind, targetDef.EquipLoc))
            return new InlayValidation(false, -1, TextCannotInlay);

        // Niveau natif : item+0x114 (grow-equip) → niveau du perso
        // (char+0x470), sinon itemdata+0xfc+0x22 = niveau requis.
        var requiredLevel = targetIsGrowEquip
            ? charLevel : targetDef.RequiredLevel;
        if (gemDef.GemLevel > requiredLevel)
            return new InlayValidation(false, -1, TextLevelTooLow);

        var count = target.EmbeddedAmount & 7;
        if (count == 0)
            return new InlayValidation(false, -1, TextNoSlots);

        // Scan natif : vec[0]==-1 → échec direct ; itère i<count,
        // premier 0 gagne, premier −1 arrête.
        for (var i = 0; i < count; i++)
        {
            var id = target.GetEmbeddedId(i);
            if (id == 0)
                return new InlayValidation(true, i, 0);
            if (id == -1)
                break;
        }
        return new InlayValidation(false, -1, TextNoSlots);
    }

    /// <summary>Incruste la gemme : `embedded[idx] = item_id` de la
    /// gemme (natif : `*(lapisItem+4)` — le TEMPLATE id, pas le
    /// serial). Le vecteur DB natif utilise s16.</summary>
    public static void ApplyInlay(ItemEntity target, int socketIndex,
        int gemItemId)
        => target.SetEmbeddedId(socketIndex, gemItemId);

    /// <summary>Validation d'un scroll d'enchantement sur sa cible —
    /// <c>UseItemToItemControl::CheckExecute</c> @0x84ecd60 cases
    /// 0x38 (kind 56) et 0x4f (kind 79, enchants de costume) :
    /// <list type="bullet">
    /// <item>cible résolue, classe item ∈{0,1} (arme/armure) et
    /// kind ≠0x51 (œuf) — approché par kind ∈[1,0x28) ici ;</item>
    /// <item>cible kind 0x10 → EquipLoc scroll == 6 ;
    /// cible kind 0x27 → EquipLoc scroll == 0xc ;</item>
    /// <item>paires natives srcEquipLoc→kind : 7→0x13, 0xb→0x14,
    /// 9→0x23 ; EquipLoc ≠0 hors paires → == kind cible ;
    /// EquipLoc ==0 → tout équipement ;</item>
    /// <item>kind 79 : EquipLoc==0 ou == EquipLoc de la cible (la
    /// classe cible 0xd « costume » n'est pas dérivable des kinds —
    /// le match EquipLoc fait office) ;</item>
    /// <item><c>witchcraft</c> déjà = id du scroll → refus (pas de
    /// re-application du même enchant) ;</item>
    /// <item>niveau requis du scroll ≤ niveau du perso — approche du
    /// gate natif (limite = lvl perso si item+0x114≠0 sinon lvl
    /// requis de la cible — champ non exporté).</item>
    /// </list></summary>
    /// <returns>0 si OK, sinon le text-id natif d'erreur
    /// (<see cref="TextCannotEnchant"/> ou
    /// <see cref="TextEnchantLevel"/>).</returns>
    public static int ValidateWitchcraft(ItemParser.ItemDef scrollDef,
        ItemEntity? target, ItemParser.ItemDef? targetDef,
        int charLevel)
    {
        if (target == null || targetDef == null)
            return TextCannotEnchant;
        var k = targetDef.Kind;
        // Classe ∈{0,1} natif (arme/armure) + ≠0x51 — approché par la
        // plage de kinds équipement joueur (même borne que le natif :
        // kind <0x28).
        if (k is < 1 or >= 0x28 or 0x51) return TextCannotEnchant;
        var e = scrollDef.EquipLoc;
        if (scrollDef.Kind == 79)
        {
            // Case 0x4f : EquipLoc==0 (universel) ou == EquipLoc
            // de la pièce de costume ciblée.
            if (e != 0 && e != targetDef.EquipLoc)
                return TextCannotEnchant;
        }
        else
        {
            // Cases spéciales kind cible → EquipLoc exigé.
            if ((k == 0x10 && e != 6) || (k == 0x27 && e != 0xc))
                return TextCannotEnchant;
            var compat = e switch
            {
                7 => k == 0x13,   // EquipLoc 7 → kind 19 (collier)
                0xb => k == 0x14, // EquipLoc 11 → kind 20 (anneau)
                9 => k == 0x23,   // EquipLoc 9 → kind 35 (trophée)
                0 => true,        // EquipLoc 0 = scroll universel
                _ => e == k,      // sinon EquipLoc == kind cible
            };
            if (!compat) return TextCannotEnchant;
        }
        // clone+0x4c == scrollId → le même enchant est déjà posé.
        if (target.Witchcraft == scrollDef.Id) return TextCannotEnchant;
        if (charLevel < scrollDef.RequiredLevel) return TextEnchantLevel;
        return 0;
    }

    /// <summary>Résultat de <see cref="ValidateInlay"/> : Ok + index
    /// de socket choisi, sinon l'id de texte natif à renvoyer.</summary>
    public readonly record struct InlayValidation(
        bool Ok, int SocketIndex, int ErrorTextId);
}
