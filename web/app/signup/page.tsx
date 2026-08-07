"use client"

import type React from "react"

import { useEffect, useState } from "react"
import Link from "next/link"
import { Building2, Loader2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import SetupWizard from "@/components/setup-wizard"
import { authApi } from "@/lib/api/auth"
import { CAPABILITY_PROBE_TIMEOUT_MS, withTimeout } from "@/lib/capability-probe"

/**
 * The public door onto a hosted backend — and, since the two flows were unified, **the same wizard `/setup`
 * shows**, not a second form of its own.
 *
 * <p>They had drifted into two surfaces asking overlapping questions with different wording, different fields
 * (this one collected no gouvernorat and no horaires) and one long scroll instead of three steps, while ending
 * at the *same* `LocalClinicProvisioning.ProvisionAsync` on the server. `SetupWizard flow="signup"` carries the
 * only two differences a public door forces: the answers become a pending `ClinicSignup` that an emailed token
 * provisions, and the logo step is deferred to Paramètres because blobs are keyed by a clinic id that does not
 * exist yet.</p>
 *
 * <p>What stays here is the capability probe, which is this page's own job: `/setup` is gated server-side by
 * « no users yet » while this one is gated by `AllowsPublicClinicSignup`.</p>
 */
export default function SignUpPage() {
  const [isProbing, setIsProbing] = useState(true)
  const [signupClosed, setSignupClosed] = useState(false)

  useEffect(() => {
    let cancelled = false

    const probe = async () => {
      try {
        const { publicSignupEnabled } = await withTimeout(authApi.getMode(), CAPABILITY_PROBE_TIMEOUT_MS)
        // `=== true`, not `!publicSignupEnabled`: the field is absent on an older API, and an undefined answer is
        // not a « non ». It closes the door, which is the safe direction and matches the endpoint's own 404.
        if (!cancelled) setSignupClosed(publicSignupEnabled !== true)
      } catch (err) {
        // A failed probe is not evidence the door is shut, and refusing on a network hiccup is the worse error
        // — the `/join` precedent. Fall through to the form; the endpoint's own 404 becomes the same explanation.
        console.error("Could not read the deployment's signup capability:", err)
      }
      if (!cancelled) setIsProbing(false)
    }

    probe()
    return () => {
      cancelled = true
    }
  }, [])

  if (isProbing) {
    return (
      <Shell>
        <div className="flex items-center gap-3 text-muted-foreground" role="status">
          <Loader2 className="size-5 animate-spin" aria-hidden="true" />
          <p>Vérification…</p>
        </div>
      </Shell>
    )
  }

  if (signupClosed) {
    return (
      <Shell>
        <Card className="border-primary/20 shadow-lg">
          <CardHeader className="space-y-4 text-center">
            <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-accent/20">
              <Building2 className="size-8 text-primary" aria-hidden="true" />
            </div>
            <div>
              <CardTitle className="text-2xl text-accent-foreground">Inscription non disponible ici</CardTitle>
              <CardDescription className="mt-2">
                Cette installation ne permet pas de créer un cabinet en ligne. Si votre cabinet existe déjà,
                connectez-vous ; sinon, contactez l&apos;exploitant du service, qui créera votre cabinet.
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent>
            <Button asChild className="w-full coarse:h-11">
              <Link href="/login">Aller à la connexion</Link>
            </Button>
          </CardContent>
        </Card>
      </Shell>
    )
  }

  // `onComplete` is a no-op: in the signup flow the wizard ends on its own « Vérifiez votre boîte mail » panel
  // rather than handing control back — nothing exists yet to navigate into.
  return <SetupWizard flow="signup" onComplete={() => {}} />
}

/** The centred column the two pre-wizard states share. Wide content is the wizard's own concern. */
function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">{children}</div>
    </div>
  )
}
