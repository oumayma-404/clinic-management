"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { TimeInput } from "@/components/ui/time-field"
import {
  CORRECTION_REASON_MAX,
  attendanceDayLabel,
  pointageApi,
  type AttendanceEntryDto,
  type AttendancePersonDto,
} from "@/lib/api/pointage"
import { getErrorMessage } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"

/**
 * « Corriger » (`pointage-borne` AC-10): the hours that count and a motif. The borne's own hours stay on the line
 * and are shown here, so a correction is always read against what was punched.
 */
export function CorrectAttendanceDialog({
  entry,
  onOpenChange,
  onSaved,
  reloadEntry,
}: {
  entry: AttendanceEntryDto | null
  onOpenChange: (open: boolean) => void
  onSaved: () => void
  /** Re-reads the line after a 409, so « Recharger » shows a colleague's correction. */
  reloadEntry: (id: string) => Promise<AttendanceEntryDto | null>
}) {
  const open = entry !== null
  const [current, setCurrent] = useState<AttendanceEntryDto | null>(entry)
  const [arrival, setArrival] = useState("")
  const [departure, setDeparture] = useState("")
  const [reason, setReason] = useState("")
  const [saving, setSaving] = useState(false)
  const conflict = useConflict()
  const resetConflict = conflict.reset
  const guard = useDirtyGuard(open, onOpenChange)
  // The sheet row is as old as the last refetch; the save checks against the line as it is now.
  const fresh = useFreshVersion(open, entry?.id, entry, () => (entry ? reloadEntry(entry.id) : Promise.resolve(null)))

  const [reloading, setReloading] = useState(false)

  const fill = useCallback((line: AttendanceEntryDto) => {
    setCurrent(line)
    setArrival(line.arrival)
    setDeparture(line.departure ?? "")
  }, [])

  useEffect(() => {
    if (!entry) return
    fill(entry)
    setReason("")
    resetConflict()
  }, [entry, fill, resetConflict])

  // A colleague's correction made between the sheet's read and this open: show it rather than overwrite it — but
  // only over the copy the dialog was opened with, or a slower read would put back what « Recharger » replaced.
  useEffect(() => {
    if (!fresh.source || !current || !entry) return
    const stillOnOpenedCopy = current.id === entry.id && current.version === entry.version
    if (stillOnOpenedCopy && fresh.source.id === current.id && fresh.source.version !== current.version) {
      fill(fresh.source)
    }
  }, [fresh.source, current, entry, fill])

  const reload = async () => {
    if (!current) return
    setReloading(true)
    try {
      const reread = await reloadEntry(current.id)
      if (reread) {
        fill(reread)
        conflict.reset()
      } else {
        conflict.setError("Ce pointage n'existe plus.")
      }
    } catch (err) {
      conflict.setError(getErrorMessage(err, "Le pointage n'a pas pu être relu. Réessayez."))
    } finally {
      setReloading(false)
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!current) return
    if (!reason.trim()) {
      conflict.setError("Indiquez le motif de la correction.")
      return
    }
    setSaving(true)
    conflict.clearMessage()
    try {
      await pointageApi.correct(current.id, {
        arrival,
        departure: departure || null,
        reason: reason.trim(),
        version: current.version,
      })
      guard.markClean()
      toast.success("Pointage corrigé.")
      onSaved()
    } catch (err) {
      conflict.capture(err, "Le pointage n'a pas pu être corrigé.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Corriger le pointage</DialogTitle>
            <DialogDescription>
              {current ? `${current.userName} · ${attendanceDayLabel(current.localDay)}` : ""}
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="correct-attendance-form" noValidate onSubmit={submit} className="space-y-4">
              {current && (current.punchedArrival || current.terminalName) && (
                <p className="rounded-md bg-muted/50 px-3 py-2 text-sm text-muted-foreground">
                  Borne{current.terminalName ? ` ${current.terminalName}` : ""} : arrivée {current.punchedArrival ?? "—"}
                  {" · "}départ {current.punchedDeparture ?? "non pointé"}
                </p>
              )}
              {current?.isCorrected && current.correctionReason && (
                <p className="text-sm text-muted-foreground">
                  Corrigé{current.correctedByName ? ` par ${current.correctedByName}` : ""} : {current.correctionReason}
                </p>
              )}
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="correct-arrival">Arrivée</Label>
                  <TimeInput id="correct-arrival" value={arrival} onChange={setArrival} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="correct-departure">Départ</Label>
                  <TimeInput id="correct-departure" value={departure} onChange={setDeparture} allowEmpty />
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="correct-reason">Motif</Label>
                <Textarea
                  id="correct-reason"
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  rows={2}
                  maxLength={CORRECTION_REASON_MAX}
                  required
                />
              </div>
              <FormErrorBanner
                message={conflict.error}
                action={
                  conflict.isConflict
                    ? { label: "Recharger", onClick: () => void reload(), disabled: saving || reloading }
                    : undefined
                }
              />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="correct-attendance-form" disabled={saving}>
              Corriger
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** Where « Ajouter un pointage » was started from: that person, that day. */
export interface AttendanceRecordPrefill {
  userId?: string
  /** `YYYY-MM-DD`. */
  day?: string
}

/** « 14:05 » — the viewer's clock, the arrival an admin records while the person stands in front of her. */
const nowHm = () => {
  const now = new Date()
  return `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`
}

/** Today's arrival is « now »; another day's is typed, never invented. */
const arrivalFor = (day: string) => (day === todayLocalIso() ? nowHm() : "")

/** « Ajouter un pointage » (AC-10): a day with none, with a motif. */
export function RecordAttendanceDialog({
  open,
  onOpenChange,
  people,
  defaultUserId,
  prefill,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  people: AttendancePersonDto[]
  /** One person's file: the person is fixed and not asked. */
  defaultUserId?: string
  /** Started from a row: that person and that day, still changeable. */
  prefill?: AttendanceRecordPrefill | null
  onSaved: () => void
}) {
  const [userId, setUserId] = useState("")
  const [day, setDay] = useState(todayLocalIso())
  const [arrival, setArrival] = useState("")
  const [arrivalTouched, setArrivalTouched] = useState(false)
  const [departure, setDeparture] = useState("")
  const [reason, setReason] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const guard = useDirtyGuard(open, onOpenChange)
  const wasOpen = useRef(false)

  // Reset only when the dialog opens: the people list is re-derived on every punch broadcast, mid-typing.
  useEffect(() => {
    const opening = open && !wasOpen.current
    wasOpen.current = open
    if (!opening) return
    const firstDay = prefill?.day ?? todayLocalIso()
    setUserId(prefill?.userId ?? defaultUserId ?? (people.length === 1 ? people[0].userId : ""))
    setDay(firstDay)
    setArrival(arrivalFor(firstDay))
    setArrivalTouched(false)
    setDeparture("")
    setReason("")
    setError(null)
  }, [open, defaultUserId, prefill, people])

  const changeDay = (next: string) => {
    setDay(next)
    if (!arrivalTouched) setArrival(arrivalFor(next))
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!userId) return setError("Choisissez la personne.")
    if (!day) return setError("Choisissez le jour.")
    if (!arrival) return setError("Indiquez l'heure d'arrivée.")
    if (!reason.trim()) return setError("Indiquez le motif.")
    setSaving(true)
    setError(null)
    try {
      await pointageApi.record({ userId, day, arrival, departure: departure || null, reason: reason.trim() })
      guard.markClean()
      toast.success("Pointage ajouté.")
      onSaved()
    } catch (err) {
      setError(getErrorMessage(err, "Le pointage n'a pas pu être ajouté."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Ajouter un pointage</DialogTitle>
            <DialogDescription className="sr-only">Un jour où la borne n&apos;a rien enregistré.</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="record-attendance-form" noValidate onSubmit={submit} className="space-y-4">
              {!defaultUserId && (
                <div className="space-y-2">
                  <Label htmlFor="record-person">Personne</Label>
                  <Select value={userId} onValueChange={setUserId}>
                    <SelectTrigger id="record-person" className="w-full">
                      <SelectValue placeholder="Choisir" />
                    </SelectTrigger>
                    <SelectContent>
                      {people.map((person) => (
                        <SelectItem key={person.userId} value={person.userId}>
                          {person.fullName}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )}
              <div className="space-y-2">
                <Label htmlFor="record-day">Jour</Label>
                <Input id="record-day" type="date" value={day} max={todayLocalIso()} onChange={(e) => changeDay(e.target.value)} />
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="record-arrival">Arrivée</Label>
                  <TimeInput
                    id="record-arrival"
                    value={arrival}
                    onChange={(next) => {
                      setArrival(next)
                      setArrivalTouched(true)
                    }}
                    allowEmpty
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="record-departure">Départ</Label>
                  <TimeInput id="record-departure" value={departure} onChange={setDeparture} allowEmpty />
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="record-reason">Motif</Label>
                <Textarea
                  id="record-reason"
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  rows={2}
                  maxLength={CORRECTION_REASON_MAX}
                />
              </div>
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="record-attendance-form" disabled={saving}>
              Ajouter
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}
