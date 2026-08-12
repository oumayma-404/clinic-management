# Follow-up Items

Track incomplete work, bugs, and technical debt across all features.

## Open Items

| Item | Feature | Type | Priority | Created |
|------|---------|------|----------|---------|
| [A write path for the per-clinic TTN identity (`set-clinic-ttn-identity` verb)](./ttn-per-clinic-identity-write-path.md) | multi-tenant-cloud | incomplete | high | 2026-08-06 |
| [Verify/regenerate the CNAM EF migration with the EF tool](./cnam-migration-ef-verify.md) | cnam-bulletin-soins | debt | high | 2026-07-17 |
| [Validate & correct the BS1 PDF against the official form](./cnam-bs1-pdf-fidelity.md) | cnam-bulletin-soins | incomplete | high | 2026-07-17 |
| [Admin editing of CNAM nomenclature + reimbursement rates](./cnam-nomenclature-admin-editing.md) | cnam-nomenclature-lookup | enhancement | medium | 2026-07-17 |
| [Prosthesis (Prothèses dentaires) flow on the bulletin](./cnam-prosthesis-flow.md) | cnam-bulletin-soins | enhancement | medium | 2026-07-17 |
| [BS1 bulletin lifecycle status tracking](./cnam-bulletin-lifecycle-status.md) | cnam-bulletin-soins | enhancement | medium | 2026-07-17 |
| [CNAM identity backfill for existing patients](./cnam-identity-backfill.md) | cnam-bulletin-soins | enhancement | low | 2026-07-17 |
| [Complete bilingual FR/AR labels (CNAM UI + PDF)](./cnam-bilingual-fr-ar.md) | cnam-bulletin-soins | enhancement | low | 2026-07-17 |
| [Conventionné pathway: bordereau + télétransmission](./cnam-conventionne-bordereau.md) | cnam-bulletin-soins | enhancement | low | 2026-07-17 |
| [BS1 overlay — deferred review findings (font bundling, job failure surfacing, non-retryable fail-fast)](./cnam-bs1-overlay-deferred-review.md) | cnam-bs1-official-overlay | debt | medium | 2026-07-20 |
| [Embed the real BS1 in the document-editor preview](./cnam-bs1-live-preview.md) | cnam-bs1-official-overlay | enhancement | medium | 2026-07-20 |
| [Patient merge — dropped in favour of duplicate prevention; design already done](./patient-merge.md) | audit-sections-3-to-10 | enhancement | low | 2026-07-28 |
| [The archive restore's query path has no automated gate — add a `restore-dry-run` verb](./archive-restore-real-database-checks.md) | clinic-data-archive-and-restore | correctness | high | 2026-08-12 |
| [The sidecars' secrets still reach them as environment variables (FR-3.10, second half)](./hosted-secrets-to-files.md) | hosted-security-hardening | debt | medium | 2026-08-12 |

## Completed Items

| Item | Feature | Type | Completed |
|------|---------|------|-----------|

## How to Work on These

Start a fresh session and say:
```
Work on follow-up: follow-up/[item].md
```
(or run `/follow` and pick the item). Claude reads the context and continues from where you left off.

> These items were consciously scoped out of the CNAM bulletin work (see
> `features/cnam-bulletin-soins/spec.md` and `features/cnam-nomenclature-lookup/spec.md` — "Out of Scope").
> Roughly ordered: the two **high** items are pre-merge / correctness; the **medium** items are the next
> feature increments; the **low** items are polish or pathway-dependent (bordereau is conventionné-only).
