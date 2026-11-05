namespace EdenClient.Data.Tables;

/// <summary>
/// Arbre des catégories de l'archive — spécification native extraite du
/// binaire client (rf_game/client_game @0x00fe58b0 dans
/// <c>FUN_00848db0</c>, init de <c>collectionWnd_Ver2</c>) :
/// <list type="bullet">
/// <item><c>DAT_00fe58b0</c> = 4 ids textindex de groupes
/// (9237-9240 : 外觀/武器/防具/特殊物品, T_TextIndex EN
/// « Appearance / Weapon / Armor / Special Items ») ;</item>
/// <item><c>DAT_00fe58c0</c> = nb de feuilles par groupe
/// (4, 18, 6, 4) ;</item>
/// <item><c>DAT_00fe58d0</c> = décalages dans la table des feuilles
/// (0, 4, 22, 28) ;</item>
/// <item><c>DAT_00fe58e0</c> = 32 ids t_textindex des feuilles, dans
/// l'ordre d'affichage.</item>
/// </list>
///
/// La catégorisation des cartes se fait par <b>l'onglet</b>
/// (<see cref="CollectionTable.Card.Tab"/> = col3 de c_collection —
/// le natif compte dans 32 slots indexés par <c>tab-1</c>,
/// FUN_00846920), <b>pas</b> par le kind/EquipLoc de c_item : ce
/// dernier donnait des libellés ordinaires (« Legs », « Hands »…)
/// sur des cartes costume, monture ou familier qui n'ont rien à y
/// faire. La correspondance feuille↔onglet ci-dessous est vérifiée
/// sur corpus/fr-full : tab6..22 = kinds 17..34 par type d'arme,
/// tab31 = kind 30 (fusille), tab5 = montures, tab29 = familiers,
/// tab30 = Boîte Mystique.
/// </summary>
public static class CollectionTree
{
    /// <summary>Feuille native de l'arbre : index de groupe, id
    /// t_textindex du libellé, onglet c_collection.</summary>
    public readonly record struct Leaf(int Group, int TextIndex, int Tab,
        string EnName);

    /// <summary>Noms de groupe canoniques (T_TextIndex 9237-9240 du
    /// client classique — absents de la table FR, voir
    /// <see cref="GroupTextIndex"/> pour les libellés affichés).</summary>
    public static readonly string[] GroupEnNames =
        { "Appearance", "Weapon", "Armor", "Special Items" };

    /// <summary>Libellés FR des groupes par t_textindex (les ids natifs
    /// 9237-9240 n'existent pas dans t_textindex FR — équivalents de
    /// sens identique : 3109 « Apparence », 1718 « Armes »,
    /// 6374 « Armure », 3928 « Objets Spéciaux »).</summary>
    public static readonly int[] GroupTextIndex = { 3109, 1718, 6374, 3928 };

    /// <summary>Les 32 feuilles, dans l'ordre natif d'affichage
    /// (<c>DAT_00fe58e0</c>). Onglet cible déduit des ids/textes et
    /// vérifié contre les kinds c_item du corpus FR.</summary>
    public static readonly Leaf[] Leaves =
    {
        // -- Groupe 0 « Appearance » : costumes ----------------------
        new(0, 1715, 1, "Head Gear"),   // Coiffe          (kind68 loc1)
        new(0, 1716, 2, "Back Gear"),   // Équipement Dos  (kind68 loc6)
        new(0, 1717, 3, "Body Gear"),   // Plastron        (kind68 loc2)
        new(0, 1718, 4, "Weapons"),     // Armes           (kinds 61-67)
        // -- Groupe 1 « Weapon » : 18 types d'armes -----------------
        new(1, 1720, 6, "Daggers"),          // Poignards     kind17
        new(1, 1721, 7, "Rapiers"),          // Rapières      kind18
        new(1, 1722, 8, "1H Swords"),        // Épées à 1main kind19
        new(1, 1723, 9, "Maces"),            // Masses        kind24
        new(1, 1724, 10, "Cestus"),          // Griffes       kind25
        new(1, 2757, 11, "Clubs"),           // Gourdin       kind26
        new(1, 2758, 12, "Katanas"),         // Katana        kind20
        new(1, 2759, 13, "Greatswords"),     // Épée Coloss.  kind21
        new(1, 2760, 14, "Axes"),            // Hache         kind22
        new(1, 5465, 15, "Pikes"),           // Lances        kind23
        new(1, 2761, 16, "Hammers"),         // Marteau       kind27
        new(1, 2870, 17, "Bows"),            // Arc           kind28
        new(1, 2871, 18, "Artillery Guns"),  // Artillerie    kind29
        new(1, 2872, 19, "Guitars"),         // Guitare       kind31
        new(1, 2873, 20, "Grimoires"),       // Grimoire      kind33
        new(1, 2874, 21, "Staff"),           // Bâton         kind32
        new(1, 2875, 22, "Shields"),         // Bouclier      kind34
        new(1, 8155, 31, "Sickleshot"),      // Fusille       kind30
        // -- Groupe 2 « Armor » : armures + accessoires -------------
        new(2, 2876, 23, "Heavy Armors"),  // Armure Lourde   kinds 1/4/7/10/13
        new(2, 2877, 24, "Light Armors"),  // Armure Légère   kinds 2/5/8/11/14
        new(2, 2878, 25, "Cloth Armors"),  // Armure Protect. kinds 3/6/9/12/15
        new(2, 2879, 26, "Accessories"),   // Accessoires     kinds 37/38
        new(2, 2880, 27, "Capes"),         // Cape            kind16 loc6
        new(2, 2881, 28, "Trophies"),      // Trophée         kind39 loc12
        // -- Groupe 3 « Special Items » ------------------------------
        new(3, 1719, 5, "Mounts"),         // Montures   (kind44, tab5)
        new(3, 5426, 29, "Pets"),          // Familier   (kind60 + divers)
        new(3, 5520, 30, "Arcane Box"),    // Boîte Mystique (tab30)
        new(3, 2886, 32, "Other Items"),   // Autres objets (tab32 — vide FR)
    };

    /// <summary>Onglet → feuille native.</summary>
    private static readonly Dictionary<int, Leaf> _byTab =
        BuildIndex();

    private static Dictionary<int, Leaf> BuildIndex()
    {
        var d = new Dictionary<int, Leaf>(Leaves.Length);
        foreach (var l in Leaves) d[l.Tab] = l;
        return d;
    }

    /// <summary>Feuille native d'un onglet c_collection, null si
    /// l'onglet n'a pas de feuille dédiée.</summary>
    public static Leaf? OfTab(int tab) =>
        _byTab.TryGetValue(tab, out var l) ? l : null;

    /// <summary>Groupe (0-3) d'un onglet ; les onglets inconnus
    /// retombent sur le groupe « Special Items » comme les items
    /// sans catégorie dédiée.</summary>
    public static int GroupOf(int tab) => OfTab(tab)?.Group ?? 3;

    /// <summary>Clé de catégorie stable « g{groupe}|t{onglet} » —
    /// indépendante de la langue ; un onglet sans feuille dédiée
    /// est rangé dans la feuille « Autres objets » (tab32).</summary>
    public static string KeyOf(int tab)
    {
        var l = OfTab(tab) ?? Leaves[^1];   // « Other Items » (tab32)
        return $"g{l.Group}|t{l.Tab}";
    }
}
