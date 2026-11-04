# Guided first days — blast radius

Written before the first edit (`.claude/rules/regression-safety.md` § 1). Verdicts: `unaffected` (say why) ·
`must change` (moves in the same commit) · `must re-test` (a row of the `/test-in-browser` tier D sweep).
Line numbers are `origin/main` @ `66ecc657`.

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `EnrolTotpCommand` step one | sign-in enrolment | first enrolment, replacement after a recovery code (15-min grant), both shells, `e2e/bootstrap.mjs:115-136` | must re-test — all four |
| 2 | `User.IssueTotpSecret` | sets the new timestamp | 8 test setups (`ClinicTotpAuthTests`, `CompletePasswordResetCommandHandlerTests:142`, `ClinicMoneyMaskTests:38`, `PlatformPasswordResetTests:148`, `PlatformSecondFactorResetTests:90`, `DeclareRelayLostCommandTests:35,180`, `RelayInstallOfferTests:34`) | unaffected — optional `nowUtc` parameter |
| 3 | `User.RequireNewCredentials` | « Déclarer perdu » | `DeclareRelayLostCommand.cs:91`, every member of the cabinet | must change — discard a pending secret; test it |
| 4 | Users table: 3 new columns | migration | PC de secours copy + hourly digest (`ClinicRelayScope`), `verify-schema`, the `xmin` scaffold trap | must re-test — `verify-schema` before/after |
| 5 | `TotpEnrolmentDto` | wire shape | login page, `e2e/bootstrap.mjs` (reads `secret`) | unaffected — one added optional field |
| 6 | Login body + BFF `local-login` route + `LoginCommand` | sign-in | every sign-in on every surface; `RedeemRecoveryCodeCommand` | must change — route forwards `clientSurface`; recovery sign-in records too |
| 7 | « Rester connecté » default | login page state | browser sign-in, both shells, `?enrol=1` path, idle rule N15 (`idle-limit.ts`) | must re-test |
| 8 | `web/lib/push/web-push.ts` `isStandalone()` | moves to `web/lib/shell.ts` | web push capability | must change — import from the one owner |
| 9 | `DashboardController` + `DashboardKpiKeys` + `dashboard-blocks.ts` | block key + admin-only route | customiser, `UpdateDashboardPreferencesCommand`, `DashboardKpiKeysTests`, `ControllerAuthorizationCoverageTests` | must change — both sides in one commit |
| 10 | `dashboard-customizer.tsx` | `omitKeys` prop | every user's « Personnaliser » | must re-test — « Tout afficher » / « Par défaut » |
| 11 | `dashboard-page.tsx` | card mount | admin and doctor layouts | must re-test — 5 widths, 730 px tall |
| 12 | `user-management.tsx` `?invite=1` + part C | users screen | admin create/reset flows; uncommitted `feature/security-remediation` edits | must change — build on top of security-remediation |
| 13 | `letterhead-card.tsx` `#en-tete` | settings card | `clinic-settings.tsx:984` | must re-test — opens only on the hash |
| 14 | `CreateClinicUserCommand` | account creation | users screen, `CreateClinicUserCommandHandlerTests`, doctor practitioner creation | must change — online path only |
| 15 | `DeploymentProfile` | new capability | `DeploymentProfileTests` matrix + `hostedOnlyCapabilities` | must change |
| 16 | `ClinicArchiveScope.Excluded` / `ClinicRelayScope.Excluded` | data scopes | archive zip, PC de secours copy, their tests | must change — invitations and feedback stay out |
| 17 | `PlatformReadShape` | console's closed field list | `PlatformReadShapeTests` (both directions); the privacy promise | must change — with the owner's answer 4 |
| 18 | `RealtimeResourceResolver.ExcludedAreas` | realtime keys | `RealtimeResourceResolverTests` | must change — add `Feedback` |
| 19 | `dashboard-header.tsx` | header | every screen and role; rule `header-orphans` | must re-test — 320 px with four controls |
| 20 | `create-appointment-dialog.tsx` | new-patient extraction + guide marks | every booking with a new patient | must re-test — new patient, duplicate both answers, retry never doubles, phone check |
| 21 | `treatment-plan-form-modal.tsx` | « Nouveau patient » | patient page (preset), plan workspace, Traitements | must re-test — all three |
| 22 | `ui/floating-notice.ts` | « floats over dialogs » list | every Dialog/Sheet outside-press rule; staff chat; N52 | must re-test — chat notices still float over a dialog |
| 23 | `post-visit-review-popup.tsx` | opening guard | every desktop/tablet session | must re-test — never during a guide or the feedback card |
| 24 | `web/app/rappels/page.tsx` | coming-soon page | `feature/sms-rappels` reopens the real screen there | unaffected — page content replaced, `nav.ts` untouched |
| 25 | Console navigation | third link | « Cabinets », « Journal » | unaffected — additive |
| 26 | Welcome e-mail | one detail line | `ConsoleTeamSignupNoticeTests` (visitor gets exactly one e-mail) | unaffected — same e-mail |
| 27 | `public-routes.ts` | `/invitation` | middleware, `public-routes-have-one-owner` | must change |
| 28 | `HelpRows({ phone, whatsapp, onFeedback? })` (R2) | shared « Aide » rows | header « Aide », Paramètres « Besoin d'aide ? » | must re-test |
| 29 | `useInlineNewPatient` + `NewPatientFields` + `DuplicatePatientDialog` (R2) | extracted from the booking dialog | booking dialog, devis form | must re-test — new patient, duplicate both answers, create-once, phone check |
| 30 | `CreatedClinicUserDto.temporaryPassword` nullable (C) | create response | `user-management.tsx` (the only reader) | must change — branch on the response: temp password vs invitation link |
| 31 | `/login` `?email=` prefill (C) | login effect | enrol (`?enrol=1&email=`), replace (`?replace=1&email=`), plain sign-in | must re-test — plain sign-in empty, prefill shown, `passwordChanged=1` banner |
| 32 | « Réinitialiser » ↔ « Renvoyer l'e-mail » swap (C) | users row actions | both trees; reset after first sign-in | must re-test — reset still offered once she has signed in |
| 33 | `dentalRecordGuide` → a tour (`tour: true`, manual steps 3–9) (2026-11-04 remarks) | one guide | overlay, provider, N54 marks | must re-test — every step lit, « Terminer » last, nothing saved |
| 34 | Overlay « Terminer » on a LAST manual step | bubble button label | the four other guides end on `savedAndClosed`/`route`, never a manual last step | unaffected — label only differs on a manual last step |
| 35 | Ticking a step by hand: `PUT`/`DELETE getting-started/{key}/tick` (the card's circle, « Terminé » on the phone window, the end of the tour) | new write | `GettingStartedRules.TickableSteps` (all but the security code) | must re-test — tick, untick, celebrated once, security code refused |
| 36 | `GettingStartedFacts.TickedSteps` + `GettingStartedStepDto.DoneByHand` (both optional) | rule input / wire field | `GettingStartedRules.Steps`, the card | unaffected — absent = old answer |
| 37 | `GetGettingStartedQueryHandler` reads `UserDashboardPreference` | one more read | the card | must re-test — card renders, steps tick |
| 38 | `UserDashboardPreference.TickedStepsCsv` + migration `AddFeedbackOtherAndTickedSteps` | shared per-user row | `UpdateDashboardPreferencesCommand` (hidden KPIs) | must re-test — a PUT of hidden blocks keeps the ticked set |
| 39 | `GET /meta/phone-app` → `{ url, qrPng }` | wire shape | `phone-app-dialog.tsx` (only reader) | must change — done |
| 40 | `ProductFeedback.LikedOther/HardOther` + `SubmitFeedbackCommand` | new fields, empty-check widened | week-one card, « Aide » form, notice e-mail, console « Retours », `PlatformReadShape` | must re-test — stored, e-mailed, listed |
| 41 | `FeedbackDialog` (tabs for « Aide », « Autre » fields) | shared form, two entries | week-one card (star kept on close), « Aide » | must re-test — both entries |
| 42 | Fiche modal: `<div data-guide>` wrappers around « Notes de séance » / « Prescription » | layout | the fiche de soins | must re-test — both sections still open and close |
