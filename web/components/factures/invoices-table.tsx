"use client"

import { useState, useEffect, useCallback } from "react"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { usePagedList } from "@/lib/hooks/use-paged-list"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { FileDown, CreditCard, Plus, Loader2, ReceiptText, MoreHorizontal, SearchX } from "lucide-react"
import { SendDocumentEmailDialog } from "@/components/send-document-email-dialog"
import { DOCUMENT_EMAIL_KINDS } from "@/lib/api/document-emails"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import Link from "next/link"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto } from "@/lib/api/types"
import { formatAmount, formatDT, formatDateFr, parseAmountInput, todayLocalIso } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { InvoiceFormModal } from "./invoice-form-modal"
import { PaymentModal } from "./payment-modal"
import { InvoiceDetailModal } from "./invoice-detail-modal"
import {
  invoiceStatusLabel, invoiceStatusBadgeClass,
  paymentMethodLabel, PAYMENT_METHODS,
} from "./invoice-labels"

/** « Factures » is the Finances zone — an empty state here wears the hue the rail and the eyebrow already do. */
const MONEY_CHIP = zoneChipClass(ZONES.money)

/** How many placeholder rows the desktop table shows while the first page loads. */
const SKELETON_ROWS = 6

interface InvoicesTableProps {
  patientId?: string
  patientName?: string
  from?: string
  to?: string
  status?: string
  /**
   * L9 — only the notes attributed to this practitioner. ⚠️ An **unattributed** note is excluded, not silently
   * included, so a practice that has just upgraded sees fewer rows under a filter than it expects — that is the
   * truth about its historical data rather than a bug.
   */
  doctorId?: string
  showPatientColumn?: boolean
  /** Bumped by the parent (e.g. after filter change) to force a reload. */
  reloadKey?: number
  /** Called after any mutation so the parent can refresh dependent views (e.g. revenue totals). */
  onChanged?: () => void
}

