\connect edenfr_db
-- Migration initiale pour la base de données Eden Eternal
-- Base: edenfr_db (Game Database)
-- Date: 15 Octobre 2026

-- Création de la table player_classes pour la progression des classes
DROP TABLE IF EXISTS "public"."player_classes";
CREATE TABLE "public"."player_classes" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER NOT NULL,
  "class_id" INTEGER NOT NULL,
  "class_level" INTEGER DEFAULT 1,
  "class_exp" BIGINT DEFAULT 0,
  "class_points" INTEGER DEFAULT 0,
  "lifelong_unlocked" BOOLEAN DEFAULT FALSE,
  "create_time" TIMESTAMP DEFAULT NOW()
);

-- Index pour rechercher rapidement les classes d'un personnage
CREATE INDEX idx_player_classes_character ON player_classes(character_id);
CREATE INDEX idx_player_classes_class ON player_classes(class_id);

-- Commentaires
COMMENT ON TABLE player_classes IS 'Progression des classes par personnage (1-20: base, 21-60: awakened)';
COMMENT ON COLUMN player_classes.class_id IS 'ID de la classe (1-20 base, 21-60 awakened avec noms uniques comme Berserker/Shielder)';
COMMENT ON COLUMN player_classes.class_level IS 'Niveau dans cette classe spécifique';
COMMENT ON COLUMN player_classes.class_exp IS 'XP accumulée dans cette classe';
COMMENT ON COLUMN player_classes.class_points IS 'Points de classe pour talents/certificats';
COMMENT ON COLUMN player_classes.lifelong_unlocked IS 'Débloquée pour toujours via item lifelong';

-- Création de la table class_progression pour les stats par niveau
DROP TABLE IF EXISTS "public"."class_progression";
CREATE TABLE "public"."class_progression" (
  "class_id" INTEGER NOT NULL,
  "level" INTEGER NOT NULL,
  "required_exp" BIGINT NOT NULL,
  "hp_bonus" INTEGER DEFAULT 0,
  "mp_bonus" INTEGER DEFAULT 0,
  "str_bonus" INTEGER DEFAULT 0,
  "dex_bonus" INTEGER DEFAULT 0,
  "con_bonus" INTEGER DEFAULT 0,
  "int_bonus" INTEGER DEFAULT 0,
  "psy_bonus" INTEGER DEFAULT 0,
  "agi_bonus" INTEGER DEFAULT 0,
  PRIMARY KEY (class_id, level)
);

COMMENT ON TABLE class_progression IS 'Stats des classes par niveau (issu de S_Class.ini)';
COMMENT ON COLUMN class_progression.required_exp IS 'XP requise pour atteindre ce niveau';

-- Création de la table class_spells pour les sorts par classe
DROP TABLE IF EXISTS "public"."class_spells";
CREATE TABLE "public"."class_spells" (
  "class_id" INTEGER NOT NULL,
  "spell_id" INTEGER NOT NULL,
  "required_level" INTEGER NOT NULL,
  "is_passive" BOOLEAN DEFAULT FALSE,
  PRIMARY KEY (class_id, spell_id, required_level)
);

COMMENT ON TABLE class_spells IS 'Sorts déblocables par classe et niveau';

-- Création de la table class_talents pour les talents de classe
DROP TABLE IF EXISTS "public"."class_talents";
CREATE TABLE "public"."class_talents" (
  "id" SERIAL PRIMARY KEY,
  "class_id" INTEGER NOT NULL,
  "talent_id" INTEGER NOT NULL,
  "required_class_points" INTEGER NOT NULL,
  "is_active" BOOLEAN DEFAULT FALSE
);

COMMENT ON TABLE class_talents IS 'Talents/certificats déblocables avec les points de classe';

