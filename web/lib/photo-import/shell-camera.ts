/**
 * The camera half of the Windows app's bridge (`features/camera-photo-import`, phase 2, shell 1.8) — **the only
 * reader of it**. The shell notices a card or a camera, copies the photos onto the clinic PC, hands them to this page
 * as read-only files, keeps the full-size originals and copies them to the archive disk; this page makes every server
 * call that follows (the shell's own credential is refused outside the routes made for it).
 *
 * ⚠️ **Every read is a feature detection.** In a browser, on a phone, and in a Windows app older than 1.8 nothing
 * here exists, and the import screen is exactly phase 1's: the card's folder, picked by hand.
 *
 * ⚠️ **A batch arrives as a message, never as the reply to « Importer ».** File handles travel only with a posted
 * message, so `cameraScan` answers a count while the photos reach `__clinicShellDeliverCamera` — or wait in
 * `__clinicShellPendingCamera` until this module installs it.
 */
import { useEffect, useState, useSyncExternalStore } from "react"
import { toast } from "sonner"
import { ApiError } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
import { photoImportApi } from "@/lib/api/photo-import"
import { addToBatch, emptyBatch, type SkippedKind, type SourceBatch } from "./sources"

export type CameraShell = Required<
  Pick<
    ClinicShell,
    | "cameraStatus"
    | "setCameraAutoImport"
    | "cameraScan"
    | "cameraAck"
    | "cameraOriginalRecorded"
    | "ignoreCamera"
    | "openOriginal"
    | "originalsHeld"
    | "copyOriginalsNow"
  >
>

/** The Windows app's camera methods, or null wherever they do not exist. */
export function cameraShell(): CameraShell | null {
  if (typeof window === "undefined") return null
  const shell = window.__clinicShell
  return shell && typeof shell.cameraScan === "function" && typeof shell.cameraAck === "function"
    ? (shell as CameraShell)
    : null
}

interface ShellCameraState {
  /** A batch delivered and not started yet. */
  batch: SourceBatch | null
  /** The last device a notice was raised for. */
  detected: { sourceKey: string; label: string; at: number } | null
  /** « Importer » is copying photos off the source, or the page is opening what the shell posted. */
  busy: boolean
  /** How far the copy is (`camera-progress`, at most once a second) — null before the first count and after it ends. */
  progress: { copied: number; total: number } | null
}

const SERVER_STATE: ShellCameraState = { batch: null, detected: null, busy: false, progress: null }
let state: ShellCameraState = { batch: null, detected: null, busy: false, progress: null }
const listeners = new Set<() => void>()

function set(patch: Partial<ShellCameraState>) {
  state = { ...state, ...patch }
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useShellCamera(): ShellCameraState {
  return useSyncExternalStore(subscribe, () => state, () => SERVER_STATE)
}

/** Whether this page runs in a Windows app that imports from a camera — false on the server and in every browser. */
export function useCameraShell(): CameraShell | null {
  const [shell, setShell] = useState<CameraShell | null>(null)
  useEffect(() => setShell(cameraShell()), [])
  return shell
}

let pendingScan: Promise<void> | null = null

/**
 * « Importer » / « Chercher l'appareil » / « plus anciennes » — the shell copies the photos and posts them; this waits
 * for the reply and says in French when nothing came. One at a time: a second press joins the first.
 */
export function scanShellCamera(request: { sourceKey?: string; includeOlder?: boolean } = {}): Promise<void> {
  const shell = cameraShell()
  if (!shell) return Promise.resolve()
  if (pendingScan) return pendingScan

  set({ busy: true, progress: null })
  pendingScan = (async () => {
    try {
      const result = await shell.cameraScan(request)
      if (result.delivered === 0 && result.sentence) toast.info(result.sentence)
    } catch {
      toast.info("L'appareil ne répond plus. Débranchez-le, rebranchez-le, puis réessayez.")
    } finally {
      pendingScan = null
      set({ progress: null })
      // A batch posted before the reply is still being opened (`receiveBatch`); it clears `busy` itself.
      if (!receiving) set({ busy: false })
    }
  })()
  return pendingScan
}

let receiving = false

/** Hands the waiting batch over, once. */
export function takeShellBatch(): SourceBatch | null {
  const batch = state.batch
  if (batch) set({ batch: null })
  return batch
}

/** The page's answer about one photo it was handed. Silent where there is no shell or the photo was not the shell's. */
export function ackShellPhoto(
  itemId: string | null,
  outcome: "filed" | "already" | "skipped" | "failed",
  filed?: { fileId: string; patientId: string; patientName: string; keepOriginal: boolean },
): void {
  if (!itemId) return
  cameraShell()?.cameraAck({ itemId, outcome, ...filed })
}

// ── Messages from the shell ────────────────────────────────────────────────────────────────────────────────────

interface BatchPayload {
  kind: "camera-batch"
  batchId: string
  label: string
  older: number
  failed: number
  skipped: Record<SkippedKind, number>
  items: Array<{ itemId: string; name: string }>
}

async function receiveBatch(payload: BatchPayload, handles: unknown[]) {
  receiving = true
  set({ busy: true })
  const batch = emptyBatch()
  const itemIds = new Map<File, string>()

  for (let index = 0; index < payload.items.length; index++) {
    const handle = handles[index] as FileSystemFileHandle | undefined
    if (!handle || typeof handle.getFile !== "function") continue
    try {
      const file = await handle.getFile()
      const before = batch.photos.length
      addToBatch(batch, file)
      if (batch.photos.length > before) itemIds.set(file, payload.items[index].itemId)
    } catch {
      // A copy that vanished between the shell's post and this read: it is offered again next time.
    }
  }

  for (const kind of ["raw", "heic", "video", "other"] as const) batch.skipped[kind] += payload.skipped?.[kind] ?? 0
  batch.shell = {
    batchId: payload.batchId,
    label: payload.label,
    older: payload.older ?? 0,
    failed: payload.failed ?? 0,
    itemIds,
  }

  receiving = false
  set({ batch, busy: pendingScan !== null })
  return batch
}

/**
 * The server writes down that this PC holds the original (« Original : PC ACCUEIL »). A file deleted meanwhile has
 * nothing to record — told « done » too, or the shell would offer it on every page load for ever.
 */
async function recordKept(payload: { fileId: string; patientId: string; sha256: string; sizeBytes: number; heldBy: string }) {
  const shell = cameraShell()
  try {
    await patientFilesApi.recordOriginal(payload.patientId, payload.fileId, {
      sha256: payload.sha256,
      sizeBytes: payload.sizeBytes,
      heldBy: payload.heldBy,
    })
    shell?.cameraOriginalRecorded(payload.fileId)
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) shell?.cameraOriginalRecorded(payload.fileId)
    // Anything else (offline, a session that ended): the shell sends it again at the next page load.
  }
}

