# 2026-10-24 — Lot live 5 : sémantique combat, pets, shop, skills, autels, client

Session autonome (joueur endormi). Retours live après le lot 4
(`615d4b7` + `1cc7ccf`, zone :5201 Release). Preuves natives citées
par binaire@adresse. Puis : prompt5.txt (3 chantiers Limitless).
Puis : NCIDs combat/perso/succès/titre/équipement en dernier recours.

## 0. Règle transverse — sémantique des sorts (le « gros souci »)

> « Pourquoi des buffs provoquent des mobs, et un soin m'auto-aggro ?
> Il faut revoir la définition des possibilités des skills : mélanges
> malus+attaque, soin+buff en zone, etc. »

À formaliser : les déterminants natifs sont
- **TargetType** (S_Spell c36 / SSpellData target class) : ennemi /
  soi / allié / zone — pas le kind de dégât.
- **HitKind** (Power/Type) : dégâts vs soin vs pur effet.
- **Enchant résultat** (c39/c40/c41) : `-1` instantané seul (heal,
  procs), `-9` actif caché, `>0` status visible — un heal instantané
  ne doit JAMAIS créer de status.
- **Combat/aggro** : seuls les effets **hostiles** sur une cible
  **ennemie** engagent. Buff soi/allié : jamais d'aggro, jamais de
  stance combat. Soin soi/allié : idem.

## 1. Bug combat/aggro — PRIORITÉ

- [x] Buff self aggro : CAUSE = `ApplyNowhereResultAsync` traitait
  ResultSpellId (enchant 52xxx) comme sort de dégâts → AoE hostile
  sur tous les mobs à 5u. FIX : la branche résultat ne s'exécute que
  si l'id résout vers un spell réellement hostile (kind Physical/
  Magic). Deploy :5201.
- [x] Heal → combat vs soi : `LifeSpellReleaseAsync` émettait
  `302 0000 sel=4` inconditionnel → stance combat. FIX : sel=4
  uniquement si st.InCombat.
- [x] Cure en status : `AddPlayerEnchantAsync` créait un statut pour
  des enchants purement instantanés (dispel 182x, heal…). FIX :
  `IsInstantOnlyEnchant` + `ExecuteInstantEnchantAsync` (gate natif
  effet+0x12c=0 → pas de CEnchant) + dispel réel implémenté.

## 2. Status buff qui ne partent pas

- [~] Le tick d'expiration existe déjà (RestSeconds-- →
  RemovePlayerEnchantAsync @Status.cs ~505) — les statuts « coincés »
  étaient les enchants instantanés créés par erreur (fix ci-dessus)
  + les `-9` persistants volontaires. À RETESTER : si un vrai buff à
  durée reste affiché après expiration, vérifier que la boucle tick
  tourne bien sur ce joueur.

## 3. Talents : stats invisibles au profile

- [~] Point investi (cast spd) : `BonusCastSpeed`/`BonusCastSpeedPercent`
  SONT émis par ProfileStatEmitter (l.264-265) — vérifier live que
  le talent testé mappe bien vers ces champs (cast-spd talent =
  commande 2069 → `CastSpeedRatePercent`). Si rien ne bouge :
  (a) l'enchant du talent n'a pas la commande attendue → dump ses
  vars ; (b) le stage talents ne rejoue pas après +1 point → vérifier
  InvokeAAEnchants → recalcul profile à l'investissement.

## 4. Pets — full bug

- [ ] Spawn sous la map : z=0 prouvé natif — suspect restant =
  coordonnées du SeeMob ou position transitoire. LIVE requis.
- [ ] Ne suit pas : le tick follow EXISTE (`d2>9 → MovePetToward`)
  — vérifier live si le 326 part / owner résolu.
- [ ] Pray : don de stats dynamique — NON implémenté. À reverser :
  NCID/flag pray, enchant associé (stats au maître en status+pipeline).
- [~] UI « rest » : le 187 natif n'émet AUCUN paquet UI (seulement
  textes 0xb30/0xb31 + SetAttackState persiste elf+0x3a bit0x1 via
  DBA — désasm Execute @0x82700f0 + SetAttackState @0x8456760). Le
  client lit donc l'état ailleurs (paquet liste/attr elf) — à
  identifier : probablement le 418 ClientElfUpdateAttr ou le paquet
  de liste elf (470). Vérifier quel champ le UI pet lit.

## 5. Shop / buyback UI

- [ ] Onglet buyback → page blanche : la 512 émise à l'ouverture de
  l'onglet doit avoir le bon flag/ordre ; comparer la requête client
  (NCID 512 c→s probable avec count=0 ou flag).
- [ ] Retour onglet Items → vide : le client redemande la liste shop
  → renvoyer la liste items NPC (paquet existant) à la réouverture.

## 6. Skills — bouton « Max »

- [ ] Monter un sort au max selon gold/CP : trouver le NCID de
  level-up (learn) et sa sémantique « max » (boucle tant que
  affordable, ou flag dédié dans la requête).

## 7. Autels lottery (eden gem / star diamond / eden crystal)

- [ ] Bouton « transform » inopérant : identifier le NCID/flux de la
  lottery d'autel (reindex search "altar|lottery|gem|diamond|crystal"
  -b zone + côté client). Nouveau système à câbler.

## 8. Client rf_game

- [x] Caméra RMB pendant timed cast : `0x75a996` `74 13`→`EB 13` —
  la gate `ctrl+0x2e4 bit1` (lock-marche) ne dévie plus le yaw vers
  la directive turn ; le RMB orbite toujours. Effet de bord :
  le RMB-drag en mode lock-marche tourne la caméra au lieu de steer
  (comportement vanilla du mode). RETEST JOUEUR.
- [ ] Strafe monté toujours mauvais gauche/droite après patch
  0x75DC08 : la directive `cam+0xe4=10` n'était peut-être pas le
  mécanisme — creuser les records de direction monture
  (DAT_00fe4ba0/4bd8), le fn-ptr `record+0x10`, et qui pivote
  réellement le skin (anims strafe monture ? facing serveur 326 ?).

## 9. AoE — vérification finale

- [ ] Retester multi-hit en jeu après le fix shape0 (Tornado 50027,
  Lightning Web 50032).

## 10. Validation

- [ ] `dotnet build` 0 err/0 warn + `dotnet test` ≥1073 verts.
- [ ] Deploy zone :5201 Release.
- [ ] Compte-rendu : headless-vs-live + attribution par bug.
