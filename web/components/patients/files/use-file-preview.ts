"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { patientFilesApi } from "@/lib/api/patient-files"
import {
  DICOM_ADVISORY,
  decodeForViewing,
  decoderFor,
  decodesToImage,
  decodesWithoutAsking,
  type ArchiveListing,
} from "@/lib/files/decoders"
import { findVerifiedInVault } from "@/lib/vault/path"
import { showErrorToast } from "@/lib/errors"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientFileDto } from "@/lib/api/types"

import { previewMode } from "./file-kind"

/**
 * Opening, holding, navigating and releasing a patient file's preview — **one copy** (AC-5.3).
 *
 * It existed twice, in `patient-files-manager.tsx` and in `app/patients/[id]/page.tsx`, byte for byte down to
 * the object-URL revoke. Only the PDF frame had ever been extracted, so the half that leaks memory when it goes
 * wrong was the half that was duplicated.
 *
 * <p>⚠️ **Where the bytes come from depends on the file's residency, and getting that wrong showed « ce format
 * ne s'affiche pas » on the one machine holding the file.** A coffre original never reached the server, so
 * asking the server for it can only fail; it is read from the paired folder instead, and on a machine with no
 * copy the dialog says where it is rather than reporting a failure.</p>
 *
 * <p>⚠️ **A format with no decoder never fetches anything.** The gate is `previewMode`, so a `.docx` is not
 * pulled across a clinic's uplink to discover that nothing can paint it.</p>
 *
 * <p>⚠️ **And a format that HAS a decoder still may not fetch, if it has a viewer of its own.** STL, PLY, OBJ
 * and DICOM can all be turned into a still picture now, but doing it automatically means spending the whole
 * download for every file somebody arrows past — so above `AUTO_DECODE_MAX_BYTES` the dialog stops at
 * « viewer-only » and lets the « Visionneuse » button fetch the bytes deliberately. This used to read « a
 * 150 Mo STL is not pulled across a clinic's uplink to discover that nothing can paint it »; something can
 * paint it now, and the bandwidth argument survived the reason for it.</p>
 *
 * <p>⚠️ **A file that needs a decoder shows its stored stand-in FIRST, and this is not an optimisation — it
 * is the difference between usable and not.** Measured in Chrome on a 51 Mpx HEIF: libheif takes **11 seconds**
 * to decode it. That is libheif's own work, not the resize or the re-encode (1171 ms and 91 ms respectively),
 * so no amount of tuning the pipeline touches it — and a dentist opening a photo met eleven seconds of spinner.
 * The stand-in is a ~200 Ko JPEG the server already holds, painted in about a fifth of a second.</p>
 *
 * <p>⚠️ **The original is still reachable, deliberately** (§ 0: no capability removed by a performance
 * decision). `showFullResolution` runs the decode on demand, and the dialog offers it as a button — rather than
 * running it automatically in the background, which would spend eleven seconds and several hundred megabytes
 * per file arrowed past, for a difference nobody asked to see.</p>
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

/** What the dialog should draw, once the bytes are in and any decoder has run. */
export type PreviewRender = "image" | "pdf" | "archive" | "none"

/** Which wait the user is in, so the spinner can say which. */
export type PreviewStage = "idle" | "fetching" | "decoding"

/** Why nothing is shown — so the dialog can say which, rather than one sentence covering both. */
export type PreviewUnavailable =
  /** A coffre original, and this machine is not the one holding it. */
  | "elsewhere"
  /** The bytes are here and nothing in this build can turn them into something to look at. */
  | "undecodable"
  /**
   * Something CAN show it, but not for free and not unasked: a large model or study with no stored stand-in,
   * whose own viewer is one tap away. ⚠️ Distinct from `undecodable` because the action is opposite — « open
   * the viewer », not « download it and use another program ».
   */
  | "viewer-only"

