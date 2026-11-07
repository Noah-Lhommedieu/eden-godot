# Ville de guilde (S501/S502) : monde, PNJ, ressources — 2026-11-06

Statut : `IMPLEMENTED` serveur (`dotnet test` 1796 verts), client **non compilé,
non vu à l'écran**. Rien n'est `VISUALLY_VALIDATED`. Le lead teste en jeu.

Sources utilisées : décompilé zone (`re/reindex`), `corpus/fr-full/data/{db,scene}`,
guides FR `guilde-et-ville-de-guile.md` + `le-craft.md`, code serveur/client,
logs `/tmp/zone.log` + `/tmp/client.log` du 2026-11-06. Aucun rapport IA antérieur lu.

## 1. Ce que le natif pose dans la ville

Preuve : `lapis::CZoneServer::InitFamilyNpc` zone @0x0816f5d0 (corps lu en entier).
À l'init du node `Family_<id>` :

1. **un housekeeper central** (`SFamily::GetCenterBuildingHouseKeeperPos`
   @0x084678c0 → `BornNPC`) ;
2. **une `BornNPC` par item du conteneur 0** dont `item_type` (+0x86) vaut
   0x2a ou 0x52, à (x, y, z) persistés (+0x88/+0x8c/+0x90).

Le natif ne pose **aucun autre PNJ tout seul**. Les PNJ de service sont des items
« contrat » que le chef met dans les slots d'un bâtiment (guide : bâtiments achetés
à Chaussons, PNJ achetés à Aldata, Aven). Cap de PNJ par bâtiment (hors housekeeper) =
tables `.rodata` citées par `FamilyHallRules` (`CheckNpcSizeLimit` @0x8464ea0) :

| niveau bâtiment | 3 | 4 | 5 | ≥6 |
|---|---|---|---|---|
| production (types 4-9) `ProduceNpcLimit` | 4 | 5 | 6 | 8 |
| hôtel (type 2) `HouseNpcLimit` | 2 | 3 | 3 | 3 |
| marché (type 10) `MallNpcLimit` | 0 | 1 | 1 | 2-3 |

Disposition (code existant, formule native `GetNpcPositionByNpcLoc` @0x84662b0) :
bâtiment aux locs 0/10/20/30/40 = aires `[position]` 0x33…0x37 de la scène ;
manoir = aire 0x32 (loc 100) ; slot sacré 0x33 = aire 0x38. Le NPC du slot `base+k`
est à `[position] + offsets 4PHouse[k-1]` tourné de `-f`.

### Bâtiments (c_buliding.ini, col3 = type) et leur housekeeper

Le housekeeper d'un bâtiment de production **est** le PNJ d'assignation de rang 1
(S_Buliding col6 → S_Item 35002/35012/35022/35030/35039/35068 → col35 = S_Npc) :

| type | bâtiment (code, tid niv.3) | housekeeper (base+1) | service PNJ |
|---|---|---|---|
| 1 | manoir H00101 (S501, tid 1) / H00301 (S502, tid 51) | 53007 clerc de ville | 11 family_building, 19 coffre |
| 4 | ferme H01001, tid 101 | 53008 fermier novice | 13 planting |
| 5 | mine H01101, tid 102 | 53018 mineur novice | 12 mining |
| 6 | autel H01201, tid 103 | 53028 invocateur | 14 summon |
| 7 | alchimie H01301, tid 104 | 53036 alchimiste novice | 15 |
| 8 | fonderie H01401, tid 105 | 53045 fondeur | 16 |
| 9 | mare magique H01501, tid 106 | 53074 artisan novice | 17 |
| 2 | hôtels H004/5/6 (tid 107-109) | aucun | — |
| 10 | marchés H007/8/9 (tid 110-112) | aucun | — |

Niveaux suivants : tid +12 par niveau (113 = ferme niv.4 …). Rangs supérieurs des PNJ
d'assignation : ferme 53009/53010, mine 53019/53020, alchimie 53037/53038, fonderie
53046/53047, mare 53075/53076 ; l'autel n'a qu'un invocateur. PNJ de plans/composants
(`#:Service dlg 24 <store>`) : ferme 53011/53012 (stores 501/502), mine 53021/53022
(508/509), autel 53029/53030 (515/516), alchimie 53039/53040 (522/523), fonderie
53048… (528…), mare 53077/53078 (554/555).

## 2. Matrice 4 + 1 systèmes de ressources

