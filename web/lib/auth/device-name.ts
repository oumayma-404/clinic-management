/**
 * A short, human name for the machine signing in, for « Mes appareils ».
 *
 * ⚠️ **This is a label, never an identifier.** Nothing keys off it, nothing matches on it, and two devices
 * legitimately produce the same string — the practice's two reception PCs are both « Chrome sur Windows ». Its
 * one job is to let somebody looking at a list of sessions recognise which row is the laptop they left at a
 * conference. `SessionFamily.Id` is what actually identifies a session.
 *
 * ⚠️ **Deliberately coarse.** A finer fingerprint (screen size, fonts, timezone) would identify the device
 * better and would also be a tracking surface stored against a named user in a medical records system, which is
 * not a trade this feature needs to make: the user is about to see this list themselves and can end anything
 * they do not recognise.
 */
export function deviceName(): string | undefined {
  if (typeof window === "undefined") return undefined

  // A native shell knows what it is, and says so more accurately than any user-agent string. Read straight off
  // the global like every other consumer in `web/lib` — there is no shared accessor for it.
  const platform = window.__clinicShell?.platform
  if (platform) {
    return `Application ${platform === "windows" ? "Windows" : platform}`
  }

  const browser = browserName(navigator.userAgent)
  const system = platformName(navigator.userAgent)

  if (!browser && !system) return undefined
  if (!system) return browser
  if (!browser) return system
  return `${browser} sur ${system}`
}

/**
 * ⚠️ Order matters and is not alphabetical: Edge's user-agent contains « Chrome », and Chrome's contains
 * « Safari ». Testing the more specific string first is the whole correctness of this function.
 */
function browserName(ua: string): string | undefined {
  if (/Edg\//.test(ua)) return "Edge"
  if (/OPR\/|Opera/.test(ua)) return "Opera"
  if (/Firefox\//.test(ua)) return "Firefox"
  if (/Chrome\//.test(ua)) return "Chrome"
  if (/Safari\//.test(ua)) return "Safari"
  return undefined
}

function platformName(ua: string): string | undefined {
  if (/Windows/.test(ua)) return "Windows"
  if (/Android/.test(ua)) return "Android"
  // Before « Mac », because an iPad reports a Mac-like user-agent in desktop mode.
  if (/iPhone|iPad|iPod/.test(ua)) return "iOS"
  if (/Mac OS X|Macintosh/.test(ua)) return "Mac"
  if (/Linux/.test(ua)) return "Linux"
  return undefined
}
