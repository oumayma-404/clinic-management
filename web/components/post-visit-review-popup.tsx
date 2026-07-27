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
 */
export function PostVisitReviewPopup() {
  const { user } = useSession()
  const router = useRouter()

  const [reviews, setReviews] = useState<PendingReviewDto[]>([])
  const [snoozed, setSnoozed] = useState<SnoozeMap>({})
  const [now, setNow] = useState(0)
  // True while the appointment's patient is being resolved, so the button can't fire twice.
  const [resolving, setResolving] = useState(false)

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
        snooze(reviewId)
        router.push(`/patients/${patientId}?addRecord=1&appointmentId=${encodeURIComponent(appointmentId)}`)
      } catch {
        // Keep it pending rather than navigate to a dead page; the poll will offer it again.
      } finally {
        setResolving(false)
      }
    })()
  }, [active, resolving, snooze, router])

  const handleLater = useCallback(() => {
    if (active) snooze(active.id)
  }, [active, snooze])

  return (
    <Dialog open={active !== null} onOpenChange={(open) => { if (!open) handleLater() }}>
      <DialogContent className="sm:max-w-md">
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
