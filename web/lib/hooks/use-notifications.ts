import { useCallback, useEffect, useRef, useState } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { notificationsApi } from "@/lib/api/notifications"
import type { NotificationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { showErrorToast } from "@/lib/errors"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useSession } from "@/lib/auth/session"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"

/**
 * Backs the header notification bell + panel. The unread count is a shared cached query (so the badge is
 * live even while the panel is closed); the 50-row list is fetched lazily whenever the panel opens.
 *
 * Real-time: subscribes to the "notifications" resource — on any change the count refetches (and the
 * list too, if the panel is open); a dropped-then-reconnected socket refetches both so the feed
 * self-corrects even without a live push (spec: real-time-unavailable edge). Mark actions update
 * optimistically, then reconcile the badge from the server.
 */
export function useNotifications(isOpen: boolean) {
  const [notifications, setNotifications] = useState<NotificationDto[]>([])
  const { user } = useSession()
  const queryClient = useQueryClient()
  // Cached and shared: the header remounts on every page and used to re-read the badge on each sidebar click.
  // Refreshed by the `notifications` broadcast; best-effort, so a failed count shows the last one, or 0.
  const countQuery = useQuery({
    queryKey: queryKeys.unreadCount,
    queryFn: () => notificationsApi.unreadCount(),
    enabled: Boolean(user),
    meta: realtimeMeta.unreadCount,
  })
  const unreadCount = countQuery.data?.unreadCount ?? 0
  const setUnreadCount = useCallback(
    (count: number) => queryClient.setQueryData(queryKeys.unreadCount, { unreadCount: count }),
    [queryClient],
  )
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // The list as it stands right now, for the two dismiss callbacks: they must be able to put the previous list
  // back when the write fails, and reading `notifications` from the closure would either capture a stale array
  // or force the callbacks to change identity on every feed update.
  const notificationsRef = useRef<NotificationDto[]>([])
  notificationsRef.current = notifications

  // DashboardHeader is rendered per-page, so navigating between routes unmounts this hook mid-fetch.
  // Skip post-await setState once unmounted so we never touch a dead component.
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  // Joins a refetch already in flight (the broadcast echo of this user's own mark-read) instead of repeating it.
  const refetchCount = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.unreadCount }, { cancelRefetch: false })
  }, [queryClient])

  const refetchList = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await notificationsApi.list()
      if (mountedRef.current) setNotifications(list)
    } catch (err) {
      if (mountedRef.current) {
        setError(err instanceof ApiError ? err.message : "Erreur lors du chargement des notifications")
      }
    } finally {
      if (mountedRef.current) setLoading(false)
    }
  }, [])

  // List lazily, each time the panel opens (also refetches to self-correct after being offline).
  useEffect(() => {
    if (isOpen) void refetchList()
  }, [isOpen, refetchList])

  // Live updates. Keep the latest open-state in a ref so the subscription isn't torn down on toggle.
  const isOpenRef = useRef(isOpen)
  isOpenRef.current = isOpen
  // The badge refreshes through its query's realtime meta; only the open list is re-read here.
  useClinicRealtime(RealtimeResource.Notifications, () => {
    if (isOpenRef.current) void refetchList()
  })

  const markRead = useCallback(async (id: string) => {
    setNotifications((prev) => prev.map((n) => (n.id === id ? { ...n, isRead: true } : n)))
    try {
      await notificationsApi.markRead(id)
    } catch {
      // Ignore — the realtime broadcast / next refetch reconciles state.
    } finally {
      void refetchCount()
    }
  }, [refetchCount])

  const markAllRead = useCallback(async () => {
    setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })))
    setUnreadCount(0)
    try {
      await notificationsApi.markAllRead()
    } catch {
      // Ignore — reconciled by refetch below.
    } finally {
      void refetchCount()
    }
  }, [refetchCount, setUnreadCount])

  /**
   * Clears one row from this user's bell.
   *
   * ⚠️ **Optimistic, and it reconciles on failure rather than swallowing it.** `markRead` above may safely ignore
   * a failed write — the row stays, merely still bold. Here the row has already left the list, so an ignored
   * failure would show an empty bell over a notification the server still holds, and the next refetch would put
   * it back with no explanation. A failure therefore restores the list and re-reads.
   */
  const dismiss = useCallback(async (id: string) => {
    const previous = notificationsRef.current
    setNotifications((prev) => prev.filter((n) => n.id !== id))
    try {
      await notificationsApi.dismiss(id)
    } catch (err) {
      if (mountedRef.current) setNotifications(previous)
      showErrorToast(err)
      void refetchList()
    } finally {
      void refetchCount()
    }
  }, [refetchCount, refetchList])

  /** Empties this user's bell. Same optimistic-then-reconcile shape as {@link dismiss}. */
  const dismissAll = useCallback(async () => {
    const previous = notificationsRef.current
    setNotifications([])
    setUnreadCount(0)
    try {
      await notificationsApi.dismissAll()
    } catch (err) {
      if (mountedRef.current) setNotifications(previous)
      showErrorToast(err)
      void refetchList()
    } finally {
      void refetchCount()
    }
  }, [refetchCount, refetchList, setUnreadCount])

  return {
    notifications, unreadCount, loading, error,
    refetchCount, refetchList, markRead, markAllRead, dismiss, dismissAll,
  }
}
