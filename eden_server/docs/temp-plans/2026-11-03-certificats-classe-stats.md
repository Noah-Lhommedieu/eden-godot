# Plan — certificats de classe : stats non appliquées (audit complet)

> Symptôme rapporté : « les certificats dans le panneau de classes
> n'appliquent pas les stats (vérifie pour tous) ».
> Référence exécutable : `server_us/ZoneServer/ZoneServer` (ELF32).

## État vérifié (preuves)

### Chaîne données EDENFR (`corpus/fr-full`)

- `c_class.ini` : records **multi-lignes** (le texte de prérequis
  contient un `\n` avant `◎角色等級…`). `IniTable.BuildRows` fusionne
  correctement (header `|V.6|65|`). Certif. = `spellBase−8..−1`
  (`CClassData+0x44`, ancre = 1er run ≥4 cellules ≥50000).
- **Seules les classes 1–19 et 59 ont un vecteur de 6 certs** ;
  classes 20–58/60/61 : vecteur vide (classes non sorties en FR —
  donnée authentique, pas un bug).
- `c_item.ini` : lignes multi-records (78 cols déclarées) ;
  cert → `UseEnchantId` col23 ; `LicenseReplaceId` col35
  (34170→33272, 28051→28048… : le tier supérieur supprime le
  précédent — déjà implémenté dans `GrantLicenseAsync`).
- `c_enchant.ini` : FR 61 cellules → normalisation +1 vide en idx 6
  (déjà correct). Blocs cmd : col9+7k (4 blocs) ; `TriggerSlotsRaw`
  col40 / event 41 / chance 42 / durée 43.
- Octroi : `certIdx = classLv/10 − 1` à chaque multiple de 10
  (`GainClassPoint`, `ZoneEntryFlow.Combat.Rewards.cs:401-419`) et
  `level/10` premiers certs à `AddClass`
  (`ZoneEntryFlow.ClassGrant.cs:52`).

### Comportement natif prouvé (objdump `DecodeEffectCommand` @0x8380ad0 + `CEC_*::Execute`)

| cmd | sémantique native | état serveur |
|-----|-------------------|--------------|
| 2002 | AttrMaxHPRate % (arg0) | OK |
| 2013 | AttrMaxMPRate % | OK |
| 2015 | CostMPPlus flat (arg0, négatif=réduction) | OK |
| 2031/2032 | MultiStroke — **proc passif d'attaque**, pas un stat | commandes lues via `GetAttackCommands` mais les enchants de licences n'atteignent jamais `ProcSources` → procs morts |
| 2034 | DamageAddPlus (dégâts par coup, combat) | pas de canal — à documenter (hors stats profil) |
| 2040 | AttrCriticalRate 2 / AttrMagicCriticalRate 2 (a0 phys, a1 mag) | OK |
| 2043/2044 | AttrCriticalDamage 0 / mag (a0 phys, a1 mag) | OK |
| 2047/1004 | HealPlus / HPPlusRate — **transférés col40** (procs) | **BUG : appliqués en passif** (41709/41712) |
| 2051 | AttrMovement % | OK |
| 2052 | AttrWeaponAttackSpeedLevel `<cat>` (arg i = catégorie d'arme, accums +0x414.. ; signe négatif = plus rapide) | **non mappé → cert « VIT ATQ +8/15 % » (52530/52700) inerte** |
| 2053 | arg0→AttrCastTime (+0x138, négatif=cast plus court) ; args1-4→AttrWeaponCastSpeedRate/AttackSpeedRate | **BUG signe : castSpeed=V(0) affiche −5 % au lieu de +5 %** |
| 2055 | AttrWeaponCriticalLevel (proc, slots=1) | OK non mappé ; mais transféré → ne doit pas être passif |
| 2061 | AttrCastFailRate (proc evt4, arg4=100) | OK non mappé |
| 2083 | a0→P-ATK %, a1→DEF %, a2→M-ATK % | OK ; mais versions proc (41710/41713) actuellement passives → **BUG** |
| 2085 | 5 args → AttrBase Str/Dex/Int/Spr/Luk mode 2 (**arg5 jamais lu nativement** — 52526 « CHA +10 % » inerte natif) | OK fidèle |
| 2088 | a0→AttrHitRateRate, a1→AttrDodgeRateRate (% multiplicatif) | OK ; **hack EVA tooltip ajoute +4 pp en plus → double comptage** |
| 2095 | AttrResist éléms 4-9 | OK |
| 2101 | AttrResist phys 1-3 | OK |
| 2108 | AttrShieldBlock — **conditionnel bouclier** | approx inconditionnelle (documentée) |
| 2109 | AttrWeaponParryRate — **conditionnel 2H** | approx inconditionnelle (documentée) |
| 2117 | AttrBlock % | OK |
| 2122 | AttrHateRate % | OK |
| 1014 | MPPlusRate — transféré col40 (proc « 10 % → +5 % PM ») | **BUG : appliqué en passif** |
| 2145/2151 | hooks combat OnAttackBuffMagicCritical / OnAttackBuffPerry (commande « passive » sans stat — enregistre un hook runtime) | non implémentable sans hooks combat — à documenter |

## Corrections à implémenter — état : FAIT (2026-11-03)

1. ✅ **`EnchantService`** : `GetPassiveCommands(id)` ajouté (+ helper
   partagé `TriggeredSlots` repris par `GetAttackCommands`).
2. ✅ **`LicenseService.ProvideStats`** : utilise `GetPassiveCommands`
   — fin de l'over-application des certs à proc.
3. ✅ **Hack EVA supprimé** (`ApplyTooltipStats` + regex) —
   `enchantDescriptions` ignoré (param conservé pour la signature).
4. ✅ **`ApplyEnchantCommands`** : `case 2053` signe inversé
   (`castSpeed: -V(0)`) + args1-4 → `attackSpeedPct` ; `case 2052`
   ajouté → `attackSpeedPct: -ΣV(i)` ; cas no-op explicites documentés
   (1031/2031/2032, 2033-2036, 2055, 2061, 2035/2105/2112/2144-2152).
   Note : arg index 5 de 2085 n'est jamais lu nativement — les certs
   CHA utilisent en fait a4 (Luk) → mapping déjà correct.
5. ✅ **`ZoneEntryFlow.License.cs`** : `MergeLicenseProcEnchants(st)`
   appelé après 181/182/183 + `SyncLicensesOnClassChangeAsync`.
6. ✅ **Tests** : 83/83 LicenseTests verts (71 nouveaux cas
   paramétrés réels + procs exclus + signes) ; suite complète
   1542/1542 ; build solution 0 avertissement/0 erreur.
7. ✅ **Doc** : `docs/knowledge/certificate-stats.md` créé.
   Graphe régénéré (`tools/graph/graph.sh`).

## Hors portée (documenté, pas d'édition)

- `ProcSources` (fichier combat — interdit) : intégration native
  complète des enchants de licence dans le dispatch de procs.
- 2033/2034/2036 DamageAddPlus, 2145/2151 hooks : effets combat sans
  canal `StatContribution` — fidélité = ignorés + doc.
- Pas de migration DB requise : `player_license`/`lic_1..7` sont
  conformes au schéma natif.
