"use client"

import { useState, useEffect, useCallback } from "react"
import { ClinicGuard } from "@/components/clinic-guard"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"
import { PageHeader } from "@/components/ui/page-header"
import { ExportButton } from "@/components/ui/export-button"
import { AppShell } from "@/components/app-shell"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { invoicesApi } from "@/lib/api/invoices"
import type { InvoiceRevenueDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"
import { Loader2 } from "lucide-react"
import { INVOICE_STATUS_LABELS } from "@/components/factures/invoice-labels"
import { useDoctors } from "@/lib/hooks/use-doctors"

const ALL_STATUSES = "all"
/** Radix cannot hold an empty Select value, so « tous » is an explicit sentinel — same as ALL_STATUSES. */
const ALL_DOCTORS = "all-doctors"

/**
 * One money figure inside the shared {@link KpiGrid} surface.
 *
 * <p>`bg-card` is load-bearing: `KpiGrid` is a `bg-border` container showing through `gap-px`, so a cell that does
 * not paint its own background renders as a solid border block.</p>
 *
 * <p>Deliberately a local component rather than `KpiCard` — these figures are not links and carry no period
 * comparison, so reusing that would mean passing an `href` of `#` and lying about it. What it does share is the
 * one value treatment: `text-2xl font-semibold tabular-nums tracking-tight`. « Total encaissé » used to be
 * `font-bold` in a `Card` of its own here and `font-semibold` on a hairline grid two clicks away on la caisse —
 * the same number drawn two ways.</p>
 */
function RevenueFigure({
  label,
  tone,
  loading,
  failed,
  value,
}: {
  label: string
  /** The semantic ink for this figure. Omitted for a neutral one — never a `green-*` / `amber-*` literal. */
  tone?: string
  loading: boolean
  failed: boolean
  value?: number
}) {
  return (
    <div className="bg-card p-4">
      <p className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
        <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-primary/70" />
        {label}
      </p>
      <p className={cn("mt-1 text-2xl font-semibold tabular-nums tracking-tight", tone)}>
        <RevenueValue loading={loading} failed={failed} value={value} />
      </p>
    </div>
  )
}

/**
 * One KPI figure, with the three states kept apart (AC-P3.28): still loading, failed to load, or a real
 * amount. « — » is reserved for a figure that genuinely has no value — a failed read says « indisponible »
 * so nobody reads a network error as "nothing was billed this month".
 */
function RevenueValue({ loading, failed, value }: { loading: boolean; failed: boolean; value?: number }) {
  if (loading) {
    return (
      <span className="inline-flex items-center gap-2 text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        <span className="sr-only">Chargement…</span>
      </span>
    )
  }
  if (failed) {
    return <span className="text-base font-medium text-muted-foreground">Indisponible</span>
  }
  return <>{value === undefined ? "—" : formatDT(value)}</>
}

/**
 * I3 — the role gate, as a **wrapper around** the page rather than a branch inside it.
 *
 * <p>The split is not stylistic. Everything below opens its own `useState`/`useEffect` and fetches on mount, so a
 * branch inside the component would still fire every request for a secretary — three 403s and their French error
 * toasts, on top of the refusal card. Not mounting the body at all is what makes the refusal the only thing that
 * happens.</p>
 *
 * <p>The server is the gate: `GET /api/invoices/revenue` is `AdminOrDoctor`, so the two KPIs at the top of this page are refused outright. This is the polish on top of it.</p>
 */
export default function FacturesPage() {
  const { user, isLoading } = useSession()

  if (isLoading) {
    return (
      <ClinicGuard>
        <AppShell width="none" gutter={false}>
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        </AppShell>
      </ClinicGuard>
    )
  }

  if (hidesClinicWideMoney(user?.role)) {
    return (
      <ClinicGuard>
        <AppShell width="none" gutter={false}>
          <AccessDeniedCard description="Les factures et le chiffre d'affaires de la clinique sont réservés au praticien et à l'administrateur. Vous pouvez encaisser un paiement depuis la fiche du patient." />
        </AppShell>
      </ClinicGuard>
    )
  }

  return <FacturesContent />
}

function FacturesContent() {
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  // The clinic roster, for the L9 practitioner filter. The hook already resolves the caller's own doctor.
  const { doctors } = useDoctors()
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  // L9 — the practitioner filter. `ALL_DOCTORS` rather than "" because a Radix Select cannot hold an empty value.
  const [doctorId, setDoctorId] = useState<string>(ALL_DOCTORS)
  // Dashboard drill-through (« Encaissé » / « Facturé »): ?from=&to=&status= pre-applies the filters so the table and
  // the revenue KPIs describe the same window the card counted. window.location in an effect rather than
  // useSearchParams — the repo's idiom, and it keeps this page out of a Suspense boundary.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlFrom = params.get("from")
    const urlTo = params.get("to")
    const urlStatus = params.get("status")
    // A malformed date or an unknown status is ignored, not refused — a stale link lands on the unfiltered list.
    if (urlFrom && !Number.isNaN(Date.parse(urlFrom))) setFrom(urlFrom)
    if (urlTo && !Number.isNaN(Date.parse(urlTo))) setTo(urlTo)
    if (urlStatus && urlStatus in INVOICE_STATUS_LABELS) setStatus(urlStatus)
  }, [])

  const [revenue, setRevenue] = useState<InvoiceRevenueDto | null>(null)
  const [revenueLoading, setRevenueLoading] = useState(true)
  // AC-P3.28 — the revenue read used to swallow its error without even a console.error, so a failed call and
  // a genuinely-empty period both rendered « — ». On a money screen those must not look alike.
  const [revenueError, setRevenueError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const fromIso = from ? `${from}T00:00:00` : undefined
  const toIso = to ? `${to}T23:59:59` : undefined
  const statusFilter = status === ALL_STATUSES ? undefined : status
  const doctorFilter = doctorId === ALL_DOCTORS ? undefined : doctorId

  const loadRevenue = useCallback(async () => {
    try {
      setRevenueLoading(true)
      setRevenueError(null)
      const data = await invoicesApi.revenue({ from: fromIso, to: toIso })
      setRevenue(data)
    } catch (err) {
      setRevenue(null)
      setRevenueError(getErrorMessage(err, "Les recettes n'ont pas pu être chargées."))
    } finally {
      setRevenueLoading(false)
    }
  }, [fromIso, toIso])

  useEffect(() => {
    loadRevenue()
  }, [loadRevenue, reloadKey])

  /*
   * ⚠️ There is no « Filtrer » button, deliberately. The three controls flow straight into `fromIso`/`toIso`/
   * `statusFilter`, which are dependencies of both the revenue read and the table's `fetchPage` — so the list has
   * *already* narrowed by the time a user could reach a submit button. A button that does nothing is worse than a
   * missing one: it reads as « le filtre n'est pas appliqué tant que vous n'avez pas cliqué », so the one figure
   * a user then trusts is the one they believe they have not yet asked for.
   */
  const hasFilters = from !== "" || to !== "" || status !== ALL_STATUSES || doctorId !== ALL_DOCTORS
  const resetFilters = () => {
    setDoctorId(ALL_DOCTORS)
    setFrom("")
    setTo("")
    setStatus(ALL_STATUSES)
    setReloadKey((k) => k + 1)
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Factures &amp; recettes"
          subtitle="Notes d'honoraires, encaissements et suivi des recettes."
          // L5 — the same window and statut the table is showing. The endpoint is AdminOrDoctor (a CSV of every
          // note d'honoraires IS the clinic-wide money read), and a refusal arrives as its French message.
          actions={
            <ExportButton
              path="/invoices/export"
              label="factures"
              params={{ from: fromIso, to: toIso, status: statusFilter, doctorId: doctorFilter }}
            />
          }
        />

        {/* Revenue summary. AC-P3.28 — three states, never conflated: loading, failed-to-load (with a
            retry), and a real figure. The refusal goes through the shared banner rather than a fourth
            hand-rolled red box — that primitive renders on `--destructive-wash` and follows the theme. */}
        <FormErrorBanner
          message={revenueError}
          action={{ label: "Réessayer", onClick: () => void loadRevenue(), disabled: revenueLoading }}
        />
        {/*
          The three figures share ONE surface (`KpiGrid`) — the same object la caisse and the dashboard draw
          their money on. Three separate `Card`s meant three borders and three shadows for a row of figures
          that is conceptually one thing, and « Encaissé » was `text-green-700` here, `text-emerald-600` in
          the caisse statement and `text-success` on the dashboard: three greens for one concept, on the
          screens where a colour that does not mean the same thing twice costs trust.

          `sm:grid-cols-3` overrides the grid's default 2-up from `sm` on: three money figures belong on one
          line as soon as there is room, and the base stays 2-up because one figure per row is the scroll the
          grid's own note argues against.
        */}
        <KpiGrid columns={3} className="sm:grid-cols-3">
          <RevenueFigure
            label="Total facturé"
            loading={revenueLoading}
            failed={!!revenueError}
            value={revenue?.totalInvoiced}
          />
          <RevenueFigure
            label="Total encaissé"
            tone="text-success"
            loading={revenueLoading}
            failed={!!revenueError}
            value={revenue?.totalCollected}
          />
          {/* `text-warning-ink`, not `text-warning`: the darkened amber step is the one that stays legible
              wherever the theme's amber is used as ink. */}
          <RevenueFigure
            label="Reste à recouvrer"
            tone="text-warning-ink"
            loading={revenueLoading}
            failed={!!revenueError}
            value={revenue?.outstanding}
          />
        </KpiGrid>

        {/* Filters */}
        <Card>
          <CardContent className="flex flex-wrap items-end gap-4 pt-6">
            <div className="space-y-1.5">
              <Label htmlFor="from">Du</Label>
              <Input id="from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="to">Au</Label>
              <Input id="to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="status">Statut</Label>
              <Select value={status} onValueChange={setStatus}>
                <SelectTrigger id="status" className="w-48">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_STATUSES}>Tous</SelectItem>
                  {Object.entries(INVOICE_STATUS_LABELS).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            {/*
              L9 — the practitioner filter. Offered only when the practice HAS more than one practitioner: in the
              single-dentist case (the common Tunisian one) it would be a control with exactly one meaningful value,
              and « filtrer par praticien » on a solo practice reads as a feature that is broken.
            */}
            {doctors.length > 1 && (
              <div className="space-y-1.5">
                <Label htmlFor="doctor">Praticien</Label>
                <Select value={doctorId} onValueChange={setDoctorId}>
                  <SelectTrigger id="doctor" className="w-48">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_DOCTORS}>Tous</SelectItem>
                    {doctors
                      .filter((d) => d.id)
                      .map((d) => (
                        <SelectItem key={d.id} value={d.id as string}>
                          {d.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </div>
            )}
            {/* ⚠️ Historical notes carry no practitioner, so a filter excludes them. Said out loud rather than left
                for someone to discover as « mes factures ont disparu ». */}
            {doctorId !== ALL_DOCTORS && (
              <p className="w-full text-xs text-muted-foreground">
                Les notes d&apos;honoraires antérieures à la mise à jour ne sont attribuées à aucun praticien et
                n&apos;apparaissent pas sous ce filtre.
              </p>
            )}
            {/* Shown only when something is actually narrowing the list — an always-present « Effacer » on an
                unfiltered page invites a click that changes nothing. */}
            {hasFilters && (
              <Button variant="outline" onClick={resetFilters}>
                Effacer les filtres
              </Button>
            )}
          </CardContent>
        </Card>

        <InvoicesTable
          from={fromIso}
          to={toIso}
          status={statusFilter}
          doctorId={doctorFilter}
          reloadKey={reloadKey}
          onChanged={loadRevenue}
        />
      </AppShell>
    </ClinicGuard>
  )
}
