# Guided first days — implementation blueprint

> **Status: APPROVED 2026-11-03** by the owner (« approved, go with your recommendations »).
> Design and screens: https://claude.ai/artifact/JEYyUxrWyNS34xoNGa3v4z (version 2, screens S1–S17).
> Evidence: https://claude.ai/artifact/1oag7VWiFRgLTD7L89SHdt (where the 12 trial cabinets stopped).
> Branch: `feature/guided-first-days` off `origin/main` @ `66ecc657`, worktree `.claude/worktrees/guided-first-days`.
> Replaces parts 2 and 3 of `features/premiers-pas/blueprint.md` (27 Oct, untracked in the main checkout).
> Blast radius: [`blast-radius.md`](blast-radius.md). Every line number below is `origin/main`.

## 0. Summary

A dentist who signs up alone today loses at the security code (2 of 12), finds an empty dashboard (5 of 12), works
alone (11 of 12 had no second account) and never hears from APEXA again. Ten changes, in six parts, three releases:

| Part | Release | Changes | Screens |
|---|---|---|---|
| A · Sign-in | R1 | same QR for 24 h · « Rester connecté » ticked in the apps · verifier names the next step | S1, S2, S9 |
| B · « Premiers pas » + phone step | R1 | dashboard card (7 steps, video per step) · « Votre téléphone » | S3, S4, S6 |
| C · Secretary by e-mail | R2 | invitation link sent by APEXA (online version) | S7, S8 |
| D · Aide, devis, Rappels | R2 | « Aide » header menu · « Nouveau devis » creates the patient · « Rappels » explains itself | S10, S15, S16 |
| E · Feedback | R2 | week-one corner card · « Donner mon avis ou une idée » · e-mail to the vendor · console « Retours » | S11–S14 |
| F · « Me guider » | R3 | in-app guide over the real screens, 5 guides | S5 |

## 1. The owner's answers (2026-11-03)

| # | Question | Answer | Consequence |
|---|---|---|---|
| 1 | Same QR for 24 hours? | **yes** | Part A as designed |
| 2 | Where does « Vidéos » point? | **publish apexa.tn/tutoriels and point there** | The page is drafted, uncommitted, in the **main checkout**: `site/src/pages/tutoriels.html` (untracked) **plus** its `site/src/js/site.js` (« § Tutoriels », +44), `site/src/css/components.css` (+77) and `site/src/partials/nav.html` (+2) edits. Ship them together with release 1. ⚠️ Site deploy = two pushes; show the owner a local preview first (memories `website-work-needs-two-pushes`, `never-push-without-consent`) |
| 3 | Which number answers « Aide »? | **the « Abonnement » contact** (`Subscription:ContactPhone`, set on prod) | Before release 2, confirm with the owner that this number is on WhatsApp; if not, hide the WhatsApp row and keep the number as text |
| 4 | May the console show feedback text? | **yes, with « Sans nom de patient » in the field** | `PlatformReadShape` gains the feedback names, each with its reason; `features/platform-console/notes.md` gets a line |

Other decisions, with their reasons, are on the design page (« Decided, with the reason »). The ones an
implementer could undo by accident:

- **Steps tick themselves; never a manual tick.** A ticked step states that work exists.
- **« Vos tarifs » is not a step** (it could only tick by editing a price).
- **« Importer une liste » is withdrawn** from the empty patients list. The CSV import confused the owner herself.
- **« Rappels » stays in the menu.** Its page is replaced; it is never removed from `nav.ts`.
- **Invitation by e-mail from APEXA, never WhatsApp.** The dentist is on the desktop app; « Copier le lien » is
  the fallback.
- **No tour library** (§ F): modal dialogs make anything outside them inert.
- **Feedback is a plain `Entity`, not an aggregate**: not audited, not cabinet activity.

## 2. Video catalogue (one owner: `web/lib/onboarding-videos.ts`)

From `site/src/pages/tutoriels.html` (working tree of the main checkout, untracked):

| # | Title | YouTube id | Length | Used by step |
|---|---|---|---|---|
| 1 | Créer votre compte | `vXwtPbCL_UY` | 0:51 | — |
| 2 | Première connexion | `wQ-WNx39U50` | 0:53 | securityCode, verifier page (S1) |
| 3 | Préparer le cabinet | `UZokOPtfJBc` | 1:22 | secretary |
| 4 | Votre premier patient | `u8KUh00l3QA` | 0:45 | patients |
| 5 | Votre premier rendez-vous | `FZzPvkufkKU` | 0:39 | appointment |
| 6 | La séance | `ixdLZxswDU8` | 0:58 | dentalRecord |
| 7 | Le plan de traitement | `zVTUW-ZP17g` | 0:59 | — |
| 8 | Les documents | `XOXCFck4GgA` | 0:33 | letterhead |
| 9 | L'application sur votre téléphone | — (to film) | — | phoneApp: no ▶ until it exists |

