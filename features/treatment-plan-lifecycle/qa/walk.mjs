// The browser half of the Phase 6 QA pass. ONE launch, every row, no check throws, no source is fixed.
// Re-run mode is the SAME file (`test-in-browser` rule 5) — never a narrower one.
//
//   node walk.mjs                 # the whole plan
//   QA_ONLY=A1,A2 node walk.mjs   # iterate on a probe bug, in the same launch as the rest
import { chromium } from "../../../e2e/node_modules/playwright-core/index.mjs"
import { signIn, bearer, ORIGIN } from "./auth.mjs"
import { readFileSync, writeFileSync, mkdirSync } from "node:fs"
import { join, dirname } from "node:path"
import { fileURLToPath } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, "shots")
mkdirSync(SHOTS, { recursive: true })

const state = JSON.parse(readFileSync(join(HERE, ".artifacts", "state.json"), "utf8"))
const ONLY = process.env.QA_ONLY ? new Set(process.env.QA_ONLY.split(",")) : null

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = "") => {
  console.log(`  ❌ [${id}] ${m} ${d}`)
  findings.push({ id, severity: "?", m, d })
}
const skip = (id, why) => {
  console.log(`  ⏭  [${id}] not exercised — ${why}`)
  findings.push({ id, skipped: true, why })
}

const { token } = await signIn()
const call = bearer(token)
const planUrl = (key) => `${ORIGIN}/treatment-plans/${state.plans[key].id}`

const browser = await chromium.launch({ channel: "chrome", headless: false })
const ctx = await browser.newContext({
  storageState: join(HERE, ".artifacts", "session.json"),
  viewport: { width: 1440, height: 900 },
  locale: "fr-FR",
})
// ⚠️ The post-visit review popup is a queue of modals that SWALLOWS CLICKS, and its « Ajouter le dossier
// médical » button is itself filled — so it both blocks the ⋯ menu and pollutes any « what is the primary
// action? » reading. Its snooze store (`clinic:pvr-snooze`) is a map keyed per appointment, so it cannot be
// pre-seeded for ids we do not know: starve the feed instead.
await ctx.route("**/api/notifications/pending-reviews*", (r) =>
  // ⚠️ A BARE ARRAY. Wrapping it in `{ value: [] }` — this API's usual envelope — made `s.find is not a
  // function` throw inside the header, and the error boundary then replaced EVERY page in the app. One wrong
  // stub shape reads as a total product collapse.
  r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
)
const page = await ctx.newPage()
const consoleErrors = []
page.on("console", (m) => {
  if (m.type() === "error") consoleErrors.push(m.text().slice(0, 200))
})

// ── helpers ────────────────────────────────────────────────────────────────────────────────────────────
const settle = async (ms = 900) => page.waitForTimeout(ms)

/** Closes the post-visit review modal if one slipped past the route block. */
async function dismissOverlays() {
  for (let i = 0; i < 4; i++) {
    const later = page.getByRole("button", { name: /^Plus tard$/i }).first()
    if (!(await later.count())) return
    await later.click({ timeout: 2000 }).catch(() => {})
    await settle(250)
  }
}

/** Waits for the DATA, never for `load` — a skeleton is not a screen. */
async function openPlan(key) {
  consoleErrors.length = 0
  await page.goto(planUrl(key), { waitUntil: "domcontentloaded" })
  await page.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
  await dismissOverlays()
  await settle()
}

const body = () => page.locator("body").innerText()

/** The one filled primary in the header, if there is one. `primaryAction` renders a non-outline Button. */
async function primaryLabel() {
  const trigger = page.getByRole("button", { name: "Autres actions sur ce devis" })
  const header = page.locator("div").filter({ has: trigger }).last()
  const btns = header.locator("button:visible")
  const n = await btns.count()
  const out = []
  for (let i = 0; i < n && i < 40; i++) {
    const b = btns.nth(i)
    const cls = (await b.getAttribute("class")) ?? ""
    const txt = (await b.innerText().catch(() => "")).trim()
    // A filled Button carries neither `variant="outline"`'s border token nor ghost's transparency.
    if (!txt) continue
    if (/bg-primary|bg-\[--primary\]/.test(cls) && !/variant-outline|border-input/.test(cls)) out.push(txt)
  }
  return out
}

async function openActionsMenu() {
  await page.getByRole("button", { name: "Autres actions sur ce devis" }).click()
  await page.locator('[role="menu"]').waitFor({ timeout: 5000 })
  await settle(300)
  return (await page.locator('[role="menu"]').innerText()).split("\n").map((s) => s.trim()).filter(Boolean)
}

async function closeMenu() {
  await page.keyboard.press("Escape")
  await settle(250)
}

/** A confirm is `role="alertdialog"`, NOT `[role="dialog"]` — the probe trap that reads as « has no text ». */
const anyDialog = () => page.locator('[role="alertdialog"]:visible, [role="dialog"]:visible').first()

async function dialogText(timeout = 6000) {
  await anyDialog().waitFor({ timeout })
  await settle(250)
  return anyDialog().innerText()
}

async function shot(id, opts = {}) {
  const p = join(SHOTS, `${id}.png`)
  // ⚠️ `animations: "disabled"` is not cosmetic: a page holding a running animation never reaches Playwright's
  // « stable » frame, the call times out at 30 s, and every row after it dies with it. And a failed screenshot
  // must never end the walk.
  await page
    .screenshot({ path: p, animations: "disabled", caret: "hide", timeout: 10000, ...opts })
    .catch((e) => console.log(`     (screenshot ${id} skipped: ${String(e.message).split("\n")[0]})`))
  return p
}

/** Does the page grow a scrollbar it should not? `page-scroller-contains-its-absolutes`' symptom. */
async function overflow() {
  return page.evaluate(() => ({
    docW: document.documentElement.scrollWidth,
    winW: window.innerWidth,
    docH: document.documentElement.scrollHeight,
    winH: window.innerHeight,
  }))
}

