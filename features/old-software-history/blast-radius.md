# Blast radius — old-software history

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Patient` + `Occupation` / `MaritalStatus` / `CountryOfOrigin` | 3 nullable columns + setters | `PatientConfiguration`, `PatientDto` + `PatientMappingExtensions`, `CreatePatientCommand` + `PatientFromRequest`, `UpdatePatientCommand`, `ExportTables`, `PatientDossierPackager` | must change — all in this commit |
| 2 | same | — | cabinet archive / restore, clinic purge | unaffected — both derived from the EF model |
| 3 | same | — | CSV import (`PatientImportFields` + row reader), demo seed | must change — the import reads the 3 export columns back (round trip tested); demo seed unaffected |
| 4 | `UpdatePatientCommand` (+3 keys) | tri-state DTO (null = unchanged, "" = clear) | every existing caller omits them; `edit-patient-dialog.tsx` sends | must change — the dialog reads back AND sends all 3 |
| 5 | `PatientLinkedDataCounts` (+`LegacyHistoryEntries`) | positional record, shared guard | `PatientRepository`, `PatientDeletionBlockers`, delete, merge, deletion check, calendar-import revert | must change (repo + blockers); must re-test delete & merge refusals |
| 6 | new `PatientLegacyHistoryEntry` | clinical child table | `ApplicationDbContext` (DbSet + tenant filter), audit interceptor, `AuditLabels`, archive restore labels, `verify-schema` clinic-matches-patient check, access/tenant/audit guard tests | must change — the guards say which |
| 7 | web `Patient` type (+3) | wire type | `edit-patient-dialog.tsx`, patient page fields, `patient-merge.ts` | must change (dialog, page); must re-test merge |
| 8 | patient page `PatientSection` + tab bar (+`legacyHistory`) | page sections, tab list | deletion dialog's `?tab=` links (`patients-table.tsx`) | must change — `?tab=legacy-history` must open the tab; must re-test tab bar at 320 px |
| 9 | `FileTypeCatalog` (+ mp4/m4v · mov · webm · avi) | the upload allow-list, served to every door | upload policy (picker `accept`, client pre-check), `FileUploadValidator`, `SignatureIndex` cross-check, residency, two tests that used « mp4 » as the unknown example | must change (the two tests); unaffected — the profile-image / PDF / CSV doors list their own formats |
| 10 | the CSP (`media-src`) | mirrored set, 5 copies | `ContentSecurityPolicyAgreementTests` | must change — all five in this commit |
| 11 | `file-kind.ts` `PreviewMode` / `use-file-preview` / preview dialog / `lib/files/preview.ts` | viewer + stand-in builder | upload queue, coffre ingest, camera import (`buildPreview`) | unaffected — video branch only; must re-test the drawer and the viewer |
