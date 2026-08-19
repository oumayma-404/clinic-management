"use client"

import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Clock, Save, Trash2 } from "lucide-react"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { doctorsApi, type WorkingDay } from "@/lib/api/doctors"
import { ApiError } from "@/lib/api/client"
import { DEFAULT_WORKING_HOURS, WEEKDAYS } from "@/lib/working-hours"

/** French labels for the (English) weekday storage keys — the `weekdayLabelsFr` convention. */
const DAY_LABELS_FR: Record<string, string> = {
  Monday: "Lundi",
  Tuesday: "Mardi",
  Wednesday: "Mercredi",
  Thursday: "Jeudi",
  Friday: "Vendredi",
  Saturday: "Samedi",
  Sunday: "Dimanche",
}

interface DoctorWorkingHoursCardProps {
  doctorId: string
  doctorName?: string
  /** Rendered as a plain block rather than its own Card, for embedding inside an existing card. */
  embedded?: boolean
  /**
   * Bumped by the host to force a refetch — « Mon profil » drives it from the `doctors` realtime key
   * (AC-P4.21). The subscription lives in the host rather than here because this card is rendered once per
   * practitioner in « Paramètres → Médecins », and one hub connection per row is not a subscription model.
   */
  reloadKey?: number
}

/**
 * View and edit **one practitioner's** working hours (§ 5.4, AC-P1.25/1.26).
 *
 * `GET`/`PUT /api/doctors/{id}/working-hours`, `SetDoctorWorkingHoursCommand`, `GetDoctorWorkingHoursQuery` and
 * `Doctor.WorkingHoursJson` have all existed and been own-or-admin-gated for a while, and **nothing in the
 * product could reach any of them** — `doctorsApi.getWorkingHours`/`setWorkingHours` had zero callers, so no
 * clinic could set a per-dentist override and nothing could observe one. This component is that missing caller,
 * shared by « Paramètres → Médecins » (admin, any practitioner) and « Mon profil » (a doctor, their own).
 *
 * The empty state is stated explicitly rather than left blank (AC-P1.26): an empty `PUT` silently means "clear
 * the override" server-side, and nothing told the user that the clinic-wide hours would take over.
 */
