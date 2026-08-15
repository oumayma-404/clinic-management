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
import { CategoryCombobox } from "@/components/ui/category-combobox"
import { SupplierPicker } from "@/components/suppliers/supplier-picker"
import { SupplierFormDialog } from "@/components/suppliers/supplier-form-dialog"
import { stockApi } from "@/lib/api/stock"
import { ApiError } from "@/lib/api/client"
import { formatAmount, parseAmountInput } from "@/lib/format"
import type { StockItemDto } from "@/lib/api/types"

/**
 * ⚠️ `STOCK_CATEGORIES` and `STOCK_CATEGORY_LABELS_FR` used to live here — six English storage keys and a French
 * display map, this repo's standing convention for a **closed** value set. The set stopped being closed:
 * `GET /api/stock` already served the clinic's own distinct categories as a filter facet, and nothing ever
 * refused a category typed straight into the database — so a clinic-authored one rendered raw beside six
 * translated ones. The storage key is the French label now (`Domain/Services/StockCategories`), the suppliers
 * migration rewrote the six existing keys, and the options come from the server like every other open set.
 */

const UNITS = ["Unit", "Box", "Bag", "Roll", "Bottle", "Pack", "Liter"]

/** The unit is still a CLOSED set persisted in English and read in French — unlike the category,
 * which became an open set served by the server. */
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
  /**
   * The catégorie options — the canonical suggestions unioned with the clinic's own, as `GET /api/stock`
   * already returns them. Passed in rather than fetched here: the stock page has them for its own filter, and a
   * second read would be a second answer to what this clinic files articles under.
   */
  categories?: string[]
}

export function StockItemFormModal({
  open,
  onOpenChange,
  editingItem,
  onSaved,
  categories = [],
}: StockItemFormModalProps) {
  const [name, setName] = useState("")
  const [category, setCategory] = useState("")
  const [unit, setUnit] = useState("")
  const [quantity, setQuantity] = useState("")
  const [minimumStockLevel, setMinimumStockLevel] = useState("")
  const [description, setDescription] = useState("")
  const [unitPrice, setUnitPrice] = useState("")
  const [supplierId, setSupplierId] = useState<string | null>(null)
  const [supplierCreateOpen, setSupplierCreateOpen] = useState(false)
  const [supplierReloadKey, setSupplierReloadKey] = useState(0)
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
      setSupplierId(editingItem.supplierId ?? null)
    } else {
      setName("")
      setCategory("")
      setUnit("")
      setQuantity("")
      setMinimumStockLevel("")
      setDescription("")
      setUnitPrice("")
      setSupplierId(null)
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
      // Always present, never omitted: the update command reads an ABSENT key as « unchanged », so
      // `|| undefined` here would make clearing a fournisseur silently succeed and do nothing (AC-5).
      supplierId: supplierId,
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
              {/* A combobox, not a Select (AC-2): the categories are *suggestions* and a practice may file an
                  article under one of its own. The list already includes the clinic's own, which is what makes an
                  open set converge — and typing a variant is safe, since the server folds « prothese » back onto
                  the canonical spelling on save. */}
              <CategoryCombobox
                id="category"
                value={category}
                onChange={setCategory}
                options={categories}
                placeholder="Sélectionner une catégorie"
                emptyLabel="Sans catégorie"
              />
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
              {/* Was a free-text input, which is how the same dépôt ended up under three spellings with no
                  number behind any of them. « Aucun » is the common case and stays one tap away (AC-5). */}
              <SupplierPicker
                id="supplier"
                value={supplierId}
                onChange={setSupplierId}
                selectedFallback={
                  editingItem?.supplierId && editingItem.supplierName
                    ? { id: editingItem.supplierId, name: editingItem.supplierName }
                    : null
                }
                onCreateNew={() => setSupplierCreateOpen(true)}
                reloadKey={supplierReloadKey}
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

      {/* « + Créer un fournisseur » from inside the picker. It selects the new supplier straight away, which is
          the whole reason the inline path exists — sending the user to /fournisseurs and back would lose
          everything already typed into this form. */}
      <SupplierFormDialog
        open={supplierCreateOpen}
        onOpenChange={setSupplierCreateOpen}
        editing={null}
        categories={[]}
        onSaved={(created) => {
          setSupplierId(created.id)
          setSupplierReloadKey((k) => k + 1)
        }}
      />
    </Dialog>
  )
}
