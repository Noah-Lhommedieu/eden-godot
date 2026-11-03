# Plan temp — Phase 6 vague 2 : BankWindow complète + coffre famille

Date : 2026-04-13 · Lot : banque perso + family storage + extension slots

## Audit (avant code)

- `BankWindow.cs` existant : onglets par conteneur du 358, liste
  sac droite, dbl-clic dépôt (60) / retrait (61). Pas de grille de
  slots, pas de drag-drop, pas d'extension, pas de coffre famille.
- Protocole prouvé serveur (`InventoryHandlers.cs`,
  `ZoneEntryFlow.Dialogue.cs`, `FamilyStoragePackets.cs`,
  `ZoneMessages.g.cs`) :
  - 357 EnterStorage [i32 npcUid] → client demande 62 → 358 contenu ;
  - 7 SwitchItemLocation type 4 = banque : 0→4 dépôt stack entier
    (auto-merge), 4→0 retrait, 4→4 swap (loc globaux) — ack 277 ;
  - 69 StorageExpand (vide) → 373 [s16] (0 ok, 1 cap 2 expansions,
    2 or insuffisant) + TextOutputIndex 0x5ab4/5aad/5aae ;
  - 507 EnterFamilyStorage [i32 npcUid] (service NPC type 19) ;
  - 508 par coffre non vide `[s16 0][s16 idx][u16 n]{[s16 loc][record
    FIXE]}[s16 0]` — record sans mask (WriteStorageItem serveur) ;
  - 177 put [s16 coffre][s16 loc -1=auto][s16 sac][s16 slot] ;
  - 178 get [s16 coffre][s16 loc][s16 sac -1→0][s16 slot -1=auto] ;
  - 504 delete [s16 idx][s16 loc], 505 put notify (même record),
    506 log [u16 n]{s16 action,str}, 179 query log.
- Or banque : **aucun NCID or de stockage** (ni perso ni famille —
  la trésorerie famille = 159 déjà dans GuildWindow). Pas
  d'implémentation → résidu documenté.
- Ouverture : banque = service NPC 6 → 357 (déjà bindé GameScene) ;
  famille = service NPC 19 → 507 (nouveau bind). Pas d'accès
  « always-open » prouvé côté serveur — fenêtre via NPC.

## Implémentation

- [x] `scripts/protocol/ZoneStorageMessages.cs` : codecs 69/373,
      177/178/179, 504/505/506/507/508 + lecteur record fixe +
      `FamilyStorageState` + `ZoneBankActions` (requêtes 7 type 4).
- [x] `scripts/hud/StorageItemCell.cs` : cellule partagée
      (icône/qté/tooltip + payload `{kind,cont,slot,bag}`).
- [x] `scripts/hud/BankWindow.cs` : grille de cellules par onglet
      (capacité du 358), drag-drop 0↔4/4↔4, bouton « Étendre »,
      garde 60/61 en dbl-clic.
- [x] `scripts/hud/FamilyStorageWindow.cs` : layout
      `FamilyStorageWnd.layout`, 3 onglets × 40 cellules, panneau
      sac, journal 179→506.
- [x] `HudExtensions.MountAll` : `FamilyStorageWnd` montée.
- [x] `GameScene.RegisterZoneDispatch` : binds 507/508/504/505/506/
      373 (+62 refresh après expansion OK).
- [x] `HudRoot.ForceOpen` : clés `bank` + `familystorage`/`fchest`
      (+ démo hors session via EnsureDemo).
- [x] `GameScene.SendChatAsync` : `//cmd` → NCID 3 TextCommand
      (chemin natif GM — requis pour //transport_node vers le
      banquier ; avant, tout partait en NCID 34).
- [x] `tools/NetTests/ZoneStorageTests.cs` : 11 tests — 177/178,
      504/506, 508 record fixe, 373, état.
- [x] Build 0 erreur + 373 tests verts (avant édits concurrentes
      d'un autre lot dans le même arbre — erreurs étrangères dans
      ZoneLoverMessages/ZoneEnchantMessages/ItemMallWindow).
- [ ] Live : //transport_node 1 → banquier 50346 (s001) →
      ouverture 357/358, dépôt/retrait réels, extension 69/373,
      coffre famille via 177/178/179 (pas de NPC service 19
      spawné — preuve directe), captures + hud-dump.

## Risques / résidus attendus

- `loc` banque = global (pas par onglet) — grille indexée par loc.
- Coffres 1/2 verrouillés par privilège famille : le serveur refuse
  silencieusement (ou texte) — l'UI montre les 3 onglets.
- family_storage vide en base → dépôt réel requis pour preuve 505.
- `Amount` vs `Durability` pour les stacks : count = Durability pour
  les stackables (convention serveur `IsStackable`), pas `Amount`.
- **Entrée 507 intestable dans ce monde** : le seul NPC service 19
  (53007 Town Clerk, `Service 4568 19`) n'est spawné dans aucune
  scène sNNN.ini — le bind + parse + fenêtre restent couverts, le
  chemin d'ouverture par dialogue est documenté comme non prouvé
  live (service 19 dispatché par RunNpcServiceAsync natif).
- Or en banque : aucun NCID — non implémenté (documenté).
