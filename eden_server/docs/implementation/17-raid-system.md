# 17 - Système de Raid (25 joueurs)

> **Status:** 🟡 En cours — partagé avec Party

## Symboles
`CCharacter::IsInRaid25Instance`, `IsInRaidInstance`

## Implémentation
- `TeamEntity.IsRaid` → `MaxRaidSize = 25` (vs 5 party)
- `TeamService` gère raid via `isRaid: true`
- `IsInRaid` via `TeamService.GetTeamOf(c).IsRaid`

## TODO
- Instances raid dédiées (Boss raids, `CRebornMonster`)
- Chat raid, coordination, mécaniques spécifiques
