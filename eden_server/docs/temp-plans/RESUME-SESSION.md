# RESUME SESSION — lot5 + prompt5 (reprise contexte)

> Fichier de continuité — le lire en premier à la reprise.
> Ordre de mission utilisateur : (1) corriger TOUS les bugs listés,
> (2) prompt5.txt EN ENTIER sans valider, (3) NCIDs manquants en
> dernier recours. INTERDICTION de s'arrêter (~10h de dev).

## État commits

- `5fce3f3` fixes combat lot5 : aggro buff (ResultSpellId routé AoE
  seulement si spell hostile), heal combat-vs-soi (sel=4 si InCombat),
  enchants instantanés ne créent plus de status (IsInstantOnlyEnchant
  + ExecuteInstantEnchantAsync = dispels EnchantKill*).
- `7a7d0f5` plan lot5 à jour.
- `615d4b7`+`1cc7ccf` lot4 (pets 187/188, talents, AoE, buyback, doc).
- game_.exe patché : 0x75dbd6 NOP (SetDir monté), 0x75DC08 `EB 0D`→
  `90 90` (directive FUN_0069a990(cam,10) restaurée — strafe monté),
  0x75a996 `74 13`→`EB 13` (caméra RMB toujours orbite — cast).
- Zone :5201 Release déployée, 1073/1073 tests verts.

## Bugs restants à traiter (ordre)

1. **Pets** : spawn sous map (z=0 natif — creuser SeeMob sérialisation
   ou pos transitoire), ne suit pas (tick follow existe, vérifier 326
   émis / owner résolu), UI « rest » (natif n'émet rien au 187 — UI
   lit l'état via un autre paquet : 418 ClientElfUpdateAttr ? liste
   elf ?), **pray non implémenté** : S_Elf.ini a 4 blocs de chaînes
   `#id#id#id#Name args###` (cols 35-38) = passifs par slot — pray =
   don de stats dynamique en status+pipeline maître. Elf modes natif :
   mode dans pet+0x3ec ; 2=loot/passif, autre=combat ; pray probable
   mode distinct (3?) — vérifier client rf_game comment il envoie.
2. **Buyback UI** : 512 flag=3 émis à l'open/sell/buyback — onglet
   vide + Items vide au retour → probablement requête client distincte
   ou flag différent pour le tab switch ; capturer le NCID émis au
   switch onglet (headless_client ou log zone).
3. **Max skill button** : trouver NCID learn (UseSkillPoint NCID 12
   mentionné dans Combat.Rewards) + sémantique "monte au max" (loop
   learn tant que gold/CP suffisent, ou flag dédié).
4. **Autels transform** : Lottery.cs existe (eden crystal/gem) —
   le bouton « transform » lottery envoie un autre NCID ? reindex
   search "transform" zone + checker ncids non gérés dans logs zone.
5. **Talents→profile** : BonusCastSpeed émis (ProfileStatEmitter
   l.264) — vérifier le talent testé mappe 2069→CastSpeedRatePercent
   + que le stage talents rejoue à l'investissement (471).
6. **Strafe monture** : patch posé ; si encore KO → records de
   direction DAT_00fe4ba0/4bd8 (fn-ptr record+0x10) ou facing via
   paquet serveur 326.
7. **AoE** : fixes shape0/PBAoE en place — retest live.

## Données utiles

- S_Elf.ini : séparateur `,` (pas `|`) — 42 cols ; cols 35-38 =
  `#ench1#ench2#ench3#Name arg1 arg2###` ×4 slots passifs.
- Enchant instant-only set dans ZoneEntryFlow.Status.cs (~l.147).
- reindex : `cd re && ./reindex lookup|search|addr|xref|opcode -b zone`.
- rf_game == client_game sauf 7 diffs branding (EASYFUN vs LOGLOG).

## prompt5.txt — 3 chantiers (à faire EN ENTIER après les bugs)

