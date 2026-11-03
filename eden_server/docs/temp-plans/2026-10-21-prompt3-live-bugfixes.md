# Plan d'action — prompt3 (correctifs live 2026-10-21)

> Copie exacte du prompt utilisateur `/home/ster2lune/Bureau/prompt3.txt`
> + sections ajoutées : vérifications, code, résultat, guide de test.

---

## PROMPT ORIGINAL (copie verbatim)

```
- J'ai des annonce "leaving combat" en plein combat pendant que je me fais attaquer, la notion d'entrée en combat et de sortir de combat je te l'ai expliquer ALORS CODE LA PUTAIN, et prend 100% des infos ->
Quand on prend aggro 2 mob ou plus, en quelque seconde a peine ils partent, il faut que le système "d'abandon d'aggro" soit bien ficelé, voici les cas :
1) Si hors dj dans une map classique, le seul moyen qu'un mob arrete de nous aggro et nous fasse sortir du mode combat est que le joueur s'éloigne trop des coordonnées où le mob a été aggro par le joueur.
2) Si dans un donjon ou instance (comme Battle time and space ou Vingot Lab ou Devastation Dimension), les mobs qui ne sont pas des boss n'arrête jamais d'aggro le joueur SAUF si le mob n'a pas pu toucher le joueur pendant plus de 3 minutes
3) Si est un boss ou élite ou mini-boss (qu'importe le lieu), si le joueur s'éloigne trop des coordonnées où le mob a été aggro par le joueur alors le boss arrête d'aggro et retourne a son spawn.

Je précise également que si j'attaque plusieurs mobs, ou que j'arrete d'en attaquer un quelque seconde, je peux pas le selectionner comme s'il existait pas visuellement, alors que je le vois me taper (le seul moyen de le cibler est via la touche TAB, mais via le curseur il est pas selectionnable).

- la commande /help indique rien du tout, je sais pas quel commande ou interface peut m'aider pour les commandes, il me faudrait un /help détaillé avec pour chaque commande les moyens de remplir, car "add_class id" je connais pas les id des classes, et c'est qu'un exemple car toutes les commandes sont incompréhensible a utiliser (il me faut un panel ou une database clair pour savoir quel commande faire, comme me give un item il me faut l'id mais je connais pas les id bref, sans compter le fait que la moitié des commandes comme par exemple level marchent juste pas du tout)

- Quand on est en cast time d'un skill a cast time, je souhaite créer une mécanique spécial qui fait qu'on peut bouger pendant un cast time (que les joueurs pas les ennemi) et l'animation doit permettre de se faire en bougeant également, c'est pas une mécanique existant dans le jeu de base mais je veux faire quelque chose d'original, ici je parle de tous les sorts qui ont un cast time (heal, dps, buff ou debuff etc)

- il faut que tu fasses marcher le système des status, les status sont des sorte d'état positif ou négatif, les status donc la nature est positif ou neutre (être sur une monture, avoir un buff du cleric par un allié ou autre etc), et les status a la nature négatif (nerf / debuff) sont dans l'autre UI dédié a ça, les deux existent déjà dans le jeu (actuellement les joueur bas level ont un status qui permet que toutes les portal stone sont pas payante en item, on peut tp a tous les objectif gratuitement, et un autre status "Invincible" qui fonctionne pas mais est présent pour rien, c'est un status qu'on a quelque seconde après une mort etc).
Tu dois faire fonctionner les status (actuellement j'ai un sort du cleric qui soigne sur la durée et il fonctionne pas car)

- Les objectifs de collect item on ennemy sont fonctionnel.
- Vendre un item dans un npc shop (genre une arme blanche etc) marche pas, il faut gérer la vente d'item dans les npc shop (clique droit pour vendre etc).
- Quand on achète des potions on voit bien le nombre augmenté mais quand on les consomme le nombre diminue pas visuellement (mais en réalité il diminue bien), corrige le problème visuel dans l'inventaire.

- Quand on passe des niveaux le système propose pas les quete au joueur mais les accepte directement sans son consentement, il y a 3 interface / UI différente pour l'accès au quete, le "Quest Log" qui est un gros panel de quete montrant tout (quete possédé, quête disponible dans le monde actuellement pour le joueur a son niveau d'avancement dans le jeu etc), ensuite il y a le "Available Quests" qui est un petit panel montrant les quete importante (principal de l'histoire du jeu) et qui permet quand on clique gauche sur une des quete qui est proposé que le joueur aurait oublié ,de directement ouvrir l'interface de talk npc quest pour proposer la quete au joueur, et enfin la dernière UI est l'UI de Suivie de quete, c'est pas une interface complète mais simplement une UI a droite de l'écran du joueur pour pouvoir suivre les quetes en cours ou les unacquired quest (sans rien ouvrir en interface). Tu dois également corriger le fait que l'UI de suivie de quete (qui est pas son vraie nom donc cherche bien mais tu l'avais appelé tracker de quêtes dans ton rapport de ma dernière requete IA) n'apparaissent plus du tout depuis quelque temps et je sais pas pourquoi mais cette UI est obligatoire pour jouer.

- Toute l'interface communauté marche pas au minimum coté client (que ce soit ami ou search player).
- Le souci des icones NPC est partiellement corrigé, il y a certain cas où on voit encore le mauvais icone dans l'interface talk mais en soit c'est corrigé pour beaucoup de cas.
- Tous les shortcut (raccourci) de classe sont fonctionnel !
- Cooldown de potion fonctionnel !
- Monture toujours pas équipable a cause d'erreur "Unable to use the item.", et visuellement dans l'inventaire une indication "0 sec. remain" comme si la monture était en cooldown d'utilisation.
- Interface profile du joueur avec ses stats etc pas du tout fonctionnel / actualisé selon les équipement (title honor ou general, certificat etc)
- j'étais lvl 2 cleric, j'ai reconnecté mon personnage et c'était écrit Lv1 cleric, j'ai taper un mob j'ai pris 2 level, ça peut être un rattrapage visuel du client ou je sais pas mais cherche.
- Quand on level up général on gagne pas les skills général (de classes de couleur), en gros certain skill sont généraux a une couleur de classe (comme le jaune avec l'ensemble des classe physique et tank) et certain skill sont catégorisé par leur classes (d'où les Class point pour level up les skills de classe et les gold pour level up les skill généraux), il faut que tu vérifie que quand on up la classe on gagne au fur et a mesure les skills de classe, et que quand on up character on gagne également les skills généraux pour chaque couleur de classe (jaune, orange, vert, bleu, violet).

- Item mall et Loyalty shop vide.
- Aucune commande admin rajouté réellement.
- Je veux que tu codes le système des sac a dos pour agrandir mon inventaire, il est petit et rien que pour mes test il m'en faudra de grande taille (les items existent déjà, t'a juste a coder toute la logique de placement des sacs dans l'inventaire, le rajout de slot possible etc)
- Quand un joueur est plein ou autre, les items doivent drop au sol et le joueur peut plus tard le récupérer (l'item au sol disparait si le joueur change de map ou il disparait au bout de 5 vraie minutes). Si jamais le joueur veut rendre une quete ou prendre une quete mais que la quete donne un item pour la faire ou que la quete donne en récompense des items mais que le joueur est déjà plein d'inventaire, il faut donner une error comme quoi il peut pas accepter la quete.
- Je veux que tu rajoutes le système de License Knowledge (qu'on appelle KP) qui est une sorte d'arbre de compétence où on peut mettre des points KP pour avoir des stats supplémentaire ou réduction de cooldown de skill etc, et je veux également que tu codes le système de certificat qui permet quand on level up différente classe d'avoir des certificat a placer dans des cases, et qui permettent d'avoir des stats bonus (ces certificat peuvent passer du niveau I a II selon le niveau actuel de la classe du certificat), et les certificat de n'importe quel classe peuvent être utiliser n'importe où (un certificat Hunter peut etre utiliser pour samourai etc), n'oublie le système des combo de certificat où certain certificat placé sous une combinaison précise font un bonus supplémentaire.

- Pour finir, rajoute un système de rate exp sur l'exp character et classes que donne les mobs, et met la rate a 100, je veux que mes tests soit rapide.

- Il existe certain "mob" qui sont par exemple des coffres ou des entité pour les quete qui ne peuvent pas bouger (comme un coffre ou autre) et qui actuellement se mouvoit comme des mobs normaux

- Quand je me connecte le jeu prend automatiquement des quetes sans que je le veuille, retire le fait que le jeu prend des quete par lui même.
- Logique d'appartenance des stats :
STR -> P-ATK
AGI -> Eva, Parry, ATK Speed, Cast speed, Move speed
INT -> M-ATK
WIS -> G-Healing, P-Healing
LCK -> Taux de crit phy, Taux de crit Mag, Acc

Uncap la move speed a 300 (de base c'est 200 max)
A part si les bon ratio pour STR et INT sont dans le code, voici leur ratio :
- 1 STR -> 80 P-ATK
- 1 INT -> 80 M-ATK

De façon custom, on rajoutera le fait que l'agi donne de la move speed (car c'est pas dans le jeu initial) :
1 agi 0.08 move speed

Pour LCK, WIS, AGI (hors move speed), il faut donner 1 pour 1 dans chaque stats.

Coté compréhension voici les infos :
- P-ATK et M-ATK des stats d'attaque phy et mag.
- ATK SPD -> la vitesse d'auto-attaque
- CRIT rate et m-crit rate -> taux pour critique un sort phy et mag (et auto-attaque c'est uniquement crit rate phy)
- CRIT DMG et m-crit dmg c'est le scale de dégât en plus quand on critique (100% = 2x dégât)
- ACC -> précision (les ennemi ont les mêmes stats que nous, donc leur eva contrebalance notre ACC et inversement notre EVA contrebalance leur ACC pour savoir si les mobs ou non on fait miss l'ennemi, pour rappel miss c'est un dodge et ça marche sur les sorts et auto-attaque de chacun)
- Cast Spd c'est la vitesse pour réduire un cast time de skill.
- DEF c'est une défense physique (cap 70%, a partir du cap atteint, une réduction jusqu'a 70% de dmg crit physique reçu s'alimente)
- M-DEF mêem chose que DEF mais c'est une défense magique uniquement, ça existe pas encore mais je vais la créer pour mon serveur.
- Parry est une notion spécial, elle ne fonctionne en combat que si le joueur joue une arme a 2 main, ça permet de réduire de 50% le  damage reçu de l'ennemi qu'importe le type d'attaque (mag ou phy c'est pas important pour la parry, qu'importe le type de damage ça divise par deux), le cap est a 70%.
- Block c'est uniquement fonctionnel si le joueur utilise un bouclier comme deuxième arme a une main (le bouclier possède la notion spécial "Off hand" propre aux bouclier), le cap est a 50%.
La dernière notion de stats est la résistance, dans le jeu il y a plusieurs élément physique et magique, voici les physiques :
- Slash
- Strike
- Pierce

Magique :
- Fire
- Lightning
- Ice
- Nature
- Holy Light
- Dark

Chaque arme possède un attribut physique (pour au minimum les auto-attaque ou les skills qui attaque en physique, l'arme appliquera sa res pour les BP de boss ou la résistance de l'ennemi), pour les éléments magique c'est que les skill magique élémentaire.



Tu as corrigé énormément de chose visuel et c'est cool car tu bloquais jusque là, mais il manque encore ENORMEMENT de correctif réel, beaucoup de correction sur le fait que le client n'affiche pas réellement ce que le serveur sait, ou alors l'affiche avec trop de retard ou nécessite un rattrapage des infos, dans tous les cas tu dois coder mieux le serveur pour qu'il puisse envoyer a chaque game client les vrais data dès le départ et en continue pour chaque action du joueur.

Si tu n'as pas de détail sur des test que tu attendais dans ce prompt, cela veut dire que tu les a bien corrigé.

Tant que tu n'as pas TOUT coder et corriger, tu ne t'arretera pas, tu peux coder pendant plusieurs heures.
```

