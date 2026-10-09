import { apiDelete, apiGet, apiPost } from './client';
import type { PagedResponse } from './paging';

/**
 * The PC de secours's state as « Paramètres → PC de secours » reads it (`clinic-pc-copy` AC-2.1). Mirrors the backend
 * `RelayStatusDto`. The sentence is built server-side in the clinic's own clock (FR-2), so it is shown verbatim.
 */
export interface RelayStatusDto {
  exists: boolean;
  relayId?: string | null;
  label?: string | null;
  /** One of {@link RelayStateKey}. */
  state: RelayStateKey;
  sentence: string;
  /** A state admins are told about (off, late, disk nearly full, not matching, a failed setup). */
  isProblem: boolean;
  sinceUtc?: string | null;
  seedPercent?: number | null;
  lanAddresses: string[];
  certificateFingerprint?: string | null;
  pairedAtUtc?: string | null;
  seededAtUtc?: string | null;
  retiredAtUtc?: string | null;
  filesTotal: number;
  filesCopied: number;
  diskFreeBytes?: number | null;
  /** Retired by « Déclarer perdu ou volé » (AC-8.4) — there is nothing left to declare. */
  lostOrStolen: boolean;
  /** Whether a PC de secours may be set up now (AC-1.10): none, retired, or a setup that lapsed. */
  canInstall?: boolean;
  /** The free space a PC de secours needs (AC-1.8), when `canInstall`; 0 otherwise. */
  needBytes?: number;
  /** The cabinet's saves are refused on the cloud now (D13): its PC fell silent armed, or said it holds them. */
  cloudLocked?: boolean;
  lockedSinceUtc?: string | null;
  /** The PC said it holds the cabinet's saves (a cut), rather than falling silent. */
  pcHolding?: boolean;
  /** Why the saves are refused, in the cabinet's clock — shown verbatim above « Reprendre la main ». */
  lockSentence?: string | null;
  /** AC-7.1's warning, shown before « Reprendre la main » and before a retire or a loss while locked (AC-8.6). */
  reclaimWarning?: string | null;
  /** D18 / AC-5.6: lines of « Modifications à vérifier » nobody has marked « Vu ». */
  reviewPending?: number;
  /** US-7 / AC-7.4: records of « À reprendre » nobody has marked « Repris ». */
  reEnterPending?: number;
}

/**
 * One line of « Modifications à vérifier » or « À reprendre » (`RelayReviewItemDto`). Each side's version is one French
 * line built server-side (`cloudSummary`, `cabinetSummary`) — shown verbatim, never the raw JSON beside it.
 */
export interface RelayReviewItemDto {
  id: string;
  /** `CloudOnly` | `BothChanged` | `ProbableDuplicate` | `ToReEnter` | `KeptAfterRestore` — branched on, never the label. */
  kind: string;
  kindLabel: string;
  table: string;
  tableLabel: string;
  entityKey: string;
  cloudEntityKey?: string | null;
  /** Null when the cloud has no such record (created on the PC, or deleted in the cloud). */
  cloudSummary?: string | null;
  /** Null when the cabinet's side is a deletion, or for a change made in the cloud only. */
  cabinetSummary?: string | null;
  /** AC-7.5: a document numbered on an overruled PC, to be redone. */
  warning?: string | null;
  cloudVersion?: string | null;
  cabinetVersion?: string | null;
  cloudChangedAtUtc?: string | null;
  cloudChangedBy?: string | null;
  cabinetChangedAtUtc?: string | null;
  cabinetChangedBy?: string | null;
  createdAtUtc: string;
  reviewedAtUtc?: string | null;
}

/** A one-time setup code (AC-1.4), handed straight to the Windows app — shown to nobody. */
export interface RelayPairingCodeDto {
  relayId: string;
  code: string;
  expiresAtUtc: string;
  /** The room the installer refuses below (`/NEEDBYTES=`), the cloud's own figure. */
  needBytes: number;
}

/** `RelayLabels.Key` on the server — the state names the card branches on, never the sentence. */
export type RelayStateKey =
  | 'none'
  | 'installing'
  | 'install-failed'
  | 'abandoned'
  | 'ready'
  | 'late'
  | 'off'
  | 'updating'
  | 'disk-nearly-full'
  | 'mismatch'
  | 'retired'
  /** The cloud went back to an older state; the PC stopped copying to lose nothing (AC-9.4). */
  | 'stopped'
  /** D20b: the PC's clock is wrong and it cannot set it — it would not take over. */
  | 'clock-wrong';

/**
 * AC-6.2: what a Windows or Android app tries while the cloud is locked for a silent PC de secours. `probe` is false
 * unless the cloud is locked, the PC said nothing, and this request came from the cabinet's own internet line.
 */
export interface RelayDeviceTargetDto {
  probe: boolean;
  addresses: string[];
  port?: number | null;
  certificateFingerprint?: string | null;
  /** When to ask again: half a minute while the cabinet has a PC de secours, ten when it has none. */
  intervalSeconds: number;
}

/** `counted`: this device was on the cabinet's network during a lock. `unlocked`: its report ended the lock. */
export interface RelayDeviceReportDto {
  counted: boolean;
  unlocked: boolean;
}