export function InvoicesTable({
  
  doctorId,patientId,
  patientName,
  from,
  to,
  status,
  showPatientColumn = true,
  reloadKey = 0,
  onChanged,
}: InvoicesTableProps) {
  const [search, setSearch] = useState("")
  // Bumped by a mutation or a realtime event to refetch the CURRENT page (rather than reset to page 1 — a
  // colleague recording a payment should not move the page you are reading).
  const [localRefresh, setLocalRefresh] = useState(0)
  const [busyId, setBusyId] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<InvoiceDto | null>(null)
  const [paymentTarget, setPaymentTarget] = useState<InvoiceDto | null>(null)
  // The invoice detail modal — the app's first invoice detail surface, and the only place a specific
  // payment can be voided.
  const [detailInvoiceId, setDetailInvoiceId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<InvoiceDto | null>(null)
  const [cancelTarget, setCancelTarget] = useState<InvoiceDto | null>(null)
  // Which note d'honoraires « Envoyer par email » was clicked for (a draft has no PDF, so it is never offered).
  const [emailTarget, setEmailTarget] = useState<InvoiceDto | null>(null)
  const [cancelReason, setCancelReason] = useState("")
  /*
   * Each destructive dialog keeps its OWN refusal, inline and persistent.
   *
   * The three used to report failure through a toast while the `finally` closed the dialog and wiped the typed
   * motif — so a refused annulation left the user with four seconds of red text and an empty form to retype from
   * memory. `invoice-detail-modal.tsx` already got this right for voiding a payment; these follow it.
   */
  const [cancelError, setCancelError] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [avoirError, setAvoirError] = useState<string | null>(null)
  // Avoir (credit note) modal state (finding #8).
  const [avoirTarget, setAvoirTarget] = useState<InvoiceDto | null>(null)
  const [avoirMethod, setAvoirMethod] = useState<string>("Cash")
  const [avoirRefundedOn, setAvoirRefundedOn] = useState<string>("")
  const [avoirAmount, setAvoirAmount] = useState("")
  const [avoirReason, setAvoirReason] = useState("")

  // `search` matches the invoice number AND the patient's name, server-side across the whole clinic. The
  // patient half has to be server-side: the names on these rows are resolved by a batched lookup after the page
  // is cut, so a filter here would only ever see the page already on screen.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      invoicesApi.listPaged({ page, pageSize, search, patientId, from, to, status, doctorId }),
    [patientId, from, to, status, doctorId],
  )

  const {
    items: invoices,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<InvoiceDto>({
    fetchPage,
    search,
    refreshKey: `${reloadKey}:${localRefresh}`,
  })

  const load = useCallback(() => setLocalRefresh((n) => n + 1), [])

  useClinicRealtime(RealtimeResource.Invoices, load)

  const afterMutation = () => {
    load()
    onChanged?.()
  }

  const openAvoir = (invoice: InvoiceDto) => {
    setAvoirTarget(invoice)
    setAvoirAmount("")
    setAvoirReason("")
    setAvoirMethod("Cash")
    setAvoirError(null)
    // Today, in the browser's own calendar. The API rejects an absent or future date, and the previous
    // dialog sent neither date nor method — so every avoir was stamped "now" with no recorded means of
    // refund, and its PDF had nothing to print.
    setAvoirRefundedOn(todayLocalIso())
  }

  /*
   * The avoir's client-side gate, mirroring the server's.
   *
   * ⚠️ It exists because « Établir l'avoir » was disabled on `busyId` alone: an avoir could be submitted with an
   * empty amount AND an empty motif, and only the server refused it — while the « Annuler la facture » dialog
   * forty lines below blocked on `!cancelReason.trim()`. Two adjacent money-reversal flows, opposite rules.
   */
  const avoirAmountValue = parseAmountInput(avoirAmount)
  const avoirAmountIsNumber = Number.isFinite(avoirAmountValue)
  const avoirExceedsCollected =
    avoirTarget !== null && avoirAmountIsNumber && avoirAmountValue > avoirTarget.amountCollected
  const avoirIsValid =
    avoirAmountIsNumber && avoirAmountValue > 0 && !avoirExceedsCollected && avoirReason.trim().length > 0

  const confirmAvoir = async () => {
    if (!avoirTarget || !avoirIsValid) return
    setBusyId(avoirTarget.id)
    setAvoirError(null)
    try {
      const created = await invoicesApi.createAvoir(avoirTarget.id, {
        amount: avoirAmountValue,
        reason: avoirReason.trim(),
        method: avoirMethod,
        refundedOn: avoirRefundedOn,
      })
      toast.success(`Avoir ${created.number} établi`)
      setAvoirTarget(null)
      afterMutation()
    } catch (err) {
      // The dialog stays open with the motif intact — retyping a justification is the last thing a user
      // should have to do after being refused.
      setAvoirError(err instanceof ApiError ? err.message : "Échec de l'établissement de l'avoir.")
    } finally {
      setBusyId(null)
    }
  }

  const handleIssue = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const issued = await invoicesApi.issue(invoice.id)
      // A devis-born invoice carries its plan's already-collected money across at issue. Naming the amount
      // explains why the note is not showing its full total as owing.
      toast.success(
        issued.amountCollected > 0
          ? `Facture émise — ${formatDT(issued.amountCollected)} reporté depuis le devis`
          : "Facture émise",
      )
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'émission.")
    } finally {
      setBusyId(null)
    }
  }

  // P1-D: the common "paid on the spot" case in one action — issue the draft, then open the payment
  // modal (prefilled with the full outstanding) on the freshly-issued invoice.
  const handleIssueAndPay = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const issued = await invoicesApi.issue(invoice.id)
      toast.success(
        issued.amountCollected > 0
          ? `Facture émise — ${formatDT(issued.amountCollected)} reporté depuis le devis`
          : "Facture émise",
      )
      afterMutation()
      // Prefilled from the POST-carry-over invoice, so the modal offers the real remaining balance. Using the
      // pre-issue snapshot here would ask the dentist to collect the full amount a second time.
      setPaymentTarget(issued)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'émission.")
    } finally {
      setBusyId(null)
    }
  }

  const handleDownloadPdf = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const blob = await invoicesApi.downloadPdf(invoice.id)
      // AC-4. The `<a download>` this replaced never delivered on iOS Safari, so a dentist on an iPhone could not
      // get a note d'honoraires out of the app at all.
      await downloadBlob(blob, `note-honoraires-${invoice.number ?? invoice.id}.pdf`)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du PDF.")
    } finally {
      setBusyId(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setBusyId(deleteTarget.id)
    setDeleteError(null)
    try {
      await invoicesApi.delete(deleteTarget.id)
      toast.success("Brouillon supprimé")
      // Closing belongs to the SUCCESS path. In `finally` it closed on refusal too, which is how a server
      // « ce brouillon n'existe plus » became a dialog that simply vanished.
      setDeleteTarget(null)
      afterMutation()
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : "Échec de la suppression.")
    } finally {
      setBusyId(null)
    }
  }

  const confirmCancel = async () => {
    if (!cancelTarget) return
    if (!cancelReason.trim()) {
      setCancelError("Le motif d'annulation est requis.")
      return
    }
    setBusyId(cancelTarget.id)
    setCancelError(null)
    try {
      await invoicesApi.cancel(cancelTarget.id, cancelReason.trim())
      toast.success("Facture annulée")
      setCancelTarget(null)
      setCancelReason("")
      afterMutation()
    } catch (err) {
      // Dialog stays open, motif intact: it is a required justification the user has just composed.
      setCancelError(err instanceof ApiError ? err.message : "Échec de l'annulation.")
    } finally {
      setBusyId(null)
    }
  }

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (invoice: InvoiceDto) => {
    setEditing(invoice)
    setFormOpen(true)
  }

  const colSpan = showPatientColumn ? 9 : 8

  /*
   * ONE actions menu, rendered by both halves of the responsive pair.
   *
   * The desktop row used to carry up to **eleven** `size="icon"` ghost buttons labelled only by a `title=`
   * tooltip — which does not exist on the tablet this app is used on, so the actions column was a row of
   * indistinguishable grey glyphs, and only one of them had an `aria-label`. The mobile card already had the
   * right answer; extracting it means the two can never offer different actions for the same invoice, which a
   * second hand-maintained copy of eleven gates guarantees eventually.
   */
  const renderActions = (inv: InvoiceDto) => {
    const isBusy = busyId === inv.id
    const isDraft = inv.status === "Draft"
    const isPayable = inv.status === "Issued" || inv.status === "PartiallyPaid"
    const label = inv.number ? `la facture ${inv.number}` : "le brouillon"
    return (
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon" disabled={isBusy} aria-label={`Actions pour ${label}`}>
            {isBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <MoreHorizontal className="h-4 w-4" />}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onSelect={() => setDetailInvoiceId(inv.id)}>Voir le détail</DropdownMenuItem>
          {isDraft && (
            <>
              <DropdownMenuItem onSelect={() => openEdit(inv)}>Modifier</DropdownMenuItem>
              <DropdownMenuItem onSelect={() => handleIssue(inv)}>Émettre</DropdownMenuItem>
              <DropdownMenuItem onSelect={() => handleIssueAndPay(inv)}>Émettre et encaisser</DropdownMenuItem>
            </>
          )}
          {isPayable && (
            <DropdownMenuItem onSelect={() => setPaymentTarget(inv)}>Enregistrer un paiement</DropdownMenuItem>
          )}
          {inv.canCreateAvoir && (
            <DropdownMenuItem onSelect={() => openAvoir(inv)}>Établir un avoir</DropdownMenuItem>
          )}
          {!isDraft && (
            <DropdownMenuItem onSelect={() => handleDownloadPdf(inv)}>Télécharger le PDF</DropdownMenuItem>
          )}
          {!isDraft && (
            <DropdownMenuItem onSelect={() => setEmailTarget(inv)}>Envoyer par email</DropdownMenuItem>
          )}
          {(isDraft || inv.canCancel) && <DropdownMenuSeparator />}
          {isDraft && (
            <DropdownMenuItem
              className="text-destructive focus:text-destructive"
              onSelect={() => { setDeleteError(null); setDeleteTarget(inv) }}
            >
              Supprimer
            </DropdownMenuItem>
          )}
          {inv.canCancel && (
            <DropdownMenuItem
              className="text-destructive focus:text-destructive"
              onSelect={() => { setCancelError(null); setCancelTarget(inv) }}
            >
              Annuler
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
    )
  }

  /*
   * Three emptinesses, not two — and only the last may offer « Nouvelle facture ».
   *
   * A search that matched nothing and a *date/statut* filter that matched nothing are both cases where the note
   * probably exists and the question was simply too narrow; an « Ajouter » button there is an invitation to
   * raise a duplicate note d'honoraires, which consumes a gapless fiscal number.
   */
  const hasParentFilter = Boolean(from || to || status)
  const emptyState = isSearching ? (
    <EmptyState
      size="compact"
      icon={SearchX}
      chipClassName={MONEY_CHIP}
      title="Aucune facture ne correspond à votre recherche"
      description="La recherche porte sur le numéro et sur le nom du patient, dans toute la période filtrée."
      secondaryAction={
        <Button size="sm" variant="outline" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      }
    />
  ) : hasParentFilter ? (
    <EmptyState
      size="compact"
      icon={SearchX}
      chipClassName={MONEY_CHIP}
      title="Aucune facture sur cette période"
      description="Aucune note d'honoraires ne correspond aux filtres de date et de statut choisis ci-dessus."
    />
  ) : (
    <EmptyState
      size="compact"
      icon={ReceiptText}
      chipClassName={MONEY_CHIP}
      title={patientName ? `Aucune facture pour ${patientName}` : "Aucune facture"}
      description="Les notes d'honoraires apparaîtront ici, du brouillon à l'encaissement."
      action={
        <Button size="sm" onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" />
          Nouvelle facture
        </Button>
      }
    />
  )

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <Button onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" /> Nouvelle facture
        </Button>
      </div>

      {/* The shared banner, on `--destructive-wash` — not a fifth hand-maintained `red-50 / dark:red-950` pair. */}
      <FormErrorBanner message={error} />

      <div>
        <Label htmlFor="invoices-search" className="sr-only">
          Rechercher une facture
        </Label>
        <Input
          id="invoices-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Rechercher une facture (numéro, patient)…"
        />
      </div>

      <div className={`rounded-md border overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
        {/*
          ⚠️ A draft has NO number — the table renders « — », which as a card title would identify nothing. The
          title falls back to the patient and the date, which is what a draft actually is: someone's unbilled
          work on a day. The nine icon buttons collapse into one menu; every gate stays exactly as the row had
          it, including the two that come from the SERVER (`canCancel`, `canCreateAvoir`) rather than being
          re-derived here.
        */}
        <CardList
          className={CARDS_ONLY}
          ariaLabel="Notes d'honoraires"
          items={invoices}
          getKey={(inv) => inv.id}
          loading={loading}
          muted={(inv) => inv.status === "Cancelled"}
          title={(inv) =>
            inv.number ?? `${inv.patientName ?? "Brouillon"} · ${formatDateFr(inv.createdAt)}`
          }
          subtitle={(inv) => (showPatientColumn && inv.number ? inv.patientName : null)}
          onSelect={(inv) => setDetailInvoiceId(inv.id)}
          status={(inv) => (
            <>
              <Badge variant="secondary" className={invoiceStatusBadgeClass(inv.status)}>
                {invoiceStatusLabel(inv.status)}
              </Badge>
              {inv.treatmentPlanId && (
                <Badge variant="outline" className="whitespace-nowrap">
                  Devis
                </Badge>
              )}
            </>
          )}
          fields={(inv) => [
            { label: "Total TTC", value: `${formatAmount(inv.totalTtc)} DT` },
            {
              label: "Encaissé",
              value: (
                <span className="inline-flex flex-col items-end">
                  <span>{formatAmount(inv.amountCollected)} DT</span>
                  {inv.creditedTotal > 0 && (
                    <span className="text-xs text-primary">−{formatAmount(inv.creditedTotal)} avoir</span>
                  )}
                </span>
              ),
            },
            { label: "Reste", value: `${formatAmount(inv.outstanding)} DT` },
            {
              label: "Date",
              value: inv.issueDate ? formatDateFr(inv.issueDate) : formatDateFr(inv.createdAt),
            },
          ]}
          actions={renderActions}
          empty={emptyState}
        />
        <Table containerClassName={TABLE_ONLY}>
          {/* Sticky: this list pages, so the columns are gone by row ten and « Reste » becomes an unlabelled
              column of money. The unit is stated once in the three money headers rather than on every cell. */}
          <TableHeader sticky>
            <TableRow>
              <TableHead>Numéro</TableHead>
              {showPatientColumn && <TableHead>Patient</TableHead>}
              <TableHead>Date</TableHead>
              <TableHead>Statut</TableHead>
              <TableHead className="text-right">Total TTC (DT)</TableHead>
              <TableHead className="text-right">Encaissé (DT)</TableHead>
              <TableHead className="text-right">Reste (DT)</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              /* Skeleton ROWS, not a one-line « Chargement... » that a 25-row table then shoves off the screen.
                 The mobile CardList beside this already renders skeletons; the desktop half jumped. */
              Array.from({ length: SKELETON_ROWS }).map((_, rowIndex) => (
                <TableRow key={`skeleton-${rowIndex}`} aria-hidden="true">
                  {Array.from({ length: colSpan }).map((__, cellIndex) => (
                    <TableCell key={cellIndex}>
                      <span className="block h-4 animate-pulse rounded bg-muted" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : invoices.length === 0 ? (
              <TableRow>
                {/* `p-0`: the empty state owns its own vertical rhythm, and the cell's padding on top of it
                    would push the « Nouvelle facture » action a screen down. */}
                <TableCell colSpan={colSpan} className="p-0">
                  {emptyState}
                </TableCell>
              </TableRow>
            ) : (
              invoices.map((invoice) => {
                const isBusy = busyId === invoice.id
                const isDraft = invoice.status === "Draft"
                const isPayable = invoice.status === "Issued" || invoice.status === "PartiallyPaid"
                return (
                  // A cancelled note dims: the badge already says « Annulée », and a row rendering in
                  // full-strength ink beside it made the least relevant line as loud as the rest.
                  <TableRow key={invoice.id} muted={invoice.status === "Cancelled"}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2">
                        {/* The number is the detail affordance. It was inert text, and the row already
                            carries up to eight icon buttons — a ninth would not have been readable. This
                            also gives draft rows (« — ») a target. */}
                        <button
                          type="button"
                          className="underline-offset-2 hover:underline"
                          title="Voir le détail"
                          onClick={(e) => { e.stopPropagation(); setDetailInvoiceId(invoice.id) }}
                        >
                          {invoice.number ?? "—"}
                        </button>
                        {/* A devis-born note is otherwise indistinguishable here from a standalone one, and
                            the devis→facture link was write-only until now. Mirrors the plans table's
                            « Facturé — N° » badge, closing the loop from both ends. */}
                        {invoice.treatmentPlanId && (
                          <Link
                            href={`/treatment-plans/${invoice.treatmentPlanId}`}
                            title="Voir le devis d'origine"
                          >
                            <Badge variant="outline" className="whitespace-nowrap hover:bg-accent">
                              Devis
                            </Badge>
                          </Link>
                        )}
                      </div>
                    </TableCell>
                    {showPatientColumn && <TableCell>{invoice.patientName ?? "—"}</TableCell>}
                    <TableCell>{invoice.issueDate ? formatDateFr(invoice.issueDate) : formatDateFr(invoice.createdAt)}</TableCell>
                    <TableCell>
                      <Badge variant="secondary" className={invoiceStatusBadgeClass(invoice.status)}>
                        {invoiceStatusLabel(invoice.status)}
                      </Badge>
                    </TableCell>
                    <TableCell numeric>{formatAmount(invoice.totalTtc)}</TableCell>
                    <TableCell numeric>
                      <div className="flex flex-col items-end">
                        <span>{formatAmount(invoice.amountCollected)}</span>
                        {/* An avoir was invisible everywhere once established. The row is where a user
                            notices that money went back — and why « Encaissé » no longer matches the caisse. */}
                        {invoice.creditedTotal > 0 && (
                          <span
                            className="text-xs text-primary"
                            title="Montant remboursé au patient par avoir"
                          >
                            −{formatAmount(invoice.creditedTotal)} avoir
                          </span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell numeric>{formatAmount(invoice.outstanding)}</TableCell>
                    <TableCell>
                      {/*
                        Two shortcuts and a menu, not eleven glyphs. « Enregistrer un paiement » and
                        « Télécharger le PDF » are the two a receptionist performs all day, so they keep a
                        one-tap button — each with a real `aria-label` naming the invoice, because a `title=`
                        tooltip does not exist under a finger. Everything else lives in the same labelled menu
                        the phone card uses, so there is exactly one list of what an invoice can do.
                      */}
                      <div className="flex items-center justify-end gap-1">
                        {isPayable && (
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Enregistrer un paiement"
                            aria-label={`Enregistrer un paiement sur la facture ${invoice.number ?? "brouillon"}`}
                            onClick={() => setPaymentTarget(invoice)}
                            disabled={isBusy}
                          >
                            <CreditCard className="h-4 w-4" />
                          </Button>
                        )}
                        {!isDraft && (
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Télécharger le PDF"
                            aria-label={`Télécharger le PDF de la facture ${invoice.number ?? ""}`.trim()}
                            onClick={() => handleDownloadPdf(invoice)}
                            disabled={isBusy}
                          >
                            <FileDown className="h-4 w-4" />
                          </Button>
                        )}
                        {renderActions(invoice)}
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
        <DataTablePagination
          page={pageInfo}
          onPageChange={setPage}
          onPageSizeChange={setPageSize}
          loading={refreshing}
          label={["facture", "factures"]}
        />
      </div>

      <InvoiceFormModal
        open={formOpen}
        onOpenChange={setFormOpen}
        editingInvoice={editing}
        presetPatientId={patientId}
        presetPatientName={patientName}
        onSuccess={afterMutation}
      />

      <PaymentModal
        open={!!paymentTarget}
        onOpenChange={(open) => !open && setPaymentTarget(null)}
        invoice={paymentTarget}
        onSuccess={afterMutation}
      />

      <InvoiceDetailModal
        open={!!detailInvoiceId}
        onOpenChange={(open) => !open && setDetailInvoiceId(null)}
        invoiceId={detailInvoiceId}
        onChanged={afterMutation}
      />

      <AlertDialog
        open={!!deleteTarget}
        onOpenChange={(open) => { if (!open) { setDeleteTarget(null); setDeleteError(null) } }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce brouillon ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cette action est irréversible. Seuls les brouillons peuvent être supprimés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <FormErrorBanner message={deleteError} />
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busyId === deleteTarget?.id}>Annuler</AlertDialogCancel>
            {/* `preventDefault` because `AlertDialogAction` closes the dialog on click: without it a refusal
                dismisses the surface the refusal would have been shown on. */}
            <AlertDialogAction
              onClick={(e) => { e.preventDefault(); void confirmDelete() }}
              disabled={busyId === deleteTarget?.id}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {busyId === deleteTarget?.id ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {emailTarget && (
        <SendDocumentEmailDialog
          open={Boolean(emailTarget)}
          onOpenChange={(next) => { if (!next) setEmailTarget(null) }}
          documentKind={DOCUMENT_EMAIL_KINDS.Invoice}
          documentId={emailTarget.id}
          documentLabel={`Note d'honoraires ${emailTarget.number ?? ""}`.trim()}
          patientId={emailTarget.patientId}
        />
      )}

      <Dialog
        open={!!cancelTarget}
        onOpenChange={(open) => { if (!open) { setCancelTarget(null); setCancelReason(""); setCancelError(null) } }}
      >
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler la facture</DialogTitle>
            <DialogDescription>
              {cancelTarget?.number ? `Facture ${cancelTarget.number}` : "Facture"} — le numéro est conservé. Un motif est requis.
            </DialogDescription>
          </DialogHeader>
          <FormErrorBanner message={cancelError} />
          <div className="space-y-1.5">
            {/* AC-P3.41 — a real <Label htmlFor>, as the avoir dialog forty lines below already does. The
                placeholder was the only thing naming this required field, and a placeholder disappears the
                moment you type and is not an accessible name. */}
            <Label htmlFor="cancelReason">Motif d&apos;annulation</Label>
            <Textarea
              id="cancelReason"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Erreur de saisie, acte non réalisé…"
              rows={3}
              required
            />
          </div>
          <DialogFooter className="gap-2">
            <Button
              variant="outline"
              onClick={() => { setCancelTarget(null); setCancelReason(""); setCancelError(null) }}
              disabled={busyId === cancelTarget?.id}
            >
              Retour
            </Button>
            <Button
              onClick={confirmCancel}
              disabled={busyId === cancelTarget?.id || !cancelReason.trim()}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {busyId === cancelTarget?.id ? "Annulation…" : "Confirmer l'annulation"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!avoirTarget} onOpenChange={(open) => { if (!open) { setAvoirTarget(null); setAvoirError(null) } }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Établir un avoir</DialogTitle>
            <DialogDescription>
              {avoirTarget?.number ? `Facture ${avoirTarget.number}` : "Facture"} — encaissé {avoirTarget ? formatDT(avoirTarget.amountCollected) : ""}. L'avoir crédite tout ou partie du montant encaissé.
            </DialogDescription>
          </DialogHeader>
          <FormErrorBanner message={avoirError} />
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label htmlFor="avoirAmount">
                Montant (DT) <span className="text-destructive">*</span>
              </Label>
              {/* `type="text" inputMode="decimal"`, deliberately — see `parseAmountInput`. A `type="number"`
                  field silently discards the comma this product prints everywhere, returning "" for a value
                  the user can see in the box. `inputMode` still brings up the numeric keypad on a tablet. */}
              <Input
                id="avoirAmount"
                type="text"
                inputMode="decimal"
                value={avoirAmount}
                onChange={(e) => setAvoirAmount(e.target.value)}
                placeholder="0,000"
                aria-invalid={avoirExceedsCollected || undefined}
              />
              {avoirExceedsCollected && avoirTarget && (
                <p className="text-xs text-destructive">
                  Le montant ne peut pas dépasser {formatDT(avoirTarget.amountCollected)} encaissés.
                </p>
              )}
            </div>
            {/* `grid-cols-1` base: at 390 px this dialog is ~358 px wide, so two columns give each field 165 px —
                enough for « Mode » and not for « Date du remboursement » plus a native date picker's glyph. */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="avoirRefundedOn">Date du remboursement</Label>
                <Input
                  id="avoirRefundedOn"
                  type="date"
                  value={avoirRefundedOn}
                  onChange={(e) => setAvoirRefundedOn(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="avoirMethod">Mode</Label>
                <Select value={avoirMethod} onValueChange={setAvoirMethod}>
                  {/* `w-full`: `ui/select.tsx` ships `w-fit`, so the trigger otherwise renders narrower than
                      the date field beside it in the same grid cell pair. */}
                  <SelectTrigger id="avoirMethod" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PAYMENT_METHODS.map((method) => (
                      <SelectItem key={method} value={method}>
                        {paymentMethodLabel(method)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="avoirReason">
                Motif <span className="text-destructive">*</span>
              </Label>
              <Textarea
                id="avoirReason"
                value={avoirReason}
                onChange={(e) => setAvoirReason(e.target.value)}
                placeholder="Motif de l'avoir"
                rows={3}
                required
              />
            </div>
          </div>
          <DialogFooter className="gap-2">
            <Button
              variant="outline"
              onClick={() => { setAvoirTarget(null); setAvoirError(null) }}
              disabled={busyId === avoirTarget?.id}
            >
              Retour
            </Button>
            {/* Blocked until the amount AND the motif are real — the same rule « Annuler la facture » above
                already enforced. An avoir is a numbered fiscal document; it cannot be issued blank. */}
            <Button onClick={confirmAvoir} disabled={busyId === avoirTarget?.id || !avoirIsValid}>
              {busyId === avoirTarget?.id ? "Établissement…" : "Établir l'avoir"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
