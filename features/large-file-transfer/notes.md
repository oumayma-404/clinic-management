# large-file-transfer — making a big study something the product can actually carry

The coffre exists because a 340 Mo CBCT was thought to be untransferable. The industry disagrees: comparable
products upload everything and solve the bandwidth with **resumable chunked upload** (tus, S3 multipart), and
where an on-premises component exists it is a *relay* to the cloud, not a terminus. The arithmetic backs them —
340 Mo at Tunisia's median 9 Mbps uplink is about five minutes, not an impossibility.

So the plan is four parts, in a deliberately reversible order. **Parts 1 and 2 have landed; 3 and 4 have not.**

| | | |
|---|---|---|
| **Part 1** | Downloads stream instead of buffering | ✅ landed |
| **Part 2** | Resumable chunked upload | ✅ landed |
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

## Part 2 — resumable chunked upload (landed)

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

### The browser half — what actually calls the endpoints

`lib/files/resumable-upload.ts` is the only thing in the app that speaks the five endpoints, and the order they
go in lives there rather than in the queue component so that « what happens when part 7 fails » has one answer.

⚠️ **The path is chosen on size, and the threshold is the SERVER's chunk size** — published as
`UploadPolicyDto.ResumableChunkBytes` (0 on the three doors that have no resumable endpoints, which is what stops
a cachet picker opening a session against a route that would 404). A file that fits in one part keeps the single
POST: three extra round trips buy nothing, and its « progress bar » would go 0 % → 100 % with nothing between,
which is an animation rather than a measurement. So « worth chunking? » is exactly « more than one part? », and
asking the server means the number cannot drift the way a second copy would.

⚠️ **Every count comes back from the server.** The loop is driven by `session.nextPart`, never by a local
counter: a part whose response was lost is stored there and unknown here, and a browser trusting its own tally
would skip it and assemble a file that is the right length in its row with a hole in the middle. For the same
reason a retry **re-reads the session** before re-sending — the likeliest cause of a failed part is a lost
*response*, so blindly re-sending is eight megabytes to be told « already have it ».

⚠️ **A refusal is not retried, a transport failure is.** The server's 4xx sentences are facts about the file or
the protocol; re-sending the same bytes reproduces them exactly, at the clinic's expense. Only
`ApiErrorCode.Network` earns the four attempts and the 1 s / 2 s / 4 s backoff.

⚠️ **A cancellation is not a failure and must not be worded as one.** An aborted `fetch` arrives as the same
`AbortError` a fired deadline does, which `client.ts` maps to « Vérifiez votre connexion » — true for a timeout
and a lie for somebody who just pressed « Annuler ». `UploadCancelledError` is what tells them apart. Cancelling
also *abandons* the session; every other failure leaves it, because the parts already sent are the whole point.

### Resuming across a reload, and the thing deliberately NOT stored

`lib/files/upload-resume-store.ts` remembers an interrupted upload so `ResumeUploadsNotice` can offer it back
after a closed tab. It stores a **description** — the raw file name, size and `lastModified`, the server's byte
count, the expiry — and **never the bytes**.

⚠️ That is a decision, not a limitation. A `File` is structured-cloneable, so a 150 Mo radiograph could go into
IndexedDB and the resume would cost one click instead of two. It would also leave a copy of a patient's imaging
unencrypted in a shared clinic PC's browser profile, surviving reboots, with no lifecycle beyond our own
cleanup — a data-at-rest question this product has no answer for. So the user re-picks the file.

⚠️ **The identity check is three properties and lives here**, not in the orchestrator: `lastModified` is what
catches the case that actually happens — the same study re-exported from the scanner between attempts, same
name, same size, different bytes. Resuming that assembles a file from two exports with no error anywhere. The
orchestrator checks only the **declared length**, and deliberately not the name: what a session reports is the
name after `FileNameSanitizer` (path segments stripped, seven characters removed, whitespace collapsed, dots
trimmed, length bounded), so comparing it to a raw `file.name` needs a second copy of that sanitiser in
TypeScript — and a copy that drifts calls ordinary accented filenames « a different file » and silently restarts
uploads that were perfectly resumable.

⚠️ An **expired** record is deleted on read rather than offered: its staged parts have been reclaimed, so
« reprendre » would open a new session and start from zero while saying it was continuing.

### Three defects the browser walk found, none of them visible as an error

⚠️ **The file list reloaded between every chunk.** The realtime convention is by *area*: any command under
`Features/<Area>/Commands` tells every browser in the clinic that `<area>` changed. Correct for an edit, wrong
for a **step** of a longer operation — so one 29,8 Mo file in four parts made every open tablet in the practice
refetch the patient's drawer four times before the file existed, and a 300 Mo study is 38 of them. Fixed with
`IDoesNotBroadcast` on the three step commands; **completion still broadcasts**, which is the direction silence
would break. `RealtimeResourceResolverTests` pins both, plus a derived guard — proven red — that a marker is only
worn where it changes something, because a redundant marker in an excluded area reads to the next author as a
live one.

