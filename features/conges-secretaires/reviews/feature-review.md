# Feature Review: congés des secrétaires — functionality & ease of use

**Status:** COMPLETE
**Challenged:** Yes
**Challenged Date:** 2026-11-09
**Date:** 2026-11-09
**Parent Branch:** main
**Merge Base:** 7000b0e0
**Scope:** the whole congés feature in the worktree — commit `256ff8ce` (requests, decisions, notifications) plus the
uncommitted « Droit à congé » rework (`droit-conges-blueprint.md`: rule, opening balance, adjustments, relevé).
Pointage excluded (reviewed in `../pointage-obligatoire/reviews/`).
**Review method:** the owner asked « review the congé feature on functionality, ease of use, how intuitive it is —
users must know how it works instantly ». The four default code mandates were replaced by four read-only agents
re-aimed at that question: **Doctor journey · Secretary journey · Functionality (numbers & rules) · Device & UX**.
Duplicates merged; the four Criticals re-checked in `StaffLeaveLedger.cs` by the orchestrator. The earlier code
review was renamed `feature-review-1-code.md`, not overwritten.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 41 |
| Confirmed | 14 |
| Confirmed (adjusted) | 26 |
| Dismissed (false positive) | 0 |
| Dismissed (out of scope) | 1 |
| **Final findings** | 40 |

Challenged by two read-only agents against the source, the spec, the blueprint and `progress.md`'s recorded decisions;
every ledger claim (Findings 1–6 of the original numbering) was worked by hand against `StaffLeaveLedger.cs`.
Dismissed: original Finding 39 (the dashboard block's loading gap — pointage's own surface). Several findings describe
a behaviour the blueprint or `progress.md` chose on purpose; their notes say so, and `/apply-review-fixes` must not
silently revert those choices. Findings are renumbered by severity below.

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Functionality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 114
- **Anchor:** `StaffLeaveLedger.Fold` (walk starts at 0 in the first year with any event)
- **Comment:** **Congés taken before the first rule become a debt.** 8 j accepted in 2026, no rule; the doctor then
  sets « 20 j par an » with the default start 1 Jan 2027 → on 1 Jan the card reads « 12 jours restants » and the
  relevé « Dépassement reporté de 2026 −8 j », while 2026 itself shows no balance. With « par mois » (default 1 Dec)
  the card shows « −6 jours restants » in red. Fix: uses dated before the first account day (first rule, opening or
  adjustment) are history, not debits — the way an opening already absorbs earlier lines.

### Finding 2
- **Severity:** Critical
- **Category:** Functionality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 138
- **Anchor:** `StaffLeaveLedger.Fold` › `ruleAtEnd`
- **Comment:** **Days never expire when the rule starts after 31/12 — the owner's own example.** Rule « 20 j par an,
  Perdus » from 1 Jan 2027 + « Solde de départ 10 j » on 6 Nov 2026 + 4 j taken: no rule is in force on 31/12/2026,
  so nothing expires; in January she has **26** j, with one « Report de 2026 +6 j » line, although « Perdus » was
  ticked and the 2026 card says « non pris perdus au 31/12 ». Fix: when no rule is in force at 31/12, the next rule's
  carry choice decides. Add the 2027 assertion to `An_Opening_Absorbs_What_Came_Before_It…`.
- **Challenge note:** progress.md chose « no rule in force on 31/12 → nothing expires », but its premise fails: `Rule` falls back to the future rule, so the 2026 card itself already promises « non pris perdus au 31/12 ».

