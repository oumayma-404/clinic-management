# web/components/ — Feature Components & UI Primitives

Feature/screen components for the clinic frontend; `components/ui/` holds shadcn/ui primitives. All feature components are client components. Most fetch through `lib/api/*`, surface errors via `sonner`/`lib/errors`, and subscribe to `useClinicRealtime` for live refresh. Money is French/Tunisian via `lib/format` (`formatDT`, `formatDateFr`).

## Chrome & navigation

| File | What it renders / does |
|------|------------------------|
| `dashboard-sidebar.tsx` | Left nav: Tableau de bord, Rendez-vous, Patients, Types de procédures, Dossiers médicaux, Documents, Factures, Créances, Plans/Devis, Fichiers, Stock, Relances, Salle d'attente, Laboratoire, Caisse, Mon profil, Paramètres. Any-admin adds **Nomenclature CNAM / Médicaments / Actes dentaires**; local-admin adds **Utilisateurs**. Footer shows the clinic's working-hours summary (`summarizeWorkingHours`); header brand = clinic name or `PRODUCT_NAME`. Collapsible via `useSidebar()`. |
| `dashboard-header.tsx` | Top bar: live patient **search** (debounced `patientsApi.list` → navigate), `<ConnectivityIndicator/>` (Local), notification **bell + `<NotificationPanel/>`** (`useNotifications`, `99+` cap, deep-links + mark-read), user menu via `useSession()` (logout; local adds "Changer le mot de passe"). Mounts `<PostVisitReviewPopup/>`. |
| `connectivity-indicator.tsx` | **Local only**. 3-state header badge (server-unreachable / no-internet / online) from `useConnectivity()`; renders nothing in Cloud. |
| `clinic-guard.tsx` | Route gate via `useClinicAccess`/`useAuthToken`: loading → children if member → `unauthorized-page` if not → a distinct retry screen on transient errors → redirect to `/auth/login` if unauthenticated. Skips setup/join/login. Wraps protected pages. |
| `unauthorized-page.tsx` | "Accès restreint" screen when the user has no clinic. |
| `stats-card.tsx` | Presentational KPI card (title/value/icon/description, `default`/`urgent`, loading skeleton). |

## Notifications

| File | What it renders / does |
|------|------------------------|
| `notification-panel.tsx` | Presentational feed popover (props-driven): 50 newest rows with category icon, title, message, relative French time (`n.createdAt`), unread styling, empty/loading/error states, "Tout marquer comme lu", row deep-link. |
| `post-visit-review-popup.tsx` | Modal prompting staff to record a finished visit. Polls `notificationsApi.pendingReviews` (60s) + realtime; "Ajouter le dossier médical" → `/documents?appointmentId=`; "Plus tard" snoozes client-side (localStorage). Mounted once in the header. |

## Appointments

