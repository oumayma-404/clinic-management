/**
 * What this browser remembers about an upload that did not finish.
 *
 * <p>Within one page it is the queue's own state that survives a dropped connection — `resumable-upload.ts` just
 * carries on. This store is for the other interruption: the tab was closed, the machine slept, the dev server
 * reloaded, and the `File` the user chose is gone with the page. The server still holds the staged parts for
 * twenty-four hours, so the only thing missing is the bytes — and the user has them on their disk.</p>
 *
 * ⚠️ **The bytes are deliberately NOT kept here, and that is the whole design decision.** A `File` is
 * structured-cloneable, so a 150 Mo radiograph could be put in IndexedDB and the resume made seamless. It would
 * also mean a copy of a patient's imaging sitting unencrypted in a shared clinic PC's browser profile, surviving
 * reboots, with no lifecycle beyond our own cleanup — a data-at-rest question this product has no answer for, in
 * exchange for saving one click. So what is stored is a *description*: enough to recognise the file when the
 * user picks it again, and worthless to anyone who reads it.
 *
 * ⚠️ **Nothing here throws.** A private window, cleared site data or a browser refusing IndexedDB all mean « no
 * remembered upload », which is an ordinary state — the same reasoning as `lib/vault/handle.ts`.
 */

const DB_NAME = "clinic-uploads"
const STORE = "interrupted"
const BY_PATIENT = "byPatient"

/**
 * One interrupted upload, as the user needs it described back to them.
 *
 * `fileName`, `fileSize` and `lastModified` are the **raw** properties of the `File` the browser handed us —
 * never the server's sanitised name — because their one job is to recognise the same file when it is picked
 * again. Together they are what a browser can cheaply know about a file's identity: not a guarantee, but a
 * mismatch is certain proof it is a different file, which is the direction that matters.
 */
export interface InterruptedUpload {
  uploadId: string
  patientId: string
  folderId?: string
  fileName: string
  fileSize: number
  lastModified: number
  /** The server's own count at the last confirmed part — what « il en reste 40 Mo » is computed from. */
  receivedBytes: number
  /** ISO-8601 UTC. Past it the staged parts are reclaimed and there is nothing left to resume. */
  expiresAtUtc: string
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) {
        const store = request.result.createObjectStore(STORE, { keyPath: "uploadId" })
        // A patient's file drawer asks « what was interrupted here? », never « what was interrupted anywhere? ».
        store.createIndex(BY_PATIENT, "patientId", { unique: false })
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

function settle<T>(request: IDBRequest<T>, fallback: T): Promise<T> {
  return new Promise((resolve) => {
    request.onsuccess = () => resolve(request.result ?? fallback)
    request.onerror = () => resolve(fallback)
  })
}

/** Records — or updates — an upload in flight. Called on every confirmed part, so the count stays honest. */
export async function rememberUpload(upload: InterruptedUpload): Promise<void> {
  try {
    const db = await openDb()
    db.transaction(STORE, "readwrite").objectStore(STORE).put(upload)
  } catch {
    // The upload still works for this page; it just will not be offered back after a reload.
  }
}

/** Drops one, on success, on abandonment, or once the user has been told it cannot be resumed. */
export async function forgetUpload(uploadId: string): Promise<void> {
  try {
    const db = await openDb()
    db.transaction(STORE, "readwrite").objectStore(STORE).delete(uploadId)
  } catch {
    // Nothing to do: an entry we cannot delete is dropped on the next read for being expired.
  }
}

/**
 * The interrupted uploads worth offering back for this patient, newest first.
 *
 * ⚠️ **Expired entries are deleted rather than returned**, because a session past its life has had its staged
 * parts reclaimed: offering « reprendre » for one would open a session, find nothing, and start the file from
 * zero while telling the user it was continuing. Cleaning up on the read is also the only sweep this store gets —
 * a browser that never comes back is a browser that never accumulates anything either.
 */
export async function interruptedUploadsFor(patientId: string): Promise<InterruptedUpload[]> {
  try {
    const db = await openDb()
    const index = db.transaction(STORE, "readonly").objectStore(STORE).index(BY_PATIENT)
    const found = await settle(index.getAll(patientId) as IDBRequest<InterruptedUpload[]>, [])

    const now = Date.now()
    const live: InterruptedUpload[] = []
    for (const entry of found) {
      if (Date.parse(entry.expiresAtUtc) > now) {
        live.push(entry)
      } else {
        void forgetUpload(entry.uploadId)
      }
    }

    return live.sort((a, b) => Date.parse(b.expiresAtUtc) - Date.parse(a.expiresAtUtc))
  } catch {
    return []
  }
}

/**
 * Whether the file the user just picked is the one that was interrupted.
 *
 * ⚠️ Three properties and not one: a name alone matches « radio.dcm » from a different patient's folder, and a
 * size alone matches nothing a human would recognise. `lastModified` is what catches the case that actually
 * happens — the same file re-exported from the scanner between the two attempts, same name, same folder, and
 * different bytes. Resuming that one would assemble a study half from each export, with the right length in its
 * row and no error anywhere.
 */
export function isSameFile(record: InterruptedUpload, file: File): boolean {
  return (
    record.fileName === file.name &&
    record.fileSize === file.size &&
    record.lastModified === file.lastModified
  )
}
