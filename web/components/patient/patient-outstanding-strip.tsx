"use client"

import { useState } from "react"
import { AlertTriangle, Receipt, ClipboardList, Wallet } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import type { PatientBillingSummaryDto, PatientDebtLineDto, VisitToCloseDto } from "@/lib/api/types"

/**
 * The section's anchor, exported so the patient header's « Solde dû » can scroll to it.
 *
 * ⚠️ Shared rather than a literal at each end: the figure in the header is the only route to this section now
 * that it lives inside a tab, and two hand-written ids is how that route silently becomes a no-op.
 */
export const PATIENT_OUTSTANDING_SECTION_ID = "patient-outstanding"

interface PatientOutstandingStripProps {
  /** `null` = the read failed or has not landed. The band renders nothing — never « 0,000 DT ». */
  summary: PatientBillingSummaryDto | null
  /**
   * This patient's séances whose remaining question is « combien a-t-il payé ? », straight from
   * `GET /appointments/to-close?patientId=…`. Filtered by `VisitClosureRules` server-side, so a contrôle
   * gratuit, a séance carried by a devis, and one marked « rien à facturer » or « retirée » are already gone.
   */
  unbilled: VisitToCloseDto[]
  /** Record a payment on this note d'honoraires. The host re-reads the invoice before opening its dialog. */
  onCollectInvoice: (line: PatientDebtLineDto) => void
  /** Record a payment on this devis's oldest unpaid échéance. Only called when `payableInstallmentId` is set. */
  onCollectInstallment: (line: PatientDebtLineDto) => void
  /** Open the document itself — the devis workspace, or the note in the Factures tab. */
  onOpenDocument: (line: PatientDebtLineDto) => void
  /** Raise the note d'honoraires for a séance that has none, via « Facturer cette intervention ». */
  onBillVisit: (visit: VisitToCloseDto) => void
  /** True while a document is being re-read, so the pressed row can disable rather than double-open. */
  busyDocumentId?: string | null
}

/**
 * « Reste à payer » — what this patient still owes, itemised, with the payment beside each row.
 *
 * <p><b>The gap this closes.</b> The patient file showed the debt as a single inert `<span>` in the identity
 * strip — « Solde dû 240,000 DT », not a link and not a button, with nothing saying what it covered. The
 * composition lived in two places the file could not put side by side: the notes d'honoraires in the seventh
 * tab, and the devis échéanciers only on `/treatment-plans/{id}`. So settling an échéance meant leaving the
 * patient's file altogether, with the patient standing at the desk. And `/factures` is closed to a secretary,
 * whose refusal text reads « Vous pouvez encaisser un paiement depuis la fiche du patient » — a promise this
 * page could only keep through tab 7 of 7.</p>
 *
 * <p><b>Where it sits, and why it moved.</b> Under <i>l'historique des actes</i>, inside the « Actes dentaires »
 * tab. It was a `border-y` band above the tabs at first — which put a money surface between the odontogramme and
 * the whole record, and pushed the tabs down on every patient who owed anything. Here it reads as the second
 * half of the tab it is in: the table above says what was done and what each fiche took, this says what is
 * still owed on it and settles it. It renders <b>nothing</b> when nothing is owed and nothing is unbilled, so a
 * settled patient pays no height at all.</p>
 *
 * <p>⚠️ Because it lives inside a tab it cannot be seen from the top of the page, so
 * {@link PATIENT_OUTSTANDING_SECTION_ID} is the anchor the header's « Solde dû » scrolls to — that figure is
 * now the only route here, which is why it is a real control rather than a `<span>`.</p>
 *
 * <p><b>One bordered surface, both trees inside it</b> (`invoices-table`'s shape). A bare `Table` element paints
 * `bg-card` with no border and inherits its radius from a parent that had none, so it read as a borderless
 * white slab beside lists that are all framed.</p>
 *
 * <p><b>Two groups, and only the first is money.</b> « Reste à payer » decomposes the served balance exactly.
 * « Travail non facturé » is séances that produced no document, which is <i>not</i> debt — no note, no échéance,
 * nothing in any money read — and is therefore kept out of the total and given « Facturer » rather than
 * « Encaisser ». Folding it in would make this band disagree with « Solde dû », la caisse, the dashboard and the
 * console, five reads `MoneyReadConsistencyTests` holds equal.</p>
 *
 * <p>⚠️ <b>Nothing here sums anything.</b> Every figure is served. Only the server can apply
 * `PlanBillingRules.BilledPlanIds`, and the one client-side attempt at this rule measured 4 of 4 bridged plans
 * as wholly unpaid, two of them fully settled.</p>
 */
