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
import type { StockItemDto } from "@/lib/api/types"

const CATEGORIES = ["Medical Supplies", "Medical Equipment", "PPE", "Medications", "Lab Supplies", "Office Supplies"]
const UNITS = ["Unit", "Box", "Bag", "Roll", "Bottle", "Pack", "Liter"]

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
      setUnitPrice(editingItem.unitPrice != null ? String(editingItem.unitPrice) : "")
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
    if (!name.trim()) next.name = "Name is required"
    if (!category) next.category = "Category is required"
    if (!unit) next.unit = "Unit is required"
    if (quantity === "" || Number(quantity) < 0) next.quantity = "Enter a quantity of 0 or more"
    if (minimumStockLevel === "" || Number(minimumStockLevel) < 0) next.minimumStockLevel = "Enter a minimum of 0 or more"
    if (unitPrice !== "" && Number(unitPrice) < 0) next.unitPrice = "Price cannot be negative"
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
      unitPrice: unitPrice !== "" ? Number(unitPrice) : null,
      supplier: supplier.trim() || null,
    }

    try {
      setSaving(true)
      if (editingItem) {
        await stockApi.update(editingItem.id, payload)
        toast.success("Stock item updated")
      } else {
        await stockApi.create(payload)
        toast.success("Stock item added")
      }
      onOpenChange(false)
      onSaved()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed to save stock item")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{editingItem ? "Edit Stock Item" : "Add New Stock Item"}</DialogTitle>
          <DialogDescription>
            {editingItem ? "Update the details of the stock item" : "Enter the details of the new stock item"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="name">
              Item Name <span className="text-destructive">*</span>
            </Label>
            <Input id="name" placeholder="e.g., Surgical Gloves" value={name} onChange={(e) => setName(e.target.value)} />
            {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="quantity">
                Quantity <span className="text-destructive">*</span>
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
                Unit <span className="text-destructive">*</span>
              </Label>
              <Select value={unit} onValueChange={setUnit}>
                <SelectTrigger id="unit">
                  <SelectValue placeholder="Select unit" />
                </SelectTrigger>
                <SelectContent>
                  {UNITS.map((u) => (
                    <SelectItem key={u} value={u}>
                      {u}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {errors.unit && <p className="text-xs text-destructive">{errors.unit}</p>}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="minimumStockLevel">
                Min. Stock Level <span className="text-destructive">*</span>
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
                Category <span className="text-destructive">*</span>
              </Label>
              <Select value={category} onValueChange={setCategory}>
                <SelectTrigger id="category">
                  <SelectValue placeholder="Select category" />
                </SelectTrigger>
                <SelectContent>
                  {CATEGORIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {errors.category && <p className="text-xs text-destructive">{errors.category}</p>}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="unitPrice">Unit Price</Label>
              <Input
                id="unitPrice"
                type="number"
                min="0"
                step="0.01"
                placeholder="Optional"
                value={unitPrice}
                onChange={(e) => setUnitPrice(e.target.value)}
              />
              {errors.unitPrice && <p className="text-xs text-destructive">{errors.unitPrice}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="supplier">Supplier</Label>
              <Input
                id="supplier"
                placeholder="Optional"
                value={supplier}
                onChange={(e) => setSupplier(e.target.value)}
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              placeholder="Optional"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
              Cancel
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Saving..." : editingItem ? "Update Item" : "Add Item"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
