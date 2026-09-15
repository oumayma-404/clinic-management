// The plan's `api` / `sql` rows — answered BEFORE the browser opens (verification.md § 5).
// B2 · B3 · C5, plus the arrange for the browser walk.
import { signIn } from "./auth.mjs"
import { makeArranger, RUN } from "./arrange.mjs"
import { writeFileSync } from "node:fs"
import { join, dirname } from "node:path"
import { fileURLToPath } from "node:url"

const HERE = dirname(fileURLToPath(import.meta.url))

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = "") => {
  console.log(`  ❌ [${id}] ${m} ${d}`)
  findings.push({ id, m, d })
}
const skip = (id, why) => {
  console.log(`  ⏭  [${id}] not exercised — ${why}`)
  findings.push({ id, why })
}

const { token } = await signIn()
const A = makeArranger(token)
console.log(`\n  run ${RUN}\n`)

const types = await A.procedureTypes()
const pick = (re) => types.find((t) => re.test(t.name ?? t.nameFr ?? ""))
const simple = pick(/détartrage|detartrage/i) ?? types[0]
const stepped = types.find((t) => (t.steps?.length ?? 0) > 1) ?? pick(/couronne/i) ?? types[1]
console.log(`  catalogue: simple=${simple?.name} · stepped=${stepped?.name} (${types.length} types)`)

const state = { run: RUN, plans: {}, patients: {} }

// ── Arrange: one patient per lifecycle branch, so a row cannot disturb its neighbour ───────────────────
const mk = async (suffix, title, items) => {
  const p = await A.patient(suffix)
  state.patients[suffix] = { id: p.id, name: `${p.firstName} ${p.lastName}` }
  const plan = await A.plan({ patientId: p.id, title, items })
  state.plans[suffix] = { id: plan.id, number: plan.number, patientId: p.id, status: plan.status }
  return { patient: p, plan }
}

const line = (designation, cost, teeth = []) => ({
  procedureTypeId: simple?.id,
  designationFr: designation,
  plannedCost: cost,
  toothNumbers: teeth,
})

// ── B2 — reopen refused on a live plan ─────────────────────────────────────────────────────────────────
try {
  const { plan } = await mk("b2", `QA ${RUN} · reopen refusal`, [line("Détartrage", 120)])
  const r = await A.call("POST", `/treatment-plans/${plan.id}/reopen`, {})
  if (r.ok) bad("B2", "reopen was ACCEPTED on a live plan", `status ${r.status}`)
  else if (/peut être repris/i.test(r.text)) ok("B2", `refused: ${r.status} — sentence matches AC-2`)
  else bad("B2", `refused but with the wrong sentence (${r.status})`, r.text.slice(0, 250))
} catch (e) {
  bad("B2", "threw", e.message)
}

// ── B3 — a remise greater than the act's PlannedCost ────────────────────────────────────────────────────
try {
  const { plan } = await mk("b3", `QA ${RUN} · remise bounds`, [line("Détartrage", 100)])
  const item = plan.items[0]
  const over = await A.call("PUT", `/treatment-plans/${plan.id}/items/${item.id}/discount`, {
    discountAmount: 150,
  })
  if (over.ok) bad("B3", "a remise ABOVE the act's cost was accepted", JSON.stringify(over.json?.value?.items?.[0]))
  else ok("B3", `remise > plannedCost refused: ${over.status} ${String(over.text).slice(0, 120)}`)

  const fine = await A.call("PUT", `/treatment-plans/${plan.id}/items/${item.id}/discount`, {
    discountAmount: 25,
  })
  if (!fine.ok) bad("B3b", "a legitimate 25 DT remise was refused", `${fine.status} ${fine.text.slice(0, 200)}`)
  else {
    const after = fine.json?.value ?? fine.json
    const net = after.items[0].netCost ?? after.items[0].plannedCost
    if (Math.abs(Number(after.totalPlanned) - 75) < 0.001) ok("B3b", `remise applied — totalPlanned ${after.totalPlanned}, net ${net}`)
    else bad("B3b", `remise applied but totalPlanned is ${after.totalPlanned}, expected 75`, JSON.stringify(after.items[0]))
  }
  state.plans.b3.itemId = item.id
} catch (e) {
  bad("B3", "threw", e.message)
}

