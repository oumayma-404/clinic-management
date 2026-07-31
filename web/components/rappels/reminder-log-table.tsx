"use client"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { cn } from "@/lib/utils"
import { formatDateTime } from "@/lib/format"
import type { ReminderDeliveryStatus, ReminderStatusDto } from "@/lib/api/reminder-settings"

/**
 * The delivery log — who was sent what, when, and why it failed.
 *
 * <p>Follows the shared <b>DataTable</b> rules from the visual-language pass: monospace uppercase column headers a
 * step quieter than the data, <code>tabular-nums</code> on every column of digits, hairlines between rows rather
 * than a border per row, and a faded zero/absent value instead of a black one.</p>
 *
 * <p>Two calls specific to this table:</p>
 * <ul>
 *   <li><b>Status is a 2px left stripe plus a pill, never a tinted row.</b> At table density a filled row reads as
 *       a rendering fault, and a stripe survives hover, focus and selection — a background does not.</li>
 *   <li><b>Channel is a monospace label, not a colour.</b> WhatsApp's brand green would collide head-on with the
 *       « Envoyé » green, leaving the reader decoding two different meanings from one hue.</li>
 * </ul>
 *
 * <p>Every pill goes through the semantic theme tokens (<code>bg-success-wash text-success</code> …) rather than
 * hardcoded <code>bg-green-100 text-green-800</code> pairs, so dark mode needs no <code>dark:</code> variant and
 * the palette cannot drift from the rest of the app.</p>
 */
interface ReminderLogTableProps {
  rows: ReminderStatusDto[]
  loading?: boolean
  /** True while refetching with rows already on screen — dim them, never blank them. */
  refreshing?: boolean
  /** Set when a filter is narrowing the log, so the empty state can say so instead of « rien envoyé ». */
  isFiltered?: boolean
  onResetFilters?: () => void
  /** No channel is sendable — a different emptiness from "nothing sent yet", with a different way out. */
  noChannelConfigured?: boolean
  onConfigure?: () => void
}

