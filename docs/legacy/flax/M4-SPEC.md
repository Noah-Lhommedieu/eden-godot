# M4 Spec — Props, herbe, joueur (audit 2026-11-01)

## Bloquants tâche 0 (à résoudre avant offsets)

1. **Queue V7 EDENFR** : ~15 fsm (s002,s003,s004,s005,s010,s011,s021,s030,s032,s051,s054,s119,s225,s307,s501) ont GRL/GRS/OBJ sérialisés SANS tags après LAY0 (`FsmFile.TrailingBytes`). Crack : comparer avec le fsm US framé de `portage/corpus/official/map/model/<map>/` (oracle d'analyse seulement — jamais au runtime). Hypothèse principale : `count u32 + count×44 (GRL)`, puis `count + count×32 (GRS)`, puis `count + count×92 (OBJ)`. Validation : names ASCII[40] = fichiers `map/model/object/*.nif` réels, typeIds < GRL count, positions dans bornes monde. Vérifier aussi les blocs non-tuile DANS LAY0 (~222 Ko sautés +4o) — une partie peut y vivre. Ajouter accesseur `TrailingSpan` à FsmFile.
2. **Variante NIF FR** : 1061 nifs FR ont géométrie « hors spec nif2gltf » (char 348, map 240, monster 157, effect 132, item 95, ride 67) — en portage ils étaient substitués par des nifs US, INTERDIT ici (EDENFR pur). Le lecteur Eden.Core doit gérer la variante ou stubber. Hexdump un nif FR (ex `map/model/object/UB124.nif`) : version, types blocs, diff vs spec.

## Records/chunks

- Chunks `{u32 tag, u32 size}` ; LAY0→GRL→GRS→OBJ ; chunk compté = `count u32` + records.
- GRL 44o : `u32 typeId + char modelName[40]` (ex `UE704M.nif`).
- GRS 32o : `typeId, x,y,z, scale, flags, ParamA, ParamB` (A/B/flags : parser, ignorer — sémantique non prouvée).
- OBJ 92o : `pos(3f)@0x00, matrice 3×3@0x0c (colonnes=axes), scale@0x30, name[40]@0x34`. Scale 0→1. Placement = coords natives sous WorldRoot −90°/X — pas de conversion.

## NIF/KF/KFM — sous-ensemble M4

- Gamebryo/NetImmerse 20.x, spec niftools nif.xml (563 niobjects). Blocs nécessaires : `NiNode`, `NiTriShape`/`NiTriShapeData` (pos/normals/UV/tris), `NiTexturingProperty`+`NiSourceTexture` (dds), `NiMaterialProperty`, `NiAlphaProperty`. Blocs inconnus skippés par taille déclarée.
- M4 = statique + bind pose. Skinning (`NiSkinInstance`), KF (`NiKeyframeController`), KFM manifest → **M9**.
- Convertisseur référence `tools/nif2gltf` (copié dans eden-flax/tools/) = oracle offline.

## Props (OBJ)

- Asset : `map/model/object/<name>.nif` (résoudre insensible — casse UE*/ue* mixte). Variantes `UB124_1..5` : sélection inconnue → `<name>.nif` puis `_1` (HYPOTHESIS).
- Batching : groupe (modèle × cellule 64u). ~2300 OBJ sur S009 → privilégier **mesh procédural fusionné par groupe** (façon TerrainMeshBuilder) plutôt que ~2000 StaticModel, à trancher au profilage. Alpha-blend hors groupement.

## Herbe (GRL/GRS)

- `GrassLayout` existe (Eden.Core) : groupe typeId×cellule 64, cap 5000.
- Référence : quads croisés alpha-scissor, yaw pseudo-aléatoire déterministe, scale GRS, ombres off, pas de vent (M9). Repli texture `effect/texture/grassx01.dds`/`grass_01.dds`.
- M4 : quad croisé procédural instancié par (typeId, cellule) ; texture du GRL nif si extractible.

## Joueur / NPC

- Corps : `CharModelSpec` appearance32[28]>>4 → {1→b,2→m,4→f,6→k,8→l,9→c,défaut→g} → `char/model/<L>.nif` + `char/<L>.kfm` + `char/animation/<L>.kf` (présents fr-full). Mega-modèle : visible = `#BFACE`+`SB*`+cheveux `#L{100+hairStyle}`+tenue `#B202`/`SB202` (HYPOTHESIS). M4 minimal : `g.nif` bind pose filtrage naïf `#BFACE`+`SB*`, repli capsule.
- Orientation : natif −Y avant, racine Eden −90°/X, yaw `EdenDir.ToYaw(dir)`.
- NPC `[npc]` kind 8 : `NpcModelTable.Resolve` = `data/db/c_npc.ini` → `data/db/npclist.ini` → stem `mNNN` → `monster/model/<stem>.nif` (stretch, même chemin que props).
- `[reborn_monsters]` = entités live (NCID 298) → M5+.

## Ordre chargement

scene.ini → fsm(+queue V7) → smp → env.ini → textures(+props/herbe) → tuiles → **OBJ props → GRS herbe → joueur bind pose → NPC (stretch)** → env → caméra. Parsing en tasks, SpawnActor main-thread.

## Tests M4

1. Queue V7 s002 : counts == fsm US oracle ; 1er OBJ name ASCII valide ; typeIds < GRL.
2. `NifFile.Parse` UE013.nif + b.nif → ≥1 NiTriShape cohérent, texture extraite.
3. Variante FR : zéro exception, mesh vide explicite.
4. ResolveInsensitive casse mixte object/.
5. Transform OBJ parapets UB107 (test existant à réactiver sur queue).

## Pièges

Big5 (scene/db c_*) vs Latin-1 (t_*) via EdenEncoding ; casse mixte ; `.ifl` eau animée hors M4 ; handedness LH (WorldConv RH → flip à la frontière) ; `FieldZ` trames = direction pas hauteur.
