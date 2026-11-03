# M5 — flux pré-jeu (Boot→Login→Channel→Select→Create→Loading→jeu)

Code : `eden_flax/Source/Eden.Game/{Screens,Session}/`, entrée `EdenGamePlugin.cs`.

- **Pas de `EDEN_MAP`/`EDEN_UI`** → flux réseau complet (`ScreenManager.Start`).
  `EDEN_MAP=sXXX` / `EDEN_UI=<layout>` → mode direct sans réseau (captures M3/M4).
- `EdenFlow` : config `gamedata/client.cfg` (+ `EDEN_TLS_INSECURE=1`, `EDEN_HOST`),
  session unique `EdenSession` (jamais reloguée), `Run(step, job, ok, fail)` =
  async hors main-thread → `Scripting.InvokeOnUpdate`, traces `[Eden][flow]`/`[Eden][net]`.
- Écrans en contrôles Flax natifs (verre sombre + liseré doré, miroir de
  `PreGameStyle` de la référence) — pas via CEGUI : le login natif de la
  référence est lui aussi un panneau stylé. Fond = `gamedata/login_bg.jpg`.
- Loading = 5 phases réelles : ticket zone → connexion zone → entrée zone →
  carte (`MapLoaderFlax.Boot("s{node:D3}")`) → prêt ; puis `ScreenManager.Close`.
- Autopilote QA : `EDEN_AUTO=compte:mdp EDEN_AUTO_PAUSE_MS=4000` enchaîne les
  écrans (preuves `shots/m5flow-*`).
- Limites : pas d'aperçu 3D du perso, création sans choix visage/peau
  (code corps HYPOTHESIS), pas de sous-canaux BranchList, Entrée ne valide pas.
