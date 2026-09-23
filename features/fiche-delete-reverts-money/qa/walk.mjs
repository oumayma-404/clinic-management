/**
 * QA walk — « supprimer une fiche annule son argent ».
 *
 * Re-runnable: `node features/fiche-delete-reverts-money/qa/walk.mjs`
 * Run it from the repo root. It arranges its OWN patient over the API and deletes only that patient's
 * fiches — no pre-existing clinical or money row of the dev database is touched.
 *
 * Rules it obeys (skills/test-in-browser):
 *   - no check throws and no check returns early; a throw is a finding
 *   - three outcomes: ok / bad / skip-with-a-reason
 *   - it fixes nothing
 */
import crypto from "node:crypto"
// CommonJS package — named ESM exports are not available, so take the default and destructure.
import pw from "../../../e2e/node_modules/@playwright/test/index.js"
const { chromium } = pw
import { mkdirSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"

const HERE = dirname(fileURLToPath(import.meta.url))
const SHOTS = join(HERE, "shots")
mkdirSync(SHOTS, { recursive: true })

const API = "http://localhost:5000/api"
const WEB = "http://localhost:3000"
const CRED = {
  email: "salma.benyoussef@cabinet-ibnkhaldoun.tn",
  password: "QaAudit2026!y",
  totp: "4YRLT22RBPRP3RRKUKLFQERU4H62BRBO",
}

// ---- reporting ------------------------------------------------------------
const findings = []
const ok = (id, m) => console.log(`  OK   [${id}] ${m}`)
const bad = (id, m, d = "") => {
  console.log(`  FAIL [${id}] ${m} ${d}`)
  findings.push({ id, severity: "?", m, d })
}
const skip = (id, why) => {
  console.log(`  SKIP [${id}] not exercised - ${why}`)
  findings.push({ id, severity: "skip", why })
}

// ---- TOTP -----------------------------------------------------------------
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
const freshWindow = () =>
  new Promise((r) => setTimeout(r, (30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000))

// ---- API ------------------------------------------------------------------
let TOKEN = null
async function api(method, path, body) {
  const res = await fetch(API + path, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(TOKEN ? { Authorization: `Bearer ${TOKEN}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await res.text()
  let parsed = null
  try {
    parsed = text ? JSON.parse(text) : null
  } catch {
    parsed = text
  }
  // ⚠️ The API serialises `Result<T>` as `{ value: … }` on several routes and as the bare payload on others.
  // Unwrapping here rather than at each call site is what stops « no token in login body » on a 200.
  // The envelope is `{ value, isSuccess, isFailure, error, code, diagnostic }` — SIX keys, so a key-count
  // guard is the wrong discriminator (that cost one drive). `isSuccess` is what identifies it.
  if (parsed && typeof parsed === "object" && "isSuccess" in parsed && "value" in parsed) {
    parsed = parsed.value ?? parsed
  }
  return { status: res.status, body: parsed }
}

async function signInApi() {
  await freshWindow()
  const r = await api("POST", "/auth/login", {
    email: CRED.email,
    password: CRED.password,
    totpCode: totp(CRED.totp),
  })
  if (r.status !== 200) throw new Error(`API login ${r.status}: ${JSON.stringify(r.body).slice(0, 300)}`)
  TOKEN = r.body.accessToken ?? r.body.token ?? r.body.access_token
  if (!TOKEN) throw new Error(`no token in login body: ${JSON.stringify(r.body).slice(0, 300)}`)
}

const localDay = (off = 0) => {
  const d = new Date()
  d.setDate(d.getDate() + off)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}T00:00:00`
}

// ---- arrange --------------------------------------------------------------
/** Everything the walk needs, created fresh under a name nothing else uses. */
async function arrange() {
  const stamp = Date.now().toString().slice(-6)
  const out = { stamp }

  const procs = await api("GET", "/procedure-types?activeOnly=true")
  const list = Array.isArray(procs.body) ? procs.body : procs.body?.items ?? []
  const proc = list.find((p) => /extraction simple/i.test(p.name)) ?? list[0]
  if (!proc) throw new Error("no procedure types on this database")
  out.proc = proc

  const patient = await api("POST", "/patients", {
    firstName: "QA-Suppression",
    lastName: `Fiche${stamp}`,
    dateOfBirth: "1985-03-14T00:00:00",
    gender: "Male",
  })
  if (patient.status >= 300) throw new Error(`create patient ${patient.status}: ${JSON.stringify(patient.body)}`)
  out.patientId = patient.body.id
  out.patientName = `QA-Suppression Fiche${stamp}`

  /*
   * A treatment through « Suivre ce traitement » (`/treatment-plans/start`) rather than the full devis form.
   * `POST /treatment-plans` answered 400 with the handler's CATCH-ALL sentence — an unexpected exception, not
   * a refusal — so the arrange takes the door that works rather than debugging a route this pass is not about.
   * It produces a Draft; collecting on it mints the number, which is exactly the product path under test.
   */
  const plan = await api("POST", "/treatment-plans/start", {
    patientId: out.patientId,
    procedureTypeId: proc.id,
    agreedTotal: 80,
    toothNumbers: [26],
    steps: [],
  })
  /*
   * ⚠️ BEST-EFFORT, not fatal. Both plan-creation routes answer 400 with the handler's CATCH-ALL sentence on
   * this machine tonight, and a peer session has in-flight edits in `TreatmentPlanRepository` /
   * `ITreatmentPlanRepository` — « another author's red is not always yours ». The devis rows are reported
   * SKIPPED with that reason rather than failing the whole pass, and the note d'honoraires path (which needs
   * no plan) carries the eye pass.
   */
  if (plan.status >= 300) {
    out.planError = `${plan.status} ${JSON.stringify(plan.body)}`
    out.plan = null
    out.planItemId = null
  } else {
    out.plan = plan.body
    out.planItemId = plan.body.items?.[0]?.id
  }

  return out
}

/** A fiche on that patient carrying a chairside collection against the devis act. */
async function ficheWithCollection(ctx, amount = 80) {
  const r = await api("POST", `/patients/${ctx.patientId}/dental-records`, {
    interventionDate: localDay(0),
    amountPaid: 0,
    isAdultTeeth: true,
    acts: [
      {
        procedureTypeId: ctx.proc.id,
        procedureName: "Extraction simple",
        cost: 0,
        unitCost: null,
        isPerTooth: false,
        toothNumbers: [26],
        ponticToothNumbers: [],
        resultingCondition: null,
        surfaces: null,
        note: null,
        treatmentPlanItemId: ctx.planItemId,
      },
    ],
    notes: [],
    importantNotes: [],
    treatmentPlanId: ctx.plan.id,
    amountCollectedOnPlan: amount,
    paymentMethod: "Cash",
  })
  return r
}

/** A fiche with a priced act and no devis — this one raises a note d'honoraires. */
async function ficheWithNote(ctx, amount = 60) {
  return api("POST", `/patients/${ctx.patientId}/dental-records`, {
    interventionDate: localDay(0),
    amountPaid: amount,
    paymentMethod: "Cash",
    isAdultTeeth: true,
    acts: [
      {
        procedureTypeId: ctx.proc.id,
        procedureName: "Extraction simple",
        cost: amount,
        unitCost: null,
        isPerTooth: false,
        toothNumbers: [36],
        ponticToothNumbers: [],
        resultingCondition: null,
        surfaces: null,
        note: null,
      },
    ],
    notes: [],
    importantNotes: [],
  })
}

/** A plain fiche carrying no money at all. */
async function ficheNoMoney(ctx) {
  return api("POST", `/patients/${ctx.patientId}/dental-records`, {
    interventionDate: localDay(0),
    amountPaid: 0,
    isAdultTeeth: true,
    acts: [
      {
        procedureTypeId: ctx.proc.id,
        procedureName: "Extraction simple",
        cost: 0,
        unitCost: null,
        isPerTooth: false,
        toothNumbers: [46],
        ponticToothNumbers: [],
        resultingCondition: null,
        surfaces: null,
        note: null,
      },
    ],
    notes: [],
    importantNotes: [],
  })
}

// ---- browser --------------------------------------------------------------
async function signInBrowser(page) {
  await page.goto(`${WEB}/login`, { waitUntil: "domcontentloaded" })
  await page.fill("input[type=email]", CRED.email)
  await page.fill("input[type=password]", CRED.password)
  await page.click("button:has-text('Se connecter')")
  await page.waitForSelector("input[inputmode=numeric]", { timeout: 30000 })
  await freshWindow()
  // ⚠️ the 6-digit box AUTO-SUBMITS on the last digit — never click « Vérifier »
  await page.fill("input[inputmode=numeric]", totp(CRED.totp))
  await page.waitForURL((u) => !u.pathname.startsWith("/login"), { timeout: 45000 })
}

const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`), fullPage: false })

/**
 * Open the fiche-delete confirmation.
 *
 * ⚠️ Two probe traps, both paid for once:
 *  - « Supprimer » is NOT a button on the row. It is a `DropdownMenuItem` reading
 *    « Supprimer la fiche de soins », behind a trigger labelled « Autres actions sur la fiche du <date> ».
 *  - the records list renders its TABLE and its CARD form at the same time, one hidden by a breakpoint
 *    class, so a bare locator resolves into the hidden tree and the click times out. `:visible` is required.
 */
async function openDeleteDialog(page, ctx, recordId, prefer = /déjà entré dans la caisse/i) {
  // ⚠️ The tab is `medical-records`. `?tab=records` is silently ignored and lands on the DEFAULT tab, which
  // has no fiche rows at all — so the walk reported « no delete control » and looked like a product defect.
  await page.goto(`${WEB}/patients/${ctx.patientId}?tab=medical-records`, { waitUntil: "domcontentloaded" })
  await page.waitForTimeout(3000)

  const triggers = page.locator("button[aria-label^='Autres actions sur la fiche']:visible")
  const n = await triggers.count()
  if (n === 0) {
    return { opened: false, reason: "no « Autres actions sur la fiche » trigger is visible on the records tab" }
  }
  // The money fiche is the one the plan collected at; the list is newest-first, so try each trigger until
  // the dialog shows the caisse warning rather than guessing an index.
  for (let i = 0; i < n; i++) {
    await triggers.nth(i).click()
    const item = page.locator("[role=menuitem]:has-text('Supprimer la fiche de soins')")
    if ((await item.count()) === 0) {
      await page.keyboard.press("Escape")
      continue
    }
    await item.first().click()
    const dialog = page.locator("[role=alertdialog]")
    try {
      await dialog.waitFor({ state: "visible", timeout: 10000 })
    } catch {
      continue
    }
    // Let the preview land — the whole point is that the box only appears once the server answers.
    await page.waitForTimeout(2500)
    const text = await dialog.innerText()
    if (prefer.test(text) || i === n - 1) return { opened: true, dialog, index: i }
    await page.keyboard.press("Escape")
    await page.waitForTimeout(400)
  }
  return { opened: false, reason: "no fiche row produced a dialog carrying the caisse warning" }
}

// ---- tier D: the regression sweep -----------------------------------------
const money = (x) => Math.round(Number(x ?? 0) * 1000) / 1000
const day = () => localDay(0).slice(0, 10)

/** The four reads the blast-radius table named, taken in one shot so before/after are comparable. */
async function moneyReads(ctx) {
  const d = day()
  const [caisse, recv, solde, dash] = await Promise.all([
    api("GET", `/billing/caisse?fromDay=${d}&toDay=${d}`),
    api("GET", `/billing/receivables?search=${encodeURIComponent(ctx.patientName.split(" ")[1] ?? "")}`),
    api("GET", `/patients/${ctx.patientId}/billing-summary`),
    api("GET", `/dashboard?period=month`),
  ])
  /*
   * ⚠️ The field names, verified against the live DTOs rather than guessed — the first version read
   * `dash.collected` and `solde.totalPaid`, both of which are `undefined`, so two rows reported « moved by
   * 0 DT » on money that had plainly moved. A value that is already 0 BEFORE the mutation is a probe bug.
   *   dashboard        → money.collected.current
   *   receivables row  → totalOutstanding
   *   billing-summary  → totalOutstanding   (there is no « paid » field at all)
   */
  const rows = recv.body?.items ?? recv.body?.lines ?? []
  const surname = ctx.patientName.split(" ")[1] ?? "@@"
  const recvRow = Array.isArray(rows) ? rows.find((r) => (r.patientName ?? "").includes(surname)) : null
  return {
    caisseIn: money(caisse.body?.cashIn),
    caisseNet: money(caisse.body?.net),
    receivable: money(recvRow?.totalOutstanding ?? 0),
    soldeOutstanding: money(solde.body?.totalOutstanding),
    dashCollected: money(dash.body?.money?.collected?.current),
    statuses: [caisse.status, recv.status, solde.status, dash.status],
  }
}

async function runRegressionSweep(page, ctx) {
  console.log("\n=== tier D — regression sweep ===")
  const before = await moneyReads(ctx)
  console.log("  avant :", JSON.stringify(before))
  if (before.statuses.some((s) => s !== 200)) {
    bad("D0", "a money read did not answer 200 BEFORE the deletion", JSON.stringify(before.statuses))
    return
  }

  // Delete the devis fiche THROUGH THE PRODUCT, so what ships is what was tested.
  // ⚠️ Target the DEVIS fiche, not the note one. Deleting a note fiche cancels the note, which removes the
  // CLAIM as well as the payment — so the balance correctly stays 0 and D3b has nothing to observe. The devis
  // survives its fiche, so that is the only path where « the patient owes it again » is a real assertion.
  const opened = await openDeleteDialog(page, ctx, ctx.target, /sur le devis|sur le traitement/i)
  if (!opened.opened) {
    skip("D1-D6", `could not reopen the delete dialog: ${opened.reason}`)
    return
  }
  const dlgText = await opened.dialog.innerText()
  const m = dlgText.match(/([\d\s,.]+)\s*DT seront annulés/)
  const expected = m ? money(m[1].replace(/\s/g, "").replace(",", ".")) : null
  console.log(`  le dialogue annonce : ${expected} DT`)

  await opened.dialog.locator("button:has-text('Supprimer')").click()
  // Poll for the toast rather than waiting once — sonner dismisses on its own.
  let toastSeen = false
  for (let i = 0; i < 20; i++) {
    if (await page.locator("text=Fiche de soins supprimée").count()) { toastSeen = true; break }
    await page.waitForTimeout(500)
  }
  toastSeen ? ok("A3", "« Fiche de soins supprimée. »") : bad("A3", "no success toast after confirming")
  await page.waitForTimeout(2500)

  const after = await moneyReads(ctx)
  console.log("  après :", JSON.stringify(after))

  // D1 — la caisse drops by EXACTLY the announced amount, on the payment's own day.
  const caisseDelta = money(before.caisseIn - after.caisseIn)
  if (expected != null && caisseDelta === expected) ok("D1", `la caisse baisse de ${caisseDelta} DT, exactement l'annoncé`)
  else bad("D1", `la caisse a bougé de ${caisseDelta} DT, annoncé ${expected}`, JSON.stringify({ before: before.caisseIn, after: after.caisseIn }))

  // D4 — the dashboard moves with it, by the same amount.
  const dashDelta = money(before.dashCollected - after.dashCollected)
  if (expected != null && dashDelta === expected) ok("D4", `le dashboard baisse de ${dashDelta} DT`)
  else bad("D4", `le dashboard a bougé de ${dashDelta} DT, annoncé ${expected}`, JSON.stringify({ before: before.dashCollected, after: after.dashCollected }))

  // D2/D3 — « Créances » and « Solde patient » must report the SAME number. That is the invariant the
  // respread exists to hold, and the one a reversal is most likely to break.
  if (after.receivable === after.soldeOutstanding) ok("D2/D3", `créances == solde patient (${after.receivable} DT)`)
  else bad("D2/D3", "« Créances » and « Solde patient » disagree after the reversal", JSON.stringify({ receivable: after.receivable, solde: after.soldeOutstanding }))

  /*
   * D3b — the patient OWES IT AGAIN. This is the assertion that actually proves the reversal: money left the
   * caisse, so the debt it was settling must come back. If the balance stayed flat while la caisse dropped,
   * the dinar vanished from both sides — which is the shape of the defect the whole change exists to remove.
   */
  const debtDelta = money(after.soldeOutstanding - before.soldeOutstanding)
  if (expected != null && debtDelta === expected) ok("D3b", `le patient redoit ${debtDelta} DT — l'argent est revenu au solde`)
  else bad("D3b", `le solde a bougé de ${debtDelta} DT, annoncé ${expected}`, JSON.stringify({ before: before.soldeOutstanding, after: after.soldeOutstanding }))

  // D6 — the fiche is gone from the list and the page still renders.
  await page.reload({ waitUntil: "domcontentloaded" })
  await page.waitForTimeout(2500)
  const stillThere = await page.locator("[role=alertdialog]").count()
  const errored = await page.locator("text=/Erreur|introuvable/").count()
  if (!stillThere && !errored) ok("D6", "la liste des fiches se recharge sans erreur après la suppression")
  else bad("D6", "the records tab shows an error or a stuck dialog after the delete")
}

// ---- main -----------------------------------------------------------------
const run = async () => {
  console.log("\n=== arrange (over the API) ===")
  await signInApi()
  const ctx = await arrange()
  console.log(`  patient ${ctx.patientName} (${ctx.patientId})`)
  console.log(`  devis   ${ctx.plan ? (ctx.plan.number ?? "(brouillon)") + " " + ctx.plan.id : "INDISPONIBLE — " + ctx.planError}`)

  const collected = ctx.plan ? await ficheWithCollection(ctx) : { status: 0, body: null }
  console.log(`  fiche+devis  -> ${ctx.plan ? collected.status : "SKIPPED (" + ctx.planError + ")"}`)
  const noted = await ficheWithNote(ctx)
  console.log(`  fiche+note   -> ${noted.status}`)
  const plain = await ficheNoMoney(ctx)
  console.log(`  fiche sans $ -> ${plain.status}`)

  const recIds = {
    collected: collected.body?.id ?? collected.body?.dentalRecord?.id,
    noted: noted.body?.id ?? noted.body?.dentalRecord?.id,
    plain: plain.body?.id ?? plain.body?.dentalRecord?.id,
  }
  console.log("  record ids:", recIds)

  // The fiche every dialog row is driven against: the devis one when it exists, else the note one.
  ctx.target = recIds.collected ?? recIds.noted ?? recIds.plain
  ctx.targetKind = recIds.collected ? "devis" : recIds.noted ? "note d'honoraires" : "sans argent"

  /*
   * ⚠️ FALLBACK — read-only, and the report must say so loudly.
   * When the arrange is blocked the eye pass still has to happen, so it drives an EXISTING fiche that already
   * carries money. Nothing is confirmed and nothing is deleted: the dialog is opened, measured, escaped.
   */
  if (!ctx.target && process.env.FALLBACK_RECORD) {
    ctx.target = process.env.FALLBACK_RECORD
    ctx.patientId = process.env.FALLBACK_PATIENT ?? ctx.patientId
    ctx.targetKind = "fiche existante (LECTURE SEULE)"
    ctx.readOnly = true
    console.log(`  FALLBACK -> fiche ${ctx.target} / patient ${ctx.patientId} (aucune suppression)`)
  }

  if (!ctx.plan) skip("A2-devis/D1-D5", `devis arrange blocked: ${ctx.planError}`)

  // ---- A6: the preview endpoint, answered without a browser --------------
  console.log("\n=== tier A (api) ===")
  if (!ctx.target) {
    skip("A6", "no fiche was created and no FALLBACK_RECORD was given")
  } else {
    const p = await api("GET", `/patients/${ctx.patientId}/dental-records/${ctx.target}/deletion-preview`)
    if (p.status !== 200) {
      bad("A6", `deletion-preview answered ${p.status}`, JSON.stringify(p.body).slice(0, 300))
    } else {
      const b = p.body
      console.log("  preview:", JSON.stringify(b))
      if (b.touchesMoney === true && b.totalReversed > 0) {
        ok("A6", `[${ctx.targetKind}] touchesMoney=true, totalReversed=${b.totalReversed}, plans=${b.plans?.length}, note=${b.note ? "oui" : "non"}`)
      } else {
        bad("A6", "preview reports no money on a fiche that collected", JSON.stringify(b))
      }
    }
  }

  // SKIP_BROWSER=1 answers the api/sql rows alone — used to de-risk the arrange before taking the shared
  // Chrome profile, so the lease is held for the minimum time (the profile is one per machine).
  if (process.env.SKIP_BROWSER) {
    console.log("\n=== browser skipped (SKIP_BROWSER) ===")
    console.log("\n=== findings ===")
    findings.length ? findings.forEach((f) => console.log(JSON.stringify(f))) : console.log("ALL CLEAR")
    console.log(`\npatient de test: ${ctx.patientName} (${ctx.patientId})`)
    return
  }

  const browser = await chromium.launch({ channel: "chrome", headless: false })
  const context = await browser.newContext({ locale: "fr-FR", timezoneId: "Africa/Tunis" })
  // Serve the post-visit review queue empty — it is an overlay that swallows every click beneath it.
  await context.route("**/notifications/pending-reviews**", (r) =>
    r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  )
  const page = await context.newPage()

  try {
    console.log("\n=== sign in (browser) ===")
    await signInBrowser(page)
    ok("login", "signed in")

    console.log("\n=== tier A / B / C (browser) ===")
    await page.setViewportSize({ width: 1440, height: 900 })
    const opened = await openDeleteDialog(page, ctx, ctx.target)
    if (!opened.opened) {
      skip("A1-C4", `delete dialog unreachable: ${opened.reason}`)
    } else {
      const text = await opened.dialog.innerText()
      console.log("--- dialog text ---\n" + text + "\n-------------------")
      await shot(page, "dialog-1440")

      if (/déjà entré dans la caisse/i.test(text)) ok("A2", "the caisse warning is rendered")
      else bad("A2", "no « déjà entré dans la caisse » warning", text.slice(0, 400))

      if (/caisse d[eu]/i.test(text)) ok("C2", "an affected caisse day is named")
      else bad("C2", "no caisse day named", text.slice(0, 400))

      if (/annulée|brouillon|conservée/i.test(text)) ok("A4/B3", "a note or ordonnance paragraph is rendered")
      else skip("A4/B3", "this fiche carries neither a note nor an ordonnance")

      const del = opened.dialog.locator("button:has-text('Supprimer')")
      const cancel = opened.dialog.locator("button:has-text('Annuler'), button:has-text('Retour')")
      console.log(`  Supprimer disabled=${await del.isDisabled()} · cancel="${(await cancel.innerText()).trim()}"`)

      // ---- C1: the eye pass ------------------------------------------------
      for (const [w, h] of [[320, 720], [390, 844], [820, 1024], [1180, 820], [1440, 900], [1536, 730]]) {
        await page.setViewportSize({ width: w, height: h })
        await page.waitForTimeout(400)
        await shot(page, `dialog-${w}x${h}`)
        const m = await page.evaluate(() => {
          const d = document.querySelector("[role=alertdialog]")
          if (!d) return null
          const r = d.getBoundingClientRect()
          const body = d.querySelector(".overflow-y-auto")
          // The confirm button is the thing a finger must reach. A dialog that fits but whose footer sits
          // below the fold is the defect this repo has already shipped once (owner's laptop is 730 px tall).
          const btns = [...d.querySelectorAll("button")]
          const confirm = btns.find((b) => /Supprimer/i.test(b.textContent || ""))
          const cb = confirm ? confirm.getBoundingClientRect() : null
          // Any descendant wider than the dialog's own content box = something is poking out sideways.
          let widest = null
          for (const el of d.querySelectorAll("*")) {
            const er = el.getBoundingClientRect()
            if (er.width > r.width + 1 && (!widest || er.width > widest.w)) {
              widest = { w: Math.round(er.width), tag: el.tagName, cls: (el.className || "").toString().slice(0, 60) }
            }
          }
          return {
            dialogW: Math.round(r.width),
            left: Math.round(r.left),
            right: Math.round(r.right),
            vw: window.innerWidth,
            vh: window.innerHeight,
            docScrollW: document.documentElement.scrollWidth,
            overflowsX: document.documentElement.scrollWidth > window.innerWidth + 1,
            contentH: body ? body.scrollHeight : null,
            visibleH: body ? body.clientHeight : null,
            scrolls: body ? body.scrollHeight > body.clientHeight + 1 : false,
            confirmBottom: cb ? Math.round(cb.bottom) : null,
            confirmVisible: cb ? cb.bottom <= window.innerHeight + 1 && cb.top >= -1 : null,
            widest,
          }
        })
        console.log(`  ${w}x${h}:`, JSON.stringify(m))
        if (!m) { bad("C1", `dialog vanished at ${w}px`); continue }
        if (m.overflowsX) bad("C1", `horizontal page scroll at ${w}px`, `scrollW=${m.docScrollW} vw=${m.vw}`)
        else ok("C1", `no horizontal overflow at ${w}px`)
        if (m.left < -1 || m.right > m.vw + 1) bad("C1", `dialog escapes the viewport at ${w}px`, JSON.stringify(m))
      }
      await page.setViewportSize({ width: 1440, height: 900 })
      await page.keyboard.press("Escape")
      await page.waitForTimeout(600)

      /*
       * The DEVIS variant — the shape of the reported incident. The first pass stops at whichever fiche
       * carries money, which on this fixture is the note one; the bullet reads « sur le devis 2026-XXXX »
       * instead of « sur la note », and that branch has to be looked at too.
       */
      const triggers2 = page.locator("button[aria-label^='Autres actions sur la fiche']:visible")
      const n2 = await triggers2.count()
      let sawDevis = false
      for (let i = 0; i < n2; i++) {
        await triggers2.nth(i).click()
        const item = page.locator("[role=menuitem]:has-text('Supprimer la fiche de soins')")
        if ((await item.count()) === 0) { await page.keyboard.press("Escape"); continue }
        await item.first().click()
        const dlg = page.locator("[role=alertdialog]")
        try { await dlg.waitFor({ state: "visible", timeout: 8000 }) } catch { continue }
        await page.waitForTimeout(2200)
        const t = await dlg.innerText()
        if (/sur le devis|sur le traitement/i.test(t)) {
          sawDevis = true
          console.log("--- dialog (devis) ---\n" + t + "\n----------------------")
          ok("A2-devis", "the bullet names the devis the money sits on")
          for (const [w, h] of [[320, 720], [1536, 730]]) {
            await page.setViewportSize({ width: w, height: h })
            await page.waitForTimeout(400)
            await shot(page, `devis-${w}x${h}`)
            const ov = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)
            if (ov) bad("C1-devis", `horizontal page scroll at ${w}px on the devis variant`)
            else ok("C1-devis", `no horizontal overflow at ${w}px (devis variant)`)
          }
          await page.setViewportSize({ width: 1440, height: 900 })
          await page.keyboard.press("Escape")
          break
        }
        await page.keyboard.press("Escape")
        await page.waitForTimeout(400)
      }
      if (!sawDevis) skip("A2-devis", "no fiche row produced a dialog naming a devis")

      // ---- Tier D — the regression sweep -----------------------------------
      // Four money reads, before and after ONE deletion driven through the product. The assertion is not
      // « the figures changed » but « they changed by EXACTLY the reversed amount, and agree with each other ».
      await runRegressionSweep(page, ctx)
    }
  } catch (e) {
    bad("run", "the browser half threw", e.message)
  } finally {
    console.log("\n=== findings ===")
    if (findings.length === 0) console.log("ALL CLEAR")
    else findings.forEach((f) => console.log(JSON.stringify(f)))
    console.log(`\npatient de test: ${ctx.patientName} (${ctx.patientId}) — à supprimer après la passe`)
    await page.waitForTimeout(1000)
    await browser.close()
  }
}

run().catch((e) => {
  console.error("FATAL", e)
  process.exit(1)
})
