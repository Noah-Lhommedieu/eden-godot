# Plan — FamilyTownQuery (174→502) + FamilyTownMemberQuery (175→503)

## Preuves natives (toutes vérifiées dans les exports Ghidra)

### Requête 174 — `CNE_CZ_ZoneServerFamilyTownQuery` (zone)
- Wire : `[s16 lvlMin][s16 lvlMax][i32 buildingMask][str nameFilter]`
  (objet : +0x12, +0x14, +0x18, +0x1c).
- `Execute` @ zone 0x825a890 : rate-limit **3 s** sur `char+0x650/0x654`
  (champ PARTAGÉ avec 173/176/175 : `3 < sec || (sec == 3 && µs > 0)`),
  puis `CPlayerFactory::FamilyTownQuery` @0x8110830.
- `FamilyTownQuery` :
  - candidats = copie du `set<int> Ins+0x47c` (ids des familles
    **instanciées** — peuplé par `CNE_DBA_ClientAllFamilyInstnce`
    @0x82dfd20 mission, familles avec hall ⇒ `house_open != 0`) ;
  - si nameFilter non vide : intersection avec `Ins+0x490`
    (`map<str nom,set<int>>` peuplée par `CZoneServer::InsertTownNpcMap`
    @0x8178260 : clé = **nom de l'item** `item+8→proto+8` des items
    `item_type==0x2a` du conteneur 0, subtype != 0x16) ; matching
    `string::find` = substring ;
  - filtre niveau `param_2 <= (char)fam+0x18 <= param_3` (inclusif) ;
  - mask != 0 : exige un item `item_type==0x29` du conteneur 0 dont
    `GetBuildingValue` & mask ≠ 0 (`CFamilyItem::GetBuildingValue`
    @0x8464870 : template `S_Buliding` via `tid` item+0x80, type@+0xc
    : 4→1, 5→2, 6→4, 7→8, 8→0x10, 9→0x20) ;
  - résultats ordonnés par id (set<int> + map<int,SFamily*>) ;
  - **chunk ×10** puis paquet final **toujours émis** (même vide).

### Réponse 502 — `CNC_CZ_ClientFamilyTownQuery` (zone→client)
Ctor @0x8148b90, Serialize ~0x8148xxx : **17 vecteurs parallèles**,
chacun `[u16 count][elems]` :

| vec | type | source |
|-----|------|--------|
| 0 | i32 | fam+0x04 id |
| 1 | s16 | (char)fam+0x18 level |
| 2 | str | fam+0x0c name |
| 3 | str | fam+0x14 leader_name |
| 4 | u32 | fam+0x28 **exp** (mission CFamily+0x28 = col `exp`) |
| 5 | s16 | fam+0x32 **business_tax** (mission +0x36 = col business_tax) |
| 6 | u32 | fam+0xfc **member count** (incrémenté par ClientFamilyAllMember) |
| 7 | s16 | fam+0x3c **house_open** |
| 8 | u32 | count de bâtiments de la famille (conteneur 0) |
| 9 | s16 | loc (clé map interne fam+0x7c[0]) |
| 10 | i32 | item+0x84 item_tid (u16→i32) |
| 11 | i32 | item+0x80 tid (template S_Buliding / NPC) |
| 12 | s16 | item+0x86 item_type |
| 13 | s16 | item+0x0c durability |
| 14 | s16 | item+0x94 level |
| 15 | u32 | item+0x98 exp |
| 16 | u32 | item+0x18 due_date |

- **Gate ctor** : famille sans entrée `fam+0x7c[0]` (conteneur 0 absent)
  → exclue du résultat.

### Requête 175 — `CNE_CZ_ZoneServerFamilyTownMemberQuery` (zone)
- Wire : `[i32 famId]` (objet +0x12).
- `Execute` @ zone : rate-limit **500 ms** sur char+0x650 (même champ),
  relais mission `FamilyTownMemberQuery fm_id=%d`.
- `TC_FamilyTownMemberQuery` @ mission 0x820a670 :
  - `GetFamily(famId)` absent → **silence** (rien émis) ;
  - itère `fam+0xf8` (map membres, ordre charId croissant) ;
  - filtre par membre : `exec+0xac != 1` (**col `quit`**) &&
    `exec+0xe2 != 0` (**EnableSearch**, char+0x794 zone, défaut ctor=1,
    synchronisé par `TC_SearchConditionOpt`) ;
  - rang : `charId == fam+0x10` (leader) → **1**, sinon cadre
    `fam+0x110` → `*(u8*)(val+4)`, sinon **0**.

### Réponse 503 — `CNC_CZ_ClientFamilyTownMemberQuery`
6 vecteurs `[u16 count][elems]` :
v0 i32 char_id (exec+8), v1 str name (exec+0xc), v2 s16 level
(exec+0x5c, col `level`), v3 u8 rank, v4 s16 node_id (exec+0x30,
col `node_id` — cf. `UpdateFamilyMemberNodeID` @mission 0x807fb40),
v5 str titres famille (vecteur fam+0x124, 9 titres — niveau réponse).

## Écarts/approximations documentés

- `exec+0xe2` (EnableSearch) côté mission : valeur live poussée par la
  zone ; pour un membre hors ligne on ne la connaît pas → **défaut
  natif = 1** (visible). En ligne : `NetSession.SearchVisible`.
- node_id membre : live via EntryState si connecté sur cette zone,
  sinon `player_characters.node_id` (persisté). Le natif lit le exec
  mission qui reflète le dernier node poussé.
- `Ins+0x490` : index reconstruit à la demande depuis le conteneur 0
  (nom d'item via `ItemDef` ; `Kind != 0x16`).
- `S_Buliding.ini` : chargé dans `GameDataService` (`tid → type`).

## Implémentation

- [ ] `GameDataService` : parser `S_Buliding.ini` → `BuildingTypes`
      (`Dictionary<int tid, short type>`).
- [ ] `SocialRepository` : `GuildRow` += business_tax, leader_name,
      titres (9) ; `GuildMemberRow` += name, level, node_id, quit.
- [ ] `GuildService` : `Guild` += `BusinessTax`, `LeaderName`,
      `Titles` ; `GuildMember` += `Level`, `NodeId`, `Quit` ;
      `LoadAsync` propage.
- [ ] `FamilyPackets.cs` : codec dédié 502 (`FamilyTownQuery`) +
      503 (`FamilyTownMemberQuery`).
- [ ] `ZoneEntryFlow.FamilyInstance.cs` : cases 174/175 dans
      `TryHandleOnLoopAsync` (partage `_familyInstanceOpAt` —
      3 s pour 174, 500 ms pour 175) ; orchestration
      `FamilyTownQuery`/`FamilyTownMemberQuery` sur la boucle.
- [ ] Retirer les stubs RegisterChar 174/175 de `FamilyHandlers.cs`.
- [ ] Tests : parsing 174, rate-limit 3 s, filtres niveau/nom/mask,
      ordre+largeurs des 17 vecteurs 502, chunk ×10 + paquet final
      vide, filtres membres 503, rangs (leader/cadre/membre), titres.
- [ ] `dotnet build` 0/0, `dotnet test` vert, commit atomique avec
      preuves @addr.
