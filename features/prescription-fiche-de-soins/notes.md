# prescription-fiche-de-soins — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.

## La séance prescrit, et l'ordonnance est une vraie ordonnance

The fiche de soins gained a second folding section, « Prescription », directly under « Notes de séance ». It
holds médicaments (catalogue or free text, with a posologie) and examens (bilan, radio, avis — one free-text
field each), and on save it **emits or updates a real `MedicalDocument` of type `prescription`** for that
séance: printable, e-mailable, listed in the patient's Documents tab, re-rendered by the background PDF job,
with the norm-mandated identity block the shared `DocumentIdentity` composer already produces.

⚠️ **The whole design turns on one finding: almost none of this needed building.** The ordonnance was already a
complete legal artefact — a per-clinic `Medication` catalogue searchable by molecule with a **free-text fallback
that already shipped** (typing a name clears `medicationId` + `dci`), the six-field line
(`name · dosage · timesPerDay · route · quantity · duration`), the R.5132-3 mentions, `renewals`, a PDF
renderer, `PdfGenerationJob`, `DocumentEmailJob`, « Renouveler », and a Documents tab with a **« Séance »**
column already linking to the appointment. So the question was never « how do we model a prescription » but
« how does the fiche write the one that exists ». A `DentalRecordPrescription` child collection was the obvious
alternative and it is this repo's dominant defect shape in its worst form: the dentist would type the ordonnance
twice, and the printed one is the one the patient takes to the pharmacy.

## Un examen est une ORDONNANCE DISTINCTE — et la première version avait tort

A séance that prescribes an antibiotic **and** a panoramique emits **two** documents: a `prescription` for the
médicaments and an `examens` for the examens. One section in the fiche, one save, two papers.

⚠️ **This reverses the decision this feature shipped with**, which was « an examen is a line of the same
ordonnance ». That was chosen because it touched no line formatter — an examen carries only a `name`, so
`PrescriptionContent.FormatLine` already printed it verbatim — and cheapness is not a medical argument. The
rule is that a prescriber writes **« sur des ordonnances distinctes »** the médicaments, the produits et
prestations, and the examens de laboratoire, and three practical facts make it a rule rather than a formality:

- **The sheets go to different people** — la pharmacie, le laboratoire, le centre d'imagerie. An examen
  prescription is single-use unless the prescriber says otherwise, so one sheet cannot serve two destinations.
- **Validity differs per category**, so one sheet would carry two contradictory lifetimes.
- **CNAM reimburses per line**, against the prescription for *that* line: « les ordonnances et prescriptions
  médicales relatives à **chaque** consultation, analyse, examen ou médicament ». One sheet cannot be filed
  with two claims.

And a fourth reason specific to *this* product: our `prescription` document is a **médicament form**.
`ordonnance-certificat-norms` built it to R.5132-3 for listes I/II — voie d'administration, quantité, the
renouvellement mention. « Radiographie panoramique » printed on it is an imaging request on a drug
prescription.

⚠️ **The printed title of both sheets is « ORDONNANCE », deliberately.** That is what the second document
legally is, and what a laboratoire or a CNAM clerk expects at the top of the page; its body's own opening line
(« Prière de bien vouloir faire pratiquer … ») says what is being prescribed. What tells the two apart is the
**app** label — « Demande d'examens » — because the Documents tab is the one place a human has to pick between
them.

⚠️ **No migration, and the round-trip is what makes that safe.** An ordonnance written before the split holds
its examens inside `content.medications`, each marked `kind: "examen"`. The fiche reads them back **as examens**
(`prescriptionKind` in the browser, `PrescriptionLines.Read` on the server), so the next save moves them onto
their own sheet: the médicament ordonnance is rewritten without them and a demande d'examens is issued. Nothing
is touched until a human reopens the séance.
`A_Legacy_Examen_Inside_The_Medications_Array_Moves_To_Its_Own_Sheet_On_Re_Save` pins it.

⚠️ **`kind` stays on the médicament wire even though every line there is now a médicament** — it is what makes
the legacy line above recognisable, and `PrescriptionLineKinds.Normalize` is still the one place « an absent
kind is a médicament » lives. The **examens** array carries no `kind` at all: every line of that document is
an examen because the document type says so, and a per-line discriminator inside a single-kind array is the
half-measure the médicament ordonnance was carrying before the split.

