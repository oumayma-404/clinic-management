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
const SNOOZE_MS = 60 * 60 * 1000
const SNOOZE_STORAGE_KEY = "clinic:pvr-snooze"

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

  const refetch = useCallback(async () => {
    if (!user) return
    try {
      const list = await notificationsApi.pendingReviews()
      if (mountedRef.current) {
        setReviews(list)
        setNow(Date.now())
        // New data — lift the dismissal so a *different* pending visit can prompt. The one just dismissed is
        // held back by its snooze, so this cannot resurrect it.
        setDismissed(false)
      }
    } catch {
      // Best-effort — a failed poll must never surface an error over the app.
    }
  }, [user])

  // Poll on mount + on an interval while authenticated.
  useEffect(() => {
    if (!user) return
    void refetch()
    const id = window.setInterval(() => void refetch(), POLL_INTERVAL_MS)
    return () => window.clearInterval(id)
  }, [user, refetch])

  // A generation/removal broadcast (e.g. a review became due or was fulfilled) refetches promptly.
  useClinicRealtime(RealtimeResource.Notifications, () => {
    void refetch()
  })

  const active = useMemo(
    () => reviews.find((r) => !snoozed[r.id] || snoozed[r.id] <= now) ?? null,
    [reviews, snoozed, now],
  )

  const snooze = useCallback((id: string) => {
    setSnoozed((prev) => {
      const nowMs = Date.now()
      // Prune expired entries while writing so the persisted map can't grow unbounded on a long-lived
      // browser (expired entries are ignored on read anyway).
      const next: SnoozeMap = { [id]: nowMs + SNOOZE_MS }
      for (const [key, until] of Object.entries(prev)) {
        if (until > nowMs) next[key] = until
      }
      saveSnooze(next)
      return next
    })
  }, [])

  const handleAddRecord = useCallback(() => {
    // A review with no appointment can't be fulfilled here (saving a record marks *that* appointment
    // Completed) — don't snooze-and-navigate one that would just return forever. In practice a
    // PostVisitReview is always generated with an appointmentId, so this guard is defensive.
    if (!active?.appointmentId || resolving) return
    const reviewId = active.id
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
        snooze(reviewId)
        router.push(`/patients/${patientId}?addRecord=1&appointmentId=${encodeURIComponent(appointmentId)}`)
      } catch {
        // Keep it pending rather than navigate to a dead page; the poll will offer it again.
      } finally {
        setResolving(false)
      }
    })()
  }, [active, resolving, snooze, router])

  /**
   * The one dismissal path — « Plus tard », the ✕, Escape and a click outside all land here, so every way of
   * saying "not now" behaves identically (the ✕ used to depend on the snooze taking effect; now it cannot fail).
   *
   * Order matters: close first, snooze second. The close is unconditional local state, so a snooze that throws
   * still leaves a closed dialog.
   */
  const handleLater = useCallback(() => {
    setDismissed(true)
    if (active) snooze(active.id)
  }, [active, snooze])

  /*
   * AC-27 — on a coarse pointer this is a **toast with an action, not a sheet**.
   *
   * It is mounted in the header, so it fires on all 24 routes and re-polls every 60 s. As a modal on a phone
   * that means the app can seize the whole screen while the user is mid-task, on a *reminder* — the one
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
    if (!isCoarse || active === null || dismissed) return
    if (document.body.hasAttribute("data-sheet-open") || document.body.hasAttribute("data-scroll-locked")) return

    toast(active.title ?? "Compte rendu de visite", {
      id: `pvr-${active.id}`,
      description: active.message ?? "La visite est terminée. Ajoutez le dossier médical du patient.",
      duration: 30_000,
      icon: <ClipboardPlus className="h-5 w-5 text-primary" />,
      action: { label: "Ajouter", onClick: handleAddRecord },
      onDismiss: handleLater,
      onAutoClose: handleLater,
    })
  }, [isCoarse, active, dismissed, handleAddRecord, handleLater])

  // On a coarse pointer the toast above *is* the prompt; rendering the dialog too would show both.
  if (isCoarse) return null

  return (
    <Dialog
      open={active !== null && !dismissed}
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
