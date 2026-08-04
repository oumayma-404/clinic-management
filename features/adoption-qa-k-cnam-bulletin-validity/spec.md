# Spec: Adoption QA — K (a BS1 the caisse accepts)

**Status:** APPROVED
**Type:** Small (single-theme multi-item pass — one catalogue, one validation gate, one practitioner, one print path)
**Created:** 2026-08-03
**Scope:** Full
**Branch:** new, off `main`
**Feature:** Make the bulletin de soins leave the cabinet **valid** — real CNAM act codes, the mandatory fields refused when absent, the treating practitioner's own code PS, and a Print button that works — closing the three CNAM Blockers and the six defects around them.

> **Why this is one feature, not several.** CNAM is the reason a Tunisian dentist buys practice software at all, and today every bulletin filled from the act picker is rejected on the code column. Every item below is on the path from « je remplis un BS1 » to « la caisse l'accepte ». Nothing here is optional to that outcome.

## Context — what the review confirmed

- **The two CNAM act catalogues are disjoint and the bulletin reads the wrong one.** `CnamCatalogSeed` seeds 26 internal mnemonics as `CodeActe` — `DETART`, `OBT-1F`, `OBT-2F`, `OBT-3F`, `EXT-SIMPLE`, `EXT-TEMP`, `EXT-COMPLEXE`, `PROTH-1`, `PROTH-COMPL`, `PANO`… — into `CnamNomenclatureEntry`. The genuine Tunisian nomenclature, **100 real `DCH010010`…`DCH060150` codes**, is seeded by `DentalActCatalogSeed` into `DentalActCode`, which the BS1 renderer never reads. `document-editor-content.tsx:732` loads the *former* and `selectNomenclatureEntry:872` writes `entry.codeActe` straight into the row `CnamBs1BulletinRenderer.StampActs:286` stamps.
- **`DentalActCode` is a strict superset of `CnamNomenclatureEntry`** — identical ten fields (`ClinicId`, `CodeActe`, `DesignationFr`, `LettreCle`, `Coefficient`, `Category`, `IsActive`, `IsProvisional`, `CreatedAt`, `UpdatedAt`) **plus** `DefaultFee` and `RequiresAccordPrealable`. This is what makes the fix cheap.
- `CreateMedicalDocumentCommand` has **no `bulletin-cnam` validation branch** — the only checks are "honoraires is retired" and "liaison needs a recipient". Downstream every field degrades silently *by design*: `DrawLeft` returns on blank, and the régime and lien `switch`es tick nothing for an absent value. `CnamInfo` validates nothing — every field optional.
- `selectedDoctor = currentUserDoctor || doctors[0]` — a silent fall-back to the first doctor in the roster whenever the logged-in user has no linked `Doctor` (a secretary always). There is **no `setSelectedDoctor` in the file**; no picker exists. A missing code is equally silent, unlike the certificat's CNOMDT field which says « Aucun numéro d'ordre sur votre profil ».
- Print `ref={documentRef}` sits on the `<Card>` in the **else** branch of the `bulletin-cnam ? … : (…)` ternary; for a bulletin the preview is an `<iframe>`, so `documentRef.current` is null and `handlePrint`'s guard returns « Le contenu du document n'est pas disponible pour l'impression ». The Print button is in the shared Actions block, not gated on type.
- Verified **not** defects: the bundled `Assets/BS1.pdf` is a genuine 2-page A4-landscape form whose MediaBox matches the renderer's declared space exactly, and lettre-clé (VLC) values **are** per-clinic editable without a deploy, admin-only, with seeded values flagged `IsProvisional` « à vérifier ». The mechanism is right; the codes feeding it are not.

## What Changes

### K1 — The bulletin reads the real nomenclature (Blocker)

**Recommended: re-point the bulletin's act picker at `DentalActCode`.**

- `document-editor-content.tsx` loads `dentalActsApi.list()` instead of `cnamNomenclatureApi.list()` for the act lookup; `selectNomenclatureEntry` writes the real `DCH…` `CodeActe`.
- `LettreCle` and `Coefficient` both exist on `DentalActCode`, so the cotation cell and the reimbursement estimate keep working unchanged — `parseCotation` and `estimateReimbursements` take a lettre clé + coefficient and do not care which table supplied them.
- **Free win:** `DentalActCode.RequiresAccordPrealable` is correctly seeded (all of Prothèse, ODF, most Parodontologie) and currently consumed only by its own admin table. Once the bulletin reads this entity, show « Accord préalable requis » on the act row. That closes a separate deal-breaker gap at no extra cost, and is the strongest argument for this fork.

