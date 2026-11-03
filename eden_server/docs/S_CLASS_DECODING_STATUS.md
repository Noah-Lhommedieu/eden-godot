# S_Class.ini / MyCrypt Decoding Status

> **Date:** 15 Octobre 2026  
> **Status:** ✅ **RÉSOLU — 100%**  
> **Password:** `ghfdsjdl`

---

## � Résolution Complète

L'algorithme **MyCrypt** a été entièrement cassé par désassemblage direct
(objdump) du binaire `ZoneServer` — sans avoir besoin de Ghidra.

### Password

**`ghfdsjdl`** — chaîne hardcodée en `.rodata` @ `0x87cdff0`.

Trouvée via les deux appelants de `MyCrypt::coding()`:
- `main` @ `0x80dd1bc` — passe `0x87cdff0` (mode 0: encodage CLI)
- `GameData::ReadINIFile` @ `0x85b9453` — passe `0x87cdff0` (mode 1: décodage au chargement des .ini)

### Algorithme = ZipCrypto (PKZIP legacy)

Reconstitué depuis le désassemblage :

```csharp
// init_keys @ 0x08594d70 — constantes PKZIP standard
k0 = 0x12345678; k1 = 0x23456789; k2 = 0x34567890;
foreach (byte c in password) update_keys(k, c);

// update_keys @ 0x08594be0
k0 = CRC32(k0, b);                          // poly 0xEDB88320
k1 = (k1 + (k0 & 0xFF)) * 0x08088405 + 1;   // multiplicateur PKZIP
k2 = CRC32(k2, k1 >> 24);

// decrypt_byte @ 0x08594bc0 — NOTE: seuls les 16 bits bas de k2 servent
uint t = (k2 & 0xFFFF) | 2;
return (byte)((t * (t ^ 1)) >> 8);

// crypt_decode @ 0x08595050
foreach (byte cb in cipher) {
    byte plain = cb ^ decrypt_byte(k);
    update_keys(k, plain);   // clés évoluent avec le PLAINTEXT
    output[i] = plain;
}
```

### Format de fichier

```
[<encode>]           8 bytes magic — marqueur "fichier chiffré"
[ciphertext]         stream ZipCrypto brut — plaintext commence au byte 0
```

**Pas de header aléatoire dans les fichiers stockés.** `MyCrypt::crypthead`
(0x085952e0) génère un header de 10 bytes via `rand()` côté ENCODAGE uniquement
(path `coding` mode 0); les fichiers livrés commencent directement par le
plaintext (premier octet = `|` ou `V` ou `N` selon la variante de header de table).

### Headers des tables décodées

| Format | Exemple | Fichiers |
|--------|---------|----------|
| `\|V.x\|N\|` + CRLF + rows pipe-delimited | `\|V.6\|65\|` | S_Item, S_Class, S_Monster, S_Spell, S_Mission, S_Enchant… |
| `V.x,N,` + CRLF + rows comma-delimited | `V.5,23,` | S_ElfCombine, S_Races, S_Parameter, S_GMToolCmds |
| `N\|` + CRLF + cols + rows | `4\|`… | T_Mission, T_Title (fichiers client) |
| `[INFO]…` INI classique | Motion.ini | Motion |

---

## � Fichiers décodés

Répertoire généré : `server_us/Data/db_decoded/` (164 fichiers — tous les
`S_*.ini` + les `.ini` client chiffrés).

Outil permanent : `tools/mycrypt.py`
```bash
python3 tools/mycrypt.py decode-all server_us/Data/db server_us/Data/db_decoded
python3 tools/mycrypt.py decode server_us/Data/db/S_Class.ini out.ini
```

### Vérification

- `S_Class.ini` → `|V.6|65|` + 685 lignes (classes + progression par niveau
  `1 A01101 A01201 0|10 A01102 A01202 0|…|90 A01110 A01210 0`)
