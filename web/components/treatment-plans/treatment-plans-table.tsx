"use client"

import { useState, useEffect, useCallback } from "react"
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
  from?: string
  to?: string
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
  showPatientColumn = true,
  reloadKey = 0,
  onChanged,
}: TreatmentPlansTableProps) {
  const [plans, setPlans] = useState<TreatmentPlanDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<TreatmentPlanDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TreatmentPlanDto | null>(null)

  const router = useRouter()

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await treatmentPlansApi.list({ patientId, status, from, to })
      setPlans(data)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec du chargement des plans de traitement.")
    } finally {
      setLoading(false)
    }
  }, [patientId, status, from, to])

  useEffect(() => {
    load()
  }, [load, reloadKey])

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
      <div className="flex justify-end">
        <Button onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" /> Nouveau plan
        </Button>
      </div>

      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
          {error}
        </div>
      )}

      <div className="rounded-md border overflow-x-auto">
        <Table>
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
                  Aucun plan de traitement.
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
