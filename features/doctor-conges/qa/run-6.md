# QA run 6 — after the owner's call on the secretary's congé list

**Date:** 2026-10-20 · same plan + `walk.mjs`, one new row · isolated stack (API :5099, web :3098, DB `clinic_conges_qa`).

| Row | Proves | Result |
|-----|--------|--------|
| RV-all-leaves | « ⋯ » → « Tous les congés » (1440) and the phone panel's entry (390) open a sheet listing every practitioner with their congés | ✅ 2 practitioners, Salma's congé listed at both widths, no overflow |

Probe fix in the same run: the phone panel opener is the title button (`hasText: /nov/`), not the first `aria-expanded` (that was the bell).
Product fix found by the eye pass: the « ⋯ » popover stayed open behind the sheet — it now closes when the list opens.

**Result: 53 ✅, 0 ❌, 0 ⏭ — GREEN.** Gates after the last edit: check:responsive 81/81 · tsc · build.
