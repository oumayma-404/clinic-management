"use client"

import type React from "react"
import { useState, useEffect, useRef } from "react"
import {
  Dialog,
  DialogBody,
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
import { CNAM_LIENS, CNAM_REGIMES } from "@/lib/cnam"
import { Switch } from "@/components/ui/switch"
import { Separator } from "@/components/ui/separator"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Badge } from "@/components/ui/badge"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { toast } from "sonner"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { User, Phone, Heart, CreditCard, Flag, Save, X, Plus, Trash2, StickyNote, AlertTriangle, Shield } from "lucide-react"
import { RecordSection } from "@/components/record/record-section"
import { cn } from "@/lib/utils"
import { patientsApi } from "@/lib/api/patients"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import type { PatientDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto } from "@/lib/api/types"
import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { isDeliverablePhone, PHONE_ERROR_FR } from "@/lib/phone"
import { formatAmount, formatDT, parseAmountInput, roundMillimes } from "@/lib/format"
import { CNAM_DENTAL_ALLOWANCE, CNAM_PLAFOND_SUPPLEMENTS, cnamBaseCeiling, cnamDefaultCeiling } from "@/lib/cnam"
import { SELECTABLE_GENDERS, genderLabel } from "@/components/appointment-labels"
import {
  DENTITIONS,
  DENTITION_LABELS_FR,
  dentitionFromBirthdate,
  type Dentition,
} from "@/lib/dentition"

/**
 * A blank / unreadable numeric CNAM field → `null`, not `0` (L10).
 *
 * <p>The distinction is load-bearing on both fields. A dependant count of `0` is a real statement (« assuré seul »)
 * and so is a ceiling of `0` — which is why the server clamps a non-positive value away rather than storing it: a
 * zero ceiling would report every patient as fully consumed, i.e. « CNAM refuses this patient ». Sending `null` for
 * a box nobody filled says « not recorded », which is what it is.</p>
 */
