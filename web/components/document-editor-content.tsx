"use client"

import { useState, useRef, useEffect, useCallback } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import { Card } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import { EmptyState } from "@/components/ui/empty-state"
import {
  Printer,
  RotateCcw,
  Save,
  Search,
  ArrowLeft,
  FileText,
  Download,
  Loader2,
  Plus,
  X,
  Mail,
  Pill,
  ClipboardList,
  AlertTriangle,
  ExternalLink,
} from "lucide-react"
import { SendDocumentEmailDialog } from "@/components/send-document-email-dialog"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { DOCUMENT_EMAIL_KINDS } from "@/lib/api/document-emails"
import { formatDT, formatDateFr, quoteFr, toLocalIso, todayLocalIso } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useRouter, useParams, useSearchParams } from "next/navigation"
import { patientsApi } from "@/lib/api/patients"
import { appointmentsApi } from "@/lib/api/appointments"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import { clinicsApi } from "@/lib/api/clinics"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { estimateReimbursements, parseCotation } from "@/lib/api/dental-acts"
import { CnamCeilingNotice } from "@/components/cnam/cnam-ceiling-notice"
import { dentalActsApi } from "@/lib/api/dental-acts"
import { medicationsApi } from "@/lib/api/medications"
import {
  CNAM_IDENTIFIANT_DIGITS,
  cnamIdentifiantDigitCount,
  cnamLienRequiresRang,
  isKnownCnamLien,
  isKnownCnamRegime,
  isValidCnamIdentifiant,
} from "@/lib/cnam"
import type { PatientDto, DentalRecordDto, DentalActDto, MedicationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { specialtyLabel } from "@/lib/specialties"
import { ARRET_MAX_DAYS, TRAUMA_CAUSES, TRAUMA_CAUSE_LABELS_FR, type TraumaCause } from "@/lib/arret-travail"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { downloadBlob } from "@/lib/download"
import { Document, Packer, Paragraph, HeadingLevel, AlignmentType } from "docx"

// Certificat médical (FR-2). The ordre label (FR-2.4) and the mandatory deontological mention (FR-2.3) —
// kept in sync with the backend PdfGenerationService/CertificatTextBuilder so the preview, the Word export,
// and the generated PDF read identically.
const CERTIFICAT_ORDRE_LABEL = "Ordre National des Médecins Dentistes (CNOMDT)"
// Carries both halves the CNOM requires: the remise en main propre AND the finality — « pour faire valoir ce
// que de droit » is what states the certificate serves whatever lawful use the patient needs, rather than a
// purpose the practitioner has vouched for.
const CERTIFICAT_MANDATORY_MENTION =
  "Certificat établi à la demande de l'intéressé(e) et remis en main propre pour faire valoir ce que de droit."

// A prescription medication line. `medicationId` + `dci` are set when the line is picked from the catalog
// (dci is a snapshot of the drug's molecules at selection time); both are absent for a free-text entry.
type MedicationLine = {
  name: string
  dosage: string
  timesPerDay: string
  /** Voie d'administration — « par voie orale », « en application locale »… Free text: the norms name no closed list. */
  route?: string
  /** Quantité à délivrer (boîtes / unités) — what makes the line dispensable. */
  quantity?: string
  duration: string
  medicationId?: string
  dci?: string[]
}

/**
 * The one client-side rendering of a prescribed line, shared by the read-only preview and the Word export.
 *
 * ⚠️ Must stay identical to the server's `PrescriptionContent.FormatLine`, which renders the PDF — the two are
 * the same ordonnance seen twice. It exists because the preview and the Word export each carried their own copy
 * of this formatting, so adding the voie and the quantité would have made three implementations of what a
 * prescription line says.
 */
const formatMedicationLine = (med: MedicationLine): string => {
  let text = med.name?.trim() || "Médicament"
  if (med.dosage?.trim()) text += ` ${med.dosage.trim()}`
  if (med.timesPerDay?.trim()) text += `, ${med.timesPerDay.trim()}x par jour`
  if (med.route?.trim()) text += `, ${med.route.trim()}`
  if (med.duration?.trim()) {
    const days = Number.parseInt(med.duration, 10)
    text += ` pendant ${med.duration.trim()} jour${days > 1 ? "s" : ""}`
  }
  if (med.quantity?.trim()) text += ` — quantité : ${med.quantity.trim()}`
  const dci = (med.dci ?? []).map((d) => d?.trim()).filter(Boolean).join(", ")
  if (dci) text += ` (DCI : ${dci})`
  return text
}

/**
 * The renewal mention, mirroring the server's `PrescriptionContent`. Blank ⇒ the ordonnance is silent on
 * renewal (the default); "0"/"non" ⇒ explicitly non-renewable; anything else ⇒ a count.
 */
const formatRenewalMention = (renewals: string): string | null => {
  const value = renewals?.trim()
  if (!value) return null
  if (value === "0" || value.toLowerCase() === "non") return "Ordonnance non renouvelable."
  return `Ordonnance à renouveler ${value} fois.`
}

/**
 * What a clinical picker shows when its catalogue **failed to load** — never the same thing as an empty one.
 *
 * <p>The three catalogue reads in this editor each used to `catch { setState([]) }`. An empty picker and a
 * failed read then looked identical, and the reading a practitioner takes from an empty picker is « ce
 * catalogue n'a jamais été configuré » — so they free-text the médicament, which drops the dosage defaults and
 * the DCI/CNAM link the catalogue entry carries. On a prescription that is a silent loss of clinical data
 * caused by a transient network blip. Saying « n'a pas pu être chargé » plus a « Réessayer » is the whole fix,
 * and it belongs in one component because all three pickers must fail the same way.</p>
 */
function CatalogLoadFailed({ label, onRetry }: { label: string; onRetry: () => void }) {
  return (
    <EmptyState
      size="compact"
      icon={AlertTriangle}
      chipClassName="bg-warning-wash text-warning-ink"
      title={`${label} n'a pas pu être chargé.`}
      description="Ce n'est pas un catalogue vide — la lecture a échoué. Réessayez avant de saisir à la main."
      action={
        <Button type="button" variant="outline" size="sm" onClick={onRetry}>
          <RotateCcw className="w-4 h-4 mr-2" />
          Réessayer
        </Button>
      }
    />
  )
}

// Medication Item Component
function MedicationItem({
  medication,
  onUpdate,
  onRemove,
  catalog,
  catalogFailed,
  onRetryCatalog,
}: {
  medication: MedicationLine
  onUpdate: (med: MedicationLine) => void
  onRemove: () => void
  catalog: MedicationDto[]
  /** The catalogue read failed — the picker must say so instead of rendering « Aucun médicament ne correspond. ». */
  catalogFailed: boolean
  onRetryCatalog: () => void
}) {
  const [lookupOpen, setLookupOpen] = useState(false)
  // Printed/displayed label for a catalog entry: "Marque Dosage Forme" (empty parts dropped).
  const catalogLabel = (m: MedicationDto) => [m.brandName, m.strength, m.form].filter(Boolean).join(" ")

  return (
    <div className="p-4 border rounded-lg space-y-3">
      <div className="grid grid-cols-[1fr_2.5rem] gap-2">
        <div className="space-y-3">
          <div className="flex flex-col gap-2">
            <Label className="text-xs text-muted-foreground min-h-4">Nom du médicament</Label>
            <div className="flex gap-2">
              <Input
                type="text"
                placeholder="Ex : Amoxicilline"
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
                    <CommandInput placeholder="Rechercher un médicament…" />
                    <CommandList>
                      {catalogFailed ? (
                        <CatalogLoadFailed label="Le catalogue des médicaments" onRetry={onRetryCatalog} />
                      ) : (
                        <>
                          <CommandEmpty>Aucun médicament ne correspond.</CommandEmpty>
                          <CommandGroup>
                            {catalog.map((m) => (
                              <CommandItem
                                key={m.id}
                                value={`${m.brandName} ${m.strength} ${m.form} ${m.dcis.join(" ")}`}
                                onSelect={() => {
                                  // Name = brand + form only; the strength goes to the Dosage field (not crammed
                                  // into « Nom du médicament »). The search list above still searches the full label.
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
                        </>
                      )}
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
            <Label className="text-xs text-muted-foreground min-h-4">Dosage</Label>
            <Input
              type="text"
              placeholder="Ex : 500mg"
              value={medication.dosage || ""}
              onChange={(e) => {
                onUpdate({ ...medication, dosage: e.target.value })
              }}
              className="h-10 w-full"
            />
          </div>
          {/*
            `grid-cols-1 … sm:grid-cols-2`, not a bare `grid-cols-2` (defect #3). Below `md:` the form column is
            the FULL viewport width, so a two-column grid inside this card gives each field ~120 px on a 360 px
            phone — and « Voie d'administration » is wider than that, so the label wrapped to a second line
            *inside* a fixed `h-4` box and overlapped the Input under it. The labels are `min-h-4` for the same
            reason: the fixed box was what turned a wrap into an overlap rather than into a taller row.
          */}
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Fois par jour</Label>
              <Input
                type="number"
                min="1"
                placeholder="Ex : 3"
                value={medication.timesPerDay || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, timesPerDay: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Durée (jours)</Label>
              <Input
                type="number"
                min="1"
                placeholder="Ex : 7"
                value={medication.duration || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, duration: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
          </div>
          {/* Voie + quantité — required of a prescription (R.5132-3): a posologie with no route and no quantity
              is not a dispensable instruction. Both optional, like every other norm field. */}
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Voie d&apos;administration</Label>
              <Input
                type="text"
                placeholder="Ex : par voie orale"
                value={medication.route || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, route: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Quantité</Label>
              <Input
                type="text"
                placeholder="Ex : 1 boîte"
                value={medication.quantity || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, quantity: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
          </div>
        </div>
        {/* Named after the medication it removes. Unlabelled, this announced « bouton » and nothing else —
            on a prescription, in a list where every row's remove control is identical. The name is optional
            while the row is still being typed, hence the fallback. */}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          className="h-10 w-10"
          title="Retirer ce médicament"
          aria-label={
            medication.name
              ? `Retirer ${medication.name} de l'ordonnance`
              : "Retirer ce médicament de l'ordonnance"
          }
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
  /** The patient list read failed — never rendered as « Aucun patient disponible ». */
  const [patientsFailed, setPatientsFailed] = useState(false)
  const [saving, setSaving] = useState(false)
  // ⚠️ Seeded null, NEVER from the URL. Seeding it made the edit-load effect's `urlDocumentId !== documentId`
  // guard false on the very first render, so reopening a stored document never issued its GET and « Mettre à
  // jour » PUT an empty body over the stored prescription.
  const [documentId, setDocumentId] = useState<string | null>(null)
  // True from the first render when the URL names a document, so the save path cannot be taken as "create"
  // while the stored content is still in flight.
  const [loadingDocument, setLoadingDocument] = useState(Boolean(urlDocumentId))
  /** The edit-load GET failed — the form holds no stored content, so a save must not create a second document. */
  const [documentLoadFailed, setDocumentLoadFailed] = useState(false)
  const [documentReload, setDocumentReload] = useState(0)
  const [documentVersion, setDocumentVersion] = useState<number | undefined>(undefined)
  // Set once "Renouveler" (P2-B) forks a loaded document into a new draft, so the edit-load effect below
  // does not immediately reload the original when we clear documentId.
  const renewedRef = useRef(false)

  const [formFields, setFormFields] = useState({
    date: todayLocalIso(),
    medications: [] as MedicationLine[],
    content: "", // Liaison: the PRIMARY free-text body (« Corps de la lettre / Synthèse clinique »)
    duration: "",
    // Ordonnance: renouvellement — governs the whole document, so it is not per medication line.
    renewals: "",
    // Norm identity values captured on the document (R.5132-3). Sexe is prefilled from the patient record;
    // poids is typed per-document and deliberately never stored on the patient (a stale weight that looks
    // verified is worse than a blank field).
    patientSex: "",
    patientWeightKg: "",
    doctorOrderNumber: "", // Certificat: CNOMDT ordre (FR-2.5 — pre-filled from the doctor's profile, read-only)
    startDate: "", // Certificat: repos médical start date (FR-2.1 — optional)
    objetMotif: "", // Certificat: free objet/motif body (FR-2.1)
    // Liaison — external confrère destinataire (free text) + the norm sections, ALL optional. Only the
    // destinataire is ever required: the doctor writes the letter in `content` and fills whichever of these
    // the case calls for (décret n° 2016-995 + HAS).
    recipientName: "",
    recipientSpecialty: "",
    recipientAddress: "",
    recipientEmail: "",
    medecinTraitant: "",
    motif: "",
    examenClinique: "",
    examenRadiologique: "",
    actesRealises: "",
    traitementEnCours: "",
    prescriptions: "",
    examensEnAttente: "",
    consignesSuivi: "",
    piecesJointes: "",
  })

  // Bulletin de soins CNAM (BS1) — care type + acts table (pre-filled from the patient's dental records).
  const [bulletinFields, setBulletinFields] = useState<{
    careType: string
    apciCode: string
    actsFrom: string
    actsTo: string
    acts: Array<{ date: string; teeth: string; codeActe: string; cotation: string; honoraires: string }>
  }>({ careType: "APCI", apciCode: "", actsFrom: "", actsTo: "", acts: [] })

  /*
   * Arrêt de travail — the fields of the CNAM **P 061** form's practitioner half (L11).
   *
   * Its own state object rather than more keys on `formFields`, for the same reason `bulletinFields` is separate:
   * these are the fields of one specific official form, they are read by one branch, and folding them into the
   * shared bag is how the certificat's `duration` and this one's `days` end up being the same key by accident.
   *
   * ⚠️ `traumaCause` and `hospitalised` hold **stored** values, not labels (`lib/arret-travail.ts`) — the server's
   * renderer matches them to decide which box to tick, exactly like the bulletin's régime and lien.
   */
  const [arretFields, setArretFields] = useState<{
    days: string
    fromDate: string
    outingsFrom: string
    outingsTo: string
    traumaCause: string
    hospitalised: string
    motif: string
  }>({ days: "", fromDate: todayLocalIso(), outingsFrom: "", outingsTo: "", traumaCause: "", hospitalised: "", motif: "" })

  const [dentalRecords, setDentalRecords] = useState<DentalRecordDto[]>([])
  /*
   * K1 — the act picker reads the **DCH dental-act catalogue** (`DentalActCode`), the only act catalogue.
   *
   * The two catalogues are disjoint and this one was reading the wrong one: `CnamCatalogSeed` seeds 26 internal
   * mnemonics as its `CodeActe` (`DETART`, `OBT-2F`, `EXT-SIMPLE`…), while the genuine Tunisian nomenclature — the
   * 100 real `DCH010010`…`DCH060150` codes — lives in `DentalActCode`. The picker wrote the mnemonic straight into
   * the row the server stamps onto the BS1, so **every bulletin filled from this picker was rejected at the caisse
   * on the code column**. `DentalActCode` is a strict superset (same ten fields plus `DefaultFee` and
   * `RequiresAccordPrealable`), which is what made the swap a read-side change.
   *
   * ⚠️ The *stored* acts of an existing bulletin are deliberately untouched. A document already saved with a
   * `DETART`-style code must still open and print: those rows are a snapshot, the renderer stamps whatever the row
   * holds, and re-pointing the picker must not rewrite history.
   */
  const [dentalActCatalog, setDentalActCatalog] = useState<DentalActDto[]>([])
  const [medicationCatalog, setMedicationCatalog] = useState<MedicationDto[]>([])
  const [openActLookup, setOpenActLookup] = useState<number | null>(null)

  /*
   * ── Why each of the three reads below carries a `…Failed` flag AND a reload counter (defect #1) ────────────
   *
   * All three used to swallow their error into an empty array. On a clinical picker that is not a graceful
   * degradation, it is a **wrong answer**: an empty list asserts « ce catalogue est vide », the practitioner
   * concludes it was never configured, and their next move is to type the médicament / the code acte by hand —
   * which silently discards the dosage defaults, the DCI snapshot and the CNAM cotation the catalogue entry
   * exists to supply. The document is then saved and printed with less data than the software had.
   *
   * The reload counter rather than a `useCallback` loader: the reads already live in effects with a `cancelled`
   * guard, and bumping a dependency reuses that guard for the retry instead of writing a second code path that
   * can race the first one.
   */
  const [dentalRecordsFailed, setDentalRecordsFailed] = useState(false)
  const [dentalRecordsReload, setDentalRecordsReload] = useState(0)
  const [dentalActCatalogFailed, setDentalActCatalogFailed] = useState(false)
  const [dentalActCatalogReload, setDentalActCatalogReload] = useState(0)
  const [medicationCatalogFailed, setMedicationCatalogFailed] = useState(false)
  const [medicationCatalogReload, setMedicationCatalogReload] = useState(0)
  // Certificat: whether the optional "Repos médical" block is expanded (opened automatically when editing a
  // document that already carries repos data).
  const [reposOpen, setReposOpen] = useState(false)
  // Liaison: whether the optional norm sections are expanded. Collapsed by default so the free-text body is
  // what the doctor meets first; opened automatically when a loaded letter already fills one of them, since
  // collapsing a section that holds text would hide content rather than merely fold it away.
  const [liaisonExtrasOpen, setLiaisonExtrasOpen] = useState(false)
  // « Envoyer par e-mail » — only reachable once the document has been saved and therefore has an id.
  const [emailOpen, setEmailOpen] = useState(false)

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

  // Pre-fill the patient's sexe from their record, same fill-if-empty rule as the ordre above: a legacy
  // document's stored value wins, and the field stays editable because the value is *shown* on the document —
  // a box the practitioner reads must be a box they can correct.
  useEffect(() => {
    const gender = patients.find((p) => p.id === selectedPatient)?.gender
    if (gender) {
      setFormFields((prev) => (prev.patientSex ? prev : { ...prev, patientSex: gender }))
    }
  }, [patients, selectedPatient, documentId])

  /*
   * ── K3: the treating practitioner is chosen, never guessed ──────────────────────────────────────────────────
   *
   * This was `currentUserDoctor || doctors[0]` — a silent fall-back to whoever happens to be first in the roster
   * whenever the logged-in user has no linked `Doctor`, which a secretary never has. On a bulletin de soins that is
   * not a cosmetic default: `doctorCodeProfessionnel` (the code conventionnel `StampActs` prints on **every** act
   * row) came from that guess, so a secretary filing a bulletin attributed the acts to the wrong practitioner, with
   * nothing on screen naming anyone. There was no `setSelectedDoctor` in this file at all.
   *
   * The selection is now explicit state with a *defaulting* effect below, and for a bulletin there is **no
   * fall-back**: nothing selected is a refusal at Save (see `bulletinProblems`), which is the honest outcome —
   * a bulletin nobody can be named on is one the caisse would reject anyway.
   */
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("")

  /*
   * Default the selection: the logged-in user's own doctor record when there is one, otherwise — and only when the
   * clinic has **exactly one** practitioner — that one. The single-dentist case is not a guess (there is nothing to
   * guess between), and the spec is explicit that such a cabinet must not be handed a pointless empty picker; the
   * control stays visible and pre-filled. With two or more practitioners and no linked doctor, this deliberately
   * leaves the field empty.
   *
   * Fills only while empty, so it never overrides a choice the user has made, and re-runs on `documentId` for the
   * same reason the ordre effect does: loading a document can land after the roster has already resolved.
   */
  useEffect(() => {
    if (selectedDoctorId) return
    if (currentUserDoctor?.id) {
      setSelectedDoctorId(currentUserDoctor.id)
      return
    }
    if (doctors.length === 1 && doctors[0].id) {
      setSelectedDoctorId(doctors[0].id)
    }
  }, [currentUserDoctor, doctors, selectedDoctorId, documentId])

  const chosenDoctor = doctors.find((d) => d.id === selectedDoctorId) ?? null

  /**
   * True for the two documents that are **overlays onto an official CNAM form** — the BS1 bulletin and the P 061
   * arrêt de travail (L11).
   *
   * <p>It exists because those two share every mechanism that the four free-form documents do not: the preview is
   * the server-rendered PDF in an iframe (there is no `<Card>` to clone, so Print must go through the iframe), the
   * practitioner is an explicit choice with **no `doctors[0]` fall-back**, mandatory fields are refused before
   * Save, and a Word export is meaningless because the paper is a pre-printed form.</p>
   *
   * <p>⚠️ It is deliberately <b>not</b> « has a validation gate » or « has a PDF preview » — those would each be a
   * different predicate that happens to select the same two types today. This one names the actual reason.</p>
   */
  const isOfficialForm = documentType === "bulletin-cnam" || documentType === "arret-travail"

  /** The frame's accessible name, so a screen reader names the form rather than announcing "iframe". */
  const officialFormPreviewTitle =
    documentType === "arret-travail" ? "Aperçu de l'arrêt de travail CNAM" : "Aperçu du bulletin de soins CNAM"

  /*
   * ⚠️ The `doctors[0]` fall-back is **gone**, for every document type — the narrow scoping K3 left in place no
   * longer holds. K3 kept it for the four free-form documents on the grounds that removing it would change what
   * they print as a side effect of a CNAM fix, and that the wrong name only costs a rejected claim on a bulletin.
   * Both premises depended on who could reach this editor: the caller was always a practitioner, so
   * `currentUserDoctor` answered first and the guess was nearly unreachable.
   *
   * `MedicalDocumentsController` is now `AnyClinicRole`, so the routine caller is reception — who has no linked
   * `Doctor` record — and the guess became the *normal* path. `doctors[0]` is the first name in the roster: on an
   * ordonnance that is a prescription attributed to a dentist who did not write it, and it is now the server's
   * resolved cachet too (`issuingDoctorId` below). A guess nobody sees is worse on a prescription than on a
   * bulletin, not better.
   *
   * Note what did *not* change: the defaulting effect above still pre-fills the caller's own record, and still
   * pre-fills the single-practitioner cabinet — there is nothing to guess between there. This only stops the
   * ≥2-practitioner case from silently picking one, which is exactly the case reception works in.
   */
  const selectedDoctor = chosenDoctor ?? currentUserDoctor ?? null


  const formData = {
    doctorName: selectedDoctor?.name || "Dr. [Nom]",
    /**
     * AC-P2.42 — mapped **here**, at the single point every printed surface derives from: the letterhead, the
     * certificat body sentence, the DOCX letterhead + signature block, and the on-screen preview's letterhead +
     * signature block all read `formData.doctorSpecialty`. One map at the source reaches all six without going
     * near this file's documentType switches (plan risk R-11).
     *
     * This value is also the `doctorSpecialty` snapshot persisted on the document and re-rendered by the
     * server-side PDF, which is what makes the *printed* certificat French with no backend change. That is
     * correct rather than a storage-key migration: `MedicalDocument.DoctorSpecialty` records the text that was
     * printed on that document (existing rows already hold French), unlike `Doctor.Specialty`, which stays the
     * English catalog key (AC-P2.43). `specialtyLabel` passes unknown values through, so re-saving an older
     * French snapshot is idempotent.
     */
    doctorSpecialty: specialtyLabel(selectedDoctor?.specialty) || "[Spécialité]",
    clinicName: clinicInfo?.name || "[Nom du cabinet]",
    clinicAddress: clinicInfo?.address || "[Adresse]",
    clinicCity: clinicInfo?.city || "",
    clinicPhone: clinicInfo?.phone || "[Téléphone]",
    clinicEmail: clinicInfo?.email || "",
  }

  // Helper functions
  const calculateAge = (dob: string | null | undefined) => {
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

  /**
   * Seed the liaison letter's « Traitement en cours et allergies connues » from the patient's own record.
   *
   * <p>Fill-if-empty, the same rule as the ordre number and the sexe above: a stored document's value wins, and the
   * box stays editable because what it holds is *printed* — a confrère reads it, so the practitioner must be able
   * to correct and extend it. Before this it was an empty textarea, so the letter told a maxillo-facial surgeon
   * nothing about a penicillin allergy unless someone retyped it out of another tab.</p>
   */
  useEffect(() => {
    if (documentType !== "liaison") return
    const patient = patients.find((p) => p.id === selectedPatient)
    if (!patient) return
    const seeded = [
      patient.allergies?.trim() ? `Allergies : ${patient.allergies.trim()}` : null,
      patient.medicalHistory?.trim() ? `Antécédents : ${patient.medicalHistory.trim()}` : null,
    ]
      .filter(Boolean)
      .join("\n")
    if (!seeded) return
    setFormFields((prev) => (prev.traitementEnCours ? prev : { ...prev, traitementEnCours: seeded }))
  }, [documentType, patients, selectedPatient, documentId])

  /*
   * Load patients from the API.
   *
   * ⚠️ The failure is recorded rather than emptied into « Aucun patient disponible » — the same class of defect the
   * `failed-read-as-empty` check now bans in its single-expression form. On this screen the consequence is that
   * every document type becomes unusable (the patient is required) while the picker states the clinic has no
   * patients, and the only report was a `console.error` nobody sees.
   */
  const loadPatients = useCallback(async () => {
    try {
      setLoadingPatients(true)
      const data = await patientsApi.list()
      setPatients(data)
      setFilteredPatients(data)
      setPatientsFailed(false)
    } catch {
      setPatientsFailed(true)
    } finally {
      setLoadingPatients(false)
    }
  }, [])

  useEffect(() => {
    void loadPatients()
  }, [loadPatients])

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
          setDocumentLoadFailed(false)
          const doc = await medicalDocumentsApi.get(urlDocumentId)
          setDocumentId(doc.id)
          // Band B — the token the save round-trips. Read HERE rather than taken from a list row: this editor is
          // reached from three places and the only copy that can be trusted is the one the GET just returned.
          setDocumentVersion(doc.version)
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
            duration: content.duration || "",
            // FR-2.5: the ordre is pre-filled from the doctor's profile (set by the effect below); a value
            // stored on a legacy document is still read back so an older certificat keeps rendering its ordre.
            doctorOrderNumber: content.doctorOrderNumber || "",
            renewals: content.renewals || "",
            patientSex: content.patientSex || "",
            patientWeightKg: content.patientWeightKg || "",
            startDate: content.startDate || "",
            objetMotif: content.objetMotif || "",
            // Liaison: recipient name/specialty come from the snapshot columns (works for legacy internal-
            // recipient letters too, LIA-5); address + guided fields from ContentJson (FR-4.1/FR-4.2).
            recipientName: doc.recipientDoctorName || "",
            recipientSpecialty: doc.recipientDoctorSpecialty || "",
            recipientAddress: content.recipientAddress || "",
            recipientEmail: content.recipientEmail || "",
            medecinTraitant: content.medecinTraitant || "",
            motif: content.motif || "",
            examenClinique: content.examenClinique || "",
            examenRadiologique: content.examenRadiologique || "",
            actesRealises: content.actesRealises || "",
            traitementEnCours: content.traitementEnCours || "",
            prescriptions: content.prescriptions || "",
            examensEnAttente: content.examensEnAttente || "",
            consignesSuivi: content.consignesSuivi || "",
            piecesJointes: content.piecesJointes || "",
          })

          // Expand the optional repos block when the loaded certificat already carries repos data.
          if (documentType === "certificat") {
            setReposOpen(Boolean(content.startDate || content.duration))
          }

          // Same rule for the liaison's optional norm sections: a section holding text must not open collapsed.
          if (documentType === "liaison") {
            setLiaisonExtrasOpen(Boolean(
              content.examenClinique || content.examenRadiologique || content.actesRealises ||
              content.traitementEnCours || content.prescriptions || content.examensEnAttente ||
              content.consignesSuivi || content.piecesJointes || content.medecinTraitant
            ))
          }

          // Arrêt de travail: restore the practitioner half. The identity half is re-derived from the patient's
          // fiche on every render, deliberately — a stored address that has since changed on the fiche would
          // otherwise reprint stale.
          if (documentType === "arret-travail") {
            setArretFields({
              days: content.days || "",
              // Stored as a French calendar day (that is what prints), so it is parsed back for the date input.
              fromDate: frenchDayToIso(content.fromDate) || todayLocalIso(),
              outingsFrom: content.outingsFrom || "",
              outingsTo: content.outingsTo || "",
              traumaCause: content.traumaCause || "",
              hospitalised: content.hospitalised || "",
              motif: content.motif || "",
            })
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
          setDocumentLoadFailed(true)
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
  }, [urlDocumentId, documentId, doctors, documentReload])

  // Load the selected patient's dental records — the source for pre-filling the CNAM bulletin acts table.
  // A failure is recorded, not swallowed: « Pré-remplir depuis les soins (0) » on a patient who has soins reads
  // as « ce patient n'a aucun soin enregistré », and the bulletin then gets typed from memory.
  useEffect(() => {
    if (documentType !== "bulletin-cnam" || !selectedPatient) {
      setDentalRecords([])
      setDentalRecordsFailed(false)
      return
    }
    let cancelled = false
    ;(async () => {
      try {
        const records = await dentalRecordsApi.list(selectedPatient)
        if (!cancelled) {
          setDentalRecords(records)
          setDentalRecordsFailed(false)
        }
      } catch {
        if (!cancelled) {
          setDentalRecords([])
          setDentalRecordsFailed(true)
        }
      }
    })()
    return () => { cancelled = true }
  }, [documentType, selectedPatient, dentalRecordsReload])

  // Load the DB-backed **DCH dental-act catalogue** once when editing a bulletin (searched client-side for the act
  // picker) — the DCH catalogue is the only act catalogue. The VLC values are not
  // fetched here: their only consumer was the client-side estimate calculator that AC-P6.15 replaced with the
  // backend endpoint, which resolves them itself.
  useEffect(() => {
    if (documentType !== "bulletin-cnam") return
    let cancelled = false
    ;(async () => {
      try {
        const acts = await dentalActsApi.list()
        if (!cancelled) {
          setDentalActCatalog(acts)
          setDentalActCatalogFailed(false)
        }
      } catch {
        if (!cancelled) {
          setDentalActCatalog([])
          setDentalActCatalogFailed(true)
        }
      }
    })()
    return () => { cancelled = true }
  }, [documentType, dentalActCatalogReload])

  // Load the medication catalog once when editing a prescription (searched client-side in the picker).
  useEffect(() => {
    if (documentType !== "prescription") return
    let cancelled = false
    ;(async () => {
      try {
        const meds = await medicationsApi.list()
        if (!cancelled) {
          setMedicationCatalog(meds)
          setMedicationCatalogFailed(false)
        }
      } catch {
        if (!cancelled) {
          setMedicationCatalog([])
          setMedicationCatalogFailed(true)
        }
      }
    })()
    return () => { cancelled = true }
  }, [documentType, medicationCatalogReload])

  const resetForm = () => {
    setSelectedPatient("")
    setDocumentId(null)
    setFormFields({
      date: todayLocalIso(),
      medications: [],
      content: "",
      duration: "",
      doctorOrderNumber: "",
      renewals: "",
      patientSex: "",
      patientWeightKg: "",
      startDate: "",
      objetMotif: "",
      recipientName: "",
      recipientSpecialty: "",
      recipientAddress: "",
      recipientEmail: "",
      medecinTraitant: "",
      motif: "",
      examenClinique: "",
      examenRadiologique: "",
      actesRealises: "",
      traitementEnCours: "",
      prescriptions: "",
      examensEnAttente: "",
      consignesSuivi: "",
      piecesJointes: "",
    })
    setBulletinFields({ careType: "APCI", apciCode: "", actsFrom: "", actsTo: "", acts: [] })
  }

  // Renouveler (P2-B): fork the loaded ordonnance into a new draft — same patient + same medications,
  // dated today — so renewing keeps the original in history instead of overwriting it. Clearing
  // documentId flips the save path to "create"; renewedRef stops the edit-load effect from reloading.
  const renewDocument = () => {
    renewedRef.current = true
    setDocumentId(null)
    setFormFields((prev) => ({ ...prev, date: todayLocalIso() }))
    toast.success("Ordonnance dupliquée", {
      description: "Modifiez si besoin, puis enregistrez pour créer une nouvelle ordonnance. L'originale est conservée.",
      duration: 4000,
    })
  }

  // Hoisted out of the « Ajouter un médicament » button so the empty state can offer the same action: an empty
  // list that only *describes* what to press is the pattern `ui/empty-state.tsx` exists to replace.
  const addMedicationLine = () =>
    setFormFields((prev) => ({
      ...prev,
      medications: [...prev.medications, { name: "", dosage: "", timesPerDay: "", duration: "" }],
    }))

  const addBulletinAct = () =>
    setBulletinFields((p) => ({
      ...p,
      acts: [...p.acts, { date: "", teeth: "", codeActe: "", cotation: "", honoraires: "" }],
    }))

  // ---- CNAM bulletin helpers ----
  /*
   * Pre-fill the acts table from the patient's dental records within the chosen date range. Code acte + Cotation
   * are left blank for the doctor to fill (or pick from the catalogue); honoraires = record cost.
   *
   * K6 — **both bounds compare clinic-local calendar days, as strings.** This used to build
   * `new Date(bulletinFields.actsTo)` and compare it against the record's full `interventionDate` instant. A bare
   * `YYYY-MM-DD` parses as **midnight UTC**, so any care recorded after 00:00 UTC on the end date failed the test:
   * the upper bound excluded its own day. With « Au » set to today — the ordinary way to file a bulletin — today's
   * séance was silently dropped and the bulletin went to the caisse one act short.
   *
   * Comparing the two `YYYY-MM-DD` strings is the fix and is exact: the bounds are already local calendar days
   * (that is what a `type="date"` input yields), `toLocalIso` renders the record's instant as the local day it
   * actually falls on, and lexicographic order on `YYYY-MM-DD` is chronological order. Inclusive on both ends,
   * which is what « Du … Au … » means to the person typing it. A séance at 23:30 local on the « Au » day is in;
   * one at 00:30 the next clinic day is out.
   */
  const prefillActsFromRecords = () => {
    const from = bulletinFields.actsFrom
    const to = bulletinFields.actsTo
    const inRange = dentalRecords.filter((r) => {
      if (!r.interventionDate) return true
      const day = toLocalIso(new Date(r.interventionDate))
      if (!day) return true // unparseable stored date: keep the séance and let the dentist judge it
      if (from && day < from) return false
      if (to && day > to) return false
      return true
    })
    const acts = inRange.map((r) => ({
      // Same rule for the value that lands on the form: `split("T")[0]` is the stored UTC day, which for an
      // evening séance is tomorrow's date printed on a CNAM document.
      date: r.interventionDate ? toLocalIso(new Date(r.interventionDate)) : "",
      teeth: (r.toothNumbers || []).join(", "),
      codeActe: "",
      cotation: "",
      /*
       * ⚠️ Deliberately NOT `formatDT`/`formatAmount`, and this is the one money-shaped `toFixed` in the file
       * that stays. This seeds an editable text input whose value is persisted verbatim into ContentJson and
       * stamped onto the BS1 overlay by the server — it is a wire value, not a rendered amount. French grouping
       * (« 1 234,500 ») would change what the CNAM form prints and what the server has to parse back.
       */
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

  /**
   * Pick a catalogue act: fills the real DCH Code acte + the Cotation (`"<lettreCle> <coefficient>"`). Both stay
   * editable.
   *
   * ⚠️ **`DentalActCode.Coefficient` is nullable where `CnamNomenclatureEntry.Coefficient` was not**, and in the
   * shipped DCH seed it is null for *every* act — the cotation lives in the NGAP arrêté, not in the acts list. So a
   * picked act normally fills the code and leaves the coefficient for the practitioner. Two things must NOT happen
   * here, and both are one keystroke away: writing `"D 0"` (a zero estimate reads as « non remboursable », which is
   * a different clinical statement) and writing `"D null"` (which `parseCotation` rejects, so the estimate silently
   * disappears with nothing saying why). Writing the lettre clé **alone** is deliberate: it is the half we know,
   * `parseCotation` correctly declines to estimate from it, and `missingCoefficient` below turns that into a
   * visible sentence naming the catalogue as the place to fix it.
   */
  const selectDentalAct = (index: number, entry: DentalActDto) => {
    setBulletinFields((prev) => ({
      ...prev,
      acts: prev.acts.map((act, i) =>
        i === index
          ? {
              ...act,
              codeActe: entry.codeActe,
              cotation:
                entry.coefficient != null ? `${entry.lettreCle} ${entry.coefficient}` : entry.lettreCle,
            }
          : act,
      ),
    }))
    setOpenActLookup(null)
  }

  /**
   * The catalogue row behind an act's code, or `undefined` for a hand-typed code — and for every act of a bulletin
   * saved before K1, whose stored mnemonic (`DETART`…) matches nothing in the DCH catalogue.
   *
   * Looked up at render time rather than copied onto the act row, for two reasons: `RequiresAccordPrealable` must
   * **not** be persisted into `ContentJson` (nothing on the BS1 carries it, and the flag is per-clinic and
   * correctable, so a snapshot would freeze a value the admin can fix), and a legacy row then degrades to « no
   * badge » instead of asserting something about a code this catalogue has never heard of.
   */
  const dentalActFor = (codeActe: string): DentalActDto | undefined => {
    const code = codeActe.trim().toUpperCase()
    if (!code) return undefined
    return dentalActCatalog.find((a) => a.codeActe.toUpperCase() === code)
  }

  // Indicative reimbursement (catalog-backed acts only). Editor-only — never persisted / never on the PDF
  // (AC-P6.16). The arithmetic is the BACKEND's: this component used to call a client-side calculator that
  // carried its own copy of the CNAM rates, which is the duplication AC-P6.15 removes. One request covers the
  // whole acts table, so the estimate is still per-act and still live.
  const bulletinPatientDob = patients.find((p) => p.id === selectedPatient)?.dateOfBirth ?? null

  // Estimates aligned by act index; `null` = not estimable (free text, unknown lettre clé, no coefficient).
  const [actEstimates, setActEstimates] = useState<Array<number | null>>([])
  /**
   * Why an estimate is absent, per act, as the server reported it. `MissingCoefficient` is also derivable here
   * (`missingCoefficient` below), but `NoLetterValue` is not: the cotation parses, the request succeeds, and the
   * estimate comes back null because the convention fixes no valeur for that lettre clé — which used to render as
   * nothing at all, indistinguishable from « non remboursable ».
   */
  const [actEstimateReasons, setActEstimateReasons] = useState<Array<'MissingCoefficient' | 'NoLetterValue' | null>>([])
  // A failed call must SAY so (AC-P6.17). Showing an empty column instead is indistinguishable from
  // « aucun acte remboursable » — the reader would conclude the CNAM pays nothing.
  const [estimateFailed, setEstimateFailed] = useState(false)

  // Only the cotation and the care date move the estimate, so the effect keys on those alone — not on the whole
  // acts array, which changes on every honoraires or teeth keystroke and would re-request for nothing.
  const estimateInputsKey = JSON.stringify(
    bulletinFields.acts.map((act) => [act.cotation, act.date]),
  )

  useEffect(() => {
    if (documentType !== "bulletin-cnam") return

    const parsed: Array<{ lettreCle: string; coefficient: number } | null> =
      bulletinFields.acts.map((act) => parseCotation(act.cotation))
    const requestIndexes = parsed.flatMap((p, i) => (p ? [i] : []))

    if (requestIndexes.length === 0) {
      setActEstimates(bulletinFields.acts.map(() => null))
      setEstimateFailed(false)
      return
    }

    // Drop stale estimates for rows that no longer exist BEFORE the debounce. The estimates are held by index,
    // so removing act 2 would otherwise leave act 3's figure rendered against act 2's row for up to 350 ms —
    // a wrong money-adjacent number on the wrong line, which is worse than showing none.
    setActEstimates((prev) =>
      prev.length === bulletinFields.acts.length
        ? prev
        : bulletinFields.acts.map((_, i) => (i < prev.length ? prev[i] : null)),
    )

    let cancelled = false
    // Debounced: the cotation is typed character by character, and « D 1 » is a valid cotation on the way to
    // « D 15 ».
    const timer = setTimeout(() => {
      void (async () => {
        try {
          const results = await estimateReimbursements(
            requestIndexes.map((i) => ({
              lettreCle: parsed[i]!.lettreCle,
              coefficient: parsed[i]!.coefficient,
              careDate: bulletinFields.acts[i].date || null,
            })),
            bulletinPatientDob,
            formFields.date || null,
          )
          if (cancelled) return
          const byIndex = bulletinFields.acts.map(() => null as number | null)
          const reasons = bulletinFields.acts.map(() => null as 'MissingCoefficient' | 'NoLetterValue' | null)
          requestIndexes.forEach((actIndex, resultIndex) => {
            byIndex[actIndex] = results[resultIndex]?.estimate ?? null
            reasons[actIndex] = results[resultIndex]?.unavailableReason ?? null
          })
          setActEstimates(byIndex)
          setActEstimateReasons(reasons)
          setEstimateFailed(false)
        } catch {
          if (cancelled) return
          setActEstimates([])
          setActEstimateReasons([])
          setEstimateFailed(true)
        }
      })()
    }, 350)

    return () => {
      cancelled = true
      clearTimeout(timer)
    }
    // `estimateInputsKey` stands in for the acts' cotations and dates (see above); the array itself is a new
    // object on every keystroke.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [documentType, estimateInputsKey, bulletinPatientDob, formFields.date])

  const bulletinEstimateTotal = actEstimates.reduce<number>((sum, e) => (e != null ? sum + e : sum), 0)
  const hasAnyBulletinEstimate = actEstimates.some((e) => e != null)

  /*
   * ── K2 (editor half): what is missing, named, before Save is reachable ──────────────────────────────────────
   *
   * The backend refuses an incomplete bulletin (`BulletinCnamValidation`) and that is the real gate. This is the
   * same five checks computed here so the practitioner sees them *while filling the form* rather than as a toast
   * after pressing Save — and, more importantly, so each one says **where** it is fixed. Four of the five are not
   * fields of this editor at all: they live on the patient's fiche and on the practitioner's profile, so « le
   * régime est absent » with no destination would be a dead end.
   *
   * ⚠️ Deliberately mirrors the server's messages rather than paraphrasing them: the two are read minutes apart by
   * the same person, and a client that says something subtly different reads as a second, disagreeing opinion.
   * The régime/lien membership tests come from `lib/cnam.ts`, which mirrors `CnamInfo`'s constants — never retyped
   * literals, because the accents are load-bearing (« Convention bilatérale ») and a mismatch fails silently.
   *
   * ⚠️ Computed on the SELECTED PATIENT, and empty while no patient is chosen: the editor opens blank, and listing
   * five refusals at someone who has not begun is noise, not guidance. Save is already disabled without a patient.
   */
  const bulletinCnam = patients.find((p) => p.id === selectedPatient)?.cnamInfo
  const bulletinIdentifiant = (bulletinCnam?.identifiantUnique || "").trim()
  const bulletinRegime = (bulletinCnam?.regime || "").trim()
  const bulletinLien = (bulletinCnam?.maladeLien || "").trim()
  const bulletinRang = (bulletinCnam?.maladeLienRang || "").trim()
  const bulletinDoctorCode = (selectedDoctor?.codeProfessionnelSante || "").trim()

  /** One entry per unusable field. `onPatient` = fixed on the patient's fiche, not here. */
  const bulletinProblems: Array<{ key: string; message: string; onPatient: boolean }> = []
  if (documentType === "bulletin-cnam" && selectedPatient) {
    if (!bulletinIdentifiant) {
      bulletinProblems.push({
        key: "identifiant",
        message: "L'identifiant unique CNAM du patient est absent de sa fiche.",
        onPatient: true,
      })
    } else if (!isValidCnamIdentifiant(bulletinIdentifiant)) {
      // K7: the renderer combs one digit per printed cell and used to drop the tail without a trace.
      bulletinProblems.push({
        key: "identifiant",
        message:
          `L'identifiant unique CNAM ne tient pas dans le formulaire ` +
          `(${cnamIdentifiantDigitCount(bulletinIdentifiant)} chiffres pour ${CNAM_IDENTIFIANT_DIGITS} cases).`,
        onPatient: true,
      })
    }

    if (!bulletinRegime) {
      bulletinProblems.push({ key: "regime", message: "Le régime est absent.", onPatient: true })
    } else if (!isKnownCnamRegime(bulletinRegime)) {
      bulletinProblems.push({
        key: "regime",
        message: `Le régime ${quoteFr(bulletinRegime)} n'est pas reconnu.`,
        onPatient: true,
      })
    }

    if (!bulletinLien) {
      bulletinProblems.push({ key: "lien", message: "Le lien de parenté est absent.", onPatient: true })
    } else if (!isKnownCnamLien(bulletinLien)) {
      bulletinProblems.push({
        key: "lien",
        message: `Le lien de parenté ${quoteFr(bulletinLien)} n'est pas reconnu.`,
        onPatient: true,
      })
    } else if (cnamLienRequiresRang(bulletinLien) && !bulletinRang) {
      bulletinProblems.push({
        key: "rang",
        message: `Le rang est obligatoire pour le lien ${quoteFr(bulletinLien)}.`,
        onPatient: true,
      })
    }

    if (bulletinFields.acts.length === 0) {
      bulletinProblems.push({ key: "acts", message: "Le bulletin ne porte aucun acte.", onPatient: false })
    }

    if (!bulletinDoctorCode) {
      bulletinProblems.push({
        key: "code",
        message: selectedDoctor
          ? `Aucun code conventionnel sur le profil de ${selectedDoctor.name}.`
          : "Aucun praticien traitant sélectionné — son code conventionnel s'imprime sur chaque ligne d'acte.",
        onPatient: false,
      })
    }
  }

  const bulletinBlocked = bulletinProblems.length > 0

  /*
   * The arrêt de travail's own gate (L11), mirroring `ArretTravailValidation` message for message — same reason as
   * the bulletin's: the two are read minutes apart by the same person, and a client that paraphrases the server
   * reads as a second, disagreeing opinion.
   *
   * ⚠️ **The motif is absent from this list on purpose.** P 061's practitioner half carries no diagnosis field —
   * the form's own « partie confidentielle au verso » is where a medical reason goes, and the front is what the
   * patient hands their employer. Requiring it would demand a value with nowhere to print.
   */
  const arretProblems: Array<{ key: string; message: string; onPatient: boolean }> = []
  if (documentType === "arret-travail" && selectedPatient) {
    const days = arretFields.days.trim()
    const parsedDays = Number.parseInt(days, 10)
    if (!days) {
      arretProblems.push({ key: "days", message: "La durée de l'arrêt (en jours) est absente.", onPatient: false })
    } else if (!Number.isFinite(parsedDays) || parsedDays <= 0) {
      arretProblems.push({
        key: "days",
        message: `La durée ${quoteFr(days)} n'est pas un nombre de jours valide.`,
        onPatient: false,
      })
    } else if (parsedDays > ARRET_MAX_DAYS) {
      arretProblems.push({
        key: "days",
        message: `La durée de ${parsedDays} jours dépasse le maximum accepté (${ARRET_MAX_DAYS} jours).`,
        onPatient: false,
      })
    }

    if (!arretFields.fromDate) {
      arretProblems.push({ key: "fromDate", message: "La date de début de l'arrêt est absente.", onPatient: false })
    }

    if (!selectedDoctor) {
      arretProblems.push({
        key: "doctor",
        message: "Aucun praticien traitant sélectionné — son nom et son code s'impriment sur le certificat.",
        onPatient: false,
      })
    } else if (!(selectedDoctor.codeProfessionnelSante || "").trim() && !(selectedDoctor.ordreNumberCnomdt || "").trim()) {
      // One of the two, never both: a conventionné dentist has a code conventionnel, one who is not still has a
      // CNOMDT ordre number, and requiring both would refuse a legitimate practitioner.
      arretProblems.push({
        key: "code",
        message: `Ni code conventionnel ni n° au Conseil de l'Ordre sur le profil de ${selectedDoctor.name}.`,
        onPatient: false,
      })
    }

    // The box and its two hours are one statement; half of it is worse than none, because the caisse reads the
    // empty hour slot beside a ticked box as the answer.
    if (Boolean(arretFields.outingsFrom.trim()) !== Boolean(arretFields.outingsTo.trim())) {
      arretProblems.push({
        key: "outings",
        message: "Les sorties autorisées demandent une heure de début et une heure de fin.",
        onPatient: false,
      })
    }
  }

  const arretBlocked = arretProblems.length > 0

  /*
   * The two gates, unioned once. Only one of the lists is ever non-empty (each is guarded on its own
   * `documentType`), so this is a merge rather than a combination — but it is what lets the banner, the Save
   * button's `disabled` and the Save-time refusal read a single value. Three separate `bulletinBlocked ||
   * arretBlocked` expressions is how the third one gets forgotten and a blocked document saves.
   */
  const officialFormProblems = documentType === "arret-travail" ? arretProblems : bulletinProblems
  const officialFormBlocked = bulletinBlocked || arretBlocked

  /**
   * The arrêt's `ContentJson` — and the PDF data, which is the same object. Keys come from `lib/arret-travail.ts`'s
   * backend mirror rather than typed here: the editor writes them, the server's validation reads them and the
   * renderer stamps from them, so a literal spelled differently in one of the three degrades **silently**.
   *
   * The patient identity half is prefilled from the fiche because the form's left panel asks the *patient* for
   * exactly the values the product already holds — and an identifiant unique copied out by hand is where a digit
   * gets lost.
   */
  /**
   * `dd/MM/yyyy` → `yyyy-MM-dd`, for hydrating a `<input type="date">` from a stored value.
   *
   * <p>The arrêt persists its start date in the form it **prints** — a French calendar day — because that string is
   * what the renderer stamps and re-deriving it at render time from an ISO value would be a second date authority.
   * Reopening the document therefore has to parse it back, and it is done by splitting on `/` rather than by
   * `new Date(...)`: the latter reads `04/08/2026` as 4 August in some locales and 8 April in others, which would
   * silently move the start of somebody's arrêt on every reopen.</p>
   */
  const frenchDayToIso = (value: unknown): string => {
    if (typeof value !== "string") return ""
    const parts = value.trim().split("/")
    if (parts.length !== 3) return ""
    const [day, month, year] = parts
    if (year.length !== 4) return ""
    return `${year}-${month.padStart(2, "0")}-${day.padStart(2, "0")}`
  }

  const buildArretContent = (patient: PatientDto): Record<string, string> => {
    const address = patient.address
      ? [patient.address.street, patient.address.city, patient.address.state]
          .filter((part) => part && part.trim())
          .join(", ")
      : ""
    return {
      identifiantUnique: patient.cnamInfo?.identifiantUnique || "",
      patientFirstName: patient.firstName || "",
      patientLastName: patient.lastName || "",
      patientDateOfBirth: patient.dateOfBirth ? formatDateFr(patient.dateOfBirth) : "",
      patientAddress: address,
      // The comb takes four digits; the value is stored free-text, so the server strips non-digits too.
      postalCode: patient.address?.zipCode || "",
      patientPhone: patient.phoneNumber || "",
      doctorName: selectedDoctor?.name || "",
      doctorQuality: specialtyLabel(selectedDoctor?.specialty) || "",
      city: clinicInfo?.city || "",
      doctorCodeConventionnel: (selectedDoctor?.codeProfessionnelSante || "").trim(),
      doctorOrdreNumber: (selectedDoctor?.ordreNumberCnomdt || "").trim(),
      days: arretFields.days.trim(),
      // ⚠️ Printed as a French calendar day. `formatDateFr` and never `toISOString()`, which would shift an arrêt
      // starting on the 1st into the previous month.
      fromDate: arretFields.fromDate ? formatDateFr(arretFields.fromDate) : "",
      outingsFrom: arretFields.outingsFrom.trim(),
      outingsTo: arretFields.outingsTo.trim(),
      traumaCause: arretFields.traumaCause,
      hospitalised: arretFields.hospitalised,
      // Kept, deliberately never printed — see arretProblems.
      motif: arretFields.motif.trim(),
      // The « ..........le,.......... » line above the practitioner's stamp.
      signPlace: clinicInfo?.city || "",
      signDate: formatDateFr(formFields.date),
    }
  }

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
    const patientName = patientData ? getPatientName(patientData) : "[Nom du patient]"
    const dob = patientData?.dateOfBirth ? formatFrDate(patientData.dateOfBirth) : "[JJ/MM/AAAA]"

    // Mirrors the server's CertificatTextBuilder: the attestation formula names the registering body, and no
    // longer restates the ordre NUMBER or the cabinet address — both render once in the shared identity block
    // (the letterhead above), which every document type now carries.
    const paras = [
      `Je soussigné(e), Docteur ${formData.doctorName}, ${specialty}, inscrit(e) à l'${CERTIFICAT_ORDRE_LABEL}, certifie avoir examiné ce jour ${patientName}, né(e) le ${dob}.`,
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

  // Lettre de liaison — the single source of truth for the body sections, shared by the read-only preview and
  // the Word export. ⚠️ This order and these headings must stay identical to the server's `LiaisonContent`,
  // which renders the PDF; the two are the same letter seen twice.
  // The free-text body (`content`) is a first-class unlabelled section, NOT a legacy fallback — prose and the
  // norm sections coexist, and every section is optional.
  const liaisonSections = (): { heading: string | null; body: string }[] => {
    const ordered: { heading: string | null; value: string }[] = [
      { heading: "Motif de la liaison", value: formFields.motif },
      { heading: null, value: formFields.content },
      { heading: "Examen clinique", value: formFields.examenClinique },
      { heading: "Examen radiologique", value: formFields.examenRadiologique },
      { heading: "Actes réalisés", value: formFields.actesRealises },
      { heading: "Traitement en cours et allergies connues", value: formFields.traitementEnCours },
      { heading: "Prescriptions", value: formFields.prescriptions },
      { heading: "Résultats d'examens en attente", value: formFields.examensEnAttente },
      { heading: "Consignes de suivi / avis attendu", value: formFields.consignesSuivi },
      { heading: "Pièces jointes", value: formFields.piecesJointes },
    ]
    return ordered
      .filter((s) => s.value && s.value.trim())
      .map((s) => ({ heading: s.heading, body: s.value.trim() }))
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
      content.renewals = formFields.renewals || "";
    } else if (documentType === "liaison") {
      // The recipient's address/email + the norm sections ride in ContentJson (name/specialty go through the
      // recipient snapshot columns). `content` is the letter's primary free-text body.
      content.content = formFields.content || "";
      content.recipientAddress = formFields.recipientAddress || "";
      content.recipientEmail = formFields.recipientEmail || "";
      content.medecinTraitant = formFields.medecinTraitant || "";
      content.motif = formFields.motif || "";
      content.examenClinique = formFields.examenClinique || "";
      content.examenRadiologique = formFields.examenRadiologique || "";
      content.actesRealises = formFields.actesRealises || "";
      content.traitementEnCours = formFields.traitementEnCours || "";
      content.prescriptions = formFields.prescriptions || "";
      content.examensEnAttente = formFields.examensEnAttente || "";
      content.consignesSuivi = formFields.consignesSuivi || "";
      content.piecesJointes = formFields.piecesJointes || "";
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
    } else if (documentType === "arret-travail") {
      Object.assign(content, buildArretContent(patientData));
    }

    // Written for EVERY type, not per type: the identity block is shared, so a value stored only on the
    // ordonnance would vanish from any other document that carries the same block. Snapshotted so the
    // background PDF job renders them with no live patient lookup (AC-7).
    content.patientSex = formFields.patientSex || "";
    content.patientWeightKg = formFields.patientWeightKg || "";

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
      patientSex: formFields.patientSex || undefined,
      patientWeightKg: formFields.patientWeightKg || undefined,
      clinicName: formData.clinicName,
      clinicAddress: formData.clinicAddress,
      clinicPhone: formData.clinicPhone,
      // clinicEmail is deliberately NOT sent: the server strips any client-supplied value and overlays its own
      // (a caller must not be able to put another cabinet's address on a document it issues).
      doctorName: formData.doctorName,
      doctorSpecialty: formData.doctorSpecialty,
      /*
       * The chosen practitioner's **id**, alongside the name that is printed. This is what the server resolves the
       * cachet + n° d'ordre from, so the rendered document carries the identity of the practitioner named on it
       * rather than of whoever is logged in — the case that matters now that reception can author documents.
       *
       * ⚠️ Sending an id here is not the same as sending a cachet: `doctorCachetKey`, its content type, the ordre
       * and the cabinet city are all stripped server-side and re-resolved (like `clinicEmail` above). This is a
       * *selector*, checked against the caller's own clinic roster.
       */
      issuingDoctorId: selectedDoctor?.id || undefined,
      recipientDoctorName: documentType === "liaison" ? recipientDoctorName : undefined,
      recipientDoctorSpecialty: documentType === "liaison" ? recipientDoctorSpecialty : undefined,
      content,
    };
  };

  /*
   * K5 — a bulletin de soins has **no Word export**, and the button is not offered for one.
   *
   * `generateWordInternal`'s branch chain is `prescription` / `liaison` / `certificat` with no `bulletin-cnam`
   * branch and no `else`, so pressing it on a bulletin produced a .docx containing only the letterhead and a
   * signature line — and the success toast still fired. The fix is not to write the missing branch: a BS1 is a
   * stamped overlay on an official pre-printed form, so a Word rendering of it has no legitimate use and could be
   * mistaken for something submittable. « Télécharger PDF » is the export for this document type.
   */
  /**
   * Neither official form has a Word export, and that is not an omission: the deliverable is an overlay onto a
   * pre-printed CNAM form, so a `.docx` could only ever be the letterhead with none of the form on it. The K-series
   * defect was exactly that — the branch chain had no `bulletin-cnam` case and no `else`, so pressing the button
   * produced a letterhead-only file **and** a success toast.
   */
  const wordExportSupported = !isOfficialForm

  const generateWord = async () => {
    if (!wordExportSupported) {
      return;
    }

    if (!patientData) {
      toast.error("Patient requis", {
        description: "Sélectionnez un patient avant de générer le document Word.",
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
            const medText = formatMedicationLine(med);
            paragraphs.push(new Paragraph({ text: medText }));
          });
        } else {
          paragraphs.push(new Paragraph({ text: "Aucune prescription" }));
        }
        const renewalMention = formatRenewalMention(formFields.renewals);
        if (renewalMention) {
          paragraphs.push(new Paragraph({ text: renewalMention }));
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
      // AC-4. `file-saver`'s `saveAs` was a THIRD delivery mechanism in this app, and it is an `<a download>`
      // underneath — so like the other two it delivered nothing on iOS Safari.
      await downloadBlob(blob, fileName);
      toast.success("Document Word téléchargé", {
        description: `Le fichier ${quoteFr(fileName)} est en cours de téléchargement.`,
        duration: 3000,
      });
    } catch (error) {
      console.error('Error generating Word document:', error);
      toast.error("La génération du document Word a échoué", {
        description: "Le document n'a pas pu être créé. Veuillez réessayer.",
        duration: 4000,
      });
    }
  };

  /** The one filename this document's PDF gets — « Télécharger », and the shell delivery AC-8 added below. */
  const buildPdfFileName = () => {
    const typeSlug = getDocumentTitle().toLowerCase().replace(/\s+/g, '-');
    const patientSlug = patientData ? `-${`${patientData.firstName}-${patientData.lastName}`.toLowerCase()}` : '';
    return `${typeSlug}${patientSlug}.pdf`;
  };

  const handleDownloadPdf = async () => {
    if (saving) {
      return;
    }

    if (!patientData) {
      toast.error("Patient requis", {
        description: "Sélectionnez un patient avant de générer le PDF.",
        duration: 3000,
      });
      return;
    }

    const loadingToast = toast.loading("Génération du PDF…", {
      description: "Un instant, le document se prépare.",
    });
    
    try {
      const documentData = buildDocumentData();
      if (!documentData) {
        toast.dismiss(loadingToast);
        toast.error("Données manquantes", {
          description: "Impossible de générer le PDF. Vérifiez que tous les champs obligatoires sont remplis.",
          duration: 4000,
        });
        return;
      }

      // Generate PDF on server using structured data
      const pdfBlob = await medicalDocumentsApi.generatePdfForDownload(documentData);
      
      const fileName = buildPdfFileName();

      await downloadBlob(pdfBlob, fileName);

      toast.dismiss(loadingToast);
      toast.success("PDF téléchargé", {
        description: `Le fichier ${quoteFr(fileName)} est en cours de téléchargement.`,
        duration: 3000,
      });
    } catch (error) {
      console.error('Error in handleDownloadPdf:', error);
      toast.dismiss(loadingToast);
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur est survenue";
      toast.error("Erreur lors du téléchargement du PDF", {
        description: errorMessage,
        duration: 4000,
      });
    }
  };

  const handleSavePdfToFiles = async () => {
    if (!documentId || !patientData) {
      toast.error("Document pas encore enregistré", {
        description: "Enregistrez le document avant de générer le PDF.",
        duration: 3000,
      });
      return;
    }

    if (saving) {
      return;
    }

    const loadingToast = toast.loading("Génération et enregistrement du PDF…", {
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
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur est survenue";
      toast.error("L'enregistrement du PDF a échoué", {
        description: errorMessage,
        duration: 4000,
      });
    }
  };

  /**
   * Hand the generated form to the OS — the only working preview *and* print route in a native shell (AC-8).
   *
   * `downloadBlob` tries the shell's `saveFile` first (which writes the file and offers to open it, landing in
   * Android's `PdfRenderer` / iOS's `QLPreviewController`), then the share sheet on a coarse browser. The OS
   * viewer owns the printing from there; there is no `window.print()` to reach in an Android WebView.
   */
  const deliverOfficialFormPdf = async (reason: "preview" | "print") => {
    const blob = bs1BlobRef.current;
    if (!blob) {
      toast.error("Document indisponible", {
        description: "Le document n'a pas encore été généré. Complétez le formulaire, puis réessayez.",
        duration: 4000,
      });
      return;
    }
    if (reason === "print") {
      // Announced BEFORE delivery, so a `downloadBlob` failure toast lands after it and is the last word.
      toast.info("Ouverture du document", {
        description: "Utilisez l'impression de la visionneuse de votre appareil pour l'imprimer sur le formulaire pré-imprimé.",
        duration: 5000,
      });
    }
    await downloadBlob(blob, buildPdfFileName());
  };

  /*
   * K4 — printing a bulletin is a **different operation**, and conflating the two is what broke it.
   *
   * « Imprimer » always failed on a BS1: `documentRef` is attached to the `<Card>` in the *else* branch of the
   * `bulletin-cnam ? … : (…)` preview ternary, so for a bulletin `documentRef.current` was null and the guard below
   * refused with « Le contenu du document n'est pas disponible pour l'impression » — on the one document a
   * conventionné dentist prints all day.
   *
   * The fix is to gate on the document type rather than to move the ref. For every other type the printable thing
   * is a styled DOM subtree, cloned into a blank window. For a bulletin it is the **overlaid PDF itself**, already
   * rendered by the server and already on screen in an iframe: there is no HTML to clone, and re-deriving the paper
   * from the DOM would print something other than what the caisse receives.
   *
   * ⚠️ `contentWindow.print()` on the live preview iframe is the primary path (same-origin `blob:`, so it is
   * reachable) and is what keeps the printed sheet byte-identical to the preview. Chromium and Firefox honour it;
   * where the embedded PDF viewer refuses, the `catch` falls back to opening the blob in its own tab so the user
   * still has a print dialog one keystroke away, with a French toast saying so. Never silently nothing.
   */
  const printBulletinPdf = () => {
    if (bs1PreviewLoading) {
      toast.info("Aperçu en cours de génération", {
        description: "Attendez la fin de la génération de l'aperçu avant d'imprimer.",
        duration: 3000,
      });
      return;
    }

    if (!bs1PreviewUrl) {
      toast.error("Impossible d'imprimer", {
        description:
          "L'aperçu du bulletin n'a pas encore été généré. Sélectionnez un patient et complétez le bulletin, puis réessayez.",
        duration: 4000,
      });
      return;
    }

    /*
     * ⚠️ Where the frame is not actually rendered, printing *through* it prints nothing (AC-8).
     * `offsetParent === null` reads the `coarse:hidden` tree below rather than re-deriving its media query —
     * one hinge, so the two cannot disagree about whether a frame is on screen. That covers the native shell,
     * where an embedded `blob:` PDF has no viewer and an Android WebView has no `window.print()` at all: a
     * blank frame beside an inert « Imprimer » is exactly what this criterion forbids.
     */
    const hiddenFrame = bs1IframeRef.current;
    if (!hiddenFrame || hiddenFrame.offsetParent === null) {
      void deliverOfficialFormPdf("print");
      return;
    }

    try {
      const frame = bs1IframeRef.current;
      if (frame?.contentWindow) {
        frame.contentWindow.focus();
        frame.contentWindow.print();
        return;
      }
      throw new Error("preview iframe unavailable");
    } catch {
      const printWindow = window.open(bs1PreviewUrl, "_blank");
      if (!printWindow) {
        toast.error("Fenêtre bloquée", {
          description: "Autorisez les fenêtres pop-up de votre navigateur pour lancer l'impression.",
          duration: 4000,
        });
        return;
      }
      toast.info("Bulletin ouvert dans un nouvel onglet", {
        description: "Utilisez l'impression de votre lecteur PDF pour l'imprimer sur le formulaire BS1.",
        duration: 4000,
      });
    }
  };

  const handlePrint = () => {
    if (saving) {
      return; // Prevent action while saving
    }

    // Both official forms preview as the SERVER-rendered PDF in an iframe, not as a `<Card>` — there is no HTML
    // to clone, so Print must go through the iframe (K4). Reaching the DOM path here is the defect that made
    // « Imprimer » fail silently on the one document a conventionné dentist prints all day.
    if (isOfficialForm) {
      printBulletinPdf();
      return;
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
      toast.error("Fenêtre bloquée", {
        description: "Autorisez les fenêtres pop-up de votre navigateur pour lancer l'impression.",
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

  const handleSave = async () => {
    // The stored content is still in flight (or its read failed): saving now would take the "create" path and
    // put a second, empty document beside the one the user opened.
    if (loadingDocument || documentLoadFailed) {
      toast.error("Document non chargé", {
        description: "Le document n'a pas encore été chargé. Rechargez la page avant d'enregistrer.",
        duration: 4000,
      })
      return
    }

    if (!selectedPatient || !patientData) {
      toast.error("Patient requis", {
        description: "Sélectionnez un patient avant d'enregistrer le document.",
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

    // K2 — the same refusal the disabled button already explains above the form. Kept as a guard rather than
    // relying on `disabled` alone: this function is also reachable by keyboard submit, and the authoritative gate
    // is the server's (`BulletinCnamValidation`) — this only spares the round trip and keeps the wording identical.
    if (officialFormBlocked) {
      toast.error("Bulletin de soins incomplet", {
        description: officialFormProblems.map((p) => p.message).join(" "),
        duration: 6000,
      })
      return
    }

    setSaving(true)
    try {
      // Build content JSON from form fields
      const content: Record<string, any> = {
        date: formFields.date,
      }

      if (documentType === "prescription") {
        content.medications = formFields.medications // Array will be serialized as JSON
        content.renewals = formFields.renewals
    } else if (documentType === "liaison") {
      // Same ContentJson shape the renderer reads (buildDocumentData) — the free-text body, the recipient's
      // address/email and the norm sections. Recipient name/specialty go through the update payload.
      content.content = formFields.content
      content.recipientAddress = formFields.recipientAddress
      content.recipientEmail = formFields.recipientEmail
      content.medecinTraitant = formFields.medecinTraitant
      content.motif = formFields.motif
      content.examenClinique = formFields.examenClinique
      content.examenRadiologique = formFields.examenRadiologique
      content.actesRealises = formFields.actesRealises
      content.traitementEnCours = formFields.traitementEnCours
      content.prescriptions = formFields.prescriptions
      content.examensEnAttente = formFields.examensEnAttente
      content.consignesSuivi = formFields.consignesSuivi
      content.piecesJointes = formFields.piecesJointes
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
      } else if (documentType === "arret-travail") {
        Object.assign(content, buildArretContent(patientData))
      }

      // Same shape buildDocumentData writes, for every type — the identity block is shared. Persisted so the
      // background-job PDF renders these with no live patient lookup (AC-7); the download path would otherwise
      // show a sexe the re-rendered document silently lost.
      content.patientSex = formFields.patientSex
      content.patientWeightKg = formFields.patientWeightKg

      const contentJson = JSON.stringify(content)

      // Save document first
      let savedDocumentId = documentId;
      
      if (documentId) {
        // Update existing document. Its response carries the row's new token, which the next save needs — a
        // second « Mettre à jour » without it would 409 on a change this same user made.
        const saved = await medicalDocumentsApi.update(documentId, {
          documentDate: formFields.date,
          recipientDoctorName: recipientDoctorName || undefined,
          recipientDoctorSpecialty: recipientDoctorSpecialty || undefined,
          contentJson,
          // Re-assert the practitioner on every save: the editor rebuilds contentJson from its own fields, so the
          // reserved cachet/ordre keys are not in the payload and the server re-resolves them. Omitted, it would
          // fall back to the stored snapshot — right for the background PDF job, wrong here, where the user may
          // have just changed who the document is issued by.
          issuingDoctorId: selectedDoctor?.id || undefined,
          version: documentVersion,
        })
        setDocumentVersion(saved.version)
        toast.success("Document mis à jour", {
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
          issuingDoctorId: selectedDoctor?.id || undefined,
          appointmentId: urlAppointmentId || undefined,
        })
        savedDocumentId = result.id;
        setDocumentId(result.id)
        setDocumentVersion(result.version)
        toast.success("Document enregistré", {
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
          // AC-P3.33 — "optional" was the wrong word: the toast above has just promised the user that the
          // PDF will land in the patient's files. If the enqueue failed it never will, and staying silent
          // means they go looking for a document that is not coming. The document itself IS saved, so this
          // is a warning about the attachment, not a failure of the save.
          toast.warning("Le PDF n'a pas pu être mis en file de génération", {
            description: `${getErrorMessage(error)} Le document est enregistré ; relancez la génération du PDF depuis le document.`,
            duration: 6000,
          });
        }
      }

    } catch (error) {
      console.error("Failed to save document:", error)
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur est survenue"
      toast.error("L'enregistrement a échoué", {
        description: errorMessage,
        duration: 4000,
      })
    } finally {
      setSaving(false)
    }
  }

  /*
   * ⚠️ No « honoraires » case, deliberately (defect #6). `/documents/honoraires` is guarded by the route
   * (`app/documents/[type]/page.tsx` renders a notice pointing at /factures), so this editor is never mounted
   * for that type — and a note d'honoraires is money, owned by the Factures module, which numbers it, applies
   * TVA + timbre. The branches that used to live here rendered « 120,00 € » on a
   * Tunisian document, including in the Word export; they were one route change away from printing euros.
   */
  const getDocumentTitle = () => {
    switch (documentType) {
      case "prescription":
        return "Ordonnance"
      case "liaison":
        return "Lettre de liaison"
      case "certificat":
        return "Certificat médical"
      case "bulletin-cnam":
        return "Bulletin de soins CNAM"
      case "arret-travail":
        return "Arrêt de travail"
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
  // The blob behind that URL. Kept because where the frame cannot render it, the file itself is the answer
  // (AC-8) and `downloadBlob` takes bytes, not a `blob:` URL — re-fetching one we already hold would be silly.
  const bs1BlobRef = useRef<Blob | null>(null)
  // The preview iframe itself, so « Imprimer » can print the overlaid PDF the dentist is looking at rather than
  // re-deriving the paper from the DOM (K4 — see printBulletinPdf).
  const bs1IframeRef = useRef<HTMLIFrameElement>(null)

  // Serialized snapshot of the inputs that feed the BS1 PDF; the effect re-runs only when it changes.
  // null when no patient is selected (buildDocumentData() returns null) — short-circuits without an API call (AC-5).
  const bs1DocumentData = isOfficialForm ? buildDocumentData() : null
  const bs1DataKey = bs1DocumentData ? JSON.stringify(bs1DocumentData) : null

  useEffect(() => {
    if (!isOfficialForm) return
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
        bs1BlobRef.current = blob
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
    // `flex-1 min-h-0`, not `h-screen`: this renders inside `AppShell`'s `<main>`, which is already a bounded
    // flex column of the viewport minus the header. Demanding a second full viewport here made the editor
    // taller than its own container by exactly the header's height.
    <div className="flex min-h-0 flex-1 bg-background">
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* AC-P3.17 — the two columns stack below `xl:`. A fixed 420px form beside a preview does not fit a
            375px phone: the form was clipped and the preview unreachable. Stacked, the form is full-width and
            the rendered document follows it, below.
            ⚠️ The hinge is `xl:` (1280px), NOT `md:`, and the arithmetic is why: the sidebar rail also appears
            at `md:`, so an iPad portrait (820px) had 564px for the editor — 420px of it spent on the fixed form
            column, leaving 144px for the preview desk, which then spent `xl:p-12` (96px) on padding for ~48px
            of « paper », narrower than the card's own padding. That is not a preview of anything. 1280px is the
            « desktop » boundary globals.css already names (see its `--breakpoint-*` note): it is the first width
            at which the desk holds ~500px of A4, and it stacks tablet **landscape** (1180px) too. */}
        <div className="flex h-full flex-col overflow-y-auto xl:flex-row xl:overflow-hidden">
        {/* Left Panel - Input Fields */}
        {/* `bg-card/90`, not `bg-white/90 dark:bg-slate-950/90`: this is app chrome, so it follows the palette
            instead of maintaining its own light/dark pair by hand. (The A4 preview on the right is the opposite
            case — a paper surface, kept white on purpose. See its `.light` island below.) */}
        <div className="w-full shrink-0 border-b border-border bg-card/90 backdrop-blur-xl xl:w-[420px] xl:border-b-0 xl:border-r xl:overflow-y-auto">
          <div className="p-4 space-y-6 md:p-8">
            {/* Header */}
            <div className="space-y-4">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => router.push("/documents")}
                className="hover:bg-accent -ml-2"
              >
                <ArrowLeft className="w-4 h-4 mr-2" />
                Retour aux modèles
              </Button>

              <div className="space-y-2">
                <div className="flex items-center gap-3">
                  {/* The tinted icon chip `/documents` already uses for its template tiles — `size-12
                      rounded-lg` in the zone's own hue. It replaces `bg-gradient-to-br from-accent0
                      to-primary/90`, whose first stop (`accent0`) is not a class at all: the gradient rendered
                      as a single flat teal, and the white glyph on it was the only `text-white` in the file. */}
                  <div className={`flex size-12 items-center justify-center rounded-lg ${zoneChipClass(ZONES.clinical)}`}>
                    <FileText className="size-6" />
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
                        {loadingPatients ? "Chargement…" : "Sélectionner un patient…"}
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
                        placeholder="Rechercher un patient…"
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
                    ) : patientsFailed ? (
                      <LoadFailureNotice
                        message="La liste des patients n'a pas pu être chargée."
                        detail="Ce n'est pas un cabinet sans patients — la lecture a échoué."
                        onRetry={() => void loadPatients()}
                        className="m-2"
                      />
                    ) : filteredPatients.length === 0 ? (
                      <div className="p-8 text-center text-sm text-muted-foreground">
                        {patientSearchQuery ? "Aucun patient ne correspond." : "Aucun patient enregistré."}
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

            {/*
              THE PATIENT'S ALERTS, on every document type.

              ⚠️ This editor is where an **ordonnance** is written, and nothing on it read `patient.allergies`. The
              medication picker offers Clamoxyl and Augmentin, both of which carry `Amoxicilline` as a structured
              DCI in the seeded catalogue, and prescribing either to a penicillin-allergic patient raised nothing at
              all — the only allergy field in the whole editor was the liaison letter's empty textarea, which the
              prescriber was expected to retype from the patient's file in another tab.
              It is deliberately not gated on `documentType`: an allergy is not a property of the document being
              written, and a per-type copy is how the ordonnance came to be the one type without it.
              Read-only and outside the form: this is the patient's record, corrected in the patient's file. It does
              **not** block — a real DCI-vs-allergy check needs structured allergies (out of scope) — it makes the
              fact visible at the moment the decision is taken.
            */}
            {patientData && <PatientAlertPanel patient={patientData} />}

            {/* FR-4.1: external confrère destinataire — free text, no longer chosen from the clinic's doctors. */}
            {documentType === "liaison" && (
              <div className="space-y-3">
                <Label className="text-sm font-semibold text-foreground">Confrère destinataire</Label>
                <div className="space-y-2">
                  <Label htmlFor="recipientName" className="text-xs text-muted-foreground">Nom *</Label>
                  <Input
                    id="recipientName"
                    type="text"
                    placeholder="Ex : Dr Ahmed Ben Salah"
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
                    placeholder="Ex : Chirurgien maxillo-facial"
                    value={formFields.recipientSpecialty}
                    onChange={(e) => setFormFields({ ...formFields, recipientSpecialty: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientAddress" className="text-xs text-muted-foreground">Adresse</Label>
                  <Textarea
                    id="recipientAddress"
                    placeholder="Ex : 12 rue de la Santé, Tunis"
                    value={formFields.recipientAddress}
                    onChange={(e) => setFormFields({ ...formFields, recipientAddress: e.target.value })}
                    className="min-h-[60px]"
                  />
                </div>
                {/* Not printed on the letter — it prefills the recipient when the letter is sent by email. */}
                <div className="space-y-2">
                  <Label htmlFor="recipientEmail" className="text-xs text-muted-foreground">
                    E-mail (pour l'envoi de la lettre)
                  </Label>
                  <Input
                    id="recipientEmail"
                    type="email"
                    placeholder="Ex : confrere@cabinet.tn"
                    value={formFields.recipientEmail}
                    onChange={(e) => setFormFields({ ...formFields, recipientEmail: e.target.value })}
                    className="h-11"
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
                  <Button type="button" variant="outline" size="sm" onClick={addMedicationLine}>
                    <Plus className="w-4 h-4 mr-2" />
                    Ajouter un médicament
                  </Button>
                </div>

                {formFields.medications.length === 0 ? (
                  /* The shared empty state, with the action rather than a sentence describing it. The old copy
                     also used ASCII quotes around a French label — the product writes « … » everywhere else. */
                  <EmptyState
                    size="compact"
                    icon={Pill}
                    chipClassName={zoneChipClass(ZONES.clinical)}
                    title="Aucun médicament sur cette ordonnance"
                    description="Ajoutez une ligne, puis choisissez le médicament dans le catalogue pour reprendre son dosage et sa DCI."
                    action={
                      <Button type="button" variant="outline" size="sm" onClick={addMedicationLine}>
                        <Plus className="w-4 h-4 mr-2" />
                        Ajouter un médicament
                      </Button>
                    }
                  />
                ) : (
                  <div className="space-y-3">
                    {formFields.medications.map((med, index) => (
                      <MedicationItem
                        key={index}
                        medication={med}
                        catalog={medicationCatalog}
                        catalogFailed={medicationCatalogFailed}
                        onRetryCatalog={() => setMedicationCatalogReload((n) => n + 1)}
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

                {/* Renouvellement governs the whole ordonnance, so it sits with the document and not on a
                    medication row. Blank = the ordonnance says nothing about renewal, which is the default. */}
                <div className="space-y-2 pt-2">
                  <Label htmlFor="renewals" className="text-sm font-semibold text-foreground">
                    Renouvellement
                  </Label>
                  <Input
                    id="renewals"
                    type="text"
                    placeholder="Ex : 2 — ou « non » pour non renouvelable"
                    value={formFields.renewals}
                    onChange={(e) => setFormFields({ ...formFields, renewals: e.target.value })}
                    className="h-11"
                  />
                  <p className="text-xs text-muted-foreground">
                    Laissez vide pour ne rien mentionner. « non » ou « 0 » imprime « Ordonnance non renouvelable ».
                  </p>
                </div>
              </div>
            )}

            {/* Patient identity the norms require on a prescription (R.5132-3): the sexe is pre-rempli from the
                record and stays correctable; the poids is per-document and never stored on the patient. */}
            {documentType === "prescription" && (
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="patientSex" className="text-sm font-semibold text-foreground">Sexe</Label>
                  <Input
                    id="patientSex"
                    type="text"
                    placeholder="Ex : Femme"
                    value={formFields.patientSex}
                    onChange={(e) => setFormFields({ ...formFields, patientSex: e.target.value })}
                    className="h-11"
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="patientWeightKg" className="text-sm font-semibold text-foreground">
                    Poids (kg)
                  </Label>
                  <Input
                    id="patientWeightKg"
                    type="text"
                    placeholder="Ex : 32"
                    value={formFields.patientWeightKg}
                    onChange={(e) => setFormFields({ ...formFields, patientWeightKg: e.target.value })}
                    className="h-11"
                  />
                  <p className="text-xs text-muted-foreground">
                    Utile pour une posologie pédiatrique. Renseigné pour cette ordonnance uniquement.
                  </p>
                </div>
              </div>
            )}

            {/* Lettre de liaison — free text is the primary body; every norm section below is optional and
                folded away, so the doctor is never obliged to fill a form to write a letter. */}
            {documentType === "liaison" && (
              <>
                <div className="space-y-2">
                  <Label htmlFor="motif" className="text-sm font-semibold text-foreground">Motif de la liaison</Label>
                  <Textarea
                    id="motif"
                    placeholder="Demande d'avis spécialisé, de prise en charge, suite d'hospitalisation…"
                    value={formFields.motif}
                    onChange={(e) => setFormFields({ ...formFields, motif: e.target.value })}
                    className="min-h-[70px]"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="liaisonBody" className="text-sm font-semibold text-foreground">
                    Corps de la lettre / Synthèse clinique
                  </Label>
                  <Textarea
                    id="liaisonBody"
                    placeholder={"Cher Confrère,\n\nJe vous adresse ce patient pour…"}
                    value={formFields.content}
                    onChange={(e) => setFormFields({ ...formFields, content: e.target.value })}
                    className="min-h-[220px]"
                  />
                  <p className="text-xs text-muted-foreground">
                    Rédigez librement. Les sections ci-dessous sont facultatives — elles reprennent les éléments
                    attendus d'une lettre de liaison, à remplir uniquement si vous le souhaitez.
                  </p>
                </div>

                <details
                  className="rounded-lg border px-4 py-3"
                  open={liaisonExtrasOpen}
                  onToggle={(e) => setLiaisonExtrasOpen(e.currentTarget.open)}
                >
                  <summary className="cursor-pointer text-sm font-semibold text-foreground">
                    Sections complémentaires (facultatives)
                  </summary>
                  <div className="mt-4 space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="medecinTraitant" className="text-sm font-semibold text-foreground">
                        Médecin traitant / praticien adresseur
                      </Label>
                      <Input
                        id="medecinTraitant"
                        type="text"
                        placeholder="Dr …"
                        value={formFields.medecinTraitant}
                        onChange={(e) => setFormFields({ ...formFields, medecinTraitant: e.target.value })}
                        className="h-11"
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
                      <Label htmlFor="traitementEnCours" className="text-sm font-semibold text-foreground">
                        Traitement en cours et allergies connues
                      </Label>
                      <Textarea
                        id="traitementEnCours"
                        placeholder="Médicaments en cours, allergies, antécédents pouvant interférer avec les soins"
                        value={formFields.traitementEnCours}
                        onChange={(e) => setFormFields({ ...formFields, traitementEnCours: e.target.value })}
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
                    <div className="space-y-2">
                      <Label htmlFor="examensEnAttente" className="text-sm font-semibold text-foreground">
                        Résultats d'examens en attente
                      </Label>
                      <Textarea
                        id="examensEnAttente"
                        placeholder="Examens demandés dont les résultats ne sont pas encore disponibles"
                        value={formFields.examensEnAttente}
                        onChange={(e) => setFormFields({ ...formFields, examensEnAttente: e.target.value })}
                        className="min-h-[80px]"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="consignesSuivi" className="text-sm font-semibold text-foreground">
                        Consignes de suivi / avis attendu
                      </Label>
                      <Textarea
                        id="consignesSuivi"
                        placeholder="Ce que vous attendez du confrère, suivi à établir"
                        value={formFields.consignesSuivi}
                        onChange={(e) => setFormFields({ ...formFields, consignesSuivi: e.target.value })}
                        className="min-h-[80px]"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="piecesJointes" className="text-sm font-semibold text-foreground">Pièces jointes</Label>
                      <Textarea
                        id="piecesJointes"
                        placeholder="Radiographies, comptes rendus, photographies remis au patient"
                        value={formFields.piecesJointes}
                        onChange={(e) => setFormFields({ ...formFields, piecesJointes: e.target.value })}
                        className="min-h-[70px]"
                      />
                    </div>
                  </div>
                </details>
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
                    placeholder="Ex : certifie la présence de l'intéressé(e) ce jour ; soins dentaires en cours ; aptitude à la pratique sportive…"
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
                    Repos médical (facultatif)
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
                        placeholder="Ex : 3"
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

            {documentType === "arret-travail" && (
              <div className="space-y-5">
                {/*
                  The treating practitioner FIRST, chosen — never `doctors[0]`. His name, his quality and his code
                  are printed on the certificate and are what the caisse attributes the arret to, so the K3 lesson
                  applies here from the start rather than after the first misattributed form.
                */}
                <div className="space-y-2">
                  <Label htmlFor="arretDoctor" className="text-sm font-semibold text-foreground">
                    Praticien traitant
                  </Label>
                  <Select value={selectedDoctorId || undefined} onValueChange={setSelectedDoctorId}>
                    <SelectTrigger id="arretDoctor" className="h-11 w-full">
                      <SelectValue placeholder="Choisir le praticien…" />
                    </SelectTrigger>
                    <SelectContent>
                      {doctors
                        .filter((d) => d.id)
                        .map((d) => (
                          <SelectItem key={d.id} value={d.id as string}>
                            {d.name}
                            {d.codeProfessionnelSante
                              ? ` — ${d.codeProfessionnelSante}`
                              : d.ordreNumberCnomdt
                                ? ` — CNOMDT ${d.ordreNumberCnomdt}`
                                : " — sans code ni n° d'ordre"}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                  {/* Names the missing value AND where it is entered — visible text, not a `title`: a tooltip is
                      unreachable on the reception tablet these are filled on. */}
                  {selectedDoctor &&
                    !(selectedDoctor.codeProfessionnelSante || "").trim() &&
                    !(selectedDoctor.ordreNumberCnomdt || "").trim() && (
                      <p className="text-xs text-warning-ink">
                        Ni code conventionnel ni n° au Conseil de l&apos;Ordre sur ce profil — renseignez-le dans
                        «&nbsp;Mon profil&nbsp;».
                      </p>
                    )}
                </div>

                {/* The duration IS the document. `sm:grid-cols-2`, so one-up below — two number fields side by
                    side at 320 px leaves neither readable. */}
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="arretDays" className="text-sm font-semibold text-foreground">
                      Durée de l&apos;arrêt (en jours)
                    </Label>
                    <Input
                      id="arretDays"
                      type="number"
                      min="1"
                      max={ARRET_MAX_DAYS}
                      placeholder="Ex : 5"
                      value={arretFields.days}
                      onChange={(e) => setArretFields({ ...arretFields, days: e.target.value })}
                      className="h-11 md:text-sm"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="arretFrom" className="text-sm font-semibold text-foreground">
                      À compter du
                    </Label>
                    <Input
                      id="arretFrom"
                      type="date"
                      value={arretFields.fromDate}
                      onChange={(e) => setArretFields({ ...arretFields, fromDate: e.target.value })}
                      className="h-11 md:text-sm"
                    />
                  </div>
                </div>

                {/* « Sorties autorisées » — one statement made of a box and two hours, so both or neither. The
                    server refuses half of it; this is the visual half of that rule. */}
                <details className="rounded-lg border px-4 py-3">
                  <summary className="cursor-pointer text-sm font-semibold text-foreground">
                    Sorties autorisées (facultatives)
                  </summary>
                  <div className="grid grid-cols-1 gap-4 pt-4 sm:grid-cols-2">
                    <div className="space-y-2">
                      <Label htmlFor="arretOutFrom" className="text-sm font-semibold text-foreground">
                        De (heure)
                      </Label>
                      <Input
                        id="arretOutFrom"
                        type="number"
                        min="0"
                        max="23"
                        placeholder="Ex : 10"
                        value={arretFields.outingsFrom}
                        onChange={(e) => setArretFields({ ...arretFields, outingsFrom: e.target.value })}
                        className="h-11 md:text-sm"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="arretOutTo" className="text-sm font-semibold text-foreground">
                        À (heure)
                      </Label>
                      <Input
                        id="arretOutTo"
                        type="number"
                        min="0"
                        max="23"
                        placeholder="Ex : 16"
                        value={arretFields.outingsTo}
                        onChange={(e) => setArretFields({ ...arretFields, outingsTo: e.target.value })}
                        className="h-11 md:text-sm"
                      />
                    </div>
                  </div>
                </details>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="arretTrauma" className="text-sm font-semibold text-foreground">
                      En cas de traumatisme
                    </Label>
                    {/* An empty-string value is not selectable in a Radix Select, so « aucun » is its own explicit
                        option rather than a blank item — and it is the default, because most arrets are not
                        traumatic and the form leaves all three boxes empty then. */}
                    <Select
                      value={arretFields.traumaCause || "none"}
                      onValueChange={(v) => setArretFields({ ...arretFields, traumaCause: v === "none" ? "" : v })}
                    >
                      <SelectTrigger id="arretTrauma" className="h-11 w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="none">Aucun / sans objet</SelectItem>
                        {TRAUMA_CAUSES.map((cause) => (
                          <SelectItem key={cause} value={cause}>
                            {TRAUMA_CAUSE_LABELS_FR[cause as TraumaCause]}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="arretHosp" className="text-sm font-semibold text-foreground">
                      Hospitalisé pendant l&apos;arrêt&nbsp;?
                    </Label>
                    {/* Three states, and « Non renseigné » is the default: ticking « Non » by default would make
                        the software assert a clinical fact nobody entered, on a form that decides an indemnity. */}
                    <Select
                      value={arretFields.hospitalised || "unknown"}
                      onValueChange={(v) =>
                        setArretFields({ ...arretFields, hospitalised: v === "unknown" ? "" : v })
                      }
                    >
                      <SelectTrigger id="arretHosp" className="h-11 w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="unknown">Non renseigné</SelectItem>
                        <SelectItem value="false">Non</SelectItem>
                        <SelectItem value="true">Oui (joindre l&apos;attestation)</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="arretMotif" className="text-sm font-semibold text-foreground">
                    Motif (dossier interne — non imprimé)
                  </Label>
                  <Textarea
                    id="arretMotif"
                    rows={2}
                    placeholder="Ex : avulsion de 38, œdème post-opératoire"
                    value={arretFields.motif}
                    onChange={(e) => setArretFields({ ...arretFields, motif: e.target.value })}
                    className="md:text-sm"
                  />
                  {/* Stated, because a field that looks like every other field and behaves differently is a trap:
                      the practitioner needs to know the employer will not read this. */}
                  <p className="text-xs text-muted-foreground">
                    Conservé dans le dossier du patient, <strong>jamais imprimé</strong> sur le certificat&nbsp;: le
                    recto du formulaire P 061 est remis à l&apos;employeur et ne porte aucun diagnostic.
                  </p>
                </div>

                <p className="rounded-md bg-muted/40 p-3 text-xs text-muted-foreground">
                  L&apos;identité du patient (identifiant unique, nom, date de naissance, adresse, code postal,
                  téléphone) est reprise automatiquement de sa fiche et pré-remplie sur la partie
                  «&nbsp;assuré social&nbsp;» du formulaire. Les deux signatures et le cachet restent à apposer sur
                  le papier.
                </p>
              </div>
            )}

            {documentType === "bulletin-cnam" && (
              <div className="space-y-5">
                {/*
                  K3 — the treating practitioner, chosen. First field of the bulletin, because its code
                  conventionnel is stamped on **every** act row of the printed form and it used to be
                  `doctors[0]` with nothing on screen naming anyone.
                  Kept visible even in a single-dentist cabinet (pre-filled, not hidden): what the form asserts
                  about who performed the acts should be readable before it is printed, not inferred.
                */}
                <div className="space-y-2">
                  <Label htmlFor="bulletinDoctor" className="text-sm font-semibold text-foreground">
                    Praticien traitant
                  </Label>
                  <Select value={selectedDoctorId || undefined} onValueChange={setSelectedDoctorId}>
                    <SelectTrigger id="bulletinDoctor" className="h-11 w-full">
                      <SelectValue placeholder="Choisir le praticien…" />
                    </SelectTrigger>
                    <SelectContent>
                      {doctors
                        .filter((d) => d.id)
                        .map((d) => (
                          <SelectItem key={d.id} value={d.id as string}>
                            {d.name}
                            {d.codeProfessionnelSante ? ` — ${d.codeProfessionnelSante}` : " — sans code conventionnel"}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                  {/*
                    The certificat's CNOMDT treatment, applied to the code conventionnel: name the missing value and
                    where it is entered. Visible text, not a `title` — a tooltip is unreachable on a reception
                    tablet, which is where a secretary fills these.
                  */}
                  {selectedDoctor && !bulletinDoctorCode ? (
                    <p className="text-xs text-warning-ink">
                      Aucun code conventionnel sur le profil de {selectedDoctor.name}. Ajoutez-le dans «&nbsp;
                      <button
                        type="button"
                        className="underline underline-offset-2"
                        onClick={() => router.push("/mon-profil")}
                      >
                        Mon profil
                      </button>
                      &nbsp;»&nbsp;: il s&apos;imprime sur chaque ligne d&apos;acte du bulletin.
                    </p>
                  ) : (
                    <p className="text-xs text-muted-foreground">
                      Son code conventionnel est imprimé sur chaque ligne d&apos;acte du formulaire.
                    </p>
                  )}
                </div>

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
                    <Input id="apciCode" value={bulletinFields.apciCode} onChange={(e) => setBulletinFields((p) => ({ ...p, apciCode: e.target.value }))} className="h-11" placeholder="Ex : 12" />
                  </div>
                )}

                <div className="space-y-3">
                  <Label className="text-sm font-semibold text-foreground">Actes (depuis les soins dentaires)</Label>
                  {/* Two date fields at ~120px each on a 360px phone — see the medication card above (defect #3). */}
                  <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    <div className="flex flex-col gap-1">
                      <Label className="text-xs text-muted-foreground">Du</Label>
                      <Input type="date" value={bulletinFields.actsFrom} onChange={(e) => setBulletinFields((p) => ({ ...p, actsFrom: e.target.value }))} className="h-10" />
                    </div>
                    <div className="flex flex-col gap-1">
                      <Label className="text-xs text-muted-foreground">Au</Label>
                      <Input type="date" value={bulletinFields.actsTo} onChange={(e) => setBulletinFields((p) => ({ ...p, actsTo: e.target.value }))} className="h-10" />
                    </div>
                  </div>

                  {/* A failed read of the patient's soins would otherwise render « Pré-remplir depuis les soins
                      (0) », disabled — indistinguishable from a patient with no soins at all, and the bulletin
                      then gets typed from memory (defect #1). */}
                  {dentalRecordsFailed ? (
                    <CatalogLoadFailed
                      label="Les soins du patient"
                      onRetry={() => setDentalRecordsReload((n) => n + 1)}
                    />
                  ) : (
                    <Button type="button" variant="outline" size="sm" className="w-full" onClick={prefillActsFromRecords} disabled={!selectedPatient || dentalRecords.length === 0}>
                      <Search className="w-4 h-4 mr-2" />
                      Pré-remplir depuis les soins ({dentalRecords.length})
                    </Button>
                  )}

                  {bulletinFields.acts.length === 0 ? (
                    <EmptyState
                      size="compact"
                      icon={ClipboardList}
                      chipClassName={zoneChipClass(ZONES.clinical)}
                      title="Aucun acte sur ce bulletin"
                      description="Pré-remplissez depuis les soins enregistrés du patient, ou ajoutez une ligne à la main."
                      action={
                        <Button type="button" variant="outline" size="sm" onClick={addBulletinAct}>
                          <Plus className="w-4 h-4 mr-2" />
                          Ajouter un acte
                        </Button>
                      }
                    />
                  ) : (
                    <div className="space-y-3">
                      {bulletinFields.acts.map((act, index) => {
                        const actEstimate = actEstimates[index] ?? null
                        const estimateReason = actEstimateReasons[index] ?? null
                        // K1: the catalogue row behind this act's code, if the code is one of ours. `undefined`
                        // for a hand-typed code and for every act of a pre-K1 bulletin (stored mnemonics).
                        const catalogAct = dentalActFor(act.codeActe)
                        // A catalogue act whose cotation the DCH list does not carry (the seed leaves every
                        // Coefficient null — it lives in the NGAP arrêté). Named, because the alternative is an
                        // estimate column that is simply blank, which reads as « non remboursable ».
                        const missingCoefficient =
                          catalogAct != null && catalogAct.coefficient == null && !parseCotation(act.cotation)
                        return (
                        <div key={index} className="p-3 border rounded-lg space-y-2">
                          <div className="flex items-center justify-between">
                            <span className="text-xs font-medium text-muted-foreground">Acte {index + 1}</span>
                            <Button type="button" variant="ghost" size="sm" className="h-7 w-7 p-0" aria-label={`Retirer l'acte ${index + 1}`} onClick={() => setBulletinFields((p) => ({ ...p, acts: p.acts.filter((_, i) => i !== index) }))}>
                              <X className="w-4 h-4" />
                            </Button>
                          </div>
                          {/*
                            Same collapse as the other four grids (defect #3), plus the `md:text-sm` prefix on
                            every field (defect #4): `ui/input.tsx` ships `text-base md:text-sm` precisely so
                            iOS Safari does not zoom a focused field — and it never zooms back out. An
                            UNPREFIXED `text-sm` from a call site is in the same tailwind-merge group, so it
                            *removes* the primitive's `text-base` and the field is 14px at every width.

                            ⚠️ The child below needs `col-span-1 sm:col-span-2`: a `span 2` item in a
                            one-column grid creates an implicit second column, which would break the row rather
                            than let it stack.
                          */}
                          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                            <Input type="date" value={act.date} onChange={(e) => updateBulletinAct(index, "date", e.target.value)} className="h-9 md:text-sm" />
                            <Input placeholder="Dent(s)" value={act.teeth} onChange={(e) => updateBulletinAct(index, "teeth", e.target.value)} className="h-9 md:text-sm" />
                            <div className="col-span-1 flex gap-2 sm:col-span-2">
                              <Input placeholder="Code acte" value={act.codeActe} onChange={(e) => updateBulletinAct(index, "codeActe", e.target.value)} className="h-9 flex-1 md:text-sm" />
                              <Popover open={openActLookup === index} onOpenChange={(o) => setOpenActLookup(o ? index : null)} modal>
                                <PopoverTrigger asChild>
                                  <Button type="button" variant="outline" size="sm" className="h-9 px-3 shrink-0" title="Rechercher un acte dentaire CNAM (DCH)">
                                    <Search className="w-4 h-4" />
                                    <span className="sr-only">Rechercher un acte dentaire CNAM (DCH)</span>
                                  </Button>
                                </PopoverTrigger>
                                {/* `w-[min(20rem,calc(100vw-2rem))]`: an unqualified `w-80` is 320px inside a
                                    320px viewport, i.e. edge to edge with no gutter. */}
                                <PopoverContent className="p-0 w-[min(20rem,calc(100vw-2rem))]" align="end">
                                  <Command>
                                    <CommandInput placeholder="Rechercher un acte (code DCH ou désignation)…" />
                                    <CommandList>
                                      {dentalActCatalogFailed ? (
                                        <CatalogLoadFailed
                                          label="Le catalogue des actes dentaires"
                                          onRetry={() => setDentalActCatalogReload((n) => n + 1)}
                                        />
                                      ) : (
                                        <>
                                          <CommandEmpty>Aucun acte ne correspond.</CommandEmpty>
                                          <CommandGroup>
                                            {dentalActCatalog.map((entry) => (
                                              <CommandItem key={entry.codeActe} value={`${entry.codeActe} ${entry.designationFr} ${entry.lettreCle} ${entry.category}`} onSelect={() => selectDentalAct(index, entry)}>
                                                <div className="flex min-w-0 flex-col">
                                                  <span className="text-sm font-medium">{entry.designationFr}</span>
                                                  <span className="text-xs text-muted-foreground">
                                                    {entry.codeActe} · {entry.lettreCle}
                                                    {entry.coefficient != null ? ` ${entry.coefficient}` : ""} · {entry.category}
                                                  </span>
                                                  {/* K1's free win: the flag was seeded correctly and consumed by
                                                      nothing but its own admin table. Shown at the point of choice
                                                      as well as on the row, because « demander l'accord d'abord »
                                                      is a decision made when picking the act. */}
                                                  {entry.requiresAccordPrealable && (
                                                    <span className="text-xs text-warning-ink">Accord préalable requis</span>
                                                  )}
                                                </div>
                                              </CommandItem>
                                            ))}
                                          </CommandGroup>
                                        </>
                                      )}
                                    </CommandList>
                                  </Command>
                                </PopoverContent>
                              </Popover>
                            </div>
                            <Input placeholder="Cotation" value={act.cotation} onChange={(e) => updateBulletinAct(index, "cotation", e.target.value)} className="h-9 md:text-sm" />
                            <Input placeholder="Honoraires (DT)" value={act.honoraires} onChange={(e) => updateBulletinAct(index, "honoraires", e.target.value)} className="h-9 md:text-sm" />
                          </div>
                          {/* K1's free win, on the row this time: `RequiresAccordPrealable` is correctly seeded
                              and was consumed by nothing outside its own admin table. Now that the bulletin reads
                              this catalogue it costs nothing to say so — and an act sent without the accord is a
                              claim the caisse refuses. Derived from the code at render time, never persisted:
                              nothing on the BS1 carries the flag, and it is per-clinic and correctable. */}
                          {catalogAct?.requiresAccordPrealable && (
                            <p className="text-xs text-warning-ink">
                              Accord préalable requis&nbsp;— joignez la demande avant d&apos;envoyer ce bulletin.
                            </p>
                          )}
                          {/* A catalogue act carrying no coefficient. Says which half is missing and where it is
                              filled in, instead of leaving the estimate column silently empty — « pas d'estimation »
                              and « non remboursable » must never look the same. */}
                          {missingCoefficient && (
                            <p className="text-xs text-muted-foreground">
                              Cotation à compléter&nbsp;: le catalogue ne fixe pas de coefficient pour cet acte
                              (il figure à l&apos;arrêté NGAP). Saisissez «&nbsp;{catalogAct?.lettreCle}&nbsp;
                              coefficient&nbsp;» pour obtenir une estimation — le remboursement reste calculé par la
                              CNAM dans tous les cas.
                            </p>
                          )}
                          {/* A cotation that parses but whose lettre clé the convention values at nothing. Unlike
                              the case above, this is not a gap anybody can close — so it says so rather than
                              pointing at the catalogue, and never renders as 0. */}
                          {actEstimate == null && estimateReason === 'NoLetterValue' && (
                            <p className="text-xs text-muted-foreground">
                              Aucune estimation&nbsp;: la convention ne fixe pas de valeur pour la lettre clé
                              «&nbsp;{parseCotation(act.cotation)?.lettreCle}&nbsp;». Le remboursement reste calculé
                              par la CNAM.
                            </p>
                          )}
                          {/* `formatDT`, not `toFixed(3) + " TND"` (defect #5): a period decimal separator and a
                              currency code the product uses nowhere else, on a CNAM document. */}
                          {actEstimate != null && (
                            <p className="text-xs text-muted-foreground">Remb. indicatif&nbsp;: <span className="font-medium text-foreground">{formatDT(actEstimate)}</span></p>
                          )}
                        </div>
                        )
                      })}
                    </div>
                  )}

                  <Button type="button" variant="outline" size="sm" className="w-full" onClick={addBulletinAct}>
                    <Plus className="w-4 h-4 mr-2" />
                    Ajouter un acte
                  </Button>

                  {/* On the theme's warning family rather than four hand-maintained `amber-*` / `dark:amber-*`
                      pairs. `--warning-ink` exists because `--warning` itself lands near 3.5:1 on its own wash. */}
                  {estimateFailed && (
                    <div role="status" className="rounded-lg border border-dashed border-warning/40 bg-warning-wash p-3">
                      <p className="text-xs text-warning-ink">
                        Estimation du remboursement indisponible — le calcul n&apos;a pas pu être effectué. Le bulletin
                        reste valide&nbsp;: l&apos;estimation est indicative et ne figure pas sur le formulaire.
                      </p>
                    </div>
                  )}

                  {!estimateFailed && hasAnyBulletinEstimate && (
                    <div className="rounded-lg border border-dashed p-3 space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-foreground">Remboursement indicatif (total)</span>
                        <span className="text-sm font-semibold text-foreground">{formatDT(bulletinEstimateTotal)}</span>
                      </div>
                      <p className="text-xs text-muted-foreground">Estimation indicative, non contractuelle — montant réel fixé par la CNAM. Taux selon l'âge du patient (70&nbsp;% de 4 à 18&nbsp;ans, 60&nbsp;% sinon).</p>
                    </div>
                  )}

                  {/*
                    L10 — the ceiling, right under the estimate it caps. Rendered whenever a patient is selected and
                    NOT gated on `hasAnyBulletinEstimate`: « ce patient a déjà épuisé son plafond » is worth knowing
                    *before* the acts are typed, which is the moment an alternative can still be discussed. It passes
                    the running total so it can also answer « et après ce bulletin ? » without a second request.
                  */}
                  <CnamCeilingNotice
                    patientId={selectedPatient || null}
                    pendingEstimate={hasAnyBulletinEstimate ? bulletinEstimateTotal : undefined}
                  />
                </div>
              </div>
            )}

            <Separator />

            {/* Actions */}
            <div className="space-y-3 pt-2">
              {/*
                K2 — the reason Save is unavailable, as **visible text** above the button.
                Not a `title` and not only a toast: a `title` is unreachable on the reception tablet these are
                filled on, and a toast fires after the press, i.e. after the practitioner has already decided the
                bulletin was finished. Each line says where the value is entered, because four of the five live on
                the patient's fiche or the practitioner's profile rather than in this editor.
                `role="status"` so the list is announced as it changes while the fiche is being completed.
              */}
              {officialFormBlocked && (
                <div
                  role="status"
                  className="space-y-2 rounded-lg border border-warning/40 bg-warning-wash p-3"
                >
                  <p className="text-xs font-medium text-warning-ink">
                    {documentType === "arret-travail"
                      ? "Arrêt de travail incomplet — la caisse le refuserait :"
                      : "Bulletin incomplet — la caisse le refuserait :"}
                  </p>
                  <ul className="list-disc space-y-1 ps-4 text-xs text-warning-ink">
                    {officialFormProblems.map((problem) => (
                      <li key={problem.key}>{problem.message}</li>
                    ))}
                  </ul>
                  {officialFormProblems.some((p) => p.onPatient) && selectedPatient && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="w-full"
                      onClick={() => router.push(`/patients/${selectedPatient}`)}
                    >
                      Ouvrir la fiche du patient
                    </Button>
                  )}
                </div>
              )}
              {documentLoadFailed && (
                <LoadFailureNotice
                  message="Le document enregistré n'a pas pu être chargé."
                  detail="Il n'est pas modifiable tant qu'il n'est pas lu — enregistrer maintenant créerait un second document vide."
                  onRetry={() => setDocumentReload((n) => n + 1)}
                />
              )}
              <Button
                className="w-full h-11 bg-primary hover:bg-primary/90 text-base font-medium"
                onClick={() => handleSave()}
                disabled={saving || loadingDocument || documentLoadFailed || !selectedPatient || officialFormBlocked}
              >
                <Save className="w-4 h-4 mr-2" />
                {loadingDocument
                  ? "Chargement du document…"
                  : saving
                    ? "Enregistrement…"
                    : documentId
                      ? "Mettre à jour"
                      : "Enregistrer le document"}
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
              {/* Only offered once the document is saved: the server renders the attachment from its id, so
                  there is nothing to send while the document exists only in this form. */}
              {documentId && (
                <Button
                  variant="outline"
                  className="w-full h-11 bg-transparent"
                  onClick={() => setEmailOpen(true)}
                  disabled={saving}
                >
                  <Mail className="w-4 h-4 mr-2" />
                  Envoyer par e-mail
                </Button>
              )}
              <div className="grid grid-cols-2 gap-3">
                <Button variant="outline" onClick={resetForm} className="h-11 bg-transparent">
                  <RotateCcw className="w-4 h-4 mr-2" />
                  Réinitialiser
                </Button>
                {/* `!patientData`, matching « Télécharger PDF » / « Word » below: it was the one output action
                    with no patient gate, so a blank document — letterhead and cachet, no patient, no content —
                    went to the printer. Four ways out, one of them ungated, is not a decision anybody made. */}
                <Button
                  variant="outline"
                  className="h-11 bg-transparent"
                  onClick={() => handlePrint()}
                  disabled={!patientData || saving}
                >
                  <Printer className="w-4 h-4 mr-2" />
                  Imprimer
                </Button>
              </div>
              {/*
                K5 — one column when Word is not on offer. `grid-cols-2` with a single child leaves « Télécharger
                PDF » at half width beside a gap, which reads as a control that failed to render rather than as one
                that does not apply.
              */}
              <div className={`grid gap-3 ${wordExportSupported ? "grid-cols-2" : "grid-cols-1"}`}>
                <Button
                  variant="outline"
                  /* `--success` + its wash, not a `green-500/600/50/950` quartet with a hand-written dark twin.
                     Same colour in both themes, and it follows the palette when the palette moves. */
                  className="h-11 border-success bg-transparent text-success hover:bg-success-wash"
                  onClick={() => handleDownloadPdf()}
                  disabled={!patientData || saving}
                >
                  <Download className="w-4 h-4 mr-2" />
                  Télécharger PDF
                </Button>
                {wordExportSupported && (
                  <Button
                    variant="outline"
                    className="h-11 bg-transparent border-primary text-primary hover:bg-accent"
                    onClick={() => generateWord()}
                    disabled={!patientData || saving}
                  >
                    <Download className="w-4 h-4 mr-2" />
                    Télécharger Word
                  </Button>
                )}
              </div>
              {/* Says why rather than just omitting the control: a button that was there yesterday and is gone
                  today reads as a bug. See `wordExportSupported`. */}
              {!wordExportSupported && (
                <p className="text-xs text-muted-foreground">
                  {documentType === "arret-travail"
                    ? "L'arrêt de travail n'a pas d'export Word : c'est une impression sur le formulaire officiel CNAM P 061."
                    : "Le bulletin de soins n'a pas d'export Word : c'est une impression sur le formulaire officiel BS1."}{" "}
                  Utilisez «&nbsp;Télécharger PDF&nbsp;» ou «&nbsp;Imprimer&nbsp;».
                </p>
              )}
            </div>
          </div>
        </div>

        {/* Right Panel - Document Preview. Its own scroll container only from `xl:` up — stacked, the outer
            column scrolls once instead of nesting two scrollers on a phone or a tablet. */}
        {/* The desk the paper sits on — app chrome, so it takes the palette instead of a `slate-*` gradient with
            a hand-written dark twin. */}
        <div className="min-w-0 flex-1 bg-gradient-to-br from-muted to-accent/60 p-4 xl:overflow-y-auto xl:p-12">
          <div className="max-w-4xl mx-auto">
            {isOfficialForm ? (
              <>
                <div className="mb-6 flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-muted-foreground">Aperçu du document</p>
                    <p className="text-xs text-muted-foreground mt-1">
                      {documentType === "arret-travail"
                        ? "Aperçu en direct du formulaire CNAM P 061 généré"
                        : "Aperçu en direct du bulletin BS1 généré"}
                    </p>
                  </div>
                  <div className="text-sm text-muted-foreground">Format A4</div>
                </div>

                {/*
                  `.light` — a document surface, not app chrome (AC-39, see the `@custom-variant dark` comment
                  at the top of globals.css). The variant is `&:is(.dark *):not(:is(.light, .light *))`, so this
                  subtree keeps the light palette *and* stops every `dark:` utility inside it. That is the point:
                  the BS1 the patient files at the CNAM is black on white, and a preview that renders it on slate
                  in dark mode is showing the dentist something other than what will be printed. It is also why
                  the `dark:bg-slate-900` this element used to carry is gone rather than merely overridden — the
                  variant makes it inert, and a class that cannot fire is a class that misleads the next reader.
                */}
                <div className="light relative bg-white shadow-2xl rounded-lg overflow-hidden min-h-[1123px] flex flex-col">
                  {!patientData ? (
                    <div className="flex-1 flex flex-col items-center justify-center gap-3 p-12 text-center">
                      <FileText className="w-12 h-12 text-muted-foreground/40" />
                      <p className="text-sm text-muted-foreground">
                        Sélectionnez un patient pour afficher l&apos;aperçu du bulletin de soins CNAM.
                      </p>
                    </div>
                  ) : (
                    <>
                      {bs1PreviewUrl ? (
                        <>
                          {/* Two trees behind `coarse:`, the same shape as `patient-file-pdf-preview.tsx` — and
                              for the same reason: an Android WebView renders an embedded `blob:` PDF **blank**
                              and iOS Safari renders it as one non-scrollable page. CSS, not `useMediaQuery`,
                              which returns false on the first client render and would tear down a loaded PDF. */}
                          <iframe
                            ref={bs1IframeRef}
                            src={bs1PreviewUrl}
                            title={officialFormPreviewTitle}
                            className="block flex-1 w-full border-0 coarse:hidden"
                            style={{ minHeight: "1123px" }}
                          />

                          <div className="hidden flex-1 flex-col items-center justify-center gap-3 p-6 text-center coarse:flex">
                            <FileText className="w-12 h-12 text-muted-foreground/40" />
                            <p className="font-medium text-foreground">Aperçu non disponible sur cet appareil</p>
                            <p className="max-w-[42ch] text-sm text-muted-foreground">
                              Les visionneuses PDF intégrées ne fonctionnent pas de façon fiable sur mobile. Ouvrez le
                              document pour le consulter et l&apos;imprimer depuis la visionneuse de votre appareil,
                              sur le formulaire pré-imprimé.
                            </p>
                            {/* 44px on a finger, grown rather than overlaid: it is the panel's only control. */}
                            <Button onClick={() => void deliverOfficialFormPdf("preview")} className="coarse:h-11">
                              <ExternalLink className="me-2 h-4 w-4" />
                              Ouvrir le document
                            </Button>
                          </div>
                        </>
                      ) : (
                        <div className="flex-1 flex flex-col items-center justify-center gap-3 p-12 text-center">
                          {bs1PreviewError && !bs1PreviewLoading ? (
                            <>
                              <FileText className="w-12 h-12 text-destructive" />
                              <p className="text-sm font-medium text-foreground">Impossible de générer l&apos;aperçu du PDF</p>
                              <p className="text-xs text-muted-foreground">
                                Une erreur s&apos;est produite. Modifiez un champ pour réessayer.
                              </p>
                            </>
                          ) : (
                            !bs1PreviewLoading && (
                              <p className="text-sm text-muted-foreground">Préparation de l&apos;aperçu…</p>
                            )
                          )}
                        </div>
                      )}

                      {bs1PreviewLoading && (
                        <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-card/70 backdrop-blur-sm">
                          <Loader2 className="w-8 h-8 animate-spin text-primary" />
                          <p className="text-sm text-muted-foreground">Génération de l&apos;aperçu…</p>
                        </div>
                      )}

                      {/* The theme's destructive family, which is what `--destructive-wash` was added for —
                          replacing a `red-50/200/600` trio plus three `dark:` twins that no theme could follow.
                          Inside the `.light` island above, these resolve to the light palette, i.e. on paper. */}
                      {bs1PreviewError && !bs1PreviewLoading && bs1PreviewUrl && (
                        <div className="absolute inset-x-0 top-0 border-b border-destructive/25 bg-destructive-wash px-4 py-2">
                          <p className="text-xs text-destructive text-center">
                            Impossible de mettre à jour l&apos;aperçu — dernière version affichée. Modifiez un champ pour réessayer.
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

            {/*
              `p-6 sm:p-10 xl:p-16` (defect #2). An unprefixed `p-16` is 128px of a 358px phone — 36 % of the
              width spent on margin, leaving ~230px of « paper » for 11pt type and two `grid-cols-2` identity
              blocks. The preview's only job is to show what will be printed, and at 230px it reflows into a
              layout the A4 will not have, so it stops being a preview. The A4 metrics (`min-h-[1123px]`, the pt
              sizes, the two-column identity blocks) are deliberately NOT made responsive for the same reason.
              The `sm:p-10` step exists because the padding must follow the SPLIT, not the old `md:` hinge: now
              that the document is stacked below the form up to 1280px, a tablet portrait renders a ~532px paper,
              on which `p-16` would again leave only ~276px of content. 40px is ~7.5 % of 532px — about what a
              real A4's 2cm margin is — where 24px would read as no margin at all.

              `.light` — the second document surface (AC-39; see the BS1 wrapper above and the `@custom-variant
              dark` comment in globals.css). `bg-white` stays and the `dark:bg-slate-900` twin goes: a certificat
              médical that is white-on-black on screen and black-on-white on paper is not a preview of anything.
            */}
            <Card className="light p-6 sm:p-10 xl:p-16 bg-white shadow-2xl min-h-[1123px] flex flex-col" ref={documentRef} style={{ fontFamily: 'Helvetica, Arial, sans-serif' }}>
              <div className="flex-1 flex flex-col space-y-5" style={{ fontSize: '11pt', lineHeight: '1.5' }}>
                {/* Letterhead */}
                <div className="space-y-1 pb-4">
                  <h1
                    className="font-bold text-primary focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                    style={{ fontSize: '14pt' }}
                  >
                    {formData.clinicName}
                  </h1>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    {formData.clinicAddress}
                  </p>
                  <p
                    className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    Tél: {formData.clinicPhone}
                  </p>
                  {/* Mirrors the server's DocumentIdentity.PrescriberLines. The email is resolved server-side
                      and not held in this form, so the preview names it without a value rather than pretending
                      the printed document will omit it. */}
                  {clinicInfo?.email && (
                    <p className="text-muted-foreground px-1" style={{ fontSize: '11pt' }}>
                      Email : {clinicInfo.email}
                    </p>
                  )}
                  <p
                    className="font-bold focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                    style={{ fontSize: '11pt' }}
                  >
                    {formData.doctorName} — {formData.doctorSpecialty}
                  </p>
                  {formFields.doctorOrderNumber && (
                    <p className="px-1" style={{ fontSize: '11pt' }}>
                      N° CNOMDT : {formFields.doctorOrderNumber}
                    </p>
                  )}
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
                    className="focus:outline-none focus:ring-2 focus:ring-ring rounded px-1 inline-block"
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
                        className="font-bold focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                        style={{ fontSize: '12pt' }}
                      >
                        {patientData ? getPatientName(patientData) : "Sélectionnez un patient"}
                      </p>
                    </div>
                    {patientData?.dateOfBirth && (
                      <div>
                        <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Date de naissance</p>
                        <p
                          className="focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
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
                  {/* Mirrors DocumentIdentity.PatientLines: sexe and poids belong with the patient's identity,
                      and an unset value prints no label at all rather than an empty one. */}
                  {(formFields.patientSex.trim() || formFields.patientWeightKg.trim()) && (
                    <div className="grid grid-cols-2 gap-4">
                      {formFields.patientSex.trim() && (
                        <div>
                          <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Sexe</p>
                          <p className="px-1" style={{ fontSize: '12pt' }}>{formFields.patientSex.trim()}</p>
                        </div>
                      )}
                      {formFields.patientWeightKg.trim() && (
                        <div>
                          <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Poids</p>
                          <p className="px-1" style={{ fontSize: '12pt' }}>
                            {/^.*kg\s*$/i.test(formFields.patientWeightKg.trim())
                              ? formFields.patientWeightKg.trim()
                              : `${formFields.patientWeightKg.trim()} kg`}
                          </p>
                        </div>
                      )}
                    </div>
                  )}
                  {/* Mirrors the PDF's identity block: the norms keep the professionals' identity with the
                      patient's, not in the clinical synthèse. */}
                  {documentType === "liaison" && formFields.medecinTraitant.trim() && (
                    <div>
                      <p className="text-muted-foreground mb-1" style={{ fontSize: '9pt' }}>Médecin traitant / praticien adresseur</p>
                      <p className="px-1" style={{ fontSize: '12pt' }}>{formFields.medecinTraitant.trim()}</p>
                    </div>
                  )}
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
                            const medText = formatMedicationLine(med);
                            return (
                              <div key={idx} className="py-1" style={{ fontSize: '11pt' }}>
                                {medText}
                              </div>
                            );
                          })}
                        </div>
                      ) : (
                        // Deliberately NOT an `EmptyState`: this is inside the paper, and `handlePrint` clones
                        // this subtree straight into the print window — an icon chip and a call to action would
                        // be printed onto the ordonnance. Inside the `.light` island `border-border` resolves to
                        // the light palette, so the dashed box is a hairline on paper in either theme.
                        <div className="min-h-[200px] p-4 border-2 border-dashed border-border rounded-lg text-muted-foreground" style={{ fontSize: '11pt' }}>
                          Aucun médicament ajouté
                        </div>
                      )}
                      {/* Governs the document, so it renders once below the lines — never against one médicament. */}
                      {formatRenewalMention(formFields.renewals) && (
                        <p className="italic pt-2" style={{ fontSize: '11pt' }}>
                          {formatRenewalMention(formFields.renewals)}
                        </p>
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
                    <div className="w-48 h-16 border-b border-foreground/40"></div>
                  </div>
                  <div className="text-right space-y-1">
                    <p
                      className="font-bold focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
                      style={{ fontSize: '12pt' }}
                    >
                      {formData.doctorName}
                    </p>
                    <p
                      className="text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring rounded px-1"
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

      {documentId && (
        <SendDocumentEmailDialog
          open={emailOpen}
          onOpenChange={setEmailOpen}
          documentKind={DOCUMENT_EMAIL_KINDS.MedicalDocument}
          documentId={documentId}
          documentLabel={getDocumentTitle()}
          // A lettre de liaison goes to the confrère, not the patient — which is the whole point of the letter.
          defaultRecipientEmail={documentType === "liaison" ? formFields.recipientEmail : null}
          patientId={documentType === "liaison" ? null : selectedPatient || null}
        />
      )}
    </div>
  )
}

