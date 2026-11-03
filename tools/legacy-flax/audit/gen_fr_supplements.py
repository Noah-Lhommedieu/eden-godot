#!/usr/bin/env python3
"""Suppléments FR maison : lignes traduites pour les ids complétés (L80+).

Append dans eden_godot/godot_project/gamedata/data/db/lang/fr/ — avec merge
par id, elles écrasent le remplissage EN de la base officielle.
Idempotent : remplace la ligne si l'id existe déjà.
"""
import os, re

DIR = '/home/ster2lune/Bureau/reverseden/portage/eden_godot/godot_project/gamedata/data/db/lang/fr'

MISSIONS = {
 3475: ("Nv. 80 La montre du temps et de l'espace I (Illusionniste)", "Quête d'Éveil de Classe",
  "Apportez la « lettre de mission de Silfer » au Cap de la Fureur (244, 203), trouvez le « Vieux Capitaine Silver » et comprenez ce qu'il vous demande. "),
 3476: ("Nv. 80 La montre du temps et de l'espace II (Illusionniste)", "Quête d'Éveil de Classe",
  "Obtenez la « Boisson énergisante Voile Noire » et le « Bonbon de vitalité Voile Noire » auprès du « Marin Voile Noire » et du « Combattant Voile Noire » sur la côte du Cap de la Fureur. Remettez-les au « Petit Jim ». "),
 3477: ("Nv. 80 La montre du temps et de l'espace III (Illusionniste)", "Quête d'Éveil de Classe",
  "Au Cap de la Fureur (495,348), utilisez la « vieille montre à gousset » pour entrer dans la faille spatio-temporelle, vainquez le roi démon « Kag Riel » et trouvez la « nouvelle montre à gousset ». "),
 3478: ("Nv. 80 La montre du temps et de l'espace IV (Illusionniste)", "Quête d'Éveil de Classe",
  "Remettez la « Vieille montre à gousset » et la « Nouvelle montre à gousset » au « Vieux Capitaine Silver », écoutez l'histoire du Cap de la Fureur et obtenez l'éveil d'Illusionniste. "),
 3479: ("Nv. 90 Le successeur de la salle de recherche", "Quête spéciale",
  "Allez à la capitale de Mir Yaxiu et trouvez la secrétaire du syndicat Yvonne (403, 420) pour comprendre la situation. "),
 3480: ("Nv. 80 Épreuve de compétence martiale I (Rôdeur)", "Quête d'Éveil de Classe",
  "Apportez le « carquois » à la Lande Bénie (400, 180) en passant par la Colline de la Tranquillité, et trouvez le Capitaine des Rôdeurs Zagreb pour comprendre sa mission. "),
 3481: ("Nv. 80 Épreuve de compétence martiale II (Rôdeur)", "Quête d'Éveil de Classe",
  "Éliminez 15 léopards cannibales de la Forêt tropicale de la Vipère pour prouver votre force, puis retournez voir Zagreb. "),
 3482: ("Nv. 80 Épreuve de compétence martiale III (Rôdeur)", "Quête d'Éveil de Classe",
  "Éliminez 15 dragons à couronne de pierre violents dans la Vallée de la Mort pour prouver votre courage face à la mort, puis retournez voir Zagreb. "),
 3483: ("Nv. 80 Épreuve de compétence martiale IV (Rôdeur)", "Quête d'Éveil de Classe",
  "Trouvez Zagreb, entrez dans la Vallée du Dragon Noir, cherchez <l'Archer de l'Art de Guerre> et passez les trois épreuves ; apprenez de lui et obtenez l'éveil de Rôdeur. "),
 3484: ("Nv. 80 Épreuve du Roi Vertueux I (Sage)", "Quête d'Éveil de Classe",
  "Dans la cité du Très Saint Blason (160,341), trouvez Helena pour comprendre la mission. "),
 3485: ("Nv. 80 Épreuve du Roi Vertueux II (Sage)", "Quête d'Éveil de Classe",
  "Trouvez Patricia dans la Plaine de glace cruelle (138,407) et demandez des indices sur les notes du Roi Vertueux. "),
 3486: ("Nv. 80 Épreuve du Roi Vertueux III (Sage)", "Quête d'Éveil de Classe",
  "Trouvez Apoda dans les Terres de l'Honneur (82,349) et demandez l'emplacement du Temple de l'Esprit du Roi Vertueux. "),
 3487: ("Nv. 80 Épreuve du Roi Vertueux IV (Sage)", "Quête d'Éveil de Classe",
  "Trouvez la pierre du sage dans l'ancien royaume de Dorent (233,495), entrez dans le palais du sage pour obtenir la seconde moitié des notes du Roi Vertueux et remettez-la avec la première moitié à Helena pour obtenir l'éveil de Sage. "),
 3488: ("Nv. 80 Rugissement des Dragons I (Chevalier Dragon)", "Quête d'Éveil de Classe",
  "Une fois le niveau 83 atteint, allez dans le donjon de la jungle de Méranti « Vallée de Baatro », cherchez l'« Herbe-dragon Bart » et remettez-la à Madame Kelly sur le Plateau de l'Aube. La tâche doit être accomplie en mode intensité solo. "),
 3489: ("Nv. 80 Rugissement des Dragons II (Chevalier Dragon)", "Quête d'Éveil de Classe",
  "Apportez le paquet de Madame Kelly à l'ouest du Plateau de l'Aube (41,409) pour trouver le garde de Tolède Amre, et renseignez-vous sur la situation du camp des dragons. "),
 3490: ("Nv. 80 Rugissement des Dragons III (Chevalier Dragon)", "Quête d'Éveil de Classe",
  "Parlez à Madame Kelly devant la cité royale de Tolède sur le Plateau de l'Aube, puis allez à la Colline du Souffle du Dragon dans la même zone pour trouver le Dragonnier de Tolède Korah (636, 252) et lui faire votre rapport. "),
 3491: ("Nv. 80 Rugissement des Dragons IV (Chevalier Dragon)", "Quête d'Éveil de Classe",
  "Entrez dans la « Crête Suspendue de la Tempête » avec le Dragonnier de Tolède Kola sur le Plateau de l'Aube, vainquez <le Dragon Empereur> Baraka pour résoudre les querelles draconiques, puis retournez sur le Plateau de l'Aube faire votre rapport à Madame Kelly. "),
 3496: ("Nv. 80 Conflit de lumière et d'obscurité I (Épéiste Magique)", "Quête d'Éveil de Classe",
  "Allez au [Bassin des Ouragans] avec le « Motif magique », éliminez 10 morts-vivants de Prak, puis rendez-vous auprès de Sakavo. "),
 3497: ("Nv. 80 Conflit de lumière et d'obscurité II (Épéiste Magique)", "Quête d'Éveil de Classe",
  "Allez dans la [Forêt tropicale de la Vipère] pour éliminer 10 Azna sanguinaires, puis rendez-vous auprès de Sakavo. "),
 3498: ("Nv. 80 Conflit de lumière et d'obscurité III (Épéiste Magique)", "Quête d'Éveil de Classe",
  "Allez dans la Vallée de la Mort et parlez à Sakavor, entrez dans l'Abîme du Chaos et cherchez <l'Épéiste Magique> Spade. "),
 3499: ("Nv. 80 Conflit de lumière et d'obscurité IV (Épéiste Magique)", "Quête d'Éveil de Classe",
  "Allez au plus profond de l'Abîme du Chaos et récupérez le pouvoir de Spade. "),
}

