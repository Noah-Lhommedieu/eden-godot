# 37 - Système de Mail

> **Status:** 🟡 En cours — service livré

## Symboles
`CMailShop`, `CNE_ZMis_MissionServerMail*`

## Implémentation — `MailService.cs`
- `Send` — sujet/corps + gold + items (max 5) + COD
- `GetInbox` / `MarkRead` / `ClaimAttachments` / `Delete`
- TTL 30 jours, `PurgeExpired` — renvoie les pièces jointes non
  réclamées à l'expéditeur

## TODO
- Persistance DB, notifications temps réel, COD paiement effectif
