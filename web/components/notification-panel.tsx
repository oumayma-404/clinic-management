"use client"

import { formatDistanceToNow } from "date-fns"
import { fr } from "date-fns/locale"
import {
  AlertTriangle,
  CalendarClock,
  CalendarPlus,
  CalendarX,
  ClipboardPlus,
  Clock,
  Loader2,
  MessageSquareX,
  type LucideIcon,
} from "lucide-react"
import { cn } from "@/lib/utils"
import type { NotificationDto } from "@/lib/api/types"

interface NotificationPanelProps {
  notifications: NotificationDto[]
  loading: boolean
  error: string | null
  hasUnread: boolean
  onMarkAllRead: () => void
  onRowClick: (notification: NotificationDto) => void
}

const CATEGORY_ICON: Record<string, LucideIcon> = {
  AppointmentCreated: CalendarPlus,
  AppointmentCancelled: CalendarX,
  AppointmentRescheduled: CalendarClock,
  Reminder: Clock,
  LowStock: AlertTriangle,
  PostVisitReview: ClipboardPlus,
  // AC-P3.7 — an SMS/WhatsApp reminder or recall that never reached the patient.
  ReminderFailed: MessageSquareX,
}

function relativeTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ""
  return formatDistanceToNow(date, { addSuffix: true, locale: fr })
}

export function NotificationPanel({
  notifications,
  loading,
  error,
  hasUnread,
  onMarkAllRead,
  onRowClick,
}: NotificationPanelProps) {
  return (
    <div className="flex max-h-[28rem] w-80 flex-col sm:w-96">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold text-foreground">Notifications</h2>
        {hasUnread && (
          <button
            type="button"
            onClick={onMarkAllRead}
            className="text-xs font-medium text-primary hover:underline"
          >
            Tout marquer comme lu
          </button>
        )}
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {loading ? (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
          </div>
        ) : error ? (
          <p className="px-4 py-8 text-center text-sm text-destructive">{error}</p>
        ) : notifications.length === 0 ? (
          <p className="px-4 py-10 text-center text-sm text-muted-foreground">Aucune notification</p>
        ) : (
          <ul className="divide-y divide-border">
            {notifications.map((n) => {
              const Icon = CATEGORY_ICON[n.category] ?? Clock
              return (
                <li key={n.id}>
                  <button
                    type="button"
                    onClick={() => onRowClick(n)}
                    className={cn(
                      "flex w-full items-start gap-3 px-4 py-3 text-left transition-colors hover:bg-accent",
                      !n.isRead && "bg-accent/40",
                    )}
                  >
                    <span className="mt-0.5 flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
                      <Icon className="h-4 w-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center gap-2">
                        <span className={cn("truncate text-sm", n.isRead ? "font-medium text-foreground" : "font-semibold text-foreground")}>
                          {n.title}
                        </span>
                        {!n.isRead && <span className="h-2 w-2 flex-shrink-0 rounded-full bg-primary" aria-label="Non lu" />}
                      </span>
                      <span className="mt-0.5 block text-sm text-muted-foreground">{n.message}</span>
                      <span className="mt-1 block text-xs text-muted-foreground">{relativeTime(n.createdAt)}</span>
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}
