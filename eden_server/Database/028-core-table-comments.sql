-- 028-core-table-comments.sql — documentation SQL des tables centrales (PLAN-ACTION-3, lot DB-C).
-- Règle du propriétaire : chaque table et chaque colonne est commentée. Premier lot : les tables
-- de la ville de guilde, des entrées de donjon et des comptes ; le reste est suivi par
-- tools/db/doc-lint.py (baseline à faire baisser). Idempotent (COMMENT ON remplace).
-- Base : edenfr.

COMMENT ON TABLE game.family_building_container IS
  'Contenu du hall d''une ville de guilde : bâtiments (item_type 41) et contrats de PNJ (item_type 42/82) posés dans le conteneur 0. Une ligne = un objet posé.';
COMMENT ON COLUMN game.family_building_container.family_id IS 'Guilde propriétaire (game.family.id).';
COMMENT ON COLUMN game.family_building_container.id IS 'Numéro de série de l''objet (séquence item_serial, partagée avec les inventaires).';
COMMENT ON COLUMN game.family_building_container.item_tid IS 'Id de l''objet posé (item bâtiment, ou item « contrat » du PNJ, c_item kind 41/42).';
COMMENT ON COLUMN game.family_building_container.tid IS 'Template : id du bâtiment (c_buliding) pour un bâtiment, id du PNJ (c_npc) pour un contrat de PNJ.';
COMMENT ON COLUMN game.family_building_container.item_type IS '41 (0x29) = bâtiment ; 42 (0x2a) ou 82 (0x52) = contrat de PNJ.';
COMMENT ON COLUMN game.family_building_container.durability IS 'Durabilité courante (usure par les ordres de travail ; réparée au prix du fonds de guilde).';
COMMENT ON COLUMN game.family_building_container.container_index IS 'Conteneur : 0 = hall de la ville (seul utilisé).';
COMMENT ON COLUMN game.family_building_container.loc IS 'Emplacement : 0/10/20/30/40 = bâtiments ; base+1 = housekeeper ; base+2… = PNJ de service ; 100 = manoir.';
COMMENT ON COLUMN game.family_building_container.x IS 'Position X dans le repère de scène Eden (calculée par la formule native des slots).';
COMMENT ON COLUMN game.family_building_container.y IS 'Position Y (profondeur) dans le repère de scène Eden.';
COMMENT ON COLUMN game.family_building_container.face_dir IS 'Orientation en radians.';
COMMENT ON COLUMN game.family_building_container.level IS 'Niveau du bâtiment (3-12) ; 0 pour un PNJ.';
COMMENT ON COLUMN game.family_building_container.exp IS 'Expérience du bâtiment (monte de 2 par ordre de travail ; seuil c_level col 12).';
COMMENT ON COLUMN game.family_building_container.due_date IS 'Fin du contrat de PNJ, en secondes Unix ; 0 = permanent.';

COMMENT ON TABLE game.player_dungeon_entries IS
  'Entrées de donjon consommées par personnage, node et jour de reset (quota journalier ; configurable, 10 solo + 10 multi par défaut).';
COMMENT ON COLUMN game.player_dungeon_entries.player_id IS 'Personnage (game.player_characters.id).';
COMMENT ON COLUMN game.player_dungeon_entries.node_id IS 'Node du donjon (c_node), version solo ou multijoueur.';
COMMENT ON COLUMN game.player_dungeon_entries.day IS 'Numéro du jour de jeu (le jour bascule à l''heure de reset, 06:00 visé — voir IGameClock).';
COMMENT ON COLUMN game.player_dungeon_entries.times IS 'Nombre d''entrées déjà consommées ce jour-là.';

COMMENT ON TABLE account.accounts IS 'Comptes de jeu (identifiants, état, rôle). Un compte administrateur a role = ''Admin'' ; le niveau admin par compte remplacera le privilège par personnage.';
COMMENT ON COLUMN account.accounts.id IS 'Identifiant du compte.';
COMMENT ON COLUMN account.accounts.username IS 'Nom de connexion.';
COMMENT ON COLUMN account.accounts.role IS '« Admin » pour un compte d''administration, sinon vide / joueur.';

COMMENT ON TABLE game.family IS 'Guildes (« familles ») : identité, niveau, trésorerie, privilèges, ville ouverte (house_open = node 501/502).';
COMMENT ON TABLE game.player_characters IS 'Personnages : identité, apparence, niveau, expérience, position, argent, appartenance à une guilde, privilège de jeu.';
COMMENT ON TABLE game.inventory1 IS 'Sac principal des personnages : un objet par ligne (loc = case du sac).';
COMMENT ON TABLE game.inventory2 IS 'Sacs supplémentaires des personnages (même format que inventory1).';
COMMENT ON TABLE game.equipment1 IS 'Équipement porté (jeu 1) : un objet par emplacement.';
COMMENT ON TABLE game.equipment2 IS 'Équipement porté (jeu 2, changement rapide).';
COMMENT ON TABLE game.mailitem IS 'Objets joints aux courriers.';
COMMENT ON TABLE game.active_auctions IS 'Mises en vente en cours à l''hôtel des ventes.';
COMMENT ON TABLE game.active_trades IS 'Échanges entre joueurs en cours.';
COMMENT ON TABLE game.family_storage IS 'Coffre de guilde : objets déposés.';
COMMENT ON TABLE game.family_storage_log IS 'Journal des dépôts/retraits du coffre de guilde.';
COMMENT ON TABLE game.family_log IS 'Journal des événements de la guilde.';
COMMENT ON TABLE game.family_achievement IS 'Succès de guilde obtenus.';
COMMENT ON TABLE game.mission_trace IS 'Quêtes suivies (tracker) par personnage.';
COMMENT ON TABLE game.personal_mission_states IS 'État d''avancement des quêtes par personnage.';
COMMENT ON TABLE game.final_mission_state1 IS 'États de quêtes terminées (première moitié de la vue final_mission_state_v2).';
COMMENT ON TABLE game.player_enchants IS 'Statuts / enchants actifs et persistés d''un personnage.';
COMMENT ON TABLE game.player_classlist IS 'Classes apprises par personnage et leur progression.';
COMMENT ON TABLE game.player_appellation IS 'Titres obtenus par personnage.';
COMMENT ON TABLE game.player_card IS 'Fiche de profil publique du personnage (statistiques affichées aux amis, mise à jour par le serveur Mission).';
COMMENT ON TABLE game.fast_dress IS 'Tenues rapides (24 raccourcis × 3 pages) enregistrées par personnage.';
COMMENT ON TABLE game.daily_awards IS 'Calendrier de connexion : récompenses obtenues.';
COMMENT ON TABLE game.configuration IS 'Réglages généraux du serveur de jeu (clé/valeur).';
COMMENT ON TABLE game.login_tickets IS 'Tickets de connexion à usage unique émis à l''entrée en zone.';
COMMENT ON TABLE game.colosseum_ranking IS 'Classement du colisée (lu par le service de classements de Mission).';
COMMENT ON TABLE account.itemmall IS 'Catalogue de la boutique à diamants.';
COMMENT ON TABLE account.worlds IS 'Serveurs de jeu (mondes) proposés à la connexion.';
