# `window.__clinicShell` — the shell bridge contract

**This file is the contract.** Not `web/types/clinic-shell.d.ts`, which describes only the part of it the web
bundle happens to consume today, and not either shell's source, which implements it. When the three disagree, this
file is what the other two are wrong about.

**The rule:** a change to the method set edits **this file** *and* bumps the shell's `version`. One without the
other produces a build that reports a capability set it does not have, and the failure is a French error on a
screen nobody can explain.

---

## The object

One global, `window.__clinicShell`, installed **before the page's own scripts run** and frozen.

| Member | Type | Since | Notes |
|---|---|---|---|
| `version` | `string` | 1 | The shell's own version. Dotted integers — the server parses it with `System.Version`. |
| `platform` | `"android" \| "ios"` | 1 | So a message can name the right store or OS setting. |
| `maxFileBytes` | `number?` | 1 | Ceiling for `saveFile`. **25 MB** on Android. |
| `saveFile(base64, filename, mimeType)` | `void \| Promise<void>` | 1 | Write the file and offer to open or share it. Base64 **without** a `data:` prefix. |
| `print()` | `void` | 1 | Print the current page through the OS print service. |
| `onPushToken(listener)` | `void` | 1 | Register for the OS push token. Inert until Part 6 delivers one. |

### The per-phase method set (FR-6)

| Phase | Methods |
|---|---|
| **1** | `saveFile` · `print` · `onPushToken` |
| 4 | the additional methods AC-57 (biometric gate) and AC-61 (native viewer) require, each named in that phase |

Phase 4's methods are **not** declared here yet, deliberately. A method declared with no implementation is a
capability the web bundle will feature-detect as present and then call into nothing.

---

## The invariant: every read is a feature detection

With `__clinicShell` **absent** — which is every browser, and that is where this app is used today — behaviour
must be **byte-identical** to what it was before the bridge existed. Never a required global, never an
assumption, never a `!`.

```ts
// ✅ The only shape.
const shell = typeof window !== "undefined" ? window.__clinicShell : undefined
if (shell) { … } else { /* exactly the pre-bridge path */ }
```

AC-26 is verified by **deleting the object at runtime** and walking every affected screen, so the property must
stay deletable — it is assigned, not defined as non-configurable.

---

## `saveFile` — how a file leaves the app in a shell

`web/lib/download.ts` is the single caller, and it tries this route **first**, before `navigator.share` and before
any anchor. Inside a WebView a `blob:` download has nowhere to go and `navigator.share` does not exist, so every
later path delivers *nothing* there.

The size refusal happens on the **web** side, against `blob.size`, **before** the bytes are read: the base64
encode is where the memory is actually spent, so a refusal after it is a refusal the device has already crashed
past. `maxFileBytes` exists because that ceiling is a property of the platform's own JS bridge, not of the web app
— base64 inflates a file ~1.33×, so 25 MB arrives as a ~33 MB single Java `String`. An absent `maxFileBytes` means
*the documented default* (25 MB), never "unlimited".

⚠️ **On Android the failure message is native.** A Java exception thrown out of an `@JavascriptInterface` method
is not observable in JavaScript — the WebView logs it and the call returns `undefined` — so `download.ts`'s
`try/catch` can never fire there. `ShellBridge` shows its own French toast instead. One message either way, never
two and never none. On iOS, where the bridge answers through a reply proxy, the web-side `catch` is the one that
speaks.

## `print` — and why `window.print` is shimmed

Android's WebView has **no** `window.print()` implementation at all. Rather than teach every print call site about
the bridge, the shell replaces `window.print` with a shim that prefers `__clinicShell.print()` **and falls back to
the original**. That fallback is the AC-26 half that is easy to miss: with the bridge deleted, `window.print()`
must behave exactly as it does in a WebView with no shell — do nothing — rather than throw.

What comes out honours the app's `@media print` rules, so the rail, the header, the bottom bar, the assistant
launcher and the toaster are absent and the page prints as document content.

## `onPushToken` — registered, inert

Phase 1 registers the listener and never calls it: nothing produces a token until Part 6 wires FCM/APNs. The
delivery seam is a separate global (`window.__clinicShellDeliverPushToken`) that the native side invokes; it is
deliberately **not** a member of `__clinicShell`, so deleting the bridge cannot leave a half-live subscription.

---

## What the bridge is **not**

- **Not a way to observe API responses.** A WebView does not see `fetch`/XHR responses —
  `WebViewClient.onReceivedHttpError` fires only for loads the WebView itself makes. That is why the version floor
  is read over **native HTTP at launch** and why the in-session 426 and the `must_change_password` 403 are handled
  in `web/lib/api/client.ts`. A shell cannot route either one.
- **Not a header writer.** A WebView can add headers to a document load, never to a subresource request, so
  `X-Client-Version` is attached by `apiHeaders(...)` in `web/lib/api/client.ts` from `__clinicShell?.version`.
- **Not origin-checked by itself.** The native `@JavascriptInterface` object is reachable from any page the WebView
  holds; what keeps `__clinicShell` off a foreign origin is that the wrapper above is installed through
  `addDocumentStartJavaScript` scoped to the configured origin, and that every off-origin top-level navigation
  leaves the WebView entirely. Both halves are load-bearing.

## Implemented sets, as of Part 4

| | Android | iOS |
|---|---|---|
| `version` · `platform` · `maxFileBytes` | ✅ | not built (Part 5) |
| `saveFile` | ✅ | not built |
| `print` | ✅ | not built |
| `onPushToken` | ✅ registered, inert | not built |

The Android native object is exposed as `__clinicShellNative` and carries **two** methods (`saveFile`, `print`);
`onPushToken` lives entirely in the injected wrapper because it registers a JavaScript callback. The
**JS-visible** set is the three the table above names, which is the set this contract and FR-6 both state.