-- Création de la table character_stats pour les stats des personnages
DROP TABLE IF EXISTS "public"."character_stats";
CREATE TABLE "public"."character_stats" (
  "character_id" INTEGER PRIMARY KEY REFERENCES player_characters(id) ON DELETE CASCADE,
  "str_base" INTEGER DEFAULT 0,
  "dex_base" INTEGER DEFAULT 0,
  "con_base" INTEGER DEFAULT 0,
  "int_base" INTEGER DEFAULT 0,
  "psy_base" INTEGER DEFAULT 0,
  "agi_base" INTEGER DEFAULT 0,
  "str_bonus" INTEGER DEFAULT 0,
  "dex_bonus" INTEGER DEFAULT 0,
  "con_bonus" INTEGER DEFAULT 0,
  "int_bonus" INTEGER DEFAULT 0,
  "psy_bonus" INTEGER DEFAULT 0,
  "agi_bonus" INTEGER DEFAULT 0
);

COMMENT ON TABLE character_stats IS 'Stats des personnages (base + bonus)';
COMMENT ON COLUMN character_stats.str_base IS 'STR de base (race, classe, niveau)';
COMMENT ON COLUMN character_stats.str_bonus IS 'STR bonus (équipement, buffs, talents)';

-- Création de la table player_items pour l'inventaire
DROP TABLE IF EXISTS "public"."player_items";
CREATE TABLE "public"."player_items" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "bag_id" INTEGER DEFAULT 0,
  "slot" INTEGER NOT NULL,
  "item_id" INTEGER NOT NULL,
  "quantity" INTEGER DEFAULT 1,
  "durability" INTEGER DEFAULT 100,
  "max_durability" INTEGER DEFAULT 100,
  "is_equipped" BOOLEAN DEFAULT FALSE,
  "enchant_level" INTEGER DEFAULT 0,
  "is_bound" BOOLEAN DEFAULT FALSE,
  UNIQUE(character_id, bag_id, slot)
);

COMMENT ON TABLE player_items IS 'Items dans l inventaire des joueurs';
COMMENT ON COLUMN player_items.bag_id IS 'ID du sac (0 = sac principal)';
COMMENT ON COLUMN player_items.slot IS 'Slot dans le sac (0-indexed)';
COMMENT ON COLUMN player_items.durability IS 'Durabilité actuelle (0-100%)';

-- Création de la table inventory_slots pour les capacités de sacs
DROP TABLE IF EXISTS "public"."inventory_slots";
CREATE TABLE "public"."inventory_slots" (
  "character_id" INTEGER PRIMARY KEY REFERENCES player_characters(id) ON DELETE CASCADE,
  "bag_0_slots" INTEGER DEFAULT 32,
  "bag_1_slots" INTEGER DEFAULT 0,
  "bag_2_slots" INTEGER DEFAULT 0,
  "bag_3_slots" INTEGER DEFAULT 0,
  "bag_0_rental_expiry" TIMESTAMP,
  "bag_1_rental_expiry" TIMESTAMP,
  "bag_2_rental_expiry" TIMESTAMP,
  "bag_3_rental_expiry" TIMESTAMP
);

COMMENT ON TABLE inventory_slots IS 'Capacités des sacs par personnage';
COMMENT ON COLUMN inventory_slots.bag_0_slots IS 'Nombre de slots du sac principal (32 par défaut, capture native NCID 312)';
COMMENT ON COLUMN inventory_slots.bag_0_rental_expiry IS 'Expiration du rental du sac principal (NULL = permanent)';

-- Création de la table player_bank pour la banque personnelle
DROP TABLE IF EXISTS "public"."player_bank";
CREATE TABLE "public"."player_bank" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "slot" INTEGER NOT NULL,
  "item_id" INTEGER NOT NULL,
  "quantity" INTEGER DEFAULT 1,
  "durability" INTEGER DEFAULT 100,
  "max_durability" INTEGER DEFAULT 100,
  UNIQUE(character_id, slot)
);

COMMENT ON TABLE player_bank IS 'Items stockés en banque personnelle';
COMMENT ON COLUMN player_bank.slot IS 'Slot en banque (0-indexed)';