/** One sentence per reason and per session — forty photos refused for one full disk are one problem. */
const refusalsShown = new Set<string>()

/**
 * Installs the seam the shell posts to and the one a Windows notification opens. Idempotent; returns the undo.
 * `openImport` navigates to the import screen for a device (a notice's « Importer »).
 */
export function installShellCamera(openImport: (sourceKey: string | null) => void): () => void {
  if (typeof window === "undefined" || !cameraShell()) return () => {}

  const deliver = (payload: unknown, handles: unknown[]) => {
    const message = payload as { kind?: string } | null
    switch (message?.kind) {
      case "camera-batch":
        void receiveBatch(message as BatchPayload, handles ?? [])
        break
      case "camera-detected": {
        const { sourceKey, label } = message as unknown as { sourceKey: string; label: string }
        set({ detected: { sourceKey, label, at: Date.now() } })
        break
      }
      case "camera-progress": {
        // The shell's own script already restarted its wait; this is only the count. A late one is ignored.
        const { copied, total } = message as unknown as { copied: number; total: number }
        if (pendingScan && Number.isFinite(copied) && Number.isFinite(total) && total > 0) {
          set({ progress: { copied: Math.min(copied, total), total } })
        }
        break
      }
      case "camera-original-kept":
        void recordKept(message as unknown as Parameters<typeof recordKept>[0])
        break
      case "camera-original-refused": {
        const { sentence } = message as unknown as { sentence: string }
        if (sentence && !refusalsShown.has(sentence)) {
          refusalsShown.add(sentence)
          toast.warning(sentence)
        }
        break
      }
      case "originals-copy": {
        const { heldBy, files, bytes } = message as unknown as { heldBy: string; files: number; bytes: number }
        // The server compares the figures with what it has on record for this PC; only then does the alert clear.
        void photoImportApi.reportOriginalsCopy({ heldBy, fileCount: files, totalBytes: bytes }).catch(() => undefined)
        break
      }
    }
  }

  window.__clinicShellDeliverCamera = deliver
  const parked = window.__clinicShellPendingCamera ?? []
  window.__clinicShellPendingCamera = []
  for (const message of parked) deliver(message.payload, message.handles)

  const open = (sourceKey: string) => openImport(sourceKey || null)
  window.__clinicShellOpenCameraImport = open

  return () => {
    if (window.__clinicShellDeliverCamera === deliver) delete window.__clinicShellDeliverCamera
    if (window.__clinicShellOpenCameraImport === open) delete window.__clinicShellOpenCameraImport
  }
}

/**
 * Whether THIS PC keeps the full-size original of a camera photo — the cloud's copy is 2 560 px, so « Ouvrir
 * l'original » is offered exactly where the bytes are. False everywhere else, and while the answer is pending.
 */
export function useOriginalOnThisPc(file: { id: string; origin?: string | null } | null): boolean {
  const [held, setHeld] = useState(false)
  const fileId = file?.id ?? null
  const isCamera = file?.origin === "Camera"

  useEffect(() => {
    setHeld(false)
    const shell = cameraShell()
    if (!shell || !fileId || !isCamera) return
    let live = true
    void shell.originalsHeld([fileId]).then((ids) => {
      if (live) setHeld(ids.includes(fileId))
    })
    return () => {
      live = false
    }
  }, [fileId, isCamera])

  return held
}

/** Opens a kept original in Windows' own viewer, or says it is no longer on this PC. */
export async function openOriginalOnThisPc(fileId: string): Promise<void> {
  const shell = cameraShell()
  if (!shell) return
  if (!(await shell.openOriginal(fileId))) toast.error("L'original n'est plus sur ce PC.")
}

/** Forgets the notice — answered, or ignored. */
export function clearDetected() {
  set({ detected: null })
}
