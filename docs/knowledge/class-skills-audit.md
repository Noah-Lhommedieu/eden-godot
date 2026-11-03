# Audit chaîne « skills du panneau Classe » — c_spell/c_class/c_talent → 166/489/311 → rendu

Statut : **verified** (code × données corpus × paquets × captures croisés) —
2026-11-03 — lecture seule, aucune modif, aucun build/test (demande explicite ;
le code reste en mouvement sur un autre agent).

Objet : demande 11 du `docs/reports/HANDOFF-2026-11-02.md` — « Panneau Classe :
TOUS les skills manquants ; talents non dynamiques par arbre de classe/éveil »,
d'après la capture `artifacts/shots/20261102-113239-S001-hud-class-inv.png`
(« Classe vide », « En attente de la liste »).

## Verdict court

La chaîne bout-en-bout est **câblée et cohérente** : tables natives présentes et
parsées correctement, dérivation des listes conforme à l'oracle serveur
(`ClassSkillTable`), paquets 166/489/311/286 émis et traités, rafraîchissement
événementiel complet. **Les trois captures « panneau vide » existantes ont
toutes été prises en `--solo` via `tools/capture.sh`** (le script impose
`--map … --solo --shot`, `tools/capture.sh:15` ; logs `[hud] --hud-open planifié
(class)` dans les `.log` associés). En solo — sans session, sans personnage,
sans `--class-mock` — le panneau vide est le comportement **structuré** du code.
**H1 tranchée par capture le 2026-11-03** : `tools/capture.sh S001 classmock --hud-open=class -- --class-mock=1;1;1:30,2:20,8:5` → `artifacts/shots/20261103-154342-S001-classmock.png` montre le panneau **rempli** (liste classes, Comp. Générales et Compétences de classe avec icônes/noms FR/niveaux, 120 PC). La chaîne tables→rendu fonctionne ; le symptôme initial était l'absence de données de session (`--solo` sans 489).
(`RefreshSkills` retourne quand `sel ≤ 0`, `ClassWindow.cs:2101-2102`).
Le symptôme rapporté est donc très probablement un **artefact de la méthode de
capture**, pas un bug du client : la version live n'a jamais été validée.

Restent de vrais points faibles trouvés au passage (§ Écarts), dont un
verrouillage silencieux et définitif des tables si la résolution d'assets échoue
au premier appel, et 4 sorts de classe natifs perdus par le filtre `Pay == 9`.

## 1. Attendu (preuves natives)

### Tables EDENFR (`corpus/fr-full/data/db/`)

| Table | Rôle | En-tête / taille |
|---|---|---|
| `c_spell.ini` | référentiel sorts (icône, famille, niveau requis, masque de classes, monnaie) | `\|V.7\|44\|`, 19 501 lignes physiques dont descriptions Big5 multi-lignes, ~4 165 sorts |
| `c_class.ini` | définition de classe : vecteur de 19 spellmasters, 8 certificats, 6 talents, stats % | `\|V.6\|65\|`, 305 lignes = 61 classes × 5 races |
| `c_talent.ini` | arbres « License Knowledge » : lignes `classe×10+branche`, 8 cellules chacune | `\|V.2\|10\|` |
| `c_spellreference.ini` | taux de coût +1 par niveau (utilisé par `NextCost`) | — |

**Aucune** table `c_skill`/`s_skill`/`skilllist` n'existe dans fr-full : le nom
« skill list » du symptôme correspond à `c_spell` + `c_class` + `c_talent`
(vérifié par `find`, cf. `docs/reference/corpus/tables-donnees.md` § `c_class`,
`c_spell`, `c_talent`, `lic`).

Colonnes `c_spell` fr-full utilisées par le code (0-basé après split `|`) :
0 id, 1 icône (code J/G…), 17 famille (7 = passif), 22 niveau de classe requis,
23 masque u64 bit(classe-1), 41 monnaie (1 = or, 9 = CP, 0 = non payant),
42 base de coût — `ClassSkillTree.cs:18-21,72-91`. La ligne se termine par un
`|` → 45 cellules pour 44 colonnes déclarées.

