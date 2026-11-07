namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/c_item.ini</c> — base d'items du client (~71k entrées,
/// <c>|V.11|78|</c> — même layout que S_Item.ini serveur).
///
/// Sous-ensemble porté pour les besoins client : codes (modèle/icône),
/// nom, description (tooltips), équipabilité, stats d'arme/armure pour
/// l'affichage. Spécification serveur : <c>ItemParser</c>
/// (<c>$EDEN/server/.../Parsers/ItemParser.cs</c>) — indices de colonnes
/// prouvés par <c>CItemQuery::Deserialize</c> @0x8602f80 et
/// <c>CItemDBO::SetItemAttrData</c> @0x8358200 (zone désasm), repris
/// tels quels.
///
/// Noms/descriptions anglais : croiser <see cref="TextTable"/> sur
/// t_item.ini (col0 = même id).
/// </summary>
public static class ItemTable
{
    /// <summary>Un item depuis c_item.ini.</summary>
    public sealed class ItemDef
    {
        /// <summary>Col 0 — id d'item.</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — code item (Q00134…).</summary>
        public string Code { get; init; } = string.Empty;
        /// <summary>Col 2 — code modèle/variante (souvent vide).</summary>
        public string ModelCode { get; init; } = string.Empty;
        /// <summary>Col 3 — type/groupe (G00005…).</summary>
        public string TypeCode { get; init; } = string.Empty;
        /// <summary>Col 4 — code icône UI (UI012…) — indexe les
        /// imagesets/textures d'inventaire.</summary>
        public string IconCode { get; init; } = string.Empty;
        /// <summary>Col 7 — nom Big5.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 8 — kind/type d'item (CItemData+0x20) :
        /// 0x11-0x1e = armes, 0x3d-0x42 = armes awoken, 0x2a/0x52 =
        /// items NPC hall, 40 = certificat de classe…</summary>
        public int Kind { get; init; }
        /// <summary>Col 9 — loc d'équipement (épée→7, armure→2, gants→4,
        /// bottes→5 ; 0 = non équipable) — jointure vers
        /// <see cref="EquipTypeTable"/>.</summary>
        public int EquipLoc { get; init; }
        /// <summary>Col 10 — masque des types d'entraînement (Mana
        /// Infusion : bit k = attribut k, 0=HP…14=P-Healing —
        /// <c>SetEquipTrainItem</c> @0x80ee6b0). ⚠️ PAS une restriction de
        /// classe : Blazing Jet = 3580 (0xDFC) alors qu'il est
        /// équipable par toute classe d'artilleur.</summary>
        public int TrainMask { get; init; }
        /// <summary>Col 12 — type d'usage (CItemData+0x38) : 1/2
        /// consommable, 3/5 item-sur-item, 6 elf-item.</summary>
        public int UseType { get; init; }
        /// <summary>Groupe de cooldown d'usage (col22 — clé du 368 kind 0).</summary>
        public int CooldownGroup { get; init; }
        /// <summary>Durée du cooldown d'usage en 1/10 s (col21 ; 0 = aucun).</summary>
        public int CooldownDs { get; init; }
        /// <summary>Col 20 — durée de canalisation d'usage en 1/10 s
        /// (CItemData+0x54 natif ; ≠0 → usage différé via 407
        /// ClientUseItemTime + barre de cast, annulé par 408).</summary>
        public int UseTimeDs { get; init; }
        /// <summary>Col 14 — niveau requis.</summary>
        public int RequiredLevel { get; init; }
        /// <summary>Col 28 — capacité offerte quand l'item est un sac
        /// d'extension équipé (kind 43 ; 0 sinon).</summary>
        public int BagCapacity { get; init; }
        /// <summary>Col 15 — niveau max d'utilisation (0 = pas de cap).</summary>
        public int MaxLevel { get; init; }
        /// <summary>Col 17 — monnaie alternative (~1000-6000 boutique —
        /// PAS le prix or ; voir <see cref="SellPrice"/>).</summary>
        public int BuyPrice { get; init; }
        /// <summary>Col 29 — cap de stack (potions → 100 ; vide = autre
        /// compteur sur les armes, ne pas empiler).</summary>
        public int MaxStack { get; init; }
        /// <summary>Col 74 — durabilité max
        /// d'équipement (épées → 75, staff → 130).</summary>
        public int MaxDurability { get; init; }
        /// <summary>Col 31 — prix système (achat PNJ ×remise, revente
        /// ×0.05, identification ×0.05).</summary>
        public int SellPrice { get; init; }
        /// <summary>Col 76 — prix de réparation de base.</summary>
        public int RepairPrice { get; init; }
        /// <summary>Col 34 — description Big5 (multi-lignes, préfixes
        /// $N$ couleur).</summary>
        public string Description { get; init; } = string.Empty;

