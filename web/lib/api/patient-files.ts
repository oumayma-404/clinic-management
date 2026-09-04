import { apiGet, apiGetBlob, apiPost, apiPut, apiPostFormData, apiPutBinary, apiDelete } from './client';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';
import { PREVIEW_FILE_NAME } from '@/lib/files/preview';
import type {
  FileUploadSessionDto,
  PatientFileAnnotationDto,
  PatientFileDto,
  PatientFileSummaryDto,
  PatientFolderDto,
} from './types';

/**
 * How the « Fichiers » directory is ordered. The three keys the server accepts, as a union so a typo is a `tsc`
 * error rather than a silent fall-back to alphabetical (the endpoint clamps an unknown value on purpose, which
 * is right for a stale bookmark and wrong for our own code).
 */
export type PatientFileDirectorySort = 'name' | 'files' | 'recent';

/**
 * Patient folders and files.
 *
 * ⚠️ **Every call goes through `client.ts`, and that is the fix for a real defect** — not tidiness. All four of
 * the write/download calls here used to be raw `fetch` with their own error block reading `errorData.message`,
 * while the backend's canonical failure body is `{ error }` (`ApiControllerBase`). So a refused upload — the
 * signature check catching a `.txt` renamed to `.pdf`, say — threw away « Le contenu du fichier ne correspond pas
 * à son format déclaré » and surfaced the English sentinel « HTTP 400: Bad Request » instead. Those calls also
 * had no request deadline (a dead transport froze the drop zone with no toast and no retry) and no 401 retry.
 */
