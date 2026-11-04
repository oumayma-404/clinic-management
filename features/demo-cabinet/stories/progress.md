# Progress — public demo cabinet

| Story | Status | Session |
|---|---|---|
| 1 — Enter the demo | done | 2026-11-03 (per-story tests skipped: no approved test plans) |
| 2 — Saves refused kindly + band | done | 2026-11-03 (per-story tests skipped: no approved test plans) |
| 3 — Nothing misbehaves | done | 2026-11-03 (per-story tests skipped: no approved test plans) |
| 4 — Full cabinet | reviewed | 2026-11-03 |
| 5 — Spécimen PDFs | reviewed | 2026-11-03 |
| 6 — Nightly rebuild | reviewed | 2026-11-03 |
| 7 — demo.apexa.tn prepared | reviewed (2026-11-04) — server steps wait for the owner | 2026-11-03 |
| 8 — Essayer la démo | not-started | |

## Working tree note (start of session)
Worktree created this session off `origin/main` @ `66ecc657`; only `features/demo-cabinet/` untracked at start.

## Story 1 — Enter the demo

### Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `DeploymentProfile` ctor + `For()` | capability matrix | every `For()` arm, `DeploymentProfileTests`, `KeyRingProtectionTests` | must change — all three existing arms + tests updated |
| 2 | `PermitsOsPush` | kind switch, `_ => throw` | startup (`Program.cs:1412`) | must change — PublicDemo arm, else boot throws |
| 3 | `LocalDataProtection` kind checks | 2 × `Kind == HostedMultiTenant` | `SchemaVerificationReader` key-ring check | must change → `ProtectsKeyRingWithCertificate`; same answers for the 3 old kinds |
| 4 | `OutboundEndpointPolicy` | kind switch, `_ => false` | reminder settings writes | unaffected — explicit arm added, same answer |
| 5 | `ILocalAuthService.GenerateRefreshToken` | new overload | 3 callers + 10 test files mocking it | unaffected — additive overload, old signature unchanged |
| 6 | `RefreshTokenCommandHandler` ctor | new `IDemoPolicy` param | 1 test file (2 construction sites) | must change — updated |
| 7 | `GET /api/auth/mode` | 2 new optional fields | `AuthModeDto`, `SelfRegistrationGateTests` | unaffected — additive, read `=== true` |
| 8 | anonymous `POST auth/demo-session` | new anonymous door | `ControllerAuthorizationCoverageTests`, `RelayOnlineOnlyCoverageTests`, `SubscriptionExemptionCoverageTests`, `SecondFactorCoverageTests` | must change — classified in each |
| 9 | `DemoCabinetRebuilder` (`UseSystemWide`) | new background path | `RelayFenceCoverageTests`, `SystemWideCallerCoverageTests` | must change — reviewed entry added |
| 10 | `PUBLIC_ROUTES`, `HIDDEN_PATHS` | route lists | middleware, 3 session guards, banners | unaffected — one additive entry each |
| 11 | `LocalSessionProvider.logout` target | session end navigation | every logout / expiry path | must re-test — only changes when `isDemo`; QA B3/B4 |

### Steps
- [x] 1 Kind + capabilities (+ tests)
- [x] 2 `IDemoPolicy` / `DemoPolicy`
- [x] 3 2 h session cap (refresh + token overload)
- [x] 4 Locator + mint + endpoint + coverage guards
- [x] 5 Minimal rebuilder in the API host (first boot / `Demo:RebuildOnStart`)
- [x] 6 Web entry (`DemoProvider`, `/demo`, BFF route, closed doors, session end)

### Auto-Approved Deviations
| Deviation | Classification | Reason |
|---|---|---|
| Dr Bing, Rachel Green and the letterhead move from story 1 to story 4 | Trivial (scope order) | story 1 needs only the dentist a visitor enters as; the team belongs with the scenario |
| `Demo:SessionMinutes` (default 120, 1–1440) | Trivial | lets QA expire a session in one run; the capability itself reads no config |

### DEV-1: `demoBuiltOn` not on `GET /api/auth/mode`
**Story:** 1 · **Category:** Technical
**Original Plan:** `mode` returns `demoBuiltOn`.
**Actual:** left out; `mode` stays synchronous.
**Justification:** eight existing tests call `GetMode()` directly; an async read with a DB query on the one anonymous probe every page makes is better added where it is used — story 6, as its own operator read.
**Impact:** story 6 adds it. **Approved:** auto (moves within the plan).

### DEV-2: `demo_visit` cookie (QA F1)
**Story:** 1 · **Category:** Technical
**Original Plan:** session end → `/demo?expired=1`.
**Actual:** also a non-secret `demo_visit` cookie (24 h), set on entry, cleared on a deliberate logout.
**Justification:** the session cookie expires with the session, so after the cap a returning visitor arrived with no session and looked new; the marker is the only fact left that says « was here ».
**Impact:** none outside the demo. **Approved:** auto (needed to meet spec FR-3 / EC-1).

### DEV-3: fixed a date bomb in `FreeSlotFinderTests` (pre-existing on `main`)
**Story:** 1 · **Category:** Other
**Actual:** the two query tests used a fixed Monday (2026-11-02) while the handler reads the real clock and never offers a past start; they now use the next Monday after the clinic's today.
**Justification:** it turned the unfiltered suite red today, unrelated to this feature.
**Approved:** auto (test-only, no product change).

### Gates (after the last edit)
- API: full unfiltered `dotnet test -c Release` (temp `BaseOutputPath`) → **6 077 passed, 0 failed**, 6 skipped. 0 new warnings in changed files (`LocalDataProtection.cs` CA1416 at :189 is pre-existing).
- Web: `check:responsive` 81/81 · `tsc --noEmit` clean · `npm run build` green.
- Isolated stack (`:5099` PublicDemo on `clinic_demo_local`, `:3099`): QA `qa/run-1.md` RED (2) → fixed → `qa/run-2.md` **GREEN** (21 ✅, 1 ⏭).
- Server probes: `mode.isDemo = true`; 3 entries → 3 distinct untrusted families, expiry +2 h; `LastLoginAt` NULL; demo signing key ≠ main dev key.
- Widths looked at: 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 (PNGs in `qa/shots/`).

### Learnings
- A `bash` assignment in quotes (`"A=b" cmd`) is not an assignment — it runs `A=b` as a command (exit 127). Use `env "A=b" cmd`.
- A session cookie whose lifetime equals the session leaves no trace to tell « expired » from « new ».

