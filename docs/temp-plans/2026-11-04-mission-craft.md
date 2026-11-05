# Mission — Craft & Guilde complet + Gameplay réel + Bugs récurrents

Date : 2026-11-04. Statuts : `docs/workflows/task-contract.md`.
Priorité : **§3 bugs récurrents + §2 gameplay** (bloqueurs) → **§1 phases A→G**.

## §3 — Bugs récurrents (à corriger DÉFINITIVEMENT + preuve live)

| # | Bug | État connu | Statut | Preuve |
|---|---|---|---|---|
| B1 | Élites/mini-boss : pas de chemin tracé vers les crystaux (bleu→rouge) | Le concept : les élites vont vers des crystaux sur la map | TODO | |
| B2 | Déséquipe/rééquipe perd le skin tête d'armure ; seul le panel profile (cases à cocher slots tête) doit toggler | Race `QueueFree` différé : `ClearEquipAttachments` condamnait le `BoneAttachment3D`, `AttachGlbToBone` le réutilisait → sous-arbre libéré fin de frame (perte « parfois », alternance visible/invisible). Cases ShowKuso* = toggle local natif (options.ini, aucun paquet) | **FIXÉ** | RemoveChild+QueueFree + garde IsQueuedForDeletion ; HudPrefs pref.kuso.* ; ApplyEquipment attachVisible(loc)+hideOutfit ; flag test --kuso ; captures /tmp/kuso_off.png (tête nue) + /tmp/kuso_on.png (heaume après rebuild) ; item 32234 inséré en DB pour le test |
| B3 | Impossible d'up character ou class (XP cassée) | 284 ne maj que le FX — portrait/ratio XP stales ; double-kill serveur (2×305+2×XP) | **FIXÉ** | case 284→SetSelfLevel ; capture /tmp/lv45.png (niv45+barre XP) ; serveur : guard Alive+DeathEmitted |
| B4 | Barre exp character + TopLeftVitals cassées (traits héroïques) — 3e casse, code retiré | Capture live : portrait+HP/MP+icônes trait présents, barre EXP bas-gauche «0.000%» vivante (600→SetExp→Recompute) | **VÉRIFIÉ** | /tmp/lv45.png |
| B5 | Armes/skills distance ne marchent pas (hunter + autres) | 3 bugs : (a) `AutoAttack.AttackReach=3` hardcodé → chasseur courait au contact ; (b) anim swing `attack01/02` générique au lieu des pools kfId natifs par kind ; (c) `SkillCast.BeyondReach` col28 ×1,5 au lieu de ×0,5 (SSpellData+0x40 natif) → casts déclenchés 3× trop loin → 0x333 « trop éloignée » | **FIXÉ+prouvé live** | Natif : `CCharFormula::AttackRange` @0x083f6f10 = col48 brute SANS multiplicateur ; `CanAttack` @0x08417120 ajoute rayons+DAT_087b2730. Client : `ReachNow()` = col48+1 (arc 42→43u) ; `AttackAnimNames()` miroir `MotionParser.WeaponAttackKfIds` (arc→attack12, gun→attack13, dual C[m×3+o]). Serveur : `AttackRange` ×1,5→brute+5. Live (arc 33631 loc10, `//cc 3` Chasseur) : swing#1 **immédiat à ~5,6u** (impossible avec reach=3) anim attack12, `272 dmg=291 type=3 kf=1421`, rshoot01-79 cuits, kill + XP. Epées/arc test restaurés en DB |
| B6 | Stun/sommeil : pas sous l'animation du nerf ; DOT élémentaire = particules sur joueur/cible | Le visuel col3 (BUFF特效) des enchants n'était jamais attaché — seul le cast/impact SpellFx jouait | **FIXÉ+prouvé live** | `EntityBuffs` émet EnchantVisuals (added→`SpellFx` attache col3 sur l'entité pour la durée rest, removed→detach) ; `EnchantTable.VisualKeyOf`. Live : `enchant 52038 «s01151» sur 0xffff809a (2s)` sur le stun de charge ; DOT/debuff = même chaîne (299→icône+particules, 300→detach) |
| B7 | Sorts de stun joueurs : ne stun pas, mauvaise durée ; sorts de déplacement (charge guerrier) KO | Charge : le serveur émet NCID 267 `ClientGoto` au charId **self**, mais `EntityRegistry.HandleGoto` n'acceptait que `_entities` (jamais le self) → drop silencieux, le perso restait à 20u | **FIXÉ+prouvé live** | `HandleGoto` route le self vers `PlayerController.TeleportTo` (clear path/moveTarget + snap). Live charge 50038 : poursuite portée 20u → 279 → `267 self pos=(227,43) flag=0x6a` → client renvoie NCID 1 depuis la nouvelle pos → mêlée immédiate + `299 eid=52038 rest=2` (stun mob 2055). Stun joueur : `//self_use_effect 52059 40` → `299 rest=40` + 280 attrs 1002-1006=1 → `Cast()` refuse tout sort via `NoAnySkill` (0 paquet émis, gate client OK) ; à +40s : `300` + attrs→0 → déverrouillé. Zone log : `expire`+`remove rest=0` à la seconde prévue |
| B8 | Items drop au sol « Pas assez de place » alors que slots libres ; doit drop sur soi si place, sinon sol + clic gauche ramassage | Divergence capacités : `inventory_slots` non réconcilié avec sacs équipés à l'entrée (312 émettait des caps stales) + `NativeInventoryParser.Capacities` jamais vidé (caps fantômes entre persos) ; ramassage clic droit seulement | **FIXÉ+prouvé live** | Réconciliation slots↔sacs équipés dans `EntryHandlers` avant 312 ; `Capacities.Clear()` au début de Parse ; clic gauche sur kind0+EntityId≠0 → `TreasurePickup.BeginPickup` (approche + NCID 35). Wire : kill→291 merge stack (loc12/13), 274 nouveaux slots (bag0 loc30/31 puis bag1 loc0 quand bag0 plein), sacs 47/47 → **329 trésor au sol** ×3. DropRate=100 temporaire pour la preuve, restauré. Ramassage live à re-valider au prochain run graphique |
| B9 | Inversion animations de montures toujours là | Records monture natifs 9/10 décodés (DirectionControl.cpp, init @0x…2470067, table DAT_00fe49a8) : rec9 = copie Left + facing −90° D3D, même callback vtable+0x38 → action 102 → move02 ; rec10 = copie Right + facing +90° → move03. Le natif ne croise PAS — notre `MountAnimFor` croisé était l'inversion ; le remote (flag 326→AnimForFlag) jouait déjà le mapping natif | **FIXÉ+prouvé live** | `MountAnimFor` non-croisé dans les 2 copies (Left→move02, Right→move03) + NetTests 30 verts ; montage live via `//clone_item 20051` (UseEnchant 58991) + `//self_use_effect 58991 600` → 283 key=14010 (Puma Enflammé) ; flag `--strafe=<dir>[:sec]` ajouté. Gauche : `rec=3 mount=move02 cur=ride05 modelYaw=90` + `/tmp/b9_mount_left.png` (profil gauche) ; droite : `rec=5 mount=move03 cur=ride06 modelYaw=-90` + `/tmp/b9_mount_right.png` |
| B10 | Changé de classe → clerc : ne peut cast aucun skill (mana OK) | Gate client lisait `SessionCarry.Character.ClassId` (stale après cc) + refusait les masks multi-bits autorisés par `CanCast` serveur | **FIXÉ** | SkillCast.cs : `PlayerVitals.ActiveClass` + PopCount>1 ; live : `//cc 3` → 50038 guerrier refusé (cls=3), 50150 casté → 279 OK + 281 + floaters |
| B11 | Mobs : model « cassé » affiché dès l'état normal (au lieu d'intact jusqu'à BP terminé/gros hit) | `PatienceSeen` fix posé — garde WeakMask+PatienceSeen suffisante ; le symptôme d'origine venait de patience=0 par défaut → KNOCK caché au spawn | **FIXÉ+prouvé live** | Cycle complet prouvé : `/tmp/b11_normal.png` (coquille KNOCK verte visible au spawn — M025 431 tris, block 37 skinné ; golem M020 idem) → `/tmp/b11_broken2.png` (pilote `--broken-test` : SetBroken(true) → KNOCK masqué, champignon nu) → `/tmp/b11_restored.png` (`--broken-restore` : SetBroken(false) → coquille revenue, FUN_00771920). Dump nœuds confirme `KNOCK` sous Skeleton3D dans le visuel NIF |
| B12 | Énormes lag quand on kill des mobs | 3 causes trouvées et bornées : (a) drains réseau **non bornés** — `GameScene._pending` vidait tout le burst en 1 frame (pic `proc=1204ms` à l'entrée de zone, `reg.net 37,7ms` aux kills) ; (b) GLB mobs chargés à froid au 298 (18-37 ms de parse par nouveau stem) ; (c) `Interp.SetProcess(false)` posé hors arbre ne tenait pas → **256 callbacks C# fantômes/frame** (proc-dump) | **FIXÉ+prouvé live** | `--kill-loop` 17 kills S005 (`/tmp/b12_loop4.log`) : pire image/kill 8-26 ms (vs 11-41 ms), `seeTreasure` 12,9→1,7 ms, `reg.visual` quasi absent ; pic entrée zone 1204 ms → ~140 ms étalé ; proc-dump : `NetworkInterpolator×256` disparu. Fixes : drains `_pending`/`_deferredDuringMigration`/`_netPending` bornés 8/6 ms, warm async modèles mobs dès le 298 + preload G00001-10 au map, `SetProcess(false)` post-`AddChild`. Restes mesurés (hors kill, consignés) : actions unitaires login ~40-100 ms (1re construction inventaire/sorts), `CooldownOverlay×450` en process, `reg.tick` ~11 ms occasionnel, trésors 300 s TTL → accumulation draw/labels |
| B13 | UI talk NPC : virer les ids, taille fixe, fond clair (cf. ref-shots/web-shots `npc talk*.png`) | Déjà implémenté (ResolveOptionLabel+IsBareId+StripId, TalkW/H=358×664 = QuestWnd, Paper crème) — re-vérifié proprement en live | **VÉRIFIÉ live** | `--talk-test` S005 : 285 réel du Forgeron Ursun `ctx=4033` → `/tmp/b13_talk_live2.png` (titre « Forgeron Ursun Bryson », texte parlé, crème, « Fermer » sombre — conforme ref `npc talk propositions.png`) ; mock `--hud-open=dialogue` → `/tmp/b13_mock.png` : ids bruts «1012»/«1013» résolus en «Nv. 2 Votre première Quête IV/V» (ampoule), «29931»→«Discuter» — aucun id affiché. Bonus mesuré : NPC « Passage » rend 393 transport 40 routes (dont 1 libellé «#57» non résolu — gap donnée à consigner) |

## §2 — Gameplay réel (jouer jusqu'à ~niv 25, `tools/run.sh -- --login=test123:test123 --channel`)

| # | Vérification | Statut | Preuve |
|---|---|---|---|
| G1 | Level-up perso + classe (XP, stats, sorts) | TODO | |
| G2 | Quêtes fonctionnelles jusqu'au niv 20-25 (prendre/TP/accomplir/valider/récompense) | TODO | |
| G3 | Craft d'équipement de bout en bout | TODO | |
| G4 | Création guilde → VDG : poser bâtiments via NPC/commandes | TODO | |
| G5 | Récolte VDG (pioche/gant/boule), plans mine/ferme/autel | TODO | |
| G6 | Fortification jusqu'à +14, fusions, enchant/inlay | TODO | |
| G7 | Commerce : shop, HdV, étal, trade, item mall | TODO | |

## §1 — Phases craft/guilde (SYNTHESE.md §4)

| Phase | Contenu | Statut | Preuve |
|---|---|---|---|
| A | Fondations UI : ItemSlot interactif, MCL, TabControl, icônes métier, ItemRecord complet | TODO | |
| B | Fenêtres fil chaud : shopwnd→work→collect/combine→strengthen→lifeskill→familytownlist | TODO | |
| C | Guilde complète : familywnd+onglets, familybuilding, storage, familybattle | TODO | |
| D | Cube/MagicBox/specialcoin/itemmall/tradewnd/auction/stall/tooltips | TODO | |
| E | Serveur : plafond DEV 10s, combos strengthen, devises fame shop, lifecycle bâtiments, achievements, auction_history, RandItemCombo | TODO | |
| F | VDG monde réel : instanciation S501/S502, NPCs, zones récolte, reliques | TODO | |
| G | Validation globale : captures par fenêtre, wire_diff vs natif | TODO | |

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
