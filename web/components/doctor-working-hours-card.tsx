"use client"

import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Save, Trash2 } from "lucide-react"
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
                  <div className="flex flex-1 items-center gap-2">
                    <Label htmlFor={fromId} className="sr-only">
                      {`Heure d'ouverture — ${DAY_LABELS_FR[weekday] ?? weekday}`}
                    </Label>
                    <Input
                      id={fromId}
                      type="time"
                      value={day.from}
                      onChange={(e) => updateDay(weekday, { from: e.target.value })}
                      disabled={saving || !day.enabled}
                      className="h-7 text-xs"
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
                      className="h-7 text-xs"
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
    <Card className="border border-gray-200 dark:border-slate-800">
      <CardHeader className="pb-3">
        <CardTitle className="text-base">
          Mes horaires{doctorName ? ` — ${doctorName}` : ""}
        </CardTitle>
      </CardHeader>
      <CardContent>{body}</CardContent>
    </Card>
  )
}
