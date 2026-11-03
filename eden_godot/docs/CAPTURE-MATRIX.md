# Matrice de captures — Godot vs ref-shots

État au 2026-10-27 (plan `archive/temp-plans/2026-10-27-refonte-fidelite-totale-v2.md`,
Bloc K). « Vérifié » = comparaison visuelle faite cette session sur capture
Godot ; « monté » = fenêtre attachée au layout natif et capturée ;
« live » = nécessite un déclencheur serveur réel, non falsifiable.

Captures Godot du lot : `godot-shots/mx_*.png` (écrans,
`--screen=` + `--shot`) ; fenêtres HUD : captures `--hud-open=<clé>`
documentées au fil du plan (fichiers `/tmp`, non versionnées).

## Écrans

| ref-shot | Godot | État |
|---|---|---|
| `Selection_channel.png` | `mx_channel.png` | ✅ vérifié — liste serveurs + channels, tag Recommended, Confirm/Cancel |
| `Selection_personnage.png` | `mx_chars.png` | ✅ vérifié — strip persos + carte portrait + boutons droits (Create/Delete/Set pwd/Select Server/Start Game) |
| (login, pas de ref-shot) | `mx_login.png` | ✅ vérifié — fond temple, panneau sombre, titre, footer serveur/version |
| (charcreate, pas de ref-shot) | `mx_create.png` | ✅ vérifié — modèle centré + panneau natif droit complet |
| (loading, pas de ref-shot) | `mx_loading.png` | ✅ vérifié — artwork non-brandé + barres or + barre native ornée + alpaca |

## HUD permanent

| ref-shot | Godot | État |
|---|---|---|
| `juste le jeu.png` | `hud_solo.png`, `qa2_hud_solo_*.png` | ✅ vérifié — HUD complet en jeu (live régulier) |
| `TopLeftVitals.png` | vitals dans `hud_solo.png` | ✅ vérifié — HP/MP/XP + portrait |
| `HotbarPanel.png` | skillbar solo (`--hud-open`) | ✅ monté — 1-3 lignes, hotkeys |
| `ChatPanel.png` / `ChatPanel-Peer.png` | chat solo/live | ✅ monté — onglets Normal/Battle |
| `QuestTracker.png` | tracker HUD | ✅ monté |
| `DoubleEXPbar.png` | barre XP vitals | ✅ monté |
| `carte.png` | minimap (16 boutons câblés) | ✅ vérifié |
| `QuestLog.png` | QuestLog window | ✅ monté (preuves quest live : rituel 285→11→531) |

## Fenêtres (panels)

| ref-shot | Godot | État |
|---|---|---|
| `ClassePanel*.png` (4) | ClassWindow sur `JobWnd_NA` | ✅ monté — layout natif, onglets |
| `ClassPanel-ChangementDeClasse.png` | ClassWindow onglet changement | 🔶 monté — données réelles à prouver live |
| `CommunityPanel*.png` (2) | SocialWindow `CommunityWnd` | ✅ monté (onglets TabControl non rendus — limite connue v1) |
| `ItemMallPanel*.png` (2) | ItemMallWindow | ✅ achat prouvé live (40362, points 136→135) |
| `Mail.png` | MailWindow | ✅ monté — lecture OK, pièces jointes à prouver |
| `ParameterPanel*.png` (4) | SystemPanel/options | ✅ monté — options UI/son/graphismes |
| `SystemPanel.png` | SystemPanel | ✅ monté |
| `PetsPanel.png` | PetFunctionBar/Elf panel | ✅ monté — binds 412 posés (0 familiers sur compte test) |
| `Profile*.png` (3) | ProfileWindow + AchievementWindow | ✅ monté |
| `daily reward.png` | DailyRewardWindow | ✅ prouvé live — `--daily-test` : état 615 → jour 1 levé à 600 s en ligne → claim 258 op=2 → 274 item livré + 615 type=2 |
| `FindGuild.png` | FindGuild/Social | 🔶 monté — données live à prouver |
| `HoverToolTipClasse.png` | tooltips sort/classe | ✅ monté — tooltips actifs |
| `TransportationArray*.png` (2) | TransportWindow | 🔶 monté — TP réel à prouver (changement de map = trigger serveur) |

## États de jeu (déclencheurs live requis)

| ref-shot | Godot | État |
|---|---|---|
| `TailleMob.png` | échelle mobs | ✅ vérifié — proportions natives (mushroid 0,67 ≈ ref 0,7) |
| `mouvement caméra.png` | caméra orbitale | ✅ vérifié — spring 11 u, cadrage corrigé |
| `TimedCastSkill.png` | CastBar | ✅ prouvé live — `--hud-open=cast`, barre + secondes |
| `SkillAnimationTornado(cleric).png` | anims sorts | ✅ partiel — cast anim joueur prouvé (`--buff-test`, C_Spell col3 → motion.ini, `shoot12`) ; anims dégâts mobs via CombatFx/272 ; FX visuels de sorts restent à couvrir |
| `Fumée quand on se déplace.png` | trail/poussière course | 🔶 non couvert — cosmétique, à faire si demandé |
| `changement de map - changement d'étape de connexion.png` | loading migration | ✅ monté — LoadMap + loading screen sur migration |
| `NouveauSkillApresLevelUp.png` | notif skill level-up | 🔶 284 level-up prouvé (`--levelup-test`, `//set_level` 30↔31) — la notif *nouveau sort* nécessite un palier octroyant un sort (aucun au niv. 31) |

## Résumé

- **Vérifiés visuellement cette session** : 5/5 écrans + minimap +
  castbar + échelle/caméra — conformes aux refs.
- **Montés + layout natif prouvé par capture** : ~25 fenêtres
  (détail dans le plan, Bloc I).
- **Round-trips live prouvés** : talk/rituel, achat shop, trade
  2-clients, banque/neobank/familystorage, item mall, repair
  45→342 + 47→343, buff 279→299 (BuffBar), cast anim, étal joueur
  (77/377+72/378/76/274), défi 1v1 complet (135→451→136→453→454
  via `tools/HeadlessBot`), mail PJ↔PJ (99→403 + vérif boîte
  destinataire), daily reward (258→615 état+claim+274 item),
  level-up (284, `//set_level`), téléport/migration
  (`//transport_node`+`//tele`).
- **Non falsifiables sans gameplay réel** : loot/roll en groupe,
  notif nouveau-sort au level-up (aucun sort octroyé aux paliers
  testés), trail de course.
