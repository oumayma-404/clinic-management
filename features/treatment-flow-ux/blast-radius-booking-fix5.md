# Blast radius — booking fix 5 (continuation always visible · « Séances » on a treatment act)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `ContinueTreatmentList` fold rule + label | shared booking block | create + edit dialogs (only two) | must re-test both — fold now shows with 0 rows |
| 2 | « Suite d'une séance passée… » label | UI string matched by a test | `e2e/specs/booking-browser.spec.ts` (`/séance passée/`), `scenarios.md` BOOK-49, `notes.md` | must change — regex + docs in this commit |
| 3 | `AppointmentActsPicker` new optional props `onEditPlanSteps` / `canEditPlanSteps` | additive props | create + edit dialogs; N26/N28 read `<AppointmentActsPicker` only | unaffected — optional, absent = button hidden |
| 4 | « Séances » button + tick chips on a devis act row | new UI on `stepOptions` rows | `toggleStep` (unchanged), `groupActs`, `actLabelsOf` | must re-test — chips call the same `toggleStep` as the strip |
| 5 | `planItemStepOptions` extracted from `planItemToPreset` | pure mapper | `planItemToPreset` (both dialogs' presets, workspace « Planifier ») | unaffected — same mapping, one owner |
| 6 | `PlanItemStepsDialog` opened from a booking dialog | 2nd host of an existing dialog | `plan-workspace.tsx` (unchanged) | must re-test — nested dirty guards: inner pop lands on the outer marker, which `onPop` ignores |
| 7 | `SetItemSteps` endpoint | `AdminOrDoctor` | secretary booking | the add button is withheld for a secretary (`isAdminOrDoctor`) |
| 8 | review fix — edit dialog `versionToSend` after the séances window | the token of THIS visit | `performUpdate` + `handleCancelAppointment` (the two writes carrying a version) | must change — both send it; superseded by any re-hydration (keyed on the version it replaced) |
| 9 | review fix — picker `commitRows` / `filterRows` | carries `openProtocols` / `totalDrafts` with the rows | `removeGroup`, `addPlanSeance`, `toggleStep` untick + tick-clone insert | must re-test — the four row writers that move indices |
| 10 | review fix — `useDirtyGuard` top-of-stack | shared hook | 15 dialogs (caisse ×2, both booking dialogs, patient form, facturer, invoice form, payment, odontogram, fiche, échéance payment, séances window, revise schedule, settle, plan form) | must re-test the nested case only — a single open guard is always the top one, so its behaviour is unchanged |
| 11 | review fix — create dialog `planActs` withheld while `isPlanScheduling` | « Actes du devis » group | workspace « Planifier » only | unaffected elsewhere — same value as before when the loader is on |
