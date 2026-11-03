# Scènes et cartes — `map/`, `data/scene/`, `ui/map/`

Chaque map a un **code `S###`**. Ses fichiers :

| Type | Où | Rôle |
|---|---|---|
| `s###.fsm` | `map/model/s###/` | terrain (grille V7 EDENFR — spec `eden_godot/docs/FSM-FORMAT.md`) |
| `*.area` | `map/model/s###/` | zones/déclencheurs de la map |
| `*.ini` | `map/model/s###/` | config locale (Big5) |
| `*.nif` | `map/model/s###/` | props/modèles propres à la map |
| `s###.smp`/`.fsmp` | `map/sknodemap/` | objets nodaux animés (`SMP2.0`) |
| `s###.pgp` | `map/sknodemap/` | grille de pathfinding (`PathMapVer0001`) |
| `s###.csm` | `data/scene/` | ombres/lumière (Cascaded Shadow Maps) |
| `s###.ini` | `data/scene/` | réglages de scène (météo, ambiant…) |
| `S###s.dds`/`S###m.dds` | `ui/map/` | carte région 1024² / minimap 512² |

⚠️ Casse : le dossier `s002/` (fsm) et `S002/` (area/ini) coexistent
— résolution insensible à la casse obligatoire (`EdenVfs`).
`map/map.nif` = exécutable PE, pas un modèle (G12). Textures :
`map/texture/` (9 326 DDS), nommées par map ou partagées.

## Par map

