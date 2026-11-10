"use client"

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react"
import { usePathname, useRouter } from "next/navigation"
import { toast } from "sonner"

import { GuideOverlay } from "@/components/guide/guide-overlay"
import { ReadyCelebrationHost } from "@/components/onboarding/ready-celebration"
import { celebrateReady, celebrateStep } from "@/components/onboarding/step-celebration"
import { dashboardApi } from "@/lib/api/dashboard"
import type { GettingStartedDto } from "@/lib/api/types"
import { useSession } from "@/lib/auth/session"
import { GETTING_STARTED_STEPS, type GettingStartedKey } from "@/lib/getting-started"
import { markCelebrated, markReadyCelebrated, takeNewlyDone } from "@/lib/getting-started-celebration"
import { GUIDES, GUIDE_RUNNING_ATTR, type Guide, type GuideKey } from "@/lib/guides"
import { nextStepPrompt } from "@/lib/guides/next-step"

interface GuideContextValue {
  /** Opens the tour's page and starts it. */
  start: (key: GuideKey) => void
  /**
   * The guided tour's hand-over: on the dashboard, dims around the next open step and offers « C'est parti » / « Plus
   * tard ». After a tour, after a tick by hand, from « Aide » → « Premiers pas », and on a cabinet's first visit.
   * `started` saves a read the caller already has.
   */
  continueTour: (options?: { first?: boolean; started?: GettingStartedDto }) => void
  /** Withdraws the hand-over prompt, if it is up — its mark lives on the card, which « Masquer » just removed. */
  endPrompt: () => void
  /** The guide running now, if any. */
  running: Guide["key"] | null
}

const GuideContext = createContext<GuideContextValue>({
  start: () => {},
  continueTour: () => {},
  endPrompt: () => {},
  running: null,
})

/** « Me guider » — what « Premiers pas » calls. Outside the provider it does nothing. */
export const useGuide = () => useContext(GuideContext)

/** Asks the card to open « Votre téléphone » — the one step whose « tour » is a window on the dashboard itself. */
export const OPEN_PHONE_STEP_EVENT = "premiers-pas:phone"

/** How long the guide's own page may take to open before the guide gives up. */
const ARRIVAL_TIMEOUT_MS = 20_000

interface Run {
  guide: Guide
  index: number
  /** The address has matched the guide's pages once; leaving them after that ends it. */
  arrived: boolean
  /** The hand-over prompt: its « C'est parti » starts the next step rather than ticking anything. */
  onDone?: () => void
}

