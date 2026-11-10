# Feature Review: pointage-borne

**Status:** COMPLETE
**Challenged:** Yes
**Challenged Date:** 2026-11-05
**Date:** 2026-11-05
**Parent Branch:** feature/conges-secretaires → HEAD b1a35839 (part 1, already reviewed and committed)
**Merge Base:** b1a35839 (origin/main 7000b0e0 is not ahead — no stale base)
**Files Reviewed:** 69 files (+6828 / −48), working tree vs HEAD, assembled with `git add -N` (new files as all-`+` hunks).
Excluded: migration `.Designer.cs`, `ApplicationDbContextModelSnapshot.cs` (read directly by the migration agent), `CLAUDE.md` docs, `features/**`.
**Review method:** 6 parallel agents — Code Quality · Error Handling (MediatR + `Result<T>`, not ROP) · Business Logic ·
Breaking Changes + migration · Device & UX (rendering files in the diff) · Security (anonymous borne, cookie, schéma).
Duplicates merged: the borne-cookie path (3 agents), malformed pattern counted as a try (2), punch-panel timers (3),
hidden failed month read (2), Mon profil card hidden on a failed read (2), leave predicate (2), user-management flag (2).
Not reported (deliberate): DEV-1/2/3, version 0 on « Personnes qui pointent », part 3 scope.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 57 |
| Confirmed | 49 |
| Confirmed (adjusted) | 8 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 57 |

