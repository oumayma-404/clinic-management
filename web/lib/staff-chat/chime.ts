/**
 * The chat's two tones, synthesised — no audio file to fetch or cache (staff-chat AC-4.1, AC-4.5). Only
 * `lib/staff-chat/alerts.ts` calls this module (`check:responsive` N52).
 *
 * A browser keeps audio suspended until the person has pressed something on the page; the first press anywhere
 * resumes it. Until then a chime is not heard, and `isAudioBlocked()` says so for « Son bloqué — activer ».
 */

type Note = { at: number; freq: number; length: number }

const NORMAL: Note[] = [
  { at: 0, freq: 880, length: 0.14 },
  { at: 0.16, freq: 1318.5, length: 0.22 },
]

// Three rising pairs, louder and harsher: it must be told apart from across a room.
const URGENT: Note[] = [0, 0.42, 0.84].flatMap((start) => [
  { at: start, freq: 987.8, length: 0.16 },
  { at: start + 0.18, freq: 1480, length: 0.2 },
])

let context: AudioContext | null = null
let blocked = false
let unlockInstalled = false
const listeners = new Set<() => void>()

function setBlocked(value: boolean) {
  if (blocked === value) return
  blocked = value
  for (const listener of [...listeners]) listener()
}

function audio(): AudioContext | null {
  if (typeof window === "undefined") return null
  if (!context) {
    const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctor) return null
    try {
      context = new Ctor()
    } catch {
      return null
    }
  }
  return context
}

/**
 * Resumes audio on every press while it is not running; safe to call more than once.
 *
 * ⚠️ `click` and `touchend` are the presses iOS counts as a gesture — `pointerdown` alone is not — and an iPhone
 * suspends the audio again each time the app goes to the background, so this is needed after every return, not once.
 */
export function installAudioUnlock() {
  if (unlockInstalled || typeof window === "undefined") return
  unlockInstalled = true
  const unlock = () => {
    if (context?.state === "running") return
    void resumeAudio()
  }
  for (const type of ["pointerdown", "touchend", "click", "keydown"]) {
    window.addEventListener(type, unlock, { capture: true, passive: true })
  }
}

/** Called from a press: resumes a suspended context. */
export async function resumeAudio(): Promise<boolean> {
  let ctx = audio()
  if (!ctx) return false
  if (ctx.state === "closed") {
    context = null
    ctx = audio()
    if (!ctx) return false
  }
  if (ctx.state !== "running") {
    try {
      await ctx.resume()
      // iOS lets the context run only once something has played inside the gesture: one silent sample.
      const source = ctx.createBufferSource()
      source.buffer = ctx.createBuffer(1, 1, ctx.sampleRate)
      source.connect(ctx.destination)
      source.start()
    } catch {
      // Still blocked: the flag below stays up.
    }
  }
  const running = ctx.state === "running"
  if (running) setBlocked(false)
  return running
}

export function isAudioBlocked() {
  return blocked
}

export function onAudioBlockedChange(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function playChime(urgent: boolean) {
  const ctx = audio()
  if (!ctx) return
  if (ctx.state !== "running") {
    setBlocked(true)
    void ctx.resume().then(() => {
      if (ctx.state === "running") setBlocked(false)
    }, () => {})
    return
  }

  const start = ctx.currentTime + 0.02
  const peak = urgent ? 0.28 : 0.16
  for (const note of urgent ? URGENT : NORMAL) {
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = urgent ? "square" : "sine"
    osc.frequency.value = note.freq
    const t = start + note.at
    gain.gain.setValueAtTime(0.0001, t)
    gain.gain.exponentialRampToValueAtTime(urgent ? peak / 2 : peak, t + 0.015)
    gain.gain.exponentialRampToValueAtTime(0.0001, t + note.length)
    osc.connect(gain).connect(ctx.destination)
    osc.start(t)
    osc.stop(t + note.length + 0.02)
  }
}
