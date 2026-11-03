# Item Mall + Neobank (client Godot)

> Audits `re/ghidra/rf_itemmall` (ItemMall.dll) et `re/ghidra/rf_neobank`
> (NeoInventoryBank.dll) + protocole mall serveur C#. Cible : fenêtres
> `ItemMallWindow` / `NeobankWindow` montées via `HudExtensions`, codecs
> dans `ZoneItemMallMessages.cs` / `ZoneNeobankMessages.cs`, preuve
> runtime `--hud-open` + screenshots.

## Audit DLL Reforged (fait — aucune surprise)

### rf_itemmall — ItemMall.dll (`mods/_testing/`, `ItemMallInit/Pause`,
`RfgModInit/PrepareExit`)

- **AUCUN protocole propre** : DLL de byte-patchs. Strings =
  hooks (`FUN_100013e0` = HookMgr) : `purchase hook bytes unavailable`,
  `paced purchasing active`, `purchase-only publication failed`,
  `shader-cache suppression active`, `shader-cache patch installed`,
  `purchase hook failed`, ini `IntervalMs`/`MaximumPackages`/`ShaderCache`.
  Vérifie le PE du client (MZ/PE timestamp), épingle le module en RAM,
  écrit des patchs RVA, crée un timer périodique.
- Le pacing mall = patch côté client sur le chemin d'achat **natif** →
  le protocole mall reste le CW natif.

### Protocole item mall (client↔**world** :5200, connexion gardée en jeu)

| NCID | Sens | Nom | Format | Preuve |
|---|---|---|---|---|
| 15 | C→W | `CNE_CW_ServerItemMallAnythingReceivable` | vide | serveur `ItemMallHandlers` |
| 17 | C→W | `ServerItemMallReceivableQuery` | `[u16][str][str]` | mitm `0x011` |
| 18 | C→W | `ServerItemMallReceieveAttempt` | `[u32 receipt]` | serveur |
| 23 | C→W | `ServerItemMallReqBuyItem` | `[s16 mu*100+grp][s16 idx][s16 amount][u16 version][str][str][str]` | désasm world + serveur |
| 24 | C→W | `ServerItemMallListQuery` | `[s16][u16]` | serveur |
| 25 | C→W | `ServerItemMallPointInfo` | vide | mitm `0x019` |
| 56 | W→C | `ClientItemMallAnythingReceivable` | `[u8 bool]` | serveur |
| 57 | W→C | `ClientItemMallReceivableList` | `[s16 n][n×{u32 id,u16 item,u16 num,str src}]` | serveur (mitm confirme 0x0a00=10) |
| 58 | W→C | `ClientItemMallReceivableAttempt` | `[s16 result][u32 receipt]` | serveur |
| 62 | W→C | `ClientItemMallList` | `[u16 ver][u16 n][n×{s16 key,s16 idx,u16 item,u16 num,u32 point,u32 special,u16 numlim,i64 color,i32 notsell,str bag,str note,u16 lvl,i32 sell}]` batches ≤20 | serveur `ItemMallPackets.BuildListBatch` |
| 63 | W→C | `ClientItemMallResBuyItem` | `[s16 res][i32 p1][i32 p2]` | serveur |
| 64 | W→C | `ClientItemMallPointInfo` | `[i32 pvalues][i32 bonus]` | mitm `0x040` |
| 79 | W→C | `ClientItemMallLimitAmount` | `[u16 ver][u16 n][n×{s16 key,s16 idx,u16 item,u16 num,u16 sold}]` | serveur |
| 70 | C→Z | `CNE_CZ_ZoneServerItemMallReceivableEnter` | vide | serveur `ZoneMessages.g` |
| 83 | C→Z | `ZoneServerItemMallUseItem` | `[s16][s16][s16][i32][str]` | serveur `ZoneMessages.g` |

- money_unit : 1=pvalues(AP), 2=bonus, 3=play time (serveur
  `ItemMallHandlers`). group seed : 1,3,4,5,6,7,8(=num_limit),50,99.
- Serveur complet : `Eden.World/Handlers/ItemMallHandlers.cs`,
  `ItemMallCatalog`, `ItemMallPackets`, `ItemMallRepository`
  (`fnitemmall.itemmall/item_receivable/itemmall_limit_amount`,
  `fnmember.tb_user.pvalues/bonus`), livraison Z→ZW→Z via
  `ZoneEntryFlow.ItemMall.cs` (+ CZ 70 Enter receivable + 83 UseItem).
- Client déjà : CW 17/25 + 64 dans `WorldMessages.cs`.

### rf_neobank — NeoInventoryBank.dll

- **AUCUN protocole** : extension UI pure greffée sur `StorageUI.layout`
  (banque) et `Root/Bag` (inventaire). Strings `neo/Bank/*` :
  `Scroll`, `Footer`, `QueryBackground`, `HoverBackground`, `Row`,
  `Columns`, `Rows`, `Mode`, `Previous`, `Page`, `Next`, `Query`,
  `HoverId`, `SearchHint`, `Popup`, `Choice0`..`Choice19`, `bag%u`.
