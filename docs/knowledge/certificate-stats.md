# Certificats de classe — chaîne stats auditée (EDENFR)

> Statut : **verified** (données `corpus/fr-full`, désasm `server_us/
> ZoneServer/ZoneServer` ELF32, tests `Eden.Tests.LicenseTests` 83/83
> verts, suite complète 1542/1542 — 2026-11-03).
> Impact visuel client : *hypothesis* — l'émission serveur des stats est
> prouvée, le rendu panneau reste à confirmer en jeu.

## Chaîne complète

```
c_class.ini cols22-29 (CClassData+0x44, vecteur 8 — 6 remplis)
  └─ certificats octroyés : classLv/10−1 à chaque multiple de 10
     (GainClassPoint — ZoneEntryFlow.Combat.Rewards.cs:401-419)
     et level/10 premiers à AddClass (ZoneEntryFlow.ClassGrant.cs:52)
        └─ player_license (item_id, loc)  — INSERT + DELETE du tier
           remplacé via c_item col35 (LicenseReplaceId, CItemData+0x8c)
              └─ loc 1..7 → slots actifs (player_classlist.lic_1..7
                 pour les autres classes ; SetCurrent/OtherClassLicense)
                    └─ c_item col23 (UseEnchantId) → c_enchant cmd col9+7k
                          ├─ blocs NON listés en col40 → passif →
                          │   StatContribution (stage « licenses » du
                          │   StatPipeline) → CombatantSnapshot.
                          │   ApplyContribution → NativeCombatFormulas
                          │   → ProfileStatEmitter (296/492/493)
                          └─ blocs listés en col40 → set déclenché →
                              procs combat (st.ProcEnchantIds)
```

Capacité native : `charLevel/10 + 1` slots (erreur 0x325, affichage
`lv/10+2` — KB-18). Combos : `InvokeSpellStonesSet` (183) flag 0 =
slots 1-4, flag 1 = slots 4-7, résolu via `c_equipset` (EquipSetPool).

## Couverture par classe (EDENFR c_class.ini)

- **Classes 1-19 et 59 : 6 certificats chacune** (tiers I/II : items
  33xxx/20xxx/21xxx/24xxx/28xxx → 34xxx/37xxx/46xxx/41xxx/56xxx).
- **Classes 20-58, 60, 61 : vecteur vide — données natives authentiques**
  (classes non sorties dans EDENFR : Messie, Barbare, Juge-avancées…).
  Pas de bouche-trou (règle EDENFR).
- Lignes multi-records : le texte `職業取得門檻：` contient `\n` ;
  `IniTable.BuildRows` fusionne jusqu'à 66 cellules — un split naïf
  sur `\n` fait croire à tort à des classes « sans données ».

## Table des effets (tous certs, audit complet)

