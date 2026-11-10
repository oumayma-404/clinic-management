"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { CheckCircle2, Loader2 } from "lucide-react"

import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PunchFlow, type PunchStage } from "@/components/team/punch-flow"
import {
  pointageApi,
  pointageStateLabel,
  type PointagePersonDto,
  type PointageStatusDto,
  type PunchResultDto,
} from "@/lib/api/pointage"
import { cn } from "@/lib/utils"

/** The confirmation stays this long, then the tiles come back (AC-6). */
const CONFIRMATION_MS = 2200
/** A punch panel left alone goes back to the tiles, so the next person never finds someone else's name open. */
const IDLE_MS = 30_000
/** The « départ d'hier » step asks for an hour to type: it waits longer before giving up. */
const DECLARE_IDLE_MS = 120_000
/** The tiles re-read the day this often, for a borne left on all morning. */
const REFRESH_MS = 60_000
/** How long a refusal that ends the panel (blocked, not a borne, stale tile) stays readable. */
const REFUSAL_BACK_MS = 3500

/**
 * Whether this device is a borne, and its tiles — read on mount, every minute, when the tab comes back, and the moment
 * a tile's 15-min block ends. `active` false (a closed dialog) reads nothing.
 */
export function useBorneStatus(active = true) {
  const [status, setStatus] = useState<PointageStatusDto | null>(null)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    try {
      setStatus(await pointageApi.status())
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])

  useEffect(() => {
    if (!active) return
    void load()
    const id = window.setInterval(() => void load(), REFRESH_MS)
    const onVisible = () => document.visibilityState === "visible" && void load()
    document.addEventListener("visibilitychange", onVisible)
    return () => {
      window.clearInterval(id)
      document.removeEventListener("visibilitychange", onVisible)
    }
  }, [active, load])

  // A locked tile opens again the moment its lock ends, not at the next minute's refresh.
  useEffect(() => {
    if (!active) return
    const ends = (status?.people ?? []).flatMap((p) => (p.lockedUntil ? [new Date(p.lockedUntil).getTime()] : []))
    if (ends.length === 0) return
    const wait = Math.max(1000, Math.min(...ends) - Date.now() + 500)
    const id = window.setTimeout(() => void load(), wait)
    return () => window.clearTimeout(id)
  }, [active, status, load])

  return { status, failed, load }
}

type Stage =
  | { kind: "tiles" }
  | { kind: "punch"; person: PointagePersonDto }
  | { kind: "done"; result: PunchResultDto }

export type BorneStage = Stage["kind"]

/**
 * La borne's tiles and punch (`pointage-borne` AC-6, AC-16): tap your tile, read « Arrivée » or « Départ » in large,
 * draw your schéma, read the confirmation. One implementation for `/pointage` and the « Pointage » dialog a signed-in
 * reception PC opens over its page.
 */
