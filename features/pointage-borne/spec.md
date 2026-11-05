# Feature Specification: Pointage sur la borne du cabinet

**Status:** APPROVED
**Type:** Small
**Created:** 2026-11-05
**Scope:** Full
**Feature:** Secretaries clock in and out on a cabinet borne with a personal schéma. The admin sees who came, when,
and who left.
**Part 2 of 3** of [`pointage-conges/blueprint.md`](../pointage-conges/blueprint.md) · after
[`conges-secretaires`](../conges-secretaires/spec.md) · before [`pointage-obligatoire`](../pointage-obligatoire/spec.md).
Visuals: https://claude.ai/artifact/LJQDQvKRaLqCLmXvRJrrsF · ⚠️ build from `main`.

## Overview
A cabinet's admin turns the reception PC or a tablet into a **borne**, from that device. A secretary taps her name
on it, sees « Arrivée » or « Départ », and draws her schéma.

Only a borne is accepted, which is what proves she is at the cabinet. The schéma proves it is her. The admin gets a
month sheet per secretary and corrects mistakes with a motif. The original borne hours are always kept.

It is off by default, per cabinet. There is no fingerprint and no GPS.

## What Changes
- **Paramètres → « Équipe »:** a « Pointage des secrétaires » switch (off by default), the list of bornes with
  « Retirer », « Faire de cet appareil une borne », and « Personnes qui pointent ».
- **« Mon profil »:** a « Schéma de pointage » card for any account that clocks in.
- **`/pointage`** (the borne): name tiles → « Arrivée » or « Départ » → the schéma → a confirmation.
- **On a borne:** « Pointage » also appears on the sign-in screen and in the top bar.
- **« Utilisateurs »:** a « Présences » tab (today plus the month sheet, « Exporter »).
- **The secretary's file:** presence figures.
- **« Mes présences »:** a read-only tab for the secretary.
- **Tableau de bord:** an « Équipe aujourd'hui » block for the admin.

## Acceptance Criteria
- **AC-1:** The switch is off by default.
  - While it is off the borne refuses (`team_module_off`) and the « Présences » tabs are hidden.
  - Switching it off deletes nothing.
- **AC-2:** « Faire de cet appareil une borne »:
  - It is pressed on the device itself by an admin, asks for a name, and confirms with the existing step-up.
  - That device then shows « Pointage » on the sign-in screen and in the top bar.
  - « Retirer » takes effect at the device's next punch.