⚠️ **No renouvellement on the examens sheet, and `ExamenBody` has nowhere to put one.** Renewal is a dispensing
concept; an examen prescription is single-use by default, which is one of the reasons the two cannot share a
sheet in the first place. `A_Renewals_Key_Changes_Nothing_About_The_Body` and
`The_Renouvellement_Reaches_The_Medicament_Sheet_Only` pin both halves.

⚠️ **`FicheOrdonnanceEmitter.Split` is the only place the sorting lives**, and the preview shares it — so an
« Aperçu » can never file a line differently from the document the save writes.

⚠️ **Still no line formatter changed.** `ExamenContent` composes the new body and there is nothing to compose:
an examen is printed verbatim. The médicament line's three contractually-identical copies (C# for the PDF, TS
for the A4 preview, TS for the Word export) were untouched by the split as they were by the first version.

⚠️ **The type set is mirrored in five places and nothing validates it** — `DocumentTypes`, the browser's
`DOCUMENT_TEMPLATES`, `DocumentFileNaming`, the PDF title map, the editor's heading map — so a type missing
from one renders as its raw key with no error. That shipped once: a saved arrêt de travail was labelled
`arret-travail` in the patient's own Documents tab. `check:responsive`'s `document-type-set-has-one-owner`
compares the two mirrors that have a total mapping, in both directions.

⚠️ **`creatable: false`, and it is not a soft delete.** The `examens` template is in `DOCUMENT_TEMPLATES`
(without it `documentTypeLabel` prints the raw key) but out of `CREATABLE_DOCUMENT_TEMPLATES`, because the
fiche de soins is its only writer — so the standalone editor needs no form for it, and a row in the Documents
tab opens the **read dialog** instead of an editor that cannot render it. Flipping that flag is the whole of
« let a dentist write one without a fiche », and it needs the editor's form and preview first.
⚠️ `honoraires` is deliberately still *creatable*: its tile is a signpost that intercepts and points at the
Factures module, and dropping it would leave whoever is looking for one with nothing to click.

## MedicalDocument.DentalRecordId, et pourquoi AppointmentId ne suffisait pas

One nullable, un-FK'd, indexed column, the exact shape `AppointmentId` already has. `AppointmentId` could not
do the job: it is null on any fiche entered outside the agenda, and on every day where `DentalRecordVisitLink`
deliberately refuses to guess between several visits — so joining a séance to its ordonnance through the
appointment loses precisely the fiches charted from the patient's own page, which is most of them.

⚠️ **Nothing was backfilled.** The read falls back to the appointment for documents written before the column
(`DentalRecordId IS NULL AND AppointmentId IN (…)`), which gives the legacy rows the same behaviour without
freezing an inference into data. The null test in that predicate is load-bearing: without it an ordonnance
already claimed by one fiche would be claimed again by any other fiche sharing its visit.

⚠️ **The fiche's own document wins over a legacy one sharing its visit**, in the emitter's `FindAsync` and in
the history read — a séance that has issued its own ordonnance must not report the older one instead.

## L'émission est DANS la transaction, jamais post-commit

`FicheOrdonnanceEmitter.EmitAsync` is called from both fiche commands **before** `SaveChangesAsync`, not with
the post-commit side effects beside it. Those — stock consumption, the note d'honoraires, marking the visit
complete — are *derived* and may fail without the clinical record being wrong. A prescription is **entered
clinical data**: a dentist who types an antibiotic, reads « fiche enregistrée » and has no ordonnance has lost
work silently, which is the shape this codebase keeps finding. Either both land or neither does.

## Elle n'efface jamais

⚠️ **Clearing the section and re-saving leaves the ordonnance standing.** Three reasons, each sufficient: the
paper may already be in the patient's hand; `DeleteMedicalDocumentCommand` is `AdminOrDoctor` while the fiche is
open to reception, so the fiche cannot honour the gate; and « patient records resist destruction » is a standing
rule here. The document's own delete path — named, confirmed, role-gated — stays the only way, and the section
says so in words rather than silently keeping a document the user has just emptied.

`Emptying_The_Section_Never_Deletes_The_Ordonnance` is the direct test.

⚠️ **The rule holds PER DOCUMENT.** Removing every examen from a séance that still prescribes a médicament
updates the ordonnance and leaves the demande d'examens exactly as it is — two papers, two histories, and the
one already handed over is not rewritten by the other's edit.
`Emptying_Only_The_Examens_Leaves_The_Demande_Standing` pins it.

