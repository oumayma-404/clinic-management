// FAKE old-software exports for the PR #123 QA pass — invented people, the old software's PDF form (fixed grid,
// « Dossier créé le … sous le numéro N/AAAA », three « Historique » tables), plus one radio folder per patient.
// Usage: node pr123-gen-data.mjs <outDir>
import { createRequire } from 'node:module'
import { mkdir, rm, writeFile } from 'node:fs/promises'
import { randomBytes } from 'node:crypto'
import { join } from 'node:path'

const { PDFDocument, StandardFonts } = createRequire('C:/dev/clinic-migration/')('pdf-lib')
const sharp = createRequire(new URL('../../../web/', import.meta.url))('sharp')

const out = process.argv[2]
if (!out) throw new Error('usage: node pr123-gen-data.mjs <outDir>')

const FIRST = ['Amira', 'Sami', 'Leila', 'Karim', 'Nour', 'Hedi', 'Fatma', 'Ines', 'Walid', 'Rania', 'Mehdi', 'Salma', 'Youssef',
  'Sarra', 'Anis', 'Mariem', 'Bilel', 'Olfa', 'Firas', 'Hiba', 'Omar', 'Asma', 'Ghassen', 'Emna', 'Riadh', 'Dorra', 'Nizar',
  'Yasmine', 'Fares', 'Khadija', 'Slim', 'Wafa', 'Aziz', 'Lina', 'Moez', 'Rim', 'Tarek', 'Houda', 'Zied', 'Sonia', 'Hamza',
  'Nadia', 'Jalel', 'Amel', 'Chokri', 'Hela', 'Lotfi', 'Imen', 'Marwen', 'Ons', 'Sofiene', 'Afef', 'Habib', 'Kaouther', 'Nabil',
  'Sihem', 'Raouf', 'Faten', 'Adel', 'Monia', 'Kamel']
const LAST = ['Trabelsi', 'Gharbi', 'Jaziri', 'Mabrouk', 'Hammami', 'Dridi', 'Ben Ali', 'Ben Ali', 'Chaabane', 'Ayari', 'Mejri',
  'Bouazizi', 'Khelifi', 'Saidi', 'Riahi', 'Ferchichi', 'Haddad', 'Zouari', 'Masmoudi', 'Karray', 'Ellouze', 'Fourati', 'Sfaxi',
  'Kammoun', 'Bouzid', 'Gabsi', 'Mansouri', 'Tlili', 'Hamdi', 'Nasri', 'Oueslati', 'Jebali', 'Baccar', 'Chebbi', 'Ghannouchi',
  'Ksouri', 'Laabidi', 'Mzoughi', 'Sassi', 'Toumi', 'Zribi', 'Abidi', 'Barhoumi', 'Chouchane', 'Dhaouadi', 'Ezzine', 'Fakhfakh',
  'Guesmi', 'Hosni', 'Issaoui', 'Jlassi', 'Kefi', 'Lahmar', 'Maalej', 'Najjar', 'Ouni', 'Rekik', 'Selmi', 'Turki', 'Yahyaoui',
  'Zaier']
const MONTHS = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre']
const DAYS = ['dimanche', 'lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi']
const dd = n => String(n).padStart(2, '0')

function person(n) {
  const i = n - 1
  const birth = new Date(Date.UTC(1950 + ((n * 7) % 55), (n * 5) % 12, 1 + ((n * 3) % 27)))
  const opened = new Date(Date.UTC(2014 + (n % 9), (n * 2) % 12, 2 + ((n * 5) % 26)))
  // Patients 7 and 8 are a mother and her daughter sharing ONE phone (C3).
  const phone = n === 8 ? '98 765 432' : n === 7 ? '98 765 432' : `${[9, 5, 2][n % 3]}${(n * 13) % 10} ${String(100 + n * 7).slice(-3)} ${String(200 + n * 11).slice(-3)}`
  // Past the 61 names, a new pairing (first name i, last name i + 13) so batches E and F meet nobody from A–C.
  const first = i < FIRST.length ? FIRST[i] : FIRST[i % FIRST.length]
  const last = i < FIRST.length ? LAST[i] : LAST[(i + 13) % LAST.length]
  return { n, first, last, birth, opened, phone, female: n % 2 === 1 }
}