/** The PC de secours's own view of itself, read on that PC (`RelayLocalStatusDto`). */
export interface RelayLocalStatusDto {
  retired: boolean;
  retiredAtUtc?: string | null;
  /** Built server-side in the clinic's clock: shown verbatim. */
  sentence: string;
}

/** The card's anchor on « Paramètres », shared with the bell's deep link so the two cannot drift. */
export const RELAY_CARD_ID = "pc-de-secours";

export const relayApi = {
  /** ⚠️ A 404 means this deployment has no PC de secours at all; any other failure is a retryable read (AC-2.4). */
  status: () => apiGet<RelayStatusDto>('/relay/status'),

  /** On the PC de secours itself (AC-8.1): still following, or retired since a day. 404 on every other install. */
  local: () => apiGet<RelayLocalStatusDto>('/relay/local'),

  /**
   * « Effacer la copie » on a retired PC (AC-8.2) — needs a step-up token for {@link RELAY_ERASE_STEP_UP}. Every record
   * and file of the cabinet leaves this PC, the caller's own account included: the session ends with the answer.
   */
  eraseLocal: (stepUpToken: string) =>
    apiPost<{ erased: boolean; filesDeleted: number }>('/relay/local/erase', {}, undefined, stepUpToken),

  /** « Oui » on the offer (AC-1.4) — needs a step-up token for {@link RELAY_PAIRING_STEP_UP}. */
  issuePairingCode: (label: string, stepUpToken: string) =>
    apiPost<RelayPairingCodeDto>('/relay/pairing-codes', { label }, undefined, stepUpToken),

  /**
   * « Installer le PC de secours ici… » on any PC (AC-1.5): an admin's email, password and code, whoever is signed in.
   * ⚠️ Sent with **no** session token (`null`): the door is the sign-in's own check, and a refusal here must never be
   * read as the signed-in secretary's session expiring.
   */
  issuePairingCodeWithCredentials: (body: { email: string; password: string; totpCode: string; label: string }) =>
    apiPost<RelayPairingCodeDto>('/auth/relay-pairing-code', body, null),

  /**
   * Gives back a code the installer never presented (AC-1.11), so the clinic's place is free at once. The code is the
   * credential; every outcome is the same 204, and a failure here costs nothing — the code lapses in ten minutes.
   */
  releasePairingCode: (code: string) => apiPost<void>('/relay/pairing-codes/release', { code }, null),

  /** « Retirer ce PC » (AC-8.1): the copy stops and the clinic may set up another. Returns the new state. */
  retire: () => apiDelete<RelayStatusDto>('/relay'),

  /**
   * « Déclarer perdu ou volé » (AC-8.4) — needs a step-up token for {@link RELAY_LOST_STEP_UP}. Every account of the
   * cabinet, the caller's included, must then choose a new password: the caller's session ends with the answer.
   */
  declareLost: (stepUpToken: string) => apiPost<RelayStatusDto>('/relay/lost', {}, undefined, stepUpToken),
  /**
   * « Reprendre la main » (US-7) — needs a step-up token for {@link RELAY_RECLAIM_STEP_UP}. Refused 409
   * `relay_not_holding` when the cloud is not locked.
   */
  reclaim: (stepUpToken: string) => apiPost<RelayStatusDto>('/relay/reclaim', {}, undefined, stepUpToken),

  /** « Modifications à vérifier » (`reEnter` false) or « À reprendre » (true), newest first. */
  reviewItems: (params: { reEnter: boolean; includeReviewed: boolean; page?: number; pageSize?: number }) =>
    apiGet<PagedResponse<RelayReviewItemDto>>('/relay/review-items', params),

  /** « Vu » / « Repris » — changes no record; a second press keeps the first reader. */
  markReviewItemSeen: (id: string) => apiPost<RelayReviewItemDto>(`/relay/review-items/${id}/seen`, {}),

  /** AC-6.2 — asked by the Windows and Android apps only (`relay-device-watch`). 404 where no change feed exists. */
  deviceTarget: () => apiGet<RelayDeviceTargetDto>('/relay/devices/target'),

  /** AC-6.2 — whether this device reached the PC, and its own gateways. Counted only from the cabinet's network. */
  deviceReport: (body: { reachesPc: boolean; gateways: string[] }) =>
    apiPost<RelayDeviceReportDto>('/relay/devices/report', body),
};

/** The step-up action « Oui » on the offer is confirmed with (AC-1.4) — an authenticator code, never a password. */
export const RELAY_PAIRING_STEP_UP = "relay-pairing";

/** The step-up action « Déclarer perdu ou volé » is confirmed with — an authenticator code, never a password. */
export const RELAY_LOST_STEP_UP = "relay-lost";

/** The step-up action « Reprendre la main » consumes (`RelayStepUpActions.Reclaim`). */
export const RELAY_RECLAIM_STEP_UP = "relay-reclaim";

/** The step-up action « Effacer la copie » is confirmed with — an authenticator code, never a password. */
export const RELAY_ERASE_STEP_UP = "relay-erase";