---

## Reprise intégrale — audit à zéro (2026-10-21)

Les statuts et résumés antérieurs de ce fichier sont retirés comme preuve :
ils ne seront pas réutilisés sans reproduction. Chaque ligne ci-dessous est
à refaire depuis le code et les deux clients. « À vérifier » signifie qu'il
n'y a pas de validation utilisateur/client vérifiable, même si des classes
ou tests existent déjà.

### Incidents live bloquants à traiter d'abord

| Domaine | Évidence observée | État après correction locale | Validation encore requise |
|---|---|---|---|
| Inventaire `test3` | DB contient actuellement 46 lignes (22 conteneur0 + 24 conteneur1, aucun doublon); NCID25→312 émis; les anciennes exécutions lisaient seulement bag0; native cap=32. | Repository lit 0..4, capacites 32+30, le codec émet les conteneurs contigus; tests vérifient bitmaps et records à champs conditionnels; diagnostic `[Inventory312]` ajouté. | Reconnecter test3 et confirmer le log `22/32 + 24/30`, `invalidSlots=0`, puis l'affichage réel sur les deux clients. |
| Stats certificat/profil | `test3`: item 33281, slot 1, `UseEnchantId=52515`; T_Enchant indique « EVA +4% ». L'enchant s16 négatif n'était pas résolu avec sa clé u16; les blocs login rejouaient aussi le profil capturé. | Enchants normalisés s16→u16, refresh 296/492/493 à MigrationDone, contribution EVA + tests dédiés. Client Reforged `FUN_00a44bb0` confirme que les points raw scalés 0,01 et taux float sont distincts. | 415 tests passent. Reste à mapper 296 vers les offsets UI, confirmer la conversion EVA et valider profil/certificat sur les deux clients. |
| Slots équipement impossibles | DB test3 group0 contient loc 9 (EquipLoc9) et 35 (EquipLoc35); natif 9→7, 35→10, `GetWeaponItem` lit 7/8/10. Broken Demon Pot et book39329 ont EquipLoc0. | Resolver natif + normalizer avant311; retours d'items incompatibles vers un sac; `DoWeaponUnEquip` ports7/8/10 + 277 distinct par item auto-déplacé. | Build déployé; vérifier au prochain login canonicalisation/serials, puis drag/clic droit/swap/déséquipement live sur les deux clients. |
| Profil points/% | Screenshot : ACC 77/4.52%, EVA 69/13.24%, Parry 73/3.28%, Block 75/0.68%, ATK SPD 7.5/74%, CRIT 59.34/5.87%; utilisateur rapporte ~3800 EVA=100%. `rf_game` FUN_00a44bb0 affiche des entiers multipliés par 0.01 séparément des floats de taux. | La paire raw/% est confirmée côté client, mais le mapping de NCID296 vers ces champs et les formules restent non vérifiés; aucune nouvelle conversion code. | Suivre le parse 296 vers les offsets UI, déterminer raw vs rate/caps (dont EVA 3800), puis golden tests et vérification sur les deux clients. |
| DOT et teinte post-combat | Les logs d'une exécution précédente montrent 292/effect 52022 à chaque riposte. T_Enchant 52022 est Perforation (150 DoT / -100 MP); le report visuel ne change pas `LiveMob.Hp/Alive`. `player_enchants` ne contient pas 52022. | La source courante ne contient plus ces deux 292 génériques; le log est append-only et documente une ancienne DLL. | Retester la dernière DLL: aucune émission 292/52022 sur attaque standard; vérifier cible, teinte, tick DOT, fin d'effet et mobilité. |
| Mob 0 PV / personnage bloqué | Les attaques différées et plusieurs chemins pouvaient lancer `MobCombatLoopAsync`; les anciens logs montrent aussi 292/52022 injecté à chaque riposte. | Génération d'engagement exclusive testée; source actuelle sans 292/52022 générique; Release Zone PID 76725 redémarré. | 415 tests passent. Reste : reproduire combat sur la build courante et prouver en jeu l'absence de DOT/freeze/tint. |