### Finding 3
- **Severity:** Major
- **Category:** Functionality
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 290
- **Anchor:** `StaffLeaveAccount.BalanceAfter`
- **Comment:** **« Il restera » / « Solde après » and every other screen show different figures under a monthly
  rule.** `BalanceAfter` reads on the congé's first day (credits up to then counted); the card, relevé and decision
  e-mail read today. Example: 2 j/mois from 1 Jan, today 10 Mar, request 6–10 Jul (5 j): the form and the admin card
  say 9, the card says 6; once accepted the e-mail, relevé and card say 1. She thinks days vanished. Fix: compute it
  on the same day as the card (`Remaining − share`) and, if useful, show the credits still to come on their own line
  (« +8 j d'ici le 6 juillet »). Add a monthly-rule case to `The_Balance_After_Equals_The_Remaining_Once_Accepted`.
- **Challenge note:** Critical → Major — progress.md « Decisions (reversible) » chose « read on the first day » and a test pins it; each figure is true on its own day, so the defect is the missing date on the figure, not a wrong number. Fix either way: state the day, or align on today.

### Finding 4
- **Severity:** Major
- **Category:** Functionality
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 290
- **Anchor:** `StaffLeaveAccount.BalanceAfter` (`on = start > AsOf ? start : AsOf`)
- **Comment:** **A congé starting last year is checked against THIS year's balance.** On 5 Jan 2027 an absence for
  28–31 Dec 2026 (4 j) shows « Il restera 16 jours » (2027's 20 − 4); in truth 2026 ends with 1 j (lost) and 2027
  stays at 20. A December request still waiting in January shows the same wrong figure. Fix: when
  `start.Year < AsOf.Year`, read the start year's account on 31/12, before its expiry line.
- **Challenge note:** Critical → Major — real (the method's own doc says « start's year »), but display only and a narrow window (a past-dated congé decided in January).

### Finding 5
- **Severity:** Major
- **Category:** Functionality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 97
- **Anchor:** `StaffLeaveLedger.Fold` (credit generation)
- **Comment:** Switching « 20 j par an » to « 2 j par mois » in November (default start 1 Dec) credits 2026 twice:
  « Droit 2026 +20 » and « Acquis — décembre +2 » → 22 on a 20-day year. Fix: a monthly rule starting in a year
  already credited by a yearly rule earns nothing until the next 1 January (or default its start to 1 Jan and say so
  in the live result, F7).

### Finding 6
- **Severity:** Major
- **Category:** Functionality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 124
- **Anchor:** `StaffLeaveLedger.Fold` (opening) / `StaffLeaveAccount.Year`; `GetStaffFileQuery.cs:81`
- **Comment:** An opening wipes earlier congés from « Pris » and the relevé while other boxes still count them: March
  congé 10 j, opening 10 j on 6 Nov → card « Pris 0 j », the box beside it « Congé payé 10 j », the footer « Total pris
  depuis le début : 10 j », and no trace of March in the relevé (the blueprint said « listed as history »). Fix: count
  every accepted day of the year in « Pris » (« dont 10 j inclus dans le solde de départ ») and show absorbed lines
  greyed, without moving the balance.

### Finding 7
- **Severity:** Major
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-account-card.tsx
- **Line:** 177
- **Anchor:** `RuleDialog` › `setSince(nextStart(...))`
- **Comment:** **A first rule set in November gives her nothing this year.** « 24 j par an » defaults to 1 Jan 2027,
  « par mois » to 1 Dec: the card keeps showing « N jours pris » instead of a balance, the team strip « 0 jour pris »,
  request cards « Non suivi », and nothing says she also needs « Solde de départ ». Fix: default a FIRST rule to
  1 January of this year, and show the result live in the dialog (« Acquis aujourd'hui : 24 j · prochain +24 j le
  1er janvier 2027 »), the way the request form shows « Il restera ».
- **Challenge note:** The default is a code comment, not a spec decision, and it is what leads to Finding 3's debt.

### Finding 8
- **Severity:** Major
- **Category:** UX — Secretary / Device & UX
- **Verdict:** Confirmed
- **File:** web/app/conges/page.tsx
- **Line:** 74
- **Anchor:** `CongesPage` › `body()`, `showAsk` (111)
- **Comment:** **Her first request is the hardest to make, and her status is two screens down.** The full-year relevé
  (~11 lines, ~670 px on a phone) sits between the card and her requests, so a bell tap on « Congé refusé » lands far
  from the answer; and with no request yet, the header and fixed phone « Demander un congé » do not render — the only
  one is in the empty state, under the whole relevé. Fix: « En cours et à venir » (or the empty state) right under the
  card, the relevé below behind « Voir le relevé » (folds, never removed), and the ask buttons whenever she may ask.

### Finding 9
- **Severity:** Major
- **Category:** UX — Doctor / Secretary
- **Verdict:** Confirmed
- **File:** web/app/users/[userId]/page.tsx
- **Line:** 137
- **Anchor:** `leaveBody` › « Relevé {year} »; `useMyStaffLeave` (`web/lib/hooks/use-staff-leave.ts:77`)
- **Comment:** **Expired days are never visible — the feature the owner asked for.** The relevé shows only the current
  year with no year control, and the expiry line belongs to the year before, so « Perdus au 31/12 » always reads 0
  and on 2 January six days vanish without a trace on either side; nothing warns in December either. Fix: a year
  stepper on the relevé, a first line « 6 j non pris en 2026 : perdus » in the new year, and « À prendre avant le
  31/12 : N j » while the rule says « Perdus ».

### Finding 10
- **Severity:** Major
- **Category:** Functionality
- **Verdict:** Confirmed (adjusted)
- **File:** api/ClinicManagement.Domain/Entities/StaffLeaveRequest.cs
- **Line:** 150
- **Anchor:** `StaffLeaveRequest.CanBeCancelledOn`; `CancelStaffLeaveRequestCommand.cs:85`
- **Comment:** An absence the admin entered for a past date can never be corrected: the type defaults to « Congé
  payé », and if she meant « Maladie » the congé has « started », so cancelling is refused for good and the days stay
  off the balance. Fix: an admin may cancel, with a motif, any accepted congé or absence whatever its date; « already
  started » stays the secretary's rule.
- **Challenge note:** The balance can be repaired with an adjustment, but the record stays wrong for good (type, history row, « Absences acceptées »); AC-10's rule was written for the secretary.

### Finding 11
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-account-card.tsx
- **Line:** 60
- **Anchor:** `StaffLeaveAccountCard` › « Règle »; `staff-leave-balance-card.tsx:89`
- **Comment:** **A saved rule that starts later is invisible.** After « 2 j par mois à partir du 1er décembre » the
  line still reads « 24 j par an » (looks like the save failed); a future-only rule reads « depuis le 1er janvier
  2027 » (wrong tense); « Modifier » re-seeds the date to next year; the secretary's card says « prochain : +2 j le
  1er décembre » under « 24 j par an » with no mention of the change. Fix: show both — « 24 j par an → 2 j par mois à
  partir du 1er décembre » — and « à partir du » for a future date, on both cards.
- **Challenge note:** Major → Minor — the new rule IS listed just below in the account card's lines (« Règle : … à partir du 1er décembre »), so not invisible; the « Règle » line, the « depuis » tense and the re-seed are real.

### Finding 12
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 196
- **Anchor:** `StaffLeaveDecisionCards` › « Annuler »
- **Comment:** A waiting card offers « Annuler · Refuser · Accepter »; « Annuler » looks like « close » but withdraws
  the request with no motif and tells the secretary « Votre congé … a été annulé par Dr X » — a refusal without the
  required motif (AC-8). Fix: remove « Annuler » from waiting cards (« Refuser » covers it), keep it in the ⋯ of
  accepted congés, and ask a motif when an admin cancels someone else's congé.
- **Challenge note:** Major → Minor — the spec's Edge Cases keep pending requests cancellable by the admin, and a confirm names the congé first; the missing motif and the « annulé par » wording are the real gap.

### Finding 13
- **Severity:** Minor
- **Category:** UX — Doctor / Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/lib/staff-leave.ts
- **Line:** 49
- **Anchor:** `staffDaysLabel`; `balanceAfterValue` (`staff-leave-request-list.tsx:133`); `FrenchDayPeriod.Days`
- **Comment:** **Going over the balance is invisible.** A request beyond the balance is allowed, so « Solde après » is
  the only warning — and it prints « -3 jour » in plain ink (singular for any value ≤ 1, a hyphen), on the card, in
  the form (« Il restera -3 jour ») and in the e-mail. Fix: below zero, « 3 j de dépassement » in the destructive tone
  everywhere; plural from `Math.Abs(days) > 1`, « − » for the sign.
- **Challenge note:** Major → Minor — the blueprint decided a request beyond the balance is allowed and « shows it negative », and the balance card does turn red; the « -3 jour » wording and the plain ink on the form/card are the gap.

### Finding 14
- **Severity:** Minor
- **Category:** Functionality / UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Domain/Services/StaffLeaveLedger.cs
- **Line:** 288
- **Anchor:** `StaffLeaveAccount.BalanceAfter`; `PreviewStaffLeaveQuery.cs:86`; `ListStaffLeaveRequestsQuery.cs:121`
- **Comment:** « Il restera » and « Solde après » ignore her other WAITING requests: Reste 10, a 5-day request waiting,
  she asks 5 more → the form says 5; the admin sees two cards saying 5 and 5; if both are accepted she has 0. Fix:
  subtract her other waiting congés payés of that year, or show « 5 j · 0 j si la demande en attente est acceptée ».
- **Challenge note:** Major → Minor — after one acceptance the `team` broadcast recomputes the other card, so the admin never decides on the stale figure; only the secretary's form overstates.

### Finding 15
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-balance-card.tsx
- **Line:** 38
- **Anchor:** `StaffLeaveBalanceCard` › stats row
- **Comment:** The card's figures do not add up to « Reste »: « En attente » sits in the row with Acquis · Pris ·
  Prévus but is not subtracted (24 − 0 − 4,5 − 1 = 18,5 while the card says 19,5); `upcoming` is never read, so with
  a monthly rule she cannot see what she will have by summer. Fix: one group Acquis − Pris − Prévus = Reste, « En
  attente » set apart beside the bar's orange segment, and « À venir d'ici le 31/12 » when above 0.
- **Challenge note:** Major → Minor — excluding « En attente » from « Reste » is the blueprint's design and pending is already set apart in amber with its own bar segment; `upcoming` never being shown is the real gap.

### Finding 16
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/lib/staff-leave.ts
- **Line:** 187
- **Anchor:** `staffLeaveDaysText`; `StaffLeaveNotices.Approved` (`StaffLeaveNotices.cs:25`)
- **Comment:** « 2,5 jours décomptés (3 jours demandés) » and « … est accepté (2,5 jours) » read as « only 2,5 of my
  3 days were granted », while every date is granted and only 2,5 come off her balance (jour férié). Fix: « 16 → 18
  novembre accordé · 2,5 j retirés de votre solde »; no « décomptés » figure for Maladie / Sans solde.
- **Challenge note:** Major → Minor — accurate text and AC-7's own term; wording only.

### Finding 17
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-bits.tsx
- **Line:** 69
- **Anchor:** `staffLeaveDecisionLine`
- **Comment:** A congé the admin withdrew looks exactly like one she cancelled herself — a grey « Annulé » with no name;
  the only explanation was a bell row that scrolls away. Fix: carry who cancelled and show « Annulé par Dr Salma Ben
  Youssef » / « Annulé par vous ».
- **Challenge note:** Major → Minor — an information gap; the bell row does name the admin.

### Finding 18
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/team-modules-card.tsx
- **Line:** 78
- **Anchor:** `TeamModulesCard` › « Demandes de congé des secrétaires »
- **Comment:** Switching congés on gives a toast and nothing else — nothing leads her to set each secretary's droit,
  and there is no « Me guider ». Fix: under the switch, one line per secretary (« Ines Gharbi — 24 j par an » /
  « Amira — Droit à définir [Définir] ») linking to her file, plus a « Congés » tour (`web/lib/guides/`, like
  `pointage.ts`): switch → file → droit → À décider.
- **Challenge note:** Major → Minor — not in the spec, and each file already shows « Définir la règle » / « Aucun droit à congé défini »; a guide is a new build.

### Finding 19
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 44
- **Anchor:** `TeamMemberCard`; « Non suivi » (`staff-leave-request-list.tsx:134`)
- **Comment:** The team strip cannot tell « no droit set » from « fine » (« 0 jour pris · Aucun congé prévu »);
  over-balance reads « −2 / 24 j » in normal colour with the bar stuck at 100 %; waiting days are not shown. Fix: a
  « Droit à définir » chip, a red remaining when negative, « 1 j en attente »; on request cards « Droit non défini »
  linking to her file instead of « Non suivi ».
- **Challenge note:** Major → Minor — « no droit » already reads differently (« 0 jour pris » vs « 18 / 24 j »); negative in plain colour, the bar clamped at 100 % and no pending days are real.

### Finding 20
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 90
- **Anchor:** `StaffLeaveAdminPanel` (no « Ajouter une absence »)
- **Comment:** « Ajouter une absence » exists only in one secretary's file header; when a secretary calls in sick the
  doctor opens Utilisateurs → Congés and finds no button. Fix: the button in the Congés tab with a « Secrétaire »
  picker; the file keeps its prefilled one.
- **Challenge note:** Major → Minor — each team-strip card links to the file, two clicks away.

### Finding 21
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/dashboard/dashboard-page.tsx
- **Line:** 202
- **Anchor:** `teamTodayApplies`
- **Comment:** The dashboard's « N demandes de congé en attente » appears only when POINTAGE is on; a cabinet with
  congés alone sees nothing on the dashboard (only the bell and a badge on « Utilisateurs » at the bottom of the
  rail). Fix: show the block, or at least the pending link, when `leaveRequestsEnabled`.
- **Challenge note:** Major → Minor — the gate comes from the pointage commit on this same branch (not main), and a congés-only cabinet gets no dashboard cue; the bell and the tab badge remain.

### Finding 22
- **Severity:** Minor
- **Category:** Functionality
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/RecordStaffAbsenceCommand.cs
- **Line:** 84
- **Anchor:** `RecordStaffAbsenceCommandHandler.Handle`
- **Comment:** « Ajouter une absence » saves the raw count, so a jour férié cannot be taken out (AC-7's « retiré dans
  Décompter » exists on « Accepter » only): 30/12 → 02/01 counts 1 January → 4 j instead of 3, with no way to edit
  it. Fix: « Jours à décompter » on the absence form, prefilled from the preview, passed to `RecordByAdmin`.
- **Challenge note:** Major → Minor — AC-11 gives the absence the request's fields and AC-7 puts « Décompter » on Accepter only; workarounds exist (split the range, or « Ajouter ou retirer des jours »).

### Finding 23
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 152
- **Anchor:** `AcceptLeaveDialog.step` / `useLeaveDecision` (69)
- **Comment:** Changes made with buttons are not seen as edits: « − » on « Jours à décompter » (3 → 2 for a jour
  férié), « Perdus / Reportés », the request's Type and « Demi-journée » change React state only, so `useDirtyGuard`
  never marks the dialog dirty and an outside tap, Escape or the back gesture closes it silently; reopening shows 3
  again. Fix: pass the hook's `alsoDirty` argument (as `feedback-dialog.tsx` does) in each of these dialogs.
- **Challenge note:** Major → Minor — only button choices are lost (and show again on reopen); every typed field is guarded.

### Finding 24
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-account-card.tsx
- **Line:** 351
- **Anchor:** `OpeningDialog`
- **Comment:** « Solde de départ » depends on a description sentence (against the no-helper-text rule) and stays
  unclear about congés already accepted for later (the fold subtracts them again, 10 → 6). Fix: « Fixer le solde »,
  the form as a sentence (« Il lui reste [ ] j au [date] »), and the result live (« Après les congés déjà acceptés :
  6 j »); drop the description.

### Finding 25
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted)
- **File:** web/app/users/[userId]/page.tsx
- **Line:** 97
- **Anchor:** `body` › `leaveEnabled && leaveBody()`
- **Comment:** With pointage on, a doctor's or admin's file shows « Congé payé », « Droit à congé — Définir la règle »
  and « Historique », though they cannot ask for congés. Fix: the congé section only when `file.role === "secretary"`.
- **Challenge note:** The proposed fix (secretary only) breaks the spec Edge Case « role changed away from secretary: her history stays in her file » — show the section for a secretary or for anyone with congé history or entries.

### Finding 26
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted)
- **File:** web/components/staff-leave/staff-leave-request-dialog.tsx
- **Line:** 77
- **Anchor:** preview effect; `PreviewStaffLeaveQuery.cs:75`, `StaffLeaveRules.cs:35`
- **Comment:** Overlapping dates are refused only after the motif is typed and sent, under a « Il restera » that
  looked fine, and the sentence does not say whether the other one is waiting or accepted. Fix: the preview returns
  the overlap and shows it under Du/Au at once, phrased like the admin's (« une demande en attente » / « un congé
  accepté »).
