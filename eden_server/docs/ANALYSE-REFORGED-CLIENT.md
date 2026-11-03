# Analyse — client Eden Reforged : connexion, bypass launcher, compatibilité

> 18 Oct. Sources : `re/ghidra/rf_game/decompiled.c`, `rf_d3d9/decompiled.c`,
> `app.asar` du launcher extrait (`/tmp/asar/launcher`), logs client
> (`main.log`, `eden_hook.log`), logs serveur C#.

## 1. Pourquoi ça ne se connecte pas

Cause principale — **le client tape encore le serveur officiel** :

- `EdenReforged/connect.ini` contenait `Server=login.edenreforged.com,6543`
  (`main.log` : `LoginServer IP: 192.99.9.193(login.edenreforged.com)`).
  → Le client ne joignait jamais notre serveur.
- **Corrigé** : `Server=127.0.0.1,6543` (backup `connect.ini.bak`).

Second point — le lancement : le client vérifie sa ligne de commande.

- `rf_game/decompiled.c` (~l.514100) : `strstr(GetCommandLineA(), "LogLog")`
  — sans cet argument, MessageBox + `return 0` (le client se ferme).
- Le launcher Electron ne fait que `spawn(game_.exe, ['LogLog'], {cwd: GAME_DIR})`
  (`app.asar/main.js:728`). **Aucun jeton/session n'est transmis** — le login
  se fait dans le jeu (écran compte/mot de passe classique).

## 2. Bypass du launcher (fait)

Script créé : `launch-reforged.sh` (racine du repo). Il fait exactement ce
que fait le launcher, en moins : `cd <game dir>` + `wine game_.exe LogLog`
avec `WINEPREFIX=<dépôt>/reforged/prefix` et le runner
Lutris `wine-ge-8-26` (fallback `wine` système).

```bash
./launch-reforged.sh
```

Pour repasser par le launcher plus tard : remettre `connect.ini.bak`
(ou laisser 127.0.0.1 — le launcher ne le réécrit pas, il ne fait que
patcher les fichiers du jeu).

### Piège résolu — « Place clal.xmf beside alaudio.dll »

`users\ster2lune` est un **symlink** vers `users\steamuser`. Le proxy
`ALAudio.dll` de Reforged (179 Ko — la vraie lib audio est renommée
`clal.xmf`) normalise son propre chemin en `\\?\X:\` puis valide chaque
composant via `GetFileAttributesW` en exigeant `attr & 0x410 == 0x10`
(dossier, PAS reparse point). Wine rapporte les symlinks comme
`FILE_ATTRIBUTE_REPARSE_POINT` → lancer depuis `users\ster2lune` faisait
échouer le check → MessageBox + audio coupé. **Fix : le script lance
depuis `users\steamuser` (vrai dossier)** — vérifié :
`Loaded "C:\users\steamuser\...\clal.xmf"`.

Au login, utiliser un compte de **notre** base `fnaccount` (`admin`,
`test123`, `test1`…) — les comptes du serveur officiel Reforged n'existent
pas chez nous.

## 3. Compatibilité protocole ↔ serveur C#

| Couche | Verdict | Détail |
|---|---|---|
| Handshake RSA→RC4 | ✅ même moteur | même `game_.exe` Gamebryo (~12,67 Mo), handshake login identique |
| Login/channels | ✅ | notre `LoginPacketHandlers` accepte n'importe quelle version (`ServerHello` → 9.9 fixe, version client ignorée) |
| Flux ticket→world→zone | ✅ | le flow `admin → ch01 → world:5200 → zone:5201` fonctionne avec le client projet ; le protocole est le même |
| NCID inconnus (features Reforged) | ✅ toléré | la zone logge `opcode inconnu` sans casser la session |
| Risque résiduel | ⚠️ | les features Reforged (itemmall, neobank, captcha…) peuvent émettre des NCID customs → ignorés côté serveur, ou attendre des réponses qui ne viendront pas (features désactivables dans `eden_patch.ini`) |

**Politique de compat** : le serveur reste piloté par le protocole du
client `client/` — **aucune adaptation serveur pour Reforged**. Si une
feature Reforged pose problème en jeu, on la désactive dans
`EdenReforged/eden_patch.ini` (`[Features]` : ~20 toggles) plutôt que de
modifier le serveur. Le client `client/` reste la référence.

## 4. À tester in-game

1. `./launch-reforged.sh` → écran de login.
2. Compte `admin` (ou autre compte fnaccount) → liste channels → **Ch01**.
3. Si « Cannot connect to server » au choix du channel → regarder
   `/tmp/eden-login.log` (`[SELECT]`/`[TICKET]`) et `/tmp/eden-ticket.log`.
4. Si écran noir après le channel → `/tmp/eden-world.log` / `eden-zone.log`.

### Bonus : auto-login par arguments

`rf_game` lit 3 chaînes supplémentaires après `LogLog` dans la ligne de
commande (`local_14c`/`local_1350`/`local_350` @ ~514110) — très
probablement account/password/serveur pour un auto-login. Si besoin on
peut tenter `game_.exe LogLog <compte> <mdp>` — à valider par test ou en
creusant `FUN_00602dc0` (split des args).
