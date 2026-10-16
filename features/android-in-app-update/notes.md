# Android in-app update — what shipped

Decided 2026-10-16 (`/think-solution`, option « update inside the app »). Plan: [`blueprint.md`](blueprint.md).
Affected-consumers table: [`blast-radius.md`](blast-radius.md).

## The flow

« Une nouvelle version (X) est disponible » strip above the page → « Mettre à jour » → download from the
cabinet's own server, SHA-256 checked → `PackageInstaller` → Android's « Mettre à jour cette application ? ».
Checked at launch and on resume after 30 min. « ✕ » hides one version until the next cold start. Below the
Android minimum, the native « Mise à jour requise » wall's button runs the same download instead of opening a URL.

## Decisions that are easy to undo by accident

- **The phone has its own floor and its own release.** `Clients:CurrentShellVersion` is auto-filled from the
  Velopack package, i.e. it is the desktop's (1.3.0 against the phone's 1.1.1), and one shared
  `MinimumShellVersion` meant a desktop release could lock out every phone. The `android` block on
  `client-requirements` and `X-Client-Platform` fix both; the top-level fields keep their meaning for every
  client that already reads them.
- **The platform header comes from the WEB app**, not from a new shell. `apiHeaders` reads
  `window.__clinicShell.platform`, which every shipped bridge already has — so the split floor protects older
  phones the day the server deploys, without a new APK.
- **The version is the file NAME** (`APEXA-1.4.0.apk`). Parsing `versionName` out of a binary manifest was not
  worth it; the release workflow names the file from the version it built. A `.part` matches nothing.
- **`storeUrls.android` is auto-filled with the APK URL** when empty. It is what an older phone's native wall opens
  in the browser — their one way onto an updater build if a floor ever walls them.
- **The web gate's button is a same-origin download inside the WebView**, which has nowhere to go — so the shell
  sets a `DownloadListener` that routes the server's own APK (and nothing else) into `ShellUpdater`.
- **`versionCode` is derived** (`major·10000 + minor·100 + patch`): 1.4.0 → 10400. Android refuses an update whose
  code is not higher; the last hand-set code was 6 (1.3.0).
- **An unsigned release is named `app-release-unsigned.apk`**, so `publish-server.ps1` (which copies
  `app-release.apk`) and the workflow's « is it signed » step cannot ship one by mistake.

## What stays manual

- Phones **below 1.4.0** (1.1.1, 1.3.0) install 1.4.0 by hand once — those builds have no updater.
- The **four `ANDROID_*` secrets** (deploy/README.md § « Mettre à jour l'application Android »).
- The **marketing site's** « Télécharger l'APK » still points at the hand-copied file on GitHub Pages. Pointing it at
  `https://app.apexa.tn/api/meta/client-download/android` makes it follow every release — only once the server has
  an APK in `updates/`.

## Not verified yet

- The whole on-device flow (download → unknown-sources screen → confirm → reopen on the new version). No phone was
  available; `lint` + `assembleDebug` + a shrunk `assembleRelease` are green.
- The workflow has not run (it needs the secrets and a push to `main`).
