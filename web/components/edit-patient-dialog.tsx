"use client"

import type React from "react"
import { useState, useEffect, useMemo, useRef } from "react"
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
import { CNAM_LIENS, CNAM_REGIMES } from "@/lib/cnam"
import {
  MAX_TOBACCO_PER_DAY,
  SMOKING_STATUSES,
  TOBACCO_UNITS,
  smokingStatusLabel,
  tobaccoUnitLabel,
} from "@/lib/tobacco"
import { Switch } from "@/components/ui/switch"
import { Separator } from "@/components/ui/separator"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
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
import { User, MapPin, Heart, Pill, CreditCard, Save, X, Plus, Trash2, StickyNote, AlertTriangle } from "lucide-react"
import { RecordSection } from "@/components/record/record-section"
import { cn } from "@/lib/utils"
import { patientsApi } from "@/lib/api/patients"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import type {
  PatientDto,
  PatientMedicalHistoryDto,
  PatientFamilyHistoryDto,
  ReminderConsent,
  SmokingStatus,
  TobaccoUnit,
  TobaccoUse,
} from "@/lib/api/types"
import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { isDeliverablePhone, PHONE_ERROR_FR, DEFAULT_REGION, regionOf } from "@/lib/phone"
import type { CountryCode } from "libphonenumber-js/max"
import { PhoneField } from "@/components/ui/phone-field"
import { formatAmount, formatDT, parseAmountInput, quoteFr, roundMillimes } from "@/lib/format"
import { CNAM_DENTAL_ALLOWANCE, CNAM_PLAFOND_SUPPLEMENTS, cnamBaseCeiling, cnamDefaultCeiling } from "@/lib/cnam"
import { SELECTABLE_GENDERS, genderLabel } from "@/components/appointment-labels"
import { handleHealthBulletKeyDown } from "@/lib/health-list"
import {
  DENTITIONS,
  DENTITION_BANDS_FR,
  DENTITION_SHORT_FR,
  ageFromBirthdate,
  dentitionFromBirthdate,
  dentitionFromAge,
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
  /**
   * Which block to unfold and scroll to on opening, or null for « as it comes ».
   *
   * ⚠️ It exists because the patient file's two summary panels each carry their own « Modifier »: a reader who
   * spots a wrong allergy presses the one beside it, and landing at the top of a form to hunt for the section
   * is the failure that makes such a button worse than no button. `"essentiel"` is not a `SectionKey` — that
   * block is never folded — so it scrolls and unfolds nothing.
   *
   * ⚠️ Typed as the anchor map's own keys, not `SectionKey`: only a block that HAS an id can be scrolled to, so
   * asking for « cnam » must be a compile error rather than a button that opens the form and goes nowhere.
   */
  focusSection?: PatientFormAnchor | null
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
  approximateAge: "Âge",
  dentition: "Denture",
  phone: "Numéro de téléphone",
  email: "E-mail",
  // ⚠️ `smokingPerDay` is the only OTHER key `validateForm` sets, and it was missing here — so when the tobacco
  // quantity was the sole refusal the banner printed « Corrigez « smokingPerDay » ci-dessous. » It also breaks
  // this map's old claim that every field it names lives in « L'essentiel »: this one is in « Informations
  // médicales », which is open on arrival, so the summary still names something on screen.
  smokingPerDay: "Tabac — quantité par jour",
}

/**
 * DOM ids the two summary panels on the patient file scroll this form to.
 *
 * ⚠️ A map rather than a template literal at each site, so a renamed key is a `tsc` error rather than a button
 * that opens the form and silently scrolls nowhere.
 */
const SECTION_ANCHOR = {
  essentiel: "patient-form-essentiel",
  medical: "patient-form-medical",
} as const

export type PatientFormAnchor = keyof typeof SECTION_ANCHOR

/** The four foldable sections of the patient form, in the order they appear. */
type SectionKey = "medical" | "notes" | "coordonnees" | "cnam"

/**
 * The opening state of each foldable section.
 *
 * ⚠️ **Two sections are folded on arrival and two are open, and the split is by SUBJECT, not by convenience.**
 * The rule this form has always been held to is that a folded section is a question the desk never sees, and
 * « Informations médicales » folded on a new patient is how a smoker, an allergy and a chronic condition go
 * unrecorded at the one moment somebody is sitting there answering. That still holds, so the two blocks carrying a
 * clinical question stay open.
 *
 * What is folded is the pair the practice itself describes as rarely filled — the postal address with the e-mail
 * and the reminder consent, and the CNAM identity. Both keep a summary that states what they hold, so folding
 * makes a value read-only rather than invisible.
 *
 * ⚠️ This is the second reversal on this line and the first one is worth keeping in view: every section used to be
 * open, which was itself a reversal of every section being folded. Neither extreme was the answer — the form was
 * simply too long, and the fix was to shorten it (33 controls over 28 rows became 29 over 11) rather than to hide
 * more or less of it.
 */
function defaultSections(): Record<SectionKey, boolean> {
  return { medical: true, notes: true, coordonnees: false, cnam: false }
}

