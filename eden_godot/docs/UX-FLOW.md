# UX Flow — chaîne de jeu complète (24/10/2026)

`run/main_scene` = `scenes/boot.tscn` → `ScreenManager`
(`scripts/screens/ScreenManager.cs`, Control racine plein écran).

```
boot.tscn ─ ScreenManager
  ├─ BootScreen        logo « EDEN REBORN » + ProgressBar (~0,85 s)
  ├─ LoginScreen       CEGUI Login.layout — AccountEdit/PasswordEdit/
  │                    StartButton/WarentText/ExitGameButton + champ
  │                    « host:port » (gamedata/client.cfg)
  ├─ ChannelSelectScreen  CWorldServerListWnd.layout — ServerList
  │                    remplie (Ch01/Ch02, ip:port, état), Confirm/
  │                    CancelBtn, ItemActivated = double-clic
  ├─ CharSelectScreen  SelectCharacterWnd.layout — slots
  │                    characterBG1..5 (Name/Lv/classLv/node/race),
  │                    StartGameBtn/ReturnBtn/ServerBtn/
  │                    CreateCharacterBtn/DeleteCharacterBtn,
  │                    Left/RightTurnBtn (rotation preview),
  │                    preview 3D du perso (SubViewport 360×460)
  ├─ CharCreateScreen  panneau custom stylé : nom, race (1..5),
  │                    genre, classe (T_Class.ini 1..5), coiffure,
  │                    « Vérifier le nom », « Créer », « Retour »
  └─ → game.tscn       session attachée via SessionCarry
```

## Séquence réseau (EdenSession réutilisée — jamais reloguée)

- Login : `ConnectGatewayAsync` + `LoginAsync` (codes LoginFail
  affichés : 2=mauvais mot de passe, 6=monde down, 9=plein,
  10=générique ; erreurs socket/timeout traduites).
- Channel : `SelectWorldAsync(id)` → `WorldLoginAsync` →
  `ReceiveCharacterListAsync` (retour char→channel : `DisconnectWorldAsync`
  puis nouveau SelectWorld sur la connexion login conservée).
- Sélection perso : `SelectCharacterAsync` + `GetZoneTicketAsync`
  puis `ChangeSceneToFile(game.tscn)` avec `SessionCarry.Session`.
- GameScene (`_attached`) : **pas de re-login** — ZoneConnect →
  ZoneEntry → ZoneListenLoop. Map initiale = `S{nodeId:D3}` du perso
  (plus la constante S009) ; bascule 313 inchangée.
- Mode `--solo` conservé (debug, aucune session).

## Modèle personnage (preuve)

- Le blob `Appearance[32]` de CharacterListChunk (+0x24) porte le
  **code corps** en `app[28]>>4` — miroir exact du `AppearanceBlob`
  serveur (`scripts/protocol/AppearanceCodec.cs`).
- Code → lettre kfm : switch natif `rf_game FUN_0073ddc0` @0x0073ddc0
  `{0:g, 1:b, 2:m, 4:f, 6:k, 8:l, 9:c, défaut:g}` (miroir serveur
  `MotionParser.KfmForGender`). → `assets/models/char/model/<L>.glb`.
- **Validé live** : les 3 persos `admin` sont race=1 bodyCode=1 → « b ».
- `CharVisual.BuildBody` filtre le mega-GLB (306 meshes) : visibles =
  `#BFACE` + `SB*` (corps, texture BX01AS01A « skin ») + le mesh
  cheveux `#<L>1xx` le plus proche de `hairStyle`. **HYPOTHESIS** —
  le toggling natif par item équipé n'est pas désassemblé ; le rendu
  est un « corps nu + visage + cheveux » approximatif.
- Codes corps par race/genre à la création : **HYPOTHESIS**
  `code = (race-1)*2 + (garçon?1:0)` — confirmé seulement pour
  humain♂=1. Codes hors switch → « g » (comme le natif).

## Création de personnage — NCIDs réels (canal World)

Corrigés depuis `Eden.World/Program.cs` (les 88/89/90/103/97/98 de la
demande ne correspondent à rien sur ce canal) :

