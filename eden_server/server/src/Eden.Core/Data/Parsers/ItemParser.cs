using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Item.ini — 71941 items (|V.11|78|, NON chiffre).
///
/// Colonnes identifiees:
///   [0]  Item ID
///   [1]  Code item (Q00134...)
///   [3]  Type/groupe (G00005...)
///   [4]  Icone UI (UI012)
///   [7]  Nom (Big5)
///   [8]  Kind/type d'item (CItemData+0x20 — desasm : 0x11-0x1c = armes,
///        0x11/12/13/3d epees, 0x1c arc, 0x1d fusil... utilise par
///        CCharKfm::Integration pour choisir le pool d'anims d'attaque)
///   [11] Flags
///   [13] ?(-1)
///   [14] ?(niveau requis probable — 1 sur les items lv1)
///   [16] ?
///   [17] Prix d'achat (7500)
///   [19] Stack max (5)
///   [29] Flag (1)
///   [30] Flag (1)
///   [31] Prix de vente / mall (2250000)
///   [33] ?(47)
///   [34+] Description (Big5, multi-ligne prefixe $7$)
///   [48] Portee d'attaque (CItemData+0xba — desasm Deserialize ;
///        melee=3, fusil=4, arc=42 ; CCharFormula::AttackRange
///        applique x1.5 → portee effective 63/6/4.5)
///
/// Les descriptions contiennent des CRLF embeddes — IniTable assemble les
/// lignes jusqu'a 78 colonnes.
/// Semantique detaillee a confirmer via CItemData::Read* dans ZoneServer.
/// </summary>
public static class ItemParser
{
    public sealed class ItemDef
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;      // Q00134
        public string TypeCode { get; set; } = string.Empty;  // G00005
        public string IconCode { get; set; } = string.Empty;  // UI012
        public string Name { get; set; } = string.Empty;      // Big5
        /// <summary>Kind/type d'item (col 8 → CItemData+0x20) —
        /// armes = 0x11..0x1e et variants awoken 0x3d..0x42.</summary>
        public int Kind { get; set; }
        public int RequiredLevel { get; set; }
        /// <summary>Niveau maximal d'utilisation (col15 — octet
        /// +0x3f de CItemData ; la sous-struct itemdata+0xfc des
        /// items de travail y lit le cap : ToWork refuse si
        /// char level &gt; MaxLevel, 0 = pas de cap).</summary>
        public int MaxLevel { get; set; }

        /// <summary>Cooldown d'usage en dixièmes de seconde —
        /// S_Item col21 (0-indexé 21) → CItemData+0x58, lu par
        /// <c>UseItemControl::Init</c> @0x84eb2c0 (control+8) puis
        /// armé par <c>CCharacter::AddCoolDownItem</c> @0x84283c0.
        /// L'event natif découpe la valeur en <c>v/10</c> secondes +
        /// reste en µs (ctor @0x8663ac0). 0 = pas de cooldown.</summary>
        public int CooldownDs { get; set; }

