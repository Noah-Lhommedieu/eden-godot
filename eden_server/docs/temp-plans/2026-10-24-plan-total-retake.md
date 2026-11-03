# Plan total — retake (24/10/2026)

> Consigne du user : « fait des copier coller pour tout dans un même fichier,
> puis formattes les infos (tu dois négliger AUCUN détail dans mes paroles) ».
> Toutes les sections ci-dessous reprennent ses mots.

---

## PARTIE 0 — Retours user du 24/10 (verbatim, priorité absolue)

- « Je peux toujours pas bouger l'orientation du personnage via le clique droite (caméra) en me déplaçant pendant un timed cast skill. »
- « je vois bien les items dans buyback, mais si je clique droit ça rachète pas, et si je change d'interface entre buyback et Items sur le npc shop, l'item que j'ai vendu se rajoute une fois de plus dans le buyback (comme si j'en avais vendu un autre, alors que j'ai juste passé de buyback à Items). »
- « Dans le class talent, y'a certaines stats qui ne donnent pas la bonne stat (m-crit rate qui donne du m-crit damage, mais vérifie tout), et certaines stats qu'on peut même pas valider (cas du m-atk +0.5 sur le cleric, mais encore une fois vérifie pour TOUS les stats des class talent de chaque classes, je veux pas que tu corriges cleric mais toutes les classes, même les classes éveillé). »
- « Les autels marchent toujours pas »
- « Les boutons max pour up les skills généraux et class skill marchent pas. »
- « toujours pas le bon strafe animation sur monture pour déplacement que à gauche, ou que à droite. »
- « les sorts de buffs font toujours aggro les mobs »
- « Le site web ET LE limitless studio sont blanc et moche et même mal contrasté ALORS QUE JE T'AVAIS DEMANDÉ CHARTE GRAPHIQUE ET SHADCN, et pire encore y'a strictement RIEN de fonctionnel (quand je dis rien, c'est au sens littéral, sur le site frontend rien ne marche tu vas devoir le recoder 100%, je sais même pas comment t'as fait pour que réellement rien marche comme si t'avais fait exprès de faire de la merde) »
- « Pour finir je te demande des questions si t'as mal compris un truc, je m'attendais à 30 vraiment interrogation et à la place tu me poses 3 questions de merde dont tu as déjà les réponses. »
- « enfaite t'as tellement RIEN FAIT DU TOUT que je vais juste te redonner le prompt entier, tu peux le refaire en ENTIER littéralement étant donné que RIEN a été fait avec prompt5 etc »

## PARTIE 1 — Prompt original des bugs (verbatim, à traiter un par un)

- « Strafe visuel de monture toujours pas bon pour gauche et droite. »
- « AoE (sort de zone) fonctionnel » → considéré OK (marqué comme acquis).
- « Pour une raison bizarre quand je fais un sort de buff j'aggro TOUS les mobs autour de moi, ça n'a aucun sens »
- « Pour une autre raison bizarre les sorts de soins classique comme Cure (et d'autres) me mettent en mode combat contre moi-même, et pour le cas de cure qui ne fait QUE du heal, il est ajouté en status (ce qui n'a aucun sens puisque ce n'est pas un buff mais un soin) »
- « Quand les sorts de buff dans les status se terminent ils restent dans le status (dans tous les cas ils sont bug côté stats, mais ils ne partent quand même pas des status même en étant bug, corrige). »
- « les stats en class talent sont toujours pas rajouté (en jeu j'ai rajouté un point de cast spd et j'ai rien vu changé sur l'interface profile) »
- « Les pets ont le mode battle moyennement fonctionnel, déjà le pet spawn encore une fois sous la map et ils ne suit pas son joueur, et ensuite dans l'interface pet, un pet défini comme pray doit être attribué en mode pray (don de stats dynamique selon le pet, affiché sous forme de status et stats bien appliqué au joueur), et quand on met le pet en mode battle il apparait bug, mais dans l'interface il apparait même pas comme défini en battle ou en pray, il reste en "rest", donc les pets c'est full bug. »
- « On peut toujours pas bouger la caméra pendant un timed cast, en réalité on peut la bouger mais QUE en mode clique gauche, en gros on peut pas modifier la direction du personnage quand il bouge via le clique droit comme le permet le jeu de base, en timed cast skill on est bloqué sur le clique droit. »
- « Pour les 3 autels (eden gem, star diamond, eden crystal) le bouton transform pour faire la lottery marche pas. »
- « Quand on parle à un npc shop, quand on fait va dans "buyback" plus rien apparaît, et si on revient dans Items (la boutique de base du npc) tout disparaît aussi, il faut obligatoirement fermer l'interface et reparler au npc pour voir les items en vente. En gros techniquement la vente marche, l'achat marche, mais le buyback pour racheter ses items vendus marche pas car on voit page blanche sur l'interface. »
- « Le bouton "max" d'un general skill ou d'un max skill ne marche pas (il doit monter au max possible le level du sort selon les golds du joueur ou selon les class point CP de la classe) »
- « Y'a vraiment un GROS SOUCI sur la notion d'entrée en combat, pourquoi des sorts de buff de stats classique provoquent des mobs, et pourquoi un sort de soin m'auto-aggro ? ça n'est même pas des sorts malus envers un ennemi, on parle ici de simple sort de buff pour soi ou ses alliés, ils ne devraient pas mettre en combat (c'est que de la logique pour le coup, tu aurais dû savoir ça dès le départ). Il faut vraiment revoir la définition des possibilités que peuvent être les skills, entre les skills qui font des mélanges, des status de procs, des soins, attaque, et des skills qui mélangent ces notions pour divers cas (malus + attaque, soin + buff en zone etc), tu vas devoir retravailler la logique. »

## PARTIE 2 — prompt5.txt (3 chantiers — verbatim résumé)

- **Chantier 1 — Limitless Web CMS & Portail** : Next.js App Router + TS + Tailwind, backend ASP.NET Core Web API, `docker-compose.yml` multi-stages. Auth JWT rôles User/Admin, hash mot de passe identique au Game Server, création compte/personnage, dashboard joueur protégé. Wiki : parseur items/mobs (.ini ou BDD), recherche filtrée, données missions (quête main/daily/repeat, certificats), TOUS les class talent/license knowledge de chaque classe ET classe éveil avec simulateurs concrets pour class talent, les succès, crafts et objectifs/items requis, et pour ses personnages voir ses avancées. Page d'accueil (news, statut serveur) gérable depuis panel Admin (JWT + table rôle). Page `/rankings` : Top PvP (arènes/niveaux), Top PvE, Top Succès, Top Ultime EXP. Style : **shadcn UI** (`ui-shadcn-4.22.1.zip` dans `~/Téléchargements`).
- **Chantier 2 — Limitless Studio** : desktop C# Avalonia + MVVM. Éditeur items/monstres (PostgreSQL via Dapper + fichiers .ini). Vérifier unicité .ini, comment un launcher fait des majs client distant par rapport aux .ini, sécurisation/chiffrement des fichiers si possible sans tout casser, moyen de casser/remplacer les .pkg en sécurisant. Assets visuels Reforged : parser et afficher icônes 2D (.dds/.tga/.png), champ `ModelID` pour futures animations. SukiUI pour l'esthétique.
- **Chantier 3 — Limitless Level & Exp Ultime** : système de progression infinie (battle pass permanent, buffs modifiables/supprimables/ajoutés dynamiquement) actif uniquement au level cap actuel du serveur. Cap dynamique (config). EXP normale bloquée au cap → 1 point Exp Ultime par monstre tué SI monstre a max 5 niveaux de différence ET joueur a lancé ≥1 compétence sur le monstre (anti-leech). Paliers débloquent buffs permanents (drop×2, +500 PV max, −20% temps craft). Doc `LIMITLESS_SYSTEM_DESIGN.md` (BDD, backend, client modding UI). Base logique non impactante dans le serveur UNIQUEMENT SI possible de modifier UI client.
- Contraintes : organisé, dernières versions, charte graphique + pré-design UI complet pour chantiers 1 et 2, V1 release sérieusement propre, base de logique non impactante pour chantier 3. User sous Linux (Nobara) → solutions exécutables Linux.
- « si jamais tu as tout corrigé, alors je te demande de passer au plan d'action dans prompt5.txt » puis « en dernier recours les autres NCIDs liés aux systèmes de combats / characters / succès / title / équipements etc ».

---

## PLAN D'EXÉCUTION

### Phase A — Bugs serveur vérifiables headless (fichiers zone/core)

| # | Bug | Piste d'analyse | Statut |
|---|-----|-----------------|--------|
| A1 | Buffs aggro tous les mobs | SkillApply → aggro/provoke appliqué à tort ; vérifier flag "hostile" vs buff friendly ; chercher où `Aggro`/`Provoke` est émis après cast | [ ] |
| A2 | Cure auto-aggro + ajouté en status | Heal pur ne doit jamais set combat ni pousser StatusAdd ; vérifier branche "apply status" pour skills heal-only | [ ] |
| A3 | Buffs ne disparaissent pas des status à expiration | StatusManager tick/expiry → packet RemoveStatus jamais émis ou jamais retiré | [ ] |
| A4 | Talents : stats pas appliquées + mauvais mapping + stats non validables | `S_Talent.ini` col mapping → StatId ; audit TOUTES classes incl. éveil ; cast spd point n'apparaît pas dans profile → SpellAttrMods non émis ou mauvais attr | [ ] |
| A5 | Buyback : clic droit ne rachète pas + duplicata au changement d'onglet + page blanche | NCID rebuy handler, resend logic on tab switch (client renvoie open-buyback à chaque onglet ?) — vérifier ShopHandlers | [ ] |
| A6 | Autels transform (3 loteries) | NCID transform pas dispatché ou mauvais handler | [ ] |
| A7 | Boutons Max skill upgrade | NCID skill-upgrade-max : doit boucle tant que gold/CP suffisants | [ ] |
| A8 | Pets : spawn sous map, pas de follow, pray sans don de stats/status, battle state pas reflété dans interface (reste "rest") | spawn z + follow packet + pray→buff joueur + state echo | [ ] |

### Phase B — Patches client (rf_game.dll Reforged)

| # | Bug | Piste | Statut |
|---|-----|-------|--------|
| B1 | Strafe monture gauche/droite | adresse patch 0x75DC08 précédente — vérifier sens inversés | [ ] |
| B2 | Caméra clic droit pendant timed cast | client bloque yaw update en cast ; trouver gate dans binaire décompilé | [ ] |

### Phase C — Web : recode 100% (charte sombre + fonctionnel)

- [ ] C1 : Fix `.dark .theme-zinc` → `.dark.theme-zinc` (sélecteur descendant vs même élément = site blanc actuel)
- [ ] C2 : Recoder TOUTES les pages en composants shadcn réels avec fetch côté client fonctionnels : Accueil (news+statut serveur live), Login/Register (JWT vrai), Wiki 6 onglets (recherche/filtres fonctionnels, fiches détaillées), Talents (simulateur interactif), Rankings 4 tops, Dashboard (persos + Limitless), Admin (news)
- [ ] C3 : docker-compose multi-stages

### Phase D — Studio thème

- [ ] D1 : SukiUI dark theme forcé + contraste

### Phase E — prompt5 restant + NCIDs

- [ ] E1 : Compléter chantiers 1-3 selon prompt5
- [ ] E2 : NCIDs combat/characters/succès/title/équipements en dernier recours

### Vérification continue

- `dotnet build` 0 erreur par lot, `dotnet test` en fin de phase
- Redéployer zone :5201 après chaque lot serveur (ZONE_PORT=5201 impératif)
- Commit par bug

### Questions (bloquantes seulement — sinon décider seul)

1. Altars : le « transform » des 3 autels — c'est le mini-jeu loterie dans la fenêtre autel ? (je vérifierai via S_Altar/captures si doute)
2. Pets pray : le don de stats est-il affiché comme un status/buff standard dans la barre de status ?
3. Reforged uniquement pour les patches binaires (strafe/caméra) ou aussi le client projet ?
