# Plan d'action — Bugs live client (session 20/10)

Bugs remontés en jeu sur la stack C#. Ordre de travail : regroupé par
domaine pour minimiser les recompiles. **Note : ce fichier fait office
de mémoire — chaque preuve native trouvée est consignée ici avec
adresses/fonctions.**

Sources de vérité : `re/ghidra/zone/decompiled.c` (serveur natif),
`re/ghidra/client_game/decompiled.c` (client référence — pour tout
bug d'affichage, c'est LE client à analyser), `re/ghidra/rf_game/`
(Reforged — même protocole).

Conventions : handles = sight-handles ; paquets vérifiés vs désasm ;
pas de branche Reforged côté serveur.

---

## 0. INFRA — CORRECTION MAJEURE (cause racine transverse)

**Constate** : Zone (pid dotnet run Debug) et World tournaient SANS
`DB_CONN` → fallback `Password=postgres` invalide → TOUTES les
requêtes DB des deux services échouaient : mission states (348 →
tracker vide = D1), amis (52 → F1), recherche (F2), inventaire,
shortcuts, vitals. Mission tournait sur un binaire Debug `(deleted)`.
**Fix** : `start-native.sh` réécrit — relance Mission+World+Zone
depuis Release avec DB_CONN/ACCOUNT_DB_CONN/MEMBER_DB_CONN/DATA_DIR.
**À re-tester live** : tracker quêtes, amis, recherche, persistance.

---

## A. COMBAT — feedback & sorts

### A1. Sorts de soin KO — aucun soin appliqué [CORRIGÉ — commit en cours]
**Symptôme** : les heals ne soignent pas, pas de nombre vert.
**Attendu natif** : hit de soin affiché en VERT ; un soin peut être
critique → nombre vert + visuel critique.
**CAUSE RACINE** : le mapping `S_Spell` était faux. La désérialisation
native `CSpellQuery::Deserialize` (header `|V.8|45|`, 45 cellules
séquentielles) prouve :
- col17 → `SSpellData+0x08` = **famille** (PAS le type)
- col18 → `SSpellData+0x0c` = **type dispatch** (`OnHitLife` lit +0xc)
- col33 → `+0xbe` élément ; col35 → `+0xc0` power ; col36 → `+0xc2`
  heal power ; col37 → `+0xc4` variance.
Les heals cleric ont col17=6 (famille) et col18=4 → l'ancien code les
lisait Type=6 → canal magique, DamagePower=0 → 0.
**Preuves** : `OnHitLife` @0x84f2080 (dispatch +0xc → type∈{4,5} →
`HitRestoreSkill` @0x84f1f40 → `CalculateMagicSkillHeal` @0x83f9310) ;
flags 281 : `0x200` = restore (vert), `|0x200000` = crit heal ;
formule `heal = ROUND(power×coef/100) × (100+2r−var)/100`, coef =
S_SpellReference col4 (ref+0x10) ; mods Heal()/BeHeal() ≈ identité.
**Implémenté** :
- [x] `SpellParser` : col18→Type/TargetType, col17→Family, col35→
      DamagePower, col36→HealPower, col37→DamageVariance, col29→
      Duration (était col33=élément, erroné).
- [x] `SpellReferenceParser.LevelCost` : + `HealCoef` (col4 = ref+0x10).
- [x] `NativeCombatFormulas` : `ResolveSpellHeal` (formule native +
      roll crit magique ×MCritDmg), `SpellHitFlags` (Hit=1, Miss=8,
      Block=0x10, Restore=0x200, Crit=0x200000), `SpellHitFlagsOf`,
      `SpellHitKindOf` dispatch corrigé (type col18 ∈{4,5} → Restore).
- [x] `ZoneEntryFlow.Combat` : `SendSpellHitAsync` accepte les flags ;
      voie heal dans `ExecuteSpellOnLifeAsync` (HP capé max, 281 au
      lanceur + cible + broadcast can-see, vitals 280/HP) ; cast
      joueur→mob type4/5 soigne le mob (cas rare mais natif).
- [x] `PlayerHandlers` : gate d'apprentissage sur `Family` (col17)
      au lieu de `Type` — les heals cleric (Family=6) gardent leur
      gate de classe.
