"use client"

import { useState, useRef, useEffect, useCallback, useMemo } from "react"
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
  Pill,
  ClipboardList,
  AlertTriangle,
  ExternalLink,
  Trash2,
} from "lucide-react"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { BillableActsDialog } from "@/components/documents/billable-acts-dialog"
import { DocumentPreviewDialog } from "@/components/documents/document-preview-dialog"
import { formatAmount, formatDT, formatDateFr, quoteFr, todayLocalIso } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import {
  DURATION_UNITS,
  patientCivility,
  durationUnitLabel,
  durationUnitOf,
  matchMedications,
  medicationCatalogLabel,
  prescriptionLineParts,
  showsMedicalAlerts,
  type MedicationLine,
} from "@/lib/documents"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useRouter, useParams, useSearchParams } from "next/navigation"
import { patientsApi } from "@/lib/api/patients"
import { appointmentsApi } from "@/lib/api/appointments"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import { clinicsApi } from "@/lib/api/clinics"
import type { BillableActLine } from "@/lib/api/dental-records"
import { medicationsApi } from "@/lib/api/medications"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { PatientDto, MedicationDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { specialtyLabel } from "@/lib/specialties"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { downloadBlob } from "@/lib/download"
import { printPdfBlob } from "@/lib/print"
import { Document, Packer, Paragraph, HeadingLevel, AlignmentType, TextRun, BorderStyle } from "docx"

/*
 * Certificat médical — ONE sentence, and `CertificatTextBuilder` (which renders the PDF) is the authority on
 * its wording. `CERTIFICAT_ORDRE_LABEL` (« Ordre National des Médecins Dentistes ») and
 * `CERTIFICAT_MANDATORY_MENTION` (« … remis en main propre pour faire valoir ce que de droit. ») were here and
 * are gone with the spécialité, the date de naissance and the objet/motif: read the tombstone on the server
 * builder before putting any of them back. The practitioner and the cabinet are still identified — in the
 * letterhead, on every document type.
 */

/*
 * The prescription line type and its two formatters now live in `lib/documents.ts` — see the imports at the
 * top of this file. They moved because the fiche de soins writes the same ordonnance, and a private copy in
 * here is how this formatting came to exist in three places once already.
 *
 * `MedicationLine` is re-exported from there under its old name, so every call site below reads unchanged.
 */

/** One priced line of a note d'honoraires document. Strings, because they are what the inputs hold. */
type HonorairesActLine = {
  designation: string
  quantity: string
  unitPrice: string
  /** The price shown came from the catalogue, not the keyboard — so picking another act may replace it. */
  pricedFromCatalog: boolean
}

/**
 * A line's total, and the document's — the browser half of `HonorairesContent` on the server.
 *
 * <p>⚠️ The server is the authority: it recomputes both from the stored quantities and prices at render time, so
 * the printed sheet cannot disagree with its own lines. This exists only so the A4 on screen shows the figure
 * before the save, and it must read a price the same three ways round the server does — a JSON number,
 * « 75.500 », and the Tunisian « 75,500 » these very inputs produce.</p>
 */
function honorairesAmount(raw: string): number {
  const parsed = Number(String(raw ?? "").replace(/\s/g, "").replace(",", "."))
  return Number.isFinite(parsed) ? parsed : 0
}

/** Quantité defaults to 1, not 0: an unparsable one must never silently zero a priced act. */
function honorairesQuantity(raw: string): number {
  const text = String(raw ?? "").trim()
  if (text === "") return 1
  const parsed = Number(text.replace(/\s/g, "").replace(",", "."))
  return Number.isFinite(parsed) ? parsed : 1
}

/** The named lines only — a line with no désignation bills nothing and prints as an empty priced row. */
function honorairesNamedLines(acts: HonorairesActLine[]): HonorairesActLine[] {
  return acts.filter((act) => act.designation.trim() !== "")
}

/**
 * What actually goes into `ContentJson` — the three fields `HonorairesContent` reads, and not `pricedFromCatalog`,
 * which is a fact about this form's session rather than about the document.
 */
function honorairesContentActs(acts: HonorairesActLine[]) {
  return honorairesNamedLines(acts).map(({ designation, quantity, unitPrice }) => ({
    designation,
    quantity,
    unitPrice,
  }))
}

function honorairesLineTotal(act: HonorairesActLine): number {
  return honorairesQuantity(act.quantity) * honorairesAmount(act.unitPrice)
}

function honorairesTotal(acts: HonorairesActLine[]): number {
  return honorairesNamedLines(acts).reduce((sum, act) => sum + honorairesLineTotal(act), 0)
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

  // Name = brand + form only; the strength goes to the Dosage field (not crammed into « Nom du médicament »).
  const pick = (m: MedicationDto) =>
    onUpdate({
      ...medication,
      name: [m.brandName, m.form].filter(Boolean).join(" "),
      dosage: m.strength,
      medicationId: m.id,
      dci: m.dcis,
    })

  /**
   * What the NAME field proposes as it is typed — the same catalogue as the loupe beside it, reached without
   * opening anything. Suggestions, never a closed list: the field stays free text, and a médicament the
   * catalogue has never heard of is prescribed exactly as typed.
   */
  const nameSuggestions = useMemo(() => {
    const typed = medication.name?.trim() ?? ""
    if (medication.medicationId || typed.length < 2) return []
    return matchMedications(catalog, typed, 5).filter((m) => medicationCatalogLabel(m) !== typed)
  }, [catalog, medication.medicationId, medication.name])

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
                                  pick(m)
                                  setLookupOpen(false)
                                }}
                              >
                                <div className="flex flex-col">
                                  <span className="text-sm font-medium">{medicationCatalogLabel(m)}</span>
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
            {nameSuggestions.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {nameSuggestions.map((m) => (
                  <Button
                    key={m.id}
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => pick(m)}
                    // Its own box rather than a touch-target overlay: these chips sit in a wrapping row.
                    className="h-auto min-h-8 whitespace-normal px-2 py-1 text-xs coarse:min-h-11"
                  >
                    {medicationCatalogLabel(m)}
                  </Button>
                ))}
              </div>
            )}
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
              <Label className="text-xs text-muted-foreground min-h-4">Dose par prise</Label>
              <Input
                type="text"
                placeholder="Ex : 1 comprimé"
                value={medication.dose || ""}
                onChange={(e) => {
                  onUpdate({ ...medication, dose: e.target.value })
                }}
                className="h-10 w-full"
              />
            </div>
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
          </div>
          {/* Durée + son unité — un traitement de fond se compte en MOIS, pas en 180 jours. */}
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Durée</Label>
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
            <div className="flex flex-col gap-2">
              <Label className="text-xs text-muted-foreground min-h-4">Unité de durée</Label>
              <select
                value={durationUnitOf(medication.durationUnit)}
                onChange={(e) => {
                  onUpdate({ ...medication, durationUnit: e.target.value })
                }}
                className="h-10 w-full min-w-0 rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value={DURATION_UNITS.jours}>jours</option>
                <option value={DURATION_UNITS.mois}>mois</option>
              </select>
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
    /*
     * `patientSex` / `patientWeightKg` were here and are gone. A Tunisian dental ordonnance does not carry
     * them: « Sexe » was prefilled from the patient record and so printed on every ordonnance ever issued,
     * and « Poids » was optional and nearly always blank. The tombstone that matters is in
     * `DocumentIdentity.PatientLines` — read it before putting them back on the strength of
     * `ordonnance-certificat-norms`' spec, which is otherwise still accurate.
     *
     * Legacy documents keep both keys in their ContentJson; nothing reads them any more.
     */
    doctorOrderNumber: "", // CNOMDT ordre for the letterhead (FR-2.5) — profile-filled, never typed, never on a certificat
    startDate: "", // Certificat: repos médical start date
    // Certificat: « jours » | « mois ». Shared vocabulary with an ordonnance's durée — see `DURATION_UNITS`.
    // Widened to `string`: a stored document's value arrives as one, and `durationUnitOf` is the reader.
    durationUnit: DURATION_UNITS.jours as string,
    objetMotif: "", // Certificat: free objet/motif body (FR-2.1)
    // Liaison — external confrère destinataire (free text) + the norm sections, ALL optional, the
    // destinataire included: the letter is a blank letterhead and nothing writes these any more. Kept in
    // state so a letter saved with them keeps them (décret n° 2016-995 + HAS).
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
    /*
     * Note d'honoraires — the priced lines and the sentence under them.
     *
     * ⚠️ This is a DOCUMENT, not an `Invoice`. It mints no number, enters no balance and reaches no money read:
     * saving one moves nothing in « Solde patient », « Créances », la caisse or the dashboard. The fiscal note —
     * the numbered one every one of those sums — is raised in the Factures module, and the two must never be
     * conflated. `HonorairesContent` on the server is the authority for the arithmetic; what is here computes the
     * same sum for the on-screen A4, and that is the only place the two may both exist.
     */
    honorairesActs: [] as HonorairesActLine[],
    honorairesNote: "",
  })

  const [medicationCatalog, setMedicationCatalog] = useState<MedicationDto[]>([])

  /*
   * ── Why each of the reads below carries a `…Failed` flag AND a reload counter (defect #1) ─────────────────
   *
   * They used to swallow their error into an empty array. On a clinical picker that is not a graceful
   * degradation, it is a **wrong answer**: an empty list asserts « ce catalogue est vide », the practitioner
   * concludes it was never configured, and their next move is to type the médicament by hand — which silently
   * discards the dosage defaults and the DCI snapshot the catalogue entry exists to supply. The document is
   * then saved and printed with less data than the software had.
   *
   * The reload counter rather than a `useCallback` loader: the reads already live in effects with a `cancelled`
   * guard, and bumping a dependency reuses that guard for the retry instead of writing a second code path that
   * can race the first one.
   */
  const [medicationCatalogFailed, setMedicationCatalogFailed] = useState(false)
  const [medicationCatalogReload, setMedicationCatalogReload] = useState(0)
  /*
   * The clinic's OWN act catalogue, for the note d'honoraires' lines — `procedureTypesApi`: what a fee note
   * bills is the practice's own act at the practice's own tarif.
   */
  const [procedureCatalog, setProcedureCatalog] = useState<ProcedureTypeDto[]>([])
  const [procedureCatalogFailed, setProcedureCatalogFailed] = useState(false)
  const [procedureCatalogReload, setProcedureCatalogReload] = useState(0)
  const [actPickerOpenIndex, setActPickerOpenIndex] = useState<number | null>(null)
  /** « Reprendre des actes réalisés » — the note d'honoraires' second act source. */
  const [billableActsOpen, setBillableActsOpen] = useState(false)
  /** « Voir le document » — the saved sheet, read in place rather than navigated to. */
  const [savedPreviewOpen, setSavedPreviewOpen] = useState(false)
  // `reposOpen` / `liaisonExtrasOpen` are gone with the two folds they drove: the repos fields ARE the
  // certificat now, and the liaison's guided sections are no longer offered.

  // `documentRef` is gone with the DOM-clone print: nothing reads the A4 block's subtree any more, and
  // leaving a handle on it is an invitation to render this legal document a second way. See `handlePrint`.

  // Load clinic and doctor info
  const { doctors, allDoctors, currentUserDoctor } = useDoctors()
  
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

  // FR-2.5: the CNOMDT ordre from the current doctor's profile, for the A4 preview's letterhead — never
  // retyped. Only fills when empty, so a legacy document's stored ordre is kept. Re-runs on `documentId` too:
  // loading a document sets it to "" (possibly after the doctor already resolved), so depending on the id
  // re-applies the profile fallback instead of leaving the preview's letterhead a line short of the PDF's.
  // ⚠️ The certificat does not print it at all — see `DocumentIdentity.CarriesOrdreNumber`.
  useEffect(() => {
    const ordre = currentUserDoctor?.ordreNumberCnomdt
    if (ordre) {
      setFormFields((prev) => (prev.doctorOrderNumber ? prev : { ...prev, doctorOrderNumber: ordre }))
    }
  }, [currentUserDoctor, documentId])

  /*
   * ── K3: the treating practitioner is chosen, never guessed ──────────────────────────────────────────────────
   *
   * This was `currentUserDoctor || doctors[0]` — a silent fall-back to whoever happens to be first in the roster
   * whenever the logged-in user has no linked `Doctor`, which a secretary never has. On a bulletin de soins that is
   * not a cosmetic default: `doctorCodeProfessionnel` (the code conventionnel `StampActs` prints on **every** act
   * row) came from that guess, so a secretary filing a bulletin attributed the acts to the wrong practitioner, with
   * nothing on screen naming anyone. There was no `setSelectedDoctor` in this file at all.
   *
   * The selection is now explicit state with a *defaulting* effect below. ⚠️ The two official CNAM forms went
   * further — they had no fall-back at all, and nothing selected was a refusal at Save — but they are withdrawn
   * (`features/cnam-ui-withdrawal/notes.md`), so what is left here is the defaulting effect alone.
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

  // A reopened document keeps its prescriber even once retired (I3) — the roster is only for the default.
  const chosenDoctor = allDoctors.find((d) => d.id === selectedDoctorId) ?? null

  /*
   * ⚠️ The `doctors[0]` fall-back is **gone**, for every document type — the narrow scoping K3 left in place no
   * longer holds. K3 kept it for the four free-form documents on the grounds that removing it would change what
   * they print as a side effect of a fix to the official CNAM forms, whose wrong name only cost a rejected claim.
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
            startDate: content.startDate || "",
            durationUnit: durationUnitOf(content.durationUnit),
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
            // Stored as strings, and read back as strings: a number would print « 75 » where the practitioner
            // typed « 75,000 », and the server parses both anyway.
            honorairesActs: Array.isArray(content.acts)
              ? content.acts.map((act: Partial<HonorairesActLine>) => ({
                  designation: String(act?.designation ?? ""),
                  quantity: String(act?.quantity ?? "1"),
                  unitPrice: String(act?.unitPrice ?? ""),
                  // A stored price is a decision already taken — picking an act must not overwrite it.
                  pricedFromCatalog: false,
                }))
              : [],
            honorairesNote: content.note || "",
          })

          // Nothing to expand any more: the certificat's repos fields and the liaison's free text are both
          // unfolded, and the liaison's guided sections have no controls to open.

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

  // The clinic's own acts, for the note d'honoraires' line picker.
  useEffect(() => {
    if (documentType !== "honoraires") return
    let cancelled = false
    ;(async () => {
      try {
        const acts = await procedureTypesApi.list()
        if (!cancelled) {
          setProcedureCatalog(acts)
          setProcedureCatalogFailed(false)
        }
      } catch {
        if (!cancelled) {
          setProcedureCatalog([])
          setProcedureCatalogFailed(true)
        }
      }
    })()
    return () => { cancelled = true }
  }, [documentType, procedureCatalogReload])

  const resetForm = () => {
    setSelectedPatient("")
    setDocumentId(null)
    setFormFields({
      date: todayLocalIso(),
      medications: [],
      content: "",
      duration: "",
      doctorOrderNumber: "",
      startDate: "",
      durationUnit: DURATION_UNITS.jours,
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
      honorairesActs: [],
      honorairesNote: "",
    })
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

  // Certificat médical (FR-2) — the single source of truth for the body text, shared by the read-only
  // preview and the Word export (the PDF is rendered server-side by CertificatTextBuilder with the same
  // shape). objet/motif is the primary body; the repos médical clause is rendered only when a duration is set.
  const formatFrDate = (value?: string) =>
    value
      ? new Date(value).toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit", year: "numeric" })
      : ""

  /**
   * The certificat's body, as bold-aware runs. Mirrors the server's `CertificatTextBuilder`, which renders the
   * PDF — one sentence, the repos clause included, and nothing else.
   *
   * ⚠️ Four facts are bold and the list is exactly what a reader must find: the patient's name, the count, its
   * unit and the start date. The certificat carries **no patient identity block**, so the bold name in the
   * sentence is the only place it appears.
   */
  const certificatBodyParagraphs = (): { text: string; bold?: boolean }[][] => {
    const patientName = patientData ? getPatientName(patientData) : "[Nom du patient]"
    const civility = patientCivility(patientData?.gender)

    const sentence: { text: string; bold?: boolean }[] = [
      { text: `Je soussigné(e) Docteur ${formData.doctorName}, certifie avoir examiné ce jour ${civility} ` },
      { text: patientName, bold: true },
    ]
    const duration = formFields.duration?.trim()
    if (duration) {
      const unit = durationUnitLabel(formFields.durationUnit, duration)
      sentence.push({ text: ", et atteste que son état de santé nécessite un repos de " })
      sentence.push({ text: `${duration} ${unit}`, bold: true })
      if (formFields.startDate) {
        sentence.push({ text: " à compter du " })
        sentence.push({ text: formatFrDate(formFields.startDate), bold: true })
      }
      sentence.push({ text: ", sauf complications" })
    }
    sentence.push({ text: "." })

    const paras = [sentence]
    // Legacy only — the objet/motif field is gone from the form, but a certificat already issued with one
    // must not lose the paragraph when it is re-rendered.
    if (formFields.objetMotif && formFields.objetMotif.trim()) {
      paras.push([{ text: formFields.objetMotif.trim() }])
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
      content.startDate = formFields.startDate || "";
      content.duration = formFields.duration || "";
      content.durationUnit = durationUnitOf(formFields.durationUnit);
      content.patientCivility = patientCivility(patientData?.gender);
    } else if (documentType === "honoraires") {
      // The array goes in as an array; `contentStrings` below is what re-serialises it. It used to be sent
      // raw and that was the whole of the « Ces champs ne sont pas valides » refusal — see below.
      content.acts = honorairesContentActs(formFields.honorairesActs);
      content.note = formFields.honorairesNote || "";
    }

    /*
     * ⚠️ **`MedicalDocumentPdfData.Content` is a `Dictionary<string, string>` on the wire, so EVERY value
     * here has to be a string — and an array has to be a JSON string.** This mirrors the server's own
     * `MedicalDocumentPdfMapping.FlattenContent`, whose summary states the contract: « a non-string node (the
     * medications / acts arrays) is re-serialized rather than dropped, because the renderer parses those back
     * out of the string. »
     *
     * It is enforced HERE, once, rather than trusted to each branch, because trusting the branches is exactly
     * what failed: `prescription` stringified its medications and the bulletin's builder its acts, while
     * `honoraires` sent a raw array — so `POST /medical-documents/generate-pdf-download` refused to bind the
     * body and answered « Ces champs ne sont pas valides ou n'ont pas été envoyés : documentData, acts ».
     * Both « Télécharger PDF » and « Imprimer » were dead on every note d'honoraires.
     *
     * ⚠️ The `typeof v === "string"` passthrough is load-bearing: stringifying an already-serialised value
     * would double-encode it, and the renderer would parse a string where it expects a list.
     */
    const contentStrings: Record<string, string> = {};
    for (const [key, value] of Object.entries(content)) {
      contentStrings[key] =
        typeof value === "string" ? value : value == null ? "" : JSON.stringify(value);
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
      // `contentStrings`, never `content` — see the contract note above.
      content: contentStrings,
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
   * Every remaining document type is free-form, so all of them support a Word export.
   *
   * ⚠️ It used to be `!isOfficialForm`: the two CNAM overlays had none, because the deliverable was a stamp onto
   * a pre-printed form and a `.docx` could only be the letterhead with none of the form on it. The K-series
   * defect was exactly that — the branch chain had no `bulletin-cnam` case and no `else`, so pressing the button
   * produced a letterhead-only file **and** a success toast. **Keep the constant**: it is the seam a new
   * stamped-form type would reuse instead of repeating that.
   */
  const wordExportSupported = true

  /**
   * ⚠️ **« Télécharger Word » is WITHDRAWN from the whole app, on purpose and for now.** The owner's call:
   * « let's remove all download word buttons from app, just hide, we'll return to it later if we need to ».
   * A `.docx` is a SECOND renderer of a legal document — hand-built paragraph by paragraph here while the PDF
   * comes from the server — so the two drift, and only the PDF is the paper a pharmacist or a caisse reads.
   * Everything below it (`generateWord`, the `docx` import, every branch) is left standing so putting the
   * button back is one flag, not a rewrite.
   */
  const WORD_EXPORT_OFFERED = false
  const wordExportOffered = wordExportSupported && WORD_EXPORT_OFFERED

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

      // No « À l'attention de » block: a lettre de liaison is a blank letterhead the practitioner writes on,
      // and the confrère is addressed in the prose. Mirrors the PDF renderer.

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

      // Patient info — withheld on the two types that name their own patient in the prose (see the PDF
      // renderer): the lettre de liaison and the certificat.
      if (documentType !== "liaison" && documentType !== "certificat") {
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
      }
      paragraphs.push(new Paragraph({ text: "" }));

      // Document-specific content
      if (documentType === "prescription") {
        if (Array.isArray(formFields.medications) && formFields.medications.length > 0) {
          // « 1/ Augmentin (1 g) » then the posologie indented under it, both underlined, then a rule closing
          // the list — the same three rules the PDF renderer applies. See `prescriptionLineParts`.
          formFields.medications.forEach((med, index) => {
            const printed = prescriptionLineParts(med);
            paragraphs.push(
              new Paragraph({
                children: [new TextRun({ text: `${index + 1}/ ${printed.heading}`, underline: {} })],
              })
            );
            // Plain — the underline stops at the médicament. See `prescriptionLineParts`.
            if (printed.posology) {
              paragraphs.push(new Paragraph({ indent: { left: 720 }, text: printed.posology }));
            }
            if (printed.details) {
              paragraphs.push(new Paragraph({ indent: { left: 720 }, text: printed.details }));
            }
            paragraphs.push(new Paragraph({ text: "" }));
          });
          paragraphs.push(
            new Paragraph({
              text: "",
              border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: "000000", space: 1 } },
            })
          );
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
      } else if (documentType === "certificat") {
        // Mirrors the PDF renderer: one sentence, and nothing under it.
        certificatBodyParagraphs().forEach((runs) =>
          paragraphs.push(
            new Paragraph({
              children: runs.map((run) => new TextRun({ text: run.text, bold: run.bold })),
            })
          )
        );
      } else if (documentType === "honoraires") {
        const lines = honorairesNamedLines(formFields.honorairesActs);
        if (lines.length === 0) {
          paragraphs.push(new Paragraph({ text: "Aucun acte" }));
        } else {
          lines.forEach((act) => {
            paragraphs.push(
              new Paragraph({
                text: `${act.designation} — ${honorairesQuantity(act.quantity)} × ${formatDT(honorairesAmount(act.unitPrice))} = ${formatDT(honorairesLineTotal(act))}`,
              })
            );
          });
          paragraphs.push(
            new Paragraph({
              alignment: AlignmentType.RIGHT,
              children: [
                new TextRun({ text: `Total : ${formatDT(honorairesTotal(formFields.honorairesActs))}`, bold: true }),
              ],
            })
          );
        }
        if (formFields.honorairesNote.trim()) {
          paragraphs.push(new Paragraph({ text: "" }), new Paragraph({ text: formFields.honorairesNote.trim() }));
        }
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
   * Print the document — **the bytes the server renders, never a clone of the A4 block on screen**.
   *
   * <p>It used to `window.open('')` and write in a copy of `documentRef`'s subtree plus
   * `document.querySelector('style')?.textContent`. That selector is the page's FIRST `<style>` element, which
   * in a Tailwind v4 app is not the stylesheet — so the print preview arrived essentially unstyled: no
   * margins, a collapsed table, labels at body size, everything against the left edge. Reported with a
   * screenshot of `about:blank`.</p>
   *
   * <p>⚠️ It was also a second renderer of a legal document, which `DocumentPreviewDialog` already refuses
   * for the reason that applies here twice over: what a patient is handed must be what the e-mail attaches and
   * what the PDF job stores.</p>
   *
   * <p>⚠️ **It prints the form as it stands, not the last saved version** — the same
   * `buildDocumentData()` that « Télécharger PDF » directly beside it sends. Printing the stored document
   * instead would silently print a stale sheet after an edit, and would make « Imprimer » unavailable before
   * the first save, which the DOM path did support.</p>
   */
  const handlePrint = async () => {
    if (saving) {
      return; // Prevent action while saving
    }

    if (!patientData) {
      toast.error("Patient requis", {
        description: "Sélectionnez un patient avant d'imprimer le document.",
        duration: 3000,
      });
      return;
    }

    const loadingToast = toast.loading("Préparation de l'impression…", {
      description: "Le document se prépare.",
    });

    try {
      const documentData = buildDocumentData();
      if (!documentData) {
        toast.dismiss(loadingToast);
        toast.error("Données manquantes", {
          description: "Impossible de préparer le document. Vérifiez que tous les champs obligatoires sont remplis.",
          duration: 4000,
        });
        return;
      }

      const pdfBlob = await medicalDocumentsApi.generatePdfForDownload(documentData);
      /*
       * ⚠️ Dismissed BEFORE printing, not after. `print()` blocks until the dialog is closed, so awaiting
       * it first left « Préparation de l'impression… » spinning behind an open print dialog — telling the
       * user something is still being prepared while it is plainly in front of them, and clearing only once
       * they had finished. The wait this toast describes ends when the bytes arrive.
       */
      toast.dismiss(loadingToast);
      const outcome = await printPdfBlob(pdfBlob, buildPdfFileName());

      // Said only where it is true: on a coarse pointer nothing was printed, the file was handed to the OS
      // viewer, and telling somebody « impression lancée » there would be a claim about a dialog they never saw.
      if (outcome === "delivered") {
        toast.info("Document ouvert", {
          description: "Utilisez l'impression de la visionneuse de votre appareil.",
          duration: 5000,
        });
      }
    } catch (error) {
      console.error("Error in handlePrint:", error);
      toast.dismiss(loadingToast);
      const errorMessage = error instanceof ApiError ? error.message : "Une erreur est survenue";
      toast.error("Impossible d'imprimer", {
        description: errorMessage,
        duration: 4000,
      });
    }
  };

  /**
   * Put the picked recorded acts onto the fee note.
   *
   * ⚠️ **Appends, never replaces**, and drops a blank trailing line rather than leaving a hole: lines
   * already typed are the user's work and a picker that cleared them would lose it off screen. The prices are
   * the server's (`DentalRecordInvoiceLines`), so `pricedFromCatalog` is **false** — this figure is what was
   * charged for this patient, not a tarif a later act pick may overwrite.
   *
   * ⚠️ `formatAmount`, not `String(n)`: these strings go into the money inputs, which the rest of the
   * product writes and reads in Tunisian millimes.
   */
  const addBillableActs = (lines: BillableActLine[]) => {
    if (lines.length === 0) return
    setFormFields((prev) => {
      const kept = prev.honorairesActs.filter(
        (act) => act.designation.trim() !== "" || act.unitPrice.trim() !== "",
      )
      return {
        ...prev,
        honorairesActs: [
          ...kept,
          ...lines.map((line) => ({
            designation: line.designation,
            quantity: String(line.quantity),
            unitPrice: formatAmount(line.unitPriceHt),
            pricedFromCatalog: false,
          })),
        ],
      }
    })
  }

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

    // No « destinataire requis » refusal any more: a lettre de liaison has no recipient field to fill, so the
    // only thing that guard could do is refuse every letter this editor can now write.

    setSaving(true)
    try {
      // Build content JSON from form fields
      const content: Record<string, any> = {
        date: formFields.date,
      }

      if (documentType === "prescription") {
        content.medications = formFields.medications // Array will be serialized as JSON
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
        // No `doctorOrderNumber`: a certificat's letterhead no longer prints one. A legacy certificat keeps
        // its stored key untouched — nothing reads it for this type any more.
        content.startDate = formFields.startDate
        content.duration = formFields.duration
        content.durationUnit = durationUnitOf(formFields.durationUnit)
        // « M. » / « Mme » / « M./Mme » — snapshotted, so the background-job PDF prints the same civilité the
        // download path does without reading the patient row. See `patientCivility` for why it travels in the
        // document's own content and not as a field on the PDF model.
        content.patientCivility = patientCivility(patientData?.gender)
      } else if (documentType === "honoraires") {
        // The keys `HonorairesContent` reads. No total is stored: the server sums the lines at render time, so
        // a figure on the paper cannot disagree with the rows above it.
        content.acts = honorairesContentActs(formFields.honorairesActs)
        content.note = formFields.honorairesNote
      }

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
      case "honoraires":
        return "Note d'honoraires"
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
              THE PATIENT'S ALERTS, on the types that PRESCRIBE — see `DOCUMENT_TYPES_WITH_MEDICAL_ALERTS`,
              which is the one owner of that list and carries the reversal it represents.

              ⚠️ This editor is where an **ordonnance** is written, and nothing on it read `patient.allergies`. The
              medication picker offers Clamoxyl and Augmentin, both of which carry `Amoxicilline` as a structured
              DCI in the seeded catalogue, and prescribing either to a penicillin-allergic patient raised nothing at
              all — the only allergy field in the whole editor was the liaison letter's empty textarea, which the
              prescriber was expected to retype from the patient's file in another tab.
              Read-only and outside the form: this is the patient's record, corrected in the patient's file. It does
              **not** block — a real DCI-vs-allergy check needs structured allergies (out of scope) — it makes the
              fact visible at the moment the decision is taken.
            */}
            {patientData && showsMedicalAlerts(documentType) && <PatientAlertPanel patient={patientData} />}

            {/*
              The liaison's « Confrère destinataire » fieldset (nom, spécialité, adresse, e-mail) is gone with
              the « À l'attention de » block it fed. A lettre de liaison is now a blank letterhead: entête,
              date, titre, texte libre — the practitioner addresses the confrère in their own words and types
              the address in « Envoyer par e-mail ». `recipientName` / `recipientSpecialty` /
              `recipientAddress` / `recipientEmail` stay in `formFields` so a letter saved with them keeps
              them; nothing writes them any more.
            */}

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
                {/* ⚠️ `flex-wrap`: the button is `whitespace-nowrap shrink-0`, so at 320 px this row measured
                    295 px of min-content in a 273 px box and pushed the whole editor pane sideways. */}
                <div className="flex flex-wrap items-center justify-between gap-2">
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

              </div>
            )}

            {/*
              Note d'honoraires — a printable sheet, and NOTHING it does touches the money path.

              ⚠️ Saving this creates a `MedicalDocument`, never an `Invoice`: no number is minted, no balance
              moves, la caisse does not see it. The numbered, fiscal note — the one « Solde patient »,
              « Créances », la caisse and the dashboard all sum — is raised in the Factures module, and the
              documents module minting one is exactly the defect that got this type withdrawn once already.
            */}
            {documentType === "honoraires" && (
              /* ⚠️ `@container`, and the act rows below hinge on it rather than on the VIEWPORT — which is
                 what made them unusable. This panel is `xl:w-[420px]` with `md:p-8`, so from 1280 px up its
                 content box is **356 px** while `sm:` has been true since 640: the four-column row then
                 measured auto (36) + 7rem + 5rem + gaps, leaving ~104 px for the désignation cell — an input
                 of about 64 px beside its catalogue button. A desktop was narrower here than a phone, where the
                 panel is full width. */
              <div className="@container space-y-4">
                <div className="space-y-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <Label className="text-sm font-semibold text-foreground">Actes</Label>
                    {/*
                      « Reprendre des actes réalisés » — the patient's own recorded work, beside the per-line
                      catalogue picker rather than instead of it. The catalogue answers « what does a
                      détartrage cost? »; this answers « what did we do for this patient, and for how much? »,
                      which is the question a fee note is raised to answer. Disabled with no patient because
                      there is no record to read — and it says so rather than being absent.
                    */}
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="coarse:h-11"
                      disabled={!selectedPatient}
                      title={selectedPatient ? undefined : "Sélectionnez d'abord un patient."}
                      onClick={() => setBillableActsOpen(true)}
                    >
                      <ClipboardList className="mr-2 h-4 w-4" />
                      Reprendre des actes réalisés
                    </Button>
                  </div>
                  {formFields.honorairesActs.length === 0 && (
                    <p className="text-xs text-muted-foreground">
                      Aucun acte pour l&apos;instant — ajoutez la première ligne, ou reprenez les soins déjà
                      enregistrés pour ce patient.
                    </p>
                  )}
                  {formFields.honorairesActs.map((act, index) => {
                    const update = (patch: Partial<HonorairesActLine>) =>
                      setFormFields((prev) => ({
                        ...prev,
                        honorairesActs: prev.honorairesActs.map((row, i) =>
                          i === index ? { ...row, ...patch } : row,
                        ),
                      }))
                    return (
                      // One column until the SECTION is 32rem wide, four above it — see the `@container` note
                      // on the wrapper. `@lg` rather than `@md` is measured: at 448 px the four tracks leave the
                      // désignation input ~156 px once its catalogue button is taken out, which is a field you
                      // cannot read an act name in.
                      <div key={index} className="grid grid-cols-1 gap-2 @lg:grid-cols-[1fr_5rem_7rem_auto]">
                        {/* The clinic's own act, or free text. Both, deliberately: the catalogue carries the
                            tarif so a fee note stops being retyped from memory, and a one-off still has to be
                            writable. A Popover is right here — this is a page, not a dialog, so nothing else
                            has a claim on Enter (see `record/act-catalog-picker.tsx` for the dialog case). */}
                        <div className="flex min-w-0 gap-1">
                          <Input
                            aria-label={`Désignation de l'acte ${index + 1}`}
                            placeholder="Ex. Détartrage"
                            value={act.designation}
                            onChange={(e) => update({ designation: e.target.value, pricedFromCatalog: false })}
                            className="min-w-0 flex-1 md:text-sm"
                          />
                          <Popover
                            open={actPickerOpenIndex === index}
                            onOpenChange={(open) => setActPickerOpenIndex(open ? index : null)}
                            modal
                          >
                            <PopoverTrigger asChild>
                              <Button
                                type="button"
                                variant="outline"
                                size="icon"
                                className="shrink-0 coarse:size-11"
                                aria-label={`Choisir un acte du catalogue pour la ligne ${index + 1}`}
                              >
                                <Search className="h-4 w-4" />
                              </Button>
                            </PopoverTrigger>
                            <PopoverContent align="start" className="w-[min(22rem,calc(100vw-2rem))] p-0">
                              {procedureCatalogFailed ? (
                                <div className="p-3">
                                  <CatalogLoadFailed
                                    label="Le catalogue des actes"
                                    onRetry={() => setProcedureCatalogReload((n) => n + 1)}
                                  />
                                </div>
                              ) : (
                                <Command>
                                  <CommandInput placeholder="Rechercher un acte…" />
                                  <CommandList>
                                    <CommandEmpty>Aucun acte ne correspond.</CommandEmpty>
                                    <CommandGroup>
                                      {procedureCatalog.map((procedure) => (
                                        <CommandItem
                                          key={procedure.id}
                                          value={procedure.name}
                                          onSelect={() => {
                                            // The tarif fills the price only when the field is empty or still
                                            // holds a catalogue figure — a fee the user typed is theirs and
                                            // survives picking the act it belongs to.
                                            const tarif = procedure.defaultCost
                                            const takeTarif =
                                              tarif != null && (act.unitPrice.trim() === "" || act.pricedFromCatalog)
                                            update({
                                              designation: procedure.name,
                                              unitPrice: takeTarif ? formatAmount(tarif) : act.unitPrice,
                                              pricedFromCatalog: takeTarif,
                                            })
                                            setActPickerOpenIndex(null)
                                          }}
                                        >
                                          <span className="min-w-0 flex-1 truncate">{procedure.name}</span>
                                          {procedure.defaultCost != null && (
                                            <span className="ms-2 shrink-0 text-xs tabular-nums text-muted-foreground">
                                              {formatDT(procedure.defaultCost)}
                                            </span>
                                          )}
                                        </CommandItem>
                                      ))}
                                    </CommandGroup>
                                  </CommandList>
                                </Command>
                              )}
                            </PopoverContent>
                          </Popover>
                        </div>
                        {/* Stacked, the two number fields are side by side with nothing naming them, and a
                            placeholder disappears the moment a value is in — so each carries a visible inline
                            label at that width (`invoice-form-modal`'s « Qté » shape). Once the row forms, the
                            four columns read as a row and the label would eat a 5 rem cell. */}
                        <div className="flex items-center gap-1.5">
                          <span className="shrink-0 text-xs text-muted-foreground @lg:hidden">Qté</span>
                          <Input
                            aria-label={`Quantité de l'acte ${index + 1}`}
                            inputMode="decimal"
                            placeholder="Qté"
                            value={act.quantity}
                            onChange={(e) => update({ quantity: e.target.value })}
                            className="min-w-0 flex-1 md:text-sm"
                          />
                        </div>
                        <div className="flex items-center gap-1.5">
                          <span className="shrink-0 text-xs text-muted-foreground @lg:hidden">P.U. (DT)</span>
                          <Input
                            aria-label={`Prix unitaire de l'acte ${index + 1} (DT)`}
                            inputMode="decimal"
                            placeholder="P.U. (DT)"
                            value={act.unitPrice}
                            onChange={(e) => update({ unitPrice: e.target.value, pricedFromCatalog: false })}
                            className="min-w-0 flex-1 md:text-sm"
                          />
                        </div>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="coarse:size-11"
                          aria-label={`Retirer l'acte ${index + 1}`}
                          onClick={() =>
                            setFormFields((prev) => ({
                              ...prev,
                              honorairesActs: prev.honorairesActs.filter((_, i) => i !== index),
                            }))
                          }
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    )
                  })}
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="w-full coarse:h-11"
                    onClick={() =>
                      setFormFields((prev) => ({
                        ...prev,
                        honorairesActs: [
                          ...prev.honorairesActs,
                          { designation: "", quantity: "1", unitPrice: "", pricedFromCatalog: false },
                        ],
                      }))
                    }
                  >
                    <Plus className="mr-2 h-4 w-4" />
                    Ajouter un acte
                  </Button>
                  <p className="text-right text-sm font-semibold tabular-nums">
                    Total : {formatDT(honorairesTotal(formFields.honorairesActs))}
                  </p>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="honorairesNote" className="text-sm font-semibold text-foreground">
                    Mention (facultatif)
                  </Label>
                  <Textarea
                    id="honorairesNote"
                    placeholder="Ex. Règlement à réception."
                    value={formFields.honorairesNote}
                    onChange={(e) => setFormFields({ ...formFields, honorairesNote: e.target.value })}
                    className="min-h-[80px]"
                  />
                </div>

                <p className="text-xs text-muted-foreground">
                  Ce document s&apos;imprime et se télécharge comme les autres. Il n&apos;entre ni dans la caisse
                  ni dans les factures — pour une facture numérotée, passez par le module Factures.
                </p>
              </div>
            )}

            {/*
              Lettre de liaison — ONE free-text field, and no guidance at all.

              ⚠️ The ten guided norm sections (motif, examen clinique, examen radiologique, actes réalisés,
              traitement en cours, prescriptions, examens en attente, consignes de suivi, pièces jointes,
              médecin traitant) are gone from this form, deliberately: the letter is a blank sheet with the
              cabinet's letterhead, its date and its title, and a practitioner writes on it. Their keys stay in
              `formFields` and are still saved and still rendered, so a letter already written with them keeps
              every word — nothing new writes one. `LiaisonContent` (the PDF) says the same thing on its side.
            */}
            {documentType === "liaison" && (
              <div className="space-y-2">
                <Label htmlFor="liaisonBody" className="text-sm font-semibold text-foreground">
                  Corps de la lettre
                </Label>
                <Textarea
                  id="liaisonBody"
                  placeholder={"Cher Confrère,\n\nJe vous adresse ce patient pour…"}
                  value={formFields.content}
                  onChange={(e) => setFormFields({ ...formFields, content: e.target.value })}
                  className="min-h-[420px]"
                />
              </div>
            )}


            {/*
              Certificat médical — le repos EST le certificat.

              ⚠️ « Objet / motif du certificat » is gone: the document is one sentence (see
              `CertificatTextBuilder`), so a free body had nowhere to print. `objetMotif` stays in `formFields`
              and is still saved and still rendered, so a certificat already issued with one keeps its
              paragraph. ⚠️ The repos fields are no longer folded behind a `<details>` — they were « facultatif »
              when the certificate had another body, and they are now the only thing on it.
            */}
            {documentType === "certificat" && (
              <>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="duration" className="text-sm font-semibold text-foreground">
                      Durée du repos
                    </Label>
                    {/* Le compte et son unité sur une ligne : « 5 jours » est une seule réponse. */}
                    <div className="flex gap-2">
                      <Input
                        id="duration"
                        type="number"
                        min="1"
                        placeholder="Ex : 3"
                        value={formFields.duration}
                        onChange={(e) => setFormFields({ ...formFields, duration: e.target.value })}
                        className="h-11 min-w-0 flex-1 md:text-sm"
                      />
                      <select
                        aria-label="Unité de la durée du repos"
                        value={durationUnitOf(formFields.durationUnit)}
                        onChange={(e) => setFormFields({ ...formFields, durationUnit: e.target.value })}
                        className="h-11 shrink-0 rounded-md border border-input bg-background px-3 text-base md:text-sm"
                      >
                        <option value={DURATION_UNITS.jours}>jours</option>
                        <option value={DURATION_UNITS.mois}>mois</option>
                      </select>
                    </div>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="startDate" className="text-sm font-semibold text-foreground">
                      À compter du
                    </Label>
                    <Input
                      id="startDate"
                      type="date"
                      value={formFields.startDate}
                      onChange={(e) => setFormFields({ ...formFields, startDate: e.target.value })}
                      className="h-11 md:text-sm"
                    />
                  </div>
                </div>
                <p className="text-xs text-muted-foreground">
                  Laissez la durée vide pour un certificat sans repos : la phrase s&apos;arrête alors après
                  «&nbsp;certifie avoir examiné ce jour&nbsp;».
                </p>

                {/* ⚠️ No « Numéro d'ordre (CNOMDT) » field here, on the practice owner's instruction — a
                    certificat is one signed, stamped sentence and the cachet already identifies its author.
                    `DocumentIdentity.CarriesOrdreNumber` is the render half; the two move together. */}
              </>
            )}


            <Separator />

            {/* Actions */}
            <div className="space-y-3 pt-2">
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
                disabled={saving || loadingDocument || documentLoadFailed || !selectedPatient}
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
              {/*
                WHERE THE DOCUMENT NOW LIVES — the standing answer to « enregistré, mais où ? ».

                ⚠️ It is a **persistent panel, not a toast action**, and that is the whole request: the save
                toast is gone in three seconds and the question is asked later, when the patient is at the desk
                and somebody wants the paper. It renders on `documentId`, so a document reopened to be edited
                says it too — the fact is « this is on file », not « you have just saved ».

                ⚠️ **No redirect.** Navigating away on save was the other candidate and it loses the editor
                mid-correction: the ordinary next action after saving a certificat is to print it, which is two
                controls below this. Two doors instead, both stated.

                ⚠️ « Voir le document » frames the **server-rendered PDF** (`DocumentPreviewDialog`), never a
                second HTML rendering of a legal document — see that component. It used to be withheld for the
                two official CNAM forms, whose paper was a pre-printed overlay `GET /medical-documents/{id}/pdf`
                cannot render; with those withdrawn, every remaining type renders through it.
              */}
              {documentId && selectedPatient && (
                <div className="space-y-2 rounded-lg border bg-muted/40 p-3" role="status">
                  <p className="text-xs text-muted-foreground">
                    Enregistré dans le dossier du patient. Le PDF est ajouté à ses fichiers dès qu&apos;il est
                    généré.
                  </p>
                  {/* `flex-wrap` + a real basis on each: two French labels that can neither shrink nor wrap
                      measure past this panel at 320 px — the `RecordSection` trap, one surface over. */}
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="min-w-0 shrink grow basis-40 coarse:h-11"
                      onClick={() => setSavedPreviewOpen(true)}
                    >
                      <FileText className="mr-2 h-4 w-4 shrink-0" />
                      Voir le document
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="min-w-0 shrink grow basis-40 coarse:h-11"
                      /*
                       * ⚠️ **The patient's FILE DRAWER, not `?tab=documents` on the patient page.** Reported
                       * as « ouvrir le dossier du patient is not working properly … the patient folder with
                       * all its files is better ». A route needs nothing to survive the navigation, unlike a
                       * `?tab=` the destination reads from `window.location` on mount — and it is where the
                       * document actually is: `CreateMedicalDocumentCommand` files every generated PDF as a
                       * `PatientFile` in the « documents » folder, which this drawer shows with previews and
                       * folders the tab has neither of. ⚠️ The drawer opens on the UNFILED files, so the
                       * document is one « Documents » chip away rather than in the first view; landing on the
                       * folder itself needs its id (`?folder=`), which this editor does not hold.
                       */
                      onClick={() => router.push(`/patients/${selectedPatient}/files`)}
                    >
                      <ExternalLink className="mr-2 h-4 w-4 shrink-0" />
                      Ouvrir le dossier du patient
                    </Button>
                  </div>
                </div>
              )}
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
              <div className={`grid gap-3 ${wordExportOffered ? "grid-cols-2" : "grid-cols-1"}`}>
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
                {wordExportOffered && (
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
            </div>
          </div>
        </div>

        {/* Right Panel - Document Preview. Its own scroll container only from `xl:` up — stacked, the outer
            column scrolls once instead of nesting two scrollers on a phone or a tablet. */}
        {/* The desk the paper sits on — app chrome, so it takes the palette instead of a `slate-*` gradient with
            a hand-written dark twin. */}
        <div className="min-w-0 flex-1 bg-gradient-to-br from-muted to-accent/60 p-4 xl:overflow-y-auto xl:p-12">
          <div className="max-w-4xl mx-auto">
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

              `.light` — the document surface (AC-39; see the `@custom-variant dark` comment in globals.css,
              which still names the CNAM overlay this file no longer renders). `bg-white` stays and the
              `dark:bg-slate-900` twin goes: a certificat
              médical that is white-on-black on screen and black-on-white on paper is not a preview of anything.
            */}
            <Card className="light p-6 sm:p-10 xl:p-16 bg-white shadow-2xl min-h-[1123px] flex flex-col" style={{ fontFamily: 'Helvetica, Arial, sans-serif' }}>
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
                  {/* Mirrors `DocumentIdentity.CarriesOrdreNumber`: every type but the certificat. */}
                  {formFields.doctorOrderNumber && documentType !== "certificat" && (
                    <p className="px-1" style={{ fontSize: '11pt' }}>
                      N° CNOMDT : {formFields.doctorOrderNumber}
                    </p>
                  )}
                </div>

                {/* No « À l'attention de » block — the letter is a blank letterhead. See the PDF renderer. */}

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

                {/* Patient Info — withheld on the two types that name their own patient in the prose. */}
                {documentType !== "liaison" && documentType !== "certificat" && (
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
                </div>
                )}

                <Separator />

                {/* Document Content */}
                <div className="space-y-4 flex-1">
                  {documentType === "prescription" && (
                    <div className="space-y-2">
                      {Array.isArray(formFields.medications) && formFields.medications.length > 0 ? (
                        <div className="space-y-1">
                          {formFields.medications.map((med, idx) => {
                            const printed = prescriptionLineParts(med);
                            return (
                              /* « 1/ », the médicament underlined, its posologie and whatever else the
                                 prescriber filled in indented plain under it — the PDF renderer's own rules,
                                 from the same composer. The underline stops at the médicament. */
                              <div key={idx} className="flex gap-2 py-1" style={{ fontSize: '11pt' }}>
                                <span className="shrink-0">{idx + 1}/</span>
                                <div className="min-w-0 space-y-0.5">
                                  <p className="underline">{printed.heading}</p>
                                  {printed.posology && <p className="ps-5">{printed.posology}</p>}
                                  {printed.details && <p className="ps-5">{printed.details}</p>}
                                </div>
                              </div>
                            );
                          })}
                          {/* Closes the list, so nothing can be written under the last médicament. */}
                          <div className="border-b border-foreground pt-1" />
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
                      {certificatBodyParagraphs().map((runs, index) => (
                        <p key={index}>
                          {runs.map((run, runIndex) =>
                            run.bold ? <strong key={runIndex}>{run.text}</strong> : <span key={runIndex}>{run.text}</span>,
                          )}
                        </p>
                      ))}
                    </div>
                  )}

                  {documentType === "honoraires" && (
                    // Read-only preview of the priced table — the left-hand form is the single source of truth,
                    // and the server recomputes every figure at render time (see `honorairesAmount`).
                    <div style={{ fontSize: '11pt' }} className="space-y-3">
                      {honorairesNamedLines(formFields.honorairesActs).length === 0 ? (
                        // Deliberately not an `EmptyState`: `handlePrint` clones this subtree into the print
                        // window, so an icon chip and a call to action would be printed onto the note.
                        <div className="min-h-[120px] p-4 border-2 border-dashed border-border rounded-lg text-muted-foreground">
                          Aucun acte ajouté
                        </div>
                      ) : (
                        <>
                          <table className="w-full">
                            <thead>
                              <tr className="border-b border-foreground/40">
                                <th className="py-1 text-left font-bold">Désignation</th>
                                <th className="py-1 text-right font-bold">Qté</th>
                                <th className="py-1 text-right font-bold">P.U.</th>
                                <th className="py-1 text-right font-bold">Total</th>
                              </tr>
                            </thead>
                            <tbody>
                              {honorairesNamedLines(formFields.honorairesActs).map((act, index) => (
                                <tr key={index} className="border-b border-foreground/10">
                                  <td className="py-1">{act.designation}</td>
                                  <td className="py-1 text-right">{honorairesQuantity(act.quantity)}</td>
                                  <td className="py-1 text-right">{formatDT(honorairesAmount(act.unitPrice))}</td>
                                  <td className="py-1 text-right">{formatDT(honorairesLineTotal(act))}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                          <p className="text-right font-bold" style={{ fontSize: '12pt' }}>
                            Total : {formatDT(honorairesTotal(formFields.honorairesActs))}
                          </p>
                        </>
                      )}
                      {formFields.honorairesNote.trim() && (
                        <p className="whitespace-pre-wrap pt-2" style={{ fontSize: '10pt' }}>
                          {formFields.honorairesNote.trim()}
                        </p>
                      )}
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
          </div>
        </div>
        </div>
      </div>

      {/* ⚠️ `mode: "saved"` reads the document back from the server rather than composing it here, so what is
          framed is byte-for-byte the paper the e-mail attaches. No `onEditInEditor`: this IS the editor. */}
      {documentId && (
        <DocumentPreviewDialog
          target={savedPreviewOpen ? { mode: "saved", documentId } : null}
          onClose={() => setSavedPreviewOpen(false)}
        />
      )}

      {/* Mounted for the honoraires editor only — it reads that patient's fiches, which no other type bills. */}
      {documentType === "honoraires" && (
        <BillableActsDialog
          open={billableActsOpen}
          onOpenChange={setBillableActsOpen}
          patientId={selectedPatient || null}
          onConfirm={addBillableActs}
        />
      )}
    </div>
  )
}