*Fork considered and rejected:* rewriting `CnamCatalogSeed`'s 26 `CodeActe` values to real DCH codes. It needs a data migration for existing rows, leaves **two** catalogues of near-identical shape to keep in step, and still would not carry `RequiresAccordPrealable` or `DefaultFee`.

⚠️ **Do not retire `CnamNomenclatureEntry` in this feature.** `/cnam-nomenclature` (the admin table + `CnamLetterValuesCard`) and `GetReimbursementEstimatesQuery` read it, and the VLC letter-value table hangs off that area. Consolidating the two entities is a follow-up worth doing — record it — but folding it in here would make this a Full feature.

### K2 — A bulletin with missing mandatory fields is refused (Blocker)

- `CreateMedicalDocumentCommand` (and the update path) gain a `bulletin-cnam` branch refusing, with a French message naming each missing field: **identifiant unique**, **régime** (CNSS/CNRPS/Convention bilatérale), **lien de parenté** (assuré/conjoint/enfant/ascendant, plus rang where the lien requires it), **at least one act**, and the practitioner's **code conventionnel**.
- Editor-side: mark the missing fields before the user reaches Save, and disable Save with the reason as **visible text** (a `title` is unreachable on touch). The liaison recipient check at `:1517` is the pattern to follow.
- ⚠️ The renderer's silent-degradation behaviour (`DrawLeft` returning on blank, the two `switch`es ticking nothing) is **correct for a renderer** and stays. Validation belongs at the write, not in the drawing code. Do not make the renderer throw.
- ⚠️ The régime and lien `switch`es are keyed on exact French strings from the frontend `<SelectItem value>` — verify byte-for-byte (casing **and** accents: « Convention bilatérale » carries the accent) while the file is open. A mismatch ticks nothing and raises nothing.

### K3 — The treating practitioner is chosen, never guessed (Blocker)

- Add a practitioner selector to the bulletin editor, defaulting to `currentUserDoctor` when there is one and otherwise to **nothing selected** — never `doctors[0]`.
- The selection feeds `doctorCodeProfessionnel`, which `StampActs` prints on every act row.
- A practitioner with no code conventionnel gets the certificat's treatment: « Aucun code conventionnel sur le profil de ce praticien », with a link to `/mon-profil`. Combined with K2 this makes an unstampable bulletin unsaveable rather than silently wrong.

### K4 — Print works for a bulletin (Major)

- Move `ref={documentRef}` so it applies in **both** branches of the ternary, or gate the Print button on document type and print the `<iframe>` for a bulletin. **Recommended: the latter** — the bulletin preview genuinely is an iframe over the overlaid PDF, and printing that is a different operation from printing a `<Card>`; conflating them is what produced the bug.
- « Imprimer » on a BS1 currently **always** fails, and the BS1 is the one document a conventionné dentist prints all day.

### K5 — The Word export does not lie (Major)

`generateWordInternal`'s branch chain (`prescription` / `liaison` / `certificat`) ends with **no `bulletin-cnam` branch and no else**, so the button produces a .docx containing only the letterhead and a signature line — and the success toast still fires.

**Recommended: hide/disable « Télécharger Word » for `bulletin-cnam`** with a short reason. A BS1 is a stamped overlay on an official pre-printed form; a Word rendering of it has no legitimate use and could be mistaken for a submittable document. *Fork:* implement the branch — rejected as work with no user.

### K6 — « Pré-remplir depuis les soins » includes today (Major)

`if (to && d > to) return false` compares a full `interventionDate` timestamp against `new Date("2026-08-03")`, which parses as midnight **UTC** — so any care recorded after 00:00 UTC on the end date fails, and the upper bound is exclusive of its own day. With « Au » set to today, today's séance is silently omitted and the bulletin is filed one act short.

- Compare on the clinic-local calendar day. The client-side counterpart of `ClinicClock` is `lib/format.ts` — extend it rather than inlining a second rule (`todayLocalIso` already lives there for exactly this class of bug).
- The lower bound is unaffected; fix both for symmetry and assert it.

### K7 — The identifiant unique is validated, not truncated (Major)

`for (var i = 0; i < idu.Length && i < IduCellCentersX.Length; i++)` silently drops digits past the 10 declared cell centres, with no log and no failure — so a longer number prints cut off mid-way.

- Validate the length on entry (`edit-patient-dialog.tsx:1313` is a free-text `Input`; `CnamInfo` constrains nothing) and refuse at K2's gate.
- The renderer additionally logs at Warning if it ever receives more digits than cells, so a future form revision surfaces instead of truncating.

### K8 — Amounts on the form use the product's separator (Minor)

