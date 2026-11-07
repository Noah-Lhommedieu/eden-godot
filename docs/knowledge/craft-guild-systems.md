# Systèmes craft + guilde — index du repérage complet

**Statut : verified (audit statique, pas de validation in-game) · 2026-11-04 · Godot 4.8-beta2**

## Validations live (client réel) — 2026-11-06

**Chaîne guilde → VDG complète, `verified`** — parcours joueur réel :

- **Création de guilde** : NPC 50340 (S001) → service 4187 → node 347 →
  `GuildWindow` (nom saisi via `GuildNameEdit`, bouton `GuildCreateBtn`)
  → NCID 49 → 346 → DB `family 5002 'devinguild'`, débit 50 000 or.
  Fix requis : `GuildState.ApplyZoneChange` n'assignait jamais
  `FamilyId` (vue restée « sans guilde ») ; `@key` probe n'injectait
  pas l'Unicode (`@type` ajouté).
- **Don trésorerie** : NCID 159 ; fix ajouté — la zone relayait pas le
  don au cache famille du monde (`family_donate` dans
  `ZoneLinkServer.cs`), le gate 0x6ae refusait à tort.
- **Achat VDG** : NPC 50340 → service 4188 → node 501 → dialogue
  `commondlg` (sélecteur type + case VIP) → NCID 173 → gates natifs
  zone+monde (lvl≥30, chef, lv≥3, tréso≥500k, **≥30 membres**,
  cap 49 villes) → `family.house_open=501`, tréso débitée 500 000→0,
  push 346/50 au client. Fix rendu : panneaux `*_static` en fond
  (`Root_CommonDlg_static`) avalaient les clics → `MouseFilter=Ignore`.
- **Entrée VDG** : NCID 176 (`@fament`) → `TownEnter → node 501` →
  `MigrationDone node=501`, DB `node_id=501`. Flameshot : forêt,
  canal famille 5002 rejoint, fenêtre `<devinguild>`/Urbanisme.
- Fixture de test documenté : 29 persos `membre1-29`
  (`player_characters.family_id=5002`, account_id=1) pour honorer le
  gate natif ≥30 membres — **pas** un bypass joueur.
- ⚠️ `family_cadre_members` seul ne compte PAS pour le gate membres :
  `fam.Members` (monde) = `player_characters.family_id<>0` ; les cadres
  ne fixent que le rang des membres existants.
- ⚠️ Le client n'émet pas 132 (FamilyInstanceGetData) à l'entrée de
  ville — données bâtiments (487) non demandées automatiquement.
- ⚠️ `EnterTerritoryBtn` (familyinfownd_na, sous `FamilyBor`) n'est pas
  trouvé par le probe quand la fenêtre bascule — l'entrée a été prouvée
  au niveau wire (176) ; chemin bouton UI à re-valider.

**Placement de bâtiment par drag-drop réel, `verified` (2026-11-06)** :
- NPC housekeeper `53007` (spawné en S501 à ~(106,124), service
  `Service 4521 11` = op 0x3b) → dialogue → 483 → fenêtre native
  `familybuilding_forest.layout`.
- Drag d'un blueprint kind 0x29 (35071) du sac vers le slot `Building1`
  → NCID 130 `[mode=0][container=0][loc=10][bag=0][slot=3]` → zone
  accepte → `family_building_container` : ligne 0x29 loc=10 (tid=1,
  résolu `ResolveBuildingPut` niveau guilde 3) **+ NPC 0x2a auto-posé
  loc=11** (item 35001 → npc 53007) → 484+487 renvoyés, UI affiche
  « L3 建設藍圖 » — capture Flameshot.
- **Bug client majeur corrigé** : `ItemSlotCell.AllIn` filtrait
  `FindChildren` par nom de classe C# — les types non-`[GlobalClass]`
  ne matchent jamais → `Cell()` null → **tous les slots des fenêtres
  `familybuilding`/`magiccube`/`magicbox` étaient non câblés** (aucun
  drop possible). Fix : `OfType<ItemSlotCell>()` côté managed. Même
  motif corrigé pour `CeguiTitleBar` (titres map/transport) et
  `EntityAnimator` (statues figées).
