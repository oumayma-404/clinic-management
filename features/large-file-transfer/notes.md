# large-file-transfer — making a big study something the product can actually carry

The coffre exists because a 340 Mo CBCT was thought to be untransferable. The industry disagrees: comparable
products upload everything and solve the bandwidth with **resumable chunked upload** (tus, S3 multipart), and
where an on-premises component exists it is a *relay* to the cloud, not a terminus. The arithmetic backs them —
340 Mo at Tunisia's median 9 Mbps uplink is about five minutes, not an impossibility.

So the plan is four parts, in a deliberately reversible order. **Part 1 has landed; the rest have not.**

| | | |
|---|---|---|
| **Part 1** | Downloads stream instead of buffering | ✅ landed |
| **Part 2** | Resumable chunked upload | ◨ server done, browser half to come |
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

## Part 2 — resumable chunked upload (server done, browser half to come)

Five endpoints under `…/files/uploads`: open, ask where you got to, put part *n*, complete, abandon.

### The shape, and why each piece is the way it is

⚠️ **Everything that can be refused is refused at OPEN, before a byte is sent.** That is what the declared
length is for: a 200 Mo file of a format this deployment does not take should cost the clinic nothing, and a
refusal after four minutes of uploading is the failure this whole part exists to end. What cannot be judged then
is the **signature** — the bytes are not here — so it is checked against the first chunk's header, which is where
it arrives.

⚠️ **Parts are sequential, and that is a deliberate limit** rather than an oversight. `ReceivedParts` is a
count, not a set: part *n+1* is the only one a client may send next. Parallel chunks would use more of the uplink
but need a set of received parts, a child table to hold it and an ordering decision at assembly — and on the
connections this exists for, one stream already saturates the link.

⚠️ **A part's length is checked against the session's own arithmetic.** Nothing downstream re-measures a
staged part, so a chunk cut off by a dropped connection and accepted here would produce a file that is the right
length in its row and has a hole in the middle on disk. A re-sent part, by contrast, is a **success**: a client
whose response was lost cannot tell « stored » from « never arrived ».

⚠️ **The session is a table, not server memory**, because the point is surviving a restart — an in-process
dictionary would lose every upload in flight on a deploy, which is exactly when a practice is most likely to be
sending one. It is deleted on completion rather than marked done: the `PatientFile` is the record, and a spent
session beside it would be a second, staler answer to « does this file exist? ».

### The storage seam, and the thing that was checked rather than assumed

`IResumableUploadStore` — its own seam, not more overloads on `IFileStorage`, whose contract is « a blob, whole,
at a key » and whose every upload is proved by a reflection test to name a clinic. A half-written file is not a
blob.

⚠️ **Not S3 multipart.** The obvious implementation is the object store's own multipart API, and
**`Minio 5.0.0` keeps it internal** — `NewMultipartUploadAsync`, `PutObjectPartAsync` and
`CompleteMultipartUploadAsync` are all non-public, the exposed surface being only `ListIncompleteUploads` and
`RemoveIncompleteUploadAsync`, and there is no `ComposeObject`. Verified by reflecting over the shipped assembly
rather than by reading documentation. So a part is an ordinary object under a staging prefix **inside the owning
clinic**, and completing concatenates them.

⚠️ **The concatenation streams and never buffers.** `ConcatenatedStream` opens one part at a time and is
handed to `PutObjectAsync` with the total declared up front, so a gigabyte costs the server one part-sized
buffer. It deliberately does **not** go through `IFileStorage.UploadAsync`: that method buffers a non-seekable
stream whole to learn its size, which is exactly what this avoids. The bytes travel between the API and the
object store — the same host or the same private network — and never touch the clinic's uplink twice.

⚠️ `ListObjectsAsync` returns an **`IObservable<Item>`**, not an async sequence, so the subscription is
bridged by hand rather than by taking a reactive dependency for one call — with the error callback settling the
same task, because a listing that fails silently would read as « nothing left to clean up ».

### The migration

`AddFileUploadSessions` — one table, purely additive. ⚠️ **EF's differ emitted an `xmin` column**, the same
rejection three earlier migrations had to undo; removed by hand. Verified the way this repo requires: the drift
set **before** was the 4 pre-existing plus 3 from the model change, and **after** applying it is exactly the 4
pre-existing — none new, none on any existing table.

### Verified against the running stack, on a 29,8 Mo file in 4 chunks

| | |
|---|---|
| Straight through | assembled **byte-identical** (SHA-256 against the source) |
| **Interrupted after 2 of 4**, then asked where it got to | `receivedParts: 2, nextPart: 3` → resumed → **byte-identical** |
| Out-of-order part | refused, own sentence |
| Short part (16 bytes where 8 Mo was due) | refused, own sentence — the one that stops a hole in a radiograph |
| Re-sent part | success, reports where the upload stands |
| Open with `.exe` / 400 Mo / 0 bytes | refused **before a byte was sent** |
| Abandon | 204, and the session reads back as gone |

⚠️ **The first run of that probe proved nothing and looked green**: the sample was 388 Ko against an 8 Mo
chunk, so `totalParts` was 1 and the multi-part path — the whole feature — was never entered. The numbers above
are from a file that genuinely needs four.

### Still to come in Part 2

The browser half: `Blob.slice()`, the resume state in IndexedDB, and `upload-queue.tsx` choosing this path over
the single-POST one. Until then the endpoints exist and nothing in the app calls them.

⚠️ And **real progress becomes possible for the first time**: `fetch` exposes no upload progress for a single
POST, which is why the queue deliberately shows none today — chunks are what make an honest progress bar
something other than an animation.

## Part 3 — raise the threshold (not started)

`LargeStaysAtTheCabinet = ResidencyRule.HostedUpTo(DocumentBytes)` is the line, and `DocumentBytes` is 25 Mo
because it is also "what counts as a document". Part 3 gives the residency rule its own constant and raises it.
⚠️ Do not do this before Part 2: a 150 Mo single POST over a Tunisian uplink that fails at 95 % restarts from
zero, and the operator has no way to know it will.

## Part 4 — a quota (not started)

Once the deployment stores everything, bytes are the cost and a clinic needs a ceiling it can see. Nothing in
the product counts stored bytes per clinic today.
