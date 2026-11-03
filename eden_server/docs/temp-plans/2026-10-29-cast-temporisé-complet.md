# Plan — 2.3.2 Cast temporisé complet

## État constaté

- NCID 9 ciblé : déjà temporisé (`CastSequenceAsync` : 279 castDs +
  278 + flag → `ResolveCastAsync` schedulé sur boucle).
- NCID 16 location : déjà temporisé (`CastLocationSequenceAsync`).
- **NCID 17/59 Nowhere/Nearby : instantané** — 279 `Field2=0`, 288
  émis au départ, effets à `SwingDelayMs`. Écart vs
  `CSpellToNowhere`/`ISpell::OnCast` natif (@0x84ef270 :
  NotifyCastToSelf 279 castDs + NotifyCastToOthers 278 +
  `LifeEntityDo(0x0d)` → flag 0x2000 → `CEventSpellCasting` attend
  GetCastTime avant `OnBlast` → 288).
- **Sorts mob : instantanés** — `MobCastSpellAsync` enchaîne 495/302/
  279/289/dégâts d'un coup, ignore `sdef.CastTimeMs` (col26 →
  SSpellData+0x38). Un wind-up mort/stun devait annuler.
- **`ResolveCastAsync`/`ResolveLocationCastAsync` sans garde mort** :
  `PlayerDeathAsync` pose `CastingSpell=0` sans annuler `CastCts` →
  résolution post-mortem possible, barre jamais refermée.
- **Bug connexe live** : le 279 « mob » envoyé au joueur victime
  (paquet spéculatif — `NotifyCastToSelf` natif notifie la session du
  lanceur, un mob n'en a pas) fait `CastingSpell=mobSpell` côté client
  (`OnCastOk` else-branch inconditionnel) → `IsCasting` bloque nos
  casts suivants, et joue NOTRE anim de cast pour le sort du mob.

## Changements

1. `HandleCastNowhereAsync` (17/59) : si `def0.CastTimeMs > 0` —
   refus 279 flag=0 si `CastingSpell != 0` (avant arm cooldown) ;
   `CastCts/CastingSpell/CastingTarget/CastSeq` posés ; 279 avec
   `Field2=castDs` ; 278 broadcast ; flag 302 ; `ScheduleOnLoop(castMs)`
   → `ResolveNowhereCastAsync` : release flag (même convention
   sel=4/0), gardes token/seq/`PlayerHp>0 && !Dead && Migrated`,
   puis 288 + `ScheduleOnLoop(SwingDelay → NowhereCastReleaseAsync)`
   (chemin existant réutilisé tel quel). castMs==0 → flux actuel
   inchangé.
2. `ResolveCastAsync` + `ResolveLocationCastAsync` : garde
   `st.PlayerHp <= 0 || st.Dead || !st.Migrated` après le check seq —
   jamais de blast post-mortem.
3. `PlayerDeathAsync` : `await BreakCastAsync` en tête — annule le
   token, émet 287 `[i32 caster][s16 spell][u8 0]` + release flag
   (désasm `CheckCastBreak` @0x80f5790 / Serialize @0x81e97f0 — paquet
   déjà prouvé). Migration = déconnexion → `OnDisconnectAsync` annule
   déjà `CastCts` (ZoneEntryFlow.cs:321).
4. Sorts mob : `MobCastSpellAsync` — si `sdef.CastTimeMs > 0`,
   `mob.CastingUntil = now+castMs` + blast schedulé à castMs ;
   `MobCastBlastAsync` (495→289→dégâts→hit différé) gardé par
   `mob.IsCombatLoopCurrent(generation, charId)` (mort/deaggro/
   retarget invalident — MarkDead/CancelCombat incrémentent la gen)
   et `!mob.AttackLocked` (stun posé pendant le wind-up annule).
   Tick combat : `UtcNow < mob.CastingUntil` → mob occupé, reste en
   place sans frapper. `CastingUntil` reset dans
   `ResetCombatForRespawn`. **Suppression du 279 victime** pour les
   casts mob (spéculatif — aucun castreur session ; le client se
   croyait en cast). castMs==0 → flux actuel (289 immédiat).
5. Client `CastBar` : à `_elapsed >= _duration` la barre reste
   visible, pleine, « 0.0 » — la résolution est serveur
   (281/287/410 ferment) ; garde-fou +3 s sans réponse → auto-cancel.
6. Client `SkillCast.OnCastOk` : la branche accept exige
   `isSelfCast` — un 279 pour un sort non demandé ne peut plus
   détourner `CastingSpell`.
7. Test : `CombatLoopStateTests` — `CastingUntil` purge au respawn.
8. **Bug pré-existant découvert en live** : le tick cast lisait
   `Live.MonsterDefs[mobId].SpellIds` — defs `gameData.Monsters`
   brutes **sans** `ResolveRefs` (l'enrichissement ne s'applique qu'aux
   copies de `monsterSvc`) → `SpellIds` vide pour tous les mobs →
   aucun mob ne castait jamais. Lookup remplacé par
   `_services.Monsters.GetDefinition` (defs enrichies).

## Verdict runtime (live S002, stack Release)

- NCID 17 : `cast #50028 → sans cible` → `279 castDs=20` → `288` après
  ~2 s (effets à +0,7 s).
- NCID 9 ciblé : `279 castDs=20` → `NCID 13` en vol (pilote
  `--cast-break=800`) → `287 [caster][50028][0]` ; les casts suivants
  partent normalement (pas de lock résiduel). Cast 51000 castDs=50 →
  `281` après 5 s.
- Barre de cast : capture **Flameshot** réelle `castbar-live.png` —
  « Guérison » + remplissage « 0,7 » s restant.
- Mob temporisé : clone 58592 (4/4 sorts castDs=20) — `495 action
  0x0195` (geste de cast) → `289 spell 51021` **+2,1 s** après (×2) ;
  seules les réactions « hurt » 0x3f7 continuent pendant le wind-up
  (coups du joueur — pas d'attaque mob). Aucun 279 vers le joueur.
- Mob 20603 (78 k HP) a one-shot le joueur (Die) — mort traitée sans
  cast post-mortem.
- Tests : 1230/1230 verts.

## Hors périmètre

- Interrupt-on-movement : décision produit documentée (cast-mobile
  custom assumé — `CheckCastBreak` natif désactivé côté client).
- Interrupt-on-hit : inexistant dans EE natif.
- NCID 16 déjà temporisé — seule la garde mort y est ajoutée.
