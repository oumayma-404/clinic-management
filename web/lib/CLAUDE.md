# web/lib/ — API Client, Hooks, Realtime, Utils

The data-access layer: a thin `fetch` wrapper, per-resource API modules, shared DTO types, React hooks, a SignalR realtime seam, the auth/connectivity contexts, and formatting/utility helpers. No React Query/SWR — modules return promises; hooks/components manage state; realtime signals trigger refetches.

## lib/api/

### `client.ts` — the fetch wrapper (foundation)
- Exports `apiGet`, `apiPost`, `apiPut`, `apiDelete`, `apiPostFormData`, `apiPutFormData` and the `ApiError` class.
- Base URL from `NEXT_PUBLIC_API_URL` (fallback `http://localhost:5000/api`).
- **Auth**: each call auto-fetches the token from `/bff/auth/token` (Phase 5 relocation) and sets `Authorization: Bearer <token>`; an explicit `accessToken` arg overrides (pass `null` to skip). All calls send `credentials: 'include'`.
- **Relative base URL (Phase 5)**: `apiGet` resolves the base against `window.location.origin` so a *relative* `NEXT_PUBLIC_API_URL=/api` (same-origin front-door build) parses; `window` is guarded for SSR/Node-test imports; absolute Cloud bases ignore the origin (no-op).
- **Error handling**: `handleResponse` flattens .NET ProblemDetails (`title`/`message`/`errors`) **and** bare-JSON-string failures into one message, throwing `ApiError(status, message)`. `fetch`/network failures throw `ApiError(0, ...)` — the shared "offline" signal.
- FormData helpers omit `Content-Type` so the browser sets the multipart boundary.

### `types.ts` — shared DTOs (mirror backend)
~40 interfaces. Highlights (all fields optional unless noted):
- **Scheduling**: `AppointmentDto` (has `doctorId`, `treatmentPlanItemId`, `isSyncedToGoogle`; `duration` is a TimeSpan string like `"00:30:00"`), `RecurringAppointmentDto`, `RecurringSeriesResultDto`.
- **Patients/records**: `PatientDto` (+ `cnamInfo`, `flags`), `CnamInfo`, `PatientMedicalHistoryDto`, `PatientFamilyHistoryDto`, `DentalRecordDto`/`DentalRecordActDto`/`DentalActInput`, `ToothStateDto` (odontogram entry; `source` = `Diagnosis`|`Treatment`), `ProcedureTypeDto` (+ `resultingCondition`), `PatientFileDto`/`PatientFolderDto`, `MedicalDocumentDto`, `DoctorProfileDto`.
- **Billing**: `InvoiceDto`/`InvoiceLineDto`/`PaymentDto` (+ TTN e-invoice state), `InvoiceRevenueDto`, `PatientBillingSummaryDto` (« solde patient » + CNAM split), `ReceivableDto`, `TreatmentPlanDto`/`TreatmentPlanItemDto`/`InstallmentDto`, `ExpenseDto`/`CaisseSummaryDto`.
- **Catalogs (per-clinic reference data)**: `CnamNomenclatureEntryDto`, `CnamLetterValueDto`, `MedicationDto`, `DentalActDto`.
- **Clinical-depth**: `WaitingListEntryDto`, `LabWorkOrderDto`, `RecallDto`/`RecallSettingsDto`, `StockItemDto`.
- **Feed/dashboard**: `NotificationDto` (feed row — `category`, `title`, `message`, `createdAt` [effective feed time], `isRead`, `targetKind` = `Appointment`|`StockItem` + optional ids), `PendingReviewDto` (due post-visit review), `DashboardStats` (incl. `monthlyRevenueCollected`, `totalOutstanding`).

### Per-resource modules
Each exports a `<name>Api` object of async methods over `client.ts` (endpoints relative to the API base). Some (`reminder-settings`, `clinics`, `users`) unwrap a backend `Result<T>` (`isSuccess`/`value`/`error`).

