# Story 0: SPIKE — Embedded Signup version confirmation

**Status:** APPROVED
**Story Status:** done — see [`../progress.md`](../progress.md) for the findings

> ✅ **Outcome: the answer was neither declared branch.** Meta's current version is **v4**; the 15 Oct 2026
> deprecation names **v2 only**; we are on **v3**. Resolution: migrate to v4 inside Part 4 § 31 (plan **D-1a**) —
> four edits in one file, the delta to Meta's sample being a single key. The spike also disproved an approved-spec
> pricing claim and exposed two live defects in the shipped connect path.
**Layer:** Spike (human-in-the-loop; one small code deliverable)
**Depends On:** None
**Blocks:** 1

## Objective

Settle, before any connection work is built, **which Meta Embedded Signup version the shipped integration actually
implements** — and record it, because the answer decides whether Story 1's Part 4 is a config consolidation (Branch A)
or a flow migration (Branch B). Along the way, read the four JavaScript-gated Meta pages the spec could not, so the
acceptance criteria that rest on unconfirmed Meta rules stop resting on assumption. The one code change is real and is
made either way: the two independent `v21.0` pins become one.

This story exists because it is the only part of the feature an implementation agent cannot do — it needs a logged-in
Meta browser session — and because discovering the answer *after* the connection slice is built is what the plan's
**D-1** calls « the expensive order ».

## Acceptance Criteria

_From spec:_

- [ ] Scope item: « **Confirming which Embedded Signup version to build on, and migrating if it is v2** — the first
      task of the connection slice, not a later one »
- [ ] Open Question 1: the display-name guideline list, template edit caps, whether one payment instrument can serve
      several accounts in a portfolio, and Tunisia's per-message rates are opened in a logged-in browser and recorded
