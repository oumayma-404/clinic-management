"use client"

import { useState, useEffect, useCallback } from "react"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { FileDown, Pencil, Trash2, CheckCircle2, Ban, ListChecks, CreditCard, CalendarPlus, Plus, Loader2 } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto, InstallmentDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { planStatusLabel, planStatusBadgeClass, itemStatusLabel } from "./treatment-plan-labels"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"

/** Target for the "Planifier" action: an open plan item to schedule as an appointment. */
interface ScheduleTarget {
  patientId: string
  patientName: string
  planId: string
  itemId: string
  label: string
}

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
  const [manageTarget, setManageTarget] = useState<TreatmentPlanDto | null>(null)
  const [paymentTarget, setPaymentTarget] = useState<{ planId: string; installment: InstallmentDto } | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TreatmentPlanDto | null>(null)
  const [cancelTarget, setCancelTarget] = useState<TreatmentPlanDto | null>(null)
  const [cancelReason, setCancelReason] = useState("")
  const [scheduleTarget, setScheduleTarget] = useState<ScheduleTarget | null>(null)

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

  useClinicRealtime(RealtimeResource.TreatmentPlans, load)

  const afterMutation = () => {
    load()
    onChanged?.()
  }

  // Refetch a single plan (keeps the open "manage" dialog in sync after a sub-entity mutation).
  const refreshManaged = async (id: string) => {
    try {
      const fresh = await treatmentPlansApi.get(id)
      setManageTarget(fresh)
    } catch {
      // Non-blocking — the list reload below still reflects the change.
    }
  }

  const handleAccept = async (plan: TreatmentPlanDto) => {
    setBusyId(plan.id)
    try {
      await treatmentPlansApi.accept(plan.id)
      toast.success("Plan accepté")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'acceptation.")
    } finally {
      setBusyId(null)
    }
  }

  const handleDownloadPdf = async (plan: TreatmentPlanDto) => {
    setBusyId(plan.id)
    try {
      const blob = await treatmentPlansApi.downloadDevisPdf(plan.id)
      const url = URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      a.download = `devis-${plan.number ?? plan.id}.pdf`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
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

  const confirmCancel = async () => {
    if (!cancelTarget) return
    if (!cancelReason.trim()) {
      toast.error("Le motif d'annulation est requis.")
      return
    }
    setBusyId(cancelTarget.id)
    try {
      await treatmentPlansApi.cancel(cancelTarget.id, cancelReason.trim())
      toast.success("Plan annulé")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'annulation.")
    } finally {
      setBusyId(null)
      setCancelTarget(null)
      setCancelReason("")
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

  const colSpan = showPatientColumn ? 7 : 6

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
                const isActive = plan.status === "Accepted" || plan.status === "InProgress"
                const isCancellable = isActive
                return (
                  <TableRow key={plan.id}>
                    <TableCell className="font-medium">{plan.number ?? plan.title}</TableCell>
                    {showPatientColumn && <TableCell>{plan.patientName ?? "—"}</TableCell>}
                    <TableCell>
                      <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
                        {planStatusLabel(plan.status)}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">{formatDT(plan.totalPlanned)}</TableCell>
                    <TableCell className="text-right">{formatDT(plan.amountPaid)}</TableCell>
                    <TableCell className="text-right">{formatDT(plan.outstanding)}</TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        {isBusy && <Loader2 className="h-4 w-4 animate-spin self-center" />}
                        {isDraft && (
                          <>
                            <Button variant="ghost" size="icon" title="Modifier" onClick={() => openEdit(plan)} disabled={isBusy}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" title="Accepter" onClick={() => handleAccept(plan)} disabled={isBusy}>
                              <CheckCircle2 className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" title="Supprimer" onClick={() => setDeleteTarget(plan)} disabled={isBusy}>
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </>
                        )}
                        {(isActive || plan.status === "Completed") && (
                          <Button variant="ghost" size="icon" title="Gérer les actes et paiements" onClick={() => setManageTarget(plan)} disabled={isBusy}>
                            <ListChecks className="h-4 w-4" />
                          </Button>
                        )}
                        {isCancellable && (
                          <Button variant="ghost" size="icon" title="Annuler" onClick={() => setCancelTarget(plan)} disabled={isBusy}>
                            <Ban className="h-4 w-4" />
                          </Button>
                        )}
                        <Button variant="ghost" size="icon" title="Télécharger le devis" onClick={() => handleDownloadPdf(plan)} disabled={isBusy}>
                          <FileDown className="h-4 w-4" />
                        </Button>
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

      {/* Manage dialog: mark items done + record installment payments. */}
      <Dialog open={!!manageTarget} onOpenChange={(open) => !open && setManageTarget(null)}>
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>{manageTarget?.number ?? manageTarget?.title ?? "Plan de traitement"}</DialogTitle>
            <DialogDescription>Actes réalisés et encaissement des échéances.</DialogDescription>
          </DialogHeader>

          {manageTarget && (
            <div className="space-y-6">
              {/* Items */}
              <div className="space-y-2">
                <h3 className="text-sm font-semibold">Actes</h3>
                <div className="rounded-md border overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Désignation</TableHead>
                        <TableHead>Dents</TableHead>
                        <TableHead className="text-right">Coût</TableHead>
                        <TableHead>Statut</TableHead>
                        <TableHead className="text-right">Action</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {manageTarget.items.map((item) => (
                        <TableRow key={item.id}>
                          <TableCell>{item.designationFr}</TableCell>
                          <TableCell>
                            {item.toothNumbers.length > 0 ? (
                              <div className="flex flex-wrap gap-1">
                                {item.toothNumbers.map((t) => (
                                  <Badge key={t} variant="secondary" className="text-xs">{t}</Badge>
                                ))}
                              </div>
                            ) : (
                              <span className="text-muted-foreground text-sm">—</span>
                            )}
                          </TableCell>
                          <TableCell className="text-right">{formatDT(item.plannedCost)}</TableCell>
                          <TableCell>
                            <Badge variant={item.status === "Done" ? "secondary" : "outline"}>
                              {itemStatusLabel(item.status)}
                            </Badge>
                          </TableCell>
                          <TableCell className="text-right">
                            {item.status !== "Done" &&
                              (manageTarget.status === "Accepted" || manageTarget.status === "InProgress") && (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  onClick={() =>
                                    setScheduleTarget({
                                      patientId: manageTarget.patientId,
                                      patientName: manageTarget.patientName ?? "Patient",
                                      planId: manageTarget.id,
                                      itemId: item.id,
                                      label:
                                        item.toothNumbers.length > 0
                                          ? `${item.designationFr} (dents ${item.toothNumbers.join(", ")})`
                                          : item.designationFr,
                                    })
                                  }
                                >
                                  <CalendarPlus className="h-4 w-4" />
                                  Planifier
                                </Button>
                              )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
                <p className="text-xs text-muted-foreground">
                  Un acte est marqué « réalisé » automatiquement lors de l'enregistrement de la fiche de soins liée
                  (choisissez l'acte du plan dans la fiche). Utilisez « Planifier » pour créer le rendez-vous.
                </p>
              </div>

              {/* Installments */}
              <div className="space-y-2">
                <h3 className="text-sm font-semibold">Échéances</h3>
                {manageTarget.installments.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Aucune échéance définie.</p>
                ) : (
                  <div className="rounded-md border overflow-x-auto">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Échéance</TableHead>
                          <TableHead className="text-right">Montant</TableHead>
                          <TableHead className="text-right">Encaissé</TableHead>
                          <TableHead className="text-right">Reste</TableHead>
                          <TableHead>Statut</TableHead>
                          <TableHead className="text-right">Action</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {manageTarget.installments.map((inst) => (
                          <TableRow key={inst.id}>
                            <TableCell>{formatDateFr(inst.dueDate)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.amount)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.amountPaid)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.outstanding)}</TableCell>
                            <TableCell>
                              <Badge variant={inst.isPaid ? "secondary" : "outline"}>
                                {inst.isPaid ? "Payée" : "En attente"}
                              </Badge>
                            </TableCell>
                            <TableCell className="text-right">
                              {!inst.isPaid && manageTarget.status !== "Cancelled" && (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  onClick={() => setPaymentTarget({ planId: manageTarget.id, installment: inst })}
                                >
                                  <CreditCard className="h-4 w-4" />
                                  Encaisser
                                </Button>
                              )}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </div>
                )}
              </div>
            </div>
          )}

          <DialogFooter>
            <Button variant="outline" onClick={() => setManageTarget(null)}>Fermer</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <InstallmentPaymentModal
        open={!!paymentTarget}
        onOpenChange={(open) => !open && setPaymentTarget(null)}
        planId={paymentTarget?.planId ?? null}
        installment={paymentTarget?.installment ?? null}
        onSuccess={() => {
          if (manageTarget) refreshManaged(manageTarget.id)
          afterMutation()
        }}
      />

      {/* Schedule an appointment for a plan step ("Planifier") — links the appointment to the plan item. */}
      <CreateAppointmentDialog
        open={!!scheduleTarget}
        onOpenChange={(open) => !open && setScheduleTarget(null)}
        presetPatientId={scheduleTarget?.patientId}
        presetPatientName={scheduleTarget?.patientName}
        presetPlanId={scheduleTarget?.planId}
        presetPlanItemId={scheduleTarget?.itemId}
        presetProcedureName={scheduleTarget?.label}
        onSuccess={() => {
          setScheduleTarget(null)
          afterMutation()
        }}
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

      <Dialog open={!!cancelTarget} onOpenChange={(open) => { if (!open) { setCancelTarget(null); setCancelReason("") } }}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler le plan</DialogTitle>
            <DialogDescription>
              {cancelTarget?.number ? `Plan ${cancelTarget.number}` : "Plan de traitement"} — un motif est requis.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Textarea
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Motif d'annulation"
              rows={3}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" onClick={() => { setCancelTarget(null); setCancelReason("") }} disabled={busyId === cancelTarget?.id}>
              Retour
            </Button>
            <Button
              onClick={confirmCancel}
              disabled={busyId === cancelTarget?.id}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Confirmer l'annulation
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
