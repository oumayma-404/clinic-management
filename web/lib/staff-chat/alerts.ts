"use client"

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react"
import { staffChatApi, type StaffChatUnreadDto, type StaffMessageDto, type StaffQuickMessageDto } from "@/lib/api/staff-chat"
import { useClinicRealtimeSubscribe } from "@/lib/realtime/clinic-realtime-provider"
import { formatChatTime } from "./format"
import { installAudioUnlock, isAudioBlocked, onAudioBlockedChange, playChime, resumeAudio } from "./chime"

/**
 * The ONE place a chat message makes itself noticed (staff-chat US-4): the sound, the in-app notice, the browser
 * notification. `check:responsive` N52 keeps it that way — two owners is two sounds for one message.
 */

const ARRIVAL_KEY = "patient-arrived"
const NOTICE_MS = 8_000
const MAX_NOTICES = 3
const CATCH_UP_AT = 3
const CLAIM_PREFIX = "staffchat:alerted:"
const CLAIM_TTL_MS = 2 * 24 * 60 * 60 * 1000
const MUTE_KEY = "staffchat:sound-muted"
const TAB = Math.random().toString(36).slice(2)

/** What a notice says: the quick message or « Nouveau message » — never free text, never a patient's name (AC-4.1). */
export function noticeText(message: StaffMessageDto): string {
  const label = message.quickMessage?.label ?? "Nouveau message"
  const visit = message.appointment
  if (message.quickMessage?.key === ARRIVAL_KEY && visit?.startsAt && !visit.missing && !visit.cancelled) {
    return `${label} · RDV ${formatChatTime(visit.startsAt)}`
  }
  return label
}

const isUrgent = (message: StaffMessageDto) => Boolean(message.quickMessage?.urgent)

// ── The notices ──────────────────────────────────────────────────────────────────────────────────────────────

export interface StaffNotice {
  message: StaffMessageDto
  urgent: boolean
}

let notices: StaffNotice[] = []
const timers = new Map<string, ReturnType<typeof setTimeout>>()
const noticeListeners = new Set<() => void>()
const EMPTY: StaffNotice[] = []

function emitNotices() {
  for (const listener of [...noticeListeners]) listener()
}

function startTimer(id: string, ms: number) {
  clearTimeout(timers.get(id))
  timers.set(id, setTimeout(() => dismissNotice(id), ms))
}

function addNotices(messages: StaffMessageDto[]) {
  const fresh = messages.filter((m) => !notices.some((n) => n.message.id === m.id))
  if (fresh.length === 0) return
  let next = [...notices, ...fresh.map((m) => ({ message: m, urgent: isUrgent(m) }))]
  // Past three, the oldest ordinary one goes first; an urgent one stays until it is answered.
  while (next.length > MAX_NOTICES) {
    const drop = next.findIndex((n) => !n.urgent)
    if (drop === -1) break
    clearTimeout(timers.get(next[drop].message.id))
    timers.delete(next[drop].message.id)
    next = next.filter((_, i) => i !== drop)
  }
  notices = next
  for (const m of fresh) if (!isUrgent(m)) startTimer(m.id, NOTICE_MS)
  emitNotices()
}

export function dismissNotice(id: string) {
  clearTimeout(timers.get(id))
  timers.delete(id)
  if (!notices.some((n) => n.message.id === id)) return
  notices = notices.filter((n) => n.message.id !== id)
  emitNotices()
}

/** Hovering or focusing an ordinary notice holds it; leaving gives it a few seconds more. */
export function holdNotice(id: string, held: boolean) {
  const notice = notices.find((n) => n.message.id === id)
  if (!notice || notice.urgent) return
  if (held) clearTimeout(timers.get(id))
  else startTimer(id, NOTICE_MS / 2)
}

/** Read anywhere, or taken by a colleague: the notice has nothing left to ask. */
function withdrawNoticesExcept(stillOpen: Set<string>) {
  const gone = notices.filter((n) => !stillOpen.has(n.message.id))
  if (gone.length === 0) return
  for (const n of gone) {
    clearTimeout(timers.get(n.message.id))
    timers.delete(n.message.id)
  }
  notices = notices.filter((n) => stillOpen.has(n.message.id))
  emitNotices()
}

function clearNotices() {
  for (const timer of timers.values()) clearTimeout(timer)
  timers.clear()
  if (notices.length === 0) return
  notices = []
  emitNotices()
}

