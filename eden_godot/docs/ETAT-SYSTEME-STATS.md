# ÉTAT DU SYSTÈME DE STATS — sources, ordre, provenance (2026-10-30, maj phase C + A4.5)

Document de référence **avant tout ajout de source de stats**.
L'ordre d'application détermine la valeur finale — une source
branchée au mauvais endroit fausse toutes les dérivées.

⚠️ **Règle d'or** : toute nouvelle source de stats = **un stage
nommé ordonné dans le `StatPipeline`** (`Eden.Zone/Program.cs`),
jamais d'injection ad hoc. Chaque contribution déclare `pre` / `pct`
/ `post` — c'est ça la provenance.

---

## 1. Architecture (5 couches serveur + 1 client)

```
[1] Base         ProfileStatEmitter.ComputeBaseStats = S_Level × S_Class.StatPercents%
[2] Pipeline     StatPipeline — étapes nommées ORDONNÉES (ci-dessous)
[3] Équipement   stages equipment/gems/fortify/costumes (Sum = agrégat snapshot combat, voir §4)
[4] Dérivées     CombatantSnapshot + NativeCombatFormulas (S_Parameter, 677 params)
[5] Émission     ProfileStatEmitter.SendAsync → NCID 296/492/493 (+ vitaux 280/600/605/606)
[6] Client       PlayerVitals → PlayerStats → ProfileWindow/CombatHud/Minimap
```

Fichiers : `eden_server/server/src/Eden.Core/Game/Stats/StatPipeline.cs`
(pipeline), `…/Services/StatsCalculationService.cs` (S_Parameter),
`…/Services/EquipmentStatService.cs` (agrégation équipement),
`eden_server/server/src/Eden.Zone/ProfileStatEmitter.cs` (émission),
`…/Combat/NativeCombatFormulas.cs` + `CombatantSnapshot.cs` (combat),
`Eden.Zone/Program.cs (AddStage)` (enregistrement des stages).

## 2. Formule d'application

`StatPipeline.cs:140-154` — par stat (7 cores : STR INT AGI WIS LCK
+ HP_MAX MP_MAX) :

```
résolu = ⌊(statNiveau + Σ pre) × (classPct + Σ pct) / 100⌋ + Σ post
```
bornée `[0, 32000]` (col 53).

→ **pre** = bonus plats avant % ; **pct** = % cumulés avec le %
classe ; **post** = bonus plats après %. Choisir le bon champ est
la provenance : un +10% STR en `pct` est absorbé par le multiplicateur
classe, en `post` il s'empile après — les deux donnent des résultats
différents.

## 3. Ordre réel des stages (Program.cs, ordre d'enregistrement)

| # | Stage | Source | État |
|---|-------|--------|------|
| — | *(base)* | S_Level × S_Class% — hors pipeline, valeur initiale | ✅ |
| 1 | `equipment` | armes/armures/glyphes portés (conteneurs 0 et 5, identifiés) : core ÷diviseur + HP/MP → **pre** | ✅ (2026-10-30) |
| 2 | `gems` | gemmes `embedded_id1-6` (= item_id gemme, ≤ `embedded_amount`) : colonnes stats S_Item de la gemme, tous canaux | ✅ (2026-10-30) |
| 3 | `fortify` | +N (`strengthen`, S_Parameter 56 arme / 55 armure = +3 %/niv de la base item → P-ATK/M-ATK ou DEF) + enchants des scrolls (`witchcraft` → `UseEnchantId`) des pièces portées | ✅ (2026-10-30) |
| 3b | `magicbox` | combos `magicbox_enchant1-3` (ids S_ItemCombo) des pièces portées → **S_ItemCombo col 10 = id S_Enchant** → `GetUseCommands` | ✅ (table trouvée, application native non prouvée au désasm) |
| 3c | `goldenchant` | `gold_enchant` (id S_Enchant natif) des pièces portées → `GetUseCommands` | ✅ CODÉ (test `GoldEnchant_OnlyWornIdentifiedPieces`), pas de producteur |
| 4 | `costumes` | pages costume (conteneurs 2-4) : zéro stat propre, seuls les enchants des scrolls kind 79 (`witchcraft`) | ✅ (2026-10-30) ⚠️ inférence (voir §4) |
| 5 | `licenses` | Certificats S_License + combos 181-183 | ✅ |
| 6 | `classrank` | Expertise classes `S_ClassRankBuff`, somme sur toutes les classes (rank = ⌊min(clv,param578)×0.2⌋) | ✅ |
| 7 | `talents` | Arbre talents (knowledge branch × points) | ✅ |
| 8 | `limitless` | Perks Limitless Pass (optionnel) | ✅ |
| 9 | `titles` | Titre/honor actif (flag 0x1000, ex. enchant 59465 HP/MP+3%) | ✅ |
| 9b | `herotrait` | trait héroïque `create_flag` 1..8 → enchant 52000+n (PV/PM +12 %, FOR +10 %…) | ✅ (A4.5) |
| 10 | `buffs` | Enchants actifs (`player_enchants` + `SessionEnchants` procs) | ✅ |
| 11 | `ride` | Monture active (RideEnchant + bVar16) | ✅ |
| ✗ | `guild` | statues de guilde (bonus 3 h/jour) — voir §4b | ❌ données OK, mécanisme absent |
| ✗ | `honor_titles` | succès légendaires → titres → stats | △ via titres/appellations (pas de sous-système dédié) |

