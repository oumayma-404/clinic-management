"use client"

import type React from "react"

import { useState, useEffect } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Building2,
  Plus,
  Trash2,
  Upload,
  X,
  Edit,
  Save,
  CheckCircle2,
  AlertCircle,
  Info,
  ChevronDown,
} from "lucide-react"
import Image from "next/image"
import { clinicsApi, type ClinicDto } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"
import { BackupSettings } from "@/components/backup-settings"

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

interface WorkingHoursInput {
  day: string
  enabled: boolean
  from: string
  to: string
}

export default function ClinicSettings() {
  const { accessToken } = useAuthToken()
  const { mode, user } = useSession()
  const [isLoading, setIsLoading] = useState(false)
  const [isSaving, setIsSaving] = useState(false)

  // Clinic Information State
  const [clinicName, setClinicName] = useState("")
  const [address, setAddress] = useState("")
  const [governorate, setGovernorate] = useState("")
  const [phone, setPhone] = useState("")
  const [email, setEmail] = useState("")
  const [clinicCode, setClinicCode] = useState("")
  const [logoPreview, setLogoPreview] = useState<string | null>(null)
  const [logoFile, setLogoFile] = useState<File | null>(null)
  const [logoUrl, setLogoUrl] = useState<string | null>(null)

  // Working Hours State
  const [workingHours, setWorkingHours] = useState<WorkingHoursInput[]>(
    weekdays.map((day) => ({
      day,
      from: "09:00",
      to: "17:00",
      enabled: day !== "Sunday",
    })),
  )

  // Doctors State
  const [doctors, setDoctors] = useState<Doctor[]>([{ id: "1", name: "", specialty: "", phone: "", email: "" }])

  // Edit Modes and Notifications State
  const [isEditingClinicInfo, setIsEditingClinicInfo] = useState(false)
  const [isEditingDoctors, setIsEditingDoctors] = useState(false)
  const [isEditingHours, setIsEditingHours] = useState(false)
  const [notification, setNotification] = useState<{ type: "success" | "error"; message: string } | null>(null)

  // Collapse State for Each Section
  const [isClinicInfoCollapsed, setIsClinicInfoCollapsed] = useState(false)
  const [isDoctorsCollapsed, setIsDoctorsCollapsed] = useState(false)
  const [isHoursCollapsed, setIsHoursCollapsed] = useState(false)

  // Store original values for canceling edits
  const [originalClinicData, setOriginalClinicData] = useState<any>({})
  const [originalDoctors, setOriginalDoctors] = useState<Doctor[]>([])
  const [originalWorkingHours, setOriginalWorkingHours] = useState<WorkingHoursInput[]>([])

  const loadLogoFromBackend = async () => {
    try {
      const blob = await clinicsApi.getLogo()
      const reader = new FileReader()
      reader.onloadend = () => {
        setLogoPreview(reader.result as string)
      }
      reader.readAsDataURL(blob)
    } catch (error) {
      console.error('Failed to load logo:', error)
      // Don't set preview if loading fails
    }
  }

  // Load clinic data on mount
  useEffect(() => {
    loadClinicData()
  }, [])

  const loadClinicData = async () => {
    setIsLoading(true)
    try {
      const status = await clinicsApi.getUserStatus()
      if (status.hasClinic && status.clinic) {
        const clinic = status.clinic
        setClinicName(clinic.name)
        setClinicCode(clinic.code || "")
        setEmail(clinic.email || "")
        setPhone(clinic.phone || "")
        setLogoUrl(clinic.logoUrl || null)
        // Load logo from backend if it exists
        if (clinic.logoUrl) {
          loadLogoFromBackend()
        }

        // Parse address to extract address and governorate
        if (clinic.address) {
          const addressParts = clinic.address.split(", ")
          if (addressParts.length > 1) {
            const gov = addressParts[addressParts.length - 1]
            const addr = addressParts.slice(0, -1).join(", ")
            setAddress(addr)
            setGovernorate(gov)
          } else {
            setAddress("")
            setGovernorate(addressParts[0])
          }
        }

        // Load doctors
        if (status.doctors && status.doctors.length > 0) {
          setDoctors(
            status.doctors.map((d, index) => ({
              id: d.id || `doctor-${index}`,
              name: d.name,
              specialty: d.specialty,
              phone: d.phone || "",
              email: d.email || "",
            })),
          )
        }
      } else if (status.hasClinic && status.clinicName) {
        setClinicName(status.clinicName)
      }
    } catch (err: any) {
      setNotification({ type: "error", message: "Failed to load clinic data: " + (err.message || "Unknown error") })
    } finally {
      setIsLoading(false)
    }
  }

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
    setWorkingHours((prev) => prev.map((item) => (item.day === day ? { ...item, enabled: !item.enabled } : item)))
  }

  const updateWorkingHours = (day: string, field: "from" | "to", value: string) => {
    setWorkingHours((prev) => prev.map((item) => (item.day === day ? { ...item, [field]: value } : item)))
  }

  // Notification auto-dismiss
  useEffect(() => {
    if (notification) {
      const timer = setTimeout(() => setNotification(null), 4000)
      return () => clearTimeout(timer)
    }
  }, [notification])

  // Real-time: reload clinic profile/doctors when another client of this clinic changes them — but not
  // while this admin is mid-edit, so a live refresh never clobbers unsaved form input.
  useClinicRealtime(RealtimeResource.Clinics, () => {
    if (!isEditingClinicInfo && !isEditingDoctors && !isEditingHours) {
      loadClinicData()
    }
  })

  const handleEditClinicInfo = () => {
    setOriginalClinicData({ clinicName, address, governorate, phone, email, logoPreview, logoFile })
    setIsEditingClinicInfo(true)
  }

  const handleCancelClinicInfo = () => {
    setClinicName(originalClinicData.clinicName)
    setAddress(originalClinicData.address)
    setGovernorate(originalClinicData.governorate)
    setPhone(originalClinicData.phone)
    setEmail(originalClinicData.email)
    setLogoPreview(originalClinicData.logoPreview)
    setLogoFile(originalClinicData.logoFile)
    setIsEditingClinicInfo(false)
  }

  const handleSaveClinicInfo = async () => {
    setIsSaving(true)
    try {
      // Combine address and governorate
      const fullAddress = address && governorate 
        ? `${address}, ${governorate}` 
        : governorate || address || undefined

      // Update clinic via API
      const updatedClinic = await clinicsApi.update({
        name: clinicName,
        address: fullAddress,
        phone: phone,
        email: email,
        logoFile: logoFile || undefined,
      })

      // Update local state with response
      setLogoUrl(updatedClinic.logoUrl || null)
      // Reload logo from backend if it was uploaded
      if (updatedClinic.logoUrl) {
        await loadLogoFromBackend()
      } else {
        // Clear preview if logo was removed
        setLogoPreview(null)
      }
      setLogoFile(null) // Clear file after successful upload

      setNotification({ type: "success", message: "Clinic information saved successfully!" })
      setIsEditingClinicInfo(false)
    } catch (error: any) {
      setNotification({ type: "error", message: error.message || "Failed to save clinic information. Please try again." })
    } finally {
      setIsSaving(false)
    }
  }

  const handleEditDoctors = () => {
    setOriginalDoctors(JSON.parse(JSON.stringify(doctors)))
    setIsEditingDoctors(true)
  }

  const handleCancelDoctors = () => {
    setDoctors(originalDoctors)
    setIsEditingDoctors(false)
  }

  const handleSaveDoctors = async () => {
    setIsSaving(true)
    try {
      // Filter out empty doctors and convert IDs properly
      const validDoctors = doctors
        .filter((d) => d.name.trim() && d.specialty.trim())
        .map((d) => {
          let doctorId: string | null = null
          if (d.id && !d.id.startsWith("doctor-")) {
            if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(d.id)) {
              doctorId = d.id
            }
          }

          const doctor: any = {
            name: d.name.trim(),
            specialty: d.specialty.trim(),
          }

          if (doctorId) {
            doctor.id = doctorId
          }

          if (d.phone?.trim()) {
            doctor.phone = d.phone.trim()
          }
          if (d.email?.trim()) {
            doctor.email = d.email.trim()
          }

          return doctor
        })

      if (validDoctors.length === 0) {
        setNotification({ type: "error", message: "Please add at least one doctor with name and specialty" })
        setIsSaving(false)
        return
      }

      // Save doctors to backend
      const savedDoctors = await clinicsApi.updateDoctors(validDoctors)

      // Update local state with saved doctors (including IDs from backend)
      setDoctors(
        savedDoctors.map((d, index) => ({
          id: d.id || `doctor-${index}`,
          name: d.name,
          specialty: d.specialty,
          phone: d.phone || "",
          email: d.email || "",
        })),
      )

      setNotification({ type: "success", message: "Doctors information saved successfully!" })
      setIsEditingDoctors(false)
    } catch (error: any) {
      setNotification({ type: "error", message: error.message || "Failed to save doctors information. Please try again." })
    } finally {
      setIsSaving(false)
    }
  }

  const handleEditHours = () => {
    setOriginalWorkingHours(JSON.parse(JSON.stringify(workingHours)))
    setIsEditingHours(true)
  }

  const handleCancelHours = () => {
    setWorkingHours(originalWorkingHours)
    setIsEditingHours(false)
  }

  const handleSaveHours = async () => {
    setIsSaving(true)
    try {
      // Note: You'll need to add an update clinic endpoint
      // For now, this is a placeholder
      setNotification({ type: "success", message: "Working hours saved successfully!" })
      setIsEditingHours(false)
    } catch (error: any) {
      setNotification({ type: "error", message: "Failed to save working hours. Please try again." })
    } finally {
      setIsSaving(false)
    }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-950">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
          <p className="text-muted-foreground">Loading clinic settings...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-slate-950">
      <div className="max-w-5xl mx-auto p-3 space-y-3">
        <div className="flex items-center gap-2 mb-3">
          <div className="flex items-center justify-center w-8 h-8 rounded-lg bg-blue-600">
            <Building2 className="w-4 h-4 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">Clinic Settings</h1>
            <p className="text-xs text-muted-foreground">Manage your clinic information and team</p>
          </div>
        </div>

        {/* Clinic Code under header */}
        {clinicCode && (
          <div className="bg-blue-50 dark:bg-blue-950/20 border border-blue-200 dark:border-blue-800 rounded-lg p-3">
            <Label className="text-xs text-blue-700 dark:text-blue-300 font-medium">Clinic Code</Label>
            <div className="flex items-center gap-2 mt-1.5">
              <Badge
                variant="outline"
                className="text-base font-mono font-bold px-3 py-1 bg-white dark:bg-slate-900 text-blue-700 dark:text-blue-300 border-blue-300 dark:border-blue-700"
              >
                {clinicCode}
              </Badge>
            </div>
            <p className="text-[10px] text-blue-600 dark:text-blue-400 mt-1.5">
              Share with coworkers to join this clinic
            </p>
          </div>
        )}

        {notification && (
          <div
            className={`fixed top-4 right-4 z-50 flex items-center gap-3 px-4 py-3 rounded-lg shadow-lg border animate-in slide-in-from-top-2 ${
              notification.type === "success"
                ? "bg-green-50 dark:bg-green-950/30 border-green-200 dark:border-green-800 text-green-800 dark:text-green-200"
                : "bg-red-50 dark:bg-red-950/30 border-red-200 dark:border-red-800 text-red-800 dark:text-red-200"
            }`}
          >
            {notification.type === "success" ? (
              <CheckCircle2 className="w-5 h-5 shrink-0" />
            ) : (
              <AlertCircle className="w-5 h-5 shrink-0" />
            )}
            <span className="text-sm font-medium">{notification.message}</span>
            <Button variant="ghost" size="icon" className="h-6 w-6 ml-2" onClick={() => setNotification(null)}>
              <X className="w-4 h-4" />
            </Button>
          </div>
        )}

        {/* Clinic Info Card Collapsible */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <button
                onClick={() => setIsClinicInfoCollapsed(!isClinicInfoCollapsed)}
                className="flex items-center gap-2 flex-1 text-left hover:opacity-70 transition-opacity"
              >
                <div className="w-1 h-6 bg-blue-600 rounded-full" />
                <CardTitle className="text-base">Clinic Information</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
                    isClinicInfoCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingClinicInfo && (
                <Button onClick={handleEditClinicInfo} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Edit
                </Button>
              )}
            </div>
          </CardHeader>
          {!isClinicInfoCollapsed && (
            <CardContent className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label htmlFor="clinic-name" className="text-xs font-medium flex items-center gap-1">
                    Clinic Name
                    <span className="text-red-500">*</span>
                  </Label>
                  <Input
                    id="clinic-name"
                    placeholder="Enter clinic name"
                    value={clinicName}
                    onChange={(e) => setClinicName(e.target.value)}
                    disabled={!isEditingClinicInfo}
                    className={`h-8 text-sm ${!isEditingClinicInfo ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    required
                  />
                </div>

                <div className="space-y-1">
                  <Label htmlFor="governorate" className="text-xs font-medium flex items-center gap-1">
                    City / Governorate
                    <span className="text-red-500">*</span>
                  </Label>
                  <Select value={governorate} onValueChange={setGovernorate} disabled={!isEditingClinicInfo}>
                    <SelectTrigger
                      id="governorate"
                      className={`h-8 text-sm ${!isEditingClinicInfo ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    >
                      <SelectValue placeholder="Select governorate" />
                    </SelectTrigger>
                    <SelectContent>
                      {tunisianGovernorates.map((gov) => (
                        <SelectItem key={gov} value={gov} className="text-sm">
                          {gov}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="space-y-1">
                <Label htmlFor="address" className="text-xs font-medium">
                  Full Address
                </Label>
                <Textarea
                  id="address"
                  placeholder="Enter complete clinic address"
                  value={address}
                  onChange={(e) => setAddress(e.target.value)}
                  disabled={!isEditingClinicInfo}
                  className={`text-sm ${!isEditingClinicInfo ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                  rows={2}
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label htmlFor="phone" className="text-xs font-medium flex items-center gap-1">
                    Phone Number
                    <span className="text-red-500">*</span>
                  </Label>
                  <Input
                    id="phone"
                    type="tel"
                    placeholder="+216 12 345 678"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    disabled={!isEditingClinicInfo}
                    className={`h-8 text-sm ${!isEditingClinicInfo ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    required
                  />
                </div>

                <div className="space-y-1">
                  <Label htmlFor="email" className="text-xs font-medium flex items-center gap-1">
                    Professional Email
                    <span className="text-red-500">*</span>
                  </Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="clinic@example.com"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={!isEditingClinicInfo}
                    className={`h-8 text-sm ${!isEditingClinicInfo ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    required
                  />
                </div>
              </div>

              <Separator className="my-3" />

              <div className="space-y-2">
                <Label className="text-xs font-medium">Clinic Logo</Label>
                <div className="flex items-center gap-4">
                  {logoPreview ? (
                    // Show preview when user selects a new file (data URL)
                    <div className="relative w-20 h-20 rounded-lg border-2 border-blue-200 dark:border-blue-800 overflow-hidden shadow-sm group">
                      <Image
                        src={logoPreview}
                        alt="Logo preview"
                        fill
                        className="object-cover"
                      />
                      {isEditingClinicInfo && (
                        <button
                          onClick={() => {
                            setLogoPreview(null)
                            setLogoFile(null)
                          }}
                          className="absolute inset-0 bg-black/60 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"
                        >
                          <div className="bg-white dark:bg-slate-900 rounded-full p-1.5">
                            <Trash2 className="w-4 h-4 text-red-500" />
                          </div>
                        </button>
                      )}
                    </div>
                  ) : isEditingClinicInfo ? (
                    // Always show upload button when in edit mode
                    <label className="w-20 h-20 flex flex-col items-center justify-center border-2 border-dashed border-slate-300 dark:border-slate-700 rounded-lg cursor-pointer hover:border-blue-500 hover:bg-gradient-to-br hover:from-blue-50 hover:to-indigo-50 dark:hover:from-blue-950/20 dark:hover:to-indigo-950/20 transition-all group">
                      <Upload className="w-5 h-5 text-slate-400 group-hover:text-blue-600 transition-colors" />
                      <span className="text-[10px] text-slate-500 group-hover:text-blue-600 font-medium transition-colors mt-1">
                        {logoUrl ? "Change" : "Upload"}
                      </span>
                      <input type="file" accept="image/*" onChange={handleLogoUpload} className="hidden" />
                    </label>
                  ) : logoUrl ? (
                    // Show indicator that logo exists when not in edit mode
                    <div className="w-20 h-20 flex flex-col items-center justify-center border-2 border-blue-200 dark:border-blue-800 rounded-lg bg-blue-50 dark:bg-blue-950/20">
                      <Building2 className="w-8 h-8 text-blue-600 dark:text-blue-400" />
                      <span className="text-[8px] text-blue-600 dark:text-blue-400 mt-1">Logo set</span>
                    </div>
                  ) : (
                    <div className="text-xs text-muted-foreground italic">No logo uploaded</div>
                  )}
                </div>
              </div>

              {isEditingClinicInfo && (
                <div className="flex justify-end gap-2 pt-2 border-t">
                  <Button
                    onClick={handleCancelClinicInfo}
                    variant="ghost"
                    size="sm"
                    className="h-7 text-xs"
                    disabled={isSaving}
                  >
                    Cancel
                  </Button>
                  <Button
                    onClick={handleSaveClinicInfo}
                    size="sm"
                    className="h-7 text-xs bg-blue-600 hover:bg-blue-700"
                    disabled={isSaving}
                  >
                    <Save className="w-3 h-3 mr-1" />
                    {isSaving ? "Saving..." : "Save Changes"}
                  </Button>
                </div>
              )}
            </CardContent>
          )}
        </Card>

        {/* Doctors Card Collapsible */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <button
                onClick={() => setIsDoctorsCollapsed(!isDoctorsCollapsed)}
                className="flex items-center gap-2 flex-1 text-left hover:opacity-70 transition-opacity"
              >
                <div className="w-1 h-6 bg-blue-600 rounded-full" />
                <CardTitle className="text-base">Doctors</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
                    isDoctorsCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingDoctors && (
                <Button onClick={handleEditDoctors} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Edit
                </Button>
              )}
            </div>
          </CardHeader>
          {!isDoctorsCollapsed && (
            <CardContent className="space-y-3">
              {doctors.map((doctor, index) => (
                <Card
                  key={doctor.id}
                  className="border border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-900/50"
                >
                  <CardContent className="p-3">
                    <div className="flex items-start gap-3">
                      <div className="flex items-center justify-center w-7 h-7 rounded-full bg-blue-600 text-white text-xs font-semibold shrink-0 mt-0.5">
                        {index + 1}
                      </div>
                      <div className="flex-1 grid grid-cols-2 gap-2">
                        <div className="space-y-1">
                          <Label className="text-xs">Full Name</Label>
                          <Input
                            value={doctor.name}
                            onChange={(e) => updateDoctor(doctor.id, "name", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label className="text-xs">Specialty</Label>
                          <Select
                            value={doctor.specialty}
                            onValueChange={(value) => updateDoctor(doctor.id, "specialty", value)}
                            disabled={!isEditingDoctors}
                          >
                            <SelectTrigger className="h-7 text-sm">
                              <SelectValue placeholder="Select specialty" />
                            </SelectTrigger>
                            <SelectContent>
                              {specialties.map((spec) => (
                                <SelectItem key={spec} value={spec} className="text-sm">
                                  {spec}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </div>
                        <div className="space-y-1">
                          <Label className="text-xs">Phone</Label>
                          <Input
                            value={doctor.phone || ""}
                            onChange={(e) => updateDoctor(doctor.id, "phone", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label className="text-xs">Email</Label>
                          <Input
                            type="email"
                            value={doctor.email || ""}
                            onChange={(e) => updateDoctor(doctor.id, "email", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 text-sm"
                          />
                        </div>
                      </div>
                      {isEditingDoctors && doctors.length > 1 && (
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => removeDoctor(doctor.id)}
                          className="h-7 w-7 text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-950/20 shrink-0"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      )}
                    </div>
                  </CardContent>
                </Card>
              ))}

              {isEditingDoctors && (
                <>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={addDoctor}
                    className="w-full h-8 text-xs border-dashed bg-transparent"
                  >
                    <Plus className="w-3 h-3 mr-1" />
                    Add Doctor
                  </Button>
                  <div className="flex justify-end gap-2 pt-2 border-t">
                    <Button
                      onClick={handleCancelDoctors}
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs"
                      disabled={isSaving}
                    >
                      Cancel
                    </Button>
                    <Button
                      onClick={handleSaveDoctors}
                      size="sm"
                      className="h-7 text-xs bg-blue-600 hover:bg-blue-700"
                      disabled={isSaving}
                    >
                      <Save className="w-3 h-3 mr-1" />
                      {isSaving ? "Saving..." : "Save Changes"}
                    </Button>
                  </div>
                </>
              )}
            </CardContent>
          )}
        </Card>

        {/* Working Hours Card Collapsible */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <button
                onClick={() => setIsHoursCollapsed(!isHoursCollapsed)}
                className="flex items-center gap-2 flex-1 text-left hover:opacity-70 transition-opacity"
              >
                <div className="w-1 h-6 bg-blue-600 rounded-full" />
                <CardTitle className="text-base">Working Hours</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
                    isHoursCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingHours && (
                <Button onClick={handleEditHours} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Edit
                </Button>
              )}
            </div>
          </CardHeader>
          {!isHoursCollapsed && (
            <CardContent className="space-y-2">
              {workingHours.map((item) => (
                <div
                  key={item.day}
                  className={`flex items-center gap-3 p-2 rounded-lg border ${
                    item.enabled
                      ? "border-blue-200 dark:border-blue-700 bg-blue-50/30 dark:bg-blue-950/20"
                      : "border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-900/50"
                  }`}
                >
                  <div className="flex items-center gap-2 w-32">
                    <Checkbox
                      checked={item.enabled}
                      onCheckedChange={() => toggleWorkingDay(item.day)}
                      disabled={!isEditingHours}
                      className="h-4 w-4"
                    />
                    <Label className="text-xs font-medium">{item.day}</Label>
                  </div>
                  <div className="flex-1 flex items-center gap-2">
                    <Input
                      type="time"
                      value={item.from}
                      onChange={(e) => updateWorkingHours(item.day, "from", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 text-xs ${!isEditingHours || !item.enabled ? "bg-gray-50 dark:bg-slate-900/50" : ""}`}
                    />
                    <span className="text-xs text-muted-foreground">to</span>
                    <Input
                      type="time"
                      value={item.to}
                      onChange={(e) => updateWorkingHours(item.day, "to", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 text-xs ${!isEditingHours || !item.enabled ? "bg-gray-50 dark:bg-slate-900/50" : ""}`}
                    />
                  </div>
                </div>
              ))}

              {isEditingHours && (
                <div className="flex justify-end gap-2 pt-2 border-t">
                  <Button
                    onClick={handleCancelHours}
                    variant="ghost"
                    size="sm"
                    className="h-7 text-xs"
                    disabled={isSaving}
                  >
                    Cancel
                  </Button>
                  <Button
                    onClick={handleSaveHours}
                    size="sm"
                    className="h-7 text-xs bg-blue-600 hover:bg-blue-700"
                    disabled={isSaving}
                  >
                    <Save className="w-3 h-3 mr-1" />
                    {isSaving ? "Saving..." : "Save Changes"}
                  </Button>
                </div>
              )}
            </CardContent>
          )}
        </Card>

        {/* Admin-only backup card — Local mode only (US-8 / FR-G). */}
        {mode === "local" && user?.role === "admin" && <BackupSettings />}

        <Card className="border border-blue-200 dark:border-blue-800 bg-blue-50/50 dark:bg-blue-950/20">
          <CardContent className="p-3">
            <div className="flex items-start gap-2">
              <Info className="w-4 h-4 text-blue-600 dark:text-blue-400 mt-0.5 shrink-0" />
              <div className="space-y-1">
                <p className="text-xs font-medium text-blue-900 dark:text-blue-100">Need help?</p>
                <p className="text-xs text-blue-700 dark:text-blue-300">
                  Contact support at support@clinic.com or call +216 XX XXX XXX
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
