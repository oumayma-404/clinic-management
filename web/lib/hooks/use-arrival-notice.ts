import { useCallback } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { staffChatApi, type StaffArrivalDto } from "@/lib/api/staff-chat"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"

/**
 * « Signaler l'arrivée » for today's appointments (US-3): who already announced each one, and why one cannot go —
 * the server's own rule, read once for the whole agenda and shared by every block's menu.
 */
export function useArrivalNotice(enabled: boolean) {
  const queryClient = useQueryClient()
  const arrivals = useQuery({
    queryKey: queryKeys.staffChatArrivals,
    queryFn: () => staffChatApi.arrivalsToday(),
    enabled,
    // Another person's arrival in a thread this person is not in wakes nothing here; the 409 covers that race.
    staleTime: 30_000,
    meta: realtimeMeta.staffChat,
  })

  const arrivalFor = useCallback(
    (appointmentId: string): StaffArrivalDto | undefined => arrivals.data?.find((a) => a.appointmentId === appointmentId),
    [arrivals.data],
  )

  const send = useCallback(
    async (appointmentId: string, resend: boolean) => {
      const message = await staffChatApi.sendArrival(appointmentId, resend)
      void queryClient.invalidateQueries({ queryKey: queryKeys.staffChatArrivals }, { cancelRefetch: false })
      return message
    },
    [queryClient],
  )

  return { arrivalFor, send, refresh: arrivals.refetch }
}