Les 4 stages d'items partagent UNE lecture DB des conteneurs 0..5
(clé `equipment.all` du `StatFetch`, seedée à l'entrée par
`ResolveEntryStatsAsync`, sinon chargée par le 1er stage puis
mémorisée via `StatFetch.Put`). Tests : `StatPipelineTests`
(Gems_*, Fortify_*, Witchcraft_*, ItemStagesOrdered_*).

Traduction enchant → contribution : `EffectToLife` (S_Enchant
commands, `EnchantCommands.cs` — extrait de `CSpellDBO::
DecodeEffectCommand`) ; enchant_ids 2001-2065 = attributs
(var4→pre, var6→pct — `Program.cs:121-126`). **Fractionnels
conservés en double** (un cast int historique effaçait les 0.5).

## 4. Équipement, gemmes, fortify, costumes

`EquipmentStatService.Sum` reste l'agrégat complet (core, ratings,
résists…) utilisé pour le **snapshot combat** et les vitals ; sa
partie core+HP/MP entre dans le calcul de fiche **uniquement via le
stage `equipment`** (`ProfileStatEmitter.SendAsync` n'injecte plus
`FromEquipment` quand `charId>0`). Changement de comportement
assumé : les glyphes (conteneur 5) sont maintenant lus (avant :
seul le conteneur 0 était chargé).

* `gems` / `fortify` : `StatContribution.FromBonusStats` — core et
  HP/MP en **pre**, P-ATK/M-ATK en `PhysAttackAdd`/`MagicAttackAdd`,
  DEF/ratings/vitesses en `AddSecondary`, résists en `AddResist`.
  Ces canaux passent au snapshot combat via `ApplyContribution`
  (maintenant aussi à la reconstruction du snapshot,
  `ResolveWeaponAsync`, et l'inlay/enchant d'item équipé invalide le
  snapshot). ✅ Les core stats (pre/pct) de TOUS les stages (gemmes,
  fortify, trait, talents, buffs…) sont versées dans le snapshot par
  `ApplyContribution` : `StrTotal`/`StrPct` (idem INT/AGI/WIS/LCK)
  = base init-only + `CoreBonusPre` (pre du pipeline − `GearCore`,
  l'équipement étant déjà dans la base) / `CoreBonusPct` (points de %
  ajoutés au % classe). Les ~30 sites de formule lisent inchangés.
  Le canal `post` des cores n'a pas d'équivalent dans les formules de
  niveau natives (non versé).
* `costumes` : le natif saute les conteneurs 2/3/4 dans
  `RefreshEQsAttr` ; **inférence non prouvée** que les enchants de
  scrolls kind 79 posés dessus (`witchcraft`) comptent — à
  confirmer au désasm/en jeu. Pages = `kind%3` (cont2→0, cont3→2,
  cont4→1), locs 1-5 (`ResolveEquipLocAsync`).
* Fortify : formule = base item × N × 3 % (params 55/56) —
  hypothèse de la doc plan, pas de preuve désasm sur la base exacte.

Armes (dégâts) : `CombatantSnapshot` lit P-ATK/M-ATK/Variance/Hit/
Crit/Kind de l'arme (chemin combat séparé de la fiche). Le +N
(fortify) et les gemmes arrivent en `PhysAttackAdd`/`MagicAttackAdd`
→ `BonusPAtk`/`BonusMAtk` = terme **flat** de `MeleeDamage`/
`MagicDamage` (test `FortifyPlusN_RaisesMeleeDamage`). Hypothèse
non prouvée : le natif pourrait compter le +N dans le terme arme
(× buff %) plutôt qu'en flat.

### 4b. A4.5 trait héroïque, combos boîte magique, gold, guilde

