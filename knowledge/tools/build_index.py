#!/usr/bin/env python3
"""Genere INDEX.md pour knowledge/wiki-fr et knowledge/guides-fr.

Lit tools/index_data.json (produit par extract_fr.py).
"""
import json
import re
from pathlib import Path

ROOT = Path("/home/ster2lune/Bureau/reverseden/eden-godot/knowledge")
DATA = json.loads((ROOT / "tools/index_data.json").read_text(encoding="utf-8"))

# ---------------------------------------------------------------- thèmes
THEMES = [
    ("portail", "Pages portail et sommaires"),
    ("classes", "Classes, prérequis, certificats et compétences"),
    ("races", "Races, caractéristiques et création de personnage"),
    ("equipements", "Équipements : armes, armures, accessoires, améliorations"),
    ("artisanat", "Artisanat racial et craft"),
    ("donjons", "Donjons, instances et zones de combat"),
    ("monde", "Monde d'Ezur : géographie, villes, zones, PNJ"),
    ("monstres", "Monstres, boss et bestiaire"),
    ("montures", "Montures"),
    ("guildes", "Guildes, villes de guilde et PVP de guilde"),
    ("systemes", "Systèmes de jeu : couple, familiers, trophées, monnaies…"),
    ("quetes", "Quêtes détaillées et livres de Hautes Terres"),
    ("divers", "Divers et pages méta"),
    ("archives", "Archives de dates (listings WordPress)"),
    ("attachments", "Pages d'attachement image (WordPress)"),
]

# --- wiki-fr : nom de fichier (sans .html) -> theme
W3_CLASSES = {
    "Classes", "Tableau_des_classes", "Apparence_de_classe", "Les_Talents",
    "Les_Certificats", "Sorts_de_race_(par_classe)",
    "Comparatif_des_compétences_raciales", "''Canonnier''",
    "Tank", "Tank_(Branche_de_classes)", "Tank_(Gameplay)", "Défense",
    "DPS_à_Distance", "DPS_Corps_à_Corps", "DPS_Magiques",
    "DPS_mêlée_(_Gameplay_)", "Soins",
    "Guerrier", "Chevalier", "Templier", "Chevalier_Dragon",
    "Chevalier_du_Chaos", "Voleur", "Assassin", "Danselame", "Samouraï",
    "Chasseur", "Ingénieur", "Corsaire", "Clerc", "Barde", "Shaman",
    "Sage", "Magicien", "Illusionniste", "Démoniste",
}
W3_RACES = {"Races", "Les_Races", "Caractéristiques", "Traits_héroïques",
            "Titre", "Liste_des_Titres", "Tableaux_d'Expérience"}
W3_EQUIP = {
    "Armes", "Armes_Blanches", "Armes_Contondantes", "Armes_Magiques",
    "Armes_éveillées_(en_cours_d'écriture)", "Arcs", "Bâtons",
    "Épées_à_une_main", "Épées_colossales", "Gourdins", "Griffes",
    "Grimoires", "Guitares", "Haches", "Katanas", "Masses",
    "Pièces_d'Artillerie", "Piques", "Poignards", "Projectiles",
    "Rapières", "Arc_Longinus", "Aile_d'Ouragan_d'Icare",
    "Souffle_du_dragon_d'Améthyste", "Papillon_Majestueux",
    "Armures", "Armures_Légères", "Armures_Lourdes", "Boucliers",
    "Robes_de_Protection", "Robe_du_Pouvoir_Magique", "Tunique_de_Fer_Noir",
    "Tenue_du_Maître-lame", "Vetements_chauds", "Mains_magiques",
    "Accessoires", "Anneaux", "Colliers", "Capes", "Sacs_à_dos",
    "Costumes", "Teintures",
    "Amélioration_d'équipement", "Améliorer_la_Qualité", "Barre_de_Rupture",
}
W3_CRAFT = {"Craft", "Ingénierie_(Zumis)", "Tissage_(Ezelins)",
            "Fusion_de_Bijoux_(Humains)", "Décrypteur_de_Glyphes_(Ursuns)"}
W3_DONJONS = {"Donjons", "Boss_de_Donjons", "Canal_Tassos",
              "Canal_Tassos_Héroïque",
              "Trophées_obtenus_dans_les_Donjons",
              "Trophées_obtenus_dans_les_Donjons_(niveau_30_à_60)",
              "Trophées_obtenus_dans_les_Donjons_(niveau_inférieur_à_30)"}
