"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { patientFilesApi } from "@/lib/api/patient-files"
import { showErrorToast } from "@/lib/errors"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientFileDto } from "@/lib/api/types"

import { isPreviewableFile } from "./file-kind"

/**
 * Opening, holding, navigating and releasing a patient file's preview — **one copy** (AC-5.3).
 *
 * It existed twice, in `patient-files-manager.tsx` and in `app/patients/[id]/page.tsx`, byte for byte down to
 * the object-URL revoke. Only the PDF frame had ever been extracted, so the half that leaks memory when it goes
 * wrong was the half that was duplicated.
 */

/**
 * The list the arrows walk. **Every file is in it, not only the previewable ones** — a STL between two
 * radiographs would otherwise make « suivant » skip a file that is genuinely there, which reads as data loss.
 */
export interface FilePreviewSequence {
  files: PatientFileDto[]
  /** Position of `files[0]` in the whole set, so a paged list can count « 27 / 112 » rather than « 2 / 25 ». */
  offset?: number
  total?: number
  /** Whether an adjacent page exists; the handler turns it and reopens from the far end. */
  hasMoreBefore?: boolean
  hasMoreAfter?: boolean
  onPastStart?: () => void
  onPastEnd?: () => void
}

export interface FilePreview {
  file: PatientFileDto | null
  url: string | null
  loading: boolean
  files: PatientFileDto[]
  /** 1-based across the whole set, 0 when the open file is not in the sequence. */
  position: number
  total: number
  hasPrev: boolean
  hasNext: boolean
  open: (file: PatientFileDto) => void
  close: () => void
  prev: () => void
  next: () => void
}

export function useFilePreview(
  patientId: string,
  policy?: UploadPolicy | null,
  sequence?: FilePreviewSequence,
): FilePreview {
  const [file, setFile] = useState<PatientFileDto | null>(null)
  const [url, setUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // The live URL, for the unmount release — reading it out of state there would capture a stale value and leak
  // the blob for the lifetime of the tab.
  const liveUrl = useRef<string | null>(null)
  useEffect(() => () => {
    if (liveUrl.current) window.URL.revokeObjectURL(liveUrl.current)
  }, [])

  // Read at call time, never through a dependency: both change identity on every render of the caller, and
  // `open` must stay stable for the page-turn effect that depends on it.
  const seq = useRef(sequence)
  seq.current = sequence
  const policyRef = useRef(policy)
  policyRef.current = policy

  /** Discards a download that a faster arrow press has already superseded. */
  const requestId = useRef(0)

  const release = useCallback(() => {
    if (liveUrl.current) {
      window.URL.revokeObjectURL(liveUrl.current)
      liveUrl.current = null
    }
  }, [])

  const close = useCallback(() => {
    requestId.current++
    release()
    setFile(null)
    setUrl(null)
    setLoading(false)
  }, [release])

  const open = useCallback(
    (target: PatientFileDto) => {
      // Releasing here as well as on close is what the arrows made necessary: stepping through a folder would
      // otherwise retain one blob per file visited until the dialog is dismissed.
      release()
      const token = ++requestId.current
      setFile(target)
      setUrl(null)

      if (!isPreviewableFile(target, policyRef.current)) {
        // Nothing to fetch: the dialog opens on its « télécharger pour consulter » branch rather than pulling a
        // 150 MB study the browser cannot paint.
        setLoading(false)
        return
      }

      setLoading(true)
      patientFilesApi
        .downloadFile(patientId, target.id)
        .then((blob) => {
          if (token !== requestId.current) return
          const objectUrl = window.URL.createObjectURL(blob)
          liveUrl.current = objectUrl
          setUrl(objectUrl)
        })
        .catch((error) => {
          if (token !== requestId.current) return
          // The dialog used to close itself with no explanation, which reads as « the click did nothing ».
          showErrorToast(error, "Impossible d'afficher l'aperçu de ce fichier. Essayez de le télécharger.")
          setFile(null)
        })
        .finally(() => {
          if (token === requestId.current) setLoading(false)
        })
    },
    [patientId, release],
  )

  const step = useCallback(
    (delta: -1 | 1) => {
      const current = seq.current
      if (!current || !file) return
      const at = current.files.findIndex((candidate) => candidate.id === file.id)
      if (at < 0) return

      const target = current.files[at + delta]
      if (target) {
        open(target)
        return
      }
      if (delta === 1 && current.hasMoreAfter) current.onPastEnd?.()
      if (delta === -1 && current.hasMoreBefore) current.onPastStart?.()
    },
    [file, open],
  )

  const files = sequence?.files ?? []
  const index = file ? files.findIndex((candidate) => candidate.id === file.id) : -1

  return {
    file,
    url,
    loading,
    files,
    position: index < 0 ? 0 : (sequence?.offset ?? 0) + index + 1,
    total: sequence?.total ?? files.length,
    hasPrev: index > 0 || (index >= 0 && !!sequence?.hasMoreBefore),
    hasNext: index >= 0 && (index < files.length - 1 || !!sequence?.hasMoreAfter),
    open,
    close,
    prev: () => step(-1),
    next: () => step(1),
  }
}
