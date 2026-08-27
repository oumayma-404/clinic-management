"use client"

import { useCallback, useEffect, useRef, useState } from "react"

/**
 * Meta's **Embedded Signup v4** flow, in one place.
 *
 * ⚠️ **It was extracted rather than copied** (`vendor-whatsapp-messaging-quota` § 31/§ 38). Two surfaces run this
 * flow now — the vendor-managed connect card on « Rappels » and the manual-credentials card in « Paramètres » — and
 * two copies of a five-outcome popup protocol is how one of them keeps handling only `FINISH` for ever.
 *
 * ⚠️ **v3 → v4 (Story 0).** `extras.sessionInfoVersion: "3"` *is* the v3 marker and is gone; every other member of
 * the `FB.login` config below already matched v4. The 15 Oct 2026 deprecation names **v2** only, so this is a move to
 * current rather than a forced one.
 *
 * ⚠️ **The origin allow-list is deliberately stricter than Meta's own sample**, which uses
 * `event.origin.endsWith('facebook.com')` — that also matches `notfacebook.com`. Do not « align with the sample ».
 *
 * ⚠️ **Graph `v21.0` → `v26.0` is NOT this hook's business.** It moves every *server* Graph call too (onboarding,
 * template submission, the status poll) and has its own follow-up (R-2a). One key feeds both sides already.
 */

const META_APP_ID = process.env.NEXT_PUBLIC_META_APP_ID ?? ""
const META_CONFIG_ID = process.env.NEXT_PUBLIC_META_CONFIG_ID ?? ""

/**
 * ⚠️ Read from the environment, never hard-coded: the server pins the same version in `MetaConfig`, and before
 * Story 0 the two never derived from each other — so `Meta:GraphApiVersion` moved the server's calls and left the
 * browser SDK a version behind. Both come from one `META_GRAPH_API_VERSION` key in `deploy/`.
 */
const META_GRAPH_VERSION = process.env.NEXT_PUBLIC_META_GRAPH_VERSION ?? "v21.0"

const FACEBOOK_ORIGINS = ["https://www.facebook.com", "https://web.facebook.com"]

/**
 * Meta documents **five** finish events plus `ERROR`, and the shipped code matched `=== "FINISH"` alone — so a
 * cabinet completing any other way was silently dropped and the connection merely appeared to have failed.
 */
const FINISH_EVENTS = [
  "FINISH",
  "FINISH_ONLY_WABA",
  "FINISH_WHATSAPP_BUSINESS_BUSINESS_APP_ONBOARDING",
  "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING",
  "FINISH_OBO_MIGRATION",
  "FINISH_GRANT_ONLY_API_ACCESS",
] as const

/** What the popup told us, once the `FB.login` callback has also returned. */
export type EmbeddedSignupOutcome =
  /** A complete connection: a code, a WABA and a phone number. */
  | { kind: "connected"; code: string; wabaId: string; phoneNumberId: string; businessId: string | null }
  /**
   * ⚠️ `FINISH_ONLY_WABA` means the user finished **without adding a phone number**, so it must not be treated as a
   * completed connection — there is no `phone_number_id` to register and the old code would have crashed on its
   * absence. Its own outcome, with its own French sentence.
   */
  | { kind: "no-phone-number" }
  /** The popup was closed, or it finished in a shape that carried no code. */
  | { kind: "cancelled" }
  /** Meta reported `ERROR`. */
  | { kind: "failed" }

interface EmbeddedSignupData {
  waba_id?: string
  phone_number_id?: string
  business_id?: string
}

interface FbLoginResponse {
  authResponse?: { code?: string } | null
  status?: string
}

interface FbSdk {
  init(params: { appId: string; autoLogAppEvents?: boolean; xfbml?: boolean; version: string }): void
  login(callback: (response: FbLoginResponse) => void, options?: Record<string, unknown>): void
}

declare global {
  interface Window {
    FB?: FbSdk
    fbAsyncInit?: () => void
  }
}

export function useWhatsAppEmbeddedSignup({
  enabled,
  onOutcome,
}: {
  /** Load the SDK only where the flow can actually be offered. */
  enabled: boolean
  onOutcome: (outcome: EmbeddedSignupOutcome) => void
}) {
  const [sdkReady, setSdkReady] = useState(false)
  const signupDataRef = useRef<EmbeddedSignupData | null>(null)
  const finishEventRef = useRef<string | null>(null)
  const mounted = useRef(true)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    if (!enabled || !META_APP_ID) return

    const handleMessage = (event: MessageEvent) => {
      if (!FACEBOOK_ORIGINS.includes(event.origin)) return
      try {
        const parsed = JSON.parse(event.data as string)
        if (parsed?.type !== "WA_EMBEDDED_SIGNUP") return

        if (typeof parsed?.event === "string" && FINISH_EVENTS.includes(parsed.event)) {
          finishEventRef.current = parsed.event
          signupDataRef.current = {
            waba_id: parsed?.data?.waba_id,
            phone_number_id: parsed?.data?.phone_number_id,
            // v4's success payload carries the customer's business-portfolio id, which we used to drop.
            business_id: parsed?.data?.business_id,
          }
        } else if (parsed?.event === "ERROR") {
          finishEventRef.current = "ERROR"
        }
      } catch {
        // Non-JSON messages from other sources are ignored.
      }
    }
    window.addEventListener("message", handleMessage)

    if (window.FB) {
      setSdkReady(true)
    } else if (!document.getElementById("facebook-jssdk")) {
      window.fbAsyncInit = () => {
        window.FB?.init({ appId: META_APP_ID, autoLogAppEvents: true, xfbml: false, version: META_GRAPH_VERSION })
        if (mounted.current) setSdkReady(true)
      }
      const script = document.createElement("script")
      script.id = "facebook-jssdk"
      script.src = "https://connect.facebook.net/en_US/sdk.js"
      script.async = true
      script.defer = true
      script.crossOrigin = "anonymous"
      document.body.appendChild(script)
    }

    return () => window.removeEventListener("message", handleMessage)
  }, [enabled])

  const start = useCallback(() => {
    signupDataRef.current = null
    finishEventRef.current = null

    window.FB?.login(
      (response) => {
        const code = response.authResponse?.code
        const data = signupDataRef.current
        const event = finishEventRef.current
        signupDataRef.current = null
        finishEventRef.current = null

        if (event === "ERROR") {
          onOutcome({ kind: "failed" })
          return
        }

        // A WABA with no phone number is a real, distinct completion — not a cancellation and not a crash.
        if (event === "FINISH_ONLY_WABA" || (data?.waba_id && !data?.phone_number_id)) {
          onOutcome({ kind: "no-phone-number" })
          return
        }

        if (!code || !data?.waba_id || !data?.phone_number_id) {
          onOutcome({ kind: "cancelled" })
          return
        }

        onOutcome({
          kind: "connected",
          code,
          wabaId: data.waba_id,
          phoneNumberId: data.phone_number_id,
          businessId: data.business_id ?? null,
        })
      },
      {
        config_id: META_CONFIG_ID,
        response_type: "code",
        override_default_response_type: true,
        // No `sessionInfoVersion` — see the ⚠️ at the top. Meta's v4 sample carries none.
        extras: { setup: {} },
      },
    )
  }, [onOutcome])

  return {
    /** The SDK has loaded and `start()` can run. */
    sdkReady,
    /** Whether this build carries the two public Meta ids the flow needs at all. */
    configured: Boolean(META_APP_ID) && Boolean(META_CONFIG_ID),
    start,
  }
}
