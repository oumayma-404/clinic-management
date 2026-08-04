"use client"

import type React from "react"

import { useEffect, useState } from "react"
import { toast } from "sonner"
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { stockApi } from "@/lib/api/stock"
import { ApiError } from "@/lib/api/client"
import { formatAmount, parseAmountInput } from "@/lib/format"
import type { StockItemDto } from "@/lib/api/types"

/**
 * The stock categories, as **stored**. English on purpose — see `stockCategoryLabel` below.
 *
 * Exported because `stock-table.tsx` renders the same values as badges and in its category filter, and a second
 * copy of a closed value set is how the two drift (the precedent is `dental-act-form-modal`'s
 * `DENTAL_ACT_CATEGORIES`, exported for exactly this reason).
 */
export const STOCK_CATEGORIES = [
  "Medical Supplies",
  "Medical Equipment",
  "PPE",
  "Medications",
  "Lab Supplies",
  "Office Supplies",
]

/**
 * French labels for the stored (English) category keys.
 *
 * The defect: these six strings were persisted on `StockItem.Category`, offered raw in this Select, printed raw as
 * a badge on every stock row, and listed raw in the category filter — so a Tunisian dentist read « PPE » and
 * « Lab Supplies » in an otherwise entirely French product.
 *
 * Fixed the way this repo already fixes it everywhere else (`lib/specialties.ts`, `lib/working-hours.ts`,
 * `components/appointment-labels.ts`): **keep the storage key, map at display time**. A rename would be a data
 * migration — the values are already in the database, already returned by `GET /api/stock` as the `categories`
 * facet, and already used as the `category=` filter argument, so a renamed key would silently stop matching every
 * existing row.
 */
export const STOCK_CATEGORY_LABELS_FR: Record<string, string> = {
  "Medical Supplies": "Consommables médicaux",
  "Medical Equipment": "Équipement médical",
  PPE: "Protection (EPI)",
  Medications: "Médicaments",
  "Lab Supplies": "Fournitures de laboratoire",
  "Office Supplies": "Fournitures de bureau",
}

/**
 * The French label for a stored category, or the stored value verbatim when it has none.
 *
 * Passing an unknown value through matters: a clinic whose rows predate this map — or that had a category written
 * straight into the database — keeps rendering. Blanking it would make the row look corrupt.
 */
export function stockCategoryLabel(category: string | null | undefined): string {
  if (!category) return ""
  const trimmed = category.trim()
  return STOCK_CATEGORY_LABELS_FR[trimmed] ?? trimmed
}

const UNITS = ["Unit", "Box", "Bag", "Roll", "Bottle", "Pack", "Liter"]

/** Same convention as the categories: the unit is persisted in English and read in French. */
const UNIT_LABELS_FR: Record<string, string> = {
  Unit: "Unité",
  Box: "Boîte",
  Bag: "Sachet",
  Roll: "Rouleau",
  Bottle: "Flacon",
  Pack: "Paquet",
  Liter: "Litre",
}

export function stockUnitLabel(unit: string | null | undefined): string {
  if (!unit) return ""
  const trimmed = unit.trim()
  return UNIT_LABELS_FR[trimmed] ?? trimmed
}

interface StockItemFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingItem?: StockItemDto | null
  onSaved: () => void
}

