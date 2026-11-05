// QA walk — demo-cabinet story 5 (SPÉCIMEN PDFs, per-visit limit). Run: node walk-5.mjs <playwright-core> <shots>
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require(process.argv[2])
const SHOTS = process.argv[3]
mkdirSync(SHOTS, { recursive: true })
const BASE = 'http://localhost:3099'
const API = 'http://localhost:5099/api'
const MARK = 'SPÉCIMEN — DÉMONSTRATION'

const findings = []
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why) => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }

const browser = await chromium.launch({ channel: 'chrome' })

// One visitor: a fresh context, « Entrer dans la démo », and the bearer the app itself sends.
async function visit(label) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'fr-FR' })
  const page = await ctx.newPage()
  const v = { ctx, page, auth: null, writes: [] }
  page.on('request', (r) => {
    const u = new URL(r.url())
    const h = r.headers()['authorization']
    if (h && r.url().startsWith(API)) v.auth = h
    if (u.pathname.includes('/api/') && !['GET', 'OPTIONS'].includes(r.method())) v.writes.push(`${r.method()} ${u.pathname}`)
  })
  await page.goto(`${BASE}/demo`)
  await page.getByRole('button', { name: 'Entrer dans la démo' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/demo'), { timeout: 30000 })
  await page.getByRole('button', { name: 'Mon compte' }).waitFor({ timeout: 45000 })
  for (let i = 0; i < 40 && !v.auth; i++) await page.waitForTimeout(250)
  console.log(`  · ${label} entered, token ${v.auth ? 'captured' : 'MISSING'}`)
  return v
}

const json = async (v, path) => (await v.page.request.get(`${API}${path}`, { headers: { authorization: v.auth } })).json()
const items = (body) => (Array.isArray(body) ? body : body?.items ?? [])

// The /Keywords string of a PDF's Info dictionary, literal or hex, UTF-16BE or PDFDocEncoding.
function keywords(buf) {
  const text = buf.toString('latin1')
  const m = text.match(/\/Keywords\s*(<[0-9A-Fa-f\s]*>|\((?:\\.|[^\\)])*\))/)
  if (!m) return null
  let bytes
  if (m[1].startsWith('<')) bytes = Buffer.from(m[1].slice(1, -1).replace(/\s/g, ''), 'hex')
  else {
    const out = []
    const s = m[1].slice(1, -1)
    for (let i = 0; i < s.length; i++) {
      if (s[i] !== '\\') { out.push(s.charCodeAt(i)); continue }
      const n = s[++i]
      if (/[0-7]/.test(n)) { let o = n; while (o.length < 3 && /[0-7]/.test(s[i + 1])) o += s[++i]; out.push(parseInt(o, 8)) }
      else out.push({ n: 10, r: 13, t: 9, b: 8, f: 12 }[n] ?? n.charCodeAt(0))
    }
    bytes = Buffer.from(out)
  }
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return Buffer.from(bytes.subarray(2)).swap16().toString('utf16le')
  return bytes.toString('latin1')
}

const saved = {}
async function checkPdf(id, label, res) {
  try {
    const status = res.status()
    const body = await res.body()
    if (status !== 200 || body.subarray(0, 4).toString() !== '%PDF') {
      return bad(id, `${label}: not a PDF`, `${status} ${body.subarray(0, 160).toString()}`)
    }
    const kw = keywords(body)
    if (kw === MARK) { ok(id, `${label}: ${body.length} bytes, stamped`); saved[id] = body }
    else bad(id, `${label}: no SPÉCIMEN mark`, `keywords=${JSON.stringify(kw)}`)
  } catch (e) { bad(id, `${label} threw`, e.message.split('\n')[0]) }
}
const getPdf = (v, path) => v.page.request.get(`${API}${path}`, { headers: { authorization: v.auth } })

const a = await visit('visitor A')

async function patientId(v, name) {
  const hit = items(await json(v, `/patients?searchTerm=${encodeURIComponent(name)}&page=1&pageSize=5`))
    .find((p) => `${p.firstName} ${p.lastName}` === name)
  return hit?.id
}