        // --- Bloc stats équipement (mapping SetItemAttrData natif) ---
        /// <summary>Col 37 — bonus HP.</summary>
        public int BonusHp { get; init; }
        /// <summary>Col 38 — bonus MP.</summary>
        public int BonusMp { get; init; }
        /// <summary>Col 39 — bonus STR.</summary>
        public int BonusStr { get; init; }
        /// <summary>Col 40 — bonus AGI (DEX natif).</summary>
        public int BonusAgi { get; init; }
        /// <summary>Col 41 — bonus INT.</summary>
        public int BonusInt { get; init; }
        /// <summary>Col 42 — bonus WIS (SPR natif).</summary>
        public int BonusWis { get; init; }
        /// <summary>Col 43 — bonus LCK (LUK natif).</summary>
        public int BonusLck { get; init; }
        /// <summary>Col 44 — élément d'attaque de l'arme.</summary>
        public int AttackElement { get; init; }
        /// <summary>Col 45 — attaque physique.</summary>
        public int PhysicalAttack { get; init; }
        /// <summary>Col 46 — attaque magique.</summary>
        public int MagicAttack { get; init; }
        /// <summary>Col 47 — variance d'attaque en %.</summary>
        public int PhysicalVariance { get; init; }
        /// <summary>Col 48 — portée d'attaque brute (×0.5 natif).</summary>
        public float AttackRange { get; init; }
        /// <summary>Col 49 — vitesse d'attaque (×1.5 natif).</summary>
        public int AttackSpeed { get; init; }
        /// <summary>Col 50 — défense.</summary>
        public int Defence { get; init; }
        /// <summary>Col 51 — précision.</summary>
        public int Hit { get; init; }
        /// <summary>Col 52 — esquive.</summary>
        public int Dodge { get; init; }
        /// <summary>Col 53 — critique.</summary>
        public int Critical { get; init; }
        /// <summary>Col 54 — dégâts critiques (CRIT DMG).</summary>
        public int CriticalDamage { get; init; }
        /// <summary>Col 55 — critique magique (M-CRIT Rate).</summary>
        public int MagicCritical { get; init; }
        /// <summary>Col 56 — dégâts critiques magiques (M-CRIT DMG).</summary>
        public int MagicCriticalDamage { get; init; }
        /// <summary>Cols 57-62 — bonus ATK SPD, Cast SPD, G-Healing,
        /// P-Healing, Parry, Block : ordre de la struct d'affichage native
        /// (désasm @0x00898d70 +0x7c…+0x8a) ; Parry = col 61 (épées) et
        /// Block = col 62 (boucliers) recoupés sur les données.</summary>
        public int AtkSpeedBonus { get; init; }
        public int CastSpeed { get; init; }
        public int GHealing { get; init; }
        public int PHealing { get; init; }
        public int Parry { get; init; }
        public int Block { get; init; }
        /// <summary>Col 72 — nombre max de sockets (0 = jamais).</summary>
        public int MaxSocket { get; init; }
        /// <summary>Cols 63-71 — résistances élémentaires (9 valeurs), ordre
        /// natif Slash, Strike, Pierce, Fire, Ice, Lightning, Nature, Holy,
        /// Dark (cf. <c>ItemTipFormat.ResistNames</c>).</summary>
        public int[] Resistances { get; init; } = Array.Empty<int>();
        /// <summary>Col 19 — qualité/rareté (CItemData+0x50 ; 0 = aucune).</summary>
        public int Quality { get; init; }
        /// <summary>Col 11 (bas 32 bits, CItemData+0x30) — bit 0x80 =
        /// l'item se lie au porteur (BindItem natif).</summary>
        public long Flags { get; init; }
        /// <summary>Col 23 — enchant appliqué à l'usage
        /// (CItemData+0x60 ; certificats et cristaux 38xxx).</summary>
        public int UseEnchantId { get; init; }
        /// <summary>Cellules brutes de la ligne.</summary>
        public string[] Raw { get; init; } = Array.Empty<string>();
    }