- [x] Tests : `SpellParser_NativeColumnMapping` (données réelles),
      `SpellHitKind_RealSpells_DispatchCorrectly`,
      `ResolveSpellHeal_ZeroVariance_IsBaseOrCrit`,
      `ResolveSpellHeal_Variance15_StaysInNativeBand`,
      `ResolveSpellHeal_NoPower_ReturnsZero`,
      `SpellHitFlags_MatchNativeConstants` — 29/29 verts.

### A2. Sorts à cast time → 0 dégât [CORRIGÉ — même cause]
**Cause** : même bug de mapping — les sorts d'attaque à cast
(Earth Shock 50026 : col17=4 famille, col18=1 type) étaient lus
Type=4 → routés Restore → `ResolveSpell` retournait 0.
Corrigé par le même lot (col18 = type réel → Magic via elem7).

### A3. (lié) ordre d'application des stats — voir §C

---

## B. INVENTAIRE / ITEMS

### B1. Quantités affichées fausses (stack toujours 1) + pas de cooldown potion ET consommables a temps de cooldown (monture, star stone et autre)
**Symptôme** : plusieurs potions → affiche 1 ; aucun CD.
**À faire** :
- [x] `inventory1.durability` = count natif (confirmé DBO
      "Update Item Durability" + `update_quest_item`) ; le 312 écrit
      déjà f1=Durability → l'affichage 1 était probablement causé par
      l'ordre fautif du 274 (items arrivés avec dur=0 côté client).
      À revérifier en live après fix 274.
- [x] Cooldown potion [IMPLÉMENTÉ + COLONNES CORRIGÉES] — mapping
      natif re-vérifié par comptage cellule→offset dans
      `CItemQuery::Deserialize` : **col21 → CItemData+0x58 = durée en
      DIXIÈMES de seconde** (le ctor `CEventCoolDownItemGroup`
      @0x8663ac0 fait `v/10` s + reste µs ; valeurs réelles 10/100/
      18000 = 1 s/10 s/30 min) et **col22 → +0x5c = groupe s16**
      (UseItemControl::Init @0x84eb2c0 : control+8=+0x58,
      control+0xc=+0x5c). Le code lisait 19/20 → durée 0 ou groupe
      fantaisiste. Renommé `CooldownSeconds`→`CooldownDs`, `Arm()`
      prend des ds (`AddMilliseconds(ds*100)`).
      Gate natif : use-type ∈ {1,2,3,5,6} uniquement.
      `ItemCooldownService` = map natif char+0x6a8 ; refus → texte
      0x5ef ; armement → 368 ClientUpdateCoolDown
      `[u8 n]{u8 0|s16 group|u16 itemId|i32 /10s}` (Serialize
      @0x8237d30 — la classe générée était mal formée, corps brut).
      Tests ItemCooldownTests mis à jour en ds (4/4 verts).
      À tester live : grisage potion + blocage spam.
**Preuves** : voir B2.

### B2. Achat PNJ : or débité, item invisible (inventaire pas mis a jour visuellement)
**Symptôme** : `BuyItems` débite l'or mais l'item n'apparaît pas.
**Preuves** (désasm `CItemFactory::PutIntoContainer` @0x809ea00) :
- Le 274 `ClientPutItemIntoInventory` est construit :
  `msg+0x04`=u16 itemId, **`msg+0x06`=s16 container_index (bag)**,
  **`msg+0x08`=s16 loc (slot)** (`local_20` passé à
  `IItemContainer::PutIn` + `CItemDBO::PutItemInInventory`),
  **`msg+0x0a`=s16 clone+0xc durability**, **`msg+0x0c`=s16
  clone+0xe cur_maxdurability**, puis str maker / u8 flags / mask.
- Callers natifs : `PutIntoContainer(…, param_4=-1, param_5=count,…)`
  → `param_5==-1 → clone+0xc=clone+0xe` (équipement plein) ;
  pour stacks param_5=count → durability=count.
- **BUG** : `NativeItemCodec.BuildPutItem` écrivait
  `[id][qty][qty][bag][slot]` → le client lisait bag=qty, dur=0 →
  item invisible. Ordre natif : `[id][bag][slot][dur][maxdur]`.
