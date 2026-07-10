"use client"

import type React from "react"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Building2, ChevronRight, CheckCircle2, AlertCircle } from "lucide-react"
import { clinicsApi, type JoinClinicRequest } from "@/lib/api/clinics"
import { useSession } from "@/lib/auth/session"

const specialties = [
  "Dentist",
  "Orthodontist",
  "Prosthodontist",
  "Endodontist",
  "Periodontist",
  "Oral Surgeon",
  "Pediatric Dentist",
]

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

  const isStep1Valid = () => {
    const roleOk = role === "doctor" || role === "secretary"
    if (isLocalMode) {
      // Local self-registration collects the account (name + email + password) in step 1.
      return (
        roleOk &&
        regFullName.trim() !== "" &&
        /\S+@\S+\.\S+/.test(regEmail) &&
        regPassword.length >= 8 &&
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
        window.location.href = "/login"
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
    } catch (err: any) {
      setError(err.message || "Failed to join clinic. Please try again.")
      console.error("Error joining clinic:", err)
      setIsLoading(false)
    }
  }

  // Calculate steps based on current role
  const steps = role === "secretary" 
    ? [{ number: 1, title: "Role Selection", description: "Choose your role" }]
    : [
        { number: 1, title: "Role Selection", description: "Choose your role" },
        { number: 2, title: "Personal Info", description: "Your information" },
      ]

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 via-white to-slate-50 dark:from-slate-950 dark:to-slate-900 flex items-center justify-center p-6">
      <div className="w-full max-w-2xl">
        {/* Header */}
        <div className="text-center space-y-3 mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-blue-100 dark:bg-blue-900/20 mb-2">
            <Building2 className="w-8 h-8 text-blue-600 dark:text-blue-400" />
          </div>
          <h1 className="text-3xl font-bold text-blue-900 dark:text-blue-100">Join Clinic</h1>
          <p className="text-muted-foreground">Complete your profile to access the clinic</p>
        </div>

        {/* Error Message */}
        {error && (
          <div className="mb-4 p-4 bg-destructive/10 border border-destructive/20 rounded-lg flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-destructive shrink-0 mt-0.5" />
            <p className="text-sm text-destructive">{error}</p>
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
            {/* Step 1: Role Selection */}
            {currentStep === 1 && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-2xl font-semibold text-blue-900 dark:text-blue-100 mb-2">Select Your Role</h2>
                  <p className="text-muted-foreground">Choose the role that best describes your position</p>
                </div>

                <div className="space-y-4">
                  {isLocalMode && (
                    <div className="space-y-4 pb-4 border-b">
                      <div className="space-y-2">
                        <Label htmlFor="reg-full-name" className="text-sm font-medium">
                          Full Name <span className="text-destructive">*</span>
                        </Label>
                        <Input
                          id="reg-full-name"
                          placeholder="Your full name"
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
                          placeholder="you@clinic.com"
                          value={regEmail}
                          onChange={(e) => setRegEmail(e.target.value)}
                          required
                        />
                      </div>
                      <div className="grid md:grid-cols-2 gap-4">
                        <div className="space-y-2">
                          <Label htmlFor="reg-password" className="text-sm font-medium">
                            Password <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="reg-password"
                            type="password"
                            placeholder="At least 8 characters"
                            value={regPassword}
                            onChange={(e) => setRegPassword(e.target.value)}
                            required
                          />
                        </div>
                        <div className="space-y-2">
                          <Label htmlFor="reg-password-confirm" className="text-sm font-medium">
                            Confirm Password <span className="text-destructive">*</span>
                          </Label>
                          <Input
                            id="reg-password-confirm"
                            type="password"
                            placeholder="Re-enter password"
                            value={regPasswordConfirm}
                            onChange={(e) => setRegPasswordConfirm(e.target.value)}
                            required
                          />
                        </div>
                      </div>
                      {regPassword.length > 0 && regPassword.length < 8 && (
                        <p className="text-xs text-destructive">Password must be at least 8 characters.</p>
                      )}
                      {regPasswordConfirm.length > 0 && regPassword !== regPasswordConfirm && (
                        <p className="text-xs text-destructive">Passwords do not match.</p>
                      )}
                    </div>
                  )}
                  <div className="space-y-2">
                    <Label htmlFor="role" className="text-sm font-medium">
                      Your Role <span className="text-destructive">*</span>
                    </Label>
                    <Select value={role} onValueChange={(value: "doctor" | "secretary") => setRole(value)}>
                      <SelectTrigger id="role" className="h-12">
                        <SelectValue placeholder="Select your role" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="doctor">Doctor</SelectItem>
                        <SelectItem value="secretary">Secretary / Assistant</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {role === "secretary" && (
                    <div className="p-4 bg-blue-50 dark:bg-blue-950/20 rounded-lg border border-blue-200 dark:border-blue-800">
                      <p className="text-sm text-blue-700 dark:text-blue-300">
                        As a secretary/assistant, you don't need to provide additional personal information. 
                        Your email from your account will be used.
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
                  <h2 className="text-2xl font-semibold text-blue-900 dark:text-blue-100 mb-2">Your Information</h2>
                  <p className="text-muted-foreground">Tell us about yourself</p>
                </div>

                {role === "doctor" ? (
                  <div className="space-y-4">
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
                ) : (
                  <div className="p-4 bg-green-50 dark:bg-green-950/20 rounded-lg border border-green-200 dark:border-green-800">
                    <p className="text-sm text-green-700 dark:text-green-300">
                      You're all set! As a secretary/assistant, no additional information is needed. 
                      Click "Complete" to join the clinic.
                    </p>
                  </div>
                )}
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
                Previous
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
                  className="bg-blue-600 hover:bg-blue-700"
                >
                  {role === "secretary" && currentStep === 1 ? "Complete" : "Next"}
                  <ChevronRight className="w-4 h-4 ml-2" />
                </Button>
              ) : (
                <Button onClick={handleComplete} disabled={isLoading || !isStep2Valid()} className="bg-green-600 hover:bg-green-700">
                  {isLoading ? (
                    "Joining..."
                  ) : (
                    <>
                      <CheckCircle2 className="w-4 h-4 mr-2" />
                      Complete
                    </>
                  )}
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

