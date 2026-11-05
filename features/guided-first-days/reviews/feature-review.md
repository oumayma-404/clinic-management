# Feature Review: guided-first-days (whole branch, PR #107)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-05
**Challenged Date:** 2026-11-05
**Parent Branch:** main (origin/main)
**Merge Base:** 3262d2e0 (branch head 8198e8ae, `main` merged in)
**Files Reviewed:** 162 code files (+9,628 / −475). Excluded: `features/**` docs, `*.Designer.cs`, model snapshot (read directly by the breaking-change reviewer), lock files, images, `CLAUDE.md` maps. Full diff: 347 files, +44,012.
**Review method:** 7 parallel agents, adapted to this stack (MediatR + `Result<T>`, no ROP): backend code quality · error handling · business logic · breaking changes · device & UX · security · frontend logic. Challenge: 3 parallel verifiers read the full source, `origin/main` and the built CSS for every finding; each verdict below carries its evidence.
**Not raised (owner decisions):** manual ticks (all but the security code) · admin-only 30-day card · no tour library, passive tours · e-mail-only invitation · feedback as a plain entity · « Rester connecté » ticked by default in the **Windows** and Android shells (approved blueprint Part A — the security reviewer flagged the shared reception PC; left to the owner).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 55 |
| Confirmed | 30 |
| Confirmed (adjusted) | 24 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 (one half of old #20 was pre-existing; the finding kept its other half) |
| **Final findings** | 54 |

Dismissed: old #11 (`canResendInvitation`): `FrontendUrl` always has a value (defaults to `http://localhost:3000`, set by every hosted compose), so the « no public URL on hosted » case never happens. A never-signed-in account taking an invitation works (`AcceptStaffInvitationCommand` sets the password whatever the account's origin). Gating on `invitationPending` would remove the resend once a 7-day link expired.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Users/Commands/ResendStaffInvitationCommand.cs
- **Line:** 92
- **Anchor:** `ResendStaffInvitationCommandHandler.Handle` (route `UsersController.ResendInvitation`, :176)
- **Comment:** « Renvoyer l'e-mail » has no cooldown and no cap. There is no `[EnableRateLimiting]`, only the 600/min per-user limit (RateLimiting.cs:120), and `[AllowsWithoutSubscription]` lets a suspended cabinet through. Anyone can sign up, add a « colleague » with a victim's address and loop it: APEXA-signed mails with attacker-chosen names, the SMTP quota drained for real cabinets, a sender reputation burnt. Fix: a 2-min resend cooldown (precedent `RequestPasswordResetCommand.ResendCooldown` :83, `SignUpClinicCommand.ResendCooldown` :90), a daily cap per cabinet, and refuse a suspended cabinet.

### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetGettingStartedQuery.cs
- **Line:** 87
- **Anchor:** `GetGettingStartedQueryHandler.Handle`
- **Comment:** On demo.apexa.tn, `DemoCabinetRebuilder.ProvisionAdminAsync` creates a new clinic each night (`CreatedAt = now`) with an admin visitor and a fresh e-mail stamp. So every visitor gets the card with « Votre téléphone » still open, plus the « Bienvenue » tour. `DemoReadOnlyMiddleware` answers 403 to every tick, including closing the phone window, and `dashboard-page.tsx:195` has no `isDemo` gate. Fix: return `Hidden` when the deployment refuses writes, or skip the block when `useDemo().isDemo`.

### Finding 3
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Domain/Entities/User.cs
- **Line:** 566
- **Anchor:** `User.SetPassword` (with `EnrolTotpCommandHandler.Handle`, EnrolTotpCommand.cs:188)
- **Comment:** The 24 h re-show gives the same pending secret to anyone who passes step one. No password path clears it: `SetPassword` (reset, change, admin, console, CLI) leaves `ProtectedTotpSecret` alone, and the three 2FA resets refuse a pending-only account (ResetUserTotpCommand.cs:76, console :172, CLI :72). Someone with a leaked password copies the QR; the owner resets her password and then confirms that same secret, so the copy keeps working. Fix: `SetPassword` clears a pending secret + `TotpEnrolmentStartedAt` when `!IsTotpEnrolled`, as `RequireNewCredentials` (:335) already does, and the admin reset clears a pending-only one.
- **Challenge note:** Major → Minor. Before this branch a password holder could already enrol their own factor on an un-enrolled account and take it over outright. The new risk is a quiet copy of the owner's factor. The branch fixed the same gap in `RequireNewCredentials` but not on the password paths. Cheap fix.

### Finding 4
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/UserFeedbackPrompt.cs
- **Line:** 51
- **Anchor:** `UserFeedbackPrompt.MarkShown`
- **Comment:** Only « Plus tard » moves the prompt to its second showing. `MayShow` (:44) and `GetFeedbackPromptQuery` (:62-78) have no age cap, so an ignored « Une semaine avec APEXA » card comes back every session for good. That contradicts the class doc (« at most MaxShows ») and the blueprint's « never more than twice ». Fix: count a new showing per day/session, or treat N ignored sessions as « later ».

### Finding 5
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/feedback/feedback-prompt-card.tsx
- **Line:** 132
- **Anchor:** `FeedbackPromptCard`
- **Comment:** Below 768 px the card is fixed full-width above the bottom bar (~182 px tall on a coarse pointer, z-40) and is mounted in the header, so it shows on every screen until answered. On the phone agenda it covers the « + » (`absolute end-4 z-30`, appointment-calendar.tsx:3858). `<main>` reserves only `p-4`, so it also covers the last rows of every list; app-shell.tsx:151-160 says to re-add `coarse:pb-20` « the day something floats over `<main>` again ». Fix: add that padding while the card is up, and a one-line bar below `md:`.
- **Challenge note:** Major → Minor. One tap on « Plus tard » clears it, tapping an empty slot still books, and spec § E mandates « docked above `--bottom-inset` » (keep the dock, do not move it). The « 80 px reserved » claim was wrong: it is 16 px, which makes the overlap worse. The re-show is Finding 4.

### Finding 6
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/post-visit-review-popup.tsx
- **Line:** 501
- **Anchor:** `PendingReviewPrompt`
- **Comment:** « Séance terminée » waits while `FEEDBACK_CARD_OPEN_ATTR` is on `<body>`. Only an answer, « Plus tard » or « Ne plus demander » removes it, and there is no timeout, so an ignored card holds the visit question back for the tab's whole life. Fix: let the visit question win (hide the card while it shows), or cap the hold.
- **Challenge note:** Major → Minor. Spec § E mandates the wait (« also waits while the feedback card or a guide is up »); do not remove it, only bound it. Phones never get the visit question anyway (`isPhone` gate).

### Finding 7
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 54
- **Anchor:** `GettingStartedBlock` first-visit effect + `GuideProvider` value memo (guide-provider.tsx:209-212)
- **Comment:** `continueTour` is a new function whenever `running` changes, and it is in this effect's deps (:69). When `localStorage` throws, `read()` returns null and `write()` swallows the error, so `hasSeenCard` stays false. Then « Plus tard » brings « Bienvenue » straight back, and « C'est parti » loses its guide (the effect fires again before the move to `/users`, its prompt replaces the run). Fix: a stable `continueTour` (ref), plus an in-memory « seen » fallback.
- **Challenge note:** Major → Minor. Harmless while storage works; it only fires with site data blocked (modern private windows do not throw).

### Finding 8
- **Severity:** Minor
- **Category:** Device & UX / Frontend
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/feedback/feedback-dialog.tsx
- **Line:** 147
- **Anchor:** `FeedbackDialog.close`
- **Comment:** `useDirtyGuard` listens only to `input`/`change` (use-dirty-guard.ts:92-93). Stars, chips and the Radix checkbox fire neither, so on « Aide » they are lost to Escape, an outside tap or ✕ with no confirm. On the week-one form, `close()` posts only `helpRating`, so the ease star and the chips are dropped (nothing is sent if `helpRating` is null) while « Merci… » still shows. Fix: dirty = `avisFilled`; on week-one close send `avisBody()` when `avisFilled`.
- **Challenge note:** Major → Minor. What is lost is feedback, not clinic data.

### Finding 9
- **Severity:** Minor
- **Category:** Device & UX / Frontend
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/guide/guide-overlay.tsx
- **Line:** 283
- **Anchor:** `GuideOverlay` (portal) + keydown listener (:139-144)
- **Comment:** The bubble is portalled to `document.body`. `unlessOnFloatingNotice` only stops the dialog closing; Radix FocusScope still pulls focus back and wraps Tab. So inside a window « Suivant » is mouse-only, and Escape (closing the dialog) ends the guide. Fix: bind Enter/ArrowRight to « Suivant » when focus is not in a field, or portal the bubble into the scoped dialog content.
- **Challenge note:** Major → Minor. The `aria-hidden` half was false: `hideOthers` keeps `[aria-live]` nodes and the bubble is `aria-live="polite"`. The tours are optional and the steps can be ticked by hand.

### Finding 10
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Auth/Queries/PreviewStaffInvitationQuery.cs
- **Line:** 61
- **Anchor:** `PreviewStaffInvitationQueryHandler.Handle`
- **Comment:** The catch-all returns `invitation_invalid`, so a DB failure becomes 410 (AuthController.cs:345), and the page (invitation/page.tsx:29) says « Lien expiré… demandez à l'administrateur de vous le renvoyer ». The « votre lien reste valable · Réessayer » branch is never reached. Fix: return a code-less failure, as the siblings `AcceptStaffInvitation` (:77) and `CompletePasswordReset` (:155) do.
- **Challenge note:** Major → Minor. Needs a server fault during the read; nothing is lost and a resend works.

### Finding 11
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — the second half was pre-existing)
- **File:** web/lib/hooks/use-inline-new-patient.ts
- **Line:** 117
- **Anchor:** `useInlineNewPatient.restore` (callers create-appointment-dialog.tsx:1079, treatment-plan-form-modal.tsx:373)
- **Comment:** On `main` the booking draft was flat (`newPatientFirstName…`, `createdPatientId`), and it is now `newPatient: {…}` under the same `v: 1`. `carried-draft.ts` `parse()` checks the envelope only, never the shape. So an old-build draft makes `s.firstName` throw inside `takeCarriedOnce`'s `.then`: the draft is already claimed, the acts, date and devis lines are lost, and the switch refusal is never armed. This hits the Windows/Android shells within the 2 h draft window, across the deploy reload or a switch between servers on different builds. Fix: accept `undefined` and map the old flat keys.
- **Challenge note:** The « `createdName` not restored » half is pre-existing (`main` never restored it either) and was dropped.

