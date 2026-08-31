# Feature Specification: Calendar Import — « Did you mean this patient? »

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-31
**Scope:** Full
**Feature:** A calendar-imported patient whose name is a writing variant of an existing one carries a suggestion the doctor answers; accepting reassigns the appointment and deletes the placeholder.

## Overview

`calendar-import-review` creates a patient when no existing one matches **exactly** — deliberately, because the substring match it replaced could book an event onto the wrong person's file. The cost is that « Chaïma Ben Khalifa » in Google Agenda produces a second fiche for a patient the cabinet already holds as « Chaïma Benkhalifa », and a duplicate is the one mistake this product currently cannot undo.

The import now also looks for a **writing variant** of an existing name and, when it finds exactly one, stamps it as a suggestion. « Patients à compléter » asks the question; accepting reassigns the appointment and deletes the placeholder, refusing clears the suggestion for good.

**Two names are compared as two strings — a given name and a surname — in either order, never as one blob and never on a single token.** And the match is an *equivalence of spellings*, not a distance: there is no edit budget anywhere in this feature, so it cannot drift into guessing.

**This is the product's first patient merge** — `ArchivePatientCommand` and `CreatePatientCommand` both state there is none. It is deliberately not a general one: it merges only a placeholder this import created, only while nothing real is attached.

## What Changes

- The import canonicalises the given name and the surname separately and compares them against the clinic's patients it already loads, in both orders. Exactly one match → stamped as a suggestion. Zero or more than one → nothing, mirroring the exact path's refusal to guess.
- The near-match test **never links anything by itself.** `MatchesName` stays byte-exact; an equivalence only ever produces a question for a human.
- A phone number found in the event description **corroborates or vetoes** the suggestion, and is stored on the created patient.
- « Patients à compléter » renders the question with the existing patient's birth date and phone, and two answers.
- Accepting reassigns every appointment of the placeholder to the suggested patient, clears the import notification, deletes the placeholder and journals the merge — one transaction.
- Accepting is refused when anything other than appointments and the import notification is attached, naming the blocker the way deletion already does.
- Refusing clears the suggestion permanently and keeps the review stamp, so the row stays with « Compléter les infos patient » alone.

## Acceptance Criteria