// ── the scenarios ──────────────────────────────────────────────────────────────────────────────────────
const SCENARIOS = []
const S = (id, run) => SCENARIOS.push({ id, run })

// A1 — a Draft plan offers exactly one filled button, « Éditer le devis ».
S("A1", async () => {
  if (!state.plans.draft) return skip("A1", "no followed Draft was minted")
  await openPlan("draft")
  const filled = await primaryLabel()
  const t = await body()
  await shot("A1")
  if (filled.length === 1 && /Éditer le devis/i.test(filled[0]))
    ok("A1", `one filled button: « ${filled[0]} »`)
  else bad("A1", `expected exactly one filled « Éditer le devis »`, `saw ${JSON.stringify(filled)}`)
  if (/Sans devis/i.test(t)) ok("A1b", "the Draft is labelled « Sans devis »")
  else bad("A1b", "a Draft did not read « Sans devis »", t.slice(0, 200).replace(/\n/g, " | "))
})

// A2 — the ⋯ menu on a numbered live plan.
S("A2", async () => {
  await openPlan("acts")
  const items = await openActionsMenu()
  await shot("A2")
  const want = [
    ["Devis PDF", /Devis PDF/i],
    ["Modifier les actes et les prix", /Modifier les actes et les prix/i],
    ["Arrêter le traitement", /Arrêter le traitement/i],
    ["Dupliquer ce devis", /Dupliquer ce devis/i],
    ["Changer le praticien", /Changer le praticien/i],
    ["Changer de patient", /Changer de patient/i],
  ]
  const text = items.join(" | ")
  for (const [name, re] of want) {
    if (re.test(text)) ok(`A2·${name}`, "present")
    else bad(`A2·${name}`, "missing from the ⋯ menu", text)
  }
  if (/Terminer le traitement/i.test(text)) bad("A2·terminer", "« Terminer le traitement » is present — AC-11 says absent everywhere", text)
  else ok("A2·terminer", "« Terminer le traitement » absent (AC-11)")
  await closeMenu()
})

// A5 — cancel-with-money states the encaissé amount and « irréversible ».
S("A5", async () => {
  // ⚠️ « Annuler le devis » is offered only where the stop branch CANNOT reach (`canCancelPlan`), so on a live
  // devis carrying a deposit the reachable door is « Arrêter le traitement » — and that is the dialog that has
  // to state the money. Read whichever one this plan actually offers.
  await openPlan("cancelmoney")
  const items = await openActionsMenu()
  const door = /Annuler le devis/i.test(items.join(" "))
    ? /Annuler le devis/i
    : /Arrêter le traitement/i.test(items.join(" "))
      ? /Arrêter le traitement/i
      : null
  if (!door) {
    await closeMenu()
    return bad("A5", "neither « Annuler le devis » nor « Arrêter le traitement » is offered on a devis holding money", items.join(" | "))
  }
  await page.locator('[role="menuitem"]', { hasText: door }).first().click()
  const t = await dialogText()
  await shot("A5")
  const doorName = String(door).includes("Annuler") ? "« Annuler le devis »" : "« Arrêter le traitement »"
  const isCancelDoor = String(door).includes("Annuler")
  if (/150|irréversible|avoir/i.test(t)) {
    // « Cette action est irréversible » is C2's requirement for the CANCEL branch. The refund branch refuses
    // and destroys nothing, so the word would be false there — what it owes instead is the remedy.
    if (isCancelDoor) {
      if (/irréversible/i.test(t)) ok("A5", "the cancel dialog says « irréversible »")
      else bad("A5", "the cancel dialog never says « irréversible »", t.replace(/\n/g, " | ").slice(0, 300))
    } else if (/avoir/i.test(t)) ok("A5", "the arrêt dialog names the avoir instead of claiming irreversibility")
    else bad("A5", "the arrêt dialog neither claims irreversibility nor names a remedy", t.replace(/\n/g, " | ").slice(0, 300))
    if (/150/.test(t)) ok("A5b", "the dialog states the encaissé amount (150)")
    else bad("A5b", "the dialog does not state the 150 DT already collected", t.replace(/\n/g, " | ").slice(0, 300))
  } else
    bad(
      "A5",
      `${doorName} — the only door this devis offers — never states the 150,000 DT already collected`,
      t.replace(/\n/g, " | ").slice(0, 300),
    )
  // B4 rides on the same dialog: an empty motif must keep the confirm disabled.
  // B4 rides on this dialog only when it is the CANCEL one — an arrêt that keeps delivered work needs no motif.
  if (String(door) === String(/Annuler le devis/i)) {
    const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Annuler le devis|Confirmer/i }).last()
    const disabled = await confirm.isDisabled().catch(() => null)
    if (disabled === true) ok("B4", "an empty motif leaves the confirm disabled")
    else if (disabled === false) bad("B4", "the confirm is ENABLED with an empty motif")
    else skip("B4", "could not resolve the confirm button")
    if (/obligatoire/i.test(t)) ok("B4b", "the motif field says « obligatoire »")
    else bad("B4b", "the motif field never says « obligatoire »", t.replace(/\n/g, " | ").slice(0, 200))
  } else skip("B4", "this devis takes the arrêt branch, which asks for no motif — B1 covers the cancel branch")
  await page.keyboard.press("Escape")
  await settle(400)
})