Every finding was read against the working-tree source; line numbers re-anchored where they had drifted.
Cross-references in comments (« Finding 2 », « Finding 5 ») use the ORIGINAL numbering: original 2 is now 2
(borne cookie), original 5 is now 5 (« Ajouter un pointage » overlap).

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-dialogs.tsx
- **Line:** 202
- **Anchor:** `RecordAttendanceDialog` (reset effect)
- **Comment:** The reset effect depends on `people`, which `AttendancePanel` passes as `sheet?.people.filter(p => p.isActive) ?? []` (attendance-panel.tsx:225) — a new array every render. Every `team` realtime event (each punch at the borne, each congé decision, another admin's correction) reloads the panel and re-runs the reset, so « Ajouter un pointage » silently loses Personne, Jour, hours and the typed Motif. On a morning when the team clocks in, the form cannot be filled. Fix: reset only on the `open` false→true transition (previous `open` in a ref) or `useMemo` the filtered list.


### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/TeamController.cs
- **Line:** 188
- **Anchor:** `TeamController.RegisterTerminal`
- **Comment:** `ReplacingSecret = Request.Cookies[PointageController.BorneCookie]` is always null: the cookie is written with `Path = "/api/pointage"` (PointageController.cs:45), so the browser never sends it to `POST /api/team/terminals`. The « retire this device's previous borne » branch (PointageCommands.cs:60) is dead; unit tests set `ReplacingSecret` by hand and cannot see it. Re-registering a device (« Renommer cet appareil ») leaves the old row active forever with a secret nobody holds — two « Accueil » bornes, both reading « cet appareil » (name match), and a copied cookie survives the admin's attempt to replace it. Fix: register under `/api/pointage/terminals` (AdminOnly) or widen the cookie path; add a controller test proving the cookie reaches the action; relabel « Renommer » to what it does.


### Finding 3
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 192
- **Anchor:** `PunchCommandHandler.Handle`
- **Comment:** Parallel guesses get past the 5-try lock: N concurrent wrong guesses race on the user row's `xmin`, one save wins and N−1 return a generic 409, so the counter rises by 1. A correct guess normally hides in the same 409s — except on a full-day congé, where `on_leave` is returned after the schéma is verified and saves nothing, so the right guess answers `on_leave` while the others answer a conflict. Fix: check every refusal that does not depend on the schéma (full-day congé included — it is already public on the tile) before `VerifyPassword`, and make the attempt count atomic (pre-charge with a conditional UPDATE … RETURNING and refund on success, or lock the row for the verify-and-save).


### Finding 4
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 170
- **Anchor:** `PunchCommandHandler.Handle`
- **Comment:** A pattern that is not a schéma (< 4 dots, repeated or out-of-range dot) is counted as a wrong schéma and spends a try. The borne submits any non-empty stroke (`SchemaPad.finish`, `PunchPanel.submit` — web/app/pointage/page.tsx:223), and on a 10" tablet a double-tap on a tile lands on a dot of the pad that mounts underneath. Five stray taps lock her for 15 min (AC-7 counts wrong *schémas*, AC-5 defines one as 4–9 distinct dots). Fix: refuse a malformed pattern with the `SchemaRefusalFor` sentence without touching `SchemaFailedAttempts`; on the borne, never submit fewer than 4 dots (show « Reliez au moins 4 points » locally) and ignore pointerdowns for ~300 ms after the panel mounts.


### Finding 5
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/AttendanceAdminCommands.cs
- **Line:** 277
- **Anchor:** `RecordAttendanceEntryCommandHandler.Handle`
- **Comment:** « Ajouter un pointage » never looks at the person's existing lines. Adding 08:00–17:00 on a day the borne already recorded 08:02–12:00 double-counts ~13 h in the totals and the export; adding a line with no departure today, while she is « Présente », creates a second open line — the borne closes only the newest, the other turns into a red « Départ non pointé » tomorrow. Fix: refuse an overlap with an existing line of that person and day, and never create a second open line on a day that has one.


### Finding 6
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 173
- **Anchor:** `AttendancePanel` (failed-read branch)
- **Comment:** The failure notice only shows when no sheet was ever loaded (`failed && !sheet`). Offline, « Mois précédent » moves `month` to October, the read fails, November stays on screen at full opacity with its own label and no error — then the arrows step from a month not displayed and « Exporter » downloads October while November is shown. Fix: show `LoadFailureNotice` whenever `failed`, show the stale sheet only when it matches `month`, and disable « Exporter » while the read has failed.


### Finding 7
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 40
- **Anchor:** `SchemaSetupCard.load`
- **Comment:** A failed `mine()` read sets `mine` to null and the component returns null: the « Schéma de pointage » card and « Mes présences » vanish with no message and no retry — the card the borne sends her to (« Schéma à créer dans Mon profil »). A failed realtime refetch also unmounts an open `SchemaDialog` mid-drawing. Fix: keep the last good `mine` on a failed refetch, render `LoadFailureNotice` with « Réessayer » on a failed first load, mount `SchemaDialog` outside the conditional.


### Finding 8
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 76
- **Anchor:** `PointagePage`
- **Comment:** AC-2 has the admin register the borne while signed in on it, and « Ouvrir » then lands on /pointage with that session live. `/pointage` is public but not in `HIDDEN_PATHS` (lib/nav.ts) and the inactivity effect (lib/auth/session.tsx:308) has no public-route guard: after 30 min the borne is moved to /login; with « Rester connecté » an opaque « Session en pause » cover hides the tiles every morning until someone resumes the admin's session at reception; the Windows app asks for her Windows Hello; staff-chat notices render over the tiles; « Se connecter » forwards into her session. Fix: add `/pointage` to `HIDDEN_PATHS`, guard the idle effect on public routes, and sign the device out once it becomes a borne (or make the borne's « Se connecter » sign out first).


### Finding 9
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 277
- **Anchor:** `EntryLine` (« Corrigé » chip)
- **Comment:** The correction motif is only in `title` + `sr-only` text. On a tablet (coarse pointer, no hover) a sighted admin cannot read why a line was changed, and the secretary in « Mes présences » cannot either; the « Corriger » dialog does not show the current motif. AC-9/10 make the motif part of the record. Fix: show it on the line (a muted second line) or make « Corrigé » a tappable popover, and show the current motif in the dialog's borne detail.


### Finding 10
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 182
- **Anchor:** `PunchCommandHandler.Handle`
- **Comment:** A punch whose answer is lost is replayed as the opposite action. The write-key protection does nothing on this route (`IdempotencyMiddleware.Applies` needs a Clinic scope; the borne has none). If an arrival commits but the response is lost (20 s deadline, Wi-Fi drop), the panel shows the error under « Arrivée »; she draws again and the server closes the line a minute after it opened, and her real departure later opens a line that stays « Départ non pointé ». A stale tile (punch on another borne, 60 s refresh) gives the same mismatch silently. Fix: send the action the panel announced (`expected`) and refuse with a code (e.g. `punch_state_changed`, 409) when it no longer matches; the borne goes back to the tiles and reloads.
- **Challenge note:** Severity lowered Major → Minor — an edge case: with one borne the tiles reload after every punch, the confirmation names the action actually recorded, and « Corriger » repairs the line.

