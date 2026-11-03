# Architecture cible — serveur Eden C#

> 18 Oct. Audit du code actuel + conception pour un **vrai serveur de jeu
> complet** (pas un simulateur de paquets). Objectif : toute mécanique du
> jeu d'origine (formules, résistances, immunités boss, drops spéciaux,
> events…) doit avoir un **point d'extension naturel**, jamais un `if`
> ajouté dans un flux réseau.

## 1. Audit — ce qui pose problème aujourd'hui

### 1.1 `ZoneEntryFlow` = god object (5 043 lignes / 14 partials)

Les partials découpent le *fichier*, pas les responsabilités :

- `TryHandleAsync` : switch de ~40 NCID où la **logique métier vit dans
  le case** (MigrationDone = 60 lignes de replay+DB+mission+regen inline).
- `ZoneEntryFlow.Combat.cs` (1 604 lignes) mélange 4 couches : émission
  de paquets (`Concat(BitConverter…)` partout), résolution de combat,
  IA mob, mort/loot/exp/respawn, regen.
- `EntryState` : sac mutable de ~50 champs couvrant identité, combat,
  sight, dialogue, shop, transport — chaque méthode touche le même sac.

### 1.2 Gameplay hardcodé — le vrai risque « petit simulateur »

- `dmg = 60 + rand(40)` (mêlée), `80 + rand(60)` (sort) —
  **aucune stat n'intervient** (atk/def/resist/crit/dodge/absorb inexistants).
- Aucun `TargetRule` : impossible d'exprimer « ce skill ne s'applique pas
  aux boss » sans patcher `ExecuteSpellHitAsync`.
- Loot appelé en dur dans `DistributeLootAsync` ; ajouter un type de
  drop (BP drop, world drop, drop event) = modifier la méthode.
- HP/MP : la formule S_Level+CON est **dupliquée** (entrée, level-up,
  création) — 3 copies à faire diverger.

### 1.3 Doublons structurels

- `Eden.Core/Data/Repositories/CharacterRepository.cs` **et**
  `Eden.Core/Database/Repositories/CharacterRepository.cs` — deux classes
  du même nom, deux schémas.
- `Game/Services/CharacterCreationService.cs` **et**
  `Game/Characters/CharacterCreationService.cs`.
- Deux chemins de dispatch : `ZoneEntryFlow.TryHandleAsync` (switch) et
  `Handlers/*Handlers.cs` (modules) — un NCID peut passer par l'un ou
  l'autre selon qui l'enregistre.

### 1.4 Ce qui est déjà bon (à garder)

- `ZoneServices` : record centralisateur — bon pattern, à conserver.
- `Codecs/NativeItemCodec` : builders de paquets séparés — à généraliser.
- Parsers `.ini` typés (`MonsterParser`, `SpellParser`…), repositories,
  messages générés `*.g.cs`, liens Zone↔World↔Mission isolés.
- `ZoneLiveWorld` : registre d'entités vivantes séparé de la session.

## 2. Architecture cible — 4 couches strictes

```
┌─ Eden.Zone (serveur) ─────────────────────────────────────────────┐
│  Program.cs           : bootstrap, sockets, crypto                │
│  Dispatch/            : NCID → IZonePacketHandler (table unique)  │
│  Handlers/            : UN handler par domaine, MINCE :           │
│                         parse payload → appel orchestrateur →      │
│                         émet réponses via IZoneWire                │
│  Gameplay/            : orchestrateurs par domaine                 │
│  Codecs/ + *.g.cs     : formats de paquets (SEUL endroit où des    │
│                         octets sont écrits)                        │
└──────────────┬────────────────────────────────────────────────────┘
               │ dépend de
┌─ Eden.Core.Game (domaine pur — ZÉRO socket/paquet) ────────────────┐
│  Stats/    : StatSheet, formules (S_Parameter)                     │
│  Combat/   : DamagePipeline, AttackContext, stages, règles cible   │
│  Skills/   : ISkillEffect (stratégies d'effets de sorts)           │
│  Loot/     : LootEngine + ILootSource (providers)                  │
│  Progression/, Vitals/, …                                          │
└──────────────┬────────────────────────────────────────────────────┘
               │ dépend de
┌─ Eden.Core.Data + Eden.Core.Database ──────────────────────────────┐
│  Parsers .ini (lecture seule) · Repositories PostgreSQL            │
└────────────────────────────────────────────────────────────────────┘
```

