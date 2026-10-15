import { useCallback, useEffect, useRef, useState } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { staffChatApi, type StaffMessageDto } from "@/lib/api/staff-chat"
import { getErrorMessage } from "@/lib/errors"
import { useSession } from "@/lib/auth/session"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"
import { STAFF_CHAT_KEY } from "@/lib/realtime/clinic-hub"
import { useClinicRealtimeSubscribe } from "@/lib/realtime/clinic-realtime-provider"

/** The « Messages » badge — shared and cached, refreshed by the chat's own signal (staff-chat AC-1.1). */
export function useStaffChatUnread(enabled: boolean) {
  const { user } = useSession()
  return useQuery({
    queryKey: queryKeys.staffChatUnread,
    queryFn: () => staffChatApi.unread(),
    enabled: enabled && Boolean(user),
    meta: realtimeMeta.staffChat,
  })
}

/** The panel's list; read only while the panel is open. */
export function useStaffChatConversations(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.staffChatConversations,
    queryFn: () => staffChatApi.conversations(),
    enabled,
    meta: realtimeMeta.staffChat,
  })
}


/** The cabinet's quick messages in this person's order; a list edit anywhere in the cabinet refreshes it (US-2). */
export function useStaffQuickMessages(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.staffChatQuickMessages,
    queryFn: () => staffChatApi.quickMessages(),
    enabled,
    meta: realtimeMeta.staffChat,
  })
}

export type ThreadMessageState = "sent" | "sending" | "failed"

/** What a press sends: a text, a quick message, or both. */
export interface OutgoingMessage {
  body: string | null
  quickMessage: StaffMessageDto["quickMessage"]
}

export interface ThreadMessage extends StaffMessageDto {
  state: ThreadMessageState
  /** Set on a message typed here until the server has it; the retry key. */
  localId?: string
  error?: string
}

const byTime = (a: StaffMessageDto, b: StaffMessageDto) =>
  a.sentAt === b.sentAt ? a.id.localeCompare(b.id) : a.sentAt < b.sentAt ? -1 : 1

/**
 * One thread, oldest first, with what this tab is still sending kept at the end. A signal or a reconnect re-reads
 * the newest page and merges it by id, so nothing is shown twice and nothing typed here is dropped (AC-2.6).
 */