### Finding 11
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 241
- **Anchor:** `PunchCommandHandler.SubscriptionRefusalAsync`
- **Comment:** Copies `SubscriptionGateMiddleware`'s status → (error, code) switch and drops its `DayToJudge` rule (7 days of grace on a PC de secours holding a cut) by reading `ClinicClock.ClinicToday()` directly. During a cut every other write passes and the borne refuses — while `ClinicRelayScope` now carries `AttendanceTerminal` precisely so the borne works there. Fix: one Application-level owner of « the refusal for this entitlement today » (incl. the day-to-judge rule over `IRelayLocalStatus`), called by both the middleware and this handler.
- **Challenge note:** Severity lowered Major → Minor — the cut scenario is spec § Out of Scope (« A borne that keeps working during an internet cut on the PC de secours »), so the divergence is not a delivered defect; what remains is the duplicated refusal switch.

### Finding 12
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 267
- **Anchor:** `EntryLine` (hours button)
- **Comment:** On a closed line the only way to « Corriger » is the hours text itself, styled as plain text with `hover:underline` only. On a coarse pointer (tablet portrait cards, iPad landscape table at 1180 px) nothing signals that « 08:02 – 17:05 » is tappable, so correcting a wrong arrival on a complete line is undiscoverable. Fix: a visible cue at rest (dotted underline + pencil) or the same visible « Corriger » button the missing-departure line has (`coarse:h-11`).
- **Challenge note:** Severity lowered Major → Minor — spec § Device Behaviour requires a visible « Corriger » button on a red line only; on a closed line the hours are a tappable 44 px button, so this is discoverability, not a missing capability.

### Finding 13
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/AttendanceAdminCommands.cs
- **Line:** 215
- **Anchor:** `CorrectAttendanceEntryCommandHandler.Handle`
- **Comment:** « Corriger » checks hours only against its own line: clearing the departure of today's closed morning line while the afternoon line is open leaves two open lines; moving hours into a sibling line double-counts the minutes. Fix: refuse a correction that leaves two open lines on a day or overlaps another line of the same person and day (same rule as Finding 5).


### Finding 14
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/AttendanceEntry.cs
- **Line:** 126
- **Anchor:** `AttendanceEntry.Correct`
- **Comment:** `DepartureSource` is decided by `departureUtc == PunchedDepartureUtc`, but the borne stores full precision (16:01:37.4) and the form round-trips « HH:mm » (16:01:00). Any correction of a borne-closed line — even one fixing only the arrival — marks the departure `CorrectedByAdmin` and truncates it. Fix: compare at minute precision, or keep `DepartureUtc`/`DepartureSource` when the submitted departure equals the stored one formatted « HH:mm ».


### Finding 15
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 144
- **Anchor:** `stateLine`
- **Comment:** AC-7 says a locked tile shows « Réessayez dans 14 min »; the tile shows a fixed « Bloqué : trop d'essais » and is disabled, so the countdown is never visible after the 4 s refusal, and the tile stays disabled after the lock ends until the 60 s refresh. Fix: compute the minutes from `lockedUntil` and re-enable / reload when it passes.


### Finding 16
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 49
- **Anchor:** `SchemaSetupCard`
- **Comment:** « Mes présences » renders only when `mine.tracked`, so an account unticked mid-month loses read access to her own lines while the admin's sheet still shows her column; the server serves them regardless. AC-11 is about reading her own lines. Fix: hide only the schéma half when untracked; keep « Mes présences » while the module is on (or while the month holds her lines).


