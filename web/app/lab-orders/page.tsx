"use client"

import type React from "react"

import { useCallback, useEffect, useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { Plus, Pencil, Trash2, Loader2, FlaskConical } from "lucide-react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/components/ui/dialog"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { labOrdersApi, type LabWorkOrderPayload } from "@/lib/api/lab-orders"
import { patientsApi } from "@/lib/api/patients"
import { ApiError } from "@/lib/api/client"
import type { LabWorkOrderDto, PatientDto } from "@/lib/api/types"

// The four lifecycle stages a lab work order moves through (mirrors the backend enum).
type LabOrderStatus = "Sent" | "InProgress" | "Received" | "Fitted"


const STATUS_LABELS: Record<LabOrderStatus, string> = {
  Sent: "Envoyé",
  InProgress: "En cours",
  Received: "Reçu",
  Fitted: "Posé",
}

const STATUS_VARIANTS: Record<LabOrderStatus, "default" | "secondary" | "outline" | "destructive"> = {
  Sent: "secondary",
  InProgress: "default",
  Received: "outline",
  Fitted: "default",
}

// `status` arrives as a plain string on the DTO — narrow to the known set, falling back to the raw value.
function statusLabel(status: string): string {
  return STATUS_LABELS[status as LabOrderStatus] ?? status
}

function statusVariant(status: string): "default" | "secondary" | "outline" | "destructive" {
  return STATUS_VARIANTS[status as LabOrderStatus] ?? "secondary"
}

// French short date (e.g. "17 juil. 2026"); "—" when absent or unparseable.
function formatDateFr(iso?: string | null): string {
  if (!iso) return "—"
  try {
    return format(parseISO(iso), "d MMM yyyy", { locale: fr })
  } catch {
    return "—"
  }
}

// Tunisian dinar with millime precision (3 decimals); "—" when no cost recorded.
function formatCost(cost?: number | null): string {
  return cost != null ? `${cost.toFixed(3)} DT` : "—"
}

// An ISO date string begins with yyyy-MM-dd, exactly what <input type="date"> expects as its value.
function toDateInput(iso?: string | null): string {
  return iso ? iso.slice(0, 10) : ""
}

function parseIntOrNull(value: string): number | null {
  if (value.trim() === "") return null
  const n = parseInt(value, 10)
  return Number.isNaN(n) ? null : n
}

function parseFloatOrNull(value: string): number | null {
  if (value.trim() === "") return null
  const n = parseFloat(value)
  return Number.isNaN(n) ? null : n
}

// Native <select> styled to match the shadcn Input primitive.
const SELECT_CLASS =
  "border-input h-9 w-full min-w-0 rounded-md border bg-transparent px-3 py-1 text-base shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 md:text-sm"

interface LabOrderFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingOrder: LabWorkOrderDto | null
  patients: PatientDto[]
  onSaved: () => void
}

