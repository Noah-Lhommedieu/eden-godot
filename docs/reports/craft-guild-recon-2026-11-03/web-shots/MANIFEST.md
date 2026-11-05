# web-shots/ — captures de panels issues du corpus HTML wiki

**Origine** : `knowledge/raw/` (13 265 HTML — présent sur disque au 2026-11-04,
contrairement à ce que les premiers rapports indiquaient) référence des images
**distantes** (CDN `static.wikia.nocookie.net` + `edenfr.wordpress.com`) ;
aucune image n'est stockée en local. La quasi-totalité (~3 500 fichiers
référencés, `image-name-histogram.txt`) = icônes d'items/skills/classes
(`I00xxx`, `PA.jpg`, `*_skill_icon`, `*_class_*_icon`) — mais ~40 noms
correspondent à de **vraies captures d'UI**.

**Téléchargement** : `curl -A "Mozilla/5.0" -e "https://eden-eternal.fandom.com/"`
(le Referer est obligatoire : 403 sinon) → WebP → `magick x.webp x.png`.

## Captures vérifiées visuellement (panels natifs réels)

| Fichier | Contenu vérifié | Rapporte pour |
|---|---|---|
| `Guildinterface.png` | Fenêtre « Guild » : onglets Info/Message/Guild Order, liste Rang (Queen/Officer/Captain/Members), matrice **Rank Privileges** à cases (Use Chat/Recruit/Kick/Write Msg/Shift Rank/Post Msg/Del Msgs/Treasury/Repair/Guild WH 1), panels Guild History + Guild Events | `familyorderwnd` + `familyprivilege` — **comble le GAP « pas de ref-shot guilde »** |
| `Guildranks.png` | Même fenêtre guilde, gestion des rangs | `familyorderwnd` |
| `Shoprecipe.png` | Fenêtre « Shop » d'un Agricultural Merchant : liste plans Cultivation Sec + prix + tooltip « Crafting Level 5 / Used in Alchemy / Open Shops in the Aven or farms » | `shopwnd`/`shopitem` + tooltip plan |
| `Assign.png` | Fenêtre **« Assign Worker »** : onglets (récoltes), bouton « Add Worker », « Your Cash 50 » | Assignation NPC VDG — **comble un GAP visuel** |
| `Molding.png` | Fenêtre **« Foundry »** : slot Use Item (blueprint), Time 0 min 5 sec, Cost or+argent, slot Charm, liste **Materials Required** avec compteurs `possédé/requis` (Blueprint 1/1, Tough Branch 0/24, Cat's Eye 0/2, Terrified Thought 3/7), slot Product, Request/Close | `combinewnd`/`collectwnd` commission — **spec clé de la fenêtre de craft** |
| `Racial_crafting_button.png` | Inventaire complet : onglets All/Potions/Gear/**Town**/Special, grille qualité colorée, barre d'icônes basse avec bouton **« Racial Crafting »** (entrée LifeSkill) | ouverture craft racial (K1 §1.1), tabs sac |
| `Guildtown.png` | Vue 3D VDG **forêt** (cascade, bâtiments, rivière) | scène VDG S501 |
| `EdenEternalForet_Guilde_Ville.png` | VDG forêt (wiki-fr `Ville_de_Guilde`) | scène VDG |
| `Montagne.png` | VDG **montagne** (wiki-fr `Ville_de_Guilde`) | scène VDG S502 |
| `Backpack.png`, `Questbag.png` | sacs complets | référence inventaire |
| `Item_mall_2023-09-02.png` | Item mall | `itemmall` |
| `Eden_Warehouse.png` | entrepôt | `hotelstoragewnd`/storage |
| `Chatwindow.png` | fenêtre chat | HUD |
| `Alec_shop.png`, `Stella_shop_1-3.png`, `Lady_rainbow_dye_shop_1/2.png`, `Horn_shop_1/2.png` | boutiques PNJ | `shopwnd` |
| `Minerairy.png`, `Villageheadanrew.png`, `Villageheadschoss.png` | scènes/NPC mine+village | contexte VDG |
| `Openbackpackicon.png`, `Menuicon.png`, `Guildicon.png`, `Craftingicon.png`, `Auctioneericon.png`, `Itemmallicon.png` | icônes du menu FunctionMenu | barre de menus HUD |
| `Engineer_screenie.png`, `Engineer_stats.png` | captures classe Ingénieur | contexte |
| `Relics_Rune_Blueprint_preview.png`, `Collective_Language_Dictionary_preview.png`, `Painting_*_preview.png` | aperçus items/plans | — |

## Méthode pour en extraire d'autres

1. Noms d'images par fréquence : `image-name-histogram.txt` (complet, 3 578 noms).
2. Filtrer hors icônes (`I\d{5}|A\d{5}|W\d{5}|T\d{5}|Gold|Silver|Copper|_skill_icon|_class_.*_icon|J\d{3}|E\d{4}`).
3. Retrouver l'URL : `grep -rhoE "https?://[^\"' ]*<nom>[^\"' ]*" knowledge/raw/` (attention `&amp;` → `&` sur le wiki FR).
4. Télécharger avec le Referer, convertir en PNG.

## Limites

- Captures issues du client **EN** (wikis anglophones) — la géométrie est
  celle du client natif, les libellés à retraduire via `t_*.ini`.
- `image-name-histogram.txt` recense ~3 500 images distantes supplémentaires
  (surtout icônes) — la présente extraction couvre les panels évidents par
  nom ; un autre passage ciblé reste possible si besoin.
