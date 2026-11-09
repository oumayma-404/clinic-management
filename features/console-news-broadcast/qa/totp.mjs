import { createHmac } from "node:crypto";

const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

function base32(secret) {
  let bits = "";
  for (const ch of secret.replace(/=+$/, "").toUpperCase()) {
    bits += ALPHABET.indexOf(ch).toString(2).padStart(5, "0");
  }
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

export function totp(secret, at = Date.now()) {
  const counter = Math.floor(at / 1000 / 30);
  const buf = Buffer.alloc(8);
  buf.writeBigUInt64BE(BigInt(counter));
  const h = createHmac("sha1", base32(secret)).update(buf).digest();
  const o = h[h.length - 1] & 0xf;
  const code = ((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
  return code;
}

/** Waits for the next 30 s window so a code is never reused (the server's replay guard). */
export async function freshCode(secret) {
  const ms = 30_000 - (Date.now() % 30_000) + 1_000;
  await new Promise((r) => setTimeout(r, ms));
  return totp(secret);
}
