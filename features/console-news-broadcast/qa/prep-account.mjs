import { writeFileSync } from "node:fs";
import { totp, freshCode } from "./totp.mjs";
const API = "http://localhost:5494/api/platform/auth";
const [email, temp, secret, newPassword] = ["qa@news-qa.test", process.argv[2], process.argv[3], "QaConsole-news-2026!"];
const post = async (path, body, token) => {
  const r = await fetch(API + path, { method: "POST", headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body) });
  const text = await r.text(); console.log(path, r.status); return text ? JSON.parse(text) : {};
};
await post("/totp/enrol", { email, password: temp, totpCode: totp(secret) });
const { token } = await post("/login", { email, password: temp, totpCode: await freshCode(secret) });
await post("/password", { currentPassword: temp, newPassword }, token);
writeFileSync(new URL("./account.json", import.meta.url), JSON.stringify({ email, password: newPassword, secret }));
