# Small-Feature Prompts — derived from `ux-review.md`

Each block below is a **paste-ready prompt for `/define-small-feature`**. They break the UX review's 43 findings into cohesive, independently-shippable units. Run them one at a time: `/define-small-feature` then paste the prompt. Priority: **P1** = trust/correctness (do first), **P2** = core ergonomics, **P3** = IA/polish. Pure one-liners are listed at the end as `/quick-fix` candidates.

---

## P1 — Trust & correctness (fix the fiction and the data loss)

### SF-01 — Live dashboard (replace hardcoded home screen)
```
Define a small feature: make the dashboard home page (app/page.tsx) show REAL data instead of hardcoded values. Today the 4 KPI cards are literal strings ("12", "1,248", "5", "2") with fake deltas in app/page.tsx, and components/appointment-list.tsx renders a static array of fake patients for "Today's Appointments". 
Desired: KPI cards fetch real aggregates (today's appointment count, total patients, and two more meaningful counts) and "Today's Appointments" fetches today's real appointments via the existing appointments API/useAppointments hook. Add loading (numeric skeleton/spinner) and empty states ("No appointments today").
In scope: app/page.tsx, components/appointment-list.tsx, components/stats-card.tsx, any small read endpoints/aggregation needed. Out of scope: notifications panel (separate feature), charts.
```

### SF-02 — Persist stock inventory (wire Stock to the API)
```
Define a small feature: make the Stock module actually persist. Today components/stock-table.tsx holds a hardcoded 10-row array in useState and components/stock-item-form-modal.tsx "saves" via console.log, so adds/deletes vanish on refresh. The domain already has a StockItem entity, IStockItemRepository, and StockItemRepository.
Desired: a real Stock API (list/create/update/delete) and a typed client module under web/lib/api; the table loads from it; the form modal creates/updates via it with toast feedback and inline validation (no alert()); deletes use AlertDialog; surface a low-stock indicator/filter.
In scope: new StockController + commands/queries/DTOs in the API, web/lib/api/stock.ts, components/stock-table.tsx, components/stock-item-form-modal.tsx, app/stock/page.tsx. Out of scope: stock movements/audit history, supplier management.
```

### SF-03 — Wire notifications (list + bell) or gate it
```
Define a small feature: make the notifications experience real. Today components/notifications-list.tsx renders a fabricated array and components/dashboard-header.tsx (~line 49) shows a permanently-red unread dot with no click handler. The domain has a Notification entity, INotificationRepository, NotificationType/Status enums.
Desired: a notifications API (list current user's/clinic's notifications, mark-as-read) + web client; the dashboard panel and a header bell dropdown render real notifications with unread count; the red dot reflects actual unread state; empty/loading states. If real notification generation is out of reach now, instead hide the panel + badge behind a feature flag rather than showing fake data — decide during definition.
In scope: NotificationsController + queries, web/lib/api/notifications.ts, components/notifications-list.tsx, components/dashboard-header.tsx. Out of scope: email/SMS sending, notification preferences.
```

### SF-04 — Real AI patient summary
```
Define a small feature: replace the fake "AI-Generated Patient Summary" on the patient detail page. Today app/patients/[id]/page.tsx (~403-417) shows aiSummary built by string concatenation (~line 352) stamped "Last updated: {today}", implying AI analysis that doesn't exist.
Desired: the card calls the real AI/summary endpoint (AIController / IPatientSummaryService) on demand or on load, shows a loading state, a real "generated at" timestamp, and a refresh action; if the AI call fails, show a graceful fallback. If wiring the AI path is too big, alternative is to relabel it "Patient overview" and drop the fake timestamp — decide during definition.
In scope: app/patients/[id]/page.tsx, a summary endpoint + web client, loading/error states. Out of scope: changing the underlying AI provider.
```

### SF-05 — Preserve patient context when booking from a patient
```
Define a small feature: fix the broken "Schedule Appointment" deep-link from a patient. Today app/patients/[id]/page.tsx (~line 397) navigates to /appointments?patientId=<id>, but app/appointments/page.tsx never reads the param, so the patient is dropped and staff must re-pick them from a dropdown.
Desired: the appointments page reads patientId (and optionally an "open=create" flag) from the URL via useSearchParams, auto-opens CreateAppointmentDialog, and preselects that patient. 
In scope: app/appointments/page.tsx, components/create-appointment-dialog.tsx (accept an initial patientId prop), app/patients/[id]/page.tsx link. Out of scope: the searchable-picker rework (separate feature SF-07).
```

