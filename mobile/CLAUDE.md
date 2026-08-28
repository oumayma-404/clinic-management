# mobile/ — the native Android & iOS shells

Two thin native shells that render **the hosted server's own web bundle**. There is no second frontend here: the
shell owns a WebView, an address, five French states and a JavaScript bridge, and nothing else. Every screen, every
label and every business rule lives in `web/`.

**Not CI-runnable, operator-verified.** Same standing as `desktop/`: this is a separate toolchain (Gradle + Android
SDK / Xcode) with no CI in the repository, so the gate is a local build plus a walk on physical hardware, recorded
in the feature's `progress.md`. It is in **neither** `api/ClinicManagement.sln` **nor** `web/`, deliberately — a
mobile toolchain must not be able to redden the backend or frontend gate.

## Layout

```
mobile/
├── shared/bridge.md        THE bridge contract — read this before touching either shell
├── android/                Gradle build, Kotlin, `WebView`            (Part 4, built)
│   └── app/src/main/java/com/clinicmanagement/shell/
│       ├── MainActivity.kt        the five states, the WebView, insets, back, the launch version check
│       ├── ServerConfig.kt        the address + SharedPreferences; a port of desktop/ServerConfig.cs
│       ├── ClientRequirements.kt  the native pre-launch read of GET /api/meta/client-requirements
│       ├── ShellBridge.kt         window.__clinicShell — saveFile · print · onPushToken · confirmIdentity
│       ├── BiometricGate.kt       the OS owner check behind confirmIdentity (AC-57…AC-60, API 28+)
│       ├── FileChooser.kt         WebChromeClient.onShowFileChooser + the camera
│       └── ExternalNavigation.kt  off-origin top-level navigations → Custom Tabs
└── ios/                    Swift, `WKWebView`            (Part 5, ⚠️ WRITTEN BUT NEVER COMPILED — no Mac)
    ├── project.yml                the Xcode project as text; XcodeGen builds the .xcodeproj on CI
    ├── README.md                  the free build→phone chain, and what free signing costs
    └── ClinicShell/
        ├── AppDelegate.swift      one window, no scene manifest, no storyboard
        ├── ShellViewController.swift  the five states, the WebView, the launch version check, shake menu
        ├── ServerConfig.swift     a faithful port of ServerConfig.kt — quirks carried, not fixed
        ├── ClientRequirements.swift   the native pre-launch read of GET /api/meta/client-requirements
        ├── ShellBridge.swift      window.__clinicShell over WKScriptMessageHandlerWithReply
        ├── BiometricGate.swift    LAContext.deviceOwnerAuthentication
        ├── ExternalNavigation.swift   off-origin top-level navigations → SFSafariViewController
        ├── ShellPanels.swift      the shared panel shape + the brand tokens
        └── Strings.swift          every French string, as constants
```

⚠️ **`mobile/ios/` has never been compiled, signed or run.** `.github/workflows/ios-shell.yml` on a free
`macos-latest` runner is the first compiler it will meet. Do not treat its presence as Part 5 being done, and read
`mobile/ios/README.md` before touching it.

## The three things a shell must not get wrong

1. **`window.__clinicShell` is always feature-detected.** With it absent — every browser, which is where this app
   is used today — behaviour must be byte-identical to the pre-bridge app. `shared/bridge.md` is the contract and a
   change to the method set edits that file *and* bumps the shell's version.
2. **The address is never compiled in.** One build serves a clinic's own PC on a LAN and a hosted backend on the
   internet. `ServerConfig` persists whatever the user typed and `ServerConfig.parseAddress` is a faithful port of
   the desktop shell's — the two clients must agree on what an address means.
3. **A WebView cannot see its page's `fetch` responses.** So the version floor is read over **native HTTP before
   the app is loaded**, and the in-session 426 and the `must_change_password` 403 are handled in
   `web/lib/api/client.ts`. Anything that needs a response body is web-side work, not shell work.

## Android specifics worth knowing before editing

- **Rotation must not recreate the activity.** `android:configChanges` lists every configuration the shell handles;
  without it Android destroys the activity on rotation or a Split View resize, the WebView goes with it and the app
  reloads — losing whatever was typed in an open dialog. No amount of web-side work can fix that from inside.
- **Insets are consumed as padding, not drawn under.** An app targeting SDK 35 is edge-to-edge on Android 15
  whether it asks or not, and `setDecorFitsSystemWindows(true)` no longer opts out. `applyWindowInsets` pads the
  root instead, so the viewport ends above the gesture bar and the web app's `--bottom-inset` works without
  depending on whether this WebView build reports the navigation bar through `env(safe-area-inset-*)`.
