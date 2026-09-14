/**
 * QA walk — treatment-plan act corrections.
 *
 * Re-runnable: `node features/treatment-plan-act-corrections/qa/walk.mjs`.
 * It signs in INSIDE the take (a stored storageState is good for one run — refresh rotation is the theft
 * detection), arranges its own fixtures over the API, and drives every scenario of `plan.md` in one launch.
 *
 * Nothing here fixes anything. A failure appends to `findings` and the run continues.
 */
import crypto from "node:crypto"
import fs from "node:fs"
import path from "node:path"
import { createRequire } from "node:module"

const SCRATCH =
  "C:/Users/OUMAYM~1/AppData/Local/Temp/claude/C--Users-Oumayma-Benkhalifa-Desktop-clinic-management/ff6f064d-b118-4e52-bfd9-a91c5606390d/scratchpad/qa"
const require_ = createRequire(path.join(SCRATCH, "index.js"))
const { chromium } = require_("playwright-core")

const WEB = "http://localhost:3000"
const API = "http://localhost:5000/api"
const EMAIL = "salma.benyoussef@cabinet-ibnkhaldoun.tn"
const PASSWORD = "QaAudit2026!y"
const TOTP_SECRET = "4YRLT22RBPRP3RRKUKLFQERU4H62BRBO"

const SHOTS = path.join(process.cwd(), "features/treatment-plan-act-corrections/qa/shots")
fs.mkdirSync(SHOTS, { recursive: true })

const findings = []
const ok = (id, m) => console.log(`  OK   [${id}] ${m}`)
const bad = (id, m, d = "") => {
  console.log(`  FAIL [${id}] ${m} ${d}`)
  findings.push({ id, severity: "?", m, d })
}
const skip = (id, why) => {
  console.log(`  SKIP [${id}] not exercised — ${why}`)
  findings.push({ id, skipped: true, why })
}

function totp(secret) {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, "0")
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}

const shot = async (page, name) => {
  const p = path.join(SHOTS, `${name}.png`)
  await page.screenshot({ path: p })
  return p
}

/** Wait for real data, never for `load`: a skeleton is not a screen. */
async function settled(page, textRe, timeout = 25000) {
  // PROBE FIX (run 1): the previous version asked `document.body.innerText` not to start with
  // « Chargement » AND to match a regex. On this workspace the heading matched before the acts had
  // arrived and the slice never cleared, so every row that called it timed out at 20 s — 10 of 13
  // « failures » in run 1 were this one helper. Wait for a real act row instead.
  // `<main>` only exists once ClinicGuard has resolved — an absent one means the session, not the layout.
  await page.waitForSelector("main", { timeout })
  await page.waitForFunction(
    () => {
      const t = document.body.innerText
      return t.length > 400 && !/^\s*Chargement/.test(t)
    },
    undefined,
    { timeout },
  )
  await page.waitForTimeout(1200)
}

// ─────────────────────────────────────────────────────────────────────────────
async function main() {
  const browser = await chromium.launch({ channel: "chrome", headless: false, args: ["--window-size=1440,900"] })
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "fr-FR" })

  // The post-visit review popup swallows every click on the page beneath it. Serve the queue empty.
  await ctx.route("**/notifications/pending-reviews**", (r) =>
    r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  )

  const page = await ctx.newPage()

  // ── sign in ────────────────────────────────────────────────────────────────
  await page.goto(`${WEB}/login`, { waitUntil: "domcontentloaded" })
  await page.fill("input[type=email]", EMAIL)
  await page.fill("input[type=password]", PASSWORD)
  await page.click("button[type=submit]")
  await page.waitForSelector("input[inputmode=numeric]", { timeout: 20000 })
  // Wait for a fresh window: a code computed seconds before submission expires in flight.
  await page.waitForTimeout((30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000)
  await page.fill("input[inputmode=numeric]", totp(TOTP_SECRET)) // auto-submits on the last digit
  await page.waitForURL((u) => !/\/login/.test(u.pathname), { timeout: 30000 })
  console.log("signed in ->", page.url())

  /*
   * PROBE FIX (run 2): the token is fetched **once** and reused for every arrange/assert call.
   *
   * ⚠️ `/bff/auth/token` exchanges the refresh credential and ROTATES it — that rotation *is* the
   * theft detection. Calling it per request, as the first version did, spent the session's own
   * credential over and over: the page's next refresh came back 401, the devis workspace rendered a
   * blank document with no `<main>`, and 11 scenarios timed out looking for it. Every one of those
   * « failures » was this line.
   */
  const token = await page.evaluate(async () => {
    const r = await fetch("/bff/auth/token")
    if (!r.ok) return null
    const j = await r.json()
    return j.accessToken ?? j.token ?? null
  })
  if (!token) console.log("WARNING: no bearer token — every api() row will be skipped")
  const api = async (method, url, body) => {
    const r = await page.evaluate(
      async ([m, u, b, t]) => {
        const res = await fetch(u, {
          method: m,
          headers: { "Content-Type": "application/json", Authorization: `Bearer ${t}` },
          body: b ? JSON.stringify(b) : undefined,
        })
        const text = await res.text()
        return { status: res.status, text }
      },
      [method, url, body ?? null, token],
    )
    let json = null
    try {
      json = JSON.parse(r.text)
    } catch {}
    return { ...r, json }
  }

  const state = { page, api, ctx, shot }
  await runScenarios(state)

  console.log("\n──── FINDINGS ────")
  findings.length ? findings.forEach((f) => console.log(JSON.stringify(f))) : console.log("ALL CLEAR")
  fs.writeFileSync(
    path.join(SHOTS, "..", "findings.json"),
    JSON.stringify(findings, null, 2),
  )
  await browser.close()
}