**Règle d'or** : la donnée circule vers le bas (payload → contexte →
résultat → paquet), jamais de réseau dans le domaine.

### 2.1 Stats — `StatSheet` (point d'entrée unique des formules)

```csharp
// Eden.Core.Game/Stats/StatSheet.cs — domaine pur, testable.
public sealed record StatSheet(
    int Level, int MaxHp, int MaxMp,
    int Atk, int Matk, int Def, int Mdef,
    int Hit, int Dodge, int Crit, double Aspd,
    ImmutableDictionary<Element,int> Resists /* feu/glace/foudre/sol/lumière/ténèbre */);
```

Sources combinées **dans cet ordre** (modèle `CCharacter` natif) :
`S_Level` (base) → `% classe` (S_Class) → `CON/WIS/…` via `S_Parameter`
→ `+ équipement` (S_Item attrs) → `+ enchants/buffs` (S_Enchant) →
`+ talents` → `flat params`. Aujourd'hui : seule la 1re moitié existe,
dupliquée 3×. Demain : la résistance d'un anneau ou l'immunité d'un buff
est un *addend*, pas un patch.

### 2.2 Combat — `DamagePipeline` (stages ordonnés)

```csharp
// Eden.Core.Game/Combat/DamagePipeline.cs
public sealed record AttackContext(
    Combatant Attacker, Combatant Target,
    SkillDef? Skill, DamageKind Kind, Element Element);

public interface IDamageStage { void Apply(AttackContext ctx, DamageWork w); }

// Pipeline = liste ordonnée injectée au boot :
//   HitCheckStage      → miss si rand > Hit/(Hit+Dodge)   (esquive)
//   BasePowerStage     → atk/matk du StatSheet + skill.Power
//   MitigationStage    → def/mdef du StatSheet cible
//   ResistStage        → resists[element] de la cible
//   TargetRuleStage    → ITargerRule : BossImmune(grade≥5 & flags skill),
//                        PvpOnly, NoHeal… — « skill X pas sur boss » = UNE règle
//   VarianceStage      → ±x% (S_Parameter)
//   CritStage          → crit → multiplicateur
//   FinalClampStage    → min 1, cap, absorb/shield
```

Ajouter une mécanique native (ex. résistance %, réduction boss, réflexion)
= une **classe stage/règle de 20 lignes**, jamais un `if` dans le flux.

`MobCombatLoop`, `HandleMelee`, `ExecuteSpellHit` ne calculent plus :
ils construisent un `AttackContext` et lisent `result.Damage`.

### 2.3 Skills — `ISkillEffect` (stratégies au lieu du switch)

```csharp
public interface ISkillEffect {
    bool CanApply(AttackContext ctx);          // règles cible/niveau/portée
    Task ApplyAsync(AttackContext ctx, ISkillSink sink);
}
// Enregistrement par type d'effet S_Spell : DamageEffect, HealEffect,
// BuffEffect (S_Enchant), BlinkEffect, SummonEffect, DispelEffect…
```

Le `Param1 & 0x04000000` (blink) inline actuel devient `BlinkEffect`.
Les immunités boss passent par `CanApply` + `TargetRuleStage`.

### 2.4 Loot — `LootEngine` + providers composables

```csharp
public interface ILootSource {
    IEnumerable<LootDrop> Roll(LootContext ctx);
    // ctx : mobDef (grade normal/elite/boss), killerLvl, classe active, party
}
// Providers enregistrés : NormalDrop (S_DropItem), ClassDrop
// (S_ClassDropItem), BpDrop, WorldDrop, EventDrop, GoldDrop.
```

