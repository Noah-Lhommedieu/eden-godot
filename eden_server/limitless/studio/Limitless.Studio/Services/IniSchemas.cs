using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Catalogue des schémas de tables `db_decoded` — construit
/// depuis les parsers C# du serveur (`server/src/Eden.Core/Data/
/// Parsers/*Parser.cs`, dont les commentaires de colonnes sont prouvés
/// par désasm/captures). Chaque table reçoit : titre, utilité EN, note
/// d'impact (« ce que modifier ce fichier implique »), fichier T_*.ini
/// de libellés EN, et colonnes documentées par groupe. Toute table
/// absente du catalogue retombe sur <see cref="Generic"/> — le
/// formulaire reste utilisable avec des colonnes « col N ».</summary>
public static class IniSchemas
{
    private static IniColumn C(int i, string name, string hint,
        IniFieldKind kind = IniFieldKind.Number,
        string group = "Other", string? refTrans = null) =>
        new(i, name, hint, kind, group, refTrans);

    /// <summary>Note d'impact générique — serveur lit au boot,
    /// le client peut avoir sa propre copie.</summary>
    private const string DefaultImpact =
        "The server reads this file at boot from " +
        "server_us/Data/db_decoded — restart the zone service to " +
        "apply changes. The client may hold its own copy " +
        "(C_*.ini data or T_*.ini labels) under " +
        "reforged/EdenReforged/data/db — check the duplicates panel.";

