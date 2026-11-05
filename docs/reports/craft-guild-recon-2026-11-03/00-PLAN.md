# Repérage CRAFT + GUILDE — plan d'action (2026-11-03)

Objectif : cartographier **tout** le système craft + guilde (fonctionnel ET
visuel) pour reconstruire les HUD/panels à l'identique du client original.

Périmètre demandé : crafts raciaux, ville de guilde (VDG), NPCs de VDG
(vendeurs + crafteurs), mine & farm (outils, plans, gains), bâtiments VDG
(niveaux, choix), fusions, fortification, items annexes (pierres de sécurité,
Gathering Scroll -95 %, boules de cristal…), niveaux de guilde/bâtiments et
leurs moyens d'augmentation, NPCs crafteurs à bonus (échec↓ / rendement↑),
toutes les voies d'obtention de ressources (mobs, NPC/panels, fame shops,
HdV, plans mine/farm/summon, pioches, gants, boule de cristal).

## Vocabulaire pivot (vérifié)

- **Guilde = "Family"** dans tout le code : `lapis::CFamily*` (zone),
  `c_family*.ini`, layouts `family*.layout`, `FamilyHandlers.cs`.
- **VDG = "Family Town"** : `CFamilyTown*`, `CFamilyBuilding*`,
  `familytownlistwnd.layout`, `c_buliding.ini`, `4PHouse.ini`, `decorate.ini`.
- **Récolte/craft plans = "Work"** : `lapis::CWorkManager/CWorkItem/
  CWorkOperation`, `worklistwnd/workresultwnd.layout`, NCID `PlayerWork`,
  `EnterWorks`, work levels 1-12.
- **Craft racial = "LifeSkill"** : `c_lifeskill.ini`, `lifeskillitem.layout`,
  `engineeringstudy/jewelrystudy/jobglyph.layout`, NCID `LifeSkill`.
- **Fortification = "Strengthen"** : `lapis::CStrengthenOp`,
  `strengthenwnd*.layout`, NCID `StrengthenItem`, `c_equiptrain.ini`(?),
  pierres de sécurité.
- **Fusion = "ItemCombo"** : `lapis::CItemCombo`, `GameData::CItemComboQuery`,
  `c_itemcombo.ini`, `combinewnd.layout`.
- **Enchants** : `c_enchant.ini` + `t_enchant.ini`, `EnchantService` (hub
  graphe), `enchant-tables.md` déjà décodé (bug row.Length==60 vs 61 connu).
- **Magic Box / Arcane Box** : `magicboxwnd.layout`, `ExecuteMagicBox`,
  `magiccube*`, `newmagiccube*`.

## Vagues d'analyse (un rapport .md par agent dans ce dossier)

| # | Agent | Domaine | Sources principales |
|---|-------|---------|---------------------|
| K1 | métier | Crafts raciaux (5 races) | wiki-tables/R/, wiki-fr, guides-fr artisanat-*, wiki-en |
| K2 | métier | Guilde + VDG (création, niveaux, bâtiments, NPCs, taxe) | misc/Useful_Info, guides-fr, decorations-guilde (105 pages EN) |
| K3 | métier | Récolte : mine/ferme/autel/summon, outils, work levels | M/Gathering_Tools, EN Mining/Farm/Summon Material |
| K4 | métier | Fortification, fusion, enchants, Arcane Box | guides-fr le-craft, C/*, M/Arcane_Box, ref-shots §5/§12 |
| K5 | métier | Économie d'obtention (fame, HdV, monnaies, shops) | misc/Useful_Info factions Aven, monnaies-et-devises |
| D1 | corpus | Tables craft/racial/fusion/fortif | c_lifeskill, c_itemcombo, c_enchant, c_equiptrain, c_itemmall |
| D2 | corpus | Tables guilde/VDG/récolte | c_buliding, GuildFunction, c_family*, FarmEvent, 4PHouse, decorate, npclist, c_npc, c_store |
| D3 | corpus | Items craft/récolte (pioches, gants, plans, pierres…) | c_item+t_item + artefacts TSV |
| D4 | corpus | Layouts craft/forge/fusion (~20) | uidata/layouts work*, strengthen*, magiccube*, combinewnd… |
| D5 | corpus | Layouts guilde/commerce/npc (~45) | family*, shop*, auction*, hotel*, npctalk, tradewnd, itemmall |
| D6 | visuels | ref-shots PNG + imagesets + guides-fr/images | ref-shots/, catalog, uidata/imagesets |
| R1 | décompilé | Zone native : CFamily*/town/building/storage/battle | re/reindex, seeds /tmp/recon-seeds/family-symbols.txt |
| R2 | décompilé | Zone native : CWork*, LifeSkill, Strengthen, ItemCombo, MagicBox, Vendor | seeds work/lifeskill/strengthen/itemcombo/magicbox/vendor |
| R3 | décompilé | Client natif UI (fenêtres craft/guilde) | client_game strings, rf_game, limitless.c |
| S1 | serveur | C# : craft/work/enchant handlers+parsers+repos | Eden.Zone/Handlers, Eden.Core/Data/Parsers, FEATURE-MATRIX |
| S2 | serveur | C# : guilde/family/town handlers+repos+DB | FamilyHandlers zone+world, SocialRepositories |
| S3 | serveur | C# : commerce (vendor, auction, trade, itemmall) | ShopHandlers, TradeHandlers, ItemMallRepositories |
| G1 | client | Godot : fenêtres existantes craft/guilde/commerce | scripts/hud/*Window.cs |
| G2 | client | Godot : protocole NCID craft/guilde/work/shop | scripts/protocol/Zone*.cs |
| G3 | client | Godot : HUD/entrées/tooltips/pipeline icônes | hud/, ui/, game/ |

## Sortie attendue par agent

Rapport `NN-slug.md` dans ce dossier : faits cités `fichier:ligne` (ou
`binaire @ 0x<addr>` + symbole), marqueurs [VERIFIED]/[HYPOTHESIS]/[GAP].

## Synthèse finale

`SYNTHÈSE.md` : matrice complète métier×données×natif×serveur×client×visuel,
état implemented/partial/missing, plan d'action ordonné de reconstruction.
