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
import { medicationsApi } from "@/lib/api/medications"
import type { PatientDto, MedicalDocumentDto, ProcedureTypeDto, DentalRecordDto, CnamNomenclatureEntryDto, MedicationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { saveAs } from "file-saver"
import { Document, Packer, Paragraph, HeadingLevel, AlignmentType } from "docx"

// Certificat médical (FR-2). The ordre label (FR-2.4) and the mandatory deontological mention (FR-2.3) —
// kept in sync with the backend PdfGenerationService/CertificatTextBuilder so the preview, the Word export,
// and the generated PDF read identically.
const CERTIFICAT_ORDRE_LABEL = "Ordre National des Médecins Dentistes (CNOMDT)"
const CERTIFICAT_MANDATORY_MENTION =
  "Certificat établi à la demande de l'intéressé(e) et remis en main propre."

// A prescription medication line. `medicationId` + `dci` are set when the line is picked from the catalog
// (dci is a snapshot of the drug's molecules at selection time); both are absent for a free-text entry.
type MedicationLine = {
  name: string
  dosage: string
  timesPerDay: string
  duration: string
  medicationId?: string
  dci?: string[]
}

// Medication Item Component
function MedicationItem({
  medication,
  onUpdate,
  onRemove,
  catalog
}: {
  medication: MedicationLine
  onUpdate: (med: MedicationLine) => void
  onRemove: () => void
  catalog: MedicationDto[]
}) {
  const [lookupOpen, setLookupOpen] = useState(false)
  // Printed/displayed label for a catalog entry: "Marque Dosage Forme" (empty parts dropped).
  const catalogLabel = (m: MedicationDto) => [m.brandName, m.strength, m.form].filter(Boolean).join(" ")

  return (
    <div className="p-4 border rounded-lg space-y-3">
      <div className="grid grid-cols-[1fr_2.5rem] gap-2">
        <div className="space-y-3">
          <div className="flex flex-col gap-2">
            <Label className="text-xs text-muted-foreground h-4">Nom du médicament</Label>
            <div className="flex gap-2">
              <Input
                type="text"
                placeholder="Ex: Amoxicilline"
                value={medication.name || ""}
                onChange={(e) => {
                  // Manual edit → free-text entry: drop any catalog link + molecule snapshot.
                  onUpdate({ ...medication, name: e.target.value, medicationId: undefined, dci: [] })
                }}
                className="h-10 flex-1"
              />
              <Popover open={lookupOpen} onOpenChange={setLookupOpen} modal>
                <PopoverTrigger asChild>
                  <Button type="button" variant="outline" size="sm" className="h-10 px-3 shrink-0" title="Choisir dans le catalogue">
                    <Search className="w-4 h-4" />
                    <span className="sr-only">Choisir dans le catalogue</span>
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="p-0 w-80" align="end">
                  <Command>
                    <CommandInput placeholder="Rechercher un médicament..." />
                    <CommandList>
                      <CommandEmpty>Aucun médicament trouvé.</CommandEmpty>
                      <CommandGroup>
                        {catalog.map((m) => (
                          <CommandItem
                            key={m.id}
                            value={`${m.brandName} ${m.strength} ${m.form} ${m.dcis.join(" ")}`}
                            onSelect={() => {
                              // Name = brand + form only; the strength goes to the Dosage field (not crammed
                              // into "Nom du médicament"). The search list above still shows/searches the full label.
                              onUpdate({
                                ...medication,
                                name: [m.brandName, m.form].filter(Boolean).join(" "),
                                dosage: m.strength,
                                medicationId: m.id,
                                dci: m.dcis,
                              })
                              setLookupOpen(false)
                            }}
                          >
                            <div className="flex flex-col">
                              <span className="text-sm font-medium">{catalogLabel(m)}</span>
                              <span className="text-xs text-muted-foreground">
                                {m.dcis.join(", ")}{m.isProvisional ? " · à vérifier" : ""}
                              </span>
                            </div>
                          </CommandItem>
                        ))}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
            </div>
            {medication.medicationId && medication.dci && medication.dci.length > 0 && (
              <span className="text-xs text-muted-foreground">DCI : {medication.dci.join(", ")}</span>
            )}
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
  // Patient-page deep-link (P2-A): launch the editor with the patient already selected, so prescribing
  // mid-visit no longer means leaving the patient and re-searching. Only used when creating.
  const urlPatientId = searchParams.get('patientId')

  const [selectedPatient, setSelectedPatient] = useState<string>("")

  const [patientSearchOpen, setPatientSearchOpen] = useState(false)
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [filteredPatients, setFilteredPatients] = useState<PatientDto[]>([])
  const [patientSearchQuery, setPatientSearchQuery] = useState("")
  const [loadingPatients, setLoadingPatients] = useState(false)
  const [saving, setSaving] = useState(false)
  const [documentId, setDocumentId] = useState<string | null>(urlDocumentId)
  const [loadingDocument, setLoadingDocument] = useState(false)
  // Set once "Renouveler" (P2-B) forks a loaded document into a new draft, so the edit-load effect below
  // does not immediately reload the original when we clear documentId.
  const renewedRef = useRef(false)

  const [formFields, setFormFields] = useState({
    date: new Date().toISOString().split("T")[0],
    medications: [] as MedicationLine[],
    content: "", // Liaison: legacy free-text body (kept so pre-Part-E letters round-trip; new letters use the guided fields below)
    procedures: [] as Array<{ name: string; cost: number; procedureTypeId?: string }>, // Array of procedures with costs
    totalCost: "",
    duration: "",
    doctorOrderNumber: "", // Certificat: CNOMDT ordre (FR-2.5 — pre-filled from the doctor's profile, read-only)
    startDate: "", // Certificat: repos médical start date (FR-2.1 — optional)
    objetMotif: "", // Certificat: free objet/motif body (FR-2.1)
    // Liaison — external confrère destinataire (FR-4.1, free text) + guided clinical fields (FR-4.2, all optional)
    recipientName: "",
    recipientSpecialty: "",
    recipientAddress: "",
    motif: "",
    examenClinique: "",
    examenRadiologique: "",
    actesRealises: "",
    prescriptions: "",
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
  // Admin-managed VLC values (lettre clé → dinar value), fed to the indicative reimbursement estimate.
  const [cnamLetterValues, setCnamLetterValues] = useState<Record<string, number>>({})
  const [medicationCatalog, setMedicationCatalog] = useState<MedicationDto[]>([])
  const [openActLookup, setOpenActLookup] = useState<number | null>(null)
  // Certificat: whether the optional "Repos médical" block is expanded (opened automatically when editing a
  // document that already carries repos data).
  const [reposOpen, setReposOpen] = useState(false)

  const documentRef = useRef<HTMLDivElement>(null)

  // Load clinic and doctor info
  const { doctors, currentUserDoctor } = useDoctors()
  
  // FR-4.1: the liaison recipient is a free-text external confrère (no longer selected from clinic doctors).
  // These derived names feed the recipient snapshot columns (RecipientDoctorName/Specialty) unchanged.
  const recipientDoctorName = formFields.recipientName || ""
  const recipientDoctorSpecialty = formFields.recipientSpecialty || ""
  const [clinicInfo, setClinicInfo] = useState<{
    name: string
    address: string
    city: string
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
            city: status.clinic.city || "",
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

  // FR-2.5: pre-fill the certificat CNOMDT ordre from the current doctor's profile (the field is read-only —
  // no longer retyped per certificat). Only fills when empty, so a legacy document's stored ordre is kept.
  // Re-runs on `documentId` too: when editing a legacy certificat with an empty stored ordre, the document
  // load sets it to "" (possibly after the doctor already loaded); depending on `documentId` re-applies the
  // profile fallback afterwards instead of leaving the read-only field blank ([Numéro] in the render).
  useEffect(() => {
    const ordre = currentUserDoctor?.ordreNumberCnomdt
    if (ordre) {
      setFormFields((prev) => (prev.doctorOrderNumber ? prev : { ...prev, doctorOrderNumber: ordre }))
    }
  }, [currentUserDoctor, documentId])

  // Get doctor info (current user's doctor or first doctor in list)
  const selectedDoctor = currentUserDoctor || (doctors.length > 0 ? doctors[0] : null)
  
  const formData = {
    doctorName: selectedDoctor?.name || "Dr. [Nom]",
    doctorSpecialty: selectedDoctor?.specialty || "[Spécialité]",
    clinicName: clinicInfo?.name || "[Nom du cabinet]",
    clinicAddress: clinicInfo?.address || "[Adresse]",
    clinicCity: clinicInfo?.city || "",
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

  // Patient-page deep-link (P2-A): pre-select the patient when launched from the patient documents tab.
  // Skipped when editing an existing document or coming from a post-visit appointment link.
  useEffect(() => {
    if (!urlPatientId || urlDocumentId || urlAppointmentId) return
    setSelectedPatient(urlPatientId)
  }, [urlPatientId, urlDocumentId, urlAppointmentId])

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
    if (urlDocumentId && urlDocumentId !== documentId && !renewedRef.current) {
      const loadDocument = async () => {
        try {
          setLoadingDocument(true)
          const doc = await medicalDocumentsApi.get(urlDocumentId)
          setDocumentId(doc.id)
          setSelectedPatient(doc.patientId)

          // Parse and set form fields from contentJson
          const content = JSON.parse(doc.contentJson)
          
          // Handle medications: support both old string format and new array format
          let medications: MedicationLine[] = []
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
            duration: content.duration || "",
            // FR-2.5: the ordre is pre-filled from the doctor's profile (set by the effect below); a value
            // stored on a legacy document is still read back so an older certificat keeps rendering its ordre.
            doctorOrderNumber: content.doctorOrderNumber || "",
            startDate: content.startDate || "",
            objetMotif: content.objetMotif || "",
            // Liaison: recipient name/specialty come from the snapshot columns (works for legacy internal-
            // recipient letters too, LIA-5); address + guided fields from ContentJson (FR-4.1/FR-4.2).
            recipientName: doc.recipientDoctorName || "",
            recipientSpecialty: doc.recipientDoctorSpecialty || "",
            recipientAddress: content.recipientAddress || "",
            motif: content.motif || "",
            examenClinique: content.examenClinique || "",
            examenRadiologique: content.examenRadiologique || "",
            actesRealises: content.actesRealises || "",
            prescriptions: content.prescriptions || "",
          })

          // Expand the optional repos block when the loaded certificat already carries repos data.
          if (documentType === "certificat") {
            setReposOpen(Boolean(content.startDate || content.duration))
          }

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

  // Load the DB-backed CNAM nomenclature + VLC values once when editing a bulletin (searched client-side).
  useEffect(() => {
    if (documentType !== "bulletin-cnam") return
    let cancelled = false
    ;(async () => {
      try {
        const [entries, letterValues] = await Promise.all([
          cnamNomenclatureApi.list(),
          cnamNomenclatureApi.listLetterValues(),
        ])
        if (!cancelled) {
          setCnamNomenclature(entries)
          setCnamLetterValues(Object.fromEntries(letterValues.map((v) => [v.lettreCle.toUpperCase(), v.value])))
        }
      } catch {
        if (!cancelled) {
          setCnamNomenclature([])
          setCnamLetterValues({})
        }
      }
    })()
    return () => { cancelled = true }
  }, [documentType])

  // Load the medication catalog once when editing a prescription (searched client-side in the picker).
  useEffect(() => {
    if (documentType !== "prescription") return
    let cancelled = false
    ;(async () => {
      try {
        const meds = await medicationsApi.list()
        if (!cancelled) setMedicationCatalog(meds)
      } catch {
        if (!cancelled) setMedicationCatalog([])
      }
    })()
    return () => { cancelled = true }
  }, [documentType])

  const resetForm = () => {
    setSelectedPatient("")
    setDocumentId(null)
    setFormFields({
      date: new Date().toISOString().split("T")[0],
      medications: [],
      content: "",
      procedures: [],
      totalCost: "",
      duration: "",
      doctorOrderNumber: "",
      startDate: "",
      objetMotif: "",
      recipientName: "",
      recipientSpecialty: "",
      recipientAddress: "",
      motif: "",
      examenClinique: "",
      examenRadiologique: "",
      actesRealises: "",
      prescriptions: "",
    })
    setBulletinFields({ careType: "APCI", apciCode: "", actsFrom: "", actsTo: "", acts: [] })
  }

  // Renouveler (P2-B): fork the loaded ordonnance into a new draft — same patient + same medications,
  // dated today — so renewing keeps the original in history instead of overwriting it. Clearing
  // documentId flips the save path to "create"; renewedRef stops the edit-load effect from reloading.
  const renewDocument = () => {
    renewedRef.current = true
    setDocumentId(null)
    setFormFields((prev) => ({ ...prev, date: new Date().toISOString().split("T")[0] }))
    toast.success("Ordonnance dupliquée", {
      description: "Modifiez si besoin, puis enregistrez pour créer une nouvelle ordonnance. L'originale est conservée.",
      duration: 4000,
    })
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

  // Indicative reimbursement (catalog-backed acts only). Editor-only — never persisted / never on the PDF.
  // Uses the admin-managed VLC values + the age-based CNAM rate (70% ages 4–18 inclusive, 60% otherwise),
  // computed from the patient's DOB and the act's care date (mirrors the authoritative backend calculator).
  const bulletinPatientDob = patients.find((p) => p.id === selectedPatient)?.dateOfBirth ?? null
  const actCareDate = (actDate: string) =>
    actDate ? new Date(actDate) : formFields.date ? new Date(formFields.date) : new Date()
  const actReimbursement = (act: { cotation: string; date: string }) =>
    estimateReimbursement(act.cotation, cnamLetterValues, bulletinPatientDob, actCareDate(act.date))
  const bulletinEstimateTotal = bulletinFields.acts.reduce((sum, act) => {
    const e = actReimbursement(act)
    return e != null ? sum + e : sum
  }, 0)
  const hasAnyBulletinEstimate = bulletinFields.acts.some((act) => actReimbursement(act) != null)

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
      // Real date of birth (dd/MM/yyyy) for the BS1 "Le malade" box — persisted in the content so the
      // saved/background-job PDF shows the DOB, not the patient's age.
      patientDateOfBirth: patient.dateOfBirth
        ? new Date(patient.dateOfBirth).toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit", year: "numeric" })
        : "",
      patientPhone: patient.phoneNumber || "",
      doctorCodeProfessionnel: selectedDoctor?.codeProfessionnelSante || "",
    }
  }

  // Certificat médical (FR-2) — the single source of truth for the body text, shared by the read-only
  // preview and the Word export (the PDF is rendered server-side by CertificatTextBuilder with the same
  // shape). objet/motif is the primary body; the repos médical clause is rendered only when a duration is set.
  const formatFrDate = (value?: string) =>
    value
      ? new Date(value).toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit", year: "numeric" })
      : ""

  const certificatBodyParagraphs = (): string[] => {
    const specialty =
      formData.doctorSpecialty && formData.doctorSpecialty !== "[Spécialité]"
        ? formData.doctorSpecialty
        : "médecin dentiste"
    const ordre = formFields.doctorOrderNumber || "[Numéro]"
    const address = formData.clinicAddress || "[Adresse]"
    const patientName = patientData ? getPatientName(patientData) : "[Nom du patient]"
    const dob = patientData?.dateOfBirth ? formatFrDate(patientData.dateOfBirth) : "[JJ/MM/AAAA]"

    const paras = [
      `Je soussigné(e), Docteur ${formData.doctorName}, ${specialty}, inscrit(e) à l'${CERTIFICAT_ORDRE_LABEL} sous le n° ${ordre}, exerçant à ${address}, certifie avoir examiné ce jour ${patientName}, né(e) le ${dob}.`,
    ]
    if (formFields.objetMotif && formFields.objetMotif.trim()) {
      paras.push(formFields.objetMotif.trim())
    }
    if (formFields.duration && formFields.duration.trim()) {
      const plural = parseInt(formFields.duration) > 1 ? "s" : ""
      let repos = `Son état de santé nécessite un repos médical d'une durée de ${formFields.duration.trim()} jour${plural}`
      if (formFields.startDate) repos += ` à compter du ${formatFrDate(formFields.startDate)}`
      repos += "."
      paras.push(repos)
    }
    return paras
  }

  // Lettre de liaison (FR-4) — the single source of truth for the body sections, shared by the read-only
  // preview and the Word export (the PDF is rendered server-side by LiaisonContent with the same shape).
  // Only filled guided fields render; a legacy letter's free-text body is shown when no guided field is set.
  const liaisonSections = (): { heading: string | null; body: string }[] => {
    const guided = [
      { heading: "Motif", value: formFields.motif },
      { heading: "Examen clinique", value: formFields.examenClinique },
      { heading: "Examen radiologique", value: formFields.examenRadiologique },
      { heading: "Actes réalisés", value: formFields.actesRealises },
      { heading: "Prescriptions", value: formFields.prescriptions },
    ]
    const sections = guided
      .filter((g) => g.value && g.value.trim())
      .map((g) => ({ heading: g.heading as string | null, body: g.value.trim() }))
    if (sections.length === 0 && formFields.content && formFields.content.trim()) {
      sections.push({ heading: null, body: formFields.content.trim() })
    }
    return sections
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
      // FR-4: external recipient address + guided clinical fields ride in ContentJson (name/specialty go
      // through the recipient snapshot columns). `content` is kept for legacy-letter round-trip only.
      content.content = formFields.content || "";
      content.recipientAddress = formFields.recipientAddress || "";
      content.motif = formFields.motif || "";
      content.examenClinique = formFields.examenClinique || "";
      content.examenRadiologique = formFields.examenRadiologique || "";
      content.actesRealises = formFields.actesRealises || "";
      content.prescriptions = formFields.prescriptions || "";
    } else if (documentType === "honoraires") {
      // Serialize procedures array as JSON string
      content.procedures = Array.isArray(formFields.procedures)
        ? JSON.stringify(formFields.procedures)
        : "";
      content.totalCost = formFields.totalCost || "0,00 €";
    } else if (documentType === "certificat") {
      // FR-2.2: one consistent certificat content schema across save (handleSave) and render
      // (buildDocumentData) — objet/motif + ordre + repos start/duration all round-trip through ContentJson.
      content.objetMotif = formFields.objetMotif || "";
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
        if (formFields.recipientAddress) {
          paragraphs.push(new Paragraph({ text: formFields.recipientAddress }));
        }
        paragraphs.push(new Paragraph({ text: "" }));
      }

      // Date
      paragraphs.push(
        new Paragraph({
          text: `${formData.clinicCity ? `${formData.clinicCity}, le` : "Le"} ${format(new Date(formFields.date), "dd MMMM yyyy", { locale: fr })}`,
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
        const sections = liaisonSections();
        if (sections.length === 0) {
          paragraphs.push(new Paragraph({ text: "—" }));
        } else {
          sections.forEach((s) => {
            if (s.heading) {
              paragraphs.push(new Paragraph({ text: s.heading, heading: HeadingLevel.HEADING_2 }));
            }
            paragraphs.push(new Paragraph({ text: s.body }));
          });
        }
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
        // FR-2: mirror the PDF renderer — objet/motif body + optional repos clause + CNOMDT label +
        // mandatory deontological mention. Keeps the Word export consistent with the generated PDF.
        certificatBodyParagraphs().forEach((text) =>
          paragraphs.push(new Paragraph({ text }))
        );
        paragraphs.push(
          new Paragraph({ text: "" }),
          new Paragraph({ text: CERTIFICAT_MANDATORY_MENTION })
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

    // FR-4.1: the confrère destinataire name is the only required liaison field — enforce it client-side so
    // the user gets immediate feedback (the backend also rejects it on create/update). Pairs with the label's "*".
    if (documentType === "liaison" && !recipientDoctorName.trim()) {
      toast.error("Destinataire requis", {
        description: "Le nom du confrère destinataire est obligatoire pour une lettre de liaison.",
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
      // FR-4: same ContentJson shape the renderer reads (buildDocumentData) — address + guided fields;
      // `content` kept for legacy-letter round-trip. Recipient name/specialty go through the update payload.
      content.content = formFields.content
      content.recipientAddress = formFields.recipientAddress
      content.motif = formFields.motif
      content.examenClinique = formFields.examenClinique
      content.examenRadiologique = formFields.examenRadiologique
      content.actesRealises = formFields.actesRealises
      content.prescriptions = formFields.prescriptions
      } else if (documentType === "honoraires") {
        content.procedures = formFields.procedures
        content.totalCost = formFields.totalCost
      } else if (documentType === "certificat") {
        // FR-2.2: same schema the renderer reads (buildDocumentData) — previously this path saved
        // reason/notes while the renderer read objetMotif/startDate/doctorOrderNumber, silently dropping data.
        content.objetMotif = formFields.objetMotif
        content.doctorOrderNumber = formFields.doctorOrderNumber
        content.startDate = formFields.startDate
        content.duration = formFields.duration
        // Persist the patient DOB so the background-job PDF renders it (not only the download path).
        if (patientData?.dateOfBirth) {
          content.patientDateOfBirth = patientData.dateOfBirth
        }
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

  // ---- CNAM BS1 live preview (bulletin-cnam only) ----
  // Embed the real generated BS1 PDF in the preview pane, regenerated ~800ms after editing pauses,
  // via the same medicalDocumentsApi.generatePdfForDownload(buildDocumentData()) the Download button uses.
  const [bs1PreviewUrl, setBs1PreviewUrl] = useState<string | null>(null)
  const [bs1PreviewLoading, setBs1PreviewLoading] = useState(false)
  const [bs1PreviewError, setBs1PreviewError] = useState(false)
  const bs1UrlRef = useRef<string | null>(null)

  // Serialized snapshot of the inputs that feed the BS1 PDF; the effect re-runs only when it changes.
  // null when no patient is selected (buildDocumentData() returns null) — short-circuits without an API call (AC-5).
  const bs1DocumentData = documentType === "bulletin-cnam" ? buildDocumentData() : null
  const bs1DataKey = bs1DocumentData ? JSON.stringify(bs1DocumentData) : null

  useEffect(() => {
    if (documentType !== "bulletin-cnam") return
    // No patient / missing required data → neutral state, no API call (AC-5).
    if (!bs1DataKey) {
      setBs1PreviewLoading(false)
      setBs1PreviewError(false)
      return
    }
    let cancelled = false
    // Debounce: regenerate ~800ms after edits pause. Cleanup cancels a pending/in-flight render so a
    // superseded response never overwrites a newer one (AC-3) and none runs after unmount/type change.
    const timer = setTimeout(async () => {
      setBs1PreviewLoading(true)
      setBs1PreviewError(false)
      try {
        const blob = await medicalDocumentsApi.generatePdfForDownload(JSON.parse(bs1DataKey))
        if (cancelled) return
        const url = URL.createObjectURL(blob)
        // Revoke the previous object URL so blobs don't leak (AC-3).
        if (bs1UrlRef.current) URL.revokeObjectURL(bs1UrlRef.current)
        bs1UrlRef.current = url
        setBs1PreviewUrl(url)
      } catch {
        // Keep the last good preview; surface the error state (AC-4). Next successful edit recovers.
        if (!cancelled) setBs1PreviewError(true)
      } finally {
        if (!cancelled) setBs1PreviewLoading(false)
      }
    }, 800)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [bs1DataKey, documentType])

  // Revoke the last preview object URL on unmount to avoid leaking the blob.
  useEffect(() => {
    return () => {
      if (bs1UrlRef.current) {
        URL.revokeObjectURL(bs1UrlRef.current)
        bs1UrlRef.current = null
      }
    }
  }, [])

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

            {/* FR-4.1: external confrère destinataire — free text, no longer chosen from the clinic's doctors. */}
            {documentType === "liaison" && (
              <div className="space-y-3">
                <Label className="text-sm font-semibold text-foreground">Confrère destinataire</Label>
                <div className="space-y-2">
                  <Label htmlFor="recipientName" className="text-xs text-muted-foreground">Nom *</Label>
                  <Input
                    id="recipientName"
                    type="text"
                    placeholder="Ex: Dr Ahmed Ben Salah"
                    value={formFields.recipientName}
                    onChange={(e) => setFormFields({ ...formFields, recipientName: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientSpecialty" className="text-xs text-muted-foreground">Spécialité</Label>
                  <Input
                    id="recipientSpecialty"
                    type="text"
                    placeholder="Ex: Chirurgien maxillo-facial"
                    value={formFields.recipientSpecialty}
                    onChange={(e) => setFormFields({ ...formFields, recipientSpecialty: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientAddress" className="text-xs text-muted-foreground">Adresse</Label>
                  <Textarea
                    id="recipientAddress"
                    placeholder="Ex: 12 rue de la Santé, Tunis"
                    value={formFields.recipientAddress}
                    onChange={(e) => setFormFields({ ...formFields, recipientAddress: e.target.value })}
                    className="min-h-[60px]"
                  />
                </div>
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
                        catalog={medicationCatalog}
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

            {/* FR-4.2: guided clinical fields — all optional; empty ones are omitted from the letter. */}
            {documentType === "liaison" && (
              <>
                <div className="space-y-2">
                  <Label htmlFor="motif" className="text-sm font-semibold text-foreground">Motif</Label>
                  <Textarea
                    id="motif"
                    placeholder="Motif de l'adressage / de la demande d'avis"
                    value={formFields.motif}
                    onChange={(e) => setFormFields({ ...formFields, motif: e.target.value })}
                    className="min-h-[80px]"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="examenClinique" className="text-sm font-semibold text-foreground">Examen clinique</Label>
                  <Textarea
                    id="examenClinique"
                    placeholder="Constatations cliniques"
                    value={formFields.examenClinique}
                    onChange={(e) => setFormFields({ ...formFields, examenClinique: e.target.value })}
                    className="min-h-[80px]"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="examenRadiologique" className="text-sm font-semibold text-foreground">Examen radiologique</Label>
                  <Textarea
                    id="examenRadiologique"
                    placeholder="Résultats radiologiques (panoramique, rétro-alvéolaire…)"
                    value={formFields.examenRadiologique}
                    onChange={(e) => setFormFields({ ...formFields, examenRadiologique: e.target.value })}
                    className="min-h-[80px]"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="actesRealises" className="text-sm font-semibold text-foreground">Actes réalisés</Label>
                  <Textarea
                    id="actesRealises"
                    placeholder="Actes déjà effectués au cabinet"
                    value={formFields.actesRealises}
                    onChange={(e) => setFormFields({ ...formFields, actesRealises: e.target.value })}
                    className="min-h-[80px]"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="prescriptions" className="text-sm font-semibold text-foreground">Prescriptions</Label>
                  <Textarea
                    id="prescriptions"
                    placeholder="Prescriptions (posologie, durée)"
                    value={formFields.prescriptions}
                    onChange={(e) => setFormFields({ ...formFields, prescriptions: e.target.value })}
                    className="min-h-[80px]"
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
                {/* FR-2.1: the free objet/motif is the primary body (présence, soins en cours, aptitude…). */}
                <div className="space-y-2">
                  <Label htmlFor="objetMotif" className="text-sm font-semibold text-foreground">
                    Objet / motif du certificat
                  </Label>
                  <Textarea
                    id="objetMotif"
                    placeholder="Ex: certifie la présence de l'intéressé(e) ce jour ; soins dentaires en cours ; aptitude à la pratique sportive…"
                    value={formFields.objetMotif}
                    onChange={(e) => setFormFields({ ...formFields, objetMotif: e.target.value })}
                    className="min-h-[120px]"
                  />
                </div>

                {/* FR-2.5: CNOMDT ordre — pre-filled from the doctor's profile, not retyped per certificat. */}
                <div className="space-y-2">
                  <Label htmlFor="doctorOrderNumber" className="text-sm font-semibold text-foreground">
                    Numéro d'ordre (CNOMDT)
                  </Label>
                  <Input
                    id="doctorOrderNumber"
                    type="text"
                    value={formFields.doctorOrderNumber}
                    disabled
                    readOnly
                    placeholder="—"
                    className="h-11 bg-muted"
                  />
                  <p className="text-xs text-muted-foreground">
                    {formFields.doctorOrderNumber
                      ? "Renseigné automatiquement depuis votre profil."
                      : "Aucun numéro d'ordre sur votre profil. Ajoutez-le dans « Mon profil »."}
                  </p>
                </div>

                {/* FR-2.1: the repos médical block is one optional use, not the only template. */}
                <details
                  className="rounded-lg border px-4 py-3"
                  open={reposOpen}
                  onToggle={(e) => setReposOpen(e.currentTarget.open)}
                >
                  <summary className="cursor-pointer text-sm font-semibold text-foreground">
                    Repos médical (optionnel)
                  </summary>
                  <div className="space-y-4 pt-4">
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
                  </div>
                </details>
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
                        const actEstimate = actReimbursement(act)
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
                      <p className="text-xs text-muted-foreground">Estimation indicative, non contractuelle — montant réel fixé par la CNAM. Taux selon l'âge du patient (70&nbsp;% de 4 à 18&nbsp;ans, 60&nbsp;% sinon).</p>
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
              {documentId && documentType === "prescription" && (
                <Button
                  variant="outline"
                  className="w-full h-11 bg-transparent"
                  onClick={renewDocument}
                  disabled={saving}
                >
                  <FileText className="w-4 h-4 mr-2" />
                  Renouveler (nouvelle ordonnance)
                </Button>
              )}
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
            {documentType === "bulletin-cnam" ? (
              <>
                <div className="mb-6 flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-muted-foreground">Aperçu du document</p>
                    <p className="text-xs text-muted-foreground mt-1">Aperçu en direct du bulletin BS1 généré</p>
                  </div>
                  <div className="text-sm text-muted-foreground">Format A4</div>
                </div>

                <div className="relative bg-white dark:bg-slate-900 shadow-2xl rounded-lg overflow-hidden min-h-[1123px] flex flex-col">
                  {!patientData ? (
                    <div className="flex-1 flex flex-col items-center justify-center gap-3 p-12 text-center">
                      <FileText className="w-12 h-12 text-muted-foreground/40" />
                      <p className="text-sm text-muted-foreground">
                        Sélectionnez un patient pour afficher l'aperçu du bulletin de soins CNAM.
                      </p>
                    </div>
                  ) : (
                    <>
                      {bs1PreviewUrl ? (
                        <iframe
                          src={bs1PreviewUrl}
                          title="Aperçu du bulletin de soins CNAM"
                          className="flex-1 w-full border-0"
                          style={{ minHeight: "1123px" }}
                        />
                      ) : (
                        <div className="flex-1 flex flex-col items-center justify-center gap-3 p-12 text-center">
                          {bs1PreviewError && !bs1PreviewLoading ? (
                            <>
                              <FileText className="w-12 h-12 text-red-400" />
                              <p className="text-sm font-medium text-foreground">Impossible de générer l'aperçu du PDF</p>
                              <p className="text-xs text-muted-foreground">
                                Une erreur s'est produite. Modifiez un champ pour réessayer.
                              </p>
                            </>
                          ) : (
                            !bs1PreviewLoading && (
                              <p className="text-sm text-muted-foreground">Préparation de l'aperçu…</p>
                            )
                          )}
                        </div>
                      )}

                      {bs1PreviewLoading && (
                        <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-white/70 dark:bg-slate-900/70 backdrop-blur-sm">
                          <Loader2 className="w-8 h-8 animate-spin text-blue-600" />
                          <p className="text-sm text-muted-foreground">Génération de l'aperçu…</p>
                        </div>
                      )}

                      {bs1PreviewError && !bs1PreviewLoading && bs1PreviewUrl && (
                        <div className="absolute inset-x-0 top-0 bg-red-50 dark:bg-red-950/40 border-b border-red-200 dark:border-red-900 px-4 py-2">
                          <p className="text-xs text-red-600 dark:text-red-400 text-center">
                            Impossible de mettre à jour l'aperçu — dernière version affichée. Modifiez un champ pour réessayer.
                          </p>
                        </div>
                      )}
                    </>
                  )}
                </div>
              </>
            ) : (
              <>
            <div className="mb-6 flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-muted-foreground">Aperçu du document</p>
                <p className="text-xs text-muted-foreground mt-1">Aperçu en lecture seule — modifiez via le formulaire</p>
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
                  >
                    {formData.clinicName}
                  </h1>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    {formData.clinicAddress}
                  </p>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    Tél: {formData.clinicPhone}
                  </p>
                  <p
                    className="font-bold focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    {formData.doctorName} - {formData.doctorSpecialty}
                  </p>
                </div>

                {/* Recipient (for liaison) */}
                {documentType === "liaison" && (
                  <div className="space-y-1 py-3 px-3">
                    <p style={{ fontSize: '11pt' }}>À l'attention de:</p>
                    <div
                      className="rounded px-1"
                    >
                      {recipientDoctorName ? (
                        <>
                          <p className="font-bold" style={{ fontSize: '12pt' }}>{recipientDoctorName}</p>
                          {recipientDoctorSpecialty && (
                            <p className="text-muted-foreground" style={{ fontSize: '11pt' }}>{recipientDoctorSpecialty}</p>
                          )}
                          {formFields.recipientAddress && (
                            <p className="text-muted-foreground whitespace-pre-wrap" style={{ fontSize: '11pt' }}>{formFields.recipientAddress}</p>
                          )}
                        </>
                      ) : (
                        <p className="text-muted-foreground italic" style={{ fontSize: '11pt' }}>Entrez le nom du confrère destinataire</p>
                      )}
                    </div>
                  </div>
                )}

                {/* Date */}
                <div className="text-right pb-2">
                  <p
                    className="focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1 inline-block"
                    style={{ fontSize: '11pt' }}
                  >
                    {formData.clinicCity ? `${formData.clinicCity}, le ` : "Le "}
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
                    // FR-4.2/FR-6.3: read-only preview — the guided form is the single source of truth (the
                    // old write-back box is gone). Only filled sections show; empty ones are omitted.
                    <div className="space-y-4" style={{ fontSize: '11pt' }}>
                      {liaisonSections().length === 0 ? (
                        <p className="text-muted-foreground italic">Renseignez les champs de la lettre de liaison…</p>
                      ) : (
                        liaisonSections().map((section, index) => (
                          <div key={index} className="space-y-1">
                            {section.heading && (
                              <p className="font-bold" style={{ fontSize: '12pt' }}>{section.heading}</p>
                            )}
                            <p className="whitespace-pre-wrap">{section.body}</p>
                          </div>
                        ))
                      )}
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
                    // FR-6.3: read-only preview — the left-hand form is the single source of truth. Rendered
                    // from the same shared builder as the Word export / PDF so all three read identically.
                    <div style={{ fontSize: '11pt', lineHeight: '1.8', textAlign: 'justify' }} className="space-y-3">
                      {certificatBodyParagraphs().map((paragraph, index) => (
                        <p key={index}>{paragraph}</p>
                      ))}
                      <p className="italic">{CERTIFICAT_MANDATORY_MENTION}</p>
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
                    >
                      {formData.doctorName}
                    </p>
                    <p
                      className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-blue-300 rounded px-1"
                      style={{ fontSize: '10pt' }}
                    >
                      {formData.doctorSpecialty}
                    </p>
                  </div>
                </div>
              </div>
            </Card>
              </>
            )}
          </div>
        </div>
        </div>
      </div>
    </div>
  )
}

