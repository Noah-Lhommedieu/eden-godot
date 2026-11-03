# Audit serveur C# — chaîne changement de classe + compétences

Statut : **verified** pour les constats de code (chaque affirmation cite
`fichier:ligne` côté C# et `fonction @0x<addr>` dans
`re/ghidra/{zone,mission}/decompiled.c` côté natif) — **hypothesis** pour
l'ampleur en jeu (jamais exécutée). Audit **statique, lecture seule** du
2026-11-03 : aucun build, aucun test, aucune session client/serveur ; seul ce
document est écrit. Complète `docs/knowledge/class-skills-audit.md` (côté
client) et le bug C de `docs/knowledge/rankbuff-server-audit.md` (côté
serveur).

> **Correctifs livrés 2026-11-03** (statuts détaillés dans
> `known-bugs.md` § « Statuts des correctifs ») : D1 → **KB-03 TESTED**
> (`ClassUnlockRules` + appels à `GrantClassPoint`/`GrantKillExp`/1er
> MigrationDone ; alignement des colonnes prérequis revérifié sur les 61
> lignes locales et les bandes raciales de `S_Class.ini` décodé) ;
> D2+D6 → **KB-04 TESTED** (éveillées lv80/aa80, certs, sorts +0x5c à
> 80, JPAward, migration) ; D3 → **KB-06 TESTED** (`ClassFor` bande
> `(race-1)*100` aux 11 sites) ; D4 → **KB-07 TESTED** (flag race col 14
> parsé, filtres `RaceAllows`) ; D5 → **KB-18 TESTED** (`lv/10+1`) ;
> D7 → **KB-19 IMPLEMENTED** (489 = possédées, 168 refusé sinon — à
> vérifier live) ; D8 = informationnel, non traité.

Données serveur : `eden_server/server_us/Data/db_decoded/S_Class.ini`
(305 lignes = 61 classes × 5 races, ids `index_race×100 + classe`, index 0 =
Humain…4 = Halfkin) et `S_Spell.ini` (V.8) ; données client de référence
`corpus/fr-full/data/db/` (EDENFR seul) ; inventaire attendu
`artifacts/corpus-inventory/class-skills-expected.tsv` (+ notes md).

## Verdict court

La moitié « runtime » de la chaîne est **solide et fidèle** : le burst de
changement de classe (168 → vitals/296/488/489/0x2c/283/509/286+316/510),
la liste 489 (format capture-vérifié), les licences par classe
(`lic_1..7` + `player_license.loc`), les barres par classe
(`shortcut_menu.class` + 510), le pool AA par classe
(`player_characters.aa_point` ↔ `player_classlist.aa_point`), le level-up de
classe (284 flag=1, AA, sorts, certificats tous les 10 niveaux, 509, 489,
JPAward) — tout ça est implémenté avec les preuves wire/désasm en
commentaires.

La moitié « acquisition » est en revanche **cassée par quatre défauts qui
se cumulent** :

1. **Aucun auto-déblocage** — `AddClassFromLimit` n'a pas d'équivalent :
   les classes avancées (6-20) et éveillées (21-60) n'arrivent jamais par
   le gameplay (attendu : check à chaque
   `PostInit`/`GainClassPoint`/`GainExp`).
2. **Éveillées à lv1 au lieu de lv80** — `AddClassAsync` insère
   `level=1, aa_point=1` quels que soient la classe et le chemin, sans
   les sorts/certificats/AA de l'initialisation éveillée native.
3. **Lignes raciales de `S_Class` injoignables** — `RaceId*100+cls` avec
   repli `|| id==cls` retourne toujours la ligne Humaine (index 0) : toutes
   les races jouent avec les stats %, sorts d'expertise, certificats et
   garde-robes **Humains**.
4. **La dimension race de `S_Spell` est ignorée** — le champ de flags race
   (`SSpellData+0x2c`, ~col 14 : valeurs 3/4/16/64/768 pour les 5 variantes
   J504) n'est pas parsé : les sorts communs sont indexés côté natif par
   `raceIdx×100+classe`, côté C# n'importe quel sort multi-masque passe pour
   toutes les races → crafts/expertises raciaux des 5 races octroyés à tout
   le monde, et le dédoublonnage par code retient toujours la variante
   Humaine (id min).

## 1. Attendu (preuves natives)

### `CPlayerFactory::AddClass` — zone @0x080fd070 (`decompiled.c` l.144249-144386)

- Si la classe existe déjà dans la table de hachage `char+0xad0/0xad8`
  (clés = classId u8) → no-op.
- `GameData+0xec` = `QueryClass(classId, (byte)char[0x4aa] >> 4)`
  (l.144281, rappelé l.144301) — la donnée de classe **de la race du
  perso** (formule `(nibble>>1)×100 + classId`, cf.
  `CGameData::QueryClass` et `GetCommonSpell` ci-dessous).
- Insertion : `SCharacterClass` neuf `{level(+0x18)=1, exp(+0)=0,
  point(+4)=0, aa_point(+8)=1, style(+0x19)=0}` → DBA
  `classlist = %hd 1 0 0 1 0` (l.144295).