- **AC-1:** An event is imported **only when its title carries a given name AND a surname** — never one token alone. This is already the shipped behaviour (`LooksLikeAPersonName`'s two-word minimum, the check that retired the branch storing « Karim » as both halves); this feature relies on it rather than adding it. The title splits into the first token and the rest, so « Mohamed Ben Salah » is `Mohamed` + `Ben Salah`.
- **AC-2:** Two halves match when their **canonical forms are equal**. Canonicalisation, in order: fold accents · drop non-letters (so `Benkhalifa` = `Ben Khalifa`) · protect `ch` · `x`→`ks` · `kh`→`k` · `gh`→`g` · `ph`→`f` · `ou`/`w`→`u` · `y`→`i` · `c`→`s` · `k`→`q` · collapse repeated letters · drop `h` · drop a trailing `e`. **There is no edit budget** — an unequal canonical form is not a match, however close.
- **AC-3:** Both halves must match, and the **reversed reading is tried too**: « Zouari Fatma » finds « Fatma Zouari ». Writing the surname first is a real and common way to enter a name, so it is a first-class case, not a fallback.
- **AC-4:** A repeated letter is a writing variant wherever it sits — « Anis Kacem » / « Aniss Kacem », « Salma » / « Salmaa », « Mohamed » / « Mohammed », « Chaabane » / « Chabane » all match. Owner's decision, recorded here because the alternative (final doubles refused) was considered and rejected.
- **AC-5:** `ch` is protected before `c`→`s` runs. Without it « Chaima » canonicalises into « Samia » — a different patient. Pinned by a test.
- **AC-6:** **Measured, not asserted.** The unit tests carry three corpora so a change to the table is re-measured rather than re-argued: 17 real writing variants that must all match, 16 pairs that must all be refused (« Imen »/« Iman », « Olfa »/« Alfa », « Hamza »/« Hamdi », « Mohamed »/« Mohsen », « Ali »/« Sami Ben Salah », « Slim »/« Selim », …), and 46 distinct names cross-multiplied. Current result: **17/17 matched, 16/16 refused, and exactly one intended pair** across the corpus. For contrast, the per-half edit budget this replaced scored 16/16 matched but claimed **16** different people were the same, and a whole-name budget scored 13/14 and 14.
- **AC-7:** When the event description contains a phone number, it decides: equal to the candidate's phone → the suggestion stands and the row says the phone matches. **Different from it → no suggestion at all**, whatever the names do. A name equivalence never survives a contradicting phone.
- **AC-8:** A phone found in the description is stored on the created patient. This narrows `calendar-import-review`'s AC-7 ("no contact details either") — a description that carries one is real data, and it is what the reviewer would otherwise retype.
- **AC-9:** A patient with a live suggestion carries a « Doublon possible » chip **beside their name**, opening a dialog that shows the two fiches **side by side** — name, birth date, phone, each absent field stated as « non renseigné » — with « Oui, fusionner les fiches » and « Non, deux patients différents ». Without a suggestion the row is unchanged. ⚠️ The question is **not** asked in the row itself: a first attempt put it there as a sentence over two answers, which made the row twice as tall as its neighbours, left three controls competing in one cell, and asked the reader to tell « Imen » from « Iman » out of one line of small print. **The dialog is the confirmation** — no second « êtes-vous sûr ? » on top of it.
- **AC-10:** Accepting moves the appointment rows themselves — same ids, `GoogleCalendarEventId` preserved — so the next sync updates the event instead of re-importing it. Verified by running the import twice across an accept.
- **AC-11:** Accepting is refused with the blocker named when a fiche, invoice, plan, file, tooth state or any other linked row exists on the placeholder. Nothing is deleted, nothing is reassigned.
- **AC-12:** When the surviving patient already holds an overlapping appointment, the merge succeeds and the response says so — the exclusion constraint is keyed on `DoctorId` and an imported appointment has none, so the database does not stop it.
- **AC-13:** Refusing nulls the suggestion and leaves `CalendarImportPendingReviewSince` set; re-running the import does not bring it back. A second accept answers « Patient introuvable. » rather than throwing.
- **AC-14:** Saving the placeholder's fiche clears the suggestion along with the review stamp — one call site in `UpdatePersonalInfo`, so no caller can clear one and forget the other.
- **AC-15:** At 320 px the comparison is a bottom sheet with the two fiches stacked (the arrow between them rotates to point down) and both answers full-width, 44 px on a coarse pointer. The table above `lg:` gains **no column** — the chip sits with the identity and « À faire » keeps exactly one control, so every row is the same height.

## API Contract

### POST /api/patients/{id}/merge-into-suggested-duplicate
Request: `{}`
Response 200: `{ survivingPatientId: guid, appointmentsMoved: int, overlapsExisting: bool }`
Errors: `400 { error }` — no suggestion stamped, or linked data blocks it (named) · `404 { error }` — unknown, other clinic, or already merged

### POST /api/patients/{id}/reject-suggested-duplicate
Request: `{}`
Response 204
Errors: `404 { error }` — unknown or other clinic

## Data / Schema Changes

- `Patient.CalendarImportSuggestedDuplicateId` — `Guid?`, null default. Set only by the import; cleared by rejection, by `UpdatePersonalInfo`, and by the merge deleting the row. **No foreign key**: the suggested patient can be deleted or archived independently, and a dangling id must degrade to "no suggestion", not to a load failure.
- `PatientDto.suggestedDuplicate` — `{ id, fullName, dateOfBirth, phone, phoneMatches } | null`, resolved on read, null when the id no longer resolves.

## Device Behaviour

- **Leading device:** tablet portrait (820 px) — `/a-cloturer` is a reception screen.
- **Narrow width (< 640):** the chip goes in `CardList`'s `status` slot — a mark read with the identity — and the comparison opens as a bottom sheet with the two fiches stacked.
- **Touch:** the chip grows its own box (`coarse:min-h-11`) rather than using `.touch-target`, since it sits inline with text and an overlay would overhang the line above. Both answers are real buttons at 44 px on a coarse pointer.

## Out of Scope

- Merging any two patients the user picks. This merges only an untouched placeholder this import created, into the one candidate it stamped.
- The pre-existing hole where **two exact** name matches make the sync skip the event entirely, importing nothing and logging only. Real, and a separate fix.
- Re-suggesting later, or offering several candidates. Decided at import; more than one match stamps nothing.
- Undoing an accept. The placeholder is deleted; the appointment is reassignable by hand afterwards.
- Widening the canonical table to Arabic script or to given-name nicknames (« Mohamed » / « Hamma »). A nickname is not a writing variant and would need a lookup table, not a rule.

## Edge Cases (Critical only)

- **A surname is more than one token, and that is normal here.** « Ben Salah », « Ben Khalifa », « Ben Youssef » are ubiquitous, so the title is split into *two parts*, not required to be two words. Requiring exactly two tokens would refuse a large share of real Tunisian patients — see AC-1.
- **The import notification counts as attached data.** `GetLinkedDataCountsAsync` counts `Notifications` by `PatientId`, so the merge clears it *before* the delete or the delete refuses itself.
- **The suggested patient may be archived.** Matching already reads `includeArchived: true`, and suggesting an archived patient is correct — that is precisely one the cabinet would otherwise duplicate.
- **A placeholder can hold several appointments** when two events name the same misspelling. All of them move.
- **Both sides can be placeholders.** Two imported spellings of one person may suggest each other; merging is valid and the survivor keeps its own review stamp.