| # | système | déclencheur | PNJ / lieu | données | serveur | état |
|---|---|---|---|---|---|---|
| a | **mine via plan** (work type 12) | PNJ mineur → `ClientEnterWorks` (496), plan « 礦質圖 » en catalyseur, cast long | 53018/19/20 dans le bâtiment type 5 ; plans chez 53021… | `c_familycollect.ini` (cat. mine, ex. 100 = « 60級礦場礦質圖04 »), durée col10 × 600 ds (30 min ; `EDEN_DEV_WORK_CAP=100` → 10 s) | `ZoneEntryFlow.Work.cs` (HandlePlayerWorkAsync), `Dialogue.cs` svc 12-17, gates 0x251/0x392/0xa09/0x9f1 | porté avant ; **dépend des PNJ présents** (corrigé §3) |
| b | **mine physique** | clic droit sur la pioche **dans l'aire** | aire GATEWAY keyId **60** (S501 aire 3 (78,53) r12 ; S502 aire 3 (44,129) r20) | S_Item 36819/36820/36825/36826/36831/…/37097/37118 (kind 44, col32 `501 502;60`, enchant `AttrGainCollectItem`), produits `c_familycollect` 245…(catalyseur = la pioche) | `Items.cs` « AttrGainCollectItem » (jet par produit, rejet 0xe1c hors aire) | porté avant ; **aucun nœud/PNJ dans la ville** → §3 |
| c | **ferme via plan** (type 13) | PNJ fermier, plan « 農場栽種秘方 » | 53008/09/10 dans la ferme (type 4) | `c_familycollect` (cat. ferme ex. 1-2) | idem (a) | idem (a) |
| d | **ferme physique** | gants dans l'aire keyId **61** | S501 aire 4 (161,64) r11 ; S502 aire 4 (77,74) r9 | S_Item 36821/36822/36827/…/37099 (col32 `…;61`) | idem (b) | idem (b) |
| (d') | boule de cristal | aire keyId **62** (fissure de mana) | S501 aire 5 (187,49) r10 ; S502 aire 5 (101,141) r10 | S_Item 36823/36824/36829/…/37101 (`…;62`) | idem (b) | idem (b) |
| e | **invocation via plan** (type 14) | PNJ invocateur, rouleau « 召喚卷軸 » ; fin d'ordre = `BornMonster` hostile près du joueur ; le **kill** donne le loot du mob (`c_dropitem`) | 53028 dans l'autel (type 6) | `c_familycollect` cat. 2 (244 = rouleau 36277 → mobs 58965/58966 ; drops 58965 = 36305/36307/36309, 58966 = 36307/36309/36311) | `Work.cs` `MaterializeSummonAsync` (EndSummon @0x8163110) | porté avant ; dépend du PNJ présent |

Aires 63/64 de `[gateway_area]` = événements 2012 (fête des mères / mi-automne), inactifs.

## 3. Causes prouvées du constat live et corrections

1. **Bâtiments dans le mauvais sens** — cause : `FamilyHallBuildings.SpawnHouse` tournait
   le holder de `EdenDir.ToYaw(face)` = **π − f** (repère des personnages), alors que le
   natif tourne les slots NPC de **−f** (`x = bx + dx·cos f + dy·sin f`,
   `y = by + dy·cos f − dx·sin f`, @0x84662b0) et que les offsets 4PHouse sont à **−Y**
   de la façade. Test numérique sur les `[position]` : avec −f la façade regarde le
   centre de la carte sur 10 slots bâtiment sur 12 (produit scalaire ≥ 0,5) ; avec
   π − f c'est l'exact opposé. Preuve interne : log live `housekeeper … pos=136,0,115,7`
   = manoir (138,4 ; 132,6 ; f=0) + offset (−2,39 ; −16,87), tableaux d'affichage à
   y≈113-117, donc devant (−Y). Correction : `Rotation = (0, 0, -face)`
   (`scripts/world/FamilyHallBuildings.cs`). Les positions étaient bonnes.
2. **« Pas tous les bâtiments »** — la guilde de test n'en possède que **3** sur 5
   (`family_building_container` : tid 101 loc 0, 102 loc 10, 103 loc 20) ; le log client
   montre les 3 + le manoir posés. Ce n'est pas un bug de rendu : il faut en poser
   d'autres (`//hall_setup` ci-dessous). Des logs `[hallbld]` ont été ajoutés (ignorés,
   total reçu/posé).
3. **Un seul PNJ** — cause : les bâtiments posés par `//give_building` n'ont **pas de
   housekeeper** (le natif l'écrit au moment de la pose, `FamilyBuildingPutItemFromInventroy`
   @0x808a770 ; le chemin client/serveur `ContainerPut` le fait, pas la commande GM). Les
   4 PNJ de test en base (locs 1-4) ont des positions de GM (une à (220 ; 299), hors carte,
   deux au point de spawn) et sont rangés dans le groupe de la **ferme** (mineur et
   invocateur dans la ferme) : `ResolveWorksBuildingAsync` leur donne alors le bâtiment
   ferme. Corrections serveur :
   * `SpawnFamilyHallNpcsAsync` : auto-réparation des housekeepers manquants, position
     des PNJ de groupe **recalculée** par la formule native (log si écart), logs
     `[FamilyHall] npc tid=… loc=… @(x,y)`.
   * `//hall_setup [reset]` (nouveau, `Handlers/GmHallSetupHandlers.cs`) : complète le
     hall : bâtiments ferme/mine/autel/alchimie/fonderie manquants (échelon = niveau de
     guilde), niveau de ligne = niveau du template, housekeepers, puis PNJ de service
     jusqu'au cap (`FamilyCityRules.PlanPopulate`). `reset` supprime d'abord les PNJ
     déjà rangés dans les groupes. Positions = formule native uniquement.
