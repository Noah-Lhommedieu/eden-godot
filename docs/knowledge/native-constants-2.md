# Constantes et formules natives hors combat — volume 2

Statut : **verified** pour tout ce qui a un extrait de code cité (lu dans le
décompilé `re/ghidra/zone/decompiled.c` via `reindex`), **hypothesis** pour les
interprétations de champs non nommés (offsets de structures). 2026-11-11.
Binaire : `eden_server/server_us/ZoneServer` (zone, symboles `lapis::*`),
complété par `mission` pour les commandes SQL identiques.
Suite de `native-constants.md` (combat, stats, XP) — hors combat ici :
licences/certificats, compagnons (elfs), sorts raciaux, artisanats, groupe/
guilde/PvP, mouvement.

Convention : `m_pkInstance+0xNN` = vtable de `GameData::IGameData::m_pkInstance`
; `+0xf8` = `GetItemData(itemId)` (utilisé partout pour les items), `+0xec` =
`GetClassData(classId, race)`, `+0x1d8` = `GetClassGroup`-like (id de la classe
de base du groupe), `+0xf4`? non utilisé ici.

---

## 1. Licences / certificats de classe — verified

Le « certificat de classe » client = `license` serveur : un **item** porté dans
un vecteur dédié `char+0xaf0` (`vector<SCharacterLicense>`, entrée 4 octets :
`{short itemId, byte slotFlag, byte pad}` ; `slotFlag` 0 = rangé, 1..7 = slot
d'équipement).

### 1.1 Octroi — tous les 10 niveaux de classe, gratuit

`CCharacter::GainClassPoint` zone @0x0841c140 (à la fin du level-up de classe) :

```c
/* zone @0x0841c7d8 — octroi licence */
if (((float)(int)sVar11 <  *(float *)(CFormula::AllFormulaParas + 0x908) ||   // S_Parameter[578]=100
     (float)(int)sVar11 == *(float *)(CFormula::AllFormulaParas + 0x908)) &&
    (sVar11 == (short)((sVar11 / 10) * 10))) {                              // classLv % 10 == 0
  uVar12 = (int)(sVar11 / 10) - 1;                                          // idx = classLv/10 - 1
  iVar4  = *(int *)(*(int *)(this + 0x32c) + 0x44);                         // classData+0x44
  if (uVar12 < (uint)(*(int *)(*(int *)(this + 0x32c) + 0x48) - iVar4 >> 1)) {
    sVar11 = *(short *)(iVar4 + uVar12 * 2);
    CPlayerFactory::GetLicense(this,sVar11);                                // ajoute l'item-licence
    iVar4 = (**(code **)(*m_pkInstance + 0xf8))(m_pkInstance,sVar11);        // GetItemData
    if ((iVar4 != 0) && (*(short *)(iVar4 + 0x8c) != 0)) {
      CPlayerFactory::RemoveLicense(this,*(short *)(iVar4 + 0x8c));         // retire la licence inférieure
    }
  }
}
```

- `classData+0x44` = `vector<ushort>` de **8 ids d'items-licence** (paliers
  classLv 10→80 ; borné par la taille du vecteur, pas au-delà de lv80).
