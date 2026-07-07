# Product / UX Review — Clinic Management

**Type:** Whole-app product & ergonomics review (not a code diff)
**Date:** 2026-06-25
**Method:** 4 parallel UX reviewers over the real frontend (`web/`) — onboarding, clinical workflows, documents/files/AI, cross-cutting quality.
**Question asked:** Is it ergonomic, easy to use, not too many steps? What can be optimized for the best UX?

---

## Verdict

The product **idea is strong and the bones are professional** — a coherent oklch design system, shadcn/ui throughout, real API-backed patient/appointment/document flows, and genuinely smart touches (procedure types auto-fill duration + cost + calendar color; inline "new patient" while booking; live A4 document preview with auto-filled letterhead).

But it is **not yet ergonomic for daily clinical use**, and the **first impression actively undermines trust**: the home dashboard, notifications, stock module, and the "AI patient summary" are all **fabricated placeholder data**. A clinic owner who knows they have 3 patients opens the app to "1,248 patients" and fake appointments — they will distrust everything else.

Two things to fix before anything else:
1. **Kill the fiction.** Every screen showing hardcoded data must be wired to the API or hidden.
2. **Fix the lossy/broken flows.** Several flows silently lose user input (document edits, certificat fields, failed chat messages) or drop context (booking from a patient).

After that, the biggest *ergonomic* wins are about **step reduction**: searchable pickers, better time entry, drag-to-reschedule, deep-linked patient context, and a consolidated navigation.

| Severity | Count |
|----------|-------|
| Critical | 9 |
| Major | 17 |
| Minor | 13 |
| Suggestion | 4 |
| **Total** | **43** |

---

## Theme 1 — Trust-breakers (fake/placeholder data) 🔴

These are the #1 reason the app *feels* broken even though much of it works. Fix first — high impact, mostly low effort.

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| 1 | Critical | `app/page.tsx` | All 4 dashboard KPI cards are literal strings (`"12"`, `"1,248"`, `"5"`, `"2"`) with fake deltas. The first screen is fiction. → wire to real aggregates + numeric loading state. |
| 2 | Critical | `components/appointment-list.tsx` | Dashboard "Today's Appointments" is a hardcoded array of fake patients. Dangerous on a scheduling tool. → fetch today's real appointments + empty state. |
| 3 | Critical | `components/notifications-list.tsx` + `dashboard-header.tsx:49` | Notifications fabricated; bell badge permanently red (no handler) → permanent false-alarm fatigue. → wire or remove. |
| 4 | Critical | `components/stock-table.tsx` + `stock-item-form-modal.tsx:60` | Entire Stock module is fake: hardcoded rows, "save" is `console.log`, deletes reset on refresh. → wire to a stock API or gate behind "coming soon". |
| 5 | Critical | `app/patients/[id]/page.tsx:403-417` | "AI-Generated Patient Summary" is a string-concatenation template (`:352`) stamped "Last updated: now" — implies AI analysis that doesn't exist. → wire to the real AI summary endpoint or relabel + drop fake timestamp. |
| 6 | Major | `components/dashboard-header.tsx` | Prominent global search box has no state/handler/results — dead decoration on every page. → implement or remove. |
| 7 | Minor | `dashboard-header.tsx:71-72` | Account-menu "Profile"/"Settings" are dead links. → link or drop. |

---

## Theme 2 — Flows that lose data or context 🔴

