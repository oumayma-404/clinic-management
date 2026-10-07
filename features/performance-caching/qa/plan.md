# QA plan — thumbnail revalidation (blueprint 1.3)

**Change under test:** `GET /api/patients/{id}/files/{fileId}/preview` now sends `ETag` +
`Cache-Control: private, no-cache` and answers **304** to a matching `If-None-Match`, after the tenant check.
No rendering file changed — this pass checks that the browser's own HTTP cache turns the 304 back into a
painted image through `apiGetBlob` → `file-thumbnail.tsx`.

**Environment (nothing shared is touched):**

| Tier | Where | Why not the shared one |
|---|---|---|
| API | this branch, `:5099`, `FrontendUrl=http://localhost:3099`, `Diagnostics__SlowRequestMs=0` | `:5000` belongs to peer `quick-fix-notes` and runs other code |
| Web | this worktree's `next dev`, `:3099`, own `.next`, `NEXT_PUBLIC_API_URL=http://localhost:5099/api` | `:3000` is the peer's and talks to `:5000` |
| Browser | own Chrome via `playwright-core`, fresh context, **no `route()`** | the MCP profile is held by a peer; Playwright disables the HTTP cache once any route is registered — the very thing under test |

**Patient:** Emna Belhadj `d3220000-0000-4000-8000-000000000003` — 19 files at the root, 16 with a stand-in,
3 without (`coupe-jpeg-2000.dcm`, `radio-jpeg-12-bits.dcm`, `bon-labo-couronne-26.zip`).
**Accounts:** admin `salma.benyoussef@…` (TOTP), secretary `qa.secretary@ibnkhaldoun.test` (password only).

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| API-1 | A | api | GET a preview | 200 · `ETag: "<32 hex>"` · `Cache-Control: private, no-cache` · body > 0 bytes |
| API-2 | A | api | same GET with `If-None-Match: <that tag>` | 304 · empty body · same `ETag` |
| API-3 | D | api + sql | GET the original's `/download` | 200 · **no** `ETag` · **no** `Cache-Control` from the app · one new access-ledger row for that file (`AuditEntries`) |
| BR-1 | A | browser | admin opens the patient's file manager `/patients/{id}/files` (grid, the default view) | every tile whose file has a stand-in paints (`img.naturalWidth > 0`); server log shows one preview **200** per such tile |
| BR-2 | A | browser | full reload of the same page | the same tiles paint again; server log shows the previews answered **304** (not 200); the browser sent `If-None-Match` (CDP) |
| BR-3 | D | browser | click a card (JPEG) to open the in-app viewer | the viewer shows a painted image (no error, no grey box); its filmstrip thumbnails paint |
| BR-4 | B | browser | sign out, sign in as the secretary in the same browser, open the same file manager | tiles paint; server log shows the preview requests **reached the server** for the new user (304 or 200 — never served unseen from cache) |
| BR-5 | B | browser | a file with no stand-in (`coupe-jpeg-2000.dcm`, on page 1) | its tile shows the type icon, and no preview request is made for it |

**Widths:** none of the layout changed, so a single 1440 × 900 screenshot per browser row is the eye check.
**Mutations:** none, except API-3's ledger row, which is what a download is supposed to write.

**Correction during run 1 (probe, not product):** the thumbnails live on `/patients/{id}/files` (grid). The
patient page's `?tab=files` is a table with type icons and renders no thumbnail, so BR-1/2/4 moved there.