export function BorneBoard({
  status,
  failed,
  reload,
  onFinished,
  onStageChange,
  onHoldChange,
  padClassName,
}: {
  status: PointageStatusDto | null
  failed: boolean
  reload: () => Promise<void>
  /** Called once the confirmation has been read; absent, the tiles come back instead. */
  onFinished?: (result: PunchResultDto) => void
  onStageChange?: (stage: BorneStage) => void
  /** A punch in flight or the « départ d'hier » step: the host must not close over it. */
  onHoldChange?: (hold: boolean) => void
  /** The pad's size in a host shorter than a borne, so a stroke never needs a scroll. */
  padClassName?: string
}) {
  const [stage, setStage] = useState<Stage>({ kind: "tiles" })
  const [announcement, setAnnouncement] = useState("")
  const lastPersonId = useRef<string | null>(null)
  const refocus = useRef(false)

  useEffect(() => {
    onStageChange?.(stage.kind)
  }, [stage.kind, onStageChange])

  const backToTiles = useCallback(() => {
    refocus.current = true
    onHoldChange?.(false)
    setStage({ kind: "tiles" })
    void reload()
  }, [reload, onHoldChange])

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
    const result = stage.result
    const id = window.setTimeout(() => (onFinished ? onFinished(result) : backToTiles()), CONFIRMATION_MS)
    return () => window.clearTimeout(id)
  }, [stage, backToTiles, onFinished])

  const done = (result: PunchResultDto) => {
    onHoldChange?.(false)
    setAnnouncement(
      `${result.action === "arrival" ? "Arrivée enregistrée" : "Départ enregistré"} à ${result.at}, ${result.name}.`,
    )
    setStage({ kind: "done", result })
  }

  return (
    <>
      <p className="sr-only" role="status" aria-live="polite">
        {announcement}
      </p>
      {failed && !status ? (
        <LoadFailureNotice message="La borne n'a pas pu joindre le serveur." onRetry={() => void reload()} />
      ) : !status ? (
        <div className="flex flex-1 items-center justify-center py-6">
          <Loader2 className="size-6 animate-spin text-muted-foreground" aria-label="Chargement" />
        </div>
      ) : !status.isBorne ? (
        <BorneMessage
          title="Cet appareil n'est pas une borne du cabinet."
          detail="Un administrateur peut l'ajouter depuis Paramètres → Équipe → « Ajouter une borne »."
        />
      ) : !status.enabled ? (
        <BorneMessage title="Le pointage n'est pas activé pour ce cabinet." detail="Il s'active dans Paramètres → Équipe." />
      ) : stage.kind === "punch" ? (
        <PunchPanel
          person={stage.person}
          onDone={done}
          onBack={backToTiles}
          onHoldChange={onHoldChange}
          padClassName={padClassName}
        />
      ) : stage.kind === "done" ? (
        <Confirmation result={stage.result} />
      ) : status.people.length === 0 ? (
        <BorneMessage title="Personne ne pointe sur cette borne." detail="Choisissez qui pointe dans Paramètres → Équipe." />
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
    </>
  )
}

function BorneMessage({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="mx-auto my-auto max-w-md space-y-2 py-4 text-center" role="status">
      <p className="text-lg font-semibold">{title}</p>
      <p className="text-sm text-muted-foreground">{detail}</p>
    </div>
  )
}

/** « Schéma bloqué · réessayez dans 14 min » (AC-7) while a lock runs; the shared wording otherwise. */
function stateLine(person: PointagePersonDto): string {
  if (person.state === "leave") return pointageStateLabel(person)
  if (person.needsSchema) return "Schéma à créer"
  if (person.lockedUntil) {
    const minutes = Math.max(1, Math.ceil((new Date(person.lockedUntil).getTime() - Date.now()) / 60_000))
    return `Schéma bloqué · réessayez dans ${minutes} min`
  }
  return pointageStateLabel(person)
}

function Tiles({ people, onPick }: { people: PointagePersonDto[]; onPick: (person: PointagePersonDto) => void }) {
  return (
    // The width is the host's — a full page or a dialog — so the columns follow it, not the window.
    <div className="@container">
      <ul className="grid gap-3 @xl:grid-cols-2 @4xl:grid-cols-3 @6xl:grid-cols-4" aria-label="Qui pointe ?">
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
    </div>
  )
}

function PunchPanel({
  person,
  onDone,
  onBack,
  onHoldChange,
  padClassName,
}: {
  person: PointagePersonDto
  onDone: (result: PunchResultDto) => void
  onBack: () => void
  onHoldChange?: (hold: boolean) => void
  padClassName?: string
}) {
  const [flowStage, setFlowStage] = useState<PunchStage>("pad")
  const [busy, setBusy] = useState(false)
  const idle = useRef<number | null>(null)
  const goBack = useRef<number | null>(null)
  // Escape and the idle return both read these: a typed hour and a punch in flight are never thrown away.
  const declaring = useRef(false)
  const inFlight = useRef(false)

  useEffect(() => {
    declaring.current = flowStage === "declare"
    inFlight.current = busy
    onHoldChange?.(flowStage === "declare" || busy)
  }, [flowStage, busy, onHoldChange])

  const idleMs = flowStage === "declare" ? DECLARE_IDLE_MS : IDLE_MS
  const armIdle = useCallback(() => {
    if (idle.current) window.clearTimeout(idle.current)
    const fire = () => {
      // A punch still on the wire answers first; the timer waits for it.
      if (inFlight.current) idle.current = window.setTimeout(fire, IDLE_MS)
      else onBack()
    }
    idle.current = window.setTimeout(fire, idleMs)
  }, [onBack, idleMs])

  useEffect(() => {
    armIdle()
    return () => {
      if (idle.current) window.clearTimeout(idle.current)
    }
  }, [armIdle])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || declaring.current || inFlight.current) return
      onBack()
    }
    window.addEventListener("keydown", onKey)
    return () => {
      if (goBack.current) window.clearTimeout(goBack.current)
      window.removeEventListener("keydown", onKey)
    }
  }, [onBack])

  return (
    <div className="flex flex-1 flex-col">
      <PunchFlow
        userId={person.userId}
        name={person.name}
        action={person.state === "in" ? "departure" : "arrival"}
        onDone={onDone}
        onBack={onBack}
        onActivity={armIdle}
        onBusyChange={setBusy}
        onStageChange={setFlowStage}
        padClassName={padClassName}
        onEndingRefusal={() => {
          // The tile behind this panel is out of date or closed: the sentence stays readable, then the tiles return.
          goBack.current = window.setTimeout(onBack, REFUSAL_BACK_MS)
        }}
      />
    </div>
  )
}

function Confirmation({ result }: { result: PunchResultDto }) {
  const arrival = result.action === "arrival"
  return (
    <div className="mx-auto my-auto flex max-w-md flex-col items-center gap-3 py-4 text-center">
      <CheckCircle2 className={cn("size-16", arrival ? "text-success" : "text-warning-ink")} aria-hidden="true" />
      <p className="text-2xl font-semibold">{arrival ? "Arrivée enregistrée" : "Départ enregistré"}</p>
      <p className="text-lg tabular-nums text-muted-foreground">
        {result.at} · Borne {result.terminalName}
      </p>
      <p className="text-base">{result.name}</p>
    </div>
  )
}
