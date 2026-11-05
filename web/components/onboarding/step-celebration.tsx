"use client"

import { toast } from "sonner"

import { GETTING_STARTED_STEPS, type GettingStartedKey } from "@/lib/getting-started"

/**
 * « Premiers pas » celebrated (owner's idea, 2026-11-04): a step that ticks gets confetti over the whole screen, a
 * « 🎉 » message, its own dental joke, the next step and « Étape suivante » back to the list.
 *
 * <p>Called from two places that cannot both fire for one step — the end of a « Me guider » (`guide-provider.tsx`)
 * and the dashboard noticing a step done by ordinary work (`getting-started-card.tsx`) — and
 * `lib/getting-started-celebration.ts` is what keeps it to once.</p>
 */

/** The next step's title and where « Étape suivante » goes; absent on the dashboard itself, where the list is. */
export interface NextStep {
  title: string | null
  onNext?: () => void
}

/** `headline` is the guide's own « fait » sentence when a guide ticked the step; else « C'est fait : <étape> ». */
export function celebrateStep(key: GettingStartedKey, next: NextStep, headline?: string) {
  const step = GETTING_STARTED_STEPS[key]
  launchConfetti()
  toast.success(`🎉 ${headline ?? `C'est fait : ${step.title}`}`, {
    description: (
      <span className="block space-y-1">
        <span className="block">{step.joke}</span>
        {next.title && <span className="block font-medium">Prochaine étape : {next.title}</span>}
      </span>
    ),
    action: next.onNext ? { label: "Étape suivante", onClick: next.onNext } : undefined,
    duration: 12_000,
  })
}

/** « Bienvenue dans votre nouveau cabinet ! » — the window `ReadyCelebrationHost` shows (mounted once, in the provider). */
export const READY_EVENT = "premiers-pas:ready"

export function celebrateReady() {
  window.dispatchEvent(new Event(READY_EVENT))
}

/** The palette, read from the theme so the confetti follows it (and no brand hex is copied a sixth time). */
const CONFETTI_TOKENS = ["--primary", "--chart-1", "--chart-2", "--chart-3", "--chart-4", "--chart-5", "--warning"]
const DURATION_MS = 3200
const PIECES = 180

/** `launchConfetti` in waves — the cabinet's « prêt » moment, which nobody should miss. */
export function launchConfettiWaves(waves = 3, everyMs = 900) {
  for (let i = 0; i < waves; i++) window.setTimeout(() => launchConfetti(), i * everyMs)
}

/**
 * Confetti over the whole app, then gone: one fixed canvas above everything (dialogs included), `pointer-events:
 * none` so nothing under it stops working, removed when the last piece has fallen. Nothing at all under
 * « réduire les animations ».
 */
export function launchConfetti() {
  if (typeof window === "undefined") return
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return

  const root = getComputedStyle(document.documentElement)
  const colors = CONFETTI_TOKENS.map((token) => root.getPropertyValue(token).trim()).filter(Boolean)
  if (colors.length === 0) return

  const canvas = document.createElement("canvas")
  canvas.setAttribute("aria-hidden", "true")
  canvas.style.cssText = "position:fixed;inset:0;width:100vw;height:100dvh;pointer-events:none;z-index:2147483000"
  document.body.appendChild(canvas)
  const ctx = canvas.getContext("2d")
  if (!ctx) {
    canvas.remove()
    return
  }

  const dpr = Math.min(window.devicePixelRatio || 1, 2)
  const width = window.innerWidth
  const height = window.innerHeight
  canvas.width = width * dpr
  canvas.height = height * dpr
  ctx.scale(dpr, dpr)

  // Two bursts from the bottom corners plus a fall from the top — the whole screen, not one spot.
  const pieces = Array.from({ length: PIECES }, (_, i) => {
    const fromTop = i % 3 === 0
    const fromLeft = i % 2 === 0
    const angle = fromLeft ? -Math.PI / 3 - Math.random() * 0.5 : (-2 * Math.PI) / 3 + Math.random() * 0.5
    const speed = 9 + Math.random() * 9
    return {
      x: fromTop ? Math.random() * width : fromLeft ? 0 : width,
      y: fromTop ? -20 - Math.random() * height * 0.3 : height,
      vx: fromTop ? (Math.random() - 0.5) * 3 : Math.cos(angle) * speed,
      vy: fromTop ? 2 + Math.random() * 3 : Math.sin(angle) * speed,
      size: 6 + Math.random() * 6,
      spin: Math.random() * Math.PI,
      spinSpeed: (Math.random() - 0.5) * 0.3,
      color: colors[i % colors.length],
    }
  })

  const start = performance.now()
  const frame = (now: number) => {
    const elapsed = now - start
    ctx.clearRect(0, 0, width, height)
    ctx.globalAlpha = elapsed > DURATION_MS - 600 ? Math.max(0, (DURATION_MS - elapsed) / 600) : 1
    for (const p of pieces) {
      p.vy += 0.25
      p.vx *= 0.99
      p.x += p.vx
      p.y += p.vy
      p.spin += p.spinSpeed
      ctx.save()
      ctx.translate(p.x, p.y)
      ctx.rotate(p.spin)
      ctx.fillStyle = p.color
      ctx.fillRect(-p.size / 2, -p.size / 4, p.size, p.size / 2)
      ctx.restore()
    }
    if (elapsed < DURATION_MS) requestAnimationFrame(frame)
    else canvas.remove()
  }
  requestAnimationFrame(frame)
}