Vecteur `c_class` : ancrage dynamique au premier run de ≥ 4 ids ≥ 50000
(`ClassTable.FirstSpellBlockIndex`, `ClassTable.cs:169-177`), sinon col 30 ;
8 certificats juste avant, 6 talents juste après le bloc de 19
(`ClassTable.cs:103-127`).

### Règle native (oracle serveur recodé)

`eden_server/server/src/Eden.Core/Game/Classes/ClassSkillTable.cs` :

- **Class skills** (`GrantableSpells`, L45-86) = vecteur `S_Class` ∪ sorts à
  masque **strictement mono-classe**, filtrés `1 ≤ RequiredLevel ≤ classLevel`,
  dédupliqués par code J (palier de certificat = plus petit id). **Aucun filtre
  `pay` ni `family` côté octroi.**
- **Reg./general skills** (`GrantableGeneralSpells`, L102-125) = masque
  **multi-bits** contenant le bit de la classe (couleur partagée), gatés sur le
  niveau **personnage** ; sorts raciaux 50372-50421 filtrés par race
  (`raceIndex = (id-50372)%10/2`, 0-4 = Humain/Zumi/Anuran/Ursun/Halfkin).
- `CanCast` : masque 0 ou bit actif ou multi-bits → lançable après switch.

### Layouts natifs (`corpus/fr-full/uidata/layouts/`)

- `newskilllist.layout` : `Root/NewSkillList/Detail/Skill/border/` →
  **`CommonSkillList`** (gauche, « 共用技能 » = skills communs/or) +
  **`SkillList`** (droite, « 職業技能 » = skills de classe/CP) + `Money`
  (bandeau monnaie). Fond illustré `NewSkillList.png` posé par code sur
  `BGImage` (`ui-atlas-map.md:81,83,123`).
