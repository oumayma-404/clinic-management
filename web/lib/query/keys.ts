import { RealtimeResource, type RealtimeResourceKey } from "@/lib/realtime/clinic-hub"

declare module "@tanstack/react-query" {
  interface Register {
    // The broadcasts that make a query stale; `ClinicRealtimeProvider` invalidates on them.
    queryMeta: { realtime?: readonly RealtimeResourceKey[] }
  }
}

/** Every shared query's key. The first segment names the resource; `realtimeMeta` says what refreshes it. */
export const queryKeys = {
  userStatus: ["clinics", "user-status"] as const,
  procedureTypes: (includeInactive: boolean) => ["proceduretypes", "list", { includeInactive }] as const,
  medications: (includeInactive: boolean) => ["medications", "list", { includeInactive }] as const,
  unreadCount: ["notifications", "unread-count"] as const,
}

export const realtimeMeta = {
  // Clinic settings, the doctor roster and the caller's own account all live on this one read.
  userStatus: { realtime: [RealtimeResource.Clinics, RealtimeResource.Users, RealtimeResource.Doctors] },
  procedureTypes: { realtime: [RealtimeResource.ProcedureTypes] },
  medications: { realtime: [RealtimeResource.Medications] },
  unreadCount: { realtime: [RealtimeResource.Notifications] },
} as const