### Finding 12
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Feedback/Commands/SubmitFeedbackCommand.cs
- **Line:** 150
- **Anchor:** `SubmitFeedbackCommandHandler.Handle` (route `FeedbackController.Submit` :55)
- **Comment:** Every submit e-mails every active console account (ConsoleTeamFeedbackNotice.cs:51), with no rate-limit attribute and `[AllowsWithoutSubscription]`. Any self-signed-up user can push ~600 notices a minute into each vendor inbox. Fix: a per-user cooldown / daily cap, or one digest mail.

### Finding 13
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Users/Commands/CreateClinicUserCommand.cs
- **Line:** 205
- **Anchor:** `CreateClinicUserCommandHandler.Handle` (also ResendStaffInvitationCommand.cs:97)
- **Comment:** SMTP `SendTimeout = 20 s` (SmtpTransactionalEmailSender.cs:34) equals the browser's `REQUEST_TIMEOUT_MS = 20_000` (client.ts:945), and the DB work runs before the send. So a hung SMTP always loses the race: the admin reads « Impossible de joindre le serveur » while the account exists and the shown-once link is lost. Fix: keep the in-request send well under 20 s (~8 s), or send after responding.

### Finding 14
- **Severity:** Minor
- **Category:** Business Logic / Frontend
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 57
- **Anchor:** `GettingStartedBlock` effect (ready branch)
- **Comment:** `wasVisible` is a per-mount ref that starts false. When the last step is done on another page (fiche, letterhead), the dashboard mounts with `visible=false` and « Bienvenue dans votre nouveau cabinet ! » never fires. Line 68 also updates it while `!shown`, so it never fires after « Masquer ». A tour's finish and a hand tick on the card are covered. Fix: the server says why the card is hidden (all done vs day 30), and the client celebrates once on « all done ».

