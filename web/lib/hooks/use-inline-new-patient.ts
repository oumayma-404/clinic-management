"use client"

import { useCallback, useRef, useState } from "react"
import type { CountryCode } from "libphonenumber-js/max"

import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { patientsApi } from "@/lib/api/patients"
import { DEFAULT_REGION, isDeliverablePhone, PHONE_ERROR_FR } from "@/lib/phone"

/** What a carried draft keeps of the form (`useCarriedDraft` — JSON only). */
export interface InlineNewPatientSnapshot {
  firstName: string
  lastName: string
  phone: string
  country: CountryCode
  createdId: string | null
}

export type EnsureCreatedOutcome =
  | { kind: "created"; id: string }
  /** « Ce patient existe peut-être déjà » — the server's own sentence, a question for the user, never an error. */
  | { kind: "duplicate"; message: string }
  | { kind: "failed"; error: string }

/**
 * « Nouveau patient » inside another form — the booking dialog and the devis form (guided-first-days D). One owner,
 * because both doors must answer the same three questions the same way: what a valid walk-in is (a first and last
 * name; a number only if deliverable), what to do when the server says the person may already be on file, and how
 * never to create the same patient twice.
 *
 * <p>⚠️ <b>Reuse before create.</b> A host re-runs its save from the top on every confirmation (slot taken, out of
 * hours, « créer quand même »), so {@link ensureCreated} returns the patient already created by an earlier attempt
 * rather than creating a second one — the booking dialog once put the same walk-in on /patients twice. The id is a
 * ref because the retry reads it in the same tick the confirmation is granted; {@link createdName} is the render
 * signal (a ref change re-renders nothing), and once it is set the fields go read-only: the record exists under the
 * name typed, and editing this form cannot reach it.</p>
 */
export function useInlineNewPatient() {
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [phone, setPhone] = useState("")
  const [country, setCountry] = useState<CountryCode>(DEFAULT_REGION)
  const createdIdRef = useRef<string | null>(null)
  const [createdName, setCreatedName] = useState<string | null>(null)

  /** The name as typed, or null — what a prompt or a recap calls this person before they exist. */
  const typedName = [firstName.trim(), lastName.trim()].filter(Boolean).join(" ") || null

  /** The French reason the fields cannot be saved yet, or null. */
  const validate = useCallback((): string | null => {
    if (!firstName.trim() || !lastName.trim()) return "Saisissez le prénom et le nom du nouveau patient."
    // Optional: a walk-in is often registered with a name alone. A number that IS given must be deliverable.
    if (phone.trim() && !isDeliverablePhone(phone, country)) return PHONE_ERROR_FR
    return null
  }, [firstName, lastName, phone, country])

  const ensureCreated = useCallback(
    async (allowDuplicate: boolean): Promise<EnsureCreatedOutcome> => {
      if (createdIdRef.current) return { kind: "created", id: createdIdRef.current }
      try {
        const patient = await patientsApi.create({
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          phoneNumber: phone.trim() || null,
          // The country control's own value — without it the server reads every number as Tunisian.
          phoneRegion: country,
          // Absent on the first attempt, so the server checks whether this person is already on file.
          allowDuplicate: allowDuplicate || undefined,
        })
        createdIdRef.current = patient.id
        setCreatedName(`${patient.firstName} ${patient.lastName}`.trim())
        return { kind: "created", id: patient.id }
      } catch (err) {
        // Guarded on the grant, so a refusal that somehow persists is a real error rather than the same prompt again.
        if (err instanceof ApiError && err.code === ApiErrorCode.PatientDuplicate && !allowDuplicate) {
          return { kind: "duplicate", message: err.message }
        }
        return {
          kind: "failed",
          error:
            err instanceof ApiError
              ? `Échec de la création du patient : ${err.message}`
              : "Échec de la création du patient",
        }
      }
    },
    [firstName, lastName, phone, country],
  )

  /** Back to « Patient existant »: what was typed goes, the country stays the cabinet's. */
  const clearFields = useCallback(() => {
    setFirstName("")
    setLastName("")
    setPhone("")
  }, [])

  /** The host form closed: a new opening starts blank, and never books onto the last one's created patient. */
  const reset = useCallback(() => {
    clearFields()
    setCountry(DEFAULT_REGION)
    createdIdRef.current = null
    setCreatedName(null)
  }, [clearFields])

  const snapshot = (): InlineNewPatientSnapshot => ({
    firstName,
    lastName,
    phone,
    country,
    createdId: createdIdRef.current,
  })

  /**
   * A draft carried across a switch of server. A patient created on the OTHER server just before the switch may
   * not exist here, so the reuse is withdrawn on a 404 rather than failing every save.
   */
  const restore = useCallback((s: InlineNewPatientSnapshot, fromAnotherServer: boolean) => {
    setFirstName(s.firstName)
    setLastName(s.lastName)
    setPhone(s.phone)
    setCountry(s.country)
    if (!s.createdId) return
    const id = s.createdId
    createdIdRef.current = id
    if (fromAnotherServer) {
      patientsApi.get(id).catch((err) => {
        if (err instanceof ApiError && err.status === 404 && createdIdRef.current === id) {
          createdIdRef.current = null
        }
      })
    }
  }, [])

  return {
    firstName,
    setFirstName,
    lastName,
    setLastName,
    phone,
    setPhone,
    country,
    setCountry,
    typedName,
    createdName,
    alreadyCreated: createdName !== null,
    createdIdRef,
    validate,
    ensureCreated,
    clearFields,
    reset,
    snapshot,
    restore,
  }
}

export type InlineNewPatient = ReturnType<typeof useInlineNewPatient>
