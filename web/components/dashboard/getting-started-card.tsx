"use client"

import { useEffect, useRef, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import { Check, PlayCircle } from "lucide-react"

import { PhoneAppDialog } from "@/components/dashboard/phone-app-dialog"
import { celebrateReady, celebrateStep } from "@/components/onboarding/step-celebration"
import { OPEN_PHONE_STEP_EVENT, useGuide } from "@/components/guide/guide-provider"
import { Button } from "@/components/ui/button"
import type { GettingStartedDto } from "@/lib/api/types"
import { dashboardApi } from "@/lib/api/dashboard"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { GETTING_STARTED_STEPS, type GettingStartedKey } from "@/lib/getting-started"
import { hasSeenCard, markReadyCelebrated, takeNewlyDone } from "@/lib/getting-started-celebration"
import { ONBOARDING_VIDEOS, onboardingVideoUrl } from "@/lib/onboarding-videos"
import { useSubscription } from "@/lib/subscription/subscription-context"
import { cn } from "@/lib/utils"

/** The one step a person may not tick by hand — mirrors the server's `GettingStartedRules.TickableSteps`. */
const UNTICKABLE_STEP: GettingStartedKey = "securityCode"

/**
 * The page's half of « Premiers pas »: renders the card when it applies and is not hidden, and owns its two
 * toasts — « Votre cabinet est prêt » when the last step ticks during the session, and the five-second « Annuler »
 * after « Masquer ».
 *
 * <p>The read itself stays on the page (`useGettingStarted`), because the page also needs to know whether the
 * card applies — to keep its switch out of « Personnaliser » when it does not.</p>
 */
export function GettingStartedBlock({
  data,
  shown,
  onToggle,
  onReload,
}: {
  data: GettingStartedDto | null
  /** The reader's own customiser choice. */
  shown: boolean
  /** Flips the block's hidden state — « Masquer », and its « Annuler ». */
  onToggle: () => void
  onReload: () => void
}) {
  const wasVisible = useRef(false)
  const { user } = useSession()
  const owner = user?.email ?? ""
  const { continueTour } = useGuide()

  useEffect(() => {
    if (!data) return
    // A new cabinet's very first look at the card starts the guided tour on its first open step.
    if (data.visible && shown && owner && !hasSeenCard(owner)) continueTour({ first: true, started: data })
    // The server hides the card once every step is done, so « was on screen, now hidden » is the moment it got
    // there. (The 30th day turning over mid-session would read the same; that is an hour a month, not a lie.)
    if (wasVisible.current && !data.visible && shown) {
      if (!owner || markReadyCelebrated(owner)) celebrateReady()
    } else if (data.visible && shown && owner) {
      // A step ticked by ordinary work (a patient created from the list, the first sign-in on a phone) is celebrated
      // the next time the card is read — once; the first read for an account only remembers.
      const done = data.steps.filter((step) => step.done).map((step) => step.key as GettingStartedKey)
      const fresh = takeNewlyDone(owner, done)
      if (fresh.length > 0) {
        celebrateStep(fresh[fresh.length - 1], { title: null })
      }
    }
    wasVisible.current = data.visible
  }, [data, shown, owner, continueTour])

  if (!data?.visible || !shown) return null

  const hide = () => {
    onToggle()
    toast("Premiers pas masqués.", {
      description: "Ils restent dans « Aide », en haut de l'écran.",
      duration: 5000,
      action: { label: "Annuler", onClick: onToggle },
    })
  }

  return <GettingStartedCard data={data} onHide={hide} onChanged={onReload} />
}

/**
 * « Premiers pas » — a new cabinet's seven setup steps (guided-first-days B), under the greeting, for the
 * administrator, during the first 30 days.
 *
 * <p>Every step ticks from the cabinet's own data (the server decides), so nothing here is a box to tick by hand
 * and the card cannot claim a cabinet is ready when it is not. The NEXT step wears the filled button; the others
 * are outlined, so the eye knows where to start without reading the list.</p>
 *
 * <p>⚠️ A step's video opens on YouTube, outside the app — never embedded (`frame-src`). On a phone it is a round
 * ▶ named for screen readers; from `sm:` it shows its length.</p>
 *
 * <p>« Masquer » is the page's: it hides the block like « Personnaliser » would, with five seconds to undo.</p>
 */
export function GettingStartedCard({
  data,
  onHide,
  onChanged,
}: {
  data: GettingStartedDto
  onHide: () => void
  /** A tick by hand broadcasts nothing (per-user state), so the card re-reads itself after one. */
  onChanged: () => void
}) {
  const [phoneOpen, setPhoneOpen] = useState(false)
  const [ticking, setTicking] = useState<GettingStartedKey | null>(null)
  const { start: startGuide, continueTour } = useGuide()

  // « C'est parti » on the tour's prompt, when the next step is « Votre téléphone »: its window is here.
  useEffect(() => {
    const open = () => setPhoneOpen(true)
    window.addEventListener(OPEN_PHONE_STEP_EVENT, open)
    return () => window.removeEventListener(OPEN_PHONE_STEP_EVENT, open)
  }, [])

  /**
   * « Fait, ou au moins compris » (owner's call, 2026-11-04): the circle of a step ticks it by hand, and unticks one
   * ticked by hand. The card re-reads, and a step newly done is celebrated by `GettingStartedBlock` like any other.
   */
  const setTicked = async (key: GettingStartedKey, ticked: boolean) => {
    if (ticking) return
    setTicking(key)
    try {
      if (ticked) await dashboardApi.tickStep(key)
      else await dashboardApi.untickStep(key)
      onChanged()
      // A step ticked by hand moves the tour on, like a tour finished.
      if (ticked) continueTour()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setTicking(null)
    }
  }
  const { subscription } = useSubscription()

  const steps = data.steps.filter(
    (step): step is { key: GettingStartedKey; done: boolean; doneByHand?: boolean } => step.key in GETTING_STARTED_STEPS,
  )
  const doneCount = steps.filter((step) => step.done).length
  const nextKey = steps.find((step) => !step.done)?.key
  const trialDays =
    subscription?.state === "Trial" && subscription.daysRemaining !== null ? subscription.daysRemaining : null

  return (
    <section aria-labelledby="getting-started-title" className="rounded-xl border bg-card p-4 shadow-xs sm:p-5">
      {/* No wrap here: the TITLE gives way on a phone, so « Masquer » stays top-right instead of dropping to a line
          of its own under the chip. */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-xs font-semibold uppercase tracking-wide text-primary">Premiers pas</p>
          <h2 id="getting-started-title" className="text-lg font-semibold leading-snug">
            Votre cabinet en {steps.length} étapes
          </h2>
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
            <span>
              {doneCount} sur {steps.length} {doneCount > 1 ? "faites" : "faite"}
            </span>
            {trialDays !== null && (
              <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-foreground">
                Essai gratuit · {trialDays} {trialDays > 1 ? "jours" : "jour"}
              </span>
            )}
          </p>
        </div>
        <Button type="button" variant="ghost" size="sm" className="-me-2 min-h-9 shrink-0 coarse:min-h-11" onClick={onHide}>
          Masquer
        </Button>
      </div>

      {/* The bar says the same as « N sur 7 faite », so it is decoration for assistive technology. */}
      <div aria-hidden className="mt-3 h-1.5 overflow-hidden rounded-full bg-muted">
        <div
          className="h-full rounded-full bg-primary transition-[width] duration-500"
          style={{ width: `${steps.length === 0 ? 0 : (doneCount / steps.length) * 100}%` }}
        />
      </div>

      <ol className="mt-4 divide-y">
        {steps.map((step) => {
          const meta = GETTING_STARTED_STEPS[step.key]
          const video = ONBOARDING_VIDEOS[meta.video]
          const videoUrl = onboardingVideoUrl(video)
          const isNext = step.key === nextKey
          // Done by its data → a plain mark; undone (or ticked by hand) → the tick itself. The security code is never
          // ticked by hand (the server's `GettingStartedRules.TickableSteps` refuses it too).
          const tickable = step.key !== UNTICKABLE_STEP && (!step.done || step.doneByHand === true)

          return (
            <li
              key={step.key}
              // The tour's prompt lights the next open step through this mark (`lib/guides/next-step.ts`).
              data-guide={isNext ? "premiers-pas-next" : undefined}
              className="flex min-h-12 items-center gap-3 py-2 coarse:min-h-14"
            >
              {tickable ? (
                <button
                  type="button"
                  aria-label={step.done ? `Remettre à faire : ${meta.title}` : `Marquer comme fait : ${meta.title}`}
                  disabled={ticking !== null}
                  onClick={() => void setTicked(step.key, !step.done)}
                  className={cn(
                    "group touch-target inline-flex size-7 shrink-0 items-center justify-center rounded-full transition-colors disabled:opacity-60",
                    step.done
                      ? "bg-success-wash text-success hover:bg-success-wash/70"
                      : isNext
                        ? "border-2 border-primary text-primary hover:bg-primary/10"
                        : "border-2 border-muted-foreground/40 text-muted-foreground hover:border-primary hover:text-primary",
                  )}
                >
                  <Check
                    aria-hidden
                    className={cn("size-4", !step.done && "opacity-0 group-hover:opacity-60 group-focus-visible:opacity-60")}
                  />
                </button>
              ) : (
                <span
                  aria-hidden
                  className={cn(
                    "inline-flex size-7 shrink-0 items-center justify-center rounded-full",
                    step.done ? "bg-success-wash text-success" : "border-2 border-muted-foreground/40",
                  )}
                >
                  {step.done && <Check className="size-4" />}
                </span>
              )}

              <span className={cn("min-w-0 flex-1 text-sm", step.done ? "text-muted-foreground" : "font-medium")}>
                {meta.title}
                {step.done && <span className="sr-only"> — fait</span>}
              </span>

              {step.done ? (
                <span aria-hidden className="shrink-0 text-xs font-medium text-success">
                  Fait
                </span>
              ) : (
                <div className="flex shrink-0 items-center gap-2">
                  {videoUrl && (
                    <Button
                      asChild
                      variant="ghost"
                      size="sm"
                      className="size-9 px-0 coarse:size-11 sm:w-auto sm:px-2.5"
                    >
                      <a
                        href={videoUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label={`Vidéo : ${meta.title.toLowerCase()} (${video.length}), s'ouvre sur YouTube`}
                      >
                        <PlayCircle aria-hidden />
                        <span aria-hidden className="hidden tabular-nums sm:inline">
                          {video.length}
                        </span>
                      </a>
                    </Button>
                  )}
                  {/* The two ways to learn a step, side by side: its video, or « Me guider » over the real screen
                      (guided-first-days F). A step with a guide offers it in place of « Commencer », whose page is
                      the guide's first stop anyway. */}
                  {meta.guide ? (
                    <Button
                      type="button"
                      size="sm"
                      variant={isNext ? "default" : "outline"}
                      className="min-h-9 coarse:min-h-11"
                      onClick={() => startGuide(meta.guide!)}
                      aria-label={`Me guider : ${meta.title}`}
                    >
                      Me guider
                    </Button>
                  ) : meta.href ? (
                    <Button
                      asChild
                      size="sm"
                      variant={isNext ? "default" : "outline"}
                      className="min-h-9 coarse:min-h-11"
                    >
                      <Link href={meta.href} aria-label={`Commencer : ${meta.title}`}>
                        Commencer
                      </Link>
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant={isNext ? "default" : "outline"}
                      className="min-h-9 coarse:min-h-11"
                      onClick={() => setPhoneOpen(true)}
                      aria-label={`Installer : ${meta.title}`}
                    >
                      Installer
                    </Button>
                  )}
                </div>
              )}
            </li>
          )
        })}
      </ol>

      <PhoneAppDialog
        open={phoneOpen}
        onOpenChange={(open) => {
          setPhoneOpen(open)
          // Closing the window any way — « Terminé », ✕, Escape — is « vu » (owner's call, 2026-11-04): it ticks the
          // step by hand unless the first phone sign-in already did, then re-reads.
          if (!open) {
            const phone = steps.find((s) => s.key === "phoneApp")
            if (phone && !phone.done) void setTicked("phoneApp", true)
            else onChanged()
          }
        }}
        people={data.phoneApp}
      />
    </section>
  )
}
