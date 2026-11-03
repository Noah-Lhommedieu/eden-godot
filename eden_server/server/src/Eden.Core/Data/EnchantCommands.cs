namespace Eden.Core.Data;

/// <summary>
/// Table numéro→nom des commandes d'effet S_Enchant (blocs cmd+6 vars,
/// col 9+7k). Extraite par objdump du dispatch de
/// <c>CSpellDBO::DecodeEffectCommand</c> @0x8380ad0 (ZoneServer) —
/// fonction en timeout dans l'export Ghidra : pour chaque
/// <c>cmp edx,&lt;id&gt;</c> + <c>je</c>, la chaîne passée au premier
/// <c>ostringstream &lt;&lt; PKc</c> du bloc donne le nom.
/// Familles observées : 1xxx restauration/ciblage direct,
/// 18xx transport/items, 20xx/21xx attributs et buffs,
/// 29xx gains items/XP, 30xx variantes HP/MP, 31xx checks enchant,
/// 5xxx elf, 6xxx life-skill/pet, 70xx cible, 80xx équipement,
/// 90xx shops/fonctions UI.
/// </summary>
public static class EnchantCommands
{
    /// <summary>Nom de commande par id numérique (S_Enchant col 9+7k).
    /// Les ids absents de la table sont des commandes non résolues
    /// (cas via registres doubles ou dispatch indirect).</summary>
    public static readonly IReadOnlyDictionary<int, string> Names =
        new Dictionary<int, string>
    {
        [1] = "SetMigrationInvincible",
        [2] = "Weak",
        [1001] = "HP",
        [1004] = "HPPlusRate",
        [1011] = "MP",
        [1014] = "MPPlusRate",
        [1021] = "Damage2HP caster",
        [1022] = "EMP 100",
        [1023] = "PetDedicate",
        [1031] = "MultiStroke",
        [1051] = "NP",
        [1071] = "DeSummon",
        [1072] = "PetInstruction",
        [1073] = "PetSuicide",
        [1074] = "SummonInEventArea",
        [1075] = "SummonInEventArea",
        [1076] = "ItemTriggerEvent",
        [1120] = "AttrHateReset",
        [1121] = "AttrHatePlus",
        [1122] = "RideSpell",
        [1802] = "DistanceGoto",
        [1803] = "DistanceGoto",
        [1804] = "DistanceGoto",
        [1805] = "ItemTransportArea",
        [1807] = "DistanceSummon",
        [1809] = "DistanceGoto",
        [1811] = "RevivePlus",
        [1812] = "Revive",
        [1821] = "EnchantKillRandom",
        [1822] = "EnchantCancel",
        [1823] = "EnchantKills",
        [1824] = "EnchantKillRandomForce",
        [1825] = "EnchantIncrease",
        [1901] = "DrillItemPlus",
        [1911] = "Repair",
        [1912] = "Repair",
        // 1913 : sélection par args — "PlayEffect 0" (effet visuel).
        [1913] = "PlayEffect",
        [1914] = "UnBind",
        // 1921 : sélection par args — "EffectNextID" /
        // "EffectNextID2Other" (chaîne appliquée à la cible).
        [1921] = "EffectNextID",
        [1997] = "LifeFloat",
        [1999] = "SpellAttack",
        [2000] = "LifeFloat",
        [2001] = "AttrMaxHPPlus",
        // 2002 : « AttrMaxHPRate » (chaîne 0x087b40ff) — % plafond
        // HP ; confirmé par les titres S_Title (ench 59465 :
        // « HP和MP上限3% » = 2002|3 + 2013|3, la part MP passe par
        // la commande AttrMaxMPRate séparée).
        [2002] = "AttrMaxHPRate",
        [2003] = "AttrMaxHP",
        [2011] = "AttrMaxMPPlus",
        [2012] = "CostMPPlus 1",
        [2013] = "AttrMaxMPRate",
        [2014] = "AttrMaxMP",
        [2015] = "CostMPPlus 0",
        [2016] = "CostMP",
        [2021] = "Damage2HP caster",
        [2022] = "Damage2MPPlus caster",
        [2023] = "Damage2MP caster",
        [2031] = "MultiStroke",
        [2032] = "MultiStroke",
        // 2033/2034 : DecodeEffectCommand @0x8380ad0 émet pour chacun
        // `DamageAddPlus <attr 3|4|8> 100 <argN>` (args 0-2) — tag
        // d'enchant dont Execute est un no-op (@0x08520d30), lu par la
        // formule de dégâts. (Ancien label « MultiStroke » erroné.)
        [2033] = "DamageAddPlus",
        [2034] = "DamageAddPlus",
        [2035] = "OnAttackBuffBePhyCritical",
        [2036] = "DamageAddPlusPlus",
        // 2039 : sélection par args — "HealPlus 0" / "HealPlus 1".
        [2039] = "HealPlus",
        // 2047 : sélection par args — "HealPlus 4" / "HealPlus 5".
        [2047] = "HealPlus",
        // 2048 : sélection par args — "AttrCriticalRate 0" /
        // "AttrMagicCriticalRate 0".
        [2048] = "AttrCriticalRate",
        [2037] = "SummonPet",
        // 2040 : vars → « AttrCriticalRate 2 » / « AttrMagicCriticalRate 2 »
        // (dispatch `cmp edx,2040` → bloc @0x838cfb6, chaînes lues dans
        // le binaire) — canal % flottant kinddata+0x104/+0x110, facteur
        // (1+pct) sur le numérateur de PhysicalCriticalRate /
        // MagicCriticalRate.
        [2040] = "AttrCriticalRate 2",
        [2041] = "MusouCheck",
        // 2043 : vars → « AttrCriticalDamage 0 » / « AttrMagicCriticalDamage 0 »
        // (`cmp edx,0x7fb` @0x8384b38 → bloc @0x8391176) — stats joueur
        // DIRECTES (+0xfc/+0x108), pas du per-spell : le crit dmg
        // magique par sort est la commande 2099.
        [2043] = "AttrCriticalDamage",
        [2045] = "SummonInEventArea",
        [2051] = "AttrMovement",
        [2052] = "SummonPet",
        [2054] = "SummonPet",
        [2056] = "AttrAttackSpeedPlus 0",
        [2058] = "AttrAttackSpeedPlus 1",
        [2059] = "AttrCastFailRate",
        [2061] = "AttrCastFailRate",
        [2063] = "AttrSpellDamage",
        [2064] = "AttrSpellDamageRate",
        [2065] = "AttrSpellDuration",
        [2066] = "AttrSpellChangeEffectID",
        [2067] = "AttrCastSuccessRate",
        [2068] = "AttrAttackSpeedPlus 2",
        [2069] = "AttrCastTimePlus 2",
        [2072] = "SummonPetPlus",
        [2073] = "SummonPet",
        // 2081 : vars 0-4 → « AttrBase Str/Dex/Int/Spr/Luk 1 » (mode 1,
        // valeur = S_SpellReference[niveau] × var × 0.01 — corps
        // @8389821, erreur précédente corrigée).
        [2081] = "AttrBase 1",
        // 2082 : sélection par args — "AttrDefence 1" /
        // "AttrHitRate 1" / "AttrDodgeRate 1" / "AttrPhysicoDamage 1" /
        // "AttrMagicDamage 1" (mode 1, refs +0x58/+0x5c/+0x5e/+8/+0xc).
        [2082] = "AttrDefence",
        // 2083 : vars → « AttrPhysicoDamageRate » / « AttrDefenceRate » /
        // « AttrMagicDamageRate » (% — kinddata +0xe0/+0xf0/+0xe8) ;
        // l'ancienne entrée « AttrMaxMPPlus » venait d'un bloc voisin.
        [2083] = "AttrPhysicoDamageRate",
        // 2084 : sélection par args — "AttrPhysicoDamage 0" /
        // "AttrDefence 0" / "AttrMagicDamage 0".
        [2084] = "AttrPhysicoDamage",
        // 2085 : vars 0-4 → chaînes « AttrBase Str/Dex/Int/Spr/Luk 2 »
        // (canal 2 = taux %) — titres « 全能力提高 N% ».
        [2085] = "AttrBaseRate",
        // 2086 : vars 0-4 → chaînes « AttrBase Str/Dex/Int/Spr/Luk 0 »
        // (canal 0 = flat) — LA commande des titres (136/312 enchants
        // de S_Title) ; ordre Str, Agi, Int, Wis, Lck confirmé par
        // S_Enchant 53090-53094 croisé avec les textes de bonus.
        [2086] = "AttrBase",
        // 2087 : var0 → « AttrHitRate 0 », var1 → « AttrDodgeRate 0 »
        // (corps @83898b7 — flats, PAS de la vitesse d'attaque ;
        // l'ancien nom venait d'un bloc voisin).
        [2087] = "AttrHitRate",
        // 2038 : sélection par args — « HealPlus 2 » (arg0, soins
        // prodigués) / « HealPlus 3 » (arg1, soins reçus) — désasm
        // du bloc @0x8392e28 (cmp edx,0x7f2 dans DecodeEffectCommand).
        [2038] = "HealPlus",
        // 2044 : sélection par args — « AttrCriticalDamage » (arg0) /
        // « AttrMagicCriticalDamage » (arg1) — désasm @0x838e8d8.
        [2044] = "AttrCriticalDamage",
        // 2053 : sélection par args — arg0 → « AttrCastTime »,
        // args1-4 → « AttrWeaponCastSpeedRate » /
        // « AttrWeaponAttackSpeedRate » (par type d'arme).
        [2053] = "AttrCastTime",
        // 2088 : sélection par args — « AttrHitRateRate » (arg0) /
        // « AttrDodgeRateRate » (arg1) — désasm @0x838f1a2 (corrigé :
        // l'ancienne entrée « SpellAttack » était fausse).
        [2088] = "AttrHitRateRate",
        [2092] = "SpellAttack",
        // 2095 : corps @838d67e, 6 vars → « AttrResist » (résistances
        // élémentaires — titre 53140 « 自然抗性提高3% » var4). Le nom
        // précédent « AttrSpellPhyCriRates » venait du cas 2096 voisin.
        [2095] = "AttrResist",
        [2096] = "AttrSpellPhyCriRates",
        [2097] = "AttrSpellPhyCriDamages",
        [2098] = "AttrSpellMagicCriRates",
        [2099] = "AttrSpellMagicCriDamages",
        // 2101 : vars → « AttrResist » (résistances physiques —
        // titre 53143 « 斬劈耐性提高3% » var0).
        [2101] = "AttrResist",
        // 2102/2103/2104 : vars → « AttrWeaponDamageRate 0 » —
        // atk/matk % par type d'arme (titres « X大師 » :
        // 58219 匕首+2.5% var0, 58226 太刀+7.5% var3…).
        [2102] = "AttrWeaponDamageRate",
        [2103] = "AttrWeaponDamageRate",
        [2104] = "AttrWeaponDamageRate",
        [2105] = "OnAttackBuffBePhyCritical",
        [2106] = "TransformPlus",
        [2108] = "AttrShieldBlock",
        [2109] = "AttrWeaponParryRate",
        [2110] = "AttrWeaponParryRate",
        [2112] = "OnAttackBuffBePhyCritical",
        [2113] = "EffectChange",
        [2115] = "EffectChange",
        [2117] = "AttrBlock",
        [2119] = "AttrHateHot",
        [2121] = "AttrHateHot",
        [2122] = "AttrHateRate",
        [2129] = "EffectChange",
        [2130] = "SpellEffectChange",
        [2131] = "Transforms",
        [2133] = "ModelAlpha",
        [2134] = "Ride",
        [2135] = "LevelCheck",
        [2136] = "TransformPlus",
        [2137] = "LevelCheckPlus",
        [2138] = "LevelCheckPlus",
        [2140] = "RideSpell",
        [2141] = "AttrSpellCoolDown",
        [2143] = "AttrSpellRange",
        [2144] = "OnAttackBuffPhyCritical",
        [2145] = "OnAttackBuffMagicCritical",
        [2146] = "OnAttackBuffDodge",
        [2148] = "OnAttackBuffBePhyCritical",
        [2149] = "OnAttackBuffBeMagicCritical",
        [2150] = "AttrSpellRadius",
        [2151] = "OnAttackBuffPerry",
        [2152] = "OnBuffEndBuff",
        [2153] = "AttrType",
        [2155] = "AttrType",
        [2156] = "AttrCastFailRate",
        [2157] = "AttrSpellMagicCriRates",
        [2158] = "PetAttackGiveBuff",
        [2159] = "PetPowerUp",
        [2902] = "ItemDropRateUp",
        [2903] = "ItemDropCntUp",
        [2904] = "ItemDropCntUpTime",
        [2911] = "ItemExpRateUp",
        [2912] = "ItemWakenNpRateUp",
        [2913] = "TrainSelf",
        [2914] = "ItemWakenNpRateUp",
        [2931] = "AttrGainExp",
        [2932] = "AttrGainPrestigeExp",
        [2933] = "AttrGainCollectItem",
        [2936] = "ResetEquipTraining",
        [2941] = "GainForeverGold",
        // 2942 : sélecteur multi-devises — objdump @0x838621e
        // `cmp edx,0xb7e` → `je 0x838a14c` : le corps compare chaque
        // var à 0 et émet une commande par var non nulle —
        // var0 « GainLifelongGold » (0x8878f73, Crystal Cross Medal),
        // var1 « GainColosseumGold » (0x8878f85, Honor Stars),
        // var2 « GainWarCoin » (0x8878f98, Battle Merit),
        // var3 « GainAlpacaCoin » (0x8878fa5),
        // var4 « GainKnightCoin » (0x8878fb5, special coin type 10).
        // L'ancienne entrée « EnchantCheckByType » venait d'un bloc
        // voisin (3101/3102).
        [2942] = "GainCoin",
        // 2943 : « GainSpecialCoin <type> <money> » — objdump
        // @0x838dd7f : dans la branche edx>0xb7e, `cmp edx,0xb80` +
        // `jb 0x838df56` (donc edx==0xb7f) émet la chaîne 0x8878fc5
        // + var0 + var1 → CEC_GainSpecialGold → DBA
        // « GMGainSpecialCoin Type = %d Money = %d ».
        [2943] = "GainSpecialCoin",
        [2944] = "SlotFlicker",
        [2945] = "SpellToSpell",
        [3001] = "AttrCastFailRate",
        [3002] = "HP",
        [3011] = "MPPlusPlus",
        [3012] = "MP",
        [3013] = "HPPlusAttr",
        [3024] = "HPPlusRate",
        [3025] = "MPPlusRate",
        [3101] = "EnchantCheckByType",
        [3102] = "EnchantCheckByType",
        [3103] = "EnchantCheckBuffStack",
        [4032] = "AttrSkillPoint",
        [5001] = "ElfCollectTime",
        [5003] = "ElfCollectRate",
        [5012] = "ElfCombineRate",
        [6001] = "MusouCheck",
        [6002] = "ItemReCombo",
        [6003] = "ResetAA",
        [6005] = "ClearStrengthen",
        [6007] = "LifeSkillGain",
        [6011] = "SpellCardEffect",
        [6021] = "LevelCheck",
        [6022] = "EnchantCheckEx",
        [6035] = "ChangePetBattleSpell",
        [6036] = "ChangePetIntegrateSpell",
        [6037] = "ChangePetBattleSpellPlus",
        [6038] = "ChangePetIntegrateSpellPlus",
        [7001] = "SummonEquipPet",
        [7002] = "ItemRecoverElfEnergy",
        [7003] = "Effect2Target",
        [7005] = "ItemElfExpOnce",
        [7006] = "PetReGrow",
        [8016] = "EquipReCombo",
        [8018] = "GoldItemPlus",
        [9022] = "OpenShop",
        [9023] = "RoomExtend",
        [9101] = "UseTwoWeapon",
    };
}
