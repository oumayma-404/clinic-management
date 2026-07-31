"use client"

import { useEffect, useRef, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import Image from "next/image"
import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Upload, Trash2, UserCircle, Stethoscope } from "lucide-react"
import { toast } from "sonner"
import { doctorsApi } from "@/lib/api/doctors"
import { ApiError } from "@/lib/api/client"
import type { DoctorProfileDto } from "@/lib/api/types"
import { specialtyLabel } from "@/lib/specialties"
import { DoctorWorkingHoursCard } from "@/components/doctor-working-hours-card"

export function MonProfilContent() {
  const [profile, setProfile] = useState<DoctorProfileDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [ordreNumber, setOrdreNumber] = useState("")
  const [cachetFile, setCachetFile] = useState<File | null>(null)
  const [cachetPreview, setCachetPreview] = useState<string | null>(null) // object URL of the current/selected cachet
  const [removeCachet, setRemoveCachet] = useState(false)
  // AC-P4.21 — `doctors` was emitted from the start with nothing listening. An admin can set THIS
  // practitioner's ordre CNOMDT and cachet from « Paramètres → Médecins » (doctor-document-identity-dialog),
  // so the person whose identity it is watched it change on someone else's screen and not on their own.
  const [reloadKey, setReloadKey] = useState(0)

  // Tracks the currently-set object URL (loaded OR later selected via handleFile) so it can be revoked on
  // unmount — the load effect's local closure only knew the initially-loaded URL, leaking any later one.
  const cachetPreviewUrlRef = useRef<string | null>(null)
  useEffect(() => () => {
    if (cachetPreviewUrlRef.current) URL.revokeObjectURL(cachetPreviewUrlRef.current)
  }, [])

  // Load the profile + (if present) the existing cachet image.
  useEffect(() => {
    let cancelled = false
    let objectUrl: string | null = null
    setLoading(true)
    doctorsApi
      .getMyProfile()
      .then(async (p) => {
        if (cancelled) return
        setProfile(p)
        setOrdreNumber(p.ordreNumberCnomdt ?? "")
        if (p.hasCachet) {
          try {
            const blob = await doctorsApi.fetchCachetBlob(p.id)
            if (cancelled) return
            objectUrl = URL.createObjectURL(blob)
            cachetPreviewUrlRef.current = objectUrl
            setCachetPreview(objectUrl)
          } catch {
            /* preview is best-effort; the "cachet enregistré" state still shows */
          }
        }
      })
      .catch((e) => {
        if (!cancelled) setLoadError(e instanceof ApiError ? e.message : "Impossible de charger le profil.")
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [reloadKey])

  // Skipped while a save is in flight: a broadcast landing between the PUT and its response would refetch the
  // pre-save row and put the old ordre number back into the field the user just submitted. The save's own
  // response already updates the form, so nothing is lost by ignoring the signal for that moment.
  useClinicRealtime(RealtimeResource.Doctors, () => {
    if (!saving) setReloadKey((k) => k + 1)
  })

  const handleFile = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    setCachetFile(file)
    setRemoveCachet(false)
    setCachetPreview((prev) => {
      if (prev) URL.revokeObjectURL(prev)
      const url = URL.createObjectURL(file)
      cachetPreviewUrlRef.current = url
      return url
    })
  }

  const handleRemove = () => {
    setCachetFile(null)
    setRemoveCachet(true)
    setCachetPreview((prev) => {
      if (prev) URL.revokeObjectURL(prev)
      cachetPreviewUrlRef.current = null
      return null
    })
  }

  const handleSave = async () => {
    setSaving(true)
    try {
      const updated = await doctorsApi.updateMyProfile({
        ordreNumberCnomdt: ordreNumber,
        cachet: cachetFile,
        removeCachet: removeCachet,
      })
      setProfile(updated)
      setCachetFile(null)
      setRemoveCachet(false)
      toast.success("Profil enregistré")
    } catch (e) {
      toast.error("Erreur", { description: e instanceof ApiError ? e.message : "Enregistrement impossible" })
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return <Card className="p-6"><p className="text-center text-muted-foreground">Chargement du profil...</p></Card>
  }

  if (loadError || !profile) {
    return (
      <Card className="p-6">
        <p className="text-sm text-muted-foreground">
          {loadError ?? "Aucun profil praticien n'est associé à votre compte."}
        </p>
      </Card>
    )
  }

  return (
    <div className="space-y-6">
      {/* Identity (read-only) */}
      <Card className="p-6">
        <div className="flex items-center gap-2 font-semibold mb-4"><UserCircle className="h-5 w-5" /> Identité</div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
          <div>
            <div className="text-xs text-muted-foreground">Nom complet</div>
            <div className="mt-1 font-medium">{profile.fullName}</div>
          </div>
          <div>
            <div className="text-xs text-muted-foreground">Spécialité</div>
            {/* AC-P2.42 — French label over the English storage key; a custom value renders verbatim. */}
            <div className="mt-1 font-medium">{specialtyLabel(profile.specialty)}</div>
          </div>
        </div>
        <p className="text-xs text-muted-foreground mt-3">Ces champs sont gérés dans Paramètres → Médecins.</p>
      </Card>

      {/* Document identity (editable) */}
      <Card className="p-6 space-y-5">
        <div className="flex items-center gap-2 font-semibold"><Stethoscope className="h-5 w-5" /> Informations pour les documents</div>

        <div className="space-y-1.5 max-w-md">
          <Label htmlFor="ordre" className="text-sm">
            Numéro d&apos;ordre — Ordre National des Médecins Dentistes (CNOMDT)
          </Label>
          <Input
            id="ordre"
            value={ordreNumber}
            onChange={(e) => setOrdreNumber(e.target.value)}
            placeholder="Ex : D-04-1287"
            disabled={saving}
          />
          <p className="text-xs text-muted-foreground">Pré-rempli automatiquement sur vos certificats et courriers.</p>
        </div>

        <div className="space-y-2">
          <Label className="text-sm">Cachet / signature</Label>
          <div className="flex items-center gap-4">
            {cachetPreview ? (
              <div className="relative w-40 h-24 rounded-lg border-2 border-primary/30 overflow-hidden bg-white group">
                <Image src={cachetPreview} alt="Cachet" fill className="object-contain" unoptimized />
                {/*
                  AC-11 — the same two-rendering treatment as the clinic logo. The hover overlay is the only
                  way to clear a cachet, and hover does not exist on the tablet a dentist signs from; but an
                  always-on full-bleed overlay would hide the cachet it is previewing and make a stray tap
                  delete a signature. A coarse pointer gets a corner button instead.
                */}
                <button
                  type="button"
                  onClick={handleRemove}
                  disabled={saving}
                  className="absolute inset-0 hidden bg-black/50 opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-100 hover-hover:flex items-center justify-center"
                  aria-label="Supprimer le cachet"
                >
                  <span className="bg-white rounded-full p-1.5"><Trash2 className="h-4 w-4 text-destructive" /></span>
                </button>
                <button
                  type="button"
                  onClick={handleRemove}
                  disabled={saving}
                  className="touch-target absolute -right-1 -top-1 hidden rounded-full bg-white p-1.5 shadow ring-1 ring-border coarse:block"
                  aria-label="Supprimer le cachet"
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </button>
              </div>
            ) : (
              <label className="w-40 h-24 flex flex-col items-center justify-center border-2 border-dashed border-slate-300 dark:border-slate-700 rounded-lg cursor-pointer hover:border-primary hover:bg-accent transition text-slate-400 hover:text-primary">
                <Upload className="h-5 w-5" />
                <span className="text-2xs font-medium mt-1">Charger</span>
                <input type="file" accept="image/*" onChange={handleFile} className="hidden" disabled={saving} />
              </label>
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            PNG ou JPEG. Si aucun cachet n&apos;est chargé, le document affiche une simple ligne de signature.
          </p>
        </div>

        <div className="flex justify-end gap-2 pt-2 border-t border-border">
          <Button onClick={handleSave} disabled={saving}>
            {saving ? "Enregistrement..." : "Enregistrer"}
          </Button>
        </div>
      </Card>

      {/* § 5.4 / AC-P1.25 — a doctor edits their own hours. `profile.id` is the doctorId the endpoint wants. */}
      <DoctorWorkingHoursCard doctorId={profile.id} reloadKey={reloadKey} />

      <div className="rounded-lg border border-border bg-muted/40 px-4 py-3 text-xs text-muted-foreground">
        <span className="font-medium text-foreground">Administrateur :</span> un admin peut définir le cachet et le
        numéro d&apos;ordre d&apos;un autre praticien depuis Paramètres → Médecins.
      </div>
    </div>
  )
}