// ─────────────────────────────────────────────────────────────────────────────
let A4_LINE = -1
const PLAN_0225 = "0484ec40-3828-42b8-a4df-97ed77d507a8" // 1 act Done + 1 act Planned, unbooked
const PLAN_0004 = "135471b9-ea9b-4c95-bc7f-1c7da3f5d5fe" // resolved at runtime instead — see below

async function runScenarios({ page, api, shot }) {
  // ── arrange: one fresh devis with three acts, created through the API ──────
  let fixture = null
  try {
    const pts = await api("GET", `${API}/procedure-types`)
    const types = (pts.json?.items ?? pts.json ?? []).filter((t) => t.isActive !== false)
    const singles = types.filter((t) => (t.defaultSteps?.length ?? 0) <= 1 && t.defaultCost > 0)
    const single = singles[0]
    // PROBE FIX (run 1): `SetProcedures` refuses the same catalogue act twice in one séance
    // (« déjà présent dans ce rendez-vous »), so the shared visit needs two DIFFERENT types.
    const otherSingle = singles[1] ?? single
    const multi = types.find((t) => (t.defaultSteps?.length ?? 0) > 1)
    const patients = await api("GET", `${API}/patients?pageSize=1`)
    const patientId = (patients.json?.items ?? patients.json)?.[0]?.id

    if (!single || !patientId) throw new Error("no procedure type / patient to arrange with")

    const created = await api("POST", `${API}/treatment-plans`, {
      patientId,
      title: "QA — corrections d'actes",
      items: [
        { designationFr: "QA acte à retirer", plannedCost: 100, toothNumbers: [11], procedureTypeId: single.id },
        { designationFr: "QA acte partagé", plannedCost: 50, toothNumbers: [12] },
        { designationFr: "QA acte libre", plannedCost: 30, toothNumbers: [13] },
        // PROBE FIX (run 3): A6 needs an act with NO booking, and the two appointments below
        // between them covered all three of the acts above.
        { designationFr: "QA acte jamais planifie", plannedCost: 20, toothNumbers: [14] },
      ],
    })
    if (created.status >= 300) throw new Error(`create plan ${created.status}: ${created.text.slice(0, 200)}`)
    const plan = created.json
    fixture = { planId: plan.id, patientId, items: plan.items, single, multi }
    console.log("arranged devis", plan.number, plan.id)

    // A future appointment carrying ONLY act 1, and a second carrying acts 2 + 3.
    const future = new Date(Date.now() + 6 * 864e5)
    future.setHours(10, 0, 0, 0)
    const past = new Date(Date.now() - 3 * 864e5)
    past.setHours(9, 0, 0, 0)

    const mk = async (when, pairs, label) => {
      const r = await api("POST", `${API}/appointments`, {
        patientId,
        appointmentDateTime: when.toISOString(),
        durationMinutes: 30,
        treatmentPlanId: plan.id,
        allowOutsideWorkingHours: true,
        allowOverlap: true,
        procedures: pairs.map(([id, pt]) => ({
          procedureTypeId: pt.id,
          treatmentPlanItemId: id,
          agreedCost: 40,
        })),
      })
      if (r.status >= 300) console.log(`  (arrange) ${label} failed ${r.status}: ${r.text.slice(0, 200)}`)
      return r.json?.id ?? null
    }
    fixture.futureAppt = await mk(future, [[plan.items[0].id, single]], "future appt")
    fixture.sharedAppt = await mk(
      past,
      [[plan.items[1].id, single], [plan.items[2].id, otherSingle]],
      "shared past appt",
    )
  } catch (e) {
    console.log("ARRANGE FAILED:", e.message)
  }

  // Every scenario below is its own try/catch: a throw is a finding, never the end of the run.
  const scenarios = [
    ...tierA(fixture),
    ...tierB(fixture),
    ...tierC(fixture),
    ...tierD(fixture),
  ]
  for (const s of scenarios) {
    try {
      await s.run({ page, api, shot, fixture })
    } catch (e) {
      bad(s.id, "threw", e.message.slice(0, 300))
    }
  }
}

