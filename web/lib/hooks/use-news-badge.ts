"use client"

import { useEffect, useSyncExternalStore } from "react"
import { newsApi } from "@/lib/api/news"

/**
 * The « Nouveautés » row and its count, shared by the rail, the phone drawer and the bar's « Plus ».
 *
 * One read per page load (`feedbackApi.prompt`'s shape): `absent` is the server's 404 — no news on this install, so no
 * row; a failed read is `failed`, which still shows the row (the page explains its own failure) but no count.
 */
export type NewsBadge =
  | { status: "unknown" }
  | { status: "absent" }
  | { status: "failed" }
  | { status: "ready"; unread: number }

let badge: NewsBadge = { status: "unknown" }
let inflight: Promise<void> | null = null
const listeners = new Set<() => void>()

function publish(next: NewsBadge) {
  badge = next
  listeners.forEach((listener) => listener())
}

function load() {
  if (inflight || badge.status !== "unknown") return
  inflight = newsApi
    .summary()
    .then((summary) => publish(summary ? { status: "ready", unread: summary.unread } : { status: "absent" }))
    .catch(() => publish({ status: "failed" }))
    .finally(() => {
      inflight = null
    })
}

/** The page read the news: the count drops at once, on every surface showing it. */
export function clearNewsBadge() {
  if (badge.status === "ready" && badge.unread !== 0) publish({ status: "ready", unread: 0 })
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** `enabled` is « the signed-in person is an administrator » — nobody else is ever asked. */
export function useNewsBadge(enabled: boolean): { shown: boolean; unread: number } {
  const current = useSyncExternalStore(subscribe, () => badge, () => badge)

  useEffect(() => {
    if (enabled) load()
  }, [enabled])

  if (!enabled) return { shown: false, unread: 0 }
  return {
    shown: current.status === "ready" || current.status === "failed",
    unread: current.status === "ready" ? current.unread : 0,
  }
}