- [x] `BuildPutItem` réécrit : `[u16 id][s16 bag][s16 slot][s16 dur]
      [s16 maxdur][u16 slen][u8 flags][u24 mask][u32 create_time]
      [34B]` + param `createTime` (bit8 = `clone+0x18` réel, pas "now")
      + overload `BuildPutItem(ItemEntity)` dérivant tout de l'entité
      (durability=count stack, IsBound→0x48, sockets/combos via meta).
- [x] 11 call sites migrés : shop buy/buyback, loot, quest-drop,
      GiveQuestItem, mail-attach (maxDur relais @+26), stall-vente,
      itemmall, works ×2, lottery, lifeskill, divide, storage-withdraw
      ×2 — tous relecture `GetItemAsync` post-insert.
- [x] Tests régression byte-level : `BuildPutItem_NativeFieldOrder` +
      `BuildPutItem_EntityOverloadCarriesStackCount` (6/6 verts).

### B3. Montures non équipables [CORRIGÉ — commit en cours]
**Analyse** : les montures NE SONT PAS des équipements — kind 44,
loc vide, UseType=2, enchant `2134|rideKey|flag|seats`
(CEC_Ride). Le clic droit toggle le ride.
**CAUSE RACINE** : le visuel de monture natif vient de
`CNC_CZ_ClientModelModify` (283), pas de l'enchant seul —
`CEC_Ride::Execute` → `SetState(rideKey)` @0x854f2xx :
`char+0x7f0=rideKey`, `+0x4dc`++ → 283 mask=2 +
NotifyClient + BroadCastToCanSee. On n'envoyait que 299.
**Preuves** : `ClientModelModify::Serialize` @0x813c230 :
`[i32 handle][u8 ver][i32 mask][u16 n][n×u32][u16 m][m×u32]` ;
ctor 9 args @0x813f2c0 : mask=2 → liste `[rideKey(char+0x7f0),
flag(char+0x4ad>>1&1)]`, vecteur = passagers (`char+0x7fc`,
[0]=conducteur, taille=seats). `Init` @0x854f960 :
arg1=rideKey, arg2=flag(&1→0x4ad bit1), arg3=sièges (min 1).
Démontage → rideKey=0, flag=0, vecteur vide.
**Implémenté** :
- [x] `RideInfo` + `Flag`/`Seats` (args 2/3 de la commande 2134).
- [x] `ZoneServices.ModelVersions` — compteur char+0x4dc.
- [x] `SendRideModelAsync` : 283 mask=2 `[handle][ver][2][2]
      [rideKey][flag][seats][handle…]` + broadcast node, montage
      ET démontage (rideKey=0, vecteur vide).
- [ ] À tester live : right-click monture → modèle r{key} visible.

### B4. Objectif "collecter" : 0/1 + item absent de l'inventaire, vérifier tous les autres objectif
**Symptôme** : item de quête collecté → compteur reste 0/1, item
n'apparaît pas.
**Preuves + implémentation** :
- [x] **Cause 1 — comptage DB** : `MissionDb.CountItemAsync` faisait
      `COUNT(*)` de lignes → un stack de 5 potions comptait 1. Corrigé
      pour matcher `IItemContainer::CheckItemNum` natif : somme des
      `durability` (count) pour les items stackables (`S_Item`
      max_stack>1), 1 par ligne sinon. Callers mis à jour
      (`If_InventoryItem`, `AllItemsDoneAsync`, validation invitation
      mariage — tous passent désormais itemId + quantité requise).
- [x] **Cause 2 — item invisible** : corrigé par B2 (ordre 274 natif).
- [x] **Cause 3 — notification 146 manquante** : `update_quest_item`
      natif déclenché à chaque insert/delete du miroir d'inventaire.
      Ajouté `ZoneServices.NotifyItemChanged(charId, itemId)` →
      146 zone→mission, branché sur TOUS les sites de mutation :
      achat/vente/rachat shop, enchères (mise en vente + annulation),
      jeter/détruire (DropItem), dépôt/retrait stockage perso ET
      banque de famille, consommables (déjà fait), loot mob
      (CombatHandlers + 274 ajouté), trade (callback
      `OnInventoryChanged` de TradeService → réserve/restitution/
      échange), craft métier (LifeSkill), produits work/combine,
      cube (ingrédients + lucky consommés), magic box (sacrifices),
      renforcement (matériau + chance + item détruit), quêtes (déjà),
      lottery (déjà via NotifyItemAddedAsync), item mall (livraison).
