"use client"

import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Trash2, Plus, Search, X, AlertTriangle } from "lucide-react"
import { cn } from "@/lib/utils"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"
import type { ProcedureTypeDto, DentalRecordDto, DentalActInput, PatientDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import {
  CONDITION_ORDER,
  conditionStyle,
  SURFACE_ORDER,
  SURFACE_LABELS,
  parseSurfaces,
  serializeSurfaces,
} from "@/components/odontogram-conditions"
import { ADULT_FDI, CHILD_FDI } from "@/components/tooth-multiselect"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"

// Sentinel value for the "no resulting condition" option in the État résultant Select
// (Radix Select forbids an empty-string item value).
const NO_CONDITION = "__none__"
// Sentinel for "not linked to a treatment-plan step".
const NO_PLAN_ITEM = "__none__"

/** An open treatment-plan step offered for linking a dental record (closes the plan→record loop). */
export interface PlanItemOption {
  itemId: string
  planId: string
  label: string
  /** Plan-step designation — prefilled into the record act on link (P0-1, carry-forward). */
  designationFr?: string
  /** Plan-step planned cost — prefilled into the record act on link. */
  plannedCost?: number
  /** Plan-step teeth — prefilled into the record act on link. */
  toothNumbers?: number[]
}

interface ActRow {
  procedureTypeId: string | null
  procedureName: string
  cost: string
  toothNumbers: number[]
  resultingCondition: string | null
  surfaces: Set<string>
  note: string
}

const emptyAct = (): ActRow => ({
  procedureTypeId: null,
  procedureName: "",
  cost: "",
  toothNumbers: [],
  resultingCondition: null,
  surfaces: new Set<string>(),
  note: "",
})

interface PatientRecordModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patientName?: string
  patientId?: string
  record?: DentalRecordDto | null // Record to edit, null for a new record
  /** True when the record is already billed by a (non-cancelled) invoice — its payment is invoice-managed (AC-8). */
  isInvoiced?: boolean
  /** Patient — used to surface allergy / flag / medical-history alerts at the point of care. */
  patient?: PatientDto | null
  /** Open treatment-plan steps the record can complete (marks the step "réalisé" on save). */
  planItems?: PlanItemOption[]
  onSuccess?: () => void
}

