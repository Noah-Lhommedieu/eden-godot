# Plan session — objectifs live, consommables, XP classe, portails

## Symptômes rapportés (live, client Reforged)
- Tracker : seuls les objectifs kill se mettent à jour en live ; collect/
  deliver/talk restent gris jusqu'au relog.
- Inventaire : équipement fige/crash ; déplacements, montures,
  consommables sans effet.
- Portails monde/instance inertes.
- Pas de regen mana.
- XP classe : pas de level-up, XP "perdue" au changement de classe,
  shortcuts non sauvegardés par classe.
- Mort après chaque kill + ciblage bloqué après revive.

## Preuves natives trouvées
- `CSpellDBO::DecodeEffectCommand` @0x8380ad0 (timeout Ghidra →
  objdump) : table numéro→nom des commandes S_Enchant extraite —
  `EnchantCommands.cs` (164 entrées).
- S_Enchant : blocs de 7 cols — cmd id en col 9+7k, 6 vars en 10+7k.
- Potion réelle : item 20583 → enchant 53074 `1001|151|161` =
  « HP 151 161 » → heal uniforme (CEC_HP::Init range=|a2−a1|).
- `CEC_ItemTransportArea` @0x853e6xx : `1805|node|areaKey` →
  CEventRegenerate key = node<<16|areaKey.
- Formules PMS natives confirmées byte-par-byte
  (`Get*PMS` mission/decompiled.c) : kill 10000+, trésor 20000+,
  ItemGot 30000+, ItemGiven 40000+, Talk 50000+, NPCItem 60000+,
  AreaTravel 70000+, MultiTalk 90000+, ItemCollect clé tracker
  100000 (partagée, complétion via inventaire).

## Changements livrés
- `ZoneEntryFlow.Combat` : 305 ré-inversé `[victime][tueur]` (désasm
  ctor ClientDie), 306 flag=1, NCID 44 routé, désaggro au revive.
- `Handlers/ItemActionHandlers` : UpdateEquipment n'était qu'un ack —
  ajout 282 ClientUpdateEquipment + 283 ModelModify broadcast
  (désasm Serialize @0x82xxxxx) ; ItemUse appelle `ItemUseApply`.
- `ZoneEntryFlow.Items.cs` (nouveau) : `ApplyUseEnchantAsync` —
  HP/MP direct+% , ItemTransportArea → MigrateToNodeAsync,
  EffectNextID chaîne, EnchantCancel→300, Attr*/buffs → 299.
- `ZoneServices.ItemUseApply` : callback posé par le flow.
- `EnchantService.GetUseCommands` : parse blocs cmd+6 vars.
- `ClassRepository` : player_classlist partout ; switch de classe
  envoie attr6 = XP réelle persistée (plus 0).
- `QuestEngine` : ItemGot crédité au dialogue (give item + PMS
  30000+off), ItemCollect → 146 zone→mission + réévaluation
  CountItemAsync + émission tracker clé 100000 + FMS up/down,
  AreaTravel → 147.
- `ZoneEntryFlow.Migration` : log diagnostic event sans transport.

## À valider en jeu
- [ ] Tuer un mob → vivant ; revive → re-cliquer l'ennemi OK.
- [ ] Objectifs non-kill (collect/talk/deliver) → tracker bouge en
      live, bleu à complétion, sans relog.
- [ ] Potion HP/MP → barre remonte ; item téléport → change de map.
- [ ] Équiper/déplacer items → pas de freeze ; monture monte/descend.
- [ ] Changement de classe → barre XP classe non nulle.
- [ ] Portail S002 key 100 → node 100 ; clés 51-59 → instance quête.
- [ ] `[Regen] char=… mp=…` dans le log toutes les 5 s après un cast.

## Session 19 oct — CAUSE RACINE trouvée : DB morte sur les 2 services

### Le bug qui expliquait ~80% des symptômes
- **Zone** relancée avec `DB_CONN=Host=postgres` — `postgres` ne résout QUE
  dans le réseau docker. En natif : `getent hosts postgres` → échec →
  Npgsql jette à chaque requête → toutes les écritures (quêtes, shortcuts,
  vitals, inventaire, XP) mortes en silence dans les try/catch.
- **Mission** tournait SANS `DB_CONN` → fallback
  `Password=postgres` (mauvais) → idem : `_chars` vide, aucune évaluation
  d'objectif possible.
- Les 2 sont maintenant sur `Host=localhost` → eden_server-postgres-1
  (155 tables, la vraie base — `player_classlist` montre de l'XP réelle).
- `start-native.sh` écrit à la racine pour ne plus jamais reproduire
  (le `source .env` casse sur les ';' de la connexion).

### Mana/vitals — l'inversion cur/max
- Désasm des ctors `ClientUpdateAttr` natifs : attr **0**=HP courant
  (p1=low32, p2=i64), attr **1**=MP courant (p1, p2=0), attr **0x11**=MaxHP
  (p1=low32, p2=i64 max via GetMaxHP +0xe0), attr **0x12**=MaxMP (p1=max
  via GetMaxMP +0xe4, p2=0).
- On envoyait MP courant dans 0x12 → le "max" client descendait au cast,
  remontait à la regen/level-up — les symptômes exacts rapportés.
- `SendVitalHpAsync`/`SendVitalMpAsync` émettent les DEUX attrs natifs.

### MP figé à 69 (Onrush & co)
- `player_characters.max_mp` n'était JAMAIS réécrit après le lv1
  (`UpdateExpAsync` ne touche que exp/level ; SaveFullState ne portait
  pas les max). → MP courant clampé à l'ancien max → skills à coût>69
  impossibles (Onrush = 80 MP, col 27 tooltip `#27#` confirmé).
- Fix : `SaveFullStateAsync` écrit `max_hp`/`max_mp` ; à l'entrée, si le
  max DB < max recalculé S_Level → auto-heal + vitals pleines.

### Shortcuts par classe — décalage positionnel
- Le 510 natif est POSITIONNEL (pas d'id de classe par entrée) : le
  client mappe la i-ème entrée sur la i-ème classe de SA liste = l'ordre
  du 489. On n'émettait que les classes possédées → les barres
  atterrissaient sur la classe suivante. Aligné sur
  `AccessibleClassIds ∪ possédées` trié — même ordre que le 489.

### À retester live
- Quêtes non-kill (mission a maintenant sa DB) ; inventaire (écritures
  réelles) ; portails (log `[Zone-Entry] CheckNodeExit` guidera si un
  cas reste) ; Onrush (MP ≥80 maintenant) ; mana (cur stable, regen
  monte, max fixe).