W3_MONDE = {"Géographie", "Aven", "PNJ_d'Aven", "Les_associations_a_aven!!",
            "Carrière_Angor", "Plaines_d'Elos", "Plaines_Dorées",
            "Monts_Calcaires", "Lagune_Enchantée", "Frontière_de_Basel",
            "Fort_Verdure", "Les_Ruines", "Tindilos", "Sabaster",
            "Ressources_Locales"}
W3_MONSTRES = {"Monstres", "Monstres_Élites", "Cerbère_des_Enfers",
               "Champinoïde_rouge", "Charognard_démoniaque",
               "Traitre_Violent", "Rabid_Chimera", "Mandragore", "Volesang",
               "Mori_Mori", "Calcaire_Forgé", "Angelée_Arc-en-ciel",
               "Juan", "Justin_Allspore", "Papineau", "Guillaume"}
W3_MONTURES = {n for n in
               "Montures Alpaga_brillant Alpaga_de_prairie Avolpaga "
               "Barbapalpaga Royalpalga Saharalpaga Portalpaga_volant "
               "Licorne_boréale Licorne_boréale_(Exceptionnelle) "
               "Licorne_boréale_(Supérieure) Licorne_des_tempêtes "
               "Licorne_des_tempêtes_(Supérieure) Mysterious_Uniceros "
               "Yack_de_voyage Yack_de_voyage_aventurier "
               "Yack_de_voyage_de_luxe Yak_à_Cornes_Aiguisés "
               "Carrosse_citrouille Carrosse_citrouille_(Exceptionnel) "
               "Carrosse_citrouille_(Supérieur) Dinde_en_cavale "
               "Dinde_en_cavale_(Exceptionelle) "
               "Dinde_en_cavale_(Supérieure) Destrier_de_preux_chevalier "
               "Destrier_de_preux_chevalier_(Supérieur) Sidecar_Futuriste "
               "Sidecar_Futuriste_Nitro Sidecar_Futuriste_Turbo "
               "Steam_Locomotive Hélicoptère_Steampunk_(Exceptionnel) "
               "Armure_Mobile_classe_Titan_(Supérieur) "
               "Armure_Mobile_MGS_ED209_Commando "
               "Prototype_Auricom_Version_2 Mecha_dragon "
               "Poisson_Rêve_(Supérieur) Tortue_de_cristal "
               "Puma_enflammé Stalwart_Gobbler "
               "Dragon_abyssal Dragon_Fantastique "
               "Dragon_Flamboyant_Indigo Sanglier "
               "Sanglier_No_Future Sanglier_punk Sanglier_sauvage "
               "Dents_de_sabre Dent_de_sabre_du_blizzard "
               "Dent_de_sabre_sauvage".split(" ")}
W3_GUILDES = {"Guilde", "Ville_de_Guilde", "Quêtes_de_guilde",
              "Monstres_de_guilde", "Monstres_de_Guilde",
              "Trophées_obtenus_par_les_Boss_de_guildes"}
W3_SYSTEMES = {"Quêtes", "Arène", "Familiers", "Couple", "Cartes_de_Trésor",
               "Chasseurs_de_Trésors", "Boite_Mystique", "Trophées",
               "Trophées_du_Centre_d'Amitié_(Cristaux_d'Asbi)",
               "-Trophés_obtenus_par_les_cristaux_Asobi", "Cristaux_Asbee",
               "Manoir", "Ferme_Organique", "Amis_et_Ennemis",
               "Gestionnaire_de_l'inventaire", "Lexique",
               "Radio_d'Eden_Eternal"}
W3_QUETES = {n for n in
             "Votre_Première_Quête_I Votre_première_quête_II "
             "Votre_première_quête_III Votre_première_quête_IV "
             "Votre_première_quête_V Oracle_magicienne_I "
             "Oracle_magicienne_II Oracle_magicienne_III "
             "Oracle_magicienne_IV Oracle_magicienne_V Apprenti_Livreur "
             "Astuce_de_Ravitaillement Crise_au_village_d'Andrew "
             "Cuisine_du_Front Découverte_Bénéfique Détournement Escorte "
             "Fuite Indice_du_cristal_I Indice_du_cristal_II "
             "Indice_du_cristal_III Les_jarres_cachées "
             "Mission_de_Patrouille Récupérer_le_Ravitaillement "
             "Regard_d'Expert Réparer_la_Machine Tricot_d'amour "
             "Voleur_de_Fourrage Chevalier_Bison Affaires_Militaires Arsenal_Fédéral "
             "Diseuse_de_Fortune L'art_de_la_Guerre Mode_Ninja".split(" ")}
