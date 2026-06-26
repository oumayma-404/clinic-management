'use client'

import { useAuthToken } from '@/lib/hooks/use-auth-token'
import { apiGet, apiPost, apiPut, apiDelete } from './client'

/**
 * Hook to use authenticated API calls
 * This hook provides API functions that automatically include the Auth0 access token
 */
export function useAuthenticatedApi() {
  const { accessToken, isLoading } = useAuthToken()

  return {
    isLoading,
    api: {
      get: <T>(endpoint: string, params?: Record<string, any>) => 
        apiGet<T>(endpoint, params, accessToken),
      post: <T>(endpoint: string, data: any) => 
        apiPost<T>(endpoint, data, accessToken),
      put: <T>(endpoint: string, data: any) => 
        apiPut<T>(endpoint, data, accessToken),
      delete: <T>(endpoint: string) => 
        apiDelete<T>(endpoint, accessToken),
    }
  }
}