- **`onReceivedHttpError` is deliberately not overridden.** An HTTP status means the server answered, and what it
  answered with is the app's own French error page — AC-74 requires that be shown, not replaced by a shell state.
- **`onReceivedSslError` IS overridden, and only to report the refusal.** It calls `handler.cancel()`, sets
  `mainFrameFailed` and shows « certificat non approuvé ». ⚠️ The earlier reasoning — « the default cancels the
  load, so the failure surfaces as *Impossible de joindre* » — was **wrong, and was found wrong on a physical
  Galaxy S9**: when the SSL handler cancels, `onReceivedError` is *not* raised for the main frame, so
  `mainFrameFailed` stayed false, `onPageFinished` still fired and the shell switched to an **empty WebView** — the
  white rectangle AC-74 forbids. The security property is unchanged: the certificate is still refused and
  `proceed()` appears nowhere in this project. What changed is only that the user is told, and told what to do.
- **`network_security_config.xml` trusts user-installed CAs.** That is what makes the offline-LAN install reachable
  at all: its certificate is self-signed into `.local/` by the API on first boot. Cleartext stays refused.
- **The « Serveur » actions hang off the back gesture at the root**, not off a title bar. A permanent strip of
  chrome would contradict AC-13 (« full-screen, no browser chrome »), and back-at-the-root was otherwise a free
  gesture that closed the app.
- **`@JavascriptInterface` methods run on the WebView's JS thread**, and an exception thrown out of one is
  invisible to JavaScript. `ShellBridge` posts to the UI thread and reports its own failures natively.
- **An async answer comes back through a separate global, never a return value.** `@JavascriptInterface` is
  synchronous and cannot hand back a `Promise`, so `confirmIdentity` parks a resolver by id in the injected
  wrapper and the native side calls `window.__clinicShellDeliverIdentityResult(id, outcome)` — the shape
  `onPushToken` already uses, and deliberately outside `__clinicShell` so deleting the bridge (AC-26) cannot
  leave a resolver the native side can still reach.
- **`BiometricGate` asks only from API 28**, using the *framework* `BiometricPrompt`. `androidx.biometric` would
  add a dependency, force `MainActivity` to be a `FragmentActivity`, and below 28 render its own dialog against
  AppCompat theme attributes `Theme.ClinicShell` does not carry. An API 26–27 device answers `unavailable`, which
  the web side already treats as « fall back to the password screen » — a first-class outcome, not a failure.

## Gotchas

