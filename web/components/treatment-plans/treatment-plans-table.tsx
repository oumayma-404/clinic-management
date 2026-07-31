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
import { MoreHorizontal, Plus, Loader2, ChevronRight } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
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
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du devis.")
    } finally {
      setBusyId(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setBusyId(deleteTarget.id)
    try {
      await treatmentPlansApi.remove(deleteTarget.id)
      toast.success("Brouillon supprimé")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression.")
    } finally {
      setBusyId(null)
      setDeleteTarget(null)
    }
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

      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
          {error}
        </div>
      )}

      <div className={`rounded-md border overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
        {/* This table already used a DropdownMenu for its row actions, so the card's menu is the same content —
            it is the template the other conversions followed rather than the other way round. */}
        <CardList
          className={CARDS_ONLY}
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
                        onSelect={() => setDeleteTarget(p)}
                      >
                        Supprimer le brouillon
                      </DropdownMenuItem>
                    </>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            )
          }}
          empty={isSearching ? "Aucun devis ne correspond à votre recherche." : "Aucun plan de traitement."}
        />
        <Table containerClassName={TABLE_ONLY}>
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
                <TableCell colSpan={colSpan} className="text-center text-muted-foreground py-8">
                  {isSearching ? "Aucun devis ne correspond à votre recherche." : "Aucun plan de traitement."}
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
                                  onSelect={() => setDeleteTarget(plan)}
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

      <AlertDialog open={!!deleteTarget} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce brouillon ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cette action est irréversible. Seuls les brouillons peuvent être supprimés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busyId === deleteTarget?.id}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
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
