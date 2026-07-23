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
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import JoinWizard from "@/components/join-wizard"

export default function JoinClinicPage() {
  const router = useRouter()
  const { user, isLoading: userLoading, mode } = useSession()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const [clinicCode, setClinicCode] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isChecking, setIsChecking] = useState(true)
  const [showWizard, setShowWizard] = useState(false)

  useEffect(() => {
    let cancelled = false

    const checkUserStatus = async () => {
      // Local self-registration is open (no session yet) — the clinic code is the gate.
      if (mode === "local") {
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
      <div className="min-h-screen bg-gradient-to-br from-blue-50 via-white to-slate-50 dark:from-slate-950 dark:to-slate-900 flex items-center justify-center p-6">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
          <p className="text-muted-foreground">Checking your clinic status...</p>
        </div>
      </div>
    )
  }

  // Show wizard if code is entered and validated
  if (showWizard) {
    return <JoinWizard clinicCode={clinicCode.trim()} onComplete={handleWizardComplete} />
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 via-white to-slate-50 dark:from-slate-950 dark:to-slate-900 flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        <Card className="border-blue-100 shadow-lg">
          <CardHeader className="text-center space-y-4">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-blue-100 dark:bg-blue-900/20 mx-auto">
              <Building2 className="w-8 h-8 text-blue-600 dark:text-blue-400" />
            </div>
            <div>
              <CardTitle className="text-2xl text-blue-900 dark:text-blue-100">Rejoindre une clinique</CardTitle>
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
                className="w-full bg-blue-600 hover:bg-blue-700"
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
                  className="text-muted-foreground hover:text-blue-600"
                >
                  Vous n'avez pas de code ? Créez plutôt une nouvelle clinique
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

