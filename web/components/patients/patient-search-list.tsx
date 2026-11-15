"use client"

import { useEffect, useId, useRef, useState } from "react"
import { Loader2, Search, User, UserPlus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { patientsApi } from "@/lib/api/patients"
import type { PatientDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { quoteFr } from "@/lib/format"

export interface PickedPatient {
  patientId: string
  patientName: string
}

interface PatientSearchListProps {
  /** The field's accessible name — « Rechercher le patient de ces photos ». */
  label: string
  onPick: (patient: PickedPatient) => void
  /** A patient that must not be offered — the one a file already belongs to. */
  excludeId?: string
  autoFocus?: boolean
  /**
   * Offers « Nouveau patient « … » » for the name typed, once the search has answered — the camera import's walk-in
   * nobody registered. Below the results too: a namesake on file is not the person in the photo.
   */
  onCreate?: (typed: string) => void
}

const SEARCH_DELAY_MS = 250

/**
 * One patient, by name or telephone (`features/camera-photo-import`: « Autre patient » under a group of photos, and
 * « Changer de patient » on a filed file). Matched server-side over the whole cabinet, so a patient on the second page
 * of the list is found like any other.
 *
 * ⚠️ A failed search renders « Réessayer », never « Aucun patient » — the two look alike and mean opposite things.
 */
export function PatientSearchList({ label, onPick, excludeId, autoFocus = false, onCreate }: PatientSearchListProps) {
  const [term, setTerm] = useState("")
  const [results, setResults] = useState<PatientDto[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [retryKey, setRetryKey] = useState(0)
  const inputId = useId()
  const latest = useRef(0)

  useEffect(() => {
    const query = term.trim()
    if (query.length < 2) {
      setResults(null)
      setError(null)
      return
    }
    const ticket = ++latest.current
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const found = await patientsApi.list({ searchTerm: query, limit: 8 })
        if (ticket === latest.current) {
          setResults(found.filter((patient) => patient.id !== excludeId))
          setError(null)
        }
      } catch (err) {
        if (ticket === latest.current) setError(getErrorMessage(err, "La recherche n'a pas abouti."))
      } finally {
        if (ticket === latest.current) setSearching(false)
      }
    }, SEARCH_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [term, retryKey, excludeId])

  return (
    <div className="space-y-2">
      <Label htmlFor={inputId} className="sr-only">
        {label}
      </Label>
      <div className="relative">
        <Search
          className="pointer-events-none absolute start-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          id={inputId}
          autoFocus={autoFocus}
          value={term}
          placeholder="Nom ou téléphone du patient"
          onChange={(event) => setTerm(event.target.value)}
          className="ps-8"
          autoComplete="off"
        />
      </div>

      {error ? (
        <LoadFailureNotice variant="inline" message={error} onRetry={() => setRetryKey((key) => key + 1)} />
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
          <ul className="max-h-56 overflow-y-auto rounded-md border border-border" aria-label="Patients trouvés">
            {results.map((patient) => {
              const name = `${patient.firstName} ${patient.lastName}`.trim()
              return (
                <li key={patient.id} className="border-b border-border last:border-b-0">
                  <button
                    type="button"
                    onClick={() => onPick({ patientId: patient.id, patientName: name })}
                    className="flex w-full items-center gap-2 px-3 py-2 text-start text-sm hover:bg-accent focus-visible:bg-accent coarse:min-h-11"
                  >
                    <User className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                    <span className="min-w-0 flex-1 truncate">{name}</span>
                    {patient.phoneNumber && (
                      <span className="shrink-0 text-xs tabular-nums text-muted-foreground">{patient.phoneNumber}</span>
                    )}
                  </button>
                </li>
              )
            })}
          </ul>
        )
      )}

      {onCreate && results && !error && term.trim().length >= 2 && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-auto min-h-9 w-full justify-start gap-2 whitespace-normal text-start coarse:min-h-11"
          onClick={() => onCreate(term.trim())}
        >
          <UserPlus className="size-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0">Nouveau patient {quoteFr(term.trim())}</span>
        </Button>
      )}
    </div>
  )
}
