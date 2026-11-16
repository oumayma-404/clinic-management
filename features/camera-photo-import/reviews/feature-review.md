# Feature review — camera-photo-import (2026-11-16)

**Scope:** branch `feature/camera-photo-import` against `origin/main` (d46c3d11, 0 commits behind on 2026-11-16) —
the 9 local commits plus the uncommitted pre-merge fixes. Web, API, Windows app, vendor console.

**Method:** six parallel review agents (code quality · error handling / `Result` · business logic · breaking changes ·
device & UX · robustness), every finding then checked against the source before it was fixed. Two more inputs fed the
same pass: 38 real camera files run through the EXIF reader, and browser run 2 ([`../qa/run-2.md`](../qa/run-2.md)).
The fixes were made by two helpers (server, Windows app) and in the web directly; every helper diff was read before
it was kept.

## Confirmed and fixed

| Sev. | Where | What was wrong | Fix |
|---|---|---|---|
| Major | matcher | A fiche charted at the end of the day made every later unbooked hour « sûr » for that patient | the fiche stretches its visit 60 min at most, never past the next arrival |
| Major | matcher | One walk-in's burst of 8 shots, landing 1 h 40 away, cleared the clock-offset bar alone and was applied and saved | the offset counts bursts: ≥ 3 bursts on ≥ 2 patients |
| Major | matcher | A refused clock correction came back at the next recount | `refusedOffset`, per detected value |
| Minor | matcher | A cut followed the matcher's proposal after a recount | cut kept at the photo it was made on |
| Major | matcher | A new camera's groups offered candidates from a *guessed* agenda; « Ranger » saved the guess for good | its groups ask one question, offer no candidate; the guess is never saved |
| Major | EXIF | The camera's own zone moved real photos an hour onto the neighbouring visit (38 files) | the zone is not applied; the clock face wins |
| Major | EXIF | Two Nikon DSLR bodies of one model were one camera (serial only in the maker notes) | serial read from Nikon tag 0x001D |
| Minor | EXIF | Orientation 5 and 7 swapped | fixed, checked against libvips |
| Major | session | A Windows-app batch arriving during a review restarted the import and lost every choice | added to the review (`addToImport`) |
| Major | session | « Autre carte » dropped every decision with no warning | asks « Abandonner le tri de N photos ? » |
| Major | session | A photo filed before (answer lost on the way back) was acknowledged « already », so no PC kept its original | acknowledged by what happened; `known` now carries `originalHeldBy` |
| Minor | session | A thumbnail range past the end of the file; blob URLs kept on failure | dropped; revoked on every failure path |
| Minor | session | A file's « appareil » was the registry's editable name | the model the camera wrote |
| Minor | sources | One unreadable subfolder failed the whole card | skipped and counted |
| Major | server | A renamed « Photographie » made every photo for that patient fail (a second entity under the same key) | default folders found by id first; a patient with none gets the full set |
| Major | server | After « Changer de patient » a kept-original report was refused for ever (looked up under the old patient) | found by file id + cabinet; a deleted file answers 404 `file-not-found`, final |
| Major | server | The 80/95 % bell never went down after deletions or a raised ceiling | re-evaluated after delete, folder delete and quota change; lowered in place, silently; admins only |
| Minor | server | Two uploads crossing 80 % together wrote two bell rows | deterministic id per level |
| Minor | server | A post-commit failure in the bell could delete the committed file's blobs | moved out of the cleanup scope; logged |
| Minor | server | « 0,0 Go » under 1 Go; console refusals reaching the catch-all | the refusal's own size wording; named refusals with codes |
| Major | Windows app | « Importer » read devices somebody chose to ignore | never |
| Major | Windows app | A named device gone → every other device read instead | « n'est plus branché » |
| Major | Windows app | 500 answers rewrote the journal on the window's thread (« Ne répond pas ») | one background queue; journal saved per burst, flushed on close |
| Major | Windows app | With the archive disk unplugged, the camera's card took its letter and received the copies | `ArchiveDisk`: refused when nothing or a card is there; originals also refuse an unknown disk |
| Major | Windows app | The coffre copy on the PC's own disk was refused on update — cabinets silently uncopied | said, never refused |
| Minor | Windows app | A capped listing kept the oldest photos | keeps the newest |
| Minor | Windows app | `index.csv` rewritten per photo; a sheet open in Excel undid a kept original | appended; held back until it can be written |
| Minor | Windows app | A slow cable copying > 15 min was told « ne répond plus » | `camera-progress` restarts the wait; the page shows « N sur M » |
| Minor | Windows app | A kept original the server refuses was re-sent for ever | at most 20 page loads, 20 per load |
| Minor | docs | `bridge.md` listed the methods but not the messages the shell posts | « The camera's messages » table |

The browser findings of run 2 (« Autre carte », owner candidates, the search losing its name, the clock-reset hint,
the viewer's counter) are in [`../qa/run-2.md`](../qa/run-2.md) and were fixed in the same pass.

## Decided, not changed

| What | Why |
|---|---|
| A removable drive with `DCIM` at its root counts as a camera card for the copies | the alternative writes patient records onto a card leaving in a camera bag; the sentence names the folder |
| `index.csv` keeps the patient name it was written with after a « Changer de patient » | the file id is the key; the sheet is a convenience, appended only |
| The Windows toast's « Ignorer cet appareil » is small and has no undo there | Windows draws the toast; the setting is reversible in Paramètres |

## Gates after the fixes

API 7222/0 (6 skipped) · Windows app 270/0 · web `tsc` 0 · `check:responsive` 88/88 · pure specs 29/29 · build OK.
