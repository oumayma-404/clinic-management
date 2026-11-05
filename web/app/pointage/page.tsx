"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import Link from "next/link"
import { CheckCircle2, Loader2, LogIn, MonitorSmartphone } from "lucide-react"

import { Button } from "@/components/ui/button"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { SchemaPad } from "@/components/team/schema-pad"
import { ApiError } from "@/lib/api/client"
import {
  PointageErrorCode,
  SCHEMA_MIN_DOTS,
  pointageApi,
  pointageStateLabel,
  type PointagePersonDto,
  type PointageStatusDto,
  type PunchResultDto,
} from "@/lib/api/pointage"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"

/** The confirmation stays this long, then the tiles come back (AC-6). */
const CONFIRMATION_MS = 2200
/** A punch panel left alone goes back to the tiles, so the next person never finds someone else's name open. */
const IDLE_MS = 30_000
/** The tiles re-read the day this often, for a borne left on all morning. */
const REFRESH_MS = 60_000
/** How long a refusal that ends the panel (blocked, not a borne, stale tile) stays readable. */
const REFUSAL_BACK_MS = 3500

/** Refusals meaning the tile itself is out of date or closed: the panel goes back and the tiles are re-read. */
const BACK_TO_TILES: ReadonlySet<string> = new Set([
  PointageErrorCode.SchemaLocked,
  PointageErrorCode.TerminalUnknown,
  PointageErrorCode.ModuleOff,
  PointageErrorCode.SchemaMissing,
  PointageErrorCode.PersonNotTracked,
  PointageErrorCode.OnLeave,
  PointageErrorCode.PunchStateChanged,
])

type Stage =
  | { kind: "tiles" }
  | { kind: "punch"; person: PointagePersonDto }
  | { kind: "done"; result: PunchResultDto }

/**
 * La borne (`pointage-borne` AC-6, AC-16): a reception device nobody is signed in on. Tap your tile, read
 * « Arrivée » or « Départ » in large, draw your schéma. Public route — the device proves itself with the HttpOnly
 * cookie an admin set from Paramètres → Équipe.
 */
