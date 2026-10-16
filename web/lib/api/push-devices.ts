import { apiDelete, apiGet, apiPost } from './client';

/** Matches the backend `DevicePlatform` enum (Android = 1, Ios = 2). */
export type DevicePlatform = 1 | 2;

/** One platform's verdict on this installation (backend `PushPlatformAvailabilityDto`). */
export interface PushPlatformAvailability {
  platform: DevicePlatform;
  /** « Android » / « iOS » — server-side, so the settings screen and the shell say the same thing. */
  label: string;
  supported: boolean;
  /** French explanation when `supported` is false; null when it is true. */
  reason: string | null;
  registeredDevices: number;
}

/**
 * What `GET /api/push-devices/availability` answers.
 *
 * ⚠️ `platforms` is **never empty** — every platform is always present with its own verdict, so « iOS : non
 * configuré » is a statement rather than an absence. That is why `availableAtAll` is a field of its own rather
 * than something a client derives from the list's length.
 */
export interface PushAvailability {
  availableAtAll: boolean;
  platforms: PushPlatformAvailability[];
}

export const pushDevicesApi = {
  /**
   * Whether this installation can deliver OS notifications, per platform (AC-51, AC-52).
   *
   * ⚠️ This route answers even where push is unavailable — unlike register/deregister, which 404 there. It is the
   * endpoint that *says* push is off, so refusing it would make the one call that can explain the state the one
   * call that cannot be made.
   */
  availability: (): Promise<PushAvailability> => apiGet<PushAvailability>('/push-devices/availability'),

  /** Registers, refreshes or rebinds this phone's token to the signed-in account — one call for all three. */
  register: (body: { platform: DevicePlatform; token: string; shellVersion?: string }): Promise<unknown> =>
    apiPost<unknown>('/push-devices', body),

  /** Sign-out: this phone stops receiving the account's notifications. Idempotent on the server. */
  deregister: (token: string): Promise<unknown> => apiDelete<unknown>(`/push-devices/${encodeURIComponent(token)}`),
};
