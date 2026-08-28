# Store submission — readiness checklist (operator)

What it takes to put « APEXA » on Google Play and the App Store, and what to do in the meantime.

Policy facts here were verified on **2026-08-19** against Google's and Apple's own documentation, and the links are
in § Sources. **Re-check them before acting** — both stores change these rules, and two of the four facts below did
not exist when the shells were written.

> `README.md` beside this file is the build + signing guide. `CLAUDE.md` is the architectural map. This file is
> only about getting the artifact into a store.

---

## The one fact that reorders everything

**Google Play will not accept this app from a personal developer account.** Health and medical apps must be
published from a **verified Organization account**, which requires a registered legal entity and a D-U-N-S number.
Individual accounts were barred from the Medical/Health categories, with existing health apps forced to migrate by
28 January 2026.

This is not a soft preference or a review risk — it is an account-type restriction, so there is no version of
« submit now and sort the paperwork later ». The $25 personal account is a dead end for this product.

**Apple has no equivalent rule.** An *individual* Apple Developer account needs no D-U-N-S and can publish health
apps. So the App Store is **less** entity-blocked than Play; it is blocked on a Mac and on the Swift never having
been compiled instead. (There is a separate Apple requirement — see § App Store.)

### Consequence: two distribution channels that need nothing

Both work today, with no company, no store account and no purchase.

| | Android | iPhone |
|---|---|---|
| **Sideloaded APK** | ✅ send the `.apk`; the user allows install from that source once | ❌ iOS has no general sideloading. Alternative app stores are EU-and-some-regions only, not Tunisia, and still need a paid Apple account plus notarization. AltStore/Sideloadly re-sign every 7 days — fine for your own testing, useless for a clinic |
| **Installable web app** | ✅ Chrome → « Ajouter à l'écran d'accueil » | ✅ Safari → Partager → « Sur l'écran d'accueil » |

⚠️ **Push notifications survive sideloading.** FCM needs Google Play *Services* on the phone — which every
Tunisian Android device has — not Play Store *distribution*. `mobile-native-shells` P6 works on a sideloaded APK.

⚠️ **What sideloading actually costs**: discovery, auto-update, and the store's trust signal. The update question
needs a decision either way — a « nouvelle version disponible » notice, or a download page — the same decision
`GO-LIVE.md` § 4 already records as open for the desktop installer. `Clients:MinimumShellVersion` +
`GET /api/meta/client-requirements` is the mechanism that already exists for telling a phone it is too old.

⚠️ **The web app already declares itself installable** — `web/app/manifest.ts`: `display: "standalone"`, French
locale, stable `id`, and all three icons generated from `web/branding/icon.svg`. What it does **not** have is a
service worker, so on some Chrome versions Android may create a plain shortcut rather than a full WebAPK (own
icon, no browser chrome). **Verify on a real phone before promising it to a clinic**; a minimal service worker is
cheap to add if it turns out to matter.

The web app loses exactly what the shell exists to add: `saveFile`, reliable print, biometric resume, and OS push.
Everything else — every screen, every label, every business rule — is the same code.

---

## Blocked on a legal entity

- [ ] **Register a legal entity.** Needed for Play, and needed anyway: `GO-LIVE.md` § 7 already lists INPDP
      registration for health data, a Data Processing Agreement with the hosting provider, and invoicing clinics.
      Play is surfacing this requirement, not adding it.
      ⚠️ **It may not have to be a company.** Dun & Bradstreet issues D-U-N-S numbers to **sole proprietorships**,
      and Google asks for « business registration or incorporation documents from a government authority » — a
      Tunisian *entreprise individuelle* / *patente* registration is plausibly enough. This is **unconfirmed for
      Tunisia** and Apple is stricter (it explicitly rejects sole traders and DBAs), so confirm it against the Play
      Console signup flow rather than assuming. If it holds, this is weeks and small money rather than a SARL.
- [ ] **Request the D-U-N-S number** (free; instant to ~30 days depending on region). **Start this first** — it is
      the critical path and nothing else about Play can begin without it.
- [ ] Gather what Google asks beside it: government-issued business registration/incorporation documents, proof of
      the organization's physical address, and the authorized representative's own personal ID.
