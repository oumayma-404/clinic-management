import crypto from "node:crypto"

/**
 * A TOTP code for the QA account's second factor.
 *
 * ⚠️ **Single-use.** The server refuses a code it has already accepted, even inside the same 30-second window,
 * so a retry must wait for the next one — see `global-setup`'s `nextWindow`. A code that is arithmetically
 * correct and refused reads exactly like a wrong password, which is the trap this note exists for.
 */
export function totp(secret: string, skew = 0): string {
  const A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const c of secret.replace(/[\s=]/g, "").toUpperCase()) {
    bits += A.indexOf(c).toString(2).padStart(5, "0")
  }
  const bytes = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const counter = Math.floor(Date.now() / 1000 / 30) + skew
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac("sha1", bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, "0")
}
