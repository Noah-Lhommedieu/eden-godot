# Plan — gate de cast des mobs : portée + premier coup + chase 10 Hz

Date : 2026-11-04. Tâche propriétaire : « comportement de cast des
monstres côté serveur ».

## Symptômes / exigences

1. Un mob doit être **à portée de son sort** pour caster (mêlée =
   contact ; distance = col28 S_Spell ×0,5). Aujourd'hui casts visibles
   « de très loin ».
2. Un mob doit avoir **touché au moins une fois au corps-à-corps**
   (attaque de base) avant de pouvoir caster. Chercher la règle dans
   le décompilé ; sinon l'implémenter comme règle propriétaire.
3. Technique « run » : les mobs en poursuite doivent recevoir la
   position de la cible souvent (≈10 Hz si pas déjà) pour changer de
   direction vite — coût raisonnable.
4. Ne pas casser : wander/patrol récent, immobilité des world boss
   sans PatrolId, structures (ai=164) passives.

## Preuves natives (déjà collectées)

- `lapis::CAI::CheckUseSpell` zone @0x084d91c0 : cast refusé →
  poursuite si `dist > +0x40 + rCaster + rCible + 0,5`
  (`DAT_08894104` = -0.5f). `SSpellData+0x40` = S_Spell col28 × 0,5
  (`_DAT_0887b5b0` = double 0.5, `LoadAllSpellData` @0x08380130).
  rCible joueur = ~1,2 u (`CCharacter::GetSize`), rCaster mob =
  BoundInfo col7 (`CMonsterTemplate::GetSize`, template+0x28 ; rayon
  par défaut 1,0 — `GetAllMonsterSize` @0x0835xxx : BoundInfo col7 =
  rayon, col8 = autre map, clé = `strtol(mNNNNNN)` = chiffres de
  `ModelCode` S_Monster col1).
- `lapis::CAIFSMCombat::Update` @0x084db480 : `CheckUseSpell()` en
  premier, puis `InAttackRange()` + `Attack()` si pas de cast —
  poursuite quand hors de portée du sort. `DetermineAttackMode`
  (arme le sort) est appelé en fin de pipeline `Attack()` — dans
  l'état combat natif le pick de sort ne vient qu'après un swing.
- **Absence constatée** : pas de flag natif « un coup mêlée a touché »
  trouvé avant le cast — `CAIFSMHunt::Update` @0x084dbeb0 arme des
  sorts pendant la poursuite. La règle « premier hit » est donc une
  règle propriétaire (≈ le séquencement combat natif : cast armé
  seulement après `Attack()`).
- `CSpellToLife::CastTest` @0x084f04d0 : borne extérieure =
  `rC + rT + +0x40 + attrMod + 2,5` (`DAT_08896c68` = 2.5) — marge
  plus large pour le cast réactif 1999.
- `CAI::BroadCastMove` @0x084d3320 = NCID 326 (pos + dst + dir +
  flag). `CEventAIUpdate::DelayByFrames(1)` @0x084d4960 ≈ 66,7 ms
  (frames/15) — la cadence IA native est ~15 Hz max, throttlée par
  le budget entité+200.
- Client Godot `GameScene.MaybeSendMove` : MoveSelf à ~10 Hz.
  `ZoneLoop.TickInterval` = 100 ms ; `TickMobCombatsAsync` itère tous
  les mobs en combat chaque tick → mise à jour cible + 326 déjà à
  ≈10 Hz bout-en-bout = **cible de la tâche 3 déjà atteinte** (pas de
  changement de cadence, documentation ajoutée).

## Points d'édition

- [x] `src/Eden.Core/Data/Parsers/MonsterParser.cs` : `MonsterDef.
      BoundRadius` (défaut 1.0f) + `ParseBoundInfo(path)` (CSV
      `mNNNNNN,...,rayon(col7)` → Dictionary<int,float>) +
      `BoundInfoKey`.
- [x] `src/Eden.Core/Game/Services/MonsterService.cs` :
      `LoadFromDataDir` résout `BoundRadius` via BoundInfo.ini /
      boundinfo.ini + chiffres de `ModelCode`.
- [x] `src/Eden.Zone/ZoneLiveWorld.cs` : `LiveMob.HasLandedMelee`,
      reset dans `BeginCombat` (nouvelle génération), `CancelCombat`,
      `MarkDead` (couvre `ResetCombatForRespawn`).
- [x] `src/Eden.Zone/ZoneEntryFlow.Combat.Mob.cs` :
      `MobSpellCastRange(mdef, sdef, margin=0.5f)` = AreaLength
      (col28×0,5, toutes formes — la borne native est
      indifférente à la classe CSpell) + BoundRadius + 1,2 + margin ;
      `MobSpellInCastRange` ; gate du tick = `canRiposte &&
      mob.HasLandedMelee && pick && d2 ≤ range²` ; pose du flag sur
      `mres.Outcome != Miss` dans la riposte.
- [x] `src/Eden.Zone/ZoneEntryFlow.MobEnchants.cs` :
      `MobCounterCastAsync` — gate portée marge `OuterRangeBonus`
      (2,5, borne CastTest native) ; pas de gate « premier hit »
      (cast réactif enchant).
- [x] `tests/Eden.Tests/MobRulesTests.cs` : portées natives
      (50→27,7 rayon 1,0 ; mêlée 6→5,7 ≈ contact ; rayon modèle ;
      borne indifférente à la forme) + tests gate (hors portée →
      refus ; reset flag par engagement/mort/respawn).
- [x] `tests/Eden.Tests/SpellEffectTests.cs` : attentes alignées sur
      le changement antérieur en arbre (arg4 7003 = dixièmes via
      EnchantRestSeconds) — pré-existant, hors périmètre cast.

## Résultat

- `dotnet build Eden.sln` : 0 erreur, 0 warning.
- `dotnet test tests/Eden.Tests` : 1658/1658 verts.

## Hors-scope vérifié

- Pas de modif `ZoneEntryFlow.MobWander.cs` ni du cadence 100 ms de
  `ZoneLoop` (déjà ~10 Hz).
- Blast shape-3 appliqué à la cible courante (pipeline sans HitTest
  d'aire) — approximation existante inchangée ; le gate portée utilise
  le rayon col30 pour ces sorts.
- `MobCastSpellHitAsync` (restores sur le joueur) — pré-existant.
