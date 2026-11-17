"use client"

import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { DoctorDto } from "@/lib/api/clinics"
import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"

/**
 * Which practitioner record an account is (cabinet-de-groupe US-3) — the one block the invite form and
 * « Lier à un praticien » share, so the two cannot disagree about it.
 *
 * ⚠️ When the roster already holds a practitioner with no account, choosing it LINKS that record: inviting a dentist
 * added earlier in Paramètres → Praticiens used to create a second one, which nothing could merge. « Nouveau
 * praticien » creates a record from the three fields, as before.
 */

/** « Nouveau praticien » in the Select — a Radix item cannot carry an empty value. */
export const NEW_PRACTITIONER = "__new__"

/** The roster's practitioners an account may be linked to: still active, linked to nobody. */
export function linkablePractitioners(allDoctors: readonly DoctorDto[]): DoctorDto[] {
  return allDoctors.filter((d) => d.id && d.isActive !== false && !d.userId)
}

/** The choice a form opens on: the one unlinked practitioner whose e-mail is the account's, else a new record. */
export function defaultPractitionerChoice(linkable: readonly DoctorDto[], email: string): string {
  const typed = email.trim().toLowerCase()
  const matches = typed ? linkable.filter((d) => d.email?.trim().toLowerCase() === typed) : []
  return matches.length === 1 && matches[0].id ? matches[0].id : NEW_PRACTITIONER
}

export interface PractitionerFields {
  firstName: string
  lastName: string
  specialty: string
}

interface PractitionerChoiceProps {
  idPrefix: string
  linkable: readonly DoctorDto[]
  choice: string
  onChoiceChange: (choice: string) => void
  fields: PractitionerFields
  onFieldsChange: (fields: PractitionerFields) => void
  disabled?: boolean
}

export function PractitionerChoice({
  idPrefix,
  linkable,
  choice,
  onChoiceChange,
  fields,
  onFieldsChange,
  disabled,
}: PractitionerChoiceProps) {
  return (
    <div className="space-y-4 rounded-lg border bg-muted/40 p-3">
      {linkable.length > 0 && (
        <div className="space-y-2">
          <Label htmlFor={`${idPrefix}-practitioner`}>Fiche praticien</Label>
          <Select value={choice} onValueChange={onChoiceChange} disabled={disabled}>
            <SelectTrigger id={`${idPrefix}-practitioner`} className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {linkable.map((d) => (
                <SelectItem key={d.id} value={d.id!}>
                  {d.name} — déjà dans Paramètres
                </SelectItem>
              ))}
              <SelectItem value={NEW_PRACTITIONER}>Nouveau praticien</SelectItem>
            </SelectContent>
          </Select>
        </div>
      )}

      {choice === NEW_PRACTITIONER && (
        <>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor={`${idPrefix}-firstname`}>Prénom du praticien</Label>
              <Input
                id={`${idPrefix}-firstname`}
                value={fields.firstName}
                onChange={(e) => onFieldsChange({ ...fields, firstName: e.target.value })}
                autoComplete="off"
                disabled={disabled}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor={`${idPrefix}-lastname`}>Nom du praticien</Label>
              <Input
                id={`${idPrefix}-lastname`}
                value={fields.lastName}
                onChange={(e) => onFieldsChange({ ...fields, lastName: e.target.value })}
                autoComplete="off"
                disabled={disabled}
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor={`${idPrefix}-specialty`}>Spécialité</Label>
            <Select
              value={fields.specialty}
              onValueChange={(specialty) => onFieldsChange({ ...fields, specialty })}
              disabled={disabled}
            >
              <SelectTrigger id={`${idPrefix}-specialty`} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DOCTOR_SPECIALTIES.map((specialty) => (
                  <SelectItem key={specialty} value={specialty}>
                    {specialtyLabel(specialty)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </>
      )}
    </div>
  )
}

/** Is the choice complete enough to send? A new record needs its three fields. */
export function practitionerChoiceReady(choice: string, fields: PractitionerFields): boolean {
  return choice !== NEW_PRACTITIONER || Boolean(fields.firstName.trim() && fields.lastName.trim() && fields.specialty)
}

/** The request half: link an existing record, or create one. */
export function practitionerPayload(choice: string, fields: PractitionerFields) {
  return choice === NEW_PRACTITIONER
    ? {
        doctorInfo: {
          firstName: fields.firstName.trim(),
          lastName: fields.lastName.trim(),
          specialty: fields.specialty,
        },
      }
    : { existingDoctorId: choice }
}