### SF-06 — Document editor data-integrity fixes
```
Define a small feature: stop the document editor from silently losing data. Three issues in components/document-editor-content.tsx: (1) the preview advertises "Cliquez pour modifier" and makes clinic/patient/date fields contentEditable, but only the liaison body has an onBlur handler (~1674) — edits to every other field are shown then discarded (~1519); (2) for "certificat" the save writes keys reason/duration/notes (~1031) but the editor reads/exports doctorOrderNumber/duration/startDate (~461/539), so a saved certificat reopens blank for order number and start date; (3) handleSave (~1080) always fires a redundant background PDF job + extra toast and drops an unrequested PDF into the patient's Files.
Desired: remove contentEditable from fields that aren't wired to state (or wire them); align the certificat save payload with the actual fields it reads/exports; make "save PDF to patient files" an explicit opt-in distinct from "Télécharger PDF".
In scope: components/document-editor-content.tsx and the medical-documents save/generate path. Out of scope: redesigning templates, currency (separate quick-fix).
```

---

## P2 — Core ergonomics (reduce steps in daily tasks)

### SF-07 — Searchable patient picker in appointment dialogs
```
Define a small feature: replace the unsearchable patient <Select> in components/create-appointment-dialog.tsx (~410-430) (and edit dialog) with a type-to-search Combobox built on the existing ui/command + ui/popover primitives, filtering by patient name and phone. Keep the existing inline "new patient" toggle. Goal: stop scrolling a long list on every booking.
In scope: create/edit appointment dialogs, a reusable PatientCombobox component. Out of scope: server-side patient search/pagination unless trivially needed.
```

### SF-08 — Faster appointment time entry
```
Define a small feature: speed up time entry in create/edit appointment dialogs. Today start/end times are each two stacked dropdowns (24-row hour + 60-row minute) in components/create-appointment-dialog.tsx (~480-581) and components/edit-appointment-dialog.tsx (~393-423). 
Desired: replace with a single time input (input type="time" or 5/15-minute stepped options) for start and end; default end from procedure-type duration (already computed). Keep it consistent across both dialogs.
In scope: both appointment dialogs. Out of scope: calendar grid granularity (note it, but optional).
```

### SF-09 — Drag-to-reschedule on the calendar
```
Define a small feature: allow rescheduling an appointment by dragging it on the calendar grid (components/appointment-calendar.tsx), instead of the current 6-step edit-dialog round-trip. Dragging to a new time/day updates the appointment via the existing update API with optimistic UI and a toast; snap to a sensible interval (e.g. 15 min). 
In scope: appointment-calendar.tsx, the appointments page update handler. Out of scope: resizing duration by drag (could be a follow-up), intra-hour collision lane layout (optional).
```

### SF-10 — Lean patient quick-create
```
Define a small feature: make registering a patient fast for a front desk. Today components/edit-patient-dialog.tsx (~284-315) requires firstName, lastName, gender, dateOfBirth, AND phone, while the booking quick-create path only needs first/last name and the backend accepts that. 
Desired: require only first + last name; make gender/DOB/phone optional (gender default "Unknown" as the booking path already does); keep them editable later. Align the required-field set across both creation paths.
In scope: components/edit-patient-dialog.tsx, create-appointment inline create. Out of scope: backend validation changes if any are needed (confirm during definition).
```

### SF-11 — Create a document from a patient (+ saved documents list)
```
Define a small feature: let users create a document for the patient they're viewing without re-searching them, and find documents they already saved. Today app/patients/[id]/page.tsx has no "create document" action and the editor (components/document-editor-content.tsx ~1165) takes no patientId, so you go to /documents, pick a template, then re-select the same patient. Also app/documents/page.tsx imports medicalDocumentsApi but never renders a saved-documents list.
Desired: add a "Nouveau document" action on the patient page that deep-links to /documents/[type]?patientId=<id>; the editor reads patientId and pre-fills the patient; add a "Saved documents" list (recent and/or per patient) on the documents hub with open/edit/delete wired to medicalDocumentsApi.
In scope: app/patients/[id]/page.tsx, app/documents/page.tsx, document-editor-content.tsx param handling. Out of scope: new document templates.
```