Link form: `https://www.youtube.com/watch?v=<id>`, `target="_blank" rel="noopener noreferrer"`. Both shells send a
foreign `https` link to the system browser / a Custom Tab (`desktop/…/ExternalNavigation.cs:62-84`,
`mobile/android/…/ExternalNavigation.kt:56-73`). **No embedded player**: `frame-src 'self' blob:` exists in four
byte-identical CSP copies.

---

## Part A — Sign-in (release 1)

### Summary
Re-show the pending TOTP secret for 24 h instead of minting a new one on every « Se connecter », say so on the
screen, tick « Rester connecté » by default inside the Windows and Android shells, and name the next step on the
verifier page.

### Files to modify
- `api/ClinicManagement.Domain/Entities/User.cs`
  - add `public DateTime? TotpEnrolmentStartedAt { get; private set; }`. ⚠️ The name must avoid
    `Token/Secret/Password/Credential/ApiKey/Encrypted/Refresh` (`UnitTests/Common/SecretShapedNames.cs:19-23`).
  - `IssueTotpSecret(:342-354)` sets it (pass `nowUtc`, default `DateTime.UtcNow` as the grant does, :307-317).
  - `CompleteTotpEnrolment(:385-411)` and `DisableTotp(:472-484)` clear it.
  - `RequireNewCredentials(:265-276)` **also discards a pending secret** (`!IsTotpEnrolled && ProtectedTotpSecret != null`
    → clear secret + timestamp). Today a pending secret survives « Déclarer perdu », and the lost PC held a sealed
    copy (`ClinicRelayScope.cs:46-50`).
  - new `bool HasReusablePendingTotpSecret(TimeSpan lifetime, DateTime? nowUtc = null)` — pure predicate, no unprotect.
- `api/ClinicManagement.Application/Features/Auth/Commands/EnrolTotpCommand.cs`
  - `TotpEnrolmentDto` (:25-29) gains `string? QrStatus = null` (`"first" | "same" | "replaced"`), constants in a
    static `TotpQrStatus`.
  - step one (:161-191), **after** the unchanged password/lockout/already-enrolled checks (:108-158): if the user
    is not enrolled, holds a pending secret younger than `PendingQrLifetime = 24 h`, and `TryUnprotect` succeeds →
    build URI/QR from it, return `"same"`, **no save** (so no `TokenVersion` bump). Otherwise mint as today and
    return `"replaced"` when any secret existed before, else `"first"`.
- `api/ClinicManagement.Application/Common/Interfaces/ITotpService.cs:11-15` — stale comment (« no request-time
  path may mint one »).
- `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddOnboardingSignInColumns.cs` (+ Designer + snapshot) —
  one migration for parts A and B: `Users.TotpEnrolmentStartedAt`, `Users.PhoneAppFirstSignInAt`,
  `Users.PhoneAppPlatform` (all nullable). Template: `20260820145745_AddTotpReplacementGrant.cs:29-33`.
  ⚠️ remove any scaffolded `xmin` line.
- `web/components/security/totp-enrolment-step.tsx` — new prop `qrStatus?: "first" | "same" | "replaced"`; the
  green « Même QR code… » / amber « Nouveau QR code : il remplace le précédent… » line above the QR (S2).
- `web/app/login/page.tsx`
  - keep `qrStatus` with the enrolment material (:189-193, :268-294).
  - `trustDevice` (:235): after mount, `setTrustDevice(true)` when `appSurface() !== "browser"` **and** the person
    has not touched the box (SSR renders `false`; avoid a hydration mismatch).
  - `?enrol=1` « Plus tard » (:550-558) renders only with `&from=securite`.
- `web/app/securite/page.tsx:196` — link `/login?enrol=1&from=securite`.
- `web/app/signup/verifier/page.tsx:120-132` — success state gains « Prochaine étape : votre code de sécurité »,
  two numbered lines, store links (Google Authenticator on Play / App Store), « Voir la vidéo · 0:53 ».
- `mobile/shared/bridge.md:55` — the idle line still says a flat 30 minutes (it is 30 min / 8 h trusted).

### Files to create
- `web/lib/shell.ts` — `export type AppSurface = "windows" | "android" | "ios-home-screen" | "browser"` and
  `appSurface()`: `window.__clinicShell?.platform` for the shells; `(display-mode: standalone)` or
  `navigator.standalone` on iOS for the home-screen app. ⚠️ `web/lib/push/web-push.ts:30-33` has a private
  `isStandalone()` — move it here and import it there (one owner).
- `web/lib/onboarding-videos.ts` — § 2.

