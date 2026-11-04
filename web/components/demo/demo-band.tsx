"use client"

import { useDemo } from "@/lib/demo/demo-context"
import { DEMO_SIGNUP_LABEL } from "@/lib/demo/demo-refusal"

/**
 * The public demo's permanent band (`demo-cabinet` US-4): on every screen, never closable, never printed.
 * A landmark rather than a live region — the shell remounts per navigation, and a live region would re-announce.
 */
export function DemoBand() {
  const { isDemo, signupUrl } = useDemo()
  if (isDemo !== true) return null

  return (
    <aside
      aria-label="Démonstration"
      className="flex shrink-0 flex-wrap items-center justify-center gap-x-3 gap-y-1 border-b bg-primary px-4 pt-[max(0.375rem,env(safe-area-inset-top,0px))] pb-1.5 text-center text-sm text-primary-foreground md:px-6 print:hidden"
    >
      <p className="font-medium">
        <span className="sm:hidden">Démo · rien n&apos;est enregistré</span>
        <span className="hidden sm:inline">Cabinet de démonstration · rien n&apos;est enregistré</span>
      </p>
      <a
        href={signupUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="inline-flex items-center font-semibold underline underline-offset-2 hover:no-underline coarse:min-h-11"
      >
        <span className="sm:hidden">{DEMO_SIGNUP_LABEL}</span>
        <span className="hidden sm:inline">{DEMO_SIGNUP_LABEL} gratuitement</span>
      </a>
    </aside>
  )
}
