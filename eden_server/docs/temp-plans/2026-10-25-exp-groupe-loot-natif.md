# Plan — Exp de groupe native + distribution de loot (2026-10-25)

## Objectif

Remplacer le partage d'exp d'équipe spéculatif par la transcription
fidèle de `CMonster::GiveTeamExp` + `CCharFormula::ExpForTeam*`
(zone désasm), et câbler une distribution de loot réelle selon la
règle de l'équipe (round-robin effectif, éligibilité zone).

## Preuves natives

| Élément | Source |
|---|---|
| `ExpForTeam` = `ROUND(base×0.01×lv×(n×para330+100)/Σlv)` capé à base | `CCharFormula::ExpForTeam` @0x083f4470 |
| `ExpDeclineRate` : facteur [0,1] par écart lv/mob, borné ±100 | @0x083f5150 — littéral `-5` branche mob fort (incohérence native conservée, param 329 partout) |
| `ExpForTeamNoShare` = `ROUND((n×para331+100)×0.01×base×decline)`, borne `base×2`→base | @0x083f5340 |
| Membres locaux : même node, vivants ; exclus si `maxLv−lv > 9` (retirés de n ET de Σlv) ; tueur exclus → NoShare, autres exclus → 0 | `CMonster::GiveTeamExp` |
| Ordre par membre : gear exp (share×para496) → GainExp → elf reçoit la part | idem |
| Params : 328/329 diff/decline, 330 share, 331 noshare, 496 gear % | `S_Parameter.ini` ↔ `CFormula::AllFormulaParas` |

## Modifications

### `NativeCombatFormulas.cs`
- `ExpDeclineRate(charLv, mobLv, p)` — déjà posé.
- `ExpForTeam(base, n, lv, Σlv, p)` — déjà posé.
- `ExpForTeamNoShare(base, n, lv, mobLv, p)` — déjà posé.

### `TeamService.cs`
- **Supprimé** `ShareExperience` (formule `base×(1+0.1(n-1))/n`
  inventée, sans source native — remplacée par les formules ci-dessus).
- `PickLootReceiver(teamId, killerCharId, isEligible)` :
  - FreeForAll / TauntExclusive → tueur, repli 1er éligible.
  - LeaderOnly → leader, repli tueur puis 1er éligible.
  - Random → tirage parmi les éligibles.
  - RoundRobin → curseur `TeamEntity.LootCursor`, saute les
    inéligibles, n'avance que sur attribution réelle.
  - null si personne n'est éligible.

### `TeamEntity.cs`
- `LootCursor` — index persistant du prochain attributaire RR.

### `ZoneEntryFlow.Combat.cs`
- `ComputeTeamExpShares(st, mobLv, baseExp)` — transcription de
  `GiveTeamExp` : membres locaux (même node, migrés, vivants),
  exclusion ±9, parts partagées ou NoShare pour le tueur exclus.
  Solo/1 local → part entière.
- `GrantMobExpToAsync(ost, share)` — séquence native par membre :
  texte 248, gear exp, `GrantCharExpAsync` (600+level-up), elf.
- `GrantKillExpAsync` — classExp reste tueur-seul ; char exp passe
  par `ComputeTeamExpShares` + `GrantMobExpToAsync` par membre.
- `ResolveLootReceiver(st)` — traduit `PickLootReceiver` en
  `EntryState` (éligible = session migrée sur le même node).
- `DistributeLootAsync` — chaque roll réévalue l'attributaire ;
  add item / notif slots / ground-treasure / ItemCollect sur le
  receveur, pas toujours le tueur. Or et quest drops inchangés
  (tueur, comportement natif).

### Tests (`TeamServiceTests.cs`)
- `ExpForTeam_NativeFormula` — parts 75%/45%, cap base, déclin.
- `PickLootReceiver_RoundRobin_SkipsIneligible` — rotation,
  saut d'inéligible, curseur.
- `PickLootReceiver_FreeForAll_FallsBackToEligible`.

## Vérification

- [x] `dotnet build` : 0 erreur, 0 warning.
- [x] `dotnet test` : **719/719**.
- [ ] Live : kill en groupe → exp visible chez tous les membres sur
      la même map ; loot round-robin distribué (à valider client).

## Reste

- ~~flag « no-share » et or partagé~~ — résolu ci-dessous (vague 2).

---

## Vague 2 — flags `CForceTeam` + `GiveGold` natifs (même jour)

### Preuves complémentaires

| Élément | Source |
|---|---|
| `CForceTeam+0x64` = **TeamType** : `SetTeamType` clamp 1-4 ; déduit de la capacité par `SetMaxTeamSize` (5→1, 10→2, 25→3, autre→0) | @0x0846f3d0 + tail |
| `CForceTeam+0x56` = **flag no-share exp** : `SetExpRule` — wire 1-2 → 0 (partagé), >2 → 1, valeurs ≤0 non écrites | @0x0846cdb0 |
| `CForceTeam+0x54` = capacité max (comparée au nb de locaux) | `GiveTeamExp` |
| `GiveTeamExp` : TeamType==3 → `GiveExp` solo ; TeamType==4 → map type 0xc seulement sinon **rien** ; capacité ∈[6,10] → partage seulement en map type 7 | @0x0849d190 |
| `GiveGold` : TeamType≠3 → or/n entre locaux vivants ; spread natif `rand(2*spread)` borne haute **exclue** (56001 : 1-2 or) | @0x08497200 |
| Bonus par membre (`+0x4ec` rate texte, `+0x508` proc, `GetExpUpRateFromGender`) — attrs de personnage non modélisés | idem |

