\connect edenfr_db
-- 012-item-serial-sequence.sql — séquences partagées d'allocation
-- d'ids. Base : edenfr_db.
--
-- Problème corrigé : ItemSerialAllocator (zone) et MissionDb
-- (mission) allouaient les serials d'items par « SELECT max(id)+1
-- seedé en mémoire + Interlocked » — thread-safe intra-processus
-- seulement : deux processus produisaient les mêmes ids → collision
-- réelle sur les PK des tables items. Même pattern buggé sur
-- elf.elf_id (ElfRepository.NextElfIdAsync) et player_mail.id
-- (MailIdAllocator — player_mail.id est PRIMARY KEY).
--
-- Fix : séquences PostgreSQL — nextval() est atomique et
-- cross-process. Le C# fait un nextval par allocation (un seul
-- aller-retour) — voir ItemSerialAllocator / MailIdAllocator /
-- ElfRepository.
--
-- Seed : setval(seq, N, false) → le premier nextval rend N.
-- Le garde GREATEST(last_value + is_called…) évite de REMBOBINER la
-- séquence si la migration est rejouée après des allocations —
-- ré-exécution idempotente et sans collision.
--
-- Liste des tables items = InventoryRepository.ItemSerialTables :
-- les conteneurs dont la colonne `id` vit dans le même espace de
-- serials (un serial n'est jamais réutilisé entre sacs, banque,
-- mail, équipement — convention native). family_building_container
-- incluse : InsertFamilyBuildingItemAsync alloue via l'allocateur.
-- Tenir les deux listes en sync.

-- =====================================================================
-- item_serial — espace global des serials d'items (16 tables)
-- =====================================================================
CREATE SEQUENCE IF NOT EXISTS item_serial;

SELECT setval('item_serial', GREATEST(
    1,
    -- prochaine valeur que la séquence servirait (ne jamais rembobiner)
    (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END
       FROM item_serial),
    -- premier serial libre au-dessus du max des tables items
    (SELECT COALESCE(MAX(m), 0) + 1 FROM (
        SELECT max(id) AS m FROM inventory1 UNION ALL
        SELECT max(id) FROM inventory2 UNION ALL
        SELECT max(id) FROM equipment1 UNION ALL
        SELECT max(id) FROM equipment2 UNION ALL
        SELECT max(id) FROM storage1 UNION ALL
        SELECT max(id) FROM storage2 UNION ALL
        SELECT max(id) FROM mailitem UNION ALL
        SELECT max(id) FROM family_storage UNION ALL
        SELECT max(id) FROM player_room_container UNION ALL
        SELECT max(id) FROM farm_container UNION ALL
        SELECT max(id) FROM bags UNION ALL
        SELECT max(id) FROM elf_container UNION ALL
        SELECT max(id) FROM item_mall_items UNION ALL
        SELECT max(id) FROM player_bank UNION ALL
        SELECT max(id) FROM collection UNION ALL
        SELECT max(id) FROM family_building_container) t)),
    false);

COMMENT ON SEQUENCE item_serial IS
  'Serials globaux d''items (espace partagé des 16 tables items — ItemSerialAllocator)';

-- =====================================================================
-- elf_serial — ids de familiers (elf.elf_id)
-- =====================================================================
CREATE SEQUENCE IF NOT EXISTS elf_serial;

SELECT setval('elf_serial', GREATEST(
    1,
    (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END
       FROM elf_serial),
    (SELECT COALESCE(MAX(elf_id), 0) + 1 FROM elf)),
    false);

COMMENT ON SEQUENCE elf_serial IS
  'Ids des familiers elf (ElfRepository.NextElfIdAsync)';

-- =====================================================================
-- mail_serial — ids de courriers (player_mail.id, PK)
-- =====================================================================
CREATE SEQUENCE IF NOT EXISTS mail_serial;

SELECT setval('mail_serial', GREATEST(
    1,
    (SELECT last_value + CASE WHEN is_called THEN 1 ELSE 0 END
       FROM mail_serial),
    (SELECT COALESCE(MAX(id), 0) + 1 FROM player_mail)),
    false);

COMMENT ON SEQUENCE mail_serial IS
  'Ids de courriers player_mail (MailIdAllocator)';
