"use client"

import type React from "react"

import { useState, useEffect, useRef } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Trash2, Plus, Search, X } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi, type InvoiceLineInput, type CreateInvoiceRequest } from "@/lib/api/invoices"
import { patientsApi } from "@/lib/api/patients"
import { dentalActsApi } from "@/lib/api/dental-acts"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto, PatientDto, DentalActDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"

interface LineRow {
  designation: string
  quantity: string
  unitPriceHt: string
  /** Catalog CNAM/DCH act attached to the line (drives the reimbursable split); null for free text. */
  dentalActCodeId: string | null
  codeActe: string | null
}

interface InvoiceFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingInvoice?: InvoiceDto | null
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  /** Pre-filled act lines (create mode only) — e.g. seeded from a dental record. */
  presetLines?: InvoiceLineInput[]
  /** Optional source dental-record link, persisted on the created draft (create mode only). */
  dentalRecordId?: string
  /**
   * The visit this note bills, persisted on the created draft (create mode only) — AC-P6.12. The backend
   * column has always existed and nothing populated it, so an invoice could never say which consultation it
   * was for. Passed only when the form is opened from an appointment context; the server verifies the
   * appointment belongs to this clinic and this patient.
   */
  appointmentId?: string
  onSuccess?: () => void
}

const emptyLine = (): LineRow => ({ designation: "", quantity: "1", unitPriceHt: "", dentalActCodeId: null, codeActe: null })

/**
 * Upgrade the message when the same edit conflicts twice running. The first 409 means "someone saved before
 * you"; the second means "someone is editing this right now", and telling the user to reload again would be
 * repeating advice that has already failed.
 */
function conflictMessage(err: unknown, fallback: string, consecutive: React.MutableRefObject<number>): string {
  if (err instanceof ApiError && err.status === 409) {
    consecutive.current += 1
    if (consecutive.current > 1) {
      return "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement "
        + "dessus en même temps — coordonnez-vous avant de réessayer."
    }
    return err.message || fallback
  }
  consecutive.current = 0
  return err instanceof ApiError ? err.message : fallback
}