W3_THEME = {}
for t, names in (("classes", W3_CLASSES), ("races", W3_RACES),
                 ("equipements", W3_EQUIP), ("artisanat", W3_CRAFT),
                 ("donjons", W3_DONJONS), ("monde", W3_MONDE),
                 ("monstres", W3_MONSTRES), ("montures", W3_MONTURES),
                 ("guildes", W3_GUILDES), ("systemes", W3_SYSTEMES),
                 ("quetes", W3_QUETES)):
    for n in names:
        W3_THEME[n] = t

# --- guides-fr : fichier relatif -> theme
def guides_theme(f: str) -> str:
    s = f.rsplit("/", 1)[-1][:-5]
    if f.count("/") == 0:
        return "portail"
    if s.startswith("quete-de-classe-eveillee") or \
            s.startswith("quetes-des-classes-eveillees") or \
            s.startswith("competences-des-") or \
            s.startswith("competences-du-chevalier-dragon"):
        return "classes"
    if s.startswith("caracteristiques-raciales"):
        return "races"
    if s.startswith("artisanat-") or s in ("le-craft", "compos-de-craft"):
        return "artisanat"
    if s.startswith(("donjons-", "prive-donjons-")) or s in (
            "la-tour-des-cieux", "le-laboratoire-de-reno", "purgatoire",
            "arene-du-temple-antique-du-chaos", "les-echecs",
            "expedition-anti-dragon-autel-volcanique",
            "dragon-slaying-expedition", "4trial", "gates-of-pandemonium",
            "zones-de-combat", "donjons-dasymo",
            "donjons-des-cinq-animaux"):
        return "donjons"
    if s in ("guilde-et-ville-de-guile", "elimination-de-guilde",
             "guerres-de-territoire"):
        return "guildes"
    if s in ("geographie-du-monde-dezur", "cristaux-dasbi",
             "monstres-mutants", "taen-et-les-pirates-daven",
             "boss-de-guilde-world-boss", "monnaies-et-devises-dezur",
             "gloires-daven"):
        return "monde"
    if s in ("armes-eveillees-75", "armes-eveillees-75-2",
             "armures-eveillees-lvl-70", "armures-eveillees-lvl-80",
             "armures-eveillees-lvl-95", "boites-darmures-des-elites",
             "recolter-les-coffres-darmures-68-et-73", "costumes",
             "costumes-legendaires-disponibles-en-jeu",
             "montures-disponibles-en-jeu", "infusion-de-mana",
             "succes-rune-vers-les-etoiles"):
        return "equipements"
    if s in ("prerequis-des-classes", "expertise-de-classe",
             "certificats-de-classe",
             "combinaisons-des-certificats-de-classe", "les-sorts-niv-72",
             "traits-heroiques", "le-messie-mimic", "la-quete-du-sage"):
        return "classes"
    if s in ("systeme-de-couple-love-co", "familiers", "la-boite-mystique",
             "le-chemin-de-la-destinee", "systeme-de-villa",
             "meteores-etoiles-et-spheres",
             "gemmes-et-cartes-de-metamorphose", "trophees-de-gloire-regionale",
             "trophees-des-donjons-heroiques", "trophees-des-donjons-regionaux"):
        return "systemes"
    if s in ("aventures-de-la-conteuse-kimy",
             "secourir-les-citoyens-du-roi-alpaga",
             "gemmes-de-la-tour-des-cieux", "gemmes-du-labo-de-reno",
             "gemmes-du-4trial-vendetta"):
        return "quetes"
    if s in ("contributeurs", "bienvenue"):
        return "divers"
    return "divers"

# hors périmètre : contenu niveau > 90 ou exclusif Vendetta
HORS = re.compile(r"(lvl-9[0-9]|lvl-10\d|hq-9[5-9]|hq-100|dod-95|"
                  r"niv[.\s-]*9[5-9]|dragon-slaying|"
                  r"vgn|vendetta|pandemonium|4trial|luminary|"
                  r"dragon-soul-dragon-emperor|patch-29)", re.I)
