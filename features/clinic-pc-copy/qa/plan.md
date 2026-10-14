# QA plan — « Retirer ce PC » asks for an authenticator code (review finding 10)

**Stack (isolated, nothing shared):** the relay e2e cloud (`RT_PREFIX=rtb RT_STOP_AFTER=paired RT_PUBLISH=5098
RT_FRONTEND_URL=http://localhost:3099 RT_KEEP=1 e2e/relay-roundtrip/run.sh`) — its own Postgres, a real PC de secours
paired as « PC-CI » — plus the worktree's `next dev -p 3099` pointed at `http://127.0.0.1:5098/api`, and a
playwright-core Chrome with its own context. Account: the run's `rt.admin@cabinet-roundtrip.tn` (`.out-rtb/bootstrap.txt`).

**Diff under test:** `web/components/relay/relay-card.tsx` (confirm → StepUpDialog → retire), `web/lib/api/relay.ts`
(`retire(stepUpToken)`), `web/lib/api/client.ts` (`apiDelete` step-up header; unsettled-write listeners),
`web/lib/hooks/use-carried-draft.ts` (`switchRefusal`).

| ID | Tier | Scenario | Expected (observable) | Layer |
|---|---|---|---|---|
| A1 | A | `/settings#pc-de-secours` as the admin | card shows « PC-CI » and a « Retirer ce PC » button | browser |
| B1 | B | « Retirer ce PC » → confirm → « Annuler » | confirm `alertdialog` titled « Retirer « PC-CI » ? » closes; no step-up dialog; SQL: `RetiredAtUtc` null | browser + sql |
| B2 | B | confirm → « Retirer ce PC » → step-up « Annuler » | step-up dialog « Confirmer votre identité » with purpose « Vous allez retirer le PC de secours : sa copie s'arrête. » and a code field (no password field); closing it retires nothing (SQL) | browser + sql |
| B3 | B | confirm → step-up → wrong code `000000` → Confirmer | dialog stays open, red error banner, SQL: still not retired | browser + sql |
| B4 | B | `DELETE /api/relay` with no `X-Step-Up-Confirmation` | 403 | api (covered by `drive.mjs retire` in the round trip) |
| C1 | C | step-up dialog at 320×640 and 1536×730 | « Confirmer » and « Annuler » fully inside the viewport, no horizontal overflow | browser (screenshot, read) |
| D1 | D | `client.ts` + `use-carried-draft`: open « Modifier » on a patient, change the city, Enregistrer | success toast, SQL: the city persisted (no switch refusal on an ordinary save) | browser + sql |
| D2 | D | the card's other step-up: « Déclarer perdu ou volé » → confirm → its step-up opens with ITS purpose → Annuler | purpose is not the retire one; nothing declared (SQL) | browser + sql |
| A2 | A | confirm → step-up → correct code → Confirmer | toast « PC de secours retiré. »; card no longer offers « Retirer ce PC »; SQL: `RetiredAtUtc` set + journal row « PC de secours retiré » | browser + sql |
| X1 | — | the switch refusal naming a save « maybe already saved » (#14) | ⏭ needs the Windows/Android shell and a real server switch | — |

Order: B1 → B2 → B3 → C1 → D1 → D2 → A2 (A2 is irreversible, so last).