### Modifications vague 2

- `TeamEntity` : `TeamType` (dérivé capacité) + `ExpNoShare`.
- `TeamService.SetExpRule(charId, rule)` — sémantique native
  (leader only ; ≤0 n'écrit pas). Op wire pas encore mappée —
  `RecvWorldTeamInfo` natif n'a pas de case exp-rule dans les
  ops 0-10 ; le flag est probablement posé à la création
  (`SetTeamRecruit`/`CloneMemberData`) — TODO si besoin.
- `ComputeTeamExpShares` : gates TeamType 3/4, capacité 6-10 ∧
  node type ≠ 7, `ExpNoShare` → killer seul en NoShare.
- `LocalTeamMembers` extrait (partagé exp + or).
- `DistributeGoldAsync` : split gold/n entre locaux
  (TeamType≠3), attr 0x02 par session. Fix off-by-one spread :
  `Next(spread*2)` au lieu de `Next(spread*2+1)`.
- Tests : `SetExpRule_NativeSemantics`, `TeamType_DerivedFromCapacity`.

### Vérification

- [x] `dotnet test` : **721/721**.
- [ ] Live raid/party à vérifier.

---

## Vague 3 — LFG : recrutement + recherche d'équipe (Phase 5)

### Preuves natives

| Élément | Source |
|---|---|
| `CNE_CZ_ZoneServerTeamRecruitV2` = `[u8 enable][s16][s16][s16][u32][string]` | Deserialize @0x082935xx |
| `CNE_CZ_ZoneServerSearchTeamV2` = `[s16][s16][string]` | Deserialize @0x082855xx |
| `ClientSearchTeamInfoV2` = `[s16][u16 n][n × SSearchTeamDataV2][s16]` ; entrée = `[u16 m][m×member][s16][string][s16][u8×3]` ; member = `[i32][string][s16×6]` | Serialize @0x081ea240 + operator<< @0x081ea180 |
| `ClientTeamRecruitMsgV2` = `[u8][s16][s16][string][u32]` | Serialize @0x08135xx |
| `ChangeRecruitDataV2` : solo→annonce perso, équipe→leader only (membre en map 0xe → refus) | @0x0840fxxx |
| Recherche déléguée à la mission (`RecruitTeamSearchTeamV2` TEC) dans le natif → index zone-local chez nous, réponse client identique | Execute @0x08285570 |

### Modifications

- `TeamService` : `RecruitPost` + `RegisterRecruit`/`SearchRecruits`/
  `ClearRecruit` (leader-gated en équipe).
- `CNC_CZ_ClientSearchTeamInfoV2` réécrit au format variable natif
  (la classe générée plate était fausse : 6B au lieu du vecteur).
- `TeamHandlers` : recherche peuplée (membres d'équipe ou déposant
  solo via DB), broadcast 583 avec les champs réels.
- Test `Recruit_RegisterSearchClear`. **722/722**.

## Vague 4 — `GetLooter`/`GetTreasureLooter` natifs (distribution réelle)

Preuves :
- `CItemFactory::GetTreasureLooter` @0x0808c1f0 : à la récolte, si
  owner en team (type≠3) ET `lootRule != 3` → `CForceTeam::GetLooter`
  choisit le vrai destinataire ; sinon l'owner (tueur) garde.
- `CTreasure::CanPickUp` @0x084ea5d0 : règle 3 = seul l'owner peut
  ramasser ; règles 1-2 = tout membre de l'équipe peut ramasser (puis
  redistribution aléatoire).
- `CForceTeam::GetLooter` @0x0846dfe0 : éligibles locaux en rotation
  depuis le curseur +0x52 ; ≥2 → `random() % n` UNIFORME (1/n chacun —
  pas de table de paliers pondérés dans le désasm) ; <2 → cueilleur
  garde ; autre valeur de règle → premier éligible après le curseur
  (séquentiel). Curseur = index du dernier membre servi.
- `GetLootOrderMember` @0x0846deb0 : filtre = membre local connecté,
  vivant, même node.
- `ChageLootRule` @0x0846d7f0 : écrit AUSSI +0x56 — règle 3 coupe le
  partage d'exp (tueur garde son exp), 1-2 le rétablissent.

Implémenté :
- [x] `PickLootReceiver` réécrit en transcription fidèle (rotation
      depuis `LootCursor+1`, tirage uniforme règles 1-2, séquentiel
      hors {1,2,3}, tueur garde règle 3 / TeamType 3 / <2 éligibles).
- [x] `TeamEntity.LootCursor` = index du dernier servi (init −1),
      sémantique +0x52 ; enum interne `LootRule` supprimé — la valeur
      wire porte la sémantique.
- [x] `ChangeLootRule` écrit `ExpNoShare = rule == 3` (couplage natif).
- [x] Tests : uniformité (~1/n sur 300 tirages), curseur suit le
      servi, skip inéligible, count<2 → tueur, règle 3 / raid25 →
      tueur, couplage ExpNoShare. 724/724 verts.
