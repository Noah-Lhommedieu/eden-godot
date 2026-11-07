#!/usr/bin/env python3
"""Génère les COMMENT ON COLUMN des colonnes non commentées (PLAN-ACTION-3, DB-C).

1) un dictionnaire de motifs (nom de colonne → description) couvre les colonnes dont le sens est établi ;
2) sinon, repli explicite « sens natif non documenté » : le lint compte ces replis comme dette à résorber.
Usage : tools/db/doc-columns.py --out eden_server/Database/033-column-comments.sql
"""
import argparse, re, subprocess, sys

def psql(sql):
    r = subprocess.run(["docker", "exec", "edengodot-postgres", "psql", "-U", "postgres", "-d", "eden_live", "-At", "-F", "\t", "-c", sql], capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.strip().split("\n") if l]

# (regex sur le nom, description) ; \1 = premier groupe
RULES = [
 (r"^id$", "Identifiant de la ligne (clé primaire)."),
 (r"^player_id$|^char_id$|^character_id$", "Personnage concerné (character.player_characters.id)."),
 (r"^account_id$", "Compte concerné (account.accounts.id)."),
 (r"^account_name$|^username$", "Nom de compte de connexion."),
 (r"^family_id$|^fm_id$", "Famille (guilde) concernée (guild.family.id)."),
 (r"^item_id$|^itemid$", "Identifiant natif de l'objet (item.item.item_id dans eden_content)."),
 (r"^item_serial$|^serial$", "Numéro de série unique de l'exemplaire d'objet (séquence system.item_serial)."),
 (r"^player_name$|^given_name$|^name$", "Nom affiché."),
 (r"^create_time$|^created_at$|^create_date$", "Date de création (secondes Unix)."),
 (r"^due_date$", "Date d'échéance ou de fin (secondes Unix)."),
 (r"^durability$", "Durabilité actuelle de l'objet."),
 (r"^cur_maxdurability$", "Durabilité maximale actuelle de l'objet (diminue à chaque réparation)."),
 (r"^rank$", "Rang dans le classement ou dans la liste."),
 (r"^bind$|^unbind$", "État de liaison de l'objet au personnage (bind) ou temps restant avant déliaison (unbind), valeur native."),
 (r"^extra_combo_id(\d+)$", "Identifiant d'enchant bonus n°\\1 posé par combinaison sur l'objet (0 = vide)."),
 (r"^combo_id$", "Identifiant de combinaison (combo) appliquée à l'objet."),
 (r"^embedded_id(\d+)$", "Identifiant de la gemme sertie dans l'emplacement n°\\1 (0 = vide)."),
 (r"^embedded_amount$", "Nombre de gemmes serties dans l'objet."),
 (r"^train_slot(\d+)_type$", "Type d'entraînement de l'emplacement n°\\1 de l'équipement."),
 (r"^train_slot(\d+)_level$", "Niveau d'entraînement de l'emplacement n°\\1 de l'équipement."),
 (r"^train_level$", "Niveau d'entraînement de l'équipement."),
 (r"^train_exp$", "EXP d'entraînement accumulée par l'équipement."),
 (r"^magicbox_enchant(\d+)$", "Identifiant de l'enchant n°\\1 de la boîte magique de l'objet."),
 (r"^magicbox_starlevel$", "Niveau d'étoiles de la boîte magique de l'objet."),
 (r"^magicbox_energy$", "Énergie stockée dans la boîte magique de l'objet."),
 (r"^witchcraft$", "Niveau de sorcellerie (enchantement caché) de l'objet."),
 (r"^magic$", "Valeur de magie (enchantement) de l'objet."),
 (r"^quality$", "Qualité de l'objet (couleur native)."),
 (r"^strengthen$", "Niveau de renforcement de l'objet (+N)."),
 (r"^plus_parameter$", "Paramètre bonus (+) de l'objet issu du renforcement."),
 (r"^color$", "Teinte de l'objet (code couleur natif)."),
 (r"^gold_enchant$", "Niveau d'enchantement doré de l'objet."),
 (r"^identify$", "État d'identification de l'objet (inconnu / identifié)."),
 (r"^maker$", "Nom du fabricant de l'objet."),
 (r"^container_index$", "Numéro du conteneur (sac, entrepôt ou plateau) où se trouve la ligne."),
 (r"^loc$|^location$", "Emplacement natif dans le conteneur (position ou case)."),
 (r"^slot$", "Numéro de case dans le sac."),
 (r"^point$|^points$", "Points (valeur cumulée, unité selon la table)."),
 (r"^level$|^lv$", "Niveau."),
 (r"^exp$|^experience$", "Points d'expérience accumulés."),
 (r"^state$|^status$", "État natif de la ligne (valeur énumérée du serveur d'origine)."),
 (r"^gold$|^money$", "Quantité d'or (pièces) détenue."),
 (r"^tid$", "Identifiant de modèle (template) natif de l'élément."),
 (r"^type$|^kind$", "Type natif de l'élément."),
 (r"^count$|^amount$|^quantity$|^num$", "Quantité."),
 (r"^node_id$", "Carte (node) concernée (map.map.node_id dans eden_content)."),
 (r"^x$|^pos_x$", "Position en X dans la carte."),
 (r"^y$|^pos_y$", "Position en Y (hauteur) dans la carte."),
 (r"^z$|^pos_z$", "Position en Z dans la carte."),
 (r"^class_id$", "Classe concernée (identifiant natif de classe)."),
 (r"^message$|^msg$|^content$", "Texte du message."),
 (r"^title$", "Titre."),
 (r"^sender$|^from_name$", "Expéditeur."),
 (r"^receiver$|^to_name$", "Destinataire."),
 (r"^password$|^password_hash$", "Mot de passe haché (jamais en clair)."),
]
CT = [(re.compile(p), d) for p, d in RULES]
TOKENS = {"hp": "points de vie", "mp": "points de mana", "max": "maximum", "id": "identifiant", "time": "heure/durée", "date": "date", "count": "compteur",
 "num": "nombre", "name": "nom", "gender": "genre", "hair": "coiffure", "face": "visage", "skin": "peau", "color": "couleur", "privilege": "privilège",
 "rate": "taux", "probability": "probabilité", "price": "prix", "item": "objet", "family": "famille", "leader": "chef", "member": "membre", "mail": "courrier",
 "sender": "expéditeur", "receiver": "destinataire", "achievement": "succès", "mission": "quête", "wins": "victoires", "kills": "éliminations", "score": "score",
 "round": "tour", "week": "semaine", "mood": "humeur", "elf": "elfe", "spell": "sort", "grow": "croissance", "attack": "attaque", "hit": "précision",
 "critical": "critique", "speed": "vitesse", "template": "modèle", "player": "joueur", "lv": "niveau", "level": "niveau", "class": "classe", "point": "points",
 "gold": "or", "crystal": "cristal", "log": "journal", "message": "message", "title": "titre", "emblem": "emblème", "ip": "adresse IP", "port": "port",
 "online": "en ligne", "user": "joueurs", "version": "version", "order": "ordre", "show": "affichage", "write": "écriture", "start": "début", "end": "fin",
 "last": "dernier", "first": "premier", "login": "connexion", "reset": "remise à zéro", "bonus": "bonus", "note": "note", "remark": "remarque",
 "effect": "effet", "theme": "thème", "type": "type", "index": "rang", "amount": "quantité", "opened": "ouvert", "returned": "renvoyé", "cod": "contre-remboursement",
 "stack": "cumul", "caster": "lanceur", "rest": "restant", "factor": "facteur", "partner": "partenaire", "initiator": "initiateur", "seller": "vendeur",
 "duedate": "échéance", "quality": "qualité", "filter": "filtre", "alignment": "alignement", "territory": "territoire", "own": "propriétaire", "times": "fois",
 "battle": "combat", "integrate": "fusion", "mix": "mélange", "star": "étoiles", "tag": "étiquette", "flags": "drapeaux", "flag": "drapeau", "quit": "départ",
 "revive": "résurrection", "area": "zone", "face": "visage", "dir": "direction", "race": "race", "skill": "compétence", "combo": "combo", "bind": "liaison",
 "closed": "fermé", "trigger": "déclencheur", "trace": "suivi", "complete": "terminé", "target": "cible", "finished": "terminé", "tracing": "suivi actif",
 "sign": "signature", "region": "région", "like": "aime", "hate": "déteste", "public": "public", "introduct": "présentation", "birth": "naissance", "month": "mois",
 "day": "jour", "blood": "groupe sanguin", "claimed": "réclamé", "at": "le", "perk": "avantage", "code": "code", "description": "description", "required": "requis",
 "value": "valeur", "kind": "genre", "enabled": "activé", "sort": "tri", "cost": "coût", "honor": "honneur", "phase": "phase", "energy": "énergie", "update": "mise à jour",
 "total": "total", "collection": "collection", "monster": "monstre", "kill": "éliminations", "instance": "instance", "node": "carte", "join": "participations",
 "win": "victoires", "lose": "défaites", "mvp": "MVP", "high": "record", "hight": "record", "die": "morts", "damage": "dégâts", "altar": "autel", "cure": "soins",
 "weight": "pondération", "equip": "équipement", "head": "tête", "chest": "torse", "pants": "pantalon", "glove": "gants", "feet": "pieds", "back": "dos", "right": "main droite",
 "shoot": "tir", "weapon": "arme", "active": "actif", "hotel": "hôtel", "friend": "ami", "privilege": "privilège", "lover": "amoureux", "other": "autres", "populity": "popularité",
 "extend": "extension", "wallpaper": "papier peint", "living": "salon", "floor": "sol", "bed": "lit", "party": "fête", "address": "adresse", "int": "interne", "ext": "externe",
 "world": "monde", "schema": "schéma", "pwd": "mot de passe", "authority": "autorité", "regdate": "date d'inscription", "email": "courriel", "admin": "administrateur",
 "bulletin": "annonce", "set": "ensemble", "sequence": "séquence", "embedded": "serti", "counter": "compteur", "str": "texte", "para": "paramètre", "uniform": "uniforme",
 "info": "informations", "invite": "invitation", "recruit": "recrutement", "delete": "suppression", "cadre": "cadre", "exploit": "exploits", "group": "groupe",
 "alone": "seul", "saving": "sauvegarde", "current": "actuel", "bonus": "bonus", "gettime": "date d'obtention", "event": "événement", "extra": "supplémentaire", "plus": "bonus",
 "lottery": "loterie", "update": "mise à jour", "multiple": "multiplicateur", "limit": "limite", "sell": "vente", "recognized": "reconnu", "percentage": "pourcentage",
 "unit": "unité", "money": "monnaie", "special": "spécial", "sale": "vente", "cant": "interdit", "gonext": "passage au suivant", "mem": "membre", "evaluate": "évaluation",
 "size": "taille", "coin": "pièce", "number": "numéro", "frame": "cadre", "card": "carte", "slot": "emplacement"}