    private static readonly Dictionary<string, IniTableSchema> ByFile =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        // ---------- S_Item.ini — ItemParser.cs ----------
        ["S_Item.ini"] = new("S_Item.ini", "Items",
            "Every item in the game: stats, prices, usage rules, " +
            "equipment bonuses (78 columns).",
            "Affects server loot/stats/economy. The English name " +
            "players see lives in the client's t_item.ini — rename " +
            "there too for a visible name change. Client-side stat " +
            "display comes from c_item.ini: if that copy diverged " +
            "(duplicates panel), in-game tooltips may not match " +
            "server behavior. " + DefaultImpact,
            "t_item.ini", 7,
            new IniColumn[]
            {
                C(0, "Item ID", "Unique id — referenced by drop " +
                    "tables, shops, quests, mails.", group: "Identity"),
                C(1, "Item code", "Asset key (Q00134…) — resolves " +
                    "itemicon/<code>.dds and model files.",
                    IniFieldKind.Code, "Identity"),
                C(3, "Model/type code", "Equipment model group " +
                    "(G00005…) — visual family.",
                    IniFieldKind.Code, "Identity"),
                C(4, "UI icon code", "Alternate UI icon key (UI012).",
                    IniFieldKind.Code, "Identity"),
                C(7, "Name (Big5)", "Server-side name. Players see " +
                    "the English label from the client's t_item.ini.",
                    IniFieldKind.Name, "Identity"),
                C(34, "Description (first line)",
                    "Item description — continuation lives on the " +
                    "following physical lines ($7$ color codes).",
                    IniFieldKind.Text, "Identity"),

                C(8, "Item kind", "Family code — drives parsing and " +
                    "which UI can use it. 0x11-0x1e weapons " +
                    "(awoken variants 0x3d-0x42), 22/25 spell books, " +
                    "33 grimoire, 38 accessory, 40 class " +
                    "certificate, 43 bag, 44 mount/usable " +
                    "(Ride enchant), 45 gem, 47 mall mount.",
                    IniFieldKind.Number, "Requirements"),
                C(9, "Equip slot", "0/absent = not equippable " +
                    "(mounts have none). Observed: 2 armor, " +
                    "4 gloves, 5 boots, 7 weapon.",
                    IniFieldKind.Number, "Requirements"),
                C(10, "Train mask", "Bitmask of stats selectable in " +
                    "Mana Infusion (bit k = attribute k). " +
                    "0 = not trainable.", IniFieldKind.Number, "Requirements"),
                C(12, "Use type", "1/2 consumable/right-click use " +
                    "(mounts = 2), 3/5 item-on-item (gems), " +
                    "6 elf item, 4 rejected.",
                    IniFieldKind.Number, "Requirements"),
                C(14, "Required level", "Minimum character level to " +
                    "equip/use.", IniFieldKind.Number, "Requirements"),
                C(15, "Max level", "Level cap for work items — " +
                    "0 = no cap.", IniFieldKind.Number, "Requirements"),
                C(19, "Quality", "Base quality/color — cube default.",
                    IniFieldKind.Number, "Requirements"),

                C(11, "Flags (i64)", "Bitfield. Low byte bit7 = binds " +
                    "on equip; high bits: 8 weapon, 0x20 armor, " +
                    "0x10 accessory.", IniFieldKind.Flags, "Flags"),
                C(30, "Flag", "Undocumented flag column.",
                    IniFieldKind.Flags, "Flags"),

                C(17, "Mall price", "Alternate-currency price " +
                    "(item mall) — NOT gold. The real gold price " +
                    "is the system price.",
                    IniFieldKind.Number, "Economy"),
                C(28, "Bag capacity", "Slot count for bag items " +
                    "(kind 43 only).", IniFieldKind.Number, "Economy"),
                C(29, "Max stack", "Stack cap for stackables " +
                    "(potions → 100). Empty on weapons.",
                    IniFieldKind.Number, "Economy"),
                C(31, "System price (gold)", "Reference price: NPC " +
                    "buy = price × discount, resell = ×5% × durability, " +
                    "identify = ×5%.", IniFieldKind.Number, "Economy"),
                C(76, "Repair price", "Base repair cost.",
                    IniFieldKind.Number, "Economy"),

                C(21, "Use cooldown (ds)", "Tenths of a second " +
                    "between uses; 0 = none.",
                    IniFieldKind.Number, "Usage & duration"),
                C(22, "Cooldown group", "Items of the same group " +
                    "block each other; 0 = shared global group.",
                    IniFieldKind.Number, "Usage & duration"),
                C(23, "Attached enchant", "S_Enchant id carried by " +
                    "this item. On right-click-usable items it runs " +
                    "on use — a MOUNT is: kind 44 (or 47 mall) + " +
                    "use type 2 + an enchant containing command " +
                    "2134 Ride (args: rideKey|flag|seats|" +
                    "combatType) and usually 2051|N = +N% move " +
                    "speed (ex. item 20051 → enchant 58991). On " +
                    "equipped gear it is the item's proc/passive " +
                    "(ex. grimoire 21878 → enchant 46011 '15% on " +
                    "attack': the trigger lives in S_Enchant " +
                    "cols 40-43).",
                    IniFieldKind.Number, "Usage & duration",
                    "T_Enchant.ini"),
                C(24, "Buff duration unit", "2 = minutes, 3 = hours, " +
                    "4 = days — with the amount below.",
                    IniFieldKind.Number, "Usage & duration"),
                C(25, "Buff duration amount", "Quantity in the " +
                    "unit above.", IniFieldKind.Number, "Usage & duration"),
                C(26, "Rent duration unit", "1/4 = minutes, " +
                    "2/5 = hours, 3/6 = days; 0 or ≥7 = permanent. " +
                    "Units 4-6 start the countdown at creation.",
                    IniFieldKind.Number, "Usage & duration"),
                C(27, "Rent duration amount", "Quantity in the " +
                    "rent unit above.", IniFieldKind.Number, "Usage & duration"),
                C(35, "Teaches spell / gem level", "Spell id for " +
                    "books (kind 22/25); gem level for kind 45.",
                    IniFieldKind.Number, "Usage & duration",
                    "T_Spell.ini"),
                C(75, "Cube group ids", "';'-separated cube groups " +
                    "this item can come out of.",
                    IniFieldKind.Text, "Usage & duration"),

                C(37, "Bonus HP", "Equipment HP bonus.",
                    IniFieldKind.Number, "Equipment stats"),
                C(38, "Bonus MP", "Equipment MP bonus.",
                    IniFieldKind.Number, "Equipment stats"),
                C(39, "Bonus STR", "", IniFieldKind.Number, "Equipment stats"),
                C(40, "Bonus AGI", "Native DEX.",
                    IniFieldKind.Number, "Equipment stats"),
                C(41, "Bonus INT", "", IniFieldKind.Number, "Equipment stats"),
                C(42, "Bonus WIS", "Native SPR.",
                    IniFieldKind.Number, "Equipment stats"),
                C(43, "Bonus LCK", "Native LUK.",
                    IniFieldKind.Number, "Equipment stats"),
                C(44, "Attack element", "0 = neutral; indexes the " +
                    "defender's resistance block.",
                    IniFieldKind.Number, "Equipment stats"),
                C(45, "P-ATK", "Weapon physical attack.",
                    IniFieldKind.Number, "Equipment stats"),
                C(46, "M-ATK", "Weapon magic attack.",
                    IniFieldKind.Number, "Equipment stats"),
                C(47, "Attack variance %", "Damage spread — " +
                    "100−var + rand[0, 2·var).",
                    IniFieldKind.Number, "Equipment stats"),
                C(48, "Attack range (raw)", "Native ×0.5 — '4' = " +
                    "range 2.0.", IniFieldKind.Number, "Equipment stats"),
                C(49, "Attack speed (raw)", "Native ×1.5 — " +
                    "'16' = 24.", IniFieldKind.Number, "Equipment stats"),
                C(50, "DEF", "Physical defence.",
                    IniFieldKind.Number, "Equipment stats"),
                C(51, "HIT", "Accuracy.", IniFieldKind.Number, "Equipment stats"),
                C(52, "DODGE", "", IniFieldKind.Number, "Equipment stats"),
                C(53, "CRIT", "", IniFieldKind.Number, "Equipment stats"),
                C(54, "CRIT damage", "", IniFieldKind.Number, "Equipment stats"),
                C(55, "M-CRIT", "", IniFieldKind.Number, "Equipment stats"),
                C(56, "M-CRIT damage", "", IniFieldKind.Number, "Equipment stats"),
                C(57, "Pet equip stat", "Summed into the pet's 4th " +
                    "panel stat (speed).",
                    IniFieldKind.Number, "Equipment stats"),
                C(63, "Resist 1", "Elemental resistances — " +
                    "9 columns (63-71).", IniFieldKind.Number, "Resistances"),
                C(64, "Resist 2", "", IniFieldKind.Number, "Resistances"),
                C(65, "Resist 3", "", IniFieldKind.Number, "Resistances"),
                C(66, "Resist 4", "", IniFieldKind.Number, "Resistances"),
                C(67, "Resist 5", "", IniFieldKind.Number, "Resistances"),
                C(68, "Resist 6", "", IniFieldKind.Number, "Resistances"),
                C(69, "Resist 7 / restrict A", "Also read as " +
                    "restriction flags A: bits 3|4 (0x18) block " +
                    "family-storage deposit.",
                    IniFieldKind.Number, "Resistances"),
                C(70, "Resist 8 / restrict B", "Also read as " +
                    "restriction flags B: bit 0 blocks " +
                    "family-storage deposit.",
                    IniFieldKind.Number, "Resistances"),
                C(71, "Resist 9", "", IniFieldKind.Number, "Resistances"),
                C(72, "Max sockets", "0 = never socketable; 1-6.",
                    IniFieldKind.Number, "Equipment stats"),
                C(73, "Socket open rate %", "Chance per socket roll " +
                    "(30/50/70 observed).",
                    IniFieldKind.Number, "Equipment stats"),
                C(74, "Max durability", "Equipment durability " +
                    "(weapons ~75-130).",
                    IniFieldKind.Number, "Equipment stats"),
            }),