### Key signatures
```csharp
public DateTime? TotpEnrolmentStartedAt { get; private set; }
public static readonly TimeSpan PendingQrLifetime = TimeSpan.FromHours(24);
public record TotpEnrolmentDto(string? SecretUri, string? Secret, string? SecretQrPng,
    IReadOnlyList<string>? RecoveryCodes, string? QrStatus = null);
public static class TotpQrStatus { public const string First = "first", Same = "same", Replaced = "replaced"; }
```

### Pitfalls
- `IssueTotpSecret` bumps `TokenVersion`: a re-show must **not** call it.
- `RecoveryCodeLoadingCoverageTests` (:64-67): `EnrolTotpCommand` must still read the user with recovery codes loaded.
- `TotpReplayCoverageTests` (:48-55) lists `EnrolTotpCommand` as allowed-by-design without `TryConsume` — do not
  add `TryConsume`.
- `RelayFenceCoverageTests` keeps `["EnrolTotpCommand:Clinic"]` — the clinic read stays.
- The BFF `totp-enrol` route returns `data.value` whole, so the new field needs no BFF change.

### Test strategy (`UnitTests/Features/Auth/ClinicTotpAuthTests.cs`)
Same code within 24 h (URI identical, no save, `TokenVersion` unchanged) · new code after 24 h → `replaced` · first
ever → `first` · wrong password never reaches a pending secret · an enrolled account with a recovery-code grant
still gets a new secret (`replaced`) · `RequireNewCredentials` discards a pending secret · completing clears the
timestamp. Existing step-one tests stay green unchanged.

---

## Part B — « Premiers pas » and « Votre téléphone » (release 1)

### Summary
An admin-only dashboard card during the cabinet's first 30 days: seven steps that tick themselves from data, each
with its video (and later its guide), and a phone step fed by the first sign-in from the phone app.

### Files to create
- `api/…/Application/Features/Dashboard/GettingStartedRules.cs` — the seven keys, `VisibleDays = 30`, each tick.
- `api/…/Application/Features/Dashboard/Queries/GetGettingStartedQuery.cs` — query + handler. Reads one after
  another (shared DbContext, `GetDashboardQuery.cs:36-41`). Returns `Visible = false` **before any count** when
  `Clinic.CreatedAt` is older than 30 days. Current user via the `GetTotpStateQuery.cs:57-67` pattern; clinic via
  `ICurrentClinicResolver`.
- `api/…/Application/DTOs/GettingStartedDto.cs`.
- `api/…/UnitTests/Features/Dashboard/GetGettingStartedQueryTests.cs`, `GettingStartedKeysTests.cs` (reads
  `web/lib/getting-started.ts` both ways, as `DashboardKpiKeysTests` reads `dashboard-blocks.ts`).
- `web/lib/getting-started.ts` — key → label, video id, guide key (part F), action.
- `web/lib/hooks/use-getting-started.ts` — modelled on `use-appointment-status-mix.ts:25-68`; `useClinicRealtime`
  on `Patients`, `Appointments`, `Users`, `Clinics` (the dashboard listens to neither of the last two today).
- `web/components/dashboard/getting-started-card.tsx` — S3/S4; rows ≥ 44 px on a coarse pointer; ▶ is a real link
  with an `aria-label`; header « 1 sur 7 faite » (never a bare fraction, N31); trial chip from `useSubscription()`;
  toast « Votre cabinet est prêt » when the last step ticks in the session; « Masquer » toggles the block key with a
  5-second « Annuler ».
- `web/components/dashboard/phone-app-dialog.tsx` — S6: Android QR + text, iPhone three steps, « Déjà fait » chips
  per account.

### Files to modify
- `api/…/Domain/Entities/User.cs` — `PhoneAppFirstSignInAt`, `PhoneAppPlatform` (string, `android` |
  `ios-home-screen`), `RecordPhoneAppSignIn(string surface, DateTime nowUtc)` — **first time only**, never
  overwritten, ignored for `windows`/`browser`. ✅ Done in A1. ⚠️ B1: `web/lib/shell.ts` also answers `ios` (the
  native iPhone shell, written, never compiled) — add `PhoneAppSurfaces.Ios = "ios"` so it counts the day it ships.
- `api/…/API/Models/LoginRequest.cs:21` + `api/…/Features/Auth/Commands/LoginCommand.cs` — optional
  `ClientSurface`; on a successful sign-in (:252-277) call `RecordPhoneAppSignIn`. Same in
  `RedeemRecoveryCodeCommand` (a sign-in too).
- `web/app/bff/auth/local-login/route.ts:60` — forward `clientSurface: appSurface()` sent by the login page.
- `api/…/API/Controllers/MetaController.cs` — `GET api/meta/phone-app` → `{ androidUrl, androidQrPng }`; URL built
  from the request host exactly as `client-requirements` does (:116-141, :264-265); PNG via
  `IQrCodeGenerator.GeneratePng` as a `data:` URI (the TOTP pattern). Anonymous is fine (the download is anonymous).
