"use client"

import type React from "react"
import Link from "next/link"

import { useCallback, useEffect, useState } from "react"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"

import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import {
  AlertTriangle,
  Check,
  ChevronsUpDown,
  FlaskConical,
  MoreHorizontal,
  Plus,
} from "lucide-react"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { cn } from "@/lib/utils"
import { SupplierPicker } from "@/components/suppliers/supplier-picker"
import { SupplierFormDialog } from "@/components/suppliers/supplier-form-dialog"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { labOrderFollowUpMessage } from "@/lib/whatsapp"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ExportButton } from "@/components/ui/export-button"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
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
import { formatDT, parseAmountInput, todayLocalIso } from "@/lib/format"

// The four lifecycle stages a lab work order moves through (mirrors the backend enum).
type LabOrderStatus = "Sent" | "InProgress" | "Received" | "Fitted"


const ALL_STATUSES = "all"

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

/**
 * The same date with the year dropped when it is the current one — the table's three date columns cost 329 px of
 * an 1086 px budget, and « 2026 » is repeated on every row of every column all year.
 *
 * <p>The year comes back for a bon from another year, so « 18 déc. 2025 » stays unambiguous rather than reading as
 * this December. Compared on the PARSED year, not `iso.slice(0, 4)`: an instant just before UTC midnight on 31
 * December is already the next year in Tunisia, which is the year `format` below would print.</p>
 */
function formatDateFrCompact(iso?: string | null): string {
  if (!iso) return "—"
  try {
    const date = parseISO(iso)
    const thisYear = Number(todayLocalIso().slice(0, 4))
    return format(date, date.getFullYear() === thisYear ? "d MMM" : "d MMM yyyy", { locale: fr })
  } catch {
    return "—"
  }
}

