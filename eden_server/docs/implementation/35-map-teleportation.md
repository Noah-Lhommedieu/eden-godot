# 35 - Maps et Téléportation

> **Status:** 🟡 En cours — nodes chargés  
> **Priorité:** HAUTE  
> **Complexité:** Élevée

---

## 1. Description

379 maps/zones avec dimensions, bornes de zone, voisins, POI et BGM.
40 tables de transport/fast travel.

## 2. Symboles lapis::

- `CNode`, `CNodeTemplate`, `CDoor` (téléporteurs)
- `CEC_UpdatePCXY`, `CEC_GotoLover`, `CCityScroll`, `CFriendScroll`

## 3. Données

### S_Node.ini (`|V.9|78|`, 379 zones)
[0] id, [1] nom (聖徽城), [4-6] codes map (S001/S001M/S001S),
[7-10] dimensions, [11-18] bornes monde, [19-22] palettes RGBA,
[23-25] BGM, [31-34] codes voisins (N102/N201/N302/N402),
[35-42] timers, [48] description, [49-66] POIs (coords + noms),
[67] sous-titre.

### S_Transport.ini (`|V.10|59|`, 40 lignes)
Coûts par palier de niveau + destinations.

## 4. Implémentation C#

- `GameDataParsers.ParseNodes()` — `NodeDef` (codes, bornes, voisins, POI)

## 5. TODO / À reverser

- Parser `S_Transport.ini` dédié
- `CDoor` : téléporteurs inter-maps (positions dans les mapdata)
- Save points, Recall/City scrolls (items dans S_Item.ini)
- `CEC_UpdatePCXY` : protocole de mise à jour de position
