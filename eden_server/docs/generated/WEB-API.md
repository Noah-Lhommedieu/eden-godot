# WEB-API — Limitless portal (backend :5154 dev)

> Généré pour l'agent frontend (WF). Toutes les routes wiki sont
> rétro-compatibles ; seules les **formes** des datasets
> classes/certificates/talents/monsters ont changé (voir ci-dessous).

## Auth

`POST /api/auth/login` `{username,password}` → JWT (`Authorization:
Bearer …`). `POST /api/account/register` `{username,password,email}`.

## Wiki (`/api/wiki`)

- `GET /api/wiki/datasets` → `[{id,count,untranslated}]`
- `GET /api/wiki/{kind}?q=&f=…&f=…&page=1&size=60` →
  `{total,page,size,items[]}`
- `GET /api/wiki/{kind}/{id}` → entrée (certificats : id string
  « classId:Name », pas d'id int — utiliser la liste).

### Filtres `f` (répétables, ET logique)

| Forme | Sens | Exemple |
|---|---|---|
| `champ:val` | égalité scalaire | `color:yellow`, `awakened:true` |
| `champ>v` `champ<v` `champ>=v` `champ<=v` | comparaison num. | `level>70`, `charLevel>=65` |
| `champ.sub:val` | dans un tableau d'objets | `maps.name:S001`, `drops.id:32631`, `bpDrops.classGroup:14`, `materials.id:44025` |
| `champ.*:val` | dans un tableau scalaire | `usedInCrafts.*:120`, `requiredClasses.*:4` (id) |

Datasets : `items monsters missions spells classes talents
achievements crafts titles npcs maps certificates`.

## Datasets — formes clés

### classes (61 entrées — ids 1-61 ; variantes races 101+ retirées)

```json
{"id":1,"icon":"J009","name":"Warrior","branchId":1,
 "branch":"Defense","color":"yellow","awakened":false,
 "parentClassId":null,"charLevel":null,
 "requiredClasses":[{"id":1,"name":"Warrior","level":10}]|null,
 "requirement":"…","short":"…","trait":"…","desc":"…",
 "statPct":{"hpPct":100,…},
 "licenses":[{"classLevel":10,"itemId":33272,"name":"Taunt I",
              "icon":"J501","buff":{"enchantId":52506,
              "name":"Taunt I","effect":"P-ATK +5%"}}],
 "certificates":[{"classLevel":10,"itemId":33272}],
 "skills":[{"id":50202,"name":"Double Cut","desc":"…"}],      // actifs
 "passives":[{"id":50201,"name":"Unshakable","desc":"…"}],     // passifs
 "rankBuffs":[{"rank":1,"classLevel":5,"reqLevel":33,
               "enchantId":45492,"name":"Warrior Bonus Skill 1",
               "effect":"Max HP +#17# and P-ATK +#10#"}]}
```

- `color` ∈ yellow|orange|green|violet|blue (Defense / Melee /
  Ranged / Support / Magic).
- **`licenses` = la piste de licences affichée en haut de la classe
  dans le client** (1 certificat par palier de 10 niveaux de classe,
  avec son buff) — à afficher en tête de fiche.
- `rankBuffs` = les « stats passives » gagnées tous les 5 niveaux de
  classe (S_ClassRankBuff, rang=floor(classLv×0.2)) — c'est ÇA que
  le joueur appelle « talents » sur la fiche classe.
- Noms corrigés : 39 Demon Summoner, 47 Lethal Arrow,
  48 Celestial Arrow, 53 Holy Lamentor, 54 Litigator,
  55 Dragon Soul, 56 Dragon Emperor, 57 Darkness Blade, 58 Holy
  Blade.
- Le bloc « knowledge » brut (« 1 A01101 A01201 0 ») a été retiré
  (données incompréhensibles).

### certificates (60 = 20 classes × 3, I+II fusionnés)

```json
{"id":"1:Taunt","icon":"J501","name":"Taunt","classId":1,
 "className":"Warrior","color":"yellow","stat":"P-ATK",
 "levels":[{"itemId":33272,"name":"Taunt I","classLevel":10,
   "buff":{"enchantId":52506,"name":"Taunt I","effect":"P-ATK +5%"}},
          {"itemId":34170,"name":"Taunt II","classLevel":40,
   "buff":{"enchantId":52676,"name":"Taunt II",
           "effect":"P-ATK +10%"}}]}
```

Filtres utiles : `f=classId:N`, `f=color:yellow`, `f=stat:P-ATK`.

### talents (61 classes × 3 arbres — pu ligne/ classe !)

```json
{"classId":1,"className":"Warrior",
 "trees":[{"tree":1,"slots":[{"pos":1,"effectId":52582,
   "name":"…","desc":"…","maxPts":20,"preEffect":null,
   "prePoints":null,"tier":1,"reqPoints":null}]}]}
```

`GET /api/talents` → `[{classId,className,trees}]` (61 entrées).
`GET /api/talents/{classId}` → arbre ci-dessus.
`GET /api/talents/rules` →
`{pointsPerClassLevel:1, maxCharacterLevel:100, maxAaPoints:120,
 costPerRank[0..20], costTableTotal[0..20]}` — coût d'un rang
(1-5→1pt, 6-15→2pts, 16-20→3pts) ; le niveau de classe ne dépasse
pas le niveau perso (cap 100).

### monsters

Champs ajoutés : `title` (« <Earth's Relic> » séparé du `name`),
`maps` (spawns réels scène :
`[{map:"S648",nodeId:648,name:"Trial: Rose Temple",x,y,count,
respawnSec}]` — null si le mob n'est spawné que par event/script,
ex. world boss), `bpDrops` (drops Break Point par groupe de classe
du tueur : `[{classGroup,className,id,name,icon,qty,chance}]` —
les drops normaux restent dans `drops`, affichés « sans BP »).

### items

`category` gagne **« Mount »** (détecté par enchant ride 2134 ;
`moveSpeedBonus` en %). `usedInCrafts:[comboId]` = dans quels
crafts l'item est un matériau.

### maps

`mapCode` (« S648 ») — jointure avec `monsters.maps[].map`.

## Personnages (`/api/account`, `/api/character`, `/api/progress`)

- `GET /api/account/characters` (auth) → liste.
- `POST /api/character` (auth) `{name,race,classId,gender,hair,
  face,hairColor,skinColor,eyes,eyes2,flag}` → crée un perso via le
  repo natif (identique au flux in-game).
- `GET /api/progress/achievements/{charId}` → succès (union des 3
  tables natives) `[{achievement_id,point,success_num,name}]`.
- `GET /api/progress/quests/{charId}` → `[{quest_id,status,name}]`.
- `GET /api/progress/talents/{charId}` →
  `{effects:[{effect_id,points,class_id,class_name,name,desc}],
    pools:[{class_id,class_level,aa_point,class_name}]}` —
  points AA investis par classe + pools disponibles.

## Admin (`/api/admin`, rôle Admin)

- `GET /api/admin/status` (public) `{online,total,now}`
- `GET /api/admin/news` (public) · `POST /api/admin/news`
  `{title,body}` · `PUT /api/admin/news/{id}` ·
  `DELETE /api/admin/news/{id}`
- `GET /api/admin/players?q=&page=&size=` → comptes + persos
- `POST /api/admin/players/{accountId}/ban` `{banned:true}` —
  accounts.state = -1 (banni) / 0.
- `POST /api/admin/command` `{kind:"gm|kick|message",target,
  command}` → file `fndb1.web_admin_commands`
  (`status=pending`). **TODO server-side** : le game-server doit
  dépiler cette table (poller) — endpoint stable pour le front.
  `GET /api/admin/command?status=pending` → historique.

## Divers

`GET /api/rankings/{level|class|achievements|pvp|limitless}` ·
`GET /api/limitless/{charId}` · `/api/limitless/top|perks|levels` ·
`GET /api/guides…` (mini-CMS).
