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

### ✅ Le défaut de port — fixed, in all three clients at once

`ServerConfig.parseAddress` used to default to **5001** when the address carried no explicit port, so
`clinic.example.com` became `https://clinic.example.com:5001` and a hosted deployment on 443 could not be reached
unless the user typed `clinic.example.com:443`.

It was a defect of **all three** clients — desktop, Android and iOS — and was fixed in one change across all
three, which is the only way it could be fixed: they must agree on what a typed address means. An address with no
port is now left *unresolved* and settled at connect time by `ServerProbe`, which tries **443 before 5001** and
takes the first port that answers (a TLS failure counts as an answer — a self-signed LAN certificate is the
expected case), then persists the result.

**The rule is stated once, in `mobile/CLAUDE.md` § « The port rule ».** Read it before touching `ServerConfig` or
`ServerProbe` in any client. ⚠️ Note the iOS half is still **unverified on a device** like the rest of this shell —
CI proves it compiles.

### Avant la première soumission

- **`PRODUCT_BUNDLE_IDENTIFIER` is settled**: `com.clinicmanagement.shell`, matching Android's `applicationId`.
  A bundle identifier **cannot be changed after the first store submission**, so a change here is a change in
  `android/app/build.gradle.kts` on the same commit or the two stores hold two different products.
- No signing certificate, no provisioning profile and no App Store Connect record exist.
- **The app icon exists** — `Assets.xcassets/AppIcon.appiconset`, one 1024×1024 generated from the single master
  (`web/branding/icon.svg`) by `web/scripts/generate-icons.mjs`, wired up by
  `ASSETCATALOG_COMPILER_APPICON_NAME` in `project.yml`. ⚠️ **Never hand-edit that PNG**: replace the master and
  re-run the script, or the phone's home screen and the browser tab stop being the same mark. It carries **no
  alpha channel** on purpose — App Store Connect rejects an app icon that has one, and a locally-built app
  renders its transparent corners black.
- ⚠️ **The icon has never been seen on a device**, like everything else in this shell. That it compiles is not
  evidence that it renders: a catalog that fails to compile into the bundle produces a *successful build* and a
  white square, which is the failure mode that is only ever found on the first install.

## Owed verification

Everything. Listed per criterion in
[`../../features/mobile-native-shells/stories/progress.md`](../../features/mobile-native-shells/stories/progress.md).
The first three, in order: **a green CI run**, then the app launching at all, then AC-14 — still signed in after a
cold start, which is what the persistent `WKWebsiteDataStore` is for.