`amount.ToString("0.000", CultureInfo.InvariantCulture)` prints `30.000` with a period on a CNAM document. The millime precision is right; the separator is not — and the editor's own comment at `document-editor-content.tsx:2402` rejects exactly this for its on-screen figure and uses `formatDT`. The server renderer that produces the actual paper does the opposite. Use the fr-TN convention.

### K9 — A generation failure says what broke (Major)

`MedicalDocumentsController:309` returns `BadRequest($"Error generating PDF: {ex.Message}")` — a bare string, not the canonical `{ error }` body — so `generatePdfForDownload` throws a plain `Error`, `handleDownloadPdf` only surfaces the message `if (error instanceof ApiError)`, and the two fail-fast French operator messages in `CnamBs1BulletinRenderer` (120, 132) and `Bs1FontResolver` (67) are **structurally unreachable**. Route it through `ApiControllerBase` so the real reason reaches the toast.

### K10 — The seeded lettre-clé values are the old tariffs, so every estimate under-promises (Blocker)

Discovered by the regulatory research after this spec was drafted. `CnamCatalogSeed.BuildLetterValues()` seeds:

| Lettre-clé | Seeded | **Convention, in force 01/01/2021** |
|---|---|---|
| `Cd` consultation médecin dentiste | `7m` | **30,000 DT** |
| `Cds` consultation spécialiste/orthodontiste | `10m` | **45,000 DT** |
| `D` acte de soins dentaires | `1.200m` | **3,000 DT** |
| `Vd` | `10m` | Unverified |
| `Rd` | `2m` | Unverified |

Source: **Convention sectorielle des médecins dentistes de libre pratique** (CNAM + STMDLP, Dec 2020), approved by **arrêté du ministre des affaires sociales du 3 février 2021**, JORT 2021-014. **Confirmed** (primary text). The convention notes the prior values were Cd 18,000 and D 1,700 — so the seeded numbers are older still, and are revised every three years against SMIG/CPI.

The estimate is `coefficient × VLC × rate`, so **every reimbursement figure shown to a patient is understated by roughly 60–75 %**.

- Correct the three confirmed values in the seed. Leave `Vd`/`Rd` as-is but keep them `IsProvisional`.
- **Existing clinics must be offered the correction, not silently overwritten** — a clinic may have already edited its own values, and clobbering an admin's deliberate entry is worse than a stale default. Update only rows still flagged `IsProvisional` (untouched since seeding); surface the rest as a prompt on `/cnam-nomenclature`.
- Keep the `IsProvisional` « à vérifier » mechanism. It is the design that makes this recoverable and it worked as intended — the defect is the shipped number, not the model.
- ⚠️ Add the convention's revision cadence (every three years) to the admin screen's help text, so the next staleness is expected rather than discovered.

### K11 — Prostheses no longer require accord préalable (Major)

`DentalActCatalogSeed` flags **all of Prothèse** as `RequiresAccordPrealable = true`. Since **April 2019** dental prostheses are covered **hors plafond and without a demande d'accord préalable** (**Likely** — Tunisian press, consistent with convention art. 7's *« ou hors plafond »* wording). With K1 wiring this flag into the bulletin editor, a wrong flag becomes a visible wrong warning.

- Clear the flag on the Prothèse rows.
- ⚠️ **Which act families genuinely require accord préalable is Unverified.** The convention (art. 24) confirms the *mechanism* in detail — a completed demande on CNAM's model plus a confidential medical report carrying the diagnosis, the prestation and **its code** — but the list itself is fixed by an **arrêté conjoint** the research could not retrieve. So: correct the Prothèse rows (sourced), leave Parodontologie and ODF flagged as-is, and mark the whole flag `IsProvisional`-equivalent in the admin UI so a dentist can correct it per clinic. **Do not invent the list.**

## Data / Schema Changes

- **None required.** K1 re-points a read at an existing, already-seeded per-clinic table; K2–K9 are validation, UI and formatting.
- If K7's length constraint is enforced in `CnamInfo`, that is a value-object change with no column change.
- No migration ⇒ no `verify-schema` run needed, but run it anyway if the batch is combined with J.

## API Contract

### POST /api/medical-documents, PUT /api/medical-documents/{id}  (K2) — new refusals
Errors: `400` with the canonical `{ error }` body naming every missing mandatory field, French.

### POST /api/medical-documents/{id}/pdf  (K9) — error shape corrected
Errors: `400` `{ error }` (was a bare JSON string), so the renderer's own French message surfaces.

### No new endpoints.
The bulletin's act lookup moves from `GET /api/cnam-nomenclature` to the existing `GET /api/dental-acts` (K1). Both already exist and both are per-clinic.

## Out of Scope

