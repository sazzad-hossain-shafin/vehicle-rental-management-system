-- Provisions (or re-asserts) the restricted runtime role the API connects as. Idempotent: run it as often as you like,
-- and always AFTER the migrations (it grants on the tables that exist). Run it as the MIGRATION OWNER (the role that
-- created the tables). It is the single source: Docker Compose's db-roles service and the automated tests run this file.
--
-- Inputs, as custom settings so they work through psql and through any driver, and so the password never appears in a
-- command line or in this file:
--   provision.app_user       the runtime role's name (lower-case letters, digits, underscore)
--   provision.app_password   its password (at least 16 characters)
--   psql:   PGOPTIONS="-c provision.app_user=... -c provision.app_password=..." psql -v ON_ERROR_STOP=1 -f provision-runtime-role.sql
--   other:  SELECT set_config('provision.app_user', '...', false); SELECT set_config('provision.app_password', '...', false);
--
-- The role it creates, and keeps creating the same way on every run:
--   * LOGIN only: NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOREPLICATION, NOBYPASSRLS, NOINHERIT, no memberships
--   * owns nothing; cannot create objects in the schema or the database
--   * SELECT, INSERT, UPDATE, DELETE on the application tables; USAGE and SELECT on sequences; no TRUNCATE
--   * SELECT only on the migration history (it reads it to report pending migrations; it can never change it)
--   * the same privileges on tables and sequences the owner creates LATER (default privileges)
-- It never grants the owner's rights, never alters PUBLIC, and refuses to run if the runtime role is the owner.

DO $provision$
DECLARE
    app_user     text := current_setting('provision.app_user', true);
    app_password text := current_setting('provision.app_password', true);
    owner_role   text := current_user;
    db           text := current_database();
    parent       record;
    owned        bigint;
BEGIN
    IF app_user IS NULL OR app_user !~ '^[a-z_][a-z0-9_]{2,62}$' THEN
        RAISE EXCEPTION 'provision.app_user is missing or not a valid role name (lower-case letters, digits and underscore, 3 to 63 characters)';
    END IF;

    IF app_password IS NULL OR length(app_password) < 16 THEN
        RAISE EXCEPTION 'provision.app_password is missing or shorter than 16 characters';
    END IF;

    IF app_user = owner_role OR app_user IN ('postgres', 'public') THEN
        RAISE EXCEPTION 'the runtime role must be a different role from the migration owner (and not postgres)';
    END IF;

    -- Create it, or re-assert every attribute so a role that was made more powerful by hand is brought back down.
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = app_user) THEN
        EXECUTE format(
            'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L',
            app_user, app_password);
    ELSE
        EXECUTE format(
            'ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT PASSWORD %L',
            app_user, app_password);
    END IF;

    -- It must not be a member of any other role (a membership could hand it someone else's rights).
    FOR parent IN
        SELECT r.rolname AS parent_name
        FROM pg_auth_members m
        JOIN pg_roles r ON r.oid = m.roleid
        JOIN pg_roles u ON u.oid = m.member
        WHERE u.rolname = app_user
    LOOP
        EXECUTE format('REVOKE %I FROM %I', parent.parent_name, app_user);
    END LOOP;

    -- It must not own anything here. If it does, something was set up wrongly: stop rather than guess.
    SELECT count(*) INTO owned
    FROM pg_class c
    JOIN pg_roles o ON o.oid = c.relowner
    WHERE o.rolname = app_user AND c.relnamespace NOT IN ('pg_catalog'::regnamespace, 'information_schema'::regnamespace);

    IF owned > 0 THEN
        RAISE EXCEPTION 'the runtime role owns % object(s) in this database; it must own nothing', owned;
    END IF;

    -- Connect and use the schema, but never create anything.
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', db, app_user);
    EXECUTE format('REVOKE CREATE ON DATABASE %I FROM %I', db, app_user);
    EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', app_user);
    EXECUTE format('REVOKE CREATE ON SCHEMA public FROM %I', app_user);

    -- Start from nothing and grant exactly what the API needs.
    EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I', app_user);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO %I', app_user);

    EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I', app_user);
    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO %I', app_user);

    -- The migration history: read only, so the API can report pending migrations but can never change the schema record.
    IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
        EXECUTE format('REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM %I', app_user);
        EXECUTE format('GRANT SELECT ON TABLE public."__EFMigrationsHistory" TO %I', app_user);
    END IF;

    -- Tables and sequences the owner creates in later migrations get the same privileges automatically. (Running this
    -- script again after a migration is still safe and also covers anything created before these defaults existed.)
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', owner_role, app_user);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I', owner_role, app_user);
END
$provision$;
