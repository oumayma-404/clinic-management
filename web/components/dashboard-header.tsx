"use client"

import { useState } from 'react'
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
import type { NotificationDto } from "@/lib/api/types"
import { Bell, Search, LogOut, KeyRound } from "lucide-react"

export function DashboardHeader() {
  const { user, isLoading, mode, logout } = useSession()
  const router = useRouter()

  const [notifOpen, setNotifOpen] = useState(false)
  const { notifications, unreadCount, loading, error, markRead, markAllRead } = useNotifications(notifOpen)

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

  const userName = user?.name || user?.email || "User"
  const userEmail = user?.email || ""
  const userPicture = user?.picture

  return (
    <>
    <PostVisitReviewPopup />
    <header className="flex h-16 items-center justify-between border-b border-border bg-card px-6">
      <div className="flex flex-1 items-center gap-4">
        <div className="relative w-full max-w-md">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <input
            type="search"
            placeholder="Search patients, appointments..."
            className="h-10 w-full rounded-lg border border-input bg-background pl-10 pr-4 text-sm outline-none placeholder:text-muted-foreground focus:border-ring focus:ring-1 focus:ring-ring"
          />
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
                  <p className="text-xs text-muted-foreground">{userEmail || "User"}</p>
                </div>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-56">
              <DropdownMenuLabel>My Account</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => router.push("/settings")} className="cursor-pointer">
                Settings
              </DropdownMenuItem>
              {mode === "local" && (
                <DropdownMenuItem
                  onClick={() => router.push("/change-password")}
                  className="flex items-center cursor-pointer"
                >
                  <KeyRound className="mr-2 h-4 w-4" />
                  Change password
                </DropdownMenuItem>
              )}
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onClick={logout}
                className="flex items-center text-destructive cursor-pointer"
              >
                <LogOut className="mr-2 h-4 w-4" />
                Log out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </header>
    </>
  )
}