export function ReminderLogTable({
  rows,
  loading = false,
  refreshing = false,
  isFiltered = false,
  onResetFilters,
  noChannelConfigured = false,
  onConfigure,
}: ReminderLogTableProps) {
  if (loading) {
    return (
      <div className="rounded-xl border" role="status" aria-label="Chargement du journal">
        <div className="flex flex-col gap-3 p-4">
          {[38, 82, 74, 79, 60, 71].map((w, i) => (
            <div key={i} className="h-3.5 animate-pulse rounded bg-muted" style={{ width: `${w}%` }} />
          ))}
        </div>
      </div>
    )
  }

  if (rows.length === 0) {
    return (
      <div className="rounded-xl border">
        <div className="flex flex-col items-center gap-2 px-5 py-11 text-center">
          {/*
            Three distinct emptinesses, because the ways out differ. Collapsing them into one « aucun message »
            would leave a clinic that has never configured a channel waiting for reminders that can never be sent.
          */}
          {noChannelConfigured ? (
            <>
              <Badge className="bg-warning-wash text-warning">Aucun canal actif</Badge>
              <p className="text-base font-semibold">Les rappels ne sont pas encore activés</p>
              <p className="max-w-[46ch] text-sm text-muted-foreground">
                Ni SMS ni WhatsApp n&apos;est configuré, donc aucun message ne part. Renseignez un canal pour
                commencer à envoyer des rappels de rendez-vous.
              </p>
              {onConfigure && (
                <Button size="sm" className="mt-1" onClick={onConfigure}>
                  Configurer les canaux
                </Button>
              )}
            </>
          ) : isFiltered ? (
            <>
              <p className="text-base font-semibold">Aucun message ne correspond</p>
              <p className="max-w-[46ch] text-sm text-muted-foreground">
                Aucun envoi ne correspond à ces filtres. Élargissez la période ou retirez un filtre.
              </p>
              {onResetFilters && (
                <Button size="sm" variant="outline" className="mt-1" onClick={onResetFilters}>
                  Réinitialiser les filtres
                </Button>
              )}
            </>
          ) : (
            <>
              <p className="text-base font-semibold">Aucun message pour le moment</p>
              <p className="max-w-[46ch] text-sm text-muted-foreground">
                Les rappels partent automatiquement avant chaque rendez-vous, selon les délais configurés.
              </p>
            </>
          )}
        </div>
      </div>
    )
  }

  return (
    <div
      className={cn(
        "overflow-hidden rounded-xl border transition-opacity duration-200",
        refreshing && "opacity-60",
      )}
      aria-busy={refreshing || undefined}
    >
      <Table>
        <TableHeader>
          {/* Headers a step quieter than the data: mono, uppercase, muted. In the stock primitive they are
              `text-foreground`, i.e. as black as the values, so the eye cannot tell which is the content. */}
          <TableRow>
            {["Patient", "Canal", "Destinataire", "Rendez-vous", "Prévu", "Statut"].map((h) => (
              <TableHead
                key={h}
                className="whitespace-nowrap font-mono text-2xs font-medium uppercase tracking-[0.07em] text-muted-foreground"
              >
                {h}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.id} className="relative">
              <TableCell className="relative">
                <span aria-hidden="true" className={cn("absolute inset-y-0 left-0 w-[2px]", STRIPE[row.status])} />
                <span className="font-medium">{row.patientName ?? "Patient inconnu"}</span>
                {/*
                  A recall row. Kept visible even though the feature is being retired: these messages really were
                  sent to patients, and hiding them would rewrite the past rather than remove a feature.
                */}
                {row.isRecall && (
                  <Badge variant="secondary" className="ml-2 bg-muted text-muted-foreground">
                    relance
                  </Badge>
                )}
              </TableCell>
              <TableCell>
                <span className="font-mono text-2xs tracking-[0.04em] text-muted-foreground">{row.channel}</span>
              </TableCell>
              <TableCell className="font-mono text-xs text-muted-foreground">{row.recipientMasked}</TableCell>
              <TableCell className="whitespace-nowrap tabular-nums">
                {row.appointmentAt ? (
                  formatDateTime(row.appointmentAt)
                ) : (
                  // A faded dash, not a black one: the absence of an appointment is not a value to read.
                  <span className="text-muted-foreground/60">—</span>
                )}
              </TableCell>
              <TableCell className="whitespace-nowrap tabular-nums text-muted-foreground">
                {formatDateTime(row.scheduledAt)}
              </TableCell>
              <TableCell>
                <span className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary" className={STATUS_CLASS[row.status]}>
                    {STATUS_LABEL[row.status]}
                  </Badge>
                  {row.sentAt && (
                    <span className="whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                      {formatDateTime(row.sentAt)}
                    </span>
                  )}
                </span>
                {/*
                  The reason lives IN the row, not in a tooltip. It is the only thing that makes a failure
                  actionable, and a tooltip is unreachable on the tablet a dentist actually holds.
                */}
                {row.failureReason && (
                  <p className="mt-1 max-w-[38ch] text-xs text-destructive">{row.failureReason}</p>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

const STATUS_LABEL: Record<ReminderDeliveryStatus, string> = {
  sent: "Envoyé",
  pending: "En attente",
  failed: "Échec",
}

/** Semantic theme tokens, never hardcoded Tailwind colour pairs — dark mode follows with no `dark:` variant. */
const STATUS_CLASS: Record<ReminderDeliveryStatus, string> = {
  sent: "bg-success-wash text-success",
  pending: "bg-warning-wash text-warning",
  failed: "bg-destructive-wash text-destructive",
}

const STRIPE: Record<ReminderDeliveryStatus, string> = {
  sent: "bg-success",
  pending: "bg-warning",
  failed: "bg-destructive",
}
