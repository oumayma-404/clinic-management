"use client"

import { useCallback, useEffect, useState } from "react"
import Link from "next/link"
import { CheckCircle2, Circle, Wallet } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { ContactActions } from "@/components/visits/contact-actions"
import { PatientNameLink } from "@/components/patient-name-link"
import { billingApi } from "@/lib/api/billing"
import type { PatientBillingSummaryDto, PatientDebtLineDto, ResteAPayerRowDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { formatDT, formatDateFr } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * One patient's « Reste à payer »: due now, then not due yet, acts by date under each document. Every figure is the
 * server's `billing-summary` lines, so the panel cannot disagree with the patient file.
 */
export function ResteAPayerPatientSheet({
  row,
  reloadKey,
  busyDocumentId,
  onClose,
  onCollect,
}: {
  row: ResteAPayerRowDto | null
  /** Bumped by the parent after a payment, so the panel re-reads while it stays open. */
  reloadKey: number
  busyDocumentId: string | null
  onClose: () => void
  onCollect: (line: PatientDebtLineDto) => void
}) {
  const [summary, setSummary] = useState<PatientBillingSummaryDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const patientId = row?.patientId ?? null

  const load = useCallback(async () => {
    if (!patientId) return
    try {
      setSummary(await billingApi.getPatientSummary(patientId))
      setError(null)
    } catch (err) {
      // A failed read is not an empty balance — say so, never render « rien à payer ».
      setError(getErrorMessage(err))
      setSummary(null)
    }
  }, [patientId])

  useEffect(() => {
    setSummary(null)
    setError(null)
    void load()
  }, [load, reloadKey])

  const lines = summary?.lines ?? []
  const dueLines = lines.filter((l) => l.dueNow > 0)
  const runningLines = lines.filter((l) => l.dueNow <= 0 && l.outstanding > 0)
  const due = lines.reduce((s, l) => s + l.dueNow, 0)
  const running = lines.reduce((s, l) => s + (l.outstanding - l.dueNow), 0)

  return (
    <Sheet open={row !== null} onOpenChange={(open) => { if (!open) onClose() }}>
      <SheetContent side="right" className="w-full gap-0 sm:max-w-xl">
        <SheetHeader className="border-b pe-12">
          <SheetTitle className="sr-only">Reste à payer de {row?.patientName ?? "ce patient"}</SheetTitle>
          <SheetDescription className="sr-only">Ce qui est dû maintenant, puis ce qui est en cours, document par document.</SheetDescription>
          {row && (
            <div className="flex flex-wrap items-center gap-2">
              <PatientNameLink patientId={row.patientId} name={row.patientName} className="basis-full text-lg font-semibold" />
              {row.phoneNumber && (
                <span className="me-1 font-mono text-sm tabular-nums text-muted-foreground">{row.phoneNumber}</span>
              )}
              <ContactActions phoneE164={row.phoneE164} name={row.patientName} variant="default" />
            </div>
          )}
          {summary && <SplitBar due={due} running={running} total={summary.totalOutstanding} />}
        </SheetHeader>

        <div className="min-h-0 flex-1 space-y-6 overflow-y-auto px-4 py-4">
          {error ? (
            <LoadFailureNotice message={error} detail="Aucun paiement n'a été enregistré." onRetry={() => void load()} />
          ) : !summary ? (
            <div className="space-y-3" role="status" aria-label="Chargement">
              {[0, 1].map((i) => <div key={i} className="h-32 animate-pulse rounded-lg bg-muted" />)}
            </div>
          ) : lines.length === 0 ? (
            <p className="text-sm text-muted-foreground" role="status">Ce patient n&apos;a plus rien à payer.</p>
          ) : (
            <>
              {dueLines.length > 0 && (
                <LineGroup tone="due" title="À relancer" hint="à régler maintenant">
                  {dueLines.map((line) => (
                    <DebtDocument key={line.documentId} line={line} busy={busyDocumentId === line.documentId} onCollect={onCollect} />
                  ))}
                </LineGroup>
              )}
              {runningLines.length > 0 && (
                <LineGroup tone="running" title="En cours" hint="se paie au fil du traitement">
                  {runningLines.map((line) => (
                    <DebtDocument key={line.documentId} line={line} busy={busyDocumentId === line.documentId} onCollect={onCollect} />
                  ))}
                </LineGroup>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}

/** The two parts of « Solde dû » side by side — a bar for the eye, the figures in words beside it. */
function SplitBar({ due, running, total }: { due: number; running: number; total: number }) {
  if (total <= 0) return null
  return (
    <div className="space-y-2 pt-2">
      <div className="flex h-2 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
        {due > 0 && <span className="bg-warning" style={{ width: `${(due / total) * 100}%` }} />}
        {running > 0 && <span className="flex-1 bg-primary" />}
      </div>
      <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
        {due > 0 && <SplitFigure dot="bg-warning" label="À relancer" value={due} />}
        {running > 0 && <SplitFigure dot="bg-primary" label="En cours" value={running} />}
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <dt>Solde dû</dt>
          <dd className="font-semibold tabular-nums text-foreground">{formatDT(total)}</dd>
        </div>
      </dl>
    </div>
  )
}

function SplitFigure({ dot, label, value }: { dot: string; label: string; value: number }) {
  return (
    <div className="flex items-center gap-1.5">
      <span className={cn("size-2 rounded-full", dot)} aria-hidden="true" />
      <dt>{label}</dt>
      <dd className="font-semibold tabular-nums">{formatDT(value)}</dd>
    </div>
  )
}

function LineGroup({
  tone,
  title,
  hint,
  children,
}: {
  tone: "due" | "running"
  title: string
  hint: string
  children: React.ReactNode
}) {
  return (
    <section className="space-y-3" aria-label={title}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold">
          <span className={cn("size-2 rounded-full", tone === "due" ? "bg-warning" : "bg-primary")} aria-hidden="true" />
          {title}
        </h3>
        <span className="text-xs text-muted-foreground">{hint}</span>
      </div>
      {children}
    </section>
  )
}

/** One note or devis: its acts by date, then Total · Payé · Reste and the payment that settles it. */
function DebtDocument({
  line,
  busy,
  onCollect,
}: {
  line: PatientDebtLineDto
  busy: boolean
  onCollect: (line: PatientDebtLineDto) => void
}) {
  const isDue = line.dueNow > 0
  const reason = isDue ? line.dueReason : line.runningReason
  // A devis whose échéancier can take nothing gets the devis itself, never a dialog with no target.
  const payable = line.kind === "Invoice" || line.payableInstallmentId !== null

  return (
    <article
      className={cn(
        "overflow-hidden rounded-lg border bg-card",
        isDue && "border-warning/50",
      )}
    >
      <header className="flex flex-wrap items-center justify-between gap-2 border-b px-3 py-2.5 text-sm">
        <span className="font-semibold">
          {line.label}{line.number ? ` n° ${line.number}` : ""}
        </span>
        {reason && (
          <Badge
            variant="outline"
            className={cn(
              "whitespace-normal text-start",
              isDue ? "border-warning/40 bg-warning-wash text-warning-ink" : "bg-primary/10 text-primary",
            )}
          >
            {reason}
          </Badge>
        )}
      </header>

      {line.acts.length > 0 && (
        <ul className="divide-y text-sm">
          {line.acts.map((act, i) => (
            <li key={`${act.designation}-${i}`} className="grid grid-cols-[5.5rem_1rem_minmax(0,1fr)_auto] items-start gap-2 px-3 py-2">
              <span className="whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                {act.date ? formatDateFr(act.date) : "—"}
              </span>
              {act.done ? (
                <CheckCircle2 className="mt-0.5 size-4 text-success" aria-label="fait" />
              ) : (
                <Circle className="mt-0.5 size-4 text-muted-foreground" aria-label="pas encore fait" />
              )}
              <span className={cn("min-w-0 break-words", !act.done && "text-muted-foreground")}>
                {act.designation}
                {act.teeth.length > 0 && (
                  <span className="ms-1.5 rounded-full bg-muted px-1.5 text-2xs font-semibold tabular-nums">
                    {act.teeth.join(" · ")}
                  </span>
                )}
                {act.progress && <span className="ms-1.5 text-2xs text-muted-foreground">{act.progress}</span>}
                {!act.done && !act.progress && <span className="ms-1.5 text-2xs text-muted-foreground">prévu</span>}
              </span>
              <span className="whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                {act.amount > 0 ? formatDT(act.amount) : "—"}
              </span>
            </li>
          ))}
        </ul>
      )}

      <footer className="flex flex-wrap items-center justify-between gap-3 bg-muted/40 px-3 py-2.5 text-xs">
        <dl className="flex flex-wrap gap-x-3 gap-y-1 text-muted-foreground">
          <div className="flex gap-1"><dt>Total</dt><dd className="font-semibold tabular-nums text-foreground">{formatDT(line.total)}</dd></div>
          <div className="flex gap-1"><dt>Payé</dt><dd className="font-semibold tabular-nums text-foreground">{formatDT(line.collected)}</dd></div>
          {isDue && line.dueNow < line.outstanding && (
            <div className="flex gap-1"><dt>À régler maintenant</dt><dd className="font-semibold tabular-nums text-warning-ink">{formatDT(line.dueNow)}</dd></div>
          )}
        </dl>
        <div className="flex items-center gap-3">
          <div className="text-end">
            <div className={cn("text-sm font-bold tabular-nums", isDue && "text-warning-ink")}>{formatDT(line.outstanding)}</div>
            <div className="text-2xs text-muted-foreground">reste à payer</div>
          </div>
          {payable ? (
            <Button
              size="sm"
              className="coarse:h-11"
              disabled={busy}
              onClick={() => onCollect(line)}
              aria-label={`Encaisser sur ${line.label}${line.number ? ` n° ${line.number}` : ""}`}
            >
              <Wallet className="size-4" aria-hidden="true" />
              {busy ? "Ouverture…" : "Encaisser"}
            </Button>
          ) : (
            <Button asChild size="sm" variant="outline" className="coarse:h-11">
              <Link href={`/treatment-plans/${line.documentId}`}>Ouvrir le devis</Link>
            </Button>
          )}
        </div>
      </footer>
    </article>
  )
}
