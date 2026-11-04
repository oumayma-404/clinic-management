# QA plan — story 2 (saves refused kindly + band)

**Stack:** isolated — API :5099 `PublicDemo` on `clinic_demo_local`, web :3099 (`next dev`). Fresh Chrome contexts
via `playwright-core` (no MCP profile). The cabinet holds only Dr Geller (data arrives in story 4), so rows that need
an existing patient, visit or payment are ⏭ with that reason.

| ID | Tier | Layer | Scenario | Expected (observable) |
|----|------|-------|----------|------------------------|
| A1 | A | browser | Band at 1440 | « Cabinet de démonstration · rien n'est enregistré » + link « Créer mon cabinet gratuitement » → `https://app.apexa.tn/signup`, `_blank` |
| A2 | A | browser | Band at 390 | « Démo · rien n'est enregistré » + « Créer mon cabinet »; long text hidden |
| A3 | A | browser | « Nouveau patient » → fill prénom/nom → save | dialog stays open, notice with the demo sentence + « Créer mon cabinet » link, `role=status`, no `role=alert`, typed names still in the inputs |
| A4 | A | browser | Same dialog, save pressed twice | still one notice in the dialog, **no** toast on top (AC-3.3d) |
| A5 | A | browser | Réglages → informations du cabinet → Enregistrer (no dialog) | one neutral sonner toast with the sentence + « Créer mon cabinet » action; not `data-type=error` |
| A6 | A | browser | A5 pressed three times | exactly one demo toast on screen |
| B1 | B | api | `POST /api/patients` with the visitor's token | 403 `{ code: "demo_read_only", error: <sentence> }` |
| B2 | B | api | `GET /api/patients/export` | 403 `demo_read_only` |
| B3 | B | api | `GET /api/patients` (ordinary read) | 200 |
| B4 | B | api | `POST /api/auth/refresh` path stays allowed | covered by B-row of story 1 walk (session survives a reload) |
| C1 | C | browser | Band at 320×640, 390×844, 820×1180, 1180×820, 1440×900, 1536×730 | band ≤ 2 lines (≤ 72 px), header « Mon compte » fully visible below it, no horizontal scroll |
| C2 | C | browser | Print media | band not displayed |
| C3 | C | browser | Coarse pointer (touch context) at 390 | band CTA ≥ 44 px tall |
| D1 | D | browser | `FormErrorBanner` ordinary path: patient dialog saved empty | the red validation banner/feedback still appears (not the demo notice) |
| D2 | D | browser | Story 1 walk (`walk.mjs`) re-run | entry, two visitors, doors, logout, expiry all still green |
| D3 | D | sql | `IdempotencyRecords` in `clinic_demo_local` after the walk | 0 rows |
| D4 | D | — | `HostedMultiTenant`: ordinary save + error banner unchanged | ⏭ no hosted stack here; covered by `Another_Deployment_Is_Untouched` + the identity-keyed branch |
