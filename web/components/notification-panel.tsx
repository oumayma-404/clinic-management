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
  UserPlus,
  X,
} from "lucide-react"
import { cn } from "@/lib/utils"
import { quoteFr } from "@/lib/format"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { lowStockOrderMessageFromAlert } from "@/lib/whatsapp"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import type { NotificationDto } from "@/lib/api/types"

interface NotificationPanelProps {
  notifications: NotificationDto[]
  loading: boolean
  error: string | null
  /** Re-reads the feed after a failure. Without it a failed read is a dead end in the app's own chrome. */
  onRetry?: () => void
  hasUnread: boolean
  onMarkAllRead: () => void
  onRowClick: (notification: NotificationDto) => void
  /**
   * Clears one row from **this user's** bell. Not an undo of whatever produced it, and a colleague's bell is
   * untouched — the server writes a per-user dismissal, because one notification row is shown to everyone it
   * targets.
   */
  onDismiss: (id: string) => void
  /** Empties this user's bell. */
  onDismissAll: () => void
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
  // calendar-import-review — Google Agenda conjured a patient record from an event title. `UserPlus` rather than a
  // warning glyph: a fiche was added and needs finishing, which is a task rather than a fault.
  PatientImportedNeedsReview: UserPlus,
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
  PatientImportedNeedsReview: "bg-primary/10 text-primary",
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
  onRetry,
  onDismiss,
  onDismissAll,
}: NotificationPanelProps) {
  // « Tout effacer » is about the rows on screen, so it is gated on there being rows — never on `hasUnread`,
  // which is a different question and false for exactly the long read-through list this action exists for.
  const hasRows = !loading && !error && notifications.length > 0
  return (
    <div className="flex max-h-[28rem] w-80 flex-col sm:w-96">
      <div className="flex items-center justify-between gap-1 border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold text-foreground">Notifications</h2>
        {/*
          ⚠️ **Two bulk actions, and they are not the same action.** « Tout marquer comme lu » silences the badge
          and keeps the list; « Tout effacer » empties the list. The bell is where a cabinet's whole year of
          rappels, alertes de stock and comptes rendus accumulates, so without the second one the panel only ever
          grows — every row read, none removable — which is what the request behind this named.

          Each appears only when it has something to do: « Tout lire » with unread rows, « Tout effacer » with
          any rows at all. `min-h-9` rather than `.touch-target`, because these two sit a few pixels apart in one
          row and an overlaid 44 px hit area would overhang its neighbour — the later sibling paints last, so a
          thumb aimed at « Tout lire » would fire « Tout effacer », which is the more destructive of the two
          (§ 2 of the device contract).

          ⚠️ **« Tout lire » is the shortened VISIBLE half of « Tout marquer comme lu », which stays as the
          accessible name** (§ 10.1's rule, and `odontogram.tsx`'s « Créer un plan » is the precedent). This row
          was built for one action and now holds two, and the panel is `w-80 sm:w-96` — it barely widens — so the
          title and the two labels filled the row edge to edge at *every* width: measured at 390 px and at 820 px,
          « Notifications » sat against the first button and « Tout effacer » against the panel's own edge.
          Nothing overflowed, which is exactly why no check saw it and only looking did.
        */}
        <span className="-me-2 flex shrink-0 items-center gap-1">
          {hasUnread && (
            <button
              type="button"
              onClick={onMarkAllRead}
              aria-label="Tout marquer comme lu"
              className="inline-flex min-h-9 items-center rounded px-2 text-xs font-medium text-primary hover:underline coarse:min-h-11"
            >
              Tout lire
            </button>
          )}
          {hasRows && (
            <button
              type="button"
              onClick={onDismissAll}
              className="inline-flex min-h-9 items-center rounded px-2 text-xs font-medium text-muted-foreground hover:text-foreground hover:underline coarse:min-h-11"
            >
              Tout effacer
            </button>
          )}
        </span>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {loading ? (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
          </div>
        ) : error ? (
          /*
            Band C/D — the app's one failed-read treatment, with a way out.

            ⚠️ This used to print `error` verbatim and offer nothing: the server's own string, whatever language it
            happened to be in, in the app's chrome on every screen, with no retry — so a transient failure left the
            bell permanently showing a sentence the user could neither act on nor dismiss. The message is now the
            panel's own French, because the reader does not need to know which call failed; they need it to work.
          */
          <div className="p-4">
            <LoadFailureNotice
              message="Les notifications n'ont pas pu être chargées."
              onRetry={onRetry}
            />
          </div>
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
                  {/*
                    Both trailing controls are SIBLINGS of the row button for the reason stated above — a button
                    inside a button is invalid markup and the inner click would not survive the outer handler.

                    ⚠️ **Always rendered, never `opacity-0 group-hover:`.** § 9.2: an affordance reachable only
                    by hover has no touch path at all, and this panel is opened with a thumb on the device this
                    product is used on most. It is a real 36 px control (44 px on a coarse pointer) sized by its
                    own box rather than by `.touch-target`, because on a low-stock row it stands beside the
                    WhatsApp action and an overlay would steal that neighbour's taps.

                    ⚠️ The `aria-label` names the notification, not just the verb: a panel of ten rows would
                    otherwise announce « Supprimer » ten times (§ 13).
                  */}
                  <span className="flex items-center gap-0.5 self-center pe-2">
                    {n.supplierName ? (
                      /* No « Ajouter un numéro » fallback: the bell cannot open the fournisseur's form, and a
                         dead control in a notification is worse than a row that simply names the contact. */
                      <WhatsAppAction
                        phoneE164={n.supplierPhoneE164}
                        contactName={n.supplierName}
                        message={lowStockOrderMessageFromAlert(n.message)}
                      />
                    ) : null}
                    <button
                      type="button"
                      onClick={() => onDismiss(n.id)}
                      aria-label={`Supprimer la notification ${quoteFr(n.title)}`}
                      className="inline-flex size-9 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring coarse:size-11"
                    >
                      <X className="size-4" aria-hidden="true" />
                    </button>
                  </span>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}