- `api/…/Domain/Repositories/IDentalRecordRepository.cs` + implementation — `Task<bool> AnyForClinicAsync(Guid
  clinicId, CancellationToken ct = default)` (no per-clinic read exists).
- `api/…/Features/Dashboard/DashboardKpiKeys.cs` (+ `All`) and `web/lib/dashboard-blocks.ts` —
  `gettingStarted: { section: "journee", form: "list", label: "Premiers pas" }` at **two-space indent**
  (`DashboardKpiKeysTests` regex, :122). Same commit.
- `web/components/dashboard/dashboard-customizer.tsx` — an `omitKeys` prop; the page passes `["gettingStarted"]`
  when the card does not apply (not admin, not visible).
- `api/…/Controllers/DashboardController.cs` — `[HttpGet("getting-started")]` +
  `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]` on the method (class is `AdminOrDoctor`; precedent
  `TreatmentPlansController.cs:46-48`).
- `web/components/dashboard/dashboard-page.tsx` — mount under the greeting row (:478-488), admin only.
- `web/lib/api/dashboard.ts`, `web/lib/api/types.ts`, `web/lib/api/meta.ts`.
- Deep links:
  - `web/components/user-management.tsx` reads `?invite=1` once, opens the create dialog with role `secretary`,
    strips the param with `replaceUrl` (`history-writes-keep-next-state`, N53).
  - `web/components/letterhead/letterhead-card.tsx` — `id="en-tete"`; starts expanded when `location.hash ===
    "#en-tete"` (today `useState(true)` collapsed, :44); scroll into view after mount.
  - `/patients?newPatient=1` and `/appointments?newAppointment=1` already exist — but see part F's warning on the
    latter (unrounded start time).
- `api/…/Features/Auth/Commands/VerifyClinicSignUpCommand.cs` `SendWelcomeAsync` (:290-375) —
  `new EmailDetail("Vidéos : vos premiers pas", "https://apexa.tn/tutoriels.html", IsLink: true)`. Keep « no
  button » (:275-282).

### Key signatures
```csharp
public sealed record GettingStartedStepDto(string Key, bool Done);
public sealed record PhoneAppPersonDto(string Name, string Role, string? Platform, DateTime? FirstSignInAt);
public sealed record GettingStartedDto(bool Visible, IReadOnlyList<GettingStartedStepDto> Steps,
    IReadOnlyList<PhoneAppPersonDto> PhoneApp);
public class GetGettingStartedQuery : IRequest<Result<GettingStartedDto>> { }
public static class GettingStartedKeys
{
    public const string SecurityCode = "securityCode", Secretary = "secretary", PhoneApp = "phoneApp",
        Patients = "patients", Appointment = "appointment", DentalRecord = "dentalRecord", Letterhead = "letterhead";
}
```

### Done rules (`GettingStartedRules`)
| Key | Done when | Read |
|---|---|---|
| securityCode | current admin `IsTotpEnrolled`; **step omitted** when `!ISecondFactorPolicy.RequiresAdminSecondFactor` | user |
| secretary | ≥ 2 accounts in the cabinet | `IUserRepository.GetStaffSummaryAsync` (:42) |
| phoneApp | current admin `PhoneAppFirstSignInAt != null` | user |
| patients | ≥ 1 patient | `IPatientRepository.CountByClinicIdAsync` (:260) |
| appointment | ≥ 1 appointment | `IAppointmentRepository.CountByClinicIdAsync` (:11) |
| dentalRecord | ≥ 1 fiche | new `AnyForClinicAsync` |
| letterhead | `Clinic.LetterheadHeaderStorageKey != null` | `IClinicRepository.GetByIdAsync` |

`Visible = CreatedAt within 30 days && any step not done`.

### Pitfalls
- No new `Features/<Area>` folder — a query under `Features/Dashboard` emits no realtime key (it is a query, and
  `Dashboard` is excluded, `RealtimeResourceResolver.cs:50-54`).
- `ControllerAuthorizationCoverageTests`: the admin-only method under an admin-or-doctor class.
- The phone signal does not exist on production today (no Firebase, no web-push keys; `DeviceLabel` only when
  « Rester connecté » is ticked) — that is why the login records it.
- The QR must never point to the site's `APEXA-1.1.1.apk` (no updater, older than push).
- `check:responsive`: `icon-button-is-named`, `failed-read-as-empty` (the card disappears on a failed read, it
  never shows zeros), `type-scale`, `french-quote-binding`, `cached-read-names-its-broadcast` if React Query is used.

### Test strategy
`GetGettingStartedQueryTests`: each rule true/false · security step absent when not required · older than 30 days
→ invisible without counting (repository mocks `Times.Never`) · all done → invisible · a doctor is refused
(policy coverage) · another cabinet's rows never count (the resolver's clinic only). Login: a phone surface records
once; a second sign-in never overwrites; `windows`/`browser`/missing record nothing. `DashboardKpiKeysTests` +
`GettingStartedKeysTests` both ways.