NPCS = {
 51996: "Maître de l'Art de Guerre Sagittaire",
 51999: "Gardes de Tolède",
 52023: "Dragonniers de Tolède",
 52031: "Épéiste Magique",
 52032: "Épéiste Magique",
}

ITEMS = {  # id: (nom, desc) — desc « Objet de Quête » sauf armes
 571: ("Vieille montre à gousset", "Objet de Quête"),
 572: ("Nouvelle montre à gousset", "Objet de Quête"),
 574: ("Boisson énergisante Voile Noire", "Objet de Quête"),
 575: ("Bonbon de vitalité Voile Noire", "Objet de Quête"),
 576: ("Notes du Roi Vertueux (première moitié)", "Objet de Quête"),
 577: ("Notes du Roi Vertueux (seconde moitié)", "Objet de Quête"),
 578: ("Mot de Patricia", "Objet de Quête"),
 29563: ("Herbe-dragon Bart", "Objet de Quête"),
 29564: ("Paquet de Madame Kelly", "Objet de Quête"),
 29584: ("Espadon de Spade", ""),
 29585: ("Épée d'estoc de Spade", ""),
 29586: ("Épée à une main de Spade", ""),
 29590: ("Âme d'Élan", "Objet de Quête"),
 29591: ("Âme de Baraka", "Objet de Quête"),
}

