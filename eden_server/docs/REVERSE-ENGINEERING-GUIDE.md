# Guide de Reverse Engineering - Eden Eternal Server

> **Objectif:** Décompiler et analyser les binaires serveur originaux pour reconstruire fidèlement la logique de jeu en C#/.NET 8  
> **Date:** 15 Octobre 2026

---

## 📦 Binaires Serveur à Analyser

Les binaires originaux sont situés dans `server_us/`:

1. **ZoneServer** - 5,760 classes lapis:: (Instance de jeu, combat, déplacement)
2. **WorldServer** - 905 classes lapis:: (Gestion personnages, sélection, tickets)
3. **MissionServer** - 2,458 classes lapis:: (Quêtes globales, classements)
4. **LoginServer** - Authentification et liste des mondes
5. **TicketServer** - Validation des jetons de connexion
6. **GatewayServer** - Passerelle et heartbeat
7. **HTTPAServer** - Interface HTTP/HTTPA

**Total:** ~9,123 classes lapis:: identifiées

---

## 🔧 Outils de Reverse Engineering

### 1. Ghidra (Décompilation principale)

**Installation:**
```bash
# Chemin local: ghidra_12.1.4_PUBLIC (dans le dépôt)
# Version: 12.1.4 PUBLIC
```

**Utilisation:**
```bash
cd ghidra_12.1.4_PUBLIC
./ghidraRun
```

**Workflow:**
1. Importer le binaire (File → Import File)
2. Créer un projet Ghidra
3. Analyser avec Auto-Analyzer (le plus complet possible)
4. Utiliser les symboles DWARF déjà présents (binaires non-stripped)
5. Exporter le code décompilé vers des fichiers texte

**Commandes utiles:**
- View → Symbol Tree pour voir toutes les classes lapis::
- Window → Functions pour lister toutes les fonctions
- Right-click → Decompile pour voir le code C équivalent

### 2. REA (Reverse Engineering Agent) - MCP — NE PAS UTILISER

**DÉPRÉCIÉ : ne jamais utiliser le serveur MCP `rea` sur ce projet.**
Tous les binaires pertinents sont déjà décompilés dans `re/ghidra/<nom>/`
(`decompiled.c`, `functions.txt`, `strings.txt`) — REA réanalyse les
mêmes binaires et ne produit rien de plus.

- Code décompilé → `grep` dans `re/ghidra/*/decompiled.c`.
- Fonction `FAILED/TIMEOUT` dans l'export → `objdump -d
  --start-address=0x<addr>` sur le binaire (adresses dans
  `functions.txt`), `nm -C`, `strings`.
- Binaire non encore exporté → `re/decompile.sh <binaire>
  re/ghidra/<nom>` (Ghidra headless + `re/ExportAll.java`).

### 3. nm (Extraction de symboles)

**Commande:**
```bash
nm -C server_us/ZoneServer/ZoneServer > docs/generated/ZoneServer-symbols.txt
nm -C server_us/WorldServer/WorldServer > docs/generated/WorldServer-symbols.txt
nm -C server_us/MissionServer/MissionServer > docs/generated/MissionServer-symbols.txt
```

**Options:**
- `-C` : Demangle les symboles C++ (affiche lapis::ClassName::Method)
- `--demangle` : Alternative à `-C`

### 4. objdump (Analyse détaillée)

**Commande:**
```bash
objdump -d server_us/ZoneServer/ZoneServer > docs/generated/ZoneServer-disassembly.txt
objdump -t server_us/ZoneServer/ZoneServer > docs/generated/ZoneServer-symbols-full.txt
```

---

## 📚 Méthodologie d'Analyse

### Étape 1: Extraction des Symboles

**Pour chaque binaire:**
```bash
# 1. Extraire les symboles C++
nm -C server_us/<Service>/<Service> > docs/generated/<Service>-symbols.txt

# 2. Filtrer par namespace lapis::
grep "lapis::" docs/generated/<Service>-symbols.txt > docs/generated/<Service>-lapis-only.txt

