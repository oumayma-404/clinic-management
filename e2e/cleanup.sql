-- Removes the suite's own fixture patients and everything hanging off them.
--
--   docker exec -i clinic-postgres psql -U clinic_user -d clinic_management < e2e/cleanup.sql
--
-- ⚠️ **For a SHARED developer database only.** CI bootstraps an empty one per run and throws it away, so it
-- never needs this. What it is actually for is the local pass: a full run creates ~65 fixture patients with
-- their invoices, payments and treatment plans, and those land in the **same** caisse, « Créances » and
-- dashboard the developer reads all day. Sixty-five is a nuisance; five runs is 300 and the day's figures stop
-- meaning anything.
--
-- ⚠️ **Scoped to `FirstName = 'E2E'` and nothing else.** That is the only marker the fixture writes
-- (`lib/fixtures.ts`), it cannot collide with a Tunisian given name, and every statement below joins through
-- it — so this can neither widen by accident nor touch a real patient's money.
--
-- ⚠️ **SQL rather than the product's own DELETE, deliberately.** `PatientDeletionBlockers` refuses a patient
-- carrying money — correctly, and that is a rule this suite tests — so the API route cannot remove exactly the
-- fixtures that matter. This is reverting a verification pass's own artefacts, which
-- `.claude/rules/verification.md` § 7 permits by name; it is not a way around the product's rules.
--
-- Order is child-to-parent: no `ON DELETE CASCADE` is assumed, because assuming one and being wrong leaves
-- orphans that read as corrupt data.

BEGIN;

CREATE TEMP TABLE _e2e_patients AS
SELECT "Id" FROM "Patients" WHERE "FirstName" = 'E2E';

CREATE TEMP TABLE _e2e_invoices AS
SELECT "Id" FROM "Invoices" WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients);

CREATE TEMP TABLE _e2e_plans AS
SELECT "Id" FROM "TreatmentPlans" WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients);

CREATE TEMP TABLE _e2e_records AS
SELECT "Id" FROM "DentalRecords" WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients);

\echo 'about to remove:'
SELECT
  (SELECT count(*) FROM _e2e_patients) AS patients,
  (SELECT count(*) FROM _e2e_invoices) AS invoices,
  (SELECT count(*) FROM _e2e_plans)    AS plans,
  (SELECT count(*) FROM _e2e_records)  AS fiches;

-- Money: credit notes and payments before the invoices they hang off.
DELETE FROM "CreditNotes"       WHERE "InvoiceId" IN (SELECT "Id" FROM _e2e_invoices);
DELETE FROM "Payments"          WHERE "InvoiceId" IN (SELECT "Id" FROM _e2e_invoices);
DELETE FROM "InvoiceLines"      WHERE "InvoiceId" IN (SELECT "Id" FROM _e2e_invoices);

-- The échéancier, then the plan's acts and their steps.
DELETE FROM "InstallmentPayments"
  WHERE "InstallmentId" IN (SELECT "Id" FROM "Installments" WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans));
DELETE FROM "Installments"      WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans);
DELETE FROM "TreatmentPlanItemSteps"
  WHERE "TreatmentPlanItemId" IN (SELECT "Id" FROM "TreatmentPlanItems" WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans));

-- Break the links the fiches and appointments hold INTO the plan before removing its acts, so nothing is left
-- pointing at a row that no longer exists.
UPDATE "AppointmentProcedures"
   SET "TreatmentPlanItemId" = NULL, "TreatmentPlanItemStepId" = NULL
 WHERE "TreatmentPlanItemId" IN (SELECT "Id" FROM "TreatmentPlanItems" WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans));
UPDATE "Appointments"
   SET "TreatmentPlanItemId" = NULL
 WHERE "TreatmentPlanItemId" IN (SELECT "Id" FROM "TreatmentPlanItems" WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans));

DELETE FROM "TreatmentPlanItems" WHERE "TreatmentPlanId" IN (SELECT "Id" FROM _e2e_plans);
DELETE FROM "Invoices"           WHERE "Id" IN (SELECT "Id" FROM _e2e_invoices);
DELETE FROM "TreatmentPlans"     WHERE "Id" IN (SELECT "Id" FROM _e2e_plans);

-- Clinical records.
DELETE FROM "DentalRecordActs"  WHERE "DentalRecordId" IN (SELECT "Id" FROM _e2e_records);
DELETE FROM "DentalRecordTeeth" WHERE "DentalRecordId" IN (SELECT "Id" FROM _e2e_records);
DELETE FROM "DentalRecords"     WHERE "Id" IN (SELECT "Id" FROM _e2e_records);
DELETE FROM "ToothStates"       WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients);

DELETE FROM "AppointmentProcedures"
  WHERE "AppointmentId" IN (SELECT "Id" FROM "Appointments" WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients));
DELETE FROM "Appointments" WHERE "PatientId" IN (SELECT "Id" FROM _e2e_patients);

DELETE FROM "Patients" WHERE "Id" IN (SELECT "Id" FROM _e2e_patients);

\echo 'left behind (all four must read 0):'
SELECT
  (SELECT count(*) FROM "Patients" WHERE "FirstName" = 'E2E') AS patients,
  (SELECT count(*) FROM "Invoices" i WHERE NOT EXISTS (SELECT 1 FROM "Patients" p WHERE p."Id" = i."PatientId")) AS orphan_invoices,
  (SELECT count(*) FROM "DentalRecords" d WHERE NOT EXISTS (SELECT 1 FROM "Patients" p WHERE p."Id" = d."PatientId")) AS orphan_fiches,
  (SELECT count(*) FROM "TreatmentPlans" t WHERE NOT EXISTS (SELECT 1 FROM "Patients" p WHERE p."Id" = t."PatientId")) AS orphan_plans;

COMMIT;
