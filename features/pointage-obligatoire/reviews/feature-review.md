# Feature Review: pointage (borne + obligatoire) — ease of use & guidance

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-09
**Challenged Date:** 2026-11-09
**Parent Branch:** main
**Merge Base:** 7000b0e0
**Scope:** the whole pointage feature as it stands in the worktree — commit `13302491` (borne) + the uncommitted
part 3 (lock, sign-out prompt, forgotten departure) + DEV-12 (doctor picks the borne device). Congés excluded.
**Review method:** the owner asked « is it easy to use by both doctor and secretary, does the app guide them, can
they miss how to do it properly? » — so the agents were re-aimed at that question instead of the four default code
mandates (the same code already went through two full code reviews: 57 + 41 findings applied, kept in
`feature-review-1-code.md` and `../pointage-borne/reviews/`). Four agents, read-only, source + QA screenshots:
**Doctor journey · Secretary journey · Device & UX · Setup states & dead-ends.** Duplicates merged; one finding from
the live demo added. Every finding then challenged against the full source and both specs.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 37 |
| Confirmed | 27 |
| Confirmed (adjusted) | 9 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 36 |

Dismissed: original Finding 36 (« show the schéma line at the borne ») — hiding it is a deliberate security fix
(code review `pointage-borne` Finding 56: shoulder-surfing at a public desk). Do not revert.

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 122
- **Anchor:** `ClockStatusProvider` (`covering`)
- **Comment:** **A secretary who forgets her schéma is locked out of the whole app.** Once she has a schéma, the
  lock covers « Mon profil » (only `hasSchema === false` uncovers it — DEV-4 covered the « no schéma yet » case only),
  so « Changer de schéma » is unreachable. At the borne, 5 wrong tries block her 15 min; `ClockStatusDto` carries no
  `lockedUntil`, so the lock still shows a live pad and « Réessayer » (`clock-in-gate.tsx:228`) for punches the server
  will refuse. She often opens the cabinet before the doctor arrives. The server already lets her fix it:
  `PUT /team/me/schema` is lock-exempt (`TeamController.cs:256`) and clears the block. Fix: a « Schéma oublié ? »
  button on the lock that opens the draw-twice dialog in place; while blocked, « Bloqué jusqu'à 08:47 » instead of
  the pad.
- **Challenge note:** ⚠️ The lock can be showing on a shared borne PC under someone else's session (Finding 4). A
  redraw from the lock must therefore ask that account's password first, or a colleague could replace another
  person's schéma. The earlier code review left « prove the current schéma before a change » untaken (`pointage-borne`
  Finding 56) — this fix is where it becomes necessary.

