# Feature Specification: Acte non terminé, et la suite qu'on propose

**Status:** APPROVED
**Type:** Small
**Created:** 2026-09-04
**Scope:** Full
**Feature:** A dentist marks an act « non terminé » on the fiche de soins; the next booking for that patient offers to plan its continuation, and states the money instead of moving it.

## Overview
A séance planned as one visit runs long and the work is left unfinished. Nothing records that today — a fiche
says what was *done*, never what remains — so `GetContinuableActsQuery` offers every recent act and makes the
dentist recognise the right one from a list. This adds the one fact only the dentist has: an act-level
« non terminé ». The booking dialog then names that act unprompted, instead of asking a neutral question.

The money is **stated, never moved**. An act billed 1 000 with 800 collected leaves 200 owed *on its note* —
where la caisse, « Créances » and « Solde patient » already carry it. This feature says so in the notice and
changes no figure anywhere.

## What Changes
- Each act on a fiche de soins carries « Acte non terminé » (off by default), set by the dentist alone.
- The act's card shows a « Non terminé » chip with the detail fold collapsed.
- Once a continuation devis exists for that fiche, the chip names it: « Non terminé · suite planifiée DV-… ».
- Booking a rdv for a patient with an unfinished act raises a dismissible notice naming the act, its date, its
  fee, and what is still owed on its note.
- Accepting the notice opens the existing continuation dialog with that act preselected.
- `continuable-acts` returns `isUnfinished` and sorts unfinished acts first; the list still offers every recent
  act, so a forgotten tick is not a dead end.

## Acceptance Criteria
- **AC-1:** In « Ajouter fiche dentaire » each act has « Acte non terminé », unchecked by default; saving
  persists it per act, and re-opening the fiche restores it.
- **AC-2:** An act marked non terminé shows a « Non terminé » chip on its card **face** — readable with the
  detail fold collapsed.
- **AC-3:** When that fiche has since been picked up by a continuation devis, the chip reads
  « Non terminé · suite planifiée <numéro> ». The flag itself is never cleared automatically.
- **AC-4:** Booking a rdv for a patient with an unfinished act ≤ 120 days old and not already on a devis shows a
  dismissible `role="status"` notice naming the act, its teeth, its date and its fee. Dismissal is keyed on the
  patient, and the notice is withdrawn once any devis act is on the séance.
- **AC-5:** When a plan step suggestion also applies, the plan suggestion wins — **at most one notice** renders.
- **AC-6:** When the fiche is on a note with an outstanding balance, the notice reads « Acte prévu à
  1 000,000 DT · 200,000 DT restent dus sur la note F-… — à encaisser sur cette note. » Nothing is prefilled
  into « Montant du travail restant », which keeps its one meaning: **new work only**.
- **AC-7:** Accepting the notice opens `ContinueSessionDialog` with that act preselected and the step label
  prefilled « Suite ». The existing irreversibility confirmation still applies; the notice alone creates no devis.
- **AC-8:** No act cost, fiche total, or note d'honoraires is written by any path in this feature.
- **AC-9 (device):** At 320 px the notice's accept button is full-width on its own row and wraps
  (`whitespace-normal`, `h-auto`); the chip wraps rather than truncating the act's name; the checkbox and its
  label form one target ≥ 44 px on a coarse pointer. Floor per `~/.claude/skills/DEVICE-CONTRACT.md`.

## API Contract
No new endpoints. `GET /api/treatment-plans/continuable-acts` gains `isUnfinished: bool` per row, unfinished
first. `POST/PUT` dental-record payloads gain `isUnfinished` per act (optional, defaults false).

## Data / Schema Changes
- `DentalRecordAct.IsUnfinished` — `bool NOT NULL DEFAULT false`. ⚠️ Check the scaffolded migration for the
  `AddColumn<uint>("xmin")` trap and run `verify-schema` either side.
- `DentalRecordActDto.IsUnfinished`, `DentalActInput.IsUnfinished`, `ContinuableActDto.IsUnfinished`.
- `DentalRecordDto` gains the continuation plan's id + number (record-level: a fiche can be continued at most
  once, enforced by the existing `alreadyTracked` guard).

## Device Behaviour
- **Leading device:** desk for the fiche, **phone** for the booking notice — that is where the dialog is used in
  a hurry, and where `PlanStepSuggestionNotice` already had to be moved onto the content.
- **Narrow width (< 640):** the notice keeps its stacked layout — text block, then a full-width wrapping accept
  button; the fiche's checkbox sits in the detail fold with its state mirrored as a card-face chip.
- **Touch:** checkbox + label are one ≥ 44 px target on a coarse pointer; the dismiss button is `coarse:size-11`
  and carries its own `aria-label`.

## Out of Scope
- **Any money mutation.** No note re-totalling, no tariff prefill, no transfer of the outstanding onto the devis
  — the exclusion in `PlanBillingRules.BilledPlanIds` means money moved onto an attached devis stops being read.
- Inference of any kind: an act is unfinished only because someone ticked it.
- Feeding the flag to the visit-closure worklist, notifications, the dashboard or « Traitements en cours ».
- Changing the generic « Séance suivante » default on the existing continuation door.

## Edge Cases (Critical only)
- **Multi-act fiche:** the flag is per **act**, the outstanding is per **note**. The notice states both and does
  no arithmetic between them — they are not the same quantity.
- **Already on a devis:** excluded from `continuable-acts` today; it must raise no notice either, or the dentist
  is offered a second devis over the same work.
- **Several unfinished acts:** the notice names the most recent one only; the dialog's list holds the rest.
- **Flag on a deleted/edited act:** the flag lives on the act row, so re-saving the fiche without that act drops
  it with the act — no orphan.