- **Challenge note:** The sentence « Vous avez déjà une demande du … » is AC-4's own example; the gap is that the preview never checks overlap, so the refusal only comes on send.

### Finding 27
- **Severity:** Minor
- **Category:** Functionality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/StaffLeaveRules.cs
- **Line:** 142
- **Anchor:** `StaffLeaveRules.ValidateNewAsync`
- **Comment:** A request on a closed day counts 0 and is sent (« … demande un congé le 15/11 (0 jour) »). Fix: refuse
  when the count is 0 — « Le cabinet est fermé ces jours-là. » — on the field and on the server.

### Finding 28
- **Severity:** Minor
- **Category:** UX — Secretary / Device & UX
- **Verdict:** Confirmed (adjusted)
- **File:** web/components/staff-leave/staff-leave-request-dialog.tsx
- **Line:** 215
- **Anchor:** day-count card
- **Comment:** The count (« 3 jours · Il restera 21 jours ») renders after the motif — below the fold on a phone sheet
  and hidden by the keyboard while typing; it vanishes silently for Maladie / Sans solde / no droit and on a failed
  preview read. Fix: directly under Du/Au; the same slot says « Ne compte pas sur votre solde », « Solde non suivi »
  or « Décompte indisponible ».
- **Challenge note:** Not true that it vanishes for Maladie / Sans solde — the count stays and only « Il restera » drops, per AC-3; placement after the motif and the silent failed read are real.