- **Aucun coût** : pas d'or, pas de consommable — la licence est offerte.
- `itemData+0x8c` = id de la licence à **retirer** quand celle-ci est obtenue
  (chaîne de tiers : le certificat supérieur remplace l'inférieur).

`CPlayerFactory::GetLicense` zone @0x080fce80 : refuse les doublons
(`"the license item (%hu) is existed already"`), écrit `license = %hd %hhd`
(DB) puis notifie `CNC_CZ_ClientPlayerLicense`.
`CPlayerFactory::RemoveLicense` @0x080fa490 : même paquet, flag 0xff.
Commandes GM : `/get_license <number>`, `/remove_all_license`
(`TC_GetLicense` @0x083d21e0, `TC_RemoveAllLicense` @0x083bf220).

### 1.2 Équipement — 7 slots, limite `charLv/10 + 1`

`CNE_CZ_ZoneServerPlayerLicense::SetCurrentClassLicense` zone @0x082714f0 :
le client envoie {licenceId → slot `&7`} ; slot 0 = retrait
(`ReplaceSpellStones(char, ancienSlotByte, 0)`), sinon :

```c
/* zone @0x08271600 */
if ((int)(*(short *)(param_1 + 0x470) / 10) + 1U <= local_20) {   // nb déjà équipées
  (**(code **)(*(int *)param_1 + 0x58))
            (param_1,0x325,0xffffff97,&DAT_087cf0e3,
             *(short *)(param_1 + 0x470) / 10 + 2);               // msg refus
  goto LAB_08271631;
}
CCharacter::ReplaceSpellStones(param_1,bVar4,sVar2);              // équipe en slot bVar4
```

- **Slots physiques : 7** (`bVar4 = flag & 7`, indices 1..7 ; `InvokeSpellStones`
  filtre `6 < (byte)(flag-1)` → accepte flag ∈ [1,7]).
- **Limite : `charLv/10 + 1` licences équipées** (`char+0x470` = niveau
  personnage) — atteint le maximum de 7 à partir du lv 60.

### 1.3 Effet — enchant permanent par slot

`CCharacter::InvokeSpellStones` zone @0x08413bb0 : reconstruit deux vecteurs
de 8 shorts `char+0xafc` (enchant par slot) et `char+0xb08` (licence par slot)
puis applique chaque enchant manquant :

```c
puVar4 = (**(code **)(*m_pkInstance + 0xf8))(m_pkInstance, licenseItemId);  // item data
*(short *)(char+0xafc + flag*2) = (short)puVar4[0x18];   // item+0x60 = enchant id
*(short *)(char+0xb08 + flag*2) = (short)*puVar4;        // item+0x00 = licence id
...
CEffectFactory::EffectToLife(Ins,this,this,enchantId,-2,0x1000,false,0,1,0,0);
```

- `itemData+0x60` = **enchant accordé par le certificat équipé** ;
  `itemData+0x00` = id de l'item (rappelé dans le slot).
- Enchant permanent (durée −2, flags 0x1000 — même convention que
  RefreshJPAward), ré-appliqué au login/revive/changement de classe.

### 1.4 Loadout par classe — sauvegardé et restauré au change

`CPlayerFactory::ChangeClass` zone @0x081048c0 : le nœud de classe possédée
(`char+0xad8` hashmap) stocke **7 shorts à node+0xe..+0x1a** = les ids de
licence par slot pour cette classe. Au `ChangeClass` :

```c
/* zone @0x08105940 — restauration du loadout */
for each licence in char+0xaf0:
  if licence.id == *(short*)(node + 0xe + i*2)  // i = 0..6
      licence.slotFlag = i + 1;                  // slot 1..7
  else licence.slotFlag = 0;                     // déséquipée
CCharacter::InvokeSpellStones(param_1);
```

Layout nœud classe (verified) : `node+0` = classId, `node+4` = classPoint,
`node+8` = champ 4 octets (`this+0x488`), `node+0xc` = byte (`this+0xa1c`),
`node+0xe..+0x1a` = 7 shorts licences, `node+0x1c` = classLv,
`node+0x1d` = flag.

### 1.5 Déblocage de classe — automatique, par prérequis (pas de coût)

`CPlayerFactory::AddClassFromLimit` zone @0x080fd440 (appelé depuis
`GainClassPoint`, `AddClass`, `PostInit`) : itère la carte
`m_pkInstance+0x74` indexée `(racePair*100 + classId)`
(`racePair = ((byte)char[0x4aa] >> 4) >> 1`) ; pour chaque classe non
possédée, `AddClass` si :

```c
if ((*(byte *)(clsData + 0x25) != 0) && ((short)*(byte *)(clsData + 0x25) <= charLv)) {
  if ((*(byte *)(clsData + 0x24) != 0) && (*(uint *)(clsData + 0x20) != 0)) {
    for (bit = 1; bit != 0x21; bit++)                       // bitmask 32 classes
      if (clsData[0x20] & (1 << (bit-1)))
        exige classe `bit` possédée ET classLv (node+0x1c) >= clsData[0x24];
  }
  AddClass(param_1, classId);                                // aucun débit d'or/objet
}
```

- `classData+0x25` (byte) = **niveau personnage requis** ;
- `classData+0x20` (uint) = **bitmask des classes prérequisites** (bit n =
  classe n, 1..32) ; `classData+0x24` (byte) = **niveau de classe requis**
  pour chacune.
- ⚠️ Pas de coût en or/objet : le déblocage est entièrement prérequis +
  automatique dès qu'une montée de niveau (classe ou perso) remplit les
  conditions.

`CPlayerFactory::AddClass` zone @0x080fd070 : la nouvelle classe part à
classLv 1 **sauf** si `classId != (m_pkInstance+0x1d8)(classId, race)`
(classe ≠ base de son groupe → classe « éveillée ») : alors
`classLv = 0x50` (80), champ+8 = 0x50, les **8 licences sont octroyées
d'un coup** (boucle `GetLicense` sur `classData+0x44`, `80/10 = 8` fois),
les sorts `classData+0x50` (vec≤4) appris au niveau 80 et `classData+0x5c`
au niveau 0x50, puis `RefreshJPAward`.

### 1.6 `CClassData` — layout (S_Class*, `CClassQuery::Deserialize` zone @0x085ca130)

Taille 0x98 (`operator_new(0x98)` dans `LoadDB<CClassQuery>` @0x085cbd50) ;

| Offset | Type | Contenu | Statut |
|---|---|---|---|
| +0x00 | long | class id (clé) | verified |
| +0x04 | string | nom | verified |
| +0x08 | short | (version > 4 seulement) | verified |
| +0x0c..+0x1c | 5 strings | libellés/desc | verified |
| +0x20 | uint | bitmask classes requises (§1.5) | verified |
| +0x24 | byte | classLv requis des classes requises | verified |
| +0x25 | byte | charLv requis | verified |
| +0x26 | byte | (lu, usage non vu) | hypothesis |
| +0x28 | long | | hypothesis |
| +0x2c / +0x30 | uint ×2 | | hypothesis |
| +0x34..+0x40 | 7 shorts | gains au level-up (dont +0x38..+0x40 → stats ; voir doc 1 §4) | verified partiellement |
| +0x44 | vec<ushort>[8] | **ids des 8 licences** (paliers 10..80) | verified |
| +0x50 | vec<ushort>[4] | sorts appris à l'obtention (niv. classLv) | verified |
| +0x5c | vec<ushort>[15] (v≥5 ; 10 sinon) | sorts appris à l'obtention (niv. 0x50) | verified |
| +0x68 | vec<ushort>[6] | | hypothesis |
| +0x74 | vec<SClassCloset>[10] (0x10 o.) | tenues/closet de classe | verified (type) |
| +0x84 | map<short id, short lvReq> | succès déclenchés au classLv (`GainClassPoint` : node+0x10 id, +0x14 lv requis → paquet mission "Achievement") | verified |

---

## 2. Compagnons (elfs / pets) — verified + hypothesis

Modèle : les elfs sont des **items spéciaux** stockés dans un conteneur dédié
(`CElfContainer`, nom `"elf_container"`) et matérialisés en `CKFElf`
(`char+0x8ac` = `vector<CKFElf*>`) — le pet invoqué est l'entité `CElfPet`
(`CPetController` à `char+0x7dc`).

### 2.1 Enregistrement `elf` (table SQL) — verified

`CExecutionerData::GetElfInsertSQLCmd` @0x086576b0 (zone ; copie mission
@0x0834c350) + `StreamInElfData` @0x0863af40 — `CElfDBA` (0x98 o.) :

| Offset | Colonne / contenu |
|---|---|
| +0x08 | `elf_id` |
| +0x0c | `player_id` |
| +0x10 | `template_id` |
| +0x14 | `level` (short) |
| +0x18 | `exp` (int) |
| +0x1c | `hp` (int) |
| +0x20 | `given_name` (string, tronqué 0x14 o. via `FixMultibyte`) |
| +0x24 | `loc` (short) |
| +0x26 | `mix_star` (short — niveau « étoile ») |
| +0x2a/0x2c/0x2e/0x30 | `grow_attack / grow_hit / grow_critical / grow_attackspeed` (shorts — stats entraînées) |
| +0x32..+0x3a | `battle_spell_1..5` (5 shorts) |
| +0x3c..+0x44 | `integrate_spell_1..5` (5 shorts) |
| +0x5e / +0x60 / +0x62 | loterie : `extra_item_id / extra_counts / extra_multiple` (`pet_lottery`) |
| +0x6a / +0x6e (+0x68,+0x6c,+0x70,+0x74,+0x78) | second jeu loterie (`pet_new_lottery` / diamant) |

Champs runtime CKFElf : `+0x0c` = owner `CCharacter*`, `+0x14` = niveau
(byte), `+0x18` = exp, `+0x3c` = loc, `+0x58` = statut (`EElfStatus`),
`+0x98` = `CPartnerData*` (template), `+0x9c` = `CPartnerGrowData*`
(stats par niveau, chargé via `m_pkInstance+0x1ac`).

### 2.2 Niveau & exp — verified

`CKFElf::GainExp` zone @0x08457770 / `SetLevel` @0x08456ed0 :

- Table d'exp : `CZoneServer::Ins+0x3b4` = `vector<int>` indexé niveau,
  remplie par `CLevelDBO::LoadPetExp` @0x083627d0 — **colonne 25 de
  `S_Level.ini`** (`CLevelData+0x64`), tronquée à `S_Parameter[362] = 100`
  entrées (même cap que le perso).
- **Le niveau de l'elf ne peut pas dépasser le niveau du propriétaire** :
  `SetLevel` refuse `param_1 > *(short*)(owner+0x470)` ; `GainExp` borne
  l'exp à `seuil-1` quand `elfLv == ownerLv` (même schéma que le JP de classe).
- Status `elf+0x58` : `1` = mix (compagnon d'assistance), `2` = invoqué au
  combat (`CElfPet`), `7..9` = états « appelables »
  (`CKFElfManager::CallElf` @0x08072c30 exige `status−7 ≤ 2` ;
  `CheckCallElf` @0x08073a20 re-summon status 2 et re-mix status 1 au login).
- Stats par niveau = table `CPartnerGrowData` (level → stats) ;
  `GetAttack/GetHit/GetCritical/GetAttackSpeed/GetMaxHp/GetDodge/GetResist`
  @0x08453d60+ (accesseurs ; formule non isolée — champs `grow_*` du record).

### 2.3 Travaux d'elf (`CWorkManager`) — verified

Jusqu'à **11 travaux simultanés** (`param_2 ∈ [0,0xa]` ; slots
`char+(0x2c8+i)*4` = `char+0xb20..`). `CWork+0x10` = type :

| Type | Sens | Fonction de fin |
|---|---|---|
| 0xc / 0xd | collecte (récolte raciale) | `EndCollect` @0x08167110 |
| 0xe | invocation d'elf (NPC type **0x3f**) | `EndSummon` @0x08163110 |
| 0xf / 0x10 / 0x11 | combinaison (craft) | `EndCombine` @0x08165510 |

Les PNJ d'artisanat = **types 0x3d..0x42** (`5 < (ushort)(type-0x3d)` rejeté
dans EndCollect/EndCombine ; `char+0x670` = PNJ courant en dialogue,
`TargetInTalkingRange` exigé). Un slot « chance » itemmall optionnel est
consommé (`CChanceItem`).

