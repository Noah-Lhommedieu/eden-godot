# Direction visuelle — portail Limitless (web + Studio)

> Tokens partagés pour `limitless/web/frontend` (Next.js 16/Tailwind) et
> `limitless/studio` (Avalonia/SukiUI). Brief skill frontend-design
> (.agents/skills/frontend-design) : sujet = portail communautaire
> Eden Eternal "Limitless" (MMORPG anime-fantasy) — job = trouver une
> info de jeu vite (wiki), suivre sa progression (dashboard), se
> comparer (rankings). Audience : joueurs actifs du serveur privé.
> **Le site est en français par défaut** (FR/EN/PT-BR via cookie
> `locale`) — le monde du jeu reste en EN (libellés du client).

## Direction « aetherfall » — le hall de cristal d'Eden

V2 (2026-10) : la V1 « night of Eden » a été jugée « sans âme » —
bleu-nuit générique + cartes SaaS. Nouvelle direction assumée :
**le cristal d'Éden** — l'artefact central du lore du jeu. La nuit
vire au violet (aether), un accent cristal rejoint l'or et l'aurore,
et les panneaux adoptent le **chanfrein** des cadres d'items du jeu.

## Palette (9 valeurs nommées)

| Nom | Hex | Rôle |
|---|---|---|
| `void` | `#120F1E` | fond principal (nuit violette) |
| `panel` | `#1D1830` | surfaces / panneaux |
| `raised` | `#2B2450` | élévation, lignes actives |
| `ink` | `#F1EFFB` | texte principal |
| `muted` | `#9D96C9` | texte secondaire, labels |
| `eden-gold` | `#F0BC55` | accent principal (or fantasy) |
| `aurora` | `#7FE3C8` | accent Limitless/Ultimate, succès |
| `crystal` | `#9B8AF5` | accent tertiaire (cristal — catégories, lueur) |
| `ember` | `#E5647C` | danger / erreurs |

Violet foncé choisi car c'est LA couleur signature du cristal d'Éden
(l'écran de chargement, les gemmes, l'éveil) — distinct du bleu-nuit
générique des dashboards. Zéro beige, zéro acid-green, zéro card-kit
SaaS : hiérarchie par couleur et filets, pas par ombres.

## Typographie

- **Display/titres** : `Cinzel` (Google Fonts, 600/700) — capitals
  épiques, collé à l'univers roman-fantasy d'Eden.
- **Corps/UI** : `Sora` (400/500/600) — géométrique lisible, distincte
  des Inter/Roboto par défaut.
- Échelle : 12 (meta) / 14 (corps) / 16 (lead) / 24 (h3) / 32 (h2) /
  40 (h1). Line-length < 80 ch. **Jamais** d'eyebrow en capitales
  trackées, jamais de point médian décoratif "A · B · C" (les « · »
  ne servent qu'à joindre des faits de données : `Lv 12 · Boss`).

## L'élément mémorable (une seule audace)

**La flèche de cristal** (`.spire`) : l'échelle Limitless
matérialisée en obélisque de facettes trapézoïdes clip-path
or→aurore→cristal, révélée facette par facette au chargement de
l'accueil (stagger 55 ms). Le **chanfrein haut-droit des panneaux**
(`.panel`, `clip-path` 12 px) est la marque de fabrique discrète —
le vernaculaire des cadres d'items du jeu, appliqué en système.
Le reste reste discipliné : tables denses, filtres en haut de liste,
pagination serveur.

## Layouts cibles (ASCII)

```
ACCUEIL (max-w-4xl, allégée)      WIKI                        DASHBOARD
┌ titre Cinzel ←    [SPIRE]    ┌rail: datasets + counts┐  ┌persos: Lv, Limitless┐
│ tagline 1 ligne   facettes   │ ┌──┬──┬──┬──┬──┬──┐    │ │ ladder + perks      │
│ join/wiki         or→aur→cri │ │ic│ic│ic│ic│ic│ic│    │ └ dernier login, exp  │
│ accès rapide (liste filets)  │ └──┴──┴──┴──┴──┴──┘    └───────────────────────┘
├ statut (filet) ───────────── │ vue: vignettes|liste|table (persistée)
├ ladder Limitless | top jouers│ filtres f=champ:valeur + champ libre
├ derniers guides | annonces   └table détail 2 cols (stats ←→ méta)
RANKINGS
┌ podium 1-3 (Cinzel + or) ┐┌ table dense ┐┌ filtre classe ┐┌ mes persos+rang ┐
```

Alignement : contenu à gauche partout (sauf hero), pas de centrage
systématique. L'accueil ne montre que l'utile : statut serveur,
accès rapide (liste de liens, pas de cartes), derniers guides,
top joueurs, annonces si elles existent.

## Motion

Une seule orchestration : la flèche de cristal (facettes stagger) +
les barres du ladder (300 ms) au chargement. Transitions uniquement
en réponse à une action (onglet, filtre, tri, vue). Le point « live »
pulse à 2.4 s — signal vivant, pas décoratif.
`prefers-reduced-motion` respecté (reset global).

## i18n (WF1)

Trois locales : **FR (défaut), EN, PT-BR** — `locales/{fr,en,br}.json`,
clé `locale` en cookie (1 an), sélecteur FR/EN/BR dans la nav.
Chaque libellé UI passe par `t("clé")` (serveur : `getT()` ; client :
`useT()` du `I18nProvider` du layout). Les **données du jeu restent
en EN** (noms d'items/classes venant du client anglais) — seuls les
libellés d'interface changent.

## Décisions gardées (toujours valables)

- **Couleurs de rareté items** (`--q-common … --q-legendary`, classes
  `q1-q5`/`qb1-qb5`) : convention MMO, portées par le NOM et le filet.
- **Médailles podium** : or/argent `#C9CEDD`/bronze `#C08A5A` sur 1-3.
- **Tooltips wiki** `.wiki-tip` : stats clés au survol des tuiles.
- **Chip « N en ligne »** : polling 60 s, point aurora/gris.
- **Guides** : CMS admin (`wiki_articles` en fnaccount), markdown-lite.

## Copie

Voix utilitaire : noms parlants, erreurs qui disent quoi faire
("Aucun résultat pour « q » — videz la recherche"), pas de marketing.
Données en EN ; jamais de Big5 brut (marqueur « untranslated »).

## À éviter (rappels du skill)

- Pas de cream+terracotta, pas de near-black+acid, pas de kit SaaS.
- Pas de mot unique en italique/coloré dans les titres.
- Pas de labels 01/02/03 hors séquence réelle.
- Pas de fade-slide-up sur chaque section ni hover-transition partout.

## Studio (Avalonia)

Mêmes tokens (couleurs en `App.axaml`/`ControlTheme`). Structure :
nav latérale gauche (Items / Mobs / Icônes / .ini audit / Launcher /
Crypto), en-tête contextuel (dataset + recherche globale), zone de
travail (tables + formulaires). États vide/erreur rédigés.
