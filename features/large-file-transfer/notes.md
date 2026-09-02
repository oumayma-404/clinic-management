# large-file-transfer — making a big study something the product can actually carry

The coffre exists because a 340 Mo CBCT was thought to be untransferable. The industry disagrees: comparable
products upload everything and solve the bandwidth with **resumable chunked upload** (tus, S3 multipart), and
where an on-premises component exists it is a *relay* to the cloud, not a terminus. The arithmetic backs them —
340 Mo at Tunisia's median 9 Mbps uplink is about five minutes, not an impossibility.

So the plan is four parts, in a deliberately reversible order. **Part 1 has landed; the rest have not.**

| | | |
|---|---|---|
| **Part 1** | Downloads stream instead of buffering | ✅ landed |
| **Part 2** | Resumable chunked upload | ⬜ |
| **Part 3** | Raise the coffre threshold | ⬜ |
| **Part 4** | Per-clinic storage quota | ⬜ |

⚠️ **Retiring the coffre outright is not on this list, and that is a decision, not an omission.** Existing rows
describe files that live *only* on a clinic's own PC; deleting the residency concept orphans them. The coffre
also remains the right answer for `SelfHostedLan`, where there is no cloud to upload to. What Part 3 changes is
the *line*, not the mechanism.

---

## Part 1 — a download is read as it is sent (landed)

**Both backends copied the whole object into a `MemoryStream` before returning it.** Every concurrent download
therefore held the entire file in the server's RAM: three people opening a 50 Mo panoramique was 150 Mo of a
small VPS — at *today's* caps, before anything got bigger. It is listed as an open item by the file-vault audit
and it is a prerequisite for every part below: raising the threshold on top of a buffering download turns a
memory cost into an out-of-memory kill.

- **Local disk** now returns the `FileStream` itself (`Asynchronous | SequentialScan`). The old comment said it
  buffered "to release the file handle"; on a clinic PC serving its own LAN that meant a 150 Mo study lived
  twice, once on disk and once in memory, per reader.
- **MinIO** feeds a `System.IO.Pipelines` pipe from a background task, because the SDK is push-based — it hands
  you a stream inside a callback and closes it when the callback returns.

### Three ways to get that silently wrong, and what stops each

- **A `StatObjectAsync` runs first.** Without it, a missing object or a refused credential surfaces on the
  caller's *first read* — after the handler's `try/catch` has returned success and the headers have gone out. The
  client then gets a **200 with a truncated body**, which every client treats as a real file. One HEAD restores
  the old error timing.
- **The writer is completed *with* its exception**, which makes the reader throw. Completing cleanly reports
  end-of-stream, i.e. the same silent truncation. This is also why `System.IO.Pipes` was rejected despite needing
  no package: an OS pipe closes as EOF when its producer dies.
- **The async `WithCallbackStream` overload**, never `Action<Stream>` — the latter makes an `async` lambda
  async-void, so the copy runs on after MinIO has disposed the response stream, throwing on a background thread
  and taking the host with it. (That one was already right and is now written down.)

### `GetLengthAsync`, and why the row's own number will not do

Streaming costs the `Content-Length` header: ASP.NET derives it from a *seekable* stream's own length, so the
buffering supplied it as a side effect. Without it a browser downloading a study reports « unknown size » and
shows no progress bar — on a slow connection, precisely when somebody is watching it.

⚠️ It is asked of the **store**, not read off `PatientFile.FileSize`. That column looks like the same number and
is the *client's claim* for any row written before upload validation existed; a wrong `Content-Length` truncates
or hangs a response rather than merely misreporting it.

### Verified

- 4076 backend tests pass, 0 failed. Solution builds with 0 errors.
- New tests pin the local-disk backend: the download is **not a `MemoryStream`** (precisely the regression — the
  buffer coming back — and a claim that cannot produce a false positive), a missing key throws *before* anything
  is read, and `GetLengthAsync` reports the size and null for an absent key.
- **Against the real stack**, because MinIO's pipe is not unit-testable: five files pulled over HTTP from the
  running API through MinIO — every one byte-exact against what was uploaded, every one carrying a correct
  `Content-Length`. And on two seeded rows whose blobs were never written: **400 / 404 with a French `{ error }`
  body**, no truncated 200, and `/health` still 200 afterwards.

---

## Part 2 — resumable chunked upload (not started)

The enabler for Part 3, and the bulk of the work. Sketch, so the next session does not re-derive it:

- A session row (`fileName`, `fileSize`, `folderId`, `description`, the store's upload reference, which parts
  arrived, an expiry) — it must survive a server restart, so it is a table and a migration.
- `POST …/files/uploads` → validate the name, extension and declared size against `FileTypeCatalog` **before a
  byte arrives**; return `{ uploadId, chunkSize }`.
- `PUT …/uploads/{id}/chunks/{n}` · `GET …/uploads/{id}` (what arrived, for resume) · `POST …/uploads/{id}/complete`
  · `DELETE …/uploads/{id}`.
- The signature check still works: the magic bytes are in part 1, so validate on the first chunk and re-check the
  total length on complete.
- Parts need a storage seam both backends can implement — **S3 multipart requires parts ≥ 5 Mo except the last**,
  which fixes the chunk size from below; local disk concatenates part files.
- Front end: `Blob.slice()`, resume state in IndexedDB, and the queue reporting real progress at last (`fetch`
  exposes no upload progress for a single POST, which is why `upload-queue.tsx` deliberately shows none today —
  chunks are what make an honest progress bar possible).

## Part 3 — raise the threshold (not started)

`LargeStaysAtTheCabinet = ResidencyRule.HostedUpTo(DocumentBytes)` is the line, and `DocumentBytes` is 25 Mo
because it is also "what counts as a document". Part 3 gives the residency rule its own constant and raises it.
⚠️ Do not do this before Part 2: a 150 Mo single POST over a Tunisian uplink that fails at 95 % restarts from
zero, and the operator has no way to know it will.

## Part 4 — a quota (not started)

Once the deployment stores everything, bytes are the cost and a clinic needs a ceiling it can see. Nothing in
the product counts stored bytes per clinic today.