- **Bug drop corrigé** : le payload `ItemCell` de l'inventaire ne porte
  pas de clé `kind` — les filtres `kind=="bag"` rejetaient le drop
  (familybuilding, magiccube ×2, magicbox) → `StorageItemCell.IsBagPayload`
  partout.

**Fortification +14 avec combos, `verified` (2026-11-06)** :
- Ouverture native par action 13 → NCID 432 op9 → `strengthenwnd_na.layout`.
- Drops UI réels : cible 28597, pierre 40009, chance 39260 ; chaque clic
  `StrengthenBtn` émet NCID 119 `[combine=8][0/14][0/7][0/15][quick=0]`.
- Run complet +0→+14 : 433 cast, 436 niveau, 435 succès à chaque palier ;
  369 à +6/+8/+10/+12/+14 accepté sans erreur client.
- DB : `strengthen=14`, `extra_combo_id7..11 = 195,145,2795,245,95`,
  consommation matériau/chance confirmée.
- **Bug codec client corrigé** : `ZoneClientModifyItemInfo` lisait
  `color` en `i32` puis 3 champs surnuméraires ; le Serialize natif
  écrit `longlong` + queue `[u8][u8]`, et mode 8 = bind court.
- Captures Flameshot : `godot-shots/2026-11-06-strengthen-plus14-*.png`.

**Sertissage de gemme (inlay) par UX réelle, `verified` (2026-11-06)** :
- Flux natif implémenté : clic droit sur item UseType 3/5 (gemme kind 45,
  scrolls 56/79) → `InventoryWindow.Held` + notice « Cliquez sur un
  équipement… » → clic gauche sur l'équipement (sac ou panneau
  équipé, contType 0/1) → `ZoneItemUse` NCID 19 avec champs cible
  `[contType][contIdx][loc]`.
- Live réel : `@rclick` sur cellule gemme 30937 (sac 0 slot 31) +
  `@click` sur cellule arme 32041 (slot 29, 2 châsses) → NCID 19 émis
  → zone `Inlay gemme=30937 → item=32041 socket=1 left=0` →
  `game.inventory` : `embedded_id1=embedded_id2=30937`, persisté.
- Refus natifs prouvés : texte 3278 (niveau équipement trop bas vs
  gemme), texte 195 (aucune châsse).
- Tooltip : ligne « Emplacements » rend `MaxSocket` icônes —
  `Meta.Embedded[i]>0` → icône `EmbedJewelry` (grille ToolTips 12×12,
  idx 18), sinon `EmbedNone` (idx 2). Capture : 2 icônes pleines.
- **Bug corrigé** : `AssetCatalog.LoadTexture` ne savait lire que
  DDS/ifl — `ToolTips.png` (PNG natif) tombait en « texture absente » →
  les icônes sockets/étoiles ne rendaient jamais. Fallback
  `EdenVfs.LoadImage` pour les `.png` (fixe aussi tout autre imageset
  PNG demandé par `GetTexture`).
- **GemBreaker (`gembreaker.layout`)** : fenêtre native d'extraction de
  gemme (Rune1-6). Déclencheur natif = item dont le champ +0x98 vaut
  `0x2334`/9012 (FUN_0083a8d0 client_game — ouvre aussi DyeingWindow
  0x2336-9 / ChangePetNameWnd 0x2338) ; **aucun item EDENFR ne porte
  9012** (c_item, c_enchant, itemmallenchant vides) → fonction
  non atteignable avec les données fr-full — non implémentée, gap
  documenté dans EDENFR-GAPS.

**Ordre de travail ferme (type 13) par UX réelle, `verified` (2026-11-06)** :
- Fermier Félix `50350` (S001, ~(249,486)) → dialogue 285 → option
  index 0 « Missions Agricoles » → NCID 11 → **496** EnterWorks
  type=13 → `worklistwnd.layout` peuplé des recettes farm.
- Clic ligne recette (produit 30001) → `collectwnd.layout` ouvert
  (slot objet, charme, temps 20 min, coût, produit, Commission).
- Clic `CommissionBtn` → catalyseur `30136` auto-localisé (sac 0
  slot 18) → **170** `[workId=30136][cat 0:18][boost -1:-1]` → zone
  `Work armé num=0 type=13` → ~10 s (`EDEN_DEV_WORK_CAP=100`) →
  **498** résultat → `workresultwnd.layout` → OK → **171** → **500**.
