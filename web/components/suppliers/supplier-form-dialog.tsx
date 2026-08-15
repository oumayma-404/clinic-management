"use client"

import type React from "react"
import { useEffect, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { CategoryCombobox } from "@/components/ui/category-combobox"
import { ApiError } from "@/lib/api/client"
import { suppliersApi, SUPPLIER_DUPLICATE_CODE } from "@/lib/api/suppliers"
import type { SupplierDto } from "@/lib/api/types"
import { isDeliverablePhone } from "@/lib/phone"

/**
 * Create / edit a fournisseur.
 *
 * <p><b>Only the nom is required</b> (AC-1) — a practice files a dépôt from a delivery note that carries nothing
 * else, and demanding a category or a number would mean the record does not get made at all.</p>
 *
 * <p>⚠️ <b>A non-Tunisian number is accepted, with a note rather than an error</b> (EC-1). Refusing the save
 * would lose a real foreign supplier; what a non-deliverable number actually costs is the WhatsApp action, and
 * saying so is more useful than a refusal. The note is `role="status"`, not a `text-destructive` line, because
 * nothing is wrong.</p>
 *
 * <p>⚠️ The duplicate refusal is branched on the server's <b>code</b> (`supplier_duplicate`), never on its French
 * sentence — and it is surfaced in the banner with the dialog left open and every field as typed, so correcting
 * the name costs one edit rather than a full retype.</p>
 */
interface SupplierFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Null = create. */
  editing?: SupplierDto | null
  categories: string[]
  onSaved: (supplier: SupplierDto) => void
}

export function SupplierFormDialog({
  open,
  onOpenChange,
  editing,
  categories,
  onSaved,
}: SupplierFormDialogProps) {
  const [name, setName] = useState("")
  const [category, setCategory] = useState("")
  const [phoneNumber, setPhoneNumber] = useState("")
  const [address, setAddress] = useState("")
  const [notes, setNotes] = useState("")
  const [nameError, setNameError] = useState("")
  const [banner, setBanner] = useState("")
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setName(editing?.name ?? "")
    setCategory(editing?.category ?? "")
    setPhoneNumber(editing?.phoneNumber ?? "")
    setAddress(editing?.address ?? "")
    setNotes(editing?.notes ?? "")
    setNameError("")
    setBanner("")
  }, [editing, open])

  const phoneTyped = phoneNumber.trim() !== ""
  const phoneUnreachable = phoneTyped && !isDeliverablePhone(phoneNumber)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      setNameError("Le nom est requis")
      return
    }
    setNameError("")
    setBanner("")

    const payload = {
      name: name.trim(),
      category: category.trim() || null,
      phoneNumber: phoneNumber.trim() || null,
      address: address.trim() || null,
      notes: notes.trim() || null,
    }

    try {
      setSaving(true)
      const saved = editing
        ? await suppliersApi.update(editing.id, { ...payload, version: editing.version })
        : await suppliersApi.create(payload)
      toast.success(editing ? "Fournisseur mis à jour" : "Fournisseur ajouté")
      onOpenChange(false)
      onSaved(saved)
    } catch (err) {
      if (err instanceof ApiError && err.code === SUPPLIER_DUPLICATE_CODE) {
        // The server names the existing record; showing it verbatim is what makes the message useful.
        setBanner(err.message)
      } else {
        setBanner(err instanceof ApiError ? err.message : "L'enregistrement du fournisseur a échoué.")
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* Breakpoint-prefixed: an unprefixed max-w removes the base gutter and leaves the dialog edge-to-edge
          on a phone while still losing to `sm:max-w-lg` on a desktop. */}
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>{editing ? "Modifier le fournisseur" : "Nouveau fournisseur"}</DialogTitle>
          <DialogDescription>
            Un laboratoire de prothèse, un laboratoire d'analyses, un dépôt dentaire, un prestataire de
            maintenance — seul le nom est obligatoire.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {banner ? <FormErrorBanner message={banner} /> : null}

          <div className="space-y-2">
            <Label htmlFor="supplier-name">
              Nom <span className="text-destructive">*</span>
            </Label>
            <Input
              id="supplier-name"
              placeholder="ex. : Laboratoire Dentaire Sfax"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
            {nameError ? <p className="text-xs text-destructive">{nameError}</p> : null}
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="supplier-category">Catégorie</Label>
              <CategoryCombobox
                id="supplier-category"
                value={category}
                onChange={setCategory}
                options={categories}
                placeholder="Optionnel"
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="supplier-phone">Téléphone</Label>
              <Input
                id="supplier-phone"
                type="tel"
                inputMode="tel"
                placeholder="ex. : 71 234 567"
                value={phoneNumber}
                onChange={(e) => setPhoneNumber(e.target.value)}
              />
              {phoneUnreachable ? (
                <p role="status" className="text-xs text-muted-foreground">
                  Ce numéro sera enregistré, mais l'action WhatsApp ne sera pas proposée : elle demande un
                  numéro tunisien à 8 chiffres.
                </p>
              ) : null}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="supplier-address">Adresse</Label>
            <Input
              id="supplier-address"
              placeholder="Optionnel"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="supplier-notes">Notes</Label>
            <Textarea
              id="supplier-notes"
              placeholder="Optionnel"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={saving}
              className="coarse:h-11"
            >
              Annuler
            </Button>
            <Button type="submit" disabled={saving} className="coarse:h-11">
              {saving ? "Enregistrement…" : editing ? "Mettre à jour" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
