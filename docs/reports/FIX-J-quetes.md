# FIX J-quetes — quêtes terminées affichées « en cours »

## Cause racine
- Le serveur (`ZoneEntryFlow.Mission.cs:869 HandleQueryMissionStatesAsync`) envoie dans le 348 :
  registre FMS (mid→état), map « en cours » = FMS 3/4 seulement, bitvec « done » (FMS 1), ids FMS 5, et les 10 slots `mission_trace`.
- DB réelle test123 (edenfr_db, char 200000001) : `mission_trace = [1009,0…]`, FMS : 1010=1, 266×état 5, **1009 absent (FMS 0)**.
  La quête 1009 « Nv. 1 Votre première Quête I » (Cynthia) est donc faite/reset côté FMS mais son slot trace traîne.
- Client `QuestTracker.SetMissions` (avant) : `Live(mid) = fms.GetValueOrDefault(mid) is not (1 or 5)` → FMS 0/absent comptait comme « vivant » → la trace périmée 1009 s'affichait. Même défaut dans `SetTrace`, et le compteur 391 kind=1 (`UpdateMission`) réinsérait une quête déjà finie.
- Le journal (`QuestWindow`) liste déjà par FMS 3/4 uniquement : pas touché (OK).

## Correctif
- Nouveau `godot_project/scripts/protocol/QuestStateLogic.cs` (pur) : `IsOngoing` (3/4), `IsLive`, `Visible(trace, inProgress, fms)`.
- `hud/QuestTracker.cs` : `SetMissions`/`SetTrace` utilisent la règle « seul FMS 3/4 est en cours » ; le compteur 391 n'ajoute pas une quête dont le FMS connu n'est pas 3/4.
- Test : `tools/NetTests/QuestStateLogicTests.cs` (7 cas, vert).

## Limites
- Je n'ai pas retrouvé la routine tracker du client officiel décompilé (non lu) : la règle « en cours = FMS 3/4 » vient du code serveur désassemblé (commentaires du serveur) et du journal existant.
- Côté serveur (hors périmètre) : la trace 1009 devrait être purgée (`ClearTraceAsync`) quand FMS repasse à 0 / à la complétion ; ici 1009 est resté en trace.