---

## Part C — The secretary, invited by e-mail (release 2)

### Summary
On the online version, creating an account sends an invitation e-mail with a one-time link (7 days) where the
person chooses her own password. Own-server installs keep the temporary password, unchanged.

### Files to create
- `api/ClinicManagement.Domain/Entities/StaffInvitation.cs` — `AggregateRoot<Guid>` built like
  `PasswordResetRequest.cs` (hash, fixed-time compare, single use, `Rearm`) with `Lifetime = 7 days` and a
  **required FK to `Users` (cascade)** so `ClinicPurge` reaches it by FK. Separate entity because
  `PasswordResetRequests` has a unique `UserId` index and a static one-hour lifetime its resend cooldown is
  computed from (`LastIssuedAtUtc()`, :134).
- Repository + EF configuration (unique `UserId`, unique `TokenHash`) + migration `<ts>_AddStaffInvitations`.
- `api/…/Features/Users/Commands/ResendStaffInvitationCommand.cs` — `POST api/users/{id}/invitation`, only while
  `LastLoginAt == null`; re-arms (old link dies).
- `api/…/Features/Auth/Queries/PreviewStaffInvitationQuery.cs` — `POST api/auth/invitation/preview` → first name,
  e-mail, cabinet name; 410 shape otherwise.
- `api/…/Features/Auth/Commands/AcceptStaffInvitationCommand.cs` — `POST api/auth/invitation/accept` → length
  check, token check, `SetPassword(hash, mustChangePassword: false)`, consume. **No session** (as the reset,
  `AuthController.cs:236-240`), no « mot de passe modifié » e-mail.
- `api/…/Application/Common/StaffInvitationEmail.cs` — subject « {Admin} vous invite sur APEXA », Action « Choisir
  mon mot de passe », details: identifiant, « Au cabinet », « Sur votre téléphone » (Android link `IsLink`, iPhone
  steps), note « Le lien est valable 7 jours et ne sert qu'une fois. »
- `web/app/invitation/page.tsx` — S8 right; reads `#t=`, erases it; `PasswordInput` with reveal
  (`password-has-a-reveal`); min length from `usePasswordMinLength()` (`PasswordFloorSingleSourceTests`); success →
  `/login?email=…`.

### Files to modify
- `api/…/Infrastructure/Deployment/DeploymentProfile.cs` — capability `InvitesStaffByLink` (Lan false, Hosted
  true, Relay false) + `DeploymentProfileTests.ExpectedMatrix` + `hostedOnlyCapabilities`.
- `api/…/Features/Users/Commands/CreateClinicUserCommand.cs` — when the capability is on: random unusable
  password, `mustChangePassword: false`, invitation, send the e-mail after commit (best-effort, result reported);
  `CreatedClinicUserDto` gains `InvitationEmailed`, `InvitationUrl` (shown once), `TemporaryPassword` null.
- `api/…/DTOs/ClinicUserDto.cs` + mapping + `ListUsersQuery` — `InvitationPending`.
- `api/…/Controllers/UsersController.cs`, `AuthController.cs` — endpoints, capability 404 first.
- `api/…/Infrastructure/Persistence/ClinicArchiveScope.cs` (`Excluded`) and `ClinicRelayScope.cs` (`Excluded`).
- `api/…/BackgroundJobs/SessionFamilyPurgeJob.cs:84-89` — purge used/expired invitations.
- `web/components/user-management.tsx` — online: « Inviter », « Envoyer l'invitation », the result dialog
  (« Copier le lien », failure notice), the « Invitation envoyée » chip and « Renvoyer l'e-mail »; own-server path
  unchanged.
- `web/lib/auth/public-routes.ts` — `/invitation` (`public-routes-have-one-owner`).
- `web/lib/api/users.ts`, `web/lib/api/auth.ts`, `web/app/login/page.tsx` (`?email=` prefill in login mode).

### Guards to satisfy
`SecretProtectionCoverageTests.PlaintextByDecision` (`StaffInvitation.TokenHash`, reason ≥ 40 chars, like
`PasswordResetRequest.TokenHash` :123-128) · `ControllerAuthorizationCoverageTests.ExpectedAnonymous` (two
entries) · `SubscriptionExemptionCoverageTests.ExpectedExemptWrites` (Auth is class-exempt) ·
`RelayOnlineOnlyCoverageTests` (`[OnlineOnly]` on every Auth/Users write) · `RelayFenceCoverageTests` (passes only
because the table is excluded from the copy) · `ClinicPurgePlanTests` (reached by FK) · `ClinicArchiveScopeTests`.

### Pitfalls
- ⚠️ **Uncommitted work on `feature/security-remediation`** edits `user-management.tsx`, `ClinicUserDto`,
  `ClinicUserMappingExtensions`, `ListUsersQueryHandlerTests`, `AuthController`, `web/lib/api/auth.ts`,
  `web/app/login/page.tsx`. Part C must be built on top of it once it lands; check `git log` before starting.