- [ ] À tester live : collecter un item de quête → compteur n/N.
- [x] Types d'objectif : collect, talk-to, escort, use-item, explore —
      couverts par QuestEngine (kills/collecte/talk/livraison/travel) ;
      le blocage était la DB (fix infra). Comptage collect corrigé pour
      matcher CheckItemNum natif (count stack vs 1/slot).

---

## C. PROFIL / STATS — refonte complète

### C1. Interface profil = stats fake + types d'armure faux
**Symptôme** : stats jamais mises à jour à l'équipement ; slots
"cloth" affichent "heavy armor".
**Verdict user** : refaire entièrement le code de cette interface.
**À faire** :
- [x] NCID 296/492/493 — **ordre natif corrigé** : désasm
      `CCharacter::NotifyRefreshAttr` @0x8415210 = ClientUpdateAttr
      vitals → **296 AttrRefresh (totales) → 492 BaseAttrRefresh →
      493 AttrLevelRefresh** → UpdateAttr. Notre émetteur envoyait
      492→493→296 (inversé) → réordonné dans `ProfileStatEmitter`.
- [x] **Bug généré NCID 280** `ClientUpdateAttr` : le natif écrit
      `[i32][s16][i32][i64]` = 18B (Serialize @0x804fb90), la classe
      générée écrivait Field4 en i32 = 14B → corps tronqué pour tous
      les callers de la classe typée. Field4 passé en `long`.
- [x] Types d'armure affichés — **cause racine trouvée et corrigée** :
      NCID 311 `ClientEnterNodeEquipAndSpell` porte l'équipement par
      conteneur EClassEquipType — le replay envoyait l'équipement de
      TEST3 (ses 10 items), donc le panneau affichait les types du
      perso capturé. Corps entièrement reconstruit depuis la vue
      `equipment` (`container_index` = clé de conteneur 0..5, `loc` =
      bit du mask u32, records WriteItem) + sorts `player_spells`.
      Format désassemblé `Serialize` + capture décodée octet par octet
      (masks 0xb4/0x34/0x34 reproduits). Tests
      `EnterNodeEquipSpellTests` 3/3.
- [ ] Le client cache peut-être les stats jusqu'à un invalidate —
      vérifier dans client_game ce qui déclenche le refresh du panneau.
      Note : le natif envoie aussi les UpdateAttr vitals (280 attr
      0/1/0x11/0x12) autour du refresh — déjà émis par nos callers
      (`SendVitalHp/MpAsync`).
- [x] **Ordre d'application des stats** (demande explicite) :
      `StatPipeline` ordonné dans Eden.Core/Game/Stats — étapes nommées
      Base → Equipment → Permanent/Titre → Costume → Temporaire,
      composables ; intégré dans ProfileStatEmitter (296/492/493).
**Preuves** : (à remplir)

---

## D. QUÊTES

### D1. Tracker de quête (panneau droit) disparu
**Déjà signalé, jamais débugué.** 
**Piste** : le client affiche le tracker si un paquet liste les quêtes
suivies — probablement au login ou à l'accept de quête. Chercher dans
client_game le NCID qui peuple le tracker (souvent un 4xx "quest
symbol/notify" ou l'état mission relaié ZM).
- [x] Quel NCID pousse l'objectif tracker ? — le 391 ZM relayé porte
      `[u8 kind][u32 key][u16 value][s16 extra]` (9B) — décodage
      vérifié dans mission/decompiled.c ; la Zone relaie tel quel.
      La vraie cause était la DB (fix infra `26b0661`).
**Preuves** : (à remplir)

### D2. Icône de choix NPC : "talk" au lieu de "!" quête [CORRIGÉ]
**Symptôme** : quête à rendre → choix affiché avec icône talk.
**Preuve native** : dans `CGlobalInfo::GetQueryDialogue` (mission
@0x808bbe0+), le flag s16 F[i][j] poussé au client =
`ReplyDialog._2_2_` = **`GetFMS(exec, mid)`** — le FMS brut
(mission decompiled.c @0x81ac2xx : `ReplyDialog._2_2_ = GetFMS`).
Le client choisit l'icône du choix sur ce flag (0=« ! », 3=en cours,
4=à rendre, 6=reprenable).
**Fix** : `QuestEngine.OnListDialogueAsync` envoie le FMS réel
(open→FMS 0/6, reward→4, en cours→3) au lieu de 0.
**À tester live** : icône « ! » sur quête dispo, « ? » sur rendable.

