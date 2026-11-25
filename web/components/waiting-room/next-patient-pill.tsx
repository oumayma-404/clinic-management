"use client"

import { useState } from "react"
import Link from "next/link"
import { usePathname } from "next/navigation"
import { Armchair, BellRing, Loader2 } from "lucide-react"

import { myChair, myQueue } from "@/components/waiting-room/doctor-next-panel"
import { Button } from "@/components/ui/button"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { useSession } from "@/lib/auth/session"
import { runBoardAction, useWaitingRoom } from "@/lib/hooks/use-waiting-room"
import { cn } from "@/lib/utils"

/**
 * The dentist's « Patient suivant » from any screen (`salle-attente` S5): how many wait for him, and the button —
 * so the fiche he is typing in does not have to be left to call the next patient. Rendered only for a caller the
 * server says may call (`canCallNext`), and not on the salle d'attente itself, where the full panel already is.
 */
export function NextPatientPill() {
  const { user } = useSession()
  const pathname = usePathname()
  const mayCall = user?.role === "doctor" || user?.role === "admin"
  const onBoard = pathname?.startsWith("/salle-attente") ?? false
  const { board, reload } = useWaitingRoom(null, { enabled: mayCall && !onBoard })
  const [busy, setBusy] = useState(false)

  if (!mayCall || onBoard || !board?.canCallNext) return null
  const queue = myQueue(board, board.callerDoctorId)
  const chair = myChair(board, board.callerDoctorId)
  if (queue.length === 0 && !chair) return null
  const next = queue[0] ?? null

  const call = async () => {
    if (!next || busy) return
    setBusy(true)
    await runBoardAction(() => waitingRoomApi.callNext({ expectedNextId: next.appointmentId }), reload, {
      message: (change) => `${change.patientName ?? "Le patient"} est appelé — la réception est prévenue`,
    })
    setBusy(false)
  }

  return (
    <div className="flex items-center gap-1 rounded-full border bg-card p-0.5 ps-1">
      <Link
        href="/salle-attente"
        className="relative flex size-8 items-center justify-center rounded-full hover:bg-accent coarse:size-11"
        aria-label={`Salle d'attente : ${queue.length} patient${queue.length > 1 ? "s" : ""} pour vous`}
      >
        <Armchair className="size-4" aria-hidden="true" />
        {queue.length > 0 && (
          <span className="absolute -end-0.5 -top-0.5 flex min-w-4 items-center justify-center rounded-full bg-primary px-1 text-2xs font-bold leading-4 text-primary-foreground tabular-nums">
            {queue.length}
          </span>
        )}
      </Link>
      {next && (
        <Button
          size="sm"
          className={cn("h-8 gap-1.5 rounded-full px-3 coarse:h-11")}
          disabled={busy}
          onClick={() => void call()}
          aria-label={`Patient suivant : faire entrer ${next.patientName}`}
        >
          {busy ? (
            <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />
          ) : (
            <BellRing className="size-4" aria-hidden="true" />
          )}
          <span className="hidden sm:inline">Suivant</span>
        </Button>
      )}
    </div>
  )
}
