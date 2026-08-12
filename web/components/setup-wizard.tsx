"use client"

import type React from "react"

import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Building2, Plus, Trash2, Upload, X, ChevronRight, ChevronLeft, CheckCircle2, ArrowRight } from "lucide-react"
import Image from "next/image"
import { useRouter } from "next/navigation"
import { clinicsApi, type CreateClinicRequest } from "@/lib/api/clinics"
import { authApi } from "@/lib/api/auth"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { usePasswordMinLength } from "@/lib/hooks/use-password-policy"
import { useSession } from "@/lib/auth/session"
import { TUNISIAN_GOVERNORATES } from "@/lib/tunisia"
import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"
import { getErrorMessage } from "@/lib/errors"

const tunisianGovernorates = TUNISIAN_GOVERNORATES

const weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

// French display labels for the (English-keyed) weekdays — the keys stay English (used as state keys).
const weekdayLabelsFr: Record<string, string> = {
  Monday: "Lundi",
  Tuesday: "Mardi",
  Wednesday: "Mercredi",
  Thursday: "Jeudi",
  Friday: "Vendredi",
  Saturday: "Samedi",
  Sunday: "Dimanche",
}

interface Doctor {
  id: string
  name: string
  specialty: string
  phone?: string
  email?: string
}

interface WorkingHours {
  [key: string]: { from: string; to: string; enabled: boolean }
}

/**
 * Which door this wizard is serving. The **steps, the fields and the resulting clinic are identical** — both
 * end at the backend's one `LocalClinicProvisioning.ProvisionAsync` — and the only differences are the two a
 * public door forces:
 *
 * - `setup` — first-run bootstrap on an install that has no users yet, reached from the server's own machine.
 *   Provisions immediately, because being at the machine on a clinic-less install *is* the proof.
 * - `signup` — anyone, over the internet. There is no such proof, so the answers are held as a pending
 *   `ClinicSignup` and an emailed single-use token provisions them. It also drops the logo step: blobs are
 *   keyed `clinics/{clinicId}/…` and there is no clinic to own one yet.
 *
 * One component rather than two, deliberately: a second copy is how « what a new clinic is asked » grows two
 * answers, and the offline install is the copy nobody would remember to update.
 */
export type SetupWizardFlow = "setup" | "signup"

interface SetupWizardProps {
  onComplete: () => void
  flow?: SetupWizardFlow
}

/**
 * What AC-1.3 promises when the deployment has not said otherwise. It is a **fallback**, not the source: the real
 * figure is `Subscription:TrialDays`, served on `GET /api/auth/mode`. It exists so an older API — or a probe that
 * could not answer — still states something true of a default deployment rather than leaving the promise unmade.
 */
const DEFAULT_TRIAL_DAYS = 30

