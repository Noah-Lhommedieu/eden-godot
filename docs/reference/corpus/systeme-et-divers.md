# Système, launcher & divers

Fichiers racine du client d'origine : référence seulement, le client
Godot n'en a pas besoin.

| Fichier | Taille | Rôle |
|---|---|---|
| `ACDC.dat` | 927 Ko | données anti-cheat (AhnLab?) |
| `ALAudio.dll` | 1.8 Mo | audio (DLL Windows) |
| `Banner.ini` | 118 o | bannière launcher |
| `CrashRpt.dll` | 80 Ko | rapport de crash (DLL) |
| `Launcher.exe` | 5.7 Mo | lanceur Windows d'origine (référence) |
| `LauncherMod.ini` | 2 Ko | config launcher |
| `WebBrowser.dll` | 28 Ko | navigateur intégré (DLL) |
| `cltax.xmf` | 96 Ko | binaire propriétaire (index média ?, non documenté) |
| `connects.ini` | 122 o | adresses serveur d'origine |
| `dbghelp.dll` | 1014 Ko | debug Microsoft (DLL) |
| `effect/texture/s81114.max` | 4.3 Mo | max effet |
| `ftrace.dos` | 70 Ko | trace/debug DOS d'origine |
| `game.bin` | 12.0 Mo | binaire de jeu d'origine |
| `locate.ini` | 229 o | localisation/langue launcher |
| `map/model/S010/WaterVars.cfg` | 52 o | cfg carte |
| `map/model/S100/S100.txt` | 13 o | txt carte |
| `map/model/S501/WaterVars.cfg` | 52 o | cfg carte |
| `map/model/S999/WaterVars.cfg` | 52 o | cfg carte |
| `map/model/effect/convertdds.bat` | 118 o | bat carte |
| `map/model/s041/map/s041.jpg` | 55 Ko | jpg carte |
| `map/model/s041/map/s041_01.jpg` | 144 Ko | jpg carte |
| `map/model/s099/watervars.cfg` | 52 o | cfg carte |
| `map/model/s228/s228_01.bmp` | 1.3 Mo | bmp carte |
| `map/model/s228/s228_02.bmp` | 1.3 Mo | bmp carte |
| `map/model/s229/s229.bmp` | 1.0 Mo | bmp carte |
| `map/model/s229/s229_dauble.bmp` | 4.0 Mo | bmp carte |
| `map/model/s230/s230.bmp` | 3.1 Mo | bmp carte |
| `map/model/s230/s230_dauble.bmp` | 784 Ko | bmp carte |
| `map/model/s231/s231_dauble.bmp` | 5.5 Mo | bmp carte |
| `map/model/s231/s231_dauble1.bmp` | 5.5 Mo | bmp carte |
| `map/texture/pollution_01.psd` | 255 Ko | psd carte |
| `map/texture/ud50101.psd` | 605 Ko | psd carte |
| `map/texture/ud501012.psd` | 601 Ko | psd carte |
| `packver.txt` | 14 o | version du pack |
| `ride/animation/r014` | 209 Ko |  monture |
| `updatedate.txt` | 34 o | date de mise à jour |

Extensions numériques (`.949/.950/.936/.1258`) : artefacts de
patch/version du launcher d'origine.

**Conteneur source** (hors corpus, dans l'install d'origine
`~/Téléchargements/EDENFR`) : `pkg.idx` + `pkg001…pkgNNN.pkg` +
`GameDataFileList.txt` (manifeste CRC32C). Spec :
`docs/formats/{PKG-FORMAT,MANIFEST-CRC}.md`.
