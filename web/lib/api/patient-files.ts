import { apiGet, apiGetBlob, apiPost, apiPostFormData, apiDelete } from './client';
import type { PatientFileDto, PatientFolderDto } from './types';

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
  // Get folders for a patient
  getFolders: async (patientId: string, parentFolderId?: string): Promise<PatientFolderDto[]> => {
    const params = parentFolderId ? { parentFolderId } : undefined;
    return apiGet<PatientFolderDto[]>(`/patients/${patientId}/files/folders`, params);
  },

  // Get files for a patient
  getFiles: async (patientId: string, folderId?: string): Promise<PatientFileDto[]> => {
    const params = folderId ? { folderId } : undefined;
    return apiGet<PatientFileDto[]>(`/patients/${patientId}/files`, params);
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

  // Download a file
  downloadFile: async (patientId: string, fileId: string): Promise<Blob> => {
    return apiGetBlob(`/patients/${patientId}/files/${fileId}/download`);
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
