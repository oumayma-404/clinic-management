# CNAM BS1 overlay — deferred review findings

> **Type:** technical-debt
> **Priority:** medium
> **Created:** 2026-07-20
> **Feature:** cnam-bs1-official-overlay
> **Source:** `features/cnam-bs1-official-overlay/reviews/feature-review.md` (findings #1, #2, #7)

## Summary
`/apply-review-fixes` applied 9 of the 12 challenged findings from the BS1-overlay feature review
directly in the working tree. Three were deferred because each needs a change wider than a surgical
review-fix (a new bundled binary, or a cross-cutting change to a shared API-layer background job that
this feature did not touch). Each is captured below **with the chosen remedy**, not just options.

---

## Finding #1 (Major) — bulletin-cnam hard-depends on an OS-installed font
**File:** `api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs`

**Problem.** Core PdfSharp ships no fonts, so `Bs1FontResolver` probes four hard-coded OS font paths
(Arial on Windows; Liberation/DejaVu on Linux). If none exists, generation throws. The old QuestPDF
bulletin path had no such dependency (SkiaSharp embeds its own fonts). The branch is **not** gated by
`Auth:Mode`, so a Cloud/Docker deploy on a slim base image (`mcr.microsoft.com/dotnet/aspnet:8.0` ships
none of these fonts) now throws on every bulletin-cnam render where it previously succeeded.

**Not urgent on the real target.** The primary Windows-LAN (Local) deployment always has
`C:\Windows\Fonts\arial.ttf`, so this is a latent Cloud/slim-container regression, not a Local defect.

**Chosen remedy.** Bundle **Liberation Sans** (regular + bold) as an embedded/`Content` Infrastructure
asset — same mechanism as `Assets/BS1.pdf` (`CopyToOutputDirectory=PreserveNewest`) — and have
`Bs1FontResolver` load the bundled bytes **first**, keeping the OS-font probe only as a fallback. This
removes the runtime dependency entirely (works in a bare container) and makes the render deterministic.
- *Why Liberation Sans:* metric-compatible with Arial (so the calibrated coordinates still land), and
  redistributable (SIL OFL / GPL font-exception).
- *Rejected — bundle Arial:* not redistributable (Microsoft licensing); cannot ship it in the repo.
- *Rejected — documentation-only (add the font prerequisite to the publish checklist for both modes):*
  leaves the Cloud slim-container regression latent; acceptable only as an interim stop-gap.

**Still to validate:** confirm the bundled TTF's metrics keep every field in its box (re-run the visual
calibration), and confirm the licence file is included alongside the font asset.

---

## Finding #2 (Minor) — AC-6 French message reaches an operator on only one of the two PDF paths
**File:** `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs` (surfaces in
`api/ClinicManagement.API/BackgroundJobs/PdfGenerationJob.cs`)

**Problem.** AC-6's *core* ("fail fast, never a blank/malformed PDF") is met on **both** paths — the
renderer throws and no fallback PDF is produced. What's weak is only the *surfacing* of the French
message: the direct-download path (`MedicalDocumentsController` generate-pdf-download) returns it as a
`BadRequest`, but the primary "generate & attach" path enqueues `PdfGenerationJob`, returns `200 {JobId}`
immediately, and on failure the message lands only in server logs / the loopback-only Hangfire dashboard.
A LAN receptionist sees a queued "success" and a PDF that never attaches.

**Why deferred.** The log-and-rethrow-into-Hangfire behavior lives in `PdfGenerationJob.cs`, which this
feature did not change — it is pre-existing behavior shared by **every** document type, not a regression
this feature introduced.

**Chosen remedy.** Persist the terminal failure reason onto the document/job status the UI polls, so both
paths honor the fail-fast contract end-to-end: add a failure-reason field to the medical-document
generation status, have `PdfGenerationJob`'s terminal `catch` write it (instead of only logging), and
surface it in the document editor's generation-status poll. This is a cross-cutting change spanning the
job, the document entity/status, and the frontend poll — hence a separate ticket.
- *Rejected — make the async path synchronous:* defeats the point of the background job (large docs).

---

## Finding #7 (Minor) — deterministic BS1 fail-fast conditions are retried 3× by Hangfire
**File:** `api/ClinicManagement.API/BackgroundJobs/PdfGenerationJob.cs` (`[AutomaticRetry(Attempts = 3)]`)

**Problem.** The BS1 fail-fast conditions (missing/unreadable `Assets/BS1.pdf`, no usable font) are
deterministic and non-transient — they cannot self-heal between retries. Under `[AutomaticRetry(3)]` a
missing-asset bulletin is re-rendered and re-thrown 3× with back-off, wasting minutes, delaying the
terminal "failed" state, and triple-logging the same exception. The retry attribute is pre-existing and
shared by all document types, but this feature introduces the first deterministic, non-retryable failure
mode into that job.

**Chosen remedy.** Introduce a dedicated non-retryable exception type (e.g.
`NonRetryablePdfGenerationException`) that the BS1 renderer throws for its deterministic fail-fast cases,
and exclude it from Hangfire retry via a small `ElectStateFilter` (or a `catch` in the job that marks the
job failed without rethrowing for that type). A deterministic config error then fails **once**, fast.
- *Rejected — lower `Attempts` globally:* weakens transient-failure resilience for every other document
  type that genuinely benefits from retry (DB blips, transient storage errors).

**Depends on** the same `PdfGenerationJob` surface as #2 — worth doing together.

---

## Key Files
| File | Relevance |
|------|-----------|
| `api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs` | #1 — font loading |
| `api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs` | #1 fonts, #7 exception source |
| `api/ClinicManagement.API/BackgroundJobs/PdfGenerationJob.cs` | #2, #7 — shared job retry + failure surfacing |
| `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs` | #2 — the two generation paths |