-- Création de la table family_bank pour le stockage de famille/guilde
DROP TABLE IF EXISTS "public"."family_bank";
CREATE TABLE "public"."family_bank" (
  "id" SERIAL PRIMARY KEY,
  "family_id" INTEGER NOT NULL,
  "slot" INTEGER NOT NULL,
  "item_id" INTEGER NOT NULL,
  "quantity" INTEGER DEFAULT 1,
  "durability" INTEGER DEFAULT 100,
  "max_durability" INTEGER DEFAULT 100,
  "deposited_by" INTEGER, -- character_id qui a déposé
  "deposited_at" TIMESTAMP DEFAULT NOW(),
  UNIQUE(family_id, slot)
);

COMMENT ON TABLE family_bank IS 'Items stockés en banque de famille/guilde';
COMMENT ON COLUMN family_bank.family_id IS 'ID de la famille/guilde';
COMMENT ON COLUMN family_bank.deposited_by IS 'ID du personnage qui a déposé l item';

-- Création de la table active_trades pour les échanges en cours
DROP TABLE IF EXISTS "public"."active_trades";
CREATE TABLE "public"."active_trades" (
  "id" SERIAL PRIMARY KEY,
  "initiator_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "partner_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "initiator_confirmed" BOOLEAN DEFAULT FALSE,
  "partner_confirmed" BOOLEAN DEFAULT FALSE,
  "initiator_gold" INTEGER DEFAULT 0,
  "partner_gold" INTEGER DEFAULT 0,
  "created_at" TIMESTAMP DEFAULT NOW(),
  UNIQUE(initiator_id, partner_id)
);

COMMENT ON TABLE active_trades IS 'Sessions de trade actives entre joueurs';
COMMENT ON COLUMN active_trades.initiator_confirmed IS 'Confirmation de l initiateur';
COMMENT ON COLUMN active_trades.partner_confirmed IS 'Confirmation du partenaire';

-- Création de la table active_auctions pour les ventes en cours
DROP TABLE IF EXISTS "public"."active_auctions";
CREATE TABLE "public"."active_auctions" (
  "id" SERIAL PRIMARY KEY,
  "seller_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "item_id" INTEGER NOT NULL,
  "quantity" INTEGER DEFAULT 1,
  "price" INTEGER NOT NULL,
  "duration_hours" INTEGER DEFAULT 24,
  "created_at" TIMESTAMP DEFAULT NOW(),
  "expires_at" TIMESTAMP NOT NULL,
  "status" VARCHAR(20) DEFAULT 'active' -- 'active', 'sold', 'cancelled', 'expired'
);

COMMENT ON TABLE active_auctions IS 'Ventes en cours dans l hôtel des ventes';
COMMENT ON COLUMN active_auctions.status IS 'Status de la vente (active, sold, cancelled, expired)';

-- Création de la table auction_history pour l'historique des ventes
DROP TABLE IF EXISTS "public"."auction_history";
CREATE TABLE "public"."auction_history" (
  "id" SERIAL PRIMARY KEY,
  "auction_id" INTEGER REFERENCES active_auctions(id),
  "seller_id" INTEGER REFERENCES player_characters(id),
  "buyer_id" INTEGER REFERENCES player_characters(id),
  "item_id" INTEGER,
  "quantity" INTEGER,
  "price" INTEGER,
  "sold_at" TIMESTAMP DEFAULT NOW()
);

COMMENT ON TABLE auction_history IS 'Historique des ventes réalisées';
COMMENT ON COLUMN auction_history.sold_at IS 'Date de vente réussie';

-- Création de la table player_coins pour les points du joueur
DROP TABLE IF EXISTS "public"."player_coins";
CREATE TABLE "public"."player_coins" (
  "character_id" INTEGER PRIMARY KEY REFERENCES player_characters(id) ON DELETE CASCADE,
  "ap_points" INTEGER DEFAULT 0,
  "ep_points" INTEGER DEFAULT 0,
  "last_updated" TIMESTAMP DEFAULT NOW()
);