### SF-12 — Record a procedure from a completed appointment
```
Define a small feature: let a dentist record the procedure straight from a completed appointment. Today marking an appointment "Completed" (components/edit-appointment-dialog.tsx ~605-621) is separate from recording a dental record, which only lives on the patient detail page (components/patient-record-modal.tsx), forcing re-navigation and re-selecting the same procedure type. Also the same record action is labeled "Add Medical Record" (header) and "Add Dental Record" (card) on the patient page — unify to one label.
Desired: from a completed appointment, offer "Record procedure" that opens the record modal prefilled with the patient and the appointment's procedure type. Unify the duplicate button labels.
In scope: edit-appointment-dialog.tsx, patient-record-modal.tsx, app/patients/[id]/page.tsx labels. Out of scope: billing/insurance.
```

### SF-13 — Onboarding: post-login chooser & smart routing
```
Define a small feature: fix where users land after login. Today app/login/page.tsx (~line 16) sends EVERY authenticated user to /setup (the create-a-clinic wizard), even staff who should join, and ignores middleware's returnTo. The components/unauthorized-page.tsx component is already a clean "Create vs Join" chooser but only shows as an error fallback.
Desired: after login, route based on clinic status — users WITH a clinic go to returnTo or dashboard; users WITHOUT one see a welcoming "Create new / Join existing" chooser (reuse/reframe unauthorized-page, dropping the "Access Restricted" red-lock framing). Prefill firstName/lastName/email from the Auth0 user object where collected. Distinguish a transient status-check error (retry) from a confirmed no-clinic state so users aren't wrongly pushed to create a second clinic.
In scope: app/login/page.tsx, components/clinic-guard.tsx, lib/hooks/use-clinic-access, unauthorized-page.tsx. Out of scope: the wizard internals (SF-14).
```

### SF-14 — Onboarding: streamline the setup wizard
```
Define a small feature: cut friction from the create-clinic wizard (components/setup-wizard.tsx). Problems: the entire "Working Hours" step (~163-197) is never sent to the API (dead friction); there's no draft persistence so a refresh wipes ~8 fields; "Next" is silently disabled with no hint of the missing field; on submit it does a hard window.location.href="/" causing a second full-screen clinic-status spinner in ClinicGuard; "Skip for now" actually creates the clinic.
Desired: remove the dead working-hours step (or wire it and move to Settings); persist wizard state to localStorage and rehydrate; add inline validation messages + email/phone format checks; navigate with router.push (no full reload) and reuse the already-fetched clinic status; relabel/remove the misleading "Skip for now".
In scope: components/setup-wizard.tsx, app/setup/page.tsx. Out of scope: join flow (SF-15).
```

### SF-15 — Onboarding: improve the join flow
```
Define a small feature: make joining a clinic one coherent, validated flow. Today app/join/page.tsx is two pages (code entry, then a separate JoinWizard) and the code is only length-checked client-side — it's validated against the backend only on the FINAL submit, so a user can fill their whole profile on a bad/expired code before being told.
Desired: validate the code with a lightweight API call on "Continue" and show the resolved clinic name/logo ("You're joining: <Clinic>"); merge the code field into a single wizard with one progress indicator; keep the role-aware short path for secretaries.
In scope: app/join/page.tsx, components/join-wizard.tsx, a validate-code endpoint/client if missing. Out of scope: setup wizard (SF-14).
```

---

## P3 — Information architecture & polish

### SF-16 — Shared responsive app shell
```
Define a small feature: give the app one consistent, mobile-friendly frame. Today the shell (ClinicGuard + DashboardSidebar + DashboardHeader + main) is copy-pasted into every page (app/page.tsx, app/stock, app/settings, app/documents...), which is why layout diverges (settings has no padding; documents has a marketing hero). The sidebar (components/dashboard-sidebar.tsx) also has NO mobile behavior — it only shrinks 256px→64px, never hides (no Sheet/drawer/hamburger anywhere). And the collapse state flashes on load (contexts/sidebar-context.tsx inits false then reads localStorage).
Desired: move the shell into an App Router route-group layout.tsx so all pages share one frame and spacing; below md, hide the static sidebar and present it as a shadcn Sheet/drawer triggered by a header hamburger; fix the collapse flash with a lazy SSR-guarded initializer.
In scope: a route-group layout, dashboard-sidebar.tsx, dashboard-header.tsx, sidebar-context.tsx, removing per-page shells. Out of scope: visual redesign.
```