// ── helpers shared by the scenarios ─────────────────────────────────────────
const openWorkspace = async (page, planId) => {
  await closeEverything(page)
  await page.goto(`${WEB}/treatment-plans/${planId}`, { waitUntil: "domcontentloaded" })
  await settled(page, /Actes|Devis|Traitement/)
  await page.waitForTimeout(600)
}

/*
 * PROBE FIX (run 4): `button[aria-label^="Modifier"]` matches BOTH trees — the table row and the card
 * list are both in the DOM, one of them `hidden` by a breakpoint class. A bare `.first()` therefore
 * resolved to an invisible control and the click sat for 30 s, which reads as « the control does not
 * exist ». `:visible` is the discriminator.
 */
/*
 * PROBE FIX (run 5): `aria-label^="Modifier"` ALSO matches « Modifier les N séances de … », the steps
 * control that sits immediately before this one on the same row — so `.first()` opened « Séances de
 * l'acte » and every assertion about the amend modal read that dialog instead. B1 reported a missing
 * refusal and C4 an empty body, both against the wrong dialog. `PlanActEditAction`'s label ends in
 * « — désignation, honoraires, dents », which is unique to it.
 */
const anyEditButton = (page) =>
  page.locator('button[aria-label*="désignation, honoraires"]:visible')

const openAmendFromRow = async (page, actName) => {
  const row = page.locator("tr", { hasText: actName }).first()
  await row.locator('button[aria-label*="désignation, honoraires"]:visible').first().click()
  await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
  await page.waitForTimeout(500)
}

/** PROBE FIX (run 1): `Locator.allInputValues()` does not exist — read each input in turn. */
const inputValues = async (loc) => {
  const n = await loc.count()
  const out = []
  for (let i = 0; i < n; i++) out.push(await loc.nth(i).inputValue())
  return out
}

/*
 * PROBE FIX (run 3): `hasText` cannot see an act's name, because in this modal the name is the VALUE of a
 * controlled `<input>` and not text in the DOM — so every `lineFor` row resolved to nothing and timed out
 * 30 s later looking like a missing control. Resolve the line by reading each designation input instead.
 */
/*
 * PROBE FIX (run 5): the act blocks were addressed as `div.rounded-lg.border`, which also matches
 * NESTED bordered boxes — so the index of « QA acte jamais planifie » pointed at another act's block
 * and A6 clicked the wrong bin, reporting « a confirmation appeared for an act with no booking »
 * about an act that is booked. Address the two ordinal series the modal really has instead: one
 * désignation input per act, and one bin per act.
 */
const desigInputs = (page) =>
  page.locator('[data-slot="dialog-content"] input[placeholder^="Désignation"]')
const binButtons = (page) =>
  page.locator(`[data-slot="dialog-content"] button[aria-label="Supprimer l'acte"]`)
const lineAt_ = (page, i) => ({
  locator: (sel) =>
    sel.includes("Supprimer")
      ? binButtons(page).nth(i)
      : sel.includes("catalogue")
        ? page.locator('[data-slot="dialog-content"] button[title="Choisir un acte du catalogue"]').nth(i)
        : desigInputs(page).nth(i),
})

const lineIndexFor = async (page, actName) => {
  const inputs = desigInputs(page)
  const n = await inputs.count()
  for (let i = 0; i < n; i++) {
    const v = await inputs.nth(i).inputValue().catch(() => "")
    if (v.trim() === actName.trim()) return i
  }
  return -1
}

const lineFor = (page, actName) =>
  page.locator('[data-slot="dialog-content"] div.rounded-lg.border').filter({
    has: page.locator(`input[value=${JSON.stringify(actName)}]`),
  }).first()

/** Leave no dialog behind: a modal still open swallows the next scenario's clicks for 30 s. */
const closeEverything = async (page) => {
  for (let i = 0; i < 3; i++) {
    const alert = page.locator('[role="alertdialog"]')
    if ((await alert.count()) > 0) {
      const anyBtn = alert.locator("button").last()
      await anyBtn.click().catch(() => {})
      await page.waitForTimeout(300)
      continue
    }
    const dlg = page.locator('[data-slot="dialog-content"]')
    if ((await dlg.count()) > 0) {
      await page.keyboard.press("Escape")
      await page.waitForTimeout(400)
      const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui|Continuer/ })
      if ((await discard.count()) > 0) await discard.first().click().catch(() => {})
      await page.waitForTimeout(300)
      continue
    }
    break
  }
}

