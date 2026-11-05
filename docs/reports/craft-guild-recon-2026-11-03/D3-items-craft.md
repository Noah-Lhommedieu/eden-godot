# D3 — Inventaire items craft / récolte / guilde (c_item + t_item, EDENFR)

Source primaire : `docs/reference/items.tsv` (généré depuis `corpus/fr-full/data/db/{c_item,t_item,c_itemmall,t_itemmall}.ini` par `tools/item-dictionary`, schéma `docs/reference/ITEMS.md`) + `t_item.ini` (CP1252, `id|nom|desc`) + `c_item.ini` (Big5 ; colonnes per `eden_server/server/src/Eden.Core/Data/Parsers/ItemParser.cs:11-36,341-346`) + `c_store.ini`/`c_npc.ini`/`t_npc.ini`. Croisements : D2 (nœuds `c_familycollect`, boutiques), K4 (fortif/fusion/enchant), K5 (monnaies).

Kind utiles (col 8 de `c_item`, libellés `t_textindex`) : 44 Objet · 46 Parchemin · 49 Matériel de Fusion · 50 Agricole · 51 Minier · 52 Invocation · 53 Alchimie · 54 Fonderie · 55 Mare Magique · 56 Formule d'Enchantement · 58 Parchemin de compétence · 41 Bâtiment · 42 PNJ · 69-76 mobilier. Emplacement équip. **32 = « Outil de récolte »** (139 items total ; `items.tsv` col 11).

---

## (a) Outils de récolte — pioches / gants / boules de cristal [VERIFIED]

Tous kind 44, emplacement 32, usage clic-droit à la mine/potager/source d'énergie **de la VDG ou des mines d'Eden** (desc `t_item.ini:65339+`). Restriction d'aire en `c_item` col 32 (`ItemParser.cs:341-346` : nodeIds;subType 60=mine, 61=champ, 62=champ d'énergie).

