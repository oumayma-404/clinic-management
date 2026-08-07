"use client"

import { useEffect, useState } from "react"
import { uploadPolicyApi, type UploadPolicy } from "@/lib/api/upload-policy"

/**
 * The upload policy, fetched once per page load and shared by every consumer.
 *
 * ⚠️ **A failed probe leaves the picker fully open**, exactly like `/join`'s capability probe: the policy is a
 * courtesy pre-check, the server is the guard, and refusing an upload because a *metadata* read failed would
 * remove a working capability over a network hiccup (§ 0).
 */
let cached: Promise<UploadPolicy> | null = null

function load(): Promise<UploadPolicy> {
  if (!cached) {
    cached = uploadPolicyApi.get().catch((error) => {
      // Drop the rejected promise so a later mount retries rather than replaying the failure for ever.
      cached = null
      throw error
    })
  }
  return cached
}

export function useUploadPolicy(): UploadPolicy | null {
  const [policy, setPolicy] = useState<UploadPolicy | null>(null)

  useEffect(() => {
    let active = true
    load()
      .then((value) => { if (active) setPolicy(value) })
      .catch(() => { /* the picker stays open; the server still checks */ })
    return () => { active = false }
  }, [])

  return policy
}
