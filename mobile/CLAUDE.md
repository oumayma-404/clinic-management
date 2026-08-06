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
│       ├── ShellBridge.kt         window.__clinicShell — saveFile · print · onPushToken
│       ├── FileChooser.kt         WebChromeClient.onShowFileChooser + the camera
│       └── ExternalNavigation.kt  off-origin top-level navigations → Custom Tabs
└── ios/                    Swift, `WKWebView`                          (Part 5, NOT built — no Mac, no Apple account)
```

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
- **`onReceivedHttpError` and `onReceivedSslError` are deliberately not overridden.** An HTTP status means the
  server answered, and what it answered with is the app's own French error page — AC-74 requires that be shown, not
  replaced by a shell state. And the default SSL behaviour cancels the load, which is what keeps « untrusted
  certificate » from becoming a silently accepted MITM.
- **`network_security_config.xml` trusts user-installed CAs.** That is what makes the offline-LAN install reachable
  at all: its certificate is self-signed into `.local/` by the API on first boot. Cleartext stays refused.
- **The « Serveur » actions hang off the back gesture at the root**, not off a title bar. A permanent strip of
  chrome would contradict AC-13 (« full-screen, no browser chrome »), and back-at-the-root was otherwise a free
  gesture that closed the app.
- **`@JavascriptInterface` methods run on the WebView's JS thread**, and an exception thrown out of one is
  invisible to JavaScript. `ShellBridge` posts to the UI thread and reports its own failures natively.

## Gotchas

- The build needs a JDK 17 and the Android SDK on the machine; `mobile/README.md` has the exact versions and the
  build commands. There is no standalone `gradle` requirement — the committed wrapper is the entry point.
- `applicationId` is **provisional**. The bundle identifier and the display name are two of Part 8's deferred
  business decisions and an applicationId cannot be changed after the first store submission.
- `mobile/ios/` does not exist yet. Do not write code that assumes it, and do not mark Part 5 done on a simulator —
  a simulator does not faithfully exercise persistent cookies, print or biometrics.
