# Feature Specification: Windows Desktop / Offline-LAN Deployment Mode

**Status:** APPROVED
**Challenged:** Yes
**Type:** Large (multi-workstream — candidate for phased delivery)
**Created:** 2026-07-07
**Scope:** Full-stack + packaging/DevOps
**Feature:** Ship the clinic app as installable Windows software that runs on a clinic LAN and works offline, while keeping the existing Auth0 cloud web app fully intact (dual-mode, same codebase).

---

## Overview

Today the app is a three-tier web system (Next.js + .NET API + PostgreSQL + MinIO) that hard-depends on cloud Auth0 for login and on the internet for AI chat and Google Calendar. This feature **adds a second deployment mode** — a self-hosted Windows installation for a single clinic — **without removing or breaking the current cloud/web deployment**.

The two modes share one codebase and are selected by configuration:

- **Cloud mode (existing, unchanged):** Auth0 login, MinIO storage, deployed on the web/Docker as today.
- **Local/offline mode (new):** local email + password login, local-disk file storage, everything self-hosted on one clinic "server" PC, works with no internet.

**Deployment topology (Local mode):**

```
   ┌──────────────────────── Clinic LAN ────────────────────────┐
   │  SERVER PC (one machine)            CLIENT PCs (several)     │
   │  ┌───────────────────────┐          ┌────────────────────┐  │
   │  │ .NET API              │◀─ LAN ───│ Installed desktop   │  │
   │  │ Next.js web (hosted)  │◀─ HTTPS ─│ app (WebView2 shell)│  │
   │  │ PostgreSQL (service)  │          │ → points at server  │  │
   │  │ Local-disk file store │          └────────────────────┘  │
   │  └───────────┬───────────┘           ...more clients         │
   └──────────────┼──────────────────────────────────────────────┘
                  ▼ only when internet is up
        HuggingFace (AI) · Google Calendar  → auto-gated
```

The clinic data is **self-contained** on the server PC. There is **no synchronization with the cloud**; a Local install and the cloud web app are independent instances.

**A Local install hosts exactly ONE clinic.** Multi-tenant/multi-clinic behavior applies only to Cloud mode. In Local mode, login email is unique per install, and the clinic join code is retained only as a **light self-registration gate** (so not literally any device on the LAN can create a staff account) — not as a tenant selector.

---

## User Stories

### US-1 — Install the clinic server
**As** a clinic owner, **I want** to install the app on one clinic PC with a single installer, **so that** the whole clinic can use it over the local network without any cloud account or internet.
- **AC-1.1:** A single Windows installer sets up the server PC: the .NET API, the hosted web UI, a bundled PostgreSQL, and a local file-storage folder — all running as background services that start automatically on boot.
- **AC-1.2:** On first launch the server presents a first-run setup that creates the clinic and the first user (an admin), with no internet or Auth0 required.
- **AC-1.2a:** First-run setup is reachable **only from the server PC itself (localhost)** — never from a LAN client. Once the first admin exists, the setup route is closed. This prevents any client on the network from claiming the admin account.
- **AC-1.3:** After setup, the app is reachable from other PCs on the same LAN.
- **AC-1.4:** Installing/uninstalling the server does not require the user to manually configure PostgreSQL, storage, ports, or certificates.

### US-2 — Install and connect a client PC
**As** clinic staff, **I want** to install a lightweight app on my PC and point it at the clinic server, **so that** I can use the system like normal Windows software.
- **AC-2.1:** A lightweight Windows client installer places an app with a Start-menu/taskbar icon.
- **AC-2.2:** On first launch the client asks for the clinic server address (IP or hostname); the value is stored and reused on subsequent launches.
- **AC-2.3:** The server address is editable later (a "change server" option) without reinstalling.
- **AC-2.4:** When the configured server is unreachable, the client shows a clear, non-technical "Cannot reach the clinic server" screen with a retry action — never a blank page or raw browser error.
- **AC-2.5:** The server PC itself can also run the client app (pointing at localhost).