### Finding 15
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 65
- **Anchor:** `GettingStartedBlock` effect — `celebrateStep(fresh[fresh.length - 1], …)`
- **Comment:** `takeNewlyDone` marks every newly done step, but only the last one gets confetti. A booking with « Nouveau patient » ticks patients + appointment together, and the patients moment is lost for good, against the owner's « confetti per step ». Fix: celebrate each in turn, or mark only the one shown.

### Finding 16
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 73
- **Anchor:** `GettingStartedBlock.hide`
- **Comment:** `premiers-pas-next` exists only on the card (:196). « Masquer » during the hand-over prompt unmounts it, but the run goes on: the bubble stays pointing at nothing, `GUIDE_RUNNING_ATTR` blocks the visit question, and after `MISSING_TIMEOUT_MS = 10 s` the toast says « Le guide « Premiers pas » s'est arrêté. ». Fix: end the run on hide.

### Finding 17
- **Severity:** Minor
- **Category:** Business Logic / Device & UX
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 76
- **Anchor:** `GettingStartedBlock.hide`
- **Comment:** The « Masquer » toast says the steps stay in « Aide ». On a SelfHostedLan PC, `HelpMenu` returns null (feedback 404, no contact phone; help-menu.tsx:167, as spec § D wants), while `GetGettingStartedQuery` has no deployment gate, so the card still shows. Paramètres' `HelpRows` gets no `premiersPas`. Fix: word the toast per deployment (« Personnaliser » on LAN).

