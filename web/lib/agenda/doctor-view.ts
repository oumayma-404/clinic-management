import type { DoctorDto } from "@/lib/api/clinics"

/**
 * Whose RDV a person looks at — the agenda's and « À clôturer »'s « Praticien » filter, remembered per account on
 * this device (cabinet-de-groupe D8/D9). ONE preference for both screens, so they cannot drift apart.
 *
 * ⚠️ Browser storage, keyed by the account's e-mail (the « Premiers pas » precedent): a convenience, never state that
 * must persist — a private window, cleared site data or a new PC simply opens on the default. Every access is in
 * try/catch for that reason.
 */

/** « Tous les praticiens ». */
export const ALL_DOCTORS_VIEW = "all"

const KEY_PREFIX = "apexa.agenda.praticien."

function keyFor(email: string): string {
  return KEY_PREFIX + email.trim().toLowerCase()
}

export function readDoctorView(email: string | null | undefined): string | null {
  if (!email) return null
  try {
    return window.localStorage.getItem(keyFor(email))
  } catch {
    return null
  }
}

export function writeDoctorView(email: string | null | undefined, value: string): void {
  if (!email) return
  try {
    window.localStorage.setItem(keyFor(email), value)
  } catch {
    // A device that refuses storage opens on the default next time — nothing is lost.
  }
}

/**
 * The view a screen opens on: the URL's (a shared link), else the remembered one, else the person's own RDV when
 * they are one of several dentists, else everyone's. A dentist id no longer on the active roster is ignored at every
 * step, so a retired colleague's view is never restored by default.
 */
export function resolveDoctorView(input: {
  fromUrl?: string | null
  stored?: string | null
  activeDoctors: readonly DoctorDto[]
  ownDoctorId?: string | null
}): string {
  const ids = input.activeDoctors.map((d) => d.id).filter((id): id is string => Boolean(id))
  const usable = (value: string | null | undefined): value is string =>
    value === ALL_DOCTORS_VIEW || (Boolean(value) && ids.includes(value as string))
  if (usable(input.fromUrl)) return input.fromUrl
  if (usable(input.stored)) return input.stored
  if (ids.length >= 2 && input.ownDoctorId && ids.includes(input.ownDoctorId)) return input.ownDoctorId
  return ALL_DOCTORS_VIEW
}
