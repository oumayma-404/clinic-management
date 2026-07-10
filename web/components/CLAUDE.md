# web/components/ — Feature Components & UI Primitives

Feature/screen components for the clinic frontend. `components/ui/` holds shadcn/ui primitives. All feature components are client components.

## Feature Components

| File | What it renders / does |
|------|------------------------|
| `dashboard-sidebar.tsx` | Left nav (Dashboard, Appointments, Patients, Procedure Types, Records, Documents, Files, Stock, Settings). Adds an admin-only **Users** entry in local mode (`mode==='local' && role==='admin'`). Collapsible via `useSidebar()`; persists collapse state. |
| `dashboard-header.tsx` | Top bar with user avatar/menu + logout, via the unified `useSession()` (not Auth0 `useUser` directly). Local mode adds a **Change password** menu item; **Settings** navigates to `/settings`. Hosts `<ConnectivityIndicator/>` (Local only). |
| `connectivity-indicator.tsx` | **Local mode only** (Phase 3). 3-state badge in the header driven by `useConnectivity()`: server-unreachable ("Serveur injoignable") vs internet-unreachable vs online. Renders nothing in Cloud. |
| `clinic-guard.tsx` | Route gate. Uses `useClinicAccess`; renders children only if user belongs to a clinic, else `unauthorized-page` / redirect. Wraps every protected page. |
| `unauthorized-page.tsx` | "Access Restricted" screen shown when user has no clinic. |
| `stats-card.tsx` | Small KPI card (title, value, icon, description, `default`/`urgent` variant). Used on dashboard. |
| `appointment-list.tsx` | Dashboard appointment list — **static sample data**. |
| `notifications-list.tsx` | Dashboard notifications list — **static sample data**. |
| `appointment-calendar.tsx` | Day/week calendar grid (24h hourly slots); renders appointments, handles slot/appointment clicks. Core of `/appointments`. Phase 3: a shared `renderSyncControls(appointment)` helper adds a "non synchronisé" badge + per-card "Push to Google" in both week/day views (gated on `useConnectivity().internetReachable`; hidden on synced/cancelled/completed and very-short cards); push success calls `onChanged` so the page refetches and the badge clears. |
| `create-appointment-dialog.tsx` | Dialog to create an appointment (patient, procedure type, doctor, date/time, duration). |
| `edit-appointment-dialog.tsx` | Dialog to edit/cancel/delete an appointment (with confirm AlertDialog). |
| `patients-table.tsx` | Patients list table; filters by `searchQuery` and `showFlaggedOnly`; fetches via `patientsApi`. |
| `edit-patient-dialog.tsx` | Create/edit patient dialog (demographics, address, insurance, medical/family history). |
| `patient-record-modal.tsx` | Patient dental-record entry modal (uses `dental-chart`). |
| `patient-summary-modal.tsx` | Read-only patient summary (info + dental records). Used on `/records`. |
| `patient-files-manager.tsx` | Per-patient folder/file browser: list, create folder, upload, download, delete (`patientFilesApi`). |
| `dental-chart.tsx` | Interactive teeth chart (adult/child), per-tooth procedures/notes; emits selection. Read-only mode supported. |
| `procedure-types-table.tsx` | Procedure types CRUD table with delete confirm. |
| `procedure-type-form-modal.tsx` | Create/edit procedure type (name, duration, cost, color, description). |
| `stock-table.tsx` | Inventory table with delete confirm — **sample data, not API-backed**. |
| `stock-item-form-modal.tsx` | Create/edit stock item modal — local state only. |
| `clinic-settings.tsx` | Clinic profile + doctors management (name, address, logo upload, add/remove doctors) via `clinicsApi`. Mounts `<BackupSettings/>` (Phase 5) only in Local mode for admins. |
| `backup-settings.tsx` | **Local, admin-only** (Phase 5 / US-8). "Sauvegarde" card: optional destination folder + "Sauvegarder maintenant" → `backupApi.backupNow`; toasts success (path + human-readable size) or the failure reason, and shows the last-backup path/size. Guards `setState` against unmount (a backup can be long-running). |
| `setup-wizard.tsx` | First-run clinic creation wizard (FR; Tunisian governorates). Calls `clinicsApi.create`; in local mode also collects the admin account (full name, email, password) → `/auth/setup`. |
| `join-wizard.tsx` | Join-clinic-by-code wizard (role, specialty). Calls `clinicsApi.join`; in local mode collects account fields (name/email/password) and self-registers via `clinicsApi.register` → `/auth/register`. |
| `user-management.tsx` | **Local, admin-only** (`/users`): users table (status + must-change badge + last login) with reset-password (temp shown once in a dialog) and deactivate/reactivate, each behind a confirm dialog; clinic-code display + Regenerate. Own row's Deactivate disabled (mirrors backend self-deactivation guard). |
| `change-password-form.tsx` | **Local** (`/change-password`): current/temp + new + confirm; posts to `/api/auth/change-password` (clears the forced-change cookie on success). |
| `document-editor-content.tsx` | Editor for medical documents (ordonnance, lettre de liaison, etc.); generates/exports PDF via `medicalDocumentsApi`. Rendered by `/documents/[type]`. |
| `ai-chat.tsx` | Floating AI assistant widget (mounted globally in `layout.tsx`, inside the session/connectivity providers). Calls `aiChatApi.chat`. Phase 3: consumes `useConnectivity()` — disables mic/textarea/send + shows a "connexion internet requise" banner when offline, and maps `ApiError.status===0` (mid-request drop) to a retryable "connexion perdue" toast; auto re-enables when internet returns. |

## components/ui/ — shadcn/ui primitives

Standard shadcn/ui (new-york style) wrappers over Radix UI + CVA + `cn()`. Do not document individually; treat as the design-system layer. Present:

`alert-dialog`, `avatar`, `badge`, `button`, `calendar`, `card`, `checkbox`, `command`, `dialog`, `dropdown-menu`, `input`, `label`, `popover`, `select`, `separator`, `switch`, `table`, `tabs`, `textarea`, `tooltip`.

Add new primitives with the shadcn CLI (config in `web/components.json`, base color neutral, CSS vars enabled, icons = lucide).

## Conventions

- Dialogs/modals are controlled (`open` + `onOpenChange` props) and report success via callbacks so parent pages bump a `refreshKey` to refetch.
- Components fetch through `lib/api/*` modules and surface errors with `sonner` `toast`.
- Confirm-destructive flows use `ui/alert-dialog`.
