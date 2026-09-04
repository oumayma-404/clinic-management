"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { patientFilesApi } from "@/lib/api/patient-files"
import { showErrorToast } from "@/lib/errors"
import { defaultLabel, type MeshAnnotation } from "@/lib/files/mesh/annotation"
import type { MeshPoint } from "@/lib/files/mesh/measure"
import type { PatientFileAnnotationDto } from "@/lib/api/types"

/**
 * The markers on one model, kept in step with the server.
 *
 * <p>⚠️ **Optimistic, and it has to be.** A marker is placed by touching the model, and a pin that appears a
 * round trip later reads as a tap that missed — so the reader taps again, and now there are two. The pin is
 * therefore drawn immediately under a temporary id and reconciled with the server's row when it lands; a
 * failure takes it away again **and says so**, because a marker that silently vanishes is worse than one that
 * never appeared.</p>
 *
 * <p>⚠️ **Renaming is debounced and the last write wins over the field, not over the row.** Typing a label
 * would otherwise be one request per keystroke. The timer is per marker, so renaming two markers quickly does
 * not have one cancel the other — a single shared timer was the obvious shape and would drop the first
 * rename entirely.</p>
 */

/** How long the field settles before a rename is sent. Long enough to type a word, short enough to feel saved. */
const RENAME_DEBOUNCE_MS = 600

/** A marker the server has not acknowledged yet. Its id is replaced by the row's when it does. */
const PENDING_PREFIX = "pending:"

function toAnnotation(dto: PatientFileAnnotationDto): MeshAnnotation {
  return {
    id: dto.id,
    point: { x: dto.x, y: dto.y, z: dto.z },
    normal: { x: dto.normalX, y: dto.normalY, z: dto.normalZ },
    label: dto.label,
  }
}

export interface MeshAnnotationStore {
  annotations: MeshAnnotation[]
  /** True while the first read is in flight, so the viewer can avoid drawing an empty model as « no markers ». */
  loading: boolean
  place: (point: MeshPoint, normal: MeshPoint) => void
  rename: (id: string, label: string) => void
  remove: (id: string) => void
  selectedId: string | null
  select: (id: string | null) => void
}

export function useMeshAnnotations(
  patientId: string,
  fileId: string,
  open: boolean,
): MeshAnnotationStore {
  const [annotations, setAnnotations] = useState<MeshAnnotation[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const renameTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>())
  // ⚠️ Bumped on every open. A response that lands after the dialog has moved to another file must not paint
  // that file's markers onto this one — the same token discipline `use-file-preview` uses.
  const request = useRef(0)

  useEffect(() => {
    if (!open) {
      setAnnotations([])
      setSelectedId(null)
      return
    }

    const token = ++request.current
    setLoading(true)

    void (async () => {
      try {
        const rows = await patientFilesApi.getAnnotations(patientId, fileId)
        if (token !== request.current) return
        setAnnotations(rows.map(toAnnotation))
      } catch (error) {
        if (token !== request.current) return
        // ⚠️ An empty list and a failed read are the same picture and opposite facts, and here the wrong one is
        // reassuring: « this model has no markers » when in truth we could not ask.
        showErrorToast(error, "Impossible de lire les repères de ce modèle.")
      } finally {
        if (token === request.current) setLoading(false)
      }
    })()
  }, [open, patientId, fileId])

  // Timers outlive a render but must not outlive the dialog.
  useEffect(() => {
    const timers = renameTimers.current
    return () => {
      for (const timer of timers.values()) clearTimeout(timer)
      timers.clear()
    }
  }, [])

  const place = useCallback(
    (point: MeshPoint, normal: MeshPoint) => {
      const temporaryId = `${PENDING_PREFIX}${crypto.randomUUID()}`
      const token = request.current

      let label = ""
      setAnnotations((current) => {
        label = defaultLabel(current)
        return [...current, { id: temporaryId, point, normal, label }]
      })
      setSelectedId(temporaryId)

      void (async () => {
        try {
          const created = await patientFilesApi.createAnnotation(patientId, fileId, {
            x: point.x,
            y: point.y,
            z: point.z,
            normalX: normal.x,
            normalY: normal.y,
            normalZ: normal.z,
            label,
          })
          if (token !== request.current) return

          setAnnotations((current) =>
            current.map((one) => (one.id === temporaryId ? toAnnotation(created) : one)),
          )
          setSelectedId((current) => (current === temporaryId ? created.id : current))
        } catch (error) {
          if (token !== request.current) return
          setAnnotations((current) => current.filter((one) => one.id !== temporaryId))
          setSelectedId((current) => (current === temporaryId ? null : current))
          showErrorToast(error, "Le repère n’a pas pu être enregistré.")
        }
      })()
    },
    [patientId, fileId],
  )

  const rename = useCallback(
    (id: string, label: string) => {
      setAnnotations((current) => current.map((one) => (one.id === id ? { ...one, label } : one)))

      // ⚠️ A marker the server has not acknowledged carries a temporary id, so there is nothing to rename yet.
      // Its label travels with the create — which is why `place` reads the default label before sending.
      if (id.startsWith(PENDING_PREFIX)) return

      const existing = renameTimers.current.get(id)
      if (existing) clearTimeout(existing)

      const token = request.current
      renameTimers.current.set(
        id,
        setTimeout(() => {
          renameTimers.current.delete(id)
          void (async () => {
            try {
              await patientFilesApi.renameAnnotation(patientId, fileId, id, label)
            } catch (error) {
              if (token !== request.current) return
              showErrorToast(error, "Le nom du repère n’a pas pu être enregistré.")
            }
          })()
        }, RENAME_DEBOUNCE_MS),
      )
    },
    [patientId, fileId],
  )

  const remove = useCallback(
    (id: string) => {
      const timer = renameTimers.current.get(id)
      if (timer) {
        // A rename still in flight for a marker that is going away would resurrect nothing, but it would put a
        // refusal toast on screen for a marker the reader has already deleted.
        clearTimeout(timer)
        renameTimers.current.delete(id)
      }

      const token = request.current
      let removed: MeshAnnotation | undefined
      setAnnotations((current) => {
        removed = current.find((one) => one.id === id)
        return current.filter((one) => one.id !== id)
      })
      setSelectedId((current) => (current === id ? null : current))

      if (id.startsWith(PENDING_PREFIX)) return

      void (async () => {
        try {
          await patientFilesApi.deleteAnnotation(patientId, fileId, id)
        } catch (error) {
          if (token !== request.current) return
          // Put it back: a marker that disappears from the screen while surviving on the server is a lie the
          // next reader inherits.
          if (removed) setAnnotations((current) => [...current, removed!])
          showErrorToast(error, "Le repère n’a pas pu être supprimé.")
        }
      })()
    },
    [patientId, fileId],
  )

  return { annotations, loading, place, rename, remove, selectedId, select: setSelectedId }
}
