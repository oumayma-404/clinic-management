# Feature Review: cnam-bs1-official-overlay

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-20
**Challenged Date:** 2026-07-20
**Parent Branch:** feature/windows-desktop-app (feature is uncommitted working-tree work on the reused branch)
**Merge Base:** n/a — reviewed the working tree (feature files are untracked/unstaged; diff assembled via `git add -N` + `git diff HEAD`, then index restored)
**Files Reviewed:** 6 changed files (+875, -76) — `.csproj`, `Bs1FontResolver.cs` (new), `CnamBs1BulletinRenderer.cs` (new), `PdfGenerationService.cs`, `CnamBs1BulletinRendererTests.cs` (new), `web/components/document-editor-content.tsx`

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 13 |
| Confirmed | 10 |
| Confirmed (adjusted) | 2 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 12 |

Each finding below was verified by reading the full source (not just the diff). Adjustments and the one dismissal are explained inline.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs
- **Line:** 57
- **Anchor:** `Bs1FontResolver.RegularCandidates` / `EnsureInstalled` (throw)
- **Comment:** The bulletin-cnam path now hard-depends on an OS-installed sans-serif font (Arial on Windows; Liberation/DejaVu on Linux) probed from four hard-coded paths. If none exist, `EnsureInstalled` throws and PDF generation fails. The old QuestPDF bulletin path had **no** such dependency (QuestPDF renders via SkiaSharp with its own embedded fonts), so it produced a valid PDF even in a bare container. This branch is **not** gated by `Auth:Mode` (`PdfGenerationService.cs:34` branches on document type only), so it also runs in Cloud: a Cloud/Docker deploy on a slim base image (`mcr.microsoft.com/dotnet/aspnet:8.0` ships none of these fonts) will now throw on every bulletin-cnam render where it previously succeeded — a runtime-only regression. The primary Windows-LAN (Local) target does have `C:\Windows\Fonts\arial.ttf`, so it's fine there. **Fix:** bundle a font file as a `Content`/embedded asset and load it deterministically (matching how `BS1.pdf` itself is bundled), instead of probing OS font stores — or document the font prerequisite and verify it in the publish checklist for **both** modes.

### Finding 2
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs
- **Line:** 34
- **Anchor:** `PdfGenerationService.GeneratePdfFromDocumentDataAsync` (bulletin-cnam branch)
- **Comment:** AC-6's "clear French operator message" reaches an operator on only one of the two generation paths. The direct-download path (`MedicalDocumentsController` generate-pdf-download, line 278) catches and returns `BadRequest($"Error generating PDF: {ex.Message}")`, surfacing the French text. But the primary "generate & attach" path enqueues `PdfGenerationJob` and immediately returns `200 {JobId}`; when the BS1 asset or system font is missing the `InvalidOperationException` is logged+rethrown inside the Hangfire job and the French message lands only in server logs / the Hangfire dashboard (loopback-only in Local — a LAN receptionist never sees it). The attach flow then looks like a silent no-op (queued "successfully", PDF never attaches). **Fix:** persist the terminal failure reason onto the document/job status the UI polls, so both paths honor the fail-fast contract.
- **Challenge note:** Severity lowered Major → Minor. The AC-6 *core* ("fails fast … never a blank or malformed PDF") is met on **both** paths — the renderer throws, no blank/fallback PDF is ever produced. What's weak is only the *surfacing* of the French message on the async attach path, and that log-and-rethrow-into-Hangfire behavior lives entirely in **`PdfGenerationJob.cs`, which this feature did not change** — it is pre-existing behavior shared by every document type, not a regression this feature introduced. The fix is a worthwhile cross-cutting improvement, not a delivered defect, hence Minor.

