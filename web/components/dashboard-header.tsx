"use client"

import { useState, useEffect, useRef } from 'react'
import { useRouter } from 'next/navigation'
import { useSession } from '@/lib/auth/session'
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { ConnectivityIndicator } from "@/components/connectivity-indicator"
import { NotificationPanel } from "@/components/notification-panel"
import { PostVisitReviewPopup } from "@/components/post-visit-review-popup"
import { useNotifications } from "@/lib/hooks/use-notifications"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import type { NotificationDto, PatientDto } from "@/lib/api/types"
import { useSidebar } from "@/contexts/sidebar-context"
import { cn } from "@/lib/utils"
import { Bell, Search, LogOut, KeyRound, Loader2, UserCircle, Menu } from "lucide-react"

export function DashboardHeader() {
  const { user, isLoading, mode, logout } = useSession()
  const router = useRouter()
  // The mobile nav drawer's only opener (AC-P3.12) — the rail itself is hidden below `md:`.
  const { setMobileOpen } = useSidebar()

  const [notifOpen, setNotifOpen] = useState(false)
  const { notifications, unreadCount, loading, error, markRead, markAllRead } = useNotifications(notifOpen)

  // Global patient search (AC-6): type → debounced patient lookup → navigate to the selected patient.
  const [searchQuery, setSearchQuery] = useState("")
  const [searchResults, setSearchResults] = useState<PatientDto[]>([])
  const [searchOpen, setSearchOpen] = useState(false)
  const [searching, setSearching] = useState(false)
  const searchBoxRef = useRef<HTMLDivElement>(null)
  /**
   * Which result the keyboard is on; -1 = none.
   *
   * This is the fastest route to a patient's file in the whole product, and it was mouse-only: the results
   * were a plain stack of buttons, so typing a name and then having to lift a hand to the trackpad to pick
   * the obvious first match was the normal case. ↑/↓ move, Enter opens, Escape closes.
   */
  const [activeIndex, setActiveIndex] = useState(-1)
  const resultsRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const term = searchQuery.trim()
    if (term.length < 2) {
      setSearchResults([])
      setSearching(false)
      return
    }
    setSearching(true)
    let active = true
    const handle = setTimeout(async () => {
      try {
        const results = await patientsApi.list({ searchTerm: term, limit: 8 })
        if (active) {
          setSearchResults(results)
          setSearchOpen(true)
          // Preselect the top match: after typing a name, Enter should open the obvious result without a
          // preparatory ↓. Reset on every new result set so the highlight never points at a stale row.
          setActiveIndex(results.length > 0 ? 0 : -1)
        }
      } catch {
        if (active) setSearchResults([])
      } finally {
        if (active) setSearching(false)
      }
    }, 250)
    return () => {
      active = false
      clearTimeout(handle)
    }
  }, [searchQuery])

  // Close the results dropdown on an outside click.
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (searchBoxRef.current && !searchBoxRef.current.contains(e.target as Node)) {
        setSearchOpen(false)
      }
    }
    document.addEventListener("mousedown", onClick)
    return () => document.removeEventListener("mousedown", onClick)
  }, [])

  const goToPatient = (id: string) => {
    setSearchOpen(false)
    setSearchQuery("")
    setSearchResults([])
    setActiveIndex(-1)
    router.push(`/patients/${id}`)
  }

  /**
   * Keyboard on the search field. `preventDefault` on the arrows matters: without it ↓ moves the text
   * caret to the end of the input instead of moving through the list.
   */
  const handleSearchKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") {
      setSearchOpen(false)
      setActiveIndex(-1)
      return
    }
    if (!searchOpen || searchResults.length === 0) return

    if (event.key === "ArrowDown") {
      event.preventDefault()
      setActiveIndex((i) => (i + 1) % searchResults.length)
    } else if (event.key === "ArrowUp") {
      event.preventDefault()
      setActiveIndex((i) => (i - 1 + searchResults.length) % searchResults.length)
    } else if (event.key === "Enter") {
      const target = searchResults[activeIndex]
      if (target) {
        event.preventDefault()
        goToPatient(target.id)
      }
    }
  }

  const handleNotificationClick = (notification: NotificationDto) => {
    setNotifOpen(false)
    void markRead(notification.id)
    // A post-visit review notification targets an appointment, but fulfilling it means adding the patient's
    // medical record (saving that record marks the appointment Completed) — NOT opening the appointment. So
    // resolve the patient from the appointment and deep-link to the Add-Medical-Record modal, mirroring the
    // post-visit popup's "Ajouter le dossier médical". Must run before the generic Appointment branch below,
    // since this notification also carries targetKind === "Appointment".
    if (notification.category === "PostVisitReview" && notification.appointmentId) {
      const appointmentId = notification.appointmentId
      void (async () => {
        let patientId: string | null = null
        try {
          const appointment = await appointmentsApi.get(appointmentId)
          patientId = appointment.patientId ?? null
        } catch {
          return // keep it pending rather than navigate to a dead page (mirrors the popup)
        }
        if (!patientId) return
        router.push(`/patients/${patientId}?addRecord=1&appointmentId=${encodeURIComponent(appointmentId)}`)
      })()
      return
    }
    // router.push handles cross-page navigation (the target page reads the query param on mount). When
    // the user is ALREADY on the target page, a same-route push does not remount it, so we also emit a
    // deep-link event the target page listens for — see the "clinic:deeplink" handlers on those pages.
    if (notification.targetKind === "Appointment" && notification.appointmentId) {
      router.push(`/appointments?appointmentId=${notification.appointmentId}`)
      window.dispatchEvent(
        new CustomEvent("clinic:deeplink", { detail: { appointmentId: notification.appointmentId } }),
      )
    } else if (notification.targetKind === "StockItem" && notification.stockItemId) {
      router.push(`/stock?itemId=${notification.stockItemId}`)
      window.dispatchEvent(new CustomEvent("clinic:deeplink", { detail: { itemId: notification.stockItemId } }))
    } else if (notification.targetKind === "Recall") {
      /*
       * AC-P3.7 — a failed recall carries no appointment, so it cannot deep-link to a visit.
       *
       * It used to land on `/recalls`, the worklist page, which has been removed. It now lands on « Rappels »,
       * the delivery log, filtered to failures: that is where the failed send itself is listed, with its channel
       * and its reason. Strictly less than the old destination (the worklist also offered « contacté » and
       * « relancer »), but it is the only surface that still exists AND actually contains this notification's
       * subject — pointing at a deleted route, or at an unfiltered patient list, would be worse.
       */
      router.push("/rappels?status=failed")
    }
  }

  const getInitials = (name?: string) => {
    if (!name) return "U"
    return name
      .split(' ')
      .map(n => n[0])
      .join('')
      .toUpperCase()
      .slice(0, 2)
  }

  const userName = user?.name || user?.email || "Utilisateur"
  const userEmail = user?.email || ""
  const userPicture = user?.picture

  return (
    <>
    <PostVisitReviewPopup />
    {/* Reflows rather than overflows below `md:` (AC-P3.15): tighter padding and gaps, the nav opener
        appears, and the user block drops its name/email text so the row fits 375 px. */}
    <header className="flex h-16 items-center justify-between gap-2 border-b border-border bg-card px-4 md:px-6">
      <div className="flex min-w-0 flex-1 items-center gap-2 md:gap-4">
        <Button
          variant="ghost"
          size="icon"
          className="shrink-0 md:hidden"
          onClick={() => setMobileOpen(true)}
          aria-label="Ouvrir la navigation"
        >
          <Menu className="h-5 w-5" />
        </Button>
        <div ref={searchBoxRef} className="relative w-full min-w-0 max-w-md">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <input
            type="search"
            placeholder="Rechercher un patient…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onFocus={() => {
              if (searchResults.length > 0) setSearchOpen(true)
            }}
            onKeyDown={handleSearchKeyDown}
            role="combobox"
            aria-expanded={searchOpen}
            aria-controls="patient-search-results"
            aria-autocomplete="list"
            aria-activedescendant={activeIndex >= 0 ? `patient-search-option-${activeIndex}` : undefined}
            className="h-10 w-full rounded-lg border border-input bg-background pl-10 pr-9 text-sm outline-none placeholder:text-muted-foreground focus:border-ring focus:ring-1 focus:ring-ring"
          />
          {searching && (
            <Loader2 className="absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-muted-foreground" />
          )}

          {searchOpen && searchQuery.trim().length >= 2 && (
            <div
              id="patient-search-results"
              ref={resultsRef}
              role="listbox"
              aria-label="Résultats de la recherche de patients"
              className="absolute left-0 right-0 top-full z-50 mt-1 max-h-80 overflow-y-auto rounded-lg border border-border bg-popover shadow-md"
            >
              {searching && searchResults.length === 0 ? (
                <p className="px-3 py-2 text-sm text-muted-foreground">Recherche…</p>
              ) : searchResults.length === 0 ? (
                <p className="px-3 py-2 text-sm text-muted-foreground">Aucun patient trouvé.</p>
              ) : (
                searchResults.map((patient, index) => (
                  <button
                    key={patient.id}
                    id={`patient-search-option-${index}`}
                    role="option"
                    aria-selected={index === activeIndex}
                    type="button"
                    // The keyboard highlight and the mouse hover are the same visual state on purpose —
                    // two competing highlights in one list is how a user loses track of what Enter will do.
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => goToPatient(patient.id)}
                    className={cn(
                      "flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left text-sm transition-colors",
                      index === activeIndex ? "bg-accent text-accent-foreground" : "hover:bg-accent",
                    )}
                  >
                    <span className="font-medium">
                      {patient.firstName} {patient.lastName}
                    </span>
                    {patient.phoneNumber && (
                      <span className="text-xs text-muted-foreground">{patient.phoneNumber}</span>
                    )}
                  </button>
                ))
              )}
            </div>
          )}
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1 md:gap-4">
        <ConnectivityIndicator />

        <Popover open={notifOpen} onOpenChange={setNotifOpen}>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="icon" className="relative" aria-label="Notifications">
              <Bell className="h-5 w-5" />
              {unreadCount > 0 && (
                <Badge
                  variant="destructive"
                  className="absolute -right-1 -top-1 flex h-5 min-w-[1.25rem] items-center justify-center rounded-full px-1 text-2xs leading-none"
                >
                  {unreadCount > 99 ? "99+" : unreadCount}
                </Badge>
              )}
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-auto p-0">
            <NotificationPanel
              notifications={notifications}
              loading={loading}
              error={error}
              hasUnread={unreadCount > 0}
              onMarkAllRead={markAllRead}
              onRowClick={handleNotificationClick}
            />
          </PopoverContent>
        </Popover>

        {!isLoading && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="flex items-center gap-2 px-2 md:px-3" aria-label="Mon compte">
                <Avatar className="h-8 w-8">
                  {userPicture && <AvatarImage src={userPicture} alt={userName} />}
                  <AvatarFallback className="bg-primary text-primary-foreground text-sm">
                    {getInitials(userName)}
                  </AvatarFallback>
                </Avatar>
                {/* Identity text is the first thing to go on a phone — the avatar still identifies the
                    session, and the same name/email head the menu itself. */}
                <div className="hidden text-left md:block">
                  <p className="text-sm font-medium">{userName}</p>
                  <p className="text-xs text-muted-foreground">{userEmail || "Utilisateur"}</p>
                </div>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-56">
              <DropdownMenuLabel className="truncate">
                {userName}
                {userEmail && (
                  <span className="block truncate text-xs font-normal text-muted-foreground">{userEmail}</span>
                )}
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => router.push("/mon-profil")} className="flex items-center cursor-pointer">
                <UserCircle className="mr-2 h-4 w-4" />
                Mon profil
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => router.push("/settings")} className="cursor-pointer">
                Paramètres
              </DropdownMenuItem>
              {mode === "local" && (
                <DropdownMenuItem
                  onClick={() => router.push("/change-password")}
                  className="flex items-center cursor-pointer"
                >
                  <KeyRound className="mr-2 h-4 w-4" />
                  Changer le mot de passe
                </DropdownMenuItem>
              )}
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onClick={logout}
                className="flex items-center text-destructive cursor-pointer"
              >
                <LogOut className="mr-2 h-4 w-4" />
                Se déconnecter
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </header>
    </>
  )
}