        // ---------- S_Monster.ini — MonsterParser.cs ----------
        ["S_Monster.ini"] = new("S_Monster.ini", "Monsters",
            "Every mob template: vitals, combat stats, resists, " +
            "EXP, spell/drop references (62 columns).",
            "Affects mob stats at zone boot. The English name comes " +
            "from the client's t_monster.ini. Spawn placement is NOT " +
            "here — it lives in scene/monster spawn files. Loot is in " +
            "S_DropItem (gold) and S_ClassDropItem. " + DefaultImpact,
            "t_monster.ini", 2,
            new IniColumn[]
            {
                C(0, "Monster ID", "Unique id — referenced by spawn " +
                    "lists, quests, drop tables.",
                    IniFieldKind.Number, "Identity"),
                C(1, "Model code", "M312040 → model + icon " +
                    "(uiicon/<base>.dds after digit-strip).",
                    IniFieldKind.Code, "Identity"),
                C(2, "Name (Big5)", "Server-side name — players see " +
                    "t_monster.ini. Boss records span TWO physical " +
                    "lines: this column carries \"<Title>\\n<name>\" " +
                    "(e.g. 58001 Justin Allspore) — edit carefully, " +
                    "the line break is part of the value.",
                    IniFieldKind.Name, "Identity"),
                C(3, "Kill category", "DSL KillMonster bucket " +
                    "(S_RewardScore): {2}=mob, {3,4}=elite, " +
                    "{5,6}=boss.", IniFieldKind.Number, "Identity"),
                C(4, "Level", "", IniFieldKind.Number, "Identity"),
                C(5, "Monster type", "1 normal, 2 elite/mini-boss, " +
                    "3 boss, 4-5 special — drives leash rules.",
                    IniFieldKind.Number, "Identity"),
                C(6, "Map marker icon", "mapNNN.dds — boss markers " +
                    "shown on the zone map (world/dungeon bosses " +
                    "only, empty on normal mobs).",
                    IniFieldKind.Code, "Identity"),
                C(7, "Active flag", "1 = enabled/spawnable.",
                    IniFieldKind.Flags, "Identity"),

                C(8, "HP", "", IniFieldKind.Number, "Vitals"),
                C(9, "MP", "", IniFieldKind.Number, "Vitals"),

                C(13, "Attack element/type", "Element of the basic " +
                    "attack — indexes defender resists.",
                    IniFieldKind.Number, "Combat"),
                C(14, "Attack power", "Physical power before " +
                    "variance/buffs.", IniFieldKind.Number, "Combat"),
                C(15, "Attack variance %", "100−var + rand[0, 2·var).",
                    IniFieldKind.Number, "Combat"),
                C(16, "Attack range", "Max melee/ranged reach.",
                    IniFieldKind.Number, "Combat"),
                C(17, "Attack speed", "Swing timer base.",
                    IniFieldKind.Number, "Combat"),
                C(18, "Physical DEF", "Magic DEF is always 0 " +
                    "(native never routes a column to it).",
                    IniFieldKind.Number, "Combat"),
                C(19, "HIT", "", IniFieldKind.Number, "Combat"),
                C(20, "DODGE", "", IniFieldKind.Number, "Combat"),
                C(21, "Crit %", "", IniFieldKind.Number, "Combat"),
                C(22, "Crit damage %", "Clamped [0,300].",
                    IniFieldKind.Number, "Combat"),
                C(23, "Magic crit %", "", IniFieldKind.Number, "Combat"),
                C(24, "Magic crit damage %", "",
                    IniFieldKind.Number, "Combat"),
                C(25, "Resist 1", "Physical/elemental resistances " +
                    "— 9 columns (25-33).", IniFieldKind.Number,
                    "Resistances"),
                C(26, "Resist 2", "", IniFieldKind.Number, "Resistances"),
                C(27, "Resist 3", "", IniFieldKind.Number, "Resistances"),
                C(28, "Resist 4", "", IniFieldKind.Number, "Resistances"),
                C(29, "Resist 5", "", IniFieldKind.Number, "Resistances"),
                C(30, "Resist 6", "", IniFieldKind.Number, "Resistances"),
                C(31, "Resist 7", "", IniFieldKind.Number, "Resistances"),
                C(32, "Resist 8", "", IniFieldKind.Number, "Resistances"),
                C(33, "Resist 9", "", IniFieldKind.Number, "Resistances"),

                C(34, "Aux combat 1", "Undocumented combat column " +
                    "(crit-family block 34-36).",
                    IniFieldKind.Number, "Combat"),
                C(35, "Aux combat 2", "Undocumented combat column.",
                    IniFieldKind.Number, "Combat"),
                C(36, "Aux combat 3", "Undocumented combat column.",
                    IniFieldKind.Number, "Combat"),

                C(37, "Aggro radius (cells)", "×0.5 world units. " +
                    "No attack + no range = static object (chests, " +
                    "altars).", IniFieldKind.Number, "AI"),
                C(38, "AI profile", "S_Ai row id — drives detection " +
                    "chance.", IniFieldKind.Number, "AI"),

                C(40, "Ref 1", "Active references — 10 slots " +
                    "(40-49): S_Spell ids (50xxx-60xxx = mob " +
                    "skills), S_Enchant ids (10xxx-49xxx = procs/" +
                    "buffs), S_ClassDropItem ids (56xxx-63xxx = " +
                    "class-scoped loot). Ranges overlap — the " +
                    "server sorts by real table membership.",
                    IniFieldKind.Number, "References"),
                C(41, "Ref 2", "", IniFieldKind.Number, "References"),
                C(42, "Ref 3", "", IniFieldKind.Number, "References"),
                C(43, "Ref 4", "", IniFieldKind.Number, "References"),
                C(44, "Ref 5", "", IniFieldKind.Number, "References"),
                C(45, "Ref 6", "", IniFieldKind.Number, "References"),
                C(46, "Ref 7", "", IniFieldKind.Number, "References"),
                C(47, "Ref 8", "", IniFieldKind.Number, "References"),
                C(48, "Ref 9", "", IniFieldKind.Number, "References"),
                C(49, "Ref 10", "", IniFieldKind.Number, "References"),

                C(50, "DoD kill score", "Defender-of-Destiny score " +
                    "per kill; 0 = kill not counted.",
                    IniFieldKind.Number, "Rewards"),
                C(59, "Locate mask", "Regional/event gate: 0 = " +
                    "spawnable everywhere; bit(locate) required — " +
                    "bit 5 = US. Non-zero values are all " +
                    "festival/test mobs.",
                    IniFieldKind.Flags, "Identity"),
                C(53, "EXP reward", "Character EXP granted on kill.",
                    IniFieldKind.Number, "Rewards"),
                C(54, "Class EXP (NP)", "Class points granted " +
                    "on kill.", IniFieldKind.Number, "Rewards"),
            }),