// A6 / B8 — a Cancelled plan: the primary action, and the « Prochaine séance » block.
S("A6", async () => {
  await openPlan("cancelled")
  const filled = await primaryLabel()
  const t = await body()
  await shot("A6")
  if (filled.some((l) => /R[ée]tablir ce devis annul[ée]|Reprendre ce devis annul[ée]/i.test(l))) ok("A6", `primary is « ${filled.find((l) => /R[ée]tablir|Reprendre/i.test(l))} »`)
  else bad("A6", "« Rétablir ce devis annulé » is not the primary action", `filled = ${JSON.stringify(filled)}`)
  if (/Devis annulé/i.test(t)) ok("B8", "the « Prochaine séance » block reads « Devis annulé »")
  else bad("B8", "no « Devis annulé » arm on a cancelled plan", t.slice(0, 300).replace(/\n/g, " | "))
  if (new RegExp(state.run, "i").test(t)) ok("B8b", "the cancellation motif is shown")
  else bad("B8b", "the motif is not shown beside « Devis annulé »", t.slice(0, 300).replace(/\n/g, " | "))
})

// B7 — a Completed plan has NO filled primary; « Reprendre » lives in the ⋯ menu.
S("B7", async () => {
  await openPlan("completed")
  const filled = await primaryLabel()
  await shot("B7")
  if (filled.some((l) => /Reprendre le traitement/i.test(l)))
    bad("B7", "« Reprendre le traitement » is the filled primary on a Completed plan (m13)", JSON.stringify(filled))
  else ok("B7", `« Reprendre » is not the filled primary (header shows ${JSON.stringify(filled)})`)
  const items = (await openActionsMenu()).join(" | ")
  if (/Reprendre le traitement/i.test(items)) ok("B7b", "« Reprendre le traitement » is in the ⋯ menu")
  else bad("B7b", "« Reprendre le traitement » is not in the ⋯ menu of a Completed plan", items)
  await closeMenu()
})

// A7 / A8 — park one act, then restore it.
S("A7", async () => {
  await openPlan("acts")
  const before = await call("GET", `/treatment-plans/${state.plans.acts.id}`)
  const totalBefore = Number((before.json?.value ?? before.json).totalPlanned)

  const target = page.getByRole("button", { name: /Mettre .* de côté/i }).first()
  if (!(await target.count())) return skip("A7", "no « … de côté » control found on an act row")
  await target.click()
  const t = await dialogText()
  await shot("A7")
  if (/gard|conserv|encaiss|reste/i.test(t)) ok("A7", "the confirm names what is kept")
  else bad("A7", "the park confirm does not name what is kept", t.replace(/\n/g, " | ").slice(0, 300))
  const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Mettre de côté|Confirmer/i }).last()
  await confirm.click()
  await settle(1500)
  const t2 = await body()
  if (/mis de côté/i.test(t2)) ok("A7b", "the row carries a « mis de côté » marker")
  else bad("A7b", "no « mis de côté » marker after parking", t2.slice(0, 300).replace(/\n/g, " | "))

  // A8 — restore it, and the total must return to its pre-park figure.
  const r = page.getByRole("button", { name: /Remettre .* au devis/i }).first()
  if (!(await r.count())) return skip("A8", "no « Remettre au devis » control after parking")
  await r.click()
  await settle(600)
  const confirm2 = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Remettre|Confirmer/i }).last()
  if (await confirm2.count()) await confirm2.click()
  await settle(1500)
  const after = await call("GET", `/treatment-plans/${state.plans.acts.id}`)
  const totalAfter = Number((after.json?.value ?? after.json).totalPlanned)
  if (Math.abs(totalAfter - totalBefore) < 0.001) ok("A8", `total returned to ${totalAfter}`)
  else bad("A8", `total did not return after restore`, `${totalBefore} → ${totalAfter}`)
})

// A3 / A4 — stop a plan with a deposit, then reopen it, and check the balance on TWO screens.
S("A3", async () => {
  await openPlan("stoppable")
  const items = await openActionsMenu()
  if (!/Arrêter le traitement/i.test(items.join(" "))) {
    await closeMenu()
    return skip("A3", `« Arrêter le traitement » not offered; menu = ${items.join(" | ")}`)
  }
  await page.locator('[role="menuitem"]', { hasText: /Arrêter le traitement/i }).first().click()
  const t = await dialogText()
  await shot("A3-dialog")
  const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Arrêter|Confirmer/i }).last()
  const needsMotif = await confirm.isDisabled().catch(() => false)
  if (needsMotif) {
    const ta = page.locator('[role="alertdialog"]:visible textarea, [role="dialog"]:visible textarea, [role="alertdialog"]:visible input[type="text"]').first()
    if (await ta.count()) await ta.fill(`QA ${state.run} — arrêt`)
    await settle(300)
  }
  await confirm.click()
  await settle(2000)
  const after = await body()
  await shot("A3")
  const refusal = after.match(/Aucun acte de ce devis[^\n]*/i)?.[0] ?? ""
  const live = await call("GET", `/treatment-plans/${state.plans.stoppable.id}`)
  const st = (live.json?.value ?? live.json).status
  if (st === "Stopped") ok("A3", "the plan landed on Stopped (server)")
  else if (refusal)
    bad(
      "A3",
      "the arrêt dialog described the park and « le traitement passe à « Arrêté » », then the SERVER REFUSED it",
      `status stayed ${st} · refusal: ${refusal}`,
    )
  else bad("A3", "the plan did not land on Stopped", `status ${st} · dialog was: ${t.slice(0, 160).replace(/\n/g, " | ")}`)
  if (st === "Stopped") ok("B10", `server agrees: status ${st}`)
  else skip("B10", `plan is ${st}, not Stopped — nothing to assert about a Stopped plan's debt`)

  if (st === "Stopped") {
    const filled = await primaryLabel()
    if (filled.some((l) => /Reprendre le traitement/i.test(l))) ok("A3b", "primary is « Reprendre le traitement »")
    else bad("A3b", "« Reprendre le traitement » is not the primary on a Stopped plan", JSON.stringify(filled))
  } else skip("A3b", `plan is ${st} — the Stopped header cannot be read`)
})