- **Branche éveillée** : `GameData+0x1d8(classId, nibble)` (l.144297)
  rend l'id de la classe de base ; si `classId != base` :
  - `level = 0x50` (**80**) (l.144300) ;
  - copie du vecteur certificats `classData+0x44`, boucle
    `for i < level/10` → `GetLicense(char, certId)` (l.144313),
    puis `RemoveLicense(itemData+0x8c)` du tier remplacé (l.144322) ;
  - `SimpleGetSpell` sur le vecteur `+0x50` (4 ids) avec `level=classLv,
    spell_level=1` (l.144338) et sur `+0x5c` (15 ids) avec
    `spell_level=0x50` (**80**) (l.144352) ;
  - `RefreshJPAward(char)` (l.144359) ;
  - `aa_point = 0x50` (**80**) puis `classlist = %hhd 80 0 0 80 0`
    (l.144361-144366).
- Émet `CNC_CZ_ClientPlayerClassList` (paquet **489**) dans les deux cas
  (l.144368-144370). Le symétrique `RemoveClass` @0x080fecb0 (l.145237+)
  persiste `classlist = %hd 0 0 0 0 0` et émet aussi un 489 — le 489 est
  donc **le** canal d'annonce add/remove/liste des classes.

### `CPlayerFactory::AddClassFromLimit` — zone @0x080fd440 (`decompiled.c` l.144387-144560)

- `raceIdx = ((byte)char[0x4aa] >> 4) >> 1` (0-4) ; itère la map
  `CClassData` sur les clés `[raceIdx×100, (raceIdx+1)×100)` — **toutes
  les classes de la race du perso**.
- Pour chaque classe non possédée : `classData+0x25` = niveau personnage
  requis (0 → classe jamais auto-débloquée) ; `classData+0x20` = masque de
  classes prérequises, `classData+0x24` = leur niveau requis — pour chaque
  bit du masque la classe doit être possédée **et** de niveau ≥ +0x24 ;
  sinon `goto next`.
- Éligible → `AddClass(char, classId)` — y compris l'initialisation
  éveillée complète ci-dessus.
