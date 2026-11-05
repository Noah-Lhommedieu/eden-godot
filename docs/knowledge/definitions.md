# Définitions gameplay (source de vérité propriétaire)

Statut : verified (définitions dictées par le propriétaire, 2026-11-03). Tout code,
panneau ou doc qui range une entité dans une de ces catégories doit s'y conformer.

## Statuts

- **Status** : tout effet *porté* par une entité, à durée limitée ou illimitée, qui
  a un effet *persistant* sur elle. Seul ce qui agit sur une durée (ou jusqu'à une
  condition : niveau 80, fin de quête…) est un status.
- **Buff** : status qui *augmente* des statistiques (durée limitée ou illimitée).
  Ex. « Grâce de la Sagesse ».
- **Nerf** : status qui *réduit* des statistiques (durée limitée ou illimitée).
- **État** : status qui donne un bonus/malus *sans lien avec les statistiques*, ou
  lié à une quête, une instance ou un évènement spécial. Ex. portal stone gratuite
  (TP partout, illimité jusqu'au niveau 80), état de monture.
- **Debuff** : *action* de retirer un buff ou un nerf à l'entité touchée (selon la
  cible : ennemi ou allié). Ce n'est pas un status affiché.

Conséquences :
- Un sort instantané sans effet sur durée (ex. « Guérison » : un soin après timed
  cast) **ne crée aucun status** et n'apparaît dans aucune liste de statuts.
- Le panneau de statuts du joueur liste buffs, nerfs et états ; rien d'autre.
- Le panneau Profil reflète l'état courant du joueur en tout temps (stats avec
  certificats, buffs, équipement…), sans rechargement manuel.

## Règles de lancer (cast)

- Un sort ciblé ne se lance pas sans cible valide.
- Exceptions : sorts dont la description dit qu'ils se lancent autour du lanceur
  (zone centrée caster), buffs en zone autour du joueur, buffs sur soi, ou sur une
  cible distante. La description du sort dit toujours la ou les possibilités.

## Monstres et boss

- **Break Point (BP)** : barre secondaire d'un mob/boss. Vidée entièrement → état
  « BP cassé » : animation spéciale du boss, changement éventuel de skin/modèle, et
  drops supplémentaires « Break Point ».
- **Skins d'état** : beaucoup de mobs ont des modèles différents selon BP cassé,
  seuil de PV ou gros dégâts d'un coup (% PV perdu). Le modèle par défaut est le
  modèle « plein de vie ».
- **Drops Normal** : lâchés à la mort du mob.
- **Drops Break Point** : lâchés *en plus*, uniquement si la barre de BP a été
  entièrement cassée.
- **Drops Class Specific** : drops réservés à une classe donnée.
  Traductions FR : voir section ci-dessous.
- **Normal skills** : sorts que le boss lance aléatoirement pendant le combat.
- **Countdown skills** : sorts lancés automatiquement dès que possible (pas
  d'étourdissement…) quand le mob atteint un pourcentage de PV donné (ex. 50 %).
- **Worldboss** : taille agrandie d'un multiple défini (voir code/données).
- Les éléments de faiblesse s'affichent par l'élément seul (« Feu »), pas
  « Faiblesse : Feu ».

## Traductions FR (client)

- Break Point → **Point de rupture**
- Class Specific drop → **Butin de classe**
- Normal skills → **Compétences normales**
- Countdown skills → **Compétences à seuil**
