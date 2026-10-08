"use client"

import { useEffect } from "react"

import { ApiError } from "@/lib/api/client"
import { relayApi } from "@/lib/api/relay"
import { useSession } from "@/lib/auth/session"

/**
 * AC-6.2 (`clinic-pc-copy`): the cabinet's own Windows and Android apps tell the cloud, while it is locked for a PC de
 * secours that said nothing, whether they still reach that PC. Two « cloud yes, PC no » reports from the cabinet's
 * network end the lock; the cloud decides everything — this only asks where to look and passes the answer on.
 *
 * ⚠️ **Inert in a browser, by construction**: without the shell's `relayProbe` nothing is scheduled and no request is
 * sent, so a browser is byte-identical to before (a browser cannot tell an untrusted certificate from a PC that is down).
 * ⚠️ **One loop per page, not per mount**: the app shell remounts on every navigation, so the schedule lives at module
 * level and a mount only says « someone signed in is here ». It stops at the next tick once nothing is mounted.
 */
export function RelayDeviceWatch() {
  const { user } = useSession()
  useEffect(() => (user ? watchRelayDevices() : undefined), [user])
  return null
}

const FIRST_CHECK_MS = 5_000
const RETRY_SECONDS = 30
const IDLE_SECONDS = 600

let mounted = 0
let timer: number | null = null
let running = false

function watchRelayDevices(): (() => void) | undefined {
  if (typeof window === "undefined" || typeof window.__clinicShell?.relayProbe !== "function") return undefined
  mounted++
  if (timer === null && !running) timer = window.setTimeout(tick, FIRST_CHECK_MS)
  return () => {
    mounted = Math.max(0, mounted - 1)
  }
}

async function tick() {
  timer = null
  if (mounted === 0) return
  running = true
  let nextSeconds = RETRY_SECONDS
  try {
    nextSeconds = await checkOnce()
  } catch (err) {
    // A deployment with no PC de secours at all answers 404: ask again much later. Anything else is a retry.
    nextSeconds = err instanceof ApiError && err.status === 404 ? IDLE_SECONDS : RETRY_SECONDS
  } finally {
    running = false
  }
  if (mounted > 0) timer = window.setTimeout(tick, Math.max(RETRY_SECONDS, nextSeconds) * 1000)
}

async function checkOnce(): Promise<number> {
  const shell = window.__clinicShell
  const target = await relayApi.deviceTarget()
  if (target.probe && target.port && target.certificateFingerprint && typeof shell?.relayProbe === "function") {
    const result = await shell.relayProbe({
      addresses: target.addresses,
      port: target.port,
      fingerprint: target.certificateFingerprint,
    })
    if (result) {
      await relayApi.deviceReport({ reachesPc: result.reached === true, gateways: result.gateways ?? [] })
    }
  }
  return target.intervalSeconds
}