PARTIEL = {"2017/05/30/donjons-lvl-81-a-ω-de-kamodo-au-temple-enchante.html",
           "2017/05/30/donjons-lvl-81-a-ω-de-kamodo-au-temple-enchante_8b58c2.html",
           "2017/06/12/zones-de-combat.html", "classes.html",
           "equipements.html", "artisanat.html",
           "v-monde-dezur-et-ressources.html",
           "2017/06/08/gloires-daven.html",
           "2017/09/01/expedition-anti-dragon-autel-volcanique.html",
           "2017/06/09/armes-eveillees-75-2.html",
           "2017/06/06/donjons-heroiques-hq-90.html",
           "2017/06/16/trophees-des-donjons-heroiques.html",
           "2017/06/16/trophees-de-gloire-regionale.html"}

# ------------------------------------------------------- résumés curés
CURATED = {
# wiki-fr
"Classes": "Système de classes : 5 branches, changement de classe libre, niveau de classe distinct du niveau personnage.",
"Tableau_des_classes": "Tableau des classes par branche : tank, DPS corps-à-corps, à distance, magiques et soins.",
"Races": "Les 5 races jouables : Humain, Zumi, Anuran, Ursun, Ézelin.",
"Les_Races": "Présentation des races jouables et de leurs particularités.",
"Caractéristiques": "Explication des stats du personnage (FOR, AGI, INT, SAG, CHA…).",
"Traits_héroïques": "Traits héroïques choisis à la création du personnage.",
"Tableaux_d'Expérience": "Tables d'XP par niveau (personnage et classes).",
"Armes": "Vue d'ensemble des types d'armes du jeu.",
"Armures": "Vue d'ensemble des types d'armures (lourde, légère, robe).",
"Amélioration_d'équipement": "Renforcement d'équipement : parchemins, enchantement.",
"Améliorer_la_Qualité": "Montée en qualité d'un objet (blanc→vert→bleu→or…).",
"Barre_de_Rupture": "Système de barre de rupture des boss (break) et ses bonus.",
"Craft": "Système de craft : fusion d'objets via les PNJ et l'atelier.",
"Ingénierie_(Zumis)": "Artisanat racial zumi : fabrication de gemmes et composants.",
"Tissage_(Ezelins)": "Artisanat racial ézelin : étoffes pour trophées.",
"Fusion_de_Bijoux_(Humains)": "Artisanat racial humain : fusion de bijoux/gemmes.",
"Décrypteur_de_Glyphes_(Ursuns)": "Artisanat racial ursun : glyphes.",
"Donjons": "Liste des donjons du jeu par zone et niveau.",
"Boss_de_Donjons": "Boss de donjons : localisation et particularités.",
"Canal_Tassos": "Donjon Canal Tassos (Forêt Arc-en-Ciel) : guide.",
"Canal_Tassos_Héroïque": "Version héroïque du Canal Tassos.",
"Montures": "Liste des montures : vitesses, prix, obtention.",
"Guilde": "Système de guilde : création, niveaux, avantages.",
"Ville_de_Guilde": "Ville de guilde : bâtiments, PNJ, récolte d'artisanat.",
"Quêtes_de_guilde": "Quêtes de guilde et récompenses.",
"Monstres_de_Guilde": "Boss de guilde : invocation et butin.",
"Couple": "Système de couple (Love Co.) : avantages et fonctionnement.",
"Familiers": "Familiers : obtention, races, compétences.",
"Arène": "Arène PVP : règles et récompenses.",
"Quêtes": "Vue d'ensemble des types de quêtes.",
"Cartes_de_Trésor": "Cartes de trésor : obtention et utilisation.",
"Boite_Mystique": "Boîte mystique : contenu et récompenses.",
"Cristaux_Asbee": "Cristaux d'Asbi : collecte dans les coffres et échanges au Centre d'amitié.",
"Trophées": "Trophées : équipements de boss à bonus déclenchés.",
"Manoir": "Manoir (villa personnelle) : création et banquets.",
"Ferme_Organique": "Ferme organique de ville de guilde : récoltes.",
"Géographie": "Carte et régions du monde d'Ezur.",
"Aven": "Capitale Aven : quartiers, PNJ, services.",
"Monstres": "Bestiaire général : types et niveaux de monstres.",
"Monstres_Élites": "Monstres élites : localisation et butin.",
"Lexique": "Lexique des termes et abréviations du jeu.",
"Titre": "Système de titres et leurs bonus.",
"Liste_des_Titres": "Liste des titres obtenables et conditions.",
"Votre_Première_Quête_I": "Première quête du tutoriel (partie 1).",
# guides-fr
"index.html": "Accueil du site de guides : présentation, historique des mises à jour.",
"classes.html": "Sommaire classes : 61 classes (normales + éveillées), passifs raciaux, prérequis, tableau complet.",
"equipements.html": "Sommaire équipements : armes éveillées, armures éveillées 70/80/95, costumes, montures, familiers.",
"donjons-bestiaire.html": "Sommaire donjons : donjons par tranche de niveau, héroïques, instances journalières, zones de combat, PVP.",
"artisanat.html": "Sommaire artisanat : les 5 artisanats raciaux, outils de récolte, craft.",
"v-monde-dezur-et-ressources.html": "Sommaire monde : géographie d'Ezur, monnaies, cristaux d'Asbi, gloires d'Aven.",
"vi-patchnotes-vgn.html": "Sommaire des patchnotes traduites du serveur privé Vendetta (VGN).",
"2017/05/29/prerequis-des-classes.html": "Prérequis de déblocage de toutes les classes normales (niveaux et quêtes).",
"2017/07/19/certificats-de-classe.html": "Certificats de classe : statistiques et emplacement par classe.",
"2017/05/29/combinaisons-des-certificats-de-classe.html": "Combinaisons optimales de certificats de classe.",
"2017/05/29/expertise-de-classe.html": "Points d'expertise de classe à répartir : effets par branche.",
"2017/05/29/les-sorts-niv-72.html": "Compétences de classe niveau 72 : obtention (insignes de combat) et effets.",
"2017/06/09/le-craft.html": "Le craft : principe, PNJ, qualités de production.",
"2017/06/09/compos-de-craft.html": "Composants de craft : listes par type et par niveau.",
"2017/06/06/guilde-et-ville-de-guile.html": "Créer et gérer une guilde et sa ville : bâtiments, améliorations.",
"2017/07/25/elimination-de-guilde.html": "Tournoi d'élimination de guilde (PVP) : règles et récompenses.",
"2017/06/08/guerres-de-territoire.html": "Guerres de territoire : PVP de guildes pour conquérir un territoire.",
"2017/06/12/zones-de-combat.html": "Zones de combat (Labo Vingot, Dimension Dévastée, Bataille Spatio-temporelle, Dimension of Soul) : trophées et combinaisons. Dimension of Soul = Vendetta (niv. 90).",
"2017/06/07/la-tour-des-cieux.html": "Tour des Cieux : donjon journalier de 50 étages à Aven.",
"2017/06/07/le-laboratoire-de-reno.html": "Laboratoire de Réno : instance journalière de Meeryasha.",
"2017/07/04/purgatoire.html": "Purgatoire : instance 5 joueurs d'Aven (quête niv. 80, titre Chevalier Templier).",
"2017/06/07/arene-du-temple-antique-du-chaos.html": "Arène du Temple antique du chaos : instance 5v5 PVE/PVP (niv. 80).",
"2017/07/17/les-echecs.html": "Les Échecs : instance d'échecs grandeur nature en tour-par-tour.",
"2017/09/01/expedition-anti-dragon-autel-volcanique.html": "Expédition anti-dragon : instance de masse (25 joueurs) contre Borsey, dès le niv. 70 ; version Vendetta diffère.",
"2017/06/09/systeme-de-couple-love-co.html": "Système de couple : rencontres, buffs et récompenses.",
"2017/06/01/familiers.html": "Familiers : obtention, races et compétences.",
"2017/07/03/montures-disponibles-en-jeu.html": "Toutes les montures du jeu : stats et obtention.",
"2017/06/08/monnaies-et-devises-dezur.html": "Monnaies d'Ezur : PO, PA, médailles de cristal, pièces d'Eternal…",
"2017/06/08/geographie-du-monde-dezur.html": "Géographie d'Ezur : continents et régions par niveau.",
"2017/06/08/gloires-daven.html": "Gloire dans les associations d'Aven : compos de craft et équipements de gloire.",
"2017/06/12/cristaux-dasbi.html": "Cristaux d'Asbi : localisation des coffres et échanges au Centre de recherche.",
"2017/06/07/monstres-mutants.html": "Les 6 monstres mutants : worldboss itinérants du continent central.",
"2017/06/07/taen-et-les-pirates-daven.html": "Invasion de Taen et ses pirates à Aven (event périodique).",
"2017/06/07/meteores-etoiles-et-spheres.html": "Météores, étoiles et sphères : boosters de stats (quêtes, succès).",
"2017/06/07/gemmes-et-cartes-de-metamorphose.html": "Gemmes et cartes de métamorphose : transformations à bonus.",
"2017/05/31/infusion-de-mana.html": "Infusion de mana : amélioration d'équipement par le mana.",
"2017/06/01/succes-rune-vers-les-etoiles.html": "Succès 'Rune vers les étoiles' : enchantements 5-7 et spéciaux, localisation des formules.",
"2017/06/09/armes-eveillees-75-2.html": "Armes éveillées niveau 75 : quêtes et matériaux.",
"2017/05/31/armures-eveillees-lvl-70.html": "Sets d'armures éveillées niveau 70 : pièces et obtention.",
"2017/05/31/armures-eveillees-lvl-80.html": "Sets d'armures éveillées niveau 80 : pièces et obtention.",
"2017/07/04/costumes.html": "Costumes / fashions disponibles.",
"2017/06/09/la-boite-mystique.html": "Boîte mystique : contenu et taux.",
"2017/06/08/le-chemin-de-la-destinee.html": "Chemin de la destinée : jeu de l'oie à récompenses.",
"2017/06/06/systeme-de-villa.html": "Villa personnelle : création et banquets à buffs.",
"2017/05/30/donjons-lvl-1-a-30-de-carriere-angor-a-vilrequin.html": "Donjons niv. 1-30 : Carrière Angor → Vilrequin (boss, loots).",
"2017/05/30/donjons-lvl-31-a-50-de-racine-branda-a-crypticone.html": "Donjons niv. 31-50 : Racine Branda → Crypticône.",
"2017/05/30/donjons-lvl-51-a-60-du-temple-de-delphes-a-frontiere-de-basel.html": "Donjons niv. 51-60 : Temple de Delphes → Frontière de Basel.",
"2017/05/30/donjons-lvl-61-a-70-de-cratere-a-abysse-de-siecle.html": "Donjons niv. 61-70 : Cratère → Abysse de Siècle.",
"2017/05/30/donjons-lvl-71-a-80-de-la-grotte-non-repertoriee-a-iyernafalo.html": "Donjons niv. 71-80 : Grotte non-répertoriée → Iyernafalo.",
"2017/05/30/donjons-lvl-81-a-ω-de-kamodo-au-temple-enchante.html": "Donjons niv. 81+ : Kamodo → Decayed Hallows (contient du contenu > 90 / Vendetta).",
"2017/05/30/donjons-lvl-81-a-ω-de-kamodo-au-temple-enchante_8b58c2.html": "Donjons niv. 81+ : Kamodo → Decayed Hallows (doublon de capture ; contenu > 90 / Vendetta).",
"2017/05/30/donjons-des-cinq-animaux.html": "Donjons des Cinq Animaux : instances journalières thématiques.",
"2017/05/31/donjons-dasymo.html": "Donjons d'Asymo : 5 instances journalières d'Aven (Garde du cimetière…).",
"2017/05/29/boss-de-guilde-world-boss.html": "Worldboss : boss de monde et boss de guilde, localisation et loot.",
"2017/05/29/traits-heroiques.html": "Traits héroïques : bonus de création de personnage.",
"2017/07/20/le-messie-mimic.html": "Quête de déblocage du Messie (niv. 60).",
"2017/10/18/la-quete-du-sage.html": "Quêtes de déblocage du Sage (niv. 69-70).",
"2017/05/31/aventures-de-la-conteuse-kimy.html": "Quêtes journalières de la conteuse Kimy.",
"2017/07/19/secourir-les-citoyens-du-roi-alpaga.html": "Quête 'Secourir les sujets du Roi Alpaga'.",
"2017/05/31/gemmes-de-la-tour-des-cieux.html": "Gemmes obtenables à la Tour des Cieux.",
"2017/05/31/gemmes-du-labo-de-reno.html": "Gemmes obtenables au Laboratoire de Réno.",
"2017/06/16/trophees-des-donjons-regionaux.html": "Trophées des donjons régionaux : liste par donjon.",
"2017/06/16/trophees-des-donjons-heroiques.html": "Trophées des donjons héroïques.",
"2017/06/16/trophees-de-gloire-regionale.html": "Trophées de gloire régionale : échange contre la gloire d'association.",
}