Silent data loss is the most damaging class of bug in a medical tool.

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| 8 | Critical | `app/appointments/page.tsx` | "Schedule Appointment" from a patient routes to `/appointments?patientId=…` but the page never reads the param — patient context is dropped, staff must re-pick from a dropdown. → read `patientId`, preselect, auto-open dialog. |
| 9 | Major | `components/document-editor-content.tsx` (~1519, ~1674) | Preview says "Cliquez pour modifier" and makes most fields `contentEditable`, but only the liaison body has an `onBlur` handler — edits to patient name/dates/letterhead are shown then silently discarded. → remove contentEditable from unbound fields or wire them. |
| 10 | Major | `document-editor-content.tsx` (~1031 vs ~461/539) | Certificat saves keys `reason/duration/notes` but reads/exports `doctorOrderNumber/duration/startDate` — a saved medical certificate reopens **blank** for order number & start date. Real clinical data lost. → align save payload to actual fields. |
| 11 | Major | `document-editor-content.tsx` `handleSave` (~1080) | Every Save also fires a redundant background PDF job + extra toast and drops an unrequested PDF into the patient's Files. → make PDF-to-files explicit opt-in; clarify the export buttons. |
| 12 | Minor | `components/ai-chat.tsx:210` | On chat failure the catch resets `messages` and input was already cleared — the user's typed message is gone, must retype. → restore input / keep message with retry. |
| 13 | Minor | `components/patient-record-modal.tsx:147-151` | `amountPaid` is force-overwritten to `cost` on every cost change — a partial payment entered first is wiped. → prefill only when empty. |

---

## Theme 3 — Too many steps (core daily ergonomics) 🟠

The user explicitly asked "not too many steps." These are the repeated, high-frequency frictions.

| # | Sev | Where | Step cost & fix |
|---|-----|-------|-----------------|
| 14 | Major | `create-appointment-dialog.tsx:410-430` | Patient picker is an unsearchable `<Select>` of every patient. The project already ships a `command` Combobox. → searchable patient picker (by name/phone). Saves scrolling on every booking. |
| 15 | Major | `create-appointment-dialog.tsx:480-581`, `edit-appointment-dialog.tsx:393-423` | Time entry = two stacked dropdowns (24-row hour + 60-row minute) per time, twice per appointment. → `<input type="time">` or 5/15-min steps. |
| 16 | Major | `appointment-calendar.tsx` + `edit-appointment-dialog.tsx` | No drag-to-reschedule; moving an appointment 1 hour = a 6-step dialog round-trip. → drag-to-reschedule on the grid and/or +/- time steppers. |
| 17 | Major | `edit-patient-dialog.tsx:284-315` | New patient requires 5 fields (name, gender, DOB, phone) while the booking quick-create needs only a name — inconsistent and blocks walk-in registration. → make gender/DOB optional/default "Unknown". |
| 18 | Major | `app/patients/[id]/page.tsx` → `documents` | No "Create document for this patient" path; the editor takes no `patientId`, so you leave to `/documents`, pick a template, and **re-search the same patient**. → "Nouveau document" action deep-linking `/documents/[type]?patientId=…`. |
| 19 | Minor | `edit-appointment-dialog.tsx:605-621` + `patient-record-modal.tsx` | Marking an appointment "Completed" doesn't lead into recording the procedure; you navigate to the patient and re-pick the same procedure type. → "Record procedure" from the completed appointment, prefilled. |
| 20 | Suggestion | `create-appointment-dialog.tsx` | No double-booking warning — a dentist can be silently double-booked. → soft conflict check against loaded appointments. |

---

## Theme 4 — Onboarding friction 🟠