### 2.4 Mood / familiar / energy — hypothesis / non localisé

Les commandes d'effet existent (`CEC_ElfMood`, `CEC_ElfMoodRate`,
`CEC_ElfFamiliar`, `CEC_ElfFamiliarRate`, `CEC_ElfFamiliarEnergyMood(Rate)`,
`CEC_ElfEnergy`, `CEC_KFElfEnergy`, `CEC_ElfDecrease`, `CEC_ElfRegenerate`,
`CEC_ElfTrainExp/Stuff/Time`, `CEC_TrainSelf`) — corps `Execute` = stubs de
sérialisation ; l'application réelle passe par `CWorkEventCmd`
(`LoadAllWorkEventCmd` @0x08168050, tables de travail). Aucune constante de
seuil trouvée dans les corps lus ; champs candidats non confirmés
(`CElfDBA+0x48..+0x4e` ou CKFElf `+0xa4..+0xd2`).

---

## 3. Sorts raciaux — verified

**Encodage dans la table de sorts elle-même** (pas de table dédiée) :
`CSpellDBO::LoadAllCommonSpell` zone @0x083732b0 itère la carte des sorts
(`m_pkInstance+4`) et remplit `unordered_map<short key, vector<short>>` à
`CZoneServer::Ins+0x1f0` :