def clean_summary(s: str) -> str:
    s = re.sub(r"!\[[^\]]*\]\([^)]*\)", "", s)          # images
    s = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", s)      # liens
    s = re.sub(r"[*_#>`|]", "", s)
    s = re.sub(r"\s+", " ", s).strip(" -–—\t")
    return s[:160] + ("…" if len(s) > 160 else "")


def build(site, title, intro, hors_note):
    recs = [r for r in DATA if r["site"] == site]
    by_theme = {k: [] for k, _ in THEMES}
    hors = []
    for r in recs:
        t = (W3_THEME.get(Path(r["file"]).stem, "divers")
             if site == "wiki-fr" else
             r.get("theme") or guides_theme(r["file"]))
        if t in ("archives", "attachments"):
            by_theme[t].append(r)
            continue
        flag = ""
        if site == "guides-fr" and (HORS.search(r["file"]) or
                                  HORS.search(r["title"])):
            flag = " ⚠️ **hors périmètre**"
            hors.append(r)
        elif r["file"] in PARTIEL:
            flag = " ⚠️ *partiellement hors périmètre*"
            hors.append(r)
        r["_flag"] = flag
        r["_theme"] = t
        by_theme[t].append(r)

    lines = [f"# {title}\n", intro, ""]
    for key, label in THEMES:
        items = by_theme.get(key, [])
        if not items:
            continue
        lines.append(f"\n## {label}\n")
        lines.append("| Page | Fichier source | Résumé |")
        lines.append("|---|---|---|")
        for r in sorted(items, key=lambda x: x["title"].lower()):
            md = r.get("md")
            name = f"[{r['title']}]({md})" if md else r["title"]
            src = f"`{r['file']}`"
            if key == "attachments" and r.get("image_url"):
                from urllib.parse import urlparse
                import posixpath
                base = posixpath.basename(urlparse(r["image_url"]).path)
                summ = f"Image téléchargée : [images/{base}](images/{base})"
            else:
                summ = CURATED.get(r["file"],
                                   clean_summary(r.get("summary", "")))
            lines.append(f"| {name} | {src} | {summ}{r.get('_flag','')} |")
    # section hors périmètre
    if hors:
        lines.append("\n## Contenu hors périmètre (niveau > 90 / exclusif Vendetta)\n")
        lines.append(hors_note + "\n")
        for r in hors:
            md = r.get("md")
            name = f"[{r['title']}]({md})" if md else r["title"]
            lines.append(f"- {name} (`{r['file']}`)")
    return "\n".join(lines) + "\n"


