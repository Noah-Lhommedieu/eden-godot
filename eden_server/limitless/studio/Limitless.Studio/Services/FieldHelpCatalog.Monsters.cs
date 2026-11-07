using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Wiki complet des champs de <b>S_Monster.ini</b> (62 col,
/// |V.6|62| — MonsterParser.cs). Mapping colonne→offset prouvé par
/// CMonsterQuery::Deserialize @0x860d790 (zone) : chaque token de la
/// ligne écrit un champ de CMonsterData ; la conversion
/// CMonsterData→CMonsterTemplate (CMonsterDBO::GetAllMonsterTemplate
/// @0x8367910) route ensuite les stats de combat vers le template.
/// Les consommateurs cités par colonne sont les lectures réelles du
/// binaire (BornMonster @0x84b02b0, SummonMonster @0x8495d40,
/// DetermineAttackMode @0x84d8a60, KilledBy @0x849a010,
/// GainKillScore @0x845c1e0, GiveExp @0x84994a0, GiveNp @0x8491a30…).
/// La fenêtre complète avec les valeurs réellement distribuées
/// (compteurs + exemples via IniColumnStats).</summary>
public static partial class FieldHelpCatalog
{
    private static void RegisterMonsters()
    {
        var t = Items["S_Monster.ini"] = new();

        // ---------------- Identity ----------------
        t[0] = E("Unique monster id (→ CMonsterData+0x00, u32 — the " +
            "map key). Referenced by scene spawn files (where the " +
            "mob is placed — NOT in this file), quest scripts " +
            "(MonsterKilled clauses), S_DropItem loot rows keyed by " +
            "this id, S_ClassDropItem, achievement rules and the " +
            "summon column 48 (mobs that spawn THIS mob on " +
            "death/combat). Renumbering means updating every table.",
            recipe: "To add a mob: pick a free id, fill this row, " +
                "then add its EN label in the client's t_monster.ini " +
                "and place it in a scene spawn file.",
            seeAlso: SA("S_DropItem.ini", "S_ClassDropItem.ini",
                "t_monster.ini", "scene spawn files"));
        t[1] = E("Model code (→ CMonsterData+0x04, i64 — the native " +
            "reads the digits after 'M': M312040 → 312040). Triple " +
            "duty: (1) resolves the .kfm animation table via " +
            "sprintf(\"m%06d\") + CMotionMgr::GetKfm in " +
            "GetAllMonsterTemplate @0x8367910; (2) keys BoundInfo.ini " +
            "→ bound radius template+0x28 (default 1.0) and bound " +
            "size template+0x2c (default 5.0); (3) the client icon " +
            "`uiicon/<base>.dds`. Mobs sharing the first digits " +
            "share the model family (M397010 ×1272 rows).",
            seeAlso: SA("BoundInfo.ini", "CMotionMgr"));
        t[2] = E("Server-side name in Big5 — players see " +
            "t_monster.ini. The native splits this field on '\\n' " +
            "and keeps the LAST line in CMonsterData+0x0c: boss " +
            "records are 'title\\nname' (e.g. 21268 " +
            "'＜沼澤霸主＞\\n肌肉鱷魚' → title 'Marsh Master'). " +
            "Keep the line break when editing a boss name. " +
            "Placeholders '新區段可用' (×1095) / '不可用(掉寶衝突)' " +
            "(×509) mark reserved ids.",
            seeAlso: SA("t_monster.ini"));
        t[3] = E("Kill category (→ CMonsterData+0x10 → " +
            "template+0x12) — the evaluation bucket this kill " +
            "feeds: GainKillScore(param_1) switches on it " +
            "(@0x845c1e0): {2}→'monster' counter, {3,4}→'elite', " +
            "{5,6}→'boss' — used by KillMonster <cat> <n> clauses " +
            "in S_RewardScore. Killcat 7 is special-cased in " +
            "GiveExp/GiveNp: goes to SetMonsterKillAchievement " +
            "instead of normal EXP. ≠ Monster type (col 5).",
            KillCategoryValues(),
            "Points per kill come from col 60 (data+0xc4, " +
            "GainKillScore param_2) — 0 there = the kill is not " +
            "counted at all.",
            null, "S_RewardScore.ini", "ZoneEntryFlow.Evaluate.cs");
        t[4] = E("Level (→ CMonsterData+0x14, u8 → template+0x10) — " +
            "displayed level; also the level fed to " +
            "ExpDeclineRate/JpDeclineRate (EXP/NP decay vs player " +
            "level) and to the level-gated aggro rules. Observed " +
            "1-100 (lv1 ×1723, lv90 ×969, lv80 ×700…).");
        t[5] = E("Monster type (→ CMonsterData+0x15, u8) — the " +
            "display/leash class checked by the DSL command " +
            "MonsterTypeCheck <n> (CEC_MonsterTypeCheck) and by " +
            "leash rules: on instance maps (NodeDef.Type≥4) types " +
            "≥2 keep distance-leash while normal mobs never leash. " +
            "Real shipped values are 1-5; the 6-135 ids live on " +
            "placeholder rows.",
            new FieldValueCase[]
            {
                V("1", "normal mob — the standard monster " +
                    "(×7259 — ex. 20069 'Razorfang Tiger')"),
                V("2", "elite / mini-boss — <bracketed> named mobs " +
                    "(×2000 — ex. 20170 '<Bloodtooth Fighter>')"),
                V("3", "boss — zone/dungeon bosses (×2372 — ex. " +
                    "20001 'Rose Knight' lv80)"),
                V("4", "special — event/objective mob (×828 — ex. " +
                    "20620 '<Head Mecha Soldier>')"),
                V("5", "special — rare/alt spawn (×224 — ex. " +
                    "20650 '<Berserker>')"),
                V("6-25", "unused test rows — one or two " +
                    "placeholder mobs each (ids 22416+)"),
                V("100-135", "unused placeholder rows — dev/test " +
                    "mobs, no shipped name"),
            });
        t[6] = E("Map marker icon (→ CMonsterData+0x18, string — " +
            "version>3 token) — the `mapNNN.dds`/`I0NNNN.dds` " +
            "marker broadcast on the zone map for bosses and " +
            "objectives (the native checks the string is " +
            "non-empty before sending it). Empty on normal mobs.",
            new FieldValueCase[]
            {
                V("map044.dds", "standard boss marker (×976)"),
                V("map040-043.dds", "event/quest objective markers " +
                    "(042 ×124, 040 ×58, 041 ×51, 043 ×50)"),
                V("map003/009.dds", "treasure/special markers"),
                V("I00660.dds…", "custom icon files (×8 rows)"),
                V("empty", "no map marker (×11593)"),
            });
        t[7] = E("Active flag (→ CMonsterData+0x1c, u8) — 1 = " +
            "spawnable row. The C# gate is `!= 0` (enabled).",
            new FieldValueCase[]
            {
                V("1", "live mob (×11538)"),
                V("2", "disabled/hidden variant row (×1343)"),
                V("3", "special flag (×15)"),
            });

        // ---------------- Vitals & patience ----------------
        t[8] = E("HP (→ CMonsterData+0x20, u32 → template+0x30) — " +
            "hit points logged at spawn. lv1 mobs ~200, the lv80 " +
            "boss 20001 → 202589, static objects/turrets 9000000 " +
            "(unkillable), '999999' ×1408 = placeholder cap rows.");
        t[9] = E("MP (→ CMonsterData+0x24, u32 → template+0x34) — " +
            "mana pool for the mob's spells (the spell slots of " +
            "cols 44-46 cast from it).");
        t[10] = E("Patience damage attribute mask (→ " +
            "CMonsterData+0x28, u32 bitmask) — which attack " +
            "attribute types drain the mob's patience pool: " +
            "CalculatePatienceAttrDamage tests `col10 & " +
            "1<<(attrType−1)`. Only meaningful with col 12 > 0. " +
            "Observed: 0 ×11338 (patience never drains); non-zero " +
            "on ~1400 patience-enabled elite/boss rows (9, 33, " +
            "511, 146…). Native quirk: when cols 10 AND 12 are " +
            "both 0, the loader forces patience = 100.",
            new FieldValueCase[]
            {
                V("0", "no attribute drains patience (most rows)"),
                V("nonzero bitmask", "bit(attr−1) per attack " +
                    "attribute that chips the patience pool"),
            },
            null, null, "col 11 (direction mask)",
            "col 12 (max patience)");
        t[11] = E("Patience direction mask (→ CMonsterData+0x2c, " +
            "u8) — which ATTACK DIRECTIONS may drain patience " +
            "(AttackResult @0x8416030): the facing difference is " +
            "bucketed 0-4, then `(col11 >> bucket) & 1` must be " +
            "set. Special case: 15 (0xF) SKIPS the direction " +
            "check entirely — patience drains from any side.",
            new FieldValueCase[]
            {
                V("0", "no direction can drain patience " +
                    "(×11378)"),
                V("15 (0xF)", "any direction drains patience — " +
                    "the only other shipped value (×1518, the " +
                    "patience-enabled rows)"),
            });
        t[12] = E("Max patience (→ CMonsterData+0x30, u32) — the " +
            "patience/shield pool initialised at spawn " +
            "(monster+0x78) and depleted by attacks passing the " +
            "col10/col11 masks. MaxPatience @0x83f3d30 clamps to " +
            "9999999; huge values = boss shields that never " +
            "break. EMPTY = no patience system on this mob (and " +
            "the loader writes 100 when cols 10 and 12 are both " +
            "empty). Values like 1000/68080/1906236/2110031 on " +
            "elite+boss rows; one float quirk 55495.15 (parses " +
            "as 55495).",
            new FieldValueCase[]
            {
                V("empty", "no patience pool (×11404 — the " +
                    "loader then writes 100 if col 10 is also " +
                    "empty)"),
                V("1000-2110031", "boss patience pools — the mob " +
                    "shrugs damage until the pool is depleted " +
                    "by allowed attributes/directions"),
            });

        // ---------------- Combat ----------------
        t[13] = E("Attack element (→ CMonsterData+0x34, u8 → " +
            "template+0x70) — element channel of the basic " +
            "attack: indexes the defender's Resist 1-9 block " +
            "(vtable +0xe8 in CalculatePhysicalDamage). Native " +
            "guard: must be 0 or 1-9 — anything else logs " +
            "'Monster resist(%d) is error' and aborts the row.",
            new FieldValueCase[]
            {
                V("1", "physical channel 1 — slash (×3082, boss " +
                    "20001 'Rose Knight')"),
                V("2", "physical channel 2 — strike, the default " +
                    "for standard mobs (×5776)"),
                V("3", "physical channel 3 — pierce (×1476)"),
                V("4", "fire channel (×511 — 20003 'Flame Turret')"),
                V("5", "ice channel (×358)"),
                V("6", "lightning channel (×193)"),
                V("7", "nature channel (×635)"),
                V("8", "holy channel (×176)"),
                V("9", "dark channel (×658)"),
                V("empty", "no element — ×29 static objects"),
            },
            "Channel names proven by title enchants (2101-2103 " +
            "= slash/strike/pierce, 53140 '自然抗性' = nature " +
            "resist on channel 8). The matching resistance " +
            "columns are 25-33.",
            null, "cols 25-33 (resist block)");
        t[14] = E("Attack power (→ CMonsterData+0x38, u32 → " +
            "template+0x4c) — physical power BEFORE variance and " +
            "buffs: `ROUND((100+buff%)×col14×0.01)+flat` " +
            "(CalculatePhysicalDamage @0x83fdec0). lv1 mob 56001 " +
            "→ 186, boss 20001 → 53641, '0'/'1' on static objects " +
            "(a mob with attack ≤1 is decorative — see " +
            "IsStationary/IsStructure).");
        t[15] = E("Attack variance % (→ CMonsterData+0x3c, u8 → " +
            "template+0x44) — damage spread: the native rolls " +
            "`100 − var + rand[0, 2·var)` percent per swing. " +
            "Almost everything uses 10 (×8753); 0 (×1613) = " +
            "perfectly stable damage (objects).");
        t[16] = E("Attack range (→ CMonsterData+0x3e, s16) — max " +
            "reach ×0.5 world units → template+0x40, RAW value " +
            "also kept at template+0x12c (AttackRange = env + " +
            "col16). '3' = melee reach 1.5 (×7388), '5' (×2081), " +
            "24/36 = ranged, 100 = extreme. Empty/0 + empty " +
            "attack = static object (chest, altar, statue — the " +
            "native gives it no move FSM).");
        t[17] = E("Attack speed (→ CMonsterData+0x40, u8 → " +
            "template+0x38) — swing timer base: " +
            "`col17 × (100−buff) / 100`, floored at 12 " +
            "(AttackSpeed @0x83f83b0). Native warns when the " +
            "value is < 9 on a combat mob (killcat ≠ 7/13). " +
            "Observed: 19 ×6497, 50 ×1771 (slow objects), " +
            "12 ×1099.");
        t[18] = E("Physical DEF (→ CMonsterData+0x44, u32 → " +
            "template+0x5c) — `ROUND(col18×(100+buff%)×0.01)+" +
            "flat`, clamp [0,99999] (PhysicalDefence " +
            "@0x83f46e0). NOTE: magic DEF does NOT exist for " +
            "monsters — the template's magic-def field is never " +
            "written by the native conversion (always 0).");
        t[19] = E("HIT (→ CMonsterData+0x48, s16 → template+0x52) " +
            "— accuracy base feeding PhysicalHitRate. Bosses run " +
            "9999/99999 (never miss), objects 1.");
        t[20] = E("DODGE (→ CMonsterData+0x4a, s16 → " +
            "template+0x50) — evasion base feeding " +
            "PhysicalDodgeRate @0x83f6ff0.");
        t[21] = E("Physical crit % (→ CMonsterData+0x4c, s16 → " +
            "template+0x54) — PhysicalCriticalRate @0x83f8660.");
        t[22] = E("Physical crit damage % (→ CMonsterData+0x4e, " +
            "u8 → template+0x58) — `ROUND(param311) + buff + " +
            "col22`, clamped [0,300] (PhysicalCriticalDamage " +
            "@0x83f4690). Typical 10 (×9078).");
        t[23] = E("Magic crit % (→ CMonsterData+0x50, s16 → " +
            "template+0x56) — the magic pendant of col 21.");
        t[24] = E("Magic crit damage % (→ CMonsterData+0x52, u8 " +
            "→ template+0x5a) — the magic pendant of col 22.");
        for (var i = 0; i < 9; i++)
            t[25 + i] = E($"Resist {i + 1} (→ CMonsterData+" +
                $"0x{0x53 + i:x}, u8 → template+0xa8+i×2) — " +
                $"percent of damage absorbed on element channel " +
                $"{i + 1} (read when the attacker's element " +
                "channel matches). 100 = immune; NEGATIVE values " +
                "are shipped (−3, −13, −25…) = the mob takes " +
                "EXTRA damage on that channel.",
                ResistNote());

        // ---------------- Movement & AI ----------------
        t[34] = E("Walk/roam speed (→ CMonsterData+0x5c, u8 → " +
            "template+0x1c ×0.5 world units — the float4 sent in " +
            "ClientSeeMob 298 for idle mobs; native GetMovement " +
            "returns it until the in-combat flag is set). " +
            "Observed: 4 ×7074 (2.0 u/s), 8 ×2374, 0 = static " +
            "objects.",
            seeAlso: SA("col 35 (battle speed)",
                "col 36 (wander radius)"));
        t[35] = E("Battle speed (→ CMonsterData+0x5d, u8 → " +
            "template+0x18 ×0.5 world units — the chasing/" +
            "fighting movement speed; CElfPet::GetMovement also " +
            "reads it for pets). Observed: 14 ×4662 (7.0 u/s), " +
            "22 ×2230, 26 ×1158.");
        t[36] = E("Wander radius (→ CMonsterData+0x5e, s16 → " +
            "template+0x24 ×0.5 world units) — the roam area " +
            "radius: SetNewGoalNormal @0x84ddf20 picks a random " +
            "point in the disc of this radius around the spawn " +
            "point; CAI::Init stores it (min clamp 2.0). " +
            "CAIFSMRoam::Update checks data+0x5e == 0 → mob does " +
            "NOT wander and falls asleep instead. Observed: " +
            "20 ×6515 (10u), 15 ×2502, 0 ×1966 (never roams).");
        t[37] = E("Aggro radius (→ CMonsterData+0x60, u8 → " +
            "template+0x20 ×0.5 world units — conversion " +
            "@0x8367a5, DAT_087cc688=0.5). Passive detection " +
            "radius passed to CircularRangeSearch by " +
            "CAI::DoSeekEnemy AND reused as the call-for-help " +
            "broadcast radius (BroadCastCallForHelp). 24 = " +
            "typical melee mob (×5253 → 12u), 0 = never aggroes " +
            "(×2206), 40-50 = boss/long sight.",
            seeAlso: SA("S_Ai.ini (detection chance)"));
        t[38] = E("AI profile — S_Ai row id (→ CMonsterData+0x64, " +
            "u32 → template+0x86, read by " +
            "CMonsterTemplate::GetAiId @0x84911a0). The native " +
            "aborts the row with 'Can't read AI id(%d) from " +
            "QueryAi' when the id is absent from S_Ai (killcat " +
            "7/13 exempt). The S_Ai row carries: detection " +
            "chance (byte+4), per-slot cast chances for the " +
            "col44-46 spells (bytes +9/+b/+d), 3 AI spell ids " +
            "(+1a/+1e/+22 → template+0x114), special-spell " +
            "chance (+25, drives col 47), call-for-help chance " +
            "(+26), flee/chase flags (+27), random-target bits " +
            "(+2c). AI 164 = bastion crystals (never move).",
            new FieldValueCase[]
            {
                V("2", "standard melee profile (×2269)"),
                V("141", "zero profile — all chances 0 " +
                    "(×2005, passive mobs)"),
                V("1 / 3 / 8 / 9", "common combat profiles " +
                    "(×1502/×1263/×916/×815)"),
                V("164", "bastion/legion crystal — seek=100, " +
                    "3 spells 58023, never moves (×106)"),
                V("242 / 476", "event/pet profiles (×403/×309)"),
            },
            "Example row 164: '164|0|100|0|100|100|100|75|100|" +
            "40|100|||0|30|58023|30|58023|30|58023|0|0|0|0|0|' — " +
            "three (chance 30, spell 58023) pairs.",
            null, "S_Ai.ini");
        t[39] = E("Call-for-help max count (→ CMonsterData+0x68, " +
            "u8) — how many times the mob may broadcast " +
            "'call for help' while in combat: CAIFSMCombat::Update " +
            "requires `mob+0x380 < col39` before " +
            "BroadCastCallForHelp (chance from S_Ai byte+26, " +
            "radius = aggro col 37). Empty = never calls allies. " +
            "Observed: 4 ×1261, 20 ×302, 1 ×107 — set on pack/" +
            "leader mobs only.",
            new FieldValueCase[]
            {
                V("empty", "never calls for help (×11125)"),
                V("4", "up to 4 help broadcasts per fight " +
                    "(×1261)"),
                V("20", "up to 20 broadcasts (×302 — persistent " +
                    "callers)"),
                V("1 / 2 / 5 / 10", "smaller caps on other " +
                    "leaders"),
            },
            null, null, "S_Ai.ini (help chance byte+26)");

        // ---------------- Birth enchants & spells ----------------
        // Layout PROUVÉ par CMonsterQuery::Deserialize @0x860d790 :
        // col40→data+0x6c (enchant appliqué au spawn, flag 0x1000),
        // col41→data+0x70 (nb d'enchants tirés du pool),
        // col42/43→borne lo/hi expansée dans le vector data+0x74
        // (pool d'enchants de naissance, mélangé), col44-46→vector
        // data+0x80 → slots de sorts template+0x108[0..2],
        // col47→data+0x8c → sort signature template+0x74.
        t[40] = E("Birth enchant — guaranteed (→ CMonsterData+0x6c, " +
            "u32) — an S_Enchant id applied to the mob at EVERY " +
            "spawn: BornMonster @0x84b02b0 calls " +
            "EffectToLife(mob, mob, col40, flag 0x1000) " +
            "unconditionally. This is the mob's built-in passive/" +
            "aura (stat buffs, immunities, auras) — NOT a cast " +
            "spell. Empty = no birth enchant (×9695).",
            new FieldValueCase[]
            {
                V("45040", "'Common Monster Buff' — the standard " +
                    "mob stat aura (×343)"),
                V("52343 / 52344", "'Hunter Eagle' family auras " +
                    "(×223/×204)"),
                V("52219-52282", "totem/spirit birth enchants " +
                    "(×199-201 each)"),
                V("50122 / 43433 / 43518", "other birth auras " +
                    "(×199/×110/×94)"),
            },
            null,
            "To give a mob a permanent passive: put its " +
                "S_Enchant id here — it lands at spawn with no " +
                "roll. For a RANDOM buff per spawn, use the pool " +
                "(cols 41-43) instead.",
            "S_Enchant.ini", "T_Enchant.ini");
        t[41] = E("Birth enchant pick count (→ CMonsterData+0x70, " +
            "u8) — how many enchants the mob RANDOMLY picks from " +
            "the col42-43 pool at spawn: BornMonster copies the " +
            "expanded pool, random_shuffles it, then applies the " +
            "first min(col41, poolSize) via EffectToLife flag " +
            "0x20. 0/empty = pool ignored. 1 = one random buff — " +
            "the standard 'variety aura' setup.",
            new FieldValueCase[]
            {
                V("empty", "no random birth enchant (×9507)"),
                V("1", "pick 1 random enchant from the pool " +
                    "(×2453 — most buffed mobs)"),
                V("2", "pick 2 (×239)"),
                V("3", "pick 3 (×413)"),
                V("5-8", "pick 5/6/7/8 — heavy randomization on " +
                    "special rows (×25/×244/×14/×1)"),
            },
            null,
            "Pool = EVERY enchant id in [col42 .. col43]. " +
                "Example: 52171|52217 + col41=1 → the mob spawns " +
                "with ONE random buff among 47 ('Strong'… family).",
            "S_Enchant.ini", "col 42", "col 43");
        t[42] = E("Birth enchant pool LOW (→ expanded into the " +
            "vector CMonsterData+0x74) — first S_Enchant id of " +
            "the random-buff pool: the native expands EVERY " +
            "non-zero id in [col42 … col43] into the pool, " +
            "shuffles it and applies col41 picks at spawn. " +
            "Equal bounds = a single fixed candidate. Mob buff " +
            "enchant ranges live in 43xxx-59xxx.",
            null,
            "Observed pools: 52171→52217 (47 enchants, 'Strong' " +
                "stat family ×595), 54552→54557 ('Immune to " +
                "Immobilize' family ×240), 50118→50118 (single " +
                "'Darkpool Spirit'), 43401→43403 ('Challenge " +
                "Mode'), 48187→48191.",
            null, "S_Enchant.ini", "col 43", "col 41");
        t[43] = E("Birth enchant pool HIGH — last S_Enchant id " +
            "of the range started in col 42; every id between " +
            "the bounds joins the spawn-buff pool. Set equal to " +
            "col 42 for a single-candidate pool.",
            null, null, null, "S_Enchant.ini", "col 42");
        for (var i = 0; i < 3; i++)
            t[44 + i] = E($"Combat spell slot {i + 1} (→ vector " +
                "CMonsterData+0x80 → resolved to SSpellData* → " +
                "template+0x108[" + i + "]) — a S_Spell id the " +
                "mob casts in combat. The native resolves the id " +
                "through the spell map (unknown id → NULL slot). " +
                "In DetermineAttackMode @0x84d8a60 each slot has " +
                "its own cast chance read from the mob's S_Ai " +
                "row (byte +9 for slot 1, +0xb slot 2, +0xd " +
                "slot 3). Empty = unused slot.",
                new FieldValueCase[]
                {
                    V("51001", "'Slam' — the common mob strike " +
                        "spell (×531 in slot 1)"),
                    V("51002-51019", "standard mob skill family " +
                        "(Slam variants, buffs, curses)"),
                    V("51279 / 51280", "'Totem of Grace's " +
                        "Benefit' totem spells (×201 each)"),
                    V("57001-57013", "'Deep Shocks'/'Demon " +
                        "Blood' caster family"),
                    V("58022 / 58023", "'Self-Detruct' / " +
                        "crystal spells (×202/×52)"),
                },
                null, null,
                "S_Spell.ini", "S_Ai.ini (cast chances)");
        t[47] = E("Signature spell (→ CMonsterData+0x8c → " +
            "resolved S_Spell → template+0x74) — a single spell " +
            "the combat FSM queues when it rolls the AI's " +
            "special-cast chance (CAIFSMCombat::Update tests " +
            "template+0x74 ≠ 0 then rolls S_Ai byte+0x25). " +
            "Empty on almost every mob.",
            new FieldValueCase[]
            {
                V("empty", "no signature spell (×12768)"),
                V("51021", "'Cure' — self-heal on support mobs " +
                    "(×120)"),
                V("51020", "'Blood Curse' (×4)"),
                V("57013", "'Demon Blood' (×3)"),
                V("57521", "'Cure' variant (×1)"),
            },
            null, null, "S_Spell.ini", "S_Ai.ini");

        // ---------------- Summon block (cols 48-52) ----------------
        // Prouvé par CMonster::SummonMonster @0x8495d40 : gate
        // `col49 == trigger` + `col48 != 0` + `summons alive <
        // col51` + roll `rand%100 <= col50` → CEventDelayBornMonster
        // spawn de col48 avec effet col52. Appels : roam=1
        // (CAIFSMRoam::Update), combat=2 (CAIFSMCombat/Hunt::Update),
        // mort=3 (KilledBy @0x849a010).
        t[48] = E("Summoned monster id (→ CMonsterData+0x90, u32) " +
            "— the S_Monster id spawned by SummonMonster " +
            "@0x8495d40 when the col49 trigger fires and the " +
            "col50 roll passes (CEventDelayBornMonster). Real " +
            "uses: eggs hatching (20872 'Enormous Egg' → 20861), " +
            "boss phase chains (21268→21269→21270→21271→64559 " +
            "'Marsh Master'), kill-spawns-loot (63225 'Mystic " +
            "Crab' → 63237 'Occultus Pass Treasure Chest'), " +
            "waves of adds (mining kings → 61025, 'Sheep " +
            "Devourer' ×200 → 60574 'Lost Alpaca Tribe Member').",
            null,
            "The spawned mob is a real S_Monster row — " +
                "point this at a valid mob id. Empty = never " +
                "summons anything.",
            null, "col 49 (trigger)", "col 50 (chance)",
            "col 51 (max alive)");
        t[49] = E("Summon trigger (→ CMonsterData+0x94, u32) — " +
            "WHEN the summon is attempted, matched against the " +
            "caller param of SummonMonster: 1 = while roaming/" +
            "idle (CAIFSMRoam::Update), 2 = on entering combat " +
            "(CAIFSMCombat/Hunt::Update — bosses that call adds " +
            "at pull, e.g. 59304 '<Pirate Leader>' → swordsman), " +
            "3 = on death (KilledBy — eggs, chest spawns, boss " +
            "phases). The trigger also picks the summon " +
            "duration: 1 → IdleSummonDuration, 2 → " +
            "CombatSummonDuration, 3 → SummonLifeTime (formula " +
            "paras +0xcc/+0xd4/+0xd8).",
            new FieldValueCase[]
            {
                V("empty", "never summons (×12548)"),
                V("1", "summon while roaming/idle (×2 — " +
                    "64605/64705 event mobs)"),
                V("2", "summon on combat start (×19 — add-" +
                    "caller bosses)"),
                V("3", "summon on death (×327 — eggs, phase " +
                    "bosses, treasure chests)"),
            });
        t[50] = E("Summon chance % (→ CMonsterData+0x98, u8) — " +
            "per-attempt roll: `random()%100 <= col50` spawns " +
            "the col48 mob. 100 = guaranteed (×124 — eggs/" +
            "phase bosses), 5/3/1 = rare event spawns (×107/" +
            "×84 — the 'Mysterious Pot' ambushes), 70/75 mid. " +
            "Empty = summon never fires.",
            new FieldValueCase[]
            {
                V("empty", "no summon (×12547)"),
                V("100", "guaranteed spawn (×124)"),
                V("5", "5% rare spawn — 'Mysterious Pot' " +
                    "families (×107)"),
                V("1", "1% very rare spawn (×84)"),
                V("70-75", "frequent spawn (×16)"),
            });
        t[51] = E("Max simultaneous summons (→ CMonsterData+0x99, " +
            "u8) — the gate before every summon attempt: the mob's " +
            "live summon vector (monster+0x374) must hold FEWER " +
            "than col51 entries. '1' = one spawn at a time " +
            "(×329), 2/4/5/20 for multi-wave spawners (64526-28 " +
            "fragments → 20). Empty = cap 0 = never.",
            new FieldValueCase[]
            {
                V("empty", "cap 0 — no summons (×12547)"),
                V("1", "one summon at a time (×329)"),
                V("2-20", "multi-summon spawners (×19)"),
            });
        t[52] = E("Summon spawn effect id (→ CMonsterData+0x9c, " +
            "u32) — the born effect attached to " +
            "CEventDelayBornMonster; 0 falls back to " +
            "CMonster::DefaultSummonEffect (formula paras " +
            "+0x114). NOTE: the column is numeric — the 8 rows " +
            "carrying 'E15021' parse to 0 (strtol stops at 'E') " +
            "and silently get the default effect; treat it as a " +
            "dead marker, not a working code.");

        // ---------------- Rewards & meta ----------------
        t[53] = E("EXP reward (→ CMonsterData+0xa0, u32 → " +
            "template+0x14) — character EXP granted on kill: " +
            "GiveExp @0x84994a0 scales it by " +
            "ExpDeclineRate(mob level vs player level). Proven " +
            "by native AttribLog: mob 56001 → 'EXP Gain:25' = " +
            "col 54 1-idx. Empty/0 = no EXP (×8836 empty — " +
            "event/object mobs).",
            seeAlso: SA("col 54 (class EXP)", "col 60 (kill score)"));
        t[54] = E("Class EXP / NP (→ CMonsterData+0xa4, u32 → " +
            "template+0x88) — class points granted on kill: " +
            "GiveNp @0x8491a30 scales by JpDeclineRate and the " +
            "class-exp rate (AttribLog: 56001 → 'NP Gain:5'). " +
            "Killcat-7 mobs skip this path entirely.");
        t[55] = E("Chest drop 1 (→ CMonsterData+0xa8, u32) — " +
            "S_ItemChest ROW ID of a chest this mob can drop on " +
            "kill: 2240 'Ultramodern Savage Armor Threads', " +
            "3179 'Udder Delight Headdress (White)', 2231 " +
            "'Summer Kimono (White)'… — costume chests on " +
            "dungeon elites/bosses. Chance shared in col 57.",
            seeAlso: SA("S_ItemChest.ini", "t_itemchest.ini",
                "col 57 (chance)"));
        t[56] = E("Chest drop 2 (→ CMonsterData+0xac, u32) — " +
            "second S_ItemChest row id of the kill-drop table " +
            "(usually paired with col 55: 2240+2243, 2083+2087…). " +
            "Chance in col 57.",
            seeAlso: SA("S_ItemChest.ini", "col 57 (chance)"));
        t[57] = E("Chest drop chance % (→ CMonsterData+0xb0, u8) " +
            "— percent roll for the chest drops of cols 55/56/58: " +
            "100 = always (eggs, event mobs — ×766), 50 = the " +
            "common boss value (×635), 15-80 rarer rolls. Empty " +
            "= no chest drop.",
            new FieldValueCase[]
            {
                V("", "no chest drop (×11443)"),
                V("100", "guaranteed chest drop (×766)"),
                V("50", "50% roll — the common boss value " +
                    "(×635)"),
                V("15-80", "rarer rolls (15×30, 30×7, 70×5, " +
                    "40×4, 80×3, 25×2, 75×1)"),
            });
        t[58] = E("Chest drop 3 (→ CMonsterData+0xb4, u32) — " +
            "third S_ItemChest row id of the kill-drop table " +
            "(2231, 784 'Lv36-45 Leg Gear', 3191, 3530…). Chance " +
            "in col 57.",
            seeAlso: SA("S_ItemChest.ini", "t_itemchest.ini",
                "col 57 (chance)"));
        t[59] = E("Locate mask (→ CMonsterData+0xb8, s16) — " +
            "regional/event spawn gate: the native skips the row " +
            "when `mask ≠ 0 && ((mask >> (locate & 0x1f)) & 1) " +
            "== 0` (GetAllRebornMonsterInfo @0x83688a0). Bit 5 = " +
            "US (ConvertLocate @0x8589260). Every non-zero row " +
            "is a festival/test mob (97 barrel/egg rows at 2, " +
            "one at 16).",
            new FieldValueCase[]
            {
                V("0", "spawnable everywhere (×12798)"),
                V("2", "festival/event mobs (×97)"),
                V("16", "regional test mob (×1)"),
            });
        t[60] = E("Kill score points (→ CMonsterData+0xc4, u32 — " +
            "version>4 field) — points ADDED to the evaluation " +
            "score per kill: CharacterKillMonster passes it as " +
            "GainKillScore param_2 (@0x845c1e0: param_2==0 → the " +
            "kill is not counted; else +0x44 += it and the " +
            "col3-category counter increments). Empty on most " +
            "rows; temple guardians 40012-19 → 30, purgatory " +
            "placeholders → 1/3/5, challenge rows → 600/24/60.",
            new FieldValueCase[]
            {
                V("empty", "kill not scored (×12451)"),
                V("30", "standard scored kill (×259 — " +
                    "'<Temple Guardian>' family)"),
                V("600", "high-value kill (×110)"),
                V("24 / 60 / 5 / 2 / 50", "other score tiers " +
                    "(×33/×20/×10/×8/×2)"),
            },
            "WARN: MonsterParser.cs reads KillScore from col 50 " +
                "(0-idx) — the native field is data+0xc4 = col " +
                "60. The observed table below reflects the REAL " +
                "column.",
            null, "col 3 (kill category)", "S_RewardScore.ini");
        t[61] = E("Reserved flag (→ CMonsterData+0xc8, s16 — " +
            "version>5 field) — read by the native loader but no " +
            "consumer found in the zone binary. Observed: 0 " +
            "×12022 (default), 2 ×615, 3 ×254, 1 ×5 — small " +
            "mode/category markers on special rows (Purgatory " +
            "chests → 1, eggs/events → 2/3).");
    }

    /// <summary>Toutes les catégories de kill distribuées —
    /// buckets natifs prouvés par le switch de GainKillScore
    /// @0x845c1e0 ({2}→compteur 'monster', {3,4}→'elite',
    /// {5,6}→'boss') + valeurs réelles de S_Monster.</summary>
    private static FieldValueCase[] KillCategoryValues() => new[]
    {
        V("1", "common mob family (×2689 — ex. 20002 'Flying " +
            "Arrow') — outside the native switch, no score " +
            "counter"),
        V("2", "'monster' bucket (×2646 — ex. 20012 'Crippling " +
            "Device')"),
        V("3", "'elite' bucket (×2687 — ex. 20069 'Razorfang " +
            "Tiger')"),
        V("4", "'elite' bucket (×2461 — ex. 20001 'Rose " +
            "Knight')"),
        V("5", "'boss' bucket (×557 — ex. 20671 'Hope Rainbow " +
            "Egg')"),
        V("6", "'boss' bucket (×1249 — ex. 20005 'Soul of " +
            "Frost')"),
        V("7", "event objects — achievement path, NOT normal " +
            "EXP (×482 — ex. 21193 'Big Ole Bucket')"),
        V("8", "event/special (×9 — ex. 55019 '<Brutal " +
            "Machine>')"),
        V("9", "event/special (×59 — ex. 55011 '<Forest " +
            "Titan>')"),
        V("10", "bastion objective (×26 — ex. 60007 " +
            "'<Protection Stone>')"),
        V("11", "zone-crystal objective (×14 — ex. 60116 " +
            "'Attacker's Outpost Zone Crystal')"),
        V("12", "zone crystal (×2 — ex. 60148 'Zone Crystal')"),
        V("13", "special category (×15 — ex. 20683)"),
    };

    /// <summary>Entrée pour colonne non mappée — la section
    /// « valeurs observées » de la fenêtre reste la vraie
    /// référence.</summary>
    private static FieldHelpEntry Undocumented(string note) =>
        new("This column is NOT mapped in the server parser " +
            "(MonsterParser.cs / ItemParser.cs). " + note);
}
