# Photos de l'appareil, rangées par l'agenda — what shipped

Blueprint: [`blueprint.md`](blueprint.md). Progress and what is still owed: [`progress.md`](progress.md).

## The shape, in one paragraph

The dentist's camera photos reach « Importer les photos » (`/photos/import`) from a card in any browser, or — in the
Windows app 1.8 — straight from a camera or a card the app noticed. Each burst is matched to that day's visits (late
arrivals and fiche save times included), the dentist checks the groups and presses « Ranger », and each photo lands in
the patient's « Photographie » folder with the time it was **taken**. On a hosted cabinet the cloud keeps a 2 560 px
copy and the Windows app keeps the full-size original on the clinic PC, copied add-only to the archive disk; a cabinet
serving itself stores the original directly. Every hosted cabinet has a storage ceiling the vendor can raise from the
console, with bell warnings at 80 % and 95 %.

## Decisions that are easy to undo by accident

- **Nothing is filed before « Ranger », and an unsure group blocks it.** A photo in the wrong patient is a clinical
  error. The matcher's rule is « never sure and wrong »: a group it cannot place asks.
- **The clock face, never a zone.** Visit times arrive as clinic wall-clock with no offset, EXIF times are read as the
  same number line (`lib/photo-import/wall-clock.ts`). `new Date(string)` on either reintroduces the workstation's zone
  and files a photo one hour off.
- **The capture key is the duplicate check, and it is computed from the camera's own file name and size.** The Windows
  app therefore copies a photo under its exact original name, in a folder of its own per photo — a renamed copy would
  not be recognised as the photo the browser path already filed.
- **The page makes every server call.** The shell's only credential (the archive grant) is refused outside its own
  routes, so the shell copies, remembers and keeps, and hands the page what the server must hear (a kept original, a
  complete copy).
- **A detection only raises a notice.** A phone charged on the clinic PC exposes a `DCIM` too; its owner's photos are
  never copied because it was plugged in. Copying starts at « Importer », and « Ignorer cet appareil » stops the
  notices for a device on that PC. The switch is off by default (a `desktop/**` merge reaches every clinic silently).
- **The originals live by file id** (`photos\{id[0..2]}\{id}.jpg` + `index.csv`), so « Changer de patient » moves
  nothing on disk, and the folder stays readable without the app.
- **The journal and the staging copies are per Windows account** (`%LocalAppData%`), the originals machine-wide
  (`%ProgramData%`, the coffre's ACL). A store another account created is refused with a sentence; the cloud copy still
  files — losing the PC loses detail, never a photo.
- **A copy on the same physical disk is no copy.** `AddOnlyFolderCopy.SameDiskRefusal` asks the volume's disk extents,
  because `C:` and `D:` are often two partitions of one disk. It applies to the coffre too — see the trap below.

## Traps

- **A visit booked with no practitioner belongs to every practitioner's agenda.** Most visits name nobody (32 of 42 in
  prod), and a new camera follows the signed-in dentist by default — an exact `DoctorId` match made the import answer
  « Aucun rendez-vous à cette heure-là » over a visit in the book. The rule lives twice, in `agendaFor` (the one the page
  uses) and in `GetPhotoImportVisitsAsync`, and both say so. Found by the browser walk, not by any unit test.
- **The coffre's copy report used to send this run's bytes as the total.** After the first complete copy every run
  reported « 0 octet », the server answered « incomplete », and the 30-day alert fired over a copy that was complete.
  `AddOnlyFolderCopy.Result` separates `Copied/CopiedBytes` (this run) from `Files/Bytes` (what the destination holds);
  a report is made of the second pair. Pinned by `A_Second_Copy_With_Nothing_New_Still_Reports_Everything…`.
- **⚠️ Behaviour change on update:** a cabinet whose archive folder is on another partition of the PC's own disk had a
  coffre copy that « succeeded ». From 1.8 it is refused with « même disque », the report stops, and the coffre alert
  will say so after 30 days. That is the truth about that copy; tell the pilot before rolling out.
- **The mirror's file list contained coffre files**, whose download answers 400; ten in a row tripped the mirror's
  circuit breaker and stopped every run at the first imaging series. The manifest is hosted files only now
  (server-side, so every shell version benefits).
- **A batch is a message, not a reply.** File handles travel only with `PostWebMessageAsJson`, so `cameraScan` answers
  a count while the photos reach `__clinicShellDeliverCamera`, or wait in the `__clinicShellPendingCamera` **queue** —
  the coffre's single slot would drop a second batch.
- **Two mirrored sets are held by tests, not by memory:** the JPEG/RAW/HEIC/video extensions (shell ↔
  `web/lib/photo-import/sources.ts`, `CameraContractTests`) and the bridge's method set (script ↔
  `web/types/clinic-shell.d.ts` ↔ `mobile/shared/bridge.md`, same class). Both red-proofed.
- **Every shell handler swallows.** An exception in `NavigationCompleted` or `WebMessageReceived` ends the WPF
  process; the camera code is wrapped so a failure costs the camera and nothing else.
- **A camera on a USB cable is not verified.** `Windows.Devices.Portable.StorageDevice` is the built-in route and is
  guarded everywhere; a card in a reader (every camera supports one) is the path that does not depend on it. The
  camera's own clock cannot be read through it, so the matcher's offset detection stands in.
- **The storage alerts dedupe per level**, re-arm only below 75 %, and a vendor-set quota of 0 is impossible (1 Go –
  2 To): « nothing enforced » is the deployment's answer, never a cabinet's.
