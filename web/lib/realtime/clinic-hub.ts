import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr"

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
  Notifications: "notifications",
  Invoices: "invoices",
  CnamNomenclature: "cnamnomenclature",
  Medications: "medications",
  DentalActs: "dentalacts",
  TreatmentPlans: "treatmentplans",
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
 * Fetches the bearer token the same way `lib/api/client.ts` does (mode-aware: Auth0 access token in
 * cloud, the local JWT from the cookie in local). SignalR sends it as the `Authorization` header on
 * the negotiate request and as the `access_token` query param on the WebSocket handshake — the API
 * honors both for `/hub` paths.
 */
async function fetchAccessToken(): Promise<string> {
  try {
    const response = await fetch("/bff/auth/token", { credentials: "include" })
    if (response.ok) {
      const data = await response.json()
      return data.accessToken || ""
    }
  } catch {
    // Token endpoint unavailable — the connection attempt will fail and be retried.
  }
  return ""
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
    .withUrl(url, { accessTokenFactory: fetchAccessToken })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
}
