# The PDF viewer's own toolbar saves as a bare UUID, with no name and no extension

**Reported:** 2026-09-10, by the practice owner — « download from the pdf tool, downloads without an extension ».

**Status:** not fixed. Measured, understood, and the fix is bigger than the surface it appears on.

## What happens

Every PDF this product frames — `documents/document-preview-dialog.tsx`, `factures/invoice-pdf-dialog.tsx`,
`patient-file-pdf-preview.tsx` — is painted from a `blob:` URL in an `<iframe>`. Chrome renders it with **its own
PDF viewer**, whose toolbar carries a download button we do not own. Chrome names that save after the **last path
segment of the document's URL**, and a blob URL's last segment is a bare UUID:

```
blob:http://localhost:3000/504688d8-cf62-4386-85de-469f...
```

so the file lands as `504688d8-cf62-4386-85de-469f…`, unnamed and (depending on the platform) unextended.

## What is already true, and is not the bug

**The app's own « Télécharger » button names the file correctly** — `lib/download.ts` sets the name, and the
document dialog composes it from the document's own type (« Note d'honoraires.pdf »). Both dialogs carry that
button in their footer. The defect is only the *browser's* toolbar sitting a few pixels above it.

## Two things that do NOT fix it — both measured, don't re-try them

1. **Creating the object URL from a named `File` rather than a `Blob`.** Measured 2026-09-10 in Chrome: both
   produce `blob:<origin>/<uuid>` with the same shape, and the `File`'s `name` reaches nothing.
   `invoice-pdf-dialog.tsx`'s own commit message had already recorded this.
2. **`#toolbar=0` on the iframe src.** It would remove the mis-naming button — along with zoom, page navigation
   and rotate, which is a capability removed by a naming decision (`frontend-web.md` § 0). It is also
   Chromium-only and was deliberately taken out of `patient-file-pdf-preview.tsx` because Android WebView
   ignores it and renders the frame **blank**.

## What would fix it

Serve the bytes from a **same-origin URL whose last path segment is a real filename**, so Chrome has a name to
take — e.g. a BFF route `app/bff/documents/[id]/[name].pdf/route.ts` that attaches the server-side token and
streams `GET /api/medical-documents/{id}/pdf`. The iframe is same-origin and credentialed, so the cookie travels.

Design surface to settle before writing it:

- **Three call sites, one route or three?** The document dialog, the invoice dialog and the patient-file preview
  all want it; a route per resource is three, a generic `?src=` proxy is an open redirect. Probably one route per
  resource kind, named for it.
- **`Content-Disposition: inline` with a `filename`** — inline so it still *renders* rather than downloading, and
  the filename is what Chrome then suggests. Worth checking whether that alone is enough and the path segment is
  not needed.
- **Revocation and caching**: the current blob is revoked on close; a URL is not. `Cache-Control: no-store`, and
  the route must re-authorise per request rather than trusting the id.
- **The coarse-pointer tree is unaffected** — it hands the file to the OS through `downloadBlob`, which already
  names it.

## Where to start

`web/components/documents/document-preview-dialog.tsx` (the `pdfUrl` state and `PatientFilePdfPreview`), and
`web/components/patient-file-pdf-preview.tsx`, which is the one component all three surfaces frame their PDF
through — so the change lands once.
