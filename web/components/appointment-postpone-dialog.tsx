"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { addDays, format, parseISO, startOfDay } from "date-fns"
import { fr } from "date-fns/locale"
import { CalendarIcon } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { TimeField } from "@/components/ui/time-field"
import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { appointmentsApi } from "@/lib/api/appointments"
import { waitingListApi } from "@/lib/api/waiting-list"
import type { AppointmentDto, FreeDayDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useFreeSlots } from "@/lib/hooks/use-free-slots"
import { cn, parseDurationToMinutes } from "@/lib/utils"

export type PostponeOutcome = "moved" | "waiting-list"

/** The motif a postponement with no date cancels the visit with — what tells it apart from an absence later. */
export const POSTPONED_BY_PATIENT_REASON = "Reporté par le patient"

/** Day chips before the calendar button. */
const DAY_CHIPS = 5

/** A free start from this time on is listed under « Après-midi ». */
const AFTERNOON_FROM = "13:00"

const isoDay = (day: Date) => format(day, "yyyy-MM-dd")

function dayChipLabel(iso: string): string {
  if (iso === todayLocalIso()) return "Aujourd'hui"
  if (iso === isoDay(addDays(startOfDay(new Date()), 1))) return "Demain"
  const text = format(parseISO(iso), "EEE d", { locale: fr })
  return text.charAt(0).toUpperCase() + text.slice(1)
}

const dayLongLabel = (iso: string) => format(parseISO(iso), "EEEE d MMMM", { locale: fr })

/** What a day chip says under its date. */
function dayCount(day: FreeDayDto): string {
  if (day.state === "Open") return day.starts.length === 1 ? "1 libre" : `${day.starts.length} libres`
  if (day.state === "Leave") return "Congé"
  if (day.state === "Full") return "Complet"
  return "Fermé"
}

/** Why the chosen day offers no time — the server's own state, in words. */
function noTimeSentence(state: FreeDayDto["state"]): string {
  if (state === "Leave") return "Le praticien est en congé ce jour-là."
  if (state === "Full") return "Aucune heure libre ce jour-là."
  return "Le cabinet est fermé ce jour-là."
}

/** « Amine Trabelsi · prévue jeudi 29 oct. à 10:00 · 30 min » — what the visit is, before it moves. */
export function postponeSubject(appointment: AppointmentDto): string {
  const booked = parseISO(appointment.appointmentDateTime)
  const minutes = parseDurationToMinutes(appointment.duration)
  return `${appointment.patientName} · prévue ${format(booked, "EEEE d MMM 'à' HH:mm", { locale: fr })} · ${minutes} min`
}

/**
 * « Reporter » — the practitioner's free times, from the server's free-slot read (the save's own rules, so a listed
 * time is accepted without an override), or a typed time behind « Autre heure… », or the Liste d'attente. Body +
 * footer only, so it sits inside the post-visit prompt's own dialog as well as in {@link PostponeAppointmentDialog}.
 */