### Finding 3
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 156
- **Anchor:** `CnamBs1BulletinRenderer.StampAssureAndMalade` (Identifiant Unique digit comb)
- **Comment:** The IDU is stamped one character per cell over exactly 10 fixed cells (`for i < idu.Length && i < IduCellCentersX.Length`), and the value is only `.Trim()`'d — never digit-filtered. The FE CNAM identifier field is unvalidated free text. Two AC-1 ("fields in the correct boxes") risks: (1) an IDU longer than 10 chars is silently truncated with no indication; (2) any spaces/dashes/letters the user types are placed into digit cells as if digits, shifting every subsequent character out of box alignment. **Fix:** strip non-digits before combing (and ideally guard/surface the overlong case) so digits land in their boxes regardless of input formatting.

### Finding 4
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs
- **Line:** 16
- **Anchor:** `Bs1FontResolver.EnsureInstalled` / `_installed`
- **Comment:** Double-checked locking over a plain `private static bool _installed;`. The fast-path read (`if (_installed) return;`) runs outside the lock, the field isn't `volatile`, and `_regularBytes`/`_boldBytes` are read lock-free in `GetFont`. Under the ECMA-335 memory model a second thread can observe `_installed == true` without an acquire barrier and read a stale `null` for the font bytes → `GetFont` returns null → PdfSharp stamps a fontless page. Real on ARM; benign on the x86/x64 Windows target (hence Minor). **Fix:** mark `_installed` `volatile` (or use `Volatile.Read/Write`) so publication of the byte arrays happens-before any lock-free observation of `_installed`.

### Finding 5
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs
- **Line:** 62
- **Anchor:** `Bs1FontResolver.EnsureInstalled` (`GlobalFontSettings.FontResolver ??= …`)
- **Comment:** `_installed = true` is set unconditionally even when `??=` did **not** install our resolver because some other resolver was already set process-wide. In that case our `bs1-sans` faces are never resolvable and, instead of the intended fail-fast French message, you get an opaque PdfSharp render-time failure / wrong glyphs — the AC-6 fail-fast guarantee is silently defeated. No other PdfSharp consumer exists today (QuestPDF uses SkiaSharp), so this is latent, not active. **Fix:** after the `??=`, assert the active resolver is ours and throw the same French `InvalidOperationException` (or at least log) if a foreign resolver is present. *(Raised by 3 agents.)*

### Finding 6
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 370
- **Anchor:** `Bs1Model.ParseActs` (`catch (JsonException)`)
- **Comment:** The catch scope itself is correct (only `JsonDocument.Parse` throws it; `Bs1Act.From` uses safe `TryGetProperty`/`TryParse`), so this is not a mis-scoped swallow. The concern is behavioural: malformed `acts` JSON is silently swallowed to an empty act list, producing a fully-populated, official-looking CNAM reimbursement bulletin with **zero acts** — worse than a clear failure, since it can be printed/submitted looking valid, and the corruption is completely invisible. The no-throw behaviour itself is intentional and test-covered (progress.md defers a "malformed → empty, not a throw" test), so the actionable ask is narrow. **Fix:** at minimum log a Warning here so an operator/dev can discover why a bulletin came out act-less.

### Finding 7
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/BackgroundJobs/PdfGenerationJob.cs
- **Line:** 28
- **Anchor:** `PdfGenerationJob.GenerateAndAttachPdfAsync` (`[AutomaticRetry(Attempts = 3)]`)
- **Comment:** The BS1 fail-fast conditions (missing/unreadable `Assets/BS1.pdf`, no system font) are deterministic and non-transient — they cannot self-heal between retries. Under `[AutomaticRetry(Attempts = 3)]` a bulletin-cnam with a missing asset is re-rendered and re-thrown 3× with back-off, wasting minutes, delaying the terminal "failed" state, and emitting the same exception to the log 3× (double-logged by `PdfGenerationService` + the job each attempt). The retry attribute is pre-existing on the job (shared by all document types), but this feature introduces the first deterministic, non-retryable failure mode into it. **Fix:** mark these fail-fast conditions non-retryable for the bulletin path (dedicated exception type excluded from retry, or short-circuit when the asset is known-missing) so a deterministic config error fails once and fast.

