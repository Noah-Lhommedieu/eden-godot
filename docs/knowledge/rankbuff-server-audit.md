# Audit serveur — Class Ability Bonus (`c_classrankbuff` / `S_ClassRankBuff`)

Statut : **verified** pour les constats de code (chemins fichier:ligne cités,
désasm `re/ghidra/zone/decompiled.c` relu) et **hypothesis** pour l'ampleur
in-game (quels persos sont réellement affectés dépend des classes débloquées).
Audit **statique** du 2026-11-03 : aucun build ni test exécuté, lecture seule.
Données attendues : `artifacts/corpus-inventory/class-rankbuff-expected.tsv`
+ `class-rankbuff-notes.md`, mécanique native dans `native-constants.md` §1.

## 1. Attendu (rappel natif)

- `rang = floor(min(classLv, S_Parameter[578]=100) × 0.2)` → lookup **exact**
  du rang dans `map<u8 palier, 6 longs>` (RefreshJPAward zone @0x08417c10).
- `CClassRankBuffQuery::Deserialize` zone @0x085c4430 : `Skip(1)` = le
  **champ 0 est ignoré** (index séquentiel) ; la clé lue ensuite est le
  **champ 1 = classId** (`*param_2 = uVar4 ; *(uint*)param_3 = uVar4`), puis
  6 longs/palier sur paliers 1..22, enchant = 6ᵉ champ (`+0x28`).
