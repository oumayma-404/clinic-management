"use client"

import Link from "next/link"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { ArrowDownLeft, ArrowLeftRight, ArrowUpRight, Ban, Receipt, CalendarClock, SearchX, Undo2, Wallet } from "lucide-react"
import { formatDT, formatDate } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import type { CaisseMovementDto, CaisseMovementKind } from "@/lib/api/types"
import { paymentMethodLabel } from "@/components/factures/invoice-labels"
import { PatientNameLink } from "@/components/patient-name-link"
import { CaisseRowActions } from "@/components/caisse/caisse-row-actions"

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
 * A cheque in one line — « n° 4512873 · BIAT · encaissable le 15/09/2026 » (L8).
 *
 * <p>Returns `null` when the movement is not a cheque or carries none of the three fields (a cheque recorded before
 * they existed), so both trees can test it and omit the row entirely rather than printing an empty « Chèque : — ».</p>
 *
 * <p>⚠️ **One formatter for both trees.** This table renders twice — a card list below `md:` and a real `<table>`
 * above it — and a cheque described one way in one and another way in the other is the drift `ConventionPrompt`
 * documents for the same reason. « encaissable le » rather than « échéance » on purpose: the due date is when the
 * cheque may be *banked*, and « échéance » already means an instalment of a devis on this very screen.</p>
 */
