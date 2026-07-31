"use client"

import type React from "react"

import { useState, useEffect } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
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
  Edit,
  Save,
  Info,
  ChevronDown,
} from "lucide-react"
import { toast } from "sonner"
import Image from "next/image"
import { clinicsApi, type ClinicDto } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"
import { BackupSettings } from "@/components/backup-settings"
import Link from "next/link"
import { DoctorDocumentIdentityDialog } from "@/components/doctor-document-identity-dialog"
import { DoctorWorkingHoursCard } from "@/components/doctor-working-hours-card"
import { DEFAULT_WORKING_HOURS } from "@/lib/working-hours"
import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"

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


interface Doctor {
  id: string
  name: string
  specialty: string
  phone?: string
  email?: string
  codeProfessionnelSante?: string
  /**
   * Document identity (CNOMDT + cachet presence). Read-only here: it is projected by `GetUserStatusQuery` and
   * edited through `PUT /api/doctors/{id}` in its own dialog — the roster save (`PUT /clinics/doctors`) neither
   * reads nor writes it, so mixing the two would drop these values on every roster save.
   */
  ordreNumberCnomdt?: string | null
  hasCachet?: boolean
}

interface WorkingHoursInput {
  day: string
  enabled: boolean
  from: string
  to: string
}

/** French labels for the (English) weekday storage keys — the `weekdayLabelsFr` convention. */
const WEEKDAY_LABELS_FR: Record<string, string> = {
  Monday: "Lundi",
  Tuesday: "Mardi",
  Wednesday: "Mercredi",
  Thursday: "Jeudi",
  Friday: "Vendredi",
  Saturday: "Samedi",
  Sunday: "Dimanche",
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

  // Billing / note-d'honoraires settings
  const [matriculeFiscal, setMatriculeFiscal] = useState("")
  const [vatApplicable, setVatApplicable] = useState(false)
  const [vatRate, setVatRate] = useState("7")
  const [stampDutyEnabled, setStampDutyEnabled] = useState(true)
  const [stampDutyAmount, setStampDutyAmount] = useState("1.000")
  const [isEditingBilling, setIsEditingBilling] = useState(false)
  const [isBillingCollapsed, setIsBillingCollapsed] = useState(true)
  const [originalBilling, setOriginalBilling] = useState<any>({})

  // TTN « El Fatoora » e-invoicing settings (part of the billing card)
  const [ttnEInvoicingEnabled, setTtnEInvoicingEnabled] = useState(false)
  const [ttnEnvironment, setTtnEnvironment] = useState("Sandbox")

  // Working Hours State — seeded from the shared default; overwritten by the clinic's saved hours on load.
  const [workingHours, setWorkingHours] = useState<WorkingHoursInput[]>(
    DEFAULT_WORKING_HOURS.map((d) => ({ ...d })),
  )

  // Doctors State
  const [doctors, setDoctors] = useState<Doctor[]>([{ id: "1", name: "", specialty: "", phone: "", email: "" }])
  /**
   * The practitioner whose CNOMDT + cachet are being edited (AC-P2.30); null closes the dialog. Admin-only, and
   * only for a doctor that actually exists server-side — an unsaved roster row has a client-side placeholder id
   * that `PUT /api/doctors/{id}` could not resolve.
   */
  const [documentIdentityTarget, setDocumentIdentityTarget] = useState<Doctor | null>(null)
  const isClinicAdmin = user?.role === "admin"

  // Edit Modes and Notifications State
  const [isEditingClinicInfo, setIsEditingClinicInfo] = useState(false)
  const [isEditingDoctors, setIsEditingDoctors] = useState(false)
  const [isEditingHours, setIsEditingHours] = useState(false)

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
        // Billing settings
        setMatriculeFiscal(clinic.matriculeFiscal || "")
        setVatApplicable(clinic.vatApplicable ?? false)
        setVatRate(String(clinic.vatRate ?? 7))
        setStampDutyEnabled(clinic.stampDutyEnabled ?? true)
        setStampDutyAmount(String(clinic.stampDutyAmount ?? 1))
        setTtnEInvoicingEnabled(clinic.ttnEInvoicingEnabled ?? false)
        setTtnEnvironment(clinic.ttnEnvironment ?? "Sandbox")
        // Working hours (AC-7): use the clinic's saved hours; keep the default when none are stored.
        if (clinic.workingHours && clinic.workingHours.length > 0) {
          setWorkingHours(clinic.workingHours.map((d) => ({ ...d })))
        }
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
              codeProfessionnelSante: d.codeProfessionnelSante || "",
              ordreNumberCnomdt: d.ordreNumberCnomdt ?? "",
              hasCachet: d.hasCachet ?? false,
            })),
          )
        }
      } else if (status.hasClinic && status.clinicName) {
        setClinicName(status.clinicName)
      }
    } catch (err: any) {
      toast.error("Échec du chargement des données de la clinique : " + (err.message || "Erreur inconnue"))
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

  // AC-P3.37 — the bespoke `fixed top-4 right-4` banner and this 4-second timer are gone; feedback goes
  // through `sonner` like every other screen, so it stacks, dismisses and reads the same everywhere.

  // Real-time: reload clinic profile/doctors when another client of this clinic changes them — but not
  // while this admin is mid-edit, so a live refresh never clobbers unsaved form input.
  // A peer's change is deliberately NOT applied while a section is being edited — that would wipe the
  // user's typing. But silently dropping it meant they went on to save over the other person with no idea
  // anything had happened, and then hit a 409 they could not explain. Record it and offer the reload.
  const [peerChangePending, setPeerChangePending] = useState(false)

  useClinicRealtime(RealtimeResource.Clinics, () => {
    if (!isEditingClinicInfo && !isEditingDoctors && !isEditingHours && !isEditingBilling) {
      loadClinicData()
      return
    }
    setPeerChangePending(true)
  })

  const reloadAfterPeerChange = () => {
    setPeerChangePending(false)
    loadClinicData()
  }

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
        // The governorate is the cabinet city printed on generated documents ("{ville}, le …", FR-6.1).
        city: governorate || "",
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

      toast.success("Informations de la clinique enregistrées.")
      setIsEditingClinicInfo(false)
    } catch (error: any) {
      toast.error(error.message || "Échec de l'enregistrement des informations de la clinique. Veuillez réessayer.")
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
          if (d.codeProfessionnelSante?.trim()) {
            doctor.codeProfessionnelSante = d.codeProfessionnelSante.trim()
          }

          return doctor
        })

      if (validDoctors.length === 0) {
        toast.error("Veuillez ajouter au moins un médecin avec un nom et une spécialité")
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
          codeProfessionnelSante: d.codeProfessionnelSante || "",
        })),
      )

      toast.success("Informations des médecins enregistrées.")
      setIsEditingDoctors(false)
    } catch (error: any) {
      toast.error(error.message || "Échec de l'enregistrement des informations des médecins. Veuillez réessayer.")
    } finally {
      setIsSaving(false)
    }
  }

  const handleEditBilling = () => {
    setOriginalBilling({ matriculeFiscal, vatApplicable, vatRate, stampDutyEnabled, stampDutyAmount, ttnEInvoicingEnabled, ttnEnvironment })
    setIsEditingBilling(true)
  }

  const handleCancelBilling = () => {
    setMatriculeFiscal(originalBilling.matriculeFiscal ?? "")
    setVatApplicable(originalBilling.vatApplicable ?? false)
    setVatRate(originalBilling.vatRate ?? "7")
    setStampDutyEnabled(originalBilling.stampDutyEnabled ?? true)
    setStampDutyAmount(originalBilling.stampDutyAmount ?? "1.000")
    setTtnEInvoicingEnabled(originalBilling.ttnEInvoicingEnabled ?? false)
    setTtnEnvironment(originalBilling.ttnEnvironment ?? "Sandbox")
    setIsEditingBilling(false)
  }

  const handleSaveBilling = async () => {
    setIsSaving(true)
    try {
      const fullAddress = address && governorate
        ? `${address}, ${governorate}`
        : governorate || address || undefined

      await clinicsApi.update({
        name: clinicName,
        address: fullAddress,
        phone,
        email,
        matriculeFiscal,
        vatApplicable,
        vatRate: Number(vatRate) || 0,
        stampDutyEnabled,
        stampDutyAmount: Number(stampDutyAmount) || 0,
        ttnEInvoicingEnabled,
        ttnEnvironment,
      })

      toast.success("Paramètres de facturation enregistrés.")
      setIsEditingBilling(false)
    } catch (error: any) {
      toast.error(error.message || "Échec de l'enregistrement des paramètres de facturation.")
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
      // Re-send the clinic identity fields (the update path overwrites them) alongside the working hours,
      // mirroring the billing save — otherwise omitting them would clear name/address/phone/email.
      const fullAddress = address && governorate
        ? `${address}, ${governorate}`
        : governorate || address || undefined

      const updated = await clinicsApi.update({
        name: clinicName,
        address: fullAddress,
        phone,
        email,
        workingHoursJson: JSON.stringify(workingHours),
      })

      if (updated.workingHours && updated.workingHours.length > 0) {
        setWorkingHours(updated.workingHours.map((d) => ({ ...d })))
      }

      toast.success("Horaires enregistrés.")
      setIsEditingHours(false)
    } catch (error: any) {
      toast.error(error.message || "Échec de l'enregistrement des horaires.")
    } finally {
      setIsSaving(false)
    }
  }

  if (isLoading) {
    return (
      <div className="min-h-full flex items-center justify-center bg-gray-50 dark:bg-slate-950">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
          <p className="text-muted-foreground">Chargement des paramètres de la clinique…</p>
        </div>
      </div>
    )
  }

  // `min-h-full`, not `min-h-screen`. This renders inside `<main>`, which is already the viewport minus the header
  // — so demanding a full 100vh here made the content taller than its own scroll container by exactly the header's
  // height, producing a scrollbar and a band of empty page below the last card on every visit.
  return (
    <div className="min-h-full bg-gray-50 dark:bg-slate-950">

      {/* A colleague saved these settings while this form was open. */}
      {peerChangePending && (
        <FormErrorBanner
          message="Les paramètres du cabinet ont été modifiés par quelqu'un d'autre pendant votre saisie. Vos modifications non enregistrées seront conservées si vous rechargez maintenant… mais la version affichée n'est plus à jour."
          action={{ label: "Recharger les paramètres", onClick: reloadAfterPeerChange }}
        />
      )}
      <div className="max-w-5xl mx-auto p-3 space-y-3">
        <div className="flex items-center gap-2 mb-3">
          <div className="flex items-center justify-center w-8 h-8 rounded-lg bg-primary">
            <Building2 className="w-4 h-4 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">Paramètres de la clinique</h1>
            <p className="text-xs text-muted-foreground">Gérez les informations et l&apos;équipe de votre clinique</p>
          </div>
        </div>

        {/* Clinic Code under header */}
        {clinicCode && (
          <div className="bg-accent/20 border border-primary/25 rounded-lg p-3">
            <Label className="text-xs text-primary font-medium">Code de la clinique</Label>
            <div className="flex items-center gap-2 mt-1.5">
              <Badge
                variant="outline"
                className="text-base font-mono font-bold px-3 py-1 bg-white dark:bg-slate-900 text-primary border-primary/40"
              >
                {clinicCode}
              </Badge>
            </div>
            <p className="text-2xs text-primary mt-1.5">
              Communiquez ce code à vos collègues pour qu'ils rejoignent la clinique
            </p>
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
                <div className="w-1 h-6 bg-primary rounded-full" />
                <CardTitle className="text-base">Informations de la clinique</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
 isClinicInfoCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingClinicInfo && (
                <Button onClick={handleEditClinicInfo} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
                </Button>
              )}
            </div>
          </CardHeader>
          {!isClinicInfoCollapsed && (
            <CardContent className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label htmlFor="clinic-name" className="text-xs font-medium flex items-center gap-1">
                    Nom de la clinique
                    <span className="text-red-500">*</span>
                  </Label>
                  <Input
                    id="clinic-name"
                    placeholder="Saisir le nom de la clinique"
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
                      <SelectValue placeholder="Sélectionner un gouvernorat" />
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
                  Adresse complète
                </Label>
                <Textarea
                  id="address"
                  placeholder="Saisir l'adresse complète de la clinique"
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
                    Numéro de téléphone
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
                    Email professionnel
                    <span className="text-red-500">*</span>
                  </Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="ex. : contact@maclinique.tn"
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
                <Label className="text-xs font-medium">Logo de la clinique</Label>
                <div className="flex items-center gap-4">
                  {logoPreview ? (
                    // Show preview when user selects a new file (data URL)
                    <div className="relative w-20 h-20 rounded-lg border-2 border-primary/25 overflow-hidden shadow-sm group">
                      <Image
                        src={logoPreview}
                        alt="Aperçu du logo"
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
                    <label className="w-20 h-20 flex flex-col items-center justify-center border-2 border-dashed border-slate-300 dark:border-slate-700 rounded-lg cursor-pointer hover:border-primary hover:bg-gradient-to-br hover:from-accent hover:to-indigo-50/20 dark:hover:to-indigo-950/20 transition-all group">
                      <Upload className="w-5 h-5 text-slate-400 group-hover:text-primary transition-colors" />
                      <span className="text-2xs text-slate-500 group-hover:text-primary font-medium transition-colors mt-1">
                        {logoUrl ? "Modifier" : "Téléverser"}
                      </span>
                      <input type="file" accept="image/*" onChange={handleLogoUpload} className="hidden" />
                    </label>
                  ) : logoUrl ? (
                    // Show indicator that logo exists when not in edit mode
                    <div className="w-20 h-20 flex flex-col items-center justify-center border-2 border-primary/25 rounded-lg bg-accent/20">
                      <Building2 className="w-8 h-8 text-primary" />
                      <span className="text-2xs text-primary mt-1">Logo défini</span>
                    </div>
                  ) : (
                    <div className="text-xs text-muted-foreground italic">Aucun logo téléversé</div>
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
                    Annuler
                  </Button>
                  <Button
                    onClick={handleSaveClinicInfo}
                    size="sm"
                    className="h-7 text-xs bg-primary hover:bg-primary/90"
                    disabled={isSaving}
                  >
                    <Save className="w-3 h-3 mr-1" />
                    {isSaving ? "Enregistrement…" : "Enregistrer les modifications"}
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
                <div className="w-1 h-6 bg-primary rounded-full" />
                <CardTitle className="text-base">Médecins</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
 isDoctorsCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingDoctors && (
                <Button onClick={handleEditDoctors} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
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
                      <div className="flex items-center justify-center w-7 h-7 rounded-full bg-primary text-white text-xs font-semibold shrink-0 mt-0.5">
                        {index + 1}
                      </div>
                      <div className="flex-1 grid grid-cols-2 gap-2">
                        <div className="space-y-1">
                          <Label className="text-xs">Nom complet</Label>
                          <Input
                            value={doctor.name}
                            onChange={(e) => updateDoctor(doctor.id, "name", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label className="text-xs">Spécialité</Label>
                          <Select
                            value={doctor.specialty}
                            onValueChange={(value) => updateDoctor(doctor.id, "specialty", value)}
                            disabled={!isEditingDoctors}
                          >
                            <SelectTrigger className="h-7 text-sm">
                              <SelectValue placeholder="Sélectionner une spécialité" />
                            </SelectTrigger>
                            <SelectContent>
                              {/* AC-P2.42/2.43 — the option VALUE stays the English storage key; only the label
                                  is French. A doctor already stored as "Dentist" therefore still matches. */}
                              {DOCTOR_SPECIALTIES.map((spec) => (
                                <SelectItem key={spec} value={spec} className="text-sm">
                                  {specialtyLabel(spec)}
                                </SelectItem>
                              ))}
                              {/* AC-P2.45 — a stored custom value is no option of ours; add it so the trigger
                                  shows it verbatim instead of falling back to the placeholder (which would let
                                  an unrelated save silently rewrite it). */}
                              {doctor.specialty &&
                                !DOCTOR_SPECIALTIES.includes(doctor.specialty as (typeof DOCTOR_SPECIALTIES)[number]) && (
                                  <SelectItem value={doctor.specialty} className="text-sm">
                                    {specialtyLabel(doctor.specialty)}
                                  </SelectItem>
                                )}
                            </SelectContent>
                          </Select>
                        </div>
                        <div className="space-y-1">
                          <Label className="text-xs">Téléphone</Label>
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
                        <div className="space-y-1">
                          <Label className="text-xs">Code prof. santé (CNAM)</Label>
                          <Input
                            value={doctor.codeProfessionnelSante || ""}
                            onChange={(e) => updateDoctor(doctor.id, "codeProfessionnelSante", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 text-sm"
                          />
                        </div>
                        {/* AC-P2.30 — the CNOMDT number and cachet « Mon profil » already told the admin they
                            could set from here. Read-only in the roster because they belong to
                            `PUT /api/doctors/{id}`, not to the roster rewrite; « Modifier » opens that. */}
                        <div className="space-y-1">
                          <Label className="text-xs">Identité documentaire</Label>
                          <div className="flex h-7 items-center gap-2 text-sm">
                            <span className="text-muted-foreground">
                              {doctor.ordreNumberCnomdt
                                ? `N° ordre ${doctor.ordreNumberCnomdt}`
                                : "Pas de n° d'ordre"}
                              {" · "}
                              {doctor.hasCachet ? "cachet enregistré" : "pas de cachet"}
                            </span>
                            {isClinicAdmin && !isEditingDoctors && doctor.id && !doctor.id.startsWith("doctor-") && (
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-6 px-2 text-xs"
                                onClick={() => setDocumentIdentityTarget(doctor)}
                              >
                                Modifier
                              </Button>
                            )}
                          </div>
                        </div>
                      </div>
                      {/* § 5.4 / AC-P1.25 — an admin sets any practitioner's own hours. Only for a doctor
                          that exists server-side: an unsaved roster row has a client-side placeholder id the
                          endpoint could not resolve. */}
                      {isClinicAdmin && !isEditingDoctors && doctor.id && !doctor.id.startsWith("doctor-") && (
                        <details className="mt-2 w-full">
                          <summary className="cursor-pointer text-xs font-medium text-muted-foreground hover:text-foreground">
                            Horaires de ce praticien
                          </summary>
                          <div className="mt-2">
                            <DoctorWorkingHoursCard doctorId={doctor.id} embedded />
                          </div>
                        </details>
                      )}
                                            {isEditingDoctors && doctors.length > 1 && (
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => removeDoctor(doctor.id)}
                          className="h-7 w-7 text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-950/20 shrink-0"
                          aria-label={doctor.name ? `Retirer ${doctor.name} de la liste` : "Retirer ce praticien de la liste"}
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
                    Ajouter un médecin
                  </Button>
                  <div className="flex justify-end gap-2 pt-2 border-t">
                    <Button
                      onClick={handleCancelDoctors}
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs"
                      disabled={isSaving}
                    >
                      Annuler
                    </Button>
                    <Button
                      onClick={handleSaveDoctors}
                      size="sm"
                      className="h-7 text-xs bg-primary hover:bg-primary/90"
                      disabled={isSaving}
                    >
                      <Save className="w-3 h-3 mr-1" />
                      {isSaving ? "Enregistrement…" : "Enregistrer les modifications"}
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
                <div className="w-1 h-6 bg-primary rounded-full" />
                <CardTitle className="text-base">Horaires d'ouverture</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
 isHoursCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingHours && (
                <Button onClick={handleEditHours} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
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
                      ? "border-primary/25 bg-accent/30/20"
                      : "border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-900/50"
                  }`}
                >
                  {/* AC-P1.54: the day name labelled nothing and both time inputs were nameless to a screen
                      reader, in a card where every other field is wired. The separator also read « to ». */}
                  <div className="flex items-center gap-2 w-32">
                    <Checkbox
                      id={`clinic-hours-${item.day}-enabled`}
                      checked={item.enabled}
                      onCheckedChange={() => toggleWorkingDay(item.day)}
                      disabled={!isEditingHours}
                      className="h-4 w-4"
                    />
                    <Label htmlFor={`clinic-hours-${item.day}-enabled`} className="text-xs font-medium">
                      {WEEKDAY_LABELS_FR[item.day] ?? item.day}
                    </Label>
                  </div>
                  <div className="flex-1 flex items-center gap-2">
                    <Label htmlFor={`clinic-hours-${item.day}-from`} className="sr-only">
                      {`Heure d'ouverture — ${WEEKDAY_LABELS_FR[item.day] ?? item.day}`}
                    </Label>
                    <Input
                      id={`clinic-hours-${item.day}-from`}
                      type="time"
                      value={item.from}
                      onChange={(e) => updateWorkingHours(item.day, "from", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 text-xs ${!isEditingHours || !item.enabled ? "bg-gray-50 dark:bg-slate-900/50" : ""}`}
                    />
                    <span className="text-xs text-muted-foreground">à</span>
                    <Label htmlFor={`clinic-hours-${item.day}-to`} className="sr-only">
                      {`Heure de fermeture — ${WEEKDAY_LABELS_FR[item.day] ?? item.day}`}
                    </Label>
                    <Input
                      id={`clinic-hours-${item.day}-to`}
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
                    Annuler
                  </Button>
                  <Button
                    onClick={handleSaveHours}
                    size="sm"
                    className="h-7 text-xs bg-primary hover:bg-primary/90"
                    disabled={isSaving}
                  >
                    <Save className="w-3 h-3 mr-1" />
                    {isSaving ? "Enregistrement…" : "Enregistrer les modifications"}
                  </Button>
                </div>
              )}
            </CardContent>
          )}
        </Card>

        {/* Billing / note-d'honoraires settings */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <button
                onClick={() => setIsBillingCollapsed(!isBillingCollapsed)}
                className="flex items-center gap-2 flex-1 text-left hover:opacity-70 transition-opacity"
              >
                <div className="w-1 h-6 bg-primary rounded-full" />
                <CardTitle className="text-base">Facturation (note d'honoraires)</CardTitle>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
 isBillingCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {!isEditingBilling && (
                <Button onClick={handleEditBilling} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
                </Button>
              )}
            </div>
          </CardHeader>
          {!isBillingCollapsed && (
            <CardContent className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="matricule-fiscal" className="text-xs font-medium">
                  Matricule fiscal
                </Label>
                <Input
                  id="matricule-fiscal"
                  placeholder="Ex. 1234567/A/M/000"
                  value={matriculeFiscal}
                  onChange={(e) => setMatriculeFiscal(e.target.value)}
                  disabled={!isEditingBilling}
                  className={`h-8 text-sm ${!isEditingBilling ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-2">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      id="vat-applicable"
                      checked={vatApplicable}
                      onCheckedChange={(checked) => setVatApplicable(checked === true)}
                      disabled={!isEditingBilling}
                      className="h-4 w-4"
                    />
                    <Label htmlFor="vat-applicable" className="text-xs font-medium">TVA applicable</Label>
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="vat-rate" className="text-xs font-medium">Taux de TVA (%)</Label>
                    <Input
                      id="vat-rate"
                      type="number"
                      min="0"
                      step="0.01"
                      value={vatRate}
                      onChange={(e) => setVatRate(e.target.value)}
                      disabled={!isEditingBilling || !vatApplicable}
                      className={`h-8 text-sm ${!isEditingBilling || !vatApplicable ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      id="stamp-enabled"
                      checked={stampDutyEnabled}
                      onCheckedChange={(checked) => setStampDutyEnabled(checked === true)}
                      disabled={!isEditingBilling}
                      className="h-4 w-4"
                    />
                    <Label htmlFor="stamp-enabled" className="text-xs font-medium">Timbre fiscal</Label>
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="stamp-amount" className="text-xs font-medium">Montant du timbre (DT)</Label>
                    <Input
                      id="stamp-amount"
                      type="number"
                      min="0"
                      step="0.001"
                      value={stampDutyAmount}
                      onChange={(e) => setStampDutyAmount(e.target.value)}
                      disabled={!isEditingBilling || !stampDutyEnabled}
                      className={`h-8 text-sm ${!isEditingBilling || !stampDutyEnabled ? "bg-slate-50 dark:bg-slate-900/50" : ""}`}
                    />
                  </div>
                </div>
              </div>

              {/* TTN « El Fatoora » electronic invoicing (FR-8) */}
              <div className="space-y-3 pt-3 border-t">
                <div className="flex items-center gap-2">
                  <Checkbox
                    id="ttn-enabled"
                    checked={ttnEInvoicingEnabled}
                    onCheckedChange={(checked) => setTtnEInvoicingEnabled(checked === true)}
                    disabled={!isEditingBilling}
                    className="h-4 w-4"
                  />
                  <Label htmlFor="ttn-enabled" className="text-xs font-medium">
                    Facturation électronique TTN « El Fatoora »
                  </Label>
                </div>
                <div className="space-y-1 max-w-xs">
                  <Label htmlFor="ttn-environment" className="text-xs font-medium">Environnement</Label>
                  <Select
                    value={ttnEnvironment}
                    onValueChange={setTtnEnvironment}
                    disabled={!isEditingBilling || !ttnEInvoicingEnabled}
                  >
                    <SelectTrigger id="ttn-environment" className="h-8 text-sm">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="Sandbox">Test (sandbox)</SelectItem>
                      <SelectItem value="Production">Production</SelectItem>
                    </SelectContent>
                  </Select>
                  <p className="text-2xs text-muted-foreground">
                    Le certificat qualifié et les identifiants TTN sont fournis côté serveur (dossier sécurisé
                    <code className="mx-1">.local/</code>), jamais saisis ici.
                  </p>
                </div>
              </div>

              {isEditingBilling && (
                <div className="flex justify-end gap-2 pt-2 border-t">
                  <Button onClick={handleCancelBilling} variant="ghost" size="sm" className="h-7 text-xs" disabled={isSaving}>
                    Annuler
                  </Button>
                  <Button onClick={handleSaveBilling} size="sm" className="h-7 text-xs bg-primary hover:bg-primary/90" disabled={isSaving}>
                    <Save className="w-3 h-3 mr-1" />
                    {isSaving ? "Enregistrement…" : "Enregistrer"}
                  </Button>
                </div>
              )}
            </CardContent>
          )}
        </Card>

        {/*
          The reminder channel configuration moved to its own page — « Rappels » (/rappels), where it opens in a
          sheet beside the delivery log. It sat here as a card whose bottom third was a 20-row status list, which
          put the thing staff read daily underneath the thing an admin sets once. This link is left behind
          deliberately: someone who knows the setting as "in Paramètres" has to find where it went.
        */}
        {user?.role === "admin" && (
          <Card>
            <CardContent className="flex flex-wrap items-center justify-between gap-3 p-4">
              <div className="space-y-0.5">
                <p className="text-sm font-medium">Rappels SMS / WhatsApp</p>
                <p className="text-xs text-muted-foreground">
                  Les canaux, les délais et le journal des envois ont leur propre page.
                </p>
              </div>
              <Button variant="outline" size="sm" asChild>
                <Link href="/rappels">Ouvrir « Rappels »</Link>
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Admin-only backup card — Local mode only (US-8 / FR-G). */}
        {mode === "local" && user?.role === "admin" && <BackupSettings />}

        <Card className="border border-primary/25 bg-accent/50/20">
          <CardContent className="p-3">
            <div className="flex items-start gap-2">
              <Info className="w-4 h-4 text-primary mt-0.5 shrink-0" />
              <div className="space-y-1">
                <p className="text-xs font-medium text-accent-foreground">Need help?</p>
                <p className="text-xs text-primary">
                  Contact support at support@clinic.com or call +216 XX XXX XXX
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* AC-P2.30 — set another practitioner's CNOMDT number and cachet (PUT /api/doctors/{id}). */}
      <DoctorDocumentIdentityDialog
        doctor={documentIdentityTarget}
        onOpenChange={(open) => { if (!open) setDocumentIdentityTarget(null) }}
        onSaved={() => {
          setDocumentIdentityTarget(null)
          // Re-read the roster so the row's « n° ordre / cachet » summary reflects what was just saved.
          loadClinicData()
        }}
      />
    </div>
  )
}