### Finding 2
- **Severity:** Critical
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Repositories/AttendanceTerminalRepository.cs
- **Line:** 40
- **Anchor:** `HasActiveAsync`
- **Comment:** **A dead borne locks everyone and nobody sees why.** Reception PC reinstalled, replaced, or its cookies
  cleared: the row stays active (`RevokedAt == null && SecretHash != null`), so every tracked secretary is locked and
  sent to « Accueil », while that PC now says « Cet appareil n'est pas une borne du cabinet ». The doctor still sees
  « Utilisée le 3 nov. ». Re-adding it from that PC sends no `replacingTerminalId` (no cookie → `isBorne` false,
  `attendance-settings-card.tsx:112`), so a second « Accueil » appears and the dead one stays. Fix: flag a borne red
  (settings + « Équipe aujourd'hui ») when an open day passes opening + 30 min with no punch on it; in « Ajouter une
  borne », offer « Remplacer Accueil » (re-key the same row).

### Finding 3
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 94
- **Anchor:** `RegisterAttendanceTerminalCommandHandler` (borne added) — found in the live demo
- **Comment:** **Nothing tells a secretary to create her schéma.** When the doctor adds a borne, no notification and
  no e-mail reach the secretaries (`NotificationCategory` has no such member). Outside opening hours, or with the lock
  off, she only finds out at the borne, the day she tries to point. Fix: when a borne goes live, a bell row + push to
  every tracked person without a schéma, « Créez votre schéma de pointage », opening the draw-twice dialog (see
  Finding 14).
- **Challenge note:** Critical → Major — with the lock on (the default) she is told at opening by the lock itself
  (« Créer mon schéma »), so she is not stuck; the gap is everywhere else.

### Finding 4
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 207
- **Anchor:** `ClockInGate` (borne branch, `PunchFlow showHeading={false}`)
- **Comment:** **Shared reception PC: the lock's pad belongs to whoever is signed in, and her name is not shown.**
  The reception PC is the borne; A's session stayed open; B arrives first and draws HER schéma on A's pad
  (`userId={status.userId}`, heading hidden). Each try counts against A — 5 lock A out for 15 min. The lock offers only
  « Se déconnecter » and « Demander un congé »; the header « Pointage » link is under the overlay. Fix: show A's name
  above the pad and a « Ce n'est pas moi » button that opens the tiles (`/pointage`).
- **Challenge note:** Critical → Major — B is not stuck: « Se déconnecter » → the borne's sign-in page offers
  « Pointage ». The harm is A's burned tries and B's confusion.

### Finding 5
- **Severity:** Major
- **Category:** UX — Doctor
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 341
- **Anchor:** `AddBorneButton` — device list / `DeviceOption` (406); `GetBorneDevicesQuery.cs:58`
- **Comment:** **The doctor can easily pick the wrong device as the borne.** The list is one row per live session of
  every account, led by a person's name, phones included (the query only drops demo / PC-de-secours labels); devices
  that signed in before DEV-12 read « Appareil sans nom »; at 320–390 px the « actif il y a … » tail is `truncate`d.
  A secretary's phone as borne = she can point from home, with no warning. « Cet appareil » is still the first option.
  Fix: lead each row with the device type (PC / tablette / téléphone), one row per device (newest), leave tracked
  people's phones out (and refuse them server-side), wrap instead of truncate, « Cet appareil (Dr Salma) » last.
- **Challenge note:** Critical → Major — the device label is visible on a desk screen; the risk is a wrong but
  deliberate pick, recoverable by retiring the borne.

### Finding 6
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 51
- **Anchor:** `AttendanceLockRules.IsLocked`; `team-modules-card.tsx:85`
- **Comment:** **The lock starts the minute a borne goes live (or pointage is switched on), mid-day.** Seen in the
  demo: every secretary without a line today is locked in the middle of her work, including those with no schéma; her
  arrival is then stamped 14:05 although she came at 08:00. The doctor gets no warning. Fix: the lock starts at the
  next opening after the first live borne / the switch; the switch shows « Blocage actif demain à 08:00 ».
- **Challenge note:** Critical → Major — nobody is stuck (a punch or a schéma lifts it within a minute), but the first
  day's sheet is wrong and the first impression is an interruption.

### Finding 7
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 89
- **Anchor:** `AttendanceSettingsCard`; `web/lib/guides/index.ts`
- **Comment:** **No guide and no « setup complete ».** After switching pointage on, the card shows « Aucune borne pour
  l'instant. » and a people list — no order (horaires → borne → schémas → blocage), no sign it isn't working yet, no
  sign it is ready. Pointage is in no « Premiers pas » step and no « Me guider » tour (`web/lib/guides/` has
  appointment, dental-record, letterhead, next-step, patients, secretary — none mention it). Fix: a numbered checklist
  at the top of the card, each row a status with its action — « Horaires ✓ », « Borne · Accueil (PC d'Ines) ✓ / en
  attente / ✗ », « Schémas 1 sur 2 », « Blocage » — the next undone step gets the filled button; the lock switch stays
  disabled until the first three are green; plus a « Me guider » pointage tour on `/settings`.

### Finding 8
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** web/components/team-modules-card.tsx
- **Line:** 85
- **Anchor:** `TeamModulesCard` (lock switch, `lockOn = … !== false`, line 35)
- **Comment:** **The lock switch reads « on » while it can never lock.** It defaults on, but the lock needs opening
  hours set (`IsInsideOpeningHours` is false with none — no departure reminder either) AND a live borne. The doctor
  sees a green switch and the toast « L'app attend désormais l'arrivée pointée. » — and nothing happens. Fix: a
  server-computed reason shown as a status chip beside the switch: « Inactif — horaires non définis » (link to
  Horaires) / « Inactif — aucune borne active ».

### Finding 9
- **Severity:** Major
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/clinic-settings.tsx
- **Line:** 1567
- **Anchor:** `ClinicSettings` — `TeamModulesCard` placement
- **Comment:** **Hard to find.** The « Équipe » card is the last thing in Paramètres, below « Besoin d'aide ? »;
  « Utilisateurs » shows no Présences/Congés tab while both modules are off (`app/users/page.tsx:108`); « Choisissez
  qui pointe dans Paramètres → Équipe. » (`team-today-list.tsx:19`, `attendance-panel.tsx:442`) is plain text, not a
  link. Fix: move the card up beside Horaires with an anchor; for admins always show the tabs, with the switch itself
  in the empty state; make both sentences links.

### Finding 10
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 293
- **Anchor:** `AddBorneButton.register` toast; `TerminalRow` (172–175); `PointageCommands.cs:94`
- **Comment:** **A borne « en attente » is a blind spot.** The toast says « Borne « Accueil » envoyée à l'appareil
  de Ines » — not true yet (the device picks it up later, signed in, app open; owner rule: never state the next step
  as done). If that session ends first, the borne never arrives: only the settings row turns red, its fix is two steps
  (« retirez-la et recommencez ») with only a red trash button. The doctor is never told it was installed; the
  secretary gets a 4-second toast. Fix: toast « en attente du PC d'Ines »; row « En attente depuis 14:02 »; admin bell
  on install (« Borne Accueil installée ») and on failure with « Choisir un autre appareil » (picker prefilled,
  replaces in one save); a bell row for the secretary.

### Finding 11
- **Severity:** Major
- **Category:** UX — Doctor / Secretary
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/TeamTodayRules.cs
- **Line:** 22
- **Anchor:** `TeamTodayRules.PersonToday`; `web/lib/api/pointage.ts:258` (`pointageStateLabel`)
- **Comment:** **« Pas encore arrivée » also means « already left ».** `State` is `in` only while a line is open, else
  `out`. After her departure (or a lunch punch) her tile, « Aujourd'hui » and the dashboard read « Pas encore arrivée »
  — every evening the doctor sees the whole team as never having come, and the secretary thinks her punch was lost.
  Fix: a « left » state, « Partie à 17:02 »; on a closed day, one line « Cabinet fermé ».

### Finding 12
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** web/components/dashboard/team-today-block.tsx
- **Line:** 52
- **Anchor:** `TeamTodayBlock` / `TeamTodayList`; `TeamTodayPersonDto` (`AttendanceDtos.cs:212`)
- **Comment:** **« Équipe aujourd'hui » shows setup problems as lateness, and offers no action.** The DTO has no
  schéma, schéma-block or app-lock fact, so with no live borne, no schéma, a blocked schéma or a locked app every person
  reads « Pas encore arrivée ». When she calls « la borne ne marche pas », the doctor's fix (« Ajouter un pointage »)
  is three screens away. Fix: per-person states (« Schéma à créer », « Schéma bloqué ») with inline « Pointer son
  arrivée » (prefilled, now) / « Débloquer »; one setup row above the list when the borne is missing, waiting or dead.

### Finding 13
- **Severity:** Major
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 392
- **Anchor:** `PunchCommandHandler.WrongSchemaAsync`
- **Comment:** **5 wrong schémas: the doctor sees nothing and can only delete the schéma.** A refusal broadcasts
  nothing and rings no bell; the only remedy is « Réinitialiser le schéma » (she must redraw twice) although
  `User.ClearSchemaFailures` exists. Fix: broadcast `team` on a failure; admin bell « Schéma de Ines bloqué » with
  « Débloquer » (clears the tries, keeps the schéma); send `lockedUntil` in clock-status (see Finding 1).

### Finding 14
- **Severity:** Major
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 203
- **Anchor:** `ClockInGate` « Créer mon schéma »; `schema-setup-card.tsx:57`
- **Comment:** **Creating her first schéma is a detour that can dead-end.** The lock sends her to « Mon profil », where
  she must find « Créer mon schéma » again; that card renders only when the « Mes présences » month read succeeded
  (`mine?.tracked`), so if that read fails the page she was sent to has no schéma button while everything else is
  locked. Fix: draw it twice inside the lock itself (the endpoint is exempt); on a borne the arrival pad follows in
  place. Decide the card from `useClockStatus().hasSchema`, not the month read.

### Finding 15
- **Severity:** Major
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/sign-out-prompt.tsx
- **Line:** 72
- **Anchor:** `SignOutPrompt`
- **Comment:** **On a normal PC, the sign-out prompt's main button skips the punch.** Title « Pointer votre départ ? »,
  no « oui » button; off a borne the primary (`variant="default"`) is « Se déconnecter sans pointer », and the borne
  name trails the description. Fix: off a borne, the title says what to do — « Pointez votre départ à la borne
  « Accueil » » — with « Annuler » and « Se déconnecter »; keep « sans pointer » only on a borne, where the pad is the
  « oui ».

### Finding 16
- **Severity:** Major
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/borne-link.tsx
- **Line:** 53
- **Anchor:** `BorneLink` (header variant → `<Link href="/pointage">`); `app/pointage/page.tsx:116`
- **Comment:** **Pointing on a working reception PC throws the user off her page.** The borne is usually the PC
  someone works on (DEV-12); a colleague pressing « Pointage » takes that tab to the kiosk (open patient file or form
  gone), and the only way back reads « Se connecter » to someone already signed in. Fix: on a signed-in device, open
  the tiles + pad in a sheet over the page that closes itself after the confirmation.

### Finding 17
- **Severity:** Minor
- **Category:** Setup & Guidance
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 481
- **Anchor:** `PersonRow` (tracked switch); `AttendanceLockEvaluator.IsLockable` (line 45)
- **Comment:** Ticking a non-admin dentist in « Personnes qui pointent » makes her lockable: her fiches and devis are
  refused (423) during opening hours until she points, and she has no schéma. No warning, no toast on toggle. Fix: show
  « App bloquée avant pointage » on the row and confirm the toggle for a non-secretary (or exempt `doctor` from the
  lock and only record her).
- **Challenge note:** Major → Minor — spec-conformant (`pointage-borne` AC-4 lets the admin track any account; the
  lock applies to tracked accounts; only admins are exempt, deliberately). The gap is the missing warning, not the
  rule.

### Finding 18
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 56
- **Anchor:** `AttendanceLockRules.IsLocked` (`!facts.HasLineToday`)
- **Comment:** **Lunch break: after she points out, nothing brings her back.** Any line today, even closed, lifts the
  lock; the sign-out prompt, the reminder and the next-morning step all look only at open lines. She can work all
  afternoon unpointed and the day just looks short. Proposed: at « Départ » before closing, the borne asks « Pause » or
  « Fin de journée »; after « Pause », the lock returns as « Pointez votre retour ».
- **Challenge note:** Major → Minor — `pointage-obligatoire` AC-4 explicitly says « no lock after her departure ». Its
  reason (checking tomorrow's agenda from home) holds for end of day, not for lunch. **Owner's call — do not change
  without her decision.**

### Finding 19
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/dashboard-header.tsx
- **Line:** 65
- **Anchor:** `DashboardHeader` (`requestLogout`)
- **Comment:** Once the lock lifts, nothing in the app shows her pointage state; the sign-out prompt only fires if she
  signs out, and most secretaries close the browser. Fix: a header chip for a tracked account, « Présente depuis
  08:02 » / « Partie à 12:05 »; on a borne it opens the departure pad, elsewhere it names the borne.
- **Challenge note:** Major → Minor — the spec's design is three nets (reminder, sign-out prompt, next-morning
  declaration), all built; the chip is an extra net, not a missing one.

### Finding 20
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/app/pointage/page.tsx
- **Line:** 254
- **Anchor:** `PunchPanel` (Escape listener + `IDLE_MS = 30_000`)
- **Comment:** On the borne, Escape (anywhere) and 30 s without input send the « départ d'hier » step back to the
  tiles with no confirm: the typed hour and the drawn schéma are lost. Fix: PunchFlow reports its stage; in the
  declaration step ignore Escape and use a longer idle; ignore Escape while a punch is in flight.
- **Challenge note:** Major → Minor — typing re-arms the idle timer; the loss is one hour field plus a redraw.

### Finding 21
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 151
- **Anchor:** `SchemaDialog` → `SchemaPad`; `clock-in-gate.tsx:207`
- **Comment:** The pad is not capped by height. Landscape phone (~375 px tall): the bottom sheet (90dvh) shows ~175 of
  the 288 px pad and can't scroll mid-stroke — a schéma using top and bottom rows can't be drawn. At 320×568 with the
  phone as borne, the lock is ~670 px and « Demander un congé » is below the fold. Fix: `max-w-[min(18rem,40dvh)]` as
  the sign-out prompt does, in both places.
- **Challenge note:** Major → Minor — landscape phone and phone-as-borne are edge devices; rotating fixes it.

### Finding 22
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 199
- **Anchor:** `TerminalRow` « Retirer » AlertDialog
- **Comment:** « Cet appareil ne pourra plus pointer » reads as the doctor's own laptop. Removing the last live borne
  also silently switches the lock off. Fix: name the device (« Le PC d'Ines Gharbi ne pourra plus pointer »); when it
  is the last live borne and the lock is on, say « L'app ne sera plus bloquée ».

### Finding 23
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-dialogs.tsx
- **Line:** 209
- **Anchor:** `RecordAttendanceDialog` (`useState("08:00")`, reset line 224)
- **Comment:** « Ajouter un pointage » opens with no person, today, and an invented 08:00 that can be saved unnoticed.
  Fix: the current time (or empty), never 08:00; open prefilled from the row it was started from.

### Finding 24
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 329
- **Anchor:** `DayCell`
- **Comment:** A past open day with no pointage and no congé renders an empty cell, identical to future days — absences
  can't be scanned. Fix: « Aucun pointage » + inline « Ajouter » (prefilled) on past open days; future days lighter.

### Finding 25
- **Severity:** Minor
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 254
- **Anchor:** `AddBorneButton` (`DEFAULT_BORNE_NAME = "Accueil"`, line 52)
- **Comment:** Every new borne defaults to « Accueil » and the server accepts duplicates → « Bornes : Accueil,
  Accueil ». Fix: default to the next free name; refuse a name already in use (or offer « Remplacer », Finding 2).

### Finding 26
- **Severity:** Minor
- **Category:** Setup & Guidance
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/BackgroundJobs/AttendanceDepartureReminderJob.cs
- **Line:** 61
- **Anchor:** `RemindMissingDepartures`; `clock-in-gate.tsx:152` (`bornesPhrase`)
- **Comment:** Retiring the last borne while someone is « présente » leaves her unable to point out; her prompt says
  « Aucune borne n'est encore installée » (false), and the job still reminds her. Fix: refuse retiring the last borne
  while someone is present (name her); skip the reminder with no live borne; « Aucune borne active : demandez à
  l'administrateur. »

### Finding 27
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/dashboard-header.tsx
- **Line:** 258
- **Anchor:** `handleNotificationClick` (`targetKind === "Attendance"` → `/mon-profil`)
- **Comment:** « Départ non pointé » opens « Mon profil », where she can do nothing, and doesn't name the borne. Fix:
  name the borne in the message; open the pad when this device is the borne.

### Finding 28
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/punch-flow.tsx
- **Line:** 119
- **Anchor:** `PunchFlow.send` (declare stage re-entered)
- **Comment:** With two days left open, « Valider » clears the form and shows the same screen with only the date
  changed — looks like the press failed. Fix: list the days already settled above the form.

### Finding 29
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 295
- **Anchor:** `EntryLine` « Corrigé »
- **Comment:** In « Mes présences » a corrected line hides what the borne recorded and who corrected it, though the DTO
  carries both (`punchedArrival`, `correctedByName`). Fix: in her own view, « Borne : 08:02 – 17:46 · corrigé par Dr
  Salma ».

### Finding 30
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/lib/api/pointage.ts
- **Line:** 258
- **Anchor:** `pointageStateLabel` (+ `pointage/page.tsx:190`, `sign-out-prompt.tsx:74`)
- **Comment:** Wording mixes genders (« Présente », « Pas encore arrivée » vs « Bloqué ») though any account can be
  tracked, and « Créer mon schéma » / « Tracez votre schéma » / « Schéma à créer dans Mon profil » differ. Fix: one form
  for the module, « votre » throughout.

### Finding 31
- **Severity:** Minor
- **Category:** UX — Secretary
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 73
- **Anchor:** `SchemaSetupCard` « Mes présences »
- **Comment:** Her own record is only under avatar → « Mon profil » (no entry in `lib/nav.ts`), a page about her
  cachet. Fix: a rail entry « Mes présences » for tracked accounts.

### Finding 32
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/users/[userId]/page.tsx
- **Line:** 95
- **Anchor:** `StaffFilePage` → `body()`
- **Comment:** With congés on, a failed congés-file read returns « La fiche n'a pas pu être chargée » and the
  presences section (its own read) is never rendered. Fix: render `presences` outside the congés loading/failure
  branches.

### Finding 33
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 78
- **Anchor:** `AttendancePanel.load` (« Aujourd'hui »)
- **Comment:** `pointageApi.today().then(setToday, () => undefined)` — a failed « Aujourd'hui » read just makes the
  section vanish. Fix: a compact `LoadFailureNotice` with « Réessayer » in its place.

### Finding 34
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/team-today-list.tsx
- **Line:** 34
- **Anchor:** `TeamTodayList` (name span)
- **Comment:** At 320 px names truncate (« QA Secrétair… ») with no way to read them. Fix: `break-words` instead of
  `truncate` (the row already wraps).

### Finding 35
- **Severity:** Suggestion
- **Category:** UX — Doctor
- **Verdict:** Confirmed
- **File:** web/components/user-management.tsx
- **Line:** 424
- **Anchor:** user row → staff file (`u.role === "secretary"`)
- **Comment:** The staff file shows Présences but not her pointage setup (pointe / schéma / « Réinitialiser »), and is
  linked only for `secretary`. Fix: put them in the file's Présences header; link the file for every tracked person;
  sheet column headers link to it.

### Finding 36
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 146
- **Anchor:** month navigator
- **Comment:** A second month stepper beside `staff-leave-month-calendar.tsx`'s, with different layout and touch size,
  on adjacent tabs. Fix: one shared `MonthStepper`.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 2 |
| Major | 14 |
| Minor | 18 |
| Suggestion | 2 |
| **Total** | 36 |
