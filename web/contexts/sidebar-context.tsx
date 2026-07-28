"use client"

import { createContext, useContext, useState, useEffect, useCallback, ReactNode } from "react"
import { usePathname } from "next/navigation"

interface SidebarContextType {
  /** Desktop-only rail collapse. Persisted, and deliberately untouched by the mobile drawer (AC-P3.18). */
  isCollapsed: boolean
  toggleSidebar: () => void
  /** Below `md:` the rail is a slide-over drawer instead — closed by default (AC-P3.12). */
  isMobileOpen: boolean
  setMobileOpen: (open: boolean) => void
}

const SidebarContext = createContext<SidebarContextType | undefined>(undefined)

export function SidebarProvider({ children }: { children: ReactNode }) {
  const [isCollapsed, setIsCollapsed] = useState(false)
  // Closed by default: on a phone an open rail covers the page, which is the § 7.1 complaint.
  const [isMobileOpen, setMobileOpen] = useState(false)
  const pathname = usePathname()

  // Load from localStorage on mount
  useEffect(() => {
    const saved = localStorage.getItem("sidebar-collapsed")
    if (saved !== null) {
      setIsCollapsed(JSON.parse(saved))
    }
  }, [])

  // Save to localStorage when changed. Only `isCollapsed` is persisted — the drawer is per-visit, and
  // writing its state here would let a phone session overwrite the user's desktop rail preference
  // (AC-P3.18: a user who collapsed the rail on desktop still finds it collapsed on desktop).
  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", JSON.stringify(isCollapsed))
  }, [isCollapsed])

  // AC-P3.12 — the drawer closes on navigation. A nav link both navigates and dismisses, so the user is
  // never left tapping the page behind an overlay. (Escape and the overlay click are the Sheet's own.)
  useEffect(() => {
    setMobileOpen(false)
  }, [pathname])

  const toggleSidebar = useCallback(() => {
    setIsCollapsed((prev) => !prev)
  }, [])

  return (
    <SidebarContext.Provider value={{ isCollapsed, toggleSidebar, isMobileOpen, setMobileOpen }}>
      {children}
    </SidebarContext.Provider>
  )
}

export function useSidebar() {
  const context = useContext(SidebarContext)
  if (context === undefined) {
    throw new Error("useSidebar must be used within a SidebarProvider")
  }
  return context
}
