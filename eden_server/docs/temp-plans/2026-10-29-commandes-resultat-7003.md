# 2.3.3 — Commandes résultat manquantes (batch 1)

Batch du 2026-10-29 : `7003 Effect2Target`, `3101-3103` checks,
`1822 EnchantCancel` voie joueur.

## Preuves natives

- `CEC_Effect2Target::Init` @0x08526300 : arg0=mode (1-5 bornés),
  arg1=faction (0 alliés +0x7c / 1 ennemis +0x80), arg2=rayon s16
  × `DAT_087cc688` = **0,5**, arg3=id effet secondaire (doit exister),
  arg4=durée (rest, -9=icône cachée), arg5=source <2.
- `CEC_Effect2Target::Execute` @0x08526780 : sélection (1=lanceur,
  2=cible, 3=groupe, 4=cercle autour de la cible, 5=cercle autour du
  lanceur) puis `CEffectFactory::EffectToLife` sur chaque victime.
- `CEC_EnchantCheckByType::Execute` @0x0852c790 = `return 1` — check
  consulté par d'autres chemins, AUCUNE action à l'application (3102
  idem, 3103 même famille).
- `CEC_EnchantCancel` @0x852c470 : retrait par type c38 ∈ [a0,a1],
  grade c39 ≤ a2, non indissipable, 2 triplets — déjà porté voie mob.

## Implémenté

- `ISpellEffectHost` +4 méthodes : `ApplyEnchantToMobAsync`,
  `ApplyEnchantToCasterAsync`, `ApplyEnchantToPlayersNearAsync`,
  `MobsNear`. Helpers flux : `MobsNear`, `ApplyEnchantToPlayersNearAsync`
  (joueurs migrés+vivants du node+instance dans le rayon).
- `Effect2TargetHandler` : modes 1/2/3/4/5 ; alliés=joueurs du node,
  ennemis=mobs du node (lanceur joueur).
- `EnchantmentCheckHandler` : 3101/3102/3103 = no-op enregistré.
- `ExecuteInstantEnchantAsync` : branche `1822` (types+grade sur
  `st.Statuses`) — miroir du handler mob.
- Tests : `Effect2Target_Mode2_AppliesToMob`, `_Modes1And4`,
  `EnchantChecks_AreHandledNoOps` (25/25 SpellEffectTests).

## Vérif live

- Cast `58041` (Aura Ami du Marteau, `7003|4|1|15|50470|50`) sur mob :
  `D NCID=9` → `279` → `281` OK.
- Zone : `add mob=ffff80ae enchant=50470 rest=50` **et**
  `add mob=ffff80af enchant=50470 rest=50` — le cercle 7,5 u a touché
  la cible ET un voisin (sélection multi-cibles réelle).
- Fil : 3× `NCID=299` (`26c5`=50470 rest=50 sur les DEUX mobs +
  `27c5`=50471 rest=150 wrapper).
- Aucun `spell-result cmd` pour 7003/3101-3103 → handlers reconnus.

## Batch 2 (même journée)

- `AttrHateHotHandler` 2119/2121 — `CEC_AttrHateHot::Execute`
  @0x08507aa0 : `hate[lanceur] += arg0` sur le mob (provoc +100 /
  apaisement -450 dans la donnée S_Enchant).
- `2137 LevelCheckPlus` ajouté aux checks no-op (Execute retourne un
  booléen @0x08541250 — condition, pas action).
- Commit `ece1423`.

## Limites connues

- shortcut: mode 3 (groupe) applique à tous les joueurs du node dans
  le rayon, sans filtre équipe, <quand le système d'équipe existe>.
- shortcut: arg5=1 (cible-source) garde le lanceur comme casterId —
  kill credit DoT au joueur, <si une donnée exige la source mob>.
- `1822` voie joueur : branche testée par la donnée, non rejouée live
  (nécessite un sort mob→joueur contenant 1822).
- `2052/2037` pets, `1076`, `1913`, `1997/1999`, `2945`,
  `2066`, `2132`, `2152`, `1807`, `1074` : restent logués
  `spell-result cmd` — batchs suivants.
- `#2055` et autres commandes réalisées via le registre persistant du
  mob (`FillMobEnchantFields`) restent loguées — bruit acceptable,
  utile pour l'audit.
- Observé live (après batch 1+2) : `LifeFloat` (1997), `HealPlus`,
  `SummonPet` (2037), `DamageAddPlus` — prochaines cibles.

## Batch 3 (même journée)

