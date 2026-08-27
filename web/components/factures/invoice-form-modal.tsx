"use client"

import type React from "react"

import { useState, useEffect, useRef } from "react"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Check, ChevronsUpDown, Trash2, Plus, Search, X } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi, type InvoiceLineInput, type CreateInvoiceRequest } from "@/lib/api/invoices"
import { patientsApi } from "@/lib/api/patients"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto, PatientDto, ProcedureTypeDto } from "@/lib/api/types"
import { formatAmount, formatDT, parseAmountInput, quoteFr } from "@/lib/format"
import { cn } from "@/lib/utils"

interface LineRow {
  designation: string
  quantity: string
  unitPriceHt: string
  /**
   * Catalog CNAM/DCH act attached to the line (drives the reimbursable split); null for free text.
   *
   * ⚠️ Round-tripped, no longer *chosen* here: the per-line picker searches the clinic's own procedure types
   * (`/procedure-types`) rather than the CNAM nomenclature. A line loaded from a saved note keeps whatever code
   * it carries — and can still be detached — but nothing in this form attaches a new one, so a note created
   * here has no reimbursable split unless the code came with the line.
   */
  dentalActCodeId: string | null
  codeActe: string | null
  /**
   * The price currently shown came from a catalogue act rather than from the user's keyboard.
   *
   * Picking an act twice on the same line has to replace the first act's price — otherwise the line reads as
   * act B at act A's tarif — while a price the user typed must survive the same gesture. Those two are
   * indistinguishable from the value alone, so the provenance is recorded. Cleared by the price input's own
   * onChange: the moment it is edited, it is theirs.
   */
  pricedFromCatalog: boolean
}

interface InvoiceFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingInvoice?: InvoiceDto | null
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  /** Pre-filled act lines (create mode only) — e.g. seeded from a dental record. */
  presetLines?: InvoiceLineInput[]
  /** Optional source dental-record link, persisted on the created draft (create mode only). */
  dentalRecordId?: string
  /**
   * The visit this note bills, persisted on the created draft (create mode only) — AC-P6.12. The backend
   * column has always existed and nothing populated it, so an invoice could never say which consultation it
   * was for. Passed only when the form is opened from an appointment context; the server verifies the
   * appointment belongs to this clinic and this patient.
   */
  appointmentId?: string
  onSuccess?: () => void
}

const emptyLine = (): LineRow => ({
  designation: "",
  quantity: "1",
  unitPriceHt: "",
  dentalActCodeId: null,
  codeActe: null,
  pricedFromCatalog: false,
})

/**
 * Upgrade the message when the same edit conflicts twice running. The first 409 means "someone saved before
 * you"; the second means "someone is editing this right now", and telling the user to reload again would be
 * repeating advice that has already failed.
 */
function conflictMessage(err: unknown, fallback: string, consecutive: React.MutableRefObject<number>): string {
  if (err instanceof ApiError && err.status === 409) {
    consecutive.current += 1
    if (consecutive.current > 1) {
      return "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement "
        + "dessus en même temps — coordonnez-vous avant de réessayer."
    }
    return err.message || fallback
  }
  consecutive.current = 0
  return err instanceof ApiError ? err.message : fallback
}

