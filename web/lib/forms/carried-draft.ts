/**
 * `clinic-pc-copy` D23 — a form open when the app switches server (cloud ⇄ PC de secours) reopens there with
 * everything typed (AC-3.2).
 *
 * The page cannot carry anything across the switch itself: the new server is another origin, so its storage is
 * another storage. The shell can — it holds one draft in memory for the window (`carryDraft`), and hands it to the
 * next clinic page that asks (`takeCarriedDraft`, which also forgets it). This module is the page's half:
 *
 * - an open form pushes its state as it changes (`carry`), and withdraws it when it closes or saves (`dropCarried`);
 * - each page load takes the draft once (`takeCarriedOnce`); a draft for another screen sends the browser there
 *   (`CarriedDraftResume`), and the form with the same `form` + `key` claims it (`claimCarried`).
 *
 * ⚠️ **Inert in a browser and in an older shell** — nothing is stored anywhere without `carryDraft`.
 * ⚠️ **Never in browser storage**: a fiche holds a patient's clinical record, and the shell's memory dies with the
 * window, which is the lifetime a carried form needs.
 */

export interface CarriedDraft<S = unknown> {
  v: 1
  /** Which form this is (`"fiche"`, …) — the claim matches on it with `key`. */
  form: string
  /** Which record the form is about (an id, or `new:…` for a form that creates one). */
  key: string
  /** Where the form is opened from (a deep link on the same app), so the next server can reopen it. */
  path: string
  /** Only the same person gets it back (the session's e-mail, lower-cased — the same on both servers). */
  userId: string
  /** The server it was typed on — a different one on claim is a switch, and the first save is refused once. */
  origin: string
  at: number
  state: S
}

/** A draft older than this is not handed back: it is not the form somebody was in the middle of. */
const MAX_AGE_MS = 2 * 60 * 60 * 1000
/** The shell keeps the draft in memory; a form this large is not one somebody typed. */
const MAX_CHARS = 2_000_000

export const SWITCH_REFUSAL =
  "Le serveur a changé pendant votre saisie — vérifiez puis enregistrez à nouveau."

let pending: CarriedDraft | null = null
let taken: Promise<CarriedDraft | null> | null = null
let lastCarried: string | null = null

function shell() {
  return typeof window === "undefined" ? undefined : window.__clinicShell
}

export function canCarry(): boolean {
  return typeof shell()?.carryDraft === "function"
}

/** Hands the form's current state to the shell, replacing whatever it held. Never throws. */
export function carry(draft: Omit<CarriedDraft, "v" | "origin" | "at">): void {
  const s = shell()
  if (typeof s?.carryDraft !== "function") return
  try {
    const json = JSON.stringify({ v: 1, ...draft, origin: window.location.origin, at: Date.now() })
    if (json.length > MAX_CHARS) return
    lastCarried = `${draft.form}|${draft.key}`
    s.carryDraft(json)
  } catch {
    // A shell that cannot keep it: the form still works, it simply would not survive a switch.
  }
}

/** The form closed or saved: the shell forgets it — only if it is still the one this tab handed over. */
export function dropCarried(form: string, key: string): void {
  if (lastCarried !== `${form}|${key}`) return
  lastCarried = null
  try {
    shell()?.carryDraft?.(null)
  } catch {
    // Nothing to forget.
  }
}

function parse(json: string | null, userId: string): CarriedDraft | null {
  if (!json) return null
  try {
    const draft = JSON.parse(json) as CarriedDraft
    if (draft?.v !== 1 || typeof draft.form !== "string" || typeof draft.key !== "string") return null
    if (typeof draft.path !== "string" || !draft.path.startsWith("/") || draft.path.startsWith("//")) return null
    if (draft.userId !== userId || typeof draft.at !== "number" || Date.now() - draft.at > MAX_AGE_MS) return null
    return draft
  } catch {
    return null
  }
}

/** Takes the shell's draft once per page load (for this person); every caller shares the one answer. */
export function takeCarriedOnce(userId: string): Promise<CarriedDraft | null> {
  if (taken) return taken
  const s = shell()
  if (typeof s?.takeCarriedDraft !== "function") {
    taken = Promise.resolve(null)
    return taken
  }
  taken = s
    .takeCarriedDraft()
    .then((json) => {
      pending = parse(json, userId)
      return pending
    })
    .catch(() => null)
  return taken
}

/** The draft taken on this load, if it is for this screen's form — and it is no longer pending once claimed. */
export function claimCarried<S>(form: string, key: string): CarriedDraft<S> | null {
  if (!pending || pending.form !== form || pending.key !== key) return null
  const draft = pending as CarriedDraft<S>
  pending = null
  return draft
}

/** The draft taken on this load and not claimed yet. */
export function pendingCarried(): CarriedDraft | null {
  return pending
}

/** Puts a taken draft back in the shell (before the page goes to the screen that will claim it). */
export function handBack(draft: CarriedDraft): void {
  const s = shell()
  if (typeof s?.carryDraft !== "function") return
  try {
    s.carryDraft(JSON.stringify(draft))
    lastCarried = `${draft.form}|${draft.key}`
  } catch {
    // Lost: the form opens empty, as it would have without a shell.
  }
}

/** The path the browser is on, as a carried `path` is written. */
export function currentPath(): string {
  return typeof window === "undefined" ? "" : window.location.pathname + window.location.search
}
