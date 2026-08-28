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
  Clock,
  Plus,
  Receipt,
  Stethoscope,
  Trash2,
  Upload,
  Edit,
  Save,
  Info,
  ChevronDown,
  AlertTriangle,
} from "lucide-react"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { toast } from "sonner"
import Image from "next/image"
import { clinicsApi, type ClinicDto } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"
import { BackupSettings } from "@/components/backup-settings"
import { useSelfRegistrationEnabled } from "@/lib/hooks/use-password-policy"
import Link from "next/link"
import { DoctorDocumentIdentityDialog } from "@/components/doctor-document-identity-dialog"
import { DoctorWorkingHoursCard } from "@/components/doctor-working-hours-card"
import {
  DEFAULT_WORKING_HOURS,
  WEEKDAY_LABELS_FR,
  type WorkingDay,
  validateWorkingHours,
} from "@/lib/working-hours"
import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"
import { formatAmount, parseAmountInput } from "@/lib/format"

/**
 * The icon chip every section header on this page wears.
 *
 * <p>The idiom is `app/documents/page.tsx`'s template tiles, sized down for a card header: a glyph inside a
 * tinted `rounded-lg` square instead of loose beside the text. A lucide glyph drawn in the same ink as the
 * heading next to it is not an icon, it is more text — it costs a line of markup and buys no scanning value,
 * which is most of why the product read as one long grey list.</p>
 *
 * <p>The hue is the <b>zone</b>'s, not the accent's, because « Paramètres » is a coloured area of the app and the
 * rail and the page eyebrow already paint it that way — a `primary` chip here would say "this section is
 * important" where the zone hue says "you are in Configuration". `config` is deliberately the near-neutral
 * zone (see `lib/zones.ts`), which is exactly why five of these stacked down one page do not read as a paint
 * chart.</p>
 *
 * <p>⚠️ It <b>replaces</b> the `w-1 h-6 bg-primary rounded-full` accent bar each header used to carry, rather
 * than joining it. Two marks competing for the same title is the thing the chip exists to fix, and that bar was
 * a one-off idiom living in three files and nowhere else in the app.</p>
 */
const CONFIG_CHIP = `flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`

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

/** A fourth copy of the shape, now the shared one — see the note on `WorkingDay`. */
type WorkingHoursInput = WorkingDay