- Android: coming back from any hand-off reloads the WebView (`MainActivity.kt:983-991`) — the result dialog is
  gone; « Renvoyer l'e-mail » is the way back.
- Anonymous context: no tenant scope. `StaffInvitation` has no `ClinicId`; `Users` and `Clinics` are unfiltered.

### Test strategy
Record: expiry, single use, re-arm, fixed-time compare. Create: online → invitation + e-mail, no temp password;
LAN → unchanged; doctor role still creates its practitioner; a failed e-mail is reported. Accept: sets password,
clears `MustChangePassword`; second use, expired, wrong token → one refusal (410). Resend: refused after first
sign-in; old token dies. Preview: names; never for used/expired.

---

## Part D — « Aide », the devis patient, « Rappels » (release 2)

### Files to create
- `web/components/help-menu.tsx` — Popover in the header **before** `StaffChatLauncher`
  (`dashboard-header.tsx:416-419`): label from `md` up, icon-only below with `aria-label="Aide"`,
  `coarse:size-11`. Rows: WhatsApp (`whatsAppUrl(contactPhone, "Bonjour, ici {clinic}. ")`, `web/lib/whatsapp.ts:35-43`),
  « Vidéos : vos premiers pas » (`https://apexa.tn/tutoriels.html`), « Donner mon avis ou une idée » (part E),
  the number as text + copy. Data: `useSubscription().subscription?.contactPhone` (every role, hosted only). No
  contact → no WhatsApp row; no contact and no feedback (LAN) → no button.
- `web/components/patients/new-patient-fields.tsx` + `web/lib/hooks/use-inline-new-patient.ts` — extracted from
  `create-appointment-dialog.tsx:1323-1405` (fields), `:859-873` (validation), `:937-989` (create on submit),
  `:560, 949-951` (`createdPatientIdRef`), `:1918-1960` (duplicate AlertDialog).

### Files to modify
- `web/components/create-appointment-dialog.tsx` — uses the extracted piece (behaviour identical).
- `web/components/treatment-plan-form-modal.tsx:963-980` — `ModeSegmented` « Patient existant / Nouveau patient »
  when no preset; the patient is created before the plan, once.
- `web/components/clinic-settings.tsx:1540-1543` — « Besoin d'aide ? » becomes the help rows when a contact exists.
- `web/app/rappels/page.tsx` — S16: « Bientôt disponible », « Rappels par SMS », three steps, facts chips, the
  example SMS, « Retour à l'agenda ». Content limited to `features/sms-rappels-tunisie-sms/blueprint.md` decisions;
  **no date, no price, no count**. `nav.ts:113` untouched.

### Test strategy
Booking dialog regression (riskiest): new patient · duplicate warning both answers · a retry after « créer quand
même » never creates two · phone check. Devis: new patient from Traitements; preset from a patient page; plan
workspace. Header at 320 px (`header-orphans`, `icon-button-is-named`).

---

## Part E — Feedback (release 2)

### Summary
A week-one corner card per person (once, after real use), a « Donner mon avis ou une idée » form any time, each
submission e-mailed to the console team at once and listed in a console page « Retours ». Online version only.

### Files to create
- `api/…/Domain/Entities/ProductFeedback.cs` — **`Entity<Guid>`, not an aggregate** (not audited, not activity;
  precedent `StaffMessage`, `features/staff-chat/notes.md:26-29`): `ClinicId`, `UserId`, `AuthorRole`, `Kind`
  (`idee|probleme|question|avis`), `Source` (`semaine-1|aide`), `HelpRating?`, `EaseRating?` (1–5),
  `LikedAreasCsv?`, `HardAreasCsv?`, `Message?` (trimmed, capped), `FromScreen?`, `MayContact`, `CreatedAt`.
- `api/…/Domain/Entities/UserFeedbackPrompt.cs` — `Entity<string>` keyed by user id, like
  `UserDashboardPreference`: `TimesShown`, `LaterUntil?`, `ClosedAt?` (answered or « never »), `ClosedReason`.
- `api/…/Application/Features/Feedback/FeedbackRules.cs` — kinds, sources, the closed list of areas (Agenda,
  Dossier patient, Odontogramme, Fiche de soins, Devis, Caisse, Documents, Téléphone), `Parse` helpers (strings on
  the wire, never raw enums — `ReminderConsentWireShapeTests`).
- `…/Features/Feedback/Commands/SubmitFeedbackCommand.cs` — refuses an empty submission; closes the prompt when
  `Source = semaine-1`; after commit `ConsoleTeamFeedbackNotice.SendAsync` (best-effort).
