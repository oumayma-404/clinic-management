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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cnamNomenclatureApi } from "@/lib/api/cnam-nomenclature"
import type { CnamNomenclatureEntryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { parseAmountInput } from "@/lib/format"

const CATEGORIES = [
  "Consultation",
  "Soins conservateurs",
  "Chirurgie/Extraction",
  "Prothèse",
  "Radiologie",
]

interface CnamEntryFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingEntry?: CnamNomenclatureEntryDto | null
  onSuccess?: () => void
}

export function CnamEntryFormModal({ open, onOpenChange, editingEntry, onSuccess }: CnamEntryFormModalProps) {
  const [codeActe, setCodeActe] = useState("")
  const [designationFr, setDesignationFr] = useState("")
  const [lettreCle, setLettreCle] = useState("")
  const [coefficient, setCoefficient] = useState("")
  const [category, setCategory] = useState(CATEGORIES[0])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  /*
   * Band B — the version this form saves with, re-read from the server on open rather than taken from the row
   * that was clicked. ⚠️ The VERSION only: the read lands after the fields are hydrated below, so applying its
   * values would replace what the user has already typed. `q=codeActe` narrows the read; there is no GET-by-id.
   */
  const { source: freshEntry, resync } = useFreshVersion(
    open,
    editingEntry?.id,
    editingEntry,
    async () =>
      (await cnamNomenclatureApi.list(editingEntry!.codeActe, undefined, true))
        .find((e) => e.id === editingEntry!.id) ?? null,
  )

  useEffect(() => {
    if (editingEntry) {
      setCodeActe(editingEntry.codeActe)
      setDesignationFr(editingEntry.designationFr)
      setLettreCle(editingEntry.lettreCle)
      setCoefficient(String(editingEntry.coefficient))
      setCategory(CATEGORIES.includes(editingEntry.category) ? editingEntry.category : CATEGORIES[0])
    } else {
      setCodeActe("")
      setDesignationFr("")
      setLettreCle("")
      setCoefficient("")
      setCategory(CATEGORIES[0])
    }
    setError(null)
  }, [editingEntry, open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!codeActe.trim()) return setError("Le code acte est obligatoire.")
    if (!designationFr.trim()) return setError("La désignation est obligatoire.")
    if (!lettreCle.trim()) return setError("La lettre clé est obligatoire.")
    const coef = parseAmountInput(coefficient)
    if (!Number.isFinite(coef) || coef <= 0) return setError("Le coefficient doit être strictement positif.")

    const payload = {
      codeActe: codeActe.trim(),
      designationFr: designationFr.trim(),
      lettreCle: lettreCle.trim().toUpperCase(),
      coefficient: coef,
      category,
    }

    try {
      setLoading(true)
      setIsConflict(false)
      if (editingEntry) {
        await cnamNomenclatureApi.update(editingEntry.id, {
          ...payload,
          version: freshEntry?.version ?? editingEntry.version,
        })
      } else {
        await cnamNomenclatureApi.create(payload)
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      const conflict = err instanceof ApiError && err.status === 409
      setIsConflict(conflict)
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement de l'acte.")
      // A non-conflict failure may still have moved the row. A real 409 is left alone — resyncing would let the
      // retry silently overwrite the colleague whose edit caused it.
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
          <DialogTitle>{editingEntry ? "Modifier l'acte" : "Ajouter un acte"}</DialogTitle>
          <DialogDescription>
            Acte de la nomenclature dentaire CNAM (code, désignation, lettre clé, coefficient, catégorie).
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* The shared refusal banner, on `--destructive-wash` / `--destructive`. It replaces a hand-written
              `border-red-200 bg-red-50 … dark:` copy — one of ~18 that each maintained dark mode themselves. */}
          <FormErrorBanner
            message={error}
            action={isConflict ? { label: "Recharger", onClick: () => onSuccess?.(), disabled: loading } : undefined}
          />

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="codeActe" className="text-sm">
                Code acte <span className="text-destructive">*</span>
              </Label>
              <Input
                id="codeActe"
                placeholder="ex. DETART"
                value={codeActe}
                onChange={(e) => setCodeActe(e.target.value)}
                disabled={loading}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="lettreCle" className="text-sm">
                Lettre clé <span className="text-destructive">*</span>
              </Label>
              <Input
                id="lettreCle"
                placeholder="ex. D"
                value={lettreCle}
                onChange={(e) => setLettreCle(e.target.value)}
                disabled={loading}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="designationFr" className="text-sm">
              Désignation (FR) <span className="text-destructive">*</span>
            </Label>
            <Input
              id="designationFr"
              placeholder="ex. Détartrage (deux arcades)"
              value={designationFr}
              onChange={(e) => setDesignationFr(e.target.value)}
              disabled={loading}
            />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="coefficient" className="text-sm">
                Coefficient <span className="text-destructive">*</span>
              </Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). The `.replace(",", ".")` this
                    field's handler already carried was **dead code**: a number input never yields a comma, it
                    returns an EMPTY value for the rejected keystroke. Parsing now goes through the shared
                    `parseAmountInput`. */}
              <Input
                id="coefficient"
                type="text"
                inputMode="decimal"
                placeholder="ex. 10"
                value={coefficient}
                onChange={(e) => setCoefficient(e.target.value)}
                disabled={loading}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="category" className="text-sm">
                Catégorie <span className="text-destructive">*</span>
              </Label>
              <Select value={category} onValueChange={setCategory} disabled={loading}>
                {/* `w-full`: `ui/select.tsx`'s trigger ships `w-fit`, so with no width the control rendered
                    narrower than the « Coefficient » input beside it in the same grid row. */}
                <SelectTrigger id="category" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CATEGORIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : editingEntry ? "Enregistrer" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