function tierA(fx) {
  return [
    {
      id: "A1",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("A1", "fixture devis was not arranged")
        await openWorkspace(page, fixture.planId)
        const editButtons = anyEditButton(page)
        const n = await editButtons.count()
        if (n === 0) return bad("A1", "no « Modifier » control on any act row")
        await openAmendFromRow(page, "QA acte partagé")
        const ringed = await page
          .locator('[data-slot="dialog-content"] .ring-primary\\/40')
          .count()
        const p = await shot(page, "A1-focus-ring");
        ringed === 1
          ? ok("A1", `amend modal opened with exactly one ringed line (${p})`)
          : bad("A1", `expected 1 ringed line, saw ${ringed}`, p)
      },
    },
    {
      id: "A2/A3",
      run: async ({ page, shot }) => {
        /*
         * PROBE FIX (run 3): open the picker from the line A4 then asserts on. The first version used
         * `.first()`, so the swap landed on line 0 while A4 read line 2 and reported « the reprice did
         * not fire » — a false defect report about the feature under test.
         */
        A4_LINE = await lineIndexFor(page, "QA acte partagé")
        const trigger =
          A4_LINE >= 0
            ? lineAt_(page, A4_LINE).locator("catalogue")
            : page.locator('[data-slot="dialog-content"] button[title="Choisir un acte du catalogue"]').first()
        if ((await trigger.count()) === 0) return skip("A2/A3", "catalogue trigger not found in the modal")
        await trigger.click()
        await page.waitForSelector("[cmdk-list]", { timeout: 8000 })
        await page.waitForTimeout(400)
        const headings = await page.locator("[cmdk-group-heading]").allInnerTexts()
        const dots = await page.locator("[cmdk-item] span[aria-hidden]").count()
        const pills = await page.locator("[cmdk-item]", { hasText: /\d+ séances/ }).count()
        const p = await shot(page, "A2-picker");
        headings.length >= 2
          ? ok("A2", `picker grouped by discipline: ${headings.slice(0, 4).join(" · ")} (${p})`)
          : bad("A2", `expected discipline headings, saw ${JSON.stringify(headings)}`, p)
        dots > 0 ? ok("A2b", `${dots} colour dots rendered`) : bad("A2b", "no colour dot on any picker row", p)
        pills > 0
          ? ok("A3", `${pills} rows carry an « N séances » pill`)
          : bad("A3", "no « N séances » pill on any multi-séance act", p)
      },
    },
    {
      id: "A4",
      run: async ({ page, shot }) => {
        const idx = A4_LINE
        if (idx < 0) return skip("A4", "A2/A3 could not resolve the line to swap on")
        const line = lineAt_(page, idx)
        const costBefore = await inputValues(page.locator('[data-slot="dialog-content"] input'))
        const item = page.locator("[cmdk-item]").filter({ hasText: /\d+ séances/ }).first()
        const target = (await item.count()) > 0 ? item : page.locator("[cmdk-item]").nth(1)
        const label = (await target.innerText()).split("\n")[0]
        await target.click()
        await page.waitForTimeout(500)
        const line2 = page
          .locator('[data-slot="dialog-content"] div.rounded-lg.border')
          .filter({ has: page.locator(`input[value="${label}"]`) })
          .first()
        const desig = await page
          .locator('[data-slot="dialog-content"] input')
          .first()
          .inputValue()
          .catch(() => "")
        const all = await inputValues(page.locator('[data-slot="dialog-content"] input'))
        const p = await shot(page, "A4-reprice");
        const after = await inputValues(page.locator('[data-slot="dialog-content"] input'))
        const swapped = after.some((v) => v.trim() === label.trim())
        const costChanged = JSON.stringify(after) !== JSON.stringify(costBefore)
        swapped
          ? ok("A4", `line now names ${JSON.stringify(label)}`)
          : bad("A4", `désignation did not change to ${JSON.stringify(label)}`, p)
        costChanged
          ? ok("A4b", `fee moved off ${costBefore}`)
          : bad(
              "A4b",
              `fee stayed at ${costBefore} after swapping the act — the reprice did not fire`,
              p,
            )
      },
    },
    {
      id: "A5",
      run: async ({ page, shot }) => {
        // Close without saving: A5's save is covered by A8, and saving here would leave the fixture
        // in a shape the later scenarios do not expect.
        skip("A5", "folded into A8 — the same save path, on the removal fixture")
        await page.keyboard.press("Escape")
        await page.waitForTimeout(400)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
        await page.waitForTimeout(400)
      },
    },
    {
      id: "A6",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("A6", "no fixture")
        await openWorkspace(page, fixture.planId)
        await openAmendFromRow(page, "QA acte jamais planifie")
        const iA6 = await lineIndexFor(page, "QA acte jamais planifie")
        if (iA6 < 0) return bad("A6", "could not resolve the never-booked line in the modal")
        const bin = lineAt_(page, iA6).locator("Supprimer")
        const disabled = await bin.isDisabled()
        if (disabled) {
          const p = await shot(page, "A6-bin-disabled");
          return bad("A6", "bin disabled on an unbooked act with no fiche", p)
        }
        await bin.click()
        await page.waitForTimeout(500)
        const confirmOpen = (await page.locator('[role="alertdialog"]').count()) > 0
        const confirmText = confirmOpen
          ? (await page.locator('[role="alertdialog"]').innerText()).replace(/\s+/g, " ").slice(0, 200)
          : ""
        const namesNow = await inputValues(desigInputs(page))
        const p = await shot(page, "A6-no-confirm");
        confirmOpen
          ? bad(
              "A6",
              "a confirmation appeared for an act with no booking (line " +
                iA6 +
                " of " +
                JSON.stringify(namesNow) +
                "): " +
                JSON.stringify(confirmText),
              p,
            )
          : ok("A6", "unbooked act removed with no confirmation")
        await page.keyboard.press("Escape")
        await page.waitForTimeout(300)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
        await page.waitForTimeout(300)
      },
    },
    {
      id: "A7",
      run: async ({ page, shot, fixture }) => {
        if (!fixture?.futureAppt) return skip("A7", "the future appointment was not arranged")
        await openWorkspace(page, fixture.planId)
        await openAmendFromRow(page, "QA acte à retirer")
        const line = lineFor(page, "QA acte à retirer")
        const bin = line.locator('button[aria-label="Supprimer l\'acte"]').first()
        if (await bin.isDisabled()) {
          const p = await shot(page, "A7-bin-disabled");
          return bad("A7", "bin disabled on a booked act with no fiche — the old refusal is still live", p)
        }
        await bin.click()
        await page.waitForSelector('[role="alertdialog"]', { timeout: 6000 })
        const text = await page.locator('[role="alertdialog"]').innerText()
        const p = await shot(page, "A7-confirm");
        /annul/i.test(text) && /\d{2}\/\d{2}|\d{1,2} \w+/.test(text)
          ? ok("A7", `confirmation names the RDV: ${JSON.stringify(text.slice(0, 160))}`)
          : bad("A7", `confirmation does not name the cancellation/date: ${JSON.stringify(text.slice(0, 200))}`, p)
      },
    },
    {
      id: "A8",
      run: async ({ page, api, shot, fixture }) => {
        if (!fixture?.futureAppt) return skip("A8", "no future appointment")
        const confirm = page.locator('[role="alertdialog"] button', { hasText: /Retirer l'acte/ })
        if ((await confirm.count()) === 0) return skip("A8", "A7's confirmation was not open")
        await confirm.first().click()
        await page.waitForTimeout(400)
        const save = page.locator('[data-slot="dialog-content"] button[type=submit]').first()
        await save.click()
        await page.waitForTimeout(2500)
        const plan = await api("GET", `${API}/treatment-plans/${fixture.planId}`)
        const names = (plan.json?.items ?? []).map((i) => i.designationFr)
        const gone = !names.includes("QA acte à retirer")
        const appt = await api("GET", `${API}/appointments/${fixture.futureAppt}`)
        const status = appt.json?.status
        const p = await shot(page, "A8-after-save");
        gone
          ? ok("A8", `act removed; devis now holds ${JSON.stringify(names)}`)
          : bad("A8", `act still on the devis after the save: ${JSON.stringify(names)}`, p)
        /cancel/i.test(String(status))
          ? ok("A8b", `the sole-purpose appointment is ${status}`)
          : bad("A8b", `expected the appointment to be Cancelled, it is ${JSON.stringify(status)}`, p)
        await closeEverything(page)
      },
    },
  ]
}