function LabOrderFormModal({ open, onOpenChange, editingOrder, patients, onSaved }: LabOrderFormModalProps) {
  const [patientId, setPatientId] = useState("")
  const [prosthetist, setProsthetist] = useState("")
  const [workDescription, setWorkDescription] = useState("")
  const [toothNumber, setToothNumber] = useState("")
  const [sentDate, setSentDate] = useState("")
  const [expectedDate, setExpectedDate] = useState("")
  const [cost, setCost] = useState("")
  const [notes, setNotes] = useState("")
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (editingOrder) {
      setPatientId(editingOrder.patientId)
      setProsthetist(editingOrder.prosthetist)
      setWorkDescription(editingOrder.workDescription)
      setToothNumber(editingOrder.toothNumber != null ? String(editingOrder.toothNumber) : "")
      setSentDate(toDateInput(editingOrder.sentDate))
      setExpectedDate(toDateInput(editingOrder.expectedDate))
      setCost(editingOrder.cost != null ? String(editingOrder.cost) : "")
      setNotes(editingOrder.notes ?? "")
    } else {
      setPatientId("")
      setProsthetist("")
      setWorkDescription("")
      setToothNumber("")
      setSentDate("")
      setExpectedDate("")
      setCost("")
      setNotes("")
    }
    setErrors({})
  }, [editingOrder, open])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!editingOrder && !patientId) next.patientId = "Le patient est requis"
    if (!prosthetist.trim()) next.prosthetist = "Le prothésiste est requis"
    if (!workDescription.trim()) next.workDescription = "La description du travail est requise"
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return

    // The patient can't be reassigned on edit — the update endpoint omits patientId.
    const common: Omit<LabWorkOrderPayload, "patientId"> = {
      prosthetist: prosthetist.trim(),
      workDescription: workDescription.trim(),
      toothNumber: parseIntOrNull(toothNumber),
      sentDate: sentDate || null,
      expectedDate: expectedDate || null,
      cost: parseFloatOrNull(cost),
      notes: notes.trim() || null,
    }

    try {
      setSaving(true)
      if (editingOrder) {
        await labOrdersApi.update(editingOrder.id, common)
        toast.success("Bon de laboratoire mis à jour")
      } else {
        await labOrdersApi.create({ patientId, ...common })
        toast.success("Bon de laboratoire créé")
      }
      onOpenChange(false)
      onSaved()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement du bon")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{editingOrder ? "Modifier le bon" : "Nouveau bon de prothèse"}</DialogTitle>
          <DialogDescription>
            {editingOrder
              ? "Mettez à jour les détails du bon de laboratoire"
              : "Renseignez les détails du bon envoyé au laboratoire"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="patient">
              Patient <span className="text-destructive">*</span>
            </Label>
            {editingOrder ? (
              <Input id="patient" value={editingOrder.patientName ?? "—"} disabled />
            ) : (
              <select
                id="patient"
                className={SELECT_CLASS}
                value={patientId}
                onChange={(e) => setPatientId(e.target.value)}
              >
                <option value="">Sélectionner un patient</option>
                {patients.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.firstName} {p.lastName}
                  </option>
                ))}
              </select>
            )}
            {errors.patientId && <p className="text-xs text-destructive">{errors.patientId}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="prosthetist">
              Prothésiste / laboratoire <span className="text-destructive">*</span>
            </Label>
            <Input
              id="prosthetist"
              placeholder="ex. Labo Dentaire Tunis"
              value={prosthetist}
              onChange={(e) => setProsthetist(e.target.value)}
            />
            {errors.prosthetist && <p className="text-xs text-destructive">{errors.prosthetist}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="workDescription">
              Description du travail <span className="text-destructive">*</span>
            </Label>
            <Input
              id="workDescription"
              placeholder="ex. couronne céramique"
              value={workDescription}
              onChange={(e) => setWorkDescription(e.target.value)}
            />
            {errors.workDescription && <p className="text-xs text-destructive">{errors.workDescription}</p>}
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="toothNumber">Dent (FDI)</Label>
              <Input
                id="toothNumber"
                type="number"
                min="0"
                placeholder="Optionnel"
                value={toothNumber}
                onChange={(e) => setToothNumber(e.target.value)}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="cost">Coût</Label>
              <Input
                id="cost"
                type="number"
                min="0"
                step="0.001"
                placeholder="Optionnel"
                value={cost}
                onChange={(e) => setCost(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="sentDate">Date d&apos;envoi</Label>
              <Input id="sentDate" type="date" value={sentDate} onChange={(e) => setSentDate(e.target.value)} />
            </div>

            <div className="space-y-2">
              <Label htmlFor="expectedDate">Date prévue</Label>
              <Input
                id="expectedDate"
                type="date"
                value={expectedDate}
                onChange={(e) => setExpectedDate(e.target.value)}
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea
              id="notes"
              placeholder="Optionnel"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Enregistrement..." : editingOrder ? "Mettre à jour" : "Créer le bon"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

export default function LabOrdersPage() {
  const [orders, setOrders] = useState<LabWorkOrderDto[]>([])
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingOrder, setEditingOrder] = useState<LabWorkOrderDto | null>(null)
  const [statusUpdatingId, setStatusUpdatingId] = useState<string | null>(null)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [orderToDelete, setOrderToDelete] = useState<LabWorkOrderDto | null>(null)
  const [deleting, setDeleting] = useState(false)

  const loadOrders = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await labOrdersApi.list()
      setOrders(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des bons de laboratoire"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [])

  // Patients back the "Nouveau bon" picker; a failure there shouldn't blank the page, just warn.
  const loadPatients = useCallback(async () => {
    try {
      const data = await patientsApi.list()
      setPatients(data)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du chargement des patients")
    }
  }, [])

  useEffect(() => {
    loadOrders()
    loadPatients()
  }, [loadOrders, loadPatients])

  const handleAddNew = () => {
    setEditingOrder(null)
    setModalOpen(true)
  }

  const handleEdit = (order: LabWorkOrderDto) => {
    setEditingOrder(order)
    setModalOpen(true)
  }

  const handleStatusChange = async (order: LabWorkOrderDto, status: string) => {
    if (status === order.status) return
    try {
      setStatusUpdatingId(order.id)
      await labOrdersApi.updateStatus(order.id, status)
      toast.success(`Statut mis à jour : ${statusLabel(status)}`)
      await loadOrders()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la mise à jour du statut")
    } finally {
      setStatusUpdatingId(null)
    }
  }

  const handleDelete = (order: LabWorkOrderDto) => {
    setOrderToDelete(order)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (!orderToDelete) return
    try {
      setDeleting(true)
      await labOrdersApi.delete(orderToDelete.id)
      toast.success("Bon de laboratoire supprimé")
      setDeleteDialogOpen(false)
      setOrderToDelete(null)
      await loadOrders()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression du bon")
    } finally {
      setDeleting(false)
    }
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div className="flex items-center justify-between">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Laboratoire — bons de prothèse</h1>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Suivez les travaux prothétiques envoyés au laboratoire
                  </p>
                </div>

                <Button onClick={handleAddNew} className="gap-2">
                  <Plus className="h-4 w-4" />
                  Nouveau bon
                </Button>
              </div>

              {/* Orders Table */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <FlaskConical className="h-5 w-5" />
                    Bons de laboratoire
                    <Badge variant="secondary" className="ml-2">
                      {orders.length}
                    </Badge>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {loading ? (
                    <div className="flex items-center justify-center py-12 text-muted-foreground">
                      <Loader2 className="h-5 w-5 animate-spin" />
                    </div>
                  ) : error ? (
                    <p className="py-12 text-center text-sm text-destructive">{error}</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <Table>
                        <TableHeader>
                          <TableRow>
                            <TableHead>Patient</TableHead>
                            <TableHead>Travail</TableHead>
                            <TableHead>Prothésiste</TableHead>
                            <TableHead>Dent</TableHead>
                            <TableHead>Envoyé</TableHead>
                            <TableHead>Prévu</TableHead>
                            <TableHead>Reçu</TableHead>
                            <TableHead>Coût</TableHead>
                            <TableHead>Statut</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                          </TableRow>
                        </TableHeader>
                        <TableBody>
                          {orders.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={10} className="h-24 text-center">
                                <p className="text-muted-foreground">Aucun bon de laboratoire</p>
                              </TableCell>
                            </TableRow>
                          ) : (
                            orders.map((order) => (
                              <TableRow key={order.id}>
                                <TableCell className="font-medium text-foreground">
                                  {order.patientName ?? "—"}
                                </TableCell>
                                <TableCell>{order.workDescription}</TableCell>
                                <TableCell className="text-muted-foreground">{order.prosthetist}</TableCell>
                                <TableCell className="text-muted-foreground">{order.toothNumber ?? "—"}</TableCell>
                                <TableCell className="text-muted-foreground">{formatDateFr(order.sentDate)}</TableCell>
                                <TableCell className="text-muted-foreground">
                                  {formatDateFr(order.expectedDate)}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                  {formatDateFr(order.receivedDate)}
                                </TableCell>
                                <TableCell className="text-muted-foreground">{formatCost(order.cost)}</TableCell>
                                <TableCell>
                                  <Badge variant={statusVariant(order.status)}>{statusLabel(order.status)}</Badge>
                                </TableCell>
                                <TableCell className="text-right">
                                  <div className="flex items-center justify-end gap-2">
                                    {/* AC-P2.40 — offer only the stages the server will accept from here. It
                                        used to list all four unconditionally, so « Posé » → « Envoyé » looked
                                        like a normal choice and (before the domain had any rules) silently
                                        rewound a delivered prothèse. The current stage stays in the list as the
                                        selected value; `allowedNextStatuses` comes from the domain's table, so
                                        the client never re-derives it. A legacy row in an unmapped state gets an
                                        empty list — the control is then disabled rather than offering a
                                        transition that would be refused. */}
                                    <select
                                      aria-label="Changer le statut"
                                      className={`${SELECT_CLASS} h-8 w-32`}
                                      value={order.status}
                                      disabled={
                                        statusUpdatingId === order.id ||
                                        (order.allowedNextStatuses?.length ?? 0) === 0
                                      }
                                      onChange={(e) => handleStatusChange(order, e.target.value)}
                                    >
                                      <option value={order.status}>{statusLabel(order.status)}</option>
                                      {(order.allowedNextStatuses ?? []).map((s) => (
                                        <option key={s} value={s}>
                                          {statusLabel(s)}
                                        </option>
                                      ))}
                                    </select>
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      onClick={() => handleEdit(order)}
                                      className="h-8 gap-1"
                                    >
                                      <Pencil className="h-3 w-3" />
                                      Modifier
                                    </Button>
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      onClick={() => handleDelete(order)}
                                      className="h-8 gap-1 text-destructive hover:text-destructive"
                                    >
                                      <Trash2 className="h-3 w-3" />
                                      Supprimer
                                    </Button>
                                  </div>
                                </TableCell>
                              </TableRow>
                            ))
                          )}
                        </TableBody>
                      </Table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </main>
        </div>

        <LabOrderFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingOrder={editingOrder}
          patients={patients}
          onSaved={loadOrders}
        />

        <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Êtes-vous sûr ?</AlertDialogTitle>
              <AlertDialogDescription>
                Le bon de{" "}
                <span className="font-semibold">{orderToDelete?.workDescription}</span> sera définitivement
                supprimé. Cette action est irréversible.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
              <AlertDialogAction
                onClick={(e) => {
                  e.preventDefault()
                  confirmDelete()
                }}
                disabled={deleting}
                className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              >
                {deleting ? "Suppression..." : "Supprimer"}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>
    </ClinicGuard>
  )
}
