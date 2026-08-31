import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr"
import { CLIENT_VERSION_HEADER, getAccessToken } from "@/lib/api/client"

/**
 * Server → client event name (mirrors ClinicHub.EntityChanged on the API). Carries one argument: the
 * lowercase resource key that changed (see RealtimeResource) so a client refetches only its own views.
 */
export const ENTITY_CHANGED_EVENT = "entityChanged"

/**
 * Resource keys carried by ENTITY_CHANGED_EVENT. These mirror the backend RealtimeBroadcastBehavior,
 * which derives the key from a mutating command's feature area (`Features/<Area>/Commands`) lowercased.
 * Keep in sync with the API's feature-area folder names.
 */
export const RealtimeResource = {
  Appointments: "appointments",
  Patients: "patients",
  ProcedureTypes: "proceduretypes",
  Documents: "documents",
  Files: "files",
  Clinics: "clinics",
  Users: "users",
  Stock: "stock",
  // Features/Suppliers/Commands. Genuinely two-user: reception files a dépôt's number while the dentist is
  // looking at the stock article that names it, and « Désactiver » must reach every open picker.
  Suppliers: "suppliers",
  Notifications: "notifications",
  Invoices: "invoices",
  Medications: "medications",
  DentalActs: "dentalacts",
  TreatmentPlans: "treatmentplans",
  // The server has always broadcast this key — RealtimeResourceResolver derives it from the
  // Features.Expenses namespace — and no client listened. La caisse is the screen that needs it.
  Expenses: "expenses",
  // AC-P4.20 — the last four orphans of audit § 9.1. Each was already emitted by its
  // Features/<Area>/Commands folder with nothing on this side able to name it, so the screens below
  // never live-refreshed. RealtimeResourceResolverTests now asserts this map and the backend's emitted
  // set are EQUAL in both directions, so neither side can grow alone again.
  Doctors: "doctors",         // « Mon profil » / Paramètres → Médecins (profile, cachet, working hours)
  LabOrders: "laborders",     // /lab-orders — bons de prothèse, a two-user status lifecycle
  // Recall commands (snooze / « contacté » / send) still exist server-side, so they still BROADCAST this key —
  // which is why it must stay declared here even though /recalls was removed and nothing subscribes to it right
  // now. `RealtimeResourceResolverTests` compares the emitted and declared sets in both directions; dropping this
  // line while `Features/Recall/Commands` exists would fail the build. It gets a subscriber again when the recall
  // worklist gets a new home.
  Recall: "recall",
  WaitingList: "waitinglist", // /waiting-list — the canonical two-user screen (salle d'attente)
  // Derived from Features/DocumentEmails/Commands. Declared because the queue command broadcasts it, and the
  // send history is genuinely two-user: one person queues « Envoyer par email », the row's status then changes
  // under them when the dispatcher picks it up a minute later.
  DocumentEmails: "documentemails",
} as const

export type RealtimeResourceKey = (typeof RealtimeResource)[keyof typeof RealtimeResource]

/**
 * Resolves the SignalR hub URL. The hub is hosted at `/hub/clinic` on the API HOST ROOT — not under
 * the `/api` base. `NEXT_PUBLIC_API_URL` may be absolute (cloud/dev, e.g. `http://localhost:5000/api`)
 * or relative (`/api`, the Local same-origin front-door build), so resolve its origin and swap in the
 * hub path. Returns null when there is no window (SSR / Node import) — the hub is browser-only.
 */
function resolveHubUrl(): string | null {
  if (typeof window === "undefined") return null
  const apiBase = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000/api"
  const apiOrigin = new URL(apiBase, window.location.origin).origin
  return new URL("/hub/clinic", apiOrigin).toString()
}

