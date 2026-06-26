'use client'

import { useUser } from '@auth0/nextjs-auth0/client'
import { useEffect, useState } from 'react'

export function useAuthToken() {
  const { user, isLoading } = useUser()
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

    // User exists, fetch access token from our API route
    fetch('/api/auth/token')
      .then(res => {
        if (!res.ok) {
          throw new Error('Failed to fetch token')
        }
        return res.json()
      })
      .then(data => {
        setAccessToken(data.accessToken || null)
        setTokenLoading(false)
      })
      .catch((err) => {
        console.error('Error fetching access token:', err)
        setAccessToken(null)
        setTokenLoading(false)
      })
  }, [user, isLoading])

  return { accessToken, isLoading: tokenLoading || isLoading, user }
}