export default function PointagePage() {
  const [status, setStatus] = useState<PointageStatusDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [stage, setStage] = useState<Stage>({ kind: "tiles" })
  const [announcement, setAnnouncement] = useState("")
  const lastPersonId = useRef<string | null>(null)
  const refocus = useRef(false)

  const load = useCallback(async () => {
    try {
      setStatus(await pointageApi.status())
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])

  useEffect(() => {
    void load()
    const id = window.setInterval(() => void load(), REFRESH_MS)
    const onVisible = () => document.visibilityState === "visible" && void load()
    document.addEventListener("visibilitychange", onVisible)
    return () => {
      window.clearInterval(id)
      document.removeEventListener("visibilitychange", onVisible)
    }
  }, [load])

  // A locked tile opens again the moment its lock ends, not at the next minute's refresh.
  useEffect(() => {
    const ends = (status?.people ?? []).flatMap((p) => (p.lockedUntil ? [new Date(p.lockedUntil).getTime()] : []))
    if (ends.length === 0) return
    const wait = Math.max(1000, Math.min(...ends) - Date.now() + 500)
    const id = window.setTimeout(() => void load(), wait)
    return () => window.clearTimeout(id)
  }, [status, load])

  const backToTiles = useCallback(() => {
    refocus.current = true
    setStage({ kind: "tiles" })
    void load()
  }, [load])

  // Focus returns to the tile just used, so a keyboard or screen-reader user is not dropped at the top of the page.
  useEffect(() => {
    if (stage.kind !== "tiles" || !refocus.current || !lastPersonId.current) return
    const tile = document.querySelector<HTMLButtonElement>(`[data-person-id="${CSS.escape(lastPersonId.current)}"]`)
    if (!tile) return
    refocus.current = false
    tile.focus()
  }, [stage, status])

  useEffect(() => {
    if (stage.kind !== "done") return
    const id = window.setTimeout(backToTiles, CONFIRMATION_MS)
    return () => window.clearTimeout(id)
  }, [stage, backToTiles])

  const done = (result: PunchResultDto) => {
    setAnnouncement(
      `${result.action === "arrival" ? "Arrivée enregistrée" : "Départ enregistré"} à ${result.at}, ${result.name}.`,
    )
    setStage({ kind: "done", result })
  }

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
        <Button asChild variant="ghost" size="sm" className="gap-1.5">
          <Link href="/login">
            <LogIn className="size-4" aria-hidden="true" />
            <span className="hidden sm:inline">Se connecter</span>
            <span className="sr-only sm:hidden">Se connecter</span>
          </Link>
        </Button>
      </header>

      <p className="sr-only" role="status" aria-live="polite">
        {announcement}
      </p>

      <div className="flex flex-1 flex-col px-4 py-6 sm:px-6">
        {failed && !status ? (
          <LoadFailureNotice message="La borne n'a pas pu joindre le serveur." onRetry={() => void load()} />
        ) : !status ? (
          <div className="flex flex-1 items-center justify-center">
            <Loader2 className="size-6 animate-spin text-muted-foreground" aria-label="Chargement" />
          </div>
        ) : !status.isBorne ? (
          <Message
            title="Cet appareil n'est pas une borne du cabinet."
            detail="Un administrateur peut l'enregistrer depuis Paramètres → Équipe → « Faire de cet appareil une borne »."
          />
        ) : !status.enabled ? (
          <Message title="Le pointage n'est pas activé pour ce cabinet." detail="Il s'active dans Paramètres → Équipe." />
        ) : stage.kind === "punch" ? (
          <PunchPanel person={stage.person} onDone={done} onBack={backToTiles} />
        ) : stage.kind === "done" ? (
          <Confirmation result={stage.result} />
        ) : status.people.length === 0 ? (
          <Message title="Personne ne pointe sur cette borne." detail="Choisissez qui pointe dans Paramètres → Équipe." />
        ) : (
          <Tiles
            people={status.people}
            onPick={(person) => {
              lastPersonId.current = person.userId
              setAnnouncement("")
              setStage({ kind: "punch", person })
            }}
          />
        )}
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

function Message({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="mx-auto my-auto max-w-md space-y-2 text-center" role="status">
      <p className="text-lg font-semibold">{title}</p>
      <p className="text-sm text-muted-foreground">{detail}</p>
    </div>
  )
}

/** « Réessayez dans 14 min » (AC-7) while a lock runs; the shared wording otherwise. */
function stateLine(person: PointagePersonDto): string {
  if (person.state === "leave") return pointageStateLabel(person)
  if (person.needsSchema) return "Schéma à créer dans Mon profil"
  if (person.lockedUntil) {
    const minutes = Math.max(1, Math.ceil((new Date(person.lockedUntil).getTime() - Date.now()) / 60_000))
    return `Bloqué · réessayez dans ${minutes} min`
  }
  return pointageStateLabel(person)
}

function Tiles({ people, onPick }: { people: PointagePersonDto[]; onPick: (person: PointagePersonDto) => void }) {
  return (
    <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4" aria-label="Qui pointe ?">
      {people.map((person) => {
        const blocked = person.state === "leave" || person.needsSchema || person.lockedUntil !== null
        return (
          <li key={person.userId}>
            <button
              type="button"
              data-person-id={person.userId}
              disabled={blocked}
              onClick={() => onPick(person)}
              className={cn(
                "flex w-full items-center gap-4 rounded-xl border bg-card p-4 text-start transition-colors",
                "min-h-24 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
                blocked ? "cursor-not-allowed opacity-60" : "hover:bg-accent/40 active:bg-accent/60",
                person.state === "in" && !blocked && "border-success/50",
              )}
            >
              <InitialsAvatar name={person.name} className="size-14 text-lg" />
              <span className="min-w-0 flex-1">
                <span className="line-clamp-2 break-words text-lg font-semibold">{person.name}</span>
                <span
                  className={cn(
                    "block text-sm",
                    person.state === "in" && !blocked ? "text-success" : "text-muted-foreground",
                    person.lockedUntil && "text-destructive",
                  )}
                >
                  {stateLine(person)}
                </span>
              </span>
            </button>
          </li>
        )
      })}
    </ul>
  )
}

function PunchPanel({
  person,
  onDone,
  onBack,
}: {
  person: PointagePersonDto
  onDone: (result: PunchResultDto) => void
  onBack: () => void
}) {
  const departure = person.state === "in"
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [closing, setClosing] = useState(false)
  const idle = useRef<number | null>(null)
  const goBack = useRef<number | null>(null)
  const mounted = useRef(true)

  const armIdle = useCallback(() => {
    if (idle.current) window.clearTimeout(idle.current)
    idle.current = window.setTimeout(onBack, IDLE_MS)
  }, [onBack])

  useEffect(() => {
    mounted.current = true
    armIdle()
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onBack()
    window.addEventListener("keydown", onKey)
    return () => {
      mounted.current = false
      if (idle.current) window.clearTimeout(idle.current)
      if (goBack.current) window.clearTimeout(goBack.current)
      window.removeEventListener("keydown", onKey)
    }
  }, [armIdle, onBack])

  const submit = async (pattern: number[]) => {
    if (busy || closing) return
    armIdle()
    // A stray tap is not a try: the server would refuse it the same way, without counting it.
    if (pattern.length < SCHEMA_MIN_DOTS) {
      setError(`Reliez au moins ${SCHEMA_MIN_DOTS} points.`)
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await pointageApi.punch(person.userId, pattern, departure ? "departure" : "arrival")
      if (mounted.current) onDone(result)
    } catch (err) {
      if (!mounted.current) return
      const code = err instanceof ApiError ? err.code : undefined
      setError(getErrorMessage(err, "Le pointage n'a pas pu être enregistré."))
      if (code && BACK_TO_TILES.has(code)) {
        setClosing(true)
        goBack.current = window.setTimeout(onBack, REFUSAL_BACK_MS)
      }
    } finally {
      if (mounted.current) setBusy(false)
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-sm flex-1 flex-col items-center gap-5" onPointerDown={armIdle}>
      <div className="text-center">
        <p className="text-base text-muted-foreground">{person.name}</p>
        <p
          className={cn(
            "text-5xl font-bold tracking-tight sm:text-6xl",
            departure ? "text-warning-ink" : "text-success",
          )}
        >
          {departure ? "Départ" : "Arrivée"}
        </p>
      </div>
      <div className="flex w-full justify-center">
        <SchemaPad
          label={`Schéma de ${person.name}`}
          onComplete={(pattern) => void submit(pattern)}
          disabled={busy || closing}
          error={error !== null}
          hideTrail
          autoFocus
        />
      </div>
      <p className="min-h-10 text-center text-sm text-destructive" role="alert">
        {busy ? "" : error}
      </p>
      <Button variant="outline" size="lg" className="w-full max-w-72" onClick={onBack} disabled={busy}>
        Retour
      </Button>
    </div>
  )
}

function Confirmation({ result }: { result: PunchResultDto }) {
  const arrival = result.action === "arrival"
  return (
    <div className="mx-auto my-auto flex max-w-md flex-col items-center gap-3 text-center">
      <CheckCircle2 className={cn("size-16", arrival ? "text-success" : "text-warning-ink")} aria-hidden="true" />
      <p className="text-2xl font-semibold">{arrival ? "Arrivée enregistrée" : "Départ enregistré"}</p>
      <p className="text-lg tabular-nums text-muted-foreground">
        {result.at} · Borne {result.terminalName}
      </p>
      <p className="text-base">{result.name}</p>
    </div>
  )
}