S("A4", async () => {
  const before = await call("GET", `/treatment-plans/${state.plans.stoppable.id}`)
  const b = before.json?.value ?? before.json
  if (b.status !== "Stopped") return skip("A4", `plan is ${b.status}, not Stopped — A3 did not land`)
  await openPlan("stoppable")
  const filled = page.getByRole("button", { name: /Reprendre le traitement/i }).first()
  if (!(await filled.count())) return skip("A4", "no « Reprendre le traitement » control")
  await filled.click()
  await settle(700)
  const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Reprendre|Confirmer/i }).last()
  if (await confirm.count()) await confirm.click()
  await settle(2000)
  const after = await call("GET", `/treatment-plans/${state.plans.stoppable.id}`)
  const a = after.json?.value ?? after.json
  await shot("A4")
  if (a.status !== "Stopped") ok("A4", `reopened to ${a.status}`)
  else bad("A4", "the plan is still Stopped after « Reprendre »")
  const want = state.plans.stoppable.totalPlanned
  if (Math.abs(Number(a.totalPlanned) - want) < 0.001) ok("A4b", `parked acts restored — totalPlanned back to ${a.totalPlanned}`)
  else bad("A4b", `totalPlanned is ${a.totalPlanned}, expected ${want} after restore`)
  // The second screen: « Créances ».
  await page.goto(`${ORIGIN}/creances`, { waitUntil: "domcontentloaded" })
  await settle(2500)
  const cre = await body()
  if (cre.includes(state.plans.stoppable.number) || /QA-PHASE6/i.test(cre))
    ok("A4c", "the reopened plan is visible on « Créances »")
  else skip("A4c", "could not find the plan on page 1 of « Créances » (it pages)")
})

// A3x — a numbered devis carrying a deposit with NOTHING delivered: the arrêt is offered and described,
// and the server refuses it. `StopWouldCancel`'s own note says the money term exists so the dentist is not
// sent to a remedy the product refuses — this is the same shape, one door over.
S("A3x", async () => {
  await openPlan("deposit")
  const items = await openActionsMenu()
  if (!/Arrêter le traitement/i.test(items.join(" "))) {
    await closeMenu()
    return ok("A3x", "« Arrêter le traitement » is correctly withheld on a deposit devis with nothing delivered")
  }
  await page.locator('[role="menuitem"]', { hasText: /Arrêter le traitement/i }).first().click()
  const t = await dialogText()
  await shot("A3x-dialog")

  // The dialog must state the money and the avoir BEFORE any press, and must promise no « Arrêté ».
  if (/200[.,]000|avoir/i.test(t)) {
    if (/avoir/i.test(t)) ok("A3x", "the dialog names the avoir as the remedy, before the press")
    else bad("A3x", "the dialog states the money but never names the avoir", t.replace(/\n/g, " | ").slice(0, 300))
    if (/200[.,]000/.test(t)) ok("A3x·amount", "the dialog states the 200,000 DT already collected")
    else bad("A3x·amount", "the dialog does not state the amount collected", t.replace(/\n/g, " | ").slice(0, 300))
  } else bad("A3x", "the arrêt dialog says nothing about the money that blocks it", t.replace(/\n/g, " | ").slice(0, 300))

  if (/passe à\s*«?\s*Arrêté/i.test(t))
    bad("A3x·promise", "the dialog still promises « le traitement passe à « Arrêté » » on a devis the server refuses", t.replace(/\n/g, " | ").slice(0, 300))
  else ok("A3x·promise", "the dialog no longer promises an « Arrêté » it cannot deliver")

  // And no action may be offered whose only outcome is a red toast.
  const act = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /^Arrêter le traitement$|^Annuler le devis$/i })
  if ((await act.count()) === 0) ok("A3x·no-action", "no confirm is offered on the refund branch — « Retour » only")
  else bad("A3x·no-action", "a confirm is still offered on a devis the server refuses to stop")

  await page.keyboard.press("Escape")
  await settle(500)
  const live = await call("GET", `/treatment-plans/${state.plans.deposit.id}`)
  const st = (live.json?.value ?? live.json).status
  if (st === "InProgress" || st === "Accepted") ok("A3x·untouched", `the devis is untouched (${st})`)
  else bad("A3x·untouched", `the devis moved to ${st} without a confirm being pressed`)
})

// A11 — duplicate.
S("A11", async () => {
  await openPlan("duplicate")
  const items = await openActionsMenu()
  if (!/Dupliquer ce devis/i.test(items.join(" "))) {
    await closeMenu()
    return skip("A11", "« Dupliquer ce devis » not offered")
  }
  await page.locator('[role="menuitem"]', { hasText: /Dupliquer ce devis/i }).first().click()
  await settle(700)
  const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Dupliquer|Confirmer/i }).last()
  if (await confirm.count()) await confirm.click()
  await settle(2500)
  await shot("A11")
  const list = await call("GET", `/treatment-plans?pageNumber=1&pageSize=50&patientId=${state.plans.duplicate.patientId}`)
  const plans = (list.json?.value ?? list.json).items ?? []
  const copy = plans.find((p) => p.id !== state.plans.duplicate.id)
  if (!copy) return bad("A11", "no duplicate plan exists for that patient", `${plans.length} plan(s)`)
  if (copy.status === "Draft" && !copy.number) ok("A11", `duplicate is Draft with no number (${copy.id})`)
  else bad("A11", `duplicate is ${copy.status} number ${copy.number} — expected Draft / no number`)
  const full = await call("GET", `/treatment-plans/${copy.id}`)
  const n = ((full.json?.value ?? full.json).items ?? []).length
  if (n === state.plans.duplicate.itemCount) ok("A11b", `the copy carries all ${n} acts`)
  else bad("A11b", `the copy has ${n} acts, source had ${state.plans.duplicate.itemCount}`)
})

