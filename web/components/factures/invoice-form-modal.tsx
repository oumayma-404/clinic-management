"use client"

import { useState, useEffect } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Trash2, Plus } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { patientsApi } from "@/lib/api/patients"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto, PatientDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"

interface LineRow {
  designation: string
  quantity: string
  unitPriceHt: string
}

interface InvoiceFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingInvoice?: InvoiceDto | null
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  onSuccess?: () => void
}

const emptyLine = (): LineRow => ({ designation: "", quantity: "1", unitPriceHt: "" })

export function InvoiceFormModal({
  open,
  onOpenChange,
  editingInvoice,
  presetPatientId,
  presetPatientName,
  onSuccess,
}: InvoiceFormModalProps) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [patientId, setPatientId] = useState("")
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isEditing = !!editingInvoice

  useEffect(() => {
    if (!open) return

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
            }))
          : [emptyLine()],
      )
    } else {
      setPatientId(presetPatientId ?? "")
      setLines([emptyLine()])
    }
    setError(null)
  }, [open, editingInvoice, presetPatientId])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

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

    const parsedLines = lines
      .map((l) => ({
        designation: l.designation.trim(),
        quantity: Number(l.quantity),
        unitPriceHt: Number(l.unitPriceHt),
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
      const payload = { patientId, lines: parsedLines }
      if (isEditing && editingInvoice) {
        await invoicesApi.update(editingInvoice.id, payload)
        toast.success("Brouillon mis à jour")
      } else {
        await invoicesApi.create(payload)
        toast.success("Brouillon de facture créé")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement de la facture.")
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
          {error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
              {error}
            </div>
          )}

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
            <div className="space-y-2">
              {lines.map((line, index) => (
                <div key={index} className="flex items-end gap-2">
                  <div className="flex-1 space-y-1">
                    {index === 0 && <span className="text-xs text-muted-foreground">Désignation</span>}
                    <Input
                      value={line.designation}
                      onChange={(e) => updateLine(index, { designation: e.target.value })}
                      placeholder="Ex. Détartrage"
                      disabled={loading}
                    />
                  </div>
                  <div className="w-16 space-y-1">
                    {index === 0 && <span className="text-xs text-muted-foreground">Qté</span>}
                    <Input
                      type="number"
                      min="1"
                      step="1"
                      value={line.quantity}
                      onChange={(e) => updateLine(index, { quantity: e.target.value })}
                      disabled={loading}
                    />
                  </div>
                  <div className="w-28 space-y-1">
                    {index === 0 && <span className="text-xs text-muted-foreground">P.U. HT</span>}
                    <Input
                      type="number"
                      min="0"
                      step="0.001"
                      value={line.unitPriceHt}
                      onChange={(e) => updateLine(index, { unitPriceHt: e.target.value })}
                      disabled={loading}
                    />
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
              ))}
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
