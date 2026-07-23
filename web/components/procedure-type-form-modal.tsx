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
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Check } from "lucide-react"
import { cn } from "@/lib/utils"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { CONDITION_ORDER, conditionStyle } from "@/components/odontogram-conditions"

// Sentinel for the "no resulting condition" option (Radix Select forbids an empty-string value).
const NO_CONDITION = "__none__"

// Curated color palette - must match backend ColorHex value object
const COLOR_PALETTE = [
  { name: "Bleu doux", value: "#4F83CC" },
  { name: "Sarcelle", value: "#2A9D8F" },
  { name: "Vert doux", value: "#6BAA75" },
  { name: "Lavande", value: "#9B8EDC" },
  { name: "Ambre chaud", value: "#E9A23B" },
  { name: "Corail", value: "#E76F51" },
  { name: "Ardoise", value: "#6C757D" },
  { name: "Bleu ciel", value: "#60A5FA" },
  { name: "Menthe", value: "#5EEAD4" },
  { name: "Rose", value: "#FB7185" },
]

interface ProcedureTypeFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingProcedure?: ProcedureTypeDto | null
  onSuccess?: () => void
}

export function ProcedureTypeFormModal({ open, onOpenChange, editingProcedure, onSuccess }: ProcedureTypeFormModalProps) {
  const [name, setName] = useState("")
  const [duration, setDuration] = useState("")
  const [defaultCost, setDefaultCost] = useState("")
  const [description, setDescription] = useState("")
  const [selectedColor, setSelectedColor] = useState(COLOR_PALETTE[0].value)
  const [resultingCondition, setResultingCondition] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Populate form when editing
  useEffect(() => {
    if (editingProcedure) {
      setName(editingProcedure.name)
      setDuration(String(editingProcedure.defaultDurationMinutes))
      setDefaultCost(editingProcedure.defaultCost ? String(editingProcedure.defaultCost) : "")
      setDescription(editingProcedure.description || "")
      setSelectedColor(editingProcedure.colorHex)
      setResultingCondition(editingProcedure.resultingCondition ?? null)
    } else {
      // Reset form for new procedure
      setName("")
      setDuration("")
      setDefaultCost("")
      setDescription("")
      setSelectedColor(COLOR_PALETTE[0].value)
      setResultingCondition(null)
    }
    setError(null)
  }, [editingProcedure, open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)

    try {
      // Validate required fields
      if (!name.trim()) {
        setError("Le nom de l'acte est requis")
        setLoading(false)
        return
      }

      if (!duration || Number(duration) <= 0) {
        setError("La durée doit être supérieure à 0")
        setLoading(false)
        return
      }

      if (Number(duration) >= 480) {
        setError("La durée doit être inférieure à 480 minutes (8 heures)")
        setLoading(false)
        return
      }

      const durationMinutes = Number(duration)

      const defaultCostValue = defaultCost ? Number.parseFloat(defaultCost) : null
      if (defaultCostValue !== null && defaultCostValue < 0) {
        setError("Le coût par défaut ne peut pas être négatif")
        setLoading(false)
        return
      }

      if (editingProcedure) {
        // Update existing procedure
        await procedureTypesApi.update(editingProcedure.id, {
          name: name.trim(),
          defaultDurationMinutes: durationMinutes,
          defaultCost: defaultCostValue,
          colorHex: selectedColor,
          description: description.trim() || undefined,
          resultingCondition,
        })
      } else {
        // Create new procedure
        await procedureTypesApi.create({
          name: name.trim(),
          defaultDurationMinutes: durationMinutes,
          defaultCost: defaultCostValue,
          colorHex: selectedColor,
          description: description.trim() || undefined,
          resultingCondition,
        })
      }

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Échec de l'enregistrement du type d'acte. Veuillez réessayer.")
      }
      console.error("Error saving procedure type:", err)
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{editingProcedure ? "Modifier le type d'acte" : "Ajouter un type d'acte"}</DialogTitle>
          <DialogDescription>
            {editingProcedure
              ? "Mettez à jour les détails et la couleur du type d'acte"
              : "Définissez un nouvel acte avec sa durée et sa couleur d'agenda"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">
              {error}
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="name" className="text-sm">
                Nom de l'acte <span className="text-destructive">*</span>
              </Label>
              <Input
                id="name"
                placeholder="ex. : consultation"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                disabled={loading}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="duration" className="text-sm">
                Durée (min) <span className="text-destructive">*</span>
              </Label>
              <Input
                id="duration"
                type="number"
                min="1"
                max="479"
                step="1"
                placeholder="30"
                value={duration}
                onChange={(e) => setDuration(e.target.value)}
                required
                disabled={loading}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="defaultCost" className="text-sm">
              Coût par défaut (optionnel)
            </Label>
            <div className="relative">
              <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">DT</span>
              <Input
                id="defaultCost"
                type="number"
                min="0"
                step="0.01"
                placeholder="e.g., 70.00"
                value={defaultCost}
                onChange={(e) => setDefaultCost(e.target.value)}
                className="pl-10"
                disabled={loading}
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Coût habituel de cet acte. Utilisé pour préremplir le coût dans les dossiers dentaires.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description" className="text-sm">
              Description (optionnel)
            </Label>
            <Textarea
              id="description"
              placeholder="Brève description…"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              className="resize-none"
              disabled={loading}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="resultingCondition" className="text-sm">
              État résultant sur l'odontogramme (facultatif)
            </Label>
            <Select
              value={resultingCondition ?? NO_CONDITION}
              onValueChange={(v) => setResultingCondition(v === NO_CONDITION ? null : v)}
              disabled={loading}
            >
              <SelectTrigger id="resultingCondition">
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
            <p className="text-xs text-muted-foreground">
              État appliqué automatiquement aux dents traitées par cet acte dans l'odontogramme.
            </p>
          </div>

          <div className="space-y-2">
            <Label className="text-sm">
              Couleur de l'agenda <span className="text-destructive">*</span>
            </Label>

            <div className="grid grid-cols-5 gap-2">
              {COLOR_PALETTE.map((color) => (
                <button
                  key={color.value}
                  type="button"
                  onClick={() => setSelectedColor(color.value)}
                  disabled={loading}
                  className={cn(
                    "relative flex flex-col items-center gap-1 rounded-lg border-2 p-2 transition-all hover:scale-105",
                    selectedColor === color.value
                      ? "border-primary bg-accent"
                      : "border-border bg-background hover:border-muted-foreground/50",
                    loading && "opacity-50 cursor-not-allowed"
                  )}
                  title={color.name}
                >
                  <div
                    className="h-6 w-6 rounded-full border-2 border-background shadow-sm"
                    style={{ backgroundColor: color.value }}
                  />
                  {selectedColor === color.value && (
                    <div className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-primary text-primary-foreground">
                      <Check className="h-2.5 w-2.5" />
                    </div>
                  )}
                  <span className="text-[9px] text-center leading-tight text-muted-foreground">{color.name}</span>
                </button>
              ))}
            </div>
          </div>

          <div className="space-y-2 rounded-lg border border-border bg-muted/30 p-3">
            <Label className="text-xs font-medium">Aperçu</Label>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <p className="text-[10px] font-medium text-muted-foreground">Agenda</p>
                <div
                  className="rounded-md border-l-4 bg-card p-2 shadow-sm"
                  style={{
                    borderLeftColor: selectedColor,
                    backgroundColor: `${selectedColor}15`,
                  }}
                >
                  <p className="text-xs font-medium text-foreground">{name || "Nom de l'acte"}</p>
                  <p className="text-[10px] text-muted-foreground">{duration ? `${duration} min` : "Durée"}</p>
                </div>
              </div>

              <div className="space-y-1.5">
                <p className="text-[10px] font-medium text-muted-foreground">Badge</p>
                <div className="flex items-center gap-2">
                  <div
                    className="h-2.5 w-2.5 rounded-full border border-border"
                    style={{ backgroundColor: selectedColor }}
                  />
                  <Badge
                    variant="outline"
                    className="border-2 text-xs"
                    style={{
                      borderColor: selectedColor,
                      color: selectedColor,
                      backgroundColor: `${selectedColor}10`,
                    }}
                  >
                    {name || "Acte"}
                  </Badge>
                </div>
              </div>
            </div>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? (editingProcedure ? "Mise à jour…" : "Création…") : (editingProcedure ? "Mettre à jour" : "Ajouter l'acte")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}


