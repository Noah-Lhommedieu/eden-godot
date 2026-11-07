-- eden_content / 001 — base STATIQUE du jeu (PLAN-ACTION-3 B0). Tables typées, une colonne par champ en clair.
-- Écrite par le seed (tools/Eden.ContentSeed) puis par l'éditeur admin ; lue par tous. Pas de clé étrangère
-- vers eden_live : eden_live référence ce contenu par identifiant métier (item_id…).
-- Convention : snake_case, singulier, suffixes _id / _percent / _seconds / _silver ; chaque table et colonne commentée.
BEGIN;
CREATE SCHEMA IF NOT EXISTS item;     COMMENT ON SCHEMA item     IS 'Objets du jeu (équipements, consommables, matériaux, plans).';
CREATE SCHEMA IF NOT EXISTS creature; COMMENT ON SCHEMA creature IS 'Monstres et leurs caractéristiques de base.';
CREATE SCHEMA IF NOT EXISTS npc;      COMMENT ON SCHEMA npc      IS 'Personnages non joueurs.';
CREATE SCHEMA IF NOT EXISTS quest;    COMMENT ON SCHEMA quest    IS 'Quêtes (missions).';
CREATE SCHEMA IF NOT EXISTS map;      COMMENT ON SCHEMA map      IS 'Cartes (nodes) et leur type.';
CREATE SCHEMA IF NOT EXISTS history;  COMMENT ON SCHEMA history  IS 'Journal des éditions faites depuis le panneau admin, et traces des imports (seed).';

CREATE TABLE item.item (
    item_id          integer PRIMARY KEY,
    name_fr          text    NOT NULL,
    item_kind        integer NOT NULL,
    required_level   integer NOT NULL,
    max_level        integer NOT NULL,
    buy_price_silver integer NOT NULL,
    icon_code        text    NOT NULL,
    in_scope         boolean NOT NULL
);
COMMENT ON TABLE  item.item IS 'Un objet du jeu (source : S_Item/c_item + t_item FR).';
COMMENT ON COLUMN item.item.item_id IS 'Identifiant natif de l''objet (clé métier stable, utilisée par eden_live).';
COMMENT ON COLUMN item.item.name_fr IS 'Nom en français (t_item.ini, UTF-8).';
COMMENT ON COLUMN item.item.item_kind IS 'Genre natif (c_item col 8) : 50-52 plans de récolte, 53-56 formules de fabrication, etc.';
COMMENT ON COLUMN item.item.required_level IS 'Niveau requis pour utiliser l''objet (0 = aucun).';
COMMENT ON COLUMN item.item.max_level IS 'Niveau maximal d''utilisation (0 = aucun plafond).';
COMMENT ON COLUMN item.item.buy_price_silver IS 'Prix d''achat chez un marchand, en pièces d''argent.';
COMMENT ON COLUMN item.item.icon_code IS 'Code de l''icône (ex. UI012), clé vers les textures du client.';
COMMENT ON COLUMN item.item.in_scope IS 'Présent en jeu et niveau ≤ 90 (règle C3, content/rules/scope.json) : seule définition du périmètre du wiki, de l''admin et de l''anglais.';

CREATE TABLE creature.monster (
    monster_id   integer PRIMARY KEY,
    name_fr      text    NOT NULL,
    level        integer NOT NULL,
    monster_type integer NOT NULL,
    family       integer NOT NULL,
    hp           bigint  NOT NULL,
    mp           bigint  NOT NULL,
    exp_reward   bigint  NOT NULL,
    in_scope     boolean NOT NULL
);
COMMENT ON TABLE  creature.monster IS 'Un monstre (source : S_Monster + t_monster FR).';
COMMENT ON COLUMN creature.monster.monster_id IS 'Identifiant natif du monstre (clé métier stable).';
COMMENT ON COLUMN creature.monster.name_fr IS 'Nom en français (t_monster.ini).';
COMMENT ON COLUMN creature.monster.level IS 'Niveau du monstre.';
COMMENT ON COLUMN creature.monster.monster_type IS 'Type natif : 0 normal, 1 spécial, ≥2 élite ou boss.';
COMMENT ON COLUMN creature.monster.family IS 'Famille de monstre (regroupement natif).';
COMMENT ON COLUMN creature.monster.hp IS 'Points de vie de base.';
COMMENT ON COLUMN creature.monster.mp IS 'Points de mana de base.';
COMMENT ON COLUMN creature.monster.exp_reward IS 'EXP personnage donnée à la mort (avant taux serveur, VIP et charmes).';
COMMENT ON COLUMN creature.monster.in_scope IS 'Présent en jeu et niveau ≤ 90 (règle C3).';

CREATE TABLE npc.npc (
    npc_id   integer PRIMARY KEY,
    name_fr  text    NOT NULL,
    npc_type integer NOT NULL,
    level    integer NOT NULL,
    role     text    NOT NULL,
    in_scope boolean NOT NULL
);
COMMENT ON TABLE  npc.npc IS 'Un PNJ (source : S_NPC + t_npc FR).';
COMMENT ON COLUMN npc.npc.npc_id IS 'Identifiant natif du PNJ (clé métier stable).';
COMMENT ON COLUMN npc.npc.name_fr IS 'Titre et nom en français (t_npc.ini), séparés par un espace.';
COMMENT ON COLUMN npc.npc.npc_type IS 'Type natif du PNJ.';
COMMENT ON COLUMN npc.npc.level IS 'Niveau affiché du PNJ.';
COMMENT ON COLUMN npc.npc.role IS 'Rôle natif en clair (ex. quête, boutique) ; chaîne vide si non renseigné.';
COMMENT ON COLUMN npc.npc.in_scope IS 'Posé dans une carte comptée par la règle C3.';