// A12 — a remise, set through the product.
S("A12", async () => {
  await openPlan("acts")
  const before = await call("GET", `/treatment-plans/${state.plans.acts.id}`)
  const totalBefore = Number((before.json?.value ?? before.json).totalPlanned)
  const target = page.getByRole("button", { name: /remise sur/i }).first()
  if (!(await target.count())) return skip("A12", "no « … remise sur … » control on an act row")
  await target.click()
  await settle(700)
  const input = page.locator("#plan-item-discount").first()
  if (!(await input.count())) return skip("A12", "the remise dialog has no « Remise (DT) » field")
  await input.fill("30")
  await settle(200)
  const save = page.locator('[role="dialog"]:visible button, [role="alertdialog"]:visible button').filter({ hasText: /Enregistrer|Appliquer|Confirmer/i }).last()
  await save.click()
  await settle(2000)
  await shot("A12")
  const after = await call("GET", `/treatment-plans/${state.plans.acts.id}`)
  const totalAfter = Number((after.json?.value ?? after.json).totalPlanned)
  if (Math.abs(totalBefore - totalAfter - 30) < 0.001) ok("A12", `total dropped by exactly the remise: ${totalBefore} → ${totalAfter}`)
  else bad("A12", `total moved by ${(totalBefore - totalAfter).toFixed(3)}, expected 30`, `${totalBefore} → ${totalAfter}`)
})

// A13 — « Régler le devis ».
S("A13", async () => {
  await openPlan("settle")
  const btn = page.getByRole("button", { name: /Régler le devis/i }).first()
  let target = btn
  if (!(await btn.count())) {
    const items = await openActionsMenu()
    if (!/Régler/i.test(items.join(" "))) {
      await closeMenu()
      return skip("A13", `no « Régler le devis » control; menu = ${items.join(" | ")}`)
    }
    target = page.locator('[role="menuitem"]', { hasText: /Régler/i }).first()
  }
  await target.click()
  await settle(800)
  const input = page
    .locator('[role="dialog"]:visible input[inputmode="decimal"], [role="alertdialog"]:visible input[inputmode="decimal"]')
    .first()
  if (!(await input.count())) return skip("A13", "the settle dialog has no decimal field")
  await input.fill("300")
  await settle(200)
  const save = page.locator('[role="dialog"]:visible button, [role="alertdialog"]:visible button').filter({ hasText: /Régler|Enregistrer|Encaisser|Confirmer/i }).last()
  await save.click()
  await settle(2500)
  await shot("A13")
  const after = await call("GET", `/treatment-plans/${state.plans.settle.id}`)
  const a = after.json?.value ?? after.json
  if (Math.abs(Number(a.outstanding)) < 0.001) ok("A13", `« Reste » reached 0 (collected ${a.amountPaid})`)
  else bad("A13", `outstanding is ${a.outstanding}, expected 0`, `amountPaid ${a.amountPaid}`)
})

// A14 — « Passer la créance en perte ».
S("A14", async () => {
  await openPlan("writeoff")
  const items = await openActionsMenu()
  if (!/Passer la créance en perte/i.test(items.join(" "))) {
    await closeMenu()
    return skip("A14", `« Passer la créance en perte » not offered; menu = ${items.join(" | ")}`)
  }
  await page.locator('[role="menuitem"]', { hasText: /Passer la créance en perte/i }).first().click()
  await settle(700)
  const ta = page.locator('[role="dialog"]:visible textarea, [role="alertdialog"]:visible textarea').first()
  const confirm = page.locator('[role="dialog"]:visible button, [role="alertdialog"]:visible button').filter({ hasText: /Passer|Confirmer/i }).last()
  if (await confirm.isDisabled().catch(() => false)) ok("A14b", "the confirm is disabled until a motif is typed")
  else bad("A14b", "the write-off confirm is enabled with no motif")
  if (await ta.count()) await ta.fill(`QA ${state.run} — irrécouvrable`)
  await settle(300)
  await confirm.click()
  await settle(2500)
  await shot("A14")
  const after = await call("GET", `/treatment-plans/${state.plans.writeoff.id}`)
  const a = after.json?.value ?? after.json
  if (a.status === "WrittenOff") ok("A14", `status is WrittenOff (« ${(await body()).match(/Créance abandonnée/i) ? "Créance abandonnée" : "?"} » on screen)`)
  else bad("A14", `status is ${a.status}, expected WrittenOff`)
})

// A18 — the praticien, named and editable.
S("A18", async () => {
  await openPlan("acts")
  const t = await body()
  const items = await openActionsMenu()
  await closeMenu()
  if (/Changer le praticien/i.test(items.join(" "))) ok("A18b", "« Changer le praticien » is offered")
  else bad("A18b", "no « Changer le praticien » entry", items.join(" | "))
  if (/Dr|praticien|Praticien/i.test(t)) ok("A18", "the header names a praticien")
  else bad("A18", "the workspace header names no praticien", t.slice(0, 300).replace(/\n/g, " | "))
})

