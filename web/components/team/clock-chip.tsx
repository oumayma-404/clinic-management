"use client"

import { LogIn, LogOut } from "lucide-react"

import { Button } from "@/components/ui/button"
import { useClockStatus } from "@/components/team/clock-in-gate"
import { pointageStateLabel } from "@/lib/api/pointage"
import { cn } from "@/lib/utils"

/**
 * « Arrivée à 08:02 » / « Départ à 12:05 » in the top bar, for an account that pointe — the one place her pointage is
 * visible once the lock has lifted. Icon and time on a phone; `onOpen` shows the clock dialog.
 */
export function ClockChip({ onOpen }: { onOpen: () => void }) {
  const { status } = useClockStatus()
  if (!status?.tracked) return null
  const present = Boolean(status.presentSince)
  if (!present && !status.leftAt) return null

  // « Arrivée à » / « Départ à » — the shared wording, without its hour.
  const words = pointageStateLabel({ state: present ? "in" : "left", since: null, leftAt: null })
  const time = present ? status.presentSince : status.leftAt
  const Icon = present ? LogIn : LogOut

  return (
    <Button
      variant="ghost"
      size="sm"
      aria-haspopup="dialog"
      onClick={onOpen}
      className={cn(
        "h-8 gap-1 rounded-full border text-xs font-medium tabular-nums has-[>svg]:px-1.5 sm:has-[>svg]:px-2.5 coarse:min-h-11 print:hidden",
        present ? "border-success/40 text-success" : "border-border text-muted-foreground",
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {/* Hidden on a phone, still read aloud: « Arrivée à 08:02 ». */}
      <span className="sr-only sm:not-sr-only">{words}</span>
      <span>{time}</span>
    </Button>
  )
}
