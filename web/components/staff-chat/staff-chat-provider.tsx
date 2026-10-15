"use client"

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"
import { usePathname } from "next/navigation"
import { useSession } from "@/lib/auth/session"
import { isChromeLessPath } from "@/lib/nav"
import { StaffChatPanel } from "./staff-chat-panel"

interface StaffChatContextValue {
  /** False on the sign-in screens and before a session exists — nothing chat-related renders there. */
  available: boolean
  isOpen: boolean
  conversation: string | null
  openPanel: (conversation?: string) => void
  closePanel: () => void
  selectConversation: (conversation: string | null) => void
}

const StaffChatContext = createContext<StaffChatContextValue | null>(null)

/**
 * « Messages » state for the whole tab (staff-chat). It lives above the pages because the header remounts on every
 * navigation, and the panel must survive it with its conversation and draft intact (AC-1.2, FR-7).
 */
export function StaffChatProvider({ children }: { children: React.ReactNode }) {
  const pathname = usePathname()
  const { user } = useSession()
  const available = Boolean(user) && !isChromeLessPath(pathname)
  const [isOpen, setOpen] = useState(false)
  const [conversation, setConversation] = useState<string | null>(null)
  const returnFocusTo = useRef<HTMLElement | null>(null)
  const identity = user?.email ?? null

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

  const closePanel = useCallback(() => {
    setOpen(false)
    requestAnimationFrame(() => returnFocusTo.current?.focus())
  }, [])

  const value = useMemo<StaffChatContextValue>(
    () => ({ available, isOpen, conversation, openPanel, closePanel, selectConversation: setConversation }),
    [available, isOpen, conversation, openPanel, closePanel],
  )

  return (
    <StaffChatContext.Provider value={value}>
      {children}
      {available && isOpen && <StaffChatPanel />}
    </StaffChatContext.Provider>
  )
}

const OUTSIDE: StaffChatContextValue = {
  available: false,
  isOpen: false,
  conversation: null,
  openPanel: () => {},
  closePanel: () => {},
  selectConversation: () => {},
}

/** Outside the provider (a server render, a test page) nothing is available, rather than a throw. */
export function useStaffChat(): StaffChatContextValue {
  return useContext(StaffChatContext) ?? OUTSIDE
}
