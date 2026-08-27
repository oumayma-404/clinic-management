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
}

export interface UploadPolicy {
  profile: string;
  maxBytes: number;
  accept: string;
  formats: UploadPolicyFormat[];
  deniedExtensions: string[];
  unsupportedMessage: string;
  deniedMessage: string;
}

export const uploadPolicyApi = {
  get: async (): Promise<UploadPolicy> => apiGet<UploadPolicy>('/meta/upload-policy'),
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
 * The client-side half of the same rules: the reason this file cannot be sent, or `null` when it can. The server
 * re-checks every one of these — a pre-check is a courtesy, never the guard.
 */
export function refusalFor(policy: UploadPolicy, file: File): string | null {
  const extension = extensionOf(file.name);
  if (!extension) return policy.unsupportedMessage;
  if (policy.deniedExtensions.includes(extension)) return policy.deniedMessage;

  const format = formatFor(policy, file.name);
  if (!format) return policy.unsupportedMessage;
  if (file.size > format.maxBytes) return format.tooLargeMessage;
  return null;
}