export function PatientRecordModal({
  open,
  onOpenChange,
  patientName: initialPatientName = "",
  patientId,
  record,
  isInvoiced = false,
  patient,
  planItems = [],
  onSuccess,
}: PatientRecordModalProps) {
  const [patientName, setPatientName] = useState(initialPatientName)
  const [interventionDate, setInterventionDate] = useState(new Date().toISOString().split("T")[0])
  const [isAdultTeeth, setIsAdultTeeth] = useState(true)
  const [amountPaid, setAmountPaid] = useState("")
  const [notes, setNotes] = useState<string[]>([])
  const [importantNotes, setImportantNotes] = useState<string[]>([])
  const [acts, setActs] = useState<ActRow[]>([emptyAct()])
  const [focusedActIndex, setFocusedActIndex] = useState(0)
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [linkedPlanItemId, setLinkedPlanItemId] = useState<string>(NO_PLAN_ITEM)
  const [loading, setLoading] = useState(false)

  // Active point-of-care alerts for this patient (allergies / flags / medical history).
  const activeFlags = (patient?.flags ?? []).filter((f) => f.isActive)
  const hasAlerts =
    Boolean(patient?.allergies?.trim()) || activeFlags.length > 0 || Boolean(patient?.medicalHistory?.trim())

  // Load the active procedure catalog (the "Mes actes" picker source) when the modal opens.
  useEffect(() => {
    if (!open) return
    procedureTypesApi
      .list(false)
      .then((data) => setProcedureTypes(data || []))
      .catch(() => setProcedureTypes([]))
  }, [open])

  // Reset (create) or prefill (edit) the form when the modal opens.
  useEffect(() => {
    if (!open) return
    setPatientName(initialPatientName)
    setFocusedActIndex(0)
    setLinkedPlanItemId(NO_PLAN_ITEM)

    if (record) {
      setInterventionDate(new Date(record.interventionDate).toISOString().split("T")[0])
      setIsAdultTeeth(record.isAdultTeeth)
      setAmountPaid(String(record.amountPaid))
      setNotes([...record.notes])
      setImportantNotes([...record.importantNotes])
      setActs(
        record.acts && record.acts.length > 0
          ? record.acts.map((a) => ({
              procedureTypeId: a.procedureTypeId ?? null,
              procedureName: a.procedureName,
              cost: String(a.cost),
              toothNumbers: a.toothNumbers ?? [],
              resultingCondition: a.resultingCondition ?? null,
              surfaces: parseSurfaces(a.surfaces),
              note: a.note ?? "",
            }))
          : [emptyAct()],
      )
    } else {
      setInterventionDate(new Date().toISOString().split("T")[0])
      setIsAdultTeeth(true)
      setAmountPaid("")
      setNotes([])
      setImportantNotes([])
      setActs([emptyAct()])
    }
  }, [open, initialPatientName, record])

  const updateAct = (index: number, patch: Partial<ActRow>) => {
    setActs((prev) => prev.map((a, i) => (i === index ? { ...a, ...patch } : a)))
  }
  const addAct = () => {
    setActs((prev) => [...prev, emptyAct()])
    setFocusedActIndex(acts.length) // the appended act lands at the current length
  }
  const removeAct = (index: number) => {
    setActs((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))
    setFocusedActIndex((prev) => {
      if (acts.length <= 1) return 0
      if (index < prev) return prev - 1
      if (index === prev) return Math.min(prev, acts.length - 2)
      return prev
    })
  }

  // Linking a plan step carries its designation / cost / teeth into the focused act row, so the dentist
  // does not retype what the plan already knows (P0-1). Only an empty row is prefilled — a value the
  // user already typed is never overwritten.
  const handlePlanItemLink = (value: string) => {
    setLinkedPlanItemId(value)
    if (value === NO_PLAN_ITEM) return
    const item = planItems.find((p) => p.itemId === value)
    if (!item) return
    setActs((prev) =>
      prev.map((a, i) => {
        if (i !== focusedActIndex) return a
        const isEmpty = a.procedureName.trim() === "" && a.cost.trim() === "" && a.toothNumbers.length === 0
        if (!isEmpty) return a
        return {
          ...a,
          procedureName: item.designationFr ?? a.procedureName,
          cost: item.plannedCost != null && item.plannedCost > 0 ? String(item.plannedCost) : a.cost,
          toothNumbers: item.toothNumbers && item.toothNumbers.length > 0 ? [...item.toothNumbers] : a.toothNumbers,
        }
      }),
    )
  }

  // Chart-driven tooth selection: toggle a tooth in the currently focused act.
  const toggleTooth = (n: number) => {
    setActs((prev) =>
      prev.map((a, i) => {
        if (i !== focusedActIndex) return a
        const has = a.toothNumbers.includes(n)
        return {
          ...a,
          toothNumbers: has ? a.toothNumbers.filter((t) => t !== n) : [...a.toothNumbers, n].sort((x, y) => x - y),
        }
      }),
    )
  }
  const clearActTeeth = (index: number) => updateAct(index, { toothNumbers: [] })

  const selectProcedureType = (index: number, pt: ProcedureTypeDto) => {
    setActs((prev) =>
      prev.map((a, i) =>
        i === index
          ? {
              ...a,
              procedureTypeId: pt.id,
              procedureName: pt.name,
              // Prefill the fee only when empty; prefill the resulting condition from the procedure.
              cost: a.cost.trim() === "" && pt.defaultCost != null ? String(pt.defaultCost) : a.cost,
              resultingCondition: pt.resultingCondition ?? a.resultingCondition,
            }
          : a,
      ),
    )
    setPickerOpenIndex(null)
  }

  const detachProcedure = (index: number) => updateAct(index, { procedureTypeId: null })

  const toggleActSurface = (index: number, s: string) => {
    setActs((prev) =>
      prev.map((a, i) => {
        if (i !== index) return a
        const next = new Set(a.surfaces)
        if (next.has(s)) next.delete(s)
        else next.add(s)
        return { ...a, surfaces: next }
      }),
    )
  }

  const total = acts.reduce((sum, a) => {
    const c = Number(a.cost)
    return Number.isFinite(c) ? sum + c : sum
  }, 0)

  // Prefill amount paid to the full total only while it hasn't been set yet (never overwrites an entry).
  useEffect(() => {
    if (total > 0 && amountPaid.trim() === "") setAmountPaid(String(total))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [total])

  const reste = Math.max(0, total - (Number.parseFloat(amountPaid) || 0))

  // Per-tooth paint for the chart: focused act wins; otherwise the latest non-focused act's condition color.
  const toothPaint = new Map<number, ToothPaint>()
  acts.forEach((act, i) => {
    const focused = i === focusedActIndex
    const color = act.resultingCondition ? conditionStyle(act.resultingCondition).color : null
    for (const n of act.toothNumbers) {
      const prev = toothPaint.get(n)
      const count = (prev?.count ?? 0) + 1
      if (focused) {
        toothPaint.set(n, { focused: true, color, count })
      } else if (prev?.focused) {
        toothPaint.set(n, { focused: true, color: prev.color, count })
      } else {
        toothPaint.set(n, { focused: false, color, count })
      }
    }
  })

  const handleSave = async () => {
    if (!patientId) {
      toast.error("Identifiant du patient requis")
      return
    }

    const parsedActs: DentalActInput[] = acts
      .map((a) => ({
        procedureTypeId: a.procedureTypeId,
        procedureName: a.procedureName.trim(),
        cost: Number(a.cost) || 0,
        toothNumbers: a.toothNumbers,
        resultingCondition: a.resultingCondition, // null when "Aucun"
        surfaces: serializeSurfaces(a.surfaces) || null,
        note: a.note.trim() || null,
      }))
      .filter((a) => a.procedureName !== "")

    if (parsedActs.length === 0) {
      toast.error("Ajoutez au moins un acte", { description: "Chaque acte nécessite une désignation." })
      return
    }

    // Teeth must match the selected dentition (backend also enforces).
    const allowed = new Set(isAdultTeeth ? ADULT_FDI : CHILD_FDI)
    for (const a of parsedActs) {
      if (a.toothNumbers.some((t) => !allowed.has(t))) {
        toast.error("Dents incompatibles", {
          description: `Les dents d'un acte ne correspondent pas à la dentition ${isAdultTeeth ? "adulte" : "enfant"}.`,
        })
        return
      }
    }

    setLoading(true)
    try {
      const linkedItem = planItems.find((p) => p.itemId === linkedPlanItemId)
      const recordData = {
        interventionDate,
        amountPaid: Number.parseFloat(amountPaid) || 0,
        isAdultTeeth,
        notes: notes.filter((n) => n.trim()).map((n) => n.trim()),
        importantNotes: importantNotes.filter((n) => n.trim()).map((n) => n.trim()),
        acts: parsedActs,
        treatmentPlanId: linkedItem?.planId ?? null,
        treatmentPlanItemId: linkedItem?.itemId ?? null,
      }

      if (record) {
        await dentalRecordsApi.update(patientId, record.id, recordData)
        toast.success("Fiche dentaire mise à jour")
      } else {
        await dentalRecordsApi.create(patientId, recordData)
        toast.success("Fiche dentaire enregistrée")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      toast.error("Erreur lors de l'enregistrement", {
        description: err instanceof ApiError ? err.message : "Une erreur s'est produite.",
      })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{record ? "Modifier la fiche médicale" : "Ajouter une fiche médicale"}</DialogTitle>
          <DialogDescription>
            Détaillez les actes réalisés. L'état résultant de chaque acte alimente l'odontogramme du patient.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-5">
          {/* Point-of-care medical alerts — surfaced before treatment (safety). */}
          {hasAlerts && (
            <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/40">
              <p className="flex items-center gap-1.5 text-sm font-semibold text-amber-800 dark:text-amber-200">
                <AlertTriangle className="h-4 w-4" /> Alertes médicales
              </p>
              <div className="mt-2 space-y-1.5 text-xs">
                {patient?.allergies?.trim() && (
                  <p className="text-red-700 dark:text-red-300">
                    <span className="font-semibold">Allergies :</span> {patient.allergies}
                  </p>
                )}
                {activeFlags.length > 0 && (
                  <div className="flex flex-wrap items-center gap-1.5">
                    {activeFlags.map((f) => (
                      <Badge key={f.id} variant="destructive" className="text-[10px]">
                        {f.description || f.flagType}
                      </Badge>
                    ))}
                  </div>
                )}
                {patient?.medicalHistory?.trim() && (
                  <p className="text-amber-800 dark:text-amber-200">
                    <span className="font-semibold">Antécédents :</span> {patient.medicalHistory}
                  </p>
                )}
              </div>
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="patient-name">Patient</Label>
            <Input id="patient-name" value={patientName} readOnly className="font-medium" />
          </div>

          {/* Optional link to a scheduled treatment-plan step — marks it "réalisé" on save. */}
          {planItems.length > 0 && (
            <div className="space-y-1.5">
              <Label htmlFor="plan-item">
                Acte du plan de traitement <span className="font-normal text-muted-foreground">(optionnel)</span>
              </Label>
              <Select value={linkedPlanItemId} onValueChange={handlePlanItemLink} disabled={loading}>
                <SelectTrigger id="plan-item">
                  <SelectValue placeholder="Lier cette fiche à un acte planifié" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NO_PLAN_ITEM}>Aucun</SelectItem>
                  {planItems.map((p) => (
                    <SelectItem key={p.itemId} value={p.itemId}>
                      {p.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                L'acte planifié sera marqué « réalisé » et lié à cette fiche.
              </p>
            </div>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="date">Date</Label>
              <Input
                id="date"
                type="date"
                value={interventionDate}
                onChange={(e) => setInterventionDate(e.target.value)}
                disabled={loading}
              />
            </div>
            <div className="space-y-1.5">
              <Label>Dentition</Label>
              <div className="flex items-center gap-1 rounded-lg bg-muted p-1 w-fit">
                <Button
                  type="button"
                  variant={isAdultTeeth ? "default" : "ghost"}
                  size="sm"
                  className="h-8 px-4 text-xs"
                  onClick={() => setIsAdultTeeth(true)}
                  disabled={loading}
                >
                  Adulte
                </Button>
                <Button
                  type="button"
                  variant={!isAdultTeeth ? "default" : "ghost"}
                  size="sm"
                  className="h-8 px-4 text-xs"
                  onClick={() => setIsAdultTeeth(false)}
                  disabled={loading}
                >
                  Enfant
                </Button>
              </div>
            </div>
          </div>

          {/* Visual dental chart — click a tooth to toggle it in the focused act. */}
          <div className="space-y-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Label>Schéma dentaire</Label>
              <span className="text-xs text-muted-foreground">
                Cliquez une dent pour l'ajouter/retirer de l'acte ciblé.
              </span>
            </div>
            <RecordToothChart isAdult={isAdultTeeth} paint={toothPaint} onToggleTooth={toggleTooth} disabled={loading} />
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px]">
              {CONDITION_ORDER.filter((c) => c !== "Sain").map((c) => (
                <span key={c} className="flex items-center gap-1">
                  <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: conditionStyle(c).color }} />
                  <span className="text-muted-foreground">{conditionStyle(c).label}</span>
                </span>
              ))}
            </div>
          </div>

          {/* Acts */}
          <div className="space-y-2">
            <Label>Actes</Label>
            <div className="space-y-3">
              {acts.map((act, index) => {
                const chipStyle = conditionStyle(act.resultingCondition ?? "Sain")
                const isFocused = index === focusedActIndex
                return (
                  <div
                    key={index}
                    onClick={() => setFocusedActIndex(index)}
                    className={cn(
                      "cursor-pointer space-y-2 rounded-lg border p-3 transition-colors",
                      isFocused ? "border-primary ring-2 ring-primary/60" : "hover:border-muted-foreground/40",
                    )}
                  >
                    <div className="flex items-center justify-between">
                      <span className="flex items-center gap-1.5 text-xs font-medium">
                        <span className={cn("h-2 w-2 rounded-full", isFocused ? "bg-primary" : "bg-muted-foreground/40")} />
                        Acte {index + 1}
                        {isFocused && <Badge variant="secondary" className="ml-1 text-[10px]">Ciblé</Badge>}
                      </span>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="h-7 w-7"
                        onClick={(e) => {
                          e.stopPropagation()
                          removeAct(index)
                        }}
                        disabled={loading || acts.length === 1}
                        aria-label="Supprimer l'acte"
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>

                    <div className="space-y-1">
                      <div className="flex items-center gap-2">
                        <Input
                          value={act.procedureName}
                          onChange={(e) => updateAct(index, { procedureName: e.target.value })}
                          placeholder="Désignation de l'acte (ou choisir au catalogue)"
                          disabled={loading}
                        />
                        <Popover
                          open={pickerOpenIndex === index}
                          onOpenChange={(o) => setPickerOpenIndex(o ? index : null)}
                          modal
                        >
                          <PopoverTrigger asChild>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-9 px-3 shrink-0"
                              disabled={loading}
                              title="Choisir un acte du catalogue"
                            >
                              <Search className="h-4 w-4" />
                              <span className="sr-only">Choisir un acte du catalogue</span>
                            </Button>
                          </PopoverTrigger>
                          <PopoverContent className="p-0 w-80" align="end">
                            <Command>
                              <CommandInput placeholder="Rechercher un acte…" />
                              <CommandList>
                                <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                <CommandGroup heading="Mes actes">
                                  {procedureTypes.map((pt) => (
                                    <CommandItem
                                      key={pt.id}
                                      value={`pt ${pt.name}`}
                                      onSelect={() => selectProcedureType(index, pt)}
                                    >
                                      <div className="flex flex-col">
                                        <span className="text-sm font-medium">{pt.name}</span>
                                        {pt.defaultCost != null && pt.defaultCost > 0 && (
                                          <span className="text-xs text-muted-foreground">{formatDT(pt.defaultCost)}</span>
                                        )}
                                      </div>
                                    </CommandItem>
                                  ))}
                                </CommandGroup>
                              </CommandList>
                            </Command>
                          </PopoverContent>
                        </Popover>
                      </div>
                      {act.procedureTypeId && (
                        <Badge variant="secondary" className="gap-1 text-xs">
                          Catalogue
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation()
                              detachProcedure(index)
                            }}
                            className="ml-1 rounded-full hover:text-destructive"
                            title="Détacher du catalogue (texte libre)"
                          >
                            <X className="h-3 w-3" />
                          </button>
                        </Badge>
                      )}
                    </div>

                    {/* Teeth — set from the chart above (focus this act, then click teeth). */}
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="text-xs text-muted-foreground">Dents :</span>
                      {act.toothNumbers.length > 0 ? (
                        <>
                          {act.toothNumbers.map((t) => (
                            <Badge key={t} variant="secondary" className="text-xs">
                              {t}
                            </Badge>
                          ))}
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="h-6 px-2 text-xs"
                            onClick={(e) => {
                              e.stopPropagation()
                              clearActTeeth(index)
                            }}
                            disabled={loading}
                          >
                            Vider
                          </Button>
                        </>
                      ) : (
                        <span className="text-xs italic text-muted-foreground">
                          {isFocused ? "cliquez une dent sur le schéma" : "aucune — ciblez cet acte pour en ajouter"}
                        </span>
                      )}
                    </div>

                    <div className="flex items-center gap-1.5">
                      <span className="text-xs text-muted-foreground">Coût (DT)</span>
                      <Input
                        type="number"
                        min="0"
                        step="0.001"
                        value={act.cost}
                        onChange={(e) => updateAct(index, { cost: e.target.value })}
                        className="w-28"
                        disabled={loading}
                      />
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs text-muted-foreground">État résultant</span>
                      <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs">
                        <span
                          className={cn(
                            "h-2.5 w-2.5 rounded-full border",
                            act.resultingCondition ? chipStyle.swatch : "bg-background",
                          )}
                        />
                        {act.resultingCondition ? chipStyle.label : "Aucun"}
                      </span>
                      <Select
                        value={act.resultingCondition ?? NO_CONDITION}
                        onValueChange={(v) => updateAct(index, { resultingCondition: v === NO_CONDITION ? null : v })}
                        disabled={loading}
                      >
                        <SelectTrigger className="h-8 w-44 text-xs">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value={NO_CONDITION}>Aucun</SelectItem>
                          {CONDITION_ORDER.map((c) => (
                            <SelectItem key={c} value={c}>
                              {conditionStyle(c).label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs text-muted-foreground">Faces</span>
                      <div className="flex items-center gap-1">
                        {SURFACE_ORDER.map((s) => (
                          <Button
                            key={s}
                            type="button"
                            size="sm"
                            variant={act.surfaces.has(s) ? "default" : "outline"}
                            className="h-8 w-8 p-0 text-xs"
                            onClick={() => toggleActSurface(index, s)}
                            disabled={loading}
                            title={SURFACE_LABELS[s]}
                          >
                            {s}
                          </Button>
                        ))}
                      </div>
                      <Input
                        value={act.note}
                        onChange={(e) => updateAct(index, { note: e.target.value })}
                        placeholder="Note (optionnel)"
                        className="h-8 min-w-[8rem] flex-1 text-xs"
                        disabled={loading}
                      />
                    </div>
                  </div>
                )
              })}
            </div>
            <Button type="button" variant="outline" size="sm" onClick={addAct} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter un acte
            </Button>
          </div>

          {/* Totals + payment */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="paid">Montant payé (DT)</Label>
              <Input
                id="paid"
                type="number"
                min="0"
                step="0.001"
                value={amountPaid}
                onChange={(e) => setAmountPaid(e.target.value)}
                placeholder="0.000"
                disabled={loading || isInvoiced}
              />
              {isInvoiced ? (
                <p className="text-xs text-muted-foreground">
                  Facturé — le paiement est géré par la facture (voir l'onglet Factures).
                </p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  Reste à payer :{" "}
                  <span className={reste > 0 ? "font-semibold text-amber-600" : "font-medium text-foreground"}>
                    {formatDT(reste)}
                  </span>
                </p>
              )}
            </div>
            <div className="flex items-end justify-end">
              <div className="text-sm">
                <span className="text-muted-foreground">Total :&nbsp;</span>
                <span className="font-semibold">{formatDT(total)}</span>
              </div>
            </div>
          </div>

          {/* Notes */}
          <div className="space-y-3">
            <Label>
              Notes <span className="font-normal text-muted-foreground">(facultatif)</span>
            </Label>
            <div className="space-y-2">
              {notes.map((note, index) => (
                <div key={index} className="flex gap-2">
                  <Textarea
                    value={note}
                    onChange={(e) => {
                      const next = [...notes]
                      next[index] = e.target.value
                      setNotes(next)
                    }}
                    placeholder="Saisir une note…"
                    className="min-h-[70px] resize-y text-sm"
                    disabled={loading}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="shrink-0"
                    onClick={() => setNotes(notes.filter((_, i) => i !== index))}
                    disabled={loading}
                    aria-label="Supprimer la note"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setNotes([...notes, ""])}
                className="w-full"
                disabled={loading}
              >
                <Plus className="mr-1 h-4 w-4" /> Ajouter une note
              </Button>
            </div>
          </div>

          <div className="space-y-3">
            <Label>
              Notes importantes <span className="font-normal text-muted-foreground">(facultatif)</span>
              <span className="ml-2 text-xs text-amber-600 dark:text-amber-500">⚠ Mises en évidence</span>
            </Label>
            <div className="space-y-2">
              {importantNotes.map((note, index) => (
                <div key={index} className="flex gap-2">
                  <Textarea
                    value={note}
                    onChange={(e) => {
                      const next = [...importantNotes]
                      next[index] = e.target.value
                      setImportantNotes(next)
                    }}
                    placeholder="Saisir une note importante…"
                    className="min-h-[70px] resize-y border-amber-300 bg-amber-50/50 text-sm dark:border-amber-700 dark:bg-amber-950/20"
                    disabled={loading}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="shrink-0"
                    onClick={() => setImportantNotes(importantNotes.filter((_, i) => i !== index))}
                    disabled={loading}
                    aria-label="Supprimer la note importante"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setImportantNotes([...importantNotes, ""])}
                className="w-full border-amber-300 dark:border-amber-700"
                disabled={loading}
              >
                <Plus className="mr-1 h-4 w-4" /> Ajouter une note importante
              </Button>
            </div>
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={loading} className="min-w-[140px]">
            {loading ? "Enregistrement…" : record ? "Enregistrer" : "Créer la fiche"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