// P1–P4: the documents seeded on four patients' files.
const docsOf = async (name) => items(await json(a, `/medical-documents?patientId=${await patientId(a, name)}`))
try {
  const ross = await docsOf('Ross Geller')
  const ordonnance = ross.find((d) => d.documentType === 'prescription')
  const examens = ross.find((d) => d.documentType === 'examens')
  ordonnance ? await checkPdf('P1', 'ordonnance', await getPdf(a, `/medical-documents/${ordonnance.id}/pdf`)) : bad('P1', 'no ordonnance on Ross', JSON.stringify(ross.map((d) => d.documentType)))
  examens ? await checkPdf('P2', "demande d'examens", await getPdf(a, `/medical-documents/${examens.id}/pdf`)) : bad('P2', 'no examens on Ross')
  const certificat = (await docsOf('Joey Tribbiani')).find((d) => d.documentType === 'certificat')
  certificat ? await checkPdf('P3', 'certificat', await getPdf(a, `/medical-documents/${certificat.id}/pdf`)) : bad('P3', 'no certificat on Joey')
  const arret = (await docsOf('Frank Buffay')).find((d) => d.documentType === 'arret-travail')
  arret ? await checkPdf('P4', 'arrêt de travail (P 061)', await getPdf(a, `/medical-documents/${arret.id}/pdf`)) : bad('P4', 'no arrêt on Frank')
} catch (e) { bad('P1-4', 'threw', e.message.split('\n')[0]) }

// P5, P6, P9: a note d'honoraires, its payment's reçu, and an avoir if any invoice carries one.
try {
  const invoices = items(await json(a, '/invoices?page=1&pageSize=50')).filter((i) => i.number)
  if (!invoices.length) bad('P5', 'no issued invoice')
  else {
    await checkPdf('P5', `note ${invoices[0].number}`, await getPdf(a, `/invoices/${invoices[0].id}/pdf`))
    let receipt = null
    let avoir = null
    for (const inv of invoices) {
      if (!receipt) receipt = (await json(a, `/invoices/${inv.id}`)).payments?.find((p) => !p.isVoided)
      if (!avoir) avoir = items(await json(a, `/invoices/${inv.id}/avoirs`))[0]
      if (receipt && avoir) break
    }
    receipt ? await checkPdf('P6', 'reçu', await getPdf(a, `/payments/${receipt.id}/receipt-pdf`)) : bad('P6', 'no payment on any note')
    avoir ? await checkPdf('P9', 'avoir', await getPdf(a, `/invoices/avoirs/${avoir.id}/pdf`)) : skip('P9', 'the demo holds no avoir')
  }
} catch (e) { bad('P5-9', 'threw', e.message.split('\n')[0]) }

// P7, P8: a devis, and an installment receipt if any plan collected one.
try {
  const plans = items(await json(a, '/treatment-plans?page=1&pageSize=50')).filter((p) => p.number)
  if (!plans.length) bad('P7', 'no numbered devis')
  else {
    await checkPdf('P7', `devis ${plans[0].number}`, await getPdf(a, `/treatment-plans/${plans[0].id}/devis-pdf`))
    let found = null
    for (const p of plans) {
      const plan = await json(a, `/treatment-plans/${p.id}`)
      for (const inst of plan.installments ?? []) {
        const pay = (inst.payments ?? []).find((x) => !x.isVoided && x.amount > 0)
        if (pay) { found = { plan: p.id, inst: inst.id, pay: pay.id }; break }
      }
      if (found) break
    }
    found ? await checkPdf('P8', "reçu d'échéance", await getPdf(a, `/treatment-plans/${found.plan}/installments/${found.inst}/payments/${found.pay}/receipt-pdf`))
      : skip('P8', 'no installment payment in the demo')
  }
} catch (e) { bad('P7-8', 'threw', e.message.split('\n')[0]) }

