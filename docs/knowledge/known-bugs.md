# Index consolidé des bugs réels — audits de la nuit 2026-11-03

Statut : **verified** pour les constats de code (chaque entrée cite
`fichier:ligne` dans l'arbre au 2026-11-03 — re-vérifier avant d'éditer) —
**hypothesis** pour l'ampleur in-game (audits statiques, aucun build/test
exécuté). Document index uniquement : synthétise, ne refait pas les audits ;
le détail et les preuves natives sont dans les `source_doc`.

Chemins : client sous `eden_godot/godot_project/scripts/` ; serveur sous
`eden_server/server/src/` (`Eden.Core`, `Eden.Zone`, `Eden.Mission`).

## Sources

| Doc | Objet |
|---|---|
| `docs/knowledge/class-bug-fixplan.md` | Plan ordonné F1-F12 (skins + rankbuff + panneau skills) — le détail des fixes n'est pas dupliqué ici |
| `docs/knowledge/schema-drift-audit.md` | Drift schéma FR `c_*`/`C_*` vs oracles `S_*` US (enchant, achievement, collection) |
| `docs/knowledge/rankbuff-server-audit.md` | Class Ability Bonus serveur (bugs A-D) |
| `docs/knowledge/class-skills-server-audit.md` | Chaîne serveur acquisition/runtime classes (D1-D8) |
| `docs/knowledge/class-skills-audit.md` | Panneau Classe client |
| `docs/knowledge/class-skins-audit.md` + `equip-visuals-audit.md` | Skins de classe / visuels équipement client |
| `artifacts/corpus-inventory/mission-index-notes.md` | `MissionParser` TriggerGroup col25 vs donnée col24 |
| `artifacts/corpus-inventory/achievement-index-notes.md` | Indices `AchievementTable` écrits pour US V.4 |
| `artifacts/corpus-inventory/drop-index-notes.md` | col18 `reborn_monsters` = drapeau d'annonce, pas drop |
| `artifacts/corpus-inventory/fr-coverage-notes.md` + `misc-tables-notes.md` | `C_Mask.ini` header `V1.0,14,` inparseable |
| `docs/workflows/EDENFR-GAPS.md` | G22-G27 (gaps authentiques — section « pas un bug ») |

## Table des bugs

Ordonnée par sévérité : **CRITIQUE** (gameplay cassé pour tous ou la
majorité), **MAJEUR** (un sous-ensemble de joueurs/fonctionnalités),
**MINEUR** (cosmétique, robustesse, latent). Les ids entre parenthèses
renvoient aux numéros des audits sources ; `F#` = entrée du
`class-bug-fixplan.md`.

