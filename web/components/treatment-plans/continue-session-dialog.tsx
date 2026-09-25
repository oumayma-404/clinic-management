"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { ContinuableActDto } from "@/lib/api/types"

/**
 * « C'est la suite d'une séance précédente » — what is left of the second window that used to ask it.
 *
 * <p>⚠️ <b>There is no dialog any more.</b> The continuable séances are cards in the booking dialog's own
 * « Continuer un traitement » list (`ContinueTreatmentList`), and pressing « Continuer » adds the row at once;
 * the séance's name and « Prix du reste » are typed on that row's card in the acts picker. The file keeps the
 * two things both doors still share: the choice's shape and the loader.</p>
 *
 * <p>⚠️ <b>Still nothing is written on the press.</b> The row carries `SelectedAct.pendingContinuation` and
 * `materialiseTreatments` mints the devis — numbered and accepted — when the booking is SAVED. Minting it on
 * the press left a live créance for a séance nobody booked the moment « Annuler » was pressed.</p>
 */

/** The continuation as chosen, before anything exists on the server. */
export interface ContinuationChoice {
  /** The séance being continued, as the list offered it — the card reads its money straight from this. */
  previous: ContinuableActDto
  /** What this séance is called. */
  nextStepLabel: string
  /** What the remaining work is worth, or null for « rien de plus ». */
  remainingWorkCost: number | null
}

/** What the next séance is called when the dentist does not say. Matches the server's own default. */
export const DEFAULT_NEXT_LABEL = "Séance suivante"

/** « Planifier la suite » names the séance the same way the in-dialog « Continuer » does — one default, not two. */
export const PRESELECTED_NEXT_LABEL = DEFAULT_NEXT_LABEL

export interface ContinuableActs {
  /** The patient's continuable séances, ticked « non terminé » first; null while loading or not offered. */
  acts: ContinuableActDto[] | null
  /** The read failed — never shown as « aucune séance » (§ 13). */
  failed: boolean
  reload: () => void
}

/**
 * The séances of this patient a booking may continue.
 *
 * <p>⚠️ The server puts ticked acts first and returns every recent act: the tick is a SORT, never a filter,
 * because it is one checkbox at the end of a séance and a forgotten tick must stay recoverable. The list
 * shows the ticked ones and folds the rest behind « Suite d'une séance passée… ».</p>
 *
 * <p>⚠️ A stale answer is dropped: the booking dialog changes patient under this read, and a late response for
 * the previous patient would offer their séances on the new one.</p>
 */
export function useContinuableActs(patientId: string | null | undefined, enabled: boolean): ContinuableActs {
  const [acts, setActs] = useState<ContinuableActDto[] | null>(null)
  const [failed, setFailed] = useState(false)
  const requestRef = useRef(0)

  const load = useCallback(async () => {
    if (!patientId) return
    const request = ++requestRef.current
    setFailed(false)
    setActs(null)
    try {
      const result = await treatmentPlansApi.continuableActs(patientId)
      if (request === requestRef.current) setActs(result)
    } catch {
      if (request === requestRef.current) setFailed(true)
    }
  }, [patientId])

  useEffect(() => {
    if (!enabled || !patientId) {
      requestRef.current++
      setActs(null)
      setFailed(false)
      return
    }
    void load()
  }, [enabled, patientId, load])

  return { acts, failed, reload: () => void load() }
}
