# Audit animations / effets de sorts (client Godot)

Date 2026-10-28. Source : `C_Spell.ini` (col 3 action KF, col 5 effet de cast,
col 10 effet d'impact) + `assets/models/effect/model/S*.glb` (2244 glb).

## Colonnes C_Spell utilisées

| Col | Contenu | Exemple | Rendu |
|---|---|---|---|
| 3 | KF d'action du lanceur (3002 chant, 412 shoot, 10xx attack) | 3002 | `SkillCast.OnCastOk` → `MotionTable` → anim perso (déjà là) |
| 5 | effet de cast `E<n>` → `effect/model/S<n>.glb` | E10121 | `SpellFx` : sous le lanceur, suit, dure le cast |
| 10 | effet d'impact `s<n>` → `S<n>.glb` | s10141 | `SpellFx` : sur la victime (278/279/281/288/289) |
| 6/7/4/12 | autres effets (projectile/aura ?) | s55331 | NON porté (sémantique non prouvée) |

## Couverture données (sorts « classe » payants, par classe)

glb présent pour 100 % des effets d'impact (col 10) et ≥ 90 % des effets de
cast (col 5), sauf classes 3, 14, 25, 26, 35, 36 : ~40-60 % des sorts n'ont
pas de col 5 (sorts passifs/buffs sans effet de cast). Classe 5 (test1) :
35/35 cast, 19/19 impact. Global : 2839/3383 cast, 1998/2032 impact.

## État AVANT ce lot

| Élément | État |
|---|---|
| Anim de cast perso self | OK (279 → col3 → motion.ini) |
| Anim de cast joueurs distants (278) | MANQUE |
| Effet de cast (cercle magique sous le lanceur) | MANQUE |
| Effet d'impact sur la cible | MANQUE |
| Anim blessure mob touché par sort | MANQUE (hurt01 seulement sur 272 mêlée) |
| Projectile (vol lanceur→cible, 282 FlyToLocation) | MANQUE (serveur n'émet pas 282) |
| Anim cast des mobs (sorts de mob) | MANQUE |

## État APRÈS (`scripts/combat/SpellFx.cs`)

- Effet de cast (col 5) sous le lanceur self (279) et distants (278) : FAIT.
- Effet d'impact (col 10) sur la victime (281/288/289, dédoublonné) : FAIT.
- hurt01 sur mob touché par sort hostile : FAIT.
- Anim de cast joueurs distants (278 → col 3 → motion.ini) : FAIT.
- Piège corrigé : les matériaux glb d'effet ont alpha=0 (animé par le KF
  natif, non porté par glTF) → forcé à 1, rendu additif non éclairé, fondu
  manuel (0,15 s entrée / dernier tiers sortie). Durée = anim embarquée
  (0,6-3 s) ou durée du cast.

Preuve live : `--combat-test --clone-mob=20678 --cast-spell=50011 --fx-hold`
(effets maintenus 5 s), Flameshot `eden_godot/godot-shots/fxf_*.png` :
éclats de glace (S10121 cast, puis S12521) sous le perso, faisceau rose
d'impact (S10141/S12541) sur le mob, mob en `hurt01`. Log :
`[spellfx] S10121 sur Player`, `[spellfx] S10141 sur E_ffff…`.
Sorts 50150/50269 : pas de col 5/10 → aucun effet (normal).

## Restes

- Projectile / cols 4, 6, 7, 12 : sémantique à prouver par désasm
  (`FUN_00d0cc70` parse C_Spell ; offsets 0x94..0x13c).
- Matériaux des glb d'effet passent par `ToonMaterializer` (alpha-hash au
  lieu de blend additif) → rendu plus opaque que le natif.
- Sorts de mob, effets de zone (CastToLocation) et durée exacte (boucle
  tant que le cast tient) approximatifs.