        // ---------- S_Spell.ini — SpellParser.cs ----------
        ["S_Spell.ini"] = new("S_Spell.ini", "Spells",
            "Skills & buffs: damage, heal, cost, cast rules, " +
            "class restrictions (45 columns).",
            "Affects all skill behavior server-side. Client label = " +
            "T_Spell.ini; per-level cost/power curves live in " +
            "S_SpellReference; class availability = ClassMask column " +
            "here. " + DefaultImpact,
            "T_Spell.ini", 16,
            new IniColumn[]
            {
                C(0, "Spell ID", "Referenced by S_Monster refs, " +
                    "S_Enchant, books, talents.",
                    IniFieldKind.Number, "Identity"),
                C(1, "Code", "Skill code (J001…).",
                    IniFieldKind.Code, "Identity"),
                C(3, "Blast/motion group", "Animation group id sent " +
                    "in the cast packet — client resolves the visual.",
                    IniFieldKind.Number, "Identity"),
                C(7, "Projectile code", "Non-empty (sNNNNN) = the " +
                    "spell flies to the target before impact.",
                    IniFieldKind.Code, "Identity"),
                C(8, "Fly speed (raw)", "×0.5 world units/s; " +
                    "0 = default 40 u/s.",
                    IniFieldKind.Number, "Identity"),
                C(15, "Category", "Single-letter category ('O').",
                    IniFieldKind.Code, "Identity"),
                C(16, "Name (Big5)", "Players see T_Spell.ini.",
                    IniFieldKind.Name, "Identity"),

                C(17, "Family", "6/7 = class spells (gated by class " +
                    "level); 1/2/4 physical/heal/common.",
                    IniFieldKind.Number, "Dispatch"),
                C(18, "Type", "Dispatch type: 4/5 = heal " +
                    "(HitRestoreSkill), 1 = hostile attack.",
                    IniFieldKind.Number, "Dispatch"),
                C(19, "Op flags (i64)", "Cast-condition bitmask " +
                    "(entity state, target, mount…).",
                    IniFieldKind.Flags, "Dispatch"),
                C(20, "Cast shape", "Target selector: 0 = single, " +
                    "4/5 = area shapes (with shape sub).",
                    IniFieldKind.Number, "Dispatch"),
                C(21, "Shape param", "Auxiliary shape parameter.",
                    IniFieldKind.Number, "Dispatch"),
                C(30, "Shape sub", "1 = direction, 2 = sector, " +
                    "3 = cross (for shapes 4/5).",
                    IniFieldKind.Number, "Dispatch"),
                C(22, "Required class level", "Min class level to " +
                    "learn/cast.", IniFieldKind.Number, "Requirements"),
                C(23, "Class mask (i64)", "bit(classId−1) — which " +
                    "classes can use it; 0x3FFFF = all 18.",
                    IniFieldKind.Flags, "Requirements"),

                C(26, "Cast time (ds)", "Tenths of a second; " +
                    "0 = instant.", IniFieldKind.Number, "Timing"),
                C(27, "Cooldown (ds)", "Down-time after a " +
                    "successful cast.", IniFieldKind.Number, "Timing"),
                C(28, "Range", "Cast range — melee 4, heals ~40. " +
                    "Empty = weapon range.",
                    IniFieldKind.Number, "Timing"),
                C(29, "AoE radius (×2)", "Zone radius for area " +
                    "spells (native ×0.5).",
                    IniFieldKind.Number, "Timing"),

                C(32, "MP cost base", "× S_SpellReference coef ×0.01.",
                    IniFieldKind.Number, "Cost & power"),
                C(33, "Attack element", "Element for resistance " +
                    "checks (6 = lightning, 10 = heal).",
                    IniFieldKind.Number, "Cost & power"),
                C(34, "Elf spell group", "Elf skill-book group id.",
                    IniFieldKind.Number, "Cost & power"),
                C(35, "Damage power", "Direct-damage power " +
                    "(× ref coef /100).",
                    IniFieldKind.Number, "Cost & power"),
                C(36, "Heal power", "Restore power for type 4/5 " +
                    "spells.", IniFieldKind.Number, "Cost & power"),
                C(37, "Variance %", "0 = fixed value.",
                    IniFieldKind.Number, "Cost & power"),
                C(39, "Result enchant", "S_Enchant id applied on " +
                    "impact.", IniFieldKind.Number, "Cost & power",
                    "T_Enchant.ini"),
                C(40, "Result chance %", "0/empty = always.",
                    IniFieldKind.Number, "Cost & power"),
                C(41, "Value", "Result value (native default −9).",
                    IniFieldKind.Number, "Cost & power"),
                C(42, "Upgrade pay type", "1 = gold, 9 = class " +
                    "points.", IniFieldKind.Number, "Cost & power"),
                C(43, "Upgrade cost base", "× S_SpellReference " +
                    "tier rate/100.", IniFieldKind.Number, "Cost & power"),
            }),