export function InvoiceFormModal({
  open,
  onOpenChange,
  editingInvoice,
  presetPatientId,
  presetPatientName,
  presetLines,
  dentalRecordId,
  appointmentId,
  onSuccess,
}: InvoiceFormModalProps) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [acts, setActs] = useState<DentalActDto[]>([])
  const [patientId, setPatientId] = useState("")
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const conflictStreak = useRef(0)

  const isEditing = !!editingInvoice

  useEffect(() => {
    if (!open) return

    // Load the active CNAM dental act catalog for the per-line act picker (drives the reimbursable split).
    dentalActsApi
      .list()
      .then(setActs)
      .catch(() => setActs([]))

    // Only load a patient list when the caller doesn't preset one.
    if (!presetPatientId) {
      patientsApi
        .list({ limit: 500 })
        .then(setPatients)
        .catch(() => setPatients([]))
    }

    if (editingInvoice) {
      setPatientId(editingInvoice.patientId)
      setLines(
        editingInvoice.lines.length > 0
          ? editingInvoice.lines.map((l) => ({
              designation: l.designation,
              quantity: String(l.quantity),
              unitPriceHt: String(l.unitPriceHt),
              dentalActCodeId: l.dentalActCodeId ?? null,
              codeActe: l.codeActe ?? null,
            }))
          : [emptyLine()],
      )
    } else {
      setPatientId(presetPatientId ?? "")
      setLines(
        presetLines && presetLines.length > 0
          ? presetLines.map((l) => ({
              designation: l.designation,
              quantity: String(l.quantity),
              unitPriceHt: String(l.unitPriceHt),
              dentalActCodeId: l.dentalActCodeId ?? null,
              codeActe: l.codeActe ?? null,
            }))
          : [emptyLine()],
      )
    }
    setError(null)
    // Seeds once when the dialog opens; presetLines are read from the opening render (like presetPatientName).
  }, [open, editingInvoice, presetPatientId])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

  const selectAct = (index: number, act: DentalActDto) => {
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? {
              ...l,
              dentalActCodeId: act.id,
              codeActe: act.codeActe,
              // Fill the designation from the act, and the price from its default fee, only when empty.
              designation: l.designation.trim() === "" ? act.designationFr : l.designation,
              unitPriceHt: l.unitPriceHt.trim() === "" && act.defaultFee != null ? String(act.defaultFee) : l.unitPriceHt,
            }
          : l,
      ),
    )
    setPickerOpenIndex(null)
  }

  const detachAct = (index: number) => updateLine(index, { dentalActCodeId: null, codeActe: null })

  const totalHt = lines.reduce((sum, l) => {
    const qty = Number(l.quantity)
    const price = Number(l.unitPriceHt)
    if (!Number.isFinite(qty) || !Number.isFinite(price)) return sum
    return sum + qty * price
  }, 0)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!patientId) {
      setError("Veuillez sélectionner un patient.")
      return
    }

    const parsedLines: InvoiceLineInput[] = lines
      .map((l) => ({
        designation: l.designation.trim(),
        quantity: Number(l.quantity),
        unitPriceHt: Number(l.unitPriceHt),
        dentalActCodeId: l.dentalActCodeId,
        codeActe: l.codeActe,
      }))
      .filter((l) => l.designation !== "")

    if (parsedLines.length === 0) {
      setError("Ajoutez au moins une ligne d'acte.")
      return
    }

    for (const l of parsedLines) {
      if (!Number.isFinite(l.quantity) || l.quantity <= 0) {
        setError(`Quantité invalide pour « ${l.designation} ».`)
        return
      }
      if (!Number.isFinite(l.unitPriceHt) || l.unitPriceHt < 0) {
        setError(`Prix unitaire invalide pour « ${l.designation} ».`)
        return
      }
    }

    setLoading(true)
    try {
      if (isEditing && editingInvoice) {
        await invoicesApi.update(editingInvoice.id, {
          patientId,
          lines: parsedLines,
          version: editingInvoice.version,
        })
        toast.success("Brouillon mis à jour")
      } else {
        const payload: CreateInvoiceRequest = { patientId, lines: parsedLines }
        // Persist the source dental-record link on the new draft (spec AC-2).
        if (dentalRecordId) payload.dentalRecordId = dentalRecordId
        if (appointmentId) payload.appointmentId = appointmentId
        await invoicesApi.create(payload)
        toast.success("Brouillon de facture créé")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(conflictMessage(err, "Échec de l'enregistrement de la facture.", conflictStreak))
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{isEditing ? "Modifier le brouillon" : "Nouvelle facture"}</DialogTitle>
          <DialogDescription>
            Un brouillon ne consomme aucun numéro. Le numéro est attribué à l'émission.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <FormErrorBanner message={error} />

          <div className="space-y-1.5">
            <Label htmlFor="patient">
              Patient <span className="text-destructive">*</span>
            </Label>
            {presetPatientId ? (
              <Input id="patient" value={presetPatientName ?? "Patient"} disabled />
            ) : (
              <Select value={patientId} onValueChange={setPatientId} disabled={loading}>
                <SelectTrigger id="patient">
                  <SelectValue placeholder="Sélectionner un patient" />
                </SelectTrigger>
                <SelectContent>
                  {patients.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.firstName} {p.lastName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>

          <div className="space-y-2">
            <Label>Actes</Label>
            <div className="space-y-3">
              {lines.map((line, index) => {
                const qty = Number(line.quantity)
                const price = Number(line.unitPriceHt)
                const lineTotal = Number.isFinite(qty) && Number.isFinite(price) ? qty * price : 0
                return (
                  <div key={index} className="rounded-lg border p-3 space-y-2">
                    <div className="flex items-start gap-2">
                      <div className="flex-1 space-y-1">
                        <div className="flex items-center gap-2">
                          <Input
                            value={line.designation}
                            onChange={(e) => updateLine(index, { designation: e.target.value })}
                            placeholder="Ex. Détartrage (ou choisir un acte CNAM)"
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
                                title="Rattacher un acte CNAM (pour le calcul du remboursement)"
                              >
                                <Search className="h-4 w-4" />
                                <span className="sr-only">Rattacher un acte CNAM</span>
                              </Button>
                            </PopoverTrigger>
                            <PopoverContent className="p-0 w-80" align="end">
                              <Command>
                                <CommandInput placeholder="Rechercher un acte…" />
                                <CommandList>
                                  <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                  <CommandGroup heading="Nomenclature CNAM">
                                    {acts.map((act) => (
                                      <CommandItem
                                        key={act.id}
                                        value={`${act.codeActe} ${act.designationFr} ${act.lettreCle}`}
                                        onSelect={() => selectAct(index, act)}
                                      >
                                        <div className="flex flex-col">
                                          <span className="text-sm font-medium">{act.designationFr}</span>
                                          <span className="text-xs text-muted-foreground">
                                            {act.codeActe} · {act.category}
                                            {act.defaultFee != null ? ` · ${formatDT(act.defaultFee)}` : ""}
                                          </span>
                                        </div>
                                      </CommandItem>
                                    ))}
                                  </CommandGroup>
                                </CommandList>
                              </Command>
                            </PopoverContent>
                          </Popover>
                        </div>
                        {line.codeActe && (
                          <Badge variant="secondary" className="gap-1 font-mono text-xs">
                            {line.codeActe}
                            <button
                              type="button"
                              onClick={() => detachAct(index)}
                              className="ml-1 rounded-full hover:text-destructive"
                              title="Détacher l'acte CNAM (reste à charge intégral)"
                            >
                              <X className="h-3 w-3" />
                            </button>
                          </Badge>
                        )}
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => removeLine(index)}
                        disabled={loading || lines.length === 1}
                        aria-label="Supprimer la ligne"
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                    <div className="flex flex-wrap items-center gap-3">
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs text-muted-foreground">Qté</span>
                        <Input
                          type="number"
                          min="1"
                          step="1"
                          value={line.quantity}
                          onChange={(e) => updateLine(index, { quantity: e.target.value })}
                          className="w-20"
                          disabled={loading}
                        />
                      </div>
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs text-muted-foreground">P.U. HT (DT)</span>
                        <Input
                          type="number"
                          min="0"
                          step="0.001"
                          value={line.unitPriceHt}
                          onChange={(e) => updateLine(index, { unitPriceHt: e.target.value })}
                          className="w-32"
                          disabled={loading}
                        />
                      </div>
                      <span className="ml-auto text-sm text-muted-foreground">
                        Total HT : <span className="font-medium text-foreground">{formatDT(lineTotal)}</span>
                      </span>
                    </div>
                  </div>
                )
              })}
            </div>
            <Button type="button" variant="outline" size="sm" onClick={addLine} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter une ligne
            </Button>
          </div>

          <div className="flex justify-end text-sm">
            <span className="text-muted-foreground">Total HT :&nbsp;</span>
            <span className="font-semibold">{formatDT(totalHt)}</span>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement..." : isEditing ? "Enregistrer" : "Créer le brouillon"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
