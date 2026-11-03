# ÉTAT COURANT — Eden Client Reborn (passe 26 oct. 2026)

Cette note remplace l'ancien état « final » : les plaintes visuelles P0
ont été reproduites puis corrigées avec captures réelles. Elle ne clôt
pas le master plan : beaucoup de systèmes restent à éprouver live sur
plusieurs maps, résolutions et comptes.

## Vérifications faites

| Vérification | Résultat |
|---|---|
| `dotnet build godot_project/EdenClientReborn.csproj` | **0 erreur / 0 warning** |
| `dotnet test tools/NetTests` | **276/276 verts** |
| Pré-jeu `--screen=login|channel|chars|create --shot` | hall `S218` monté, `temple=True`, captures `final2-*.png` |
| Monde `vis_test --map=S002` | terrain/splats/eau/ciel/alpha propres, capture `world-final3-s002.png` |
| HUD `--drag-test=inventory` | titre `(256,220)→(376,310)`, fond `(376,310)→(496,400)`, persistance confirmée |
| Entités `vis_test --mob=M025 --mob=M014 --chars --pick-test` | modèles GLB, au sol, capsule `PickArea` **3/3 HIT** |
| Table NPC | `3127 npcId → 702 codes npclist`, `S002` : `53/53` GLB |
| Flux live écrans `admin/adminmdp` | login → channel → sélection → loading → **S002** ; map, HUD, inventaire, sorts et NPC wire chargés |
| `--input-test` live | clic sol + mouvement, clic droit sur NPC `0x88c99262`, `TalkToNpc` puis `EnterShop` réel (`5 articles`) |
| `--mob-test --combat-test` live | `MoveSelf` vers `[reborn_monsters]`, 8 mobs `kind=12`, `AutoAttack` sur `Red Cap Mushroid`, `279→281` sur sort `50011`, mort serveur |
| `--cursor-test` / `--cursor-test=mob` live | pointeur réel : NPC `kind=8` → `Talk`, mob `kind=12` → `Attack`, bouton HUD → `Arrow` |
| `--hud-open` + `--hud-dump` solo | 15 fenêtres/panneaux montés (`inventory`, `profile`, `skills`, `quests`, `map`, `dialogue`, `target`, `shop`, `bank`, `social`, `skillbar`, `craft`, `class`, `death`, `transport`) ; positions par défaut relevées dans un profil isolé |

## Correctifs principaux validés

- Décor pré-jeu : la piste `S406` était fausse ; preuve reverse
  `FUN_00699a60(...,99)` + `c_node.ini` → scène `S218`. Extraction
  `S218_room10b`, coques `_NDLWL` masquées, escaliers/tapis restaurés,
  environnement pré-jeu protégé de `SettingsApplier`, captures en
  attente de `TempleLoaded` au lieu d'un délai fixe.
- Terrain S002 : winding corrigé, splatmap lue comme données (pas
  `source_color`), repli de texture de slot, overlays `__night_` /
  `*_BB_*` masqués, double fog neutralisé, émission bleue parasite des
  matériaux alpha (`UD501`) coupée.
- HUD : chrome DejaVu/CEGUI sur fenêtres, drag réel par barre de titre
  et par fond, clamp viewport + persistance `ui_layout.cfg`, sonde
  `--drag-test`, slots FastBar1 branchés sur `PlayerSkills`/
  `SpellCatalog`, hauteur d'icône `SkillBarWindow` corrigée.
- Entités : modèles NPC par `c_npc.ini` + `npclist.ini` (stem/skin/
  scale), orientation `(-90°,180°,0°)`, grounding `Y ≥ terrain`,
  capsules `Area3D` de picking couche 16, locomotion locale basculée
  sur l'input direct (`InputMoving`). Le clic droit live sur un
  marchand a produit `TalkToNpc → EnterShop`.
- Préchargement live : le cache `ToonMaterializer` partagé par les
  tâches GLB était non thread-safe et cassait `LoadMap(S002)` ; son
  verrouillage a permis la chaîne live complète.
- Combat live : `--mob-test` amène le joueur au spawn hostile réel
  le plus proche, puis `AutoAttack`/`SkillCast` frappent un vrai mob
  serveur (`272` dégâts + mort), au lieu de seulement tester la ville.
- Curseurs : mapping cells `SystemCursors` revu sur preuve
  désassemblée ; priorité UI prouvée en live par `--cursor-test`
  (`NPC→Talk`, `mob→Attack`, `bouton HUD→Arrow`). Les états
  système `Busy`, `Forbid`, `DragOk`, `DragBad` restent à prouver.

## Captures de référence

- `godot-shots/final2-login.png`, `final2-channel.png`,
  `final2-chars.png`, `final2-create.png`
- `godot-shots/world-final3-s002.png`
- `godot-shots/hud-inventory-final2.png`
- `godot-shots/entities-final-s002.png`
- `godot-shots/live-mob-combat.png`

## Défauts / preuves encore ouverts

- Parcours live happy-path validé jusqu'en ville S002 ; restent à
  éprouver : échec/retry/retour arrière, création, création+premier
  personnage, drag/drop, bank/craft/transport/relog.
- NPC : la table modèle est branchée, mais skin/facing/animation/
  service/noms EN doivent être contrôlés par échantillon sur plusieurs
  maps.
- UI : drag prouvé sur l'inventaire ; chaque autre fenêtre, état
  bouton, tooltip, z-order et scaling restent à auditer.
- Curseur : les états système `Busy`, `Forbid`, `DragOk`, `DragBad`
  restent hypothétiques tant qu'ils ne sont pas vus en contexte live.
- Animations : input→move corrigé dans le code, mais la latence perçue,
  backpedal/strafe/turn/attack/death/mount doivent être filmés.
- Les erreurs `RID allocations ... leaked at exit` après les captures
  Godot surviennent à l'arrêt forcé et ne sont pas assimilées à un
  échec fonctionnel, mais restent à nettoyer dans un cycle d'arrêt
  normal.

## Plan immédiat

1. Rejouer un parcours live complet avec captures login→monde et une
   courte vidéo déplacement/facing/attaque/clic NPC/drag HUD.
2. Faire une matrice UI par fenêtre à 1280×720 et 1600×900.
3. Vérifier NPC/mobs sur une map avec hostiles réels ; cibler corps,
   pieds et cas de chevauchement.
4. Produire l'export autonome et noter précisément ce qui reste mock
   ou non prouvé dans `MASTER-PLAN.md`.