### Finding 18
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/lib/guides/secretary.ts
- **Line:** 25
- **Anchor:** `secretaryGuide` (also appointment.ts:40, patients.ts:25, dental-record.ts:70)
- **Comment:** Four tours say « rien n'est envoyé / enregistré », but the overlay is `pointer-events-none` on purpose and the lit submit stays live. Type a name + e-mail when the tour points at the fields, press the lit button, and a real invitation, patient or booking is saved. Fix: block clicks on the lit submit mark during a tour, or reword (passive tours are the owner's decision; only the sentence is wrong).

### Finding 19
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/guides/dental-record.ts
- **Line:** 20
- **Anchor:** `dentalRecordGuide.steps[0].emptyMark`
- **Comment:** `emptyMark: "patients-add"` is the header button, always drawn (patients/page.tsx:148), while `patients-list` only exists once rows load (patients-table.tsx:436). After `EMPTY_SETTLE_MS = 1500` the overlay shows the empty text and « Terminer », and pressing it ticks « Première fiche » in a cabinet that has patients. Fix: mark the real empty state in `patients-table.tsx`.

### Finding 20
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/EnrolTotpCommand.cs
- **Line:** 197
- **Anchor:** `EnrolTotpCommandHandler.Handle` (step one)
- **Comment:** `hadSecret` reads only `ProtectedTotpSecret`, which `DisableTotp` clears (User.cs:547). After « Déclarer perdu » or an admin/console reset the screen says « first », while the phone still holds the dead « APEXA » line that the amber warning exists for. Fix: also `Replaced` when `TotpReenrolmentRequired` (« Déclarer perdu »). The admin and console resets set no flag, so they need a « had a factor » marker too.