| enchant | cmd (arg) | effet tooltip FR | canal serveur |
|---------|-----------|------------------|----------------|
| 52506/52676 | 2122\|+5/+8 | Mal (aggro) +% | `HateRatePercent` |
| 52542/52712, 52525/52695, 52521/52691, 50601/50604 | 2122\|−5/−8 | Mal −% | `HateRatePercent` |
| 52507/52677, 52520/52690 | 2083 a1 | « Toutes les DÉF +N % » | `DefenceRatePercent` |
| 46306/46307 | 2083 a2 | « ATQ MAG +N % » | `MagicAttackRatePercent` |
| 52523/52693, 52528/52698 | 2040 a0 | « Taux CRIT +N % » | `CritRatePercent` |
| 52543/52713, 52536/52706 | 2040 a1 | « Taux CRIT MAG +N % » | `MagicCritRatePercent` |
| 52524/52694, 52971/52974 | 2043 a0 | « DÉG CRIT +N % » | `CritDamageAdd` |
| 52540/52710, 52548/52718 | 2043 a1 | « DÉG CRIT MAG +N % » | `MagicCritDamageAdd` |
| 50603/50606 | 2044 a1 | « DÉG MAG CRIT −N % » | `MagicCritDamageAdd` (négatif — fidèle au dispatch ; le « subis » du tooltip est probablement un libellé de données) |
| 52518/52688, 52517/52687, 52544/52714, 52533/52703, 52534/52704, 52526/52696 | 2085 a0..a4 | « FOR/AGI/INT/SAG/CHA +N % » | pct Str/Agi/Int/Wis/Lck — **arg5 jamais lu par le dispatch natif** |
| 52515/52685, 52538/52708, 52509/52679, 52545/52715 | 2088 a1 | « ESQ +N % » | `DodgeRatePercent` (×(1+pct) sur le numérateur) |
| 52527/52697, 52549/52719 | 2088 a0 | « PRÉC +N % » | `HitRatePercent` |
| 52519/52689 | 2002 a0 | « PV MAX +N % » | pct Hp |
| 52535/52705 | 2013 a0 | « PM +N % » | pct Mp |
| 52537/52707, 52547/52717 | 2015 a0 | « Coût PM −N % » | `MpCostFlat` |
| 52531/52701 | 2051 a0 | « VIT DÉP +N % » | `MoveSpeedPercent` |
| 52530/52700 | 2052 a0 | « VIT ATQ +N % » | `AttackSpeedRatePercent` ≈ −arg (AttrWeaponAttackSpeedLevel par catégorie d'arme, +0x414.. — rapproché au % global, **approximation**) |
| 52546/52716, 52541/52711 | 2053 a0 | « VIT INV +N % » | `CastSpeedAdd` = −arg (AttrCastTime +0x138 : − = cast plus court — **signe corrigé**) |
| 52539/52709, 52513/52683 | 2095 a0-5 | « Résist éléments +N % » | `ResistAdds[4..9]` |
| 52973/52976, 50602/50605 | 2101 a0-2 | « Résistances +N % » | `ResistAdds[0..2]` |
| 52512/52682 | 2108 a0 | « Blocage +N % » (si bouclier) | `BlockPercentAdd` (**inconditionnel — approximation**) |
| 52510/52680, 52522/52692, 52972/52975 | 2109 a0 | « Parade +N % » (si 2H) | `ParryAdd` (**inconditionnel — approximation**) |

## Effets NON-stat (procs / hooks combat)

Les blocs listés en **col40** sortent du vecteur passif natif
(`CEnchantQuery::Deserialize` @0x85ecf00 : `*slotPtr = 0` après
insertion dans le set déclenché). Ils ne comptent donc **pas** dans
`ProvideStats` — corrigé 2026-11-03 (avant : `GetUseCommands` les
comptait en passif → MP/ATQ/heal octroyés en permanence).

| enchant | contenu | effet natif |
|---------|---------|-------------|
| 52516/52686, 52511/52681, 52532/52702, 52550/52720 | 2031+2032 non déclenchés | « Double Attaque » — MultiStroke passif : roulé à l'attaque via `GetAttackCommands` + `ProcSources` |
| 52529/52699, 52514/52684, 41711/41714 | 2055 slots=1 evt2 | stun 1-5 %, 3-10 s (« Sonar Tremblant », « Prisonnier de la Mort ») |
| 56112/56113 | 2061 a4=100 slots=1 evt4 | « statut Trivial » 2/4 % (« Rugissement du Dragon ») |
| 46304/46305 | 1014\|5/10 slots=1 evt1 | « 10 % attaque → +5/10 % PM » |
| 41709/41712 | 2047\|1 + 1004 slots=1;2 evt1 | « Mort Ressuscitée » — heal proc |
| 41710/41713 | 2083 slots=1 evt1 | « Assaut de la Mort » — +5/10 % atk 10 s |
| 56114/56115 | 2151 non déclenché | hook **OnAttackBuffPerry** « parade → heal 1000-2000 PV » — commande passive qui *enregistre un hook runtime*, sans accumulateur stats : non implémentable sans hooks combat (documenté) |
| 46308/46309 | 2145 non déclenché | hook **OnAttackBuffMagicCritical** « crit mag → +VIT COMP » — idem |
| 52508/52678, 56110/56111 | 2034\|−3..−8 | « DÉG PHY reçus −N % » — DamageAddPlus natif (réduction **par coup**, `CEC_DamageAddPlus::AffectDamage` @0x085203c0) : combat pur, pas de canal stats — **non couvert** |

## Bugs trouvés et corrigés (2026-11-03)

1. **Blocs transférés comptés en passif** — `ProvideStats` lisait
   `GetUseCommands` (tous les blocs). `GetPassiveCommands` ajouté :
   exclut les slots de `TriggerSlotsRaw` (col40). Effet : les certs
   24235/24238 (+PM), 28048/28051 (heal+PV), 28049/28052 (atk),
   21913 (Trivial), 33295/28050 (stun) n'octroient plus leurs procs
   en permanence.
2. **Double comptage EVA** — le hack regex « EVA +N % » ajoutait +4 pp
   additifs (`DodgePercent`) *en plus* du multiplicatif
   `dodgeRatePct` (2088). Supprimé : le canal multiplicatif natif
   suffit (PhysicalDodgeRate ×(1+pct)).
3. **Signe cmd 2053** — arg0 `AttrCastTime` (− = plus court) était
   versé tel quel en `castSpeed` : « VIT INV +5 % » (2053|-5)
   affichait −5 %. Corrigé en `castSpeed: −V(0)` ; args1-4
   (AttrWeaponCastSpeedRate/AttackSpeedRate %) passés du canal flat
   au canal %.
4. **cmd 2052 non mappé** — « VIT ATQ +8/15 % » (Corsaire) inerte.
   Mappé → `attackSpeedPct` = −Σargs (approximation documentée :
   accumulateurs natifs par catégorie d'arme +0x414..+0x434).
5. **Procs de licences jamais évalués** — `ProcSources` (combat)
   n'énumérait que l'équipement + statuts. `ZoneEntryFlow.License.cs`
   fusionne désormais les enchants de licences à proc/MultiStroke
   dans `st.ProcEnchantIds` après 181/182/183 et la bascule de classe.
   **Limite connue** : `ResolveWeaponAsync` reconstruit la liste
   depuis l'équipement seul ; un re-resolve ultérieur par un autre
   flux (changement d'équipement) perd les ids licence jusqu'à la
   prochaine opération licence — fusion complète exigerait d'éditer
   le chemin combat (hors périmètre autorisé).

## Non couvert (fidélité — à connaître)

- 2034 « DÉG reçus −N % » : réduction par coup (combat), pas de canal.
- 2145/2151 : hooks runtime sans accumulateur.
- 2108/2109 : appliqués sans la condition bouclier/2H native.
- 2052 : catégorie d'arme native → % global (approximation).
- `ActiveEnchantIds` déduplique par `Distinct()` comme `FindEnchant`
  natif : deux licences sur le même enchant = une seule application.
- Aucune migration DB : `player_license`/`lic_1..7` sont conformes.

## Preuves natives citées

- `CSpellDBO::DecodeEffectCommand` @0x8380ad0 (objdump — export
  `[FAILED]`) : 2085→« AttrBase Str/Dex/Int/Spr/Luk 2 » (5 args) ;
  2053 a0→« AttrCastTime », a1-4→« AttrWeaponCastSpeedRate »/
  « AttrWeaponAttackSpeedRate » ; 2052 a0-5→« AttrWeaponAttackSpeedLevel
  0-5 » ; 2088 a0/a1→« AttrHitRateRate »/« AttrDodgeRateRate ».
- `CEC_AttrCastTime::Execute` : `kinddata+0x138 += arg` consommé par
  `CCharacter::GetBuffCastTimeRate` (retranché) → `CCharFormula::
  CastTime` @0x083f7b10.
- `CEC_AttrWeaponAttackSpeedLevel::Execute` @0x085149d0 : accums
  `kinddata+0x414..+0x434` par catégorie (s16, signe négatif=rapide).
- `CEC_AttrBase::Execute` @0x084ff230 : une instance par stat
  (sélecteur 3-7) → arg5 d'un cmd jamais décodé = inerte.
- `SetCurrentClassLicense` @0x82714f0, `SetOtherClassLicense`
  @0x82717f0, `InvokeSpellStonesSet` @0x8418aa0, `ReplaceSpellStones`
  @0x8418cd0, `CPlayerFactory::GetLicense` @0x80fd2xx/0x80fce80.
- `CEnchantQuery::Deserialize` @0x85ecf00 : 4 blocs (cmd+6 double),
  transfert col40 vers le set déclenché (`*slotPtr = 0`).
