import { apiGet, apiPost, apiDelete } from './client';
import type { ToothStateDto } from './types';

export interface DiagnoseToothRequest {
  toothNumber: number;
  /** ToothCondition enum name (Carie, ATraiter, ExtraitAbsent, …). */
  condition: string;
  surfaces?: string | null;
  note?: string | null;
}

export const odontogramApi = {
  // Returns every recorded tooth-condition entry for the patient (many per tooth) — both charted diagnoses
  // (source "Diagnosis") and completed treatments (source "Treatment", written via the dental-record flow).
  get: async (patientId: string): Promise<ToothStateDto[]> => {
    return apiGet<ToothStateDto[]>(`/patients/${patientId}/odontogram`);
  },

  // Chart a diagnosis directly on a tooth (existing pathology / à traiter), before any treatment.
  diagnose: async (patientId: string, data: DiagnoseToothRequest): Promise<ToothStateDto> => {
    return apiPost<ToothStateDto>(`/patients/${patientId}/odontogram/conditions`, data);
  },

  // Remove a charted diagnosis (diagnosis entries only; treatments are edited via their dental record).
  removeCondition: async (patientId: string, toothStateId: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/odontogram/conditions/${toothStateId}`);
  },
};