### Finding 21
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/help-menu.tsx
- **Line:** 194
- **Anchor:** `HelpMenu` (also getting-started-card.tsx:247)
- **Comment:** In the built CSS, the `@media (pointer:coarse)` block comes after the `md:` and `sm:` blocks at equal specificity, so `coarse:size-11` beats `md:w-auto` / `sm:w-auto`. On a ≥ 768 px tablet, « Aide » is a 44 px box with icon + label spilling out, and the video button on the card has the same problem. Fix: `coarse:h-11 coarse:min-w-11`.

### Finding 22
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/guide/guide-overlay.tsx
- **Line:** 393
- **Anchor:** `placeBubble` (constant at :355)
- **Comment:** With `viewportFit: "cover"`, `--bottom-inset` = 56 px + the safe area (34 px on a notched iPhone), but the collision box assumes 56 px. So the real bubble sits 34 px higher and can cover a lit control in that band. Fix: test the bubble's measured rect.

### Finding 23
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 216
- **Anchor:** `GettingStartedCard`
- **Comment:** At rest, a tickable circle looks exactly like the untickable « Code de sécurité » one (:225-229). The ✓ appears only on hover/focus, so on a tablet the manual tick the owner asked for has no visible cue. Fix: a faint ✓ at rest on coarse (`coarse:opacity-40`).

### Finding 24
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/user-management.tsx
- **Line:** 434
- **Anchor:** `UserManagement.rowMenu`
- **Comment:** « Réinitialiser le second facteur » deletes the factor + recovery codes, yet the item is plain (« Désactiver » beside it is `text-destructive`). `StepUpDialog` has a fixed « Confirmer » with no variant, while frontend-web.md § 13 requires a `variant="destructive"` button. Fix: `text-destructive` on the item plus a destructive confirm first (precedent relay-card.tsx:267/297), or a variant prop.

### Finding 25
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.UnitTests/Features/Users/StaffInvitationTests.cs
- **Line:** 211
- **Anchor:** `StaffInvitationTests` (Accepting section)
- **Comment:** Accept/Preview are tested for a wrong token, a spent link and a short password, but not for the three refusals `ResolveAsync` (:89-95) makes: a link over 7 days old, a deactivated account, and an account with `LastLoginAt` set. Fix: add the three cases.

### Finding 26
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.UnitTests/Features/Users/ListUsersQueryHandlerTests.cs
- **Line:** 22
- **Anchor:** `ListUsersQueryHandlerTests` (constructor)
- **Comment:** `GetPendingUserIdsAsync` is always stubbed empty, and no test anywhere asserts `InvitationPending == true`. Fix: one test where one id is pending and only that row is flagged.

### Finding 27
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.UnitTests/Api/SessionFamilyPurgeJobTests.cs
- **Line:** 28
- **Anchor:** `Purges_Spent_Signups_And_Resets_With_Their_Own_Retention`
- **Comment:** The job calls `_invitations.PurgeSpentAsync` (SessionFamilyPurgeJob.cs:91), but the test never checks it. Fix: add the verify with its retention.

### Finding 28
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/FeedbackController.cs
- **Line:** 34
- **Anchor:** `FeedbackController.GetPrompt`
- **Comment:** No test asserts the capability 404s: the three feedback routes (`ServesPlatformConsole`), the invitation preview/accept (AuthController.cs:331/357) and « Renvoyer » (UsersController.cs:178). Fix: assert `NotFoundResult` on SelfHostedLan with the mediator never called (`MessagingCapabilityRegistrationTests.cs:97-98` shape).

### Finding 29
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Dashboard/GettingStartedRules.cs
- **Line:** 86
- **Anchor:** `GettingStartedRules.IsDone`
- **Comment:** The comment says an invitation not yet accepted « is not a colleague who can work yet », but the step ticks on invite (an invited account is created active). Fix the comment only.
- **Challenge note:** The blueprint's done rule is « ≥ 2 accounts in the cabinet », so ticking on invite is what the spec asks for. Do not change the behaviour.