| Map | Nom FR | Niveaux | Fichiers présents |
|---|---|---|---|
| S001 | Aven |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp, smp2 |
| S002 | Monts Calcaires | LV 1~8 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S003 | Colline Tranquille | LV 9~14 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S004 | Forêt Arc-en-Ciel | LV 15~19 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S005 | Monts Arides | LV 20~24 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S006 | Baie Bélouga | LV 25~29 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S007 | Bourbes | LV 30~33 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S008 | Vallée des Rois | LV 34~37 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S009 | Volcan Avila | LV 38~41 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S010 | Plaines Dorées | LV 42~45 | area, carte-ui, cfg, csm, dds, fsm, fsmp, ini, pgp, smp |
| S011 | Terres des Dunes Éternelles | LV 46~48 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S012 | Forêt de Delphes | LV 49~51 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S013 | Pic de Noireflamme | LV 52~54 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S014 | Marais Décrépits | LV 55~57 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S015 | Hautes-terres | LV 58~60 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S016 | Crocs Brumeux | LV 1~8 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S017 | ? | ? | carte-ui |
| S018 | Cabinet du Maire |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S019 | Vallée Feregal | LV 1~8 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S020 | Désert du Couchant | LV 52~53 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S021 | Jungle Céleste | LV 53~54 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S022 | Forêt Ensorcelée | LV 61~63 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S023 | Crête Diamantée | LV 62~64 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S024 | Pic du Maëlstrom | LV 63~65 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S025 | Vallée de la Mort | LV64~66 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S026 | Plaines Fleuries | LV66~68 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S027 | Falaise des Grands Vents | LV68~70 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S028 | Lagune Enchantée | LV 1~8 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S029 | Côte de Hurlevent | LV 55~57 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S030 | Jungle du Dieu Serpent | LV 58~60 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S031 | Île de Sakura |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S032 | Vallée de Vulcain | LV 69~71 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S033 | Plaines du Levant | LV 71~73 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S034 | Royaume Dévasté | LV 73~75 | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S035 | Palais Heldentod |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S036 | Plaines d'Elos | LV74~76 | area, carte-ui, dds, fsm, fsmp, ini, smp |
| S037 | Marina d'Edouard | LV76~78 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S038 | Royaume Durango | LV78~80 | area, carte-ui, dds, fsm, fsmp, ini, smp |
| S039 | Meteor's Landing |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S040 | Scenic Aurapoint |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S041 | Forteresse d'Éternité |  | area, carte-ui, fsmp, ini, jpg, nif, smp |
| S042 | Bill's Training Facility |  | area, carte-ui, dds, fsm, fsmp, ini, smp |
| S043 | Braider Army Base |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S044 | Tente 2 |  | area, carte-ui, fsmp, ini, nif, smp |
| S045 | Tunnel de l'Union de Valence |  | ini |
| S046 | Profondeurs du Mont Gowen |  | ini |
| S047 | Faubourg Populaire de la Marina d'Edouard |  | ini |
| S048 | Domaine de la Noblesse de la Marina d'Edouard |  | ini |
| S049 | Congrès de Braider |  | ini |
| S050 | Tour Kahlo |  | ini |
| S051 | Dôme Céleste | LV79~81 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S052 | Hauteurs Feudegivre | LV81~83 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S053 | Meeryasha | LV83~85 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S054 | Jungle Mayland | LV84~87 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S055 | Bassin Shaxia | LV87~90 | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S093 | Errea |  | ini |
| S094 | Aven rêvée |  | ini |
| S095 | Vieil Entrepôt |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S096 | Île aux Cerisiers |  | ini |
| S097 | Île du Mariage |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S098 | Arena Dungeon Place Holder |  | area, carte-ui, dds, fsm, fsmp, ini, smp |
| S099 | Création de Personnage |  | area, cfg, csm, dds, fsm, ini, nif, smp, smp2 |
| S100 | Mine Andrew |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp, txt |
| S101 | Carrière Angor |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S102 | Fort Verdure |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S103 | Canal Tassos |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S104 | Caverne de Schiste |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S105 | Éroda |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S106 | Morticora |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S107 | Racine Branda |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S108 | Palais des Malices |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S109 | Pourriture |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S110 | Abîme de Baram |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S111 | Coeur Baltaroï |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S112 | Rêve du Maire |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S113 | Fléau |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S114 | Chambre de Baïra |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S115 | Crypticône |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S116 | Temple de Delphes |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S117 | Veine Tordue |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S118 | Autel du Culte de Mara |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S119 | Chutes des Âmes en Peine |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S120 | Vortek |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S121 | Arsenal Fédéral |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S122 | Sturmfrau |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S123 | Abysse Glacée de Skleros |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S124 | Cratère des Géants |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S125 | Antre des Échos |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S126 | Sanctuaire du Mal à l'État Pur |  | area, carte-ui, csm, dds, ini, nif, pgp, smp |
| S127 | Grotte de la Marée des Ombres |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S128 | ? | ? | area, fsmp, ini, nif, smp |
| S129 | Lieu du rituel (niv. 80 - Vérité et Illusion IV) |  | ini |
| S130 | Orphidée |  | ini |
| S131 | Banque Meeryasha |  | ini |
| S132 | Maison de Marjorie |  | ini |
| S133 | Pays des souvenirs |  | ini |
| S134 | Tunnel éboulé |  | ini |
| S135 | Jungle Mystique |  | area, dds, fsm, fsmp, ini, pgp, smp |
| S136 | Palais Heldentod |  | ini |
| S137 | Bassin Shaxia |  | ini |
| S176 | Tour des Épreuves |  | csm, ini |
| S177 | Caverne de Schiste |  | ini |
| S178 | Palais des Malices |  | ini |
| S179 | Fort Loselle |  | csm, ini |
| S180 | Écarlate |  | csm, ini |
| S181 | Royaume Vide |  | csm, ini |
| S182 | Temple Perdu |  | csm, ini |
| S183 | Plaines Séraphines |  | csm, ini |
| S184 | Domaine Démoniaque Intérieur |  | csm, ini |
| S185 | Tombe du Samouraï |  | csm, ini |
| S186 | Pays des Merveilles Diablepourpre |  | csm, ini |
| S187 | Palais des Bourbes |  | csm, ini |
| S188 | Vallée du Sacrifice |  | csm, dds, ini |
| S189 | Donjon d'Éveil de Classe - Illusionniste |  | csm, ini |
| S190 | Class Awakening Dungeon (Place Holder) |  | csm, ini |
| S191 | Class Awakening Dungeon (Place Holder) |  | csm, ini |
| S192 | Class Awakening Dungeon (Place Holder) |  | carte-ui, csm, ini |
| S193 | Class Awakening Dungeon (Place Holder) |  | csm, ini |
| S194 | Class Awakening Dungeon (Place Holder) |  | csm, ini |
| S195 | Version de la Cabane n°1 |  | area, csm, fsmp, ini, nif, smp |
| S196 | Version de la Cabane n°2 |  | area, csm, dds, fsmp, ini, nif, smp |
| S197 | Nouvelle Cabane n°3 |  | area, csm, dds, fsmp, ini, nif, smp |
| S198 | Test Cottage : Océan |  | area, carte-ui, dds, ini, nif, pgp, smp |
| S199 | Test Cottage : Chaleur |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S201 | Base des Vilrequins |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S202 | Salle Ulta |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S203 | Crocvenin |  | area, carte-ui, csm, dds, fsm, fsmp, ini, nif, pgp, smp |
| S204 | Écarlate |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S205 | Siècle |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S206 | Manoir |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S207 | Frontière de Basel |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S208 | Prison Ossuaire |  | area, carte-ui, csm, fsmp, ini, nif, pgp, smp |
| S209 | Guillotine |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S210 | Temple Spirenfer |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S211 | Temple de la Rose |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S212 | Labo Vingot |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S213 | Cap Hurlant |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S214 | Abysse de Siècle |  | csm, dds, ini |
| S215 | Grotte Non Répertoriée |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S216 | Dimension Dévastée |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S217 | Cratère Météoritique <Mode Multijoueur> |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S218 | Tour Kahlo <Mode Multijoueur> |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S219 | Iyernafalo <Multijoueur> |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S220 | Terre sacrée de Kamodo (multijoueur) |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S221 | Temple aquatique <multijoueur> |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S222 | Labo Antigravité <multijoueur> |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S223 | Grey Lynn <multijoueur> |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S224 | Bataille Spatiotemporelle |  | ini |
| S225 | Vallée Batelo (Groupe) |  | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S226 | Allolans (DJN multijoueur) |  | area, carte-ui, dds, fsm, fsmp, ini, pgp, smp |
| S227 | Temple enchanté (DJN multijoueur) |  | ini |
| S228 | Fort Verdure <Mode Multijoueur> <Évaluation> |  | area, bmp, csm, dds, fsmp, ini, nif, smp |
| S229 | Canal Tassos <Mode Multijoueur> <Évaluation> |  | area, bmp, csm, dds, fsmp, ini, nif, smp |
| S230 | Caverne de Schiste <Mode Multijoueur> <Évaluation> |  | area, bmp, csm, dds, fsmp, ini, nif, smp |
| S231 | Éroda <Mode Multijoueur> <Évaluation> |  | area, bmp, csm, dds, fsmp, ini, nif, smp |
| S280 | Tour des Cieux Hall du Purgatoire |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S281 | Tour des Cieux Autel Sinistre |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S282 | Tour des Cieux Salle Fleurépine |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S283 | Tour des Cieux Zone de la Mélodie Sacrée |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S284 | Laboratoire de Réno |  | area, carte-ui, csm, dds, fsmp, ini, nif, smp |
| S299 | ? | ? | ini, nif, pgp, smp |
| S301 | Bataille de la Forêt |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S302 | Bataille du Désert |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S303 | Bataille de l'Océan |  | area, carte-ui, csm, dds, fsm, fsmp, ini, nif, pgp, smp |
| S304 | Champ de Bataille T.4 |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S305 | Zone de Guerre d'Ezur |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S306 | Île du Boucanier |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S307 | Toundra Hostile |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S308 | Arène Sacrée : Le Crucifix |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S309 | Survie sur l'Île Déserte |  | carte-ui, ini |
| S312 | ? | ? | dds |
| S321 | ? | ? | dds |
| S330 | ? | ? | dds |
| S340 | ? | ? | dds |
| S401 | Arène Phénix |  | area, carte-ui, csm, fsmp, ini, nif, pgp, smp |
| S402 | Arène des Boss |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S403 |  |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S404 | Prison du Fantôme Enflammé |  | area, carte-ui, csm, fsmp, ini, nif, pgp, smp |
| S405 | ? | ? | area, dds, fsmp, ini, nif, pgp, smp |
| S406 | Temple Antique du Chaos |  | area, carte-ui, dds, fsmp, ini, nif, smp |
| S410 | ? | ? | dds |
| S430 | ? | ? | dds |
| S501 | Forêt d'Eden |  | area, carte-ui, cfg, csm, dds, fsm, fsmp, ini, nif, pgp, smp |
| S502 | Mont Eden |  | area, carte-ui, csm, dds, fsm, fsmp, ini, pgp, smp |
| S503 | ? | ? | carte-ui, dds |
| S504 | ? | ? | carte-ui |
| S514 | ? | ? | dds |
| S599 | Villa Personnelle |  | carte-ui, csm, ini |
| S601 | Base des Vilrequins Héroïque |  | csm, ini |
| S602 | Salle Ulta Héroïque |  | csm, ini |
| S603 | Carrière Angor Héroïque |  | csm, dds, ini |
| S604 | Fort Verdure Héroïque |  | csm, ini |
| S605 | Coeur Baltaroï Héroïque |  | csm, ini |
| S606 | Crocvenin Héroïque |  | csm, ini |
| S607 | Canal Tassos Héroïque |  | csm, ini |
| S608 | Carrière de Schiste Héroïque |  | csm, ini |
| S609 | Épreuve : Écarlate |  | csm, ini |
| S610 | Épreuve : Siècle |  | csm, ini |
| S611 | Épreuve : Manoir |  | csm, ini |
| S612 | Épreuve : Éroda |  | csm, ini |
| S613 | Épreuve : Morticora |  | csm, ini |
| S614 | Épreuve : Palais des Malices |  | csm, ini |
| S615 | Épreuve : Fléau |  | csm, ini |
| S616 | Épreuve : Racine Branda |  | csm, ini |
| S617 | Épreuve : Rêve du Maire |  | csm, ini |
| S619 | Expédition Anti Dragon - Autel Volcanique |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S620 |  |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S621 |  |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S622 |  |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S623 |  |  | area, carte-ui, csm, dds, fsmp, ini, nif, pgp, smp |
| S624 | Épreuve : Frontière de Basel |  | csm, ini |
| S625 | Épreuve : Arsenal Fédéral |  | csm, ini |
| S626 | Épreuve : Sturmfrau |  | csm, ini |
| S627 | Épreuve : Abysse Glacée de Skleros |  | csm, ini |
| S628 | Épreuve: Base des Vilrequins S |  | csm, ini |
| S629 | Épreuve: Salle Ulta S |  | csm, ini |
| S630 | Épreuve : Angor S |  | csm, ini |
| S631 | Épreuve : Fort Verdure S |  | csm, ini |
| S632 | Épreuve : Cœur Baltaroï S |  | csm, ini |
| S633 | Épreuve: Crocvenin S |  | csm, ini |
| S634 | Épreuve: Canal Tassos S |  | csm, ini |
| S635 | Épreuve: Caverne de Schiste S |  | csm, ini |
| S636 | Épreuve: Écarlate S |  | csm, ini |
| S637 | Épreuve: Siècle S |  | csm, ini |
| S638 | Épreuve: Manoir S |  | csm, ini |
| S639 | Épreuve: Éroda S |  | csm, ini |
| S640 | Épreuve: Morticora S |  | csm, ini |
| S641 | Épreuve : Antre des Échos |  | csm, ini |
| S642 | Épreuve : Prison Ossuaire |  | csm, ini |
| S643 | Épreuve : Cratère des Géants |  | csm, ini |
| S644 | Épreuve : Vieux Siècle |  | csm, ini |
| S645 | Épreuve : Cratère des Géants |  | ini |
| S646 | Épreuve : Abysse de Siècle |  | ini |
| S647 | Épreuve : Temple Spirenfer |  | ini |
| S648 | Épreuve : Temple de la Rose |  | ini |
| S649 | Épreuve des Héros du Chemin d'Occultus |  | ini |
| S650 | Épreuve des Héros de la Colline Tranquille |  | ini |
| S651 | Trésor : Cœur Baltaroï |  | csm, ini |
| S652 | Trésor : Crocvenin |  | csm, ini |
| S653 | Trésor : Canal Tassos |  | csm, ini |
| S654 | Trésor : Caverne de Schiste |  | csm, ini |
| S655 | Illusion Destructrice : Carrière Angor (DJN multijoueur) |  | ini |
| S656 | Illusion Destructrice : Fort Verdure (DJN multijoueur) |  | ini |
| S657 | Illusion Destructrice : Canal Tassos (DJN multijoueur) |  | ini |
| S658 | Illusion Destructrice : Caverne de Schiste (DJN multijoueur) |  | ini |
| S659 | Illusion Destructrice : Éroda (DJN multijoueur) |  | ini |
| S660 | Illusion Destructrice : Morticora (DJN multijoueur) |  | ini |
| S661 | Illusion Destructrice : Base des Vilrequins (DJN multijoueur) |  | ini |
| S662 | Illusion Destructrice : Racine Branda (DJN multijoueur) |  | ini |
| S663 | Illusion Destructrice : Palais des Malices (DJN multijoueur) |  | ini |
| S664 | Illusion Destructrice : Salle Ulta (DJN multijoueur) |  | ini |
| S665 | Illusion Destructrice : Cœur Baltaroï (DJN multijoueur) |  | ini |
| S666 | Illusion Destructrice : Rêve du Maire (DJN multijoueur) |  | ini |
| S667 | Illusion Destructrice : Crocvenin (DJN multijoueur) |  | ini |
| S668 | Illusion Destructrice : Fléau (DJN multijoueur) |  | ini |
| S701 | Carrière Angor |  | csm, ini |
| S702 | Fort Verdure |  | csm, ini |
| S703 | Canal Tassos |  | csm, ini |
| S704 | Caverne de Schiste |  | csm, ini |
| S705 | Éroda |  | csm, ini |
| S706 | Morticora |  | csm, ini |
| S707 | Racine Branda |  | csm, ini |
| S708 | Palais des Malices |  | csm, ini |
| S709 | Pourriture |  | csm, ini |
| S710 | Abîme de Baram |  | csm, ini |
| S711 | Coeur Baltaroï |  | csm, ini |
| S712 | Rêve du Maire |  | csm, ini |
| S713 | Fléau |  | csm, ini |
| S714 | Chambre de Baïra |  | csm, ini |
| S715 | Crypticône |  | csm, ini |
| S716 | Temple de Delphes |  | csm, ini |
| S717 | Veine Tordue |  | csm, ini |
| S718 | Autel du Culte de Mara |  | csm, ini |
| S719 | Chutes des Âmes en Peine |  | csm, ini |
| S720 | Vortek |  | csm, ini |
| S721 | Arsenal Fédéral |  | csm, ini |
| S756 | ? | ? | carte-ui, ini |
| S757 | ? | ? | carte-ui, ini |
| S758 | ? | ? | carte-ui, ini |
| S759 | ? | ? | carte-ui |
| S764 | Bataille d'Hélios [Groupe] |  | ini |
| S765 | Sortir des méandres du Fort (Solo) |  | ini |
| S766 | Passage spatiotemporel |  | carte-ui, ini |
| S767 | Une cave où l'on entrepose de la bière. |  | ini |
| S768 | Canal d'Aven |  | ini |
| S769 | Arène du Démon des Enfers - Assemblée |  | ini |
| S770 | Courez ! Des vaches ! Courez ! |  | ini |
| S771 | Dimension du chaos <Groupe> |  | ini |
| S772 | Purgatoire : Arène des Chevaliers Templiers |  | ini |
| S773 | Course d'Alpagas |  | ini |
| S774 | Ville de Guilde : Couloir de l'Esprit Gardien |  | ini |
| S775 | Aventure fabuleuse : Cœur de Dragon |  | ini |
| S776 | Terrains d'Entraînement |  | ini |
| S777 | Forêt Camoufle |  | ini |
| S778 | Gardien de l'Arbre de Vie  <1 joueur> |  | ini |
| S779 | Garde du Cimetière <Event de groupe> |  | ini |
| S783 | Conte de légende : les voleurs de trésor et le lutin légendaire. |  | csm, ini |
| S784 | Les 5 Animaux : La légende de Bonka <Event 1 joueur> |  | csm, ini |
| S785 | Les 5 Animaux : La légende de Yula <Event 1 joueur> |  | csm, ini |
| S786 | Les 5 Animaux : La légende de Barlilu <Event 1 joueur> |  | csm, ini |
| S787 | Les 5 Animaux : La légende de Ron <Event 1 joueur> |  | csm, ini |
| S788 | Les 5 Animaux : La légende de Maschatta <Event 1 joueur> |  | csm, ini |
| S789 | Défi des Boss <Groupe> |  | csm, ini |
| S790 | Le Pouvoir du Cristal d'Eternal <Groupe> |  | csm, ini |
| S791 | Génération Future <Groupe> |  | csm, ini |
| S792 | Profondeurs du Canal Tassos |  | csm, ini |
| S793 | Abîme de Baram |  | csm, ini |
| S794 | Mine du Village Andrew |  | csm, ini |
| S795 | Temple de Delphes |  | csm, ini |
| S796 | Les Profondeurs de l'Abysse Glacée de Skleros |  | csm, ini |
| S797 | Ancien Autel |  | csm, ini |
| S798 | Refuge des Sang Noir |  | csm, ini |
| S799 | Salle Ulta |  | csm, ini |
| S801 | Base des Vilrequins |  | csm, ini |
| S802 | Salle Ulta |  | csm, ini |
| S803 | Crocvenin |  | csm, ini |
| S804 | Écarlate |  | csm, ini |
| S805 | Siècle |  | csm, ini |
| S806 | Manoir |  | csm, ini |
| S807 | Frontière de Basel |  | csm, ini |
| S808 | Sturmfrau |  | csm, ini |
| S809 | Abysse Glacée de Skleros |  | csm, ini |
| S810 | Cratère des Géants |  | csm, ini |
| S811 | Antre des Échos |  | csm, ini |
| S812 | Prison Ossuaire |  | csm, ini |
| S813 | Guillotine |  | csm, ini |
| S814 | Cap Hurlant |  | csm, ini |
| S815 | Abysse de Siècle |  | csm, ini |
| S816 | Temple Spirenfer |  | csm, ini |
| S817 | Temple de la Rose |  | csm, ini |
| S818 | Grotte Non Répertoriée |  | csm, ini |
| S819 | Cratère Météoritique <Solo> |  | ini |
| S820 | Grotte de la Marée des Ombres <Solo> |  | ini |
| S821 | Tour Kahlo <Solo> |  | ini |
| S822 | Iyernafalo <Solo> |  | ini |
| S823 | Terre sacrée de Kamodo <Solo> |  | ini |
| S824 | Temple aquatique <Solo> |  | ini |
| S825 | Labo Antigravité <Solo> |  | ini |
| S826 | Grey Lynn <Solo> |  | ini |
| S827 | Vallée Batelo (Solo) |  | ini |
| S828 | Allolans (Solo) |  | ini |
| S829 | Illusion Destructrice : Carrière Angor (Solo) |  | ini |
| S830 | Illusion Destructrice : Fort Verdure (Solo) |  | ini |
| S831 | Illusion Destructrice : Canal Tassos (Solo) |  | ini |
| S832 | Illusion Destructrice : Caverne de Schiste (Solo) |  | ini |
| S833 | Illusion Destructrice : Éroda (Solo) |  | ini |
| S834 | Illusion Destructrice : Morticora (Solo) |  | ini |
| S835 | Illusion Destructrice : Base des Vilrequins (Solo) |  | ini |
| S836 | Illusion Destructrice : Racine Branda (Solo) |  | ini |
| S837 | Illusion Destructrice : Palais des Malices (Solo) |  | ini |
| S838 | Illusion Destructrice : Salle Ulta (Solo) |  | ini |
| S839 | Illusion Destructrice : Cœur Baltaroï (Solo) |  | ini |
| S840 | Illusion Destructrice : Rêve du Maire (Solo) |  | ini |
| S841 | Illusion Destructrice : Crocvenin (Solo) |  | ini |
| S842 | Illusion Destructrice : Fléau (Solo) |  | ini |
| S889 | ? | ? | carte-ui |
| S901 |  |  | csm |
| S902 |  |  | csm |
| S903 |  |  | csm |
| S904 |  |  | csm |
| S905 |  |  | csm |
| S906 |  |  | csm |
| S907 |  |  | csm |
| S908 |  |  | csm, ini |
| S994 | Zone déclenchant automatiquement les quêtes |  | area, carte-ui, ini, nif, pgp, smp |
| S995 |  |  | area, carte-ui, ini, nif, pgp, smp |
| S996 |  |  | csm, ini |
| S997 | Emplacement de l'Event |  | ini |
| S998 | Emplacement d'Événement |  | ini |
| S999 | Plan de Grille |  | area, carte-ui, cfg, dds, fsm, ini, pgp, smp |

