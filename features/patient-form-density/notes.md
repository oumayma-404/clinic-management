# La fiche patient tient sur onze lignes, et la denture en a trois

What shipped, and the decisions that are easy to undo by accident. Companion to
`web/components/CLAUDE.md`'s row for `edit-patient-dialog.tsx`.

## The complaint, and what the measurement actually said

A trialling dentist reported getting lost in the patient modal, abandoning it half-read, and missing fields as
a result. The obvious reading is "too many fields". The measurement said something more useful: **33 controls
over 28 rows** — very nearly one field per row, in five blocks all unfolded on arrival.

That distinction decided the whole design. Removing fields was worth **5 rows**; putting fields that belong
together *on the same row* was worth **17**. The form now holds 29 controls on **11 rows**.

| | Before | After |
|---|---|---|
| Controls | 33 | 29 |
| Rows on open | 28 | **11** |
| Blocks folded on arrival | 0 of 5 | 2 of 4 |

## A row carries several fields when they answer one question

- **Prénom · Nom** — unchanged.
- **Motif de consultation · Adressé par** — row 2, directly under the names. They were the *third* control and
  the *tenth*, the second of them at the very bottom under the CNAM-adjacent fields. Both are what the desk
  actually asks (« pourquoi venez-vous ? », « qui vous envoie ? ») and both are read beside the patient's name
  on their own page.
- **Téléphone · Sexe · Naissance** — one row of three.
- **Denture** — full width, three options.
- In « Informations médicales »: **Allergies · Maladies · Médicaments** on one row. Three lists of the same
  kind, read as a set before an injection.

⚠️ **`md:items-start` plus a `min-h-8` label box on all three cells of the naissance row, and both halves are
load-bearing.** The naissance label carries a segmented control (« Date » / « Âge ») and the other two carry
plain text, so **without a shared label height its input starts ~12 px below its neighbours'** and the three
boxes visibly stop lining up — the defect the client spotted first in the mockup. `items-start` then keeps that
alignment when one column grows a message the others do not have (the phone's « ni rappel ni relance », a
validation error). The labels are short — « Téléphone », « Naissance », « Date » / « Âge » — because at this
width « Date de naissance » beside the switch wraps to a second line, which is the row the arrangement exists
to remove.

⚠️ **The derived age is printed beside the « Naissance » label, not on a help line under it**
(`ageFromBirthdate`). One fewer row, and it lands where the reader is already looking. It states what IS — the
age this date makes today — and never what the denture will be, which the control below says for itself.

## What is folded is what the practice says it never fills

⚠️ **This is the second reversal on that line and the first one is worth keeping in view.** Every section used
to be open, which was itself a reversal of every section being folded on create. The argument for opening them
still holds and is **kept**: a folded section is a question the desk never sees, and « Informations médicales »
folded on a new patient is how a smoker, an allergy and a chronic condition go unrecorded at the one moment
somebody is sitting there answering. So the two blocks carrying a clinical question — **médical** and
**notes** — stay open, and médical now comes **first**.

Folded are the two the client itself describes as rarely filled: **« Coordonnées et rappels »** (adresse,
e-mail, consentement) and **« Identité CNAM »**. Neither extreme was ever the answer; the form was simply too
long, and the fix was to shorten it.

⚠️ A folded section's **summary must be true about everything it holds**. « Coordonnées et rappels » says
« adresse et rappels non renseignés » rather than staying silent, because an unrecorded consent still **sends** —
that is the half people misread, and folding a consent question would be indefensible if the closed header were
quiet about it.

## Five fields leave, one arrives

Removed from the form: **contact d'urgence**, **téléphone d'urgence**, **gouvernorat**, **ville**,
**code postal**. Added: **« Médicaments »**.

⚠️ **The emergency pair is removed from the FORM, not from the record.** The update DTO is tri-state, so the
payload simply drops the two keys and the stored values are left alone. Sending `""` would have cleared them.
Whether those columns are eventually dropped is a separate, destructive decision nobody has taken.

⚠️ **The address fold is one-way.** Four boxes become one free-text line, and on hydration the stored parts are
**joined back into it in full** — a stored « 12 rue de Carthage / Tunis / La Marsa / 2070 » must reappear whole,
or opening the form and pressing « Enregistrer » would quietly drop three quarters of the address, which is the
exact silent-drop shape `Address.OfAny` was written to end. On save the whole line goes to `Address.Street` and
the other three are sent blank. What is lost is the **decomposition**: every display surface already joins the
parts (`formatAddress`, the summary modal, the lettre de liaison), so nothing on screen changes — but the **CSV
export** and the **dossier archive** read Ville / Gouvernorat / Code postal as separate columns, and those go
empty for any patient saved through this form from now on.