        /// <summary>Groupe de cooldown — S_Item col22 (0-indexé 22) →
        /// CItemData+0x5c (control+0xc, passé à
        /// <c>CheckIsCoolDowning</c>/<c>AddCoolDownItem</c>). Les items
        /// du même groupe se bloquent mutuellement ; 0 = groupe
        /// global partagé (comportement natif).</summary>
        public int CooldownGroup { get; set; }
        /// <summary>Flags bas 32 bits de col11 (CItemData+0x30) —
        /// le natif lit col11 comme un long long.</summary>
        public int Flags { get; set; }
        /// <summary>Flags hauts 32 bits de col11 (CItemData+0x34) —
        /// bits de type d'opération : 8=arme, 0x20=armure,
        /// 0x10=accessoire (CheckMagicBoxCosts @0x8088200).</summary>
        public int FlagsHi { get; set; }
        /// <summary>Item « bindable » — bit 7 du byte `CItemData+0x30`
        /// (signe du char lu sur <see cref="Flags"/>) : natif
        /// `Equip` @0x080b5810, `ElfEquip` @0x0808cb20,
        /// `DoUseItem`/`DoUseItemAfterCheck` et `CheckAutoStackItem`
        /// @0x08090410 (déplacement vers conteneurs 1/5/7) appellent
        /// `BindItem` @0x08116870 quand ce bit est levé — l'item
        /// devient lié (clone+0x24 | 0x10, persistance `bind=1`).</summary>
        public bool Bindable => (Flags & 0x80) != 0;
        /// <summary>Col 17 — monnaie alternative (~1000-6000 sur les
        /// items de boutique, vide sinon). Ce n'est PAS le prix or :
        /// l'achat/réparation/identification/revente passent par le
        /// prix système de col 31 (<see cref="SellPrice"/>).</summary>
        public int BuyPrice { get; set; }
        /// <summary>Cap de stack (col 29 0-indexé) — potions 32631/
        /// 32639 → 100, confirmé par `cur_maxdurability`=100 en DB et
        /// le cap lu en `CItemFactory::CheckAutoStackItem`
        /// (CItemData+0xc, désasm @0x8090410). Vide pour les armes
        /// (col29 y porte un autre compteur — ne pas empiler :
        /// EquipLoc>0 disqualifie le stack de toute façon).</summary>
        public int MaxStack { get; set; }
        /// <summary>Durabilité max d'équipement (col 74 0-indexé) —
        /// staff 31926 → 130, épées → 75, identique aux lignes
        /// `cur_maxdurability` de la DB native.</summary>
        public int MaxDurability { get; set; }
        /// <summary>Prix système (col 31 0-indexée →
        /// GameData::CItemData+0x7c float, recopié lapis CItem+0x10 /
        /// CItemAttr+0xc0 — désasm `GetAllNormalItems` @0x835ad80 et
        /// `SetItemAttrData` @0x8358200). C'est LE prix de référence :
        /// achat PNJ = col31×remise, revente = col31×0.05×dur/maxDur,
        /// identification = col31×0.05. 194400 pour une armure shopée
        /// (32378), 35262 pour une arme lv93 (994).</summary>
        public int SellPrice { get; set; }
        /// <summary>Prix de réparation de base (col 76 0-indexée →
        /// GameData::CItemData+0x114, recopié dans lapis CItem+0x14 /
        /// CItemAttr+0xc4 — désasm `GetAllArmors` @0x835cd2a et
        /// `SetItemAttrData` @0x835a5xx). 4 pour une épée lv1
        /// (31899), 5082 pour une arme lv93 (996).</summary>
        public int RepairPrice { get; set; }
        /// <summary>Portee d'attaque brute (col 48 → CItemData+0xb8 →
        /// CItemAttr+0x2c, ×0.5 natif : épée '4'→portée 2.0 —
        /// désasm `SetItemAttrData` @0x8358200 + `AttackRange(CCharacter)`
        /// lisant addattr+0x48).</summary>
        public float AttackRange { get; set; }
        /// <summary>Enchant d'usage (col 23, 0-indexé) — l'id S_Enchant
        /// appliqué par CItemFactory::UseItemParam au clic droit.
        /// Preuve : item 20051 (Flame Soul Puma) → col23=58991 = enchant
        /// `2134|14010|1` (ride) + `2051|40` (+40% vitesse) ; 21272 →
        /// col23=55260. (col24=5 = flag ride/consommable, pas l'enchant.)</summary>
        public int UseEnchantId { get; set; }
        /// <summary>Licence remplacée — `CItemData+0x8c`
        /// (`CItemQuery::Deserialize` @0x86038xx) : après l'octroi
        /// d'une licence, le natif supprime la licence dont l'id est
        /// dans ce champ (`RemoveLicense(itemdata+0x8c)` —
        /// `CPlayerFactory::GetLicense` @0x80fd2xx). La colonne dépend
        /// du kind (c8 → +0x20) : 0x2a/0x52 lisent c35/c36 en +0x12c/
        /// +0x130 (→ +0x8c = c37), 0x45-0x4d lisent une string (→
        /// +0x8c = c36), sinon +0x8c = c35. Les certificats de classe
        /// (kind 40, ex. 33272/34170) l'ont vide → pas de remplacement.
        /// </summary>
        public int LicenseReplaceId { get; set; }
        /// <summary>Loc d'équipement (col 9, 0-indexé) — épée 31899→7,
        /// armure 31939→2, gants 31940→4, bottes 31941→5. 0/absent =
        /// non équipable (monture 20051 : pas de col9).</summary>
        public int EquipLoc { get; set; }
        /// <summary>Masque des types d'entraînement permis (col 10,
        /// 0-indexé → `CItemData+0x2c` natif) : bit k = type d'attribut
        /// k (0=HP…14=P-Healing) sélectionnable dans la Mana Infusion.
        /// 0 = item non entraînable (rejet `SetEquipTrainItem`
        /// @0x80ee6b0). Les types demandés par le client sont validés
        /// contre `mask &amp; 0xffff`.</summary>
        public int TrainMask { get; set; }
        /// <summary>Durée de vie en jours (legacy — voir
        /// <see cref="DurationSeconds"/> pour la formule native).</summary>
        public int DurationDays { get; set; }
        /// <summary>Durée de vie en SECONDES — formule native
        /// <c>CItemQuery::Deserialize</c> @0x8602f80 :
        /// <c>CItemData+0x70 = multiplicateur(col26) × col27</c> où
        /// col26 est l'unité : {1,4}=×60 (minutes), {2,5}=×3600
        /// (heures), {3,6}=×86400 (jours), 0 ou ≥7 → 0 (permanent).
        /// Preuve données : item mall 44025 → col26=3 col27=3 →
        /// 3×86400 = 259200 s (3 jours) et sa ligne DB porte un
        /// due_date futur. Le natif arme <c>item+0x18 = now + durée</c>
        /// au premier déplacement/usage (ItemFactory @0x80901b2 +
        /// DoUseItem @0x80b9a10) puis émet ClientItemRent (461) +
        /// persiste due_date — sans ça le client affiche
        /// "0 sec. remain" et l'item est mort-né.</summary>
        public int DurationSeconds { get; set; }
        /// <summary>Unité de la durée (S_Item col26 → `CItemData+0x6c`
        /// natif) : {1,4}=minutes, {2,5}=heures, {3,6}=jours. Le natif
        /// arme due_date **à la création** quand l'unité ∈ {4,5,6}
        /// (`itemdata+0x6c - 4 < 3` dans `CItemFactory::AddItem*`
        /// @0x80b9xx : montures « 入手限時 » / « N日 » partent en
        /// compte à rebours dès l'obtention) ; les unités {1,2,3}
        /// s'arment au premier usage/déplacement.</summary>
        public int DurationUnit { get; set; }