export default function SetupWizard({ onComplete, flow = "setup" }: SetupWizardProps) {
  const isSignup = flow === "signup"
  // The server's own neutral sentence, shown verbatim once a signup is accepted. Non-null IS the success state.
  const [signupAcknowledgement, setSignupAcknowledgement] = useState<string | null>(null)
  const router = useRouter()
  const [currentStep, setCurrentStep] = useState(1)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { accessToken } = useAuthToken()
  const { mode } = useSession()
  const isLocalMode = mode === "local"
  /*
   * Does step 2 collect an administrator account (name + e-mail + password)?
   *
   * ⚠️ **`isSignup` is ORed in deliberately, and it is what makes the password rule safe.** `isLocalMode` comes
   * from `useSession()`, whose context default is `mode: "cloud"` until `LocalSessionProvider` is in scope — so
   * on the public signup route, which always collects an admin account whatever the session says, keying the
   * rule on the deployment alone leaves a window where step 2 is validated against the Cloud branch and the
   * password is not checked at all. `flow` is a prop and is therefore correct on the very first render.
   */
  const collectsAdminAccount = isLocalMode || isSignup
  // The server's floor. `null` = not known yet or the probe failed, in which case nothing is pre-checked here.
  const minLength = usePasswordMinLength()

  /*
   * AC-1.3: the trial is stated **before the visitor submits anything**, so this is read on mount and shown in the
   * header — visible on all three steps rather than tucked beside the final button.
   *
   * `null` until the probe answers and where the deployment grants no trial; `/setup` (first-run on a clinic's own
   * PC) never asks, because nothing expires there and promising free days would be a sentence with no meaning.
   */
  const [trialDays, setTrialDays] = useState<number | null>(null)

  useEffect(() => {
    if (!isSignup) return
    let cancelled = false
    authApi
      .getMode()
      .then((m) => {
        if (cancelled || m.requiresSubscription !== true) return
        setTrialDays(m.trialDays ?? DEFAULT_TRIAL_DAYS)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [isSignup])

  // Local (offline) first-run: the first user is the clinic admin (email + password).
  const [adminFullName, setAdminFullName] = useState("")
  const [adminEmail, setAdminEmail] = useState("")
  const [adminPassword, setAdminPassword] = useState("")
  const [adminPasswordConfirm, setAdminPasswordConfirm] = useState("")
  // Single-dentist cabinet: the admin is usually also the practitioner. When on, a linked Doctor is created
  // (with the specialty below) so their cachet / CNOMDT ordre + "Mon profil" work. Off → admin-only account.
  const [adminIsPractitioner, setAdminIsPractitioner] = useState(true)
  const [adminSpecialty, setAdminSpecialty] = useState("")

  // Clinic Information State
  const [clinicName, setClinicName] = useState("")
  const [address, setAddress] = useState("")
  const [governorate, setGovernorate] = useState("")
  const [phone, setPhone] = useState("")
  const [email, setEmail] = useState("")
  const [logoPreview, setLogoPreview] = useState<string | null>(null)
  const [logoFile, setLogoFile] = useState<File | null>(null)

  // Role and Personal Info State
  const [role, setRole] = useState<"doctor" | "secretary">("doctor")
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [specialty, setSpecialty] = useState("")
  const [personalPhone, setPersonalPhone] = useState("")

  // Working Hours State
  const [workingHours, setWorkingHours] = useState<WorkingHours>(
    weekdays.reduce(
      (acc, day) => ({
        ...acc,
        [day]: { from: "09:00", to: "17:00", enabled: day !== "Sunday" },
      }),
      {},
    ),
  )

  // Additional Doctors State (optional, for adding other doctors)
  const [doctors, setDoctors] = useState<Doctor[]>([{ id: "1", name: "", specialty: "", phone: "", email: "" }])

  const handleLogoUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      setLogoFile(file)
      const reader = new FileReader()
      reader.onloadend = () => {
        setLogoPreview(reader.result as string)
      }
      reader.readAsDataURL(file)
    }
  }

  const addDoctor = () => {
    setDoctors([...doctors, { id: Date.now().toString(), name: "", specialty: "", phone: "", email: "" }])
  }

  const removeDoctor = (id: string) => {
    if (doctors.length > 1) {
      setDoctors(doctors.filter((d) => d.id !== id))
    }
  }

  const updateDoctor = (id: string, field: keyof Doctor, value: string) => {
    setDoctors(doctors.map((d) => (d.id === id ? { ...d, [field]: value } : d)))
  }

  const toggleWorkingDay = (day: string) => {
    setWorkingHours((prev) => ({
      ...prev,
      [day]: { ...prev[day], enabled: !prev[day].enabled },
    }))
  }

  const updateWorkingHours = (day: string, field: "from" | "to", value: string) => {
    setWorkingHours((prev) => ({
      ...prev,
      [day]: { ...prev[day], [field]: value },
    }))
  }

  const isStep1Valid = () => {
    // Local first-run: the admin account email (step 2) is the clinic's contact email, so a separate
    // clinic email isn't collected here (it would otherwise be dropped on submit).
    if (isLocalMode) {
      return clinicName.trim() !== "" && governorate !== "" && phone.trim() !== ""
    }
    return clinicName.trim() !== "" && governorate !== "" && phone.trim() !== "" && email.trim() !== ""
  }

  const isStep2Valid = () => {
    // ⚠️ Gated on « this step collects a password », never on the deployment. `useSession()` returns
    // `mode: "cloud"` until its provider is in scope, and this same component serves the **public signup**
    // flow — so keying the account rules on `isLocalMode` alone means that during that window step 2 falls
    // through to the Cloud branch below and the password is never checked at all. `isSignup` comes from a
    // prop, so it is right on the first render.
    if (collectsAdminAccount) {
      // Admin account (full name + email + a password meeting the served floor, confirmed). When the admin is
      // also the practitioner, a specialty is required (it seeds the linked Doctor record).
      return (
        adminFullName.trim() !== "" &&
        /\S+@\S+\.\S+/.test(adminEmail) &&
        // An unknown floor does not block « Continuer »: the server refuses a short password with its own
        // sentence, and stalling the wizard on a metadata read would remove a working capability.
        (minLength === null || adminPassword.length >= minLength) &&
        adminPassword === adminPasswordConfirm &&
        (!adminIsPractitioner || adminSpecialty !== "")
      )
    }
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
      // Combine address and governorate
      const fullAddress = address ? `${address}, ${governorate}` : governorate

      // Serialize the working hours collected in step 3 into the same JSON shape the settings page +
      // backend expect ([{ day, enabled, from, to }]) so onboarding no longer discards them (finding #16).
      const workingHoursJson = JSON.stringify(
        weekdays.map((day) => ({
          day,
          enabled: workingHours[day]?.enabled ?? false,
          from: workingHours[day]?.from ?? "09:00",
          to: workingHours[day]?.to ?? "17:00",
        })),
      )

      // The admin-is-also-the-practitioner block, shared by the signup and first-run branches below: both
      // collect it in step 2 and both hand it to the same `LocalClinicRequest.DoctorInfo`.
      const adminDoctorInfo = (() => {
        if (!adminIsPractitioner || !adminSpecialty) return undefined
        const parts = adminFullName.trim().split(/\s+/)
        const first = parts[0] || adminFullName.trim()
        return {
          firstName: first,
          lastName: parts.slice(1).join(" ") || first,
          specialty: adminSpecialty,
          phone: phone || undefined,
        }
      })()

      // Public signup: nothing is created here. The answers become one pending row and the emailed link
      // provisions them — so this branch ends on the server's sentence, never on a redirect into an app the
      // visitor cannot enter yet.
      if (isSignup) {
        const result = await authApi.signUp({
          clinicName,
          fullName: adminFullName.trim(),
          email: adminEmail.trim(),
          password: adminPassword,
          phone: phone || undefined,
          address: fullAddress || undefined,
          city: governorate || undefined,
          // ⚠️ Name + specialty only, no `phone`. The wizard's one phone field is the **clinic's**, and sending
          // it here would persist it on `Doctor` as the practitioner's own contact — a number the visitor never
          // typed as theirs. `ClinicSignUpRequest.doctorInfo` omits the field for exactly this reason.
          doctorInfo: adminDoctorInfo && {
            firstName: adminDoctorInfo.firstName,
            lastName: adminDoctorInfo.lastName,
            specialty: adminDoctorInfo.specialty,
          },
          workingHoursJson,
        })
        setSignupAcknowledgement(result.message)
        setIsLoading(false)
        return
      }

      // Local (offline) first-run: create clinic + admin, then go to the login screen.
      if (isLocalMode) {
        await clinicsApi.setup({
          clinicName: clinicName,
          email: adminEmail.trim(),
          password: adminPassword,
          fullName: adminFullName.trim(),
          phone: phone || undefined,
          address: fullAddress || undefined,
          city: governorate || undefined,
          // The same block signup sends — derived once above, so the two doors cannot disagree about when a
          // Doctor record is created for the admin.
          doctorInfo: adminDoctorInfo,
          workingHoursJson,
        })
        window.location.href = "/login"
        return
      }

      const clinicData: CreateClinicRequest & { logoFile?: File } = {
        name: clinicName,
        address: fullAddress,
        city: governorate || undefined,
        phone: phone,
        email: email,
        generateCode: true,
        role: role,
        doctorInfo: role === "doctor" ? {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          specialty: specialty.trim(),
          phone: personalPhone.trim() || undefined,
        } : undefined,
        logoFile: logoFile || undefined,
        workingHoursJson,
      }

      await clinicsApi.create(clinicData)

      // Redirect to app immediately after successful creation
      window.location.href = "/"
    } catch (err) {
      // Single formatting point (lib/errors): keeps the French fallback when the thrown value carries
      // no usable message, instead of rendering a bare `undefined`/transport string.
      setError(getErrorMessage(err, "Échec de la création de la clinique. Veuillez réessayer."))
      console.error("Error creating clinic:", err)
      setIsLoading(false)
    }
  }

  const steps = [
    { number: 1, title: "Clinique", description: "Informations de base" },
    { number: 2, title: "Votre rôle", description: "Rôle et infos personnelles" },
    { number: 3, title: "Horaires", description: "Définir les horaires (optionnel)" },
  ]

  return (
    /*
     * `bg-background`, not a two-stop gradient with a hand-written `dark:` pair. This is the very first screen
     * a clinic ever sees; the gradient it carried was the app's own theme being ignored on the one page that
     * sets the impression for everything after it.
     */
    signupAcknowledgement ? (
      /*
        The end of the public flow. It renders the server's sentence **verbatim** — that sentence is identical
        whether the address was free, already an account, or already had a pending signup, and rewording it here
        is how a page grows the « adresse déjà utilisée » distinction the API took care never to send.
        `role="status"` because it replaces the form the user just submitted.
      */
      <div className="min-h-dvh bg-background flex items-center justify-center p-6">
        <Card className="w-full max-w-lg border-primary/20">
          <CardContent className="p-8 text-center space-y-4">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-primary/10">
              <CheckCircle2 className="w-8 h-8 text-primary" />
            </div>
            <h1 className="text-2xl font-bold text-accent-foreground">Vérifiez votre boîte mail</h1>
            <p className="text-muted-foreground" role="status">{signupAcknowledgement}</p>
            <p className="text-sm text-muted-foreground">
              Votre cabinet sera créé une fois le lien ouvert. Pensez à regarder vos courriers indésirables.
            </p>
            <Button variant="outline" onClick={() => router.push("/login")} className="coarse:h-11">
              Aller à la connexion
            </Button>
          </CardContent>
        </Card>
      </div>
    ) : (
    <div className="min-h-dvh bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-4xl">
        {/* Header */}
        <div className="text-center space-y-3 mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-accent/20 mb-2">
            <Building2 className="w-8 h-8 text-primary" />
          </div>
          <h1 className="text-3xl font-bold text-accent-foreground">
            {isSignup ? "Créez le cabinet de votre clinique" : "Bienvenue dans la gestion de votre clinique"}
          </h1>
          <p className="text-muted-foreground">Configurons votre clinique en 3 étapes simples</p>
          {/* AC-1.3. `flex-wrap` + `[overflow-wrap:anywhere]` so it stays one readable line at 320 px instead of
              pushing the first field below a second scroll on the product's first screen. */}
          {trialDays !== null && (
            <p
              role="status"
              className="inline-flex max-w-full flex-wrap items-center justify-center gap-2 rounded-full bg-success-wash px-4 py-1.5 text-sm text-foreground"
            >
              <CheckCircle2 className="size-4 shrink-0 text-success" aria-hidden="true" />
              <span className="[overflow-wrap:anywhere]">
                {trialDays} jours d&apos;essai gratuit, sans carte bancaire.
              </span>
            </p>
          )}
          <div className="pt-2">
            {/*
              On the public door the way back is « I already have an account », not « join with a clinic code »:
              `AllowsPublicClinicSignup` and `AllowsSelfRegistration` are opposite questions and the hosted
              profile answers no to the second, so /join there renders « non disponible ».
            */}
            <Button
              variant="ghost"
              onClick={() => router.push(isSignup ? "/login" : "/join")}
              className="text-muted-foreground hover:text-primary"
            >
              {isSignup
                ? "Vous avez déjà un compte ? Se connecter"
                : "Vous avez déjà un code clinique ? Rejoindre une clinique"}
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
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
            {/* Step 1: Clinic Information */}
            {currentStep === 1 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-accent-foreground mb-2">Informations de la clinique</h2>
                  <p className="text-muted-foreground">Parlez-nous de votre clinique</p>
                </div>

                <div className="grid md:grid-cols-2 gap-6">
                  <div className="space-y-2">
                    <Label htmlFor="clinic-name" className="text-sm font-medium">
                      Nom de la clinique <span className="text-destructive">*</span>
                    </Label>
                    <Input
                      id="clinic-name"
                      placeholder="Saisir le nom de la clinique"
                      value={clinicName}
                      onChange={(e) => setClinicName(e.target.value)}
                      required
                    />
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="governorate" className="text-sm font-medium">
                      Ville / Gouvernorat <span className="text-destructive">*</span>
                    </Label>
                    <Select value={governorate} onValueChange={setGovernorate}>
                      <SelectTrigger id="governorate">
                        <SelectValue placeholder="Sélectionner le gouvernorat" />
                      </SelectTrigger>
                      <SelectContent>
                        {tunisianGovernorates.map((gov) => (
                          <SelectItem key={gov} value={gov}>
                            {gov}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="address" className="text-sm font-medium">
                    Adresse
                  </Label>
                  <Textarea
                    id="address"
                    placeholder="Saisir l'adresse complète"
                    value={address}
                    onChange={(e) => setAddress(e.target.value)}
                    rows={3}
                  />
                </div>

                <div className="grid md:grid-cols-2 gap-6">
                  <div className="space-y-2">
                    <Label htmlFor="phone" className="text-sm font-medium">
                      Numéro de téléphone <span className="text-destructive">*</span>
                    </Label>
                    <Input
                      id="phone"
                      type="tel"
                      placeholder="+216 12 345 678"
                      value={phone}
                      onChange={(e) => setPhone(e.target.value)}
                      required
                    />
                  </div>

                  {/* In Local mode the admin account email (step 2) doubles as the clinic contact,
                      so this separate field is hidden to avoid collecting data that is dropped. */}
                  {!isLocalMode && (
                    <div className="space-y-2">
                      <Label htmlFor="email" className="text-sm font-medium">
                        Email professionnel <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="email"
                        type="email"
                        placeholder="ex. : contact@maclinique.tn"
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        required
                      />
                    </div>
                  )}
                </div>

                {/*
                  No logo on the public door. Blobs are keyed `clinics/{clinicId}/…` and the clinic does not
                  exist until the emailed link is opened — often on a different device from the one that filled
                  this form, which is where a browser-held file would silently be lost. It is added in
                  « Paramètres » after the first login, where the upload already works. (§ 0: the capability is
                  not removed, it is moved to where it can succeed.)
                */}
                {!isSignup && (
                <div className="space-y-2">
                  <Label className="text-sm font-medium">Logo de la clinique (optionnel)</Label>
                  <div className="flex items-center gap-4">
                    {logoPreview ? (
                      <div className="relative w-24 h-24 rounded-lg border-2 border-primary/25 overflow-hidden">
                        <Image
                          src={logoPreview || "/placeholder.svg"}
                          alt="Aperçu du logo"
                          fill
                          className="object-cover"
                        />
                        {/*
                          `type="button"` because this sits in a form-shaped step and an untyped button
                          submits; `touch-target` because the painted control is 20×20 and it is the ONLY way
                          to undo a mis-picked logo; `aria-label` because the icon is its whole content.
                          `clinic-settings.tsx`'s equivalent already does all three — this one is the copy
                          that never got them.
                        */}
                        <button
                          type="button"
                          onClick={() => {
                            setLogoPreview(null)
                            setLogoFile(null)
                          }}
                          aria-label="Supprimer le logo"
                          className="touch-target absolute top-1 right-1 p-1 bg-destructive rounded-full text-destructive-foreground hover:bg-destructive/90"
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </div>
                    ) : (
                      <label className="w-24 h-24 flex flex-col items-center justify-center border-2 border-dashed border-primary/40 rounded-lg cursor-pointer hover:border-primary hover:bg-accent/20 transition-colors">
                        <Upload className="w-6 h-6 text-primary mb-1" />
                        <span className="text-xs text-muted-foreground">Téléverser</span>
                        <input type="file" accept="image/*" onChange={handleLogoUpload} className="hidden" />
                      </label>
                    )}
                  </div>
                </div>
                )}
              </div>
            )}

            {/* Step 2: Role Selection and Personal Info */}
            {currentStep === 2 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-accent-foreground mb-2">{isLocalMode ? "Compte administrateur" : "Votre rôle et vos informations"}</h2>
                  <p className="text-muted-foreground">{isLocalMode ? "Créez le compte administrateur de la clinique" : "Parlez-nous de vous"}</p>
                </div>

                {isLocalMode && (
                  <div className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="admin-full-name" className="text-sm font-medium">
                        Nom complet <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="admin-full-name"
                        placeholder="Dr Jean Dupont"
                        value={adminFullName}
                        onChange={(e) => setAdminFullName(e.target.value)}
                        required
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="admin-email" className="text-sm font-medium">
                        Email <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="admin-email"
                        type="email"
                        placeholder="admin@clinique.com"
                        value={adminEmail}
                        onChange={(e) => setAdminEmail(e.target.value)}
                        required
                      />
                    </div>
                    <div className="grid md:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label htmlFor="admin-password" className="text-sm font-medium">
                          Mot de passe <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="admin-password"
                          type="password"
                          // The floor was hardcoded here too, so the placeholder went on promising 8 while the
                          // server refused at 12. Falls back to a sentence with no number when it is unknown.
                          placeholder={
                            minLength !== null ? `Au moins ${minLength} caractères` : "Choisissez un mot de passe"
                          }
                          value={adminPassword}
                          onChange={(e) => setAdminPassword(e.target.value)}
                          required
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="admin-password-confirm" className="text-sm font-medium">
                          Confirmer le mot de passe <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="admin-password-confirm"
                          type="password"
                          placeholder="Ressaisir le mot de passe"
                          value={adminPasswordConfirm}
                          onChange={(e) => setAdminPasswordConfirm(e.target.value)}
                          required
                        />
                      </div>
                    </div>
                    {minLength !== null && adminPassword.length > 0 && adminPassword.length < minLength && (
                      <p className="text-xs text-destructive">
                        Le mot de passe doit contenir au moins {minLength} caractères.
                      </p>
                    )}
                    {adminPasswordConfirm.length > 0 && adminPassword !== adminPasswordConfirm && (
                      <p className="text-xs text-destructive">Les mots de passe ne correspondent pas.</p>
                    )}

                    {/* Single-dentist cabinet: the admin is usually the practitioner too. When enabled, a
                        linked Doctor record is created so their cachet / CNOMDT ordre + "Mon profil" work. */}
                    {/*
                      `ui/checkbox.tsx`, not a raw `<input type="checkbox">`. The coarse-pointer floor in
                      `globals.css` deliberately EXCLUDES `[type=checkbox]` because the primitive is supposed
                      to carry `touch-target` itself — so hand-rolling one gets neither, leaving a 16×16 tap
                      target. A `<Label htmlFor>` replaces the wrapping `<label>` for the same reason the rest
                      of this wizard uses one.
                    */}
                    <div className="pt-4 border-t space-y-4">
                      <div className="flex items-center gap-3">
                        <Checkbox
                          id="admin-is-practitioner"
                          checked={adminIsPractitioner}
                          onCheckedChange={(checked) => setAdminIsPractitioner(checked === true)}
                        />
                        <Label htmlFor="admin-is-practitioner" className="text-sm font-medium cursor-pointer">
                          Je suis aussi le praticien (dentiste) de ce cabinet
                        </Label>
                      </div>

                      {adminIsPractitioner && (
                        <div className="space-y-2">
                          <Label htmlFor="admin-specialty" className="text-sm font-medium">
                            Spécialité <span className="text-destructive">*</span>
                          </Label>
                          <Select value={adminSpecialty} onValueChange={setAdminSpecialty}>
                            <SelectTrigger id="admin-specialty">
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
                          <p className="text-xs text-muted-foreground">
                            Crée votre profil praticien (cachet, numéro d&apos;ordre) accessible via « Mon profil ».
                          </p>
                        </div>
                      )}
                    </div>
                  </div>
                )}

                {!isLocalMode && (
                <div className="space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor="role" className="text-sm font-medium">
                      Votre rôle <span className="text-destructive">*</span>
                    </Label>
                    <Select value={role} onValueChange={(value: "doctor" | "secretary") => setRole(value)}>
                      <SelectTrigger id="role">
                        <SelectValue placeholder="Sélectionnez votre rôle" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="doctor">Médecin</SelectItem>
                        <SelectItem value="secretary">Secrétaire / Assistant(e)</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {role === "doctor" && (
                    <div className="space-y-4 pt-4 border-t">
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
                  )}

                  {role === "secretary" && (
                    <div className="p-4 bg-accent/20 rounded-lg border border-primary/25">
                      <p className="text-sm text-primary">
                        En tant que secrétaire/assistant(e), vous n&apos;avez pas besoin de fournir d&apos;informations
                        personnelles supplémentaires. L&apos;email de votre compte sera utilisé.
                      </p>
                    </div>
                  )}
                </div>
                )}
              </div>
            )}

            {/* Step 3: Working Hours */}
            {currentStep === 3 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-accent-foreground mb-2">Définir les horaires</h2>
                  <p className="text-muted-foreground">Configurez les horaires d&apos;ouverture de votre clinique (optionnel)</p>
                </div>

                <div className="space-y-3">
                  {/*
                    `flex-wrap` + no fixed width on the time pair. The row is a `w-32` day column plus two
                    `w-36` fields with no wrap: on a 390px phone the content box is ~246px, so both fields
                    were squeezed to ~33px — of which `px-3` takes 24 — and the opening and closing hours
                    became unreadable on the LAST step of first-run setup, seven rows over. Wrapping lets the
                    pair drop onto its own full-width line instead of shrinking.

                    `bg-muted/40`: the class here was `bg-accent/30/10`, a double opacity modifier that
                    Tailwind does not parse — so the row has in fact been painting nothing at all.
                  */}
                  {weekdays.map((day) => (
                    <div key={day} className="flex flex-wrap items-center gap-4 p-4 rounded-lg bg-muted/40">
                      <div className="flex items-center gap-3 w-32">
                        <Checkbox
                          id={`day-${day}`}
                          checked={workingHours[day].enabled}
                          onCheckedChange={() => toggleWorkingDay(day)}
                        />
                        <Label htmlFor={`day-${day}`} className="text-sm font-medium cursor-pointer">
                          {weekdayLabelsFr[day] ?? day}
                        </Label>
                      </div>
                      {workingHours[day].enabled && (
                        <div className="flex w-full items-center gap-3 sm:w-auto sm:flex-1">
                          <Label htmlFor={`day-${day}-from`} className="sr-only">
                            {`Heure d'ouverture — ${weekdayLabelsFr[day] ?? day}`}
                          </Label>
                          <Input
                            id={`day-${day}-from`}
                            type="time"
                            value={workingHours[day].from}
                            onChange={(e) => updateWorkingHours(day, "from", e.target.value)}
                          />
                          <span className="text-muted-foreground text-sm">à</span>
                          <Label htmlFor={`day-${day}-to`} className="sr-only">
                            {`Heure de fermeture — ${weekdayLabelsFr[day] ?? day}`}
                          </Label>
                          <Input
                            id={`day-${day}-to`}
                            type="time"
                            value={workingHours[day].to}
                            onChange={(e) => updateWorkingHours(day, "to", e.target.value)}
                          />
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Navigation Buttons */}
            <div className="mt-8 pt-6 border-t">
              {/*
                The refusal belongs here, beside the button that produced it — it used to render at the top of
                the page, above the progress rail, where step 3's seven weekday rows push it off screen and a
                rejected « Terminer » reads as a dead button.
              */}
              <FormErrorBanner message={error} className="mb-4" />
              <div className="flex items-center justify-between">
                <Button
                  variant="outline"
                  onClick={() => setCurrentStep(currentStep - 1)}
                  disabled={currentStep === 1 || isLoading}
                  className="border-primary/25"
                >
                  <ChevronLeft className="w-4 h-4 mr-2" />
                  Précédent
                </Button>

                {currentStep < 3 ? (
                  <Button
                    onClick={() => setCurrentStep(currentStep + 1)}
                    disabled={
                      currentStep === 1 ? !isStep1Valid()
                      : currentStep === 2 ? !isStep2Valid()
                      : false
                    }
                    className="bg-primary hover:bg-primary/90"
                  >
                    Suivant
                    <ChevronRight className="w-4 h-4 ml-2" />
                  </Button>
                ) : (
                  /*
                    The default (primary) fill, not `bg-green-600`. There is deliberately no solid-success
                    token: `--success` is an INK meant for `--success-wash`, and at its dark-mode step
                    (L 0.70) white type on it measures ~2.6:1 — so "convert the green button to `bg-success`"
                    would have shipped an unreadable CTA. The completion signal is carried by the check icon
                    and « Terminer la configuration », which is where it belongs.
                  */
                  <Button onClick={handleComplete} disabled={isLoading}>
                    {isLoading ? (
                      isSignup ? "Envoi…" : "Création…"
                    ) : (
                      <>
                        <CheckCircle2 className="w-4 h-4 mr-2" />
                        {isSignup ? "Créer mon cabinet" : "Terminer la configuration"}
                      </>
                    )}
                  </Button>
                )}
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Skip Option */}
        {currentStep === 3 && (
          <div className="text-center mt-4">
            <Button variant="ghost" onClick={handleComplete} disabled={isLoading} className="text-muted-foreground hover:text-primary">
              Passer pour l&apos;instant
            </Button>
          </div>
        )}
      </div>
    </div>
    )
  )
}

