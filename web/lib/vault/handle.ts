/**
 * The cabinet's coffre, as this browser can reach it.
 *
 * Two ways in, and the order matters. Inside the **desktop shell** the folder arrives already granted through
 * `window.__clinicShellDeliverVault` — the shell creates the handle natively, so there is no picker and no
 * permission prompt. In a plain browser the user picks it once with `showDirectoryPicker()` and the handle is
 * kept in IndexedDB; an **installed** app keeps the grant indefinitely with no prompt on return, and a tab keeps
 * it for the session.
 *
 * ⚠️ **A missing coffre is a first-class state, not an error.** Most machines have none — a phone, a laptop at
 * home, a browser that does not implement the API at all — and on those the app still reads every record and
 * every hosted file. Nothing here throws for want of a vault.
 */

const DB_NAME = 'clinic-vault'
const STORE = 'handles'
const KEY = 'coffre'

/** Whether this browser can hold a coffre at all. False on Safari and on every mobile browser. */
export function vaultSupported(): boolean {
  return typeof window !== 'undefined' && typeof window.showDirectoryPicker === 'function'
}

let shellHandle: FileSystemDirectoryHandle | null = null
const shellWaiters: Array<(handle: FileSystemDirectoryHandle) => void> = []

/**
 * Installs the seam the shell calls. Idempotent, and safe to call before the shell is ready — the shell may
 * deliver at any point after the document is created, so a caller that arrived first waits rather than
 * concluding there is no coffre.
 */
export function listenForShellVault(): void {
  if (typeof window === 'undefined') return

  // The shell may have posted before this bundle evaluated; it parks the handle rather than losing it.
  if (!shellHandle && window.__clinicShellPendingVault) {
    shellHandle = window.__clinicShellPendingVault
  }

  if (window.__clinicShellDeliverVault) return

  window.__clinicShellDeliverVault = (handle: FileSystemDirectoryHandle) => {
    shellHandle = handle
    while (shellWaiters.length > 0) {
      shellWaiters.shift()?.(handle)
    }
  }
}

/** True when this page is running inside a shell that can deliver a coffre. */
export function shellCanDeliverVault(): boolean {
  return typeof window !== 'undefined' && window.__clinicShell?.platform === 'windows'
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) request.result.createObjectStore(STORE)
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function readStoredHandle(): Promise<FileSystemDirectoryHandle | null> {
  try {
    const db = await openDb()
    return await new Promise((resolve) => {
      const request = db.transaction(STORE, 'readonly').objectStore(STORE).get(KEY)
      request.onsuccess = () => resolve((request.result as FileSystemDirectoryHandle) ?? null)
      request.onerror = () => resolve(null)
    })
  } catch {
    // A private window, cleared site data, or a browser refusing IndexedDB. « No coffre » is the right answer.
    return null
  }
}

async function storeHandle(handle: FileSystemDirectoryHandle): Promise<void> {
  try {
    const db = await openDb()
    db.transaction(STORE, 'readwrite').objectStore(STORE).put(handle, KEY)
  } catch {
    // The handle still works for this page; it just will not survive a reload.
  }
}

/** Whether a folder is remembered at all, regardless of whether its permission still stands. */
export async function storedVaultExists(): Promise<boolean> {
  return (await readStoredHandle()) !== null
}

/** Forgets the stored folder. The bytes on disk are untouched — this unpairs, it does not erase. */
export async function forgetVault(): Promise<void> {
  try {
    const db = await openDb()
    db.transaction(STORE, 'readwrite').objectStore(STORE).delete(KEY)
  } catch {
    // Nothing stored, or no IndexedDB. Either way there is nothing to forget.
  }
}

async function stillGranted(handle: FileSystemDirectoryHandle, prompt: boolean): Promise<boolean> {
  // A shell-delivered handle answers 'granted' without a prompt because the native side already granted it.
  const state = await handle.queryPermission({ mode: 'readwrite' })
  if (state === 'granted') return true
  if (!prompt) return false

  return (await handle.requestPermission({ mode: 'readwrite' })) === 'granted'
}

/**
 * What this machine's coffre is right now.
 *
 * ⚠️ **`lapsed` and `none` are different questions, and collapsing them costs a folder re-pick every morning.**
 * A browser drops a File System Access grant once the last tab for the origin closes, so a folder chosen
 * yesterday is still *stored* and merely un-granted today. That is one click to restore (`reconnectVault`),
 * while « no folder has ever been chosen » is the whole picker. Returning `null` for both sent the second
 * journey to everyone.
 */
export type VaultLookup =
  | { kind: 'ready'; handle: FileSystemDirectoryHandle }
  | { kind: 'lapsed' }
  | { kind: 'none' }

/**
 * The coffre this machine already has. **Never prompts** — it is safe on every page load, which is why
 * the file list can ask on mount without a permission dialog appearing over a patient record.
 */
export async function currentVault(): Promise<VaultLookup> {
  if (typeof window === 'undefined') return { kind: 'none' }

  listenForShellVault()

  if (shellHandle) return { kind: 'ready', handle: shellHandle }

  if (shellCanDeliverVault()) {
    // The shell posts the handle shortly after the document is created; a page that mounted first must not
    // conclude there is no coffre. Bounded, so a shell that never delivers degrades to « no coffre ».
    const delivered = await new Promise<FileSystemDirectoryHandle | null>((resolve) => {
      const timer = setTimeout(() => resolve(null), 3000)
      shellWaiters.push((handle) => {
        clearTimeout(timer)
        resolve(handle)
      })
    })
    if (delivered) return { kind: 'ready', handle: delivered }
  }

  const stored = await readStoredHandle()
  if (!stored) return { kind: 'none' }

  return (await stillGranted(stored, false)) ? { kind: 'ready', handle: stored } : { kind: 'lapsed' }
}

/**
 * Asks the user for the coffre folder. Only ever called from a real click — `showDirectoryPicker` requires a
 * user gesture, and a picker opening on its own over a patient's file drawer is not something to arrange.
 */
export async function chooseVault(): Promise<FileSystemDirectoryHandle | null> {
  // Captured rather than re-read after the `vaultSupported()` guard: narrowing does not survive an optional
  // member on a mutable global, and `tsc` is right to say so.
  const picker = typeof window !== 'undefined' ? window.showDirectoryPicker : undefined
  if (!picker) return null

  try {
    const handle = await picker.call(window, { id: 'clinic-coffre', mode: 'readwrite' })
    if (!(await stillGranted(handle, true))) return null

    await storeHandle(handle)
    return handle
  } catch {
    // The user dismissed the picker. Not an error, and nothing to say about it.
    return null
  }
}

/** Re-asks for a stored folder whose permission lapsed. Requires a user gesture, like the picker. */
export async function reconnectVault(): Promise<FileSystemDirectoryHandle | null> {
  const stored = await readStoredHandle()
  if (!stored) return null

  return (await stillGranted(stored, true)) ? stored : null
}