## Fichiers `map/` hors code S###

Regroupés par dossier/extension (détail unitaire : `files.tsv`).

| Dossier | Ext | Nb | Exemples |
|---|---|---|---|
| `map/` | nif | 1 | map.nif |
| `map/model/` | ini | 1 | treeobject.ini |
| `map/model/effect/` | area | 1 | effect.area |
| `map/model/effect/` | bat | 1 | convertdds.bat |
| `map/model/effect/` | dds | 1 | AlphaMap0-0.dds |
| `map/model/effect/` | fsm | 1 | effect.fsm |
| `map/model/object/` | dds | 1 | uc03301.dds |
| `map/model/object/` | kf | 2 | ud107.kf, ud705.kf |
| `map/model/object/` | kfm | 2 | ud107.kfm, ud705.kfm |
| `map/model/object/` | nif | 1179 | BLOCK.nif, UA042.nif, UA043.nif |
| `map/model/sky/` | nif | 41 | A102.nif, A201.nif, A302.nif |
| `map/sknodemap/` | smp | 1 | .smp |
| `map/texture/` | dds | 1498 | 011.dds, 05tree_01.dds, 1100.dds |
| `map/texture/` | ifl | 7 | WaterAni_normal.ifl, WaterairAni-01.ifl, WaterairAni-02.ifl |
| `map/texture/` | psd | 3 | pollution_01.psd, ud50101.psd, ud501012.psd |