* **Trait héroïque — sémantique native PROUVÉE** (désasm World
  `CCharacterDBO::CreateCharacterIntoDB` @0x080a2170) : 3e octet du
  NCID 5 = `Feature` ∈ [0,7] (≥ 8 → « invaild.Feature », refus) ; le
  natif insère `player_spells.spellmaster_id = 0xc351+Feature`
  (spells 50001-50008, passifs `S_Spell` « J00n » → enchant
  52001-52008) et n'écrit **pas** `create_flag` (l'INSERT
  `player_characters` n'a pas la colonne). `GetHeroFeature` zone
  @0x840ace0 = id enchant − 52000. Reborn : `create_flag` = Feature+1
  (0 = persos antérieurs) ; `Eden.Core/Game/HeroTrait.cs` ; stage
  `herotrait` ; paquet custom 9058 (`[u8 trait]`) vers
  `TopLeftVitals`/`HotbarPanel`. Client : sélecteur 8 icônes dans
  `CharCreateScreen` (`--create-trait=<0-7>` QA). Pas encore : item
  « volume des actes héroïques » (`CResetHeroFeature`) pour changer.
  ⚠️ Enchants 52001-52008 (S_Enchant) : 2002/2013 (PV/PM %),
  2083/2095 (DEF/résists), 2085 (cores %) — mapping testé
  (`HeroTraitEnchants_ApplyExpectedChannels`) ; 52006 porte aussi
  `2052 -5` (non traduit en stat, signe à vérifier).
* **magicbox_enchant1-3** : le log natif les nomme `magicbox_combo`
  → ids `S_ItemCombo`. Table combo→effet = col 10 (id S_Enchant) pour
  les ~2200 combos AttrType 4-6 (ids ≥ 3904, tiers = stars 1/3/5).
  Branché (`magicbox`). Les affixes classiques (ids 1-3903,
  `combo_id`/`extra_combo_id1-11` des crafts) portent des valeurs par
  **colonne** 11-40 (11-15 = FOR/AGI/INT/SAG/CHA d'après les noms
  « de Puissance/Vitesse/Talent/Spiritualité/Chance », 20 Précision,
  22 Attaque, 24 Mystère…) = `EComboType` de
  `CLevelDBO::LoadCombo2StatMap` @0x08362dc0 : mapping colonne→stat
  non prouvé → **non branché** (à faire : désasm du consommateur du
  map CZoneServer+0x3fc).
