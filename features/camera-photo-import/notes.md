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
  and files a photo one hour off. **The camera's own zone (OffsetTimeOriginal) is not applied either**: on 38 real
  files cameras wrote a zone that ignores summer time or was never set (a body showing Tunis time, its menu on
  « Londres »), and converting by it moved every photo an hour onto the neighbouring visit. A face that is really off is
  the offset detection's job.
- **A late fiche is not presence.** A fiche's save stretches its visit's core only up to `ficheTailMaxMinutes` (60)
  after the expected end, and never past the next patient's arrival in that agenda. Unbounded, one fiche charted at the
  end of the day made every unbooked hour after its visit « sûr » for that patient.
- **The clock-offset search counts bursts, not photos**: at least 3 bursts, landing on at least 2 different patients.
  Counted by photos, one walk-in's series of eight shots falling into some visit 1 h 40 away cleared the bar alone, was
  applied, made its group « sûr » and was saved on the camera.
- **A correction the dentist refused stays refused** (`refusedOffset`, per detected value) through every recount —
  « plus anciennes », another camera's agenda. **A cut stays at the photo it was made on** (`splits` is keyed by the
  first photo of the second half), not at wherever the matcher's proposal moves after a recount.
- **The capture key is the duplicate check, and it is computed from the camera's own file name and size.** The Windows
  app therefore copies a photo under its exact original name, in a folder of its own per photo — a renamed copy would
  not be recognised as the photo the browser path already filed.
- **The page makes every server call.** The shell's only credential (the archive grant) is refused outside its own
  routes, so the shell copies, remembers and keeps, and hands the page what the server must hear (a kept original, a
  complete copy).
- **A detection only raises a notice.** A phone charged on the clinic PC exposes a `DCIM` too; its owner's photos are
  never copied because it was plugged in. Copying starts at « Importer », and « Ignorer cet appareil » stops the
  notices for a device on that PC. The switch is off by default (a `desktop/**` merge reaches every clinic silently).
- **A tap on a thumbnail opens the photo; it never sets it aside.** At 64 px a name card or a face cannot be read,
  and the thumbnails are the last line against a photo in the wrong group. Both decisions live in the viewer:
  « Écarter » (it stays on the card) and « Ranger chez un autre patient » (`takePhotoOut`).
- **A photo taken out of its group asks, and never offers back the patient it left.** What stays is decided again
  and keeps its group's key, so a choice already made on it survives; a group the order check demoted stays demoted.
- **A choice survives a recount only on a group that still holds exactly the same photos** (`unchangedGroupKeys`).
  A clock correction, another agenda or « plus anciennes » used to drop them all; carrying one onto other photos
  would answer a question nobody asked.
- **« Nouveau patient » is under the search, never beside it**: the person is looked for first, and a namesake on
  file is still offered below the results. The create is the booking dialog's (`useInlineNewPatient`).
- **A card's bookkeeping is skipped in silence** (Canon `.CTG`, Nikon `.DSC`, a Sony's `PRIVATE` tree whose
  `THMBNL` JPEGs are video stills, not photos), and **a RAW beside its JPEG is not « ignoré »** — the advice
  « réglez l'appareil en RAW + JPEG » appears only for a RAW alone.
- **A new camera's guessed agenda makes nothing sure.** In a cabinet with several practitioners a camera met for
  the first time follows the signed-in one by guess; until that is picked or confirmed its groups ask and no clock
  correction is searched for — a colleague's camera with a wrong clock could otherwise be « corrected » into the
  wrong dentist's visits and come out « sûr ». Every group of it asks the same one question (« ses photos
  suivent-elles l'agenda de … ? » — « Oui, confirmer » in any card, or the prompt at the top), offers **no**
  candidate from the guessed agenda, and the bar says « confirmez à qui est … » rather than « choisissez le patient de
  12 groupes ». **The guess is never saved** where there was something to guess: « Ranger » without an answer leaves
  the camera unassigned (whole cabinet), instead of making it the importer's for good.
- **A batch the Windows app delivers during a review joins it** (`addToImport`) — « Ajouter les photos plus anciennes
  restées sur l'appareil » is a button of that review — and **« Autre carte » over a review holding a decision asks
  first** (« Abandonner le tri de … ? »). Both used to wipe every choice.
