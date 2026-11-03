# `limitless_pass.ini` — récompenses du Limitless Pass

Fichier de config lu par `LimitlessService` (zone) à côté de
`balance.ini` dans `DATA_DIR` (`server_us/Data/db_decoded/`).
Surcharge de chemin : env `EDEN_LIMITLESS_PASS_INI`.

**Hot-reload** : modifier le fichier puis `//ll_reload` en jeu (ou
`ReloadAsync`) — pas de restart, pas de rebuild. La révision de
config (`PassRevision`) est bumpée → la liste des paliers (NCID 2033)
est re-poussée aux clients à la prochaine émission.

## Format

INI maison (même style que `balance.ini` : `#`/`;` = commentaires,
lignes vides ignorées) avec deux sections :

### `[level]` — courbe de coût (optionnelle)

```
[level]
niveau = expCumuléeRequise
```

Surcharge/complète la table BDD `ll_level_cost` : même `niveau` → la
valeur ini est prioritaire. **Ne peut pas retirer** un palier défini
en BDD (DELETE/UPDATE en base pour ça).

### `[reward]` — perks/buffs par palier

```
[reward]
niveau | code | kind | valeur | libellé EN
```

- `niveau` — palier Limitless requis (>= 1). Dès que le perso atteint
  ce palier, le perk est actif (comme `limitless_perks` en BDD).
- `code` — code du perk, même namespace que `limitless_perks.code` :
  - stats (pipeline `StatPipeline` stage "limitless") :
    `attack_speed_pct`, `cast_speed_pct`, `move_speed_pct`,
    `defence`, `block_pct`, `parry`, `crit_rate_pct`,
    `phys_attack_pct`, `magic_attack_pct`
  - call-sites (`GetPerkValueAsync`) : `drop_rate` (loot),
    `max_hp` (vitals), `craft_time` (work orders),
    `exp_ultime_mult` (gain Exp Ultime)
  - tout autre code = perk cosmétique affiché mais sans effet tant
    qu'aucun call-site ne le lit.
- `kind` — `mult` (multiplicateur, produit entre perks du même code)
  | `flat` | `percent` (somme entre perks du même code).
- `valeur` — nombre (point décimal invariant), ex. `2`, `1.5`,
  `10` (+10 %), `-20` (-20 %).
- `libellé` — texte EN affiché dans la fenêtre Limitless Pass
  (Ctrl+L) et dans la liste des paliers 2033 ; peut contenir `|`
  (tout après le 4e `|` est le libellé).

## Sémantique de la fusion

Les perks ini sont **fusionnées** aux perks de `limitless_perks`
(additives — l'ini ne peut pas désactiver une perk BDD : mettre
`enabled=false` en base). Elles portent des ids synthétiques
**négatifs** (`-numéroDeLigne`) : elles ne sont **pas journalisées**
dans `player_limitless_perks` (FK sur `limitless_perks.id` —
l'audit des déblocages reste réservé aux perks BDD).

## Exemple livré

Voir `server_us/Data/db_decoded/limitless_pass.ini` — quelques paliers
(1, 2, 3, 5, 7, 10) qui se cumulent avec les perks seed de la
migration `limitless/db/001_limitless.sql`.
