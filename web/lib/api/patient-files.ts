import { apiGet, apiDelete, apiHeaders, getAccessToken } from './client';
import type { PatientFileDto, PatientFolderDto } from './types';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

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
    const token = await getAccessToken();
    const response = await fetch(`${API_BASE_URL}/patients/${patientId}/files/folders/initialize-defaults`, {
      method: 'POST',
      headers: apiHeaders(token, 'none'),
      credentials: 'include',
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `HTTP ${response.status}: ${response.statusText}`);
    }

    return response.json();
  },

  // Create a new folder
  createFolder: async (patientId: string, name: string, parentFolderId?: string): Promise<PatientFolderDto> => {
    const token = await getAccessToken();
    const response = await fetch(`${API_BASE_URL}/patients/${patientId}/files/folders`, {
      method: 'POST',
      headers: apiHeaders(token),
      credentials: 'include',
      body: JSON.stringify({ name, parentFolderId }),
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `HTTP ${response.status}: ${response.statusText}`);
    }

    return response.json();
  },

  // Upload a file
  uploadFile: async (
    patientId: string,
    file: File,
    folderId?: string,
    description?: string
  ): Promise<PatientFileDto> => {
    const token = await getAccessToken();
    const formData = new FormData();
    formData.append('file', file);
    if (folderId) {
      formData.append('folderId', folderId);
    }
    if (description) {
      formData.append('description', description);
    }

    const response = await fetch(`${API_BASE_URL}/patients/${patientId}/files/upload`, {
      method: 'POST',
      headers: apiHeaders(token, 'none'),
      credentials: 'include',
      body: formData,
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `HTTP ${response.status}: ${response.statusText}`);
    }

    return response.json();
  },

  // Download a file
  downloadFile: async (patientId: string, fileId: string): Promise<Blob> => {
    const token = await getAccessToken();
    const response = await fetch(`${API_BASE_URL}/patients/${patientId}/files/${fileId}/download`, {
      method: 'GET',
      headers: apiHeaders(token, 'none'),
      credentials: 'include',
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `HTTP ${response.status}: ${response.statusText}`);
    }

    return response.blob();
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