First-run is where you lose users. From landing → usable dashboard a new owner currently passes through **two extra full-screen spinners, a dead step, and the wrong default path**.

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| 21 | Critical | `app/login/page.tsx:16` → `/setup` | Every authenticated user defaults to the **Create-a-clinic** wizard, even staff who should *join* — they must spot a small ghost link. Risk: duplicate clinics. → land on a neutral "Create vs Join" chooser (the `unauthorized-page.tsx` component already *is* this — just reframe it from "Access Restricted" to a welcome). |
| 22 | Major | `components/setup-wizard.tsx:163-197` | The whole "Working Hours" step (7 day toggles + times) is **never sent to the API** — pure dead friction. Plus dead `doctors` state. → remove the step (or wire + defer to Settings). |
| 23 | Major | `app/setup/page.tsx` + `clinic-guard.tsx` | `getUserStatus` runs on the setup page, then **again** after a hard `window.location.href="/"` reload — two bracketing spinners, cache-busted. → `router.push` + share clinic status via context. |
| 24 | Major | `app/join/page.tsx` | Join is two pages and the code is validated only on the **final** submit — a user can fill their whole profile on a bad code before being told. → validate code on "Continue" (show clinic name), merge into one wizard. |
| 25 | Major | `setup-wizard.tsx` | No draft persistence — refresh mid-wizard wipes ~8 fields back to step 1. → persist to localStorage / warn on unload. |
| 26 | Minor | `setup-wizard.tsx:410-438` | First/last name retyped despite Auth0 already providing `given_name`/`family_name`/`email`. → prefill, let user edit. |
| 27 | Minor | `clinic-guard.tsx` + `use-clinic-access.ts:71-85` | Any status-check error → `hasAccess:false` → red "Access Restricted" identical to genuine no-clinic state, nudging users to create a 2nd clinic. → distinguish transient errors with a retry. |
| 28 | Minor | `setup-wizard.tsx:547-579` | "Next" is silently disabled with no hint of the missing field; "Skip for now" actually *creates* the clinic. → inline validation + relabel. |
| 29 | Minor | `login/page.tsx` vs `middleware.ts` | `returnTo` is set by middleware but ignored after login — deep links don't survive auth. → honor `returnTo`. |

---

## Theme 5 — Navigation & information architecture 🟠

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| 30 | Major | `dashboard-sidebar.tsx` | 9 flat top-level items with overlap. **Records / Documents / Files** are three document-ish destinations; **Files** (global) duplicates per-patient files; **Procedure Types** is config sitting as a primary peer. → group config (Procedure Types, Stock, Settings), consolidate the three doc destinations. |
| 31 | Major | `app/files/page.tsx` vs `patient-files-manager.tsx` vs patient detail Files tab | The "global" Files browser still forces picking one patient first — it's a near-duplicate of the per-patient manager (duplicated helpers too). → make `/files` a true cross-patient view (recent uploads, filename search) or drop it. |
| 32 | Major | `app/records/page.tsx` | `/records`, `/files`, `/patients` all open with a patient grid; Records just shows a read-only modal of what `/patients/[id]` already shows in full. Users won't know where to go. → fold Records into patient detail or make it a real cross-patient clinical search. |
| 33 | Major | `app/page.tsx` (+ stock/settings/documents) | The shell (`ClinicGuard` + sidebar + header + main) is copy-pasted into every page — which is *why* layout already diverges (settings has no padding; documents has a marketing hero). → move the shell into an App Router route-group `layout.tsx`. |

---

## Theme 6 — Consistency, feedback & polish 🟡

| # | Sev | Where | Problem |
|---|-----|-------|---------|
| 34 | Critical | `dashboard-sidebar.tsx` | No mobile/responsive behavior at all (no Sheet/drawer/hamburger). The 256px rail only shrinks to 64px, never hides; content is squeezed on phones/tablets. → mobile drawer below `md`. |
| 35 | Major | multiple | `alert()`/`confirm()` used for feedback in `stock-item-form-modal.tsx:55`, `procedure-types-table.tsx:75`, `app/appointments/page.tsx:69/85/88` (Google sync), and native `confirm()` for file/folder deletes — blocking, off-brand, and the app otherwise standardizes on `sonner` toasts + `AlertDialog`. → convert all. |
| 36 | Major | app-wide | Inconsistent **French/English** mix: nav/dashboard/stock are EN, Documents + wizards are FR. `<html lang="en">` while UI is partly French. → pick FR (target market) or add i18n; at minimum unify nav + chrome. |
| 37 | Major | `notifications-list.tsx` / app-wide | No shared loading/empty/error states and **no `Skeleton` primitive exists** — perceived-speed skeletons impossible. API-backed pages (`patients-table.tsx`) do it right; dashboard widgets have none. → add `Skeleton` + shared `EmptyState`, standardize the triad. |
| 38 | Minor | `app/documents/page.tsx` | Page never renders a saved-documents list despite importing `medicalDocumentsApi` — you can't reopen yesterday's ordonnance except via the patient's Files PDFs. Also its visual style (gradient hero, `text-4xl`) diverges from every other page. → add saved-docs list + align header. |
| 39 | Minor | `edit-patient-dialog.tsx:879-891` | Patient "Flags" section looks actionable but is fully disabled ("not yet supported by the API") — staff think they flagged a high-risk patient. → hide until wired. |
| 40 | Minor | `app/patients/[id]/page.tsx:749-872` | Dental-record notes hidden behind per-row "View notes"; safety-relevant ⚠ notes not visible at a glance; a separate Notes tab duplicates them. → surface important notes inline, merge tab. |
| 41 | Minor | `contexts/sidebar-context.tsx` | Collapse state inits `false` then reads localStorage post-mount → sidebar flashes 256→64px on every load. → lazy SSR-guarded initializer. |
| 42 | Minor | `app/layout.tsx` + `globals.css` | Full `.dark` token set + `dark:` variants everywhere but no theme toggle ever applies `.dark` — dead code. → add a toggle or remove. |
| 43 | Minor | `patient-record-modal.tsx` + `[id]/page.tsx:393/625` | Same record action is launched as "Add Medical Record" (header) and "Add Dental Record" (card) — two labels, one action. → unify. |