- [ ] **Register Play Console as an Organization** ($25, one-time).
- [ ] Register the accounts to the **entity, never a personal account** — moving them later is painful or
      impossible, and for a health app the account owner is the party held responsible for a data leak.

⚠️ **Do not publish under a third party's existing company** (an employer, a partner, a friend's business). That
entity becomes legally answerable for patient data, the listing and the bundle id belong to them, and transferring
a Play account later is a formal workflow with restrictions.

---

## Ready now ✅

Verified by building it, on 2026-08-19:

- [x] **`targetSdk` / `compileSdk` 36.** Play refuses a new submission below API 36 from **31 August 2026**. Done,
      with the toolchain chain it forced (Gradle 8.13 / AGP 8.13.0 / Kotlin 2.2.20) — see `README.md`.
      An extension to **1 November 2026** is available, but it is a fallback, not a plan.
- [x] **Release signing wired.** `keystore.properties` (git-ignored) → signed `assembleRelease` / `bundleRelease`
      with no command-line arguments; absent, the release still builds unsigned so R8 stays exercisable.
- [x] **`versionCode` 3**, `versionName` `1.1.0`.
- [x] **Lint clean** under `warningsAsErrors` — this module's only static gate — at the new toolchain.
- [x] **Release APK + AAB produced.** R8 takes it from 2.4 MB debug to ~130 KB.
- [x] **Bundle identifier and display name settled**: `com.clinicmanagement.shell`, « APEXA ». ⚠️ The identifier kept its pre-rebrand name deliberately. Neither
      can change after first submission, and the identifier lives in **two** files
      (`android/app/build.gradle.kts`, `ios/project.yml`) that must move together or the stores hold two products.
- [x] **512×512 store icon** — `web/scripts/generate-icons.mjs` already emits `icon-512.png`.

---

## Owed regardless of any store

- [ ] **The hardware walk.** Owed since the shell was written; no physical Android phone has ever run it, and the
      **release** build has never run anywhere. Do it on the signed release APK, not the debug one:
      rotation · Split View · the gesture bar and insets · camera upload · print · biometric resume
      (`confirmIdentity`) · **still signed in after force-quit and cold start** (AC-14) ·
      **a bare hosted domain with no port** (the `ServerProbe` 443-before-5001 path has never met a real hosted
      server) · an untrusted certificate showing « certificat non approuvé » and *not* a white screen ·
      `delete window.__clinicShell` leaving the app byte-identical (AC-26).
      Record it in `features/mobile-native-shells/stories/progress.md` as the existing entries are.
      ⚠️ **The `@JavascriptInterface` keep rule in `proguard-rules.pro` has never run on a device.** If `saveFile`,
      `print` or `confirmIdentity` fail only in release, that rule is the first suspect.
- [ ] **Re-verify two Android 16 behaviours at `targetSdk` 36**, both of which the shell should already handle:
      the **edge-to-edge opt-out is removed** (`applyWindowInsets` pads the root rather than drawing under, so this
      should be a no-op), and **predictive back** — `MainActivity` uses the modern `onBackPressedDispatcher`, not
      the deprecated `onBackPressed()` override, but an always-enabled callback suppresses the system animation and
      the « Serveur » menu depends on catching back at the root.
- [ ] **Privacy policy, published at a public URL.** There is **no such page** in `web/app/` today. Both stores
      require the URL, and clinics will ask for it before the stores do.
- [ ] **Terms of service** (French).
- [ ] Decide the **update channel** for sideloaded builds (see § Consequence above).

---

## Google Play — when the entity exists

- [ ] **Enable Play App Signing**, upload the AAB to **internal testing** first.
- [ ] **Health apps declaration** (App content → Policy). Mandatory, and required on testing tracks too, not just
      production. Declare honestly: disease/condition management, clinical decision support, medication management.
- [ ] **The « not a medical device » disclaimer.** Required where there is no CE mark or FDA clearance — which is
      this product's position. Apps *with* clearance get a verified label instead.
- [ ] **Data safety form.** This app handles health data; answer it against what the app actually collects.
- [ ] **Store listing**: description, phone **screenshots**, and a **feature graphic 1024×500** (must be created —
      only the 512 icon exists).
