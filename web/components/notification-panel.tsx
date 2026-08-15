"use client"

import { formatDistanceToNow } from "date-fns"
import { fr } from "date-fns/locale"
import {
  AlertTriangle,
  BellOff,
  CalendarClock,
  CalendarPlus,
  CalendarX,
  ClipboardPlus,
  Clock,
  CreditCard,
  Loader2,
  MessageSquareX,
  Hourglass,
  type LucideIcon,
  DatabaseBackup,
} from "lucide-react"
import { cn } from "@/lib/utils"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { lowStockOrderMessageFromAlert } from "@/lib/whatsapp"
import { EmptyState } from "@/components/ui/empty-state"
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
  // AC-P4.6 — a lot is about to expire. Distinct from LowStock's AlertTriangle: low stock means "order more",
  // this means "use it or lose it", and the two land in the same feed for the same item.
  StockExpiringSoon: Hourglass,
  // L4d — no successful backup for longer than the clinic's threshold. `DatabaseBackup` rather than another
  // warning triangle: this is the only alert in the feed that is about the data itself rather than about a
  // patient, a visit or a shelf.
  BackupStale: DatabaseBackup,
  // clinic-subscription AC-3.4 — the cabinet's entitlement to record new work is running out. `CreditCard` rather
  // than a clock or a triangle: what it asks for is a payment, and it is the only row in the feed about money owed
  // to the software vendor rather than about the practice's own work.
  SubscriptionExpiring: CreditCard,
}

/**
 * The chip's colour per category — the tone, not a per-category hue.
 *
 * <p>The chip already existed and was `bg-muted text-muted-foreground` for all eight categories, so a feed
 * carrying « rendez-vous annulé », « stock bas » and « rappel non délivré » presented them as three identical
 * grey circles and the only way to triage was to read every line. The glyphs were already distinct; only the
 * colour was missing.</p>
 *
 * <p>Mapped to the app's **semantic** family rather than to eight new hues, deliberately — a notification centre
 * with a colour per category is a legend nobody learns. Four tones answer the only question a reader has when
 * they open the bell: <i>is this something that went wrong, something that needs me, or something that simply
 * happened?</i></p>
 *
 * <p>⚠️ Keyed by the same strings as {@link CATEGORY_ICON} and falling back to neutral, so a category added on
 * the server renders as an uncoloured chip rather than crashing or, worse, borrowing a tone that claims the
 * wrong news about it.</p>
 */
const CATEGORY_TONE: Record<string, string> = {
  // Something failed or was undone. Red.
  AppointmentCancelled: "bg-destructive-wash text-destructive",
  ReminderFailed: "bg-destructive-wash text-destructive",
  // Something needs attention before a deadline. Amber — `-ink`, because `--warning` is under the contrast
  // floor on its own wash (see the note in `ui/status-tone.ts`).
  LowStock: "bg-warning-wash text-warning-ink",
  StockExpiringSoon: "bg-warning-wash text-warning-ink",
  Reminder: "bg-warning-wash text-warning-ink",
  // L4d - a stale backup is the highest-consequence amber in the feed: everything else costs a visit or a
  // box of consumables, this one costs the practice its records.
  BackupStale: "bg-warning-wash text-warning-ink",
  // clinic-subscription — a deadline with a countdown, so amber like the two above. Deliberately not red even on
  // the last day: nothing has gone wrong, and the record is never at risk (reads and exports keep working).
  SubscriptionExpiring: "bg-warning-wash text-warning-ink",
  // Something the user is asked to complete. Teal — the app's "do this" colour.
  PostVisitReview: "bg-primary/10 text-primary",
  // Something simply happened. Teal at lower weight; it is information, not a task.
  AppointmentCreated: "bg-primary/10 text-primary",
  AppointmentRescheduled: "bg-primary/10 text-primary",
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
            // `touch-target` + padding: a bare inline button around 12px text is a ~16px tall target, in a
            // panel whose own rows are 60px, and it is the feed's only bulk action. The negative margin keeps
            // the enlarged hit area from pushing the header taller.
            className="touch-target -me-2 rounded px-2 py-1 text-xs font-medium text-primary hover:underline"
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
          <EmptyState
            icon={BellOff}
            size="compact"
            title="Aucune notification"
            description="Les rendez-vous, les rappels et les alertes de stock apparaîtront ici."
          />
        ) : (
          <ul className="divide-y divide-border">
            {notifications.map((n) => {
              const Icon = CATEGORY_ICON[n.category] ?? Clock
              return (
                // AC-6 — the WhatsApp control is a SIBLING of the row button, never nested inside it: a button
                // inside a button is invalid markup and the inner one's click would not survive the outer's
                // handler. Tapping the text still deep-links to /stock exactly as before.
                <li key={n.id} className={cn("flex items-start", !n.isRead && "bg-accent/40")}>
                  <button
                    type="button"
                    onClick={() => onRowClick(n)}
                    className="flex min-w-0 flex-1 items-start gap-3 px-4 py-3 text-left transition-colors hover:bg-accent"
                  >
                    <span
                      className={cn(
                        "mt-0.5 flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full",
                        CATEGORY_TONE[n.category] ?? "bg-muted text-muted-foreground",
                      )}
                    >
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
                      {n.supplierName ? (
                        <span className="mt-1 block text-sm font-medium text-foreground">
                          Contacter {n.supplierName}
                        </span>
                      ) : null}
                      <span className="mt-1 block text-xs text-muted-foreground">{relativeTime(n.createdAt)}</span>
                    </span>
                  </button>
                  {n.supplierName ? (
                    <span className="flex items-center self-center pe-2">
                      {/* No « Ajouter un numéro » fallback: the bell cannot open the fournisseur's form, and a
                          dead control in a notification is worse than a row that simply names the contact. */}
                      <WhatsAppAction
                        phoneE164={n.supplierPhoneE164}
                        contactName={n.supplierName}
                        message={lowStockOrderMessageFromAlert(n.message)}
                      />
                    </span>
                  ) : null}
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}
