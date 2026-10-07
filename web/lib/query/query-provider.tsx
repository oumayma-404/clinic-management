"use client"

import { useEffect, useRef, useState } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { useSession } from "@/lib/auth/session"

/**
 * The tab's one in-memory query cache (`features/performance-caching` 2b). Never persisted: patient data on disk is
 * `features/offline-drafts`' decision, not this one. ⚠️ Emptied whenever the signed-in user changes, so the next
 * person on a shared reception PC never reads the previous one's data.
 */
export function QueryProvider({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // A safety net, not the mechanism: broadcasts invalidate; this bounds what a missed one can cost.
            staleTime: 5 * 60_000,
            gcTime: 10 * 60_000,
            // Same as before the cache: `client.ts` already retries a 401 once, and nothing else retried.
            retry: false,
            refetchOnWindowFocus: true,
          },
        },
      }),
  )

  const { user, isLoading } = useSession()
  const identity = isLoading ? undefined : (user?.email ?? null)
  const previous = useRef<string | null | undefined>(undefined)

  useEffect(() => {
    if (identity === undefined) return
    if (previous.current !== undefined && previous.current !== identity) client.clear()
    previous.current = identity
  }, [identity, client])

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}