intro_w3 = (
    "Index des pages du **wiki fandom français** d'Eden Eternal"
    " (`raw/wiki-fr/`, 243 pages HTML, ère ~2012-2017).\n\n"
    "Chaque page a été convertie en markdown dans `pages/` (texte,"
    " tableaux et listes conservés ; navigation et pied de page"
    " supprimés). Les images restent des URL distantes"
    " (`static.wikia.nocookie.net` — non téléchargées). Les liens"
    " internes `/fr/wiki/X` pointent vers les `.md` locaux quand la"
    " page existe dans le dump, sinon vers eden-eternal.fandom.com.\n\n"
    "**Valeur principale** : les **noms FR officiels** (zones, donjons,"
    " boss, PNJ, quêtes, items, sorts) pour l'i18n — plus des tables"
    " uniques : `Classes`/`Tableau_des_classes` (prérequis par classe),"
    " `Les_Certificats` (combos), `Comparatif_des_compétences_raciales`"
    " + `Sorts_de_race_(par_classe)` (20 classes × 5 races),"
    " `Boss_de_Donjons` (~80 boss avec faiblesses), `Quêtes` (chapitres"
    " + succès FR + armes éveillées 65 / sets éveillés 70),"
    " `Quêtes_de_guilde` (~50 quêtes), `Montures` (liste complète par"
    " famille), `Géographie` (régions par niveau), `Ville_de_Guilde`,"
    " `Arène`, `Familiers`, `Décrypteur_de_Glyphes`, `Tissage_(Ezelins)`,"
    " `Trophées_obtenus_par_les_Boss_de_guildes`, la série didactique"
    " des Monts Calcaires (~15 quêtes chaînées avec dialogues) et ~20"
    " quêtes Hautes-terres (56-60).\n\n"
    "**Lacunes** : wiki arrêté vers 2017 — couvre ≤70-80, **rien niv."
    " 76-90** ; tables de trophées 30-60 quasi vides ; élites rares non"
    " listés après Baie Bélouga ; sorts raciaux Ézelin absents ;"
    " plusieurs pages stubs/doublons et pages mal rangées (montures"
    " classées monstres, quêtes classées équipements) ; noms FR"
    " instables (Champignoïde/Champinoïde, Cristaux Asbi/Asbee…).\n\n"
    "**Périmètre serveur** : notre serveur réplique l'Eden de base"
    " (contenu jusqu'au niveau 90). Ce wiki n'a **aucun contenu >90**"
    " (arrêté avant) — le seul piège est de ne pas confondre les"
    " armes/sets éveillés 65-80 (contenu de base, dans périmètre) avec"
    " l'éveil 95+ de Vendetta.")