| Ids (blanc/vert par paire) | Noms FR | Niv. req. | Vente |
|---|---|---|---|
| 36819 / 36820 | Pioche / Pioche en Titane | 30 | Phio (PNJ 53199) **2000 or**, script `c_npc.ini:3424` (`If_Gold>=2000 … If_CharLevel>=30 → InventoryItem 36819`) ; boutique 679 `c_store.ini:479` |
| 36825 / 36826 | Pioche d'Expert / en Titane | 45 | Phio 5000 or, `c_npc.ini:3424` |
| 36831 / 36832 | Pioche de Maître / en Titane | 60 | Phio 10000 or, `c_npc.ini:3424` |
| 36821 / 36822 | Gants de Jardinage / Renforcés | 30 | Ri (PNJ 53200), `c_npc.ini:3426` |
| 36827 / 36828 | Gants d'Expert / Renforcés | 45 | Ri 5000 or |
| 36833 / 36834 | Gants de Maître / Renforcés | 60 | Ri 10000 or |
| 36823 / 36824 | Boule de Cristal / Divinatoire | 30 | Aidey (PNJ 53201), `c_npc.ini:3428` |
| 36829 / 36830 | Boule d'Expert / d'Expert | 45 | Aidey 5000 or |
| 36835 / 36836 | Boule de Maître / Divinatoire de Maître | 60 | Aidey 10000 or |
| 37097 / 37098 | Pioche de Gourou / (Supérieur) | 70 | `t_item.ini:65975` |
| 37099 / 37100 | Gants de Jardinage de Gourou / (Sup.) | 70 | — |
| 37101 / 37102 | Boule de Cristal de Gourou / (Sup.) | 70 | — |
| 37118-37123 | Pioche/Gants/Boule « Perdue(s) » (+Précieux/Précieuse) | 85 | `t_item.ini:66044` |
| 29191-29193 | Pioche / Gants / Orbe de Cristal (Mines d'Eden, potager, Source) | 80 | `t_item.ini:39506-39520` |

**Bleus itemmall** (q Bleu, `tables_source=c_itemmall+t_itemmall`) : 40970-40978 (Parfait n30 / Expert Parfait n45 / Maître Parfait n60, 3 types ×3), doublons 41015-41023, 42104-42106 (Super … de Gourou n70), 43579-43580 « (sans nom) » n70, 43850-43852 (Bonne Pioche/Gants/Boule Perdue n85), 48208-48213 (CN Testing, sans nom FR) [VERIFIED items.tsv].
Vendeurs : `t_npc.ini:3585,3587,3589` = « Marchand de Pioches **Phio** », « Vendeur de Gants de Jardinage **Ri** », « Marchand de Boules de Cristal **Aidey** » (type Récupération/Production). [VERIFIED]
Outils de craft racial (non récolte) : 22015-22032 & 42418-42435 (Agenda/Marteau d'Arthur/Parchemin Arcanum/Pierres de Sagesse/Rapport Marché/Aiguille d'Argent/Minerai Enchanté/Kit Œil Ambré/Journal Esprits — paires base/amélioré) [VERIFIED].

## (b) Accélérateurs de récolte [VERIFIED]

| Id | Nom FR | Effet (`effet_usage`) | Source |
|---|---|---|---|
| 40414 / 40415 | Parchemin de Récolte Instantanée | « Réduit le temps de récolte de **95 %** » | itemmall |
| 43315 / 43316 | Parchemin de Vitesse de Récolte | « Temps de récolte divisé par deux » (−50 %) | itemmall |
| 40078 / 40096 | Aide à la Récolte / (NE) | « taux de réussite de la Récolte +**8 %** » | itemmall |
| 22418 | Super Fertiliseur | « peut nourrir toutes les cultures » `t_item.ini:12781` | c_item+t_item |

## (c) Plans de récolte = « graines de nœud » (catalyseurs `c_familycollect` col2, cf. D2 §3) [VERIFIED]

Trois familles × rareté (* → *******, qualité blanc→doré), nom = « <Famille> Nv. <carte> Travail Nv. <work> », desc = « Récolte/Production/Butin : <produit> ×qté » :

| Famille | kind | Plages d'ids | Paliers |
|---|---|---|---|
| **Culture** (ferme, « Cultivation Secret ») | 50 | 30136-30190 (55) ; 22038-22065 (28) ; 36198-36225 (28) ; **34670 Culture Secrète Élémentaire** (tutoriel) | Nv. 20-80 / Travail 1-12 |
| **Gisements** (mine, « Ore Location Map ») | 51 | 30191-30245 (55) ; 22066-22093 (28) ; 36226-36253 (28) ; **34671 Carte de Gisements Élém.** | idem |
| **Incantation** (autel, « Summoning Scroll ») | 52 | 30246-30293 (48) ; 22094-22117 (24) ; 36254-36277 (24) ; **34672 Parchemin d'Invocation Élém.** | idem |

`items.tsv` ids cités : `t_item.ini:40503` (30136), `:56511-56513` (34670-72), `:11686` (22038), `:61926` (36198). Variante nom « Vente de Semences » (22052+). Produits nœud 65/70 = 36278-36296 (Séquoia à Pois/en Sang, Coton de Soie Rouge/de Cerf, Orchidée à Clochettes, Fleur Lunaire, Fruit Polaire/Émeraude, Vigne Pourpre/Tigrée, Racine Hallucinogène, Graine Kaia, Platine Tempête de Glace/de Feu, Alliage Inuit/Argenté, Diamant Étincelant/de la Crique Étoilée, Cristal des Sables Brûlants — 19 « Objet ») [VERIFIED].

## (d) Blueprints craft équipement [VERIFIED]

| Plage | Contenu | kind | Détail |
|---|---|---|---|
| 30297-30596 | **produits-armes** (les « croquis » = noms d'armes) | kind de l'arme | 300 items, 16 types (Poignard 20 … Katana 16), q Orange/Doré, effet en `effet_usage` (`items.tsv` ; `t_item.ini:41791`) |
| 30597-31095 | produits : Armure Lourde/Légère/Robe 80 ch., Collier/Anneau 36 ch., Retour 28, Gemme 48, Formule d'Enchantement 48 (résultats), Objet 63 | — | comptage `items.tsv` |
| 31096-31663 | **« Formule : <nom> »** blueprints | 54 Fonderie | **568** items (task indiquait →31615 : la plage utile court à 31663), nom = produit ; mapping 1:1 produit→formule ≈ +2799 (ex. 30297 ↔ 31096 `t_item.ini:42704`) |
| 31664-31894 | « Formule : … » | 55 Mare Magique 120 (anneaux/colliers/capes, `t_item.ini:48352`) + 53 Alchimie 111 (potions/cuisine : recettes consommées par `c_familycombine` cat.3, cf. D2:40) | 231 |
| autres plages k54 | blueprints par palier (sets, arena, boss) | 54 | 20001-20006, 20336-20572, 21114-21228, 21704-21778, 22476-22855, 24652-24876, 27093-27544, 27835-28439, 28701-28745, 35970-36181, 37614-37658 (compté `items.tsv`) |
| 35071-35086 | **Town Blueprints** | 41 Bâtiment (14) + 42 PNJ (2) | `Plan : Salle du Chef (Village)/(Mont.)` 35071/35073, `Hôtel Brique/Magique/Bunker` 35075-77, `Marché ×3` 35078-80, **`Plan : Ferme` 35081, `Mine` 35082, `Autel` 35083, `Labo d'Alchimie` 35084, `Fonderie` 35085, `Mare Magique` 35086** (`t_item.ini:57634-57666`), PNJ : 35072 Marchand d'Enchantements, 35074 Marchand d'Artisanat |

## (e) Fortification [VERIFIED]

| Ids | Nom FR | Rôle |
|---|---|---|
| 40002-40010 | Parchemin de Garde Nv. 20→100 | kind 46, matériau fortif armures (`c_familycombine` 11-20 ; K4 §2) |
| 40011-40019 | Parchemin de Puissance Nv. 20→100 | kind 46, fortif armes (combine 1-10) |
| 40192 / 40193 | Parchemin de Garde/Puissance **(Exceptionnel)** | haut renfort : refusés si cible ≤+9 (natif 0x9d00/01, K4:20) |
| 40037 / 40097 / 40176 | Pierre de Sécurité (/NE) | anti-échec de base |
| 40038 / 40177 / 40922 | Pierre de Sécurité **Fétiche** | +5 % amélioration |
| 40173 / 40178 / 40472 | Pierre de Sécurité **Alcyon** (Halcyon) | « +100 % » |
| 40174 / 40179 / 40471 / 41469 | Pierre de Sécurité **Sublime** | « +100 % » |
| 42127-42147+ | Pierre de Sécurité/Fétiche/Alcyon/Sublime **de Feregal/Hurlevent/Noireflamme** | variantes liées aux sets d'armes éveillées |
| 40172 | Corona's Heartflame | « Parchemin d'Amélioration : +100 % » (booster de parchemin) |
| 40051 | Elixir de Protecteur | « Taux de Réussite d'Artisanat » |
| 40082-40086, 40185-40186 | Essence de Couronne Nv. 35-70 | « Taux de Réussite de Fusion +25 % » |
| 40194-40197 | Parchemin d'amélioration / parfaite amélioration des Stats d'Équipement | transfert de niveau d'amélioration |
| 22626 | Parchemin de Réinitialisation d'Amélioration | reset |
| 40022-40024, 40149, 40180-81, 40282, 41126, 42098-99, 42564, 43838-39 | Ciseau d'Ezur/Eternal Nv. 30-90 | protection casse (chisel) |

**Boosters kind 8002 (0x1f41-48)** : slot « chance » de `StrengthenItem` — [VERIFIED K4:20] « aucun n'existe dans cette version des données » ; confirmé : aucun `type_code` ≥8000 dans `items.tsv` → [GAP données].

## (f) Enchants (kind 56 « Formule d'Enchantement », 614 items) [VERIFIED]

Par slot (préfixe du nom, compté `items.tsv`) : **Arme à 1 main** 43+5 élémentaires, **Arme à 2 mains** 34+2+3 élém., **Anneau** 35+5+2, **Collier** 30+5+3, **Trophée** 30, **« Tête »/« Main »/« Jambe »/« Corps »/« Taille »** ~24+12 (variante typographique) +3-4 élémentaires chacun, **« Main Secondaire »** 15+6+3 élém., **« Bouclier »/« Arme »** 4+4, **Étoffe Enchantée** (trophée, mats) 35 dont 20289-20329 « Gardien/Attaquant/Sniper/Prêtre/Sorcier » Nv.1-3 ; tests « Enchantement Partiel ». Plages : 20289-20329, 20894-20901, 21652-21677, 22459-22475, 22979-22986, 23431-23474, 24371-24375, 24957-24964, 25993-26036, 26745-26752, 26826-26869, 27749-27753, 28920-29070, 29134-29138, 30985-31032, 34405-34436, 34685-34692, 36355-36436 zone 36460-36630 (126+42), 36758-36765, 39232-39257. Kind 79 = enchant costume (8 items, surtout « test » 39142-39148). « Parchemin entier/endommagé/décodé » = kind 58 (parchemins de compétence de classe, 611 items : 23103-23120, 25026-25319, 25633-25920…) — **pas** des enchants partiels [VERIFIED].

## (g) Matériaux [VERIFIED]

- **Récolte ferme+mine 30001-30060** (60 « Objet » `t_item.ini:40090+`) : bois/coton/herbes/fleurs/fruits/vignes/graines/racines (30001-30030 : `c_familycollect` type 0) puis minerais/cristaux/diamants/alliages/fossile/corail (30031-30060 : type 1, D2:30).
- **Invocation/autel 30061-30108** : Énergie Perdue/Emmêlée/Agitée/Cinétique (30061-64), Fourrures ×7 (30068-74), Runes ×3 (30075-77), Esprits ×4 (30081-84), **Pensées** ×4 (30088-91), **Cendres** ×2 (30096-97), **Cœurs** ×3 (30098-30100), **Sangs** ×5 (30101-105), **Noyaux** ×3 (30106-08), Moule des Âmes (30131).
- **58901-58929, 56711-56715, 56821-56825** (autel/cristal « 神秘水晶礦床 », `c_familycollect` type 2, D2:30) : **[GAP] aucune ligne `c_item`/`t_item`** — 60 produits référencés sans définition (`ITEMS.md:285,291`) ; octroyés par table/collecte.
- **Compos raciaux** (palier 30-110, détail K1) : filatures (bobines coton→soie divine), minerais raciaux p. ex. Minerai Écarlate 549, Minerai Doré 21983, Minerai de Cristal Abscons 21984, Minerai azur 26632, Minerai de Jade 28888… ; énergies/gemmes élémentaires k45 (576 Gemmes) — table complète hors scope D3 (K1 §compos, `c_lifeskill.ini`).
- **Gemmes/monster stones** : 576 items kind 45 (« Gemme … Niv. X », p. ex. sanguines 22401-22407) ; pierres spéciales Humain : Cristal d'aventurier/du maître/arc-en-ciel/chasseur de trésor (K1:75).

## (h) VDG — items-PNJ, reliques, déco [VERIFIED]

| Plage | Contenu |
|---|---|
| 35001 | Clerc de la Ville (info ville) `t_item.ini:57316` |
| 35002-35010 | ferme : Fermier Novice/Fermier/Maître Fermier, Marchand Agricole, Botaniste, Marchand Vétéran, Expert Botaniste, Marchand Spécialisé, Maître Botaniste |
| 35011 / 35021 / 35029 | Monsieur Mystère ×3 (q Orange) |
| 35012-35020 | mine : Apprenti Mineur→Maître des Minéraux + marchands |
| 35022-35028 | autel : Invocateur→Maître Invocateur + marchands |
| 35030-35040 | alchimie : Apprenti→Maître Alchimiste, Sertisseur/Pharmaceutique, Maître(-Vétéran) du Moulage |
| 35041-35067 | fonderie : Forgeron Renommé + 26 « Créateur d'Armes/… » (1 par type d'arme + 3 armures, doublons 35046-48↔65-67) |
| 35068-35070 | mare : Artisan Novice/Artisan/Maître Artisan |
| 35071-35086 | plans bâtiments + 2 PNJ marchands (cf. (d)) |
| **23817-23842** | **26 reliques kind 42** : Cristal d'Eternal, Obélisque d'Enchantement, Fleur de Rikerine, Arbre de Verre Sacré, Statue Scellée, Mystère Céleste, Arche d'Alliance, Porte de la Sagesse, Statue de Déesse Guerrière, Graal du Prophète, Trône Noir, Crâne de Dragon Terrifiant (q Vert 23817-28) puis versions Bleu 23829-40 + Arbre de la Paix, Tour Citrouille (`t_item.ini:19372` ; versions NPC 53083-53106 « Relique de Guilde » per D2:64) |
| kinds 69-76 | déco maison : 273 items (Petit Meuble 72, Grand Meuble 63, Déco Murales 88, Tapis 18, Papier Peint 8, Sol Salon/Chambre 10/12, Suspendues 2) |
| 32684-32687 | « Secrets de Guilde 1-4 » (Objet) |
| deed terrain | **[GAP]** aucun item « acte/titre/parcelle » localisé (accès VDG déjà [GAP] D2:108, NPC 50604 « Agent Immobilier » suspecté) |

## (i) Monnaies [VERIFIED]

| Monnaie | Item(s) | Note |
|---|---|---|
| Eden Crystal | 40241, 40242 (Bleu), 40243 (Or), 40175 (NE), 20982 Fragment | autel de cristal (K5 §1) |
| CCM « Crystal Cross Medal » | **37115-37117 « Médaille de Cristal : 3/6/18 packs »** (`t_item.ini:66035` : « utilisée comme monnaie d'échange ») | items octroyant la monnaie-compteur |
| Étoiles d'Honneur | item 43449 ; coffres 10/50/100/160/175/200 : 26825, 21864, 21863, 21779-81, 26321 | arène (K5) |
| Médailles d'honneur/classe | 25615 Médaille d'Honneur ; **37594-37609, 37612** Médaille du Guerrier…Chaos (16 classes, k44) | composants craft gold |
| Insignes de Combat | octroyés par Badge Heldentod 23176-78 (×10/100/1000, `t_item.ini:16738`) | Vallée de Vulcain |
| Pièces d'Eternal/Ezur | packs : 37020 (×1), 34962 (×2), 35457 (×3), 23787 (×5), 26503 (×10), 34999 (×40), 35000 (×99), 40170 (×129), 27741 (×200 Ezur) | boutique de loyauté |
| Pièces/Insignes Alpaga | 20119-20121 Insigne Cuivre/Argent/Dorée (`t_item.ini:3332`), 38780 Pièce Contrefaite, 38847 Cadeau | Joankéna |
| AP / VGN | — | monnaie réelle, pas d'item (k59 teinture mall = seul type mall-spécifique) |

## GAPs consolidés

1. [GAP] **58901-58929, 56711-56715, 56821-56825** : produits d'autel sans lignes `c_item`/`t_item` (60 ids, `ITEMS.md:285`) — noms inconnus côté items (D2 a les noms Big5 dans `c_familycollect`).
2. [GAP] **Kind 8002 (0x1f41-48) boosters de fortification** : absent des données FR (slot chance inutilisé, K4).
3. [GAP] **Deed/titre de terrain VDG** : aucun item correspondant trouvé.
4. [GAP] **Noms EN-only** : « Viridian/Lucky/Sanguine/Premium Safety Stone » ne figurent pas comme tels — le corpus FR a Fétiche/Alcyon/Sublime + variantes Feregal/Hurlevent/Noireflamme ; « Premium » ↔ Parchemin (Exceptionnel) 40192-93 [HYPOTHESIS mapping].
5. [GAP partiel] 43579-43580 (outils bleus n70 mall) et ~530 ids : nom FR vide dans `t_*` (`ITEMS.md:271`).
6. [HYPOTHESIS] « boules cristal EXP » de la tâche = Boules de Cristal d'invocation (pas d'item « EXP ball » trouvé hors Gear Upgrade/Forge Stone 40198/42276-42278/42443-45 qui donnent de l'EXP équipement).