async function recordPdf(p) {
  const pdf = await PDFDocument.create()
  const page = pdf.addPage([595, 842])
  const font = await pdf.embedFont(StandardFonts.Helvetica)
  const t = (s, x, y) => page.drawText(s, { x, y, size: 9, font })
  const o = p.opened
  t(`Dossier créé le ${DAYS[o.getUTCDay()]} ${o.getUTCDate()} ${MONTHS[o.getUTCMonth()]} ${o.getUTCFullYear()} sous le numéro ${p.n}/${o.getUTCFullYear()}`, 150, 800)
  let y = 770
  const row = (l1, v1, l2, v2) => {
    t(`${l1}:`, 38, y); if (v1) t(v1, 140, y)
    if (l2) { t(`${l2}:`, 300, y); if (v2) t(v2, 400, y) }
    y -= 20
  }
  const b = p.birth
  row('Prénom', p.first, 'Nom', p.last.toUpperCase())
  row('Date de naissance', `${dd(b.getUTCDate())}/${dd(b.getUTCMonth() + 1)}/${b.getUTCFullYear()}`, 'Sexe', p.female ? 'F' : 'M')
  row('Situation familiale', p.n % 3 ? 'Marié(e)' : 'Célibataire', 'Profession', p.n % 2 ? 'Enseignant(e)' : 'Commerçant(e)')
  row('Tél 1 Patient', p.phone, 'Tél 2 Patient', '')
  row('Gouvernorat', 'Tunis', 'Ville', 'La Marsa')
  row('Adresse', `${p.n} rue de Carthage`, 'Assurances', 'CNAM')
  y -= 10
  const section = (title, lines) => { t(`${title}:`, 36, y); y -= 18; for (const l of lines) { t(l, 40, y); y -= 18 } y -= 6 }
  section('Histoire de la maladie', [p.n % 4 ? 'RAS' : 'Diabète type 2, sous metformine'])
  section('Diagnostic', [`Carie ${p.n % 2 ? '36' : '46'}, tartre`])
  const year = 2023 + (p.n % 2)
  const table = (title, headers, entries) => {
    t(`${title}:`, 36, y); y -= 18
    for (const [x, h] of headers) t(h, x, y)
    y -= 22
    for (const e of entries) { for (const [x, s] of e) t(s, x, y); y -= 28 }
    y -= 6
  }
  table('Historique consultations', [[40, 'Actes'], [160, 'Compte Rendu'], [480, 'Date']], [
    [[40, 'Détartrage'], [160, 'Détartrage complet, conseils d\'hygiène'], [480, `14/01/${year}`]],
    [[40, 'Obturation'], [160, `Composite ${p.n % 2 ? '36' : '46'} occlusal`], [480, `02/02/${year}`]],
  ])
  table('Historique préscriptions', [[40, 'Médicament'], [160, 'Posologie'], [340, 'Note'], [480, 'Date']], [
    [[40, 'AMOXICILLINE 1g'], [160, '1 cp matin et soir'], [340, '6 jours'], [480, `02/02/${year}`]],
  ])
  table('Historique radios', [[40, 'Radio'], [160, 'Note'], [480, 'Date']], [
    [[40, 'Panoramique'], [160, 'Contrôle général'], [480, `14/01/${year}`]],
  ])
  return pdf.save()
}

async function image(w, h, noisy, seed) {
  if (noisy) return sharp(randomBytes(w * h * 3), { raw: { width: w, height: h, channels: 3 } }).png({ compressionLevel: 0 }).toBuffer()
  return sharp({ create: { width: w, height: h, channels: 3, background: { r: (seed * 37) % 255, g: (seed * 91) % 255, b: 160 } } }).jpeg().toBuffer()
}

// E (run 3): the unattended import under injected connection faults. F: a file that vanishes after it was picked.
const BATCHES = { A: [1, 40, false], B: [41, 55, false], C: [56, 61, true], E: [62, 101, false], F: [102, 104, false] }
await rm(out, { recursive: true, force: true })
for (const [name, [from, to, big]] of Object.entries(BATCHES)) {
  const records = join(out, name, `dossiers-${name}`)
  const radios = join(out, name, `radios-${name}`)
  await mkdir(records, { recursive: true })
  await mkdir(radios, { recursive: true })
  for (let n = from; n <= to; n++) {
    const p = person(n)
    await writeFile(join(records, `${n}-${p.first} ${p.last}.pdf`), await recordPdf(p))
    const dir = join(radios, `${p.last.toUpperCase()} ${p.first}`)
    await mkdir(dir)
    // ~400 Ko each for batch C (incompressible PNG), small JPEGs otherwise.
    await writeFile(join(dir, big ? 'panoramique.png' : 'panoramique.jpg'), await image(big ? 370 : 320, big ? 370 : 200, big, n))
    await writeFile(join(dir, big ? 'retroalveolaire.png' : 'retroalveolaire.jpg'), await image(big ? 370 : 200, big ? 370 : 160, big, n + 100))
  }
  console.log(`batch ${name}: ${to - from + 1} dossiers`)
}