Ajouter « BP drop » = un provider ajouté à la liste — le flux de mort
appelle `LootEngine.RollAll(ctx)` et ne connaît plus les tables.

### 2.5 Dispatch — table unique

```csharp
// Remplace le switch de TryHandleAsync + les modules Handlers parallèles.
sealed class ZoneDispatch {
    readonly Dictionary<ushort, IZonePacketHandler> _t = new();
    // enregistré au boot : _t[5]=new MeleeHandler(combat), _t[9]=…
}
```

`ZoneEntryFlow` réduit à l'**entrée** (ticket, echo burst, migration,
déco) — tout le reste sort dans des orchestrateurs.

### 2.6 État session — `EntryState` éclaté

`PlayerSession { Identity, Vitals, Combat, Sight, Dialog, Shop, Cast,
Transport }` — chaque orchestrateur ne reçoit que ses sous-états ; fini
le sac de 50 champs que tout le monde mute.

## 3. Migration incrémentale (pas de big-bang)

| Étape | Action | Risque |
|---|---|---|
| 1 | `StatSheet` + `DamagePipeline` ; le combat garde son flux exact mais lit `result.Damage` au lieu de `60+rand` | faible — valeurs seules changent |
| 2 | Extraire tous les `SendPacketAsync(Concat…)` inline → `Codecs/ZonePackets.cs` (façade typée) | nul — déplacement pur |
| 3 | Sortir Loot/Exp/Regen/Mort → orchestrateurs `Gameplay/` appelés par `FinishMobDeath` | faible |
| 4 | Table de dispatch unique ; `EntryState` éclaté | moyen — toucher l'entrée |
| 5 | Supprimer les doublons `Data/Repositories`, `Game/Services` orphelins | nul — dead code |

Chaque étape = un temp-plan `docs/temp-plans/` + build 0 warning +
validation in-game (règle : le combat actuel, fonctionnel, reste la
référence — aucun changement de séquence paquet tant que l'étape 1-2
n'est pas validée).

## 4. Correspondance avec le natif (preuves à suivre)

Le natif a déjà cette séparation — on la reflète :

| Natif (decompiled.c) | Cible C# |
|---|---|
| `CCharFormula` (MaxHP/AttackRange/CastTime/RecoverHP) | `StatSheet` + `EdenFormulas` |
| `CCharacter::CalcDamage*` / résistances | `DamagePipeline` stages |
| `SSpellData` flags + `CheckLevel`/`CheckClass` | `ISkillEffect.CanApply` |
| `CItemDropData` + tables drop | `ILootSource` providers |
| `CEvent*` (DelayDamage, SpellCasting) | ordonnanceurs orchestrateurs |

Avant d'écrire une formule : la chercher dans `re/ghidra/zone/decompiled.c`
(`CCharFormula::*`, `S_Parameter.ini`) — **jamais de formule inventée**.

## 5. Mode de déploiement — choix officiel hybride

- **Dev/prod locale** : les 6 services C# tournent **nativement sur
  l'hôte** (`dotnet run` / `tools/start-local.sh`) ; **seul PostgreSQL
  est sous Docker** (`docker-compose.yml`, service `postgres`).
  Itération plus rapide, pas de build image à chaque changement.
- **CI / reproduction** : `docker-compose.yml` conserve les services
  .NET + healthchecks pour reproduire la stack complète en un
  `docker compose up` (audit `AUDIT_PROJET_ETAT_ACTUEL.md` §5.2).
- **Prod (cible)** : conteneurisation complète de l'ensemble des
  services — le compose existant en est la base ; l'hybride actuel
  n'est que le mode de développement.
- Config centralisée : `EdenConfig` (`server/src/Eden.Core/
  EdenConfig.cs`) — toutes les variables `EDEN_*`/ports/rates y sont
  typées avec défauts documentés ; ne plus lire
  `Environment.GetEnvironmentVariable` ailleurs.