| id | bug | sévérité | fichier:ligne | symptôme | fix minimal | test discriminant | source_doc |
|---|---|---|---|---|---|---|---|
| KB-01 | **Enchant overlay : garde-fou schéma mort** — `row.Length == 60` jamais vrai (`IniTable` laisse la cellule vide du `\|` terminal → 61 cellules) | CRITIQUE | `Eden.Core/Data/Parsers/EnchantParser.cs:84-90` ; client `scripts/data/tables/EnchantTable.cs:75` (`s = row.Length == 60 ? -1 : 0` même défaut) ; `src/Eden.Core/Data/tables/EnchantTable.cs` (aucun garde-fou) ; doc fausse `EnchantService.cs:93-94` | 18 486 defs `c_enchant` + 1 399 `c_itemmallenchant` FR **remplacent** les defs US correctes avec tous les champs ≥6 décalés de −1 : `Enabled`, `TriggerType`, `TriggerParams`, `SecondaryBlock`, `SpellId1/2`, `ProcChance`, cmds 37-43, `Description`, flags 7/8, `MaxStack` → `Evaluate`/`GetProc`/`GetUseCommands`/`GetAttackCommands` décodent des args comme ids ; groupe elf « 1 » pollué ~18k ids. Client : `IsBuff`/`NoAbandon`/`NoReapply`/`PetGroup`/`MaxStack`/`Description` mal lus. Intacts : `Id`, `GroupCode`, `SpellRef`, `Level`, `Name`, scans bruts (montures OK — c'est pour ça que ça passe inaperçu) | Tester `row.Length == 61` dans les deux gardes (ou retirer la cellule vide terminale en amont de `BuildRows`) ; ajouter le garde dans la copie `src/Eden.Core` ; corriger la doc | `EnchantParser.Parse` sur `corpus/fr-full/data/db/c_enchant.ini` → le garde-fou se déclenche sur les 18 486 lignes (aujourd'hui : 1 seule, la ligne anomalie 46131) ; puis `GetUseCommands` d'un enchant à cmds connues (ex. 45747 → `2084`,`2047`) retourne les vrais ids | schema-drift-audit.md §3.1 |
| KB-02 | **Bug A — rankbuff : lookup par index de ligne au lieu du classId** (`d.Id` = champ 0 que le natif `Skip(1)` ; la clé est le champ 1) | CRITIQUE | `Eden.Zone/Program.cs:423` (`d.Id == row.ClassId`) ; commentaire faux `Eden.Core/Data/Parsers/ShopAndMiscParsers.cs:240` (« Id = classId ») | Classes 1-19 correctes ; tout le reste décalé : Messie(20) reçoit les bonus de 21, Juge(59) ceux de 57, classe 61 rien, bases 49-52 perdent leurs paliers 1-16, éveillées 39-44 reçoivent 20 paliers de classes de base → cumul inter-classes incohérent. Tests verts malgré le bug (`GameDataTests.cs:44-58` ne couvre que f0==f1) | Comparer `d.ClassId == row.ClassId` (une comparaison) ; corriger le commentaire du modèle. Aucun changement de parseur | `dotnet test` : `ClassRankBuffs.First(d => d.ClassId == 59).Tiers[0].EnchantId == 40911` sur le fichier réel | rankbuff-server-audit.md §4.A ; fixplan F2 |
| KB-03 | **D1 — aucun équivalent d'`AddClassFromLimit`** : les classes avancées/éveillées ne se débloquent jamais par le gameplay | CRITIQUE | champs prérequis non parsés `Eden.Core/Data/Parsers/ClassStatsParser.cs:135-199` ; appels manquants `Eden.Zone/ZoneEntryFlow.Combat.Rewards.cs:260-435` (`GrantClassPointAsync`), `:445+` (`GrantKillExpAsync`), `ZoneEntryFlow.EntryPipeline.Load.cs` (équiv. `PostInit`) | Natif : balayage des lignes `S_Class` de la race du perso à `PostInit`/`GainClassPoint`/`GainExp` → `AddClass` des classes dont +0x25 (niv. perso) et +0x20/+0x24 (masque + niv. classes requises) sont satisfaits. C# : `AccessibleClassIds` figé {1,2,3,4,5,19} ; seuls `MC AddClass`/GM débloquent → 489 figé | Parser les 3 champs prérequis ; balayage bande `(RaceId-1)*100` (helper de KB-06) → chemin d'octroi unifié de KB-04 ; appeler en fin de `GrantClassPointAsync`, `GrantKillExpAsync` et à l'entrée zone. **Prérequis : la correspondance colonnes `S_Class.ini` ↔ +0x20/+0x24/+0x25 n'est pas revérifiée** — aligner sur ≥ 2 classes connues avant d'activer | Perso remplissant les prérequis natifs d'une classe avancée (ex. 6) → la classe apparaît dans `player_classlist`/489 au prochain gain ou à la reconnexion | class-skills-server-audit.md §D1 ; fixplan F1. **Dépend de KB-06 et KB-04** |
| KB-04 | **D2 = Bug C — éveillées insérées lv1/aa1 au lieu de lv80/aa80** (sans sorts gatés 80, certs, AA ni JPAward) | CRITIQUE (périmètre éveillées) | `Eden.Core/Database/Repositories/ClassRepository.cs:86-102` (`VALUES …,1,0,0,1` pour toute classe) ; orchestration `Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:339-373` (`eden-add-class`), `ZoneEntryFlow.Admin.cs:325-334` (GM `add_class` : ni sorts, ni 489, ni licences) ; helpers morts `Eden.Core/Game/Classes/ClassProgressEntity.cs:36-71` | Natif `AddClass` : `classId != base` → `level=80`, `aa_point=80`, `GetLicense` des `lv/10` premiers certs, `SimpleGetSpell` +0x50 niv.1 et **+0x5c spell_level 80**, `RefreshJPAward`, 489. C# : lv1/aa1 + sorts `req≤1` seulement → l'éveillé monte 84 niveaux au lieu de 5 avant le 1er palier rankbuff | Distinguer éveillée via `GetBaseClassId` (lookup GameData côté service) → insert 80/80 ; certs paliers `lv/10` via `GrantLicenseAsync` ; param niveau sur `LearnSpell(s)Async` (`ClassRepository.cs:405,441`) pour +0x5c à 80 ; `RefreshJPAward` sur octrois directs ; 489 chemin GM. **Migration nécessaire** : les lignes déjà insérées à lv1 restent lv1 | `eden-add-class 21` → `SELECT level, aa_point FROM player_classlist WHERE class=21` = `80,80` (constaté `1,1`) ; `player_license` ≥ 8 certs 33xxx ; `player_spells` sorts +0x5c à `spell_level=80` | class-skills-server-audit.md §D2 ; rankbuff-server-audit.md §4.C ; fixplan F4. **Recouvrements : absorbe Bug D et D6** |
| KB-05 | **Bug B — cmd 2047 `HealPlus` flat ignorée** (mode 4 = soins prodigués flat, mode 5 = soins reçus flat) | MAJEUR (classes soignantes) | `Eden.Core/Game/Classes/LicenseService.cs:412-594` (`ApplyEnchantCommands`, pas de case 2047 ; commentaire :585-591 « dupliqué de 2038 » faux) ; `StatContribution` sans canal heal flat | 168/560 enchants de palier portent une 2047 (classes f1 : 4,6,11,12,13,15,17,27,32,42,44,46,50,54) → bonus « Soin » silencieusement perdu serveur, affiché au client sans effet | Ajouter 2 canaux flat à `StatContribution` + case 2047 modes 4/5 + consommation dans les formules `Heal`/`BeHeal` | Enchant 45747 (`2047|13`) → contribution heal-given flat +13 (canal absent aujourd'hui — à créer pour rendre le test exprimable) | rankbuff-server-audit.md §4.B ; fixplan F3 |
| KB-06 | **D3 — lignes raciales `S_Class` injoignables** : `RaceId*100+cls` avec repli `‖ c.Id==cls` matche toujours la ligne Humaine (index 0) | MAJEUR (toutes races non-Humaines) | 11 sites : `Eden.Zone/ZoneEntryFlow.Services.cs:332,631`, `ZoneEntryFlow.EntryPipeline.Load.cs:314`, `ZoneEntryFlow.Combat.Rewards.cs:371,746`, `ZoneEntryFlow.Combat.cs:386`, `ZoneEntryFlow.Closet.cs:33`, `ZoneEntryFlow.Mission.Commands.cs:353`, `Eden.Zone/ProfileStatEmitter.cs:63,97,163,230` | Formule native `(RaceId-1)*100 + classId` (déjà utilisée `Program.cs:449`, `ClassSkillTable.cs:116`). Résultat : Zumi/Anuran/Ursun/Halfkin jouent avec stats %, vecteur sorts +0x50 (expertises/crafts/raciaux), certificats, garde-robes, `Archetype`/`TalentIds` **Humains** — Humains corrects par accident | Helper unique `ClassFor(raceId, classId)` = `(raceId-1)*100 + classId` repli `cls` ; remplacer les 11 sites | Hors moteur : `Classes.FirstOrDefault(c => c.Id == 2*100+1 ‖ c.Id == 1)` → attendu `101`, constaté `1`. En jeu : guerrier Zumi apprend `50239` (pas `50201`) ; son 296 affiche les stats de la ligne 301 | class-skills-server-audit.md §D3 ; fixplan F5. **Ne suffit pas sans KB-07** pour les sorts raciaux ; sorts déjà appris faux restent en base |
| KB-07 | **D4 — flag race de `S_Spell` non parsé** : crafts/expertises raciaux octroyés à toutes les races | MAJEUR (tous persos — sorts étrangers appris) | `Eden.Core/Data/Parsers/SpellParser.cs:36-200` (champ ~col 14 / `SSpellData+0x2c` absent) ; `Eden.Core/Game/Classes/ClassSkillTable.cs:115-122` (`GrantableGeneralSpells` ne filtre la race que sur ids 50372-50421), `:73-85` (`GrantableSpells` dédup `GroupBy(code).Min()` → variante Humaine gagne) | Crafts raciaux `50202/50240/50273/50307/50339` et supports `50269-50371` (+50370) appris par **tout** personnage de la ligne ; expertises J504/J509 → variante Humaine retenue quelle que soit la race (`855` = tous bits = octroi toutes races, conforme) | Parser le champ race dans `SpellDef` (**confirmer la position exacte ~col14 par alignement complet des 45 colonnes**) ; `GrantableGeneralSpells` : filtrer par flag du perso ; `GrantableSpells` : dédup préférant la variante flaggée race du perso | Hors moteur : `GrantableGeneralSpells(1, 60, spells, raceId=2)` ne doit pas retourner `50202` (flag Humain). Zumi frais : `player_spells` contient `50240` **sans** `50202` | class-skills-server-audit.md §D4 ; fixplan F6 |
| KB-08 | **`AchievementTable` : indices écrits pour US `\|V.4\|15\|` lus sur FR `\|V.3\|13\|`** | MAJEUR (fenêtre succès + totaux 480) | `scripts/data/tables/AchievementTable.cs:80-90` ; copie `src/Eden.Core/Data/tables/AchievementTable.cs` | `GroupKey = c6*100+c7` alors que la clé native = **`c5*100+c6`** (`SetAchievement` @0x808cd70 → totaux « Conquête monstre »/« Succès de ville » du 480 sous de mauvaises clefs) ; `Point` lit **c8** (seuil, =999 partout) au lieu de **c7** ; titre/item décalés ; `ItemReward` lit c11 (membres) → 0 ; `TargetIds` (c13) et `Description` (c14) hors bornes → **toujours vides** → les 92 groupes légendaires perdent descriptions et sous-succès. Serveur `GameDataParsers.cs:227-251` correct sur S_ | Remapper V.3 : groupKey=c5×100+c6, Point=c7, seuil=c8, titre=c9, item=c10, membres=c11, desc=c12 | Parse `c_achievement.ini` réel → un succès-groupe (série 5001-5092) a `TargetIds` non vide et `Description` non vide ; `Point` d'un succès connu ∈ 1-10, jamais 999 | achievement-index-notes.md ; schema-drift-audit.md §3.2 |
| KB-09 | **`MissionParser.TriggerGroup` lit col25 au lieu de col24** | MAJEUR (269 quêtes satellites) | `Eden.Core/Data/Parsers/MissionParser.cs:112` (`GetInt(row, 25)`, commentaires :79-85) ; consommateur `Eden.Mission/Services/QuestEngine.Objectives.cs:727-733` (`x.TriggerGroup == groupId`) | Col25 = id dialogue (ex. 12958) ; le gid est en col24 (S_Mission oracle : mission 1295 → c24=426 = gid émis par la récompense `TriggerGroup 426` de 1293) → `FireTriggerGroupAsync` ne matche jamais → les quêtes satellites déclarées en col24 ne s'auto-accordent jamais (chaînes de déblocage groupé cassées) | `GetInt(row, 24)` + corriger les commentaires | `MissionParser.Parse` sur S_Mission → `Missions.First(m => m.Id == 1295).TriggerGroup == 426` (constaté 12958) | mission-index-notes.md |
| KB-10 | **Repli « 202 » : appelants sans `classId` → tenue Guerrier pour toute classe** | MAJEUR (visuel : sélecteur, création, distants, previews) | `scripts/screens/CharSelectScreen.cs:575-587` (`c.ClassId` dispo :469/478 ; `PreviewWorn` = conteneur 0 seul `scripts/session/EdenSession.cs:558-562` → **costumes invisibles au sélecteur**) ; `scripts/screens/CharCreateScreen.cs:355-372` (`_class` :105-109 ignoré) ; `scripts/world/EntityRegistry.cs:1386-1407` + `ApplyWornEquipment` :171-184 (`m.ClassId` du 263 parsé `scripts/protocol/CombatMessages.cs:171,212` mais jamais stocké) ; `scripts/screens/CharDressing.cs:44-50` ; `scripts/game/CharBodyCache.cs:29-30,88` (clé cache sans classe) | `BaseEquipIds={"202"}` (`CharVisual.cs:113`) = tenue Guerrier A01201 : sélecteur (perso sans armure `#`), création (toujours), distants et previews montrent un Guerrier quelle que soit la classe ; costumes (conteneurs 2-4) jamais au sélecteur | Ajouter `classId` (+`style`) à `CharDressing.Apply` ; stocker `m.ClassId` côté distant ; sélecteur `c.ClassId` ; création `_class` ; `classId` dans la clé `CharBodyCache` ; sélecteur : fusionner conteneurs 2-4 comme `VisualWorn` (`ZoneEquipMessages.cs:249-251`) | `--look-log` : ligne `[outfit] letter=X class=C style=S → …` montre le vrai classId ; capture sélecteur d'un mage non équipé → `#G205` (son `closet[0]`), pas `#G202` | class-skins-audit.md maillon 6 ; equip-visuals-audit.md maillon 6 ; fixplan F8 |
| KB-11 | **`RowForSuffix` = première variante : skin AS01 au lieu de ASnn** | MAJEUR (visuel : teintes de tenues partagées) | `scripts/game/CharModelSpec.cs:219-227` (première ligne `Mesh=="#X<suf>"` = plus petit DBn) ; repli `scripts/game/CharVisual.cs:432-435` | Mesh `#X2nn` partagé par plusieurs codes (`#B202` = A01201/02/05/09, skins AS01/02/05/09) affiché sans sa ligne dans `rows` (distant `style>0` non transmis, `RetintVisible` sans rows) → teinté variante AS01 au lieu de la sienne | Propager la ligne `EquipMesh` exacte (résolue par **code**) partout où le code est connu ; `RowForSuffix` reste un repli suffixe-seul | `VisTest --skin-classes --skin-style=8` + log `[dye] <mesh> row=… skin=…` → `skin=AS09` attendu, `AS01` = bug | class-skins-audit.md §3/H5 ; fixplan F7 |
| KB-12 | **Costumes d'arme en page jamais fusionnés** : `CostumeVisual.Merge` borne aux locs 1-5 | MAJEUR (fonctionnalité inerte) — divergence probable vs natif | `scripts/game/CostumeVisual.cs:32-47` | Locs arme/off-hand (≥6) jamais recouvertes → un « costume arme camouflée » (K3####/K90###) en page costume est strictement sans effet (indépendant du gap G22) | Étendre le merge aux locs arme **si le natif les couvre** (non désassemblé — confirmer avant) | Page costume avec item K3 → `--look-log` : le `worn` résolu doit contenir le loc arme | equip-visuals-audit.md maillon 7/H4 |
| KB-13 | **`Hidden` (paquet 282, `IsCanSee=false`) parsé mais jamais consommé** | MINEUR→MAJEUR (visuel/pvp : équipement masqué rendu) | `scripts/protocol/ZoneEquipMessages.cs:59-60,96-100` (`Hidden` rempli, jamais lu en aval) | Un slot marqué non visible rend quand même — le choix « masquer l'équipement » est ignoré | Consommer `Hidden` dans la résolution des visuels distants (sémantique native à confirmer) | Tramer un 282 avec `Hidden=[id]` → l'item doit disparaître du rendu | equip-visuals-audit.md maillon 7/H5 |
| KB-14 | **`Pay == 9` : 4 sorts de classe perdus du panneau** | MINEUR (affichage — le serveur octroie quand même) | `scripts/data/tables/ClassSkillTree.cs:133` | L'oracle serveur n'a pas de filtre `pay` : 14 sorts mono-masque `fam≠7` ont `pay=0` ; 10 rattrapés par le vecteur `c_class`, **4 perdus** : `50455` (cl. 18), `50685`+`50686` (cl. 20 Messie), `59459` (cl. 38) | Retirer le filtre `Pay == 9` de la branche mono-masque (conserver `fam != 7`, `Mask == bit`, dédup) | `dotnet test` `ClassSkillTreeTests` : `ClassSkills(18) ∋ 50455`, `ClassSkills(20) ∋ {50685,50686}`, `ClassSkills(38) ∋ 59459` | class-skills-audit.md §3.1 ; fixplan F9 |
| KB-15 | **`EnsureLoaded` : resolve KO → cache vide définitif et silencieux** | MINEUR (robustesse — seul mécanisme produisant « tout sauf skills » en live packagé) | `scripts/data/tables/ClassSkillTree.cs:57-69` (resolve null → dict vide caché pour la session, 0 log) ; absence de garde `scripts/hud/ClassWindow.cs:2096-2117` | Pack sans `raw_assets`, `EDEN_ALLOW_LOOSE=0` ou packs périmés → `c_spell`/`c_class`/`c_talent` non résolus → grilles vides silencieuses pendant que les paquets remplissent le reste | Ne pas mettre en cache un resolve null ; `GD.Print` des counts (modèle `[skill] C_Spell : …`) ; try/catch + log dans `RefreshSkills` (modèle `RefreshLicense` `ClassWindow.cs:1273-1277`) | Run packagé ou `EDEN_ALLOW_LOOSE=0` : log explicite du resolve KO ; après correction du pack, réouverture → grilles remplies | class-skills-audit.md §3.2 ; fixplan F10 |
| KB-16 | **471 type 1 (reset talents) ignoré → points affichés obsolètes** | MINEUR | `scripts/protocol/ZoneEntryDataMessages.cs:249-256` (`type==0` seulement) ; doublon `src/Eden.Core/Protocol/ZoneEntryDataMessages.cs` | Le serveur émet 471 type 1 lors d'un reset talents ; le client garde les anciens points (KPs et nœuds) jusqu'à reconnexion | Traiter `type==1` — **vérifier d'abord la sémantique émetteur (snapshot vs delta) dans `ZoneEntryFlow`** | Live : reset talents → l'arbre repasse à l'état serveur sans reconnexion | class-skills-audit.md §3.4 ; fixplan F11 |
| KB-17 | **Branche code1 morte : la coiffe de classe n'est jamais affichée** | MINEUR — divergence probable non prouvée | `scripts/game/CharModelSpec.cs:265-274` (`map.TryGetValue(e.Male)` filtré `StartsWith('#')` — code1 = grp 2 mesh externe → jamais retenu) ; pipeline d'attache existant `scripts/game/CharVisual.cs:1272-1277`, `:1324` | Chaque palier `c_class` porte 2 codes : code1 = série H tête (`G1101` + skin `G1101H04`), code2 = tenue. Seul code2 est affiché → coiffe de classe jamais visible | Résoudre code1 séparément (lookup `map` par code sans exiger `#`), charger `char/model/<X11nn>`, attacher « Bip01 Head » ; priorité au casque équipé. **Vérifier d'abord que le natif porte les 2 codes** | `VisTest` sur une classe à coiffe + capture comparée à `ref-shots/` natif | class-skins-audit.md maillon 2 ; fixplan F12 |
| KB-18 | **D5 — capacité certificats `level/10 + 2` au lieu de `level/10 + 1`** | MINEUR (un slot de trop par palier, lv1-59) | `Eden.Core/Game/Classes/LicenseService.cs:37-38` (`Min(7, level/10 + 2)` — auto-contradiction avec les commentaires :127,:182,:204) ; compteur client `scripts/hud/ClassWindow.cs:1451-1454` (même +2) | Natif `SetCurrentClassLicense` refuse quand `level/10 + 1 ≤ occupés` (le message 0x325 affiche +2, la règle est +1) → un certificat supplémentaire équipable à chaque palier | `Math.Min(7, level/10 + 1)` côté serveur ; aligner le compteur client | Perso lv10 avec 2 certs posés → 3ᵉ via 182 : natif refuse `0x325`, C# accepte | class-skills-server-audit.md §D5 |
| KB-19 | **D7 — le 489 liste des classes de base non possédées** (`level=0`) et le 168 accepte le switch | MINEUR (données historiques seulement) | `Eden.Zone/ZoneEntryFlow.Services.cs:183-184` (union `AccessibleClassIds`), `:300-307` | Une ligne `level=0, aa=1, lic=0` est émise pour une classe de base absente de `player_classlist` ; le natif n'émet que le vecteur possédé et refuse le switch | N'émettre que les classes possédées (ou refuser le 168 vers une non possédée) | Supprimer la ligne `class=3` d'un perso → natif : 489 sans classe 3, 168 vers 3 refusé ; C# : 489 avec `lv0`, 168 accepté | class-skills-server-audit.md §D7 |
| KB-20 | **`c_collection` : `Group` = 0 pour toutes les cartes en FR V.3** | MINEUR (agrégation archive) | `scripts/data/tables/CollectionTable.cs:69` (`GetInt(row, 8)`) | FR `\|V.3\|8\|` → 9 cellules (8 cols + trailing vide) → `Group=0` pour les ~3 000 cartes → `GroupPoints`/`ArchiveWindow` agrègent tout en groupe 0 (compteurs 1000/1001 survivent via col2). US V.4 ajoute col8 en fin — **le groupe n'existe peut-être pas en FR (gap, pas remapping) — à trancher avant fix** | Vérifier où vit le groupe en FR ou accepter l'absence ; ne pas lire col8 hors schéma V.3 | Parse `c_collection.ini` → `Cards.Any(c => c.Group != 0)` si le groupe existe en FR | schema-drift-audit.md §3.3 |
| KB-21 | **`SceneTable.DropTableId` : nom/commentaire obsolète** — col18 = drapeau d'annonce de spawn | MINEUR (doc/nom — aucun consommateur actuel) | `src/Eden.Core/Data/tables/SceneTable.cs:40-41` (propriété), `:444,461` (commentaire + parse) ; copie `scripts/data/tables/SceneTable.cs:40-41,473,490` | Col18 des `[reborn_monsters]` = flag d'annonce (`GetAllRebornMonsterInfo` @0x083688a0 ; col19 = index `t_textindex`) — déjà renommé `AnnounceFlag` côté serveur (`SceneParser.cs:37-42`). Le nom « DropTableId » induirait en erreur tout futur consommateur | Renommer `DropTableId` → `AnnounceFlag` + commentaire (aligner sur `SceneParser`) | `grep DropTableId` hors `SceneTable.cs` = 0 → renommage sans risque | drop-index-notes.md |
| KB-22 | **`C_Mask.ini` : header `V1.0,14,` rejeté par `IniTable.ParseLines`** | MINEUR/latent (aucun lecteur aujourd'hui) | `scripts/data/IniTable.cs` (`ParseLines` : ni `\|V.` ni `V.` ni format Count ne matche `V1.`) ; table `corpus/fr-full/data/db/C_Mask.ini` | 36 records de masques de visibilité de sous-mailles de tête → `IniTable` = 0 ligne si un lecteur est un jour ajouté. **Même défaut dans `S_Mask.ini` US** — anomalie native des données, pas corruption FR (G26) | Si la table devient nécessaire : étendre `ParseLines` au format `V<d>.<d>,N,` ou pré-filtrer ce fichier — pas avant | `IniTable.ParseFile("data/db/C_Mask.ini")` → 36 rows après support du header | fr-coverage-notes.md ; misc-tables-notes.md ; EDENFR-GAPS G26 |
| KB-23 | **`ItemLookup` du sélecteur charge `c_item` sans `c_itemmall`** | MINEUR (cosmétique — à corriger avec KB-10) | `scripts/screens/CharSelectScreen.cs:706-734` (table `_items` propre, c_item seul) | Les ids 40xxx boutique ne résolvent pas au sélecteur (costumes invisibles même quand `PreviewWorn` les connaîtrait) | Fusionner `c_itemmall` comme `InventoryWindow.cs:562-574` (`_items.TryAdd`) | Perso avec costume boutique équipé au sélecteur → id résolu | equip-visuals-audit.md maillon 1/H10 |
| KB-24 | **`DemoRemoteEquip` : ids de preuve obsolètes** | MINEUR (harnais démo) | `scripts/game/PlayerController.cs:1581-1623` | `608` absent de `c_item` EDENFR + `K20105` orphelin → l'armure du factice ne résout rien ; le log `[demo] remote tenue=#B230` n'arrive plus | Remplacer par des ids présents dans fr-full | Run `--solo` avec factice → log `[demo] remote tenue=…` montre une tenue résolue | equip-visuals-audit.md maillon 8 |

## Recouvrements et dépendances

- **Bug C (rankbuff §4.C) = D2 (skills-serveur §D2)** : même ligne, même
  fix → **KB-04**.
- **Bug D** (pas de re-resolve JPAward sur `eden-add-class`/GM
  `add_class`) : absorbé par **KB-04** (le refresh fait partie de
  l'orchestration d'octroi — impact nul tant que lv1, bug latent si lv80
  implémenté).
- **D6** (`spell_level=80` pour le vecteur `+0x5c`,
  `ClassRepository.cs:405,441`) : absorbé par **KB-04** (param de niveau
  sur `LearnSpell(s)Async`).
- **KB-03 (F1) dépend de KB-06 (F5) et KB-04 (F4)** : le balayage itère la
  bande `raceIdx×100` (lookup corrigé) et doit octroyer via le chemin
  complet d'init pour que les éveillées débloquées arrivent à lv80.
  Ordre : KB-04/KB-06 d'abord.
- **KB-06 ne suffit pas sans KB-07** pour les sorts raciaux : la bonne
  ligne raciale contient le bon id, mais la branche mono-masque de
  `GrantableSpells` remet la variante Humaine en candidat et `Min()` la
  retient — les deux fixes forment un tout.
- **KB-10 (F8) inclut les costumes au sélecteur** (skins-audit H6 :
  `PreviewWorn` = conteneur 0) — même touch de fichiers, et KB-23 est la
  moitié donnée du même écran.
- **G24** (`EDENFR-GAPS.md`) : après KB-02, les 12 éveillées présentes
  dans la table **US** chargée par le serveur reçoivent des bonus que la
  table FR (et donc l'UI client) n'a pas → effet réel sans affichage.
  Gap de données à décider (authentique FR vs compléter), **pas un bug
  de code**.
- **D8 (informationnel)** : filtre `GrantableSpells` `RequiredLevel >= 1`
  (`ClassSkillTable.cs:56`) vs `CheckLevel` natif sans borne basse (aucun
  `req=0` constaté dans les vecteurs étudiés) ; commentaire dispatch 168
  `ZoneEntryFlow.cs:920` (`[u8 type][u16 classId][u8]`) probablement
  périmé vs `payload[0]` lu à `Services.cs:287` ; `ClassChangeService`
  (`Eden.Core/Game/Services/`) = code mort (peut servir de point de
  départ à KB-03). Pas de ligne propre — à garder en tête au fix.
- **Dette de drift (pas de bug actuel)** : `BaseOf` dupliqué
  (`ClassSkillTree.cs:100` / `ClassWindow.cs:1763`) ; copies
  `src/Eden.Core/` vs `godot_project/scripts/` (`CharModelSpec.cs` Core
  sans `ClassCloset`/`ClassOutfitRow` ; `EnchantTable.cs` Core sans
  garde-fou — cf. KB-01).
- **Parseurs alignés US qui liraient mal le FR si repointés**
  (schema-drift §6) : `SpellParser` (cols ≥34), `ParseAchievements`
  (11-14), `CollectionParser` (col8) — corrects aujourd'hui sur `S_*`,
  cassés si `EDEN_REFORGED_DB` est élargi. `EnchantParser` est le seul
  déjà pointé → KB-01.

## « Pas un bug » — à ne pas corriger

- **Artefact `--solo`** : les trois captures « panneau Classe vide » du
  HANDOFF #11 sont des runs `tools/capture.sh` (`--map … --solo --shot`,
  `capture.sh:15`) sans session ni `--class-mock` → `RefreshSkills`
  retourne quand `sel ≤ 0` (`ClassWindow.cs:2101-2102`) — vide
  **structuré**, pas un bug. Avant tout fix client :
  `tools/capture.sh S001 classmock --hud-open=class -- --class-mock=1;1;1:30,2:20,8:5`
  (attendu : Reg + Class Skills remplis), `dotnet test tools/NetTests
  --filter ClassSkillTreeTests`, puis run live sur ≥ 3 classes dont une
  éveillée. Le seul vrai mécanisme live du symptôme = KB-15.
- **`style=0` faute de 489** → tenue palier 1 affichée : repli correct en
  absence de session (ne survient qu'en `--solo` ou avant le paquet) —
  pas un bug du code ; à traiter via KB-10 pour les appelants qui
  devraient transmettre `classId`/`style`.
- **Gaps EDENFR authentiques** (`docs/workflows/EDENFR-GAPS.md`) —
  absences du corpus FR à **reproduire**, jamais à combler par des
  bouche-trou : **G22** (193 codes `K#####` cités par `c_item`/
  `c_itemmall` absents de `4pequip.ini` → skip muet, repli tenue de
  classe/« 202 », jamais nu) ; **G23** (~64 icônes d'items sans fichier
  livré) ; **G24** (12 éveillées sans ligne `c_classrankbuff` FR — voir
  recouvrement KB-02) ; **G25** (85→409 sorts éveillées sans nom FR →
  fallback id à l'affichage) ; **G26** (`C_Mask.ini` — anomalie native
  identique dans `S_Mask` US, cf. KB-22 pour l'aspect parseur) ; **G27**
  (9 fichiers audio référencés absents → silence).
- **Anomalies de données tolérées** (schema-drift §4) : row
  `c_dropitem` 24230 (identique dans `S_DropItem` US — resynchro en
  place), `c_activity` row 118 (aucun lecteur), rows fantômes `c_talent`
  #735 / `c_equipset` #380 (skippées par `TryParse`), row `c_enchant`
  46131 à 60 cellules (anomalie propre — seule ligne que le garde-fou
  KB-01 actuel atteint).
- **Valeurs divergentes FR vs US dans `c_item`/`c_itemmall`** : même
  schéma `|V.11|78|` — différence de **donnée**, pas de drift.

## Limites

- Synthèse d'audits **statiques** (2026-11-03) : pas de `dotnet build`,
  `dotnet test`, ni session client/serveur — les `fichier:ligne` sont
  ceux de l'arbre à cette date ; re-vérifier avant d'éditer (le code
  bouge entre agents).
- KB-12, KB-13, KB-17 reposent sur des « divergences probables » (natif
  non désassemblé pour ce point précis) : confirmer le comportement
  oracle avant d'implémenter.
- L'impact live réel de KB-01 (quels champs mal lus deviennent des bugs
  visibles : tooltips d'enchant, annulation de buff, stacks) reste à
  confirmer en jeu.
