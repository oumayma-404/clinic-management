/**
 * The PDFs this product renders itself, and the one place a browser-facing URL for one is composed.
 *
 * <p>Shared by the BFF route that serves them (`app/bff/pdf/[kind]/[id]/route.ts`) and by the dialogs that
 * frame them, so the set of servable kinds and the URL shape cannot drift apart — a dialog pointing at a
 * kind the route does not know would render an « introuvable » inside a frame, with nothing saying why.</p>
 *
 * <p>⚠️ <b>This is the closed set that makes the route safe.</b> It is a literal map, and the route refuses
 * any `kind` outside it and any `id` that is not a GUID. Adding an entry here opens a new upstream path to
 * the browser, so add one only for a PDF <b>we compose from our own rows</b> — never for an uploaded file
 * (see the route's own note on why patient files stay on the blob path).</p>
 */
export const PDF_KINDS = {
  /** A `MedicalDocument` — ordonnance, certificat, lettre de liaison, note d'honoraires. */
  document: { path: (id: string) => `/medical-documents/${id}/pdf` },
  /** A numbered note d'honoraires from the Factures module. */
  invoice: { path: (id: string) => `/invoices/${id}/pdf` },
  /** The avoir that corrects one. */
  avoir: { path: (id: string) => `/invoices/avoirs/${id}/pdf` },
} as const;

export type PdfKind = keyof typeof PDF_KINDS;

export function isPdfKind(value: string): value is PdfKind {
  return Object.prototype.hasOwnProperty.call(PDF_KINDS, value);
}

/**
 * Where a dialog points its frame for a PDF the server renders.
 *
 * ⚠️ **Relative on purpose.** It is fetched by the browser as an ordinary same-origin request so the session
 * cookie travels — the whole reason this is a BFF hop rather than a direct call to the API, which is on
 * another origin and takes a bearer the frame cannot send.
 *
 * ⚠️ **`fileName` ends up as the last path segment because that is the ONLY thing Chrome's PDF viewer reads.**
 * Measured: with the filename in `Content-Disposition` alone the viewer still titled the document by the id in
 * the URL. Pass the same name the dialog's own « Télécharger » uses, so the two ways out of that dialog cannot
 * save the same document under different names.
 */
export const pdfSourceUrl = (kind: PdfKind, id: string, fileName: string): string =>
  `/bff/pdf/${kind}/${id}/${encodeURIComponent(pdfFileName(fileName))}`;

/**
 * The name made safe to be a path segment, and guaranteed to end in `.pdf`.
 *
 * ⚠️ The extension is the half the report was actually about — « downloads without an extension » — so it is
 * appended rather than assumed. Slashes and control characters go because they would change what the path
 * means; everything else (spaces, accents, apostrophes) survives `encodeURIComponent` and is what the reader
 * sees in their downloads folder.
 */
function pdfFileName(fileName: string): string {
  // Path separators and control characters only — they would change what the path means. A hyphen, a space,
  // an accent and an apostrophe are all ordinary in « Note d'honoraires.pdf » and must survive, which
  // `encodeURIComponent` sees to.
  const cleaned = Array.from(fileName)
    .filter((c) => c !== '/' && c !== '\\' && c.charCodeAt(0) > 31)
    .join('')
    .replace(/\s+/g, ' ')
    .trim();
  const named = cleaned || 'document';
  return named.toLowerCase().endsWith('.pdf') ? named : `${named}.pdf`;
}
