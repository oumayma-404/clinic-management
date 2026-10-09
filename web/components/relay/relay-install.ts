/**
 * The PC de secours offer's rules (`clinic-pc-copy` AC-1.1–1.13), kept out of the component so the start-up offer,
 * the « Paramètres » card and the « Installer le PC de secours ici… » page cannot disagree about them.
 */

type RelayShell = ClinicShell & Required<Pick<ClinicShell, "relayHostFacts" | "installRelay">>

/**
 * The Windows app's two relay members, or `null` — every browser and the Android app (AC-1.12). Feature-detected on
 * the METHODS, never on the bridge: the desktop shell carried a bridge for two releases before it could install.
 */
export function relayInstallShell(): RelayShell | null {
  if (typeof window === "undefined") return null
  const shell = window.__clinicShell
  return shell && typeof shell.relayHostFacts === "function" && typeof shell.installRelay === "function"
    ? (shell as RelayShell)
    : null
}

const GIB = 1024 ** 3

/**
 * AC-1.8's refusal, or `null` when the room is there or cannot be read. ⚠️ Worded and rounded exactly as the
 * installer's own `InstallRefusal` (`GiB(…, True)` up for the need, down for what is free), which says it again
 * should a PC fill up between this check and the install — the same sentence twice, never two.
 */
export function roomRefusal(freeBytes: number | null | undefined, needBytes: number | null | undefined): string | null {
  if (freeBytes == null || !needBytes || needBytes <= 0 || freeBytes >= needBytes) return null
  return `Il faut ${Math.ceil(needBytes / GIB)} Go libres sur ce PC (${Math.floor(freeBytes / GIB)} Go disponibles).`
}

// ── « Plus tard » / « Pas sur ce PC » (AC-1.3) — remembered on this PC, in this app's own browser storage ──────────

const OFFER_KEY = "apexa.relay-offer"
const ASKED_THIS_RUN_KEY = "apexa.relay-offer.asked"
const LATER_DAYS = 7

/** Whether this PC asked not to be offered again — for ever, or for the week after « Plus tard ». */
export function offerSetAside(now = Date.now()): boolean {
  try {
    const stored = JSON.parse(window.localStorage.getItem(OFFER_KEY) ?? "null") as { never?: boolean; laterUntil?: number } | null
    return stored?.never === true || (typeof stored?.laterUntil === "number" && stored.laterUntil > now)
  } catch {
    return false
  }
}

export function rememberLater(now = Date.now()) {
  try {
    window.localStorage.setItem(OFFER_KEY, JSON.stringify({ laterUntil: now + LATER_DAYS * 24 * 60 * 60 * 1000 }))
  } catch {
    // Storage refused: the offer comes back at the next start, which is the safe direction.
  }
}

export function rememberNever() {
  try {
    window.localStorage.setItem(OFFER_KEY, JSON.stringify({ never: true }))
  } catch {
    // As above.
  }
}

/**
 * AC-1.2: the offer belongs to the app's start, not to every page — `sessionStorage` lives exactly as long as this
 * window, survives a reload and is gone at the next launch. Returns whether this run had already been considered,
 * and marks it either way, so a page that cannot make the offer still spends the run's one chance.
 */
export function alreadyConsideredThisRun(): boolean {
  try {
    if (window.sessionStorage.getItem(ASKED_THIS_RUN_KEY)) return true
    window.sessionStorage.setItem(ASKED_THIS_RUN_KEY, "1")
    return false
  } catch {
    return true
  }
}
