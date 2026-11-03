"use client"

import { useEffect, useLayoutEffect, useState } from "react"

import { FeedbackDialog, StarRow } from "@/components/feedback/feedback-dialog"
import { Button } from "@/components/ui/button"
import { feedbackApi } from "@/lib/api/feedback"
import { showErrorToast } from "@/lib/errors"
import { FEEDBACK_CARD_OPEN_ATTR } from "@/lib/feedback"

/** Not in the first minute of a page load — a person who has just signed in came to do something. */
const SETTLE_MS = 60_000
/** How often the card re-checks whether the screen is free (no dialog, no sheet, no typing). */
const CHECK_MS = 5_000

/**
 * Module-level, because the header — and this card with it — remounts on every screen: when the tab first mounted
 * it (so moving between screens does not restart the minute), whether this showing was already reported, and
 * whether the card is up.
 *
 * <p>⚠️ `latched` is what keeps the card in place across a screen change. As component state it reset on every
 * remount, the body marker went with the old instance, and on the new screen the « Séance terminée » question found
 * the body free before the new card had shown itself again — so the visit question took the screen and the card
 * vanished (QA run 1, F-1).</p>
 */
let firstMountAt: number | null = null
let shownReported = false
let latched = false

/** True while something else owns the screen or the keyboard: a dialog, a sheet, or a field being typed in. */
function screenIsBusy(): boolean {
  const body = document.body
  if (body.hasAttribute("data-sheet-open") || body.hasAttribute("data-scroll-locked")) return true
  const active = document.activeElement as HTMLElement | null
  if (!active) return false
  return active.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(active.tagName)
}

/**
 * « Une semaine avec APEXA » — the week-one question, a corner card that never blocks the screen (guided-first-days
 * E, S11). The server decides WHETHER (7 days, 3 days of real use, twice at most); this decides WHEN: never in the
 * first minute, never over a dialog or sheet, never while somebody types — and it marks the body while it is up so
 * the « Séance terminée » question waits for it rather than stacking on top.
 *
 * <p>A star is already an answer: it opens the rest of the form with that star kept (S12). « Plus tard » brings it
 * back once, a week later; « Ne plus demander » ends it.</p>
 */
export function FeedbackPromptCard() {
  const [eligible, setEligible] = useState(false)
  // Already up on the previous screen → up from this instance's first render, with no wait and no re-check.
  const [visible, setVisible] = useState(() => latched)
  const [dialogRating, setDialogRating] = useState<number | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)

  useEffect(() => {
    if (firstMountAt === null) firstMountAt = Date.now()
    let cancelled = false
    feedbackApi
      .prompt()
      .then((prompt) => {
        if (!cancelled) setEligible(prompt?.show === true)
      })
      .catch(() => {
        // A failed read asks nothing — the question is never worth an error.
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Wait for a free screen, then latch: once up, the card stays until it is answered.
  useEffect(() => {
    if (!eligible || visible) return
    const tryShow = () => {
      if (Date.now() - (firstMountAt ?? Date.now()) < SETTLE_MS) return
      if (screenIsBusy()) return
      latched = true
      setVisible(true)
    }
    tryShow()
    const timer = window.setInterval(tryShow, CHECK_MS)
    return () => window.clearInterval(timer)
  }, [eligible, visible])

  // Counted once per appearance.
  useEffect(() => {
    if (!visible || shownReported) return
    shownReported = true
    void feedbackApi.setPrompt("shown").catch(() => {
      // A lost count only means the card may be shown once more — never worth an error over the page.
      shownReported = false
    })
  }, [visible])

  /*
   * The body marker the « Séance terminée » question reads. A LAYOUT effect, deliberately: on a screen change the old
   * header's cleanup and this one both run before any passive effect of the commit, so the visit question's own check
   * (a passive effect) can never see the body free between the two cards.
   */
  useLayoutEffect(() => {
    if (!visible) return
    document.body.setAttribute(FEEDBACK_CARD_OPEN_ATTR, "")
    return () => document.body.removeAttribute(FEEDBACK_CARD_OPEN_ATTR)
  }, [visible])

  const hide = () => {
    latched = false
    setVisible(false)
    setEligible(false)
  }

  const answer = (rating: number | null) => {
    feedbackApi.settle()
    setDialogRating(rating)
    setDialogOpen(true)
    hide()
  }

  const decline = (action: "later" | "never") => {
    hide()
    // Not saved means the card comes back on the next page — say so rather than let it look ignored.
    void feedbackApi.setPrompt(action).catch(showErrorToast)
  }

  return (
    <>
      {visible && (
        <section
          aria-labelledby="feedback-prompt-title"
          className="fixed inset-x-3 bottom-[calc(var(--bottom-inset)+0.75rem)] z-40 rounded-xl border bg-card p-4 shadow-lg print:hidden md:inset-x-auto md:bottom-4 md:end-4 md:w-80"
        >
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Une semaine avec APEXA</p>
          <h2 id="feedback-prompt-title" className="sr-only">
            Une semaine avec APEXA
          </h2>
          <div className="mt-2">
            <StarRow label="APEXA vous aide-t-il au cabinet ?" value={null} onChange={answer} size="lg" />
          </div>
          <div className="mt-3 flex flex-wrap justify-end gap-2">
            <Button type="button" variant="ghost" size="sm" className="coarse:h-11" onClick={() => decline("later")}>
              Plus tard
            </Button>
            <Button type="button" variant="ghost" size="sm" className="coarse:h-11" onClick={() => decline("never")}>
              Ne plus demander
            </Button>
          </div>
        </section>
      )}
      <FeedbackDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        source="semaine-1"
        initialHelpRating={dialogRating}
      />
    </>
  )
}
