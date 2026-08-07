"use client"

import { useCallback, useEffect, useState } from "react"
import { billingApi } from "@/lib/api/billing"
import type { CnamCeilingDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { formatDT } from "@/lib/format"
import { LoadFailureNotice } from "@/components/ui/load-failure"

/**
 * « Plafond annuel CNAM » beside a reimbursement estimate (L10).
 *
 * <p><b>What it closes.</b> « Remboursement indicatif » was <c>coefficient × VLC × rate</c> with no cap and no
 * memory, so it told a patient who had exhausted their ceiling in March exactly what it told one who had never
 * claimed — and the disclaimer under it named only the age band. This states the ceiling, what the clinic has
 * consumed of it this year, and what is left.</p>
 *
 * <p>⚠️ <b>It is a component and not a line of JSX in the editor</b> because the caveats are the load-bearing half
 * and there will be a second caller. The two reasons the figure is an estimate are structurally different and both
 * have to be said: the barème is sourced rather than officially confirmed (<c>ceilingIsDefault</c>), and this clinic
 * can only count the acts it performed, so « reste » is an <b>upper bound</b> (<c>seesThisClinicOnly</c>). A copy of
 * that wording per screen is a copy that loses one of them.</p>
 *
 * <p>⚠️ <b>A failed read never renders as « plafond épuisé » or as zero.</b> `.catch(() => null)` here would put a
 * confident financial claim on screen after a network blip, which is the § 13 defect this repo keeps documenting —
 * so a failure is its own state with a « Réessayer », and the estimate above it stays valid and visible.</p>
 */
export function CnamCeilingNotice({
  patientId,
  /**
   * The estimate the caller is showing, so « et après ce bulletin ? » can be answered without a second request.
   * Optional: with none, the component simply reports the year's position.
   */
  pendingEstimate,
}: {
  patientId: string | null | undefined
  pendingEstimate?: number
}) {
  const [ceiling, setCeiling] = useState<CnamCeilingDto | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const retry = useCallback(() => setReloadKey((k) => k + 1), [])

  useEffect(() => {
    if (!patientId) {
      setCeiling(null)
      setFailure(null)
      return
    }
    let active = true
    const load = async () => {
      setFailure(null)
      try {
        const result = await billingApi.getPatientCnamCeiling(patientId)
        if (active) setCeiling(result)
      } catch (e) {
        if (active) {
          // The stale figure is dropped along with the success state: a ceiling from the previously selected
          // patient shown under this patient's estimate would be worse than none.
          setCeiling(null)
          setFailure(getErrorMessage(e, "Le plafond annuel n'a pas pu être chargé."))
        }
      }
    }
    load()
    return () => {
      active = false
    }
  }, [patientId, reloadKey])

  if (!patientId) return null

  if (failure) {
    return <LoadFailureNotice variant="inline" message={failure} onRetry={retry} />
  }

  if (!ceiling) return null

  // Floored at zero for the same reason the server floors `remaining`: « −80,000 DT » under a bulletin reads as a
  // debt to the caisse rather than as « the ceiling is used up », which is what `willExceed` says instead.
  const afterThis = Math.max(0, ceiling.remaining - (pendingEstimate ?? 0))
  const willExceed = (pendingEstimate ?? 0) > ceiling.remaining

  return (
    <div
      role="note"
      className={`rounded-lg border border-dashed p-3 text-xs ${
        ceiling.exhausted || willExceed ? "border-warning/40 bg-warning-wash text-warning-ink" : "text-muted-foreground"
      }`}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <span className="font-medium">Plafond annuel CNAM {ceiling.year}</span>
        <span className="font-semibold">
          {formatDT(ceiling.ceiling)}
          {ceiling.ceilingIsDefault && ceiling.baseCeiling != null && ceiling.dentalAllowance != null && (
            <span className="ms-1 font-normal">
              ({formatDT(ceiling.baseCeiling)} foyer + {formatDT(ceiling.dentalAllowance)} dentaire)
            </span>
          )}
        </span>
      </div>

      <p className="mt-1">
        Consommé dans ce cabinet&nbsp;: <span className="font-medium">{formatDT(ceiling.consumed)}</span>
        {/* Distinguishes « nothing claimed » from « nothing billed yet » — the same figure, two different facts. */}
        {ceiling.invoiceCount === 0 && " (aucune note d'honoraires cette année)"} · reste{" "}
        <span className="font-medium">{formatDT(ceiling.remaining)}</span>
        {/* Only mentioned when there is some: « hors plafond 0,000 » on every bulletin is noise. */}
        {ceiling.horsPlafond > 0 && (
          <> · dont {formatDT(ceiling.horsPlafond)} hors plafond (prothèse, non décompté)</>
        )}
      </p>

      {pendingEstimate != null && pendingEstimate > 0 && (
        <p className="mt-1">
          {willExceed ? (
            <>
              ⚠️ Ce bulletin ({formatDT(pendingEstimate)}) <strong>dépasse le reste disponible</strong> — la CNAM ne
              remboursera au plus que {formatDT(ceiling.remaining)}.
            </>
          ) : (
            <>
              Après ce bulletin&nbsp;: <span className="font-medium">{formatDT(afterThis)}</span>
            </>
          )}
        </p>
      )}

      {/* Both caveats, always. The first is conditional because it stops being true once somebody records the real
          figure; the second never does — which is exactly why it is worth printing every time. */}
      <p className="mt-1.5 opacity-90">
        {ceiling.ceilingIsDefault &&
          "Barème 2024 par défaut (sources concordantes, non officielles) — saisissez le plafond réel sur la fiche du patient si vous le connaissez. "}
        {ceiling.seesThisClinicOnly &&
          "Ce cabinet ne voit que ses propres actes : un patient soigné ailleurs a consommé un plafond invisible ici, donc le « reste » est un maximum."}
      </p>
    </div>
  )
}
