# Règles projet Eden — point d'entrée agents IA

> **Ce fichier est le point d'entrée.** Les règles détaillées et
> obligatoires du portage C# sont dans **`server/AGENTS.md`** — le lire
> en entier avant toute implémentation, avec
> `docs/MASTER-IMPLEMENTATION-PLAN.md`.

## Le projet en 10 secondes

Réécriture du serveur Eden Eternal en **C#/.NET 10** (`server/Eden.sln`,
un service par ancien processus C++ : Gateway/Login/World/Zone/Mission/
Ticket/HttpApi). Les binaires d'origine (`server_us/`, C++ 32 bits
non-strippés, moteur `lapis`) sont la **spécification exécutable** — on
observe, on ne devine pas.

État : concurrence fiable (Channel borné, ZoneLoop acteur,
PersistenceQueue), 254 cas golden byte-à-byte, 721+ tests verts —
détails dans `server/AGENTS.md` § « État du code ».

## Source de vérité reverse : `re/` + `reindex`

Tout le reverse est déjà exporté dans `re/ghidra/<binaire>/` (copié
dans le dépôt — l'original `~/re` existe encore hors dépôt mais n'est
plus la référence) et indexé dans `re/index.sqlite`. **Commande native à utiliser en priorité :**

```bash
reindex lookup <Nom|lapis::Qualifié> -b zone  # corps décompilé complet
reindex search "<sous-chaîne>" -b zone        # type grep, noms+corps+snippet
reindex addr 0x0805a2c0 -b zone               # fonction contenant l'adresse
reindex xref 0x<addr>|PTR__CNC_Xxx -b zone    # qui référence adresse/vtable
reindex callers|callees <Nom>                 # graphe d'appels (hors vtables)
reindex strings "<motif>" -b zone             # chaînes du binaire
reindex opcode 298                            # NCID ↔ classe C# ↔ nom natif
```

Règles strictes (détail dans `re/AGENTS.md` et `server/AGENTS.md`) :

- ❌ **JAMAIS le serveur MCP `rea`** — doublon des exports existants.
- ❌ Jamais re-décompiler un binaire déjà dans `re/ghidra/`.
- ❌ `server_us/` et `re/ghidra/` = **lecture seule**.
- ✅ Fonction `[FAILED]` → `objdump -d` sur le binaire, pas de ré-export.
- ✅ Toujours citer la preuve : `binaire @ 0x<addr>` + symbole.

## Client cible — Godot uniquement

| Client | Usage |
|---|---|
| `../eden_godot/` | **seul client supporté** — Godot 4 C#, lancé via `godot --path ../eden_godot/godot_project` |

Les clients natifs `client/` et Reforged (`reforged/EdenReforged/`) deviennent
des **oracles en lecture seule** : ils servent à comparer le comportement,
les animations, les layouts et les formules, mais ils ne sont plus des cibles
de compatibilité. Aucune modification protocole/gameplay n'a à rester
byte-compatible avec eux ; la fidélité attendue est comportementale et doit
être prouvée par tests d'oracle, captures ou désassemblage.

Compte test : `test123`/`test123` (ou `admin`) de **notre** base `edenfr_account`.

## Qualité / architecture (résumé — le dur est dans server/AGENTS.md)

- Une classe = une responsabilité ; méthode > ~80 lignes → découper.
- Octets de paquets **uniquement** dans les codecs/builders ; dispatch
  NCID par tables/handlers ; handlers minces → orchestrateurs Gameplay.
- `MatchNamesWithUnderscores` Dapper activé — ne pas désactiver ;
  toujours `await` avant la fin d'un `using` de connexion.
- Items : `ItemSerialAllocator.NextAsync` avant tout INSERT (les
  tables natives n'ont pas de séquence sur `id` — l'allocateur passe
  par la séquence partagée `item_serial`, migration 012).
- Aucun paquet spéculatif : preuve = capture native, désassemblage, ou
  test reproduisant les bytes. Hypothèses = `// HYPOTHESIS:` désactivées.
- Sight-handles (`0x88C8xxxx`/`0xFFFFAxxx`) jamais des charId internes.

## Process

1. Lire `server/AGENTS.md` + `docs/MASTER-IMPLEMENTATION-PLAN.md`.
2. Plan d'action dans `docs/temp-plans/YYYY-MM-DD-<sujet>.md` avant de
   coder ; le cocher au fil de l'eau.
3. `dotnet build` → 0 erreur, viser 0 warning.
4. "Fait" = confirmé côté client, pas juste émis dans les logs.
5. Crash client = bloquant → désactiver le paquet suspect d'abord.

## Outils de preuve wire (déjà en place — les utiliser)

- `server/tests/Eden.Tests/Data/golden/` — 254 cas `Deserialize→
  Serialize` extraits des captures natives ; un codec qui casse le
  golden est un bug de production.
- `tools/wire_diff.py` — diff NCID-par-NCID des streams natif vs C#
  (sorties MITM) avec masques de champs volatils.
- `tools/headless_client` — client de test RSA+RC4 : handshake +
  rejeu de trames contre le serveur live (validé sur la zone :5201).
- `tools/lapis.lua` — dissector Wireshark (header u16 + NCID).
- `docs/generated/FEATURE-MATRIX.md` — statut de preuve par NCID
  (GOLDEN/DESASM/PASSTHRU/INCOMPLET/NON PROUVE), régénéré par
  `tools/gen_feature_matrix.py`.
- `docs/generated/constants.md` + `ncid_map.json` — lexique unique
  des constantes protocole (attrs, services, handles…).

## Docs utiles

`docs/` : `ARCHITECTURE.md` (couches cibles), `METHODOLOGIE-REVERSE-DEV.md`,
`AUDIT-QUALITE-CODE.md` (pièges déjà corrigés), `WIRE-EVIDENCE.md`,
`TUTO-CLIENT.md` (édition contenu `.ini`), `protocole/`, `generated/`.

## Commits / attribution

- Auteur des commits = **Noah Lhommedieu**
  `<115083482+Noah-Lhommedieu@users.noreply.github.com>` (compte GitHub
  du projet — configuré en local dans ce dépôt, ne pas écraser).
- L'agent apparaît en **contributeur** via le trailer
  `Co-Authored-By: Devin <158243242+devin-ai-integration[bot]@users.noreply.github.com>`.