### D3. Objectifs autres que "defeat mob" [IMPLÉMENTÉ — à tester]
Couverture existante vérifiée (QuestEngine) :
- MonsterKilled → OnMobDieAsync (clé mid×1e6+10000+off)
- MonsterTreasureGot → OnQuestItemGotAsync (+20000)
- ItemGot/ItemGiven/NPCTalk/NPCItem/NPCMultiTalk →
  CreditNpcObjectivesAsync à la SÉLECTION du choix (128)
- ItemCollect → OnQuestItemChangedAsync : comptage inventaire live
  (stacks comptés, fix B4), notify zone → mission NCID 144
- AreaTravel → OnAreaTravelAsync/OnCheckTriggerAsync (+70000)
À tester live : compteur « collecter n/N », talk-to intermédiaire,
livraison, aires.

---

## E. AGGRO / ÉTAT DE COMBAT — refonte [IMPLÉMENTÉ — à tester]

**Preuves natives** :
- `CAI::CheckOutOfGuardArea` @0x84d4b20 : leash = distance
  **cible ↔ home-d'aggro** (`CAI+0x44/0x48`) > max_hunt_range
  (= 0.5 × formula_para[300], DAT_087cc688=0.5 lu en .rodata —
  valeur du paramètre absente du schéma, ~30u retenu HYPOTHESIS).
- Home d'aggro = **position du mob au premier contact**, pas le spawn
  (AI msg 0x6a @0x84d7xxx : si home==spawn → home=pos courante).
- Le check est sauté si node type == 4 (`entity+0x324→+0x12` —
  = `NodeDef.Type` col3 de S_Node : 1=normal, ≥4=instance).
- MonsterType = S_Monster col6 (CEC_MonsterTypeCheck) : 1 normal,
  2 élite/mini-boss, 3 boss — distribution : 1×6960, 2×1196, 3×1444.

**Implémenté** :
- `MonsterDef.MonsterType`/`IsEliteOrBoss` ; `LiveMob.AggroX/Y` +
  `LastDamageDealtAt` ; `BeginMobCombat` unifié (4 sites).
- Map normale : leash joueur↔point-d'aggro >30u OU 10 s sans
  activité combat (max(hit reçu, coup porté) — un tank qui n'attaque
  pas mais encaisse ne fait plus fuir le mob).
- Instance non-élite : AUCUN leash distance ; deaggro seulement si
  le mob n'a pas touché le joueur pendant 3 min.
- Élite/boss : leash partout, y compris en instance.
- Sortie de boucle (deaggro ou mort joueur) → `ReturnHomeAsync` :
  marche retour au spawn (326 ×6u/1.6s) + heal complet à l'arrivée.
- Restant : règles PvP (duel/TW 20 s) — pas de système PvP existant.

---

## F. SOCIAL

### F1. Add ami : pas visible en live dans la liste
**Causes trouvées** :
- `ServerQueryFriendInfo` répondait `78 + 0000` — **faux** : 78 =
  `ClientUpdateSpecialCoin` (champs vides). La vraie liste =
  **NCID 52 `CNC_CW_ClientUpdateFriendMember`**.
- `ServerAddFriend`/`FriendInvitation`/`RemoveFriend` n'émettaient
  **aucun paquet** — invitation jamais livrée, listes jamais
  rafraîchies.
**Preuves natives** (mission `CGlobalInfo::QueryFriendData` →
`CNC_MW_WorldServerUpdateFriendMember` → relais world) :
- 52 = `[u16 worldServerId][s16 count][records]`, **chunké à 10**.
- Record : `[i32 charId][str name][s16 type][s16 level][s16 node_id]
  [s16 race_id][s16 class_id][s16 class_lv][str familyName]
  [u8 online][u16 serverId][u8 isPublic]` (exec+0x5c=level,
  +0x30=node_id, +0x3a=race_id, +0x38=class_id, +0x68=class_lv,
  +0xb0=family_id→nom, +6=serverId, +0x120+0x18=card.is_public ;
  −1/""/0 quand non-mutuel ou carte absente).