/** French labels for the (English) weekday storage keys — the `weekdayLabelsFr` convention. */
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
  // Gated on the flag, never its negation — see useSelfRegistrationEnabled.
  const selfRegistrationEnabled = useSelfRegistrationEnabled()
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


  // Working Hours State — seeded from the shared default; overwritten by the clinic's saved hours on load.
  const [workingHours, setWorkingHours] = useState<WorkingHoursInput[]>(
    DEFAULT_WORKING_HOURS.map((d) => ({ ...d })),
  )

  // Doctors State. ⚠️ An unsaved row's id MUST carry the `doctor-` prefix: it is what the working-hours card's
  // render guard below tests, and a bare "1" passed it — so every load of this page fetched
  // `/api/doctors/1/working-hours` for a doctor that cannot exist and took a 404.
  const [doctors, setDoctors] = useState<Doctor[]>([
    { id: "doctor-new", name: "", specialty: "", phone: "", email: "" },
  ])
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
        setClinicVersion(clinic.version)
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
        // `formatAmount` (J8) — the timbre is a millime-precision amount and the field now accepts « 1,000 ».
        setStampDutyAmount(formatAmount(clinic.stampDutyAmount ?? 1))
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
      toast.error("Échec du chargement des données du cabinet : " + (err.message || "Erreur inconnue"))
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
    setDoctors([...doctors, { id: `doctor-${Date.now()}`, name: "", specialty: "", phone: "", email: "" }])
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

  const updateWorkingHours = (
    day: string,
    field: "from" | "to" | "breakFrom" | "breakTo",
    value: string,
  ) => {
    setWorkingHours((prev) =>
      // An emptied break end is stored as null, not "": null is what the server reads as « pas de pause », and
      // an empty string would fail the HH:mm check instead of clearing the closure.
      prev.map((item) =>
        item.day === day
          ? { ...item, [field]: field.startsWith("break") ? value || null : value }
          : item,
      ),
    )
  }

  // AC-P3.37 — the bespoke `fixed top-4 right-4` banner and this 4-second timer are gone; feedback goes
  // through `sonner` like every other screen, so it stacks, dismisses and reads the same everywhere.

  // Real-time: reload clinic profile/doctors when another client of this clinic changes them — but not
  // while this admin is mid-edit, so a live refresh never clobbers unsaved form input.
  // A peer's change is deliberately NOT applied while a section is being edited — that would wipe the
  // user's typing. But silently dropping it meant they went on to save over the other person with no idea
  // anything had happened, and then hit a 409 they could not explain. Record it and offer the reload.
  const [peerChangePending, setPeerChangePending] = useState(false)
  /*
   * Band B — the clinic row's concurrency token, as the last read returned it. `UpdateClinicCommand.Version` and
   * `SetExpectedVersion` were fully wired on the server and this screen simply never sent the token, so the
   * protection was inert and two admins saving the same tab silently overwrote each other.
   *
   * It is re-read on every load AND replaced from each save's own response, so a save followed by another save
   * works without a reload — which matters here, because the four cards write the same row.
   */
  const [clinicVersion, setClinicVersion] = useState<number | undefined>(undefined)
  const [clinicNameError, setClinicNameError] = useState<string | null>(null)

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
    /*
     * ⚠️ On the field, and it names « Nom du cabinet ».
     *
     * Saving an emptied name met a 400 whose toast read « Le champ « name » n'est pas valide ou n'a pas été
     * envoyé. » — the WIRE field name, in a toast, floated away from the input that caused it. `required` on the
     * input does not help: this is not a `<form>` submit, so the browser never validates it.
     */
    if (!clinicName.trim()) {
      setClinicNameError("Le nom du cabinet est obligatoire.")
      return
    }
    setClinicNameError(null)

    setIsSaving(true)
    try {
      // ⚠️ Band A — always a STRING, never `undefined`. It used to fall back to `undefined` when both halves were
      // empty, which omits the key, which the server reads as « leave unchanged » — so an address could be typed
      // but never cleared. `""` is what clears; `undefined` would mean « this save is not about the address ».
      const fullAddress = [address, governorate].filter((part) => part.trim()).join(", ")

      // Update clinic via API
      const updatedClinic = await clinicsApi.update({
        name: clinicName,
        address: fullAddress,
        // The governorate is the cabinet city printed on generated documents ("{ville}, le …", FR-6.1).
        city: governorate || "",
        phone: phone,
        email: email,
        logoFile: logoFile || undefined,
        version: clinicVersion,
      })

      // The row this screen's other three cards write is the same one — carry the new token forward, or the
      // next save 409s on a change this user made themselves.
      setClinicVersion(updatedClinic.version)
      setLogoUrl(updatedClinic.logoUrl || null)
      // Reload logo from backend if it was uploaded
      if (updatedClinic.logoUrl) {
        await loadLogoFromBackend()
      } else {
        // Clear preview if logo was removed
        setLogoPreview(null)
      }
      setLogoFile(null) // Clear file after successful upload

      toast.success("Informations du cabinet enregistrées.")
      setIsEditingClinicInfo(false)
    } catch (error: any) {
      toast.error(error.message || "Échec de l'enregistrement des informations du cabinet. Veuillez réessayer.")
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
        toast.error("Ajoutez au moins un médecin, avec un nom et une spécialité.")
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
    setOriginalBilling({ matriculeFiscal, vatApplicable, vatRate, stampDutyEnabled, stampDutyAmount })
    setIsEditingBilling(true)
  }

  const handleCancelBilling = () => {
    setMatriculeFiscal(originalBilling.matriculeFiscal ?? "")
    setVatApplicable(originalBilling.vatApplicable ?? false)
    setVatRate(originalBilling.vatRate ?? "7")
    setStampDutyEnabled(originalBilling.stampDutyEnabled ?? true)
    setStampDutyAmount(originalBilling.stampDutyAmount ?? "1.000")
    setIsEditingBilling(false)
  }

  const handleSaveBilling = async () => {
    setIsSaving(true)
    try {
      // ⚠️ No address / phone / email here any more. They used to be re-sent because an omitted key CLEARED the
      // field; with the tri-state fix an omitted key means « unchanged », so this card now writes only what it
      // owns — and cannot overwrite an identity field a colleague changed while the billing tab was open.
      const saved = await clinicsApi.update({
        name: clinicName,
        matriculeFiscal,
        vatApplicable,
        vatRate: parseAmountInput(vatRate) || 0,
        stampDutyEnabled,
        stampDutyAmount: parseAmountInput(stampDutyAmount) || 0,
        version: clinicVersion,
      })

      setClinicVersion(saved.version)
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
    // ⚠️ Before the round-trip, and it NAMES the day. The server refuses an inverted row with a bare
    // « Horaires de travail invalides. » — on a screen with seven rows, that does not say which one.
    const invalid = validateWorkingHours(workingHours)
    if (invalid) {
      toast.error(invalid)
      return
    }

    setIsSaving(true)
    try {
      // Only the hours — see the note on the billing save for why the identity fields are no longer re-sent.
      const updated = await clinicsApi.update({
        name: clinicName,
        workingHoursJson: JSON.stringify(workingHours),
        version: clinicVersion,
      })

      setClinicVersion(updated.version)
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
      <div className="min-h-full flex items-center justify-center bg-background">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
          <p className="text-muted-foreground">Chargement des paramètres du cabinet…</p>
        </div>
      </div>
    )
  }

  // `min-h-full`, not `min-h-screen`. This renders inside `<main>`, which is already the viewport minus the header
  // — so demanding a full 100vh here made the content taller than its own scroll container by exactly the header's
  // height, producing a scrollbar and a band of empty page below the last card on every visit.
  return (
    <div className="min-h-full bg-background">

      {/*
        § 0 — a capability removed by a role is STATED, not silently absent. Without this the four « Modifier »
        buttons simply vanish for a doctor, which reads as a broken screen rather than as an access boundary.
      */}
      {!isClinicAdmin && (
        <p role="note" className="mb-4 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
          Ces réglages sont en lecture seule&nbsp;: l&apos;identité du cabinet, les praticiens, les horaires et la
          facturation se modifient par un administrateur du cabinet. Vos propres informations — n° CNOMDT, cachet,
          horaires personnels — se modifient dans «&nbsp;Mon profil&nbsp;».
        </p>
      )}

      {/* A colleague saved these settings while this form was open. */}
      {peerChangePending && (
        <FormErrorBanner
          message="Les paramètres du cabinet ont été modifiés par quelqu'un d'autre pendant votre saisie. Vos modifications non enregistrées seront conservées si vous rechargez maintenant… mais la version affichée n'est plus à jour."
          action={{ label: "Recharger les paramètres", onClick: reloadAfterPeerChange }}
        />
      )}
      {/*
        ⚠️ The hand-rolled page header that used to sit here is gone, and its padding with it.

        `/settings` now renders `<PageHeader title="Paramètres du cabinet">`, so this block was a **second**
        page title on the same screen — and it gave the same page a second French name (« Paramètres de la
        clinique »), which is precisely the drift `PageHeader` deriving its zone from the route was meant to end.
        Its solid-primary `Building2` mark also collided with the route's own `Building2` page chip ~60px above.

        `p-3` went too: `AppShell` supplies `p-4 md:p-6` now that this page uses the default gutter, and the two
        were stacking to a 28px inset. `mx-auto max-w-5xl` stays — a dense settings form reads better narrower
        than the shell's `max-w-7xl`, and that is a real decision rather than an accident.
      */}
      <div className="mx-auto max-w-5xl space-y-3">
        {/*
          The clinic code — and ONLY where it still does something.

          ⚠️ **This was the second, ungated copy.** `multi-tenant-cloud` US-3 hid the card on `/users` where
          self-registration is closed, on the reasoning that a badge nobody can use under a paragraph explaining
          why it does not work invites an admin to hunt for a door that is not there. This strip says the same
          thing in the same product and was never found: on the hosted deployment « Paramètres » went on telling
          the owner to hand the code to colleagues, for whom it creates nothing.
        */}
        {selfRegistrationEnabled && clinicCode && (
          <div className="bg-accent/20 border border-primary/25 rounded-lg p-3">
            <Label className="text-xs text-primary font-medium">Code du cabinet</Label>
            <div className="flex items-center gap-2 mt-1.5">
              <Badge
                variant="outline"
                className="text-base font-mono font-bold px-3 py-1 bg-card text-primary border-primary/40"
              >
                {clinicCode}
              </Badge>
            </div>
            <p className="text-2xs text-primary mt-1.5">
              Communiquez ce code à vos collègues pour qu'ils rejoignent le cabinet
            </p>
          </div>
        )}

        {/*
          Clinic Info Card Collapsible.

          None of the four section Cards carries a border override any more: `ui/card.tsx` already renders
          `border`, and `globals.css`'s base layer paints every border `--border`. The
          `border-gray-200 dark:border-slate-800` they each repeated was a hand-maintained copy of exactly
          that token pair.

          The header toggle carries `touch-target py-2`: `CardTitle` is `leading-none`, so the tallest thing
          in the button is the icon chip and the real target was 24px back when that was a 24px accent bar. A
          plain `<button>` gets neither the primitive's floor nor the coarse-pointer rule in `globals.css`
          (which is button-exempt on purpose), and these four toggles are how every settings section is opened.

          The row gained `flex-wrap gap-2`: the chip takes ~40px out of the title column, and at 390px
          « Facturation (note d'honoraires) » beside a « Modifier » button had nowhere left to go.
        */}
        <Card>
          <CardHeader className="pb-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <button
                onClick={() => setIsClinicInfoCollapsed(!isClinicInfoCollapsed)}
                className="touch-target flex flex-1 items-center gap-2.5 py-2 text-left hover:opacity-70 transition-opacity"
              >
                {/*
                  See CONFIG_CHIP — the `/documents` tile idiom, sized for a header.

                  ⚠️ `Building2` repeats the glyph in this page's own title chip ~60px above. That is left
                  alone rather than worked around: the two are different objects (the page mark is a solid
                  `bg-primary` square with an inverted glyph, this is a near-neutral wash), and `Building2` is
                  the *right* mark for « Informations du cabinet » — swapping in a second-choice glyph to
                  dodge the repeat makes the section harder to recognise, which is the only thing the chip is
                  for. The honest fix is in the page mark, which hand-rolls its header instead of using
                  `ui/page-header.tsx` + `navIconForPath` — the rail draws `Settings` for `/settings`, and that
                  helper exists precisely so a page never shows one icon while the rail shows another.
                */}
                <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
                  <span aria-hidden="true" className={CONFIG_CHIP}>
                    <Building2 className="size-4" strokeWidth={1.75} />
                  </span>
                  Informations du cabinet
                </CardTitle>
                <ChevronDown
                  className={`size-4 shrink-0 text-muted-foreground transition-transform ${
 isClinicInfoCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {/*
                ⚠️ Admin only, on all four cards. `PUT /api/clinics` is `AdminOnly`, so a doctor was offered
                « Modifier », allowed to type, and then met a 403 on « Enregistrer » — losing the typing. The
                boundary held; the presentation did not. `frontend-web.md` § 0 is explicit that a capability is
                never removed silently, which is why the read-only note below says whose job this is.
              */}
              {!isEditingClinicInfo && isClinicAdmin && (
                <Button onClick={handleEditClinicInfo} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
                </Button>
              )}
            </div>
          </CardHeader>
          {!isClinicInfoCollapsed && (
            <CardContent className="space-y-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor="clinic-name" className="text-xs font-medium flex items-center gap-1">
                    Nom du cabinet
                    <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="clinic-name"
                    placeholder="Saisir le nom du cabinet"
                    value={clinicName}
                    onChange={(e) => {
                      setClinicName(e.target.value)
                      if (clinicNameError) setClinicNameError(null)
                    }}
                    disabled={!isEditingClinicInfo}
                    aria-invalid={clinicNameError ? true : undefined}
                    aria-describedby={clinicNameError ? "clinic-name-error" : undefined}
                    /*
                     * ⚠️ `md:text-sm`, never a bare `text-sm`, on every `Input`/`Textarea` in this file.
                     * `ui/input.tsx` ships `text-base md:text-sm` as the iOS focus-zoom guard — Safari zooms
                     * into any field under 16px and never zooms back out — and tailwind-merge treats an
                     * unprefixed size at the call site as a REPLACEMENT for `text-base`, so the class written
                     * to make the field compact is exactly the class that disarms the guard.
                     */
                    className={`h-8 md:text-sm ${!isEditingClinicInfo ? "bg-muted/40" : ""}`}
                    aria-required="true"
                  />
                  {clinicNameError && (
                    <p id="clinic-name-error" role="alert" className="text-xs text-destructive">
                      {clinicNameError}
                    </p>
                  )}
                </div>

                <div className="space-y-1">
                  <Label htmlFor="governorate" className="text-xs font-medium flex items-center gap-1">
                    Ville / Gouvernorat
                    <span className="text-destructive">*</span>
                  </Label>
                  <Select value={governorate} onValueChange={setGovernorate} disabled={!isEditingClinicInfo}>
                    {/* The asterisk is not the only "required" signal: every sibling field carries the
                        native `required` attribute, and a Radix trigger cannot — so it states it itself. */}
                    {/* ⚠️ `w-full` + `md:text-sm`. `SelectTrigger`'s base is **`w-fit`**, so with no width of its
                        own it sizes to the selected value — narrower than the inputs it is stacked with, and
                        wider than its own grid cell once a long gouvernorat is picked. And an *unprefixed*
                        `text-sm` replaces the primitive's `text-base` under tailwind-merge, which is the § 3
                        guard. */}
                    <SelectTrigger
                      id="governorate"
                      aria-required="true"
                      className={`h-8 w-full md:text-sm ${!isEditingClinicInfo ? "bg-muted/40" : ""}`}
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
                  placeholder="Saisir l'adresse complète du cabinet"
                  value={address}
                  onChange={(e) => setAddress(e.target.value)}
                  disabled={!isEditingClinicInfo}
                  className={`md:text-sm ${!isEditingClinicInfo ? "bg-muted/40" : ""}`}
                  rows={2}
                />
              </div>

              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor="phone" className="text-xs font-medium flex items-center gap-1">
                    Numéro de téléphone
                    <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="phone"
                    type="tel"
                    placeholder="+216 12 345 678"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    disabled={!isEditingClinicInfo}
                    className={`h-8 md:text-sm ${!isEditingClinicInfo ? "bg-muted/40" : ""}`}
                    required
                  />
                </div>

                <div className="space-y-1">
                  <Label htmlFor="email" className="text-xs font-medium flex items-center gap-1">
                    E-mail professionnel
                    <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="ex. : contact@moncabinet.tn"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={!isEditingClinicInfo}
                    className={`h-8 md:text-sm ${!isEditingClinicInfo ? "bg-muted/40" : ""}`}
                    required
                  />
                </div>
              </div>

              <Separator className="my-3" />

              <div className="space-y-2">
                <Label className="text-xs font-medium">Logo du cabinet</Label>
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
                      {/*
                        AC-11 — two renderings of one action, because a hover-revealed control does not exist
                        on a touch device and this is the only way to clear the logo.

                        Not simply "always visible on touch": the fine-pointer version is a full-bleed overlay,
                        and leaving that on would both hide the logo and turn the whole thumbnail into an
                        unconfirmed delete target. So a coarse pointer gets a small persistent corner button
                        instead, and the overlay stays where hovering is real.
                      */}
                      {isEditingClinicInfo && (
                        <>
                          <button
                            type="button"
                            onClick={() => {
                              setLogoPreview(null)
                              setLogoFile(null)
                            }}
                            aria-label="Supprimer le logo"
                            className="absolute inset-0 hidden bg-black/60 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100 hover-hover:flex items-center justify-center"
                          >
                            {/* `bg-card` is the token for exactly the `bg-white dark:bg-slate-900` pair this
                                chip hand-maintained. `bg-black/60` stays: a scrim is not a surface, and the
                                palette has no token for one (`ui/dialog.tsx`'s overlay is the same literal). */}
                            <div className="bg-card rounded-full p-1.5">
                              <Trash2 className="w-4 h-4 text-destructive" />
                            </div>
                          </button>
                          <button
                            type="button"
                            onClick={() => {
                              setLogoPreview(null)
                              setLogoFile(null)
                            }}
                            aria-label="Supprimer le logo"
                            className="touch-target absolute -right-1 -top-1 hidden rounded-full bg-card p-1.5 shadow ring-1 ring-border coarse:block"
                          >
                            <Trash2 className="w-4 h-4 text-destructive" />
                          </button>
                        </>
                      )}
                    </div>
                  ) : isEditingClinicInfo ? (
                    // Always show upload button when in edit mode.
                    // The hover fill was a two-stop gradient into `indigo-50/20` with a `dark:` twin — an
                    // indigo the palette does not contain, on the one interaction that only ever needed to
                    // say "droppable". `hover:bg-accent` is the theme's own tinted hover surface.
                    <label className="w-20 h-20 flex flex-col items-center justify-center border-2 border-dashed border-border rounded-lg cursor-pointer hover:border-primary hover:bg-accent transition-all group">
                      <Upload className="w-5 h-5 text-muted-foreground group-hover:text-primary transition-colors" />
                      <span className="text-2xs text-muted-foreground group-hover:text-primary font-medium transition-colors mt-1">
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
        <Card>
          <CardHeader className="pb-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <button
                onClick={() => setIsDoctorsCollapsed(!isDoctorsCollapsed)}
                className="touch-target flex flex-1 items-center gap-2.5 py-2 text-left hover:opacity-70 transition-opacity"
              >
                <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
                  <span aria-hidden="true" className={CONFIG_CHIP}>
                    <Stethoscope className="size-4" strokeWidth={1.75} />
                  </span>
                  Médecins
                </CardTitle>
                <ChevronDown
                  className={`size-4 shrink-0 text-muted-foreground transition-transform ${
 isDoctorsCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {/*
                ⚠️ Admin only, on all four cards. `PUT /api/clinics` is `AdminOnly`, so a doctor was offered
                « Modifier », allowed to type, and then met a 403 on « Enregistrer » — losing the typing. The
                boundary held; the presentation did not. `frontend-web.md` § 0 is explicit that a capability is
                never removed silently, which is why the read-only note below says whose job this is.
              */}
              {!isEditingDoctors && isClinicAdmin && (
                <Button onClick={handleEditDoctors} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
                </Button>
              )}
            </div>
          </CardHeader>
          {!isDoctorsCollapsed && (
            <CardContent className="space-y-3">
              {doctors.map((doctor, index) => {
                // Keyed on the row's own id, not the index: reordering or removing a row must not hand one
                // practitioner's field ids to another.
                const fieldId = `clinic-doctor-${doctor.id}`
                return (
                <Card key={doctor.id} className="bg-muted/40">
                  <CardContent className="p-3">
                    <div className="flex items-start gap-3">
                      <div className="flex items-center justify-center w-7 h-7 rounded-full bg-primary text-primary-foreground text-xs font-semibold shrink-0 mt-0.5">
                        {index + 1}
                      </div>
                      {/* One field per line below `sm:` (rule 2 of the mobile pass). Two columns here left
                          ~106px each once the row's 28px index bubble and gaps are taken out of a 288px card
                          — a « Chirurgien-dentiste » select does not fit that, and the name input was too
                          narrow to read what you had typed. `min-w-0` so a long value wraps instead of
                          widening the grid. */}
                      <div className="grid min-w-0 flex-1 grid-cols-1 gap-2 sm:grid-cols-2">
                        <div className="space-y-1">
                          {/* htmlFor/id on all six — the hours rows in this same file already carry them
                              (AC-P1.54), so these labels named nothing while their sibling card did. */}
                          <Label htmlFor={`${fieldId}-name`} className="text-xs">
                            Nom complet
                          </Label>
                          <Input
                            id={`${fieldId}-name`}
                            value={doctor.name}
                            onChange={(e) => updateDoctor(doctor.id, "name", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 md:text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label htmlFor={`${fieldId}-specialty`} className="text-xs">
                            Spécialité
                          </Label>
                          <Select
                            value={doctor.specialty}
                            onValueChange={(value) => updateDoctor(doctor.id, "specialty", value)}
                            disabled={!isEditingDoctors}
                          >
                            {/* `w-full`: the base is `w-fit`, so « Médecin dentiste » made this trigger
                                18 px wider than its grid cell at 320 px — see the note on the gouvernorat
                                trigger above for the `md:` prefix. */}
                            <SelectTrigger id={`${fieldId}-specialty`} className="h-7 w-full md:text-sm">
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
                          <Label htmlFor={`${fieldId}-phone`} className="text-xs">
                            Téléphone
                          </Label>
                          <Input
                            id={`${fieldId}-phone`}
                            value={doctor.phone || ""}
                            onChange={(e) => updateDoctor(doctor.id, "phone", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 md:text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label htmlFor={`${fieldId}-email`} className="text-xs">
                            Email
                          </Label>
                          <Input
                            id={`${fieldId}-email`}
                            type="email"
                            value={doctor.email || ""}
                            onChange={(e) => updateDoctor(doctor.id, "email", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 md:text-sm"
                          />
                        </div>
                        <div className="space-y-1">
                          <Label htmlFor={`${fieldId}-cnam`} className="text-xs">
                            Code prof. santé (CNAM)
                          </Label>
                          <Input
                            id={`${fieldId}-cnam`}
                            value={doctor.codeProfessionnelSante || ""}
                            onChange={(e) => updateDoctor(doctor.id, "codeProfessionnelSante", e.target.value)}
                            disabled={!isEditingDoctors}
                            className="h-7 md:text-sm"
                          />
                        </div>
                        {/* AC-P2.30 — the CNOMDT number and cachet « Mon profil » already told the admin they
                            could set from here. Read-only in the roster because they belong to
                            `PUT /api/doctors/{id}`, not to the roster rewrite; « Modifier » opens that. */}
                        <div className="space-y-1">
                          <Label id={`${fieldId}-identity-label`} className="text-xs">
                            Identité documentaire
                          </Label>
                          <div
                            role="group"
                            aria-labelledby={`${fieldId}-identity-label`}
                            className="flex h-7 items-center gap-2 text-sm"
                          >
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
                                /* Four buttons on this screen read exactly « Modifier », so nothing said which
                                   practitioner this one edits. The visible label stays short; the accessible
                                   name says whose identity it opens. */
                                aria-label={
                                  doctor.name
                                    ? `Modifier l’identité documentaire de ${doctor.name}`
                                    : "Modifier l’identité documentaire de ce praticien"
                                }
                              >
                                Modifier
                              </Button>
                            )}
                          </div>
                        </div>
                      </div>
                      {isEditingDoctors && doctors.length > 1 && (
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => removeDoctor(doctor.id)}
                          className="h-7 w-7 shrink-0 text-destructive hover:bg-destructive-wash hover:text-destructive"
                          aria-label={doctor.name ? `Retirer ${doctor.name} de la liste` : "Retirer ce praticien de la liste"}
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      )}
                    </div>

                    {/*
                      § 5.4 / AC-P1.25 — an admin sets any practitioner's own hours. Only for a doctor that
                      exists server-side: an unsaved roster row has a client-side placeholder id the endpoint
                      could not resolve.

                      ⚠️ A SIBLING of the field row above, never a child of it. As a flex item this block
                      carried `w-full`, and `width` on a flex item resolves its base size — so the row's base
                      sizes summed past the container, free space went negative, and the `flex-1 min-w-0`
                      field grid (flex-basis 0, with its automatic min-content floor removed by `min-w-0`)
                      had nothing to grow into. Every doctor field — nom, spécialité, téléphone, email, code
                      CNAM, identité documentaire — collapsed to 0px, at every viewport, for every admin.
                      As a block-level sibling it is full width by construction, so `w-full` is gone too.
                    */}
                    {isClinicAdmin && !isEditingDoctors && doctor.id && !doctor.id.startsWith("doctor-") && (
                      <details className="mt-2">
                        <summary className="cursor-pointer py-2 text-xs font-medium text-muted-foreground hover:text-foreground">
                          Horaires de ce praticien
                        </summary>
                        <div className="mt-2">
                          <DoctorWorkingHoursCard doctorId={doctor.id} embedded />
                        </div>
                      </details>
                    )}
                  </CardContent>
                </Card>
                )
              })}

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
        <Card>
          <CardHeader className="pb-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <button
                onClick={() => setIsHoursCollapsed(!isHoursCollapsed)}
                className="touch-target flex flex-1 items-center gap-2.5 py-2 text-left hover:opacity-70 transition-opacity"
              >
                <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
                  <span aria-hidden="true" className={CONFIG_CHIP}>
                    <Clock className="size-4" strokeWidth={1.75} />
                  </span>
                  Horaires d&apos;ouverture
                </CardTitle>
                <ChevronDown
                  className={`size-4 shrink-0 text-muted-foreground transition-transform ${
 isHoursCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {/*
                ⚠️ Admin only, on all four cards. `PUT /api/clinics` is `AdminOnly`, so a doctor was offered
                « Modifier », allowed to type, and then met a 403 on « Enregistrer » — losing the typing. The
                boundary held; the presentation did not. `frontend-web.md` § 0 is explicit that a capability is
                never removed silently, which is why the read-only note below says whose job this is.
              */}
              {!isEditingHours && isClinicAdmin && (
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
                  /*
                   * `flex-wrap`, copied from `doctor-working-hours-card.tsx` — the sibling that renders the
                   * identical row and already wraps. Without it the `w-32` day column plus two time fields
                   * do not fit a phone, and `ui/input.tsx`'s `min-w-0` lets both shrink to ~67px, of which
                   * `px-3` takes 24.
                   *
                   * `bg-accent/20` for the enabled row: the class here was `bg-accent/30/20`, a double
                   * opacity modifier Tailwind does not parse — so the "this day is open" tint has in fact
                   * been painting nothing, and the two states were told apart by their border alone.
                   */
                  className={`flex flex-wrap items-center gap-3 p-2 rounded-lg border ${
 item.enabled
                      ? "border-primary/25 bg-accent/20"
                      : "border-border bg-muted/40"
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
                  {/* Full width on its own wrapped line below `sm:`, sharing the row above it. */}
                  <div className="flex w-full items-center gap-2 sm:w-auto sm:flex-1">
                    <Label htmlFor={`clinic-hours-${item.day}-from`} className="sr-only">
                      {`Heure d'ouverture — ${WEEKDAY_LABELS_FR[item.day] ?? item.day}`}
                    </Label>
                    <Input
                      id={`clinic-hours-${item.day}-from`}
                      type="time"
                      value={item.from}
                      onChange={(e) => updateWorkingHours(item.day, "from", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 md:text-xs ${!isEditingHours || !item.enabled ? "bg-muted/40" : ""}`}
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
                      className={`h-7 md:text-xs ${!isEditingHours || !item.enabled ? "bg-muted/40" : ""}`}
                    />
                  </div>
                  {/* The mid-day closure. Optional and empty by default, so a cabinet that does not close at
                      lunch sees no change; leaving both blank is « pas de pause ». */}
                  <div className="flex w-full items-center gap-2 sm:basis-full">
                    <span className="w-32 shrink-0 text-xs text-muted-foreground">Pause (facultative)</span>
                    <Label htmlFor={`clinic-hours-${item.day}-break-from`} className="sr-only">
                      {`Début de la pause — ${WEEKDAY_LABELS_FR[item.day] ?? item.day}`}
                    </Label>
                    <Input
                      id={`clinic-hours-${item.day}-break-from`}
                      type="time"
                      value={item.breakFrom ?? ""}
                      onChange={(e) => updateWorkingHours(item.day, "breakFrom", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 min-w-0 flex-1 basis-28 md:text-xs ${!isEditingHours || !item.enabled ? "bg-muted/40" : ""}`}
                    />
                    <span className="text-xs text-muted-foreground">à</span>
                    <Label htmlFor={`clinic-hours-${item.day}-break-to`} className="sr-only">
                      {`Fin de la pause — ${WEEKDAY_LABELS_FR[item.day] ?? item.day}`}
                    </Label>
                    <Input
                      id={`clinic-hours-${item.day}-break-to`}
                      type="time"
                      value={item.breakTo ?? ""}
                      onChange={(e) => updateWorkingHours(item.day, "breakTo", e.target.value)}
                      disabled={!isEditingHours || !item.enabled}
                      className={`h-7 min-w-0 flex-1 basis-28 md:text-xs ${!isEditingHours || !item.enabled ? "bg-muted/40" : ""}`}
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
        <Card>
          <CardHeader className="pb-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <button
                onClick={() => setIsBillingCollapsed(!isBillingCollapsed)}
                className="touch-target flex flex-1 items-center gap-2.5 py-2 text-left hover:opacity-70 transition-opacity"
              >
                <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
                  <span aria-hidden="true" className={CONFIG_CHIP}>
                    <Receipt className="size-4" strokeWidth={1.75} />
                  </span>
                  Facturation (note d&apos;honoraires)
                </CardTitle>
                <ChevronDown
                  className={`size-4 shrink-0 text-muted-foreground transition-transform ${
 isBillingCollapsed ? "-rotate-90" : ""
                  }`}
                />
              </button>
              {/*
                ⚠️ Admin only, on all four cards. `PUT /api/clinics` is `AdminOnly`, so a doctor was offered
                « Modifier », allowed to type, and then met a 403 on « Enregistrer » — losing the typing. The
                boundary held; the presentation did not. `frontend-web.md` § 0 is explicit that a capability is
                never removed silently, which is why the read-only note below says whose job this is.
              */}
              {!isEditingBilling && isClinicAdmin && (
                <Button onClick={handleEditBilling} variant="ghost" size="sm" className="h-7 text-xs">
                  <Edit className="w-3 h-3 mr-1" />
                  Modifier
                </Button>
              )}
            </div>
          </CardHeader>
          {!isBillingCollapsed && (
            <CardContent className="space-y-3">
              {/*
                There is no TVA setting and no timbre fiscal any more: the price of an act IS what the patient
                owes. The two controls that used to sit here were the cause of a real money defect — the fiche de
                soins priced the acts and told the dentist « Reste à payer : 0,000 » while the note d'honoraires
                it generated added 7 % + 1,000 DT on top, so a patient who had settled in full still owed money
                and nobody was told. Rather than teach the chairside screen about the tax, the tax was removed:
                one number, stated once, is the only version of this that cannot drift.

                The matricule fiscal stays — it is the cabinet's tax identifier, printed on the note as identity,
                and has nothing to do with computing a total.
              */}
              <p role="note" className="rounded-md bg-muted/40 p-3 text-xs text-muted-foreground">
                Les montants sont ceux du catalogue d&apos;actes : le prix d&apos;un acte est le montant dû par le
                patient. Aucune TVA ni timbre fiscal n&apos;est ajouté à la note d&apos;honoraires.
              </p>

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
                  className={`h-8 md:text-sm ${!isEditingBilling ? "bg-muted/40" : ""}`}
                />
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

        {/* OS notifications, per platform (Part 6, AC-51/AC-52). Not mode-gated: the card's whole job is to say
            whether this installation can push, and hiding it where it cannot would leave the owner of a
            self-hosted install with no explanation for why the phone app is silent. */}

        {/* `bg-accent/20` — the same tinted-info-panel pairing as the clinic-code block at the top of this
            page. The class here was `bg-accent/50/20`, a double opacity modifier Tailwind does not parse. */}
        <Card className="border border-primary/25 bg-accent/20">
          <CardContent className="p-3">
            <div className="flex items-start gap-2">
              <Info className="w-4 h-4 text-primary mt-0.5 shrink-0" />
              <div className="space-y-1">
                {/* Was English ("Need help? / Contact support at …") in an otherwise entirely French UI, with a
                    placeholder address and a placeholder phone number. The support channel is not something
                    this screen knows, so it now points at the person who installed the clinic rather than
                    inventing a contact that does not answer. */}
                <p className="text-xs font-medium text-accent-foreground">Besoin d&apos;aide ?</p>
                <p className="text-xs text-primary">
                  Contactez la personne qui a installé votre logiciel de cabinet.
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
