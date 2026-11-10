"use client"

import Link from "next/link"
import { useMemo, useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { Users } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { RecordAttendanceDialog, type AttendanceRecordPrefill } from "@/components/team/attendance-dialogs"
import { pointageApi, pointageStateLabel, type TeamTodayDto, type TeamTodayPersonDto } from "@/lib/api/pointage"
import { dayFromKey } from "@/lib/doctor-leaves"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"

/** Paramètres → « Équipe »: the card's anchor, and the address every « choisissez qui pointe » points at. */
export const TEAM_SETTINGS_ANCHOR = "equipe"
export const TEAM_SETTINGS_HREF = `/settings#${TEAM_SETTINGS_ANCHOR}`

const SETUP_ISSUE: Record<NonNullable<TeamTodayDto["setupIssue"]>, { text: string; tone: "warning" | "negative" }> = {
  no_borne: { text: "Aucune borne active", tone: "warning" },
  borne_pending: { text: "Borne en attente de son appareil", tone: "warning" },
  no_punch_since_opening: { text: "Aucun pointage depuis l'ouverture", tone: "negative" },
}

/** A 5-wrong-schémas block still running. */
export const isSchemaBlocked = (until: string | null | undefined) => Boolean(until && new Date(until).getTime() > Date.now())

/**
 * « Équipe aujourd'hui » (dashboard) and Présences → « Aujourd'hui »: who is here, who has left, who is off — and, per
 * person, what stands in the way (no schéma, a blocked schéma) with the admin's fix beside it.
 */
export function TeamTodayList({ today, onChanged }: { today: TeamTodayDto; onChanged?: () => void }) {
  const [recording, setRecording] = useState<AttendanceRecordPrefill | null>(null)
  const people = useMemo(
    () => today.people.map((p) => ({ userId: p.userId, fullName: p.fullName, isActive: true, countsFrom: null })),
    [today.people],
  )

  if (today.people.length === 0) {
    return (
      <EmptyState
        icon={Users}
        size="compact"
        title="Personne ne pointe dans ce cabinet"
        description={<TeamSettingsLink>Choisissez qui pointe dans Paramètres → Équipe</TeamSettingsLink>}
      />
    )
  }

  // A closed day lists only who came anyway, or still owes a departure.
  const shown = today.isOpenDay
    ? today.people
    : today.people.filter((p) => p.state === "in" || p.state === "left" || p.missingDepartureOn)
  const issue = today.setupIssue ? SETUP_ISSUE[today.setupIssue] : null

  return (
    <div className="space-y-2">
      {!today.isOpenDay && (
        <p role="status" className="rounded-md border bg-card px-3 py-2 text-sm text-muted-foreground">
          Cabinet fermé aujourd&apos;hui
        </p>
      )}
      {today.isOpenDay && issue && (
        <p
          role="status"
          className={cn(
            "flex flex-wrap items-center gap-x-2 gap-y-0.5 rounded-md px-3 py-2 text-sm font-medium",
            issue.tone === "negative" ? "bg-destructive-wash text-destructive" : "bg-warning-wash text-warning-ink",
          )}
        >
          <span>{issue.text}</span>
          <TeamSettingsLink className="font-normal">Paramètres → Équipe</TeamSettingsLink>
        </p>
      )}
      {shown.length > 0 && (
        <ul className="divide-y rounded-md border bg-card">
          {shown.map((person) => (
            <PersonToday
              key={person.userId}
              person={person}
              canRecordArrival={today.isOpenDay}
              onRecordArrival={() => setRecording({ userId: person.userId, day: today.day })}
              onChanged={onChanged}
            />
          ))}
        </ul>
      )}
      <RecordAttendanceDialog
        open={recording !== null}
        onOpenChange={(open) => !open && setRecording(null)}
        people={people}
        prefill={recording}
        onSaved={() => {
          setRecording(null)
          onChanged?.()
        }}
      />
    </div>
  )
}

function PersonToday({
  person,
  canRecordArrival,
  onRecordArrival,
  onChanged,
}: {
  person: TeamTodayPersonDto
  canRecordArrival: boolean
  onRecordArrival: () => void
  onChanged?: () => void
}) {
  const [unlocking, setUnlocking] = useState(false)
  const blocked = isSchemaBlocked(person.schemaLockedUntil)

  const unlock = async () => {
    setUnlocking(true)
    try {
      await pointageApi.unlockSchema(person.userId)
      toast.success(`Schéma de ${person.fullName} débloqué.`)
      onChanged?.()
    } catch (err) {
      showErrorToast(err, "Le schéma n'a pas pu être débloqué.")
    } finally {
      setUnlocking(false)
    }
  }

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2">
      <span
        aria-hidden="true"
        className={cn(
          "size-2 shrink-0 rounded-full",
          person.state === "in"
            ? "bg-success"
            : person.state === "left"
              ? "bg-muted-foreground"
              : person.state === "leave"
                ? "bg-primary/50"
                : "bg-muted-foreground/40",
        )}
      />
      <span className="min-w-0 flex-1 break-words text-sm font-medium">{person.fullName}</span>
      <span className={cn("text-sm", person.state === "in" ? "text-success" : "text-muted-foreground")}>
        {pointageStateLabel(person)}
      </span>
      {(person.needsSchema || blocked || (canRecordArrival && person.state === "out")) && (
        <span className="flex basis-full flex-wrap items-center gap-2 ps-5">
          {person.needsSchema && (
            <span className="rounded bg-warning-wash px-1.5 py-0.5 text-xs font-medium text-warning-ink">
              Schéma à créer
            </span>
          )}
          {blocked && (
            <>
              <span className="rounded bg-destructive-wash px-1.5 py-0.5 text-xs font-medium text-destructive">
                Schéma bloqué
              </span>
              <Button
                variant="outline"
                size="sm"
                className="h-7 px-2 text-xs coarse:h-11"
                disabled={unlocking}
                onClick={() => void unlock()}
                aria-label={`Débloquer le schéma de ${person.fullName}`}
              >
                Débloquer
              </Button>
            </>
          )}
          {canRecordArrival && person.state === "out" && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 px-2 text-xs coarse:h-11"
              onClick={onRecordArrival}
              aria-label={`Pointer son arrivée : ${person.fullName}`}
            >
              Pointer son arrivée
            </Button>
          )}
        </span>
      )}
      {person.missingDepartureOn && (
        <span className="basis-full ps-5 text-xs text-destructive">
          Départ non pointé le {format(dayFromKey(person.missingDepartureOn), "d MMMM", { locale: fr })}
        </span>
      )}
    </li>
  )
}

/** « Paramètres → Équipe » as a real link, wherever a sentence sends the admin there. */
export function TeamSettingsLink({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <Link
      href={TEAM_SETTINGS_HREF}
      className={cn("inline-flex items-center underline underline-offset-2 hover-hover:hover:no-underline coarse:min-h-11", className)}
    >
      {children}
    </Link>
  )
}
