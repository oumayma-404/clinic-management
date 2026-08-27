"use client"

import type React from "react"
import { useState, useEffect } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { medicationsApi } from "@/lib/api/medications"
import type { MedicationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

interface MedicationFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingMedication?: MedicationDto | null
  onSuccess?: () => void
}

// Split the comma-separated DCI input into a clean molecule list (trimmed, empties dropped).
function parseDcis(raw: string): string[] {
  return raw
    .split(",")
    .map((d) => d.trim())
    .filter((d) => d.length > 0)
}

export function MedicationFormModal({ open, onOpenChange, editingMedication, onSuccess }: MedicationFormModalProps) {
  const [brandName, setBrandName] = useState("")
  const [form, setForm] = useState("")
  const [strength, setStrength] = useState("")
  const [dcisInput, setDcisInput] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  /*
   * Band B — the version this form saves with, re-read on open rather than taken from the clicked row. ⚠️ The
   * VERSION only: the read lands after the fields hydrate below, so its values would replace what was typed.
   */
  const { source: freshMedication, resync } = useFreshVersion(
    open,
    editingMedication?.id,
    editingMedication,
    async () =>
      (await medicationsApi.list(editingMedication!.brandName, true))
        .find((m) => m.id === editingMedication!.id) ?? null,
  )

  useEffect(() => {
    if (editingMedication) {
      setBrandName(editingMedication.brandName)
      setForm(editingMedication.form)
      setStrength(editingMedication.strength)
      setDcisInput(editingMedication.dcis.join(", "))
    } else {
      setBrandName("")
      setForm("")
      setStrength("")
      setDcisInput("")
    }
    setError(null)
  }, [editingMedication, open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!brandName.trim()) return setError("Le nom commercial est obligatoire.")
    const dcis = parseDcis(dcisInput)
    if (dcis.length === 0) return setError("Au moins une DCI (molécule) est requise.")

    const payload = {
      brandName: brandName.trim(),
      form: form.trim(),
      strength: strength.trim(),
      dcis,
    }

    try {
      setLoading(true)
      setIsConflict(false)
      if (editingMedication) {
        await medicationsApi.update(editingMedication.id, {
          ...payload,
          version: freshMedication?.version ?? editingMedication.version,
        })
      } else {
        await medicationsApi.create(payload)
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      const conflict = err instanceof ApiError && err.status === 409
      setIsConflict(conflict)
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement du médicament.")
      // A real 409 is left alone: resyncing would let the retry overwrite the colleague who caused it.
      if (!conflict) await resync()
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* No `max-h-[90dvh] overflow-y-auto` here: `ui/dialog.tsx`'s base already declares both (and the `md:`
          counterparts). Repeating them unprefixed meant this call site would silently override the primitive
          the day it changes. */}
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>{editingMedication ? "Modifier le médicament" : "Ajouter un médicament"}</DialogTitle>
          <DialogDescription>
            Médicament du catalogue (nom commercial, forme, dosage, molécules DCI).
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* The shared refusal banner, on `--destructive-wash` / `--destructive`. It replaces a hand-written
              `border-red-200 bg-red-50 … dark:` copy — one of ~18 that each maintained dark mode themselves. */}
          <FormErrorBanner
            message={error}
            action={isConflict ? { label: "Recharger", onClick: () => onSuccess?.(), disabled: loading } : undefined}
          />

          <div className="space-y-1.5">
            <Label htmlFor="brandName" className="text-sm">
              Nom commercial <span className="text-destructive">*</span>
            </Label>
            <Input
              id="brandName"
              placeholder="ex. Augmentin"
              value={brandName}
              onChange={(e) => setBrandName(e.target.value)}
              disabled={loading}
            />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="form" className="text-sm">
                Forme
              </Label>
              <Input
                id="form"
                placeholder="ex. Comprimé"
                value={form}
                onChange={(e) => setForm(e.target.value)}
                disabled={loading}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="strength" className="text-sm">
                Dosage
              </Label>
              <Input
                id="strength"
                placeholder="ex. 1 g"
                value={strength}
                onChange={(e) => setStrength(e.target.value)}
                disabled={loading}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="dcis" className="text-sm">
              DCI / molécules <span className="text-destructive">*</span>
            </Label>
            <Input
              id="dcis"
              placeholder="ex. Amoxicilline, Acide clavulanique"
              value={dcisInput}
              onChange={(e) => setDcisInput(e.target.value)}
              disabled={loading}
            />
            <p className="text-xs text-muted-foreground">
              Séparez plusieurs molécules par des virgules (médicament en association).
            </p>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : editingMedication ? "Enregistrer" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
