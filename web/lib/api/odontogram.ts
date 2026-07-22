import { apiGet } from './client';
import type { ToothStateDto } from './types';

export const odontogramApi = {
  // Returns every recorded tooth-condition entry for the patient (many per tooth — one per treatment).
  // Read-only: conditions are written through the dental-record create/update endpoints, not here.
  get: async (patientId: string): Promise<ToothStateDto[]> => {
    return apiGet<ToothStateDto[]>(`/patients/${patientId}/odontogram`);
  },
};