```c
/* zone @0x083732e0 — par sort (node+0x10 = spellId, data iVar3) */
uVar1 = *(ushort *)(iVar3 + 0x2c);          // spell+0x2c = bitmask RACES
if (uVar1 & 0x001) raceOffsets += 0;
if (uVar1 & 0x004) raceOffsets += 100;
if (uVar1 & 0x010) raceOffsets += 200;
if (uVar1 & 0x040) raceOffsets += 300;
if (uVar1 & 0x100) raceOffsets += 400;      // 5 races (paires M/F)
for (n = 1; n != 0x3e; n++)                 // spell+0x50 = bitmask 64 bits des classes
  if (classMask64 >> (n-1) & 1)
    map[classId n + raceOffset].push_back(spellId);
```

- `spell+0x2c` bits {0,2,4,6,8} → buckets race `+0,+100,+200,+300,+400`
  (5 races × 2 genres regroupés — `race/2`).
- `spell+0x50` (long long) = **bitmask des classes** (bits 0..0x3d = classes
  1..62) — un sort peut donc être « racial ET multi-classes (branche) ».
- Clé map = `classId + raceBucket`.

`CSpellMasterFactory::GetCommonSpell` zone @0x08157320 — appelée depuis
`PostInit` (login), `ChangeClass`, `GainExp` :

