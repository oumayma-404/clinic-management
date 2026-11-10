"use client"

import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { Fingerprint } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { AttendancePanel } from "@/components/team/attendance-panel"
import { clockTime, useClockStatus, useFutureInstant } from "@/components/team/clock-in-gate"
import { OPEN_SCHEMA_EVENT, SCHEMA_PARAM, SchemaSetter } from "@/components/team/schema-setter"
import type { MyAttendanceDto } from "@/lib/api/pointage"
import { replaceUrl } from "@/lib/history"
import { useUrlFilterSeed } from "@/lib/hooks/use-url-filters"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { cn } from "@/lib/utils"
import { ZONES, zoneChipClass } from "@/lib/zones"

/**
 * « Schéma de pointage » and « Mes présences » (`pointage-borne` AC-5, AC-11): the person draws her schéma twice, and
 * reads her own month, read-only. In Mon profil and on « Mes présences »; the schéma card is decided from her clock
 * status, so a failed month read never takes the button away.
 */
export function SchemaSetupCard({ page = false }: { page?: boolean }) {
  const { data: userStatus } = useUserStatus()
  const enabled = userStatus?.clinic?.attendanceEnabled === true
  const { status: clock, refresh } = useClockStatus()
  const tracked = clock?.tracked === true
  const hasSchema = clock?.hasSchema === true
  const blockedUntil = useFutureInstant(clock?.schemaLockedUntil)
  const [hasLines, setHasLines] = useState(false)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)
  const seed = useUrlFilterSeed()
  const [wantsOpen, setWantsOpen] = useState(() => seed.get(SCHEMA_PARAM) === "1")

  // A month with lines keeps her presences readable after she stops pointing.
  const onMine = useCallback((read: MyAttendanceDto | null) => {
    if (!read) {
      setFailed(true)
      return
    }
    setFailed(false)
    if (read.sheet.days.some((day) => day.entries.length > 0)) setHasLines(true)
  }, [])

  useEffect(() => {
    const ask = () => setWantsOpen(true)
    window.addEventListener(OPEN_SCHEMA_EVENT, ask)
    return () => window.removeEventListener(OPEN_SCHEMA_EVENT, ask)
  }, [])

  // The deep link opens the dialog once her status says she pointe, then leaves the address bar clean.
  useEffect(() => {
    if (!wantsOpen || !tracked) return
    setWantsOpen(false)
    setOpen(true)
    const url = new URL(window.location.href)
    if (url.searchParams.has(SCHEMA_PARAM)) {
      url.searchParams.delete(SCHEMA_PARAM)
      replaceUrl(url)
    }
  }, [wantsOpen, tracked])

  if (!enabled) return null
  const showPresences = page || tracked || hasLines || failed

  return (
    <>
      {tracked && (
        <Card className="flex-row flex-wrap items-center gap-3 p-4 sm:p-6">
          <span aria-hidden="true" className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}>
            <Fingerprint className="size-4" strokeWidth={1.75} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="font-semibold">Schéma de pointage</p>
            <p className={cn("text-sm", !hasSchema ? "text-warning-ink" : blockedUntil ? "text-destructive" : "text-muted-foreground")}>
              {!hasSchema ? "Schéma à créer" : blockedUntil ? `Bloqué jusqu'à ${clockTime(blockedUntil)}` : "Enregistré"}
            </p>
          </div>
          <Button variant={hasSchema ? "outline" : "default"} onClick={() => setOpen(true)}>
            {hasSchema ? "Changer votre schéma" : "Créer votre schéma"}
          </Button>
        </Card>
      )}
      <Card className={cn("space-y-4 p-4 sm:p-6", !showPresences && "hidden")}>
        {!page && <h2 className="font-semibold">Mes présences</h2>}
        <AttendancePanel scope={{ kind: "mine" }} onMine={onMine} />
      </Card>
      <SchemaDialog
        open={open}
        hasSchema={hasSchema}
        onOpenChange={setOpen}
        onSaved={() => {
          setOpen(false)
          toast.success("Schéma enregistré.")
          void refresh()
        }}
      />
    </>
  )
}

function SchemaDialog({
  open,
  hasSchema,
  onOpenChange,
  onSaved,
}: {
  open: boolean
  hasSchema: boolean
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent
        className="md:max-w-sm"
        aria-describedby={undefined}
        onEscapeKeyDown={(e) => dirty && e.preventDefault()}
        onInteractOutside={(e) => dirty && e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>{hasSchema ? "Changer votre schéma" : "Créer votre schéma"}</DialogTitle>
        </DialogHeader>
        <DialogBody className="flex flex-col items-center">
          <SchemaSetter
            hasSchema={hasSchema}
            onSaved={onSaved}
            onBusyChange={setBusy}
            onDirtyChange={setDirty}
            padClassName="max-w-[min(18rem,40dvh)]"
          />
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Retour
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
