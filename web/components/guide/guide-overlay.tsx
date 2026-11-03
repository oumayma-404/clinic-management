"use client"

import { useEffect, useLayoutEffect, useRef, useState } from "react"
import { createPortal } from "react-dom"

import { Button } from "@/components/ui/button"
import { quoteFr } from "@/lib/format"
import { useMediaQuery } from "@/lib/hooks/use-media-query"
import { cn } from "@/lib/utils"
import type { Guide } from "@/lib/guides"

/** How long a step's control may be missing before the guide stops (a page still loading takes a few seconds). */
const MISSING_TIMEOUT_MS = 10_000
/** A dialog briefly gone (its exit animation, a re-render) is not a dialog closed. */
const SCOPE_GONE_MS = 400
const GAP = 12
const PAD = 6

interface Box {
  top: number
  left: number
  width: number
  height: number
}

/** Every element carrying the mark that is actually drawn — a hidden tree (`display: none`) measures 0. */
function drawn(mark: string): Element[] {
  return Array.from(document.querySelectorAll(`[data-guide="${mark}"]`)).filter((el) => {
    const r = el.getBoundingClientRect()
    return r.width > 0 && r.height > 0
  })
}

function unionOf(elements: Element[]): Box | null {
  if (elements.length === 0) return null
  let top = Infinity
  let left = Infinity
  let right = -Infinity
  let bottom = -Infinity
  for (const el of elements) {
    const r = el.getBoundingClientRect()
    top = Math.min(top, r.top)
    left = Math.min(left, r.left)
    right = Math.max(right, r.right)
    bottom = Math.max(bottom, r.bottom)
  }
  return { top, left, width: right - left, height: bottom - top }
}

const same = (a: Box | null, b: Box | null) =>
  a === b ||
  (!!a && !!b && Math.round(a.top) === Math.round(b.top) && Math.round(a.left) === Math.round(b.left) &&
    Math.round(a.width) === Math.round(b.width) && Math.round(a.height) === Math.round(b.height))

/**
 * One step of a guide over the real screen (`guided-first-days` F): the lit control, and a bubble saying what to do.
 *
 * ⚠️ **Drawing only.** The layer is `pointer-events: none` — the person clicks the real control through it, and
 * nothing outside the bubble can be pressed « on the guide ». The bubble is the one interactive piece: it carries
 * `data-guide-bubble`, which every dialog lets through (`ui/floating-notice.ts`), and its `mousedown` is prevented so
 * pressing « Suivant » never takes the focus out of the field being typed in.
 *
 * ⚠️ **Polled, not wired.** Each frame reads the page: is the lit control there, did the next one appear, did the
 * dialog close, is a confirmation up (the guide hides behind it), is the session locked (the guide ends). That keeps
 * the screens free of guide code beyond their `data-guide` marks.
 */
