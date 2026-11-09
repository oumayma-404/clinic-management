import crypto from 'node:crypto'

export function totp(secret, at = Date.now()) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secret.replace(/[\s=]/g, '').toUpperCase()) bits += A.indexOf(c).toString(2).padStart(5, '0')
  const bytes = Buffer.from((bits.match(/.{8}/g) || []).map(b => parseInt(b, 2)))
  const counter = Math.floor(at / 1000 / 30)
  const buf = Buffer.alloc(8)
  buf.writeUInt32BE(Math.floor(counter / 2 ** 32), 0); buf.writeUInt32BE(counter >>> 0, 4)
  const h = crypto.createHmac('sha1', bytes).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  return ((h.readUInt32BE(o) & 0x7fffffff) % 1000000).toString().padStart(6, '0')
}

/** Wait for a fresh 30 s window so the code does not expire in flight. */
export async function freshTotp(secret) {
  const s = Math.floor(Date.now() / 1000)
  const left = 30 - (s % 30)
  if (left < 8) await new Promise(r => setTimeout(r, (left + 1) * 1000))
  return totp(secret)
}

if (process.argv[2]) console.log(totp(process.argv[2]))