- Le serveur n'applique que l'enchant (`EffectToLife`, durée −2, flag 0x1000)
  — les 5 champs stats (HP/MP/ATK/MATK/heal) sont ignorés serveur (portés
  par l'enchant + affichés par le client).
- Recalcul complet (purge du vecteur `char+0xba4` puis ré-application) aux
  appels : `CPlayerFactory::AddClass` @0x080fd070, `GainClassPoint`
  @0x0841c140, `Revive` @0x0841dbd0, `HookCharacter` (login) @0x0841e6a0.
- Cumul sur TOUTES les classes de `player_classlist`, pas seulement l'active.
- `AddClass` natif : classe ≠ classe de base (lookup `GameData+0x1d8`) →
  ligne insérée `level=0x50` (**80**), `aa_point=0x50` + certs des paliers
  lv/10 + spellmasters gatés 80, puis RefreshJPAward.

## 2. Constaté — chaîne serveur C#

| Étape | Emplacement | Constat |
|---|---|---|
| Parseur | `Eden.Core/Data/Parsers/ShopAndMiscParsers.cs:233-252` | Lit `S_ClassRankBuff.ini`, saute `row<8`, `Id = row[0]`, `ClassId = row[1]`, tiers = groupes de 6 dès `row[2]`, ne retient que `enchantId > 0` (`row[i+5]`), rang = index d'itération 1..22 ✓ |
| Chargement | `Eden.Core/Game/Services/GameDataService.cs:323` + `:55` | `Load("S_ClassRankBuff.ini")` — `DATA_DIR` = `eden_server/server_us/Data/db_decoded` (`start-native.sh:35,96`, `docker-compose.yml:191`) = fichier **US 60 lignes de données** (48 FR + 12 US-only) ; fichier absent → `MissingFiles` logué seulement, `RequireLoaded` ne l'exige pas |
| Application | `Eden.Zone/Program.cs:408-430` (stage « classrank ») | `cap = Parameters.Get(578,100f)` ; pour chaque ligne `player_classlist` : `rank=(int)(Min(ClassLevel,cap)*0.2f)`, puis `defs.FirstOrDefault(d => d.Id == row.ClassId)?.Tiers.FirstOrDefault(t => t.Rank == rank)` → `LicenseService.ApplyEnchantCommands(enchants.GetUseCommands(enchant))` |
| Enchant → stats | `Eden.Core/Game/Services/EnchantService.cs:396-428` (4 blocs cols 9+7k exactement comme le natif) + `Eden.Core/Game/Classes/LicenseService.cs:408-594` | Commandes résolues ; overlay client `c_enchant.ini` **FR** actif (`EDEN_REFORGED_DB` = `corpus/fr-full/data/db`, `start-native.sh:80,87,95`) — les enchants éveillées 40931-41090 existent en FR |
| Cumul | `Program.cs:419-428` | Boucle sur toutes les classes possédées, contribution sommée ✓ |
| 6 champs | `ShopAndMiscParsers.cs:242-248` | Seul le 6ᵉ (enchant) est lu — HP/MP/ATK/MATK/heal **jamais appliqués serveur** → pas de double bonus ✓ fidèle |
| Rebuild | `StatPipeline.cs:421-430` | `ResolveAsync` reconstruit tout à chaque appel (pas d'état) — équivalent du purge+ré-apply natif ✓ |

## 3. Déclencheurs (recalcul)

| Trigger natif | Chemin C# | Verdict |
|---|---|---|
| Login (`HookCharacter`) | `ZoneEntryFlow.EntryPipeline.Load.cs:584-602` — `ResolveAsync` sur prefetch « classlist » (`pf.Classes`) | ✓ couvert |
| `GainClassPoint` | `ZoneEntryFlow.Combat.Rewards.cs:430-435` — `SendForCharacterAsync` **seulement si `leveled`** → `ProfileStatEmitter.SendAsync` → `ResolveAsync(charId)` frais (`ProfileStatEmitter.cs:274-279`) | ✓ équivalent observable : le natif ré-applique à chaque appel, mais le rang ne peut changer qu'avec le niveau |
| `Revive` | `ZoneEntryFlow.Combat.Death.cs:327-435` — **aucun** `ResolveAsync` ni `SendForCharacterAsync` | ~ équivalent en résultat : pipeline stateless relu à la prochaine émission ; le rang ne peut pas baisser au revive (pénalité `class_exp` plancher 0, `level` conservé — Death.cs:379-398). Divergence formelle, impact nul |
| `AddClass` | `ZoneEntryFlow.Mission.Commands.cs:339-372` (`eden-add-class`) : `AddClassAsync` + sorts niv. 1 + 489 — **pas de 296** ; `ZoneEntryFlow.Services.cs:283-362` (`HandleChangeClassAsync`, classe de base non possédée) : `AddClassAsync` puis burst → `SendStatBlocksAsync` → `ResolveAsync` (`Services.cs:440,496`) ; GM `add_class` (`ZoneEntryFlow.Admin.cs:325-334`) : `AddClassAsync` seul, pas de refresh | ~ le recalcul existe sur le chemin switch ; absent sur `eden-add-class`/GM — **impact nul** tant que la nouvelle ligne est lv1 (rang 0) — mais devient visible si le lv80 éveillé est implémenté |
| Autres mutations de stats | `ZoneEntryFlow.Status.cs:563-578` `RefreshStatsAfterStatusAsync` (add/remove enchant) → `ResolveAsync` + `ApplyContribution` ; `ZoneEntryFlow.Combat.cs:241-244` (resolve arme/équipement) | ✓ tout refresh re-somme le classrank |

## 4. Divergences constatées (triées par gravité)

### A. MAJEUR — lookup par index de ligne au lieu du classId

`Program.cs:423` compare `d.Id == row.ClassId` où `d.Id` = **champ 0**
(`ShopAndMiscParsers.cs:240` : `Id = row[0]`). Or le natif **saute** ce champ
(`Skip(param_1,1)`) et clé la map sur le **champ 1**. `ClassRankBuffDef.ClassId`
est parsé mais **jamais utilisé**. Le commentaire de `ClassRankBuffDef.Id`
(« Id de ligne = classId ») est factuellement faux.

Dans le fichier chargé (`server_us/Data/db_decoded/S_ClassRankBuff.ini`),
f0 = index séquentiel 1..60, f1 = classId — f0 ≠ f1 dès la ligne 20
(classe 20 absente) et pour les lignes ajoutées en fin (29,30,45,46 puis
les 12 éveillées US). Table effective du serveur actuel :

| classId demandée | ligne lue (f0=classId) | bonus appliqué (f1) | classId demandée | ligne lue | bonus appliqué |
|---|---|---|---|---|---|
| 1-19 | 1-19 | **correct** | 42 | 42 | 59 |
| 20 (Messie) | 20 | 21 ⚠ (bonus indu) | 43 | 43 | 60 |
| 21-27 | 21-27 | 22-28 (décalé +1) | 44 | 44 | 61 |
| 28 | 28 | 31 | 45 | 45 | 29 |
| 29 | 29 | 33 | 46 | 46 | 30 |
| 30 | 30 | 34 | 47 | 47 | 45 |
| 31-35 | 31-35 | 34-38 (décalé) | 48 | 48 | 46 |
| 36 | 36 | 41 | 49-52 | 49-52 | 39,40,43,44 (éveillées 4 paliers) |
| 37 | 37 | 42 | 53-56 | 53-56 | 47,48,53,54 |
| 38 | 38 | 49 | 57 | 57 | 55 |
| 39 | 39 | 50 (base, 20 paliers !) | 58 | 58 | 56 |
| 40 | 40 | 51 (base) | 59 (Juge) | 59 | 57 ⚠ |
| 41 | 41 | 52 | 60 | 60 | 58 |
|  |  |  | **61** | **aucune** | **rien** (f0 max=60) |

Conséquences concrètes :
- classes 1-19 (dont toutes les classes de départ + secondaires 6-18) :
  **correctes** — d'où des tests verts (`GameDataTests.cs:44-58` ne couvre
  que `d.Id==1` et `d.Id==4`, lignes où f0==f1) ;
- Messie (20) reçoit les bonus éveillés de la classe 21 au lieu de rien ;
- Juge (59) reçoit les bonus de l'éveillée 57 au lieu des siens ; classe 61
  ne reçoit **rien** ;
- les classes de base 49-52 perdent leurs paliers 1-16 (elles tombent sur
  des lignes éveillées 4 paliers) ;
- éveillées 39-44 reçoivent des lignes de classes de base (20 paliers —
  bonus dès lv5 là où l'éveillée ne devrait rien avoir avant lv85) ;
- le cumul inter-classes somme des bonus de **mauvaises classes** — un
  multi-classé accumule un mélange incohérent (physique/caster croisé).

Test qui tranche : `ParseClassRankBuffs` sur le fichier réel +
`Assert.True(gd.ClassRankBuffs.First(d => d.ClassId == 59).Tiers[0].EnchantId
== 40911)` (Juge palier 1, cf. notes §4) — échoue aujourd'hui : `Id` de la
ligne f1=59 vaut 42. Test minimal équivalent : chercher `d => d.Id == 29`
doit retourner les enchant de la classe **29**, elle retourne ceux de la 33.

### B. MAJEUR (classes soignantes) — cmd 2047 `HealPlus` flat ignorée

`ApplyEnchantCommands` (`LicenseService.cs:412-594`) n'a **pas de case 2047** ;
le commentaire lignes 585-591 la classe en « dupliqué de 2038 » alors que
`EnchantCommands.cs:97-98` la documente : `2047 = "HealPlus 4" / "HealPlus 5"`
— les modes **flat** : `CEC_HealPlus::Execute` @0x08535480, mode 4 →
`kinddata+0x144` (**soins prodigués flat**, `CCharFormula::Heal` @0x083f9210),
mode 5 → `+0x14c` (soins reçus flat, `BeHeal` @0x083f9110).

- **168/560** enchants de palier portent une commande 2047 (mesuré sur le
  fichier chargé) — classes f1 concernées :
  **4, 6, 11, 12, 13, 15, 17, 27, 32, 42, 44, 46, 50, 54** (les classes à
  « Soin » — ex. 45747 `2084|||47` M-ATK + `2047|13` G-Healing flat).
- `StatContribution` n'a **aucun canal** heal flat (seuls
  `HealGivenPercent`/`HealTakenPercent` existent, pour 2038 modes 2/3)
  → la donnée n'est même pas représentable ; le bonus Soin des paliers est
  **silencieusement perdu** serveur (le client affiche quand même le champ
  `heal` → affichage sans effet réel).

Test qui tranche : enchant 45747 → `ApplyEnchantCommands` doit produire un
canal heal-given flat +13 ; aujourd'hui toute la contribution de la cmd 2047
est nulle (aucune assertion possible — canal absent, à créer).

### C. MAJEUR (éveil) — classe éveillée insérée lv1/aa1 au lieu de lv80/aa80

`ClassRepository.AddClassAsync` (`ClassRepository.cs:95-98`) insère
`level=1, aa_point=1` pour **toute** classe. Natif `AddClass` @0x080fd070 :
si `classId != baseClass` (`GameData+0x1d8`) → `pSVar4[0x18] = 0x50` et le
`classlist` INSERT porte `level=0x50` (**80**), `aa_point=0x50` (**80**), +
`GetLicense` des paliers lv/10 + `SimpleGetSpell` gatés 80 + RefreshJPAward.

Chemins C# concernés : `eden-add-class` (`Mission.Commands.cs:349`, sorts
`GrantableSpells(cls, 1, …)` = niveau 1 seulement), GM `add_class`
(`Admin.cs:329`), base non jouée `HandleChangeClassAsync` (`Services.cs:303`
— correct pour une base lv1). Conséquence rankbuff : aucune à lv1 (rang 0)
comme à lv80 (rang 16, paliers éveillés vides) ; mais les sorts, certs et
aa_point de l'éveil sont faux, et le palier 17 (lv85) arrive plus tard —
le joueur éveillé monte 84 niveaux au lieu de 5 avant le premier bonus.