| Module | Object | Endpoints / notes |
|--------|--------|-------------------|
| `appointments.ts` | `appointmentsApi` | `/appointments` list/get/create/update (**no delete**); recurring series: `listRecurring`/`createRecurring`/`cancelRecurring`. Create **and update** can link a treatment-plan step — on update the pair is **tri-state**: omit the key to leave the link alone, send `treatmentPlanItemId: null` to clear it. |
| `patients.ts` | `patientsApi` | `/patients` list(searchTerm/limit)/get/create/update; `getAiSummary` (`/patients/{id}/ai-summary`, live HuggingFace). Create accepts CNAM + inline medical/family-history entries. |
| `procedure-types.ts` | `procedureTypesApi` | `/procedure-types` CRUD (`includeInactive`); `initializeDefaults` (19 general Tunisian procedures). |
| `dental-records.ts` | `dentalRecordsApi` | `/patients/{id}/dental-records` CRUD (multi-act; exports `CreateDentalRecordRequest`); can mark a plan step réalisé. |
| `odontogram.ts` | `odontogramApi` | `/patients/{id}/odontogram` get; `diagnose` / `removeCondition` (charted diagnoses only). |
| `patient-medical-history.ts` / `patient-family-history.ts` | `patientMedicalHistoryApi` / `patientFamilyHistoryApi` | `/patients/{id}/medical-history` \| `/family-history` CRUD. |
| `patient-files.ts` | `patientFilesApi` | `/patients/{id}/files` folders/files: list, init defaults, create folder, upload, download (Blob), delete. Raw `fetch` for multipart/blob. |
| `medical-documents.ts` | `medicalDocumentsApi` | `/medical-documents` CRUD; `generatePdf` (job) + `generatePdfForDownload` (Blob). FormData when a PDF is attached. |
| `dashboard.ts` | `dashboardApi` | `/dashboard/stats` (day/week/month range params). |
| `stock.ts` | `stockApi` | `/stock` list(`lowStockOnly`)/create/update/delete; exports `StockItemPayload`. |
| `invoices.ts` | `invoicesApi` | `/invoices` list/get/create/update/issue/recordPayment/cancel/delete; `revenue`; `submitToElFatoora`; `downloadPdf`/`downloadEInvoiceArtifact` (Blob, raw fetch). |
| `billing.ts` | `billingApi` | `getPatientSummary` (`/patients/{id}/billing-summary`), `getReceivables` (`/billing/receivables`), `downloadPaymentReceipt` (Blob). |
| `treatment-plans.ts` | `treatmentPlansApi` | `/treatment-plans` list/get/create/update/accept/**complete**/cancel/remove; `recordInstallmentPayment`, `markItemDone`; `downloadDevisPdf`/`downloadInstallmentReceipt` (Blob). |
| `expenses.ts` | `expensesApi` | `/expenses` CRUD + `caisseSummary` (`/billing/caisse`). |
| `cnam-nomenclature.ts` | `cnamNomenclatureApi` | `/cnam-nomenclature` list/create/update/deactivate/`confirmData` (admin) + `listLetterValues`/`updateLetterValue`. Also exports client-side `estimateReimbursement`/`reimbursementRate` (mirrors backend calculator; editor-only, never persisted). |
| `medications.ts` | `medicationsApi` | `/medications` list/create/update/deactivate/`confirmData` (admin; backs ordonnance picker). |
| `dental-acts.ts` | `dentalActsApi` | `/dental-acts` list/create/update/deactivate/`confirmData` (admin; backs treatment-plan/invoice act picker). |
| `doctors.ts` | `doctorsApi` | `/doctors/me` get/update (CNOMDT ordre + cachet upload, FormData); per-dentist working hours; `fetchCachetBlob` (Blob). |
| `lab-orders.ts` | `labOrdersApi` | `/lab-orders` list(patient?)/create/update/updateStatus/delete. |
| `recalls.ts` | `recallsApi` | `/patients/recalls` list + settings; `markContacted`/`snooze`/`send`. |
| `waiting-list.ts` | `waitingListApi` | `/waiting-list` list/create/update/promote/delete. |
| `reminder-settings.ts` | `reminderSettingsApi` | `/clinics/reminder-settings` get/update, `/clinics/whatsapp/connect` connect/disconnect (Cloud Embedded-Signup), `reminder-status`. `Result<T>`-wrapped; secrets write-only. |
| `clinics.ts` | `clinicsApi` | `/clinics`: `getUserStatus` (cache-busted), `create`, `join`, `updateDoctors`, `update` (profile + billing + El Fatoora + working-hours), `getLogo` (Blob), `regenerateCode` (admin), `setup`→`/auth/setup`, `register`→`/auth/register` (Local, anonymous, `null` token). `Result<T>`-wrapped. Exports `DoctorDto`/`UserStatusDto`/`ClinicDto` + request types. |
| `users.ts` | `usersApi` | **Local, admin**: `list`, `resetPassword` (temp password once), `setStatus`. Unwrapped. |
| `notifications.ts` | `notificationsApi` | In-app feed: `list`, `unreadCount`, `pendingReviews`, `markRead`, `markAllRead`. |
| `google-calendar.ts` | `googleCalendarApi` | `getStatus` (`/googlecalendar/status`), `connect` (POST→authUrl, per-clinic admin, browser redirect), `syncFromGoogle`, `syncAppointment` — the last two route via `client.ts` so a mid-request drop surfaces as `ApiError(status:0)`. |
| `ai-chat.ts` | `aiChatApi` | `/ai/chat` — POST messages + optional context. |
| `backup.ts` | `backupApi` | **Local, admin (Phase 5)**: `backupNow(dest?)`→`/backup`→`BackupResultDto`. |
| `auth-client.ts` | `useAuthenticatedApi()` | Hook returning `get/post/put/delete` pre-bound with the token from `useAuthToken` (alternative to client.ts auto-fetch). |

## lib/hooks/

