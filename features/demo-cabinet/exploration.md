# Exploration — public demo cabinet

**Date:** 2026-11-03 · **Base:** `origin/main` @ `66ecc657` (worktree `feature/demo-cabinet`)
Complements `handoff.md` § 4 (facts verified there are not repeated). Four read-only sweeps: jobs, POST-reads,
frontend surfaces, seed + site.

## 1. Background work and outbound calls (api)

| Job / service | Gate today | Demo note |
|---|---|---|
| `process-notifications` | `DispatchesOutboxes` | off → also stops the per-minute internet probe and the per-clinic SMS/WhatsApp path |
| `start-running-appointments` | `RunsClinicJobs \|\| RunsCutJobs` | must run |
| `flag-expiring-stock`, `post-monthly-expenses`, `count-clinic-activity`, `take-recovery-points` | `RunsClinicJobs` | `RunsClinicJobs` drags these in with the progress job — the plan must not read `RunsCutJobs` as « keep progress only » (a capability read as another) |
| `warn-subscription-expiry` | `RequiresSubscription && RunsClinicJobs` | off |
| `review-messaging-allowances` | `SellsVendorMessaging` | off |
| `watch-relays`, `prune-clinic-changes` | `PublishesChangeFeed` | off |
| `run-scheduled-backups` | `BacksUpItsOwnData` | off |
| `dispatch-os-push` | `PermitsOsPush(Kind)` + `Push:*` config | ⚠️ `PermitsOsPush` ends in `_ => throw` (`DeploymentProfile.cs:355-362`) and runs at startup → **a new kind crashes boot** until it gets an arm |
| `purge-expired-sessions` | UNGATED | harmless (local DB) |
| `PushDispatchWake` | UNGATED, idle unless push available | harmless |
| `DeferredStartupService` | `DefersMigrations` | runs a `pg_dump` before migrating → demo must have `DefersMigrations = false` |
| Google sync dispatcher (`Task.Run` on appointment create/update) | **UNGATED** — calls the internet probe before checking the clinic has Google | no appointment writes reach it in the demo except the seed; still an outbound call nothing can switch off |
| `PUT api/clinics/reminder-settings` | **no capability gate** | a clinic admin can type in a real SMS gateway — closed in the demo by the read-only gate + `DispatchesOutboxes = false` |
| `LocalDataProtection.cs:55,142` | checks `Kind == HostedMultiTenant` directly | demo key ring separation must be explicit (plan) |

## 2. What « read-only » really has to let through

**Non-GETs that are reads** (visitor needs them):
- `POST /api/medical-documents/generate-pdf-download` — Télécharger / Imprimer a document.
- `POST /api/medical-documents/fiche-ordonnance-preview` — aperçu in the fiche de soins.
- `POST /api/clinics/letterhead/preview` — only reached inside the letterhead import flow (a write flow).
- `POST /api/patients/import/preview` — dry run of a CSV the visitor uploads.
- `POST /api/auth/step-up` — TOTP re-check before exports and « Afficher l'argent ».
- `POST /api/auth/refresh`, `POST /api/auth/logout` — every session uses them (the BFF's GET `/bff/auth/token`
  and GET `/bff/pdf/...` call refresh server-side).
- `POST /api/csp-report` — the browser's own.

**Looks like a read, writes:** `POST /medical-documents/{id}/generate-pdf` (attaches a PDF to the patient's files).

**GETs that write, and refuse the read if the write fails:** file download, dossier export, patient/appointment
CSV export, archive (audit ledgers). → the demo database cannot be read-only at the DB level; the gate is HTTP.

**Non-GETs fired on their own while browsing:** default folders on first Fichiers open, staff-chat mark-read,
push-device register at sign-in, notification mark-read on click, relay device report (desktop shell only).

**Order:** `IdempotencyMiddleware` (`Program.cs:1097`) writes a claim row for any non-GET before the
subscription gate (`:1108`) → a demo gate belongs before it.

**Precedent:** `SubscriptionGateMiddleware` (exempt attribute + `SubscriptionExemptionCoverageTests`, both
directions, reviewed list). Counts OPTIONS as a read.

## 3. Frontend facts (web)

- Capabilities reach the web through **one** anonymous read: `GET /api/auth/mode` (`AuthModeDto`); new flags are
  optional and read `=== true`. `SubscriptionProvider` is the model for an app-wide flag.
- Entry: `PUBLIC_ROUTES` (exact) + `HIDDEN_PATHS` (prefix) + a BFF route under `app/bff/auth/` writing cookies with
  `writeSessionCookies`. A signed-in visitor opening `/login` is bounced to `/`.
- Errors: `client.ts` global handlers keyed on `code` (`subscription_required` 402 is the template). Calls that
  skip `throwIfNotOk`: login, change-password (its BFF flattens every status to 400 and drops `code`), session,
  token exchange.
- Idle limit 30 min (untrusted) → `/login`.
- Surfaces that misbehave in a shared read-only demo: post-visit popup (desktop, every shell page), dashboard
  customiser (per-user prefs = shared), Mode discret (clinic-wide, TOTP to show), Sécurité (TOTP, recovery codes,
  devices), change password, forced password change, forced TOTP enrolment, default-folders write on Fichiers,
  staff chat, web push register.
- « Premiers pas » cards do **not** exist in code yet (option 1 is still a plan).
- Banners live inside the shell column (`app-shell.tsx:96-109`), right of the sidebar on desktop; no top safe-area
  handling anywhere.
- Every screen: see the agent table in the session; 6 nav groups, ~22 entries, plus patient file, plan, documents.

## 4. Seed and site

- **Console verbs have no MediatR** (`AddInfrastructure` only, no `IClinicContext`). `MessagingGrantCommand.cs:91-100`
  builds a handler by hand under `UseClinic`. The e2e `ClinicApi` + `Arrange` (`e2e/lib/`) build patients, bookings,
  fiches, plans, payments **through the HTTP API** — the other model for a seed.
- No demo/fake-data generator exists anywhere.
- « Premiers pas » cabinet (`C:\dev\clinic-film`): **« Cabinet Dentaire Démo »**, Avenue des Jasmins, Tunis;
  Dr Monica Geller (admin), Rachel Green (secrétaire), patient Ross Geller (pénicilline, asthme); letterhead
  `papier-a4.png`, `cachet.png`; « Dr Dr » trap when the first name holds the title.
- Site: nav allows **one** button in the top bar (`nav.html:21-22`); hero actions `index.html:38-39`; feature pages
  are `odontogramme`, `logiciel-dentaire-hors-ligne`, `guide-choisir…` (no agenda / dossier page). Umami click
  listener lives on `feature/security-remediation` (`587ad9ea`, `layout.html:96-101`), **not on main**.
- Banned claims: CNAM, TVA/timbre, client counts/testimonials, SMS/WhatsApp reminders, « sans internet ».