- **A photo already filed is acknowledged by what happened to it**, not « already » blindly: when the cloud keeps only
  the 2 560 px copy and no PC holds the original (`known` carries `originalHeldBy`), the Windows app is told « filed »
  and keeps the original — the case of a filing whose answer was lost on the way back.
- **Two bodies of one model are one camera unless the body writes a serial.** Read from 0xA431, else from a Nikon's
  maker notes (tag 0x001D — every Nikon DSLR measured keeps it only there). Sony (encrypted), Olympus, Panasonic and
  phones give none. The fingerprint enters the capture key, so this must not change once photos are filed in
  production.
- **A file's « appareil » is the model the camera wrote** (« Canon EOS 250D »), never the registry's editable name.
- **The originals live by file id** (`photos\{id[0..2]}\{id}.jpg` + `index.csv`), so « Changer de patient » moves
  nothing on disk, and the folder stays readable without the app.
- **The journal and the staging copies are per Windows account** (`%LocalAppData%`), the originals machine-wide
  (`%ProgramData%`, the coffre's ACL). A store another account created is refused with a sentence; the cloud copy still
  files — losing the PC loses detail, never a photo.
- **A copy on the same physical disk is no copy.** `AddOnlyFolderCopy.SameDiskRefusal` asks the volume's disk extents,
  because `C:` and `D:` are often two partitions of one disk. It **refuses** the photo originals' copy and is only
  **said** in the coffre copy's result: the coffre copied that way before 1.8, and refusing it on update left cabinets
  uncopied and then told, 30 days later, to plug in a disk they had.
- **A drive letter is not a disk** (`ArchiveDisk`). With the archive disk unplugged the next volume takes its letter —
  from 1.8 often the camera's card. Every copy into the archive folder (archive, coffre, mirror, originals) stops when
  nothing, or a camera's card, sits there; the originals' copy also refuses a disk that is not a recorded archive disk.
- **A scan never reads an ignored device**, a named device that is gone answers « n'est plus branché » (never « every
  other device instead »), and « plus anciennes » reads only the devices of the batch on screen.
- **« Importer » says it is alive** (`camera-progress`, at most once a second): the injected script's 15-minute wait
  counts from the last one, so a slow cable is never told « ne répond plus » while copying, and the page shows « N sur M ».
- **The storage bell follows the space both ways.** A deletion, a folder deletion or a new ceiling re-evaluates it after
  the commit; going down never announces — the row is lowered in place — and only under 75 % does it re-arm. Admins only
  (both ways out are theirs). The row id is deterministic per level, so two uploads crossing 80 % at once make one row.
- **A default folder is found by id first, then by name**: a renamed « Photographie » still receives the photos, and a
  patient with no folder at all gets the whole default set, as the « Fichiers » tab would.
- **A kept original is recorded on the file (id + cabinet), never on the route's patient**: « Changer de patient » may
  have moved it after the report was queued. A deleted file answers 404 `file-not-found`, the one answer the page treats
  as final.

## Traps

- **A visit booked with no practitioner belongs to every practitioner's agenda.** Most visits name nobody (32 of 42 in
  prod), and a new camera follows the signed-in dentist by default — an exact `DoctorId` match made the import answer
  « Aucun rendez-vous à cette heure-là » over a visit in the book. The rule lives twice, in `agendaFor` (the one the page
  uses) and in `GetPhotoImportVisitsAsync`, and both say so. Found by the browser walk, not by any unit test.
- **The coffre's copy report used to send this run's bytes as the total.** After the first complete copy every run
  reported « 0 octet », the server answered « incomplete », and the 30-day alert fired over a copy that was complete.
  `AddOnlyFolderCopy.Result` separates `Copied/CopiedBytes` (this run) from `Files/Bytes` (what the destination holds);
  a report is made of the second pair. Pinned by `A_Second_Copy_With_Nothing_New_Still_Reports_Everything…`.
- **⚠️ Behaviour change on update — tell the pilot.** From 1.8 the archive, coffre and mirror copies stop, with a
  sentence, when the archive folder's drive is missing or is a camera's card. A removable drive with a `DCIM` folder at
  its root counts as a card (the sentence names the folder): a cabinet using such a USB stick as its archive disk sees
  its copies stop until the folder goes. A coffre copy onto the PC's own disk keeps running and now says « même disque ».
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
