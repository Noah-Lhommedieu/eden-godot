# Systèmes craft + guilde — index du repérage complet

**Statut : verified (audit statique, pas de validation in-game) · 2026-11-04 · Godot 4.8-beta2**

Repérage exhaustif des systèmes de craft et de guilde EDENFR, en 20 rapports
sourcés (`fichier:ligne` / `binaire @ 0x<addr>`) :

📁 **`docs/reports/craft-guild-recon-2026-11-03/`**

- `SYNTHESE.md` — matrice croisée + plan d'action ordonné (phases A-G). **Commencer ici.**
- `00-PLAN.md` — méthode du repérage (5 vagues × sources).
- Métier (knowledge/) : `K1` crafts raciaux · `K2` guilde+VDG · `K3` récolte/outils · `K4` fortif/fusion/enchants/cube · `K5` économie/monnaies/achats.
- Données (corpus fr-full) : `D1` tables craft · `D2` tables guilde/VDG · `D3` items · `D4` layouts craft/fusion · `D5` layouts guilde/commerce · `D6` ref-shots+visuels.
- Décompilé (re/) : `R1` natif guilde/VDG zone · `R2` natif work/craft/shop zone · `R3` natif UI client (map 621 handlers + 210 senders dans `client-ui-maps/`).
- Serveur C# : `S1` craft/enchant · `S2` guilde/VDG · `S3` commerce.
- Client Godot : `G1` fenêtres · `G2` protocole NCID · `G3` HUD/tooltips/icônes.

## Raccourcis vocabulaire (pièges connus)

| Joueur | Code | Tables |
|---|---|---|
| Guilde | **Family** | `GuildFunction`, `family*.layout` |
| VDG | FamilyTown/Instance | `c_buliding`, `4PHouse`, `decorate`, maps S501/S502 |
| Plans récolte/craft | **Work** | `c_familycollect` (478), `c_familycombine` (3 178) |
| Craft racial | **LifeSkill** | `c_lifeskill` (595, 5 groupes de race) |
| Fortification | **Strengthen** | `S_FamilyCombine` id 1-20, scrolls 40002-40019 |
| Boîte mystique | **Cube** (op 0x52) | `c_itemchest` (1 538) |
| Magic Box (étoiles) | op 0x58 | `c_itemcombo` (5 413) |

## État global (résumé de la matrice)

- **Serveur C#** : craft/guilde quasi intégralement implémenté — les trous sont
  précis et listés (SYNTHESE §phase E : cap DEV 10 s, combos strengthen,
  monnaies fame-shop, lifecycle bâtiments…).
- **Client Godot** : aucune UI craft/guilde fidèle — toutes custom ou
  squelettes ; ~29/40 layouts `family*` et toute la pile cube/magicbox/
  strengthen/lifeskill absents. Le déblocage n°1 = widget `ItemSlot`
  interactif dans `CeguiBuilder` (SYNTHESE §phase A).
- **Visuel** : specs = les 415 `.layout` du corpus + ~40 ref-shots
  cataloguées ; **aucun ref-shot** pour strengthen/family building/work/
  cube — la fidélité se mesurera aux layouts.

⚠️ `c_*` = Big5, `t_*` = CP1252, `S_*` côté serveur `server_us/` uniquement.
Périmètre contenu : ≤ niveau 90 (les paliers 95/100/110 des rapports =
Vendetta, hors implémentation).
