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
| Android SDK platform | **android-36** | `ANDROID_HOME/platforms` |
| Build tools | **36.0.0** | `ANDROID_HOME/build-tools` |
| Platform tools | `adb` 1.0.41+ | `ANDROID_HOME/platform-tools` |
| Gradle | **8.13**, via the committed wrapper — do **not** install one | `./gradlew` |
| AGP / Kotlin | **8.13.0** / **2.2.20**, pinned in `android/build.gradle.kts` | — |

Install the SDK pieces with:

```bash
sdkmanager "platforms;android-36" "build-tools;36.0.0"
```

⚠️ **These six versions are one set and cannot be raised individually** — the chain starts at Google Play, which
refuses a new submission below **targetSdk 36** from 31 August 2026. `compileSdk = 36` needs AGP ≥ 8.9.1; AGP
8.13.0 requires Gradle 8.13; and Gradle above 8.10 is outside Kotlin 2.0.21's supported range. The root
`build.gradle.kts` carries the same note beside the versions themselves. The previous verified set was AGP 8.7.3 /
Gradle 8.9 / Kotlin 2.0.21 / SDK 35.

Point the build at the SDK either with `ANDROID_HOME` or with an `android/local.properties` holding
`sdk.dir=<path>` (git-ignored, machine-specific).

⚠️ **Escape the drive-letter colon**, and use forward slashes for the separators:

```properties
sdk.dir=C\:/Users/you/AppData/Local/Android/Sdk
```

A Java properties file treats both `\` and `:` as significant. Forward slashes have always been required here;
the escaped colon became a **lint error** (`PropertyEscape`) in AGP 8.13's lint, and since this module runs lint
with `warningsAsErrors` an unescaped one now fails the build rather than passing quietly.

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

The key itself is never committed — an upload key that lives in the repository is an upload key anyone with the
repository can publish as. Generate one:

```bash
keytool -genkeypair -v -keystore clinic-upload.jks -keyalg RSA -keysize 4096 \
        -validity 10000 -alias clinic-upload
```

Then copy `android/keystore.properties.example` to `android/keystore.properties` (git-ignored, along with `*.jks`
and `*.keystore`) and fill in the four values. After that, signing needs no arguments:

```bash
./gradlew assembleRelease   # app/build/outputs/apk/release/app-release.apk
./gradlew bundleRelease     # app/build/outputs/bundle/release/app-release.aab  ← what Play wants
```

⚠️ **A machine without `keystore.properties` still builds a release — unsigned.** That is deliberate, not a
failure: the release build type is also the only way to exercise R8, `isShrinkResources` and the
`@JavascriptInterface` keep rule, and only the publishing machine has any business holding the upload key. You can
tell the two apart by the filename — `app-release-unsigned.apk` versus `app-release.apk`. An unsigned APK cannot
be installed on a phone or uploaded to Play.

⚠️ **The earlier instruction here was the `-Pandroid.injected.signing.*` command-line form.** It does work, and it
works *without* any `signingConfigs` block — which is why `GO-LIVE.md`'s claim that the module « can only produce
debug-signed builds » was never true. It is no longer the documented route because a password passed as a Gradle
property lands in shell history and in the process list, it must be retyped correctly on every publish, and it
cannot be reproduced by a CI runner reading secrets from the environment.

⚠️ **Losing this keystore means the listing can never be updated again** under Play's app signing unless key
upgrade is available. Back it up in **two** places that are not this machine. The passwords are not recoverable
either.

### Building the APK for the download page

An APK published on the product's own download page should connect with nothing typed — a phone downloading it has
already been told which deployment it belongs to, and the iOS route has no such step at all because there the user
*arrives* at the server by opening its URL. So pass the address in:

```bash
./gradlew assembleRelease -PclinicServerAddress=front-7476.onrender.com
```

- **Omit the property and the build behaves exactly as it always did** — the address screen on first launch. That
  is the default, and it is what a LAN build wants.
- It is a **starting value, not a compiled-in server**: consulted only when nothing is stored, so
  « Serveur → Changer de serveur… » still reaches any address, and a chosen one is persisted and wins for ever
  after. *One build still serves a clinic's own PC on a LAN and a hosted backend on the internet* — what is new is
  only that a build published for one of them may be aimed at it.
- Give the address **without a port**, exactly as a user would type it. It goes through the same `parseAddress`, so
  `ServerProbe` settles 443-versus-5001 against the real server; naming a port here would pin the build to one
  nobody chose.
- It is deliberately **not** in `gradle.properties`: an address committed to the repository is an address that
  rots, and which deployment an APK is published for is a property of the publish, not of the source.

Then copy the APK into the site and republish — see `landing-v2/DEPLOY.md`:

```bash
cp app/build/outputs/apk/release/app-release.apk ../../landing-v2/dist/GestionClinique-<version>.apk
```

⚠️ The filename carries the version, so **the download link in `landing-v2/template.html` moves with it.** Edit the
template, never `dist/index.html`, which is generated.

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

**`versionCode`** must increase on every upload Play accepts and can never go back down for this
`applicationId`; 1 and 2 were pre-store builds, so 3 is the first that may leave this machine. `versionName` is
what reaches `X-Client-Version` and must stay parseable as dotted integers by `System.Version.TryParse`.

⚠️ **Everything else about submission now lives in [`STORE-SUBMISSION.md`](STORE-SUBMISSION.md)**, because the
answer stopped being « two things still open ». The short version: **Google Play cannot accept this app from a
personal developer account at all** — health and medical apps require a verified Organization account, which
requires a registered legal entity and a D-U-N-S number. Apple has no such rule, so the App Store is *less*
blocked than Play. Until an entity exists, distribution is a **sideloaded APK** on Android and the **installable
web app** on both platforms; neither needs a store, an account or a company.

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