# 3. Catégoriser par sous-système
grep "CClass" docs/generated/<Service>-lapis-only.txt > docs/generated/<Service>-classes.txt
grep "CSpell" docs/generated/<Service>-lapis-only.txt > docs/generated/<Service>-spells.txt
grep "CEC_" docs/generated/<Service>-lapis-only.txt > docs/generated/<Service>-effects.txt
```

### Étape 2: Inventaire des Classes

**Créer un fichier d'inventaire par sous-système:**
```bash
# Exemple pour les classes
grep "CCharacter::" docs/generated/ZoneServer-lapis-only.txt > docs/inventory/character-methods.txt
```

**Structure d'inventaire:**
```
docs/inventory/
├── character-methods.txt
├── inventory-methods.txt
├── combat-methods.txt
├── class-methods.txt
├── spell-methods.txt
└── ...
```

### Étape 3: Décompilation Ghidra

**Pour chaque système prioritaire:**
1. Ouvrir Ghidra
2. Importer le binaire
3. Lancer Auto-Analyzer avec options:
   - Analyze entry points
   - Stack analysis
   - Function ID
   - Decompiler switch statements
   - Data references
4. Localiser les fonctions cibles dans Symbol Tree
5. Décompiler (Decompile)
6. Exporter vers fichier texte

**Exemple:**
```
Symbol Tree → lapis → CCharacter → ChangeClassAA
Right-click → Decompile
File → Export → Text → docs/decompiled/CCharacter_ChangeClassAA.txt
```

### Étape 4: Documentation

**Pour chaque fonction décompilée:**
1. Copier le code C décompilé
2. Ajouter dans le markdown du système correspondant
3. Noter:
   - Nom de la fonction originale
   - Paramètres (types et noms)
   - Valeur de retour
   - Logique principale
   - Conditions et formules
   - Appels de fonction (calls)
   - Références SQL

**Format de documentation:**
```markdown
### CCharacter::ChangeClassAA

**Signature:** `void CCharacter::ChangeClassAA(int newClassId)`

**Code décompilé:**
```c
void CCharacter::ChangeClassAA(int newClassId) {
    // Code décompilé ici
}
```

**Analyse:**
- Vérifie si la classe est débloquée
- Valide les prérequis
- Met à jour l'équipement
- Applique les buffs raciaux
- Enregistre en DB
```

### Étape 5: Implémentation C#

**Conversion en C#:**
1. Traduire la logique C en C#
2. Adapter les types (int → int, char → byte, etc.)
3. Remplacer les appels PostgreSQL par Dapper/Npgsql
4. Remplacer les structures de données natives par des classes C#
5. Ajouter les tests unitaires

---

## 🎯 Ordre Prioritaire d'Analyse

### Phase 1: Fondamentaux (Weeks 1-2)
1. **CCharacter** - Classe de base du personnage
2. **CClassDBO** - Gestion des classes
3. **CInventory** - Inventaire
4. **CCharacterDBO** - Persistence personnages

### Phase 2: Classes et Skills (Weeks 3-4)
5. **CSpellFactory** - Système de sorts
6. **CSpellMasterFactory** - Master de sorts
7. **CEC_Effect*** - Système d'effets
8. **CLevelDBO** - Progression niveaux

### Phase 3: Combat (Weeks 5-6)
9. **CCharFormula** - Formules de combat
10. **CMonster** - Monstres
11. **CMonsterDBO** - Database monstres
12. **CAIFSM*** - États AI

### Phase 4: Groupes et Social (Weeks 7-8)
13. **CTeamManager** - Gestion de team
14. **CLover** - Système de couple
15. **CFriend** - Système d'amis
16. **CFamily** - Guildes

### Phase 5: Économie (Weeks 9-10)
17. **CItemShop** - Boutiques
18. **CAuctionShop** - Hôtel des ventes
19. **CEnchantMgr** - Enchantement
20. **CCombineOp** - Craft

---

## 📊 Sources de Données

### Fichiers .ini (server_us/Data/db/)
- **T_*.ini** - Données textuelles (noms, descriptions)
- **S_*.ini** - Données serveur (stats, valeurs)
- Encodage: UTF-8 pour T_*, binaire pour S_*

### Structure des .ini
```
# Header
 Nombre de champs |
 Colonnes |

