import { apiGet } from './client';

/** What `GET /api/meta/client-requirements` answers (backend `Models/ClientRequirements`). */
export interface ClientRequirementsDto {
  /** Oldest shell this server still answers. Empty = no floor is set, so nothing is refused. */
  minimumShellVersion: string;
  /** The release the stores currently carry. Empty until an operator states one. */
  currentShellVersion: string;
  /** Empty for a platform with no listing yet. */
  storeUrls: { android: string; ios: string };
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
