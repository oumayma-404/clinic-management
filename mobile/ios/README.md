# `mobile/ios/` — the iOS shell, built without a Mac

A thin `WKWebView` shell rendering the hosted server's **own** web bundle. There is no second frontend here: every
screen, label and business rule lives in `web/`. The Swift mirror of `mobile/android/`, and
[`../shared/bridge.md`](../shared/bridge.md) is the contract both implement.

> ## ⚠️ Status: **UNCOMPILED**
>
> This code has **never been compiled, never been signed and never been run** — not on a device, not in a
> simulator. There is no Mac on the development machine, so the CI workflow below is the first compiler it will
> ever meet. Treat every file here as a *proposal* until a green run exists, and do not mark Part 5 implemented on
> the strength of the code being present.
>
> Android's own history is the argument for that caution: two defects in ~300 lines of Kotlin were invisible to
> reading and caught only by tooling — a missing `USE_BIOMETRIC` permission that would have crashed the one path
> the feature exists for, and a NUL byte that had made a file undiffable for a whole part.

## Why there is no `.xcodeproj`

An `.xcodeproj` is a directory of machine-written XML with UUID cross-references. It cannot be reviewed in a diff,
it conflicts on every concurrent edit, and it can only be *authored* by Xcode — which needs the Mac this project
does not have. [`project.yml`](./project.yml) is the project, as text; **XcodeGen** turns it into an `.xcodeproj`
on the CI runner. `.gitignore` keeps the generated one out.

## The free build → phone chain

Nothing below costs money. The one thing it cannot buy is the App Store.

| Step | Tool | Where |
|---|---|---|
| 1. Compile | GitHub Actions `macos-latest` ([`../../.github/workflows/ios-shell.yml`](../../.github/workflows/ios-shell.yml)) | free on **public** repos |
| 2. Download `ClinicShell-unsigned.ipa` | the run's artifacts | browser |
| 3. Sign + install | **Sideloadly** or **AltStore**, free Apple ID | your **Windows** PC, iPhone on USB |
| 4. Trust the app | Réglages → Général → VPN et gestion de l'appareil | iPhone |
| 5. Trust the clinic's CA | see below | iPhone |

⚠️ **GitHub's macOS runners are free for public repositories and billed for private ones.** If this repository is
private, either accept the cost or move the build to a free-tier alternative — **Codemagic** offers free macOS
minutes and works with private repositories. Free-tier terms change; check before relying on either.

### What free signing costs you

- **7-day certificate.** The app stops launching after a week and must be re-signed. Fine for verification,
  unusable for a clinic.
- **Three sideloaded apps** maximum, ten app IDs per week.
- **No push notifications.** The APNs entitlement is paid-only, so Part 6's iOS half stays untestable — the shell
  registers `onPushToken` and nothing will ever call it.
- **No App Store.** That needs the Apple Developer Program, and there is no free path around it.

### Reaching an offline-LAN server

The API mints a self-signed CA into `.local/` on first boot. iOS rejects it until you:

1. Copy the CA certificate to the iPhone (AirDrop, email, or the clinic's own trust page).
2. Open it → Réglages → Profil téléchargé → Installer.
3. **Réglages → Général → Informations → Réglages de confiance de certificat** → enable full trust for it.

Step 3 is the one people miss; without it the app shows « Impossible de joindre le serveur ».

⚠️ There is deliberately **no `NSAppTransportSecurity` exception** in `Info.plist`. `NSAllowsArbitraryLoads` would
accept *any* certificate, which is exactly the silently-accepted MITM the shell exists to refuse.

## Using the app

- **First launch** asks for the server address: a bare host (`192.168.1.10`), `host:port`, or a full URL.
- **Back** is the standard edge swipe.
- **Recovery actions** (« Recharger », « Changer de serveur… ») are reached by **shaking the device**. iOS has no
  system back button, and a permanent title bar would contradict AC-13 (« full-screen, no browser chrome »), so
  shake is the conflict-free gesture left. It is not discoverable — it is documented here because that is the only
  place it can be.

## Known defects and open decisions

### ⚠️ Le défaut de port — a hosted address with no port is unreachable

`ServerConfig.parseAddress` defaults to **5001** when the address carries no explicit port, so
`clinic.example.com` becomes `https://clinic.example.com:5001` and a hosted deployment on 443 cannot be reached
unless the user types `clinic.example.com:443`.

**This is a defect of all three clients** — desktop, Android and iOS — not of this port. It is carried across
deliberately: the three must agree on what a typed address means, and fixing one alone is the
two-answers-to-one-question defect the ports exist to avoid. The fix belongs in one change across all three, and
the likely shape is to probe 443 before 5001 when no port was given, since the launch probe already makes a
request. **Not done. Recorded here so it is not rediscovered as an iOS bug.**

### Avant la première soumission

- **`PRODUCT_BUNDLE_IDENTIFIER` is provisional** (`com.clinicmanagement.shell`, matching Android's
  `applicationId`). A bundle identifier **cannot be changed after the first store submission** — it is one of
  Part 8's four deferred business decisions.
- No signing certificate, no provisioning profile and no App Store Connect record exist.
- There is **no app icon**: `Info.plist` declares no `CFBundleIconName` and no asset catalog is committed, so the
  build produces a blank tile. Fine for sideloaded verification, a rejection for submission.

## Owed verification

Everything. Listed per criterion in
[`../../features/mobile-native-shells/stories/progress.md`](../../features/mobile-native-shells/stories/progress.md).
The first three, in order: **a green CI run**, then the app launching at all, then AC-14 — still signed in after a
cold start, which is what the persistent `WKWebsiteDataStore` is for.
