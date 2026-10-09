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

/**
 * What the Windows app knows about the PC it runs on, for the PC de secours offer (`clinic-pc-copy` AC-1.6–1.8).
 * `diskEncrypted` is **three-valued**: `null` is « je ne sais pas », which is the common answer without elevation,
 * and the offer must not turn it into « non chiffré ». `freeBytes` is the drive the server installs to.
 */
interface ShellRelayHostFacts {
  machineName: string
  hasBattery: boolean
  diskEncrypted: boolean | null
  freeBytes: number | null
}

/**
 * How an `installRelay` ended (AC-1.4, AC-1.11). `declined` is Windows' permission prompt answered « Non » — nothing
 * installed. `refused` is the installer refusing before copying (too little room, already the cabinet's server) or
 * the code refused at pairing. `sentence` is always French and always set: the shell words what only it can know.
 */
interface ShellRelayInstallOutcome {
  outcome: "installed" | "declined" | "refused" | "failed"
  sentence: string
}

/**
 * `relayProbe`'s answer (`clinic-pc-copy` AC-6.2): whether this device reached the PC de secours through its pinned
 * certificate, and this device's own default gateways — the cloud counts the report only from the cabinet's box.
 */
interface ShellRelayProbeResult {
  reached: boolean
  gateways: string[]
}

interface ClinicShell {
  /**
   * The shell's own version, injected before first paint.
   *
   * Sent as `X-Client-Version` on every API call so the server can refuse a shell below its floor. A browser sends
   * no such header at all, which is what keeps the floor from applying to it.
   */
  readonly version: string

  /**
   * Which shell is running. Present so a platform-specific message can name the right store or setting.
   *
   * ⚠️ `"windows"` is the WPF desktop shell, which had **no bridge at all** until the coffre — it was
   * indistinguishable from a plain browser, and its version floor was read over native HTTP before navigation
   * instead. It exposes `version`, `platform` and the vault seam below, and none of the mobile members.
   */
  readonly platform: "android" | "ios" | "windows"

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

  /**
   * This PC's facts for the PC de secours offer — Windows app only (since 1.4). Never rejects: `null` when the shell
   * cannot say, and then the offer is not made. Absent in every browser and on Android, which is AC-1.12.
   */
  relayHostFacts?(): Promise<ShellRelayHostFacts | null>

  /**
   * Turns this PC into the cabinet's PC de secours with a one-time code the cloud issued (AC-1.4): the shell fetches
   * the server installer from the cloud it already uses, shows Windows' permission prompt once, and runs it. Never
   * rejects; the code goes to a file the installer reads and deletes, never onto a command line.
   */
  installRelay?(request: { code: string; needBytes: number }): Promise<ShellRelayInstallOutcome>

  /**
   * While the cloud is locked for a PC de secours that said nothing (AC-6.2): try the PC at the addresses the cloud
   * gave, recognising it only by its certificate's SHA-256. Windows since 1.5, Android since 1.2.0 — never a browser,
   * which cannot tell an untrusted certificate from a PC that is down. Never rejects: `null` when it cannot answer.
   */
  relayProbe?(request: { addresses: string[]; port: number; fingerprint: string }): Promise<ShellRelayProbeResult | null>

  /**
   * `clinic-pc-copy` Part 3 (Windows since 1.6): trade the cloud's ticket on the PC de secours through its pinned
   * certificate and keep that session in the app's cookie for the PC — so after a switch nobody signs in again. Never
   * rejects: `false` when the shell could not.
   */
  relayPrepare?(request: { assertion: string; addresses: string[]; port: number; fingerprint: string }): Promise<boolean>

  /** Part 3: the cloud says the cabinet works on the PC — the shell asks the PC and moves there when it holds. */
  relaySwitch?(): Promise<boolean>

  /**
   * `clinic-pc-copy` D23 (Windows 1.6 · Android 1.3.0): keep the open form's state (a JSON string) in the shell's memory
   * for this window, replacing what it held — `null` forgets it. So a form survives the app switching server.
   */
  carryDraft?(draft: string | null): void

  /** D23: the form state the shell holds, handed over once (and forgotten) — `null` when there is none. Never rejects. */
  takeCarriedDraft?(): Promise<string | null>
}

interface Window {
  readonly __clinicShell?: ClinicShell

  /**
   * How the desktop shell hands the page its coffre folder — a `FileSystemDirectoryHandle` for
   * `{dossier}\coffre`, created native-side with its permission **already granted**, so the page never calls
   * `requestPermission` and the user never meets a picker.
   *
   * ⚠️ **Deliberately not a member of `__clinicShell`**, exactly like `__clinicShellDeliverPushToken` and
   * `__clinicShellDeliverIdentityResult`: AC-26 verifies the bridge by *deleting* it at runtime, and a resolver
   * living on the object would either die with it or keep a live reference to something that is gone.
   *
   * ⚠️ The page **defines** this; the shell calls it. It is absent in every browser, where
   * `lib/vault/handle.ts` falls back to `showDirectoryPicker()`.
   */
  __clinicShellDeliverVault?: (handle: FileSystemDirectoryHandle) => void

  /**
   * Where the shell parks the coffre handle when it arrives **before** the page installed its listener.
   *
   * ⚠️ The two sides genuinely race: the shell posts on navigation-completed, the bundle evaluates whenever it
   * evaluates. Without a parking slot the handle would be lost on exactly the fast loads — and « no coffre »
   * looks identical to « this machine has none », so the bug would read as a configuration problem.
   */
  __clinicShellPendingVault?: FileSystemDirectoryHandle
}