### Matrice complète du prompt — tous les domaines repartent « à vérifier »

| ID | Exigence à réauditer depuis zéro | État |
|---|---|---|
| 1 | Aggro/leash hors instance, trash instance (3 min sans toucher), boss/élite, plusieurs cibles et sortie de combat | À vérifier en source + clients |
| 2 | Sélection souris des mobs, sélection TAB, sight handles et état clic après aggro/deaggro | À vérifier |
| 3 | `/help`, commandes GM, argumentaires, identifiants classe/item/sort et effet de chaque commande | À vérifier |
| 4 | Cast mobile joueur, animation, interruption manuelle, nouveau cast, mort et reconnect | À vérifier |
| 5 | Buffs/debuffs, HoT/DoT, monture, invincibilité, durée et persistance des statuts | À vérifier |
| 6 | Objectifs collecte/kill, récompenses de quête, erreurs d'inventaire plein, dialogue de proposition et consentement | À vérifier |
| 7 | Quest Log, Available Quests et tracker : listes, clics, progression et reconnexion | À vérifier |
| 8 | Vente NPC par clic droit, achat, buyback, stacks, or et notifications d'inventaire | À vérifier |
| 9 | Compteur visuel des consommables après utilisation | À vérifier |
| 10 | Communauté : amis, relation mutuelle, offline, blacklist, recherche, entraide, famille | À vérifier |
| 11 | Icônes NPC et sélection/interactions par type de NPC | À vérifier |
| 12 | Raccourcis spécifiques à la classe, pagination, persistance et affichage | À vérifier |
| 13 | Cooldowns potion/skills, coût, visuel et persistance | À vérifier |
| 14 | Monture : équipement/usage, durée restante, déplacement, vitesse et reconnexion | À vérifier |
| 15 | Profil : stats de base, équipement, titres, certificats, combos et rafraîchissement temps réel/login | À vérifier |
| 16 | Niveau perso et niveau de classe au login/reconnect; XP/CP et progression persistés | À vérifier |
| 17 | Acquisition/upgrades de skills privés et généraux; `spell_order` vs `spell_level`; refresh en session | À vérifier |
| 18 | Item Mall / Loyalty : catalogue, groupes/monnaies, limites, achat et livraison | À vérifier |
| 19 | Commandes de test pour AP, EP et items des trois autels via le pipeline normal | À vérifier |
| 20 | Porte-sacs : holder, slots, capacité active, transferts, rangement et persistance | À vérifier |
| 21 | Drop au sol si inventaire plein, durée de 5 min, ownership, ramassage et départ de map | À vérifier |
| 22 | License Knowledge/KP : points, arbre, coût, stats et cooldowns | À vérifier |
| 23 | Certificats : octroi niveau I/II, cross-class, slots, déséquipement, persistance et stats | À vérifier |
| 24 | Combos de certificats : résolution des sets, enchants, retrait et stats | À vérifier |
| 25 | EXP perso/classe ×100 (configuration effective et une seule attribution) | À vérifier |
| 26 | Mobs statiques (coffres/objets de quête), mouvement, aggro et interaction | À vérifier |
| 27 | Stats : STR→P-ATK, INT→M-ATK, AGI→esquive/parry/AS/cast/move, WIS→soins, LCK→crit/ACC | À vérifier |
| 28 | Move speed cap 300, AGI→0.08, cap/ordre de cumul des buffs et montures | À vérifier |
| 29 | Formules hit/evasion, crit phys/mag, crit damage et résistances sur sorts/attaques | À vérifier |
| 30 | DEF/MDEF, caps, réduction crit physique, parry gated 2H, block gated shield et caps | À vérifier |
| 31 | Résistances Slash/Strike/Pierce, Fire/Lightning/Ice/Nature/Holy/Dark et élément des armes | À vérifier |
| 32 | Cohérence durable UI ↔ serveur pour chaque mutation, entrée de node, changement de classe et reconnexion | À vérifier |
| 33 | Deux clients jouables avec le même protocole/serveur; aucune branche Reforged côté serveur | À vérifier |
| 34 | Equip types → slots canoniques; validation contre consommables et livres; réparation sans perte des lignes historiques | À vérifier |
| 35 | Conversion points→% de chaque attribut de profil (ancrage EVA ~3800=100%) | À vérifier |
| 36 | NCID 292 d'effet seulement sur proc natif; aucun DOT/tint auto-appliqué à chaque riposte | À vérifier |