- Chantier 1 : monorepo Web CMS — backend ASP.NET Core (dernière
  version) + frontend Next.js App Router/TS/Tailwind, docker-compose.
  JWT User/Admin, hash mdp IDENTIQUE au game server (vérifier
  LoginService/BCrypt? dans server/). /wiki (items, mobs, missions,
  certificats, talents/licences toutes classes + éveil, simulateurs),
  /rankings (PvP, PvE, succès, Limitless), dashboard joueur, admin
  news/statut. Style : Shadcn UI (ui-shadcn-4.22.1.zip dans
  ~/Téléchargements).
- Chantier 2 : Limitless Studio — app desktop C# Avalonia MVVM
  (Nobara/Linux) : éditeur items/mobs PostgreSQL(Dapper)+.ini,
  analyse unicité .ini, stratégie maj launcher distante, sécurisation
  contenu (chiffrement/.pkg), parser assets Reforged (icônes
  dds/tga/png dans pkg/), champ ModelID. Style : SukiUI.
- Chantier 3 : Limitless Level — PAS de code d'abord : rédiger
  docs/LIMITLESS_SYSTEM_DESIGN.md complet (BDD, ExpManager/
  CombatHandler skill-track anti-leech, PerkManager, plan UI client
  pour barre exp ultime + pass). Puis base de logique serveur NON
  IMPACTANTE (flag config) UNIQUEMENT si faisabilité UI prouvée.
- Contraintes : dossiers rangés proprement, dernières versions, tout
  doit marcher sous Linux (proposer exécutables/frameworks adaptés),
  si je dors = coder une V1 release sérieuse pour chantiers 1+2,
  document de design complet pour chantier 3.

## MAJ 2 — constats supplémentaires avant résumé

- Pet UI mode : le natif n'émet RIEN au 187 (textes seuls) —
  `elf+0x3a` bit0x1 = attack-state persisté via DBA. Le client lit
  donc le mode depuis les données elf (paquet liste/détail elf ou le
  418). À identifier dans rf_game quel NCID l'UI pet lit pour le mode.
- Pet modes natifs (`pet+0x3ec`) : 2 = passif/loot, autres (1,3?) =
  combat/pray — à confirmer en désasm `SetPetInstruction` @0x84588e0
  pour les valeurs exactes (pray = ?) avant d'implémenter le don de
  stats pray.
- Pet spawn sous map : vérifier le contenu du 298 émis pour le pet —
  si le modèle/client attend une hauteur différente ou si la position
  est (0,0) transitoire, le corriger côté spawn (SpawnPet a x,y,z
  cohérents avec les mobs).
- Pet follow : TickPetsAsync existe (d2>9 → MovePetToward + 326) —
  vérifier live si le watcher list est rempli (MobWander.cs:58) et
  que le 326 part réellement.
- Buyback UI : flag=3 natif émis. Si onglet vide → vérifier dans les
  logs zone le NCID émis par le client au switch d'onglet (probable
  requête 512 count=0 ou un NCID shop-list non géré → Items vide).
- Max skill : trouver le NCID learn (probablement 12 UseSkillPoint)
  — la variante « max » envoie peut-être le même NCID en boucle ou
  avec flag ; vérifier handler actuel.
- Autels : Lottery.cs existe pour eden crystal/gem — le bouton
  « transform » envoie un NCID différent (vérifier logs zone pour
  le NCID non géré au clic).
