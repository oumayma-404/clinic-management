# Story 1: Full - A visitor enters the demo with one click

**Status:** APPROVED
**Story Status:** done
**Layer:** Full (vertical slice — see README)
**Depends On:** None
**Blocks:** 2, 4

## Objective

On a `PublicDemo` deployment, a visitor with no session opening `/` lands on a one-screen entry page; « Entrer dans
la démo » signs them in as Dr Monica Geller — no password, no security code, no forced password change — in a
session of their own that ends after 2 h (or 30 min idle) back on the entry page. A minimal cabinet (clinic + team)
is built by the demo API on first boot. Production (`HostedMultiTenant`) is unchanged.

## Acceptance Criteria

_From spec:_

- [ ] AC-1.1 entry screen (APEXA, « Cabinet de démonstration », nothing-saved line, one button)
- [ ] AC-1.2 lands on the dashboard as Dr Geller, < 3 s, never asked a password/code/password change
- [ ] AC-1.3 « Créer mon cabinet » → `https://apexa.tn/#telecharger`
- [ ] AC-1.4 two visitors get two sessions; one ending never signs the other out
- [ ] AC-1.5 `/login`, reset, signup, join → entry screen
- [ ] AC-1.6 a visitor already inside reopening `/` goes to the dashboard
- [ ] AC-7.2 a demo session is refused by production (different key + issuer/audience)
- [ ] FR-1 every capability answered for `PublicDemo`; FR-3 session rules; EC-1 expiry → « La démo a expiré »; EC-9 link preview title

_Story-specific:_

- [ ] Entering writes only a `SessionFamily` row — never the `User` row (no `LastLoginAt`)
- [ ] `POST /api/auth/demo-session` → 404 on every other kind
- [ ] `PermitsOsPush` has a `PublicDemo` arm (boot no longer throws)
- [ ] Nothing reads `Kind == PublicDemo`; `LocalDataProtection` reads `ProtectsKeyRingWithCertificate`

## Entry Criteria

- [ ] spec.md + plan.md APPROVED (done)
- [ ] `stack-lease.ps1 status` read; isolated stack free on `:5099` / `:3099`
- [ ] A throwaway database `clinic_demo_local` created in the shared Postgres (own DB, not `clinic_management`)

## Steps

1. **Deployment kind + capabilities**
   - `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs`: `PublicDemo`, `RefusesWrites`,
     `IssuesDemoSessions`, `ProtectsKeyRingWithCertificate`; the `PublicDemo` column exactly as plan.md US-1 table,
     one-line reason each; `PermitsOsPush` arm → false.
   - `LocalDataProtection.cs:55,142` → the new capability; `OutboundEndpointPolicy.cs` explicit arm.
   - `DeploymentProfileTests.cs`: 4-tuple matrix, new rows, InlineData, `hostedOnlyCapabilities` as needed.
2. **Policy seam**
   - `IDemoPolicy` (Application) + `DemoPolicy` (Infrastructure, `SessionLifetime = 2 h`), registered in `Extensions.cs`.
3. **Session cap**
   - `LocalAuthService.GenerateRefreshToken(..., DateTime? notAfterUtc = null)`.
   - `RefreshTokenCommand`: when `IssuesDemoSessions`, refuse past `family.CreatedAt + SessionLifetime` (end the
     family, `EndedReason = "demo-expired"`) and pass the cap as `notAfterUtc`.
4. **Mint + endpoint**
   - `DemoCabinetLocator` (newest clinic with an active admin **and** ≥ 1 patient — patients arrive in story 4; until
     then accept active admin, and tighten in story 4).
   - `StartDemoSessionCommand`: copy the `TradeRelayAssertionCommand` mint — untrusted family, `AuthenticatedAs`,
     no `RecordSuccessfulLogin`, no `User` update; `demo_rebuilding` when no cabinet.
   - `AuthController`: `POST demo-session` with `[AllowAnonymous]`, `[EnableRateLimiting(AnonymousAuthPolicy)]`,
     `[OnlineOnly("…")]`, 404 before the mediator unless `IssuesDemoSessions` (copy `RelaySession`);
     `mode` gains `isDemo`, `demoSignupUrl` (`Demo:SignupUrl`), `demoBuiltOn`.
   - Coverage guards: `ControllerAuthorizationCoverageTests`, `RelayOnlineOnlyCoverageTests`,
     `SubscriptionExemptionCoverageTests`, `SelfRegistrationGateTests`.