### Méthode d'audit obligatoire pour chaque ligne

1. Suivre le code réel du chargement DB/data jusqu'au handler, service,
   codec et paquet client; noter les fichiers/fonctions.
2. Vérifier les champs/tailles avec `re/ghidra/<binary>/decompiled.c`,
   captures ou test bytes golden; aucune supposition de wire.
3. Ajouter un test de régression ciblant l'incident/sémantique; build/test
   Release avec zéro warning/erreur.
4. Cocher seulement après test client réel; sinon laisser « à vérifier » et
   documenter précisément le blocage ou l'hypothèse.
5. Ne jamais réparer des données persistées avec un DELETE/UPDATE massif;
   sauvegarde, transaction et confirmation explicite requises pour une
   modification destructive.

### Suivi courant

- [x] Les correctifs locaux compilent et passent les tests automatisés.
- [x] Build Release : 0 avertissement / 0 erreur; suite complète 415/415.
- [x] Zone relancée depuis la DLL Release (PID 76725); port 5201 et liens
  Ticket/World/Mission rétablis.
- [x] Tests 312 parse conditional fields/create_time/socket/combo/due_date;
  capture réelle test3 byte-par-byte encore à faire.
- [ ] Sens du certificat 52515 relié aux commandes natives et confirmé en jeu.
- [x] Garde d'une seule boucle mob par génération et annulation mort/deaggro
  testées au niveau état; preuve client après mort/deaggro encore requise.
