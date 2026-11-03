# Méthodologie Opérationnelle — IA -> Reverse -> Recoder le Serveur

> Ce guide est la référence méthodologique stricte pour les agents d'IA et développeurs travaillant sur le projet.
> Objectif : recréer un serveur propre, moderne et performant en **C# / .NET 8**, en utilisant les binaires d'origine (`server_us`) comme spécification formelle.

---

## 1. La Boucle d'Ingénierie Inverse

```
┌────────────────────────────────────────────────────────┐
│ 1. CIBLAGE DU BESOIN FONCTIONNEL                      │
│    (Ex: handshake initial, login compte, spawn joueur) │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ 2. INVESTIGATION SUR LE BINAIRE ORIGINAL (server_us)   │
│    • Localiser les symboles dans docs/generated/      │
│    • Décompilation Ghidra (Serialize / Execute)        │
│    • Extraction : Opcode, champs binaires, logique DB │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ 3. CODAGE DANS LA SOLUTION C# (.NET 8)                 │
│    • Modéliser le message dans Eden.Core/Networking/   │
│    • Implémenter le handler dans le microservice cible │
│    • Intégrer les requêtes PostgreSQL correspondantes  │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│ 4. VALIDATION ET ESSAIS                                │
│    • dotnet build Eden.sln (0 avertissement, 0 erreur) │
│    • Tests unitaires de sérialisation / désérialisation│
│    • Test de connexion avec le client de jeu           │
└────────────────────────────────────────────────────────┘
```

---

## 2. Outils de Reverse Disponibles

L'environnement dispose de tous les outils nécessaires pré-configurés :

1. **Table des symboles démanglés** (`docs/generated/*-symbols.txt`) :
   - Les binaires originaux sont non-strippés et compilés avec symboles DWARF.
   - Recherche instantanée via `grep` pour trouver les adresses de fonctions, vtables et classes.
2. **Ghidra 12.1.4 & OpenJDK 25** (Ghidra dans `ghidra_12.1.4_PUBLIC/` du dépôt — `$GHIDRA_HOME` ; JDK système) :
   - Projets pré-analysés dans `ghidra_projects/`.
   - Décompilation instantanée en ligne de commande via script headless :
     ```bash
     $GHIDRA_HOME/support/analyzeHeadless ghidra_projects EdenLogin \
       -process LoginServer -noanalysis -readOnly \
       -scriptPath tools/ghidra_scripts \
       -postScript DecompileFunction.java <adresse_ou_nom>
     ```
3. **Framework REA** (Reverse Engineer Anything) :
   - Orchestration assistée par IA et extraction d'évidences.
   - Commandes `node rea-rea-agents-6.5.0/scripts/rea.mjs <cmd>`.

---

## 3. Protocole Réseau & BinStream

### Format de Sérialisation BinStream (`lapis::CBinStream`)
- **Endianness** : Little-endian standard (x86).
- **Types primitifs** :
  - `bool` : 1 octet (0 ou 1)
  - `byte` : 1 octet
  - `short` : 2 octets (16 bits)
  - `int` : 4 octets (32 bits)
  - `float` : 4 octets (IEEE-754 simple précision)
  - `double` : 8 octets (IEEE-754 double précision)
  - `string` : longueur sur 16 bits (`short`), suivie des octets de chaîne.

### Découverte des Opcodes
Les opcodes sont attribués séquentiellement lors de l'enregistrement dans les tables d'initialisation du serveur (`lapis::InitLoginCommandTable`, `lapis::InitGatewayCommandTable`, etc.).

---

## 4. Règles de Développement C#

1. **Immuabilité des binaires d'origine** : Le dossier `server_us` est en lecture seule stricte.
2. **Respect des conventions .NET 8** :
   - Nullable reference types activés (`<Nullable>enable</Nullable>`).
   - Méthodes asynchrones non-bloquantes (`async` / `await`).
3. **Organisation des messages** :
   - Hériter de `NetCommand` (`Eden.Core.Networking.NetCommand`).
   - Implémenter obligatoirement `Serialize(OutBinStream)` et `Deserialize(InBinStream)`.
   - Documenter chaque champ avec le nom et type original C++.
