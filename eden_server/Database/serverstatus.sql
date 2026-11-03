\connect edenfr_db
DROP TABLE IF EXISTS "public"."serverstatus";
CREATE TABLE "public"."serverstatus" (
  "id" int4 NOT NULL,
  "name" text COLLATE "pg_catalog"."default",
  "ext_address" text COLLATE "pg_catalog"."default",
  "ext_port" int4 NOT NULL,
  "int_address" text COLLATE "pg_catalog"."default",
  "int_port" int4 NOT NULL,
  "last_start_time" int4 DEFAULT 0
);

INSERT INTO "public"."serverstatus" VALUES (1010, 'WorldServer', '192.168.0.55', 5712, '127.0.0.1', 5712, 0);
INSERT INTO "public"."serverstatus" VALUES (1011, 'ZoneServer', '192.168.0.55', 10166, '127.0.0.1', 10166, 0);
INSERT INTO "public"."serverstatus" VALUES (-2, 'MissionServer', 'none', -1, '127.0.0.1', 7654, 1628266391);

ALTER TABLE "public"."serverstatus" ADD CONSTRAINT "serverstatus_pkey" PRIMARY KEY ("id");