- [x] Resolver équipement 9→7 / 35→10 + non-équipables rejetés; normalizer
  pré-311 est câblé. Mouvements DB/serials sur le prochain login restent à
  confirmer.
- [x] Conflits natifs 7/8/10 avec exception ClassId 0x14 et 277
  auto-déséquipement codés; test byte exact du 277.
- [x] Source sans NCID 292/52022 générique; vérifier absence dans la nouvelle
  session, les vraies capacités restent à implémenter.
- [ ] Mapping raw/% profile du parser 296 vers `FUN_00a44bb0` finalisé.
- [ ] Chaque ligne de la matrice a une preuve source, test et statut client.
- [ ] Les deux clients valident les corrections d'interface et de combat.
- [ ] Plans et master plan mis à jour sans réutiliser les anciens « fait ».

### Lot familiers de combat + stabilité combat (session en cours)

| Domaine | Preuve native | Implémentation | Validation encore requise |
|---|---|---|---|
| Pets kind 60 (« Combat Bunny Sprite » 23993) | `CItemFactory::UseItem` → `CKFElfManager::AddElf` quand kind=60 ; `S_Partner` (30 cols) + `S_PartnerGrow` (41 cols) désasm `CPartnerQuery::Deserialize` ; table native `elf` (10 slots, `loc` 0-9, status 0/2) | `PartnerParser`, `ElfRepository` (table `elf`, pas de séquence → MAX+1), `NativeElfCodec` (412/413/418/513), `ZoneEntryFlow.Elf.cs`, intercept kind-60 dans UseItem | Équiper/utiliser l'œuf en jeu → 413 → elf visible panneau familiers, sur les deux clients |
| 412 EnterNodeElfInfo | `CNC_CZ_ClientEnterNodeElfInfo::Serialize` : `[u16 n][n×43 champs][s16]` — l'ancien code envoyait un record brut sans count | Codec rebuild avec count + 43 champs/record + s16 terminal | Panneau familiers peuplé au login |
| Invocation 109 / ElfWork mode 2 (107) | `CallElf` @0x8073xxx : cooldown 60 s, CallElfBack puis `CNode::BornElfPet` (modèle `m%06d` du partner, monstre résolu par ModelCode+nom, ex. 21553 Bunny), status→2, 513 `[u8=1][loc]` + 418 `[loc][2][hp]` | `HandleElfCallElfAsync` + `HandleElfWorkAsync` (mode 0/2 interceptés, mix/loterie au dispatcher) | Invoquer depuis le panneau → pet visible, suit le joueur |
| Renvoi 108 / mort du pet | `CallElfBack` : status→0, 513 `[u8=0][loc]` ; hp persisté | `CallElfBackAsync` + `NotifyElfPetDiedAsync` au `FinishMobDeathAsync` | Renvoyer/faire tuer le pet → panneau cohérent |
| Renommage 228 | `ElfChangeName::Execute` : parchemin 40056/40156 requis+consommé, nom 2-16B, 418 `[loc][type=12][0][nom]` | `HandleElfChangeNameAsync` | Renommer en jeu |
| PetModeChange 187 / SendPetCmd 188 | mode→pet+0x3ec (2=passif) ; cmd→`CElfPet::DoAction` → 470 broadcast | `HandlePetModeChangeAsync` (tick pets respecte le passif), `HandleSendPetCmdAsync` | Mode passif/actif en jeu |
| Cast en mouvement ne démarre pas | `CastToLife`/`CheckCanCast` natifs : AUCUN check de portée serveur (le client gère la poursuite) | Gate silencieux supprimé — borne défensive 60 u → 410, jamais de silence | Caster en courant, vérifier aucun blocage ni freeze |
| Oscillation entrée/sortie combat au cast | Capture : `act` constant sur toute la chaîne, `0020 sel=0` au 1er cast puis `0020 sel=4`, exit `0000 sel=0` nouvel act | `ActionChain` n'est plus reset à chaque cast (reste ouvert jusqu'aux vraies sorties de combat — mort/stop/deaggro) ; releases sel=0 vs sel=4 selon `st.InCombat` à l'émission | Caster en combat → plus de retrait/remise du mode combat |
| Poursuite mobs saccadée | 326 annonce ~7 u/s (0x4725) mais le serveur déplaçait 3,75 u/s → sur-interpolation client + rattrapages | Step 2,15 u/tick 300 ms (=7 u/s annoncé) ; 326 broadcast à tous les témoins du node (était : cible seule) ; tick pets 1,5 s → 300 ms | Mobs suivent fluidement un joueur qui court/tourne |
| Pet elfe après migration | `BornElfPet` sur le node de destination | `RespawnElfPetAfterMigrationAsync` | Changer de map avec pet invoqué → pet réapparaît |

- [x] Build Release 0 avertissement / 0 erreur ; 422/422 tests verts
  (dont `ElfCodecTests` : 412/413/513/418/414 vs formats natifs).
- [x] Zone redéployée sur la DLL Release (PID 93123, port 5201, liens
  monde/mission OK, `fndb1` connectée).
- [ ] Validation client des 4 symptômes (pets, oscillation combat, cast
  mobile, poursuite) — **aucun de ces points n'est « fait » tant que
  non confirmé en jeu** :
  1. Utiliser l'item 23993 → elf ajouté (413) ; panneau familiers (412) ;
     invocation → pet visible et suivant ; mode passif ; renvoi.
  2. Caster un sort à cast-time **en courant** → le cast démarre.
  3. Caster en combat → plus de retrait/remise du mode combat.
  4. Courir autour d'un mob → poursuite fluide, pas de freeze.
