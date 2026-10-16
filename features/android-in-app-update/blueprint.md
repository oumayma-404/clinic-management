# Android in-app update — implementation blueprint

> Chosen 2026-10-16 (`/think-solution`): **update inside the app**, like the Windows shell. Option 2 (web
> banner + link to apexa.tn) and option 3 (Google Play) were declined. Not implemented yet.

## Summary

The phone checks the cabinet's own server for a newer APK, shows « Une nouvelle version (X) est disponible »
with « Mettre à jour », downloads it, checks its SHA-256 and hands it to Android's installer, which asks the
user to confirm. The APK is served by the API from `updates/` exactly like the Windows feed, so it works on the
hosted server **and** on a cabinet's offline LAN server. A CI workflow builds, signs and uploads it.

The phone and Windows shells get **separate version numbers and separate minimums**. Today there is one of
each, and it is the Windows one: `CurrentShellVersion` is auto-filled from the Velopack package
(`MetaController.cs:87-91`), so the phone would compare 1.1.1 against 1.3.0, and raising
`MinimumShellVersion` for Windows would lock out every phone.

## What the user sees

| Moment | Screen |
|---|---|
| Opening the app, or returning to it after 30 min, when a newer APK exists | Native strip: « Une nouvelle version (1.2.1) est disponible. » · **Mettre à jour** · ✕ |
| After pressing it | « Téléchargement… 42 % » in the same strip |
| Download complete | Android's own « Voulez-vous mettre à jour cette application ? » → Mettre à jour |
| First update only | Android first asks to allow « Installer des applis inconnues » for APEXA (its own screen) |
| Below the Android minimum | The existing « Mise à jour requise » wall; its button now runs the same download + install instead of opening a URL |
| Offline, no APK on the server, or anything fails | Nothing (silent), like Windows: « not today » |

## Files to create

- `mobile/android/app/src/main/java/com/clinicmanagement/shell/ShellUpdater.kt`: checks, downloads,
  verifies, installs. Same name and job as `desktop/.../ShellUpdater.cs`.
- `mobile/android/app/src/main/java/com/clinicmanagement/shell/UpdateInstallReceiver.kt`: receives the
  `PackageInstaller` status (`STATUS_PENDING_USER_ACTION` → start the system confirm; failure → strip text).
- `api/ClinicManagement.API/Models/AndroidUpdatePackage.cs`: resolves the newest `APEXA-<version>.apk` in the
  updates folder, with its SHA-256 cached by name+length+mtime. Mirrors `ClientUpdatePackage.cs`.
- `.github/workflows/android-release.yml`: build, sign, upload the APK. Mirrors `client-installer.yml`.
- `api/ClinicManagement.UnitTests/Api/AndroidUpdatePackageTests.cs`
- `api/ClinicManagement.UnitTests/Api/ClientVersionPlatformFloorTests.cs` (or extend
  `ClientVersionMiddlewareTests.cs`)

## Files to modify