```c
key = (race/2)*100 + classId;               // race = (byte)char[0x4aa] >> 4
for each spellId in map[key]:
    SimpleGetSpell(char, spellId, charLv, 1);   // appris au niveau = charLv
```

- Les sorts raciaux sont **appris automatiquement** au changement de classe /
  level-up, au niveau de sort = niveau personnage (`char+0x470`).
- Cas spécial : `classId % 100 == 0x14` → retour immédiat (la classe 0x14
  n'a pas de sorts raciaux — même classe à mécanique spéciale qu'en doc 1 §5).
- `race = char[0x4aa] >> 4` ∈ 0..9 → paires {0,1},{2,3},{4,5},{6,7},{8,9}
  = les 5 races (cohérent avec §4.2).

**Vérification de `docs/reference/harvest/racial-skills.md`** :
- « sorts raciaux par branche, 2 par race » → **cohérent** (bitmask classe
  64-bit = la « branche » entière partage le sort ; la race choisit la
  variante via la clé race).
- « appris aux niveaux de classe 45 et 60 » → **non confirmé dans le code** :
  l'apprentissage est inconditionnel à l'obtention/level-up ; le gating 45/60
  est probablement une exigence d'usage dans les données du sort
  (hypothesis).
- « un passif racial par classe (trait racial) » → **non localisé** dans ce
  scan (voir « non trouvé »).

---

## 4. Artisanat / métiers (`CLifeSkillFactory` + `CWorkManager`) — verified

### 4.1 Races → artisanats (`CheckLifeSkillType` @0x080cfeb0)

`lifeSkillData+4` = type racial 1..5 ; races autorisées
(`(byte)char[0x4aa] & 0xf0` = race×0x10) :

| Type | Races (nibble) | Paire race/2 | Artisanat (wiki — hypothesis) |
|---|---|---|---|
| 1 | 0x00 / 0x10 | {0,1} | Joaillerie / Fusion de bijoux (Humain ?) |
| 2 | 0x20 / 0x30 | {2,3} | Ingénierie (Zumi ?) |
| 3 | 0x40 / 0x50 | {4,5} | Alchimie (Anuran ?) |
| 4 | 0x80 / 0x90 | {8,9} | ? (Ursun ou Ézelin) |
| 5 | 0x60 / 0x70 | {6,7} | ? (Ursun ou Ézelin) |

Le mapping race→nom exact dépend de la numérotation des races (non relue) —
la structure par paires est verified, les noms restent hypothesis.
`char[0x4aa]` = nibble haut = race (0..9), nibble bas = genre/autre.

