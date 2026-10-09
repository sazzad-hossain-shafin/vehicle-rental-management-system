-- Throwaway schema for the Row-Level Security proof of concept (ADR 007, PR 14A-2).
-- Run as the table OWNER role. {app} is replaced with the name of the application role created for the test run.
--
-- Design under test:
--   * every tenant table has company_id
--   * RLS is enabled AND forced, so even the owner is subject to the policies
--   * the policies compare company_id with a TRANSACTION-LOCAL setting (app.company_id); no setting = no rows
--   * the application connects as {app}: it owns nothing and has no BYPASSRLS
--   * composite foreign keys keep a row and the rows it references in the same company

CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Reads the tenant setting. Anything that is not a well-formed UUID (missing, empty, garbage, an injection attempt)
-- becomes NULL, and "company_id = NULL" matches nothing: the policies fail closed instead of raising errors.
CREATE FUNCTION app_company_id() RETURNS uuid
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$
        SELECT CASE
            WHEN current_setting('app.company_id', true)
                 ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
            THEN current_setting('app.company_id', true)::uuid
        END
    $$;

CREATE TABLE companies (
    id            uuid PRIMARY KEY,
    slug          text NOT NULL UNIQUE,
    name          text NOT NULL,
    status        text NOT NULL,
    internal_note text
);

CREATE TABLE vehicles (
    id           uuid PRIMARY KEY,
    company_id   uuid NOT NULL REFERENCES companies (id),
    registration text NOT NULL,
    daily_rate   numeric(10, 2) NOT NULL,
    -- Targets for composite foreign keys, and the per-company business key. A registration number unique across
    -- ALL companies would let one company probe another's fleet through "duplicate key" errors (see tests).
    UNIQUE (company_id, id),
    UNIQUE (company_id, registration)
);
CREATE INDEX ix_vehicles_company ON vehicles (company_id, registration);

CREATE TABLE reservations (
    id         uuid PRIMARY KEY,
    company_id uuid NOT NULL,
    vehicle_id uuid NOT NULL,
    start_date date NOT NULL,
    end_date   date NOT NULL,
    status     text NOT NULL,
    CHECK (end_date > start_date),
    -- The vehicle must belong to the same company as the reservation, enforced by the database itself.
    FOREIGN KEY (company_id, vehicle_id) REFERENCES vehicles (company_id, id),
    -- Same shape as the production no-overlap rule; it is per vehicle, and a vehicle has one company.
    CONSTRAINT ex_reservations_no_overlap EXCLUDE USING gist (
        vehicle_id WITH =,
        daterange(start_date, end_date, '[)') WITH &&)
        WHERE (status = 'Active')
);
CREATE INDEX ix_reservations_company ON reservations (company_id, start_date);

-- Negative controls: the shapes the migration must AVOID, kept here to prove why.
CREATE TABLE legacy_vehicles (
    id           uuid PRIMARY KEY,
    company_id   uuid NOT NULL,
    registration text NOT NULL UNIQUE          -- global unique key: leaks existence across tenants
);

CREATE TABLE naive_reservations (
    id         uuid PRIMARY KEY,
    company_id uuid NOT NULL,
    vehicle_id uuid NOT NULL REFERENCES vehicles (id)   -- plain foreign key: the check ignores RLS
);

-- ---------------------------------------------------------------------------------------------------------------
-- Row-Level Security
-- ---------------------------------------------------------------------------------------------------------------
ALTER TABLE vehicles           ENABLE ROW LEVEL SECURITY;
ALTER TABLE vehicles           FORCE  ROW LEVEL SECURITY;
ALTER TABLE reservations       ENABLE ROW LEVEL SECURITY;
ALTER TABLE reservations       FORCE  ROW LEVEL SECURITY;
ALTER TABLE legacy_vehicles    ENABLE ROW LEVEL SECURITY;
ALTER TABLE legacy_vehicles    FORCE  ROW LEVEL SECURITY;
ALTER TABLE naive_reservations ENABLE ROW LEVEL SECURITY;
ALTER TABLE naive_reservations FORCE  ROW LEVEL SECURITY;

-- USING filters reads, updates and deletes; WITH CHECK stops a write from creating or moving a row into another
-- company (an UPDATE that changes company_id is refused).
CREATE POLICY tenant_isolation ON vehicles
    USING (company_id = app_company_id()) WITH CHECK (company_id = app_company_id());
CREATE POLICY tenant_isolation ON reservations
    USING (company_id = app_company_id()) WITH CHECK (company_id = app_company_id());
CREATE POLICY tenant_isolation ON legacy_vehicles
    USING (company_id = app_company_id()) WITH CHECK (company_id = app_company_id());
CREATE POLICY tenant_isolation ON naive_reservations
    USING (company_id = app_company_id()) WITH CHECK (company_id = app_company_id());

-- "companies" is not tenant business data. A company sees only its own row. RLS is enabled but NOT forced, so the
-- owner (never the application) can read all rows, and that is what the platform function below relies on.
ALTER TABLE companies ENABLE ROW LEVEL SECURITY;
CREATE POLICY own_company ON companies USING (id = app_company_id());

-- The one controlled way to read across companies without BYPASSRLS: a narrow SECURITY DEFINER function that returns
-- only non-sensitive columns. A pinned search_path stops it from being hijacked by objects the caller creates.
CREATE FUNCTION platform_list_companies() RETURNS TABLE (id uuid, slug text, status text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$ SELECT c.id, c.slug, c.status FROM public.companies c ORDER BY c.slug $$;
REVOKE ALL ON FUNCTION platform_list_companies() FROM PUBLIC;

-- ---------------------------------------------------------------------------------------------------------------
-- Privileges: the application role gets exactly what it needs and no more.
-- ---------------------------------------------------------------------------------------------------------------
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO {app};
GRANT SELECT ON companies TO {app};
GRANT SELECT, INSERT, UPDATE, DELETE ON vehicles, reservations, legacy_vehicles, naive_reservations TO {app};
GRANT EXECUTE ON FUNCTION app_company_id() TO {app};
GRANT EXECUTE ON FUNCTION platform_list_companies() TO {app};