| File | Change |
|---|---|
| `api/.../Models/ClientRequirements.cs` | Add `AndroidClient Android` (`MinimumVersion`, `CurrentVersion`, `DownloadUrl`, `Sha256`) read from `Clients:Android:*`. `IsBelowFloor(version, platform)`: `android` → Android floor only; anything else → the existing `MinimumShellVersion`. Top-level fields unchanged, so the desktop and iOS shells keep working. |
| `api/.../Middleware/ClientVersionMiddleware.cs` | Read `X-Client-Platform` and pass it to `IsBelowFloor`. No header → today's behaviour. |
| `api/.../Controllers/MetaController.cs` | `ClientRequirements()` fills `android.currentVersion` / `downloadUrl` / `sha256` from `AndroidUpdatePackage` when the operator left them empty (same « configured wins » rule as Windows). New `GET client-download/android`: anonymous, `PhysicalFile(..., "application/vnd.android.package-archive", enableRangeProcessing: true)`, 404 `{ error }` when there is none. It sits under `ClientDownloadPath`, so it is already exempt from the minimum. |
| `web/lib/api/client.ts` (`apiHeaders`) | Also send `X-Client-Platform` from `window.__clinicShell?.platform`. The web is served by the server, so **every installed shell, 1.1.1 phones included, sends it the day the server deploys**: no shell update needed for the separate minimums. |
| `web/components/client-version-gate.tsx` | Android: the button reads `requirements.android.downloadUrl` and is labelled « Télécharger la mise à jour », not « Google Play ». |
| `web/lib/api/meta.ts` + its types | The `android` block on `ClientRequirements`. |
| `mobile/android/.../ClientRequirements.kt` | Send `X-Client-Platform: android`; parse the `android` block (`minimumVersion`, `currentVersion`, `downloadUrl`, `sha256`); fall back to the top-level fields when the block is absent (older server). |
| `mobile/android/.../MainActivity.kt` | After `startSession` resolves: `ShellUpdater.check`. `onResume`: re-check when the last check is more than 30 min old. `showUpdateRequired`: button runs `ShellUpdater.downloadAndInstall` instead of `openStoreListing`. `setDownloadListener` on the WebView: an `.apk` from the app's own origin goes to `ShellUpdater` (that is how the web gate's button works inside the WebView, where a same-origin link otherwise does nothing). |
| `mobile/android/app/src/main/res/layout/activity_main.xml` | The update strip (text, progress, « Mettre à jour », ✕), above the WebView. 44 px minimum targets. |
| `mobile/android/app/src/main/res/values/strings.xml` | The strip strings. |
| `mobile/android/app/src/main/AndroidManifest.xml` | `REQUEST_INSTALL_PACKAGES`; register `UpdateInstallReceiver` (not exported). |
| `mobile/android/app/build.gradle.kts` | `versionName` / `versionCode` can be overridden by Gradle properties from CI. `versionCode` = `major*10000 + minor*100 + patch` (1.2.0 → 10200, above today's 4). |
| `packaging/publish-server.ps1` | Copy a signed release APK into `server/updates/APEXA-<v>.apk` when one exists, so LAN cabinets ship it. |
| `deploy/updates/.gitignore` | Add `*.apk`. |
| `site/src/pages/index.html:864` | Point « Télécharger l'APK » at `https://app.apexa.tn/api/meta/client-download/android`, like the Windows button. The site then never needs a hand-copied APK again. Needs her OK to push. |
| Docs | `mobile/CLAUDE.md`, `mobile/shared/bridge.md` (document the header; the bridge itself is unchanged), `deploy/README.md` (« Mettre à jour l'application Android »), `mobile/STORE-SUBMISSION.md:119` + `GO-LIVE.md:215` (tick the update-channel decision), `features/mobile-native-shells/notes.md`. |

## Key signatures

```csharp
public sealed record AndroidClient(string MinimumVersion, string CurrentVersion, string DownloadUrl, string Sha256);

public sealed record ClientRequirements(
    string MinimumShellVersion, string CurrentShellVersion, ClientStoreUrls StoreUrls,
    string WindowsSetupSha256, AndroidClient Android)
{
    public bool IsBelowFloor(string? clientVersion, string? platform);   // "android" → Android.MinimumVersion
}

public sealed record AndroidUpdatePackage(string Version, string FileName, long Length, string Sha256, string FullPath)
{
    public static AndroidUpdatePackage? Resolve(IConfiguration configuration, string baseDirectory);
    // pattern ^APEXA-(?<version>\d+\.\d+\.\d+(\.\d+)?)\.apk$ ; newest by System.Version ; ignores *.part
}
```

```kotlin
object ShellUpdater {
    data class Available(val version: String, val downloadUrl: String, val sha256: String)
    fun newerThanInstalled(req: ClientRequirements.Requirements): Available?   // reuses isBelowFloor()
    fun downloadAndInstall(context: Context, update: Available, onProgress: (Int) -> Unit, onFailed: () -> Unit)
    // download → cacheDir/updates/APEXA-<v>.apk → SHA-256 check → PackageInstaller session → commit(PendingIntent)
}
```

## Wiring

- **Server:** none. Static resolvers like `ClientUpdatePackage`; `MetaController` already takes `IConfiguration`.
- **Config:** `Clients:Android:MinimumVersion` / `CurrentVersion` / `DownloadUrl` / `Sha256`, all optional,
  empty by default. Hosted needs no `.env` change: the APK file in `updates/` fills the rest.
- **CI secrets:** `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
  `ANDROID_KEY_PASSWORD`; reuse `VPS_SSH_KEY`, `VPS_SSH_KNOWN_HOSTS`, `VPS_HOST`, `VPS_USER`, `VPS_DEPLOY_DIR`.
- **Workflow trigger:** a `mobile/android/**` change on `main`, an `android-v*` tag, or a manual run. Tag
  `android-v<version>` after upload. Upload as `APEXA-<v>.apk.part`, then `mv` over ssh, so a half-copied file
  is never served.

## Potential pitfalls

- **The signing key is everything.** Every APK must be signed with `mobile/android/clinic-pilot.jks`. If it is
  lost, no installed phone can ever update again. Back it up outside this PC before the first CI release.
- **Phones on 1.1.1 have no updater.** They install the first updater build by hand, once. Do **not** set an
  Android minimum while any phone is on 1.1.1: its web gate button is a same-origin link, and the old shell has
  no download listener, so the button would do nothing.
- **`versionCode` must always go up**, or Android refuses the update (« application non installée »).
- **Android always asks the user to confirm.** No silent install for a sideloaded app.
- **Play Protect may warn** about an app from outside the store. That is expected; the user taps « Installer
  quand même ».
- **On a LAN server the download must trust the cabinet's private certificate**, the same way the
  `ClientRequirements.kt` fetch already does. Reuse its connection setup, never a second one.
- **After the update, Android closes the app.** Check on a real phone whether the system offers « Ouvrir »; if
  not, the user reopens it.
- **Do not reuse `currentShellVersion` on Android.** It is the Windows version. Read the `android` block only.
- **Keep the old fall-back:** a newer phone talking to an older server (no `android` block) must behave
  exactly like 1.1.1 does today.
- **The download is ~140 KB–a few MB.** Use a `.part` file and rename after the SHA-256 matches; never install
  a file whose hash differs.

## Test strategy

| Where | What |
|---|---|
| `UnitTests` (full suite, never a filter) | Android minimum refuses only `X-Client-Platform: android` · the Windows minimum never refuses an `android` request · no platform header → today's behaviour · unreadable values refuse nothing · `client-download/android` is exempt from the minimum · `AndroidUpdatePackage`: newest by version, ignores `.part` and other names, hash matches the bytes, re-hashes on replace, configured URL/version win · `client-download/android` 404 `{ error }` when none · `ControllerAuthorizationCoverageTests.ExpectedAnonymous` gains the new action |
| `web` gate | `npm run check:responsive` + `npx tsc --noEmit` + `npm run build` (header + gate only) |
| CI `android` job | `lint` + `assembleRelease` with the secrets present |
| Real phone, by hand | Install 1.2.0 → publish 1.2.1 → strip appears → update → reopen shows 1.2.1 · first-time « applis inconnues » screen · Android minimum above installed → wall → its button installs · tampered APK (wrong hash) → refused · offline → no strip, no error · ✕ hides it until the next launch |
