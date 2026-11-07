# M1 — audit des 4 comportements de mobs (état au 2026-11-07, lecture du code, sans correctif)

Source : `eden_server/server/src/Eden.Zone/ZoneEntryFlow.MobWander.cs` (390 l.), `ZoneLiveWorld.cs`.

| Comportement | Présent ? | Où | Reste à vérifier |
|---|---|---|---|
| Errance | oui | `MobWanderStepAsync` : rayon/intervalle de `GameBalance` (`MobWanderRadius`, `MobWanderMinMs/MaxMs`), horodaté par `NextWanderAt` | comparer rayon/intervalle au natif par famille de mob |
| Trajet fixe (patrouille) | oui | `ResolveMobPatrol` + `mob.Patrol`/`PatrolIndex` (= `SetNewGoalPatrol` natif), boucle ou fin de marche pour les mobs d'invasion | mobs sans `PatrolId` qui errent comme des mobs libres (commentaire du code l. 160) : confirmer que c'est le natif |
| Caché / déclenché | partiel | `EventSpawned`, `ArmBornTimers` (cycle 據點 : events à +30 s / +420 s), `EventActionLocked` | pas de moteur Déclencheur → Condition → Action (Q6) : chaque cas est codé en dur |
| Immobile | non prouvé | aucun drapeau « immobile » explicite trouvé (`MoveLocked` = verrou d'état, pas un comportement de scène) | chercher dans `c_monster`/scènes la colonne qui fixe un mob sur place |

Conclusion : 2 comportements sur 4 sont complets ; « caché-déclenché » dépend de Q6 ; « immobile » est à prouver avant tout correctif.
Décision propriétaire rappelée : les 4 comportements sont portés mais **non éditables**.