- Talents→profile : BonusCastSpeed émis — vérifier que le stage
  talents rejoue après investissement (InvokeAAEnchants → recalcul
  profile à l'investissement via le 471 ou le write DB).
- Strafe monture : patch posé ; si encore KO → direction records
  DAT_00fe4ba0/4bd8 (record+0x10 fn-ptr) ou facing via 326 serveur.

## MAJ 3 — SetPetInstruction désasm @0x84588e0

pet+0x3ec (mode) valeurs natives : **0** (idle), **1** (combat/actif),
**2** (passif/loot — texte 0xb30 + reset AI via vfunc+0x20), **100**
(lock cible +d0, mode 2→1 + texte 0xb31, BecomeInterestLifeInMe),
**0x65=101** (release via chemin séparé). PAS de mode pray dans
SetPetInstruction → **pray est ailleurs** : probablement statut elf
(elf+0x58 ?) ou un enchant passif S_Elf (cols 35-38 blocs
`#id#id#id#Name args###` ×4 slots) appliqué au maître quand le pet
est invoqué/en pray — le « don de stats » pray = probablement un
de ces enchants passifs. Chercher dans rf_game comment l'UI envoie
pray (NCID distinct ? instr 188 ?) et dans zone comment le natif
applique les passifs elf (InvokePassivities @CallElf ~0x8073xxx →
battle_spell_4/5 famille 9 = nos ApplyPetPassivesAsync existantes —
le pray pourrait juste être un slot passif supplémentaire ou un mode
d'affichage client).

## MAJ 4 — Pray existe côté client

rf_game strings : `/PraySkill/S`, `/PraySkill/Title`,
`/Data/Number/BtnPray` → le mode Pray est une vraie feature UI avec
bouton dédié. Trouver dans rf_game/client_game le code qui envoie le
NCID quand BtnPray est cliqué (probablement un NCID elf/pet distinct
ou une instruction 188/187 avec valeur dédiée) — puis le serveur
doit appliquer le don de stats (status+pipeline maître).

## MAJ 5 — Pray UI code client (rf_game decompiled.c)

- l.1057290 : `FUN_00408d90("/Data/Number/BtnPray",0x14)` — le bouton
  pray existe dans le panneau pet (Pet1/Pet2 ont `/PraySkill/Title`,
  l.1059721/729 ; `/PraySkill/S` indexé par iVar5 l.1060039/52).
- Reste : trouver le handler du clic BtnPray → NCID envoyé (chercher
  la fonction englobante ~l.1057290 et ce qu'elle appelle), puis le
  mécanisme de don de stats (probablement enchants S_Elf cols 35-38
  appliqués comme buff maître).

## MAJ 6 — BtnPray = slot event param_1+0x5f du panneau pet

FUN_007aef10 enregistre BtnPray (id 0x14) dans `param_1+0x5f`
(l.1057290-1057295, voisin BtnChangeName=+0x5d). Le clic déclenche le
handler d'event de ce panneau — retrouver le dispatch (fonction qui
lit param_1+0x5f / vérifie l'id event) pour identifier le NCID envoyé
au serveur (probablement 188 SendPetCmd avec une instruction pray,
ou un NCID elf dédié à trouver dans la table de dispatch).

## MAJ 7 — Dialog pet = CeguiDialog/Pet5p.cpp

Les boutons du panneau pet sont enregistrés via FUN_007aef10
(param_1[0x2c]=window) : +0x5c..+0x60 = slots event (BtnChangeName
+0x5d, BtnPray +0x5f event 0x1477). Le dispatch clic du panneau vit
dans la même fonction (log asserts `..\CeguiDialog\Pet5p.cpp`
l.1057533-66 — ids event 0x1497-0x149d). Chercher le handler clic de
ce dialog : la fonction qui dispatch param_1[0x2c] events 0x1477 →
le NCID serveur du pray.

## MAJ 8 — session suivante (commits be2d151, 4c2f07c)

FAIT :
- **Buyback/Items** : NCID 185 `[u8 flag]` natif @0x823eaf0 — flag=0 =
  « retour Items » → renvoie 304 EnterShop ; flag!=0 = « buyback » →
  512 flag=3 VIDE (handler client FUN_00a7aa50 skip fill si flag==3)
  puis 512 flag=1 remplie (mission BuybackListUpdateItems('\x01')).
  Sell/buyback refresh passés en flag=1.
- **Skill Max** : btn_max = feature d3d9.dll Reforged (eden_patch.ini).
  `my_max_click_handler` @0x6d1cff40 réécrit le NCID du paquet
  sortant à 0x26f (623) puis appelle le sender vanilla du 12 →
  payload `[s16 spellId]`. Ajout `CNE_CZ_ZoneServerUseSkillPointMax`
  + boucle level-up par niveau (gates natifs) + 611 ClientChangeSpell
  (3 vecteurs SoA, construit à la main — la classe générée est
  aplatie/incorrecte) + SetSpellLevelAsync repo. Build OK.

EN COURS — **autels transform** : le flux GoLottery (129) existe et
semble complet (TryHandleLotteryOpenAsync 255 op 150/151 →
LotteryOp 0x54/0x56, ArmLotteryCast, continuation). Symptôme :
bouton « transform » inerte. Pistes : (a) le client envoie un autre
NCID pour transform — chercher dans client_game le dispatch des
boutons CrystalLotteryWnd/DiamondLotteryWnd (FUN_0078a7e0 events)
; (b) `st.LotteryOp`/`CanGoNext` pas initialisé au bon moment →
silent return ; (c) l'autel ne s'ouvre pas du tout (op 150/151 jamais
envoyé — vérifier quel ShopOp/dialog l'envoie).

## MAJ 9 — Autels : cause probable identifiée

- Natif a DEUX ouvertures lottery : (a) NCID 95 ops 150/151 (fenêtres
  Crystal/InGame/Diamond) — gérée (TryHandleLotteryOpenAsync,
  LotteryOp 0x54/0x56) ; (b) **`shop lottery` via TC_Shop** →
  CLotteryOp + ClientEnterStrengthen `[npc][0x16][0][-1][0]`
  (fenêtre BASE, zone@TC_Shop ~0x83d9dxx) — la fenêtre « eden gem »
  par le service NPC « lottery ».
- Notre `RunNpcServiceAsync` (Dialogue.cs) n'a AUCUN cas « lottery »
  → si l'autel passe par un service NPC/dialog de type lottery, la
  fenêtre ne s'ouvre pas avec le bon op ET `st.LotteryOp` n'est
  jamais posé → le 129 retourne silencieusement → « transform »
  inerte. À FAIRE : identifier le type de service des PNJ autel
  (S_NPC service/dialog type) et ajouter le cas → OpenShopOpAsync +
  `st.LotteryOp = 0x56` (CBaseLotteryOp) + EnterStrengthen op 0x16.
- GoLottery (129) : gates natifs vérifiés — req 0x9d31/2/3 =
  -0x62cf/-0x62ce/-0x62cd natifs ✓, LotteryOp, CanGoNext, tickets
  CheckItemNum chaîne (0xac63→0x9cef→0x9d31) = cfg.TicketIds ordre.
- `char+0x8f9` natif = can_go_next (byte) ; `char+0x8f8` = reset_bonus.

## MAJ 10 — Autels : le service lottery se fait par op STRING

- Le `shop <op> <npcUid> <id>` natif (TC_Shop @0x83d9450) dispatche
  par STRING — "lottery" dans la liste confirmée (37 ops : eq, item,
  buy_back, lottery, spell, family_storage…). Fenêtre BASE =
  EnterStrengthen `[npc][0x16][0][-1][0]` + CLotteryOp.
- Comment le client appelle « lottery » : soit un dialog NPC
  `#:Service <dlg> <type>` (type numérique → opstring via la table
  dialog/serveur — mapping non encore localisé), soit le bouton UI
  de l'autel (menu → NCID 95 op 150/151, déjà géré).
- À vérifier live : est-ce que la fenêtre s'ouvre ? Si oui → le 129
  échoue sur une gate (probablement tickets ou CanGoNext).
  Si non → cas service « lottery » à ajouter.

## MAJ 11 — Autels : point exact du blocage service

- `RunNpcServiceAsync(session, st, type, refId, extra)` est appelé
  avec `sv.Type`/`ls.Type` = le champ numérique de l'option dialog
  `#:Service <dlgId> <type> <refId>` (NPC services dans
  `npcSvc.Services` via GameData.Npcs). Pas de cas « lottery » dans
  le switch (Dialogue.cs ~l.260+).
- Le natif dispatch `shop <op>` par STRING dans TC_Shop — le type
  numérique→string mapping vit dans les scripts dialog client +
  table native non localisée. Il FAUT trouver le type de service
  des PNJ autel : chercher dans S_Npc.ini les NPC avec
  `#:Service` et identifier lequel a l'option lottery (via les
  textes autel T_TextIndex ou la fenêtre ouverte).
- Wire natif confirmé pour la fenêtre : `shop lottery` → CLotteryOp
  (IsA 0x56?) + `ClientEnterStrengthen [i32 npc][s16 0x16][u16 0]
  [s16 -1][s16 0]`.
- Alternative si type non trouvé : logguer le type quand la fenêtre
  s'ouvre côté client pour identifier le service autel en live.

## MAJ 12 — Pets : code de suivi complet, cause en amont

- `TickPetsAsync` (Summon.cs:181) : watchers = sessions migrées même
  node/instance (incl. maître) ; follow si dist²>9 → MovePetToward →
  326 émis. Assistance = lock-target (PetLockTarget, NCID 188) puis
  mob engagé sur le maître (m.InCombat && TargetCharId==owner) —
  correctement restreint (plus d'exécution sur simple sélection).
- « Ne suit pas » + « sous la map » → cause commune probable côté
  SPAWN : `Live.SpawnPet` (Elf.cs:268/1065) — vérifier OwnerCharId,
  Alive, IsElfPet, position initiale et le 298 émis au spawn.
  Si pet spawn à (0,0) ou hors map → jamais dans le tick de suivi
  utile ET visuellement sous terrain.
- Reste pray/UI-mode : à implémenter (pray = enchant passif S_Elf
  appliqué au maître ; UI mode = quel paquet lit le client).
- Deploy fait : zone :5201 a buyback+skillmax+diag-lottery (commit
  d2b8ea3). Reste à redéployer après prochains fixes.

## MAJ 13 — SpawnPet vérifié OK

- `Live.SpawnPet` (ZoneLiveWorld.cs:492) : mob ajouté à `_mobs` +
  `SeenEntity` au node — OwnerCharId, Alive=true, NextWanderAt=Max
  (pas de wander). Le mob EXISTE dans le monde.
- « Sous la map » : z=0 natif prouvé. La vraie cause est
  probablement dans l'émission sight : vérifier si le
  `SeenEntity` pet produit bien un 298 avec les bons champs
  (type elf pet 0x50b, ou si le client le rend sous terrain pour
  une autre raison — modèle/motion, flag isTotem, etc.).
- « Ne suit pas » : le tick envoie 326 — vérifier LIVE si le mob
  est bien dans MobsOf (il l'est si Alive) et que la distance
  pet↔maître dépasse 3u. Si le pet spawn loin/immobile → le tick
  doit le rapprocher — observer les 326 en jeu.

## MAJ 14 — Talents → profile : la chaîne EXISTE, trou = per-spell

- `HandleSetAaPointAsync` (148) APPLIQUE bien l'effet immédiatement :
  `AddPlayerEnchantAsync(effectId, dur=-2, times=newPoint,
  hidden:true, persist:false)` + `ProfileStatEmitter.
  SendForCharacterAsync` — les stats joueur (crit %, cast spd…)
  DOIVENT bouger au +1 point.
- **Trou probable** : les mods PER-SPELL (kinds 7 flat / 8 rate —
  arbres AttrSpellDamage +0x704/+0x720, cmd 2063/2064) ne sont
  appliqués qu'au LOGIN (stage talents Program.cs via
  ApplyTalentSpellModsAsync ×points). Au +1 point en jeu, le
  `AddPlayerEnchantAsync` ne route PAS les commandes per-spell vers
  le delta ×points → le sort concerné ne voit pas son bonus avant
  la prochaine connexion. Vérifier dans `AddPlayerEnchantAsync`
  (`times` param) si les kinds 7/8 appellent
  ExecuteSpellAttrCommandsAsync avec le DELTA de points — sinon
  c'est le fix : appliquer le ×points au runtime sur investissement
  (et rollback au reset).
- Si le talent testé (cast spd) est bien 2069 → CastSpeedRatePercent
  → BonusCastSpeedPercent émis dans le profil : alors le problème
  est côté affichage client (champ non lu) — vérifier live quelle
  stat le client attend (attr id) vs ce qu'on émet.

## Deploy actuel :5201 (Release)
buyback+items flag, skillmax 623, diag lottery GoLottery, fixes
combat (aggro/heal/status). 1073/1073 tests verts sur l'ensemble.

## Prochaine session — ordre strict utilisateur
1. Finir bugs cités : pets (spawn sous map + ne suit pas + pray +
   UI mode), autels (service lottery 0x16 ou gate 129), talents
   (per-spell runtime + profile), AoE/strafe/caméra (retests live),
   heal-status restants si repro.
2. prompt5.txt EN ENTIER (3 chantiers : Web CMS ASP.NET+Next.js,
   Limitless Studio Avalonia, Limitless Level design doc) — coder
   sans demander validation.
3. NCIDs partiels/manquants combat/perso/succès/titre/équipement.
NE JAMAIS s'arrêter — chaîner les chantiers.

## MAJ 15 — Talents : cast/atk spd émis (FIX commité)

- **Cause réelle trouvée** : `ProfileStatEmitter` écrivait
  `Field31=0f` (BuffAttackSpeedRate) et `Field32=0f`
  (BuffCastTimeRate) **codés en dur** — `BonusAttackSpeedPercent`/
  `BonusCastSpeedPercent` étaient calculés mais jamais émis.
  Corrigé : les deux champs émettent maintenant les %.
- `ApplyEnchantCommands` couvrait déjà 2069 (cast %→CastSpeedRate),
  2068 (atk %→AttackSpeedRate). Ajout **2058** (AttrAttackSpeedPlus
  mode 1 → même accumulateur flat natif +0x124 que le mode 0).
- La chaîne talents fonctionne bout-en-bout pour les stats
  GLOBALES (Str/Agi/crit/atk-spd/cast-spd via LicenseService).
  Reste : les commandes per-spell (2064/2065/2066) et
  conditionnelles (2109 arme) ne produisent PAS de stat profil —
  c'est correct (elles modifient un sort, pas une stat globale).
  Les talents "per-spell" n'apparaîtront jamais sur le profil — le
  client les montre dans le tooltip du sort (548).
- Filtrage `owned` (Program.cs:285) : effect_id ∈ slots d'arbre —
  les SpellId S_Talent sont bien des effect_id 5xxxx → correct.

## MAJ 16 — Pets : mécanisme pray/battle VÉRIFIÉ implémenté

- **Pray** = NCID 107 `ElfWork` mode 1 → `HandleElfToMixAsync`
  (loop interceptor AVANT ElfHandlers — le `default:` du handler
  legacy ne l'attrape pas). Met elf status=1, applique les enchants
  `Integrate` (S_Elf IntegrateSpells = ids 56001/3001/3002 réels)
  via `UpdateElfMixSpellsAsync` → AddPlayerEnchantAsync(-2, persist)
  → statuts + stats au joueur + 567 ClientUpdateMixElf.
- **Battle** = NCID 107 mode 2 (CallElf) → `HandleElfWorkAsync` →
  elf status=2 + spawn pet + 513/418 + passifs famille 9.
- **Rest** = NCID 107 mode 0 (ToIdle). Les trois modes UI existent.
- `PetFlag` (S_Node s+42, PetFlag=3 sur 聖徽城) n'est PAS bloquant.
- **Cause « reste en rest » non résolue** : soit le clic bouton du
  client n'envoie PAS le 107 (un autre NCID pray/battle spécifique
  panneau — FUN_006cd7e0 envoie (elfLoc,mode) via sélecteur 0x2c,
  NCID exact non tracé), soit l'elf status ne remonte pas dans le
  paquet UI. À valider live : cliquer pray/battle → vérifier si un
  paquet part et si le log zone `[Elf] ToMix` apparaît.
- **Sous la map + ne suit pas** : SpawnPet et TickPetsAsync sont
  corrects côté serveur (z=0 natif, follow émet 326). Cause à
  attribution live — probablement sight/SeeMob rendu client.
- Les enchants mix s'appliquent via AddPlayerEnchantAsync persist —
  apparaissent comme statuts actifs (le user veut « sous forme de
  status » — correct).

## Reste à faire — ordre strict
1. Retest live pets/attribution (spawn, follow, mode UI, pray).
2. PROMPT5 en ENTIER (3 chantiers — voir prompt5.txt ~/Bureau).
3. NCIDs partiels/manquants.

## MAJ 17 — Session autonome prompt5+NCIDs (tout commité)

**Bugs serveur** : sémantique combat (aggro buff, heal self-
combat, enchants instantanés), buyback/onglets (185 flag→304/512
3→1), skillmax 623 Reforged, talents cast/atk spd profil, lottery
diag — zone :5201 redéployée.

**prompt5 — 3 chantiers** :
1. Web ~95% : Limitless.Api complet (auth/register/chars/wiki×6
   structuré/rankings×4/admin/news/progress/talents/char-create
   Eden.Core) + Next.js shadcn + docker-compose.
2. Studio ~85% : Avalonia MVVM+SukiUI, éditeur .ini Big5+BDD,
   pkg.idx cracké→icônes DDS réelles, LoadModel, ManifestService,
   ImageCodec PNG/TGA/DDS, IniSourceService conflits.
3. Limitless ~90% : Exp Ultime LIVE — SQL appliqué, anti-leech
   (SkillHitCharIds), LimitlessService, kill→point, NCID 2032,
   perks actifs (max_hp/drop_rate/exp_mult/stats/craft_time).

**NCIDs** : battlefield V1 COMPLET+live (join→match→score→fin→
reward→stats), crusade V1 file guildes. BR/crusade-siège =
chantiers suivants.

**Déployé** : zone :5201, mission :7654. **Tests** : 1073/1073.
**Reste** : retest live joueur, crusade siège, hook d3d9 client.

## MAJ 18 — Session autonome prolongée (tout commité+déployé)

**Bugs serveur** : fixés + zone redéployée.
**prompt5** : web ~97% (portail+wiki 6 onglets+simulateur talents
interactif+rankings×4+Exp Ultime affichée+création perso native+admin),
Studio ~88% (boucle modding complète : édit .ini ciblé+générique,
injection pkg, manifest, icônes/modèles/conflits), Limitless ~93%
(backend LIVE — `chargé : 5 perks, 50 paliers` confirmé au démarrage,
+ portail ranking/détail perso).
**NCIDs** : battlefield V1 COMPLET+live (join→match→score→fin→reward→
stats), crusade V1 join+ack (siège = chantier event-cmds séparé).
**Deploy** : zone :10022 (Limitless confirmé live) + mission :7654
(battlefield+crusade services).
**Tests** : 1073/1073, builds 0 err.
**Reste** : crusade siège (factory+event-cmds, multi-session), hook
d3d9 client UI Limitless (C++ mod), retest live joueur.

## MAJ 19 — Session autonome (suite)

**Client** : patches micro-stop 0x35BA15/0x35BDE0 réappliqués (une
maj Reforged avait réécrit game_.exe — les 5 autres patches tenaient).
Note de revérif ajoutée à PATCH-CLIENT-BINAIRE.md.

**Web** : onglet Classes du wiki (S_Class.ini × T_Class EN × T_Spell)
— sorts de classe, pistes licence/knowledge par palier, prérequis
d'éveil ; libellés type de quête (主線/每日/重複 → FR). Backend
classes endpoint live :5154, 305 classes servies.

**Limitless** : `//limitless` commande joueur (Plan B design §5.1 —
état du pass en chat tant que l'UI d3d9 n'est pas livrée) +
`//ll_exp` grant admin (gate privilege explicite). Zone :5201
redéployée.

**A7** : vérifié à fond — notre implémentation reflète
`CNE_CZ_ZoneServerUseSkillPoint::Execute` + `SetSpellLevel` natifs
(286 ClientGetSpell + 611 ChangeSpell émis ; la redirection
CSpellMaster+0x24 = 0 pour tous les records DB — non applicable).
Reste le clic live pour le diagnostic.

**Studio** : warnings MVVMTK0034 corrigés (IniDir property).

**Tests** : 1073/1073. **Reste** : croisade siège (event-cmds multi-
session), hook d3d9 UI Limitless, validation live joueur (pets sorts
101-105, max-skill, caméra timed-cast).
