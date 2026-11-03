# Audit effets de sorts (28/10/2026)

Source : `S_Spell.ini` col 39 (enchant résultat) → `S_Enchant.ini` blocs de commandes (cols 9-36, cmd 160 = imbriquée). 4485 sorts, **4249** avec enchant résultat, **141** commandes distinctes. « Mentionnée » = l'id ou le nom apparaît dans `server/src` (heuristique statique : ne prouve PAS l'exécution complète — preuve runtime = absence de la ligne `[Zone] spell-result cmd …` dans `zone.log`).

Constat runtime (cast Bear's Rage 50388 → enchant 50106) : `#2055`, `#2034`, `AttrMaxHP`, `HealPlus`, `LifeFloat` sont **uniquement loguées** par `ApplySpellResultCommandsAsync` (seul `DistanceGoto` est exécuté sur la voie mob) ; les stats passent par `AddPlayerEnchantAsync`/`ExecuteSpellAttrCommandsAsync` pour la voie joueur.

| Cmd | Nom natif | Sorts | Mentionnée | Statut exécution |
|---|---|---|---|---|
| 1921 | EffectNextID | 979 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2055 | ? | 514 | oui | Voie joueur préexistante ; voie mob = attribut registre (`FillMobEnchantFields` verrous) — no-op `MobRegistryAttrsHandler` |
| 2051 | AttrMovement | 299 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1031 | MultiStroke | 288 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 3001 | AttrCastFailRate | 268 | oui | Voie joueur préexistante ; voie mob = attribut registre (HP/tick) — no-op `MobRegistryAttrsHandler` |
| 1001 | HP | 267 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 7003 | Effect2Target | 240 | oui | **Exécuté** — `Effect2TargetHandler` (modes 1-5, cercle ×0,5), test unitaire + live |
| 2047 | HealPlus | 225 | oui | Voie joueur préexistante ; **voie mob exécutée** — `HealPlusHandler` : types 4/5 = soin flat des vitals du lanceur (`CEC_HealPlus::Execute` — entity+0x8c = attaquant), types 0-3 = no-op (taux spellref), test unitaire |
| 2082 | AttrDefence | 221 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2083 | AttrPhysicoDamageRate | 218 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1004 | HPPlusRate | 218 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2034 | ? | 214 | oui | Tag natif (Execute no-op) — lu par la formule de dégâts ; voie mob no-op `MobRegistryAttrsHandler` |
| 3101 | EnchantCheckByType | 184 | **NON** | **No-op natif** — `Execute` @0x0852c790 = `return 1` (check consulté ailleurs), handler enregistré |
| 1821 | EnchantKillRandom | 134 | oui | **Exécuté** — `EnchantDispel` (voie joueur), test unitaire |
| 2052 | SummonPet | 129 | **NON** | **Différé** — `CNode::BornPet`/`BornTotemPet` : système de pet invocable complet à implémenter (IA, lien maître, vue), hors portée batch commandes |
| 1804 | DistanceGoto | 128 | oui | **Exécuté** — handler Combat/SpellEffects (voie mob), test unitaire |
| 2095 | AttrResist | 101 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1823 | EnchantKills | 101 | oui | **Exécuté** — `EnchantDispel` (voie joueur), test unitaire |
| 1076 | ItemTriggerEvent | 99 | oui | **Différé** — envoie `CNC_ZMis_MissionServerListDialogue` au serveur **Mission** (`MissionServerConnection`) : plomberie Zone→Mission à faire |
| 3002 | HP | 95 | oui | Voie joueur préexistante ; voie mob = attribut registre (HP/tick DoT) — no-op `MobRegistryAttrsHandler` |
| 2038 | HealPlus | 84 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2088 | AttrHitRateRate | 81 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1120 | AttrHateReset | 79 | **NON** | **Exécuté** — handler Combat/SpellEffects (voie mob), test unitaire |
| 2053 | AttrCastTime | 73 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1802 | DistanceGoto | 72 | oui | **Exécuté** — handler Combat/SpellEffects (voie mob), test unitaire |
| 1074 | SummonInEventArea | 65 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2132 | ? | 63 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2037 | SummonPet | 61 | **NON** | **Différé** — idem 2052 (pet invoqué) |
| 2103 | AttrWeaponDamageRate | 61 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2101 | AttrResist | 59 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2152 | OnBuffEndBuff | 58 | **NON** | **Différé** — `CEC_OnBuffEndBuff::Execute` : applique un enchant À LA FIN du buff courant (chance+type) — nécessite un hook « fin de buff » dans le registre mob |
| 3024 | HPPlusRate | 57 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2104 | AttrWeaponDamageRate | 56 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1999 | SpellAttack | 56 | oui | **Exécuté** — `SpellAttackHandler` : la cible mob recaste arg0 vers l'attaquant (`CEC_SpellAttack::Execute` @0x08553900) — pipeline cast mob (wind-up honoré), **preuve live : cast 51546 → clone recaste 51547** (495+289 sur le fil), test unitaire |
| 2002 | AttrMaxHPRate | 55 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 3102 | EnchantCheckByType | 54 | **NON** | **No-op natif** — idem 3101 (`return 1`), handler enregistré |
| 2066 | AttrSpellChangeEffectID | 50 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1807 | DistanceSummon | 46 | **NON** | **Exécuté** — `DistanceSummonHandler` : mob téléporté au contact du lanceur (`CEC_DistanceSummon::Execute` @0x08522bd0 — lerp bounding radii → marge 1.5 u) + PlayEffect broadcast, test unitaire |
| 2033 | MultiStroke | 45 | oui | Tag natif (Execute no-op) — lu par la formule de dégâts ; voie mob no-op `MobRegistryAttrsHandler` |
| 2003 | AttrMaxHP | 41 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2137 | LevelCheckPlus | 41 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2102 | AttrWeaponDamageRate | 41 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2085 | AttrBaseRate | 40 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2117 | AttrBlock | 40 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2118 | ? | 39 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2036 | DamageAddPlusPlus | 36 | **NON** | Tag natif (Execute no-op) — lu par la formule de dégâts, **non câblé** |
| 2056 | AttrAttackSpeedPlus 0 | 34 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2072 | SummonPetPlus | 34 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1809 | DistanceGoto | 33 | oui | **Exécuté** — handler Combat/SpellEffects (voie mob), test unitaire |
| 2945 | SpellToSpell | 33 | oui | **Différé** — `CEC_SpellToSpell::Execute` : remplace des sorts appris par paires via `CNC_CZ_ClientChangeSpell` (feature morph de sorts, 33 enchants) |
| 1121 | AttrHatePlus | 32 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2040 | AttrCriticalRate 2 | 32 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2041 | MusouCheck | 31 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2032 | MultiStroke | 26 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2081 | AttrBase 1 | 26 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2105 | OnAttackBuffBePhyCritical | 25 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1011 | MP | 25 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2068 | AttrAttackSpeedPlus 2 | 24 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2043 | AttrCriticalDamage | 23 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2031 | MultiStroke | 21 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2001 | AttrMaxHPPlus | 21 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 3012 | MP | 20 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2122 | AttrHateRate | 19 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2140 | RideSpell | 19 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2057 | ? | 17 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2133 | ModelAlpha | 17 | oui | Non exécuté (loggé `spell-result cmd`) |
| 1997 | LifeFloat | 17 | oui | **No-op natif** — arg1 flag vide dans toute la donnée réelle → les deux blocs d'`Execute` sautent (tag de formule), enregistré `MobRegistryAttrsHandler` |
| 1825 | EnchantIncrease | 16 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2069 | AttrCastTimePlus 2 | 15 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2044 | AttrCriticalDamage | 14 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1021 | Damage2HP caster | 13 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2064 | AttrSpellDamageRate | 13 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2061 | AttrCastFailRate | 13 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2013 | AttrMaxMPRate | 12 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2108 | AttrShieldBlock | 12 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2158 | PetAttackGiveBuff | 12 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2121 | AttrHateHot | 11 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 3011 | MPPlusPlus | 11 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1824 | EnchantKillRandomForce | 11 | oui | **Exécuté** — `EnchantDispel` (voie joueur), test unitaire |
| 2062 | ? | 11 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2153 | AttrType | 11 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2039 | HealPlus | 10 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2109 | AttrWeaponParryRate | 10 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 3013 | HPPlusAttr | 10 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2129 | EffectChange | 9 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1913 | PlayEffect | 9 | **NON** | **Exécuté** — `PlayEffectHandler` : `CNC_CZ_ClientPlayEffect` (383) au lanceur + can-see (`CEC_PlayEffect::Execute` @0x0854b200 — 5 args → champs paquet), test unitaire ; codec client existant |
| 1072 | PetInstruction | 8 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2016 | CostMP | 8 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 3025 | MPPlusRate | 7 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1014 | MPPlusRate | 7 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1122 | RideSpell | 7 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2046 | ? | 7 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2011 | AttrMaxMPPlus | 6 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2065 | AttrSpellDuration | 6 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2087 | AttrHitRate | 6 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2138 | LevelCheckPlus | 5 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1801 | ? | 5 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1073 | PetSuicide | 5 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2159 | PetPowerUp | 5 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1811 | RevivePlus | 4 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2136 | TransformPlus | 4 | oui | Non exécuté (loggé `spell-result cmd`) |
| 1803 | DistanceGoto | 4 | oui | **Exécuté** — handler Combat/SpellEffects (voie mob), test unitaire |
| 2035 | OnAttackBuffBePhyCritical | 4 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2134 | Ride | 4 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2147 | ? | 4 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2146 | OnAttackBuffDodge | 4 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2091 | ? | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2054 | SummonPet | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2148 | OnAttackBuffBePhyCritical | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2149 | OnAttackBuffBeMagicCritical | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2155 | AttrType | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2944 | SlotFlicker | 3 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2067 | AttrCastSuccessRate | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2154 | ? | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2073 | SummonPet | 3 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2130 | SpellEffectChange | 3 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2144 | OnAttackBuffPhyCritical | 2 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2145 | OnAttackBuffMagicCritical | 2 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2139 | ? | 2 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2084 | AttrPhysicoDamage | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1812 | Revive | 2 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2111 | ? | 2 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2012 | CostMPPlus 1 | 2 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2141 | AttrSpellCoolDown | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2096 | AttrSpellPhyCriRates | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2097 | AttrSpellPhyCriDamages | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2098 | AttrSpellMagicCriRates | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2099 | AttrSpellMagicCriDamages | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2911 | ItemExpRateUp | 2 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2021 | Damage2HP caster | 1 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2151 | OnAttackBuffPerry | 1 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2157 | AttrSpellMagicCriRates | 1 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2156 | AttrCastFailRate | 1 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 1912 | Repair | 1 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2045 | SummonInEventArea | 1 | oui | Non exécuté (loggé `spell-result cmd`) |
| 2131 | Transforms | 1 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2150 | AttrSpellRadius | 1 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 1022 | EMP 100 | 1 | **NON** | Non exécuté (loggé `spell-result cmd`) |
| 2058 | AttrAttackSpeedPlus 1 | 1 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2143 | AttrSpellRange | 1 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |
| 2902 | ItemDropRateUp | 1 | oui | Voie joueur / pipeline stats / procs préexistants (non ré-audité ici) |


## Mise à jour 28/10 — exécuteur générique

`Eden.Zone/Combat/SpellEffects/` : `SpellEffectExecutor` (registre id → handler, `ISpellEffectHost` pour les paquets) remplace le switch de `ApplySpellResultCommandsAsync` ; `EnchantDispel` (sélection pure des dissipations). Handlers prouvés par désasm : DistanceGoto 1802/1803/1804/1809, AttrHateReset 1120 (`CEC_AttrHateReset::Execute` @0x08507570 : table de haine du mob → 1, lanceur = cible forcée), EnchantKills 1823 (@0x0852dc90 : liste d'ids a0..a5), EnchantKillRandom 1821 (@0x0852ea60) et Force 1824 (@0x0852eed0).

Corrections de sémantique découvertes :
- **1823 retire les enchants listés** (ids a0..a5), il ne dissipait pas « tous les debuffs » ; **1821/1824** ne lisent pas `a0` comme count : décodage `DecodeEffectCommand` @0x8380ad0 → `EnchantKillRandom 1 a0 a1 a4 a5` (type buff, chance a0, count a1), `... 2 a2 a3 a4 a5` (type debuff), `EnchantKillRandomForce 2 a0 a1`. Type = filtre `CCheckBufType` @0x0852f2e0 (col7 bit1 = debuff).
- **2033 et 2034 ne sont ni MultiStroke ni des soins** : le décodeur émet `DamageAddPlus <attr 3|4|8> 100 <argN>` ; `CEC_DamageAddPlus::Execute` @0x08520d30 et `CEC_DamageAddPlusPlus::Execute` @0x08520d60 sont des no-op (la valeur est lue par la formule de dégâts). Le code qui les traitait en MultiStroke/heal a été retiré.
- Signatures natives des 322 commandes : `InitEffectCommandTable` @0x080627a0 (dump dans les notes de session `effect-signatures.txt`).

Reste à faire (ordre de valeur) : DamageAddPlus(Plus) dans la formule de dégâts, AttrHatePlus/Hot (table de haine alimentée par les dégâts), Damage2HP (`CEC_Damage2HP::Execute` @0x0851ee80, vol de vie sur hit), SummonPet*, OnAttackBuff*, SpellToSpell, LevelCheckPlus, EnchantCheckByType.
