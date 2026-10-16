# Blast radius — Android in-app update

Written before the first edit (`.claude/rules/regression-safety.md` § 1).

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `ClientRequirements` record (`api/.../Models`) | positional record = the wire DTO of `client-requirements` | `MetaController.ClientRequirements` (`with` expression), `ClientVersionMiddleware`, `ClientVersionMiddlewareTests` (Read ×2), desktop `ClientRequirements.cs`, Android `ClientRequirements.kt`, iOS `ClientRequirements.swift`, web `meta.ts` | unaffected for the 3 shells + web — **additive** `android` block, every top-level field unchanged; the `with` in MetaController keeps compiling |
| 2 | `ClientRequirements.IsBelowFloor` | the floor predicate | one call site: `ClientVersionMiddleware` | must change — gains `platform`; android → Android floor only |
| 3 | `ClientVersionMiddleware` | runs before auth on every `/api` call | every shell + every browser request | must re-test — unfiltered suite; no header ⇒ today's behaviour |
| 4 | `X-Client-Platform` header (new) | sent by `apiHeaders` | 14 raw-`fetch` sites go through `apiHeaders` (`api-headers` check) · CORS is `AllowAnyHeader()` (`Program.cs:649`) | unaffected — additive header, CORS already open to it |
| 5 | `MetaController.ClientRequirements()` | auto-fills from the Windows package | desktop strip reads `currentShellVersion` | unaffected — Windows fill unchanged; Android fill writes only the new block + `storeUrls.android` when empty |
| 6 | `storeUrls.android` auto-filled | read by Android 1.1.1 native wall + web gate | old phones' wall button opens it in the browser | must re-test — an `https` APK URL downloads in the browser (the transition path for 1.1.1 phones) |
| 7 | `client-download/android` (new route) | anonymous, floor-exempt via `ClientDownloadPath` prefix | `ControllerAuthorizationCoverageTests.ExpectedAnonymous` | must change — add `Meta.ClientDownloadAndroid` |
| 8 | `ClientVersionGate` | web takeover on 426 | every shell | must change — Android button + the « Version requise » line (it printed the Windows *current*, not the floor) |
| 9 | Android `ClientRequirements.kt` | native launch read | `MainActivity.startSession` | must change — parse the `android` block, fall back to top level |
| 10 | Android `MainActivity` update panel | shared by « app too old » and « WebView too old » | `showWebViewOutdated` (opens Play by package) | must re-test — the WebView branch keeps `openStoreListing` |
| 11 | `versionCode` / `versionName` | Gradle | `bridge.md` version table, `X-Client-Version` | must change — 1.2.0 / derived code 10200 (> 4) |
| 12 | `deploy/updates/` folder | API reads it for the Windows feed | `ClientFeed` allow-list (no `.apk`), `ClientUpdatePackage.Resolve` (`.exe` only) | unaffected — `.apk` matches neither pattern |
