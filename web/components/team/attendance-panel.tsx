"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { CalendarClock, ChevronLeft, ChevronRight, Download, Pencil, Plus, Users } from "lucide-react"
import { Button } from "@/components/ui/button"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Stat, StatStrip } from "@/components/ui/stat-strip"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CorrectAttendanceDialog, RecordAttendanceDialog } from "@/components/team/attendance-dialogs"
import { TeamTodayList } from "@/components/team/team-today-list"
import {
  attendanceDayLabel,
  formatAttendanceMinutes,
  pointageApi,
  type AttendanceDayDto,
  type AttendanceEntryDto,
  type AttendanceSheetDto,
  type AttendanceTotalsDto,
  type MyAttendanceDto,
  type TeamTodayDto,
} from "@/lib/api/pointage"
import { dayFromKey } from "@/lib/doctor-leaves"
import { downloadBlob } from "@/lib/download"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { cn } from "@/lib/utils"

/** Whose month: the cabinet's (admin), one person's file (admin), or her own (« Mes présences », read-only). */
export type AttendanceScope = { kind: "all" } | { kind: "person"; userId: string } | { kind: "mine" }

const CLOSED_DAY =
  "bg-[repeating-linear-gradient(135deg,var(--muted)_0_4px,transparent_4px_9px)] text-muted-foreground"

