# 25 - Système de Couple/Mariage

> **Status:** 🟡 En cours — service livré

## Symboles
`CLover`, `CWedding`, `CEventWedding`,
`CCharacter::GetLover/IsInWedding`, `CEC_GotoLover`

## Implémentation — `LoverService.cs`
- `Propose` / `Accept` / `Decline` — formation du couple
- `Marry` (date de mariage), `Breakup` (divorce)
- `AddAffection`, `GetPartnerForTeleport` (CEC_GotoLover)
- Un seul couple actif par joueur

## TODO
- Buffs de couple, cérémonie de mariage (CEventWedding), lover chat