### Finding 30
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Dashboard/Commands/UpdateDashboardPreferencesCommand.cs
- **Line:** 93
- **Anchor:** `UpdateDashboardPreferencesCommandHandler.Handle` (+ use-dashboard-preferences.ts:161)
- **Comment:** `gettingStarted` joined `DashboardKpiKeys.All` (22 keys) and `DASHBOARD_BLOCK_KEYS`, so the « keep at least one block » guard is off by one on both sides. A doctor, or an admin after day 30, can hide all 21 real blocks. Fix: leave `gettingStarted` out of both counts.
- **Challenge note:** Minor → Suggestion. It takes hiding every block one by one, and « Personnaliser » undoes it. The count is 21, not 18.

### Finding 31
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs
- **Line:** 262
- **Anchor:** `LoginCommandHandler.Handle` → `user.RecordPhoneAppSignIn`
- **Comment:** `PhoneAppFirstSignInAt` / `PhoneAppPlatform` (and `TotpEnrolmentStartedAt`) are not in `ClinicRelayScope.PerSideColumns[User]`. A first phone-app sign-in against a standby PC de secours would then be refused by the relay fence. Fix: add them to `PerSideColumns[User]`.
- **Challenge note:** Minor → Suggestion. Practically unreachable: the Android shell only moves to the PC once the PC holds the saves, and then signs in by ticket, not `Auth.Login`. The « overwritten by the hourly check » half was wrong: writes made while the PC holds go back at the return.

### Finding 32
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/lib/hooks/use-getting-started.ts
- **Line:** 29
- **Anchor:** `useGettingStarted`
- **Comment:** `failed` is returned and read by nobody (dashboard-page.tsx:525). help-menu.tsx:161's comment (« the card's own read reports its failures ») is false. Fix: drop the dead return value and fix the comment.
- **Challenge note:** Minor → Suggestion. The blueprint (Part B) says « the card disappears on a failed read », so do NOT add a failure banner. The hook re-reads on realtime and visibility.

### Finding 33
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/guide/guide-provider.tsx
- **Line:** 127
- **Anchor:** `GuideProvider.finish`
- **Comment:** A failed `tickStep` is swallowed on purpose (:128), but the default `celebrate` still shows `toast.success(guide.doneText)`. If the write fails and the next read succeeds, the same step's tour is offered again. Fix: skip the success toast and `continueTour` on a failed tick.
- **Challenge note:** Minor → Suggestion. All five guide keys are tickable and the endpoint is subscription-exempt, so it needs a server fault; a network loss also fails the next read, which stops the loop.

### Finding 34
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/user-management.tsx
- **Line:** 315
- **Anchor:** `UserManagement.confirmAction`
- **Comment:** On `invitation_not_needed` (she signed in meanwhile), the catch never calls `loadData()`, and a sign-in broadcasts nothing, so the row keeps « Renvoyer l'e-mail ». Fix: `loadData()` on that code.
- **Challenge note:** Minor → Suggestion. Two of the three claims were false: `showErrorToast` shows the server's own sentence, and `AlertDialogAction` closes the dialog.

### Finding 35
- **Severity:** Suggestion
- **Category:** Error Handling / Device & UX
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/dashboard/phone-app-dialog.tsx
- **Line:** 96
- **Anchor:** `PhoneAppDialog`
- **Comment:** « Scannez ce code avec l'appareil photo du téléphone » (`hidden sm:block`) does not depend on `download`, so it stays when the meta read failed and no QR is drawn. Fix: render it only when `download` is set.
- **Challenge note:** Minor → Suggestion. Only on a failed read, and the « Ouvrir la page de téléchargement » button beside it still works.

