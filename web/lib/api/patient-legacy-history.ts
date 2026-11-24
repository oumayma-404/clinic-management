import { apiGet } from './client';

/**
 * One line of the patient's history as the cabinet's PREVIOUS software recorded it (« Ancien logiciel »).
 * Read-only: written once by the migration import, never edited here, and never a fiche (no money, no chart).
 */
export interface PatientLegacyHistoryEntryDto {
  id: string;
  /** `yyyy-MM-dd`, or null when the old software gave no date. A calendar day — never shift it by a timezone. */
  date: string | null;
  /** The old software's own section word, shown as is (« Consultation », « Ordonnance », « Radio »…). */
  category: string;
  title: string;
  details: string | null;
}

export const patientLegacyHistoryApi = {
  /** Newest day first, undated lines last, the old record's order within a day. */
  list: async (patientId: string): Promise<PatientLegacyHistoryEntryDto[]> => {
    return apiGet<PatientLegacyHistoryEntryDto[]>(`/patients/${patientId}/legacy-history`);
  },
};
