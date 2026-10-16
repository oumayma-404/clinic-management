import { apiGet } from './client';

/** What `GET /api/meta/client-requirements` answers (backend `Models/ClientRequirements`). */
export interface ClientRequirementsDto {
  /** Oldest shell this server still answers. Empty = no floor is set, so nothing is refused. */
  minimumShellVersion: string;
  /** The release the stores currently carry. Empty until an operator states one. */
  currentShellVersion: string;
  /** Empty for a platform with no listing yet. */
  // `windows` is a plain installer download URL, not a store listing — the desktop shell is distributed by the
  // operator. The web gate never reads it (a browser is never out of date); the WPF shell does, natively.
  storeUrls: { android: string; ios: string; windows: string };
  /**
   * The Android shell's own floor and release. The two top-level versions above are the desktop's, so an Android
   * shell is measured by — and must be told about — this block only. Absent on a server older than the split.
   */
  android?: { minimumVersion: string; currentVersion: string; downloadUrl: string; sha256: string };
}

export const metaApi = {
  /**
   * The client-version floor and where to update (AC-28).
   *
   * ⚠️ Called with an **explicit `null` token**, like the other anonymous routes in `clinics.ts`: this is the one
   * route a refused client can still read, and it must not depend on a session — which is the whole point of the
   * route existing (AC-29). It is also the only `/api` route exempt from the floor, so `<ClientVersionGate>` can
   * ask it *after* the 426 that summoned it.
   */
  clientRequirements: (): Promise<ClientRequirementsDto> =>
    apiGet<ClientRequirementsDto>('/meta/client-requirements', undefined, null),
};