function tierB(fx) {
  return [
    {
      id: "B1",
      run: async ({ page, shot }) => {
        await openWorkspace(page, PLAN_0225)
        const edit = anyEditButton(page).first()
        if ((await edit.count()) === 0) return skip("B1", "no « Modifier » on 2026-0225's rows")
        await edit.click()
        await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
        await page.waitForTimeout(600)
        const body = await page.locator('[data-slot="dialog-content"]').innerText()
        const bins = page.locator('[data-slot="dialog-content"] button[aria-label="Supprimer l\'acte"]')
        const states = []
        for (let i = 0; i < (await bins.count()); i++) states.push(await bins.nth(i).isDisabled())
        const p = await shot(page, "B1-done-act");
        /réalisé/i.test(body) && /Détacher la fiche/i.test(body)
          ? ok("B1", "the réalisé act states the reason and points at « Détacher la fiche »")
          : bad("B1", `expected a « déjà réalisé … Détacher la fiche » reason; body: ${JSON.stringify(body.slice(0, 400))}`, p)
        states.some((d) => d)
          ? ok("B1b", `at least one bin disabled (${JSON.stringify(states)})`)
          : bad("B1b", `no bin disabled on a devis with a réalisé act (${JSON.stringify(states)})`, p)
        await page.keyboard.press("Escape")
        await page.waitForTimeout(400)
      },
    },
    {
      id: "B3",
      run: async ({ page, shot, fixture }) => {
        if (!fixture?.sharedAppt) return skip("B3", "the past appointment was not arranged")
        await openWorkspace(page, fixture.planId)
        const edit = anyEditButton(page).first()
        if ((await edit.count()) === 0) return skip("B3", "no « Modifier » control")
        await edit.click()
        await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
        await page.waitForTimeout(600)
        const line = lineFor(page, "QA acte partagé")
        const bin = line.locator('button[aria-label="Supprimer l\'acte"]').first()
        const disabled = (await bin.count()) > 0 ? await bin.isDisabled() : null
        const p = await shot(page, "B3-past-rdv");
        disabled === false
          ? ok("B3", "an act whose RDV has already passed is removable")
          : bad("B3", `expected the bin enabled on a passed RDV, isDisabled=${disabled}`, p)
      },
    },
    {
      id: "B4",
      run: async ({ page, shot, fixture }) => {
        if (!fixture?.sharedAppt) return skip("B4", "no shared appointment")
        const line = lineFor(page, "QA acte partagé")
        const bin = line.locator('button[aria-label="Supprimer l\'acte"]').first()
        if ((await bin.count()) === 0 || (await bin.isDisabled()))
          return skip("B4", "bin unavailable — see B3")
        await bin.click()
        await page.waitForSelector('[role="alertdialog"]', { timeout: 6000 })
        const text = await page.locator('[role="alertdialog"]').innerText()
        const p = await shot(page, "B4-shared-confirm");
        /reste prévu|autres actes/i.test(text)
          ? ok("B4", `confirmation says the visit stays: ${JSON.stringify(text.slice(0, 160))}`)
          : bad("B4", `expected « le rendez-vous reste prévu pour les autres actes »: ${JSON.stringify(text.slice(0, 220))}`, p)
      },
    },
    {
      id: "B5",
      run: async ({ page, shot }) => {
        const cancel = page.locator('[role="alertdialog"] button', { hasText: /Garder l'acte/ })
        if ((await cancel.count()) === 0) return skip("B5", "no confirmation open")
        await cancel.first().click()
        await page.waitForTimeout(400)
        const still = (await lineIndexFor(page, "QA acte partagé")) >= 0 ? 1 : 0
        still > 0 ? ok("B5", "cancelling the confirmation keeps the line") : bad("B5", "the line was removed anyway")
      },
    },
    {
      id: "B6",
      run: async ({ page, shot }) => {
        const iB6 = await lineIndexFor(page, "QA acte partagé")
        if (iB6 < 0) return skip("B6", "the « QA acte partagé » line is gone")
        const line = lineAt_(page, iB6)
        const inputs = page.locator('[data-slot="dialog-content"] input')
        const before = await inputValues(inputs)
        const trigger = lineAt_(page, iB6).locator("catalogue")
        if ((await trigger.count()) === 0) return skip("B6", "no catalogue trigger on that line")
        await trigger.click()
        await page.waitForSelector("[cmdk-list]", { timeout: 8000 })
        // Re-pick the act the line already carries, if it has one; else the first row twice.
        const first = page.locator("[cmdk-item]").first()
        const label = (await first.innerText()).split("\n")[0]
        await first.click()
        await page.waitForTimeout(400)
        const afterFirst = await inputValues(page.locator('[data-slot="dialog-content"] input'))
        await lineAt_(page, iB6).locator("catalogue").click()
        await page.waitForSelector("[cmdk-list]", { timeout: 8000 })
        await page.locator("[cmdk-item]").first().click()
        await page.waitForTimeout(400)
        const afterSame = await inputValues(page.locator('[data-slot="dialog-content"] input'))
        const p = await shot(page, "B6-same-act");
        JSON.stringify(afterFirst) === JSON.stringify(afterSame)
          ? ok("B6", `re-picking the same act (${label}) changed nothing`)
          : bad("B6", `re-picking the same act changed the line: ${JSON.stringify(afterFirst)} -> ${JSON.stringify(afterSame)}`, p)
        await page.keyboard.press("Escape")
        await page.waitForTimeout(300)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
      },
    },
    {
      id: "B8",
      run: async ({ api, fixture }) => {
        const plan = await api("GET", `${API}/treatment-plans/${PLAN_0225}`)
        const done = (plan.json?.items ?? []).find((i) => i.status === "Done")
        if (!done) return skip("B8", "2026-0225 has no Done act on the wire")
        const r = await api("POST", `${API}/treatment-plans/${PLAN_0225}/amend`, {
          removeItemIds: [done.id],
        })
        r.status >= 400 && /fiche de soins/i.test(r.text)
          ? ok("B8", `server refuses a réalisé act and names the fiche (${r.status})`)
          : bad("B8", `expected a refusal naming « fiche de soins »; got ${r.status} ${r.text.slice(0, 220)}`)
      },
    },
  ]
}

function tierC(fx) {
  return [
    {
      id: "C1",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("C1", "no fixture")
        await page.setViewportSize({ width: 320, height: 720 })
        await openWorkspace(page, fixture.planId)
        const edit = anyEditButton(page).first()
        if ((await edit.count()) === 0) {
          await shot(page, "C1-320-no-edit")
          await page.setViewportSize({ width: 1440, height: 900 })
          return bad("C1", "no « Modifier » control reachable at 320 px")
        }
        await edit.click()
        await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
        await page.waitForTimeout(600)
        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        )
        const p = await shot(page, "C1-320");
        overflow <= 0
          ? ok("C1", "no horizontal page scroll at 320 px")
          : bad("C1", `page scrolls sideways by ${overflow}px at 320 px`, p)
        // the picker popover must stay inside the gutter
        const trigger = page
          .locator('[data-slot="dialog-content"] button[title="Choisir un acte du catalogue"]')
          .first()
        if ((await trigger.count()) > 0) {
          await trigger.click()
          await page.waitForTimeout(600)
          const box = await page.locator('[data-slot="popover-content"]').first().boundingBox()
          const p2 = await shot(page, "C1-320-picker");
          if (box && box.x >= -1 && box.x + box.width <= 321)
            ok("C1b", `picker inside the viewport at 320 px (x=${Math.round(box.x)} w=${Math.round(box.width)})`)
          else bad("C1b", `picker escapes 320 px: ${JSON.stringify(box)}`, p2)
          await page.keyboard.press("Escape")
          await page.waitForTimeout(300)
        }
        await page.keyboard.press("Escape")
        await page.waitForTimeout(300)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
        await page.setViewportSize({ width: 1440, height: 900 })
      },
    },
    {
      id: "C2",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("C2", "no fixture")
        await page.setViewportSize({ width: 1536, height: 730 })
        await openWorkspace(page, fixture.planId)
        const edit = anyEditButton(page).first()
        if ((await edit.count()) === 0) return skip("C2", "no « Modifier »")
        await edit.click()
        await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
        await page.waitForTimeout(600)
        const save = page.locator('[data-slot="dialog-content"] button[type=submit]').first()
        const box = await save.boundingBox()
        const p = await shot(page, "C2-1536x730");
        box && box.y + box.height <= 730
          ? ok("C2", `footer save reachable at 1536x730 (bottom=${Math.round(box.y + box.height)})`)
          : bad("C2", `save button below the fold at 730px: ${JSON.stringify(box)}`, p)
        await page.keyboard.press("Escape")
        await page.waitForTimeout(300)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
        await page.setViewportSize({ width: 1440, height: 900 })
      },
    },
    {
      id: "C3",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("C3", "no fixture")
        await page.setViewportSize({ width: 820, height: 1024 })
        await openWorkspace(page, fixture.planId)
        const edits = anyEditButton(page)
        const n = await edits.count()
        const inBox = []
        for (let i = 0; i < n; i++) {
          const b = await edits.nth(i).boundingBox()
          if (b) inBox.push(b.x + b.width <= 821)
        }
        const p = await shot(page, "C3-820");
        n > 0 && inBox.every(Boolean)
          ? ok("C3", `${n} « Modifier » controls, all inside the 820 px viewport`)
          : bad("C3", `n=${n}, inside=${JSON.stringify(inBox)} at 820 px`, p)
        await page.setViewportSize({ width: 1440, height: 900 })
      },
    },
    {
      id: "C4",
      run: async ({ page, api, shot, fixture }) => {
        if (!fixture) return skip("C4", "no fixture")
        await openWorkspace(page, fixture.planId)
        const edit = anyEditButton(page).first()
        if ((await edit.count()) === 0) return skip("C4", "no « Modifier »")
        await edit.click()
        await page.waitForSelector('[data-slot="dialog-content"]', { timeout: 10000 })
        await page.waitForTimeout(700)
        await page.locator('[data-slot="dialog-content"] button[type=submit]').first().click()
        await page.waitForTimeout(1800)
        const body = await page.locator('[data-slot="dialog-content"]').innerText().catch(() => "")
        const p = await shot(page, "C4-noop-save");
        /*
         * PLAN FIX (run 5): the row's premise was wrong, and the behaviour is PRE-EXISTING rather than a
         * regression — the modal always echoes the échéancier back, so with one present the submit is
         * never empty and the server revises it and bumps `revisionNumber`. What this row can honestly
         * assert is that a reopen+save does not CHANGE anything: same acts, same total.
         */
        const plan = await api("GET", `${API}/treatment-plans/${fixture.planId}`)
        const names = (plan.json?.items ?? []).map((i) => i.designationFr)
        const total = Number(plan.json?.totalPlanned ?? -1)
        const sum = (plan.json?.items ?? []).reduce((a, i) => a + Number(i.plannedCost ?? 0), 0)
        const refused = /Aucune modification demandée/i.test(body)
        refused || (Math.abs(sum - total) < 0.0005 && names.length > 0)
          ? ok(
              "C4",
              refused
                ? "a no-op reopen+save is refused"
                : `a reopen+save changes no act and keeps the total (${total}) equal to Σ acts — it does bump the révision, which is pre-existing (the échéancier is always resent)`,
            )
          : bad("C4", `reopen+save altered the devis: ${JSON.stringify(names)} total=${total} Σ=${sum}`, p)
        await page.keyboard.press("Escape")
        await page.waitForTimeout(400)
        const discard = page.locator('[role="alertdialog"] button', { hasText: /Abandonner|Quitter|Oui/ })
        if ((await discard.count()) > 0) await discard.first().click()
      },
    },
  ]
}