### SF-17 — Navigation / IA consolidation
```
Define a small feature: simplify the sidebar IA. Today components/dashboard-sidebar.tsx has 9 flat top-level items with overlap: Medical Records, Documents, and Files are three document-ish destinations; the global Files browser (app/files/page.tsx) duplicates the per-patient files manager (it still forces picking one patient first); Records (app/records/page.tsx) just shows a read-only modal of what /patients/[id] already shows; Procedure Types is configuration sitting as a primary peer.
Desired: group config items (Procedure Types, Stock, Settings) under a secondary/config section; consolidate the three document destinations; make /files either a true cross-patient view (recent uploads + filename search) or remove it in favor of per-patient files; fold Records into the patient detail page or make it a real cross-patient clinical search.
In scope: dashboard-sidebar.tsx, app/files, app/records, routing. Out of scope: building net-new search infra beyond a basic filename/patient filter (decide during definition).
```

### SF-18 — Consistent feedback & shared states
```
Define a small feature: standardize feedback across the app. Today native alert()/confirm() are used in components/stock-item-form-modal.tsx (~55), components/procedure-types-table.tsx (~75), app/appointments/page.tsx (~69/85/88, Google sync), and file/folder deletes — blocking and off-brand, while the app otherwise uses sonner toasts + ui/alert-dialog. There's also no Skeleton primitive and no shared EmptyState, so loading/empty/error handling is uneven.
Desired: replace every alert()/confirm() with toast (success/error) and AlertDialog for destructive actions; add a components/ui/skeleton.tsx and a small shared EmptyState component; apply the loading/empty/error triad consistently on list/table pages (pattern already done well in patients-table.tsx).
In scope: the files above + the shared primitives. Out of scope: per-page redesigns.
```

### SF-19 — AI assistant UX upgrade
```
Define a small feature: make the AI chat widget (components/ai-chat.tsx) discoverable and safe. Problems: opens with a generic greeting and no capabilities/suggested prompts, and only sends doctorId as context though ChatRequest supports patientId/appointmentId; auto text-to-speech reads EVERY reply aloud in en-US with no persisted opt-out; speech recognition is hardcoded en-US in a French clinic; ~220 lines of client-side Levenshtein auto-REWRITE typed patient names to the nearest match (safety risk); "Clear chat" uses an X icon that reads as close and wipes history with no confirm; on a failed request the user's typed message is lost.
Desired: add starter suggestion chips + a one-line capability description and pass current-page context (e.g. patientId); make TTS opt-in with a persisted toggle; set recognition + synthesis to fr-FR (or configurable); turn name autocorrect into a confirm-suggestion limited to voice input (or move server-side); use a distinct clear icon with confirmation; preserve the user's input on error with a retry.
In scope: components/ai-chat.tsx, lib/api/ai-chat.ts (pass context). Out of scope: changing the AI backend.
```

### SF-20 — Functional global search (or remove)
```
Define a small feature: make the header search real. Today components/dashboard-header.tsx renders a prominent "Search patients, appointments..." box with no state/handler/results on every page, and the account dropdown's "Profile"/"Settings" items are dead (no onClick/href).
Desired: implement the search to query patients (and optionally appointments) and route to results, with a simple dropdown of matches; OR if out of scope now, remove the box until it works. Wire "Settings" to /settings and either implement or remove "Profile".
In scope: components/dashboard-header.tsx, a patients search query/client. Out of scope: full global search across all entities.
```

---

## Bucket — too small for a spec → use `/quick-fix`

These are one- or two-line corrections; skip `/define-small-feature` and run `/quick-fix` with the note:
- **amountPaid overwrite** — `components/patient-record-modal.tsx:147-151` force-sets amountPaid=cost on every cost change, wiping partial payments; only prefill when empty.
- **Patient flags section disabled** — `components/edit-patient-dialog.tsx:879-891` looks actionable but is disabled ("not supported"); hide it until the API exists.
- **Dental notes visibility** — `app/patients/[id]/page.tsx:749-872`; surface important (⚠) notes inline and merge the duplicate Notes tab.
- **Dark mode** — `app/layout.tsx` + `globals.css` define full `.dark` tokens but nothing applies `.dark`; add a theme toggle or remove dead tokens.
- **Hardcoded "Paris" + € currency** — `components/document-editor-content.tsx` (~663/1593, ~206/237); derive city from clinic address and use TND for the Tunisian market.
- **html lang** — `app/layout.tsx` hardcodes `lang="en"` while much of the UI is French; set `lang="fr"`.

## Larger than "small" → consider `/define-feature` instead
- **Full internationalization (i18n)** — the app mixes French/English (nav/dashboard/stock EN; documents/wizards FR). Unifying language properly (likely FR for the target market) with an i18n framework is a full feature, not a small one.
- **Double-booking prevention** — a real availability/conflict model (vs. a soft client-side warning) touches scheduling rules and may warrant the full pipeline.
```
