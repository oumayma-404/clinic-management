# Handoff — devis · fiche · RDV flexibility, wave 4 (H + I + J — the last wave)

Start here in a new session. Read this file, then `notes.md` beside it (waves 1–3, what each did and why).

## The goal (owner's words)

« I need the treatment plan, fiche, RDV to work spotless, no bugs whatsoever … the app should not block edits,
because doctor work always needs to refine and edit along the way … unless it is nonsense. »

**Talk to her:** English, short bullets, tables, no paragraphs (`.claude/rules/response-style.md`). She asks for
progress as a table + percentage; give it that way.

## Where things stand (2026-09-23)

| | |
|---|---|
| Audit board (76 findings, root causes A–J) | https://claude.ai/artifact/93SGuXQNLHUM352jJUP5aW |
| Older 42-row board (« old NN ») | https://claude.ai/artifact/PGmXJNc91ZymEuNy9gi9LW |
| Wave 1 (A–D) | ✅ `ecd8f131` |
| Wave 2 (E + F + C4) | ✅ `e2552551` |
| Wave 3 (G — money) | ✅ `01e59505` — all three on `feature/security-remediation`, **not pushed** |
| Progress | 56 / 76 (~74 %) |
| Left | **wave 4 = H (11) + I (4) + J (5) = 20**, plus 1 carry-over (C4 reopen) |

## Owner decisions already taken — do not re-ask

1. Total lowered below what was paid → a « rendu » dated today on the devis, one short question, no avoir. (done, G3)
2. Reception booking a multi-séance act → automatic split kept, reception may create the draft. (done, E3)
3. Remise / « Total convenu » → keep the agreed dates, take the difference off the last unpaid rows. (done, G2)

Standing rules: no helper/explanation text under controls · never trade a capability for space · decide reversible
calls yourself · ask only when being wrong spends money or loses data.

---

## Wave 4 — the 20 rows

⚠️ Line numbers are from the audit (before waves 1–3). **Grep the symbol, not the line.**
Three rows look already done by earlier waves — **verify, don't redo** (marked « verify »).

### H — status and date edits that silently don't happen

