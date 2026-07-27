import { apiGet, apiPost, apiPut, apiDelete, apiPostFormData, apiPutFormData, getAccessToken } from './client';
import type { MedicalDocumentDto } from './types';

export interface CreateMedicalDocumentRequest {
  patientId: string;
  documentType: string;
  documentDate: string;
  recipientDoctorName?: string;
  recipientDoctorSpecialty?: string;
  contentJson: string;
  clinicName: string;
  clinicAddress: string;
  clinicPhone: string;
  doctorName: string;
  doctorSpecialty: string;
  pdfFile?: File;
  /** Optional link to the documented appointment — filling the record marks it Completed (post-visit review). */
  appointmentId?: string;
}

export interface UpdateMedicalDocumentRequest {
  documentDate: string;
  recipientDoctorName?: string;
  recipientDoctorSpecialty?: string;
  contentJson: string;
  fileId?: string;
  pdfFile?: File;
}

export const medicalDocumentsApi = {
  list: async (patientId?: string, documentType?: string): Promise<MedicalDocumentDto[]> => {
    const params: Record<string, string> = {};
    if (patientId) params.patientId = patientId;
    if (documentType) params.documentType = documentType;
    return apiGet<MedicalDocumentDto[]>('/medical-documents', params);
  },

  get: async (id: string): Promise<MedicalDocumentDto> => {
    return apiGet<MedicalDocumentDto>(`/medical-documents/${id}`);
  },

  create: async (data: CreateMedicalDocumentRequest): Promise<MedicalDocumentDto> => {
    // If PDF file is provided, use FormData
    if (data.pdfFile) {
      const formData = new FormData();
      formData.append('patientId', data.patientId);
      formData.append('documentType', data.documentType);
      formData.append('documentDate', data.documentDate);
      if (data.recipientDoctorName) formData.append('recipientDoctorName', data.recipientDoctorName);
      if (data.recipientDoctorSpecialty) formData.append('recipientDoctorSpecialty', data.recipientDoctorSpecialty);
      formData.append('contentJson', data.contentJson);
      formData.append('clinicName', data.clinicName);
      formData.append('clinicAddress', data.clinicAddress);
      formData.append('clinicPhone', data.clinicPhone);
      formData.append('doctorName', data.doctorName);
      formData.append('doctorSpecialty', data.doctorSpecialty);
      if (data.appointmentId) formData.append('appointmentId', data.appointmentId);
      formData.append('pdfFile', data.pdfFile);
      const result = await apiPostFormData<MedicalDocumentDto>('/medical-documents', formData);
      return result;
    }
    // Otherwise, use JSON
    return apiPost<MedicalDocumentDto>('/medical-documents', data);
  },

  update: async (id: string, data: UpdateMedicalDocumentRequest): Promise<MedicalDocumentDto> => {
    // If PDF file is provided, use FormData
    if (data.pdfFile) {
      const formData = new FormData();
      formData.append('documentDate', data.documentDate);
      if (data.recipientDoctorName) formData.append('recipientDoctorName', data.recipientDoctorName);
      if (data.recipientDoctorSpecialty) formData.append('recipientDoctorSpecialty', data.recipientDoctorSpecialty);
      formData.append('contentJson', data.contentJson);
      if (data.fileId) formData.append('fileId', data.fileId);
      formData.append('pdfFile', data.pdfFile);
      return apiPutFormData<MedicalDocumentDto>(`/medical-documents/${id}`, formData);
    }
    // Otherwise, use JSON
    return apiPut<MedicalDocumentDto>(`/medical-documents/${id}`, data);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/medical-documents/${id}`);
  },

  generatePdf: async (id: string): Promise<{ jobId: string; message: string }> => {
    return apiPost<{ jobId: string; message: string }>(`/medical-documents/${id}/generate-pdf`, {});
  },

  generatePdfForDownload: async (documentData: {
    documentType: string;
    documentDate: string;
    patientName: string;
    patientAge?: string;
    clinicName: string;
    clinicAddress: string;
    clinicPhone: string;
    doctorName: string;
    doctorSpecialty: string;
    recipientDoctorName?: string;
    recipientDoctorSpecialty?: string;
    content: Record<string, string>;
  }): Promise<Blob> => {
    const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
    const token = await getAccessToken();

    const headers: HeadersInit = { 'Content-Type': 'application/json' };
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(`${API_BASE_URL}/medical-documents/generate-pdf-download`, {
      method: 'POST',
      headers,
      credentials: 'include',
      body: JSON.stringify(documentData),
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Failed to generate PDF: ${response.statusText} - ${errorText}`);
    }

    return response.blob();
  },
};

