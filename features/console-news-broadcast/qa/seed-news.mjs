// Part 2 QA: sends 12 news through the console API (:5494) as the QA console account.
import { readFileSync } from "node:fs";
import { freshCode } from "./totp.mjs";

const API = "http://localhost:5494/api/platform";
const account = JSON.parse(readFileSync(new URL("./account.json", import.meta.url)));

async function call(path, body, token) {
  const r = await fetch(API + path, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  const text = await r.text();
  if (!r.ok) throw new Error(`${path} ${r.status} ${text}`);
  return text ? JSON.parse(text) : {};
}

const { token } = await call("/auth/login", { email: account.email, password: account.password, totpCode: await freshCode(account.secret) });
const subjects = [
  "Relevé de caisse du jour", "Reste à payer, patient par patient", "Fournisseurs avec leur numéro",
  "Mode discret et heures sur 24 h", "Plusieurs numéros par patient", "L'ordonnance dans la fiche de soins",
  "Radios DICOM : contraste et mesure", "Scans 3D dans le dossier", "Le PC de secours",
  "Rappels par SMS, bientôt", "Messagerie interne du cabinet", "Premiers pas guidés",
];
for (const [i, subject] of subjects.entries()) {
  const queued = await call("/news", {
    requestId: crypto.randomUUID(),
    subject,
    items: [
      { emoji: "🚀", text: `Changement ${i + 1}, première ligne.` },
      { emoji: "📅", text: "Deuxième ligne, un peu plus longue pour voir comment le texte revient à la ligne sur un téléphone." },
    ],
  }, token);
  console.log(subject, "→", queued.broadcastId);
}
