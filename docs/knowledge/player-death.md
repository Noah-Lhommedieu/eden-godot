# Mort et revive du joueur (2026-11-05)

Statut : **verified** en live (Godot 4.8-beta2, `tools/run.sh -- --login=test123:test123 --channel --char`)
y compris les feuilles de vie.

## Comportement

- Mort : serveur `PlayerDeathAsync` (`ZoneEntryFlow.Combat.Death.cs`) → `st.Dead`, HP 0, 305 + 531.
  Client : `PlayerVitals.IsDead` → mouvement, auto-attaque, sorts bloqués ; fenêtre `DeathWindow`
  (layout natif `ReviveMenuUI`, bandeau « Vous êtes mort »).
- Serveur autoritaire : tant que `st.Dead`, les NCID 1 (move), 5 (mêlée), 9/16/17/59 (sorts), 15 (portail),
  107-109/187/188/227 (familiers) sont ignorés (`DeadBlockedOpcodes`) ; l'usage d'item (NCID 19) est refusé
  sauf item de revive (`ItemUseLifeGate`) ; l'item de revive est refusé à un vivant.
- NCID 20 `[i32 type]` (`HandleReviveAsync`), refusé si le joueur est vivant ou si le type est inconnu :
  | type | bouton | effet |
  |---|---|---|
  | 1 | Revenir ici | revive sur place, −10 % d'exp (perso + classe) |
  | 0 | Revenir au point de sauvegarde | téléport `revive_area_id` + revive, −10 % d'exp |
  | 5 | Utiliser un item (Locus) | consomme 1 Feuille de Vie de Locus (violette, ids 40036/41468), revive sur place, **sans** perte d'exp |
  | 6 | Utiliser un item (Vie) | consomme 1 Feuille de Vie (verte, ids 40035/40157), téléport au point de sauvegarde, **sans** perte d'exp |

  Les feuilles sont des items item-mall (`c_item`/`t_itemmall`, icônes I00523/I00522) sans enchant d'usage :
  le natif passe par l'option « Objet de Résurrection » de la fenêtre de mort. Le client envoie 5 si une
  Locus est en sac (priorité), sinon 6 ; le bouton est grisé sans feuille (`PlayerBag`).
- Perte d'exp : `ReviveRules.ExpLoss` = 10 % du palier, bornée à l'exp courante (jamais de niveau perdu) ;
  texte 316/0xba + 600 pour la barre d'exp.
- Persistance : la mort sauvegarde `hp = 0` ; au chargement (`EntryPipeline.Load`) `hp <= 0` ⇒ `st.Dead`
  (avant : relog = soin complet). À l'entrée le serveur renvoie 305 + 531 pour rouvrir la fenêtre.
- HP/MP après revive : 10 % du max (natif `CPlayerFactory::Revive(…,5,10,10,…)`), invincibilité S_Parameter 349.

## Preuves

- `dotnet test` : `ReviveRulesTests` (types, perte d'exp, détection item).
- Live : `--death-test=die|here|save|item` (`scripts/combat/DeathProbe.cs`, envoie `//suicide`, GM) ;
  captures `artifacts/shots/death-live-*.png`.
- Limites : le modèle du perso mort n'a pas d'animation de chute ; avec les deux feuilles en sac, le bouton unique utilise la Locus.
