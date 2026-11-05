# QA run 1 — Guided first days, release 1 (parts A + B)

**Date:** 2026-11-03
**Cycle:** 1 of at most 3
**Plan:** features/guided-first-days/qa/plan.md (34 scenarios: A18 B8 C5 D8)
**Driver:** features/guided-first-days/qa/walk.mjs (final drive: `walk-run1e.log`)
**Environment:** isolated — api :5097 (`dotnet run` from the worktree, started 13:4x, owned by me) · web :3097
`next dev` from the worktree (owned by me) · database `clinic_gfd_qa` (throwaway, all migrations incl.
`AddOnboardingSignInColumns`) in the shared `clinic-postgres` · mail through the shared `clinic-mailpit` ·
`Clients__StoreUrls__Android` set so the phone QR has a link · own `playwright-core` Chrome (no MCP profile).
Ports 5099/5098/3098 belonged to a peer and were not touched.
**Peer sessions live during the run:** clinic-management-29 (busy — demo-cabinet stack on :5099/:5098/:3098),
clinic-management-77 (shell), anakin-8f / anakin-21 / career-ops-f5 / career-ops-83 (idle) — from `ListAgents`
**Widths looked at:** 320 · 390 · 820 · 1180 · 1440 · 1536×730 (screenshots `Read`, not just captured)
**Status:** RED (2 findings: 2 minor)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | heading, 2 store links (`target=_blank`), « Voir la vidéo · 0:53 » → `watch?v=wQ-WNx39U50`; shots/A1-verifier-1440.png, A1-verifier-390.png |
| A2 | ✅ | welcome e-mail carries « Vidéos : vos premiers pas » + `https://apexa.tn/tutoriels.html` (mailpit) |
| A3 | ✅ | first QR, no notice; shots/A3-first-qr.png |
| A4 | ✅ | « Même QR code que tout à l'heure. Déjà scanné ? … », same key; shots/A4-same-qr.png |
| A5 | ✅ | after a reload: same key, same notice |
| A6 | ✅ | `ProtectedTotpSecret`, `TotpEnrolmentStartedAt`, `TokenVersion` identical A3→A5 (SQL) |
| A7 | ✅ | codes screen → sign-in with a fresh code → dashboard |
| A8 | ✅ | « Votre cabinet en 7 étapes », « 1 sur 7 faite », « Essai gratuit · 29 jours », next step `data-variant=default`, others `outline`; shots/A8-card-1440.png |
| A9 | ✅ | 5 named ▶ (`Vidéo : …`), none on « Votre téléphone » |
| A10 | ✅ | QR `data:image/png`, iPhone steps name `localhost:3097`, « Vous · pas encore »; shots/A10-phone-dialog-1440.png |
| A11 | ✅ | `PhoneAppPlatform=android` (SQL); read names « Vous · Android »; card ticks on return, « 2 sur 7 faites » |
| A12 | ✅ | `/users`, « Créer un compte » open, « Secrétaire », no `invite` in the URL; shots/A12-invite.png |
| A13 | ✅ | « Votre secrétaire » Fait, « 3 sur 7 faites » |
| A14 | ✅ | « Ajouter un patient » opens |
| A15 | ✅ | `aria-expanded=true`, card brought into view; shots/A15-en-tete.png |
| A16 | ✅ | card leaves, « Premiers pas masqués. » + « Annuler » restores it |
| A17 | ✅ | switch listed, off, « Carte, sous le bonjour »; on → card back |
| A18 | ✅ | card leaves + « Votre cabinet est prêt. » (fixture: `Clinics.CreatedAt` −31 d on the throwaway DB, reverted) |
| B1 | ✅ | `?enrol=1` alone: no « Plus tard » |
| B2 | ✅ | `&from=securite`: « Plus tard » present |
| B3 | ✅ | « Nouveau QR code : il remplace le précédent. … supprimez la ligne « Cabinet QA … » », new key (fixture: start time −25 h); shots/B3-replaced-qr-1440.png |
| B4 | ✅ | browser: « Rester connecté » `data-state=unchecked` |
| B5 | ✅ | `window.__clinicShell = { platform: 'windows' }`: `data-state=checked` |
| B6 | ✅ | browser sign-in left `PhoneAppFirstSignInAt` NULL |
| B7 | ✅ | doctor: no card, no switch; shots/B7-doctor-dashboard.png |
| B8 | ⏭ | not exercised — the stack runs WITH an Android link; the no-link sentence is one JSX branch |
| C1 | ✅ | verifier at 320: no horizontal scroll; shots/C1-verifier-320.png |
| C2 | ✅ | replaced notice at 320: no horizontal scroll; shots/C2-replaced-qr-320.png |
| C3 | ✅ / ❌ F-2 | no horizontal scroll and every control inside the card at 320/390/820/1180/1440; but see F-2 (layout) |
| C4 | ✅ | card starts above the fold at 1536×730; « Terminé » reachable; shots/C4-*.png |
| C5 | ✅ / ❌ F-1 | no overflow, « Terminé » on screen at 320; but see F-1 (link wraps mid-word) |
| D1 | ✅ | recovery code → « Remplacer maintenant » → « Nouveau QR code : il remplace le précédent. »; shots/D1-replace-after-recovery.png |
| D2 | ✅ | first enrolment completes and signs in |
| D3 | ⏭ | not exercised — `e2e/bootstrap.mjs` not run here; it reads `secret`, which is unchanged |
| D4 | ✅ | `verify-schema` identical before/after (2026-11-03, throwaway DB) |
| D5 | ✅ | A7 signs in with the box unticked; it ticks only in a shell (B4/B5) |
| D6 | ✅ | card survives « Tout afficher » / « Par défaut »; admin 19/22, doctor 18/21 |
| D7 | ✅ | admin and doctor dashboards render |
| D8 | ✅ | `/settings` without the hash: `aria-expanded=false` |

