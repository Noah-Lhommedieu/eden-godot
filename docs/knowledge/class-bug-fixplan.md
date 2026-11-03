# Plan d'action consolidé — skins de classes + Class Ability Bonus + panneau skills

Statut : **verified** pour les constats (synthèse des quatre audits du
2026-11-03, eux-mêmes `verified` côté code) — **hypothesis** pour l'ordre et
l'ampleur in-game (plan non exécuté, aucun build/test). Document plan
uniquement : synthétise, ne refait pas les audits. 2026-11-03.

Périmètre : HANDOFF #3 (skins de classes / Class Ability Bonus) et #11
(panneau skills) — `docs/reports/HANDOFF-2026-11-02.md`.

## Sources

| Source | Objet |
|---|---|
| `docs/knowledge/class-skins-audit.md` | Chaîne skins client : `c_class.ini` → `4pequip.ini` → mesh+skin. Constatés : repli « 202 » (H1), `style=0` faute de 489 (H2), coiffe code1 jamais affichée (H3), `RowForSuffix` = variante fixe (H5), costumes invisibles au sélecteur (H6). |
| `docs/knowledge/rankbuff-server-audit.md` | Chaîne serveur Class Ability Bonus. Bug A (lookup champ 0), Bug B (cmd 2047 `HealPlus` flat ignorée), Bug C (éveillées lv1 — **= D2**), Bug D (pas de re-resolve sur octroi direct). |
| `docs/knowledge/class-skills-audit.md` | Chaîne panneau Classe client. Verdict : câblée ; « panneau vide » = artefact `--solo` de `tools/capture.sh` (H1). Écarts : `Pay==9` (−4 sorts), `EnsureLoaded` cache vide silencieux, 471 type-1 (reset) ignoré, `BaseOf` dupliqué. |
| `docs/knowledge/class-skills-server-audit.md` | Chaîne serveur acquisition/runtime. D1 (pas d'`AddClassFromLimit`), D2 (éveillées lv1), D3 (lignes raciales injoignables → Humain), D4 (flag race `S_Spell` non parsé), D5-D8 mineurs. |
| `artifacts/corpus-inventory/class-outfits.tsv` | 20 720 lignes (classe × niveau × corps) : mesh/skin attendus par palier. |
| `artifacts/corpus-inventory/class-rankbuff-expected.tsv` (+ `class-rankbuff-notes.md`) | Paliers attendus par classe (FR 48 lignes / US 59+1 ; formule `rang=floor(min(lv,100)×0.2)`). |
| `artifacts/corpus-inventory/class-skills-expected.tsv` (+ `class-skills-expected-notes.md`) | Listes reg/class/expertise attendues par classe et race (règles du client + oracle serveur). |
| `docs/workflows/EDENFR-GAPS.md` | G24 (12 éveillées sans ligne `c_classrankbuff` FR) ; G25 (85+ skills éveillées sans nom FR). |

Chemins : client sous `eden_godot/godot_project/scripts/` ; serveur sous
`eden_server/server/src/` (`Eden.Core`, `Eden.Zone`…). Les numéros de ligne
sont ceux de l'arbre au 2026-11-03 — re-vérifier avant d'éditer.

## Préalable : valider le symptôme « panneau vide » avant tout fix client

Les trois captures du HANDOFF sont des runs `--solo` (`tools/capture.sh:15`
impose `--map … --solo --shot`) : sans session ni `--class-mock`,
`RefreshSkills` retourne quand `sel ≤ 0` (`hud/ClassWindow.cs:2101-2102`) —
le vide est **structuré**, pas un bug (audit skills §verdict, H1 très
élevée). Avant de toucher au panneau :

1. `tools/capture.sh S001 classmock --hud-open=class -- --class-mock=1;1;1:30,2:20,8:5`
   — attendu : Reg (2 col.) + Class Skills remplis. Si oui, le symptôme
   était l'absence de session ; les fixes F9-F11 deviennent des corrections
   de robustesse, pas la cause du rapport.
2. `dotnet test tools/NetTests --filter ClassSkillTreeTests` — tables +
   filtres hors moteur (classe 1 : 9 reg + 10 class attendus).
3. Run live (`eden_server/start-native.sh`, compte test) : ouvrir K sur
   ≥ 3 classes dont une éveillée ; comparer à `class-skills-expected.tsv`.

## Fixes ordonnés par impact décroissant

| # | Fix | Fichier:ligne | Hypothèse | Changement minimal | Test discriminant | Risque / régression |
|---|---|---|---|---|---|---|
| F1 | **D1 — `AddClassFromLimit` absent : les classes ne se débloquent jamais par le gameplay** | `Eden.Core/Data/Parsers/ClassStatsParser.cs:135-199` (champs prérequis non parsés) ; appels manquants dans `Eden.Zone/ZoneEntryFlow.Combat.Rewards.cs:260-435` (`GrantClassPointAsync`), `:445+` (`GrantKillExpAsync`), `ZoneEntryFlow.EntryPipeline.Load.cs` (équivalent `PostInit` au login) | Natif `AddClassFromLimit` @0x080fd440 appelé à `PostInit`/`GainClassPoint`/`GainExp` : itère les lignes `S_Class` de la **race du perso** (bande `raceIdx×100..+99`), octroie toute classe dont `+0x25` (niveau perso requis) et `+0x20`/`+0x24` (masque + niveau de classes prérequises) sont satisfaits → le 489 s'enrichit seul | Parser les 3 champs prérequis dans `ClassDefinition` ; ajouter un balayage « déblocage » itérant la bande race (`(RaceId-1)*100`, helper de F5) et appelant le chemin d'octroi unifié de F4 ; l'appeler en fin de `GrantClassPointAsync`, `GrantKillExpAsync` et à l'entrée zone | Perso remplissant les prérequis natifs d'une classe avancée (ex. classe 6) → la classe apparaît dans `player_classlist`/489 au prochain gain ou à la reconnexion. Aujourd'hui : jamais (`AccessibleClassIds` figé {1,2,3,4,5,19}) | **Prérequis bloquant : la correspondance colonnes `S_Class.ini` ↔ `+0x20/+0x24/+0x25` n'est pas revérifiée** (limite d'audit) — aligner sur ≥ 2 classes connues avant d'activer, sinon déblocages erronés. Dépend de F5 (bande race) et F4 (init complète). Le service mort `ClassChangeService` (`Eden.Core/Game/Services/`) peut servir de point de départ, sans appelant à détourner |
| F2 | **Bug A — rankbuff : lookup par index de ligne au lieu du classId** | `Eden.Zone/Program.cs:423` (`d.Id == row.ClassId` ; `Id` = champ 0) ; commentaire faux `Eden.Core/Data/Parsers/ShopAndMiscParsers.cs:240` (« Id = classId ») | Natif `Skip(1)` : le champ 0 est un index ignoré, la clé est le **champ 1**. `ClassId` est parsé mais jamais utilisé → classes 1-19 correctes, tout le reste décalé : Messie(20) reçoit les bonus de 21, Juge(59) ceux de 57, classe 61 rien, bases 49-52 perdent paliers 1-16, cumul inter-classes incohérent | Comparer `d.ClassId == row.ClassId` (une comparaison) ; corriger le commentaire du modèle. Aucun changement de parseur | `dotnet test` : `ClassRankBuffs.First(d => d.ClassId == 59).Tiers[0].EnchantId == 40911` sur le fichier réel ; étendre `GameDataTests.cs:44-58` (ne couvre que f0==f1 aujourd'hui — d'où les tests verts malgré le bug) | Stats de tous les persos multi-classés >19 changent au recalcul — c'est la correction. Avec la table US chargée, les 12 éveillées sans ligne FR reçoivent des bonus **invisibles dans l'UI FR** (G24 : effet serveur réel, affichage client absent — à consigner, pas à masquer) |
| F3 | **Bug B — cmd 2047 `HealPlus` flat ignorée** | `Eden.Core/Game/Classes/LicenseService.cs:412-594` (`ApplyEnchantCommands`, pas de case 2047 ; commentaire :585-591 « dupliqué de 2038 » faux) ; `StatContribution` sans canal heal flat (seuls `HealGiven/TakenPercent` pour 2038 modes 2/3) | 2047 = `CEC_HealPlus` @0x08535480 : mode 4 → soins prodigués **flat** (`+0x144`), mode 5 → soins reçus flat (`+0x14c`). 168/560 enchants de palier portent une 2047 (classes à « Soin » : 4,6,11,12,13,15,17,27,32,42,44,46,50,54) → bonus perdu silencieusement, affichage client sans effet | Ajouter 2 canaux flat (heal given / heal taken) à `StatContribution` + consommation dans les formules Heal/BeHeal ; case 2047 modes 4/5 dans `ApplyEnchantCommands` | Enchant 45747 (`2084|…` + `2047|13`) → contribution heal-given flat +13 (aujourd'hui nulle — canal absent, à créer pour rendre le test exprimable) | Surface moyenne : nouveau canal stats à câbler pipeline → formules de soins. Le « Soin » affiché devient réel → soigneurs effectivement buffés |
| F4 | **D2 = Bug C — éveillées insérées lv1/aa1 au lieu de lv80/aa80** *(Bug C rankbuff = même fix ; absorbe aussi Bug D et D6)* | `Eden.Core/Database/Repositories/ClassRepository.cs:86-102` (`AddClassAsync` insère `1,0,0,1` pour toute classe) ; orchestration `Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:339-373` (`eden-add-class`), `ZoneEntryFlow.Admin.cs:325-334` (GM `add_class` : ni sorts, ni 489, ni licences), `ZoneEntryFlow.Services.cs:300-307` (base non possédée au switch — lv1 correct) ; helpers inutilisés `Eden.Core/Game/Classes/ClassProgressEntity.cs:36-71` (`IsAwakened`/`GetBaseClassId`) | Natif `AddClass` @0x080fd070 : `classId != base` (`GameData+0x1d8`) → `level=80`, `aa_point=80`, `GetLicense` des `level/10` premiers certs (+`RemoveLicense` du tier remplacé), `SimpleGetSpell` vecteur `+0x50` (niv. 1) et `+0x5c` (**spell_level 80**), `RefreshJPAward`, 489 | Distinguer éveillée (`classId != GetBaseClassId`) → insert `80/80` ; orchestrer : certs des paliers `lv/10` via `GrantLicenseAsync`, sorts `+0x5c` avec `spell_level=80` (param niveau sur `LearnSpell(s)Async` :405,441 — **absorbe D6**), refresh JPAward sur les chemins d'octroi direct — **absorbe Bug D** (pas de re-resolve sur `eden-add-class`/GM ; sans lui l'éveil lv80 d'un perso lv85+ changerait le cumul sans réémission) ; 489 sur le chemin GM | `eden-add-class 21` sur perso test → `SELECT level, aa_point FROM player_classlist WHERE class=21` = `80,80` (constaté `1,1`) ; `player_license` ≥ 8 certs 33xxx ; `player_spells` des sorts `+0x5c` à `spell_level=80` | **Migration nécessaire** : les lignes éveillées déjà insérées à lv1 restent lv1 après le fix (limite d'audit explicite). Le lookup « classe de base » exige GameData → le mettre côté service/orchestrateur, pas dans le repository. AA 80 offerts d'un coup = arbre talents éveillé jouable immédiatement (conforme natif) |
| F5 | **D3 — lignes raciales `S_Class` injoignables : toutes les races jouent « Humain »** | 11 sites `RaceId*100+cls || c.Id==cls` : `Eden.Zone/ZoneEntryFlow.Services.cs:332,631`, `ZoneEntryFlow.EntryPipeline.Load.cs:314`, `ZoneEntryFlow.Combat.Rewards.cs:371,746`, `ZoneEntryFlow.Combat.cs:386`, `ZoneEntryFlow.Closet.cs:33`, `ZoneEntryFlow.Mission.Commands.cs:353`, `Eden.Zone/ProfileStatEmitter.cs:63,97,163,230` | La ligne du perso = `(RaceId-1)*100 + classId` (formule native `(nibble>>1)×100+classId` ; déjà utilisée correctement `Program.cs:449`, `ClassSkillTable.cs:116`). Le repli `|| Id==cls` matche toujours la ligne index 0 (fichier trié 1-61 avant 101-461) → stats %, expertises/crafts, certs, garde-robes, talents **Humains** pour Zumi/Anuran/Ursun/Halfkin | Helper unique `ClassFor(raceId, classId)` = `(raceId-1)*100 + classId` avec repli `cls` ; remplacer les 11 sites | Hors moteur : lookup `(2-1)*100+1 || 1` → attendu `Id==101`, constaté `1`. En jeu : guerrier Zumi apprend `50239` (pas `50201`) ; son 296 affiche les stats Zumi (`…,131,65,128,112,…,103` ligne 301 vs `…,123,69,125,114,…,104` ligne 1) | Humains inchangés (corrects par accident). Non-Humains : stats et futurs octrois changent — visible joueurs ; **les sorts déjà appris faux restent en base** (nettoyage à décider). Interaction F6 : même avec la bonne ligne raciale, la branche mono-masque de `GrantableSpells` re-ajoute la variante Humaine en candidat → F5 ne suffit pas sans F6 |
| F6 | **D4 — flag race de `S_Spell` non parsé : crafts/expertises raciaux octroyés à tous** | `Eden.Core/Data/Parsers/SpellParser.cs:36-200` (champ ~col 14 / `SSpellData+0x2c` absent) ; `Eden.Core/Game/Classes/ClassSkillTable.cs:115-122` (`GrantableGeneralSpells` ne filtre la race que sur ids 50372-50421), `:73-85` (`GrantableSpells` dédup `GroupBy(code).Min()` → variante Humaine gagne toujours) | `LoadAllCommonSpell` @0x83732b0 indexe sous `raceIdx×100+bit_classe` via flags race (bits 1/4/16/64/256 → bases 0-400). Constaté : crafts `50202/50240/50273/50307/50339` et supports `50269-50371` appris par **tout le monde** ; expertises J504/J509 → variante Humaine retenue quelle que soit la race | Parser le champ race dans `SpellDef` ; `GrantableGeneralSpells` : filtrer par flag race du perso ; `GrantableSpells` : dédupliquer par code en préférant la variante flaggée race du perso (cas `855` = tous bits = octroi toutes races conforme natif) | Hors moteur : `GrantableGeneralSpells(1, 60, spells, raceId=2)` ne doit pas retourner `50202` (flag Humain). Zumi frais : `player_spells` contient `50240` **sans** `50202` | **Confirmer la position exacte de la colonne (~14) par alignement complet des 45 colonnes avant de parser** — déduite des valeurs 3/4/16/64/768, non revérifiée. Les sorts raciaux étrangers déjà appris restent en base (nettoyage à décider avec F5) |
| F7 | **`RowForSuffix` = première variante : skin AS01 au lieu de ASnn** | `eden_godot/godot_project/scripts/game/CharModelSpec.cs:219-227` (première ligne `Mesh=="#X<suf>"` = plus petit DBn) ; invoqué en repli `scripts/game/CharVisual.cs:432-435` | Un mesh `#X2nn` partagé par plusieurs codes (ex. `#B202` = A01201/02/05/09, skins AS01/02/05/09) affiché **sans sa ligne dans `rows`** (distant `style>0` non transmis, `RetintVisible` sans rows) → teinté variante AS01 au lieu de la sienne (skins-audit H5) | Propager la ligne `EquipMesh` exacte (résolue par **code**) partout où le code est connu (closet, porté) ; `RowForSuffix` ne doit rester qu'un repli quand seul le suffixe est disponible — ne pas lui laisser écraser une ligne connue | `VisTest --skin-classes --skin-style=8` + log `[dye] <mesh> row=… skin=…` → `skin=AS09` attendu ; `AS01` confirme le bug | Cosmétique. Garder le repli pour les cas « suffixe seul » (distants sans style) — le retirer complètement casserait leur teinte |
| F8 | **Repli « 202 » : appelants sans `classId` → tenue Guerrier pour toute classe** | `scripts/screens/CharSelectScreen.cs:575-587` (`c.ClassId` dispo :469/478 ; `PreviewWorn` = conteneur 0 seul `scripts/session/EdenSession.cs:558-562` → **costumes invisibles au sélecteur**) ; `scripts/screens/CharCreateScreen.cs:355-372` (`_class` :105-109 ignoré) ; `scripts/world/EntityRegistry.cs:1386-1407` + `ApplyWornEquipment` :171-184 (`m.ClassId` du 263 parsé `scripts/protocol/CombatMessages.cs:171,212` mais jamais stocké) ; `scripts/screens/CharDressing.cs:44-50` (signature sans classId) ; `scripts/game/CharBodyCache.cs:29-30,88` (clé cache sans classe) | `BaseEquipIds={"202"}` (`CharVisual.cs:113`) = tenue Guerrier A01201, pas « débutant universel » : sélecteur sans armure `#`, création (toujours), distants et previews montrent un Guerrier quelle que soit la classe (skins-audit H1 + H6 costumes) | Ajouter `classId` (+`style` si dispo) à `CharDressing.Apply` ; stocker `m.ClassId` côté distant et le passer ; sélecteur : `c.ClassId` ; création : `_class` courant ; inclure `classId` dans la clé `CharBodyCache` ; sélecteur : fusionner les conteneurs costumes 2-4 comme `VisualWorn` (`ZoneEquipMessages.cs:249-251`) | `--look-log` : ligne `[outfit] letter=X class=C style=S → … (closet=N)` montre le vrai classId ; capture sélecteur d'un mage non équipé → `#G205` (son `closet[0]`) et non `#G202` | Clé cache modifiée → protos reconstruits une fois (perf négligeable). Vérifier en jeu que le champ du 263 est bien le classId (parsé, non observé live). Fusion costumes au sélecteur = fidélité natif à valider sur ref-shot |
| F9 | **`Pay==9` : 4 sorts de classe perdus du panneau** | `eden_godot/godot_project/scripts/data/tables/ClassSkillTree.cs:133` (filtre `Pay == 9` branche mono-masque) | L'oracle serveur n'a pas de filtre `pay` (`GrantableSpells` :73-76) : 14 sorts mono-masque `fam≠7` ont `pay=0` ; 10 rattrapés par le vecteur `c_class`, **4 perdus** : `50455` (classe 18), `50685`+`50686` (classe 20 Messie), `59459` (classe 38). Écart **d'affichage** seulement — le serveur octroie quand même | Retirer le filtre `Pay == 9` de la branche mono-masque (conserver `fam != 7`, `Mask == bit`, dédup) | `dotnet test` `ClassSkillTreeTests` : `ClassSkills(18) ∋ 50455`, `ClassSkills(20) ∋ {50685,50686}`, `ClassSkills(38) ∋ 59459` ; diff `ClassSkills` vs `GrantableSpells` sur ces classes = 0 | `50685/50686` sans nom FR (G25) → libellé fallback id : reproduction de l'absence EDENFR, pas un bug à corriger |
| F10 | **`EnsureLoaded` : resolve KO → cache vide définitif et silencieux** | `eden_godot/godot_project/scripts/data/tables/ClassSkillTree.cs:57-69` (resolve null → dict vide caché pour la session, 0 log) ; absence de garde `scripts/hud/ClassWindow.cs:2096-2117` (`RefreshSkills`) | Pack sans `raw_assets`, `EDEN_ALLOW_LOOSE=0` ou packs périmés → `c_spell.ini`/`c_class.ini`/`c_talent.ini` non résolus → grilles vides silencieuses pendant que paquets (classes, talents) remplissent le reste — le seul mécanisme capable de produire « tout sauf skills » en live (H2) | Ne pas mettre en cache un resolve null (réessai à l'ouverture suivante) ; `GD.Print` des counts modèle `[skill] C_Spell : …` ; try/catch + log dans `RefreshSkills` (modèle `RefreshLicense` `ClassWindow.cs:1273-1277`) | Run packagé ou `EDEN_ALLOW_LOOSE=0` : log explicite du resolve KO ; après correction du pack, réouverture → grilles remplies | Négligeable : en échec persistant, un log par ouverture du panneau |
| F11 | **471 type 1 (reset talents) ignoré → points obsolètes** | `eden_godot/godot_project/scripts/protocol/ZoneEntryDataMessages.cs:249-256` (`Talents[effectId]=pt`, `type==0` seulement) ; doublon `src/Eden.Core/Protocol/ZoneEntryDataMessages.cs` | Le serveur émet 471 type 1 lors d'un reset talents ; le client garde les anciens points affichés (KPs et nœuds d'arbre) | Traiter `type==1` : remplacement du dictionnaire `Talents` par le payload (snapshot). **Vérifier d'abord la sémantique exacte côté émission serveur** (snapshot vs delta) dans `ZoneEntryFlow` | Live : reset talents → l'arbre repasse à l'état serveur sans reconnexion. Hors moteur : payload type 1 → dict remplacé | Si le type 1 est un delta et non un snapshot, une implémentation « remplacer » perdrait des points — lire l'émetteur avant de coder |
| F12 | **Branche code1 morte : la coiffe de classe n'est jamais affichée** | `eden_godot/godot_project/scripts/game/CharModelSpec.cs:265-274` (`ClassOutfitRow` : `map.TryGetValue(e.Male)` filtré `StartsWith('#')` — code1 = grp 2 mesh externe → jamais retenu) ; pipeline d'attache existant `scripts/game/CharVisual.cs:1272-1277` (`AttachmentBone` « Bip01 Head »), `:1324` (`AttachGlbToBone`) | Chaque palier `c_class` porte 2 codes : code1 = série H (tête, ex. `G1101` + skin `G1101H04`), code2 = tenue. Le natif habille vraisemblablement les deux — sinon pourquoi 2 codes — mais **non vérifié sur désasm** (« divergence probable ») | Résoudre code1 séparément : lookup `map` par code (sans exiger `#`), charger `char/model/<X11nn>` et l'attacher à « Bip01 Head » en complément de la tenue corps ; le casque équipé (porté) doit garder la priorité comme pour le corps | `VisTest` sur une classe à coiffe + capture comparée à `ref-shots/` natif ; log d'attache « Bip01 Head » présent | **Divergence probable non prouvée** : si le natif ne porte pas la coiffe, on ajoute un visuel absent de l'original. Risque de double-affichage tête si un casque est équipé — la priorité porté > classe doit être explicite |

