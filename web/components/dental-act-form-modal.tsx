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
import { Checkbox } from "@/components/ui/checkbox"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { dentalActsApi, type DentalActInput } from "@/lib/api/dental-acts"
import type { DentalActDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

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
      coef = Number.parseFloat(coefficient.replace(",", "."))
      if (!Number.isFinite(coef) || coef <= 0) return setError("Le coefficient doit être strictement positif.")
    }

    let fee: number | null = null
    if (defaultFee.trim() !== "") {
      fee = Number.parseFloat(defaultFee.replace(",", "."))
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
      if (editingAct) {
        await dentalActsApi.update(editingAct.id, payload)
      } else {
        await dentalActsApi.create(payload)
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement de l'acte.")
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{editingAct ? "Modifier l'acte" : "Ajouter un acte"}</DialogTitle>
          <DialogDescription>
            Acte du catalogue dentaire (code, désignation, lettre clé, coefficient, catégorie, tarif).
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
              {error}
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
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

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="coefficient" className="text-sm">
                Coefficient
              </Label>
              <Input
                id="coefficient"
                type="number"
                min="0"
                step="0.001"
                placeholder="Optionnel"
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
                <SelectTrigger id="category">
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
            <Input
              id="defaultFee"
              type="number"
              min="0"
              step="0.001"
              placeholder="Optionnel"
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
