"use client"

import type React from "react"
import { useState, useEffect } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { TUNISIAN_GOVERNORATES } from "@/lib/tunisia"
import { Switch } from "@/components/ui/switch"
import { Separator } from "@/components/ui/separator"
import { Badge } from "@/components/ui/badge"
import { toast } from "sonner"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useConflict } from "@/lib/hooks/use-conflict"
import { User, Phone, Heart, CreditCard, Flag, Save, X, Plus, Trash2 } from "lucide-react"
import { cn } from "@/lib/utils"
import { patientsApi } from "@/lib/api/patients"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import type { PatientDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { isDeliverablePhone, PHONE_ERROR_FR } from "@/lib/phone"
import { SELECTABLE_GENDERS, genderLabel } from "@/components/appointment-labels"

interface EditPatientDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patient: PatientDto | null
  /** Called on success; receives the saved patient (used to open a newly-created patient). */
  onSuccess?: (patient?: PatientDto) => void
}

export function EditPatientDialog({ open, onOpenChange, patient, onSuccess }: EditPatientDialogProps) {
  // Personal Info State
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [gender, setGender] = useState("")
  const [birthdate, setBirthdate] = useState("")
  const [phone, setPhone] = useState("")
  const [email, setEmail] = useState("")
  const [addressStreet, setAddressStreet] = useState("")
  const [addressGovernorate, setAddressGovernorate] = useState("")
  const [addressCity, setAddressCity] = useState("")
  const [addressPostalCode, setAddressPostalCode] = useState("")
  const [emergencyName, setEmergencyName] = useState("")
  const [emergencyPhone, setEmergencyPhone] = useState("")

  // Medical Info State
  const [chronicDiseases, setChronicDiseases] = useState("")
  const [allergies, setAllergies] = useState("")
  
  // Medical History Entries (replaces Past Surgeries)
  const [medicalHistoryEntries, setMedicalHistoryEntries] = useState<Array<{
    id?: string;
    description: string;
    date?: string;
    notes?: string;
    isNew?: boolean;
  }>>([])
  
  // Family History Entries
  const [familyHistoryEntries, setFamilyHistoryEntries] = useState<Array<{
    id?: string;
    relationship: string;
    condition: string;
    notes?: string;
    isNew?: boolean;
  }>>([])

  // Administrative State
  const [insuranceProvider, setInsuranceProvider] = useState("")
  const [insuranceNumber, setInsuranceNumber] = useState("")
  const [policyHolder, setPolicyHolder] = useState("")

  // CNAM identity (optional — pre-fills the Bulletin de soins BS1).
  const [cnam, setCnam] = useState({
    identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "",
    assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "",
  })

  // Flags State
  const [flagged, setFlagged] = useState(false)
  const [flagNotes, setFlagNotes] = useState("")

  const [loading, setLoading] = useState(false)
  // The one editing surface in the app with no form-level error display: a failed save produced a toast
  // that disappeared while the dialog sat there looking fine.
  const conflict = useConflict()
  const [errors, setErrors] = useState<Record<string, string>>({})

  // Populate the form once per opening.
  //
  // Keyed on the patient's ID, not the object: this dialog's parent refetches on every realtime
  // `patients` event, which hands down a new object identity. Depending on the object meant a peer's
  // unrelated edit re-ran this effect and wiped whatever the user had typed, mid-sentence.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (open) {
      if (patient) {
        // Edit mode: populate with existing patient data
      setFirstName(patient.firstName || "")
      setLastName(patient.lastName || "")
      setGender(patient.gender || "")
      setBirthdate(patient.dateOfBirth ? patient.dateOfBirth.split('T')[0] : "")
      setPhone(patient.phoneNumber || "")
      setEmail(patient.email || "")
      
      // Set address fields from address object
      if (patient.address) {
        setAddressStreet(patient.address.street || "")
        setAddressGovernorate(patient.address.state || "")
        setAddressCity(patient.address.city || "")
        setAddressPostalCode(patient.address.zipCode || "")
      } else {
        setAddressStreet("")
        setAddressGovernorate("")
        setAddressCity("")
        setAddressPostalCode("")
      }

      // Emergency contact (finding #11)
      setEmergencyName(patient.emergencyContactName || "")
      setEmergencyPhone(patient.emergencyContactPhone || "")

      // Insurance info
      setInsuranceProvider(patient.insuranceInfo?.provider || "")
      setInsuranceNumber(patient.insuranceInfo?.policyNumber || "")
      setPolicyHolder(patient.insuranceInfo?.groupNumber || "")

      // CNAM identity
      setCnam({
        identifiantUnique: patient.cnamInfo?.identifiantUnique || "",
        regime: patient.cnamInfo?.regime || "",
        assureFirstName: patient.cnamInfo?.assureFirstName || "",
        assureLastName: patient.cnamInfo?.assureLastName || "",
        assureAddress: patient.cnamInfo?.assureAddress || "",
        assurePostalCode: patient.cnamInfo?.assurePostalCode || "",
        maladeLien: patient.cnamInfo?.maladeLien || "",
        maladeLienRang: patient.cnamInfo?.maladeLienRang || "",
      })

      // Medical info - parse from strings
      setAllergies(patient.allergies || "")
      setChronicDiseases(patient.medicalHistory || "")
      
      // Flags
      const hasActiveFlags = patient.flags && patient.flags.some(flag => flag.isActive)
      setFlagged(hasActiveFlags || false)
      if (hasActiveFlags && patient.flags) {
        const activeFlag = patient.flags.find(flag => flag.isActive)
        setFlagNotes(activeFlag?.notes || activeFlag?.description || "")
      } else {
        setFlagNotes("")
      }

        // Load medical and family history entries when dialog opens
        if (patient.id) {
          loadMedicalHistoryEntries(patient.id)
          loadFamilyHistoryEntries(patient.id)
        }
      } else {
        // Create mode: reset form to empty
        setFirstName("")
        setLastName("")
        setGender("")
        setBirthdate("")
        setPhone("")
        setEmail("")
        setAddressStreet("")
        setAddressGovernorate("")
        setAddressCity("")
        setAddressPostalCode("")
        setEmergencyName("")
        setEmergencyPhone("")
        setChronicDiseases("")
        setAllergies("")
        setInsuranceProvider("")
        setInsuranceNumber("")
        setCnam({ identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "", assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "" })
        setPolicyHolder("")
        setFlagged(false)
        setFlagNotes("")
        setMedicalHistoryEntries([])
        setFamilyHistoryEntries([])
      }
    }
    conflict.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [patient?.id, open])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setErrors({})
      setMedicalHistoryEntries([])
      setFamilyHistoryEntries([])
    }
  }, [open])

  // Load medical history entries
  const loadMedicalHistoryEntries = async (patientId: string) => {
    try {
      const entries = await patientMedicalHistoryApi.list(patientId)
      setMedicalHistoryEntries(entries.map(e => ({
        id: e.id,
        description: e.description,
        date: e.date,
        notes: e.notes,
      })))
    } catch (err) {
      console.error("Failed to load medical history:", err)
      setMedicalHistoryEntries([])
    }
  }

  // Load family history entries
  const loadFamilyHistoryEntries = async (patientId: string) => {
    try {
      const entries = await patientFamilyHistoryApi.list(patientId)
      setFamilyHistoryEntries(entries.map(e => ({
        id: e.id,
        relationship: e.relationship,
        condition: e.condition,
        notes: e.notes,
      })))
    } catch (err) {
      console.error("Failed to load family history:", err)
      setFamilyHistoryEntries([])
    }
  }

  // Add new medical history entry
  const addMedicalHistoryEntry = () => {
    setMedicalHistoryEntries([...medicalHistoryEntries, {
      description: "",
      date: "",
      notes: "",
      isNew: true,
    }])
  }

  // Update medical history entry
  const updateMedicalHistoryEntry = (index: number, field: 'description' | 'date' | 'notes', value: string) => {
    const updated = [...medicalHistoryEntries]
    updated[index] = { ...updated[index], [field]: value }
    setMedicalHistoryEntries(updated)
  }

  // Remove medical history entry
  const removeMedicalHistoryEntry = async (index: number) => {
    const entry = medicalHistoryEntries[index]
    if (entry.id && patient) {
      // Delete from API if it exists
      try {
        await patientMedicalHistoryApi.delete(patient.id, entry.id)
      } catch (err) {
        console.error("Failed to delete medical history entry:", err)
        toast.error("Échec de la suppression", {
          description: "Impossible de supprimer l'entrée d'historique médical. Veuillez réessayer.",
          duration: 4000,
        })
        return
      }
    }
    setMedicalHistoryEntries(medicalHistoryEntries.filter((_, i) => i !== index))
  }

  // Add new family history entry
  const addFamilyHistoryEntry = () => {
    setFamilyHistoryEntries([...familyHistoryEntries, {
      relationship: "",
      condition: "",
      notes: "",
      isNew: true,
    }])
  }

  // Update family history entry
  const updateFamilyHistoryEntry = (index: number, field: 'relationship' | 'condition' | 'notes', value: string) => {
    const updated = [...familyHistoryEntries]
    updated[index] = { ...updated[index], [field]: value }
    setFamilyHistoryEntries(updated)
  }

  // Remove family history entry
  const removeFamilyHistoryEntry = async (index: number) => {
    const entry = familyHistoryEntries[index]
    if (entry.id && patient) {
      // Delete from API if it exists
      try {
        await patientFamilyHistoryApi.delete(patient.id, entry.id)
      } catch (err) {
        console.error("Failed to delete family history entry:", err)
        toast.error("Échec de la suppression", {
          description: "Impossible de supprimer l'entrée d'historique familial. Veuillez réessayer.",
          duration: 4000,
        })
        return
      }
    }
    setFamilyHistoryEntries(familyHistoryEntries.filter((_, i) => i !== index))
  }

  const validateEmail = (email: string) => {
    if (!email) return true // Optional field
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
    return emailRegex.test(email)
  }

  const validateForm = () => {
    const newErrors: Record<string, string> = {}

    if (!firstName.trim()) {
      newErrors.firstName = "Le prénom est requis"
    }

    if (!lastName.trim()) {
      newErrors.lastName = "Le nom est requis"
    }

    if (!gender) {
      newErrors.gender = "Le sexe est requis"
    }

    if (!birthdate) {
      newErrors.birthdate = "La date de naissance est requise"
    }

    // The phone is optional — a walk-in who does not give one is an ordinary patient. A NON-BLANK number is
    // still held to the reminder engine's rule, so anything accepted here can actually be delivered to.
    if (phone.trim() && !isDeliverablePhone(phone.trim())) {
      newErrors.phone = PHONE_ERROR_FR
    }

    if (email && !validateEmail(email)) {
      newErrors.email = "Format d'email invalide"
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!validateForm()) {
      toast.error("Erreurs dans le formulaire", {
        description: "Veuillez vérifier que tous les champs requis sont correctement remplis",
        duration: 4000,
      })
      return
    }

    setLoading(true)

    try {
      // Build address object from separate fields
      let addressObj = undefined
      if (addressStreet.trim() || addressCity.trim() || addressGovernorate.trim() || addressPostalCode.trim()) {
        addressObj = {
          street: addressStreet.trim() || "",
          city: addressCity.trim() || "",
          state: addressGovernorate.trim() || "",
          zipCode: addressPostalCode.trim() || "",
          country: "Tunisia" // Default country, can be made configurable
        }
      }

      let savedPatient: PatientDto

      if (patient) {
        // Edit mode: Update existing patient
        const updateData: Partial<PatientDto> & { isFlagged?: boolean; flagNotes?: string } = {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          gender,
          dateOfBirth: birthdate,
          // Explicit null, not undefined: the command is tri-state, so undefined would be read as
          // "leave it alone" and clearing the box would silently do nothing.
          phoneNumber: phone.trim() || null,
          email: email.trim() || null,
          // The version this form was hydrated from — so a peer's save in the meantime is a 409, not a
          // silent overwrite of their work.
          version: patient.version,
          address: addressObj,
          emergencyContactName: emergencyName.trim(),
          emergencyContactPhone: emergencyPhone.trim(),
          medicalHistory: chronicDiseases.trim() || undefined,
          allergies: allergies.trim() || undefined,
          insuranceInfo: (insuranceProvider.trim() || insuranceNumber.trim()) ? {
            provider: insuranceProvider.trim() || "Unknown",
            policyNumber: insuranceNumber.trim() || "Unknown",
            groupNumber: policyHolder.trim() || undefined,
          } : undefined,
          cnamInfo: {
            identifiantUnique: cnam.identifiantUnique.trim() || null,
            regime: cnam.regime.trim() || null,
            assureFirstName: cnam.assureFirstName.trim() || null,
            assureLastName: cnam.assureLastName.trim() || null,
            assureAddress: cnam.assureAddress.trim() || null,
            assurePostalCode: cnam.assurePostalCode.trim() || null,
            maladeLien: cnam.maladeLien.trim() || null,
            maladeLienRang: cnam.maladeLienRang.trim() || null,
          },
          // "Signaler ce patient" toggle: true ensures an active flag, false clears it.
          isFlagged: flagged,
          flagNotes: flagNotes.trim() || undefined,
        }

        await patientsApi.update(patient.id, updateData)
        savedPatient = { ...patient, ...updateData } as PatientDto

        // Save medical history entries
        for (const entry of medicalHistoryEntries) {
          if (entry.description.trim()) {
            if (entry.id && !entry.isNew) {
              // Update existing entry
              await patientMedicalHistoryApi.update(patient.id, entry.id, {
                description: entry.description.trim(),
                date: entry.date || undefined,
                notes: entry.notes?.trim() || undefined,
              })
            } else {
              // Create new entry
              await patientMedicalHistoryApi.create(patient.id, {
                description: entry.description.trim(),
                date: entry.date || undefined,
                notes: entry.notes?.trim() || undefined,
              })
            }
          }
        }

        // Save family history entries
        for (const entry of familyHistoryEntries) {
          if (entry.relationship.trim() && entry.condition.trim()) {
            if (entry.id && !entry.isNew) {
              // Update existing entry
              await patientFamilyHistoryApi.update(patient.id, entry.id, {
                relationship: entry.relationship.trim(),
                condition: entry.condition.trim(),
                notes: entry.notes?.trim() || undefined,
              })
            } else {
              // Create new entry
              await patientFamilyHistoryApi.create(patient.id, {
                relationship: entry.relationship.trim(),
                condition: entry.condition.trim(),
                notes: entry.notes?.trim() || undefined,
              })
            }
          }
        }

        toast.success("Informations patient mises à jour", {
          description: "Les modifications ont été enregistrées avec succès",
          duration: 3000,
        })
      } else {
        // Create mode: Create new patient with all fields including history entries
        const medicalHistoryEntriesToSend = medicalHistoryEntries
          .filter(entry => entry.description.trim())
          .map(entry => ({
            description: entry.description.trim(),
            date: entry.date || undefined,
            notes: entry.notes?.trim() || undefined,
          }));

        const familyHistoryEntriesToSend = familyHistoryEntries
          .filter(entry => entry.relationship.trim() && entry.condition.trim())
          .map(entry => ({
            relationship: entry.relationship.trim(),
            condition: entry.condition.trim(),
            notes: entry.notes?.trim() || undefined,
          }));

        savedPatient = await patientsApi.create({
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          dateOfBirth: birthdate || new Date().toISOString(),
          gender: gender || "Unknown",
          email: email.trim() || null,
          phoneNumber: phone.trim() || null,
          medicalHistory: chronicDiseases.trim() || undefined,
          allergies: allergies.trim() || undefined,
          address: addressObj,
          emergencyContactName: emergencyName.trim() || undefined,
          emergencyContactPhone: emergencyPhone.trim() || undefined,
          insuranceInfo: (insuranceProvider.trim() || insuranceNumber.trim()) ? {
            provider: insuranceProvider.trim() || "Unknown",
            policyNumber: insuranceNumber.trim() || "Unknown",
            groupNumber: policyHolder.trim() || undefined,
          } : undefined,
          cnamInfo: {
            identifiantUnique: cnam.identifiantUnique.trim() || null,
            regime: cnam.regime.trim() || null,
            assureFirstName: cnam.assureFirstName.trim() || null,
            assureLastName: cnam.assureLastName.trim() || null,
            assureAddress: cnam.assureAddress.trim() || null,
            assurePostalCode: cnam.assurePostalCode.trim() || null,
            maladeLien: cnam.maladeLien.trim() || null,
            maladeLienRang: cnam.maladeLienRang.trim() || null,
          },
          medicalHistoryEntries: medicalHistoryEntriesToSend.length > 0 ? medicalHistoryEntriesToSend : undefined,
          familyHistoryEntries: familyHistoryEntriesToSend.length > 0 ? familyHistoryEntriesToSend : undefined,
          isFlagged: flagged,
          flagNotes: flagNotes.trim() || undefined,
        })

        toast.success("Patient créé avec succès", {
          description: "Le nouveau patient a été ajouté avec succès",
          duration: 3000,
        })
      }

      onSuccess?.(savedPatient)
      onOpenChange(false)
    } catch (err) {
      console.error("Failed to save patient:", err)
      const fallback = patient
        ? "Échec de la mise à jour des informations du patient"
        : "Échec de la création du patient"
      // A conflict stays on screen with a reload; anything else keeps the familiar toast as well, since
      // those are usually transient and the user may already have looked away.
      if (!conflict.capture(err, fallback)) {
        toast.error(patient ? "Erreur lors de la mise à jour" : "Erreur lors de la création", {
          description: conflict.error ?? fallback,
          duration: 4000,
        })
      }
    } finally {
      setLoading(false)
    }
  }

  const fullName = `${firstName} ${lastName}`.trim()
  const hasActiveFlags = patient?.flags && patient.flags.some(flag => flag.isActive)

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl max-h-[90vh] p-0 gap-0">
        <DialogHeader className="p-6 pb-4">
          <div className="flex items-start justify-between">
            <div>
              <DialogTitle className="text-2xl">
                {patient ? "Modifier les informations du patient" : "Ajouter un patient"}
              </DialogTitle>
              <DialogDescription className="mt-1">
                {patient
                  ? "Mettez à jour toutes les informations du patient, y compris les antécédents médicaux et l'assurance"
                  : "Saisissez les informations du patient pour créer un nouveau dossier"}
              </DialogDescription>
            </div>
            {hasActiveFlags && (
              <Badge variant="destructive" className="gap-1">
                <Flag className="h-3 w-3" />
                Signalé
              </Badge>
            )}
          </div>
        </DialogHeader>

        <Separator />

        <div className="overflow-y-auto max-h-[calc(90vh-200px)]">
          <form onSubmit={handleSave} className="p-6 space-y-6">
            <FormErrorBanner message={conflict.error} />
            {/* Personal Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <User className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Informations personnelles</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* First Name */}
                <div className="space-y-2">
                  <Label htmlFor="firstName">
                    Prénom <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="firstName"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    placeholder="John"
                    aria-invalid={!!errors.firstName}
                    className={cn(errors.firstName && "border-destructive")}
                  />
                  {errors.firstName && <p className="text-sm text-destructive">{errors.firstName}</p>}
                </div>

                {/* Last Name */}
                <div className="space-y-2">
                  <Label htmlFor="lastName">
                    Nom <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="lastName"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    placeholder="Doe"
                    aria-invalid={!!errors.lastName}
                    className={cn(errors.lastName && "border-destructive")}
                  />
                  {errors.lastName && <p className="text-sm text-destructive">{errors.lastName}</p>}
                </div>

                {/* Gender */}
                <div className="space-y-2">
                  <Label htmlFor="gender">
                    Sexe <span className="text-destructive">*</span>
                  </Label>
                  <Select value={gender} onValueChange={setGender}>
                    <SelectTrigger id="gender" className={cn("w-full", errors.gender && "border-destructive")}>
                      <SelectValue placeholder="Sélectionner le sexe" />
                    </SelectTrigger>
                    <SelectContent>
                      {/* AC-P1.45: values stay the English storage keys; labels come from the shared map. */}
                      {SELECTABLE_GENDERS.map((g) => (
                        <SelectItem key={g} value={g}>
                          {genderLabel(g)}
                        </SelectItem>
                      ))}
                      {/* AC-P1.46: an existing "Unknown" row hydrated the Select with a value no option
                          matched, so the trigger fell back to the placeholder and looked unset. */}
                      {gender && !SELECTABLE_GENDERS.includes(gender as (typeof SELECTABLE_GENDERS)[number]) && (
                        <SelectItem value={gender}>{genderLabel(gender)}</SelectItem>
                      )}
                    </SelectContent>
                  </Select>
                  {errors.gender && <p className="text-sm text-destructive">{errors.gender}</p>}
                </div>

                {/* Date of Birth */}
                <div className="space-y-2">
                  <Label htmlFor="birthdate">
                    Date de naissance <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="birthdate"
                    type="date"
                    value={birthdate}
                    onChange={(e) => setBirthdate(e.target.value)}
                    aria-invalid={!!errors.birthdate}
                    className={cn(errors.birthdate && "border-destructive")}
                  />
                  {errors.birthdate && <p className="text-sm text-destructive">{errors.birthdate}</p>}
                </div>
              </div>
            </div>

            {/* Contact Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <Phone className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Coordonnées</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Phone */}
                <div className="space-y-2">
                  <Label htmlFor="phone">
                    Numéro de téléphone{" "}
                    <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="phone"
                    type="tel"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    placeholder="Ex. 20 123 456 (ou +216…)"
                    aria-invalid={!!errors.phone}
                    className={cn(errors.phone && "border-destructive")}
                  />
                  {errors.phone && <p className="text-sm text-destructive">{errors.phone}</p>}
                  {/* Optional does not mean consequence-free. Saying it here beats a neutral blank the user
                      only understands weeks later, when the patient misses an appointment. */}
                  {!phone.trim() && !errors.phone && (
                    <p className="text-xs text-muted-foreground">
                      Sans numéro de téléphone, ce patient ne recevra ni rappel ni relance.
                    </p>
                  )}
                </div>

                {/* Email */}
                <div className="space-y-2">
                  <Label htmlFor="email">
                    Email <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="email"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="john.doe@email.com"
                    aria-invalid={!!errors.email}
                    className={cn(errors.email && "border-destructive")}
                  />
                  {errors.email && <p className="text-sm text-destructive">{errors.email}</p>}
                </div>

                {/* Address - Street */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="addressStreet">Adresse</Label>
                  <Input
                    id="addressStreet"
                    value={addressStreet}
                    onChange={(e) => setAddressStreet(e.target.value)}
                    placeholder="123 Main Street"
                  />
                </div>

                {/* Governorate (finding #17: Tunisian dropdown, not free text) */}
                <div className="space-y-2">
                  <Label htmlFor="addressGovernorate">Gouvernorat</Label>
                  <Select value={addressGovernorate} onValueChange={setAddressGovernorate}>
                    <SelectTrigger id="addressGovernorate">
                      <SelectValue placeholder="Sélectionner un gouvernorat" />
                    </SelectTrigger>
                    <SelectContent>
                      {TUNISIAN_GOVERNORATES.map((gov) => (
                        <SelectItem key={gov} value={gov}>{gov}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                {/* City */}
                <div className="space-y-2">
                  <Label htmlFor="addressCity">Ville</Label>
                  <Input
                    id="addressCity"
                    value={addressCity}
                    onChange={(e) => setAddressCity(e.target.value)}
                    placeholder="Tunis"
                  />
                </div>

                {/* Postal Code */}
                <div className="space-y-2">
                  <Label htmlFor="addressPostalCode">
                    Code postal <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="addressPostalCode"
                    value={addressPostalCode}
                    onChange={(e) => setAddressPostalCode(e.target.value)}
                    placeholder="1000"
                  />
                </div>

                {/* Emergency contact (finding #11) */}
                <div className="space-y-2">
                  <Label htmlFor="emergencyName">
                    Contact d'urgence <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="emergencyName"
                    value={emergencyName}
                    onChange={(e) => setEmergencyName(e.target.value)}
                    placeholder="Nom du contact"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="emergencyPhone">
                    Téléphone d'urgence <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="emergencyPhone"
                    value={emergencyPhone}
                    onChange={(e) => setEmergencyPhone(e.target.value)}
                    placeholder="+216 ..."
                  />
                </div>
              </div>
            </div>

            {/* Medical Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <Heart className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Informations médicales</h3>
              </div>

              <div className="grid grid-cols-1 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Chronic Diseases */}
                <div className="space-y-2">
                  <Label htmlFor="chronicDiseases">Maladies chroniques / affections</Label>
                  <Textarea
                    id="chronicDiseases"
                    value={chronicDiseases}
                    onChange={(e) => setChronicDiseases(e.target.value)}
                    placeholder="Hypertension, Type 2 Diabetes"
                    className="min-h-[60px] resize-none"
                  />
                </div>

                {/* Allergies */}
                <div className="space-y-2">
                  <Label htmlFor="allergies">Allergies</Label>
                  <Textarea
                    id="allergies"
                    value={allergies}
                    onChange={(e) => setAllergies(e.target.value)}
                    placeholder="Penicillin, Shellfish"
                    className="min-h-[60px] resize-none"
                  />
                </div>

                {/* Medical History (replaces Past Surgeries) */}
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <Label>Antécédents médicaux</Label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addMedicalHistoryEntry}
                      className="gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Ajouter une entrée
                    </Button>
                  </div>
                  
                  {medicalHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Aucun antécédent médical. Cliquez sur « Ajouter une entrée » pour en ajouter un.</p>
                  ) : (
                    <div className="space-y-3">
                      {medicalHistoryEntries.map((entry, index) => (
                        <div key={index} className="p-3 border rounded-lg space-y-2 bg-background">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex-1 space-y-2">
                              <Input
                                placeholder="Description (ex. : appendicectomie, chirurgie du genou)"
                                value={entry.description}
                                onChange={(e) => updateMedicalHistoryEntry(index, 'description', e.target.value)}
                                className="w-full"
                              />
                              <div className="grid grid-cols-2 gap-2">
                                <Input
                                  type="date"
                                  placeholder="Date (optionnel)"
                                  value={entry.date || ""}
                                  onChange={(e) => updateMedicalHistoryEntry(index, 'date', e.target.value)}
                                />
                                <Input
                                  placeholder="Notes (optionnel)"
                                  value={entry.notes || ""}
                                  onChange={(e) => updateMedicalHistoryEntry(index, 'notes', e.target.value)}
                                />
                              </div>
                            </div>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => removeMedicalHistoryEntry(index)}
                              className="text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Family Medical History */}
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <Label>Antécédents familiaux</Label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addFamilyHistoryEntry}
                      className="gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Ajouter une entrée
                    </Button>
                  </div>
                  
                  {familyHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Aucun antécédent familial. Cliquez sur « Ajouter une entrée » pour en ajouter un.</p>
                  ) : (
                    <div className="space-y-3">
                      {familyHistoryEntries.map((entry, index) => (
                        <div key={index} className="p-3 border rounded-lg space-y-2 bg-background">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex-1 space-y-2">
                              <div className="grid grid-cols-2 gap-2">
                                <Input
                                  placeholder="Lien de parenté (ex. : père, mère)"
                                  value={entry.relationship}
                                  onChange={(e) => updateFamilyHistoryEntry(index, 'relationship', e.target.value)}
                                />
                                <Input
                                  placeholder="Affection (ex. : maladie cardiaque, diabète)"
                                  value={entry.condition}
                                  onChange={(e) => updateFamilyHistoryEntry(index, 'condition', e.target.value)}
                                />
                              </div>
                              <Input
                                placeholder="Notes (optionnel)"
                                value={entry.notes || ""}
                                onChange={(e) => updateFamilyHistoryEntry(index, 'notes', e.target.value)}
                              />
                            </div>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => removeFamilyHistoryEntry(index)}
                              className="text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* CNAM Identity Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <CreditCard className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Identité CNAM</h3>
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                <div className="space-y-2">
                  <Label htmlFor="cnamIdentifiant">Identifiant Unique</Label>
                  <Input id="cnamIdentifiant" value={cnam.identifiantUnique} onChange={(e) => setCnam({ ...cnam, identifiantUnique: e.target.value })} placeholder="Ex: 12345678" />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamRegime">Régime</Label>
                  <Select value={cnam.regime || undefined} onValueChange={(v) => setCnam({ ...cnam, regime: v })}>
                    <SelectTrigger id="cnamRegime"><SelectValue placeholder="Choisir…" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="CNSS">CNSS</SelectItem>
                      <SelectItem value="CNRPS">CNRPS</SelectItem>
                      <SelectItem value="Convention bilatérale">Convention bilatérale</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamMaladeLien">Lien du malade à l'assuré</Label>
                  <Select value={cnam.maladeLien || undefined} onValueChange={(v) => setCnam({ ...cnam, maladeLien: v })}>
                    <SelectTrigger id="cnamMaladeLien"><SelectValue placeholder="Choisir…" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="Assuré lui-même">Assuré lui-même</SelectItem>
                      <SelectItem value="Conjoint">Conjoint</SelectItem>
                      <SelectItem value="Enfant">Enfant</SelectItem>
                      <SelectItem value="Ascendant">Ascendant</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamRang">Rang enfant / Père-Mère (ascendant)</Label>
                  <Input id="cnamRang" value={cnam.maladeLienRang} onChange={(e) => setCnam({ ...cnam, maladeLienRang: e.target.value })} placeholder="Ex: 1 — ou père/mère" />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamAssureFirst">Prénom de l'assuré</Label>
                  <Input id="cnamAssureFirst" value={cnam.assureFirstName} onChange={(e) => setCnam({ ...cnam, assureFirstName: e.target.value })} placeholder="Si différent du patient" />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamAssureLast">Nom de l'assuré</Label>
                  <Input id="cnamAssureLast" value={cnam.assureLastName} onChange={(e) => setCnam({ ...cnam, assureLastName: e.target.value })} placeholder="Si différent du patient" />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamAssureAddr">Adresse de l'assuré</Label>
                  <Input id="cnamAssureAddr" value={cnam.assureAddress} onChange={(e) => setCnam({ ...cnam, assureAddress: e.target.value })} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamAssureCp">Code postal de l'assuré</Label>
                  <Input id="cnamAssureCp" value={cnam.assurePostalCode} onChange={(e) => setCnam({ ...cnam, assurePostalCode: e.target.value })} />
                </div>
              </div>
            </div>

            {/* Insurance Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <CreditCard className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Informations d'assurance</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Insurance Provider */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceProvider">Assureur</Label>
                  <Input
                    id="insuranceProvider"
                    value={insuranceProvider}
                    onChange={(e) => setInsuranceProvider(e.target.value)}
                    placeholder="Blue Cross Blue Shield"
                  />
                </div>

                {/* Insurance Number */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceNumber">Numéro d'assurance / d'identification</Label>
                  <Input
                    id="insuranceNumber"
                    value={insuranceNumber}
                    onChange={(e) => setInsuranceNumber(e.target.value)}
                    placeholder="BCBS-123456789"
                  />
                </div>

                {/* Policy Holder */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="policyHolder">Numéro de groupe</Label>
                  <Input
                    id="policyHolder"
                    value={policyHolder}
                    onChange={(e) => setPolicyHolder(e.target.value)}
                    placeholder="Group-12345"
                  />
                </div>
              </div>
            </div>

            {/* Flags Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <Flag className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Signalements du patient</h3>
              </div>

              <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <Label htmlFor="flagged" className="cursor-pointer">
                      Signaler ce patient pour une attention particulière
                    </Label>
                    <p className="text-sm text-muted-foreground">
                      Marquez les patients qui nécessitent une attention médicale particulière ou présentent un état critique
                    </p>
                  </div>
                  <Switch id="flagged" checked={flagged} onCheckedChange={setFlagged} />
                </div>

                {flagged && (
                  <div className="space-y-2 pt-2">
                    <Label htmlFor="flagNotes">Notes de signalement</Label>
                    <Textarea
                      id="flagNotes"
                      value={flagNotes}
                      onChange={(e) => setFlagNotes(e.target.value)}
                      placeholder="Motif du signalement (ex. : patient à haut risque, allergies sévères, etc.)"
                      className="min-h-[60px] resize-none"
                    />
                  </div>
                )}
              </div>
            </div>
          </form>
        </div>

        <Separator />

        <DialogFooter className="p-6 pt-4">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
            <X className="h-4 w-4 mr-2" />
            Annuler
          </Button>
          <Button type="submit" onClick={handleSave} disabled={loading}>
            <Save className="h-4 w-4 mr-2" />
            {loading
              ? (patient ? "Enregistrement…" : "Création…")
              : (patient ? "Enregistrer les modifications" : "Créer le patient")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

