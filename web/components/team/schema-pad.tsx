"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { cn } from "@/lib/utils"

const VIEW = 300
const CENTRES = [50, 150, 250]
const HIT_RADIUS = 46
const dotXY = (dot: number) => ({ x: CENTRES[dot % 3], y: CENTRES[Math.floor(dot / 3)] })

/** The dot between two others on a straight line (row, column or diagonal), if any. */
function middleOf(a: number, b: number): number | null {
  const pa = dotXY(a)
  const pb = dotXY(b)
  const mx = (pa.x + pb.x) / 2
  const my = (pa.y + pb.y) / 2
  const index = CENTRES.indexOf(mx) + 3 * CENTRES.indexOf(my)
  return CENTRES.includes(mx) && CENTRES.includes(my) && index !== a && index !== b ? index : null
}

interface SchemaPadProps {
  /** Called with the dots in drawing order when the finger lifts or Entrée is pressed. */
  onComplete: (pattern: number[]) => void
  disabled?: boolean
  /** Red dots and lines, cleared on the next stroke. */
  error?: boolean
  /** The accessible name, e.g. « Schéma de Salma ». */
  label: string
  /** At a public desk: no lines and no lit dots once drawn — only the dot under the finger shows. */
  hideTrail?: boolean
  autoFocus?: boolean
  className?: string
}

/**
 * The 3 × 3 schéma pad (`pointage-borne` AC-5, AC-16): a finger or a mouse draws through the dots, keys 1–9 draw
 * from a keyboard (Retour arrière undoes, Entrée finishes, Échap clears). Dots are 0–8, row by row, and each is
 * used at most once; a stroke across an unused middle dot takes it too, as a phone's pattern lock does.
 *
 * ⚠️ `touch-action: none` on the pad is what lets a drag draw instead of scrolling the page.
 */
export function SchemaPad({ onComplete, disabled, error, label, hideTrail, autoFocus, className }: SchemaPadProps) {
  const [pattern, setPattern] = useState<number[]>([])
  const [pointer, setPointer] = useState<{ x: number; y: number } | null>(null)
  const [showError, setShowError] = useState(false)
  const drawing = useRef(false)
  const finished = useRef(false)
  const svgRef = useRef<SVGSVGElement>(null)
  const patternRef = useRef<number[]>([])

  useEffect(() => setShowError(Boolean(error)), [error])
  useEffect(() => {
    if (autoFocus) svgRef.current?.focus()
  }, [autoFocus])

  const set = (next: number[]) => {
    patternRef.current = next
    setPattern(next)
  }

  const add = useCallback((dot: number) => {
    const current = patternRef.current
    if (current.includes(dot)) return
    const last = current[current.length - 1]
    const middle = last === undefined ? null : middleOf(last, dot)
    const next = middle !== null && !current.includes(middle) ? [...current, middle, dot] : [...current, dot]
    set(next)
  }, [])

  // The drawn path stays until the next stroke, so a refusal can paint it red — except where it must not be seen.
  const finish = useCallback(() => {
    drawing.current = false
    setPointer(null)
    const done = patternRef.current
    if (done.length > 0) {
      finished.current = true
      onComplete(done)
    }
    if (hideTrail) set([])
  }, [onComplete, hideTrail])

  const startOver = () => {
    if (!finished.current) return
    finished.current = false
    set([])
  }

  const toView = (clientX: number, clientY: number) => {
    const rect = svgRef.current?.getBoundingClientRect()
    if (!rect || rect.width === 0) return null
    return { x: ((clientX - rect.left) / rect.width) * VIEW, y: ((clientY - rect.top) / rect.height) * VIEW }
  }

  const dotAt = (p: { x: number; y: number }) => {
    for (let dot = 0; dot < 9; dot++) {
      const c = dotXY(dot)
      if (Math.hypot(c.x - p.x, c.y - p.y) <= HIT_RADIUS) return dot
    }
    return null
  }

  const onPointerDown = (e: React.PointerEvent<SVGSVGElement>) => {
    if (disabled) return
    const p = toView(e.clientX, e.clientY)
    if (!p) return
    e.currentTarget.setPointerCapture(e.pointerId)
    drawing.current = true
    finished.current = false
    setShowError(false)
    set([])
    const dot = dotAt(p)
    if (dot !== null) add(dot)
    setPointer(p)
  }

  const onPointerMove = (e: React.PointerEvent<SVGSVGElement>) => {
    if (!drawing.current) return
    const p = toView(e.clientX, e.clientY)
    if (!p) return
    const dot = dotAt(p)
    if (dot !== null) add(dot)
    setPointer(p)
  }

  const onKeyDown = (e: React.KeyboardEvent<SVGSVGElement>) => {
    if (disabled) return
    if (/^[1-9]$/.test(e.key)) {
      e.preventDefault()
      setShowError(false)
      startOver()
      add(Number(e.key) - 1)
    } else if (e.key === "Backspace") {
      e.preventDefault()
      set(patternRef.current.slice(0, -1))
    } else if (e.key === "Enter") {
      e.preventDefault()
      finish()
    } else if (e.key === "Escape" && patternRef.current.length > 0) {
      e.preventDefault()
      e.stopPropagation()
      set([])
    }
  }

  const stroke = showError ? "var(--destructive)" : "var(--primary)"
  const points = pattern.map(dotXY)
  const last = points[points.length - 1]
  const lit = (dot: number) => (hideTrail ? pattern[pattern.length - 1] === dot : pattern.includes(dot))
  const count = pattern.length

  return (
    <>
    <svg
      ref={svgRef}
      viewBox={`0 0 ${VIEW} ${VIEW}`}
      role="application"
      aria-label={`${label} : tracez le schéma, ou tapez les chiffres 1 à 9 puis Entrée`}
      aria-disabled={disabled || undefined}
      tabIndex={disabled ? -1 : 0}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={finish}
      onPointerCancel={finish}
      onKeyDown={onKeyDown}
      className={cn(
        "aspect-square w-full max-w-72 touch-none select-none rounded-2xl bg-muted/40 outline-none focus-visible:ring-2 focus-visible:ring-ring",
        disabled && "opacity-50",
        hideTrail && showError && "ring-2 ring-destructive",
        className,
      )}
    >
      {!hideTrail && points.length > 1 && (
        <polyline
          points={points.map((p) => `${p.x},${p.y}`).join(" ")}
          fill="none"
          stroke={stroke}
          strokeWidth={10}
          strokeLinecap="round"
          strokeLinejoin="round"
          opacity={0.55}
        />
      )}
      {!hideTrail && last && pointer && drawing.current && (
        <line x1={last.x} y1={last.y} x2={pointer.x} y2={pointer.y} stroke={stroke} strokeWidth={10} strokeLinecap="round" opacity={0.3} />
      )}
      {Array.from({ length: 9 }, (_, dot) => {
        const c = dotXY(dot)
        const on = lit(dot)
        return (
          <g key={dot}>
            {on && <circle cx={c.x} cy={c.y} r={30} fill={stroke} opacity={0.15} />}
            <circle cx={c.x} cy={c.y} r={on ? 16 : 12} fill={on ? stroke : "var(--muted-foreground)"} opacity={on ? 1 : 0.55} />
          </g>
        )
      })}
    </svg>
    <span className="sr-only" aria-live="polite">
      {count === 0 ? "" : count === 1 ? "1 point" : `${count} points`}
    </span>
    </>
  )
}
