# Feature Specification: Congés du praticien

**Status:** APPROVED
**Type:** Small
**Created:** 2026-10-20
**Scope:** Full
**Feature:** A practitioner records a congé (date range); for the whole period they are marked out of the cabinet.

## Overview
Today the only way to say « le Dr X n'est pas là du 12 au 20 » is a « créneau occupé » block or editing the
working hours and remembering to put them back. A congé is a dated period of whole days, recorded per
practitioner: the agenda shows it, and booking that practitioner inside it gets the same « Continuer quand même »
warning as a closed day.

## What Changes
- A visible **« Congé »** button in the agenda bar opens the add form — the agenda is where a congé is thought of.
- « Congés » card, beside « Horaires de travail », in « Mon profil » (own) and « Paramètres → Médecins » (any practitioner) — the full list lives there.
- Add / modify / delete a congé: **Du**, **Au** (inclusive, whole days, clinic-local), optional **Motif** (« Vacances », « Formation »…).
- The card lists upcoming and current congés first; past ones are folded under « Congés passés ».
- Booking that practitioner on a congé day is warned, never hard-blocked (emergency, favour) — same rule as out-of-hours.
- The agenda states the congé on the day itself.
- Saving a congé that covers already-booked visits succeeds and says how many; those visits are not touched.

## Acceptance Criteria
- **AC-1:** A doctor can add, modify and delete their own congés; an admin **and a secretary** can for any practitioner — reception keeps the agenda, so it records the congé it is told about.
- **AC-1b:** The agenda bar carries a « Congé » button beside « Nouveau » — labelled from `xl:`, icon-only with `aria-label="Ajouter un congé"` below, never inside « ⋯ » or « Filtres ». On a phone it is in the agenda header, ≥ 44 px. Shown to every role.
- **AC-1c:** Its form pre-fills « Praticien » with the agenda's filtered practitioner, else the user's own, else the cabinet's only one; a doctor's form is locked to themselves. « Du » pre-fills with the day on screen.
- **AC-1d:** Tapping a « Congé — Dr X » marker on the agenda opens that congé (Modifier · Supprimer) for those allowed to edit it (AC-1); a doctor looking at a colleague's sees its dates and motif read-only.
- **AC-2:** « Au » before « Du » is refused on the field; a congé longer than 366 days is refused (a mistyped year).
- **AC-3:** A congé overlapping another congé of the same practitioner is refused, naming the existing one (« Dr X est déjà en congé du 12/08 au 20/08 »).
- **AC-4:** Creating or updating an appointment for that practitioner on a congé day — create dialog, edit dialog, agenda drag-move, waiting-list promotion — is refused with « Dr X est en congé du 12/08 au 20/08. » and offers « Continuer quand même », which books it.
- **AC-5:** A recurring series treats a congé day exactly as it treats a closed day.
- **AC-6:** A visit with no practitioner, or with another practitioner, is not affected by the congé.
- **AC-7:** Saving a congé that covers N booked, non-cancelled visits of that practitioner shows « N rendez-vous sont déjà prévus pendant ce congé » with a link to the agenda on the first one, filtered on the practitioner. Nothing is cancelled or moved.
- **AC-8:** In Jour view, and Semaine view from `md:`, a congé day carries a **coloured ribbon** across the top of its column naming who is on congé (« Congé — Dr X ») — a filled band, not grey text, so it is seen at a glance at every width.
- **AC-9:** When the agenda shows one practitioner — filtered on them, or the cabinet has a single active practitioner — a congé day is drawn closed (the closed-day hatch) and its header reads « congé » instead of the RDV count; visits already booked stay visible on it.
- **AC-10:** In the phone Semaine and Mois cells a congé day carries a « congé » marker, its accessible name names the practitioner, and tapping the day opens Jour view, which names them (AC-8).
- **AC-11:** Deleting a congé asks for confirmation naming its dates; nothing else changes.
- **AC-12:** A change made on one device reaches every open agenda and card without a reload.
- **AC-13 (device):** At 320 px the congés card has no horizontal scroll, each congé is one stacked row whose actions sit in one menu with a ≥ 44 px trigger on a coarse pointer, and the add/edit form opens as a bottom sheet with its « Enregistrer » visible above the keyboard.

## API Contract
### GET /api/doctors/{doctorId}/leaves
Response 200: `[{ id, doctorId, startDate: "YYYY-MM-DD", endDate: "YYYY-MM-DD", note: string|null, version }]`
### GET /api/doctors/leaves?from=YYYY-MM-DD&to=YYYY-MM-DD
Clinic-wide, for the agenda. Response 200: same items + `doctorName`.
### POST /api/doctors/{doctorId}/leaves — PUT /api/doctors/{doctorId}/leaves/{id}
Request: `{ startDate, endDate, note?, version? (PUT) }` → Response: `{ leave, appointmentsDuring: number }`
Errors: `400 leave_invalid_range` · `400 leave_too_long` · `409 leave_overlaps` · `409` stale version · `403` not own/admin · `404`
### DELETE /api/doctors/{doctorId}/leaves/{id} → 204
### Booking (existing endpoints)
A congé refusal reuses `outside_working_hours` and the existing `allowOutsideWorkingHours` override, so every surface that already offers « Continuer quand même » covers it with no new client path.

## Data / Schema Changes
- New table `DoctorLeaves`: `Id`, `ClinicId` (tenant-filtered), `DoctorId` (FK), `StartDate` / `EndDate` (`date`, inclusive, clinic-local), `Note` (nullable, ≤ 200), `CreatedAt`, `UpdatedAt`, `xmin`. Index `(ClinicId, DoctorId, StartDate)`. Audited.

## Device Behaviour
- **Leading device:** desk (card), tablet at the chair (agenda).
- **Narrow width (< 640):** card rows stack (dates · nb de jours · motif), actions in one « ⋯ » menu; form = bottom sheet; « Congé » button in the phone agenda header; agenda per AC-8–10.
- **820 px (tablet portrait):** the agenda bar stays one row — the « Congé » button is icon-only there, like the Google control.
- **Touch:** no hover-only state; the practitioner's name is never in a tooltip alone.

## Out of Scope
- Cabinet-wide closures (jours fériés, fermeture annuelle).
- Half-days or a few hours (use a « créneau occupé »).
- Cancelling, moving or messaging patients already booked during the congé; their SMS reminders still go out.
- Google Calendar sync of a congé; the dashboard's « prochaine journée » line; future online booking.

## Edge Cases (Critical only)
- A congé that started in the past can still be modified or deleted.
- A retired practitioner keeps their congés on history; none can be added for them.
- Days are clinic-local (`ClinicClock`): a 00:30 visit on the first day of the congé is on the congé.
