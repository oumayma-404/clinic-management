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
import { Switch } from "@/components/ui/switch"
import { Separator } from "@/components/ui/separator"
import { Badge } from "@/components/ui/badge"
import { toast } from "sonner"
import { User, Phone, Heart, CreditCard, Flag, Save, X, Plus, Trash2 } from "lucide-react"
import { cn } from "@/lib/utils"
import { patientsApi } from "@/lib/api/patients"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import type { PatientDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { isDeliverablePhone, PHONE_ERROR_FR } from "@/lib/phone"

interface EditPatientDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patient: PatientDto | null
  onSuccess?: () => void
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
  const [errors, setErrors] = useState<Record<string, string>>({})

  // Populate form when patient changes or dialog opens
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
  }, [patient, open])

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
      newErrors.firstName = "First name is required"
    }

    if (!lastName.trim()) {
      newErrors.lastName = "Last name is required"
    }

    if (!gender) {
      newErrors.gender = "Gender is required"
    }

    if (!birthdate) {
      newErrors.birthdate = "Date of birth is required"
    }

    if (!phone.trim()) {
      newErrors.phone = "Numéro de téléphone requis"
    } else if (!isDeliverablePhone(phone.trim())) {
      // AC-5: match the reminder engine's rule so a number accepted here can actually receive reminders.
      newErrors.phone = PHONE_ERROR_FR
    }

    if (email && !validateEmail(email)) {
      newErrors.email = "Invalid email format"
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
        const updateData: Partial<PatientDto> = {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          gender,
          dateOfBirth: birthdate,
          phoneNumber: phone.trim(),
          email: email.trim() || undefined,
          address: addressObj,
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
          email: email.trim() || "",
          phoneNumber: phone.trim() || "",
          medicalHistory: chronicDiseases.trim() || undefined,
          allergies: allergies.trim() || undefined,
          address: addressObj,
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
        })

        toast.success("Patient créé avec succès", {
          description: "Le nouveau patient a été ajouté avec succès",
          duration: 3000,
        })
      }

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      console.error("Failed to save patient:", err)
      const errorMessage = err instanceof ApiError ? err.message : (patient ? "Failed to update patient information" : "Failed to create patient")
      toast.error(patient ? "Erreur lors de la mise à jour" : "Erreur lors de la création", {
        description: errorMessage,
        duration: 4000,
      })
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
                {patient ? "Edit Patient Information" : "Add New Patient"}
              </DialogTitle>
              <DialogDescription className="mt-1">
                {patient 
                  ? "Update all patient details including medical history and insurance information"
                  : "Enter patient information to create a new patient record"}
              </DialogDescription>
            </div>
            {hasActiveFlags && (
              <Badge variant="destructive" className="gap-1">
                <Flag className="h-3 w-3" />
                Flagged
              </Badge>
            )}
          </div>
        </DialogHeader>

        <Separator />

        <div className="overflow-y-auto max-h-[calc(90vh-200px)]">
          <form onSubmit={handleSave} className="p-6 space-y-6">
            {/* Personal Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <User className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Personal Information</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* First Name */}
                <div className="space-y-2">
                  <Label htmlFor="firstName">
                    First Name <span className="text-destructive">*</span>
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
                    Last Name <span className="text-destructive">*</span>
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
                    Gender <span className="text-destructive">*</span>
                  </Label>
                  <Select value={gender} onValueChange={setGender}>
                    <SelectTrigger id="gender" className={cn("w-full", errors.gender && "border-destructive")}>
                      <SelectValue placeholder="Select gender" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="Male">Male</SelectItem>
                      <SelectItem value="Female">Female</SelectItem>
                      <SelectItem value="Other">Other</SelectItem>
                    </SelectContent>
                  </Select>
                  {errors.gender && <p className="text-sm text-destructive">{errors.gender}</p>}
                </div>

                {/* Date of Birth */}
                <div className="space-y-2">
                  <Label htmlFor="birthdate">
                    Date of Birth <span className="text-destructive">*</span>
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
                <h3 className="text-lg font-semibold">Contact Information</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Phone */}
                <div className="space-y-2">
                  <Label htmlFor="phone">
                    Phone Number <span className="text-destructive">*</span>
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
                </div>

                {/* Email */}
                <div className="space-y-2">
                  <Label htmlFor="email">
                    Email <span className="text-muted-foreground text-xs">(Optional)</span>
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
                  <Label htmlFor="addressStreet">Address</Label>
                  <Input
                    id="addressStreet"
                    value={addressStreet}
                    onChange={(e) => setAddressStreet(e.target.value)}
                    placeholder="123 Main Street"
                  />
                </div>

                {/* Governorate */}
                <div className="space-y-2">
                  <Label htmlFor="addressGovernorate">Governorate</Label>
                  <Input
                    id="addressGovernorate"
                    value={addressGovernorate}
                    onChange={(e) => setAddressGovernorate(e.target.value)}
                    placeholder="Tunis"
                  />
                </div>

                {/* City */}
                <div className="space-y-2">
                  <Label htmlFor="addressCity">City</Label>
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
                    Postal Code <span className="text-muted-foreground text-xs">(Optional)</span>
                  </Label>
                  <Input
                    id="addressPostalCode"
                    value={addressPostalCode}
                    onChange={(e) => setAddressPostalCode(e.target.value)}
                    placeholder="1000"
                  />
                </div>
              </div>
            </div>

            {/* Medical Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <Heart className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">Medical Information</h3>
              </div>

              <div className="grid grid-cols-1 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Chronic Diseases */}
                <div className="space-y-2">
                  <Label htmlFor="chronicDiseases">Chronic Diseases / Conditions</Label>
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
                    <Label>Medical History</Label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addMedicalHistoryEntry}
                      className="gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Add Entry
                    </Button>
                  </div>
                  
                  {medicalHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No medical history entries. Click "Add Entry" to add one.</p>
                  ) : (
                    <div className="space-y-3">
                      {medicalHistoryEntries.map((entry, index) => (
                        <div key={index} className="p-3 border rounded-lg space-y-2 bg-background">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex-1 space-y-2">
                              <Input
                                placeholder="Description (e.g., Appendectomy, Knee Surgery)"
                                value={entry.description}
                                onChange={(e) => updateMedicalHistoryEntry(index, 'description', e.target.value)}
                                className="w-full"
                              />
                              <div className="grid grid-cols-2 gap-2">
                                <Input
                                  type="date"
                                  placeholder="Date (optional)"
                                  value={entry.date || ""}
                                  onChange={(e) => updateMedicalHistoryEntry(index, 'date', e.target.value)}
                                />
                                <Input
                                  placeholder="Notes (optional)"
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
                    <Label>Family Medical History</Label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addFamilyHistoryEntry}
                      className="gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Add Entry
                    </Button>
                  </div>
                  
                  {familyHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No family history entries. Click "Add Entry" to add one.</p>
                  ) : (
                    <div className="space-y-3">
                      {familyHistoryEntries.map((entry, index) => (
                        <div key={index} className="p-3 border rounded-lg space-y-2 bg-background">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex-1 space-y-2">
                              <div className="grid grid-cols-2 gap-2">
                                <Input
                                  placeholder="Relationship (e.g., Father, Mother)"
                                  value={entry.relationship}
                                  onChange={(e) => updateFamilyHistoryEntry(index, 'relationship', e.target.value)}
                                />
                                <Input
                                  placeholder="Condition (e.g., Heart Disease, Diabetes)"
                                  value={entry.condition}
                                  onChange={(e) => updateFamilyHistoryEntry(index, 'condition', e.target.value)}
                                />
                              </div>
                              <Input
                                placeholder="Notes (optional)"
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
                <h3 className="text-lg font-semibold">Insurance Information</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/* Insurance Provider */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceProvider">Insurance Provider</Label>
                  <Input
                    id="insuranceProvider"
                    value={insuranceProvider}
                    onChange={(e) => setInsuranceProvider(e.target.value)}
                    placeholder="Blue Cross Blue Shield"
                  />
                </div>

                {/* Insurance Number */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceNumber">Insurance / ID Number</Label>
                  <Input
                    id="insuranceNumber"
                    value={insuranceNumber}
                    onChange={(e) => setInsuranceNumber(e.target.value)}
                    placeholder="BCBS-123456789"
                  />
                </div>

                {/* Policy Holder */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="policyHolder">Group Number</Label>
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
                <h3 className="text-lg font-semibold">Patient Flags</h3>
              </div>

              <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <Label htmlFor="flagged" className="cursor-pointer">
                      Flag this patient for special attention
                    </Label>
                    <p className="text-sm text-muted-foreground">
                      Mark patients who require special medical attention or have critical conditions
                    </p>
                  </div>
                  <Switch id="flagged" checked={flagged} onCheckedChange={setFlagged} disabled />
                </div>

                {flagged && (
                  <div className="space-y-2 pt-2">
                    <Label htmlFor="flagNotes">Flag Notes</Label>
                    <Textarea
                      id="flagNotes"
                      value={flagNotes}
                      onChange={(e) => setFlagNotes(e.target.value)}
                      placeholder="Reason for flagging (e.g., High risk patient, Severe allergies, etc.)"
                      className="min-h-[60px] resize-none"
                      disabled
                    />
                    <p className="text-xs text-muted-foreground">Flag management is not yet supported by the API</p>
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
            Cancel
          </Button>
          <Button type="submit" onClick={handleSave} disabled={loading}>
            <Save className="h-4 w-4 mr-2" />
            {loading 
              ? (patient ? "Saving..." : "Creating...") 
              : (patient ? "Save Changes" : "Create Patient")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

