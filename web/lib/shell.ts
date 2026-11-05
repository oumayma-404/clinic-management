/**
 * Where this page is running: a native shell, the iPhone home-screen app, or a plain browser.
 *
 * One owner, because two screens ask: Web Push (an iPhone can receive it only once installed) and the sign-in
 * (« Rester connecté » defaults on in an app, and the server records the first sign-in from a phone app).
 *
 * ⚠️ `window.__clinicShell` is always feature-detected — with it absent this answers exactly as a browser would.
 * An Android Chrome « installed » page is still `"browser"`: the Android app is the shell, not a PWA.
 */
export type AppSurface = "windows" | "android" | "ios" | "ios-home-screen" | "browser"

/** An iPhone or iPad — iPadOS reports itself as a Mac, with a touch screen. */
export function isAppleMobile(): boolean {
  return /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1)
}

/** Running installed (home screen / standalone window) rather than in a browser tab. */
export function isStandalone(): boolean {
  const legacy = (navigator as Navigator & { standalone?: boolean }).standalone
  return legacy === true || window.matchMedia?.("(display-mode: standalone)").matches === true
}

export function appSurface(): AppSurface {
  if (typeof window === "undefined") return "browser"
  const shell = window.__clinicShell?.platform
  if (shell) return shell
  return isAppleMobile() && isStandalone() ? "ios-home-screen" : "browser"
}
