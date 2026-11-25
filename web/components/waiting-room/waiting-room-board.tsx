"use client"

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react"
import Link from "next/link"
import {
  Armchair,
  ArrowLeft,
  ArrowRight,
  Check,
  CircleCheck,
  Clock,
  Coffee,
  DoorOpen,
  ExternalLink,
  MoreHorizontal,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import type { WaitingRoomVisitDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import {
  BOARD_LANES,
  LANE_LABEL,
  boardLaneOf,
  boardTime,
  canMoveTo,
  formatWait,
  minutesBetween,
  waitTone,
  type BoardLane,
} from "@/lib/waiting-room"

/**
 * The reception's board (`salle-attente` S2–S4): À venir → En salle d'attente → Au fauteuil → Terminé.
 *
 * <p>Every move is a button or a menu item first and a drag second (`frontend-web.md` § 0 — no capability is
 * reachable only by a gesture). The drag is pointer events on purpose: HTML5 drag-and-drop does not exist on a
 * finger, and the tablet at the desk is where this board is used most. A press-and-hold starts it on touch, so a
 * swipe still scrolls.</p>
 *
 * <p>⚠️ A card's name is not a link, deliberately — the agenda's blocks made the same call: a link inside a drag
 * target fights the gesture. « Ouvrir la fiche » is the first item of every card's menu instead.</p>
 */

export type MoveTarget = BoardLane | "Left"

interface WaitingRoomBoardProps {
  visits: Record<BoardLane, WaitingRoomVisitDto[]>
  nowMs: number
  /** A cabinet de groupe names the dentist on each card. */
  showDentist: boolean
  busyId: string | null
  onMove: (visit: WaitingRoomVisitDto, target: MoveTarget) => void
}

const LANE_DOT: Record<BoardLane, string> = {
  Upcoming: "bg-muted-foreground/50",
  Waiting: "bg-warning",
  InChair: "bg-success",
  Done: "bg-primary",
}

const LANE_EMPTY: Record<BoardLane, { icon: typeof Clock; text: string }> = {
  Upcoming: { icon: CircleCheck, text: "Tout le monde est arrivé" },
  Waiting: { icon: Coffee, text: "Personne n'attend" },
  InChair: { icon: Armchair, text: "Fauteuil libre" },
  Done: { icon: Check, text: "Personne encore" },
}

export function WaitingRoomBoard({ visits, nowMs, showDentist, busyId, onMove }: WaitingRoomBoardProps) {
  const drag = useBoardDrag(onMove)

  return (
    <div className="@container">
      <div className="grid grid-cols-1 gap-3 @lg:grid-cols-2 @5xl:grid-cols-4">
        {BOARD_LANES.map((lane) => {
          const rows = visits[lane]
          const state = drag.laneState(lane)
          return (
            <section
              key={lane}
              data-lane={lane}
              aria-label={`${LANE_LABEL[lane]} — ${rows.length}`}
              className={cn(
                "flex min-h-40 flex-col gap-2 rounded-xl border bg-muted/40 p-2 transition-colors",
                state === "can" && "border-dashed border-primary/60",
                state === "over" && "border-primary bg-primary/10 ring-1 ring-primary",
                state === "no" && "bg-destructive-wash",
              )}
            >
              <h2 className="flex items-center gap-2 px-1 pt-1 text-sm font-semibold">
                <span className={cn("size-2 rounded-full", LANE_DOT[lane])} aria-hidden="true" />
                {LANE_LABEL[lane]}
                <span className="ms-auto rounded-full border bg-card px-2 text-xs font-semibold tabular-nums text-muted-foreground">
                  {rows.length}
                </span>
              </h2>
              {rows.length === 0 ? (
                <LaneEmpty lane={lane} />
              ) : (
                rows.map((visit, index) => (
                  <VisitCard
                    key={visit.appointmentId}
                    visit={visit}
                    index={index}
                    nowMs={nowMs}
                    showDentist={showDentist}
                    busy={busyId === visit.appointmentId}
                    dragging={drag.draggingId === visit.appointmentId}
                    onPointerDown={(event) => drag.onPointerDown(event, visit)}
                    onMove={onMove}
                  />
                ))
              )}
            </section>
          )
        })}
      </div>
    </div>
  )
}

function LaneEmpty({ lane }: { lane: BoardLane }) {
  const { icon: Icon, text } = LANE_EMPTY[lane]
  return (
    <p className="flex flex-1 items-center justify-center gap-2 px-2 py-6 text-sm text-muted-foreground" role="status">
      <Icon className="size-4" aria-hidden="true" />
      {text}
    </p>
  )
}

// ── One card ─────────────────────────────────────────────────────────────────────────────────────────────────

interface VisitCardProps {
  visit: WaitingRoomVisitDto
  index: number
  nowMs: number
  showDentist: boolean
  busy: boolean
  dragging: boolean
  onPointerDown: (event: React.PointerEvent<HTMLElement>) => void
  onMove: (visit: WaitingRoomVisitDto, target: MoveTarget) => void
}

function VisitCard({ visit, index, nowMs, showDentist, busy, dragging, onPointerDown, onMove }: VisitCardProps) {
  const lane = boardLaneOf(visit)
  const left = visit.lane === "Left"
  const name = visit.patientName

  let lead: ReactNode
  let meta: ReactNode
  let action: ReactNode = null

  if (lane === "Upcoming") {
    const lateBy = minutesBetween(visit.appointmentDateTime, nowMs)
    lead = <Initials name={name} />
    meta = (
      <>
        <span className="inline-flex items-center gap-1 font-semibold tabular-nums text-foreground">
          <Clock className="size-3.5" aria-hidden="true" />
          {boardTime(visit.appointmentDateTime)}
        </span>
        {lateBy >= 5 && <Tag tone="warn">en retard de {formatWait(lateBy)}</Tag>}
        {visit.acts && <span className="truncate">{visit.acts}</span>}
      </>
    )
    action = (
      <Button
        size="sm"
        className="gap-1.5 bg-success text-white hover:bg-success/90 coarse:min-h-11"
        disabled={busy}
        onClick={() => onMove(visit, "Waiting")}
        aria-label={`${name} est arrivé·e`}
      >
        <Check className="size-4" aria-hidden="true" />
        Arrivé
      </Button>
    )
  } else if (lane === "Waiting") {
    const waited = minutesBetween(visit.arrivedAtUtc, nowMs)
    lead = (
      <span
        className={cn(
          "flex size-7 shrink-0 items-center justify-center rounded-md text-xs font-bold tabular-nums",
          index === 0 ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground",
        )}
        aria-label={`${index + 1}e dans la file`}
      >
        {visit.position ?? index + 1}
      </span>
    )
    meta = (
      <>
        {visit.isWalkIn ? <Tag tone="warn">Sans RDV</Tag> : <Tag tone="info">RDV {boardTime(visit.appointmentDateTime)}</Tag>}
        {index === 0 && <Tag tone="ok">Suivant</Tag>}
      </>
    )
    action = (
      <>
        <WaitPill minutes={waited} />
        <Button
          size="sm"
          variant="secondary"
          className="gap-1 bg-primary/10 text-primary hover:bg-primary/15 coarse:min-h-11"
          disabled={busy}
          onClick={() => onMove(visit, "InChair")}
          aria-label={`Faire entrer ${name}`}
        >
          Faire entrer
          <ArrowRight className="size-4" aria-hidden="true" />
        </Button>
      </>
    )
  } else if (lane === "InChair") {
    lead = (
      <span className="flex size-7 shrink-0 items-center justify-center rounded-md bg-success text-white">
        <Armchair className="size-4" aria-hidden="true" />
      </span>
    )
    meta = <span>depuis {formatWait(minutesBetween(visit.calledInAtUtc, nowMs))}</span>
    action = (
      <Button
        size="sm"
        variant="outline"
        className="gap-1.5 coarse:min-h-11"
        disabled={busy}
        onClick={() => onMove(visit, "Done")}
        aria-label={`Terminer la visite de ${name}`}
      >
        <Check className="size-4" aria-hidden="true" />
        Terminer
      </Button>
    )
  } else {
    lead = (
      <span className={cn("flex size-7 shrink-0 items-center justify-center", left ? "text-destructive" : "text-success")}>
        {left ? <DoorOpen className="size-4" aria-hidden="true" /> : <CircleCheck className="size-4" aria-hidden="true" />}
      </span>
    )
    meta = left ? (
      <Tag tone="bad">Parti sans être vu{visit.leftAtUtc ? ` · ${boardTime(visit.leftAtUtc)}` : ""}</Tag>
    ) : visit.calledInAtUtc && visit.leftChairAtUtc ? (
      <span className="tabular-nums">
        {boardTime(visit.calledInAtUtc)} – {boardTime(visit.leftChairAtUtc)} ·{" "}
        {formatWait(minutesBetween(visit.calledInAtUtc, Date.parse(visit.leftChairAtUtc)))}
      </span>
    ) : (
      <span>Visite terminée</span>
    )
  }

  return (
    <article
      data-visit={visit.appointmentId}
      onPointerDown={onPointerDown}
      className={cn(
        "relative grid touch-manipulation select-none gap-2 rounded-lg border bg-card p-2.5 shadow-sm transition-shadow",
        "cursor-grab hover:border-foreground/20 hover:shadow",
        lane === "InChair" && "border-success/40 bg-success-wash",
        dragging && "border-dashed opacity-40 shadow-none",
        busy && "opacity-60",
      )}
    >
      <div className="flex min-w-0 items-center gap-2">
        {lead}
        <div className="grid min-w-0 flex-1">
          <span className="truncate text-sm font-semibold">{name}</span>
          <span className="flex min-w-0 flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
            {meta}
            {showDentist && visit.doctorName && <span className="truncate">· Dr {visit.doctorName}</span>}
          </span>
        </div>
        <CardMenu visit={visit} onMove={onMove} />
      </div>
      {action && <div className="flex flex-wrap items-center justify-end gap-2">{action}</div>}
    </article>
  )
}

function CardMenu({ visit, onMove }: { visit: WaitingRoomVisitDto; onMove: VisitCardProps["onMove"] }) {
  const lane = boardLaneOf(visit)
  const items: { label: string; target: MoveTarget; icon: typeof ArrowLeft; destructive?: boolean }[] = []

  if (lane === "Upcoming") {
    items.push({ label: "Faire entrer directement", target: "InChair", icon: Armchair })
  } else if (lane === "Waiting") {
    items.push({ label: "Faire entrer", target: "InChair", icon: Armchair })
    if (!visit.isWalkIn) items.push({ label: "Annuler l'arrivée", target: "Upcoming", icon: ArrowLeft })
    items.push({ label: "Parti sans être vu", target: "Left", icon: DoorOpen, destructive: true })
  } else if (lane === "InChair") {
    items.push({ label: "Remettre en salle d'attente", target: "Waiting", icon: ArrowLeft })
    items.push({ label: "Terminer", target: "Done", icon: Check })
  } else {
    if (visit.lane !== "Left") items.push({ label: "Remettre au fauteuil", target: "InChair", icon: Armchair })
    items.push({ label: "Remettre en salle d'attente", target: "Waiting", icon: ArrowLeft })
    if (!visit.isWalkIn) items.push({ label: "Remettre dans « À venir »", target: "Upcoming", icon: ArrowLeft })
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="size-8 shrink-0 coarse:size-11"
          aria-label={`Plus d'actions pour ${visit.patientName}`}
        >
          <MoreHorizontal className="size-4" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-56">
        <DropdownMenuItem asChild className="coarse:py-3">
          <Link href={`/patients/${visit.patientId}`}>
            <ExternalLink className="size-4" aria-hidden="true" />
            Ouvrir la fiche patient
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        {items.map((item) => (
          <DropdownMenuItem
            key={item.label}
            className={cn("coarse:py-3", item.destructive && "text-destructive focus:text-destructive")}
            onSelect={() => onMove(visit, item.target)}
          >
            <item.icon className="size-4" aria-hidden="true" />
            {item.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

// ── Small pieces ─────────────────────────────────────────────────────────────────────────────────────────────

export function WaitPill({ minutes, className }: { minutes: number; className?: string }) {
  const tone = waitTone(minutes)
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-bold tabular-nums",
        tone === "calm" && "bg-muted text-foreground",
        tone === "long" && "bg-warning-wash text-warning-ink",
        tone === "too-long" && "bg-destructive-wash text-destructive",
        className,
      )}
      aria-label={`Attend depuis ${formatWait(minutes)}`}
    >
      <Clock className="size-3" aria-hidden="true" />
      {formatWait(minutes)}
    </span>
  )
}

function Tag({ tone, children }: { tone: "info" | "warn" | "ok" | "bad"; children: ReactNode }) {
  return (
    <span
      className={cn(
        "whitespace-nowrap rounded px-1.5 text-2xs font-semibold leading-5",
        tone === "info" && "bg-primary/10 text-primary",
        tone === "warn" && "bg-warning-wash text-warning-ink",
        tone === "ok" && "bg-success-wash text-success",
        tone === "bad" && "bg-destructive-wash text-destructive",
      )}
    >
      {children}
    </span>
  )
}

function Initials({ name }: { name: string }) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("")
  return (
    <span
      className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary/10 text-2xs font-bold text-primary"
      aria-hidden="true"
    >
      {initials || "?"}
    </span>
  )
}

// ── Drag and drop ────────────────────────────────────────────────────────────────────────────────────────────

type LaneState = "idle" | "can" | "over" | "no"

/**
 * Jira-style drag (`salle-attente` S4): a faded placeholder stays, a ghost follows the pointer, the lanes the card
 * may go to are outlined, the hovered one lights, and a drop calls the same move its button calls. The ghost is
 * positioned through its own style, never through React state, so a drag costs no render per pointer move.
 */
function useBoardDrag(onMove: WaitingRoomBoardProps["onMove"]) {
  const [draggingId, setDraggingId] = useState<string | null>(null)
  const [allowed, setAllowed] = useState<ReadonlySet<BoardLane>>(new Set())
  const [over, setOver] = useState<BoardLane | null>(null)
  const [refused, setRefused] = useState<BoardLane | null>(null)

  const session = useRef<{
    visit: WaitingRoomVisitDto
    card: HTMLElement
    startX: number
    startY: number
    offsetX: number
    offsetY: number
    touch: boolean
    started: boolean
    timer: ReturnType<typeof setTimeout> | null
    ghost: HTMLElement | null
    over: BoardLane | null
  } | null>(null)

  const place = (x: number, y: number) => {
    const s = session.current
    if (!s?.ghost) return
    s.ghost.style.transform = `translate(${x - s.offsetX}px, ${y - s.offsetY}px) rotate(2deg)`
  }

  const laneAt = (x: number, y: number): BoardLane | null => {
    const element = document.elementFromPoint(x, y)?.closest<HTMLElement>("[data-lane]")
    return (element?.dataset.lane as BoardLane | undefined) ?? null
  }

  const mark = (x: number, y: number) => {
    const s = session.current
    if (!s) return
    const lane = laneAt(x, y)
    const ok = lane !== null && canMoveTo(s.visit, lane)
    s.over = ok ? lane : null
    setOver(ok ? lane : null)
    setRefused(lane !== null && !ok && lane !== boardLaneOf(s.visit) ? lane : null)
  }

  const begin = (x: number, y: number) => {
    const s = session.current
    if (!s || s.started) return
    s.started = true
    const rect = s.card.getBoundingClientRect()
    const ghost = s.card.cloneNode(true) as HTMLElement
    ghost.removeAttribute("data-visit")
    Object.assign(ghost.style, {
      position: "fixed",
      left: "0px",
      top: "0px",
      width: `${rect.width}px`,
      zIndex: "100",
      pointerEvents: "none",
      boxShadow: "0 22px 44px -14px rgb(0 0 0 / 0.4)",
      opacity: "0.97",
    })
    document.body.appendChild(ghost)
    s.ghost = ghost
    document.body.style.userSelect = "none"
    document.body.style.cursor = "grabbing"
    setDraggingId(s.visit.appointmentId)
    setAllowed(new Set(BOARD_LANES.filter((lane) => canMoveTo(s.visit, lane))))
    if (s.touch) navigator.vibrate?.(10)
    place(x, y)
    mark(x, y)
  }

  const end = useCallback(
    (drop: boolean) => {
      const s = session.current
      session.current = null
      if (!s) return
      if (s.timer) clearTimeout(s.timer)
      if (!s.started) return
      s.ghost?.remove()
      document.body.style.userSelect = ""
      document.body.style.cursor = ""
      setDraggingId(null)
      setAllowed(new Set())
      setOver(null)
      setRefused(null)
      if (drop && s.over) onMove(s.visit, s.over)
    },
    [onMove],
  )

  useEffect(() => {
    const move = (event: PointerEvent) => {
      const s = session.current
      if (!s) return
      const distance = Math.hypot(event.clientX - s.startX, event.clientY - s.startY)
      if (!s.started) {
        if (s.touch) {
          // A finger that moves before the hold is a scroll, not a drag.
          if (distance > 8) end(false)
          return
        }
        if (distance < 5) return
        begin(event.clientX, event.clientY)
      }
      place(event.clientX, event.clientY)
      mark(event.clientX, event.clientY)
    }
    const up = () => end(true)
    const cancel = () => end(false)
    const touchMove = (event: TouchEvent) => {
      if (session.current?.started) event.preventDefault()
    }
    const key = (event: KeyboardEvent) => {
      if (event.key === "Escape") end(false)
    }
    window.addEventListener("pointermove", move)
    window.addEventListener("pointerup", up)
    window.addEventListener("pointercancel", cancel)
    window.addEventListener("touchmove", touchMove, { passive: false })
    window.addEventListener("keydown", key)
    return () => {
      window.removeEventListener("pointermove", move)
      window.removeEventListener("pointerup", up)
      window.removeEventListener("pointercancel", cancel)
      window.removeEventListener("touchmove", touchMove)
      window.removeEventListener("keydown", key)
    }
    // `begin`/`mark`/`place` read only the ref; `end` is the one dependency that can change.
  }, [end])

  const onPointerDown = (event: React.PointerEvent<HTMLElement>, visit: WaitingRoomVisitDto) => {
    if (event.button > 0) return
    if ((event.target as HTMLElement).closest("button, a, input, [role='menu']")) return
    const card = event.currentTarget
    const rect = card.getBoundingClientRect()
    const touch = event.pointerType !== "mouse"
    session.current = {
      visit,
      card,
      startX: event.clientX,
      startY: event.clientY,
      offsetX: event.clientX - rect.left,
      offsetY: event.clientY - rect.top,
      touch,
      started: false,
      timer: null,
      ghost: null,
      over: null,
    }
    if (touch) {
      const { clientX, clientY } = event
      session.current.timer = setTimeout(() => begin(clientX, clientY), 280)
    }
  }

  const laneState = (lane: BoardLane): LaneState => {
    if (!draggingId) return "idle"
    if (over === lane) return "over"
    if (refused === lane) return "no"
    return allowed.has(lane) ? "can" : "idle"
  }

  return { draggingId, onPointerDown, laneState }
}
