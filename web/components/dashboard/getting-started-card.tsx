"use client"

import { useEffect, useRef, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import { Check, PlayCircle } from "lucide-react"

import { PhoneAppDialog } from "@/components/dashboard/phone-app-dialog"
import { Button } from "@/components/ui/button"
import type { GettingStartedDto } from "@/lib/api/types"
import { GETTING_STARTED_STEPS, type GettingStartedKey } from "@/lib/getting-started"
import { ONBOARDING_VIDEOS, onboardingVideoUrl } from "@/lib/onboarding-videos"
import { useSubscription } from "@/lib/subscription/subscription-context"
import { cn } from "@/lib/utils"

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

  useEffect(() => {
    if (!data) return
    // The server hides the card once every step is done, so « was on screen, now hidden » is the moment it got
    // there. (The 30th day turning over mid-session would read the same; that is an hour a month, not a lie.)
    if (wasVisible.current && !data.visible && shown) {
      toast.success("Votre cabinet est prêt.", { description: "Les premiers pas sont faits." })
    }
    wasVisible.current = data.visible
  }, [data, shown])

  if (!data?.visible || !shown) return null

  const hide = () => {
    onToggle()
    toast("Premiers pas masqués.", {
      description: "« Personnaliser » les remet.",
      duration: 5000,
      action: { label: "Annuler", onClick: onToggle },
    })
  }

  return <GettingStartedCard data={data} onHide={hide} onPhoneDialogClosed={onReload} />
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
  onPhoneDialogClosed,
}: {
  data: GettingStartedDto
  onHide: () => void
  /** The phone sign-in broadcasts nothing, so closing the window is the moment to re-read. */
  onPhoneDialogClosed: () => void
}) {
  const [phoneOpen, setPhoneOpen] = useState(false)
  const { subscription } = useSubscription()

  const steps = data.steps.filter((step): step is { key: GettingStartedKey; done: boolean } =>
    step.key in GETTING_STARTED_STEPS,
  )
  const doneCount = steps.filter((step) => step.done).length
  const nextKey = steps.find((step) => !step.done)?.key
  const trialDays =
    subscription?.state === "Trial" && subscription.daysRemaining !== null ? subscription.daysRemaining : null

  return (
    <section aria-labelledby="getting-started-title" className="rounded-xl border bg-card p-4 shadow-xs sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0 space-y-1">
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
        <Button type="button" variant="ghost" size="sm" className="min-h-9 coarse:min-h-11" onClick={onHide}>
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
        {steps.map((step, index) => {
          const meta = GETTING_STARTED_STEPS[step.key]
          const video = ONBOARDING_VIDEOS[meta.video]
          const videoUrl = onboardingVideoUrl(video)
          const isNext = step.key === nextKey

          return (
            <li key={step.key} className="flex min-h-12 items-center gap-3 py-2 coarse:min-h-14">
              <span
                aria-hidden
                className={cn(
                  "inline-flex size-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold",
                  step.done
                    ? "bg-success-wash text-success"
                    : isNext
                      ? "bg-primary text-primary-foreground"
                      : "border text-muted-foreground",
                )}
              >
                {step.done ? <Check className="size-4" /> : index + 1}
              </span>

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
                  {meta.href ? (
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
          if (!open) onPhoneDialogClosed()
        }}
        people={data.phoneApp}
      />
    </section>
  )
}
