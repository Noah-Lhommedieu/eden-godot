# 29 - Système de Familiers (Elfs)

> **Status:** 🟡 En cours — données + service livrés

## Symboles
`CElfContainer`, `CNE_CZ_ZoneServerElf*`, `CEC_Elf*` (mood/work/energy)

## Données — S_Elf.ini (`V.7,42,`, 226 elfs)
[id, code J10101, race, nom, level, stats base ×6, spell, skills ×4,
 coût, ?, scripts de mood ×6 `#id#id#weight#Effect##`]

## Implémentation
- `ElfParser.cs` — `ElfDef` (skills, scripts de mood)
- `ElfService.cs` — `ElfInstance` { Mood, Energy, Friendship, IsWorking }
  - `Acquire`, `ConsumeEnergy`, `AdjustMood`, `Feed`, `AdjustFriendship`
  - Intégré au craft : `CraftService.CraftElfAsync` consomme l'énergie

## TODO
- Évolution/fusion d'elfs, `S_ElfCollect.ini`, `S_ElfEvent.ini`,
  `S_ElfCombineElf.ini` (coûts de craft par elfe)
