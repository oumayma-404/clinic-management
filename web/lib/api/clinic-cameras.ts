import { apiDelete, apiGet, apiPost, apiPut } from './client';

/**
 * The cabinet's paired cameras (`features/camera-photo-import`). A camera is « paired » the first time an import meets
 * it, and what the cabinet tells the app about it — whose agenda its photos belong to, how wrong its clock is — is
 * kept on the server so every PC and every browser agrees.
 */
export interface ClinicCameraDto {
  id: string;
  /** SHA-256 of `Make|Model|Serial` — see `lib/photo-import/keys.ts`. */
  fingerprint: string;
  make: string;
  model: string;
  serialSuffix: string | null;
  label: string;
  /** Null = every practitioner's agenda (« Tout le cabinet »). */
  doctorId: string | null;
  /** Minutes to ADD to this camera's clock to read the clinic's. */
  clockOffsetMinutes: number;
  /** A UTC instant. */
  lastImportedAt: string | null;
  /** The same moment as clinic wall-clock — compare it with EXIF times, never `lastImportedAt`. */
  lastImportedAtLocal: string | null;
  version: number;
}

export interface RegisterCameraRequest {
  fingerprint: string;
  make: string;
  model: string;
  serialSuffix?: string | null;
  /** Applied only when the camera is new; a paired camera keeps its own. */
  doctorId?: string | null;
}

/**
 * Tri-state like every update here: omit a key to leave it alone. ⚠️ `doctorId: null` is « Tout le cabinet », which
 * is why the key is only sent when the user changed it.
 */
export interface UpdateCameraRequest {
  label?: string;
  doctorId?: string | null;
  clockOffsetMinutes?: number;
  version: number;
}

export const clinicCamerasApi = {
  list: () => apiGet<ClinicCameraDto[]>('/clinic-cameras'),

  /** Upsert by fingerprint: returns the camera already paired, untouched, when there is one. */
  register: (request: RegisterCameraRequest) => apiPost<ClinicCameraDto>('/clinic-cameras', request),

  update: (cameraId: string, request: UpdateCameraRequest) =>
    apiPut<ClinicCameraDto>(`/clinic-cameras/${cameraId}`, request),

  /** What « Ranger » reports: the latest shot filed (wall-clock) and the clock correction it used. */
  recordImport: (cameraId: string, latestCapturedAtLocal: string | null, clockOffsetMinutes: number | null) =>
    apiPost<ClinicCameraDto>(`/clinic-cameras/${cameraId}/imports`, { latestCapturedAtLocal, clockOffsetMinutes }),

  /** Admin or doctor. Photos keep the model they were taken with. */
  forget: (cameraId: string) => apiDelete<void>(`/clinic-cameras/${cameraId}`),
};
