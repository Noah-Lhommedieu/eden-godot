# Patchnote VGN 23 5/5 : rééquilibrage

> Source : `raw/guides-fr/2017/09/26/patchnote-vgn-23-55-reequilibrage-classes.html` — publié le 2017/09/26 (site WordPress edenfr.wordpress.com)

_Il s’agit de la traduction en français des[posts de preview](https://forum.vendettagn.com/index.php?/topic/1767-preview-pvp-balancing-update/) du GM Jordan concernant le rééquilibrage des classes sur le serveur Vendetta. Les traductions sont majoritairement produites par **Aelenwa** , qui m’a autorisé à les partager ici. Merci beaucoup! **Attention** : ce post est _massif_!_
**SOMMAIRE**
Post du 12/05/2017 : Introduction, objectifs du patch | Post du 12/05/2017 : Arch-Elementalist
---|---
Post du 19/05/2017 : Asura | Post du 27/05/2017 : Généralités
Post du 28/05/2017 : Trainer | Post du 30/05/2017 : Annihilator
Post du 11/06/2017 : Généralités, Demon Tamer | Post du 20/06/2017 : Certificat, Battle Poet
Post du 24/06/2017 : Life Worshipper & Elegant Dance | Post du 10/07/2017 : Blade Master
Post du 18/07/2017 : Paladin | Post du 22/07/2017 : Druid
Post du 01/08/2017 : Guardians Valley Battlefield, Darkness Blade
**POST DU 12 MAI 2017**
---
**Introduction**
* * *
Comme beaucoup d’entre vous le savent, je travaille en ce moment sur une mise à jour massive du pvp sur Eden Eternal. Ce sera l’apport principal du prochain patch. Ce patch comportera aussi d’autres apports non-pvp pour que chacun y trouve son compte. Pour que ce soit bien clair pour tout le monde, vu que quelques joueurs n’ont pas l’air d’avoir saisi : **il ne s’agit pas de nerf toutes les bonnes classes pour avantager des classes plus faibles. Ce patch a pour objectif de revoir toutes les classes disponibles du jeu pour les amener toutes à un niveau globalement équivalent.**
_**Je sais bien que certaines classes seront toujours plus performantes que d’autres. J’ajouterais toutefois des modifications au jeu dans de futures mises à jour après ce patch, en rectifiant les stats des classes qui fonctionnent trop ou pas assez bien.**_.
Je souhaite aussi préciser que les classes ne seront pas les seules à subir des changements. Les modes pvp vont également être modifiés pour accompagner la mise à jour des classes. Par exemple **les guerres de territoire [TW], les éliminations de guilde [GvG], l’île de survie [Elysian Island], les arènes et l’île des cerisiers [Sakura Island], entre autres.**
_**Je prévois de créer de nouvelles maps de TW mais ce n’est pour l’instant pas ma priorité. Ne les attendez donc pas d’entrée de jeu mais plutôt en fin de patch. De plus il y aura d’abord une phase BETA pour que les gens puissent tester et donner leur avis avant de les intégrer définitivement dans la liste des TW.**_
Et pour répondre à une petite question : les classes normales ne seront pas modifiées dans ce patch. Elles le seront dans une mise à jour prochaine pour les ramener au niveau standard des classes éveil.
**Ce sujet**
* * *
Bien, maintenant qu’on a passé en revue les principales informations dans l’introduction, entrons dans le vif du sujet. Il semble que quelques joueurs aient décidé de me dire à moi personnellement, qui plus est en public, ou bien ont ressenti l’extrême nécessité de se plaindre à d’autres que je ne prenais pas en compte les retours, suggestions et idées de la communauté. Permettez-moi de vous expliquer mon raisonnement afin que vous compreniez la situation et les choix que j’opère.
En premier lieu, je tiens à dire que je reçois l’aide de quelques joueurs qui ont d’ores et déjà prouvé que je pouvais leur faire confiance pour ne pas communiquer à tort et à travers sur les changements auxquels ils contribuent. A ce stade tout est très très expérimental, d’où notre besoin de revenir aux bases. Ce que je dis en canal pair [peer] ou ce que j’autorise les autres à mentionner, ce sont seulement des informations confirmées.
D’autre part beaucoup de suggestions que je reçois sont très basiques et sans grande réflexion. Ça ne veut pas dire que je n’en tiens pas compte. Ça veut juste dire que j’essaie de les exploiter d’une façon qui satisferait le plus grand nombre.
Quelques exemples :
¤ Augmenter la barre de vie en pvp pour éviter le oneshot. Ceci ne fera que rendre les classes faibles plus faibles et les classes fortes plus fortes. Regardez le Blade Master. C’était une bonne classe avant la première augmentation des PV mais aujourd’hui, plus personne ne le joue puisqu’il ne peut rien faire face à un ennemi accompagné d’un healer.
¤ Augmenter la barre de vie des cristaux de TW et GVG pour éviter que les Gravity Manipulators [Astro] et les Assassins ne les two-shot. Cela va de pair avec le point précédent. Ça ne règle rien. Je veux dire, regardez seulement certaines vidéos de FNO [le jeu dans sa version taiwanaise] et vous verrez qu’ils campent à deux cristaux contre deux pendant toute la durée de la TW.
¤ Améliorer une classe parce qu’elle est faible. Vous ne pouvez pas améliorer une classe pour en faire un truc ultime. Certaines classes n’ont tout simplement pas d’intérêt dans un groupe ou ne s’y adaptent que très peu à cause du renvoi ou des nombreux contrôles de zone. (lisez un peu plus bas, ce sera expliqué plus en détail)
¤ Donner juste un stun au Berserker et ça deviendra une bonne classe. Ça, c’est la suggestion qui revient le plus souvent. Mais ce n’est pas aussi simple. N’oubliez pas que l’organisation en pvp a radicalement changé avec le cap du level 80. Ce n’est donc pas un petit stun qui empêchera les gens de vous kite. [kite : taper-reculer-taper-reculer-etc sans jamais laisser l’adversaire te toucher] J’ai fait du Berserker une bonne classe sans lui coller un stun comme l’aurait fait une vieille rustine à la mode Aeria.
En fait, je pourrais vous donner encore 20 ou 30 exemples de ce type où en théorie l’idée est bonne mais où en pratique, elle est inexploitable.
Je donnerais des précisions sur les progrès réalisés toutes les semaines en réponse à ce sujet. Il y aura en premier lieu une check-list (dans la première réponse) de mes travaux en cours (dont je m’occupe quotidiennement). J’ajouterais également un Google Form (toujours dans la première réponse) afin que vous puissiez me soumettre vos suggestions pour m’aider à faire mes choix sur la meilleures façon de modifier une classe.
**Mes objectifs**
* * *
Mon objectif principal dans le prochain patch est l’équilibrage des classes. Je veux dire, ne pas rendre les classes faibles complètement fumées ni les classes fortes complètement inutiles, mais bel et bien un rééquilibrage global. Ça veut dire que vous pourrez avoir un Battle Poet [Poète de Bataille] et un Executioner [Chevalier éveillé], ou encore un Arch-Elementalist ou un Gravity Manipulator [Astro] sans avoir besoin de rester coller à un Adjudicator [Disciplinaire] comme heal ; vous pourrez vous satisfaire tout autant d’un Life Worshiper [Poète de l’Essence] ou d’un Totem Master [shaman éveillé].
J’essaie de donner à chaque classe des caractéristiques qui la rendent absolument unique (je ne suis pas près d’arriver au bout mais au moins j’essaie XD). Un exemple que vous m’avez déjà vu mentionner : l’Arch-Elementalist, dont le dps est désormais bien plus intéressant et qui a perdu beaucoup de ses fonctions de support pour enfin devenir l’Elemental Mage dont on nous avait toujours fait la pub (le spam boubou ne sera plus son unique intérêt).
Si je peux faire une checklist des apports les plus sûrs de ce patch :
¤ Proposer à nouveau aux joueurs la possibilité de jouer full dégâts vs full tank.
¤ Retravailler sur certains enchantements de trophées, notamment ceux relatifs aux dégâts pour en faire un peu plus une option pvp.
¤ Retravailler tous les glyphes éveillés pour les adapter aux changements que j’ai prévu sur les classes correspondantes.
¤ Retravailler sur les pratiques pvp actuelles (comme le renvoi, les trophées de transformation et ceux basés sur les résistances, etc).
¤ Des changements concernant les résistances à grande échelle.
¤ (incertain) Essayer de ramener l’esquive à un niveau raisonnable. C’est incertain dans le sens où j’ai besoin de faire plus d’expérimentations pour voir comment ça pourrait fonctionner. Ce n’est pas une modification assurée mais je verrais ce que je peux faire).
**¤ Réduire les chances de oneshot au minimum, de façon à ne pas se faire oneshot par n’importe quel Gravity Manipulator [Astro] ou Holy Blade [luminary éveillé] qui passe, mais plutôt dans le sens où un bon Gravity Manipulator pourrait bien réussir à vous oneshot.**
_**Note : Ces classes vont/ont déjà été modifiées sur certains points afin qu’elles ne soient plus des classes bi-touches, et de façon à élargir l’emploi de toute la palette de skills pour infliger un maximum de dégats que possible dans un minimum de temps.**_
¤ Retravailler les armes holy [celles des donjons Illusion 90] pour compenser les futurs changements. Cette fois elles fusionneront avec les skills de classe et les combos, vu que je ne suis pas X-Legend qui va toujours au plus simple.
**En bref**
* * *
Pour résumer tout ce que je viens de dire, j’espère ramener le pvp au niveau que les gens ont connu et apprécié. Quand le jeu était basé sur le travail d’équipe et non sur l’effet de masse. Quand les dps mettaient en œuvre leurs plus gros dégâts pour descendre en flèche l’ennemi et quand les tanks pouvaient encaisser des dégâts pendant des plombes. Au lieu d’avoir un build full tank et quand même se faire oneshot par la moitié des dps qui passent, ou de subir sans rien pouvoir faire le festival des contrôles dans taggle toutes les 30 secondes.
Cela dit je sais qu’il y aura quelques ratés à la sortie du patch, avec des classes toujours trop ou pas assez performantes, mais comme je l’ai dit plus haut : ce n’est que le début pour la communauté d’Eden Eternal. J’ai bien l’intention de maintenir ce jeu aussi longtemps que je le peux et de faire en sorte que le pvp soit un plaisir pour le plus de joueurs possible. (même si je sais qu’il y en aura pour pleurnicher sur telle classe est trop forte ou trop faible) Ça ne signifie pas qu’il y aura chaque semaine des mises à jour ou modification sur la moitié des classes du jeu. Mais que dans les prochains patchs, je ferais en sorte d’apporter un nouvel équilibrage pour garder le jeu attrayant.
Faites-moi confiance s’il vous plaît !
**POST DU 12 MAI 2017 : ARCH-ELEMENTALIST**
---
![](https://c1.staticflickr.com/5/4191/33829124354_d8f1660327_o.png)
**Objectifs de la classe**
* * *
En créant cette classe, j’ai voulu en faire un véritable élémentaliste et non plus seulement un esclave du spam boubou. Je me suis inspiré de plusieurs autres jeux et en reprenant des mécaniques déjà éprouvées, il s’agissait pour moi de développer le potentiel de cette classe au maximum. Elle permettra notamment d’accéder à tous les principaux éléments de façon diversifiée (bien que la principale reste le switch élémentaire [stance]). Je voulais aboutir à une classe qui ne se contente pas d’un gameplay unique, mais qui soit plus réactive en fonction de la situation qui se présente.
Je n’irais pas trop dans les détails de la classe car je souhaite garder plein de surprises pour les joueurs qui la découvriront lors de la sortie du patch.
**Arbre de compétences**
* * *
![](https://c1.staticflickr.com/5/4169/34286623000_e94732986d_o.png)
L’arbre de compétence de cette classe est un peu différent de celui des autres classes actuellement en jeu. Je l’ai créé de façon à limiter le choix des joueurs à deux chemins sur les quatre possibles. Par exemple, vous ne pourrez mettre de points dans les chemins élémentaires qu’après avoir disposé 70 points dans les stats de base (attaque magique, esquive, vitesse d’invocation, etc). Chaque chemin élémentaire nécessite ensuite 14 points puisque l’idée est donc de n’avoir que deux chemins complets.
**Vidéos**
* * *
J’ai outrageusement profité de crash testeurs pour enregistrer quelques petites vidéos de démonstration. Seulement quelques combinaisons de skills sont présentées et les dégâts ne sont pas du tout réalistes. J’ai équipé le premier bâton venu.
_Thunderlord Combo_ => [sur YouTube](https://youtu.be/OAETyS1UKiM)
Lightning Stance > Lightning Storm > Lightning Tornado
(augmente la portée de Lightning Tornado)
_Lightning Rod Combo_ => [sur YouTube](https://youtu.be/HvIfjlYUYZo)
Wing Stance > Wind Storm / Wind Tornado > Lightning Stance > Lightning Bombardment
(augmente la portée de Lightning Bombardment)
_Hydra’s Destructive Rage Combo_ => [sur YouTube](https://youtu.be/6IYtHdU_yQo)
Wind Stance > Wind Bombardment = Hydra’s Destructive Rage – Nature
Lightning Stance > Lightning Tornado = Hydra’s Destructive Rage – Thunder
Ice Stance > Arctic Storm = Hydra’s Destructive Rage – Ice
Chaque stack de Hydra’s Destructive Rage réinitialisera la durée de tous les buffs des autres Hydra’s Destructive Rage, et réduira le cooldown et augmentera la portée de Fire Hydra.
_Run à Dimension of Soul_ => [sur YouTube](https://youtu.be/Q9q3fkrUXy0)
Merci à mon crash testeur Wyzer pour l’enregistrement de cette vidéo. Il montre quelques nouveautés de la classe dont je n’ai pas encore parlé dans ce post, mais ça c’est à vous de trouver !
**POST DU 19 MAI 2017 : ASURA**
---
![](https://www.vendettagn.com/assets/banners/vgn/patch21_class_03.PNG)
![](https://puu.sh/w3wYY.jpg) ![](https://i0.wp.com/puu.sh/w3wX9.jpg)
_Asura’s Rage_
Cout : 4% de MP par seconde.
Active « Asura’s Rage ».
P-ATK, ATK SPD et ACC +10
Taux de double coup +20%.
Immunisé a l’immobilisation tant que ce buff est actif.
Chaque attaque donne une charge de « Asura’s Power ». « Asura’s Power » donne 5% de P-ATK et ATK SPD pour 7 secondes. Se cumule 10 fois.
En atteignant 5 charges de « Infiltration Punch » vous bénéficiez d’un bonus de 30% de P-ATK.
En utilisant « Power Burst » vous recevez 15% de dégâts supplémentaires.
(L’effet est annulé si les MP sont inssufisants ou si le skill est ré-utilisé. L’effet est annulé si « Asura’s Domain » est utilisé.)
---
_Asura’s Domain_
Cout : 4% de MP par seconde.
Active « Asura’s Domain ».
DEF, EVA, et toutes les résistances +10%
20% de vol de vie sur les attaques normales.
Immunisés aux étourdissements tant que ce buff est actif.
Chaque attaque donne une charge de « Asura’s Energy ». « Asura’s Energy » donne 5% de DEF et EVA pendant 7 secondes. Se cumule 10 fois.
En atteignant 5 charges de « Hammer Fist » vous bénéficiez de 30% de P-ATK supplémentaire.
En utilisant « Power Trance » vous recevez 15% de dégâts en moins.
(L’effet est annulé si les MP sont inssufisants ou si le skill est ré-utilisé. L’effet est annulé si « Asura’s Rage » est utilisé.)
---
**POST DU 27 MAI 2017**
---
Hey tout le monde, une petite mise a jour sur le patch d’équilibrage et sa progression. Je vais malheureusement être absent pendant un mois a compter du mois prochain et ne pourrais pas être aussi présent (mes horaires seront inversés). Cela ne veut cependant pas dire que je ne travaillerai pas sur le patch. C’est juste que j’aurai d’autres obligations liées a mon travail et donc moins de temps pour m’y consacrer. Je continuerai cependant à mettre a jour ce post et de me connecter en jeu pour communiquer avec vous tous quand je le pourrais mais comme je l’ai dit, mon rythme de travail sera affecté.
Une autre chose dont je voudrais parler est le petit « leak » : un ou deux joueurs ont entendu des informations qu’ils n’auraient pas du avoir (informations que je détaillerais pas). Un des joueurs en question ne semble pas avoir compris les changements apportés à la classe qu’il joue donc je voudrais en parler à tout le monde afin que les gens comprennent; si de nouvelles informations fuitent ou que l’information en question fuite plus qu’elle ne l’a déjà fait.
Les classes seront modifiées de multiples façons et une partie de leur gameplay actuel en sera altérée. Mais c’est pour dynamiser d’autres éléments de ce gameplay, et améliorer les classes de façon bien plus globale. J’utiliserai l’information qui a fuité comme exemple afin que tout le monde soit à la page et ne s’en tienne pas au bouche-à-oreille (Par exemple, « Jordan a ruiné cette classe parce il a supprimé cette forme et c’est la seule chose pour laquelle cette classe est douée »).
La fuite concernait le fait que la classe « Equilibrian » (ndt : Prêtre de l’Ombre) pourrait, et je dis bien pourrait, perdre la diminution de résistance en AoE. C’est un changement qu’il me semblait nécessaire puisqu’il affectait les points décrits dans le post précédent :
_¤ Proposer à nouveau aux joueurs la possibilité de jouer full dégâts vs full tank._
_¤ Retravailler sur certains enchantements de trophées, notamment ceux relatifs aux dégâts pour en faire un peu plus une option pvp._
_¤ Retravailler toutes les glyphes éveillées pour les adapter aux changements que j’ai prévu sur les classes correspondantes._
¤ Retravailler sur les pratiques pvp actuelles (renvoi, trophées de transformation _et de résistances_ , etc).
_¤ Des changements concernant les résistances à grande échelle._
_¤ Retravailler les armes holy [celles des donjons Illusion] pour compenser les futurs changements._ Cette fois elles fusionneront avec les skills de classe et les combos, vu que je ne suis pas X-Legend qui va toujours au plus simple.
---
(Je voudrais également ajouter une précision pour la partie sur les armes « Holy ». Je veux que toutes les classes soient jouables peu importe l’arme équipée. Evidemment, une arme Holy ou une arme 95 donnera un boost à la classe. Simplement ce ne sera pas un boost aussi significatif que pour l’Equilibrian, que je vous ne pouvez pas jouer sans le bâton Holy. Cela vaut également pour la masse du Angel of Justice (ndt : Sage éveillé).)
Alors avant que quelqu’un ne demande : oui, cette compétence en particulier a été nerf mais je vous assure que les autres compétences sont maintenant tout aussi rentables et utiles. Je veux dire, sérieux, un patch d’équilibrage qui nerf une compétence rendant inutile de monter ses résistances Holy et Dark, je pensais que ce serait une modification évidente.
Donc pour résumer ma diatribe / explication je voudrais confirmer la chose suivante, à l’heure oà j’écris ces lignes (ndt : Posté samedi 27 Mai 23:18) je ne travaille que sur les classes proprement dites (c’est-à-dire que les modifications des armes Holy ne sont pas encore en chantier). Pour prévenir toute fuite d’information intempestive, sachez que je planifie d’ajouter tout un tas d’interactions avec les glyphes et les armes Holy.
En ce qui concerne les previews de la semaine, c’est repoussé du fait d’obligations IRL et des problèmes liés à cette fuite d’information, dont j’ai du m’occuper au lieu de bosser sur le patch. Donc amusez-vous sur Eden Eternal et soumettez vos idées / problèmes via le formulaire dédié que je regarde quotidiennement. Ceux qui ont posté sur ledit formulaire le savent déjà car je suis venu en parler avec eux en jeu. Le prochain aperçu sera celui du Trainer (ndt : Maître des Bêtes) et pour compenser le retard je vous donnerai des infos sur l’Asura également.
Merci d’avoir lu, Jordan.
**POST DU 28 MAI 2017 : TRAINER**
---
**Objectifs de la classe**
* * *
Pour la refonte de cette classe, je voulais garder l’idée de la classe et utiliser ses familier à son avantage. Cependant, à la base cette classe dispose de compétences qui n’ont globalement aucune intéraction entre elles (contrairement au Dragon Emperor (ndt : Chevalier Dragon éveillé) dont les familiers modifient l’intégralité de la classe).
Donc en me basant sur cette constatation, j’ai décidé de lier familiers et compétences pour la refonte de la classe. Maintenant chaque compétence de dégâts disposera d’effets dépendant du familier invoqué. En plus de reprendre l’idée de la fusion familier / compétences du Dragon Emperor, j’ai décidé d’aller encore plus loin : apporter la compétence d’invocation de monture ses propres particularités.
**Compétences**
* * *
Disclaimer : J’ai retiré la plupart des valeurs des compétences qui doivent encore être affinées, mais vous comprendrez le concept de base de la classe en lisant les compétences. La P-ATK de ces dernières est celle du niveau 100.
– **Compétences de dégâts**
![](https://i0.wp.com/puu.sh/w3wqp.png) ![](https://puu.sh/w3wuO.png) ![](https://i0.wp.com/puu.sh/w3wxj.png)
_Entangling Volley_
Cout : 326MP
Temps de rechargement : 4 secondes
Augmente la P-ATK de 11826 et inflige trois fois les dégâts à la cible (ndt : triplette avec réduction des dégâts)
Quand Crolla est actif : La résistance percée de la cible est diminuée de X pendant 10 secondes. Se cumule 2 fois.
Quand Sukoda est actif : La vitesse de déplacement, vitesse d’attaque, et vitesse d’incantation est diminuée de X% pendant 10 secondes. Se cumule 2 fois.
Si vous êtes dans l’état « Leader of the Pack », cette compétence se transforme en « Quintuple Volly ».
– Quintuple Volley : Augmente la P-ATK de 11826 et inflige cinq fois les dégâts a la cible (ndt : quintuplette avec réduction des dégâts).
Arme requise : Arc.
---
_Swift Shot_
Cout : 296MP
Temps de rechargement : 4 secondes
Augemente la P-ATK de 12589 et inflige des dégâts a la cible.
Quand Crolla est actif : l’utilisateur gagne X% de vitesse d’attaque pendant 10 secondes.
Quand Sukoda est actif : La vitesse de déplacement de la cible est diminuée de X% pendant 10 secondes.
Si vous êtes dans l’état « Leader of the Pack », cette compétence se transforme en « Sniper’s Shot »
– Sniper’s Shot : Augmente la P-ATK de 12589 et inflige des dégâts à la cible. Chaque coup réussi augmente de X mètres la portée de « Sniper’s Shot » et de « Swift Shot » pendant 7 secondes. Se cumule 5 fois.
Arme requise : Arc
---
_Disengaging Shot_
Cout : 237 MP
Temps de rechargement : 4 secondes
Augmente la P-ATK de 11063 et inflige des dégâts a la cible. L’utilisateur bénéficie de X EVA supplémentaire pendant 10 secondes.
Quand Crolla est actif : L’utilisateur fait un bond de 10 mètres en arrière.
Quand Sukoda est actif : La cible sera repoussée de 10 mètres.
Si l’état « Leader of the Pack » est actif, cette compétence se transforme en « Engaging Charge »
– Engaging Charge: Augmente la P-ATK de 11063 et charge la cible lui infligeant des dégâts. La cible est étourdie pendant 3 secondes.
---
– **Compétence de monture**
Juste pour préciser, Crolla est la familier favorisant les dégâts et Sukora est celui qui favorise la défense donc prenez en compte que le buff donnera plus de dégâts infligés que de dégâts reçus avec Crolla tandis que Sukoda sera l’inverse.
![](https://i0.wp.com/puu.sh/w3wB9.png)
_Survival of the Fittest_
Cout : 26MP
Temps de rechargement : 40 secondes
Temps d’incantation : 1 seconde.
Devenez le « Leader of the Pack » (ndt : Chef de la Meute), chevauchez votre tigre apprivoisée dans la bataille.
Leader of the pack altère les compétences suivantes :
– Entangling Volley devient Quintuple Volley.
– Swift Shot devient Sniper’s Shot
– Disengaging Shot devient Engaging Charge
Quand Crolla est actif : l’utilisateur obtient « Leader of the Pack – Lionheart »
– Dégâts subis x%
– Dégâts infligés x%
– Vitesse de déplacement x%
Quand Sukoda est actif : l’utilisateur obtient « Leader of the Pack – Dinosoul »
– Dégâts subis x%
– Dégâts infligés x%
– Vitesse de déplacement x%
Arme requise : Arc
---
**Note : Il ne s’agit que de la classe Trainer de base, il aura plus de choses ajoutées ou modifiées plus tard dans son dévelopement. Ces modifications ne prennent également pas en compte les glyphes ou armes qui pourraient influencer la classe.**
**POST DU 30 MAI 2017 : ANNIHILATOR**
---
![](https://i0.wp.com/puu.sh/w5Nbo.png)
_Annihilateur VS 100 Dimension Shaman_ => [sur YouTube](https://youtu.be/P6Rw3hjdKuk)
Mon build n’est pas parfait et mes glypes ne sont pas optimisées. J’ai simplement capé les dégâts crit à 3.00 et cumulé le maximum de P-ATK que je pouvais sans être trop fragile. J’ai oublié quelques trucs mais c’est une illustration honnête de la puissance que cette classe peut déployer.
**POST DU 11 JUIN 2017 : DEMON TAMER**
---
Hey tout le monde, oui je suis encore en vie / dans le coin et je me suis dit que j’allais poster une petite mise à jour sur le prochain patch et sur ce qu’il se passera avec. Ca a été une semaine assez mouvementée pour moi et j’en suis enfin sorti. Mes disponibilités ont été et seront encore plutôt limitées pour les 4 prochaines semaines mais cela ne veut pas dire que je ne travaillerai pas sur ce patch, au contraire.
**Progression**
* * *
L’ajustement de mon planning étant une toute nouvelle expérience pour moi, la charge de travail a été très légère cette semaine. J’ai néanmoins terminé pas mal de choses diverses et variées, telles que les modification de certificats, et j’ai également terminé le Demon Tamer (vous pouvez voir un aperçu de ce que sera la classe a la fin du post). J’ai prévu de m’occuper des classes du Samurai eveillé une fois ce post publié. J’ai commencé a noter quelques idées pour les nouvelles glyphes éveillées et je continuerai à le faire au cours des prochaines semaines. Je veux m’assurer qu’elles soient réellement innovantes et importantes lorsque vous choisissez quelle glyphe équiper pour quelle classe. Un bon exemple serait les pièces d’artillerie Holy du Mecha Ares, l’un offrant une bonne défense et l’autre plus de DPS.
**Rumeurs and Explications**
* * *
Il y a eu quelques trucs qui ont attiré mon attention par rapport à ce que les gens m’ont demandé. Choses aussi interessantes qu’incorrectes (donc je me suis dit que j’allais évoquer le problème ici très vite).
**Changement des arènes**
Comme je l’ai mentionné dans le premier post, c’est l’une des nombreuses modifications que vous pourrez constater lors du prochain patch. Ces changements n’ont pas encore été décidés. J’attends que les fondamentaux du PvP aient été assurés suite à la refonte des classes avant de prendre la moindre décision à ce sujet. Cependant, je peux vous confirmer qu’il est très probable que les enchantements d’arènes soient supprimés ou remplacés par quelque chose de plus équilibré, en fonction ce qui arrivera aux sets d’arène. C’est parce qu’ils créent un grand déséquilibre à force d’introduire du cheese (ndt: en gros, il s’agit de strats ultra simples mais beaucoup trop puissantes) et finissent par ruiner l’expérience PvP. Je suis d’accord avec le fait que les gens qui se stuff spécialement pour les arènes devraient être récompensés, mais leur donner autant de puissance n’aide pas à attirer de nouveaux joueurs dans les arènes. Personne ne veut jouer à un jeu ou il sait pertinemment qu’il a aucune chance de gagner pendant plusieurs semaines, le temps de rattraper les autres.
Il y aura plus de récompenses casual et fun pour les gens qui jouent en arène mais qui ne seront PAS indispensables pour d’autres aspects du jeu, comme les équipements de plus haut niveau (quelqu’un avait suggérer d’ajouter aux quetes des sets eveillés 95 des Honor Stars (ndt : Etoiles d’honneur) ainsi que des objets de quêtes dans l’arène 10v10). Ce que vous pouvez attendre de ces changement c’est que les gens qui aiment faire des arènes seront encouragés à jouer pour des récompenses qui seront limitées à ce systèmes et qui ne restreindront pas d’autres contenus du jeu comme la Territory War (ndt : Guerre de Territoire) et les Trials (ndt : HQ). Je verrais également pour ajouter d’avantage d’heures d’ouverture pour l’arène, voire l’ouvrir de façon permanente.
**Les autres modes de PvP**
Un post créé dans les suggestions mentionne que les gens sont impatients d’avoir des modifications apportés dans tous les modes PvP comme Elysian (ndt : T4 je crois ?) et MBA (ndt : Monster Battle Arena/Arene des boss). Ces modes subiront quelques changements, de même que l’île Sakura sera actualisée. Je ne veux pas trop entrer dans les détails pour le moment car tout ceci n’est encore qu’au stade brainstorming. J’y reviendrais quand les classes seront terminées.
**Pourquoi modifier les classes ?**
Ok, quelqu’un disait qu’il ne comprenait pas pourquoi nous faisions une mise à jour à cette échelle plutôt que de juste nerf les trucs craqués et buffer les trucs nuls. Ce n’est juste pas une bonne façon de voir les choses car la configuration de certaines classes ne leur permet juste pas de faire le poids face à un Gravity Manipulator (ndt: Astromagicien) ou un Darkness Blade. Se contenter d’ajouter des dégâts à une classe ou en retirer ne résout pas le problème.
Prennons l’Arch-Elementalist par exemple, lorsqu’elle est jouée en DPS on ne se repose que sur un seul skill pour assurer ses dégâts (Thunder Bombardment (ndt : Bombardement de Foudre) avec un petit panel d’invulnérabilités (la moitié des skills est ignorée, je partie que la moitié des joueurs ne serait pas capable de me dire ce que fait l’Hydre normale sans aller regarder). Je voudrais également rappeler que nous parlons de modifier tout cela sur Vendetta depuis déjà l’année dernière. (littéralement une année, des mois avant l’ajout de Dimension of Souls). On a décidé de le faire après que toutes les classes soient sorties afin de continuer à observer l’équilibrage à chaque patch.
**Annihilator**
Pour mettre fin à la confusion autour de cette classe et à propos de sa puissance dans la vidéo. Oui, c’est définitivement une classe plus puissante qu’avant et oui, cette classe est davantage concentrée sur les dégâts que dans sa version précédente. En PvP, cette classe sera bel et bien capable de vous tuer si vous n’avez qu’une résistance percée moyenne et êtes en Cloth Armor. Mais elle n’est pas absolue. Vous pouvez switch sur un bouclier pour survivre ou si vous êtes avec votre groupe, les soins et les buff reçus vous protégerons très bien aussi. La classe a été repensée massivement et les testeurs ont vraiment apprécié ce qu’elle apporte désormais. Elle possède toujours des éléments de support mais est désormais plus concentrée sur l’offensif que le défensif.
**Aperçus [du Demon Tamer]**
* * *
Voila ce que tout le monde attendait : des aperçus de classes.
![](https://i0.wp.com/puu.sh/wgQqv.png)
_Infernal Contract_
Cout : 335 MP
Temps d’incantation : 1.5 secondes
Infoque le démons des flammes « Ragnas ». Le niveau de Ragnas est identique à celui de cette compétence.
Ragnas diminue la résistance au feu de la cible de X ainsi que son Cast SPD et ATK SPD de Y% pendant 5 secondes. Cet effet ne peut être purifié.
L’invocateur bénéficie de Z resistance au feu tant que Ragnas est invoqué. La compétence « Flaming Artillery » sera amélioré en « Infernal Artillery ».
Note : « Ragnas » désinvoque automatiquement « Agelas »
---
![](https://i0.wp.com/puu.sh/wgQnT.png)
Sacrifie « Ragnas » ou « Agelas » pour activer « Flame Embrace » ou « Darkness Embrace » pendant 25 secondes. _Etat « Flame Embrace »_ :
– Resistance Feu +X
– Resistance Physique : +X
– HP Max & INT +X%
– Inflige des dégâts de feu egaux à x% de votre M-ATK chaque seconde a tous les ennemis dans les 15 mètres.
– Soigne X% de vos points de vie chaque seconde.
– Vous bénéficiez des compétences du démon de feu ancien _Etat « Darkness Embrace »_ :
– Resistance Ombre +X
– Resistance Physique : +X
– HP Max & INT +X%
– Inflige des dégâts d’ombre égaux à x% de votre M-ATK chaque seconde a tous les ennemis dans les 15 mètres.
– Soigne X% de vos points de vie chaque seconde.
– Vous bénéficiez des compétences du démon de la nuit ancien
Note : Ce skill génère énormement de malice.
---
![](https://i0.wp.com/puu.sh/wgQso.png)
_Oblivion Pact_
Cout : 355 MP
Temps d’incantation : 1.5 secondes.
Invoque le démon de la nuit « Agelas ». Le niveau d’Agelas est indique a celui de cette compétence.
Agelas diminue la résistance ombre des cibles de X et leur vitesse de déplacement de X% pendant 5 secondes. Cet effet ne peut être purifié.
L’invocateur bénéficie de X résistance ombre tant que Agelas est invoqué.
La compétence Pitchblack Impact est améliorée en « Oblivion Impact ».
Note : « Agelas » désinvoque automatiquement « Ragnas »
---
**POST DU 20 JUIN 2017 : BATTLE POET & EXECUTIONER**
---
**Aperçu de certificat**
* * *
![](https://i0.wp.com/puu.sh/wpqiS.jpg)
Dynamic Force II : Augmente la P-ATK de 10%
_Bienvenu dans le rêve des DPS physiques!_
**Aperçu du Battle Poet**
* * *
![](https://i0.wp.com/puu.sh/wpql4.jpg) ![](https://i0.wp.com/puu.sh/wpqn2.jpg)
_Flame Vibrato_
Cout : 503MP
Temps de recharge : 2 secondes
Instantané
Augement la M-ATK de 33383, inflige des dégâts de feu aux cibles et baisse leur défense de 10% pour 5 secondes. Augemente également la M-ATK de l’utilisateur de 10% pendant 10 secondes. Se cumule 5 fois.
Arme requise : Guitare
Note : Il y a des effets supplémentaires selon l’utilisation, cependant il ne s’agit ici que d’un aperçu du skill de base.
---
_Requiem_
Cout : 16MP
Temps de recharge : 180 secondes
Instantané
Ressuscite la cible. Le personnage sera ressuscité avec 100% de ses PVs et MP, la pénalité d’expérience est réduite de 50%.
Arme requise : Guitare
Je sais le texte buggue un peu avec le « Instant Cast Seconds », je vais corriger ça d’ici peu 🙂
---
_Note : C’est l’une des dernières compétences de support restantes que cette classe possède, c’est pourquoi je vous la montre plutôt que les autres compétences cool._
**Aperçu de l’Executioner**
* * *
![](https://i0.wp.com/puu.sh/wpqqh.jpg)
_Bloodlust Thrust_
Cout : 186MP
Temps de recharge : 5 secondes
Instantané.
Augmente la P(ATK de 11635 et inflige les dégâts trois fois à la cible, l’utilisateur obtient l’état « Bloodlust » (ndt : Sanglant/Soif de sang). L’etat Bloodlust augmente la P-ATK, la vitesse de deplacement et l’ATK SPD de 5% pendant 10 secondes. Se cumule 5 fois.
De plus, à 5 cumul de Bloodlust, l’utilisateur obtient l’état « Blood Shed » (ndt : carnage/massacre). L’état Blood Shed augmente la portée de « Bloodlust Thrust » de 10 mètres et augmente les dégâts infligés de 100% pendant 10 secondes.
Arme requise : Dague/Rapière/Epée/Katana/Epee colossale/Hache/Lance/Masse/Griffe/Gourdin/Marteau
---
C’est la compétence principale de cette classe je suppose. Fonctionne très bien pour runner Dimension of Souls.
Les 3 Toggles (les états infinis qui bouffent un % de mana par seconde), sont complètement modifées pour se compenser entre elles ainsi que la classe. Fini les « duets (ndt : ?) avec le Glacier Knight!
![](https://i0.wp.com/puu.sh/wpqAv.jpg) ![](https://i0.wp.com/puu.sh/wpqDA.jpg) ![](https://i0.wp.com/puu.sh/wpqED.jpg)
_Soul Flare_
Cout : 3% des MP par seconde
Active « Soul Flare »
Soigne de 10% des dégâts infligés par les attaques de base.
Réduit le temps de recharge des compétences « Bloodlust Thrust », « Riposte » et « Hero » de 50%
L’effet est annulé si les MP sont insuffisants ou si la compétence et re-activée.
---
_Offense not Defense_
Active « Offense Not Defense »
Dégâts subis +20%
Rends 2% des MP par seconde.
Les membres du groupes présents dans les 30 mètres gagnent 220 d’ATK SPD et de ACC.
---
_Final Judgement_
Cout : 2% des MP par seconde.
Active « Final Judgement »
P-ATK +15%
Toutes les attaques de base ont 50% de chances de frapper deux fois.
L’effet est annulé si les MP sont insuffisants ou si la compétence et re-activée.
---
Encore une fois, je suis au courant que « Offense not Defense » a un texte rouge qui doit être supprimé. Je suis également au courant que Final Judgement de devrait pas necessiter de Rapière/Epée. Les deux problèmes seront corrigés.
EDIT : Whoops ‘Offense not Defense’ est niveau 81 (les autres sont 100).
**POST DU 24 JUIN 2017 : LIFE WOSHIPPER & ELEGANT DANCER**
---
Hey tout le monde, juste une petite mise à jour quant à l’avancement du patch. Je n’ai pas eu beaucoup de temps à consacrer à l’équilibrage du PVP mais j’ai tout de même pu planifier 2 – 3 trucs. Cette semaine sera ma dernière semaine d’absence donc je compte en profiter le plus possible avant de rentrer. Je vous tiens tout de même au courant de mon avancement sur le Blade Master. J’ai juste besoin de revoir son arbre de talent [KP = knowledge point] pour l’adapter au nouveau standard mis en place pour harmoniser les stats de toutes les classes (ou tout du moins les stats qui font sens vis-à-vis de la classe elle-même).
Je voudrais réitérer ce que j’ai déjà dit à de multiple reprises aux gens qui ne semblent pas l’avoir encore compris, **toutes ces modifications de classes sont en phase bêta et seront très probablement ré-analysées lors de mon second passage, les changements reçus négativements reçus pourront ainsi être annulés. Ce n’est donc pas la fin du monde si quelque chose déconne. Aussi veuillez ne pas juger la classe avant de l’avoir testée. Ca vous donne juste l’air stupides.**
**Life Worshipper & Elegant Dancer**
* * *
L’un n’est rien sans l’autre. Créez le concert ultime.
**Les combinaisons de compétences sont la clé d’un groupe réussi.**
![](https://i0.wp.com/puu.sh/wt7LJ.jpg) ![](https://i0.wp.com/puu.sh/wt7KD.jpg)
_Song of Light_
Coute 3% de MP par seconde.
Active « Song of Light »
Dégâts reçus -10%
Augmente le taux crit phy de 10% des alliés dans un rayon de 50 mètres.
Combiné avec « Swift Dance », donne l’état « Concert ». « Concert » augmente la P-ATK et la M-ATK de 30% et le tx crit phy et mag de 5%.
L’effet est annulé si les MP sont insuffisants ou si la compétence est re-activée.
Arme requise : Guitare
---
_Swift Dance_
Coute 3% de MP par seconde.
Active « Swift Dance »
Dégâts reçus -10%
Augmente le tx crit mag de tous les alliés dans les 50 mètres de 10%.
Combiné avec « Song of Light » du Life Worshipper, donne l’état « Concert ». « Concert » augmente la P-ATK et la M-ATK de 30% et le tx crit phy & mag de 5%.
L’effet est annulé si les MP sont insuffisants ou si la compétence est re-activée.
---
**Mais parfois, il faut employer vos propres buff pour aider votre groupe.**
![](https://i0.wp.com/puu.sh/wt7NL.jpg) ![](https://i0.wp.com/puu.sh/wt7Ov.jpg)
_Song of Life_
Cout : 355MP
Temps d’incantation : 1 seconde
Augement les HP max de tous les membres du groupe dans les 40 mètres de 20% pendant 20 minutes. De plus, les membres du groupe dans les 40 mètres récupèrent 1716 HP pendant 15 secondes. (ndt : je suppose que c’est par seconde)
Arme requise : Guitare
---
_Dance of the White Dove_
Cout : 23MP
Temps de rechargement : 25 secondes
Temps d’incantation : 2 secondes
La cible est forcée de danser qu’importe ses immunités pendant 3 secondes.
Note : Cette compétence n’affecte que les joueurs.
---
**POST DU 10 JUILLET 2017 : BLADE MASTER**
---
Quatre compétences principales définissent cette classe. Raging River n’existe plus et cette classe peut infliger une tonne de dégâts si elle est jouée correctement. Si vous ne connaissez pas vos combos, autant jouer le samourai de base. Mais si vous les maîtrisez, alors vous serez le plus puissant des DPS physiques du jeu. Même les dégâts de l’Asura ne rivalisent pas avec le burst mono-cible de cette classe.
![](https://puu.sh/wFLnJ.jpg)
_Brainwash_
Cout : 385 MP
Temps de recharge : 2 secondes
Instantané
Augmente la P(ATK de 11445 et inflige les dégâts deux fois a la cible.
Cette compétence démarre le combo « Thousand-Cranes Stance ».
1\. Thousand-Cranes Stance : Brainwash – Point de départ
2\. Thousand-Cranes Stance : Swallowing Strike –
3\. Inflige X dégâts supplémentaires
4\. Thousand-Cranes Stance : Brutal Mirror – Inflige X dégâts supplémentaires
5\. Thousand-Cranes Stance : Oneiric Sword – Inflige X dégâts supplémentaires Cette compétence démarre le combo « Tendon Cut ».
1\. Tendon Cut : Brainwash – Point de départ
2\. Tendon Cut : Swallowing Strike – La cible est renversée pendant 2 secondes Cette compétence démarre le combo « Amputate ».
1\. Amputate : Brainwash – Point de départ
2\. Amputate : Brutal Mirror – Réduit l’EVA de la cible
3\. Amputate : Oneiric Sword – Réduit l’EVA de la cible
4\. Amputate : Swallowing Strike – La cible est reversée pendant 4 secondes. Note : Les deux renversements activent l’immunité.
Arme requise : Katana.
---
![](https://puu.sh/wFLuQ.jpg)
_Swallowing Strike_
Cout : 326MP
Instantané
Augmente l’attaque de 13352 et inflige des dégâts a la cible. Réduit de 15% la Move SPD de la cible pendant 5 secondes. Cette compétence démarre le combo « Tameshigiri Slash »
1\. Tameshigiri Slash : Swallowing Strike – Point de départ
2\. Tameshigiri Slash : Brutal Mirror – Baisse la résistance entaille de la cible
3\. Tameshigiri Slash : Brainwash – Baisse la résistance entaille de la cible
4\. Tameshigiri Slash : Swallowing Strike – Baisse la résistance entaille de la cible Note : Utiliser Swallowing Strike réactivera ce débuff.
Arme requise : Katana
---
![](https://puu.sh/wFLyp.jpg)
_Brutal Mirror_
Cout : 414 MP
Instantané
Augmente la P-ATK et inflige des dégâts a la cible. Réduit de 15% la Move SPD de la cible pendant 5 secondes. Cette compétence démarre le combo « Chi Strike ».
1\. Chi Strike : Brutal Mirror – Point de départ
2\. Chi Strike : Brainwash – Réduit la défense de la cible
3\. Chi Strike : Oneiric Sword – Réduit la défense de la cible
4\. Chi Exposed : Brutal Mirror – Réduit la défense de la cible Note : Utiliser Brutal Mirror réactivera ce débuff
Arme requise : Katana
---
![](https://puu.sh/wFLBL.jpg)
_Oneiric Sword_
Cout : 503 MP
Instantané
Augmente la P-ATK de 15260 et inflige des dégâts à la cible. Réduit de 15% la Move SPD de la cible pendant 5 secondes. Cette compétence démarre le combo « Kanabo Style »
1\. Kanabo Style : Oneiric Sword – Point de départ
2\. Kanabo Style : Brutal Mirror – Dégâts supplémentaires
3\. Kanabo Style : Oneiric Sword – Dégâts supplémentaires
4\. Kanabo Style : Swallowing Strike – Dégâts supplémentaires
5\. Kanabo Style : Oneiric Sword – Dégâts supplémentaires Note : Utiliser Oneiric Sword réactivera ce buff.
Arme requise : Kanata.
---
Note : Cette classe est conçue pour être à haut risque, mais le jeu en vaut la chandelle. J’ai repensé cette classe en me basant sur ce qu’il manquait au Blade Master d’après les joueurs de Samurai. J’espère que tout le monde l’appréciera. Elle sortira particulièrement du lot pour les joueurs qui aiment les classes difficiles à jouer et pas se contenter faire de gros dégâts.
**POST DU 18 JUILLET 2017 : PALADIN**
---
La refonte de cette classe se base sur trois gameplays différents issus des classes Templier et Paladin : Tank Soigneur, Tank Support / Healer monocible (ndt : « Pocket Healer ») et Tank DPS.
Ces styles de jeu sont désormais regroupés dans une seule classe qui vous permettra de les sélectionner via des Sceaux (« Seals »). Contrairement à l’Arch-Elementalist, il ne sera pas vraiment possible de fusionner les différents modes. Il s’agira plus d’un choix définitif lorsque vous déciderez de monter cette classe.
**Tank soigneur**
* * *
Le mode Tank Soigneur est appelé Seal of Light. Ce mode se concentre sur les soins de zone pour le groupe et des buffs de support. Il devrait être capable de remplacer utilement un Clerc grâce à plusieurs compétences qui manquaient au Paladin original. En plus d’être un Soigneur c’est également un Tank, ce qui signifie qu’il possède des éléments qui inciteront les gens à vous frapper en priorité. (plus d’information plus bas)
**Tank Support / Pocket Healer**
* * *
(ndt : « Pocket Healer ». En gros, c’est un soigneur qui se focalise sur un seul joueur uniquement)
Ce mode est spécifiquement envisagé pour soigner UNE cible et buffer UNE cible. Il dispose de buffs et soins plus puissants que le Tank Healer, mais uniquement mono-cibles. Pas grand chose de plus à dire là dessus.
**Tank DPS**
* * *
Ce mode fait appel a la nostalgie des vieux mains Templiers. Il ne s’agit pas de d’en faire un « top killer » en PvP ni le DPS principal en PvE, mais plutôt cet irritant DPS tanky qu’était le Templier original.
J’espère avoir fait du bon boulot. N’étant pas main Templar, je ne peux qu’espérer avoir fait quelque chose de décent.
**Compétences**
* * *
![](https://i0.wp.com/puu.sh/wMDqY.jpg) ![](https://i0.wp.com/puu.sh/wMDoL.jpg) ![](https://i0.wp.com/puu.sh/wMDtA.jpg)
__Blessing Aura_
Seal of Light : Soigne toutes les alliés dans les 25 mètres de la cible et augmente leur P-Heal de 5% pendant 5 secondes.
Seal of Defense : Soigne la cible et augemente ses dégâts infligés de 5% pendant 10 secondes.
Note : Le valeur de soin de base de cette compétence est doublée dans cette forme.
Seal of Punishment : Soigne l’utilisateur de 2% de ses HP max chaque secondes pendant 10 secondes.
Note : Cette compétence necessite l’activation d’un des trois sceaux pour être utilisé.
Arme requise : Dague/Rapière/Epée/Masse/Griffe/Gourdin_
---
__Divine Shield_
Seal of Light : Créé une zone sanctifiée autour de l’utilisateur qui soigne tous les alliés aux alentours de X% de leurs HP max toutes les 2 secondes et réduit leurs dégâts subis de X% pendant 3 secondes. Cependant cette compétence immobilise l’utilisateur qui subit alors X% de dégâts supplémentaires.
Note : L’utilisateur peut toujours utiliser d’autre compétences durant cet effet.
Note : En désactivant de sort, l’utilisateur reçoit un malus de X% de Move SPD pendant 3 secondes.
Note : Retirer le Seal of Light désactive également cette compétence.
Note : Réutiliser cette compétence pour désactiver son effet.
Seal of Defense : Augmente la Move SPD de la cible de X%, réduit les dégâts subis de X% et rend X% des HP Max chaque seconde pendant 10 secondes.
Seal of Punishment : Augmente la M-ATK de l’utilisateur de X% et, lorsque l’utilisateur est attaqué, augemente les dégâts infligés de X% pendant 10 secondes. Se cumule 5 fois.
Note : Cette compétence necessite l’activation d’un des trois sceaux pour être utilisé.
Arme requise : Bouclier_
---
__Seal of Light_
Ressucite la cible avec X% de ses HP et X% de ses MP. La pénalité d’XP est réduite de 50%
Note : Ne peut être utilisé en combat
Seal of Defense : Octroie à la cible l’état « Paladin’s Protection » pendant 5 secondes. L’état « Paladin’s Protection » immunise la cible a tous les dégâts directs endant 3 secondes et lui rend X% de ses HP par seconde lorsque ses points de vie passent en dessous des X%.
Seal of Punishment : Augmente la M-ATK et inflige des dégâts sacré trois fois à la cible, baissant leur résistance lumière de X points pendant 10 secondes.
Note : Cette compétence necessite l’activation d’un des trois sceaux pour être utilisé.
Arme requise : Dague/Rapière/Epée/Masse/Griffe/Gourdin_
---
**POST DU 22 JUILLET 2017 : DRUID**
---
Hey tout le monde, une petite mise à jour rapide sur le patch d’équilibrage. J’espère terminer l’ensemble des classes vers la fin de la semaine prochaine et je planifie une grosse séance de test pour voir quels rectirifcatifs seront nécessaires. Je vais également terminer tous les autres contenus à venir pour ce patch la semaine suivante. J’espère donc pouvoir le déployer vers la fin de la première semaine ou dans la seconde semaine d’Aout (c’est un objectif et non pas une promesse). Rappelez-vous que ces classes ne seront pas figées une fois sorties, des modifications pourront advenir dans le futur. Si l’une d’elles est très mal reçue, elle pourra être annulée ou modifiée à nouveau si nécessaire.
**Druid**
* * *
![](https://i0.wp.com/puu.sh/wQgTd.png) ![](https://i0.wp.com/puu.sh/wQh12.png)
__Shamanic Curse_
Cout : 521 MP
Temps de recharge : 3 secondes
Instantané
Augemente la M-ATK de 14307 et inflige des dégâts de Nature a tous les ennemis dans les 15 mètres de la cible.
_Raging Bear_ : (ndt : transformations)
Octroie à l’utilisateur l’efffet « Raging Bear – Shamanic Curse » qui réduit les dégâts reçus de X pendant 3 secondes lorsqu’il reçoit des dégâts. Se cumule 10 fois.
Note : L’effet est supprimé si l’utilisateur change de forme.
_Shrewd Monkey_ :
Augmente la M-ATK de 14307 et inflige des dégâts de Nature a tous les ennemis dans les 15 mètres. Les ennemis touchés voient leur résistance Nature réduite de X pendant 7 secondes.
_Fanciful Unicorn_ :
Shamanic Curse devient Shamanic Reverse Curse
Supprime un effet négatif de tous les membres du groupe dans les 50 mètres. De plus, il y a 50% de chances d’en supprimer un second.
Note : Shamanic Reverse Curse n’a pas de temps de recharge
Note : Shamanic Reverse Curse a un temps d’incantation. (ndt : non précisé)
Armre Requise : Masse/Guitare/Batôn/Grimoire_
---
__Nature’s Power_
Instantané
A l’aide du pouvoir de la nature, vous transforme en un l’un des trois différents gardiens animaux.
_Raging Bear_ : Ce gardien octroie une grand force pour repousser les puissants adversaires avec facilité, se concentrant sur la défense plutôt que l’attaque.
– Augemente les HP Max de 100%
– Dégâts subis -30%
_Shrewed Monkey_ : Ce gardien, contrairement au Raging Bear, se concentre sur l’attaque au détriment de la défense disposant d’une grande puissance offensive pour vaincre ses adversaires rapidement.
– M-ATK +30%
– Move SPD +10%
_Fanciful Unicorn_ : Ce gardien donne la capacité de protéger les autres, se focalisant sur les soins et la pureté pour vos alliés.
– G-Heal +30%
– Resistances Physique +5
Note : Vous disposez de 10 secondes pour choisir la forme avant que la compétence s’annule d’elle même.
Note : Relancer la compétence pour désactiver la transformation._
---
En utilisant Nature’s Power :
![](https://i0.wp.com/puu.sh/wQh2A.png)
**POST DU 1ER AOUT 2017 : GUARDIANS VALLEY BATTLEFIELD & DARKNESS BLADE**
---
**Guardians Valley Battlefield**
* * *
![](https://www.vendettagn.com/assets/banners/vgn/patch21_class_05.png)
Bienvenue dans le champ de bataille de la vallée des gardiens, la toute nouvelle carte de guerre de territoire à venir sur Eden Eternal Vendetta avec la mise à jour d’équilibrage du PvP. Cette carte est unique en son genre avec ses 5 cristaux à capturer et ses deux boss qui apparaitront durant la guerre de territoire. Ceux-ci donneront à leur vainqueur un buff unique pour aider son équipe à conquérier les cristaux plus facilement, en compagnie des troupes alliées. On se reverra sur le champ de bataille !
![](https://www.vendettagn.com/assets/banners/vgn/patch21_class_06.png)
**Darkness Blade**
* * *
Voir la vidéo => [sur YouTube](https://youtu.be/3-rVq9V2AK0)
