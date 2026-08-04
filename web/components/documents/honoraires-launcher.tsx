"use client"

import { useEffect, useState } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog"
import {
  Command,
  CommandInput,
  CommandList,
  CommandEmpty,
  CommandGroup,
  CommandItem,
} from "@/components/ui/command"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { EmptyState } from "@/components/ui/empty-state"
import { Check, ClipboardList } from "lucide-react"
import { cn } from "@/lib/utils"
import { toast } from "sonner"
import { patientsApi } from "@/lib/api/patients"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { invoicesApi, type InvoiceLineInput } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { DentalRecordDto, PatientDto } from "@/lib/api/types"
import { formatDT, formatDate } from "@/lib/format"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"

/**
 * One of the patient's fiches de soins, offered for billing.
 *
 * `billedBy` is the number of the non-cancelled note that already bills it (« brouillon » when that note has
 * not been issued yet and therefore has no number). It is carried per séance rather than used to filter the
 * list: a séance that vanishes with no explanation is indistinguishable from a séance that was never recorded,
 * which is the reading the old silent prefill produced.
 */
type BillableSession = {
  record: DentalRecordDto
  billedBy: string | null
}

/**
 * FR-1: the "Note d'honoraires" card no longer opens the document editor. It opens a patient-selection
 * step, then a **séance-selection** step, then the existing compliant InvoiceFormModal (draft).
 * Numbering / TVA / timbre / El Fatoora are applied later at the separate "issue" step in the Factures
 * module — this flow only creates the draft, it never auto-issues.
 *
 * <p>⚠️ The séance step is the point, and it replaced an automatic prefill of *every* not-yet-invoiced fiche.
 * That prefill decided for you and could not be argued with: in a clinic where finished séances are billed as
 * they happen (« Facturer cette intervention » on the fiche), the un-invoiced set is normally **empty**, so the
 * flow silently produced one blank line and no reason for it — the note d'honoraires appeared to have lost the
 * patient's work. Choosing is also what makes the free path possible: ticking nothing is « autre chose », and
 * the modal then renders its own blank line.</p>
 */