- **Consolidating `CnamNomenclatureEntry` into `DentalActCode`.** They are near-duplicates and should become one, but `/cnam-nomenclature`, `CnamLetterValuesCard` and `GetReimbursementEstimatesQuery` all read the former. Follow-up.
- **CNAM annual ceiling / plafond in the estimate.** Still deferred, but now with sourced numbers to build against when it is picked up. Effective **1 February 2024** (**Likely** — two Tunisian outlets in agreement, no official CNAM page retrieved): **450 DT** for the insured alone; **675 / 900 / 1 125 / 1 350 DT** at 1/2/3/4+ dependants; **+150 DT dedicated to soins dentaires externes**; +100 DT per dependent parent; +100 DT per dependent disabled child; +150 DT pregnancy. **Cone Beam is hors plafond.** So is a **dental prosthesis** (since April 2019). Needs per-patient year-to-date tracking; a feature of its own. Note the estimate over-promises for a patient near the ceiling *and* under-promises via K10 — fix K10 first, since it affects every patient rather than a few.
- **Reimbursement rate.** The calculator's 70 % / 60 % age bands are **not verified**. The general private-filière rate is **70 % of the tarif conventionnel** (30 % co-payment — **Likely**, from a WHO worked example on a GP consultation); a **dental-specific** percentage could not be sourced at all. Leave the bands as they are and do not "correct" them without a primary source.
- **Arrêt de travail on the official form.** `features/cnam-arret-travail-overlay/` holds only assets (`CMIATMP.pdf`, `p61.pdf`, `P61_2024.pdf`) — no spec, no code. A second overlay renderer, same size as the BS1 work. Deferred deliberately.
- **Télétransmission of any kind.** Whether CNAM offers an electronic channel to dentists is unverified (the regulatory research did not complete); this feature keeps the BS1 a printed overlay.
- **Allergy cross-check on the ordonnance** (the prescription branch never reads `patient.allergies`). Patient-safety class, belongs with the clinical-record work, not here.
- **Arabic on any document.** No i18n framework exists and the BS1's first Linux font candidate carries no Arabic coverage.
- **Paediatric dosage forms** in the medication catalogue (all 26 seeded rows are adult forms) — a seed-data change, not this.
- Verifying the overlay's coordinate calibration. It is documented as numerically extracted and visually verified; no automated test pins it and adding one is not in this scope.

## Edge Cases (Critical only)

- **K1 must not orphan existing bulletins.** A `MedicalDocument` already saved with a `DETART`-style code must still open and print. The stored act rows are a snapshot — re-pointing the *picker* must not rewrite history, and the renderer must keep stamping whatever code the row holds.
- **K1 must not break the reimbursement estimate.** `estimateReimbursements` aligns results to items **by index** and takes a lettre clé + coefficient; confirm `DentalActCode.Coefficient` being **nullable** (it is; `CnamNomenclatureEntry.Coefficient` is not) is handled — a null coefficient must not silently estimate zero, which is indistinguishable from « non remboursable ».
- **K2 must not make a legitimately partial bulletin unsaveable mid-typing.** Validate on save, not on every keystroke; a draft the dentist is halfway through must still be savable if the product supports bulletin drafts — check whether it does before choosing.
- K2's régime/lien values must match the frontend `<SelectItem value>` strings **byte-for-byte**, accents included, or the validation passes and the checkbox still ticks nothing.
- K3: a clinic with exactly one dentist must not gain a pointless picker — default to the only practitioner and keep it visible but pre-filled.
- K6: a séance recorded at 23:30 clinic-local on the end date must be included; one at 00:30 the next clinic day must not.
- K8: changing the separator must not change the **column width** the number occupies on the overlay — a comma and a period are not the same glyph width in every font candidate.

## Testing

- New: `Bs1UsesRealNomenclatureTests` (K1) — assert every code the picker can supply matches the `DCH` pattern, and that a legacy mnemonic-coded document still renders.
- New: `BulletinMandatoryFieldsTests` (K2) — one case per missing field, plus the exact-string match for all régime and lien values (this is the test that would have caught a silent no-op switch).
- New: renderer tests for K7 (over-length IDU logs and refuses rather than truncating) and K8 (comma separator).
- Extend the document-error path test for K9's canonical `{ error }` body.
- ⚠️ Per `smart-app-control-blocks-tests`: `dotnet test` fails at assembly load with `0x800711C7` on this machine (SAC ON — environmental, not a defect). Write the tests; verify with SAC off or elsewhere.
- **The load-bearing verification is manual and cannot be automated:** fill a bulletin end to end, print it onto the real pre-printed BS1 form, and check by eye that every stamped field lands in its box — codes, IDU comb, régime tick, lien tick, code PS, honoraires. Record the result in `progress.md`. No test in this repo can assert paper.
- Frontend gate: `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at 320/390/820/1180/1440 px.