# 136 partenaires 45xxx : paires forme-combat / forme-sort, traduits proprement.
PARTNERS = {
 45004:"Labrador combattant",45005:"Labrador ensorcelé",
 45006:"Spitz nain combattant",45007:"Spitz nain ensorcelé",
 45008:"Chat à poil court combattant",45009:"Chat à poil court ensorcelé",
 45010:"Chihuahua combattant",45011:"Chihuahua ensorcelé",
 45012:"Shiba Inu combattant",45013:"Shiba Inu ensorcelé",
 45014:"Migru combattant",45015:"Migru ensorcelé",
 45016:"Jin Jira combattant",45017:"Jin Jira ensorcelé",
 45018:"Nian combattant",45019:"Nian ensorcelé",
 45020:"Chat Highland Fold combattant",45021:"Chat Highland Fold ensorcelé",
 45022:"Chaton bleu combattant",45023:"Chaton bleu ensorcelé",
 45024:"Chat masqué combattant",45025:"Chat masqué ensorcelé",
 45026:"Papillon combattant",45027:"Papillon ensorcelé",
 45028:"Caniche rouge combattant",45029:"Caniche rouge ensorcelé",
 45030:"Bull Terrier combattant",45031:"Bull Terrier ensorcelé",
 45032:"Chat-pantin combattant",45033:"Chat-pantin ensorcelé",
 45034:"Chien-faon de Noël combattant",45035:"Chien-faon de Noël ensorcelé",
 45036:"Husky combattant",45037:"Husky ensorcelé",
 45038:"Dragon rouge feu-givre",45039:"Dragon doré du crépuscule",
 45040:"Dragon d'argent neige-froide",45041:"Dragon démon pictural",
 45042:"Lapin blanc combattant",45043:"Lapin blanc ensorcelé",
 45044:"Passereau combattant",45045:"Passereau ensorcelé",
 45046:"Faucon combattant",45047:"Faucon ensorcelé",
 45048:"Lapin-gelée rose combattant",45049:"Lapin-gelée rose ensorcelé",
 45050:"Lapin-gelée coco combattant",45051:"Lapin-gelée coco ensorcelé",
 45052:"Lapin-gelée sylvestre combattant",45053:"Lapin-gelée sylvestre ensorcelé",
 45054:"Renard blanc Kat combattant",45055:"Renard blanc ensorcelé",
 45056:"Renard noir Kat combattant",45057:"Renard noir ensorcelé",
 45058:"Renard roux Kat combattant",45059:"Renard roux ensorcelé",
 45060:"Esprit sylvestre combattant",45061:"Esprit sylvestre ensorcelé",
 45062:"Esprit des marais combattant",45063:"Esprit des marais ensorcelé",
 45064:"Elfe des bois sombres combattant",45065:"Elfe des bois sombres ensorcelé",
 45066:"Renardeau combattant",45067:"Renardeau ensorcelé",
 45068:"Ourson Kat combattant",45069:"Ourson Kat ensorcelé",
 45070:"Lapin fuyard combattant",45071:"Lapin fuyard ensorcelé",
 45072:"Guerrier mécanique‧Percée",45073:"Guerrier mécanique‧Bouclier",
 45074:"Diablotin combattant",45075:"Diablotin ensorcelé",
 45076:"Bonhomme de pain d'épices combattant",45077:"Bonhomme de pain d'épices ensorcelé",
 45078:"Renard spirituel à neuf queues combattant",45079:"Renard spirituel à neuf queues ensorcelé",
 45080:"Chat botté épéiste combattant",45081:"Chat botté épéiste ensorcelé",
 45082:"Renard de feu à neuf queues combattant",45083:"Renard de feu à neuf queues ensorcelé",
 45084:"Phénix aux ailes gelées",45085:"Phénix Hong Yan",
 45086:"Phénix du crépuscule noir",45087:"Phénix Yaoyang",
 45088:"Dame blanche combattante",45089:"Dame blanche ensorcelée",
 45090:"Petite verte combattante",45091:"Petite verte ensorcelée",
 45092:"Cochon pirate combattant",45093:"Cochon pirate ensorcelé",
 45094:"Singe boxeur sanguinaire",45095:"Singe boxeur sage",
 45096:"Kaminin Tetsubei du combat sanglant",45097:"Kaminin Shinbei le sage",
 45098:"Renarde-fée Inari combattante",45099:"Renarde-fée Inari ensorcelée",
 45100:"Ours-burger combattant",45101:"Ours-burger ensorcelé",
 45102:"Bacchus le combattant",45103:"Bacchus l'ensorcelé",
 45104:"Bébé Cupidon espiègle",45105:"Bébé Cupidon énergique",
 45106:"Anubis de la guerre sanglante",45107:"Anubis le sage",
 45108:"Pingouin Toot sanguinaire",45109:"Pingouin Toot le sage",
 45110:"Yeux cernés sanguinaires",45111:"Yeux ronds noirs sages",
 45112:"Princesse sirène espiègle",45113:"Princesse sirène énergique",
 45114:"Petite démone Lilith espiègle",45115:"Petite démone Lilith vigoureuse",
 45116:"Dieu de la mort Dennis cruel",45117:"Dennis du crépuscule sombre",
 45118:"Chevalier lion Roland courageux",45119:"Chevalier lion Roland fier",
 45120:"Grand dragon démon de la nuit Iru",45121:"Dragon démon de la nuit Ilu mystérieux",
 45122:"Déesse de guerre Athéna précise",45123:"Déesse de guerre Athéna sainte",
 45124:"Garçon Zashiki de Juli",45125:"Garçon Zashiki priant",
 45126:"Ange sacré Angel bombardé",45127:"Ange sacré Angel du vœu divin",
 45128:"Grande princesse-fée Alisha",45129:"Princesse-fée Alisa du vœu divin",
 45130:"Soubrette aux oreilles de chat Fina ambitieuse",45131:"Soubrette aux oreilles de chat Fina érudite",
 45132:"Poupée magique Palim active",45133:"Poupée magique Palym mystérieuse",
 45134:"Renarde Rika espiègle",45135:"Renarde-sorcière Rika mystérieuse",
 45136:"Petite Chaperon rouge Jenny l'alerte",45137:"Petite Chaperon rouge Jenny l'érudite",
 45138:"Scorpion bleu cuirassé du combat sanglant",45139:"Scorpion bleu cuirassé mystérieux",
}

def upsert(path, rows):
    existing = {}
    lines = []
    if os.path.exists(path):
        for l in open(path, encoding='utf8').read().split('\n'):
            m = re.match(r'^(\d+)\|', l)
            if m: existing[int(m.group(1))] = len(lines)
            lines.append(l)
    for i, row in sorted(rows.items()):
        if i in existing:
            lines[existing[i]] = row
        else:
            lines.append(row)
    open(path, 'w', encoding='utf8').write('\n'.join(lines))
    return len(rows)

n = upsert(DIR + '/t_mission.ini', {i: f"{i}|{t}|{z}|{d}" for i, (t, z, d) in MISSIONS.items()})
print('t_mission:', n)
n = upsert(DIR + '/t_npc.ini', {i: f"{i}|{nm}" for i, nm in NPCS.items()})
print('t_npc:', n)
n = upsert(DIR + '/t_item.ini', {i: f"{i}|{nm}|{d}" for i, (nm, d) in ITEMS.items()})
print('t_item:', n)
n = upsert(DIR + '/t_partner.ini', {i: f"{i}|{nm}|" for i, nm in PARTNERS.items()})
print('t_partner:', n)