export interface FilePreview {
  file: PatientFileDto | null
  /** A `blob:` URL for the `image` and `pdf` renders; null for every other. */
  url: string | null
  /** The archive's index, for the `archive` render. */
  archive: ArchiveListing | null
  render: PreviewRender
  unavailable: PreviewUnavailable | null
  /**
   * A sentence the viewer is obliged to show beside the picture. Set for DICOM on **both** paths — the stored
   * stand-in was produced by the same windowing as a fresh decode, so it carries the same caveat.
   */
  advisory: string | null
  loading: boolean
  /** What the spinner should say — a download and an eleven-second decode are not the same wait. */
  stage: PreviewStage
  /**
   * Set when what is on screen is the server's stand-in rather than the original, and the original could be
   * decoded on request. Null whenever there is nothing better to offer.
   */
  showFullResolution: (() => void) | null
  /**
   * The open file's own bytes, from wherever they actually are — null meaning « not on this machine ».
   *
   * ⚠️ **Exposed so a richer viewer can have the original without a second copy of the residency rule.** The
   * DICOM study viewer needs the whole file, and the fast path deliberately never downloads it, so there is
   * nothing to hand over — but a caller fetching it itself would be a second place deciding that a coffre
   * original is read from disk and a hosted one from the server. It is also why the viewer does not take a
   * `vault` prop: the handle stays here, where it already was.
   */
  loadSource: () => Promise<Blob | null>
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
  /** This machine's coffre, when the screen has one. Without it a coffre original reads as « elsewhere ». */
  vault?: FileSystemDirectoryHandle | null,
): FilePreview {
  const [file, setFile] = useState<PatientFileDto | null>(null)
  const [url, setUrl] = useState<string | null>(null)
  const [archive, setArchive] = useState<ArchiveListing | null>(null)
  const [render, setRender] = useState<PreviewRender>("none")
  const [unavailable, setUnavailable] = useState<PreviewUnavailable | null>(null)
  const [advisory, setAdvisory] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [stage, setStage] = useState<PreviewStage>("idle")

  /** The file whose stand-in is on screen, and whose original could still be decoded on request. */
  const [standingIn, setStandingIn] = useState<PatientFileDto | null>(null)

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
  const vaultRef = useRef(vault)
  vaultRef.current = vault

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
    setArchive(null)
    setRender("none")
    setUnavailable(null)
    setAdvisory(null)
    setLoading(false)
    setStage("idle")
    setStandingIn(null)
  }, [release])

  /**
   * Fetches the original and runs the decoder over it. The slow path — on a 51 Mpx HEIF this is about eleven
   * seconds of libheif, which is why it is not what opening a file does by default.
   */
  const decodeOriginal = useCallback(
    async (target: PatientFileDto, token: number) => {
      setStage("fetching")
      const source = await sourceBytes(patientId, target, vaultRef.current ?? null)
      if (token !== requestId.current) return

      if (!source) {
        // ⚠️ Not an error, and not « undecodable » either. A coffre original lives on the machine that
        // recorded it, and a colleague's laptop legitimately has no copy.
        setUnavailable("elsewhere")
        return
      }

      setStage("decoding")
      const decoded = await decodeForViewing(source, target.fileName)
      if (token !== requestId.current) return

      if (!decoded) {
        setUnavailable("undecodable")
        return
      }

      if (decoded.kind === "archive") {
        setArchive(decoded)
        setRender("archive")
        return
      }

      release()
      liveUrl.current = window.URL.createObjectURL(decoded.blob)
      setUrl(liveUrl.current)
      setRender("image")
      setAdvisory(decoded.advisory ?? null)
      setStandingIn(null)
    },
    [patientId, release],
  )

  const open = useCallback(
    (target: PatientFileDto) => {
      // Releasing here as well as on close is what the arrows made necessary: stepping through a folder would
      // otherwise retain one blob per file visited until the dialog is dismissed.
      release()
      const token = ++requestId.current
      setFile(target)
      setUrl(null)
      setArchive(null)
      setRender("none")
      setUnavailable(null)
      setAdvisory(null)
      setStandingIn(null)

      const mode = previewMode(target, policyRef.current)
      if (mode === "none") {
        // Nothing to fetch: the dialog opens on its « télécharger pour consulter » branch rather than pulling a
        // 150 MB study nothing in this build can paint.
        setLoading(false)
        setStage("idle")
        return
      }

      setLoading(true)
      void (async () => {
        try {
          // ⚠️ **The fast path, and it is the difference between usable and not.** A format needing a decoder
          // whose stand-in the server already holds is painted from that stand-in — a ~200 Ko JPEG — instead of
          // spending eleven seconds decoding the original for a dialog a thousand pixels wide.
          if (mode === "decode" && decodesToImage(target.fileName) && target.hasPreview) {
            try {
              setStage("fetching")
              const standIn = await patientFilesApi.downloadPreview(patientId, target.id)
              if (token !== requestId.current) return

              liveUrl.current = window.URL.createObjectURL(standIn)
              setUrl(liveUrl.current)
              setRender("image")
              // ⚠️ The stand-in for a DICOM was built by the SAME windowing as a fresh decode, so it carries
              // the same caveat — and this path never loads the decoder, so it cannot be told by one. Without
              // the frame count, which only parsing the file could give.
              setAdvisory(decoderFor(target.fileName) === "dicom" ? DICOM_ADVISORY : null)
              setStandingIn(target)
              return
            } catch {
              // The stand-in is a courtesy: a row whose blob is missing, or a server that refused, simply falls
              // through to the original. Never a toast — nothing has failed from the reader's point of view.
              if (token !== requestId.current) return
            }
          }

          // ⚠️ Reached with no stand-in in hand — either the file has none, or fetching it failed. A large
          // model or study is NOT downloaded here just to make a still picture: its own viewer is one tap away
          // and will fetch the same bytes on purpose. See `decodesWithoutAsking`.
          if (!decodesWithoutAsking(target.fileName, target.fileSize)) {
            setUnavailable("viewer-only")
            setRender("none")
            return
          }

          await decodeOriginal(target, token)
        } catch (error) {
          if (token !== requestId.current) return
          // The dialog used to close itself with no explanation, which reads as « the click did nothing ».
          showErrorToast(error, "Impossible d'afficher l'aperçu de ce fichier. Essayez de le télécharger.")
          setFile(null)
        } finally {
          if (token === requestId.current) {
            setLoading(false)
            setStage("idle")
          }
        }
      })()
    },
    [patientId, release, decodeOriginal],
  )

  /** Runs the decode the fast path skipped, for the file currently on screen. */
  const showFullResolution = useCallback(() => {
    const target = standingIn
    if (!target) return

    const token = ++requestId.current
    setLoading(true)
    void (async () => {
      try {
        await decodeOriginal(target, token)
      } catch (error) {
        if (token !== requestId.current) return
        showErrorToast(error, "Impossible d'afficher cette image en pleine résolution.")
      } finally {
        if (token === requestId.current) {
          setLoading(false)
          setStage("idle")
        }
      }
    })()
  }, [standingIn, decodeOriginal])

  const loadSource = useCallback(async () => {
    if (!file) return null
    return sourceBytes(patientId, file, vaultRef.current ?? null)
  }, [patientId, file])

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
    archive,
    render,
    unavailable,
    advisory,
    loading,
    stage,
    showFullResolution: standingIn ? showFullResolution : null,
    loadSource,
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

/**
 * The file's bytes, from wherever they actually are.
 *
 * ⚠️ **A coffre original is read from the disk it never left** — the same rule the download path follows
 * (AC-9). Asking the server for one can only 404, and at Tunisia's median uplink a 400 Mo study would come back
 * down a wire it never went up. Null means « not on this machine », which is an ordinary answer.
 */
async function sourceBytes(
  patientId: string,
  file: PatientFileDto,
  vault: FileSystemDirectoryHandle | null,
): Promise<Blob | null> {
  if (file.residency === "Vault") {
    if (!vault) return null
    return findVerifiedInVault(vault, patientId, file.id, file.fileName, file.fileSize)
  }

  return patientFilesApi.downloadFile(patientId, file.id)
}
