# Mobs : sorts normaux / à seuil, fiche Inspecter, taille et skins

Statut : verified (code + tests) / hypothesis (états de skin) — Godot 4.8-beta2,
2026-11-04. Définitions : `definitions.md`.

## Sorts des mobs (preuves natives zone)

`CAI::DetermineAttackMode` @0x084d8a60, `GetAllMonsterTemplate` @0x08367910,
`CNode::BornMonster` @0x084b02b0, `CAiQuery::Deserialize` @0x085b5c30.

| Donnée | Colonnes | Rôle |
|---|---|---|
| Normal skills | `c_monster` 44-46 | vecteur template +0x108 (3 sorts), chance % = `c_ai` col 6/8/10 |
| Countdown skills | `c_ai` col 15/17/19 | vecteur +0x114 (3 sorts) |
| Seuils PV % | `c_ai` col 5/7/9 | palier 1 : PV ≤ col5 et > col7 ; palier 2 : ≤ col7 et > col9 ; palier 3 : ≤ col9 |
| Relance | `c_ai` col 14/16/18 | secondes entre deux lancers du sort du palier |
| Cris de palier | `c_ai` col 11/12 | texte (CombatShout / StageShout) — non porté |

Un palier k autorise les sorts normaux d'indice ≤ k−1 (essayés du plus haut au
plus bas). Au-dessus de col5 : rien. Serveur : `Eden.Core/Game/MobSkillRules.cs`
(tests `MobSkillRulesTests`), branché dans `ZoneEntryFlow.Combat.Mob.cs`
(`PickMobSpell`). Un mob sans sorts col 44-46 ni sort à seuil garde l'ancien tirage
35 % sur S_Monster 40-49 (repli, non natif).

## Drops

`S_DropItem` pool 0 = Normal, pool 1 = Break Point (tiré en plus **seulement si
`LiveMob.BpBroken`** — la rupture a réellement eu lieu), `S_ClassDropItem` (cols
40-49 de S_Monster) = Butin de classe, non conditionné par la rupture.

## BP cassée

Serveur déjà complet : `StaggerBrokenMobAsync` diffuse 495 `PlayMonsterAction`
motion 0x3f7 = 1015 = `hurt05` (m0xx) + verrou d'IA 3-4 s ; client
`EntityRegistry.HandleMonsterAction` joue la motion. Le teinte violacée
(`SetBroken`) reste une approximation.

## Taille / skin des mobs (world boss)

`data/db/monsterlist.ini` col 2 = échelle, col 8 = texture, clé = code modèle
`c_monster` col 1 (client natif `FUN_00cff380`). La variante « 013 » des world
boss est 2-4× plus grande que la « 010 » (m083 0,40→0,90, m105 0,95→3,80, m405
1,40→4,00). Client : `world/MonsterModelTable.cs` + `EntityRegistry.HandleSeeMob`.
Même table = bug champignoïdes (3 textures m02501-03 : tous gardaient celle du GLB).