function shiftMonth(month: string, delta: number): string {
  const [y, m] = month.split("-").map(Number)
  const d = new Date(y, m - 1 + delta, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`
}

/**
 * « Présences » (`pointage-borne` AC-9…AC-11): a month of pointage — the day's team (cabinet view), the figures, and
 * the sheet: a table of days × people from `lg`, one card per day below. A missing departure is red and correctable;
 * no hour is ever invented for it.
 */
export function AttendancePanel({
  scope,
  onPersonName,
  onMine,
}: {
  scope: AttendanceScope
  /** One person's file learns the name from the sheet when the congés read is not there to give it. */
  onPersonName?: (name: string) => void
  /** « Mes présences »: the read's flags for Mon profil's schéma card, or null when it failed. */
  onMine?: (mine: MyAttendanceDto | null) => void
}) {
  const currentMonth = todayLocalIso().slice(0, 7)
  const [month, setMonth] = useState(currentMonth)
  const [sheet, setSheet] = useState<AttendanceSheetDto | null>(null)
  const [today, setToday] = useState<TeamTodayDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [loading, setLoading] = useState(true)
  const [correcting, setCorrecting] = useState<AttendanceEntryDto | null>(null)
  const [adding, setAdding] = useState(false)
  const [exporting, setExporting] = useState(false)
  const editable = scope.kind !== "mine"
  const userId = scope.kind === "person" ? scope.userId : undefined

  const load = useCallback(async () => {
    setLoading(true)
    // « Aujourd'hui » is its own read: when it fails, the month it sits above still shows.
    if (scope.kind === "all") {
      void pointageApi.today().then(setToday, () => undefined)
    }
    try {
      if (scope.kind === "mine") {
        const mine = await pointageApi.mine(month)
        setSheet(mine.sheet)
        onMine?.(mine)
      } else {
        const read = await pointageApi.sheet(month, userId)
        setSheet(read)
        if (scope.kind === "person" && read.people[0]) onPersonName?.(read.people[0].fullName)
      }
      setFailed(false)
    } catch {
      setFailed(true)
      if (scope.kind === "mine") onMine?.(null)
    } finally {
      setLoading(false)
    }
  }, [month, scope.kind, userId, onPersonName, onMine])

  useEffect(() => {
    void load()
  }, [load])
  useClinicRealtime(RealtimeResource.Team, () => void load())

  const reloadEntry = useCallback(
    async (id: string) => {
      const read = await pointageApi.sheet(month, userId)
      setSheet(read)
      return read.days.flatMap((d) => d.entries).find((e) => e.id === id) ?? null
    },
    [month, userId],
  )

  const exportMonth = async () => {
    setExporting(true)
    try {
      const { blob, filename } = await pointageApi.exportSheet(month)
      await downloadBlob(blob, filename)
    } catch (err) {
      showErrorToast(err, "L'export n'a pas pu être préparé.")
    } finally {
      setExporting(false)
    }
  }

  const single = scope.kind !== "all"
  // A sheet of another month is never shown under this one's arrows: a failed « Mois précédent » says so instead.
  const shown = sheet && sheet.month === month ? sheet : null
  const totals = useMemo(() => {
    if (!shown) return null
    if (!single) return null
    return shown.totals[0] ?? { userId: "", days: 0, minutes: 0, missingDepartures: 0, openToday: false }
  }, [shown, single])

  return (
    <div className="space-y-5">
      {scope.kind === "all" && today && (
        <section aria-labelledby="presences-today" className="space-y-2">
          <h3 id="presences-today" className="text-sm font-medium">
            Aujourd&apos;hui
          </h3>
          <TeamTodayList people={today.people} />
        </section>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-1">
          <Button
            variant="outline"
            size="icon"
            onClick={() => setMonth((m) => shiftMonth(m, -1))}
            aria-label="Mois précédent"
          >
            <ChevronLeft className="size-4" aria-hidden="true" />
          </Button>
          <h3 className="min-w-36 text-center text-sm font-semibold capitalize" aria-live="polite">
            {shown?.monthLabel ?? format(dayFromKey(`${month}-01`), "MMMM yyyy", { locale: fr })}
          </h3>
          <Button
            variant="outline"
            size="icon"
            onClick={() => setMonth((m) => shiftMonth(m, 1))}
            disabled={month >= currentMonth}
            aria-label="Mois suivant"
          >
            <ChevronRight className="size-4" aria-hidden="true" />
          </Button>
        </div>
        {editable && (
          <div className="ms-auto flex flex-wrap gap-2">
            <Button variant="outline" size="sm" className="gap-1.5" onClick={() => setAdding(true)} disabled={!shown}>
              <Plus className="size-4" aria-hidden="true" />
              Ajouter un pointage
            </Button>
            {scope.kind === "all" && (
              <Button
                variant="outline"
                size="sm"
                className="gap-1.5"
                onClick={() => void exportMonth()}
                disabled={exporting || !shown}
              >
                <Download className="size-4" aria-hidden="true" />
                Exporter
              </Button>
            )}
          </div>
        )}
      </div>

      {failed && <LoadFailureNotice message="Les présences n'ont pas pu être chargées." onRetry={() => void load()} />}
      {!shown ? (
        failed ? null : <div className="h-48 animate-pulse rounded-lg bg-muted/50" aria-hidden="true" />
      ) : (
        <div className={cn("space-y-5", loading && "opacity-60 transition-opacity")}>
          {totals && (
            <StatStrip className="grid-cols-3">
              <Stat label="Jours pointés" value={String(totals.days)} />
              <Stat
                label="Heures"
                value={formatAttendanceMinutes(totals.minutes)}
                hint={totals.openToday ? "sans la journée en cours" : undefined}
              />
              <Stat
                label="Départs non pointés"
                value={String(totals.missingDepartures)}
                tone={totals.missingDepartures > 0 ? "negative" : "neutral"}
              />
            </StatStrip>
          )}

          {single ? (
            <DayCards
              sheet={shown}
              onCorrect={editable ? setCorrecting : undefined}
              onAdd={editable ? () => setAdding(true) : undefined}
              showNames={false}
            />
          ) : (
            <>
              <div className={TABLE_ONLY_LG}>
                <SheetTable sheet={shown} onCorrect={setCorrecting} />
              </div>
              <div className={CARDS_ONLY_LG}>
                <TotalsCards sheet={shown} />
                <DayCards sheet={shown} onCorrect={setCorrecting} onAdd={() => setAdding(true)} showNames />
              </div>
            </>
          )}
        </div>
      )}

      {editable && (
        <>
          <CorrectAttendanceDialog
            entry={correcting}
            onOpenChange={(open) => !open && setCorrecting(null)}
            onSaved={() => {
              setCorrecting(null)
              void load()
            }}
            reloadEntry={reloadEntry}
          />
          <RecordAttendanceDialog
            open={adding}
            onOpenChange={setAdding}
            people={shown?.people.filter((p) => p.isActive) ?? []}
            defaultUserId={userId}
            onSaved={() => {
              setAdding(false)
              void load()
            }}
          />
        </>
      )}
    </div>
  )
}

const dayHeading = (key: string) => format(dayFromKey(key), "EEE d", { locale: fr })
/** « Jeudi 5 novembre » — `capitalize` would raise the month too. */
const dayTitle = (key: string) => {
  const text = attendanceDayLabel(key)
  return text.charAt(0).toUpperCase() + text.slice(1)
}

function EntryLine({ entry, onCorrect }: { entry: AttendanceEntryDto; onCorrect?: (entry: AttendanceEntryDto) => void }) {
  const tail = entry.isPresent ? (
    <span className="text-success">présente</span>
  ) : entry.departureMissing ? (
    <span className="font-medium text-destructive">Départ non pointé</span>
  ) : (
    <span>{entry.departure}</span>
  )
  const label = `Corriger le pointage de ${entry.userName} du ${attendanceDayLabel(entry.localDay)}`
  const hours = (
    <>
      {entry.arrival} – {tail}
    </>
  )
  return (
    <span className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5">
      {onCorrect && !entry.departureMissing ? (
        <button
          type="button"
          onClick={() => onCorrect(entry)}
          aria-label={label}
          className="inline-flex items-center gap-1 whitespace-nowrap rounded tabular-nums underline decoration-muted-foreground/50 decoration-dotted underline-offset-4 hover:decoration-solid focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none coarse:min-h-11"
        >
          {hours}
          <Pencil className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
        </button>
      ) : (
        <span className="whitespace-nowrap tabular-nums">{hours}</span>
      )}
      {entry.isCorrected && (
        <span
          className="rounded bg-muted px-1 text-2xs text-muted-foreground"
          title={entry.correctionReason ?? undefined}
        >
          Corrigé
        </span>
      )}
      {entry.isCorrected && entry.correctionReason && (
        <span className="basis-full text-xs text-muted-foreground">{entry.correctionReason}</span>
      )}
      {onCorrect && entry.departureMissing && (
        <Button
          variant="outline"
          size="sm"
          className="h-7 px-2 text-xs coarse:h-11"
          onClick={() => onCorrect(entry)}
          aria-label={label}
        >
          Corriger
        </Button>
      )}
    </span>
  )
}

function DayCell({
  day,
  userId,
  onCorrect,
}: {
  day: AttendanceDayDto
  userId: string
  onCorrect?: (entry: AttendanceEntryDto) => void
}) {
  const entries = day.entries.filter((e) => e.userId === userId)
  const leave = day.leaves.find((l) => l.userId === userId)
  return (
    <div className="space-y-1 text-sm">
      {leave && (
        <span className="block text-xs text-primary">
          {leave.typeLabel}
          {leave.halfDay ? " · demi-journée" : ""}
        </span>
      )}
      {entries.map((entry) => (
        <EntryLine key={entry.id} entry={entry} onCorrect={onCorrect} />
      ))}
    </div>
  )
}

function SheetTable({ sheet, onCorrect }: { sheet: AttendanceSheetDto; onCorrect: (entry: AttendanceEntryDto) => void }) {
  if (sheet.people.length === 0) return <NobodyPointe />
  return (
    <div className="rounded-md border bg-card">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-28">Jour</TableHead>
            {sheet.people.map((person) => (
              <TableHead key={person.userId}>{person.fullName}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {sheet.days.map((day) => (
            <TableRow key={day.day} className={cn(!day.isOpenDay && CLOSED_DAY, day.isToday && "bg-primary/5")}>
              <TableCell className="whitespace-nowrap font-medium capitalize">{dayHeading(day.day)}</TableCell>
              {sheet.people.map((person) => (
                <TableCell key={person.userId}>
                  <DayCell day={day} userId={person.userId} onCorrect={onCorrect} />
                </TableCell>
              ))}
            </TableRow>
          ))}
          <TableRow className="bg-muted/40 font-medium">
            <TableCell>Total</TableCell>
            {sheet.people.map((person) => (
              <TableCell key={person.userId}>
                <TotalsLine totals={sheet.totals.find((t) => t.userId === person.userId)} />
              </TableCell>
            ))}
          </TableRow>
        </TableBody>
      </Table>
    </div>
  )
}

function TotalsLine({ totals }: { totals?: AttendanceTotalsDto }) {
  if (!totals) return null
  return (
    <span className="block text-sm">
      {totals.days} j · {formatAttendanceMinutes(totals.minutes)}
      {totals.missingDepartures > 0 && (
        <span className="block text-xs text-destructive">
          {totals.missingDepartures} départ{totals.missingDepartures > 1 ? "s" : ""} non pointé
          {totals.missingDepartures > 1 ? "s" : ""}
        </span>
      )}
    </span>
  )
}

function TotalsCards({ sheet }: { sheet: AttendanceSheetDto }) {
  if (sheet.people.length === 0) return null
  return (
    <ul className="mb-4 divide-y rounded-md border bg-card">
      {sheet.people.map((person) => (
        <li key={person.userId} className="flex items-start justify-between gap-3 px-3 py-2">
          <span className="min-w-0 truncate text-sm font-medium">{person.fullName}</span>
          <TotalsLine totals={sheet.totals.find((t) => t.userId === person.userId)} />
        </li>
      ))}
    </ul>
  )
}

function NobodyPointe() {
  return (
    <EmptyState
      icon={Users}
      size="compact"
      title="Personne ne pointe dans ce cabinet"
      description="Choisissez qui pointe dans Paramètres → Équipe."
    />
  )
}

function DayCards({
  sheet,
  onCorrect,
  onAdd,
  showNames,
}: {
  sheet: AttendanceSheetDto
  onCorrect?: (entry: AttendanceEntryDto) => void
  onAdd?: () => void
  showNames: boolean
}) {
  // Newest first, and only the days that hold something: a month of empty cards would bury the one red line.
  const withContent = [...sheet.days].reverse().filter((d) => d.entries.length > 0 || d.leaves.length > 0)
  const people = sheet.people
  return (
    <CardList
      items={withContent}
      getKey={(day) => day.day}
      title={(day) => dayTitle(day.day)}
      accent={(day) => (day.entries.some((e) => e.departureMissing) ? "var(--destructive)" : undefined)}
      fields={(day) =>
        people
          .filter((p) => day.entries.some((e) => e.userId === p.userId) || day.leaves.some((l) => l.userId === p.userId))
          .map((person) => ({
            label: showNames ? person.fullName : "Pointage",
            value: <DayCell day={day} userId={person.userId} onCorrect={onCorrect} />,
          }))
      }
      empty={
        <EmptyState
          icon={CalendarClock}
          size="compact"
          title="Aucun pointage ce mois-ci"
          action={
            onAdd ? (
              <Button variant="outline" size="sm" className="gap-1.5" onClick={onAdd}>
                <Plus className="size-4" aria-hidden="true" />
                Ajouter un pointage
              </Button>
            ) : undefined
          }
        />
      }
      ariaLabel={`Présences de ${sheet.monthLabel}`}
    />
  )
}