### 4.2 Points d'expertise (`GainLifeSkillExp` @0x080d1f30)

```c
if (charLv <= 0x1d) return;                          // exige lv personnage >= 30
gain  = lifeSkillData + 0x14;                        // exp de base
var   = lifeSkillData + 0x18;                        // variance
if (gain >= var) gain = gain - var + rand(2*var + 1);// uniforme [gain-var, gain+var]
*points = min(*points + gain, lifeSkillData + 0x1c); // cap = seuil du palier
if (capped) type==1 → ApperceptionLifeSKill (tirage pondéré)
            type 2..5 → LevelUpLifeSkill (id suivante fixe)
```

`CLifeSkillData` (`m_pkInstance+0x1a4`) : `+0` id, `+4` type racial (1..5),
`+0xc` nom (string), `+0x14` exp base / borne rand Apperception,
`+0x18` variance, `+0x1c` cap de points / points requis,
`+0x24` id de la compétence suivante (LevelUp), cartes à `+0x3c..` pour
Apperception.

### 4.3 Montée de palier

- `ApperceptionLifeSKill` @0x080d08e0 : exige `charLv ≥ 0x1e` (30), race OK
  (§4.1), points ≥ `data+0x1c` (sinon msg 0xf84), aucune compétence de la
  liste d'exclusion possédée (msg 0xfe3) puis **tirage pondéré** :
  `GetShortRand(data+0x50)` + `lower_bound` sur la map {poids@+0x10 →
  nextSkillId@+0x14} — la spécialisation supérieure est **tirée au sort**
  parmi les candidats.
- `LevelUpLifeSkill` @0x080d16a0 : mêmes exigences (msg 0xf83), remplace
  l'id possédée par `data+0x24` (`ReplaceLifeSkill`), ou insère une
  nouvelle `CLifeSkill` (`InsertNewSkill`) — logs « Jewelry »/« Engineering ».
- `char+0xb84` = `unordered_map<ushort id, CLifeSkill*>` (`char+0xb88` =
  bucket count) ; `CLifeSkill {+0 = points}`.
- `ForgotLifeSkill` @0x080d1020, `EndLifeSkill` @0x080d28a0,
  `JewelryAnalysis` @0x080d05f0 (mêmes contrôles de race).

### 4.4 Fabrication (`EndCombine` @0x08165510) — formule de réussite

Recette via `m_pkInstance+0x178` (id = `work+0x14`) ; champs recette :
`+0x14` type (1 = via PNJ), `+0x24` coût or de base, `+0x28` item produit,
`+0x30` quantité, `+0x32` taux d'échec de base (short, %), `+0x3c` chance
« perfect » par 1e8, `+0x40..+0x44` vecteur de réactifs {id,count}.

```c
/* zone @0x08165720 — coût PNJ */
fee  = ((npcRate * baseCost) >> 16) * K1 + (npcRate * baseCost & 0xffff) * K2 + K3;
                                        // K1/K2/K3 = _DAT_087cc694/90/88 (non lus)
cost = baseCost(recipe+0x24) + fee;      // or char+0x4b0 ; msg 0x232 si insuffisant
FamilyLevyBusinessTaxes(char, fee, cost);// taxe → trésor de guilde (§5)
GainGold(char, -cost);                   // débit
/* réactifs consommés : DropItemByItemID(char,0,id,count,0x2d) par paire */

/* zone @0x08165730 — réussite */
fail%  = (100 - recipe+0x32) * (1 - work+0x1c / 100);   // work+0x1c = bonus elf %
succès ⇔ rand()%100 < (100 - fail%) + work+0x32;        // work+0x32 = bonus succès
```