- [ ] **Privacy policy URL** (mandatory).
- [ ] **App access — the rejection risk nothing else flags.** The shell's *first screen asks for a server address*,
      so a reviewer with no address sees a dead end. All four of these must be true together:
      - a hosted deployment on a real domain with a valid certificate (`GO-LIVE.md` § 3 — not done);
      - a demo clinic with a **live subscription** (`subscription-grant`), or `SubscriptionGateMiddleware` answers
        **402** on every write the reviewer tries;
      - a demo user of role **`doctor`, never `admin`** — `HostedMultiTenant` sets `requiresAdminSecondFactor:
        true`, and `LoginCommand`'s rule is `IsTotpEnrolled || (RequiresAdminSecondFactor && IsAdmin())`, so an
        admin is forced into TOTP enrolment and a reviewer cannot pass it. A non-enrolled doctor signs in on
        password alone;
      - ⚠️ `CreateClinicUserCommand` creates users with `mustChangePassword: true`. **Sign in once yourself, change
        the password, confirm no TOTP prompt appears**, then hand over the settled credentials.
      Put the domain, the credentials and « type this address on the first screen » into the App access section.
- [ ] **Organization accounts are exempt from the 12-testers-for-14-days rule** that applies to personal accounts
      created after 13 November 2023 — so going Organization removes that clock as well as unblocking the category.

### After the listing is live

Set in `appsettings.Production.json` (operator-owned, no restart needed):

- [ ] `Clients:StoreUrls:Android` → the listing URL
- [ ] `Clients:CurrentShellVersion` → `1.1.0`
- [ ] ⚠️ **Leave `Clients:MinimumShellVersion` empty until the build is actually live in the store.** A floor above
      the highest published version bricks every phone with no route forward. Raising it is the last step, never
      the first.

---

## App Store — the blockers are different

Not entity-blocked, but not close either.

- [ ] **Get `.github/workflows/ios-shell.yml` green.** The Swift in `mobile/ios/` has **never been compiled by
      anything**. A green free-runner build is the cheapest proof it is real, and it costs nothing.
- [ ] **Apple Developer Program**, ~$99/year. An **individual** enrolment needs no D-U-N-S.
- [ ] **A Mac** (or a paid macOS runner) to archive and upload. A free CI build proves the Swift compiles and
      nothing more.
- [ ] **Declare regulated-medical-device status in App Store Connect.** Required for *new* Health & Fitness and
      Medical apps in the EEA, UK and US since **26 March 2026**; existing apps must declare by early 2027 or lose
      the ability to submit updates. The answer here is « not a regulated medical device ».
- [ ] `PRODUCT_BUNDLE_IDENTIFIER` in `ios/project.yml` — identical to Android's `applicationId`.
- [ ] Register the App ID, certificates and provisioning profiles.
- [ ] **TestFlight onto a real iPhone.** ⚠️ Do not accept a simulator: it does not faithfully exercise persistent
      cookies, print or biometrics — three of the four reasons the iOS shell exists. A miswired asset catalog also
      builds **successfully** and ships a white square.
- [ ] App Store listing, privacy policy, and the **App Privacy** questionnaire (health data).
- [ ] Submit. Budget 1–3 days per review and expect at least one rejection round.

⚠️ Free signing carries **no APNs entitlement**, so P6's iOS push half cannot be tested on the free path at all.

---

## Sources

Verified 2026-08-19. Re-check before acting.

- [Target API level requirements for Google Play apps](https://support.google.com/googleplay/android-developer/answer/11926878)
- [Meet Google Play's target API level requirement](https://developer.android.com/google/play/requirements/target-sdk)
- [Play Console requirements](https://support.google.com/googleplay/android-developer/answer/10788890)
- [Health apps declaration form](https://support.google.com/googleplay/android-developer/answer/14738291)
- [App testing requirements for new personal developer accounts](https://support.google.com/googleplay/android-developer/answer/14151465)
- [Required information to create a Play Console developer account](https://support.google.com/googleplay/android-developer/answer/13628312)
- [AGP 8.13.0 release notes](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
- [Claim a free D-U-N-S number](https://www.dnb.com/en-us/smb/duns/get-a-duns.html)
- [Apple: regulated medical device apps in the EEA, UK and US](https://developer.apple.com/news/?id=nyqbfz1y)
- [Apple: D-U-N-S requirement by enrolment type](https://developer.apple.com/support/D-U-N-S/)
