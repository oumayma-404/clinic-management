'use client'

import { useEffect, useState } from 'react'
import { useSession } from '@/lib/auth/session'
import { getAccessToken } from '@/lib/api/client'

/**
 * Exposes an API token for components that need to pass one explicitly.
 *
 * Acquisition goes through the shared `getAccessToken` helper — never a direct fetch — so there is exactly
 * one place tokens come from (security-hardening R-4).
 *
 * NOTE for the renewal work (P3.4-P3.6): this hook holds the token in state for the component's lifetime,
 * which is safe only while tokens are long-lived. Once the lifetime drops to ~30 minutes, a long-open page
 * would hand out a stale token from here. Prefer calling the shared client (which will renew on 401) over
 * threading a token through props; this hook should re-acquire rather than cache.
 */
export function useAuthToken() {
  const { user, isLoading } = useSession()
  const [accessToken, setAccessToken] = useState<string | null>(null)
  const [tokenLoading, setTokenLoading] = useState(true)

  useEffect(() => {
    if (isLoading) {
      // Still loading user, wait
      return
    }

    if (!user) {
      // No user, set loading to false
      setAccessToken(null)
      setTokenLoading(false)
      return
    }

    // User exists — acquire a token through the single shared helper.
    let active = true
    getAccessToken()
      .then(token => {
        if (!active) return
        setAccessToken(token)
        setTokenLoading(false)
      })
      .catch((err) => {
        if (!active) return
        console.error('Error fetching access token:', err)
        setAccessToken(null)
        setTokenLoading(false)
      })
    return () => {
      active = false
    }
  }, [user, isLoading])

  return { accessToken, isLoading: tokenLoading || isLoading, user }
}







