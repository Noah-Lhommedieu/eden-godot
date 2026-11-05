# 2026-11-05 — Mobs qui « attaquent de super loin » + lag au kill

## Symptôme

- Les mobs « cast leur sorts et auto-attack de super loin ».
- Le jeu « empire par mob touché » (lag cumulatif aux kills).

## Diagnostic

### Attaques de loin — divergence position serveur / position rendue

Trois couches empilées, toutes confirmées en live (`EDEN_HIT_DIST=1`) :

1. **Vitesse d'interp trop basse** — serveur : poursuite 0,72 u/tick
   ≈ 7,2 u/s codé en dur (`ZoneEntryFlow.Combat.Mob.cs`). Client :
   horizon 326 = `e.MoveSpeed` = `RoamSpeed` (298 FieldSpeed ≈ 2 u/s).
   Le mob rendu traînait 10-30 u derrière → swings « de loin ».
2. **dst = position exacte du joueur** — le 267 d'aggro, le 326
   d'engagement et les 326 de poursuite annonçaient tous `dst =
   PosX/PosZ`. Le client marche l'entité jusqu'à dst : le mob rendu
   entrait dans le modèle du perso (`rendue=0,0 u` pendant les coups),
   et quand le joueur avançait, le point ancré devenait intérieur.
3. **`_player` jamais assigné** dans `EntityRegistry` — les branches
   self (anims hurt/death, mesure hit-dist) étaient du code mort.

Portées de cast grandes (col29=100 → 50 u) = fidèle au natif
(`CAI::CheckUseSpell` @0x084d91c0 : `dist > range + rayons + 0,5`).
Non touché — pas un bug.

### Lag au kill

- `KillLoopProbe` : aucun spike post-kill mesuré (p99 ≈ 6,1-6,7 ms,
  max 12 ms), compteurs bornés (ents 17-19, corpses=0, slowq=0,
  fxhosts=0) sur 6 kills en combat réel — pas de fuite client.
- Serveur : instrumentation `MeasureSubsystemAsync` — pas de stall
  >30 ms hors drain de login.
- Fuite réelle trouvée : `EntityPos` non purgé quand une entité
  **vivante** sort de vue dans `ZoneEntryFlow.Sight.cs` → croissance
  d'une entrée par mob/npc vu par session. Fix appliqué. La branche
  « replayed » le conserve volontairement (ancre de ré-annonce).

## Fixes appliqués

Serveur (`ZoneEntryFlow.Combat.Mob.cs`, `Combat.Aggro.cs`,
`Sight.cs`) :

- [x] `MeleeChaseDst` : dst des 267/326 d'engagement = cercle d'arrêt
      mêlée (6 u sur le segment joueur→mob), ou position du mob s'il
      y est déjà.
- [x] 326 de poursuite : dst = prochain step (0,72 u), borné au cercle
      d'arrêt — le rendu suit la vérité du fil tick par tick.
- [x] `EntityPos` purgé à la sortie de vue d'une entité vivante.

Client (`EntityRegistry.cs`, `SlowFree.cs`) :

- [x] `PredictSpeed` = max(annoncée, observée d/dt entre 326) pour
      l'horizon de prédiction des 326/267.
- [x] `_player` résolu en lazy (`SelfPlayer()`) — branches self
      réactivées.
- [x] Nullability `SlowFree` corrigée.

## Validation live (client graphique, Zone sur `edenfr`)

`/tmp/mobdist6.log` + `/tmp/killshot3.png` (capture `spectacle` en
plein combat — le mob « Éclaireur Pirate » se tient à portée mêlée
du perso, ni dedans ni loin) :

- `hit-dist` : rendue **5,3-6,5 u** sur tous les coups (avant :
  15-25 u au début du thread, 0 u après le 1er fix dst).
- Kill-loop ×6 : p99 post-kill 6,1 ms, pires images 6-12 ms, compteurs
  bornés.

## Limites assumées

- Portées de cast grandes (col29=100 → 50 u) = fidèle au natif ; non touché.
- Vitesse de poursuite serveur 7,2 u/s hardcodée ≠ BattleSpeed par mob :
  divergence de balance, hors scope du symptôme visuel.
- Le « lag au kill » n'a produit aucune métrique dégradée en combat
  réel — la seule fuite trouvée (`EntityPos`) est mémoire/itération,
  pas frame-time. Si le symptôme persiste côté joueur, reste à
  reproduire sur sa machine (matériel, map, densité).
