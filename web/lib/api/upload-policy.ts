import { apiGet } from './client';

/**
 * What the server accepts on the patient-file door — **served, never mirrored** (AC-5.1).
 *
 * The picker's `accept` used to be the literal `application/pdf,image/png,image/jpeg` beside a comment claiming it
 * mirrored `FileContentValidation.PatientFileTypes`. That was true when written and false the moment the catalog
 * widened, and the failure mode is silent in the direction that matters: the picker hides files the server would
 * have taken, so a dentist concludes the app cannot open their DICOMs. Everything here — the accept string, the
 * caps, the refusal wording — is computed from `FileTypeCatalog` on the server.
 */
export interface UploadPolicyFormat {
  extensions: string[];
  contentType: string;
  label: string;
  maxBytes: number;
  isBrowserPreviewable: boolean;
  /** The server's own « trop volumineux » sentence for this format's cap, so the two agree word for word. */
  tooLargeMessage: string;
  /**
   * `hosted` — every file of this format goes to the server. `hostedUpTo` — files past `hostedMaxBytes` are
   * kept in the cabinet's coffre instead. The server answers per deployment, so a clinic hosting its own blobs
   * sees `hosted` everywhere and needs no branch of its own.
   */
  residency: 'hosted' | 'hostedUpTo';
  /** The largest file of this format the **server** will hold — where the coffre takes over. */
  hostedMaxBytes: number;
  /** The largest file the coffre will take, or 0 where this format never goes there. */
  vaultMaxBytes: number;
  /** The server's own sentence for a file past even the coffre's ceiling. Empty for an always-hosted format. */
  vaultTooLargeMessage: string;
}

export interface UploadPolicy {
  profile: string;
  maxBytes: number;
  accept: string;
  formats: UploadPolicyFormat[];
  deniedExtensions: string[];
  unsupportedMessage: string;
  deniedMessage: string;
  /** Whether this deployment files large studies in the cabinet's own coffre. */
  vaultAvailable: boolean;
  /** The server's own wording for « this one belongs at the cabinet and this machine has no coffre ». */
  vaultUnavailableMessage: string;
  /**
   * The size of every part but the last for an upload sent in pieces, or **0 where this door has no resumable
   * endpoints** — see `shouldUploadInParts`.
   */
  resumableChunkBytes: number;
}

/** Which door a file goes through. Decided by the server's policy, never guessed from the extension. */
export type FileDestination = 'hosted' | 'vault';

/**
 * The doors the server publishes a policy for. Named rather than free-form so a typo is a `tsc` error and not a
 * refusal at runtime — the server answers « Ce type d'envoi n'existe pas » for an unknown one.
 */
export type UploadProfile = 'patient-file' | 'profile-image' | 'medical-document-pdf' | 'csv';

export const uploadPolicyApi = {
  get: async (profile: UploadProfile = 'patient-file'): Promise<UploadPolicy> =>
    apiGet<UploadPolicy>(
      profile === 'patient-file' ? '/meta/upload-policy' : `/meta/upload-policy?profile=${profile}`,
    ),
};

/** Lower-case, dot-less — the catalog's own key. Mirrors `FileNameSanitizer.ExtensionOf`. */
export function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  if (dot <= 0 || dot === fileName.length - 1) return '';
  const extension = fileName.slice(dot + 1).toLowerCase();
  return /^[a-z0-9]+$/.test(extension) ? extension : '';
}

export function formatFor(policy: UploadPolicy, fileName: string): UploadPolicyFormat | null {
  const extension = extensionOf(fileName);
  if (!extension) return null;
  return policy.formats.find((format) => format.extensions.includes(extension)) ?? null;
}

/**
 * Which door this file goes through. ⚠️ **The server's answer, never a guess from the extension** — the same
 * `.dcm` is hosted at two megabytes and kept at the cabinet at four hundred, and a deployment where the clinic
 * already holds its own blobs sends everything to the server whatever its size.
 */
export function destinationFor(policy: UploadPolicy, file: File): FileDestination {
  if (!policy.vaultAvailable) return 'hosted';

  const format = formatFor(policy, file.name);
  if (!format || format.residency !== 'hostedUpTo') return 'hosted';

  return file.size > format.hostedMaxBytes ? 'vault' : 'hosted';
}

/**
 * The client-side half of the same rules: the reason this file cannot be sent, or `null` when it can. The server
 * re-checks every one of these — a pre-check is a courtesy, never the guard.
 *
 * ⚠️ **`vaultReachable` is a fact about this machine, not about the file.** A study bound for the coffre is
 * perfectly acceptable; what can be missing is a coffre to put it in — a phone, a laptop at home, a browser that
 * does not implement the API. Refusing with the server's own « ouvrez APEXA au cabinet » sentence is the whole
 * point of AC-6, and it is deliberately not a size refusal.
 */
export function refusalFor(
  policy: UploadPolicy,
  file: File,
  options: { vaultReachable?: boolean } = {},
): string | null {
  const extension = extensionOf(file.name);
  if (!extension) return policy.unsupportedMessage;
  if (policy.deniedExtensions.includes(extension)) return policy.deniedMessage;

  const format = formatFor(policy, file.name);
  if (!format) return policy.unsupportedMessage;

  if (destinationFor(policy, file) === 'vault') {
    if (!options.vaultReachable) return policy.vaultUnavailableMessage;
    return file.size > format.vaultMaxBytes ? format.vaultTooLargeMessage : null;
  }

  // An always-hosted format's hostedMaxBytes IS its maxBytes, so this one comparison covers both shapes.
  return file.size > format.hostedMaxBytes ? format.tooLargeMessage : null;
}

/**
 * Whether this file is worth sending in parts.
 *
 * ⚠️ **The threshold is the server's chunk size, and deliberately not a constant of our own.** A file that fits
 * in one part gains nothing from the protocol — three extra round trips, and a « progress bar » that goes from
 * 0 % to 100 % with nothing in between, which is an animation rather than a measurement. More than one part and
 * both of the things this exists for become true at once: an interruption costs the last part instead of the
 * whole file, and the bar moves because something really happened.
 *
 * A door that publishes 0 has no resumable endpoints at all, so the single POST is the only way through it.
 */
export function shouldUploadInParts(policy: UploadPolicy | null | undefined, file: File): boolean {
  if (!policy || policy.resumableChunkBytes <= 0) return false;
  return file.size > policy.resumableChunkBytes;
}

/**
 * « PNG ou JPEG, 5 Mo maximum. » — the helper line under a picker, derived from the door it stands in front of.
 *
 * ⚠️ It exists because that sentence was **written by hand** on the cachet field and said « 2 Mo » while the
 * server accepted twenty-five: a helper line is a promise about what will be refused, and a hand-written one is
 * the first thing to drift when a cap moves. Returns null with no policy in hand, so the field simply says
 * nothing rather than quoting a number nobody checked.
 */
export function acceptHint(policy: UploadPolicy | null): string | null {
  if (!policy || policy.formats.length === 0) return null;

  const labels = Array.from(new Set(policy.formats.map((format) => format.label)));
  const megabytes = Math.floor(policy.maxBytes / (1024 * 1024));

  // Two formats read as « PNG ou JPEG »; more than three would be a paragraph, so the accept attribute carries
  // the full list and the line states the cap alone.
  const kinds = labels.length <= 3 ? labels.join(' ou ') : null;

  return kinds ? `${kinds}, ${megabytes} Mo maximum.` : `${megabytes} Mo maximum par fichier.`;
}
