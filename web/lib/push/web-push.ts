import { pushDevicesApi } from "@/lib/api/push-devices"

/**
 * Web Push (staff-chat Part 8): notifications for a browser, and above all for an iPhone home-screen app, which can
 * receive them only this way. `public/sw.js` shows them; this module subscribes the browser and ties the
 * subscription to whoever is signed in.
 *
 * ⚠️ **Nothing here ever asks for the permission** — that is `askNotifications` in `lib/staff-chat/alerts.ts`, behind
 * a tap, because iOS refuses a prompt that no gesture started. A browser that already allowed notifications is
 * re-subscribed silently at sign-in, which is what moves a shared phone over to its next user.
 *
 * ⚠️ **Off inside a shell**: the Windows app has its own notifications and the Android app receives Firebase's.
 */

/** Matches the backend `DevicePlatform.WebPush`. */
const WEB_PUSH = 3
const WORKER = "/sw.js"
const RELEASE_TIMEOUT_MS = 2000

export type WebPushCapability = "unsupported" | "home-screen" | "supported"

let registered: string | null = null
let publicKey: Promise<string | null> | null = null

/** An iPhone or iPad — iPadOS reports itself as a Mac, with a touch screen. */
export function isAppleMobile(): boolean {
  return /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1)
}

function isStandalone(): boolean {
  const legacy = (navigator as Navigator & { standalone?: boolean }).standalone
  return legacy === true || window.matchMedia?.("(display-mode: standalone)").matches === true
}

/** What this browser can do, before asking the server. `home-screen`: an iPhone in Safari, which must install the app first. */
export function webPushCapability(): WebPushCapability {
  if (typeof window === "undefined" || window.__clinicShell) return "unsupported"
  if (isAppleMobile() && !isStandalone()) return "home-screen"
  return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window ? "supported" : "unsupported"
}

/** The server's VAPID public key — null when this deployment cannot send Web Push. Read once per page. */
export function webPushPublicKey(): Promise<string | null> {
  publicKey ??= pushDevicesApi
    .availability()
    .then((availability) => availability.webPushPublicKey ?? null)
    .catch(() => {
      publicKey = null
      return null
    })
  return publicKey
}

function fromBase64Url(value: string): Uint8Array<ArrayBuffer> {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=")
  const raw = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}

function sameKey(current: ArrayBuffer | null | undefined, expected: Uint8Array): boolean {
  if (!current) return false
  const bytes = new Uint8Array(current)
  return bytes.length === expected.length && bytes.every((b, i) => b === expected[i])
}

/** Subscribes this browser (the permission already granted) and registers it to the signed-in account. */
export async function subscribeWebPush(): Promise<boolean> {
  if (webPushCapability() !== "supported" || Notification.permission !== "granted") return false
  const key = await webPushPublicKey()
  if (!key) return false

  const applicationServerKey = fromBase64Url(key)
  const registration = await navigator.serviceWorker.register(WORKER, { scope: "/" })
  await navigator.serviceWorker.ready
  let subscription = await registration.pushManager.getSubscription()
  // A subscription made against an older server key can no longer be sent to.
  if (subscription && !sameKey(subscription.options.applicationServerKey, applicationServerKey)) {
    await subscription.unsubscribe()
    subscription = null
  }
  subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })

  const keys = subscription.toJSON().keys ?? {}
  await pushDevicesApi.register({
    platform: WEB_PUSH,
    token: subscription.endpoint,
    webPushP256dh: keys.p256dh,
    webPushAuth: keys.auth,
  })
  registered = subscription.endpoint
  return true
}

/** At sign-in. Never prompts; a browser that has not allowed notifications is left alone. */
export async function resumeWebPush() {
  try {
    await subscribeWebPush()
  } catch {
    // A refused or offline registration leaves the in-app alerts exactly as they were.
  }
}

/** Before sign-out: this browser stops receiving the account's notifications. Bounded, never blocking. */
export async function releaseWebPush() {
  const endpoint = registered
  if (!endpoint) return
  registered = null
  try {
    await Promise.race([
      pushDevicesApi.release(endpoint),
      new Promise((resolve) => setTimeout(resolve, RELEASE_TIMEOUT_MS)),
    ])
  } catch {
    // The next sign-in on this browser rebinds the subscription.
  }
}

/** A page notice routed through the worker, so a later push for the same thread keeps it; null without one. */
export async function workerRegistration(): Promise<ServiceWorkerRegistration | null> {
  if (typeof navigator === "undefined" || !("serviceWorker" in navigator)) return null
  try {
    return (await navigator.serviceWorker.getRegistration("/")) ?? null
  } catch {
    return null
  }
}
