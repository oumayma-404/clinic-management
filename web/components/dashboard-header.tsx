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
import { Bell, Search, LogOut, KeyRound, Loader2 } from "lucide-react"

export function DashboardHeader() {
  const { user, isLoading, mode, logout } = useSession()
  const router = useRouter()

  const [notifOpen, setNotifOpen] = useState(false)
  const { notifications, unreadCount, loading, error, markRead, markAllRead } = useNotifications(notifOpen)

  // Global patient search (AC-6): type → debounced patient lookup → navigate to the selected patient.
  const [searchQuery, setSearchQuery] = useState("")
  const [searchResults, setSearchResults] = useState<PatientDto[]>([])
  const [searchOpen, setSearchOpen] = useState(false)
  const [searching, setSearching] = useState(false)
  const searchBoxRef = useRef<HTMLDivElement>(null)

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
    router.push(`/patients/${id}`)
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
    <header className="flex h-16 items-center justify-between border-b border-border bg-card px-6">
      <div className="flex flex-1 items-center gap-4">
        <div ref={searchBoxRef} className="relative w-full max-w-md">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <input
            type="search"
            placeholder="Rechercher un patient…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onFocus={() => {
              if (searchResults.length > 0) setSearchOpen(true)
            }}
            className="h-10 w-full rounded-lg border border-input bg-background pl-10 pr-9 text-sm outline-none placeholder:text-muted-foreground focus:border-ring focus:ring-1 focus:ring-ring"
          />
          {searching && (
            <Loader2 className="absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-muted-foreground" />
          )}

          {searchOpen && searchQuery.trim().length >= 2 && (
            <div className="absolute left-0 right-0 top-full z-50 mt-1 max-h-80 overflow-y-auto rounded-lg border border-border bg-popover shadow-md">
              {searching && searchResults.length === 0 ? (
                <p className="px-3 py-2 text-sm text-muted-foreground">Recherche…</p>
              ) : searchResults.length === 0 ? (
                <p className="px-3 py-2 text-sm text-muted-foreground">Aucun patient trouvé.</p>
              ) : (
                searchResults.map((patient) => (
                  <button
                    key={patient.id}
                    type="button"
                    onClick={() => goToPatient(patient.id)}
                    className="flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left text-sm hover:bg-accent"
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

      <div className="flex items-center gap-4">
        <ConnectivityIndicator />

        <Popover open={notifOpen} onOpenChange={setNotifOpen}>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="icon" className="relative" aria-label="Notifications">
              <Bell className="h-5 w-5" />
              {unreadCount > 0 && (
                <Badge
                  variant="destructive"
                  className="absolute -right-1 -top-1 flex h-5 min-w-[1.25rem] items-center justify-center rounded-full px-1 text-[10px] leading-none"
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
              <Button variant="ghost" className="flex items-center gap-2">
                <Avatar className="h-8 w-8">
                  {userPicture && <AvatarImage src={userPicture} alt={userName} />}
                  <AvatarFallback className="bg-primary text-primary-foreground text-sm">
                    {getInitials(userName)}
                  </AvatarFallback>
                </Avatar>
                <div className="text-left">
                  <p className="text-sm font-medium">{userName}</p>
                  <p className="text-xs text-muted-foreground">{userEmail || "Utilisateur"}</p>
                </div>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-56">
              <DropdownMenuLabel>Mon compte</DropdownMenuLabel>
              <DropdownMenuSeparator />
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