| # | Bug | Evidence | Fix |
|---|---|---|---|
| H1 | Move a « Terminé » / cancelled visit's date → 200 « enregistré », date not moved (doctor/notes are) | `UpdateAppointmentCommand` → `Appointment.Reschedule` early branches on `Completed` / `Cancelled` (`Appointment.cs` ~632–640) | Allow the move on `Completed` (the visit happened, the date was mistyped); on `Cancelled` lock the field with the reason. Never a silent 200 |
| H2 | Delete a fiche on a cancelled / written-off devis → « Veuillez réessayer » | `DeleteDentalRecordCommand` | **verify** — wave 1 C added `ReleaseDentalRecord` (void plan: pointers only). Walk it once |
| H3 | Add an act to a « Terminé » devis → stays « Terminé »; act unbookable, fiche refused, Créances bills it now | `TreatmentPlan.AddItems` (no status re-derive) · `RestoreItem` | Re-derive status after add/restore — through `StatusFollowsTheWork` / `StatusAfterWork`, never a hand-written condition |
| H4 | Turn a done 1-séance act into 3 séances → refused; workaround is detach + re-save | `TreatmentPlanItem.SetSteps` (refuses cutting a finished step-less act) | Convert in place: séance 1 inherits the act's `DoneDate` + `LinkedDentalRecordId` |
| H5 | Billed fiche: fixing a tooth (36→46) on a flat act → refused, must redo the note | `DentalRecordInvoiceLines` (compares designation text) | Compare act + quantity + price, not the text |
| H6 | « ⋯ » → « Annulé » on a visit with a fiche + paid note: one tap, no confirm, counted as an absence while billed | `appointment-quick-actions.tsx` | Confirm dialog naming the fiche / the note |
| H7 | Delete a fiche → visit stays « Terminé », « Enregistrer la fiche » gone; re-creating loses the booked act + price | `app/patients/[id]/page.tsx` (fiche button gate) | Offer the button whenever the visit has no fiche |
| H8 | Lowering a chairside collection: save disabled, reason shown, no link to the devis | `patient-record-modal.tsx` (⚠️ peer a6's file — see below) | Add the link to the devis workspace |
| H9 | « Traitements en cours » dots wrong when séance 2 is done first; « stalled » from day 15 whatever the protocol | old board 21, 22 · `TreatmentsInProgressReader` / list cells | Read each séance's own date and due date (`nextStepDueFrom`) |
| H10 | « En cours » visit moved to a later day stays « En cours » | `Appointment.Reschedule` | ⚠️ read the A-2 note first (Reschedule *preserves* `Confirmed`/`InProgress` on purpose). Moving to a **different day** resets `InProgress` → `Scheduled`; same day keeps it |
| H11 | An existing visit can't become « suite d'une séance précédente » (create dialog only) | old board 32 · `ContinueSessionDialog` mounted by the create dialog only | Same control in the edit dialog — through `materialiseTreatments`, one materialiser (N26) |

### I — the catalogue (« Mes actes »)

| # | Bug | Evidence | Fix |
|---|---|---|---|
| I1 | Archiving an act is one-way: no « Réactiver », hidden from the list, name blocked by the invisible row | `ProcedureType` · `ProcedureTypeRepository` name check | « Réactiver » + « archivés » filter + name uniqueness on active rows only (copy the dental-acts catalogue) |
| I2 | Renaming an act: other desks' agenda keeps the old name; also skips the 409 check | `UpdateProcedureTypeCommand` | Broadcast `appointments` too; `SetExpectedVersion` before the first save |
| I3 | Retiring a practitioner = hard delete → wipes who earned past money | `UpdateDoctorsCommand` (roster replace deletes absent doctors) | `Doctor.IsActive` (⚠️ **migration**): hide from pickers, keep on history. Run `verify-schema` before/after |
| I4 | Delete-act dialog doesn't mention devis lines; its check loads every visit of the clinic | `procedure-types-table.tsx` · `DeleteProcedureTypeCommand` | Count devis lines in the refusal; query in SQL |

### J — sentences that say the wrong thing

| # | Bug | Evidence | Fix |
|---|---|---|---|
| J1 | Park-one-act dialog: « il reste à annuler dans l'agenda » while the server already cancels | `plan-workspace.tsx` | **verify** — the sentence no longer exists (grep finds nothing); check what replaced it is true |
| J2 | Stop dialog promises an avoir | `plan-workspace.tsx` | **done in wave 3** (« Rendre et arrêter ») — count it |
| J3 | Eleven screens show the BOOKED act, not the done one, unlabelled (« À clôturer » worst) | `GetVisitsToCloseQuery` + 10 others | Label « prévu » / « réalisé »; list the 11 surfaces first (grep the booked-act field, not the screen) |
| J4 | Domain refusals shown as « Erreur… Veuillez réessayer » | `UpdateAppointmentCommand` catch-all | Pass the `InvalidOperationException` message (keep `when (ex is not ConflictException)`) |
| J5 | Ten controls vanish with no reason (devis list menu); « Facturer » missing from the devis menu | old board 34, 35 · `treatment-plans-table.tsx` menu | Keep the control, state the rule (named refusal, M26) |

### Carry-over from wave 3 (found by peer a6's walk)

| # | Bug | Evidence | Fix |
|---|---|---|---|
| C4b | A fiche whose acts are ALL on the devis reopens with « Payé », « Mode », « Total » showing | `markBilledOnPlan` marks one card; `DentalRecordDto` carries one `TreatmentPlanItemId` | DTO carries every linked item id; the modal marks them all. No money moves (server re-imposes 0). a6's `qa/run-act-onto-devis.md` finding 1 |

### Suggested order

1. **Server state rules first**: H1 + H10 (both `Reschedule`), H3, H4, H5 — unit-testable, one file each mostly.
2. **I1–I4** — I3 is the only migration of the whole feature; do it alone, `verify-schema` before and after.
3. **Client**: H6, H7, H11, C4b, J4, J5.
4. **J3 last** — widest (11 surfaces); blast-radius table first.
5. Verify H2 / J1 / J2 in the browser walk rather than re-coding them.

---

## Peer work you must not commit as yours

**clinic-management-a6** built « Ajouter au devis » on the fiche — **uncommitted** when wave 3 closed:
`Features/Patients/FichePlanActAdditions.cs` (+ tests), `Create/UpdateDentalRecordCommand.cs` (their hunks),
`DentalRecordActHandlerTests.cs`, `DentalRecordCorrectionTests.cs`, `patient-record-modal.tsx`,
`record/act-card.tsx`, `record/use-session-acts.ts`. `ListAgents` first — if it has landed, H8 and C4b sit on top
of it; if not, message a6 before touching `patient-record-modal.tsx`. It calls `RespreadScheduleToTotal` (G2's
rule is under it; its tests assert totals, not rows).

Also dirty and NOT ours: `.claude/rules/frontend-web.md`, `.claude/rules/verification.md`,
`.claude/skills/start-clinic/SKILL.md`, `FEATURE-OVERVIEW.md`, `console/tsconfig.json`.

## How a wave is done (what worked in waves 1–3)

1. `ListAgents` + `stack-lease.ps1 status -Session <you>` (full path: `.claude/skills/start-clinic/scripts/`).
   API was last started by clinic-management-8f (pid 29736); web is bf's `next dev`. Restart a peer's tier only
   when its owner is idle, and say so first.
2. Blast-radius table first — append a wave-4 section to `blast-radius.md`.
3. Fix. Long edits: a Python script **written to the scratchpad with Write** — bash heredocs truncate long bodies.
4. Tests in `DevisFicheRdvFlexibilityWave4Tests.cs`. Gate: `BaseOutputPath=<temp> dotnet test … -c Release`
   (unfiltered; **4823** green after wave 3) + `cd web && npx tsc --noEmit && npm run check:responsive`.
   ⚠️ **`npm run build` in `web/` collides with the live `next dev`** — build a copy: robocopy `web` to the
   scratchpad without `node_modules`/`.next`, junction `node_modules`, add `turbopack: { root: "C:/Users" }` +
   `outputFileTracingRoot` to the COPY's `next.config.ts`, then `npm run build` there.
5. `reconcile-money` before/after when money moves: run HEAD from a `git worktree add --detach` in the scratchpad
   and the tree from `api/ClinicManagement.API`, both with `-p:BaseOutputPath=<temp>`; diff the `[  ok ]` lines and
   the monthly baseline. A delta can be a **peer's live writes** between the two runs — check `InstallmentPayments`
   by `CreatedAt` before blaming the change.
6. Browser: copy `qa/arrange-3.mjs` / `qa/walk-3.mjs` to `-4`. Tooling in the scratchpad: `node_modules`
   (playwright-core, copy from another session's scratchpad) + a `totp.mjs` that **exports** `totp(secret)` (some
   copies are CLIs — `qa/` scripts need the export). Headless `channel: 'chrome'` in its own context needs no
   browser lease, but tell peers before a walk. Write `qa/run-4.md`; add wave-4 rows to `qa/plan.md`.
7. Commit only the wave's files (`git diff HEAD --numstat` per file). For a file with a peer's hunks, stage a
   blob built on HEAD with only yours (`git hash-object -w` + `git update-index --cacheinfo`), then prove the
   staged set compiles alone: `git worktree add --detach <scratch>/idx HEAD`, `git apply` the cached diff, test.
   ⚠️ In a worktree the `RepositoryRoot()`/`PhoneRuleCorpus` scan tests fail (a worktree's `.git` is a file) —
   environmental, not a regression.

## Traps hit so far

- **A TOTP code is single-use per 30 s window.** An arrange login followed at once by a second login gets
  « Identifiants invalides. » and every API read in the walk comes back null. Mint each extra token in a fresh window.
- **Server sentences with `{x:0.000}` print « 300.000 DT »** (invariant culture), read as three hundred thousand.
  Pin fr-FR for any figure a user reads (`PlanRefund.Dt`).
- New handler dependency → **optional** constructor parameter.
- Moq returns **null** for an unstubbed `Task<IReadOnlyList<T>>` → `?? Array.Empty<T>()`.
- A new command in `Features/TreatmentPlans/Commands` needs `SetExpectedVersion` or a `PlanVersionCoverageTests.Exempt`
  reason; a new controller action needs a row in `TreatmentPlansControllerAuthorizationTests`.
- A `run()`-style helper returning a value breaks `onConfirm: () => run(...)` typed `Promise<void>` — use
  `async () => { await run(...) }`.
- The fiche modal's save is « Confirmer la séance » on a new fiche and « Enregistrer » on a reopened one.
- Workspace menu: `aria-label="Autres actions sur ce devis"`; remise button `Accorder une remise sur « … »`,
  its input `#plan-item-discount`; the rendu question is an `alertdialog` titled « Rendre au patient ? ».
- Deep links: `/appointments?appointmentId=<id>` · `/patients/<id>?editRecord=<ficheId>` ·
  `?addRecord=1&appointmentId=<id>`. Enum ints: appointment Completed 4, Cancelled 5 · plan Completed 3,
  Cancelled 4, Stopped 5, WrittenOff 6 · item Planned 0, Done 1, Withdrawn 3.
- DB for read-only SQL: `docker exec clinic-postgres psql -U clinic_user -d clinic_management`.