- **Appelants** (preuves dans `decompiled.c`) : `CCharacter::PostInit`
  @0x08410230 (l.814703 — à chaque connexion), `CCharacter::GainClassPoint`
  @0x0841c140 (l.822808 — chaque gain d'exp de classe),
  `CCharacter::GainExp` @0x0841cb80 (l.823291 — chaque gain d'exp perso).
  C'est le déblocage **passif** : pas de quête ni de NPC, les classes
  arrivent d'elles-mêmes dans le 489 quand les prérequis tombent.

### `CPlayerFactory::ChangeClass` — zone @0x081048c0 (`decompiled.c` l.149097-149951)

- Refuse si la classe n'est pas dans la table `+0xad8`
  (log `0x3c1` l.~149504) — **aucun auto-AddClass** : la possession est un
  prérequis dur (l'octroi des classes de base à la création et des autres
  via AddClassFromLimit/quêtes rend le cas impossible en jeu normal).
- Bascule du pool AA : le `char+0xa1c` courant est rangé dans
  `SCharacterClass+0xc` de l'ancienne classe, puis rechargé depuis celle
  de la nouvelle (commentaire C# correspondant, cf. §2).
- Réécrit `player_license.loc` depuis `lic_1..7` de la nouvelle ligne
  classlist (`license = %hd %hhd`, l.~149504) puis `InvokeSpellStones`.
- Sauvegarde `SavePlayerCharacter`, émet 488 `ClientPlayerClosets`,
  **489**, plusieurs `ClientUpdateAttr` (classe active `0x2c` incluse),
  283 `ClientModelModify` mask `0x5e00`, 509 `ClientPlayerLicense`
  (copie du vecteur `+0xaf0`).
- Appelle `CSpellMasterFactory::GetCommonSpell(char, newClass, nibble)`
  puis `GetClassSpell(char, newClass)` (l.149924-149925) — les sorts de la
  **nouvelle** classe sont octroyés au switch, pas seulement annoncés.

### Spellmasters — `CSpellMasterFactory` zone

- `GetClassSpell` @0x08157230 : itère `classData(+0x32c du perso =
  classe active) -> vecteurs +0x50/+0x54 et +0x5c/+0x60`
  (l.217298-217323) → `SimpleGetSpell(id, char+0x47e = niveau de
  classe, 1)` (l.217310,217323).
- `GetCommonSpell` @0x08157320 : clé `(param_3/2)×100 + param_2`
  (l.217359 — `param_3` = nibble race/genre → raceIdx 0-4) dans la map
  `CZoneServer::Ins+0x1f8` → `SimpleGetSpell(id, char+0x470 = niveau
  perso, 1)` (l.217383) pour chaque id du vecteur. Sortie prématurée si
  `classId%100 == 20` — la classe 20 (Mimic) est exclue des sorts
  communs (l.217356-217358).
- `CSpellDBO::LoadAllCommonSpell` @0x83732b0 (`decompiled.c` l.733978+) :
  construit cette map depuis S_Spell — `SSpellData+0x2c` = **flags race**
  (bit0→base 0, bit2→100, bit4→200, bit6→300, bit8→400) et `+0x50` =
  masque de classes u64 ; pour chaque bit de classe posé et chaque flag
  race, clé `raceIdx×100 + classId` ; **masques mono-classe exclus**
  (condition `mask != 1<<bit`).
- `SimpleGetSpell` @0x08156d60 : `CheckLevel` (`SSpellData+0x20 ≤ niveau
  donné`), insert `player_spells(spell_level = param_4)` +
  `CNC_CZ_ClientGetSpell` (**286**) + log `0x5f4` → nom localisé (**316**).
  `param_4` = niveau de spellmaster à inscrire (1 en usage courant,
  **0x50** pour le vecteur +0x5c des éveillées).
- `SSpellData::CheckLevel` @0x084edfe0 : `required ≤ donné` — borne
  basse uniquement (un sort `req=0` est toujours octroyé).

### Certificats / licences — zone

- `GainClassPoint` @0x0841c140 : level-up de classe → 284 variante classe
  (u8=1), `GainAAPoint` (+1 par niveau sur `char+0xa1c`, cap `0x78`=120 —
  @0x0840c7f0, émet `ClientUpdateAttr` attr `0x31`), puis pour chaque
  multiple de 10 franchi `GetLicense(certIds[lv/10 - 1])`
  (`CClassData+0x44`).
- `GetLicense` @0x080fce80 : skip si déjà possédée, push dans le vecteur
  `+0xaf0`, DBA `license = %hd %hhd` (loc=0), émet **509**
  `ClientPlayerLicense` à une entrée — notification par octroi.
- `SetCurrentClassLicense` @0x82714f0 (NCID 182, classe active) : passe de
  conflit slot→loc0, puis `if (level/10 + 1 <= occupied) → refus 0x325`
  (l.~486634 : message affiché `level/10+2` — la **capacité réelle est
  `level/10 + 1`**, l'affichage ajoute un cran) ; `ReplaceSpellStones` à
  chaque retrait/pose ; écho 509 des licences modifiées.
- `SetOtherClassLicense` @0x82717f0 : réécrit `lic_1..7` de la ligne
  classlist d'une **autre** classe (layout par classe indépendant) → 489.
- `CNE_CZ_ZoneServerPlayerLicense::Execute` @0x8271da0 : dispatch 182 —
  `classId == active ? SetCurrent : SetOther`.
- NCID 181 → 509 complet ; NCID 183 → `InvokeSpellStonesSet` @0x8418aa0
  (combo par moitié de barre).

### Paquets

| NCID | Rôle |
|---|---|
| 166 `QueryClassList` | → **489** `ClientPlayerClassList` : `[u8][u8][u16 n]` puis par classe `classId, level, style, exp, point, aa_point, lic_1..7` — la classe active n'est **pas** flaguée dans le 489 (capture : flag = aa_point) ; elle passe par attr `0x2c` (280) + 283. |
| 168 `ChangeClass` | → burst ci-dessus (39 paquets capturés). |
| 181 `QueryLicense` | → **509** `{s16 itemId, u8 loc}n`. |
| 182 `PlayerLicense` | pose/retrait certs (classe active ou autre). |
| 183 `QueryLicenseSet` | combos de certificats. |
| 284 | level-up, flag 1 = variante classe. |
| 286/316 | acquisition de sort + nom localisé (0x5f4). |
| 510 `ClientChangeClassShortcut` | pages de barres de la classe active. |

## 2. Constaté — implémentation C#

| Étape | Emplacement | Constat |
|---|---|---|
| Création perso | `Eden.Core/Database/Repositories/CharacterRepository.cs:206-216` | `player_classlist` {1,2,3,4,5,19} lv1/aa1 + 23 spellmasters initiaux — capture native reproduite ✓ |
| Tables | `Eden.Core/Game/Services/GameDataService.cs:259` + `Eden.Core/Data/Parsers/ClassStatsParser.cs:125-199` | `S_Class.ini` parsé en 305 `ClassDefinition` (file order → ids 1-61 puis 101-461), `RaceIndex => Id/100`, `CertificateIds[8]` (+0x44), `ClassSpellIds` (+0x50/+0x5c), `TalentIds`, `Archetype`, `LevelProgression` ✓ ; **aucun** champ `RequiredLevel`/mask de déblocage (+0x20/+0x24/+0x25) parsé |
| Dispatch | `Eden.Zone/ZoneEntryFlow.cs:911,920,924,928,931` | 166→`HandleQueryClassListAsync`, 168→`HandleChangeClassAsync`, 181→`HandleQueryLicenseAsync`, 182→`HandlePlayerLicenseAsync`, 183→`HandleQueryLicenseSetAsync` ✓ |
| 489 | `Eden.Zone/ZoneEntryFlow.Services.cs:167-245` | Wire capture-vérifié `[u8 id][u8 lv][u8 style][i32 exp][i32 point][u8 aa][7×u16 lic]` ; `AccessibleClassIds={1,2,3,4,5,19}` ∪ possédées ; +592 +471 +mods talents dans la foulée ✓ |
| Changement | `Services.cs:283-362` + burst `SendClassChangeBurstAsync:422-483` | owned-ou-accessible → `AddClassAsync` si base non possédée → `ChangeClassAsync` → `SwapAaPoolOnClassChangeAsync` → `SyncLicensesOnClassChangeAsync` → octrois `GrantableSpells(newClass, newLevel)` + `GrantableGeneralSpells(newClass, charLv)` → vitals → 296/492/493 → 488 → 489 → attr `0x2c` → 283 → attrs 6/`0x3ef`/`0x31` → 509 → 286/316 → 510. Ordre = capture native 17:22:35 ✓ |
| Octroi entrée | `ZoneEntryFlow.EntryPipeline.Load.cs:303-339` | `EnsureEntrySpellsAsync` : `GrantableSpells(active, classLv)` ∪ `GrantableGeneralSpells(∀ possédée, charLv)`, batch `LearnSpellsAsync` → 311 ✓ |
| Octroi level-up | `ZoneEntryFlow.Combat.Rewards.cs:340-435` | 284 flag=1, `AddAaPointAsync(lv-startLv)` + attr `0x31`, `GrantableSpells` du nouveau palier → 286/316, boucle `m%10==0` → `GrantLicenseAsync(certs[m/10-1])` + 509 par cert, 489, `ProfileStatEmitter.SendForCharacterAsync` (JPAward) ✓ |
| Modèle sorts | `Eden.Core/Game/Classes/ClassSkillTable.cs:45-153` | `GrantableSpells` = vec `classDef` ∪ mono-mask, `req ∈ [1,classLv]`, dédup par code → min id ; `GrantableGeneralSpells` = multi-mask ∧ bit, `req ≤ charLv`, filtre racial id-range 50372-50421 (`raceId-1`) ; `CanCast` mask |
| Persistance | `Eden.Core/Database/Repositories/ClassRepository.cs` | `AddClassAsync:86-102` (lv1/aa1), `ChangeClassAsync:104-146` (+ miroir `class_lv`/`class_point`), `AddAaPointAsync:229-248` (cap 120 + miroir ligne active), `TrySpendAaPointAsync:255-280` / `TrySpendCurrentAaPointAsync:287-307` (pool autre classe / courant), `SwapAaPoolOnClassChangeAsync:316-338` (ancien→classlist, nouveau→characters), `LearnSpell(s)Async:380-444` (insert lv1 / level++ si connu), `SetClassLicensesAsync:462-479` (`lic_1..7`), `GetClassesByCharacterIdAsync:17-38` (joint `style`+lics) |
| Licences | `Eden.Core/Game/Classes/LicenseService.cs` | `CapacityFor = min(7, level/10 + 2)` (:37-38), `SetCurrentClassLicenseAsync:129-198` (conflits→loc0, `occupied ≥ capacity` → 0x325), `ValidateOtherClassLayout:206-230`, `GrantLicenseAsync:311-333` (dedup, loc=0, `LicenseReplaceId` → ReplaceSpellStones + delete), `LicenseRepository` player_license |
| Bascule licences | `Eden.Zone/ZoneEntryFlow.License.cs:147-192` | Sauve `lic_1..7` de l'ancienne classe, recharge le layout de la nouvelle → `SetLocAsync` par licence → `LoadAsync` (enchants) ✓ |
| Raccourcis | `SocialRepositories.cs:719-790` + `Services.cs:682-707` + `PlayerHandlers.cs:119-200` | `shortcut_menu(class,page,index1..10)` : upsert sur classe active, `GetClassShortcutPagesAsync` → 510 pages contiguës, 352 à la demande, Field3≠0 = flush sans réponse ✓ |
| Commandes | `Mission.Commands.cs:339-373` (`eden-add-class`) ; `Admin.cs:325-334` (`add_class`/`ac`) ; `QuestEngine.Objectives.cs:668-696` (mission `MC AddClass` → `eden-add-class`) | voir divergences |
| Stats | `Eden.Zone/ProfileStatEmitter.cs:63,97,163,230` ; `Combat.cs:386` | lookups `raceId*100+cls` (voir D3) |
| Classe-change service | `Eden.Core/Game/Services/ClassChangeService.cs` | **aucun appelant** (code mort) — les checks RequiredLevel/RequiredClass/RequiredQuest (TODO :62-66) ne s'exécutent nulle part |

## 3. Divergences « attendu vs constaté », triées par impact

### D1 — CRITIQUE : aucun équivalent d'`AddClassFromLimit` — les classes ne se débloquent jamais par le gameplay

**Attendu** : `CPlayerFactory::AddClassFromLimit` @0x080fd440 est appelé à
chaque connexion (`PostInit` l.814703), chaque gain d'exp de classe
(`GainClassPoint` l.822808) et chaque gain d'exp perso (`GainExp`
l.823291). Il itère les lignes `S_Class` **de la race du perso** et appelle
`AddClass` pour toute classe non possédée dont `classData+0x25` (niveau
perso requis) et `+0x20`/`+0x24` (masque + niveau de classes prérequises)
sont satisfaits. Le 489 s'enrichit alors tout seul — c'est le déblocage
passif de toutes les classes avancées (et il retombe sur `AddClass`, donc
avec l'init éveillée pour les classes éveillées si jamais leurs champs le
permettent).

**Constaté** : aucune occurrence d'un tel balayage — `grep AddClass` ne
retourne que `CharacterRepository.cs:207` (création), `Services.cs:303`
(classe de base au switch), `Mission.Commands.cs:349` (`eden-add-class`),
`Admin.cs:329` (GM). Rien dans `GrantClassPointAsync`
(`Combat.Rewards.cs:260-435`), rien dans `GrantKillExpAsync`
(`Rewards.cs:445+`), rien dans le pipeline d'entrée. Les champs de
prérequis (+0x20/+0x24/+0x25) ne sont d'ailleurs **pas parsés** par
`ClassStatsParser` (ClassStatsParser.cs:135-199 — seuls `RequirementText`
et `DisplayIndex` existent). Seul `ClassChangeService` (mort, sans
appelants) contient des checks.

Conséquence : un perso qui remplit les conditions natives (niveau perso +
niveau des classes prérequises) ne verra **jamais** les classes avancées
(6-20) ni éveillées (21-60) — elles n'apparaissent ni dans le 489 ni
comme cibles du 168 (`AccessibleClassIds` est limité à {1,2,3,4,5,19},
`Services.cs:155,292`). Le seul canal restant = récompenses de quête
`MC AddClass` / GM.

**Test qui tranche** : sur un perso test, monter la classe/classe-mère aux
prérequis d'une classe avancée (ex. classe 6 Barde, `c_class.ini`) selon
les champs de déblocage de sa ligne `S_Class` ; natif : la classe apparaît
au prochain gain/à la reconnexion dans le 489 ; C# : le 489 reste
inchangé indéfiniment (vérifier `player_classlist`).

### D2 — CRITIQUE : classes éveillées insérées lv1/aa1 au lieu de lv80/aa80, sans sorts, certificats ni JPAward

**Attendu** (AddClass @0x080fd070, l.144300-144366) : `classId != base` →
`level=0x50`, `aa_point=0x50`, `classlist=<id> 80 0 0 80 0` ; `GetLicense`
sur les `level/10` premiers certificats du vecteur `+0x44` (8 items 33xxx)
avec `RemoveLicense` du tier remplacé ; `SimpleGetSpell` sur `+0x50`
(spell_level 1) et `+0x5c` (**spell_level 80**) ; `RefreshJPAward` ; 489.

**Constaté** : `ClassRepository.AddClassAsync`
(`ClassRepository.cs:86-102`) écrit `VALUES (…, 1, 0, 0, 1)` pour **toute**
classe, aucun code ne distingue éveillé/base (`IsAwakened`/`GetBaseClassId`
de `ClassProgressEntity.cs:36-71` ne sont appelés nulle part — seul un
littéral `21..60` sert au taux d'exp, `Rewards.cs:464`).

| Chemin | Comportement constaté |
|---|---|
| Quête `MC AddClass` → `eden-add-class` (`Mission.Commands.cs:339-373`) | `AddClassAsync` lv1/aa1 + `GrantableSpells(cls, 1, …)` = uniquement les sorts `req≤1` + 489. Aucun certificat, aucun AA, aucun JPAward. |
| GM `add_class`/`ac` (`Admin.cs:325-334`) | `AddClassAsync` seul : ni sorts, ni 489, ni licences. |
| Base non possédée au switch (`Services.cs:300-307`) | `AddClassAsync` lv1 — **correct** pour les classes de base (natif identique : branche non-éveillée lv1/aa1). |

Effets combinés : une classe éveillée entre à lv1 (le natif la fait entrer
à 80 avec les 8 paliers de certificats, les sorts des deux vecteurs gatés à
80, et un pool AA de 80 pour l'arbre de talents — les talents éveillés
exigent typiquement des dizaines de points cumulés) ; elle doit ensuite
monter 84 niveaux de classe au lieu de 5 avant le premier palier rankbuff
(déjà noté : `rankbuff-server-audit.md` §4.C pour l'aspect rankbuff — ici
toute la chaîne sorts/certs/AA est en cause).

**Test qui tranche** : `eden-add-class 21` (Berserker) sur un perso test →
`SELECT level, aa_point FROM player_classlist WHERE class=21` : constaté
`1,1` ; attendu `80,80`. Et `player_license`/`player_spells` : attendu ≥8
certificats `lic_…33xxx` + les spellmasters du vecteur S_Class au
`spell_level` 80 pour la branche +0x5c ; constaté : seulement les sorts
`req≤1`.

### D3 — MAJEUR : `RaceId * 100 + cls` — les lignes raciales de `S_Class` sont injoignables

**Attendu** : la donnée de classe du perso est la ligne `index_race×100 +
classId` où `index_race = RaceId - 1` (wire 1-5 → 0-4 ; formule native
`(nibble>>1)×100 + classId` — `QueryClass` via `GameData+0xec` dans
`AddClass` l.144281/144301, `GetCommonSpell` l.217359). Preuve interne au
code C# : le même serveur calcule `(chr.RaceId - 1) * 100 + chr.ClassId`
pour l'arbre AA (`Eden.Zone/Program.cs:449`) et `raceId - 1` pour les
raciaux (`ClassSkillTable.cs:116`) — la convention est connue et utilisée
ailleurs.

**Constaté** : 11 sites écrivent `RaceId * 100 + cls` avec repli
`|| c.Id == cls` :

- `Services.cs:332` (octroi sorts au switch), `Services.cs:631`
  (émission 286/316) ;
- `EntryPipeline.Load.cs:314` (sorts à l'entrée) ;
- `Combat.Rewards.cs:371` (sorts+certs au level-up de classe) et `:746`
  (archétype Mana Infusion) ;
- `Combat.cs:386` (snapshot combat) ;
- `Closet.cs:33` (progression garde-robe) ;
- `Mission.Commands.cs:353` (`eden-add-class`) ;
- `ProfileStatEmitter.cs:63,97,163,230` (296/492/493 — stats de base,
  vitales, snapshot, bloc final).

`S_Class.ini` trie les lignes 1-61 **avant** 101-461
(`ParseClassDefinitions` itère `table.Rows` dans l'ordre,
`ClassStatsParser.cs:130`) : `c.Id == cls` matche donc **toujours** la
ligne index 0 (Humain) avant que la branche race ne puisse matcher — le
terme `RaceId*100` est du code mort. Résultat : **toutes les races jouent
avec la ligne Humaine**. Pour un Humain le résultat est correct par
accident ; pour Zumi/Anuran/Ursun/Halfkin, sont faux :

- les **pourcentages de stats** (`StatPercents`, 9 cellules) — ex.
  guerrier Ursun attendu `…,131,65,128,112,…,103` (ligne 301), constaté
  `…,123,69,125,114,…,104` (ligne 1 ; cellules 2-5 et 8 du bloc)
  → HP/MP, stats de combat et fiche 296 tous faussés ;
- le **vecteur de sorts** `+0x50` (expertises/crafts/raciaux : ligne 1 =
  `50201,50202,50231,50269` ; 101 = `50239,50240,50231,50270` ; 201 =
  `50272,50273,50231,50271` ; 301 = `50306,50307,50231,50370` ; 401 =
  `50338,50339,50231,50371`) ;
- les **certificats** `+0x44` (identiques sur la classe 1 étudiée, mais
  non garantis partout) ;
- `Archetype`, `LevelProgression` (garde-robes), `TalentIds`.

**Test qui tranche** : hors moteur,
`_services.GameData.Classes.FirstOrDefault(c => c.Id == 2*100+1 || c.Id == 1)`
retourne `Id == 1` — attendu `101`. En jeu : un guerrier Zumi doit avoir
`50239` dans `player_spells` (expertise arme J504 variante Zumi) — il
reçoit `50201` ; et son 296 affiche les stats d'un Humain.

### D4 — MAJEUR : le flag race de `S_Spell` n'est pas parsé — crafts/expertises raciaux des 5 races octroyés à tous

**Attendu** : `LoadAllCommonSpell` @0x83732b0 indexe chaque spellmaster
sous `raceIdx×100 + bit_classe` en combinant **deux** champs :
`SSpellData+0x50` (masque de classes u64, col 23) **et** `SSpellData+0x2c`
(flags race : bit `1<<2×raceIdx` → base `raceIdx×100` ; bits testés
`&1,&4,&0x10,&0x40,&0x100`). `GetCommonSpell` ne voit que la clé de la race
du perso. Sur le fichier chargé, le champ race (~col 14) vaut **3, 4, 16,
64, 768** pour les variantes J504 Humain/Zumi/Anuran/Ursun/Halfkin
(`50201/50239/50272/50306/50338` — bits 1/4/16/64/256 reconnus, bits 2 et
512 inertes pour la clé).

**Constaté** :

- `SpellParser.SpellDef` n'a **aucun** champ race (SpellParser.cs:36-200 —
  colonnes 17/18/19/22/23/… parsées, pas ~col 14) ;
- `GrantableGeneralSpells` (ClassSkillTable.cs:115-122) ne filtre la race
  que sur la plage d'ids **50372-50421** (`IsRacialSpell`). Tout sort
  racialement flagué hors de cette plage passe : crafts raciaux
  `50202/50240/50273/50307/50339` (J505/J579/J597/J619/J622, mask
  `0x300001` = bits {1,21,22}) et supports raciaux
  `50269/50270/50271/50370/50371` (J594-J596/J651/J652, mask `0x3ffff` =
  classes 1-18) — chacun avec un **code distinct** → aucun dédoublonnage
  → **les 5 versions sont apprises par tout personnage de la ligne** ;
- `GrantableSpells` (ClassSkillTable.cs:73-85) : les 5 J504 sont
  `mask=1` (mono-guerrier) → tous candidats → `GroupBy(code).Min()` →
  **50201 (Humain) gagne quelle que soit la race**, et ce même si D3
  était corrigé (le bon id serait dans le vecteur de la ligne raciale
  mais le mono-masque le re-balance en candidat). Même motif sur chaque
  expertise de classe : J509 chevalier `50203/50241/…` `mask=64`,
  col14 `3/4/16/64/768` → variante Humaine toujours retenue.
- Contre-exemple correct : `50231` (J576, col14 `855` = tous les bits
  race posés, présent dans les 5 lignes) — l'octroyer à toutes les races
  **est** le comportement natif ; seuls les ids à flag mono-race sont en
  cause.

Conséquence chiffrée : un guerrier non-Humain apprend le sort d'expertise
Humain (`50201`, pas le sien) **plus** les 5 crafts raciaux (`50202, 50240,
50273, 50307, 50339`) **plus** les 5 supports raciaux (`50269-50371` avec
50370) — au lieu de son trio `50239/50240/50270`. Même pollution chez le
Humain (il reçoit les 4 crafts et supports des autres races).

**Test qui tranche** : sur un Zumi frais, `SELECT spellmaster_id FROM
player_spells` doit contenir `50240` **sans** `50202` ; constaté : la liste
contient les cinq ids de crafts. Hors moteur :
`GrantableGeneralSpells(1, 60, spells, 2)` retourne `50202` (J505, flag
race=3/Humain) — attendu absent pour un Zumi.

### D5 — MINEUR : capacité de certificats `level/10 + 2` au lieu de `level/10 + 1`

**Attendu** : `SetCurrentClassLicense` @0x82714f0 refuse quand
`level/10 + 1 ≤ occupés` → capacité max **`level/10 + 1`** (le message
0x325 affiche `level/10 + 2` — c'est l'affichage qui ajoute un cran, pas la
règle).

**Constaté** : `LicenseService.CapacityFor` =
`Math.Min(7, level/10 + 2)` (`LicenseService.cs:37-38`) — un slot de trop
à chaque palier (lv10 : 3 au lieu de 2). Les commentaires du fichier
lui-même disent `lv/10+1` (:127,:182,:204) — le code est en
auto-contradiction. Impact : un certificat supplémentaire équipable
(enchant + stats) à chaque niveau 1-59.

**Test qui tranche** : perso lv10 avec 2 certificats posés, tenter un
3ᵉ slot via 182 → natif : refus `0x325` ; C# : accepté.

### D6 — MINEUR : `spell_level` des sorts éveillés +0x5c = 80, pas 1

**Attendu** : `AddClass` octroie les sorts du vecteur `+0x5c` avec
`SimpleGetSpell(…, spell_level=0x50)` — `player_spells.spell_level=80`
pour cette branche (decompiled.c l.144352).

**Constaté** : `LearnSpellAsync`/`LearnSpellsAsync` insèrent toujours
`spell_level=1` (`ClassRepository.cs:405,441`). Moot tant que D2 n'est pas
corrigé (les éveillées n'atteignent jamais ces sorts), à retenir pour
l'implémentation. Cas adjacent : `GrantableSpells` exige
`RequiredLevel >= 1` (ClassSkillTable.cs:56) alors que `CheckLevel` natif
n'a pas de borne basse (`+0x20 ≤ niveau`) — un sort de vecteur à `req=0`
serait sauté par le C# (aucun constaté dans les vecteurs étudiés — preuve
limitée).

**Test qui tranche** : après correction D2, `player_spells.spell_level`
d'un sort +0x5c éveillé doit être 80 ; sinon test unitaire simulant
l'init → attendu 80.

### D7 — MINEUR / informationnel : 489 liste les classes de base non possédées

**Attendu** : le 489 natif ne véhicule que le vecteur `SCharacterClass`
possédé (la capture « toutes accessibles » = les 6 lignes insérées à la
création — toutes possédées).

**Constaté** : `Services.cs:183-184` union `AccessibleClassIds` — une
ligne `level=0, aa=1, lic=0` est émise pour une classe de base absente de
`player_classlist` (impossible en DB saine, possible sur données
historiques). Le 168 permet alors de la débloquer (`Services.cs:300-307`)
là où le natif refuserait le switch. Cosmétique ; à noter.

**Test** : supprimer la ligne `class=3` d'un perso → natif : 489 sans
classe 3, 168 vers 3 refusé ; C# : 489 avec `lv0`, 168 accepté.

### D8 — Informationnel : filtre `Eligible` `RequiredLevel ≥ 1` et divers

- `GrantableSpells` borne basse `>= 1` (ClassSkillTable.cs:56) vs
  `CheckLevel` sans borne basse — cf. D6, impact non mesuré.
- Le commentaire de dispatch `ZoneEntryFlow.cs:920` dit
  `[u8 type][u16 classId][u8]` alors que le handler lit `payload[0]`
  (`Services.cs:287`) — le désasm cite un seul octet
  (`Services.cs:278-279`) ; commentaire probablement périmé, à revérifier
  sur capture 168 (le client Godot émet l'octet attendu — voir
  `class-skills-audit.md` § réseau).
- `ClassChangeService` (`Eden.Core/Game/Services/ClassChangeService.cs`)
  est du code mort (prérequis + `RequiredQuest` TODO :62-66) — le handler
  live n'a aucune vérification de prérequis, mais n'en a pas besoin au
  sens natif (la possession `classlist` est le prérequis — cf. D1 pour le
  vrai manque).
- Croisement client : le filtre `Pay == 9` côté panneau
  (`class-skills-audit.md` §3.1, 4 sorts perdus) est un écart
  **d'affichage** — le serveur octroie quand même les sorts
  (`GrantableSpells` n'a pas de filtre `pay`, ClassSkillTable.cs:73-76) ;
  à conserver comme écart client, pas serveur.

## 4. Points conformes (non-bugs)

- **Wire 489** — format par ligne `[u8 id][u8 lv][u8 style][i32 exp]
  [i32 point][u8 aa][7×u16 lic]` = `SCharacterClass::Serialize` natif,
  vérifié par capture (`Services.cs:186-200`) ; `aa_point` comme flag
  par classe ✓ ; la classe active reste portée par attr `0x2c` + 283 ✓.
- **Burst de changement de classe** — 39 paquets dans le même ordre que
  la capture native (vitals → 296/492/493 → 488 → 489 → `0x2c` → 283 →
  attrs `6/0x3ef/0x31` → 509 → 286/316 → 510) (`Services.cs:413-483`).
- **Octroi de sorts au switch** — `GetCommonSpell` + `GetClassSpell`
  natifs sont reproduits (`Services.cs:331-353`) : classe vec+mono-mask
  gatée `classLv`, généraux gatés `charLv`. L'octroi à l'entrée
  (`EnsureEntrySpellsAsync`, `EntryPipeline.Load.cs:303-339`) reproduit le
  même modèle hors-boucle + les sorts généraux de **toutes** les classes
  possédées.
- **Licences indépendantes par classe** — `lic_1..7` par ligne classlist,
  sauvegarde/restauration à chaque switch (`SyncLicensesOnClassChangeAsync`
  `License.cs:147-192`), `SetOtherClassLicense` reproduit (182 autre classe
  → `lic_1..7` + 489 ; active → `player_license.loc` + écho 509 ;
  `License.cs:51-114`), capacité/conflits/`ReplaceSpellStones` fidèles
  (modulo D5), `GrantLicenseAsync` + remplacement `LicenseReplaceId`
  fidèle à `GetLicense`+`RemoveLicense` (`LicenseService.cs:311-333`).
- **Barres par classe** — `shortcut_menu.class` persisté par upsert
  (`SocialRepositories.cs:777-790`), pages par classe dans le 510
  (`Services.cs:682-707`), `Field3` du 55 géré comme le flush natif
  (`PlayerHandlers.cs:138-160`).
- **Pool AA par classe** — `player_characters.aa_point` = pool actif
  (cap 120, attr `0x31`) + miroir `player_classlist.aa_point` +
  `SwapAaPoolOnClassChangeAsync` = la bascule native
  (`ClassRepository.cs:229-338` ; `AdvancedAbility.cs:160-177` réémet
  489 pour les dépenses hors classe active).
- **Level-up de classe** — 284 flag=1, `+1 AA` par niveau franchi
  (cap 120), sorts du palier (286/316), **certificats à chaque multiple
  de 10** (`certs[m/10-1]`, 509 par octroi), 489 de fin, JPAward re-émis
  (`Rewards.cs:340-435`) — conforme à `GainClassPoint`/`SetClassLevel`
  bouclé niveau par niveau.
- **Dépenses/upserts spells** — `LearnSpellAsync` = niveau++ si connu,
  insert lv1 sinon (`GetSpellMaster{Insert,Update}SQLCmd` natifs,
  `ClassRepository.cs:377-408`) ; NCID 12/623 traités
  (`PlayerHandlers.cs:205-240`).
- **Classes de départ** — `{1,2,3,4,5,19}` lv1/aa1 à la création =
  capture native (`CharacterRepository.cs:206-216`).
- **Refus de cast hors classe** — `SpellAllowedForClass`/`CanCast`
  (`Services.cs:251-263`, `ClassSkillTable.cs:146-152`) = `CheckClass`
  natif, multi-masque lançable après switch.

## 5. Limites de l'audit

- Statique : pas de `dotnet build`, `dotnet test`, ni session
  client/serveur — les lignes citées sont celles de l'arbre au
  2026-11-03 et chaque affirmation de comportement repose sur la lecture
  du code + des corps natifs, pas sur l'exécution.
- `DATA_DIR` supposé `eden_server/server_us/Data/db_decoded` (cf.
  `start-native.sh`) — le bug D3/D4 est mesuré sur `S_Class.ini`/
  `S_Spell.ini` de ce dossier ; si `EDEN_REFORGED_DB` substitue un autre
  dossier, les mêmes classes d'erreur s'appliquent (la structure
  `index×100+cls` est celle du format natif, pas d'un fichier).
- Les champs de prérequis de déblocage (`classData+0x20/+0x24/+0x25`)
  sont cités par leur usage natif ; la correspondance exacte avec les
  colonnes de `S_Class.ini` n'a pas été revérifiée dans cet audit (le C#
  ne les parse pas de toute façon — D1).
- La colonne exacte du flag race `S_Spell` (~col 14, valeurs
  3/4/16/64/768 observées) est déduite des valeurs croisées avec les bits
  testés par `LoadAllCommonSpell` ; la table de désérialisation
  `CSpellQuery::Deserialize` place le champ en `+0x2c` — la position CSV
  précise reste à confirmer par un alignement complet des 45 colonnes.
- L'existence de sorts `req=0` dans des vecteurs de classe (cas du filtre
  D6/D8) n'a pas été inventoriée — preuve limitée.
- Le comportement réel en jeu (symptôme utilisateur d'une éveillée, d'un
  non-Humain…) n'est pas observé — les impacts §3 sont déduits des
  différences de données et de code, pas d'une session live.

## 6. Références

- Natif : `re/ghidra/zone/decompiled.c` — `AddClass` @0x080fd070
  (l.144249-144386), `AddClassFromLimit` @0x080fd440 (l.144387-144560),
  `ChangeClass` @0x081048c0 (l.149097-149951), `RemoveClass` @0x080fecb0 (l.145237+),
  `GetLicense` @0x080fce80 (l.144173+), `RemoveLicense` @0x080fa490
  (l.142702+), `SimpleGetSpell` @0x08156d60 (l.217113-217284),
  `GetClassSpell` @0x08157230, `GetCommonSpell` @0x08157320
  (l.217339-217398), `LoadAllCommonSpell` @0x83732b0 (l.733978-734138),
  `CheckLevel` @0x084edfe0 (l.958450), `SetCurrentClassLicense`
  @0x82714f0 (l.486545-486670), `SetOtherClassLicense` @0x82717f0,
  `GainAAPoint` @0x0840c7f0 (l.812433+), `GainClassPoint` @0x0841c140,
  `PostInit` @0x08410230 (l.814703), `GainExp` @0x0841cb80 (l.823291),
  zone `TC_AddClass` @0x083d2500 (l.775136 — handler texte
  `add_class` → `AddClass`) ; `re/ghidra/mission/decompiled.c` —
  `TC_AddClass` @0x080eee80 (émet `add_class %d` vers la zone).
- C# (chemins sous `eden_server/server/src/`) : cités en ligne dans les
  sections 2-4.
- Données : `eden_server/server_us/Data/db_decoded/S_Class.ini`
  (305 lignes), `S_Spell.ini` (flags race col ~14) ;
  `corpus/fr-full/data/db/c_class.ini`, `c_spell.ini` ;
  `artifacts/corpus-inventory/class-skills-expected.tsv` +
  `class-skills-expected-notes.md`.
- Croisé : `docs/knowledge/class-skills-audit.md` (chaîne client),
  `docs/knowledge/rankbuff-server-audit.md` (bug C = D2 ici, bug D =
  rafraîchissement JPAward couvert par le même écart),
  `docs/knowledge/native-constants-2.md`,
  `docs/knowledge/enchant-tables.md`.