## On peut VOIR le document, sur place — et « enregistré » n'a jamais voulu dire « vu »

The document was saved from the day this feature shipped. What the fiche never did was **show** it: the section
promised in prose that an ordonnance would be emitted, and the only way to the paper was the patient's
Documents tab. Reported as « why are we not saving the document » — the answer being that we were, and that a
dentist has no way to tell.

`components/documents/document-preview-dialog.tsx` is a document read where the document is referred to.
Before it, the **only** surface in this app that rendered a saved `MedicalDocument` was the full editor page
`/documents/{type}?id=…`; « Ouvrir l'ordonnance » on a séance row did a `router.push` to it.

⚠️ **The rule the two doors now follow, stated so a third one cannot invent its own:** a **clinical** surface
opens a document to **READ** — the fiche, the séance history — and the **Documents module** opens it to
**EDIT**, with « Modifier » inside the dialog as the door between them.

⚠️ **It frames the real PDF, not a second rendering of it.** The editor has an on-screen A4 block built from
its own form state and lifting that here was the obvious move; it would have been a second renderer of a legal
document, which is the drift this feature already carries three guards against, one per copy of the printed
médicament line. The bytes a pharmacist reads are the only honest preview. It also makes print
`contentWindow.print()` on the actual PDF rather than a cloned DOM subtree — and the frame is reused from
`patient-file-pdf-preview.tsx` (which gained one optional `frameRef`), so the coarse-pointer hand-off is not
written a second time either.

⚠️ **Two sources, and the difference is stated rather than hidden.** `saved` renders the stored document;
`apercu` renders the sheet the fiche is *about to* emit. The aperçu carries **no Imprimer, no Télécharger and
no Envoyer**, with one sentence saying why: a printed ordonnance for an unsaved séance is a legal paper with no
clinical record behind it. It is a line rather than a disabled button, so the reader learns what to do instead
of what is refused.

⚠️ **The aperçu is composed SERVER-side, through the emitter's own method.** `PreviewFicheOrdonnanceQuery`
calls `FicheOrdonnanceEmitter.ComposeAsync` — the same method the save calls — maps the unsaved
`MedicalDocument` through the same `ToDto` → `ToPdfData` path `PdfGenerationJob` takes, and renders. So the
aperçu is the bytes the save will produce, not a rendering that resembles them. Composing it in the browser was
the alternative and the product already has that scar: the document editor posts `clinicName` / `doctorName`
from form state with literal `"[Nom du cabinet]"` / `"Dr. [Nom]"` fallbacks. ⚠️ It **persists nothing and
needs no id**, which is the whole point — on a first save there is no fiche yet, and « montre-moi l'ordonnance
avant de la donner » is asked at the chair.

⚠️ **`DentalRecordDto.DoctorId` was added for it**, and the gap it closes is narrow and real: without it the
browser has nothing to send, the preview falls back to the caller's own Doctor record — correct on a new fiche
(where `PractitionerAttribution` resolves from the caller anyway) and quietly wrong on one reopened from a
colleague's séance, which is the one case where whose cachet is on a prescription matters.

⚠️ **`GET /medical-documents/{id}/pdf` was missing and is the other half.** Until it existed the only renderer
took the *whole document in the body*, so any surface wanting to show a saved one had to re-compose it in the
browser — flatten `ContentJson` to string values, re-send the clinic and practitioner fields, get the
flattening right. That is a second copy of `MedicalDocumentPdfMapping.FlattenContent` per call site. A read of
a document that already exists should be a GET with an id.

⚠️ **« Modifier » routes by `dentalRecordId`, and that is a correctness fix rather than a nicety.** A document
a fiche owns is recomposed from the section on that fiche's **next save**, so editing it in the standalone
editor is work that gets silently overwritten. When a fiche owns it the dialog offers « Modifier dans la
fiche »; otherwise the editor, as before. `MedicalDocumentDto.DentalRecordId` was added for that — and it also
makes the Documents tab's « Séance » column truthful for a fiche charted outside the agenda, which printed
« — » for exactly those rows.

⚠️ **The save toast names the documents now.** A fiche that issues a legal prescription and confirms « Fiche
dentaire enregistrée » has said nothing about the paper now in the patient's file — the same silence the money
toasts one block up exist to end. Plural when both sheets were written.