### Finding 17
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 235
- **Anchor:** `PunchPanel.submit`
- **Comment:** `window.setTimeout(onBack, 4000/3000)` after `schema_locked` / `terminal_unknown` is never stored or cleared, and a late `onDone` still runs after « Retour »/Échap/idle closed the panel (« Retour » is not disabled while busy). Person A locked → « Retour » → person B starts drawing → A's timer or late confirmation hijacks B's panel. Fix: keep timer ids in a ref and clear them on unmount; ignore `onDone` after unmount; name the delays beside `CONFIRMATION_MS`.


### Finding 18
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 233
- **Anchor:** `PunchPanel.submit`
- **Comment:** `SchemaMissing`, `PersonNotTracked` and `OnLeave` all mean the tiles are stale, but the panel stays on the pad under the old label and every retry gets the same refusal until « Retour » or the 30 s idle. Fix: add them to the go-back branch so the panel returns to the tiles and reloads.


### Finding 19
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-dialogs.tsx
- **Line:** 81
- **Anchor:** `CorrectAttendanceDialog.reload`
- **Comment:** `reload` awaits `reloadEntry` with no try/catch; a failed re-read after a 409 is an unhandled rejection, nothing is shown, and the dialog keeps its stale version (poisoned for every later save). Sibling dialogs wrap it. Fix: try/catch with `showErrorToast` (or `conflict.raise`), disable « Recharger » while pending.


### Finding 20
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Queries/AttendanceQueries.cs
- **Line:** 300
- **Anchor:** `TeamTodayRules.PersonToday`
- **Comment:** « An accepted congé covers this day » exists three times (here, `AttendanceRules.IsOnFullDayLeave`, `AttendanceSheetBuilder.Build`), and `GetPointageStatusQuery` re-maps `State` with a second rule, so a half-day congé reads « En congé » on the desk and « Pas encore arrivée » on the borne — the opposite of this class's « means one thing ». Fix: one `AttendanceRules` predicate; have `PersonToday` carry the half-day distinction (e.g. « En congé (demi-journée) »); move `TeamTodayRules` next to `AttendanceRules`.


### Finding 21
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Queries/AttendanceQueries.cs
- **Line:** 313
- **Anchor:** `TeamTodayRules.PersonToday`
- **Comment:** The wire states `"in"`/`"out"`/`"leave"` are bare literals at three sites (here, the sort at line 276, `GetPointageStatusQuery.cs:64`) while the actions got constants. Fix: `AttendanceRules.StateIn/StateOut/StateLeave`.


### Finding 22
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/dashboard/team-today-block.tsx
- **Line:** 43
- **Anchor:** `TeamTodayBlock`
- **Comment:** `/users?onglet=presences` and `/users?onglet=conges` (line 53) are hardcoded while `USERS_ATTENDANCE_TAB` is private to `app/users/page.tsx` (its doc claims it is « the tab the dashboard opens »). Renaming the tab breaks the link silently. Fix: export the constant from `lib/` beside `USERS_LEAVE_TAB` and build both hrefs from them.


### Finding 23
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/team-today-list.tsx
- **Line:** 28
- **Anchor:** `TeamTodayList`
- **Comment:** The state → French label mapping is copied in `stateLine` (web/app/pointage/page.tsx:144). Fix: one `pointageStateLabel` in `lib/api/pointage.ts` used by both.


### Finding 24
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 37
- **Anchor:** `SchemaSetupCard.load`
- **Comment:** Reads the whole month (`mine()`) only for `tracked`/`hasSchema`, while the `AttendancePanel scope="mine"` it renders reads the same payload — two identical reads on mount and on every `team` broadcast (every punch), including for untracked doctors/admins who see nothing. Fix: one read passed down, or a light source for the two flags.


### Finding 25
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/user-management.tsx
- **Line:** 88
- **Anchor:** `UserManagement`
- **Comment:** The old comment (« only while the cabinet has [congés] switched on ») sits above the new one saying the opposite, and `leaveEnabled` now means « either module is on » while still driving « Voir sa fiche et ses congés » / « Fiche et congés de … » (lines 429, 773) — wrong when only the pointage is on. Fix: delete the stale line, rename to `staffFileEnabled`, word the labels for the module that is on.


### Finding 26
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 95
- **Anchor:** `AttendanceSettingsCard`
- **Comment:** `isThisDevice` compares borne names, which are not unique — two « Accueil » bornes both read « cet appareil », and « Retirer » on the wrong one kills the live borne. Fix: return the borne `id` in `PointageStatusDto` and compare ids.


