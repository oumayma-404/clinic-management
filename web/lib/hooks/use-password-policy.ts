"use client"

import { useEffect, useState } from "react"
import { authApi } from "@/lib/api/auth"

/**
 * The server's minimum password length, fetched once per page load and shared by every screen that collects a
 * **new** password (`hosted-security-hardening` FR-1.9).
 *
 * ⚠️ **`null` means « we do not know yet », and the caller must then not pre-check at all** — it does not mean
 * zero and it must never be replaced by a literal default. A fallback number here would restore exactly the
 * second authority this hook exists to delete: the five server-side set-paths enforce
 * `PasswordPolicy.MinLength`, and any figure written here would be the one that silently disagrees the day that
 * constant moves. The client check is a courtesy, the server is the guard — `useUploadPolicy`'s contract, for
 * the same reason.
 *
 * A failed probe therefore leaves the form fully usable: the user submits, and if the password really is too
 * short the server refuses it with its own French sentence naming its own number.
 */
let cached: Promise<number | null> | null = null

function load(): Promise<number | null> {
  if (!cached) {
    cached = authApi
      .getMode()
      .then((mode) => (typeof mode.passwordMinLength === "number" ? mode.passwordMinLength : null))
      .catch((error) => {
        // Drop the rejected promise so a later mount retries instead of replaying the failure for ever.
        cached = null
        throw error
      })
  }
  return cached
}

export function usePasswordMinLength(): number | null {
  const [minLength, setMinLength] = useState<number | null>(null)

  useEffect(() => {
    let active = true
    load()
      .then((value) => { if (active) setMinLength(value) })
      .catch(() => { /* the form stays open; the server still checks */ })
    return () => { active = false }
  }, [])

  return minLength
}