        // ---------- S_Enchant.ini — EnchantParser.cs + disasm ----------
        // Layout PROUVÉ par CEnchantQuery::Deserialize @0x85ecf00 :
        // 4 blocs de commande d'effet de 7 champs (cols 9-36 : id en
        // col 9/16/23/30 + 6 args chacun), puis col40 = liste ';'
        // d'index 1-based des blocs que le trigger déclenche, col41 =
        // événement, col42 = chance %, col43 = durée. Les ids de
        // commande viennent de DecodeEffectCommand (EnchantCommands).
        ["S_Enchant.ini"] = new("S_Enchant.ini", "Enchants / procs",
            "Buffs & conditional proc rules: up to 4 effect commands " +
            "(Ride, +stats, on-attack buffs…) plus an optional " +
            "trigger block — event + chance + duration (61 columns).",
            "This is THE proc/buff table. Items reference an enchant " +
            "in their \"Attached enchant\" column: consumables apply " +
            "it on right-click (mounts = command 2134 Ride), equipped " +
            "gear uses the trigger block for on-attack procs, talents " +
            "and spells reference it too. Client label = T_Enchant.ini. " +
            DefaultImpact,
            "T_Enchant.ini", 5,
            new IniColumn[]
            {
                C(0, "Enchant ID", "Referenced by items (attached " +
                    "enchant), spells, monsters, talents.",
                    IniFieldKind.Number, "Identity"),
                C(1, "Group code", "E0302/J515/W64301/A00010 — proc " +
                    "family; often matches the source item/spell code.",
                    IniFieldKind.Code, "Identity"),
                C(3, "Script/spell ref", "sNNNNN script reference " +
                    "when the effect runs through the spell engine.",
                    IniFieldKind.Code, "Identity"),
                C(4, "Level", "Enchant rank (I/II/III…).",
                    IniFieldKind.Number, "Identity"),
                C(5, "Name (Big5)", "Players see T_Enchant.ini.",
                    IniFieldKind.Name, "Identity"),
                C(6, "Elf enchant group", "Groups the integrate-pet " +
                    "enchant family (ElfPetEnchantGroupMap). 0 = none.",
                    IniFieldKind.Number, "Identity"),
                C(7, "Active flag", "bit0 = 1 enabled / cancellable " +
                    "buff.", IniFieldKind.Flags, "Identity"),
                C(8, "Equip condition mask", "Bitmask of conditions " +
                    "for passive enchants: 0 = unconditional; " +
                    "0xA0 (160) = shield-type passives (J515/J588), " +
                    "0x2010 (8208) = generic/use-triggered.",
                    IniFieldKind.Flags, "Identity"),

                C(9, "Command 1 id", "Effect command executed by " +
                    "this enchant. Key ids: 2134=Ride (mount: " +
                    "rideKey|flag|seats|combatType), 2051=MoveSPD %, " +
                    "2084=flat ATK/M-ATK (arg2=M-ATK), 2048=crit " +
                    "rate (arg1=M-CRIT), 2043/2044=crit dmg, " +
                    "2086=flat STR/AGI/INT/WIS/LCK, 2016=MP cost, " +
                    "1921=chain enchant (args: chance|enchantId|" +
                    "chance|flag), 2134+2140=combat mount.",
                    IniFieldKind.Number, "Effect command 1"),
                C(10, "Cmd 1 arg 1", "See command table — e.g. " +
                    "2134: rideKey (ride model id, cf. RideList.ini).",
                    IniFieldKind.Text, "Effect command 1"),
                C(11, "Cmd 1 arg 2", "e.g. 2134: flag.",
                    IniFieldKind.Text, "Effect command 1"),
                C(12, "Cmd 1 arg 3", "e.g. 2134: seats (min 1); " +
                    "2084 arg2 = M-ATK bonus.",
                    IniFieldKind.Text, "Effect command 1"),
                C(13, "Cmd 1 arg 4", "e.g. 2134: ride type — " +
                    "0 = dismounts in combat, ≠0 = combat mount.",
                    IniFieldKind.Text, "Effect command 1"),
                C(14, "Cmd 1 arg 5", "", IniFieldKind.Text,
                    "Effect command 1"),
                C(15, "Cmd 1 arg 6", "", IniFieldKind.Text,
                    "Effect command 1"),

                C(16, "Command 2 id", "Second effect slot — e.g. " +
                    "2051|40 = +40% move speed on mounts, 2048 with " +
                    "arg1 = M-CRIT rate bonus on proc gear.",
                    IniFieldKind.Number, "Effect command 2"),
                C(17, "Cmd 2 arg 1", "", IniFieldKind.Text,
                    "Effect command 2"),
                C(18, "Cmd 2 arg 2", "", IniFieldKind.Text,
                    "Effect command 2"),
                C(19, "Cmd 2 arg 3", "", IniFieldKind.Text,
                    "Effect command 2"),
                C(20, "Cmd 2 arg 4", "", IniFieldKind.Text,
                    "Effect command 2"),
                C(21, "Cmd 2 arg 5", "", IniFieldKind.Text,
                    "Effect command 2"),
                C(22, "Cmd 2 arg 6", "", IniFieldKind.Text,
                    "Effect command 2"),

                C(23, "Command 3 id", "Third effect slot.",
                    IniFieldKind.Number, "Effect command 3"),
                C(24, "Cmd 3 arg 1", "", IniFieldKind.Text,
                    "Effect command 3"),
                C(25, "Cmd 3 arg 2", "", IniFieldKind.Text,
                    "Effect command 3"),
                C(26, "Cmd 3 arg 3", "", IniFieldKind.Text,
                    "Effect command 3"),
                C(27, "Cmd 3 arg 4", "", IniFieldKind.Text,
                    "Effect command 3"),
                C(28, "Cmd 3 arg 5", "", IniFieldKind.Text,
                    "Effect command 3"),
                C(29, "Cmd 3 arg 6", "", IniFieldKind.Text,
                    "Effect command 3"),

                C(30, "Command 4 id", "Fourth effect slot — " +
                    "1921 = EffectNextID chains another enchant " +
                    "(proc-on-proc, e.g. 'if hit, 20% slow').",
                    IniFieldKind.Number, "Effect command 4"),
                C(31, "Cmd 4 arg 1", "", IniFieldKind.Text,
                    "Effect command 4"),
                C(32, "Cmd 4 arg 2", "", IniFieldKind.Text,
                    "Effect command 4"),
                C(33, "Cmd 4 arg 3", "", IniFieldKind.Text,
                    "Effect command 4"),
                C(34, "Cmd 4 arg 4", "", IniFieldKind.Text,
                    "Effect command 4"),
                C(35, "Cmd 4 arg 5", "", IniFieldKind.Text,
                    "Effect command 4"),
                C(36, "Cmd 4 arg 6", "", IniFieldKind.Text,
                    "Effect command 4"),

                C(40, "Triggered command slots", "';'-separated " +
                    "1-based indexes of the command blocks fired " +
                    "when the proc trigger passes (e.g. '1;2;3' = " +
                    "commands 1-3 roll together; '2' = only command " +
                    "2). Empty = always-on enchant (mounts, " +
                    "passives).", IniFieldKind.Text, "Proc trigger"),
                C(41, "Trigger event", "What fires the proc. " +
                    "Observed: 1 = passive/always, 2 = on attack " +
                    "(debuff target), 3 = on being hit (self), " +
                    "4 = on being attacked (affects attacker), " +
                    "5 = on attack or skill cast (buff self), " +
                    "6 = on kill, 8-9 = on death.",
                    IniFieldKind.Number, "Proc trigger"),
                C(42, "Proc chance %", "The 'X% chance' in the item " +
                    "description — 5 = 5% chance on the trigger " +
                    "event.", IniFieldKind.Number, "Proc trigger"),
                C(43, "Proc duration (ds)", "Tenths of a second — " +
                    "100 = 'lasts 10 sec', 50 = 5 sec. Matches the " +
                    "duration in T_Enchant texts.",
                    IniFieldKind.Number, "Proc trigger"),
                C(37, "Aux numeric 1", "Pre-trigger numeric field " +
                    "(+0x2c in CEnchantData) — observed 1-100, " +
                    "likely effect duration/level on use enchants.",
                    IniFieldKind.Number, "Proc trigger"),
                C(38, "Aux id", "Pre-trigger field (+0x30) — " +
                    "observed spell-id-like values (836).",
                    IniFieldKind.Number, "Proc trigger"),
                C(39, "Aux flag", "Pre-trigger flag (+0x32).",
                    IniFieldKind.Number, "Proc trigger"),

                C(44, "Aux numeric 2", "Post-trigger numeric field.",
                    IniFieldKind.Number, "Meta"),
                C(45, "Display code", "Icon/code repeat (A00010, " +
                    "W64301) — matches col1 on gear procs.",
                    IniFieldKind.Code, "Meta"),
                C(46, "Rank/flag", "1-2 observed — enchant rank or " +
                    "application flag.", IniFieldKind.Number, "Meta"),
                C(47, "Flag", "Undocumented flag column.",
                    IniFieldKind.Flags, "Meta"),
                C(56, "Description line 1", "Player-facing effect " +
                    "text ($15$/$B$ codes) — shown in item tooltips.",
                    IniFieldKind.Text, "Meta"),
                C(57, "Description line 2", "While-active text.",
                    IniFieldKind.Text, "Meta"),
                C(58, "Description line 3", "Name repeat / source.",
                    IniFieldKind.Text, "Meta"),
            }),