export function EditPatientDialog({ open, onOpenChange, patient, onSuccess, focusSection }: EditPatientDialogProps) {
  // Personal Info State
  const [firstName, setFirstName] = useState("")
  const [lastName, setLastName] = useState("")
  const [gender, setGender] = useState("")
  const [birthdate, setBirthdate] = useState("")
  /**
   * Which of the two the desk is answering with — an exact date, or an age in years.
   *
   * <p>A patient who does not know their birthday is ordinary here, not a data-quality problem: a walk-in, an
   * elderly patient, a child brought in by a neighbour. Requiring the date meant reception typed something
   * plausible so the form would let them through, which is the same fabrication the server-side « thirty years
   * ago » default was retired for — except now it wears a human's authority.</p>
   *
   * <p>⚠️ The age is <b>never stored</b>. It seeds the denture (the one thing the date was actually being used
   * for at the desk) and nothing else; the patient keeps no date of birth, and every screen that shows one says
   * « âge inconnu » — which is true. Deriving « 01/01/1996 » from « 30 ans » would put a birthday nobody stated
   * onto a CNAM form.</p>
   */
  const [birthdateMode, setBirthdateMode] = useState<"date" | "age">("date")
  const [approximateAge, setApproximateAge] = useState("")
  /**
   * Which teeth this patient is charted on. Defaulted from the birthdate, but only until the user decides for
   * themselves — `dentitionTouched` is the same guard `create-appointment-dialog` uses for its duration: a derived
   * default that keeps re-deriving would overwrite the dentist's deliberate choice the moment they corrected a typo
   * in the date of birth.
   */
  const [dentition, setDentition] = useState<Dentition | null>(null)
  const [dentitionTouched, setDentitionTouched] = useState(false)
  const [phone, setPhone] = useState("")
  // The country each number is read against. Its own state, not derived per render: picking a country must
  // survive the next keystroke (see `PhoneField`). Seeded from the stored value on hydration below.
  const [phoneCountry, setPhoneCountry] = useState<CountryCode>(DEFAULT_REGION)
  const [email, setEmail] = useState("")
  /**
   * The whole address, on one line, exactly as the desk wants to write it.
   *
   * ⚠️ **This replaces four boxes — rue, gouvernorat, ville, code postal — and the fold is one-way.** Every
   * surface that displays an address already joins the parts with « , » (the patient file's `formatAddress`, the
   * summary modal, the lettre de liaison), so nothing on screen changes shape; what is lost is the
   * *decomposition*, which only the CSV export and the dossier archive still read as separate columns. Those go
   * empty for any patient saved through this form from now on.
   *
   * The alternative — keeping the three hidden and editing only the street — was worse in the way that matters:
   * the desk could see « La Marsa » in the field and be unable to correct it.
   *
   * On the wire it is `Address.street`, and the other three are sent blank. `Address.OfAny` refuses only an
   * address with *no* side at all, so one filled line is a valid address and an empty box clears the record.
   */
  const [addressLine, setAddressLine] = useState("")
  // « Adressé par » — the referring practitioner. Optional, free text (usually a doctor outside this clinic).
  const [referredBy, setReferredBy] = useState("")
  const [reminderConsent, setReminderConsent] = useState<ReminderConsent>("NotRecorded")

  // Who took the answer and when. Shown rather than hidden because a consent nobody can date is one the cabinet
  // cannot defend — and it is read from the SAVED patient, never from the control above, so it keeps describing
  // the stored answer while somebody is still changing their mind about the new one.
  const consentRecordedLabel = (() => {
    if (!patient?.reminderConsentRecordedAtUtc) return null
    const on = new Date(patient.reminderConsentRecordedAtUtc)
    if (Number.isNaN(on.getTime())) return null
    const when = on.toLocaleDateString("fr-FR")
    const by = patient.reminderConsentRecordedBy
    return by ? `Réponse enregistrée le ${when} par ${by}.` : `Réponse enregistrée le ${when}.`
  })()

  // Patient-level notes. Distinct from a fiche de soins' notes, which describe one séance: these are what the
  // dentist wants back in front of them on every visit, which is why the section leads the form.
  const [patientNotes, setPatientNotes] = useState("")
  const [patientImportantNotes, setPatientImportantNotes] = useState("")

  // Medical Info State
  //
  // ⚠️ `chronicDiseases` is the state name for the column the wire calls `medicalHistory` and the interface now
  // calls « Maladies » — three names for one value, and the middle one is the wire's so it cannot move. The label
  // was « Maladies chroniques / affections », which was three words for one column and disagreed with what the
  // patient's file and the shared alert panel each called it.
  const [chronicDiseases, setChronicDiseases] = useState("")
  const [allergies, setAllergies] = useState("")
  // « Médicaments » — new. It had nowhere to live, so practices wrote it into « Notes importantes », where it is
  // prose rather than a field and reaches no document and no structured read.
  const [medications, setMedications] = useState("")
  
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
  /** « Motif de consultation » — free text, right under the two names. */
  const [consultationReason, setConsultationReason] = useState("")

  /**
   * « Tabac ». ⚠️ `null` is « personne n'a posé la question » and is the initial value on both paths — seeding
   * `"NonSmoker"` would make a form nobody filled in indistinguishable from an answer somebody gave, the same
   * fabrication the sexe and the date de naissance were made optional to stop.
   */
  const [smokingStatus, setSmokingStatus] = useState<SmokingStatus | null>(null)
  const [smokingPerDay, setSmokingPerDay] = useState("")
  const [smokingUnit, setSmokingUnit] = useState<TobaccoUnit>("Cigarettes")

  // CNAM identity (optional — pre-fills the Bulletin de soins BS1).
  const [cnam, setCnam] = useState({
    identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "",
    assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "",
    // L10 — the two inputs to the annual ceiling. Strings like every other field here: they are form inputs,
    // parsed once on submit, so a half-typed « 1 1 » never becomes NaN in state.
    dependantCount: "", annualCeilingOverride: "",
  })

  // Flags State

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
   * Which sections are unfolded. ⚠️ **Every section is open on arrival, on BOTH paths** — a folded section is a
   * question the desk never sees, and « Informations médicales » folded on a new patient is how a smoker, an
   * allergy and a chronic condition go unrecorded at the one moment somebody is sitting there answering.
   *
   * ⚠️ This is a **reversal**, and the argument it overturns is worth keeping: the sections were folded on create
   * so « Enregistrer » would not sit under eleven CNAM fields nobody registering a walk-in was going to touch.
   * That cost is real and is paid deliberately — the two sections that made it heavy have since been retired
   * (assurance, signalements), the form is shorter, and the footer is sticky, so the button is reachable at any
   * scroll position. Everything stays foldable; only the default moved.
   */
  const [openSections, setOpenSections] = useState<Record<SectionKey, boolean>>(defaultSections)

  const toggleSection = (key: SectionKey) =>
    setOpenSections((current) => ({ ...current, [key]: !current[key] }))

  /**
   * What each folded section says about itself. ⚠️ **A folded section must never read as an empty one**: the
   * summary states what it holds, so folding makes a value read-only rather than invisible — the rule
   * `RecordSection` was built on. « À renseigner » is deliberately not « — »: one is an invitation, the other is
   * a claim that there is nothing to say.
   */
  const filled = (...values: (string | null | undefined)[]) => values.filter((v) => v && v.trim()).length

  /**
   * The « Tabac » block as the wire wants it, or `null` for « personne n'a posé la question ».
   *
   * One builder for both save paths — the create path omits a null and the update path sends it, which is the
   * only difference between them, and it is expressed at the two call sites rather than duplicated here.
   */
  const tobaccoPayload = (): TobaccoUse | null => {
    if (smokingStatus === null) return null

    if (smokingStatus !== "Smoker") return { status: smokingStatus }

    const perDay = Number(smokingPerDay.trim())
    return smokingPerDay.trim() && Number.isFinite(perDay)
      ? { status: smokingStatus, perDay, unit: smokingUnit }
      : { status: smokingStatus }
  }

  const sectionSummary: Record<SectionKey, string> = {
    notes:
      filled(patientImportantNotes, patientNotes) > 0
        ? [patientImportantNotes.trim() && "alertes", patientNotes.trim() && "notes"].filter(Boolean).join(" · ")
        : "aucune note",
    // ⚠️ This section is FOLDED on arrival, so its summary is the only thing most users will ever read of it —
    // and it has to be true about all three of its fields. « non renseignés » on the rappels is deliberately
    // spelled out rather than omitted: an unrecorded consent still SENDS, which is the half people misread.
    coordonnees:
      [
        addressLine.trim(),
        email.trim(),
        reminderConsent === "Granted"
          ? "rappels acceptés"
          : reminderConsent === "Refused"
            ? "rappels refusés"
            : null,
      ]
        .filter(Boolean)
        .join(" · ") || "adresse et rappels non renseignés",
    medical:
      filled(chronicDiseases, allergies, medications) > 0 || smokingStatus !== null
        ? [
            allergies.trim() && "allergies",
            chronicDiseases.trim() && "maladies",
            medications.trim() && "médicaments",
            // The label itself, not the word « tabac »: « Fumeur » is the fact worth reading on a folded section.
            smokingStatus !== null && smokingStatusLabel(smokingStatus).toLowerCase(),
          ]
            .filter(Boolean)
            .join(" · ")
        : "aucune information",
    cnam: cnam.identifiantUnique.trim() || (cnam.regime.trim() ? cnam.regime.trim() : "aucun identifiant"),
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
      // ⚠️ Re-set on every open, not only on mount: this component is reused for both modes from the same
      // parent, so a section somebody folded on the previous patient would stay folded on the next one —
      // and the initial state alone cannot reach a second opening.
      // ⚠️ Applied ON TOP of the defaults, never instead of them: a caller asking for « médical » must not
      // also fold « notes », which the default opens.
      setOpenSections(
        focusSection && focusSection !== "essentiel"
          ? { ...defaultSections(), [focusSection]: true }
          : defaultSections(),
      )
      if (patient) {
        // Edit mode: populate with existing patient data
      setFirstName(patient.firstName || "")
      setLastName(patient.lastName || "")
      setGender(patient.gender || "")
      setBirthdate(patient.dateOfBirth ? patient.dateOfBirth.split('T')[0] : "")
      // A stored patient is always shown on the date tab — the age was never persisted, so there is nothing to
      // restore, and reopening on « Âge » would present an empty box beside a date the record actually holds.
      setBirthdateMode("date")
      setApproximateAge("")
      // A stored patient already has an answer; treat it as the user's own so the age rule never overrides it.
      setDentition((patient.dentition as Dentition) || null)
      setDentitionTouched(true)
      setPhone(patient.phoneNumber || "")
      // A stored number re-opens its own country, so the control never contradicts the field beside it. Falls
      // back to the default rather than to nothing: an unparseable legacy value has no country to show.
      setPhoneCountry(regionOf(patient.phoneNumber) ?? DEFAULT_REGION)
      setEmail(patient.email || "")
      
      // ⚠️ Folded, not truncated. A stored « 12 rue de Carthage / Tunis / La Marsa / 2070 » must come back into
      // the one box in FULL, or opening this form and pressing « Enregistrer » would quietly drop three quarters
      // of the address — the exact silent-drop shape `Address.OfAny` was written to end. Same join and same
      // separator as every surface that already displays one.
      // ⚠️ **Adjacent duplicates are dropped**, the same way the patient file's `formatAddress` does it: a
      // patient in Ariana has « Ariana » as both ville and gouvernorat, and a naive join reads « Ariana,
      // Ariana » — which looks like a bug in the record rather than a true statement about Tunisian
      // administrative naming. Adjacent only, and case-insensitively: two identical parts far apart in a long
      // address are far likelier to be real than a typo.
      setAddressLine(
        [patient.address?.street, patient.address?.city, patient.address?.state, patient.address?.zipCode]
          .map((part) => part?.trim())
          .filter((part): part is string => Boolean(part))
          .filter((part, index, all) => index === 0 || part.toLowerCase() !== all[index - 1].toLowerCase())
          .join(", "),
      )

      // « Adressé par »
      setReferredBy(patient.referredBy || "")
      setReminderConsent(patient.reminderConsent ?? "NotRecorded")

      // Patient-level notes
      setPatientNotes(patient.notes || "")
      setPatientImportantNotes(patient.importantNotes || "")

      // « Motif de consultation » — why they came in the first place.
      setConsultationReason(patient.consultationReason || "")

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
      setMedications(patient.medications || "")
      setChronicDiseases(patient.medicalHistory || "")
      
      // « Tabac ». A null block is « jamais renseigné » — never seeded as « Non-fumeur ».
      setSmokingStatus(patient.tobaccoUse?.status ?? null)
      setSmokingPerDay(patient.tobaccoUse?.perDay != null ? String(patient.tobaccoUse.perDay) : "")
      setSmokingUnit(patient.tobaccoUse?.unit ?? "Cigarettes")

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
        setBirthdateMode("date")
        setApproximateAge("")
        setDentition(null)
        setDentitionTouched(false)
        setPhone("")
        setPhoneCountry(DEFAULT_REGION)
        setEmail("")
        setAddressLine("")
        setReferredBy("")
        setPatientNotes("")
        setPatientImportantNotes("")
        setChronicDiseases("")
        setAllergies("")
        setMedications("")
        setConsultationReason("")
        setCnam({ identifiantUnique: "", regime: "", assureFirstName: "", assureLastName: "", assureAddress: "", assurePostalCode: "", maladeLien: "", maladeLienRang: "", dependantCount: "", annualCeilingOverride: "" })
        // ⚠️ `null`, not `"NonSmoker"` — a fresh form has asked nobody anything.
        setSmokingStatus(null)
        setSmokingPerDay("")
        setSmokingUnit("Cigarettes")
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

  // Pre-select the dentition from whichever of the two the desk answered with, until the user says otherwise.
  // Derived in an effect rather than inside an onChange so it also fires for a date typed, pasted or picked from
  // the native calendar — and so switching between « Date exacte » and « Âge » re-derives from the live field.
  useEffect(() => {
    if (dentitionTouched) return
    setDentition(birthdateMode === "age" ? dentitionFromAge(approximateAge) : dentitionFromBirthdate(birthdate))
  }, [birthdate, approximateAge, birthdateMode, dentitionTouched])

  /**
   * The age a typed date of birth makes today, printed beside the « Naissance » label.
   *
   * ⚠️ Derived on render, never held in state and never seeded by an effect — it is a pure function of the date
   * beside it, and a second copy in state is a second thing that can disagree with the field. `null` whenever the
   * box is empty or unparseable, in which case nothing is printed at all rather than « 0 ans ».
   *
   * ⚠️ Only in `date` mode. In `age` mode the box IS the age, so repeating it beside the label would be the same
   * number twice.
   */
  /**
   * Scroll the requested block into view once the dialog has actually painted.
   *
   * ⚠️ **Two frames, not one, and not zero.** Radix mounts `DialogContent` in a portal and runs its own entry
   * animation, so on the tick `open` flips the scroller has no height and `scrollIntoView` is a no-op — the same
   * shape as the patient page's own deferred tab scroll. `block: "start"` rather than `"center"`, because the
   * point is to put the section's HEADING at the top of the scroller, and centring a short block leaves its
   * title above the fold.
   *
   * ⚠️ `behavior: "auto"`: a smooth scroll on opening reads as the dialog sliding out from under the reader, and
   * `prefers-reduced-motion` would have to be honoured by hand.
   */
  useEffect(() => {
    if (!open || !focusSection) return
    let raf2 = 0
    const raf1 = requestAnimationFrame(() => {
      raf2 = requestAnimationFrame(() => {
        document.getElementById(SECTION_ANCHOR[focusSection])?.scrollIntoView({ block: "start", behavior: "auto" })
      })
    })
    return () => {
      cancelAnimationFrame(raf1)
      cancelAnimationFrame(raf2)
    }
  }, [open, focusSection])

  const derivedAge = useMemo(
    () => (birthdateMode === "date" ? ageFromBirthdate(birthdate) : null),
    [birthdate, birthdateMode],
  )

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

    /*
     * Neither the sexe nor the date of birth is required any more, and both used to be.
     *
     * ⚠️ A required field the desk cannot answer is not a data-quality guarantee — it is a **fabrication
     * generator**. « Sexe » was refused on a walk-in whose file is a name and a phone number, and « Date de
     * naissance » on the patient who does not know it, so reception picked something and moved on. The record
     * then said M or 01/01/1980 with no way to tell it apart from an answer somebody actually gave. Blank is a
     * true statement and the phone number, which the same audit made optional for exactly this reason, is the
     * shape being followed.
     *
     * What IS still refused is an age that is not a number: an unreadable answer must not silently become no
     * answer, because it is the only thing the denture is derived from.
     */
    if (birthdateMode === "age" && approximateAge.trim()) {
      const years = Number(approximateAge)
      if (!Number.isFinite(years) || !Number.isInteger(years) || years < 0 || years > 120) {
        newErrors.approximateAge = "Saisissez un âge en années (0 à 120)"
      }
    }

    /*
     * ⚠️ **La denture is no longer required either, and it was the last blocking field beyond the two names.**
     *
     * It was kept mandatory because « it decides which chart every future séance is recorded on, and there is no
     * neutral value » — the first half is true and the second is what made requiring it pointless. `DentitionType`
     * has exactly `Child` and `Adult`, so a desk that does not know is forced to *pick* one, and a guessed
     * « Adulte » is indistinguishable from an answered one. That is the same fabrication the sexe and the date de
     * naissance were made optional to stop, one field further along.
     *
     * Nothing downstream breaks, and this is why: `CreatePatientCommand.Dentition` is already optional **on the
     * wire**, an absent value falls through `DentitionRules.Parse` to `FromDateOfBirth`, and that answers **null**
     * with no date of birth rather than assuming — so nothing is asserted. The entity keeps `Adult` as a storage
     * default for a NOT NULL column, and the odontogram **does not trust it blindly**: with no date of birth it
     * asks (AC-18). So « unanswered » stays a question the chart puts to the dentist, instead of a claim the form
     * put in the record.
     */

    // Optional, in both modes: `Patients.PhoneNumber` is nullable and a walk-in, a child or an elderly patient
    // is routinely registered with a name alone. Requiring it here refused that record outright and pushed
    // reception into typing a fake number — the sentinel problem (`0000000000`) the backend deliberately retired.
    // The consequence is stated instead, under the field. A number that IS given must still be deliverable.
    // Any country now, resolved against whatever the country control says. The emergency contact below is
    // deliberately NOT validated — nothing dispatches to it and « 71 555 (bureau) » is a real value a relative
    // gives, so refusing it would cost the record to protect a field nobody sends to.
    if (phone.trim() && !isDeliverablePhone(phone.trim(), phoneCountry)) {
      newErrors.phone = PHONE_ERROR_FR
    }

    if (email && !validateEmail(email)) {
      newErrors.email = "Adresse e-mail invalide"
    }

    /*
     * A typed quantity that is not a number is refused; a blank one is not.
     *
     * « Fumeur, quantité non dite » is a real answer at a desk, so the field is optional — but an unreadable
     * answer must not silently become no answer, the same rule the approximate age above follows. The ceiling
     * mirrors `TobaccoUse.MaxPerDay`, so the server's refusal and this one cannot disagree.
     */
    if (smokingStatus === "Smoker" && smokingPerDay.trim()) {
      const perDay = Number(smokingPerDay)
      if (!Number.isFinite(perDay) || !Number.isInteger(perDay) || perDay < 1 || perDay > MAX_TOBACCO_PER_DAY) {
        newErrors.smokingPerDay = `Indiquez une quantité entre 1 et ${MAX_TOBACCO_PER_DAY} par jour`
      }
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
        description: "Vérifiez que tous les champs obligatoires sont correctement remplis.",
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
        addressLine.trim()
          ? {
              street: addressLine.trim(),
              city: "",
              state: "",
              zipCode: "",
              country: "Tunisia", // Default country, can be made configurable
            }
          : null

      let savedPatient: PatientDto

      if (patient) {
        // Edit mode: Update existing patient
        const updateData: Partial<PatientDto> = {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          // "Unknown" when unanswered, never "" — the same value the create path sends, so a patient registered
          // without a sexe and one edited to remove it end up on the same stored value rather than two.
          gender: gender || "Unknown",
          dentition: dentition ?? undefined,
          // Explicit null, not undefined: since the field became optional there has to be a request that *clears*
          // a stored date, and `undefined` is dropped by JSON.stringify and read as "leave it alone". The command
          // learned the same tri-state the address and the contact fields already had.
          dateOfBirth: birthdate || null,
          // Explicit null, not undefined: the command is tri-state, so undefined would be read as
          // "leave it alone" and clearing the box would silently do nothing.
          phoneNumber: phone.trim() || null,
          email: email.trim() || null,
          // The row's version as last read from the server — so a peer's save in the meantime is a 409, not a
          // silent overwrite of their work, and our own previous save is not mistaken for one.
          version: freshPatient?.version ?? patient.version,
          address: addressObj,
          // Always present (possibly ""), so emptying the box clears the stored value instead of
          // reading as "leave it alone" — same reason as the two contact fields above.
          referredBy: referredBy.trim(),
          // Always sent, like the fields above: the control has three explicit states and « non renseigné » is
          // one of them, so omitting it would make un-recording an answer impossible.
          reminderConsent,
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
          medications: medications.trim(),
          // Always present (possibly ""), so clearing the box clears the stored motif — the same reason
          // `notes`/`importantNotes` above are sent that way.
          consultationReason: consultationReason.trim(),
          // ⚠️ Always present, **including `null`**. The key is tri-state server-side: an omitted one leaves the
          // stored answer alone, so `undefined` would make un-recording « Tabac » impossible — the L1b defect.
          tobaccoUse: tobaccoPayload(),
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
        }

        /*
         * The **server's** patient, not `{ ...patient, ...updateData }`.
         *
         * The local spread was the second half of the L1b defect: it echoed the request back as though it had been
         * accepted, so the UI could show a state the server never stored (a cleared allergy the old payload never
         * asked it to clear looked cleared until the next refetch). It also could not be correct in principle — the
         * spread carries request-shaped keys (a raw `dentition`, for one) over a DTO, and knows
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
          description: "Les modifications ont été enregistrées.",
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
          medications: medications.trim() || undefined,
          address: addressObj,
          referredBy: referredBy.trim() || undefined,
          // Omitted at « non renseigné » so a new patient is created with an honest « nobody has asked »
          // rather than a recorded answer with today's date on it.
          reminderConsent: reminderConsent === "NotRecorded" ? undefined : reminderConsent,
          notes: patientNotes.trim() || undefined,
          importantNotes: patientImportantNotes.trim() || undefined,
          consultationReason: consultationReason.trim() || undefined,
          // `?? undefined`, not `null`: on create there is nothing stored to clear, so an unanswered « Tabac »
          // is simply omitted.
          tobaccoUse: tobaccoPayload() ?? undefined,
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
          // Absent on the first attempt, so the server checks whether this person is already on file. Only the
          // « Créer quand même » confirmation sets it — see the AlertDialog at the bottom of this file.
          allowDuplicate: allowDuplicateRef.current || undefined,
        })

        toast.success("Patient créé", {
          description: "Le dossier a été ajouté à la liste des patients.",
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
        toast.error(patient ? "La mise à jour a échoué" : "La création a échoué", {
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
                  ? "Mettez à jour toutes les informations du patient, y compris les antécédents médicaux"
                  : "Saisissez les informations du patient pour créer un nouveau dossier"}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <Separator />

        {/* Was `max-h-[calc(90vh-200px)]` — a magic number that guessed the chrome's height, and guessed in
            `vh`, so the keyboard opening pushed the footer off screen (AC-25). `DialogBody` takes whatever is
            left instead of subtracting an assumed 200 px. */}
        <DialogBody>
          <form onSubmit={handleSave} className="p-6 space-y-6">
            {/*
              ⚠️ **A 409 here MUST carry the « Recharger » its own sentence tells the user to press.** This banner
              rendered without an `action` — the only dialog in the app that round-trips a version and did — so
              the server's « cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie » named
              a remedy the screen did not offer. That is the documented amplifier of the concurrency trap, and it
              is expensive: the version this form holds never moves on its own, so every later click repeats the
              refusal (measured in production at six over 81 minutes, until the user reloaded the page by hand).

              ⚠️ **It re-reads by closing, and must not merely `resync()`.** `useFreshVersion` deliberately takes
              the VERSION alone and never the field values, so resyncing in place would hand this form a fresh
              token while it still holds what the user typed over the colleague's edit — the next save would then
              succeed and silently overwrite them, which is worse than the refusal. Re-opening is what re-reads
              the row. Same shape as `expense-form-dialog` and `dental-act-form-modal`.

              ⚠️ The RAW `onOpenChange`, never `guard.onOpenChange`: the dirty guard would ask whether to discard
              the entries, and « voulez-vous abandonner ? » immediately after pressing « Recharger » is a second
              question about a decision already taken.

              ⚠️ **The fiche de soins recovers differently on purpose, and the difference is not an inconsistency.**
              Its « Recharger » re-reads the record's version *and* both documents in place, because what is stale
              there is a document's CONTENT and the server's sentence promises to show it. This form has no such
              thing to re-read — every value in it is the patient row the conflict is about — so re-opening IS the
              re-read, and doing it in place would be the silent overwrite described above wearing a button.
            */}
            <FormErrorBanner
              message={conflict.error}
              action={
                conflict.isConflict
                  ? {
                      label: "Recharger",
                      onClick: () => {
                        onSuccess?.()
                        onOpenChange(false)
                      },
                      disabled: loading,
                    }
                  : undefined
              }
            />
            {/* ⚠️ A summary as well as the per-field messages, because on a form this long the first refusal can
                be off screen — and on a phone it always is. `FormErrorBanner` is the shared aria-live region, so
                this announces too. It names the fields; the fields themselves carry the reason. */}
            <FormErrorBanner
              message={
                Object.keys(errors).length > 0
                  ? Object.keys(errors).length === 1
                    ? `Corrigez ${quoteFr(FIELD_LABELS_FR[Object.keys(errors)[0]] ?? Object.keys(errors)[0])} ci-dessous.`
                    : `Corrigez ces champs ci-dessous : ${Object.keys(errors)
                        .map((field) => FIELD_LABELS_FR[field] ?? field)
                        .join(", ")}.`
                  : null
              }
            />

            {/* Personal Information Section */}
            <div className="space-y-4" id={SECTION_ANCHOR.essentiel}>
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
                  Autofill tokens run across the identity fields below. This form is filled at a reception desk on a
                  shared tablet, and inputs with no `autocomplete` mean the browser can offer nothing at all.

                  ⚠️ Deliberately NOT on the CNAM assuré: those fields describe a *different* person from the
                  patient, so a `tel`/`postal-code` suggestion there would be a confidently wrong answer written
                  into a clinical record.

                  ⚠️ **No « (facultatif) » markers inside this box, and that is deliberate.** The heading already
                  says « suffit à enregistrer le patient » and exactly two fields carry an asterisk; six repetitions
                  of the same word are then noise that costs horizontal room in a three-up row. The markers stay on
                  the folded sections, where no such heading is in view. Where an empty field has a real
                  *consequence* — the telephone — that consequence is still stated, which is the half that matters.
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
                    autoComplete="family-name"
                    aria-invalid={!!errors.lastName}
                    className={cn(errors.lastName && "border-destructive")}
                  />
                  {errors.lastName && <p className="text-sm text-destructive">{errors.lastName}</p>}
                </div>

                {/*
                  « Motif de consultation » and « Adressé par » — the second row, side by side, directly under the
                  two names.

                  ⚠️ **They used to be the third control and the tenth**, the second of them at the very bottom of
                  the block under CNAM-adjacent fields nobody scrolls to. Both are what the practice actually asks
                  at the desk (« pourquoi venez-vous ? », « qui vous envoie ? »), both are read beside the patient's
                  name on their own page, and the pair costs one row rather than two full-width ones.

                  ⚠️ `Input`s, not `Textarea`s: each renders on one line beside the name on the patient page, so the
                  control's shape states the length expected rather than inviting a paragraph the page will clamp.
                */}
                <div className="space-y-2">
                  <Label htmlFor="consultationReason">Motif de consultation</Label>
                  <Input
                    id="consultationReason"
                    value={consultationReason}
                    onChange={(e) => setConsultationReason(e.target.value)}
                    placeholder="Douleur 36, contrôle, suivi ortho…"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="referredBy">Adressé par</Label>
                  <Input
                    id="referredBy"
                    value={referredBy}
                    onChange={(e) => setReferredBy(e.target.value)}
                    placeholder="Dr Ben Salah, Sfax — vide si le patient vient de lui-même"
                  />
                </div>

                {/*
                  Téléphone · Sexe · Naissance — one row of three.

                  ⚠️ **`md:items-start` plus a `min-h-8` label box on all three, and both halves are load-bearing.**
                  The naissance label carries a segmented control and the other two carry plain text, so without a
                  shared label height its input starts ~12 px below its neighbours' and the three boxes visibly stop
                  lining up. `items-start` then keeps that alignment when one column grows a message the others do
                  not have (the phone's « ni rappel ni relance », a validation error).

                  ⚠️ The labels are short — « Téléphone », « Naissance », and « Date » / « Âge » on the switch —
                  because at this width « Date de naissance » beside the switch wraps to a second line, which is the
                  row this arrangement exists to avoid.
                */}
                <div className="grid grid-cols-1 gap-4 md:col-span-2 md:grid-cols-3 md:items-start">
                  {/* Phone */}
                  <div className="space-y-2">
                    <div className="flex min-h-8 items-center">
                      <Label htmlFor="phone">Téléphone</Label>
                    </div>
                    <PhoneField
                      id="phone"
                      value={phone}
                      onChange={(next) => {
                        setPhone(next)
                        // ⚠️ The error MUST clear on change. Only `birthdate`/`approximateAge` were cleared
                        // imperatively, so a corrected number kept its red border and its message until the next
                        // submit — tolerable beside a plain input, and with a country control next to it it reads
                        // as a broken control the user is fighting.
                        if (errors.phone) {
                          setErrors((prev) => {
                            const rest = { ...prev }
                            delete rest.phone
                            return rest
                          })
                        }
                      }}
                      country={phoneCountry}
                      onCountryChange={setPhoneCountry}
                      invalid={!!errors.phone}
                    />
                    {errors.phone && <p className="text-sm text-destructive">{errors.phone}</p>}
                    {/* Optional does not mean consequence-free. Saying it here beats a neutral blank the user
                        only understands weeks later, when the patient misses an appointment. */}
                    {!phone.trim() && !errors.phone && (
                      <p className="text-xs text-muted-foreground">
                        Sans numéro, ce patient ne recevra ni rappel ni relance.
                      </p>
                    )}
                  </div>

                  {/* Gender */}
                  <div className="space-y-2">
                    <div className="flex min-h-8 items-center">
                      <Label htmlFor="gender">Sexe</Label>
                    </div>
                    <Select value={gender} onValueChange={setGender}>
                      <SelectTrigger id="gender" className={cn("w-full", errors.gender && "border-destructive")}>
                        <SelectValue placeholder="Non précisé" />
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

                  {/*
                    Date of birth — or, when the patient does not know it, an age.

                    ⚠️ The two are one field with two ways of answering, not two fields. Offering an « Âge » box
                    beside a « Date de naissance » box invites both to be filled with statements that disagree, and
                    nothing on the record could then say which one the desk meant. The segmented control makes the
                    choice explicit and leaves exactly one answer on screen.
                  */}
                  <div className="space-y-2">
                    <div className="flex min-h-8 items-center justify-between gap-2">
                      <Label htmlFor={birthdateMode === "age" ? "approximate-age" : "birthdate"}>
                        Naissance
                        {/* The derived age, beside the date rather than on a help line under it: one fewer row,
                            and it lands where the reader is already looking. It states what IS — the age this
                            date makes today — and never what the denture will be, which the control below says
                            for itself. */}
                        {birthdateMode === "date" && derivedAge !== null && (
                          <span className="ms-1.5 text-xs font-normal text-muted-foreground">· {derivedAge} ans</span>
                        )}
                      </Label>
                      {/* The switch itself. `role="radiogroup"` and not two independent toggles: they are the two
                          answers to one question, and a screen reader has to hear it that way. */}
                      <div
                        role="radiogroup"
                        aria-label="Renseigner la naissance par"
                        className="flex shrink-0 items-center gap-1 rounded-md border bg-muted/40 p-0.5"
                      >
                        {([
                          ["date", "Date"],
                          ["age", "Âge"],
                        ] as const).map(([value, label]) => (
                          <button
                            key={value}
                            type="button"
                            role="radio"
                            aria-checked={birthdateMode === value}
                            onClick={() => {
                              setBirthdateMode(value)
                              // Switching answers clears the other box, so the record can never carry a date the
                              // desk has just said it does not have — nor an age beside a date it does.
                              if (value === "age") setBirthdate("")
                              else setApproximateAge("")
                              setErrors((prev) => {
                                const next = { ...prev }
                                delete next.birthdate
                                delete next.approximateAge
                                return next
                              })
                            }}
                            className={cn(
                              "touch-target rounded px-2 py-1 text-xs font-medium transition-colors",
                              birthdateMode === value
                                ? "bg-background text-foreground shadow-sm"
                                : "text-muted-foreground hover-hover:hover:text-foreground",
                            )}
                          >
                            {label}
                          </button>
                        ))}
                      </div>
                    </div>

                    {birthdateMode === "date" ? (
                      <Input
                        id="birthdate"
                        type="date"
                        value={birthdate}
                        onChange={(e) => setBirthdate(e.target.value)}
                        aria-invalid={!!errors.birthdate}
                        className={cn(errors.birthdate && "border-destructive")}
                      />
                    ) : (
                      <>
                        <div className="flex items-center gap-2">
                          <Input
                            id="approximate-age"
                            type="number"
                            inputMode="numeric"
                            min={0}
                            max={120}
                            step={1}
                            value={approximateAge}
                            onChange={(e) => setApproximateAge(e.target.value)}
                            placeholder="Ex. 42"
                            aria-invalid={!!errors.approximateAge}
                            className={cn("w-24", errors.approximateAge && "border-destructive")}
                          />
                          <span className="text-sm text-muted-foreground">ans</span>
                        </div>
                        {/* Said where the consequence is. The age is a form input: it picks the denture below and
                            is then discarded, so the fiche will read « âge inconnu » — which is the truth, and much
                            better than a birthday nobody stated printed on a bulletin CNAM. */}
                        <p className="text-xs text-muted-foreground">
                          Aucune date de naissance ne sera enregistrée.
                        </p>
                      </>
                    )}
                    {errors.birthdate && <p className="text-sm text-destructive">{errors.birthdate}</p>}
                    {errors.approximateAge && <p className="text-sm text-destructive">{errors.approximateAge}</p>}
                  </div>
                </div>

                {/*
                  Denture — asked once, here, because it is a property of the patient and not of a visit.

                  It replaces two toggles that asked the same question about the same patient every time anyone
                  opened the odontogram or the fiche editor, plus a per-fiche badge in the actes dentaires table.
                  Pre-selected from the age so the common case is already right; changeable because the age rule is
                  a heuristic and a growing child has to be switchable.

                  ⚠️ **Three options now, and each carries its own age band as a caption.** The band is the rule, and
                  printed inside the control it is read — the sentence « Proposé d'après l'âge. » that used to sit
                  under the group was not, and it stated the mechanism rather than the threshold. It also replaces
                  it: the « Déduit de l'âge » chip above says the same thing where it belongs, beside the answer it
                  qualifies, and disappears the moment the dentist chooses for themselves.

                  ⚠️ The captions use `DENTITION_SHORT_FR`, not the full labels: inside a control already named
                  « Denture », three buttons each beginning with the word « Denture » is the word three times over
                  and costs the row its width. The full labels are what the patient's own file prints.
                */}
                <div className="space-y-2 md:col-span-2">
                  <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
                    <Label htmlFor={`dentition-${DENTITIONS[0]}`}>Denture</Label>
                    {!dentitionTouched && dentition && (
                      <span className="rounded-full bg-accent px-2.5 py-0.5 text-xs font-medium text-accent-foreground">
                        Déduit de l&apos;âge
                      </span>
                    )}
                  </div>
                  <div role="radiogroup" aria-label="Denture" className="flex flex-col gap-2 sm:flex-row">
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
                            birthdate has been typed yet and therefore no option is pre-selected from the age — the
                            field rendered as greyed-out boxes that read as disabled inputs.

                            The marker: a real radio dot. `bg-card` alone was not enough, because the geometry here
                            is an input's — bordered, left-aligned text — so once it went white it read as a *text
                            field* instead. With no option chosen, the hollow circles are also the only thing on
                            screen saying an answer is still owed.
                          */
                          className={cn(
                            "flex flex-1 items-center gap-2.5 rounded-md border px-3 py-2 text-left transition-colors duration-150 ease-out motion-reduce:transition-none",
                            selected
                              ? "border-primary bg-primary/10 text-foreground"
                              : "bg-card text-foreground hover-hover:hover:bg-muted/60",
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
                          <span className="min-w-0">
                            <span className="block text-sm font-medium">{DENTITION_SHORT_FR[value]}</span>
                            <span
                              className={cn(
                                "block text-xs",
                                selected ? "text-primary" : "text-muted-foreground",
                              )}
                            >
                              {DENTITION_BANDS_FR[value]}
                            </span>
                          </span>
                        </button>
                      )
                    })}
                  </div>
                  <p className="text-xs text-muted-foreground">
                    Détermine les dents affichées dans l&apos;odontogramme et les fiches de soins.
                  </p>
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
              anchorId={SECTION_ANCHOR.medical}
              size="md"
              icon={<Heart className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Informations médicales"
              summary={sectionSummary.medical}
              open={openSections.medical}
              onToggle={() => toggleSection("medical")}
            >

              <div className="grid grid-cols-1 gap-4">
                {/*
                  Allergies · Maladies · Médicaments — one row of three.

                  ⚠️ **They are three lists of the same kind and they are read as a set**, which is why they are
                  now side by side rather than stacked three deep: it is the same question asked three ways
                  (« qu'est-ce qui pourrait mal se passer ? »), and the answer is checked in one glance before an
                  injection rather than by scrolling.

                  ⚠️ **Only the allergies are tinted**, and the restraint is the point. It is the one of the three
                  that changes what may be injected in the next five minutes; three coloured panels side by side
                  and none of them alerts. Same reasoning as `act-card`'s single amber field.
                */}
                <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                  <div className="space-y-2 rounded-md border border-destructive/30 bg-destructive-wash p-3">
                    <Label htmlFor="allergies" className="flex items-center gap-1.5 font-semibold text-destructive">
                      <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      Allergies
                    </Label>
                    {/* ⚠️ Enter opens the next bullet — see `handleHealthBulletKeyDown`. The placeholder shows
                        the shape rather than describing it, which is why it is two bulleted lines and not a
                        sentence explaining that Enter makes a list. */}
                    <Textarea
                      id="allergies"
                      value={allergies}
                      onChange={(e) => setAllergies(e.target.value)}
                      onKeyDown={(e) => handleHealthBulletKeyDown(e, setAllergies)}
                      placeholder={"• Pénicilline\n• Fruits de mer"}
                      className="min-h-[66px] resize-none border-destructive/25 bg-card [&::placeholder]:whitespace-pre-line"
                    />
                  </div>

                  {/* ⚠️ « Maladies », not « Maladies chroniques / affections ». One column had three names across
                      the product — this label, « Antécédents » in the shared alert panel, and a third list called
                      « Antécédents médicaux » that is a different table entirely. Chronic or passing, it is the
                      same list to the person writing it. */}
                  <div className="space-y-2">
                    <Label htmlFor="chronicDiseases">Maladies</Label>
                    <Textarea
                      id="chronicDiseases"
                      value={chronicDiseases}
                      onChange={(e) => setChronicDiseases(e.target.value)}
                      onKeyDown={(e) => handleHealthBulletKeyDown(e, setChronicDiseases)}
                      placeholder={"• Hypertension\n• Diabète de type 2"}
                      className="min-h-[66px] resize-none [&::placeholder]:whitespace-pre-line"
                    />
                  </div>

                  {/* « Médicaments » — new. It had nowhere to live, so an anticoagulant was written into
                      « Notes importantes » as prose, where no document and no structured read can reach it. */}
                  <div className="space-y-2">
                    <Label htmlFor="medications" className="flex items-center gap-1.5">
                      <Pill className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                      Médicaments
                    </Label>
                    <Textarea
                      id="medications"
                      value={medications}
                      onChange={(e) => setMedications(e.target.value)}
                      onKeyDown={(e) => handleHealthBulletKeyDown(e, setMedications)}
                      placeholder={"• Kardégic 75 mg — 1/j\n• Metformine 850 mg — 2/j"}
                      className="min-h-[66px] resize-none [&::placeholder]:whitespace-pre-line"
                    />
                  </div>
                </div>

                {/*
                  « Tabac » — a dental risk factor, so it sits with the allergies rather than in the free-text
                  antécédents: it bears on healing, on implant survival and on periodontal work, and a paragraph
                  nobody scrolls to is not where that belongs.

                  ⚠️ **Nothing is preselected**, and that is the whole design. `null` means the question has not
                  been put, which is a different clinical fact from an answered « Non-fumeur » — seeding the first
                  answer would make a form nobody filled in indistinguishable from one somebody did, exactly the
                  fabrication the sexe and the date de naissance were made optional to stop. « Non renseigné »
                  therefore stays reachable as a real fourth choice rather than only as an initial state.

                  ⚠️ The quantity is **withheld** for the other two statuses rather than shown at 0 — `act-card`'s
                  rule: a control that can only hold a meaningless value says less than no control. The value
                  object drops it server-side too, so a figure typed and then corrected to « Non-fumeur » cannot
                  survive as a contradiction.
                */}
                {/* ⚠️ The label sits INSIDE the row rather than above it: one word on a line of its own cost a
                    whole row for three buttons that leave two thirds of the width empty. `me-1` and the shared
                    `items-center` keep it reading as this group's name rather than as a first chip. */}
                <div className="space-y-2">
                  <div
                    id="smoking-status"
                    role="group"
                    aria-label="Tabac"
                    className="flex flex-wrap items-center gap-2"
                  >
                    {/* A `<Label htmlFor>` pointing at this div never labelled anything — it is not a form
                        control. The group carries its own `aria-label`; this is the visible name. */}
                    <span className="me-1 text-sm font-medium">Tabac</span>
                    {SMOKING_STATUSES.map((status) => (
                      <button
                        key={status}
                        type="button"
                        onClick={() => setSmokingStatus(status)}
                        aria-pressed={smokingStatus === status}
                        /* `coarse:min-h-11` grows the box rather than overlaying a `.touch-target`: these sit a
                           few pixels apart and the later sibling paints last, so an overlay steals its
                           neighbour's taps. */
                        className={cn(
                          "inline-flex min-h-9 items-center rounded-md border px-3 text-sm font-medium transition-colors coarse:min-h-11",
                          smokingStatus === status
                            ? "border-primary/40 bg-primary/10 text-primary"
                            : "border-border text-muted-foreground hover-hover:hover:text-foreground",
                        )}
                      >
                        {smokingStatusLabel(status)}
                      </button>
                    ))}
                    {smokingStatus !== null && (
                      <button
                        type="button"
                        onClick={() => {
                          setSmokingStatus(null)
                          setSmokingPerDay("")
                        }}
                        className="inline-flex min-h-9 items-center rounded-md px-2 text-sm text-muted-foreground underline-offset-2 hover-hover:hover:underline coarse:min-h-11"
                      >
                        Non renseigné
                      </button>
                    )}
                  </div>

                  {smokingStatus === "Smoker" && (
                    <div className="flex flex-wrap items-center gap-2 pt-1">
                      <Label htmlFor="smokingPerDay" className="text-sm text-muted-foreground">
                        Combien par jour
                      </Label>
                      {/* ⚠️ `inputMode="numeric"`, never `type="number"` — spinners, a scroll wheel that
                          silently changes the value, and a locale-dependent decimal separator, on a field that
                          is a plain count. Same rule as the postal code and the identifiant CNAM above. */}
                      <Input
                        id="smokingPerDay"
                        value={smokingPerDay}
                        onChange={(e) => setSmokingPerDay(e.target.value)}
                        inputMode="numeric"
                        placeholder="20"
                        className="w-20 md:text-sm"
                        aria-invalid={!!errors.smokingPerDay}
                      />
                      <div className="flex items-center gap-1 rounded-lg bg-muted p-0.5">
                        {TOBACCO_UNITS.map((unit) => (
                          <Button
                            key={unit}
                            type="button"
                            variant={smokingUnit === unit ? "default" : "ghost"}
                            size="sm"
                            /* `coarse:h-11` for `act-card`'s documented reason: `buttonVariants` centres a 44 px
                               overlay on every Button, so two 32 px pills 4 px apart overhang each other. */
                            className="h-8 px-2.5 text-2xs coarse:h-11"
                            onClick={() => setSmokingUnit(unit)}
                            aria-pressed={smokingUnit === unit}
                          >
                            {tobaccoUnitLabel(unit)}
                          </Button>
                        ))}
                      </div>
                    </div>
                  )}
                  {errors.smokingPerDay && <p className="text-sm text-destructive">{errors.smokingPerDay}</p>}
                </div>

                {/*
                  Antécédents médicaux — ⚠️ **an empty list costs ONE line here, not three.**

                  The title, the count and « Ajouter » share the bar, and the body below it exists only when
                  there is something to put in it. It used to be a title row, then a button row, then a
                  full-width sentence explaining how to use the button — three rows to say « rien », twice over
                  (the familial list did the same), on a form whose whole defect was its height. The count
                  replaces that sentence: « aucun » IS the empty state, and the control it refers to is on the
                  same line as the word.

                  ⚠️ The failure branch keeps its own full-width panel. « Aucun » and « je n'ai pas pu lire »
                  are different claims and only one of them is safe to compress.
                */}
                <div className="rounded-md border">
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-2 bg-muted/50 px-3 py-2">
                    <Label className="font-medium">Antécédents médicaux</Label>
                    {!medicalHistoryFailed && (
                      <span className="text-xs text-muted-foreground">
                        {medicalHistoryEntries.length === 0
                          ? "aucun"
                          : `${medicalHistoryEntries.length} entrée${medicalHistoryEntries.length > 1 ? "s" : ""}`}
                      </span>
                    )}
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addMedicalHistoryEntry}
                      className="ms-auto gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Ajouter
                    </Button>
                  </div>
                  
                  {/* ⚠️ « Aucun antécédent » is only ever shown when the read SUCCEEDED. */}
                  {medicalHistoryFailed && (
                    <div className="border-t p-3">
                    <HistoryLoadFailure
                      message="Les antécédents médicaux n'ont pas pu être chargés. Ne considérez pas cette liste comme complète."
                      onRetry={() => { if (patient?.id) void loadMedicalHistoryEntries(patient.id) }}
                    />
                    </div>
                  )}

                  {medicalHistoryEntries.length === 0 ? null : (
                    <div className="space-y-3 border-t p-3">
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
                                  placeholder="Date (facultative)"
                                  value={entry.date || ""}
                                  onChange={(e) => updateMedicalHistoryEntry(index, 'date', e.target.value)}
                                />
                                <Input
                                  placeholder="Notes (facultatives)"
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

                {/* Antécédents familiaux — the same one-line-when-empty bar; see the médicaux block above. */}
                <div className="rounded-md border">
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-2 bg-muted/50 px-3 py-2">
                    <Label className="font-medium">Antécédents familiaux</Label>
                    {!familyHistoryFailed && (
                      <span className="text-xs text-muted-foreground">
                        {familyHistoryEntries.length === 0
                          ? "aucun"
                          : `${familyHistoryEntries.length} entrée${familyHistoryEntries.length > 1 ? "s" : ""}`}
                      </span>
                    )}
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={addFamilyHistoryEntry}
                      className="ms-auto gap-1"
                    >
                      <Plus className="h-3 w-3" />
                      Ajouter
                    </Button>
                  </div>
                  
                  {familyHistoryFailed && (
                    <div className="border-t p-3">
                    <HistoryLoadFailure
                      message="Les antécédents familiaux n'ont pas pu être chargés. Ne considérez pas cette liste comme complète."
                      onRetry={() => { if (patient?.id) void loadFamilyHistoryEntries(patient.id) }}
                    />
                    </div>
                  )}

                  {familyHistoryEntries.length === 0 ? null : (
                    <div className="space-y-3 border-t p-3">
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
                                placeholder="Notes (facultatives)"
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

            <RecordSection
              size="md"
              icon={<StickyNote className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Notes du patient"
              summary={sectionSummary.notes}
              open={openSections.notes}
              onToggle={() => toggleSection("notes")}
            >

              {/* ⚠️ The two notes share a ROW from `md:` up. They are the same kind of thing written at two weights,
                  they are short, and stacked they cost two rows on a form whose defect was its height —
                  `md:items-start` because the amber box carries a hint line the plain one does not. */}
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:items-start">
                <div className="space-y-2">
                  <Label
                    htmlFor="patientImportantNotes"
                    className="flex items-center gap-1.5 text-amber-800 dark:text-amber-300"
                  >
                    <AlertTriangle className="h-4 w-4" />
                    Notes importantes <span className="text-muted-foreground text-xs">(facultatives)</span>
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
                    Notes <span className="text-muted-foreground text-xs">(facultatives)</span>
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
              icon={<MapPin className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
              title="Coordonnées et rappels"
              summary={sectionSummary.coordonnees}
              open={openSections.coordonnees}
              onToggle={() => toggleSection("coordonnees")}
            >
              {/*
                Folded on arrival, and holding the three fields the practice describes as rarely filled: where the
                patient lives, their e-mail, and whether they may be texted.

                ⚠️ **The e-mail moved here out of « L'essentiel ».** Nothing in this product sends to it — not a
                rappel, not a relance, not a note d'honoraires — so it is an archive field, and it was occupying a
                slot beside the telephone, which is the field reception actually needs.

                ⚠️ **The reminder consent moved here too, and its summary above states what happens when it is
                unanswered.** Folding a consent question would be indefensible if the folded state were silent
                about it; « non renseignés » is on the closed header precisely because an unrecorded consent still
                sends.
              */}
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {/*
                  One box, whatever the desk wants to write in it — « Sfax » is as complete an answer as
                  « 12 rue de Carthage, La Marsa 2070 ». It replaces a rue / gouvernorat / ville / code postal
                  questionnaire whose four boxes asked for a precision nobody had and nothing reads.
                */}
                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="addressLine">
                    Adresse <span className="text-muted-foreground text-xs">(facultatif)</span>
                  </Label>
                  <Input
                    id="addressLine"
                    value={addressLine}
                    onChange={(e) => setAddressLine(e.target.value)}
                    placeholder="Ville, quartier ou adresse complète — comme vous voulez"
                    autoComplete="street-address"
                  />
                </div>

                {/* Email */}
                <div className="space-y-2">
                  <Label htmlFor="email">
                    E-mail <span className="text-muted-foreground text-xs">(facultatif)</span>
                  </Label>
                  <Input
                    id="email"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    autoComplete="email"
                    aria-invalid={!!errors.email}
                    className={cn(errors.email && "border-destructive")}
                  />
                  {errors.email && <p className="text-sm text-destructive">{errors.email}</p>}
                </div>

                {/* Rappels automatiques — on this form rather than in a settings screen. Recording a number
                    used to enrol the patient into SMS/WhatsApp with no way out; the answer is taken while somebody
                    is actually speaking to the patient, and a separate screen would be a control nobody opens. */}
                <div className="space-y-2">
                  <Label htmlFor="reminderConsent">Rappels automatiques (SMS / WhatsApp)</Label>
                  <Select
                    value={reminderConsent}
                    onValueChange={(v) => setReminderConsent(v as ReminderConsent)}
                  >
                    <SelectTrigger id="reminderConsent" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="NotRecorded">Non renseigné</SelectItem>
                      <SelectItem value="Granted">Le patient accepte</SelectItem>
                      <SelectItem value="Refused">Le patient refuse</SelectItem>
                    </SelectContent>
                  </Select>
                  {/* Each state says what will actually happen, because « non renseigné » is the one people
                      misread — it sends, and a cabinet that assumes otherwise is the whole risk here. */}
                  <p className="text-xs text-muted-foreground">
                    {reminderConsent === "Refused"
                      ? "Aucun rappel ni relance ne sera envoyé à ce patient, même avec un numéro valide."
                      : reminderConsent === "Granted"
                        ? "Ce patient recevra les rappels de rendez-vous et les relances."
                        : "Tant que la question n'a pas été posée, ce patient reçoit les rappels."}
                  </p>
                  {consentRecordedLabel && (
                    <p className="text-xs text-muted-foreground">{consentRecordedLabel}</p>
                  )}
                </div>
              </div>
            </RecordSection>

            {/* Medical Information Section */}


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
                  <Input id="cnamIdentifiant" inputMode="numeric" value={cnam.identifiantUnique} onChange={(e) => setCnam({ ...cnam, identifiantUnique: e.target.value })} placeholder="Ex : 12345678" />
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
                  <Input id="cnamRang" value={cnam.maladeLienRang} onChange={(e) => setCnam({ ...cnam, maladeLienRang: e.target.value })} placeholder="Ex : 1 — ou père/mère" />
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
                        placeholder="Ex : 2 — laisser vide si assuré seul"
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

