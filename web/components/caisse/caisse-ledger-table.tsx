"use client"

import Link from "next/link"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ArrowDownLeft, ArrowUpRight, Ban, Receipt, CalendarClock, Undo2, Wallet } from "lucide-react"
import { formatDT, formatDate } from "@/lib/format"
import type { CaisseMovementDto, CaisseMovementKind } from "@/lib/api/types"
import { paymentMethodLabel } from "@/components/factures/invoice-labels"

/**
 * The « extrait de caisse » — every movement behind the caisse's totals, oldest first, like a bank statement.
 *
 * Before this, la caisse showed three figures and, underneath them, a table of *expenses only*: the money-out side
 * was itemised while « Encaissé » — the bigger number — was opaque, with no screen anywhere listing what made it
 * up. Every line here is derived server-side from the rows those totals sum, so the two can never disagree.
 */

/** Icon per source. English keys, French display — the standing convention for a closed persisted value set. */
const KIND_ICONS: Record<CaisseMovementKind, typeof Receipt> = {
  InvoicePayment: Receipt,
  InstallmentPayment: CalendarClock,
  Refund: Undo2,
  Expense: Wallet,
}

const KIND_LABELS: Record<CaisseMovementKind, string> = {
  InvoicePayment: "Facture",
  InstallmentPayment: "Échéance",
  Refund: "Avoir",
  Expense: "Dépense",
}

/**
 * Where a row leads. Kept beside the labels rather than server-side for the same reason `dashboard-links.ts`
 * owns its routes: the destination is a frontend concern, and an exhaustive `Record` makes a new kind without a
 * destination a `tsc` error rather than a row that goes nowhere.
 */
const kindHref = (movement: CaisseMovementDto): string | null => {
  switch (movement.kind) {
    case "InvoicePayment":
    case "Refund":
      // An avoir is listed and printable on the invoice it credits, which is the useful destination.
      return "/factures"
    case "InstallmentPayment":
      return movement.targetId ? `/treatment-plans/${movement.targetId}` : "/treatment-plans"
    case "Expense":
      // The expense rows are editable in place on this very page — no navigation.
      return null
  }
}

interface CaisseLedgerTableProps {
  movements: CaisseMovementDto[]
  loading?: boolean
}

export function CaisseLedgerTable({ movements, loading = false }: CaisseLedgerTableProps) {
  if (loading) {
    return (
      <div className="space-y-2" role="status" aria-label="Chargement de l'extrait">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="h-10 animate-pulse rounded bg-muted" />
        ))}
      </div>
    )
  }

  if (movements.length === 0) {
    return (
      <p className="py-8 text-center text-sm text-muted-foreground">
        Aucun mouvement sur cette période.
      </p>
    )
  }

  return (
    <div className="rounded-md border overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Date</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Libellé</TableHead>
            <TableHead>Patient</TableHead>
            <TableHead>Mode</TableHead>
            <TableHead className="text-right">Entrée</TableHead>
            <TableHead className="text-right">Sortie</TableHead>
            {/* Not « Solde » on its own: this opens at zero on the first line of the selected range and is not
                an account balance. Naming it after the period is the difference between a useful column and a
                figure a reader will take for the money in the drawer. */}
            <TableHead className="text-right">Solde de la période</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {movements.map((movement) => {
            const Icon = KIND_ICONS[movement.kind]
            const href = kindHref(movement)
            const isIn = movement.direction === "In"

            return (
              <TableRow key={`${movement.kind}-${movement.id}`} className={movement.isVoided ? "opacity-60" : undefined}>
                <TableCell className="whitespace-nowrap text-muted-foreground">
                  {formatDate(movement.occurredOn)}
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className="gap-1 whitespace-nowrap">
                    <Icon className="h-3 w-3" aria-hidden="true" />
                    {KIND_LABELS[movement.kind]}
                  </Badge>
                </TableCell>
                <TableCell className={movement.isVoided ? "line-through" : undefined}>
                  {href ? (
                    <Link href={href} className="underline-offset-4 hover:underline">
                      {movement.label}
                    </Link>
                  ) : (
                    movement.label
                  )}
                  {movement.isVoided && (
                    /* A void is a correction with an author. Showing the row and hiding who reversed it and why
                       would make the statement useless as the trail it exists to be. */
                    <span className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground no-underline">
                      <Ban className="h-3 w-3" aria-hidden="true" />
                      Annulé
                      {movement.voidReason ? ` — ${movement.voidReason}` : ""}
                      {movement.voidedByName ? ` (${movement.voidedByName})` : ""}
                    </span>
                  )}
                </TableCell>
                <TableCell className="text-muted-foreground">{movement.patientName ?? "—"}</TableCell>
                <TableCell className="text-muted-foreground">
                  {movement.method ? paymentMethodLabel(movement.method) : "—"}
                </TableCell>
                <TableCell className="text-right font-medium text-emerald-600 dark:text-emerald-500">
                  {isIn && !movement.isVoided ? (
                    <span className="inline-flex items-center gap-1">
                      <ArrowDownLeft className="h-3 w-3" aria-hidden="true" />
                      {formatDT(movement.amount)}
                    </span>
                  ) : isIn ? (
                    <span className="line-through">{formatDT(movement.amount)}</span>
                  ) : (
                    "—"
                  )}
                </TableCell>
                <TableCell className="text-right font-medium text-rose-600 dark:text-rose-500">
                  {!isIn && !movement.isVoided ? (
                    <span className="inline-flex items-center gap-1">
                      <ArrowUpRight className="h-3 w-3" aria-hidden="true" />
                      {formatDT(movement.amount)}
                    </span>
                  ) : !isIn ? (
                    <span className="line-through">{formatDT(movement.amount)}</span>
                  ) : (
                    "—"
                  )}
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatDT(movement.runningBalance)}</TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
