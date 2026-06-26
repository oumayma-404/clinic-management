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
import { Check } from "lucide-react"
import { cn } from "@/lib/utils"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

// Curated color palette - must match backend ColorHex value object
const COLOR_PALETTE = [
  { name: "Soft Blue", value: "#4F83CC" },
  { name: "Teal", value: "#2A9D8F" },
  { name: "Muted Green", value: "#6BAA75" },
  { name: "Lavender", value: "#9B8EDC" },
  { name: "Warm Amber", value: "#E9A23B" },
  { name: "Coral", value: "#E76F51" },
  { name: "Slate", value: "#6C757D" },
  { name: "Sky Blue", value: "#60A5FA" },
  { name: "Mint", value: "#5EEAD4" },
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
    } else {
      // Reset form for new procedure
      setName("")
      setDuration("")
      setDefaultCost("")
      setDescription("")
      setSelectedColor(COLOR_PALETTE[0].value)
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
        setError("Procedure name is required")
        setLoading(false)
        return
      }

      if (!duration || Number(duration) <= 0) {
        setError("Duration must be greater than 0")
        setLoading(false)
        return
      }

      if (Number(duration) >= 480) {
        setError("Duration must be less than 480 minutes (8 hours)")
        setLoading(false)
        return
      }

      const durationMinutes = Number(duration)

      const defaultCostValue = defaultCost ? Number.parseFloat(defaultCost) : null
      if (defaultCostValue !== null && defaultCostValue < 0) {
        setError("Default cost cannot be negative")
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
        })
      } else {
        // Create new procedure
        await procedureTypesApi.create({
          name: name.trim(),
          defaultDurationMinutes: durationMinutes,
          defaultCost: defaultCostValue,
          colorHex: selectedColor,
          description: description.trim() || undefined,
        })
      }

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Failed to save procedure type. Please try again.")
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
          <DialogTitle>{editingProcedure ? "Edit Procedure Type" : "Add New Procedure Type"}</DialogTitle>
          <DialogDescription>
            {editingProcedure
              ? "Update the procedure type details and color"
              : "Define a new procedure with its duration and calendar color"}
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
                Procedure Name <span className="text-destructive">*</span>
              </Label>
              <Input
                id="name"
                placeholder="e.g., Consultation"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                disabled={loading}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="duration" className="text-sm">
                Duration (min) <span className="text-destructive">*</span>
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
              Default Cost (Optional)
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
              Typical cost for this procedure. Will be used to prefill cost in dental records.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description" className="text-sm">
              Description (Optional)
            </Label>
            <Textarea
              id="description"
              placeholder="Brief description..."
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              className="resize-none"
              disabled={loading}
            />
          </div>

          <div className="space-y-2">
            <Label className="text-sm">
              Calendar Color <span className="text-destructive">*</span>
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
            <Label className="text-xs font-medium">Preview</Label>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <p className="text-[10px] font-medium text-muted-foreground">Calendar</p>
                <div
                  className="rounded-md border-l-4 bg-card p-2 shadow-sm"
                  style={{
                    borderLeftColor: selectedColor,
                    backgroundColor: `${selectedColor}15`,
                  }}
                >
                  <p className="text-xs font-medium text-foreground">{name || "Procedure Name"}</p>
                  <p className="text-[10px] text-muted-foreground">{duration ? `${duration} min` : "Duration"}</p>
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
                    {name || "Procedure"}
                  </Badge>
                </div>
              </div>
            </div>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Cancel
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? (editingProcedure ? "Updating..." : "Creating...") : (editingProcedure ? "Update" : "Add Procedure")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}