export function useStaffChatNotices(): StaffNotice[] {
  return useSyncExternalStore(
    (listener) => {
      noticeListeners.add(listener)
      return () => noticeListeners.delete(listener)
    },
    () => notices,
    () => EMPTY,
  )
}

/** One press on a reply: sent to the message's conversation and the message counts as read (AC-4.2). */
export async function replyFromNotice(message: StaffMessageDto, reply: StaffQuickMessageDto) {
  await staffChatApi.send(message.conversation, {
    body: null,
    quickMessageKey: reply.key,
    patientId: null,
    appointmentId: null,
  })
  dismissNotice(message.id)
  await staffChatApi.markRead(message.conversation, message.id).catch(() => {})
}

// ── One sound per message per device (EC-9) ──────────────────────────────────────────────────────────────────

/** The ids this tab won. Two tabs writing at once: the last writer wins, and only it rings. */
async function claim(ids: string[]): Promise<string[]> {
  try {
    const fresh = ids.filter((id) => !localStorage.getItem(CLAIM_PREFIX + id))
    for (const id of fresh) localStorage.setItem(CLAIM_PREFIX + id, `${TAB}:${Date.now()}`)
    if (fresh.length === 0) return []
    await new Promise((resolve) => setTimeout(resolve, 60))
    return fresh.filter((id) => localStorage.getItem(CLAIM_PREFIX + id)?.startsWith(`${TAB}:`))
  } catch {
    // No storage (private window, blocked site data): a second sound beats none.
    return ids
  }
}

function pruneClaims() {
  try {
    const now = Date.now()
    for (let i = localStorage.length - 1; i >= 0; i--) {
      const key = localStorage.key(i)
      if (!key?.startsWith(CLAIM_PREFIX)) continue
      const at = Number(localStorage.getItem(key)?.split(":")[1])
      if (!Number.isFinite(at) || now - at > CLAIM_TTL_MS) localStorage.removeItem(key)
    }
  } catch {
    // Nothing to tidy without storage.
  }
}

// ── Sound and browser notifications: the person's choices on this device (AC-4.4, AC-4.6) ────────────────────

const preferenceListeners = new Set<() => void>()

function readMuted(): boolean {
  try {
    return localStorage.getItem(MUTE_KEY) === "1"
  } catch {
    return false
  }
}

function sound(urgent: boolean) {
  // Urgent rings even when muted (AC-4.5).
  if (!urgent && readMuted()) return
  playChime(urgent)
}

export type BrowserNotificationState = "unsupported" | "default" | "granted" | "denied"

/** In the Windows app: its own notifications (`notify`), or an app too old to have them (EC-10). */
export type ShellNotificationState = "none" | "windows" | "outdated"

function shellNotificationState(): ShellNotificationState {
  const shell = typeof window === "undefined" ? undefined : window.__clinicShell
  if (!shell || shell.platform !== "windows") return "none"
  return typeof shell.notify === "function" ? "windows" : "outdated"
}

function browserNotificationState(): BrowserNotificationState {
  if (typeof window === "undefined" || !("Notification" in window)) return "unsupported"
  return Notification.permission
}

/** Windows' own notification in the desktop app (AC-4.3), else the browser's (AC-4.4). */
function raiseSystemNotification(message: StaffMessageDto, open: (conversation: string) => void) {
  const shell = window.__clinicShell
  if (typeof shell?.notify === "function") {
    // Only while the window is not in front: there, the in-app notice already says it.
    if (!document.hasFocus()) {
      shell.notify({ title: message.sender.name, body: noticeText(message), tag: message.conversation, urgent: isUrgent(message) })
    }
    return
  }
  showBrowserNotification(message, open)
}

function showBrowserNotification(message: StaffMessageDto, open: (conversation: string) => void) {
  if (browserNotificationState() !== "granted" || document.visibilityState === "visible") return
  try {
    const notification = new Notification(message.sender.name, {
      body: noticeText(message),
      tag: message.id,
      icon: "/icon-192.png",
      silent: true,
      requireInteraction: isUrgent(message),
    })
    notification.onclick = () => {
      window.focus()
      open(message.conversation)
      notification.close()
    }
  } catch {
    // A browser that refuses page-created notifications still has the sound and the badge.
  }
}