* **gold_enchant** : sémantique PROUVÉE par le log natif
  `CItemDBO::ModifyItemTransGold` zone @0x08356020 (« Update Item
  GoldEnchant … enchant_id=%hd ») : u16 clone+0x6c = **id S_Enchant**
  posé sur la pièce. Consommé (CODÉ, 2026-10-30) par le stage
  `goldenchant` (pièces conteneurs 0/5 identifiées →
  `GetUseCommands`). Toujours aucun *producteur* (la transmutation
  « trans gold » n'est pas portée) ; valeur non nulle seulement si
  posée en DB.
* **Guilde/GvG** : les « statues de guilde » (guides-fr
  `guilde-et-ville-de-guile.md`) donnent un buff **3 h, 1×/jour**,
  durabilité 100 — mécaniquement un enchant temporaire = stage
  `buffs`, aucun nouveau stage. Bloquants : pas d'objet statue-buff
  en ville de guilde (les « statues » du code = portraits de
  joueurs, `FamilyHallRules`), ids d'enchants non identifiés par nom
  dans `t_enchant`. Non implémenté.

## 5. Inventaire des sources demandées

| Source demandée | Mécanisme actuel | Provenance | État |
|---|---|---|---|
| **Proc bonus** | `SessionEnchants` (OnAttack/OnCrit…) → stage `buffs` | buff temporaire | ✅ |
| **Certificats** | S_License + combos → stage `licenses` (1er) | permanent | ✅ |
| **Succès légendaires / honor titles** | S_Achievement → récompense appellation → stage `titles` | permanent (titre actif) | ✅ mécanisme / ❌ UI (A5.6) |
| **Armes / armures** | stage `equipment` (+ `Sum` pour le snapshot combat) | pre | ✅ |
| **Core stats** | S_Level×S_Class base + `classPct` multiplicateur | valeur initiale | ✅ |
| **Class talents** | knowledge branch → stage `talents` | permanent | ✅ |
| **Expertise classes** | S_ClassRankBuff somme → stage `classrank` | permanent | ✅ |
| **Glyphes** | items kind 78 conteneur 5 → inclus dans `Sum` | équipement | ✅ |
| **Consommables (stats/procs)** | `UseEnchantId` → `AddPlayerEnchantAsync` → stage `buffs` | buff temporaire | ✅ |
| **Gemmes** | stage `gems` (`SumGems`) | permanent | ✅ |
| **Enchants item (+N, witchcraft)** | stage `fortify` (`SumFortify` + scrolls) | permanent | ✅ ; MagicBox ✅ (`magicbox`, S_ItemCombo col 10) ; gold ✅ CODÉ (`goldenchant`) |
| **Pet pray skills** | enchants elf groupés + `ElfUseItem` existent, routage pray→owner partiellement tracé | buff | △ partiel |
| **Stats costume** | stage `costumes` (enchants witchcraft bags 2-4) | permanent | ✅ ⚠️ inférence |
| **Bonus guilde (PvP/GvG)** | statues de guilde = buff 3 h/jour (§4b), mécanisme absent (pas de ville de guilde/objet statue — doc 09) | buff temporaire | ❌ **trou documenté, NON FAIT** |
| **Trait héroïque** | `create_flag` → stage `herotrait` (enchant 52000+n) | permanent | ✅ |
| **Status temporaires (event/quête/instance)** | `Event_GiveBuff`/`RewardBuff`/`GiveBuff` mission → enchant réel → stage `buffs` | buff temporaire | ✅ |
| **Points de stats manuels** | `CharacterEntity.Str/Dex/…` = **vestigiaux** — le calcul live vient de S_Level×S_Class | — | ⚠️ ne pas les brancher |

## 6. Buffs / statuts (système complet — réutiliser)

`ZoneEntryFlow.Status.cs` `ActiveStatus` : effectId, durée
(`rest<0` = permanent), `hideIcon`, `nextId` (EffectNextID 1921),
`lockFlags` (2055), stacks, HoT/DoT 3001/3002, tick 1s.
- Ajout `AddPlayerEnchantAsync` = upsert natif → NCID 299
  (hidden/`flags&0x1000` = invisible mais compte).
- Retrait NCID 300 ; `EffectToLife(-2)` = suppression permanente.
- Persistance `player_enchants` ; refresh stats auto
  (`RefreshStatsAfterStatusAsync` → 296).
- Client : `EntityBuffs` → BuffBar/TargetFrame ; clic droit
  dispel NCID 85.

→ Un nouveau buff (dev movespeed, TP gratuit, event) ne touche
PAS le pipeline : poser l'enchant, le stage `buffs` et le refresh
couvrent le reste.

## 7. Vitesse de déplacement — les deux mécanismes

| Couche | Valeur | Fichier |
|---|---|---|
| Formule serveur | `9.425 + bVar16 + bVar18 + AgiTerm`, cap `MoveSpeedCapPct`(200), ×`TempMoveSpeedMul` | `NativeCombatFormulas.cs:64-84` |
| Config serveur | `MoveSpeedBasePct`=300 → mul 3× | `ServerConfig`/`server.config.txt` (hot-reload 2s) |
| Client | `TempMoveSpeedMul=3f`, Move 6 / Run 10 | `PlayerController.cs` |

→ Le ×300 % actuel = multiplicateur, **pas** un buff. Plan B2 :
le remplacer par un enchant movespeed (taux en `server.config.txt`).
Bonus AGI→vitesse : `balance.ini` `AgiToMoveSpeed`. ⚠️ Deux caps
coexistent (300 config vs 200 balance) — vérifier l'ordre réel
au runtime.

## 8. Portal stone

NCID 160 `flag==1` : consomme 1 pierre 0x9C54/0xA476
(`ZoneEntryFlow.PortalStone.cs` — live `player_items`, refus
silencieux sans). `flag==0` = téléport **gratuit sans consommation**
— le chemin existe, il manque juste la condition « état actif »
pour le router côté serveur.

## 9. Client — réception

| NCID | Contenu | Traitement |
|---|---|---|
| 296 | ~35 champs : 5 cores, 9 résists, MaxMP, PATK/MATK, DEF, 8 rates, 6 vitesses | `GameScene.cs:2627` → `PlayerVitals.ApplyAttrRefresh` |
| 492/493 | base attrs / level attrs | `:2761` |
| 280 | UpdateAttr ponctuel | `:2638` |
| 600/605/606 | HP/MP/Exp | `:2475` |
| 299/300/301/372/315 | enchants | `ZoneEnchantMessages` → `EntityBuffs` |

Toute stat destinée au joueur passe par `ProfileStatEmitter.
SendForCharacterAsync` — déjà appelé partout (équip NCID 8,
licenses, talents, titres, succès, collection, ride, statuts,
inlay, level-up, GM). Le « Statistiques avancées » (A5.5) n'a
besoin d'un paquet d'extension QUE pour les données non émises
aujourd'hui (formules paramétrées, multi-hit, provenance).

## 10. Checklist avant d'ajouter une source

1. Est-ce permanent ou temporaire ? → stage nommé / enchant.
2. C'est du `pre`, `pct` ou `post` ? (plat-avant-%, %, plat-après-%)
3. Où dans l'ordre ? (après `classrank` pour les gains permanents
   cumulatifs ; `buffs` dernier pour les temporaires — sauf raison
   contraire documentée ici).
4. Refresh : `SendForCharacterAsync` appelé après le changement ?
5. Client : la stat arrive-t-elle dans le 296 ? Sinon champ/paquet
   à étendre — documenter ici.
