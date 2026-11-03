# 20 - Système de Donjons

> **Status:** 🟡 En cours — données + instances livrées

## Symboles
`CBattlefield`, `CBattlefieldFactory`, `CDoor`

## Données — S_Battlefield.ini (`V.4,59,`)
[id, nom, mode, joueurs requis, mapId, ?, minLevel, ... , 3×9 paliers
de récompenses aux cols 31-39 / 40-48 / 50-58]

## Implémentation
- `Data/Parsers/BattlefieldParser.cs` — `BattlefieldDef`
- `Game/Services/DungeonService.cs`
  - `CanEnter(level, partySize)`, `Enter(...) → DungeonInstance`,
    `Complete(instanceId, tier) → rewards`, `Abandon`
  - États : Preparing/InProgress/Completed/Failed

## TODO
- Modes solo/3p/5p réels, respawn/checkpoint, `CDoor` entrées
