# Vague 1-C — MailWindow + AuctionWindow (HUD courrier/enchères)

Date : 2026-10-26. Dossier : `portage/eden_godot/` uniquement.
Référence wire (lecture seule) : `eden_server/server/.../ZoneMessages.g.cs`,
`Eden.Zone/Handlers/SocialHandlers.cs` (mail), `Handlers/ShopHandlers.cs`
(enchères), `ZoneEntryFlow.Mission.cs` (relais mission→client 68-77→400-409),
`ZoneEntryFlow.Dialogue.cs` (services NPC : 7=mail→399, 18=auction→430).

## Audit — le client ne parle PAS à la mission

`MissionBridge.cs` n'est qu'un pont de NCIDs **zone** (quêtes/portails/
monture) — aucune connexion mission directe n'existe. Mail et enchères
passent par le lien zone : la zone relaie en interne les requêtes mail à la
mission via NCID 21 AgentDBACommand (texte) et re-route les réponses
mission 68-77 vers le client en 400-409. Les enchères sont 100% zone-locales
(`AuctionService` + `active_auctions` SQL).

## NCIDs couverts

Mail — C→S :
- **95** MailOpenBox `[s16 op]` — op=7 à l'ouverture (ResetOperation natif).
- **96** MailQuery `[s16 page][s16 onlyFriend]` → liste 400.
- **97** MailRead `[i32 mailId]` → 401.
- **98** MailDelete `[i32 mailId][u16 n][n×i32]` → 402 `[s16 res][i32 id]`.
- **99** MailSend `[str to][str titre][str corps][s16 bag][s16 slot]
  [i32 or][i32 cod][u8 flag]` → 403 `[s16 res]` (0=ok).
- **100** MailGetItem `[i32 mailId][u16 n][n×i32][s16 freeNeeded]` →
  405 `[i32 res]` + 274 (item au sac — déjà bindé PlayerBag).
- **101** MailGetGold `[i32 mailId]` → 406 `[i32 res]` + 280 (or).
- **102** MailReturn `[i32 mailId]` → 409 `[s16 res]`.

Mail — S→C :
- **399** MailOpenBox `[i32 npcUid]` — ouvre la fenêtre (service NPC 7).
- **400** MailList `[u16 n][n×record ~120B][s8][u16 m][m×s16]` — record :
  mailId/sender/sendTime/subject/flag lu/content/attachItemId+qty/
  expireTime i64/attachGold + ~50 champs padding lus et ignorés
  (Deserialize de `CNC_CZ_ClientMailList`, ZoneMessages.g.cs:9308).
- **401** MailRead `[s16 res]` (mission émet `[i32 char][s16][s16 res]` →
  zone forwarde `body[6..]` = s16 — HYPOTHESIS serveur : record natif plus
  riche non reproduit ; parse tolérant).
- **402** `[s16 res][i32 mailId]`, **403** `[s16 res]`,
  **404** MailReceive `[s16][s16]` (non émis par la mission C# actuelle —
  parsé, sert à requêter+notifier si un jour poussé),
  **405** `[i32 res]` (1=ok), **406** `[i32 res]` (1=ok),
  **409** `[s16 res]` (1=ok).

Enchères — C→S :
- **114** AuctionBuyQuery `[s16][s16][i32][i32][i32][s16×4][str][s16]` —
  filtres natifs IGNORÉS par le serveur (`Auctions.Search()` sans args) ;
  on envoie les défauts, filtre nom côté client.