export function GuideOverlay({
  guide,
  index,
  onAdvance,
  onEnd,
}: {
  guide: Guide
  index: number
  onAdvance: () => void
  onEnd: (message?: string) => void
}) {
  const step = guide.steps[index]
  const coarse = useMediaQuery("(pointer: coarse)")
  const docked = useMediaQuery("(max-width: 767px)")
  const reduceMotion = useMediaQuery("(prefers-reduced-motion: reduce)")

  const [box, setBox] = useState<Box | null>(null)
  const [hidden, setHidden] = useState(false)
  const readViewport = () =>
    typeof window === "undefined" ? { w: 0, h: 0 } : { w: window.innerWidth, h: window.innerHeight }
  const [viewport, setViewport] = useState(readViewport)
  const viewportRef = useRef(viewport)
  const bubbleRef = useRef<HTMLDivElement>(null)
  const [bubbleH, setBubbleH] = useState(0)

  // Per step (the overlay is keyed by step, so these start fresh each time).
  const pressed = useRef(false)
  const settled = useRef(false)
  const missingSince = useRef<number | null>(null)
  const scopeGoneSince = useRef<number | null>(null)
  const broughtIntoView = useRef(false)
  const boxRef = useRef<Box | null>(null)
  const hiddenRef = useRef(false)

  // A press on the lit control is what a `savedAndClosed` step waits for. Any OTHER press inside the step's dialog
  // (« Annuler », the ✕, a field after a refusal) withdraws it, and so does Escape: closing then is a cancel.
  useEffect(() => {
    const onClick = (event: MouseEvent) => {
      if (!(event.target instanceof Element)) return
      if (event.target.closest(`[data-guide="${step.mark}"]`)) pressed.current = true
      else if (step.scope && event.target.closest(`[data-guide="${step.scope}"]`)) pressed.current = false
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return
      pressed.current = false
      // With no dialog open, Escape is « Quitter le guide ».
      if (!document.querySelector('[role="dialog"], [role="alertdialog"]')) onEnd()
    }
    document.addEventListener("click", onClick, true)
    document.addEventListener("keydown", onKey, true)
    return () => {
      document.removeEventListener("click", onClick, true)
      document.removeEventListener("keydown", onKey, true)
    }
  }, [step.mark, step.scope, onEnd])

  useEffect(() => {
    let frame = 0
    const finish = (fn: () => void) => {
      if (settled.current) return
      settled.current = true
      fn()
    }

    const tick = () => {
      frame = requestAnimationFrame(tick)
      if (settled.current) return
      const now = performance.now()

      // A locked session covers the app: the guide stops rather than draw over the lock.
      if (document.getElementById("session-lock-gate-title")) return finish(() => onEnd())

      // A confirmation is up (« …dans le passé ? », « existe peut-être déjà ») — step aside until it is answered.
      const confirmUp = !!document.querySelector('[role="alertdialog"]')
      if (confirmUp !== hiddenRef.current) {
        hiddenRef.current = confirmUp
        setHidden(confirmUp)
      }

      const next = step.next
      if ("visible" in next && drawn(next.visible).length > 0) return finish(onAdvance)
      if ("route" in next && next.route.test(window.location.pathname)) return finish(onAdvance)

      const targets = drawn(step.mark)
      if (step.scope) {
        if (drawn(step.scope).length === 0) {
          if ("savedAndClosed" in next && pressed.current) return finish(onAdvance)
          scopeGoneSince.current ??= now
          if (now - scopeGoneSince.current > SCOPE_GONE_MS) return finish(() => onEnd())
        } else {
          scopeGoneSince.current = null
        }
      } else if ("savedAndClosed" in next && pressed.current && targets.length === 0) {
        return finish(onAdvance)
      }

      if (targets.length === 0 && !confirmUp) {
        missingSince.current ??= now
        if (now - missingSince.current > MISSING_TIMEOUT_MS) {
          return finish(() => onEnd(guide.missingText ?? `Le guide ${quoteFr(guide.title)} s'est arrêté.`))
        }
      } else {
        missingSince.current = null
      }

      const nextBox = unionOf(targets)
      if (nextBox && !broughtIntoView.current) {
        broughtIntoView.current = true
        if (nextBox.top < 0 || nextBox.top + nextBox.height > window.innerHeight) {
          targets[0].scrollIntoView({ block: "center", behavior: reduceMotion ? "auto" : "smooth" })
        }
      }
      if (!same(nextBox, boxRef.current)) {
        boxRef.current = nextBox
        setBox(nextBox)
      }
      if (window.innerWidth !== viewportRef.current.w || window.innerHeight !== viewportRef.current.h) {
        viewportRef.current = { w: window.innerWidth, h: window.innerHeight }
        setViewport(viewportRef.current)
      }
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [step, guide, onAdvance, onEnd, reduceMotion])

  useLayoutEffect(() => {
    const el = bubbleRef.current
    if (!el) return
    const observer = new ResizeObserver(() => setBubbleH(el.offsetHeight))
    observer.observe(el)
    setBubbleH(el.offsetHeight)
    return () => observer.disconnect()
  }, [])

  if (typeof document === "undefined") return null

  const manual = "manual" in step.next
  const text = coarse && step.touchText ? step.touchText : step.text

  // Where the bubble goes. On a phone it docks at the bottom, above the tab bar — or at the top when the lit control
  // is down there itself (a dialog's footer, the agenda's « + »), so it never covers what it points at.
  let bubbleStyle: React.CSSProperties
  if (docked || !box) {
    const targetLow = !!box && box.top + box.height > viewport.h - bubbleH - 3 * GAP
    bubbleStyle = targetLow
      ? { top: `calc(${GAP}px + env(safe-area-inset-top, 0px))`, left: GAP, right: GAP }
      : { bottom: `calc(var(--bottom-inset, 0px) + ${GAP}px)`, left: GAP, right: GAP }
    if (!docked) bubbleStyle = { ...bubbleStyle, left: "50%", right: "auto", width: 340, transform: "translateX(-50%)" }
  } else {
    const width = 340
    const below = box.top + box.height + PAD + GAP
    const above = box.top - PAD - GAP - bubbleH
    const top = below + bubbleH <= viewport.h - GAP ? below : above >= GAP ? above : viewport.h - bubbleH - GAP
    const left = Math.min(Math.max(box.left, GAP), Math.max(GAP, viewport.w - width - GAP))
    bubbleStyle = { top, left, width }
  }

  return createPortal(
    <div className="pointer-events-none fixed inset-0 z-[60] print:hidden" aria-hidden={hidden || undefined}>
      {box && !hidden && (
        <div
          aria-hidden
          className="absolute rounded-lg ring-2 ring-primary ring-offset-2 ring-offset-background"
          style={{
            top: box.top - PAD,
            left: box.left - PAD,
            width: box.width + 2 * PAD,
            height: box.height + 2 * PAD,
            // The dim is for « cliquez ici »; while typing (a `manual` step) the whole form stays readable and only
            // the ring says where.
            boxShadow: manual ? undefined : "0 0 0 200vmax rgb(15 23 42 / 0.45)",
          }}
        />
      )}
      <div
        ref={bubbleRef}
        data-guide-bubble=""
        role="status"
        aria-live="polite"
        onMouseDown={(event) => event.preventDefault()}
        className="pointer-events-auto absolute rounded-xl border bg-card p-4 text-card-foreground shadow-lg"
        style={{ ...bubbleStyle, visibility: hidden ? "hidden" : undefined }}
      >
        {/* Dots, not « étape 2 sur 4 »: a bare fraction is read as progress (N31). Each dot is a step — filled once
            done, ringed for this one — and the label counts what is DONE. */}
        <div className="flex items-center justify-between gap-3">
          <p className="min-w-0 text-xs font-semibold text-primary">{guide.title}</p>
          <span
            role="img"
            aria-label={`${index} ${index > 1 ? "étapes faites" : "étape faite"} sur ${guide.steps.length}`}
            className="flex shrink-0 items-center gap-1"
          >
            {guide.steps.map((_, i) => (
              <span
                key={i}
                className={cn(
                  "size-1.5 rounded-full",
                  i < index ? "bg-primary" : i === index ? "bg-primary/40 ring-1 ring-primary" : "bg-muted-foreground/30",
                )}
              />
            ))}
          </span>
        </div>
        <p className="mt-1 text-sm font-medium leading-snug">{text}</p>
        <div className="mt-3 flex flex-wrap items-center justify-end gap-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="min-h-9 coarse:min-h-11"
            onClick={() => onEnd()}
          >
            Quitter le guide
          </Button>
          {manual && (
            <Button type="button" size="sm" className="min-h-9 coarse:min-h-11" onClick={onAdvance}>
              Suivant
            </Button>
          )}
        </div>
      </div>
    </div>,
    document.body,
  )
}
