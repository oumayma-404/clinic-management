"use client"

import { useEffect, useId, useMemo, useRef, useState } from "react"
import { CalendarCheck, Check, Loader2, Search, UserPlus, X } from "lucide-react"

import { NewPatientForGroup } from "@/components/photo-import/new-patient-for-group"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { patientsApi } from "@/lib/api/patients"
import type { PatientDto, WaitingRoomDto, WaitingRoomVisitDto } from "@/lib/api/types"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { getErrorMessage } from "@/lib/errors"
import { quoteFr } from "@/lib/format"
import { runBoardAction } from "@/lib/hooks/use-waiting-room"
import { cn } from "@/lib/utils"
import { boardTime } from "@/lib/waiting-room"

/**
 * « Qui arrive ? » — the one box reception types into (`salle-attente` S2). Three answers under it, in the order a
 * patient is most likely to be: today's RDV (instant, from the board already in hand), a patient on file with no RDV
 * (« Sans RDV », searched server-side over the whole cabinet) and « Nouveau patient « … » ». Each is ONE press.
 *
 * <p>In a cabinet de groupe a walk-in needs the dentist who will see him — the server refuses one without — so the
 * press opens a row of the cabinet's dentists instead of sending at once.</p>
 */

interface ArrivalSearchProps {
  board: WaitingRoomDto
  reload: () => Promise<void>
}

type Pending = { patientId: string; patientName: string }

const SEARCH_DELAY_MS = 250

function fold(text: string): string {
  return text.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase().trim()
}

