# Plan d'action temporaire — inventaire, shop, class skills, drops, exp, regen, HP

> Batch du 18/10/2025. Le combat est confirmé **100 % fonctionnel** côté client —
> ce plan NE touche PAS le combat sauf si un point l'exige explicitement.
> Chaque fix doit être prouvé par `re/ghidra/*/decompiled.c` ou une capture
> native avant implémentation (voir règle RE→code dans AGENTS.md / master plan).

## Tâches

### 1. Inventaire vide visuellement
Symptôme : aucun item affiché alors que le perso devrait avoir les armes de
base + un crystal (consommable tous les 5 niveaux).
Pistes :
- Le perso a-t-il les items en DB (`player_items`) mais le paquet d'inventaire
  (312 `ClientInventory` / 274 `ClientPutItemIntoInventory`) ne les envoie pas ?
- Ou les items starter n'ont jamais été créés à la création du perso ?
- Vérifier le mapping loc→slot envoyé dans le paquet vs ce que le client attend.

### 2. Achat NPC shop inopérant
Symptôme : le dialogue shop s'ouvre, le conseil de niveau s'affiche, mais le
clic droit d'achat ne débite pas l'or et ne donne pas l'item.
Piste : handler du NCID d'achat manquant ou mal dispatché ; vérifier le flux
natif `decompiled.c` (shop buy → vérifier NCID et format).

### 3. Class skills / general skills
Symptôme : seul le warrior reçoit sa 1re class skill.
Règles natives à implémenter :
- **General skills** : partagées par toutes les classes d'une même branche,
  débloquées selon le **niveau du joueur**.
- **Class skills** : propres à chaque classe, débloquées selon le **niveau de
  la classe**. Niveau de classe 1 sur les 5 premières classes ⇒ **1 class
  skill de base obligatoire**.
Preuves : `S_Spell` / tables de classe dans `server_us/Data/db_decoded`,
`decompiled.c` (octroi des sorts à l'entry/level-up).

### 4. Drops réels + UI centrale
Symptôme : `DistributeLootAsync` existe mais aucun drop visible ; l'UI centrale
de l'écran doit lister les items droppés au moment du kill.
Pistes : le notify 274 suffit-il ou faut-il un paquet "drop au sol"/"loot
obtenu" dédié ? Vérifier la capture native d'un kill avec drop.

### 5. Exp des mobs
Symptôme : kill = 0 exp. Implémenter l'octroi d'exp depuis `S_Monster`
(colonne exp) + le paquet de mise à jour exp/level (attrs).

### 6. Regen HP/MP
Symptôme : aucune regen. Règle : **5 % HP max et 5 % MP max toutes les 5 s**
(hors combat au moins ; vérifier le comportement natif en combat si prouvé).

### 7. HP niveau 1
Symptôme : 120 HP au lvl 1, attendu ~300+. La formule de stats de base
(`CCharFormula`/tables de classe) est probablement fausse ou les points de
stats de base ne sont pas appliqués.

## Mise à jour documentaire (après les fixes)
- Marquer le **combat comme 100 % fonctionnel** dans le master plan.
- `AGENTS.md` + master plan : **ne plus utiliser REA** ; utiliser les sources
  décompilées dans `re/ghidra/<binaire>/decompiled.c`.
- Documenter dans les docs **la méthode de décompilation** utilisée pour
  produire `re/` (Ghidra headless + script) afin de pouvoir décompiler tout
  binaire manquant de la même façon.
- Documenter l'obligation : **chaque tâche ⇒ un plan d'action temporaire dans
  `docs/temp-plans/`** avant implémentation, aligné sur le master plan.

## Résultats (18/10 — implémenté, Zone redéployée)

1. **Inventaire** ✅ — réponse native au NCID 25 = `312 + 280(attr
   0x3f0=1)` (le 490 `PlayerCard` est déjà émis par le replay de la
   requête 133 — capture : un seul 490). Le flag 0x3f0 = "inventaire
   prêt" ; sans lui le client ignorait le 312 → sac visuellement vide.
   `EntryHandlers.cs`.
2. **Shop** ✅ — cause racine : `SendEnterShopAsync` remplissait
   `st.OpenShopId` mais pas `session.CurrentStoreId` → `GetStore(0)` →
   échec silencieux. Synchronisé à l'ouverture + reset sur LeaveShop.
   Achat émet 274 (item au bon bag/slot) + 308 `ClientBuyUp` + 280
   attr 0x02 (gold) — `ShopHandlers.cs`, `ShopService.BuyItemAsync`.
3. **Class skills** ✅ — modèle natif `LoadAllCommonSpell` (mask
   S_Spell **col 23** = bit(classId-1)) ∪ vecteur `S_Class` cols 30-54
   filtré par `RequiredLevel` (S_Spell **col 22**, `CheckLevel`), dédup
   par code (chaînes J = paliers d'un même certificat → 1er id).
   `ClassSkillTable.GrantableSpells` — octroyé à l'entrée (classe
   active) et au changement de classe (168). Lv1 : Warrior {50038,
   50201}, Mage {50126,50226}, Hunter {50078,50214}, Cleric {50102,
   50106,50220}, Thief {50058,50207}. NB : les lignes S_Class 3-5
   n'étaient PAS tronquées — les descriptions Big5 contiennent des LF
   nus ; le join multi-lignes d'`IniTable` les restaure (305 lignes).
4. **Exp mobs** ✅ — `MonsterParser.ExpReward` = **col 18** (constante
   par palier : lv1→93/47, lv10→209, lv30→783) ; col 14 = grade, pas
   l'exp. `GrantKillExpAsync` dans `FinishMobDeathAsync` →
   `ProgressionService` (persiste) + **600** `ClientUpdateAttrNewExp`
   `[i32 char][i64 exp]` et **284** `ClientLevelUp`
   `[u32 char][u8][s16 lvl][i64 maxHp][i32 maxMp]` au level-up.
5. **Regen** ✅ — tick 5 s : hors combat HP+MP 5 %/tick, assis ×4,
   en combat MP seul ÷4 (scheduler `CCharacter` natif : combat →
   `RecoverMP(0.25)` seul). Attrs 0x11/0x12 self + 0 broadcast.
6. **HP lv1** ✅ — formule native `CCharFormula::MaxHP` : `S_Level.MaxHp
   (251) + CON_eff×(100/ConToHpMax=20)` → warrior lv1 ≈ 311 ; MP =
   `S_Level.MaxMp (195) + WIS_eff×(100/15)` ≈ 261. `CON_eff =
   lvlRow.Con × S_Class StatPercents[4]/100`. Appliquée à l'entrée en
   zone + level-up + création (INSERT était 1123/123 capturés d'un
   autre perso).
7. **Drops** — le 274 loot est émis après insert DB (bag/slot réels) ;
   à valider visuellement in-game (UI centrale).

Build : 0 erreur, 0 warning. Zone :5201 redéployée (liens ticket/
world/mission établis, 20427 items/12896 mobs/4485 sorts chargés).