// ── C5 — an update DTO with the acts key ABSENT means « unchanged » ─────────────────────────────────────
try {
  const { plan } = await mk("c5", `QA ${RUN} · tri-state`, [line("Détartrage", 80), line("Composite", 140, [16])])
  const before = await A.get(plan.id)
  const r = await A.call("POST", `/treatment-plans/${plan.id}/amend`, {
    title: `QA ${RUN} · tri-state (renamed)`,
    version: before.version,
  })
  if (!r.ok) {
    skip("C5", `amend with no items key was refused (${r.status}) — ${String(r.text).slice(0, 160)}`)
  } else {
    const after = await A.get(plan.id)
    if (after.items.length === before.items.length && Number(after.totalPlanned) === Number(before.totalPlanned))
      ok("C5", `acts unchanged across an amend that omitted them (${after.items.length} acts, ${after.totalPlanned})`)
    else
      bad(
        "C5",
        "omitting the acts key CHANGED the acts",
        `before ${before.items.length}/${before.totalPlanned} → after ${after.items.length}/${after.totalPlanned}`,
      )
  }
} catch (e) {
  bad("C5", "threw", e.message)
}

// ── Arrange the browser rows' data ─────────────────────────────────────────────────────────────────────
try {
  // A3/A4 — a plan carrying a deposit, to be stopped then reopened in the browser.
  const { plan: dep } = await mk("deposit", `QA ${RUN} · acompte`, [line("Couronne", 600), line("Détartrage", 120)])
  const inst = dep.installments?.[0]
  if (inst) {
    const pay = await A.call("POST", `/treatment-plans/${dep.id}/installments/${inst.id}/payments`, {
      amount: 200,
      method: "Cash",
      paidOn: new Date().toISOString(),
    })
    if (!pay.ok) console.log(`  ⚠️  deposit payment failed: ${pay.status} ${pay.text.slice(0, 200)}`)
    else state.plans.deposit.paid = 200
  }

  // A3/A4/B10 — a devis the arrêt can LAND on: one act carried out (so `kept` is non-empty), one still to do
  // (so there is something to park), and a deposit (so the reopened balance is worth reading).
  const { plan: stoppable } = await mk("stoppable", `QA ${RUN} · arrêtable`, [
    line("Détartrage", 150),
    line("Composite", 250, [24]),
  ])
  {
    const first = stoppable.items[0]
    const d = await A.call("POST", `/treatment-plans/${stoppable.id}/items/${first.id}/done`, {
      doneOn: new Date().toISOString(),
    })
    if (!d.ok) console.log(`  ⚠️  could not deliver an act on the stoppable plan: ${d.status} ${d.text.slice(0, 160)}`)
    const inst = stoppable.installments?.[0]
    if (inst) {
      const pay = await A.call("POST", `/treatment-plans/${stoppable.id}/installments/${inst.id}/payments`, {
        amount: 100,
        method: "Cash",
        paidOn: new Date().toISOString(),
      })
      if (pay.ok) state.plans.stoppable.paid = 100
    }
    const live = await A.get(stoppable.id)
    state.plans.stoppable.totalPlanned = Number(live.totalPlanned)
    state.plans.stoppable.status = live.status
  }

  // A5 — cancel-with-money uses the same shape, on its own patient.
  const { plan: money } = await mk("cancelmoney", `QA ${RUN} · annuler avec argent`, [line("Couronne", 450)])
  const mi = money.installments?.[0]
  if (mi) {
    const pay = await A.call("POST", `/treatment-plans/${money.id}/installments/${mi.id}/payments`, {
      amount: 150,
      method: "Cash",
      paidOn: new Date().toISOString(),
    })
    if (pay.ok) state.plans.cancelmoney.paid = 150
  }

  // A6/B8 — an already-cancelled plan, for uncancel and the « Devis annulé » block.
  const { plan: canc } = await mk("cancelled", `QA ${RUN} · déjà annulé`, [line("Composite", 180)])
  const c = await A.call("POST", `/treatment-plans/${canc.id}/cancel`, {
    reason: `QA ${RUN} — annulé pour le test`,
  })
  if (!c.ok) console.log(`  ⚠️  could not pre-cancel: ${c.status} ${c.text.slice(0, 200)}`)
  else state.plans.cancelled.status = "Cancelled"

  // A7/A8/A12 — park/restore and a remise, on a multi-act plan.
  const { plan: acts } = await mk("acts", `QA ${RUN} · actes`, [
    line("Détartrage", 120),
    line("Composite", 200, [26]),
    line("Extraction", 90, [38]),
  ])
  state.plans.acts.itemIds = acts.items.map((i) => i.id)

  // A11 — duplicate source.
  const { plan: dup } = await mk("duplicate", `QA ${RUN} · à dupliquer`, [line("Détartrage", 120), line("Composite", 200)])
  state.plans.duplicate.itemCount = dup.items.length

  // A13/A14 — régler, and write-off.
  await mk("settle", `QA ${RUN} · à régler`, [line("Détartrage", 300)])
  await mk("writeoff", `QA ${RUN} · à passer en perte`, [line("Couronne", 500)])

  // B1 — a numbered devis with nothing delivered, for the stop→cancel branch.
  await mk("stopcancel", `QA ${RUN} · arrêt sans travail`, [line("Détartrage", 130)])

  // B7 — a completed plan.
  const { plan: done } = await mk("completed", `QA ${RUN} · terminé`, [line("Détartrage", 110)])
  for (const it of done.items) {
    const d = await A.call("POST", `/treatment-plans/${done.id}/items/${it.id}/done`, {
      doneOn: new Date().toISOString(),
    })
    if (!d.ok) console.log(`  ⚠️  could not mark act done: ${d.status} ${d.text.slice(0, 160)}`)
  }
  const comp = await A.call("POST", `/treatment-plans/${done.id}/complete`, {})
  if (comp.ok) state.plans.completed.status = "Completed"
  else console.log(`  ⚠️  could not complete: ${comp.status} ${comp.text.slice(0, 200)}`)

  // C6 — a many-act plan.
  const TEETH = [11, 12, 13, 14, 15, 16, 17, 21, 22]
  const many = Array.from({ length: 9 }, (_, i) => line(`Acte ${i + 1}`, 50 + i * 10, [TEETH[i]]))
  await mk("many", `QA ${RUN} · neuf actes`, many)

  // C1/C2 — the devis form's 409 and the save-reopen-save trap.
  await mk("conflict", `QA ${RUN} · conflit`, [line("Détartrage", 120), line("Composite", 160, [24])])

  // A1 / D8 — a followed Draft (« Suivre ce traitement »), the only way a Draft exists: creation accepts and
  // numbers immediately. Two of them, because the list's edit route forks on `canUseDraftEditor`:
  //   · stepless  → « Modifier le brouillon » is CORRECT (the draft editor may take it)
  //   · stepped   → « Modifier les actes et les prix », the amend modal (C8) — `SetItems` refuses the other
  const stepless = types.find((t) => (t.defaultSteps?.length ?? 0) <= 1) ?? types[0]
  const withSteps = types.find((t) => (t.defaultSteps?.length ?? 0) > 1)
  for (const [key, type, tooth] of [
    ["draft", stepless, 36],
    ["draftsteps", withSteps ?? stepless, 26],
  ]) {
    const p = await A.patient(key)
    const r = await A.call("POST", "/treatment-plans/start", {
      patientId: p.id,
      procedureTypeId: type.id,
      toothNumbers: [tooth],
    })
    if (!r.ok) {
      console.log(`  ⚠️  could not follow a treatment for ${key}: ${r.status} ${r.text.slice(0, 200)}`)
      continue
    }
    const v = r.json?.value ?? r.json
    state.patients[key] = { id: p.id, name: `${p.firstName} ${p.lastName}` }
    state.plans[key] = { id: v.id, number: v.number, patientId: p.id, status: v.status, steps: v.items[0]?.steps?.length ?? 0 }
  }

  ok("arrange", `${Object.keys(state.plans).length} plans minted under QA-PHASE6 ${RUN}`)
} catch (e) {
  bad("arrange", "threw", e.message)
}

writeFileSync(join(HERE, ".artifacts", "state.json"), JSON.stringify(state, null, 2))
console.log(`\n  state → qa/.artifacts/state.json`)
console.log(findings.length ? `\n  ${findings.length} finding(s):` : "\n  ALL CLEAR")
findings.forEach((f) => console.log("   ", JSON.stringify(f)))
