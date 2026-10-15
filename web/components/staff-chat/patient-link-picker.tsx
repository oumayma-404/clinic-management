"use client"

import { useEffect, useId, useRef, useState } from "react"
import { endOfDay, startOfDay } from "date-fns"
import { ArrowLeft, CalendarClock, Loader2, Search, User, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import type { StaffMessageAppointmentLink, StaffMessagePatientLink } from "@/lib/api/staff-chat"
import type { AppointmentDto, PatientDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { formatChatTime } from "@/lib/staff-chat/format"

export interface PickedLink {
  patient: StaffMessagePatientLink
  appointment: StaffMessageAppointmentLink | null
}

interface PatientLinkPickerProps {
  /** What the pick is for — « Joindre un patient », or a quick message waiting for one (AC-2.5). */
  title: string
  onPick: (link: PickedLink) => void
  onCancel: () => void
  /** Offered when a quick message asks for a patient: it may still go without one (AC-2.5). */
  onSkip?: () => void
}

const SEARCH_DELAY_MS = 250

/** « + Patient » (AC-2.4): a patient by name, then optionally one of their appointments of today. */
export function PatientLinkPicker({ title, onPick, onCancel, onSkip }: PatientLinkPickerProps) {
  const [term, setTerm] = useState("")
  const [results, setResults] = useState<PatientDto[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [patient, setPatient] = useState<PatientDto | null>(null)
  const [appointments, setAppointments] = useState<AppointmentDto[] | null>(null)
  const [appointmentsError, setAppointmentsError] = useState<string | null>(null)
  const [retryKey, setRetryKey] = useState(0)
  const inputId = useId()
  const latest = useRef(0)

  useEffect(() => {
    const query = term.trim()
    if (query.length < 2) {
      setResults(null)
      setSearchError(null)
      return
    }
    const ticket = ++latest.current
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const found = await patientsApi.list({ searchTerm: query, limit: 8 })
        if (ticket === latest.current) {
          setResults(found)
          setSearchError(null)
        }
      } catch (err) {
        if (ticket === latest.current) setSearchError(getErrorMessage(err, "La recherche n'a pas abouti."))
      } finally {
        if (ticket === latest.current) setSearching(false)
      }
    }, SEARCH_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [term, retryKey])

  useEffect(() => {
    if (!patient) return
    let cancelled = false
    setAppointments(null)
    setAppointmentsError(null)
    const now = new Date()
    appointmentsApi
      .list({ startDate: startOfDay(now).toISOString(), endDate: endOfDay(now).toISOString(), patientId: patient.id })
      .then((list) => {
        if (!cancelled) setAppointments(list.filter((a) => a.status !== "Cancelled"))
      })
      .catch((err) => {
        if (!cancelled) setAppointmentsError(getErrorMessage(err, "Les rendez-vous du jour n'ont pas pu être lus."))
      })
    return () => {
      cancelled = true
    }
  }, [patient, retryKey])

  const pickedPatient = (p: PatientDto): StaffMessagePatientLink => ({
    id: p.id,
    name: `${p.firstName} ${p.lastName}`.trim(),
    archived: false,
    missing: false,
  })

  return (
    <div className="shrink-0 space-y-2 border-t border-border bg-muted/30 p-2" role="group" aria-label={title}>
      <div className="flex items-center gap-1">
        {patient && (
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-8 coarse:size-11"
            aria-label="Choisir un autre patient"
            onClick={() => setPatient(null)}
          >
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          </Button>
        )}
        <p className="min-w-0 flex-1 truncate px-1 text-sm font-medium">{patient ? `${patient.firstName} ${patient.lastName}` : title}</p>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-8 coarse:size-11"
          aria-label="Fermer la recherche de patient"
          onClick={onCancel}
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>

      {!patient ? (
        <>
          <Label htmlFor={inputId} className="sr-only">
            Rechercher un patient à joindre
          </Label>
          <div className="relative">
            <Search className="pointer-events-none absolute start-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              id={inputId}
              autoFocus
              value={term}
              placeholder="Nom ou téléphone"
              onChange={(event) => setTerm(event.target.value)}
              className="ps-8"
            />
          </div>
          {searchError ? (
            <LoadFailureNotice variant="inline" message={searchError} onRetry={() => setRetryKey((k) => k + 1)} />
          ) : searching && !results ? (
            <p className="flex items-center gap-2 px-1 text-xs text-muted-foreground" role="status">
              <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
              Recherche…
            </p>
          ) : results && results.length === 0 ? (
            <p className="px-1 text-xs text-muted-foreground" role="status">
              Aucun patient pour cette recherche.
            </p>
          ) : (
            results && (
              <ul className="max-h-48 overflow-y-auto" aria-label="Patients trouvés">
                {results.map((p) => (
                  <li key={p.id}>
                    <button
                      type="button"
                      onClick={() => setPatient(p)}
                      className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-start text-sm hover:bg-accent focus-visible:bg-accent coarse:min-h-11"
                    >
                      <User className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                      <span className="min-w-0 flex-1 truncate">
                        {p.firstName} {p.lastName}
                      </span>
                      {p.phoneNumber && <span className="shrink-0 text-xs text-muted-foreground tabular-nums">{p.phoneNumber}</span>}
                    </button>
                  </li>
                ))}
              </ul>
            )
          )}
        </>
      ) : (
        <>
          {appointmentsError ? (
            <LoadFailureNotice variant="inline" message={appointmentsError} onRetry={() => setRetryKey((k) => k + 1)} />
          ) : !appointments ? (
            <p className="flex items-center gap-2 px-1 text-xs text-muted-foreground" role="status">
              <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
              Rendez-vous du jour…
            </p>
          ) : (
            <ul className="space-y-1" aria-label="Rendez-vous du jour">
              {appointments.map((a) => (
                <li key={a.id}>
                  <button
                    type="button"
                    onClick={() =>
                      onPick({
                        patient: pickedPatient(patient),
                        appointment: {
                          id: a.id,
                          startsAt: a.appointmentDateTime,
                          dentistName: a.doctorName ?? null,
                          cancelled: false,
                          missing: false,
                        },
                      })
                    }
                    className="flex w-full items-center gap-2 rounded border border-border bg-card px-2 py-1.5 text-start text-sm hover:bg-accent coarse:min-h-11"
                  >
                    <CalendarClock className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                    <span className="tabular-nums">RDV {formatChatTime(a.appointmentDateTime)}</span>
                    {a.doctorName && <span className="min-w-0 truncate text-muted-foreground">· {a.doctorName}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="w-full coarse:min-h-11"
            onClick={() => onPick({ patient: pickedPatient(patient), appointment: null })}
          >
            {appointments && appointments.length === 0 ? "Joindre le patient (aucun RDV aujourd'hui)" : "Sans rendez-vous"}
          </Button>
        </>
      )}

      {onSkip && (
        <Button type="button" variant="ghost" size="sm" className="w-full text-muted-foreground coarse:min-h-11" onClick={onSkip}>
          Envoyer sans patient
        </Button>
      )}
    </div>
  )
}