- [ ] Dependencies note: « **Confirm the target version before building on it**, and migrate if the answer is v2 »
- [ ] Indirectly de-risks **AC-1.1** (the guided connection flow) and **AC-1.3** (template submission on the
      cabinet's behalf), both of which Part 4 builds on top of this flow

_Story-specific:_

- [ ] The answer (v2 or v3) is recorded in `features/vendor-whatsapp-messaging-quota/progress.md` with the evidence it
      was read from, not merely asserted
- [ ] **Branch A or Branch B is explicitly chosen** for Story 1 Part 4 step 31, and written down
- [ ] The two `v21.0` pins are **one**, with the surviving pin named
- [ ] Whatever the four Meta pages say is recorded even where it confirms the spec — a confirmation is a finding

## Entry Criteria

Before starting this story, ensure:

- [ ] A **logged-in Meta / Facebook Business browser session** is available to the person running this
- [ ] `features/vendor-whatsapp-messaging-quota/progress.md` exists (create it if not — it is where the answer lands)
- [ ] The current integration is readable: `web/components/reminder-settings.tsx:209-290` (the `FB.login` call) and
      `api/ClinicManagement.Infrastructure/Services/MetaConfig.cs`
- [ ] Working tree is clean enough to commit a small isolated change —
      `git diff HEAD --numstat` reviewed first, since this branch carries in-flight work from other authors

## Steps

1. **Read what is actually deployed, before opening any Meta page**
   - `web/components/reminder-settings.tsx:209-290` — record the exact `FB.login` config: `config_id`,
     `response_type`, and the contents of `extras`.
   - The spec asserts `extras.sessionInfoVersion = "3"` with **no `featureType`**. Confirm or refute that from the
     source; do not carry the spec's claim forward unchecked.
   - Record both `v21.0` pins: `MetaConfig.cs`'s `DefaultGraphApiVersion` and `reminder-settings.tsx:45`'s
     `META_GRAPH_VERSION`. Note that neither derives from the other — that is the defect being closed in step 4.

2. **Confirm the Embedded Signup version against Meta's own docs**
   - Open Meta's Embedded Signup documentation in the logged-in session.
   - Determine which version the config in step 1 corresponds to, and what the **v2 → v3 marker difference** is.
   - ⚠️ The deprecation date on record is **15 October 2026**. Note what the docs say now, including whether that date
     has moved.
   - Write the answer + the page it came from into `progress.md`.

3. **Open the four JavaScript-gated pages the spec could not read** (Open Question 1, `exploration.md` § 6.8)
   - The **display-name guideline** list.
   - The **template edit caps**.
   - Whether **one payment instrument can serve several accounts** in a portfolio.
   - **Tunisia's actual per-message rates** — and whether the rates replacing the free rules that end
     **1 October 2026** have been published yet (they were due by 1 September 2026).
   - Record each answer in `progress.md`, including « still unpublished » where that is the truth. ⚠️ A rate surprise
     moves the *vendor's cost*, not the product's arithmetic — one message is one unit whatever Meta charges — so this
     informs R-12's default-allowance figure rather than any code.

4. **Consolidate the two `v21.0` pins into one** (done in both branches)
   - Make the browser constant derive from the server's configured value, or state in one place why it cannot and pin
     both from a single named source.
   - ⚠️ Do **not** silently bump the version while consolidating: this step changes *where the number lives*, not what
     it is. A version change is Branch B's business.
   - Update `api/ClinicManagement.UnitTests/Infrastructure/Services/WhatsAppOnboardingServiceTests.cs` and
     `ReminderChannelSenderTests.cs` if they assert the old arrangement.

5. **Choose and record the branch for Story 1 Part 4**
   - **Branch A — already current (v3):** Part 4 step 31 is a no-op beyond step 4's consolidation.
   - **Branch B — v2 (what the spec asserts):** Part 4 step 31 migrates the `FB.login` config and adds the version
     marker. If that migration looks larger than a config change, say so — **R-2's contingency is that Part 4 becomes
     its own story**, and Parts 1–3 do not depend on it.
   - Write the choice and its size estimate into `progress.md`. Story 1's entry criteria read this.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `features/vendor-whatsapp-messaging-quota/progress.md` | The spike's recorded answers: the ES version + evidence, the four Meta-page findings, the chosen branch and its size estimate. Created here if absent; Story 1 appends to it. |

### Files to Modify

| File | Changes |
|------|---------|
| `web/components/reminder-settings.tsx` | Consolidate `META_GRAPH_VERSION` (`:45`) so the browser's Graph version is no longer an independent pin. Branch B additionally migrates the `FB.login` config at `:209-290`. |
| `api/ClinicManagement.Infrastructure/Services/MetaConfig.cs` | The surviving single source of the Graph API version (`DefaultGraphApiVersion`, `:14`), if that is the direction chosen. |
| `api/ClinicManagement.UnitTests/Infrastructure/Services/WhatsAppOnboardingServiceTests.cs`, `.../ReminderChannelSenderTests.cs` | Update only if they assert the two-pin arrangement (`"Meta:GraphApiVersion" = "v21.0"`, `https://graph.test/v21.0`). |

## Verification Steps

After completing this story, verify:

- [ ] `progress.md` states the ES version, **the source it was read from**, and the chosen branch — not just a verdict
- [ ] All four Open-Question-1 items have a recorded answer, including any that remain unpublished
- [ ] `grep -rn "v21.0" web/components api/ClinicManagement.Infrastructure` shows **one** authority, not two
      independent ones (ignore `node_modules` and `.claude/worktrees`)
- [ ] `dotnet build` clean and the unit suite green — the two Meta test classes included
- [ ] `npx tsc --noEmit` and `npm run build` clean in `web/`
- [ ] The WhatsApp connect flow still loads in the browser (this story must not break the existing connection path)

**Verification commands:**
```bash
# Build + tests OUTSIDE the repo (Smart App Control refuses freshly-built in-repo assemblies — R-14)
cd api && dotnet build -p:BaseOutputPath="$TEMP/cm-build/"
dotnet test -c Release -p:BaseOutputPath="$TEMP/cm-test/" \
  --filter "FullyQualifiedName~WhatsAppOnboardingServiceTests|FullyQualifiedName~ReminderChannelSenderTests"

# One version authority, not two
grep -rn "v21.0" web/components api/ClinicManagement.Infrastructure --include=*.ts --include=*.tsx --include=*.cs

# Frontend gate
cd web && npx tsc --noEmit && npm run build
```

## Exit Criteria

This story is complete when:

- [ ] The Embedded Signup version is **known and recorded**, with evidence
- [ ] **Branch A or Branch B is chosen and written down**, with a size estimate for Part 4
- [ ] The four Meta-page answers are recorded in `progress.md`
- [ ] Exactly one `v21.0` authority remains, and the version value itself is **unchanged** by this story
- [ ] `dotnet build` + unit suite green; `web/` typecheck + build green
- [ ] The existing WhatsApp connect flow is confirmed still working
- [ ] All verification steps pass

## Notes

- **This story writes almost no code, and that is correct.** Its value is that Story 1's Part 4 starts with a known
  branch instead of an assumption. Resist growing it into the migration itself — if Branch B is chosen, the migration
  belongs to Part 4 (or to its own story, per R-2).
- **Record confirmations, not only surprises.** « The spec was right about `sessionInfoVersion` » is a finding worth
  having in `progress.md`, because the next person otherwise re-does this spike.
- The rates question feeds **R-12** (the default standing allowance) and nothing else. It is operator configuration
  (`Messaging:DefaultMessagesPerMonth`), so a still-unpublished rate does **not** block Story 1 — ship a provisional
  number.
- ⚠️ Check `git diff HEAD --numstat` before staging. This branch routinely carries 40+ dirty files from other work, and
  a broad `git add` here would swallow it.
