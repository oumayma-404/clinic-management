"use client"

import { useState, useEffect, useCallback } from "react"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { usePagedList } from "@/lib/hooks/use-paged-list"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { MoreHorizontal, Plus, Loader2, ChevronRight, ClipboardList } from "lucide-react"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { ZONES, zoneChipClass } from "@/lib/zones"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { planStatusLabel, planStatusBadgeClass } from "./treatment-plan-labels"
import { isPlanBilled } from "./plan-next-action"

interface TreatmentPlansTableProps {
  patientId?: string
  patientName?: string
  status?: string
  /** Creation-date bounds. */
  from?: string
  to?: string
  /**
   * ACCEPTANCE-date bounds — a different date from {@link from}/{@link to}. Set by the dashboard's « Devis acceptés »
   * drill-through, which counts by `acceptedDate`.
   */
  acceptedFrom?: string
  acceptedTo?: string
  /**
   * True when the parent is narrowing the list (a date window, a statut) — so an empty result is « nothing
   * matching », not « nothing yet ».
   *
   * <p>Load-bearing since the plans page opens on the current week: without it, a clinic with three hundred devis
   * and a quiet Monday gets the first-run invite (« Aucun plan de traitement » + « Nouveau plan »), which asserts
   * something false about its records and invites a duplicate.</p>
   */
  filtered?: boolean
  /** Clears the parent's filters entirely — the action the filtered empty state offers. */
  onClearFilters?: () => void
  showPatientColumn?: boolean
  /** Bumped by the parent (e.g. after filter change) to force a reload. */
  reloadKey?: number
  /** Called after any mutation so the parent can refresh dependent views. */
  onChanged?: () => void
}

/**
 * The plans list — a **list**, not a detail view. Each row opens `/treatment-plans/[id]`, the workspace that
 * owns the acts, the échéancier and the parcours.
 *
 * The old "Gérer" dialog and its row of eight unlabelled ghost icons are gone: the dialog was the only place
 * a plan's contents were visible, and it offered every action on every row regardless of état — which is how
 * the same act could be booked twice. What remains here is what a list legitimately does: create, and a small
 * labelled menu for the row-level operations that don't need the plan's context.
 */
