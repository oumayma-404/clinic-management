import { createSHA256 } from 'hash-wasm'

import { patientFilesApi } from '@/lib/api/patient-files'
import type { PatientFileDto } from '@/lib/api/types'
import { buildPreview } from '@/lib/files/preview'
import { removeFromVault, writeToVault } from './path'

/**
 * Recording a file that stays at the cabinet.
 *
 * ⚠️ **One pass over the file, and zero bytes on the uplink.** The original is copied disk-to-disk inside the
 * practice while the same blocks feed the hash; what reaches the server is a description and, when one could be
 * made, a preview of a few megabytes. That is what makes a 25 Go study recordable at all — at Tunisia's median
 * uplink, sending it would take about six hours of the cabinet's whole connection.
 *
 * ⚠️ **The id is minted here, before anything is written.** The coffre path is derived from it on both sides, so
 * the browser has to know it to name the file; a server-minted id would name something that is not on the disk.
 */

export interface VaultIngestProgress {
  /** 0 … 1 over the copy, which is the long part. Hashing rides the same pass. */
  copied: number
}

/** Streams `file` into the coffre, hashing as it goes, then registers it. */
export async function ingestIntoVault(
  vault: FileSystemDirectoryHandle,
  patientId: string,
  file: File,
  options: {
    folderId?: string
    description?: string
    onProgress?: (progress: VaultIngestProgress) => void
    signal?: AbortSignal
  } = {},
): Promise<PatientFileDto> {
  const fileId = crypto.randomUUID()
  const hasher = await createSHA256()
  hasher.init()

  const total = file.size || 1

  await writeToVault(
    vault,
    patientId,
    fileId,
    file.name,
    file,
    (written) => options.onProgress?.({ copied: Math.min(1, written / total) }),
    (chunk) => hasher.update(chunk),
  )

  const contentHash = hasher.digest('hex')

  // Best-effort and never load-bearing: a coffre file with no stand-in still registers, and shows its
  // format's icon. Since the decoders landed this is a real picture for TIFF and HEIC too.
  const preview = await buildPreview(file)

  try {
    return await patientFilesApi.registerVaultFile(patientId, {
      fileId,
      fileName: file.name,
      fileSize: file.size,
      contentHash,
      folderId: options.folderId,
      description: options.description,
      preview,
    })
  } catch (error) {
    // The bytes are ours until the row exists. Undo them, or the coffre keeps a file nothing references and the
    // same upload retried would write a second copy under a new id.
    await removeFromVault(vault, patientId, fileId, file.name)
    throw error
  }
}