The alternative — keeping the three hidden and editing only the street — was rejected for the reason that
matters: the desk could see « La Marsa » in the field and be unable to correct it.

⚠️ **« Médicaments » had to reach `PatientAlertPanel`, not only the patient's file.** That panel is what the
fiche de soins, the document editor and the summary modal each render before work is recorded — and it is
precisely where « sous anticoagulants » was being typed by hand into `ImportantNotes` because no field existed.
Adding the column without adding it there would have left the hand-typed note as the only thing those three
surfaces see.

## One column had three names

`Patient.MedicalHistory` was labelled « Maladies chroniques / affections » on the form and on the patient file,
and « **Antécédents** » in `PatientAlertPanel` — a few centimetres from a *different* list, from a *different
table*, called « Antécédents médicaux ». It is now **« Maladies »** everywhere. Chronic or passing, it is one
list to the person writing it.

## La denture a trois états, et le troisième est un piège

`DentitionType` shipped with two values on the argument that the field being editable made a `Mixed` value
unnecessary. It did not: a seven-year-old carries both sets, and with two values a patient marked `Child` could
not be charted on a permanent molar until the record was switched to `Adult`, at which point the remaining baby
teeth became unchartable. The odontogram had already grown a third *view* (`DentitionView.mixed`); what was
missing was a way for the record to say so.

- Labels name **dentitions, not patients**: « Denture temporaire » · « Denture mixte » · « Denture définitive ».
  « Adulte / Enfant » is gone from the interface — it described the patient, and there is no third patient.
- Bands: **under 6 → temporaire · 6–12 → mixte · 13+ → définitive**, one comparison per boundary, in
  `DentitionRules.FromAgeYears` with `dentitionForAgeYears` mirroring it client-side.
- Each option carries **its own band as a caption**. That replaces the sentence « Proposé d'après l'âge. » under
  the group, which stated the mechanism rather than the threshold and was not read. « Déduit de l'âge » now sits
  as a chip beside the answer it qualifies and disappears the moment the dentist chooses.

⚠️ **`isAdultDentition` answers `true` for `Mixed`, and that is a narrowing, not a bug.** It asks one question —
"may permanent teeth be charted?" — and for a mixed mouth the answer is yes. It is emphatically not "which arch
does the chart open on": routing that through it would have opened an eight-year-old's chart on the permanent
arch and made her deciduous teeth unreachable, **with no error anywhere**. That question is `dentitionViewFor`,
which is the one reader that had to learn the third value and now maps all three one-to-one. `isAdultDentition`
has **no call sites** outside `lib/dentition.ts` and should acquire one only for the permanent-teeth question.

⚠️ **`Mixed = 2`, appended, never inserted.** The values persist through `HasConversion<int>()`, so putting the
new member where it belongs in *reading* order — between `Child` and `Adult` — would silently repoint every
stored row and turn every adult into a mixed dentition. `DentitionBandsTests.The_Stored_Numbers_Never_Move`
asserts the numbering rather than trusting it.

⚠️ **The stored keys stay `Child` / `Adult`.** English in the database, French on screen — the standing
convention for a persisted closed set. Renaming them for a caption change would break every stored row, so
`DENTITIONS`' order in `lib/dentition.ts` is the **display** order and not the enum's numbering.

## Adjacent defects fixed in the same pass

- **`smokingPerDay` was missing from `FIELD_LABELS_FR`.** It is the only other key `validateForm` sets, so when
  the tobacco quantity was the sole refusal the error banner printed « Corrigez « smokingPerDay » ci-dessous. »
- **`NullableDateOfBirthTests` asserted age 12 → `Child`.** Correct under the two-band rule, wrong under three;
  corrected rather than deleted, and 5 and 13 stay to hold both outer bands.
- **The tabac group's `<Label htmlFor="smoking-status">` pointed at a `<div>`** and therefore labelled nothing.
  It is now a `role="group"` with its own `aria-label` and a visible `<span>`.

## Le dossier patient : cinq passes sur le même bloc

The line under the patient's name spoke about **allergies alone**. « Aucune allergie signalée » was true and
incomplete: maladies, médicaments and tabac were equally unrecorded and it said nothing about any of them, so a
blank read as « rien à signaler » when it meant « on n'a rien demandé ».