### Finding 27
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 64
- **Anchor:** `PointagePage.backToTiles`
- **Comment:** Returning to the tiles unmounts the focused pad, so focus falls to `<body>` (PC borne keyboard users re-tab from the top; screen readers lose position), and `Confirmation` mounts `role="status"` with its text already inside, which many screen readers do not announce. Fix: focus the tile just used (or the first enabled one); keep one persistent live region and write the confirmation into it.


### Finding 28
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/schema-pad.tsx
- **Line:** 68
- **Anchor:** `SchemaPad.finish`
- **Comment:** `finish()` clears the pattern on lift, before the result returns, so `error` has no path to paint red — a wrong schéma on the tablet shows a neutral pad and one small line under it. Fix: clear at the start of the next stroke/keystroke instead.


### Finding 29
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/schema-pad.tsx
- **Line:** 135
- **Anchor:** `SchemaPad` (`role="application"`)
- **Comment:** Keys 1–9 work but nothing announces progress; a screen-reader user cannot tell a digit was ignored (dot already used) until Entrée is refused. Fix: a visually hidden `aria-live="polite"` dot count (never which dots).


### Finding 30
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 132
- **Anchor:** `SchemaDialog`
- **Comment:** Radix handles Échap before the pad's own « clear », so Échap while keying closes the dialog and loses the first drawing (an overlay tap too); after the first pattern the `key` change remounts the focused pad and keyboard focus is lost. Fix: `preventDefault()` in `onEscapeKeyDown` while a drawing exists; refocus the pad after the remount.


### Finding 31
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 172
- **Anchor:** `Tiles`
- **Comment:** The tile name is `truncate`d (~180 px at 320, ~195–245 px on a PC borne at 4 columns): « Fatma Zahra Ben Mahmoud » and « … Ben Mansour » both read « Fatma Zahra Ben M… », and a wrong pick spends a colleague's try. Fix: `line-clamp-2 break-words` — never truncate a control's name.


### Finding 32
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/dashboard/team-today-block.tsx
- **Line:** 53
- **Anchor:** `TeamTodayBlock`
- **Comment:** « N demandes de congé en attente » is a ~20 px inline link with no coarse sizing — under the 44 px floor on the tablet, and the block's only route to the requests. Fix: `inline-flex items-center coarse:min-h-11`.


### Finding 33
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** web/components/dashboard-header.tsx
- **Line:** 432
- **Anchor:** `DashboardHeader` (+ `isThisDeviceABorne` in team/borne-link.tsx, `LocalLoginForm` in app/login/page.tsx)
- **Comment:** Every full page load of every signed-in user, in every cabinet, now sends an anonymous `GET /api/pointage/status` — including where the pointage was never turned on; on a borne device it returns the full tile list (3–4 queries) just to show a link, and the probe is cached for the page's life so the link survives the module being turned off. Fix: in the header, render `BorneLink` only when `clinic.attendanceEnabled`; keep the unconditional probe on /login only; optionally a light probe without people.


### Finding 34
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Audit/AuditLabels.cs
- **Line:** 57
- **Anchor:** `AuditLabels.Entity`
- **Comment:** `AttendanceEntry` and `AttendanceTerminal` are audited but have no French label, so the raw class names reach « Journal d'activité » (filter + rows) and the restore report. Also `terminal.Touch(now)` (PointageCommands.cs:201) writes an « AttendanceTerminal · Modification » row on every punch, credited to the secretary. Fix: add both labels; stop auditing a `LastUsedAt`-only update (or stop writing it per punch).


### Finding 35
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Domain/Entities/User.cs
- **Line:** 712
- **Anchor:** `User.RegisterSchemaFailure`
- **Comment:** The lock never escalates (counter reset to 0 on lock): 480 guesses per person per day, indefinitely, with no admin alert; and anyone at reception (or holding a copied cookie, remotely on hosted) can lock every tile for 15 min, repeatedly — the only remedy, « Réinitialiser le schéma », deletes the schéma. Fix: escalate the lock duration / require an admin unlock after repeated locks, raise a bell entry on a lock, add a per-borne failure budget, and let the admin lift a lock without deleting the schéma.
- **Challenge note:** Severity lowered Minor → Suggestion — AC-7 specifies exactly this (5 tries, 15 min, an admin reset unlocks). Hardening beyond the spec; do not change the AC-7 behaviour without the owner.