- **115** AuctionBuy `[i32 auctionId]` → 426 vide (achat direct =
  buyout/prix — pas d'enchère progressive côté serveur).
- **116** AuctionSellQuery `[s16][s16]` → 428.
- **117** AuctionSell `[s16 bag][s16 slot][i32 price][i32 buyout]` →
  429 `[s16 res]` (frais dépôt 5% serveur, durée 48h fixe serveur).
- **118** AuctionCancel `[i32 auctionId]` → 427 vide (item rendu par mail).

Enchères — S→C :
- **430** AuctionHouse `[i32 npcUid]` — ouvre la fenêtre (service NPC 18).
- **425** ClientAuctionBuyList `[s16 cat][u16 n][n×record]` —
  record : auctionId/itemId/price/flag/itemName/expireTime i64/sellerId
  + ~40 champs padding (ZoneMessages.g.cs:2421).
- **428** ClientAuctionSellList `[u16 n][n×même record]`.
- **426/427** corps vide, **429** `[s16 res]`.

## Comportement fenêtre

- `MailState`/`AuctionState` : états statiques + event Changed (pattern
  ShopState/StorageState) dans les fichiers codecs.
- 399 → `MailWindow` ouverte + envoi 95(op=7)+96 ; 400 remplit la liste
  (expéditeur/sujet/date/icône PJ/lu) ; clic mail → lecture + 97 ;
  boutons prendre item/or (100/101), répondre (onglet écrire pré-rempli),
  retourner (102), supprimer (98), « tout prendre » (100 lot mailId=0).
  Onglet Écrire : destinataire/sujet/corps/or/COD + slot pièce jointe
  (drop depuis l'inventaire, payload {bag,slot} des ItemCell).
- 430 → `AuctionWindow` ouverte + envoi 114+116 ; onglet Parcourir
  (liste 425 + recherche nom locale + Acheter 115), onglet Mes ventes
  (428 + création depuis inventaire 117 prix+buyout + Annuler 118).
- Layouts natifs `MailWnd.layout`/`AuctionWnd.layout` (FrameWindow +
  TabControl) quand uidata présent, repli style sombre sinon.
- GameScene.RegisterZoneDispatch pose les binds ; 293 ferme les états.
- `--hud-open=mail|auction` : ouvre via nom de nœud (MailWnd/AuctionWnd)
  + données mock si état vide (preuve visuelle sans serveur).

## Résidus assumés

- Pas d'enchère progressive (le serveur n'a que l'achat direct).
- COD non remonté dans le 400 (la zone ne mappe pas MailRow.CodAmount).
- Page/onlyFriend du 96 ignorés par le serveur C# (liste complète).
- ItemName des annonces vient du serveur (str du record) — affiché tel
  quel, résolution t_item en secours.
- 404 non émis par la mission C# (parse prêt seulement).
- Achat d'annonce dont l'item déborde `player_mail.item_id` (smallint
  < 32768) : le settle serveur lève 22003 et l'annonce saute du
  catalogue en mémoire — limitation serveur documentée, aucun envoi
  côté client à changer.

## Preuves live — 2026-10-26 (client Godot, compte admin, char test3)

Build `dotnet build` : 0 avertissement / 0 erreur. `dotnet test
tools/NetTests` : 361/361 verts.

Sondes déterministes `--mail-drive` / `--auction-drive` (nœuds DriveProbe
dans les fenêtres — rejouent les méthodes exactes des boutons UI).
Logs : `/tmp/eden-client-mail5.log`, `/tmp/eden-client-auction5.log`.
Captures : `godot-shots/mail-window.png`, `godot-shots/auction-window.png`
(1366×768, layouts natifs `MailWnd.layout`/`AuctionWnd.layout` → ok).

### Courrier — chaîne complète prouvée

| Étape | Wire | Résultat |
|---|---|---|
| Ouvrir boîte + liste | D 95 op=7, D 96 → E 399 (npc=0), E 400 ×6 | liste remplie |
| Lire | D 97 id=12 → E 401 res=0 | lecture ok |
| Prendre PJ item | D 100 id=12 → E 405 res=1 + NCID 274 | item 32034 au sac |
| Prendre or | D 97+D 101 id=5 → E 406 res=1 | gold 2500 crédité (58887→61383 avec frais envoi) |
| Renvoyer | D 97+D 102 id=11 → E 409 res=1 | `player_mail.returned=1` |
| Écrire/envoyer | D 99 → E 403 res=0 | mail id=13 « test mail-drive » créé pour test2 (200000003) |
| Supprimer | D 98 → E 402 res=1 | ligne supprimée en base |
| Notif nouveau | E 404 ×2 reçues | badge maj |
| Re-liste | D 96 → E 400 ×5 | liste rafraîchie |

### Enchères — chaîne complète prouvée

| Étape | Wire | Résultat |
|---|---|---|
| Catalogue + mes ventes | D 114, D 116 → E 425 ×3, E 428 ×0 | listes remplies |
| Acheter #3 (item 24170, 999) | D 115 → **E 426** | `active_auctions.status=sold`, mail « Auction purchase » id=11 livré, gold −999 |
| Vendre (essai 1, item lié) | D 117 item=40322 bind=1 → silence | refus serveur `IsBound` — le client retente sur item non lié |
| Vendre item 32034 | D 117 → **E 429 res=0** | listing #6 créé (48h, dépôt 5% ≈38 prélevé) |
| Mes ventes | D 116 → E 428 ×1 | annonce #6 listée |
| Annuler #6 | D 118 → **E 427** | status=cancelled, mail « Auction return » id=12 livré |
| Re-listes | D 114/D 116 → E 425 ×2, E 428 ×0 | catalogue 3→2, ventes à 0 |

NCID 430 bindé (`ZoneAuctionHouse`) mais non déclenché en session —
la zone ne l'émet que sur ouverture au PNJ (service 18), pas sur la
requête 114 directe. 399 déclenché via la requête 95.

### Incidents serveur rencontrés (données de seed, pas le client)

- `player_mail` séquence `mail_serial` non avancée après insertion
  manuelle → 23505 sur envoi 99 ; corrigé par `setval`.
- `item_serial` idem pour `mailitem` (settle achat) ; corrigé.
- `player_mail.item_id` smallint : annonces seed item_id > 32767 →
  22003 au settle ; seeds corrigés (24170/31900) + restart zone pour
  recharger `AuctionService._listings` (catalogue en mémoire).
