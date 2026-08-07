# Native shells — build & signing guide (operator)

Two thin shells rendering the clinic server's own web app. Nothing in here is built by CI; the gate is a local
build plus a walk on physical hardware. `CLAUDE.md` beside this file is the architectural map,
[`shared/bridge.md`](shared/bridge.md) is the bridge contract.

| | Android | iOS |
|---|---|---|
| Status | **built** | **not built** — needs macOS + Xcode and an Apple Developer Program membership |
| Toolchain | JDK 17 + Android SDK | Xcode |
| Artifact | `.apk` (sideload) / `.aab` (Play) | `.ipa` |

---

## Android

### What the build machine needs

Verified against exactly this set — a different SDK platform or build-tools revision may work but has not been run
here:

| | Version | Where |
|---|---|---|
| JDK | **Temurin 17** (17.0.20 verified) | `JAVA_HOME` |
| Android SDK platform | **android-35** | `ANDROID_HOME/platforms` |
| Build tools | **34.0.0** (AGP 8.7's default) and **35.0.0** | `ANDROID_HOME/build-tools` |
| Platform tools | `adb` 1.0.41+ | `ANDROID_HOME/platform-tools` |
| Gradle | **8.9**, via the committed wrapper — do **not** install one | `./gradlew` |
| AGP / Kotlin | **8.7.3** / **2.0.21**, pinned in `android/build.gradle.kts` | — |

Point the build at the SDK either with `ANDROID_HOME` or with an `android/local.properties` holding
`sdk.dir=<path>` (git-ignored, machine-specific — use forward slashes, since a Java properties file treats `\` as
an escape).

### Build

```bash
cd mobile/android

./gradlew assembleDebug          # app/build/outputs/apk/debug/app-debug.apk
./gradlew assembleRelease        # needs signing, see below
./gradlew bundleRelease          # the .aab the Play Console wants
```

Install on a connected phone:

```bash
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb logcat -s ClinicShell        # everything the shell logs uses this one tag
```

⚠️ **Smart App Control**, if `Enforced` on a Windows build machine, can refuse a freshly-produced unsigned binary
with `0x800711C7`. That is the OS, not the build. Check with
`(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState` — `1` means
Enforced.

### Signing

Not committed and not scripted, deliberately: an upload key that lives in the repository is an upload key anyone
with the repository can publish as. Generate one, keep it off the machine's disk backups, and pass it in:

```bash
keytool -genkeypair -v -keystore clinic-upload.jks -keyalg RSA -keysize 4096 \
        -validity 10000 -alias clinic-upload

./gradlew bundleRelease \
  -Pandroid.injected.signing.store.file=<abs path>/clinic-upload.jks \
  -Pandroid.injected.signing.store.password=… \
  -Pandroid.injected.signing.key.alias=clinic-upload \
  -Pandroid.injected.signing.key.password=…
```

⚠️ **Losing this keystore means the listing can never be updated again** under Play's app signing unless key
upgrade is available. Back it up somewhere that is not this machine.

### Configuring the server side

The shell reads `GET /api/meta/client-requirements` at launch and refuses to load the app below the floor. Both
values are **operator-owned** and take effect without a restart (`appsettings.Production.json`):

```jsonc
"Clients": {
  "MinimumShellVersion": "1.0.0",   // empty = no floor. A typo refuses nothing, never everything.
  "CurrentShellVersion": "1.0.0",
  "StoreUrls": { "Android": "https://play.google.com/store/apps/details?id=…", "Ios": "" }
}
```

Raise `MinimumShellVersion` only once the newer build is actually **live in the store** — a floor above the highest
published version bricks every phone with no route forward.

### Trusting a self-signed (offline-LAN) server

A `SelfHostedLan` install serves a certificate the API mints itself, so the phone must be told to trust the
clinic's CA. Install it from the server's own device-trust page (the LAN trust listener), then confirm it appears
under *Settings → Security → Encryption & credentials → Trusted credentials → User*. The shell trusts the user
store as well as the system one; it does **not** ignore certificate errors, so an untrusted certificate shows
« Impossible de joindre le serveur de la clinique » rather than connecting anyway.

A hosted deployment with a publicly-trusted certificate needs none of this.

### Using it

1. First launch asks for the server address. A bare host (`192.168.1.10`), `host:port`, or a full URL are all
   accepted; the port defaults to **5001**, the API's HTTPS front door.
2. The address is remembered. Change it later from the back gesture at the root of the app → **Serveur** →
   **Changer de serveur…** — no reinstall.
3. The same menu offers **Recharger** and **Quitter**.

### Avant la première soumission — two things still open

**Settled** — the product is called **« Gestion Clinique »** on every platform (`app_name` in
`res/values/strings.xml`, iOS's `CFBundleDisplayName`, the web app's `PRODUCT_NAME`, the Windows shell and both
installers), and the bundle identifier is **`com.clinicmanagement.shell`** on both stores. Neither can be changed
after the first submission, and the identifier must move on both platforms in one commit or the two stores hold
two different products.

The launcher icon is the adaptive icon in `res/mipmap-anydpi/ic_launcher.xml` — the same tooth mark as the web
tile and the iOS `AppIcon`. ⚠️ Its `ic_launcher_foreground.xml` carries the glyph **by hand**, because an Android
vector drawable is the one output `web/scripts/generate-icons.mjs` cannot rasterise; changing the master means
copying the new `d` across, and the file's own comment says so.

Still open:

1. **The store account owner** and **the hosted domain a reviewer can reach**.
2. **`versionCode`** must increase on every upload; `versionName` is what reaches `X-Client-Version` and must stay
   parseable as dotted integers.

---

## iOS

Not built. Unblocked by a macOS machine with Xcode (or Xcode Cloud / Codemagic) **and** an Apple Developer Program
membership. When it is built, four platform specifics are not optional:

- `WKWebView` with **`WKWebsiteDataStore.default()`** — anything else drops the `local_session` cookie on every cold
  start and the user signs in at every launch.
- `allowsBackForwardNavigationGestures = true` for swipe-back.
- `ShellBridge` as a `WKScriptMessageHandler`; print via `viewPrintFormatter()` + `UIPrintInteractionController`
  (`window.print()` is unreliable in `WKWebView`) — **verify on hardware**.
- `ASWebAuthenticationSession` for the off-origin hand-off. File inputs need no bridge work at all.

⚠️ Do not mark it done on a simulator: it does not faithfully exercise persistent cookies, print or biometrics.