// B1 — stopping a numbered devis with nothing delivered takes the CANCEL branch.
S("B1", async () => {
  await openPlan("stopcancel")
  const items = await openActionsMenu()
  if (!/Arrêter le traitement/i.test(items.join(" "))) {
    await closeMenu()
    return skip("B1", "« Arrêter le traitement » not offered")
  }
  await page.locator('[role="menuitem"]', { hasText: /Arrêter le traitement/i }).first().click()
  const t = await dialogText()
  await shot("B1")
  const confirm = page.locator('[role="alertdialog"]:visible button, [role="dialog"]:visible button').filter({ hasText: /Arrêter|Annuler le devis|Confirmer/i }).last()
  const disabled = await confirm.isDisabled().catch(() => null)
  if (disabled === true) ok("B1", "a motif is required on the cancel branch (confirm disabled)")
  else bad("B1", "the confirm is enabled with no motif on the cancel branch", t.replace(/\n/g, " | ").slice(0, 250))
  const field = page.locator('[role="alertdialog"]:visible textarea, [role="dialog"]:visible textarea').first()
  const described = (await field.count()) ? await field.getAttribute("aria-describedby") : null
  const hint = described ? await page.locator(`#${described}`).innerText().catch(() => "") : ""
  if (hint.trim()) ok("B1b", `the motif field is described by « ${hint.trim().slice(0, 80)} » (m12)`)
  else bad("B1b", "the motif field carries no aria-describedby hint (m12)", `describedby=${described}`)
  const ta = page.locator('[role="alertdialog"]:visible textarea, [role="dialog"]:visible textarea').first()
  if (await ta.count()) await ta.fill(`QA ${state.run} — rien de réalisé`)
  await settle(300)
  await confirm.click()
  await settle(2500)
  const after = await call("GET", `/treatment-plans/${state.plans.stopcancel.id}`)
  const st = (after.json?.value ?? after.json).status
  if (st === "Cancelled") ok("B1c", "nothing delivered on a numbered devis ⇒ Cancelled (AC-6)")
  else bad("B1c", `status is ${st}, expected Cancelled`)
})

// B5 — an auto-raised échéance renders « Solde à régler », no date, no « En retard ».
S("B5", async () => {
  await openPlan("acts")
  const t = await body()
  await shot("B5")
  if (/Solde à régler/i.test(t)) ok("B5", "the auto-raised row reads « Solde à régler »")
  else bad("B5", "no « Solde à régler » on a plan whose only échéance is auto-raised", t.slice(0, 400).replace(/\n/g, " | "))
  if (/En retard/i.test(t)) bad("B5b", "« En retard » appears on an auto-raised row (AC-8/AC-9)", t.slice(0, 300).replace(/\n/g, " | "))
  else ok("B5b", "no « En retard » on an auto-raised row")
  if (/Modifier l.échéancier/i.test(t)) ok("B5c", "the route to a real schedule is named (« Modifier l'échéancier »)")
  else bad("B5c", "a plan with only an auto-raised row names no route to a real schedule (AC-13)", t.slice(0, 250).replace(/\n/g, " | "))
})

// B6 — the « pourquoi Encaisser a disparu » notice names the real reason.
S("B6", async () => {
  await openPlan("cancelled")
  const t = await body()
  if (/annulé/i.test(t) && !/Encaisser/i.test(t)) ok("B6", "« Encaisser » withheld on a cancelled plan, and the page says why")
  else if (/Encaisser/i.test(t)) bad("B6", "« Encaisser » is still offered on a cancelled plan", t.slice(0, 250).replace(/\n/g, " | "))
  else bad("B6", "no reason given for the withheld « Encaisser »", t.slice(0, 250).replace(/\n/g, " | "))
})

// B9 — a plan with no schedulable act says why, rather than rendering blank.
S("B9", async () => {
  await openPlan("completed")
  const t = await body()
  await shot("B9")
  const heading = page.getByText(/Prochaine séance|Travail terminé|Devis annulé|Traitément arrêté|Créance abandonnée/i).first()
  if (!(await heading.count()))
    return bad("B9", "the plan renders NO next-séance block at all — M19 says the null tail names why there is nothing to do")
  const card = heading.locator("xpath=ancestor::*[self::section or self::div][1]")
  const block = await card.innerText().catch(() => t)
  if (block.replace(/Prochaine séance/i, "").trim().length > 10) ok("B9", `the null tail says: ${block.replace(/\n/g, " ").slice(0, 120)}`)
  else bad("B9", "the « Prochaine séance » block is empty on a plan with nothing to do", t.slice(0, 250).replace(/\n/g, " | "))
})

// C1 — a 409 must offer « Recharger ».
S("C1", async () => {
  await openPlan("conflict")
  const items = await openActionsMenu()
  if (!/Modifier les actes et les prix/i.test(items.join(" "))) {
    await closeMenu()
    return skip("C1", "the amend entry is not offered")
  }
  await page.locator('[role="menuitem"]', { hasText: /Modifier les actes et les prix/i }).first().click()
  await settle(1500)
  // Age the row from underneath the open form — a write NOBODY MADE spends the token (the xmin trap).
  const cur = await call("GET", `/treatment-plans/${state.plans.conflict.id}`)
  const c = cur.json?.value ?? cur.json
  const bump = await call("POST", `/treatment-plans/${state.plans.conflict.id}/amend`, {
    title: `QA ${state.run} · bumped`,
    version: c.version,
  })
  if (!bump.ok) return skip("C1", `could not age the row: ${bump.status} ${bump.text.slice(0, 160)}`)
  const save = page.locator('[role="dialog"]:visible button').filter({ hasText: /Enregistrer/i }).last()
  if (!(await save.count())) return skip("C1", "the amend modal has no « Enregistrer »")
  await save.click()
  await settle(2500)
  const dlg = await anyDialog().innerText().catch(() => "")
  await shot("C1")
  if (/modifié par quelqu'un d'autre|Recharger/i.test(dlg)) {
    if (/Recharger/i.test(dlg)) ok("C1", "the 409 banner offers « Recharger »")
    else bad("C1", "a 409 was shown with no « Recharger » action (the poisoned-dialog trap)", dlg.replace(/\n/g, " | ").slice(0, 300))
  } else bad("C1", "no conflict was surfaced after the row was aged", dlg.replace(/\n/g, " | ").slice(0, 300))
  await page.keyboard.press("Escape")
  await settle(500)
})

