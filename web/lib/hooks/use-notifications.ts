import { useCallback, useEffect, useRef, useState } from "react"
import { notificationsApi } from "@/lib/api/notifications"
import type { NotificationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { showErrorToast } from "@/lib/errors"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

/**
 * Backs the header notification bell + panel. The unread count is fetched on mount (so the badge is
 * live even while the panel is closed); the 50-row list is fetched lazily whenever the panel opens.
 *
 * Real-time: subscribes to the "notifications" resource — on any change the count refetches (and the
 * list too, if the panel is open); a dropped-then-reconnected socket refetches both so the feed
 * self-corrects even without a live push (spec: real-time-unavailable edge). Mark actions update
 * optimistically, then reconcile the badge from the server.
 */
export function useNotifications(isOpen: boolean) {
  const [notifications, setNotifications] = useState<NotificationDto[]>([])
  const [unreadCount, setUnreadCount] = useState(0)
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

  const refetchCount = useCallback(async () => {
    try {
      const { unreadCount } = await notificationsApi.unreadCount()
      if (mountedRef.current) setUnreadCount(unreadCount)
    } catch {
      // The badge is best-effort — a failed/offline count must never surface an error in the header.
    }
  }, [])

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

  // Badge count on mount.
  useEffect(() => {
    void refetchCount()
  }, [refetchCount])

  // List lazily, each time the panel opens (also refetches to self-correct after being offline).
  useEffect(() => {
    if (isOpen) void refetchList()
  }, [isOpen, refetchList])

  // Live updates. Keep the latest open-state in a ref so the subscription isn't torn down on toggle.
  const isOpenRef = useRef(isOpen)
  isOpenRef.current = isOpen
  useClinicRealtime(RealtimeResource.Notifications, () => {
    void refetchCount()
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
  }, [refetchCount])

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
  }, [refetchCount, refetchList])

  return {
    notifications, unreadCount, loading, error,
    refetchCount, refetchList, markRead, markAllRead, dismiss, dismissAll,
  }
}