## Le payload est tri-state, et l'absent n'est pas le vide

`Prescription` **absent** means « unchanged » and the document is not even looked up — which is what keeps every
caller predating prescriptions (and the server-internal writers) clear of one. **Present and empty** means
« nothing prescribed at this séance ». The modal always sends it, like `Acts`: a field a routine re-save forgets
is how this product has lost data before (`SetActs` and `SetProcedures` both replace their whole list).

⚠️ The first version called the emitter unconditionally and **15 existing tests failed**, all of them fiches
with no prescription at all — the read hit an unstubbed repository and the handler's catch-all turned it into a
French business failure. `FicheOrdonnanceResult.None` is the fix and the tri-state is why it is correct rather
than merely convenient.

## Le cabinet et le praticien sont résolus SERVEUR, et c'est plus sûr que ce qui existait

The document editor sends `clinicName` / `clinicAddress` / `clinicPhone` / `doctorName` / `doctorSpecialty`
**from the browser**, with literal `"[Nom du cabinet]"` / `"Dr. [Nom]"` / `"[Spécialité]"` fallbacks
(`document-editor-content.tsx`) — so a failed clinic read there snapshots a placeholder onto a legal document.
The emitter reads them from the database instead, and issues the ordonnance in the name of the practitioner the
**fiche** attributes the work to (`record.DoctorId`, resolved through `PractitionerAttribution`), which is the
answer the client could not supply: a secretary recording a dentist's séance must not put their own identity on
the prescription.

⚠️ **That is what made `DoctorSpecialtyLabels` necessary.** `Doctor.Specialty` stores an English key,
deliberately and permanently, and the map from key to French label lived only in `web/lib/specialties.ts` —
sound while every document was composed by the browser. A document composed server-side would have printed
« Orthodontist » under the prescriber's name on a French legal document. The two maps are now a pair, held in
both directions by `check:responsive`'s **`specialty-labels-have-one-owner`**.

## Ce que la ligne coûte à la fiche, et comment

⚠️ **A line at rest is ONE row carrying the exact sentence that will be printed**; only the line being typed
opens. That is the act card's gesture one section up (`record/act-card.tsx`) — a pile of records of which
exactly one is armed — reused rather than re-derived.

Measured in the browser at 1440 × 900, on the real build rather than the mockup:

| State | Height |
|---|---|
| Folded | **32 px** — exactly what « Notes de séance » costs, measured beside it |
| Open, nothing prescribed | 182 px |
| One line at rest | 38 px |
| Open, one line being typed | 462 px |
| Open, three lines with one being typed | 553 px |

⚠️ **The design mockup claimed 34 / 253 / 329 and the build is bigger.** The figures above are the measured
ones; the mockup's were an estimate and are not repeated anywhere. The comparison it also claimed — « 430 px
for one line of the ordonnance editor » — was never measured and is not restated here either.

The first draft drew every line expanded and was rejected on exactly that. What came out, and why none of it is
a lost capability:

| Removed | Why it is not a loss |
|---|---|
| the label above « Nom » | an empty field says « Ex : Amoxicilline » |
| a « MÉDICAMENT » header row | the coloured left edge and the icon already say it |
| Dosage / Fois par jour / Durée stacked | one row, labels shortened so none wraps at 320 px |
| voie + quantité | behind « Détails », the act card's own fold |
| the « Renouvellement » field | an 18 px mention with an « Ajouter » link |
| a separate « Imprimé : » preview | **the at-rest row _is_ the printed sentence** |

That last row is the one worth keeping: the density saving and the proof-read turned out to be the same feature.

⚠️ **Two add buttons, never a mode selector.** The gesture already says what it creates, and this product has
removed a control of that shape once before — `bridge-identity-and-tooth-gesture`' « the gesture stopped being a
mode ». A segmented « Médicament / Examen » above an empty form asks the user a question before they have
anything to answer it about.

⚠️ **The catalogue picker is an inline column, not a Popover** — `record/act-catalog-picker.tsx` records why in
as many words: a searchable list inside a Popover inside this Dialog gives three components a claim on Enter.
The ordonnance editor uses a Popover for the same catalogue and is right to; it is a page, not a dialog.

## Dans le dossier patient : une ligne, pas une colonne

