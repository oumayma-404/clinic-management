"use client"

import { useEffect, useState } from "react"
import { authApi, type AuthModeDto } from "@/lib/api/auth"

/**
 * The deployment's password-related capabilities, fetched **once per page load** and shared by every screen that
 * needs one.
 *
 * ⚠️ **One cached promise for the whole `GET /api/auth/mode` answer**, not one per field. It held only the minimum
 * length until the login screen needed `passwordResetEnabled` as well — and two module-level caches over the same
 * endpoint would mean the login page issuing the same probe twice, then disagreeing about it for the rest of the
 * session if one of the two failed.
 */
let cached: Promise<AuthModeDto> | null = null

function load(): Promise<AuthModeDto> {
  if (!cached) {
    cached = authApi.getMode().catch((error) => {
      // Drop the rejected promise so a later mount retries instead of replaying the failure for ever.
      cached = null
      throw error
    })
  }
  return cached
}

/**
 * The server's minimum password length (`hosted-security-hardening` FR-1.9).
 *
 * ⚠️ **`null` means « we do not know yet », and the caller must then not pre-check at all** — it does not mean
 * zero and it must never be replaced by a literal default. A fallback number here would restore exactly the
 * second authority this hook exists to delete: the server-side set-paths enforce `PasswordPolicy.MinLength`, and
 * any figure written here would be the one that silently disagrees the day that constant moves. The client check
 * is a courtesy, the server is the guard — `useUploadPolicy`'s contract, for the same reason.
 *
 * A failed probe therefore leaves the form fully usable: the user submits, and if the password really is too
 * short the server refuses it with its own French sentence naming its own number.
 */
export function usePasswordMinLength(): number | null {
  const [minLength, setMinLength] = useState<number | null>(null)

  useEffect(() => {
    let active = true
    load()
      .then((mode) => {
        if (active && typeof mode.passwordMinLength === "number") setMinLength(mode.passwordMinLength)
      })
      .catch(() => { /* the form stays open; the server still checks */ })
    return () => { active = false }
  }, [])

  return minLength
}

/**
 * Whether this deployment lets a person reset their own forgotten password behind a mailed link.
 *
 * ⚠️ **Three-valued, and the `null` matters.** `null` is « not known yet » — the login screen renders *neither*
 * branch while it holds, because the two say opposite things about who can help and flashing the wrong one is
 * worse than a moment of nothing. `false` is a real answer: a `SelfHostedLan` install has no SMTP, so the screen
 * names the administrator instead of offering a link that would 404.
 *
 * ⚠️ **`=== true`**, following `publicSignupEnabled`'s rolling-deploy convention: `web` and `api` are separate
 * containers in the hosted topology, so a newer page may be served by an older API that omits the field. An
 * absent value is `false`, which lands on the sentence that is true on every deployment.
 *
 * A failed probe stays `null`, so the login screen shows no recovery line at all rather than a wrong one — and
 * `/mot-de-passe-oublie` remains reachable by URL, where it probes again and defaults to offering the form.
 */
export function usePasswordResetEnabled(): boolean | null {
  const [enabled, setEnabled] = useState<boolean | null>(null)

  useEffect(() => {
    let active = true
    load()
      .then((mode) => { if (active) setEnabled(mode.passwordResetEnabled === true) })
      .catch(() => { /* stays null: no line rather than the wrong line */ })
    return () => { active = false }
  }, [])

  return enabled
}

/**
 * Whether this deployment still lets somebody create their own account from a clinic code.
 *
 * ⚠️ **Defaults to `true`, and a failed probe leaves it there.** Every consumer gates on the flag rather than on
 * its negation, so an unread probe shows the code — because on a `SelfHostedLan` install the join code is the
 * *only* way staff get an account, and hiding it on a network blip would remove the only door into the product.
 * Where the answer really is `false` the code has no consumer at all: nothing in this product reads `ClinicCode`
 * except the join path.
 *
 * ⚠️ **`=== true`**, following `passwordResetEnabled`'s rolling-deploy convention — `web` and `api` are separate
 * containers, so a newer page may be served by an older API that omits the field.
 *
 * ⚠️ **Two surfaces show the clinic code, and that is why this is a hook rather than an effect in one of them.**
 * `user-management.tsx` had its own private probe and `clinic-settings.tsx` had none, so « Paramètres » went on
 * printing « Communiquez ce code à vos collègues » on a hosted deployment where the code creates nothing —
 * `multi-tenant-cloud` US-3 gated one of the two and the other was never found. `check:responsive`'s
 * `clinic-code-gated` is the derived guard.
 */
export function useSelfRegistrationEnabled(): boolean {
  const [enabled, setEnabled] = useState(true)

  useEffect(() => {
    let active = true
    load()
      .then((mode) => { if (active) setEnabled(mode.selfRegistrationEnabled === true) })
      .catch(() => { /* stays true: never hide a LAN install's only door on a failed probe */ })
    return () => { active = false }
  }, [])

  return enabled
}
