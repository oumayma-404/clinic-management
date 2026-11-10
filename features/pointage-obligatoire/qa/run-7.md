# QA run 7 — Pointage obligatoire (part 3) · « Ajouter une borne » chooses the device (DEV-12)

**Date:** 2026-11-06 (13:00 Tunis)
**Why:** the owner's change — the admin no longer turns *her own* computer into the borne; « Ajouter une borne » lists
the cabinet's signed-in devices and the chosen one claims the borne (`progress.md` DEV-12). Whole plan re-run.
**Plan:** `qa/plan.md` — A2 re-stated for the new dialog; 7 rows added (A19, A20, B15, B16, B17, C10, plus A2's new
step); no row removed. Same `walk.mjs`, extended (`QA_DB`, `QA_PORTS`).
**Environment:** the demo stack — API :5096 (worktree, Debug, restarted after the migration; last migration
`20261106115045_AddBorneAssignedToDevice`, applied to `clinic_pointage_demo` only) · web :3096 `next dev` · Chrome
**9335 / 9336** (fresh profiles; the owner's demo windows on 9333 / 9334 untouched) · MinIO bucket
`clinic-demo-pointage`. The owner's two congé requests were set aside (`qa_keep_leaves`) before the walk's arrange and
restored after.
**SQL used for arrange / own artefacts (stated):** the walk's usual arrange; B16 retires and restores the walk's own
claimed bornes; B15 ends its own second session (`SessionFamilies.EndedAtUtc`).
**Status:** RED (1 finding)

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| A1 | ✅ (re-driven) | first drive threw `page.goto` timeout at 30 s — the first request to `/settings` after the edit compiled the page (environment, not product); re-driven alone: label + ON by default |
| A2 | ✅ | « Ajouter une borne » → « Cet appareil » → « Accueil » → step-up → « Cet appareil est maintenant la borne « Accueil » » |
| A3–A18, B1–B14, C1–C9, D1–D5 | ✅ | unchanged from run 6 (151 checks) |
| **B17** | ✅ | no device picked → « Choisissez l'appareil qui sera la borne. », no step-up |
| **A19** | ✅ | her row « Ines Gharbi · Chrome sur Windows · actif il y a moins d'une minute »; the admin's own session only as « Cet appareil »; toast « Borne « Accueil 2 » envoyée à l'appareil de Ines Gharbi. »; SQL waiting for her session — `shots/A19-add-borne-pick-device.png` |
| **A20** | ✅ | her open screen, untouched: toast « Cet appareil est maintenant la borne « Accueil 2 » »; SQL claimed, description kept; HttpOnly cookie on her context, path `/api/pointage`; admin list drops « En attente » by itself; her `/pointage` reads « Borne Accueil 2 » — `shots/A20-*.png` |
| **B15** | ✅ | « Salle 3 » for a second session: « En attente de l'appareil · Ines Gharbi · Firefox sur Windows »; not in her bornes; only that session hears `borneWaiting`; ended → « Appareil déconnecté … » in red, not offered any more, « Retirer » clears it |
| **B16** | ✅ | only a waiting borne → `locked:false`, no borne named, write not 423; restored → locked |
| **C10** | ❌ (probe ✅) | 1536×730: fits, footer on screen. **320×568: the device list scrolls sideways** — `shots/C10-add-borne-320x568.png`; the probe measured the dialog element, not its scrolling body, and passed |

Widths looked at: 320×568 · 560×760 · 812×380 · 820×600/1180 · 1000×830 · 1440×900 · 1536×730.

## Findings

### F1 — minor · « Ajouter une borne » scrolls sideways at 320 px

- **Repro:** Paramètres → Équipe → « Ajouter une borne » at 320×568.
- **Expected:** each device line truncates inside the dialog (§ 11: no sideways scroll).
- **Observed:** a horizontal scrollbar under the list; « Chrome sur Windows · actif il y a 1 minu… » runs past the edge.
- **Cause (source):** `ui/radio-group.tsx` is a `grid`; its one implicit track is sized by the widest row's
  min-content, and a `truncate` line makes that the whole string — the `RecordSection` trap in `CLAUDE.md`. The
  `<label>` rows carry no `min-w-0`.
- **Owner:** `web/components/team/attendance-settings-card.tsx` — `DeviceOption`'s `<label>`.
- **Probe:** C10 measured `scrollWidth` on the dialog; the scroller is `[data-slot="dialog-body"]`. Fixed in `walk.mjs`.

## Observations (off-plan)

- Old sessions read « Appareil sans nom »: they signed in before the device name was sent on every sign-in. The walk's
  own API logins add three such rows. Expected; a new sign-in names the device.
- `B15-device-signed-out.png` shows the top of the page (the row was off screen when shot); the check read the row's
  text. Probe now scrolls the row into view first.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | `ui/radio-group.tsx` is a grid and a `truncate` row with no `min-w-0` sized its one track to the whole line | `web/components/team/attendance-settings-card.tsx` (`min-w-0` on `DeviceOption`'s label) | 1 component; `ui/radio-group.tsx` untouched (its other callers hold no truncated line) |
| C10 probe | probe bug ×2 | measured the dialog, not its scrolling body; and measured a background tab, whose bottom sheet never ran its slide-in | `qa/walk.mjs` (scrollers only; `bringToFront` before measuring) | walk only — **red-proved**: without the fix C10 reads `over: 84`, with it 0 |
| B15 shot | probe cosmetic | shot taken with the row off screen | `qa/walk.mjs` | walk only |

**Gates after the last edit:** check:responsive 86/86 ✅ · tsc ✅ · unit suite 6743 ✅ (unchanged since; no backend edit).
**Re-run:** run-8.md.