# Data
ID|Field1|Field2|...|FieldN
ID|Field1|Field2|...|FieldN
...
```

### Fichiers SQL (native)
- **database.out** - Dump fnaccount
- **Commandes SQL dans le code** - Via CExecutionerData
- Utiliser `grep -i "INSERT\|UPDATE\|SELECT"` sur le code décompilé

---

## 🔍 Patterns de Code Native

### Pattern 1: Commandes Réseau
```cpp
class CNE_CZ_ZoneServerChangeClass : public CNetCommand {
    virtual void Execute() {
        // Logique de la commande
    }
};
```

### Pattern 2: Database Operations
```cpp
CExecutionerData::GetPlayerDataInsertSQLCmd() {
    return "INSERT INTO player_characters ...";
}
```

### Pattern 3: Effects
```cpp
class CEC_Effect : public CEffectCommand {
    virtual bool CanExecute() { return true; }
    virtual void Execute() { /* apply effect */ }
};
```

### Pattern 4: Formules
```cpp
int CCharFormula::ExpForTeam(int level, int memberCount) {
    return level * level * 100 / memberCount;
}
```

---

## ⚠️ Notes Importantes

### Binaires Non-Stripped
- Les binaires contiennent les symboles DWARF
- Les noms de classes/méthodes sont présents
- Cela simplifie énormément le reverse engineering

### Encodage des Fichiers — ✅ RÉSOLU
- T_*.ini: texte en clair (format `N|\r\n<colonnes>|\r\n<rows>` ou `V.x|n|`)
- S_*.ini: chiffré **MyCrypt = ZipCrypto/PKZIP**, mot de passe `ghfdsjdl`
- Fichiers client chiffrés (Mission.ini, Spell.ini, Enchant.ini…): même cipher
- Décodeur prêt: `tools/mycrypt.py` — voir `docs/S_CLASS_DECODING_STATUS.md`
- Tous les fichiers décodés disponibles dans `server_us/Data/db_decoded/`

### Section dédiée: MyCrypt (ZipCrypto)

Format d'un fichier chiffré:
```
<encode>            8 bytes magic
[ciphertext]        stream ZipCrypto (plaintext dès le byte 0, pas d'IV)
```

Algorithme (ZoneServer x86):
```csharp
k = {0x12345678, 0x23456789, 0x34567890};
foreach (char c in "ghfdsjdl") UpdateKeys(k, c);   // password
foreach (byte cb in cipher) {
    plain = cb ^ ((k2|2) * ((k2|2)^1) >> 8);
    UpdateKeys(k, plain);  // UpdateKeys: k0=CRC32(k0,b);
                           // k1=(k1+(k0&0xFF))*0x8088405+1;
                           // k2=CRC32(k2, k1>>24)
}
```

Adresses ZoneServer: `init_keys` 0x08594d70, `update_keys` 0x08594be0,
`decrypt_byte` 0x08594bc0, `crypt_decode` 0x08595050, `crypt_encode`
0x08594f40, `crypthead` 0x085952e0 (header rand() 10B, encodage seul),
`coding` 0x08595b90. Password en .rodata @ 0x87cdff0.

### Architecture
- **Namespace:** `lapis::`
- **Héritage:** Fortement orienté objet
- **STL:** Utilisation de std::vector, std::map, std::string
- **Pointeurs:** Gestion manuelle de la mémoire

### Endianness
- **CPU:** x86 32-bit
- **Endianness:** Little-endian
- **Structure BinStream:** Same que côté C#

---

## 📋 Checklist par Système

Pour chaque système du MASTER-IMPLEMENTATION-PLAN.md:

- [ ] Extraire les symboles lapis:: concernés
- [ ] Décompiler les fonctions clés avec Ghidra
- [ ] Documenter le code décompilé
- [ ] Identifier les fichiers .ini associés
- [ ] Identifier les tables SQL concernées
- [ ] Identifier les messages réseau (CNC_*/CNE_*)
- [ ] Créer les entités C#
- [ ] Créer les parsers .ini
- [ ] Créer les repositories
- [ ] Créer les services
- [ ] Créer les handlers réseau
- [ ] Écrire les tests unitaires
- [ ] Mettre à jour le markdown d'implémentation

---

## 🚀 Automatisation Future

### Script d'extraction symboles
```bash
#!/bin/bash
# extract-symbols.sh

for binary in server_us/*/; do
    name=$(basename "$binary")
    nm -C "$binary$name" > "docs/generated/$name-symbols.txt"
    grep "lapis::" "docs/generated/$name-symbols.txt" > "docs/generated/$name-lapis.txt"
done
```

### Script de génération inventaire
```bash
#!/bin/bash
# generate-inventory.sh

for file in docs/generated/*-lapis.txt; do
    # Extraire classes, effects, etc.
    grep "CCharacter::" "$file" >> docs/inventory/character.txt
    grep "CEC_" "$file" >> docs/inventory/effects.txt
done
```

---

## 📚 Références

- **Symboles déjà extraits:** `docs/generated/ZoneServer-symbols.txt`, etc.
- **Inventaire partiel:** `docs/SYMBOLS-INVENTORY.md`
- **Plan d'implémentation:** `docs/MASTER-IMPLEMENTATION-PLAN.md`
- **Code natif:** `server_us/`

---

*Document généré le 15 Octobre 2026*

---

## Migration World→Zone — protocole CTicket (phase 4)

`CNE_CZ_ZoneServerReceiveTicket` transporte un **CTicket de 22 octets**
(`CTicket::Set` @ 0x867c870, `Generate` @ 0x867c8d0) :

```
+0  u32  key1          — clé de lookup dans la map tickets émis (0x89b9890)
+4  u32  clientIp      — comparé à GetRemoteHostIP()
+8  u32  zoneIp        — comparé à inet_addr(IP du ZoneServer)
+12 u16  zonePort      — comparé au port du ZoneServer
+14 u32  microtime     — gettimeofday sec*1e6+usec
+18 u32  random        — aléa
```

Validation dans `CNE_CZ_ZoneServerReceiveTicket::Execute` (0x8279230) :
1. `ticket.zoneIp == inet_addr(zoneIpDuServeur)` sinon rejet
2. `ticket.zonePort == portDuServeur` sinon rejet
3. `ticket.clientIp == GetRemoteHostIP()` sinon rejet
4. Lookup `key1` dans la std::map 0x89b9890 des tickets émis →
   `CNC_CZ_ClientTicketInvalid` si absent

Le charId n'est **pas** dans le ticket — la map serveur (remplie à
l'émission par WorldServer) fait le lien clé→personnage. Réponses :
`CNC_CZ_ClientTicketInvalid`, puis `CNC_CZ_ClientMigrationDone` après
`CNE_CZ_ZoneServerMigrationDone`.

Réimplémentation : table `zone_tickets` (PostgreSQL, usage unique,
TTL 5 min) + `TicketRepository`. World émet après
`CNE_CW_ServerLoginCharacter` via `CNC_T_ServerGetTicket`
([u32 k1][u32 k2][u32 zoneIp][u32 microtime][u16 zonePort]) ;
Zone consomme dans `ReceiveTicket` et lie `session.CharacterId`.

## Protocole réseau — découvertes majeures (phase 3)

### Transport TCP (CMSTCPChannel / CMSTCPConnection)

Machine à états `OnParsingHeader` @0x868b7e0 (offset +0x2070) :

| Étape | Donnée fil |
|---|---|
| 1. Handshake | Serveur envoie sa clé publique RSA-2048 brute : `[u32 n_len][u32 e_len][n (BE)][e (BE)]` — construit par `MakeRSAKeyData` @0x8685610 |
| 2. | Client répond `RSA_size`=256 octets : `RSA_public_encrypt(5 octets aléa)` (PKCS1, padding type 1) |
| 3. | Serveur `RSA_private_decrypt` → clé RC4 de **5 octets** ; `RC4_set_key` @0x868cb93/cbb3 pour les DEUX directions (même clé) |
| 4. Paquets | `[u16 len EN CLAIR][RC4(u16 NCID + champs)]` — len = taille du payload chiffré (NCID inclus), RC4 OpenSSL standard @0x86a16a0, état stream continu par direction |

Points clés :
- L'en-tête `u16 len` N'EST PAS chiffré (lu en clair par le state machine).
- RC4 chiffre/déchiffre `len` octets en place (vtable+0x30 Decryption @MakePackets,
  vtable+0x2c Encryption @NextOutputPacket).
- RC4 = implémentation OpenSSL canonique (S-box u32×256 @state+8, i,j aux offsets 0/4).
- RSA-2048 e=65537 généré par `RSA_generate_key` au démarrage du serveur —
  pas besoin de la clé privée originale : le client chiffre avec n'importe quelle
  clé publique que le serveur lui envoie.
- `std::string` réseau = `u16 len` + octets bruts (operator>>@0x867af90) — encodage Big5.

### Format paquet applicatif
`[u16 NCID][payload sérialisé]` — NCID écrit par `CNetCommand<T>::_Serialize`
avant le corps. 1338 vrais NCIDs extraits (index dans `InitPCCommandTable`).

## Protocole réseau — découvertes majeures (phase 2)

### Format de trame

`CNetCommand::_Serialize(stream)` écrit d'abord le **NCID en u16**
(`movzwl NCID` → `operator<<(unsigned short)`), puis appelle le
`Serialize` virtuel pour le payload :

```
[u16 opcode/NCID][payload sérialisé champ par champ]
```

(La longueur de trame est gérée par la couche transport
CBinStream/CNetSession — à confirmer avec le header de paquet.)

### Extraction des vrais opcodes (NCIDs)

- Chaque commande a une variable `CNetCommand<T>::NCID` (symbole `V`,
  `.data`).
- Au démarrage, `lapis::InitPCCommandTable` enregistre les commandes
  dans l'ordre : `mov %ax, <ncid_addr>` où `ax` = index dans la table.
- **Donc l'opcode réel = index d'apparition du `mov %ax, <ncid_addr>`
  dans l'ordre des adresses .text.**

Outil : `tools/extract_ncids.py` → `docs/generated/ncid_map.json`
(**1338 opcodes réels** : Zone=871, World=204, Mission=205,
Login=35, Gateway=23).

Les fichiers `Generated/*.g.cs` ont été patchés avec les vrais opcodes.

### MyCrypt

- Cipher = **PKZIP/ZipCrypto canonique**, password `ghfdsjdl`
  (adresse `0x87cdff0` dans les binaires).
- Clés initiales `0x12345678/0x23456789/0x34567890`.
- Layout fichier : `<encode>` + header pseudo-aléatoire chiffré (10B,
  `rand()` seed fixe) + données.
- Decodeur : `tools/mycrypt.py` + `Eden.Core/Data/MyCrypt.cs`.
- 164 fichiers décodés dans `server_us/Data/db_decoded`.
- Les fichiers `client/data/db/C_*.ini` sont en clair (Big5), même
  schéma `|V.x|N|` que les `S_*.ini`.

## Layouts bouclés CNC — phase 5

### Patterns d'itération natifs
- **SoA** : compteur `esi` indexe des tableaux parallèles
  `[eax+esi*4]` int / `[eax+esi*2]` short — ex. `ClientMailList`
  (u16 count puis ~70 champs/mail, boucle 0x8204660→0x8204b39)
- **AoS** : pointeur avance d'un stride fixe (`add ebx,0x38`) —
  ex. `ClientTeamAllMembersInfo` (short count + [short,short,uint,
  string,long,long,int,int,short×3,ushort,short,string]×n + short)
- **Vector STL** : `[begin]→[end]`/stride avec count u16/short préalable
- **Map STL** : `ushort count` présumé + `_Rb_tree_increment` par paire
- **Boucles imbriquées** : `ClientSearchTeamInfo` = u16 équipes →
  u16 membres + queue fixe (string,string,short) + short suffixe

### Record objet partagé (SItemData)
Séquence récurrente dans `ClientMailList`, `ClientAuctionSellList`,
`ClientAuctionBuyList`, `ClientGetStorageData`, `ClientEnterNodePlayerRoom`,
`ClientEnterNodeElfContainer` :
`short×8, uint, ushort×10, short×2, int64, ushort, uint, ushort×8,
short×3, ushort×2` — précédé de champs slot/flags/string/binary
variables selon le message. `PutBinary` = octets bruts SANS préfixe
de longueur (la taille vient d'un champ adjacent ou est implicite).

### Diff-encodage / sentinelle
`ClientEnterNodeEquipAndSpell`, `ClientGetStorageData`,
`ClientFamilyStorageData` : certains champs/objets ne sont écrits que
si != sentinelle (-1 ou 0xffffffff) avec bitmask construit à la volée.
`ClientGetStorageData` : `[short][byte count]` + records — count=0 →
paquet valide de 3 octets (banque vide).

### Outils
- `tools/patch_cnc_loops.py` : réécrit les classes .g.cs CONFIRMED en
  `List<Entry>` (préfixe, corps, suffixe) — idempotent
- `tools/extract_cnc_loops.py` : heuristique d'extraction — 77
  candidats dont faux positifs (trailers fixes `short×3+uint×2`) ;
  validation manuelle obligatoire avant patch
