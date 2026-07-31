"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Loader2, Plus, Trash2 } from "lucide-react"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { stockApi } from "@/lib/api/stock"
import { getErrorMessage } from "@/lib/errors"
import type { ProcedureTypeDto, StockItemDto } from "@/lib/api/types"

interface ProcedureTypeMaterialsDialogProps {
  /** The act being edited; null closes the dialog. */
  procedureType: ProcedureTypeDto | null
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}

interface MaterialRow {
  stockItemId: string
  quantity: string
}

/**
 * The material-list editor for one act (AC-P4.14) — « Consommables ». The missing admin surface for
 * `ProcedureType.SetMaterials`: the entity method, the join table and the consumption service all shipped
 * without it, so a list could previously only be inserted straight into the database.
 *
 * Deliberately its own dialog rather than a section of `procedure-type-form-modal.tsx`, following the same
 * reasoning as `doctor-document-identity-dialog.tsx`: the list is saved by its own replace-semantics endpoint
 * (`PUT /procedure-types/{id}/materials`), it only exists for an act that already has an id, and folding it
 * into the form's save would mean two sequential requests with a window where one succeeded and the other
 * did not — for a list whose empty state is a meaningful value, that window silently clears it.
 */
export function ProcedureTypeMaterialsDialog({
  procedureType,
  onOpenChange,
  onSaved,
}: ProcedureTypeMaterialsDialogProps) {
  const [items, setItems] = useState<StockItemDto[]>([])
  const [loadingItems, setLoadingItems] = useState(false)
  const [rows, setRows] = useState<MaterialRow[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const open = !!procedureType

  const loadItems = useCallback(async () => {
    try {
      setLoadingItems(true)
      setItems(await stockApi.list())
    } catch (err) {
      // The catalogue is what makes the picker usable, so a failure is stated rather than shown as an
      // empty list — an empty picker and an empty stock cupboard look identical otherwise.
      setError(getErrorMessage(err, "Échec du chargement des articles de stock"))
    } finally {
      setLoadingItems(false)
    }
  }, [])

  // Reset from the act every time the dialog opens, so a cancelled edit never leaks into the next one.
  useEffect(() => {
    if (!procedureType) return
    setError(null)
    setRows(
      procedureType.materials.map((m) => ({
        stockItemId: m.stockItemId,
        quantity: String(m.quantityPerAct),
      })),
    )
    loadItems()
  }, [procedureType, loadItems])

  const itemName = useCallback(
    (id: string) => items.find((i) => i.id === id)?.name ?? "Article inconnu",
    [items],
  )

  // An item already on the list is not offered again: the server refuses duplicates, so offering one would
  // only let the operator build a list that cannot be saved.
  const availableFor = useCallback(
    (rowIndex: number) =>
      items.filter(
        (i) => !rows.some((r, idx) => idx !== rowIndex && r.stockItemId === i.id),
      ),
    [items, rows],
  )

  const addRow = () => setRows((prev) => [...prev, { stockItemId: "", quantity: "1" }])
  const removeRow = (index: number) => setRows((prev) => prev.filter((_, i) => i !== index))
  const patchRow = (index: number, patch: Partial<MaterialRow>) =>
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const allRowsComplete = useMemo(
    () => rows.every((r) => r.stockItemId !== "" && Number.parseInt(r.quantity, 10) > 0),
    [rows],
  )

  const handleSave = async () => {
    if (!procedureType) return

    if (!allRowsComplete) {
      setError("Chaque ligne doit désigner un article et une quantité supérieure à 0.")
      return
    }

    try {
      setSaving(true)
      setError(null)
      await procedureTypesApi.setMaterials(
        procedureType.id,
        rows.map((r) => ({ stockItemId: r.stockItemId, quantityPerAct: Number.parseInt(r.quantity, 10) })),
      )
      toast.success(
        rows.length === 0
          ? `« ${procedureType.name} » ne consomme plus de stock`
          : `Consommables enregistrés pour « ${procedureType.name} »`,
      )
      onSaved()
      onOpenChange(false)
    } catch (err) {
      // Dialog stays open with the rows intact so the operator can correct rather than retype.
      setError(getErrorMessage(err, "Échec de l'enregistrement des consommables"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => { if (!next) onOpenChange(false) }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto md:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            Consommables{procedureType ? ` — ${procedureType.name}` : ""}
          </DialogTitle>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          Le stock indiqué ici est déduit automatiquement à chaque fois que cet acte est enregistré dans une
          fiche de soins. Une liste vide signifie que l&apos;acte ne consomme rien.
        </p>

        {error && <FormErrorBanner message={error} />}

        {loadingItems ? (
          <div className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            Chargement des articles…
          </div>
        ) : (
          <div className="space-y-3">
            {rows.length === 0 && (
              <p className="rounded-md border border-dashed border-border px-3 py-4 text-sm text-muted-foreground">
                Aucun consommable. Cet acte ne déduit rien du stock.
              </p>
            )}

            {rows.map((row, index) => (
              <div key={index} className="flex items-end gap-2">
                <div className="min-w-0 flex-1 space-y-2">
                  <Label htmlFor={`material-item-${index}`}>Article</Label>
                  <Select
                    value={row.stockItemId}
                    onValueChange={(value) => patchRow(index, { stockItemId: value })}
                  >
                    <SelectTrigger id={`material-item-${index}`}>
                      <SelectValue placeholder="Choisir un article" />
                    </SelectTrigger>
                    <SelectContent>
                      {availableFor(index).map((item) => (
                        <SelectItem key={item.id} value={item.id}>
                          {item.name} ({item.unit})
                        </SelectItem>
                      ))}
                      {/* An item that has since been removed from the catalogue must still render, or saving
                          an unrelated line would silently drop it from the list. */}
                      {row.stockItemId !== "" && !items.some((i) => i.id === row.stockItemId) && (
                        <SelectItem value={row.stockItemId}>{itemName(row.stockItemId)}</SelectItem>
                      )}
                    </SelectContent>
                  </Select>
                </div>

                <div className="w-24 space-y-2">
                  <Label htmlFor={`material-qty-${index}`}>Quantité</Label>
                  <Input
                    id={`material-qty-${index}`}
                    type="number"
                    min="1"
                    step="1"
                    value={row.quantity}
                    onChange={(e) => patchRow(index, { quantity: e.target.value })}
                  />
                </div>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => removeRow(index)}
                  className="h-9 w-9 p-0 text-destructive hover:text-destructive"
                  aria-label={`Retirer ${row.stockItemId ? itemName(row.stockItemId) : "cette ligne"}`}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}

            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={addRow}
              className="gap-2"
              disabled={items.length > 0 && rows.length >= items.length}
            >
              <Plus className="h-4 w-4" />
              Ajouter un consommable
            </Button>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={saving || loadingItems}>
            {saving ? "Enregistrement…" : "Enregistrer"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