- `HealPlusHandler` **2047** — `CEC_HealPlus::Execute` @0x085353xx :
  le soin vise le LANCEUR (entity+0x8c). Types 4/5 = HP/MP flat arg1
  (clamp [1,max]/[0,max] + NCID vitals) ; types 0-3 = no-op (taux
  spellref non porté — toute la donnée réelle est type 1).
- `PlayEffectHandler` **1913** — `CEC_PlayEffect::Execute` @0x0854b200 :
  `CNC_CZ_ClientPlayEffect` (383) au lanceur + can-see (5 args →
  champs paquet, control id = handle optionnel).
- `SpellAttackHandler` **1999** — `CEC_SpellAttack::Execute`
  @0x08553900 : la cible mob recaste arg0 vers l'attaquant via le
  pipeline de cast mob existant (wind-up + génération).
- `DistanceSummonHandler` **1807** — `CEC_DistanceSummon::Execute`
  @0x08522bd0 : mob téléporté au contact du lanceur (lerp — marge
  1.5 u en lieu des bounding radii) + PlayEffect broadcast.
- `MobRegistryAttrsHandler` no-op : **2055/3001/3002** (attributs du
  registre `FillMobEnchantFields`), **1997** (arg1 flag toujours vide
  → Execute saute), **2033/2034** (tags de formule) — fin du bruit
  `spell-result cmd` pour ces ids.
- Host : +`HealCasterAsync`/`PlayEffectAsync`/`MobCounterCastAsync` ;
  helpers flow `HealCasterFromEffectAsync`, `PlayCasterEffectAsync`,
  `MobCounterCastAsync`.

### Preuve live batch 3

- Cast **51546** sur clone 56038 : enchant résultat 58195
  `1999|51547` + `1076|615|2` → le mob **recaste 51547 vers le
  joueur** (`NCID=495` geste + `NCID=289` blast `5bc9` sur le fil,
  12:55:21) — le seul `spell-result cmd` restant de toute la session
  est `1076 ItemTriggerEvent` (différé Mission RPC). 1999, 2055,
  3001/3002, 1997, 2047… = silence total.

### Restes différés (documentés AUDIT-EFFETS-SORTS)

- **2037/2052/2072 SummonPet** — `CNode::BornPet/BornTotemPet` :
  système de pet invocable (IA+lien maître+vue) — feature à part.
- **2945 SpellToSpell** — `CNC_CZ_ClientChangeSpell` : morph de sorts
  appris par paires (33 enchants).
- **2152 OnBuffEndBuff** — hook « fin de buff » dans le registre mob.
- **1076 ItemTriggerEvent** — `CNC_ZMis_MissionServerListDialogue`
  vers le serveur Mission : plomberie Zone→Mission à faire.
- **2066, 2132, 2118, 1121, 1074, 1805**… — plus petits, batchs
  suivants.

## 2.3.4 — Stacking avancé (même journée)

- `EnchantService.TypeAddAllowed` — port du FindByType de
  `CEnchantMgr::Add` @0x08569a50 : parmi les enchants actifs de même
  type (col38), un grade supérieur (col39) — ou égal avec niveau
  supérieur — fait REFUSER le nouveau ; sinon les plus faibles sont
  remplacés. Type 0 = donnée absente → jamais de conflit.
- Câblé sur les DEUX voies : joueur (`AddPlayerEnchantAsync` → rejet
  + index 0x5b9, remplacement via `RemovePlayerEnchantAsync` → 300)
  et mob (`AddMobEnchantAsync` → rejet sec, remplacement → 300).
- shortcut: partition flag-bits `param_4 & 0x7c000000` du natif
  ignorée (group 0 supposé uniforme), <si une donnée exige des
  groupes distincts>.
- `RemoveReplaceableSpell` **différé** — le registre natif
  `replaceable[0..4]` (Reg2Caster bits 0xf80000 de CEnchant+0x28) et
  sa consommation par les flags de sort (0x3e00000) exigent de porter
  les flag-bits apply dans le modèle — pas la donnée colonne claire
  aujourd'hui.

### Preuve live 2.3.4

- `//self_use_effect 59319 300` (type1 g1) → `299 add`.
- `//self_use_effect 40631 300` (type1 g2) → **`300` retire 59319**
  puis `299 add` 40631 — remplacement g2>g1.
- `//self_use_effect 59319 300` → **`NCID=316` index `0x5b9`**, zéro
  299 — rejet g1<g2. Zone log : `remove 59319 → add 40631`, jamais de
  3ᵉ add.
- Tests : `TypeAddAllowed_GradeAndLevelRule` (31/31 SpellEffectTests,
  suite 1239/1239).
