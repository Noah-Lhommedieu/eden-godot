# Combat — auto-attaque ↔ cast (règles natives)

Date : 2026-11-03 — Godot 4.8-beta2 (`engine/current/godot`).
Périmètre : `scripts/combat/AutoAttack.cs`, `SkillCast.cs`,
`CastLock.cs` (règles pures), bindings `eden_cancel_cast`, serveur
`ZoneEntryFlow.Combat.Mob.cs:HandleMeleeAsync`.

## État implémenté (verified — build + tests verts)

| Règle | Implémentation | Statut |
|---|---|---|
| Pas d'auto-attaque pendant un cast | `AutoAttack._PhysicsProcess` : sortie anticipée tant que `SkillCast.IsCasting` (demande 9/16/17 → 281/287/410/302/échéance) — ni swing, ni approche, ni NCID 5 | verified (PostCastRules + NetTests) |
| Reprise post-cast seulement à portée | `AutoAttack.ResumeAfterCast` (appelé par `SkillCast.EngageAfterCast`) + transition cast→fin dans `_PhysicsProcess` : hors de portée → `_holdAfterCast` (armée, `SetMoveTarget`/NCID 5 jamais émis) ; dès que la cible revient à portée le hold tombe et l'engagement redevient normal | verified (NetTests + code) |
| Annuler le cast | bind `eden_cancel_cast` = **Shift+S** par défaut (ClientSettings `[keys]`, remap fenêtre hotkey, `InputBindings` gère « Mod+Touche ») → `SkillCast.Break()` : NCID 13 + `ForceReleaseCast` local immédiat (barre/état/verrou) ; 287 idempotent ensuite | verified (NetTests + vérif moteur headless) |
| Melee pendant cast côté serveur | `HandleMeleeAsync` → `BreakCastAsync` (287 + flag release) avant la résolution du coup | verified (code ; pas de test unitaire — voir limites) |

## Preuves natives

- **Attaque cassant le cast** : `CPlayerFactory::MeleeAttack` zone
  @0x080f6930 — bit de cast testé → `ISpell::Break` → `NotifyBreak`
  @0x084ee5d0 émet `CNC_CZ_ClientSpellBreak` (287). Le client retient
  donc tout NCID 5 pendant l'incantation — sinon il se casserait
  lui-même. *(verified désasm)*
- **Annulation cast** : NCID 13 = `CNE_CZ_ZoneServerSpellBreak`
  (reindex opcode 13) → `CEC_SpellBreak::Execute` zone @0x08552830 —
  c'est bien le canal natif d'annulation. Corps C→S vide.
  *(verified désasm + test wire CombatCastTests)*
- **Re-attack post-skill** : option `AutoReAttackb` (client.ini,
  byte options+0xe3 bit7 — FUN_0069e290). Dans FUN_007624d0
  (handler de commande d'attaque client_game) : si AutoReAttackb et
  `FUN_0075a8d0` (flag bataille 0x2000 ou bit +0x2e6) →
  `FUN_006cb450(5,0)` = réémission de la commande 5 par la file
  d'actions du joueur. *(verified désasm)*
- **Sémantique InputMap Godot** : action `{Key=S, ShiftPressed}` —
  vérifié sur le moteur épinglé (script headless) : Shift+S déclenche
  l'action, S seul non ; une action « S » simple déclenche aussi sur
  Shift+S (sous-ensemble, matching non-exact). Conséquence assumée :
  Shift+S = « annuler le cast » + déplacement arrière (S = move_back),
  ce qui reste licite pendant un cast mobile. *(verified moteur)*

## Limites / hypothesis

- La branche exacte « poursuite ou non » de la commande 5 réémise en
  post-cast n'a pas été isolée jusqu'au bout du désassemblé
  (FUN_006cb450 → fabrique de commandes). Le comportement « pas de
  course post-sort » est le **contrat de la mission** et est cohérent
  avec le gate `FUN_0075a8d0` (état, pas distance). *hypothesis*
- Serveur : le break cast-sur-melee est ajouté mais non couvert par un
  test `Eden.Tests` (HandleMeleeAsync est privé dans la boucle
  ZoneEntryFlow — pas de harnais existant). *hypothesis code-review*
- `AutoAttack` engagé sur A + cast sur B : l'engagement reste sur A
  (comportement pré-existant conservé — le re-ciblage natif vers la
  cible du sort n'est pas implémenté). *limite connue*

## Tests

`tools/NetTests/CombatResumeTests.cs` (8 cas) : décision
portée/immobile/none, freeze pendant cast, bind par défaut Shift+S,
round-trip settings.cfg, validation des noms composés.
`CombatCastTests` (wire 13) inchangé et vert.

Résultats 2026-11-03 : NetTests 836/836, Eden.Core 960/960,
Eden.Tests 1471/1471 (tools/test.sh sous /tmp/eden-build.lock).
