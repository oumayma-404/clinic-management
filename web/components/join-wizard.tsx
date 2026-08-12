"use client"

import type React from "react"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Building2, ChevronRight, CheckCircle2 } from "lucide-react"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { clinicsApi, type JoinClinicRequest } from "@/lib/api/clinics"
import { ApiError } from "@/lib/api/client"
import { useSession } from "@/lib/auth/session"
import { getErrorMessage } from "@/lib/errors"
import JoinUnavailable from "@/components/join-unavailable"
import { usePasswordMinLength } from "@/lib/hooks/use-password-policy"

import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"

interface JoinWizardProps {
  clinicCode: string
  onComplete: () => void
}

export default function JoinWizard({ clinicCode, onComplete }: JoinWizardProps) {
  const [currentStep, setCurrentStep] = useState(1)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { mode } = useSession()
  const isLocalMode = mode === "local"

  /**
   * I5: a self-registration no longer produces an account that can log in — it produces one **pending an
   * admin's approval**. So the flow cannot end by sending the person to `/login`, where they would type
   * credentials that work, be refused, and reasonably conclude the registration failed. It ends here, saying
   * what happened and what has to happen next.
   */
  const [registered, setRegistered] = useState(false)

  // US-3: this deployment has no self-registration at all. Distinct from `error` — nothing the person typed is
  // wrong, so a banner above a form they should retry would be the wrong shape.
  const [selfRegistrationClosed, setSelfRegistrationClosed] = useState(false)

  // Local (offline) self-registration account fields.
  const [regFullName, setRegFullName] = useState("")
  const [regEmail, setRegEmail] = useState("")
  const [regPassword, setRegPassword] = useState("")
  const [regPasswordConfirm, setRegPasswordConfirm] = useState("")

  // Role and Personal Info State
  const [role, setRole] = useState<"doctor" | "secretary">("doctor")
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [specialty, setSpecialty] = useState("")
  const [personalPhone, setPersonalPhone] = useState("")
  // The server's floor, never a literal here. `null` = not known yet, in which case nothing is pre-checked.
  const minLength = usePasswordMinLength()

  const isStep1Valid = () => {
    const roleOk = role === "doctor" || role === "secretary"
    if (isLocalMode) {
      // Local self-registration collects the account (name + email + password) in step 1.
      return (
        roleOk &&
        regFullName.trim() !== "" &&
        /\S+@\S+\.\S+/.test(regEmail) &&
        // An unknown floor (the probe has not answered, or failed) does not block the step: the server refuses a
        // short password with its own sentence, and stalling « Continuer » on a metadata read would remove a
        // working capability over a network hiccup.
        (minLength === null || regPassword.length >= minLength) &&
        regPassword === regPasswordConfirm
      )
    }
    return roleOk
  }

  const isStep2Valid = () => {
    if (role === "secretary") {
      return true // Secretary doesn't need personal info
    }
    // Doctor needs firstName, lastName, and specialty
    return firstName.trim() !== "" && lastName.trim() !== "" && specialty !== ""
  }

  const handleComplete = async () => {
    setIsLoading(true)
    setError(null)

    try {
      const doctorInfo = role === "doctor" ? {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        specialty: specialty.trim(),
        phone: personalPhone.trim() || undefined,
      } : undefined

      // Local (offline) self-registration: create the account, then go to the login screen.
      if (isLocalMode) {
        await clinicsApi.register({
          code: clinicCode,
          email: regEmail.trim(),
          password: regPassword,
          fullName: regFullName.trim(),
          role,
          doctorInfo,
        })
        // Not a redirect to /login: the account exists but is inactive until an admin activates it (I5).
        setRegistered(true)
        setIsLoading(false)
        return
      }

      const joinData: JoinClinicRequest = {
        code: clinicCode,
        role: role,
        doctorInfo,
      }

      await clinicsApi.join(joinData)

      // Redirect to app after successful join
      window.location.href = "/"
    } catch (err) {
      // US-3: `register` 404s where self-registration is closed. The page probes for that and normally never
      // renders this wizard there — this is the backstop for the one case it cannot cover, a probe that failed
      // on a deployment that really has closed it. An « introuvable » toast would blame the clinic code.
      if (isLocalMode && err instanceof ApiError && err.status === 404) {
        setSelfRegistrationClosed(true)
        setIsLoading(false)
        return
      }
      // Single formatting point (lib/errors) — same reason as the setup wizard.
      setError(getErrorMessage(err, "Échec de l'adhésion à la clinique. Veuillez réessayer."))
      console.error("Error joining clinic:", err)
      setIsLoading(false)
    }
  }

  // Calculate steps based on current role
  const steps = role === "secretary"
    ? [{ number: 1, title: "Choix du rôle", description: "Choisissez votre rôle" }]
    : [
        { number: 1, title: "Choix du rôle", description: "Choisissez votre rôle" },
        { number: 2, title: "Infos personnelles", description: "Vos informations" },
      ]

  /*
   * I5 — the terminal state of a Local self-registration.
   *
   * It replaces `window.location.href = "/login"`. Sending someone to a login form they cannot yet pass is the
   * worst available ending: their password is correct, so the refusal reads as the product being broken, and the
   * one fact that would explain it — an admin has to let them in — is never stated. Nothing here is actionable
   * by them, which is exactly what it has to say.
   */
  if (selfRegistrationClosed) {
    return <JoinUnavailable />
  }

  if (registered) {
    return (
      <div className="min-h-dvh bg-background flex items-center justify-center p-6">
        <div className="w-full max-w-lg space-y-6 text-center">
          <div className="inline-flex size-16 items-center justify-center rounded-full bg-success-wash">
            <CheckCircle2 className="size-8 text-success" />
          </div>
          <div className="space-y-3">
            <h1 className="text-2xl font-bold text-accent-foreground sm:text-3xl">Demande envoyée</h1>
            <p className="text-muted-foreground">
              Votre compte a bien été créé pour{" "}
              <span className="font-semibold text-foreground">{regEmail.trim()}</span>.
            </p>
          </div>
          <div
            role="status"
            className="rounded-lg border border-warning/30 bg-warning-wash px-4 py-3 text-start text-sm text-warning-ink"
          >
            Un administrateur du cabinet doit activer votre accès avant votre première connexion. Prévenez-le, puis
            revenez vous connecter — vos identifiants sont déjà enregistrés, il n&apos;y a rien d&apos;autre à
            remplir.
          </div>
          {/* A link, not an auto-redirect: leaving is the person's decision, and the message above is the point
              of this screen. `min-h-11` for the coarse-pointer floor — this is the only control here. */}
          <Button variant="outline" className="min-h-11 w-full" onClick={() => (window.location.href = "/login")}>
            Aller à la page de connexion
          </Button>
        </div>
      </div>
    )
  }

  return (
    /* `bg-background`, not a gradient with a hand-written `dark:` pair — see the note in `setup-wizard.tsx`;
       this is the same screen for the second and every later member of a clinic. */
    <div className="min-h-dvh bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-2xl">
        {/* Header */}
        <div className="text-center space-y-3 mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-accent/20 mb-2">
            <Building2 className="w-8 h-8 text-primary" />
          </div>
          <h1 className="text-3xl font-bold text-accent-foreground">Rejoindre une clinique</h1>
          <p className="text-muted-foreground">Complétez votre profil pour accéder à la clinique</p>
        </div>

        {/* Progress Steps */}
        <div className="flex items-center justify-center gap-4 mb-8">
          {steps.map((step, index) => (
            <div key={step.number} className="flex items-center">
              <div className="flex flex-col items-center">
                <div
                  className={`w-12 h-12 rounded-full flex items-center justify-center border-2 transition-all ${
 currentStep > step.number
                      ? "bg-primary border-primary text-primary-foreground"
                      : currentStep === step.number
                        ? "bg-accent border-primary text-primary ring-4 ring-primary/20"
                        : "bg-card border-border text-muted-foreground"
                  }`}
                >
                  {currentStep > step.number ? <CheckCircle2 className="w-6 h-6" /> : step.number}
                </div>
                <div className="mt-2 text-center">
                  <p
                    className={`text-sm font-medium ${currentStep >= step.number ? "text-accent-foreground" : "text-muted-foreground"}`}
                  >
                    {step.title}
                  </p>
                  <p className="text-xs text-muted-foreground">{step.description}</p>
                </div>
              </div>
              {index < steps.length - 1 && (
                <div className={`w-16 h-0.5 mb-12 mx-2 ${currentStep > step.number ? "bg-primary" : "bg-border"}`} />
              )}
            </div>
          ))}
        </div>

        {/* Step Content */}
        <Card className="border-primary/20 shadow-lg">
          <CardContent className="p-8">
            {/* Step 1: Role Selection */}
            {currentStep === 1 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-accent-foreground mb-2">Choisissez votre rôle</h2>
                  <p className="text-muted-foreground">Sélectionnez le rôle qui décrit le mieux votre fonction</p>
                </div>

                <div className="space-y-4">
                  {isLocalMode && (
                    <div className="space-y-4 pb-4 border-b">
                      <div className="space-y-2">
                        <Label htmlFor="reg-full-name" className="text-sm font-medium">
                          Nom complet <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="reg-full-name"
                          placeholder="Votre nom complet"
                          value={regFullName}
                          onChange={(e) => setRegFullName(e.target.value)}
                          required
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="reg-email" className="text-sm font-medium">
                          Email <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="reg-email"
                          type="email"
                          placeholder="vous@clinique.com"
                          value={regEmail}
                          onChange={(e) => setRegEmail(e.target.value)}
                          required
                        />
                      </div>
                      <div className="grid md:grid-cols-2 gap-4">
                        <div className="space-y-2">
                          <Label htmlFor="reg-password" className="text-sm font-medium">
                            Mot de passe <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="reg-password"
                            type="password"
                            // The floor was hardcoded here too, so the placeholder went on promising 8 while the
                            // server refused at 12. Falls back to a sentence with no number when it is unknown.
                            placeholder={
                              minLength !== null ? `Au moins ${minLength} caractères` : "Choisissez un mot de passe"
                            }
                            value={regPassword}
                            onChange={(e) => setRegPassword(e.target.value)}
                            required
                          />
                        </div>
                        <div className="space-y-2">
                          <Label htmlFor="reg-password-confirm" className="text-sm font-medium">
                            Confirmer le mot de passe <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="reg-password-confirm"
                            type="password"
                            placeholder="Ressaisir le mot de passe"
                            value={regPasswordConfirm}
                            onChange={(e) => setRegPasswordConfirm(e.target.value)}
                            required
                          />
                        </div>
                      </div>
                      {minLength !== null && regPassword.length > 0 && regPassword.length < minLength && (
                        <p className="text-xs text-destructive">
                          Le mot de passe doit contenir au moins {minLength} caractères.
                        </p>
                      )}
                      {regPasswordConfirm.length > 0 && regPassword !== regPasswordConfirm && (
                        <p className="text-xs text-destructive">Les mots de passe ne correspondent pas.</p>
                      )}
                    </div>
                  )}
                  <div className="space-y-2">
                    <Label htmlFor="role" className="text-sm font-medium">
                      Votre rôle <span className="text-destructive">*</span>
                    </Label>
                    <Select value={role} onValueChange={(value: "doctor" | "secretary") => setRole(value)}>
                      <SelectTrigger id="role" className="h-12">
                        <SelectValue placeholder="Sélectionnez votre rôle" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="doctor">Médecin</SelectItem>
                        <SelectItem value="secretary">Secrétaire / Assistant(e)</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {role === "secretary" && (
                    <div className="p-4 bg-accent/20 rounded-lg border border-primary/25">
                      <p className="text-sm text-primary">
                        En tant que secrétaire/assistant(e), vous n&apos;avez pas besoin de fournir d&apos;informations
                        personnelles supplémentaires. L&apos;email de votre compte sera utilisé.
                      </p>
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Step 2: Personal Info (only for doctors) */}
            {currentStep === 2 && role === "doctor" && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-accent-foreground mb-2">Vos informations</h2>
                  <p className="text-muted-foreground">Parlez-nous de vous</p>
                </div>

                {role === "doctor" ? (
                  <div className="space-y-4">
                    <div className="grid md:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label htmlFor="first-name" className="text-sm font-medium">
                          Prénom <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="first-name"
                          placeholder="Saisir votre prénom"
                          value={firstName}
                          onChange={(e) => setFirstName(e.target.value)}
                          required
                        />
                      </div>

                      <div className="space-y-2">
                        <Label htmlFor="last-name" className="text-sm font-medium">
                          Nom <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="last-name"
                          placeholder="Saisir votre nom"
                          value={lastName}
                          onChange={(e) => setLastName(e.target.value)}
                          required
                        />
                      </div>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="specialty" className="text-sm font-medium">
                        Spécialité <span className="text-destructive">*</span>
                      </Label>
                      <Select value={specialty} onValueChange={setSpecialty}>
                        <SelectTrigger id="specialty">
                          <SelectValue placeholder="Sélectionnez votre spécialité" />
                        </SelectTrigger>
                        <SelectContent>
                          {/* Value = the English storage key, label = French (AC-P2.42/2.43). */}
                          {DOCTOR_SPECIALTIES.map((spec) => (
                            <SelectItem key={spec} value={spec}>
                              {specialtyLabel(spec)}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="personal-phone" className="text-sm font-medium">
                        Numéro de téléphone (optionnel)
                      </Label>
                      <Input
                        id="personal-phone"
                        type="tel"
                        placeholder="+216 12 345 678"
                        value={personalPhone}
                        onChange={(e) => setPersonalPhone(e.target.value)}
                      />
                    </div>
                  </div>
                ) : (
                  <div className="p-4 bg-success-wash rounded-lg border border-success/25">
                    <p className="text-sm text-success">
                      Tout est prêt ! En tant que secrétaire/assistant(e), aucune information supplémentaire
                      n&apos;est nécessaire. Cliquez sur « Terminer » pour rejoindre la clinique.
                    </p>
                  </div>
                )}
              </div>
            )}

            {/* Navigation Buttons */}
            <div className="mt-8 pt-6 border-t">
              {/* Beside the button that produced it — see `setup-wizard.tsx` for the defect this replaced. */}
              <FormErrorBanner message={error} className="mb-4" />
              <div className="flex items-center justify-between">
                <Button
                  variant="outline"
                  onClick={() => setCurrentStep(currentStep - 1)}
                  disabled={currentStep === 1 || isLoading}
                  className="border-primary/25"
                >
                  Précédent
                </Button>

                {currentStep < steps.length ? (
                  <Button
                    onClick={() => {
                      if (role === "secretary" && currentStep === 1) {
                        // Complete immediately if secretary
                        handleComplete()
                      } else {
                        setCurrentStep(currentStep + 1)
                      }
                    }}
                    disabled={!isStep1Valid()}
                    className="bg-primary hover:bg-primary/90"
                  >
                    {role === "secretary" && currentStep === 1 ? "Terminer" : "Suivant"}
                    <ChevronRight className="w-4 h-4 ml-2" />
                  </Button>
                ) : (
                  /* The default (primary) fill — same reason as `setup-wizard.tsx`: there is no solid-success
                     token, and `bg-success` with white type fails contrast at its dark-mode step. */
                  <Button onClick={handleComplete} disabled={isLoading || !isStep2Valid()}>
                    {isLoading ? (
                      "Adhésion…"
                    ) : (
                      <>
                        <CheckCircle2 className="w-4 h-4 mr-2" />
                        Terminer
                      </>
                    )}
                  </Button>
                )}
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