⚠️ **A live upload was also listed as interrupted.** Found by *looking*, not by a query — the first pass
checked « 81 % » and the cancel button through the DOM and read as green. On screen, at 390 px, the page showed
« L'envoi de « panoramique-lourde.png » a été interrompu · 0 o sur 29,8 Mo » directly above that same file's
progress bar at 54 %: `rememberUpload` writes a record the moment a session opens, so the notice offered to
resume a file the queue was uploading two centimetres below. Accepting would have opened a second client against
one session and had its parts refused as out of order. Two changes, and both were needed — `useUploadQueue`
publishes `activeUploads` (the sessions it is running) and the notice never offers one of those; and the record
is **not written until a part has actually landed**, since an upload dropped in its first seconds has nothing
staged to continue from and « 0 o déjà envoyés » is a second hunt through the file system to achieve exactly
what « Téléverser » does.

⚠️ **A resumed upload finished and the offer to resume it stayed on screen.** `ResumeUploadsNotice` re-read its
store on the number of queued items, which changes when an upload *starts* and never again. It reads the queue's
**settled** count now, and the notice drops a record optimistically the moment it hands the file over — two
surfaces describing one upload, one of them still calling it interrupted, is the app disagreeing with itself
about what it holds.

### Verified in a real browser, against the running stack

| | |
|---|---|
| A 29,8 Mo file through the picker | `POST /uploads` → `PUT chunks/1…4` → `POST /complete` → **201** |
| Progress mid-flight | **81 %** on the item, from the server's own byte count |
| Cancel mid-flight | « **Annulé** », not « Échec »; session abandoned, IndexedDB row gone |
| **Page reloaded at 27 %** | offer returns: « 8 Mo sur 29,8 Mo déjà envoyés » |
| Resumed from it | `GET /uploads/{id}` → `PUT chunks/2,3,4` → complete. **Part 1 never re-sent** |
| The stored file | **byte-identical** (SHA-256) on all four runs, each with a preview |
| Reprendre with the **wrong** file | refused by name, nothing uploaded, the record kept |
| A 31,6 Mo TIFF | still refused to the coffre — the residency rule is untouched |
| Between chunks, after the fix | **no** `folders` / `files?page=1` refetch; only after `complete` |
| **3 uploads running**, 3 records in IndexedDB | **0** offered as interrupted — and 3 offered once they stop running, which is the direction that proves the filter is not simply hiding everything |
| Dropped **before** any part landed | no record written, nothing offered |
| Dropped **after** one part landed | one record, « 8 Mo sur 29,8 Mo déjà envoyés » |

Eye pass at **320 / 390 / 820 / 1180 / 1440 and a landscape phone (844×380)**, with the notice and a live upload
on screen at once: no sideways page scroll at any width, the cancel control in view at all six, the notice
stacking below `sm:` and becoming a row above it — the coffre notice's own shape, one element up.

⚠️ **Seeing the in-flight state needed the uplink throttled** (CDP `Network.emulateNetworkConditions`, ~400 Ko/s).
Over loopback a 29,8 Mo upload finishes in about five seconds, which is not enough time to resize and look; the
first pass therefore only ever *measured* that state, and that is exactly how the third defect above survived it.

### Still owed

- **Nothing sweeps abandoned sessions.** `IFileUploadSessionRepository.GetExpiredAsync` exists and no job calls
  it, so a tab closed mid-upload leaves its staged parts for the full 24 h. Harmless per upload, unbounded across
  a year.
- The two coffre-sized samples (31,6 Mo TIFF, 34 Mo ZIP) are still unwalked end to end — the dev browser has no
  coffre paired, so that door refuses them before the chunked path is reached. That is the *correct* behaviour,
  and it is also why this cannot be verified here.

## Part 3 — raise the threshold (not started)

`LargeStaysAtTheCabinet = ResidencyRule.HostedUpTo(DocumentBytes)` is the line, and `DocumentBytes` is 25 Mo
because it is also "what counts as a document". Part 3 gives the residency rule its own constant and raises it.
⚠️ Do not do this before Part 2: a 150 Mo single POST over a Tunisian uplink that fails at 95 % restarts from
zero, and the operator has no way to know it will.

## Part 4 — a quota (not started)

Once the deployment stores everything, bytes are the cost and a clinic needs a ceiling it can see. Nothing in
the product counts stored bytes per clinic today.