5. **Minimal rebuilder in the API host**
   - `DemoCabinetRebuilder`: `UseSystemWide` for the locator → `LocalClinicProvisioning` (Dr Geller,
     `MustChangePassword = false`, doctor with cachet) → `Deactivate()` the admin → `UseClinic` + `DemoSeedContext`
     → Dr Chandler Bing, Rachel Green, letterhead → `Activate()`.
   - `DemoSeedContext` (Infrastructure): synthetic `HttpContext.User`, `UseClinic`, `AuthenticatedAs` per member.
   - `Program.cs`: on a demo boot with no current cabinet, or `Demo:RebuildOnStart=true`, enqueue one rebuild.
6. **Web entry**
   - `web/lib/api/auth.ts`: `isDemo?`, `demoSignupUrl?`, `demoBuiltOn?`.
   - `web/lib/demo/demo-context.tsx` (`DemoProvider`, `useDemo`) mounted in `app/layout.tsx`.
   - `web/app/demo/page.tsx`: entry / `?expired=1` / rebuilding variants; signed-in → `/`; metadata « APEXA Démo ».
   - `web/app/bff/auth/demo-session/route.ts`: forward `x-forwarded-for`, `writeSessionCookies`, pass 429/503.
   - `public-routes.ts` + `nav.ts` `HIDDEN_PATHS`: `/demo`.
   - login / mot-de-passe-oublié / signup / join: `isDemo` → `router.replace('/demo')`.
   - `lib/auth/session.tsx`: session end on the demo → `/demo?expired=1`.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Common/Interfaces/IDemoPolicy.cs` | demo capabilities for Application |
| `api/ClinicManagement.Infrastructure/Services/DemoPolicy.cs` | implementation |
| `api/ClinicManagement.Application/Features/Demo/DemoRefusals.cs` | codes + French sentences |
| `api/ClinicManagement.Application/Features/Demo/DemoCabinetLocator.cs` | current cabinet |
| `api/ClinicManagement.Application/Features/Auth/Commands/StartDemoSessionCommand.cs` | the mint |
| `api/ClinicManagement.Infrastructure/Services/DemoSeedContext.cs` | acting-member scope |
| `api/ClinicManagement.API/BackgroundJobs/DemoCabinetRebuilder.cs` | minimal build |
| `api/ClinicManagement.UnitTests/Features/Auth/StartDemoSessionCommandTests.cs` | mint rules |
| `web/lib/demo/demo-context.tsx` | `DemoProvider` |
| `web/app/demo/page.tsx` | entry screen |
| `web/app/bff/auth/demo-session/route.ts` | BFF mint |

### Files to Modify

| File | Changes |
|------|---------|
| `DeploymentProfile.cs`, `LocalDataProtection.cs`, `OutboundEndpointPolicy.cs`, `Infrastructure/Extensions.cs` | kind, capabilities, seams |
| `LocalAuthService.cs`, `RefreshTokenCommand.cs` | 2 h cap |
| `AuthController.cs`, `Program.cs` | endpoint, `mode` fields, first-boot rebuild |
| 5 test files listed in step 1 / 4 | matrix + coverage |
| `web/lib/api/auth.ts`, `app/layout.tsx`, `lib/auth/public-routes.ts`, `lib/nav.ts`, `lib/auth/session.tsx`, 4 auth pages | web entry |

## Verification Steps

- [ ] `BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release` — green, unfiltered
- [ ] `check:responsive` + `tsc --noEmit` + `npm run build` green
- [ ] Isolated stack (`PublicDemo`): entry → dashboard; second Chrome profile enters separately; `/login` → `/demo`
- [ ] SQL: `Users.LastLoginAt` for Dr Geller still null after 3 entries; 3 new `SessionFamilies`
- [ ] Refresh after cap (shorten `SessionLifetime` via config in a test run) → `/demo?expired=1`
- [ ] Demo refresh token against a `HostedMultiTenant` API (other key) → 401
- [ ] `HostedMultiTenant` boot: `POST /api/auth/demo-session` → 404; `mode` has no `isDemo: true`

## Exit Criteria

- [ ] All ACs above checked
- [ ] Every gate green; story committed on `feature/demo-cabinet`
- [ ] Story status → `implemented` in README
