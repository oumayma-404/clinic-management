import { apiDelete, apiGet, apiPost } from './client';

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
  | 'stopped';

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

  /** « Retirer ce PC » (AC-8.1): the copy stops and the clinic may set up another. Returns the new state. */
  retire: () => apiDelete<RelayStatusDto>('/relay'),

  /**
   * « Déclarer perdu ou volé » (AC-8.4) — needs a step-up token for {@link RELAY_LOST_STEP_UP}. Every account of the
   * cabinet, the caller's included, must then choose a new password: the caller's session ends with the answer.
   */
  declareLost: (stepUpToken: string) => apiPost<RelayStatusDto>('/relay/lost', {}, undefined, stepUpToken),
};

/** The step-up action « Déclarer perdu ou volé » is confirmed with — an authenticator code, never a password. */
export const RELAY_LOST_STEP_UP = "relay-lost";