- `…/Features/Feedback/Commands/SetFeedbackPromptCommand.cs` — `later` (once, +7 days) / `never`.
- `…/Features/Feedback/Queries/GetFeedbackPromptQuery.cs` — show when: account ≥ 7 days (`User.CreatedAt`), the
  cabinet active on ≥ 3 distinct days in `ClinicActivityDays` (writes > 0), `TimesShown < 2`, not closed,
  `LaterUntil` passed. Showing increments `TimesShown` via a separate « shown » call.
- `…/Features/Feedback/ConsoleTeamFeedbackNotice.cs` — copy of `ConsoleTeamSignupNotice` (fixed subject « Nouvel
  avis sur APEXA », one message per active console account, details escaped, failures logged, never thrown; no
  `{Email}` in log templates — `LogTemplateCoverageTests`).
- `…/Features/Platform/Queries/GetPlatformFeedbackQuery.cs` + DTOs — paged, newest first, kind filter;
  `PlatformTenantScope.EnsureDeclared`.
- `api/…/Controllers/FeedbackController.cs` (`AnyClinicRole`), `Controllers/Platform/PlatformFeedbackController.cs`
  (`PlatformConsole`, `[Route("api/platform")]`).
- Repositories, EF configurations (tenant filter for `ProductFeedback`: `HasQueryFilter(x => IsSystemWide ||
  x.ClinicId == ScopedClinicId)`), one migration.
- `web/components/feedback/feedback-prompt-card.tsx` — S11; opens only when no `data-sheet-open` /
  `data-scroll-locked` / running guide, not within 60 s of sign-in, not while an input has focus; docked above
  `--bottom-inset` on phones; sets a body marker the post-visit popup also respects.
- `web/components/feedback/feedback-dialog.tsx` — S12 + S13 (one form, two entries; `mobile="sheet"`).
- `web/lib/feedback.ts` (areas mirror, pinned by a test reading it), `web/lib/api/feedback.ts`.
- `console/app/retours/page.tsx`, `console/components/feedback-list.tsx` (table ≥ `lg`, cards below),
  `console/lib/api/platform.ts`, a third link in the console navigation (`console/app/cabinets/page.tsx:78-88`).