COMMENT ON TABLE player_coins IS 'Points du joueur (AP et EP)';
COMMENT ON COLUMN player_coins.ap_points IS 'Aeria Points (argent réel)';
COMMENT ON COLUMN player_coins.ep_points IS 'Eden Points (points gagnés en jeu)';

-- Création de la table item_mall_items pour les items de la boutique
DROP TABLE IF EXISTS "public"."item_mall_items";
CREATE TABLE "public"."item_mall_items" (
  "id" SERIAL PRIMARY KEY,
  "item_id" INTEGER NOT NULL,
  "category_id" INTEGER NOT NULL,
  "name" VARCHAR(100),
  "description" TEXT,
  "ap_price" INTEGER,
  "ep_price" INTEGER,
  "is_limited" BOOLEAN DEFAULT FALSE,
  "limit_start" TIMESTAMP,
  "limit_end" TIMESTAMP,
  "stock_quantity" INTEGER
);

COMMENT ON TABLE item_mall_items IS 'Items de la boutique (item mall)';
COMMENT ON COLUMN item_mall_items.is_limited IS 'Item limité en temps';

-- Création de la table item_mall_purchases pour l'historique des achats
DROP TABLE IF EXISTS "public"."item_mall_purchases";
CREATE TABLE "public"."item_mall_purchases" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER REFERENCES player_characters(id),
  "item_id" INTEGER,
  "quantity" INTEGER,
  "points_used" INTEGER,
  "points_type" VARCHAR(2), -- 'AP' or 'EP'
  "purchased_at" TIMESTAMP DEFAULT NOW()
);

COMMENT ON TABLE item_mall_purchases IS 'Historique des achats dans l item mall';

-- player_mail : table NATIVE fournie par database.out
-- (receiver_id, send_time, due_date, opened, returned, authoritative)
-- — le schéma maison posé ici à l'origine était incompatible avec
-- MailRepository ; ne plus la recréer.

-- Tickets de migration World -> Zone (CTicket natif : k1,k2,zoneIp,zonePort,time,random)
DROP TABLE IF EXISTS "public"."zone_tickets";
CREATE TABLE "public"."zone_tickets" (
  "key1" BIGINT NOT NULL,
  "key2" BIGINT NOT NULL,
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "client_ip" BIGINT DEFAULT 0,
  "zone_ip" BIGINT DEFAULT 0,
  "zone_port" INTEGER DEFAULT 0,
  "issued_at" TIMESTAMP DEFAULT NOW(),
  "used" BOOLEAN DEFAULT FALSE,
  PRIMARY KEY (key1, key2)
);
COMMENT ON TABLE zone_tickets IS 'Tickets de migration émis par WorldServer, consommés par ZoneServer (usage unique)';

-- Tickets de connexion Login -> World (CTicket 22B : k1,clientIp,worldIp,worldPort,time,random)
-- subject_id = account_id (edenfr_account.accounts.id, base distincte — pas de FK possible)
DROP TABLE IF EXISTS "public"."login_tickets";
CREATE TABLE "public"."login_tickets" (
  "key1" BIGINT NOT NULL PRIMARY KEY,
  "account_id" INTEGER NOT NULL,
  "account_name" VARCHAR(64) NOT NULL,
  "client_ip" BIGINT DEFAULT 0,
  "world_ip" BIGINT DEFAULT 0,
  "world_port" INTEGER DEFAULT 0,
  "issued_at" TIMESTAMP DEFAULT NOW(),
  "used" BOOLEAN DEFAULT FALSE
);
COMMENT ON TABLE login_tickets IS 'Tickets de connexion émis par LoginServer, consommés par WorldServer (CNE_CW_ServerReceiveTicket)';