// C3 — every échéance surface at 320 px.
S("C3", async () => {
  await page.setViewportSize({ width: 320, height: 730 })
  for (const key of ["acts", "deposit", "cancelled"]) {
    await openPlan(key)
    const o = await overflow()
    await shot(`C3-${key}-320`, { fullPage: false })
    if (o.docW <= o.winW + 1) ok(`C3·${key}`, `no horizontal scroll at 320 px (doc ${o.docW})`)
    else bad(`C3·${key}`, `horizontal overflow at 320 px: doc ${o.docW} > window ${o.winW}`)
  }
  // ⚠️⚠️ A 44 px target is a COARSE-POINTER rule, and **CDP's `Emulation.setEmulatedMedia` does not
  // deliver it**: measured 2026-09-15, `matchMedia("(pointer: coarse)").matches` stayed **false** after that
  // call, with and without `media: "screen"`, so every `coarse:` rule was still switched off and every control
  // measured at its fine-pointer height. It reported seven false violations, the patient link among them — the
  // link resolves to exactly 44 px once the pointer really is coarse.
  //
  // `hasTouch: true` on the CONTEXT is what makes it match, and a context's touch flag cannot be changed after
  // creation — hence a second context for this one measurement.
  // ⚠️ Its OWN session — see `signIn({ name })`. Handing this context the walk's storageState rotated the
  // credential and signed the main page out, taking every later row with it.
  const touch = await signIn({ name: "touch" })
  const touchCtx = await browser.newContext({
    storageState: touch.sessionPath,
    viewport: { width: 320, height: 730 },
    locale: "fr-FR",
    hasTouch: true,
    isMobile: true,
  })
  await touchCtx.route("**/api/notifications/pending-reviews*", (r) =>
    r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  )
  const touchPage = await touchCtx.newPage()
  await touchPage.goto(planUrl("cancelled"), { waitUntil: "domcontentloaded" })
  await touchPage.getByRole("button", { name: "Autres actions sur ce devis" }).waitFor({ timeout: 25000 })
  await touchPage.waitForTimeout(1500)
  const coarse = await touchPage.evaluate(() => matchMedia("(pointer: coarse)").matches)
  if (!coarse) {
    await touchCtx.close()
    await page.setViewportSize({ width: 1440, height: 900 })
    return skip("C3·targets", "the coarse pointer never engaged — a target measurement here would be void")
  }
  const small = await touchPage.evaluate(() => {
    const out = []
    for (const el of document.querySelectorAll("button:not([hidden]), a[href], [role='menuitem']")) {
      const r = el.getBoundingClientRect()
      if (r.width === 0 && r.height === 0) continue
      if (r.height >= 44) continue
      // `.touch-target`'s ::after IS the hit area; a painted 32 px control carrying it is correct by design.
      const hit = parseFloat(getComputedStyle(el, "::after").minHeight || "0")
      if (el.classList.contains("touch-target") || hit >= 44) continue
      if (String(el.className).includes("sr-only")) continue // a skip link is 1 px until focused
      out.push(`${(el.textContent || el.getAttribute("aria-label") || "?").trim().slice(0, 40)} ${Math.round(r.height)}px`)
    }
    return out.slice(0, 12)
  })
  await touchPage.screenshot({ path: join(SHOTS, "C3-coarse-320.png"), animations: "disabled", timeout: 10000 }).catch(() => {})
  await touchCtx.close()
  if (!small.length) ok("C3·targets", "every visible control clears 44 px on a REAL coarse pointer at 320 px")
  else bad("C3·targets", `${small.length} control(s) under 44 px on a coarse pointer at 320 px with no touch-target`, small.join(" · "))
  await page.setViewportSize({ width: 1440, height: 900 })
})

// C4 — the workspace and its dialogs at the owner's real laptop height.
S("C4", async () => {
  await page.setViewportSize({ width: 1536, height: 730 })
  await openPlan("acts")
  const items = await openActionsMenu()
  void items
  await closeMenu()
  await page.locator('button[aria-label*="Actions"]:visible').first().click().catch(() => {})
  await settle(400)
  await page.keyboard.press("Escape")
  await openPlan("acts")
  const menu = await openActionsMenu()
  if (/Arrêter le traitement/i.test(menu.join(" "))) {
    await page.locator('[role="menuitem"]', { hasText: /Arrêter le traitement/i }).first().click()
    await dialogText().catch(() => "")
    await shot("C4-dialog-1536x730")
    const clipped = await page.evaluate(() => {
      const d = document.querySelector('[role="alertdialog"], [role="dialog"]')
      if (!d) return null
      const r = d.getBoundingClientRect()
      return { top: Math.round(r.top), bottom: Math.round(r.bottom), vh: window.innerHeight }
    })
    if (!clipped) skip("C4", "no dialog resolved")
    else if (clipped.bottom <= clipped.vh + 1 && clipped.top >= -1) ok("C4", `dialog fits 1536×730 (${clipped.top}…${clipped.bottom} of ${clipped.vh})`)
    else bad("C4", `dialog is clipped at 1536×730`, JSON.stringify(clipped))
    await page.keyboard.press("Escape")
  } else {
    await closeMenu()
    skip("C4", "no dialog reachable on this plan")
  }
  await page.setViewportSize({ width: 1440, height: 900 })
})

// C6 — a nine-act plan and a one-act plan both render.
S("C6", async () => {
  await openPlan("many")
  const t = await body()
  await shot("C6")
  const n = (t.match(/Acte \d/g) ?? []).length
  if (n >= 9) ok("C6", `all ${n} acts render`)
  else bad("C6", `only ${n} of 9 acts render`, t.slice(0, 300).replace(/\n/g, " | "))
  if (consoleErrors.length) bad("C6b", `${consoleErrors.length} console error(s) on a nine-act plan`, consoleErrors.slice(0, 3).join(" · "))
  else ok("C6b", "no console error on a nine-act plan")
})

