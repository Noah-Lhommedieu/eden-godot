#!/usr/bin/env python3
"""Génère content/lang/en/translations.tsv pour les CARTES dans le périmètre (vague 4 partielle du lot E) : FR → EN par un dictionnaire de noms
de base + motifs de suffixes (glossaire content/lang/en/glossary.tsv). Rejouable ; les lignes déjà relues (status ≠ auto) ne sont pas touchées par lang-import.py."""
import re, subprocess, sys, os

BASE = {
 "Angor":"Angor","Abîme de Baram":"Baram Abyss","Abysse de Siècle":"Century Abyss","Abysse Glacée de Skleros":"Skleros Frozen Abyss","Allolans":"Allolans","Ancien Autel":"Ancient Altar",
 "Antre des Échos":"Echo Lair","Arène des Boss":"Boss Arena","Arène du Démon des Enfers - Assemblée":"Hell Demon Arena - Assembly","Arène Phénix":"Phoenix Arena",
 "Arène Sacrée : Le Crucifix":"Sacred Arena: The Crucifix","Arsenal Fédéral":"Federal Arsenal","Autel du Culte de Mara":"Altar of the Mara Cult","Aven":"Aven","Aven rêvée":"Dreamed Aven",
 "Aventure fabuleuse : Cœur de Dragon":"Fabulous Adventure: Dragon Heart","Baie Bélouga":"Beluga Bay","Banque Meeryasha":"Meeryasha Bank","Base des Vilrequins":"Crankshaft Base",
 "Bassin Shaxia":"Shaxia Basin","Bataille de la Forêt":"Forest Battle","Bataille de l'Océan":"Ocean Battle","Bataille d'Hélios [Groupe]":"Battle of Helios [Party]","Bataille du Désert":"Desert Battle",
 "Bataille Spatiotemporelle":"Spacetime Battle","Bill's Training Facility":"Bill's Training Facility","Bourbes":"The Mire","Braider Army Base":"Braider Army Base","Cabinet du Maire":"Mayor's Office",
 "Canal d'Aven":"Aven Canal","Canal Tassos":"Tassos Canal","Cap Hurlant":"Howling Cape","Carrière Angor":"Angor Quarry","Carrière de Schiste":"Shale Quarry","Caverne de Schiste":"Shale Cavern",
 "Chambre de Baïra":"Baira's Chamber","Champ de Bataille T.4":"Battlefield T.4","Chutes des Âmes en Peine":"Lost Souls Falls","Coeur Baltaroï":"Baltaroi Heart","Cœur Baltaroï":"Baltaroi Heart",
 "Colline Tranquille":"Quiet Hill","Congrès de Braider":"Braider Congress","Conte de légende : les voleurs de trésor et le lutin légendaire.":"Legend Tale: the Treasure Thieves and the Legendary Imp.",
 "Côte de Hurlevent":"Howlwind Coast","Courez ! Des vaches ! Courez !":"Run! Cows! Run!","Course d'Alpagas":"Alpaca Race","Cratère des Géants":"Giants' Crater","Cratère Météoritique":"Meteor Crater",
 "Création de Personnage":"Character Creation","Crête Diamantée":"Diamond Ridge","Crocs Brumeux":"Misty Fangs","Crocvenin":"Venomfang","Crypticône":"Crypticone","Défi des Boss":"Boss Challenge",
 "Désert du Couchant":"Sunset Desert","Dimension Dévastée":"Devastated Dimension","Dimension du chaos":"Chaos Dimension","Domaine de la Noblesse de la Marina d'Edouard":"Edouard Marina Nobles' Quarter",
 "Domaine Démoniaque Intérieur":"Inner Demon Domain","Dôme Céleste":"Celestial Dome","Donjon d'Éveil de Classe - Illusionniste":"Class Awakening Dungeon - Illusionist","Écarlate":"Scarlet",
 "Emplacement de l'Event":"Event Site","Emplacement d'Événement":"Event Site","Éroda":"Eroda","Expédition Anti Dragon - Autel Volcanique":"Anti-Dragon Expedition - Volcanic Altar",
 "Falaise des Grands Vents":"Great Winds Cliff","Faubourg Populaire de la Marina d'Edouard":"Edouard Marina Commoners' District","Fléau":"Scourge","Forêt Arc-en-Ciel":"Rainbow Forest",
 "Forêt Camoufle":"Camouflage Forest","Forêt de Delphes":"Delphi Forest","Forêt d'Eden":"Eden Forest","Forêt Ensorcelée":"Bewitched Forest","Forteresse d'Éternité":"Eternity Fortress",
 "Fort Loselle":"Fort Loselle","Fort Verdure":"Fort Verdure","Frontière de Basel":"Basel Frontier","Garde du Cimetière":"Cemetery Guard","Gardien de l'Arbre de Vie ":"Tree of Life Guardian",
 "Génération Future":"Future Generation","Grey Lynn":"Grey Lynn","Grotte de la Marée des Ombres":"Shadow Tide Grotto","Grotte Non Répertoriée":"Uncharted Cave","Guillotine":"Guillotine",
 "Hautes-terres":"Highlands","Hauteurs Feudegivre":"Frostfire Heights","Île aux Cerisiers":"Cherry Blossom Island","Île de Sakura":"Sakura Island","Île du Boucanier":"Buccaneer Island",
 "Île du Mariage":"Wedding Island","Iyernafalo":"Iyernafalo","Jungle Céleste":"Celestial Jungle","Jungle du Dieu Serpent":"Serpent God Jungle","Jungle Mayland":"Mayland Jungle",
 "Jungle Mystique":"Mystic Jungle","Labo Antigravité":"Antigravity Lab","Laboratoire de Réno":"Reno Laboratory","Labo Vingot":"Vingot Lab","Lagune Enchantée":"Enchanted Lagoon",
 "Le Pouvoir du Cristal d'Eternal":"The Power of the Eternal Crystal","Les 5 Animaux : La légende de Barlilu":"The 5 Animals: The Legend of Barlilu","Les 5 Animaux : La légende de Bonka":"The 5 Animals: The Legend of Bonka",
 "Les 5 Animaux : La légende de Maschatta":"The 5 Animals: The Legend of Maschatta","Les 5 Animaux : La légende de Ron":"The 5 Animals: The Legend of Ron","Les 5 Animaux : La légende de Yula":"The 5 Animals: The Legend of Yula",
 "Les Profondeurs de l'Abysse Glacée de Skleros":"Depths of the Skleros Frozen Abyss","Lieu du rituel (niv. 80 - Vérité et Illusion IV)":"Ritual Site (Lv. 80 - Truth and Illusion IV)","Maison de Marjorie":"Marjorie's House",
 "Manoir":"Manor","Marais Décrépits":"Decrepit Marshes","Marina d'Edouard":"Edouard Marina","Meeryasha":"Meeryasha","Meteor's Landing":"Meteor's Landing","Mine Andrew":"Andrew Mine",
 "Mine du Village Andrew":"Andrew Village Mine","Mont Eden":"Mount Eden","Monts Arides":"Arid Mountains","Monts Calcaires":"Limestone Mountains","Morticora":"Morticora","Orphidée":"Orphidea",
 "Palais des Bourbes":"Mire Palace","Palais des Malices":"Palace of Mischief","Palais Heldentod":"Heldentod Palace","Passage spatiotemporel":"Spacetime Passage","Pays des Merveilles Diablepourpre":"Crimson Devil Wonderland",
 "Pays des souvenirs":"Land of Memories","Pic de Noireflamme":"Blackflame Peak","Pic du Maëlstrom":"Maelstrom Peak","Plaines d'Elos":"Elos Plains","Plaines Dorées":"Golden Plains","Plaines du Levant":"Eastern Plains",
 "Plaines Fleuries":"Flowering Plains","Plaines Séraphines":"Seraphic Plains","Plan de Grille":"Grid Plan","Pourriture":"Rot","Prison du Fantôme Enflammé":"Flaming Ghost Prison","Prison Ossuaire":"Ossuary Prison",
 "Profondeurs du Canal Tassos":"Depths of the Tassos Canal","Profondeurs du Mont Gowen":"Depths of Mount Gowen","Purgatoire : Arène des Chevaliers Templiers":"Purgatory: Templar Knights' Arena","Racine Branda":"Branda Root",
 "Refuge des Sang Noir":"Blackblood Refuge","Rêve du Maire":"Mayor's Dream","Royaume Dévasté":"Devastated Kingdom","Royaume Durango":"Durango Kingdom","Royaume Vide":"Empty Kingdom","Salle Ulta":"Ulta Hall",
 "Sanctuaire du Mal à l'État Pur":"Sanctuary of Pure Evil","Scenic Aurapoint":"Scenic Aurapoint","Siècle":"Century","Sortir des méandres du Fort":"Escape the Fort's Maze","Sturmfrau":"Sturmfrau",
 "Survie sur l'Île Déserte":"Desert Island Survival","Temple Antique du Chaos":"Ancient Temple of Chaos","Temple aquatique":"Water Temple","Temple de Delphes":"Delphi Temple","Temple de la Rose":"Rose Temple",
 "Temple enchanté":"Enchanted Temple","Temple Perdu":"Lost Temple","Temple Spirenfer":"Hellspire Temple","Tente 2":"Tent 2","Terrains d'Entraînement":"Training Grounds","Terre sacrée de Kamodo":"Kamodo Sacred Land",
 "Terres des Dunes Éternelles":"Eternal Dunes","Tombe du Samouraï":"Samurai Tomb","Toundra Hostile":"Hostile Tundra","Tour des Cieux Autel Sinistre":"Heaven Tower: Sinister Altar",
 "Tour des Cieux Hall du Purgatoire":"Heaven Tower: Purgatory Hall","Tour des Cieux Salle Fleurépine":"Heaven Tower: Thornflower Hall","Tour des Cieux Zone de la Mélodie Sacrée":"Heaven Tower: Sacred Melody Zone",
 "Tour des Épreuves":"Tower of Trials","Tour Kahlo":"Kahlo Tower","Tunnel de l'Union de Valence":"Valence Union Tunnel","Tunnel éboulé":"Collapsed Tunnel","Une cave où l'on entrepose de la bière.":"A cellar where beer is stored.",
 "Vallée Batelo":"Batelo Valley","Vallée de la Mort":"Death Valley","Vallée des Rois":"Valley of Kings","Vallée de Vulcain":"Vulcan Valley","Vallée du Sacrifice":"Sacrifice Valley","Vallée Feregal":"Feregal Valley",
 "Veine Tordue":"Twisted Vein","Vieil Entrepôt":"Old Warehouse","Vieux Siècle":"Old Century","Villa Personnelle":"Personal Villa","Ville de Guilde : Couloir de l'Esprit Gardien":"Guild Town: Guardian Spirit Corridor",
 "Volcan Avila":"Avila Volcano","Vortek":"Vortek","Zone de Guerre d'Ezur":"Ezur War Zone",
 "Épreuve des Héros de la Colline Tranquille":"Heroes' Trial of the Quiet Hill","Épreuve des Héros du Chemin d'Occultus":"Heroes' Trial of the Occultus Path",
}
SUFFIX = [(r" \(DJN multijoueur\)$", " (Multiplayer Dungeon)"), (r" \(Solo\)$", " (Solo)"), (r" \(Groupe\)$", " (Party)"), (r" \(multijoueur\)$", " (Multiplayer)"),
          (r" ?<Mode Multijoueur>$", " <Multiplayer Mode>"), (r" ?<multijoueur>$", " <Multiplayer>"), (r" ?<Multijoueur>$", " <Multiplayer>"), (r" ?<Solo>$", " <Solo>"), (r" ?<Groupe>$", " <Party>"),
          (r" ?<1 joueur>$", " <1 player>"), (r" ?<Event de groupe>$", " <Party Event>"), (r" ?<Event 1 joueur>$", " <1-Player Event>")]
