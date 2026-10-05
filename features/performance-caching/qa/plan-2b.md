# QA plan — the shared reference cache (Part 2b)

**Change under test:** one TanStack Query cache (`lib/query/`) for the clinic status, the act and medication
catalogues and the bell's count; invalidated by the realtime broadcasts; emptied on a user change. Plus the
patient-page file-list fix (2.4).

**Environment:** as `plan-2a.md` — branch API `:5099`, worktree `next dev` `:3099`, own Chrome; baseline read-only
on the peer's `:3000` → `:5000`. Requests are counted with Playwright's `request` event on the API origin.

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| C-0 | baseline | browser | old code: load `/appointments`; open « Nouveau rendez-vous » twice | `user-status` and `procedure-types` request counts — recorded |
| C-1 | A | browser | new code: load `/appointments` | `/clinics/user-status` requested **once** |
| C-2 | A | browser | open « Nouveau rendez-vous », open the acts picker, close; repeat | the picker lists acts both times; `/procedure-types` requested **at most once** over the two opens |
| C-3 | A | browser | sidebar → « Patients » → « Rendez-vous » (client-side) | **no** new `user-status` and **no** new `unread-count` request; the bell still shows its number |
| C-4 | D | browser + api | tab B (secretary) keeps the dialog's acts picker open; an act is created over the API (admin), then deleted | B's picker offers the new act within 10 s, **no reload**; it is gone after the delete + a reopen |
| C-5 | D | browser + api | tab B on the patient page, « Fichiers » tab; a file is uploaded over the API, then deleted | B's list shows the file within 10 s **without a reload** (it did not before), and drops it after the delete |
| C-6 | B | browser | sign out, sign in as the secretary in the same tab | `user-status` requested again (nothing served from the previous user); the rail shows no « Factures » |
| C-7 | D | browser | smoke: patient page and odontogram, invoice form open | no « n'a pas pu être chargée » notice; the invoice form's act list is non-empty |
| C-8 | C | — | « Mode discret » toggled on another tab | ⏭ not exercised: a clinic-wide switch on a shared database with a live peer, and showing it again spends a TOTP code; the provider's state machine is unchanged and only its fetch moved |
| C-9 | C | — | a new cabinet right after `/setup` | ⏭ not exercised: needs a fresh account; `hasClinic: false` has `staleTime: 0` by construction |

**Widths:** none — no layout changed. **Mutations:** C-4's act and C-5's file, both created and deleted by the
walk (and swept by SQL if it dies between the two).