---

## AI assistant — specific notes

The AI widget is present but its **value isn't discoverable** and a couple of defaults are risky for a clinic:
- `ai-chat.tsx:24` — generic greeting, no capabilities, no suggested prompts; only `doctorId` sent as context though `ChatRequest` supports `patientId`/`appointmentId`. → add starter chips + pass current-page context.
- `ai-chat.tsx:201/294/71` — **auto text-to-speech reads every reply aloud** (en-US) with no persisted opt-out; speech recognition also hardcoded `en-US` in a French clinic. → opt-in, persisted, set `fr-FR`.
- `ai-chat.tsx:322-545` — ~220 lines of client-side Levenshtein **auto-rewrites typed patient names** to the nearest match above 0.55 with only a toast — a safety risk. → restrict to voice, make it a confirm-suggestion, move server-side.
- `ai-chat.tsx:588` — "Clear chat" uses an `X` icon (reads as close) and wipes history with no confirm; the real dismiss is Minimize. → distinct icon + confirm.

---

## Strengths (keep these)

- **Procedure types are the ergonomic backbone**: selecting one auto-fills appointment duration, prefills record cost, and color-codes the calendar — real repeat-entry savings.
- **Inline new-patient during booking**, click-a-slot-to-prefill, current-time indicator on the calendar.
- **Document editor**: live A4 preview with patient + clinic letterhead auto-filled, sensible per-template fields, good file preview (inline image/PDF, A4 aspect, blob cleanup).
- **Design foundation**: coherent oklch medical-blue tokens, consistent shadcn/ui, well-configured global toaster; `patients-table.tsx` is a clean reference for the loading/empty/error + AlertDialog pattern to copy everywhere.

---

## Recommended order of work (highest ROI first)

1. **Remove the fiction** (Theme 1) — wire dashboard/notifications/stock/AI-summary to the API or hide them. Biggest trust gain, mostly low effort.
2. **Stop losing data** (Theme 2) — fix booking context drop, document contentEditable, certificat key mismatch, chat-on-error.
3. **Shared route-group layout + mobile drawer** (#33, #34) — one consistent, responsive frame; unblocks consistency everywhere.
4. **Step-reducers** (Theme 3) — searchable patient picker, time inputs, drag-to-reschedule, deep-linked patient→document/appointment.
5. **Onboarding** (Theme 4) — neutral chooser, drop the dead working-hours step, validate join code early, prefill from Auth0, client-side nav.
6. **IA consolidation** (Theme 5) + **feedback/i18n polish** (Theme 6) — merge Records/Files/Documents, move config under Settings, replace `alert()`/`confirm()`, add Skeleton/EmptyState, pick one language.