### D. Mineur — recalcul manquant sur `eden-add-class` / GM `add_class`

Natif : RefreshJPAward **dans** AddClass. C# : ni 296 ni re-resolve sur les
deux chemins d'octroi direct (voir §3). Impact nul tant que lv1 (rang 0) ;
si C est corrigé (lv80 éveillé), un éveil offert à un perso déjà lv85+
changerait le cumul sans réémission → bug latent.

### E. Mineur — décompte de lignes du fichier chargé

`server_us/Data/db_decoded/S_ClassRankBuff.ini` contient ici **60 lignes de
données** (f0=1..60), là où `class-rankbuff-notes.md` §3 annonce « 60 lignes
header+data / 59 data » — à revérifier si le décompte du doc portait sur la
copie `portage/`. Aucun impact fonctionnel.

### F. Comportement des 12 classes sans ligne FR

Le serveur lit la table **US** (60 lignes) : les lignes des classes
39,40,43,44,47,48,53-58 **existent côté serveur** (et leurs enchants
41003-41090 sont présents dans `c_enchant.ini` FR via l'overlay). Avec un
lookup corrigé, le serveur accorderait des bonus invisibles dans l'UI FR —
cohérent avec l'architecture « effet serveur / affichage client » et le gap
déjà consigné dans `class-rankbuff-notes.md` §3. Sans erreur ni log :
`FirstOrDefault` null → palier sauté silencieusement ; enchant inconnu →
`GetUseCommands` vide silencieusement. Avec le bug A, ces classes reçoivent
les bonus de **mauvaises lignes** et les leurs sont servies aux classes
49-60 — inversement de ce qu'afficherait un client complété.

## 5. Points conformes (non-bugs)

- Formule rang + cap `S_Parameter[578]` : identique au natif
  (`Program.cs:411,421` ; `S_Parameter.ini` ligne `578|100` chargée).
- Lookup **exact** du rang (`t.Rank == rank`) : équivalent du `clé == rang`
  natif ; paliers vides (enchant 0) skippés au parse = même résultat que
  l'`EffectToLife(0)` no-op.
- Seul l'enchant est appliqué (6ᵉ champ) : **pas de double bonus** avec les
  5 champs affichage — fidèle au serveur natif.
- Cumul toutes classes possédées, rebuild complet à chaque résolution,
  login/JP/status-refresh couverts.
- `EffectToLife(-2,0x1000)` = contribution de pipeline (pas de ligne
  `player_enchants` persistée — pas de doublon possible au restore).

## 6. Limites de l'audit

- Statique : pas de `dotnet build`/`dotnet test` ni de run serveur — les
  lignes citées sont celles de l'arbre au 2026-11-03.
- `ReforgedDataLocator` peut substituer un autre dossier db si
  `EDEN_REFORGED_DB` change ; l'audit suppose `corpus/fr-full/data/db`.
- Non vérifié : la valeur DB réelle de `player_classlist.level` pour des
  persos existants (des éveillées déjà insérées à lv1 resteront à lv1 même
  après correction de C — migration à prévoir).