The séance history says **what** was prescribed — « Prescrit : Augmentin Comprimé 1 g, Ibuprofène 400 mg » — as
one 11 px line inside the existing « Actes » column, and only on rows that have one. No seventh column: that
table is measured at 522 px in a 451 px box, and a column empty on most rows is a column people stop reading.
Below `lg:` the card list carries it as a `Prescription` **field** (absent when there is none, never « — ») with
« Ouvrir l'ordonnance » in the card's one menu.

⚠️ **The word « Prescrit » is visible, not only in the accessible name.** This product has already shipped the
mirror of that mistake — a count whose only qualifier lived in an `sr-only` span, leaving the sighted reader a
bare figure to guess at (N31).

⚠️ **The labels are SERVED** (`DentalRecordDto.prescriptionSummary`, from `PrescriptionLines.ShortLabels`). The
browser has one `shortPrescriptionLabel` too, and that is not an N29 violation: it labels lines the user has
typed and **not yet saved** for the section's own folded summary, which no server can see. Same words, two
genuinely different inputs.

## Sexe et poids sont retirés de l'ordonnance

The practice owner's decision: a Tunisian dental ordonnance does not carry them. Two facts that made it
clear-cut — `patientWeightKg` was optional and nearly always blank, while `patientSex` was **prefilled from the
patient record**, so it printed on every ordonnance ever issued.

Removed from the one render owner (`DocumentIdentity.PatientLines`), from `MedicalDocumentPdfData`, from
`MedicalDocumentPdfMapping`, and from the editor's nine sites (two form fields, the A4 preview block, the
formFields entry, the gender prefill effect, the hydration, the reset, and three content writers). The orphaned
`Suffixed(" kg")` helper went with them.

⚠️ **Nothing was migrated, and re-rendering an old ordonnance now omits two lines it used to print.** That is
the intended behaviour. Legacy documents keep both keys in their `ContentJson`; they simply stop being read.

⚠️ **Tombstones matter more than usual here**, because `ordonnance-certificat-norms`' spec calls sexe
R.5132-3-mandatory and is otherwise still accurate — so a reader working from it will re-add them unless the
render site says it was deliberate. `A_Legacy_Document_Prints_Neither_Sexe_Nor_Poids` is the test.

## Le piège que seul l'œil a trouvé : deux boutons ont poussé toute la section hors du cadre

⚠️ **Reported from the phone — « it's beyond bounds, the writing and cards » — and it was one un-shrinkable
pair of buttons.** `Button` is `whitespace-nowrap shrink-0`, and `flex-1` does **not** remove that: they are
different tailwind-merge groups, so « + Médicament » / « + Examen » ended up `flex: 1 1 0%` **and**
`flex-shrink: 0`. Their combined min-content is **253 px**, and because `RecordSection`'s body is a
`display: grid`, that sized the single implicit track — so at 320 px, in a **231 px** box, *every* sibling
became 253 px: the prescription rows, the renouvellement field and the closing sentence all ran past the
dialog's edge together. The add row was the cause; the rows were the symptom.

Two fixes, both § 10.1's family:

- the row is `flex flex-wrap gap-2` and each button `min-w-0 shrink grow basis-32`, so they stack at 320 px and
  sit side by side from 390 px up — `shrink` spelled out is what beats the primitive's `shrink-0`;
- `min-w-0` on the lines list, on each line card and on the armed line's inner grid, because a `truncate`
  descendant (`white-space: nowrap`) makes a row's min-content the **whole** string, and as grid items with
  the default `min-width: auto` they refused to shrink — 272 px in a 233 px box on its own.

Measured after: **0 px of overflow** on the document, the dialog body *and* the section, at 320 · 390 · 820 ·
1180 · 1440, with no label wrapping at any of them.

⚠️ **`tsc`, `check:responsive` and `npm run build` were all green while this was live**, which is exactly what
§ 14 says about the eye pass being the load-bearing half. The first probe missed it too: it walked only the
children of elements that already overflowed, so the offending row — whose *parent* fitted — was never
visited. Walk every descendant.

## Deux pièges rencontrés, tous deux silencieux

⚠️ **The JSON must be camelCase**, and this is a requirement rather than a style: the document editor reads
these lines back with plain property access (`med.timesPerDay`), which is case-sensitive, while the C# reader is
deliberately case-**insensitive**. A PascalCase write would hand a dentist an ordonnance whose posologies had
silently emptied, and no server-side test would have noticed.

