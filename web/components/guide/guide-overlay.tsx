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
/** A list still loading renders no rows for a moment — only after this long is it read as empty. */
const EMPTY_SETTLE_MS = 1500
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

/**
 * Whether the element is entirely on screen — inside the window AND inside every scrolling box around it (a dialog's
 * body). A ring drawn on a control clipped by its dialog is a ring the person cannot see (owner, 2026-11-04).
 */
function fullyVisible(el: Element): boolean {
  const r = el.getBoundingClientRect()
  if (r.top < 0 || r.left < 0 || r.bottom > window.innerHeight || r.right > window.innerWidth) return false
  for (let p = el.parentElement; p; p = p.parentElement) {
    const style = getComputedStyle(p)
    if (!/(auto|scroll|hidden)/.test(style.overflowY + style.overflowX)) continue
    const c = p.getBoundingClientRect()
    if (r.top < c.top - 1 || r.bottom > c.bottom + 1 || r.left < c.left - 1 || r.right > c.right + 1) return false
  }
  return true
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
  onFinish,
  onJump,
}: {
  guide: Guide
  index: number
  onAdvance: () => void
  onEnd: (message?: string) => void
  /** The tour watched to its end early (an empty list: nothing further to show). */
  onFinish: () => void
  /** Go on from another step — an empty list's example opened (`emptyOpens`). */
  onJump: (index: number) => void
}) {
  const step = guide.steps[index]
  const coarse = useMediaQuery("(pointer: coarse)")
  const docked = useMediaQuery("(max-width: 767px)")
  const reduceMotion = useMediaQuery("(prefers-reduced-motion: reduce)")

  const [box, setBox] = useState<Box | null>(null)
  // The step's dialog and its footer — the bubble goes beside the dialog, and never over its buttons.
  const [scopeBox, setScopeBox] = useState<Box | null>(null)
  const [footerBox, setFooterBox] = useState<Box | null>(null)
  const scopeBoxRef = useRef<Box | null>(null)
  const footerBoxRef = useRef<Box | null>(null)
  const [hidden, setHidden] = useState(false)
  // The step's own mark is absent and its `emptyMark` is lit instead (an empty list).
  const [empty, setEmpty] = useState(false)
  const emptyRef = useRef(false)
  const absentSince = useRef<number | null>(null)
  const readViewport = () =>
    typeof window === "undefined" ? { w: 0, h: 0 } : { w: window.innerWidth, h: window.innerHeight }
  const [viewport, setViewport] = useState(readViewport)
  // What the docked bubble really sits on: the bar plus a notched phone's home indicator, not the bar alone.
  const [bottomInset, setBottomInset] = useState(BOTTOM_BAR_PX)
  useEffect(() => setBottomInset(measureBottomInset()), [viewport])
  const viewportRef = useRef(viewport)
  const bubbleRef = useRef<HTMLDivElement>(null)
  const [bubbleH, setBubbleH] = useState(0)

  // Per step (the overlay is keyed by step, so these start fresh each time).
  const pressed = useRef(false)
  const settled = useRef(false)
  const missingSince = useRef<number | null>(null)
  const scopeGoneSince = useRef<number | null>(null)
  // A window that has not appeared yet has not « gone »: it may still be opening (the example fiche loads first).
  const scopeSeen = useRef(false)
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

      let targets = drawn(step.mark)
      if (step.emptyMark) {
        const fallback = targets.length === 0 ? drawn(step.emptyMark) : []
        if (targets.length === 0 && fallback.length > 0) {
          absentSince.current ??= now
          if (now - absentSince.current > EMPTY_SETTLE_MS) targets = fallback
        } else {
          absentSince.current = null
        }
        const nowEmpty = targets.length > 0 && targets === fallback
        if (nowEmpty !== emptyRef.current) {
          emptyRef.current = nowEmpty
          setEmpty(nowEmpty)
        }
      }
      if (step.scope) {
        if (drawn(step.scope).length === 0) {
          if ("savedAndClosed" in next && pressed.current) return finish(onAdvance)
          if (scopeSeen.current) {
            scopeGoneSince.current ??= now
            if (now - scopeGoneSince.current > SCOPE_GONE_MS) return finish(() => onEnd())
          }
        } else {
          scopeSeen.current = true
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
        // Inside a dialog's scrolling body too, not only the window: `scrollIntoView` scrolls every box around it.
        if (!fullyVisible(targets[0])) {
          targets[0].scrollIntoView({ block: "center", inline: "nearest", behavior: reduceMotion ? "auto" : "smooth" })
        }
      }
      if (!same(nextBox, boxRef.current)) {
        boxRef.current = nextBox
        setBox(nextBox)
      }
      const scopeEl = step.scope ? drawn(step.scope)[0] : undefined
      const nextScope = scopeEl ? unionOf([scopeEl]) : null
      const footerEl = scopeEl?.querySelector('[data-slot="dialog-footer"], [data-slot="sheet-footer"]')
      const nextFooter = footerEl && !footerEl.querySelector(`[data-guide="${step.mark}"]`) ? unionOf([footerEl]) : null
      if (!same(nextScope, scopeBoxRef.current)) {
        scopeBoxRef.current = nextScope
        setScopeBox(nextScope)
      }
      if (!same(nextFooter, footerBoxRef.current)) {
        footerBoxRef.current = nextFooter
        setFooterBox(nextFooter)
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

  // Inside a window the bubble sits outside the window's focus trap, so Tab never reaches « Suivant »: → does, and so
  // does Enter on nothing in particular — never from a control that answers those keys itself.
  const nextRef = useRef<() => void>(() => {})
  useEffect(() => {
    if (!("manual" in step.next)) return
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
      const target = event.target instanceof HTMLElement ? event.target : null
      const typing = target?.closest(
        'input, textarea, select, [contenteditable="true"], [role="radio"], [role="slider"], [role="tab"], [role="option"], [role="menuitem"]',
      )
      const interactive = target?.closest("button, a, [role='button']")
      if (event.key === "ArrowRight" ? typing : event.key !== "Enter" || typing || interactive) return
      event.preventDefault()
      nextRef.current()
    }
    document.addEventListener("keydown", onKey)
    return () => document.removeEventListener("keydown", onKey)
  }, [step])

  if (typeof document === "undefined") return null

  const manual = "manual" in step.next
  const last = index === guide.steps.length - 1
  const text = empty && step.emptyText ? step.emptyText : coarse && step.touchText ? step.touchText : step.text

  /** « Suivant »: open what the next step shows (`press`), then move on. An empty list ends the tour instead. */
  const next = () => {
    if (empty) {
      if (!step.emptyOpens) return onFinish()
      window.dispatchEvent(new Event(step.emptyOpens.event))
      return onJump(step.emptyOpens.thenIndex)
    }
    if (step.press) {
      const lit = drawn(step.mark)[0]
      const target =
        step.press === true
          ? lit
          : (Array.from(lit?.querySelectorAll(step.press) ?? []).find((el) => {
              const r = el.getBoundingClientRect()
              return r.width > 0 && r.height > 0
            }) as Element | undefined)
      if (target instanceof HTMLElement) target.click()
    }
    onAdvance()
  }
  nextRef.current = next

  const bubbleStyle = placeBubble({ box, scopeBox, footerBox, viewport, bubbleH, docked, bottomInset })

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
            // A tour: the screen dims around what is being shown — nothing is typed, so nothing needs to stay readable.
            boxShadow: "0 0 0 200vmax rgb(15 23 42 / 0.45)",
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
          {guide.steps.length > 1 && (
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
          )}
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
            {guide.quitLabel ?? "Quitter le guide"}
          </Button>
          {manual && (
            <Button type="button" size="sm" className="min-h-9 coarse:min-h-11" onClick={next} aria-keyshortcuts="ArrowRight">
              {last && guide.continueLabel
                ? guide.continueLabel
                : last || (empty && !step.emptyOpens)
                  ? "Terminer"
                  : "Suivant"}
            </Button>
          )}
        </div>
      </div>
    </div>,
    document.body,
  )
}