export function StockItemFormModal({ open, onOpenChange, editingItem, onSaved }: StockItemFormModalProps) {
  const [name, setName] = useState("")
  const [category, setCategory] = useState("")
  const [unit, setUnit] = useState("")
  const [quantity, setQuantity] = useState("")
  const [minimumStockLevel, setMinimumStockLevel] = useState("")
  const [description, setDescription] = useState("")
  const [unitPrice, setUnitPrice] = useState("")
  const [supplier, setSupplier] = useState("")
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (editingItem) {
      setName(editingItem.name)
      setCategory(editingItem.category)
      setUnit(editingItem.unit)
      setQuantity(String(editingItem.currentStock))
      setMinimumStockLevel(String(editingItem.minimumStockLevel))
      setDescription(editingItem.description ?? "")
      setUnitPrice(editingItem.unitPrice != null ? formatAmount(editingItem.unitPrice) : "")
      setSupplier(editingItem.supplier ?? "")
    } else {
      setName("")
      setCategory("")
      setUnit("")
      setQuantity("")
      setMinimumStockLevel("")
      setDescription("")
      setUnitPrice("")
      setSupplier("")
    }
    setErrors({})
  }, [editingItem, open])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!name.trim()) next.name = "Le nom est requis"
    if (!category) next.category = "La catégorie est requise"
    if (!unit) next.unit = "L'unité est requise"
    if (quantity === "" || Number(quantity) < 0) next.quantity = "Saisissez une quantité de 0 ou plus"
    if (minimumStockLevel === "" || Number(minimumStockLevel) < 0) next.minimumStockLevel = "Saisissez un minimum de 0 ou plus"
    // NaN as well as negative: the field is `type="text"` now, so the browser no longer refuses a malformed
    // value and an unchecked NaN would be sent as a null price — silently unpricing the item.
    if (unitPrice.trim() !== "" && !(parseAmountInput(unitPrice) >= 0)) {
      next.unitPrice = "Saisissez un prix valide, par exemple 12,500"
    }
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return

    const payload = {
      name: name.trim(),
      category,
      unit,
      currentStock: Number(quantity),
      minimumStockLevel: Number(minimumStockLevel),
      // Preserve the item's existing maximum on edit (the form doesn't manage it);
      // on create, let the backend default it to the minimum.
      maximumStockLevel: editingItem ? editingItem.maximumStockLevel : null,
      description: description.trim() || null,
      unitPrice: unitPrice.trim() !== "" ? parseAmountInput(unitPrice) : null,
      supplier: supplier.trim() || null,
    }

    try {
      setSaving(true)
      if (editingItem) {
        await stockApi.update(editingItem.id, payload)
        toast.success("Article mis à jour")
      } else {
        await stockApi.create(payload)
        toast.success("Article ajouté")
      }
      onOpenChange(false)
      onSaved()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement de l'article")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <DialogTitle>{editingItem ? "Modifier l'article" : "Ajouter un article"}</DialogTitle>
          <DialogDescription>
            {editingItem ? "Mettez à jour les détails de l'article" : "Saisissez les détails du nouvel article"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="name">
              Nom de l'article <span className="text-destructive">*</span>
            </Label>
            <Input id="name" placeholder="ex. : gants chirurgicaux" value={name} onChange={(e) => setName(e.target.value)} />
            {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="quantity">
                Quantité <span className="text-destructive">*</span>
              </Label>
              <Input
                id="quantity"
                type="number"
                min="0"
                placeholder="0"
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
              />
              {errors.quantity && <p className="text-xs text-destructive">{errors.quantity}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="unit">
                Unité <span className="text-destructive">*</span>
              </Label>
              {/* `w-full`: `ui/select.tsx`'s trigger ships `w-fit`, so without this the control renders narrower
                  than the sibling « Quantité » input it shares a grid row with. */}
              <Select value={unit} onValueChange={setUnit}>
                <SelectTrigger id="unit" className="w-full">
                  <SelectValue placeholder="Sélectionner une unité" />
                </SelectTrigger>
                <SelectContent>
                  {UNITS.map((u) => (
                    <SelectItem key={u} value={u}>
                      {stockUnitLabel(u)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {errors.unit && <p className="text-xs text-destructive">{errors.unit}</p>}
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="minimumStockLevel">
                Stock minimum <span className="text-destructive">*</span>
              </Label>
              <Input
                id="minimumStockLevel"
                type="number"
                min="0"
                placeholder="0"
                value={minimumStockLevel}
                onChange={(e) => setMinimumStockLevel(e.target.value)}
              />
              {errors.minimumStockLevel && <p className="text-xs text-destructive">{errors.minimumStockLevel}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="category">
                Catégorie <span className="text-destructive">*</span>
              </Label>
              {/* The VALUE stays the English storage key; only the label is French (see `stockCategoryLabel`).
                  An `editingItem` whose stored category is not in the list would otherwise reset the Select to
                  its placeholder and a save would silently rewrite the item's category. */}
              <Select value={category} onValueChange={setCategory}>
                <SelectTrigger id="category" className="w-full">
                  <SelectValue placeholder="Sélectionner une catégorie" />
                </SelectTrigger>
                <SelectContent>
                  {STOCK_CATEGORIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {stockCategoryLabel(c)}
                    </SelectItem>
                  ))}
                  {category !== "" && !STOCK_CATEGORIES.includes(category) && (
                    <SelectItem value={category}>{stockCategoryLabel(category)}</SelectItem>
                  )}
                </SelectContent>
              </Select>
              {errors.category && <p className="text-xs text-destructive">{errors.category}</p>}
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="unitPrice">Prix unitaire</Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a dinar amount, so `step="0.01"`
                  put the millime out of reach and the comma the app prints with was refused outright. The three
                  fields above it stay real number inputs — a quantity and a stock threshold are integers. */}
              <Input
                id="unitPrice"
                type="text"
                inputMode="decimal"
                placeholder="Optionnel"
                value={unitPrice}
                onChange={(e) => setUnitPrice(e.target.value)}
              />
              {errors.unitPrice && <p className="text-xs text-destructive">{errors.unitPrice}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="supplier">Fournisseur</Label>
              <Input
                id="supplier"
                placeholder="Optionnel"
                value={supplier}
                onChange={(e) => setSupplier(e.target.value)}
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              placeholder="Optionnel"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Enregistrement…" : editingItem ? "Mettre à jour" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
