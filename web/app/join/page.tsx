"use client"

import { useState, useEffect } from "react"
import { useRouter } from "next/navigation"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Building2, ArrowRight, AlertCircle } from "lucide-react"
import { clinicsApi } from "@/lib/api/clinics"
import { authApi } from "@/lib/api/auth"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import JoinWizard from "@/components/join-wizard"
import JoinUnavailable from "@/components/join-unavailable"
import { CAPABILITY_PROBE_TIMEOUT_MS, withTimeout } from "@/lib/capability-probe"

export default function JoinClinicPage() {
  const router = useRouter()
  const { user, isLoading: userLoading, mode } = useSession()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const [clinicCode, setClinicCode] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isChecking, setIsChecking] = useState(true)
  const [showWizard, setShowWizard] = useState(false)
  const [selfRegistrationClosed, setSelfRegistrationClosed] = useState(false)

  useEffect(() => {
    let cancelled = false

    const checkUserStatus = async () => {
      // Local self-registration needs no session — the clinic code is the gate. But whether that gate exists at
      // all is a deployment capability, and `mode` cannot answer it: AUTH_MODE reads `local` both on a clinic's
      // own PC and on the hosted backend (US-3). Ask the server.
      if (mode === "local") {
        try {
          // ⚠️ Bounded, because `setIsChecking(false)` now waits on this call and `apiGet` attaches no timeout of its
          // own. A *rejected* fetch was handled; a **stalled** one was not — an API mid-restart, a marginal mobile
          // signal or a captive portal that completes the handshake and never answers left « Vérification du statut
          // de votre clinique… » on screen for ever, with no retry, no error and no way forward, on the normal way
          // into a LAN install. A timeout is treated exactly like the rejection below.
          const { selfRegistrationEnabled } = await withTimeout(authApi.getMode(), CAPABILITY_PROBE_TIMEOUT_MS)
          if (cancelled) return
          setSelfRegistrationClosed(!selfRegistrationEnabled)
        } catch (err) {
          // The probe failing is not evidence that registration is closed, and on a LAN — where this page is
          // the normal way in — refusing on a network hiccup would be the worse error. Fall through to the form;
          // JoinWizard turns the register endpoint's own 404 into the same explanation if it really is closed.
          console.error("Could not read the deployment's auth capabilities:", err)
        }
        if (!cancelled) setIsChecking(false)
        return
      }

      // Wait for auth to load
      if (userLoading || authLoading) {
        return
      }

      // If not authenticated, redirect to Auth0 login (Cloud only — Local returned above).
      if (!user || !accessToken) {
        window.location.href = "/auth/login?returnTo=/join"
        return
      }

      try {
        const status = await clinicsApi.getUserStatus()
        if (cancelled) return
        if (status.hasClinic) {
          // User has clinic, redirect to app
          window.location.href = "/"
          return
        }
        // User doesn't have clinic, show join form
        setIsChecking(false)
      } catch (err) {
        console.error("Error checking user status:", err)
        if (!cancelled) setIsChecking(false)
      }
    }

    checkUserStatus()
    return () => {
      cancelled = true
    }
  }, [user, userLoading, accessToken, authLoading, mode])

  const handleCodeSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    setError(null)

    try {
      // Validate code format (basic check)
      if (clinicCode.trim().length < 4) {
        setError("Veuillez saisir un code de clinique valide")
        setIsLoading(false)
        return
      }

      // Show wizard to collect role and personal info
      setShowWizard(true)
      setIsLoading(false)
    } catch (err: any) {
      setError(err.message || "Échec de la validation du code de clinique. Veuillez réessayer.")
      setIsLoading(false)
    }
  }

  const handleWizardComplete = () => {
    // Wizard will handle the join API call and redirect
    // This is just a callback in case we need it
  }

  if (userLoading || authLoading || isChecking) {
    return (
      <div className="min-h-dvh bg-background flex items-center justify-start p-6">
        <div className="mx-auto text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
          <p className="text-muted-foreground">Vérification du statut de votre clinique…</p>
        </div>
      </div>
    )
  }

  // Self-registration is closed on this deployment — say what to do instead rather than offering a form the
  // server will refuse (§ 0: never remove a capability silently).
  if (selfRegistrationClosed) {
    return <JoinUnavailable />
  }

  // Show wizard if code is entered and validated
  if (showWizard) {
    return <JoinWizard clinicCode={clinicCode.trim()} onComplete={handleWizardComplete} />
  }

  return (
    // `justify-start` + `mx-auto` for the reason the label comment further down already documents: a centring parent
    // splits a flex item's overflow to BOTH sides, and the inline-start half is outside the scrollable region.
    // Letting that long label wrap fixed the trigger; this fixes the structure, so the next long string cannot
    // re-create it.
    <div className="min-h-dvh bg-background flex items-center justify-start p-6">
      <div className="mx-auto w-full max-w-md">
        <Card className="border-primary/20 shadow-lg">
          <CardHeader className="text-center space-y-4">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-accent/20 mx-auto">
              <Building2 className="w-8 h-8 text-primary" />
            </div>
            <div>
              <CardTitle className="text-2xl text-accent-foreground">Rejoindre une clinique</CardTitle>
              <CardDescription className="mt-2">
                Saisissez le code de la clinique fourni par votre administrateur pour rejoindre une clinique existante
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleCodeSubmit} className="space-y-6">
              {error && (
                <div className="p-4 bg-destructive/10 border border-destructive/20 rounded-lg flex items-start gap-3">
                  <AlertCircle className="w-5 h-5 text-destructive shrink-0 mt-0.5" />
                  <p className="text-sm text-destructive">{error}</p>
                </div>
              )}

              <div className="space-y-2">
                <Label htmlFor="clinic-code" className="text-sm font-medium">
                  Code de la clinique <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="clinic-code"
                  placeholder="Saisir le code de la clinique"
                  value={clinicCode}
                  onChange={(e) => setClinicCode(e.target.value.toUpperCase())}
                  required
                  disabled={isLoading}
                  className="text-center text-lg font-mono tracking-widest"
                  maxLength={10}
                />
                <p className="text-xs text-muted-foreground">
                  Le code de la clinique comporte généralement 6 à 10 caractères
                </p>
              </div>

              <Button
                type="submit"
                className="w-full bg-primary hover:bg-primary/90"
                disabled={!clinicCode.trim() || isLoading}
              >
                {isLoading ? "Validation…" : "Continuer"}
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>

              <div className="text-center">
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => router.push("/setup")}
                  /*
                   * `h-auto whitespace-normal` — this 58-character label pushed the whole page off-canvas.
                   *
                   * `buttonVariants` carries `whitespace-nowrap` and `shrink-0`, so the label was one
                   * unbreakable ~440px line. Its ancestor card is `w-full max-w-md` inside a
                   * `flex items-center justify-center` column whose `min-width: auto` floor beats `max-w-md`,
                   * so the card was forced to ~440px inside a 342px content box — and because the parent
                   * centres, the overflow split to BOTH sides, putting ~24px of the card's left edge off-screen
                   * where no scroll can reach it. Letting the label wrap is the whole fix.
                   */
                  className="h-auto whitespace-normal py-2 text-center text-muted-foreground hover:text-primary"
                >
                  Vous n&apos;avez pas de code ? Créez plutôt une nouvelle clinique
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