export function useAlertPreferences() {
  const subscribe = useCallback((listener: () => void) => {
    preferenceListeners.add(listener)
    const stopAudio = onAudioBlockedChange(listener)
    return () => {
      preferenceListeners.delete(listener)
      stopAudio()
    }
  }, [])
  const muted = useSyncExternalStore(subscribe, readMuted, () => false)
  const audioBlocked = useSyncExternalStore(subscribe, isAudioBlocked, () => false)
  const [notifications, setNotifications] = useState<BrowserNotificationState>("unsupported")
  const [shell, setShell] = useState<ShellNotificationState>("none")
  useEffect(() => {
    setNotifications(browserNotificationState())
    setShell(shellNotificationState())
  }, [])

  const setMuted = useCallback((value: boolean) => {
    try {
      if (value) localStorage.setItem(MUTE_KEY, "1")
      else localStorage.removeItem(MUTE_KEY)
    } catch {
      // The choice holds for this page only.
    }
    for (const listener of [...preferenceListeners]) listener()
  }, [])

  const askNotifications = useCallback(async () => {
    if (browserNotificationState() !== "default") return
    try {
      setNotifications(await Notification.requestPermission())
    } catch {
      setNotifications(browserNotificationState())
    }
  }, [])

  return { muted, setMuted, audioBlocked, enableAudio: resumeAudio, notifications, askNotifications, shell }
}

// ── The decision ─────────────────────────────────────────────────────────────────────────────────────────────

interface AlertInput {
  unread: StaffChatUnreadDto | undefined
  /** The signed-in person; a change starts over, so the next person inherits nothing. */
  identity: string | null
  /** The inactivity cover is up: sound and badge only (AC-4.7, EC-13). */
  locked: boolean
  /** That conversation is open in the panel of a window in front — no alert for it (AC-4.7). */
  isShowing: (conversation: string) => boolean
  open: (conversation: string) => void
}

/**
 * Diffs each fresh unread summary against what this tab has already seen. The first summary and the one after a
 * reconnect are a catch-up: the badge and at most one sound, never a burst of notices (AC-4.8, EC-12).
 */
export function useStaffChatAlerts({ unread, identity, locked, isShowing, open }: AlertInput) {
  const seen = useRef<Set<string> | null>(null)
  const catchUp = useRef(false)
  const latest = useRef({ locked, isShowing, open })
  latest.current = { locked, isShowing, open }
  const subscribe = useClinicRealtimeSubscribe()

  useEffect(() => {
    installAudioUnlock()
    pruneClaims()
  }, [])

  useEffect(() => {
    seen.current = null
    clearNotices()
  }, [identity])

  useEffect(() => {
    if (!subscribe) return
    return subscribe((resource) => {
      if (resource === undefined) catchUp.current = true
    })
  }, [subscribe])

  // The browser's own « back online » refetch can land before the hub has reconnected.
  useEffect(() => {
    const back = () => {
      catchUp.current = true
    }
    window.addEventListener("online", back)
    return () => window.removeEventListener("online", back)
  }, [])

  useEffect(() => {
    if (!unread || !identity) return
    const recent = unread.recent
    withdrawNoticesExcept(new Set(recent.filter((m) => !m.claimedBy).map((m) => m.id)))

    const before = seen.current
    const fresh = recent.filter((m) => !before?.has(m.id) && !m.claimedBy)
    seen.current = new Set([...(before ?? []), ...recent.map((m) => m.id)])
    if (fresh.length === 0) return

    // Live, a message comes alone; several in one refresh were missed while away.
    if (before === null || catchUp.current || fresh.length >= CATCH_UP_AT) {
      catchUp.current = false
      void claim(fresh.map((m) => m.id)).then((won) => {
        if (won.length > 0) sound(fresh.some((m) => won.includes(m.id) && isUrgent(m)))
      })
      return
    }

    const due = fresh.filter((m) => !latest.current.isShowing(m.conversation)).reverse()
    if (due.length === 0) return
    void claim(due.map((m) => m.id)).then((won) => {
      const mine = due.filter((m) => won.includes(m.id))
      if (mine.length > 0) sound(mine.some(isUrgent))
      for (const m of mine) raiseSystemNotification(m, latest.current.open)
    })
    if (!latest.current.locked) addNotices(due)
  }, [unread, identity])
}
