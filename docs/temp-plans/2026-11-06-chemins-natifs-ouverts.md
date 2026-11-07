# 2026-11-06 — Chemins natifs joueur réel par système (EDENFR)

Comment UN JOUEUR RÉEL ouvre chaque système — pas les chemins de test.
Sources : `c_npc.ini`, `s001.ini`, `S_Buliding.ini`, `c_item.ini`, layouts,
code client/serveur.

## Résumé

| # | Système | Route native réelle | Existe EDENFR | Client Godot |
|---|---|---|---|---|
| 1 | Travaux VDG (6 métiers) | NPC service 12-17 (maîtres Aven 50350-50355 + NPCs bâtiments VDG) | oui | oui (CraftWindow) |
| 2 | Renforcement | Icône sac `function02/SF` → PA 13 → 432(9) ; dbl-clic pierre kind 46 ; **aucun NPC 9/10 n'existe** | oui | oui |
| 3 | LifeSkill racial | Icône sac `function06/LS` → PA 16 → 432(0x68) | oui | oui |
| 4 | Récolte VDG | Achat outil via Talk NPC 53199-53201 (S001) → S501/S502 → clic droit outil → enchant 812 | oui | partial (ItemUse générique) |
| 5 | Cube / MagicBox | Cube : icône `function07` → PA 101 → 432(0x1c). Box : **NPC 51872 service 31 → NCID 612** | oui | oui |
| 6 | Trade + étals | Trade : cible joueur → barre → NCID 36. Étals : icône sac `function05`/WorkBtn dans région 600-602 S001 | oui | oui |
| 7 | Mort/rez | Auto via 305 → ReviveMenuUI → NCID 20 type 0/1/5/6 | oui | oui |
| 8 | Item Mall | `ItemMallBtn` du FunctionMenu | oui | oui |
| 9 | Enchères | NPC 50344/50345 service 18 → NCID 430 | **oui** (corrige G36) | oui |
| 10 | Boutiques NPC | `Service <func> 3 <store>` ou `#3#N#` → NCID 304 | oui | oui |

## A. Chaîne VDG bâtiments → NPC travailleurs

`S_Buliding.ini` col6 = item contrat NPC (kind 42) → `c_item` col35 = NPC tid →
`c_npc.ini` services :

| Bâtiment | Contrat | NPC spawné | Service | Métier |
|---|---|---|---|---|
| ferme | 35002 | 53008 | 4514 13 | planting |
| mine | 35012 | 53018 | 4517 12 | mining |
| autel | 35022 | 53028 | 4520 14 | summon |
| alchimie | 35030 | 53036 | 4524 15 | alchemy |
| moulage | 35039 | 53045 | 4524 16 | casting |
| énergie | 35068 | 53074 | 4524 17 | enchanting |

**Maîtres Aven (S001)** — services publics 12-17 : 50350@(248.9,485.7),
50351@(247.2,508.6), 50352@(292.4,188.5), 50353@(279.0,498.0),
50354@(251.9,465.0), 50355@(294.7,524.6).

**Marchands de plans S001** (type 24 → 304) : 50364→70 … 50377→85.

## B. Strengthen/Combine : icônes sac, AUCUN NPC en EDENFR

Zéro `Service … 9|10` dans `c_npc.ini` (G37 confirmé) :
- **Fortify** : icône sac `function02` → `ZonePlayerAction{13}` → 432(9) →
  `strengthenwnd_na.layout` (`InventoryWindow.cs:252-255`,
  `Strengthen.cs:104`). OU dbl-clic pierre kind 46 → même PA 13.
- **Fusion** : `function01` → PA 14 → 432(10) → `combinewnd.layout`.
- Marchand de parchemins : NPC 50380 S001 @279.1,295.7 (`Service 4754 3 90`,
  `4755 3 91`) — vend l'item, pas la fenêtre.
- **Pas d'icône minimap** : `zonemapui.layout` ne contient aucun bouton SF.

## C. MagicBox : NPC 51872 (service 31, PAS d'item)

- NPC **51872** @ S001 (301.186, 523.154), `Service 101269 31` →
  `Dialogue.cs:439-446` case 31 → 612 → `magicboxwnd.layout` → NCID 257,
  close 120(31).
- Item 40052 hypothétique : absent de `c_item.ini`.
- Cube : `function07` → PA 101 → 432(0x1c) → `newmagiccubewnd.layout`, 232.

## D. Récolte VDG : outil + aire (item-driven)

60 outils kind 44 `c_item.ini`, useArea `501 502;60|61|62` :
- Pioches (aire 60) : 36819/36825/36831/37097/37118 + durables
- Gants (aire 61) : 36821/36827/36833/37099/37120 + durables
- Boules cristal (aire 62) : 36823/36829/36835/37101/37122 + durables
Enchant 812 `AttrGainCollectItem` (clé = itemId dans `c_familycollect`).

**Vendeurs S001 — Talk scripté** (`If_InventoryItem <= 0`, 1 outil max) :
- 53199 賣鎬商人 @ (494.9,444.7) — 2000→36819(lv30) … 20000→37118(lv85)
- 53200 家族手套商人 @ (498.2,440.5)
- 53201 源力水晶商人 @ (501.5,436.0)

Route : entrer VDG → aire type 60/61/62 → dbl-clic outil → NCID 19 →
serveur vérifie l'aire + enchant 812 → rolls jusqu'à durabilité 0.

## E. Boutiques NPC — syntaxe + types

`Service <FuncId> <type> <param>` ou compact `#<type>#<param>#`.
Dispatch (`Dialogue.cs:235-272`) : 1/2→equip 304 ; 3/4/23/26/29→items 304 ;
24→plans 304 ; 5→famille(347) ; 6→banque(357) ; 7→courrier(399) ;
8→transport(393) ; 9/10→432 ; 11→483 ; 12-17→496 ; **18→enchères(430)** ;
19→stock(507) ; 21→ville(501) ; 25→514 ; 31→612 ; 35→564 ; 50-54→539 ;
56→585 ; 58→602 ; 59→579 ; 60→617.

S001 : 50342 @393.5,468.5 (4 stores) ; 50380 parchemins ; 50214/50215
marchands base ; 50362/50363 aventure ; 50340 @477.2,468.7 (services 5/21/25) ;
50344/50345 enchères (service 18). **S007 : aucun shop fonctionnel** — les
marchands 50049-50059 sont décoratifs ; seul service : 50055 instance_dialog.

## F. Étals — régions S001

`[position]` col8 : **600** @(246.9,395.9) ; **601** @(437.2,343.4) ;
**602** @(321.6,341.6). Client : `MinimapPanel.StallRegionAtSelf`.

## G. Inventaire function bar — ordre natif

Natif (`inventorywnd_na.layout`) : 01 Combo · 02 SF · 03 Option · 04 Collect
· 05 Shop(étal) · 06 LS · 07 MagicCube · 08 SpecialCoin · 09 Carve.
Client : Collect, SF(13), Combo(14), LS(16), MagicCube(101), Option, Shop,
SpecialCoin, BO — ordre différent, Carve absent.

## Gaps à documenter non-proven-live

1. Parcours VDG end-to-end (hall → contrat → NPC spawné → 496).
2. Enchant 812 session récolte (durée, ticks, cast 407).
3. Level-gate enchères (texte 6025 ?).