export const patientFilesApi = {
  /**
   * One page of the « Fichiers » directory — every patient of the clinic with the size of their file drawer
   * beside them (`/fichiers`).
   *
   * ⚠️ **Every narrowing decision is the server's.** `search`, `withFilesOnly` and `sort` are all applied before
   * the page is cut, so none of them may be re-applied to the returned rows: filtering an already-cut window
   * means « those of these 25 », which shrinks pages unpredictably and hides every match on another page.
   */
  getPatientSummaries: async (
    params: PageParams & { withFilesOnly?: boolean; sort?: PatientFileDirectorySort },
  ): Promise<PagedResponse<PatientFileSummaryDto>> => {
    const { search, ...rest } = params;
    return apiGet<PagedResponse<PatientFileSummaryDto>>('/patients/file-summaries', {
      ...rest,
      searchTerm: search,
    });
  },

  // Get folders for a patient
  getFolders: async (patientId: string, parentFolderId?: string): Promise<PatientFolderDto[]> => {
    const params = parentFolderId ? { parentFolderId } : undefined;
    return apiGet<PatientFolderDto[]>(`/patients/${patientId}/files/folders`, params);
  },

  /**
   * Every file of the folder (or of the root). Sends no paging parameters, so the single page it unwraps really
   * is everything — the unpaged case the backend models as first-class.
   */
  getFiles: async (patientId: string, folderId?: string): Promise<PatientFileDto[]> => {
    const params = folderId ? { folderId } : undefined;
    return unwrapPaged(await apiGet<PagedResponse<PatientFileDto>>(`/patients/${patientId}/files`, params));
  },

  /** One page of them (AC-5.9) — a patient's drawer is unbounded and used to be fetched whole. */
  getFilesPaged: async (
    patientId: string,
    folderId: string | undefined,
    params: PageParams
  ): Promise<PagedResponse<PatientFileDto>> => {
    return apiGet<PagedResponse<PatientFileDto>>(`/patients/${patientId}/files`, { ...params, folderId });
  },

  // Initialize default folders
  initializeDefaultFolders: async (patientId: string): Promise<PatientFolderDto[]> => {
    // The action takes no body — it reads the patient from the route — so `{}` is sent and ignored.
    return apiPost<PatientFolderDto[]>(`/patients/${patientId}/files/folders/initialize-defaults`, {});
  },

  // Create a new folder
  createFolder: async (patientId: string, name: string, parentFolderId?: string): Promise<PatientFolderDto> => {
    return apiPost<PatientFolderDto>(`/patients/${patientId}/files/folders`, { name, parentFolderId });
  },

  // Upload a file
  /**
   * Upload a file the deployment stores.
   *
   * ⚠️ **`preview` is optional and is never worth failing the upload for.** It is the small stand-in the file
   * list paints; the handler drops an unusable one and stores the file regardless, exactly as the coffre door
   * below does. Before it was sent, no hosted file had one at all — `PreviewStorageKey` was written by the
   * coffre registration alone — so a patient's drawer was a column of grey icons however ordinary the files in
   * it were.
   */
  uploadFile: async (
    patientId: string,
    file: File,
    folderId?: string,
    description?: string,
    preview?: Blob | null
  ): Promise<PatientFileDto> => {
    const formData = new FormData();
    formData.append('file', file);
    if (folderId) {
      formData.append('folderId', folderId);
    }
    if (description) {
      formData.append('description', description);
    }
    if (preview) {
      formData.append('preview', preview, PREVIEW_FILE_NAME);
    }

    return apiPostFormData<PatientFileDto>(`/patients/${patientId}/files/upload`, formData);
  },

  // ── Resumable upload ──────────────────────────────────────────────────────────────────────────────────
  //
  // The same door as `uploadFile`, reached in parts. Five calls, because an upload that survives an interruption
  // has to be a thing the server remembers between requests rather than one request that either finishes or does
  // not. See `lib/files/resumable-upload.ts` for the order they go in — nothing else should call them directly.

  /**
   * Opens an upload and reserves its staging area.
   *
   * ⚠️ **This is where a file is refused**, on its name and its declared length, before a byte is sent — an
   * unsupported format, an oversized one, or one this deployment keeps in the cabinet's coffre instead. The
   * signature cannot be judged yet and is checked against the first chunk.
   */
  startUpload: async (
    patientId: string,
    upload: { fileName: string; fileSize: number; folderId?: string; description?: string },
  ): Promise<FileUploadSessionDto> => {
    return apiPost<FileUploadSessionDto>(`/patients/${patientId}/files/uploads`, upload);
  },

  /**
   * Where an upload got to. ⚠️ **The read that makes resuming honest**: a browser whose connection dropped knows
   * what it sent and not what arrived — the last part may have been stored and its response lost — so the count
   * is asked for rather than assumed. A session that expired reads as gone, because its parts have been reclaimed.
   */
  getUpload: async (patientId: string, uploadId: string): Promise<FileUploadSessionDto> => {
    return apiGet<FileUploadSessionDto>(`/patients/${patientId}/files/uploads/${uploadId}`);
  },

  /**
   * One part, as raw bytes. Parts are **sequential**: `nextPart` from the session is the only one the server
   * accepts, and re-sending the last stored part is a success rather than an error, so a client that lost a
   * response can simply send it again.
   */
  uploadChunk: async (
    patientId: string,
    uploadId: string,
    partNumber: number,
    chunk: Blob,
    signal?: AbortSignal,
  ): Promise<FileUploadSessionDto> => {
    return apiPutBinary<FileUploadSessionDto>(
      `/patients/${patientId}/files/uploads/${uploadId}/chunks/${partNumber}`,
      chunk,
      undefined,
      signal,
    );
  },

  /**
   * Assembles the parts and records the file. The `preview` is the same optional stand-in `uploadFile` takes and
   * is just as much never worth failing the upload for — the original is already staged by this point.
   */
  completeUpload: async (
    patientId: string,
    uploadId: string,
    preview?: Blob | null,
  ): Promise<PatientFileDto> => {
    const formData = new FormData();
    if (preview) {
      formData.append('preview', preview, PREVIEW_FILE_NAME);
    }

    return apiPostFormData<PatientFileDto>(
      `/patients/${patientId}/files/uploads/${uploadId}/complete`,
      formData,
    );
  },

  /** Gives up an upload and releases its parts. An upload already gone answers success. */
  abandonUpload: async (patientId: string, uploadId: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/files/uploads/${uploadId}`);
  },

  /**
   * Record a file whose bytes stay in the cabinet's coffre — a **registration, not an upload**. The original
   * never crosses the wire; what goes up is its description plus, when one could be made, a small preview.
   *
   * ⚠️ `fileId` is minted by the caller because both sides derive the coffre path from it, so the browser must
   * know it before it writes the bytes. The server treats it as untrusted and refuses a repeat.
   */
  registerVaultFile: async (
    patientId: string,
    file: {
      fileId: string
      fileName: string
      fileSize: number
      contentHash: string
      folderId?: string
      description?: string
      preview?: Blob | null
    },
  ): Promise<PatientFileDto> => {
    const formData = new FormData();
    formData.append('fileId', file.fileId);
    formData.append('fileName', file.fileName);
    formData.append('fileSize', String(file.fileSize));
    formData.append('contentHash', file.contentHash);
    if (file.folderId) formData.append('folderId', file.folderId);
    if (file.description) formData.append('description', file.description);
    if (file.preview) formData.append('preview', file.preview, `${file.fileId}.jpg`);

    return apiPostFormData<PatientFileDto>(`/patients/${patientId}/files/vault`, formData);
  },

  // Download a file
  downloadFile: async (patientId: string, fileId: string): Promise<Blob> => {
    return apiGetBlob(`/patients/${patientId}/files/${fileId}/download`);
  },

  /**
   * The stand-in image for a coffre original. ⚠️ A 404 means « no picture of this file », which is ordinary —
   * nothing can decode a DICOM in a browser today — never « something went wrong ».
   */
  downloadPreview: async (patientId: string, fileId: string): Promise<Blob> => {
    return apiGetBlob(`/patients/${patientId}/files/${fileId}/preview`);
  },

  /**
   * Rename / describe / move a file (AC-4.2). **Tri-state**: only the keys you pass are touched, so
   * `{ description: "" }` clears the description and leaves the name and the folder alone. `fileName` is the
   * **base** name — the extension is the stored one and cannot be changed through this call.
   */
  updateFile: async (
    patientId: string,
    fileId: string,
    changes: {
      fileName?: string
      description?: string | null
      folderId?: string | null
      /** The version read from the server. Omitted (or 0) the server skips the check — see `PatientDto.version`. */
      version?: number
    }
  ): Promise<PatientFileDto> => {
    return apiPut<PatientFileDto>(`/patients/${patientId}/files/${fileId}`, changes);
  },

  // Rename a folder
  renameFolder: async (patientId: string, folderId: string, name: string): Promise<PatientFolderDto> => {
    return apiPut<PatientFolderDto>(`/patients/${patientId}/files/folders/${folderId}`, { name });
  },

  // Delete a file
  deleteFile: async (patientId: string, fileId: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/files/${fileId}`);
  },

  // Delete a folder
  deleteFolder: async (patientId: string, folderId: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/files/folders/${folderId}`);
  },

  // ── Repères sur un modèle 3D (mesh-interactive-viewer) ──────────────────────────────────────────────
  //
  // Four calls rather than one « replace the set ». The set-shaped API is less code on both sides, but two
  // people looking at the same model would overwrite each other's markers with the last save winning and
  // nothing anywhere to say a marker had existed. Per-marker writes merge on their own.

  getAnnotations: async (patientId: string, fileId: string): Promise<PatientFileAnnotationDto[]> => {
    return apiGet<PatientFileAnnotationDto[]>(`/patients/${patientId}/files/${fileId}/annotations`);
  },

  createAnnotation: async (
    patientId: string,
    fileId: string,
    marker: {
      x: number
      y: number
      z: number
      normalX: number
      normalY: number
      normalZ: number
      label: string
    }
  ): Promise<PatientFileAnnotationDto> => {
    return apiPost<PatientFileAnnotationDto>(`/patients/${patientId}/files/${fileId}/annotations`, marker);
  },

  renameAnnotation: async (
    patientId: string,
    fileId: string,
    annotationId: string,
    label: string
  ): Promise<PatientFileAnnotationDto> => {
    return apiPut<PatientFileAnnotationDto>(
      `/patients/${patientId}/files/${fileId}/annotations/${annotationId}`,
      { label }
    );
  },

  deleteAnnotation: async (patientId: string, fileId: string, annotationId: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/files/${fileId}/annotations/${annotationId}`);
  },
};
