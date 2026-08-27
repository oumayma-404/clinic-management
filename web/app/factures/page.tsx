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
import { Stat, StatStrip } from "@/components/ui/stat-strip"
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
          <AccessDeniedCard description="Les factures et le chiffre d'affaires du cabinet sont réservés au praticien et à l'administrateur. Vous pouvez encaisser un paiement depuis la fiche du patient." />
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
          The three figures share ONE surface (`StatStrip`) — the app's one summary strip, the same object la
          caisse, « Chèques » and « Rappels » draw. Three separate `Card`s meant three borders and three
          shadows for a row that is conceptually one thing, and « Encaissé » was `text-green-700` here,
          `text-emerald-600` in the caisse statement and `text-success` on the dashboard: three greens for one
          concept, on the screens where a colour that does not mean the same thing twice costs trust.

          ⚠️ `loading` is NOT passed to `Stat`: the three states here are loading / failed / a real amount
          (AC-P3.28), and `Stat`'s skeleton knows only the first two of those apart. `RevenueValue` keeps
          « Indisponible » distinct from « — », which is the whole point of that component.
        */}
        <StatStrip>
          <Stat
            label="Total facturé"
            value={<RevenueValue loading={revenueLoading} failed={!!revenueError} value={revenue?.totalInvoiced} />}
          />
          <Stat
            label="Total encaissé"
            tone="positive"
            /*
             * ⚠️ The hint is load-bearing arithmetic, not decoration.
             *
             * This figure deliberately counts BOTH money tracks — invoice payments and devis instalments, net of
             * avoirs — exactly as la caisse and the dashboard KPI do, while the table below lists the invoice
             * ledger only. So a practice adding up the « Encaissé » column comes out short by the instalment
             * total (measured: 30 655,000 DT here vs 29 665,000 DT down the column — the 950,000 DT of
             * `InstallmentPayments`). Nothing on the screen said so, which made a correct figure read as a wrong
             * one. « Total facturé » and « Reste à recouvrer » DO match the rows to the millime.
             */
            hint="paiements de notes et échéances de devis"
            value={<RevenueValue loading={revenueLoading} failed={!!revenueError} value={revenue?.totalCollected} />}
          />
          <Stat
            label="Reste à recouvrer"
            tone="active"
            value={<RevenueValue loading={revenueLoading} failed={!!revenueError} value={revenue?.outstanding} />}
          />
        </StatStrip>

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