- online = byte recalculé par le monde (1 connecté / 0 sinon).
- friend_type natif : 0=blacklist 1=ajouté 2=m'a-ajouté 3=mutuel ;
  notre schéma écrit toujours les deux sens → type **3** pour tous.
- [x] 53 `ClientFriendInvitation [i32 fromId][str name]` à la cible
      à l'invitation ; 52 aux deux à l'accept + remove + accept
      réciproque ; 52 aux amis en ligne au login (badge online).
      `Eden.World/Handlers/FriendHandlers.cs` +
      `Eden.Core/Game/Social/SocialPackets.cs` +
      `CharacterRepository.GetPlayerCardsAsync` (batch, join guilds).
      À tester live : ajouter un ami → la popup 53 arrive → accept →
      les deux listes affichent l'ami.

### F2. Search channel : doit lister TOUS les joueurs du channel (soi inclus)
**Preuves natives** (`TC_SearchCondition` mission +
`CNE_ZMis_ZoneServerSearchInfo::Execute` zone) :
- La recherche **exclut le demandeur** (`id != requester`) — le soi
  n'apparaît PAS nativement ; le client l'ajoute probablement côté UI.
- Filtres : level ∈ [lower,upper] ; option bit0→flag LFG
  (exec+0xe3), bit1→card.is_public ; `selectClass` = string-bitmask
  (`classId <= len && (==len || str[classId] != '0')`) ; blacklist
  mutuelle exclue ; cap 50 ; `exec+0xe2 != 0` requis (opt visible,
  posé par `SearchOpt` [s16 v1→e3][s16 v2→e2]).
- Réponse : **411 `CNC_CZ_ClientSearchInfo`** `[s16 0][s16 n]
  [n × [i32 charId][str name][s16 level][u16 class_id][s16 class_lv]
  [s16 node_id][u8 is_public]]` — header toujours 0.
