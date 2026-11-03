# Lot S2 — Équipement & inventaire complet (plan-de-secours)

Référence wire (toutes prouvées serveur — `.g.cs` + handlers
`Eden.Zone/Handlers/InventoryHandlers.cs` + `ItemActionHandlers.cs`) :

| NCID | Dir | Nom | Corps | Usage |
|---|---|---|---|---|
| 7 | C→S | `CNE_CZ_ZoneServerSwitchItemLocation` | `[s16 srcType][s16 srcSlot][s16 srcIdx][s16 dstType][s16 dstSlot][s16 dstIdx]` | move (0→0), équipe (0→1), déséquipe (1→0). type 0=sac (idx=sac), 1=équipement (idx=groupe 0, slot=loc). −1 → résolution serveur (premier libre / loc canonique) |
| 8 | C→S | `ZoneUpdateEquipment` (existant) | `[u32 mask]` | mask = locs modifiés → réponse 282 broadcast |
| 6 | C→S | `CNE_CZ_ZoneServerDropItem` | `[s16 type][s16 loc][s16 index]` | **détruit** l'item (pas de drop au sol natif) — NON câblé UI (trop destructif) |
| 19 | C→S | `CNE_CZ_ZoneServerItemUse` | `[s16 contType][s16 contIdx][s16 slot][i32 cible][u8str][3×s16 cible(cont,idx,loc)]` | consommable (contType 0, cible −1) |
| 84 | C→S | `CNE_CZ_ZoneServerDivideItem` | `[s16 srcSlot][s16 srcSac][s16 dstSlot][s16 dstSac][s16 qté][s16 type]` | split de stack |
| 277 | S→C | `ClientSwitchItemLocationOK` | écho + dst **résolus** + `u8 résultat` (1 ok) | application locale du move/unequip |
| 274 | S→C | `ClientPutItemIntoInventory` | `[u16 itemId][s16 sac][s16 loc]` + record WriteItem (NatifItemCodec.BuildPutItem) | ajout d'item |
| 275 | S→C | `ClientDropItem` | `[s16 type][s16 loc][s16 idx]` | retrait slot (type 0 sac, 1 équipement) |
| 291 | S→C | `ClientModifyItemDurability` | `[s16 contType][s16 slot][s16 valeur][s16 maxDur][s16 contIdx][s16]` | count/durabilité |
| 303 | S→C | `ClientItemUse` | `[i32 charId][i32 0][u16 itemId][3×s16 echo][s16 3]` | ack d'usage |
| 282 | S→C | `ClientUpdateEquipment` (existant) | état porté complet | sync PlayerEquipment |

Séquence équipe native : SwitchItemLocation (0,slot,sac)→(1,loc,0)
→ 277 ack → client envoie UpdateEquipment(8) mask → 282 (état complet,
broadcast soi inclus) + 283. Déséquipe : (1,loc,0)→(0,−1,−1) puis 8.

Sémantique locs équipement (désasm Equip @0x80b5810 — table serveur
`NativeEquipmentSlots`) : type 1-6→loc=id, 7|19→{7,8}, 8→{8},
9→{7}, 10→{11}, 11|20→{12,13}, 12→{14,15}, 13-18→id, 35→{10}.
Kinds équipables joueur : 1-34, 37-40.

Visuel équipement (`data/db/4pequip.ini`, sections [TableX],
lettre corps b/g/m/f/k/l/c → Boy/Girl/Mouse/Frog/Kuma/Loli/Shota) :
`DBn=<code|modelCode>,<grp>,<mesh>,<skin>,…`
- grp 0 → mesh `#B2xx` dans le mega-GLB corps + peau `SB2xx`
- grp 2 → GLB séparé `char/model/<mesh>.glb` (casques — attache tête ;
  mesh NULL = invisible)
- grp 1/4 → GLB `char/model/<X…>.glb` (capes/dos — attache spine)
- Armes : item `Wxxxxx` → `item/model/<Wxxx>.glb` (4 premiers chars)
  attaché à `Bip01 Prop1` (main droite, locs 7/9), `Bip01 Prop2`
  (loc 8 bouclier/off-hand, loc 10 arc — HYPOTHESIS main gauche).

## Fichiers

- `scripts/protocol/ZoneItemMessages.cs` (nouveau) : codecs 7/6/19/84
  C→S + 277/274/275/291/303 S→C + helpers sémantiques
  (`ZoneItemActions` : AllowedLocations, PredictEquipLoc,
  IsPlayerEquippableKind) — miroir documenté de NativeEquipmentSlots.
- `scripts/protocol/NativeItemRecord.cs` : +`TryReadPutItem` (record
  274 = `[u16 id][s16 sac][s16 loc]` puis corps Inventory sans id —
  méthode additive, zéro changement des existants).
- `scripts/hud/ItemTooltip.cs` (nouveau) : tooltip riche — builder de
  Control (nom EN, type, stats ATK/DEF/crit…, prix, durabilité,
  niveau requis, sockets, description) + `FormatLines` pur testable.
- `scripts/hud/EquipSlotsPanel.cs` (nouveau) : panneau slots portés
  (locs 1-8, 10-15) alimenté par PlayerEquipment.Worn ; double-clic →
  déséquipe ; drop cible pour drag depuis inventaire ; applique le
  visuel équipement au corps 3D (CharVisual.ApplyEquipment) sur
  PlayerEquipment.Changed.
- `scripts/hud/InventoryWindow.cs` : tracking (sac,slot)→InventorySlot,
  lazy-bind session (274/275/277/291/303), deltas locaux (move appliqué
  au 277 — le serveur n'émet pas de 274/275 pour un move 0→0),
  double-clic équipe/consommable, drag&drop Godot, shift+drop → split,
  tooltip riche via ItemTooltip.
- `scripts/game/CharModelSpec.cs` : +lettre→table pequip, stem arme,
  loader `4pequip.ini` (code|modelCode → mesh/skin/grp).
- `scripts/game/CharVisual.cs` : +`ApplyEquipment(body, worn, lookup)` —
  toggling meshes tenue, attache GLB casque/cape/arme via
  BoneAttachment3D (Prop1/Prop2/Head) ; aucun changement des
  fonctions existantes.
- `tools/NetTests/ZoneItemTests.cs` : roundtrips/goldens wire.

## Validation

dotnet build 0/0 · dotnet test NetTests · live admin/adminmdp :
équipe arme → 277+282 reçus + mesh en main · tooltips · potion.