-- Guildes (CFamily natif) : famille/guilde + membres
DROP TABLE IF EXISTS "public"."guild_members";
DROP TABLE IF EXISTS "public"."guilds";
CREATE TABLE "public"."guilds" (
  "id" SERIAL PRIMARY KEY,
  "name" VARCHAR(64) NOT NULL UNIQUE,
  "leader_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "level" INTEGER DEFAULT 1,
  "experience" BIGINT DEFAULT 0,
  "treasury" BIGINT DEFAULT 0,
  "emblem" INTEGER DEFAULT 0,
  "created_at" TIMESTAMP DEFAULT NOW()
);
CREATE TABLE "public"."guild_members" (
  "guild_id" INTEGER NOT NULL REFERENCES guilds(id) ON DELETE CASCADE,
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "rank" SMALLINT DEFAULT 4,           -- 0=Leader 1=Officer 2=Veteran 3=Member 4=Recruit
  "contribution" BIGINT DEFAULT 0,
  "joined_at" TIMESTAMP DEFAULT NOW(),
  PRIMARY KEY (guild_id, character_id)
);
COMMENT ON TABLE guilds IS 'Familles/guildes (CFamily)';
COMMENT ON TABLE guild_members IS 'Membres de guilde avec rang';

-- Amis (CFriend natif) — relation bidirectionnelle
DROP TABLE IF EXISTS "public"."player_friends";
CREATE TABLE "public"."player_friends" (
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "friend_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "created_at" TIMESTAMP DEFAULT NOW(),
  PRIMARY KEY (character_id, friend_id),
  CHECK (character_id <> friend_id)
);
COMMENT ON TABLE player_friends IS 'Liste d amis — une ligne par sens de la relation';

-- Couples/mariages (CLover + CWedding natif)
DROP TABLE IF EXISTS "public"."player_couples";
CREATE TABLE "public"."player_couples" (
  "id" SERIAL PRIMARY KEY,
  "partner1_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "partner2_id" INTEGER NOT NULL REFERENCES player_characters(id),
  "affection" INTEGER DEFAULT 0,
  "wedding_date" TIMESTAMP,            -- NULL = fiancés, renseigné = mariés
  "created_at" TIMESTAMP DEFAULT NOW(),
  UNIQUE (partner1_id),
  UNIQUE (partner2_id)
);
COMMENT ON TABLE player_couples IS 'Couples : affection + date de mariage (NULL = fiancé)';

-- Instances de familiers (CElf natif) — état par personnage
DROP TABLE IF EXISTS "public"."player_elfs";
CREATE TABLE "public"."player_elfs" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "elf_def_id" INTEGER NOT NULL,
  "name" VARCHAR(32) DEFAULT '',
  "mood" SMALLINT DEFAULT 50,
  "energy" SMALLINT DEFAULT 100,
  "friendship" INTEGER DEFAULT 0,
  "is_summoned" BOOLEAN DEFAULT FALSE,
  "created_at" TIMESTAMP DEFAULT NOW()
);
COMMENT ON TABLE player_elfs IS 'Familiers possédés (S_Elf.ini def + état runtime)';

-- Raccourcis de la barre d'action — schéma natif `shortcut_menu`
-- (désasm CExecutionerDBO::LoadShortcut : SELECT class,page,index1..10
-- WHERE character_id=? AND class != 0 ORDER BY class,page ; indexN =
-- i32 packé client, 0 = vide ; chaque classe a ses propres barres).
DROP TABLE IF EXISTS "public"."shortcut_menu";
CREATE TABLE "public"."shortcut_menu" (
  "character_id" INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
  "page" SMALLINT NOT NULL,
  "class" SMALLINT NOT NULL,
  "index1" INTEGER NOT NULL DEFAULT 0,
  "index2" INTEGER NOT NULL DEFAULT 0,
  "index3" INTEGER NOT NULL DEFAULT 0,
  "index4" INTEGER NOT NULL DEFAULT 0,
  "index5" INTEGER NOT NULL DEFAULT 0,
  "index6" INTEGER NOT NULL DEFAULT 0,
  "index7" INTEGER NOT NULL DEFAULT 0,
  "index8" INTEGER NOT NULL DEFAULT 0,
  "index9" INTEGER NOT NULL DEFAULT 0,
  "index10" INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (character_id, page, class)
);
COMMENT ON TABLE shortcut_menu IS 'Barres de raccourcis par classe (UpdateShortcut/GetShortcutOfPage)';
