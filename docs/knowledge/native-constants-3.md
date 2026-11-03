# Constantes et formules natives — volume 3 : économie, soins, arène, familiers

Statut : **verified** pour tout ce qui a un extrait de code cité (lu dans le
décompilé `re/ghidra/zone/decompiled.c` via `reindex`), **hypothesis** pour les
interprétations de champs non nommés (offsets de structures). 2026-11-03.
Binaires : `eden_server/server_us/ZoneServer` (zone, symboles `lapis::*`),
complété par `mission` pour le paiement d'enchères. Les constantes `.rodata`
ont été relues directement dans le binaire pour cette note (les adresses
`DAT_0x…` non résolues dans `native-constants-2.md` y sont maintenant
décodées).

Suite de `native-constants.md` (combat de base, stats, XP solo) et
`native-constants-2.md` (licences, elfs, artisanat, guilde, mouvement).
Ne redocumente pas : `RefreshJPAward`, dégâts/XP de base, plafonds,
licences, crafts, guilde, déplacement. Couvre : prix PNJ (achat/vente/
réparation), enchères, blocage/parade/résistances/soins restants, XP de
groupe et d'arène, familiers (étoile/XP/énergie), états de mouvement.

Constantes `.rodata` résolues et réutilisées partout dans cette note
(zone `.rodata`, relecture directe) :

| adresse | valeur | usage |
|---|---|---|
| `0x087b0b18` (f64) | `0.0001` | ramène un tir `mod (var×10000)` en `[0, 2·var]` |
| `0x087b2748` | `5.0` | cap du bonus XP cumulé (+500 %) |
| `0x087b2754` | `1.0` | neutre multiplicatif / seuil minimal de prix |
| `0x087c2720` (f64) | `10000.0` | borne du modulo de variance des sorts |
| `0x087c2728` (f64) | `0.0` | test « variance nulle » |
| `0x087cc680` | `100.0` | facteur 100 % |
| `0x087cc684` (f64) | `3.05e-05` (≈1/32768) | complément 16.16 (rare) |
| `0x087cc688` | `0.5` | arrondi `+0.5` avant troncature |
| `0x087cc68c` | `1.0e8` | cap monétaire secondaire |
| `0x087cc690` | `0.01` | pourcentage → facteur |
| `0x087cc694` | `65536.0` | reconstruction d'un fixe 16.16 |
| `0x087cc698` | `1.0e9` | cap de la bourse (`char+0x4b0`) |
| `0x08817ba8` (f64) | `0.05` | dépôt de mise en vente aux enchères |
| `0x0888b684` | `0.4` | `niveau/2.5` des formules de taux |
| `0x0888b688` | `0.3` | facteur de débuff des stats composites |
| `0x0888b694` | `70.0` | cap résistance/réduction DEF haut |
| `0x0888b69c` | `-70.0` | cap résistance bas |
| `0x0888b6a0` | `1.0e-5` | `/100000` des dégâts de compétence magique |
| `0x0888b6a4` | `30.0` | plancher du facteur résistance physique |
| `0x0888b6a8` | `170.0` | plafond du facteur résistance physique |
| `0x0888b6ac` | `≈1.0e-7` | `/10^7` des dégâts de compétence physique |
| `0x0888b6d8` (f64) | `1.6` | exposant du niveau attaquant dans la réduction DEF |
| `0x0888b6e0` (f64) | `30.0` | terme additif de la réduction DEF |

Note 16.16 : le motif `(x >> 0x10) * 65536.0 + (x & 0xffff)` ne fait que
**reconstruire `x`** (poids fort reconstitué + poids faible) — c'est un
artefact du compilateur pour convertir un `uint` en `double` en 32 bits,
pas une division de point fixe. Toutes les lectures `ref+N` des
`SSpellReference` et le taux de taxe PNJ s'interprètent donc comme des
valeurs entières puis `× 0.01` (pourcents entiers).

---

## 1. Économie

### 1.1 Achat PNJ — verified

