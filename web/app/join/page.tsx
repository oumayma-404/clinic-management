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
    checkUserStatus()
  }, [user, userLoading, accessToken, authLoading])

  const checkUserStatus = async () => {
    // Wait for auth to load
    if (userLoading || authLoading) {
      return
    }

    // If not authenticated, redirect to the mode-appropriate login.
    if (!user || !accessToken) {
      window.location.href = mode === "local" ? "/login?returnTo=/join" : "/auth/login?returnTo=/join"
      return
    }

    try {
      const status = await clinicsApi.getUserStatus()
      if (status.hasClinic) {
        // User has clinic, redirect to app
        window.location.href = "/"
        return
      }
      // User doesn't have clinic, show join form
      setIsChecking(false)
    } catch (err) {
      console.error("Error checking user status:", err)
      setIsChecking(false)
    }
  }

  const handleCodeSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    setError(null)

    try {
      // Validate code format (basic check)
      if (clinicCode.trim().length < 4) {
        setError("Please enter a valid clinic code")
        setIsLoading(false)
        return
      }

      // Show wizard to collect role and personal info
      setShowWizard(true)
      setIsLoading(false)
    } catch (err: any) {
      setError(err.message || "Failed to validate clinic code. Please try again.")
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
              <CardTitle className="text-2xl text-blue-900 dark:text-blue-100">Join a Clinic</CardTitle>
              <CardDescription className="mt-2">
                Enter the clinic code provided by your administrator to join an existing clinic
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
                  Clinic Code <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="clinic-code"
                  placeholder="Enter clinic code"
                  value={clinicCode}
                  onChange={(e) => setClinicCode(e.target.value.toUpperCase())}
                  required
                  disabled={isLoading}
                  className="text-center text-lg font-mono tracking-widest"
                  maxLength={10}
                />
                <p className="text-xs text-muted-foreground">
                  The clinic code is usually 6-10 characters long
                </p>
              </div>

              <Button
                type="submit"
                className="w-full bg-blue-600 hover:bg-blue-700"
                disabled={!clinicCode.trim() || isLoading}
              >
                {isLoading ? "Validating..." : "Continue"}
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>

              <div className="text-center">
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => router.push("/setup")}
                  className="text-muted-foreground hover:text-blue-600"
                >
                  Don't have a code? Create a new clinic instead
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