- Deux features ini `neoBank_StorageUI`/`neoBank_Inventory` :
  `Columns` (16..32, défaut 16), `Rows` (1..20, défaut 20/4),
  `ScrollMode` (0/1 : scroll natif / fenêtré paginé), grid de slots
  `bag%u`, recherche (`SearchHint` + Query + `HoverId` highlight +
  hint « ID: %u » / « Match »), navigation `Previous`/`Page "x/y"`/`Next`,
  popup `Choice0-19` (menu contextuel), **LockedPool** : cadre doré
  `FFFFFF60` sur fond sombre `A0000000`/`28FFD040` par slot =
  verrous d'items côté client.
- Données = protocole banque natif déjà implémenté : ZC 357 enter →
  CZ 62 `ZoneGetStorageData` → ZC 358 contenu ; dépôt CZ 60, retrait
  CZ 61 (`BankWindow` + `StorageState`). Aucun travail serveur requis.

## Plan

- [x] `ZoneItemMallMessages.cs` : codecs Z 70/83 (Enter/UseItem) +
      messages mall **world** manquants (15,18,23,24,56,57,58,62,63,79)
      + `ItemMallState` (catalogue, points, receivables, version).
- [x] `ZoneNeobankMessages.cs` : pas de wire neuf — doc + paramètres
      neobank `NeobankSettings` (cols/rows/scroll/enabled, cfg
      `user://neo_bank.cfg`) + verrous client `NeobankLocks`.
- [x] `ItemMallWindow.cs` : `ItemMall.layout` + contenu — onglets
      money-unit (tab→unité 1/2/3), liste groupes (panel/list),
      grille items en lignes `ItemMaillItem.layout` (icône c_item/
      c_itemmall code→texture, nom `TextResolver.ItemName`, promo
      barrée), pagination native (mostback/back/next/mostnext +
      pageText), réclamables itembox + getallBtn, soldes moneyText/
      BonusmoneyText/ForeverGoldText ; send world 15/17/18/23/24/25 ;
      parse 56/57/58/62/63/64/79 ; CZ 70 posé à l'ouverture.
- [x] `NeobankWindow.cs` : `StorageUI2.layout` redimensionné à la
      grille Columns×Rows (cellules bag%u indexées par slot) —
      footer Columns/Rows/Mode + Previous/Page/Next, recherche
      (nom/ID, highlight vert + hint Match + HoverId), popup
      Retirer/Verrouiller/Annuler, verrous LockedPool persistés,
      dépôt via colonne sac (extension locale), bascule
      classique↔néo (bouton « Neo+ » de BankWindow /
      « Classic » ici — `StorageState.NotifyChanged`).
- [x] `EdenSession` : `OnWorld` + `WorldListenLoopAsync` (miroir
      zone) ; `Emit(world:)` route les flux synchrones vers la bonne
      table ; `SocialWindow` refactoré sur OnWorld (plus de pompe
      privée — un seul lecteur monde).
- [x] `GameScene` : `WorldListenLoopAsync` lancée en parallèle de
      `ZoneListenLoopAsync` après l'entrée (Task.Run, token live).
- [x] `HudExtensions` : mount `ItemMallWnd` + `NeobankWnd`.
- [x] `HudRoot` : action menu `itemmall` → `ItemMallWindow.Toggle`,
      clés `itemmall|mall`/`neobank` dans `ForceOpen` (EnsureDemo
      hors session), noms dump `ItemMallWnd`/`Root_ItemMal`/
      `NeobankWnd`.
- [x] `MenuIconsPanel` : `itemmall` déjà émis ✓ (rien à faire).
- [x] NetTests : `ZoneItemMallTests.cs` — round-trips 17/18/23/24/
      56/57/58/62/63/79 + zone 70/83 + flux `ItemMallState` +
      cycles `NeobankSettings`/`NeobankLocks`.
- [x] `dotnet build` 0/0 (erreurs transitoires du lot parallèle
      buffs/trade résorbées par leurs agents) + `dotnet test
      tools/NetTests` vert — 410/410.
- [x] Runtime : `--hud-open=itemmall` → `Root_ItemMal (302,111)
      762×546`, `ItemMaillItem.layout` instancié ×8 (démo) ;
      `--hud-open=neobank` → fenêtre centrée ~776×438
      (StorageUI2), banque classique masquée (diff pixels).
      Captures : `godot-shots/itemmall-window.png`,
      `godot-shots/neobank-window.png`.

## Notes d'implémentation

- Le catalogue 62 est servi en **lots ≤20** — `ItemMallState.Entries`
  fusionne par clé `(key<<16)|index` ; le 79 n'apporte que les
  compteurs `Sold`.
- Onglet money_unit ↔ `tab` natif : mappage 1/2/3 sur les 3 onglets
  = **inférence documentée** (la DLL ne précise pas la
  correspondance ; ForeverGold rabattu sur pvalues serveur →
  `ForeverGoldText = "—"`).
- `ItemTableCache` charge aussi `c_itemmall.ini` (ids mall 40001+
  absents de c_item) — même parser `ItemTable`.
- `NeobankSettings.Enabled` (défaut true) : à l'ouverture banque la
  vue néo prend le relais de `BankWindow` ; même état/protocole.
- Aucune écriture serveur requise : tous les NCIDs émis existent
  dans les dispatchers C# (monde 15/17/18/23/24/25 ; zone 70/83 ;
  banque 60/61/62 inchangés).
