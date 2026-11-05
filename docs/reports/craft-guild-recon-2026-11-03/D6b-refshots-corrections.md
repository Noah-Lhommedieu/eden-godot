# D6b — Corrections ref-shots + web-shots (2026-11-04, passe 2)

Corrections suite au retour du propriétaire : (a) le lot `web-shots/` contient
des images non pertinentes — le tri était large ; seules les captures de
panels listées dans `web-shots/MANIFEST.md` §« vérifiées » servent de spec ;
(b) la série `ref-shots/shot_*` contient davantage de panels utiles que le
rapport D6 ne l'avait crédité — re-vérification visuelle ci-dessous.

## shot_* série — contenu vérifié en lecture directe

| Fichier | Contenu réel (vérifié) | Utilité |
|---|---|---|
| `shot_0017_235743.png` | **Backpack complet** : grille géante + onglets **All/Potions/Gear/Town/Special**, or, « Archive All »/« Archive Save », barre d'icônes fonction basse | Inventaire + onglet Town + archive (récolte VDG) |
| `shot_0048_000033.png` | **Fenêtre Class** : liste classes à gauche + Transform! + onglets Class Skills/Knowledge Inventory/Class New + Reg. Skills (+1/Max) + Class Skills + 4331 CP + Class Ability CE List | Structure onglets+lignes de skills |
| `shot_0066_000211.png` | **Item Mall en jeu** : catégories (Limited Item, Featured, **Crafting Items**, Pets & Mounts, Gear, Boosts, Fashion, Colorful Dye), catalogue prix + Buy/Gift/BuyStack, badge « N left this week! », Hot Items, onglet Loyalty Shop | `itemmall` — confirme catalogue K5 §8 |
| `shot_0070_000233.png` | **Pet List** : liste 0/30 + Pet Info (stats ATK/M-ATK/ACC/CRIT/ATK SPD/Attack Attribute + Battle/Pray skill slots + Pet Gear + Rename/Feed) | `petupgradewnd` contexte |

## Depuis la planche de contact (`/tmp/refgrid/shots-all.png`)

- `shot_0018/0019` = même fenêtre Backpack que 0017 (variantes).
- `shot_0022` = panneau liste + carte (probable FindGuild/Town List variante).
- `shot_0041` = fenêtre blanche à 3 panneaux (à identifier — ressemble à un
  dialogue multi-section).
- `shot_0050-0052` = grilles d'icônes (probables fenêtres classe/pet/archétype
  de la même session que 0048).
- `shot_0056` = liste sombre à colonnes (probablement fenêtre liste type
  storage/auction/friends).
- `shot_0067` = fenêtre similaire à 0066 (autre onglet Item Mall ?).
- `shot_0077-0086` = combats terrain + petits dialogues (non craft).
- `shot_0011` = scène de théâtre/intérieur (non craft).
- `shot_0033/0036/0044/0057/0063` = méta-shots bureau Okular (confirmé D6).

## Correspondances ref-shots ↔ systèmes craft (rappel complet)

Craft/plans : `Blueprint.png`, `FusionPanel Craft…png` (Magic Pool),
`item hover (plan mine ressources).png`, `Hover Item Plans ressources.png`,
`item hover (basic ressources…).png`, `Item hover consommable avec
durability.png`, `case pour certificat.png`, `item hover (trophy enchant).png`,
`item hover (trophée).png`.
Guilde : `FindGuild.png`, `shot_0062` (Town List filtres/colonnes),
`CommunityPanel*`, `profile-fame onglet.png`.
Commerce : `Auction *.png` ×2, `ItemMallPanel*.png` ×2 + `shot_0066`,
`tradeNPCSystem.png` (paysystemwnd FR), `Mail*.png`, `npc talk*.png` ×2.
Référentiels génériques : `shot_0017-0019` (Backpack + tabs Town),
`shot_0048` (Class panel), `shot_0070` (Pet List), `daily reward.png`,
`SystemPanel.png`, `ParameterPanel-*.png` ×4.

## Reste à identifier (micro-tâche, quand besoin)

`shot_0041`, `shot_0050-0052`, `shot_0056`, `shot_0067`, `shot_0022` —
probables variantes classe/pet/liste ; à vérifier individuellement seulement
si une fenêtre craft s'avère manquer des specs ailleurs.
