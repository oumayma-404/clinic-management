"use client"

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react"
import { usePathname, useRouter } from "next/navigation"
import { toast } from "sonner"

import { GuideOverlay } from "@/components/guide/guide-overlay"
import { useSession } from "@/lib/auth/session"
import { GUIDES, GUIDE_RUNNING_ATTR, type Guide, type GuideKey } from "@/lib/guides"

interface GuideContextValue {
  /** Opens the guide's page and starts it. */
  start: (key: GuideKey) => void
  /** The guide running now, if any. */
  running: GuideKey | null
}

const GuideContext = createContext<GuideContextValue>({ start: () => {}, running: null })

/** « Me guider » — what « Premiers pas » calls. Outside the provider it does nothing. */
export const useGuide = () => useContext(GuideContext)

/** How long the guide's own page may take to open before the guide gives up. */
const ARRIVAL_TIMEOUT_MS = 20_000

interface Run {
  guide: Guide
  index: number
  /** The address has matched the guide's pages once; leaving them after that ends it. */
  arrived: boolean
}

/**
 * The guide state machine (`guided-first-days` F). Mounted once in the root layout, so a guide survives the page
 * changes it asks for (« Ouvrez le dossier d'un patient »). It ends on: the last step done (a « fait » toast), a page
 * outside the guide, the step's dialog closing without a save, « Quitter le guide », the session locking or the
 * person signing out. It never acts on the page — it only watches it.
 */
export function GuideProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter()
  const pathname = usePathname()
  const { user } = useSession()
  const [run, setRun] = useState<Run | null>(null)

  const end = useCallback((message?: string) => {
    setRun(null)
    if (message) toast(message)
  }, [])

  const start = useCallback(
    (key: GuideKey) => {
      const guide = GUIDES[key]
      setRun({ guide, index: 0, arrived: false })
      router.push(guide.startHref)
    },
    [router],
  )

  // The toast stays out of the state updater: StrictMode runs an updater twice, and « fait » would say so twice.
  const advance = useCallback(() => {
    if (!run) return
    if (run.index + 1 < run.guide.steps.length) {
      setRun({ ...run, index: run.index + 1 })
      return
    }
    setRun(null)
    toast.success(run.guide.doneText)
  }, [run])

  // The first match is the arrival; leaving the guide's pages afterwards ends it.
  useEffect(() => {
    if (!run) return
    const inside = run.guide.routes.test(pathname)
    if (inside && !run.arrived) setRun({ ...run, arrived: true })
    else if (!inside && run.arrived) end()
  }, [pathname, run, end])

  useEffect(() => {
    if (!run || run.arrived) return
    const timer = window.setTimeout(() => end(), ARRIVAL_TIMEOUT_MS)
    return () => window.clearTimeout(timer)
  }, [run, end])

  // Signing out ends it — the next person on a shared desk must not inherit a half-done guide.
  useEffect(() => {
    if (!user) setRun(null)
  }, [user])

  // The post-visit question and the week-one card wait while this is on <body>.
  const running = run?.guide.key ?? null
  useEffect(() => {
    if (!running) return
    document.body.setAttribute(GUIDE_RUNNING_ATTR, running)
    return () => document.body.removeAttribute(GUIDE_RUNNING_ATTR)
  }, [running])

  const value = useMemo(() => ({ start, running }), [start, running])

  return (
    <GuideContext.Provider value={value}>
      {children}
      {run?.arrived && (
        <GuideOverlay
          key={`${run.guide.key}:${run.index}`}
          guide={run.guide}
          index={run.index}
          onAdvance={advance}
          onEnd={end}
        />
      )}
    </GuideContext.Provider>
  )
}