### Finding 29
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 370
- **Anchor:** `CancelLeaveConfirm` and wording across the screens
- **Comment:** Wording drifts: « Ce congé était accepté » before anything happened; a waiting one is « Annuler le
  congé »/« Congé annulé » (should be « Retirer la demande »/« Demande retirée »); three date styles (« 16 → 18
  novembre », « du 16/11 au 18/11 », « du 16 novembre au 18 novembre 2026 »); « Refusé » pill vs « est refusée »
  bell; « Prochain congé · … · en cours »; « Congé prévu · … · à venir » says it twice. Fix: one vocabulary and one
  date style across screen, bell and e-mail.

### Finding 30
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 61
- **Anchor:** `TeamMemberCard`
- **Comment:** At 320 px, with no rule and both taken and planned days, the `shrink-0` figure (~170 px) leaves the name
  ~1 character wide — « Ines Gharbi » renders as a vertical stack of letters. Fix: let the row wrap
  (`flex-wrap`, figure `basis-full sm:basis-auto`).

### Finding 31
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted)
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 328
- **Anchor:** `StaffLeaveHistory` › name Link
- **Comment:** The row's only path to the secretary's file is a ~28 px link on a coarse pointer (the only path at all
  for a deactivated secretary). Fix: `coarse:min-h-11` on the Link.