- DB `game.inventory1` loc 18 : `item_id=30001, durability=10`
  (qté stackable) — le parchemin consommé, produit ×10 livré.
- **Bugs client corrigés** : (a) worklist filtrait
  `cat = workType-12` alors que col1 = `kind-0x32` (type 13 → cat 0) ;
  (b) fenêtres craft construites à `_Ready` avant le ctx CEGUI →
  re-build au premier 496 ; (c) **le 170 portait le work_id col0 au
  lieu de l'item id catalyseur** — la map `CCollectData` native est
  cléée par col2 (désasm `CCollectQuery::Deserialize` @0x085cd020)
  → refus `textId=4` « Item does not exist » ; (d) probe `@dlg`
  ne fermait pas la fenêtre dialogue (le clic natif si) → les clics
  suivants étaient mangés par le dialogue invisible.
- Capture Flameshot : `godot-shots/workorder-collect-commission-2026-11-06.png`.

Repérage exhaustif des systèmes de craft et de guilde EDENFR, en 20 rapports
sourcés (`fichier:ligne` / `binaire @ 0x<addr>`) :

📁 **`docs/reports/craft-guild-recon-2026-11-03/`**

- `SYNTHESE.md` — matrice croisée + plan d'action ordonné (phases A-G). **Commencer ici.**
- `00-PLAN.md` — méthode du repérage (5 vagues × sources).
- Métier (knowledge/) : `K1` crafts raciaux · `K2` guilde+VDG · `K3` récolte/outils · `K4` fortif/fusion/enchants/cube · `K5` économie/monnaies/achats.
- Données (corpus fr-full) : `D1` tables craft · `D2` tables guilde/VDG · `D3` items · `D4` layouts craft/fusion · `D5` layouts guilde/commerce · `D6` ref-shots+visuels.
- Décompilé (re/) : `R1` natif guilde/VDG zone · `R2` natif work/craft/shop zone · `R3` natif UI client (map 621 handlers + 210 senders dans `client-ui-maps/`).
- Serveur C# : `S1` craft/enchant · `S2` guilde/VDG · `S3` commerce.
- Client Godot : `G1` fenêtres · `G2` protocole NCID · `G3` HUD/tooltips/icônes.

## Raccourcis vocabulaire (pièges connus)

| Joueur | Code | Tables |
|---|---|---|
| Guilde | **Family** | `GuildFunction`, `family*.layout` |
| VDG | FamilyTown/Instance | `c_buliding`, `4PHouse`, `decorate`, maps S501/S502 |
| Plans récolte/craft | **Work** | `c_familycollect` (478), `c_familycombine` (3 178) |
| Craft racial | **LifeSkill** | `c_lifeskill` (595, 5 groupes de race) |
| Fortification | **Strengthen** | `S_FamilyCombine` id 1-20, scrolls 40002-40019 |
| Boîte mystique | **Cube** (op 0x52) | `c_itemchest` (1 538) |
| Magic Box (étoiles) | op 0x58 | `c_itemcombo` (5 413) |

## État global (résumé de la matrice)

- **Serveur C#** : craft/guilde quasi intégralement implémenté — les trous sont
  précis et listés (SYNTHESE §phase E : cap DEV 10 s, combos strengthen,
  monnaies fame-shop, lifecycle bâtiments…).
- **Client Godot** : aucune UI craft/guilde fidèle — toutes custom ou
  squelettes ; ~29/40 layouts `family*` et toute la pile cube/magicbox/
  strengthen/lifeskill absents. Le déblocage n°1 = widget `ItemSlot`
  interactif dans `CeguiBuilder` (SYNTHESE §phase A).
- **Visuel** : specs = les 415 `.layout` du corpus + ~40 ref-shots
  cataloguées ; **aucun ref-shot** pour strengthen/family building/work/
  cube — la fidélité se mesurera aux layouts.

⚠️ `c_*` = Big5, `t_*` = CP1252, `S_*` côté serveur `server_us/` uniquement.
Périmètre contenu : ≤ niveau 90 (les paliers 95/100/110 des rapports =
Vendetta, hors implémentation).
