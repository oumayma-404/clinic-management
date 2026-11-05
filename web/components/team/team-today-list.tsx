"use client"

import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { Users } from "lucide-react"
import { EmptyState } from "@/components/ui/empty-state"
import { pointageStateLabel, type TeamTodayPersonDto } from "@/lib/api/pointage"
import { dayFromKey } from "@/lib/doctor-leaves"
import { cn } from "@/lib/utils"

/** « Présente depuis 08:02 » · « Pas encore arrivée » · « En congé · Maladie », plus a red open line from an earlier day. */
export function TeamTodayList({ people }: { people: TeamTodayPersonDto[] }) {
  if (people.length === 0) {
    return (
      <EmptyState
        icon={Users}
        size="compact"
        title="Personne ne pointe dans ce cabinet"
        description="Choisissez qui pointe dans Paramètres → Équipe."
      />
    )
  }
  return (
    <ul className="divide-y rounded-md border bg-card">
      {people.map((person) => (
        <li key={person.userId} className="flex flex-wrap items-center gap-x-3 gap-y-0.5 px-3 py-2">
          <span
            aria-hidden="true"
            className={cn(
              "size-2 shrink-0 rounded-full",
              person.state === "in" ? "bg-success" : person.state === "leave" ? "bg-primary/50" : "bg-muted-foreground/40",
            )}
          />
          <span className="min-w-0 flex-1 truncate text-sm font-medium">{person.fullName}</span>
          <span className={cn("text-sm", person.state === "in" ? "text-success" : "text-muted-foreground")}>
            {pointageStateLabel(person)}
          </span>
          {person.missingDepartureOn && (
            <span className="basis-full ps-5 text-xs text-destructive">
              Départ non pointé le {format(dayFromKey(person.missingDepartureOn), "d MMMM", { locale: fr })}
            </span>
          )}
        </li>
      ))}
    </ul>
  )
}
