# Eden Client Reborn — plan et faits établis

Dossier de travail : `portage/eden_godot/` (tout reste dans `portage/`).
Source en lecture seule : `../../eden_server/reforged/EdenReforged/` (désigné `$REFORGED`).
Serveur de référence C# : `../../eden_server/server/` (lecture seule).

## Faits vérifiés (24 oct. 2026)
- Godot installé : **4.7.2 mono** (`godot`), SDK .NET 8.0.426 et 10.0.402, Blender 5.2 (sans addon NIF), Python 3.14, ImageMagick, 12 coeurs, ~80 Go libres.
- `$REFORGED/pkg/pkg.idx` : 78 233 entrées. **Toutes** déjà extraites en clair dans `$REFORGED` (vérifié : 0 manquant, 0 taille différente). Donc `raw_assets/` = lien symbolique vers `$REFORGED` (ne pas dupliquer 28 Go). L'unpacker reste fourni et vérifie/extrait au besoin.
- Format index (validé par `eden_server/tools/pkg_extract.py`) : en-tête 292 o, puis records de 592 o ; +4 u32 offset, +12 u32 zsize, +24 u64 flags, +56 u32 size, +60 char[260] name2 (fichier), +320 char[260] name (dossier), +584 u32 numéro pkg. flags&0x40 ou zsize==size : brut ; sinon zlib. Chemin = name+name2, `\`->`/`, `UIData/` -> `uidata/`.
- Contenu : 56 166 `.dds` + 1 899 `.DDS` (12,8 Go), 11 547 `.nif` (3,6 Go), 4 266 `.kf` + 2 009 `.kfm`, 440 layouts CEGUI XML (`uidata/layouts/*.layout`), imagesets `.imageset`+png, `uidata/looknfeel/DejaVu.looknfeel`, `uidata/schemes/DejaVu.scheme`, thèmes `themes/{Blue,Coffee,Green,Purple,Red}/*.png`, tables `data/db/*.ini` (CSV, encodage Big5, en-tête `V.2,5,...`), scènes `data/scene`, maps `map/` (S001.. , `.area`, `.smp`, `.fsmp`, `.csm`).
- NIF : `Gamebryo File Format, Version 20.2.0.8` (2599/3000 échantillons), `20.3.0.9` (392), `20.3.1.0` (7). Aucun import NIF dans Blender -> **on écrit notre propre parseur NIF en Python** (pas de dépendance Blender) qui produit du glTF/GLB.
- Protocole : handshake = serveur envoie clé publique RSA-2048 `[u32 n_len][u32 e_len][n BE][e BE]` ; client envoie 256 o = RSA PKCS#1 v1.5 type 2 de la clé RC4 (5 octets) ; ensuite trames `[u16 len clair][RC4(u16 NCID + champs)]`. Références : `eden_server/tools/eden_headless.py`, `eden_session.py`, `mycrypt.py`, `server/src/Eden.Core/Networking/*` (`InBinStream`, `OutBinStream`, `Rc4`, `EdenCrypto`), `docs/protocole/protocol_spec.json`, `docs/generated/ncid_map.json`, `docs/generated/FEATURE-MATRIX.md`.
- Parseurs .ini serveur réutilisables comme spécification : `server/src/Eden.Core/Data/Parsers/*`, `EdenEncoding.cs`, `MyCrypt.cs`, `IniTable.cs`.

## Phases
1. Assets : unpacker, DDS->PNG, NIF/KF->glTF.
2. Données .ini -> DTOs C# ; client réseau.
3. Projet Godot 4 C#, map loader, contrôleur, interpolation.
4. UI CEGUI -> Control Godot.

## Règles
- Aucune modification de `eden_server/` ni de `$REFORGED` (lecture seule).
- Sorties volumineuses (`raw_assets`, `godot_project/assets/**`) dans `.gitignore`.
- Chaque outil : `--help`, mode `--limit N` / `--only <motif>`, rapport de stats final, reprise (skip si sortie à jour).