// Tunisian dinar through the app's one money formatter; "—" when no cost recorded (AC-P6.18).
// It used to interpolate `toFixed(3)` by hand, which printed a period and no thousands grouping — every other
// amount in the product reads « 1 234,500 DT ». `formatDT` treats null as 0, hence the explicit null branch:
// « 0,000 DT » and « pas de coût saisi » are different facts.
function formatCost(cost?: number | null): string {
  return cost != null ? formatDT(cost) : "—"
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

/**
 * A money amount typed by hand, or **null when the field is empty** — an optional cost.
 *
 * <p>Delegates to the shared {@link parseAmountInput} (J8) rather than carrying its own comma swap. It had one,
 * and that copy was weaker in two ways that matter here: it replaced only the **first** comma (so « 1,2,3 » read
 * as `1.2`) and stripped no whitespace (so « 1 200,500 » pasted back out of this very app failed). A helper
 * retyped per screen is the defect J8 exists to end — this wrapper keeps only what is genuinely local, which is
 * the empty-means-null contract the lab-order cost needs and a plain amount field does not.</p>
 */
function parseAmountOrNull(value: string): number | null {
  if (value.trim() === "") return null
  const n = parseAmountInput(value)
  return Number.isFinite(n) ? n : null
}

/** Column widths the loading skeleton mirrors, in the table's own order (10 columns). */
// Patient · Travail · Prothésiste · Dent · Envoyé · Prévu · Reçu · Coût · Statut · Actions.
const LAB_COLUMN_WIDTHS = [
  "w-[13%]", "w-[18%]", "w-[14%]", "w-[5%]", "w-[8%]", "w-[8%]", "w-[8%]", "w-[8%]", "w-[12%]", "w-[6%]",
] as const

/**
 * Native <select> styled to match the shadcn Input primitive.
 *
 * `coarse:min-h-11` is the 44px touch floor. `globals.css` raises `input`, `textarea` and
 * `[data-slot="select-trigger"]` on a coarse pointer but never a bare `<select>`, so these controls — including
 * the stage picker that IS this page's main action — sat at 32–36px on the tablet at the chair. `min-height`
 * beats `height`, so the `h-8` variant below stays compact on a mouse and still clears the floor on a finger.
 */
const SELECT_CLASS =
  "border-input h-9 w-full min-w-0 rounded-md border bg-transparent px-3 py-1 text-base shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 coarse:min-h-11 md:text-sm"

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
  const [patientPickerOpen, setPatientPickerOpen] = useState(false)
  // The laboratory as a fournisseur — the number « Relancer le labo » needs. It sits BESIDE the free-text
  // prothésiste rather than replacing it: the name is what is printed on the bon, and a lab used once must be
  // recordable without first filing a contact.
  const [supplierId, setSupplierId] = useState<string | null>(null)
  const [supplierCreateOpen, setSupplierCreateOpen] = useState(false)
  const [supplierReloadKey, setSupplierReloadKey] = useState(0)

  const selectedPatient = patients.find((p) => p.id === patientId)
  const selectedPatientName = selectedPatient ? `${selectedPatient.firstName} ${selectedPatient.lastName}`.trim() : ""

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
      setSupplierId(editingOrder.supplierId ?? null)
    } else {
      setPatientId("")
      setProsthetist("")
      setWorkDescription("")
      setToothNumber("")
      setSentDate("")
      setExpectedDate("")
      setCost("")
      setNotes("")
      setSupplierId(null)
    }
    setErrors({})
  }, [editingOrder, open])

  /**
   * The two fields are different facts — the name is **printed on the bon**, the fiche is how the lab is reached —
   * but a lab that already has a fiche should not have to be named twice. Filing one inline already did this;
   * picking an existing one did not, so the common case was retyping a name the app had just been given. Never
   * overwrites what was typed, and clearing the fiche leaves the printed name alone: it is required, and the bon
   * still has to say who made the work.
   */
  const adoptSupplierName = (name: string) =>
    setProsthetist((current) => (current.trim() ? current : name))

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
      cost: parseAmountOrNull(cost),
      notes: notes.trim() || null,
      // Replace-semantics like every other field of this payload — sending null detaches the laboratory, which
      // is the « ce n'était pas ce labo » correction. Deliberately NOT the tri-state the stock item uses.
      supplierId,
    }

    try {
      setSaving(true)
      if (editingOrder) {
        const updated = await labOrdersApi.update(editingOrder.id, common)
        toast.success("Bon de laboratoire mis à jour")
        // A bon routinely arrives before the labo's facture does — received with no coût, then edited to enter
        // it. That edit is what posts the dépense, so it is what has to say so.
        if (!editingOrder.expenseId && updated.expenseId) {
          toast.success(`Dépense enregistrée en caisse : ${formatCost(updated.cost)} — Laboratoire`)
        }
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
      <DialogContent className="md:max-w-md">
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
              /*
                A searchable Popover+Command, following `create-appointment-dialog.tsx`. It replaces a native
                `<select>` holding the clinic's entire patient list — unfilterable, and at `h-9` under the 44px
                touch floor, since `globals.css` raises inputs and select TRIGGERS but not a bare `<select>`.

                `modal` because the parent Dialog kills pointer events outside its content; without it the
                portalled list can only be driven by keyboard.
              */
              <Popover open={patientPickerOpen} onOpenChange={setPatientPickerOpen} modal>
                <PopoverTrigger asChild>
                  <Button
                    id="patient"
                    type="button"
                    variant="outline"
                    role="combobox"
                    aria-expanded={patientPickerOpen}
                    className="h-10 w-full justify-between font-normal"
                  >
                    <span className={cn("truncate", !patientId && "text-muted-foreground")}>
                      {selectedPatientName || "Sélectionner un patient"}
                    </span>
                    <ChevronsUpDown className="ms-2 h-4 w-4 shrink-0 opacity-50" aria-hidden="true" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="p-0" align="start" style={{ width: "var(--radix-popover-trigger-width)" }}>
                  <Command>
                    <CommandInput placeholder="Rechercher un patient…" />
                    <CommandList>
                      <CommandEmpty>Aucun patient trouvé.</CommandEmpty>
                      <CommandGroup>
                        {patients.map((p) => {
                          const fullName = `${p.firstName} ${p.lastName}`.trim()
                          return (
                            <CommandItem
                              key={p.id}
                              value={fullName}
                              onSelect={() => {
                                setPatientId(p.id)
                                setPatientPickerOpen(false)
                              }}
                            >
                              <Check
                                className={cn("me-2 h-4 w-4", patientId === p.id ? "opacity-100" : "opacity-0")}
                              />
                              {fullName}
                            </CommandItem>
                          )
                        })}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
            )}
            {errors.patientId && <p className="text-xs text-destructive">{errors.patientId}</p>}
          </div>

          {/* Deliberately ABOVE the prothésiste name it prefills. `adoptSupplierName` only writes into an empty
              field, so asked second it almost never fired — the common case was retyping a name the app had just
              been given. Asked first, the name below arrives already filled. */}
          <div className="space-y-2">
            <Label htmlFor="lab-supplier">Fiche fournisseur (pour le contacter)</Label>
            <SupplierPicker
              id="lab-supplier"
              value={supplierId}
              onChange={(id, supplier) => {
                setSupplierId(id)
                if (supplier) adoptSupplierName(supplier.name)
              }}
              selectedFallback={
                editingOrder?.supplierId && editingOrder.supplierName
                  ? { id: editingOrder.supplierId, name: editingOrder.supplierName }
                  : null
              }
              onCreateNew={() => setSupplierCreateOpen(true)}
              reloadKey={supplierReloadKey}
            />
            <p className="text-xs text-muted-foreground">
              Facultatif. Sans fiche, le bon garde le nom saisi ci-dessous mais ne pourra pas être relancé par
              WhatsApp.
            </p>
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

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="toothNumber">Dent (FDI)</Label>
              {/* A tooth number is a LABEL, not a quantity: 46 is a name, not forty-six of anything. As
                  `type="number"` it carried spinners that invite « 47 » to be reached by stepping, and an
                  accidental scroll over the focused field silently changed which tooth the bon is for. */}
              <Input
                id="toothNumber"
                type="text"
                inputMode="numeric"
                maxLength={2}
                placeholder="Optionnel"
                value={toothNumber}
                onChange={(e) => setToothNumber(e.target.value)}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="cost">Coût (DT)</Label>
              {/* Text + `inputMode="decimal"`, parsed by `parseAmountOrNull`: the app prints « 90,500 DT », and
                  `type="number"` refuses a comma outright — the browser reports an empty value and the cost is
                  dropped without a word. */}
              <Input
                id="cost"
                type="text"
                inputMode="decimal"
                placeholder="Optionnel"
                value={cost}
                onChange={(e) => setCost(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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

      {/* « + Créer un fournisseur » from inside the picker — selects the new fiche straight away so nothing
          already typed into this bon is lost to a detour through /fournisseurs. */}
      <SupplierFormDialog
        open={supplierCreateOpen}
        onOpenChange={setSupplierCreateOpen}
        editing={null}
        categories={[]}
        onSaved={(created) => {
          setSupplierId(created.id)
          setSupplierReloadKey((k) => k + 1)
          adoptSupplierName(created.name)
        }}
      />
    </Dialog>
  )
}