- **Challenge note:** Not the only path for a deactivated secretary — « Comptes » links every secretary's file; the ~28 px target on a coarse pointer is real.

### Finding 32
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-account-card.tsx
- **Line:** 177
- **Anchor:** `RuleDialog` seed effect
- **Comment:** Seeding « À partir du » after open fires a real `change`, so an untouched rule dialog asks « Abandonner
  les modifications ? » on « Retour », ✕ or Escape. Fix: seed in the same render as `open` (or `guard.markClean()`
  after seeding).
- **Challenge note:** Only when the frequency stays « par an » (a monthly rule remounts the Select via `key`).

### Finding 33
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-dialog.tsx
- **Line:** 161
- **Anchor:** Type `ModeSegmented`
- **Comment:** From 768 px the dialog is ~400 px but `sm:flex` follows the viewport: four types at ~70 px each, and
  « Congé payé » wraps to two lines while the others do not. Fix: `grid grid-cols-2` at every width in this dialog,
  or a container hinge.

### Finding 34
- **Severity:** Suggestion
- **Category:** UX — Doctor / Functionality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 180
- **Anchor:** `StaffLeaveDecisionCards` › « Solde après »; `StaffLeaveAccounts.cs:81`
- **Comment:** A congé across 31/12 shows only the first year's share (« Jours 10 · Solde après 12 » from 16 — the
  2027 share is nowhere), and in « Accepter » the figure vanishes once the count is edited. Fix: both years (« 2026 :
  12 j · 2027 : 14 j »), recomputed server-side for the edited count.