- `S_Monster.ini` → `|V.6|62|` + monstres `20001|M312040|…` avec stats complètes
- `S_Spell.ini` → `|V.8|45|` + sorts `50001|J001|…`
- `S_Mission.ini` → `|V.5|28|` + quêtes avec scripts `#:RewardGold`, `#:NPCTalk`
- `S_ElfCombine.ini` → `V.5,23,` recettes de craft elfe
- `S_Enchant.ini` → `|V.3|61|` enchants `10001|E0302|…`
- `S_Races.ini` → `V.1,4,` races

---

## 🎯 Conséquences pour le projet

**Toutes les données serveur sont désormais lisibles.** Plus besoin de
placeholders :

- Stats de classes par niveau → `S_Class.ini`
- Sorts et leurs mécaniques → `S_Spell.ini`, `S_SpellReference.ini`
- Monstres + stats → `S_Monster.ini`, `S_MonsterStep.ini`, `S_Ai.ini`
- Quêtes + scripts → `S_Mission.ini`, `S_Dialogue.ini`
- Recettes craft → `S_ElfCombine.ini`, `S_FamilyCombine.ini`, `S_ItemCombo.ini`
- Enchants → `S_Enchant.ini`, `S_ItemMallEnchant.ini`
- Drops → `S_DropItem.ini`, `S_ClassDropItem.ini`
- Talents → `S_Talent.ini`, `S_ClassRankBuff.ini`
- Maps/nodes → `S_Node.ini`, `S_Transport.ini`, `S_Territory.ini`
- Elfes/familiers → `S_Elf.ini`, `S_ElfCollect.ini`, `S_ElfEvent.ini`
- Items → `S_Item.ini` (4.9 MB, déjà en clair), `S_ItemMall.ini`
- PVP → `S_PVP.ini`, `S_Crusade.ini`, `S_Battlefield.ini`
- Races → `S_Races.ini`

Note encodage : les chaînes contiennent du chinois en **Big5/GBK**
(non-ASCII). Parser en Latin-1 puis décoder Big5 pour les noms/descriptions.

---

## ⚠ Piège de parsing — lignes multi-lignes (18 Oct)

Les descriptions Big5 des lignes 3-5 (et d'autres) contiennent des
**LF nus** (`\n` sans `\r`) : un découpage ligne-par-ligne fait
apparaître ces classes « tronquées » (cols stats/sorts vides) alors
que la donnée continue sur la ligne suivante. `IniTable.BuildRows`
joint déjà les fragments jusqu'à `cols+1` champs → **305 lignes
complètes** ; les vecteurs de sorts des classes 3-5 (cols 30-54 :
50213/50214/50232/50269/50078… pour Hunter, etc.) sont présents.
Ne PAS supposer une troncation — vérifier le nombre de champs, pas
le nombre de lignes.

---

## Historique des adresses MyCrypt (ZoneServer, x86)

```
0x08594bc0  MyCrypt::decrypt_byte(uint const*, uint const*)
0x08594be0  MyCrypt::update_keys(uint*, uint const*, int)
0x08594cb0  MyCrypt::CreateCrc32Table()
0x08594d70  MyCrypt::init_keys(char const*, uint*, uint const*)
0x08594e00  MyCrypt::str_crypt_decode(uint*, uint const*, uchar*, int)
0x08594e90  MyCrypt::str_crypt_encode(uint*, uint const*, uchar*, int)
0x08594f40  MyCrypt::crypt_encode(uchar*, int, char const*)
0x08595050  MyCrypt::crypt_decode(uchar*, int, char const*)
0x085952e0  MyCrypt::crypthead(char const*, uchar*, uint*, uint const*, uint)
0x08595b90  MyCrypt::coding(void*, int, Ecrypt, char const*)
0x089c3b54  MyCrypt::crypthead::calls  (static counter)
0x087cdff0  password "ghfdsjdl" (.rodata)
```

Appelants de `coding()`: `main @ 0x80dd1bc`, `GameData::ReadINIFile @ 0x85b9453`.
