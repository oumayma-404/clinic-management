"use client"

import { useEffect, useSyncExternalStore } from "react"
import { newsApi } from "@/lib/api/news"

/**
 * The « Nouveautés » row and its count, shared by the rail, the phone drawer and the bar's « Plus ».
 *
 * `absent` is the server's 404 — no news on this install, so no row. A failed read stays `unknown` (no row, no count)
 * and the next screen to mount a consumer asks again, at most once a minute.
 */
export type NewsBadge = { status: "unknown" } | { status: "absent" } | { status: "ready"; unread: number }

const RETRY_AFTER_MS = 60_000

let badge: NewsBadge = { status: "unknown" }
let inflight: Promise<void> | null = null
let retryAt = 0
// Bumped by every answer the page publishes: a summary started before it is older news and is dropped.
let generation = 0
const listeners = new Set<() => void>()

function publish(next: NewsBadge) {
  badge = next
  listeners.forEach((listener) => listener())
}

function load() {
  if (inflight || badge.status !== "unknown" || Date.now() < retryAt) return
  const startedAt = generation
  inflight = newsApi
    .summary()
    .then((summary) => {
      if (startedAt !== generation) return
      publish(summary ? { status: "ready", unread: summary.unread } : { status: "absent" })
    })
    .catch(() => {
      retryAt = Date.now() + RETRY_AFTER_MS
    })
    .finally(() => {
      inflight = null
    })
}

/** The page's own answer — what it read, then 0 once it marked — replaces whatever the rail had, on every surface. */
export function setNewsBadge(unread: number) {
  generation += 1
  if (badge.status !== "ready" || badge.unread !== unread) publish({ status: "ready", unread })
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

  if (!enabled || current.status !== "ready") return { shown: false, unread: 0 }
  return { shown: true, unread: current.unread }
}