### Files to modify
- `api/…/Features/Platform/PlatformReadShape.cs` — the feedback field names, each with « written by a clinic user
  TO the vendor, as a message; never copied from a record; the form asks for no patient name » (owner's answer 4).
  `features/platform-console/notes.md` + the « confidentialité » page wording reviewed with the owner.
- `api/…/Common/Behaviors/RealtimeResourceResolver.cs:50-54` — add `Feedback` to `ExcludedAreas`.
- `ClinicArchiveScope.Excluded` — both tables (keeps them off the PC copy too).
- `SubscriptionExemptionCoverageTests.ExpectedExemptWrites` — the writes carry `[AllowsWithoutSubscription("…")]`
  (an expired cabinet saying why it left is the answer most wanted).
- `web/components/post-visit-review-popup.tsx:479-488` — also waits while the feedback card or a guide is up.
- `web/components/help-menu.tsx` (part D) — the « Donner mon avis ou une idée » row opens the dialog.

### Guards
`TenantScopeFilterTests` · `AuditInterceptorTests` unaffected (not an aggregate) · `ClinicRelayScopeTests` /
`ClinicArchiveScopeTests` · `ClinicPurgePlanTests` (ClinicId / FK to Users reach both) · `RealtimeResourceResolverTests` ·
`ControllerAuthorizationCoverageTests` · `SubscriptionExemptionCoverageTests` · `PlatformReadShapeTests` (both
directions) · `ConcurrencyConflictTests` (`UserFeedbackPrompt` may join `SkipsConcurrencyToken` like the dashboard
preference).

### Test strategy
Prompt: < 7 days no · < 3 active days no · « later » once then never more than twice · « never » / an answer end it ·
per person. Submit: empty refused · ratings 1–5 · areas from the closed list · message trimmed and capped · notice
once per console account · a failed e-mail never fails the submit. Console: list, filter, paging, read shape.

---

## Part F — « Me guider » (release 3)

### Summary
Five in-house guides over the real screens. The page dims (drawing only), one control stays lit, a bubble says what
to do, and the guide moves on when the person does it.

### Why in-house (verified)
An open Radix modal sets `pointer-events: none` on `<body>`, closes on any outside press (React-tree based), traps
focus and `aria-hidden`s everything else (`dismissable-layer`, `focus-scope`, `aria-hidden` sources). Any tour
library portals to `<body>` and meets the same wall. The app already has the one way through:
`web/components/ui/floating-notice.ts:5` `FLOATING_OVER_DIALOGS = "[data-staff-chat-notice]"` +
`unlessOnFloatingNotice`, wired into `DialogContent` (`ui/dialog.tsx:222-224`) and `SheetContent`
(`ui/sheet.tsx:140-142`).

### Files to create
- `web/lib/guides/types.ts`, one file per guide (`secretary.ts`, `patients.ts`, `appointment.ts`,
  `dental-record.ts`, `letterhead.ts`).
- `web/components/guide/guide-provider.tsx` — state machine; starts from the card; ends on route change, on the
  target dialog closing without a save, on « Quitter le guide », on session lock.
- `web/components/guide/guide-overlay.tsx` — body portal at `z-[60]`, container `pointer-events-none`, bubble
  `pointer-events-auto`, `onMouseDown preventDefault` (never takes focus), `aria-live="polite"`; spotlight =
  `box-shadow` cut-out around the target rect, tracked with `ResizeObserver` + scroll listeners on `<main>` and the
  dialog body (the window never scrolls, `app-shell.tsx:136-146`); hidden while an `alertdialog` is open; docked
  above `--bottom-inset` below `md`; no animation under reduced motion.

### Files to modify
- `web/components/ui/floating-notice.ts` — the selector list gains the guide bubble; `check-responsive` N52 learns
  the second owner.
- `data-guide="…"` marks on: agenda « Nouveau » and phone « + » (`appointment-calendar.tsx:3093-3099`,
  `:3858-3878`), booking dialog patient mode / names / acts / submit, patients « Ajouter un patient »
  (`patients/page.tsx:148-151`), patient dialog names / submit, patient page « Nouvelle fiche de soins » (both
  renders, `patients/[id]/page.tsx:2587-2596`, `:2610-2617`), fiche act picker / submit, users « Créer un compte »
  / fields / submit, letterhead card toggle / import / dialog steps.
- `web/components/post-visit-review-popup.tsx` — never during a guide.
- `web/scripts/check-responsive.mjs` — new rule `guide-marks-exist`, modelled on
  `tooth-symbol-covers-every-condition` (:425-468): every mark a guide names exists, every `data-guide` in the
  source is used, unparsable → fail. Prove it fails once with a deliberate broken mark.

### Pitfalls (verified)
- **The appointment guide starts from the « Nouveau » button, never `?newAppointment=1`**: the deep link keeps the
  page's opening minute as start time (`appointments/page.tsx:132`, `:316-321`), so a slow user meets « …dans le
  passé ? ».
- Booking success has **no toast**: advance on the `appointments` realtime event or the dialog closing after save.
- Creating a patient navigates to `/patients/{id}` — that route change is the success signal, not an exit.
- The letterhead file picker is the OS dialog — the guide waits for the « Découpe » step to appear.
- `#firstName`/`#lastName` ids are shared by two dialogs — anchor on `data-guide`, never on ids.
- No `data-testid` exists anywhere; marks are new.

### Test strategy
No test runner in `web/` — the browser pass is the test: each guide end to end at 320 / 390 / 820 / 1440 px; leave
mid-way, close a dialog mid-way, a confirmation mid-way, a locked session mid-way; chat notices still float over
dialogs; post-visit question and feedback card never during a guide.

---

## 3. Releases and order of work

| Release | Parts | Before shipping |
|---|---|---|
| R1 | A + B | `verify-schema` before/after the migration; site « Tutoriels » page published (owner's OK, preview first) |
| R2 | C + D + E | `feature/security-remediation` landed; WhatsApp number confirmed; console deployed with the API |
| R3 | F | all marks in place; the new check proven to fail |

Suggested stories (one commit each, full gate after each): A1 server · A2 web · B1 server (rules, query, phone
sign-in, QR endpoint) · B2 web card + dialog + deep links · C1 server · C2 web · D1 help menu + settings · D2
new-patient extraction + devis · D3 Rappels page · E1 server + console API · E2 web prompt + dialog · E3 console
page · F1 engine + guard · F2 the five guides.

## 4. Gates (every story)
- `BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release` —
  **unfiltered**.
- `cd web && npm run check:responsive && npx tsc --noEmit && npm run build`; same for `console/` when touched.
- `node e2e/scripts/check-coverage.mjs`.
- Per release: `/test-in-browser` (tiers A–D from the design page), then `/fix-qa-findings`, three cycles max;
  widths 320 / 390 / 820 / 1180 / 1440 and 730 px tall; a real Android phone for the phone sign-in and the outbound
  links.
- Stack: `.claude/rules/shared-stack.md` — `stack-lease.ps1 status` first; prefer the isolated stack (`:5099` /
  `:3099`, memory `isolated-test-stack`) for this worktree.

## 5. How we'll know
Two weeks after each release, the same read-only production check as 3 Nov (memory `trial-dropoff-findings`):
past the code (10/12 → all), a patient on day 1 (5/12 → > half), a second account within 7 days (1/12 → 1 in 3),
a phone sign-in within 7 days (not recorded → 1 in 3), 3 active days in the first 14 (2/12 → 1 in 3), week-one
answers (→ 1 in 2 asked).
