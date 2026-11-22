-- PREVIEW Row-Level Security policies for the three tenant tables. THIS FILE IS APPLIED ONLY INSIDE DISPOSABLE TEST
-- DATABASES. It is not a migration, nothing in the application, Docker Compose or CI deploys it, and no production table
-- has RLS in Phase 14A-4. Phase 14A-7 (the contract step) will add the real policies, after the backfill.
--
-- It lets the tests prove, against the real tables and the real EF model, that the tenant transaction, the unit of work
-- and the booking guarantees work under Row-Level Security exactly as the spike showed on throwaway tables.

CREATE FUNCTION app_company_id() RETURNS uuid
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$
        SELECT CASE
            WHEN current_setting('app.company_id', true)
                 ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
            THEN current_setting('app.company_id', true)::uuid
        END
    $$;

-- Stands in for what the application will do in a later phase (set CompanyId on every insert): fills a missing CompanyId
-- from the transaction's tenant, so the current services (which do not know about companies) can create rows in tests.
CREATE FUNCTION preview_fill_company() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
    BEGIN
        IF NEW."CompanyId" IS NULL THEN
            NEW."CompanyId" := app_company_id();
        END IF;
        RETURN NEW;
    END
    $$;

CREATE TRIGGER preview_fill_company BEFORE INSERT ON "Vehicles"     FOR EACH ROW EXECUTE FUNCTION preview_fill_company();
CREATE TRIGGER preview_fill_company BEFORE INSERT ON "Reservations" FOR EACH ROW EXECUTE FUNCTION preview_fill_company();
CREATE TRIGGER preview_fill_company BEFORE INSERT ON "Rentals"      FOR EACH ROW EXECUTE FUNCTION preview_fill_company();

ALTER TABLE "Vehicles"     ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Vehicles"     FORCE  ROW LEVEL SECURITY;
ALTER TABLE "Reservations" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Reservations" FORCE  ROW LEVEL SECURITY;
ALTER TABLE "Rentals"      ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Rentals"      FORCE  ROW LEVEL SECURITY;

CREATE POLICY preview_tenant_isolation ON "Vehicles"
    USING ("CompanyId" = app_company_id()) WITH CHECK ("CompanyId" = app_company_id());
CREATE POLICY preview_tenant_isolation ON "Reservations"
    USING ("CompanyId" = app_company_id()) WITH CHECK ("CompanyId" = app_company_id());
CREATE POLICY preview_tenant_isolation ON "Rentals"
    USING ("CompanyId" = app_company_id()) WITH CHECK ("CompanyId" = app_company_id());
