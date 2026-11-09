# Review fixes — pointage ease of use (2026-11-09)

Source: `reviews/feature-review.md` (36 challenged findings, F1–F36). Owner: fix all but **F18** (lunch break — she
chose to keep AC-4 as written: no lock after any departure). Everything stays **uncommitted** in this worktree.

Owner's design rules for every fix: no helper/caption paragraphs under controls (statuses, errors, refusals and empty
states may carry text) · never state a next step as a fact · never trade a capability for space · one-line code
comments, never a finding number in code · French UI, `quoteFr` for quoted values.

## Wire contract (camelCase JSON) — `web/lib/api/pointage.ts` is already written to it

| Endpoint / DTO | Change | For |
|---|---|---|
| `ClockStatusDto` (`GET /pointage/me/clock-status`) | + `tracked: bool`, `leftAt: "HH:mm"?` (today's last line closed, none open), `schemaLockedUntil: ISO?` | F1, F19, F31 |
| `PUT /team/me/schema` | body `{ pattern, currentPassword? }` — required when she already has a schéma; `400 schema_password_required` / `400 schema_password_wrong`; a wrong password counts on the account's sign-in lockout and is refused while locked out | F1 |
| `POST /team/users/{id}/schema/unlock` (AdminOnly) → `AttendancePersonSettingDto` | clears the 5-try block, keeps the schéma | F13 |
| `PointagePersonDto` (borne tiles) | `state` may be `left`; + `leftAt` | F11 |
| `AttendanceTerminalDto` | + `activatedAt: ISO?` | F6, F10 |
| `BorneDeviceDto` | + `kind: pc\|tablet\|phone\|unknown`; one row per (person, device label), newest; tracked accounts' phones left out | F5 |
| `POST /team/terminals` | `409 borne_name_taken` (another live borne has the name — the one being replaced excluded); `400 borne_device_phone`; `replacingTerminalId` honoured with `deviceSessionId` too (any live/pending borne of this cabinet, retired in the same save) | F2, F5, F25 |
| `DELETE /team/terminals/{id}` | `409 borne_last_while_present` naming who is present, when it is the last live borne | F26 |
| `AttendanceSettingsDto` | + `setup: AttendanceSetupDto` (see pointage.ts) | F7, F8, F2 |
| `TeamTodayDto` | + `isOpenDay`, `setupIssue: no_borne\|borne_pending\|no_punch_since_opening\|null` | F11, F12, F2 |
| `TeamTodayPersonDto` | `state` may be `left`; + `leftAt`, `needsSchema`, `schemaLockedUntil` | F11, F12 |
| Lock rule | holds only from the next opening after max(lock armed, first live borne activated) | F6 |
| Notifications (appended categories) | `AttendanceSchemaToCreate` (each tracked person without schéma, when a borne goes live; pushed) · `AttendanceBorneInstalled` (admins + the device's owner, on claim) · `AttendanceBorneNotReceived` (admins, pending borne's session signed out) · `AttendanceSchemaBlocked` (admins, on the 5th wrong try) | F3, F10, F13 |
| Reminder (`AttendanceDepartureMissing`) | neutral wording naming the borne(s); skipped when the cabinet has no live borne | F26, F27, F30 |

## Who owns what (parallel work — disjoint files)

| Owner | Files | Findings |
|---|---|---|
| Backend agent | `api/**` only | F2, F3, F5, F6, F8, F10, F11, F12, F13, F25, F26, F27, F30 (server strings), contract rows above |
| FE secretary agent | `web/components/team/{clock-in-gate,punch-flow,schema-setup-card,schema-pad,sign-out-prompt,borne-link}.tsx`, `web/app/pointage/**`, `web/app/mon-profil/**`, new `web/app/mes-presences/**`, `web/components/dashboard-header.tsx`, `web/components/notification-panel.tsx`, `web/lib/nav.ts` | F1, F3 (FE), F4, F14, F15, F16, F19, F20, F21, F26 (phrase), F27 (FE), F28, F30, F31 |
| FE doctor agent | `web/components/team/{attendance-settings-card,attendance-panel,attendance-dialogs,team-today-list}.tsx`, `web/components/team-modules-card.tsx`, `web/components/clinic-settings.tsx`, `web/components/dashboard/team-today-block.tsx`, `web/app/users/**`, `web/components/user-management.tsx`, `web/components/staff-leave/staff-leave-month-calendar.tsx`, new `web/components/ui/month-stepper.tsx`, `web/lib/guides/**` | F2, F5, F7, F8, F9, F10, F11, F12, F13, F17, F22, F23, F24, F25, F29, F32–F36 |
| Orchestrator | `web/lib/api/pointage.ts` (done), integration, gates, browser pass | — |

## Blast radius (must re-test rows go to the QA plan's tier D)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `pointageStateLabel` wording + `left` state | borne tiles, Aujourd'hui, dashboard block, staff file | must change — all read the one helper |
| 2 | `AttendanceLockRules.IsLocked` / facts | 423 middleware, clock-status, reminder job | must re-test — one rule, both callers |
| 3 | `TeamTodayRules.PersonToday` | dashboard block, Présences « Aujourd'hui », borne tiles | must change — `left` everywhere |
| 4 | `ClinicDto` / team modules | 7 `ClinicDto` sites (none changed — new fields live on the settings read) | unaffected |
| 5 | `NotificationCategory` (+4) | `StaffNotificationRules` (throws on unclassified), push decorator, web panel labels + click routes | must change |
| 6 | `SetMyAttendanceSchemaCommand` (password) | Mon profil dialog, lock « Schéma oublié ? » | must change — both callers send it |
| 7 | Migration (`AttendanceLockArmedAtUtc`, `ActivatedAtUtc`) | demo DB only; verify-schema | must re-test |
| 8 | `PunchFlow` (stage callback, settled list) | borne page, lock, sign-out prompt | must re-test all three |
| 9 | Month stepper extraction | Congés calendar, Présences sheet | must re-test both |