Every pass after that was a correction of the one before, and each was reported from looking at the real screen:

1. **Name all four** → a run of loose lines under an identity strip already carrying five more. *« It started at
   just one detail, now growing, and looks scattered. »*
2. **Frame them in one panel** with an aligned label column → the grouping was right, the decoration was not:
   uppercase tracked labels at one size, values at another, an icon, four ink colours, and a box with a border
   **and** a fill. *« Too many fonts, colours, looking extremely unprofessional. »*
3. **Cut to one type size, one label style, one accent.**
4. **Two half-width panels**, « Santé » and « Consultation » → « Motif » got a section it did not need, the right
   half was one line of white space, the left was tinted a destructive wash, and — because the block was a
   full-width grid *inside the header's flex row* — the five action buttons wrapped off the name's line.
   *« Why did the buttons move to bottom? »* · *« Do not have it bright pink like that, offputting. »*
5. **One full-width card, in the app's own hairline grid.**

⚠️ **The last pass is the one worth keeping: use the surface the app already has.** The body is
`dashboard/kpi-grid.tsx`'s idiom — `gap-px` over a `bg-border` container, each cell painting its own `bg-card`,
so the gaps read as hairline dividers — and never `divide-x`, which draws from DOM order and mis-aligns the
moment a row wraps. Four earlier attempts each invented a label/value grammar for a page that already had two.

⚠️ **The column count follows the FACT count, and every step is a divisor of it** — 2 facts step 1 → 2, 4 facts
step 1 → 2 → 4, and 3 facts skip the two-column step. `kpi-grid`'s documented trap is that an *unfilled* cell
shows the container's `bg-border` as a grey slab; it pads with `aria-hidden` fillers, but with at most four items
the holes can simply be made unreachable. Measured: 4 columns at 1440, 2 at 820 and 1180, 1 on a phone, and a
hole ratio of ~1 % — the hairlines themselves — at every width.

⚠️ **`BODY_MAX_PX` is the growth control, and without it this block has none.** Allergies, maladies and
médicaments are free text: a patient on six drugs with three conditions pushes the odontogramme — the chart the
whole consultation is read off — below the fold, which is the exact defect the notes strip was capped for and the
reason the treatment band was moved *under* the chart. Proven rather than assumed: eight injected rows took the
body's content to 300 px and the card stopped growing and scrolled. It is **160, not the notes strip's 120**,
because at 820 px the 2 × 2 layout wraps médicaments to two lines and comes to ~133 px — a 120 px ceiling clipped
the ordinary tablet case, which reads as a broken card rather than as a deliberate bound. Bounded and
**scrolling**, never collapsible: a « voir plus » on the one block a practitioner checks before injecting is a
click between them and an allergy.

⚠️ **No wash, no tint, no coloured frame.** The single accent is the allergy *value* in destructive ink. A whole
panel tinted for a patient merely allergic to penicillin reads as an emergency, and three coloured things side by
side mean nothing alerts.

⚠️ **The card renders OUTSIDE the name/actions row, and that is a constraint rather than a preference.** Inside
the header's left column it is a full-width block, so the column demanded the whole line and the five action
buttons — pinned beside the name from `xl:` up — wrapped to a row of their own at every width.

⚠️ **Tabac moved into the card** out of the identity strip, where it had taken the retired assureur's slot:
beside the age, the sexe, the telephone and the solde dû, a risk factor is rendered exactly like a contact
detail. **Motif de consultation stayed under the name** and **« Adressé par » stayed in the strip** — pass 4
moved both into a panel and both were put back: the motif is why this person is on the books, not a health fact,
and it is one short line that cannot justify half a band.

⚠️ **The card's « Modifier » opens the form scrolled to « Informations médicales »** (`focusSection` →
`SECTION_ANCHOR`). A button beside a wrong allergy that lands the reader at the top of a long form to hunt for
the section is worse than no button. `focusSection` is typed as the anchor map's own keys rather than
`SectionKey`, so asking for a block with no id is a compile error instead of a button that silently goes nowhere;
and the scroll waits **two** animation frames, because Radix mounts the content in a portal and runs an entry
animation, so on the tick `open` flips the scroller has no height and `scrollIntoView` is a no-op.

⚠️ **A cell with no value is not rendered**, so a healthy patient's card is one line rather than four; with all
four empty it says so once, naming all four — the pass-1 fix every later pass kept.

⚠️ The component is deliberately **not** told whether the read failed. A caller that could not load the patient
must not render it at all, or an empty card becomes a claim of « aucune allergie » made on the strength of a
network error.

