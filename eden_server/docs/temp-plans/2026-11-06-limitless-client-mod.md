# Plan — Mod client LimitlessHud.dll (chantier E2 / design §5)

Objectif : rendre le NCID custom **2032** (`[i32 charId][i64 exp]
[s16 level][u16 perks]`, émis par la zone au login + à chaque gain)
dans le client Reforged — barre d'Exp Ultime ou, en V0, affichage
chat/hud simple.

## Acquis débloqués cette session

### Toolchain

- **LLVM-MinGW** portable extrait dans `/tmp/llvm-mingw-20261020-
  ucrt-ubuntu-22.04-x86_64` (re-télécharger au besoin :
  `github.com/mstorsjo/llvm-mingw` tag 20261020, variante
  ucrt-ubuntu-22.04-x86_64). `i686-w64-mingw32-gcc` compile un
  PE32 i386 valide avec exports `__declspec(dllexport)` — testé.
- `winegcc` système inutilisable (CRT manquant) — ne pas y revenir.

### Contrat du loader de mods (rf_alaudio, désormais exporté dans
`re/ghidra/rf_alaudio/`)

- `ALAudio.dll` énumère `mods\_testing` (clé ini `Hub.PluginDir`),
  LoadLibrary chaque DLL puis appelle :
  1. `RfgModInitV2` → **bloqué** (gate « native packages »,
     `FUN_10006e60` refuse le mod) — **ne pas l'exporter**.
  2. `RfgModInitEx(ctx)` — ctx 0x14 octets :
     `[u32 size=0x14][u32 1][u32 pid][u32 0][ptr param+0x1104]` —
     le dernier champ pointe probablement une API interne.
  3. `RfgModInit()` — sans args, suffit (gameFix/NeoBank l'utilisent).
  4. `RfgModPrepareExit()` — cleanup à la fermeture.
- La table d'exports PE des mods est scannée — la présence du nom
  `RfgModInitV2` fait échouer le chargement.

### Mécanique de dispatch NCID client (rf_game)

- Chaque connexion a un **receiver** global avec un
  `std::vector<command*>` : NCID = index du vecteur. Receivers
  repérés : `DAT_010c3ea4`, `DAT_010c3eb4`, `DAT_010c3ec4`
  (initialisés dans `FUN_00b5ed10` — probablement
  login/world/zone, à confirmer lequel est la zone).
- `FUN_00c35250(receiver, name, obj)` = registration helper
  (`call 0xb4e6d0` = vector::push_back) ;
  `DAT_0105fdxx`/`DAT_010c6axx` = les globales NCID écrites
  `(end-begin)/4 - 1` à chaque push.
- Objets commande : 0x20 octets, `+0` = ptr/vtable, `+4` =
  `std::string` name (vide chez les CZ), Execute = vtable[0]
  (thunks `FUN_006acf80`-family : `(*(*this))(payload)`).
- `FUN_00b4f660(ncid)` est appelée par chaque Execute (marqueur
  « commande courante » dans une file circulaire) — NE capture
  PAS les NCIDs inconnus (droppés en amont).

### Stratégie d'interception (deux options)

1. **Enregistrement** : le mod appelle lui-même `0xc35250`
   (receiver_zone, "LimitlessInfo", notre_objet) en boucle jusqu'à
   index 2032, avec objets dummy puis le vrai handler — le dispatch
   natif route alors 2032 vers nous. Nécessite : identifier le
   receiver zone + reproduire le layout objet/vtable + l'ABI du
   call Execute.
2. **Hook dispatch** : détourner le site qui indexe le vecteur
   (bounds-check + call Execute) — non localisé encore en statique
   (les `*(+4)+idx*4` greppés sont des internals std::map).

### Modèle de patch existant (rf_gamefix)

`VirtualProtect` + écriture + table de restauration — voir
`GameFixInit`/`RfgModPrepareExit` dans `re/ghidra/rf_gamefix/`.
`FUN_100010b0` y mappe RVA→VA via `DAT_10016908` (image base).

## Serveur — Plan B visible (fait)

- `LimitlessService.AddExpAsync` → `GrantResult{Exp,Level,LeveledUp}`.
- Level-up Limitless → chat système canal 6 `[Limitless] Ultimate
  Level up! Lv N reached.` (visible sur les deux clients, sans
  mod client).

## ABI de dispatch — RÉSOLU (session 2)

- **Receiver zone = `DAT_010c3ea4`** — confirmé 2 façons :
  ~621 enregistrements dans `FUN_00c93a30` ET exactement 621 NCIDs
  CZ natifs (0..620, ncid_map.json).
- **Dispatch = `FUN_00d75da0`** (déjà hooké par d3d9.dll
  `native_rx` « peek RX opcode 621 » — coexiste) :
  `this` = binding `{+0: session_adj, +4: &receiver}` créé par
  `FUN_00d75c90` ; `binding+4` → receiver ; `receiver+4/+8/+0xC`
  = vector ; `gen = vec[ncid]` ; **`cmd = gen->vtbl[1](gen,
  &stream)`** ; `cmd==NULL` → paquet ignoré proprement.
- **operator() = vtbl[1]** (thiscall, ret 4 — natif 0xc37720 :
  `new` + ctor + `cmd->Deserialize(stream)`).
- **CInBinStream client** (ABI lapis serveur @ zone:0867aa00) :
  `+8` = ptr vers `{begin,end}`, `+0xC` = pos de lecture. Le mod
  sonde ce layout + fallbacks avec validation `VirtualQuery`.
- **game_.exe** : imagebase 0x400000, `DllCharacteristics=0`
  → pas d'ASLR → adresses absolues stables.

## Mod écrit — `clientmod/limitless/limitless.c`

- `Limitless.dll` compilée (llvm-mingw, PE32 i386, -nostdlib,
  exports `RfgModInit`/`RfgModPrepareExit`, aucune dépendance CRT).
- `InitThread` attend que le vecteur du receiver soit peuplé
  (polling ≤ 60 s), puis `InstallGenerator` : nouveau buffer
  2048 entrées (slots natifs copiés, gaps = `DropGenerator`,
  index 2032 = `LimitlessGenerator`), swap atomique
  `InterlockedCompareExchange64` sur `{begin,end}` + `cap`.
  Garde-fou : n'écrase jamais un slot 2032 déjà peuplé
  (Reforged utilise des opcodes custom — 621 vu dans
  `eden_hook.log`).
- `LimitlessOperator` : parse `[charId][exp][level][perks]` avec
  plausibilité (charId ≤ 0x40000000, level ≤ 20000, perks ≤ 256)
  → retourne NULL → le dispatch ignore le paquet. Aucun objet
  commande nécessaire.
- Overlay Win32 (thread dédié, fenêtre topmost suivant la
  fenêtre de jeu) affiche `ULTIMATE Lv N exp X perks Y` ;
  `limitless_mod.log` dans le dossier du jeu pour la preuve.
- Déployé dans `mods/_testing/Limitless.dll`.

## Découverte bloquante résolue — gate connect.ini (2026-10-24)

**Aucun mod `_testing` ne chargeait** (même gameFix/NeoInventoryBank)
car `FUN_100038e0` (`ALAudio.dll @ 0x38e0`) valide `connect.ini`
par whitelist SHA-224 (`.rdata @ 0x212f0`) → notre connect.ini
local (127.0.0.1) échoue → `DAT_1002d34c=0` → scan plugins off.
Patch appliqué : `mov eax,1; ret` à 0x38e0
(backup `ALAudio.dll.bak-limitless`, doc `PATCH-CLIENT-BINAIRE.md`).

**Validation live OK** (Proton-CachyOS + `d3d9=native,builtin`) :
- 4/4 DLL `_testing` dans les maps (gameFix, ItemMall,
  NeoInventoryBank, **Limitless**).
- `limitless_mod.log` : `init (receiver=010c3ea4)` →
  `receiver vec ready: begin=… size=621` →
  `install gen@2032: rc=0` → `overlay: hwnd=…`.

## Reste à faire

1. ~~Test live chargement~~ → **fait**.
2. **Test payload live** : login + entrée zone → le serveur émet
   NCID 2032 au login → vérifier `pkt2032 charId=…` dans le log +
   bandeau HUD en jeu. Nécessite une connexion joueur réelle.
3. `//ll_exp 50` pour déclencher un gain → re-vérifier capture.
4. V2 : vraie barre CEGUI (layout dupliqué de l'EXP normale).