### Finding 36
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/SetMyAttendanceSchemaCommand.cs
- **Line:** 51
- **Anchor:** `SetMyAttendanceSchemaCommandHandler.Handle`
- **Comment:** The schéma keyspace is ~10⁶ (1 624 four-dot patterns), so PBKDF2 alone falls in minutes to anyone with a copy of `Users` — every `pg_dump` backup, the PC de secours relay (`User` is in `ClinicRelayScope.Added`), the LAN Postgres. A recovered schéma lets a colleague punch for anyone, and is often a phone unlock pattern. Fix: pepper before hashing with a key kept outside the database (Data Protection key ring / per-install secret), and redact `AttendanceSchemaHash` on the relay.
- **Challenge note:** Severity lowered Minor → Suggestion — spec § Data says `AttendanceSchemaHash` is « copied to the PC de secours », so do NOT redact it on the relay (and a pepper would have to reach the PC too). The small keyspace is inherent to a pattern; a DB copy already holds every patient record.

### Finding 37
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.API/Controllers/PointageController.cs
- **Line:** 86
- **Anchor:** `PointageController.Punch`
- **Comment:** The punch re-issues the same secret, so the borne credential never rotates and never expires while in use; anyone at the reception keyboard can copy it from DevTools and, after leaving, keep reading who is present (and attempt guesses) for months with nothing revealing the clone. Fix: rotate on punch/status with one previous hash in a short grace window (the `SessionFamily.PreviousCredentialHash` pattern), auto-revoke on reuse of an older secret, and show last IP/agent in the bornes list.
- **Challenge note:** Severity lowered Minor → Suggestion — spec § Edge Cases: « it is re-issued on every successful punch ». Rotation is hardening beyond the spec.

### Finding 38
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceRules.cs
- **Line:** 151
- **Anchor:** `AttendanceRules.Initials`
- **Comment:** `PointagePersonDto.Initials` is read by no client — the borne renders `InitialsAvatar name=…`, which computes its own — so two initials rules can disagree. Fix: render `person.initials`, or drop the server rule, the DTO field and the TS field.
- **Challenge note:** Severity lowered Minor → Suggestion — `initials` is pinned in the spec's API contract (`GET /api/pointage/status`). The fix is to render it on the borne, not to drop it.

### Finding 39
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.API/Controllers/PointageController.cs
- **Line:** 3
- **Anchor:** `PointageController`
- **Comment:** `using ClinicManagement.Application.Common.Models;` is unused. Remove it.
- **Challenge note:** Severity lowered Minor → Suggestion — no compiler warning and no behaviour; tidy-up only.

### Finding 40
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 74
- **Anchor:** `AttendancePanel.load`
- **Comment:** `Promise.all([sheet, today()])` lets a failed « Aujourd'hui » read hide a sheet that loaded. Read them separately.


### Finding 41
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/AttendanceAdminCommands.cs
- **Line:** 266
- **Anchor:** `RecordAttendanceEntryCommandHandler.Handle`
- **Comment:** `"userId": null` binds null over the default and `roster.TryGetValue(null, …)` throws → generic error instead of « Compte introuvable. ». Guard with `string.IsNullOrWhiteSpace`.


### Finding 42
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceRules.cs
- **Line:** 203
- **Anchor:** `AttendanceRules.MonthDays`
- **Comment:** « 9999-12 » passes `TryParseMonthKey` and `AddMonths(1)` throws → generic error instead of `MonthInvalid`. Compute the end with `DateTime.DaysInMonth`.


### Finding 43
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/AttendanceAdminCommands.cs
- **Line:** 47
- **Anchor:** `RevokeAttendanceTerminalCommandHandler.Handle`
- **Comment:** « Borne introuvable » / « Pointage introuvable » (also line 206) carry `staff_leave_not_found`. Add `AttendanceRules.NotFoundCode` and map it.