export function PostponeForm({
  appointment,
  onDone,
  onBack,
  onReloaded,
}: {
  appointment: AppointmentDto
  onDone: (outcome: PostponeOutcome) => void
  onBack?: () => void
  /** « Recharger » after a 409 hands the host the fresh read, so the subject line and the version move together. */
  onReloaded: (fresh: AppointmentDto) => void
}) {
  const booked = useMemo(() => parseISO(appointment.appointmentDateTime), [appointment.appointmentDateTime])
  const durationMinutes = parseDurationToMinutes(appointment.duration)

  // The window starts at the clinic's today; a calendar pick past it reloads a window starting there.
  const [windowFrom, setWindowFrom] = useState<string | undefined>(undefined)
  const slots = useFreeSlots(appointment.id, windowFrom)
  const unrestricted = slots.days?.some((d) => d.state === "Unrestricted") ?? false
  const offered = useMemo(
    () => (slots.days ?? []).filter((d) => d.state !== "Closed" && d.state !== "Unrestricted"),
    [slots.days],
  )

  const [pickedDay, setPickedDay] = useState<string | null>(null)
  const day =
    pickedDay ??
    offered.find((d) => d.state === "Open")?.date ??
    offered[0]?.date ??
    isoDay(addDays(startOfDay(new Date()), 1))
  const dayEntry = slots.days?.find((d) => d.date === day) ?? null
  const chipDays = offered.slice(0, DAY_CHIPS)
  const dayIsChip = chipDays.some((d) => d.date === day)

  const [time, setTime] = useState<string | null>(null)
  // A listed time that went past while the list was on screen: cleared, and said so until another is picked.
  const [expired, setExpired] = useState(false)
  const [typing, setTyping] = useState(false)
  // No list to pick from: no hours configured anywhere, or the read failed — the typed time is the only way.
  const typed = typing || unrestricted || slots.failed
  const [hour, setHour] = useState(() => format(booked, "HH"))
  const [minute, setMinute] = useState(() => format(booked, "mm"))
  const chosenTime = typed ? `${hour}:${minute}` : time
  const [calendarOpen, setCalendarOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [reloading, setReloading] = useState(false)
  const conflict = useConflict()
  const [refusal, setRefusal] = useState<{ kind: "overlap" | "hours"; message: string } | null>(null)
  // Merged and a ref, like the agenda's drag-move: a confirmation re-sends at once, and one may follow another.
  const grantsRef = useRef({ overlap: false, hours: false })
  // The entry « Mettre en liste d'attente » created, so a retry after a failed cancel never adds a second one.
  const waitingEntryRef = useRef<string | null>(null)

  const start = useMemo(() => {
    if (!chosenTime) return null
    const at = new Date(`${day}T${chosenTime}:00`)
    return Number.isNaN(at.getTime()) ? null : at
  }, [day, chosenTime])
  const isPast = start !== null && start.getTime() <= Date.now()
  const unchanged = start !== null && start.getTime() === booked.getTime()

  // A different slot needs its own acknowledgement.
  const slotKey = start?.getTime() ?? 0
  useEffect(() => {
    grantsRef.current = { overlap: false, hours: false }
    setRefusal(null)
  }, [slotKey])

  // Only a typed time needs the advisory line: a listed one is free by construction.
  const overlap = useAppointmentOverlap({
    enabled: typed,
    date: parseISO(day),
    startHour: hour,
    startMinute: minute,
    durationMinutes,
    doctorId: appointment.doctorId ?? undefined,
    excludeAppointmentId: appointment.id,
  })

  const moveTo = async () => {
    if (!start || saving || unchanged) return
    // The list was read when the form opened, so a listed time can pass on screen; the server accepts a past time.
    if (start.getTime() <= Date.now()) {
      if (!typed) {
        setTime(null)
        setExpired(true)
      }
      slots.reload()
      return
    }
    setSaving(true)
    conflict.clearMessage()
    try {
      // The version this form was opened on, so a colleague's move since is a 409, never a silent overwrite.
      await appointmentsApi.update(appointment.id, {
        appointmentDateTime: start.toISOString(),
        version: appointment.version,
        allowOverlap: grantsRef.current.overlap || undefined,
        allowOutsideWorkingHours: grantsRef.current.hours || undefined,
      })
      toast.success("Séance reportée", {
        description: `${appointment.patientName} · ${format(start, "EEEE d MMMM 'à' HH:mm", { locale: fr })}`,
      })
      onDone("moved")
    } catch (err) {
      if (err instanceof ApiError && err.code === ApiErrorCode.SlotTaken && !grantsRef.current.overlap) {
        setRefusal({ kind: "overlap", message: err.message })
        // A listed time taken since the read: the list was wrong, so it is read again.
        if (!typed) slots.reload()
      } else if (err instanceof ApiError && err.code === ApiErrorCode.OutsideWorkingHours && !grantsRef.current.hours) {
        setRefusal({ kind: "hours", message: err.message })
        if (!typed) slots.reload()
      } else if (err instanceof ApiError && err.status === 409) {
        conflict.capture(err)
      } else {
        showErrorToast(err, "Échec du report de la séance")
      }
    } finally {
      setSaving(false)
    }
  }

  const reload = async () => {
    setReloading(true)
    try {
      onReloaded(await appointmentsApi.get(appointment.id))
      conflict.reset()
    } catch (err) {
      showErrorToast(err, "Le rendez-vous n'a pas pu être relu.")
    } finally {
      setReloading(false)
    }
  }

  const confirmAnyway = () => {
    if (refusal?.kind === "overlap") grantsRef.current.overlap = true
    if (refusal?.kind === "hours") grantsRef.current.hours = true
    setRefusal(null)
    void moveTo()
  }

  const toWaitingList = async () => {
    if (!appointment.patientId || saving) return
    setSaving(true)
    try {
      // The entry first: a failure after it leaves the visit booked and the patient listed, never neither.
      if (!waitingEntryRef.current) {
        const entry = await waitingListApi.create({
          patientId: appointment.patientId,
          preferredDoctorId: appointment.doctorId ?? null,
          priority: "Normal",
          note: `${POSTPONED_BY_PATIENT_REASON} · RDV du ${format(booked, "EEEE d MMMM 'à' HH:mm", { locale: fr })}`,
        })
        waitingEntryRef.current = entry.id
      }
      await appointmentsApi.update(appointment.id, {
        status: "Cancelled",
        cancellationReason: POSTPONED_BY_PATIENT_REASON,
      })
      toast.success("Ajouté à la liste d'attente", {
        description: `${appointment.patientName} · rendez-vous annulé (${POSTPONED_BY_PATIENT_REASON.toLowerCase()})`,
      })
      onDone("waiting-list")
    } catch (err) {
      showErrorToast(err, "Échec de la mise en liste d'attente")
    } finally {
      setSaving(false)
    }
  }

  const pickDay = (iso: string) => {
    setPickedDay(iso)
    setTime(null)
    setExpired(false)
  }

  const pickTime = (listed: string) => {
    setTime(listed)
    setExpired(false)
  }

  return (
    <>
      <DialogBody className="space-y-4">
        {slots.failed && (
          <LoadFailureNotice message="Les heures libres n'ont pas pu être chargées." onRetry={slots.reload} />
        )}

        <div className="space-y-2">
          <Label className="text-sm">Jour</Label>
          {slots.loading && !slots.days ? (
            <div className="h-11 animate-pulse rounded-md bg-muted" aria-hidden="true" />
          ) : (
            <div role="radiogroup" aria-label="Jour" className="flex flex-wrap items-center gap-1.5">
              {!unrestricted &&
                chipDays.map((d) => {
                  const active = d.date === day
                  return (
                    <Button
                      key={d.date}
                      type="button"
                      role="radio"
                      aria-checked={active}
                      aria-label={`${dayChipLabel(d.date)}, ${dayLongLabel(d.date)} · ${dayCount(d)}`}
                      variant={active ? "default" : "outline"}
                      size="sm"
                      className={cn(
                        "h-auto min-h-11 flex-col items-start gap-0 px-2.5 py-1 leading-tight",
                        !active && "bg-card",
                        !active && d.state !== "Open" && "text-muted-foreground",
                      )}
                      disabled={saving}
                      onClick={() => pickDay(d.date)}
                    >
                      <span className="text-sm font-medium">{dayChipLabel(d.date)}</span>
                      <span className="text-2xs font-normal opacity-80">{dayCount(d)}</span>
                    </Button>
                  )
                })}
              <Popover open={calendarOpen} onOpenChange={setCalendarOpen} modal>
                <PopoverTrigger asChild>
                  <Button
                    type="button"
                    variant={!dayIsChip ? "default" : "outline"}
                    size="sm"
                    className={cn("min-h-11 px-2.5", dayIsChip && "bg-card")}
                    disabled={saving}
                    aria-label="Choisir une autre date"
                  >
                    <CalendarIcon className="size-4" aria-hidden="true" />
                    {!dayIsChip && format(parseISO(day), "dd/MM/yyyy")}
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-auto p-0" align="start">
                  <Calendar
                    mode="single"
                    selected={parseISO(day)}
                    disabled={(candidate) => candidate < startOfDay(new Date())}
                    onSelect={(picked) => {
                      if (picked) {
                        const iso = isoDay(picked)
                        if (!slots.days?.some((d) => d.date === iso)) setWindowFrom(iso)
                        pickDay(iso)
                      }
                      setCalendarOpen(false)
                    }}
                    initialFocus
                  />
                </PopoverContent>
              </Popover>
            </div>
          )}
        </div>

        {typed ? (
          <div className="space-y-2">
            <Label htmlFor={`postpone-time-${appointment.id}`} className="text-sm">Heure</Label>
            <div className="flex flex-wrap items-center gap-3">
              <TimeField
                id={`postpone-time-${appointment.id}`}
                hour={hour}
                minute={minute}
                onChange={(next) => {
                  setHour(next.hour)
                  setMinute(next.minute)
                }}
                disabled={saving}
                className="w-28 bg-card"
              />
              {typing && !unrestricted && !slots.failed && (
                <Button type="button" variant="link" className="h-auto px-0" onClick={() => setTyping(false)}>
                  Voir les heures libres
                </Button>
              )}
            </div>
            {unrestricted && (
              <p className="text-sm text-muted-foreground">Aucun horaire n&apos;est enregistré pour ce cabinet.</p>
            )}
          </div>
        ) : slots.loading && !dayEntry ? (
          <div className="h-24 animate-pulse rounded-md bg-muted" aria-hidden="true" />
        ) : (
          <div className="space-y-3">
            <p className="text-sm font-medium">Heures libres · {dayLongLabel(day)}</p>
            {dayEntry?.state === "Open" ? (
              <FreeTimes starts={dayEntry.starts} value={time} onPick={pickTime} disabled={saving} />
            ) : (
              <p className="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground">
                {offered.length === 0 && !pickedDay
                  ? "Aucun jour ouvert dans les deux semaines qui viennent."
                  : noTimeSentence(dayEntry?.state ?? "Closed")}
              </p>
            )}
            <Button
              type="button"
              variant="link"
              className="h-auto px-0"
              onClick={() => {
                setTyping(true)
                setTime(null)
                setExpired(false)
              }}
            >
              Autre heure…
            </Button>
          </div>
        )}

        {typed && isPast && (
          <p role="status" className="text-sm text-warning-ink">Cette heure est déjà passée.</p>
        )}
        {typed && !isPast && overlap.warning && !refusal && (
          <p role="status" className="text-sm text-warning-ink">{overlap.warning}</p>
        )}
      </DialogBody>

      {/* What a press answered sits beside the buttons: below the grid it was off screen at 730 px tall. */}
      {(expired || (!typed && isPast) || refusal || conflict.error) && (
        <div className="shrink-0 space-y-2">
          {(expired || (!typed && isPast)) && (
            <p role="status" className="text-sm text-warning-ink">Cette heure est déjà passée.</p>
          )}
          {refusal && (
            <p role="alert" className="rounded-md border border-warning/40 bg-warning-wash px-3 py-2 text-sm text-warning-ink">
              {refusal.message}
            </p>
          )}
          <FormErrorBanner
            message={conflict.error}
            action={
              conflict.isConflict
                ? { label: "Recharger", onClick: () => void reload(), disabled: saving || reloading }
                : undefined
            }
          />
        </div>
      )}

      {/* Three buttons fill the popup's 398 px footer (measured), so it wraps rather than clipping « Retour ». */}
      <DialogFooter className="gap-2 sm:gap-2 md:flex-wrap">
        {onBack && (
          <Button type="button" variant="ghost" onClick={onBack} disabled={saving} className="md:me-auto">
            Retour
          </Button>
        )}
        {appointment.patientId && (
          <Button type="button" variant="outline" onClick={() => void toWaitingList()} disabled={saving}>
            Mettre en liste d&apos;attente
          </Button>
        )}
        <Button
          type="button"
          onClick={() => (refusal ? confirmAnyway() : void moveTo())}
          disabled={saving || !start || isPast || unchanged}
        >
          {saving ? "Enregistrement…" : refusal ? "Reporter quand même" : "Reporter"}
        </Button>
      </DialogFooter>
    </>
  )
}

/** The chosen day's free starts, morning and afternoon apart. */
function FreeTimes({
  starts,
  value,
  onPick,
  disabled,
}: {
  starts: string[]
  value: string | null
  onPick: (time: string) => void
  disabled: boolean
}) {
  const groups = [
    { label: "Matin", times: starts.filter((t) => t < AFTERNOON_FROM) },
    { label: "Après-midi", times: starts.filter((t) => t >= AFTERNOON_FROM) },
  ].filter((g) => g.times.length > 0)

  return (
    <div className="space-y-3">
      {groups.map((group) => (
        <div key={group.label} className="space-y-1.5">
          <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">{group.label}</p>
          <div
            role="radiogroup"
            aria-label={`Heures libres, ${group.label.toLowerCase()}`}
            className="grid grid-cols-[repeat(auto-fill,minmax(4rem,1fr))] gap-1.5"
          >
            {group.times.map((t) => {
              const active = t === value
              return (
                <Button
                  key={t}
                  type="button"
                  role="radio"
                  aria-checked={active}
                  variant={active ? "default" : "outline"}
                  size="sm"
                  className={cn("h-9 px-0 tabular-nums coarse:h-11", !active && "bg-card")}
                  disabled={disabled}
                  onClick={() => onPick(t)}
                >
                  {t}
                </Button>
              )
            })}
          </div>
        </div>
      ))}
    </div>
  )
}

/** « Reporter… » outside the post-visit prompt: reads the visit fresh, then hosts {@link PostponeForm}. */
export function PostponeAppointmentDialog({
  appointmentId,
  open,
  onOpenChange,
  onDone,
}: {
  appointmentId: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone?: (outcome: PostponeOutcome) => void
}) {
  const [appointment, setAppointment] = useState<AppointmentDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (!open || !appointmentId) return
    let cancelled = false
    setAppointment(null)
    setFailed(false)
    appointmentsApi
      .get(appointmentId)
      .then((a) => !cancelled && setAppointment(a))
      .catch(() => !cancelled && setFailed(true))
    return () => {
      cancelled = true
    }
  }, [open, appointmentId, attempt])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* A React portal still bubbles through the React tree: without this a press here opens the agenda block's dialog. */}
      <DialogContent className="md:max-w-md" onClick={(e) => e.stopPropagation()}>
        <DialogHeader>
          <DialogTitle>Reporter la séance</DialogTitle>
          <DialogDescription>{appointment ? postponeSubject(appointment) : "Chargement…"}</DialogDescription>
        </DialogHeader>
        {failed ? (
          <LoadFailureNotice
            message="Le rendez-vous n'a pas pu être chargé."
            onRetry={() => setAttempt((n) => n + 1)}
          />
        ) : appointment ? (
          <PostponeForm
            appointment={appointment}
            onReloaded={setAppointment}
            onDone={(outcome) => {
              onOpenChange(false)
              onDone?.(outcome)
            }}
          />
        ) : (
          <div className="h-28 animate-pulse rounded-md bg-muted" aria-hidden="true" />
        )}
      </DialogContent>
    </Dialog>
  )
}
