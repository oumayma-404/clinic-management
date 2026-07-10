"use client"

import type React from "react"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Building2, Plus, Trash2, Upload, X, ChevronRight, ChevronLeft, CheckCircle2, ArrowRight } from "lucide-react"
import Image from "next/image"
import { useRouter } from "next/navigation"
import { clinicsApi, type CreateClinicRequest } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"

const tunisianGovernorates = [
  "Tunis",
  "Ariana",
  "Ben Arous",
  "Manouba",
  "Nabeul",
  "Zaghouan",
  "Bizerte",
  "Béja",
  "Jendouba",
  "Kef",
  "Siliana",
  "Sousse",
  "Monastir",
  "Mahdia",
  "Sfax",
  "Kairouan",
  "Kasserine",
  "Sidi Bouzid",
  "Gabès",
  "Medenine",
  "Tataouine",
  "Gafsa",
  "Tozeur",
  "Kebili",
]

const specialties = [
  "Dentist",
  "Orthodontist",
  "Prosthodontist",
  "Endodontist",
  "Periodontist",
  "Oral Surgeon",
  "Pediatric Dentist",
]

const weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

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

interface SetupWizardProps {
  onComplete: () => void
}

export default function SetupWizard({ onComplete }: SetupWizardProps) {
  const router = useRouter()
  const [currentStep, setCurrentStep] = useState(1)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { accessToken } = useAuthToken()
  const { mode } = useSession()
  const isLocalMode = mode === "local"

  // Local (offline) first-run: the first user is the clinic admin (email + password).
  const [adminFullName, setAdminFullName] = useState("")
  const [adminEmail, setAdminEmail] = useState("")
  const [adminPassword, setAdminPassword] = useState("")
  const [adminPasswordConfirm, setAdminPasswordConfirm] = useState("")

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
    if (isLocalMode) {
      // Local first-run: admin account (full name + email + password ≥ 8, confirmed).
      return (
        adminFullName.trim() !== "" &&
        /\S+@\S+\.\S+/.test(adminEmail) &&
        adminPassword.length >= 8 &&
        adminPassword === adminPasswordConfirm
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

      // Local (offline) first-run: create clinic + admin, then go to the login screen.
      if (isLocalMode) {
        await clinicsApi.setup({
          clinicName: clinicName,
          email: adminEmail.trim(),
          password: adminPassword,
          fullName: adminFullName.trim(),
          phone: phone || undefined,
          address: fullAddress || undefined,
        })
        window.location.href = "/login"
        return
      }

      const clinicData: CreateClinicRequest & { logoFile?: File } = {
        name: clinicName,
        address: fullAddress,
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
      }

      await clinicsApi.create(clinicData)

      // Redirect to app immediately after successful creation
      window.location.href = "/"
    } catch (err: any) {
      setError(err.message || "Failed to create clinic. Please try again.")
      console.error("Error creating clinic:", err)
      setIsLoading(false)
    }
  }

  const steps = [
    { number: 1, title: "Clinic Info", description: "Basic details" },
    { number: 2, title: "Your Role", description: "Role & personal info" },
    { number: 3, title: "Working Hours", description: "Set schedule (optional)" },
  ]

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 via-white to-slate-50 dark:from-slate-950 dark:to-slate-900 flex items-center justify-center p-6">
      <div className="w-full max-w-4xl">
        {/* Header */}
        <div className="text-center space-y-3 mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-blue-100 dark:bg-blue-900/20 mb-2">
            <Building2 className="w-8 h-8 text-blue-600 dark:text-blue-400" />
          </div>
          <h1 className="text-3xl font-bold text-blue-900 dark:text-blue-100">Welcome to Your Clinic Management</h1>
          <p className="text-muted-foreground">Let's get your clinic set up in just 3 simple steps</p>
          <div className="pt-2">
            <Button
              variant="ghost"
              onClick={() => router.push("/join")}
              className="text-muted-foreground hover:text-blue-600"
            >
              Already have a clinic code? Join existing clinic
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
        </div>

        {/* Error Message */}
        {error && (
          <div className="mb-4 p-4 bg-destructive/10 border border-destructive/20 rounded-lg text-destructive text-sm">
            {error}
          </div>
        )}

        {/* Progress Steps */}
        <div className="flex items-center justify-center gap-4 mb-8">
          {steps.map((step, index) => (
            <div key={step.number} className="flex items-center">
              <div className="flex flex-col items-center">
                <div
                  className={`w-12 h-12 rounded-full flex items-center justify-center border-2 transition-all ${
                    currentStep > step.number
                      ? "bg-blue-600 border-blue-600 text-white"
                      : currentStep === step.number
                        ? "bg-blue-100 border-blue-600 text-blue-600 ring-4 ring-blue-100"
                        : "bg-white border-gray-300 text-gray-400"
                  }`}
                >
                  {currentStep > step.number ? <CheckCircle2 className="w-6 h-6" /> : step.number}
                </div>
                <div className="mt-2 text-center">
                  <p
                    className={`text-sm font-medium ${currentStep >= step.number ? "text-blue-900 dark:text-blue-100" : "text-gray-400"}`}
                  >
                    {step.title}
                  </p>
                  <p className="text-xs text-muted-foreground">{step.description}</p>
                </div>
              </div>
              {index < steps.length - 1 && (
                <div className={`w-16 h-0.5 mb-12 mx-2 ${currentStep > step.number ? "bg-blue-600" : "bg-gray-300"}`} />
              )}
            </div>
          ))}
        </div>

        {/* Step Content */}
        <Card className="border-blue-100 shadow-lg">
          <CardContent className="p-8">
            {/* Step 1: Clinic Information */}
            {currentStep === 1 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-blue-900 dark:text-blue-100 mb-2">Clinic Information</h2>
                  <p className="text-muted-foreground">Tell us about your clinic</p>
                </div>

                <div className="grid md:grid-cols-2 gap-6">
                  <div className="space-y-2">
                    <Label htmlFor="clinic-name" className="text-sm font-medium">
                      Clinic Name <span className="text-destructive">*</span>
                    </Label>
                    <Input
                      id="clinic-name"
                      placeholder="Enter clinic name"
                      value={clinicName}
                      onChange={(e) => setClinicName(e.target.value)}
                      required
                    />
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="governorate" className="text-sm font-medium">
                      City / Governorate <span className="text-destructive">*</span>
                    </Label>
                    <Select value={governorate} onValueChange={setGovernorate}>
                      <SelectTrigger id="governorate">
                        <SelectValue placeholder="Select governorate" />
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
                    Address
                  </Label>
                  <Textarea
                    id="address"
                    placeholder="Enter full address"
                    value={address}
                    onChange={(e) => setAddress(e.target.value)}
                    rows={3}
                  />
                </div>

                <div className="grid md:grid-cols-2 gap-6">
                  <div className="space-y-2">
                    <Label htmlFor="phone" className="text-sm font-medium">
                      Phone Number <span className="text-destructive">*</span>
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
                        Professional Email <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="email"
                        type="email"
                        placeholder="clinic@example.com"
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        required
                      />
                    </div>
                  )}
                </div>

                <div className="space-y-2">
                  <Label className="text-sm font-medium">Clinic Logo (Optional)</Label>
                  <div className="flex items-center gap-4">
                    {logoPreview ? (
                      <div className="relative w-24 h-24 rounded-lg border-2 border-blue-200 overflow-hidden">
                        <Image
                          src={logoPreview || "/placeholder.svg"}
                          alt="Logo preview"
                          fill
                          className="object-cover"
                        />
                        <button
                          onClick={() => {
                            setLogoPreview(null)
                            setLogoFile(null)
                          }}
                          className="absolute top-1 right-1 p-1 bg-destructive rounded-full text-white hover:bg-destructive/90"
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </div>
                    ) : (
                      <label className="w-24 h-24 flex flex-col items-center justify-center border-2 border-dashed border-blue-300 rounded-lg cursor-pointer hover:border-blue-500 hover:bg-blue-50 dark:hover:bg-blue-950/20 transition-colors">
                        <Upload className="w-6 h-6 text-blue-600 mb-1" />
                        <span className="text-xs text-muted-foreground">Upload</span>
                        <input type="file" accept="image/*" onChange={handleLogoUpload} className="hidden" />
                      </label>
                    )}
                  </div>
                </div>
              </div>
            )}

            {/* Step 2: Role Selection and Personal Info */}
            {currentStep === 2 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-blue-900 dark:text-blue-100 mb-2">{isLocalMode ? "Admin Account" : "Your Role & Information"}</h2>
                  <p className="text-muted-foreground">{isLocalMode ? "Create the clinic administrator account" : "Tell us about yourself"}</p>
                </div>

                {isLocalMode && (
                  <div className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="admin-full-name" className="text-sm font-medium">
                        Full Name <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="admin-full-name"
                        placeholder="Dr Jane Doe"
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
                        placeholder="admin@clinic.com"
                        value={adminEmail}
                        onChange={(e) => setAdminEmail(e.target.value)}
                        required
                      />
                    </div>
                    <div className="grid md:grid-cols-2 gap-4">
                      <div className="space-y-2">
                        <Label htmlFor="admin-password" className="text-sm font-medium">
                          Password <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="admin-password"
                          type="password"
                          placeholder="At least 8 characters"
                          value={adminPassword}
                          onChange={(e) => setAdminPassword(e.target.value)}
                          required
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="admin-password-confirm" className="text-sm font-medium">
                          Confirm Password <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="admin-password-confirm"
                          type="password"
                          placeholder="Re-enter password"
                          value={adminPasswordConfirm}
                          onChange={(e) => setAdminPasswordConfirm(e.target.value)}
                          required
                        />
                      </div>
                    </div>
                    {adminPassword.length > 0 && adminPassword.length < 8 && (
                      <p className="text-xs text-destructive">Password must be at least 8 characters.</p>
                    )}
                    {adminPasswordConfirm.length > 0 && adminPassword !== adminPasswordConfirm && (
                      <p className="text-xs text-destructive">Passwords do not match.</p>
                    )}
                  </div>
                )}

                {!isLocalMode && (
                <div className="space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor="role" className="text-sm font-medium">
                      Your Role <span className="text-destructive">*</span>
                    </Label>
                    <Select value={role} onValueChange={(value: "doctor" | "secretary") => setRole(value)}>
                      <SelectTrigger id="role">
                        <SelectValue placeholder="Select your role" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="doctor">Doctor</SelectItem>
                        <SelectItem value="secretary">Secretary / Assistant</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {role === "doctor" && (
                    <div className="space-y-4 pt-4 border-t">
                      <div className="grid md:grid-cols-2 gap-4">
                        <div className="space-y-2">
                          <Label htmlFor="first-name" className="text-sm font-medium">
                            First Name <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="first-name"
                            placeholder="Enter your first name"
                            value={firstName}
                            onChange={(e) => setFirstName(e.target.value)}
                            required
                          />
                        </div>

                        <div className="space-y-2">
                          <Label htmlFor="last-name" className="text-sm font-medium">
                            Last Name <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="last-name"
                            placeholder="Enter your last name"
                            value={lastName}
                            onChange={(e) => setLastName(e.target.value)}
                            required
                          />
                        </div>
                      </div>

                      <div className="space-y-2">
                        <Label htmlFor="specialty" className="text-sm font-medium">
                          Specialty <span className="text-destructive">*</span>
                        </Label>
                        <Select value={specialty} onValueChange={setSpecialty}>
                          <SelectTrigger id="specialty">
                            <SelectValue placeholder="Select your specialty" />
                          </SelectTrigger>
                          <SelectContent>
                            {specialties.map((spec) => (
                              <SelectItem key={spec} value={spec}>
                                {spec}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>

                      <div className="space-y-2">
                        <Label htmlFor="personal-phone" className="text-sm font-medium">
                          Phone Number (Optional)
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
                    <div className="p-4 bg-blue-50 dark:bg-blue-950/20 rounded-lg border border-blue-200 dark:border-blue-800">
                      <p className="text-sm text-blue-700 dark:text-blue-300">
                        As a secretary/assistant, you don't need to provide additional personal information. 
                        Your email from your account will be used.
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
                  <h2 className="text-2xl font-semibold text-blue-900 dark:text-blue-100 mb-2">Set Working Hours</h2>
                  <p className="text-muted-foreground">Configure your clinic's operating schedule (optional)</p>
                </div>

                <div className="space-y-3">
                  {weekdays.map((day) => (
                    <div key={day} className="flex items-center gap-4 p-4 rounded-lg bg-blue-50/30 dark:bg-blue-950/10">
                      <div className="flex items-center gap-3 w-32">
                        <input
                          type="checkbox"
                          id={`day-${day}`}
                          checked={workingHours[day].enabled}
                          onChange={() => toggleWorkingDay(day)}
                          className="w-4 h-4 rounded border-gray-300"
                        />
                        <Label htmlFor={`day-${day}`} className="text-sm font-medium cursor-pointer">
                          {day}
                        </Label>
                      </div>
                      {workingHours[day].enabled && (
                        <div className="flex items-center gap-3 flex-1">
                          <Input
                            type="time"
                            value={workingHours[day].from}
                            onChange={(e) => updateWorkingHours(day, "from", e.target.value)}
                            className="w-36"
                          />
                          <span className="text-muted-foreground text-sm">to</span>
                          <Input
                            type="time"
                            value={workingHours[day].to}
                            onChange={(e) => updateWorkingHours(day, "to", e.target.value)}
                            className="w-36"
                          />
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Navigation Buttons */}
            <div className="flex items-center justify-between mt-8 pt-6 border-t">
              <Button
                variant="outline"
                onClick={() => setCurrentStep(currentStep - 1)}
                disabled={currentStep === 1 || isLoading}
                className="border-blue-200"
              >
                <ChevronLeft className="w-4 h-4 mr-2" />
                Previous
              </Button>

              {currentStep < 3 ? (
                <Button
                  onClick={() => setCurrentStep(currentStep + 1)}
                  disabled={
                    currentStep === 1 ? !isStep1Valid() 
                    : currentStep === 2 ? !isStep2Valid() 
                    : false
                  }
                  className="bg-blue-600 hover:bg-blue-700"
                >
                  Next
                  <ChevronRight className="w-4 h-4 ml-2" />
                </Button>
              ) : (
                <Button onClick={handleComplete} disabled={isLoading} className="bg-green-600 hover:bg-green-700">
                  {isLoading ? (
                    "Creating..."
                  ) : (
                    <>
                      <CheckCircle2 className="w-4 h-4 mr-2" />
                      Complete Setup
                    </>
                  )}
                </Button>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Skip Option */}
        {currentStep === 3 && (
          <div className="text-center mt-4">
            <Button variant="ghost" onClick={handleComplete} disabled={isLoading} className="text-muted-foreground hover:text-blue-600">
              Skip for now
            </Button>
          </div>
        )}
      </div>
    </div>
  )
}

