import type { GettingStartedKey } from "@/lib/getting-started"

/**
 * Which « Premiers pas » steps this person has already been congratulated for (owner's idea, 2026-11-04) — so a
 * step is celebrated ONCE, whether it ticked at the end of a « Me guider » or from ordinary work seen on the next
 * dashboard read.
 *
 * <p>Per browser and per account (`localStorage`, keyed by e-mail): a celebration is a courtesy, not a record, and a
 * second device simply starts from what it finds. Every access is wrapped — private windows and blocked storage
 * throw — and a failure means « celebrate nothing », never an error.</p>
 *
 * <p>⚠️ The FIRST read for an account seeds the set silently: the steps already done on arrival (the security code,
 * done during sign-up) are not news, and confetti on the first dashboard view would celebrate nothing the person
 * just did.</p>
 */
const storageKey = (owner: string) => `premiers-pas.fetes.${owner.toLowerCase()}`

function read(owner: string): Set<string> | null {
  try {
    const raw = window.localStorage.getItem(storageKey(owner))
    return raw === null ? null : new Set(JSON.parse(raw) as string[])
  } catch {
    return null
  }
}

function write(owner: string, keys: Set<string>) {
  try {
    window.localStorage.setItem(storageKey(owner), JSON.stringify([...keys]))
  } catch {
    // Storage refused: the next read will seed again, silently — the worst case is one celebration missed.
  }
}

/**
 * The steps done now that were not done at the last read — and records them. The first read for an account returns
 * nothing and only remembers.
 */
export function takeNewlyDone(owner: string, done: GettingStartedKey[]): GettingStartedKey[] {
  const seen = read(owner)
  if (seen === null) {
    write(owner, new Set(done))
    return []
  }
  const fresh = done.filter((key) => !seen.has(key))
  if (fresh.length > 0) write(owner, new Set([...seen, ...fresh]))
  return fresh
}

/** Records one step as celebrated; true when it had not been yet (so the caller celebrates it now). */
export function markCelebrated(owner: string, key: GettingStartedKey): boolean {
  return mark(owner, key)
}

/**
 * « Votre cabinet est prêt » — its OWN once-only mark. It used to reuse the last step's, so a cabinet whose last
 * step had been celebrated before (ticked, unticked, then toured) reached 100 % with no celebration at all (QA F-1).
 */
export function markReadyCelebrated(owner: string): boolean {
  return mark(owner, READY_KEY)
}

const READY_KEY = "ready"

function mark(owner: string, key: string): boolean {
  const seen = read(owner) ?? new Set<string>()
  if (seen.has(key)) return false
  seen.add(key)
  write(owner, seen)
  return true
}

/** Whether this browser has read the card for this account before — the very first read starts the tour. */
export function hasSeenCard(owner: string): boolean {
  return read(owner) !== null
}

/**
 * « Aide » → « Premiers pas » (owner's call, 2026-11-04): the steps someone chose to hide come back on the dashboard
 * and the tour resumes. On the dashboard already, the event does it; from another screen, the request survives the
 * client navigation (module state — a reload forgets it, on purpose).
 */
export const SHOW_PREMIERS_PAS_EVENT = "premiers-pas:show"
let showRequested = false

export function requestShowPremiersPas() {
  showRequested = true
}

export function takeShowPremiersPasRequest(): boolean {
  const asked = showRequested
  showRequested = false
  return asked
}
