import { apiGet, apiGetBlob, apiPost, apiPut, apiPostFormData, apiDelete } from './client';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';
import type { PatientFileDto, PatientFileSummaryDto, PatientFolderDto } from './types';

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
  uploadFile: async (
    patientId: string,
    file: File,
    folderId?: string,
    description?: string
  ): Promise<PatientFileDto> => {
    const formData = new FormData();
    formData.append('file', file);
    if (folderId) {
      formData.append('folderId', folderId);
    }
    if (description) {
      formData.append('description', description);
    }

    return apiPostFormData<PatientFileDto>(`/patients/${patientId}/files/upload`, formData);
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
};
