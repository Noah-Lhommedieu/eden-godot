# 19 - Système de Guildes/Familles

> **Status:** 🟡 En cours — service livré

## Symboles
`CFamily`, `CFamilyDBO`, `CFamilyBattle`, `CFamilyBuildingShop`,
`CCharacter::FamilyLevyTaxes/FamilyLevyBusinessTaxes`

## Implémentation — `GuildService.cs`
- `CreateGuild` / `AddMember` / `LeaveGuild` / `SetRank`
- Rangs : Leader, Officer, Veteran, Member, Recruit
- `MaxMembers = 20 + Level × 10`
- `AddExperience` (RewardFamilyExp), level-up par seuil
- `LevyTax` / `DistributeTreasury` — économie de guilde
- Transfert de leadership au départ du leader

## TODO
- Family battles / Territory War (`CFamilyBattle`)
- Bâtiments de guilde (`CFamilyBuildingShop`)
- Persistance DB (`guilds`, `guild_members`)
- Coffre de guilde reliée à `family_bank`
