"use client"

import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { Fingerprint } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { SchemaPad } from "@/components/team/schema-pad"
import { AttendancePanel } from "@/components/team/attendance-panel"
import {
  SCHEMA_MAX_DOTS,
  SCHEMA_MIN_DOTS,
  pointageApi,
  type AttendancePersonSettingDto,
  type MyAttendanceDto,
} from "@/lib/api/pointage"
import { getErrorMessage } from "@/lib/errors"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { cn } from "@/lib/utils"
import { ZONES, zoneChipClass } from "@/lib/zones"

/**
 * « Schéma de pointage » and « Mes présences » in Mon profil (`pointage-borne` AC-5, AC-11): the person draws her
 * schéma twice, and reads her own month, read-only. Shown only to an account that pointe, while the cabinet has the
 * pointage on. In Mon profil rather than « Mes congés », which exists only while the congés are on.
 */
export function SchemaSetupCard() {
  const { data: status } = useUserStatus()
  const enabled = status?.clinic?.attendanceEnabled === true
  const [mine, setMine] = useState<{ tracked: boolean; hasSchema: boolean; hasLines: boolean } | null>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)

  // The panel is the one reader of « Mes présences »; its answer also says whether the schéma card applies.
  // A month with lines keeps her presences readable after she stops pointing.
  const onMine = useCallback((read: MyAttendanceDto | null) => {
    if (!read) {
      setFailed(true)
      return
    }
    setFailed(false)
    const hasLines = read.sheet.days.some((day) => day.entries.length > 0)
    setMine((previous) => ({
      tracked: read.tracked,
      hasSchema: read.hasSchema,
      hasLines: hasLines || previous?.hasLines === true,
    }))
  }, [])

  if (!enabled) return null
  const showPresences = mine ? mine.tracked || mine.hasLines : failed

  return (
    <>
      {mine?.tracked && (
        <Card className="flex-row flex-wrap items-center gap-3 p-4 sm:p-6">
          <span aria-hidden="true" className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}>
            <Fingerprint className="size-4" strokeWidth={1.75} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="font-semibold">Schéma de pointage</p>
            <p className={mine.hasSchema ? "text-sm text-muted-foreground" : "text-sm text-warning-ink"}>
              {mine.hasSchema ? "Enregistré" : "À créer avant de pointer à la borne"}
            </p>
          </div>
          <Button variant={mine.hasSchema ? "outline" : "default"} onClick={() => setOpen(true)}>
            {mine.hasSchema ? "Changer de schéma" : "Créer mon schéma"}
          </Button>
        </Card>
      )}
      <Card className={cn("space-y-4 p-4 sm:p-6", !showPresences && "hidden")}>
        <h2 className="font-semibold">Mes présences</h2>
        <AttendancePanel scope={{ kind: "mine" }} onMine={onMine} />
      </Card>
      <SchemaDialog
        open={open}
        onOpenChange={setOpen}
        onSaved={(saved) => {
          setOpen(false)
          setMine((previous) => (previous ? { ...previous, hasSchema: saved.hasSchema } : previous))
        }}
      />
    </>
  )
}

function SchemaDialog({
  open,
  onOpenChange,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (saved: AttendancePersonSettingDto) => void
}) {
  const [first, setFirst] = useState<number[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setFirst(null)
    setError(null)
  }, [open])

  const drawn = async (pattern: number[]) => {
    if (saving) return
    if (!first) {
      if (pattern.length < SCHEMA_MIN_DOTS) {
        setError(`Reliez au moins ${SCHEMA_MIN_DOTS} points.`)
        return
      }
      setError(null)
      setFirst(pattern)
      return
    }
    if (pattern.join("-") !== first.join("-")) {
      setFirst(null)
      setError("Les deux schémas ne correspondent pas. Recommencez.")
      return
    }
    setSaving(true)
    try {
      const saved = await pointageApi.setMySchema(pattern)
      toast.success("Schéma de pointage enregistré.")
      onSaved(saved)
    } catch (err) {
      setFirst(null)
      setError(getErrorMessage(err, "Le schéma n'a pas pu être enregistré."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !saving && onOpenChange(next)}>
      <DialogContent
        className="md:max-w-sm"
        onEscapeKeyDown={(e) => first && e.preventDefault()}
        onInteractOutside={(e) => first && e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>{first ? "Tracez-le à nouveau" : "Tracez votre schéma"}</DialogTitle>
          <DialogDescription>
            {first ? "Pour confirmer." : `De ${SCHEMA_MIN_DOTS} à ${SCHEMA_MAX_DOTS} points, chaque point une seule fois.`}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="flex flex-col items-center gap-3">
          <SchemaPad
            key={first ? "confirm" : "first"}
            label="Votre schéma de pointage"
            onComplete={(pattern) => void drawn(pattern)}
            disabled={saving}
            error={error !== null}
            autoFocus={first !== null}
          />
          <FormErrorBanner message={error} className="w-full" />
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Retour
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