4. **Pas de PNJ pioches/gants/cristal** — les vendeurs natifs sont 53199/53200/53201
   (dialogue-script `InventoryItem 36819 + 1`, Gold −2000…−20000 selon rang, niveau 30/45/60/70/…)
   posés **à Aven seulement** (S001 `[npc]` −143/−131/−132 ≈ (495,445)), aucune scène de
   hall n'en porte. Aucun PNJ de `c_npc` ne référence le store 679 (pioches). **Déviation
   assumée** : `InjectHallToolVendors` pose les trois vendeurs au centre des aires 60/61/62
   (log `[VDG-Gather]`). À retirer si l'on veut du 100 % natif.
5. **« Nœud » de mine/ferme absent** — il n'en existe pas dans le natif : l'aire est une
   `event_area` GATEWAY sans géométrie visible dans la scène `.ini`. Le client dessine
   désormais un anneau + un pilier + une étiquette sur chaque aire (aide visuelle **non
   native**, `SpawnGatherMarkers`). Si la carte FSM porte déjà un décor, il se superpose.
6. **Entrée dans l'aire = rejet « aire invalide »** — le client envoie `CheckNodeExit key=60..62`
   en y entrant ; il n'y a ni node 60-64 ni script → rejet 1499 + log. Le serveur ignore
   maintenant ces clés dans 501/502 (log `[VDG-Gather]`), `Migration.cs`.

## 4. Reproduire en jeu

```
tools/run.sh -- --login=test123:test123 --channel --char     # guilde QAForge #5001
G → Ville guilde                                              # node 502
//hall_setup reset      # (une fois) puis ressortir/rentrer : G → Ville guilde
```
Attendus : manoir + ferme/mine/autel (+ alchimie/fonderie) orientés vers le centre ;
housekeepers et PNJ de service à l'avant de chaque bâtiment ; trois anneaux colorés
(bleu mine, vert champ, violet mana) avec un vendeur au centre ; `//level 30`, `//give_gold`,
acheter la pioche (Bruce/53199), se tenir dans l'anneau bleu, clic droit sur la pioche.
Plan mine : PNJ 53018 → « mission d'artisanat » ; `EDEN_DEV_WORK_CAP=100` raccourcit à 10 s.
Logs : `/tmp/zone.log` `[FamilyHall]`, `[VDG-Gather]`, `[VDG-Work]`, `[Zone] Summon work` ;
`/tmp/client.log` `[hallbld]`. Commandes utiles : `//npc <id>`, `@talk`, `@dlg N`, `@cam`, `@shot`.

## 5. Doutes (non prouvés)

* Orientation : prouvée par la formule native serveur + géométrie des slots, **pas par une
  capture** ; la façade « −Y local du NIF » est déduite des offsets 4PHouse, pas mesurée sur
  les NIF `house/model/h0xx.nif`.
* Rendu des PNJ 53009…53078 et 53199-53201 (modèles `nXXXXXX`) non vérifié.
* `AttrGainCollectItem` : le test de position réel côté joueur est la vfunc +300 (non
  lue) ; le serveur utilise « keyId + event_area + rayon + 2 » (code existant).
* Le kill des mobs invoqués passe par le pipeline combat/loot générique ; non rejoué ici.
* L'écriture `FamilyBuildingItem.ItemTid` est un `short` (contrat 35002 > 32767 stocké
  non signé en base) : comportement existant, non modifié.
* Les marqueurs de récolte utilisent `z` de l'event_area ; si le terrain est plus haut,
  l'anneau peut être enterré.
* `//hall_setup` pose des contrats à `due_date = 0` (permanent) et l'item générique 39049
  quand aucun contrat dédié n'existe (la table n'en a que 18 kind 0x2a).

## 6. Fichiers

Serveur : `Eden.Core/Game/Family/FamilyCityRules.cs` (nouveau), `Eden.Zone/Handlers/GmHallSetupHandlers.cs`
(nouveau), `Eden.Zone/ZoneEntryFlow.FamilyCity.cs` (nouveau), `ZoneEntryFlow.FamilyInstance.cs`
(spawn), `ZoneEntryFlow.Migration.cs` (clés 60-64), `Program.cs` (1 ligne), test
`tests/Eden.Tests/FamilyCityRulesTests.cs`. Client : `scripts/world/FamilyHallBuildings.cs`.
