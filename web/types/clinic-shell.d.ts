/**
 * `window.__clinicShell` — the native shells' bridge, as the **web bundle** sees it.
 *
 * ⚠️ **This type is not the contract.** `mobile/shared/bridge.md` is (it lands with the Android shell), and a
 * change there bumps `version`. This file only describes the part of it the web app actually consumes, and it is
 * deliberately grown one phase at a time: a method declared here with no caller is an API that looks supported and
 * is not, which on a shell means a French error nobody can explain. `print()` and `onPushToken()` are real parts of
 * the Phase 1 contract and are absent below **because nothing in the web bundle calls them yet** — the shell owns
 * both ends of those.
 *
 * ⚠️ **Every read of this object is a feature detection, never an assumption.** With `__clinicShell` absent —
 * i.e. in every browser, which is where this app is used today — behaviour must be byte-identical to what it was
 * before the bridge existed. That is why the property is optional and why nothing here is a required global.
 */
/**
 * What the OS answered when asked to confirm the device owner. Four values, and the three that are not
 * `"confirmed"` are genuinely different actions — see `mobile/shared/bridge.md`'s table.
 *
 * `"unavailable"` is a **first-class** outcome, not an error: a phone with no enrolled biometric and no device
 * credential falls straight through to the password screen, which is what it would have shown anyway (AC-60).
 */
type ShellIdentityOutcome = "confirmed" | "rejected" | "cancelled" | "unavailable"

interface ClinicShell {
  /**
   * The shell's own version, injected before first paint.
   *
   * Sent as `X-Client-Version` on every API call so the server can refuse a shell below its floor. A browser sends
   * no such header at all, which is what keeps the floor from applying to it.
   */
  readonly version: string

  /** Which shell is running. Present so a platform-specific message can name the right store or setting. */
  readonly platform: "android" | "ios"

  /**
   * The largest file, in bytes, this shell can accept through `saveFile`.
   *
   * ⚠️ It exists because the limit is a property of **the shell's own JS bridge**, not of the web app: a base64
   * string crossing Android's `@JavascriptInterface` costs roughly 1.33× the file in a single Java `String`, so
   * the ceiling is a per-platform, per-device memory fact and the shell is the only side that can know it. The web
   * bundle treats a missing value as the documented default rather than as "no limit" — an absent bridge property
   * must never mean an unbounded marshalling attempt.
   */
  readonly maxFileBytes?: number

  /**
   * Hand a file to the OS: write it, then offer to open or share it.
   *
   * Base64 **without** a `data:` prefix. This is the only delivery route that works in a shell — a `blob:`
   * download has nowhere to go in a `WebView`, and `navigator.share` is unavailable there — which is why
   * `lib/download.ts` tries it first and only then falls back to the browser paths.
   */
  saveFile(base64: string, filename: string, mimeType: string): void | Promise<void>

  /**
   * Ask the OS to confirm the device owner, so a session past the inactivity limit can resume with no password.
   *
   * ⚠️ **It never rejects.** The one caller is `components/session-lock-gate.tsx`, which must not fail open, so
   * every failure is a value — a shell that cannot even ask answers `"unavailable"`. Absent method (a Phase 1
   * shell, or any browser) ⇒ the inactivity path is byte-identical to today: the cookie is cleared and the user
   * lands on `/login` with their place remembered (AC-58).
   */
  confirmIdentity?(): Promise<ShellIdentityOutcome>
}

interface Window {
  readonly __clinicShell?: ClinicShell
}