export function InvoiceFormModal({
  open,
  onOpenChange,
  editingInvoice,
  presetPatientId,
  presetPatientName,
  presetLines,
  dentalRecordId,
  appointmentId,
  onSuccess,
}: InvoiceFormModalProps) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [procedures, setProcedures] = useState<ProcedureTypeDto[]>([])
  /*
   * A failed catalogue read must say so rather than render as an empty picker (the repo's standing rule, and
   * `document-editor-content`'s `CatalogLoadFailed`). It matters more here than it did when this picker offered
   * the CNAM nomenclature: the clinic's own acts are now the ONLY source, so « aucun acte trouvé » on a network
   * blip reads as « ce cabinet n'a aucun tarif », and the answer to that is to retype a price from memory.
   */
  const [proceduresFailed, setProceduresFailed] = useState(false)
  const [proceduresReload, setProceduresReload] = useState(0)
  /** Same rule for the patient list — « aucun patient » must never be how a network blip renders. */
  const [patientsFailed, setPatientsFailed] = useState(false)
  const [patientsReload, setPatientsReload] = useState(0)
  const [patientId, setPatientId] = useState("")
  const [patientPickerOpen, setPatientPickerOpen] = useState(false)
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const guard = useDirtyGuard(open, onOpenChange)
  const conflictStreak = useRef(0)
  // The version this draft saves with, kept equal to the row's current one. ⚠️ The VERSION only — the read
  // lands after hydration, so its lines would replace whatever the user has already edited.
  const { source: freshInvoice, resync } = useFreshVersion(
    open,
    editingInvoice?.id,
    editingInvoice,
    () => invoicesApi.get(editingInvoice!.id),
  )

  const isEditing = !!editingInvoice
  const selectedPatient = patients.find((p) => p.id === patientId)
  /*
   * Falls back to the invoice's own `patientName` when the id is not in the loaded page — the list is capped at
   * 500, so a large clinic editing an older draft would otherwise see « Sélectionner un patient » over a draft
   * that already has one, and reassign it by accident.
   */
  const selectedPatientName = selectedPatient
    ? `${selectedPatient.firstName} ${selectedPatient.lastName}`
    : patientId && editingInvoice?.patientId === patientId
      ? editingInvoice.patientName ?? ""
      : ""

  /*
   * The clinic's own act catalogue, feeding the per-line picker. Its own effect, NOT the seeding one below:
   * the retry bumps `proceduresReload`, and re-running the seed would discard every line the user has typed
   * to reload a list.
   */
  useEffect(() => {
    if (!open) return
    let cancelled = false
    procedureTypesApi
      .list()
      .then((list) => {
        if (cancelled) return
        setProcedures(list)
        setProceduresFailed(false)
      })
      .catch(() => {
        if (cancelled) return
        setProcedures([])
        setProceduresFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [open, proceduresReload])

  /*
   * The patient list, in an effect **of its own** so « Réessayer » can re-run it.
   *
   * ⚠️ The failure is recorded, exactly like `proceduresFailed` above: `.catch(() => setPatients([]))` printed
   * « Aucun patient trouvé » on the trigger *and* inside the picker — in a clinic with three hundred files, on the
   * form that has to name one before a note d'honoraires can exist.
   *
   * Split out rather than given a reload token in place, for the reason the procedures effect already documents:
   * this used to sit inside the prefill effect, and adding a token to *that* one's deps would re-seed the lines and
   * discard everything the user has typed.
   */
  useEffect(() => {
    if (!open || presetPatientId) return
    let cancelled = false
    patientsApi
      .list({ limit: 500 })
      .then((data) => {
        if (cancelled) return
        setPatients(data)
        setPatientsFailed(false)
      })
      .catch(() => {
        if (!cancelled) setPatientsFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [open, presetPatientId, patientsReload])

  useEffect(() => {
    if (!open) return

    if (editingInvoice) {
      setPatientId(editingInvoice.patientId)
      setLines(
        editingInvoice.lines.length > 0
          ? editingInvoice.lines.map((l) => ({
              designation: l.designation,
              quantity: String(l.quantity),
              unitPriceHt: formatAmount(l.unitPriceHt),
              dentalActCodeId: l.dentalActCodeId ?? null,
              codeActe: l.codeActe ?? null,
              // A saved price is a decision already taken — picking an act must not overwrite it.
              pricedFromCatalog: false,
            }))
          : [emptyLine()],
      )
    } else {
      setPatientId(presetPatientId ?? "")
      setLines(
        presetLines && presetLines.length > 0
          ? presetLines.map((l) => ({
              designation: l.designation,
              quantity: String(l.quantity),
              unitPriceHt: formatAmount(l.unitPriceHt),
              dentalActCodeId: l.dentalActCodeId ?? null,
              codeActe: l.codeActe ?? null,
              // A séance's own fee, likewise: it is what was recorded on the fiche, not a catalogue default.
              pricedFromCatalog: false,
            }))
          : [emptyLine()],
      )
    }
    setError(null)
    // Seeds once when the dialog opens; presetLines are read from the opening render (like presetPatientName).
  }, [open, editingInvoice, presetPatientId])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

  /**
   * Put one of the clinic's own acts on a line: its name becomes the désignation, its tarif the price.
   *
   * <p>The désignation is **replaced**, not filled-when-empty: choosing an act is an explicit statement about
   * what this line bills, and keeping the previous text would leave the line naming one act at another's tarif.
   * The price is replaced only when the field is empty or still holds a catalogue tarif — a figure the user
   * typed is theirs and survives (see `LineRow.pricedFromCatalog`). It stays editable either way: the tarif is
   * a default, and a fee agreed with this patient overrides it.</p>
   */
  const selectProcedure = (index: number, procedure: ProcedureTypeDto) => {
    setLines((prev) =>
      prev.map((l, i) => {
        if (i !== index) return l
        const tarif = procedure.defaultCost
        const takeTarif = tarif != null && (l.unitPriceHt.trim() === "" || l.pricedFromCatalog)
        return {
          ...l,
          designation: procedure.name,
          unitPriceHt: takeTarif ? formatAmount(tarif) : l.unitPriceHt,
          pricedFromCatalog: takeTarif,
        }
      }),
    )
    setPickerOpenIndex(null)
  }

  const detachAct = (index: number) => updateLine(index, { dentalActCodeId: null, codeActe: null })

  const totalHt = lines.reduce((sum, l) => {
    const qty = Number(l.quantity)
    const price = parseAmountInput(l.unitPriceHt)
    if (!Number.isFinite(qty) || !Number.isFinite(price)) return sum
    return sum + qty * price
  }, 0)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!patientId) {
      setError("Sélectionnez un patient.")
      return
    }

    const parsedLines: InvoiceLineInput[] = lines
      .map((l) => ({
        designation: l.designation.trim(),
        quantity: Number(l.quantity),
        unitPriceHt: parseAmountInput(l.unitPriceHt),
        dentalActCodeId: l.dentalActCodeId,
        codeActe: l.codeActe,
      }))
      .filter((l) => l.designation !== "")

    if (parsedLines.length === 0) {
      setError("Ajoutez au moins une ligne d'acte.")
      return
    }

    for (const l of parsedLines) {
      if (!Number.isFinite(l.quantity) || l.quantity <= 0) {
        setError(`Quantité invalide pour ${quoteFr(l.designation)}.`)
        return
      }
      if (!Number.isFinite(l.unitPriceHt) || l.unitPriceHt < 0) {
        setError(`Prix unitaire invalide pour ${quoteFr(l.designation)}.`)
        return
      }
    }

    setLoading(true)
    try {
      if (isEditing && editingInvoice) {
        await invoicesApi.update(editingInvoice.id, {
          patientId,
          lines: parsedLines,
          version: freshInvoice?.version ?? editingInvoice.version,
        })
        toast.success("Brouillon mis à jour")
      } else {
        const payload: CreateInvoiceRequest = { patientId, lines: parsedLines }
        // Persist the source dental-record link on the new draft (spec AC-2).
        if (dentalRecordId) payload.dentalRecordId = dentalRecordId
        if (appointmentId) payload.appointmentId = appointmentId
        await invoicesApi.create(payload)
        toast.success("Brouillon de facture créé")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(conflictMessage(err, "Échec de l'enregistrement de la facture.", conflictStreak))
      // A non-conflict failure may still have moved the row, so the next click must not inherit a stale
      // version. A real 409 is left alone — resyncing it would let the retry overwrite the other person.
      if (!(err instanceof ApiError && err.status === 409)) await resync()
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (AC-23). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-h-[90dvh] md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isEditing ? "Modifier le brouillon" : "Nouvelle facture"}</DialogTitle>
          <DialogDescription>
            Un brouillon ne consomme aucun numéro. Le numéro est attribué à l'émission.
          </DialogDescription>
        </DialogHeader>

        {/* The form owns the remaining height so `DialogBody` scrolls and the footer stays on screen (AC-21). */}
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col gap-4">
          <DialogBody className="space-y-4">
          <FormErrorBanner message={error} />

          <div className="space-y-1.5">
            <Label htmlFor="patient">
              Patient <span className="text-destructive">*</span>
            </Label>
            {presetPatientId ? (
              <Input id="patient" value={presetPatientName ?? "Patient"} disabled />
            ) : (
              /*
               * A searchable Popover + `Command`, not a `<Select>`.
               *
               * The fetch above asks for up to **500 patients**, and a plain Select is an unfiltered scroll
               * through all of them — on the one form where picking the wrong name issues a fiscal document
               * against the wrong person. The act picker fifty lines below already uses this exact pattern for
               * a far shorter list, so the file contained both the problem and its answer.
               *
               * `modal` is load-bearing: the parent Dialog disables pointer events outside its content, and a
               * non-modal Popover portalled to <body> inherits that — the list would be keyboard-only.
               */
              <Popover open={patientPickerOpen} onOpenChange={setPatientPickerOpen} modal>
                <PopoverTrigger asChild>
                  <Button
                    id="patient"
                    type="button"
                    variant="outline"
                    role="combobox"
                    aria-expanded={patientPickerOpen}
                    disabled={loading}
                    className="h-9 w-full justify-between font-normal"
                  >
                    <span className={cn("truncate", !patientId && "text-muted-foreground")}>
                      {selectedPatientName ||
                        (patientsFailed
                          ? "Liste indisponible"
                          : patients.length === 0
                            ? "Aucun patient trouvé"
                            : "Sélectionner un patient")}
                    </span>
                    <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent
                  className="p-0"
                  align="start"
                  style={{ width: "var(--radix-popover-trigger-width)" }}
                >
                  <Command>
                    <CommandInput placeholder="Rechercher un patient…" />
                    <CommandList>
                      {patientsFailed ? (
                        <LoadFailureNotice
                          message="La liste des patients n'a pas pu être chargée."
                          detail="Ce n'est pas un cabinet sans patients — la lecture a échoué."
                          onRetry={() => setPatientsReload((n) => n + 1)}
                          className="m-2"
                        />
                      ) : (
                        <CommandEmpty>Aucun patient trouvé.</CommandEmpty>
                      )}
                      <CommandGroup>
                        {patients.map((p) => {
                          const fullName = `${p.firstName} ${p.lastName}`
                          return (
                            <CommandItem
                              key={p.id}
                              value={fullName}
                              onSelect={() => {
                                setPatientId(p.id)
                                setPatientPickerOpen(false)
                              }}
                            >
                              <Check
                                className={cn(
                                  "mr-2 h-4 w-4",
                                  patientId === p.id ? "opacity-100" : "opacity-0",
                                )}
                              />
                              {fullName}
                            </CommandItem>
                          )
                        })}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
            )}
          </div>

          <div className="space-y-2">
            <Label>Actes</Label>
            <div className="space-y-3">
              {lines.map((line, index) => {
                const qty = Number(line.quantity)
                const price = parseAmountInput(line.unitPriceHt)
                const lineTotal = Number.isFinite(qty) && Number.isFinite(price) ? qty * price : 0
                return (
                  <div key={index} className="rounded-lg border p-3 space-y-2">
                    <div className="flex items-start gap-2">
                      <div className="flex-1 space-y-1">
                        <div className="flex items-center gap-2">
                          <Input
                            value={line.designation}
                            onChange={(e) => updateLine(index, { designation: e.target.value })}
                            placeholder="Ex. Détartrage (ou choisir un de vos actes)"
                            disabled={loading}
                          />
                          <Popover
                            open={pickerOpenIndex === index}
                            onOpenChange={(o) => setPickerOpenIndex(o ? index : null)}
                            modal
                          >
                            <PopoverTrigger asChild>
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                className="h-9 px-3 shrink-0"
                                disabled={loading}
                                title="Choisir un de vos actes (le tarif reste modifiable)"
                              >
                                <Search className="h-4 w-4" />
                                <span className="sr-only">Choisir un de vos actes</span>
                              </Button>
                            </PopoverTrigger>
                            <PopoverContent className="p-0 w-80" align="end">
                              <Command>
                                <CommandInput placeholder="Rechercher un acte…" />
                                <CommandList>
                                  {proceduresFailed ? (
                                    <div className="space-y-2 p-4 text-center">
                                      <p className="text-sm font-medium text-foreground">
                                        Vos actes n&apos;ont pas pu être chargés.
                                      </p>
                                      <p className="text-xs text-muted-foreground">
                                        Ce n&apos;est pas un catalogue vide — la lecture a échoué. Réessayez
                                        avant de saisir un tarif de mémoire.
                                      </p>
                                      <Button
                                        type="button"
                                        variant="outline"
                                        size="sm"
                                        onClick={() => setProceduresReload((n) => n + 1)}
                                      >
                                        Réessayer
                                      </Button>
                                    </div>
                                  ) : (
                                    <>
                                      <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                      <CommandGroup heading="Vos actes">
                                        {procedures.map((procedure) => (
                                          <CommandItem
                                            key={procedure.id}
                                            value={`${procedure.name} ${procedure.description ?? ""}`}
                                            onSelect={() => selectProcedure(index, procedure)}
                                          >
                                            <div className="flex min-w-0 flex-col">
                                              <span className="truncate text-sm font-medium">{procedure.name}</span>
                                              <span className="text-xs text-muted-foreground">
                                                {procedure.defaultCost != null
                                                  ? formatDT(procedure.defaultCost)
                                                  : "Pas de tarif par défaut"}
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
                        {line.codeActe && (
                          <Badge variant="secondary" className="gap-1 font-mono text-xs">
                            {line.codeActe}
                            <button
                              type="button"
                              onClick={() => detachAct(index)}
                              className="ml-1 rounded-full hover:text-destructive"
                              title="Détacher l'acte CNAM (reste à charge intégral)"
                            >
                              <X className="h-3 w-3" />
                            </button>
                          </Badge>
                        )}
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => removeLine(index)}
                        disabled={loading || lines.length === 1}
                        aria-label="Supprimer la ligne"
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                    <div className="flex flex-wrap items-center gap-3">
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs text-muted-foreground">Qté</span>
                        <Input
                          type="number"
                          min="1"
                          step="1"
                          value={line.quantity}
                          onChange={(e) => updateLine(index, { quantity: e.target.value })}
                          className="w-20"
                          disabled={loading}
                        />
                      </div>
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs text-muted-foreground">P.U. HT (DT)</span>
                        {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses
                            the comma this product prints with, and a rejected keystroke returns an EMPTY value —
                            so a line looked priced and billed 0. « Qté » beside it stays a real number input:
                            it is an integer count, not money. */}
                        <Input
                          type="text"
                          inputMode="decimal"
                          value={line.unitPriceHt}
                          // Editing the price makes it the user's, so a later act pick no longer overwrites it.
                          onChange={(e) =>
                            updateLine(index, { unitPriceHt: e.target.value, pricedFromCatalog: false })
                          }
                          className="w-32"
                          disabled={loading}
                        />
                      </div>
                      <span className="ml-auto text-sm text-muted-foreground">
                        Total HT : <span className="font-medium text-foreground">{formatDT(lineTotal)}</span>
                      </span>
                    </div>
                  </div>
                )
              })}
            </div>
            <Button type="button" variant="outline" size="sm" onClick={addLine} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter une ligne
            </Button>
          </div>

          <div className="flex justify-end text-sm">
            <span className="text-muted-foreground">Total HT :&nbsp;</span>
            <span className="font-semibold">{formatDT(totalHt)}</span>
          </div>
          </DialogBody>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : isEditing ? "Enregistrer" : "Créer le brouillon"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
