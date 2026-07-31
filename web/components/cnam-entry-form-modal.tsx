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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cnamNomenclatureApi } from "@/lib/api/cnam-nomenclature"
import type { CnamNomenclatureEntryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

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
    const coef = Number.parseFloat(coefficient.replace(",", "."))
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
      if (editingEntry) {
        await cnamNomenclatureApi.update(editingEntry.id, payload)
      } else {
        await cnamNomenclatureApi.create(payload)
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
      <DialogContent className="max-h-[90dvh] overflow-y-auto md:max-w-lg">
        <DialogHeader>
          <DialogTitle>{editingEntry ? "Modifier l'acte" : "Ajouter un acte"}</DialogTitle>
          <DialogDescription>
            Acte de la nomenclature dentaire CNAM (code, désignation, lettre clé, coefficient, catégorie).
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
              {error}
            </div>
          )}

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
              <Input
                id="coefficient"
                type="number"
                min="0"
                step="0.001"
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
                <SelectTrigger id="category">
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
