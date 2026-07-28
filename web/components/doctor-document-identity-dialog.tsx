"use client"

import { useEffect, useRef, useState } from "react"
import Image from "next/image"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Upload, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { doctorsApi } from "@/lib/api/doctors"
import { ApiError } from "@/lib/api/client"

export interface DoctorDocumentIdentityTarget {
  id: string
  name: string
  ordreNumberCnomdt?: string | null
  hasCachet?: boolean
}

interface DoctorDocumentIdentityDialogProps {
  /** The practitioner to edit; null closes the dialog. */
  doctor: DoctorDocumentIdentityTarget | null
  onOpenChange: (open: boolean) => void
  onSaved?: () => void
}

const MAX_CACHET_BYTES = 2 * 1024 * 1024
const ACCEPTED_CACHET_TYPES = ["image/png", "image/jpeg", "image/jpg"]

/**
 * Set another practitioner's **document identity** — their CNOMDT ordre number and their cachet (AC-P2.30).
 *
 * Deliberately separate from the « Médecins » roster save: the roster posts `PUT /clinics/doctors`, which
 * rewrites names/specialties/contact and **deletes any doctor not echoed back**, and it neither reads nor writes
 * these two fields. Document identity has its own endpoint (`PUT /api/doctors/{id}`, own-or-admin, multipart),
 * so it gets its own dialog rather than being smuggled into a bulk roster rewrite.
 *
 * The validation here mirrors the server's (type allow-list + 2 MB) so an oversized file is refused before the
 * upload rather than after it; the server still re-checks, including the magic bytes.
 */
export function DoctorDocumentIdentityDialog({
  doctor,
  onOpenChange,
  onSaved,
}: DoctorDocumentIdentityDialogProps) {
  const [ordreNumber, setOrdreNumber] = useState("")
  const [cachetFile, setCachetFile] = useState<File | null>(null)
  const [cachetPreview, setCachetPreview] = useState<string | null>(null)
  const [removeCachet, setRemoveCachet] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!doctor) return
    setOrdreNumber(doctor.ordreNumberCnomdt ?? "")
    setCachetFile(null)
    setCachetPreview(null)
    setRemoveCachet(false)
    setError(null)
  }, [doctor])

  const pickCachet = (file: File | undefined) => {
    if (!file) return
    if (!ACCEPTED_CACHET_TYPES.includes(file.type)) {
      setError("Le cachet doit être une image PNG ou JPEG.")
      return
    }
    if (file.size > MAX_CACHET_BYTES) {
      setError("Le cachet est trop volumineux (2 Mo maximum).")
      return
    }
    setError(null)
    setCachetFile(file)
    setRemoveCachet(false)
    const reader = new FileReader()
    reader.onloadend = () => setCachetPreview(reader.result as string)
    reader.readAsDataURL(file)
  }

  const handleSave = async () => {
    if (!doctor) return
    setSaving(true)
    setError(null)
    try {
      await doctorsApi.updateProfile(doctor.id, {
        ordreNumberCnomdt: ordreNumber.trim(),
        cachet: cachetFile,
        removeCachet,
      })
      toast.success(`Identité documentaire de ${doctor.name} enregistrée.`)
      onSaved?.()
      onOpenChange(false)
    } catch (err) {
      // A non-admin editing someone else is refused by the handler with « Vous ne pouvez modifier que votre
      // propre profil. » — surfaced here rather than gated client-side, matching the repo's precedent.
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement du profil praticien.")
    } finally {
      setSaving(false)
    }
  }

  const showsExistingCachet = !!doctor?.hasCachet && !cachetPreview && !removeCachet

  return (
    <Dialog open={!!doctor} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Identité documentaire</DialogTitle>
          <DialogDescription>
            {doctor?.name} — numéro d&apos;ordre CNOMDT et cachet imprimés sur les certificats, ordonnances et
            lettres de liaison.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <FormErrorBanner message={error} />

          <div className="space-y-1.5">
            <Label htmlFor="doctor-ordre">Numéro d&apos;ordre (CNOMDT)</Label>
            <Input
              id="doctor-ordre"
              value={ordreNumber}
              onChange={(e) => setOrdreNumber(e.target.value)}
              placeholder="Ex. 12345"
              disabled={saving}
            />
            <p className="text-xs text-muted-foreground">
              Laisser vide pour le retirer des documents.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label>Cachet</Label>
            {cachetPreview ? (
              <div className="rounded-md border bg-muted/30 p-2">
                {/* eslint-disable-next-line @next/next/no-img-element -- data: URI preview of a just-picked file */}
                <Image
                  src={cachetPreview}
                  alt="Aperçu du cachet"
                  width={200}
                  height={80}
                  unoptimized
                  className="max-h-20 w-auto object-contain"
                />
              </div>
            ) : showsExistingCachet ? (
              <p className="text-sm text-muted-foreground">Un cachet est déjà enregistré pour ce praticien.</p>
            ) : (
              <p className="text-sm text-muted-foreground">
                {removeCachet ? "Le cachet sera retiré à l'enregistrement." : "Aucun cachet enregistré."}
              </p>
            )}
            <input
              ref={fileInputRef}
              type="file"
              accept="image/png,image/jpeg"
              className="hidden"
              onChange={(e) => pickCachet(e.target.files?.[0])}
            />
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="gap-2"
                disabled={saving}
                onClick={() => fileInputRef.current?.click()}
              >
                <Upload className="h-4 w-4" />
                {cachetPreview ? "Choisir une autre image" : "Téléverser un cachet"}
              </Button>
              {(showsExistingCachet || cachetPreview) && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="gap-2 text-destructive hover:text-destructive"
                  disabled={saving}
                  onClick={() => {
                    setCachetFile(null)
                    setCachetPreview(null)
                    setRemoveCachet(true)
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                  Retirer le cachet
                </Button>
              )}
            </div>
            <p className="text-xs text-muted-foreground">PNG ou JPEG, 2 Mo maximum.</p>
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? "Enregistrement…" : "Enregistrer"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
