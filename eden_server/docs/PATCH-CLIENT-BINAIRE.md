# Patcher le client binaire (game_.exe / dlls Reforged)

Méthode utilisée pour les correctifs côté client Reforged (le client
projet `client/` reste la référence et n'est patché que si le même
comportement y est demandé). À suivre par tout agent qui touche aux
binaires clients.

## Principes

- **On ne patche que ce qui est impossible côté serveur** : logique
  d'input, d'animation, de rendu, tables de features forcées
  (`_g_forced_off` dans `d3d9.dll`), gardes locales du client.
  Tout ce qui est protocole/gameplay se corrige côté serveur.
- **Preuve avant patch** : localiser le code dans
  `re/ghidra/rf_game/decompiled.c` (ou `rf_d3d9`, `client_game`…),
  identifier la condition exacte, vérifier les octets par
  `objdump -d -M intel` sur le binaire AVANT d'écrire.
- **Backup obligatoire** : `cp fichier fichier.bak_<feature>`
  avant toute écriture. Un patch par feature = un backup nommé.
- **Idempotence** : relire les octets après patch ; si le binaire est
  déjà patché (octets attendus présents), ne pas réécrire.

## Méthode pas à pas

1. **Identifier la fonction** dans le décompilé :
   `reindex search|lookup|addr|xref -b rf_game` (jamais REA).
   Pour un comportement observable, partir d'une constante parlante
   (id de message `0x912`, texte, flag `0x2000`…) ou du handler du
   NCID concerné, puis remonter les callers.