| NCID | Dir | Message | Corps |
|---|---|---|---|
| 32 | C→S | CreateNameCheck | `[str nom]` |
| 4  | C→S | CreateCharacterName | `[str nom][str][u32]` |
| 5  | C→S | CreateCharacterData | `[u8 race][u8 class][u8 flag][32B app]` |
| 6  | C→S | RemoveCharacter | `[i32 id][str nom]` |
| 7  | C→S | CancelCreateCharacter | vide |
| 39 | S→C | VerifyNameResult | `[s16 code][u8 flag]` (codes : 0 ok, 3 doublon, 5 plein, 8/9 longueur, 10 illégaux…) |
| 38 | S→C | liste persos fraîche | après 5 et 6 |
| 41 | S→C | CancelCreateCharacterResp | vide |

Tests round-trip : `WorldCharMgmtMessageTests` (6 cas).

## État rendu (textures / UV / yaw / eau)

- **Textures GLB : OK** — `GltfDocument` résout les URIs externes
  relatives (`../../../textures/…` → `assets/textures/…`) en
  `AppendFromFile`. Sonde `scenes/glb_probe.tscn` (`--headless`) :
  UB124 20/20 surfaces texturées, UE013 1/2, B.glb 249/306,
  M025 2/2, B1001 1/1. Le rendu « plat » du screenshot venait
  probablement des placeholders ou du fog — pas des URIs.
- **UV** : pas de flip constaté côté conversion (textures chargées
  natives) ; non re-vérifié pixel par pixel.
- **Splat terrain** : HYPOTHESIS canaux→slots 1..4 inchangée ;
  `mount_04.png` présent sous `assets/textures/map/texture/` et servi
  en layer0 — fog color lu depuis `Sxxx.ini` phase Day.
- **Yaw** : `EntityRegistry.EdenFacingToYaw` et l'encodage MoveSelf
  (`atan2(cosψ,−sinψ)`) sont cohérents avec la convention
  (dir=(cosθ,−sinθ) XZ, forward −Z) — vérifié analytiquement ;
  MovePrediction facing↔déplacement non re-testé visuellement.
- **Eau** : tuiles kind=1 déjà en `StandardMaterial3D` bleu
  translucide + texture `water_env` si présente — repris tel quel.
- **Screenshot `--shot=<png>`** : le readback Vulkan sur cette
  machine ne sort que le clear color (l'arbre est correct — vérifié
  par dump `children=1, cur=LoginScreen`). Limitation d'environnement,
  pas du client.

## Tests / preuve live

- `dotnet build` : 0 erreur / 0 warning.
- `dotnet test tools/NetTests` : **84 verts** (64 + 20 nouveaux :
  ClientConfig, AppearanceCodec, CharModelSpec, messages création).
- `tools/NetSmoke --granular --chars` : run complet par étapes
  (session réutilisée comme dans le flux UI) → entrée zone OK,
  dump appearance validé.
- Boot headless `--quit-after` : pas d'erreur ; solo `--solo`/`--live`
  inchangés.

## Reste pour « comparable au natif »

- Subset de meshes corps = heuristique (`#BFACE`+`SB*`+hair) ;
  idéalement : visibilité pilotée par les PreviewItems de la liste +
  équipement porté (items→meshes `#B<modelId>`) + texture peau/
  visage selon skinColor/faceStyle (`%cFACES%02d?.dds`,
  `rf_game FUN_00ab8000`).
- Anims : `char/animation/<L>.kf` non convertis en GLB (seul
  l'« embedded » du modèle joue s'il existe) — `CharVisual.PlayIdle`
  cherche stand/idle sinon première anim.
- Création : pas de choix visage/peau/yeux UI (valeurs 0 envoyées) ;
  flag de création = 0.
- Persos distants (SeeChar 265) : pas d'apparence dans le paquet →
  capsule conservée (le blob arrive via d'autres NCIDs, non étudiés).
- Screenshot GPU non vérifiable ici → vérifier à la main sur session
  X fonctionnelle.
- Pas d'écran de sélection de channel « BranchList » (sous-canaux) —
  le serveur local n'annonce que des mondes plats.
