"use client"

import { useEffect, useRef, useState } from "react"
import { format, subDays } from "date-fns"
import { CheckCircle2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { TimeInput, timeFromTyped } from "@/components/ui/time-field"
import { SchemaPad } from "@/components/team/schema-pad"
import { ApiError } from "@/lib/api/client"
import {
  PointageErrorCode,
  SCHEMA_MIN_DOTS,
  attendanceDayLabel,
  pointageApi,
  previousDayOf,
  type PreviousDay,
  type PunchResultDto,
} from "@/lib/api/pointage"
import { dayFromKey } from "@/lib/doctor-leaves"
import { getErrorMessage } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"

/** Refusals that end this punch: the tile behind it is out of date or closed. */
export const PUNCH_ENDING_CODES: ReadonlySet<string> = new Set([
  PointageErrorCode.SchemaLocked,
  PointageErrorCode.TerminalUnknown,
  PointageErrorCode.ModuleOff,
  PointageErrorCode.SchemaMissing,
  PointageErrorCode.PersonNotTracked,
  PointageErrorCode.OnLeave,
  PointageErrorCode.PunchStateChanged,
])

/** « hier » for yesterday, « du jeudi 30 octobre » otherwise. */
function dayPhrase(dayKey: string): string {
  return dayKey === format(subDays(dayFromKey(todayLocalIso()), 1), "yyyy-MM-dd")
    ? "d'hier"
    : `du ${attendanceDayLabel(dayKey)}`
}

type Stage = { kind: "pad" } | { kind: "declare"; pattern: number[]; previous: PreviousDay }

/** Which screen of the punch is up — a host holds its own way out during the declaration. */
export type PunchStage = Stage["kind"]

/** An earlier day whose departure she already declared in this punch. */
interface SettledDay {
  day: string
  departure: string
}

/** « Jeudi 29 octobre » from a `YYYY-MM-DD` key. */
function settledDayLabel(dayKey: string): string {
  const label = attendanceDayLabel(dayKey)
  return label.charAt(0).toUpperCase() + label.slice(1)
}

/**
 * One punch at a borne (`pointage-borne` AC-6, `pointage-obligatoire` AC-8): the schéma, then — when an earlier day
 * was left open — « Votre départ d'hier n'a pas été pointé » before the arrival lands. Shared by the borne's panel,
 * the arrival lock and the sign-out prompt, so the three cannot word it differently.
 */
export function PunchFlow({
  userId,
  name,
  action,
  onDone,
  onBack,
  onActivity,
  onEndingRefusal,
  onBusyChange,
  onStageChange,
  backLabel = "Retour",
  showHeading = true,
  padAutoFocus = true,
  padClassName,
}: {
  userId: string
  name: string
  action: PunchResultDto["action"]
  onDone: (result: PunchResultDto) => void
  /** The pad's way out; absent when the host already offers one (a dialog footer). */
  onBack?: () => void
  /** Any touch or press — the borne re-arms its idle timer on it. */
  onActivity?: () => void
  /** A refusal from {@link PUNCH_ENDING_CODES}: the flow stays disabled with the sentence on screen. */
  onEndingRefusal?: (code: string) => void
  /** A punch in flight — a host footer holds its own buttons meanwhile. */
  onBusyChange?: (busy: boolean) => void
  /** The pad or the « départ d'hier » declaration — a host ignores Escape and waits longer during the latter. */
  onStageChange?: (stage: PunchStage) => void
  backLabel?: string
  /** The name and « Arrivée » / « Départ » in large — off where the host's own title already says it. */
  showHeading?: boolean
  /** Off where the host places focus itself: a focused pad turns stray digits and Enter into a schéma try. */
  padAutoFocus?: boolean
  /** The pad's size in a host shorter than a borne, so a stroke never needs a scroll. */
  padClassName?: string
}) {
  const departure = action === "departure"
  const [stage, setStage] = useState<Stage>({ kind: "pad" })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [closing, setClosing] = useState(false)
  const [declared, setDeclared] = useState("")
  // What the hour field shows before it commits (on its 4th digit or on blur): « 930 » already means 09:30.
  const [typed, setTyped] = useState("")
  const [settled, setSettled] = useState<SettledDay[]>([])
  const mounted = useRef(true)

  useEffect(() => {
    onBusyChange?.(busy)
  }, [busy, onBusyChange])

  useEffect(() => {
    onStageChange?.(stage.kind)
  }, [stage.kind, onStageChange])

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const send = async (pattern: number[], declaredDeparture?: string) => {
    if (busy || closing) return
    onActivity?.()
    setBusy(true)
    setError(null)
    try {
      const result = await pointageApi.punch(userId, pattern, action, declaredDeparture)
      if (mounted.current) onDone(result)
    } catch (err) {
      if (!mounted.current) return
      const code = err instanceof ApiError ? err.code : undefined
      const previous = code === PointageErrorCode.PreviousDepartureMissing && err instanceof ApiError
        ? previousDayOf(err.details)
        : null
      if (previous) {
        // The server kept the hour just declared and asks for the next forgotten day: list it as settled.
        if (declaredDeparture && stage.kind === "declare" && stage.previous.day !== previous.day) {
          const done = { day: stage.previous.day, departure: declaredDeparture }
          setSettled((list) => [...list.filter((d) => d.day !== done.day), done])
        }
        setDeclared("")
        setTyped("")
        setStage({ kind: "declare", pattern, previous })
        return
      }
      setError(getErrorMessage(err, "Le pointage n'a pas pu être enregistré."))
      if (code && PUNCH_ENDING_CODES.has(code)) {
        setClosing(true)
        onEndingRefusal?.(code)
      }
    } finally {
      if (mounted.current) setBusy(false)
    }
  }

  const submitPattern = (pattern: number[]) => {
    // A stray tap is not a try: the server would refuse it the same way, without counting it.
    if (pattern.length < SCHEMA_MIN_DOTS) {
      onActivity?.()
      setError(`Reliez au moins ${SCHEMA_MIN_DOTS} points.`)
      return
    }
    void send(pattern)
  }

  if (stage.kind === "declare") {
    const { previous, pattern } = stage
    const tooEarly = declared !== "" && declared <= previous.arrival
    return (
      <form
        className="mx-auto flex w-full max-w-sm flex-col gap-4"
        // The step checks the hour itself: the field's `pattern` would refuse « 93:0 » + Enter before it commits as 09:30.
        noValidate
        onPointerDown={onActivity}
        onKeyDown={onActivity}
        onInput={(e) => {
          onActivity?.()
          const field = e.target as HTMLInputElement
          if (field.id === "declared-departure") setTyped(field.value)
        }}
        onSubmit={(e) => {
          e.preventDefault()
          const hour = declared || timeFromTyped(typed)
          if (!hour) return
          if (hour <= previous.arrival) {
            setDeclared(hour)
            return
          }
          void send(pattern, hour)
        }}
      >
        <div className="space-y-1 text-center">
          <p className="text-base text-muted-foreground">{name}</p>
          <h2 className="text-xl font-semibold text-balance">Votre départ {dayPhrase(previous.day)} n&apos;a pas été pointé</h2>
        </div>
        {settled.length > 0 && (
          <ul className="space-y-1 rounded-lg border border-success/40 bg-success-wash p-3 text-sm" aria-label="Départs déjà déclarés">
            {settled.map((d) => (
              <li key={d.day} className="flex items-center gap-2">
                <CheckCircle2 aria-hidden="true" className="size-4 shrink-0 text-success" />
                <span>
                  {settledDayLabel(d.day)} · départ <span className="font-medium tabular-nums">{d.departure}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-lg border bg-card p-3 text-sm">
          <dt className="text-muted-foreground">Arrivée</dt>
          <dd className="font-medium tabular-nums">{previous.arrival}</dd>
          <dt className="text-muted-foreground">Dernière action dans l&apos;app</dt>
          <dd className="font-medium tabular-nums">{previous.lastSaveAt ?? "aucune"}</dd>
        </dl>
        <div className="space-y-1.5">
          <Label htmlFor="declared-departure">Heure de départ</Label>
          <TimeInput
            id="declared-departure"
            value={declared}
            onChange={setDeclared}
            allowEmpty
            required
            disabled={busy}
            className="w-full"
            aria-describedby={tooEarly || error ? "declared-departure-error" : undefined}
          />
        </div>
        <p id="declared-departure-error" className="min-h-5 text-sm text-destructive" role="alert">
          {tooEarly ? `L'heure de départ doit être après l'arrivée (${previous.arrival}).` : busy ? "" : error}
        </p>
        <div className="flex flex-col-reverse gap-2 sm:flex-row">
          <Button
            type="button"
            variant="outline"
            size="lg"
            className="sm:flex-1"
            disabled={busy}
            onClick={() => {
              setError(null)
              setStage({ kind: "pad" })
            }}
          >
            Retour
          </Button>
          <Button type="submit" size="lg" className="sm:flex-1" disabled={busy || (!declared && !typed) || tooEarly}>
            Valider
          </Button>
        </div>
      </form>
    )
  }

  return (
    <div className="mx-auto flex w-full max-w-sm flex-col items-center gap-5" onPointerDown={onActivity}>
      {showHeading && (
        <div className="text-center">
          <p className="text-base text-muted-foreground">{name}</p>
          <p className={cn("text-5xl font-bold tracking-tight sm:text-6xl", departure ? "text-warning-ink" : "text-success")}>
            {departure ? "Départ" : "Arrivée"}
          </p>
        </div>
      )}
      <div className="flex w-full justify-center">
        <SchemaPad
          label={`Schéma de ${name}`}
          onComplete={submitPattern}
          disabled={busy || closing}
          error={error !== null}
          hideTrail
          autoFocus={padAutoFocus}
          className={padClassName}
        />
      </div>
      <p className="min-h-10 text-center text-sm text-destructive" role="alert">
        {busy ? "" : error}
      </p>
      {onBack && (
        <Button variant="outline" size="lg" className="w-full max-w-72" onClick={onBack} disabled={busy}>
          {backLabel}
        </Button>
      )}
    </div>
  )
}