intro_g = (
    "Index des **guides français** du site WordPress"
    " edenfr.wordpress.com (`raw/guides-fr/`, articles 2015-2018 +"
    " pages sommaires). Site communautaire **Vendetta** : les guides"
    " mélangent jeu de base et contenu custom dans une même page —"
    " chaque ligne est marquée quand c'est le cas.\n\n"
    "Chaque article a été converti en markdown dans `pages/`. Les"
    " images des pages d'attachement (`wp-content/`) ont été"
    " téléchargées dans `images/` et les liens qui y menaient pointent"
    " vers ces copies locales (`../images/<fichier>`). Les autres"
    " images restent des URL distantes — motifs :"
    " `edenfr.wordpress.com/wp-content/uploads/…` (souvent relayé par"
    " le proxy `i0/i1/i2.wp.com`), captures externes `farm*.staticflickr.com`,"
    " `puu.sh`, `vendettagn.com`.\n\n"
    "**Dans le périmètre (≤90)** : donjons régionaux 1-80 + héroïques"
    " HQ50-90 (runs détaillés), classes normales (prérequis,"
    " certificats, expertise, sorts 72), éveil armes 65/75 + armures"
    " 70/80, artisanat racial 5 races, monde d'Ezur (Aven, gloires,"
    " cristaux d'Asbi, world boss), systèmes (couple, familiers,"
    " villa, boîte mystique, météores, métamorphoses), PvP (territoire,"
    " élimination), monnaies, montures, costumes.\n\n"
    "**Périmètre serveur** : notre serveur réplique l'Eden de base"
    " (contenu jusqu'au niveau 90). Tout le contenu **niveau 95+** et"
    " les exclusivités du serveur privé **Vendetta** (classes Dragon"
    " Soul/Emperor, Luminary, Mimic, 4Trial, Gates of Pandemonium,"
    " HQ 95/100, armures éveillées 95, patchs VGN…) sont **hors"
    " périmètre** — voir la section dédiée en bas. Pages **mixtes**"
    " (sections ≤90 + sections >90) marquées « partiellement hors"
    " périmètre » — seules les parties haut niveau sont à écarter.")
hors_note = ("Ces pages documentent du contenu au-delà du niveau 90 ou"
             " exclusif au serveur privé Vendetta. Elles ne s'appliquent"
             " pas à notre réplique mais restent archivées ici pour"
             " référence (équilibrage, contenu endgame, mécaniques"
             " custom). Le détail complet figure dans la section lvl95+"
             " du `../README.md`.")

(ROOT / "wiki-fr/INDEX.md").write_text(
    build("wiki-fr", "Wiki Eden Eternal (fandom FR) — Index", intro_w3,
          hors_note), encoding="utf-8")
(ROOT / "guides-fr/INDEX.md").write_text(
    build("guides-fr", "Guides Eden Eternal (edenfr.wordpress.com) — Index",
          intro_g, hors_note), encoding="utf-8")
print("INDEX.md generes")
