"use client"

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"
import { usePathname } from "next/navigation"
import { toast } from "sonner"
import { useSession } from "@/lib/auth/session"
import { replaceUrl } from "@/lib/history"
import { useStaffChatUnread } from "@/lib/hooks/use-staff-chat"
import { isChromeLessPath } from "@/lib/nav"
import { useStaffChatAlerts } from "@/lib/staff-chat/alerts"
import { StaffChatNotices } from "./staff-chat-notices"
import { StaffChatPanel } from "./staff-chat-panel"

interface StaffChatContextValue {
  /** False on the sign-in screens and before a session exists — nothing chat-related renders there. */
  available: boolean
  isOpen: boolean
  conversation: string | null
  openPanel: (conversation?: string) => void
  /** From a notice: the panel waits for an open dialog to close, so nothing typed in it is lost. */
  openConversation: (conversation: string) => void
  closePanel: () => void
  selectConversation: (conversation: string | null) => void
}

const StaffChatContext = createContext<StaffChatContextValue | null>(null)

const OPEN_MODAL =
  '[data-slot="dialog-content"][data-state="open"], [data-slot="alert-dialog-content"][data-state="open"], ' +
  '[data-slot="sheet-content"][data-state="open"]'

/**
 * « Messages » state for the whole tab (staff-chat). It lives above the pages because the header remounts on every
 * navigation, and the panel must survive it with its conversation and draft intact (AC-1.2, FR-7).
 */
export function StaffChatProvider({ children }: { children: React.ReactNode }) {
  const pathname = usePathname()
  const { user, locked } = useSession()
  const available = Boolean(user) && !isChromeLessPath(pathname)
  const [isOpen, setOpen] = useState(false)
  const [conversation, setConversation] = useState<string | null>(null)
  const returnFocusTo = useRef<HTMLElement | null>(null)
  const waitingFor = useRef<ReturnType<typeof setInterval> | null>(null)
  const identity = user?.email ?? null
  const unread = useStaffChatUnread(available)

  // A shared reception PC: the next person never inherits the previous one's open thread.
  useEffect(() => {
    setOpen(false)
    setConversation(null)
  }, [identity])

  const openPanel = useCallback((key?: string) => {
    if (document.activeElement instanceof HTMLElement) returnFocusTo.current = document.activeElement
    if (key) setConversation(key)
    setOpen(true)
  }, [])

  const openConversation = useCallback(
    (key: string) => {
      if (waitingFor.current) clearInterval(waitingFor.current)
      waitingFor.current = null
      if (!document.querySelector(OPEN_MODAL)) {
        openPanel(key)
        return
      }
      toast.message("Le message s'ouvrira à la fermeture de cette fenêtre.")
      waitingFor.current = setInterval(() => {
        if (document.querySelector(OPEN_MODAL)) return
        if (waitingFor.current) clearInterval(waitingFor.current)
        waitingFor.current = null
        openPanel(key)
      }, 400)
    },
    [openPanel],
  )

  useEffect(
    () => () => {
      if (waitingFor.current) clearInterval(waitingFor.current)
    },
    [],
  )

  // A click on the Windows app's chat notification opens that conversation here (AC-4.3).
  useEffect(() => {
    if (!available) return
    window.__clinicShellOpenStaffChat = openConversation
    return () => {
      if (window.__clinicShellOpenStaffChat === openConversation) delete window.__clinicShellOpenStaffChat
    }
  }, [available, openConversation])

  // A tapped browser or iPhone notification (staff-chat Part 8): the service worker names the thread to open.
  useEffect(() => {
    if (!available) return
    const chat = new URLSearchParams(window.location.search).get("chat")
    if (chat) {
      const url = new URL(window.location.href)
      url.searchParams.delete("chat")
      replaceUrl(url.pathname + url.search)
      openConversation(chat)
    }
    if (!("serviceWorker" in navigator)) return
    const onMessage = (event: MessageEvent) => {
      const data = event.data as { type?: string; conversation?: unknown; url?: unknown } | null
      if (data?.type === "apexa-open-staff-chat" && typeof data.conversation === "string") openConversation(data.conversation)
      // Only this app's own paths: a message is data, never an address to follow off the site.
      else if (data?.type === "apexa-open-url" && typeof data.url === "string" && /^\/(?!\/)/.test(data.url)) {
        window.location.assign(data.url)
      }
    }
    navigator.serviceWorker.addEventListener("message", onMessage)
    return () => navigator.serviceWorker.removeEventListener("message", onMessage)
  }, [available, openConversation])

  const closePanel = useCallback(() => {
    setOpen(false)
    requestAnimationFrame(() => returnFocusTo.current?.focus())
  }, [])

  const showing = useRef({ isOpen, conversation })
  showing.current = { isOpen, conversation }
  const isShowing = useCallback(
    (key: string) =>
      showing.current.isOpen &&
      showing.current.conversation === key &&
      document.visibilityState === "visible" &&
      document.hasFocus(),
    [],
  )

  useStaffChatAlerts({
    unread: available ? unread.data : undefined,
    identity,
    locked,
    isShowing,
    open: openConversation,
  })

  const value = useMemo<StaffChatContextValue>(
    () => ({
      available,
      isOpen,
      conversation,
      openPanel,
      openConversation,
      closePanel,
      selectConversation: setConversation,
    }),
    [available, isOpen, conversation, openPanel, openConversation, closePanel],
  )

  return (
    <StaffChatContext.Provider value={value}>
      {children}
      {available && isOpen && <StaffChatPanel />}
      {available && <StaffChatNotices />}
    </StaffChatContext.Provider>
  )
}

const OUTSIDE: StaffChatContextValue = {
  available: false,
  isOpen: false,
  conversation: null,
  openPanel: () => {},
  openConversation: () => {},
  closePanel: () => {},
  selectConversation: () => {},
}

/** Outside the provider (a server render, a test page) nothing is available, rather than a throw. */
export function useStaffChat(): StaffChatContextValue {
  return useContext(StaffChatContext) ?? OUTSIDE
}