- **Challenge note:** Minor → Suggestion — chosen on purpose (progress.md QA F-1; the dialog hides the figure once a cross-year count is edited); congés across 31/12 only.

### Finding 35
- **Severity:** Suggestion
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/staff-leave/staff-leave-account-card.tsx
- **Line:** 95
- **Anchor:** `StaffLeaveAccountCard` › lines list
- **Comment:** Each opening/adjustment appears twice on the page, under two names and with two ✕ (the account card's
  list and the relevé). Fix: the card lists rules only (« Historique des règles »); openings and adjustments are
  cancelled from the relevé.
- **Challenge note:** Minor → Suggestion — the card list is the only place to cancel rules and earlier years' lines (the relevé has no year picker), so its proposed fix would remove a capability; do it only together with the year stepper.

### Finding 36
- **Severity:** Suggestion
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Queries/ListStaffLeaveRequestsQuery.cs
- **Line:** 126
- **Anchor:** `othersOff`
- **Comment:** « Déjà en congé sur ces dates » counts accepted congés only; two secretaries asking for the same week
  each get a card with no warning. Fix: a second line « Demande aussi ces dates : Amira (en attente) ».
- **Challenge note:** Minor → Suggestion — AC-6 asks for exactly « already on an accepted congé »; showing pending ones too is an addition.

