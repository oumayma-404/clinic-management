# The PDF viewer's own toolbar saves as a bare UUID, with no name and no extension

**Reported:** 2026-09-10, by the practice owner — « download from the pdf tool, downloads without an extension ».

**Status: FIXED**, 2026-09-10. Kept because three of the things measured on the way are worth not re-deriving —
in particular, the cause was **not** the one this file first named.

## What happened

Every PDF this product frames was painted from a `blob:` URL. Chrome renders those with its own viewer, whose
toolbar carries a download button we do not own, and it saved as `504688d8-cf62-4386-85de-469f…` — unnamed and
unextended.

## ⚠️ The cause was the URL PATH, not the missing headers — and that reverses this file's own first guess

The original write-up reasoned that a blob URL is badly named *because it carries no response headers*, and
proposed `Content-Disposition: inline; filename=…` as the fix, with the path segment as a maybe.

**Measured, and it is the other way round.** Served over HTTP with
`Content-Disposition: inline; filename=note-d-honoraires-karim-hamdi.pdf`, Chrome's viewer *still* titled the
document **`1ccc6c76-dd60-436c-99d6-f7800baf7e5d`** — the id from the URL. The viewer reads the **last path
segment** and ignores the header. A blob is badly named not because it lacks headers but because its last
segment is a UUID; headers were never going to be read.

So the trailing `[name]` segment is the whole fix. The header is sent anyway — `inline` is what stops the frame
downloading instead of rendering, and the filename in it is what every non-Chromium viewer and `curl -OJ` use.

## Two things that do NOT work — measured, don't re-try them

1. **An object URL from a named `File` rather than a `Blob`.** Both produce `blob:<origin>/<uuid>`; the name
   reaches nothing.
2. **`#toolbar=0`.** Removes the mis-naming button along with zoom, page navigation and rotate — a capability
   removed by a naming decision (`frontend-web.md` § 0) — and renders the frame **blank** in an Android WebView.

## What shipped

`web/app/bff/pdf/[kind]/[id]/[name]/route.ts` — a **closed** same-origin proxy: `kind` is looked up in
`lib/pdf-sources.ts`' literal `PDF_KINDS` and `id` must parse as a GUID, so the only reachable upstreams are the
three PDFs this product renders itself (document · invoice · avoir). `pdfSourceUrl(kind, id, fileName)` is the
one composer of the URL, and it uses the **same** `fileName` the dialog's « Télécharger » uses, so the two ways
out of a dialog cannot save one document under two names.

`middleware.ts` now skips the whole of `/bff/`, not just `/bff/auth/`: these are API surfaces and answer with a
status. Left redirecting, an absent session would have rendered the **login screen inside the PDF viewer**.

## ⚠️ The trap this route hit, which the original write-up did not anticipate

« The iframe is same-origin and credentialed, so the cookie travels » is true and insufficient: **the cookie is
a *rotating* credential.** The BFF must exchange it for a bearer, and `RefreshTokenCommand` rotates the family
on **every** exchange — that rotation *is* the theft detection, so there is no non-rotating variant to add and
none should be added.

Two consequences, both load-bearing:

- The route **must** write the rotated credential back (`writeSessionCookies`), exactly as `/bff/auth/token`
  does. Without it, framing a PDF spends the browser's credential and hands back nothing — measured: the very
  next request 401'd.
- With it, a second exchanger is safe. `SessionFamily` keeps a **one-generation grace** and its own comment
  says so (« the racing tab is a legitimate user and gets a working credential of its own »), which is what
  makes this architecturally identical to a second browser tab. Verified on a controlled run: seed → app ok →
  framed PDF → app ok, cookie rotating each time.

⚠️ **Five session families were ended as replays during this work, and none of them was the route.** They were
the probe: a re-login script minting a *new* family while the browser context still held a cookie from the old
one. The discriminator is the controlled run above — if you see replay endings while testing anything in this
area, check for a stale context cookie before believing the product did it.

## What is deliberately NOT served this way

**Patient files.** `PatientFilesController` serves them `attachment` + `nosniff` on purpose (AC-11.6) so
uploaded bytes never render in the app's origin. Flipping that to `inline` to tidy a filename would trade a
security control for a cosmetic one. They keep the blob path, and so does the fiche's **aperçu**, which is
composed by a POST, persists nothing, and therefore has no id to name.