### Finding 36
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Users/Commands/ResendStaffInvitationCommand.cs
- **Line:** 96
- **Anchor:** `ResendStaffInvitationCommandHandler.Handle`
- **Comment:** `_clinics.GetByIdAsync` runs after the commit, inside the main try (:59-116). If it throws, the admin is told the resend failed and the new link is never shown. Create guards the same step in `SendInvitationAsync` (:222-244). Fix: one shared never-throwing issue-and-send helper in `StaffInvitations` (it can return the unused `StaffInvitationIssued`, Finding 47).
- **Challenge note:** Minor → Suggestion. Needs a read failure right after a good save, and pressing « Renvoyer » again works.

### Finding 37
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/user-management.tsx
- **Line:** 687
- **Anchor:** `UserManagement` (row badge, also :795)
- **Comment:** « Invitation envoyée » means a usable invitation row exists, not that the mail left. Fix, if wanted: store `EmailedAt` and label « E-mail non parti ».
- **Challenge note:** Minor → Suggestion. Spec Part C asks for exactly this chip from `InvitationPending`, and the admin was already shown « L'e-mail n'est pas parti » + the link at create/resend. `EmailedAt` is beyond the spec.

### Finding 38
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/dashboard-header.tsx
- **Line:** 422
- **Anchor:** `DashboardHeader`
- **Comment:** At 320 px on a coarse pointer, the 44 px « Aide » leaves the patient search ~64 px of text, and ~20 px in the iPhone home-screen app with « Retour ». Fix: drop `pr-9` below `md:` while idle.
- **Challenge note:** Minor → Suggestion. Only 320 px phones (375 px leaves ~119 px). Spec Part D requires the icon in the header below `md:`, so do NOT move it to « Plus ».

### Finding 39
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/feedback/feedback-dialog.tsx
- **Line:** 344
- **Anchor:** `StarRow`
- **Comment:** A `radiogroup` with no roving tabindex or arrow keys: five tab stops per row. Tab + Enter/Space still works.
- **Challenge note:** Minor → Suggestion. Every other `role="radiogroup"` in `web/` has the same shape (late-arrival chips, postpone dialog, edit dialog, `ui/mode-segmented`), and no rule asks for arrows. A repo-wide change if ever.

### Finding 40
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Domain/Entities/StaffInvitation.cs
- **Line:** 77
- **Anchor:** `StaffInvitation.Consume`
- **Comment:** `Consume` has no `IsUsable` guard, and `Rearm` skips the `invitedByUserId` check. Optional hardening.
- **Challenge note:** Minor → Suggestion. Blueprint: « built like `PasswordResetRequest` », whose `Consume` (:150) is the same one-liner. The only caller checks `IsUsable` first, and `Rearm`'s callers always pass `admin.Id`.

### Finding 41
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/AcceptStaffInvitationCommand.cs
- **Line:** 52
- **Anchor:** `AcceptStaffInvitationCommandHandler.Handle`
- **Comment:** The short-password refusal is an inline sentence with no code. Tidier: `ClinicAuthRefusals.PasswordPolicy`.
- **Challenge note:** Minor → Suggestion. `CompletePasswordResetCommand.cs:97-98` returns the same code-less sentence, and no client branches on it.

### Finding 42
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/lib/hooks/use-getting-started.ts
- **Line:** 24
- **Anchor:** `useGettingStarted.load`
- **Comment:** Overlapping reads have no sequence guard, so a read already in flight when a hand tick lands can draw the step unticked until the next read. Fix: the `useFreeSlots` counter (use-free-slots.ts:24-47).
- **Challenge note:** Minor → Suggestion. Narrow window, nothing lost.

### Finding 43
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Users/Commands/CreateClinicUserCommand.cs
- **Line:** 219
- **Anchor:** `CreateClinicUserCommandHandler.SendInvitationAsync`
- **Comment:** The « same three-field test… » summary that sat above `HasPractitioner` now sits on `SendInvitationAsync`; `HasPractitioner` (:247) has none. Move it back.
- **Challenge note:** Minor → Suggestion. Doc only.

