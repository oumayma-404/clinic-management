#!/usr/bin/env node
/**
 * `npm run dev`, with one guarantee Next does not give you: **only one dev server per `.next` directory**.
 *
 * ⚠️ Why this exists. Next binds the next free port when 3000 is taken — so a second `next dev` in the same
 * checkout starts happily on 3001 and both processes then read and write the *same* `web/.next`. They corrupt
 * each other's build manifests within seconds, and the symptom is not a warning but **HTTP 500 on every route,
 * including `/favicon.ico`**, with `ENOENT … _buildManifest.js.tmp.*` in the log and a bare English
 * "Internal Server Error" in the browser. Nothing in the app is wrong; nothing in the app can say so. It was hit
 * twice in one afternoon, and the recovery (kill every web dev process, delete `.next`, start one) is not
 * something the error message leads you to.
 *
 * The lock is on the **build directory**, not on a port, because sharing `.next` is the actual hazard —
 * `npm run dev -- -p 3005` corrupts it just as thoroughly as a second server on 3001.
 *
 * A stale lock (the machine was killed, the PID is gone) is reclaimed silently: this must never be the reason a
 * developer cannot start their own app. `CLINIC_DEV_ALLOW_MULTIPLE=1` skips the check entirely for the rare case
 * of deliberately running two servers against separate build dirs.
 */
import { spawn } from 'node:child_process'
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const webRoot = dirname(dirname(fileURLToPath(import.meta.url)))
const buildDir = join(webRoot, '.next')
const lockFile = join(buildDir, 'dev-server.lock')

/** Is a process with this id still running? `signal 0` tests liveness without touching the process. */
function isAlive(pid) {
  try {
    process.kill(pid, 0)
    return true
  } catch (err) {
    // EPERM means it exists but belongs to another user — still alive, still a conflict.
    return err.code === 'EPERM'
  }
}

function readLock() {
  try {
    const lock = JSON.parse(readFileSync(lockFile, 'utf8'))
    return typeof lock?.pid === 'number' ? lock : null
  } catch {
    return null // absent, unreadable or truncated — all mean "no live holder".
  }
}

/**
 * A lock whose owner is gone means the last server did not shut down cleanly — which is exactly the state that
 * leaves a half-written `.next` behind.
 *
 * ⚠️ **A second cause of the same corruption has nothing to do with duplicate servers.** Turbopack writes
 * `_buildManifest.js.tmp.*` and renames it constantly, and on Windows a real-time antivirus scan can hold or
 * remove that temp file between the write and the rename — the log fills with `ENOENT … .tmp.<random>` and every
 * route 500s, with a single server running. Recovering is always the same three steps, so the unclean-exit case
 * does them for you rather than leaving the next `npm run dev` to serve a broken build. If it recurs *while* the
 * server is up, exclude `web/.next` from Defender (an admin PowerShell:
 * `Add-MpPreference -ExclusionPath '<repo>\web\.next'`) — that is the actual fix, and it needs rights this
 * script does not have.
 */
if (process.env.CLINIC_DEV_ALLOW_MULTIPLE !== '1') {
  const held = readLock()
  if (held && held.pid !== process.pid && !isAlive(held.pid)) {
    process.stdout.write(
      '\x1b[33m⚠ Le serveur précédent ne s\'est pas arrêté proprement — .next est reconstruit.\x1b[0m\n',
    )
    rmSync(buildDir, { recursive: true, force: true })
  }
  if (held && held.pid !== process.pid && isAlive(held.pid)) {
    const since = held.startedAt ? ` (démarré ${held.startedAt})` : ''
    process.stderr.write(
      '\n\x1b[31m✖ Un serveur de développement tourne déjà dans ce dossier.\x1b[0m\n\n' +
        `  PID ${held.pid}${since}${held.port ? ` · port ${held.port}` : ''}\n\n` +
        '  Deux serveurs Next partageant le même dossier .next corrompent sa build en quelques secondes :\n' +
        '  toutes les pages répondent alors « Internal Server Error » alors que le code est correct.\n\n' +
        '  Pour continuer :\n' +
        `    • réutilisez le serveur déjà lancé, ou\n` +
        `    • arrêtez-le  (Windows : taskkill /PID ${held.pid} /T /F)  puis relancez « npm run dev ».\n\n` +
        '  Pour lancer volontairement un second serveur : CLINIC_DEV_ALLOW_MULTIPLE=1 npm run dev\n\n',
    )
    process.exit(1)
  }
}

mkdirSync(buildDir, { recursive: true })

const args = process.argv.slice(2)
const portFlag = args.indexOf('-p') !== -1 ? args[args.indexOf('-p') + 1] : args.includes('--port') ? args[args.indexOf('--port') + 1] : '3000'

writeFileSync(
  lockFile,
  JSON.stringify({ pid: process.pid, port: portFlag, startedAt: new Date().toISOString() }, null, 2),
)

let released = false
const release = () => {
  if (released) return
  released = true
  // Only ever remove OUR lock: a crash-and-restart race must not delete the new holder's.
  const held = readLock()
  if (held?.pid === process.pid) rmSync(lockFile, { force: true })
}

const child = spawn('next', ['dev', '--turbopack', ...args], {
  cwd: webRoot,
  stdio: 'inherit',
  shell: true, // `next` is a .cmd shim on Windows.
})

child.on('exit', (code, signal) => {
  release()
  process.exit(signal ? 1 : (code ?? 0))
})

for (const sig of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
  process.on(sig, () => {
    release()
    child.kill(sig)
  })
}
process.on('exit', release)
