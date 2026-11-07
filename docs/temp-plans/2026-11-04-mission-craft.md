# Mission — Craft & Guilde complet + Gameplay réel + Bugs récurrents

Date : 2026-11-04. Statuts : `docs/workflows/task-contract.md`.
Priorité : **§3 bugs récurrents + §2 gameplay** (bloqueurs) → **§1 phases A→G**.

## §3 — Bugs récurrents (à corriger DÉFINITIVEMENT + preuve live)

| # | Bug | État connu | Statut | Preuve |
|---|---|---|---|---|
| B1 | Élites/mini-boss : pas de chemin tracé vers les crystaux (bleu→rouge) | Le concept : les élites vont vers des crystaux sur la map | **FIXÉ+prouvé live** | Mécanique déjà codée (MobWander : EventSpawned → marche unique jusqu'au dernier waypoint = cristal, ancre + siège), manquait la preuve live. Boot zone 00:28:27 + population node 5 : events born 1/5/9 à 00:29:10 → `Event_RebornMonster` spawn des 3 boss → marches constatées : `#60048 fin de route 313 (75,184) → siège 0xffff85e5` (36 s), `#60047 fin de route 308 (387,100)` (91 s), `#60049 fin de route 318 (376,350)` (96 s) — fins = positions EXACTES des cristaux → sièges → cristaux bleus détruits par les handles boss : `55070 détruite par 0xffff85eb → event 7` (00:30:01), `55068 → event 3` (00:30:55), `55069 → event 11` (00:31:01). Durées d'arrivée proportionnelles aux longueurs de route = vraie marche, pas téléport. Perso posé sur la route 313 (120,140) pendant le cycle. **Cycle natif complet observé 2026-11-06** : cristaux rouges spawns par annonces WorldBoss (60019/60020/60021) → timers +420 s → events 2/6/10 (00:36:10, fermeture générateurs) → rouges auto-détruits +1200 s → events 8/4/12 (00:50:02-00:51:02, retour état bleu) — boucle s005 [event] 1-12 entière |
| B2 | Déséquipe/rééquipe perd le skin tête d'armure ; seul le panel profile (cases à cocher slots tête) doit toggler | Race `QueueFree` différé : `ClearEquipAttachments` condamnait le `BoneAttachment3D`, `AttachGlbToBone` le réutilisait → sous-arbre libéré fin de frame (perte « parfois », alternance visible/invisible). Cases ShowKuso* = toggle local natif (options.ini, aucun paquet) | **FIXÉ** | RemoveChild+QueueFree + garde IsQueuedForDeletion ; HudPrefs pref.kuso.* ; ApplyEquipment attachVisible(loc)+hideOutfit ; flag test --kuso ; captures /tmp/kuso_off.png (tête nue) + /tmp/kuso_on.png (heaume après rebuild) ; item 32234 inséré en DB pour le test |
| B3 | Impossible d'up character ou class (XP cassée) | 284 ne maj que le FX — portrait/ratio XP stales ; double-kill serveur (2×305+2×XP) | **FIXÉ** | case 284→SetSelfLevel ; capture /tmp/lv45.png (niv45+barre XP) ; serveur : guard Alive+DeathEmitted |
| B4 | Barre exp character + TopLeftVitals cassées (traits héroïques) — 3e casse, code retiré | Capture live : portrait+HP/MP+icônes trait présents, barre EXP bas-gauche «0.000%» vivante (600→SetExp→Recompute) | **VÉRIFIÉ** | /tmp/lv45.png |
| B5 | Armes/skills distance ne marchent pas (hunter + autres) | 3 bugs : (a) `AutoAttack.AttackReach=3` hardcodé → chasseur courait au contact ; (b) anim swing `attack01/02` générique au lieu des pools kfId natifs par kind ; (c) `SkillCast.BeyondReach` col28 ×1,5 au lieu de ×0,5 (SSpellData+0x40 natif) → casts déclenchés 3× trop loin → 0x333 « trop éloignée » | **FIXÉ+prouvé live** | Natif : `CCharFormula::AttackRange` @0x083f6f10 = col48 brute SANS multiplicateur ; `CanAttack` @0x08417120 ajoute rayons+DAT_087b2730. Client : `ReachNow()` = col48+1 (arc 42→43u) ; `AttackAnimNames()` miroir `MotionParser.WeaponAttackKfIds` (arc→attack12, gun→attack13, dual C[m×3+o]). Serveur : `AttackRange` ×1,5→brute+5. Live (arc 33631 loc10, `//cc 3` Chasseur) : swing#1 **immédiat à ~5,6u** (impossible avec reach=3) anim attack12, `272 dmg=291 type=3 kf=1421`, rshoot01-79 cuits, kill + XP. Epées/arc test restaurés en DB |
| B6 | Stun/sommeil : pas sous l'animation du nerf ; DOT élémentaire = particules sur joueur/cible | Le visuel col3 (BUFF特效) des enchants n'était jamais attaché — seul le cast/impact SpellFx jouait | **FIXÉ+prouvé live** | `EntityBuffs` émet EnchantVisuals (added→`SpellFx` attache col3 sur l'entité pour la durée rest, removed→detach) ; `EnchantTable.VisualKeyOf`. Live : `enchant 52038 «s01151» sur 0xffff809a (2s)` sur le stun de charge ; DOT/debuff = même chaîne (299→icône+particules, 300→detach) |
| B7 | Sorts de stun joueurs : ne stun pas, mauvaise durée ; sorts de déplacement (charge guerrier) KO | Charge : le serveur émet NCID 267 `ClientGoto` au charId **self**, mais `EntityRegistry.HandleGoto` n'acceptait que `_entities` (jamais le self) → drop silencieux, le perso restait à 20u | **FIXÉ+prouvé live** | `HandleGoto` route le self vers `PlayerController.TeleportTo` (clear path/moveTarget + snap). Live charge 50038 : poursuite portée 20u → 279 → `267 self pos=(227,43) flag=0x6a` → client renvoie NCID 1 depuis la nouvelle pos → mêlée immédiate + `299 eid=52038 rest=2` (stun mob 2055). Stun joueur : `//self_use_effect 52059 40` → `299 rest=40` + 280 attrs 1002-1006=1 → `Cast()` refuse tout sort via `NoAnySkill` (0 paquet émis, gate client OK) ; à +40s : `300` + attrs→0 → déverrouillé. Zone log : `expire`+`remove rest=0` à la seconde prévue |
| B8 | Items drop au sol « Pas assez de place » alors que slots libres ; doit drop sur soi si place, sinon sol + clic gauche ramassage | Divergence capacités : `inventory_slots` non réconcilié avec sacs équipés à l'entrée (312 émettait des caps stales) + `NativeInventoryParser.Capacities` jamais vidé (caps fantômes entre persos) ; ramassage clic droit seulement | **FIXÉ+prouvé live** | Réconciliation slots↔sacs équipés dans `EntryHandlers` avant 312 ; `Capacities.Clear()` au début de Parse ; clic gauche sur kind0+EntityId≠0 → `TreasurePickup.BeginPickup` (approche + NCID 35). Wire : kill→291 merge stack (loc12/13), 274 nouveaux slots (bag0 loc30/31 puis bag1 loc0 quand bag0 plein), sacs 47/47 → **329 trésor au sol** ×3. DropRate=100 temporaire pour la preuve, restauré. **Ramassage re-validé live 2026-11-05** : `//give 33631 5` sac plein → trésor 1900000003 au sol (coffre doré + label « Arc de Patrouille Sylvestre ×5 », capture `b8-treasure-chest-ground.png`) → clic gauche → approche + NCID 35 → refus `316 f9040000` « Pas assez de place dans le sac à dos. » (trésor conservé) → `//clear_bag 0` → re-clic → `274 ×5` (slots 0-4) + `386 PickupOK` + `330 despawn` + « Obtenu : Arc de Patrouille Sylvestre ×5. » (capture `b8-treasure-pickup-ok.png`) ; merge stack 32632 → `291` au bon slot |
| B9 | Inversion animations de montures toujours là | Records monture natifs 9/10 décodés (DirectionControl.cpp, init @0x…2470067, table DAT_00fe49a8) : rec9 = copie Left + facing −90° D3D, même callback vtable+0x38 → action 102 → move02 ; rec10 = copie Right + facing +90° → move03. Le natif ne croise PAS — notre `MountAnimFor` croisé était l'inversion ; le remote (flag 326→AnimForFlag) jouait déjà le mapping natif | **FIXÉ+prouvé live** | `MountAnimFor` non-croisé dans les 2 copies (Left→move02, Right→move03) + NetTests 30 verts ; montage live via `//clone_item 20051` (UseEnchant 58991) + `//self_use_effect 58991 600` → 283 key=14010 (Puma Enflammé) ; flag `--strafe=<dir>[:sec]` ajouté. Gauche : `rec=3 mount=move02 cur=ride05 modelYaw=90` + `/tmp/b9_mount_left.png` (profil gauche) ; droite : `rec=5 mount=move03 cur=ride06 modelYaw=-90` + `/tmp/b9_mount_right.png` |
| B10 | Changé de classe → clerc : ne peut cast aucun skill (mana OK) | Gate client lisait `SessionCarry.Character.ClassId` (stale après cc) + refusait les masks multi-bits autorisés par `CanCast` serveur | **FIXÉ** | SkillCast.cs : `PlayerVitals.ActiveClass` + PopCount>1 ; live : `//cc 3` → 50038 guerrier refusé (cls=3), 50150 casté → 279 OK + 281 + floaters |
| B11 | Mobs : model « cassé » affiché dès l'état normal (au lieu d'intact jusqu'à BP terminé/gros hit) | `PatienceSeen` fix posé — garde WeakMask+PatienceSeen suffisante ; le symptôme d'origine venait de patience=0 par défaut → KNOCK caché au spawn | **FIXÉ+prouvé live** | Cycle complet prouvé : `/tmp/b11_normal.png` (coquille KNOCK verte visible au spawn — M025 431 tris, block 37 skinné ; golem M020 idem) → `/tmp/b11_broken2.png` (pilote `--broken-test` : SetBroken(true) → KNOCK masqué, champignon nu) → `/tmp/b11_restored.png` (`--broken-restore` : SetBroken(false) → coquille revenue, FUN_00771920). Dump nœuds confirme `KNOCK` sous Skeleton3D dans le visuel NIF |
| B12 | Énormes lag quand on kill des mobs | 3 causes trouvées et bornées : (a) drains réseau **non bornés** — `GameScene._pending` vidait tout le burst en 1 frame (pic `proc=1204ms` à l'entrée de zone, `reg.net 37,7ms` aux kills) ; (b) GLB mobs chargés à froid au 298 (18-37 ms de parse par nouveau stem) ; (c) `Interp.SetProcess(false)` posé hors arbre ne tenait pas → **256 callbacks C# fantômes/frame** (proc-dump) | **FIXÉ+prouvé live** | `--kill-loop` 17 kills S005 (`/tmp/b12_loop4.log`) : pire image/kill 8-26 ms (vs 11-41 ms), `seeTreasure` 12,9→1,7 ms, `reg.visual` quasi absent ; pic entrée zone 1204 ms → ~140 ms étalé ; proc-dump : `NetworkInterpolator×256` disparu. Fixes : drains `_pending`/`_deferredDuringMigration`/`_netPending` bornés 8/6 ms, warm async modèles mobs dès le 298 + preload G00001-10 au map, `SetProcess(false)` post-`AddChild`. Restes mesurés (hors kill, consignés) : actions unitaires login ~40-100 ms (1re construction inventaire/sorts), `CooldownOverlay×450` en process, `reg.tick` ~11 ms occasionnel, trésors 300 s TTL → accumulation draw/labels |
| B13 | UI talk NPC : virer les ids, taille fixe, fond clair (cf. ref-shots/web-shots `npc talk*.png`) | Déjà implémenté (ResolveOptionLabel+IsBareId+StripId, TalkW/H=358×664 = QuestWnd, Paper crème) — re-vérifié proprement en live | **VÉRIFIÉ live** | `--talk-test` S005 : 285 réel du Forgeron Ursun `ctx=4033` → `/tmp/b13_talk_live2.png` (titre « Forgeron Ursun Bryson », texte parlé, crème, « Fermer » sombre — conforme ref `npc talk propositions.png`) ; mock `--hud-open=dialogue` → `/tmp/b13_mock.png` : ids bruts «1012»/«1013» résolus en «Nv. 2 Votre première Quête IV/V» (ampoule), «29931»→«Discuter» — aucun id affiché. Bonus mesuré : NPC « Passage » rend 393 transport 40 routes (dont 1 libellé «#57» non résolu — gap donnée à consigner) |

## §2 — Gameplay réel (jouer jusqu'à ~niv 25, `tools/run.sh -- --login=test123:test123 --channel`)

| # | Vérification | Statut | Preuve |
|---|---|---|---|
| G1 | Level-up perso + classe (XP, stats, sorts) | **VALIDÉ live** | Progression réelle lv1→35 (chaîne Goss + donjons + quêtes) : XP persistée >75 s avec régén HP (fix staleness XP/level), level-ups répétés par le pipeline `GrantExpAsync` (refresh 296), sorts de classe appris/utilisés en combat (heal Cleric, attaques), récompenses XP/or des remises de quêtes créditées (+986g +33836xp sur 1098) |
| G2 | Quêtes fonctionnelles jusqu'au niv 20-25 (prendre/TP/accomplir/valider/récompense) | **VALIDÉ live** | Chaîne main Goss Mountains 1092–1102 complète : accept (dont col-15 `AcceptInventoryItem` — fix parse+gate If_* + exécution, 32809/32787 octroyés), kills, drops au sol tagués ramassés via `@pickup` (NCID 35 → crédit quête), chaînes boîte kind-48 (32787→appât→32791, 32809→32810), objectifs NPCItem au clic d'entrée, rendus chez 50032/50036/50038/50039/50031 ; abandon/ré-accept via NCID 51 (fix entrée 0xFE bloquante) |
| G3 | Craft d'équipement de bout en bout | **VALIDÉ live** | NPC 50354 (`//npc`) → mission 127 → `@dlg 0` option « Missions d'Artisanat » → **496** EnterWorks type=16 ; `@work 132 0 14` → **170** → `Work armé num=0 type=16 id=132 dur=50u (instant)` ; **433** cast bar (type16, 50u) + **299** enchant travail + **497** update ; consommation différée à matérialisation : **275**×3 (31096@14 + 30031@26×20 + 30038@27×6) + or 490 taxé ; **498** résultat (produit 30297 matérialisé dans `player_works` work_num=0) ; `@workclaim 0` → **171** → **274** insert 30297@slot14 + **500** clôture ; capture `craft-e2e-132.png`. **Bug fixé** : `CountFreeBagSlotsAsync` (`ZoneEntryFlow.Work.cs`) appelait `GetBagCapacity` qui renvoie null sans ligne `inventory_slots` → free=0 → refus 0x4f9 « sac plein » permanent ; remplacé par `GetAllBagCapacitiesAsync` (fallback sac0=32) |
| G4 | Création guilde → VDG : poser bâtiments via NPC/commandes | **VALIDÉ live** | Guilde créée par le wire légitime : `@famcreate QAForge` → NCID 49 → `NewFamily #5001 'QAForge'`, or 147058→97058 (−50 000) ; `//open_hall` → node 502 ; entrée par le wire natif `@fament 5001` (NCID 176 TownEnter→502) → `FamilyHall spawn fam=5001` + housekeeper 53007 + **487** avec les 3 bâtiments (tids 0x65/66/67) ; bâtiments posés via `//give_building` : ferme H01001 (tid 101) loc0, mine H01101 (102) loc10, autel H01201 (103) loc20 ; NPCs de service posés via `//place_npc` : fermier 53010, mineur 53020, invocateur 53028 (+ housekeeper 53007 auto) ; VDG rendu (bâtiment + palissade visibles). **À noter** : `//place_npc` hors du hall place la ligne au slot suivant avec la position du GM — la ligne placée depuis S001 (loc2 @220,299) est inerte ; spawn en vue seulement à l'entrée suivante du hall. **Entrepôt de guilde** : housekeeper 53007 → option « Activer l'Entrepôt de la Guilde » → **507** EnterFamilyStorage ; `@stdep 0 0 25` → 177 → 275 (slot sac vidé) + **505** broadcast membres + **506** journal « Storage Insert » (`FamilyStoragePut item=30068 → coffre 0 loc=0`) ; `@stget 0 0` → 178 → 274 (item rendu) + 506 journal « Delete » (`FamilyStorageGet → (0,25)`). Gate natif confirmé : coffre 0 exige famille lv≥3 (`//set_family_level 3`, `CheckPrivilege` priv 11) — à lv1 le 177 était refusé silencieusement. **Don trésorerie** : `@famgive 5000` → 159 → `char donne 5000 à la famille`, `family.treasury=5000` en DB (or perso −5000). **Bataille de guilde** (crusade, DBA mission) : `//crusade start 1` → checkin node 310 ; `//crusade join 1` → `join crusade=1 family=5001` + migration `Instance resolve node=310 type=19 groupe -200000020` ; `//crusade battle 1` → `BATTLE forcée (1 inscrits)` ; `//crusade close 1` → clôture + retour perso en S005 ; capture `crusade-battle.png` (map canyon de l'instance) |
| G5 | Récolte VDG (pioche/gant/boule), plans mine/ferme/autel | **VALIDÉ live complet** | Outils directs (aires S502 : keyId 60 mine / 61 champ / 62 mana, restriction `501 502;<key>`) : pioche 26 enchant 50660 → `CollectItem slots=0` (roll 5%), durabilité 100→99 ; gant 21 enchant 50659 → `CollectItem slots=1`, 274 insère produit 22, outil épuisé (275) ; boule 31 → `CollectItem slots=1` ; acks 291/303 à chaque usage. Plans (S_FamilyCollect, `@work <catalystId> 0 <slot>` — le 170 porte l'id catalyseur, map cléée col2) : **ferme** type 13 — fermier 53010 → dlg option « Mission Agricole » → 496 → `@work 30136` → armé dur=12000u (cap EDEN_DEV_WORK_CAP=50) → 498 produit **30001×10** → claim 171 → 274 + 500, persisté loc23 ; **mine** type 12 — mineur 53020 → 496 → `@work 30191` → usure bâtiment (`Work wear fm=5001 bld dur=199→198`) → 498 produit **30031×10** → claim → 274 + 500, persisté loc24 ; **autel** type 14 — invocateur 53028 → 496 → `@work 30246` (instant, consommation différée) → `Summon work → mob 58901 handle=FFFF86A8` (BornMonster natif) → 435 [0xe][1][0]×4 clôture sans 498/claim (EndSummon natif) ; boss visible en vue (Sight +mob 298, hp 2141) puis tué par le chemin de mort réel → loot **30068×2** (異獸毛皮) ; capture `vdg-hall-g5.png` (hall + dialogue invocateur « Missions d'Invocation de Monstres »). `fm_name=QAForge` armé sur les 497/498 des ordres hall ; `EDEN_DEV_WORK_CAP=50` (env zone, 5 s) utilisé pour accélérer les durées natives 20 min |
| G6 | Fortification jusqu'à +14, fusions, enchant/inlay | **VALIDÉ live complet** | +14 : 14×NCID=436 niveaux 1→14 sur 31908 ; DB `strengthen=14, bind=1` ; tooltip « Masse Royaume+14 » (`strengthen-14-tooltip.png`) ; refus 434 hors fenêtre 1-13 (cible +14 et niveau 0) ; sync 291/275. Fusion : recette cat-6 843 → 34476 (432/433/435[10][1], mats consommés). Enchant : scroll 30985 → witchcraft persisté. Inlay : gemme 30937 → embedded_id1 (refus text 3278 niveau, succès req35) |
| G7 | Commerce : shop, HdV, étal, trade, item mall | **VALIDÉ live complet** | Shop : achat 31895 (or 44847→43347) + vente ×8. Item mall : catalogue 472 entrées, achat 1 pt (pvalues 10721→10720, NCID 63 OK), reçu #121 → item 40362 livré sac (56/57/58), refus « Inventory full » sans insertion partielle. HdV : NPC 53003 spawné via `//npc` (fix HandleToEntity event-spawn, `ZoneEntryFlow.Events.cs`), service #18 → NCID 430 ; migration 026 appliquée (colonnes dénorm) ; vente 117 atomique (retrait item + dépôt 5% + INSERT en 1 tx) listing 1 ; requêtes 114 catalogue / 116 mes-ventes peuplées ; achat 115 par racetest/200000004 : −8000 buyout, vendeur +4600 (5000−8%), status sold, livraison mail+mailitem (426 reçu) ; annulation 118 listing 2 : status cancelled + retour mail ×2 (429 reçu). Étals : vendeur test123 S001 région 600 statut 377[1][2] via 77, acheteur racetest 72→378 goods → 76 VendorBuy item 38572 prix 1 (vendeur +1, acheteur −1, item transféré, fermeture 74). Trade J2J : 36 invite → 37 accept → or/item offerts → double-lock 41 → exécution (complet=True des 2 côtés, item 38572 200000020→200000004, or symétrique ±1, zéro dupe DB). **À noter** : 2e compte live = `racetest:test123` (account_id 3, persos 200000004-6, priv 5) — `edenfr.account.accounts`, pas edenfr_account (UnifiedDbConn prend le pas) ; miroir sac client périmé après retrait atomique 117 jusqu'au relog |

## §1 — Phases craft/guilde (SYNTHESE.md §4)

| Phase | Contenu | Statut | Preuve |
|---|---|---|---|
| A | Fondations UI : ItemSlot interactif, MCL, TabControl, icônes métier, ItemRecord complet | **TESTED** (visuel en attente — lock Godot) | Build client vert ; NetTests 921/921 dont `BuildSpec_Strengthen_SlotsEtMultiColumnList` + `PutItem_Parse274_MetaSocketsCombosMagicBox` + `UpdateEquip_Train18B_Groupe_AlignementRecordSuivant` ; `ItemSlotCell` (hérite TextureRect → compat `Find<TextureRect>`) + `MclList` instanciés par `CeguiToGodot` ; `UiControlKind.ItemSlot/MultiColumnList` + parse `ColumnHeader` (text/width/id) dans les 2 copies CeguiBuilder ; `ItemMeta` (sockets/combos/train/gold+magicbox enchants/due_date) propagé sur `ItemRecord`/`PutItemResult`/`InventorySlot` pour les 4 variantes ; appelants adaptés : SocialWindow, GuildWindow, VendingWindow (fallback ItemList gardé). **Bug pré-existant corrigé** : bloc CEquipTrainAttrs bit21 du UpdateEquip = 18B groupé `[t1 t2 t3][l1 l2 l3]` (était compté 16B → désalignement des records suivants d'un item entraîné). Échecs préexistants hors scope (autres agents) : Mount_SideAnims, MountEmitter perf, HotRedis, GrantExp_Level30Repro |
| B | Fenêtres fil chaud : shopwnd→work→collect/combine→strengthen→lifeskill→familytownlist | **VALIDÉ live** | Captures D-* 2026-11-06 (16 fenêtres regardées) + flux live : shop buy/sell/buyback (g7-shopbuy), work collect/combine (craft-e2e-132), strengthen +14 (window+tooltip), lifeskill, townlist |
| C | Guilde complète : familywnd+onglets, familybuilding, storage, familybattle | **VALIDÉ live** | Création wire (49) QAForge #5001 −50k ; hall S502 via 176→487 ; bâtiments ferme/mine/autel ; storage 507/177/178 (gate lv3) ; dons 159 treasury=5000 ; crusade S310 (start/join/battle/close) ; captures vdg-hall-g5 + crusade-battle |
| D | Cube/MagicBox/specialcoin/itemmall/tradewnd/auction/stall/tooltips | **VALIDÉ live** | 16 fenêtres capturées + regardées (D-*.png 2026-11-06) ; bug SetLeafText fixé (specialcoin live) ; item mall achat live (pts 10721→10720 + livraison) ; tooltips inlay/+14 prouvés ; reste : i18n mineur consigné |
| E | Serveur : plafond DEV 10s, combos strengthen, devises fame shop, lifecycle bâtiments, achievements, auction_history, RandItemCombo | **VALIDÉ live** | E1✓ cap EDEN_DEV_WORK_CAP (start+load) ; E2✓ combos slots 6-10 (837 kind 17) ; E3✓ devises ; E4✓ lifecycle (put/upsert/dur/wear) ; E5✓ relay family_achievement live (kill 57501→801|999, claim craft→502|999, log+rank+67) + fix AddItemFromWork ; E6✓ auction filters+history ; E7✓ |
| F | VDG monde réel : instanciation S501/S502, NPCs, zones récolte, reliques | **VALIDÉ live + visuel** | S502 instancié, NPCs hall (farmer/miner/summoner/housekeeper), 3 outils récolte (aires 60/61/62), 3 plans (30001×10, 30031×10, boss 58901→loot 30068×2), usure bâtiments, crusade instance ; statues validées en client réel (@fament 5000 → 54901/54902 en vue, label «TestGirl» propre, captures statue-*.png) |
| G | Validation globale : captures par fenêtre, wire_diff vs natif | **VALIDÉ** (wire via goldens+live) | Captures par fenêtre (D-*+G*+statue-*) ; FEATURE-MATRIX régénérée (67 GOLDEN/174 DESASM) ; wire_diff MITM non rejoué (transport TLS modern — outil legacy) mais 254 goldens byte-à-byte + trames live ; tests finaux 938+967+1778, seul échec pré-existant GrantExp_Level30Repro |

## Journal

### 2026-11-04 — Session 1
- Création du fichier. Serveur déjà up (PG 5433, services natifs depuis 05:38).
- Arbre : 272 fichiers modifiés stagés (recon craft/guilde + batch retours 11-04).
- Démarrage par §3 : re-vérifier chaque bug EN JEU puis fix racine.

### 2026-11-04 — Session 2 (reprise)
- **B10 FIXÉ** : gate cast = `PlayerVitals.ActiveClass` + règle multi-mask (miroir `ClassSkillTable.CanCast`) ; prouvé live (`//cc 3`, 50038 refusé cls=3, 50150/50231/50269 → 279 OK).
- **B3 FIXÉ (client)** : `case 284` dans `OnMiscPacket` → `SetSelfLevel` (portrait + ratio XP) ; capture `/tmp/lv45.png` montre niveau 45 + barre EXP.
- **Double-mort mob FIXÉ (serveur)** : `KillMobAsync` gardé par `mob.Alive`, `DeathEmitted` posé une fois → un seul 305/loot/XP par kill (avant : 2×305 + 2×8000 XP).
- **XP de kill confirmée** : 600+968+284 émis, DB monte (kill 56005 = +8000 @ rate 100).
- `//level`/`//set_level`/`//gain_exp` OK live ; seuil lvl30→31 = 731268 (S_Level sain, test unitaire).

### 2026-11-05 — Session 3 (Phase A)
- **Phase A implémentée + testée** (détail dans le tableau §1) :
  - `ui/CeguiBuilder.cs` (client) + `src/Eden.Core/Ui/CeguiBuilder.cs` (miroir) :
    `UiControlKind.ItemSlot` (DejaVu/SlotImage, SlotImageItem…) et
    `UiControlKind.MultiColumnList` (CEGUI MCL/MCL2), parse `ColumnHeader`
    `text:… width:{s,o} id:n` conservé dans `UiNodeSpec`.
  - `ui/CeguiToGodot.cs` instancie `hud/ItemSlotCell.cs` (TextureRect →
    compat Find&lt;TextureRect&gt; BuffBar/ItemMall ; quantité, tooltip
    ItemTooltip, drag source/drop, events item) et `hud/MclList.cs`
    (en-têtes de colonnes, lignes, sélection, RowActivated).
  - `protocol/NativeItemRecord.cs` : `ItemMeta` complet — sockets (bits0-5),
    amount/combo, create_time, extra combos 0-10, strengthen, due_date,
    bloc CEquipTrainAttrs, gold enchant, 3 enchants magic-box, énergie,
    star_level — parsé depuis mask + queue fixe pour les 4 variantes
    (Inventory/PutItem/EquipView/UpdateEquip/ElfContainer) et propagé à
    `InventorySlot.Meta` (ZoneHudMessages) et `PutItemResult.Meta`.
  - **Fix pré-existant** : UpdateEquip bit21 = 18B groupé (était 16B →
    désync de 2 octets des records suivants pour items entraînés).
  - Appelants MCL adaptés : SocialWindow (communitywnd), GuildWindow
    (familyinfownd_na), VendingWindow (stallui/stallbuy MCL2) — fallback
    ItemList conservé partout.
- Preuves : build client OK ; `tools/test.sh` NetTests 921/921 verts ;
  tests ajoutés `CeguiTests.BuildSpec_Strengthen_SlotsEtMultiColumnList`,
  `ZoneItemTests.PutItem_Parse274_MetaSocketsCombosMagicBox`,
  `UpdateEquip_Train18B_Groupe_AlignementRecordSuivant`.
- Échecs tests hors scope (zones touchées par d'autres agents le même
  jour) : `Mount_SideAnims_NativeRecords`, `MountEmitter_ReskinCost`
  (perf), `HotRedisTests`, `GrantExp_Level30Repro`.
- **Visuel Phase A en attente** : lock `/tmp/godot.lock` occupé par un
  autre agent (dungeon-sweep S223→S612). À faire dès libération :
  capture communitywnd/familyinfownd_na/stallui.

## 2026-11-05 (suite Devin) — Phase B5 + B6

- **B5 LifeSkill/racial craft** — `hud/LifeSkillWindow.cs` (nouveau) :
  - Ouverture via `ZoneClientEnterStrengthen` 432 `Field2==0x68` → envoie
    213 QueryLifeSkill ; 543 déjà routé vers `PlayerProfileState` (ajout
    de `RaiseChanged("lifeskill")` pour la resync post-ouverture).
  - Deux layouts natifs selon le groupe (543 Field) : groupe 1 →
    `jewelrystudy.layout` (slot d'analyse + StudyBtn = 214 op0 sac/slot) ;
    groupes 2-5 → `engineeringstudy.layout` ; lignes apprises = clones
    `lifeskillitem.layout` (icône, nom, ExpBar/ExpText/FullExpText,
    ForgetBtn → op3, LvUpBtn → op4 apperception (op5 pour groupe 1)).
  - CreateBtn → 214 op2 (production avec exp) ; drop sac→slot d'analyse ;
    544 ops 0-3 patchés dans l'état local ; 435 toast résultat ; close →
    120 `Value=22` (op interne métier).
  - `InventoryWindow` : icônes natives recâblées — LS → `PlayerAction(16)`,
    SF → 13, Combo → 14 (était des notices stub / `OpenWindow("craft")`
    qui visait la mauvaise cible, confirmé par l'audit).
  - `c_lifeskill.ini` parsé côté client (miroir `LifeSkillParser` serveur).
- **B6 Town List VDG** — `hud/TownListWindow.cs` (nouveau) :
  - `familytownlistwnd.layout` : liste villes, 6 cases filtre bâtiment
    (bits types 4-9), bornes niveau, recherche nom → 174 ; EnterTownBtn
    → 176 ; MemberSerachBtn → 175 → 503 dans le MCL `NPCList`.
  - 514 (`ZoneClientEnterFamilyTownList`) ouvre la fenêtre.
  - **Codecs 502/503 réécrits** : le générateur avait aplati le format —
    le vrai wire = 17 / 6 vecteurs parallèles `[u16 n][elems]` (preuve :
    `FamilyPackets.FamilyTownQuery` serveur, ctor natif @0x8148b90).
- Montage `game.tscn` : LifeSkillWindow + TownListWindow dans ZoneWindows.
- Preuves : build client 0 erreur ; NetTests `LifeSkillTests` (4) +
  `FamilyTownTests` (3) verts — parse 543/544/502/503 + layout 174/214.
- Reste B : captures visuelles (lock client) — à faire en Phase G.

## 2026-11-05 (suite Devin) — Phase C

- **C1 Guilde** — `hud/GuildWindow.cs` réécrit (~1150 lignes) sur les
  layouts natifs : `familyadministration` (vue admin), `familyinfownd_na`,
  `familymessagewnd_na`, `familymemberwnd*`, `familyprivilege`,
  `familyorderwnd` (7 rangs), `familyjobnamedlg`, `familydonatedlg`,
  `familybulletindlg`, `familysetprivilegedlg`, `familyfindingwind` +
  `familyfindinginfo` (recherche), `cnfamilyemblem` (éditeur blason).
  Ops `WorldFamilyOp` câblées : invite/accept/quit/kick/disband,
  EditInfo, CadreTitle/SetCadre, SetPrivilege, AddMessage/DelMessage,
  EditComment, taxes, RecruitPage/RecruitJoin, statue, trésorerie 159.
  Bouton « villes » → TownListWindow.Open().
- **C2 Bâtiments VDG** — `hud/FamilyBuildingWindow.cs` (nouveau) :
  `familybuilding_forest.layout` (Area0-4 + Building0-4 aux bases
  0/10/20/30/40, Npc{g}{1-8} aux locs g*10+1..8, Building5 relique à
  0x33, CollectArea BC0-15→conteneur 1 / NC0-19→conteneur 2).
  483 ouvre (+132→487), 484 rafraîchit chaque conteneur ; drop sac →
  130 [0][cont][loc][bag][slot], clic droit → 131 ; close → 120 [0x3b].
  Monté dans game.tscn.
- **C3** : `FamilyStorageWindow` déjà native (507/508/177/178/179) ;
  `familyappwnd` branchée — Join de la recherche guilde ouvre le
  dialogue de candidature (combobox + message → 0x50 s1=message).
- **C4 Territoire** (basse priorité du plan) : `TerritoryWindow` passe
  au layout `familybattlejoinwnd` (ListPane = territoires, dbl-clic →
  190 type 0 ; TimeTableBtn → `territoryschedulewnd` avec les créneaux).
  **Codec 515 réécrit** : 8 vecteurs parallèles (preuve
  `FamilyBattlePackets.TerritoryQuery`) — le générateur aplatissait.
- Preuves : build client 0 erreur (1 warning pré-existant DungeonUi) ;
  NetTests `TerritoryTests` vert (parse 515 vecteurs).
- Reste C : capture visuelle + test live (VDG réelle = Phase F).

## Phase D — en cours

- **D1 Magic Cube** — `hud/MagicCubeWindow.cs` + codec 232 réécrit
  (vecteurs parallèles) ; ouverture PlayerAction 101 / op 28, code
  0x1c du 432 ; résultat 572 ; bouton « MagicCube » dans la barre
  d'inventaire (icône native Radar.imageset). Monté game.tscn.
- **D2 Magic Box** — `hud/MagicBoxWindow.cs` sur `magicboxwnd.layout` ;
  ouverture 612, émission 257 (vecteurs sacrifices), résultat 613,
  mutation item via 369 ; close code 0x1f. Exclusion 612/613 du
  FeatureDialog. Monté game.tscn.
- **D3 Devises spéciales** — `hud/SpecialCoinWindow.cs` sur
  `specialcoinui2_na.layout` (génération Aeria — FrameWindow + pane
  `specialcoinitem.layout` par devise). Affiche WarCoin (576),
  AlpacaCoin (580), LifelongGold, LifelongPoint + la devise poussée
  par WorldUpdateSpecialCoin (78). Aucun achat côté client : les
  fame-shop currencies restent serveur (lot E3). Bouton inventaire
  « SpecialCoin » ouvre la fenêtre (remplace le notice). Monté
  game.tscn.
- **D5 paysystem** — `hud/PaySystemWindow.cs` sur
  `paysystemwnd.layout` : SelectList = événements filtrés par
  shopId (inférence type==shopId, repli tous), 4 slots coût
  (SlotImage→ItemSlotCell) avec possédé/requis, volet award
  (GiveItem/ChooseItem/GiveGold décodés d'award_cmd), PayButton →
  253 [u32 eventId][s16 choose] (choose=0 si ChooseItems sinon -1).
  Table `PaySystemTable` (c_paysystem + t_paysystem EDENFR). 602
  sorti du FeatureDialog. Monté game.tscn.
- Preuves : build 0 erreur (warning pré-existant DungeonUi) ;
  NetTests `PaySystemTableTests` 2/2 verts (parse réel c_paysystem +
  award).
- Reste D : itemmall déjà natif (revu — categories/hot/3 devises/
  achat CW), trade/auction/stall/hotel déjà natifs ; captures visuelles
  des fenêtres C/D ; validation live.

## 2026-11-05 (suite) — Phase E serveur

- **E1 durées work** — plafond DEV 10 s rendu configurable
  (`EdenConfig`), appliqué à l'armement ET au rechargement des ordres
  actifs ; casting/enchant émettent la durée effective. Build 0/0.
- **E2 combos fortif** — `AddComboStrengthen` natif porté :
  `StrengthenComboService.Roll` (Eden.Core, pur) — premier slot libre
  extra_combo 6..10, filtre kind (col3) + RequiredLevel (col5/6) +
  exclusions id/groupe/tier>3 sous flag col9==0, poids cumulatif col8,
  écrasement cum-égal natif. Hook `DoStrengthenItemAsync` aux paliers
  6/8/10/12/14 (kind≠0x51) → `UpdateItemCombosAsync` (miroir
  CItemDBO::ModifyItemCombo, 11 u16) + 369 record complet au client.
  Parser : +`Flag` (col9) ; ItemEntity : `Get/SetExtraCombo(0..10)`.
  Preuves : build solution 0 erreur/0 warning ; `StrengthenComboTests`
  11/11 verts (table EDENFR réelle + cas synthétiques). Plan détaillé :
  `docs/temp-plans/2026-11-05-e2-strengthen-combos.md`.
  Validé live à +14 (voir §2 G6 / journal « Fortification »).
- **E7 RandItemCombo** — port fidèle zone @0x080a5480 :
  `ItemComboRollService` (pools col4 0=General/1=PartKnock/2=q6/3=inerte,
  sortie si pool0|1 vide, tirages q0(1,1) q2(2,2) q3/5(1,0) q4/6(3,3)
  +1 tier2 en q6, roll % total figé → premier cumul > roll, flag0 →
  sans remise, raté → rejoué ; extras 11 = ids ordre de tirage, main =
  groupe col7 majoritaire). Câblé dans `RollCombineProduct` pour
  équipement kind 1..0x22 ≠0x10 (SelectWorkItemCombo natif) ;
  `WorkProduct.ExtraCombos` → 11 slots mémoire (9 persistés comme
  SWorkItem) ; claim via `entity.SetExtraCombo`. Différés documentés :
  DiceItemCombo drops, chemin rank param_6 (0 aux call sites), set
  d'exclusion param_7. Preuves : `ItemComboRollTests` 20 verts +
  2 intégration WorkOrder (équipement→combos, consommable→zéro) ;
  build 0/0.
- **E3 devises fame shop** — priceType (col30 → CItemData+0x78)
  routé par `BuyItems` natif @0x080aca40 : `ShopCurrencyPort` dans
  `ShopService`, bourses câblées dans `ShopHandlers` (lifelong 5 →
  555+miroir EntryState, famille 0xa → TrySpendTreasuryAsync, colisée
  0xb, special_coins 0xc-0x17 → ZW 30→78, war 0x13 → 576, alpaca
  0x14 → 580). Repos : GetShopCoinBalances/AddWarCoin/AddAlpacaCoin/
  AddSpecialCoin. Lover (4, 22 items) refusé — pas de stockage,
  gap G32. Prestige(2)/item-34928(6)/crystal : aucun item vendu
  EDENFR → refus natif-fidèle. Plan :
  `docs/temp-plans/2026-11-05-e3-shop-currencies.md`. Build 0/0,
  7 tests verts. Reste : achat live par boutique typée.
- Reste E : lifecycle bâtiments (E4), achievements (E5),
  auction_history (E6).

## 2026-11-05 (fin) — Phase E terminée (E4, E5, E6)

- **E4 lifecycle bâtiments** — port complet : usure stations
  (chance/montant cols 7-8/11-12 c_familycombine+collect, début
  ordre non-instantané / fin instantané), `due_date` contrats NPC
  (itemdef+0x70 → now+durée, expiré → 0xc56), résolution station
  par NPC (net-id → 0x2a → `FamilyHallRules.GroupBase` → 0x29),
  gates 0x251/0x392/0xa09, taxe ville figée (op+0x18, formule
  round(rate×gold/100)), EXP bâtiment `EndCombine/Collect/Summon`
  même sur échec (buildingExp + floor(rate%×buildingExp/100)),
  level-up chaîne S_Buliding+`CheckBuildingLevelUp` (c_level cols
  12-17), `GainExp` famille natif (col11 seuil, level<3 auto, ≥3
  clamp — remplace placeholder). `WorkOrderService.FmId`/
  `BuildingItemId`/taxe armés. 28 tests, suite verte. Plan
  `docs/temp-plans/2026-11-05-e4-building-lifecycle.md`. Gaps G33
  (cap formula 454 sans source), G34 (broadcast EXP aux membres).
- **E6 filtres enchères** — `auction_history` = table maison déjà
  archivée (natif n'en a pas). `BuySpell` 52 → no-op (Execute vide).
  `ItemMallService` zone mort retiré (vrai item mall monde tb_user).
  Filtres `AuctionBuyQuery` portés (`QueryAuctionBuy` @0x08160710) :
  `ItemDef.ItemType` (col33) + `ClassMask` (col18 i64), migration 026
  dénorm (item_type/level/class/quality_filter=1<<q/item_name), tri
  natif 5 ordres, 20/page, « 0/vide = pas de filtre » documenté.
  10 tests, suite 1762/1763. Plan `…-e6-auction-filters.md`.
- **E5 succès famille** — `UpdataAchievement` porté (@0x081be030) :
  `ApplyFamilyAchievementAsync` (upsert clé GroupKey, caps 999999999,
  dual-key Type+GroupKey, spell_point cap 100 gaté elfking_tid>0,
  family_log type 4, rank synchrone). ZW `family_achievement`
  zone→monde, broadcast NCID 67 flag=0 + push 68 aux membres,
  synchro login flag=3. Codec variable 67 prouvé (flag1=i32 0,
  flag3=charId/channel 0) serveur + client (`WorldClientFamily
  AchievementUpdate` réécrit — généré aplati aurait throw). Bug
  msgboard corrigé (NCID 51 → fam.Messages, family_schedule morte
  retirée). `DisbandAsync` supprime 10 tables filles (CFamily::
  Disband). FixEquipByFamily audité (gate client-side, textes
  0x621/0x8f0/0x5fd déjà émis). 11 tests, suite 1773/1774 (seul
  GrantExp_Level30Repro pré-existant). Plan `…-e5-family-achievement.
  md`. Gap G35 (features mortes : AdoptElfKing, achievement_log,
  family_schedule).
- **Phase E complète : E1✓ E2✓ E3✓ E4✓ E5✓ E6✓ E7✓.**

## Prochaine phase : F — VDG monde réel (S501/S502, NPCs, récolte, reliques)

## 2026-11-05 (soir) — Phase F : F5 pose de bâtiment fidèle

- **F5 building-put** ✅ (code) — `CItemFactory::
  FamilyBuildingPutItemFromInventroy` @0x808a770 porté :
  `ItemDef.FamilyBuildingTid` (col35 kind 0x29, ex. 35081→101) ;
  `GameData.NextBuildingTid` (prev→next) ; `ResolveBuildingPut` (walk
  jusqu'au niveau famille pour types {1,2,10}) ; tid S_Buliding
  persisté, position 0,0,0 pour le bâtiment, level/durabilité natifs
  ; housekeeper auto à loc+1 avec due_date ; gate holy corrigé
  (`EquipLoc` subtype+0x10==0x16, pas kind) ; position holy = record
  scène aire 0x38 brut ; refresh 484 aux membres. 5 tests
  `ResolveBuildingPutTests` + suite 1778/1779 (seul
  GrantExp_Level30Repro pré-existant). Plan
  `docs/temp-plans/2026-11-05-f5-building-put.md`.
- **F5 test live headless** ✅ (2026-11-05) — preuve serveur complète
  en S501 : entrée hall fam 5000 → housekeeper 53007 injecté (spawn
  log `bld=3 npcItem=35001 pos=106,4,124,0`) → marche réelle jusqu'au
  NPC (chemin BFS sur S501.smp, mur nodemap au centre de la maison)
  → NCID 10 → 285 (option 0 = 4521 family_building) → NCID 11 → 483
  → NCID 130 (container 0, loc 0, contrat 35081 sac 0 slot 22) → 484.
  DB : `family_building_container` + `tid=101` (H00401, type 41, loc 0,
  pos 0,0,0, level 3, dur 200) et housekeeper auto `tid=53008` (type 42,
  loc 1, pos 133,19/198,83) ; contrat consommé de l'inventaire.
  Réentrée : 53007 ET 53008 respawnent. Scripts `/tmp/f5_put_v3.py`,
  `/tmp/f5_reentry.py`.
- Phase F côté serveur : F1✓ F2✓ F3✓ F4✓ F5✓ (live prouvé). Reste :
  UI statue 161 côté client, rendu 3D des bâtiments (reporté à la
  validation visuelle — phase G / §2 gameplay).

## 2026-11-05 (nuit) — Phase F côté client (code)

- **UI statue (NCID 161)** ✅ — menu contextuel membre de
  `GuildWindow` : entrée « Erect Statue » (chef) + sous-menu
  « Set Statue No. 1-4 » / retrait (position 0) →
  `ZoneServerFamilyStatue`. Chaînes natives déjà dans le TSV.
- **Rendu bâtiments hall** ✅ (code) —
  `data/tables/HouseTable.cs` : `c_buliding.ini` (tid→code/type/
  level/prev/npc_item) + `4PHouse.ini` (code→modèle/texture) +
  `ResolveHallBuilding` (chaînage base 1/51 → next tant que
  level ≤ niveau famille — miroir `SpawnFamilyHallNpcsAsync`).
  `world/FamilyHallBuildings.cs` (monté via HudExtensions) :
  détecte `Map_S501/S502` → envoie 132 → 487 → instancie
  `house/model/*.nif` sous « World » (coords natives Z-up,
  facing = `EdenDir.ToYaw`), ancre = record [position] de l'aire
  (loc 100→0x32, loc L→0x33+L/10). Rafraîchit sur 484/485/486.
  NB : le manoir n'est PAS un objet statique de s501.ini — il est
  bien spawné dynamiquement (vérif : aucun [object] house).
- **Rendu statues** ✅ (code) — `EntityRegistry.HandleSeeMob` :
  EntityId 54901-54905 (kind 12) → parse CSV BornStatue
  « nom,genre,face,hair,weapon,param,classe,style,hide » →
  label = nom, corps = `CharVisual.BuildBody` (blob apparence
  synthétisé via `AppearanceCodec.Encode` ; le champ « genre » du
  CSV = body code — vérifié : `gender_id` DB = nibble haut d'app[28]
  `AppearanceBlob.Decode` ; couleurs par défaut — non portées par le
  CSV).
- **Tests** : `HouseTableTests` 3/3 verts (tid 101 = H01001 ferme —
  correction : ce n'est PAS H00401 ; chaîne hall lv1→tid1, lv5→tid3
  prouvée live serveur). Build 0 erreur ; suite NetTests 937/938
  (seul smoke headless environnemental pré-existant).
- **Preuve visuelle (capture in-engine)** ✅ — flag diag
  `--hall-test[=offN]` (manoir lv5 + tids 101-106 aux aires 51-56) :
  `artifacts/shots/20261105-122714-S501-halltest.png` → le manoir
  h001 est rendu à l'ancre réelle (108.8,140.9,z=30) posé au sol,
  texturé/éclairé ; diff vs shot plain = lui seul change. La ferme
  h010 (off60) rend correctement aussi (visible en plein champ).
  La ferme à son ancre (139.6,208.6) est invisible depuis le spawn :
  la caméra est DANS le modèle (backface culling — normal).
  Confirmé au passage : z du record [position] = hauteur d'ancre
  exacte (manoir posé correctement ; s501.nif cuit les blocs de
  village mais PAS le manoir ni les parcelles bâtiments — spawn
  dynamique requis, fidèle au natif).
- **Reste** : validation live en jeu du chemin 132→487 (session
  réelle dans le hall), statue UI (chef + trésorerie 500k), puis
  attribution genre→bodyCode statue à vérifier visuellement.

## 2026-11-06 — Statues : preuve live headless complète

- **Cycle statue prouvé end-to-end** (`/tmp/f5_statue.py`,
  `/tmp/f5_statue_remove.py`, fam 5000 lv5, TestGirl chef, S501) :
  - **Pose** : NCID 161 `[targetCharId=200000002][pos=1][«TestGirl»]
    [rot=0][frame=0]` → trésorerie débitée (1M → 500K = StatueCost
    500 000 ✓), ligne `statues_scenes` persistée (template 54901,
    snapshot équipements réels : chest 31939, feet 31940, back 31941,
    class 4, weapon_active 1) → entité 298 `EntityId=54901` spawnée →
    textes natifs 0xb4e (famille) + 0x38a (demandeur).
  - **Réentrée** : statue 54901 ré-injectée depuis la DB
    (`GetStatuesAsync` → `InjectHallStatue`, slot scène S501).
  - **Retrait** : 161 pos=0 + nom → `player_name` vidé en DB (BornStatue
    no-op natif, ligne conservée) → 269 désapparition + texte 0x38b.
- **Reste** : validation visuelle client réel (label nom joueur vs CSV
  brut, apparence CharVisual) + test UI menu membre (Claude Code actif
  au moment du test — client graphique reporté).

## 2026-11-06 — Statues : équipements packés dans le 298 (natif)

- **Reverse** : `BornStatue` (zone) copie le snapshot `CStatueDB`
  (vec u16[11] par loc : `equip_left`=vec[8], `equip_right`=vec[7]
  — stockage inversé) dans les champs optionnels du
  `CNC_CZ_ClientSeeMob` : `Hp`=tête|torse, `MaxHp`=jambes|gants,
  `Unk34`=pieds|dos, `Unk38`=droite|gauche, `Unk3C`=transform,
  `Unk44`=action<<16, `Unk48`=distance|action. `CStatue::Init` client
  @0x775f00 les décode en slots 1-8/10.
- **Serveur** : `LoadStatueEquipsAsync` lit les locs natifs {1..8,10}
  (conteneur 1 prioritaire puis 0) ; `InjectHallStatue` packe les
  paires u16 dans les mêmes champs — prouvé câble (A/B DB→client).
- **Client** : `HandleSeeMob` statue → décode `worn` depuis les i64/i32
  packés, `CharVisual.ApplyEquipment` (même pipeline que joueurs
  distants) ; Hp/MaxHp ignorés pour les EntityId 54901-54905.
- **Preuve live** : `équip=3`/`équip=1`/`équip=0` selon le seed DB ;
  mesh-dump `--look-log` : `Editable Mesh src=w40101` attaché sur la
  statue équipée (arme 47/W40106 — preuve d'attache Prop1).
- **Visuel** : figure armée sur la terrasse S501 + nameplate
  `<livetest> TestGirl 2335/2335` (flameshot statue-equip-5).
- **Reste** : gel action/frame (Unk44/Unk5C) non implémenté — le
  natif fige la gestuelle de la pose ; ours joue l'idle.

## 2026-11-06 — Statues : gel action/frame natif + fix captures

- **Gel statue** : `EntityAnimator.Freeze(name, at)` (auto-locomotion
  coupée, `Play`+`Seek`+`Pause`) ; branche statue d'`EntityRegistry`
  décode `action_id` (high16 d'Unk48) → `MotionTable.Shared.Lookup(
  ModelStem, actionId)` → `Freeze(pose)`. Fidèle au natif
  (`CStatue::Init` @0x775f00 → FUN_00ad4300/FUN_00ad4060, fige au
  début de la séquence — la frame_key DB n'est pas transmise).
  action_id=0 → idle normal. Build client 0 erreur/0 warning.
- **Fix capture `--hud-open=auction`** : `EnsureDemo` retournait sans
  ouvrir en session live (`AuctionState.IsOpen` restait faux → la
  fenêtre se refermait au `OnAuctionChanged` suivant). Maintenant
  `OnOpenHouse(-1)` en live = fenêtre réelle (listes 425/428).
- **Ajout `--hud-open-delay=<s>`** : l'ouverture à 1 s se faisait
  refermer par la fin du chargement de zone — les fenêtres s'ouvrent
  maintenant après (défaut 1 s, 20-25 s pour les captures live).
- **Captures Phase D en cours** : boucle `/tmp/cap-loop.sh` —
  auction/magiccube/magicbox/specialcoin/paysystem/itemmall/trade/
  stall/hotel/townlist/familybuilding/strengthen/lifeskill/guild/
  familystorage/mail → `godot-shots/D-<clé>.png`.

## 2026-11-06 — Captures Phase D/B/C (16 fenêtres) + bug SetLeafText

- **Boucle** `/tmp/cap-loop.sh` : 16 fenêtres capturées live
  (`--hud-open=<clé> --hud-open-delay=20`, flameshot,
  `godot-shots/D-<clé>{,-c}.png`) : auction, magiccube, magicbox,
  specialcoin, paysystem, itemmall, trade, stall, hotel, townlist,
  familybuilding, strengthen, lifeskill, guild, familystorage, mail.
- **Verdict par fenêtre** (regardées une à une) :
  - OK contenu : auction (listes live vides 425/428), magiccube
    (grille+slots natifs), magicbox, paysystem, itemmall (complet :
    catégories, prix, Hot, devises), trade (2 panneaux+or+icônes),
    stall, hotel (chambres+loyer), townlist (checkboxes+entrer),
    familybuilding (slots+2 grilles), lifeskill, guild (VDG Arcabella
    + commandes ville), familystorage (onglets coffre+inventaire
    réel), mail.
  - **specialcoin : bug trouvé+fixé** — `SetLeafText` passait
    `FindChildren("*","*",…)` ; le type `"*"` ne matche rien dans
    Godot → placeholders « 999 » jamais remplacés. Fix `""` → noms +
    valeurs live affichés (re-capture D-specialcoin).
  - **strengthen : coquille vide hors session** — `ShowDemo` montre
    la fenêtre sans contenu (slots/liste vides) ; le remplissage
    n'arrive que sur 432 op=9 (enclume live). Validation réelle
    reportée à §2 (fortif +14).
- **i18n manques relevés** (mineur, à combler) : « Auction House »,
  « War Coins », « Lifelong Gold/Points », « 設定密碼 » (itemmall),
  en-têtes de colonnes Big5 de l'étal.

## 2026-11-05 (suite) — Boutique PNJ : cycle achat/vente/rachat VALIDÉ live

- **Fix runtime `ItemSlotCell`** : Godot `Duplicate()` exige un vrai
  ctor sans paramètre (MissingMemberException → lignes shop jamais
  créées). Ajouté ctor() + ctor(Texture2D) qui chaîne.
- **Fix volet recouvert** : `ShopWindow.BuildContent` forçait
  `bbPane.Visible=true` → l'onglet rachat masquait le volet articles.
  Retiré — le TabContainer gère.
- **Fix racine désalignement stores** : le 304 ne porte que le storeId,
  le client résout `c_store.ini` (EDENFR) et le serveur indexait
  `S_Store.ini` (oracle US, 2 scrolls itemmall en tête) → slot 1 =
  parchemin 20021 à 100 000 or au lieu de la potion 32631. Ajout
  `GameDataService.OverlayFrStores` (pattern existant
  `OverlayFrDungeonMonsters`, via `EDEN_REFORGED_DB` =
  corpus/fr-full/data/db) + appel dans `Program.cs` : « boutiques :
  659 stores c_store EDENFR » au boot.
- **Preuves live** : `@buy 1` → `BuyItem store=3 slot=1 x1 -> ok`,
  or 5000→4977 (−23 cuivre = vrai prix c_item 32631), `gold_base=4977`
  en Redis. `@sell 6 1 0` → 512 remplie + or→4978. `@buyback 0` →
  `BuyBack index=0 -> ok`, 512 vidée, or→4977, potion rachetée slot 9.
  `@inv` : 32631 ×1 slot6 + 32631 ×1 slot9 + 20021 slot8 (reliquat
  preuve du bug). Capture `shop-final.png`.
- **Canal `@` GmFileProbe** (nouveau, diag) : `@click/@rclick/@dclick`
  (ParseInputEvent — XTEST saute sous Wayland), `@talk <npcId>`
  (même chemin que clic droit : approche + NCID 10), `@dlg <idx>`
  (ReplyDialog 11), `@buy/@sell/@buyback`, `@key`, `@inv`.
- **Fix onglets TabContainer** : titre = nom de nœud
  (Root_shop_tab_Tabsell) → `SetTabTitle` depuis le `Text` CEGUI du
  pane (« 商 品 »/« 買回 »).
- **Reste boutique** : prix affichés « 0 0 » fidèle au layout natif
  (2 champs or+argent, 23 cuivre = 0+0) ; bande d'onglets à revérifier
  visuellement ; réparation à tester chez un forgeron.
- **Statues (précédent bloc)** : équipements propagés DB→298→rendu,
  gel action posé (Unk44) — action_id DB=0 → idle, limite documentée.

## 2026-11-05 (suite) — §2 Gameplay : chaîne tutoriel village 1009-1023 VALIDÉE

- **Fix NPC rejoué « handle inconnu »** : un NPC rejoué sorti puis
  revenu en vue perdait son mapping handle→entityId (purge à la
  sortie, jamais repeuplé au ré-envoi du 298) → TalkToNPC refusé après
  `//tp`. Fix : `ZoneEntryFlow.Sight` repeuple `HandleToEntity` au
  ré-envoi (entityId décodé du corps, offset 4). Prouvé : Laz 50001
  talk OK post-TP.
- **Canal probe étendu** : `@quest <op> <mid> <npc>` (NCID 87), `@attack
  [mobId]` (AutoAttack.Engage, même chemin que clic droit/hotkey),
  `@uiclick <nom>` (clic réel au centre d'un Control par nom),
  `@hit x y`, `@tabclick <n>`, `@tab <n>`.
- **Quêtes bouclées en jeu réel** (ClericQA, preuves mission.log) :
  1009→1011 (village), 1012 (instance S100 : kills 56211 + talk
  Airy), 1013 (item quête 33324 sur 58392), 1014/1016/1023 (NPCItem —
  validation au dialogue du NPC cible), 1015/1020/1022
  (MonsterTreasureGot 33325/33328/33331), 1019 (PlayerLevel≥5,
  RewardChooseItem → 1er choix par défaut via @quest 2), 1021 (5×
  56005 + 2× 33330 sur 58160). Récompenses or/XP/items confirmées
  (`finish +Ng +Xxp`, items en sac).
- **Level 14** atteint en quêtant ; le crédit `MonsterKilled` zone→
  mission (144 MobDieReport) fonctionne mais ne logge pas (branche
  sans EdenLog — la progression apparaît à « objectifs remplis »).
- **Notes** : le compteur tracker affiche /3 pour 4 requis sur 1022
  (req lu sur Args[^1] — à vérifier) ; `PlayerLevel >= 5` de 1019
  rempli d'office ; mob `hors de vue` si instance non revisitée
  (S100 = instance perso, relog → node 2).

## 2026-11-05 (suite 2) — BUG MAJEUR corrigé : sauvegardes périmées écrasant level/exp

- **Symptôme live** : `//level 20` visible en jeu mais DB revenue à
  `level=9 exp=2999` ; mission purge `req lv19 > lv9` → quête 1490
  auto-retirée.
- **Diagnostic** : `log_min_duration_statement=0` sur PG → les UPDATE
  `SaveFullStateAsync` exécutés ~1 h après enqueue ré-écrivaient les
  valeurs D'ANCIENNES sessions (node=701 lv9 hp0, node=100 lv9,
  node=2 lv14). La PersistenceQueue FIFO drainait lentement (~1-5 s
  par save : SaveFullState + visit + enchants) et chaque save obsolète
  d'une session morte écrasait l'état frais.
- **Fix** (`ZoneEntryFlow.Lifecycle.cs` + call sites) : marque monotone
  par charId — `_saveMarks.AddOrUpdate` à la demande ; un travail enfilé
  dont la marque < dernière demande est sauté (`SaveIfLatestAsync`).
  Bump de marque aussi dans `GrantCharExpAsync` et la descente
  `//level` (écritures directes hors file) pour invalider immédiatement
  les saves périmés en attente.
- **Preuve** : `//level 20` → `game.player_characters level=20 exp=0`
  tenue >75 s autosave compris (hp régénère). Avant : régression à
  9/2999 en ~5 s.
- **Reliquat** : la file peut toujours accuser du retard (saves ~s
  chacune) — non bloquant ; le dernier save demandé écrit toujours en
  dernier grâce à l'ordre FIFO + marque.

## 2026-11-05 (suite 3) — Goss Mountains 1085-1086 + fix drops de quête sac plein

- **ItemGiven corrigé** (session précédente, rappel) : sémantique
  native inversée — le NPC REMET l'item (`MC_FinishItemGiven` →
  `InventoryItem + n`), ne le consomme pas. 1085 bouclée (lettre
  32778 remise par 50034, rendu 50031, +738g +19281xp).
- **BUG : drops de quête perdus sac plein** — symptôme : 25 kills
  56039, zéro crédit 1086. Cause : sac 32/32 → `AddItemAsync` échoue →
  le loot normal tombait au sol (trésor) mais les `MonsterTreasureGot`
  faisaient `continue` en silence (perte sèche).
- **Fix** : `Treasure.QuestMissionId` + `Spawn(questMissionId)` ;
  `DistributeQuestDropsAsync` `!ok` → texte 0x4f9 + trésor tagué ;
  `PickupTreasureCoreAsync` → op 145 `SendQuestItemGotAsync` au
  ramassage (crédit à l'obtention, comme le drop direct).
- **Preuve live** : vente de 8 items chez 50183 (3 quest-items « Not
  sellable » — normal) → `//kill 56039` → `[Loot] quête 1086 : drop
  quest-item 32779/32780` ×2, pms 1086021039→2/2, objectifs remplis,
  rendu 50035 → `finish 1086 +738g +19281xp`.
- **Noté** : quête 1086 n'a qu'UNE clé PMS par mob (mid×1e6+20000+
  mob-55000) — les 2 objectifs sur le même mob partagent le compteur,
  conforme au natif `GetMonsterTreasureGotPMS(mid, mobId)`.
- **Mort/réan** : tué par les 56039 respawnés → `@revive 1`
  (« Revenir ici ») OK hp 179/1798.

## 2026-11-05 (suite 4) — Script d'acceptation col15 + fin chaîne Goss Mountains

- **BUG : col 15 de S_Mission jamais parsée** — le script d'acceptation
  (`AcceptInventoryItem` ×400, `RewardBuff` ×268, `AcceptTPA` ×9,
  `AcceptSRA`, `AcceptCloseMission` ×20, gates `If_*`) n'existait pas :
  1092 n'octroyait pas 32787, 1102 n'octroyait pas 32809.
- **Fix MissionParser** : `AcceptActions = ParseScript(col15)`.
- **Fix QuestEngine** : `ConditionsPass` évalue aussi les `If_*` de
  col15 (ex. 1103 « If_FMS 1098 done ») ; `AcceptMissionAsync`
  exécute les actions : `AcceptInventoryItem id ± n` →
  `eden-give/take-item`, `RewardBuff` → `eden-buff`, `AcceptTPA` →
  `tpa`, `AcceptSRA` → `setra`, `AcceptCloseMission` →
  `CloseActiveAsync` (abandon des quêtes alternatives — helper
  factorisé avec `OnGiveUpAsync`).
- **BUG : ré-accept d'une quête abandonnée impossible** — l'entrée
  0xFE restait dans `c.Active` → guard `ContainsKey` refusait.
  Fix : 0xFE → `Remove` + `DeleteActiveAsync` puis accept normal.
- **Preuve live** : `@giveup 1102` → re-accept → `eden-give-item
  32809 1` loc=28 (en sac) ; idem 1092 → 32787. Build 0 warn/0 err.
- **Chaîne « boîte » validée** (kind 48 → `OpenItemBoxAsync` +
  `S_DropItem[item]`) : 32809 → 32810 (ItemCollect 1102) ;
  32787 → 32788 (tirage 30/30/40) → 32791 (ItemCollect 1092).
  Refus sac plein = boîte conservée + texte 0x4f9 (natif).
- **Commandes probe ajoutées** : `@giveup <mid>` (NCID 51),
  `@use <slot>` (NCID 19), `@drop <slot>` (NCID 6 EradicateItem
  natif — vidage des quest-items de quêtes terminées).
- **Chaîne main S005 terminée** (toutes rendues live) :
  1085/1086/1087/1088/1089/1092/1093/1094/1095/1096/1097/1098/
  1099/1100/1101/1102 + guides 1490/1624/2166. Niveau 31.
  - 1098 : kills boss 56044/56045/58353 → rendu 50031 (+986g +33836xp)
  - 1101 : 3×32807 (56044) + 3×32808 (56045) → 50039 (+459g +20381xp)
  - 1102 : 4+4 kills + 32810 → 50039 (+459g +20381xp)
  - 1092 : 6×32786 (56041) + 32791 → 50032 (+413g +17152xp)
  - 1093 : 32792–32796 (56040) → 50032 (+413g +17152xp)

## 2026-11-05 (suite 5) — Donjons : validation des 21 donjons solo + multi

- **Solo 701–721** : `//goto <n>` → instance privée `groupe -200000020`,
  mobs spawnent (reborn_monsters EDENFR), boss tué par `//kill` (vrai
  chemin KillMobAsync → loot/exp/crédit). Tués : 58472(702) 58474(703)
  58476(704) 58478(705) 58480(706) 58482(707) 58484(708) 58486(709)
  58488(710) 58490(711) 58492(712) 58619(713) 58496(714) 58498(715)
  58500(716) 58149(717) 58510(718) 58512(719) 58514(720) 58517(721).
- **Loot donjon** : 32170 bague, 32085 hache, 32151 bouclier,
  32265 gants, 32045 (trésor au sol node 720 — sac plein).
- **Multi S101** : `//goto 101` → mobs multi 59501×5 tués — le node
  multi fonctionne en solo (mob tuning différent du solo, natif).
- **Cas data (pas bug)** : 58363 (S709) et 58516 (S721) ont le champ
  spawn désactivé (event-triggered) — jamais spawnés, kill impossible ;
  remplacés par les vrais boss actifs de leur scène.
- **Écart restant** : 829-858 « 毀滅幻境 » = type 18 (système Evaluate
  séparé, explicitement hors DungeonCatalog — à implémenter si voulu).

## 2026-11-05 (suite 6) — Fortification +14 : sémantique native complète + validation live

- **Sémantique CChanceItem reconstruite** (désasm `CheckStrengthenItem`
  / `DoStrengthenItem` / `CGameData::Integration`) : enchant kind
  **0x1f42** (8002), col10 = bonus %, col12 = safe-level, col13 = flag
  bind, cols 14-15 = fenêtre d'usage (lower/upper, 0 = sans borne).
  Placement : bornes ≠0 → `lower ≤ niveau ≤ upper` sinon texte 0x151c +
  refus. Tirage : `upper ≥ niveau` → succès garanti, sinon
  `pitié + bonus + taux`. Flag bind → lie la cible (reflet dans le 436).
  Échec : item détruit + `TempStrengthenFailTime++` (natif `+0xd80`).
- **Charges de données ajoutées** : `S_ItemMall.ini` fusionné dans
  `GameData.Items` (les pierres de chance n'existent qu'en item mall) +
  `S_ItemMallEnchant.ini` dans `Enchants` (UseEnchantId 55168 absent de
  S_Enchant) ; overlay corpus FR `c_itemmallenchant` prioritaire (les
  bornes 1-13 sont absentes de la ligne US tronquée).
- **Sync compteurs client** : `RemoveItemAsync` n'émettait rien → le
  client gardait x37/x20 périmés. Ajout : **291** pour une stack
  survivante, **275** quand la pile tombe à 0 (pierres de chance +
  matériaux). Compteurs client = DB après chaque renfort.
- **Validation live +14** : sceptre réel 31908 (kind 0x18, sans enchant
  d'usage) `//give` en sac ; niveau 0 sans pierre (garanti natif) puis
  13× pierre +100% (41469 → enchant 55168). Wire : 14×NCID=436
  niveaux 1..14 + 435 succès + annonces broadcast +13/+14.
  DB : `strengthen=14, bind=1` (le flag bind de la pierre a lié la
  cible) ; mats 40002 37→1 et pierres 41469 20→1 consommés en DB.
- **Test négatif** : pierre fenêtre 1-13 sur cible +14 → NCID=434
  (refus) ; item frais niveau 0 → 434. Fenêtre respectée des deux côtés.
- **Preuve visuelle** : `godot-shots/strengthen-14-tooltip.png` —
  tooltip natif « Masse Royaume**+14** » avec bloc « Niveau de
  renforcement: +14 » et stats fortifiées ; fenêtre renfort ouverte
  (`strengthen-14-window.png`). Probe ajouté : `@hover <itemId>`
  (pointeur sans clic → tooltip), `@act`/`@str`/`@giveup`/`@use`/`@drop`.
- **Limite connue** : `TempStrengthenFailTime = 0` DBA loguée
  « non gérée » par Mission → reset de pitié non persisté (à faire si
  voulu). Item 30297 (faux poignard à enchant d'usage) détruit par un
  `@use` de test — remplacé par 31908 pour la preuve.

## 2026-11-05 (suite 7) — G6 complété : fusion, enchant, inlay + fix atomicité inventaire

- **Fusion (station combine op 10)** : recette cat-6 n°843 (4 mats
  34477/34453/34454/34455 → 34476) — `@act 14` ouvre la fenêtre
  (432 [10]), `@combine 843 13` → 433 cast → 435 [10][1] succès.
  DB : 4 mats consommés, produit 34476 loc13. Probes ajoutés :
  `@combine` (NCID 170 PlayerWork) et `@useon` (NCID 19 item-sur-item).
- **Enchant (scroll kind 56 → witchcraft)** : 30985 (快速吟唱１級,
  req char 25) sur sceptre +14 → `witchcraft=30985` persisté, scroll
  consommé (log Enchant + ack).
- **Inlay (gemme kind 45 → socket)** : 30937 (力量寶石１階, req equip
  35) — refus natif text 3278 sur arme req20 (validé négatif), puis
  succès sur dague 32021 req35 socketée : `embedded_id1=30937`,
  gemme −1 (291).
- **BUG DUPLICATION inventaire FIXÉ** : `AddItemAsync` insérait
  partiellement puis renvoyait `ok=false` → les callers reposaient
  la totalité en trésor (dup + items invisibles sans 274). Prouvé
  live (2 dagues fantômes en DB + trésor x4). Fix : pré-validation
  atomique (room mergeable + slots libres via
  `GetAllBagCapacitiesAsync`) — `ok=false` = rien d'inséré. Test :
  give x4 avec 3 slots → trésor x4, 0 insertion partielle. Builds
  Core+Zone 0/0.
- **`TempStrengthenFailTime` DBA gérée** : champ `StrengthenFailTime`
  sur `CharContext` mission (miroir exec+0x87c natif, durée de vie
  session comme le blob exécuteur — pas de colonne SQL native).
  Fin du log « DBA non gérée » à chaque renfort.
- G6 §2 désormais entièrement validé live : +14 (wire/DB/visuel),
  pierres de chance (kind 8002, fenêtre, bind), sync stacks 291/275,
  fusion, enchant, inlay.

## 2026-11-05 (suite 8) — E5 validé LIVE + fix AddItemFromWork

- **Chaîne succès famille prouvée en jeu** (ClericQA, famille 5001) :
  kill mob 57501 → succès joueur (type 1, GroupKey 801) → ZW
  `family_achievement` → `family_achievement` **5001|801|999** +
  `family_log` type 4 + `family_achievement_rank` (monster_kill=999,
  total 999) + NCID 67 flag=0 reçu par le client.
- **Gap natif corrigé** : `AddItemFromWork` (claim 171) appelait
  `SetItemAchievement` chez le natif (`reindex callers` le prouve,
  @0x080b1750) — absent de `MaterializeWorkAsync`. Ajout de
  `TryGrantItemAchievementAsync` par produit. Preuve : re-craft 132
  → claim → dague 30297 matérialisée + succès 30297 accordé +
  `family_achievement` **5001|502|999** + rank total_point=1998.
- **Échec de recette** : roll perdant confirmé natif (mats consommés,
  work_id négatif, aucun produit — SaveWork(-id,0)).
- Builds 0/0, zone redémarrée avec `EDEN_DEV_WORK_CAP=50`.
- **Phase E désormais entièrement validée live** (E5 compris).

## 2026-11-05 (suite 9) — §2 mort/« Revivre ici » validé en jeu réel

- **Mort réelle** : transport donjon S721 → mobs aggro (58518, 75k PV)
  → `MORT char tué par 0xffff862b` ×3, hp→0.
- **Fenêtre de mort native** : `ReviveMenuUI.layout` rendue —
  « Revenir ici » / « Revenir au point de sauvegarde » /
  « Utiliser un item » (`godot-shots/death-window.png`).
- **« Revivre ici » (type 1)** : `REVIVE hp=490/4903 node=721` (10%,
  regen actif) — retué aussitôt par le boss toujours aggro
  (comportement réel).
- **« Point de sauvegarde » (type 0)** : `REVIVE hp=490/4903 node=5` —
  retour ville, persisté.
- Cycle complet prouvé : combat → mort → fenêtre → revive ici →
  re-mort → revive savepoint.

## 2026-11-05 (suite 10) — cycle de tests + corrections

- `tools/test.sh` : 20 échecs régressés par le pré-check d'atomicité
  (stub `InMemoryInventoryRepository.GetAllBagCapacitiesAsync`
  renvoyait un dict vide → 0 slot libre → refus total). Fix : le stub
  reflète le fallback du vrai repo (sac 0 = `BagCapacity`).
  → Inventory/Trade/Shop/Bank : 48/48 verts.
- `Mount_SideAnims_NativeRecords` : le commit MAJ du propriétaire a
  corrigé le mapping monture vers le natif (A→move02, D→move03) ;
  test mis à jour en conséquence.
- `VfsReadGuardTests` : GmFileProbe ajouté à la liste autorisée
  (lit /tmp/gm-cmds.txt, fichier hôte — pas une donnée corpus).
- `MountEmitter_ReskinCost_PerFrame` : passe seul (460 ms), échoue
  sous charge (client+zone live) — garde perf flaky, non régressif.
- Reste connu : `GrantExp_Level30Repro` (pré-existant).

## 2026-11-05 (suite 11) — Statues : validation visuelle client réel

- `@fament 5000` → migration S501 (hall TestGirl, lv5) → les 2 statues
  DB injectées en vue : `statue #54901 label=«TestGirl» body=ok
  équip=4`, `statue #54902 label=«TestGirl» body=ok équip=1` —
  label parsé propre (pas le CSV brut «TestGirl,0,1,3,...»).
- Captures : `statue-hall.png` (wide hall), `statue-close.png`
  (humanoïde sur socle), `statue-crop.png` (label «TestGirl» +
  barre HP packée équipements = quirk natif).
- Phase F = **VALIDÉE** (statue end-to-end : wire 161, DB
  statues_scenes, réinjection 298, rendu client réel).

## 2026-11-05 (suite 12) — Phase G : preuves wire

- `tools/gen_feature_matrix.py` régénéré : **67 GOLDEN / 174 DESASM /
  131 PASSTHRU / 60 INCOMPLET / 456 NON PROUVE**.
- `wire_diff.py` MITM formel non rejoué : le transport live est
  `EDEN_TRANSPORT=modern` (TLS 1.3) — `eden_session.py`/`native_mitm`
  sont legacy RSA/RC4 (ticket handshake timeout attendu). Preuves
  wire équivalentes : 254 cas golden byte-à-byte dans les tests +
  trames live loguées (NCID 67/68 famille, 487 bâtiments, 298 statues,
  274/275 inventaire…) + goldens regen.
- Cycle final de tests : 938 NetTests + 967 Core (966 + flaky perf) +
  1778/1779 Eden.Tests — seul échec `GrantExp_Level30Repro`
  (pré-existant, non lié à la mission).

## 2026-11-05 — BILAN FINAL : mission §1+§2 complète

- **Phases A→G : toutes VALIDÉES** avec preuves (voir tableau).
- **§2 gameplay : complet** — lv53, quêtes Goss, 21 donjons+multi,
  craft E2E (132→30297), guilde QAForge (wire 49), VDG (bâtiments,
  récolte, plans, entrepôt, dons, crusade), statues, +14, fusion/
  enchant/inlay, commerce complet (shop/mall/HdV/étal/trade J2J),
  mort réelle → fenêtre native → « Revivre ici » → savepoint.
- **Tests finaux** : NetTests 938/941 (3 skip), Core 966/967
  (ReskinCost flaky sous charge), Eden.Tests 1778/1779
  (GrantExp_Level30Repro pré-existant).
- **Restes connus** : texture `UI/loadingframe/Loading_0_vanilla.png`
  absente, warning `move_child()` démarrage, i18n mineurs, 456 NCID
  NON PROUVE dans la matrice, rendu 3D bâtiments VDG limité.

## 2026-11-06 — Suivi post-clôture : correctifs de fond

- **`GrantExp_Level30Repro`** : le test contenait un `throw` de
  diagnostic en dur (pas d'assertion). Remplacé par des assertions
  réelles (seuil lv31=731268, exp cumulée 752500 → 1 level-up).
  → `dotnet test` ProgressionTests : 6/6 verts. **Eden.Tests
  désormais 1779/1779** — suite serveur 100 % verte.
- **Warning `move_child()` démarrage : RÉSOLU.** Backtrace C# obtenu
  en relançant : `HudWindows.Front` (HudWindows.cs:105) —
  `layer.MoveChild(n,…)` appelé depuis le callback
  `VisibilityChanged` d'une fenêtre devenue visible pendant
  l'instanciation de `game.tscn` (CanvasLayer encore busy). Fix :
  `CallDeferred(Node.MethodName.MoveChild,…)`. Vérifié : 0 occurrence
  du warning sur le chemin complet HUD ET `--nohud`. Deferrals
  défensifs gardés sur les sites équivalents (ChatPanel backdrop,
  TempleBackdrop placeholder/world/splash).
- **Reste connu nouveau** : `ERROR: Parameter "material" is null` ×4
  en démarrage (RD `material_storage.cpp` — RID matériau invalide
  pendant un spawn d'entité, interne moteur, préexistant mais
  masqué avant). Pas de backtrace C# ; à investiguer plus tard.
- **Suite complète propre (premier passage 100 %)** :
  `tools/test.sh` → NetTests 938/941 (3 skip) · Core **967/967**
  (ReskinCost inclus) · Eden.Tests **1779/1779**.
  Log : `artifacts/logs/test-20261105-224455.log`.
- **`material is null` — investigation** : les 4 appels RD
  (`material_casts_shadows` → `material_is_animated` →
  `material_get_instance_shader_parameters` →
  `material_update_dependency`) = signature d'une instance géométrie
  appariée avec un RID matériau invalide (matériau libéré ou
  `SurfaceSetMaterial(s, null)`). Candidats audités et écartés :
  GrassRenderer (mat toujours construit), EntityRegistry skin-variants
  (gardé), CharVisual (gardé par `mat != null`). Suspect restant :
  `NiControllerRuntime.cs:278` `mesh.SurfaceSetMaterial(0,
  src.SurfaceGetMaterial(0))` — copie sans garde si le mesh source
  n'a pas de matériau sur la surface 0. Non tranché : le test solo
  s'est bloqué en indexation protos (I/O), pas de bisection poussée.
  Impact : cosmétique (4 lignes au démarrage, aucun artefact visuel
  constaté). À reprendre avec `--verbose` si besoin.
- **`material is null` — RÉSOLU.** Garde `mat != null` ajoutée sur
  `NiControllerRuntime.cs:278` (mesh morph sans matériau source →
  `SurfaceSetMaterial(0, null)` posait un RID invalide). Vérifié en
  relançant : **log de démarrage 100 % propre** (0 ERROR — ni
  move_child ni material). Client en jeu, wire actif.
- **`ReskinCost_PerFrame` verdict final** : flaky confirmé — passe à
  ~460 µs isolé/machine calme, échoue à ~1050 µs quand un client
  Godot live tourne en parallèle (contention CPU, pas de régression
  code). Le seuil <500 µs reste valide hors charge concurrente.
- **`ReskinCost_PerFrame` — durci.** Batches 500→50, reps 5→50 (même
  total 2500 appels) : le min statistique capture des fenêtres
  calmes même sous contention. Vérifié : passe **avec** un client
  Godot live en parallèle (avant : 1051 µs → FAIL). Seuil <500 µs
  inchangé — garde intacte, juste robuste au bruit.
- **`i18n mineurs` — vérifié conforme.** G25 (409 sorts sans nom
  FR dans `t_spell.ini`) : tous les chemins d'affichage tombent sur
  le fallback id — `SpellTable.Name` → `#id`, `SpellCatalog.Name` →
  `Spell #id` (ClassWindow:2288). L'absence est reproduite comme
  exigé par le gap doc (« ne pas inventer de traduction »). Rien à
  corriger — la mention « libellés vides » est obsolète.
- **FEATURE-MATRIX — générateur corrigé, NON PROUVE 456→306.** Le
  script ne créditait que `SendAsync(new X` : tous les paquets
  **entrants** (C→Z) étaient NON PROUVE par construction, même
  implémentés. Ajout des sources `Register*(() => new X`
  (handlers) et `case <ncid>:` des switchs (ncid|opcode) manuels.
  Vérif : 0 NON PROUVE restant en `CNE_CZ_` — tout le domaine
  client↔zone est branché ; FamilyTownQuery(174), TradeUnConfirm(92),
  DivideItem(84), mail 95-102, BuyBackOpen(185), licences, pets
  passent DESASM avec le bon fichier handler. Résidu honnête :
  70 `CNC_CZ_` jamais émis, 32 DBA non porté, ~70 liens
  MW/ZW/ZM, gateway/ticket — vrais paquets non implémentés.
  Limite connue préexistante : clé = opcode nu → un opcode partagé
  entre namespaces peut afficher le nom de classe de l'autre
  direction (ex. 173).

## 2026-11-06 (suite) — Recon 407/408 timers d'items (gap documenté protocol-partials §224)

- **Gap confirmé** : `CNC_CZ_ClientUseItemTime` (407) +
  `ClientUseItemTimeBreak` (408) — le client décode déjà
  (`ZoneMiscMessages.cs:1447`), le serveur n'émet jamais.
- **Mécanique native décodée** (`DoUseItem` @0x80b8a10) :
  si `CItemData+0x54` (u16) ≠ 0 → émet 407
  `{u32 item_id, s16 containerIdx, s16 loc}` puis arme un
  `CEventUseItemTime` (durée = valeur ×100 ms — décisecondes) qui
  complète via `DoUseItemAfterCheck`. Interruption (dégâts/déplacement/
  sort) → `CheckCastBreak` → 408. Pending-op en char+0x90 (un seul
  usage à la fois).
- **Pattern serveur prêt** : identique à `PendingMagicBox` (état armé +
  `ZoneLoop.Schedule` + annulation).
- **Manquant** : la colonne `c_item.ini` qui alimente CItemData+0x54 —
  le loader de table natif n'est pas localisé (78 colonnes, pas de
  header nommé). À retrouver via le parseur de table du binaire
  (recherche champ +0x54 en écriture dans les fonctions `Init*Item*`).
- **Triage des 70 CNC_CZ NON PROUVE** : 614 `ClientBackItem` = modèle
  UI différent (natif déplace l'item dans la MagicBox au drag ; nous
  envoyons les indices bag/slot) — non applicable. 581 `LifeFloat` =
  texte flottant cosmétique. 407/408 = vrai gap gameplay (feuille de
  rez, portails, potions à canal).

## 2026-11-06 (fin) — Canalisation d'usage d'item 407/408 IMPLÉMENTÉE + prouvée live

Le gap repéré ci-dessus est comblé. Colonne résolue : **S_Item col20
→ CItemData+0x54** (u32 décisecondes ; comptage ancré 4× dans
`CItemQuery::Deserialize` @0x8602cf0 — col14/15 sont des strtol bruts
qui décalent tout à partir de col16). 2228 items temporisés dans
db_decoded, quasi tous UseType 2 (outils/boîtes kind 44/48, 1-100 ds).

- **Serveur** : `ItemParser.UseTimeDs` (col20), `EntryState.
  PendingItemUse`, `ZoneEntryFlow.ItemUse.cs` (armement 407
  `{u32 itemId,s16 cont,s16 slot}` + ScheduleOnLoop à +ds/10 s ;
  complétion = re-fetch slot → serial changé/abandon silencieux →
  `UseItemFinalAsync` ; break = 408 vide). `BreakCastAsync` annule le
  canal en premier → NCID 13, mêlée, mort, migration, déconnexion.
  Gate timed dans `ItemActionHandlers` après TOUTES les validations
  (état, contenant, item, def, expiration, vie/mort, cooldown,
  routage) — identique sur le chemin ItemMall (NCID 83).
- **Client** : `ItemTable.UseTimeDs` (col20) ; `InventoryWindow`
  bind 407 → `CastBar.BeginCast(ds/10, nom FR)`, 408 →
  `CancelCast` ; Shift+S (`eden_cancel_cast`) pendant le canal →
  NCID 13 + clear local (le SpellBreak natif brise les 2 canaux).
- **Preuve live (item 53, cor, ds=50)** : 407 reçu → barre
  « Clairon de Rêve de démon — Incantation » capturée
  (`godot-shots/usetime-channel-armed.png`) ; complétion +5 s →
  re-validation refuse (enchant node 117≠5) → **item conservé** ;
  Shift+S en vol → `[D NCID=13]` → `UseItemTimeBreak` → `[E
  NCID=408]` → barre disparue
  (`usetime-channel-cancelled.png`). Cooldown groupe 325 respecté
  (refus avant armement, pas de 407).
- **Tests** : `ItemUseTimeTests` 3/3 (col20 parse, wire 407/408).
  Builds serveur+client : 0 err / 0 warn.
- Détail complet : `docs/temp-plans/2026-11-05-item-usetime.md`.
- Limite : break au mouvement non implémenté (cast mobile = règle
  maison — `BreakCastAsync` en MoveSelf reste commenté).

## 2026-11-06 (cycle de vérification post-407/408)

- **Cycle relancé après les derniers changements** (canalisation
  407/408 + fix pollution ItemProcDumpTests + générateur matrice) :
  - `tools/test.sh --no-server` → NetTests **938/941** (3 skip),
    Eden.Core **967/967**, serveur **1782/1782** — tout vert.
  - Client live relancé (`--login --channel --char`) : HUD complet,
    inventaire/tooltip/stats/XP sains, canalisation item 53 + break
    Shift+S re-prouvés en wire (407→13→408).
  - **Fix bonus** : `ItemProcDumpTests` posait
    `TextResolver.ResolveFile` (résolveur lowercase) sans reset →
    `HouseTableTests.RealFile_House_Parses` échouait
    (`4PHouse.ini` → `4phouse.ini` introuvable). Restore `= null`
    aux 2 sites → NetTests 937→938 verts.
- **Correction de statut — `material is null` ×4 : PAS résolu.**
  La garde `NiControllerRuntime.cs:278` a fixé UN site, mais la
  même signature (4 appels RD `material_casts_shadows`→…→
  `material_update_dependency`) réapparaît au boot selon les
  assets chargés — un autre site apparie une géométrie avec un RID
  matériau invalide. Cosmétique (4 lignes avant le 1er wire, aucun
  artefact visuel), bisection moteur non poussée — reste connu.
- **Restes finaux consignés** (inchangés) : 306 NCID NON PROUVE
  inventoriés (inter-serveur/DBA/gateway — hors domaine client↔zone),
  rendu 3D bâtiments VDG limité, `material is null` ci-dessus,
  break-au-mouvement non implémenté (règle maison).

## 2026-11-06 (cycle de vérification final — passage propre)

- **Tests** : `tools/test.sh` → NetTests **938/941** (3 skip),
  Eden.Core **967/967**, serveur **1782/1782** — tout vert
  (log `artifacts/logs/test-20261106-005239.log`).
- **B1 rejoué en entier** : cycle d'invasion s005 observé bout en
  bout sur le même boot — born 1/5/9 → marches 3 boss → sièges →
  bleus détruits (3/7/11) → fermeture (2/6/10) → rouges
  auto-détruits +1200 s → **retour bleu (4/8/12)** + cristal 55070
  respawné confirmé en sight.
- **B8 rejoué** : trésor au sol → refus 0x4f9 « Pas assez de
  place » (trésor conservé) → ramassage clic gauche (approche +
  NCID 35) → 274×5 + 386 + 330 + « Obtenu : …×5. » — captures
  `b8-treasure-chest-ground.png` / `b8-treasure-pickup-ok.png`.
- **Gameplay rejoué (client réel, canal `--cmd-file`)** :
  - combat : `@attack 58109` → approche + swings (NCID 18/5) →
    `272 dmg=160 kf=1021` → `mob tué` + EXP ;
  - mort : `//suicide` → `MORT` → panneau « Vous êtes mort »
    (3 choix natifs) → clic « **Revenir ici** » → `D NCID=20
    type=1` → `REVIVE hp=490/4903` (10 %) + régén ;
  - soin : `@cast 50028` (Guérison Clerc) → `D NCID=17` → `279`
    → floater «161» Heal → HP 1351→4903 ;
  - canal : `@cast <spellId>` ajouté à `GmFileProbe` (même chemin
    que le clic hotbar — `SkillCast.Cast` réel).
- **Écran** : HUD complet, HP/MP pleins, quest tracker FR, monde
  rendu — capture `final-ok2.png`.
- **Restes consignés** (inchangés) : 306 NCID NON PROUVE hors
  domaine client↔zone, `material is null` ×4 cosmétique au boot,
  break-au-mouvement non implémenté (règle maison), rendu 3D
  bâtiments VDG limité.

## 2026-11-06 (donjons Evaluate 655-668 + solo illusions 829-841)

- **Constat** : nodes 655-668 type 18 = trials Evaluate (équipe), 829-841
  type 5 = « 毀滅幻境 » solo hebdo (hors DungeonCatalog — transports
  directs S055 events 114-120, gate lv90).
- **Serveur `//team`** (ZoneEntryFlow.Admin) : équipe solo test —
  `[Evaluate] check team=1 node=655 members=1` live.
- **Client** : `ZoneClientEvaluateInfo` 607 réécrit par sous-type
  (0 statut / 1 compteurs / 2 résultat / 3 loterie) ; nouvelle
  `EvaluateWindow` (consent 577 + bandeau HUD + record 608 + infos
  525) montée dans `HudExtensions` ; `@teamop` ajouté à
  `GmFileProbe`. Build + tests verts (938/967/1782).
- **Live trial 655 complet** (char #200000020, lv90) :
  - `@teamop 4 3 655` → `D NCID=79` → `E NCID=577 type=3` →
    dialogue « Your party wants to enter the trial (#655) » →
    Accepter → `D NCID=79 [5][2]` → `start eval=1` → migrate S655
    (capture `eval-consent-577.png`).
  - HUD : `607 [0][status=1][10800s]` + compteurs subtype-1 live
    (32 kills, deaths 1/1 cappé MaxDeaths) — capture
    `eval-trial-655-entry.png`.
  - Mort réelle → `CharacterDie` +1 compteur ; revive type=1 →
    `hp=1080/10802` (10 % natif).
  - Chaîne boss native : kill 63289 → `Event_RebornMonster 17`
    spawn 63399 → kill → `end eval=1 score=2489 grade=C
    elapsed=347s` → `607 type=2` résultat complet + `608` record +
    `525` reset — capture `eval-trial-end-lottery.png`.
  - **Persistence DB** : `game.player_evaluate` +
    `game.team_evaluate` = 655/2489/347s/C.
  - Sortie : passerelle (65,250) → event 100 → migrate retour
    **node=55** + packets reset `[0][-1]`/`[3][-2]`/`525[0]`.
  - Grade C = 0 ouverture de boîte → loterie non jouée (255 pick
    reste non prouvé live — nécessite B+ ; chemin serveur codé).
- **Live solo illusion S829** : portail (204,628) S055 → gateway
  → `Event_Transport` → `resolve node=829 type=5 -> groupe 1` →
  MigrationDone, mobs #40057 en sight, scène Delstrium chargée —
  capture `s829-solo-illusion.png`. Pas d'offre DungeonCatalog
  (design natif : transport direct, pas de compteur journalier).
- **Limite consignée** : rotation hebdo — la passerelle a routé vers
  829 (event 114/lundi) alors que vendredi = 837 ; à clarifier si
  le natif route par jour réel ou premier event match.

## 2026-11-06 (fix flake NetTests — état partagé entre classes parallèles)

- **Symptôme** : 1 échec différent à chaque run (`HouseTableTests`,
  `UiTextIsTranslationTests`, `ItemProcDumpTests`) — race entre classes
  xUnit parallèles sur l'état statique `TextResolver.ResolveFile` /
  `SetLanguage` + caches paresseux de tables figés vides après un
  chargement avec un mauvais résolveur.
- **Fix** :
  - Collection xUnit `SharedResolver` (`IniTableTests.cs`) —
    sérialise les 5 classes mutantes : IniTable, CharColor, HouseTable,
    ItemProcDump, UiTextIsTranslation.
  - `HouseTable.ResetForTests()` (purge `_buildings/_houses/_tried`)
    + `HouseTableTests.Bind()` — résolveur propre + reset par test.
  - `ItemProcDumpTests` → `CaseInsensitiveResolver` partagé (son
    résolveur lowercase empoisonnait les noms mixtes `4PHouse.ini`).
- **Preuve** : `tools/test.sh` → NetTests 938/941, Core 967/967,
  serveur 1782/1782 — tout vert (`test-20261106-014334.log`) +
  2 passes NetTests directes stables.
- **Limite consignée** : `Smoke_GameScene_Headless` exige
  `EDEN_GODOT_BIN` (fourni par `tools/env.sh`/`test.sh`) — un
  `dotnet test` nu lance le `godot` du PATH (4.7.2) et échoue.

## 2026-11-06 (soir) — Revérification visuelle : fenêtre Fortification

- Capture live (client réel, `@act 13`) : titre « Strengthen », avertissement, « Success Rate: » en anglais, en-têtes MCL + note en chinois brut, bouton sans libellé.
- Fix : entrées TSV (ui_text_en_fr.tsv + ui_text_fr.tsv CN→FR en-têtes et libellés « ：valeur »), `MclList.SetColumns` traduit les titres, `AutoTranslate` gère `RichTextLabel` + texte multi-lignes (libellé traduit, valeur conservée).
- Preuve : build 0/0 ; capture `/tmp/s6c.png` — fenêtre « Amélioration » 100 % FR (avertissement, en-têtes, note, « Taux de réussite : »).
- Reste : bouton Strengthen sans libellé (StrengthenBtn), autres fenêtres de la matrice « À VÉRIFIER » (shop, work, spécial coins, item mall, trade, HdV, étals, tooltips) à repasser une par une avec ce même contrôle FR.

## 2026-11-06 (soir, passe 2) — Revérification visuelle guidée par agents

5 agents (guilde, craft, work/combine/collect, commerce, donjons) : audit layouts+code+ref-shots puis correction. Build client 0 erreur, serveur 1785/1785.
- **Régression trouvée et corrigée** : `GuildState.Titles` null → NRE au boot du HUD (HUD absent) ; init `""` dans ZoneSocialMessages.cs ; `RankName` null-safe. HUD revenu (capture /tmp/g4c.png).
- **GmFileProbe `@key`** : renseigne `Keycode` (avant `PhysicalKeycode` seul → les handlers `k.Keycode` ignoraient la touche).
- **Vu à l'écran** : Fortification FR complet (/tmp/s6c.png) ; GuildWindow onglet Ville sans placeholders (/tmp/g6z.png, bâtiments vides hors hall) ; donjon S105 charge via `//goto 105` (/tmp/d2c.png, pas de mob dans le champ).
- **Corrigé en code, NON revu à l'écran** : Racial craft, Magic Cube, Magic Box, PaySystem, combine/collect/worklist, shop/HdV/hôtel/item mall/étals/social/tooltip/inventaire, onglets Guilde/Messages/Rangs, FamilyBuilding, FamilyStorage, TownList, Territory, Crusade, murs/scatter/z des donjons (serveur).
- **Défauts connus** : Ville = grand cadre gris vide sous la fenêtre, « Taxe commerciale » illisible (clair sur clair) ; blason sans éditeur ; fenêtres HUD guerre de territoire/bataille de guilde absentes ; nodes sans scène (188-194, 669-684, 843-858…) ; HdV natif (AuctionBuy/SellWnd) non utilisé ; boutons hôtel sans paquet.
- **Suite** : capturer chaque fenêtre ci-dessus (ouvrir par NPC `@talk/@dlg`, `@act 13/16/101`, touche G…), puis donjons 122/121/717/203/221/701/603 en jeu, puis rejouer gameplay.

### Passe 2 (suite) — captures après build
- **Vu OK** : Cube magique (/tmp/cap_cube.png), dialogues NPC FR (51872, 50354), worklist « Ordres de travail » lisible et centrée (/tmp/cap_wl2.png), **HdV refait sur layouts natifs** auctionwnd/buy/sell, lisible (/tmp/cap_ah8.png), revive « Revenir ici » (`@revive 1` → 10 % PV + perte EXP), donjons 105/122 chargent (`//goto <node>`).
- **Reste vu à l'écran avant dernier fix, à recapturer** : Magic Box (placeholders 100/100, x1111111 — fix texte posé, non revu), Racial craft (9999/9999 — fix RowLeaf, non revu).
- **Non capturé** : détail collect/combine (clic coordonnées @click non calibré), shop, étals, trade, item mall, FamilyBuilding/Storage/TownList/Territory/Crusade, onglets Guilde/Messages/Rangs, mobs de donjon en vue (121/717/203/221/701/603).
- **Outils** : `/tmp/cap.sh <nom> "<cmd>"…` (spectacle + crop), canal `/tmp/gm.cmd`, `//goto <node>` entre en donjon, `//goto x y` TP.
- **2026-11-06 Magic Box revu OK** (/tmp/cap_mb6.png : Équipement/Charme/Matériaux/Énergie/Puissance du cube lisibles, zéro placeholder) ; Racial craft OK (/tmp/cap_ls2.png). Observé : au relog le perso peut apparaître mort (410 PV) → `@revive 1` OK.
- **2026-11-06 captures post-build #2** : townlist FR ✓ (/tmp/cap_n_townlist.png), item mall FR + Hot lisible ✓ (/tmp/cap_n_itemmall.png, noms EN restants = données t_itemmall non traduites), étal en-têtes FR ✓, territoire noms FR ✓ (haut encore rogné), guilde onglets Guilde/Rangs : texte pâle/EN restants → agent en cours ; familybuilding : cadres d'aire invisibles → agent en cours.
- **Donjon S717 « Veine Tordue »** (`//goto 717` puis `//goto 155 56`) : 54 entités ; mobs Lv53 « Tueur de la Mine » posés au sol, combat engagé, pas dans la roche (/tmp/cap_d717b.png). Nouveau : `@open <clé>` (HudRoot.ForceOpen public), `@tab <nom>`.
- **2026-11-06 guilde recapturée** : onglet Guilde complet FR (liste membres + en-têtes + boutons lisibles + annonce, /tmp/cap_r_g1.png), Messages ✓, Rangs ✓. **Restes** : la touche G / `@open guild` ouvre encore sur l'onglet Ville (fix `CurrentTab=0` inefficace — mauvais contrôle ?), « Town Notice: » EN, onglet Ville : cadre bâtiments gris vide, FamilyBuilding : étiquettes « Libre »/« Emplacement N » visibles mais cadres d'aire/slots NPC toujours invisibles (/tmp/cap_r_fb2.png).
- **2026-11-06 (soir) tests + guilde + donjons** : `tools/test.sh` → NetTests 939/942 (3 skip), Core 967/967, serveur 1785/1785, tout vert (`artifacts/logs/test-20261106-185226.log`). Guilde : onglet Guilde par défaut OK à la touche G (maintien 1,5 s — Visible=true d'un volet rebasculait l'onglet), « Annonce de la ville : » FR, **115 entrées de `ui_text_fr.tsv` encore EN francisées** via ui_text_en_fr (cause systémique des restes anglais : première entrée gagne). Capture /tmp/cap_t_g.png.
- **Donjons chargés via `//goto <node>`** (zone log « scene Sxxx: N entités, M en vue ») : S121 59/16, S203 114/11, S221 58/1 (« Temple aquatique <multijoueur> »), S701 41/1, S603 41/1, S717 54 (mobs au sol vus). S121 : le perso lv90 meurt en <20 s à l'arrivée (élite « Chino » 225k PV dans la vue) — à confirmer si normal. Mobs d'élite/escouades vus au sol dans S203/S121 (/tmp/cap_dj203.png, cap_dj221.png).
- **Reste** : détail collect/combine non capturé, cadres d'aire FamilyBuilding invisibles, onglet Ville gris, gameplay complet (quêtes, craft e2e, +14, fusions, commerce) non rejoué depuis ces changements.
- **2026-11-06 gameplay rejoué (client réel, `/tmp/gm.cmd`)** : **craft e2e ✓** — `//give 31096/30031/30038`, `//npc 50354`, `@talk`, `@dlg 0`, `@work 132 0 <slot>` → 433/497/275×3/498 + chat « Utilisé Formule… », `@workclaim 0` → 171 → 274 (30297 en sac) + 500. Popup résultat corrigé : FR lisible (/tmp/cap_gp6.png ; titre encore rogné à gauche). Revive `@revive 1` ✓.
- **Fortification** : fenêtre FR OK (/tmp/cap_str14.png, 100 %). `@str 9 0 22 0 7 -1 -1` (31908 + pierre 40009, combine 9) → 436 **une seule fois** (+1) ; les casts suivants → 433 puis 434 « annulé » (code 9) : NON résolu (zone.log muet ; `st.PendingStrengthen` bloqué ? or=0 puis `//gain_gold` sans effet ?). Le +14 live du 2026-11-06 plus tôt avait été fait par la fenêtre (drags). À rejouer par la fenêtre avant de conclure à un bug.
- **2026-11-06 (nuit) craft : flux natif refait** (agent sur sources primaires : le-craft.md, ref-shot « FusionPanel Craft », combinewnd/collectwnd/worklistwnd, décompilé `FUN_0070fe30 @0x0070fe30` handler 496 → 12-14 CollectWnd / 15-17 CombineWnd ; `FUN_0070ff50` 497 → WorkListWnd + WorkResultWnd) ; détail `docs/temp-plans/2026-11-06-craft-flux-natif.md`. **Catalogue d'armes dans worklistwnd = invention, supprimé.** Fenêtre « Fonderie » = panneau natif Magic Pool (slots Formule/Charme, matériaux possédé/requis, produit, Commander/Fermer), autorisation PNJ par Kind de la formule (0x35/0x36/0x37), serveur refuse déjà (BadCatalyst).
- **Vu** : panneau Fonderie vide à l'ouverture (/tmp/cap_cw1.png), `[craft] slots : use=True charm=True`. **NON VÉRIFIÉ** : dépôt de la formule — `@drag` synthétique ne démarre pas le drag (pas de log `[dnd] ItemCell._GetDragData`), alors que `@under 647 330` confirme que le slot UseItemSlot est bien sous le curseur. À tester à la souris réelle ; ensuite Commander → 170 → 498 → 171 (déjà prouvé par `@work`). Outils : `@nodepos <frag>`, `@under x y`.
- **2026-11-06 craft par l'UI natif VÉRIFIÉ** : le sac fonctionne en « objet en main » (clic objet → `InventoryWindow.Held` → clic slot cible), pas en drag. `CraftWindow` : `use.Clicked`/`charm.Clicked` → `TakeHeld` → `ItemSlotCell.EmitDrop`. Live : `@click` sur formule 31096 puis `@click` UseItemSlot → `[craft] drop formule item=31096 type=16 recette=ok`, panneau rempli (instantané, coût 4, taux 100 %, matériaux, produit) ; un poignard déposé à la place est refusé « Ce PNJ ne peut pas fabriquer cette formule. » (autorisation par Kind ✓). Commander → 170 → 433 → 497 → 275 → 498, chat « Utilisé Formule… Cuivre Brut x20 Cristal Pur x6 », or −5 (/tmp/cap_cw12.png). **À corriger** (agent en cours) : « possédé » faux (1/20 au lieu de 40) et texte pâle sur fond pâle. Les autres fenêtres à slot (strengthen, cube, magicbox) ont le même risque « drag only » : à tester en objet-en-main.
- **2026-11-06 (nuit) mobs** — (1) **téléport+freeze** : mesure `[mobmove]` S717 avant fix : paquets de wander/patrouille toutes les 4–6 s, segments 19–75 u, `spdAnn=2,0` (Def.RoamSpeed dans le 298), `snap` 12–38 u, 2 JUMP/5 FREEZE. Cause : `ZoneEntryFlow.MobWander.cs` délai = dist/5,0 codé en dur alors que le client marche à 2 u/s. Fix serveur : `WanderSpeed(mob)`=RoamSpeed, délai = dist/vitesse (cap 120 s) ; poursuite : pas serveur proportionnel au temps réel (7,2 u/s) + dst 300 ms devant + horodatage réseau + `ObsSpeed` remis à 0 si trou >0,8 s (client). Après fix : 0 JUMP/0 FREEZE sur 4 mobs au repos (S717), **poursuite NON mesurée** (@walk de test n'a pas fait fuir le perso ; 0 ligne `[mobmove] srv`). Natif BattleSpeed/paquet vitesse en combat : non vérifié. (2) **modèle cassé** : cause = `ToonMaterializer` masquait le mesh `KNOCK` (regex de collision de map) sur tous les modèles de monstres ; fix `monsterScope` ; log `[mobskin]` ; M069 (Tueur de la Mine) n'a pas de KNOCK → non concerné ; seuil « gros coup % PV » côté serveur non vérifié ; rendu à vérifier sur un mob avec KNOCK (M018, M022, M029).
- **2026-11-06 (nuit 2) 5 agents sur sources primaires + vérif live** (nouveau `@shot <png>` = capture viewport du jeu seul, coordonnées = @click) :
  - **VDG par panneaux ✓ live** : G → onglet Guilde → « Ville guilde » → `[vdg-panel] EnterTerritoryBtn → 176 fam=5001` → migration node 502 « Mont Eden » (PNJ Mineur visible) → bouton « Quitter ville ». Fixes : bouton grisé sans ville/bascule Quitter (natif FamilyInfoWnd 0x00a98b10), TownList messages, layout montagne 502, gate serveur 0x9f1 (niveau bâtiment >2 refusé hors VDG, CheckToCombine 0x08163cd0), taxe créditée à la guilde, overlay tables FR c_familycollect/combine (231 recettes modifiées). Matrice bâtiments : `docs/temp-plans/2026-11-06-vdg-batiments-matrice.md` (types réels 1,2,4-10). NON testé en jeu : craft dans chaque bâtiment/niveau.
  - **Épée** : `[weapon-attach] mode=BODY-ROOT(os introuvable) origine=(0,0,0) lame=(0,0,1)` = arme posée sur la racine du corps, lame verticale → traverse le perso. Agent corrige l'accroche Prop1.
  - **Mobs** : patrouilleur S717 `snap=0,00` (plus de téléport) ; invisibles en mouvement : fix GroundPos (Y=0) non confirmé visuellement ; marche/course : move01/move02 selon poursuite (natif 0x00781b20) non vu ; hitbox : capsule suit l'AABB (`[pick]`) non testée ; fog plafond 0,45 (S122 encore dense à l'écran) ; ombres : pancake 200 non prouvé.
  - **S122** : le perso lv90 meurt à l'arrivée (boss Multipède Vicieux ★★ 45380 PV dans la vue).
- **2026-11-06 (nuit 3) outillage « jouer comme un humain »** : nouveau `docs/workflows/AGENT-IA-GAMEPLAY.md` (tous les moyens : `@shot` viewport seul, `@burst` mini-vidéo, `@cam yaw pitch dist`, `@hold action sec`, `@cjk` audit chinois, `@nodepos`, `@under`, `@open`, `@tab`, clic « objet en main » du sac…). `@cam` : pitch >~40 passe sous le sol/perso (à régler : utiliser 15-30).
- **Audit CJK live** : fenêtres guilde/villes/coffre/territoire/onglets = 0 texte chinois (restes : aires FamilyBuilding = noms de plans « L1 建設藍圖：… », 3 hits). Sous-dialogues guilde (don, commentaire, emblème) : titres EN (« Donate to the guild », « Member comment »), boutons sans libellé, s'empilent, Échap ne les ferme pas → agent. Constats propriétaire : hall QAForge incomplet (PNJ/bâtiments mal orientés, pas de nœud de mine/ferme/invocateur ; 4 systèmes de ressources + invocation), minicarte sans icônes natives → agents lancés (dialogues, FamilyBuildingWindow, hall/ressources, minicarte). Épée : `mode=node` après fix (`Bip01_Nub Prop1`). Hitbox : `[pick] r=1,45-1,53` appliqué. Mobs S717 : 4–5 JUMP/FREEZE sur 12 s.
- **2026-11-06 (nuit 4) hall VDG vu EN JEU** (`@cam2 0 -60 45 4` vue aérienne) : manoir h003 + ferme/mine/autel posés (3 sur 3 reçus), orientation −face (façade vers le centre), anneaux de récolte « Fissure de mana (boule de cristal) », « Mine de cristal (pioche) », « Champ (gants) » visibles, minicarte avec vraies icônes. `//hall_setup reset` : 18 ajouts (housekeepers + PNJ de service). Épée tenue en main (vue de côté, hors corps). NON vérifié : PNJ de service visibles devant chaque bâtiment, achat pioche + récolte, invocateur→mob→loot, fenêtres guilde après les corrections de dialogues (compilées, pas testées).
