"use client"

import { useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { StaffLeaveRequestDto } from "@/lib/api/team"
import { dayFromKey } from "@/lib/doctor-leaves"
import { toLocalIso } from "@/lib/format"
import { staffLeaveShortPeriod } from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

const WEEKDAYS = ["L", "M", "M", "J", "V", "S", "D"]
const WEEKDAY_NAMES = ["lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche"]

const covers = (request: StaffLeaveRequestDto, key: string) => request.startDate <= key && key <= request.endDate

/** The month at a glance: accepted days filled, waiting ones outlined, today ringed. */
export function StaffLeaveMonthCalendar({ items, todayKey }: { items: StaffLeaveRequestDto[]; todayKey: string }) {
  const [month, setMonth] = useState(() => {
    const today = dayFromKey(todayKey)
    return new Date(today.getFullYear(), today.getMonth(), 1)
  })
  const year = month.getFullYear()
  const monthIndex = month.getMonth()
  const offset = (month.getDay() + 6) % 7
  const length = new Date(year, monthIndex + 1, 0).getDate()

  const cells: Array<{ key: string; day: number; accepted: boolean; pending: boolean } | null> = [
    ...Array.from({ length: offset }, () => null),
    ...Array.from({ length }, (_, i) => {
      const key = toLocalIso(new Date(year, monthIndex, i + 1))
      return {
        key,
        day: i + 1,
        accepted: items.some((r) => r.status === "Approved" && covers(r, key)),
        pending: items.some((r) => r.status === "Pending" && covers(r, key)),
      }
    }),
  ]
  const weeks: (typeof cells)[] = []
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7))

  const title = format(month, "MMMM yyyy", { locale: fr })
  const shift = (delta: number) => setMonth(new Date(year, monthIndex + delta, 1))

  return (
    <div className="space-y-4 rounded-xl border bg-card p-4 sm:p-5">
      <div className="flex items-center justify-between gap-2">
        <p className="font-semibold capitalize" aria-live="polite">
          {title}
        </p>
        <div className="flex gap-1">
          <Button variant="outline" size="icon" className="size-8 coarse:size-11" aria-label="Mois précédent" onClick={() => shift(-1)}>
            <ChevronLeft className="size-4" aria-hidden="true" />
          </Button>
          <Button variant="outline" size="icon" className="size-8 coarse:size-11" aria-label="Mois suivant" onClick={() => shift(1)}>
            <ChevronRight className="size-4" aria-hidden="true" />
          </Button>
        </div>
      </div>

      <table className="w-full table-fixed border-separate border-spacing-1 text-center text-sm">
        <caption className="sr-only">Congés de {title}</caption>
        <thead>
          <tr>
            {WEEKDAYS.map((label, i) => (
              <th key={i} scope="col" className="pb-1 text-2xs font-semibold text-muted-foreground">
                <abbr title={WEEKDAY_NAMES[i]} className="no-underline">
                  {label}
                </abbr>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {weeks.map((week, w) => (
            <tr key={w}>
              {Array.from({ length: 7 }, (_, d) => {
                const cell = week[d]
                if (!cell) return <td key={d} />
                const today = cell.key === todayKey
                return (
                  <td key={d} className="p-0">
                    <span
                      className={cn(
                        "flex h-8 items-center justify-center rounded-md tabular-nums",
                        cell.accepted && "bg-primary/15 font-semibold text-primary",
                        !cell.accepted && cell.pending && "border border-dashed border-warning text-warning-ink",
                        today && "ring-2 ring-primary ring-inset",
                      )}
                    >
                      {cell.day}
                      {cell.accepted && <span className="sr-only"> — congé accepté</span>}
                      {!cell.accepted && cell.pending && <span className="sr-only"> — en attente</span>}
                      {today && <span className="sr-only"> — aujourd&apos;hui</span>}
                    </span>
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>

      <div className="flex flex-wrap gap-x-4 gap-y-1 border-t pt-3 text-xs text-muted-foreground">
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="size-2.5 rounded-sm bg-primary/15" />
          Congé accepté
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="size-2.5 rounded-sm border border-dashed border-warning" />
          En attente
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="size-2.5 rounded-sm ring-2 ring-primary ring-inset" />
          Aujourd&apos;hui
        </span>
      </div>
    </div>
  )
}

/** « Prochain congé · 16 → 18 nov. · dans 11 jours », or nothing when none is accepted ahead. */
export function StaffLeaveNextCard({ items, todayKey }: { items: StaffLeaveRequestDto[]; todayKey: string }) {
  const next = items
    .filter((r) => r.status === "Approved" && r.endDate >= todayKey)
    .sort((a, b) => a.startDate.localeCompare(b.startDate))[0]
  if (!next) return null
  const inDays = Math.round((dayFromKey(next.startDate).getTime() - dayFromKey(todayKey).getTime()) / 86_400_000)
  const when = inDays <= 0 ? "en cours" : inDays === 1 ? "demain" : `dans ${inDays} jours`
  return (
    <div className="flex items-center gap-3 rounded-xl border bg-card p-4">
      <span aria-hidden="true" className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-accent text-primary">
        <CalendarDays className="size-5" />
      </span>
      <div className="min-w-0">
        <p className="text-xs text-muted-foreground">Prochain congé</p>
        <p className="font-semibold">
          {staffLeaveShortPeriod(next)} · {when}
        </p>
      </div>
    </div>
  )
}
