"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * What the app shows while a screen or a panel is still waiting — the Apexa mark inside a spinning ring, over a
 * line of dental small-talk.
 *
 * <p>It replaces the bare « Chargement… » paragraph that stood on two dozen surfaces. Deliberately NOT a
 * replacement for the skeleton rows in the tables: a skeleton previews the SHAPE that is coming, which is more
 * information than a spinner, and swapping one in would make the table jump when the data lands. This is for the
 * waits that had nothing but a sentence.</p>
 *
 * <p>⚠️ <b>Not for a button either.</b> A submit spends 200 ms in flight and the control already has
 * <c>Loader2</c>; a joke that flashes for a fifth of a second is noise, and the same joke on the tenth save of
 * the morning is worse than none.</p>
 */
function AppLoader({
  label,
  className,
}: {
  /** What is loading, when naming it helps — « Chargement du catalogue… ». The quip stays underneath. */
  label?: string
  className?: string
}) {
  const quip = useQuip()

  return (
    <div
      role="status"
      className={cn("relative flex flex-col items-center justify-center gap-3 px-4 py-10 text-center", className)}
    >
      <span className="relative flex size-12 items-center justify-center">
        {/* `animate-spin` and nothing else: globals.css deliberately keeps THIS animation alive under
            `prefers-reduced-motion`, because it is the only signal that a request is still in flight. */}
        <span className="absolute inset-0 animate-spin rounded-full border-2 border-primary/20 border-t-primary" />
        <ApexaMark className="size-5 text-primary" />
      </span>

      {label && <p className="text-sm font-medium text-foreground">{label}</p>}
      <p className="max-w-xs text-xs text-muted-foreground" aria-hidden="true">
        {quip}
      </p>
      {!label && <span className="sr-only">Chargement…</span>}
    </div>
  )
}

/**
 * One line, held for the life of the wait.
 *
 * <p>⚠️ Picked in an effect, never in the initial render. `Math.random()` during render is a hydration mismatch:
 * the server picks one line, the client picks another, and React 19 discards the whole subtree with a console
 * error. The first line is the server's answer and the client swaps it on mount.</p>
 *
 * <p>It does not rotate on a timer either — a message that changes under you is read twice and finished neither
 * time.</p>
 */
function useQuip() {
  const [quip, setQuip] = React.useState(QUIPS[0])
  React.useEffect(() => {
    setQuip(QUIPS[Math.floor(Math.random() * QUIPS.length)])
  }, [])
  return quip
}

const QUIPS = [
  "Un instant, on passe le fil dentaire…",
  "On détartre la base de données…",
  "Deux secondes, on stérilise les instruments…",
  "On règle le fauteuil…",
  "Ouvrez grand… c'est presque prêt.",
  "On polit les derniers chiffres…",
  "On prend l'empreinte…",
  "Rincez, crachez — on arrive.",
  "On souffle un petit coup d'air…",
  "Encore un coup de brossette…",
]

/**
 * The Apexa « A » from `public/icon.svg`, inline so it takes `currentColor` and follows the theme.
 *
 * <p>The public file paints a brand gradient and flips to white under `prefers-color-scheme: dark` — which is the
 * OS setting, not this app's theme toggle, so an `<img>` here would stay dark blue on a manually-darkened page.
 * One path and `currentColor` answers both.</p>
 */
function ApexaMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 512 512" className={className} fill="currentColor" aria-hidden="true">
      <g transform="translate(-46.08 -46.08) scale(1.18)">
        <path d="M 126.15 373.14 L 256.00 79.93 L 385.85 373.14 L 348.36 389.74 L 256.00 181.20 L 163.65 389.74 Z" />
      </g>
    </svg>
  )
}

export { AppLoader }
