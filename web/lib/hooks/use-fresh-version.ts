"use client"

import { useCallback, useEffect, useRef, useState } from "react"

export interface FreshVersion<T> {
  /** The server's copy once one has been read, falling back to the caller's snapshot until then. */
  source: T | null
  /** Re-read the row. Call after a save whose failure was **not** a 409 — see the note below. */
  resync: () => Promise<void>
}

/**
 * Keeps the `version` a form saves with equal to the row's **current** one, so « cet enregistrement a été
 * modifié par quelqu'un d'autre » can only mean what it says.
 *
 * <p>Every update that round-trips a `version` is checked against PostgreSQL's `xmin`, and a mismatch is a 409.
 * That is right when a colleague edited the row — and wrong, confusingly so, in the three cases where the
 * user's <b>own</b> save moved the version further than the screen was told:</p>
 *
 * <ol>
 *   <li><b>One save, several writes to the same row.</b> Saving a patient PUTs the patient and then writes each
 *   medical- and family-history entry, and every one of those six commands calls `UpdateAsync(patient)` to touch
 *   `UpdatedAt` — so the version in the PUT's response is already behind by the time the loop ends.</li>
 *   <li><b>A save that fails partway.</b> The first write landed, the success callback never ran, so nothing
 *   refetched and the form kept its pre-save version. Every later click then 409s — permanently, until a full
 *   page reload. This is the one users report as « it says someone else edited it, but it was me ».</li>
 *   <li><b>A refetch that has not landed yet.</b> The parent refreshes on success; reopening the form before
 *   that resolves saves the stale version.</li>
 * </ol>
 *
 * <p>⚠️ <b>Do not `resync()` after a 409.</b> Refreshing the version on a real conflict would let the retry
 * silently overwrite the other person's work, which is the exact lost update the token exists to stop. Resync on
 * success, and on failures that are not conflicts; leave a 409 to {@link useConflict}, which keeps it on screen
 * with a reload. `edit-appointment-dialog` reads the appointment on open for this reason and was, until this
 * hook, the only form that did.</p>
 *
 * @param open   Whether the form is showing — the read happens on the transition into it.
 * @param key    The row's id. Drives the re-read, so `load` needs no memoisation.
 * @param snapshot The caller's own copy, used until the server answers and if the read fails.
 * @param load   Reads the row. Returning null keeps the snapshot.
 */
export function useFreshVersion<T>(
  open: boolean,
  key: string | null | undefined,
  snapshot: T | null | undefined,
  load: () => Promise<T | null>,
): FreshVersion<T> {
  const [fresh, setFresh] = useState<T | null>(null)
  // Held in a ref so an inline arrow from the caller cannot re-trigger the effect on every render.
  const loadRef = useRef(load)
  loadRef.current = load
  const mounted = useRef(true)
  useEffect(() => () => { mounted.current = false }, [])

  const read = useCallback(async () => {
    if (!key) return
    try {
      const row = await loadRef.current()
      if (row && mounted.current) setFresh(row)
    } catch {
      // A snapshot still saves better than a form that will not open; the save's own 409 remains the backstop.
    }
  }, [key])

  useEffect(() => {
    if (!open || !key) {
      // Dropped on close so a later open cannot save against the previous row's version.
      setFresh(null)
      return
    }
    void read()
  }, [open, key, read])

  return { source: fresh ?? snapshot ?? null, resync: read }
}