- [x] `SearchOpt` : flags par session sur `NetSession`
      (`SearchVisible` défaut 1, `SearchLfg` défaut 0 — le natif
      exige e2≠0 ; sans lui personne n'apparaît).
- [x] `SearchCondition` : itère `s.Sessions` (hors demandeur,
      SearchVisible requis), charge les cartes en batch, applique
      level/selectClass/option-LFG, cap 50, émet 411.
      NB : le natif EXCLUT le demandeur (`id != requester`) — si le
      client attend de se voir, c'est côté UI.

---

## G. NPC / MONDE

### G1. NPC disparu au retour en instance — FIX STRUCTUREL
**Symptôme** : s'éloigner d'un NPC en instance → il disparaît ; retour
→ absent ; portal-stone de retour le fait réapparaître.
**Causes corrigées** :
- [x] **Énumération non-snapshot** : `UpdateSightAsync` faisait
      `foreach` sur la `List<SeenEntity>` du node pendant que les
      events la mutent (`SpawnNpc`/`DespawnNpcs`/`DespawnGroup`/
      `SpawnGroup` → Add/RemoveAll) → `InvalidOperationException`
      possible avortant TOUTE la passe → aucun ré-envoi des 298
      jusqu'au prochain `force:true` (téléport). Fix : snapshot
      `ToArray()` par passe + `_nodesLock` dans ZoneLiveWorld sur
      toutes les mutations.
- [x] **Handles orphelins** : une entité retirée du node
      (Event_KillNpc, fin d'event) n'apparaissait plus dans la liste
      → son `st.Sent[handle]` n'était jamais nettoyé → client gardait
      un fantôme + handle polluant. Fix : purge — tout `st.Sent` non
      présent dans le snapshot ni dans `st.Replayed` → 269 +
      nettoyage.
- [ ] À tester live : s'éloigner/revenir d'un NPC d'instance → il
      réapparaît sans portal stone.

---

## H. RACCOURCIS DE CLASSE — RÉSOLU

**Symptôme** : changement de classe → toujours le shortcut warrior.
**Preuve native** : `CNE_DBA_ClientShortcut::Execute` @0x82f80xx —
le 510 est un RELAIS du DBA qui charge `shortcut_menu` de la classe
ACTIVE dans `char+0x684` = `vector<vector<int>>` **indexé par PAGE**
(pas par classe !). Le Serialize @0x82023a0 écrit par entrée :
u16 mask avec **bit(i) = slot i** (PAS de sentinelle — ≠ 351/352
qui ont bit0=sentinelle + bit i+1).
**Bug** : `SendClassShortcutsAsync` émettait une entrée par CLASSE
(union des classes possédées) → le client lisait l'entrée 0 comme
page 0 = la barre warrior, sur toutes les classes.
**Fix** :
- [x] `SocialRepository.GetClassShortcutPagesAsync(charId, classId)`
      — pages d'UNE classe (page → int[10]).
- [x] `SendClassShortcutsAsync` : pages 0..max de la classe courante
      (st.ClassId), mask bit(i) par slot.
- [x] `CNC_CZ_ClientChangeClassShortcut` généré : mask corrigé
      (bit i, pas sentinelle) — Serialize+Deserialize.
- [x] Handler `CNE_CZ_ZoneServerChangeClass` dupliqué dans
      SocialHandlers supprimé (code mort — NCID 168 consommé par
      ZoneEntryFlow.TryHandleAsync).
- [x] 352 ReceiveShortcutOfPage : vérifié — mask sentinelle bit0 +
      bit(i+1) correct (convention ≠ 510 confirmée par désasm).
- [ ] À tester live : switch warrior→autre classe → barres de la
      bonne classe.

---

## I. ITEM MALL / ADMIN

### I1. Item mall vide → seed catalogue exemple [RÉSOLU — déjà seedé]
- [x] Table `itemmall` (fnaccount) : 498 lignes sell=1 → **472
      entrées validées** au boot du monde ("ItemMall : 472 entrées
      de catalogue chargées"). Le catalogue existait déjà — le
      symptôme "mall vide" venait probablement du monde lancé sans
      DB_CONN (fix infra). À tester live : ouverture boutique.

### I2. Commandes admin/GM nécessaires [IMPLÉMENTÉ]
- [x] `//clone_item <id>` / `//clone_items <id> <n>` /
      `//clone_item_to` — AddItemAsync + 274 + texte 0x89 + notify.
- [x] `//gain_gold <n>` — AddForeverGoldAsync + 280 attr 0x02.
- [x] `//gain_aa_point <n>` (AP=pvalues) + `//gain_ep_point <n>`
      (EP=bonus, ajout maison) → tb_user via UserRepository +
      MEMBER_DB_CONN zone.
- [x] `//gain_exp`, `//set_level`, `//gain_skill_point` — stats de
      test du profil (refresh 296 émis).
- [ ] (plus tard) commandes guilde : level, bâtiments
- [x] GmCommandService : handlers async + Context(charId, session,
      token) ; réponse renvoyée en ChatMessage [GM] au demandeur.

---

## J. STATS — ordre d'application (demande user)

Pipeline de stats à rendre ordonné et modifiable :
1. Base : S_Level × S_Class
2. Équipement (armes/armures)
3. Titres d'honneur / buffs permanents
4. Costumes
5. Buffs temporaires / procs
- [x] Ordre natif vérifié : `RefreshEQBonus` @0x8408850 accumule les
      CItemAttr de chaque pièce dans char+0x38c.. (canal "pre" de la
      formule `TotalX = (eq+lvl)×(growth+classPct)/100 + flat`).
- [x] `StatPipeline` (Eden.Core/Game/Stats) : contributions nommées
      3 canaux (pre/pct/post) — étapes enregistrées dans
      `s.StatPipeline`, équipement versé par ProfileStatEmitter, les
      totaux du 296 passent par `ResolveStat`. Étapes
      permanents/costumes/buffs : points d'extension prêts, données
      sources pas encore câblées (titres/buffs actifs non modélisés).

---

## Séquencement proposé
1. B (inventaire — quantités/achat/monture/collect : probablement un
   seul codec fautif)
2. A (combat feedback — heals + cast dmg)
3. C (profil — gros morceau)
4. D (quêtes UI)
5. E (aggro/combat state)
6. H (shortcuts)
7. F (social)
8. G (NPC despawn)
9. I (item mall + GM)
10. J (ordre stats — probablement fusionné avec C)