        // ---------- S_Mission.ini — MissionParser.cs ----------
        ["S_Mission.ini"] = new("S_Mission.ini", "Quests",
            "Quest definitions: level gates, script blocks " +
            "(conditions / actions / rewards), chaining (28 columns).",
            "Quest logic is server-side here; the player's English " +
            "text lives in the client's t_mission.ini (+ c_mission.ini " +
            "data copy). NPC links are in S_Npc. Script syntax: " +
            "\"#:Cmd arg…:Cmd2 …:::\" — 27 known commands " +
            "(If_CharLevel, MonsterKilled, RewardGold, RewardItem…). " +
            DefaultImpact,
            "t_mission.ini", 2,
            new IniColumn[]
            {
                C(0, "Mission ID", "", IniFieldKind.Number, "Identity"),
                C(1, "Script name", "Internal script label " +
                    "(script01).", IniFieldKind.Code, "Identity"),
                C(2, "Name (Big5)", "Quest name — players see " +
                    "t_mission.ini.", IniFieldKind.Name, "Identity"),
                C(4, "Complete limit", "Max completions — counted " +
                    "per day if the daily flag is set, else per " +
                    "session.", IniFieldKind.Number, "Identity"),
                C(5, "Min level", "Minimum character level.",
                    IniFieldKind.Number, "Identity"),
                C(6, "Flags", "bit4 = daily-type (resets at " +
                    "day change); bit2 = keep the counter row " +
                    "until the limit is reached.",
                    IniFieldKind.Flags, "Identity"),
                C(10, "Map name (Big5)", "Zone where the quest " +
                    "applies.", IniFieldKind.Name, "Identity"),
                C(23, "Category/series", "e.g. S002，主線 = main " +
                    "quest line.", IniFieldKind.Text, "Identity"),
                C(25, "Trigger group", "Group id auto-granted when " +
                    "another quest rewards \"TriggerGroup <gid>\" " +
                    "(quest chains).", IniFieldKind.Number, "Identity"),

                C(13, "Conditions script", "#:If_CharLevel … — " +
                    "gates accepting the quest.",
                    IniFieldKind.Script, "Scripts"),
                C(14, "Next step / NPC id", "",
                    IniFieldKind.Number, "Scripts"),
                C(15, "Intermediate script", "",
                    IniFieldKind.Script, "Scripts"),
                C(16, "Actions script", "#:NPCTalk / MonsterKilled " +
                    "… — quest objectives.",
                    IniFieldKind.Script, "Scripts"),
                C(17, "NPC/spell id", "", IniFieldKind.Number, "Scripts"),
                C(19, "Next step A", "", IniFieldKind.Number, "Scripts"),
                C(20, "Rewards script", "#:RewardGold / RewardExp / " +
                    "RewardItem … — what the player gets.",
                    IniFieldKind.Script, "Scripts"),
                C(21, "Next step B", "", IniFieldKind.Number, "Scripts"),
                C(27, "Description", "Long quest text (Big5).",
                    IniFieldKind.Text, "Scripts"),
            }),