export function useStaffChatThread(conversation: string | null, viewer: { userId: string; name: string } | null) {
  const [messages, setMessages] = useState<ThreadMessage[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadingOlder, setLoadingOlder] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const subscribe = useClinicRealtimeSubscribe()
  const current = useRef(conversation)
  current.current = conversation

  const merge = useCallback((page: StaffMessageDto[], keepExisting: boolean) => {
    setMessages((previous) => {
      const sent = new Map<string, ThreadMessage>()
      if (keepExisting) for (const m of previous) if (!m.localId) sent.set(m.id, m)
      for (const m of page) sent.set(m.id, { ...m, state: "sent" })
      return [...[...sent.values()].sort(byTime), ...previous.filter((m) => m.localId)]
    })
  }, [])

  const loadNewest = useCallback(
    async (keepExisting: boolean) => {
      const key = current.current
      if (!key) return
      if (!keepExisting) setLoading(true)
      try {
        const page = await staffChatApi.messages(key)
        if (current.current !== key) return
        merge(page.items, keepExisting)
        if (!keepExisting) setNextCursor(page.nextCursor)
        setError(null)
      } catch (err) {
        if (current.current === key) setError(getErrorMessage(err, "Impossible de charger les messages."))
      } finally {
        if (current.current === key) setLoading(false)
      }
    },
    [merge],
  )

  useEffect(() => {
    setMessages([])
    setNextCursor(null)
    setError(null)
    if (conversation) void loadNewest(false)
  }, [conversation, loadNewest])

  useEffect(() => {
    if (!subscribe || !conversation) return
    return subscribe((resource) => {
      if (resource === undefined || resource === STAFF_CHAT_KEY) void loadNewest(true)
    })
  }, [subscribe, conversation, loadNewest])

  const loadOlder = useCallback(async () => {
    const key = current.current
    if (!key || !nextCursor || loadingOlder) return
    setLoadingOlder(true)
    try {
      const page = await staffChatApi.messages(key, nextCursor)
      if (current.current !== key) return
      setMessages((previous) => {
        const ids = new Set(previous.map((m) => m.id))
        const older = page.items.filter((m) => !ids.has(m.id)).map((m) => ({ ...m, state: "sent" as const }))
        return [...older.sort(byTime), ...previous]
      })
      setNextCursor(page.nextCursor)
    } catch (err) {
      setError(getErrorMessage(err, "Impossible de charger les messages plus anciens."))
    } finally {
      setLoadingOlder(false)
    }
  }, [nextCursor, loadingOlder])

  const deliver = useCallback(
    async (key: string, localId: string, outgoing: OutgoingMessage) => {
      try {
        const saved = await staffChatApi.send(key, outgoing.body, outgoing.quickMessage?.key ?? null)
        setMessages((previous) => {
          const others = previous.filter((m) => m.localId !== localId && m.id !== saved.id)
          const sent = [...others.filter((m) => !m.localId), { ...saved, state: "sent" as const }].sort(byTime)
          return [...sent, ...others.filter((m) => m.localId)]
        })
        void queryClient.invalidateQueries({ queryKey: queryKeys.staffChatConversations }, { cancelRefetch: false })
      } catch (err) {
        setMessages((previous) =>
          previous.map((m) =>
            m.localId === localId
              ? { ...m, state: "failed", error: getErrorMessage(err, "Le message n'a pas pu être envoyé.") }
              : m,
          ),
        )
      }
    },
    [queryClient],
  )

  const send = useCallback(
    (outgoing: OutgoingMessage) => {
      const key = current.current
      if (!key || !viewer) return
      const localId = `local-${Date.now()}-${Math.random().toString(36).slice(2)}`
      setMessages((previous) => [
        ...previous,
        {
          id: localId,
          localId,
          conversation: key,
          sender: { userId: viewer.userId, name: viewer.name },
          sentAt: new Date().toISOString(),
          body: outgoing.body,
          quickMessage: outgoing.quickMessage,
          claimedBy: null,
          seenBy: [],
          state: "sending",
        },
      ])
      void deliver(key, localId, outgoing)
    },
    [viewer, deliver],
  )

  const retry = useCallback(
    (localId: string) => {
      const key = current.current
      const failed = messages.find((m) => m.localId === localId)
      if (!key || !failed || (!failed.body && !failed.quickMessage)) return
      setMessages((previous) =>
        previous.map((m) => (m.localId === localId ? { ...m, state: "sending", error: undefined } : m)),
      )
      void deliver(key, localId, { body: failed.body, quickMessage: failed.quickMessage })
    },
    [messages, deliver],
  )

  /** A message the server just answered with (a claim, a release) replaces its copy in place. */
  const apply = useCallback((message: StaffMessageDto) => merge([message], true), [merge])

  return {
    messages,
    apply,
    loading,
    error,
    hasOlder: nextCursor !== null,
    loadingOlder,
    loadOlder,
    reload: () => loadNewest(false),
    send,
    retry,
  }
}

/** Marks a thread read once its newest message from someone else is actually in front of the person (FR-2). */
export function useMarkThreadRead(conversation: string | null, messages: ThreadMessage[], viewerId: string | undefined) {
  const queryClient = useQueryClient()
  const marked = useRef<string | null>(null)

  const markIfSeen = useCallback(() => {
    if (!conversation || !viewerId) return
    if (typeof document === "undefined" || document.visibilityState !== "visible" || !document.hasFocus()) return
    const newest = [...messages].reverse().find((m) => !m.localId && m.sender.userId !== viewerId)
    if (!newest || marked.current === `${conversation}:${newest.id}`) return
    marked.current = `${conversation}:${newest.id}`
    staffChatApi
      .markRead(conversation, newest.id)
      .then(() => {
        void queryClient.invalidateQueries({ queryKey: queryKeys.staffChatUnread }, { cancelRefetch: false })
        void queryClient.invalidateQueries({ queryKey: queryKeys.staffChatConversations }, { cancelRefetch: false })
      })
      // Best-effort: the next view or the next message tries again.
      .catch(() => {
        marked.current = null
      })
  }, [conversation, messages, viewerId, queryClient])

  useEffect(() => {
    markIfSeen()
    window.addEventListener("focus", markIfSeen)
    document.addEventListener("visibilitychange", markIfSeen)
    return () => {
      window.removeEventListener("focus", markIfSeen)
      document.removeEventListener("visibilitychange", markIfSeen)
    }
  }, [markIfSeen])
}
