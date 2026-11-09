import { readFileSync } from "node:fs";
import { freshCode } from "./totp.mjs";

const acct = JSON.parse(readFileSync(new URL("./account.json", import.meta.url)));
const API = "http://localhost:5494/api";
const findings = [];
const ok = (id, m) => console.log(`  ✅ [${id}] ${m}`);
const bad = (id, m, d = "") => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }); };

const login = await fetch(`${API}/platform/auth/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email: acct.email, password: acct.password, totpCode: await freshCode(acct.secret) }),
});
const { token } = await login.json();
const call = async (method, path, body) => {
  const r = await fetch(API + path, {
    method,
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { status: r.status, json };
};

// C5 — audience (dedupe, case-insensitive)
try {
  const a = await call("GET", "/platform/news/audience");
  const j = a.json;
  j && j.clinics === 2 && j.recipients === 3 && j.clinicsWithoutAdminEmail === 1
    ? ok("C5", `audience ${JSON.stringify(j)}`)
    : bad("C5", "audience", `${a.status} ${JSON.stringify(j)}`);
} catch (e) { bad("C5", "threw", e.message); }

// D3 — portfolio « Actif » filter still matches
try {
  const p = await call("GET", "/platform/clinics?state=active&pageSize=50");
  const names = (p.json?.items ?? []).map((i) => i.name).sort();
  JSON.stringify(names) === JSON.stringify(["Cabinet Essai", "Cabinet Payant", "Cabinet Sans Admin"])
    ? ok("D3", `active = ${names.join(", ")}`)
    : bad("D3", "active filter", `${p.status} ${JSON.stringify(names)}`);
} catch (e) { bad("D3", "threw", e.message); }

// B2 — refusals
const before = (await call("GET", "/platform/news")).json.length;
const row = (text, emoji = "✨") => ({ emoji, text });
const cases = [
  ["empty subject", { subject: "  ", items: [row("x")] }],
  ["no text row", { subject: "S", items: [row("   ")] }],
  ["subject 121", { subject: "s".repeat(121), items: [row("x")] }],
  ["row 301", { subject: "S", items: [row("x".repeat(301))] }],
  ["13 rows", { subject: "S", items: Array.from({ length: 13 }, (_, i) => row(`r${i}`)) }],
];
for (const [name, body] of cases) {
  try {
    const r = await call("POST", "/platform/news", { requestId: crypto.randomUUID(), ...body });
    r.status === 400 && r.json?.code === "news_invalid"
      ? ok("B2", `${name} → 400 news_invalid « ${r.json.error} »`)
      : bad("B2", name, `${r.status} ${JSON.stringify(r.json)}`);
  } catch (e) { bad("B2", `${name} threw`, e.message); }
}
try {
  const h = await call("GET", "/platform/news");
  Array.isArray(h.json) && h.json.length === before ? ok("B2", `no history row written (${before} before and after)`) : bad("B2", "history", JSON.stringify(h.json));
} catch (e) { bad("B2", "history threw", e.message); }

// B3 — escaping
try {
  const r = await call("POST", "/platform/news/preview", { subject: "Esc <i>t</i>", items: [row("<b>gras</b>")] });
  const html = r.json?.newsHtml ?? "";
  html.includes("&lt;b&gt;gras&lt;/b&gt;") && !html.includes("<b>gras") && !html.includes("<i>t</i>")
    ? ok("B3", "item and subject escaped")
    : bad("B3", "escaping", `${r.status} ${html.slice(0, 200)}`);
} catch (e) { bad("B3", "threw", e.message); }

console.log(findings.length ? `\n${findings.length} finding(s)` : "\nALL CLEAR");