- The build needs a JDK 17 and the Android SDK on the machine; `mobile/README.md` has the exact versions and the
  build commands. There is no standalone `gradle` requirement — the committed wrapper is the entry point.
  ⚠️ **The six build versions are one set and cannot be raised individually**, and the chain starts at Google Play:
  new submissions must target **API 36** from 31 August 2026, `compileSdk = 36` needs **AGP ≥ 8.9.1**, AGP 8.13.0
  *requires* Gradle 8.13, and Gradle above 8.10 is outside Kotlin 2.0.21's range. So « bump targetSdk » is never a
  one-line change here — raising one version alone fails in a way that names the wrong culprit. The note lives beside
  the versions in the root `build.gradle.kts`. AGP 8.13's lint also added checks the old one did not have
  (`PropertyEscape` on `local.properties`' drive-letter colon, `UseKtx`), and under `warningsAsErrors` those are
  build failures, not advice.
- **Google Play cannot accept this app from a personal developer account** — health and medical apps require a
  verified **Organization** account, i.e. a legal entity and a D-U-N-S number. Apple has no such rule, so the App
  Store is *less* blocked than Play (it is blocked on a Mac and on the Swift never compiling). Until an entity
  exists, distribution is a **sideloaded APK** on Android — push included, since FCM needs Play *Services* on the
  phone rather than Play *Store* distribution — and the **installable web app** (`web/app/manifest.ts`) on both
  platforms. Release signing reads a git-ignored `keystore.properties`; with it absent the release build still
  produces an *unsigned* APK on purpose, so R8 and the `@JavascriptInterface` keep rule stay exercisable on a
  machine that holds no key. `mobile/STORE-SUBMISSION.md` is the full checklist and carries the dated policy sources.
- **The name and the identifier are settled**: « APEXA » on every platform, `com.clinicmanagement.shell`
  on both stores. Neither can be changed after the first submission, and the identifier lives in **two** files
  (`android/app/build.gradle.kts`, `ios/project.yml`) that must move together or the stores hold two products.
- **Do not mark Part 5 done on a simulator** — it does not faithfully exercise persistent cookies, print or
  biometrics, which are three of the four things the iOS shell exists to provide. A green CI build proves the
  Swift compiles and nothing more.
- **Both shells now carry the product mark**, and it has **one** source: the `id="mark"` path in
  `web/branding/icon.svg`. `web/scripts/generate-icons.mjs` rasterises the iOS `AppIcon` (and the Windows `.ico`)
  from it directly; Android's `ic_launcher_foreground.xml` is the one copy, because a vector drawable is not
  something `sharp` can emit — its comment names the master, and changing the logo means editing that path too.
  ⚠️ Neither icon has been seen on hardware. A miswired iOS asset catalog builds **successfully** and ships a
  white square.
- Free signing carries **no APNs entitlement**, so Part 6's iOS half cannot be tested on the free path at all.

## The port rule — one rule, three clients

`parseAddress` used to default to port **5001** whenever the typed address carried no explicit port, so a hosted
deployment — reached on **443** over the internet — was unreachable unless the user knew to type `:443`. It was a
defect of **all three** clients and was fixed in **one** change across all three, which is the only way it could be
fixed: they must agree on what a typed address means, or the same string reaches different servers depending on
which client the user happens to hold.

The rule now is:

- An address **with** an explicit port uses it verbatim and is **never probed**.
- An address **without** one is left *unresolved* (`portIsExplicit` false) and settled at connect time against the
  real server: `ServerProbe` tries `candidatePorts` — **443 before 5001** — and the first port that **answers** wins.
  The resolved port is persisted, so the probe costs one round trip per address, not one per launch.

⚠️ **« Answers » deliberately includes a TLS failure.** An offline-LAN server presents a certificate signed by a CA
the device may not have imported yet, so a handshake rejection is the *expected* outcome of probing a live clinic
server; reading it as « nothing here » would send every LAN install to the wrong port. Only a transport failure —
no route, refused, name does not resolve — disqualifies a port.

⚠️ **And « answers » includes a timeout that happened AFTER the connection was established** — which is the hosted
deployment's normal state, not an edge case. A managed host that suspends an idle service (Render's free tier, and
every platform like it) accepts the TCP connection at its edge in milliseconds and only *then* wakes the
application, so the first response takes **13.4 s** measured against the live front end. All three clients gave a
candidate **4 seconds** and read the timeout as dead, so on a cold hosted server the probe disqualified **443 — the
only port a hosted install has** — fell through to the LAN candidate, found nothing, and left the address to the
not-found fallback. The question the probe asks is « is something listening on this port? », and a completed
connection has already answered it; so **the phase the timeout happened in decides, not the timeout itself.**
This was the port rule's own defect shape a second time — one rule, three clients, wrong in all three — and it is
fixed in all three, with the mechanism per platform because each runtime reports the phase differently:
Android does an explicit `connect()` and records that it succeeded; the desktop sets
`SocketsHttpHandler.ConnectTimeout` *below* `HttpClient.Timeout` so the two phases throw different exception types;
iOS cannot separate them at all (`URLSession` reports one `NSURLErrorTimedOut`) and so treats any timeout as an
answer — safe **only because 443 is probed first**, so a firewall silently dropping 5001 is never reached. Do not
reorder the candidates without revisiting that.

⚠️ **443 first, and the order is not arbitrary.** A LAN server refuses 443 instantly, whereas an internet firewall
in front of a hosted server usually *drops* traffic to 5001 — so trying the LAN port first would cost a full
timeout on every hosted launch, while trying the public one first costs a LAN launch nothing measurable.

⚠️ **Sequential, never concurrent.** With two probes in flight the winner is whichever the network returned first,
so one address could resolve differently on two launches. A shell that reaches a different server depending on
timing is worse than one that takes an extra second.

⚠️ **The 443 fallout reached two more places, and both failed silently.** `isSameOrigin` read a URL with *no* port
as 5001, but a portless HTTPS URL **is** 443 — so on a hosted deployment every same-origin link looked external and
was handed to a Custom Tab / `SFSafariViewController`. And Android scoped its document-start bridge script to
`baseUrl` alone; a page served on 443 reports its origin as `https://host` (the URL spec omits a default port), so
the bridge was silently uninstalled on exactly the deployment that has no other way in — the trap iOS's
`bridgeOrigins` had already documented. Both now go through `bridgeOrigins`, which carries the two-entry form.

The mechanism is per-platform (`HttpClient` · `HttpURLConnection` · `URLSession`) because each runtime reports
« the port answered » differently; **the rule is not**. A stored address saved before this existed has no
explicitness flag, which both stores read as *not* explicit — one probe on the next launch and it self-heals,
whereas reading it as explicit would keep an install silently pinned to 5001 for ever.