def humanize(col):
    parts = [t for t in col.split("_") if t]
    words = []
    for t in parts:
        if t.isdigit(): words.append(t); continue
        base = re.sub(r"\d+$", "", t)
        if base in TOKENS: words.append(TOKENS[base] + (t[len(base):] if t != base else ""))
        else: return None
    return " ".join(words)

ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args()
rows = psql("""select n.nspname, c.relname, at.attname, format_type(at.atttypid, at.atttypmod), coalesce(obj_description(c.oid),'')
  from pg_attribute at join pg_class c on c.oid=at.attrelid join pg_namespace n on n.oid=c.relnamespace
  where c.relkind='r' and at.attnum>0 and not at.attisdropped
    and n.nspname in ('character','inventory','market','guild','social','progress','ranking','lottery','system','admin','account','member')
    and (col_description(c.oid, at.attnum) is null or col_description(c.oid, at.attnum) like '%non documenté%') order by 1,2,at.attnum""")
out, documented, fallback = [], 0, 0
for sch, tab, col, typ, tdesc in rows:
    text = None
    for rx, d in CT:
        m = rx.match(col)
        if m: text = rx.sub(d, col) if "\\1" in d else d; break
    if text is None and (h := humanize(col)) is not None:
        text = f"{h[0].upper()+h[1:]} (déduit du nom de la colonne « {col} » ; {typ})."; documented += 1
    elif text is None:
        fallback += 1
        text = f"Colonne native « {col} » ({typ}) de {sch}.{tab} ; sens natif non documenté (valeur brute du serveur d'origine)."
    elif text is not None and not text.startswith("Colonne native") and "déduit du nom" not in text:
        documented += 1
    out.append(f"COMMENT ON COLUMN {sch}.{tab}.{col} IS '{text.replace(chr(39), chr(39)*2)}';")
open(a.out, "w", encoding="utf-8").write("-- 033 : commentaires de colonnes (générés par tools/db/doc-columns.py).\n-- Les colonnes « sens natif non documenté » sont de la dette à résorber (comptée par tools/db/doc-lint.py).\nBEGIN;\n" + "\n".join(out) + "\nCOMMIT;\n")
print(f"{len(out)} commentaires : {documented} décrites, {fallback} en repli")
