"use client"

import { useEffect, useMemo, useState } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableHead, TableHeader, TableRow, TableCell } from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import {
  ArrowLeft, Ban, CreditCard, FileDown, Loader2, ReceiptText, CheckCheck, ClipboardCheck,
} from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { invoicesApi } from "@/lib/api/invoices"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import type { InstallmentDto, ProcedureTypeDto, TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr, isBeforeToday } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { planStatusLabel, planStatusBadgeClass } from "./treatment-plan-labels"
import { isPlanBilled } from "./plan-next-action"
import { PlanProgressBar } from "./plan-progress-bar"
import { PlanActRow } from "./plan-act-row"
import { PlanTimeline } from "./plan-timeline"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"

interface PlanWorkspaceProps {
  plan: TreatmentPlanDto
  /** Refetch the plan after any mutation (the parent owns the fetch). */
  onChanged: () => void
}

/**
 * The devis's home: header, actes, échéancier and parcours on one page. Replaces the plans-table "Gérer"
 * dialog, which was the only place a plan's contents were visible and offered every action on every row.
 */
export function PlanWorkspace({ plan, onChanged }: PlanWorkspaceProps) {
  const router = useRouter()
  const [busy, setBusy] = useState(false)
  const [paymentTarget, setPaymentTarget] = useState<InstallmentDto | null>(null)
  const [scheduleTarget, setScheduleTarget] = useState<TreatmentPlanItemDto | null>(null)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState("")
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])

  // Only needed to resolve an act's procedure when booking it (below). Failure is silent — it degrades to the
  // previous free-text behaviour rather than blocking the workspace.
  useEffect(() => {
    procedureTypesApi
      .list(false)
      .then((data) => setProcedureTypes(data || []))
      .catch(() => setProcedureTypes([]))
  }, [])

  /**
   * The procedure an act stands for, so booking it produces a real `procedureTypeId` (colour, default
   * duration, and the act proposal in the dental-record modal) instead of just a name in the notes.
   *
   * Prefers the act's stored `procedureTypeId`. Falls back to matching `designationFr` against the catalog by
   * name, which works for **acts created before that column existed**: the plan editor used to snapshot a
   * « Mes actes » pick as a free-text line whose designation is `pt.name` verbatim, so the name is a reliable
   * key for those rows. Lines from the CNAM catalogue, typed by hand, or renamed after picking match neither
   * way and keep the previous free-text behaviour.
   */
  const scheduleProcedureTypeId = useMemo(() => {
    if (!scheduleTarget) return undefined

    const stored = scheduleTarget.procedureTypeId
    // Still verified against the loaded catalog — a procedure retired since the devis was written must not
    // preselect an option that no longer exists (the link is a soft reference, with no FK to guarantee it).
    if (stored && procedureTypes.some((p) => p.id === stored)) return stored

    const designation = scheduleTarget.designationFr?.trim().toLowerCase()
    if (!designation) return undefined
    const matches = procedureTypes.filter((p) => p.name.trim().toLowerCase() === designation)
    // Ambiguity means the catalog holds two procedures with the same name; guessing one would put the wrong
    // fee and colour on the appointment, so prefer no prefill.
    return matches.length === 1 ? matches[0].id : undefined
  }, [scheduleTarget, procedureTypes])

  const isDraft = plan.status === "Draft"
  const isActive = plan.status === "Accepted" || plan.status === "InProgress"
  const billed = isPlanBilled(plan)
  // Reordering is cosmetic, so it stays available on a Completed plan too — only a cancelled devis (and a
  // one-act plan, where there is nothing to move) hides the controls.
  const canReorder = plan.status !== "Cancelled" && plan.items.length > 1

  const run = async (action: () => Promise<unknown>, success: string, failure: string) => {
    setBusy(true)
    try {
      await action()
      toast.success(success)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : failure)
    } finally {
      setBusy(false)
    }
  }

  const handleDownloadDevis = async () => {
    setBusy(true)
    try {
      const blob = await treatmentPlansApi.downloadDevisPdf(plan.id)
      downloadBlob(blob, `devis-${plan.number ?? plan.id}.pdf`)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du devis.")
    } finally {
      setBusy(false)
    }
  }

  /**
   * Move an act one position up or down. The endpoint takes the **whole** order, not a delta — a partial
   * list would leave the untouched acts at stale positions and silently interleave them — so this rebuilds
   * the full id list and sends it.
   */
  const handleMove = async (index: number, direction: -1 | 1) => {
    const target = index + direction
    if (target < 0 || target >= plan.items.length) return

    const ids = plan.items.map((i) => i.id)
    ;[ids[index], ids[target]] = [ids[target], ids[index]]

    await run(
      () => treatmentPlansApi.reorderItems(plan.id, ids),
      "Ordre des actes mis à jour",
      "Échec du réordonnancement.",
    )
  }

  const handleDownloadReceipt = async (installmentId: string, paymentId: string) => {
    setBusy(true)
    try {
      const blob = await treatmentPlansApi.downloadInstallmentReceipt(plan.id, installmentId, paymentId)
      downloadBlob(blob, `recu-echeance-${paymentId.slice(0, 8)}.pdf`)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du reçu.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      {/* router.push, never router.back(): the workspace is reachable from /factures, the patient page and
          the plans list, and "back" to a different surface than the one the button names is disorienting.
          router.back() has zero uses in this codebase. */}
      <Button variant="ghost" size="sm" className="gap-2" onClick={() => router.push("/treatment-plans")}>
        <ArrowLeft className="h-4 w-4" />
        Retour aux plans
      </Button>

      {/* ---- Header -------------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <CardTitle className="flex flex-wrap items-center gap-2 text-xl">
              {plan.number ?? plan.title}
              {/* The devis PDF re-renders live from current state under the same number and is archived
                  nowhere, so this counter is the only way a patient's earlier printout can be identified.
                  Hidden at 0 — a never-amended devis says nothing about revisions. */}
              {plan.revisionNumber > 0 && (
                <span className="text-base font-normal text-muted-foreground">
                  · révision {plan.revisionNumber}
                </span>
              )}
              <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
                {planStatusLabel(plan.status)}
              </Badge>
              {billed && (
                <Badge variant="outline">
                  Facturé{plan.linkedInvoiceNumber ? ` — ${plan.linkedInvoiceNumber}` : ""}
                </Badge>
              )}
            </CardTitle>
            <div className="flex flex-wrap items-center gap-2">
              {busy && <Loader2 className="h-4 w-4 animate-spin" />}
              {isDraft && (
                <Button
                  size="sm"
                  className="gap-2"
                  disabled={busy}
                  onClick={() => run(() => treatmentPlansApi.accept(plan.id), "Devis accepté", "Échec de l'acceptation.")}
                >
                  <ClipboardCheck className="h-4 w-4" />
                  Accepter le devis
                </Button>
              )}
              {isActive && !billed && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  disabled={busy}
                  onClick={() =>
                    run(
                      async () => {
                        await invoicesApi.createFromPlan(plan.id)
                        router.push("/factures")
                      },
                      "Facture brouillon créée depuis le devis",
                      "Échec de la facturation du devis.",
                    )
                  }
                >
                  <ReceiptText className="h-4 w-4" />
                  Facturer le devis
                </Button>
              )}
              {isActive && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  disabled={busy}
                  onClick={() => run(() => treatmentPlansApi.complete(plan.id), "Plan terminé", "Échec de la clôture du plan.")}
                >
                  <CheckCheck className="h-4 w-4" />
                  Terminer
                </Button>
              )}
              <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={handleDownloadDevis}>
                <FileDown className="h-4 w-4" />
                Devis PDF
              </Button>
              {/* Cancelling a numbered devis lives here rather than in the list: it is the one destructive
                  action on the plan and needs the context of what is being voided. Server-side it is
                  AdminOrDoctor; the UI calls it unconditionally and surfaces the 403, matching the other
                  financial-reversal actions. */}
              {isActive && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2 text-destructive hover:text-destructive"
                  disabled={busy}
                  onClick={() => setCancelOpen(true)}
                >
                  <Ban className="h-4 w-4" />
                  Annuler
                </Button>
              )}
            </div>
          </div>
          <p className="text-sm text-muted-foreground">
            <button
              type="button"
              className="underline underline-offset-2 hover:text-foreground"
              onClick={() => router.push(`/patients/${plan.patientId}`)}
            >
              {plan.patientName ?? "Patient"}
            </button>
            {plan.number && plan.title ? ` · ${plan.title}` : ""}
          </p>
        </CardHeader>

        <CardContent className="space-y-4">
          <PlanProgressBar done={plan.itemsDone} total={plan.itemsTotal} />

          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Figure label="Total" value={formatDT(plan.totalPlanned)} />
            <Figure label="Encaissé" value={formatDT(plan.amountPaid)} />
            {/* A Draft devis contributes 0 to « Solde patient » by design, so showing a « Reste » here would
                contradict the balance the rest of the app reports. */}
            {!isDraft && <Figure label="Reste" value={formatDT(plan.outstanding)} />}
            <Figure
              label="Actes réalisés"
              value={plan.itemsTotal > 0 ? `${plan.itemsDone}/${plan.itemsTotal}` : "—"}
            />
          </div>

          <p className="text-sm text-foreground">
            {isDraft
              ? "À accepter pour démarrer le suivi."
              : plan.nextAppointmentAt
                ? `Prochaine séance : ${formatDateFr(plan.nextAppointmentAt)}`
                : "Aucune séance planifiée"}
          </p>

          {plan.notes && (
            <p className="whitespace-pre-line rounded-md bg-muted/50 p-3 text-sm text-muted-foreground">
              {plan.notes}
            </p>
          )}
          {plan.cancellationReason && (
            <p className="rounded-md bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950 dark:text-red-200">
              Motif d&apos;annulation : {plan.cancellationReason}
            </p>
          )}
        </CardContent>
      </Card>

      {/* ---- Actes --------------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Actes</CardTitle>
        </CardHeader>
        <CardContent>
          {plan.items.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted-foreground">Aucun acte planifié.</p>
          ) : (
            <div className="rounded-md border overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    {canReorder && <TableHead className="w-16">Ordre</TableHead>}
                    <TableHead>Désignation</TableHead>
                    <TableHead>Dents</TableHead>
                    <TableHead className="text-right">Coût</TableHead>
                    <TableHead>État</TableHead>
                    <TableHead className="text-right">Action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {plan.items.map((item, index) => (
                    <PlanActRow
                      key={item.id}
                      plan={plan}
                      item={item}
                      onSchedule={setScheduleTarget}
                      reorder={
                        canReorder
                          ? {
                              disabled: busy,
                              canMoveUp: index > 0,
                              canMoveDown: index < plan.items.length - 1,
                              onMoveUp: () => handleMove(index, -1),
                              onMoveDown: () => handleMove(index, 1),
                            }
                          : undefined
                      }
                    />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <p className="mt-2 text-xs text-muted-foreground">
            Un acte passe à « Réalisé » à l&apos;enregistrement de la fiche de soins liée — il n&apos;y a pas de
            bascule manuelle.
          </p>
        </CardContent>
      </Card>

      {/* ---- Échéancier ---------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Échéancier</CardTitle>
        </CardHeader>
        <CardContent>
          {plan.installments.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted-foreground">Aucune échéance définie.</p>
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
                  {plan.installments.map((inst) => {
                    // Late only once the due DAY has passed — an échéance due today still has the day to run.
                    const isOverdue = !inst.isPaid && isBeforeToday(inst.dueDate)
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
                            {/* Payable on a Completed plan too: « Terminé » means every act was carried out,
                                not that the patient has paid. Only Draft/Cancelled refuse. */}
                            {!inst.isPaid && !isDraft && plan.status !== "Cancelled" && (
                              <Button
                                variant="outline"
                                size="sm"
                                className="h-8 gap-1"
                                disabled={busy}
                                onClick={() => setPaymentTarget(inst)}
                              >
                                <CreditCard className="h-4 w-4" />
                                Encaisser
                              </Button>
                            )}
                            {/* One receipt per PAYMENT — an échéance can hold several, and the receipt used
                                to print the cumulative total instead of the money handed over. */}
                            {inst.payments
                              .filter((p) => !p.isVoided)
                              .map((payment) => (
                                <Button
                                  key={payment.id}
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  disabled={busy}
                                  title={`Reçu du paiement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                                  onClick={() => handleDownloadReceipt(inst.id, payment.id)}
                                >
                                  <ReceiptText className="h-4 w-4" />
                                  Reçu
                                </Button>
                              ))}
                          </div>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* ---- Parcours ------------------------------------------------------------------------------ */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Parcours</CardTitle>
        </CardHeader>
        <CardContent className="px-0">
          <PlanTimeline plan={plan} />
        </CardContent>
      </Card>

      <InstallmentPaymentModal
        open={!!paymentTarget}
        onOpenChange={(open) => !open && setPaymentTarget(null)}
        planId={paymentTarget ? plan.id : null}
        installment={paymentTarget}
        onSuccess={() => {
          setPaymentTarget(null)
          onChanged()
        }}
      />

      <CreateAppointmentDialog
        open={!!scheduleTarget}
        onOpenChange={(open) => !open && setScheduleTarget(null)}
        presetPatientId={plan.patientId}
        presetPatientName={plan.patientName ?? "Patient"}
        presetPlanId={plan.id}
        presetPlanItemId={scheduleTarget?.id}
        presetProcedureTypeId={scheduleProcedureTypeId}
        presetProcedureName={
          scheduleTarget
            ? scheduleTarget.toothNumbers.length > 0
              ? `${scheduleTarget.designationFr} (dents ${scheduleTarget.toothNumbers.join(", ")})`
              : scheduleTarget.designationFr
            : undefined
        }
        onSuccess={() => {
          setScheduleTarget(null)
          onChanged()
        }}
      />

      <Dialog
        open={cancelOpen}
        onOpenChange={(open) => { if (!open) { setCancelOpen(false); setCancelReason("") } }}
      >
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler le plan</DialogTitle>
            <DialogDescription>
              {plan.number ? `Devis ${plan.number}` : "Plan de traitement"} — le numéro est conservé. Un motif
              est requis.
            </DialogDescription>
          </DialogHeader>
          <Textarea
            value={cancelReason}
            onChange={(e) => setCancelReason(e.target.value)}
            placeholder="Motif d'annulation"
            rows={3}
          />
          <DialogFooter className="gap-2">
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => { setCancelOpen(false); setCancelReason("") }}
            >
              Retour
            </Button>
            <Button
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              disabled={busy}
              onClick={async () => {
                if (!cancelReason.trim()) {
                  toast.error("Le motif d'annulation est requis.")
                  return
                }
                await run(
                  () => treatmentPlansApi.cancel(plan.id, cancelReason.trim()),
                  "Plan annulé",
                  "Échec de l'annulation.",
                )
                setCancelOpen(false)
                setCancelReason("")
              }}
            >
              Confirmer l&apos;annulation
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-lg font-semibold">{value}</p>
    </div>
  )
}