        /// <summary>Durée de l'effet d'usage (charmes/potions buff) —
        /// S_Item cols 24/25 = unité × quantité, table DIFFÉRENTE de
        /// celle de la location (rent cols 26/27) : 2=minutes,
        /// 3=heures, 4=jours. Preuves : 37013 « EXP Charm I » c24=2
        /// c25=60 → 3600 s (1 h) ; 40026 « Iris Jewel (1 day) » c24=4
        /// c25=1 → 86400 s ; 38632 c24=3 c25=1 → 3600 s. 0 = pas de
        /// durée → effet instantané ou permanent selon la commande.</summary>
        public int UseBuffSeconds { get; set; }
        /// <summary>Tid du NPC spawné par cet item de hall de famille
        /// (col35, kinds 0x2a/0x52 uniquement — proto natif +0x124 ;
        /// 0 sinon). Sert au spawn des NPCs du hall et à la résolution
        /// du housekeeper depuis le template de bâtiment.</summary>
        public int FamilyNpcTid { get; set; }

        // --- Bloc statistique d'équipement -------------------------
        // Mapping prouvé par CItemDBO::SetItemAttrData @0x8358200
        // (désasm zone) + CItemQuery::Deserialize (lecture séquentielle
        // des colonnes ; pour les kinds armes/armures le champ
        // conditionnel de kind 0x29/0x2a/0x45-0x4c/0x4d/0x52 n'est pas
        // consommé). Indices 0-basés de S_Item :
        //   col37→attr+0x04 HP, col38→attr+0x08 MP,
        //   col39→attr+0x0c STR, col40→attr+0x14 DEX(AGI),
        //   col41→attr+0x10 INT, col42→attr+0x18 SPR(WIS),
        //   col43→attr+0x1c LUK(LCK), col45→attr+0x20 P-ATK,
        //   col46→attr+0x24 M-ATK, col50→attr+0x34 DEF.
        // Validation données : épée 31899 p284/m241, baguette 31926
        // p206/m446 (magie > physique ✓), cape 32526 def 87.

