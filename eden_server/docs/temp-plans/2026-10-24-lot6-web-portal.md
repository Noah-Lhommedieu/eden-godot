# Lot 6 — Refonte Web Portal (prompt5 chantier 1)

## Symptômes
- `.dark .theme-zinc` dans themes.css = sélecteur descendant alors que
  `dark` et `theme-zinc` sont sur le MÊME <body> → thème sombre jamais
  appliqué (site blanc).
- Pages = coquilles 30-75 lignes, aucune interactivité (recherche,
  filtres, auth, dashboards vides).
- lib/api.ts = 12 lignes (pas de client API).

## Cible prompt5
- Auth JWT (register/login, hash identique au game server) + dashboard
  joueur protégé + création de personnage.
- /wiki : recherche filtrée items/mobs/missions/certificats +
  talents/licences par classe (et éveillées) avec simulateur.
- /rankings : Top PvP / PvE / Succès / Ultima EXP.
- Accueil : news + statut serveur, gérables via panel Admin (JWT+rôle).
- Style : dark shadcn.

## État backend (843 lignes)
Controllers: Auth, Account, Admin, Character, Limitless, Progress,
Rankings, Talents, Wiki — à mapper aux pages.

## Travaux
1. Fix thème : `.dark.theme-zinc` (même élément) dans themes.css.
2. lib/api.ts : client fetch typé (JWT en localStorage/cookie, base
   URL env `NEXT_PUBLIC_API_URL`, gestion 401→login).
3. Auth : pages login/register branchées sur AuthController, stockage
   token, guard client pour /dashboard et /admin.
4. Pages :
   - / (news + statut serveur via LimitlessController)
   - /login /register (formulaires réels)
   - /dashboard (persos du compte, progress)
   - /dashboard/new (création personnage)
   - /rankings (4 tops + filtres)
   - /wiki (recherche items/mobs/missions/crafts/achievements)
   - /wiki/talents (arbres par classe + simulateur d'allocation)
   - /admin (news, gating rôle Admin)
5. Build next + test visuel.

## Réalisé (2026-10-24)
- [x] Fix CSS : 12 sélecteurs `.dark .theme-*` → `.dark.theme-*`
      (même élément — le thème dark ne s'appliquait jamais).
- [x] Backend : CORS (:3000), `[AllowAnonymous]` sur /api/admin/status,
      iniDir 6×.. corrigé, gardes négatives WikiService.Parse
      (IndexOutOfRange), schéma réel (given_name/class_id/
      colosseum_point/personal_mission_states/class_talents/
      player_characters_achievement), migration accounts.role+email,
      register plaintext+MAX(id)+1 (compat game), CharRow record.
- [x] Frontend rebuild complet : api.ts typé, auth (login/register/
      dashboard/new char), wiki 5 onglets + recherche debounce,
      rankings 4 tops, talents simulateur (alloc + prérequis +
      cascade), admin (news + statut), nav auth-aware.
- [x] Vérifié live : login test123 JWT, characters réels, wiki
      items/mobs/quests/ach/crafts (200 chacun), talents (arbres
      Big5), rankings, register webtest1 → compte créé en fnaccount.
- [x] Preview browser :3000 ouverte.
- [~] Reste : validation visuelle utilisateur, éventuels liens wiki
      EN (data Big5 = référence), docker-compose test build.
