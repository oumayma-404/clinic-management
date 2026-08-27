# Feature Specification: CNAM BS1 Live Preview

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-20
**Scope:** FE
**Feature:** In the document editor, show the real generated CNAM BS1 PDF in the preview pane for `bulletin-cnam` (auto-refreshing as the form is filled) instead of the generic letterhead preview, so what the user sees matches the downloaded document.

## Overview
The document-editor right-panel preview (`document-editor-content.tsx`) renders the same generic letterhead document ("Paris, le…", generic patient block, custom acts summary) for every document type. For `bulletin-cnam` this looks nothing like the official BS1 that `CnamBs1BulletinRenderer` actually produces. This feature replaces the preview — **only for `bulletin-cnam`** — with the real generated BS1 PDF embedded in the pane, regenerated (debounced) as the user edits, using the existing `medicalDocumentsApi.generatePdfForDownload(buildDocumentData())` (no save required). All other document types keep the current generic preview unchanged.

## What Changes
- For `documentType === "bulletin-cnam"`, the preview pane embeds the real BS1 PDF (blob → object URL in an `<iframe>`/`<embed>`) instead of the editable letterhead markup.
- The embedded preview auto-refreshes **~800ms after editing pauses** (patient selection, care type, APCI code, and any acts-table change), calling `generatePdfForDownload(buildDocumentData())`.
- An in-flight generation is cancelled/superseded when a newer edit lands; the previous object URL is revoked so blobs don't leak.
- The preview shows loading, error, and no-patient states.
- The other four document types (prescription, liaison, honoraires, certificat) keep the existing generic preview with no change.

## Acceptance Criteria
- **AC-1:** Selecting a patient and editing a `bulletin-cnam` shows the actual BS1 PDF in the preview pane; the rendered form matches what the Download button produces for the same inputs.
- **AC-2:** After the user changes any bulletin field (care type, APCI code, an act row, patient), the embedded preview updates automatically once editing pauses (~800ms debounce) — no manual refresh needed.
- **AC-3:** Rapid consecutive edits result in the preview settling on the **latest** input (a superseded/in-flight render never overwrites a newer one), and object URLs from prior renders are revoked.
- **AC-4:** While a render is in flight the pane shows a loading indicator; if generation fails, a French error message is shown (reusing the existing download error text) and the pane does not crash the editor.
- **AC-5:** With no patient selected, the pane shows a neutral "select a patient" state and does not call the API.
- **AC-6:** For every non-`bulletin-cnam` document type, the preview pane is byte-for-byte the current generic letterhead preview (no behavior change).

## Out of Scope
- Changing the BS1 renderer or the download/generation endpoints (server-side unchanged).
- Inline click-to-edit inside the preview for `bulletin-cnam` (fields are edited in the left panel).
- Any change to how the document is saved or to the generic preview of the other four types.
- The already-shipped fixes (malade DOB, acts-table overflow) — done separately.

## Edge Cases (Critical only)
- **Debounce vs. unmount/navigation:** a pending debounced render must not run after the editor unmounts or the type changes away from `bulletin-cnam` (guard state updates; clear the timer).
- **Generation error mid-typing:** a failed render keeps the last good preview (or the error state) rather than blanking permanently; the next successful edit recovers.
- **Required-field gaps:** `buildDocumentData()` returning null (no patient) short-circuits without an API call (AC-5).