- `newknowledge.layout` : `Detail/InnateBorder/Arrow{b}{1-7}` +
  `PointInfo{b}{1-8}` — 3 branches × 8 colonnes, fond `NewKnowledge.png`
  (statue d'ange) (`ui-atlas-map.md:124`).
- `newlicence.layout` : `Detail/Licenseborder/ComboIcon0-6` (7 slots actifs +
  2 cases non gérées), `SecretIcon*`, `LicenseScrpllBG/pane` (liste droite),
  fond `NewLicence.png` (cristal) (`ui-atlas-map.md:125`).
- Conteneur onglets : `jobwnd.layout` (`Root/Job`, TabControl vide — onglets
  posés par code) ; colonne classes : `joblistwnd*`/`newjoblist` ; fiche :
  `jobcurrentwnd*`/`jobinfo` (`ui-atlas-map.md:117-127`).

### Ref-shots (`eden_godot/docs/client-assets/ref-shots-catalog.md` § 4)

- `ref-shots/ClassePanel.png` : « Reg. Skills » (or, « 550 Gold 9 Silver »,
  2 rangées, boutons +1/Max) **+** « Class Skills » (« 4331 remaining CP » :
  Light's Sanction Lv.9, Life Cure Lv.11, Hard Truth Lv.11, Purifying Wind).
- `ClassePanel-ClassTalent&Certificat.png` : arbre de talents + « 7 KPs
  Remain / 1 KPs Entered », Confirm/Refresh ; « Certificates: 9 », grille
  fleur-de-lis 4×3 + liste droite.
- `ClassPanel-ChangementDeClasse.png` : après switch, la liste de skills de la
  nouvelle classe s'affiche (Thief Lv.1 : Slam, Ambush…).

## 2. Constaté (implémentation)

### Chaîne réseau

| Étape | Fichier:ligne | Détail |
|---|---|---|
| Requête liste classes | `scripts/session/EdenSession.cs:781-806` | opcode **166** `QueryClassList` dans le burst d'entrée zone ; **181** `QueryLicense` aussi |
| Réponse 489 | `scripts/hud/ClassChangeDialog.cs:74-88` | `ZonePlayerClassList.Deserialize` → `_classes` → `ClassesChanged` (file main-thread `_pending`) |
| Format 489 | `scripts/protocol/ZoneClassMessages.cs` | `[u8][u8][u16 n]` puis par classe `[u8 id][u8 lv][u8 style][i32 exp][i32 pts][u8 aa][7×u16 lic]` — conforme au wire natif décrit dans `eden_server/.../ZoneEntryFlow.Services.cs:186-200` |
| Sorts connus 311 | `scripts/GameScene.cs:2742-2753` | `PlayerEquipment.ApplyEnterNode` + `PlayerSkills.ApplyEnterNode(spells)` |
| `PlayerSkills` | `scripts/protocol/ZoneEquipMessages.cs:326-351` | `Spells` (liste), `Changed`, `SetLevel` ; 286 `ZoneClientGetSpell` (`ZoneMiscMessages.cs:109`) bindé `ZoneMiscBinds.cs:45` → `MiscPackets.Received` → `ClassWindow.OnMisc` |
| Classe active | `scripts/protocol/ZoneVitalsMessages.cs:202,275` | `PlayerVitals.ActiveClass` ← attr 280 `0x2c` (`ClassChanged`) ; serveur : émis dans le burst de **changement** (`Services.cs:417-447`), **pas à l'entrée** — `CurrentClass` repose alors sur `SessionCarry.Character.ClassId` |
| Licences 509 | `scripts/hud/HudExtensions.cs:464` + `ZoneEntryDataMessages.cs:268-280` | `PlayerProfileState.Licenses` (merge par itemId) |
| Talents 471 | `HudExtensions.cs:460` + `ZoneEntryDataMessages.cs:249-256` | `Talents[effectId]=pt`, **type==0 seulement** |
| Serveur | `eden_server/.../ZoneEntryFlow.cs:910` → `Services.cs:167-245` | 489 émis **uniquement sur 166** (`HandleQueryClassListAsync` : toutes classes accessibles + possédées) → 592 → **471** → mods talents ; sorts d'entrée via `EnsureEntrySpellsAsync` (`EntryPipeline.Load.cs:303-331`, `ClassSkillTable.GrantableSpells` + `GrantableGeneralSpells`, dédupliqués par `known.Add`) |

### Chaîne rendu (`scripts/hud/ClassWindow.cs`)

- `Build` (L47-81) : `ClassChangeDialog.ClassesChanged += Refresh`,
  `PlayerSkills.Changed += RefreshSkills`, `PlayerVitals.ClassChanged`, or,
  points, `MiscPackets.Received`, `VisibilityChanged → Refresh`. Mock démo :
  `--class-mock=cur;sel;id:lv,…` (L60-62, `ApplyMock` L86-106 — peuple
  `_mock`, `_mockCurrent` et quelques niveaux via `PlayerSkills.SetLevel`).
- `Refresh` (L1736-1745) : News → License → ClassList → Header → Banners →
  **Skills**.
- `RefreshClassList` (L1784-1820) : vide → « Waiting for the class list… »
  (L1812-1816). Colonne = classes de base du 489 + classes non débloquées
  grisées via `TextResolver.ClassName`.
- `CurrentClass` (L1731-1734) : `_mockCurrent` → `PlayerVitals.ActiveClass` →
  `SessionCarry.Character?.ClassId` → **-1**.
  `SessionCarry.Character` n'est posé qu'en sélection de personnage
  (`scripts/screens/CharSelectScreen.cs:855`) → **null en `--solo`**.
- `RefreshSkills` (L2096-2117) : vide `_regGrid`/`_classGrid` →
  `sel = _selected>0 ? _selected : CurrentClass` ; **`if (sel <= 0) return;`**
  → `ClassSkillTree.EnsureLoaded(AssetCatalog.ResolveRawAsset)` →
  `known` = `PlayerSkills.Spells.ToDictionary` (niveau affiché uniquement) →
  `RegSkills(sel, race)` × `pay=1` dans `_regGrid` (GridContainer 2 col.,
  L497-503), `ClassSkills(sel)` × `pay=9` dans `_classGrid` (VBox 1 col.,
  L506-511) — structure identique au `CommonSkillList`/`SkillList` natif.
  `race = ch.RaceId - 1` (L2107).
- `SkillCell` (L2151+) : icône `SpellCatalog.Icon`, nom, « Lv.N »/« Not
  learned », +1/Max (`ZoneUseSkillPoint` 12 / `ZoneUseSkillPointMax` 623,
  `ZoneClassMessages.cs:145-158`), drag vers hotbar — conforme au ref-shot.

### Chaîne tables (`scripts/data/tables/`)

- `ClassSkillTree.EnsureLoaded` (L57-69) : `resolve("data/db/c_spell.ini" /
  c_class / c_talent)` ; **si le resolve renvoie null → dictionnaire vide mis
  en cache** ; `lock` + « déjà chargé » = `!= null` → **état vide définitif,
  sans aucun log**.
- `LoadSpells` (L72-91) : `IniTable.ParseFile(path)` — encodage par défaut
  Big5 (`IniTable.cs:52,89`), multi-lignes jointes (descriptions LF nus), les
  int utilisés sont ASCII → robuste.
- `ClassSkills` (L117-138) : vecteur `ClassDef.ClassSpellIds` présents dans
  `_spells`, `Family != 7`, dédup ; puis **mono-masque `Mask == bit` +
  `Family != 7` + `Pay == 9`** (L131-135).
- `RegSkills` (L154-170) : `Pay == 1` + `(Mask & bit) != 0` +
  `PopCount(Mask) ≤ 30` + racial `IsRacial` → `raceIndex`. `BaseOf` éveil →
  base (L100-107).
- `Talents` (L176-182) : `TalentTable.Branches(local)` sinon base —
  `RefreshLicense` (L1263-1310) l'utilise avec cover `classLevel<2`,
  déblocage niveau+prérequis, `_pendingAa`, compteurs KPs.
- `DataWarmup.cs:24-27` pré-chauffe `ClassWindow.WarmTalents()` (c_talent) et
  `SpellCatalog` (c_spell icônes) — **mais pas `ClassSkillTree`** : son premier
  parse `c_spell` a lieu à l'ouverture du panneau.
- `ExpertiseWindows.cs` : fenêtre séparée (« Full Class Ability Bonus List »,
  `c_classrankbuff.ini`), hors chaîne du panneau Classe.

### Graphe de code

`tools/graph/graph.sh query "ClassWindow"` → 266 nœuds : `HudRoot.BuildAll()`
→ `ClassWindow.Build` → toutes les méthodes ci-dessus présentes et rattachées.
`query "SkillList"` → **0 nœud** : aucun symbole `SkillList` n'existe ; la
liste de skills vit dans `ClassWindow` + `ClassSkillTree` + `SpellCatalog`.

## 3. Écarts attendu vs constaté

1. **`Pay == 9` sur la branche mono-masque** (`ClassSkillTree.cs:133`) —
   l'oracle serveur n'a pas ce filtre (`GrantableSpells` L73-76). Simulation
   sur fr-full : 14 sorts mono-masque `fam ≠ 7` avec `pay = 0` ; 10 sont déjà
   dans le vecteur `c_class` de leur classe (affichés quand même), mais
   **4 sont absents du vecteur** → perdus pour le panneau :
   `50455` (classe 18), `50685`+`50686` (classe 20, Messie), `59459`
   (classe 38). Écart réel, faible surface.
   (`RegSkills` : aucun sort mono-masque `pay==1` ni multi-masque `pay==1`
   `fam==7` dans les données → le filtre `Pay==1` est sans effet pervers.)
2. **`EnsureLoaded` sans diagnostic** (`ClassSkillTree.cs:61-68`) : un resolve
   → null produit un cache **vide et définitif** pour la session ; aucun
   `GD.Print` contrairement à `SpellCatalog` (`[skill] C_Spell : …`) et au
   `try/catch` de `RefreshLicense` (L1273-1277). `RefreshSkills` n'a pas de
   garde : une exception de parse laisserait les grilles vidées sans message.
3. **Solo = vide par construction** (ci-dessus) ; seule issue hors-serveur =
   `--class-mock`, non utilisé par les captures du HANDOFF.
4. **471 type 1 (reset talents) ignoré** (`ZoneEntryDataMessages.cs:253`) —
   points obsolètes possibles après reset natif. Talents, pas skills.
5. `BaseOf` dupliqué (`ClassSkillTree.cs:100` / `ClassWindow.cs:1763`) —
   risque de divergence future, pas de bug actuel.
6. `c_spell` fr-full = V.7 (44 col.) vs S_Spell V.8 serveur — les colonnes
   utilisées sont celles du fr-full (documenté `ClassSkillTree.cs:18-21`),
   cohérent avec le test `ClassSkillTreeTests`.

## 4. Hypothèses « skills manquants », triées par probabilité

| # | Hypothèse | Probabilité | Test discriminant |
|---|---|---|---|
| H1 | **Artefact de capture** : les preuves du symptôme sont des runs `--solo`/`--hud-open` sans `--class-mock` → `CurrentClass = -1` → `sel ≤ 0` → retour avant tout ajout ; « Waiting for the class list… » visible = `Classes` vide (pas de 489 hors session). **Étayée** : `capture.sh:15`, logs `[hud] --hud-open planifié (class)`/`(class,inventory)`, `CharSelectScreen.cs:855`, `ClassWindow.cs:1731-1734,2101-2102`. | **Très élevée** | `tools/capture.sh S001 x --hud-open=class -- --class-mock=1;1;1:30,2:20,8:5` → si les 2 sections se remplissent, la chaîne tables→rendu est OK et le symptôme initial = absence de données de session. En live : ouvrir le panneau après entrée zone → skills attendus (classe 1/race Humain : 9 reg `[50011-15,50150,50372,50373,50443]`, 10 class `[50036-42,50151,50152,58345]`). |
| H2 | **Resolve tables KO à l'exécution** (pack/EPK sans `raw_assets`, `EDEN_ALLOW_LOOSE=0`, `EDEN_PACKS` périmés) → `EnsureLoaded` fige des dicts vides → grilles vides **silencieuses**, colonne classes et talents remplis par les paquets (donc « tout sauf skills »). | Moyenne (live packagé) ; nulle en dev loose (`[skill] C_Spell : 4159 icônes` présent dans les logs solo) | Log de dev temporaire dans `EnsureLoaded` (ou `GD.Print` des counts) ; lancer le client packagé/`EDEN_ALLOW_LOOSE=0` → vérifier `data/db/c_spell.ini` résolu ; inspecter le contenu des packs (`tools/edenpack`) pour `data/db/*.ini`. |
| H3 | **Aucune classe valide** (`sel ≤ 0`) en live : `Character.ClassId` exotique (classe personnalisée 592/CC → `id%100` hors 1-63, ou 0). Ne viderait que la vue de cette classe. | Faible-moyenne | En live sur un perso à classe custom : `sel` via log temporaire ; comparer `Character.ClassId` aux ids du 489. |
| H4 | **489 jamais reçu / perdu** : event `ClassesChanged` raté ou `_pending` non drainé → « Waiting for the class list… » persiste et `sel` retombe sur `CurrentClass` (suffisant pour les skills si >0). Le chemin événementiel est complet (bind→enqueue→invoke→Refresh). | Faible | Log serveur `[Zone-Entry] ClassList char=…` (`Services.cs:243-244`) + `NetTests`/client : vérifier `ClassChangeDialog.Classes` non vide après entrée. |
| H5 | **Parser/index `c_class` ou `c_spell` faux** → vecteurs/masques vides. Réfutée par les données : simulation 64-bit reproduit les listes serveur exactes (classe 1 : 9 reg + 10 class identiques à `GrantableSpells`) ; `ClassSkillTreeTests` charge les vraies tables et asserte ces listes (`ClassSkillTreeTests.cs:40-83`). | Très faible | `dotnet test --filter ClassSkillTreeTests` (hors moteur) : vert = parse et filtres prouvés. |
| H6 | **Filtres trop stricts** (`Pay==9` mono-masque, `PopCount>30`, familles, raciaux). Mesuré : 4 sorts perdus (§3.1) — réel mais jamais « TOUS les skills ». `PopCount>30` n'exclut que des génériques volontairement hors panneau. | Faible (impact borné) | Comparer `ClassSkills(id)` vs `GrantableSpells(id, maxLvl, …)` pour classes 18/20/38. |
| H7 | **Exception dans `RefreshSkills`** (`EnsureLoaded`/parse/`ToDictionary`) après `QueueFree` → grille vidée sans log explicite (l'erreur remonte au handler `VisibilityChanged`, Godot la journalise). Doublons `SpellMaster` impossibles côté serveur (`known.Add`, `EntryPipeline.Load.cs:319-326`). | Faible | `grep` le log client pour `scripts/hud/ClassWindow` / `EnsureLoaded` après ouverture ; erreur visible dans `godot.log`/`stderr`. |
| H8 | **Données périmées** (pack non reconstruit après modif d'assets — règle connue du projet ; aucun `.inib` trouvé dans l'arbre). | Faible | `EDEN_ALLOW_LOOSE=1` (dev) ou rebuild des packs → comparaison. |
| H9 | **Race/classe composite** (`RaceId-1`, `id%100`) : les composites ×100 sont gérés (`ClassDef` fallback L112-113, `local=id%100` L128) ; au pire les raciaux sont exclus si `RaceId` n'est pas 1-5 → −2 lignes Reg, pas vide. | Très faible | Perso non-Humain : `RegSkills(1, race)` doit contenir le racial de sa race (50372+race×2). |
| H10 | **Visibilité/clipping** : les conteneurs sont des `ScrollContainer`/`GridContainer` montés à L459-512 et la page suit `SelectTab(0)` — un contenu ajouté serait visible ; pas de preuve d'un bug de visibilité. | Très faible | `--hud-dump` des rects `_regGrid`/`_classGrid` + comptage `GetChildCount` dans un run mock. |

## 5. Certificats / licences (même famille, plus bref)

- Sources : 509 (`PlayerProfileState.Licenses`, classe active, locs 1-7) et
  489 `entry.Licenses[i]` (autres classes) — `FillCertGrid` L1514-1528,
  commentaire L1505-1513. Liste droite `_licCerts` = licences possédées non
  posées (L1455-1495) ; compteur « Certificates: N » = `min(7, level/10+2)`
  (L1451-1454). `CertificateIds` de `c_class` n'alimente que la rangée
  d'icônes de la fiche News (L960-962), pas la grille — conforme au natif
  (`ComboIcon0-6` + scroll).
- Pose = opcode 182 (`CNE_CZ_ZoneServerPlayerLicense`,
  `ZoneClientToServerMessages.cs:814-822`), combos → 183.
- État vide analogue : sans 489/509 (solo), grille vide — même cause que H1.
- Note : `RefreshLicense` catch et log `[class] tables talents` (L1273-1277) —
  le modèle à généraliser si `RefreshSkills` gagne une garde.

## 6. À faire valider (hors périmètre de cet audit)

1. Capture `--class-mock` (H1) : `tools/capture.sh S001 classmock
   --hud-open=class -- --class-mock=1;1;1:30,2:20,8:5` — attendu : Reg 2 col.
   remplies, Class Skills remplies, talents de la classe 1.
2. Run live (serveur `eden_server/start-native.sh`, compte test) : ouvrir K →
   comparer aux listes attendues du §1/H1 sur ≥ 3 classes dont une éveil
   (consigne du HANDOFF). Journal `[Zone-Entry] ClassList` + éventuel
   `[class] tables talents`/`[skill]`.
3. Tests hors moteur : `dotnet test --filter ClassSkillTreeTests`
   (`tools/NetTests`) — prouve tables + filtres sans moteur.

## Limites de l'audit

- Aucun build, test ni run effectué (interdit par la demande) ; les listes
  attendues ont été reproduites par simulation Python 64-bit sur les fichiers
  natifs, pas par exécution du code client.
- `graphs/graphify` non régénéré (lecture seule) ; requêtes lancées sur le
  graphe existant (49129 nœuds).
- Le comportement « live » du panneau n'a jamais été observé : H1 reste une
  inférence forte de la méthode de capture, pas une preuve visuelle du mode
  connecté.