### US-3 — Log in offline with a local account
**As** a staff member, **I want** to log in with my email and password, **so that** I can access the app with no internet and no Auth0.
- **AC-3.1:** In Local mode, login is by **email + password**; credentials are stored securely (hashed) in the clinic database.
- **AC-3.2:** Login works with no internet connection.
- **AC-3.3:** A successful login establishes a session that authorizes API calls (multi-tenancy/clinic scoping resolves from the local account, not Auth0 claims).
- **AC-3.4:** Wrong credentials show a clear error; the account is protected against unlimited guessing (lockout after repeated failures).
- **AC-3.5:** Sessions persist across app restarts and auto-expire after a configurable period of inactivity (default 30 minutes), returning the user to the login screen.
- **AC-3.6:** Logout returns to the login screen while preserving the configured server address.

### US-4 — Register additional staff
**As** a staff member, **I want** to create my own account using the clinic code, **so that** I can join the clinic system without an admin having to pre-create me.
- **AC-4.1:** A registration flow collects email, password, full name, role (doctor/secretary), doctor details if applicable, and the clinic code.
- **AC-4.2:** A valid clinic code creates an active account; an invalid code is rejected with a clear error.
- **AC-4.3:** Registering with an email that already exists in the clinic is rejected with a clear message.
- **AC-4.4:** A self-registered user selects doctor or secretary; **admin is never self-assignable** (only the first user is admin).
- **AC-4.5:** The clinic code is visible to the admin (in settings) to share with staff, and can be regenerated by the admin (invalidating the old code for future registrations).

### US-5 — Admin manages users and resets passwords
**As** the clinic admin, **I want** a simple user-management screen, **so that** I can help a locked-out staff member and control access.
- **AC-5.1:** The admin sees a list of clinic users (name, email, role, status).
- **AC-5.2:** The admin can reset a user's password; the new/temporary password is shown on screen for the admin to relay, and the user is required to change it at next login.
- **AC-5.3:** The admin can deactivate/reactivate a user; a deactivated user cannot log in, but their historical records (appointments, documents) are retained.
- **AC-5.4:** The user-management screen is only reachable by an admin.