CREATE TABLE quest.quest (
    quest_id     integer PRIMARY KEY,
    name_fr      text    NOT NULL,
    script_name  text    NOT NULL,
    min_level    integer NOT NULL,
    category     text    NOT NULL,
    is_daily     boolean NOT NULL,
    in_scope     boolean NOT NULL
);
COMMENT ON TABLE  quest.quest IS 'Une quête (source : S_Mission + t_mission FR).';
COMMENT ON COLUMN quest.quest.quest_id IS 'Identifiant natif de la quête (clé métier stable).';
COMMENT ON COLUMN quest.quest.name_fr IS 'Titre en français (t_mission.ini).';
COMMENT ON COLUMN quest.quest.script_name IS 'Nom du script natif de la quête.';
COMMENT ON COLUMN quest.quest.min_level IS 'Niveau minimal pour accepter la quête (If_CharLevel >= N, 0 si aucun).';
COMMENT ON COLUMN quest.quest.category IS 'Catégorie native de la quête.';
COMMENT ON COLUMN quest.quest.is_daily IS 'Quête journalière (remise à zéro chaque jour de reset 06:00).';
COMMENT ON COLUMN quest.quest.in_scope IS 'Niveau ≤ 90 (règle C3).';

CREATE TABLE map.map (
    node_id   integer PRIMARY KEY,
    name_fr   text    NOT NULL,
    node_type integer NOT NULL,
    map_code  text    NOT NULL,
    min_level integer NOT NULL,
    map_kind  text    NOT NULL,
    in_scope  boolean NOT NULL
);
COMMENT ON TABLE  map.map IS 'Une carte (node) du jeu (source : S_Node + t_node FR).';
COMMENT ON COLUMN map.map.node_id IS 'Identifiant natif de la carte (clé métier stable).';
COMMENT ON COLUMN map.map.name_fr IS 'Nom en français (t_node.ini).';
COMMENT ON COLUMN map.map.node_type IS 'Type natif (c_node col 3) : 1 monde, 4 ville de guilde, 5/7 donjon, 11 instance partagée, 18 épreuve…';
COMMENT ON COLUMN map.map.map_code IS 'Code de scène (ex. S001).';
COMMENT ON COLUMN map.map.min_level IS 'Niveau minimal d''entrée.';
COMMENT ON COLUMN map.map.map_kind IS 'Type de carte calculé par MapClassifier : field, shared_instance, dungeon, special_mode, trial, guild_city.';
COMMENT ON COLUMN map.map.in_scope IS 'Carte accessible et niveau d''entrée ≤ 90 (règle C3).';

CREATE TABLE history.change (
    change_id         bigserial PRIMARY KEY,
    entity_type       text        NOT NULL,
    entity_id         integer     NOT NULL,
    entity_name       text        NOT NULL,
    field             text        NOT NULL,
    old_value         text,
    new_value         text,
    edited_by_account text        NOT NULL,
    edited_at         timestamptz NOT NULL DEFAULT now(),
    reason            text,
    release_id        text,
    reverted_by       bigint
);
COMMENT ON TABLE  history.change IS 'Journal des modifications faites depuis le panneau admin (pas des imports) : voir, annuler, purger.';
COMMENT ON COLUMN history.change.change_id IS 'Numéro de la modification.';
COMMENT ON COLUMN history.change.entity_type IS 'Type de l''entité modifiée (item, monster, npc, quest, map…).';
COMMENT ON COLUMN history.change.entity_id IS 'Identifiant métier de l''entité modifiée.';
COMMENT ON COLUMN history.change.entity_name IS 'Nom lisible de l''entité au moment de la modification.';
COMMENT ON COLUMN history.change.field IS 'Colonne modifiée.';
COMMENT ON COLUMN history.change.old_value IS 'Valeur avant (texte), NULL si la ligne vient d''être créée.';
COMMENT ON COLUMN history.change.new_value IS 'Valeur après (texte), NULL si la ligne est supprimée.';
COMMENT ON COLUMN history.change.edited_by_account IS 'Compte administrateur auteur (l''administration est par COMPTE).';
COMMENT ON COLUMN history.change.edited_at IS 'Date et heure de la modification.';
COMMENT ON COLUMN history.change.reason IS 'Motif saisi par l''administrateur.';
COMMENT ON COLUMN history.change.release_id IS 'Publication de contenu qui a embarqué cette modification.';
COMMENT ON COLUMN history.change.reverted_by IS 'change_id de la modification inverse qui l''a annulée, NULL sinon.';

CREATE TABLE history.seed_run (
    table_name text        NOT NULL,
    row_count  integer     NOT NULL,
    sha256     text        NOT NULL,
    seeded_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (table_name, seeded_at)
);
COMMENT ON TABLE  history.seed_run IS 'Trace de chaque import (seed) : un import rejoué sur les mêmes sources donne les mêmes empreintes.';
COMMENT ON COLUMN history.seed_run.table_name IS 'Table importée (schéma.table).';
COMMENT ON COLUMN history.seed_run.row_count IS 'Nombre de lignes importées.';
COMMENT ON COLUMN history.seed_run.sha256 IS 'Empreinte SHA-256 du contenu trié de la table après import.';
COMMENT ON COLUMN history.seed_run.seeded_at IS 'Date de l''import.';
COMMIT;