        /// <summary>Bonus HP d'équipement (col 37 → CItemAttr+0x04).</summary>
        public int BonusHp { get; set; }
        /// <summary>Bonus MP d'équipement (col 38 → CItemAttr+0x08).</summary>
        public int BonusMp { get; set; }
        /// <summary>Bonus STR (col 39 → CItemAttr+0x0c).</summary>
        public int BonusStr { get; set; }
        /// <summary>Bonus AGI — DEX natif (col 40 → CItemAttr+0x14).</summary>
        public int BonusAgi { get; set; }
        /// <summary>Bonus INT (col 41 → CItemAttr+0x10).</summary>
        public int BonusInt { get; set; }
        /// <summary>Bonus WIS — SPR natif (col 42 → CItemAttr+0x18).</summary>
        public int BonusWis { get; set; }
        /// <summary>Bonus LCK — LUK natif (col 43 → CItemAttr+0x1c).</summary>
        public int BonusLck { get; set; }
        /// <summary>Attaque physique de l'arme (col 45 →
        /// CItemAttr+0x20, lu dans RefreshEQAttr pour kind arme).</summary>
        public int PhysicalAttack { get; set; }
        /// <summary>Attaque magique (col 46 → CItemAttr+0x24).</summary>
        public int MagicAttack { get; set; }
        /// <summary>Variance d'attaque de l'arme en % (col 47 →
        /// CItemAttr+0x28 → addattr+0x44) — multiplicateur
        /// `100 − var + rand[0,2var)` de CalculatePhysicalDamage
        /// @0x83fd8c0. Épée 31899 → 6, staff 31926 → 8.</summary>
        public int PhysicalVariance { get; set; }
        /// <summary>Élément d'attaque de l'arme (col 44 →
        /// CItemData+0xa8 → CWeapon+0x128) — indexe les résistances du
        /// défenseur dans CalculatePhysicalDamage quand non nul
        /// (0 = attaque physique neutre).</summary>
        public int AttackElement { get; set; }
        /// <summary>Vitesse d'attaque de l'arme (col 49 →
        /// CItemData+0xba → CItemAttr+0x30, mise à l'échelle ×1.5 par
        /// `SetItemAttrData` @0x8358200 — épée 31899 '16'→24).</summary>
        public int AttackSpeed { get; set; }
        /// <summary>Défense (col 50 → CItemAttr+0x34 → char+0x3b8).</summary>
        public int Defence { get; set; }
        /// <summary>Précision d'équipement (col 51 → CItemAttr+0x38 →
        /// char+0x3bc ; pour une arme = addattr+0x54 lu dans
        /// PhysicalHitRate).</summary>
        public int Hit { get; set; }
        /// <summary>Esquive d'équipement (col 52 → CItemAttr+0x3a →
        /// char+0x3be).</summary>
        public int Dodge { get; set; }
        /// <summary>Critique d'équipement (col 53 → CItemAttr+0x3c →
        /// char+0x3c0 ; pour une arme = addattr+0x58 lu dans
        /// PhysicalCriticalRate).</summary>
        public int Critical { get; set; }
        /// <summary>Dégâts critiques d'équipement (col 54 →
        /// CItemAttr+0x3e → char+0x3c2).</summary>
        public int CriticalDamage { get; set; }
        /// <summary>Critique magique d'équipement (col 55 →
        /// CItemAttr+0x40 → char+0x3c4 — lu par
        /// `CCharFormula::MagicCriticalRate` @0x83fab80).</summary>
        public int MagicCritical { get; set; }
        /// <summary>Dégâts critiques magiques d'équipement (col 56 →
        /// CItemAttr+0x42 → char+0x3c6 — lu par
        /// `CCharFormula::MagicCriticalDamage` @0x83f97xx).</summary>
        public int MagicCriticalDamage { get; set; }
        /// <summary>Stat d'équipement col 57 → CItemAttr+0x44
        /// (itemdata+0xcc dans SetItemAttrData @0x83582xx). Seule
        /// lecture connue : <c>CElfPet::RefreshEQsAttr</c> @0x845a230
        /// la somme dans pet+0x464 → 418 ClientElfUpdateAttr type 11
        /// (4ᵉ stat du panneau familier — « vitesse » d'après le
        /// layout client). Toutes les données actuelles l'ont vide →
        /// 0 ; le port reste fidèle si du contenu la renseigne.</summary>
        public int PetEquipStat { get; set; }
        /// <summary>Résistances élémentaires (cols 63-71 →
        /// CItemAttr+0x50..+0x5e, 9 s16) — agrégées dans
        /// char+0x3d4..+0x3e2 par RefreshEQBonus ; lues par le vtable
        /// +0xe8 (AttrResist) dans les dégâts élémentaires.</summary>
        public int[] Resistances { get; set; } = System.Array.Empty<int>();
        /// <summary>Sort enseigné par les livres (col 35 →
        /// CSpellBookItem+0x138 = puVar3[0x23], désasm zone
        /// @0x835b446) — kinds 22 (CSpellBookItem) et 25
        /// (CSpellLvUpBookItem) ; 0 pour tout autre item.</summary>
        public int LearnSpellId { get; set; }
        /// <summary>Fonction chemin d'éveil (CDestinyItem+0x138 natif)
        /// — lue par `CCharacter::UseDestinyRoadItem` @0x08422520 pour
        /// les items kind 0x18. Le parseur S_Item natif initialise ces
        /// champs à 0 et aucune donnée ne les renseigne dans ce build
        /// (zone @0x8603bf, épilogue de CItemQuery::Deserialize) —
        /// restent 0, fidèle au natif.</summary>
        public int DestinyFunc { get; set; }
        /// <summary>CDestinyItem+0x13a natif — param2 de la fonction.</summary>
        public int DestinyParam2 { get; set; }
        /// <summary>CDestinyItem+0x13c natif — param3 de la fonction.</summary>
        public int DestinyParam3 { get; set; }
        /// <summary>Type d'usage (col 12 → CItemData+0x38, comptage
        /// CItemQuery::Deserialize @0x8602f80 vérifié token par token).
        /// Dispatch natif CItemFactory::UseItemParam @0x80b9490 :
        /// 1/2 = life-item (consommable), 3/5 = UseItemToItemControl
        /// (item-sur-item : les 3 s16 finaux du wire 19 portent la
        /// cible), 4 = rejeté, 6 = elf-item. Gemmes kind 45 → 5.</summary>
        public int UseType { get; set; }
        /// <summary>Niveau de la gemme (col 35 → CItemData+0x8c →
        /// CAddAttrItem+0x124) — colonne partagée avec
        /// <see cref="LearnSpellId"/> (le même champ itemdata est lu
        /// comme spell-id par les livres et comme niveau par les
        /// gemmes). Comparé au niveau requis de la cible dans
        /// UseItemToItemControl::CheckExecute case 0x2d (erreur 0xcce
        /// = texte 3278 « Equipment level too low »). Toutes les
        /// gemmes actuelles ont col35 vide → 0.</summary>
        public int GemLevel => LearnSpellId;
        /// <summary>Nombre max de sockets de l'équipement (col 72 →
        /// CItemData+0xf6 octet bas → lapis::CItem+0x12c armes /
        /// +0x126 armures, désasm init CWeapon @0x835c7xx).
        /// 0 = jamais socketable ; valeurs réelles 1-6.</summary>
        public int MaxSocket { get; set; }
        /// <summary>Taux d'ouverture par socket (col 73 →
        /// CItemData+0xf7 octet → lapis::CItem+0x12d/+0x127) — pourcent
        /// lancé par `random()%100` dans
        /// CItemFactory::ChangeEmbeddedAmount @0x809baf0 ; valeurs
        /// réelles 30/50/70.</summary>
        public int SocketRate { get; set; }
        /// <summary>Flags de restriction A (col 69 0-indexée →
        /// CItemData+0xf0) — testés en bitmask dans
        /// `CNE_CZ_ZoneServerFamilyStoragePutItem::Execute` @0x08258a00 :
        /// bits 3|4 (0x18) → dépôt en coffre de famille refusé, texte
        /// 0x7eb. Valeurs observées dans S_Item : 1/2/4/8/15.</summary>
        public int RestrictFlags69 { get; set; }
        /// <summary>Flags de restriction B (col 70 0-indexée →
        /// CItemData+0xf2) — bit 0 → dépôt en coffre de famille refusé,
        /// texte 0x20a3 (même désasm).</summary>
        public int RestrictFlags70 { get; set; }
        /// <summary>Qualité/couleur de base de l'item (col 19 0-indexé
        /// → CItemData+0x50, désasm CItemQuery::Deserialize @0x8602xxx).
        /// Utilisée par PickAColor du cube comme qualité par défaut et
        /// pour initialiser clone+0x2c (qualité d'instance).</summary>
        public int Quality { get; set; }
        /// <summary>Capacité en slots d'un item sac (col 28 →
        /// CItemData+0x74 octet → CCloneItem+0xc sur les kind 43,
        /// désasm `CItemDBO::GetAllBagItems` et
        /// `CPlayerFactory::SwitchBagFromContainer` qui lit clone+0xc
        /// comme taille du sac inv créé). Données : 33734→4,
        /// 32649→8, 40030→5, 41261→20, 44981→30.</summary>
        public int BagCapacity { get; set; }
        /// <summary>Nodes où l'item est utilisable (col 32, partie
        /// gauche du `;` — liste ` ` séparée, ex. "501 502;60" →
        /// {501,502}) — restriction d'aire des outils de récolte
        /// kind 44 (pioches/gants/boules de divination).</summary>
        public int[] UseAreaNodeIds { get; set; } =
            System.Array.Empty<int>();
        /// <summary>Sous-type d'aire requis (col 32, partie droite du
        /// `;` — keyId du gateway_area natif : 60 = mine de cristal,
        /// 61 = champ, 62 = champ d'énergie — villes de famille).</summary>
        public int UseAreaSubType { get; set; }
        /// <summary>Ids des groupes cube auxquels l'item appartient
        /// (col 75 0-indexé, liste `;` → CItemData+0x104 set&lt;int&gt;,
        /// désasm CItemQuery::Deserialize — ex. 35672 → 120770).
        /// CItemFactory::IsCubeGroupValid et LoadCubeGroup2Item en font
        /// l'index inversé groupe→items pour le pool de produits.</summary>
        public int[] CubeGroupIds { get; set; } = System.Array.Empty<int>();

