# Plan d'action v5 — HUD/panels d'abord, double vision serveur + visuel

Remplace le plan v4 (`2026-10-27-plan-action-v4.md`). Réécrit le 28/10/2026
avec un nouveau corpus d'assets et une méthode de preuve par panneau.

## Ce qui change par rapport au v4

- **Ordre inversé (consigne utilisateur)** : le **visuel des HUD/panels
  d'abord**, puis les points Basse, puis les Haute/Critique restants.
  L'ordre v4 (Critique d'abord) n'est plus l'ordre d'exécution.
- **Deux corpus d'assets de référence, équivalents** :
  - `eden_godot/raw_assets/` → lien vers le client **Reforged**
    (`eden_server/reforged/EdenReforged`) — docs : `docs/RAW-ASSETS.md`,
    `docs/RAW-ASSETS-UI-UIDATA.md`.
  - **`/home/ster2lune/Bureau/reverseden/eden_server/client`** → client
    **vanilla officiel** (47 739 fichiers, catalogue fichier-par-fichier
    dans `docs/client-assets/` — INDEX + un .md par dossier).
    `client/UI/uiicon/` = toutes les icônes/interface du jeu ;
    `client/UI/itemicon/` (6 117), `client/UI/skillicon/` (4 754),
    `client/UIData/` (layouts/imagesets/fonts vanilla), `client/UI/map/`
    (minimaps), `client/UI/loadingframe/` (écrans de chargement).
    Utilisé quand le Reforged manque une image ou qu'on veut le look
    vanilla d'origine.
- **Chaque fiche HUD/panel a deux axes obligatoires** (voir « Format »).

## Sources de vérité

| Source | Rôle |
|---|---|
| `docs/hud-interfaces.md` | Inventaire fonctionnel des interfaces (prioritaire) |
| `ref-shots/` (82 png) | Visuel cible du client d'origine |
| `docs/client-assets/` | Catalogue par fichier du client vanilla (usage de chaque asset) |
| `raw_assets/` (Reforged) | Assets du client moddé : layouts CEGUI chargés par `hud.Build`, thèmes, data/db Big5 |
| `re/` (`client_game` Ghidra) | Comportement UX des panels (pseudo-code), lecture seule, `reindex` |
| `eden_server/server` | Handlers/serveur C# : `Eden.Zone/Handlers/*`, `Eden.Mission/*`, `Eden.World/*` |
| `eden_server/docs/generated/ncid_map.json` | Map NCID → nom natif (Zone 871, Mission 205) |
| `eden_server/docs/generated/FEATURE-MATRIX.md` | Statut de preuve par NCID (GOLDEN/DESASM/NON PROUVE) |

## Format des fiches

Chaque point HUD/panel est documenté sur **deux axes** avant de coder :

- **Serveur** — NCID C→S (actions utilisateur) et S→C (données/acks),
  handler C# (`src/Eden.*/Handlers|Services`), tables `data/db`
  (Big5 → `iconv -f big5`), flux **ouverture / mise à jour / fermeture**.
  Aucun paquet spéculatif : preuve = capture, désasm ou
  `FEATURE-MATRIX.md` ; sinon « à prouver ».
- **Visuel** — ref-shot(s) de référence, layout CEGUI natif
  (`raw_assets/uidata/layouts/` ou `client/UIData/layouts/`), icônes et
  images (`client/UI/uiicon|itemicon|skillicon`, imagesets, cf.
  `docs/client-assets/*.md` pour la description de chaque fichier),
  comportement UX d'après `re/` (`reindex lookup/search -b client_game`).

## Règles (inchangées)

- Une case n'est cochée que sur validation utilisateur ; chaque point
  fournit un **scénario de test à rejouer** (flux réel login → canal →
  perso → jeu → migration).
- `dotnet build godot_project/EdenClientReborn.csproj` = 0 erreur,
  viser 0 warning ; `dotnet test tools/NetTests` après chaque lot.
- Preuve runtime obligatoire : `--hud-open=<clé>` + `--shot` ou flag de
  diagnostic, `grep "SCRIPT ERROR\|Exception" run.log` = 0.
- Petits commits par sujet (auteur Noah Lhommedieu, trailer Devin).

## Ordre d'exécution

1. **Socle fenêtres** : H3, H4, M1, M2 (tout en dépend)
2. **HUD permanent** : U1 → U12
3. **Fenêtres principales** : P1 → P10
4. **Commerce / services** : S1 → S10
5. **Événements / PvP / divers** : E1 → E10
6. **Écrans hors-HUD** : M13, M14, M15
7. **Basse** : B1 → B6
8. **Haute restante** : H5 → H13 (y compris M10/M11 rendu entités)
9. **Critique** : C1 → C5
10. Transverse §0 (cast tous sorts + effets) après HUD/panels

---

## PRIORITÉ ABSOLUE — 10 chantiers complets (serveur + client Godot)

Consigne utilisateur 28/10 : ces 10 chantiers passent AVANT tout le reste
du plan. Chacun = serveur réel (NCID, handler, DB) + client (HUD/panel
fidèle, UX complète) + preuve live avec paquets réels + capture comparée
au ref-shot. Un chantier par exécution, dans l'ordre. Aucune case cochée
sans preuve ; l'utilisateur valide.

| # | Chantier | Serveur | Client (HUD/Panel) | Preuve |
|---|---|---|---|---|
| T1 | **Échap réel** + pile fenêtres | — | vraie touche clavier (focus chat, `_UnhandledInput` vs Control qui consomme, fenêtre Godot non focalisée) ; Échap ferme la fenêtre du dessus, sinon Main Menu | événement clavier physique (xdotool/`Input.ParseInputEvent` avec `PhysicalKeycode`+`Echo=false` + test fenêtre focalisée), log + capture |
| T2 | **P1 Inventaire complet** | 7 `SwitchItemLocation`→277, 19 `ItemUse`, 84 `DivideItem`, 8 `UpdateEquipment`, 89, tri 620/858, capacité sacs | `inventorywnd.layout`, drag/drop, split, tri, onglets, barre de fonctions, icônes `itemicon` exactes, tooltips | déplacer, scinder, trier, équiper en live ; `shot_0017` |
| T3 | **P2 Profil** | 26/280 stats, formule % (`rating/(rating+K)` décodée dans `re/`), 846/847 sets, 93/94 titres | `EquipmentWnd_new2`, 7 slots accessoires, 9 résistances, onglets Title/Fame/Achievement | stats serveur = affichage, changer set/titre ; `profile*.png` |
| T4 | **P3 Classes + talents** | 148/149 talents, 52 `BuySpell`, 12 `UseSkillPoint`, 181-183 certificats, 168 switch, restrictions classe/niveau vérifiées serveur | `JobWnd_NA`/`NewSkillList`, cadenas, Class Requirement, bandeau « Switched Class » | acheter sort, mettre point talent, switch classe ; `ClassePanel*.png` |
| T5 | **P4 Journal de quêtes** | 50/87/51→388, abandon/partage/tracking | `MissionLog.layout`, arbre par zone, récompenses icônes+qté, habillage M2 | abandon + reprise + suivi relogin ; `QuestLog.png` |
| T6 | **P6 Social + Guilde** | amis/blocage/tell (World), 103/104 recherche, guilde 49/81/82/159/161/130-132, persistance DB | `CommunityWnd`, `FamilyInfoWnd_NA`, `FamilyOrderWnd_NA`, `FamilyMessageWnd_NA`, `FamilyFindingWind` | créer guilde, inviter, annonce, dons, 2ᵉ client `Eden.Bots` ; `CommunityPanel*.png`, `FindGuild.png` |
| T7 | **S1 Boutique + S2 Banque** | 22/23 achat/vente, 764-767 rachat, 45-48 réparation, 63 fermeture, 60-62/69 banque | `ShopWnd`, `StorageUI`/`StorageUI2`, cellules partagées, prix/or, extension banque | acheter/vendre/racheter/réparer/déposer/retirer, or et items cohérents en DB |
| T8 | **S3 Échange + S4 Étal** | 36-42/56/91-92 échange, 71-80 étal vendeur+acheteur | `TradeWnd`, `StallUI`, `StallBuy` | cycle complet avec 2ᵉ client bot, or/items transférés en DB |
| T9 | **S5 Hôtel des ventes + S6 Courrier** | 114-118→425-428/733-737, 95-102→68-77 (Mission), pièces jointes, expiration | `AuctionWnd`, `MailWnd` (Take All, envoi, retour) | vendre/acheter via 2 comptes, courrier avec or+objet ; `Mail.png` |
| T10 | **§0 Cast de TOUS les sorts + effets spéciaux** | audit `data/db` sorts/enchants vs moteur d'effets Zone : procs de stats, double/multi-hit, DoT/HoT, buffs/debuffs, zone/self/cible ; implémenter les types manquants (sémantique par désasm `re/`) | hotbar→cast, barre de cast, FX, floaters multi-hit, icônes buff/proc | échantillon par catégorie via `--cast-spell`, 279/288/281/299 observés, matrice sort→effet géré/ignoré — **PARTIEL (28/10)** : exécuteur générique serveur + handlers 1802/1803/1804/1809/1120/1821/1823/1824, statut par commande dans l'audit, voir §0 |

**T1 fait (28/10)** : cause = Échap atteignait `HudRoot._Input` mais jamais `_UnhandledInput` (consommé par le GUI). Traité désormais dans `_Input` (focus chat → libère le focus, sinon `HandleEscape`). Preuve : vraie touche X11 (XTEST, `/tmp/.../xkey.py`) → log `[esc] open=[BackpackWnd]` → `[]` → `[MainMenuWnd]` ; 0 erreur, 414 tests. Approximatif : pas de capture image (import indisponible), preuve par log.