export function TreatmentPlansTable({
  patientId,
  patientName,
  status,
  from,
  to,
  acceptedFrom,
  acceptedTo,
  filtered = false,
  onClearFilters,
  showPatientColumn = true,
  reloadKey = 0,
  onChanged,
}: TreatmentPlansTableProps) {
  const [search, setSearch] = useState("")
  // Bumped by a mutation or a realtime event to refetch the CURRENT page.
  const [localRefresh, setLocalRefresh] = useState(0)
  const [busyId, setBusyId] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<TreatmentPlanDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TreatmentPlanDto | null>(null)
  /** A refusal from the delete call, shown *inside* the dialog rather than replacing it with a toast. */
  const [deleteError, setDeleteError] = useState<string | null>(null)

  const router = useRouter()

  // `search` matches the devis number, title, notes and the patient's name — server-side, whole clinic.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      treatmentPlansApi.listPaged({
        page,
        pageSize,
        search,
        patientId,
        status,
        from,
        to,
        acceptedFrom,
        acceptedTo,
      }),
    [patientId, status, from, to, acceptedFrom, acceptedTo],
  )

  const {
    items: plans,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<TreatmentPlanDto>({
    fetchPage,
    search,
    refreshKey: `${reloadKey}:${localRefresh}`,
  })

  const load = useCallback(() => setLocalRefresh((n) => n + 1), [])

  // Three keys, not one: an act's état is derived from Appointment rows and the « Facturé » badge from Invoice
  // rows, and RealtimeBroadcastBehavior keys off the *command's* namespace — so cancelling an appointment
  // broadcasts "appointments", never "treatmentplans".
  useClinicRealtime(
    [RealtimeResource.TreatmentPlans, RealtimeResource.Appointments, RealtimeResource.Invoices],
    load,
  )

  const afterMutation = () => {
    load()
    onChanged?.()
  }

  const openWorkspace = (plan: TreatmentPlanDto) => router.push(`/treatment-plans/${plan.id}`)

  const handleDownloadPdf = async (plan: TreatmentPlanDto) => {
    setBusyId(plan.id)
    try {
      const blob = await treatmentPlansApi.downloadDevisPdf(plan.id)
      downloadBlob(blob, `devis-${plan.number ?? plan.id}.pdf`)
    } catch (err) {
      showErrorToast(err, "Échec du téléchargement du devis.", () => handleDownloadPdf(plan))
    } finally {
      setBusyId(null)
    }
  }

  /**
   * ⚠️ The dialog is dismissed **only on success**.
   *
   * <p>The reset used to live in `finally`, so a refused delete closed the confirmation anyway and the reason
   * survived for four seconds in a toast, detached from the row it was about. On a list that can run to several
   * pages, the user is then hunting for a devis they can no longer name to re-read a message that has already
   * gone. Keeping the dialog open with an inline `FormErrorBanner` means the refusal stays beside the thing it
   * refuses, and « Supprimer » is one click away once the cause is understood — the same rule the dialogs that
   * carry a form already follow.</p>
   */
  const confirmDelete = async () => {
    if (!deleteTarget) return
    setBusyId(deleteTarget.id)
    setDeleteError(null)
    try {
      await treatmentPlansApi.remove(deleteTarget.id)
      toast.success("Brouillon supprimé")
      setDeleteTarget(null)
      afterMutation()
    } catch (err) {
      setDeleteError(getErrorMessage(err, "Échec de la suppression."))
    } finally {
      setBusyId(null)
    }
  }

  const openDelete = (plan: TreatmentPlanDto) => {
    setDeleteError(null)
    setDeleteTarget(plan)
  }

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (plan: TreatmentPlanDto) => {
    setEditing(plan)
    setFormOpen(true)
  }

  const colSpan = showPatientColumn ? 8 : 7

  /*
   * Whose brouillon is being deleted. The row's own `patientName` first; the `patientName` prop is the fallback
   * for the patient page, where the column is hidden and the DTO's copy may not be populated. Null when neither
   * is known, and the title then stays generic rather than printing « le brouillon de undefined ».
   */
  const deleteTargetPatient = deleteTarget?.patientName ?? patientName ?? null

  /*
   * One empty state, rendered by both halves of the surface (the card list below `md:` and the table above it),
   * so the two can never drift into saying different things about the same nothing.
   *
   * The three cases stay distinct, which is the whole point of the primitive: « rien pour cette recherche » and
   * « rien sur cette période » both offer a way to widen and **never** « Nouveau plan » — the devis may well
   * exist and the user simply mistyped or is looking at the wrong week, and a create button there is an
   * invitation to type a duplicate. « Aucun plan » is the genuine first-run state, and that is where the
   * invitation belongs. The plans list is a Finances screen, so the icon chip takes the money zone's hue — the
   * same colour the rail and the page eyebrow already use for it.
   *
   * ⚠️ `searchTerm` is read separately from `isSearching`, which tracks the **debounced** term: for a few
   * hundred ms after the box is cleared `isSearching` is still true while `search` is already empty, and
   * quoting it unguarded would flash « Aucun devis pour «  » ».
   */
  const searchTerm = search.trim()
  const emptyState = isSearching ? (
    <EmptyState
      icon={ClipboardList}
      size="compact"
      chipClassName={zoneChipClass(ZONES.money)}
      title={searchTerm ? `Aucun devis pour « ${searchTerm} »` : "Aucun devis ne correspond à votre recherche"}
      description="Vérifiez l'orthographe, ou effacez la recherche pour revoir tous les devis."
      action={
        <Button variant="outline" size="sm" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      }
    />
  ) : filtered ? (
    <EmptyState
      icon={ClipboardList}
      size="compact"
      chipClassName={zoneChipClass(ZONES.money)}
      title="Aucun devis pour ces filtres"
      description="Aucun devis n'a été créé sur la période ou avec le statut sélectionné. Élargissez la période pour revoir les autres."
      action={
        onClearFilters ? (
          <Button variant="outline" size="sm" onClick={onClearFilters}>
            Voir tous les devis
          </Button>
        ) : undefined
      }
    />
  ) : (
    <EmptyState
      icon={ClipboardList}
      size="compact"
      chipClassName={zoneChipClass(ZONES.money)}
      title="Aucun plan de traitement"
      description="Un devis chiffre les actes à venir, fixe l'échéancier de paiement et suit chaque acte jusqu'à sa fiche de soins."
      action={
        <Button size="sm" onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" /> Nouveau plan
        </Button>
      }
    />
  )

  return (
    <div className="space-y-3">
      {/*
        One toolbar row: the search grows, the action sits at the end. The two used to be separate stacked rows
        with the error banner between them, which cost a whole row of height and put a problem message in the
        middle of the controls.

        `flex-wrap` + `flex-1` rather than a fixed two-column grid, so below ~420px the button drops to its own
        line instead of squeezing the input down to a few characters.
      */}
      <div className="flex flex-wrap items-center gap-2">
        <Label htmlFor="plans-search" className="sr-only">
          Rechercher un devis
        </Label>
        <Input
          id="plans-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Rechercher un devis (numéro, titre, patient)…"
          className="min-w-[200px] flex-1 sm:max-w-sm"
        />
        <Button onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" /> Nouveau plan
        </Button>
      </div>

      {/* The shared primitive, not another hand-rolled `bg-red-50 … dark:bg-red-950` block. That block was one
          of ~18 copies of a banner that already exists, each maintaining dark mode by hand and none of them on
          `--destructive`, so the app's one red was the only colour not following the palette. */}
      <FormErrorBanner message={error} />

      <div className={`rounded-md border overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
        {/* This table already used a DropdownMenu for its row actions, so the card's menu is the same content —
            it is the template the other conversions followed rather than the other way round. */}
        <CardList
          className={CARDS_ONLY_LG}
          ariaLabel="Plans de traitement et devis"
          items={plans}
          getKey={(p) => p.id}
          title={(p) => p.number ?? p.title}
          subtitle={(p) => (showPatientColumn ? p.patientName : null)}
          onSelect={(p) => openWorkspace(p)}
          loading={loading}
          status={(p) => (
            <>
              <Badge variant="secondary" className={planStatusBadgeClass(p.status)}>
                {planStatusLabel(p.status)}
              </Badge>
              {isPlanBilled(p) && (
                <Badge variant="outline" className="whitespace-nowrap">
                  Facturé{p.linkedInvoiceNumber ? ` — ${p.linkedInvoiceNumber}` : ""}
                </Badge>
              )}
            </>
          )}
          fields={(p) => [
            { label: "Total", value: formatDT(p.totalPlanned) },
            { label: "Encaissé", value: formatDT(p.amountPaid) },
            { label: "Reste", value: formatDT(p.outstanding) },
            { label: "Avancement", value: p.itemsTotal > 0 ? `${p.itemsDone}/${p.itemsTotal} actes` : null },
            {
              label: "Prochaine séance",
              value: p.nextAppointmentAt ? formatDateFr(p.nextAppointmentAt) : null,
            },
          ]}
          actions={(p) => {
            const isDraft = p.status === "Draft"
            return (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon" disabled={busyId === p.id} aria-label="Actions du plan">
                    <MoreHorizontal className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onSelect={() => openWorkspace(p)}>Ouvrir le plan</DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => handleDownloadPdf(p)}>
                    Télécharger le devis (PDF)
                  </DropdownMenuItem>
                  {isDraft && (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem onSelect={() => openEdit(p)}>Modifier le brouillon</DropdownMenuItem>
                      <DropdownMenuItem
                        className="text-destructive focus:text-destructive"
                        onSelect={() => openDelete(p)}
                      >
                        Supprimer le brouillon
                      </DropdownMenuItem>
                    </>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            )
          }}
          empty={emptyState}
        />
        <Table containerClassName={TABLE_ONLY_LG}>
          <TableHeader>
            <TableRow>
              <TableHead>Numéro</TableHead>
              {showPatientColumn && <TableHead>Patient</TableHead>}
              <TableHead>Statut</TableHead>
              <TableHead>Avancement</TableHead>
              <TableHead className="text-right">Total</TableHead>
              <TableHead className="text-right">Encaissé</TableHead>
              <TableHead className="text-right">Reste</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={colSpan} className="text-center text-muted-foreground py-8">
                  Chargement...
                </TableCell>
              </TableRow>
            ) : plans.length === 0 ? (
              <TableRow>
                {/* `py-0`: `EmptyState` owns its own vertical rhythm, and the cell's `py-8` on top of it would
                    make the region ~64 px taller than the same state inside a card. */}
                <TableCell colSpan={colSpan} className="py-0">
                  {emptyState}
                </TableCell>
              </TableRow>
            ) : (
              plans.map((plan) => {
                const isBusy = busyId === plan.id
                const isDraft = plan.status === "Draft"
                return (
                  <TableRow
                    key={plan.id}
                    className="cursor-pointer"
                    onClick={() => openWorkspace(plan)}
                  >
                    <TableCell className="font-medium">
                      <span className="inline-flex items-center gap-1">
                        {plan.number ?? plan.title}
                        <ChevronRight className="h-4 w-4 text-muted-foreground" />
                      </span>
                    </TableCell>
                    {showPatientColumn && <TableCell>{plan.patientName ?? "—"}</TableCell>}
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
                          {planStatusLabel(plan.status)}
                        </Badge>
                        {isPlanBilled(plan) && (
                          <Badge variant="outline" className="whitespace-nowrap">
                            Facturé{plan.linkedInvoiceNumber ? ` — ${plan.linkedInvoiceNumber}` : ""}
                          </Badge>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-sm text-muted-foreground">
                      {plan.itemsTotal > 0 ? `${plan.itemsDone}/${plan.itemsTotal} actes` : "—"}
                      {plan.nextAppointmentAt && (
                        <span className="block text-xs">
                          Prochaine séance : {formatDateFr(plan.nextAppointmentAt)}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">{formatDT(plan.totalPlanned)}</TableCell>
                    <TableCell className="text-right">{formatDT(plan.amountPaid)}</TableCell>
                    <TableCell className="text-right">{formatDT(plan.outstanding)}</TableCell>
                    {/* stopPropagation so opening the menu doesn't also navigate the row. */}
                    <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                      <div className="flex items-center justify-end gap-1">
                        {isBusy && <Loader2 className="h-4 w-4 animate-spin" />}
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon" disabled={isBusy} aria-label="Actions du plan">
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuItem onSelect={() => openWorkspace(plan)}>
                              Ouvrir le plan
                            </DropdownMenuItem>
                            <DropdownMenuItem onSelect={() => handleDownloadPdf(plan)}>
                              Télécharger le devis (PDF)
                            </DropdownMenuItem>
                            {isDraft && (
                              <>
                                <DropdownMenuSeparator />
                                <DropdownMenuItem onSelect={() => openEdit(plan)}>
                                  Modifier le brouillon
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                  className="text-destructive focus:text-destructive"
                                  onSelect={() => openDelete(plan)}
                                >
                                  Supprimer le brouillon
                                </DropdownMenuItem>
                              </>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
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
          label={["devis", "devis"]}
        />
      </div>

      <TreatmentPlanFormModal
        open={formOpen}
        onOpenChange={setFormOpen}
        editingPlan={editing}
        presetPatientId={patientId}
        presetPatientName={patientName}
        onSuccess={afterMutation}
      />

      {/*
        ⚠️ The confirmation **names the devis it is about to destroy**.

        It used to read « Supprimer ce brouillon ? / Cette action est irréversible. » — a sentence that is true
        of every row and therefore identifies none of them. The dialog is opened from a `⋯` menu on a list that
        pages, so by the time it appears the row is under an overlay and the only way to check which devis was
        clicked is to cancel and look again. Every fact needed to answer « is this the right one? » is already
        in scope: the patient, the creation date and the amount.

        « Aucun numéro n'a été consommé » is the reassurance that matters here and nowhere else in the plan
        area: a *draft* has no devis number, so deleting one leaves no gap in the sequence — which is exactly
        why deletion is offered for drafts and an avoir/cancellation for everything else.
      */}
      <AlertDialog
        open={!!deleteTarget}
        onOpenChange={(open) => {
          if (open || busyId === deleteTarget?.id) return
          setDeleteTarget(null)
          setDeleteError(null)
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {deleteTargetPatient
                ? `Supprimer le brouillon de ${deleteTargetPatient} ?`
                : "Supprimer ce brouillon ?"}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {deleteTarget && (
                <>
                  Le devis du {formatDateFr(deleteTarget.createdAt)} — {formatDT(deleteTarget.totalPlanned)} sera
                  supprimé. Aucun numéro n&apos;a été consommé ; cette action est irréversible.
                </>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>

          {/* The refusal lands here, inside the dialog that caused it — see `confirmDelete`. */}
          <FormErrorBanner message={deleteError} />

          <AlertDialogFooter>
            <AlertDialogCancel disabled={busyId === deleteTarget?.id}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                // Radix dismisses on click; prevented so a refusal can keep the dialog (and its banner) open.
                event.preventDefault()
                confirmDelete()
              }}
              disabled={busyId === deleteTarget?.id}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Supprimer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