### Finding 44
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/AttendanceAdminCommands.cs
- **Line:** 271
- **Anchor:** `RecordAttendanceEntryCommandHandler.Handle`
- **Comment:** « Choisissez un jour passé ou aujourd'hui. » is inline while every other refusal is an `AttendanceRules` constant.


### Finding 45
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 60
- **Anchor:** `RegisterAttendanceTerminalCommandHandler.Handle`
- **Comment:** Re-implements the borne lookup `AttendanceRules.FindBorneAsync` owns. Call it.


### Finding 46
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceRules.cs
- **Line:** 119
- **Anchor:** `AttendanceRules.Today`
- **Comment:** Duplicates `StaffLeaveRules.ClinicToday()`; two « today » owners in one area. Make one forward to the other.


### Finding 47
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/PointageController.cs
- **Line:** 39
- **Anchor:** `PointageController.WriteBorneCookie`
- **Comment:** `TeamController` depends on another controller's static helper and constant — which hid Finding 2. Move the cookie name/options/read/write into a small API helper both use.


### Finding 48
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 102
- **Anchor:** `ClinicArchiveScope.Excluded`
- **Comment:** The new `AttendanceTerminal` comment is 3 lines; the repo ceiling is 2.


### Finding 49
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-settings-card.tsx
- **Line:** 43
- **Anchor:** `REGISTER_TERMINAL_STEP_UP_ACTION`
- **Comment:** Server mirrors are scattered (step-up action, `MIN_DOTS`/`MAX_DOTS`, `maxLength={60}`, `maxLength={500}`, « 5 essais »); the sibling `SHOW_MONEY_STEP_UP_ACTION` lives in `lib/api/clinics.ts`. Move them to `lib/api/pointage.ts`.


### Finding 50
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/schema-setup-card.tsx
- **Line:** 105
- **Anchor:** `SchemaDialog.drawn`
- **Comment:** `pattern.length > MAX_DOTS` can never be true (each dot once). Remove it.


### Finding 51
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/schema-pad.tsx
- **Line:** 13
- **Anchor:** `SchemaPad`
- **Comment:** The component's JSDoc sits above `const VIEW`, not above `export function SchemaPad`.


### Finding 52
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/app/pointage/page.tsx
- **Line:** 214
- **Anchor:** `PunchPanel`
- **Comment:** Focuses the pad by `querySelector("svg")` in the child's DOM. Give `SchemaPad` an `autoFocus` prop or forward a ref.


### Finding 53
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 97
- **Anchor:** `AttendancePanel.reloadEntry`
- **Comment:** The `scope.kind === "mine"` branch is unreachable (the dialog only renders when editable), and `DayCards` gets both `days` and `sheet`. Drop the branch and the redundant prop.


### Finding 54
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-dialogs.tsx
- **Line:** 24
- **Anchor:** `dayLabel`
- **Comment:** Same function as `dayHeadingLong` in attendance-panel.tsx. Export one.


### Finding 55
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-dialogs.tsx
- **Line:** 54
- **Anchor:** `CorrectAttendanceDialog`
- **Comment:** `fill(line)` exists but its body is re-written in both effects. Call `fill`.


### Finding 56
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed
- **File:** web/components/team/schema-pad.tsx
- **Line:** 150
- **Anchor:** `SchemaPad`
- **Comment:** The borne draws the whole path while drawing and keeps it (red after a wrong try) on a screen at a public desk — shoulder-surfing reads a schéma in one glance. Add a `hideTrail` prop for the borne (keep the trail in Mon profil). Separately, `PUT /api/team/me/schema` replaces an existing schéma without proving the current one; consider a step-up.


### Finding 57
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 421
- **Anchor:** `DayCards` / `SheetTable` empty states
- **Comment:** « Aucun pointage ce mois-ci. », « Personne ne pointe dans ce cabinet. » (also team-today-list.tsx:12) are hand-written beside `ui/empty-state.tsx`; below `lg` the empty list also offers no « Ajouter un pointage ». Use `EmptyState` with that action when editable.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 8 |
| Minor | 25 |
| Suggestion | 23 |
| **Total** | 57 |
