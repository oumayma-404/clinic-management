import { expect, test } from "@playwright/test"
import {
  apiGet,
  clearCachedAccessToken,
  getAccessToken,
  TOKEN_EXCHANGE_LOCK,
} from "../../web/lib/api/client"

/**
 * The token renewal never presents one session cookie twice at once (`web/lib/api/client.ts`) — pure, no page and
 * no API: the real module against a fake `fetch`.
 *
 * ⚠️ **Why this exists.** The server accepts the current refresh credential and the one before it, and reads an
 * older one as a stolen cookie: it ends that device's session and tells the user « Session interrompue … changez
 * votre mot de passe ». A forced renewal used to open its own exchange, so three requests refused in the same
 * second sent the same cookie three times and the third ended the session — 12 such sign-outs on prod in 60 days,
 * 11 of them in the Windows app (2026-11-26: three polls, three renewals, one second). A wire test cannot see it:
 * the race is in the browser's renewal, before any request leaves.
 */

const TOKEN_URL = "/bff/auth/token"
const realFetch = globalThis.fetch
const realNavigator = Object.getOwnPropertyDescriptor(globalThis, "navigator")

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/** A fake BFF + API: counts exchanges, tracks how many overlap, and refuses the tokens it is told to. */
function fakeServer(options: { refused: Set<string>; slowPaths?: Record<string, number> }) {
  const stats = { exchanges: 0, live: 0, maxLive: 0, exchangeTimes: [] as number[] }

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith(TOKEN_URL)) {
      stats.exchanges += 1
      stats.exchangeTimes.push(Date.now())
      stats.live += 1
      stats.maxLive = Math.max(stats.maxLive, stats.live)
      await sleep(25)
      stats.live -= 1
      return Response.json({
        accessToken: `tok-${stats.exchanges}`,
        expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
      })
    }

    const delay = Object.entries(options.slowPaths ?? {}).find(([path]) => url.includes(path))?.[1] ?? 0
    if (delay) await sleep(delay)
    const token = new Headers(init?.headers).get("Authorization")?.replace(/^Bearer /, "") ?? ""
    if (!token || options.refused.has(token)) {
      return Response.json({ error: "Session expirée." }, { status: 401 })
    }
    return Response.json({ ok: true, token })
  }) as typeof fetch

  return stats
}

/** A FIFO Web Lock, shared the way a browser shares one between every window of an origin. */
function installLocks() {
  let tail: Promise<unknown> = Promise.resolve()
  const locks = {
    request(name: string, optionsOrCallback: unknown, maybeCallback?: unknown) {
      const callback = (typeof optionsOrCallback === "function" ? optionsOrCallback : maybeCallback) as (
        lock: { name: string },
      ) => Promise<unknown>
      const run = tail.then(() => callback({ name }))
      tail = run.then(
        () => undefined,
        () => undefined,
      )
      return run
    },
  }
  Object.defineProperty(globalThis, "navigator", {
    value: { ...(realNavigator?.get ? realNavigator.get.call(globalThis) : realNavigator?.value), locks },
    configurable: true,
    writable: true,
  })
  return locks
}

test.beforeEach(() => {
  clearCachedAccessToken()
})

test.afterEach(() => {
  globalThis.fetch = realFetch
  if (realNavigator) Object.defineProperty(globalThis, "navigator", realNavigator)
  clearCachedAccessToken()
})

test("three requests refused at once renew the token ONCE", async () => {
  const stats = fakeServer({ refused: new Set(["tok-1"]) })
  expect(await getAccessToken()).toBe("tok-1")

  // tok-1 is still valid by this window's clock and already expired by the server's — the morning-after case.
  const results = await Promise.all([
    apiGet<{ token: string }>("/appointments/pending-reviews"),
    apiGet<{ token: string }>("/relay/device-target"),
    apiGet<{ token: string }>("/pointage/status"),
  ])

  expect(results.map((r) => r.token)).toEqual(["tok-2", "tok-2", "tok-2"])
  expect(stats.exchanges).toBe(2) // the first token + ONE renewal
  expect(stats.maxLive).toBe(1)
})

test("a refusal landing after the renewal reuses it instead of renewing again", async () => {
  const stats = fakeServer({ refused: new Set(["tok-1"]), slowPaths: { "/late": 120 } })
  expect(await getAccessToken()).toBe("tok-1")

  const results = await Promise.all([
    apiGet<{ token: string }>("/early-a"),
    apiGet<{ token: string }>("/early-b"),
    apiGet<{ token: string }>("/late"),
  ])

  expect(results.map((r) => r.token)).toEqual(["tok-2", "tok-2", "tok-2"])
  expect(stats.exchanges).toBe(2)
})

test("a refused token is never served again", async () => {
  const stats = fakeServer({ refused: new Set(["tok-1"]) })
  expect(await getAccessToken()).toBe("tok-1")

  expect(await getAccessToken(true, "tok-1")).toBe("tok-2")
  expect(await getAccessToken()).toBe("tok-2")
  expect(stats.exchanges).toBe(2)
})

test("an exchange waits while another window holds the lock", async () => {
  installLocks()
  const stats = fakeServer({ refused: new Set() })

  // Another window is mid-exchange: it holds the lock for 80 ms.
  let otherWindowDone = 0
  const otherWindow = navigator.locks.request(TOKEN_EXCHANGE_LOCK, async () => {
    await sleep(80)
    otherWindowDone = Date.now()
  })

  expect(await getAccessToken()).toBe("tok-1")
  await otherWindow

  expect(stats.exchanges).toBe(1)
  expect(stats.exchangeTimes[0]).toBeGreaterThanOrEqual(otherWindowDone)
})