export function HonorairesLauncher({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loadingPatients, setLoadingPatients] = useState(false)
  const [selectedId, setSelectedId] = useState<string>("")
  const [search, setSearch] = useState("")
  const [preparing, setPreparing] = useState(false)

  // Which step of the picker is showing. The patient is snapshotted on the way to `sessions` rather than
  // re-derived from `patients`: that list is refetched on every keystroke of the search, so a lookup by id
  // would come back undefined the moment the term no longer matches the patient already chosen.
  const [step, setStep] = useState<"patient" | "sessions">("patient")
  const [chosenPatient, setChosenPatient] = useState<PatientDto | null>(null)
  const [sessions, setSessions] = useState<BillableSession[]>([])
  const [pickedRecordIds, setPickedRecordIds] = useState<string[]>([])

  // Invoice draft step (the reused InvoiceFormModal, opened once the séances are chosen).
  const [invoiceOpen, setInvoiceOpen] = useState(false)
  const [presetPatientId, setPresetPatientId] = useState<string | undefined>()
  const [presetPatientName, setPresetPatientName] = useState<string | undefined>()
  const [presetLines, setPresetLines] = useState<InvoiceLineInput[] | undefined>()

  // Reset the whole picker each time it opens.
  useEffect(() => {
    if (open) {
      setSelectedId("")
      setSearch("")
      setStep("patient")
      setChosenPatient(null)
      setSessions([])
      setPickedRecordIds([])
    }
  }, [open])

  // Server-side patient search (debounced) instead of loading a fixed page and filtering client-side, so a
  // large clinic's patients are all reachable rather than silently capped.
  useEffect(() => {
    if (!open || step !== "patient") return
    let cancelled = false
    setLoadingPatients(true)
    const handle = setTimeout(() => {
      patientsApi
        .list({ searchTerm: search.trim() || undefined, limit: 50 })
        .then((list) => {
          if (!cancelled) setPatients(list)
        })
        .catch((e) => {
          if (!cancelled) {
            toast.error("Erreur", {
              description: e instanceof ApiError ? e.message : "Impossible de charger les patients",
            })
          }
        })
        .finally(() => {
          if (!cancelled) setLoadingPatients(false)
        })
    }, 250)
    return () => {
      cancelled = true
      clearTimeout(handle)
    }
  }, [open, step, search])

  const loadSessions = async () => {
    const patient = patients.find((p) => p.id === selectedId)
    if (!patient) return
    setPreparing(true)
    try {
      const [records, invoices] = await Promise.all([
        dentalRecordsApi.list(selectedId),
        invoicesApi.list({ patientId: selectedId }),
      ])
      // Which note already bills each fiche — via the header link OR any line link (a multi-record honoraires
      // note links each seeded fiche at the line level). A Cancelled note bills nothing, so it does not count.
      const billedBy = new Map<string, string>()
      for (const inv of invoices) {
        if (inv.status === "Cancelled") continue
        const label = inv.number?.trim() || "brouillon"
        if (inv.dentalRecordId) billedBy.set(inv.dentalRecordId, label)
        for (const line of inv.lines ?? []) {
          if (line.dentalRecordId) billedBy.set(line.dentalRecordId, label)
        }
      }
      setChosenPatient(patient)
      setSessions(
        [...records]
          .sort((a, b) => (b.interventionDate ?? "").localeCompare(a.interventionDate ?? ""))
          .map((record) => ({ record, billedBy: billedBy.get(record.id) ?? null })),
      )
      // Nothing pre-ticked, deliberately — this step exists because the old flow chose for you.
      setPickedRecordIds([])
      setStep("sessions")
    } catch (e) {
      toast.error("Erreur", {
        description: e instanceof ApiError ? e.message : "Impossible de charger les séances du patient",
      })
    } finally {
      setPreparing(false)
    }
  }

  const togglePicked = (recordId: string) =>
    setPickedRecordIds((prev) =>
      prev.includes(recordId) ? prev.filter((id) => id !== recordId) : [...prev, recordId],
    )

  const handleBill = () => {
    if (!chosenPatient) return
    // One line per séance, linked back to its fiche so the « déjà facturée » badge above is right next time.
    const lines: InvoiceLineInput[] = sessions
      .filter((s) => pickedRecordIds.includes(s.record.id))
      .map(({ record }) => ({
        designation: record.procedureType,
        quantity: 1,
        unitPriceHt: record.cost,
        dentalRecordId: record.id,
      }))

    setPresetPatientId(chosenPatient.id)
    setPresetPatientName(`${chosenPatient.firstName} ${chosenPatient.lastName}`.trim())
    // Nothing ticked → undefined, so the modal renders its own blank line: that IS the « autre chose » path.
    setPresetLines(lines.length > 0 ? lines : undefined)
    onOpenChange(false)
    setInvoiceOpen(true)
  }

  const pickedCount = pickedRecordIds.length
  const alreadyBilledPicked = sessions.filter(
    (s) => s.billedBy && pickedRecordIds.includes(s.record.id),
  ).length

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="md:max-w-lg">
          {step === "patient" ? (
            <>
              <DialogHeader>
                <DialogTitle>Facturer un patient</DialogTitle>
                <DialogDescription>
                  Choisissez le patient, puis la ou les séances à porter sur la note d&apos;honoraires.
                </DialogDescription>
              </DialogHeader>

              <Command className="rounded-md border" shouldFilter={false}>
                <CommandInput
                  placeholder="Rechercher un patient..."
                  value={search}
                  onValueChange={setSearch}
                />
                <CommandList>
                  <CommandEmpty>{loadingPatients ? "Chargement..." : "Aucun patient trouvé."}</CommandEmpty>
                  <CommandGroup>
                    {patients.map((p) => {
                      const name = `${p.firstName} ${p.lastName}`.trim()
                      return (
                        <CommandItem key={p.id} value={name} onSelect={() => setSelectedId(p.id)}>
                          <Check
                            className={cn("mr-2 h-4 w-4", selectedId === p.id ? "opacity-100" : "opacity-0")}
                          />
                          {name}
                        </CommandItem>
                      )
                    })}
                  </CommandGroup>
                </CommandList>
              </Command>

              <DialogFooter className="gap-2">
                <Button variant="outline" onClick={() => onOpenChange(false)} disabled={preparing}>
                  Annuler
                </Button>
                <Button onClick={loadSessions} disabled={!selectedId || preparing}>
                  {preparing ? "Chargement..." : "Continuer"}
                </Button>
              </DialogFooter>
            </>
          ) : (
            <>
              <DialogHeader>
                <DialogTitle>Que voulez-vous facturer ?</DialogTitle>
                <DialogDescription>
                  {chosenPatient
                    ? `${chosenPatient.firstName} ${chosenPatient.lastName}`.trim() + " — "
                    : ""}
                  cochez les séances à facturer. Sans en cocher aucune, vous saisissez les actes vous-même ;
                  dans les deux cas les lignes restent modifiables à l&apos;étape suivante.
                </DialogDescription>
              </DialogHeader>

              {sessions.length === 0 ? (
                <EmptyState
                  size="compact"
                  icon={ClipboardList}
                  title="Aucune fiche de soins"
                  description="Ce patient n'a encore aucune séance enregistrée. Continuez pour saisir les actes à la main."
                />
              ) : (
                <div className="max-h-72 space-y-1 overflow-y-auto rounded-md border p-2">
                  {sessions.map(({ record, billedBy }) => {
                    const id = `honoraires-seance-${record.id}`
                    const picked = pickedRecordIds.includes(record.id)
                    return (
                      <div
                        key={record.id}
                        className={cn(
                          "flex items-start gap-3 rounded-md p-3 transition-colors hover:bg-accent",
                          picked && "bg-accent",
                        )}
                      >
                        <Checkbox
                          id={id}
                          checked={picked}
                          onCheckedChange={() => togglePicked(record.id)}
                          className="mt-0.5"
                        />
                        <Label htmlFor={id} className="min-w-0 flex-1 cursor-pointer space-y-1 font-normal">
                          <span className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                            <span className="text-sm font-medium">{formatDate(record.interventionDate)}</span>
                            <span className="text-sm tabular-nums">{formatDT(record.cost)}</span>
                            {/*
                              Marked, not hidden and not disabled: a séance already carried by a live note is
                              almost never the one you want, but « almost never » is not « never » (a note
                              cancelled and retyped, a séance split across two payers), and the flow's whole
                              problem was deciding on the user's behalf. The badge names the note so the
                              consequence of ticking it is legible before the tick, and the footer counts them
                              again on the way out.
                            */}
                            {billedBy && (
                              <Badge variant="secondary" className="text-2xs font-normal">
                                Déjà facturée · {billedBy}
                              </Badge>
                            )}
                          </span>
                          <span className="block truncate text-xs text-muted-foreground">
                            {record.procedureType?.trim() || "Séance sans acte enregistré"}
                          </span>
                        </Label>
                      </div>
                    )
                  })}
                </div>
              )}

              {alreadyBilledPicked > 0 && (
                <p className="text-xs text-warning-ink" role="status">
                  {alreadyBilledPicked === 1
                    ? "Une séance déjà facturée est cochée — elle sera facturée une seconde fois."
                    : `${alreadyBilledPicked} séances déjà facturées sont cochées — elles seront facturées une seconde fois.`}
                </p>
              )}

              <DialogFooter className="gap-2">
                <Button variant="outline" onClick={() => setStep("patient")}>
                  Retour
                </Button>
                <Button onClick={handleBill}>
                  {pickedCount === 0
                    ? "Saisir les actes moi-même"
                    : `Facturer ${pickedCount} séance${pickedCount > 1 ? "s" : ""}`}
                </Button>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>

      <InvoiceFormModal
        open={invoiceOpen}
        onOpenChange={setInvoiceOpen}
        presetPatientId={presetPatientId}
        presetPatientName={presetPatientName}
        presetLines={presetLines}
        onSuccess={() => {
          setInvoiceOpen(false)
          toast.success("Brouillon de facture créé", {
            description: "Vous pouvez l'émettre depuis le module Factures.",
          })
        }}
      />
    </>
  )
}
