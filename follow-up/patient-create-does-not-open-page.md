# Creating a patient from « Patients » does not open their page

**Feature:** general (found by `guided-first-days` R3 QA, row D5) · **Type:** bug · **Priority:** medium · **Created:** 2026-11-03

## What happens

On `/patients`, « Ajouter un patient » → names → « Créer le patient »: the patient is saved (201) and the list shows
them, but the screen stays on `/patients`. The code means to open the new patient's page
(`web/app/patients/page.tsx`, `onSuccess` → `router.push(`/patients/${created.id}`)`).

Reproduced with no guide running (`features/guided-first-days/qa/r3/walk.mjs`, D5), on the isolated stack.

## Likely cause

`web/lib/hooks/use-dirty-guard.ts` keeps a history entry while a guarded dialog is open (the phone's back gesture)
and, in the effect cleanup when `open` turns false, calls `window.history.back()` if its marker is still current.
`onSuccess` closes the dialog and pushes the patient's route in the same tick; the cleanup's `back()` lands after
the push started and returns the address to `/patients`.

## Ways to fix (pick one, then test every guarded dialog that navigates on save)

- In the hook: when the dialog closes and the address has already moved away from the marker's page (or a
  navigation is pending), do not `back()`; or replace the marker instead of popping it.
- At the call site: navigate after the guard has popped (e.g. on the `popstate` that `back()` produces, with a
  short fallback timer). Only fixes this one caller — the same shape may exist elsewhere.

⚠️ The hook's own comments record two earlier defects in this exact area (a dialog closing itself a frame after
opening). Grep every `useDirtyGuard` caller whose save also calls `router.push` before choosing.
