-- Phase 0 baseline : statistiques SQL par base applicative.
-- Le module est préchargé par docker-compose (shared_preload_libraries).

\connect postgres
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

\connect edenfr_db
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

\connect edenfr_account
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

\connect edenfr_member
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