- **AC-3:** A punch from any device that is not an active borne of this cabinet is refused (`terminal_unknown`,
  « Cet appareil n'est pas une borne du cabinet »).
- **AC-4:** « Personnes qui pointent »:
  - It defaults to every active `secretary` account, and the admin can tick or untick any account.
  - This is stored as an override, so a role change follows the default.
  - Untracked and inactive accounts never appear on the borne.
- **AC-5:** « Schéma de pointage » in Mon profil:
  - She draws it twice, using 4–9 dots, each dot at most once.
  - The admin can « Réinitialiser le schéma ».
  - A tracked person with no schéma shows « Schéma à créer dans Mon profil » on her tile and cannot clock in.
- **AC-6:** Borne flow:
  - Each tile shows « Pas encore arrivée », « Présente depuis 08:02 », or « En congé » (disabled when an accepted
    congé covers today).
  - Tapping a tile shows **« Arrivée » or « Départ » in large before the pad**.
  - The right schéma shows « Arrivée enregistrée · 08:02 · Borne Accueil », then the tiles again after about 2 s.
- **AC-7:** After 5 wrong schémas the tile locks for 15 min (« Réessayez dans 14 min ») and the journal records it.
  An admin reset unlocks it.
- **AC-8:** A day holds as many arrival/departure pairs as needed (lunch).
  - A punch never closes a line from an earlier day: the arrival opens a new line, and the old one stays
    « Départ non pointé ».
  - **No hour is ever invented.**
- **AC-9:** The « Présences » tab shows:
  - **Today:** who is present and since when, and who is on congé.
  - **The month sheet:** a row per day and a column per tracked person.
    - Closed days are hatched and accepted congés are labelled.
    - An open line reads « présente ».
    - A missing departure is red « Départ non pointé ».
    - A corrected line is « Corrigé ».
  - **Monthly totals:** days, hours of closed lines, missing departures.
  - **« Exporter »:** the month as CSV.
- **AC-10:** Correction (admin):
  - « Corriger » sets the arrival and departure and requires a motif. The borne's recorded hours stay visible in
    the line's detail.
  - « Ajouter un pointage » covers a day with none, also with a motif.
- **AC-11:** The secretary's file shows the month's figures (jours pointés, heures, départs non pointés) and a
  « Présences » tab. She sees her own lines read-only in « Mes présences ».
- **AC-12:** « Équipe aujourd'hui » on the dashboard (admin only, can be hidden like the other blocks):
  - Who is present and since when, and who is on congé.
  - The pending congés, when part 1 is on.
  - It opens « Utilisateurs ».
- **AC-13:** A punch reaches the admin's open sheet and dashboard without a reload.
- **AC-14:** Times are Tunisian and on 24 h. A line belongs to the cabinet-local day of its arrival.
- **AC-15:** On a Cloud cabinet whose subscription has lapsed, the borne refuses with the usual
  « Abonnement expiré ».
- **AC-16 (device):**
  - The borne works on a 10" tablet in portrait and landscape, and on a PC.
  - The pad's dots are ≥ 44 px on a coarse pointer, and keys 1–9 draw the schéma from a keyboard.
  - Below `lg` the sheet becomes one card per day.
  - At 320 px there is no horizontal scroll.

## API Contract
### GET /api/pointage/status — anonymous; borne cookie optional
Response 200: `{ isBorne, name?, people?: [{ userId, name, initials, state: "out"|"in"|"leave", since?, needsSchema, lockedUntil? }] }`

### POST /api/pointage/punch — borne
Request: `{ userId, pattern: number[] }` → `{ action: "arrival"|"departure", at: "HH:mm", name }`
Errors: `401 terminal_unknown` · `400 schema_wrong { attemptsLeft }` · `423 schema_locked { lockedUntil }` ·
`409 schema_missing` · `403 team_module_off` · `402` subscription

### GET · POST /api/team/terminals · DELETE /api/team/terminals/{id} — AdminOnly
The POST takes `{ name, stepUpToken }` and sets the `HttpOnly` borne cookie on the response.

### PUT /api/team/me/schema — own account · DELETE /api/team/users/{id}/schema — AdminOnly

### PUT /api/team/users/{id}/attendance-profile — AdminOnly
Request: `{ tracked: bool | null, version }`

### GET /api/team/attendance?month=YYYY-MM&userId= · GET /api/team/attendance/export?month= — AdminOnly

### GET /api/team/me/attendance?month= — own account

### POST /api/team/attendance/{id}/correction · POST /api/team/attendance — AdminOnly
Requests: `{ arrival, departure?, reason, version }` · `{ userId, day, arrival, departure?, reason }`

### PUT /api/clinics/team-modules — AdminOnly
Gains `attendance?`.

## Data / Schema Changes
- **New table `AttendanceTerminals`:** `Id`, `ClinicId`, `Name` (≤ 60), `SecretHash` (unique), `CreatedByUserId`,
  `CreatedAt`, `LastUsedAt`, `RevokedAt`, `xmin`.
- **New table `AttendanceEntries`:**
  - Who and where: `Id`, `ClinicId`, `UserId`, `LocalDay` (`date`), `TerminalId` (**no FK**), `TerminalName`.
  - Borne hours, never changed: `PunchedArrivalUtc`, `PunchedDepartureUtc`.
  - Hours that count: `ArrivalUtc`, `DepartureUtc`.
  - Departure origin and correction: `DepartureSource` (int), `CorrectedByUserId`, `CorrectionReason`,
    `CorrectedAt`, `xmin`.
  - Indexes: `(ClinicId, UserId, LocalDay)` and `(ClinicId, LocalDay)`.
- **`Users`:** `AttendanceSchemaHash`, `AttendanceTrackedOverride` (bool null), `SchemaFailedAttempts`,
  `SchemaLockedUntilUtc`.
- **`Clinics.AttendanceEnabled`:** bool, default false. It goes through the same 7 `ClinicDto` sites.
- **Secrets:** `SecretHash` and `AttendanceSchemaHash` are **redacted in the archive** and **copied to the PC de
  secours**.

## Device Behaviour
- **Leading device:** a tablet or PC at reception for the borne; desk for the sheet.
- **Narrow width (< 640):** the sheet is one card per day, with each person's lines stacked; dialogs are bottom
  sheets.
- **Touch:** the pad uses pointer events with `touch-action: none`. « Corriger » is a visible button on a red
  line, never hover-only.

## Out of Scope
- The arrival lock, the departure reminder, the sign-out prompt and the next-morning declaration (part 3).
- Planned working hours per person and lateness.
- A borne that keeps working during an internet cut on the PC de secours.
- Fingerprint, GPS, a QR code.

## Edge Cases (Critical only)
- **A borne request has no user:** the scope is set from the borne (`UseClinic`), or it reads zero rows.
- **The borne cookie:** it is re-issued on every successful punch, because Chrome caps cookies at 400 days.
- **A deactivated account:** it leaves the borne at once, and its lines stay.