### Review (2026-11-03) — `reviews/story-1.md`, 100/100
- Fixed: access token now capped at the session end too (`GenerateToken(…, notAfterUtc)`); no-op memo removed; `DemoSessionLifetimeTests` added.
- Gates after fixes: 6 087 passed / 0 failed · check:responsive 81/81 · tsc · build.

## Story 2 — Saves refused kindly + band

### Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `DemoReadOnlyMiddleware` in `Program.cs` | new pipeline step, every request | every API call on every profile | unaffected off the demo — returns at `RefusesWrites == false` before reading anything (unit-tested) |
| 2 | `[AllowedInDemo]` / `[RefusedInDemo]` on 20 actions | metadata only | the actions themselves | unaffected — read only by the demo gate |
| 3 | `ApiErrorCode.DemoReadOnly` + message swap in `client.ts` | error dispatch | every API caller | unaffected — keyed on a code only the demo sends |
| 4 | `FormErrorBanner` | shared primitive, ~52 call sites | every dialog | must re-test — new branch only on the exact demo sentence; ordinary error unchanged (QA D-rows) |
| 5 | `showErrorToast` | shared toast, ~200 call sites | every error toast | must re-test — new branch only on the demo sentence |
| 6 | 42 hand-rolled `toast.error(err.message)` → `showErrorToast` (21 files) | write-path toasts | caisse, lab, séries, attente, factures, stock, médicaments, odontogramme, réglages, patient, documents, utilisateurs | must re-test — same text, now 8 s instead of 4 s on 6 of them; guard `server-error-toast-has-one-owner` |
| 7 | `app-shell.tsx` + `DemoBand` | shell column | every in-app screen | unaffected off the demo — renders null unless `isDemo === true` |
| 8 | `DemoProvider` → `rememberDemoSignupUrl` | module state for the toast | toast only | unaffected |

### Auto-Approved Deviations
| Deviation | Classification | Reason |
|---|---|---|
| Demo refusal recognised by the client-replaced message (identity), not by `useConflict` keeping the error | Trivial | `must_change_password` precedent; no call site changes; `useConflict` untouched |
| `MedicalDocuments.PreviewFicheOrdonnance` + `GeneratePdfForDownload` allowed; `Backup.GetFileManifest` + 8 more CSV exports refused | Trivial (spec FR-2 wording) | the story listed 5 refused reads; FR-2 says every CSV export and the archive, and lists PDF + aperçu as allowed |
| Band is an `aside` landmark, no `role="status"` | Trivial | the shell remounts per navigation; a live region would re-announce (spec: announced once) |

### DEV-4: hand-rolled error toasts swept into `showErrorToast`
**Story:** 2 · **Category:** Scope (in-scope masking defect)
**Original Plan:** banner + `showErrorToast` recognise the refusal.
**Actual:** also converted 42 `toast.error(err.message …)` write paths (21 files) and added `check:responsive` `server-error-toast-has-one-owner`.
**Justification:** without it AC-3.3 fails on 42 saves — the demo sentence painted as a red error; story 3 does not cover it.
**Impact:** same wording in production; duration 4 s → 8 s on 6 toasts (the documented intent of `showErrorToast`). **Approved:** auto (needed to meet AC-3.3).

### Known, left to story 3
- Odontogram per-tooth failure toast (`odontogram.tsx:1667`) is a literal « Échec de l'enregistrement du diagnostic. » — story 3 step 6 (optimistic controls) owns the tooth mark.
- Load-failure toasts (`toast.error(message)` after a GET) untouched: a GET is never refused.

### Gates (after the last edit)
- API: full unfiltered `dotnet test -c Release` (temp `BaseOutputPath`) → **6 109 passed, 0 failed**, 6 skipped.
- Web: `check:responsive` 83/83 (2 new rules, both red-proved with a throwaway probe) · `tsc --noEmit` clean · `npm run build` green.
- Isolated stack QA: `qa/run-s2-1.md` **GREEN** (17 ✅, 1 ⏭: no hosted stack) + story-1 walk re-run green.
- SQL: 0 `IdempotencyRecords` after the walk.
- Widths: 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730; coarse 390.

### Learnings
- A one-shot `isVisible()` right after `waitForURL` reads before render; wait for the text.
- `python -` in a bash chain waits on stdin and hangs the whole command.

### Review (2026-11-03) — `reviews/story-2.md`, 100/100
- Fixed: the hand-out source scan no longer skips a controller in another file or a wrapped signature; an unfound read fails the test.
- Gates after fix: 6 109 passed / 0 failed; 0 new warnings (baseline 72). Web untouched by the review.

## Story 3 — Nothing misbehaves

### Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `StepUpDialog` demo body | shared dialog, 5 callers (exports, Mode discret, archive, PC de secours ×2) | all five | unaffected off the demo — branch on `isDemo` only; must re-test one caller on the demo |
| 2 | 10 upload handlers + `refuseInDemo` | pick-time guard | upload queue, logo, cachet, letterhead, CSV import, archive restore | unaffected off the demo — `refuseInDemo(false)` returns false; guard `demo-upload-guard` |
| 3 | `useMarkThreadRead`, `useNotifications.markRead/markAllRead` | background writes | chat thread, bell | unaffected off the demo; dismiss / clear-all untouched (they refuse with the notice) |
| 4 | `subscribeWebPush` / `askNotifications` | push | sign-in resume, chat alert settings | unaffected off the demo — cached `loadAuthMode` read |
| 5 | `GetMySessionsQueryHandler` ctor (+ `IDemoPolicy`) | « Mes appareils » | DI only (no test built it) | must change — filtered on `RefusesWrites`, 3 new tests |
| 6 | `ClinicHub` ctor (+ `IDemoPolicy`) | realtime presence | `ClinicHubTests` (2 sites), `ClinicHubTenantScopeTests` | must change — updated + demo test; dependency reviewed |
| 7 | `DOORS_CLOSED_ON_THE_DEMO` minus `/change-password` | story-1 list | password form | must change — FR-4 wants the form visible; save refused in the form |
| 8 | `PostVisitReviewPopup` wrapper | header mount | header | unaffected off the demo — same component once `isDemo === false` |
| 9 | `MyDevicesCard` « Déconnecter » | security page | — | unaffected off the demo |
| 10 | odontogram multi-tooth save | chart | — | unaffected off the demo |
| 11 | staff-chat composer + thread `send` | chat | quick messages | unaffected off the demo |