### US-6 — Online features degrade gracefully
**As** a user, **I want** AI chat and Google Calendar to work when the clinic has internet and to disable cleanly when it doesn't, **so that** offline use is never confusing or broken.
- **AC-6.1:** The app distinguishes two states: **(a) clinic server unreachable** (app-level failure — see AC-2.4) and **(b) internet unreachable** (core app fine; only internet features affected).
- **AC-6.2:** When there is no internet, AI chat and Google Calendar controls are visibly disabled/greyed with a "requires internet" label — not clickable-then-error.
- **AC-6.3:** When internet returns, these features re-enable automatically (with sensible debouncing so a flapping connection doesn't thrash the UI).
- **AC-6.4:** All non-internet features (patients, appointments, records, documents, files, stock, dashboard) work fully offline.
- **AC-6.5:** An in-flight AI or calendar request that loses connectivity fails with a clear message and can be retried, rather than hanging.
- **AC-6.6:** Appointments that are not yet reflected in Google Calendar (e.g. created while offline) show a visible "not synced to Google" indicator, and a manual "Push to Google" action lets staff sync them once internet is available. (Automatic backfill of offline changes remains out of scope.)

### US-7 — Keep the cloud web app working
**As** the product owner, **I want** the existing Auth0 cloud web deployment to keep working exactly as before, **so that** adding the desktop mode costs us nothing on the web side.
- **AC-7.1:** With the app configured in Cloud mode, Auth0 login, MinIO storage, and all existing behavior are unchanged.
- **AC-7.2:** The mode is selected by server configuration; a client cannot flip a Cloud deployment into a no-auth/local state by editing its own local settings.
- **AC-7.3:** The shared database schema additions for local accounts (e.g. password fields) are inert/ignored in Cloud mode.

---

### US-8 — Manual backup of clinic data
**As** the clinic admin, **I want** a one-click backup, **so that** a disk failure, theft, or corruption on the single server PC doesn't mean total, unrecoverable loss of patient records.
- **AC-8.1:** An admin-only "Backup now" action writes a consistent backup of the database + stored files to a configurable destination folder (e.g. an external/network drive).
- **AC-8.2:** The action reports success (with location) or a clear failure reason (e.g. destination unwritable, disk full) — never a silent failure.
- **AC-8.3:** Storage/DB failures elsewhere (disk full, corruption) surface clear, non-silent operator messaging rather than appearing as a generic app outage.
- **AC-8.4:** Restoring from a backup is a documented manual procedure in v1 (no in-app restore UI required).

## Functional Requirements

Grouped by workstream. Each maps to the stories above.

### FR-A. Pluggable authentication (dual-mode)
- **FR-A1:** Introduce an explicit auth-mode setting on the server (e.g. `Auth:Mode = Cloud | Local`). Cloud preserves the current Auth0 JWT validation; Local uses locally-issued sessions/tokens.
- **FR-A2:** In Local mode, the backend issues its own signed session token on successful email+password login. The token-signing secret is **generated per install** at setup and never shipped in the installer or committed.
- **FR-A3:** The current-user/clinic resolution (`IClinicContext`) works in both modes — in Local mode it resolves identity from the local account rather than Auth0 claims — so all existing clinic-scoped features work unchanged.
- **FR-A4:** The frontend obtains and attaches its credential through the existing single seam (the API client's token acquisition + the token route), extended to return the local session token in Local mode. The route-protection gate (middleware) redirects unauthenticated users to the **local login** screen in Local mode instead of the Auth0 flow.
- **FR-A5:** The Auth0-specific management integration (pushing role/clinic metadata) is a no-op in Local mode.

### FR-B. Local accounts & user management
- **FR-B1:** Local user accounts store email (unique **per clinic**), hashed password, full name, role, active/inactive status, and a "must change password" flag. Each account has a stable internal id.
- **FR-B2:** Password policy: minimum 8 characters (enforced at the API, mirrored in the UI).
- **FR-B3:** First-run setup creates the clinic (with a join code) and the first user as **admin**.
- **FR-B4:** Self-registration (US-4) reuses the existing clinic-code join flow, extended to capture credentials.
- **FR-B5:** Admin user-management screen (US-5): list users, reset password, deactivate/reactivate.
- **FR-B6:** Admin self-lockout recovery: because there is no email/cloud reset, provide a **server-side password-reset utility** runnable on the server PC (by someone with Windows access to that machine) to reset the admin password. This is the documented recovery path.

### FR-C. Local file storage
- **FR-C1:** In Local mode, patient files, medical documents, and clinic logos are stored in a configurable folder on the server PC (a real local-disk implementation of the storage interface the features already use).
- **FR-C2:** Cloud mode continues to use MinIO unchanged.
- **FR-C3:** File save and its database record must not silently diverge — if one fails the operation reports failure rather than leaving an orphaned file or a record pointing at a missing file.

### FR-D. Connectivity awareness
- **FR-D1:** Provide a connectivity signal that separately reflects **server reachability** and **internet reachability**.
- **FR-D2:** Internet-dependent features (AI chat, Google Calendar) key off internet reachability for their enabled/disabled state (US-6).
- **FR-D3:** "Feature not configured" and "network unavailable" are distinguishable so the UI can label them correctly.
- **FR-D4:** Appointments not yet pushed to Google Calendar are trackable so the UI can show a "not synced" indicator and offer a manual "Push to Google" action (AC-6.6).

### FR-E. LAN hosting & networking
- **FR-E1:** In Local mode the API/web bind to the LAN so client PCs can connect; CORS allows the clinic's own origin(s) (the current single-origin `AllowCredentials` policy must accommodate the LAN origin).
- **FR-E2:** LAN traffic carrying patient data and credentials is served over **HTTPS**. The server generates a local CA + server certificate at install. The **client installer imports the server's CA certificate into the Windows trust store** during client setup, so the WebView2 shell connects to the server (by IP or hostname) without certificate-trust warnings. This trust-provisioning step is part of pointing a client at its server.
- **FR-E3:** **Release gate:** in Local mode, every API endpoint requires authentication. The two currently-anonymous controllers (Google Calendar, Medical Documents) must be authenticated, and the Hangfire dashboard must be locked down or disabled.
- **FR-E4:** Server address/ports are configurable; the client stores the server address locally.

### FR-F. Packaging & installers
- **FR-F1:** A **server installer** bundles and configures: the self-contained .NET API, the hosted Next.js web server, PostgreSQL, the file-storage folder, the generated HTTPS cert, and the per-install signing secret — installed as auto-starting Windows services in the correct dependency order (DB → API w/ auto-migration → web).
- **FR-F2:** A **client installer** places the lightweight WebView2 desktop shell that points at the server.
- **FR-F3:** Database migrations apply automatically on server startup (as today); a fresh install comes up empty and is populated via first-run setup.
- **FR-F4:** No real secrets are bundled in either installer; secrets are generated on the target machine at setup.
- **FR-F5:** Server startup failures (DB service down, port in use) surface a clear operator-facing message rather than failing silently.

### FR-G. Manual backup (US-8)
- **FR-G1:** Provide an admin-only "Backup now" action that produces a consistent backup of the database + stored files to a configurable destination.
- **FR-G2:** Success and failure are both reported explicitly; storage/DB failures (disk full, corruption) are surfaced with clear operator messaging, never silent.
- **FR-G3:** Restore is a documented manual procedure in v1 (no in-app restore UI). Scheduling/automation of backups is out of scope.

---

## User Interface (Local mode additions)

New/changed screens, reusing existing shadcn/ui + Tailwind + react-hook-form/zod patterns and the French-label convention already used in `/setup` and `/join`:

- **Client first-run "server address" screen** (desktop shell) — enter/edit the clinic server address; retry when unreachable.
- **Local login screen** — email + password; replaces the Auth0 redirect in Local mode.
- **Registration screen** — extends the existing join wizard with email/password fields.
- **Force-password-change screen** — shown after an admin reset.
- **Admin user-management page** — under settings; user list + reset/deactivate actions; shows the clinic code with a regenerate action.
- **Connectivity indicator** — a small status affordance; AI chat + Google Calendar controls show a "requires internet" disabled state when offline.

Cloud mode UI is unchanged.

---

## Data / Schema Changes

Additive columns on the user record (present in both modes, used only in Local mode; **inert in Cloud mode**):
- `PasswordHash` (and any inline hash parameters)
- `IsActive` / status
- `MustChangePassword` flag
- Optional: failed-login-attempt / lockout tracking, `LastLoginAt`

Constraints:
- Email **unique per install** for Local accounts (a Local install is single-clinic).
- `FullName` effectively required for Local accounts (currently nullable).

No changes to existing clinic/appointment/document schemas. Existing migrations and the auto-migrate-on-startup behavior are reused.

---

## Delivery Phases

This is an umbrella spec delivered as an ordered sequence of independently shippable/reviewable sub-features. Each phase gets its own plan, stories, and review; later phases depend on earlier ones.

1. **Phase 1 — Pluggable auth + local accounts** (FR-A, FR-B): auth-mode switch, local email+password login, first-run admin, self-registration, user management, admin recovery utility. *(Critical path — everything else builds on this.)*
2. **Phase 2 — Local-disk file storage** (FR-C): local storage implementation for Local mode; MinIO preserved for Cloud.
3. **Phase 3 — Connectivity awareness & offline UX** (FR-D, US-6): server-vs-internet reachability signal; gating of AI chat + Google Calendar.
4. **Phase 4 — LAN hosting & security gates** (FR-E): HTTPS on LAN, CORS for LAN origin, auth on all endpoints, locked-down Hangfire, first-run setup exposure control.
5. **Phase 5 — Packaging, installers & manual backup** (FR-F, FR-G): self-contained .NET publish, bundled PostgreSQL, server + client installers (incl. CA-trust provisioning), WebView2 shell, auto-start services, one-click "Backup now".

The acceptance criteria and requirements below are the shared source of truth for all phases.

## Scope

### In Scope
- Config-selectable dual-mode auth (Cloud/Auth0 preserved; Local email+password added).
- Local accounts, first-run admin, clinic-code self-registration, admin user management + password reset, server-side admin-recovery utility.
- Local-disk file storage for Local mode.
- Connectivity awareness + graceful offline degradation of AI chat & Google Calendar.
- LAN hosting (HTTPS, CORS, auth on all endpoints, locked Hangfire).
- Server + client Windows installers; bundled PostgreSQL; auto-start services.
- Keeping the cloud web deployment fully working.

### Out of Scope
- **Cloud ↔ desktop data synchronization** (each instance is independent).
- **Automatic/scheduled backups** — out of scope. A **manual one-click "Backup now"** action IS in scope (see US-8 / FR-G); scheduling it is deferred.
- **Role permission enforcement** — roles remain stored/displayed as today (policies still not enforced); enforcing them is a separate feature.
- **Auto-update** of installed apps — updates via re-running the installer; documented, not automated.
- **Automatic** backfill of offline appointment changes to Google Calendar — out of scope. A visible "not synced" indicator + a **manual "Push to Google"** action ARE in scope (AC-6.6); automatic reconciliation on reconnect is deferred.
- **At-rest disk/DB encryption** — relies on the server PC's OS-level protection; not implemented by the app in v1.
- **Audit trail** of patient-record access.
- Adding local login to the **cloud** deployment (cloud stays Auth0-only).

---

## Edge Cases (Critical only)
- **Admin forgets password (sole admin):** recovered via the server-side reset utility (FR-B6) — the only offline recovery path.
- **Server PC unreachable / DB service down / disk full / port in use:** client shows a clear reachability error; server surfaces startup/storage failures rather than failing silently. Disk-full should warn before uploads/DB writes fail silently.
- **Connectivity flapping:** debounce so online-only features don't rapidly toggle; in-flight AI/calendar calls fail cleanly and are retryable.
- **Concurrent edits from two client PCs:** last-write-wins (current behavior); acceptable for v1, documented.
- **Duplicate/colliding email at registration:** rejected with a clear message (email unique per clinic).
- **Power loss mid-write (record vs file):** operation reports failure; no silent orphaned file or dangling reference.
- **Version skew:** a client shell pointing at a newer/older server — since the client is a thin shell over the server-hosted UI, both are served by the server, avoiding client/server code drift.

## Non-Functional Hints
- **Security/privacy (medical data):** HTTPS on LAN, per-install signing secret, no bundled secrets, auth on all endpoints, lockout on brute force — these are treated as requirements, not nice-to-haves, because Local mode puts patient data on a directly-reachable LAN.
- **Simplicity for non-technical clinics:** single installer, auto-start services, no manual DB/cert/port setup.
- **Offline-first:** core features must never depend on internet.

## Dependencies
- Windows 11 WebView2 runtime on client PCs (present by default on Win 11).
- A desktop-shell toolchain (e.g. Tauri) — greenfield; none exists in the repo today.
- Self-contained .NET publish (`win-x64`) — not yet configured.
- Bundled PostgreSQL distribution for the server installer.
- Existing local-disk storage stub (`LocalFileStorageService`) as the basis for FR-C1.

## Resolved Decisions (approved 2026-07-07)
1. **LAN encryption (FR-E2):** HTTPS on the LAN with an installer-generated, client-trusted certificate. **Approved.**
2. **Self-registration exposure (US-4 / FR-B4):** Self-registered accounts are active immediately; admin can deactivate and regenerate the code. No admin-approval gate in v1. **Approved.**
3. **Client shell architecture:** Thin WebView2 shell over the server-hosted web UI (no per-client bundled web assets). **Approved.**
4. **Admin recovery (FR-B6):** Server-side password-reset utility on the server PC. **Approved.**
5. **Backups:** Scheduled/automatic backups out of scope; a **manual one-click "Backup now"** is IN scope for v1 (US-8/FR-G). Restore is a documented manual procedure. **Revised during challenge (2026-07-08).**
