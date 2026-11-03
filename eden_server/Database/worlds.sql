\connect edenfr_db
DROP TABLE IF EXISTS "public"."worlds";
CREATE TABLE "public"."worlds" (
  "id" int4 NOT NULL DEFAULT nextval(('"worlds_id_seq"'::text)::regclass),
  "name" varchar(32) COLLATE "pg_catalog"."default",
  "ip" varchar(15) COLLATE "pg_catalog"."default",
  "port" int4 DEFAULT 5200,
  "online_user" int4 DEFAULT 0,
  "maxnum_user" int4 NOT NULL DEFAULT 1000,
  "state" int4 DEFAULT 0,
  "version" varchar(32) COLLATE "pg_catalog"."default",
  "show_order" int4 DEFAULT 0,
  "nochar_block_login" int4,
  "green_level" int4,
  "red_level" int4
);

INSERT INTO "public"."worlds" VALUES (1020, 'HyperionT', '192.168.0.55', 5568, 16, 300, 0, 'Unknown_Version', 0, 0, 1, 100);
INSERT INTO "public"."worlds" VALUES (1010, 'EternityT', '192.168.0.55', 5712, 0, 150, 1, '002.003.05.67', 1, 0, 1, 100);

ALTER TABLE "public"."worlds" ADD CONSTRAINT "worlds_pkey" PRIMARY KEY ("id");
