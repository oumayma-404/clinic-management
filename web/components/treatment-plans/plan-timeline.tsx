"use client"

import {
  CalendarPlus, CheckCircle2, ClipboardCheck, FilePlus2, ReceiptText, Ban, Wallet,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"

/** One entry in the plan's « Parcours » feed. A null date sorts last (see the invoice note below). */
interface TimelineEntry {
  key: string
  at: string | null
  icon: LucideIcon
  title: string
  detail?: string
}

const PAYMENT_METHOD_LABELS: Record<string, string> = {
  Cash: "espèces",
  Cheque: "chèque",
  Card: "carte",
  Transfer: "virement",
}

/**
 * Everything that has happened on this devis, in one chronological feed, built **entirely from fields already
 * on the wire** — no new endpoint and nothing persisted.
 *
 * It reuses `notification-panel.tsx`'s activity-feed idiom (a `divide-y` list with a circular icon badge per
 * row) rather than inventing a vertical-line timeline: no timeline primitive exists in this codebase, and
 * borrowing the app's established feed shape keeps the section looking native.
 */
export function PlanTimeline({ plan }: { plan: TreatmentPlanDto }) {
  const entries = buildEntries(plan)

  if (entries.length === 0) {
    return <p className="px-4 py-8 text-center text-sm text-muted-foreground">Aucun événement.</p>
  }

  return (
    <ul className="divide-y divide-border">
      {entries.map((entry) => {
        const Icon = entry.icon
        return (
          <li key={entry.key} className="flex items-start gap-3 px-4 py-3">
            <span className="mt-0.5 flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <Icon className="h-4 w-4" />
            </span>
            <span className="min-w-0 flex-1">
              <span className="block text-sm font-medium text-foreground">{entry.title}</span>
              {entry.detail && (
                <span className="mt-0.5 block text-sm text-muted-foreground">{entry.detail}</span>
              )}
              <span className="mt-1 block text-xs text-muted-foreground">
                {entry.at ? formatDateFr(entry.at) : "Date non disponible"}
              </span>
            </span>
          </li>
        )
      })}
    </ul>
  )
}

function buildEntries(plan: TreatmentPlanDto): TimelineEntry[] {
  const entries: TimelineEntry[] = [
    { key: "created", at: plan.createdAt, icon: FilePlus2, title: "Devis créé", detail: plan.title },
  ]

  if (plan.acceptedDate) {
    entries.push({
      key: "accepted",
      at: plan.acceptedDate,
      icon: ClipboardCheck,
      title: "Devis accepté",
      detail: plan.number ? `Numéro ${plan.number}` : undefined,
    })
  }

  for (const item of plan.items) {
    // Only a *future-or-past live* appointment reaches the DTO — a cancelled one is filtered server-side, so
    // the feed never claims a séance that was called off.
    if (item.scheduledAt) {
      entries.push({
        key: `scheduled-${item.id}`,
        at: item.scheduledAt,
        icon: CalendarPlus,
        title: "Séance planifiée",
        detail: item.designationFr,
      })
    }
    if (item.doneDate) {
      entries.push({
        key: `done-${item.id}`,
        at: item.doneDate,
        icon: CheckCircle2,
        title: "Acte réalisé",
        detail: item.designationFr,
      })
    }
  }

  for (const installment of plan.installments) {
    // Installments keep only the *latest* payment (no per-payment history on the entity), so an échéance
    // topped up twice shows one entry at its last payment date. Stated here because the feed would otherwise
    // look like it lost a payment.
    if (installment.lastPaidOn && installment.amountPaid > 0) {
      const method = installment.lastMethod
        ? PAYMENT_METHOD_LABELS[installment.lastMethod] ?? installment.lastMethod.toLowerCase()
        : null
      entries.push({
        key: `paid-${installment.id}`,
        at: installment.lastPaidOn,
        icon: Wallet,
        title: `Paiement encaissé — ${formatDT(installment.amountPaid)}`,
        detail: method
          ? `Échéance du ${formatDateFr(installment.dueDate)} · ${method}`
          : `Échéance du ${formatDateFr(installment.dueDate)}`,
      })
    }
  }

  if (plan.status === "Cancelled") {
    entries.push({
      key: "cancelled",
      at: plan.updatedAt ?? null,
      icon: Ban,
      title: "Devis annulé",
      detail: plan.cancellationReason ?? undefined,
    })
  }

  if (plan.linkedInvoiceId) {
    // Undated on purpose: the API contract exposes the bridge as id/number/statut only — no issue date — and
    // widening the pinned DTO for a feed row is not worth it. It sorts last, which is where billing belongs.
    entries.push({
      key: "invoiced",
      at: null,
      icon: ReceiptText,
      title: "Devis facturé",
      detail: plan.linkedInvoiceNumber ? `Note d'honoraires ${plan.linkedInvoiceNumber}` : undefined,
    })
  }

  return entries.sort((a, b) => {
    if (a.at === null) return 1
    if (b.at === null) return -1
    return new Date(a.at).getTime() - new Date(b.at).getTime()
  })
}
