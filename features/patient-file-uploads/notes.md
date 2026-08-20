# patient-file-uploads — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## What may be uploaded has one authority, and the browser is told rather than trusted (`patient-file-uploads`)

`Application/Common/Files/` is the single catalog — entries keyed on **extension**, never on the declared
content type (Windows registers none for `.stl`, `.dcm`, `.ply` or `.obj`, so a MIME allow-list could not admit
a single STL however many types were added to it), each carrying its own cap, its French label, whether a
browser can paint it, and a signature rule that is `Required` / `Advisory` / `None(reason)` **with an offset**,
which is what makes DICOM's `DICM` at byte 128 expressible. All six upload doors name a profile;
`FileContentValidation` and `UpdateDoctorProfileCommand`'s three private magic-byte copies are deleted.
⚠️ **The reported bug was two defects stacked**: the `.txt`-renamed-to-`.pdf` refusal was *correct*, and
`web/lib/api/patient-files.ts` read `errorData.message` while the backend sends `{ error }` — so the French
explanation was replaced by an English « HTTP 400: Bad Request ». Fixing it removed the last raw `fetch` from
`lib/api/`.
⚠️ **`GET /api/meta/upload-policy` serves the policy the picker renders** — the `accept` string, the per-format
caps, and the server's *own* refusal sentences — so the instant client-side refusal cannot word things
differently from the server that re-checks it, and a widened catalog cannot leave a stale constant hiding
formats the server would take. A failed probe leaves the picker fully open: the server is the guard, the
pre-check is a courtesy.
⚠️ **A file can now be renamed, described and moved** (`PUT /api/patients/{id}/files/{fileId}`), the first
caller of four entity methods that had shipped with none. `PatientFile.Rename` takes a **base** name and
recomposes from the *stored* extension, so changing a file's format through the API is unrepresentable rather
than merely refused — and the editor shows the extension as a fixed suffix for the same reason. Both PUTs are
`AnyClinicRole`: **record yes, erase no**, the same line the clinical record is on.