/**
 * Fetches the bearer token through the shared `lib/api/client.ts` helper (mode-aware: Auth0 access token in
 * cloud, the local JWT in local). SignalR sends it as the `Authorization` header on the negotiate request and
 * as the `access_token` query param on the WebSocket handshake — the API honors both for `/hub` paths.
 *
 * Routed through the shared helper rather than fetching directly so that when tokens become short-lived and
 * renewable, the hub renews by the same path as every REST call (security-hardening R-4). SignalR invokes
 * `accessTokenFactory` on every (re)connect, so a reconnect naturally picks up a fresh token.
 */
async function fetchAccessToken(): Promise<string> {
  try {
    return (await getAccessToken()) ?? ""
  } catch {
    // Token endpoint unavailable — the connection attempt will fail and be retried.
  }
  return ""
}

/**
 * SignalR's own console logging level.
 *
 * <p><b>Silent by default, deliberately.</b> A failed connect is an <i>expected, handled</i> event here: the hub is
 * additive, `useClinicRealtime` catches the rejection and retries every 5s until it succeeds, and the page works
 * throughout via manual refresh. But SignalR's `ConsoleLogger` still writes
 * `Error: Failed to start the connection: ...` on every attempt — so restarting the API, or running the frontend
 * before the API is up, produced a `console.error` every 5 seconds <i>per mounted connection</i>. That contradicts
 * this module's documented contract that connection failures are "never surfaced", and in dev it is worse than
 * noise: Next's dev overlay badges console errors, so the count climbs and the overlay sits over the UI being
 * tested.</p>
 *
 * <p>Set <c>NEXT_PUBLIC_SIGNALR_LOG_LEVEL</c> to a SignalR level name (`Trace`, `Debug`, `Information`,
 * `Warning`, `Error`, `Critical`, `None`) when you actually need to debug the hub — silencing it by default must
 * not mean there is no way to see it.</p>
 */
function resolveLogLevel(): LogLevel {
  const configured = process.env.NEXT_PUBLIC_SIGNALR_LOG_LEVEL?.trim()
  if (!configured) return LogLevel.None

  const byName: Record<string, LogLevel> = {
    trace: LogLevel.Trace,
    debug: LogLevel.Debug,
    information: LogLevel.Information,
    info: LogLevel.Information,
    warning: LogLevel.Warning,
    warn: LogLevel.Warning,
    error: LogLevel.Error,
    critical: LogLevel.Critical,
    none: LogLevel.None,
  }

  // An unrecognised value falls back to silent rather than throwing: a typo in an env var must not be able to
  // break the app, and the hub is the one subsystem whose failure is supposed to be invisible.
  return byName[configured.toLowerCase()] ?? LogLevel.None
}

/**
 * The shell's version on the hub's own HTTP legs (AC-31), read as a feature detection like every other bridge
 * access — absent bridge ⇒ an empty object ⇒ byte-identical to before.
 *
 * ⚠️ **Honest about its reach.** A browser cannot set headers on the WebSocket upgrade, so this rides the
 * negotiate request and the fallback transports and nothing else. That is enough because it is not a gate:
 * `ClientVersionMiddleware` guards `/api`, and the hub is deliberately outside it — realtime is additive
 * (`useClinicRealtime` treats every failure as invisible), so refusing it would cost a stale shell its live
 * refresh without ever telling anyone why.
 */
function shellVersionHeader(): Record<string, string> {
  const version = typeof window !== "undefined" ? window.__clinicShell?.version : undefined
  return version ? { [CLIENT_VERSION_HEADER]: version } : {}
}

/**
 * Builds a clinic hub connection with automatic reconnection. Returns null off the browser.
 * `withAutomaticReconnect` resumes after a dropped connection (AC-4); the initial connect is retried
 * by the caller (see `useClinicRealtime`).
 */
export function createClinicHubConnection(): HubConnection | null {
  const url = resolveHubUrl()
  if (!url) return null

  return new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: fetchAccessToken, headers: shellVersionHeader() })
    .withAutomaticReconnect()
    .configureLogging(resolveLogLevel())
    .build()
}
