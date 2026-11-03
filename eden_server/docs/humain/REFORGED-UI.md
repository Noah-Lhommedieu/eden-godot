# Modifier l'UI du client Reforged

> Sources : `re/ghidra/rf_d3d9/decompiled.c` (le hook `d3d9.dll`),
> `rf_neobank/`, `rf_gamefix/`, `rf_itemmall/` (DLL de mods déjà
> décompilées), `EdenReforged/eden_patch.ini`. Cible : ajouter,
> modifier ou supprimer des panneaux **en jeu**, y compris pour des
> concepts nouveaux.

## Comment l'UI fonctionne dans Reforged

L'UI est **CEGUI** (fichiers XML `.layout`, `.looknfeel`, `.scheme`,
`.imageset`, `.font` + images). Dans le client vanilla `client/` ces
fichiers sont en clair dans `UIData/` ; dans Reforged ils sont
**packés dans `pkg/pkg001-010.pkg`** et servis par une couche de
fichier virtuel, **pkgfs**, embarquée dans le hook `d3d9.dll`.

pkgfs intercepte la factory d'assets du jeu
(`d3d9 @ 0x6d21c…`, hook posé par patch d'un saut rel32 à RVA
`0x4e1c80`) et sert chaque ressource depuis, par ordre :

1. les **fichiers en clair** : `<dossier du jeu>\uidata\<chemin>`
   (dossier `uidata\` en minuscules à côté de `game_.exe` — pas
   `UIData\` : le nom en clair est `uidata`, cf. `uiclean_walk`
   qui supprime l'ancien `UIData/` au premier démarrage) ;
2. l'**archive pkg** (`pkg/pkg.idx` + `pkgNNN.pkg`, blocs zlib) ;
3. les **replis embarqués** : des assets compilés en dur dans les
   DLL (`eden_pkgfs_register_fallback_part_0` — scheme, looknfeel
   et polices de démarrage sont déjà servis ainsi par d3d9).

## Les 3 voies de modification

### 1. Remplacer un fichier UI existant — `mode=loose`

Dans `eden_patch.ini` :

```ini
[pkgfs]
mode=loose
```

Alors `_g_archive_first = 0` → priorité « clair puis archive »
(`d3d9` : `_g_mode=2`). Tout fichier déposé dans
`EdenReforged\uidata\` avec **le même chemin relatif** que dans le
pkg remplace l'original :

- **Modifier un panneau** : extraire le `.layout` correspondant
  depuis le pkg (ou le copier depuis `client/UIData/` du projet,
  qui a les mêmes layouts vanilla), l'éditer, le poser dans
  `uidata\` → actif au prochain lancement.
- **Supprimer un élément** : éditer le `.layout` pour retirer la
  fenêtre enfant, ou poser `Visible=False` sur le widget.
- **Textes/images** : remplacer le `.looknfeel`/`.imageset` +
  le `.tga`/`.dds` référencé.

Autres modes utiles (`GetPrivateProfileStringA("pkgfs","mode")`) :
vide/absent = auto (archive prioritaire si présente), `pack` =
archive seule. `archive_scope=<nom>` limite le périmètre servant
l'archive (masque de scopes logué au boot).
`trace=1` logue chaque résolution dans `eden_hook.log` — **à activer
pour savoir quel chemin exact le client demande** pour une ressource.

### 2. DLL de mod — `mods/*.dll` + export `RfgModInit`

C'est la voie utilisée par les auteurs de Reforged pour les
**nouveaux concepts** (NeoInventoryBank = inventaire/banque fusionné,
ItemMall refait, gameFix = correctifs) :

- Déposer la DLL dans `EdenReforged\mods\` (les leurs vivent dans
  `mods\_testing\`). Le hook la charge et appelle l'export
  **`RfgModInit`** (bool) — `NeoInventoryBank.dll` exporte aussi
  `NeoInit`/`NeoShutdown`.
- La DLL patche des **sites précis** de `game_.exe` en mémoire
  (remplacement de buffers CEGUI, hooks de fonctions UI, ajout de
  boutons). Exemple : `rf_neobank` embarque un **layout CEGUI
  complet en string** et redirige la fenêtre inventaire dessus.
- Un mod peut aussi enregistrer des **assets de repli** dans pkgfs
  (`eden_pkgfs_register_fallback_part_0`) → servir un `.layout`
  **nouveau** sans toucher aux pkg.
- Les features se tolèrent : chaque mod vérifie si le site est déjà
  patché (« modifié par un autre module : laissé tel quel »,
  `uiRemake.dll` détecté → l'invbank se désactive proprement).
- Les sources de référence sont déjà décompilées :
  `re/ghidra/rf_neobank/`, `rf_gamefix/`, `rf_itemmall/` —
  `RfgModInit` @ `0x10009be0` montre l'init complète (InitOnce,
  enregistrement des sites, gestion du shutdown différé).

C'est la seule voie pour un **panneau entièrement nouveau** (nouvel
onglet, fenêtre custom branchée sur un NCID custom) : le layout se
sert en repli pkgfs, la logique d'ouverture se patche dans le
client, le paquet de données se gère côté serveur.

### 3. Features déjà câblées — `eden_patch.ini [Features]`

Avant de coder, vérifier que le besoin n'est pas déjà un toggle :
`inventory_bank`, `booth_expand`, `monster_stats`, `gear_swap`,
`item_support`, `captcha_qol`, `auto_altar`, `certificate_slots`,
`accessory_slot`, `auction_tax`, `effect_filter`, `camera_zoom`,
`auto_reconnect`, `auto_sell`, `chat_emoji`, `repair_all_btn`,
`skillbar_ext`, `font_select`, `time_display`, `heal_stats`,
`strengthen20`, `arena_1v1`, `bulk_use`, `pet_limit`, `pay_all`,
`collect_all`, `guild_warehouse_expand`, `bag_multi_delete`,
`glyph_pages`, `go_to`, `hide_effect`, `buff_panel_resize`,
`extend_bag_slot`, `npc_chatter`, `packet_logger`, `crash_reporter`,
`jump_feature`, `archive`, `btn_max`.

`[inventory] columns=20` / `[bank] columns=20` montrent que même la
géométrie des sacs est configurable.

## Recette par cas d'usage

| Besoin | Voie |
|---|---|
| Retravailler un panneau existant (couleurs, disposition, texte) | `mode=loose` + `.layout`/`.looknfeel` modifié dans `uidata\` |
| Cacher un bouton/onglet | `mode=loose` + `Visible=False` dans le `.layout` |
| Nouvelle fenêtre simple réutilisant un événement UI existant | `uidata\` + souscription CEGUI dans le layout parent |
| Nouveau concept complet (données serveur, fenêtre custom) | DLL de mod `mods\` : `RfgModInit` patch + assets repli pkgfs ; NCID custom côté serveur |
| Feature déjà prévue par Reforged | `eden_patch.ini [Features]` |

## À savoir / pièges

- **`uidata_cleaned`** : au premier boot, pkgfs supprime l'ancien
  dossier `UIData\` (cleanup « same folder clients »). Ne rien y
  mettre — le dossier override s'appelle `uidata\`.
- Le dossier en clair est relatif au **`game_.exe`** — chez nous
  `EdenReforged\uidata\`.
- `mode=loose` sans fichier en clair = comportement vanilla
  (archive), pas de risque.
- Un `.layout` mal formé fait planter le chargeur CEGUI —
  `CEGUI.log` liste l'erreur XML ; toujours lire en cas d'écran
  vide.
- Wine/Lutris : les mods sont des DLL Win32 natives — elles se
  chargent comme les autres via le proxy `d3d9.dll`, rien de
  spécial côté Wine.
- Protocole : un panneau custom a besoin d'un NCID custom — à
  implémenter côté serveur C# dans un lot dédié (le client vanilla
  ignorera ce panneau, c'est le prix du dual-client).
