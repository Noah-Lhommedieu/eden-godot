\connect edenfr_db
-- Colonnes attendues par le serveur C# mais absentes du dump legacy
-- (ajoutées manuellement dans l'ancienne base fn* — encodées ici pour
-- que l'init PG17 soit complète) :
--  - appearance_data : blob apparence envoyé dans la liste persos
--    (CharacterRepository.GetCharactersForListAsync).
--  - create_flag : marqueur de création utilisé par le flux de création.
ALTER TABLE player_characters ADD COLUMN IF NOT EXISTS appearance_data bytea;
ALTER TABLE player_characters ADD COLUMN IF NOT EXISTS create_flag smallint;
