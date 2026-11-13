import { apiGet, apiPost } from './client';

/**
 * The two reads behind « Importer les photos » (`features/camera-photo-import`). Neither writes: the photos go in
 * through the ordinary patient-file door, which keeps one upload policy, one quota and one audit trail.
 *
 * ⚠️ **Every time here is clinic wall-clock with no offset** (« 2026-11-12T09:00:00 »), the number line a camera's
 * EXIF time lives on. Parse them with `lib/photo-import/wall-clock.ts`, never `new Date(string)` — which would read
 * them in the workstation's zone.
 */
export interface PhotoImportVisitDto {
  appointmentId: string;
  patientId: string;
  patientName: string;
  doctorId: string | null;
  start: string;
  /** Includes the late arrival. */
  expectedEnd: string;
  lateArrivalMinutes: number | null;
  status: string;
}

/** When a fiche was first saved — the evidence of who was really in the chair. */
export interface PhotoImportFicheDto {
  dentalRecordId: string;
  patientId: string;
  patientName: string;
  appointmentId: string | null;
  doctorId: string | null;
  savedAt: string;
}

export interface PhotoImportDoctorDto {
  id: string;
  name: string;
}

export interface PhotoImportContextDto {
  visits: PhotoImportVisitDto[];
  fiches: PhotoImportFicheDto[];
  doctors: PhotoImportDoctorDto[];
  /** The signed-in user's own practitioner record, when they are one — the default agenda for a new camera. */
  myDoctorId: string | null;
  clinicUtcOffsetMinutes: number;
}

/** A shot this cabinet already filed, and where. */
export interface ImportedCaptureDto {
  captureKey: string;
  fileId: string;
  patientId: string;
  patientName: string;
}

/** The server's caps — mirrored here only to split a request, never to refuse one. */
export const PHOTO_IMPORT_LIMITS = { maxRangeDays: 31, maxKeysPerRequest: 2000 } as const;

export const photoImportApi = {
  /** `from`/`to` are clinic days, `YYYY-MM-DD`, inclusive. */
  context: (from: string, to: string, doctorId?: string | null) =>
    apiGet<PhotoImportContextDto>('/photo-import/context', doctorId ? { from, to, doctorId } : { from, to }),

  /** Which of these shots are already filed. Split into requests of at most 2 000 keys. */
  known: async (captureKeys: string[]): Promise<ImportedCaptureDto[]> => {
    const found: ImportedCaptureDto[] = [];
    for (let index = 0; index < captureKeys.length; index += PHOTO_IMPORT_LIMITS.maxKeysPerRequest) {
      const chunk = captureKeys.slice(index, index + PHOTO_IMPORT_LIMITS.maxKeysPerRequest);
      found.push(...(await apiPost<ImportedCaptureDto[]>('/photo-import/known', { captureKeys: chunk })));
    }
    return found;
  },
};

/** Refusal codes the import branches on. Mirrors `CameraImportRefusals` and `ClinicStorageAllowance.FullCode`. */
export const PhotoImportCode = {
  AlreadyImported: 'photo-already-imported',
  StorageFull: 'storage_full',
  CameraNotFound: 'camera-not-found',
} as const;