export function DoctorWorkingHoursCard({
  doctorId,
  doctorName,
  embedded = false,
  reloadKey = 0,
}: DoctorWorkingHoursCardProps) {
  const [days, setDays] = useState<WorkingDay[]>([])
  /** True when the practitioner has no override, so the clinic-wide hours apply. */
  const [inherited, setInherited] = useState(true)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const hours = await doctorsApi.getWorkingHours(doctorId)
      if (hours.length === 0) {
        setInherited(true)
        // Seed the editor with the shared default so enabling an override starts from something sensible
        // rather than seven blank rows. Nothing is saved until the user presses Enregistrer.
        setDays(DEFAULT_WORKING_HOURS.map((d) => ({ ...d })))
      } else {
        setInherited(false)
        setDays(hours.map((d) => ({ ...d })))
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec du chargement des horaires du praticien.")
    } finally {
      setLoading(false)
    }
  }, [doctorId, reloadKey])

  useEffect(() => {
    load()
  }, [load])

  const updateDay = (day: string, patch: Partial<WorkingDay>) =>
    setDays((prev) => prev.map((d) => (d.day === day ? { ...d, ...patch } : d)))

  /**
   * Mirror the server's validation so an invalid row is refused before the round-trip. The server is still the
   * authority (`WorkingHoursSerializer.Validate`) — this only means the user is told which day is wrong.
   */
  const validate = (): string | null => {
    for (const d of days) {
      if (!d.enabled) continue
      if (!/^\d{2}:\d{2}$/.test(d.from) || !/^\d{2}:\d{2}$/.test(d.to)) {
        return `${DAY_LABELS_FR[d.day] ?? d.day} : heures invalides (format attendu HH:mm).`
      }
      if (d.from >= d.to) {
        return `${DAY_LABELS_FR[d.day] ?? d.day} : l'heure de fermeture doit être postérieure à l'ouverture.`
      }
    }
    return null
  }

  const save = async (payload: WorkingDay[] | null) => {
    setSaving(true)
    setError(null)
    try {
      const saved = await doctorsApi.setWorkingHours(doctorId, payload ?? [])
      if (saved.length === 0) {
        setInherited(true)
        setDays(DEFAULT_WORKING_HOURS.map((d) => ({ ...d })))
        toast.success("Horaires spécifiques supprimés : les horaires du cabinet s'appliquent.")
      } else {
        setInherited(false)
        setDays(saved.map((d) => ({ ...d })))
        toast.success("Horaires du praticien enregistrés.")
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement des horaires.")
    } finally {
      setSaving(false)
    }
  }

  const handleSave = () => {
    const invalid = validate()
    if (invalid) {
      setError(invalid)
      return
    }
    void save(days)
  }

  const body = (
    <div className="space-y-3">
      <FormErrorBanner message={error} />

      {loading ? (
        <p className="text-sm text-muted-foreground">Chargement des horaires…</p>
      ) : (
        <>
          {/* AC-P1.26: say it, rather than showing an ambiguous empty editor. */}
          <p className="rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
            {inherited
              ? "Aucun horaire spécifique : les horaires du cabinet s'appliquent. Modifiez les jours ci-dessous puis enregistrez pour définir un horaire propre à ce praticien."
              : "Ce praticien a ses propres horaires. Ils remplacent les horaires du cabinet pour la prise de rendez-vous."}
          </p>

          {/* ⚠️ The two `<Input>`s below say `md:text-xs`, never a bare `text-xs`: `ui/input.tsx` ships
              `text-base md:text-sm` as the iOS focus-zoom guard (Safari zooms into any field under 16px and
              never zooms back), and tailwind-merge treats an unprefixed size at the call site as a REPLACEMENT
              for `text-base` — so the guard is defeated by the very class meant to make the field compact. */}
          <div className="space-y-1.5">
            {WEEKDAYS.map((weekday) => {
              const day = days.find((d) => d.day === weekday) ?? {
                day: weekday,
                enabled: false,
                from: "09:00",
                to: "17:00",
              }
              const enabledId = `wh-${doctorId}-${weekday}-enabled`
              const fromId = `wh-${doctorId}-${weekday}-from`
              const toId = `wh-${doctorId}-${weekday}-to`
              return (
                <div key={weekday} className="flex flex-wrap items-center gap-2 rounded-md border px-2 py-1.5">
                  <div className="flex w-32 items-center gap-2">
                    <Checkbox
                      id={enabledId}
                      checked={day.enabled}
                      onCheckedChange={(checked) => updateDay(weekday, { enabled: checked === true })}
                      disabled={saving}
                    />
                    {/* htmlFor/id throughout — the clinic-wide editor this mirrors has none (AC-P1.54). */}
                    <Label htmlFor={enabledId} className="text-xs font-medium">
                      {DAY_LABELS_FR[weekday] ?? weekday}
                    </Label>
                  </div>
                  {/* ⚠️ Three classes, three separate reasons, and it takes all three.
                      • `basis-52` — `flex-1` alone is `flex: 1 1 0%`, so this pair's *hypothetical* size is zero
                        and it can never trigger the row's `flex-wrap`; a real basis is what lets the wrap fire,
                        and `sm:basis-0` puts it back on the day's line above the hinge.
                      • `min-w-0` on THIS box — a flex item's automatic minimum size is its content, and the
                        content here is two `type="time"` fields with a ~106 px native intrinsic width each, so
                        the wrapper was clamped UP to 234 px inside a 208 px card and painted straight out of it.
                      • `min-w-0 flex-1 basis-28` on each field, so once the box can shrink the two of them
                        share what is left — and, at 320 px where 91 px each would clip « 09:00 » down to
                        « 09:( », the 7rem basis makes the second field wrap onto its own full-width line
                        instead. A time you cannot read is not a narrower control, it is a broken one.
                      (Same family of trap as `subscription-banner.tsx` and `ui/list-toolbar.tsx`.) */}
                  <div className="flex min-w-0 flex-1 basis-52 flex-wrap items-center gap-x-2 gap-y-1 sm:flex-nowrap sm:basis-0">
                    <Label htmlFor={fromId} className="sr-only">
                      {`Heure d'ouverture — ${DAY_LABELS_FR[weekday] ?? weekday}`}
                    </Label>
                    <Input
                      id={fromId}
                      type="time"
                      value={day.from}
                      onChange={(e) => updateDay(weekday, { from: e.target.value })}
                      disabled={saving || !day.enabled}
                      // `min-w-0 flex-1`: a `type="time"` field will not go below its native intrinsic width
                      // (~105 px) on its own, so even on its own wrapped line the pair measured 234 px against
                      // the 182 px this card gives it at 320 px. Sharing the row explicitly is what makes both
                      // fields fit; they are wide enough for « 09:00 » and the picker glyph at that size.
                      className="h-7 min-w-0 flex-1 basis-28 md:text-xs"
                    />
                    <span className="text-xs text-muted-foreground">à</span>
                    <Label htmlFor={toId} className="sr-only">
                      {`Heure de fermeture — ${DAY_LABELS_FR[weekday] ?? weekday}`}
                    </Label>
                    <Input
                      id={toId}
                      type="time"
                      value={day.to}
                      onChange={(e) => updateDay(weekday, { to: e.target.value })}
                      disabled={saving || !day.enabled}
                      // `min-w-0 flex-1`: a `type="time"` field will not go below its native intrinsic width
                      // (~105 px) on its own, so even on its own wrapped line the pair measured 234 px against
                      // the 182 px this card gives it at 320 px. Sharing the row explicitly is what makes both
                      // fields fit; they are wide enough for « 09:00 » and the picker glyph at that size.
                      className="h-7 min-w-0 flex-1 basis-28 md:text-xs"
                    />
                  </div>
                </div>
              )
            })}
          </div>

          <div className="flex flex-wrap justify-end gap-2 pt-1">
            {!inherited && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 gap-1 text-xs text-destructive hover:text-destructive"
                onClick={() => void save(null)}
                disabled={saving}
              >
                <Trash2 className="h-3 w-3" />
                Utiliser les horaires du cabinet
              </Button>
            )}
            <Button size="sm" className="h-7 gap-1 text-xs" onClick={handleSave} disabled={saving}>
              <Save className="h-3 w-3" />
              {saving ? "Enregistrement…" : "Enregistrer les horaires"}
            </Button>
          </div>
        </>
      )}
    </div>
  )

  if (embedded) {
    return body
  }

  return (
    // No border override: `Card` already renders `border`, which the base layer paints `--border`.
    <Card>
      <CardHeader className="pb-3">
        {/*
          The icon chip — `app/documents/page.tsx`'s template-tile idiom, sized for a header. This header had no
          glyph at all, and `Clock` is the one obviously right for « horaires »; the chip is what makes it a
          mark rather than a second word. `config` is the zone of both routes this card is reachable from
          (« Mon profil » and « Paramètres »), matching the sections it is stacked with.

          ⚠️ Only the un-`embedded` rendering has a header — inside « Paramètres → Médecins » this component
          renders its `body` bare, one per practitioner, so a chip per doctor row is not a thing that can happen.
        */}
        <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <Clock className="size-4" strokeWidth={1.75} />
          </span>
          Mes horaires{doctorName ? ` — ${doctorName}` : ""}
        </CardTitle>
      </CardHeader>
      <CardContent>{body}</CardContent>
    </Card>
  )
}
