\connect edenfr_db
-- Migration 002: Awakened Classes Data (CORRECTED)
-- Date: 15 Octobre 2026
-- Description: VRAIES classes awakened avec leurs noms uniques

-- STRUCTURE RÉELLE (analysée depuis T_Class.ini):
-- Classes 1-20: Classes de BASE
-- Classes 21-60: Classes AWAKENED avec NOMS UNIQUES (2 branches par classe de base)

-- Chaque classe de base (1-20) peut évoluer vers 2 classes awakened différentes

-- ========================================
-- WARRIOR (1) → 2 branches awakened
-- ========================================
-- 21: Berserker  - Attaque pure, sacrifice défense
-- 22: Shielder   - Défense pure, protection équipe

-- ========================================
-- MAGICIAN (2) → 2 branches awakened
-- ========================================
-- 23: Arch-Elementalist     - Maîtrise éléments (feu/glace/foudre)
-- 24: Gravity Manipulator   - Contrôle gravité

-- ========================================
-- HUNTER (3) → 2 branches awakened
-- ========================================
-- 25: Trainer      - Dresseur de bêtes, renforce familiers
-- 26: Devil Hunter - Chasseur de démons, attaques magiques

-- ========================================
-- CLERIC (4) → 2 branches awakened
-- ========================================
-- 27: Adjudicator - Juge, punition divine
-- 28: Equilibrian - Équilibre lumière/ombre

-- ========================================
-- THIEF (5) → 2 branches awakened
-- ========================================
-- 29: Assassin - Assassinat, DPS critique
-- 30: Kage     - Ninja ombre, techniques secrètes

-- ========================================
-- BARD (6) → 2 branches awakened
-- ========================================
-- 31: Battlefield Poet - Buffs de combat, inspiration guerrière
-- 32: Life Worshipper  - Heals, buffs de survie

-- ========================================
-- KNIGHT (7) → 2 branches awakened
-- ========================================
-- 33: Executioner    - Attaque rapide, exécution
-- 34: Glacier Knight - Défense magique, bouclier de glace

-- ========================================
-- ENGINEER (8) → 2 branches awakened
-- ========================================
-- 35: Mecha Ares  - Transformation en arme, corps+esprit
-- 36: Annihilator - Armes à feu, bombardement

-- ========================================
-- MARTIAL ARTIST (9) → 2 branches awakened
-- ========================================
-- 37: Asura    - Transformation asura, armes corporelles
-- 38: Rakshasa - Fantôme silencieux, attaque furtive

-- ========================================
-- ILLUSIONIST (10) → 2 branches awakened
-- ========================================
-- 39: Demon Tamer   - Contrôle démons
-- 40: Chronologist  - Contrôle du temps

-- ========================================
-- BLADE DANCER (11) → 2 branches awakened
-- ========================================
-- 41: Blade Acrobat  - Acrobatie, lames rapides
-- 42: Elegant Dancer - Charme, victoire par séduction

-- ========================================
-- SHAMAN (12) → 2 branches awakened
-- ========================================
-- 43: Druid     - Transformation en créatures
-- 44: Archimage - Invocation totems puissants

-- ========================================
-- TEMPLAR (13) → 2 branches awakened
-- ========================================
-- 45: Blood Knight - Chevalier sang, cycle combat/mort
-- 46: Paladin      - Chevalier saint, gardien suprême

-- ========================================
-- RANGER (14) → 2 branches (placeholders)
-- ========================================
-- 47-48: TBD

-- ========================================
-- WARLOCK (15) → 2 branches awakened
-- ========================================
-- 49: Demon Tamer - Domination démons
-- 50: Conjurer    - Invocation, malédictions

-- ========================================
-- SAMURAI (16) → 2 branches awakened
-- ========================================
-- 51: Blade Master  - Maîtrise katana, styles combinés
-- 52: Master Fencer - Technique ancienne, duel 1v1

-- ========================================
-- SAGE (17) → 2 branches (placeholders)
-- ========================================
-- 53-54: TBD

-- ========================================
-- DRAGON KNIGHT (18) → 2 branches (placeholders)
-- ========================================
-- 55-56: TBD

-- ========================================
-- LUMINARY (19) → 2 branches (placeholders)
-- ========================================
-- 57-58: TBD

-- ========================================
-- MIMIC (20) → 2 branches awakened
-- ========================================
-- 59: Inquisitor - Juge de mort
-- 60: Reaver     - Faucheuse

-- Prérequis typiques pour déblocage awakened:
-- - Classe de base level 80
-- - Complétion série de quêtes spécifique
-- Ex: "Complete the 'Titan's Ancestral Soul' quest series"

COMMENT ON TABLE player_classes IS 'Progression des classes (1-20: base, 21-60: awakened avec noms uniques)';
