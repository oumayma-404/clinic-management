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
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { ConnectivityIndicator } from "@/components/connectivity-indicator"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { NotificationPanel } from "@/components/notification-panel"
import { PostVisitReviewPopup } from "@/components/post-visit-review-popup"
import { useNotifications } from "@/lib/hooks/use-notifications"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import type { NotificationDto, PatientDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { Bell, Search, LogOut, KeyRound, Loader2, UserCircle, Monitor, Sun, Moon, ArrowLeft } from "lucide-react"
import { useTheme } from "next-themes"
import { useMediaQuery } from "@/lib/hooks/use-media-query"

export function DashboardHeader() {
  const { user, isLoading, mode, logout } = useSession()
  const router = useRouter()
  // `theme` (the stored choice, including « système ») drives the radio; `resolvedTheme` would collapse
  // système into whichever concrete theme the OS currently reports and tick the wrong row.
  const { theme, setTheme } = useTheme()
  /*
   * True only when the app is running installed (AC-37). `display-mode: standalone` is the media query the
   * manifest's own `display` field drives, so this reads the real state rather than sniffing the user agent.
   * ⚠️ Not `useMediaQuery` with a width — this has nothing to do with size: an installed app on a 1440px
   * desktop also has no browser back button.
   */
  const isStandalone = useMediaQuery("(display-mode: standalone)")
  // No `useSidebar()` here any more: the drawer is opened from « Plus » in the bottom bar (AC-7), so the header
  // no longer touches sidebar state at all.

  const [notifOpen, setNotifOpen] = useState(false)
  const { notifications, unreadCount, loading, error, markRead, markAllRead } = useNotifications(notifOpen)

  // Global patient search (AC-6): type → debounced patient lookup → navigate to the selected patient.
  const [searchQuery, setSearchQuery] = useState("")
  const [searchResults, setSearchResults] = useState<PatientDto[]>([])
  const [searchOpen, setSearchOpen] = useState(false)
  const [searching, setSearching] = useState(false)
  /** The last lookup **failed** — kept apart from "returned nothing", which the dropdown must not conflate. */
  const [searchFailed, setSearchFailed] = useState(false)
  /** Bumped by « Réessayer » — the term is unchanged, so only a token can re-trigger the debounced effect. */
  const [searchRetry, setSearchRetry] = useState(0)
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
      setSearchFailed(false)
      setSearching(false)
      return
    }
    setSearching(true)
    let active = true
    const handle = setTimeout(async () => {
      try {
        const results = await patientsApi.list({ searchTerm: term, limit: 8 })
        if (active) {
          setSearchFailed(false)
          setSearchResults(results)
          setSearchOpen(true)
          // Preselect the top match: after typing a name, Enter should open the obvious result without a
          // preparatory ↓. Reset on every new result set so the highlight never points at a stale row.
          setActiveIndex(results.length > 0 ? 0 : -1)
        }
      } catch {
        /*
         * ⚠️ Recorded, not swallowed into `[]`.
         *
         * The old `catch { setSearchResults([]) }` rendered « Aucun patient trouvé. » for a network failure — about
         * a twelve-year patient, on the fastest route to a file in the whole product. « This patient does not exist »
         * and « we could not ask » are different answers and only one of them is ever true here.
         */
        if (active) {
          setSearchFailed(true)
          setSearchResults([])
          setSearchOpen(true)
          setActiveIndex(-1)
        }
      } finally {
        if (active) setSearching(false)
      }
    }, 250)
    return () => {
      active = false
      clearTimeout(handle)
    }
  }, [searchQuery, searchRetry])

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
    } else if (notification.targetKind === "BackupSettings") {
      // L4d — the staleness alert carries no id: it is about the clinic, and everything it asks for (the last
      // successful backup, the schedule, « Sauvegarder maintenant », the restore command) is on one screen.
      router.push("/settings")
    } else if (notification.targetKind === "Subscription") {
      // clinic-subscription AC-3.4 — like the two above it carries no id, and « Abonnement » is where the end
      // date, the tarif, how to pay and who to contact all are. Open to every role, secretaries included.
      router.push("/abonnement")
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
    {/* Reflows rather than overflows below `md:` (AC-P3.15): tighter padding and gaps, and the user block drops
        its name/email text so the row fits 375 px.

        ⚠️ The hamburger that used to sit here is gone (AC-7). It was the drawer's only trigger — the wording of
        AC-P3.12 — and « Plus » in the bottom bar supersedes it: same `isMobileOpen` state, same drawer, but a
        thumb can reach it. Two openers for one drawer would just be two things to keep in step. */}
    {/* `print:hidden`: AC-9 says "no NAVIGATION", and search + the bell + the user menu are exactly that. */}
    <header className="flex h-16 items-center justify-between gap-2 border-b border-border bg-card px-4 md:px-6 print:hidden">
      <div className="flex min-w-0 flex-1 items-center gap-2 md:gap-4">
        {/*
          In-app « Retour » (AC-37).
          `display: standalone` — what makes the installed app feel native — also removes the browser's own
          back button, so without this there is no way back from a patient's file except the nav drawer. It is
          shown only in standalone mode: in a normal tab the browser's back button already exists and a second
          one beside it is noise.
        */}
        {isStandalone && (
          <Button
            variant="ghost"
            size="icon"
            onClick={() => router.back()}
            aria-label="Retour"
            className="shrink-0"
          >
            <ArrowLeft className="h-5 w-5" />
          </Button>
        )}
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
            // A combobox announced with no name is unusable by a screen reader, and the placeholder — its only
            // label — disappears on the first keystroke.
            aria-label="Rechercher un patient"
            /*
             * `text-base md:text-sm`, not a bare `text-sm`.
             *
             * This is a raw `<input>` rather than `ui/input.tsx`, so it never picked up that primitive's iOS
             * focus-zoom guard: Safari magnifies the page whenever a focused field is under 16px and does not
             * zoom back on blur. This field is in the header of **every** page, so tapping it left the user on
             * a zoomed, sideways-scrolling app with the drawer trigger off-screen — the single widest-reaching
             * instance of that bug in the product.
             */
            className="h-10 w-full rounded-lg border border-input bg-background pl-10 pr-9 text-base outline-none placeholder:text-muted-foreground focus:border-ring focus:ring-1 focus:ring-ring md:text-sm"
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
              ) : searchFailed ? (
                /* Failed ≠ empty — a dead search with no way to retry sends the user to look up a paper file.
                   `border-0` because the dropdown already has its own border and the two would double up. */
                <LoadFailureNotice
                  message="La recherche n'a pas abouti."
                  detail="Ce patient existe peut-être."
                  onRetry={() => setSearchRetry((n) => n + 1)}
                  className="border-0 bg-transparent"
                />
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

              {/*
                Thème (AC-38). In the user menu rather than /settings because it is a per-device preference,
                not a clinic setting — the same practitioner wants dark on the chairside tablet and light on
                the desk, and /settings is shared clinic configuration.

                A radio group, not a toggle: « Système » is a real third choice, not the absence of the other
                two, and it is the default.
              */}
              <DropdownMenuSeparator />
              <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">Thème</DropdownMenuLabel>
              <DropdownMenuRadioGroup value={theme ?? "system"} onValueChange={setTheme}>
                <DropdownMenuRadioItem value="system" className="cursor-pointer">
                  <Monitor className="mr-2 h-4 w-4" />
                  Système
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem value="light" className="cursor-pointer">
                  <Sun className="mr-2 h-4 w-4" />
                  Clair
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem value="dark" className="cursor-pointer">
                  <Moon className="mr-2 h-4 w-4" />
                  Sombre
                </DropdownMenuRadioItem>
              </DropdownMenuRadioGroup>
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