function parseOptionalCount(value: string): number | null {
  const trimmed = value.trim()
  if (!trimmed) return null
  const parsed = Number.parseInt(trimmed, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

/** @see parseOptionalCount — the dinar sibling, through the product's own amount parser (comma or point). */
function parseOptionalAmount(value: string): number | null {
  const trimmed = value.trim()
  if (!trimmed) return null
  const parsed = parseAmountInput(trimmed)
  return Number.isFinite(parsed) && parsed > 0 ? roundMillimes(parsed) : null
}

/**
 * What a history section shows when its read **failed** — deliberately not the same thing as an empty one.
 *
 * <p>⚠️ This is the single most consequential distinction in this dialog. `catch → setEntries([])` rendered
 * « Aucun antécédent médical » with total confidence after a network blip, on the list a dentist opens to check
 * for anticoagulants and cardiac conditions *before injecting*. There was no toast, no marker, nothing: a failed
 * read and a genuinely clean history were byte-identical on screen.</p>
 *
 * <p>The copy therefore says what is unknown rather than what is absent, and carries « Réessayer » so the
 * recovery is one tap and not "close the dialog and hope". `role="alert"` because it is not merely a status —
 * the user is about to act on data that is missing.</p>
 */
function HistoryLoadFailure({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="space-y-2 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
    >
      <p className="flex items-start gap-2 font-medium text-foreground">
        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
        <span>{message}</span>
      </p>
      <Button type="button" variant="outline" size="sm" onClick={onRetry}>
        Réessayer
      </Button>
    </div>
  )
}

interface EditPatientDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patient: PatientDto | null
  /** Called on success; receives the saved patient (used to open a newly-created patient). */
  onSuccess?: (patient?: PatientDto) => void
}

/**
 * French labels for the fields `validateForm` can refuse, used by the error summary. Every one of them lives in
 * « L'essentiel », which is always unfolded — so the summary names fields that are on screen rather than hiding
 * a refusal inside a folded section. Keeping them here rather than reading the `<Label>` text means the summary
 * cannot go stale against a reworded label without a compiler error.
 */
const FIELD_LABELS_FR: Record<string, string> = {
  firstName: "Prénom",
  lastName: "Nom",
  gender: "Sexe",
  birthdate: "Date de naissance",
  dentition: "Denture",
  phone: "Numéro de téléphone",
  email: "Email",
}

/** The six foldable sections of the patient form, in the order they appear. */
type SectionKey = "notes" | "adresse" | "medical" | "cnam" | "assurance" | "flags"

/** All folded (creating) or all unfolded (editing) — see `openSections` for why the two differ. */
function allSections(open: boolean): Record<SectionKey, boolean> {
  return { notes: open, adresse: open, medical: open, cnam: open, assurance: open, flags: open }
}

export function EditPatientDialog({ open, onOpenChange, patient, onSuccess }: EditPatientDialogProps) {
  // Personal Info State
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [gender, setGender] = useState("")
  const [birthdate, setBirthdate] = useState("")
  /**
   * Which teeth this patient is charted on. Defaulted from the birthdate, but only until the user decides for
   * themselves — `dentitionTouched` is the same guard `create-appointment-dialog` uses for its duration: a derived
   * default that keeps re-deriving would overwrite the dentist's deliberate choice the moment they corrected a typo
   * in the date of birth.
   */
  const [dentition, setDentition] = useState<Dentition | null>(null)
  const [dentitionTouched, setDentitionTouched] = useState(false)
  const [phone, setPhone] = useState("")
  const [email, setEmail] = useState("")
  const [addressStreet, setAddressStreet] = useState("")
  const [addressGovernorate, setAddressGovernorate] = useState("")
  const [addressCity, setAddressCity] = useState("")
  const [addressPostalCode, setAddressPostalCode] = useState("")
  const [emergencyName, setEmergencyName] = useState("")
  const [emergencyPhone, setEmergencyPhone] = useState("")
  // « Adressé par » — the referring practitioner. Optional, free text (usually a doctor outside this clinic).
  const [referredBy, setReferredBy] = useState("")

  // Patient-level notes. Distinct from a fiche de soins' notes, which describe one séance: these are what the
  // dentist wants back in front of them on every visit, which is why the section leads the form.
  const [patientNotes, setPatientNotes] = useState("")
  const [patientImportantNotes, setPatientImportantNotes] = useState("")

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

  /**
   * Whether the last read of each history list FAILED, as opposed to returning nothing.
   *
   * <p>Two flags rather than one: the two lists are fetched independently, and telling a dentist that the family
   * history is unavailable when only the medical one failed would train them to distrust a banner that is usually
   * wrong. See {@link HistoryLoadFailure}.</p>
   */
  const [medicalHistoryFailed, setMedicalHistoryFailed] = useState(false)
  const [familyHistoryFailed, setFamilyHistoryFailed] = useState(false)

  /**
   * The saved history entry the user asked to delete, held while the confirmation is open.
   *
   * <p>Deleting one used to fire on a single tap of an unlabelled `Trash2` ghost button and hit the API
   * immediately — no confirmation, no undo, no `aria-label`. With gloves on a tablet that is one slip from
   * permanently losing « allergie à la pénicilline » from a patient's file. `index` identifies the row (these
   * entries are keyed by index, not id, since an unsaved one has none) and `label` is captured now so the dialog
   * can name what it is about to destroy even as the list re-renders behind it.</p>
   */
  const [pendingHistoryRemoval, setPendingHistoryRemoval] = useState<{
    kind: "medical" | "family"
    index: number
    label: string
  } | null>(null)
  const [removingHistory, setRemovingHistory] = useState(false)

  // Administrative State
  const [insuranceProvider, setInsuranceProvider] = useState("")
  const [insuranceNumber, setInsuranceNumber] = useState("")
  const [policyHolder, setPolicyHolder] = useState("")

  // CNAM identity (optional — pre-fills the Bulletin de soins BS1).
  const [cnam, setCnam] = useState({
    identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "",
    assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "",
    // L10 — the two inputs to the annual ceiling. Strings like every other field here: they are form inputs,
    // parsed once on submit, so a half-typed « 1 1 » never becomes NaN in state.
    dependantCount: "", annualCeilingOverride: "",
  })

  // Flags State
  const [flagged, setFlagged] = useState(false)
  const [flagNotes, setFlagNotes] = useState("")

  const [loading, setLoading] = useState(false)
  // The one editing surface in the app with no form-level error display: a failed save produced a toast
  // that disappeared while the dialog sat there looking fine.
  const conflict = useConflict()
  /*
   * The version this form saves with, kept equal to the row's current one.
   *
   * ⚠️ Only the VERSION is taken from here — never the field values. The fresh read lands after the form has
   * hydrated, so feeding it back would overwrite whatever the user had already typed.
   */
  const { source: freshPatient, resync } = useFreshVersion(
    open,
    patient?.id,
    patient,
    () => patientsApi.get(patient!.id),
  )
  const [errors, setErrors] = useState<Record<string, string>>({})

  /**
   * Which secondary sections are unfolded. ⚠️ **Everything below « L'essentiel » is folded when CREATING and
   * unfolded when EDITING**, and that asymmetry is the whole design: registering a walk-in needs a name and a
   * phone, while opening an existing file is how somebody goes looking for the CNAM identity or an allergy. The
   * form used to render all forty fields expanded in both cases, which put « Enregistrer » under eleven CNAM
   * fields nobody filling in a new patient was going to touch.
   */
  const [openSections, setOpenSections] = useState<Record<SectionKey, boolean>>(() => allSections(!!patient))

  const toggleSection = (key: SectionKey) =>
    setOpenSections((current) => ({ ...current, [key]: !current[key] }))

  /**
   * What each folded section says about itself. ⚠️ **A folded section must never read as an empty one**: the
   * summary states what it holds, so folding makes a value read-only rather than invisible — the rule
   * `RecordSection` was built on. « À renseigner » is deliberately not « — »: one is an invitation, the other is
   * a claim that there is nothing to say.
   */
  const filled = (...values: (string | null | undefined)[]) => values.filter((v) => v && v.trim()).length

  const sectionSummary: Record<SectionKey, string> = {
    notes:
      filled(patientImportantNotes, patientNotes) > 0
        ? [patientImportantNotes.trim() && "alertes", patientNotes.trim() && "notes"].filter(Boolean).join(" · ")
        : "aucune note",
    adresse:
      filled(addressStreet, addressCity, addressGovernorate, emergencyName) > 0
        ? [addressCity.trim() || addressGovernorate, emergencyName.trim() && "contact d'urgence"]
            .filter(Boolean)
            .join(" · ")
        : "à renseigner",
    medical:
      filled(chronicDiseases, allergies) > 0
        ? [chronicDiseases.trim() && "affections", allergies.trim() && "allergies"].filter(Boolean).join(" · ")
        : "aucune information",
    cnam: cnam.identifiantUnique.trim() || (cnam.regime.trim() ? cnam.regime.trim() : "aucun identifiant"),
    assurance: insuranceProvider.trim() ? insuranceProvider.trim() : "aucune assurance privée",
    flags: flagged ? "patient signalé" : "aucun signalement",
  }

  /**
   * The server's « Ce patient existe déjà : … » while the confirmation is open, or null. Create mode only — an
   * update cannot produce a duplicate.
   */
  const [duplicatePrompt, setDuplicatePrompt] = useState<string | null>(null)
  /**
   * Whether the user has confirmed « Créer quand même » for the submit in flight.
   *
   * <p>A ref, not state: the confirmation grants it and re-submits in the same tick, and a state update would not
   * have landed by the time the payload is built. Reset by every fresh `handleSave`, so correcting the name and
   * saving again asks the question afresh.</p>
   */
  const allowDuplicateRef = useRef(false)

  // Populate the form once per opening.
  //
  // Keyed on the patient's ID, not the object: this dialog's parent refetches on every realtime
  // `patients` event, which hands down a new object identity. Depending on the object meant a peer's
  // unrelated edit re-ran this effect and wiped whatever the user had typed, mid-sentence.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (open) {
      // ⚠️ Re-derived on every open, not only on mount: this component is reused for both modes from the same
      // parent, so an edit followed by « Ajouter un patient » would otherwise open the new form with all six
      // sections unfolded — the exact wall of forty fields the folding exists to prevent.
      setOpenSections(allSections(!!patient))
      if (patient) {
        // Edit mode: populate with existing patient data
      setFirstName(patient.firstName || "")
      setLastName(patient.lastName || "")
      setGender(patient.gender || "")
      setBirthdate(patient.dateOfBirth ? patient.dateOfBirth.split('T')[0] : "")
      // A stored patient already has an answer; treat it as the user's own so the age rule never overrides it.
      setDentition((patient.dentition as Dentition) || null)
      setDentitionTouched(true)
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

      // « Adressé par »
      setReferredBy(patient.referredBy || "")

      // Patient-level notes
      setPatientNotes(patient.notes || "")
      setPatientImportantNotes(patient.importantNotes || "")

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
        dependantCount: patient.cnamInfo?.dependantCount != null ? String(patient.cnamInfo.dependantCount) : "",
        annualCeilingOverride:
          patient.cnamInfo?.annualCeilingOverride != null ? formatAmount(patient.cnamInfo.annualCeilingOverride) : "",
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
        setDentition(null)
        setDentitionTouched(false)
        setPhone("")
        setEmail("")
        setAddressStreet("")
        setAddressGovernorate("")
        setAddressCity("")
        setAddressPostalCode("")
        setEmergencyName("")
        setEmergencyPhone("")
        setReferredBy("")
        setPatientNotes("")
        setPatientImportantNotes("")
        setChronicDiseases("")
        setAllergies("")
        setInsuranceProvider("")
        setInsuranceNumber("")
        setCnam({ identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "", assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "", dependantCount: "", annualCeilingOverride: "" })
        setPolicyHolder("")
        setFlagged(false)
        setFlagNotes("")
        setMedicalHistoryEntries([])
        setFamilyHistoryEntries([])
        // Create mode reads nothing, so neither list can be in a failed state.
        setMedicalHistoryFailed(false)
        setFamilyHistoryFailed(false)
      }
    }
    conflict.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [patient?.id, open])

  // Pre-select the dentition from the birthdate until the user answers for themselves. Derived in an effect rather
  // than inside the date's onChange so it also fires for a date typed, pasted or picked from the native calendar.
  useEffect(() => {
    if (dentitionTouched) return
    setDentition(dentitionFromBirthdate(birthdate))
  }, [birthdate, dentitionTouched])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setErrors({})
      setMedicalHistoryEntries([])
      setFamilyHistoryEntries([])
      // A stale failure banner on the next patient would claim their history is unknown when it was never read.
      setMedicalHistoryFailed(false)
      setFamilyHistoryFailed(false)
      setPendingHistoryRemoval(null)
    }
  }, [open])

  // Load medical history entries.
  // ⚠️ The `catch` records that the read FAILED instead of quietly asserting an empty history — see
  // `HistoryLoadFailure`. Success clears the flag so a retry (or reopening the dialog) can recover.
  const loadMedicalHistoryEntries = async (patientId: string) => {
    try {
      const entries = await patientMedicalHistoryApi.list(patientId)
      setMedicalHistoryEntries(entries.map(e => ({
        id: e.id,
        description: e.description,
        date: e.date,
        notes: e.notes,
      })))
      setMedicalHistoryFailed(false)
    } catch (err) {
      console.error("Failed to load medical history:", err)
      setMedicalHistoryEntries([])
      setMedicalHistoryFailed(true)
    }
  }

  // Load family history entries (same failure contract as the medical one above).
  const loadFamilyHistoryEntries = async (patientId: string) => {
    try {
      const entries = await patientFamilyHistoryApi.list(patientId)
      setFamilyHistoryEntries(entries.map(e => ({
        id: e.id,
        relationship: e.relationship,
        condition: e.condition,
        notes: e.notes,
      })))
      setFamilyHistoryFailed(false)
    } catch (err) {
      console.error("Failed to load family history:", err)
      setFamilyHistoryEntries([])
      setFamilyHistoryFailed(true)
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

  // Remove medical history entry.
  // Returns whether it succeeded, so the confirmation dialog can stay OPEN on failure — closing it would hide
  // the refusal and leave the user believing an irreversible delete had gone through.
  const removeMedicalHistoryEntry = async (index: number): Promise<boolean> => {
    const entry = medicalHistoryEntries[index]
    if (!entry) return false
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
        return false
      }
    }
    setMedicalHistoryEntries(medicalHistoryEntries.filter((_, i) => i !== index))
    return true
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

  // Remove family history entry (same success/failure contract as the medical one above).
  const removeFamilyHistoryEntry = async (index: number): Promise<boolean> => {
    const entry = familyHistoryEntries[index]
    if (!entry) return false
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
        return false
      }
    }
    setFamilyHistoryEntries(familyHistoryEntries.filter((_, i) => i !== index))
    return true
  }

  /**
   * Ask before destroying a **saved** entry; delete an unsaved row outright.
   *
   * <p>The distinction is the whole reason this is not a blanket confirm: a row the user just added by pressing
   * « Ajouter une entrée » has nothing behind it, and making them confirm a mistap they are undoing one second
   * later is the fastest way to teach them to dismiss the dialog without reading it — which is precisely what
   * makes the real one useless.</p>
   */
  const requestHistoryRemoval = (kind: "medical" | "family", index: number) => {
    const entry = kind === "medical" ? medicalHistoryEntries[index] : familyHistoryEntries[index]
    if (!entry) return
    if (!entry.id) {
      void (kind === "medical" ? removeMedicalHistoryEntry(index) : removeFamilyHistoryEntry(index))
      return
    }
    const label =
      kind === "medical"
        ? (entry as { description: string }).description
        : [(entry as { relationship: string }).relationship, (entry as { condition: string }).condition]
            .filter(Boolean)
            .join(" — ")
    setPendingHistoryRemoval({ kind, index, label: label.trim() || "Entrée sans description" })
  }

  const confirmHistoryRemoval = async () => {
    if (!pendingHistoryRemoval) return
    setRemovingHistory(true)
    try {
      const ok =
        pendingHistoryRemoval.kind === "medical"
          ? await removeMedicalHistoryEntry(pendingHistoryRemoval.index)
          : await removeFamilyHistoryEntry(pendingHistoryRemoval.index)
      if (ok) setPendingHistoryRemoval(null)
    } finally {
      setRemovingHistory(false)
    }
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

    // Required: it decides which chart every future séance is recorded on, and there is no neutral value.
    if (!dentition) {
      newErrors.dentition = "La denture est requise"
    }

    // Optional, in both modes: `Patients.PhoneNumber` is nullable and a walk-in, a child or an elderly patient
    // is routinely registered with a name alone. Requiring it here refused that record outright and pushed
    // reception into typing a fake number — the sentinel problem (`0000000000`) the backend deliberately retired.
    // The consequence is stated instead, under the field. A number that IS given must still be deliverable.
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
    // A fresh submit re-asks: the user may have corrected the name or the birthdate since the last refusal, so a
    // grant given about the previous attempt says nothing about this one.
    allowDuplicateRef.current = false
    await savePatient()
  }

  /**
   * The save itself, callable without a form event — which is what the « Créer quand même » confirmation needs.
   */
  const savePatient = async () => {
    if (!validateForm()) {
      toast.error("Erreurs dans le formulaire", {
        description: "Veuillez vérifier que tous les champs requis sont correctement remplis",
        duration: 4000,
      })
      return
    }

    setLoading(true)

    try {
      /*
       * The address block — `null` when every box is blank, **never** `undefined`.
       *
       * ⚠️ `undefined` is dropped by `JSON.stringify`, and the update command reads an absent `address` as "leave
       * the stored one alone", so emptying the four boxes used to be a silent no-op. `null` is the clear. On create
       * the two are equivalent (there is nothing stored to keep), so one expression serves both paths.
       */
      const addressObj =
        addressStreet.trim() || addressCity.trim() || addressGovernorate.trim() || addressPostalCode.trim()
          ? {
              street: addressStreet.trim(),
              city: addressCity.trim(),
              state: addressGovernorate.trim(),
              zipCode: addressPostalCode.trim(),
              country: "Tunisia", // Default country, can be made configurable
            }
          : null

      let savedPatient: PatientDto

      if (patient) {
        // Edit mode: Update existing patient
        const updateData: Partial<PatientDto> & { isFlagged?: boolean; flagNotes?: string } = {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          gender,
          dentition: dentition ?? undefined,
          // Undefined, not "": the update command reads null-means-unchanged, and an empty string is not a date.
          dateOfBirth: birthdate || undefined,
          // Explicit null, not undefined: the command is tri-state, so undefined would be read as
          // "leave it alone" and clearing the box would silently do nothing.
          phoneNumber: phone.trim() || null,
          email: email.trim() || null,
          // The row's version as last read from the server — so a peer's save in the meantime is a 409, not a
          // silent overwrite of their work, and our own previous save is not mistaken for one.
          version: freshPatient?.version ?? patient.version,
          address: addressObj,
          emergencyContactName: emergencyName.trim(),
          emergencyContactPhone: emergencyPhone.trim(),
          // Always present (possibly ""), so emptying the box clears the stored value instead of
          // reading as "leave it alone" — same reason as the two contact fields above.
          referredBy: referredBy.trim(),
          // Always present (possibly ""), so emptying either box clears it. Each is resolved independently
          // server-side, so sending both is safe.
          notes: patientNotes.trim(),
          importantNotes: patientImportantNotes.trim(),
          /*
           * ⚠️ `.trim()`, **not** `.trim() || undefined` — the L1b defect, and the highest-consequence one in this
           * file.
           *
           * These two sat three lines below `notes`/`importantNotes`, which already carried the comment explaining
           * exactly why a present-but-empty string is required, and still sent `undefined`. `JSON.stringify` drops
           * an `undefined` value, the handler reads an absent key as "leave it alone" — so **an allergy typed on
           * the wrong patient could not be removed by anybody**, and the optimistic local spread showed it as gone
           * until a refetch put it back, which is the worst possible failure: the user believes it worked.
           *
           * Sending both is safe: the handler resolves each independently, so clearing an allergy cannot blank the
           * antécédents beside it.
           */
          medicalHistory: chronicDiseases.trim(),
          allergies: allergies.trim(),
          // Exactly what was typed (AC-21). The two `|| "Unknown"` paddings existed because the server demanded
          // both halves; it now accepts either, so a patient who named their insurer with the card at home no
          // longer acquires a policy number literally reading « Unknown » — indistinguishable, in every later
          // read, from a real one.
          insuranceInfo: (insuranceProvider.trim() || insuranceNumber.trim()) ? {
            provider: insuranceProvider.trim() || undefined,
            policyNumber: insuranceNumber.trim() || undefined,
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
            dependantCount: parseOptionalCount(cnam.dependantCount),
            annualCeilingOverride: parseOptionalAmount(cnam.annualCeilingOverride),
          },
          // "Signaler ce patient" toggle: true ensures an active flag, false clears it.
          isFlagged: flagged,
          // Present-but-blank clears the note (the handler passes it positionally to `PatientFlag.Update`), so
          // `|| undefined` here is not the L1b defect — but `.trim()` states the intent instead of relying on it.
          flagNotes: flagNotes.trim(),
        }

        /*
         * The **server's** patient, not `{ ...patient, ...updateData }`.
         *
         * The local spread was the second half of the L1b defect: it echoed the request back as though it had been
         * accepted, so the UI could show a state the server never stored (a cleared allergy the old payload never
         * asked it to clear looked cleared until the next refetch). It also could not be correct in principle — the
         * spread carries request-shaped keys (`isFlagged`, `flagNotes`, a raw `dentition`) over a DTO, and knows
         * nothing about what the handler derives. The response is the one authority on what was saved.
         */
        savedPatient = await patientsApi.update(patient.id, updateData)

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
          // Blank means blank: a walk-in registered with nothing but a name stores no date of birth. This used to
          // send *today* so the column would take the row, which is the client half of the same fabrication.
          dateOfBirth: birthdate || null,
          gender: gender || "Unknown",
          dentition: dentition ?? undefined,
          email: email.trim() || null,
          phoneNumber: phone.trim() || null,
          medicalHistory: chronicDiseases.trim() || undefined,
          allergies: allergies.trim() || undefined,
          address: addressObj,
          emergencyContactName: emergencyName.trim() || undefined,
          emergencyContactPhone: emergencyPhone.trim() || undefined,
          referredBy: referredBy.trim() || undefined,
          notes: patientNotes.trim() || undefined,
          importantNotes: patientImportantNotes.trim() || undefined,
          // Exactly what was typed (AC-21). The two `|| "Unknown"` paddings existed because the server demanded
          // both halves; it now accepts either, so a patient who named their insurer with the card at home no
          // longer acquires a policy number literally reading « Unknown » — indistinguishable, in every later
          // read, from a real one.
          insuranceInfo: (insuranceProvider.trim() || insuranceNumber.trim()) ? {
            provider: insuranceProvider.trim() || undefined,
            policyNumber: insuranceNumber.trim() || undefined,
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
            dependantCount: parseOptionalCount(cnam.dependantCount),
            annualCeilingOverride: parseOptionalAmount(cnam.annualCeilingOverride),
          },
          medicalHistoryEntries: medicalHistoryEntriesToSend.length > 0 ? medicalHistoryEntriesToSend : undefined,
          familyHistoryEntries: familyHistoryEntriesToSend.length > 0 ? familyHistoryEntriesToSend : undefined,
          isFlagged: flagged,
          flagNotes: flagNotes.trim() || undefined,
          // Absent on the first attempt, so the server checks whether this person is already on file. Only the
          // « Créer quand même » confirmation sets it — see the AlertDialog at the bottom of this file.
          allowDuplicate: allowDuplicateRef.current || undefined,
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

      /*
       * « Ce patient existe déjà » — a question, not a failure, so it gets a confirmation rather than the error
       * toast below. Two people can genuinely share a name and a birthday, but a duplicate file cannot be merged
       * or deleted afterwards, so the answer has to come from the user rather than from either default.
       *
       * Guarded on the ref so a refusal that somehow survives the grant surfaces as a real error instead of
       * reopening the same prompt for ever.
       */
      if (
        err instanceof ApiError &&
        err.code === ApiErrorCode.PatientDuplicate &&
        !allowDuplicateRef.current
      ) {
        setDuplicatePrompt(err.message)
        return
      }

      const fallback = patient
        ? "Échec de la mise à jour des informations du patient"
        : "Échec de la création du patient"
      // A conflict stays on screen with a reload; anything else keeps the familiar toast as well, since
      // those are usually transient and the user may already have looked away.
      if (!conflict.capture(err, fallback)) {
        /*
         * The patient PUT may well have landed before a later history write threw, which leaves the row a
         * version ahead of this form. Re-reading here is what stops the next click being told a colleague
         * edited it — the conflict branch deliberately does NOT resync, or a retry would overwrite a real one.
         */
        await resync()
        toast.error(patient ? "Erreur lors de la mise à jour" : "Erreur lors de la création", {
          description: conflict.error ?? fallback,
          duration: 4000,
        })
      }
    } finally {
      setLoading(false)
    }
  }

  const guard = useDirtyGuard(open, onOpenChange)

  const fullName = `${firstName} ${lastName}`.trim()
  const hasActiveFlags = patient?.flags && patient.flags.some(flag => flag.isActive)

  return (
    <>
    {/* ⚠️ Only the ROOT and « Annuler » route through the guard. The save path calls the raw `onOpenChange`
        prop, so a successful save closes without being asked to confirm — no `markClean` bookkeeping, and no
        way for the two to fall out of step (AC-23). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="gap-0 p-0 md:max-h-[90dvh] md:max-w-4xl">
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

        {/* Was `max-h-[calc(90vh-200px)]` — a magic number that guessed the chrome's height, and guessed in
            `vh`, so the keyboard opening pushed the footer off screen (AC-25). `DialogBody` takes whatever is
            left instead of subtracting an assumed 200 px. */}
        <DialogBody>
          <form onSubmit={handleSave} className="p-6 space-y-6">
            <FormErrorBanner message={conflict.error} />
            {/* ⚠️ A summary as well as the per-field messages, because on a form this long the first refusal can
                be off screen — and on a phone it always is. `FormErrorBanner` is the shared aria-live region, so
                this announces too. It names the fields; the fields themselves carry the reason. */}
            <FormErrorBanner
              message={
                Object.keys(errors).length > 0
                  ? Object.keys(errors).length === 1
                    ? `Corrigez « ${FIELD_LABELS_FR[Object.keys(errors)[0]] ?? Object.keys(errors)[0]} » ci-dessous.`
                    : `Corrigez ces champs ci-dessous : ${Object.keys(errors)
                        .map((field) => FIELD_LABELS_FR[field] ?? field)
                        .join(", ")}.`
                  : null
              }
            />

            {/* Personal Information Section */}
            <div className="space-y-4">
              <div className="flex items-center gap-2 pb-2">
                <User className="h-5 w-5 text-primary" />
                <h3 className="text-lg font-semibold">L&apos;essentiel</h3>
                {/* Named for what it is rather than for what it contains. A patient arriving without an appointment
                    is registered from these fields alone — which is why the phone moved UP into them: it was two
                    sections down, below the fold, and it is the field reception actually needs (rappels, relances). */}
                <span className="text-xs text-muted-foreground">suffit à enregistrer le patient</span>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 rounded-lg border bg-muted/30">
                {/*
                  Autofill tokens run across the identity and address fields below. This form is filled at a
                  reception desk on a shared tablet, and thirty inputs with no `autocomplete` means the browser
                  can offer nothing at all — every address retyped by hand.

                  ⚠️ Deliberately NOT on the emergency contact or the CNAM assuré: those describe a *different*
                  person from the patient, so a `tel`/`postal-code` suggestion there would be a confidently wrong
                  answer written into a clinical record.
                */}
                {/* First Name */}
                <div className="space-y-2">
                  <Label htmlFor="firstName">
                    Prénom <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="firstName"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    placeholder="Mohamed"
                    autoComplete="given-name"
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
                    placeholder="Ben Salah"
                    autoComplete="family-name"
                    aria-invalid={!!errors.lastName}
                    className={cn(errors.lastName && "border-destructive")}
                  />
                  {errors.lastName && <p className="text-sm text-destructive">{errors.lastName}</p>}
                </div>

                {/* Phone */}
                <div className="space-y-2">
                  {/* « recommandé » in both modes: an asterisk here contradicted the sentence directly below it,
                      which only makes sense for a field that may be left empty. */}
                  <Label htmlFor="phone">
                    Numéro de téléphone <span className="text-muted-foreground text-xs">(recommandé)</span>
                  </Label>
                  <Input
                    id="phone"
                    type="tel"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    placeholder="Ex. 20 123 456 (ou +216…)"
                    autoComplete="tel"
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
                    placeholder="mohamed.bensalah@email.tn"
                    autoComplete="email"
                    aria-invalid={!!errors.email}
                    className={cn(errors.email && "border-destructive")}
                  />
                  {errors.email && <p className="text-sm text-destructive">{errors.email}</p>}
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

                {/*
                  Denture — asked once, here, because it is a property of the patient and not of a visit.

                  It replaces two toggles that asked the same question about the same patient every time anyone opened
                  the odontogram or the fiche editor, plus a per-fiche badge in the dossier dentaire. Pre-selected
                  from the age so the common case is already right; changeable because the age rule is a heuristic
                  and a growing child has to be switchable.
                */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="dentition-Child">
                    Denture <span className="text-destructive">*</span>
                  </Label>
                  <div
                    role="radiogroup"
                    aria-label="Denture"
                    className={cn(
                      "flex flex-col gap-2 sm:flex-row",
                      errors.dentition && "rounded-md ring-1 ring-destructive",
                    )}
                  >
                    {DENTITIONS.map((value) => {
                      const selected = dentition === value
                      return (
                        <button
                          key={value}
                          id={`dentition-${value}`}
                          type="button"
                          role="radio"
                          aria-checked={selected}
                          onClick={() => {
                            setDentition(value)
                            setDentitionTouched(true)
                          }}
                          /*
                            ⚠️ Two separate things make this read as a choice, and it needed both.

                            The fill: `bg-card`, not `bg-background`. Page-ground fill under `text-muted-foreground`
                            is how this app paints an *inert* surface, so on « Ajouter un patient » — where no
                            birthdate has been typed yet and therefore neither option is pre-selected from the age —
                            a required field rendered as two greyed-out boxes that read as disabled inputs.

                            The marker: a real radio dot. `bg-card` alone was not enough, because the geometry here
                            is an input's — full width, bordered, left-aligned text — so once it went white it read
                            as a *text field* instead. The durée presets in `create-appointment-dialog` get away
                            with `bg-card` and no marker only because they are short, centred, button-shaped chips;
                            these labels are sentences and cannot be. With neither option chosen, the two hollow
                            circles are also the only thing on screen saying an answer is still owed.
                          */
                          className={cn(
                            "flex flex-1 items-center gap-2.5 rounded-md border px-3 py-2 text-left text-sm transition-colors duration-150 ease-out motion-reduce:transition-none",
                            selected
                              ? "border-primary bg-primary/10 font-medium text-foreground"
                              : "bg-card text-foreground hover:bg-muted/60",
                          )}
                        >
                          <span
                            aria-hidden="true"
                            className={cn(
                              "flex size-4 shrink-0 items-center justify-center rounded-full border transition-colors duration-150 ease-out motion-reduce:transition-none",
                              selected ? "border-primary" : "border-input",
                            )}
                          >
                            {selected && <span className="size-2 rounded-full bg-primary" />}
                          </span>
                          {DENTITION_LABELS_FR[value]}
                        </button>
                      )
                    })}
                  </div>
                  {errors.dentition ? (
                    <p className="text-sm text-destructive">{errors.dentition}</p>
                  ) : (
                    <p className="text-xs text-muted-foreground">
                      Détermine les dents affichées dans l&apos;odontogramme et les fiches de soins.
                      {!dentitionTouched && dentition && " Proposé d'après l'âge."}
                    </p>
                  )}
                </div>

                {/* « Adressé par » — after the identity fields, not before them: it is a fact *about* the patient,
                    and nothing should stand between opening this form and typing who the patient is. */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="referredBy">
                    Adressé par <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Input
                    id="referredBy"
                    value={referredBy}
                    onChange={(e) => setReferredBy(e.target.value)}
                    placeholder="Dr Ben Salah, Sfax — laisser vide si le patient vient de lui-même"
                  />
                </div>
              </div>
            </div>

            {/*
              Notes: second section — apparent without displacing the patient's identity.

              It sits directly after « Informations personnelles » because these are the two fields read on every
              visit, and it used to sit last, below CNAM and insurance. « Notes importantes » carries the same amber
              weight here as the widget that displays it on the patient's file, so the box you type into looks like
              the box you will read.
            */}
            <RecordSection
              size="md"
              icon={<StickyNote className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Notes du patient"
              summary={sectionSummary.notes}
              open={openSections.notes}
              onToggle={() => toggleSection("notes")}
            >

              <div className="grid grid-cols-1 gap-4">
                <div className="space-y-2">
                  <Label
                    htmlFor="patientImportantNotes"
                    className="flex items-center gap-1.5 text-amber-800 dark:text-amber-300"
                  >
                    <AlertTriangle className="h-4 w-4" />
                    Notes importantes <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Textarea
                    id="patientImportantNotes"
                    value={patientImportantNotes}
                    onChange={(e) => setPatientImportantNotes(e.target.value)}
                    placeholder="Ce qu'il faut voir avant chaque soin — ex. : sous anticoagulants, prémédication requise"
                    className="min-h-[70px] resize-none border-amber-300 bg-amber-50/60 text-amber-950 placeholder:text-amber-700/60 focus-visible:ring-amber-500 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-50 dark:placeholder:text-amber-300/50"
                  />
                  <p className="text-xs text-muted-foreground">
                    Toujours visibles en haut du dossier du patient.
                  </p>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="patientNotes">
                    Notes <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  <Textarea
                    id="patientNotes"
                    value={patientNotes}
                    onChange={(e) => setPatientNotes(e.target.value)}
                    placeholder="Contexte utile au fil des visites — ex. : patient anxieux, préfère les rendez-vous du matin"
                    className="min-h-[70px] resize-none"
                  />
                </div>
              </div>
            </RecordSection>

            {/* Contact Information Section */}
            <RecordSection
              size="md"
              icon={<Phone className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Adresse et contact d&apos;urgence"
              summary={sectionSummary.adresse}
              open={openSections.adresse}
              onToggle={() => toggleSection("adresse")}
            >

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Address - Street */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="addressStreet">Adresse</Label>
                  <Input
                    id="addressStreet"
                    value={addressStreet}
                    onChange={(e) => setAddressStreet(e.target.value)}
                    placeholder="12 rue de Carthage, Lafayette"
                    autoComplete="street-address"
                  />
                </div>

                {/* Governorate (finding #17: Tunisian dropdown, not free text) */}
                <div className="space-y-2">
                  <Label htmlFor="addressGovernorate">Gouvernorat</Label>
                  {/* `w-full`: the primitive ships `w-fit`, so an unqualified trigger renders as a short pill in
                      a column of full-width Inputs — it reads as a different kind of control than it is. */}
                  <Select value={addressGovernorate} onValueChange={setAddressGovernorate}>
                    <SelectTrigger id="addressGovernorate" className="w-full">
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
                    autoComplete="address-level2"
                  />
                </div>

                {/* Postal Code */}
                <div className="space-y-2">
                  <Label htmlFor="addressPostalCode">
                    Code postal <span className="text-muted-foreground text-xs">(optionnel)</span>
                  </Label>
                  {/* ⚠️ `inputMode="numeric"`, NOT `type="number"`. A postal code is an IDENTIFIER, not a
                      quantity: `type="number"` adds spinners, lets the scroll wheel silently change it, and
                      drops a leading zero. `inputMode` raises the digit keypad and changes nothing else. */}
                  <Input
                    id="addressPostalCode"
                    value={addressPostalCode}
                    onChange={(e) => setAddressPostalCode(e.target.value)}
                    placeholder="1000"
                    inputMode="numeric"
                    autoComplete="postal-code"
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
                  {/* `type="tel"` — the patient's own number has always had it; this one did not, so the
                      emergency contact was the single field in the form that opened the alphabet keyboard for a
                      value that is entirely digits. No `autoComplete`: this is somebody else's number. */}
                  <Input
                    id="emergencyPhone"
                    type="tel"
                    value={emergencyPhone}
                    onChange={(e) => setEmergencyPhone(e.target.value)}
                    placeholder="+216 ..."
                  />
                </div>
              </div>
            </RecordSection>

            {/* Medical Information Section */}
            <RecordSection
              size="md"
              icon={<Heart className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Informations médicales"
              summary={sectionSummary.medical}
              open={openSections.medical}
              onToggle={() => toggleSection("medical")}
            >

              <div className="grid grid-cols-1 gap-4">
                {/* Chronic Diseases */}
                <div className="space-y-2">
                  <Label htmlFor="chronicDiseases">Maladies chroniques / affections</Label>
                  <Textarea
                    id="chronicDiseases"
                    value={chronicDiseases}
                    onChange={(e) => setChronicDiseases(e.target.value)}
                    placeholder="Hypertension, diabète de type 2"
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
                    placeholder="Pénicilline, fruits de mer"
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
                  
                  {/* ⚠️ « Aucun antécédent » is only ever shown when the read SUCCEEDED. */}
                  {medicalHistoryFailed && (
                    <HistoryLoadFailure
                      message="Les antécédents médicaux n'ont pas pu être chargés. Ne considérez pas cette liste comme complète."
                      onRetry={() => { if (patient?.id) void loadMedicalHistoryEntries(patient.id) }}
                    />
                  )}

                  {!medicalHistoryFailed && medicalHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Aucun antécédent médical. Cliquez sur « Ajouter une entrée » pour en ajouter un.</p>
                  ) : medicalHistoryEntries.length === 0 ? null : (
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
                              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
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
                            {/* Was a bare icon that deleted a saved allergy on one unconfirmed tap, with no
                                accessible name at all. Now: named, and routed through the confirmation. */}
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => requestHistoryRemoval("medical", index)}
                              className="text-destructive hover:text-destructive"
                              aria-label={`Supprimer l'antécédent ${entry.description || "sans description"}`}
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
                  
                  {familyHistoryFailed && (
                    <HistoryLoadFailure
                      message="Les antécédents familiaux n'ont pas pu être chargés. Ne considérez pas cette liste comme complète."
                      onRetry={() => { if (patient?.id) void loadFamilyHistoryEntries(patient.id) }}
                    />
                  )}

                  {!familyHistoryFailed && familyHistoryEntries.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Aucun antécédent familial. Cliquez sur « Ajouter une entrée » pour en ajouter un.</p>
                  ) : familyHistoryEntries.length === 0 ? null : (
                    <div className="space-y-3">
                      {familyHistoryEntries.map((entry, index) => (
                        <div key={index} className="p-3 border rounded-lg space-y-2 bg-background">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex-1 space-y-2">
                              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
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
                              onClick={() => requestHistoryRemoval("family", index)}
                              className="text-destructive hover:text-destructive"
                              aria-label={`Supprimer l'antécédent familial ${
                                [entry.relationship, entry.condition].filter(Boolean).join(" — ") ||
                                "sans description"
                              }`}
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
            </RecordSection>

            {/* CNAM Identity Section */}
            <RecordSection
              size="md"
              icon={<CreditCard className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Identité CNAM"
              summary={sectionSummary.cnam}
              open={openSections.cnam}
              onToggle={() => toggleSection("cnam")}
            >
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="cnamIdentifiant">Identifiant Unique</Label>
                  {/* Digit keypad, but still a text field — see the postal-code note above: an identifiant is
                      an identifier, and `type="number"` would let a scroll gesture change a CNAM number. */}
                  <Input id="cnamIdentifiant" inputMode="numeric" value={cnam.identifiantUnique} onChange={(e) => setCnam({ ...cnam, identifiantUnique: e.target.value })} placeholder="Ex: 12345678" />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamRegime">Régime</Label>
                  <Select value={cnam.regime || undefined} onValueChange={(v) => setCnam({ ...cnam, regime: v })}>
                    <SelectTrigger id="cnamRegime" className="w-full"><SelectValue placeholder="Choisir…" /></SelectTrigger>
                    {/* From `lib/cnam.ts`, not literals: the stored string is what the BS1 renderer matches to
                        tick the box, so a retyped « Convention bilatérale » missing its accent prints an empty
                        régime and raises nothing. The bulletin editor validates against the same list. */}
                    <SelectContent>
                      {CNAM_REGIMES.map((r) => (
                        <SelectItem key={r} value={r}>
                          {r}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cnamMaladeLien">Lien du malade à l'assuré</Label>
                  <Select value={cnam.maladeLien || undefined} onValueChange={(v) => setCnam({ ...cnam, maladeLien: v })}>
                    <SelectTrigger id="cnamMaladeLien" className="w-full"><SelectValue placeholder="Choisir…" /></SelectTrigger>
                    <SelectContent>
                      {CNAM_LIENS.map((l) => (
                        <SelectItem key={l} value={l}>
                          {l}
                        </SelectItem>
                      ))}
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
                  <Input id="cnamAssureCp" inputMode="numeric" value={cnam.assurePostalCode} onChange={(e) => setCnam({ ...cnam, assurePostalCode: e.target.value })} />
                </div>

                {/*
                  L10 — the two inputs to the annual ceiling. They sit in the CNAM block and not with the insurance
                  fields because they describe the *caisse's* cover, and « Remboursement indicatif » is computed
                  from them.

                  ⚠️ `md:col-span-2` on the wrapper: the barème preview under the count and the supplement list
                  under the override are prose, and prose in a half-width column at 820 px wraps to five lines.
                */}
                <div className="space-y-4 md:col-span-2">
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <div className="space-y-2">
                      <Label htmlFor="cnamDependants">Ayants droit à charge</Label>
                      {/* Digit keypad, text field — the same reason as the identifiant and the postal code above:
                          `type="number"` lets a scroll gesture over the field change the value. */}
                      <Input
                        id="cnamDependants"
                        inputMode="numeric"
                        value={cnam.dependantCount}
                        onChange={(e) => setCnam({ ...cnam, dependantCount: e.target.value })}
                        placeholder="Ex: 2 — laisser vide si assuré seul"
                      />
                      <p className="text-xs text-muted-foreground">
                        Barème : {formatDT(cnamBaseCeiling(parseOptionalCount(cnam.dependantCount) ?? 0))} pour le
                        foyer, + {formatDT(CNAM_DENTAL_ALLOWANCE)} dédiés aux soins dentaires externes ={" "}
                        <span className="font-medium text-foreground">
                          {formatDT(cnamDefaultCeiling(parseOptionalCount(cnam.dependantCount) ?? 0))}
                        </span>
                        .
                      </p>
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="cnamCeiling">Plafond annuel (si connu)</Label>
                      <Input
                        id="cnamCeiling"
                        inputMode="decimal"
                        value={cnam.annualCeilingOverride}
                        onChange={(e) => setCnam({ ...cnam, annualCeilingOverride: e.target.value })}
                        placeholder="Laisser vide pour utiliser le barème"
                      />
                      <p className="text-xs text-muted-foreground">
                        Remplace le barème. À utiliser pour les suppléments, que le logiciel n'enregistre pas :{" "}
                        {CNAM_PLAFOND_SUPPLEMENTS.map((s, i) => (
                          <span key={s.label}>
                            {i > 0 ? " · " : ""}
                            {formatDT(s.amount)} {s.label}
                          </span>
                        ))}
                        .
                      </p>
                    </div>
                  </div>
                  {/*
                    § 13 and the L10 spec's own ⚠️: the figure must be labelled an estimate and must say WHY, or it
                    becomes a confident wrong number. Two independent reasons, both stated — the barème is not
                    officially confirmed, and this clinic can only count its own acts.
                  */}
                  <p className="rounded-md bg-warning-wash p-3 text-xs text-warning-ink" role="note">
                    Le plafond et le « reste » affichés sur les documents sont <strong>indicatifs</strong> : le
                    barème 2024 ci-dessus provient de sources concordantes mais non officielles, et ce cabinet ne
                    voit que les actes qu'il a lui-même réalisés — un patient soigné ailleurs a consommé un plafond
                    invisible ici. Le montant réellement remboursé est fixé par la CNAM.
                  </p>
                </div>
              </div>
            </RecordSection>

            {/* Insurance Information Section */}
            <RecordSection
              size="md"
              icon={<Shield className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Informations d'assurance"
              summary={sectionSummary.assurance}
              open={openSections.assurance}
              onToggle={() => toggleSection("assurance")}
            >

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Insurance Provider */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceProvider">Assureur</Label>
                  <Input
                    id="insuranceProvider"
                    value={insuranceProvider}
                    onChange={(e) => setInsuranceProvider(e.target.value)}
                    placeholder="CNAM, STAR Assurances, GAT"
                  />
                </div>

                {/* Insurance Number */}
                <div className="space-y-2">
                  <Label htmlFor="insuranceNumber">Numéro d'assurance / d'identification</Label>
                  <Input
                    id="insuranceNumber"
                    value={insuranceNumber}
                    onChange={(e) => setInsuranceNumber(e.target.value)}
                    placeholder="Ex. 12345678"
                  />
                </div>

                {/* Policy Holder */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="policyHolder">Numéro de groupe</Label>
                  <Input
                    id="policyHolder"
                    value={policyHolder}
                    onChange={(e) => setPolicyHolder(e.target.value)}
                    placeholder="Ex. GRP-2026-014"
                  />
                </div>
              </div>
            </RecordSection>

            {/* Flags Section */}
            <RecordSection
              size="md"
              icon={<Flag className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Signalements du patient"
              summary={sectionSummary.flags}
              open={openSections.flags}
              onToggle={() => toggleSection("flags")}
            >

              <div className="space-y-4">
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
            </RecordSection>
          </form>
        </DialogBody>

        <Separator />

        <DialogFooter className="p-6 pt-4">
          <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
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

    {/*
      Deleting a saved antécédent is irreversible and hits the API immediately, so it gets the repo's standard
      destructive confirmation. It names the entry AND the patient: these rows are edited on a shared reception
      tablet with several files open in a day, and « êtes-vous sûr ? » cannot tell you that you are about to
      delete the wrong person's cardiac history.

      A plain destructive `Button` rather than `AlertDialogAction`, following `odontogram.tsx`'s own note: an
      `AlertDialogAction` closes on click, so a failed delete would dismiss the dialog and hide the reason.
    */}
    <AlertDialog
      open={pendingHistoryRemoval !== null}
      onOpenChange={(o) => { if (!o) setPendingHistoryRemoval(null) }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {pendingHistoryRemoval?.kind === "family"
              ? "Supprimer cet antécédent familial ?"
              : "Supprimer cet antécédent ?"}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {pendingHistoryRemoval && (
              <>
                «&nbsp;{pendingHistoryRemoval.label}&nbsp;» sera supprimé du dossier de{" "}
                <span className="font-medium text-foreground">
                  {`${firstName} ${lastName}`.trim() || "ce patient"}
                </span>
                . Cette action est irréversible.
              </>
            )}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={removingHistory}>Annuler</AlertDialogCancel>
          <Button
            variant="destructive"
            onClick={() => void confirmHistoryRemoval()}
            disabled={removingHistory}
          >
            {removingHistory ? "Suppression…" : "Supprimer"}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    {/*
      « Ce patient existe déjà » (create mode only).

      ⚠️ The irreversible option here is « Créer quand même », not the cancel — which is why it is the destructive
      one. A second file for the same person cannot be merged, cannot be deleted once anything is attached to it, and
      splits that patient's appointments, money and allergies for good; closing this dialog costs nothing.
    */}
    <AlertDialog
      open={duplicatePrompt !== null}
      onOpenChange={(o) => { if (!o) setDuplicatePrompt(null) }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Ce patient existe peut-être déjà</AlertDialogTitle>
          <AlertDialogDescription>{duplicatePrompt}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          {/* Leaves the form exactly as typed: the user goes to « Patients », finds the existing file and comes back
              — or corrects a name they mistyped. Never discard their input on a question. */}
          <AlertDialogCancel disabled={loading}>Revenir au formulaire</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              setDuplicatePrompt(null)
              allowDuplicateRef.current = true
              void savePatient()
            }}
            disabled={loading}
          >
            Créer quand même
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <DiscardChangesDialog guard={guard} />
    </>
  )
}