⚠️ **`PractitionerRenderSnapshot.ApplyTo` re-serialises the whole object with the default encoder**, so the
stored bytes hold `Ibuprofène`. Setting a relaxed encoder in `PrescriptionLines` achieves nothing — the
next call undoes it — and escaped non-ASCII **is** the stored corpus for every document the API has ever
written, editor-authored ones included. The only thing it breaks is a test asserting on the raw string, which is
what it did; assert on the parsed value.

## Les quatre checks dérivés

- **`prescription-line-has-one-owner`** (N32) — `, Nx par jour` is the fragment only the line assembly
  produces, so a real string literal outside `lib/documents.ts` is a second composer. Prose is fine; comments
  are masked.
- **`specialty-labels-have-one-owner`** (N32) — compares `web/lib/specialties.ts` with
  `DoctorSpecialtyLabels.cs` in **both** directions, because a key removed from one side is the same defect as
  a key added to the other.
- **`document-type-set-has-one-owner`** (N33) — every `DocumentTypes` constant has a `DOCUMENT_TEMPLATES` entry
  and a `DocumentFileNaming` arm, both directions. It fires on the day a seventh type is written, which is the
  only day anyone would remember to check the other four mirrors.
- **`examens-intro-has-one-owner`** (N33) — the demande d'examens' opening formula, singular and plural,
  compared byte-for-byte between `ExamenContent` and `web/lib/documents.ts`. That sentence is the only thing on
  the sheet that says what is being prescribed rather than merely listing it.

All four were proven to fail on a deliberate violation before being trusted green.

⚠️ **The first version of the intro check matched nothing and reported both sentences as missing** — a
`new RegExp(name + '\s*=\s*"…"')` written through a shell heredoc, which collapses the doubled backslash
and leaves the pattern looking for a literal `s`. It is an `indexOf` on the declaration form now. The lesson is
the one already in this repo's memory: a check that cannot fail is indistinguishable from a check that passes,
which is exactly why red-proofing is not optional.

## Ce que la marche de bout en bout a trouvé — deux vraies pertes de données

The walk that mattered was not the one that proved the happy path. Three defects came out of it, and the first
two would each have lost work in a working cabinet.

### 1. L'aperçu ne nommait pas le prescripteur (trouvé par l'aperçu lui-même)

The very first screenshot of the aperçu showed the cabinet's letterhead and **no practitioner**. The cachet, the
n° CNOMDT and the cabinet's city all fall back to the caller's own `Doctor` record when the fiche attributes
nobody (`PractitionerRenderSnapshot.ResolveAsync`), while the printed **name** and **spécialité** did not — so a
document could carry one practitioner's cachet above a blank « Dr. » line. `ComposeAsync` now applies the same
fall-through, and `With_No_Attributed_Practitioner_The_Name_Falls_Back_To_The_Caller_Like_The_Cachet_Does` pins it.

⚠️ **It was findable only because the preview goes through the save's own method.** On a fiche that does not
exist yet there is no `record.DoctorId` to send, so the preview hit the case the save never reaches. A preview
composed in the browser would have shown whatever the browser had.

### 2. La fiche écrasait silencieusement l'ordonnance modifiée ailleurs

Sequence, all observed: the document held one value → the fiche modal opened and read it → a colleague changed it
through `/documents/prescription` (confirmed on disk) → the fiche saved → **the colleague's edit was gone**, with
a green success toast and no refusal.

⚠️ **The comment on that very line asserted the opposite** — « the document's own xmin travels with the tracked
entity, so a concurrent edit surfaces as a 409 ». It cannot. `EmitOneAsync` loads the document *inside* the
fiche's transaction, so the tracked entity always carries the current token and its UPDATE always matches; the
stale copy is in the browser's section state, which `xmin` has no way to see. The claim was inherited from the
first version of this emitter and re-asserted by the split, and no test could contradict it — nothing in
`UnitTests` touches a database, and the browser walk that finds it has to involve two actors.

The fix is this codebase's ordinary one: the modal round-trips the tokens it read (`prescriptionDocumentVersion`,
`examensDocumentVersion`) and the emitter declares them with `SetExpectedVersion`, so a mismatch is the
`ConflictException` both fiche commands already let through. ⚠️ **0 means « not supplied »** and turns the check
off, which is what keeps every older client and every server-internal writer behaving exactly as before; and
⚠️ **each sheet carries its own token**, because one document's edit must not refuse the other's save.

