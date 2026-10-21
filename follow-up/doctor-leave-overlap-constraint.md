# Two simultaneous saves can store overlapping congés for one practitioner

**Feature:** doctor-conges · **Type:** correctness · **Priority:** low · **Created:** 2026-10-20

## The gap
`CreateDoctorLeaveCommand` / `UpdateDoctorLeaveCommand` check for an overlap (`FindOverlappingAsync`) and then
insert. Nothing in the database backs the rule, so two saves for the **same practitioner** landing in the same
instant (the doctor on her phone, reception at the desk) both pass and store overlapping congés. Afterwards every
edit of either is refused as overlapping the other; deleting one clears it. No visit or money is affected.

## Chosen fix
A PostgreSQL exclusion constraint, the repo's own precedent (`EX_Appointments_NoDoubleBooking`; `btree_gist` is
already installed):

```sql
ALTER TABLE "DoctorLeaves" ADD CONSTRAINT "EX_DoctorLeaves_NoOverlap"
  EXCLUDE USING gist ("DoctorId" WITH =, daterange("StartDate", "EndDate", '[]') WITH &&);
```

- In a **new** migration (`AddDoctorLeaves` will be merged by then) — `migrationBuilder.Sql(...)` up, `DROP CONSTRAINT` down.
- `UnitOfWork`'s `23P01` arm currently maps **every** exclusion violation to `ErrorMessages.SlotAlreadyBooked`.
  Read the `ConstraintName` off the `PostgresException` (same reflection it already uses for `SqlState`) and map
  `EX_DoctorLeaves_NoOverlap` to « Ce praticien a déjà un congé sur ces dates. ». 409 is right: it is a race.
- `verify-schema`: add the constraint to the named checks, as `EX_Appointments_NoDoubleBooking` is.

**Why not in the feature itself:** it changes `UnitOfWork`, the save seam every write in the product goes
through, plus a migration — for a race that needs two people saving the same practitioner's congé in the same
instant. **Rejected alternative:** a serializable transaction around check + insert — heavier, and the repo
already standardised on the constraint backstop.

## Verify
`verify-schema` before/after on a DB copy; a unit test of the `ConstraintName` mapping; two parallel POSTs for one
practitioner → one 200, one 409 with the congé sentence.