### Finding 44
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Feedback/FeedbackRules.cs
- **Line:** 9
- **Anchor:** `FeedbackRules`
- **Comment:** The comment names `FeedbackAreasMirrorTests`; the guard is `FeedbackMirrorTests` (kinds + areas). Fix the name.
- **Challenge note:** Minor → Suggestion. A stale name in a comment.

### Finding 45
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Feedback/ConsoleTeamFeedbackNotice.cs
- **Line:** 26
- **Anchor:** `ConsoleTeamFeedbackNotice.SendAsync`
- **Comment:** A line-for-line copy of `ConsoleTeamSignupNotice.SendAsync`. The blueprint § E asked for the copy, so a shared « send to every console account » helper is optional.

### Finding 46
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Users/StaffInvitations.cs
- **Line:** 13
- **Anchor:** `StaffInvitationIssued`
- **Comment:** Declared, never used. Delete it, or make it the return of the helper in Finding 36.

### Finding 47
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/BackgroundJobs/SessionFamilyPurgeJob.cs
- **Line:** 91
- **Anchor:** `SessionFamilyPurgeJob` (purge pass)
- **Comment:** Invitations are purged with the password reset's `ConsumedRetention`, so changing one changes the other. Fix: `StaffInvitations.ConsumedRetention`.

### Finding 48
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Users/StaffInvitations.cs
- **Line:** 130
- **Anchor:** `StaffInvitations.Compose`
- **Comment:** « Le lien est valable 7 jours » is hard-coded beside `StaffInvitation.Lifetime`. Build it from `Lifetime.TotalDays`.

### Finding 49
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Auth/Queries/PreviewStaffInvitationQuery.cs
- **Line:** 73
- **Anchor:** `StaffInvitationLink`
- **Comment:** The shared link reader lives in a query file, so the Accept command imports `Features.Auth.Queries`. Housekeeping only, since other commands import query namespaces too (Relay); it could move next to `StaffInvitations`.

### Finding 50
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/DTOs/GettingStartedDto.cs
- **Line:** 3
- **Anchor:** `GettingStartedStepDto`
- **Comment:** The summary says `Done` = « the cabinet's own data says it is done », but it is `byData || byHand` (GettingStartedRules.cs:77). Fix the line.

### Finding 51
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetGettingStartedQuery.cs
- **Line:** 97
- **Anchor:** `GetGettingStartedQueryHandler.Handle`
- **Comment:** « Premier rendez-vous » counts any row (`CountByClinicIdAsync` with no filter), including a patient-less « créneau occupé » and a cancelled visit. The blueprint's « ≥ 1 appointment » allows that, so this is a refinement: exclude Cancelled and require a patient.

### Finding 52
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetGettingStartedQuery.cs
- **Line:** 100
- **Anchor:** `GetGettingStartedQueryHandler.Handle`
- **Comment:** Hand ticks are per user while 5 of 7 steps are cabinet facts. This only matters with two admins in the first 30 days.

### Finding 53
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/dashboard/getting-started-card.tsx
- **Line:** 23
- **Anchor:** `UNTICKABLE_STEP` / `GuideProvider.finish` cast (guide-provider.tsx:126)
- **Comment:** `UNTICKABLE_STEP` mirrors the server's `TickableSteps` with no guard, and `guide.key as GettingStartedKey` also covers `"nextStep"` (safe only because every nextStep run sets `onDone`). Fix: the server sends `tickable` per step; narrow the cast.

### Finding 54
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** web/lib/api/feedback.ts
- **Line:** 24
- **Anchor:** `FeedbackRefusalCode`
- **Comment:** No consumers. Drop the export. The new server codes need no front-end constant since nothing branches on them.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 2 |
| Minor | 26 |
| Suggestion | 26 |
| **Total** | 54 |