### 3. Le 409 disait « Rechargez » sans offrir de quoi recharger

With the refusal in place the modal showed the right sentence and **no « Recharger »**. `useConflict` exposes
`isConflict` precisely to drive that control, `FormErrorBanner` takes an `action` for it, and every other dialog
in the app passes one — the fiche was the only one that did not. That is the poisoned-dialog trap the root guide
records (a form whose held version never moves repeats the refusal for ever); it mattered less while the only 409
came from the fiche's own row, and this change made it reachable by an ordinary two-person afternoon.

« Recharger » now re-reads the record's version **and both documents**, which is exactly what the server's
sentence promises (« Rechargez pour voir la version à jour, puis appliquez à nouveau votre modification »), and
takes the banner down through `clearMessage` — not `setError(null)`, so a second consecutive 409 still escalates
to « coordonnez-vous ». Verified as a loop: refused → Recharger → the colleague's version in the section →
banner gone → save accepted.

⚠️ **Seven of the walk's failures were the probe, not the product** — `aria-label="Dent 16"` rather than the
visible « 16 », a line remover named after the line (« Retirer Bilan sanguin : NFS, glycémie de l'ordonnance »)
rather than the kind, sonner having dismissed a toast before it was read, CSS-uppercased card labels failing a
case-sensitive test, the hidden `TABLE_ONLY_LG` tree's controls found instead of the card tree's, a tab that
needed longer than the fixed delay, and the wrong card's menu opened. The repo's own rule — « a failing check is
a claim about your probe until you have excluded that » — held at 7 of 10.

## Le geste qui a survécu au collage

`web/lib/documents.ts` now owns the ordonnance's line vocabulary — `PrescriptionLine`,
`formatPrescriptionLine`, `formatRenewalMention`, `shortPrescriptionLabel`, `PRESCRIPTION_KINDS`,
`prescriptionKind`, `emptyPrescriptionLine`. The type and both formatters were **module-private inside a
~4 000-line component**; they were hoisted rather than copied, and `document-editor-content.tsx` imports them
now. `MedicationLine` is re-exported under its old name so that file's existing call sites read unchanged.

## Vérification

`dotnet test` unfiltered: **4449 / 4449** (4413 before the split; the 36 new ones cover the two sheets, the
examens body, the wire shape, the legacy round-trip, the practitioner fall-through and the document version
round-trip). `check:responsive`: **60 / 60** (the two new checks
red-proofed first). `npx tsc --noEmit`: clean. `npm run build`: passed. `verify-schema`: the migration's column and index
confirmed in the catalog (`DentalRecordId uuid` nullable + `IX_MedicalDocuments_DentalRecordId`); the four DRIFT
lines in that run are pre-existing and unrelated — clinic signups, the dev key ring, and secrets under a
superseded generation, none of which touches `MedicalDocuments`.

⚠️ **A before/after `verify-schema` diff was not possible**: a concurrent session had the dev API running, and
its startup auto-applied the migration before the first snapshot could be taken. The column, the index and the
nullability were verified directly against `psql` instead, and the scaffolded migration was read line by line
for the two traps that matter — no `AddColumn<uint>("xmin")` for the 38 entities, and no `DropColumn` above a
backfill.

⚠️ **No migration in the split batch, and none was needed.** `examens` is a new *value* in the existing
`MedicalDocuments.DocumentType` column — that column has never been constrained to a list, on purpose (the
server validates per type, not against a set), which is exactly why the five mirrors matter and
`document-type-set-has-one-owner` now holds them.

## Ce qui reste ouvert, nomme

- **The standalone editor has no form for a demande d'examens** (`creatable: false`). A dentist cannot write
  one without recording a fiche. That is the current design — a séance prescribes — and the door out of it is
  the flag plus a form and a preview in `document-editor-content.tsx`.
- **The Documents tab still opens the editor** for every type but `examens`. Making every « Ouvrir » read-first
  was offered and declined for now: it changes a surface this work was not about.
- **« Avis d'un confrère » is offered as an examen line**, which is defensible on paper and is not the same
  artefact as a `liaison` letter. If a cabinet wants prose addressed to a named confrère, that type already
  exists and is the better answer.
