# Lot : TradeRequest natif (distance + invite) — 2026-10-20

## Preuve (`CPlayerFactory::RequestTrade` zone:138188 +
`CNE_CZ_ZoneServerTradeRequest::Execute` zone:536780)
- Rate-limit 0.5 s par perso (char+0x650).
- Cible sur le même node, `CloseEnough(cible, 10.0)`.
- Trop loin → `ClientTradeCancel [u32 partenaire][s16 1]` au demandeur
  + texte 0x333.
- Succès → `ClientTradeRequest [u32 demandeur][str nom]` à la CIBLE.

## Corrections
- [x] Distance réelle entre entités (était : 0 passé au service, la
      validation `MaxTradeDistance=10` ne servait pas)
- [x] ClientTradeRequest rempli (était : paquet vide envoyé à la cible —
      pas de demandeur ni de nom)
- [x] ClientTradeCancel + texte 0x333 sur échec distance
- [x] Rate-limit 0.5 s (dict charId→timestamp)
- [x] `MaxTradeDistance` rendu public (documenté natif)

## Hors scope (constats natifs non implémentables proprement)
- `IsEncaged`/`IsNoTrade` — pas de flags prison/commerce dans notre modèle
- Ops actives 0x20-0x2c → 0x607, char+0x95c → 0x64c — déjà gérés en
  partie par TradeService (sessions d'échange en cours)