### Finding 37
- **Severity:** Suggestion
- **Category:** Functionality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.API/Controllers/TeamController.cs
- **Line:** 356
- **Anchor:** `TeamController.StatusFor`
- **Comment:** Congé refusals carry no code, so module-off is 400 (not 403 `team_module_off`) and « already answered »
  / overlap / « already started » are 400, not 409 — a second admin's decision landing between the dialog's read and
  its POST shows the sentence with no « Recharger » (AC-14). Fix: codes on the `StaffLeaveRules` refusals, mapped
  403 / 409.
- **Challenge note:** Minor → Suggestion — DEV-1 in progress.md chose the 400s, and the dialog re-reads just before posting and offers « Recharger » itself; only a decision landing in that millisecond window misses it. Side note: pointage's module-off is 403 in the same controller.

### Finding 38
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 348
- **Anchor:** `StaffLeaveHistory` › Décision cell
- **Comment:** On an iPad landscape (table from `lg:`, finger) `TableCell clamp` cuts the refusal motif to two lines,
  the rest only in a `title` that needs a hover. Fix: no `clamp` here, or the motif as its own block line.
- **Challenge note:** Minor → Suggestion — `clamp` + automatic `title` is the free-text pattern frontend-web.md § 6 prescribes; the full motif is in the card form below `lg:` and in the secretary's own view.

### Finding 39
- **Severity:** Suggestion
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-dialog.tsx
- **Line:** 192
- **Anchor:** « Demi-journée »
- **Comment:** A demi-journée has no morning/afternoon choice, so the cabinet does not know when she is away (she will
  write it in the motif). Fix: « Journée · Matin · Après-midi » when Du = Au.
- **Challenge note:** A new capability on top of AC-2 (`HalfDay` is a yes/no column).

### Finding 40
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted)
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 71
- **Anchor:** team strip bar; `StaffLeaveStatusPill` (`staff-leave-bits.tsx:284`)
- **Comment:** A hand-rolled `role="img"` bar beside `ui/progress.tsx`, and a hand-built status pill beside `Badge` +
  `STATUS_TONE_CLASS`: the next device fix to either primitive misses these copies. Fix: `<Progress>` for the strip,
  the pill on `Badge`.
- **Challenge note:** The pill already uses `STATUS_TONE_CLASS`; only Badge's shape is copied. The pill is at `staff-leave-bits.tsx:19`, not 284.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 2 |
| Major | 8 |
| Minor | 23 |
| Suggestion | 7 |
| **Total** | 40 |