const BUBBLE_W = 340
/** A phone's bottom bar alone — the fallback when `--bottom-inset` cannot be measured. */
const BOTTOM_BAR_PX = 56

/** `--bottom-inset` in pixels, read off a throwaway element — `env(safe-area-inset-bottom)` has no JS reader. */
function measureBottomInset(): number {
  const probe = document.createElement("div")
  probe.style.cssText = "position:fixed;visibility:hidden;pointer-events:none;height:var(--bottom-inset,56px)"
  document.body.appendChild(probe)
  const px = probe.getBoundingClientRect().height
  probe.remove()
  return px || BOTTOM_BAR_PX
}

function overlaps(a: Box, b: Box | null, margin = 0): boolean {
  if (!b) return false
  return !(
    a.left + a.width <= b.left - margin ||
    b.left + b.width + margin <= a.left ||
    a.top + a.height <= b.top - margin ||
    b.top + b.height + margin <= a.top
  )
}

/**
 * Where the bubble goes: never over the lit control, never over the buttons of the dialog it guides (« Annuler »
 * must stay reachable), beside the dialog when the screen has the room. On a phone it docks full width at the
 * bottom, above the tab bar — or at the top when the bottom would cover the lit control or the sheet's buttons.
 */
function placeBubble({
  box,
  scopeBox,
  footerBox,
  viewport,
  bubbleH,
  docked,
  bottomInset,
}: {
  box: Box | null
  scopeBox: Box | null
  footerBox: Box | null
  viewport: { w: number; h: number }
  bubbleH: number
  docked: boolean
  bottomInset: number
}): React.CSSProperties {
  const h = bubbleH || 140
  const fits = (c: Box) =>
    c.left >= GAP - 0.5 && c.top >= GAP - 0.5 && c.left + c.width <= viewport.w - GAP + 0.5 && c.top + c.height <= viewport.h - GAP + 0.5
  const clear = (c: Box) => !(box && overlaps(c, box, PAD)) && !overlaps(c, footerBox)

  if (docked) {
    const bottom: Box = { left: GAP, width: viewport.w - 2 * GAP, top: viewport.h - bottomInset - GAP - h, height: h }
    const top: Box = { left: GAP, width: viewport.w - 2 * GAP, top: GAP, height: h }
    const useTop = !clear(bottom) && !(box && overlaps(top, box, PAD))
    return useTop
      ? { top: `calc(${GAP}px + env(safe-area-inset-top, 0px))`, left: GAP, right: GAP }
      : { bottom: `calc(var(--bottom-inset, 0px) + ${GAP}px)`, left: GAP, right: GAP }
  }

  const width = Math.min(BUBBLE_W, viewport.w - 2 * GAP)
  const clampLeft = (left: number) => Math.min(Math.max(left, GAP), Math.max(GAP, viewport.w - width - GAP))
  const clampTop = (top: number) => Math.min(Math.max(top, GAP), Math.max(GAP, viewport.h - h - GAP))
  const anchorTop = box ? clampTop(box.top) : clampTop(viewport.h / 2 - h / 2)

  const candidates: Box[] = []
  if (scopeBox) {
    candidates.push({ left: scopeBox.left + scopeBox.width + GAP, top: anchorTop, width, height: h })
    candidates.push({ left: scopeBox.left - GAP - width, top: anchorTop, width, height: h })
  }
  if (box) {
    candidates.push({ left: clampLeft(box.left), top: box.top + box.height + PAD + GAP, width, height: h })
    candidates.push({ left: clampLeft(box.left), top: box.top - PAD - GAP - h, width, height: h })
  }
  candidates.push(
    { left: GAP, top: viewport.h - h - GAP, width, height: h },
    { left: viewport.w - width - GAP, top: viewport.h - h - GAP, width, height: h },
    { left: GAP, top: GAP, width, height: h },
    { left: viewport.w - width - GAP, top: GAP, width, height: h },
  )
  const chosen = candidates.find((c) => fits(c) && clear(c)) ?? candidates.find(fits) ?? candidates[candidates.length - 1]
  return { top: chosen.top, left: chosen.left, width }
}

