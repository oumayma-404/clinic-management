"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useRouter } from "next/navigation"
import { ClipboardPlus } from "lucide-react"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { toast } from "sonner"
import { useMediaQuery } from "@/lib/hooks/use-media-query"
import { useSession } from "@/lib/auth/session"
import { notificationsApi } from "@/lib/api/notifications"
import { appointmentsApi } from "@/lib/api/appointments"
import type { PendingReviewDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

// How often to poll for due reviews (deferred-visibility means one can become due while the app is open).
const POLL_INTERVAL_MS = 60_000
// "Remind me later" is a client-side, per-browser snooze — the notification stays unread in the panel.
//
// ⚠️ **Until the end of the local day, not for an hour, and it covers the WHOLE QUEUE.** Both halves of that
// were wrong in a way that only shows up on a real practice's data. An hour meant a full round of prompts came
// back the same afternoon; snoozing one id meant « Plus tard » answered a question nobody asked — the dentist
// is saying « not now », never « not this patient » — so the next poll simply promoted the next pending visit.
// On a cabinet with 23 séances awaiting closure that is one interruption a minute until the queue is exhausted,
// then the whole queue again an hour later. Measured on the dev database, 2026-09-02.
const SNOOZE_STORAGE_KEY = "clinic:pvr-snooze"

/** Midnight tonight, local. The reminder is never urgent, so tomorrow is soon enough to ask again. */
function endOfLocalDayMs(): number {
  const d = new Date()
  d.setHours(23, 59, 59, 999)
  return d.getTime()
}

type SnoozeMap = Record<string, number>

function loadSnooze(): SnoozeMap {
  if (typeof window === "undefined") return {}
  try {
    const raw = window.localStorage.getItem(SNOOZE_STORAGE_KEY)
    return raw ? (JSON.parse(raw) as SnoozeMap) : {}
  } catch {
    return {}
  }
}

function saveSnooze(map: SnoozeMap): void {
  if (typeof window === "undefined") return
  try {
    window.localStorage.setItem(SNOOZE_STORAGE_KEY, JSON.stringify(map))
  } catch {
    // Storage full/unavailable — snooze is best-effort; worst case the popup reappears next poll.
  }
}

/**
 * When a patient's appointment has ended, this modal prompts the responsible staff (the linked doctor, or
 * everyone) to record what happened. "Ajouter le dossier médical" deep-links to record creation for that
 * visit; "Plus tard" snoozes it client-side without marking it read. Mounted once in the dashboard header,
 * so it is present on every authenticated page.
 *
 * Three surfaces by device, not one scaled three ways: a **dialog** with a mouse, a **toast** on a tablet, and on
 * a **phone nothing at all** — there the header bell is the whole prompt. It can be nothing on a phone because it
 * never was the only channel: the review is a real `StaffNotification` (`NotificationCategory.PostVisitReview`),
 * so the bell already lists it and its row deep-links to the same destination the button here does.
 *
 * ⚠️ **Every way of dismissing it — « Plus tard », the ✕, Escape, a click outside — goes through one
 * `handleLater`, and the dialog's `open` is gated on a local `dismissed` flag rather than on the snooze map
 * alone.** Visibility used to be derived purely from the snooze, so closing depended on a five-step chain
 * (click → onOpenChange → snooze → setSnoozed → the `active` memo → `open`); if any step did not land, the ✕
 * did nothing and the prompt could not be closed at all. Dismissal is a UI fact and now has UI state.
 */
export function PostVisitReviewPopup() {
  const { user } = useSession()
  const router = useRouter()

  const [reviews, setReviews] = useState<PendingReviewDto[]>([])
  const [snoozed, setSnoozed] = useState<SnoozeMap>({})
  const [now, setNow] = useState(0)
  // True while the appointment's patient is being resolved, so the button can't fire twice.
  const [resolving, setResolving] = useState(false)
  /**
   * "The user has dismissed the prompt" — held separately from {@link snoozed} on purpose.
   *
   * Visibility used to be derived *only* from the snooze map, which made closing the dialog the last link of a
   * five-step chain: click → onOpenChange → snooze → setSnoozed → the `active` memo recomputes to null → `open`
   * flips false. Any break in that chain (a throwing `localStorage`, a stale `active` in the callback, an
   * unexpected re-render order) left the dialog with **no way to close itself** — pressing ✕ did nothing at all,
   * which is exactly the reported bug. Dismissal is a UI fact, so it gets UI state, and the persisted snooze
   * becomes a best-effort extra rather than the thing the close button depends on.
   *
   * Cleared by {@link refetch} so a *different* visit can still prompt on the next poll — the popup is suppressed
   * until new data arrives, never permanently.
   */
  const [dismissed, setDismissed] = useState(false)
  /*
   * A finger, not a width (AC-27). The same rule P2 settled: anything about *space* keys on a breakpoint,
   * anything about *fingers* keys on `(pointer: coarse)` — and a dentist's tablet in landscape is 1180 px, so
   * a width test would have left the modal exactly where it hurts most.
   */
  const isCoarse = useMediaQuery("(pointer: coarse)")
  /**
   * On a phone this component prompts **not at all** — the review is a `StaffNotification` like any other, so it
   * is already in the header bell, with a row that deep-links to the same add-record destination this would.
   *
   * ⚠️ This is a *width* test where the one above is a *pointer* test, and both are right. What makes a modal
   * wrong on a tablet is the finger; what makes any unsolicited prompt wrong on a phone is that there is one
   * screen, and an interruption on it is the whole screen — a reminder that is never urgent does not get to
   * spend that, when a badge on the bell says the same thing and waits. A tablet keeps the toast: it has the
   * room to show a prompt beside what the user is doing.
   */
  const isPhone = useMediaQuery("(max-width: 767px)")

  /*
   * ⚠️ **The guard below already existed for the toast and not for the dialog, and that is the whole defect.**
   * The toast's own note states it as a hard constraint — « It must not appear over an open dialog or sheet »
   * — and reads `data-sheet-open` / `data-scroll-locked` off the body to honour it. The `Dialog` path, which is
   * what a mouse gets, honoured nothing: it opened *on top of* whatever was already open. Measured on
   * 2026-09-02 by opening « Nouveau rendez-vous » and being interrupted three times, the prompt covering the
   * two required fields of the form underneath.
   *
   * ⚠️ **It LATCHES, and getting that wrong wedges the whole app.** `data-scroll-locked` is set by Radix for
   * *any* modal dialog — including this one. So a guard written as `open={… && !bodyBusy}` is self-referential:
   * the prompt opens, sets the lock, sees the lock, closes, and the overlay is left in the DOM at
   * `data-state="closed"` intercepting every pointer event on the page. The app looks alive and answers no
   * clicks. That was measured too, on the first attempt at this fix.
   *
   * So the body is consulted **only to decide whether to open**, never to stay open. `busyTick` exists to make
   * that decision re-run when somebody else's dialog closes, which a render-time read could not do.
   */
  const [mayPrompt, setMayPrompt] = useState(false)
  const [busyTick, setBusyTick] = useState(0)

  useEffect(() => {
    const bump = () => setBusyTick((n) => n + 1)
    const observer = new MutationObserver(bump)
    observer.observe(document.body, { attributes: true, attributeFilter: ["data-sheet-open", "data-scroll-locked"] })
    return () => observer.disconnect()
  }, [])

  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  // Hydrate the snooze map from storage on mount (client-only).
  useEffect(() => {
    setSnoozed(loadSnooze())
    setNow(Date.now())
  }, [])

  /*
   * ⚠️ `isPhone` is gated **here**, not at each call site, so the mount fetch, the 60-second interval and the
   * realtime handler all stop together. A phone renders no prompt, and a component that renders nothing has no
   * business asking the server for pending reviews once a minute on all 24 routes — the bell does its own
   * fetching, and this one would be paid for on mobile data to display nothing.
   */
  const refetch = useCallback(async () => {
    if (!user || isPhone) return
    try {
      const list = await notificationsApi.pendingReviews()
      if (mountedRef.current) {
        setReviews(list)
        setNow(Date.now())
        /*
         * ⚠️ **A poll must NOT re-arm a prompt the user has dismissed, and this used to `setDismissed(false)`
         * on every one.** The reasoning — « new data, so let a *different* pending visit prompt » — holds only
         * when there is usually nothing pending. With a queue there is ALWAYS a different one, so the
         * dismissal was lifted every 60 seconds and the prompt returned every 60 seconds, for as many séances
         * as were waiting. The snooze could not hold it back either: it was written for one id.
         *
         * « Plus tard » now means not until tomorrow, for all of them, and it is the snooze that decides when
         * to ask again — not the poll clock. The poll's job is to keep `reviews` fresh for the moment the
         * snooze lapses, which it still does.
         */
      }
    } catch {
      // Best-effort — a failed poll must never surface an error over the app.
    }
  }, [user, isPhone])

  // Poll on mount + on an interval while authenticated (never on a phone — nothing consumes the result).
  useEffect(() => {
    if (!user || isPhone) return
    void refetch()
    const id = window.setInterval(() => void refetch(), POLL_INTERVAL_MS)
    return () => window.clearInterval(id)
  }, [user, isPhone, refetch])

  // A generation/removal broadcast (e.g. a review became due or was fulfilled) refetches promptly.
  useClinicRealtime(RealtimeResource.Notifications, () => {
    void refetch()
  })

  const active = useMemo(
    () => reviews.find((r) => !snoozed[r.id] || snoozed[r.id] <= now) ?? null,
    [reviews, snoozed, now],
  )

  /**
   * Snoozes every review currently known to be pending, until the end of the local day.
   *
   * ⚠️ Takes the list rather than one id, because « not now » is a statement about the moment and not about a
   * patient. Snoozing the single active row is what let the next poll promote the next séance a minute later.
   */
  const snoozeAll = useCallback(() => {
    setSnoozed((prev) => {
      const nowMs = Date.now()
      const until = endOfLocalDayMs()
      const next: SnoozeMap = {}
      // Prune expired entries while writing so the persisted map can't grow unbounded on a long-lived
      // browser (expired entries are ignored on read anyway).
      for (const [key, prevUntil] of Object.entries(prev)) {
        if (prevUntil > nowMs) next[key] = prevUntil
      }
      for (const r of reviews) next[r.id] = until
      saveSnooze(next)
      return next
    })
  }, [reviews])

  const handleAddRecord = useCallback(() => {
    // A review with no appointment can't be fulfilled here (saving a record marks *that* appointment
    // Completed) — don't snooze-and-navigate one that would just return forever. In practice a
    // PostVisitReview is always generated with an appointmentId, so this guard is defensive.
    if (!active?.appointmentId || resolving) return
    const appointmentId = active.appointmentId

    // Go to the patient's add-record modal, the same destination as the notification-panel row. This used to
    // push `/documents`, which dropped the user on the template gallery — a different task from the one the
    // button names, and one that never closes the review.
    //
    // PendingReviewDto carries no patientId, so it is resolved from the appointment (mirrors the bell).
    setResolving(true)
    void (async () => {
      try {
        const appointment = await appointmentsApi.get(appointmentId)
        const patientId = appointment.patientId
        // Snooze only once the destination is known to exist. Snoozing first — as this did — would hide the
        // prompt for an hour on a failed lookup, with the record still unwritten.
        if (!patientId) return
        // Close before navigating, for the same reason as handleLater: the dialog must not depend on the snooze
        // to disappear, or it lingers over the page transition.
        setDismissed(true)
        snoozeAll()
        router.push(`/patients/${patientId}?addRecord=1&appointmentId=${encodeURIComponent(appointmentId)}`)
      } catch {
        // Keep it pending rather than navigate to a dead page; the poll will offer it again.
      } finally {
        setResolving(false)
      }
    })()
  }, [active, resolving, snoozeAll, router])

  /*
   * The latch. Closed → consult the body and open only if nothing else is on screen. Open → leave it alone.
   * Reset whenever there is nothing to prompt about, so the next due review gets a fresh decision.
   */
  useEffect(() => {
    if (active === null || dismissed) {
      setMayPrompt(false)
      return
    }
    if (mayPrompt) return
    const somethingElseIsOpen =
      document.body.hasAttribute("data-sheet-open") || document.body.hasAttribute("data-scroll-locked")
    if (!somethingElseIsOpen) setMayPrompt(true)
  }, [active, dismissed, mayPrompt, busyTick])

  /**
   * The one dismissal path — « Plus tard », the ✕, Escape and a click outside all land here, so every way of
   * saying "not now" behaves identically (the ✕ used to depend on the snooze taking effect; now it cannot fail).
   *
   * Order matters: close first, snooze second. The close is unconditional local state, so a snooze that throws
   * still leaves a closed dialog.
   */
  const handleLater = useCallback(() => {
    setDismissed(true)
    snoozeAll()
  }, [snoozeAll])

  /*
   * AC-27 — on a coarse pointer **that has the room for it** (a tablet) this is a **toast with an action, not a
   * sheet**. On a phone there is no prompt at all; see `isPhone`.
   *
   * It is mounted in the header, so it fires on all 24 routes and re-polls every 60 s. As a modal on a touch
   * device that means the app can seize the whole screen while the user is mid-task, on a *reminder* — the one
   * notification class that is never urgent. A toast says the same thing without taking the screen.
   *
   * ⚠️ Three constraints this has to respect, all of them already load-bearing above:
   *
   * • **Every dismissal must still funnel through `handleLater`.** Sonner's own swipe-away and timeout call
   *   `onDismiss`/`onAutoClose`, which are wired to it — without that the snooze is never written and the
   *   prompt returns on the very next poll, which is the defect the local `dismissed` flag was added to fix.
   * • **It must not appear over an open dialog or sheet.** `data-sheet-open` is the body flag P2 already
   *   maintains for the bottom bar, and Radix sets `data-scroll-locked` for a modal dialog — reading both
   *   costs nothing and needs no new state.
   * • **One toast per review.** Sonner de-dupes on `id`, so re-firing with the same id updates rather than
   *   stacks, which the 60-second poll would otherwise do.
   */
  useEffect(() => {
    // `isPhone` is redundant while `refetch` keeps `reviews` empty there — and stated anyway, because a guard
    // that depends on another guard's side effect is the kind of thing a later change quietly removes.
    // `mayPrompt` is the same latch the dialog uses — one source for « nothing else was on screen when we
    // decided to speak », so the two paths cannot drift apart again. A toast sets no scroll lock, so it was
    // never at risk of the self-reference the dialog was; sharing the latch is for the drift, not the race.
    if (isPhone || !isCoarse || active === null || dismissed || !mayPrompt) return

    toast(active.title ?? "Compte rendu de visite", {
      id: `pvr-${active.id}`,
      description: active.message ?? "La visite est terminée. Ajoutez le dossier médical du patient.",
      duration: 30_000,
      icon: <ClipboardPlus className="h-5 w-5 text-primary" />,
      action: { label: "Ajouter", onClick: handleAddRecord },
      onDismiss: handleLater,
      onAutoClose: handleLater,
    })
  }, [isPhone, isCoarse, active, dismissed, mayPrompt, handleAddRecord, handleLater])

  // On a phone the header bell *is* the prompt; on a tablet the toast above is. Either way the dialog would be
  // a second copy of a reminder the user has already been given.
  if (isPhone || isCoarse) return null

  return (
    <Dialog
      open={active !== null && !dismissed && mayPrompt}
      onOpenChange={(next) => {
        if (!next) handleLater()
      }}
    >
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <div className="mb-2 flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary">
            <ClipboardPlus className="h-5 w-5" />
          </div>
          <DialogTitle>{active?.title ?? "Compte rendu de visite"}</DialogTitle>
          <DialogDescription>
            {active?.message ?? "La visite est terminée. Ajoutez le dossier médical du patient."}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-2 sm:gap-2">
          <Button variant="outline" onClick={handleLater}>
            Plus tard
          </Button>
          <Button onClick={handleAddRecord} disabled={resolving}>
            {resolving ? "Ouverture…" : "Ajouter le dossier médical"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