2. **Confirmer l'assembleur** : `objdump -d --start-address=0xRVA
   --stop-address=0xRVA+n -M intel game_.exe`. Ghidra peut se tromper
   sur les directions de comparaison — l'octet réel fait foi.
3. **Convertir RVA → offset fichier** : sections PE via
   `objdump -h` — pour `game_.exe`, `.text` : VMA 0x401000,
   offset 0x400 → `fileOff = RVA - 0x400C00`.
4. **Choisir le patch minimal** :
   - condition à toujours franchir : `74/76/7x xx` (jcc court) →
     `EB xx` (jmp court) — même taille, zéro décalage ;
   - condition à ne jamais franchir : jcc → `90 90` (2 NOP) ou
     `EB` vers la cible « mauvaise » selon le cas ;
   - appel à neutraliser : `E8 xx xx xx xx` → `90 ×5` ;
   - table de strings (ex. `_g_forced_off` dans rf_d3d9) : repointer
     l'entrée vers un nom inerte existant (jamais de nouvelle string,
     la table de relocalisations PE rend ça fragile).
5. **Écrire** avec `python3`/`dd` à l'offset fichier, relire avec
   `objdump` pour vérifier que le désassemblage reste cohérent
   (pas d'instruction tronquée).
6. **Vérifier les deux exécutables concernés** : Reforged embarque
   `game_.exe` patché + parfois des variantes dans `mods/` —
   chercher le même motif d'octets dans les autres binaires.
7. **Consigner** : offset fichier, RVA, octets avant/après, fonction
   Ghidra, preuve (commit/notes) dans le temp-plan et ce fichier.

## Patches posés à ce jour

| Binaire | Offset fichier (RVA) | Patch | Effet |
|---|---|---|---|
| `game_.exe` | 0x35B8B0 (0x75C4B0) `FUN_0075c3a0` | `76`→`EB` | cast timed en mouvement — la garde `entity+0xcc` (moving) qui renvoyait 0x912 à ChannelWndV2 |
| `game_.exe` | 0x35BC62 (0x75C862) `FUN_0075c740` | `76`→`EB` | idem, 2ᵉ chemin de cast |
| `game_.exe` | 0x359E0C (0x75AA0C) `FUN_0075a8f0` | `75 3d`→`90 90` | steering RMB pendant le cast — le gate `ctrl+0x3c>>0xf` (bit15 casting) ne saute plus la copie yaw-caméra→heading acteur (`[actor+0x1c]`) |
| `game_.exe` | 0x35BA15 (0x75C615) `FUN_0075c3a0` | `E8`→`90×5` | micro-stop au cast en mouvement — plus de `FUN_0075ad10` (reset action → idle) quand l'action courante est la course (`+0x244`) |
| `game_.exe` | 0x35BDE0 (0x75C9E0) `FUN_0075c740` | `E8`→`90×5` | idem, 2ᵉ chemin de cast (type 5) |
| `game_.exe` | 0x35CFD6 (0x75DBD6) `FUN_0075d9f0` | `75 32`→`90 90` | SetDir(yaw caméra→acteur) autorisé monté — le test `GameControl+0xe3 & 0x40` (monture) ne saute plus vers la branche directive |
| `game_.exe` | 0x35D008 (0x75DC08) `FUN_0075d9f0` | `EB 0D`→`90 90` | **strafe monté 90°** : le `jmp` qui sautait `FUN_0069a990(cam,10)` (directive caméra `cam+0xe4=10` — mécanisme vanilla de facing monture au départ d'un déplacement) est neutralisé → tout départ de déplacement exécute SetDir **puis** la directive. Effet de bord assumé : la directive s'écrit aussi non-monté (vanilla l'écrit déjà au relâchement bouton via `FUN_0075a3b0/410`, même état `cam+0xe4=10`) |
| `game_.exe` | 0x359D5D (0x75A95D) `FUN_0075a8f0` | `75 19`→`90 90` | **caméra pendant timed cast** : le `jne` qui sautait `call FUN_0075d960` (yaw caméra) quand `obj+0x3c bit15` (casting) est neutralisé |
| `game_.exe` | 0x35CD70 (0x75D970) `FUN_0075d960` | `75 74`→`90 90` | idem — gate interne de `FUN_0075d960` sur le même bit15 casting (re-test en tête de fonction) |
| `game_.exe` | ~~swap L/R records `DAT_00fe4ba0`/`DAT_00fe4bd8`~~ | **REVERTED** (§ B2, 2026-11-06) | le swap inversait direction+facing+anim — Q faisait un strafe droit complet → records rendus vanilla (tête à gauche pour Q) |
| `game_.exe` | ~~0x35CF0C (0x75DB0C) + 0x35CF33 (0x75DB33) `FUN_0075d9f0`~~ | **REVERTED** (§ B4, 2026-10-25) | le NOP forçait le pivot 90° aussi **à pied** (sur-décalé) — vraie condition = bit9 du mot d'état 302 que le serveur n'émettait pas ; `74 38`/`74 11` restaurés, fix côté serveur |
| `d3d9.dll` | entrées `_g_forced_off` | pointeurs repointés sur `go_to` | réactive `collect_all`/`pay_all` (ini reprend la main) |

Backups : `game_.exe.bak_strafe3` (état pré-patch mount-record
sélection, 2026-11-06), `game_.exe.bak_strafe4` (état NOPs —
pré-revert, 2026-10-25) en plus de `game_.exe.bak_castmove`,
`game_.exe.bak_steer` (avant steering+micro-stop),
`game_.exe.bak_castmove2`, `game_.exe.bak_microstop`,
`game_.exe.bak_camcast`, `game_.exe.bak_mountstrafe` (état pré-swap =
records vanilla), `game_.exe.bak_strafeface` (état post-swap, avant le
revert), `d3d9.dll.bak_forcedoff` dans `reforged/EdenReforged/`
(mêmes backups sous `client/` et la copie déployée
`~/Games/reforgeddecompiled/...`).

### B2 — Strafe monture : REVERT du swap L/R des records (2026-11-06)

**Sémantique des records de direction** (table `DAT_00fe49a8`, 9
records `LR+FB*3` de 0x38 o + 2 variantes monture `DAT_00fe4ba0` =
LR0/strafe-gauche, `DAT_00fe4bd8` = LR2/droite ; sélection monture =
`player+0x3c` bit9|bit17 dans `FUN_0075d9f0` @0x75dafc-0x75db45) :

- `+0x00` : angle latéral signé (aucun lecteur trouvé — legacy).
- `+0x04` : **offset de direction de déplacement** — lu dans
  `FUN_0077a910` (`rf_game @ 0x77a91d-0x77a928` :
  `fld [arg]; fadd [0xf08ea4]=+π/2; fsubr [0xf08ea8]=2π;
  fsub [record+0x4]`) → vecteur de déplacement
  (cos,sin) de `heading − record+0x04 + π/2`, utilisé par
  `FUN_0077adb0` pour la position prédite du paquet CNE1.
- `+0x08` : facteur vitesse (× `entity+0x8c`).
- `+0x0c` : **offset de facing** — `FUN_00740ac0` appelle
  `DCtrl->vfunc+0x1c(heading + record+0x0c)` ; `ctrl+0x64`=record
  (`mov [edi+0x64],eax` @0x77b222).
- `+0x10` : callback destination (`FUN_00741d70` → `vfunc+0x38` =
  nœud gauche ; `FUN_00741db0` → `+0x3c` droite ;
  `FUN_00741d30`/`FUN_00740a90` → `+0x34`/`+0x30` forward/back) —
  remplit le descripteur de destination `movobj+0x1c` via
  `FUN_00735bc0` et y OR le flag via `FUN_007418d0`.
- `+0x14` : action id (s16 du CNE1 → MoveFlag serveur → 326 aux
  autres clients).
- `+0x18` : bits direction (ORés dans `dest+0x38`).

Constantes (`.rdata`) : `0xf09268`=+π/2, `0xf09278`=3π/2≡−π/2,
`0xf0928c`=+π/4, `0xf0927c`=−π/4, `0xf09288`=3π/4, `0xf09254`=−π/4,
`0xf09294`=π, `0xf091f8`=+π/2, `0xecdd5c`=−π/2. Convention : offsets
négatifs = gauche (prouvé par les diagonaux : rec6 avant-gauche
`+0x0c`=−45°).

**Valeurs vanilla des records monture** : LR0 (Q) : `+0x04`=270°,
`+0x0c`=270°(−90°), cb=741d70, action 3, flag 8 → **déplacement −90°,
tête −90° = strafe gauche avec la tête vers la gauche** ; LR2 (D) :
miroir +90°/cb 741db0/action 5/flag 0x10. Le swap de `bak_mountstrafe`
avait croisé **tous** les champs → Q exécutait un strafe droit complet
(bouge à droite + tête à droite) = « ne pivote pas vers la direction
de strafe ». **Correction = revert total** aux 8 sites ci-dessous
(identique aux octets de `bak_mountstrafe`).

| RVA (fichier) | Avant (swappé) | Après (vanilla) | Champ |
|---|---|---|---|
| 0xEB7B18 (0xAB6F18) | `d9 05 68 92 f0 00` | `d9 05 78 92 f0 00` | rec9 `+0x04/+0x0c` : +π/2 → −π/2 |
| 0xEB7B3A (0xAB6F3A) | `c7 05 b0 4b fe 00 b0 1d 74 00` | `… 70 1d 74 00` | rec9 `+0x10` : 741db0→741d70 |
| 0xEB7B44 (0xAB6F44) | `66 c7 05 b4 4b fe 00 05 00` | `… 03 00` | rec9 `+0x14` : action 5→3 |
| 0xEB7B4D (0xAB6F4D) | `c7 05 b8 4b fe 00 10 00 00 00` | `… 08 00 00 00` | rec9 `+0x18` : flag 0x10→8 |
| 0xEB7B81 (0xAB6F81) | `d9 05 78 92 f0 00` | `d9 05 68 92 f0 00` | rec10 `+0x04/+0x0c` : −π/2 → +π/2 |
| 0xEB7BA3 (0xAB6FA3) | `c7 05 e8 4b fe 00 70 1d 74 00` | `… b0 1d 74 00` | rec10 `+0x10` : 741d70→741db0 |
| 0xEB7BAD (0xAB6FAD) | `66 c7 05 ec 4b fe 00 03 00` | `… 05 00` | rec10 `+0x14` : action 3→5 |
| 0xEB7BB6 (0xAB6FB6) | `c7 05 f0 4b fe 00 08 00 00 00` | `… 10 00 00 00` | rec10 `+0x18` : flag 8→0x10 |

Appliqué sur `reforged/EdenReforged/game_.exe`, la copie
`~/Games/reforgeddecompiled/.../EdenReforged/game_.exe` **et**
`client/game_.exe` — vérifié `cmp` = 0 diff vs `bak_mountstrafe`.

**Combinaisons de repli si le retest montre encore un souci** :
- monture tourne+bouge correctement mais **anim visuellement
  inversée** → ne swapper QUE `+0x14`/`+0x18` (action/flag) :
  rec9 `…4b fe 00`@EB7B44 `03`→`05`, EB7B4D `08`→`10` ;
  rec10 EB7BAD `05`→`03`, EB7BB6 `10`→`08`.
- monture **ne pivote pas du tout** → la sélection des records
  monture n'est pas atteinte : vérifier que `player+0x3c` bit9|bit17
  est bien posé (flag monture côté serveur) et que les patchs
  0x75DBD6/0x75DC08 sont encore en place.
- monture pivote **à l'envers** → convention de signe : re-swapper
  uniquement les deux `fld` (EB7B18↔EB7B81) en gardant cb/action/flag
  vanilla — attention, `+0x04` (direction) change avec `+0x0c`.

⚠️ **2026-10-24 — réécriture partielle du binaire** : le `game_.exe`
daté 05:25 avait perdu les deux patches micro-stop (`0x35BA15`,
`0x35BDE0` — `E8` restauré) tout en gardant les 5 autres. Une maj du
launcher a probablement restauré le fichier. Réappliqués (`90×5`) —
**revérifier `game_.exe` après chaque maj Reforged** (script :
comparer les 7 offsets du tableau).

Notes offsets : `fileOff = RVA − 0x400C00` (`.text` VMA 0x401000,
offset 0x400). Le `75AA0C` reste suivi des gardes `+0x2e6` (cast non-
interruptible) et `+0x3c bit0` (dead/busy) — volontairement conservées.

### B3 — Strafe monture : sélection forcée des records monture (2026-11-06, v3)

**Cause racine trouvée** : la table `DAT_00fe49a8` contient 9 records
normaux + 2 variantes monture. Le record **strafe gauche NORMAL**
(index 3, `@0xfe4a50`) a `+0x0c = 0` — vanilla : strafe à pied = on
se déplace latéralement **sans tourner le torse** ; le record
**monture** rec9 (`@0xfe4ba0`) a `+0x0c = −π/2` → la monture pivote
à 90° (idem rec10/rec5 pour la droite, `+0x0c = +π/2` — init vérifiée
`objdump` aux RVA 0xEB78B2→0xFE4A5C (`fldz` → +0x0c=0 pour le record
normal) vs 0xEB7B18→0xFE4BAC (`fld 0xf09278` → −π/2 pour rec9) et
0xEB7B81→0xFE4BE4 (+π/2 pour rec10)). Diagonaux et strafes purs ont
les **mêmes** `+0x04` (direction) et `+0x14` (action id 3/5 — les
anims) : seul `+0x0c` diffère → symptôme exact du joueur : anims
bonnes, déplacement bon, rotation absente.

**Chemin complet vérifié** (rf_game) :
- record → `FUN_0077b1b0`/`FUN_0077b3b0` (movobj start/update) →
  `FUN_00740ac0(controller, heading)` → `controller+0x80 = heading`,
  `DCtrl->vfunc+0x1c(heading + record+0x0c)` ;
- `vfunc+0x1c` = `FUN_00734440` (SetDir : écrit `DCtrl+0x58` +
  copie la matrice 3x3 dans `*(DCtrl+8)+0x30` → rotation immédiate
  du modèle, gatée par `DCtrl+0x56` = lock de direction transient) ;
- sélection monture = `player+0x3c` bit9|bit17 @0x75daf4-0x75db33 —
  le state « ride » natif (`LifeEntityDo(entity,9)` côté zone,
  `lapis::CEC_Ride::SetState` @0x0854f930) n'atteint pas toujours ce
  dword chez nous → le record normal `+0x0c=0` était pris.
- correction collatérale : `GameControl+0xe3 & 0x40` testé @0x75dbcf
  n'est **pas** un flag monture — c'est l'option « CamaraRotation »
  (chaîne `CamaraRotationb=%d` @ `FUN_0069e290` l.545172) : vanilla
  SetDir(camYaw) si OFF, directive cam lissée si ON. Les NOPs
  0x75DBD6/0x75DC08 restent en place (SetDir + directive toujours).

**Patch** : `je 0x75db46` NOPés aux deux sites de sélection
(RVA 0x75DB0C fichier 0x35CF0C `74 38`→`90 90`, RVA 0x75DB33
fichier 0x35CF33 `74 11`→`90 90`) → strafe gauche prend toujours
rec9, droite rec10, que le bit ride soit posé ou non. Inoffensif si
le bit est déjà posé (même record choisi). Effet de bord assumé :
strafe pur **à pied** pivote aussi à 90° (cohérent avec les
diagonaux qui pivotent déjà ±45° à pied) — à valider en jeu ;
revert = restaurer `74 38`/`74 11` depuis `bak_strafe3`.

### B4 — Strafe monture : REVERT du NOP + fix racine côté serveur (2026-10-25)

**L'effet de bord s'est confirmé en jeu** : le pivot 90° s'applique
aussi à pied (« sur-décalé »). La cause racine côté serveur a été
trouvée → voie (a) choisie : **revert des NOPs + émission du flag
ride par le serveur**.

**Mécanisme natif du bit ride** (chaîne complète vérifiée) :

- Montée : `CItemFactory::DoUseItem`/`CheckRide` →
  `CEC_Ride::Execute` → `CEC_Ride::SetState(rideKey)` @0x0854f330
  → `LifeEntityCanDo(9)` + `LifeEntityDo(entity,9,&local,1)` →
  `CStateController::Do(9)` @0x084e3dd0 → `CStateFSM::SetFlags`
  @0x084e4470 → `CLifeEntityFlags::SetFlag(idx,true)` @0x08484110
  → `entity+0x3a |= 1<<idx`.
- `idx` = `CStateFSM[i]+0x20`, rempli par
  `CStateDBO::GetAllStatesFlagSetting` @0x0836fa40 depuis la ligne
  **-1** de `S_StateDependence.ini` = `0,1,…,18` → **flag index =
  state id** : l'état 9 « mounted » pose **bit9 = 0x0200**, l'état
  17 « changing form » pose bit17 = 0x20000 (le client teste
  bit9|bit17 → les transformés strafent aussi en mode monture).
  `CEC_Ride::SetState` teste d'ailleurs `(byte)char+0x3b & 2`
  (= word bit9) comme marqueur « déjà monté ».
- `Do(...,notify=1)` → `CStateController::NodifyFlagChange`
  @0x084e3c00 → **NCID 302 `CNC_CZ_ClientFlagModify`** =
  `[u32 handle(char+0x34)][u32 mot=char+0x3a entier][u8 seq=char+0x280]`
  émis `BroadCastToCanSee` **et** `NotifyClient` (le cavalier reçoit
  son propre mot — c'est ce qui pose `player+0x3c` chez l'acteur ;
  le client applique le mot à `actor+0x3c`, prouvé par le freeze
  sel=1 du 2026-11-04). Puis 283 `ClientModelModify` mask=2 rideKey.
- Descente : `LifeEntityRemoveDo(9,1)` → même chemin → 302 avec
  mot `& ~0x0200` → 283 rideKey=0.

**Notre serveur n'émet ce 302 nulle part sur le chemin ride**
(`ItemActionHandlers` montée/descente, `DismountAsync`,
`TryAutoDismountForCombatAsync`) → bit9 jamais posé → le `je`
vanilla repartait toujours sur le record normal.

**Patch appliqué** : `90 90`→`74 38` @0x35CF0C et `90 90`→`74 11`
@0x35CF33 sur les 3 binaires (`reforged/EdenReforged`, `client/`,
copie déployée `~/Games/reforgeddecompiled/…/EdenReforged`) —
backup `game_.exe.bak_strafe4`. `cmp` = 0 diff vs `bak_strafe3`
(restore byte-identique). Sélection vanilla restaurée : bit9 posé
→ records monture (pivot ±90°), sinon → records normaux (pas de
rotation à pied).

**Fix serveur requis** (à implémenter par l'agent serveur — hors
périmètre de cet agent) :

1. Montée (`ItemActionHandlers`, chemin `ride` else-branche) :
   émettre `CNC_CZ_ClientFlagModify` `[charId][mot | 0x0200][seq]`
   à l'acteur + broadcast node — mot de base joueur `0x01000000`
   → monté `0x01000200` (bytes `00 02 00 01`) ; OR sur le mot
   courant si combat actif. Poser l'état AVANT/avec le 283.
2. Descente (`ItemActionHandlers` else+`DismountAsync` +
   `TryAutoDismountForCombatAsync`) : 302 mot `& ~0x0200` (retour
   au mot courant hors ride).
3. seq = compteur par perso incrémenté à chaque 302 (natif
   `char+0x280`, déjà utilisé par les chemins combat/migration).
4. `ClientSeeCharCodec.LifeFlags` : `mounted` écrit aujourd'hui
   byte0 `0x40` = word bit6 (state 6 « using an item ») — le bit
   ride correct = **byte1 `0x02` = word 0x0200** pour que les
   observateurs (265/263) voient l'état monté au spawn. ⚠️
   `elfOut` écrit déjà byte1 `0x02` → poserait le bit monté quand
   l'elf est invoqué — à vérifier (l'elf natif est un autre bit,
   sinon summon elf = perso « monté » pour les observateurs).
5. Migration : le 302 de base `0x01000000` post-portail doit
   porter 0x0200 si le perso migre monté (RideStates persistant).

**À tester en jeu** : monté → strafe Q/D pivote à 90° (records
monture) ; à pied → strafe latéral sans rotation (record normal
`+0x0c`=0). Si le serveur n'émet pas encore le 302, à pied est
déjà redevenu vanilla et la monture reste sans pivot (comportement
vanilla pré-fix) — le pivot revient avec le fix serveur.

### Résolution « caméra pendant timed cast » (2026-11-06)

**Cause trouvée en statique** (le debug live prévu n'a pas été
nécessaire) : le setter RMB `0x75a1b0` est inconditionnel et le drag
`FUN_0075a8f0` (vtable+0x18) s'exécute bien pendant le cast — c'est le
yaw `FUN_0075d960` qui est gaté **deux fois** par `obj+0x3c bit15`
(casting) : le `call` sauté à `0x75a95d` **et** un re-test identique en
tête de `FUN_0075d960` (`0x75d970`). Les deux `jne` sont NOPés —
les gardes `+0x2e6` (cast non-interruptible) et `+0x3c bit0`
(dead/busy) restent actives dans les deux fonctions. Backup :
`game_.exe.bak_camcast`. À valider visuellement in-game.

### Ancien dossier « caméra pendant timed cast » (2026-10-24)

`rf_game` et `client_game` sont **le même code** (7 diffs = branding
EASYFUN/LOGLOG uniquement) — le blocage est donc vanilla ou dispatch,
pas une régression Reforged dans `game_.exe`. Cartographie faite :

- Vtable contrôleur @ `0xF074C0` : `+0x00`=cancel drags (0x75a300),
  `+0x04`=LMB-down (0x75a350, **gâté** par action timed + model+0x57),
  `+0x08`=LMB-up (0x75a410), `+0x0c`=RMB-down (0x75a1b0,
  **inconditionnel** : `cam+0xa4&~2`, `ctrl+4|=2`), `+0x10`=RMB-up
  (0x75a3b0), `+0x14`=drag bit0 (0x75a130), `+0x18`=drag bit1
  (`FUN_0075a8f0` = rotation caméra).
- `FUN_0075a8f0` : le yaw est gâté seulement par `ctrl+0x2e4 bit1`
  (toggle lock-marche, keybind 0x45/0x22 → `^2` @552507) — **pas par
  le cast**. Le pitch est non-gâté. Donc si la caméra est bloquée au
  cast, le flag `ctrl+4 bit1` n'est jamais armé → le dispatcher
  d'appui RMB (en amont, dispatch vtable invisible au statique) avale
  le clic pendant le cast.
- Prochaine étape = **debug live** : breakpoint x64dbg sur `0x75a1b0`
  (setter RMB-drag) pendant un timed cast. S'il ne s'exécute pas,
  trouver le dispatcher (souris→vtable+0x0c) et sa gate ; s'il
  s'exécute, c'est `+0x2e4 bit1` qui est armé pendant le cast (bit16
  `|= 0x10000` posé par `FUN_0075acb0`/`FUN_0075cca0` = cast-attempt,
  à distinguer).

## Limites & maintenabilité

- **Fragile aux majs du client** : un nouveau `game_.exe` invalide
  tous les offsets — retrouver le motif d'octets (`cmp [reg+0xcc]` +
  le saut) dans le nouveau binaire avant de reporter.
- **Pas de signature/checksum** côté client constaté : les patchs
  survivent au lancement via `launch-reforged.sh`. Si un check
  d'intégrité apparaît, la méthode tombe — prévoir le revert.
- **Alternatives plus maintenables** :
  - `eden_patch.ini` + table de features (d3d9) : préférable quand la
    feature existe déjà (c'est ce qu'on a débloqué pour collect_all) ;
  - DLL de hook dans `mods/_testing/` (modèle rf_gamefix) : plus
    durable qu'un patch en dur car chargée dynamiquement — à
    privilégier si le nombre de patches grandit ;
  - patch en dur = dernier recours, pour les gardes de code
    inaccessibles autrement.
- **Toujours documenter le diff octet-par-octet** — `cmp -l
  backup patched` doit lister exactement les octets attendus.

## Patch ALAudio.dll — déblocage du chargeur de plugins (2026-10-24)

**Contexte** : `rf_alaudio` (le proxy ALAudio Reforged) vérifie
l'intégrité de `connect.ini` au démarrage du loader :
`FUN_100038e0` (`ALAudio.dll @ RVA 0x38e0`) calcule un SHA-224 du
fichier et le compare à une whitelist de 2 hashes embarqués
(`.rdata @ 0x212f0`). Notre `connect.ini` réécrit
(`Server=127.0.0.1,6543`) n'y figure pas → `DAT_1002d34c = 0` →
le scan de plugins `mods\_testing\*.dll` est court-circuité
(`if ((DAT_1002d34c != 0) && (DAT_1002baac != 0))` @ init).
Symptôme : **aucun** mod `_testing` chargé (gameFix, ItemMall,
NeoInventoryBank, Limitless absents de `/proc/<pid>/maps`).

**Patch** : forcer `FUN_100038e0` à retourner 1.

- Fichier : `ALAudio.dll` (répertoire jeu) — backup
  `ALAudio.dll.bak-limitless`.
- Offset fichier `0x2ce0` (RVA `0x38e0`, début de fonction) :
  - avant : `b8 b8 20 00 00 e8 a6 7e 00 00`
    (`mov eax,0x20b8; call __alloca_probe`)
  - après : `b8 01 00 00 00 c3 90 90 90 90`
    (`mov eax,1; ret; nop×4`)
- `DAT_1002d34c` n'a que 2 usages : init (0x1647) et le gate du
  scan plugins (0x4880). `clal.xmf` = vraie lib audio déléguée,
  pas un check d'intégrité sur la DLL — patch sans effet de bord.

**Vérifié live** : après patch + relance sous Proton-CachyOS
(`WINEDLLOVERRIDES="d3d9=native,builtin"`), les 4 DLL de
`mods\_testing` apparaissent dans les maps du process et
`limitless_mod.log` rapporte `install gen@2032: rc=0`.
