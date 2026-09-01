/**
 * Where a coffre file sits, and how to reach it through a directory handle.
 *
 * ⚠️ **The mirror of the server's `VaultPath`, and it must stay one.** Both compose
 * `coffre/{patientId}/{fileId}.{ext}` from ids the row already carries, so neither side stores a path and
 * neither can drift into pointing somewhere the other does not look. The handle the shell delivers is the
 * `coffre` folder **itself**, so the segments below are relative to it.
 */

/** The extension of a stored file name, dot included and lower-cased; empty when it carries none. */
export function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  if (dot <= 0 || dot === fileName.length - 1) return ''
  return fileName.slice(dot).toLowerCase()
}

/**
 * Where the original sits, written the way the operating system writes it, for showing to a person.
 *
 * ⚠️ **Text, deliberately — not an `href`.** A page served over `https:` cannot link to `file:`; every modern
 * browser ignores the click and says nothing, which would be a control that looks like it works and does not.
 * So this is a path to read, copy and paste into the file manager. A real « ouvrir le dossier » needs the
 * desktop shell to expose a reveal method over the bridge — see `mobile/shared/bridge.md`.
 *
 * The coffre root is whatever folder the practice chose, and the browser is never told its absolute path (the
 * File System Access API hands out a handle and a name, never a location), so the root is named by its handle
 * and the rest is exact.
 */
export function vaultDisplayPath(
  vaultName: string | undefined,
  patientId: string,
  fileId: string,
  fileName: string,
): string {
  const [folder, leaf] = vaultSegments(patientId, fileId, fileName)
  return [vaultName || 'coffre', folder, leaf].join('\\')
}

/** The segments under the coffre root: the patient's folder, then the file. */
export function vaultSegments(patientId: string, fileId: string, fileName: string): [string, string] {
  return [patientId, `${fileId}${extensionOf(fileName)}`]
}

/**
 * The file itself, or null when this machine's coffre does not hold it. ⚠️ **Null is the ordinary answer** —
 * the study is on the machine that recorded it, and a colleague's laptop legitimately has no copy.
 */
export async function findInVault(
  vault: FileSystemDirectoryHandle,
  patientId: string,
  fileId: string,
  fileName: string,
): Promise<File | null> {
  const [folder, leaf] = vaultSegments(patientId, fileId, fileName)

  try {
    const directory = await vault.getDirectoryHandle(folder)
    const handle = await directory.getFileHandle(leaf)
    return await handle.getFile()
  } catch {
    return null
  }
}

/**
 * The file, but only when it is the one the row describes — size is the test, exactly as the patient-file
 * mirror's freshness check is. ⚠️ A **mismatch is « not available here », never « deleted »**: the row stands,
 * the preview still shows, and nothing is repaired behind the user's back.
 */
export async function findVerifiedInVault(
  vault: FileSystemDirectoryHandle,
  patientId: string,
  fileId: string,
  fileName: string,
  expectedSize: number,
): Promise<File | null> {
  const file = await findInVault(vault, patientId, fileId, fileName)
  return file && file.size === expectedSize ? file : null
}

/**
 * Writes the bytes into the coffre, creating the patient's folder on the way.
 *
 * ⚠️ `onChunk` is what keeps this to **one pass over the file**. A 25 Go study is read once and each block is
 * handed to the hasher and to the disk together; hashing separately would mean reading tens of gigabytes twice.
 */
export async function writeToVault(
  vault: FileSystemDirectoryHandle,
  patientId: string,
  fileId: string,
  fileName: string,
  source: File,
  onProgress?: (bytesWritten: number) => void,
  onChunk?: (chunk: Uint8Array) => void,
): Promise<void> {
  const [folder, leaf] = vaultSegments(patientId, fileId, fileName)
  const directory = await vault.getDirectoryHandle(folder, { create: true })
  const handle = await directory.getFileHandle(leaf, { create: true })
  const writable = await handle.createWritable()

  let written = 0
  const reader = source.stream().getReader()

  try {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break

      onChunk?.(value)
      await writable.write(value)
      written += value.byteLength
      onProgress?.(written)
    }

    await writable.close()
  } catch (error) {
    // Never leave a half-written original behind: a truncated file is the same size question away from looking
    // real, and the row that would describe it was never created.
    try {
      await writable.abort()
    } catch {
      // Already closed or already gone.
    }
    await removeFromVault(vault, patientId, fileId, fileName)
    throw error
  }
}

/**
 * Removes a file this session wrote, after the registration failed. ⚠️ **Only ever used to undo our own partial
 * work** — deleting a file in the app leaves the coffre alone, because those bytes are the practice's and are
 * under a ten-to-twenty-year retention duty.
 */
export async function removeFromVault(
  vault: FileSystemDirectoryHandle,
  patientId: string,
  fileId: string,
  fileName: string,
): Promise<void> {
  const [folder, leaf] = vaultSegments(patientId, fileId, fileName)

  try {
    const directory = await vault.getDirectoryHandle(folder)
    await directory.removeEntry(leaf)
  } catch {
    // Nothing was written, or it is already gone.
  }
}