        // ---------- S_Achievement.ini — GameDataParsers.cs ----------
        ["S_Achievement.ini"] = new("S_Achievement.ini", "Achievements",
            "Achievement definitions: counters, points, mailed " +
            "rewards (15 columns).",
            "Client label = T_Achievement.ini; the reward mail sends " +
            "the item id below. " + DefaultImpact,
            "T_Achievement.ini", 4,
            new IniColumn[]
            {
                C(0, "Achievement ID", "", IniFieldKind.Number, "Identity"),
                C(1, "Need count", "Triggers required to complete " +
                    "(empty/0 = 1).", IniFieldKind.Number, "Identity"),
                C(2, "Sort index", "", IniFieldKind.Number, "Identity"),
                C(3, "Code", "M025 / AC501 — group codes.",
                    IniFieldKind.Code, "Identity"),
                C(4, "Name (Big5)", "Players see T_Achievement.ini.",
                    IniFieldKind.Name, "Identity"),
                C(5, "Type", "Also cumulative-group key; " +
                    "4 = completes instantly.",
                    IniFieldKind.Number, "Identity"),
                C(14, "Description (Big5)", "",
                    IniFieldKind.Text, "Identity"),

                C(6, "Group key hi", "group = col6×100 + col7.",
                    IniFieldKind.Number, "Rewards"),
                C(7, "Group key lo", "", IniFieldKind.Number, "Rewards"),
                C(8, "Points granted", "Achievement points on " +
                    "completion.", IniFieldKind.Number, "Rewards"),
                C(9, "Reward threshold", "Rewards only mail when " +
                    "the point total crosses this.",
                    IniFieldKind.Number, "Rewards"),
                C(10, "Title granted", "Appellation/title id.",
                    IniFieldKind.Number, "Rewards", "T_Title.ini"),
                C(11, "Item reward", "Item id sent by system mail.",
                    IniFieldKind.Number, "Rewards", "t_item.ini"),
                C(12, "Prestige id", "Align/prestige credited.",
                    IniFieldKind.Number, "Rewards"),
                C(13, "Prestige exp / members", "Number = prestige " +
                    "exp; ';'-list = member achievement ids for " +
                    "groups AC5xx.", IniFieldKind.Text, "Rewards"),
            }),

        // ---------- S_Level.ini — ProgressionParsers.cs ----------
        ["S_Level.ini"] = new("S_Level.ini", "Level curve",
            "Per-level XP requirements and base stats (1-101, " +
            "27 columns).",
            "Changes the whole character progression curve — the " +
            "client shows its own copy (C_Level.ini): keep them in " +
            "sync or displayed stats will lie. " + DefaultImpact,
            "T_Level.ini", -1,
            new IniColumn[]
            {
                C(0, "Level", "", IniFieldKind.Number, "Progression"),
                C(1, "EXP needed", "Character EXP to next level.",
                    IniFieldKind.Number, "Progression"),
                C(10, "Class EXP needed", "Class points (NP) to " +
                    "next class level.", IniFieldKind.Number, "Progression"),
                C(24, "Pet EXP needed", "Familiar EXP threshold.",
                    IniFieldKind.Number, "Progression"),
                C(25, "Tier", "", IniFieldKind.Number, "Progression"),

                C(2, "Base HP", "", IniFieldKind.Number, "Base stats"),
                C(3, "Base MP", "", IniFieldKind.Number, "Base stats"),
                C(4, "Natural DEF", "Added to equipment defence.",
                    IniFieldKind.Number, "Base stats"),
                C(5, "Base STR", "All five base stats are equal " +
                    "per level — class % differentiate.",
                    IniFieldKind.Number, "Base stats"),
                C(6, "Base AGI", "Native DEX.",
                    IniFieldKind.Number, "Base stats"),
                C(7, "Base INT", "", IniFieldKind.Number, "Base stats"),
                C(8, "Base WIS", "Native SPR.",
                    IniFieldKind.Number, "Base stats"),
                C(9, "Base LCK", "Native LUK.",
                    IniFieldKind.Number, "Base stats"),
                C(19, "Attack min", "", IniFieldKind.Number, "Base stats"),
                C(20, "Attack max", "", IniFieldKind.Number, "Base stats"),
                C(21, "Defense min", "", IniFieldKind.Number, "Base stats"),
                C(22, "Defense max", "", IniFieldKind.Number, "Base stats"),
            }),

        // ---------- S_DropItem.ini — DropAndSpeedParsers.cs ----------
        ["S_DropItem.ini"] = new("S_DropItem.ini", "Drop tables",
            "Per-mob loot tables + gold; also contents of openable " +
            "items (176 columns).",
            "Row id = monster id (loot on kill) or item id (openable " +
            "contents). From column 10: repeating 4-field entries — " +
            "mobs use rate|itemId|name|qty, openable items use " +
            "itemId|name|qty|rate. " + DefaultImpact,
            null, 1,
            new IniColumn[]
            {
                C(0, "Table id", "Monster id or openable-item id.",
                    IniFieldKind.Number, "Header"),
                C(1, "Name (Big5)", "Mob/item name.",
                    IniFieldKind.Name, "Header"),
                C(3, "Gold chance %", "Chance the mob drops gold.",
                    IniFieldKind.Number, "Gold"),
                C(4, "Gold center", "Average gold amount.",
                    IniFieldKind.Number, "Gold"),
                C(5, "Gold spread", "± spread around the center.",
                    IniFieldKind.Number, "Gold"),
            }),

        // ---------- S_ClassDropItem.ini — GameDataParsers.cs ----------
        ["S_ClassDropItem.ini"] = new("S_ClassDropItem.ini",
            "Class drop tables",
            "Loot tables per monster class — repeating " +
            "chance|itemId|name|qty groups (85 columns).",
            "Referenced by S_Monster reference columns. " +
            DefaultImpact,
            null, -1,
            new IniColumn[]
            {
                C(0, "Table id", "Monster-class/group id.",
                    IniFieldKind.Number, "Header"),
            }),

        // ---------- S_Store.ini — ShopAndMiscParsers.cs ----------
        ["S_Store.ini"] = new("S_Store.ini", "NPC shops",
            "Vendor inventories — repeating itemId|name|qty " +
            "triplets from column 3.",
            "Referenced by S_Npc services (type 3 = shop). " +
            DefaultImpact,
            null, -1,
            new IniColumn[]
            {
                C(0, "Store id", "", IniFieldKind.Number, "Header"),
            }),

