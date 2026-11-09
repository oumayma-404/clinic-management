"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { ArrowLeft, LogIn, MonitorSmartphone } from "lucide-react"

import { Button } from "@/components/ui/button"
import { BorneBoard, useBorneStatus } from "@/components/team/borne-board"
import { useSession } from "@/lib/auth/session"

/**
 * La borne (`pointage-borne` AC-6, AC-16): a reception device nobody is signed in on. Tap your tile, read
 * « Arrivée » or « Départ » in large, draw your schéma. Public route — the device proves itself with the HttpOnly
 * cookie an admin set from Paramètres → Équipe.
 */
export default function PointagePage() {
  const { status, failed, load } = useBorneStatus()
  const { user } = useSession()

  return (
    <main className="flex min-h-dvh flex-col bg-background">
      <header className="flex items-center gap-3 border-b px-4 py-3 sm:px-6">
        <MonitorSmartphone className="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-base font-semibold">Pointage</h1>
          {status?.isBorne && status.name && (
            <p className="truncate text-xs text-muted-foreground">Borne {status.name}</p>
          )}
        </div>
        <LiveClock />
        {/* Someone signed in on this PC came here to point: the way back is to her page, not to a sign-in. */}
        <Button asChild variant="ghost" size="sm" className="touch-target gap-1.5">
          {user ? (
            <Link href="/" aria-label="Retour à l'application">
              <ArrowLeft className="size-4" aria-hidden="true" />
              <span className="hidden sm:inline">Retour à l&apos;application</span>
            </Link>
          ) : (
            <Link href="/login" aria-label="Se connecter">
              <LogIn className="size-4" aria-hidden="true" />
              <span className="hidden sm:inline">Se connecter</span>
            </Link>
          )}
        </Button>
      </header>

      <div className="flex flex-1 flex-col px-4 py-6 sm:px-6">
        <BorneBoard status={status} failed={failed} reload={load} />
      </div>
    </main>
  )
}

function LiveClock() {
  const [now, setNow] = useState<string | null>(null)
  useEffect(() => {
    const tick = () =>
      setNow(new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date()))
    tick()
    const id = window.setInterval(tick, 15_000)
    return () => window.clearInterval(id)
  }, [])
  return <span className="shrink-0 text-lg font-semibold tabular-nums">{now ?? ""}</span>
}