/**
 * The guided tour (`guided-first-days` F, owner's calls 2026-11-04). Mounted once in the root layout, so a tour
 * survives the page changes it makes. A step's tour finishes on « Terminer »: the step is ticked by hand, the person is
 * brought back to the dashboard, the step is celebrated (confetti, its joke) and the next open step is prompted — or,
 * the last one done, « Bienvenue dans votre nouveau cabinet ! ». A tour ends without a tick on: a page outside it, its
 * window closed, « Quitter le guide », the session locking or the person signing out. It presses only what OPENS a
 * window or a page (a step's `press`), never a save.
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

  /** « C'est parti » on the prompt: the step's own tour — or its window, or its page, for the two that have none. */
  const startStep = useCallback(
    (key: GettingStartedKey) => {
      const meta = GETTING_STARTED_STEPS[key]
      if (meta.guide) start(meta.guide)
      else if (key === "phoneApp") window.dispatchEvent(new Event(OPEN_PHONE_STEP_EVENT))
      else if (meta.href) router.push(meta.href)
    },
    [start, router],
  )

  const continueTour = useCallback(
    async (options?: { first?: boolean; started?: GettingStartedDto }) => {
      let started = options?.started
      if (!started) {
        try {
          started = await dashboardApi.getGettingStarted()
        } catch {
          return
        }
      }
      if (!started.visible) return
      const next = started.steps.find((step) => !step.done)
      if (!next || !(next.key in GETTING_STARTED_STEPS)) return
      const key = next.key as GettingStartedKey
      const percent = Math.round((started.steps.filter((step) => step.done).length / started.steps.length) * 100)
      setRun({
        guide: nextStepPrompt({ title: GETTING_STARTED_STEPS[key].title, percent, first: options?.first }),
        index: 0,
        arrived: false,
        onDone: () => startStep(key),
      })
    },
    [startStep],
  )

  /**
   * « Terminer »: ticked, celebrated, back to the dashboard and on to the next step.
   *
   * <p>⚠️ The tick and the « celebrated » mark land BEFORE the dashboard mounts, so its own read sees the step done
   * and does not celebrate it a second time. ⚠️ A window the tour opened is NOT closed first: its history guard pops
   * an entry on close, and that `back()` would race the push to the dashboard — the navigation unmounts it.</p>
   */
  const finish = useCallback(
    async (guide: Guide) => {
      setRun(null)
      // Not a « Premiers pas » step: nothing to tick, and the tour ends on the page it explained.
      if (guide.standalone) {
        toast.success(guide.doneText)
        return
      }
      const owner = user?.email ?? ""
      const stepKey = guide.key as GettingStartedKey
      await dashboardApi.tickStep(stepKey).catch(() => {
        // A write, not a read: a failure only leaves the step for its circle or its real work — no error over a
        // tour that has just told the person they are done.
      })
      let celebrate: () => void = () => toast.success(guide.doneText)
      let started: GettingStartedDto | null = null
      try {
        started = await dashboardApi.getGettingStarted()
        if (started.completed) {
          // Every step done: this is the moment the cabinet is ready — under its own mark.
          if (owner) markCelebrated(owner, stepKey)
          if (!owner || markReadyCelebrated(owner)) celebrate = celebrateReady
        } else if (started.visible && owner) {
          // Steps that ticked elsewhere since the card's last read are remembered, not celebrated here.
          takeNewlyDone(
            owner,
            started.steps
              .filter((step) => step.done && step.key !== stepKey)
              .map((step) => step.key as GettingStartedKey),
          )
          const ticked = started.steps.some((step) => step.key === stepKey && step.done)
          if (ticked && markCelebrated(owner, stepKey)) {
            celebrate = () => celebrateStep(stepKey, { title: null }, guide.doneText)
          }
        }
      } catch {
        // Unknown: still back to the dashboard, with the plain « fait ».
      }
      router.push("/")
      celebrate()
      if (started?.visible) void continueTour({ started })
    },
    [router, user?.email, continueTour],
  )

  // The toast stays out of the state updater: StrictMode runs an updater twice, and « fait » would say so twice.
  const advance = useCallback(() => {
    if (!run) return
    if (run.index + 1 < run.guide.steps.length) {
      setRun({ ...run, index: run.index + 1 })
      return
    }
    if (run.onDone) {
      setRun(null)
      run.onDone()
      return
    }
    void finish(run.guide)
  }, [run, finish])

  /** An empty list's example opened: the tour goes on from the step that example shows. */
  const jump = useCallback((index: number) => setRun((current) => (current ? { ...current, index } : current)), [])

  /** An empty list: nothing further to show, so the tour counts as watched. */
  const finishNow = useCallback(() => {
    if (run && !run.onDone) void finish(run.guide)
  }, [run, finish])

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

  // Stable, so an effect that lists it in its deps does not re-run each time a guide starts or ends.
  const continueTourVoid = useCallback(
    (options?: { first?: boolean; started?: GettingStartedDto }) => void continueTour(options),
    [continueTour],
  )
  const endPrompt = useCallback(() => setRun((current) => (current?.onDone ? null : current)), [])

  const value = useMemo(
    () => ({ start, running, continueTour: continueTourVoid, endPrompt }),
    [start, running, continueTourVoid, endPrompt],
  )

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
          onFinish={finishNow}
          onJump={jump}
        />
      )}
      <ReadyCelebrationHost />
    </GuideContext.Provider>
  )
}
