// Part 2 QA: the cabinet's admin (authenticator + own password) and a secretary, over the isolated API on :5094.
// Usage: node prep-app.mjs <admin temporary password>
import { writeFileSync } from "node:fs";
import { totp, freshCode } from "./totp.mjs";

const API = "http://localhost:5094/api";
const admin = { email: "a@news-qa.test", password: "QaAdmin-news-2026!" };
const secretary = { email: "s@news-qa.test", password: "QaSecr-news-2026!" };
const temp = process.argv[2];

async function call(path, body, token, method = "POST") {
  const r = await fetch(API + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  console.log(method, path, r.status);
  if (!r.ok) throw new Error(`${path} ${r.status} ${text}`);
  return text ? JSON.parse(text) : {};
}

const step1 = await call("/auth/totp/enrol", { email: admin.email, password: temp });
const secret = (step1.secret ?? step1.value?.secret).replace(/\s+/g, "");
await call("/auth/totp/enrol", { email: admin.email, password: temp, totpCode: totp(secret) });
const first = await call("/auth/login", { email: admin.email, password: temp, totpCode: await freshCode(secret) });
await call("/auth/change-password", { currentPassword: temp, newPassword: admin.password }, first.accessToken ?? first.value?.accessToken);
const session = await call("/auth/login", { email: admin.email, password: admin.password, totpCode: await freshCode(secret) });
const token = session.accessToken ?? session.value?.accessToken;

const created = await call("/users", { email: secretary.email, fullName: "Sonia Secretaire", role: "secretary" }, token);
const link = created.invitationUrl ?? created.value?.invitationUrl;
if (link) {
  await call("/auth/invitation/accept", { token: link.split("#t=")[1], password: secretary.password });
} else {
  secretary.password = created.temporaryPassword ?? created.value?.temporaryPassword;
  secretary.mustChange = true;
}

writeFileSync(new URL("./app-accounts.json", import.meta.url), JSON.stringify({ admin: { ...admin, secret }, secretary }));
console.log("ready");
