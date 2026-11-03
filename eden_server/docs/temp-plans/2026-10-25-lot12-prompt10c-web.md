# Lot 12 — prompt10c WEB (W1-W7) — plan d'action

Constats après investigation :

- **W2 partiellement non reproduit** : `f=color:violet` côté API renvoie
  bien les classes support (Cleric/Bard/Shaman/Sage) et le SSR :3000
  aussi. Hypothèses racines : (a) utilisateur a tapé `color=violet`
  → filtre silencieusement ignoré (pas de `:`) → tout affiché dont
  les bleus ; (b) aucun indice visuel de la couleur sur les tuiles.
  Correctifs : alias `=` dans GameDataStore.BuildPredicate + pastille
  couleur branche sur les tuiles certificats.
- **W5 root cause probable** : `accounts.role='Admin'` OK pour test123
  (vérifié en DB, login → role:"Admin"). Le rôle est figé dans
  localStorage `ll_role` au login — une session ouverte AVANT la
  promotion ne verra jamais le bouton. Fix : rafraîchir le rôle via
  `/api/auth/me` dans la nav quand un token existe.
- **W6** : ee-tools scrapé (bundle JS) : 60 certificats + combos par
  classe (cert1+cert2→buff), 35 classes × 18 talents {row,col,
  maxLevel,desc,deps} = grille Reforged (3 lignes = 3 arbres × 8
  slots). Référence : `tools/data/ee_tools_reference.json`.
- **W4** : S_Enchant col1 = code icône E#### (skillicon/E0141.dds
  existe) → exporter `icon` par slot talent. C_Talent Reforged =
  même layout V.2|10 → `talents_reforged.json` + `?variant=`.
- **W1** : item.useEnchant → S_Enchant (col40 slots « 1;2 », col41
  event, col42 chance %) + T_Enchant (col6 nom, col57 desc EN
  « When attacking, 5% chance… | CRIT Rate/EVA +189| ») → `effects[]`
  dans items.json, affiché dans le panneau stats.
- **W7** : pas de portraits monstres/PNJ dans les assets (modèles 3D
  uniquement) → glyphes enrichis et variés par sous-type/id.

## Étapes

1. `tools/export_web_data.py` : effects items (W1), icon par slot
   talent (W4), `talents_reforged.json` (W4), combos certificats
   depuis référence ee-tools (W6) — régénère les JSON.
2. Backend : alias `=` dans BuildPredicate (W2) ; `/api/talents`
   `?variant=classic|reforged` (W4).
3. Frontend : fiche item → panneau « effets » procs (W1) ;
   `app/wiki/[kind]/page.tsx` → redirect `/wiki?kind=…` (W3) ;
   refonte `/wiki/talents` maquette (rail classes icônes, 3 lignes
   horizontales d'icônes N/Max, +/−, KPs Remain/Entered, Refresh,
   onglets Classic/Reforged + Certificates) (W4) ; nav → refresh
   `/api/auth/me` (W5) ; pastille couleur certs (W2) ; glyphes
   enrichis monstres/quests/titles/npcs (W7) ; clés i18n FR/EN/BR.
4. Vérifs : export régénéré, `dotnet build` 0 err, `npm run build`
   0 err, routes 200 en curl, plan coché.