export default function LabOrdersPage() {
  const [orderPage, setOrderPage] = useState<PagedResponse<LabWorkOrderDto>>(() => emptyPage<LabWorkOrderDto>())
  const orders = orderPage.items
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingOrder, setEditingOrder] = useState<LabWorkOrderDto | null>(null)
  const [statusUpdatingId, setStatusUpdatingId] = useState<string | null>(null)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [orderToDelete, setOrderToDelete] = useState<LabWorkOrderDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  // The list had no filter of any kind, which left the dashboard's « Prothèses en retard » card with nowhere truthful
  // to land. ALL_STATUSES keeps the default behaviour (the full list) unchanged.
  const [statusFilter, setStatusFilter] = useState<string>(ALL_STATUSES)

  const loadOrders = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await labOrdersApi.listPaged({
        page,
        pageSize,
        search: debouncedSearch || undefined,
        status: statusFilter === ALL_STATUSES ? undefined : statusFilter,
      })
      setOrderPage(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des bons de laboratoire"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [statusFilter, page, pageSize, debouncedSearch])

  // Debounced so a search does not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new term (or filter) must not leave the table on a page the narrowed result set no longer has.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch, statusFilter])

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

  // Dashboard drill-through (« Prothèses en retard »): ?status=Sent narrows to the work still at the laboratory.
  // window.location in an effect rather than useSearchParams — the repo's idiom. An unknown value is ignored, so a
  // stale link lands on the full list.
  useEffect(() => {
    const urlStatus = new URLSearchParams(window.location.search).get("status")
    if (urlStatus && urlStatus in STATUS_LABELS) setStatusFilter(urlStatus)
  }, [])

  // AC-P4.21/4.26 — a bon de prothèse has a status lifecycle two people drive: the assistant sends it, the
  // dentist marks it received. `laborders` was emitted by the backend from the start with nothing listening,
  // so each of them worked from a snapshot taken when their page loaded.
  useClinicRealtime(RealtimeResource.LabOrders, loadOrders)

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
      const updated = await labOrdersApi.updateStatus(order.id, status)
      toast.success(`Statut mis à jour : ${statusLabel(status)}`)
      // Receiving the work spends money, so the response says whether la caisse actually learned of it. A bon
      // with no coût cannot be posted at all — silence there would read as « c'est fait », and the dépense would
      // simply never be filed. `order.expenseId` guards the re-arrival: the lab is paid once, not once per trip.
      if (status === "Received" && !order.expenseId) {
        if (updated.expenseId) {
          toast.success(`Dépense enregistrée en caisse : ${formatCost(updated.cost)} — Laboratoire`)
        } else {
          toast.warning("Aucun coût saisi sur ce bon — rien n'a été porté en caisse.")
        }
      }
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

  /**
   * Two different facts, never one message (finding #4). The list carries BOTH a live search box and an
   * « Étape » filter, and a single « Aucun bon de laboratoire » told a dentist who mistyped a prosthetist's
   * name — or who left the filter on « Posé » — that the laboratory register was empty.
   *
   * The filtered branch deliberately offers no « Nouveau bon »: the bon probably exists, and a create button
   * there is an invitation to raise a duplicate order with the laboratory.
   */
  const hasActiveFilter = debouncedSearch !== "" || statusFilter !== ALL_STATUSES

  const clearFilters = () => {
    setSearch("")
    setStatusFilter(ALL_STATUSES)
  }

  const renderEmpty = (size: "default" | "compact") =>
    hasActiveFilter ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucun bon ne correspond à vos filtres</p>
        <Button variant="outline" size="sm" onClick={clearFilters}>
          Effacer les filtres
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={FlaskConical}
        size={size}
        title="Aucun bon de prothèse"
        description="Suivez ici les travaux confiés au laboratoire — de « Envoyé » à « Posé » — avec la dent, le prothésiste, la date prévue et le coût."
        action={
          <Button onClick={handleAddNew} className="gap-2">
            <Plus className="h-4 w-4" />
            Nouveau bon
          </Button>
        }
      />
    )

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
      <AppShell contentClassName="space-y-6">
        {/*
          The « Étape » filter, « Exporter » and « Nouveau bon » go through `PageHeader`'s own `actions` slot
          rather than a hand-rolled flex row around it. As a flex item beside a sibling the header shrank to its
          title's width, and its zone wash — which bleeds past its own box on three sides to meet the page gutter
          — was cut off with a hard vertical edge a third of the way across the page. `actions` already wraps
          below `sm:`, which is what the wrapper was there for.

          No `zone`: `PageHeader` derives it from the route now.
        */}
        <PageHeader
          title="Laboratoire"
          subtitle="Bons de prothèse — travaux envoyés au laboratoire et leur étape."
          actions={
            // `items-end` so the labelled select's BOX lines up with the buttons, not its label.
            <div className="flex flex-wrap items-end gap-2">
              <div className="space-y-1.5">
                <Label htmlFor="lab-status" className="text-sm text-muted-foreground">
                  Étape
                </Label>
                <Select value={statusFilter} onValueChange={setStatusFilter}>
                  <SelectTrigger id="lab-status" className="w-44">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_STATUSES}>Toutes</SelectItem>
                    {Object.entries(STATUS_LABELS).map(([value, label]) => (
                      <SelectItem key={value} value={value}>
                        {label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              {/*
                L5 — « Exporter » beside the primary action, never inside it: exporting is not creating. Unlike
                `/stock` and `/creances`, this page owns its own filters, so the button lives here and reads them
                directly. `debouncedSearch` (not `search`) is what the list request carried — sending the raw
                keystroke would export a set the table has not shown yet.
              */}
              <ExportButton
                path="/lab-orders/export"
                label="bons"
                compact
                params={{
                  search: debouncedSearch || undefined,
                  status: statusFilter === ALL_STATUSES ? undefined : statusFilter,
                }}
              />
              <Button onClick={handleAddNew} className="gap-2">
                <Plus className="h-4 w-4" />
                Nouveau bon
              </Button>
            </div>
          }
        />

        {/* Orders Table */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <FlaskConical className="h-5 w-5" />
              Bons de laboratoire
              <Badge variant="secondary" className="ml-2">
                {orderPage.totalCount}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {/* Retry banner (finding #2) + `loading` routed into the list rather than a lone spinner that the
                full table then replaces (finding #3). Shape from `dashboard/dashboard-section.tsx`. */}
            {error ? (
              <div
                role="status"
                className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
                <span className="min-w-0 flex-1">{error}</span>
                <Button size="sm" variant="outline" onClick={loadOrders}>
                  Réessayer
                </Button>
              </div>
            ) : (
              <div>
                <div className="mb-4">
                  <Label htmlFor="lab-orders-search" className="sr-only">
                    Rechercher un bon
                  </Label>
                  <Input
                    id="lab-orders-search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Rechercher un bon (prothésiste, description, patient)…"
                  />
                </div>
                {/*
                  Patient + travail as title and subtitle: « Ben Ali » repeats across a busy list and
                  « Couronne céramique » does not say whose, so neither identifies a bon on its own.

                  ⚠️ The stage `<select>` stays a CONTROL, as a labelled field's value — it is the action this
                  screen exists for, and it is also the status display. Putting it in the menu would hide the
                  current stage AND make advancing it two taps. Its `allowedNextStatuses` gate is the server's,
                  unchanged.
                */}
                <CardList
                  className={CARDS_ONLY_LG}
                  ariaLabel="Bons de laboratoire"
                  items={orders}
                  loading={loading}
                  getKey={(o) => o.id}
                  title={(o) => o.patientName ?? "Patient inconnu"}
                  href={(o) => `/patients/${o.patientId}`}
                  subtitle={(o) => o.workDescription}
                  status={(o) => <Badge variant={statusVariant(o.status)}>{statusLabel(o.status)}</Badge>}
                  fields={(o) => [
                    {
                      label: "Stade",
                      value: (
                        /* `w-full` + the shared class's `coarse:min-h-11`. This control exists ONLY on the
                           phone card, and advancing a prothèse's stage is what this page is for — it was
                           rendering at 32px, the smallest tap target on the screen it matters most on. */
                        <select
                          aria-label="Changer le statut"
                          className={cn(SELECT_CLASS, "h-8 w-full")}
                          value={o.status}
                          disabled={
                            statusUpdatingId === o.id || (o.allowedNextStatuses?.length ?? 0) === 0
                          }
                          onChange={(e) => handleStatusChange(o, e.target.value)}
                        >
                          <option value={o.status}>{statusLabel(o.status)}</option>
                          {(o.allowedNextStatuses ?? []).map((s) => (
                            <option key={s} value={s}>
                              {statusLabel(s)}
                            </option>
                          ))}
                        </select>
                      ),
                    },
                    { label: "Prothésiste", value: o.prosthetist },
                    { label: "Dent", value: o.toothNumber },
                    { label: "Coût", value: formatCost(o.cost) },
                    { label: "Envoyé", value: formatDateFr(o.sentDate) },
                    { label: "Prévu", value: formatDateFr(o.expectedDate) },
                    { label: "Reçu", value: formatDateFr(o.receivedDate) },
                  ]}
                  actions={(o) => (
                    /* « Relancer le labo » is here rather than beside the prothésiste's name for the reason the
                       table documents, and it was WORSE on a card: measured at 320 px the icon rendered from
                       x=312 to 348 inside a field cell ending at 239 — 8 of its 36 px on screen, and the phone
                       is where a laboratory actually gets called. Only when a fiche fournisseur is linked; the
                       free-text name alone has no number behind it. */
                    <div className="flex items-center gap-1">
                      {o.supplierId ? (
                        <WhatsAppAction
                          phoneE164={o.supplierPhoneE164}
                          contactName={o.supplierName ?? o.prosthetist}
                          message={labOrderFollowUpMessage(
                            o.workDescription,
                            o.patientName,
                            formatDateFr(o.expectedDate),
                          )}
                        />
                      ) : null}
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" aria-label={`Actions pour le bon de ${o.patientName ?? "ce patient"}`}>
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem onSelect={() => handleEdit(o)}>Modifier</DropdownMenuItem>
                          <DropdownMenuItem
                            className="text-destructive focus:text-destructive"
                            onSelect={() => handleDelete(o)}
                          >
                            Supprimer
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </div>
                  )}
                  empty={renderEmpty("compact")}
                />
                <Table containerClassName={TABLE_ONLY_LG}>
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
                    {loading ? (
                      Array.from({ length: 5 }).map((_, row) => (
                        <TableRow key={`skeleton-${row}`}>
                          {LAB_COLUMN_WIDTHS.map((width, col) => (
                            <TableCell key={col}>
                              <div
                                className={`h-5 animate-pulse rounded bg-muted ${width}`}
                                role={row === 0 && col === 0 ? "status" : undefined}
                                aria-label={row === 0 && col === 0 ? "Chargement des bons" : undefined}
                              />
                            </TableCell>
                          ))}
                        </TableRow>
                      ))
                    ) : orders.length === 0 ? (
                      <TableEmptyRow colSpan={10}>{renderEmpty("default")}</TableEmptyRow>
                    ) : (
                      orders.map((order) => (
                        <TableRow key={order.id}>
                          {/* AC-23: the bon names a patient and, until now, offered no way to reach them —
                              the one thing a prothésiste's call always needs. « Voir le RDV » goes on its own
                              line rather than inline: beside the name it widened this column by its own length. */}
                          <TableCell className="max-w-[9.5rem] font-medium">
                            <Link
                              href={`/patients/${order.patientId}`}
                              className="block truncate text-foreground underline-offset-4 hover:underline"
                            >
                              {order.patientName ?? "Patient inconnu"}
                            </Link>
                            {order.appointmentId && (
                              <Link
                                href={`/appointments?appointmentId=${order.appointmentId}`}
                                className="text-xs text-muted-foreground underline-offset-4 hover:underline"
                              >
                                Voir le RDV
                              </Link>
                            )}
                          </TableCell>
                          {/* Capped and truncated, with the full text on hover: sized by their longest row these
                              two took 452 px of the table's 1402 and were the reason it could not fit a laptop. */}
                          <TableCell className="max-w-[12rem]">
                            <span className="block truncate" title={order.workDescription}>
                              {order.workDescription}
                            </span>
                          </TableCell>
                          <TableCell className="max-w-[9.5rem] text-muted-foreground">
                            <span className="block truncate" title={order.prosthetist}>
                              {order.prosthetist}
                            </span>
                          </TableCell>
                          <TableCell className="text-muted-foreground">{order.toothNumber ?? "—"}</TableCell>
                          <TableCell className="whitespace-nowrap text-muted-foreground">
                            {formatDateFrCompact(order.sentDate)}
                          </TableCell>
                          <TableCell className="whitespace-nowrap text-muted-foreground">
                            {formatDateFrCompact(order.expectedDate)}
                          </TableCell>
                          <TableCell className="whitespace-nowrap text-muted-foreground">
                            {formatDateFrCompact(order.receivedDate)}
                          </TableCell>
                          <TableCell className="whitespace-nowrap text-muted-foreground">
                            {formatCost(order.cost)}
                          </TableCell>
                          {/* The stage lives in the « Statut » column because the select IS the status display —
                              a Badge here beside it printed the same word twice and squeezed the control that sets
                              it down to a bare chevron. `allowedNextStatuses` is the domain's table (AC-P2.40): the
                              client never re-derives it, and a legacy row in an unmapped state gets an empty list
                              and a disabled control rather than a transition the server would refuse. */}
                          <TableCell>
                            <select
                              aria-label="Changer le statut"
                              className={cn(SELECT_CLASS, "h-8 w-full min-w-[6.5rem]")}
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
                          </TableCell>
                          {/* « Relancer le labo » sits HERE, beside the ⋯ trigger, which is where
                              `WhatsAppAction`'s own contract puts it. Inline after the prothésiste's name a long
                              name pushed it toward the clip; anchored to the row's right edge it cannot be hidden
                              by content. Two labelled buttons became one menu — the page's card view and
                              `suppliers-table` already use exactly this — taking the column from 268 px to ~100. */}
                          <TableCell className="text-right">
                            <div className="flex items-center justify-end gap-1">
                              {order.supplierId ? (
                                <WhatsAppAction
                                  phoneE164={order.supplierPhoneE164}
                                  contactName={order.supplierName ?? order.prosthetist}
                                  message={labOrderFollowUpMessage(
                                    order.workDescription,
                                    order.patientName,
                                    formatDateFr(order.expectedDate),
                                  )}
                                />
                              ) : null}
                              <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                  <Button
                                    variant="ghost"
                                    size="icon"
                                    aria-label={`Actions pour le bon de ${order.patientName ?? "ce patient"}`}
                                  >
                                    <MoreHorizontal className="h-4 w-4" />
                                  </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end">
                                  <DropdownMenuItem onSelect={() => handleEdit(order)}>Modifier</DropdownMenuItem>
                                  <DropdownMenuItem
                                    className="text-destructive focus:text-destructive"
                                    onSelect={() => handleDelete(order)}
                                  >
                                    Supprimer
                                  </DropdownMenuItem>
                                </DropdownMenuContent>
                              </DropdownMenu>
                            </div>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
                {/* Hidden while the skeletons are up: the pager reads its counts from an empty page, so it
                    would print « Aucun … » under rows that are still loading. */}
                {!loading && (
                  <DataTablePagination
                    page={orderPage}
                    onPageChange={setPage}
                    onPageSizeChange={setPageSize}
                    loading={loading}
                    label={["bon", "bons"]}
                  />
                )}
              </div>
            )}
          </CardContent>
        </Card>

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
            {/* The title names the object; « Êtes-vous sûr ? » said nothing this dialog exists to say. */}
            <AlertDialogTitle>Supprimer ce bon de prothèse ?</AlertDialogTitle>
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
      </AppShell>
    </ClinicGuard>
  )
}
