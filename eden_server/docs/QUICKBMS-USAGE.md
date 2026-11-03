# Utilité de QuickBMS

**Verdict : non utilisé pour l'instant, conservé pour extraction future.**

## Ce que c'est

`quickbms/` contient QuickBMS 0.8.3 (Windows) + le script `aeriagames.bms`
(script 0.2.2 couvrant Eden Eternal, Aura Kingdom, Kitsu Saga…).

Le script décortique le format d'archive **Aeria Games `.pkg`** :
- `pkg.idx` = index (offsets, tailles, CRC32, timestamps, noms)
- `pkgNNN.pkg` = données (blobs zlib ou bruts)

## État actuel

- ✅ Le dossier `client/` contient **déjà** les fichiers extraits
  (`C_Class.ini`, `C_Spell.ini`, `C_Item.ini`…) en **Big5 en clair** —
  le même schéma que les `S_*.ini` serveur décodés.
- ❌ Aucun fichier `.pkg` / `pkg.idx` présent dans le repo → QuickBMS
  n'a rien à extraire aujourd'hui.

## Quand l'utiliser

Si un client Eden Eternal complet (ou un patch) est téléchargé avec ses
`.pkg`, lancer :

```bash
wine quickbms/quickbms.exe quickbms/aeriagames.bms pkg.idx client_extracted/
```

Cela permettra de récupérer :
- Les assets (modèles `.nif`, textures, sons)
- Les fichiers client `C_*.ini` **à jour** (utile pour vérifier que nos
  `S_*.ini` serveur correspondent à la version du client — les IDs et
  les colonnes doivent être identiques)
- Les layouts UI (`UIData/`, `UI/`), les fichiers de scène (`data/scene/`)

## Note : comparaison client/serveur

Les fichiers `client/data/db/C_*.ini` sont en clair et correspondent aux
`S_*.ini` serveur (mêmes headers `|V.x|N|`). On peut les parser avec
`IniTable` **sans passer par `MyCrypt.DecodeFile`** — vérifier le
premier octet : si `0x3C` (`<`), c'est du `<encode>` chiffré ; sinon c'est
du plaintext direct.
