"use client"

import { useEffect, useState, useSyncExternalStore } from "react"
import { usePathname } from "next/navigation"
import { CloudOff, RefreshCw, ServerCog, WifiOff } from "lucide-react"

import { ApiError, onRelayRefused } from "@/lib/api/client"
import { relayApi, type RelayBannerDto } from "@/lib/api/relay"
import { useSession } from "@/lib/auth/session"
import { isChromeLessPath } from "@/lib/nav"
import { cn } from "@/lib/utils"

/**
 * The cut's strip on every screen (`clinic-pc-copy` AC-3.4, AC-4.1, AC-5.2, EC-20, D20): on the PC de secours « Internet
 * coupé » or « Le cloud est injoignable » (the PC decided which), on the cloud « Le cabinet travaille sur le PC de
 * secours depuis 10:42 — ici, lecture seule », and « Retour au cloud en cours » on both. Above the subscription strip:
 * the cut's first (AC-4.3). The server writes every word; this only picks an icon and a tone from `kind`.
 *
 * ⚠️ **Read at module level, not per mount**: the app shell remounts on every navigation, so a fetch per mount would
 * poll the server on every click. One read every 20 s while something is mounted, at once on focus and at once when a
 * save is refused for a cut (`onRelayRefused`). A failed read keeps what was shown — a strip that flickers off when the
 * line wobbles would be a lie in the other direction.
 *
 * ⚠️ **Announced once per change, not per mount** (AC-3.9): a live region that remounts with its text re-announces on
 * every navigation, which is why `SubscriptionBanner` has none. Here the strip itself carries no role, and a separate
 * `sr-only` status speaks only when the cut's state differs from the last one announced in this tab.
 */
export function RelayBanner() {
  const { user } = useSession()
  const pathname = usePathname()
  const banner = useSyncExternalStore(subscribe, () => current, () => null)
  const [spoken, setSpoken] = useState("")

  useEffect(() => (user ? watch() : undefined), [user])

  useEffect(() => {
    const key = banner ? `${banner.kind}|${banner.title}` : ""
    if (key === announced) return
    const wasCut = announced !== "" && announced !== null
    announced = key
    setSpoken(banner ? `${banner.title} — ${banner.detail}` : wasCut ? "Le cabinet enregistre de nouveau sur le cloud." : "")
  }, [banner])

  if (!user || isChromeLessPath(pathname)) return null

  return (
    <>
      <p role="status" className="sr-only">
        {spoken}
      </p>
      {banner && (
        <div
          className={cn(
            // One line on a desk; the title alone on a phone, the detail on a second line from `sm:` (spec § Device).
            "flex shrink-0 items-center gap-x-3 border-b px-4 py-2 text-sm md:px-6 print:hidden",
            banner.warning ? "border-warning/30 bg-warning-wash text-foreground" : "border-border bg-muted text-foreground",
          )}
          data-relay-banner={banner.kind}
        >
          <BannerIcon kind={banner.kind} />
          <p className="min-w-0 flex-1 [overflow-wrap:anywhere]">
            <span className="font-medium">{banner.title}</span>
            <span className="hidden sm:inline"> — {banner.detail}</span>
          </p>
        </div>
      )}
    </>
  )
}

function BannerIcon({ kind }: { kind: RelayBannerDto["kind"] }) {
  const Icon =
    kind === "pc-returning" || kind === "cloud-returning" || kind === "cloud-restoring"
      ? RefreshCw
      : kind === "pc-holding"
        ? WifiOff
        : kind === "cloud-silent"
          ? CloudOff
          : ServerCog
  return <Icon className="size-4 shrink-0" aria-hidden="true" />
}

// ---- one read per tab ----------------------------------------------------------------------------------------------

const POLL_MS = 20_000

let current: RelayBannerDto | null = null
let announced: string | null = null
let mounted = 0
let timer: number | null = null
let inFlight = false
const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function publish(next: RelayBannerDto | null) {
  const same =
    next === current ||
    (next !== null && current !== null && next.kind === current.kind && next.title === current.title && next.detail === current.detail)
  if (same) return
  current = next
  listeners.forEach((listener) => listener())
}

async function refresh() {
  if (inFlight) return
  inFlight = true
  try {
    publish(await relayApi.banner())
  } catch (error) {
    // No such endpoint (an older server): no strip. Anything else keeps what is shown.
    if (error instanceof ApiError && error.status === 404) publish(null)
  } finally {
    inFlight = false
  }
}

function schedule() {
  if (timer !== null) window.clearTimeout(timer)
  timer = window.setTimeout(() => {
    timer = null
    if (mounted === 0) return
    void refresh().finally(schedule)
  }, POLL_MS)
}

function watch(): () => void {
  mounted++
  if (mounted === 1) {
    void refresh()
    schedule()
  }

  const onFocus = () => void refresh()
  window.addEventListener("focus", onFocus)
  const stopRefused = onRelayRefused(() => void refresh())
  return () => {
    mounted--
    window.removeEventListener("focus", onFocus)
    stopRefused()
  }
}