    /// <summary>Charge c_item.ini → id → définition.</summary>
    public static Dictionary<int, ItemDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, ItemDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id))
                continue;

            var res = new int[9];
            for (var i = 0; i < 9; i++)
                res[i] = IniTable.GetInt(row, 63 + i);

            dict[id] = new ItemDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                ModelCode = IniTable.GetStr(row, 2),
                TypeCode = IniTable.GetStr(row, 3),
                IconCode = IniTable.GetStr(row, 4),
                Name = IniTable.GetStr(row, 7),
                Kind = IniTable.GetInt(row, 8),
                EquipLoc = IniTable.GetInt(row, 9),
                TrainMask = IniTable.GetInt(row, 10),
                UseType = IniTable.GetInt(row, 12),
                CooldownGroup = IniTable.GetInt(row, 22),
                CooldownDs = IniTable.GetInt(row, 21),
                UseTimeDs = IniTable.GetInt(row, 20),
                RequiredLevel = IniTable.GetInt(row, 14),
                BagCapacity = IniTable.GetInt(row, 28),
                MaxLevel = IniTable.GetInt(row, 15),
                BuyPrice = IniTable.GetInt(row, 17),
                MaxStack = IniTable.GetInt(row, 29),
                // Durabilité d'ÉQUIPEMENT = col74 uniquement. col29 est le
                // cap de pile (potions 100, ressources 999) : l'ancien
                // repli col29 faisait afficher une « durabilité » 100/999
                // sur les consommables/ressources (confusion durabilité/
                // quantité).
                MaxDurability = IniTable.GetInt(row, 74),
                SellPrice = IniTable.GetInt(row, 31),
                RepairPrice = IniTable.GetInt(row, 76),
                Description = IniTable.GetStr(row, 34),
                BonusHp = IniTable.GetInt(row, 37),
                BonusMp = IniTable.GetInt(row, 38),
                BonusStr = IniTable.GetInt(row, 39),
                BonusAgi = IniTable.GetInt(row, 40),
                BonusInt = IniTable.GetInt(row, 41),
                BonusWis = IniTable.GetInt(row, 42),
                BonusLck = IniTable.GetInt(row, 43),
                AttackElement = IniTable.GetInt(row, 44),
                PhysicalAttack = IniTable.GetInt(row, 45),
                MagicAttack = IniTable.GetInt(row, 46),
                PhysicalVariance = IniTable.GetInt(row, 47),
                AttackRange = IniTable.GetFloat(row, 48),
                AttackSpeed = IniTable.GetInt(row, 49),
                Defence = IniTable.GetInt(row, 50),
                Hit = IniTable.GetInt(row, 51),
                Dodge = IniTable.GetInt(row, 52),
                Critical = IniTable.GetInt(row, 53),
                CriticalDamage = IniTable.GetInt(row, 54),
                MagicCritical = IniTable.GetInt(row, 55),
                MagicCriticalDamage = IniTable.GetInt(row, 56),
                AtkSpeedBonus = IniTable.GetInt(row, 57),
                CastSpeed = IniTable.GetInt(row, 58),
                GHealing = IniTable.GetInt(row, 59),
                PHealing = IniTable.GetInt(row, 60),
                Parry = IniTable.GetInt(row, 61),
                Block = IniTable.GetInt(row, 62),
                MaxSocket = IniTable.GetInt(row, 72),
                Quality = IniTable.GetInt(row, 19),
                Flags = long.TryParse(IniTable.GetStr(row, 11), out var fl)
                    ? fl & 0xFFFFFFFFL : 0,
                UseEnchantId = IniTable.GetInt(row, 23),
                Resistances = res,
            };
        }
        return dict;
    }
    private static readonly object _cacheLock = new();
    private static Dictionary<int, ItemDef>? _cache;

    /// <summary>Définition c_item d'un item (table chargée une fois, partagée) ;
    /// null si absente ou si c_item.ini n'est pas résolu.</summary>
    public static ItemDef? Lookup(int itemId)
    {
        if (_cache == null)
            lock (_cacheLock)
            {
                if (_cache == null)
                {
                    var p = TextResolver.ResolveFile?.Invoke("data/db/c_item.ini");
                    _cache = p != null ? Load(p) : new();
                }
            }
        return _cache.TryGetValue(itemId, out var d) ? d : null;
    }

    private static Dictionary<string, int>? _byName;

    /// <summary>Id d'un item par son nom c_item (Big5), premier
    /// trouvé ; -1 si inconnu. Sert à retraduire les noms bruts que le
    /// serveur glisse dans les messages système.</summary>
    public static int FindIdByName(string name)
    {
        if (_byName == null)
        {
            Lookup(0);
            var d = new Dictionary<string, int>();
            foreach (var (id, def) in _cache!)
                if (def.Name.Length > 0) d.TryAdd(def.Name, id);
            _byName = d;
        }
        return _byName.TryGetValue(name, out var v) ? v : -1;
    }
}