### Finding 8
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 219
- **Anchor:** `CnamBs1BulletinRenderer.StampActs`
- **Comment:** `var baselineY = ActsFirstRowTopY + row * ActsRowHeight + 15;` — every other coordinate/offset in the file is a named `const`, but this `+ 15` (the baseline offset within a row) is a bare literal, reading as an unexplained magic number amid an otherwise fully-named coordinate system. **Fix:** extract a named constant (e.g. `ActRowBaselineOffsetY = 15`).
- **Challenge note:** Severity lowered Minor → Suggestion. Pure readability nit with no behavioral or correctness impact — consistent with the other cosmetic Code Quality items already rated Suggestion.

### Finding 9
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 25
- **Anchor:** `CnamBs1BulletinRenderer` font properties (`FieldFont`/`TableFont`/…)
- **Comment:** The `XFont` members are expression-bodied properties that construct a new `XFont` on every access (e.g. `TableFont` is evaluated 5–6× per act row). Deferring construction until after `EnsureInstalled()` is a legitimate reason not to use static field initializers, but per-access re-allocation in the stamping loops is avoidable. **Fix:** cache the needed fonts in locals at the top of `StampActs`/`StampAssureAndMalade` (built once per render) and reuse across the loop.

### Finding 10
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 123
- **Anchor:** `CnamBs1BulletinRenderer.Render` (overflow-append loop)
- **Comment:** The overflow loop re-opens the template (`PdfReader.Open(new MemoryStream(templateBytes), Import)`) on every iteration just to import `Pages[0]`. A single Import-mode document can serve multiple `AddPage(importSource.Pages[0])` calls, so the open/parse can be hoisted once before the loop (with `using`), avoiding re-parsing the whole PDF per extra page. Minor efficiency/clarity win; behavior unchanged. (Only exercised for >12 acts, i.e. 3+ pages.)

### Finding 11
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs
- **Line:** 246
- **Anchor:** `CnamBs1BulletinRenderer.StampCadreDeSoins`
- **Comment:** The `switch` has two arms for the same option — `case "Suivi de grossesse":` and `case "Suivi de Grossesse":`. Verified against the FE: `document-editor-content.tsx:1664` only ever emits `"Suivi de grossesse"` (lowercase g), so the second arm is unreachable dead code. **Fix:** either drop the dead branch, or if tolerating casing drift is the intent, replace with a single case-insensitive comparison (and apply the same reasoning to the other `switch`es for consistency).

### Finding 12
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs
- **Line:** 84
- **Anchor:** `Bs1FontResolver.LoadFirstAvailable` (`catch {}`)
- **Comment:** The empty catch is justified as "try next candidate" and ultimately fails fast, so it doesn't hide the terminal error. But when a candidate font **exists** yet is unreadable (locked/permission on `arial.ttf`), the swallow collapses that into the "aucune police système … n'a été trouvée" ("none found") message, misdirecting the fix (operator installs a font that's already present rather than checking permissions). **Fix:** capture the last swallowed exception and, if `_regularBytes == null`, include "(police présente mais illisible: …)" in the fail-fast message, or log the swallowed IOException at Warning.

## Dismissed Findings

### (was Finding 13) — overflow act pages appended after the page-2 "Cadre de soins"
- **Verdict:** Dismissed (false positive / non-defect)
- **File:** api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs (Line 123)
- **Reason:** The original reviewer's own text concludes this **"literally satisfies AC-4"**, that only Pages[0] is duplicated (cadre correctly not re-stamped), that it is **"not a defect"**, and that it is **"Acceptable as-is."** Verified: `Render` stamps page 0 + page 1 then appends overflow copies of page 0 only — AC-4 ("additional BS1 page copies are appended so no act is dropped") is fully met. Reading order (`[id+acts, cadre, id+acts overflow]`) is a purely aesthetic preference with no acceptance-criterion behind it. A self-described non-defect note is not an actionable finding; removed to keep the report actionable. No change recommended.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 6 |
| Suggestion | 5 |
| **Total** | 12 |