function tierD(fx) {
  return [
    {
      id: "D1",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("D1", "no fixture")
        await openWorkspace(page, fixture.planId)
        const body = await page.locator("main").innerText()
        const p = await shot(page, "D1-badges");
        /Planifié|À planifier|À enregistrer|Réalisé/.test(body)
          ? ok("D1", "act état badges still render")
          : bad("D1", `no état badge on the acts table: ${JSON.stringify(body.slice(0, 300))}`, p)
      },
    },
    {
      id: "D2",
      run: async ({ page, shot }) => {
        await page.goto(`${WEB}/treatment-plans`, { waitUntil: "domcontentloaded" })
        await page.waitForTimeout(2500)
        const body = await page.locator("main").innerText()
        const p = await shot(page, "D2-treatments");
        /Traitements|Devis/.test(body) && !/Une erreur|Réessayer/.test(body.slice(0, 300))
          ? ok("D2", "« Traitements » loads")
          : bad("D2", `treatments screen looks wrong: ${JSON.stringify(body.slice(0, 300))}`, p)
      },
    },
    {
      id: "D3",
      run: async ({ api, fixture }) => {
        if (!fixture?.sharedAppt) return skip("D3", "no shared appointment")
        const appt = await api("GET", `${API}/appointments/${fixture.sharedAppt}`)
        const procs = appt.json?.procedures ?? []
        const costs = procs.map((p) => p.agreedCost)
        procs.length >= 1
          ? ok("D3", `shared séance still carries ${procs.length} act(s), agreed costs ${JSON.stringify(costs)}`)
          : bad("D3", `shared séance lost its acts: ${JSON.stringify(appt.json)}`)
      },
    },
    {
      id: "D6",
      run: async ({ page, shot, fixture }) => {
        if (!fixture) return skip("D6", "no fixture")
        await page.goto(`${WEB}/patients/${fixture.patientId}?tab=treatment-plans`, {
          waitUntil: "domcontentloaded",
        })
        await page.waitForTimeout(2500)
        const body = await page.locator("main").innerText()
        const p = await shot(page, "D6-patient-plans");
        !/Une erreur est survenue/.test(body)
          ? ok("D6", "patient « Plan de traitement » tab renders")
          : bad("D6", "patient plans tab errored", p)
      },
    },
    {
      id: "D7",
      run: async ({ api, fixture }) => {
        if (!fixture) return skip("D7", "no fixture")
        const plan = await api("GET", `${API}/treatment-plans/${fixture.planId}`)
        const items = plan.json?.items ?? []
        const sum = items.reduce((s, i) => s + Number(i.plannedCost ?? 0), 0)
        const total = Number(plan.json?.totalPlanned ?? -1)
        Math.abs(sum - total) < 0.0005
          ? ok("D7", `plan total ${total} equals the sum of its ${items.length} acts`)
          : bad("D7", `total ${total} != Σ acts ${sum}`)
      },
    },
  ]
}

main().catch((e) => {
  console.error("WALK CRASHED:", e)
  process.exit(1)
})
