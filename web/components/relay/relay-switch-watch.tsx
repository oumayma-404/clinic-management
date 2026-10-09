"use client"

import { useEffect } from "react"

import { relayApi } from "@/lib/api/relay"
import { useSession } from "@/lib/auth/session"
import { onRelayBanner } from "./relay-banner"

/**
 * `clinic-pc-copy` Part 3: the cabinet's Windows app follows the PC de secours. This half lives in the page because only
 * the page holds the person's session on the cloud:
 *
 * - **once a day**, it fetches the person's ticket (D22) and hands it to the shell, which trades it on the PC and keeps
 *   that session in its cookie for the PC — so after a switch nobody signs in again;
 * - **when the cloud says the cabinet works on the PC** (the strip's `cloud-on-relay`), it asks the shell to check the PC
 *   and move there — the case where the PC alone lost the cloud and the cabinet would otherwise sit read-only.
 *
 * The rest is the shell's own (a cloud that does not load at all, and the way back once the PC lets go).
 *
 * ⚠️ **Inert in a browser and in an older shell**: nothing runs without `relayPrepare` / `relaySwitch`.
 * ⚠️ **Module level, not per mount**: the app shell remounts on every navigation; a mount only says « someone signed in
 * is here ».
 */
export function RelaySwitchWatch() {
  const { user } = useSession()
  useEffect(() => (user ? watch() : undefined), [user])
  return null
}

const PREPARED_KEY = "apexa.relay.preparedAt"
const PREPARE_EVERY_MS = 20 * 60 * 60 * 1000
const CHECK_EVERY_MS = 60 * 60 * 1000
const SWITCH_AGAIN_AFTER_MS = 15_000

let mounted = 0
let timer: number | null = null
let stopBanner: (() => void) | null = null
let lastSwitchAt = 0

function watch(): (() => void) | undefined {
  const shell = typeof window === "undefined" ? undefined : window.__clinicShell
  if (typeof shell?.relayPrepare !== "function" && typeof shell?.relaySwitch !== "function") return undefined

  mounted++
  if (mounted === 1) {
    void prepareIfDue()
    timer = window.setInterval(() => void prepareIfDue(), CHECK_EVERY_MS)
    stopBanner = onRelayBanner((banner) => {
      if (banner?.kind === "cloud-on-relay") void switchToPc()
    })
  }

  return () => {
    mounted = Math.max(0, mounted - 1)
    if (mounted === 0) {
      if (timer !== null) window.clearInterval(timer)
      timer = null
      stopBanner?.()
      stopBanner = null
    }
  }
}

function preparedAt(): number {
  try {
    return Number(window.localStorage.getItem(PREPARED_KEY) ?? 0) || 0
  } catch {
    return 0
  }
}

async function prepareIfDue() {
  const shell = window.__clinicShell
  if (typeof shell?.relayPrepare !== "function" || Date.now() - preparedAt() < PREPARE_EVERY_MS) return
  try {
    const ticket = await relayApi.assertion()
    if (!ticket.port || !ticket.fingerprint || ticket.addresses.length === 0) return
    const prepared = await shell.relayPrepare({
      assertion: ticket.assertion,
      addresses: ticket.addresses,
      port: ticket.port,
      fingerprint: ticket.fingerprint,
    })
    if (prepared) {
      try {
        window.localStorage.setItem(PREPARED_KEY, String(Date.now()))
      } catch {
        // Not remembered: the next check prepares again, which is harmless.
      }
    }
  } catch {
    // No PC de secours, an older server, the PC unreachable from here: nothing to prepare today.
  }
}

async function switchToPc() {
  const shell = window.__clinicShell
  if (typeof shell?.relaySwitch !== "function" || Date.now() - lastSwitchAt < SWITCH_AGAIN_AFTER_MS) return
  lastSwitchAt = Date.now()
  try {
    await shell.relaySwitch()
  } catch {
    // The strip keeps saying it; the next change asks again.
  }
}