function chequeSummary(movement: CaisseMovementDto): string | null {
  const parts = [
    movement.chequeNumber ? `n° ${movement.chequeNumber}` : null,
    movement.chequeBankName,
    movement.chequeDueDate ? `encaissable le ${formatDate(movement.chequeDueDate)}` : null,
  ].filter((part): part is string => Boolean(part))

  return parts.length > 0 ? parts.join(" · ") : null
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

/** La caisse is the « Finances » zone, so its empty state wears the same hue the rail and the eyebrow do. */
const MONEY_CHIP = zoneChipClass(ZONES.money)

interface CaisseLedgerTableProps {
  movements: CaisseMovementDto[]
  /**
   * The PERIOD's closing balance, from the server.
   *
   * ⚠️ Not derivable from `movements`: the phone footer used `movements[0].runningBalance`, i.e. the newest row of
   * the current PAGE, under a label reading « de la période » — so it changed on every page of the same window
   * (18 287,500 DT on page 1, 7 507,500 DT on page 2). Only the server sees the whole window.
   */
  closingBalance?: number
  loading?: boolean
  /** True when a search term is narrowing the statement — the empty state then has a different way out. */
  isFiltered?: boolean
  onClearSearch?: () => void
  /**
   * Re-read the statement after a line is corrected. Optional, and the actions column only exists when it is
   * given: the same table renders read-only surfaces (the export preview, an embedded summary) where offering
   * « Corriger » would be a control that cannot refresh what it changed.
   */
  onChanged?: () => void
}

export function CaisseLedgerTable({
  movements,
  closingBalance,
  loading = false,
  isFiltered = false,
  onClearSearch,
  onChanged,
}: CaisseLedgerTableProps) {
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
    /*
     * Two emptinesses, and only one of them is news. « Rien ne correspond à cette recherche » is recoverable and
     * says so; « aucun mouvement sur cette période » is a fact about a quiet day and gets NO create action —
     * a statement is a read of the four ledgers that already exist, so the only honest next step is to widen the
     * period, which is done with the date inputs above rather than from inside the table.
     */
    return isFiltered ? (
      <EmptyState
        size="compact"
        icon={SearchX}
        chipClassName={MONEY_CHIP}
        title="Aucun mouvement ne correspond"
        description="Aucun paiement, avoir ni dépense de la période ne correspond à cette recherche."
        secondaryAction={
          onClearSearch && (
            <Button size="sm" variant="outline" onClick={onClearSearch}>
              Effacer les filtres
            </Button>
          )
        }
      />
    ) : (
      <EmptyState
        size="compact"
        icon={ArrowLeftRight}
        chipClassName={MONEY_CHIP}
        title="Aucun mouvement sur cette période"
        description="Les paiements de factures, les échéances de devis, les avoirs remboursés et les dépenses de la période s'afficheront ici."
      />
    )
  }

  return (
    <div className="rounded-md border overflow-x-auto">
      {/*
        ⚠️ `runningBalance` is deliberately NOT a card field. It is « Solde de la période » — a fact about a
        movement's *position in an ordered list*, not about the movement. A card can be read on its own, and a
        running balance read on its own is a number with no referent. The statement reads newest first, so the
        period's closing balance is the FIRST row's; a footer restates it once for the card list rather than
        repeating a meaningless figure on every card.

        Entrée/Sortie collapse into one signed « Montant » for the same reason: two columns where exactly one is
        ever filled is a table's way of aligning direction, and a card shows direction with a sign and a colour.
      */}
      <CardList
        className={CARDS_ONLY_LG}
        ariaLabel="Extrait de caisse"
        items={movements}
        getKey={(m) => `${m.kind}-${m.id}`}
        muted={(m) => m.isVoided}
        title={(m) => <span className={m.isVoided ? "line-through" : undefined}>{m.label}</span>}
        href={(m) => kindHref(m) ?? undefined}
        status={(m) => {
          const Icon = KIND_ICONS[m.kind]
          return (
            <Badge variant="outline" className="gap-1 whitespace-nowrap">
              <Icon className="h-3 w-3" aria-hidden="true" />
              {KIND_LABELS[m.kind]}
            </Badge>
          )
        }}
        subtitle={(m) =>
          m.isVoided ? (
            <span className="flex items-center gap-1">
              <Ban className="h-3 w-3" aria-hidden="true" />
              Annulé
              {m.voidReason ? ` — ${m.voidReason}` : ""}
              {m.voidedByName ? ` (${m.voidedByName})` : ""}
            </span>
          ) : null
        }
        fields={(m) => [
          {
            label: m.direction === "In" ? "Entrée" : "Sortie",
            value: (
              // `text-success` / `text-destructive`, never `emerald-*` / `rose-*`: the four figures directly
              // above this statement are drawn on those tokens, and a card whose « Entrée » is a different
              // green from the « Encaissements » total it belongs to is two palettes in one viewport.
              <span
                className={
                  m.isVoided
                    ? "line-through"
                    : m.direction === "In"
                      ? "font-medium text-success"
                      : "font-medium text-destructive"
                }
              >
                {formatDT(m.amount)}
              </span>
            ),
          },
          { label: "Date", value: formatDate(m.occurredOn) },
          { label: "Patient", value: m.patientName },
          { label: "Mode", value: m.method ? paymentMethodLabel(m.method) : null },
          // L8 — omitted entirely when there is nothing to say, per the card rule (« a field with no value is
          // omitted, not rendered as « — » »).
          { label: "Chèque", value: chequeSummary(m) },
        ]}
      />
      {movements.length > 0 && closingBalance !== undefined && (
        <p className="border-t px-3 py-2 text-2xs text-muted-foreground md:hidden">
          {/* The server's window-wide figure — see `closingBalance`. */}
          Solde de la période : <span className="tabular-nums">{formatDT(closingBalance)}</span>
        </p>
      )}
      <Table containerClassName={TABLE_ONLY_LG}>
        <TableHeader>
          <TableRow>
            <TableHead>Date</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Libellé</TableHead>
            <TableHead>Patient</TableHead>
            <TableHead>Mode</TableHead>
            <TableHead className="text-right">Entrée</TableHead>
            <TableHead className="text-right">Sortie</TableHead>
            {/* Not « Solde » on its own: read bottom-up this column starts at zero at the range's oldest line and
                is not an account balance. Naming it after the period is the difference between a useful column and
                a figure a reader will take for the money in the drawer. */}
            <TableHead className="text-right">Solde de la période</TableHead>
            {onChanged && (
              <TableHead className="w-10 text-right">
                <span className="sr-only">Corriger</span>
              </TableHead>
            )}
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
                {/* The label clamps, the « Annulé » line below it never does — a void's reason is the row's whole
                    point and must not be what a two-line cap swallows. */}
                <TableCell className={movement.isVoided ? "line-through" : undefined} title={movement.label}>
                  {href ? (
                    <Link href={href} className="line-clamp-2 underline-offset-4 hover:underline">
                      {movement.label}
                    </Link>
                  ) : (
                    <span className="line-clamp-2">{movement.label}</span>
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
                <TableCell className="text-muted-foreground">
                  {movement.patientId && movement.patientName ? (
                    <PatientNameLink patientId={movement.patientId} name={movement.patientName} />
                  ) : (
                    movement.patientName ?? "—"
                  )}
                </TableCell>
                <TableCell className="text-muted-foreground">
                  {movement.method ? paymentMethodLabel(movement.method) : "—"}
                  {/* L8 — the same string the card list shows, from the one formatter, so the two trees cannot
                      drift about what a cheque row says. */}
                  {chequeSummary(movement) && (
                    <span className="mt-0.5 line-clamp-2 text-xs" title={chequeSummary(movement)!}>
                      {chequeSummary(movement)}
                    </span>
                  )}
                </TableCell>
                <TableCell numeric className="font-medium text-success">
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
                <TableCell numeric className="font-medium text-destructive">
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
                <TableCell numeric>{formatDT(movement.runningBalance)}</TableCell>
                {onChanged && (
                  <TableCell numeric className="w-10">
                    <CaisseRowActions movement={movement} onChanged={onChanged} />
                  </TableCell>
                )}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