        public string Description { get; set; } = string.Empty;
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    public static List<ItemDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var items = new List<ItemDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;

            items.Add(new ItemDef
            {
                Id = id,
                Code = row.Length > 1 ? row[1] : string.Empty,
                TypeCode = row.Length > 3 ? row[3] : string.Empty,
                IconCode = row.Length > 4 ? row[4] : string.Empty,
                Name = row.Length > 7 ? row[7] : string.Empty,
                Kind = IniTable.GetInt(row, 8),
                RequiredLevel = IniTable.GetInt(row, 14),
                MaxLevel = IniTable.GetInt(row, 15),
                Flags = unchecked((int)IniTable.GetLong(row, 11)),
                FlagsHi = unchecked((int)(IniTable.GetLong(row, 11) >> 32)),
                BuyPrice = IniTable.GetInt(row, 17),
                MaxStack = IniTable.GetInt(row, 29),
                // Durabilité d'équipement = col74 seule ; col29 est le
                // cap de pile (jamais une durabilité).
                MaxDurability = IniTable.GetInt(row, 74),
                SellPrice = IniTable.GetInt(row, 31),
                RepairPrice = IniTable.GetInt(row, 76),
                RestrictFlags69 = IniTable.GetInt(row, 69),
                RestrictFlags70 = IniTable.GetInt(row, 70),
                UseEnchantId = IniTable.GetInt(row, 23),
                LicenseReplaceId = IniTable.GetInt(row,
                    LicenseReplaceColumn(IniTable.GetInt(row, 8))),
                EquipLoc = IniTable.GetInt(row, 9),
                // col10 → CItemData+0x2c : bitmask u32 des types
                // d'entraînement autorisés (SetEquipTrainItem natif).
                TrainMask = IniTable.GetInt(row, 10),
                DurationDays = Math.Max(IniTable.GetInt(row, 26),
                    IniTable.GetInt(row, 27)),
                DurationUnit = IniTable.GetInt(row, 26),
                // Col26 = unité (mask : bits 1|4→min, 2|5→h,
                // 3|6→jour), col27 = nombre de ces unités —
                // formule native du destructeur "iVar6 × local_30".
                DurationSeconds = ParseDurationSeconds(row),
                UseBuffSeconds = ParseUseBuffSeconds(row),
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
                AttackSpeed = IniTable.GetInt(row, 49),
                Defence = IniTable.GetInt(row, 50),
                Hit = IniTable.GetInt(row, 51),
                Dodge = IniTable.GetInt(row, 52),
                Critical = IniTable.GetInt(row, 53),
                CriticalDamage = IniTable.GetInt(row, 54),
                MagicCritical = IniTable.GetInt(row, 55),
                MagicCriticalDamage = IniTable.GetInt(row, 56),
                PetEquipStat = IniTable.GetInt(row, 57),
                Resistances = ParseStats(row, 63),
                LearnSpellId = IniTable.GetInt(row, 35),
                UseType = IniTable.GetInt(row, 12),
                MaxSocket = IniTable.GetInt(row, 72),
                SocketRate = IniTable.GetInt(row, 73),
                Quality = 0,
                // col20/21 (0-indexé 19/20) = cooldown time/group
                // (CItemData+0x58/+0x5c) — S_Item n'a PAS de qualité
                // par définition : la qualité est par instance
                // (clone+0x2c), l'ancien mapping lisait le cooldown.
                CooldownDs = IniTable.GetInt(row, 21),
                CooldownGroup = IniTable.GetInt(row, 22),
                BagCapacity = IniTable.GetInt(row, 28),
                // col32 — restriction d'aire des outils kind 44 :
                // "501 502;60" = nodes utilisables + keyId de
                // l'event_area (60 mine, 61 farm, 62 énergie).
                UseAreaNodeIds = ParseUseAreaNodes(
                    IniTable.GetStr(row, 32), out var uaSub),
                UseAreaSubType = uaSub,
                CubeGroupIds = ParseIdList(IniTable.GetStr(row, 75)),
                AttackRange = float.TryParse(IniTable.GetStr(row, 48),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ar) ? ar : 0,
                Description = row.Length > 34 ? row[34] : string.Empty,
                // Kinds 0x2a/0x52 (items NPC de hall de famille) : la
                // col35 porte le tid du NPC à spawner — items 34678
                // (commissaire-priseur)→53003, 35001 (assistant
                // familial)→53007. Lu par l'équivalent natif de
                // CNpcItem::GetNpcTid (proto+0x124, désasm
                // CItemFactory::FamilyBuildingPutItemFromInventroy
                // @0x808a770).
                FamilyNpcTid = IniTable.GetInt(row, 8) is 0x2a or 0x52
                    ? IniTable.GetInt(row, 35) : 0,
                Raw = row,
            });
        }

        return items;
    }

    /// <summary>
    /// Calcule la durée de vie d'un item en secondes selon la formule
    /// native <c>CItemQuery::Deserialize</c> @0x8602f80 :
    /// unit = col26, count = col27, durée = mult(unit) × count où
    /// mult = 60 si unit∈{1,4} (bits 0x12), 3600 si unit∈{2,5}
    /// (bits 0x24), 86400 si unit∈{3,6} (bits 0x48), 0 sinon.
    /// </summary>
    /// <summary>Colonne de `CItemData+0x8c` (licence remplacée) selon
    /// le kind — branche conditionnelle du désérialiseur natif
    /// (`CItemQuery::Deserialize` @0x86038xx) : kinds 0x2a/0x52 → 2
    /// cellules extra (+0x12c/+0x130), 0x45-0x4d → 1 cellule string
    /// (+0x168) ou s16 (+0x174 pour 0x4d).</summary>
    private static int LicenseReplaceColumn(int kind) =>
        kind is 0x2a or 0x52 ? 37 :
        kind is >= 0x45 and <= 0x4d ? 36 : 35;

    private static int ParseDurationSeconds(string[] row)
    {
        var unit = IniTable.GetInt(row, 26);
        var count = IniTable.GetInt(row, 27);
        if (unit <= 0 || unit >= 7 || count <= 0) return 0;
        var bit = 1 << unit;
        int mult;
        if ((bit & 0x48) != 0) mult = 86400;
        else if ((bit & 0x24) != 0) mult = 3600;
        else if ((bit & 0x12) != 0) mult = 60;
        else return 0;
        return mult * count;
    }

    /// <summary>Cols 24/25 — durée de l'enchant d'usage (unité 2=min,
    /// 3=h, 4=jour — cf. <see cref="ItemDef.UseBuffSeconds"/>).</summary>
    private static int ParseUseBuffSeconds(string[] row)
    {
        var unit = IniTable.GetInt(row, 24);
        var count = IniTable.GetInt(row, 25);
        if (unit <= 0 || count <= 0) return 0;
        var mult = unit switch { 2 => 60, 3 => 3600, 4 => 86400, _ => 0 };
        return mult * count;
    }

    /// <summary>Lit un bloc de `count` entiers consécutifs depuis
    /// `startCol` (résistances cols63-71 → CItemAttr+0x50..+0x5e,
    /// désasm SetItemAttrData) — les cases vides lisent 0 comme le
    /// strtol natif.</summary>
    private static int[] ParseStats(string[] row, int startCol,
        int count = 9)
    {
        var v = new int[count];
        for (var i = 0; i < count; i++)
            v[i] = IniTable.GetInt(row, startCol + i);
        return v;
    }

    /// <summary>Col 32 des outils de récolte : "nodeIds…;areaType"
    /// — retourne les ids de nodes utilisables (partie gauche) et
    /// sort le keyId d'aire requis (partie droite, 0 = partout).</summary>
    private static int[] ParseUseAreaNodes(string raw, out int subType)
    {
        subType = 0;
        if (string.IsNullOrWhiteSpace(raw)) return System.Array.Empty<int>();
        var parts = raw.Split(';');
        if (parts.Length > 1)
            int.TryParse(parts[1].Trim().Split(' ')[0], out subType);
        var ids = new List<int>();
        foreach (var tok in parts[0].Split(' ',
            System.StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(tok, out var v) && v != 0) ids.Add(v);
        return ids.ToArray();
    }

    /// <summary>Éclate une liste d'entiers `;`-séparée (col 75 groupes
    /// cube) — même convention que CInTextStream/StrToken natif :
    /// strtol par segment, les vides ignorés.</summary>
    private static int[] ParseIdList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return System.Array.Empty<int>();
        var parts = raw.Split(';');
        var ids = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            // strtol natif : tronque au premier non-chiffre
            // ("501 502" → 501) — reproduit fidèlement.
            if (int.TryParse(p.Trim().Split(' ')[0],
                    out var v) && v != 0)
                ids.Add(v);
        }
        return ids.ToArray();
    }
}
