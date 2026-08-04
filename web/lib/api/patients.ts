import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { CnamInfo, PatientDto, PatientDeletionCheckDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export const patientsApi = {
  /**
   * `createdFrom` / `createdTo` are inclusive registration-date bounds, applied in SQL. They back the dashboard's
   * « Nouveaux patients » drill-through, which must list exactly the patients that KPI counted.
   */
  list: async (params?: {
    searchTerm?: string;
    limit?: number;
    createdFrom?: string;
    createdTo?: string;
  }): Promise<PatientDto[]> => {
    return unwrapPaged(await apiGet<PagedResponse<PatientDto>>('/patients', params));
  },

  /**
   * One page of patients. `search` is matched **server-side across the whole clinic** — never re-filter the
   * returned rows in the browser, or the search silently narrows to the page.
   */
  listPaged: async (
    params: PageParams & {
      searchTerm?: string;
      createdFrom?: string;
      createdTo?: string;
      /**
       * Only patients carrying an active flag. Server-side — it used to be a client-side `.filter()`, which over a
       * page means "the flagged ones among these 25" and hides the flagged patients on every other page.
       */
      flaggedOnly?: boolean;
    },
  ): Promise<PagedResponse<PatientDto>> => {
    const { search, ...rest } = params;
    return apiGet<PagedResponse<PatientDto>>('/patients', { ...rest, searchTerm: search ?? rest.searchTerm });
  },

  get: async (id: string): Promise<PatientDto> => {
    return apiGet<PatientDto>(`/patients/${id}`);
  },

  create: async (data: {
    firstName: string;
    lastName: string;
    dateOfBirth?: string;
    gender?: string;
    /** `"Child"` | `"Adult"`. Omitted ⇒ the server derives it from the date of birth. */
    dentition?: string;
    /** Omit or send null when the patient gave none — the API no longer substitutes a placeholder. */
    email?: string | null;
    phoneNumber?: string | null;
    medicalHistory?: string;
    allergies?: string;
    /** `null` is accepted so one expression can serve create and update — on create it is simply "no address". */
    address?: {
      street: string;
      city: string;
      state: string;
      zipCode: string;
      country?: string;
    } | null;
    insuranceInfo?: {
      provider: string;
      policyNumber: string;
      groupNumber?: string;
      expiryDate?: string;
    };
    /**
     * The CNAM identity block, as the shared `CnamInfo` rather than a re-listed literal. It used to be spelled out
     * inline here, so L10's two ceiling fields typechecked on the update path (which reads `CnamInfo`) and failed on
     * this one — a copy of a shape is a copy that goes one field out of date.
     */
    cnamInfo?: CnamInfo;
    medicalHistoryEntries?: Array<{
      description: string;
      date?: string;
      notes?: string;
    }>;
    familyHistoryEntries?: Array<{
      relationship: string;
      condition: string;
      notes?: string;
    }>;
    emergencyContactName?: string;
    emergencyContactPhone?: string;
    /** « Adressé par » — the referring practitioner, free text. */
    referredBy?: string;
    /** Patient-level notes; `importantNotes` is shown highlighted on the patient's file. */
    notes?: string;
    importantNotes?: string;
    isFlagged?: boolean;
    flagNotes?: string;
  }): Promise<PatientDto> => {
    return apiPost<PatientDto>('/patients', data);
  },

  update: async (
    id: string,
    data: Partial<PatientDto> & { isFlagged?: boolean; flagNotes?: string },
  ): Promise<PatientDto> => {
    return apiPut<PatientDto>(`/patients/${id}`, data);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${id}`);
  },

  /**
   * What blocks this patient's deletion, and whether archiving is available instead. Called when the confirm
   * dialog opens so the user learns the answer before clicking, not after.
   */
  deletionCheck: async (id: string): Promise<PatientDeletionCheckDto> => {
    return apiGet<PatientDeletionCheckDto>(`/patients/${id}/deletion-check`);
  },

  /** Hide a patient from lists, search, recall and every picker without destroying anything. Reversible. */
  archive: async (id: string, reason?: string): Promise<PatientDto> => {
    return apiPost<PatientDto>(`/patients/${id}/archive`, { reason });
  },

  /** Restore an archived patient everywhere. */
  unarchive: async (id: string): Promise<PatientDto> => {
    return apiPost<PatientDto>(`/patients/${id}/unarchive`, {});
  },
};