### Steps
- [x] 1 Uploads refused at pick (10 handlers) + `demo-upload-guard`
- [x] 2 Quiet paths: default folders, chat mark-read, bell mark-read, web push, notification permission
- [x] 3 Security-code dialog shows the notice (covers exports, « Afficher l'argent », archive, PC de secours)
- [x] 4 Password form refuses in-form; Sécurité « Activer / Remplacer » refused in place
- [x] 5 Popup withdrawn; « Mes appareils » own session, no « Déconnecter »; hub presence off
- [x] 6 Optimistic controls: agenda drag (never optimistic), status quick action, stock ±, dashboard toggles already roll back; odontogram multi-tooth now refuses once before N requests; chat send refused before the bubble

### Auto-Approved Deviations
| Deviation | Classification | Reason |
|---|---|---|
| `/change-password` taken off the story-1 closed doors | Trivial (spec FR-4) | FR-4: « form visible; saving refused » |
| `DemoRefusalNotice` (banner primitive) + `refuseInDemo(isDemo, showInForm?)` | Trivial | one notice in a form, one toast otherwise; both inside the guarded owners |
| Chat send refused in the composer and the thread hook | Trivial | a failed bubble with « réessayer » reads as an error (AC-3.3) |
| App install not touched here | Trivial (plan D9) | done at the demo's Caddy block in story 7 |

### Gates (after the last edit)
- API: full unfiltered `dotnet test -c Release` (temp `BaseOutputPath`) → **6 113 passed, 0 failed**, 6 skipped.
- Web: `check:responsive` 84/84 (new `demo-upload-guard`, red-proved with a bare file input) · `tsc --noEmit` clean · `npm run build` green.
- QA: `qa/run-s3-1.md` RED (1 product: Mode discret disabled on the demo; 3 probe bugs) → fixed → `qa/run-s3-2.md` **GREEN** (story-3 walk + story-1 + story-2 walks re-run).
- Widths: step-up notice 390×844 · 1536×730; password notice 320×640; story-1/2 walks re-ran their own widths.
- Not exercised (with reason): chat send (no conversation in an empty cabinet), Fichiers / odontogram (no patient) — both return in story 4's QA; hub presence is unit-tested.

### Carried to story 4
- Re-walk with data: chat send refused before the bubble, Fichiers opens without creating folders, odontogram multi-tooth refused once.

### Learnings
- `isDemo !== false` (not `isDemo`) on anything that writes on its own: while the mode is unknown it must not write either.
- A control disabled for a reason the demo cannot fix (no authenticator) is a dead control there; refuse the press instead.

### Review (2026-11-03) — `reviews/story-3.md`, 100/100
- Fixed: letterhead picker guard kept a stale `isDemo` (null) in its `useCallback`; added the dep and `check:responsive` `demo-guard-reads-live-flag` (red-proved on the original defect).
- Gates after fix: check:responsive 85/85 · tsc · build. API untouched by the review.

## Story 4 — The cabinet is full of believable work

Split into three session-sized parts (decided this session, not asked):

| Part | Delivers | Status |
|---|---|---|
| A | seam (`IDemoSeedContext` / `DemoSeedContext`) · assets · team & settings · patients & diagnoses · agenda (−14…+14 working days, congé, series, waiting list) · fiches on past visits · bell cleared | done 2026-11-03 |
| B | treatments at every state · money (cheques, notes, expenses, one owing, one avoir) | done 2026-11-03 |
| C | documents (ordonnance + demande d'examens, certificat, arrêt de travail) · files (pano + photos) · stock / fournisseurs / lab (one late) · « suite à planifier » · locator ≥ 1 patient · verify-schema + reconcile-money · browser: every sidebar screen | done 2026-11-03 |

### Blast Radius (part A)
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `DemoCabinetRebuilder.Rebuild` | the one rebuild routine | `EnsureCabinet`, `Program.cs` enqueue | must change — runs the builder between deactivate and activate; a failed build leaves the admin off, so the locator keeps yesterday's cabinet |
| 2 | `ClinicManagement.Application.csproj` | embeds `Features/Demo/Assets/*.png` | email lockup resource | unaffected — additive item |
| 3 | `DemoScenario` | constants | rebuilder, `StartDemoSessionCommand` | unaffected — additive members; `EmailFor` replaces the rebuilder's inline address (same shape) |
| 4 | the real commands the builder sends (users, clinic, letterhead, doctor, patients, odontogram, appointments, series, leave, waiting list, fiches, notifications) | read-only use | — | unaffected — no command changed; the seed is just another caller |
| 5 | `DemoSeedContext` (`CreateScope`) | new background scope | `SystemWideCallerCoverageTests` (scans `CreateScope()`) | unaffected — declares `UseClinic` in the same file; suite green |

### Steps (part A)
- [x] 1 Seam: one scope per command, synthetic signed-in member (sub · clinic_id · role · email), `UseClinic`
- [x] 2 Assets: entête, pied, cachet embedded
- [x] 3 Team (Dr Bing with practitioner record, Rachel Green) · hours with lunch break · letterhead · cachet
- [x] 4 41 patients (Ross Geller as in the videos, two « denture mixte », one « temporaire », no phones) · 11 charted marks
- [x] 5 Agenda: 14 working days each side, today 3 per dentist, Dr Bing's congé, Ben Geller's weekly series, a waiting-list entry
- [x] 6 Fiches on every past visit but two (« À clôturer »), paid cash/card at the chair
- [x] 7 Bell cleared for the three members (every booking had raised « rendez-vous créé »)

### Auto-Approved Deviations (part A)
| Deviation | Classification | Reason |
|---|---|---|
| `DemoSeedContext` lives in the API host, not Infrastructure | Trivial (plan location) | it needs `IHttpContextAccessor` + `ISender` from the composition root; the rebuilder already lives there |
| Builder takes a `stamp` | Trivial | member addresses must be unique beside yesterday's cabinet, like Dr Geller's |
| Bell cleared at the end of the build | Trivial | 189 « rendez-vous créé » rows read as 99+ unread on a quiet cabinet |
| Two « Mixed » children (Ben, Marcel) and one « Child » (Emma) | Trivial | spec asks for at least one « denture mixte » |

### Gates (part A, after the last edit)
- API: full unfiltered `dotnet test -c Release` → **6 126 passed, 0 failed**, 6 skipped (`DemoCabinetBuilderTests`: 11 cases incl. Sunday / Saturday / 1 January anchors, hours via the product's validator, no raw clock).
- Real build on `clinic_demo_local` (PublicDemo, `Demo__RebuildOnStart`): **built in ~30 s**, 0 errors logged — 41 patients, 189 visits (96 with fiche), 48 tooth rows, 3 members, journal authored by Rachel 689 / Monica 297 / Chandler 219.
- `reconcile-money` → **0**. `verify-schema` → **2, one drift: `key-ring-protection`** — the demo kind protects its key ring with a certificate and the local run has none; environment, not the cabinet (the VPS carries the certificate — story 7). Every schema check otherwise ok.
- Browser (`qa/walk-4a.mjs`, GREEN): dashboard not empty, 41 patients, Ross's allergy/asthma, and the three story-3 carry-over rows — Fichiers opens with no write, odontogram multi-tooth refused once with teeth kept, chat send refused before the bubble with text kept; 0 writes beyond sign-in.

### Observations (for part C's walk)
- Agenda blocks show « non synchronisé » (Google) on a demo with no Google connection — check whether that badge should be withheld when no calendar is connected.
- Built mid-afternoon, today's six visits are already « à clôturer »; the nightly 03:00 build shows them ahead, as the spec intends.

### Learnings
- A value tuple has no null: `FirstOrDefault` on a closed day returned Sunday 00:00 and the hours were refused — test the builder's payload with the product's own validator.
- The patients list is paged newest-first and its rows are not links: QA opens a patient by id (looked up with the visitor's own token).

### Part B — treatments & money (2026-11-03)

**Delivered** (`DemoCabinetBuilder.Treatments.cs`, a second file of the partial builder):
- Leonard Green — bridge 14·15·16 (15 pontique), three séances on fiches, paid at the chair → **Completed** devis.
- Pete Becker — « Implant dentaire » (bilan · pose · contrôle done) + « Couronne sur implant » waiting (« Désenfouissement », 90 days) → **In progress**, échéance 2 **late** (« À relancer »).
- Ross Geller — couronne 36, préparation done, 200 DT collected → **In progress**.
- Mike Hannigan — détartrage done, blanchiment declined → **Stopped** (kept total 90 DT, paid).
- Jill Green — facettes followed → un-numbered **Draft**.
- Money: cheques at the chair (9, all banked but one → « Chèques » has one to deposit) · 3 monthly expenses (loyer, salaires, STEG/SONEDE) + 3 one-offs · one avoir (20 DT) · Eddie Menuek half-paid → his note is the one open note (« Reste à payer »). Notes d'honoraires come from the fiches themselves (`DentalRecordAutoBilling`).

**Blast radius (part B)**
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `SchemaVerificationReader` `plan-step-status-agrees` SQL | verify-schema check | `deploy-hosted.yml` gate (exit 2 fails a deploy), `SchemaVerificationServiceTests` (mocked reader) | must change — a **Withdrawn** act (parked by « Arrêter le traitement ») was reported as drift; every real cabinet that stops a devis holding a stepped act would turn the deploy red. Excluded `Status = 3` |
| 2 | treatment / invoice / expense / avoir / cheque-banking commands | read-only use by the seed | — | unaffected — no command changed |

**Auto-approved deviations (part B)**
| Deviation | Classification | Reason |
|---|---|---|
| Implant waiting = « Implant dentaire » done + « Couronne sur implant » pending | Trivial (catalogue shape) | the catalogue's implant act is 3 séances; the 90-day wait is the crown act's first step |
| `verify-schema` check fixed in the same pass | Trivial (in-scope masking defect) | found by the demo's own gate run; a false drift blocks hosted deploys |

**Gates (part B, after the last edit)**
- API: full unfiltered suite → **6 128 passed, 0 failed** (`Treatments_And_Money_Are_Written`, `Nothing_Is_Paid_In_The_Future` added).
- Real build: ~30 s, 0 errors. Plan states read back from SQL: Completed · In progress ×2 · Stopped · Draft. Collections on devis 1 500 / 500 / 200 / 90; 1 of 9 cheques unbanked; 1 avoir; 6 expenses; one open note (Eddie, 45 of 90 paid).
- `reconcile-money` → **0**. `verify-schema` → `plan-step-status-agrees` 1 DRIFT → **ok** after the fix; only `key-ring-protection` left (no certificate locally — expected).
- Browser (`qa/walk-4b.mjs`, GREEN after two probe fixes — caisse's default window is this month; « Chèques » lists the one to deposit): Traitements lists all five, « Reste à payer » lists Eddie and Pete, Chèques (1), Caisse shows the loyer, no writes beyond sign-in.

**Learnings**
- A check that reports a legitimate state as drift is a broken gate: read the state's own enum before calling it data corruption.
- `Must()` messages are French and name the step (« Démo : échec — créer le devis de l'implant : … »): when a nightly build fails, that line is all the operator gets.

### Part C — records & every screen (2026-11-03)

**Delivered** (`DemoCabinetBuilder.Records.cs`, the third file of the partial builder, run after the money):
- Ross Geller — a consultation fiche prescribing **clindamycine** (pénicilline allergy) + paracétamol and a **panoramique** → the fiche emits its ordonnance **and** its demande d'examens (two documents, as the product splits them).
- Joey Tribbiani — certificat (2 jours) by Monica; Frank Buffay — arrêt de travail (3 jours) by Chandler. Both through `CreateMedicalDocumentCommand` with `IssuingDoctorId`.
- Files — the panoramique on Ross and Leonard, the smile photo on Jill (embedded `pano.jpg` / `photo-sourire.jpg`; csproj embed widened `*.png` → `*.*`).
- Fournisseurs Dentalex Tunisie + Laboratoire Dentalab · 4 articles, gloves **low** (2 / 6), an articaïne lot expiring in 21 days · two bons de laboratoire, Ross's couronne **late**, Jack's prothèse on time.
- Gunther Centralperk — a canal left « non terminé », no devis → « Suites à planifier ».

**Blast radius (part C)**
| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `AppointmentProgressJob.SkipsAsync` + new `DemoCabinetBuilds` singleton | the minutely agenda job's per-cabinet skip | every cabinet on every deployment; `AppointmentProgressJobTests` harness (1 site) | must change — the job's tick closed a past visit **while** the seed was closing it, so the fiche's completion and its note d'honoraires failed on the stale version (a paid fiche with no note, one visit left « à clôturer »). Skip a cabinet the rebuild is writing; unaffected elsewhere (the set is empty outside a demo rebuild) |
| 2 | `DemoCabinetRebuilder` | wraps `BuildAsync` in `DemoCabinetBuilds.Begin` | the rebuild only | must change |
| 3 | `SystemWideCallerCoverageTests.Exempt` | folder-derived guard over `BackgroundJobs/` | — | must change — `DemoCabinetBuilds.cs` is an in-memory set, not a job; exempted with that structural reason |
| 4 | documents / files / stock / supplier / lab / fiche commands | read-only use by the seed | — | unaffected — no command changed |

**Deviations**
| Deviation | Classification | Reason |
|---|---|---|
| « Locator ≥ 1 patient » **not** implemented | Significant, decided (reversible) | the demo-entry request is anonymous, so the tenant filter reads 0 patients there and *every* cabinet would be refused; « activated last » already guarantees a complete cabinet. Recorded rather than asked |
| `AppointmentProgressJob` skips a cabinet under construction | Trivial (in-scope masking defect) | found by the real build; without it each nightly build leaves one paid fiche with no note |

**Gates (part C, after the last edit)**
- API: full unfiltered suite → **6 130 passed, 0 failed** (`Records_Are_Written`, `A_Demo_Cabinet_Being_Built_Is_Skipped_Until_It_Is_Built` added; `Nothing_Is_Paid_In_The_Future` covers document and lab dates).
- Real build on a fresh `clinic_demo_local`: ~30 s, **0 ConflictException, 0 ERR**. SQL: documents prescription · examens · certificat · arrêt de travail (1 each), 3 files, 2 suppliers, 1 low article, 1 late bon, 1 unfinished act; « séance passée » = today's 6 ended visits + the 2 planned.
- `reconcile-money` → **0**. `verify-schema` → only `key-ring-protection` (expected locally).
- Browser (`qa/walk-4c.mjs`, GREEN after three probe fixes — `/documents` is the templates page, so documents are read on each patient's tab; `/fichiers` lists « Nom Prénom » behind « Avec fichiers »; the patient page nests tabs, so the panel is addressed by its own content id): Fichiers, Stock, Fournisseurs, Laboratoire (« En retard »), « Suites à planifier » (Gunther), Ross › Documents (ordonnance + examens) and › Fichiers (Panoramique), Joey's certificat, Frank's arrêt; no writes beyond sign-in. Widths: 1440, and 390 on Fichiers.

**Learnings**
- A seed that books past visits races every minutely job that advances past visits; the real build is the only place that race shows (the unit fake has no clock).

### Review (2026-11-03) — `reviews/story-4.md`, 100/100
- Fixed: a second clinical photo (FR-6 « a few »), doctor name/specialty literals folded into `DemoScenario` + `DoctorSpecialtyLabels`, treatments/records/no-future tests run on all four anchor days.
- Filed: `follow-up/agenda-sync-badge-without-google.md` (product-wide, off-plan).
- Gates after the last edit: suite 6 139 / 0 · real rebuild 0 conflicts · `reconcile-money` 0 · `verify-schema` key-ring only · `walk-4c` GREEN 22/22.

## Story 5 — Spécimen PDFs

### Blast Radius
| # | Touched | Shape | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `IPdfGenerationService` registration (now a factory → `SpecimenPdfGenerationService.For`) | shared service, 8 query handlers + `PdfGenerationJob` + the `generate-pdf-download` action | every PDF on every profile | unaffected off the demo — `For` returns the plain renderer unless `RefusesWrites` (test over every `DeploymentKind`) |
| 2 | `SpecimenStamp` (PdfSharp, post-render) | new, demo only | — | the two CNAM overlay renderers (P61 arrêt de travail, BS1) bypass `DocumentPage`, so the stamp goes on the finished bytes, not on the page layout (DEV-5) |
| 3 | `PdfGenerationService` ctor | unchanged | 3 `new PdfGenerationService(…)` tests | unaffected — no constructor change |
| 4 | `RateLimiting.DemoPdfPolicy` on the 9 PDF actions | named policy | the 9 actions on every profile | unaffected off the demo — `GetNoLimiter`; guard derives the 9 from `"application/pdf"` in controller source |
| 5 | Global limiter partition | shared predicate, every request | every API call | unaffected off the demo — the demo keys on `family_id` (all visitors are one user, so they shared one 600/min bucket) |
| 6 | `OnRejected` | shared 429 body | every 429 | must re-test — demo PDF policy gets its own sentence + code; other policies byte-identical |
| 7 | `DemoSessionPartitionKey` reads the bearer | the limiter runs **before** `UseAuthentication` | — | must change — found by QA R2: `User` is empty at the limiter, so the visit fell back to the address and two visitors behind one line shared a budget; the family is read off the raw bearer (a forged one still fails authentication after) |

### Steps
- [x] 1 Stamp — `SpecimenStamp` (PdfSharp, every page: diagonal mark at ~15 % ink + footer line in a white box 16 pt above the edge, `/Keywords` + `/Subject` set) applied by `SpecimenPdfGenerationService`, which `Extensions.cs` wraps around the renderer only when `RefusesWrites`
- [x] 2 The two POST reads — already `[AllowedInDemo]` since story 2
- [x] 3 `RateLimiting.DemoPdfPolicy` (30 per 10 min per visit, `RateLimiting:DemoPdf:*`), `GetNoLimiter` off the demo, on the 9 actions; demo 429 says « La démo limite le nombre de documents ouverts… » with `code: demo_pdf_limited`; on the demo the global API window is per visit too
- [x] 4 Tests — `SpecimenPdfTests` (13) + `DemoPdfRateLimitTests` (12)

### DEV-5: the stamp goes on the finished PDF, not in `DocumentPage.Compose`
**Date:** 2026-11-03 · **Story:** 5 · **Category:** Technical
**Original Plan:** `IDocumentWatermark` read by `DocumentPage.Compose` (`page.Foreground()`), « the one choke point for all five renderers ».
**Actual Implementation:** `SpecimenPdfGenerationService` wraps `IPdfGenerationService` on the demo and stamps the returned bytes with PdfSharp.
**Justification:** the arrêt de travail (CNAM P 061) and the BS1 are overlay renderers that never reach `DocumentPage`, and the demo holds an arrêt de travail — the plan's choke point would have printed it unmarked. The interface is the real choke point (every PDF leaves through it), and the wrap keeps `PdfGenerationService` untouched (its 3 direct-construction tests unchanged).
**Impact:** none off the demo (`For` returns the plain renderer). A derived test invokes every interface method through the wrap.
**Approved:** decided (reversible), owner told before coding

### DEV-6: the demo's global API window is per visit
**Date:** 2026-11-03 · **Story:** 5 · **Category:** Scope (masking defect)
**Original Plan:** only the PDF policy keyed on the visit.
**Actual Implementation:** on the demo, the global 600/min window also keys on the visit.
**Justification:** every visitor is the same dentist, so the per-user key put all visitors in one 600/min bucket — about 30 visitors browsing at once would start seeing 429s. Demo only (`IssuesDemoSessions`).
**Approved:** decided (reversible)

### Learnings
- The rate limiter runs before `UseAuthentication`, so **every** « per user » partition in `RateLimiting.cs` actually keys on the address (`user:` never fires). Harmless for the archive (AdminOnly + step-up) but worth knowing; the demo reads the bearer instead.
- A PDF's `/Keywords` is the cheap, decisive read for « is it stamped »: QuestPDF and PdfSharp text is glyph-encoded, so the mark itself cannot be grepped.

### Gates (after the last edit)
| Gate | Result |
|---|---|
| Unit suite, unfiltered (`-c Release`, temp output) | 6 164 passed, 0 failed (+25 new) |
| Build | 0 errors; no warning in a story file (8 × NU1903 baseline) |
| Red-proofs | wrap guard red with one renderer unstamped; bearer test red against the `JwtSecurityTokenHandler` version; PDF-action guard red on a probe action |
| `verify-schema` / `reconcile-money` | not applicable — no migration, no money path (both verbs exist) |
| Browser QA | `qa/run-5.md` — cycle 1 RED (1 probe, 1 product), cycle 2 GREEN 17/17 |
| Web gates | not applicable — no `web/` file changed |

AC-5.3 (files open full size) and AC-5.4 (« Modifier » opens, save refused) carry over from story 4's `walk-4c` C10 and story 2's refusal walk; this story changed neither path.

### Review (2026-11-03) — `reviews/story-5.md`, 100/100
- **Major, fixed:** a forged bearer was a fresh bucket for the demo's global limit (the visit key is read unvalidated) → the demo's global limiter is chained with a per-address ceiling (`RateLimiting:Api:DemoAddressPermitLimit`, 3000 / window); real-limiter test, red without the chain.
- **Minor, fixed:** `SpecimenStamp` doc comment cut to the one-line budget.
- Gates: 6 166 passed / 0 failed; `walk-5` re-run on a fresh demo build GREEN (17/17, P9 ⏭ no avoir).

## Story 6 — Nightly rebuild

### Blast Radius
| # | Touched | Shape | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `DemoCabinetRebuilder` (now steps behind `DemoCabinetRebuild.RunAsync`) | the one rebuild routine | `EnsureCabinet`, `Program.cs` (boot enqueue + the new recurring job) | must change — check before open, purge on failure, retire after |
| 2 | `DemoCabinetBuilds` (+ `RebuildGate`, `CurrentBuiltOn`) | demo singleton | `AppointmentProgressJob` (`IsBuilding`), `AuthController` (new) | unaffected for the job — additive members |
| 3 | `AuthController` ctor (+ `DemoCabinetBuilds`) | DI | `SelfRegistrationGateTests.Controller` (1 site) | must change — updated; `demoBuiltOn` only where `IssuesDemoSessions` |
| 4 | `GET /api/auth/mode` + `demoBuiltOn` | anonymous probe every page makes | web `AuthModeDto` | unaffected — additive, no web reader (the operator reads it) |
| 5 | `IClinicPurge` / `ClinicDeletionAddresses` | console delete path | `DeleteClinicFromConsoleCommand` | unaffected — reused, not changed |
| 6 | `SchemaVerificationReader` admin-factor count | verify-schema check | every deployment's `verify-schema`, CI `local-mode`, the deploy gate | must change — found by the real run: counted every demo visitor; now « not applicable » where `RequiresAdminSecondFactor` is false (LAN, demo) |
| 7 | `SystemWideCallerCoverageTests.Exempt` | reviewed list | — | must change — `DemoCabinetRebuild.cs` (orders steps, no scope) |

### Steps
- [x] 1 Job — `rebuild-demo-cabinet`, `0 2 * * *` UTC, registered on `DemoCabinetRebuilder.Rebuild` where `IssuesDemoSessions`, removed elsewhere; `RebuildGate` serialises it with the boot build
- [x] 2 Check before switch — `MoneyReconciliationService` (any drift fails: the demo DB holds only demo cabinets) + `SchemaVerificationService` (only drift the build **added** fails, against a baseline taken before it)
- [x] 3 Switch + retire — `Activate()`, `CurrentBuiltOn`, then `DemoCabinetRetirement` → `DemoCabinetPurge` per cabinet (transaction, `IClinicPurge`, commit, files after)
- [x] 4 Failure path — the half-built cabinet is purged the same way; error logged naming the step; the exception reaches Hangfire (job marked failed)
- [x] 5 Tests — `DemoCabinetRebuildTests` (9), `DemoCabinetRetirementTests` (8), `AdminFactorCheckScopeTests` (6), `SelfRegistrationGateTests` +3

### Auto-Approved Deviations
| Deviation | Classification | Reason |
|---|---|---|
| No `RebuildDemoCabinetJob.cs`; the recurring job targets `DemoCabinetRebuilder.Rebuild` | Trivial (plan file list) | the wrapper would only forward one call |
| `demoBuiltOn` held in memory (`DemoCabinetBuilds.CurrentBuiltOn`), set by every opening and by the boot check | Trivial | keeps the anonymous `mode` probe free of a DB read; a restart re-reads it within the boot job |
| `demoBuiltOn` not added to web `AuthModeDto` | Trivial | no screen reads it; the operator's deploy summary does (story 7) |

### DEV-7: old cabinets go when nobody is inside, not « older than the previous one »
**Date:** 2026-11-03 · **Story:** 6 · **Category:** Technical
**Original Plan:** purge every demo cabinet older than the previous one.
**Actual:** purge every demo cabinet but the current one that has no live session family (and is not being built).
**Justification:** spec FR-7 says « removed once no session can still be using it ». Counting generations breaks a visitor's session when two rebuilds land within 2 h (a deploy at 02:00, the nightly at 03:00); the session check is exact and also sweeps a cabinet left by a crashed build. A safety rule rides with it: only clinics named « Cabinet Dentaire Démo » whose every account is `@demo.apexa.tn` can ever be retired, so a demo profile pointed at a real database cannot delete a practice.
**Approved:** decided (reversible)

### DEV-8: `verify-schema`'s admin-factor check scoped to deployments that require a factor
**Date:** 2026-11-03 · **Story:** 6 · **Category:** Scope (masking defect)
**Found by:** the real run — every demo visitor is the dentist-admin with no factor and a live session.
**Effect before:** every rebuild starting with a visitor inside would fail the check (the count moves during the build), and the deploy's `verify-schema` would stay red on the demo. Same false positive on any LAN install with an admin signed in.
**Fix:** « not applicable » where `RequiresAdminSecondFactor` is false (LAN, demo); unchanged on hosted and PC de secours.
**Approved:** decided (reversible)

### DEV-9: no forced-failure switch in production code
**Original Plan:** « forced failing check (test seam) » on the real stack.
**Actual:** the failure path is proven on `DemoCabinetRebuild.RunAsync` with a recording fake (check failure, build failure, provision failure, cleanup failure, retirement failure); no configuration key can make a rebuild fail on purpose.
**Approved:** decided (a production switch to break the demo is a liability)

### Gates (after the last edit)
| Gate | Result |
|---|---|
| Unit suite, unfiltered | 6 191 passed, 0 failed (+26) |
| Build | 0 errors; no warning in a story file |
| Real stack, 4 rebuilds | run 1 A opens (`demoBuiltOn` 2026-11-03); run 2 a visitor inside A reads 200 ×14 during the build and after, new entry lands in B; run 3 (B's visitor signed out) B purged — rows **and** its 7 files — A kept, 2 cabinets; run 4 C purged, A kept, 0 ERR lines |
| `verify-schema` | only `key-ring-protection` (expected locally); admin-factor check « not applicable » |
| `reconcile-money` | no drift |
| Hangfire | `recurring-job:rebuild-demo-cabinet` — `0 2 * * *`, UTC |
| Web gates | not applicable — no `web/` file changed |

### Learnings
- A check that runs **inside** a job must be deployment-aware like the job: `verify-schema` had a check that is always red on the demo, which only the real rebuild exposed.
- A schema check around a rebuild compares against a baseline taken before it; a deployment's standing drift is not the build's fault and would otherwise block every night.

### Review (2026-11-03) — `reviews/story-6.md`, 100/100
- **Major, fixed:** provisioning saved the new dentist active and the rebuild switched her off in a second save, so an empty cabinet was enterable for a moment → `LocalClinicRequest.AdminStartsInactive` creates her off in the same save (observed off during a real build).
- **Minor, fixed:** a provisioning failure part-way left its rows → the cabinet id is known up front and any failure from provisioning on removes it.
- **Minor, fixed:** a duplicated test case trimmed; baseline-failure case added.
- Gates: 6 194 passed / 0 failed; real rebuild after the fixes opened, 0 ERR, `verify-schema` key-ring only, `reconcile-money` 0.

## Story 7 — demo.apexa.tn prepared beside production

**Scope of this session:** everything local. Nothing touched the VPS, DNS, GitHub secrets or the live site.

### Blast Radius
| # | Touched | Shape | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `deploy/Caddyfile` (+ `import /etc/caddy/sites/*.caddy`) | production proxy config | every production request; `ContentSecurityPolicyAgreementTests`; the deploy's `caddy validate` step; `config-freshness.sh` | must re-test — validated with no site file (glob matches nothing) and with the demo file |
| 2 | `deploy-hosted.yml` — 3 compose commands, the validate step, a new « Demo cabinet » step | production deploy | every production deploy | unaffected without `DEMO_DOMAIN`: the overlay line is a no-op, the validate step mounts and reads the same single file (paths kept quoted via a bash array), the demo step exits at once |
| 3 | `docker-compose.demo.yml` (new) merges into `caddy` | compose merge | production `caddy` service | must re-test — resolved config: production's api/web/console/postgres/minio **identical** with and without the overlay; caddy gains only `DEMO_DOMAIN` + the site mount |
| 4 | `StaffChatPushQueueTests` | pre-existing evening date bomb (quiet hours) | — | must change — failed every evening after 21:00 Tunis, unrelated to this story; quiet hours switched off in that class's config |

### Steps
- [x] 1 Overlay — `deploy/docker-compose.demo.yml`: `api-demo` / `web-demo` on the production images, own DB + role, signing key + issuer/audience, key ring volume + certificate, audit key, bucket + MinIO user, `FrontendUrl`, `Demo__SignupUrl`, `Demo__RebuildOnStart` (operator's « rebuild now »), `mem_limit` + `cpus` on both, no outbound settings
- [x] 2 Caddy — demo site in `deploy/demo/Caddyfile.demo`, mounted into Caddy **only by the overlay**; `X-Robots-Tag: noindex, nofollow`, closed `robots.txt`, `/manifest.webmanifest` → 404; production `Caddyfile` imports `/etc/caddy/sites/*.caddy`
- [x] 3 Workflow — overlay added to all three `$COMPOSE` lines when `.env` has `DEMO_DOMAIN`; the demo site validated with the Caddyfile before the swap; « Demo cabinet » step: `/health`, `verify-schema` on `clinic-api-demo`, `demoBuiltOn` older than yesterday → warning; a « Demo » block in the run summary
- [x] 4 Runbook — `deploy/demo/README.md`: DNS, role + DB + `REVOKE CONNECT` on production's DB, MinIO user + bucket policy, 4 secrets owned by uid 1654, `.env` switch, deploy, checks, ongoing, switching off; pointer in `deploy/README.md`
- [x] 5 Rehearsal — throwaway project `demo-rehearsal`, two hosts (`app.localhost`, `demo.localhost`): **20/20**, then torn down
- [ ] 6 Owner — reviews the diff + runbook, then OKs each server step (DNS, DB, MinIO, secrets, `.env`, deploy)

### DEV-10: the demo's Caddy site is its own file, mounted only by the overlay
**Original Plan:** a `{$DEMO_DOMAIN}` block in `deploy/Caddyfile` and `DEMO_DOMAIN` in caddy's env in `docker-compose.prod.yml`.
**Actual:** `deploy/demo/Caddyfile.demo`, mounted by `docker-compose.demo.yml` at `/etc/caddy/sites/demo.caddy`; the Caddyfile only imports that folder. `docker-compose.prod.yml` unchanged.
**Justification:** a `{$DEMO_DOMAIN}` site in the shared Caddyfile expands to an empty address on every server without the demo, which Caddy refuses — production would have needed a dummy domain it then tries to certify. With the import, a server without the overlay has exactly today's sites.
**Approved:** decided (reversible)

### DEV-11: the demo step warns, never fails
**Actual:** demo `/health`, `verify-schema` and a stale `demoBuiltOn` are `::warning::`s.
**Justification:** by then production is live; the demo must never colour a production deploy red, but a down or non-rebuilding demo must be seen (AC-7.6).
**Approved:** decided (reversible)

### Auto-Approved Deviations
| Deviation | Classification | Reason |
|---|---|---|
| No separate `api-demo`/`web-demo` pull | Trivial | they run the exact image refs `pull api web` already fetched |
| `Demo__RebuildOnStart` exposed as `DEMO_REBUILD_ON_START` | Trivial | the runbook's « rebuild now » without editing the overlay |

### Gates (after the last edit)
| Gate | Result |
|---|---|
| Unit suite, unfiltered | 6 201 passed, 0 failed (+8: `DemoDeployWiringTests` 7, CSP demo site 1) |
| `docker compose config` | resolves with and without the overlay; production services byte-identical between the two |
| `caddy validate` | production Caddyfile alone ✓; with the demo site ✓ |
| Workflow scripts | `bash -n` on every bash `run:` block and all 4 server heredocs ✓ |
| Local rehearsal (`demo-rehearsal`, real images) | 20/20: both `/health`; demo entry 200; noindex · robots · manifest 404 on the demo only; `demoBuiltOn` today; production `isDemo=false`; demo token 200 on the demo, **401 on production**; demo role **refused** on production's DB; demo MinIO user **refused** on production's bucket; demo files in `clinic-demo`; no outbound env; caps set (768 MiB / 1 CPU, 384 MiB / 0.5 CPU); demo `verify-schema` clean. Plus: cookies `__Host-` (host-only), 0 ERR in the demo log |
| Docker cleanup | project `demo-rehearsal` down `-v`; images removed: `demo-rehearsal/clinic-{api,web,console}:r1`, `clinic-postgres-pitr:16`, `clinic-internal-certs:1`, `caddy:2-alpine`; `docker builder prune -a -f` (6.4 GB); shared `clinic-management` untouched |

### Learnings
- `--resolve host:443:127.0.0.1` against `*.localhost` names lets two Caddy sites be rehearsed on one machine with Caddy's internal CA.
- The rehearsal needs the console image too (production Caddy depends on it); a web-image stand-in was enough since the console is not under test.
- A test that reads « now » against a quiet-hours window is an evening date bomb; seen here in a file nobody touched.

### Review (2026-11-04) — `reviews/story-7.md`, 100/100
- Fixed: `up -d --wait` waited on the demo too, so a broken demo failed the production deploy → production's services listed, demo started after with a warning (`The_Deploy_Never_Waits_On_The_Demo`).
- Fixed: runbook « rebuild now » ran on `:latest` → reads prefix + tag from `clinic-api-prod`, `--no-build`.
- Added: `The_Demo_Runs_Production_Images` (AC-7.4).
- Owner request: « Créer mon cabinet » → `https://apexa.tn/#telecharger` (server + browser default, overlay `DEMO_SIGNUP_URL`, spec).
- Gates: 6 218 passed · tsc · check:responsive 85/85 · `bash -n` on the deploy block.

## Owner tweaks — 2026-11-04 (after trying the demo)

| Asked | Done |
|---|---|
| Agenda tidy, no overlaps, every slot taken except lunch | Two half-day shapes alternated day by day, each half-day owned by one dentist; 09–13 + 14–18 booked edge to edge (Sat 09–13); Ben's series sits in the 17:30 slot it frees. Guarded by `No_Visit_Overlaps_Another…` (across both dentists) and `Every_Working_Half_Day_Is_Fully_Booked`. |
| Show the app's colours | Acts cycle through 25 catalogue colours (future hours also book first séances of longer treatments); statuses shown: planifié, en cours / séance passée (live job), terminé, absent, retard +10. « Confirmé » is retired in the domain (no transition reaches it), so it is not seeded. |
| Every patient full, at least one act | `DemoScenarioPatients.CommandFor`: phone (zero-run Tunisian mobiles), `example.com` e-mail, address, motif, adressé par, tabac, rappels, notes, one antécédent médical + one familial each. Allergies/maladies only for some — any value raises the red alert panel. Patients are booked in list order, so every one has fiches (`Every_Patient_Is_A_Full_File_With_Work_Recorded`); a tooth already extracted/treated/diagnosed is never charted again (`_teethTaken`). |
| Odontograms open on « Symboles » | `odontogram.tsx`: no stored preference + `isDemo` → `symbols`; a visitor's own choice still wins. |

Off-plan, not changed: the agenda's « non synchronisé » badge shows on every hour-long future visit not pushed to Google even when no Google account is connected — product-wide behaviour.

## Owner tweak — 2026-11-04: no red figures, « Net » near 3 000 DT

| Ask | Done |
|---|---|
| Money earned ~3 000 DT, never negative | The demo dashboard reads the **last 30 / 7 days** (`DashboardPeriod.ResolveRolling`, `IDemoPolicy.RollsDashboardPeriods`); the builder reads its own « Net » last and adds the month's lab bill so it lands on `NetTarget` (3 000–3 349 DT, varies daily). Monthly charges are dated 10 days back (in the 30 days, out of the 7); nothing is spent today |
| No stats in red | On the demo a fall is drawn grey, never red (`quietFalls` on `KpiCard` and the status chart). 30-day view: every delta green or « Pas de comparaison » |

- Labels follow the server's `period.rolling`: « 7 derniers jours » / « 30 derniers jours », « Comparé aux 30 jours d'avant ».
- Still a minus: « Aujourd'hui » (nothing recorded yet today, Net 0) and two grey drops on « 7 derniers jours » (today is in the window but empty).
- Not changed: the « À traiter » counts (séances à clôturer, prothèse en retard) keep their red — they are work to do, not results.
- Verified: 6 216 tests pass; tsc + check:responsive 85/85; cabinet rebuilt, Net 3 196 DT on the 30 days; eye check at 1440 / 820 / 640.
