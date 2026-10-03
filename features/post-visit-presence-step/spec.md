# Feature Specification: Post-visit popup asks « venu ? » first

**Status:** APPROVED
**Type:** Small
**Created:** 2026-10-03
**Scope:** FE
**Feature:** The « Séance terminée » popup asks whether the patient came before offering the fiche de soins.

## Overview
When a séance's slot ends, `post-visit-review-popup.tsx` prompts « Séance de X terminée » → « Remplir la fiche
de soins ». A patient who never came has no fiche to fill, and today the only way out is « Plus tard », so the
prompt keeps coming back. The popup becomes two steps: first the presence question, then the fiche.

## What Changes
- **Step 1 (new):** « Séance terminée » · « Séance de X terminée. Le patient est-il venu ? » · buttons
  « Plus tard » · « Absent » · « Venu ».
- **« Venu »** → step 2, with **no write** (saving the fiche already marks the visit « Terminé »).
- **Step 2:** today's popup, unchanged — « Plus tard » · « Remplir la fiche de soins ».
- **« Absent »** → marks the appointment « Absent » (`NoShow`), toast « Patient marqué comme absent. », closes
  the popup. Same call as « Absent » in the closure worklist (`visit-closure-list.tsx`).
- **Tablet toast** asks the same question: action « Venu », cancel « Absent ». « Venu » turns the same toast
  into today's « Remplir » toast.

## Acceptance Criteria
- **AC-1:** On a desk (fine pointer, ≥ 768 px), the popup opens on step 1 with « Plus tard », « Absent », « Venu ».
- **AC-2:** « Venu » shows step 2 with no network write; « Remplir la fiche de soins » opens the fiche as today.
- **AC-3:** « Absent » sets the appointment to « Absent », shows the success toast, and closes the popup.
  The appointment leaves the closure worklist and the bell's review row disappears (server already does this).
- **AC-4:** If « Absent » is refused (e.g. a fiche was saved meanwhile), the error toast shows the server's
  reason and the popup stays open on step 1.
- **AC-5:** « Plus tard », ✕, Escape and click-outside behave as today on both steps (snooze the queue until
  end of day).
- **AC-6:** An appointment already « Terminé » (answered « Venu » from the worklist, no fiche yet) opens
  directly on step 2.
- **AC-7:** On a tablet (coarse pointer, ≥ 768 px) the toast shows « Venu » and « Absent », each ≥ 44 px tall.
- **AC-8:** On a phone (< 768 px) nothing changes — no prompt, the bell row is the reminder.

## Device Behaviour
- **Leading device:** desk (dialog).
- **Narrow width (< 768):** no prompt at all, as today — the bell carries it.
- **Touch:** tablet toast with two actions (« Venu » / « Absent ») at 44 px.

## Out of Scope
- The bell's notification row (still deep-links to the fiche).
- A « Retour » from step 2 to step 1 (« Venu » writes nothing, so nothing to undo).
- Any backend change — `NoShow` is already a legal manual exit and already removes the review.

## Edge Cases (Critical only)
- « Absent » does **not** snooze the rest of the queue: the next waiting séance can prompt on its next turn.
- « Plus tard » must stay a visible button on step 1 — `e2e/lib/goto.ts` `dismissPostVisitPrompt` finds it by name.
- Absent / Venu buttons are disabled while the write is in flight (no double « Absent »).
