"use client"

import { useState, useRef, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import { Card } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import { Printer, RotateCcw, Save, Search, ArrowLeft, FileText, Download, Loader2, Plus, X } from "lucide-react"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useRouter, useParams, useSearchParams } from "next/navigation"
import { patientsApi } from "@/lib/api/patients"
import { appointmentsApi } from "@/lib/api/appointments"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { clinicsApi } from "@/lib/api/clinics"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { cnamNomenclatureApi, estimateReimbursement } from "@/lib/api/cnam-nomenclature"
import type { PatientDto, MedicalDocumentDto, ProcedureTypeDto, DentalRecordDto, CnamNomenclatureEntryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { saveAs } from "file-saver"
import { Document, Packer, Paragraph, HeadingLevel, AlignmentType } from "docx"

// Medication Item Component
function MedicationItem({
  medication,
  onUpdate,
  onRemove
}: {
  medication: { name: string; dosage: string; timesPerDay: string; duration: string }
  onUpdate: (med: { name: string; dosage: string; timesPerDay: string; duration: string }) => void
  onRemove: () => void
}) {
  return (
    <div className="p-4 border rounded-lg space-y-3">
      <div className="grid grid-cols-[1fr_2.5rem] gap-2">
        <div className="space-y-3">
          <div className="flex flex-col gap-2">
            <Label className="text-xs text-muted-foreground h-4">Nom du médicament</Label>
            <Input
              type="text"
              placeholder="Ex: Amoxicilline"
              value={medication.name || ""}
              onChange={(e) => {
                onUpdate({ ...medication, name: e.target.value })
              }}
              className="h-10 w-full"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label className="text-xs text-muted-foreground h-4">Dosage</Label>
            <Input
              type="text"
              placeholder="Ex: 500mg"
              value={medication.dosage || ""}
              onChange={(e) => {
                onUpdate({ ...medication, dosage: e.target.value })
              }}
              className="h-10 w-full"
            />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground h-4">Fois par jour</Label>
              <Input
                type="number"
                min="1"
                placeholder="Ex: 3"
                value={medication.timesPerDay || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, timesPerDay: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground h-4">Durée (jours)</Label>
              <Input
                type="number"
                min="1"
                placeholder="Ex: 7"
                value={medication.duration || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, duration: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
          </div>
        </div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          className="h-10 w-10"
        >
          <X className="w-4 h-4" />
        </Button>
      </div>
    </div>
  )
}

// Procedure Item Component
function ProcedureItem({ 
  procedure, 
  availableProcedures, 
  onUpdate, 
  onRemove
}: { 
  procedure: { name: string; cost: number; procedureTypeId?: string }
  availableProcedures: ProcedureTypeDto[]
  onUpdate: (proc: { name: string; cost: number; procedureTypeId?: string }) => void
  onRemove: () => void
}) {
  const [procedureSearchOpen, setProcedureSearchOpen] = useState(false)
  const [procedureSearchQuery, setProcedureSearchQuery] = useState("")
  
  const selectedProcedure = procedure.procedureTypeId 
    ? availableProcedures.find(p => p.id === procedure.procedureTypeId)
    : null
  
  const filteredProcedures = availableProcedures.filter(p => 
    p.name.toLowerCase().includes(procedureSearchQuery.toLowerCase())
  )
  
  return (
    <div className="p-4 border rounded-lg space-y-3">
      <div className="grid grid-cols-[1fr_1fr_2.5rem] gap-2 items-end">
        <div className="flex flex-col gap-2 min-w-0">
          <Label className="text-xs text-muted-foreground h-4">Nom de la procédure</Label>
          <Popover open={procedureSearchOpen} onOpenChange={setProcedureSearchOpen}>
            <PopoverTrigger asChild>
              <Button
                variant="outline"
                role="combobox"
                aria-expanded={procedureSearchOpen}
                className="w-full justify-between text-left h-10 bg-transparent overflow-hidden"
                type="button"
                onClick={(e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  setProcedureSearchOpen(true);
                }}
              >
                <span className="truncate flex-1 text-left min-w-0">
                  {selectedProcedure ? (
                    <span className="font-medium truncate">{selectedProcedure.name}</span>
                  ) : procedure.name ? (
                    <span className="text-muted-foreground truncate">{procedure.name}</span>
                  ) : (
                    <span className="text-muted-foreground truncate">Sélectionner une procédure...</span>
                  )}
                </span>
                <Search className="ml-2 h-4 w-4 shrink-0 opacity-50 flex-shrink-0" />
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-[384px] p-0 z-50" align="start" onOpenAutoFocus={(e) => e.preventDefault()}>
              <div className="p-2 border-b">
                <div className="relative">
                  <Search className="absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" />
                  <Input
                    placeholder="Rechercher une procédure..."
                    value={procedureSearchQuery}
                    onChange={(e) => setProcedureSearchQuery(e.target.value)}
                    className="pl-8 h-9"
                    autoFocus
                  />
                </div>
              </div>
              <div className="max-h-[300px] overflow-y-auto">
                {filteredProcedures.length === 0 && !procedureSearchQuery ? (
                  <div className="p-8 text-center text-sm text-muted-foreground">
                    Aucune procédure disponible.
                  </div>
                ) : (
                  <div className="p-1">
                    {filteredProcedures.map((p) => (
                      <div
                        key={p.id}
                        onClick={() => {
                          onUpdate({
                            name: p.name,
                            cost: p.defaultCost || 0,
                            procedureTypeId: p.id
                          })
                          setProcedureSearchOpen(false)
                          setProcedureSearchQuery("")
                        }}
                        className="flex items-center justify-between px-3 py-2 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors"
                      >
                        <div className="flex items-center gap-2">
                          <div
                            className="h-3 w-3 rounded-full"
                            style={{ backgroundColor: p.colorHex }}
                          />
                          <span className="font-medium text-sm">{p.name}</span>
                        </div>
                        {p.defaultCost && (
                          <span className="text-xs text-muted-foreground">
                            {p.defaultCost.toFixed(2)} €
                          </span>
                        )}
                      </div>
                    ))}
                    {/* Option to create new procedure if search query doesn't match any existing */}
                    {procedureSearchQuery && filteredProcedures.length === 0 && (
                      <div
                        onClick={() => {
                          onUpdate({
                            name: procedureSearchQuery,
                            cost: procedure.cost || 0,
                            procedureTypeId: undefined // Will be created on save
                          })
                          setProcedureSearchOpen(false)
                          setProcedureSearchQuery("")
                        }}
                        className="flex items-center gap-2 px-3 py-2 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors border-t"
                      >
                        <Plus className="h-4 w-4 text-muted-foreground" />
                        <span className="text-sm text-muted-foreground">
                          Créer "{procedureSearchQuery}" (sera sauvegardé lors de l'enregistrement)
                        </span>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </PopoverContent>
          </Popover>
        </div>
        <div className="flex flex-col gap-2 min-w-0">
          <Label className="text-xs text-muted-foreground h-4">Coût (€)</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            placeholder="0.00"
            value={procedure.cost || ""}
            onChange={(e) => {
              onUpdate({ ...procedure, cost: parseFloat(e.target.value) || 0 })
            }}
            className="h-10 w-full"
          />
        </div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          className="h-10 w-10"
        >
          <X className="w-4 h-4" />
        </Button>
      </div>
    </div>
  )
}

export function DocumentEditorContent() {
  const router = useRouter()
  const params = useParams()
  const searchParams = useSearchParams()
  const documentType = params.type as string
  const urlDocumentId = searchParams.get('id')
  // Post-visit review deep-link: pre-select this appointment's patient and associate the new record with
  // it (so saving marks the appointment Completed). Only used when creating (no urlDocumentId).
  const urlAppointmentId = searchParams.get('appointmentId')

  const [selectedPatient, setSelectedPatient] = useState<string>("")
  const [selectedRecipientDoctorId, setSelectedRecipientDoctorId] = useState<string>("")
  
  const [patientSearchOpen, setPatientSearchOpen] = useState(false)
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [filteredPatients, setFilteredPatients] = useState<PatientDto[]>([])
  const [patientSearchQuery, setPatientSearchQuery] = useState("")
  const [loadingPatients, setLoadingPatients] = useState(false)
  const [saving, setSaving] = useState(false)
  const [documentId, setDocumentId] = useState<string | null>(urlDocumentId)
  const [loadingDocument, setLoadingDocument] = useState(false)

  const [formFields, setFormFields] = useState({
    date: new Date().toISOString().split("T")[0],
    medications: [] as Array<{ name: string; dosage: string; timesPerDay: string; duration: string }>,
    content: "", // Single content field for liaison letters
    procedures: [] as Array<{ name: string; cost: number; procedureTypeId?: string }>, // Array of procedures with costs
    totalCost: "",
    reason: "",
    duration: "",
    notes: "",
    doctorOrderNumber: "", // Numéro d'ordre des médecins
    startDate: "", // Date de début du repos médical
  })
  
  const [availableProcedures, setAvailableProcedures] = useState<ProcedureTypeDto[]>([])
  const [loadingProcedures, setLoadingProcedures] = useState(false)

  // Bulletin de soins CNAM (BS1) — care type + acts table (pre-filled from the patient's dental records).
  const [bulletinFields, setBulletinFields] = useState<{
    careType: string
    apciCode: string
    actsFrom: string
    actsTo: string
    acts: Array<{ date: string; teeth: string; codeActe: string; cotation: string; honoraires: string }>
  }>({ careType: "APCI", apciCode: "", actsFrom: "", actsTo: "", acts: [] })
  const [dentalRecords, setDentalRecords] = useState<DentalRecordDto[]>([])
  const [cnamNomenclature, setCnamNomenclature] = useState<CnamNomenclatureEntryDto[]>([])
  const [openActLookup, setOpenActLookup] = useState<number | null>(null)

  const documentRef = useRef<HTMLDivElement>(null)

  // Load clinic and doctor info
  const { doctors, currentUserDoctor } = useDoctors()
  
  // Get recipient doctor name and specialty from selected doctor
  const recipientDoctor = doctors.find(d => d.id === selectedRecipientDoctorId)
  const recipientDoctorName = recipientDoctor?.name || ""
  const recipientDoctorSpecialty = recipientDoctor?.specialty || ""
  const [clinicInfo, setClinicInfo] = useState<{
    name: string
    address: string
    phone: string
    email: string
  } | null>(null)
  const [loadingClinicInfo, setLoadingClinicInfo] = useState(true)

  // Load clinic information
  useEffect(() => {
    const loadClinicInfo = async () => {
      try {
        setLoadingClinicInfo(true)
        const status = await clinicsApi.getUserStatus()
        if (status.hasClinic && status.clinic) {
          setClinicInfo({
            name: status.clinic.name || "",
            address: status.clinic.address || "",
            phone: status.clinic.phone || "",
            email: status.clinic.email || "",
          })
        }
      } catch (error) {
        console.error("Failed to load clinic info:", error)
      } finally {
        setLoadingClinicInfo(false)
      }
    }
    loadClinicInfo()
  }, [])

  // Get doctor info (current user's doctor or first doctor in list)
  const selectedDoctor = currentUserDoctor || (doctors.length > 0 ? doctors[0] : null)
  
  const formData = {
    doctorName: selectedDoctor?.name || "Dr. [Nom]",
    doctorSpecialty: selectedDoctor?.specialty || "[Spécialité]",
    clinicName: clinicInfo?.name || "[Nom du cabinet]",
    clinicAddress: clinicInfo?.address || "[Adresse]",
    clinicPhone: clinicInfo?.phone || "[Téléphone]",
    clinicEmail: clinicInfo?.email || "",
  }

  // Helper functions
  const calculateAge = (dob: string | undefined) => {
    if (!dob) return null
    try {
      const birthDate = new Date(dob)
      const today = new Date()
      let age = today.getFullYear() - birthDate.getFullYear()
      const monthDiff = today.getMonth() - birthDate.getMonth()
      if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
        age--
      }
      return `${age} ans`
    } catch {
      return null
    }
  }

  const getPatientName = (patient: PatientDto) => {
    return `${patient.firstName} ${patient.lastName}`.trim()
  }

  // Load patients from API
  useEffect(() => {
    const loadPatients = async () => {
      try {
        setLoadingPatients(true)
        const data = await patientsApi.list()
        setPatients(data)
        setFilteredPatients(data)
      } catch (err) {
        console.error("Failed to load patients:", err)
        setPatients([])
        setFilteredPatients([])
      } finally {
        setLoadingPatients(false)
      }
    }
    loadPatients()
  }, [])

  // Post-visit review deep-link: resolve the appointment's patient and pre-select it. Skipped when editing
  // an existing document (that flow sets the patient from the loaded document).
  useEffect(() => {
    if (!urlAppointmentId || urlDocumentId) return
    let cancelled = false
    const preselectFromAppointment = async () => {
      try {
        const appointment = await appointmentsApi.get(urlAppointmentId)
        if (!cancelled && appointment.patientId) {
          setSelectedPatient(appointment.patientId)
        }
      } catch {
        // Non-blocking — the user can still pick the patient manually.
      }
    }
    preselectFromAppointment()
    return () => {
      cancelled = true
    }
  }, [urlAppointmentId, urlDocumentId])

  // Load procedure types from API (for honoraires documents)
  useEffect(() => {
    const loadProcedureTypes = async () => {
      try {
        setLoadingProcedures(true)
        const data = await procedureTypesApi.list(true) // Include inactive procedures
        setAvailableProcedures(data || [])
      } catch (err) {
        console.error("Failed to load procedure types:", err)
        setAvailableProcedures([])
      } finally {
        setLoadingProcedures(false)
      }
    }
    loadProcedureTypes()
  }, [])

  // Filter patients based on search query
  useEffect(() => {
    if (!patientSearchQuery.trim()) {
      setFilteredPatients(patients)
      return
    }

    const query = patientSearchQuery.toLowerCase()
    const filtered = patients.filter((patient) => {
      const patientName = getPatientName(patient).toLowerCase()
      const patientAge = calculateAge(patient.dateOfBirth)?.toLowerCase() || ""
      return patientName.includes(query) || patientAge.includes(query) || patient.id.toLowerCase().includes(query)
    })
    setFilteredPatients(filtered)
  }, [patientSearchQuery, patients])

  // Load document for editing if ID is present in URL
  useEffect(() => {
    if (urlDocumentId && urlDocumentId !== documentId) {
      const loadDocument = async () => {
        try {
          setLoadingDocument(true)
          const doc = await medicalDocumentsApi.get(urlDocumentId)
          setDocumentId(doc.id)
          setSelectedPatient(doc.patientId)
          // Try to find doctor by name (will be set when doctors are loaded)
          if (doc.recipientDoctorName) {
            // Wait for doctors to load, then find the doctor
            if (doctors.length > 0) {
              const doctor = doctors.find(d => d.name === doc.recipientDoctorName)
              if (doctor) {
                setSelectedRecipientDoctorId(doctor.id || "")
              } else {
                setSelectedRecipientDoctorId("")
              }
            }
          } else {
            setSelectedRecipientDoctorId("")
          }
          
          // Parse and set form fields from contentJson
          const content = JSON.parse(doc.contentJson)
          
          // Handle medications: support both old string format and new array format
          let medications: Array<{ name: string; dosage: string; timesPerDay: string; duration: string }> = []
          if (Array.isArray(content.medications)) {
            medications = content.medications
          } else if (typeof content.medications === 'string' && content.medications.trim()) {
            // Try to parse old format (backward compatibility)
            // For old format, we'll create a single medication entry
            medications = [{ name: content.medications, dosage: "", timesPerDay: "", duration: "" }]
          }
          
          setFormFields({
            date: content.date || new Date(doc.documentDate).toISOString().split("T")[0],
            medications: medications,
            content: content.content || content.diagnosis || content.treatment || content.recommendations || "", // Support both old and new format
            procedures: Array.isArray(content.procedures) ? content.procedures : (content.procedures ? [{ name: content.procedures, cost: parseFloat(content.totalCost?.replace(/[^\d,.-]/g, '').replace(',', '.') || "0") || 0 }] : []),
            totalCost: content.totalCost || (Array.isArray(content.procedures) && content.procedures.length > 0 
              ? (content.procedures.reduce((sum: number, proc: any) => sum + (proc.cost || 0), 0).toFixed(2).replace('.', ',') + ' €')
              : ""),
            reason: content.reason || "",
            duration: content.duration || "",
            notes: content.notes || "",
            doctorOrderNumber: content.doctorOrderNumber || "",
            startDate: content.startDate || "",
          })

          // Bulletin CNAM: restore care type + acts (acts stored as a JSON string in ContentJson).
          if (documentType === "bulletin-cnam") {
            let parsedActs: Array<{ date: string; teeth: string; codeActe: string; cotation: string; honoraires: string }> = []
            try {
              parsedActs = typeof content.acts === "string" ? JSON.parse(content.acts) : (Array.isArray(content.acts) ? content.acts : [])
            } catch {
              parsedActs = []
            }
            setBulletinFields({
              careType: content.careType || "APCI",
              apciCode: content.apciCode || "",
              actsFrom: "",
              actsTo: "",
              acts: Array.isArray(parsedActs) ? parsedActs : [],
            })
          }
        } catch (error) {
          console.error("Failed to load document for editing:", error)
          toast.error("Échec du chargement du document", {
            description: "Impossible de charger le document pour modification. Veuillez réessayer.",
            duration: 4000,
          })
        } finally {
          setLoadingDocument(false)
        }
      }
      loadDocument()
    }
  }, [urlDocumentId, documentId, doctors])

  // Auto-calculate total cost from procedures for honoraires documents
  useEffect(() => {
    if (documentType === "honoraires" && Array.isArray(formFields.procedures)) {
      const total = formFields.procedures.reduce((sum, proc) => sum + (proc.cost || 0), 0)
      const formattedTotal = total.toFixed(2).replace('.', ',') + ' €'
      if (formFields.totalCost !== formattedTotal) {
        setFormFields(prev => ({ ...prev, totalCost: formattedTotal }))
      }
    }
  }, [formFields.procedures, documentType, formFields.totalCost])

  // Load the selected patient's dental records — the source for pre-filling the CNAM bulletin acts table.
  useEffect(() => {
    if (documentType !== "bulletin-cnam" || !selectedPatient) {
      setDentalRecords([])
      return
    }
    let cancelled = false
    ;(async () => {
      try {
        const records = await dentalRecordsApi.list(selectedPatient)
        if (!cancelled) setDentalRecords(records)
      } catch {
        if (!cancelled) setDentalRecords([])
      }
    })()
    return () => { cancelled = true }
  }, [documentType, selectedPatient])

  // Load the curated CNAM nomenclature once when editing a bulletin (full list; searched client-side).
  useEffect(() => {
    if (documentType !== "bulletin-cnam") return
    let cancelled = false
    ;(async () => {
      try {
        const entries = await cnamNomenclatureApi.list()
        if (!cancelled) setCnamNomenclature(entries)
      } catch {
        if (!cancelled) setCnamNomenclature([])
      }
    })()
    return () => { cancelled = true }
  }, [documentType])

  const resetForm = () => {
    setSelectedPatient("")
    setSelectedRecipientDoctorId("")
    setDocumentId(null)
    setFormFields({
      date: new Date().toISOString().split("T")[0],
      medications: [],
      content: "",
      procedures: [],
      totalCost: "",
    reason: "",
    duration: "",
    notes: "",
    doctorOrderNumber: "", // Numéro d'ordre des médecins
    startDate: "", // Date de début du repos médical
  })
    setBulletinFields({ careType: "APCI", apciCode: "", actsFrom: "", actsTo: "", acts: [] })
  }

  // ---- CNAM bulletin helpers ----
  // Pre-fill the acts table from the patient's dental records within the chosen date range. Code acte +
  // Cotation are left blank for the doctor to fill (or pick from the nomenclature); honoraires = record cost.
  const prefillActsFromRecords = () => {
    const from = bulletinFields.actsFrom ? new Date(bulletinFields.actsFrom) : null
    const to = bulletinFields.actsTo ? new Date(bulletinFields.actsTo) : null
    const inRange = dentalRecords.filter((r) => {
      if (!r.interventionDate) return true
      const d = new Date(r.interventionDate)
      if (from && d < from) return false
      if (to && d > to) return false
      return true
    })
    const acts = inRange.map((r) => ({
      date: r.interventionDate ? r.interventionDate.split("T")[0] : "",
      teeth: (r.toothNumbers || []).join(", "),
      codeActe: "",
      cotation: "",
      honoraires: r.cost != null ? r.cost.toFixed(3) : "",
    }))
    setBulletinFields((prev) => ({ ...prev, acts }))
  }

  const updateBulletinAct = (
    index: number,
    field: "date" | "teeth" | "codeActe" | "cotation" | "honoraires",
    value: string,
  ) => {
    setBulletinFields((prev) => ({
      ...prev,
      acts: prev.acts.map((act, i) => (i === index ? { ...act, [field]: value } : act)),
    }))
  }

  // Pick a catalog act: fills Code acte + Cotation ("<lettreCle> <coefficient>"). Both stay editable.
  const selectNomenclatureEntry = (index: number, entry: CnamNomenclatureEntryDto) => {
    setBulletinFields((prev) => ({
      ...prev,
      acts: prev.acts.map((act, i) =>
        i === index ? { ...act, codeActe: entry.codeActe, cotation: `${entry.lettreCle} ${entry.coefficient}` } : act,
      ),
    }))
    setOpenActLookup(null)
  }

  // Indicative reimbursement total (catalog-backed acts only). Editor-only — never persisted / never on the PDF.
  const bulletinEstimateTotal = bulletinFields.acts.reduce((sum, act) => {
    const e = estimateReimbursement(act.cotation, bulletinFields.careType)
    return e != null ? sum + e : sum
  }, 0)
  const hasAnyBulletinEstimate = bulletinFields.acts.some(
    (act) => estimateReimbursement(act.cotation, bulletinFields.careType) != null,
  )

  // Shared bulletin ContentJson (also the PDF data). When the malade is the insured, the assuré identity
  // defaults to the patient's own name (spec edge case — no double entry).
  const buildBulletinContent = (patient: PatientDto): Record<string, string> => {
    const cnam = patient.cnamInfo
    const isSelf = (cnam?.maladeLien || "") === "Assuré lui-même"
    return {
      careType: bulletinFields.careType,
      apciCode: bulletinFields.apciCode || "",
      acts: JSON.stringify(bulletinFields.acts),
      identifiantUnique: cnam?.identifiantUnique || "",
      regime: cnam?.regime || "",
      assureFirstName: (isSelf ? patient.firstName : cnam?.assureFirstName) || "",
      assureLastName: (isSelf ? patient.lastName : cnam?.assureLastName) || "",
      assureAddress: cnam?.assureAddress || "",
      assurePostalCode: cnam?.assurePostalCode || "",
      maladeLien: cnam?.maladeLien || "",
      maladeLienRang: cnam?.maladeLienRang || "",
      // The malade is the patient — the BS1 "Le malade" box uses the patient's own identity/contact,
      // and the acts table stamps the treating doctor's CNAM provider code on every row.
      maladeFirstName: patient.firstName || "",
      maladeLastName: patient.lastName || "",
      patientPhone: patient.phoneNumber || "",
      doctorCodeProfessionnel: selectedDoctor?.codeProfessionnelSante || "",
    }
  }

  // Build structured document data for PDF generation
  const buildDocumentData = () => {
    if (!patientData) {
      return null;
    }

    const content: Record<string, any> = {};
    
    if (documentType === "prescription") {
      // Serialize medications array as JSON string for PDF generation
      content.medications = Array.isArray(formFields.medications) 
        ? JSON.stringify(formFields.medications) 
        : "";
    } else if (documentType === "liaison") {
      content.content = formFields.content || "";
    } else if (documentType === "honoraires") {
      // Serialize procedures array as JSON string
      content.procedures = Array.isArray(formFields.procedures) 
        ? JSON.stringify(formFields.procedures) 
        : "";
      content.totalCost = formFields.totalCost || "0,00 €";
    } else if (documentType === "certificat") {
      content.doctorOrderNumber = formFields.doctorOrderNumber || "";
      content.startDate = formFields.startDate || "";
      content.duration = formFields.duration || "";
      // Add patient date of birth for certificat
      if (patientData?.dateOfBirth) {
        content.patientDateOfBirth = patientData.dateOfBirth;
      }
    } else if (documentType === "bulletin-cnam") {
      Object.assign(content, buildBulletinContent(patientData));
    }

    // Format patient date of birth for PDF
    const patientDobFormatted = patientData?.dateOfBirth
      ? new Date(patientData.dateOfBirth).toLocaleDateString("fr-FR", {
          day: "2-digit",
          month: "2-digit",
          year: "numeric",
        })
      : undefined;

    return {
      documentType,
      documentDate: formFields.date,
      patientName: getPatientName(patientData),
      patientAge: patientDobFormatted, // Use date of birth instead of age
      clinicName: formData.clinicName,
      clinicAddress: formData.clinicAddress,
      clinicPhone: formData.clinicPhone,
      doctorName: formData.doctorName,
      doctorSpecialty: formData.doctorSpecialty,
      recipientDoctorName: documentType === "liaison" ? recipientDoctorName : undefined,
      recipientDoctorSpecialty: documentType === "liaison" ? recipientDoctorSpecialty : undefined,
      content,
    };
  };

  const generateWord = async () => {
    if (!patientData) {
      toast.error("Patient requis", {
        description: "Veuillez sélectionner un patient avant de générer le document Word",
        duration: 3000,
      });
      return;
    }

    if (saving) {
      return; // Prevent action while saving
    }

    // Defer heavy work to prevent blocking
    if ('requestIdleCallback' in window) {
      requestIdleCallback(async () => {
        await generateWordInternal();
      }, { timeout: 2000 });
    } else {
      setTimeout(async () => {
        await generateWordInternal();
      }, 100);
    }
  };

  const generateWordInternal = async () => {
    if (!patientData) {
      toast.error("Patient requis", {
        description: "Veuillez sélectionner un patient avant de générer le document Word",
        duration: 3000,
      });
      return;
    }

    try {
      // Create new procedures if needed (for honoraires) before exporting
      await createNewProceduresIfNeeded();

      const documentTypeName = getDocumentTitle();
      const patientName = `${patientData.firstName} ${patientData.lastName}`;
      const patientDobFormatted = patientData.dateOfBirth
        ? new Date(patientData.dateOfBirth).toLocaleDateString("fr-FR", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
          })
        : null;
      
      // Build document content
      const paragraphs: Paragraph[] = [
        new Paragraph({
          text: formData.clinicName,
          heading: HeadingLevel.HEADING_1,
          alignment: AlignmentType.LEFT,
        }),
        new Paragraph({
          text: formData.clinicAddress,
        }),
        new Paragraph({
          text: `Tél: ${formData.clinicPhone}`,
        }),
        new Paragraph({
          text: `${formData.doctorName} - ${formData.doctorSpecialty}`,
        }),
        new Paragraph({
          text: "",
        }),
      ];

      // Add recipient for liaison
      if (documentType === "liaison" && recipientDoctorName) {
        paragraphs.push(
          new Paragraph({
            text: "À l'attention de:",
          }),
          new Paragraph({
            text: recipientDoctorName,
            heading: HeadingLevel.HEADING_2,
          })
        );
        if (recipientDoctorSpecialty) {
          paragraphs.push(new Paragraph({ text: recipientDoctorSpecialty }));
        }
        paragraphs.push(new Paragraph({ text: "" }));
      }

      // Date
      paragraphs.push(
        new Paragraph({
          text: `Paris, le ${format(new Date(formFields.date), "dd MMMM yyyy", { locale: fr })}`,
          alignment: AlignmentType.RIGHT,
        }),
        new Paragraph({ text: "" })
      );

      // Document title
      paragraphs.push(
        new Paragraph({
          text: documentTypeName.toUpperCase(),
          heading: HeadingLevel.HEADING_1,
          alignment: AlignmentType.CENTER,
        }),
        new Paragraph({ text: "" })
      );

      // Patient info
      paragraphs.push(
        new Paragraph({
          text: "Patient:",
        }),
        new Paragraph({
          text: patientName,
          heading: HeadingLevel.HEADING_2,
        })
      );
      if (patientDobFormatted) {
        paragraphs.push(new Paragraph({ text: `Date de naissance: ${patientDobFormatted}` }));
      }
      paragraphs.push(new Paragraph({ text: "" }));

      // Document-specific content
      if (documentType === "prescription") {
        paragraphs.push(
          new Paragraph({
            text: "Prescription:",
            heading: HeadingLevel.HEADING_2,
          })
        );
        
        if (Array.isArray(formFields.medications) && formFields.medications.length > 0) {
          formFields.medications.forEach((med) => {
            const medText = `${med.name}${med.dosage ? ` ${med.dosage}` : ""}${med.timesPerDay ? `, ${med.timesPerDay}x par jour` : ""}${med.duration ? ` pendant ${med.duration} jour${parseInt(med.duration) > 1 ? "s" : ""}` : ""}`;
            paragraphs.push(new Paragraph({ text: medText }));
          });
        } else {
          paragraphs.push(new Paragraph({ text: "Aucune prescription" }));
        }
      } else if (documentType === "liaison") {
        paragraphs.push(
          new Paragraph({ text: formFields.content || "—" })
        );
      } else if (documentType === "honoraires") {
        paragraphs.push(
          new Paragraph({
            text: "Détail des services:",
            heading: HeadingLevel.HEADING_2,
          })
        );

        if (Array.isArray(formFields.procedures) && formFields.procedures.length > 0) {
          formFields.procedures.forEach((proc) => {
            paragraphs.push(
              new Paragraph({
                text: `${proc.name || "Procédure sans nom"} — ${proc.cost?.toFixed(2) || "0,00"} €`,
              })
            );
          });
        } else {
          paragraphs.push(new Paragraph({ text: "Aucune procédure ajoutée" }));
        }

        paragraphs.push(
          new Paragraph({ text: "" }),
          new Paragraph({ text: `Montant total: ${formFields.totalCost || "0,00 €"}` })
        );
      } else if (documentType === "certificat") {
        const startDateFormatted = formFields.startDate
          ? new Date(formFields.startDate).toLocaleDateString("fr-FR", {
              day: "2-digit",
              month: "2-digit",
              year: "numeric",
            })
          : "[date]";
        const patientDobFormatted = patientData?.dateOfBirth
          ? new Date(patientData.dateOfBirth).toLocaleDateString("fr-FR", {
              day: "2-digit",
              month: "2-digit",
              year: "numeric",
            })
          : "[JJ/MM/AAAA]";
        
        // Build cohesive paragraph text
        const certificatText = `Je soussigné(e), Docteur ${formData.doctorName}, Docteur en médecine dentaire, Inscrit(e) à l'Ordre des Médecins sous le n° ${formFields.doctorOrderNumber || "[Numéro]"}, Exerçant à ${formData.clinicAddress}, certifie avoir examiné ce jour : Patient(e) : Nom et prénom : ${patientData ? getPatientName(patientData) : "[Nom du patient]"} né(e) le ${patientDobFormatted} Et constate que son état de santé : ☐ nécessite un repos médical Pour une durée de : ${formFields.duration || "[X]"} jour${formFields.duration && parseInt(formFields.duration) > 1 ? "s" : ""} À compter du : ${startDateFormatted} Ce certificat est délivré à la demande de l'intéressé(e) pour servir et valoir ce que de droit.`;
        
        paragraphs.push(
          new Paragraph({
            text: certificatText,
          })
        );
      }

      // Signature
      paragraphs.push(
        new Paragraph({ text: "" }),
        new Paragraph({
          text: "Date et signature du médecin",
        }),
        new Paragraph({ text: "" }),
        new Paragraph({
          text: formData.doctorName,
          heading: HeadingLevel.HEADING_2,
          alignment: AlignmentType.RIGHT,
        }),
        new Paragraph({
          text: formData.doctorSpecialty,
          alignment: AlignmentType.RIGHT,
        })
      );

      const doc = new Document({
        sections: [{
          children: paragraphs,
        }],
      });

      const blob = await Packer.toBlob(doc);
      const fileName = `${documentTypeName.toLowerCase().replace(/\s+/g, '-')}-${patientName.toLowerCase().replace(/\s+/g, '-')}.docx`;
      saveAs(blob, fileName);
      toast.success("Document Word téléchargé avec succès", {
        description: `Le fichier "${fileName}" est en cours de téléchargement`,
        duration: 3000,
      });
    } catch (error) {
      console.error('Error generating Word document:', error);
      toast.error("Erreur lors de la génération du document Word", {
        description: "Une erreur s'est produite lors de la création du document. Veuillez réessayer.",
        duration: 4000,
      });
    }
  };

  const handleDownloadPdf = async () => {
    if (saving) {
      return;
    }

    if (!patientData) {
      toast.error("Patient requis", {
        description: "Veuillez sélectionner un patient avant de générer le PDF",
        duration: 3000,
      });
      return;
    }

    const loadingToast = toast.loading("Génération du PDF en cours...", {
      description: "Veuillez patienter pendant la création du document",
    });
    
    try {
      // Create new procedures if needed (for honoraires) before exporting
      await createNewProceduresIfNeeded();

      const documentData = buildDocumentData();
      if (!documentData) {
        toast.dismiss(loadingToast);
        toast.error("Données manquantes", {
          description: "Impossible de générer le PDF. Veuillez vérifier que tous les champs requis sont remplis.",
          duration: 4000,
        });
        return;
      }

      // Generate PDF on server using structured data
      const pdfBlob = await medicalDocumentsApi.generatePdfForDownload(documentData);
      
      // Download the PDF
      const documentTypeName = getDocumentTitle();
      const patientName = `${patientData.firstName}-${patientData.lastName}`.toLowerCase();
      const fileName = `${documentTypeName.toLowerCase().replace(/\s+/g, '-')}-${patientName}.pdf`;
      
      const url = window.URL.createObjectURL(pdfBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);

      toast.dismiss(loadingToast);
      toast.success("PDF téléchargé avec succès", {
        description: `Le fichier "${fileName}" est en cours de téléchargement`,
        duration: 3000,
      });
    } catch (error) {
      console.error('Error in handleDownloadPdf:', error);
      toast.dismiss(loadingToast);
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur s'est produite";
      toast.error("Erreur lors du téléchargement du PDF", {
        description: errorMessage,
        duration: 4000,
      });
    }
  };

  const handleSavePdfToFiles = async () => {
    if (!documentId || !patientData) {
      toast.error("Document non sauvegardé", {
        description: "Veuillez sauvegarder le document d'abord avant de générer le PDF",
        duration: 3000,
      });
      return;
    }

    if (saving) {
      return;
    }

    const loadingToast = toast.loading("Génération et sauvegarde du PDF en cours...", {
      description: "Le PDF sera ajouté aux fichiers du patient une fois terminé",
    });
    
    try {
      // Queue PDF generation on server (background job) - no need to send data, it will fetch from document
      await medicalDocumentsApi.generatePdf(documentId);
      
      toast.dismiss(loadingToast);
      toast.success("PDF en cours de génération", {
        description: "Le document sera ajouté aux fichiers du patient une fois la génération terminée",
        duration: 4000,
      });
    } catch (error) {
      console.error('Error saving PDF to files:', error);
      toast.dismiss(loadingToast);
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur s'est produite";
      toast.error("Erreur lors de la sauvegarde du PDF", {
        description: errorMessage,
        duration: 4000,
      });
    }
  };

  const handlePrint = () => {
    if (saving) {
      return; // Prevent action while saving
    }

    if (!documentRef.current) {
      toast.error("Impossible d'imprimer", {
        description: "Le contenu du document n'est pas disponible pour l'impression",
        duration: 3000,
      });
      return;
    }

    // Create a new window for printing
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      toast.error("Popup bloquée", {
        description: "Veuillez autoriser les fenêtres popup dans votre navigateur pour imprimer",
        duration: 4000,
      });
      return;
    }

    // Clone the document content
    const content = documentRef.current.cloneNode(true) as HTMLElement;
    
    // Remove contentEditable attributes for cleaner print
    content.querySelectorAll('[contenteditable]').forEach(el => {
      el.removeAttribute('contenteditable');
    });

    // Write to print window
    printWindow.document.write(`
      <!DOCTYPE html>
      <html>
        <head>
          <title>${getDocumentTitle()}</title>
          <style>
            @media print {
              @page {
                size: A4;
                margin: 0;
              }
              body {
                margin: 0;
                padding: 20mm;
              }
            }
            body {
              font-family: Arial, sans-serif;
              margin: 0;
              padding: 20mm;
            }
            ${document.querySelector('style')?.textContent || ''}
          </style>
        </head>
        <body>
          ${content.innerHTML}
        </body>
      </html>
    `);
    
    printWindow.document.close();
    
    // Wait for content to load, then print
    printWindow.onload = () => {
      setTimeout(() => {
        printWindow.print();
      }, 250);
    };
  };

  // Helper function to create new procedures for honoraires documents
  const createNewProceduresIfNeeded = async (): Promise<void> => {
    if (documentType !== "honoraires" || !Array.isArray(formFields.procedures)) {
      return
    }

    try {
      let proceduresUpdated = false
      for (const proc of formFields.procedures) {
        // Only create if it's a new procedure (no procedureTypeId) and has a name
        if (!proc.procedureTypeId && proc.name && proc.name.trim()) {
          // Check if procedure already exists
          const existing = availableProcedures.find(p => 
            p.name.toLowerCase() === proc.name.trim().toLowerCase()
          )
          
          if (!existing) {
            // Create new procedure type
            await procedureTypesApi.create({
              name: proc.name.trim(),
              defaultDurationMinutes: 30, // Default duration (required field)
              defaultCost: proc.cost || null,
              colorHex: "#3b82f6", // Default blue color
              description: `Procédure créée depuis une note d'honoraires`
            })
            proceduresUpdated = true
          }
        }
      }
      
      // Reload procedures list if any were created
      if (proceduresUpdated) {
        const updatedProcedures = await procedureTypesApi.list(true)
        setAvailableProcedures(updatedProcedures)
      }
    } catch (error) {
      console.error("Failed to create new procedures:", error)
      throw error // Re-throw to let caller handle it
    }
  }

  const handleSave = async () => {
    if (!selectedPatient || !patientData) {
      toast.error("Patient requis", {
        description: "Veuillez sélectionner un patient avant de sauvegarder le document",
        duration: 3000,
      })
      return
    }

    setSaving(true)
    try {
      // Create new procedures if needed (for honoraires)
      await createNewProceduresIfNeeded()

      // Build content JSON from form fields
      const content: Record<string, any> = {
        date: formFields.date,
      }

      if (documentType === "prescription") {
        content.medications = formFields.medications // Array will be serialized as JSON
    } else if (documentType === "liaison") {
      content.content = formFields.content
      } else if (documentType === "honoraires") {
        content.procedures = formFields.procedures
        content.totalCost = formFields.totalCost
      } else if (documentType === "certificat") {
        content.reason = formFields.reason
        content.duration = formFields.duration
        content.notes = formFields.notes
      } else if (documentType === "bulletin-cnam") {
        Object.assign(content, buildBulletinContent(patientData))
      }

      const contentJson = JSON.stringify(content)

      // Save document first
      let savedDocumentId = documentId;
      
      if (documentId) {
        // Update existing document
        await medicalDocumentsApi.update(documentId, {
          documentDate: formFields.date,
          recipientDoctorName: recipientDoctorName || undefined,
          recipientDoctorSpecialty: recipientDoctorSpecialty || undefined,
          contentJson,
        })
        toast.success("Document mis à jour avec succès", {
          description: "Les modifications ont été enregistrées",
          duration: 3000,
        })
        savedDocumentId = documentId; // Use existing document ID
      } else {
        // Create new document
        const result = await medicalDocumentsApi.create({
          patientId: selectedPatient,
          documentType,
          documentDate: formFields.date,
          recipientDoctorName: recipientDoctorName || undefined,
          recipientDoctorSpecialty: recipientDoctorSpecialty || undefined,
          contentJson,
          clinicName: formData.clinicName,
          clinicAddress: formData.clinicAddress,
          clinicPhone: formData.clinicPhone,
          doctorName: formData.doctorName,
          doctorSpecialty: formData.doctorSpecialty,
          appointmentId: urlAppointmentId || undefined,
        })
        savedDocumentId = result.id;
        setDocumentId(result.id)
        toast.success("Document sauvegardé avec succès", {
          description: "Le document a été créé et enregistré",
          duration: 3000,
        })
      }

      // Automatically generate and save PDF in background (non-blocking)
      // This ensures PDF is always available in the documents folder
      if (savedDocumentId) {
        try {
          // Queue PDF generation - this happens in background, doesn't block UI
          await medicalDocumentsApi.generatePdf(savedDocumentId);
          toast.info("PDF en cours de génération", {
            description: "Le PDF sera ajouté aux fichiers du patient une fois terminé",
            duration: 4000,
          });
        } catch (error) {
          console.error("Failed to queue PDF generation:", error);
          // Don't show error to user - PDF generation is optional
        }
      }

    } catch (error) {
      console.error("Failed to save document:", error)
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur s'est produite"
      toast.error("Erreur lors de la sauvegarde", {
        description: errorMessage,
        duration: 4000,
      })
    } finally {
      setSaving(false)
    }
  }

  const getDocumentTitle = () => {
    switch (documentType) {
      case "prescription":
        return "Ordonnance"
      case "liaison":
        return "Lettre de liaison"
      case "honoraires":
        return "Note d'honoraires"
      case "certificat":
        return "Certificat médical"
      case "bulletin-cnam":
        return "Bulletin de soins CNAM"
      default:
        return "Document"
    }
  }

  const getSelectedPatientData = () => {
    return patients.find((p) => p.id === selectedPatient)
  }

  const patientData = getSelectedPatientData()
  const patientAge = patientData ? calculateAge(patientData.dateOfBirth) : null

  return (
    <div className="flex h-screen bg-background">
      <div className="flex flex-1 flex-col overflow-hidden">
        <div className="flex h-full">
        {/* Left Panel - Input Fields */}
        <div className="w-[420px] border-r border-border bg-white/90 dark:bg-slate-950/90 backdrop-blur-xl overflow-y-auto">
          <div className="p-8 space-y-6">
            {/* Header */}
            <div className="space-y-4">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => router.push("/documents")}
                className="hover:bg-blue-50 dark:hover:bg-blue-950 -ml-2"
              >
                <ArrowLeft className="w-4 h-4 mr-2" />
                Retour aux modèles
              </Button>

              <div className="space-y-2">
                <div className="flex items-center gap-3">
                  <div className="w-12 h-12 bg-gradient-to-br from-blue-500 to-blue-600 rounded-lg flex items-center justify-center">
                    <FileText className="w-6 h-6 text-white" />
                  </div>
                  <div>
                    <h2 className="text-2xl font-bold text-foreground">{getDocumentTitle()}</h2>
                    <p className="text-sm text-muted-foreground">Remplissez les informations</p>
                  </div>
                </div>
              </div>
            </div>

            <Separator />

            {/* Patient Selection */}
            <div className="space-y-2">
              <Label className="text-sm font-semibold text-foreground">Patient *</Label>
              <Popover open={patientSearchOpen} onOpenChange={setPatientSearchOpen}>
                <PopoverTrigger asChild>
                  <Button
                    variant="outline"
                    role="combobox"
                    aria-expanded={patientSearchOpen}
                    className="w-full justify-between text-left h-11 bg-transparent"
                    type="button"
                    onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      if (!loadingPatients) {
                        setPatientSearchOpen(true);
                      }
                    }}
                  >
                    {selectedPatient && patientData ? (
                      <span className="font-medium">{getPatientName(patientData)}</span>
                    ) : (
                      <span className="text-muted-foreground">
                        {loadingPatients ? "Chargement..." : "Sélectionner un patient..."}
                      </span>
                    )}
                    {loadingPatients ? (
                      <Loader2 className="ml-2 h-4 w-4 shrink-0 animate-spin opacity-50" />
                    ) : (
                      <Search className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                    )}
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-[384px] p-0 z-50" align="start" onOpenAutoFocus={(e) => e.preventDefault()}>
                  <div className="p-2 border-b">
                    <div className="relative">
                      <Search className="absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" />
                      <Input
                        placeholder="Rechercher un patient..."
                        value={patientSearchQuery}
                        onChange={(e) => setPatientSearchQuery(e.target.value)}
                        className="pl-8 h-9"
                        autoFocus
                      />
                    </div>
                  </div>
                  <div className="max-h-[300px] overflow-y-auto">
                    {loadingPatients ? (
                      <div className="flex items-center justify-center p-8">
                        <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />
                        <span className="ml-2 text-sm text-muted-foreground">Chargement...</span>
                      </div>
                    ) : filteredPatients.length === 0 ? (
                      <div className="p-8 text-center text-sm text-muted-foreground">
                        {patientSearchQuery ? "Aucun patient trouvé." : "Aucun patient disponible."}
                      </div>
                    ) : (
                      <div className="p-1">
                        {filteredPatients.map((patient) => {
                          const patientName = getPatientName(patient)
                          const patientAge = calculateAge(patient.dateOfBirth)
                          
                          return (
                            <div
                              key={patient.id}
                              onClick={() => {
                                setSelectedPatient(patient.id)
                                setPatientSearchOpen(false)
                                setPatientSearchQuery("")
                              }}
                              className="flex items-center justify-between px-3 py-2 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors"
                            >
                              <span className="font-medium text-sm">{patientName}</span>
                              {patientAge && (
                                <span className="text-xs text-muted-foreground">{patientAge}</span>
                              )}
                            </div>
                          )
                        })}
                      </div>
                    )}
                  </div>
                </PopoverContent>
              </Popover>
            </div>

            {/* Recipient Doctor (for liaison) */}
            {documentType === "liaison" && (
              <div className="space-y-2">
                <Label htmlFor="recipientDoctor" className="text-sm font-semibold text-foreground">
                  Médecin destinataire *
                </Label>
                <Select
                  value={selectedRecipientDoctorId}
                  onValueChange={setSelectedRecipientDoctorId}
                >
                  <SelectTrigger className="h-11" id="recipientDoctor">
                    <SelectValue placeholder={doctors.length === 0 ? "No doctors found" : "Choose a doctor..."} />
                  </SelectTrigger>
                  <SelectContent className="max-h-[200px]">
                    {doctors.length === 0 ? (
                      <div className="px-2 py-1.5 text-sm text-muted-foreground">No doctors available</div>
                    ) : (
                      doctors.map((doctor) => (
                        <SelectItem key={doctor.id || doctor.name} value={doctor.id || ""}>
                          {doctor.name} {doctor.specialty ? `- ${doctor.specialty}` : ""}
                        </SelectItem>
                      ))
                    )}
                  </SelectContent>
                </Select>
              </div>
            )}

            <Separator />

            {/* Date */}
            <div className="space-y-2">
              <Label htmlFor="date" className="text-sm font-semibold text-foreground">
                Date
              </Label>
              <Input
                id="date"
                type="date"
                value={formFields.date}
                onChange={(e) => setFormFields({ ...formFields, date: e.target.value })}
                className="h-11"
              />
            </div>

            {/* Document-specific fields */}
            {documentType === "prescription" && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <Label className="text-sm font-semibold text-foreground">
                    Médicaments prescrits
                  </Label>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setFormFields(prev => ({
                        ...prev,
                        medications: [...prev.medications, { name: "", dosage: "", timesPerDay: "", duration: "" }]
                      }))
                    }}
                  >
                    <Plus className="w-4 h-4 mr-2" />
                    Ajouter un médicament
                  </Button>
                </div>
                
                {formFields.medications.length === 0 ? (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    Aucun médicament ajouté. Cliquez sur "Ajouter un médicament" pour commencer.
                  </p>
                ) : (
                  <div className="space-y-3">
                    {formFields.medications.map((med, index) => (
                      <MedicationItem
                        key={index}
                        medication={med}
                        onUpdate={(updated) => {
                          const newMedications = [...formFields.medications]
                          newMedications[index] = updated
                          setFormFields(prev => ({ ...prev, medications: newMedications }))
                        }}
                        onRemove={() => {
                          const updated = formFields.medications.filter((_, i) => i !== index)
                          setFormFields(prev => ({ ...prev, medications: updated }))
                        }}
                      />
                    ))}
                  </div>
                )}
              </div>
            )}

            {documentType === "liaison" && (
              <>
                <div className="space-y-2">
                  <Label htmlFor="content" className="text-sm font-semibold text-foreground">
                    Contenu
                  </Label>
                  <Textarea
                    id="content"
                    placeholder="Entrez le contenu de la lettre de liaison..."
                    value={formFields.content}
                    onChange={(e) => setFormFields({ ...formFields, content: e.target.value })}
                    rows={12}
                    className="resize-none"
                  />
                </div>
              </>
            )}

            {documentType === "honoraires" && (
              <>
                <div className="space-y-4">
                  <div className="flex items-center justify-between">
                    <Label className="text-sm font-semibold text-foreground">
                      Procédures et services
                    </Label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        setFormFields(prev => ({
                          ...prev,
                          procedures: [...prev.procedures, { name: "", cost: 0 }]
                        }))
                      }}
                    >
                      <Plus className="w-4 h-4 mr-2" />
                      Ajouter une procédure
                    </Button>
                  </div>
                  
                  {formFields.procedures.length === 0 ? (
                    <p className="text-sm text-muted-foreground text-center py-4">
                      Aucune procédure ajoutée. Cliquez sur "Ajouter une procédure" pour commencer.
                    </p>
                  ) : (
                    <div className="space-y-3">
                      {formFields.procedures.map((proc, index) => (
                        <ProcedureItem
                          key={index}
                          procedure={proc}
                          availableProcedures={availableProcedures}
                          onUpdate={(updated) => {
                            const newProcedures = [...formFields.procedures]
                            newProcedures[index] = updated
                            setFormFields(prev => ({ ...prev, procedures: newProcedures }))
                          }}
                          onRemove={() => {
                            const updated = formFields.procedures.filter((_, i) => i !== index)
                            setFormFields(prev => ({ ...prev, procedures: updated }))
                          }}
                        />
                      ))}
                    </div>
                  )}
                  
                  <div className="pt-2 border-t">
                    <div className="flex justify-between items-center">
                      <span className="text-sm font-semibold">Total:</span>
                      <span className="text-lg font-bold">{formFields.totalCost}</span>
                    </div>
                  </div>
                </div>
                
              </>
            )}

            {documentType === "certificat" && (
              <>
                <div className="space-y-2">
                  <Label htmlFor="doctorOrderNumber" className="text-sm font-semibold text-foreground">
                    Numéro d'ordre des médecins
                  </Label>
                  <Input
                    id="doctorOrderNumber"
                    type="text"
                    placeholder="Ex: 12345"
                    value={formFields.doctorOrderNumber}
                    onChange={(e) => setFormFields({ ...formFields, doctorOrderNumber: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="duration" className="text-sm font-semibold text-foreground">
                    Durée du repos médical (en jours)
                  </Label>
                  <Input
                    id="duration"
                    type="number"
                    min="1"
                    placeholder="Ex: 3"
                    value={formFields.duration}
                    onChange={(e) => setFormFields({ ...formFields, duration: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="startDate" className="text-sm font-semibold text-foreground">
                    Date de début du repos médical
                  </Label>
                  <Input
                    id="startDate"
                    type="date"
                    value={formFields.startDate}
                    onChange={(e) => setFormFields({ ...formFields, startDate: e.target.value })}
                    className="h-11"
                  />
                </div>
              </>
            )}

            {documentType === "bulletin-cnam" && (
              <div className="space-y-5">
                <div className="space-y-2">
                  <Label className="text-sm font-semibold text-foreground">Type de prise en charge</Label>
                  <Select value={bulletinFields.careType} onValueChange={(v) => setBulletinFields((p) => ({ ...p, careType: v }))}>
                    <SelectTrigger className="h-11 w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="APCI">APCI (affection prise en charge intégralement)</SelectItem>
                      <SelectItem value="MO">Maladie ordinaire (MO)</SelectItem>
                      <SelectItem value="Hospitalisation">Hospitalisation</SelectItem>
                      <SelectItem value="Suivi de grossesse">Suivi de grossesse</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                {bulletinFields.careType === "APCI" && (
                  <div className="space-y-2">
                    <Label htmlFor="apciCode" className="text-sm font-semibold text-foreground">Code APCI</Label>
                    <Input id="apciCode" value={bulletinFields.apciCode} onChange={(e) => setBulletinFields((p) => ({ ...p, apciCode: e.target.value }))} className="h-11" placeholder="Ex: 12" />
                  </div>
                )}

                <div className="space-y-3">
                  <Label className="text-sm font-semibold text-foreground">Actes (depuis les soins dentaires)</Label>
                  <div className="grid grid-cols-2 gap-2">
                    <div className="flex flex-col gap-1">
                      <Label className="text-xs text-muted-foreground">Du</Label>
                      <Input type="date" value={bulletinFields.actsFrom} onChange={(e) => setBulletinFields((p) => ({ ...p, actsFrom: e.target.value }))} className="h-10" />
                    </div>
                    <div className="flex flex-col gap-1">
                      <Label className="text-xs text-muted-foreground">Au</Label>
                      <Input type="date" value={bulletinFields.actsTo} onChange={(e) => setBulletinFields((p) => ({ ...p, actsTo: e.target.value }))} className="h-10" />
                    </div>
                  </div>
                  <Button type="button" variant="outline" size="sm" className="w-full" onClick={prefillActsFromRecords} disabled={!selectedPatient || dentalRecords.length === 0}>
                    <Search className="w-4 h-4 mr-2" />
                    Pré-remplir depuis les soins ({dentalRecords.length})
                  </Button>

                  {bulletinFields.acts.length === 0 ? (
                    <p className="text-sm text-muted-foreground text-center py-4">Aucun acte. Pré-remplissez depuis les soins ou ajoutez une ligne.</p>
                  ) : (
                    <div className="space-y-3">
                      {bulletinFields.acts.map((act, index) => {
                        const actEstimate = estimateReimbursement(act.cotation, bulletinFields.careType)
                        return (
                        <div key={index} className="p-3 border rounded-lg space-y-2">
                          <div className="flex items-center justify-between">
                            <span className="text-xs font-medium text-muted-foreground">Acte {index + 1}</span>
                            <Button type="button" variant="ghost" size="sm" className="h-7 w-7 p-0" onClick={() => setBulletinFields((p) => ({ ...p, acts: p.acts.filter((_, i) => i !== index) }))}>
                              <X className="w-4 h-4" />
                            </Button>
                          </div>
                          <div className="grid grid-cols-2 gap-2">
                            <Input type="date" value={act.date} onChange={(e) => updateBulletinAct(index, "date", e.target.value)} className="h-9 text-sm" />
                            <Input placeholder="Dent(s)" value={act.teeth} onChange={(e) => updateBulletinAct(index, "teeth", e.target.value)} className="h-9 text-sm" />
                            <div className="col-span-2 flex gap-2">
                              <Input placeholder="Code acte" value={act.codeActe} onChange={(e) => updateBulletinAct(index, "codeActe", e.target.value)} className="h-9 text-sm flex-1" />
                              <Popover open={openActLookup === index} onOpenChange={(o) => setOpenActLookup(o ? index : null)} modal>
                                <PopoverTrigger asChild>
                                  <Button type="button" variant="outline" size="sm" className="h-9 px-3 shrink-0" title="Rechercher un acte CNAM">
                                    <Search className="w-4 h-4" />
                                    <span className="sr-only">Rechercher un acte CNAM</span>
                                  </Button>
                                </PopoverTrigger>
                                <PopoverContent className="p-0 w-80" align="end">
                                  <Command>
                                    <CommandInput placeholder="Rechercher un acte CNAM..." />
                                    <CommandList>
                                      <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                      <CommandGroup>
                                        {cnamNomenclature.map((entry) => (
                                          <CommandItem key={entry.codeActe} value={`${entry.codeActe} ${entry.designationFr} ${entry.lettreCle}`} onSelect={() => selectNomenclatureEntry(index, entry)}>
                                            <div className="flex flex-col">
                                              <span className="text-sm font-medium">{entry.designationFr}</span>
                                              <span className="text-xs text-muted-foreground">{entry.codeActe} · {entry.lettreCle} {entry.coefficient} · {entry.category}</span>
                                            </div>
                                          </CommandItem>
                                        ))}
                                      </CommandGroup>
                                    </CommandList>
                                  </Command>
                                </PopoverContent>
                              </Popover>
                            </div>
                            <Input placeholder="Cotation" value={act.cotation} onChange={(e) => updateBulletinAct(index, "cotation", e.target.value)} className="h-9 text-sm" />
                            <Input placeholder="Honoraires (TND)" value={act.honoraires} onChange={(e) => updateBulletinAct(index, "honoraires", e.target.value)} className="h-9 text-sm" />
                          </div>
                          {actEstimate != null && (
                            <p className="text-xs text-muted-foreground">Remb. indicatif&nbsp;: <span className="font-medium text-foreground">{actEstimate.toFixed(3)} TND</span></p>
                          )}
                        </div>
                        )
                      })}
                    </div>
                  )}

                  <Button type="button" variant="outline" size="sm" className="w-full" onClick={() => setBulletinFields((p) => ({ ...p, acts: [...p.acts, { date: "", teeth: "", codeActe: "", cotation: "", honoraires: "" }] }))}>
                    <Plus className="w-4 h-4 mr-2" />
                    Ajouter un acte
                  </Button>

                  {hasAnyBulletinEstimate && (
                    <div className="rounded-lg border border-dashed p-3 space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-foreground">Remboursement indicatif (total)</span>
                        <span className="text-sm font-semibold text-foreground">{bulletinEstimateTotal.toFixed(3)} TND</span>
                      </div>
                      <p className="text-xs text-muted-foreground">Estimation indicative — montant réel fixé par la CNAM.{bulletinFields.careType === "APCI" ? " Taux APCI (100%)." : " Taux standard."}</p>
                    </div>
                  )}
                </div>
              </div>
            )}

            <Separator />

            {/* Actions */}
            <div className="space-y-3 pt-2">
              <Button 
                className="w-full h-11 bg-blue-600 hover:bg-blue-700 text-base font-medium"
                onClick={() => handleSave()}
                disabled={saving || !selectedPatient}
              >
                <Save className="w-4 h-4 mr-2" />
                {saving ? "Sauvegarde..." : documentId ? "Mettre à jour" : "Sauvegarder le document"}
              </Button>
              <div className="grid grid-cols-2 gap-3">
                <Button variant="outline" onClick={resetForm} className="h-11 bg-transparent">
                  <RotateCcw className="w-4 h-4 mr-2" />
                  Réinitialiser
                </Button>
                <Button
                  variant="outline"
                  className="h-11 bg-transparent"
                  onClick={() => handlePrint()}
                  disabled={saving}
                >
                  <Printer className="w-4 h-4 mr-2" />
                  Imprimer
                </Button>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <Button
                  variant="outline"
                  className="h-11 bg-transparent border-green-500 text-green-600 hover:bg-green-50 dark:hover:bg-green-950"
                  onClick={() => handleDownloadPdf()}
                  disabled={!patientData || saving}
                >
                  <Download className="w-4 h-4 mr-2" />
                  Télécharger PDF
                </Button>
                <Button
                  variant="outline"
                  className="h-11 bg-transparent border-blue-500 text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-950"
                  onClick={() => generateWord()}
                  disabled={!patientData || saving}
                >
                  <Download className="w-4 h-4 mr-2" />
                  Télécharger Word
                </Button>
              </div>
            </div>
          </div>
        </div>

        {/* Right Panel - Document Preview */}
        <div className="flex-1 overflow-y-auto bg-gradient-to-br from-slate-100 to-blue-50 dark:from-slate-900 dark:to-slate-800 p-12">
          <div className="max-w-4xl mx-auto">
            <div className="mb-6 flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-muted-foreground">Aperçu du document</p>
                <p className="text-xs text-muted-foreground mt-1">Cliquez sur le texte pour modifier directement</p>
              </div>
              <div className="text-sm text-muted-foreground">Format A4</div>
            </div>

            <Card className="p-16 bg-white dark:bg-slate-900 shadow-2xl min-h-[1123px] flex flex-col" ref={documentRef} style={{ fontFamily: 'Helvetica, Arial, sans-serif' }}>
              <div className="flex-1 flex flex-col space-y-5" style={{ fontSize: '11pt', lineHeight: '1.5' }}>
                {/* Letterhead */}
                <div className="space-y-1 pb-4">
                  <h1
                    className="font-bold text-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '14pt' }}
                    contentEditable
                    suppressContentEditableWarning
                  >
                    {formData.clinicName}
                  </h1>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                    contentEditable
                    suppressContentEditableWarning
                  >
                    {formData.clinicAddress}
                  </p>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                    contentEditable
                    suppressContentEditableWarning
                  >
                    Tél: {formData.clinicPhone}
                  </p>
                  <p
                    className="font-bold focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                    contentEditable
                    suppressContentEditableWarning
                  >
                    {formData.doctorName} - {formData.doctorSpecialty}
                  </p>
                </div>

                {/* Recipient (for liaison) */}
                {documentType === "liaison" && (
                  <div className="space-y-1 py-3 px-3">
                    <p style={{ fontSize: '11pt' }}>À l'attention de:</p>
                    <div
                      className="focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                      contentEditable
                      suppressContentEditableWarning
                    >
                      {recipientDoctorName ? (
                        <>
                          <p className="font-bold" style={{ fontSize: '12pt' }}>{recipientDoctorName}</p>
                          {recipientDoctorSpecialty && (
                            <p className="text-muted-foreground" style={{ fontSize: '11pt' }}>{recipientDoctorSpecialty}</p>
                          )}
                        </>
                      ) : (
                        <p className="text-muted-foreground italic" style={{ fontSize: '11pt' }}>Entrez le nom du médecin destinataire</p>
                      )}
                    </div>
                  </div>
                )}

                {/* Date */}
                <div className="text-right pb-2">
                  <p
                    className="focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1 inline-block"
                    style={{ fontSize: '11pt' }}
                    contentEditable
                    suppressContentEditableWarning
                  >
                    Paris, le{" "}
                    {new Date(formFields.date).toLocaleDateString("fr-FR", {
                      day: "numeric",
                      month: "long",
                      year: "numeric",
                    })}
                  </p>
                </div>

                {/* Document Title */}
                <div className="text-center py-2">
                  <h2 className="font-bold uppercase" style={{ fontSize: '16pt' }}>{getDocumentTitle()}</h2>
                </div>

                {/* Patient Info */}
                <div className="space-y-2 py-3 px-3">
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Patient</p>
                      <p
                        className="font-bold focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                        style={{ fontSize: '12pt' }}
                        contentEditable
                        suppressContentEditableWarning
                      >
                        {patientData ? getPatientName(patientData) : "Sélectionnez un patient"}
                      </p>
                    </div>
                    {patientData?.dateOfBirth && (
                      <div>
                        <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Date de naissance</p>
                        <p
                          className="focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                          style={{ fontSize: '12pt' }}
                          contentEditable
                          suppressContentEditableWarning
                        >
                          {new Date(patientData.dateOfBirth).toLocaleDateString("fr-FR", {
                            day: "2-digit",
                            month: "2-digit",
                            year: "numeric",
                          })}
                        </p>
                      </div>
                    )}
                  </div>
                </div>

                <Separator />

                {/* Document Content */}
                <div className="space-y-4 flex-1">
                  {documentType === "prescription" && (
                    <div className="space-y-2">
                      <h3 className="font-bold pb-2" style={{ fontSize: '12pt' }}>Prescription:</h3>
                      {Array.isArray(formFields.medications) && formFields.medications.length > 0 ? (
                        <div className="space-y-2 pl-1">
                          {formFields.medications.map((med, idx) => {
                            const medText = `${med.name || "Médicament"}${med.dosage ? ` ${med.dosage}` : ""}${med.timesPerDay ? `, ${med.timesPerDay}x par jour` : ""}${med.duration ? ` pendant ${med.duration} jour${parseInt(med.duration) > 1 ? "s" : ""}` : ""}`;
                            return (
                              <div key={idx} className="py-1" style={{ fontSize: '11pt' }}>
                                {medText}
                              </div>
                            );
                          })}
                        </div>
                      ) : (
                        <div className="min-h-[200px] p-4 border-2 border-dashed border-slate-300 dark:border-slate-600 rounded-lg text-muted-foreground" style={{ fontSize: '11pt' }}>
                          Aucun médicament ajouté
                        </div>
                      )}
                    </div>
                  )}

                  {documentType === "liaison" && (
                    <div>
                      <div
                        className="min-h-[300px] p-4 border-2 border-dashed border-slate-300 dark:border-slate-600 rounded-lg focus:border-blue-500 focus:outline-none whitespace-pre-wrap"
                        style={{ fontSize: '11pt' }}
                        contentEditable
                        suppressContentEditableWarning
                        onBlur={(e) => setFormFields({ ...formFields, content: e.currentTarget.textContent || "" })}
                      >
                        {formFields.content || "Contenu de la lettre de liaison..."}
                      </div>
                    </div>
                  )}

                  {documentType === "honoraires" && (
                    <div className="space-y-4">
                      <div className="space-y-2">
                        <h3 className="font-bold pb-2" style={{ fontSize: '12pt' }}>Détail des services:</h3>
                        {Array.isArray(formFields.procedures) && formFields.procedures.length > 0 ? (
                          <div className="space-y-1 pl-1">
                            {formFields.procedures.map((proc, idx) => (
                              <div key={idx} className="flex justify-between items-center py-1" style={{ fontSize: '11pt' }}>
                                <span>{proc.name || "Procédure sans nom"}</span>
                                <span className="ml-4" style={{ minWidth: '90px', textAlign: 'right' }}>{proc.cost?.toFixed(2) || "0,00"} €</span>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <div className="min-h-[150px] p-4 border-2 border-dashed border-slate-300 dark:border-slate-600 rounded-lg text-muted-foreground" style={{ fontSize: '11pt' }}>
                            Aucune procédure ajoutée
                          </div>
                        )}
                      </div>
                      <div className="pt-4 pb-2 border-t border-slate-300 dark:border-slate-600">
                        <div className="flex justify-between items-center">
                          <span className="font-bold" style={{ fontSize: '12pt' }}>Montant total:</span>
                          <span className="font-bold text-blue-600" style={{ fontSize: '12pt' }}>{formFields.totalCost || "0,00 €"}</span>
                        </div>
                      </div>
                    </div>
                  )}

                  {documentType === "certificat" && (
                    <div style={{ fontSize: '11pt', lineHeight: '1.8', textAlign: 'justify' }}>
                      <p>
                        Je soussigné(e), Docteur <span className="font-semibold">{formData.doctorName}</span>, Docteur en médecine dentaire, Inscrit(e) à l'Ordre des Médecins sous le n°{" "}
                        <span className="font-semibold border-b border-dashed border-slate-400 px-1">
                          {formFields.doctorOrderNumber || "[Numéro]"}
                        </span>, Exerçant à <span className="font-semibold">{formData.clinicAddress}</span>, certifie avoir examiné ce jour : Patient(e) : Nom et prénom :{" "}
                        <span className="font-semibold">{patientData ? getPatientName(patientData) : "[Nom du patient]"}</span> né(e) le{" "}
                        <span className="font-semibold">
                          {patientData?.dateOfBirth
                            ? new Date(patientData.dateOfBirth).toLocaleDateString("fr-FR", {
                                day: "2-digit",
                                month: "2-digit",
                                year: "numeric",
                              })
                            : "[JJ/MM/AAAA]"}
                        </span> Et constate que son état de santé : ☐ nécessite un repos médical Pour une durée de :{" "}
                        <span className="font-semibold border-b border-dashed border-slate-400 px-1">
                          {formFields.duration || "[X]"}
                        </span>{" "}
                        jour{formFields.duration && parseInt(formFields.duration) > 1 ? "s" : ""} À compter du :{" "}
                        <span className="font-semibold border-b border-dashed border-slate-400 px-1">
                          {formFields.startDate
                            ? new Date(formFields.startDate).toLocaleDateString("fr-FR", {
                                day: "2-digit",
                                month: "2-digit",
                                year: "numeric",
                              })
                            : "[date]"}
                        </span> Ce certificat est délivré à la demande de l'intéressé(e) pour servir et valoir ce que de droit.
                      </p>
                    </div>
                  )}

                  {documentType === "bulletin-cnam" && (
                    <div style={{ fontSize: '11pt' }} className="space-y-3">
                      <p className="font-semibold text-center" style={{ fontSize: '13pt' }}>BULLETIN DE SOINS CNAM (BS1)</p>
                      <p>
                        <span className="font-semibold">Prise en charge :</span> {bulletinFields.careType}
                        {bulletinFields.careType === "APCI" && bulletinFields.apciCode ? ` — code ${bulletinFields.apciCode}` : ""}
                      </p>
                      {patientData?.cnamInfo?.identifiantUnique && (
                        <p><span className="font-semibold">Identifiant unique :</span> {patientData.cnamInfo.identifiantUnique}</p>
                      )}
                      <table className="w-full border-collapse" style={{ fontSize: '10pt' }}>
                        <thead>
                          <tr>
                            <th className="border p-1 text-left">Date</th>
                            <th className="border p-1 text-left">Dent(s)</th>
                            <th className="border p-1 text-left">Code acte</th>
                            <th className="border p-1 text-left">Cotation</th>
                            <th className="border p-1 text-right">Honoraires</th>
                          </tr>
                        </thead>
                        <tbody>
                          {bulletinFields.acts.length === 0 ? (
                            <tr><td className="border p-2 text-center text-muted-foreground" colSpan={5}>Aucun acte</td></tr>
                          ) : (
                            bulletinFields.acts.map((act, idx) => (
                              <tr key={idx}>
                                <td className="border p-1">{act.date || "—"}</td>
                                <td className="border p-1">{act.teeth || "—"}</td>
                                <td className="border p-1">{act.codeActe || "—"}</td>
                                <td className="border p-1">{act.cotation || "—"}</td>
                                <td className="border p-1 text-right">{act.honoraires || "—"}</td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>

                {/* Signature */}
                <div className="flex justify-between items-end pt-5 mt-auto">
                  <div className="space-y-2">
                    <p className="text-muted-foreground" style={{ fontSize: '10pt' }}>Date et signature du médecin</p>
                    <div className="w-48 h-16 border-b border-slate-400"></div>
                  </div>
                  <div className="text-right space-y-1">
                    <p
                      className="font-bold focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                      style={{ fontSize: '12pt' }}
                      contentEditable
                      suppressContentEditableWarning
                    >
                      {formData.doctorName}
                    </p>
                    <p
                      className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                      style={{ fontSize: '10pt' }}
                      contentEditable
                      suppressContentEditableWarning
                    >
                      {formData.doctorSpecialty}
                    </p>
                  </div>
                </div>
              </div>
            </Card>
          </div>
        </div>
        </div>
      </div>
    </div>
  )
}