- Réactifs retirés **avant** le jet ; sur échec : `SaveWork(work,
  -work+0x14, 0)` (compte-rendu d'échec).
- Produit équipement (`item+0x20` ∈ [1,0x22] \ {0x10}) : jets « combo » —
  `n = min(itemData+0xf6, 6)` tentatives, chacune réussit si
  `rand%100 < itemData+0xf7` ; puis `SelectWorkItemCombo` + marqueur
  `rand%1e8+1 ≤ recipe+0x3c` (combo parfait).
- `work+0x1c` et `work+0x32` = bonus fournis par l'elf/les items de travail
  (depuis les tables de commande de travail — hypothesis sur la source).

### 4.5 Récolte (`EndCollect` @0x08167110)

- Coût : `collect+0x34` (base) + même formule de fee PNJ ; itemmall optionnel.
- Gain guilde : `SFamily::GainExp(family, char, collect+0x18)` ;
  bâtiment : `GainBuildingExpByItemID(family, char, buildingItemId,
  work+0x2c, work+0x34*collect+0x1c*K2 + collect+0x1c)`.
- Durabilité bâtiment : `UpdateBuildingDurability(family, char, bldg,
  -collect+0x30)` si `GetShortRand(100) ≤ collect+0x2c` (% de casse).

---

## 5. Groupe / guilde / PvP — verified (partiel)

- XP/JP de groupe : déjà documenté — `ExpForTeam`/`JpForTeam*`,
  `S_Parameter[330] = 25`, `[331] = 5` (`native-constants.md` §4). Seuils
  groupe/PvP = `S_Level.ini` cols 20-23.
- **Taxe de guilde sur craft** : `CCharacter::FamilyLevyBusinessTaxes`
  zone @0x0840c960 — sur map de type 4 (`node+0x28→+0x2c == 4`, ville de
  guilde) et si `family+0x32 != 0` (taxe activée) : ajoute le fee PNJ à la
  trésorerie `family+0x34`, borné `treasury+0x2c + tax ≤
  SFamily::MaxTreasuryValue` (constante statique, valeur non relue).
- `CNE_CZ_ZoneServerFamilyDonateMoney::Execute` @0x082568d0 : don joueur →
  trésorerie, même cap `MaxTreasuryValue`.
- Exp de guilde via travaux : `SFamily::GainExp(family, char, collect+0x18)`
  (§4.5) — le montant vient de la table de collecte.

---

## 6. Mouvement / vitesses — verified

`CCharFormula::Movement(CCharacter)` zone @0x083f53b0 — chaîne de log :

```
"Movement = eMoveSpeed100<%.2f> * Buff<%.2f> * 0.5 = %.2f"
```

- `speed100 = *(float*)(char+0x8c + 0xa0)` — vitesse en 1/100 ;
  si flag `char[0x3c]&0x10` **ou** `speed ≤ 3.0` (`_DAT_087b273c`) →
  `speed · _DAT_0888b690` ; sinon constantes `DAT_0888b68c` (×0.5 dans le
  log). Formule affichée : `speed100 · buff · 0.5`.
- `MovementRate` @0x083f3cf0 (doc 1 §3) : `(min(vitesse,3.0) − 1)·100` %.
- Monstres/pets : `Movement(CMonster)` @0x083f3e10 / `(CPet|CElfPet)`
  @0x083f3e70/@0x083f3eb0 : `speed = template+0xa0 · f[+0x18 ou +0x1c]`
  (facteur marche/course selon `param_1[0x3c]&0x10`), plancher
  `dummy_bucket_9887`.

### Montures (`CheckCanRide` @0x08409c50)

- État monté : `char[0x3b] & 2` ; id d'item-monture : `char+0x7f8`.
- `itemData+0x30` = **bitmask de terrains interdits** (bit mis = interdit) :
  types de node {5,7,10,14,18} (`mask 0x408a0`) → bit 0xd ; type 3 → bit
  0xb ; type 1 → bit 0x1d. Retour = 1 si bit absent (monte autorisée).
- Bonus de vitesse de la monture : non localisé dans ces corps (probablement
  `itemData` → champ vitesse, ou l'enchant porté par l'item — hypothesis).

---

## 7. Récapitulatif des constantes chiffrées

| Constante | Valeur | Où |
|---|---|---|
| Palier licence | tous les 10 niv. classe, cap S_Parameter[578]=100 | `GainClassPoint` |
| Licences par classe | 8 (ids `classData+0x44`) | `CClassQuery::Deserialize` |
| Slots certificat | 7 (flag 1..7) | `InvokeSpellStones`, `SetCurrentClassLicense` |
| Limite équipée | `charLv/10 + 1` | `SetCurrentClassLicense` |
| Loadout par classe | 7 ids `node+0xe..+0x1a` | `ChangeClass` |
| classLv classe éveillée à l'obtention | 0x50 = 80 | `AddClass` |
| Déblocage classe | auto : charLv ≥ `classData+0x25` + classes du bitmask `+0x20` à classLv ≥ `+0x24` | `AddClassFromLimit` |
| Exp elf | `S_Level.ini` col 25, cap 100 (P362) | `LoadPetExp` |
| Niveau elf | ≤ niveau du propriétaire | `CKFElf::SetLevel/GainExp` |
| Statuts elf | 1=mix, 2=combat, 7..9=appelable | `CallElf`/`CheckCallElf` |
| Travaux elf | 11 slots max ; types 0xc/0xd collecte, 0xe summon, 0xf..0x11 combine | `CWorkManager` |
| PNJ artisanat | types 0x3d..0x42 (summon : 0x3f) | `EndCollect/EndCombine/EndSummon` |
| Sorts raciaux | clé `(race/2)*100+classId`, appris au niv. charLv | `LoadAllCommonSpell`/`GetCommonSpell` |
| Bitmasks sorts | race `spell+0x2c` (5 bits), classes `spell+0x50` (64 bits) | `LoadAllCommonSpell` |
| Métiers | charLv ≥ 30 (0x1d/0x1e), 5 types raciaux | `GainLifeSkillExp`/`Apperception`/`LevelUpLifeSkill` |
| Exp métier | `base+0x14 ± rand(2·var+1)`, cap `+0x1c` | `GainLifeSkillExp` |
| Craft réussite | `rand%100 < (100−(100−base)·(1−bonus%)) + workBonus` | `EndCombine` |
| Craft combo | ≤6 jets (`item+0xf6`), `rand%100 < item+0xf7` ; parfait `rand%1e8+1 ≤ recipe+0x3c` | `EndCombine` |
| Taxe guilde craft | fee PNJ → trésorerie (map type 4, cap `MaxTreasuryValue`) | `FamilyLevyBusinessTaxes` |
| Vitesse | `speed100(char+0x8c+0xa0)·buff·0.5` ; monture `char+0x7f8`, terrains `item+0x30` | `Movement`/`CheckCanRide` |

## Non trouvé / pistes mortes (à ne pas re-chercher)

- **Coût des licences** : n'existe pas — octroi gratuit tous les 10 niv. de
  classe ; aucun débit d'or dans `GetLicense`/`AddClass`/`AddClassFromLimit`.
- `SFamily::MaxTreasuryValue` : constante statique, valeur non présente dans
  le décompilé (à lire dans `.data` du binaire si besoin).
- `_DAT_087cc688/_690/_694` (échelle fee PNJ) et `_DAT_0888b68c/_690/_6c0/_6c8`
  (mouvement) : flottants `.rodata` non résolus par l'index.
- Mutation **mood/familiar/energy** des elfs : uniquement des commandes
  nommées ; point d'application non localisé (candidats `CElfDBA+0x48..`,
  `CKFElf+0xa4..`).
- **Passifs raciaux** (« trait racial » par classe du wiki) : pas de table ni
  de champ vu — probablement enchant porté par `classData` ou sort passif
  (hypothesis, non localisé).
- Gating « sorts raciaux aux classLv 45/60 » : pas dans le code serveur
  (vraisemblablement condition d'usage dans les données du sort).
- Tables exactes des vtables `m_pkInstance` : `+0x178` (recettes combine),
  `+0x174` (collecte/summon), `+0x1a4` (life skill), `+0x1ac`
  (partner grow), `+0xf8` (item), `+0xec` (classe), `+0x1d8` (groupe de
  classe), `+0x74` (classes par race), `+4` (sorts) — noms de fichiers `.ini`
  non croisés.
- Champs item lus par usage seul : `+0x60` enchant (licence), `+0x8c` licence
  remplacée, `+0xf6`/`+0xf7` jets/chance combo, `+0x76` seuil pile (≥2),
  `+0x30` terrains interdits monture, `+0x20` type d'item,
  `+0x50`/`+0x3e` params RA (RecommendActivities).