`CItemFactory::BuyItems(CCharacter*, int shopItemIdx, int count, bool, short)`
zone @ `0x080aca40`, appelée par `CNE_CZ_ZoneServerBuyItems::Execute`
zone @ `0x0823f1b0` (le handler vérifie que l'appelant `this+0xc` est un
personnage type `7` ; `this+0x16 != 0` bascule sur `BuyBackItems` =
rachat d'un objet revendu).

```c
/* zone @0x080aca60 — sélection du vendeur et de la boutique */
piVar23 = *(int **)(param_1 + 0x670);              // PNJ courant
if ((**(code **)(*piVar23 + 0x10))(piVar23) != 0x21) return 0;   // type 0x21 = vendeur
if (!CCharacter::TargetInTalkingRange(param_1,piVar23[1])) return 0;
sVar31 = (short)piVar23[2];                        // shopId = npc+8
/* CZoneServer::Ins+0x22c = std::map<short shopId, SShopData*> ; miss → "Nonexist ShopID" */
psVar3 = *(short **)(pCVar4 + 0x14);               // fiche boutique
/* psVar3+2/+4 = vector<short> des ids d'entrées ; entrée → map Ins+0x10c */
iVar18 = *(int *)(pCVar10 + 0x14);                 // fiche article
/* iVar18+4 = itemId, iVar18+0x10 = prix, iVar18+0xfc → requis prestige {+0x26 groupe, +0x28 exp} */
```

Conditions et formule de prix :

```c
/* zone @0x080aca40 (corps) — remise de prestige */
if (((ushort)(itemId + 0x63bfU) < 13000)            // itemId ∈ [40001, 53001) → pas de remise
    || (psVar3[1] == 0)                            // boutique sans groupe prestige
    || (priceType != 1)                            // pas payé en or
    || ((sVar12 = GetPrestigeLevel(param_1,psVar3[1])) < 2))
  local_2bc = 1.0;                                 // pas de remise
else {
  local_2bc = 0.01 * P[69 + sVar12];               // S_Parameter[69+nivPrestige]
  clamp(local_2bc, ~0, 1.0);
}
/* prix unitaire (non-empilable) : arrondi au supérieur du demi */
unitPrice = floor(local_2bc * shopPrice(iVar18+0x10) + 0.5);
```

`S_Parameter[71..77]` (index `0x114+4·niv`) : niv2 = 100, niv3 = 100,
niv4 = 98, niv5 = 95, niv6 = 92, niv7 = 88, niv8 = 84 (% du prix boutique ;
`parameter-index.tsv`, noms `.ini` « réputation LvN → prix d'achat PNJ »).

```c
/* zone @0x080aca40 (corps) — taxe du vendeur + paiement or (priceType==1) */
if (itemId ∈ [40001, 53001)) {
  /* monnaie cristal : char+0x4bc ≥ prix, prix < 100000, GainCrystal(-prix) */
} else {
  fee  = (int)((float)(int)*(short*)((int)npc + 0xa) * unitPrice * 0.01 + 0.5); // npc+0xa = taux % vendeur
  total = unitPrice + fee;
  if (total <= char+0x4b0 && total < 1e9) {
    CCharacter::FamilyLevyBusinessTaxes(param_1, fee, total);   // doc2 §5 : ponction guilde
    CCharacter::GainGold(param_1, -total, false, false);
  }
}
```

`npc+0xa` (short, sur l'objet PNJ `char+0x670`) = **taux de majoration du
vendeur en %** — ajouté au prix remisé avant débit (hypothesis :
correspond à la « taxe »/marge du commerçant). La forme
`(taux × prix >> 16) × 65536 + (taux × prix & 0xffff) × 0.01 + 0.5`
retrouvée dans `CWorkManager::EndCombine` (doc2 §4.4) se simplifie donc en
`fee = round(taux_npc × coût × 0.01)` — une majoration en pourcents
entiers (verified par la relecture `.rodata` ci-dessus).

Types de monnaie (`itemData+0x78` = `priceType`, dispatch verified dans
`BuyItems`) :

| priceType | portefeuille / action |
|---|---|
| 1 | or `char+0x4b0` (cap < 1e9) ; items `[40001,53001)` → cristal `char+0x4bc` (cap < 100 000), `GainCrystal` |
| 2 | prestige : `AddPrestigeExp(char, 0x51, −prix)` (groupe bataille) |
| 4 | « Love Amount » (points d'amour) — refus si insuffisant (msg 0x3ad) |
| 5 | LifelongGold `char+0xc54` |
| 6 | consomme l'item `0x8870` (34928) ×`prix & 0xffff` (jeton de succès) |
| 10 | trésorerie de guilde (« Family Treasury ») |
| 11 (0xb) | or du colisée `char+0xbbc` via `GainColosseumGold` |
| 12–18 | monnaie spéciale : `GetCoinTypeByPriceType` → entrée map `char+0x760` |
| 19 (0x13) | points de guerre (« War ») |
| 20 (0x14) | jetons alpaga (« Alpaca ») |
| 21/22/23 (0x15–0x17) | `SpecialCoin` via `GetCoinTypeByPriceType(0x15/0x16/0x17)` → `char+0x760` |

Autres contrôles verified : requis prestige de l'article
(`GetPrestigeExp(char, entry+0x26) < entry+0x28` → msg 0x261) ; places
libres cumulées sur tous les conteneurs (map `char+0x4e4`) < quantité
reçue → msg 0x4f9 ; les articles empilables sont d'abord complétés dans
les piles existantes (`itemData+0xc` taille de pile − `clone+0xc`).

### 1.2 Vente PNJ — verified

`CNE_CZ_ZoneServerSellItems::Execute` zone @ `0x08287160` ; exige le même
PNJ type `0x21` à portée de dialogue.

```c
fVar23 = (float)CCloneItem::GetSysPrice(item) * P[346];   // AllFormulaParas+0x568
/* non-empilable (itemData+0xf0 & 1 == 0) : prorata durabilité */
unit = fVar23 * (dur_actuelle / dur_max);                 // shorts item+0xc / +0xe
/* empilable : unit = fVar23 * count */
/* min : unit entière ≥ 1.0 ; sinon l'article produit 0 or */
```

- `S_Parameter[346] = 0.05` : un PNJ rachète à **5 % du prix système**.
- Refus `itemId ∈ [40001, 53001)` → msg 0x25d (objets « spéciaux » non
  revendables).
- Garde-fou débordement : `1852391528.0 / prixUnitaire < quantité` →
  refus (cap or `char+0x4b0` < 1e9 après ajout, verified).
- Articles `priceType == 1` : vendables si `P346 × itemData+0x7c ≥ 1.0`
  **ou** `itemData+0x76 != 1` (verified ; `+0x7c`/`+0x76` hypothesis :
  prix-or propre à l'article / drapeau « revente interdite »).
- Aucune majoration PNJ (`npc+0xa`) sur la vente — seul le coefficient
  P346 s'applique.

`CCloneItem::GetSysPrice` zone @ `0x084e5440` : retourne `itemData+0x10`
(float) ; pour les types d'objet `1` ou `6` (équipement à instance),
retourne la valeur d'instance `clone+0x28 + 0xdc` (verified).

### 1.3 Réparation — verified

`CItemFactory::GetItemRepairPrice(IItemContainer&, short slot)` zone @
`0x0808c480` :

```c
if ((itemData+0x3c & 1) == 0          // pas « irréparable »
    && clone+0x30 == 0                 // pas de marqueur de blocage (hypothesis : scellé/lié)
    && GetItemType(itemData) < 2       // types 0/1 = arme/armure réparables
    && dur(item+0xc) != max(item+0xe)) {
  base = CCloneItem::GetRepairPrice(item);
  price = (int)((base * P[347]) / maxDur) * (max − cur);   // P347 = 0.2
  return max(price, 1);
}
return 0;
```

`CCloneItem::GetRepairPrice` zone @ `0x084e54b0` : `itemData+0x14`
(float) ; types `1`/`6` → `clone+0x28 + 0xe0`.
`CCloneItem::GetMaxDurability` zone @ `0x084e53b0` : `itemData+0xc` (ushort) ; types `1`/`6`
→ `clone+0x28 + 0xd8`, sauf `clone+0x2c == 6` où la valeur retournée est
`(d8 + 5) >> 1` (arrondi moitié, verified — durabilité max réduite des
objets « tier 6 »).

Perte de durabilité max à la réparation — `CCharFormula::DecMaxDurability`
zone @ `0x083f4540` :

```c
loss = round((P[108] − attr+0x4ea) * amount * 0.01);  // P108 = 6 (% de max perdu)
return max(loss, 1);                                   // attr+0x4ea = réduction de la perte (hypothesis)
```

Perte de durabilité en combat (verified, cf. §2.8) : arme
`P[344] = 5` %, armure `P[345] = 10` % par coup (`random() % 100`).

### 1.4 Enchères — verified (côté zone) / commission non trouvée

`CNE_CZ_ZoneServerAuctionSell::Execute` zone @ `0x0823bd30` :

- PNJ type `0x29` (41 = commissaire-priseur), `TargetInTalkingRange`,
  `!IsNoTrade` (msg 0x1176).
- Objet : `itemData+0xf0 & 8` → non mises aux enchères ; `clone+0x24 &
  0x10` (lié) et `clone+0x2c == 1` exclus ; exige `itemData+0x78 == 1`
  (prix en or) et `itemData+0x84 − 1 < 0x38` (niveau d'objet 1..55,
  hypothesis).
- **Dépôt** : `gold(char+0x4b0) ≥ prix × 0.05` — `_DAT_08817ba8 = 0.05`
  (verified, double `.rodata`) → 5 % du prix demandé requis en bourse au
  moment du dépôt ; sinon `AuctionSellResult` code 2. Le débit effectif
  n'est pas dans la zone : la commande est relayée en texte au DBA de
  mission (`"AuctionSell con_index=%hd inv_loc=%hd crystal=%d
  crystal_id=-1 price=%d b_return=0"` via `CNC_DBA_AgentDBACommand`).
  Codes de retour observés : 2 = or insuffisant, 3/5 = article refusé.
- Branche « cristal » (`slot < 0`) : vente directe de cristaux,
  `this+0x18 ≤ char+0x4bc` requis.

Côté mission, `TC_…AuctionPay` (mission `decompiled.c` l.~359680) :
l'acheteur paie **`auction+0x98` intégralement** (`gold char+0x8c <
prix` → msg 0x232) — **aucune commission/taxe de vente trouvée** dans
zone ou mission (voir §7).

### 1.5 Variante de qualité (rappel) — verified

`CItemFactory::AdjustItemByQuality` zone @ `0x080892e0` : `clone+0x2c` =
palier, `clone+0x2e` = qualité % tirée uniformément dans
`[P[min], P[max]]` puis rebase `[101, …]` :

| palier (`clone+0x2c`) | paramètres | plage | qualité |
|---|---|---|---|
| 0 (blanc) | P350/P351 | 0.9 – 1.1 | 91–110 |
| 2 (vert) | P352/P353 | 1.0 – 1.2 | 101–120 |
| 3 (bleu) | P354/P355 | 1.15 – 1.35 | 116–135 |
| 4 (orange) | P356/P357 | 1.1 – 1.3 | 111–130 |
| 5 (or) | **P563/P564** | 1.0 – 1.2 | 101–120 |

P358/P359 existent dans la table mais **ne sont pas lus** par la branche
or — l'or utilise réellement 563/564 (verified). `itemData+0xf4 == 5`
(ou flag `0x80` de `+0xf0`) conditionne l'application.

---

## 2. Combat restant

### 2.1 Taux de blocage au bouclier — verified

`CCharFormula::BlockRate` zone @ `0x083f9d00`, log natif :

```
BlockRate = (Str<%d> * Para<%.2f> + Equip<%hd> + Buff<%.2f>) / (1 + Level<%hd> / 2.5)
            + shield_rate<%hd> = %d
```

- `Str` = stat composite `round((char+0x394 + char+0x354) ×
  (attr+0x178 + char+0x338) × 0.01) + attr+0x540`, débuffée `×0.3`,
  plafonnée 9999 → `× P[320] = 1` (FOR→blocage).
- `Equip` = `char+0x3d2` (short), `Buff` = `attr+0x154` (float).
- `shield_rate` = `attr+0x56c` (float) si un objet dont
  `itemData->vfunc+0xc == 0x22` (famille bouclier, hypothesis) avec
  `item+0xc > 0` est porté dans le slot parcouru par la map
  `char+0x4fc` (clé < 1) ; sinon 0.
- Dénominateur `1 + niveau × 0.4` (`vfunc+0x124` × `_DAT_0888b684`).
- Résultat `× 100` puis **cap 5000** (50 %).
- `BlockLv`/`BaseBlockLv` @ `0x083fa120`/`0x083fa1d0` : numérateur sans
  le facteur niveau (`Equip + Buff + Str×P320`).
- La classe `0x14` (byte `char+0x47c`) possède une branche spéciale
  conditionnée par l'entrée `char+0xd98` du vecteur `char+0xd9c..0xda0`
  (état de posture/double maniement — hypothesis).

### 2.2 Taux de parade — verified

`CCharFormula::ParryRate` zone @ `0x083fc240`, log natif :

```
ParryRate = (Dex<%d> * Para<%.2f> + Equip<%hd> + Buff<%.2f>) / (1 + Level<%hd> / 2.5)
            + wp_enhance<%hd> = %d
```

- `Dex` = même composite que ci-dessus (champs `0x394/0x354` etc.),
  `× P[319] = 0.8` (AGI→parade), `Equip` = `char+0x3d0`,
  `Buff` = `attr+0x158`.
- `wp_enhance` = `attr+0x458[(typeArme − 0x11)]` — tableau de 17 floats
  d'aisance par type d'arme (types `0x11..0x21`), lu via
  `GetWeaponItem(char, 7)` (main) ; si un second objet existe en slot 8
  et que les deux templates diffèrent → la bonus ne s'applique que pour
  un maniement identique ; classe `0x14` lit l'arme en slot 10.
- Résultat `× 100` puis **cap 7000** (70 %).
- `ParryLv`/`BaseParryLv` @ `0x083fb9b0`/`0x083fba60`.

Les tests « l'attaque est bloquée/parée » (`Block`/`Parry` proc) et
critique/crit-dmg sont déjà documentés §2.4–2.5 de `native-constants.md`
— non répétés ici.

### 2.3 Résistances élémentaires — verified

`CCharFormula::AttrResist(CCharacter*, short type)` zone @ `0x083f72c0` :

```c
assert(type ∈ [1,9]);                                // eAR_Begin..eAR_End
resist = attr[iVar3 = type−1]          // shorts attr+0x00.. : tableau EFBonusAttr
       + char[type*2 + 0x3d2];         // shorts char+0x3d4.. : EQBonusAttr
/* clamp : types 1–2 → [-50, +50] ; types 3–9 → [-70, +70] */
```

9 types d'élément (ordre `ResistName[]` statique). Monstre :
`AttrResist(CMonster*, …)` @ `0x083f7260` = `attr[type−1] +
monsterData+0xa6[type*2]` (modèle `char+0x324`).

Application dans les dégâts (§2.4/2.5) : bornée `[-70, +70]` puis
`×(100 − resist)` en magique ; en physique le facteur est saturé
`30/170` aux extrêmes (voir ci-dessous).

### 2.4 Dégâts de compétence magique — verified

`CCharFormula::CalculateMagicSkillDamage` zone @ `0x083fd190`, log natif :

```
skill_damage × (100 + Rand)/100 × (100 + 2·level_paras) × (100 − resist) / 100000
```

```c
base  = matk(vfunc+0x10c) + spellPower(param_3+0xc0) × (spellRef+0xc → % ×0.01)
rand  = random() mod (variance(param_3+0xc4) × 10000) ; fVar13 = 2·rand·0.0001 ∈ [0, 2·var)
lvPara= caster char/pet : clamp(lv − cibleLv, −10, +10)
        sinon (monstre) : clamp((lv − cibleLv) × 5, 0, 500)
elem  = attr+0x15c (surcharge d'élément, short) ; si 0 → spellData+0xbe
resist= cible->GetAttrResist(elem) bornée [-70,+70]
dmg   = round((base × 0.01 × (rand − var + 100)) × (2·lvPara + 100) × (100 − resist) × 1e-5)
dmg   = SpellDamage(caster, spellId, dmg)             // enhancements par sort (§2.7)
if (dmg < 5) dmg = random() % 4 + 1;                  // plancher 1–4
```

La **variance** `±spellData+0xc4 %` est identique dans les soins (§2.6) —
c'est le même motif `random() mod (var×10000) × 2·1e-4`.

`attr+0x15c` (hypothesis : « enchant change l'élément d'attaque ») a
priorité sur l'élément natif du sort.

### 2.5 Dégâts de compétence physique — verified

`CCharFormula::CalculatePhysicalSkillDamage` zone @ `0x083fd470`
(5ᵉ argument `param_5` = ATK déjà calculée par l'appelant) :

```c
defReduct = DEF(cible, vfunc+0x108) × P[327](=3) / (pow(lv_attaquant, 1.6) + 30.0)
            bornée [0, 70]                             // % de mitigation DEF
elemArme  = char : itemData+0x128 de l'arme (slot 7, ou 10 pour classe 0x14)
            monstre (type 0xb) : monsterData+0x70
si (attr+0x15c != 0 || elemArme != 0) :
    r = cible->GetAttrResist(elem) ; facteur = r>70→30 ; r<−70→170 ; sinon 100−r
sinon facteur = 100
dmg = round((param_5 + spellPower(+0xc0) × ref(+8)%) × 1e-7
            × (rand + 100 − var)                       // ±variance (même motif)
            × (100 + 2·lvPara − defReduct)              // lvPara identique §2.4
            × facteur)
dmg = SpellDamage(...) ; if (dmg < 6) dmg = random() % 4 + 1;
```

Différences notables vs magique : la réduction de DEF intervient à
l'intérieur du terme `(100 + 2Δlv − defReduct)` — la DEF du défenseur
pèse `P327 × DEF / (lv_atk^1.6 + 30)` %, plafonnée à 70 ; le facteur de
résistance est saturé `[30, 170]` aux résistances extrêmes (là où la
version magique borne la résistance elle-même à ±70 — équivalent dans
l'intervalle).

### 2.6 Soins — « canal » de heal (`attr+0x144/+0x14c`) — verified

Les offsets demandés vivent dans `CCharFormula::Heal` zone @
`0x083f9210` et `CCharFormula::BeHeal` zone @ `0x083f9110` :

```c
/* stat composite VOL (identique dans les deux) */
healStat = round((char+0x3a0 + char+0x360) × (attr+0x184 + char+0x344) × 0.01)
         + attr+0x554 ;  debuff(char[0x3a]&2) → ×0.3 ; clamp [0, 9999]

/* Heal = soins prodigués (lanceur) */
heal  = round(base × (100 + attr+0x148 + healStat × P[323](=1.2) / (lv×0.4 + 1)) × 0.01)
      + attr+0x144 + (short)char+0x3cc;

/* BeHeal = soins reçus (cible) */
bHeal = round(base × (100 + attr+0x150 + healStat × P[324](=1.0) / (lv×0.4 + 1)) × 0.01)
      + attr+0x14c + (short)char+0x3ce;
```

`attr+0x148`/`attr+0x150` = bonus % soins prodigués/reçus (EFBonusAttr),
`attr+0x144`/`attr+0x14c` = bonus **flat** soins prodigués/reçus,
`char+0x3cc`/`char+0x3ce` = équivalents d'équipement (shorts). Le couple
`+0x144/+0x14c` demandé est donc le canal « heal flat » des enchants —
les champs `attr` restent des **ints signés**, ce qui est le seul endroit
où une accumulation > 2047 peut être débattue (hypothesis sur le « bug
2047 » : aucun clamp à 2047 n'existe dans ces fonctions).

`CalculateMagicSkillHeal` zone @ `0x083f9310` enchaîne les deux côtés :

```c
base  = spellData+0xc2 × (spellRef+0x10 → %)        // heal de base du sort
rand/variance : même motif ±spellData+0xc4 %
iVar1 = round(base) ; si lanceur est char → Heal(lanceur, iVar1)
iVar2 = SpellDamage(lanceur, spellId, round((rand + 100 − var) × iVar1 × 0.01))
cible char (type 7) → return BeHeal(cible, iVar2)   // soins reçus appliqués
sinon → return iVar1 (monstre/objet : valeur lanceur)
```

Les trois champs `SSpellReference` sont donc : `+8` = % puissance
physique, `+0xc` = % puissance magique, `+0x10` = % puissance de soin
(tous en pourcents entiers, `×0.01`).

### 2.7 Enhancements par sort — verified

`CCharFormula::SpellDamage` zone @ `0x083f74c0` : deux maps du perso —

- `char+0x71c` = `map<short spellId, short rate>` → `dmg × (100+rate)/100`
  (« SpellEnhanceDamage … enhance_rate »)
- `char+0x704` = `map<short spellId, int flat>` → `dmg + flat`
  (« … enhance_dmg »)

Appliqué à tous les dégâts et soins de compétence (verified sur les 3
callers ci-dessus).

### 2.8 Usure en combat — verified

`CheckDecWeaponDurability`/`CheckDecArmorDurability` :
`random() % 100 < P[344]` (arme, 5) / `P[345]` (armure, 10). Voir aussi
§1.3 (`DecMaxDurability`, P108 = 6 % de max en moins à la réparation).

### 2.9 Coût de mana — verified

`CCharFormula::CostMp` zone @ `0x083f44d0` :

```c
mp = round(spellRef+4 (%) × param_2 × 0.01);           // coût de base du niveau de sort
mp = max(round((1.0 + attr+0xb4) × mp) − attr+0xb0, 0) // +b4 = % modif coût, +b0 = réduction flat
```

---

## 3. Expérience restante

### 3.1 Partage d'XP de groupe — verified

`CMonster::GiveTeamExp` zone @ `0x0849d190` :

1. `CForceTeam::GetLocalMembers(team, mapId(char+0x90), list)` — seuls
   les membres **sur la même map** participent ; 0 → log
   « Team Number Error », 1 → `GiveExp` solo.
2. `sumLv` = Σ niveaux membres (vfunc+0x124), `maxLv` = plus haut.
3. **Filtre d'écart** : les membres avec `maxLv − lv > 9` (**9 codé en
   dur**, distinct de P328) sont retirés de `sumLv` et comptés `exclus`.
4. `decline = ExpDeclineRate(maxLv, mobLv)` — la décroissance de niveau
   est évaluée sur le **plus haut niveau du groupe** (cf.
   `native-constants.md` §6 pour `ExpDeclineRate`, P328 = 9 de marge,
   P329 = 9 %/niveau).
5. Part membre (char vivant `char[0x3a]&1 == 0`) :
   - si `maxLv − lv < 10` **et** `team+0x56 == 0` (mode partage) :
     `ExpForTeam @0x083f4470` :
     `share = round(exp×decline × 0.01 × lv_membre × (N' × P[330](=25) + 100) / sumLv)`
     bornée ≤ `exp` ; `N'` = membres éligibles, `sumLv` = Σ niveaux
     éligibles → **répartition proportionnelle au niveau avec bonus
     +25 %/membre**.
   - sinon (écart ≥ 10 ou partage désactivé) : seul le tueur reçoit
     `ExpForTeamNoShare @0x083f5340` :
     `round((N × P[331](=5) + 100) × 0.01 × exp × ExpDeclineRate(lv_membre, mobLv))`
     bornée (dépassement → `exp`) ; les autres reçoivent 0.
6. Bonus d'XP appliqués ensuite (verified) :
   - `attr+0x4ec` = bonus XP d'enchant (affiché `min(val, 1.0)`, msg 0xf8
     `"%d,%.1f"`) ;
   - tir `random()%100 < attr+0x506` → ajoute `attr+0x508` ;
   - `CNodeTemplate::GetExpUpRateFromGender(mobTemplate, char[0x4aa]>>4)`
     — bonus d'XP par « genre »/catégorie de monstre ;
   - somme capée `DAT_087b2748 = 5.0` → `final = round(share × (1 + bonus))`.
7. `char+0xd54 != 0` (entraînement d'équipement actif) →
   `GainEquipExp(char, round(final × P[496]))`, `P496 = 0.2` → **20 %
   de l'XP gagnée part en XP d'équipement**, borné par `final` lui-même.
8. Familiers : si l'objet invoqué (`char+0x7dc`, entité `+4` de type
   `0x50b`) existe, `CKFElf::GainExp(elfs[obj+0x40c], final)` ; idem pour
   l'elfe invoquée `char+0x8dc` (`elf+0x58 == 1`) — le familier gagne la
   **même XP que son maître** (verified).

### 3.2 Tables de niveau (`CLevelDBO`) — verified partiellement

`CZoneServer::InitWorld` zone @ `0x08188450` charge 7 vecteurs de 101
entrées (`round(1.0 + P[362])`, P362 = 100 = niveau max) :

| fonction zone | champ CLevelData | correspondance .ini (hypothesis ±1) |
|---|---|---|
| `LoadClassLevelExp` @ `0x08362580` | `rec+0x2c` | ≈col10 (10, 11, 13, 14…) |
| `LoadClassWakenLevelExp` @ `0x08362460` | `rec+0x2c` et `rec+0x6c` | même colonne + borne niveau |
| `LoadEquipTrainExp` @ `0x083626a0` | **autre provider** `m_pkInstance+0x68`, `rec+4` | table propre (pas S_Level) |
| `LoadPetExp` @ `0x083627d0` | `rec+0x64` | ≈col24 (102, 107, 115…) |
| `LoadPVPJp` @ `0x08362900` | `rec+0x5c` | ≈col22 (25, 36, 46…) |
| `LoadPVPExp` @ `0x08362a30` | `rec+0x58` | ≈col21 (5, 7, 10…) |
| `LoadPartyJp` @ `0x08362b60` | `rec+0x54` | ≈col20 (25, 36, 47…) |
| `LoadPartyExp` @ `0x08362c90` | `rec+0x50` | ≈col19 (0,0,0,1440,4800…) |
| `LoadLevelExp` @ `0x08362320` | `rec+0x6c` | ≈col26/27 — deux lectures : 10000·n linéaire (col26) ou 0,76,78,82… (col27) |

La correspondance colonne `.ini` ↔ offset est déduite de l'ordre de
lecture de `CLevelQuery::Deserialize` zone @ `0x085fe950` : 13 `long`
`+0xc..+0x3c` (cols 2–14) puis `uint +0x40..+0x6c` (cols 15+ — au moins
un champ `+0x64`/`+0x70` est masqué dans les lectures chaînées, d'où le
±1, verified). Valeurs `S_Level.ini`
(EDENFR `db_decoded`) citées à titre de donnée : lv1 `cols 20-27 =
25/5/25/5/102/0/10000/0`, lv2 `36/7/36/7/107/32/20000/76`, lv40
`655/131/655/131`, lv90 `2130/426/2130/426`.

### 3.3 Arène / Colisée — verified

`CColosseum::GetExpRate(CCharacter*)` zone @ `0x083ff270` lit la table
globale `ColosseumExpTable` @ `0x0888b800` : entrées de 4 `int` =
`{param_défaite, param_victoire, param_défaite_2, param_victoire_2}`
(numéros `S_Parameter`) ; la valeur retournée est
`(int)AllFormulaParas[param]` :

| idx table | S_Parameter lus | mode déduit (noms `.ini`) |
|---|---|---|
| 1 | `[441, 440]` → 50/100 | arène **boss** (lose/win) |
| 3 | `[443, 442]` → 75/150 | 3v3 tranche 30-39 |
| 4 | `[445, 444]` → 75/150 | 3v3 tranche 40-49 |
| 5 | `[447, 446]` → 60/125 | 3v3 tranche 50-59 |
| 6 | `[449, 448]` + `[457, 456]` → 50/100 ×2 | 3v3 **et** 10v10 tranche 60-69 |
| 7 | `[451, 450]` + `[459, 458]` → 50/100 ×2 | 3v3 et 10v10 tranche 70-79 |
| 8 | `[583, 582]` + `[587, 586]` → 50/100 ×2 | 3v3 et 10v10 tranche 80-89 |
| 9 | `[585, 584]` + `[589, 588]` → 50/100 ×2 | 3v3 et 10v10 tranche 90-100 |

L'index de tranche est calculé depuis le niveau du joueur et le mode de
l'arène (`this+0xac` = mode, `this+0xb0` = tranche ; constructeur
mission `CColosseum` : mode 0/1/2/3 → nœud `0x191/0x192/0x194/0x193` —
boss/3v3/… mapping hypothesis). Équipe gagnante → 2ᵉ valeur de la paire.

Application — `CColosseum::AllGotoTeamPreparePos` zone @ `0x08404d00` :
`taux = GetExpRate(char)` ; `xp = taux × PVPExp[niv]`, `jp = taux ×
PVPJp[niv]` (tables §3.2) ; l'XP n'est créditée que si `niv < P[362]`
(100) ; les points de classe et NP suivent le même `taux` quand `jp >
0`. `S_Parameter[455] = 20` fixe l'effectif du mode 10v10 côté mission
(`CColosseumFactory`, mission @ `0x08057bb0` etc.).

### 3.4 Rappels déjà couverts

`ExpDeclineRate` (P328 = 9, P329 = 9), caps XP solo et `GainExp` :
`native-constants.md` §6. Quêtes : paliers d'écart `P332-335 = 4/6/8/10`
et décroissance `P336-339 = 100/75/50/25` % (`mission
GetRewardExp` @ `0x08071920`, verified index).

---

## 4. Familiers (CKFElf)

### 4.1 XP et niveau — verified

`CKFElf::GainExp` zone @ `0x08457770` :

```c
// elf+0x98 = CPartnerData (obligatoire), elf+0x14 = niveau (char), elf+0x18 = xp courante
// CZoneServer::Ins+0x3b4..0x3b8 = vector<int> table XP familier (LoadPetExp)
elf.xp += gain;
while (elf.xp >= table[elf.level] && elf.level < owner_level(char+0x470 court))
    SetLevel(elf, elf.level + 1, false);      // montée en cascade, bornée par le maître
// trop-plein : xp clampée à table[level]
```

Le familier **ne peut pas dépasser le niveau de son maître** (champ
`char+0x470` du propriétaire, short — hypothesis « niveau », cohérent
avec `SetLevel`/`TC_PetSetLevel`).

L'alimentation en XP : même valeur que le maître (§3.1 point 8) +
`CEC_ItemElfExp`/`CEC_ItemElfExpOnce` (items d'XP, bornés `P362`).

### 4.2 Étoile / amélioration — verified

- Cap : `S_Parameter[509] = 4` (`TC_SetPetStar` @ `0x083e2880`,
  `CKFElf::SetStar` @ `0x084568d0`, accesseur GameData `+0x130`).
- `CKFElfManager::CanUpgrade` zone @ `0x08072150` : les deux familiers
  existent, `niveau ≥ 40 (0x28)`, les 4 grow-stats pas déjà > 8, pas de
  flag d'amélioration en cours, même type, `star < 4`.
- `CKFElfManager::DoElfUpgrade` zone @ `0x08079a00` : incrémente
  aléatoirement l'une des 4 grow-stats en évitant celles à 9 ; pour un
  type/état donné, un jet `random()%100 < param(0x8d8)` sélectionne une
  3ᵉ valeur uniformément dans `[param(0x8dc), param(0x8e0)]` (offsets du
  provider formule — hypothesis sur leur signification exacte).
- Loterie « diamant » — `CKFElfManager::ElfDiamondDiceLottery` zone @
  `0x08077b90` : `roll = random() mod (roundWeight × 10000) × 0.0001 ∈
  [0, poids)` puis descente de la map `{seuil → lot}` du round
  (`CPlayerFactory::AllDiamondLotterysDBO` indexée par `elf+0xd2`).

### 4.3 Énergie — verified

`CEC_KFElfEnergy::Execute` zone @ `0x08540920` — commande d'effet sur un
**item-familier** (type `0xf`) dans un conteneur :

```c
gain  = random() % (effect+0xc + 1) + effect+8;   // uniforme [base, base+range]
clamp : si (item+0xe < gain + item+0xc) gain = item+0xe − item+0xc;
ModifyItemDurability(container, slot, gain, true); // item+0xc += gain → CNC_CZ_ClientModifyItemDurability
// msg 0x3815 "%s,%d" = <nomFamilier> +gain
```

→ L'**énergie du familier est stockée comme la durabilité de l'item**
(`item+0xc` courante, `+0xe` max). `CEC_ItemRecoverElfEnergy::Execute`
zone @ `0x0853d930` restaure via `CKFElf::UpdateHp(char, GetMaxHp-…)` —
restauration intégrale (pas de formule).

`DoMagicBox` zone @ `0x080a8b90` (boîte magique de conversion) lit
`GameData::ConverEnergyRate` — table 5 shorts/entrée indexée
`round × 10` : selon `itemData+0x2c` (0/2/3/4/5) la colonne `{0,1,3,2,…}`
donne le taux et `uVar27` la catégorie d'énergie (verified structure ;
sémantique des catégories hypothesis).

### 4.4 Humeur / familiarité — recherché, non trouvé (verified stub)

`CEC_ElfMood::Execute`, `CEC_ElfEnergy::Execute`,
`CEC_ElfMoodRate::Execute`, `CEC_ElfFamiliar*::Execute` (dont
`ElfFamiliarEnergyMood` @ `0x0852a250`) sont des **corps vides
`return 1`** dans le binaire zone ; `TC_SetElfMood` @ `0x083bede0` est
un stub `return;`. Aucune formule de satiété/humeur/familiarité n'existe
dans ce build natif — la donnée n'apparaît que dans un format de log
membre (`MemberMood:%hd`, mission). Ne pas implémenter côté C# sans
oracle supplémentaire.

---

## 5. États de mouvement / transformation

Champs relus dans les handlers de canal (`CEC_…::Execute`) :

- `char[0x3b] & 2` = **monté** ; `char+0x7f8` = id d'objet-monture
  (le seul champ relu par `CheckCanRide`) ; l'enchant de monture est
  l'**état 9** (`ILifeEntity::RemoveEnchantByState(char, 9)` retiré
  quand `CheckCanRide` échoue ou quand la boutique perso s'ouvre —
  `char[0x3b] & 0x40` = mode vendeur, `VendorClose`).
- `CheckCanRide` zone @ `0x08409c50` (corps complet relu) : si monté et
  item connu, `param_2` = type de nœud terrain `< 0x13` →
  `mask = 1 << param_2` ; `mask & 0x408a0` (types `{5,7,10,14,18}`) →
  test bit `0xd` de `itemData+0x30` ; `param_2 == 3` → bit `0xb` ;
  `param_2 == 1` → bit `0x1d` ; retour `(mask_bit >> n) ^ 1 & 1` =
  vrai si la restriction est **absente** ; non monté → toujours 1.
  Le modificateur de vitesse de monture n'y figure **pas** (voir §7).
- `char[0x3c] & 0x10` = mode marche (force la branche lente du calcul de
  déplacement — doc2 §8).
- `char[0x3b] & 4` = flag « familier suit/combat » : à la ré-invocation
  (`CallElf*`), `LifeEntityDo(elf, 10, …)` relance l'action 10.
- `char[0x3b] & 0x20` et `char[0x3b] & 1` : testés dans `CanDo*`,
  signification non résolue (hypothesis : assis / indisponible).
- Transformation : aucune formule de vitesse propre à la transformation
  trouvée dans zone (§7).

---

## 6. Paramètres `S_Parameter` nouvellement rattachés

| id | valeur | usage natif |
|---|---|---|
| 71–77 | 100/100/98/95/92/88/84 | remise boutique par niveau de prestige (×0.01) |
| 108 | 6 | % de durabilité max perdue à la réparation (`DecMaxDurability`) |
| 319 | 0.8 | AGI→niveau de parade |
| 320 | 1 | FOR→niveau de blocage |
| 323 | 1.2 | VOL→soins prodigués (`Heal`) |
| 324 | 1.0 | VOL→soins reçus (`BeHeal`) |
| 327 | 3 | coef DEF dans `CalculatePhysicalSkillDamage` |
| 330 | 25 | bonus XP de groupe partagée (+%/membre) |
| 331 | 5 | bonus XP/JP de groupe non partagée |
| 344 / 345 | 5 / 10 | % perte durabilité arme / armure par coup |
| 346 | 0.05 | coefficient de revente PNJ |
| 347 | 0.2 | coefficient du coût de réparation |
| 348 / 349 | 10 / 100 | perte durabilité à la mort / invuln. respawn (index) |
| 362 | 100 | niveau max personnage (taille des tables XP) |
| 440–451 | voir §3.3 | arène boss + 3v3 tranches 30-79 |
| 452 / 453 | 30 / 90 | lucky-bag (incrément/plafond d'explosion) |
| 455 | 20 | effectif arène 10v10 |
| 456–459 | 100/50 ×2 | arène 10v10 tranches 60-79 |
| 496 | 0.2 | % d'XP convertie en XP d'équipement |
| 509 | 4 | étoile max familier |
| 563 / 564 | 1 / 1.2 | plage qualité objet or (`AdjustItemByQuality`) |
| 565 / 579 | 10 / 10 | drop de classe : écart/seuil de niveau |
| 581 | 60 | points de kill monstre : seuil d'écart |
| 582–589 | 100/50 ×4 | arène 3v3+10v10 tranches 80-100 |

---

## 7. Recherché, non trouvé — à ne pas inventer

1. **Commission/taxe d'enchères** : le dépôt 5 % est verified (zone) ;
   aucune ponction sur la vente finale n'apparaît dans zone ni mission
   (`AuctionPay` débite le prix intégral). Si une commission existe, elle
   vit dans `CAuctionDBA`/SQL — non exporté.
2. **Vitesse de monture / transformation** : `CheckCanRide` ne porte que
   les restrictions de terrain ; aucun `×vitesse` de monture n'a été
   localisé (le facteur lu dans `Movement` côté doc2 reste le seul
   indice). Aucune formule de vitesse liée à `0x408a0`/transformation
   n'a été trouvée — recherches `reindex` sur les commandes
   transformation et les champs de vitesse sans résultat.
3. **Humeur/satiété/familiarité des familiers** : corps vides/stubs
   (§4.4) — le système semble inachevé dans le build oracle.
4. **`itemData+0x7c` vs `+0x10`** (vente §1.2) : deux champs prix
   distincts ; l'affectation exacte `.ini` des colonnes reste à écrire
   depuis `CItemData` Deserialize (non extrait ici).
5. **Mapping exact colonnes `S_Level.ini` ↔ offsets record** (§3.2) :
   l'ordre Deserialize est verified mais le nommage colonne-à-colonne
   reste ±1 — à terminer via `CLevelQuery::Deserialize` complet.
6. **Signification de `char[0x3b]` bits 0/0x20 et `team+0x56`** (mode
   « non-partage » du groupe, lecture seule verified, nommage
   hypothesis).
7. **`pow(lv,1.6)` §2.5** : vérifié comme niveau de l'attaquant
   (`vfunc+0x124` sur `param_1` avant le calcul DEF) ; si un cas de niveau
   0 se présente, `pow(0,1.6)+30 = 30` — pas de garde native.
8. **`clone+0x30` (§1.3) et `clone+0x2c == 6` (`GetMaxDurability`)** :
   conditions verified, sémantique hypothesis (« scellé » / palier
   d'objet spécial).

*Fin du volume 3 — sources : `re/ghidra/zone/decompiled.c` via
`reindex`, `eden_server/server_us/ZoneServer` (relecture `.rodata`),
`artifacts/corpus-inventory/parameter-index.tsv`,
`eden_server/server_us/Data/db_decoded/S_Level.ini`,
`re/ghidra/mission/decompiled.c`.*
