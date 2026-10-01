import crypto from 'node:crypto'

/** A TOTP code for a base32 secret. Single-use — wait for a fresh window before re-submitting. */
export function totp(secret, at = Date.now()) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.replace(/[\s=]/g, '').toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map(b => parseInt(b, 2)))
  const counter = Math.floor(at / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0)
  buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}

/** Seconds until the next 30-second window opens (+1s of slack). */
export const msToFreshWindow = () => (30 - (Math.floor(Date.now() / 1000) % 30) + 1) * 1000

if (process.argv[2]) console.log(totp(process.argv[2]))