// P10, P11: the two POSTs the read-only gate lets through.
try {
  const joey = await patientId(a, 'Joey Tribbiani')
  await checkPdf('P10', 'Imprimer (generate-pdf-download)', await a.page.request.post(`${API}/medical-documents/generate-pdf-download`, {
    headers: { authorization: a.auth },
    data: { documentType: 'certificat', documentDate: new Date().toISOString(), patientName: 'Joey Tribbiani', patientId: joey, clinicName: 'Cabinet Dentaire Démo', doctorName: 'Dr Monica Geller', content: { body: 'Certificat de démonstration.' } },
  }))
  const ross = await patientId(a, 'Ross Geller')
  await checkPdf('P11', 'aperçu ordonnance', await a.page.request.post(`${API}/medical-documents/fiche-ordonnance-preview`, {
    headers: { authorization: a.auth },
    data: { patientId: ross, interventionDate: new Date().toISOString(), prescription: { lines: [{ kind: 'medicament', name: 'Paracétamol 1 g' }] } },
  }))
} catch (e) { bad('P10-11', 'threw', e.message.split('\n')[0]) }

// E1: render three families with pdf.js and look at them.
const eye = await browser.newContext({ viewport: { width: 900, height: 1250 } })
const eyePage = await eye.newPage()
for (const [id, name] of [['P1', 'ordonnance'], ['P4', 'arret-travail'], ['P5', 'note-honoraires']]) {
  if (!saved[id]) { skip('E1', `${name}: no PDF to render`); continue }
  try {
    writeFileSync(`${SHOTS}/S5-${name}.pdf`, saved[id])
    await eyePage.setContent('<canvas id="c"></canvas>')
    await eyePage.addScriptTag({ url: 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js' })
    await eyePage.evaluate(async (b64) => {
      pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js'
      const doc = await pdfjsLib.getDocument({ data: Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)) }).promise
      const page = await doc.getPage(1)
      const vp = page.getViewport({ scale: 1.4 })
      const c = document.getElementById('c'); c.width = vp.width; c.height = vp.height
      await page.render({ canvasContext: c.getContext('2d'), viewport: vp }).promise
    }, saved[id].toString('base64'))
    await eyePage.locator('#c').screenshot({ path: `${SHOTS}/S5-E1-${name}.png` })
    ok('E1', `${name} rendered → S5-E1-${name}.png (read it)`)
  } catch (e) { bad('E1', `${name} render threw`, e.message.split('\n')[0]) }
}
await eye.close()

// R1: the same visit keeps asking until the limit answers.
try {
  const invoice = items(await json(a, '/invoices?page=1&pageSize=5')).find((i) => i.number)
  let refused = null
  let n = 0
  for (; n < 40 && !refused; n++) {
    const res = await getPdf(a, `/invoices/${invoice.id}/pdf`)
    if (res.status() === 429) refused = { body: await res.json(), retry: res.headers()['retry-after'] }
  }
  if (refused?.body?.code === 'demo_pdf_limited' && /^La démo limite/.test(refused.body.error)) ok('R1', `429 after ${n} more PDFs: « ${refused.body.error} » (Retry-After ${refused.retry})`)
  else bad('R1', 'no demo refusal within 40 PDFs', JSON.stringify(refused))
  const reads = await a.page.request.get(`${API}/patients?page=1&pageSize=5`, { headers: { authorization: a.auth } })
  reads.status() === 200 ? ok('R3', 'the limited visit still reads patients (200)') : bad('R3', `patients read ${reads.status()}`)
} catch (e) { bad('R1', 'threw', e.message.split('\n')[0]) }

// R2: a second visitor is not charged for the first one's PDFs.
try {
  const b = await visit('visitor B')
  const invoice = items(await json(b, '/invoices?page=1&pageSize=5')).find((i) => i.number)
  const res = await getPdf(b, `/invoices/${invoice.id}/pdf`)
  res.status() === 200 ? ok('R2', 'visitor B gets the PDF (200)') : bad('R2', `visitor B got ${res.status()}`, (await res.body()).subarray(0, 160).toString())
  const unexpected = [...a.writes, ...b.writes].filter((w) => !/\/auth\/|generate-pdf-download|fiche-ordonnance-preview/.test(w))
  unexpected.length === 0 ? ok('D1', 'no writes beyond sign-in') : bad('D1', 'unexpected writes', JSON.stringify([...new Set(unexpected)]))
} catch (e) { bad('R2', 'threw', e.message.split('\n')[0]) }

await browser.close()
console.log('\n' + (findings.filter((f) => !f.why).length ? `RED (${findings.filter((f) => !f.why).length})` : 'GREEN'))
findings.forEach((f) => console.log(JSON.stringify(f)))
