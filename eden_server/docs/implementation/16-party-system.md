# 16 - Système de Party (Groupe)

> **Status:** 🟡 En cours — service livré  
> **Priorité:** CRITIQUE

## Symboles
`CTeamManager`, `CTeamMember`, `CCharacter::OnCreateTeam/OnQuitFromTeam`,
`CEC_PartyExp`

## Implémentation
- `Game/Party/TeamEntity.cs` — Party (5 max) / Raid (25 max), LootRule
- `Game/Services/TeamService.cs`
  - `CreateTeam` / `AddMember` / `LeaveTeam` / `KickMember` /
    `TransferLeadership`
  - `ShareExperience` — bonus de groupe (base ×(1+0.1×(n-1))/n)
  - `PickLootReceiver` — FFA / RoundRobin / LeaderOnly / Random
- Promotion du membre suivant quand le leader quitte
- Dissolution quand le dernier membre part

## Tests — `TeamServiceTests` (7)
party pleine à 5, raid à 25, promotion leader, kick leader-only,
scaling XP, loot rules.
