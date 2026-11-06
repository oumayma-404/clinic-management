# QA run 2 — Nouveautés par e-mail depuis la console

**Date:** 2026-11-06
**Cycle:** 2 of at most 3 (re-run after F-1's fix)
**Plan:** features/console-news-broadcast/qa/plan.md (25 rows: A7 B7 C6 D5 — C6 and D5 added for F-1)
**Driver:** features/console-news-broadcast/qa/walk.mjs + api-rows.mjs — same files, two rows added, none removed
**Environment:** API :5094 / console listener :5494 unchanged since run 1 (backend untouched by the fix); console
:3094 rebuilt and restarted with the fix (built CSS checked); DB `clinic_news_qa`; Mailpit `@news-qa.test`
**Peer sessions live during the run:** clinic-management-be (busy), clinic-management-77, clinic-management-29 — none
on these ports or this database
**Widths looked at:** 320 · 390 (coarse pointer) · 820 · 1180 · 1440 · 1536×730
**Status:** GREEN (22 ✅, 3 ⏭ with reasons, 0 ❌)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Envoyer à 2 cabinets » |
| A2 | ✅ | kicker, heading, 🚀/📅 rows, closing, logo decoded |
| A3 | ✅ | « E-mail de test envoyé à qa@news-qa.test. »; 1 « [Test] » mail to qa@ |
| A4 | ✅ | « 2 cabinets · 3 adresses » + « 1 cabinet sans adresse »; « Envoi en cours vers 3 adresses. »; form reset |
| A5 | ✅ | exactly 3 mails: a@, b2@, B@News-QA.test (≡ b@) |
| A6 | ✅ | history row « 3 / 3 envoyés », not in progress |
| A7 | ✅ | HTML + text parts carry kicker, emoji rows, closing, cabinet note, `/login` button |
| B1 | ✅ | replay → `alreadyQueued`, still 3 mails, 1 row |
| B2 | ✅ | 5 × 400 `news_invalid`; history 2 → 2 |
| B3 | ✅ | `<b>`/`<i>` escaped |
| B4 | ✅ | both sends disabled with no text |
| B5 | ✅ | move / delete / cap 12 / last row undeletable |
| B6 | ⏭ | UI caps at 300 (`maxLength`); server refusal is B2 |
| B7 | ⏭ | SMTP-not-configured → unit test in `/test-small-feature` |
| C1 | ✅ | 320: no overflow, preview under form, cards |
| C2 | ✅ | coarse pointer: select 44 px, 9 buttons ≥ 44 px (unchanged by the `:where` fix) |
| C3 | ✅ | 1536×730: buttons end at 465 px; shots/C3-confirm-1536x730.png |
| C4 | ✅ | 390/820 cards, 1180/1440 table, no overflow |
| C5 | ✅ | `{clinics:2, recipients:3, clinicsWithoutAdminEmail:1}` |
| C6 | ✅ | « X » `absolute` at the panel's top-right; panel ends at its footer |
| D1 | ⏭ | byte-identical existing e-mails → unit assertion in `/test-small-feature` |
| D2 | ✅ | `/cabinets` header wraps at 320 |
| D3 | ✅ | `state=active` → Essai, Payant, Sans Admin |
| D4 | ✅ | `/journal`, `/retours` render |
| D5 | ✅ | login « eye » inside the password field; shots/D5-login-eye.png |

## Findings

None.

## Observations (off-plan)

- `/login`'s footer still says the console reads « le total encaissé par le cabinet sur le mois » — the console no
  longer knows a cabinet's takings (`platform-console` notes). Pre-existing wording, not chased.