## Recouvrements et dépendances entre fixes

- **Bug C (rankbuff) = D2 (skills-serveur)** : même ligne, même fix → F4.
- **Bug D** (pas de re-resolve JPAward sur `eden-add-class`/GM) : absorbé par
  F4 (le refresh fait partie de l'orchestration d'octroi).
- **D6** (`spell_level=80` pour le vecteur `+0x5c`) : absorbé par F4 (param
  de niveau sur `LearnSpell(s)Async` — moot tant que F4 n'existe pas).
- **F1 dépend de F5 et F4** : le balayage itère la bande `raceIdx×100`
  (lookup corrigé F5) et doit octroyer via le chemin complet d'init
  (F4) pour que les éveillées débloquées arrivent à lv80. Ordre : F4/F5
  d'abord, F1 ensuite.
- **F5 ne suffit pas sans F6** pour les sorts raciaux : la bonne ligne
  raciale contient le bon id, mais la branche mono-masque de
  `GrantableSpells` remet la variante Humaine en candidat et `Min()` la
  retient — les deux fixes forment un tout pour « sorts raciaux corrects ».
- **F8 inclut les costumes au sélecteur** (skins-audit H6 :
  `PreviewWorn` = conteneur 0) — même touch de fichiers.
- **G24** (`EDENFR-GAPS.md`) : après F2, les 12 éveillées présentes dans la
  table **US** chargée par le serveur reçoivent des bonus que la table FR
  (et donc l'UI client) n'a pas → effet réel sans affichage. Gap de données
  à décider (authentique FR vs compléter), pas un bug de code.
- **G25** : les sorts éveillées sans nom FR s'affichent en fallback id
  après F9 — reproduction de l'absence, rien à coder.
- **`BaseOf` dupliqué** (`ClassSkillTree.cs:100` / `ClassWindow.cs:1763`) :
  pas un bug actuel — refactor optionnel lors de F9, ne pas toucher sinon.
- **Non-fixes** : skins-audit H2 (`style=0` faute de 489) et H4
  (`Entry.Style` ≠ index garde-robe) sont des hypothèses à trancher par
  `--look-log`/`[outfit]` **avant** d'écrire du code ; les parseurs
  (`IniTable`, `BuildRows`, `LoadEquipMap`, accès par code) sont conformes.

## Écarts mineurs annexes (même famille, hors liste principale)

| Fix | Fichier:ligne | Écart | Changement minimal | Test |
|---|---|---|---|---|
| D5 | `Eden.Core/Game/Classes/LicenseService.cs:37-38` | Capacité certs `min(7, lv/10 + 2)` vs `lv/10 + 1` natif (le `+2` du message 0x325 est de l'affichage ; le fichier se contredit :127,:182,:204) | `+ 2` → `+ 1` | Perso lv10, 2 certs posés → 182 sur un 3ᵉ slot : natif refuse `0x325`, C# accepte aujourd'hui |
| D7 | `Eden.Zone/ZoneEntryFlow.Services.cs:183-184` | Le 489 émet une ligne lv0 pour une base absente de `player_classlist` (le natif ne liste que les possédées) et le 168 permet alors de la débloquer | Retirer l'union `AccessibleClassIds` des lignes non possédées, ou garder pour tolérance DB — cosmétique, à décider | Supprimer `class=3` d'un perso → natif : 489 sans 3, 168 refusé ; C# : 489 lv0, 168 accepté |
| D8 | `Eden.Core/Game/Classes/ClassSkillTable.cs:56` | `RequiredLevel >= 1` vs `CheckLevel` natif sans borne basse — un sort `req=0` de vecteur serait sauté | Aligner la borne basse si des `req=0` existent (inventaire non fait — preuve limitée) | Aucun sort `req=0` constaté : test à écrire quand l'inventaire le montrera |

## Ordre d'attaque

Ce qui se teste **sans serveur live** d'abord, le live en dernier — chaque
lot laisse un point de contrôle vert avant de passer au suivant.

1. **Étape 0 — trancher le symptôme** (aucun code) : ~~capture `--class-mock`~~
   **FAIT le 2026-11-03** — `artifacts/shots/20261103-154342-S001-classmock.png`
   montre le panneau rempli (H1 confirmée : le vide était l'artefact `--solo`).
   Reste : `ClassSkillTreeTests` + ouverture K en live (§ Préalable). Sortie :
   confirmation que le panneau live fonctionne → le reste = corrections
   ciblées, pas débogage à l'aveugle.
2. **Lot serveur pur — NetTests / Eden.Core.Tests, hors moteur et sans
   client graphique** (règle « un seul client graphique » non concernée) :
   - **F2** (one-liner + test `ClassId==59` sur le fichier réel) — le plus
     gros impact par ligne modifiée, immédiatement prouvable.
   - **F5** (helper `ClassFor` + test lookup 101) — prérequis de F1 et F6.
   - **F6** (parseur + filtres, tests `GrantableGeneralSpells`/`GrantableSpells`)
     — confirmer la colonne race **avant** d'écrire.
   - **D5** (one-liner + test capacité).
3. **Lot serveur DB** (`docker compose up -d postgres redis` +
   `start-native.sh`, comptes `admin`/`test123`) :
   - **F4** : `eden-add-class 21` → vérifications SQL (`player_classlist`
     80/80, `player_license` certs, `player_spells` spell_level=80) + 489.
     Livrer la **migration** des lignes éveillées existantes dans le même lot.
   - **F1** : perso aux prérequis → classe débloquée au gain suivant.
   - **F3** : canal heal flat — vérifier la contribution (test hors moteur
     possible dès le canal créé) puis l'effet en jeu.
4. **Lot client hors moteur** (NetTests, parallélisable avec le lot 3) :
   **F9** (`ClassSkillTreeTests` étendu), **F11** (test désérialiseur type 1),
   **F10** (test resolve-ko + log).
5. **Lot client VisTest** (harnais existant, sans serveur —
   `scripts/debug/VisTest.cs`) : **F7** (`--skin-classes --skin-style=8` +
   log `[dye]`), **F8** (`--look-log` + captures sélecteur/création),
   **F12** (attache head vs `ref-shots/`). Puis `tools/capture.sh` et
   comparaison `ref-shots/` pour le statut `VISUALLY_VALIDATED`.
6. **Validation live finale** : un seul client (`flock /tmp/godot.lock`),
   compte test multi-classé + non-Humain + éveillée — recouper stats,
   sorts, skins et panneau contre les trois TSV attendus.

Dépendances bloquantes à respecter : **colonne prérequis** (F1) et
**colonne race** (F6) confirmées sur les fichiers réels avant tout code ;
**F4 avant F1** (chemin d'octroi unifié) ; **F5 avant/après F6 en un lot**
pour les sorts raciaux. Les fixes client (F7-F12) sont indépendants des
fixes serveur et peuvent avancer en parallèle — un fichier = un éditeur à
la fois.

## Limites

- Plan déduit d'audits **statiques** : aucune ampleur in-game n'a été
  observée (personnages réels, races non-Humaines, éveillées existantes).
- Les positions de colonnes citées (`S_Class` +0x20/+0x24/+0x25 ; `S_Spell`
  ~col 14) sont les hypothèses fortes des audits, **à confirmer sur les
  fichiers** avant d'écrire les parseurs.
- Les numéros de ligne datent du 2026-11-03 ; le code est en mouvement —
  re-greper avant d'éditer, et régénérer `tools/graph/graph.sh` après les
  fixes.
- Les persos existants portent des données déjà corrompues par les bugs
  serveur (éveillées lv1, sorts raciaux étrangers appris) : chaque fix
  serveur livré doit décider de sa migration (ici signalée pour F4-F6).