PREFIX = [("Illusion Destructrice : ", "Destructive Illusion: "), ("Épreuve : ", "Trial: "), ("Épreuve: ", "Trial: ")]

def psql(sql):
    r = subprocess.run(["docker", "exec", "edengodot-postgres", "psql", "-U", "postgres", "-d", "eden_content", "-At", "-F", "\t", "-c", sql], capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.splitlines() if l]

def tr(fr):
    fr = re.sub(r"[\u00a0\u202f]", " ", fr)
    s, pre, suf, hero = fr, "", "", False
    s = re.sub(r"\s+:\s*", " : ", s) if s.startswith(("Épreuve", "Illusion")) else s
    for a, b in PREFIX:
        if s.startswith(a): pre, s = b, s[len(a):]; break
    for rx, rep in SUFFIX:
        if re.search(rx, s): suf, s = rep, re.sub(rx, "", s); break
    if s.endswith(" Héroïque"): hero, s = True, s[: -len(" Héroïque")]
    sstrip = re.sub(r" S$", "", s) if pre.startswith("Trial") else s
    base = BASE.get(s) or BASE.get(sstrip) or BASE.get(sstrip.strip()) or BASE.get(re.sub(r"\s+", " ", fr))
    if base is None: return None
    if sstrip != s and BASE.get(s) is None: base += " S"
    return f"{pre}{base}{' Heroic' if hero else ''}{suf}"

rows, missing = [], []
for nid, fr in psql("select node_id, name_fr from map.map where in_scope order by node_id"):
    en = tr(fr)
    (rows if en else missing).append((nid, fr, en))
out = "content/lang/en/translations.tsv"
keep = [l for l in open(out, encoding="utf-8").read().splitlines() if l and not l.startswith("map\t")] if os.path.exists(out) else ["entity_type\tentity_id\tfield\tvalue\tstatus"]
keep += [f"map\t{n}\tname\t{en}\tauto" for n, fr, en in rows]
open(out, "w", encoding="utf-8").write("\n".join(keep) + "\n")
print(f"{len(rows)} cartes traduites, {len(missing)} sans traduction")
for n, fr, _ in missing[:20]: print("  manque", n, fr)
