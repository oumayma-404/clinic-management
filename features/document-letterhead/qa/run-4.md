# QA run 4 — after the owner's report: a 449 px framed paper was blocked, and its frame was cut

**Date:** 2026-10-07 · **Plan:** `plan.md` (B1 changed, B1b and C5–C5d added) · **Driver:** `walk.mjs`, whole plan
**Why:** the owner imported their real paper (`iiii.png`, 449 × 613, a purple frame round the page) and met
« Image trop petite (449 px…) » — then asked for the whole page as an option, since cutting bands broke the frame.
**Changes under test:** small images enlarged + warned (refused below 400 px only) · the frame ignored when guessing the
lines · suggestions clamped to the server's caps · « Page entière » (a third band: the strip between the two, stretched
behind the text) · migration `AddClinicLetterheadBody`.
**Environment:** API :5000 rebuilt and restarted by this session (PID 42240, lease gen 56), migration applied at startup;
`next dev` :3000 (mine). The owner was signed in on the same clinic in a hand-over Chrome; the walk saved their current
letterhead's keys first and put them back at the end (SQL, our own artefact).
**Widths:** 1440 × 900, 320 × 640, 1536 × 730.

## Result

36 ✅ · 1 ❌ · 2 ⏭ — every row of run 3 still green except:

| ID | Result | Evidence |
|----|--------|----------|
| C1c | ❌ | 320 × 640: the cut page ends at 555, buttons at 536 — the new « En-tête et pied de page / Page entière » row (two lines at 320 px) pushed it down and the page's 14rem minimum kept it from shrinking |
| B1 | ✅ (changed) | 900 px photo enlarged, « Image de faible résolution (900 px) … », aperçu 200 |
| B1b | ✅ | 300 px image refused « … le texte de l'en-tête serait illisible » |
| C5 / C5b | ✅ | framed paper opens on « Page entière »; page fits at 1440 × 900 with the warning |
| C5c | ✅ | saved with a body key; 4-page ordonnance: header + frame strip + footer on every page |
| C5d | ✅ | the note d'honoraires carries the frame |
| A9 / D3 | ⏭ | unchanged reasons (Word withdrawn app-wide; the QA clinic's blank name) |

**Status: RED (1 finding — C1c, minor, a regression of this change)**

## Fixes applied

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| C1c | confirmed (regression) | the choice row adds ~3 rem above the page; at 320 × 640 the page's 14rem floor no longer fits | `letterhead-import-dialog.tsx` — floor 12rem | one dialog |

**Gates:** check:responsive ✅ 77/77 · tsc ✅ · dotnet suite 4 988 ✅ · **Re-run:** run-5.md