| Hook | Purpose |
|------|---------|
| `use-auth-token.ts` (`useAuthToken`) | Reads the unified `useSession()`, then fetches the token from `/bff/auth/token` (mode-aware). Returns `{ accessToken, isLoading, user }`. Base for other hooks. |
| `use-clinic-access.ts` (`useClinicAccess`) | `clinicsApi.getUserStatus` → `{ hasAccess, status, isLoading, error, refresh }`; optional `/setup` redirect. Distinguishes HTTP-200 `hasClinic:false` (not a member) from transient errors. Backs `ClinicGuard`. |
| `use-doctors.ts` (`useDoctors`) | Derives the doctor list from clinic status; auto-resolves the current user's linked doctor (by `userId`, then email/name). |
| `use-appointments.ts` (`useAppointments`) | Fetches a date range (+ optional patient/doctor); sends UTC-instant day bounds; `{ appointments, loading, error, refetch }`. |
| `use-dashboard-stats.ts` (`useDashboardStats`) | Fetches `dashboardApi.getStats` with memoized day/week/month ranges; `{ stats, loading, error, refetch }`. |
| `use-appointment-overlap.ts` (`useAppointmentOverlap`) | Advisory French overlap warning for the appointment dialogs; fetches the selected day once, recomputes on time/duration edits; non-blocking (fetch failure disables it). |
| `use-notifications.ts` (`useNotifications`) | Backs the header bell + panel: unread count (always) + list (on open) via `notificationsApi`; `markRead`/`markAllRead`; live via `useClinicRealtime(Notifications)`, refetching after a reconnect. |

## lib/realtime/ — SignalR clinic bus

- `clinic-hub.ts` — builds a `HubConnection` to `/hub/clinic` (resolved against the API origin; browser-only). `RealtimeResource` maps feature areas → lowercase keys (`appointments`, `patients`, `stock`, `notifications`, `invoices`, `treatmentplans`, `cnamnomenclature`, `medications`, `dentalacts`, `users`, …); server emits a single `entityChanged` event carrying the changed key. Mode-aware bearer token via `/bff/auth/token`.
- `use-clinic-realtime.ts` (`useClinicRealtime(resources, onChanged)`) — subscribes once per resource set, filters to the watched keys, refetches on reconnect (catch-up), retries the first connect until unmount. **Additive**: connection failures are never surfaced; pages keep working via manual refresh.

## Other lib files

- `auth0.ts` — server-side `Auth0Client` (cloud). Scope `openid profile email`, `prompt: 'login'`, conditional `audience`.
- `auth/session.tsx` — the `useSession()` seam + `CloudSessionProvider`/`LocalSessionProvider`. `CloudBridge` also resolves the clinic `role` via `clinicsApi.getUserStatus` (so cloud admins get role-gated UI); `LocalSessionProvider` reads `/bff/auth/session` and does 30-min inactivity auto-logout. Every user-identity read goes through here.
- `auth/local-auth.ts` — server-side `resolveAuthMode()` (`AUTH_MODE`) + cookie-name constants (`SESSION_COOKIE`, `MUST_CHANGE_COOKIE`).
- `connectivity/connectivity.tsx` — `ConnectivityProvider` + `useConnectivity()`. Polls `GET /api/connectivity` every 15s **only** in Local mode; Cloud gets a static online default. Exposes `{ serverReachable, internetReachable, isLocal }`, debounces transitions, toasts on change. Consumed by AI chat, the appointments calendar, and the invoices e-invoice action.
- `errors.ts` — `getErrorMessage(err, fallback)` / `showErrorToast(err)` + `DEFAULT_ERROR_MESSAGE` (single French-first error-text formatter over `ApiError`/`Error`/string).
- `format.ts` — French/Tunisian formatters: `formatDT` (millimes + "DT", fr-TN grouping), `formatDateFr`, `formatDate`, `formatDateTime`.
- `phone.ts` — `toE164Tunisian` / `isDeliverablePhone` + `PHONE_ERROR_FR` (mirrors backend `PhoneNumber.ToE164`; used in patient/appointment forms).
- `working-hours.ts` — `WorkingDay` shape, `WEEKDAYS`, `DEFAULT_WORKING_HOURS` (Mon–Sat 09:00–17:00), `summarizeWorkingHours` (grouped French summary).
- `brand.ts` — `PRODUCT_NAME = "Gestion Clinique"` (fallback name when a clinic's own name is unknown).
- `download.ts` — `downloadBlob(blob, filename)` (browser save-as for PDFs/receipts).
- `utils.ts` — `cn(...)` (clsx + tailwind-merge); `parseDurationToMinutes(timeSpan)`.

## Conventions

- API modules are stateless promise factories; React state lives in hooks/components; cross-tab freshness comes from `useClinicRealtime`.
- Resource modules use `client.ts` helpers; modules needing Blobs/multipart (`patient-files`, `medical-documents`, `invoices`, `billing`, `treatment-plans`, `doctors`, parts of `clinics`) drop to raw `fetch` and attach the token themselves.
- Result-wrapped endpoints (`clinics`, `users`, `reminder-settings`) unwrap `isSuccess`/`value`/`error` and throw on failure.
- Catch `ApiError` to read `.status`/`.message`; surface via `errors.ts` → `sonner` toast. `ApiError.status === 0` = offline/network.
