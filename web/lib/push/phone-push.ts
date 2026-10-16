import { pushDevicesApi } from "@/lib/api/push-devices"

/**
 * Phone notifications (staff-chat Part 7): the Android app's token, tied to whoever is signed in on this phone.
 *
 * ⚠️ **Sign-out must release it before the session cookie goes** — deregistering needs the bearer, and a phone left
 * registered keeps buzzing for an account its holder has left (a shared reception phone is the ordinary case). The
 * server rebinds a token on the next sign-in anyway, so a release that fails costs nothing but that gap.
 */

/** Matches the backend `DevicePlatform.Android`. */
const ANDROID = 1
const RELEASE_TIMEOUT_MS = 2000

let listening = false
let registered: string | null = null

/** Once per page, once signed in: every token the app hands over is registered to this account. */
export function startPhonePush() {
  if (listening || typeof window === "undefined") return
  const shell = window.__clinicShell
  if (shell?.platform !== "android" || typeof shell.onPushToken !== "function") return
  listening = true
  shell.onPushToken((token) => {
    if (token === registered) return
    pushDevicesApi
      .register({ platform: ANDROID, token, shellVersion: shell.version })
      .then(() => {
        registered = token
      })
      .catch(() => {
        // A deployment with no push answers 404; the phone simply gets no notifications, as before.
      })
  })
}

/** Before signing out. Bounded, because nothing may hold the sign-out up. */
export async function stopPhonePush() {
  const token = registered
  if (!token) return
  registered = null
  try {
    await Promise.race([
      pushDevicesApi.deregister(token),
      new Promise((resolve) => setTimeout(resolve, RELEASE_TIMEOUT_MS)),
    ])
  } catch {
    // The next sign-in on this phone rebinds the token.
  }
}
