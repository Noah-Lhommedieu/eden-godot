using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Wiki complet des champs de <b>S_Item.ini</b> (78 col,
/// ItemParser.cs). Chaque entrée couvre l'utilité, TOUTES les
/// valeurs connues, les recettes d'usage et les tables liées. La
/// fenêtre complète automatiquement avec les valeurs réellement
/// distribuées (compteurs + exemples).</summary>
public static partial class FieldHelpCatalog
{
    private static void RegisterItems()
    {
        var t = Items["S_Item.ini"] = new();

        // ---------------- Identity ----------------
        t[0] = E("Unique item id. Referenced by drop tables " +
            "(S_DropItem rows keyed by this id), NPC shops " +
            "(S_Store), quests and reward mails. Renumbering an " +
            "item means updating every table that mentions it.",
            recipe: "To add an item: pick a free id in the same " +
                "family range as similar items, then add its EN " +
                "label in the client's t_item.ini.",
            seeAlso: SA("S_DropItem.ini", "S_Store.ini", "t_item.ini (client EN names)"));
        t[1] = E("Asset key of the item (Q00134, W64301…) — resolves " +
            "the icon `itemicon/<code>.dds` and model files in the " +
            "client packages. Two items may share a code to reuse " +
            "the same visual.",
            seeAlso: SA("Icon finder page (decodes itemicon/*.dds)"));
        t[3] = E("Equipment model group (G00005…) — the visual " +
            "family used to resolve .nif/.kf/.dds files in pkg.idx. " +
            "Only meaningful on equipment.",
            seeAlso: SA("Model… button on the item page"));
        t[4] = E("Alternate UI icon key (UI012) — used when the " +
            "interface needs a different icon than col 1 gives.");
        t[2] = E("Secondary asset code (→ CItemData+0x08) — " +
            "W-codes on weapons (W10906 'Fire Crystal Dagger'), " +
            "K-codes on skill books (K20263), P-codes on housing " +
            "paintings/furniture (P608060). A second visual/model " +
            "key alongside col 1 — likely the previewed/skin asset.",
            null, null, null, "Icon finder page");
        t[5] = E("Script/spell code (→ CItemData+0xe0) — sNNNNN " +
            "spell-script ids on right-click-usable items: potions " +
            "carry s90511/s90411/… (the effect script run on use), " +
            "a few E-codes appear too. Empty on equipment.",
            new FieldValueCase[]
            {
                V("", "no attached script — most rows"),
                V("s90xxx", "potion/support spell scripts (HP " +
                    "Potion I → s90511, support potions → s90411-13)"),
                V("s90xxx (other)", "explosion/mana potions → " +
                    "s90211-12, s90512-13"),
                V("Exxxxx", "E-codes — a few rows (e.g. E21821)"),
            });
        t[6] = E("Aux numeric (→ CItemData+0x18) — almost " +
            "exclusively set on kind-56 enchant scrolls: values " +
            "11-20 matching the scroll's enchant tier (Swift Song " +
            "Lv1 → 16, Violence Lv1 → 18). Probably the enchant " +
            "slot/tier selector; unmapped in the port.");
        t[13] = E("Visual/costume id (→ CItemData+0x3c, u16) — " +
            "-1 = none (almost everything). Small ids on wearable " +
            "costumes and special items: 855 'Dorayaki Mouth " +
            "Accessory', 257/598 summer uniforms, 1879 pumpkin " +
            "lantern, 4 invisibility cloak, 16 elf sculpture. " +
            "Probably the client-side skin/effect reference.");
        t[7] = E("Server-side name in Big5 encoding. Players NEVER " +
            "read this directly: the client displays the English " +
            "label from t_item.ini keyed by the item id. Edit here " +
            "for server-side consistency AND in t_item.ini for the " +
            "visible name.",
            seeAlso: SA("t_item.ini", "Duplicates panel"));
        t[34] = E("Item description. Only the FIRST physical line " +
            "lives on this column — the text continues on the " +
            "following physical lines until the record reaches 78 " +
            "columns ($7$ = gray text color, $13$/$12$ = other " +
            "colors). Editing mid-text fields by hand is fragile: " +
            "prefer keeping the same line structure.");

        // ---------------- Requirements ----------------
        t[8] = E("Item kind — the family code that drives EVERYTHING: " +
            "which native class parses the row (CWeapon/CArmor/" +
            "CSpellBookItem/CGemItem…), which UI panel can hold it, " +
            "and which attack animation pool plays. Proven mapping " +
            "below (EquipType.ini + CCharKfm::Integration dispatch + " +
            "real item names per kind).",
            ItemKindValues(),
            "Full map below: EVERY kind 1-83 named — 35, 36, " +
            "46, 57, 59, 62, 63, 73 are defined or reserved but " +
            "carry no shipped item. DB-side-only kinds (not in " +
            "S_Item): 8019 = CDestinyRoadItem, 8020 = " +
            "CSpellLvUpBookItem (spell-upgrade books), 84 = " +
            "CTransGoldItem, 9000-9999 = CUIItem — " +
            "CItemDBO::GetAllNormalItems @0x835ad80.",
            "Recipes — MOUNT: kind 44 (or 47 mall) + use type 2 + " +
            "attached enchant containing command 2134 Ride. GEM: " +
            "kind 45 + use type 5. SPELL BOOK: kind 22/25 + spell " +
            "id in col 35. BAG: kind 43 + col 28 = slots. COSTUME " +
            "WEAPON: kind 61-66 (awoken variant of 17-29).",
            "NativeEquipmentSlots.cs", "MotionParser.cs",
            "EquipType.ini");
        t[9] = E("Equip LOCATION — the native equip-panel slot id " +
            "that accepts this item. NOT the EquipType.ini row id " +
            "and NOT a bag index: it's the character-equipment " +
            "location number resolved by CItemFactory::Equip. " +
            "Empty/0 = not equippable on the character (mounts, " +
            "consumables leave it empty; non-character panels use " +
            "18-35). Kind → loc correlation below is proven by the " +
            "shipped data.",
            new FieldValueCase[]
            {
                V("", "not equippable — consumables, mounts, " +
                    "furniture, blueprints…"),
                V("0", "equippable flag cleared — 1 stray row"),
                V("1", "head — helmet/hat/headwear (kinds 1-3, " +
                    "costumes 68/79, enchants 56)"),
                V("2", "upper body — armor/leather/robe (kinds 4-6)"),
                V("3", "lower body — belt/legs (kinds 7-9)"),
                V("4", "hands — gloves/gauntlets (kinds 10-12)"),
                V("5", "feet — boots/shoes (kinds 13-15)"),
                V("6", "back — cape (kind 16)"),
                V("7", "main hand — 1H weapon, no off-hand pairing " +
                    "(kinds 17-19 & 24-26 in 1H-only rows, costume " +
                    "61, some pet gear 81)"),
                V("8", "off hand — shield (kind 34, costume 67)"),
                V("9", "main hand — 2H weapon (kinds 20-23, 27, " +
                    "31-33; costumes 64-66)"),
                V("10", "necklace slot (kind 37)"),
                V("11", "accessory — earring/single ring loc " +
                    "(kind 38 variant)"),
                V("12", "trinket/trophy accessory (kind 39)"),
                V("13", "class certificate (kind 40)"),
                V("18", "bag slot (kind 43 — bag items equip in " +
                    "the bag bar)"),
                V("19", "dual-wield 1H — sits in main OR off hand " +
                    "(kinds 17, 18, 19, 24, 25, 26, costume 61)"),
                V("20", "ring pair loc (kind 38)"),
                V("21-29", "family-hall building slots — furniture/" +
                    "room/NPC placements (kinds 41, 42, 82)"),
                V("30", "family-hall NPC slot (kind 42 merchant/" +
                    "collector NPCs)"),
                V("32", "mount slot — kind 44 rideable mounts " +
                    "(×83 rows)"),
                V("33", "class-expertise slot (kind 80)"),
                V("34", "pet equipment slot (kind 81 — goes to " +
                    "the elf/pet panel, not the character)"),
                V("35", "ranged weapon slot — bow/gun/scythe " +
                    "(kinds 28-30)"),
            },
            "Native rule: kind 1-34 or 37-40 required for a " +
            "character-equippable item — other kinds are rejected " +
            "even if col 9 is set (pet gear 81, glyph 78 have their " +
            "own panels). EquipType.ini (20 rows) is the " +
            "category→loc table the client uses for labels: " +
            "head1/upper2/lower3/hands4/feet5/back6, 1H-sword→7, " +
            "2H-sword→9, 1H-mace→7, 2H-mace→9, 1H-axe→7, 2H-axe→9, " +
            "bow→10(+ammo21), gun→10(+ammo22), magic-device→10, " +
            "staff→9, shield→8, accessory→11, arrows→13, bullets→13.",
            null, "EquipType.ini", "NativeEquipmentSlots.cs");
        t[10] = E("Train mask — bitmask (u32) of attribute types " +
            "selectable for this item in Mana Infusion " +
            "(equipment training). bit k = attribute k; 0 = item " +
            "cannot be trained (native reject). Attribute ids " +
            "(S_EquipTrain column order, proven by the native " +
            "switch): 0=HP, 1=MP, 2=ATK, 3=M-ATK, 4=ACC, " +
            "5=ATK SPD, 6=CRIT, 7=Cast SPD, 8=M-CRIT, 9=DEF, " +
            "10=EVA, 11=Parry, 12=Block, 13=G-Healing, " +
            "14=P-Healing.",
            null,
            "Example: 0x3FFF = all 15 trainable attributes. The " +
            "coefficient per type is read from S_EquipTrain.ini " +
            "indexed by the item's required level (col 14).",
            null, "S_EquipTrain.ini", "EquipTrainService.cs");
        t[12] = E("Use type — native dispatch in " +
            "CItemFactory::UseItemParam when the player uses the " +
            "item. Empty/absent = the item is never right-click-usable.",
            new FieldValueCase[]
            {
                V("1", "life item — simple consumable"),
                V("2", "right-click usable — runs the attached " +
                    "enchant (col 23): mounts, charms, XP scrolls " +
                    "(observed ×5434)"),
                V("3", "item-on-item — apply to another item"),
                V("4", "rejected by the native dispatch"),
                V("5", "item-on-item — gems & enchants on gear " +
                    "(kind 45 gems, kind 56 enchants; observed " +
                    "×1237)"),
                V("6", "elf item — use on a summon/pet " +
                    "(observed ×147, kind 44 elf food)"),
                V("-1", "observed ×1 — data quirk"),
            },
            "Observed: empty ×13607 (never usable), 2, 5, 6, -1.",
            "Mount = this field = 2 + kind 44 + enchant with " +
            "command 2134.",
            "S_Enchant.ini");
        t[14] = E("Required level — minimum character level to " +
            "equip or use. Also used by Mana Infusion as the tier " +
            "index into S_EquipTrain coefficients. Empty = no " +
            "requirement (consumables).");
        t[15] = E("Max level — cap for WORK items only " +
            "(farm/mine/altar items, kinds 50-52): the work " +
            "station refuses characters above this level. 0 or " +
            "empty = no cap. Only ~169 rows set it.");
        t[16] = E("Recipe/work tier (→ CItemData+0x40, u32) — " +
            "set on blueprints/molds/secret-books (kinds 44, " +
            "53-56): values 51-84 tracking the recipe's product " +
            "tier (craft level band). Also small values on " +
            "accessories. Unmapped in the port — the observed " +
            "table below is the reference.");
        t[20] = E("Aux use parameter (→ CItemData+0x54, u32) — " +
            "3-100 observed on usable items (Portal Stone → 20, " +
            "Guardian Spell → 10, Jam → 100). Likely the use/" +
            "channel time in tenths of a second before the item " +
            "activates; unmapped in the port.");
        t[19] = E("Base quality/color — CItemData+0x50, the " +
            "default quality written on the instance (clone+0x2c) " +
            "and the cube's PickAColor fallback. Higher = better " +
            "color tier. The instance quality can still differ " +
            "(identification, cube re-roll).",
            new FieldValueCase[]
            {
                V("", "no base quality — the drop system/cube " +
                    "assigns the color (×7251)"),
                V("1", "white — common tier (×11)"),
                V("2", "green — uncommon (×2043, e.g. mount 20051)"),
                V("3", "blue — rare (×5492, most enchanted gear)"),
                V("4", "yellow — epic (×1564)"),
                V("5", "orange — legendary (×4033 — secret books, " +
                    "top gear)"),
                V("6", "test/purple tier (×31 — 'Test Katana' " +
                    "family)"),
                V("10", "special tier (×1 — 24200)"),
            });

        // ---------------- Flags ----------------
        t[11] = E("Flags — signed 64-bit bitfield " +
            "(CItemData+0x30/0x34). Low byte: bit7 (0x80, i.e. " +
            "value ≥ 128 with bit set) = binds on equip — the " +
            "native calls BindItem on equip/use/move to special " +
            "containers. High 32 bits: operation-type bits read " +
            "by the magic box — 8 = weapon, 0x20 = armor, " +
            "0x10 = accessory (CheckMagicBoxCosts). Large values " +
            "like 274877907072 = high-word flags.",
            new FieldValueCase[]
            {
                V("0x80 (128)", "bit 7 — binds on equip"),
                V("hi-word 8", "magic box: weapon type"),
                V("hi-word 0x20", "magic box: armor type"),
                V("hi-word 0x10", "magic box: accessory type"),
            },
            "Observed: 4 ×8835, 1, 274877907072, 5, 0, " +
            "274877907136, 16391, 128… — combine flags by adding " +
            "the bit values.");
        t[30] = E("Flag column — semantics not mapped in the " +
            "server parser. Observed values below.");
        t[18] = E("Restriction mask B (→ CItemData+0x48, i64) — " +
            "bit field whose meaning depends on the kind. KIND 78 " +
            "GLYPHS: bit(class−1) = which class may inlay the " +
            "glyph — proven by names: Hellfire Glyph = 2 → class " +
            "2 Magician, Demonic Symphony = 32 → class 6 Bard, " +
            "Shrink Raygun = 128 → class 8 Engineer, Song of " +
            "Patience = 1024 → class 11 Dancer, Sense of Survival " +
            "= 1 → class 1 Warrior. KIND 58 SKILL BOOKS: all carry " +
            "0x80000 (bit 19). Other rows: 0.",
            new FieldValueCase[]
            {
                V("0", "no restriction — most rows"),
                V("1-16384", "kind-78 glyph class bit — " +
                    "bit(class−1), see table values"),
                V("524288 (0x80000)", "kind-58 skill-book marker " +
                    "(×582) + a few glyphs"),
                V("32768", "kind-78 glyph — class 16 Samurai " +
                    "(Florica's Katana family)"),
            },
            "Class ids come from S_Class.ini (1=Warrior, " +
            "2=Magician, 3=Hunter, 4=Priest, 5=Thief, 6=Bard, " +
            "7=Knight, 8=Engineer, 9=MartialArtist, " +
            "10=Illusionist, 11=Dancer, 12=Shaman, 13=Paladin, " +
            "14=Ranger, 15=Necromancer, 16=Samurai, " +
            "17=Sage, 18=Dragoon, 19=MagicSwordsman, " +
            "20=Messiah, 21=Berserker…).",
            null, "S_Class.ini", "col 77 (the i64 class mask)");

        // ---------------- Economy ----------------
        t[17] = E("Mall price — price in the ALTERNATE currency " +
            "(item mall points, ~1000-6000 on shop items). This " +
            "is NOT the gold price: NPC buy/sell uses col 31.",
            null, null, null, "S_ItemMall");
        t[28] = E("Bag capacity — number of slots of the bag " +
            "created for this item (kind 43 only). Real values: " +
            "33734→4, 32649→8, 40030→5, 41261→20, 44981→30. " +
            "Irrelevant on any other kind.");
        t[29] = E("Max stack — stack cap for stackables " +
            "(potions → 100, quest items → 999). Empty on " +
            "equipment. Note: the native also reads it as " +
            "durability fallback when col 74 is empty — don't " +
            "stack equipment: EquipLoc>0 disqualifies stacking " +
            "anyway.");
        t[31] = E("System price — THE gold reference price: " +
            "NPC buy = price × store discount, player resell = " +
            "price × 5% × durability ratio, identification = " +
            "× 5%. A weapon lv93 costs ~35262, shop armor " +
            "~194400. Mall items show 2250000-type values but " +
            "are bought with col-17 currency.");
        t[76] = E("Repair price — base repair cost used by the " +
            "repair NPC (4 for a lv1 sword, 5082 for a lv93 " +
            "weapon).");

        // ---------------- Usage & duration ----------------
        t[21] = E("Use cooldown — tenths of a second between two " +
            "uses (0/empty = none). The native splits it into " +
            "v/10 seconds + microseconds remainder.");
        t[22] = E("Cooldown group — items of the same group " +
            "block each other when used (potions share a group so " +
            "you can't chug two at once). 0 = shared global group " +
            "(native default — NOT 'no group').");
        t[23] = E("Attached enchant — S_Enchant id carried by this " +
            "item. On right-click-usable items (use type 2) it " +
            "runs on use; on equipped gear it's the item's " +
            "proc/passive and its trigger is configured in " +
            "S_Enchant cols 40-43. Key enchant commands " +
            "(EnchantCommands.cs): 2134=Ride, 2140=RideSpell " +
            "(combat-mount buffs), 2051=MoveSPD%, 2084=flat " +
            "ATK/M-ATK, 2048/2040=crit rate, 2043/2044=crit dmg, " +
            "2086=flat STR/AGI/INT/WIS/LCK, 2085=same in %, " +
            "1921=chain enchant (proc-on-proc), 1821-1824=enchant " +
            "cancel, 2902-2945=drop/XP gains, 9022=OpenShop.",
            null, null,
            "Recipes — MOUNT: kind 44 + use type 2 + enchant " +
            "whose cmd1 = 2134 (args rideKey|flag|seats|" +
            "combatType, rideKey = model in RideList.ini) and " +
            "cmd2 = 2051|N (+N% move speed). Example: item " +
            "20051 'Flame Soul Puma' → enchant 58991 = " +
            "2134|14010|1 + 2051|40. COMBAT MOUNT: same + " +
            "arg4 ≠ 0 (mount keeps the ride through attacks) + " +
            "2140 buff commands. PROC WEAPON: enchant with " +
            "trigger cols 40-43 set (e.g. '15% chance on attack " +
            "→ slow' = trigger event 2 + chain cmd 1921).",
            "S_Enchant.ini", "T_Enchant.ini", "RideList.ini");
        t[24] = E("Buff duration unit — with the amount in " +
            "col 25, the lifetime of the effect granted on use " +
            "(charms, buff potions). DIFFERENT table from the " +
            "rent units (cols 26/27).",
            new FieldValueCase[]
            {
                V("0/empty", "no duration — instant effect or " +
                    "permanent, per the enchant command"),
                V("1", "seconds? (observed ×145 — rare, check a " +
                    "real row before using)"),
                V("2", "minutes (observed ×603 — e.g. EXP Charm " +
                    "c24=2 c25=60 = 1h)"),
                V("3", "hours (e.g. 38632 c24=3 c25=1 = 1h)"),
                V("4", "days (e.g. Iris Jewel c24=4 c25=1 = " +
                    "1 day)"),
                V("5", "observed ×200 — larger unit family; " +
                    "verify against a real item"),
            });
        t[25] = E("Buff duration amount — quantity in the unit " +
            "of col 24. Example: unit 2 + amount 60 = 3600 s " +
            "(1 hour buff).");
        t[26] = E("Rent duration unit — item LIFETIME (how long " +
            "the item exists / stays usable), formula " +
            "multiplier(unit) × col 27 proven in the native " +
            "deserializer. TWO families: units 1-3 arm the " +
            "countdown at first use/move; units 4-6 start " +
            "counting at creation ('time-limited on obtain' " +
            "items like rental mounts).",
            new FieldValueCase[]
            {
                V("0 or ≥7", "permanent item (no due_date)"),
                V("1", "×60 — minutes, armed at first use"),
                V("2", "×3600 — hours, armed at first use"),
                V("3", "×86400 — days, armed at first use"),
                V("4", "×60 — minutes, countdown from creation"),
                V("5", "×3600 — hours, countdown from creation"),
                V("6", "×86400 — days, countdown from creation " +
                    "(observed ×49 — timed mall items)"),
            },
            "Example: item mall 44025 → col26=3, col27=3 → " +
            "3 days lifetime.");
        t[27] = E("Rent duration amount — quantity in the unit " +
            "of col 26. col26=3 col27=3 → 3×86400 = 259200 s.");
        t[35] = E("Shared column — the native reads this field " +
            "differently per kind: KIND 22 (CSpellBookItem): " +
            "spell id taught by the book. KIND 25 " +
            "(CSpellLvUpBookItem): spell id upgraded. KIND 45 " +
            "(gem): gem level, compared to the target's required " +
            "level (native error 0xcce 'Equipment level too " +
            "low'). KIND 0x2a/0x52 (family-hall NPC items): tid " +
            "of the NPC to spawn. Otherwise: license-replace id " +
            "for most kinds.",
            null,
            "Example: spell book → put the S_Spell id here; " +
            "gem → put its tier (current gems leave it empty = 0).",
            null, "S_Spell.ini", "T_Spell.ini");
        t[32] = E("License/prereq list (→ CItemData+0x80, " +
            "string) — 'id1 id2 …;value' format on usable/quest " +
            "items: '501 502;61' on gear, '117;79', '1;70' on " +
            "tools. Probably a required-license/id list gated " +
            "behind the value after ';'. Unmapped in the port — " +
            "the observed table is the reference.");
        t[33] = E("Target/use level (→ CItemData+0x84, u32) — " +
            "auxiliary level requirement: on GEMS (kind 45) it " +
            "is the minimum equipment level the gem can be " +
            "socketed into — Strength Gem Lv1 → 35 matches its " +
            "Big5 text 'only on equipment lv35+'. On weapons/" +
            "armor it carries a small tier value (33-58) " +
            "distinct from required level.");
        t[36] = E("Random-prefix combo id (→ CItemData+0x90, " +
            "u32) — S_ItemCombo row id on equipment: the random-" +
            "stat prefix rolled on the item. Proven by " +
            "T_ItemCombo EN names: 3501 = 'Gory', 1782 = " +
            "'Deft', 2095 = 'Striker', 3507-3509 = 'Enhanced'. " +
            "Weapons carry ids ~1700-3500. Kind-dependent reuse: " +
            "for kinds 69-77 the native reads this slot as the " +
            "license-replace value instead.",
            null, null, null,
            "S_ItemCombo.ini", "T_ItemCombo.ini");
        t[75] = E("Cube group ids — ';'-separated list of magic-" +
            "cube groups this item can come out of. Recipes with " +
            "a product ≥100000 are groups: the cube picks from " +
            "all items whose list contains the group id.",
            null, null, null, "S_ItemChest.ini");

        // ---------------- Equipment stats ----------------
        t[37] = E("Bonus HP granted when equipped " +
            "(→ CItemAttr+0x04). Empty = 0.");
        t[38] = E("Bonus MP granted when equipped " +
            "(→ CItemAttr+0x08).");
        t[39] = E("Bonus STR — flat strength added to the " +
            "character sheet (→ CItemAttr+0x0c).");
        t[40] = E("Bonus AGI — native DEX (→ CItemAttr+0x14).");
        t[41] = E("Bonus INT (→ CItemAttr+0x10).");
        t[42] = E("Bonus WIS — native SPR (→ CItemAttr+0x18).");
        t[43] = E("Bonus LCK — native LUK (→ CItemAttr+0x1c).");
        t[44] = E("Attack element — the element channel of the " +
            "weapon's damage: when non-zero, damage is routed " +
            "through the defender's resistance for that channel " +
            "(their Resist 1-9 block). 0 = neutral physical.",
            ElementValues());
        t[45] = E("P-ATK — physical attack of the weapon " +
            "(→ CItemAttr+0x20, read for weapon kinds only).");
        t[46] = E("M-ATK — magic attack (→ CItemAttr+0x24). " +
            "Staves/wands carry more M-ATK than P-ATK (sword " +
            "31899: p284/m241; staff 31926: p206/m446).");
        t[47] = E("Attack variance % — damage spread: the native " +
            "rolls `100 − var + rand[0, 2·var)` percent per hit. " +
            "0 = perfectly stable damage; higher = wider swing " +
            "(swords ~6, staves ~8).");
        t[48] = E("Attack range (raw) — native ×0.5 world units: " +
            "'4' = reach 2.0 (melee swords ~3-4 raw, bows ~42 " +
            "raw = 21 units). Read by AttackRange(CCharacter).");
        t[49] = E("Attack speed (raw) — native ×1.5: '16' = " +
            "speed 24 on the sheet. Lower displayed value = " +
            "slower weapon.");
        t[50] = E("DEF — physical defence (→ CItemAttr+0x34 → " +
            "char+0x3b8). Armor pieces carry it; a cape example " +
            "has 87.");
        t[51] = E("HIT — accuracy bonus (→ CItemAttr+0x38 → " +
            "char+0x3bc; on weapons feeds PhysicalHitRate).");
        t[52] = E("DODGE — evasion bonus (→ CItemAttr+0x3a → " +
            "char+0x3be).");
        t[53] = E("CRIT — physical crit rate bonus " +
            "(→ CItemAttr+0x3c; on weapons feeds " +
            "PhysicalCriticalRate).");
        t[54] = E("CRIT damage — physical crit damage bonus " +
            "(→ CItemAttr+0x3e).");
        t[55] = E("M-CRIT — magic crit rate bonus " +
            "(→ CItemAttr+0x40 → MagicCriticalRate).");
        t[56] = E("M-CRIT damage — magic crit damage bonus " +
            "(→ CItemAttr+0x42).");
        t[57] = E("Pet equip stat — summed into the pet's 4th " +
            "panel stat (speed) on elf equipment. All current " +
            "data leaves it empty (0).");
        t[58] = E("Aux stat block field 1 (→ CItemData+0xce, " +
            "s16) — first of the 5-field block cols 58-62 that " +
            "follows the base stats: small values 16-64 on " +
            "equipment (weapons, trophies — 'Trophy Enchant - " +
            "Healer Lv4' → 64). Likely extra-stat/proc " +
            "parameters; unmapped in the port.");
        t[59] = E("Aux stat block field 2 (→ CItemData+0xd0, " +
            "u32) — second field of the cols 58-62 equipment " +
            "block (values ~17-64). Unmapped.");
        t[60] = E("Aux stat block field 3 (→ CItemData+0xd4, " +
            "u32) — third field of the cols 58-62 equipment " +
            "block (values ~6-62). Unmapped.");
        t[61] = E("Aux stat block field 4 (→ CItemData+0xd8, " +
            "u32) — fourth field of the cols 58-62 equipment " +
            "block (values ~105-240). Unmapped.");
        t[62] = E("Aux stat block field 5 (→ CItemData+0xdc, " +
            "u32) — fifth field of the cols 58-62 equipment " +
            "block (values ~50-233, shields/armor). Unmapped.");
        t[77] = E("Class restriction mask (→ CItemData+0x118, " +
            "i64) — bit(classId−1) = which classes may equip/" +
            "use the item. PROVEN: a melee weapon decodes as " +
            "Warrior+Berserker+ShieldGladiator (0x300001), " +
            "staves/wands decode as Magician/Priest/Illusionist/" +
            "Necromancer…, knight armor as Warrior/Knight/" +
            "Paladin. 0 = every class. Class ids from " +
            "S_Class.ini (1-61 base classes; 101-161/201-261/" +
            "301-361 are the promoted variants — only bits " +
            "0-60 used here).",
            new FieldValueCase[]
            {
                V("0", "usable by all classes (×16647)"),
                V("0x300001", "Warrior + Berserker + " +
                    "ShieldGladiator (melee weapons)"),
                V("0x300c008c04202", "caster mask — Magician, " +
                    "Illusionist, Necro, ElementMaster, " +
                    "GravityController, ShadowPriest, " +
                    "PhantomDemonTamer, TimeDominator, " +
                    "DarkPrisonEnvoy, CurseSage"),
                V("0x300200201041", "tank mask — Warrior, Knight, " +
                    "Paladin, ShieldGladiator, Oathkeeper, " +
                    "BloodSoulKnight, HolyKnight"),
                V("0xc0004000808", "healer mask — Priest, Shaman, " +
                    "JudgePriest…"),
                V("0xc33c33002594", "physical-dps mask — Hunter, " +
                    "Thief, Engineer, MartialArtist, Dancer, " +
                    "Ranger, BeastTamer, DemonHunter, Assassin, " +
                    "ShadowNinja, MechaSoldier, Annihilator, " +
                    "PurgAsura, PhantomRakshasa, SwordDancer, " +
                    "CharmDancer, AbsoluteArrow, HolyArrow"),
            },
            "To restrict an item: sum 1<<(classId−1) for every " +
            "allowed class. The client shows the red class " +
            "restriction line from this mask.",
            null, "S_Class.ini", "col 18 (the glyph/flag mask)");
        t[72] = E("Max sockets — gem slots of the equipment: " +
            "0/empty = never socketable, else 1-6. Observed: " +
            "2 ×1653, 6 ×775, 1 ×744, 4 ×739, 3 ×588.");
        t[73] = E("Socket open rate % — chance rolled per socket " +
            "when the item is identified/generated " +
            "(native random()%100). Only 3 values shipped: " +
            "30 (×1803), 50 (×1441), 70 (×1247).");
        t[74] = E("Max durability — equipment durability " +
            "(weapons ~75-130, staff 130, swords 75). Falls " +
            "back to col 29 when empty (native).");

        // ---------------- Resistances ----------------
        for (var i = 0; i <= 8; i++)
        {
            if (i == 6)
            {
                t[69] = E("Resist 7 — elemental resistance " +
                    "channel 7. DOUBLE DUTY: the same column is " +
                    "read as restriction flags A — bits 3|4 " +
                    "(0x18) refuse deposit into family storage. " +
                    "Observed flag values: 1/2/4/8/15.",
                    ResistNote());
                continue;
            }
            if (i == 7)
            {
                t[70] = E("Resist 8 — elemental resistance " +
                    "channel 8 (nature — proven by title " +
                    "enchant 53140 '自然抗性 +3%' = cmd 2095 " +
                    "var4). DOUBLE DUTY: read as restriction " +
                    "flags B — bit 0 refuses family-storage " +
                    "deposit.",
                    ResistNote());
                continue;
            }
            t[63 + i] = E($"Resist {i + 1} — percent of elemental " +
                $"damage absorbed on channel {i + 1} (equipment " +
                "resistances, → CItemAttr+0x50.. — 9 consecutive " +
                "columns).", ResistNote());
        }
    }

    /// <summary>Table complète des kinds d'item — TOUS les ids
    /// 0-83 : kinds observés dans S_Item.ini (avec un exemple réel
    /// nommé) + kinds réservés/non utilisés explicitement marqués.
    /// Preuves : dispatch des classes natives
    /// (CItemDBO::GetAllNormalItems @0x835ad80 — 0x3a→CSpellBookItem,
    /// 0x4d→CPartyItem, 0x54→CTransGoldItem, 0x1f53→CDestinyRoadItem,
    /// 0x1f54→CSpellLvUpBookItem, 0x30→CLuckyItem),
    /// CItemFactory::MagicBoxKusoType @0x8086850 (61-62→type1,
    /// 63-66→type2, 67→3, 68→4) + noms EN des items réels.</summary>
    private static FieldValueCase[] ItemKindValues() => new[]
    {
        // ---- Armor kinds 1-16 (loc per piece, armor class per row)
        V("1", "heavy/plate HEAD (helmet) — loc 1. Ex: " +
            "30597 'Iron Flame Helmet'"),
        V("2", "leather HEAD (hat) — loc 1. Ex: 999 匿蹤者皮帽"),
        V("3", "cloth HEAD (headwear) — loc 1. Ex: 30761 " +
            "'Manic Mage Shoes'-family headwear"),
        V("4", "heavy/plate BODY (armor) — loc 2. Ex: 995 亂戰者盔甲"),
        V("5", "leather BODY — loc 2. Ex: 30678 'Cleaver Brigandine'"),
        V("6", "cloth BODY (robe) — loc 2. Ex: 1005 嘆息者法袍"),
        V("7", "heavy/plate WAIST (belt) — loc 3. Ex: 996 亂戰者腰帶"),
        V("8", "leather WAIST — loc 3. Ex: 30679 'Cleaver Band'"),
        V("9", "cloth WAIST — loc 3. Ex: 1006 嘆息者腰帶"),
        V("10", "heavy/plate HANDS (gauntlets) — loc 4. Ex: 997"),
        V("11", "leather HANDS — loc 4. Ex: 30680 'Cleaver Bracers'"),
        V("12", "cloth HANDS — loc 4. Ex: 1007 嘆息者手套"),
        V("13", "heavy/plate FEET (greaves) — loc 5. Ex: 30601 " +
            "'Iron Flame Greaves'"),
        V("14", "leather FEET (boots) — loc 5. Ex: 1003 匿蹤者皮靴"),
        V("15", "cloth FEET (shoes) — loc 5. Ex: 30761 " +
            "'Manic Mage Shoes'"),
        V("16", "CAPE — loc 6, any armor class. Ex: 1064 光羽披風"),

        // ---- Weapon kinds 17-33 + shield 34
        V("17", "DAGGER — 1H blade, loc 7 or 19 (dual-wield). " +
            "Ex: 30297 'Fire Crystal Dagger', 30298 'Bloody " +
            "Scalpel'"),
        V("18", "SWORD — 1H blade, loc 7 or 19 (dual-wield). " +
            "Ex: 30317 'Soldier Cutlass', 30318 'Bloody Cutlass'"),
        V("19", "RAPIER — 1H fencing blade, loc 7 or 19. " +
            "Ex: 30335 'Demon Mark Sword', 30336 'Blood Omen'"),
        V("20", "KATANA — 2H, loc 9 (anim attack08/09). " +
            "Ex: 520 'Florica's Katana', 522 'Statham's Warblade'"),
        V("21", "GREATSWORD — 2H, loc 9. Ex: 1016 毀滅者大劍, " +
            "30426 'Devil Steel Battle Sword'"),
        V("22", "2H AXE — loc 9 (anim attack10/11). " +
            "Ex: 1017 荒野行者巨斧, 30443 'Bone Harvest Axe'"),
        V("23", "PIKE/LANCE — 2H, loc 9 (anim attack25/26). " +
            "Ex: 177 'Kimmy's Pike', 1058 刺棘長槍"),
        V("24", "1H MACE/SCEPTER — loc 7 or 19 (dual-wield). " +
            "Ex: 555 'Sir Aldien's Celestial Mace', 1012 捍衛者之杖"),
        V("25", "CESTUS/FIST — loc 7 or 19 (dual-wield). " +
            "Ex: 30375 'Nail Skull Gauntlets', 1013 闇影襲拳"),
        V("26", "1H CLUB/STAFF — loc 7 or 19 (dual-wield). " +
            "Ex: 30391 'Ancient Spirit Staff', 1047 雷霆武棍"),
        V("27", "2H CLUB/HAMMER — loc 9. Ex: 47 'Magic Tuskar " +
            "Club', 1018 荒蕪大鎚"),
        V("28", "BOW — ranged loc 35. Ex: 30481 'Green Wind Bow', " +
            "30482 'Crescent Moon Bow'"),
        V("29", "GUN — ranged loc 35. Ex: 30501 'Piercing Pistol', " +
            "30502 'Proton Motor Rifle'"),
        V("30", "SCYTHE — ranged loc 35 (15 rows). " +
            "Ex: 28663 'Holy - Soul Reaver Sickle'"),
        V("31", "INSTRUMENT (harp/guitar/fiddle) — 2H loc 9, " +
            "bard weapon. Ex: 30519 'Magic Fiddle', 30520 " +
            "'Melancholy Melody', 1022 攝魂之音弦"),
        V("32", "STAFF/WAND — 2H loc 9. Ex: 30557 'Dreamer's " +
            "Wand', 1024 星光碎石之杖"),
        V("33", "GRIMOIRE — 2H loc 9, book/tome weapon. " +
            "Ex: 30539 'Alchemy Formula', 1087 杜芬舒斯的禁書"),
        V("34", "SHIELD — off hand loc 8. Ex: 98 'God's Eye of " +
            "Darkness Shield', 30577 'Hardened Steel Shield'"),

        // ---- Unused / accessories / certificates 35-40
        V("35", "UNUSED — no item ships with kind 35 (gap " +
            "between weapons and accessories)"),
        V("36", "UNUSED — no item ships with kind 36"),
        V("37", "NECKLACE — loc 10. Ex: 1068 詭笑之鍊"),
        V("38", "RING — locs 11 (single) and 20 (pair). " +
            "Ex: 1060 玄影戒指"),
        V("39", "TRINKET/TROPHY — loc 12, gear-panel trophy. " +
            "Ex: 1070 'Gis Feather'"),
        V("40", "CLASS CERTIFICATE — loc 13, grants class-skill " +
            "passives. Ex: 33272 'Taunt I', 33273 'Iron Armor I'"),

        // ---- Non-equipment families 41-60
        V("41", "building blueprint — hall construction docs " +
            "(locs 21-29). Ex: 35071 'Blueprint: Village Chief " +
            "Hall'"),
        V("42", "family-hall NPC item — spawns hall merchants/" +
            "collectors (loc 30, some 21-29). Ex: 34111 'Cloth " +
            "Armor Blueprint Collector'"),
        V("43", "BAG — capacity in col 28, loc 18 (3 rows: " +
            "32649 'Traveler's Luggage')"),
        V("44", "GENERIC usable/misc — THE bucket (5663 rows): " +
            "consumables, quest items, secret books (祕傳書), " +
            "mounts when loc 32. Ex: 1 'Dagger Secret Book'. " +
            "Behavior = use type + attached enchant"),
        V("45", "GEM — socketable into gear, use type 5. " +
            "Ex: 30937 'Strength Gem Lv1'"),
        V("46", "UNUSED — no item ships with kind 46"),
        V("47", "mall/cash usable — item-mall consumables & " +
            "mounts (814 rows). Ex: 1081 詛咒強化"),
        V("48", "quest/special item (912 rows). Ex: 79 'Unknown " +
            "Magic Machine Wreckage', 205 fortune bags"),
        V("49", "reputation/slate item — slates, recipes, " +
            "extractors. Ex: 185 'Mysterious Slate', 199 " +
            "'Recipe: Ocean Feast'"),
        V("50", "farm work item — farm plants (uses max-level " +
            "cap col 15). Ex: 30136 'Lv20 Farm Plants'"),
        V("51", "mine work item — ore location maps. " +
            "Ex: 30191 'Lv20 Ore Location Map'"),
        V("52", "altar work item — summoning scrolls. " +
            "Ex: 30246 'Lv20 Summoning Scroll'"),
        V("53", "mold/pattern recipe — crafting molds " +
            "(模具/Blueprint:/Formula:). Ex: 20 'Blueprint: Soul " +
            "King's Golden Gloves'"),
        V("54", "crafting blueprint — THE recipe family (2005 " +
            "rows). Ex: 37 'Blueprint: Phire's Holy Law's Touch'"),
        V("55", "crafting blueprint family 2 — awaken/advanced " +
            "recipes (639 rows). Ex: 24 'Blueprint: Advanced " +
            "Chivalry'"),
        V("56", "equipment enchant scroll — right-click on " +
            "gear, use type 5 (614 rows). Ex: 30985 'Enchant- " +
            "Swift Song Lv1'"),
        V("57", "UNUSED — no item ships with kind 57"),
        V("58", "SKILL BOOK — teaches a class skill on use; " +
            "native class CSpellBookItem (0x3a, proven " +
            "GetAllNormalItems @0x835b446). Ex: 938 'Skill: " +
            "War Soul Enhancement'"),
        V("59", "UNUSED — no item ships with kind 59"),
        V("60", "pet egg / summon — battle-pet items. " +
            "Ex: 34700 'Battle Dog', 21834 'Lil' Pom'"),

        // ---- Costume kinds 61-68 (native KusoType dispatch)
        V("61", "costume 1H weapon (KusoType 1) — 'Disguised " +
            "Weapon' visuals on loc 7/19. Ex: 798 'Anniversary " +
            "Cake Whack-a-Mole Hammer'"),
        V("62", "costume 1H variant (KusoType 1) — defined by " +
            "the native dispatch, NO item ships with kind 62"),
        V("63", "costume ranged/2H variant (KusoType 2) — " +
            "defined by the native dispatch, no rows shipped"),
        V("64", "costume gun/ranged (KusoType 2) — 1 row: " +
            "28030 'Secret Treasure: Disguised Weapon - Puffer'"),
        V("65", "costume 2H weapon (KusoType 2) — loc 9. " +
            "Ex: 612 'Midsummer Puffy Marlin Sword', 1358 candy " +
            "cane"),
        V("66", "costume instrument (KusoType 2) — 2 rows: " +
            "28035/28036 'Artrageous Guitar'"),
        V("67", "costume shield (KusoType 3) — loc 8. " +
            "Ex: 21326 'Alpaca Knight Shield (Alpha)'"),
        V("68", "costume armor/parts (KusoType 4) — mouth/head/" +
            "body wearables on armor locs. Ex: 593 'Dorayaki " +
            "Mouth Accessory', 608 sailor uniform"),

        // ---- Housing / misc 69-83
        V("69", "housing — potted plants & table decor. " +
            "Ex: 37915 'Calming Cactus Potted Plant'"),
        V("70", "housing — sofas/chairs. Ex: 38529 'Nautical " +
            "Sofa'"),
        V("71", "housing — carpets/rugs. Ex: 38543 'Crimson " +
            "Carpet', 38687 'Alpaca Rug'"),
        V("72", "housing — hanging decor/paintings. " +
            "Ex: 38544 'Swirled Conch Shell', 38546 'Painting: " +
            "Central Continent'"),
        V("73", "UNUSED in data — inside the native housing " +
            "kind range 69-76 but no item ships with kind 73"),
        V("74", "housing — wallpaper. Ex: 38613 'Eggshell Tile " +
            "Wallpaper'"),
        V("75", "housing — living-room flooring. " +
            "Ex: 38607 'Sunshine Wooden Floor'"),
        V("76", "housing — bedroom flooring/carpet. " +
            "Ex: 38611 'Chocolate Bedroom Carpet'"),
        V("77", "BANQUET proposal — CPartyItem native class " +
            "(0x4d, proven). Ex: 20023 'Banquet Proposal: " +
            "Tea Party'"),
        V("78", "GLYPH — class glyph inlay, own glyph panel. " +
            "Ex: 20193 'Hellfire Glyph'"),
        V("79", "costume enchant item — head/body/back/1H " +
            "costume enchants (loc-matched). Ex: 39142 'Test " +
            "Costume Enchant-Head'"),
        V("80", "class expertise item — expertise-point books " +
            "(loc 33). Ex: 21179 'Class Expertise Lv 1'"),
        V("81", "PET EQUIPMENT — elf/pet panel gear (locs " +
            "34/7). Ex: 804 '<Pet> Indestructible Giant Pincers', " +
            "805 '<Pet> Precise Collar'"),
        V("82", "guardian/summon card — hall guardian NPCs " +
            "(locs 22-ish; kind 0x52 shares the NPC-tid read " +
            "with kind 42). Ex: 26217 'King Kamsky'"),
        V("83", "wedding invitation (2 rows — 26240 'Wedding " +
            "Invitation', 26349 blank)"),
        V("", "missing kind — 2 data quirks (24200 'Shameless " +
            "Undies', 27808 'Whole Grains')"),
    };

    /// <summary>Canaux d'élément partagés item/sort/mob —
    /// channels 1-3 physiques (prouvés par les titres 2101 =
    /// slash/strike/pierce), 4-9 élémentaires (canal 8 = nature
    /// prouvé par le titre 53140).</summary>
    private static FieldValueCase[] ElementValues() => new[]
    {
        V("0/empty", "neutral physical — no elemental check"),
        V("1", "physical channel 1 — slash family (indexes " +
            "defender Resist 1)"),
        V("2", "physical channel 2 — most common on mobs " +
            "(×5776) — indexes Resist 2"),
        V("3", "physical channel 3 — pierce/strike family — " +
            "indexes Resist 3"),
        V("4", "elemental channel 4 — indexes Resist 4"),
        V("5", "elemental channel 5 — indexes Resist 5"),
        V("6", "elemental channel 6 — lightning (SpellParser " +
            "note) — indexes Resist 6"),
        V("7", "elemental channel 7 — indexes Resist 7"),
        V("8", "elemental channel 8 — nature (proven: title " +
            "'自然抗性' = resist channel 8)"),
        V("9", "elemental channel 9 — indexes Resist 9"),
        V("10", "heal channel — used by spells, not weapons"),
    };

    /// <summary>Note commune des 9 colonnes de résistance.</summary>
    private static IReadOnlyList<FieldValueCase> ResistNote() =>
        new FieldValueCase[]
        {
            V("value", "percent absorbed — damage of that " +
                "element is multiplied by (1 − v/100)"),
            V("empty", "0% resist"),
        };
}