export function ArrivalSearch({ board, reload }: ArrivalSearchProps) {
  const inputId = useId()
  const [term, setTerm] = useState("")
  const [found, setFound] = useState<PatientDto[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [retryKey, setRetryKey] = useState(0)
  const [creating, setCreating] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  const latest = useRef(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const query = term.trim()
  const groupPractice = board.dentists.length > 1

  // Today's RDV still to come, matched in the browser — they are on the board already.
  const todays = useMemo(() => {
    if (!query) return []
    const needle = fold(query)
    return board.upcoming.filter((visit) => fold(visit.patientName).includes(needle)).slice(0, 5)
  }, [board.upcoming, query])

  // Patients already in the waiting room or the chair: offered as « déjà là », never as a second arrival.
  const alreadyHere = useMemo(() => {
    const ids = new Map<string, WaitingRoomVisitDto>()
    for (const visit of [...board.waiting, ...board.inChair]) ids.set(visit.patientId, visit)
    return ids
  }, [board.waiting, board.inChair])

  useEffect(() => {
    if (query.length < 2) {
      setFound(null)
      setError(null)
      return
    }
    const ticket = ++latest.current
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const patients = await patientsApi.list({ searchTerm: query, limit: 6 })
        if (ticket === latest.current) {
          setFound(patients)
          setError(null)
        }
      } catch (err) {
        if (ticket === latest.current) setError(getErrorMessage(err, "La recherche n'a pas abouti."))
      } finally {
        if (ticket === latest.current) setSearching(false)
      }
    }, SEARCH_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [query, retryKey])

  const upcomingIds = useMemo(() => new Set(todays.map((visit) => visit.patientId)), [todays])
  const walkIns = (found ?? []).filter((patient) => !upcomingIds.has(patient.id))

  const clear = () => {
    setTerm("")
    setFound(null)
    setCreating(null)
    setPending(null)
    inputRef.current?.focus()
  }

  const markArrived = async (visit: WaitingRoomVisitDto) => {
    if (busy) return
    setBusy(true)
    const change = await runBoardAction(() => waitingRoomApi.move(visit.appointmentId, "Waiting"), reload)
    setBusy(false)
    if (change) clear()
  }

  const sendWalkIn = async (patient: Pending, doctorId: string | null) => {
    if (busy) return
    setBusy(true)
    const change = await runBoardAction(() => waitingRoomApi.walkIn(patient.patientId, doctorId), reload, {
      message: (c) =>
        c.usedTodaysAppointment
          ? `${patient.patientName} → En salle d'attente (son RDV du jour)`
          : `${patient.patientName} → En salle d'attente · Sans RDV`,
    })
    setBusy(false)
    if (change) clear()
  }

  const pickWalkIn = (patient: Pending) => {
    if (groupPractice) setPending(patient)
    else void sendWalkIn(patient, null)
  }

  const showResults = query.length > 0 && !creating && !pending

  return (
    <section aria-labelledby={`${inputId}-title`} className="rounded-xl border bg-card p-3 shadow-sm sm:p-4">
      <h2 id={`${inputId}-title`} className="sr-only">
        Signaler une arrivée
      </h2>
      <Label htmlFor={inputId} className="sr-only">
        Qui arrive ? Nom ou téléphone du patient
      </Label>
      <div className="relative">
        <Search
          className="pointer-events-none absolute start-3 top-1/2 size-5 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          id={inputId}
          ref={inputRef}
          value={term}
          onChange={(event) => {
            setTerm(event.target.value)
            setCreating(null)
            setPending(null)
          }}
          onKeyDown={(event) => {
            if (event.key === "Escape" && term) {
              event.preventDefault()
              clear()
            }
            if (event.key === "Enter" && todays.length === 1 && walkIns.length === 0) {
              event.preventDefault()
              void markArrived(todays[0])
            }
          }}
          placeholder="Qui arrive ? Nom ou téléphone"
          autoComplete="off"
          className="h-12 ps-10 pe-10 text-base md:text-base"
          disabled={busy}
        />
        {term && (
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="absolute end-1 top-1/2 size-9 -translate-y-1/2 coarse:size-11"
            onClick={clear}
            aria-label="Effacer la recherche"
          >
            <X className="size-4" aria-hidden="true" />
          </Button>
        )}
      </div>

      {showResults && (
        <div className="mt-3 grid gap-3" role="region" aria-label="Résultats">
          {todays.length > 0 && (
            <ResultGroup title="Rendez-vous d'aujourd'hui">
              {todays.map((visit) => (
                <ResultRow
                  key={visit.appointmentId}
                  name={visit.patientName}
                  detail={`RDV ${boardTime(visit.appointmentDateTime)}${visit.acts ? ` · ${visit.acts}` : ""}`}
                  icon={CalendarCheck}
                  action="Arrivé"
                  disabled={busy}
                  onPick={() => void markArrived(visit)}
                />
              ))}
            </ResultGroup>
          )}

          {error ? (
            <LoadFailureNotice variant="inline" message={error} onRetry={() => setRetryKey((key) => key + 1)} />
          ) : searching && !found ? (
            <p className="flex items-center gap-2 px-1 text-sm text-muted-foreground" role="status">
              <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />
              Recherche…
            </p>
          ) : (
            walkIns.length > 0 && (
              <ResultGroup title="Sans rendez-vous aujourd'hui">
                {walkIns.map((patient) => {
                  const name = `${patient.firstName} ${patient.lastName}`.trim()
                  const here = alreadyHere.get(patient.id)
                  return (
                    <ResultRow
                      key={patient.id}
                      name={name}
                      detail={here ? "Déjà dans la salle d'attente" : (patient.phoneNumber ?? "Sans RDV")}
                      icon={UserPlus}
                      action="Sans RDV"
                      disabled={busy || Boolean(here)}
                      onPick={() => pickWalkIn({ patientId: patient.id, patientName: name })}
                    />
                  )
                })}
              </ResultGroup>
            )
          )}

          {found && !error && todays.length === 0 && walkIns.length === 0 && (
            <p className="px-1 text-sm text-muted-foreground" role="status">
              Aucun patient pour {quoteFr(query)}.
            </p>
          )}

          {query.length >= 2 && found && !error && (
            <Button
              type="button"
              variant="outline"
              className="h-auto min-h-11 justify-start gap-2 whitespace-normal text-start"
              onClick={() => setCreating(query)}
              disabled={busy}
            >
              <UserPlus className="size-4 shrink-0 text-primary" aria-hidden="true" />
              <span className="min-w-0">Nouveau patient {quoteFr(query)}</span>
            </Button>
          )}
        </div>
      )}

      {creating && !pending && (
        <div className="mt-3">
          <NewPatientForGroup
            typed={creating}
            savedWhat="son arrivée"
            submitLabel={groupPractice ? "Créer" : "Créer et marquer arrivé"}
            onCreated={(patient) => {
              setCreating(null)
              pickWalkIn(patient)
            }}
            onCancel={() => setCreating(null)}
            onPickExisting={() => setCreating(null)}
          />
        </div>
      )}

      {pending && (
        <div className="mt-3 grid gap-2 rounded-lg border border-primary/30 bg-primary/5 p-3" role="group" aria-label="Choisir le praticien">
          <p className="text-sm font-semibold">
            {pending.patientName} : pour quel praticien ?
          </p>
          <div className="flex flex-wrap gap-2">
            {board.dentists.map((dentist) => (
              <Button
                key={dentist.doctorId}
                type="button"
                variant="secondary"
                className="min-h-10 coarse:min-h-11"
                disabled={busy}
                onClick={() => void sendWalkIn(pending, dentist.doctorId)}
              >
                {busy && <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />}
                Dr {dentist.name}
              </Button>
            ))}
            <Button type="button" variant="ghost" className="min-h-10 coarse:min-h-11" disabled={busy} onClick={() => setPending(null)}>
              Annuler
            </Button>
          </div>
        </div>
      )}
    </section>
  )
}

function ResultGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-1">
      <p className="px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</p>
      <ul className="overflow-hidden rounded-lg border">{children}</ul>
    </div>
  )
}

function ResultRow({
  name,
  detail,
  icon: Icon,
  action,
  disabled,
  onPick,
}: {
  name: string
  detail: string
  icon: typeof Check
  action: string
  disabled: boolean
  onPick: () => void
}) {
  return (
    <li className="border-b last:border-b-0">
      <button
        type="button"
        onClick={onPick}
        disabled={disabled}
        className={cn(
          "flex w-full items-center gap-3 px-3 py-2 text-start hover:bg-accent focus-visible:bg-accent coarse:min-h-12",
          "disabled:cursor-not-allowed disabled:opacity-60",
        )}
        aria-label={`${action} : ${name}`}
      >
        <Icon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <span className="grid min-w-0 flex-1">
          <span className="truncate text-sm font-semibold">{name}</span>
          <span className="truncate text-xs text-muted-foreground">{detail}</span>
        </span>
        <span
          className={cn(
            "inline-flex shrink-0 items-center gap-1 rounded-md px-2.5 py-1 text-xs font-semibold",
            action === "Arrivé" ? "bg-success text-white" : "bg-warning-wash text-warning-ink",
          )}
          aria-hidden="true"
        >
          {action === "Arrivé" && <Check className="size-3.5" />}
          {action}
        </span>
      </button>
    </li>
  )
}
