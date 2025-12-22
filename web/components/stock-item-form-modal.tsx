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

interface StockItemFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingItem?: any
}

export function StockItemFormModal({ open, onOpenChange, editingItem }: StockItemFormModalProps) {
  const [itemName, setItemName] = useState("")
  const [itemCode, setItemCode] = useState("")
  const [quantity, setQuantity] = useState("")
  const [unit, setUnit] = useState("")
  const [category, setCategory] = useState("")

  // Populate form when editing
  useEffect(() => {
    if (editingItem) {
      setItemName(editingItem.itemName)
      setItemCode(editingItem.itemCode)
      setQuantity(String(editingItem.quantity))
      setUnit(editingItem.unit)
      setCategory(editingItem.category)
    } else {
      // Reset form for new item
      setItemName("")
      setItemCode("")
      setQuantity("")
      setUnit("")
      setCategory("")
    }
  }, [editingItem, open])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()

    // Validate required fields
    if (!itemName || !itemCode || !quantity || !unit || !category) {
      alert("Please fill in all required fields")
      return
    }

    // Handle form submission
    console.log({
      itemName,
      itemCode,
      quantity: Number(quantity),
      unit,
      category,
    })

    onOpenChange(false)
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
          {/* Item Name */}
          <div className="space-y-2">
            <Label htmlFor="itemName">
              Item Name <span className="text-destructive">*</span>
            </Label>
            <Input
              id="itemName"
              placeholder="e.g., Surgical Gloves"
              value={itemName}
              onChange={(e) => setItemName(e.target.value)}
              required
            />
          </div>

          {/* Item Code */}
          <div className="space-y-2">
            <Label htmlFor="itemCode">
              Item Code <span className="text-destructive">*</span>
            </Label>
            <Input
              id="itemCode"
              placeholder="e.g., SG-001"
              value={itemCode}
              onChange={(e) => setItemCode(e.target.value)}
              required
            />
          </div>

          {/* Quantity and Unit */}
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
                required
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="unit">
                Unit <span className="text-destructive">*</span>
              </Label>
              <Select value={unit} onValueChange={setUnit} required>
                <SelectTrigger id="unit">
                  <SelectValue placeholder="Select unit" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Unit">Unit</SelectItem>
                  <SelectItem value="Box">Box</SelectItem>
                  <SelectItem value="Bag">Bag</SelectItem>
                  <SelectItem value="Roll">Roll</SelectItem>
                  <SelectItem value="Bottle">Bottle</SelectItem>
                  <SelectItem value="Pack">Pack</SelectItem>
                  <SelectItem value="Liter">Liter</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* Category */}
          <div className="space-y-2">
            <Label htmlFor="category">
              Category <span className="text-destructive">*</span>
            </Label>
            <Select value={category} onValueChange={setCategory} required>
              <SelectTrigger id="category">
                <SelectValue placeholder="Select category" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Medical Supplies">Medical Supplies</SelectItem>
                <SelectItem value="Medical Equipment">Medical Equipment</SelectItem>
                <SelectItem value="PPE">PPE</SelectItem>
                <SelectItem value="Medications">Medications</SelectItem>
                <SelectItem value="Lab Supplies">Lab Supplies</SelectItem>
                <SelectItem value="Office Supplies">Office Supplies</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit">{editingItem ? "Update Item" : "Add Item"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
