"use client"

import { useState, useEffect, useCallback, useRef } from "react"
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
import { FileDown, Pencil, Trash2, CheckCircle2, CheckCheck, Ban, ListChecks, CreditCard, CalendarPlus, Plus, Loader2, ReceiptText } from "lucide-react"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { invoicesApi } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto, InstallmentDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { planStatusLabel, planStatusBadgeClass, itemWorkflowLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import { planItemState, isPlanBilled } from "./plan-next-action"
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
  /** Deep-linked plan (from a devis-born invoice's « Devis » badge): scrolled to and highlighted once loaded. */
  highlightPlanId?: string | null
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
  highlightPlanId = null,
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

  // Bring a deep-linked plan into view — the row only exists once the list has loaded, so this can't run
  // from the parent's mount effect.
  const highlightedRowRef = useRef<HTMLTableRowElement | null>(null)
  useEffect(() => {
    if (!highlightPlanId || loading) return
    highlightedRowRef.current?.scrollIntoView({ block: "center", behavior: "smooth" })
  }, [highlightPlanId, loading, plans])

  // Three keys, not one: an act's état is derived from Appointment rows and the « Facturé » badge from Invoice
  // rows, and RealtimeBroadcastBehavior keys off the *command's* namespace — so cancelling an appointment
  // broadcasts "appointments", never "treatmentplans". Watching only the latter would leave an act showing
  // « Planifié » with "Planifier" hidden: booked-looking and unbookable until a manual reload.
  useClinicRealtime(
    [RealtimeResource.TreatmentPlans, RealtimeResource.Appointments, RealtimeResource.Invoices],
    load,
  )

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

  const handleComplete = async (plan: TreatmentPlanDto) => {
    setBusyId(plan.id)
    try {
      await treatmentPlansApi.complete(plan.id)
      toast.success("Plan terminé")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la clôture du plan.")
    } finally {
      setBusyId(null)
    }
  }

  const handleInvoiceFromPlan = async (plan: TreatmentPlanDto) => {
    setBusyId(plan.id)
    try {
      await invoicesApi.createFromPlan(plan.id)
      toast.success("Facture brouillon créée depuis le devis")
      afterMutation()
      router.push("/factures")
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la facturation du devis.")
    } finally {
      setBusyId(null)
    }
  }

  const handleDownloadInstallmentReceipt = async (planId: string, installmentId: string) => {
    setBusyId(installmentId)
    try {
      const blob = await treatmentPlansApi.downloadInstallmentReceipt(planId, installmentId)
      downloadBlob(blob, `recu-echeance-${installmentId.slice(0, 8)}.pdf`)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du reçu.")
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
                const isHighlighted = plan.id === highlightPlanId
                return (
                  <TableRow
                    key={plan.id}
                    ref={isHighlighted ? highlightedRowRef : undefined}
                    className={isHighlighted ? "bg-accent/60" : undefined}
                  >
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
                        {isActive && !isPlanBilled(plan) && (
                          <Button variant="ghost" size="icon" title="Facturer le devis" onClick={() => handleInvoiceFromPlan(plan)} disabled={isBusy}>
                            <ReceiptText className="h-4 w-4" />
                          </Button>
                        )}
                        {isPlanBilled(plan) && (
                          <Badge variant="outline" className="self-center whitespace-nowrap">
                            Facturé{plan.linkedInvoiceNumber ? ` — ${plan.linkedInvoiceNumber}` : ""}
                          </Badge>
                        )}
                        {isActive && (
                          <Button variant="ghost" size="icon" title="Terminer le plan" onClick={() => handleComplete(plan)} disabled={isBusy}>
                            <CheckCheck className="h-4 w-4" />
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
                            <Badge variant="secondary" className={itemWorkflowBadgeClass(planItemState(item))}>
                              {itemWorkflowLabel(planItemState(item))}
                            </Badge>
                            {item.scheduledAt && item.status !== "Done" && (
                              <span className="ml-2 whitespace-nowrap text-xs text-muted-foreground">
                                {formatDateFr(item.scheduledAt)}
                              </span>
                            )}
                          </TableCell>
                          <TableCell className="text-right">
                            {/* Only an act with nothing booked can be scheduled — this is what makes a second
                                click impossible. A cancelled/no-show appointment leaves the act "to-schedule",
                                so it correctly becomes bookable again. */}
                            {planItemState(item) === "to-schedule" &&
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
                        {manageTarget.installments.map((inst) => {
                          const isOverdue = !inst.isPaid && new Date(inst.dueDate).getTime() < Date.now()
                          return (
                          <TableRow key={inst.id}>
                            <TableCell>{formatDateFr(inst.dueDate)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.amount)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.amountPaid)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.outstanding)}</TableCell>
                            <TableCell>
                              {inst.isPaid ? (
                                <Badge variant="secondary">Payée</Badge>
                              ) : isOverdue ? (
                                <Badge variant="destructive">En retard</Badge>
                              ) : (
                                <Badge variant="outline">En attente</Badge>
                              )}
                            </TableCell>
                            <TableCell className="text-right">
                              <div className="flex justify-end gap-1">
                                {!inst.isPaid && manageTarget.status !== "Cancelled" && (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="h-8 gap-1"
                                    onClick={() => setPaymentTarget({ planId: manageTarget.id, installment: inst })}
                                    disabled={busyId === inst.id}
                                  >
                                    <CreditCard className="h-4 w-4" />
                                    Encaisser
                                  </Button>
                                )}
                                {inst.amountPaid > 0 && (
                                  <Button
                                    variant="ghost"
                                    size="icon"
                                    className="h-8 w-8"
                                    title="Télécharger le reçu"
                                    onClick={() => handleDownloadInstallmentReceipt(manageTarget.id, inst.id)}
                                    disabled={busyId === inst.id}
                                  >
                                    <ReceiptText className="h-4 w-4" />
                                  </Button>
                                )}
                              </div>
                            </TableCell>
                          </TableRow>
                          )
                        })}
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
