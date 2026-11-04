# Story 3: Full - Nothing in the demo misbehaves around the read-only rule

**Status:** APPROVED
**Story Status:** done
**Layer:** Full (vertical slice — see README)
**Depends On:** 2
**Blocks:** 7

## Objective

No screen nags, asks for a security code, reads a file only to refuse it, shows other visitors' devices, reloads
when someone arrives, or sends a request the visitor did not ask for. Every row of spec FR-4 behaves as written.

## Acceptance Criteria

_From spec:_

- [ ] FR-4 table, every row (popup, Abonnement, Sécurité, code-gated actions, password change, logout, Mode discret, exports, uploads, dashboard « Personnaliser », bell, staff chat, booking prompts, install)
- [ ] AC-3.3c optimistic controls return to their stored state after a refusal
- [ ] EC-3 no visitor's arrival reloads another's screen; EC-6 booking prompts from the save are not shown

_Story-specific:_

- [ ] No request on opening a patient's Fichiers, a chat thread, or at sign-in
- [ ] `check:responsive` `demo-upload-guard` holds the 10 file inputs

## Entry Criteria

- [ ] Story 2 implemented (gate + refusal primitives)

## Steps

1. **Uploads refused at pick**
   - `web/lib/demo/refuse-in-demo.ts` `refuseUploadInDemo(isDemo)`; call it first in the 10 handlers:
     `patient-files-manager.tsx` (input + drop), `patients/files/upload-queue.tsx` (enqueue),
     `resume-uploads-notice.tsx`, `clinic-settings.tsx` (logo), `setup-wizard.tsx`, `mon-profil-content.tsx`,
     `doctor-document-identity-dialog.tsx`, `letterhead-import-dialog.tsx`, `import-patients-dialog.tsx`,
     `backup/clinic-archive-card.tsx`. Add the `demo-upload-guard` rule.
2. **Quiet paths skip the request on `isDemo`**
   - default folders (`patient-files-manager.tsx:225-240`), staff-chat mark-read (`use-staff-chat.ts`), web-push
     register (`web-push.ts`), notification mark-read (`use-notifications.ts`). Dismiss / clear-all keep the request.
3. **Code-gated actions**
   - `components/security/step-up-dialog.tsx`: demo notice in place of the code field.
4. **Side doors**
   - `change-password-form.tsx`: demo notice on submit, no request.
   - Sécurité « Activer maintenant » / « Remplacer »: demo notice instead of navigating to `/login?enrol|replace`.
5. **Withdrawn / filtered**
   - `post-visit-review-popup.tsx`: null on `isDemo`.
   - `GetMySessionsQuery`: caller's family only when `RefusesWrites`; « Mes appareils » hides « Déconnecter ».
   - `ClinicHub`: no presence `StaffChatChanged` when `RefusesWrites`.
6. **Optimistic controls**
   - Check agenda drag, tooth mark, status quick action, stock ±, dashboard toggle each roll back on error; fix any
     that do not.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `web/lib/demo/refuse-in-demo.ts` | pick-time guard |

### Files to Modify

| File | Changes |
|------|---------|
| the 10 upload components above | guard first |
| `patient-files-manager.tsx`, `use-staff-chat.ts`, `web-push.ts`, `use-notifications.ts` | skip on demo |
| `step-up-dialog.tsx`, `change-password-form.tsx`, `app/securite/page.tsx`, `my-devices-card.tsx` | notices |
| `post-visit-review-popup.tsx` | withdrawn |
| `GetMySessionsQuery.cs`, `ClinicHub.cs` (+ tests) | own session only, no presence |
| `web/scripts/check-responsive.mjs` | `demo-upload-guard` |

## Verification Steps

- [ ] API suite + web gate green
- [ ] `/test-in-browser` planned pass: every FR-4 row, desktop + 390 px + coarse emulation
- [ ] Network log: zero requests on Fichiers open / chat thread / sign-in
- [ ] Two visitors: « Mes appareils » shows one row each; second entering does not reload the first's chat

## Exit Criteria

- [ ] All ACs checked; gates green; committed; README status → `implemented`