        // ---------- S_Npc.ini — WorldParsers.cs ----------
        ["S_Npc.ini"] = new("S_Npc.ini", "NPCs",
            "NPC records — TWO lines per NPC: head " +
            "\"id|modelcode|title\" then body \"name|flags|…|script|…|" +
            "role|\" (or a merged 21-field line for service NPCs).",
            "NPC dialog/services are in the body script columns " +
            "(#:Service …, #:Talk …, #fn#arg#). Shop refs point to " +
            "S_Store; names shown client-side come from t_npc.ini. " +
            "Editing a body line without its head (or vice-versa) " +
            "breaks the record — keep pairs together. " + DefaultImpact,
            "t_npc.ini", -1,
            new IniColumn[]
            {
                C(0, "NPC id / name", "On head lines: NPC id. " +
                    "On body lines: display name (Big5).",
                    IniFieldKind.Text, "Identity"),
                C(1, "Model code / flag", "Head: model code " +
                    "(n106010). Body: first flag.",
                    IniFieldKind.Text, "Identity"),
                C(2, "Title / flag", "Head: title (Big5). " +
                    "Body: flag.", IniFieldKind.Text, "Identity"),
            }),

        // ---------- S_ItemChest.ini — ShopAndMiscParsers.cs ----------
        ["S_ItemChest.ini"] = new("S_ItemChest.ini", "Cube recipes",
            "Magic-cube combination recipes: ingredients → product " +
            "(32 columns).",
            "Product ≥100000 = cube group → pool = items whose " +
            "cube-group column matches. " + DefaultImpact,
            null, -1,
            new IniColumn[]
            {
                C(0, "Recipe id", "", IniFieldKind.Number, "Identity"),
                C(1, "Main ingredient", "Item id of the main input.",
                    IniFieldKind.Number, "Ingredients", "t_item.ini"),
                C(5, "Aux quality", "Required quality of auxiliary " +
                    "ingredients.", IniFieldKind.Number, "Ingredients"),
                C(6, "Main qty", "Amount of the main ingredient.",
                    IniFieldKind.Number, "Ingredients"),
                C(19, "Gold cost", "Gold charged on combine.",
                    IniFieldKind.Number, "Result"),
                C(20, "Success rate %", "Cube roll.",
                    IniFieldKind.Number, "Result"),
                C(21, "Product", "<100000 = item id; ≥100000 = " +
                    "cube group id.", IniFieldKind.Number, "Result",
                    "t_item.ini"),
                C(22, "Required level", "999 = unrestricted.",
                    IniFieldKind.Number, "Result"),
                C(24, "Output qty", "", IniFieldKind.Number, "Result"),
            }),

        // ---------- S_Talent.ini — GameDataParsers.cs ----------
        ["S_Talent.ini"] = new("S_Talent.ini", "Class talents",
            "Talent trees — slots of \"spellId v1 v2 v3 v4 v5\" " +
            "in columns 2-9.",
            "Talent spells must exist in S_Spell. " + DefaultImpact,
            null, -1,
            new IniColumn[]
            {
                C(0, "Talent id", "", IniFieldKind.Number, "Identity"),
                C(1, "Class name (Big5)", "Owning class.",
                    IniFieldKind.Name, "Identity"),
            }),

        // ---------- S_Title.ini — GameDataParsers.cs ----------
        ["S_Title.ini"] = new("S_Title.ini", "Titles",
            "Player titles with stat bonuses.",
            "Granted by achievements (title column) and scripts. " +
            "Client label = T_Title.ini. " + DefaultImpact,
            "T_Title.ini", 1,
            new IniColumn[]
            {
                C(0, "Title id", "", IniFieldKind.Number, "Identity"),
                C(1, "Name (Big5)", "", IniFieldKind.Name, "Identity"),
                C(2, "Type", "", IniFieldKind.Text, "Identity"),
                C(3, "Spell id", "Passive bonus spell.",
                    IniFieldKind.Number, "Identity", "T_Spell.ini"),
                C(4, "Bonus description", "", IniFieldKind.Text, "Identity"),
                C(5, "Obtain condition", "", IniFieldKind.Text, "Identity"),
            }),

        // ---------- S_Node.ini — GameDataParsers.cs ----------
        ["S_Node.ini"] = new("S_Node.ini", "Maps / zones",
            "Map definitions: dimensions, neighbor zones, POIs. " +
            "Column positions AFTER the Sxxx map code are " +
            "anchor-relative — see GameDataParsers.ParseNodes.",
            "Map logic also exists client-side (data/scene/*.ini " +
            "must match); playable worlds are also declared in the " +
            "worlds DB table. " + DefaultImpact,
            "t_node.ini", 1,
            new IniColumn[]
            {
                C(0, "Node id", "", IniFieldKind.Number, "Identity"),
                C(1, "Name (Big5)", "Map name — players see " +
                    "t_node.ini.", IniFieldKind.Name, "Identity"),
            }),
    };

    /// <summary>Schéma détaillé pour `fileName`, ou un schéma
    /// générique (titre + utilité courte, colonnes non documentées)
    /// quand la table n'est pas encore cartographiée.</summary>
    public static IniTableSchema For(string fileName)
    {
        if (ByFile.TryGetValue(fileName, out var s)) return s;
        return Generic(fileName);
    }

    /// <summary>La table a-t-elle un schéma documenté ?</summary>
    public static bool HasSchema(string fileName) =>
        ByFile.ContainsKey(fileName);

    /// <summary>Schéma de repli — utilité déduite du nom de fichier,
    /// aucune colonne documentée (le formulaire affiche
    /// « col N — undocumented »).</summary>
    public static IniTableSchema Generic(string fileName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var nice = stem.StartsWith("S_", System.StringComparison.Ordinal)
            ? stem[2..] : stem;
        return new IniTableSchema(fileName, nice,
            "Server data table — columns not mapped yet. Every " +
            "column is still editable below as \"col N\"; check " +
            "server/src/Eden.Core/Data/Parsers for the native " +
            "layout.",
            DefaultImpact,
            TransFileFor(fileName), -1,
            System.Array.Empty<IniColumn>());
    }

    /// <summary>T_*.ini client associé à un fichier serveur —
    /// convention `S_Xxx.ini` → `t_xxx.ini` (résolution insensible à
    /// la casse faite par TranslationCatalog).</summary>
    public static string? TransFileFor(string iniFileName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(iniFileName);
        if (stem.StartsWith("S_", System.StringComparison.Ordinal))
            return "t_" + stem[2..] + ".ini";
        return null;
    }
}