**Denture** now appears on the patient's file (« Informations personnelles »). It was stored, drove every chart,
and was printed nowhere.

## Les trois listes sont des listes, pas du texte à virgules

« Allergies », « Maladies » et « Médicaments » are free text and were being read as comma-separated runs. They
are now **bulleted lists on both sides**: pressing Enter in any of the three fields opens the next line with a
« • », and the patient's file renders each as a real `<ul>`.

⚠️ **The bullet convention is not new — `splitPatientWarnings` already stripped one.** The notes strip has always
split a textarea on newlines and removed a hand-typed `-`, `–`, `—`, `•` or `*`. `lib/health-list.ts` is that
same rule made to type itself, and its character class is copied verbatim: people paste lists written with any of
those five, and a reader that knew only its own bullet would print somebody else's straight back at them.

⚠️ **Newlines first, commas only as a fallback, and the fallback is what protects every existing record.** A
practice running for a year has « Hypertension, diabète de type 2 » on file — one line, two facts. Splitting on
newlines alone would render it as a single item; splitting on both always would break « Kardégic 75 mg, 1 le
matin », where the comma belongs to one medication's posology. So a value with newlines is a list the user built
and its lines are taken as written, commas included; a value with no newline at all is legacy, and its commas are
the only separator it can have.

⚠️ **The caret is restored SYNCHRONOUSLY, and the deferred version was a real defect.** The obvious shape — call
`onChange`, put the caret back in a `requestAnimationFrame` — loses a race against the user. Typing
« Metformine » straight after Enter put « Metf » at the stale caret, the frame then fired and moved it, and the
rest of the word landed elsewhere: measured as
`• Kardégic 75 mg` / `• ormine 850 mg` / `• MetfAmlodipine 5 mg`. Writing `el.value` first is what makes the
synchronous `setSelectionRange` correct, and React then renders the identical string, skips the DOM write and
leaves the selection alone. ⚠️ Found by **typing into the field**, not by reading it — no check in this repo
could have.

⚠️ **Enter on a line holding only a bullet leaves the list**, and Shift+Enter is left alone for a posology that
runs to two lines. Without the first, the field cannot be exited except by deleting the bullet by hand and every
abandoned list ends in a dangling « • ».

⚠️ **A `
` in a `placeholder` collapses to a space** unless the field carries `whitespace-pre-line`. The
placeholder's whole job here is to *show* that Enter starts a new bullet, so rendered as one run-on line it would
have taught the opposite.

⚠️ **A real `<ul>` past one item, a bare line at exactly one.** A single-item list still announces « liste, 1
élément » to a screen reader and pays a marker's indent for nothing — and « Tabac » is always one item (it is a
sentence the app composes, never a list the user typed), so the common card would otherwise carry a lone bullet
beside three real lists. `list-none` with an explicit « • » rather than `list-disc list-inside`, because the
built-in marker sits outside the text box and a wrapped second line hangs under the bullet instead of aligning
with the first character.

⚠️ **The lists made the card taller, so the cap moved 160 → 192.** A cell is now as tall as the list inside it:
a real patient (1 allergy · 2 maladies · 3 médicaments · tabac) comes to **183 px** of content at 820 px and
1180 px, so at 160 the ordinary tablet case scrolled by a couple of dozen pixels — a clipped card rather than a
deliberate bound, on the device this product is used on most. The same patient at 390 px is 301 px and still
scrolls, which is the point.

## Verification

- `dotnet test` — **4361 passed**. Run in a clean `git worktree` of HEAD carrying only this change, because a
  concurrent branch had the test project failing to compile; the 5 failures there are worktree artifacts
  (`PhoneRuleCorpusTests` and `StartTreatmentStepsTests` walk up from `__FILE__` looking for a `.git`
  **directory**, and a worktree has a `.git` *file*).
- `npm run check:responsive` — 56/56 · `npx tsc --noEmit` — clean · `npm run build` — clean.
- **Eye pass done** at 320 / 390 / 820 / 1180 / 1440, in the browser, signed in, against a real patient — and it
  is what found four defects the three mechanical checks passed clean over: the antécédents repeaters still
  costing three rows when empty, « Notes » still stacked instead of sharing a row, an address folding to
  « Ariana, Ariana », and the action buttons wrapping off the name's line. No horizontal overflow at any width.
- The modal's own measurements at 820 px: body content 1288 px in a 722 px scroller. The identity row's three
  inputs align exactly, which is the `min-h-8` label box doing its job.