## Findings

### F-1 · minor · « télécharger l'application » breaks mid-word on a phone

- **Repro:** dashboard → « Votre téléphone » → « Installer », at 320 px.
- **Expected:** the link wraps between words.
- **Actual:** « Ou, depuis le téléphone : télécharger l'ap » / « plication ».
- **Evidence:** shots/C5-phone-dialog-320.png.
- **Suspected owner:** `web/components/dashboard/phone-app-dialog.tsx` — `break-all` on the link (it was meant for a
  raw URL; the link text is words).
- **Regression?** No — new with this change.

### F-2 · minor · « Masquer » drops to its own line under the card title on a phone

- **Repro:** dashboard as the admin at 320 or 390 px.
- **Expected:** « Masquer » stays at the top-right of the card, like at 820 px and up.
- **Actual:** the header wraps: title block, then « Masquer » alone on a line at the left, above the progress bar.
- **Evidence:** shots/C3-card-320.png, shots/C3-card-390.png.
- **Suspected owner:** `web/components/dashboard/getting-started-card.tsx` — the header row is `flex-wrap` and the
  title block has no shrink limit, so the button wraps instead of the text.
- **Regression?** No — new with this change.

## Observations (off-plan)

- At 1536×730 the enrolment screen's « Confirmer » sits below the fold once a notice is shown (the notice adds
  ~70 px). Enter in the code field submits the form, and the page scrolls; the screen's height is pre-existing.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| D1 | « Nouveau QR code » never appeared; screenshot on « Préparation… » | `getByText('Nouveau QR code')` is case-insensitive and matched the replace screen's own « vous obtiendrez un **nouveau QR code** à scanner », so the wait returned before the request did. Now matches the notice's full sentence (also in B3). Isolated re-drives and request logging confirmed the server answered in ~200 ms |
| — | a heredoc edit put a raw newline in a JS string | wrote the line with the Edit tool instead |