// ── Tier D — the regression sweep, straight from the blast-radius table ────────────────────────────────
const sweep = [
  ["D1", "/caisse", /Caisse|Encaissements|Recettes/i],
  ["D2", "/creances", /Créances|Solde/i],
  ["D3", `/patients/${state.plans.acts.patientId}`, /Solde|Traitements|Fiche/i],
  ["D5", "/", /Tableau de bord|Rendez-vous|Aujourd/i],
  ["D8", "/traitements-en-cours", /Traitement/i],
]
for (const [id, path, expect] of sweep) {
  S(id, async () => {
    consoleErrors.length = 0
    await page.goto(`${ORIGIN}${path}`, { waitUntil: "domcontentloaded" })
    await settle(3000)
    const t = await body()
    await shot(id)
    if (/Chargement|Skeleton/i.test(t) && t.length < 400) {
      await settle(3000)
    }
    const t2 = await body()
    if (expect.test(t2)) ok(id, `${path} renders (${t2.length} chars)`)
    else bad(id, `${path} did not render its expected content`, t2.slice(0, 250).replace(/\n/g, " | "))
    if (consoleErrors.length) bad(`${id}b`, `${consoleErrors.length} console error(s) on ${path}`, consoleErrors.slice(0, 3).join(" · "))
    else ok(`${id}b`, `no console error on ${path}`)
    const o = await overflow()
    if (o.docW <= o.winW + 1) ok(`${id}c`, "no horizontal overflow")
    else bad(`${id}c`, `horizontal overflow on ${path}`, `doc ${o.docW} > window ${o.winW}`)
  })
}

// D2 detail — a Stopped plan present, a WrittenOff and a Cancelled one absent.
S("D2x", async () => {
  const r = await call("GET", "/treatment-plans?pageNumber=1&pageSize=200&status=Stopped")
  void r
  const w = await call("GET", `/treatment-plans/${state.plans.writeoff.id}`)
  const wf = (w.json?.value ?? w.json).status
  await page.goto(`${ORIGIN}/creances`, { waitUntil: "domcontentloaded" })
  await settle(3000)
  const t = await body()
  const wNum = state.plans.writeoff.number
  if (wf === "WrittenOff") {
    if (wNum && t.includes(wNum)) bad("D2x", `a WrittenOff devis (${wNum}) is still listed in « Créances »`)
    else ok("D2x", `the WrittenOff devis (${wNum}) is absent from « Créances » page 1`)
  } else skip("D2x", `the write-off plan is ${wf}, not WrittenOff — A14 did not land`)
  const cNum = state.plans.cancelled.number
  if (cNum && t.includes(cNum)) bad("D2y", `a Cancelled devis (${cNum}) is listed in « Créances »`)
  else ok("D2y", `the Cancelled devis (${cNum}) is absent from « Créances » page 1`)
})

// D6 — the odontogramme, both drawings, on a patient with charted work.
S("D6", async () => {
  consoleErrors.length = 0
  await page.goto(`${ORIGIN}/patients/${state.plans.acts.patientId}`, { waitUntil: "domcontentloaded" })
  await settle(3000)
  const tab = page.getByRole("tab", { name: /État dentaire/i }).first()
  if (!(await tab.count())) return skip("D6", "no « État dentaire » tab on this patient")
  await tab.click()
  await settle(2500)
  await shot("D6")
  const t = await body()
  const sw = page.getByRole("radio", { name: /Cases|Symboles/i })
  if (await sw.count()) ok("D6", `the Cases/Symboles switch is offered (${await sw.count()} options)`)
  else if (/Cases|Symboles/i.test(t)) ok("D6", "the Cases/Symboles switch is present")
  else bad("D6", "no Cases/Symboles switch on « État dentaire » (N37)", t.slice(0, 250).replace(/\n/g, " | "))
  if (consoleErrors.length) bad("D6b", `${consoleErrors.length} console error(s) on the odontogramme`, consoleErrors.slice(0, 3).join(" · "))
  else ok("D6b", "no console error on the odontogramme")
})

// A9 — the LIST row's edit of a followed Draft opens the AMEND modal.
S("A9", async () => {
  await page.goto(`${ORIGIN}/treatment-plans`, { waitUntil: "domcontentloaded" })
  await settle(3000)
  await dismissOverlays()
  const search = page.getByPlaceholder(/Rechercher un devis|Rechercher un traitement|Rechercher/i).last()
  if (await search.count()) {
    await search.fill(state.plans.acts.number)
    await settle(2500)
  }
  const row = page.locator("tr:visible, [data-slot='card']:visible").filter({ hasText: state.plans.acts.number }).first()
  if (!(await row.count())) return skip("A9", `the numbered devis ${state.plans.acts.number} is not on the list page`)
  const rowMenu = row.locator('button[aria-label*="Actions"]').first()
  if (!(await rowMenu.count())) return skip("A9", "no ⋯ menu on that row")
  await rowMenu.click()
  await settle(400)
  const menu = await page.locator('[role="menu"]').innerText()
  await shot("A9")
  if (/Modifier les actes et les prix/i.test(menu)) ok("A9", `the list row for ${state.plans.acts.number} offers « Modifier les actes et les prix » (C8/m15)`)
  else bad("A9", `the list row for the NUMBERED devis ${state.plans.acts.number} does not offer the amend entry`, menu.replace(/\n/g, " | "))
  await page.keyboard.press("Escape")
})

// ── drive ──────────────────────────────────────────────────────────────────────────────────────────────
console.log(`\n  walk · ${ORIGIN} · run ${state.run} · ${SCENARIOS.length} scenario blocks\n`)
for (const s of SCENARIOS) {
  if (ONLY && !ONLY.has(s.id)) continue
  try {
    await s.run()
  } catch (e) {
    bad(s.id, "threw", String(e.message).slice(0, 300))
    await shot(`${s.id}-threw`).catch(() => {})
  }
}

await browser.close()
console.log(findings.length ? `\n  ${findings.length} finding(s):\n` : "\n  ALL CLEAR\n")
findings.forEach((f) => console.log("   ", JSON.stringify(f)))
writeFileSync(join(HERE, ".artifacts", "findings.json"), JSON.stringify(findings, null, 2))
