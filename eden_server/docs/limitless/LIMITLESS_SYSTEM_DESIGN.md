# Limitless Level & Exp Ultime — Design technique

> Chantier 3 du plan Limitless. Document de conception — aucune
> implémentation ici ; il sert de référence pour le code backend
> (zone C#) et le modding client (barre + panneau du pass).

## 1. Vision

Un système de **progression infinie** (Battle Pass permanent) qui ne
s'active qu'au **niveau maximum actuel** du serveur. Le cap de
`character level` est **dynamique** (variable de configuration — pas
une constante). Quand un joueur atteint le cap :

- l'EXP normale est **bloquée** (plafond dur, aucun dépassement) ;
- il gagne à la place de l'**Exp Ultime** (points Limitless) selon
  des règles anti-leech strictes ;
- les paliers Limitless débloquent des **Perks** — des buffs
  permanents gérables dynamiquement (ajout/modif/suppression à
  chaud) par les admins.

Vocabulaire :
- **Exp Ultime** = la monnaie de progression par kill (points).
- **Limitless Level** = le niveau du pass, dérivé du total d'Exp
  Ultime via une courbe configurable.
- **Perk** = un bonus permanent appliqué au joueur (taux de drop,
  stats, temps de craft…) dont la valeur et l'état sont dynamiques.

## 2. Règles métier (figées côté design)

### 2.1 Cap dynamique
- `GameBalance` expose `LevelCap` (int, lu depuis une table de
  config ou `EDEN_BAL_LEVEL_CAP`). Ce n'est **pas** un hardcode —
  l'augmenter repousse simplement le seuil où s'arrête l'EXP
  normale.
- Toute la logique EXP normale existante conserve `LevelCap` comme
  plafond : `min(gain, needed(level, cap))`.

### 2.2 Gain d'Exp Ultime par kill — règles strictes
Le joueur gagne **1 point d'Exp Ultime** par monstre tué **si et
seulement si toutes les conditions sont vraies** :

1. `player.Level >= LevelCap` (le système n'existe pas en dessous).
2. `|mob.Level - player.Level| <= 5` — le mob doit être à 5 niveaux
   max du joueur (anti farm de mobs gris).
3. Le joueur a **lancé au moins une compétence (skill)** sur ce mob
   avant sa mort — un simple coup auto-attaque ne compte PAS
   (anti-leech / anti-bot passif). « Skill » = tout sort avec un
   `spellId != 0` résolu sur le mob (cast ou instantané), pas le
   swing d'arme.
4. La mort doit être attribuée au joueur (crédit de kill normal —
   pas un mob suicidé/dot d'un autre).

Points par kill : 1 (fixe). Un multiplicateur de palier peut
majorer ça via un Perk « Exp Ultime xN » (valeur dynamique).

### 2.3 Limitless Pass
- Les **paliers** (`limitless_level`) débloquent des Perks.
- La courbe Exp Ultime → niveau est une table configurable
  (`ll_level_cost`), pas une formule figée — l'admin ajuste sans
  recompiler.
- Les Perks sont **dynamiques** : leurs définitions (type, valeur,
  palier requis) vivent en BDD, modifiables à chaud (admin / outil
  Studio) et rechargées sans restart (table de config hot-reload).

### 2.4 Persistance & multi-personnage
L'Exp Ultime est **par personnage** (pas par compte — chaque perso
a sa progression Limitless, comme son niveau).

## 3. Architecture BDD

Nouvelles tables (schéma `fndb1`, mêmes conventions snake_case +
`player_id` que le reste) :

```sql
-- Progression Limitless par personnage (1 ligne/perso).
CREATE TABLE player_limitless (
    player_id      INTEGER PRIMARY KEY REFERENCES player_characters(id),
    exp            BIGINT NOT NULL DEFAULT 0,   -- total Exp Ultime
    level          INTEGER NOT NULL DEFAULT 0   -- Limitless Level (cache)
);

-- Définitions des perks du pass (modifiables à chaud).
CREATE TABLE limitless_perks (
    id             SERIAL PRIMARY KEY,
    code           VARCHAR(64) NOT NULL UNIQUE, -- ex 'drop_rate_mult'
    name           VARCHAR(128) NOT NULL,
    description    TEXT,
    required_level INTEGER NOT NULL,            -- palier Limitless requis
    value          DOUBLE PRECISION NOT NULL,   -- magnitude (x2 → 2.0, -20% → -0.2)
    value_kind     VARCHAR(16) NOT NULL,        -- 'mult'|'flat'|'percent'
    enabled        BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order     INTEGER NOT NULL DEFAULT 0
);

-- Perks réellement débloqués par perso (snapshot à l'atteinte du
-- palier — permet d'auditer / de retenir un perk retiré plus tard).
CREATE TABLE player_limitless_perks (
    player_id INTEGER NOT NULL REFERENCES player_characters(id),
    perk_id   INTEGER NOT NULL REFERENCES limitless_perks(id),
    claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (player_id, perk_id)
);

-- Courbe configurable : coût cumulé d'Exp Ultime par Limitless Lv.
CREATE TABLE ll_level_cost (
    level   INTEGER PRIMARY KEY,
    exp_req BIGINT NOT NULL        -- Exp Ultime cumulée pour ce niveau
);

-- Séquence config initiale (exemple — ajustable).
INSERT INTO ll_level_cost(level, exp_req)
SELECT lvl, 100 * lvl * lvl FROM generate_series(1, 50) AS lvl;
```

Notes design :
- `player_limitless.level` est un **cache** dérivé d'`exp` via
  `ll_level_cost` — on le stocke pour les requêtes classement et le
  paquet UI (évite de recalculer), mais la source de vérité est
  `exp`. Toute mise à jour réécrit les deux atomiquement.
- `player_limitless_perks` = journal des déblocages, pas la source
  des buffs actifs — les buffs actifs sont dérivés de
  `limitless_perks.enabled AND required_level <= level`. C'est ce
  qui permet de **supprimer/modifier** un perk dynamiquement : on
  édite `limitless_perks`, les buffs se recalculent, le journal
  garde l'historique de "ce perso avait atteint ce palier".
- Les valeurs des perks utilisent `value_kind` pour lever
  l'ambiguïté d'unité (x2 vs +2 vs -20%).

## 4. Logique backend (zone C#)

Points d'intégration dans le flux existant — aucun nouveau service
réseau, tout est **serveur-side** :

### 4.1 `LimitlessService` (Eden.Core.Game.Services)
- `GetProgressAsync(charId)` → `(exp, level)`.
- `AddExpAsync(charId, points)` → incrémente `player_limitless.exp`,
  recalcule `level` depuis `ll_level_cost`, émet le paquet UI si le
  niveau a changé, retourne le nouveau total.
- `GetActivePerksAsync(charId)` → `limitless_perks` actifs
  (`required_level <= level AND enabled`) — alimente la contribution
  de stats (StatPipeline) et les effets de drop/craft.
- `ReloadPerksAsync()` → recharge `limitless_perks` depuis la BDD
  (hot-reload admin).

### 4.2 Traque « skill lancée » — `CombatantSnapshot` / `LiveMob`
- `LiveMob` gagne `HashSet<int> SkillHitCharIds` — les charIds ayant
  résolu un sort sur le mob (rempli dans
  `ExecuteSpellOnLifeAsync` / la résolution de dégâts par sort —
  partout où `spellId != 0` touche le mob). Les coups
  d'auto-attaque (spellId == 0) ne remplissent pas.
- Coût mémoire nul tant que le mob vit (cleared à la mort/reset).

### 4.3 Attribution — `Combat.Rewards` (GainExp du kill)
Dans le chemin de récompense du kill (`FinishMobDeathAsync` → exp) :
```
si player.Level >= LevelCap
    ET |mob.Level - player.Level| <= 5
    ET mob.SkillHitCharIds.Contains(charId) :
        points = 1 * perkExpMult(charId)          // perk xN
        AddExpAsync(charId, points)
        // + paquet UI (barre) via NCID custom
```
Le blocage de l'EXP normale au cap existe déjà conceptuellement
(le level-up s'arrête) — on ajoute juste le routage vers l'Exp
Ultime quand le cap est atteint.

### 4.4 Application des Perks
- **Stats** : étape `"limitless"` de `StatPipeline` — somme des
  `value` des perks actifs mappés par `code` (PV max → MaxHp,
  drop → canal dédié hors stats de combat).
- **Drop/craft/rates** : lus par les call-sites concernés au moment
  de l'effet (pas de pipeline de stats — ce sont des multiplicateurs
  de taux).
- Injection : `PerkManager` (dans `LimitlessService`) expose
  `GetPerkValue(charId, code)` → les handlers lisent la valeur au
  moment utile — pas de recompute global.

### 4.5 Émissions réseau
- Paquet UI Limitless — NCID custom (hors plage native, ex. 0x7f0+)
  `[i32 charId][i64 exp][s16 level][u16 perkCount]` — le client
  l'affiche dans le panneau profile + la barre d'exp.
- À l'entrée en zone : poussé avec l'état initial (comme les 280).
- À chaque `AddExpAsync` et à chaque changement de niveau/perk.

## 5. UI & Client Modding

Le client est un exécutable Flash/CEGUI — l'affichage du Limitless
Pass demande du modding (pas de support natif). Plan d'action :

### 5.1 Barre d'Exp Ultime (profile)
- Le panneau profile (`ProfileWnd`/`CharInfoWnd` layout) a la barre
  EXP normale. Ajouter une **seconde barre** = dupliquer le widget
  dans le `.layout` (CEGUI) + label "Limitless".
- Source des données : un handler client (CeguiDialog) lisant le
  NCID custom 5.x — nécessite un hook d3d9/mode existant (le même
  mécanisme que les QoL Reforged) pour router le paquet → champ UI.
- **Plan B si le hook UI est trop coûteux** : afficher le total
  dans le chat/log via un texte index, et le pass dans une fenêtre
  dialog existante détournée (Evaluate/FortuneBag ont des listes
  réutilisables).

### 5.2 Fenêtre du Limitless Pass (perks)
- Fenêtre CEGUI personnalisée listant les perks (icône, nom, effet,
  palier) — nécessite : layout `.layout` + imageset + handler
  dialog C++. C'est la partie la plus lourde (du modding d3d9).
- Les perks arrivent via le NCID custom (payload sérialisé) — le
  client les rend en liste.

### 5.3 Faisabilité réelle (à cadrer avant de coder le client)
- Le hook d3d9 Reforged sait déjà intercepter NCIDs + modifier des
  widgets (btn_max, monster_stats). C'est le **canal** réaliste.
- Le modding layout CEGUI pur (sans hook code) ne suffit pas pour
  des données dynamiques → le hook est requis.
- Si hors de portée immédiate : la V1 serveur-side (DB + Exp Ultime
  + perks + NCID custom) reste valable — l'UI peut suivre dans un
  itérative client-mod séparée.

## 6. Questions/réponses (à clarifier avec le mainteneur)

> Rempli après retour — les suppositions actuelles sont marquées.

1. **Le multiplicateur d'Exp Ultime** vient-il d'un perk ou d'un
   taux global ? → supposé : perk « Exp Ultime xN » (dynamique).
2. **Les perks modifiables à chaud** doivent-ils conserver
   l'historique des versions précédentes ? → supposé : journal
   `player_limitless_perks` + valeur actuelle dans `limitless_perks`.
3. **La barre UI** doit-elle apparaître à la place de la barre EXP
   normale au cap, ou en plus ? → supposé : en plus (le cap de
   l'EXP normale reste visible plein).
4. **Anti-leech** : faut-il que le skill ait fait des dégâts, ou le
   simple fait d'avoir casté (même raté/missed) ? → supposé : un
   sort *résolu* sur le mob compte (même miss — la tentative
   suffit, sinon le heal/buff ne marche pas).
5. **Par perso vs par compte** : Limitless par perso confirmé ?
   → supposé : par perso (comme le level).
6. **Le cap** peut-il descendre ? Si oui, l'EXP stockée au-delà est
   perdue ou conservée ? → supposé : conservée (on ne dégrade pas
   la progression acquise).
7. **NCID custom** : plage réservée préférée ? (0x7f0+ = espace
   Reforged/Limitless, hors natif ≤ ~900.) → à confirmer.
