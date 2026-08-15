"use client"

import { useCallback, useEffect, useState } from "react"
import Link from "next/link"
import { ClipboardCheck } from "lucide-react"
import { appointmentsApi } from "@/lib/api/appointments"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/**
 * « 3 séances à clôturer → » above the agenda.
 *
 * <p><b>Why it lives here at all.</b> The dashboard is `AdminOrDoctor` and `/` redirects a secretary to this page,
 * so the dashboard chip is invisible to reception — who is exactly the person who knows whether the patient came
 * and who takes the money. This is the surface that reaches them, and `GET /api/appointments/to-close` is
 * `AnyClinicRole` for the same reason.</p>
 *
 * <p><b>One line, never a banner and never a dialog.</b> `.claude/rules/frontend-web.md` § 5 names this class of
 * prompt: an unrequested interruption must not take the screen, so this states a fact and offers a way through.
 * It is also the reason nothing here auto-opens — a dentist arriving at the agenda is on their way somewhere.</p>
 *
 * <p><b>It renders nothing when there is nothing.</b> « 0 séance à clôturer » is a line to read past every morning
 * for the practice that closes each visit at the chair, which is the practice we least want to nag.</p>
 *
 * <p>⚠️ It asks for `pageSize: 1` and reads `totalCount`: that figure is the whole clinic's, over the server's own
 * default window, and it is the same number the dashboard chip shows because both come from the same rule. Reading
 * `items.length` would report « 1 » for ever.</p>
 */
export function VisitClosureStrip() {
  const [count, setCount] = useState<number | null>(null)

  const load = useCallback(async () => {
    try {
      const page = await appointmentsApi.visitsToClose({ page: 1, pageSize: 1 })
      setCount(page.totalCount)
    } catch {
      // Deliberately silent. This is an *additive* prompt beside the agenda, not a read the page depends on —
      // a failed probe must leave the agenda exactly as it was rather than put an error above it. The worklist
      // itself reports its own failures, with a retry.
      setCount(null)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  useClinicRealtime(
    [
      RealtimeResource.Appointments,
      RealtimeResource.Patients,
      RealtimeResource.Invoices,
      RealtimeResource.TreatmentPlans,
    ],
    load,
  )

  if (count === null || count === 0) {
    return null
  }

  return (
    <Link
      href="/a-cloturer"
      className="flex min-h-11 flex-wrap items-center gap-2 rounded-md border border-warning-ink/25 bg-warning-wash px-3 py-2 text-sm text-warning-ink underline-offset-4 hover-hover:hover:underline"
    >
      <ClipboardCheck aria-hidden="true" className="size-4 shrink-0" />
      <span className="flex-1">
        <strong className="font-semibold">{count.toLocaleString("fr-TN")}</strong>{" "}
        {count === 1 ? "séance passée attend" : "séances passées attendent"} une présence, une fiche ou un
        encaissement.
      </span>
      <span aria-hidden="true" className="font-medium">
        Clôturer →
      </span>
    </Link>
  )
}