| File | What it renders / does |
|------|------------------------|
| `appointment-list.tsx` | Dashboard "Rendez-vous du jour" — API-wired via `useAppointments`; hides Cancelled/NoShow. |
| `appointment-calendar.tsx` | Day/week/**month** calendar (24h slots); optional `doctorId` filter + show-cancelled/completed toggles. Per-card "non synchronisé" badge + "Push to Google" gated on `useConnectivity().internetReachable`; push → `googleCalendarApi.syncAppointment`, `onChanged` refetch. |
| `create-appointment-dialog.tsx` | Create RDV (patient, procedure or custom, doctor, date/time, duration). Advisory `useAppointmentOverlap` warning; phone validation; can link a treatment-plan step (`presetPlan*`). |
| `edit-appointment-dialog.tsx` | Edit/cancel/complete an RDV (confirm AlertDialog); overlap warning. |

## Patients, records & odontogram

| File | What it renders / does |
|------|------------------------|
| `patients-table.tsx` | Patients list (`patientsApi`) with debounced search + flag filter + out-of-order guard; opens `EditPatientDialog` / `PatientSummaryModal`. |
| `edit-patient-dialog.tsx` | Create/edit patient (demographics, address, insurance, CNAM identity, flags, medical/family history); Tunisian phone validation. |
| `patient-record-modal.tsx` | Dental-record entry: multi-act (procedure/cost/teeth/resulting condition/surfaces/note), interactive `RecordToothChart`, optional plan-item link. |
| `patient-summary-modal.tsx` | Read-only patient summary (info + records + a read-only `RecordToothChart` of worked teeth). Opened from the patients table. |
| `odontogram.tsx` | Interactive per-tooth chart (`odontogramApi`): chart/remove diagnoses, view all recorded states per tooth, "Créer un plan depuis l'odontogramme" seeds; realtime (Patients). |
| `record-tooth-chart.tsx` | Presentational FDI tooth-chart SVG (paint map passed in); shared by the record + summary modals. |
| `odontogram-conditions.ts` | Shared `ToothCondition` metadata: labels, colors/box classes, surface labels, `conditionStyle`/`parseSurfaces`/`serializeSurfaces`. |
| `tooth-multiselect.tsx` | FDI tooth-picker popover; exports `ADULT_FDI`/`CHILD_FDI`. |
| `patient-files-manager.tsx` | Per-patient folder/file browser (`patientFilesApi`): list, create folder, upload, download, delete (destructive via `alert-dialog`). |

## Billing — factures / créances / plans

| File | What it renders / does |
|------|------------------------|
| `factures/invoices-table.tsx` | Invoices (« notes d'honoraires ») — `invoicesApi`, realtime (Invoices): create/edit draft, issue, record payment, cancel, delete, PDF, and TTN El Fatoora submit + artifact downloads (gated/queued when offline). |
| `factures/invoice-form-modal.tsx` | Draft create/edit with per-line CNAM/DCH act picker (fills designation + default fee, drives the reimbursable split). |
| `factures/payment-modal.tsx` | Record an invoice payment (method/amount/date) + "Télécharger le reçu". |
| `factures/invoice-labels.ts` | French labels + badge classes for invoice status, payment method, and e-invoice status. |
| `creances/receivables-table.tsx` | Clinic-wide « Créances » (`billingApi.getReceivables`); row → patient detail; total-due header. |
| `treatment-plans/treatment-plans-table.tsx` | Plans/devis **list** — `treatmentPlansApi`, realtime (`[TreatmentPlans, Appointments, Invoices]`). Rows navigate to `/treatment-plans/[id]`; what remains is create + a small **labelled** dropdown (Ouvrir / Devis PDF / Modifier / Supprimer le brouillon). *(The "Gérer" dialog and its row of 8 unlabelled ghost icons were retired — the dialog was the only view of a plan's contents and offered every action on every row regardless of état.)* |
| `treatment-plans/plan-workspace.tsx` | The devis workspace body: header + actes + échéancier + parcours, the plan's actions (Accepter / Facturer / Terminer / Devis PDF / Annuler), and the Encaisser + Planifier dialogs. Rendered by `/treatment-plans/[id]`. |
| `treatment-plans/plan-act-row.tsx` | One planned act: désignation, `codeActe`, dents, coût, its derived état, and **exactly one** primary action — Planifier / Voir le RDV / Enregistrer la fiche / Voir la fiche. Navigates via the existing deep links (`/appointments?appointmentId=`, `/patients/{id}?addRecord=1&appointmentId=`, `/patients/{id}?tab=`). |
| `treatment-plans/plan-timeline.tsx` | « Parcours » — a chronological feed (created / accepted / séance planifiée / acte réalisé / paiement encaissé / annulé / facturé) built only from fields already on `TreatmentPlanDto`. Reuses `notification-panel.tsx`'s feed shape rather than inventing a timeline primitive. |
| `treatment-plans/plan-progress-bar.tsx` | Hand-rolled `role="progressbar"` (no `progress.tsx` primitive, no `@radix-ui/react-progress`). Renders **nothing** at 0 acts rather than a zero-width bar. Shared by the workspace and `patient-plan-card`. |
| `treatment-plans/patient-plan-card.tsx` | The patient page's lead-in above the tabs: lead plan, statut, progress, prochaine séance, one primary action. Links into the workspace; "+N autres" opens the plans tab. Renders null when the patient has no plan. |
| `treatment-plans/plan-next-action.ts` | Pure helpers: `planItemState` (the four états), `isPlanBilled`, `planNextAction`, `leadPlan`. No I/O. |
| `treatment-plans/treatment-plan-form-modal.tsx` | Draft plan editor: act lines (dental-act picker + `ToothMultiSelect`), installment schedule; accepts odontogram seeds. |
| `treatment-plans/installment-payment-modal.tsx` | Record an installment (échéance) payment + receipt. |
| `treatment-plans/treatment-plan-labels.ts` | French labels + badge classes for plan/item status. |

## Catalogs (admin) & documents

| File | What it renders / does |
|------|------------------------|
| `cnam-nomenclature-table.tsx` / `cnam-entry-form-modal.tsx` / `cnam-letter-values-card.tsx` | Admin CNAM nomenclature catalog + valeurs de la lettre clé (`cnamNomenclatureApi`; `includeInactive`, confirm-provisional-data, in-place refetch via `reloadToken`). |
| `medication-catalog-table.tsx` / `medication-form-modal.tsx` | Admin medication catalog (`medicationsApi`; backs the ordonnance picker). |
| `dental-acts-table.tsx` / `dental-act-form-modal.tsx` | Admin dental-act catalog (`dentalActsApi`; backs the treatment-plan/invoice act picker). |
| `document-editor-content.tsx` | Medical-document editor (certificat médical with CNOMDT mention, ordonnance with medication picker + CNAM reimbursement estimate, lettre de liaison, etc.); PDF via `medicalDocumentsApi`, Word export via `docx`. Rendered by `/documents/[type]`. |
| `documents/honoraires-launcher.tsx` | "Note d'honoraires" flow: pick a patient → open `InvoiceFormModal` (draft) prefilled with the patient's un-invoiced dental records (numbering/TVA/El Fatoora happen later at issue). |

## Profile, settings & admin

| File | What it renders / does |
|------|------------------------|
| `mon-profil-content.tsx` | Logged-in practitioner's document identity (CNOMDT ordre number + cachet image upload/preview) via `doctorsApi`. |
| `clinic-settings.tsx` | Clinic profile + doctors + billing (matricule/TVA/timbre) + El Fatoora + working hours (`clinicsApi`, realtime Clinics). Mounts `<ReminderSettings/>` for admins and `<BackupSettings/>` for local admins. |
| `reminder-settings.tsx` | **Admin**. SMS/WhatsApp reminder settings (tri-state channel toggles, gateway/Graph URLs, lead times, message body, masked secrets), per-channel `effectiveStatus` badge, WhatsApp Embedded-Signup connect/disconnect (Cloud), recent delivery-status rows. |
| `backup-settings.tsx` | **Local, admin**. "Sauvegarde" card → `backupApi.backupNow`; reports path + size or failure; guards setState against unmount. |
| `user-management.tsx` | **Local, admin** (`/users`): users table (status/must-change/last-login) with reset-password (temp shown once), deactivate/reactivate (self-guarded), clinic-code display + Regenerate; realtime (Users). |
| `change-password-form.tsx` | **Local** (`/change-password`): current + new + confirm → `/bff/auth/change-password`; 8-char min; forced vs voluntary. |

## Stock, onboarding & AI

| File | What it renders / does |
|------|------------------------|
| `stock-table.tsx` | Inventory (`stockApi`) with search/category/low-stock filters + delete confirm; scroll-to/highlight a deep-linked low-stock row (`clinic:deeplink`). |
| `stock-item-form-modal.tsx` | Create/edit stock item via `stockApi`. |
| `procedure-types-table.tsx` / `procedure-type-form-modal.tsx` | Procedure-types CRUD (`procedureTypesApi`) + "seed defaults"; form has color, duration, cost, resulting condition. |
| `setup-wizard.tsx` | First-run clinic creation (FR; Tunisian governorates; `clinicsApi.create`); local mode also collects the admin account → `/auth/setup`. |
| `join-wizard.tsx` | Join-by-code (role, specialty; `clinicsApi.join`); local mode self-registers via `clinicsApi.register` → `/auth/register`. |
| `ai-chat.tsx` | Floating AI assistant (mounted globally). Calls `aiChatApi.chat`; consumes `useConnectivity()` to disable + banner offline and map `ApiError.status===0` to a retryable toast. |

## components/ui/ — shadcn/ui primitives

Standard shadcn/ui (new-york) wrappers over Radix + CVA + `cn()`. Treat as the design-system layer:

`alert-dialog`, `avatar`, `badge`, `button`, `calendar`, `card`, `checkbox`, `command`, `dialog`, `dropdown-menu`, `input`, `label`, `popover`, `select`, `separator`, `switch`, `table`, `tabs`, `textarea`, `tooltip`.

Add new primitives with the shadcn CLI (`web/components.json`, base color neutral, CSS vars, lucide icons).

## Conventions

- Dialogs/modals are controlled (`open` + `onOpenChange`) and report success via callbacks; parent tables reload via a bumped `reloadKey`/`reloadToken` **and** `useClinicRealtime`.
- Components fetch through `lib/api/*`, format money/dates via `lib/format`, and surface errors via `lib/errors`/`sonner`.
- Confirm-destructive flows use `ui/alert-dialog`. Blob downloads use `lib/download`.
- Admin-gated surfaces read `useSession().user.role === 'admin'`; local-only surfaces also check `mode === 'local'`.