**Chaîne complète revérifiée 2026-11-04** : `HandleSeeMob` résout
`MobDanger.Lookup(entityId&0xFFFF).ModelCode` → `MonsterModelTable.Resolve`
(`EntityRegistry.cs:1010-1014` : `npcScale`=col 2, `skinTex`=col 8 ; repli
`BreakPointUi.MobScale` si code absent :1019) → file `_pendingVisuals`
(:1057) → `DrainPendingVisuals` (:923-928) applique `SetVisual(skinTex)` +
`Visual.Scale = scale` + `RelayoutOverhead` (mesure le haut RÉEL mis à
l'échelle). `ApplySkinTexture` (:455-510) ne retouche que les surfaces dont
`src_tex` partage la base (`m025*` pour `m02503.dds`), repli « toutes » si
aucune ne matche, couvre ShaderMaterial et StandardMaterial3D, et pose la
variante sur les modèles sans albedo. `SetVisual` a aussi un param `scale`
appliqué AVANT la mesure de hauteur (:390) pour le chemin synchrone.
**Overhead** : capture `artifacts/vis/mob-overhead.png` (VisTest `--mob=m025`,
même chemin `RemoteEntity.SetLabel`/`UpdateHpBar`/`OverheadLayout.Apply`
que le spawn live) — barre PV verte + label « Lv25 M025 » rendus au-dessus
du modèle. Le skin « champignon presque mort » à pleins PV était l'ombre
blob projetée (Decal.CullMask, fix D4 — vérifié par capture), pas la
texture variante.

## Limites

- **Skins d'état** (BP cassée / seuil de PV / gros dégât d'un coup) : le
  mécanisme natif est les **émetteurs de particules pilotés par le KF**, pas
  des meshes alternatifs. `M609.nif`/`M607.nif` embarquent des
  `NiPSysMeshEmitter`/`SuperSpray*` texturés par la série `*5x.dds`
  (`M60951/52`, `M60751-55`) — le suffixe 5 = texture d'état (dégâts/spores),
  distincte de la variante corps `*0x` choisie par `monsterlist` col 8.
  Le `M609.kf` contient des pistes `NiBoolInterpolator`/`NiBoolData`
  `EmitterActive` + `NiPSysEmitterCtlr` (parsés par `NifFile.cs:796`) qui
  allument/éteignent les sprays par séquence. Côté client Godot :
  `KfAnimation` ne bake que les os (rot/pos/scl) → pistes émetteurs perdues ;
  `NiControllerRuntime` log `ctl-unhandled:NiPSysEmitterCtlr` ;
  `NiParticleRuntime` n'est branché que pour les FX hosts, pas les mobs.
  Statut : mécanisme identifié (2026-11-04), émetteurs mobs non implémentés.
  `MobTransform` 380 reste un effet de sort.
- Stats d'un mob : un seul attribut d'attaque (col 14) ; pas de M-ATK distinct,
  de parade ni de déf. magique natifs (affichés `—`/0).

## Suite 2026-11-04 — skins d'état mobs : preuve négative complétée

- **Variants `monsterlist` ≠ skins d'état** : `monsterlist.ini` n'a
  qu'UNE colonne texture (col 8, ex `m02501.dds`). Les suffixes
  `m025010/011/012/013…` sont des LIGNES DB distinctes (même
  `m025.kfm`, même texture, seule la colonne échelle change :
  0,65/0,80/0,95/1,90/0,30/2,30) choisies au SPAWN par le code modèle
  du monstre (`c_monster` col 2 : 20710→`M025010`, 57504→`M025011`,
  58001→`M025013`). Aucune sélection runtime par état. (verified)
- **Aucun swap texture/mesh par dégât** : `reindex strings` sur
  client_game — aucune chaîne `hurt*`, aucun `AddProperty`/`SetTexture`
  dans les corps de fonctions (FTS5), `MonsterList.ini` n'est lu qu'une
  fois (`FUN_00cff380`). Les seuls états visuels natifs : (a) les
  sprays KF `*5x.dds` documentés ci-dessus, (b) KNOCK/KNOCKNODE (BP).
- **BP/broken = KNOCK nodes (vérifié)** : `FUN_0072ffe0 @0x0072ffe0`
  cherche les objets nommés `KNOCK`/`KNOCKNODE` (vtable +0x5c
  GetObjectByName) et pose/retire le bit 0 du champ flags +0xac
  (AppCulled Gamebryo) — appelé par `FUN_00761610`, `FUN_00764f00`,
  `FUN_00765760`, `FUN_00771660`, `FUN_00771920` (état patience < 1,
  attr 0x13). Implémenté : `RemoteEntity.SetBroken`/`ApplyBrokenKnock`
  (`EntityRegistry.cs:103-140`) masque/affiche ces nœuds — plus de
  teinte violacée inventée. Déclencheur `HandleUpdateAttr` attr 0x13 :
  `WeakMask != 0 && Patience < 1` (`EntityRegistry.cs:1347-1353`).
- **Gros dégât d'un coup** : aucun mécanisme de skin trouvé ; le natif
  joue `hurt01`/l'effet de sort col 10 — rien de plus. Statut :
  **aucun skin d'état alternatif n'existe côté natif** — preuve
  exhaustive ci-dessus (2026-11-04, `reindex` client_game+zone).
