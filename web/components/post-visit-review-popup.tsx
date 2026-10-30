"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { usePathname, useRouter } from "next/navigation"
import { CalendarClock, ClipboardPlus, Clock } from "lucide-react"
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
import type { AppointmentDto, PendingReviewDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { normalizeStatus } from "@/components/appointment-labels"
import { LateArrivalChips } from "@/components/appointment-late-arrival-chips"
import { PostponeAppointmentDialog, PostponeForm, postponeSubject } from "@/components/appointment-postpone-dialog"

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

/*
 * ⚠️ The words are composed HERE, never the notification's own `title`/`message`: the server says « Compte rendu
 * de visite · … Ajoutez son dossier médical. » next to a button reading « Remplir la fiche de soins » — two names
 * for one thing. The bell keeps the server's text; this prompt states the fact and lets the button name the task.
 */
const PROMPT_TITLE = "Séance terminée"

/** « Séance de Mme X terminée. » — or the bare fact while the patient's name is unknown. */
function promptSentence(patientName: string | null | undefined): string {
  const name = patientName?.trim()
  return name ? `Séance de ${name} terminée.` : "Séance terminée."
}

const PRESENCE_QUESTION = "Le patient est-il venu ?"

/**
 * Step 1 asks whether the patient came; step 2 offers the fiche (and a retard); « postpone » moves the visit. A visit
 * already « Terminé », or carrying a retard, starts on step 2.
 */
type PromptStep = "presence" | "fiche" | "postpone"

// sonner's buttons are 24 px; the toast only renders on a coarse pointer, so they take the 44 px floor here.
const TOAST_BUTTON_CLASS = "h-11! px-4! text-sm!"

/** The visit behind the active review, read once so the sentence can name the patient and the button can go. */
interface ResolvedVisit {
  appointmentId: string
  patientId: string | null
  patientName: string | null
  status: string | null
  lateArrivalMinutes: number | null
  /** The read itself, for « Reporter »; null when it failed, which withholds that answer. */
  appointment: AppointmentDto | null
}

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
 * everyone) to record what happened. It asks « venu ? » first: « Absent » marks the visit `NoShow` and closes,
 * « Venu » moves to « Remplir la fiche de soins », which deep-links to record creation for that visit;
 * "Plus tard" snoozes it client-side without marking it read. Mounted once in the dashboard header,
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

  /**
   * Silent on a patient's own file.
   *
   * ⚠️ **The prompt used to interrupt the very page that already offers what it asks for.** Standing on a
   * patient's fiche, above an « À compléter — séances sans fiche » band listing eight visits with an
   * « Enregistrer la fiche » on each, a modal opened to say « la visite de … est terminée. Ajoutez son dossier
   * médical. » The component knew how to stand down for another *dialog* (that guard is a few lines below, and
   * it was written for this same class of interruption) and nothing about the *route*.
   *
   * ⚠️ **Route-wide, not per-patient, and that is a knowing trade.** `PendingReviewDto` carries no `patientId`
   * — the click handler resolves it by fetching the appointment — so suppressing only the reviews belonging to
   * the open file would mean a fetch per pending review on every poll. Suppressing on any patient file can
   * therefore hold back a *different* patient's prompt while one is open; the prompt is a nudge, it re-polls
   * every 60 s and nothing is marked read, so it fires as soon as the file is left. The alternative — a modal
   * over the page that answers it — is the worse of the two. Give the DTO a `patientId` and this narrows to an
   * equality check.
   */
  const pathname = usePathname()
  const onAPatientFile = /^\/patients\/[^/]+/.test(pathname ?? "")
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

  /*
   * The patient's name for the sentence, read from the appointment — `PendingReviewDto` carries no name of its
   * own. Only while the prompt could be shown, once per review; « Remplir » then reuses the `patientId`.
   */
  const [visit, setVisit] = useState<ResolvedVisit | null>(null)
  const activeAppointmentId = active?.appointmentId ?? null
  useEffect(() => {
    if (!activeAppointmentId || isPhone || dismissed || onAPatientFile) return
    if (visit?.appointmentId === activeAppointmentId) return
    let cancelled = false
    appointmentsApi
      .get(activeAppointmentId)
      .then((a) => {
        if (cancelled || !mountedRef.current) return
        setVisit({
          appointmentId: activeAppointmentId,
          patientId: a.patientId ?? null,
          patientName: a.patientName ?? null,
          status: a.status ?? null,
          lateArrivalMinutes: a.lateArrivalMinutes ?? null,
          appointment: a,
        })
      })
      .catch(() => {
        // The bare fact stands in for the name; « Remplir » looks the appointment up again on press.
        if (cancelled || !mountedRef.current) return
        setVisit({
          appointmentId: activeAppointmentId,
          patientId: null,
          patientName: null,
          status: null,
          lateArrivalMinutes: null,
          appointment: null,
        })
      })
    return () => {
      cancelled = true
    }
  }, [activeAppointmentId, isPhone, dismissed, onAPatientFile, visit])
  const activeVisit = visit && visit.appointmentId === activeAppointmentId ? visit : null
  // Held back until the lookup settles, so the sentence never swaps under the reader.
  const visitSettled = activeAppointmentId === null || activeVisit !== null

  // The appointment « Venu » was answered for — no write, so it lives only here until the fiche is saved.
  const [cameFor, setCameFor] = useState<string | null>(null)
  const [markingAbsent, setMarkingAbsent] = useState(false)
  /** The appointment the inline « Reporter » step is open for (dialog path). */
  const [postponeFor, setPostponeFor] = useState<string | null>(null)
  /** The appointment the « Reporter » dialog is open for (tablet path — a toast cannot hold a form). */
  const [postponeDialogFor, setPostponeDialogFor] = useState<string | null>(null)
  const [lateOpen, setLateOpen] = useState(false)
  const [savingLate, setSavingLate] = useState(false)
  const alreadyCompleted = activeVisit?.status != null && normalizeStatus(activeVisit.status) === "Completed"
  const lateArrival = activeVisit?.lateArrivalMinutes ?? null
  // A recorded retard says the patient came, so « venu ? » is not asked again.
  const step: PromptStep =
    activeAppointmentId === null
      ? "presence"
      : postponeFor === activeAppointmentId
        ? "postpone"
        : cameFor === activeAppointmentId || alreadyCompleted || lateArrival !== null
          ? "fiche"
          : "presence"
  const sentence =
    step === "presence"
      ? `${promptSentence(activeVisit?.patientName)} ${PRESENCE_QUESTION}`
      : promptSentence(activeVisit?.patientName)

  useEffect(() => {
    setLateOpen(false)
  }, [activeAppointmentId])

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
        // Already read for the sentence; looked up again only when that read failed.
        const patientId =
          activeVisit?.appointmentId === appointmentId && activeVisit.patientId
            ? activeVisit.patientId
            : (await appointmentsApi.get(appointmentId)).patientId
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
  }, [active, activeVisit, resolving, snoozeAll, router])

  /** « Venu » — moves to the fiche step and writes nothing: saving the fiche is what marks the visit « Terminé ». */
  const handleCame = useCallback(() => {
    if (activeAppointmentId) setCameFor(activeAppointmentId)
  }, [activeAppointmentId])

  /**
   * « Absent » — the closure worklist's own status write; the server drops the review with it.
   * Closes without snoozing the queue: the rest of the waiting séances were not answered « not now ».
   */
  const handleAbsent = useCallback(() => {
    if (!active?.appointmentId || markingAbsent) return
    const appointmentId = active.appointmentId
    const toastId = `pvr-${active.id}`
    setMarkingAbsent(true)
    void (async () => {
      try {
        await appointmentsApi.update(appointmentId, { status: "NoShow" })
        toast.success("Patient marqué comme absent.")
        // The tablet's answers live in the toast's own body now, so nothing else closes it.
        toast.dismiss(toastId)
        setDismissed(true)
      } catch (err) {
        showErrorToast(err)
        // A refusal usually means the visit moved on (a fiche was saved): re-read so the prompt follows it.
        void refetch()
      } finally {
        if (mountedRef.current) setMarkingAbsent(false)
      }
    })()
  }, [active, markingAbsent, refetch])

  /** « Reporter » — inline on the dialog; on a tablet the toast closes and the form opens as a dialog of its own. */
  const handlePostpone = useCallback(
    (asDialog: boolean) => {
      if (!active || !activeVisit?.appointment) return
      if (asDialog) {
        toast.dismiss(`pvr-${active.id}`)
        setPostponeDialogFor(active.appointmentId ?? null)
      } else {
        setPostponeFor(active.appointmentId ?? null)
      }
    },
    [active, activeVisit],
  )

  /** Moved or put on the Liste d'attente: closes like « Absent », without snoozing the rest of the queue. */
  const handlePostponed = useCallback(() => {
    setPostponeFor(null)
    setPostponeDialogFor(null)
    setDismissed(true)
  }, [])

  /** Saves the retard at once (null clears it) — no status is written, exactly like « Venu ». */
  const recordLateArrival = useCallback(
    (minutes: number | null) => {
      if (!activeAppointmentId || savingLate) return
      const appointmentId = activeAppointmentId
      setSavingLate(true)
      void (async () => {
        try {
          await appointmentsApi.update(appointmentId, { lateArrivalMinutes: minutes })
          setVisit((v) => (v && v.appointmentId === appointmentId ? { ...v, lateArrivalMinutes: minutes } : v))
          setLateOpen(false)
          toast.success(minutes === null ? "Retard retiré." : `Retard de ${minutes} min noté.`)
        } catch (err) {
          showErrorToast(err)
        } finally {
          if (mountedRef.current) setSavingLate(false)
        }
      })()
    },
    [activeAppointmentId, savingLate],
  )

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
    setPostponeFor(null)
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
    if (isPhone || !isCoarse || active === null || dismissed || !mayPrompt || onAPatientFile || !visitSettled) return
    // The « Reporter » dialog stands in for the toast; closing it without an answer brings the toast back.
    if (postponeDialogFor !== null) return

    const presence = step === "presence"
    // Same id on both steps, so « Venu » turns this toast into the « Remplir » one in place.
    toast(PROMPT_TITLE, {
      id: `pvr-${active.id}`,
      // A sonner toast holds two buttons, so the three answers sit under the sentence — 44 px, grown not overlaid.
      description: presence ? (
        <div className="space-y-2">
          <p>{sentence}</p>
          <div className="flex flex-wrap gap-2">
            <Button size="sm" className="h-11 px-4" onClick={handleCame} disabled={markingAbsent}>
              Venu
            </Button>
            <Button size="sm" variant="outline" className="h-11 bg-card px-4" onClick={handleAbsent} disabled={markingAbsent}>
              Absent
            </Button>
            {activeVisit?.appointment && (
              <Button
                size="sm"
                variant="outline"
                className="h-11 bg-card px-4"
                onClick={() => handlePostpone(true)}
                disabled={markingAbsent}
              >
                Reporter
              </Button>
            )}
          </div>
        </div>
      ) : (
        sentence
      ),
      duration: 30_000,
      icon: <ClipboardPlus className="h-5 w-5 text-primary" />,
      classNames: { actionButton: TOAST_BUTTON_CLASS, cancelButton: TOAST_BUTTON_CLASS },
      // Explicit `undefined`s: sonner merges an update into the live toast, so an omitted key would linger.
      action: presence ? undefined : { label: "Remplir", onClick: handleAddRecord },
      cancel: undefined,
      onDismiss: handleLater,
      onAutoClose: handleLater,
    })
  }, [isPhone, isCoarse, active, activeVisit, dismissed, mayPrompt, onAPatientFile, visitSettled, sentence, step, markingAbsent, postponeDialogFor, handleAddRecord, handleCame, handleAbsent, handlePostpone, handleLater])

  const postponeDialog = (
    <PostponeAppointmentDialog
      appointmentId={postponeDialogFor}
      open={postponeDialogFor !== null}
      onOpenChange={(next) => {
        if (!next) setPostponeDialogFor(null)
      }}
      onDone={handlePostponed}
    />
  )

  // On a phone the header bell *is* the prompt; on a tablet the toast above is — plus « Reporter »'s own dialog.
  if (isPhone) return null
  if (isCoarse) return postponeDialog

  const postponing = step === "postpone" ? activeVisit?.appointment ?? null : null

  return (
    <Dialog
      open={active !== null && !dismissed && mayPrompt && !onAPatientFile && visitSettled}
      onOpenChange={(next) => {
        if (!next) handleLater()
      }}
    >
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <div className="mb-2 flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary">
            {postponing ? <CalendarClock className="h-5 w-5" /> : <ClipboardPlus className="h-5 w-5" />}
          </div>
          <DialogTitle>{postponing ? "Reporter la séance" : PROMPT_TITLE}</DialogTitle>
          <DialogDescription>{postponing ? postponeSubject(postponing) : sentence}</DialogDescription>
        </DialogHeader>
        {postponing ? (
          <PostponeForm
            appointment={postponing}
            onBack={() => setPostponeFor(null)}
            onDone={handlePostponed}
            onReloaded={(fresh) =>
              setVisit((v) =>
                v && v.appointmentId === fresh.id
                  ? { ...v, status: fresh.status ?? null, lateArrivalMinutes: fresh.lateArrivalMinutes ?? null, appointment: fresh }
                  : v,
              )
            }
          />
        ) : step === "presence" ? (
          // Words only, and the same label while in flight: four buttons fit one line of the 448 px dialog that way.
          <DialogFooter className="gap-2 sm:gap-2">
            {/* « Plus tard » stays a button on this step: `e2e/lib/goto.ts` dismisses the prompt by its name. */}
            <Button variant="ghost" onClick={handleLater} className="sm:me-auto">
              Plus tard
            </Button>
            {activeVisit?.appointment && (
              <Button variant="outline" onClick={() => handlePostpone(false)} disabled={markingAbsent}>
                Reporter
              </Button>
            )}
            <Button variant="outline" onClick={handleAbsent} disabled={markingAbsent}>
              Absent
            </Button>
            <Button onClick={handleCame} disabled={markingAbsent}>
              Venu
            </Button>
          </DialogFooter>
        ) : (
          <>
            <div className="flex min-h-9 flex-wrap items-center gap-2">
              {lateOpen ? (
                <LateArrivalChips
                  value={lateArrival}
                  onChange={recordLateArrival}
                  withNone={lateArrival !== null}
                  disabled={savingLate}
                />
              ) : lateArrival !== null ? (
                <>
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-warning-wash px-2.5 py-1 text-sm font-medium text-warning-ink">
                    <Clock aria-hidden="true" className="size-3.5" />
                    Retard de {lateArrival} min
                  </span>
                  <Button variant="ghost" size="sm" onClick={() => setLateOpen(true)} disabled={savingLate}>
                    Modifier
                  </Button>
                </>
              ) : (
                <Button
                  variant="ghost"
                  size="sm"
                  className="-ms-2 text-muted-foreground"
                  onClick={() => setLateOpen(true)}
                >
                  <Clock aria-hidden="true" className="size-4" />
                  Noter un retard
                </Button>
              )}
            </div>
            <DialogFooter className="gap-2 sm:gap-2">
              <Button variant="outline" onClick={handleLater}>
                Plus tard
              </Button>
              <Button onClick={handleAddRecord} disabled={resolving}>
                {resolving ? "Ouverture…" : "Remplir la fiche de soins"}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}