**T2 partiel (28/10 nuit)** — sonde `--inv-test` (`InventoryProbe.cs`) : move 7→277 (slot→slot libre) OK ; **tri** : bug serveur corrigé (`ArrangeItem` plantait `23505 inventory1_index` — lignes orphelines loc 10000+ d'un ancien tri ; tampon désormais au-dessus du max) → `D 260` → `E 620`, sac compacté par ItemId, base saine ; équipement double-clic → `D 7`+`D 8` → `E 277/282` (perso visiblement équipé). Capture Flameshot `godot-shots/t2_inventory_live.png`. Serveur redémarré, 1204 + 414 tests verts. **T2 complété (suite)** : split `D 84` → `E 291` (source 20→19) + `E 274` (nouveau stack x1) prouvé avec item 193 (MaxStack 999, ligne DB `inventory1` id 220003126 forcée à durability 20) ; **bug client corrigé** : le 312 ne pose pas le bit6 → quantité des empilables perdue au login (tous `x0`) → `SetItems` prend `Durability` si `MaxStack>1` (même convention que 274) — compteurs « 19 » / « 5 » visibles sur la capture ; **bug client corrigé** : déséquiper (277 `(1,7)->(0,27)`) ne remettait pas l'item dans la grille → pose via `PlayerEquipment.Worn`. Sonde : n24170 2→1 (équipé)→2 (déséquipé). Capture Flameshot `godot-shots/t2_unequip_live.png`. 414 tests verts. **Approximatif** : boutons Archive/Fortify/Fusion etc. non exercés (ouvrent d'autres fenêtres) ; icônes non comparées pixel à `shot_0017` (grille 14×3 vs 22×15 du ref).

**T2/U8 suite (28/10, session inventaire+statuts)** — sonde `--inv-test` étendue (temps réel) : **potion** 32631 → `D 19` → `E 303` + `E 280` (HP/MP) ✓ ; **monture** 20051 (Flame Soul Puma, cooldown 10 s) → `E 299` (monter) / `E 300` (descendre) + 283/302 ✓ ; **verrous de statut** : `//self_use_effect 10041 6` → `E 280` attrs 1002-1005 → nouveau `PlayerStateLocks` (client) bloque déplacement (`PlayerController`), auto-attaque (`AutoAttack`), sorts si magie+kungfu verrouillés (`SkillCast`), usage d'objet si 1006 ; `10055` (NoUseItem) → potion bloquée côté client (aucun `D 19` émis), expiration serveur → `E 300` + attrs à 0 → déblocage ✓. Captures Flameshot `godot-shots/t2_mounted_live.png`, `u8_stun_live.png` (icône stun 0:04 + sac). **Bugs corrigés** : codec client 545 `RideSpell` faux (2 champs fixes → « Trame tronquée » à chaque monte/descente ; vrai layout `[s16 n][n×s16]`) ; onglet Special affichait tout (désormais = ni potion/gear/town) ; fixture DB test1 : `bag_0_slots` 32→64 (55 items > capacité → tri client/serveur divergents, l'item visé n'était pas le bon). **Restent non faits** : fenêtres Archive/Fortify/Fusion (n'existent pas côté client, boutons = notice « via NPC »), filtre natif Town/Special non extrait (le client natif n'est pas dans l'index `reindex`), sacs supplémentaires/durabilité tooltips/comparaison pixel `shot_0017` non repris, états de contrôle sans effet visuel dédié (seule l'icône BuffBar), barre de sorts monture 545 non affichée (vide pour 20051), tests serveur non relancés (aucun fichier serveur modifié par cette session). Fixture DB : item 20051 ajouté au sac 0 de test1.
**T2 suite 2 (28/10 soir)** — **clic droit** sur une cellule = utiliser/équiper (vrai clic injecté `Input.ParseInputEvent` → `D 19` → `E 303`, capture `t2_cooldown_live.png`) ; **cooldown d'objet** : `CooldownTracker` (368 kind 0 groupe S_Item col22 / kind 1 sorts, snapshot + horloge locale) + `CooldownOverlay` (balayage + secondes, vibration à l'usage) sur inventaire et barre de raccourcis (sorts + objets) — preuve : potions « 1.4 » partagées par groupe ; **monture** : modèle du joueur local refait comme `RemoteEntity.SetRide` (`BuildRideVisual` animé, enfant de « Model » donc suit le yaw, corps hissé 38 % de la hauteur réglé sur capture `t2_mounted_live.png`) — l'ancien code posait un GLB brut sans rotation, sous le perso racine. **Bug serveur corrigé** : serials `id` partagés entre `inventory1` (test1) et `inventory2` (autres persos) → `UpdateItemAsync` écrasait aussi les lignes homonymes d'AUTRES persos (tri : 23505 `inventory2_index`, items orphelins loc 10000+) ; filtre `player_id` ajouté ; lignes orphelines remises à des slots libres. Tests serveur 1223 verts, NetTests 414 ; serveur relancé (`flock` inutilisable : les daemons héritent du verrou → lancer `start-native.sh` directement).


**T3 audit (non commencé en code)** — `ProfileWindow.cs` : % bruts (« échelle native du % non décodée », l.527 — formule `rating/(rating+K)` à extraire de `re/`), `pheal`/`gheal` = « - », boutons `Equio2_FastDressBtn` (Gear Swap) et `GrowthBtn` **désactivés** alors que le serveur a `ZoneEntryFlow.FastDress.cs` (846/847), onglet Fame vide (« not sent by the server »). Tâches : décoder les formules, câbler Gear Swap → 846/847 + preuve 282, Fame si source serveur.
**T3 bilan (28/10 nuit) — NON FAIT, deux blocages documentés** :
1. *% des stats* : calibration possible seulement avec `ref-shots/profile.png` (Lv100 Cleric : ACC 122→4.29 %, CRIT 124.7→5.63 %, M-CRIT 122→1.5 %, EVA 174→8.07 %, Parry 135→2.4 %, Block 151→0.98 %, ATK SPD 7.5→135 %, Move 50→50 %) — formules différentes par stat, non fittables avec un seul échantillon ; les seuls formats `%` du binaire (`%.2f%%`, `%6.3f%%`, `%hd%%`) servent à l'EXP/pet, pas à la fenêtre stats (valeurs sans zéros finals → ostream). Aucun % inventé (règle « zéro hypothèse »). Pistes : retrouver la fonction de refresh `Equio2_*` dans `re/ghidra/rf_game/decompiled.c` (appels via vtable, non indexés par `reindex xref`) ou capturer un 2ᵉ jeu de valeurs natif (autre niveau/classe).
2. *Gear Swap* : `FastDressWindow` (`SystemServiceWindows.cs`) est un **stub texte** ; le codec client `ZoneClientUpdateFastDressData` (594) est faux (champs fixes, le natif = `[s16 mode]` puis `[s16 n][i32 page][n×6×i32]`), `DressRoomUI.layout` ne contient que fond/reset/avatar/tuto (les slots sont construits en code natif) ; le serveur (`ZoneEntryFlow.FastDress.cs`, 243-248 → 593/594) est lui complet. Reste : codecs 593/594 exacts (golden serveur), fenêtre 3 pages × 24 clés (drag inventaire→246, save 245, apply 244, page 247), bouton `Equio2_FastDressBtn` actif.
Aussi corrigé cette nuit (T2) : bug quantités empilables, déséquiper.

**T3 suite (28/10, session Profil)** — colonne % **décodée sans formule client** : le 296 porte déjà les taux en s16 ×100 (429 → 4.29 %), la colonne valeur = paliers 493 (f32 : HitLv, CriLv, MCriLv, DodgeLv, ParryLv, BlockLv, AtkSpdLv, CastSpdLv) — calibration ref (ACC 122/4.29 %, CRIT 124.7/5.63 %, EVA 174/8.07 %, Parry 135/2.4 %, Block 151/0.98 %, CRIT DMG 6.29/629 %, M-CRIT DMG 1.5/150 %) ; `rating→%` = formule serveur `NativeCombatFormulas` (`lv/(lvl×0.4+1)+base`). Client : `PlayerVitals.LevelStats` (493 stocké), `ProfileWindow.RefreshStats`. Preuve Flameshot `godot-shots/p2_profile_pct.png`. **Non prouvé, laissé « - »** : % de ATK SPD / Cast SPD (ref 135 % / 2.04 %), P/G-Healing (« - » aussi dans le ref). Move SPD = Speeds[4] (ref 50/50 %). **Title refait** (bandeau titre affiché, arbre par catégorie, Ability description + Method of Acquisition depuis T_Title, case « Displaying owned title », Set Title→93 ; preuve `godot-shots/p2_title.png`, flag QA `--profile-tab=N`) ; liste possédée = titres appris en session seulement (le serveur n'émet pas la liste complète). **Profil suite** : clic droit sur un slot équipé = déséquiper (comme le double-clic ; preuve `p2_unequip_before/after_c.png` : off-hand retiré, stats 135→95 AGI) ; aperçu 3D tourne au glisser souris (`p2_rot_c.png`) ; onglet Achievement embarqué (totaux, arbre, liste, `p2_ach_c.png`). **Reste** : lignes Achievement compactes façon ref (nom + Incomplete), Victory/Defeat Quote, Fame (aucune donnée serveur ni ref-shot), drag équipement vers inventaire, Fame, Achievement vs ref, drag/drop équipement, rotation souris de l'aperçu 3D, tooltips.

**Décision utilisateur 28/10 nuit** : Gear Swap abandonné ; priorité = système de combat + skills + TimedCastSkill + hotbar/hotkeys, inventaire complet (équiper/consommer/status monture-star stone), Class Panel, Profil, statuts buff/états. Audit effets de sorts : `eden_server/docs/AUDIT-EFFETS-SORTS.md` (141 commandes, 4249 sorts ; voie mob = seul `DistanceGoto` exécuté, les autres loguées).

**Combat — TimedCastSkill (28/10 nuit)** : `ref-shots/TimedCastSkill.png` montre le **nom du sort** (« Earth Shock ») dans une pastille sombre au-dessus de la barre — l'ancienne note « sans nom de sort » (U10) était fausse. `CastBar.BeginCast(seconds, spellName)` ajoute la pastille ; preuve `godot-shots/t10_cast_name.png` (« Bear's Rage », cast 279 castDs=10). **Reste** : cercle magique au sol + lueur sous le lanceur (FX du ref), icônes de sorts réelles sur toute la hotbar, effets instantanés de l'enchant résultat côté mob (voir `AUDIT-EFFETS-SORTS.md`).

**T4 Class Panel (28/10 nuit, session Class)** — live (Flameshot `godot-shots/class-*.png`, `thief-*`, `talent-*`, `lic2/news2`) :
- **Sorts** : liste par classe sélectionnée = sorts connus filtrés par masque C_Spell col24 (bit classe-1, ≤30 classes → exclut Soul Jump/Guidance), Reg. (col42=1, or) / Class (col42=9, CP) ; autre classe = catalogue grisé « Not learned » (`SpellCatalog.ClassSpells`). Tooltip : nom, Lv, monnaie, coût niveau+1 = `C_Spell col43 × C_SpellReference[lv+1].col30(CP)/col31(or) / 100` — **prouvé** = débit serveur (Ambush 6→7 : −6 CP, 12 UseSkillPoint observé, DB `player_spells` lv 7). Écart connu : le ref Reforged affiche 153 CP (Life Cure 11→12 = `C_SpellReference col41`), le serveur natif facture 10.
- **Bug corrigé** : 286 `ClientGetSpell` porte un s16 signé (50058→−15478) ; `PlayerSkills.SetLevel` ne retrouvait pas le sort (niveau jamais rafraîchi) → `unchecked((ushort))`.
- **Talents (148)** : saisie locale (« x+y/max », « N KPs Entered »), **Confirm** = un 148 par point, **Refresh** = annule ; KP vivants = 280 attr `0x31` (`PlayerVitals.AaPoint`, avant : champ 489 périmé) ; preuve serveur `SetAAPoint … 0->1`, `advanced_ability` en DB. 149 SetAARate = no-op natif (non câblé). Certificats possédés (509, s16 signé → ushort) listés avec icône + « Certificates: N ».
- **Switch classe (168)** : « Switched Class to X » (t_textindex 2013) en chat + gros texte centré (`ZoneBanner.ShowNotice`) ; 316 `TextOutputIndex` résolu par index (`TextResolver.TextIndex`, ex. 1524 « You have learned skill "%s1". ») + bandeau « You have learned skill X. » (index 2014) — preuve live switch Warrior/Cleric/Thief (`class-switch-a.png`, chat « Switched Class to Warrior » + 5 « learned skill »). Level-up perso 30→31 : aucun nouveau sort (pas de seuil) → pas de bandeau, non reproduit.
- **Reste / non prouvé** : description EN des tooltips de sorts = gabarit `T_Spell` (`#27#` MP, `#24#` CD, `#B11$400#` valeur d'effet…) dont la résolution (tables de niveau C_SpellReference + effets) n'est pas décodée dans `re/` → ni coût MP, ni portée, ni cast, ni description affichés (aucun chiffre inventé) ; arbre de certificats en losanges + 182 `PlayerLicense` (placement), Set Outfit + aperçu 3D + Weekly Performance + icônes Characteristics (Class News ref), CE List (désactivé, fonction inconnue), déblocage des classes avancées (serveur : seules 1-5/19 accessibles, `ClassChangeService` non branché), reset des talents (item Memory Tome), roue `JobGlyph`. Tests : serveur 1223 verts, NetTests 414 verts.

Suivi (cocher uniquement après preuve) :
- [x] T1  - [x] T2  - [ ] T3  - [ ] T4  - [ ] T5
- [ ] T6  - [ ] T7  - [ ] T8  - [ ] T9  - [ ] T10

---

## 0. Chantier transverse — Cast de TOUS les sorts + effets spéciaux (à faire)

Demande utilisateur 28/10 : vérifier que **tous les sorts** sont castables
(client → C→S 9/16/17 → serveur) et que **tous** peuvent porter des effets
spéciaux : procs de stats (enchants `c_enchant`), double hit / multi-hit,
DoT/HoT, buffs/debuffs, effets sur cible/zone/self.
- **Audit serveur** : croiser `data/db` (sorts `C_Spell*`, enchants, effets)
  contre le moteur d'effets de `Eden.Zone` (cast pipeline, `BuffBridge`) →
  table « sort → effets supportés / ignorés » ; lister les types d'effet
  non gérés (désasm `re/` pour la sémantique, aucun effet spéculatif).
- **Audit client** : chaque sort de la fenêtre Classes → hotbar → cast
  (barre de cast, projectile/FX, floaters multi-hit, icônes buff/proc).
- **Preuve** : sonde `--cast-spell=<id>` sur un échantillon par catégorie
  (instant, canalisé, zone, proc, multi-hit, buff) avec NCIDs 279/288/281/299
  observés ; matrice résultat consignée ici. Ordre : après le socle HUD/panels
  (consigne : visuel d'abord).
- **État serveur 28/10 (session effets de sorts)** : audit complet dans
  `eden_server/docs/AUDIT-EFFETS-SORTS.md` (141 commandes, colonne « Statut
  exécution »). Exécuteur générique `Eden.Zone/Combat/SpellEffects`
  (`SpellEffectExecutor` + handlers, voie mob) ; handlers prouvés par désasm :
  DistanceGoto 1802/1803/1804/1809, AttrHateReset 1120, dissipations
  1821/1823/1824 (`EnchantDispel`, voie joueur, sémantique corrigée).
  Découverte : 2033/2034 = `DamageAddPlus` (Execute no-op, lu par la formule
  de dégâts), pas MultiStroke/heal — ancien code retiré. **Reste** (ordre de
  valeur) : DamageAddPlus(Plus) dans la formule de dégâts, AttrHatePlus/Hot,
  Damage2HP, SummonPet*, OnAttackBuff*, SpellToSpell, LevelCheckPlus,
  EnchantCheckByType. 1223 tests serveur verts.

---

## 1. Socle fenêtres (avant tout panel)

### H3 — Fenêtres difficiles à fermer et à déplacer ✅ *(revérifié live 28/10 nuit)*
Barre de titre draggable sur toute la largeur, croix ≥ 20 px, clamp
écran, z-order au clic.
**Visuel** : chrome natif `DejaVu/FrameWindow` + `Titlebar*` (imagesets,
cf. `client-assets/UIData.md`), comportement natif des fenêtres CEGUI.
**Preuve** : `--drag-test=inventory` — drag par le titre `moved=True`
(313,90→433,180 = delta injecté exact), pression dans le corps
`moved=False` (drag réservé au titre), restore + persistance lues dans
`ui_layout.cfg` (`backpack=Vector2(433, 180)`) ; captures
`godot-shots/h5_inventory_{before,after,after_bg}.png`. Croix :
cliquée par `--esc-test` → fenêtre fermée (log `[esc-test] close-btn`).
**Revérif nuit** : `--drag-test=inventory` titre (384,205)→(504,295) moved=True persist=(504,295), fond moved=False ; `--esc-test` esc#1→Backpack seul, esc#2→vide, esc#3→MainMenuWnd, esc#4→vide, close-btn OK ; 0 erreur ; capture `godot-shots/h5_inventory_after.png` vs `ref-shots/shot_0017` (titre sombre + croix conformes ; liseré blanc 1 px sous le titre absent du ref = approximatif).
Approximatif : z-order au clic = chemin exercé (`MoveToFront` +
`OnPress`) mais pas d'assert visuel.

### H4 — Échap ferme les panels ✅ *(revérifié live 28/10 nuit)*
Registre central `HudWindows` ; Échap ferme la fenêtre du premier plan ;
vide → ouvre le Main Menu.
**Visuel** : pile de fenêtres comme le natif (menu Échap du client =
`SystemMenu`/« System » dans les layouts).
**Preuve** : `--esc-test` injecte 4 Échap — `CharacterInfoWnd` se
ferme avant `BackpackWnd` (plus récente d'abord), pile vide →
`MainMenuWnd` s'ouvre, 4e Échap le referme ; captures
`godot-shots/h4_two_windows.png`, `h4_after_esc1.png`, `h4_menu.png`.
0 erreur script sur les deux runs.

### M1 — Thèmes fonctionnels ⬜ *(à revérifier — ancien : prouvé live 28/10)*
`ThemePalette` pour les 6 thèmes ; StyleBox partagées modifiées en place ;
aucune couleur en dur dans `scripts/hud`.
**Visuel** : `client/themes/{Blue,Coffee,Green,Purple,Red}` =
`DejaVu.png`/`main.png`/`Radar.png` recolorés (vérifié : mêmes 5 atlas
par thème) + `UIData/imagesets/<couleur>/` — le set complet est décrit
dans `client-assets/themes.md` et `client-assets/UIData.md`.
**Preuve** : `godot-shots/m1_blue_windows.png` +
`m1_purple_windows.png` — chrome atlas swappé ET surfaces hand-built
re-teintées (corps navy StaticBackdrop, parchemins, onglets, boutons,
`__client_fill`). Méthode : `ThemePalette` = couleurs Irongray calées
ref-shots + delta pixel des régions natives (`StaticBackdrop`,
`ListboxBackdrop`, `NewTitlebarMiddle`, `ButtonNormal/Hover`,
`TabButtonMiddle*`) échantillonné dans l'atlas thémé
(`UiTextureCache.SamplePixel`) ; les `StyleBoxFlat` de EeSkin sont
enregistrées à la création et **mutées en place** au switch
(`ApplyThemePalette`, appelée par `SettingsApplier.ApplyTheme`).
Approximatif : `GreyPanel`/`DarkButton`/`KeyCell`/`BarTrack` restent
gris fixes (panneaux HUD translucides) ; Coffee = même atlas que la
base dans les assets copiés → rendu identique à Irongray (comme le
natif) ; couleurs de contenu (HP/EXP, quêtes, encre) hors palette par
design.

### M2 — Contours au pixel ⬜ *(à revérifier — ancien : prouvé live 28/10)*
Bandeau titre dégradé sombre, coins arrondis, liseré argenté 2 px, coins
ornés ; encadrés internes (onglets, listes, boutons « ? »).
**Visuel** : `DejaVu.imageset` (`FrameWindow`, `Titlebar*`, `Tab*`,
`MultiLineEditbox`, boutons) — comparaison crop ref/capture par type.
**Preuve** : `godot-shots/m2_final.png` +
`m2_native_cells2.png` vs `ref-shots/profile.png`, échantillonné :
- encadré valeur stats `EmbedTextLab` = composite `Static2*` → fond
  rendu **(193,189,179) = identique ref** ;
- cellule label `HintTextLab` (`FrameEnabled=False`) = dégradé
  `HintFrameBackdrop` natif (haut 201 vs atlas 200, bas 246 vs 249) +
  encre noire (0,0,0 vs ref ~3) — corrigé : `PanelThemeKey` Label→
  `« normal »` + centre de composite préservé en fond quand la frame
  est coupée + suppression des flats codés en dur de `BuildNativeStats` ;
- titre `Title` (frame+bg coupés dans le layout) = texte noir sur
  parchemin nu ✓ ; boutons « ? » = `HelpButton*` natif ; liseré
  argenté (132,134,133) 2 px sur le bord fenêtre ; titre/chrome
  `NewTitlebar*` dégradé natif.
Approximatif : écart résiduel de compression verticale du dégradé
(label top 201 vs ref 218 — même image, ~13 px étiré différemment) ;
les `*TextLab` d'autres panneaux héritent du même correctif global
(visuel natif gagné — à surveiller au fil des chantiers) ; fond du
viewport 3D du personnage plus sombre que le gris ref.

---

## 2. HUD permanent (U)

Pour chaque élément : `hud-interfaces.md` §1 = référence fonctionnelle.

### U1 — `TopLeftVitals` (cadre joueur)
- **Serveur** : S→C 280 `PlayerDataUpdate` (HP/MP/EXP/classe via attrs ;
  attr `0x2c` = classe active). Clic droit portrait → menu : Reset
  donjon = `TextCommand` (C→S 3, cmd reset) ; paramètres de loot groupe
  = `TeamOperation` (C→S 79, flags roll) — handler `TeamHandlers`.
- **Visuel** : `MiniInfoWnd.layout`, `ref-shots/TopLeftVitals.png`,
  portrait 3D tête (B2/B3), mini-rectangles trait héroïque + classe
  (M12) — icônes de classes : chercher `CharIcon`/`classIcon` dans `re/`
  + `client-assets/UI-uiicon.md` (`classIcon*`, `NatureIamge*`).
- **État** : ⬜ à revérifier (ancien : prouvé live) (run `--loot-test --mock-party`,
  `godot-shots/u1_portrait_menu.png`, `u1_loot_rules.png`,
  `u1_after_reset.png`) :
  - Clic droit bloc vitaux → menu groupe (« Party loot rules… »
    grisé si non leader + « Reset dungeon »). Le binding est sur la
    racine — Face a `MousePassThroughEnabled` chez le natif.
  - « Reset dungeon » → `ZoneServerNodeGroupReset` (C→S 163) :
    aller-retour prouvé en solo, serveur répond 316 texte 1697.
  - Dialogue loot = `NewLootRuleWnd.layout` natif (2 radios groupe
    champ : Random=2/Taunt-exclusive=3 ; 3 radios roll : White/Green/
    Blue=0-2), radios pré-cochés depuis `PartyState`, OK →
    `TeamOperation` C→S 79 op0+op3 (serveur `TeamHandlers`, log
    `TeamOp char=… -> refusé` attendu hors équipe réelle).
  - 381 `ClientTeamInfo` → `PartyState.ApplyTeamInfo` (9=loot,
    10=roll) + couronne `LeaderIcon` câblée au roster 324.
  - M12 : `classIcon` = texture réelle (`CharSelectScreen.ClassIcon`
    → `UI/uiicon`, JoBIamge%02d natif @0x00a83470) posée sur le slot
    natif du layout — plus de TextureRect ad hoc.
  - **Bloqué-donnée** : `CharIcon` (trait héroïque) — le natif affiche
    `NatrueIamge{feature-52000}` (features 52001-52008, @0x007904d0)
    mais aucune source trait n'existe en DB/schéma/NCID → slot vide
    (comportement natif d'un perso sans trait). Faux ♛ retiré.
  - **Approximatif** : cadres de groupes DejaVu/Border du dialogue
    non dessinés (radios sur fond sombre) ; libellé « Taunt-exclusive »
    rogné au bord (fenêtre native ~119 px) ; textes EN traduits des
    libellés zh-TW du layout ; item loot grisé pour non-leader au
    lieu d'ouvrir en lecture seule ; effet réel du TeamOperation non
    testable sans 2e client.

### U2 — `EXPBar` / `UltimateBar`
- **Serveur** : ratio = EXP courante (attr 600/`ExpForLevel`) ÷ seuil
  `S_Level.ini` (col 1 EXP, col 10 EXP classe — tranché par
  `ProgressionParsers.ParseLevels`) ; client : même table `C_Level`.
- **Visuel** : `DoubleEXPbar.png`, vert pomme + violet, précision au
  clic droit (XX → XX.XXX), icônes B4 à gauche. `UltimateBar` : cachée
  (feature custom post-portage).
- **État** : ⬜ à revérifier (ancien : prouvé live) (28 oct.) —
  `godot-shots/u2_exp_precision.png` (menu 4 radios `XX.XXX%` coché)
  + `u2_exp_bars.png` (icône classe `JoBIamge` sur le slot natif).
  Sondes : `--exp-test` (clic droit réel sur la barre → menu).
  - Désasm `rf_game` `CUIFunctionBarWnd` (FUN_008961b0 @008961b0) :
    `SoulBackGrand/icon` ← `JoBIamge{classe}`, `expBackGrand/icon` ←
    `NatrueIamge{trait}` ; format `"%6.3f%%"` courant/seuil +
    tooltip `"%s: %lld / %lld"` (FUN_008959d0/00895b80).
  - Corrigé : barres en `MouseFilter.Pass` (le clic droit *sur* la
    barre n'atteignait pas la fenêtre) ; icône classe câblée sur le
    slot natif `SoulBackGrand/icon` au lieu d'un overlay codé ;
    faux « ♛ » retiré ; tooltip « EXP: cur / max » ajouté
    (`LevelTable.ExpForNext`/`ClassExpForNext`) ; API morte
    `SetExpRatios` supprimée.
  - **Approximatif/bloqué** : `expBackGrand/icon` vide — le trait
    héroïque (feature `NatrueIamge`) est absent serveur/DB, même
    blocage que `CharIcon` U1 ; remplissage réel non visible (admin
    EXP=0 dans le niveau → « 0.000% » conforme).

### U3 — `MinimapPanel`
- **Serveur** : boutons = ouverture de fenêtres (mail C→S 95, itemmall
  70, altars locaux, Arena/Battlefield C→S 144/147 → 746-753, Rank
  S→C 763, events) ; zoom/molette local ; position joueur = conversion
  monde→carte unique (H11, bornes par map).
- **Visuel** : `RadarUI_Ver2.layout` + `Radar.imageset` (mapping boutons
  vérifié : `MapNormal`=Arena, `TerritoryNormal`=Battlefields,
  `LtyNormal`=Star Diamond, `IGLtyNormal`=Eden Gem, `ALNormal`,
  `ColNormal` à confirmer `re/`) ; minimaps `client/UI/map/Sxxx*.dds`
  (cf. `client-assets/UI-map.md`).
- **État** : ⬜ à revérifier (ancien : prouvé) (`godot-shots/u3_minimap_z1/z2/z4.png`,
  sonde `--zoom-test`) — zoom ×1/×2.25/×3.5 via `ZoomInBtn`/`ZoomOutBtn`
  + molette (mapSize/pos suivent, marqueurs à taille native constante
  repositionnés). **M8** : marqueurs = icônes natives `UI/uiicon/mapNNN`
  (tables rf_game FUN_008194b0/FUN_007ed830) — PNJ → `IconOf` catégorie
  c_npc col20 (défaut map005 emblème, 傳送→map011, 銀行拍賣場→map009,
  採集生產→map028, 情侶→map017, 圖書館→map015, 羊駝王→map012,
  魔方能量活動→map055 cube) ; mob→map053, joueur→map002 ; symboles
  quête 345 → map021/023/024 (remplace les lettres) ; aires REVIVE →
  map010. **Fix** : les ~100 triggers `gateway_area` (quête/donjon
  invisibles) ne sont plus dessinés — le natif ne les marque pas.
  Approximation : distinction weapon/armor marchand absente (la
  catégorie 商人 est unique → map005 pour tous) ; trésor = point or
  (pas d'icône native prouvée) ; sym2 des marques quête non utilisé.

### U4 — `QuestTracker` ⬜
- **Serveur** : liste suivie C→S 50/87 (`QueryMissionStates`,
  `MissionListOp`) → S→C 388/`UpdateMissionState` ; clic objectif →
  C→S 160 `MissionGoto [mid][type][npcId][flag]` (serveur prêt,
  `MissionHandlers`) ; auto-follow local ; « ! »/« ? » PNJ = S→C 345
  `UpdateNPCSymbols` ; tracking C→S 186.
- **Visuel** : lignes + surbrillance survol ; `ZoneBanner` = avancement
  au centre ; icônes `client/UI/uiicon/mission*.dds` (bulles !, ?,
  marqueurs — cf. `client-assets/UI-uiicon.md`).
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--quest-test=50342
  --flow-test --track-test --hud-open=questlog`). Tracker fidèle au
  ref `QuestTracker.png` : titres rouges, objectifs indentés à anneau
  creux, onglets Track/Unacquired, « Use Portal Stone »+« Background »,
  scroll, minimise. Journal : losanges orange sur quêtes suivies,
  « ◇ Track All » (remplit/efface ≤10 slots), hint shift+clic, détail
  « ◇ Use Portal Stone »+« Go » (160 flag=1 si coché, sinon
  auto-follow). C→S 186 `SetMissionTrace` [u16 count][count×i32] padé
  à 10 — observé zone `[Zone] SetMissionTrace ids=[…]` + persistance
  Mission `DBA 'SetMissionTrace mission_trace01=…'` ; restauration
  prouvée au relogin (`50->348 trace=1488`). **Fix serveur** :
  `ZoneEntryFlow` interceptait 186 en requête silencieuse → handler
  `TeamHandlers` mort (retiré de la liste). Fix client : clé
  `--hud-open=questlog` non gérée → alias → `ToggleQuests`.
  Sonde `--track-test` : vrai shift+clic injecté sur l'arbre → 186.
  Captures : `godot-shots/u4_tracker_final.png`,
  `u4_questlog_final.png`, `u4_restore.png`. Approximations : « Track
  All » re-clic efface tout (sémantique native non confirmée) ;
  losange orange des quêtes suivies (couleur exacte non confirmée).

### U5 — `ChatPanel`
- **Serveur** : C→S 34 `ChannelMsg`, C→S 3 `TextCommand` ; S→C texte
  indexé `TextOutput`/`TextOutputIndex` ; canaux (normal, battle,
  custom) filtrés localement.
- **Visuel** : onglets Normal/Battle/…, saisie, journal combat ;
  `ref-shots/` chat ; style de texte natif.
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--chat-test`, sonde UI
  ChatPanel.ChatProbe — vraie saisie EditBox + Entrée injectée →
  `TextSubmitted` → NCID 34). Canaux natifs confirmés par désasm
  `lapis::CPlayerFactory::ChannelMsg` (zone @ 0x08128350) :
  0 Say, 1 MutuallyHelp/Peer, 2 Team/Party, 3 Family/Guild, 4 Node/
  Zone, 5 General/World, 6 Rumor/Buzz. Préfixe `/o` = canal entraide
  (textid 4834) — `/x` implémenté : `/o`→1 prouvé, s/p/g/z/w/b→0/2/3/4
  /5/6 (approximation — table complète native non extraite) ; `/x`
  seul bascule le canal + sync bouton radio. Ligne entière colorée
  par canal (ref : `<System>` rouge, `<Peer>` magenta) — bug
  `&lt;` littéral corrigé, couleur étendue du préfixe à toute la
  ligne, log : fond sombre + marge gauche + molette (MouseFilter.Pass).
  Fil : `D 34 ch=0` → `E 268` echo say, `D 34 ch=1` (« /o » retiré) →
  `E 327` echo, `ch=2` idem ; zone.log `[Zone-Entry] CHAT ch=0/1/2`.
  Enter/Escape focus natifs déjà en place. Captures :
  `godot-shots/u5_chat.png`. Approximations : whisper = canal 1 +
  cible (sémantique wire native du tell non prouvée) ; onglets « + »
  custom non créés ; ◀▶ ne paginent pas (7 boutons tiennent) ;
  EmojiPalette = Reforged (absent vanilla) — toggle simple gardé ;
  316 serveur envoie « 1-100 » tronqué (résolution TextIndex serveur
  incomplète — hors scope client).

### U6 — `SkillBarWindow` (hotbar)
- **Serveur** : C→S 53/54/55 (`EnterNodeShortcut`, `UpdateShortcut`,
  `GetShortcutOfPage` — `PlayerHandlers` l.120-136 ; valeur objet =
  `0x40000000|id`) ; clic droit/touche → sort C→S 9/16/17 ou item
  C→S 19 ; 3 lignes configurables.
- **Visuel** : `FucntionBarWnd.layout` ; icônes `client/UI/skillicon/`
  (sorts) et `UI/itemicon/` (items), `ref-shots/` hotbar.
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--shortcut-test`, sonde
  HotbarPanel.ShortcutProbe). Modèle natif retrouvé dans rf_game :
  espace d'ids **plat** `page*10+slot` (FUN_00884810) — rangée 1 =
  page courante (▲/▼ + PageText, 11 pages 0-10), rangées 2/3 = pages
  fixes **6/7** (id 10+ → +50 → 60-69/70-79), 100-109 = extras.
  Implémenté : `_flat[110]`, restauration de TOUTES les pages du 351
  (`bars[p]`→`flat`), drops → 54 `{Page=flat/10,Slot,Value,OldValue}`
  (move → OldValue=valeur déplacée, sinon remplacée), activation clic
  droit/touche (sort → 9/16/17, item → double-clic existant), Ctrl+
  touche → rangée 2 (labels natifs C1..C+), LockBtn bloque drop/drag
  de sa rangée, drag-out des slots pleins ({itemId/spellId+fromFlat}),
  ▲/▼ → ChangePage → 55 lazy si page absente → 352 → Apply +
  RaiseChanged. Fil : `D 54 page=0/6` ×3, `D 55` → `E 352` mask+
  valeurs, `D 17` cast 50011, persistance cross-session prouvée
  (7 pages au 351 au login suivant). Capture :
  `godot-shots/u6_hotbar.png`. Approximations : extras 100-109 non
  rendus (slots 11/12 vides) ; rangée 3 = page 7 sans touche dédiée ;
  locks/visibilité non persistés client (ini natif) ; repli sorts
  connus en slots vides limité à page 0 (démo H7, non natif) ; slot
  3e rangée cachée par défaut.

### U7 — `MenuIconsPanel`
- **Serveur** : local (ouvre les fenêtres P*).
- **Visuel** : `FunctionMenu.layout` + icônes menu dans
  `client/UI/uiicon/` (`function*.dds`, `menu*` — catalogue).
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--menu-test`, sonde
  MenuIconsPanel.MenuProbe — vrai signal Pressed par icône + diff des
  contrôles nouvellement visibles). 9/9 icônes fonctionnelles :
  Charact→CharacterInfoWnd, bag→BackpackWnd, Mission→QuestWnd,
  skill→ClassWnd, Commu→SocialPanel, City→GuildPanel,
  ItemMall→ItemMallWnd, Pet→Elf, Sys→MainMenuWnd ; re-clic referme.
  Bug corrigé : le panneau natif CommunityWnd s'appelait d'après son
  layout → `ToggleByName("SocialPanel")` ne trouvait rien (renommé
  `SocialPanel` en BuildNative comme le repli). Visuel : badges CN
  `Fram`/`MissionFram`/`skillFram` (set:CNDejaVu auto1, jamais pilotés
  nativement) masqués — bordures argentées conformes au ref. Capture :
  `godot-shots/u7_menu.png`. Déjà faits avant : hover/pushed natifs
  (TextureHover/TexturePressed), raccourcis I/C/K/J/N/M/L/Échap,
  labels `*Text` (Alpha=0, sans référence native) masqués.
  Approximation : icône familier toujours visible (le natif la montre
  seulement si familier géré — le ref la montre, conservé).

### U8 — `BuffBar` + `NerfBar`
- **Serveur** : S→C 299/300/301/315/372/445/446 (enchants, états,
  stacks, timers) ; annulation clic droit → C→S 18/85/127/128 ;
  handler `BuffBridge`.
- **Visuel** : `BuffWnd.layout`, icônes `client/UI/skillicon/`,
  tooltip B5 (titre blanc / desc rouge, `shot_0045`).
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--enchant-test`, sonde
  BuffBar.BuffProbe — découverte paresseuse car HudExtensions monte
  les barres après BuildAll). Fil : `//self_use_effect 10052 600` +
  `11195 120` → `E 299` ×2 → slot Buff visible (bord droit
  affleurant la minimap x≈1022, conforme ref) + slot NerfBar
  (liseré rouge, accolé à gauche de la BuffBar après plaquage
  TopRight de la grille) ; vrai clic droit injecté (ParseInputEvent)
  → `D 85` eid=0x2744 → `E 300` → buff retiré, debuff persistant ;
  nettoyage `//remove_enchant`. Tooltip B5 unifié : couche TipRect
  par slot (les SlotImage→TextureRect n'avaient que le tooltip Godot
  par défaut). Note native : les enchants instant-only (procs
  d'attaque, ex. 11163) ne créent PAS de status → pas de 299 —
  debuff persistant requis (11195 cmds stats 3101/2055/1004).
  Captures : `godot-shots/u8_buffs.png`, `u8_after.png`.
  Approximations : debuffs regroupés en NerfBar séparée à gauche
  (doc projet §51 — le layout natif a aussi une rangée Debuff dans
  BuffWnd, masquée) ; icônes enchants cachés (48478…) filtrés par
  Hidden serveur.

### U9 — `ZoneBanner`
- **Serveur** : état combat (272 melee / sort reçu), textes système
  `TextOutput`, annonces de zone.
- **Visuel** : bannière centre-haut (Entering/Leaving Combat,
  annonces), ✅ Leaving vu live.
- **État** : ⬜ à revérifier (ancien : validé en jeu 2026-10-28 (`--combat-test --clone-mob=
  --banner-shot`). Correctifs : position native mesurée sur
  shot_0037 (bande texte y=188-216 sur 878p → OffsetTop 152 en
  720p, était 100) ; armement étendu — 272 avec self **cible ou
  attaquant** (le natif entre en combat dès qu'on frappe aussi),
  et **281 dégât impliquant self** (sort reçu/infligé, flag
  Restore exclu — les soins ne comptent pas). Sondes : `--clone-
  mob=<id>` envoie `//clone_monster` au démarrage du combat-test
  (spawns naturels hors vue sur S001) ; fin de test →
  `AutoAttacker.Cancel()` + `//kill` (chemin de mort normal, 305)
  car le serveur accepte encore les coups sur le handle mort ;
  `--banner-shot=<base>` capture les deux transitions
  (`_enter.png`/`_leave.png`). Fil réel : `D 3` clone_monster →
  `E 272` (self attaquant puis mob riposte) → Entering affiché ;
  `D 5` attaques → `E 305` mort → arrêt des coups → 6 s → Leaving.
  Captures : `godot-shots/u9_banner_enter.png`, `u9_banner_leave.png`.
  Fil non observé en live : 474 ScreenMessage (bind posé, le
  serveur ne l'émet pas dans ce flux) ; 281 self (même chemin
  ArmCombat, pas de sort de mob disponible).

### U10 — `TargetFrame` + `CastBar` ⬜ (ancien live 2026-10-29)
- **Serveur** : sélection C→S 124 au clic (→ `E 444`) ; infos cible
  S→C 444 `[i32 handle][u8][i32 ToT]` — le 3ᵉ champ = cible de
  l'entité (`[mob][0][charId]` au lock d'aggro, golden
  `0aabffff0002c2eb0b`) → « Target's Target » natif, avec repli
  inférence 272. PV cible via 280 (attr 0). Cast C→S 9 → `E 279`
  (castDs → barre) → `E 288` blast → `E 281` hit/heal ; `E 410`
  (refus cooldown Soul Return) géré ; `E 444` handle 0 = délock.
- **Visuel** : `TargetInfoWnd.layout` — portrait anneau orné, LV,
  nom jaune, HP/MP, « ? » inspect ; `TargetBg` = ToT (titre
  « Target's Target », nom, HP/MP). CastBar = progression rose +
  secondes centrées, **sans nom de sort**.
- **Preuve** (run unique `--combat-test --clone-mob=21702
  --cast-spell=50388 --cast-shot --banner-shot`) :
  `D 3` clone → `E 444 [mob][0][charId]` → `E 279` (Bear's Rage
  castDs=10 → barre) → `E 288` → `E 281` dmg 299 ; puis 3 casts
  instantanés résolus (`E 410` sur Soul Return en cooldown →
  candidat suivant) ; `//kill` → `E 444` delock → `E 305` →
  Leaving Combat.
  Captures : `godot-shots/u10_banner_enter.png` (bannière + cadre +
  ToT + floater), `u10_cast.png` (barre ~50 % + ToT),
  `u10_banner_leave.png`.
- **Approximations** : 125 non utilisé (le serveur y répond
  Field3=HP, sémantique différente — non prouvé) ; buffs cible = 4
  slots câblés sur 10 du layout, non observés live (mob sans
  enchant) ; inspect 143→460 câblé mais non prouvé (cible joueur
  requise, un seul client) ; fiche mob native à venir ; 16/17/13
  (cast zone/self/break) non exercés — 17 reste le repli sans cible ;
  cast 50388 non appris envoyé via `--cast-spell` (le serveur ne
  vérifie pas la possession — gate class mask + cooldown).

### U11 — `PartyHud` ⬜ (ancien live 2026-10-29)
- **Serveur** : cycle complet sur fil réel avec un 2ᵉ client
  (`Eden.Bots --scenario=team` : entre en zone, auto-accept le 318
  par 30 `[s16 1][i32 inviter][u8 1][s16]`) :
  `D 29` invite → `E 324` roster 2 ; `D 86` ping `[type,x,z]` →
  `E 387` écho identique ; `D 28` (charId peer) kick → `E 324`
  réduit à 1 ; re-`D 29` → `E 324` ; `D 90` promote → `E 316`
  (texte 0xd6) + `E 324` flags inversés ; `D 28` (self) → `E 317`
  → fin. Codecs client 86/90 ajoutés (387 existait, Field1/2/3).
  79 `TeamOperation` (loot) déjà prouvé en U1.
- **Visuel** : `TeamWndFrame.layout` — lignes membres (LV, nom,
  HP relatif max observé, MP pleine — non transmise, couronne
  `LeaderIcon` natif) ; `PositionIcon` natif visible quand la
  position 392 est connue → clic = ping 86 de sa position ;
  clic droit = Whisper / **Promote leader** (90, si chef) / Kick /
  Leave via le vrai popup ; 387 → marqueur orange clignotant ~8 s
  sur la minimap ; buffs membres 372 → `EntityBuffs` ; HP membres
  recopiés du `EntityRegistry` (280 → e.Hp, handle=charId).
- **Sonde** `--party-test --party-peer=<charId>
  [--party-shot=<base>]` : invite→roster→ping→kick (menu réel)→
  re-invite→promote (menu réel)→leave ; captures différées d'une
  frame (QueueFree/layout asynchrones).
- **Preuve** : `godot-shots/u11_roster.png` (couronne sur moi) +
  `u11_leader.png` (couronne transférée au peer) ; fil complet
  loggé ; `[party-test] done`, 0 erreur, 414 tests verts.
- **Approximations** : MP membre = barre pleine (le 324 ne la
  porte pas) ; HP membre ne bouge que si le membre est visible
  sur le même node (registre) ; `PositionIcon` natif réutilisé
  comme bouton de ping (déclencheur natif du 86 non prouvé —
  aucune capture wire 86 native) ; kind=1 arbitraire dans les
  3 s16 du ping.

### U12 — `PetFunctionBar` / `TreasurePickup` / `DamageFloaters` / `GoldHud` / `MountHud` ⬜ (ancien live 2026-10-29)
- **Serveur** : elf réel en base (`elf` loc 0, template 21834 → pet
  M144010) → `E 412` ; `D 107` mode 0 rappel de l'état persisté
  (status=2) → `E 513 flag=0`, puis mode 2 → `E 513 flag=1` + `E 418`
  ; `D 3 //clone_monster 63402` (drop 100 % équipement 28585) →
  `E 298` ; `D 124` → `E 444` ; pression réelle du bouton Slot1 de
  `PetFunctionWnd5p` → `D 188` instr 1 → `E 470` cmd=1 → le pet a
  tué un mob seul (`E 272` floaters). Sac réellement plein
  (32+30 slots) → `E 329` trésor au sol (« Etherworld Energy »).
  `D 35` émis (compté par la sonde) → refus 0x4f9 → trésor
  **persistant** ; `D 6` DropItem `[s16 type][s16 slot][s16 sac]` →
  `E 275` ; second `D 35` → `E 274` (insert slot libéré) + `E 386`
  + `E 330` despawn ; `D 107` mode 0 rappel → `[pet-test] done`.
- **Visuel** : `PetFunctionWnd5p.layout` natif (6 slots + PetMode +
  Lock/Close) visible uniquement à l'invocation ; icône AttackIcon +
  cadenas/× confirmés au zoom ; familier bleu rendu en monde ;
  trésor au sol pickable + notification « Obtained Etherworld
  Energy. » (canal 7) ; floaters 272 déjà prouvés U9/U10 ;
  `GoldHud`/`MountHud` confirmés absents (or = inventaire, monture
  = item/skill) — `.uid` orphelins supprimés.
- **Sonde** `--pet-test [--pet-mob=<id>] [--pet-shot=<base>]` :
  invocation→shot barre→clone→sélection→pression bouton→kill→
  trésor→pickup refusé→DropItem→pickup OK→rappel ; compte les
  `D 35` via `Session.Frame` (refus prouvé, pas seulement persistant).
- **Correctifs session** : `ZoneServerDropItem` (6 C→S) ajouté ;
  `PetFunctionBar.Refresh` renomme les enfants avant `QueueFree`
  (les nœuds morts gardaient `_pfb0` → Godot renommait en
  `@Button@…`, bouton intraçable) ; `TreasurePickup.BeginPickup`
  extrait (chemin fil partagé clic/probe) ; bind barre paresseux
  (race `_Ready`).
- **Preuve** : `godot-shots/u12_pet.png` (barre + familier) +
  `u12_loot.png` (chat « Obtained » + familier) ; fil complet
  loggé ; 0 erreur, 414 tests verts.
- **Approximations** : la sonde appelle `BeginPickup` directement
  (l'input synthétique press/release passait sous un Control HUD
  avant `_UnhandledInput` — picking écran réel non rejoué) ; sorts
  pet 101-105 non exercés (elf de test sans sorts de combat) ;
  PetMode 187 câblé non exercé ; deuxième trésor laissé au sol
  (expire via sweep 300 s).

---

## 3. Fenêtres principales (P)

### P1 — `InventoryWindow` (M3, codé à valider)
- **Serveur** : C→S 7 `SwitchItemLocation` (ack 277), 19 `ItemUse`,
  84 `DivideItem`, 8 `UpdateEquipment`, 89 `BagDueDate`, tri
  `ArrangeItem` (620/858 → OK) ; capacité = 32 + sacs (`BagCapacity`,
  `InventoryHandlers` l.1255) ; drag 2-clics optionnel (paramètre Chr).
- **Visuel** : `inventorywnd.layout`, onglets All/Potions/Gear/Town/
  Special, or+points, barre de fonctions : Archive, Fortify, Fusion,
  Craft racial, Backpack manager, Stall, Special currency, tri — icônes
  `client/UI/uiicon/` (comparaison `shot_0017` ; `Stall*`,
  `SpecialCoin*`, `AlpacaCoin`, `Bag*` dans imagesets/catalogue).
- **Reste** : prouver déplacement live, tri réel, icônes exactes.

### P2 — `ProfileWindow` (M4)
- **Serveur** : stats via S→C 26 `EnterNodePlayerData` + 280 attrs ;
  équipement via `EquipSlotsPanel` (slots serveur) ; % (ACC, CRIT,
  EVA…) = formule `rating/(rating+K)` à décoder dans `re/` ;
  titres = S→C `Appellation` (C→S 93/94).
- **Visuel** : `EquipmentWnd_new2.layout` (+ `CharacterWnd.layout`),
  `profile.png`, `profile-Title.png`, `Profile-Achievements*.png` ;
  7 slots accessoires (silhouettes + losanges bleus), sets Cloth/Light/
  Heavy (Fast Dress C→S 846/847), 9 résistances, onglets Title/Fame/
  Achievement.

### P3 — `ClassWindow` ex-`SkillsWindow` (M5)
- **Serveur** : talents = C→S 148/149 (`SetAAPoint`/`SetAARate`,
  arbre `C_Talent.ini`) ; certificats 181/182/183 → 509/489 ; switch
  classe C→S 168 → 489 ; achat de sorts C→S 52 `BuySpell`,
  C→S 12 `UseSkillPoint` ; classe active attr `0x2c`.
- **Visuel** : `JobWnd_NA.layout`, `NewSkillList.layout`,
  `JobGlyph.png` (roue des classes), 7 ref-shots classe ; cadenas +
  « Class Requirement » (`c_class.ini`), Class News, Skills/Certificates,
  Set Outfit, bandeau « Switched Class to X ».

### P4 — `QuestWindow` (M9)
- **Serveur** : C→S 50/87/51 (`QueryMissionStates`/`MissionListOp`/
  `GiveUpMission`) → S→C 388 état/objectifs/récompenses ;
  abandon/tracking/share.
- **Visuel** : `MissionLog.layout` ; récompenses en icônes
  `client/UI/itemicon/` + qté (au lieu de texte brut), arbre par zone,
  habillage M2.

### P5 — `WorldMapWindow` (M8 + H11)
- **Serveur** : marqueurs PNJ par catégorie `c_npc`/`t_npc` (col 20) ;
  portails → C→S 88 `Transport` ; « ! »/« ? » = S→C 345 ; bornes par
  map (H11 — une seule conversion monde→carte partagée minimap).
- **Visuel** : `ZoneMapUI.layout` ; fonds `client/UIData/imagesets/
  mapworld*.png` + `worldmap*` (marqueurs/pins), `map*.imageset`/`Bigworld*`
  pour les icônes de fonction ; légende « City Features ».

### P6 — `SocialWindow` + `GuildWindow` (M16)
- **Serveur** : amis/blocage/whisper via World (`Friend*`,
  `UpdateBlackList`, `Tell`) ; recherche C→S 103/104 ; lover
  154-157 → 475-479/597/599. Guilde : C→S 49 create, 81 update,
  82 emblem, 159 donate, 161 statue, 130-132 storage →
  S→C `ClientAllFamily*` (Mission/DBA 28-47).
- **Visuel** : `CommunityWnd.layout`, `FamilyInfoWnd_NA.layout`
  (✅ Infos), Members/Announce/Recruit =
  `FamilyOrderWnd_NA`/`FamilyMessageWnd_NA`/`FamilyFindingWind.layout`.

### P7 — `ElfWindow` + `ElfBook`
- **Serveur** : C→S 105-113 ; S→C famille ci-dessus (`ElfBridge`) ;
  `elf.elf_id`/`elf_serial` côté DB.
- **Visuel** : `PetWndV3.layout` (géométrie ref), stats/skills/
  nourriture/invocation ; grimoire à monter.

### P8 — `CraftWindow`
- **Serveur** : recettes via `data/db` (tables craft/racial —
  `C_Production*`/`RacialSpecial` à confirmer dans `re/`) ; craft via
  FeatureDialog + ops items (7/84) ; pas de NCID craft dédié prouvé à
  ce jour → à confirmer `reindex opcode`/désasm.
- **Visuel** : fallback 🔨 → layout natif à chercher dans
  `client/UIData/layouts/` (`CraftWnd*`/`Production*`).

### P9 — `TransportWindow`
- **Serveur** : C→S 88 `Transport`, portails 15 `CheckNodeExit`,
  arrivée S→C 393.
- **Visuel** : `TransportWnd.layout` (Transportation Array = cristaux).

### P10 — `SettingsWindow` + `InspectWindow` + `MessageBoard`
- **Serveur** : Settings local (`gamedata/settings.cfg` — ne pas
  forcer) ; Inspect C→S 143 `LookEquip` → S→C 460 ; MessageBoard
  C→S 138-142 → S→C 455-459.
- **Visuel** : inspect = rendu équipement + stats ; board
  `MessageBoardUI`/`MsgBorardFWin` (monté 🔶).

---

## 4. Commerce / services (S)

### S1 — `ShopWindow`
- **Serveur** : C→S 22 `BuyItems`, 23 `SellItems`, buyback
  S→C 764-767 ; fermeture C→S 63 `LeaveShop`.
- **Visuel** : `ShopWnd.layout` (achat/revente, prix, fixAll/fixOne →
  réparation 45-48).

### S2 — `BankWindow` / `NeobankWindow` / `FamilyStorageWindow`
- **Serveur** : C→S 60-62 (deposit/withdraw/getdata), 69 expand ;
  famille = NPC service 19 → 130-132 → S→C 755-757.
- **Visuel** : `StorageUI.layout` / `StorageUI2.layout` (Neo Bank
  Reforged, vue `rf_neobank`) / `FamilyStorageWnd.layout` ;
  `StorageItemCell` partagé.

### S3 — `TradeWindow`
- **Serveur** : C→S 36-42 (request/accept/add/remove/gold/confirm/
  cancel), 56 reset, 91-92 ready/unconfirm.
- **Visuel** : `TradeWnd.layout` (2 colonnes + or + confirm/lock).

### S4 — `VendingWindow` (stall)
- **Serveur** : C→S 71-80 (`AskVendor*`, `VendorBuy`/`SellItem`/
  `Close*`) ; vue acheteur incluse.
- **Visuel** : `StallUI.layout` + `StallBuy.layout`.

### S5 — `AuctionWindow`
- **Serveur** : C→S 114-118 (`AuctionBuy/Sell Query|Buy|Cancel`) →
  S→C 425-428/733-737.
- **Visuel** : `AuctionWnd.layout` (recherche, enchères, My Sales) ;
  live à prouver.

### S6 — `MailWindow`
- **Serveur** : C→S 95-102 (`MailOpenBox/Query/Read/Delete/Send/
  GetItem/GetGold/Return`) → S→C 68-77 (Mission) ; envoi cross-zone
  `MissionServerMailSend`.
- **Visuel** : `MailWnd.layout` natif (boîte, pièces jointes,
  Take All, envoi).

### S7 — `ItemMallWindow` (+ Loyalty)
- **Serveur** : C→S 70 `ItemMallReceivableEnter`, 83 `ItemMallUseItem` ;
  points = `UpdateSpecialCoin` (World) ; mails itemmall
  `MissionServerItemMallMail*`.
- **Visuel** : `ItemMall.layout` + `ItemMaillItem.layout` ; vues
  `rf_itemmall` pour les extensions Reforged.

### S8 — `FeatureDialogWindow` (forgeron/identifieur/fusion…)
- **Serveur** : réparation C→S 45-48 → S→C 342 (`--repair-test`
  prouvé) ; identify 64-65 ; strengthen 119-120 ; inlay 67 ;
  spellstone 121 ; décomposition/dye via ops items.
- **Visuel** : dialogs de service NPC génériques (monté 🔶) ;
  icônes service dans `uiicon`/`imagesets`.

### S9 — `LotteryWindow` / `MagicBoxWindow`
- **Serveur** : loterie C→S 129 `GoLottery` (+ count `DBA`) ;
  MagicBox = codec dédié `MagicBoxPackets` (preuve en place).
- **Visuel** : 🔶 ; `MagicBoxWindow` supprimé puis remis ? — état à
  revoir vs HANDOFF (Magic Box retirée).

### S10 — `NpcDialogueWindow`
- **Serveur** : C→S 10 `TalkToNPC`, 11 `ReplyDialog` → S→C 285
  (dialogue, quêtes, boutique/service lié) ; check S→C 768
  `NPCTalkCheck`.
- **Visuel** : `NpcTalkWnd.layout` (texte, réponses, accept/rendre).

---

## 5. Événements / PvP / divers (E)

### E1 — `DuelDialog`
- **Serveur** : C→S 135/136 → S→C 451-454 — **chaîne complète prouvée
  live**.
- **Visuel** : `DuelRequestWnd` (chrome natif monté).

### E2 — `BattlefieldWindow` / `TerritoryWindow`
- **Serveur** : C→S 144-147 (`BattlefieldJoin/Leave/ListQuery`) →
  S→C 746-753 ; territoire S→C 760-762 + `FamilyBattleInfo` 833.
- **Visuel** : `BattlefieldListUI`, `TerritoryMainWnd` ; icônes
  `client/UI/uiicon/territory*.dds` (cristaux, cf. catalogue) ;
  `CompetitionSchedule.png` (bracket tournoi).

### E3 — `ColosseumWindow` / `RecruitWindow`
- **Serveur** : colisée S→C 773-777 + mission 118-123
  (`EnterColosseum`, modes, rooms 778-781) ; recruit = recherche de
  groupe (C→S 103/104 ou op dédiée — à confirmer `re/`).
- **Visuel** : `ArenaMain`, `Recruit` (montés chrome natif).

### E4 — `RankWindow`
- **Serveur** : S→C 763/108 `RankInfo` ; PvP top 837.
- **Visuel** : `RankMainWnd` ; bouton minimap associé.

### E5 — `RollTreasureDialog`
- **Serveur** : tirage Need/Greed — NCID à confirmer
  (`reindex search Roll -b zone` / FEATURE-MATRIX) ; lié au loot
  groupe (flags 79).
- **Visuel** : `RollUI` (monté).

### E6 — `DeathWindow`
- **Serveur** : C→S 20 `Revive`, 44 `Resurrect` (types revive
  0/1/5).
- **Visuel** : `ReviveMenuUI` (monté) ; `Injured.png` overlay.

### E7 — `AchievementWindow` / `DailyRewardWindow` / `ResetDaily`
- **Serveur** : succès S→C 745/90 `AchievementUpdate` (+ mail
  récompense 824) ; daily : C→S 258 op=1/2 → S→C 615 + 274 item
  (**claim prouvé live**) ; reset S→C 825/829.
- **Visuel** : ✅ ; calendrier connexion.

### E8 — `DestinyWindow` / `CoupleWindow` / `Wedding`
- **Serveur** : Fate Road S→C 534/535/589/590/557 + actions
  203/204/223 ; couple 154-157/249/251 → 475-479/597/599 ;
  mariage S→C 848 + `WeddingPackets`.
- **Visuel** : `FateRoadWnd.layout` (✅), couple ✅.

### E9 — `HotelWindow` / `SystemServiceWindows`
- **Serveur** : hôtel = services NPC (dialogue 10/11 + op repos à
  confirmer) ; dialogs génériques = divers.
- **Visuel** : `HotelWnd` (chrome natif monté 🔶).

### E10 — Fenêtres restantes M16
Community, Guild Recruitment, Event/Daily, Pet, Portal, Channel
Settings, autels (`AltarWindow` ×3 de la minimap) — même méthode
deux-axes.

---

## 6. Écrans hors-HUD

### M13 — Écrans connexion / canal *(partiel)*
- **Serveur** : chaîne Login/Gateway (CNC_CL_* 35-39, `ClientChannel`)
  ; `Ch01 (Recommended)` rouge, `Maintenance` grisé depuis la liste.
- **Visuel** : `Login.layout`, `CWorldServerListWnd.layout`,
  `Selection_channel.png` (emblème cristal, ◀●▶, Official
  Website/Quit, trademark).

### M14 — Sélection de personnage *(partiel)*
- **Serveur** : World `ServerLoginCharacter`/list, delete/create.
- **Visuel** : `SelectCharacterWnd.layout` — carte perso haut-droite,
  flèches rotation, « Start Game » centre-bas, pile Set/Remove
  password, Select Server, Logout ; sans panneau Preview.

### M15 — Fond de connexion *(rouvert)*
`8fcef2a` non validé ; fondu splash → temple 3D stable sur 3 lancements.

---

## 7. BASSE (B)

### B1 — Ombres et lumière
Ombres dynamiques (cascades par preset), blob shadow complément,
éclairage par map, lanternes/cristaux émissifs, réglage Settings —
contrainte ≥ 60 fps (C5).
**Visuel** : lightmaps `client/map/model/*/Dark/` (ombres précalculées
natives — catalogue `client-assets/map-model-dark.md`) comme référence
d'intensité/placement.

### B2/B3 — Portrait vitals (zoom, yeux)
Recadrage front→menton (`TopLeftVitals.png`), texture visage/iris du
corps dupliqué — lié à H10.

### B4 — Icônes barres d'EXP
Mêmes icônes que M12 (trait/classe), `DoubleEXPbar.png`.

### B5 — Tooltip buffs stylé
Titre blanc / desc rouge (`shot_0045`), généralisé aux tooltips HUD.

### B6 — PetBar / CastBar chevauchement
Décaler l'une des deux quand familier invoqué pendant un cast.

---

## 8. HAUTE restante (H) — rendu & interaction monde

### H5 — Marqueur de ciblage + clic-droit trop loin *(codé, à valider)*
Cercle/cadre suivent la bonne entité ; distance de clic droit limitée.

### H6 — Faux PV du cadre de cible *(codé, à valider)*
Une seule source PV courant/max (444/280/298 vs `c_monster`) —
consolider.

### H7 — Mobs invisibles à Aven *(serveur corrigé, à valider)*
Pas de monstres sur nodes pacifiques (1/94/768 — serveur redémarré) ;
modèle manquant = repli visible + log côté client.

### H8 → U2 (déplacé dans HUD permanent)
### H9 — Modèle joueur chargé en retard *(rouvert)*
Cache corps + préchargement liste ; mesurer le délai en flux réel.

### H10 — Parties noires des modèles *(partiel)*
`--black-audit` : épaulières `#B202` corrigées ; planche de rendu
5 races × 2 genres × tenues + montures/monstres ; composites
`DyeMeshSurfaces` (masques R/V/B + artwork) — textures dans
`client-assets/char.md`.

### H11 — Coordonnées cassées *(codé, à valider)*
Une seule conversion monde→carte ; bornes par map ; 3 points connus /
map. Minimap (`UI/map/`) et Region Map partagent la fonction.

### H12 — HUD/panels vs curseur sur NPC *(à faire)*
`GuiGetHoveredControl()` prioritaire : aucun picking monde ni curseur
NPC quand le HUD est survolé.

### H13 — NPC sélectionnable trop loin *(à faire)*
Distance max de sélection (rayon streaming ~40 u) dans `PickEntity`
et ciblage Tab.

---

## 9. CRITIQUE (C) — stabilité, en dernier

### C1 — Exceptions au changement de map ⬜ *(à revérifier — ancien : corrigé, validé user)*
Garde `GroundHeight=null` + physique gelée pendant migration ;
audit des autres consommateurs de `_map` restant à rejouer.

### C2 — Gel/crash au changement de map *(partiel)*
Watchdog 5 s ; rejouer migrations + combat post-migration dans le
flux réel.

### C3 — Crash connexion lente *(rouvert)*
Keepalive/TTL en place ; rejouer attentes 2 min/10 min sur chaque
écran ; vraie déco = message + retour login.

### C4 — PNJ en double *(rouvert)*
`SpawnSceneNpcs=false` en live ; compter corps/nameplates/marqueurs
par `npc_id` avant/après migration.

### C5 — Performances à l'arrivée *(mesuré, à optimiser)*
`docs/PERF.md` (chargement ~25 s, RSS ~3,8 Go) ; pics > 33 ms :
préchargement sous loading screen, formats GPU, préchauffe shaders,
pas de GC bloquant ; ≥ 60 fps en 1280×720 dès la 1re seconde.

---

## Méthode rappelée (HANDOFF)

1. Lire le point + la ref `ref-shots/` + le layout natif +
   `docs/client-assets/<dossier>.md` pour les fichiers image utilisés.
2. `hud.Build("X.layout")` quand il existe ; `Find<T>(root, "Nom*")`.
3. Données : `raw_assets/data/db/` ou `client/data/db/` (Big5) ;
   `client-assets/data-db.md` décrit chaque table.
4. Serveur : `reindex opcode <n>` + `FEATURE-MATRIX.md` + handler C# ;
   jamais de paquet spéculatif.
5. Preuve : build + `--hud-open` + `--shot` + log propre ; case cochée
   uniquement après validation utilisateur.
