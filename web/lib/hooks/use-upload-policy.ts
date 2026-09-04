"use client"

import { useEffect, useState } from "react"
import { uploadPolicyApi, type UploadPolicy, type UploadProfile } from "@/lib/api/upload-policy"

/**
 * The upload policy, fetched once per page load and shared by every consumer.
 *
 * ⚠️ **A failed probe leaves the picker fully open**, exactly like `/join`'s capability probe: the policy is a
 * courtesy pre-check, the server is the guard, and refusing an upload because a *metadata* read failed would
 * remove a working capability over a network hiccup (§ 0).
 */
const cached = new Map<UploadProfile, Promise<UploadPolicy>>()

function load(profile: UploadProfile): Promise<UploadPolicy> {
  const existing = cached.get(profile)
  if (existing) return existing

  const pending = uploadPolicyApi.get(profile).catch((error) => {
    // Drop the rejected promise so a later mount retries rather than replaying the failure for ever.
    cached.delete(profile)
    throw error
  })

  cached.set(profile, pending)
  return pending
}

/**
 * ⚠️ **Cached per door, not once for the app.** The cachet, the clinic logo and the CSV import each carried a
 * hand-written `accept` — `image/*` against a PNG-and-JPEG server profile, `.csv` against one that also takes
 * `.txt`, and a « 2 Mo maximum » the server did not enforce — so a policy shared across doors would simply move
 * the disagreement one level up and quote the patient drawer's 150 Mo on a logo field.
 */
/**
 * ⚠️ **`null` means « do not ask », and it exists because this endpoint is authenticated.**
 * `GET /api/meta/upload-policy` is `[Authorize(AnyClinicRole)]`, so on a door reached with no session it 401s —
 * and a 401 on the token exchange behind it is what the app reads as « session expirée ». `SetupWizard` is the
 * case: one component serves `/setup` and the public `/signup`, and it called this on mount for a logo step that
 * `/signup` does not even render. Passing `null` where the picker is not on screen is the fix at the source; the
 * public-route guard in `lib/auth/session.tsx` is the one that stops the same shape ejecting a visitor again.
 */
export function useUploadPolicy(profile: UploadProfile | null = "patient-file"): UploadPolicy | null {
  const [policy, setPolicy] = useState<UploadPolicy | null>(null)

  useEffect(() => {
    let active = true
    if (!profile) {
      // Not merely « skip the fetch »: a consumer that turns the door off must not keep quoting the answer for
      // the one it had open before.
      setPolicy(null)
      return
    }
    load(profile)
      .then((value) => { if (active) setPolicy(value) })
      .catch(() => { /* the picker stays open; the server still checks */ })
    return () => { active = false }
  }, [profile])

  return policy
}