export function PatientOutstandingStrip({
  summary,
  unbilled,
  onCollectInvoice,
  onCollectInstallment,
  onOpenDocument,
  onBillVisit,
  busyDocumentId,
}: PatientOutstandingStripProps) {
  const lines = summary?.lines ?? []
  const owed = summary?.totalOutstanding ?? 0
  const refunded = summary?.creditedTotal ?? 0
  const [showAll, setShowAll] = useState(false)

  /*
   * ⚠️ **The CARD tree is capped, and it is a row-count slice rather than a `max-h`.**
   *
   * Measured at 320×568: four documents rendered a **964 px** band, and this sits between the odontogramme and
   * the seven tabs — so a patient with a handful of unpaid notes pushed the whole record two screens down.
   * `patient-undocumented-visits` capped itself for the same reason one section up.
   *
   * It caps by rows and not by pixels, deliberately: that component's own docstring says a px cap is honest
   * there **because every row is a single line**, and these are not — a card carries a title, the acts it
   * covers, two or three money fields and a full-width action, so no constant describes N of them. Nothing
   * factual is lost either way, because the header above already states the whole total and the full document
   * count; the cap only defers rows the reader can open in one press. The table tree is untouched — it is
   * 312 px for the same four rows.
   */
  const CARD_ROWS = 2
  const shownLines = showAll ? lines : lines.slice(0, CARD_ROWS)
  const hiddenCount = lines.length - shownLines.length

  // Nothing owed and nothing unbilled is nothing to say. A « Reste à payer 0,000 DT » heading on every settled
  // patient is a line that trains the eye to skip the line.
  if (lines.length === 0 && unbilled.length === 0) return null

  return (
    <section
      id={PATIENT_OUTSTANDING_SECTION_ID}
      aria-label="Reste à payer"
      className="flex scroll-mt-4 flex-col gap-2"
    >
      {lines.length > 0 && (
        <>
          {/* One line, not a band: the heading, the figure, the count and the refund all sit in the row the
              section is titled by — this is inside a tab under a full table, so a second header row here is
              height spent restating what the figure beside it already says. */}
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h3 className="text-sm font-semibold">Reste à payer</h3>
            <span className="text-base font-semibold tabular-nums text-warning-ink">{formatDT(owed)}</span>
            <span className="text-xs text-muted-foreground">
              {lines.length === 1 ? "1 document" : `${lines.length} documents`}
            </span>
            {/*
              The one thing `creditedTotal` exists for: an avoir hands the cash back *and* cancels the fee, so
              it never moves the balance — which leaves a refunded patient looking like one who simply never
              paid. Stated only when there is one.
            */}
            {refunded > 0 && (
              <span className="text-xs text-muted-foreground">
                dont {formatDT(refunded)} remboursés
              </span>
            )}
          </div>

          {/*
            ⚠️ **ONE bordered surface holding both trees** — `invoices-table`'s shape, and the shape every
            other list in this app uses. A bare `Table` element paints `bg-card` with no border and takes its radius
            from a parent that had none, so it rendered as a borderless white slab that did not read as a table
            on a page whose other lists all do.

            Five columns, so `lg:` and not `md:`: this table is nested a `Card`-inside-a-`TabsContent` deep,
            where the box measures ~451 px at 820 px, and the column that cannot give width back is
            « Actions » — the whole point of the section.
          */}
          <div className="overflow-x-auto rounded-md border">
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead>Document</TableHead>
                  <TableHead>Concerne</TableHead>
                  <TableHead>Depuis</TableHead>
                  <TableHead className="text-right">Reste</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.map((line) => (
                  <TableRow key={line.documentId}>
                    <TableCell className="whitespace-nowrap">
                      <span className="font-medium">{documentTitle(line)}</span>
                    </TableCell>
                    <TableCell clamp title={line.covers}>
                      {line.covers}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <SinceCell line={line} />
                    </TableCell>
                    <TableCell numeric>
                      <span className="font-semibold text-warning-ink">{formatDT(line.outstanding)}</span>
                      <ShortScheduleNote line={line} />
                    </TableCell>
                    <TableCell numeric>
                      <RowAction
                        line={line}
                        busy={busyDocumentId === line.documentId}
                        onCollectInvoice={onCollectInvoice}
                        onCollectInstallment={onCollectInstallment}
                        onOpenDocument={onOpenDocument}
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            {/* Card order: identity → status → money → date. Inside the same bordered box as the table, so the
                two trees are one surface at every width rather than a card stack below a framed table. */}
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Documents à régler"
              items={shownLines}
              getKey={(line) => line.documentId}
              title={(line) => documentTitle(line)}
              subtitle={(line) => line.covers || null}
              status={(line) =>
                line.isOverdue ? (
                  <Badge variant="destructive" className="gap-1">
                    <AlertTriangle aria-hidden="true" className="size-3" />
                    En retard
                  </Badge>
                ) : null
              }
              fields={(line) => [
                {
                  label: "Reste",
                  value: (
                    <span className="font-semibold text-warning-ink">{formatDT(line.outstanding)}</span>
                  ),
                },
                line.collected > 0 ? { label: "Encaissé", value: formatDT(line.collected) } : null,
                shortSchedule(line)
                  ? {
                      label: "Échéancier",
                      value: (
                        <span className="text-warning-ink">
                          {formatDT(line.payableRoom)} seulement — à compléter sur le devis
                        </span>
                      ),
                    }
                  : null,
                line.since
                  ? { label: line.kind === "Invoice" ? "Émise le" : "Échéance", value: formatDateFr(line.since) }
                  : null,
              ]}
              primaryAction={(line) => (
                <RowAction
                  line={line}
                  busy={busyDocumentId === line.documentId}
                  onCollectInvoice={onCollectInvoice}
                  onCollectInstallment={onCollectInstallment}
                  onOpenDocument={onOpenDocument}
                  full
                />
              )}
            />

            {/* Card tree only — the table shows every row and needs no expander. `CARDS_ONLY_LG` rather than a
                JS width test, so rotating a tablet changes the presentation and not the state. It carries a
                `border-t` and no border of its own, so inside the shared box it reads as the list's footer
                rather than a filet flottant under it — `/a-cloturer`'s pager shape. */}
            {(hiddenCount > 0 || showAll) && (
              <div className={CARDS_ONLY_LG}>
                <Button
                  variant="ghost"
                  size="sm"
                  className="w-full rounded-none border-t coarse:h-11"
                  onClick={() => setShowAll((v) => !v)}
                >
                  {showAll
                    ? "Afficher moins"
                    : hiddenCount === 1
                      ? "Afficher 1 autre document"
                      : `Afficher les ${hiddenCount} autres documents`}
                </Button>
              </div>
            )}
          </div>
        </>
      )}

      {unbilled.length > 0 && (
        <>
          {/*
            ⚠️ Deliberately NOT part of the figure above, and the heading says which question it answers. These
            séances produced no document at all, so nothing about them is in « Solde dû », « Créances », la
            caisse or the dashboard — a fiche saved with « Payé » = 0 raises no note (`DentalRecordAutoBilling`).
            Calling it debt would put this band at odds with every other money read in the product; leaving it
            out entirely is what the « Actes dentaires » tab already does, and it shows an amber « Reste » for
            the same séance, which is the contradiction this group resolves by naming it.
          */}
          <div className="mt-1 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h3 className="text-sm font-semibold">Travail non facturé</h3>
            <span className="text-xs text-muted-foreground">
              {unbilled.length === 1
                ? "1 séance sans note d'honoraires"
                : `${unbilled.length} séances sans note d'honoraires`}
              {" — pas encore une créance"}
            </span>
          </div>

          <ul
            aria-label="Séances non facturées"
            className="flex flex-col divide-y rounded-md border"
          >
            {unbilled.map((visit) => (
              <li
                key={visit.appointmentId}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 bg-card px-3 py-2 text-sm first:rounded-t-md last:rounded-b-md"
              >
                <span className="whitespace-nowrap font-medium tabular-nums">
                  {formatDateFr(visit.appointmentDateTime)}
                </span>
                <span className="min-w-0 flex-1 truncate text-muted-foreground" title={actsOf(visit)}>
                  {actsOf(visit)}
                </span>
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-1 coarse:h-11"
                  onClick={() => onBillVisit(visit)}
                  aria-label={`Facturer la séance du ${formatDateFr(visit.appointmentDateTime)}`}
                >
                  <Receipt aria-hidden="true" className="size-4" />
                  Facturer
                </Button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

/** « Note d'honoraires 2026-0042 ». The number is what staff say out loud; the label is what it is. */
function documentTitle(line: PatientDebtLineDto): string {
  return line.number ? `${line.label} ${line.number}` : line.label
}

/**
 * The acts a séance was booked for. Falls back to a statement rather than an empty cell — a booking with no
 * acts is a real state (a walk-in typed as « consultation »), not a missing value.
 */
function actsOf(visit: VisitToCloseDto): string {
  return visit.procedures.length > 0 ? visit.procedures.join(", ") : "Séance sans acte nommé"
}

/**
 * ⚠️ The échéancier can no longer take what the devis owes — `AddItems` raised the plan's total without
 * re-spreading the schedule, which is exactly what `reconcile-money`'s `plan-schedule-balances` reports.
 * `Installment.RecordPayment` is bounded by one échéance's own room, so offering the row's figure would produce
 * a refusal for the number the row had just printed.
 */
function shortSchedule(line: PatientDebtLineDto): boolean {
  return line.kind === "TreatmentPlan" && line.payableRoom < line.outstanding
}

function ShortScheduleNote({ line }: { line: PatientDebtLineDto }) {
  if (!shortSchedule(line)) return null
  return (
    <span className="block text-2xs font-normal text-muted-foreground">
      {line.payableRoom > 0
        ? `échéancier : ${formatDT(line.payableRoom)}`
        : "aucune échéance ouverte"}
    </span>
  )
}

/**
 * ⚠️ « En retard » is a **devis** statement only, served from `InstallmentLateness`. A note d'honoraires is
 * payable on issue, so a lateness derived from its own date would be true of every unpaid note the day after it
 * was raised — the shape that made 25 of 27 échéances read « En retard » before that rule existed. A note
 * carries its **age**, which is a fact rather than a policy nobody set.
 */
function SinceCell({ line }: { line: PatientDebtLineDto }) {
  if (!line.since) {
    // No date to say it with. State nothing rather than invent one.
    return <span className="text-muted-foreground">—</span>
  }
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className="tabular-nums">{formatDateFr(line.since)}</span>
      {line.isOverdue && (
        <Badge variant="destructive" className="gap-1">
          <AlertTriangle aria-hidden="true" className="size-3" />
          En retard
        </Badge>
      )}
    </span>
  )
}

/**
 * One action per row, and never a dead one.
 *
 * <p>A note takes a payment directly. A devis takes one on its oldest unpaid échéance — and when it has none
 * (`payableInstallmentId` null: an échéancier fully settled while an amendment raised the total) there is
 * nowhere to put the money, so the row offers the devis instead. That is § 0's rule: a capability a platform
 * limit genuinely prevents gets an explicit French message, never a hidden control or a button that answers a
 * refusal.</p>
 */
function RowAction({
  line,
  busy,
  onCollectInvoice,
  onCollectInstallment,
  onOpenDocument,
  full = false,
}: {
  line: PatientDebtLineDto
  busy: boolean
  onCollectInvoice: (line: PatientDebtLineDto) => void
  onCollectInstallment: (line: PatientDebtLineDto) => void
  onOpenDocument: (line: PatientDebtLineDto) => void
  full?: boolean
}) {
  const canCollect = line.kind === "Invoice" || line.payableInstallmentId !== null

  if (!canCollect) {
    return (
      <Button
        size="sm"
        variant="outline"
        className={full ? "w-full gap-1 coarse:h-11" : "gap-1 coarse:h-11"}
        onClick={() => onOpenDocument(line)}
        aria-label={`Ouvrir le devis ${line.number ?? ""} — aucune échéance ne peut recevoir ce paiement`.trim()}
      >
        <ClipboardList aria-hidden="true" className="size-4" />
        Ouvrir le devis
      </Button>
    )
  }

  return (
    <Button
      size="sm"
      className={full ? "w-full gap-1 coarse:h-11" : "gap-1 coarse:h-11"}
      disabled={busy}
      onClick={() => (line.kind === "Invoice" ? onCollectInvoice(line) : onCollectInstallment(line))}
      // Named for the row, not just the verb: a band of four documents would otherwise announce « Encaisser »
      // four times over.
      aria-label={`Encaisser sur ${quoteFr(documentTitle(line))}`}
    >
      <Wallet aria-hidden="true" className="size-4" />
      {busy ? "Ouverture…" : "Encaisser"}
    </Button>
  )
}
