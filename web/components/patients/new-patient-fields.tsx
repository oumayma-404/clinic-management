"use client"

import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { PhoneField } from "@/components/ui/phone-field"
import type { InlineNewPatient } from "@/lib/hooks/use-inline-new-patient"

/**
 * Prénom · Nom · Téléphone for a walk-in, inside another form (guided-first-days D) — the booking dialog and the
 * devis form. State and the create live in `useInlineNewPatient`.
 *
 * <p>The patient is created but the host's own save is not — the state left when a booking is refused after the
 * patient went in. Saying so is what makes the read-only fields legible instead of looking broken.</p>
 */
export function NewPatientFields({
  patient,
  idPrefix = "",
  savedWhat = "le rendez-vous",
  disabled = false,
}: {
  patient: InlineNewPatient
  /** Keeps two forms' field ids apart. The booking dialog uses none (its e2e specs target `#firstName`). */
  idPrefix?: string
  /** What the host saves after the patient — « le rendez-vous », « le devis ». */
  savedWhat?: string
  disabled?: boolean
}) {
  const locked = patient.alreadyCreated || disabled
  const firstId = `${idPrefix}firstName`
  const lastId = `${idPrefix}lastName`
  const phoneId = `${idPrefix}newPatientPhone`

  return (
    <div className="space-y-3">
      {patient.alreadyCreated && (
        <p
          role="status"
          className="rounded-md border border-warning/40 bg-warning-wash px-3 py-2 text-xs text-warning-ink"
        >
          <b>{patient.createdName ?? "Ce patient"}</b> est créé, pas {savedWhat} : réessayez.
        </p>
      )}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor={firstId} className="text-sm">
            Prénom *
          </Label>
          <Input
            id={firstId}
            placeholder="Mohamed"
            value={patient.firstName}
            onChange={(e) => patient.setFirstName(e.target.value)}
            className="h-10"
            disabled={locked}
            required
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor={lastId} className="text-sm">
            Nom *
          </Label>
          <Input
            id={lastId}
            placeholder="Ben Salah"
            value={patient.lastName}
            onChange={(e) => patient.setLastName(e.target.value)}
            className="h-10"
            disabled={locked}
            required
          />
        </div>
      </div>
      <div className="space-y-2">
        <Label htmlFor={phoneId} className="text-sm">
          Téléphone
        </Label>
        <PhoneField
          id={phoneId}
          placeholder="Ex. 20 123 456"
          value={patient.phone}
          onChange={patient.setPhone}
          country={patient.country}
          onCountryChange={patient.setCountry}
          disabled={locked}
        />
      </div>
    </div>
  )
}
