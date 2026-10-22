# Run 1 — thumbnail revalidation (blueprint 1.3)

**Date:** 2026-10-05 · **Branch:** `feature/performance-caching` (worktree) · **Plan:** `plan.md` · **Driver:** `walk.mjs` + `api_rows.py` (scratchpad)

**Environment:** API = this branch on `:5099` (`FrontendUrl=http://localhost:3099`, timing log on every request,
the main checkout's Data Protection ring); web = this worktree's `next dev` on `:3099`; browser = own headless
Chrome (`playwright-core`, `channel: chrome`), fresh context, **no `route()`** so the HTTP cache is live.
Docker owned by this session. **Live peers:** `quick-fix-notes` (owns the shared API `:5000`, web `:3000` and the
MCP browser profile — none of them touched), `clinic-management-0b`.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| API-1 | ✅ | 200 · 20 721 bytes · `ETag: "e4d0c2fa06e3c53510b98ae529797f8f"` · `Cache-Control: private, no-cache` |
| API-2 | ✅ | `If-None-Match` with that tag → 304 · 0 bytes · same `ETag` |
| API-3 | ✅ | `/download` → 200 · 20 709 bytes · no `ETag` · no `Cache-Control` · `AuditEntries` (`PatientFileAccess`, patient id, file id in `ChangedFields`) 1 → 2 |
| BR-1 | ✅ | `/patients/{id}/files` grid: 16/16 thumbnails painted; server log: 16 × preview **200** — `shots/BR-1.png` |
| BR-2 | ✅ | full reload: 16/16 painted; server log: 16 × preview **304**; browser sent `If-None-Match` on all 16 (CDP); the page itself saw 200 (Chrome serves the cached body) — `shots/BR-2.png` |
| BR-3 | ✅ | viewer opened on `beb03531-….jpg` (« 1 / 19 »), main image + 15 filmstrip images painted, no error text — `shots/BR-3.png` |
| BR-4 | ✅ | signed out, signed in as the secretary in the same browser: 16/16 painted; server log: 16 × preview **304** — every view reached the server and passed its check for the new user — `shots/BR-4.png` |
| BR-5 | ✅ | `coupe-jpeg-2000.dcm` shown with its type icon; no preview request made for it (also `radio-jpeg-12-bits.dcm`, `bon-labo-couronne-26.zip` in the screenshots) |

**Widths:** 1440 × 900 only — no layout changed. Screenshots read: BR-1, BR-2, BR-4.

## Findings

None.

## Probe fixes made during the run (not product changes)

| What looked broken | Real cause |
|---|---|
| API-3 « no ledger row » | the ledger keys a download on the **patient** id with the file id in `ChangedFields` (`PatientRecordAccessLedger.cs`), not on the file id |
| every browser row timed out waiting for a file name | the list renders a hidden card copy first — addressed `>> visible=true` |
| the zip « never appears » | the patient tab's list is paged (10) and the zip is the 17th newest |
| 0 thumbnails on the patient's « Fichiers » tab | that tab is a **table with type icons** and renders no thumbnail; the grid lives at `/patients/{id}/files` — plan corrected |
| 41 `If-None-Match` on reload | Next.js assets were counted too — narrowed to `/preview` URLs |

## Observations (off-plan)

- On the patient page the `?tab=files` table shows only icons, while `/patients/{id}/files` shows the grid. Not a
  defect of this change; noted because « thumbnails on the Fichiers tab » is a natural thing to expect there.

**Status: GREEN**
