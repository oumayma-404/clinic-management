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
import { Checkbox } from "@/components/ui/checkbox"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { dentalActsApi, type DentalActInput } from "@/lib/api/dental-acts"
import type { DentalActDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { parseAmountInput } from "@/lib/format"

export const DENTAL_ACT_CATEGORIES = [
  "Soins conservateurs",
  "Soins chirurgicaux",
  "Parodontologie",
  "Pédodontie",
  "Orthopédie dento-faciale",
  "Prothèse",
]

interface DentalActFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingAct?: DentalActDto | null
  onSuccess?: () => void
}

export function DentalActFormModal({ open, onOpenChange, editingAct, onSuccess }: DentalActFormModalProps) {
  const [codeActe, setCodeActe] = useState("")
  const [designationFr, setDesignationFr] = useState("")
  const [lettreCle, setLettreCle] = useState("")
  const [coefficient, setCoefficient] = useState("")
  const [category, setCategory] = useState(DENTAL_ACT_CATEGORIES[0])
  const [defaultFee, setDefaultFee] = useState("")
  const [requiresAccordPrealable, setRequiresAccordPrealable] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  /*
   * Band B — the version this form saves with, re-read on open rather than taken from the clicked row. ⚠️ The
   * VERSION only: the read lands after the fields hydrate below, so its values would replace what was typed.
   */
  const { source: freshAct, resync } = useFreshVersion(
    open,
    editingAct?.id,
    editingAct,
    async () =>
      (await dentalActsApi.list(editingAct!.codeActe, undefined, true))
        .find((a) => a.id === editingAct!.id) ?? null,
  )

  useEffect(() => {
    if (editingAct) {
      setCodeActe(editingAct.codeActe)
      setDesignationFr(editingAct.designationFr)
      setLettreCle(editingAct.lettreCle)
      setCoefficient(editingAct.coefficient != null ? String(editingAct.coefficient) : "")
      setCategory(DENTAL_ACT_CATEGORIES.includes(editingAct.category) ? editingAct.category : DENTAL_ACT_CATEGORIES[0])
      setDefaultFee(editingAct.defaultFee != null ? String(editingAct.defaultFee) : "")
      setRequiresAccordPrealable(editingAct.requiresAccordPrealable)
    } else {
      setCodeActe("")
      setDesignationFr("")
      setLettreCle("")
      setCoefficient("")
      setCategory(DENTAL_ACT_CATEGORIES[0])
      setDefaultFee("")
      setRequiresAccordPrealable(false)
    }
    setError(null)
  }, [editingAct, open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!codeActe.trim()) return setError("Le code acte est obligatoire.")
    if (!designationFr.trim()) return setError("La désignation est obligatoire.")
    if (!lettreCle.trim()) return setError("La lettre clé est obligatoire.")

    let coef: number | null = null
    if (coefficient.trim() !== "") {
      coef = parseAmountInput(coefficient)
      if (!Number.isFinite(coef) || coef <= 0) return setError("Le coefficient doit être strictement positif.")
    }

    let fee: number | null = null
    if (defaultFee.trim() !== "") {
      fee = parseAmountInput(defaultFee)
      if (!Number.isFinite(fee) || fee < 0) return setError("Le tarif par défaut doit être positif.")
    }

    const payload: DentalActInput = {
      codeActe: codeActe.trim(),
      designationFr: designationFr.trim(),
      lettreCle: lettreCle.trim().toUpperCase(),
      coefficient: coef,
      category,
      defaultFee: fee,
      requiresAccordPrealable,
    }

    try {
      setLoading(true)
      setIsConflict(false)
      if (editingAct) {
        await dentalActsApi.update(editingAct.id, {
          ...payload,
          version: freshAct?.version ?? editingAct.version,
        })
      } else {
        await dentalActsApi.create(payload)
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      const conflict = err instanceof ApiError && err.status === 409
      setIsConflict(conflict)
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement de l'acte.")
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
          <DialogTitle>{editingAct ? "Modifier l'acte" : "Ajouter un acte"}</DialogTitle>
          <DialogDescription>
            Acte du catalogue dentaire (code, désignation, lettre clé, coefficient, catégorie, tarif).
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
                placeholder="ex. OBTU2"
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
              placeholder="ex. Obturation deux faces"
              value={designationFr}
              onChange={(e) => setDesignationFr(e.target.value)}
              disabled={loading}
            />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="coefficient" className="text-sm">
                Coefficient
              </Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). The `.replace(",", ".")` this
                    field's handler already carried was **dead code**: a number input never yields a comma, it
                    returns an EMPTY value for the rejected keystroke. Parsing now goes through the shared
                    `parseAmountInput`. */}
              <Input
                id="coefficient"
                type="text"
                inputMode="decimal"
                placeholder="Facultatif"
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
                  {DENTAL_ACT_CATEGORIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="defaultFee" className="text-sm">
              Tarif par défaut (DT)
            </Label>
            {/* Same conversion (J8) — and this one is money: a tarif par défaut in millimes. */}
            <Input
              id="defaultFee"
              type="text"
              inputMode="decimal"
              placeholder="Facultatif"
              value={defaultFee}
              onChange={(e) => setDefaultFee(e.target.value)}
              disabled={loading}
            />
          </div>

          <div className="flex items-center gap-2">
            <Checkbox
              id="requiresAccordPrealable"
              checked={requiresAccordPrealable}
              onCheckedChange={(checked) => setRequiresAccordPrealable(checked === true)}
              disabled={loading}
            />
            <Label htmlFor="requiresAccordPrealable" className="text-sm font-normal">
              Accord préalable requis
            </Label>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : editingAct ? "Enregistrer" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
