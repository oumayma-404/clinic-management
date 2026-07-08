# web/lib/ — API Client, Hooks, Utils

The data-access layer: a thin `fetch` wrapper, per-resource API modules, shared DTO types, and React hooks. No React Query/SWR — modules return promises, hooks/components manage state.

## lib/api/

### `client.ts` — the fetch wrapper (foundation)
- Exports `apiGet`, `apiPost`, `apiPut`, `apiDelete`, `apiPostFormData`, `apiPutFormData` and the `ApiError` class.
- Base URL from `NEXT_PUBLIC_API_URL` (fallback `http://localhost:5000/api`).
- **Auth**: each call auto-fetches the Auth0 access token from `/api/auth/token` and sets `Authorization: Bearer <token>`; an explicit `accessToken` arg can override (pass `null` to skip). All calls send `credentials: 'include'`.
- **Error handling**: `handleResponse` parses non-OK responses, flattening .NET ProblemDetails (`title`, `message`, `errors`) into a single message and throwing `ApiError(status, message)`. Network/fetch failures throw `ApiError(0, ...)`.
- FormData helpers omit `Content-Type` so the browser sets the multipart boundary.

### `types.ts` — shared DTOs (mirror backend)
`AppointmentDto`, `PatientDto`, `PatientMedicalHistoryDto`, `PatientFamilyHistoryDto`, `ProcedureTypeDto`, `DentalRecordDto`, `PatientFileDto`, `PatientFolderDto`, `MedicalDocumentDto`. (Note: `duration` on `AppointmentDto` is a TimeSpan string like `"00:30:00"`.)

### Per-resource modules
Each exports a `<name>Api` object of async methods built on `client.ts`. Endpoints are relative to the API base.

| Module | Object | Endpoints / notes |
|--------|--------|-------------------|
| `appointments.ts` | `appointmentsApi` | `/appointments` list/get/create/update/delete |
| `patients.ts` | `patientsApi` | `/patients` list (searchTerm/limit)/get/create/update |
| `procedure-types.ts` | `procedureTypesApi` | `/procedure-types` CRUD (`includeInactive` flag) |
| `dental-records.ts` | `dentalRecordsApi` | `/patients/{id}/dental-records` CRUD; exports `CreateDentalRecordRequest` |
| `patient-medical-history.ts` | `patientMedicalHistoryApi` | `/patients/{id}/medical-history` CRUD |
| `patient-family-history.ts` | `patientFamilyHistoryApi` | `/patients/{id}/family-history` CRUD |
| `patient-files.ts` | `patientFilesApi` | `/patients/{id}/files` folders/files: list, init defaults, create folder, upload, download (Blob), delete. Uses raw `fetch` for multipart/blob with its own token fetch. |
| `medical-documents.ts` | `medicalDocumentsApi` | `/medical-documents` CRUD; `generatePdf` (job) and `generatePdfForDownload` (returns Blob). FormData when a PDF file is attached. |
| `clinics.ts` | `clinicsApi` | `/clinics`: `getUserStatus`, `create`, `join`, `updateDoctors`, `update`, `getLogo` (Blob), `regenerateCode` (admin, local). Also `register` → `/auth/register` (local self-registration). Backend wraps responses in a `Result<T>` (`isSuccess`/`value`/`error`) which this module unwraps. Exports `DoctorDto`, `UserStatusDto`, `ClinicDto`, request types. |
| `users.ts` | `usersApi` | **Local, admin**: `list` (`GET /users`), `resetPassword` (`POST /users/{id}/reset-password` → temp password once), `setStatus` (`PUT /users/{id}/status`). Values returned unwrapped. |
| `google-calendar.ts` | `googleCalendarApi` | `/googlecalendar`: `getStatus`, `authorize` (browser redirect to OAuth), `syncFromGoogle`, `syncAppointment`. Some calls use raw `fetch` (no auth header). |
| `ai-chat.ts` | `aiChatApi` | `/ai/chat` — POST chat messages + optional context. |
| `auth-client.ts` | `useAuthenticatedApi()` | Hook returning `get/post/put/delete` pre-bound with the Auth0 token from `useAuthToken` (alternative to client.ts auto-fetch). |

## lib/hooks/

| Hook | Purpose |
|------|---------|
| `use-auth-token.ts` (`useAuthToken`) | Reads the unified `useSession()` (not Auth0 `useUser` directly), then fetches the token from `/api/auth/token` (mode-aware: Auth0 access token in cloud, the local JWT from the cookie in local). Returns `{ accessToken, isLoading, user }`. Base for other hooks. |
| `use-clinic-access.ts` (`useClinicAccess`) | Calls `clinicsApi.getUserStatus`; returns `{ hasAccess, status, isLoading, error, refresh }`. Optionally redirects to `/setup` when no clinic. Backs `ClinicGuard`. |
| `use-doctors.ts` (`useDoctors`) | Derives doctor list from clinic status and auto-detects the current user's doctor record. |
| `use-appointments.ts` (`useAppointments`) | Fetches appointments for a date range (+ optional patient/doctor); memoizes formatted date params; returns `{ appointments, loading, error, refetch }`. |

## Other lib files

- `auth0.ts` — server-side `Auth0Client` (used by `middleware.ts` and the token route in cloud mode). Sets scope `openid profile email`, `prompt: 'login'`, conditional `audience`.
- `auth/session.tsx` — the `useSession()` seam + `CloudSessionProvider`/`LocalSessionProvider` (see `web/CLAUDE.md` → Auth & route protection). Every user-identity read goes through here, not Auth0 directly.
- `auth/local-auth.ts` — server-side `resolveAuthMode()` (`AUTH_MODE`) + cookie-name constants (`SESSION_COOKIE` = `local_session`, `MUST_CHANGE_COOKIE` = `local_must_change_password`). Used by middleware and the `/api/auth/*` routes.
- `utils.ts` — `cn(...)` (clsx + tailwind-merge) classname helper used everywhere.

## Conventions

- API modules are stateless promise factories; React state lives in hooks/components.
- Resource modules use `client.ts` helpers; modules that need Blobs/multipart (`patient-files`, parts of `clinics`/`medical-documents`/`google-calendar`) drop to raw `fetch` and fetch the token themselves.
- Catch `ApiError` to read `.status`/`.message`; surface via `sonner` toast.
