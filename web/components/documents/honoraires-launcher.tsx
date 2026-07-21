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
import { Check } from "lucide-react"
import { cn } from "@/lib/utils"
import { toast } from "sonner"
import { patientsApi } from "@/lib/api/patients"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { invoicesApi, type InvoiceLineInput } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { PatientDto } from "@/lib/api/types"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"

/**
 * FR-1: the "Note d'honoraires" card no longer opens the document editor. It opens a patient-selection
 * step, then the existing compliant InvoiceFormModal (draft) pre-filled with the patient's not-yet-invoiced
 * dental records. Numbering / TVA / timbre / El Fatoora are applied later at the separate "issue" step in
 * the Factures module — this flow only creates the draft, it never auto-issues.
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
  const [preparing, setPreparing] = useState(false)

  // Invoice draft step (the reused InvoiceFormModal, opened once a patient is chosen).
  const [invoiceOpen, setInvoiceOpen] = useState(false)
  const [presetPatientId, setPresetPatientId] = useState<string | undefined>()
  const [presetPatientName, setPresetPatientName] = useState<string | undefined>()
  const [presetLines, setPresetLines] = useState<InvoiceLineInput[] | undefined>()

  useEffect(() => {
    if (!open) return
    let cancelled = false
    setSelectedId("")
    setLoadingPatients(true)
    patientsApi
      .list({ limit: 500 })
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
    return () => {
      cancelled = true
    }
  }, [open])

  const handleContinue = async () => {
    const patient = patients.find((p) => p.id === selectedId)
    if (!patient) return
    setPreparing(true)
    try {
      // Seed the draft from the patient's not-yet-invoiced dental records (FR-1.2). A record is already
      // "invoiced" when a non-cancelled invoice references it — mirrors the invoicedDentalRecordIds
      // computation in web/app/patients/[id]/page.tsx.
      const [records, invoices] = await Promise.all([
        dentalRecordsApi.list(selectedId),
        invoicesApi.list({ patientId: selectedId }),
      ])
      const invoicedRecordIds = new Set(
        invoices
          .filter((inv) => inv.dentalRecordId && inv.status !== "Cancelled")
          .map((inv) => inv.dentalRecordId as string),
      )
      const lines: InvoiceLineInput[] = records
        .filter((r) => !invoicedRecordIds.has(r.id))
        .map((r) => ({ designation: r.procedureType, quantity: 1, unitPriceHt: r.cost }))

      setPresetPatientId(patient.id)
      setPresetPatientName(`${patient.firstName} ${patient.lastName}`.trim())
      // Empty → undefined so the modal renders its default single blank line the user can fill.
      setPresetLines(lines.length > 0 ? lines : undefined)
      onOpenChange(false)
      setInvoiceOpen(true)
    } catch (e) {
      toast.error("Erreur", {
        description: e instanceof ApiError ? e.message : "Impossible de préparer la facture",
      })
    } finally {
      setPreparing(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Facturer un patient</DialogTitle>
            <DialogDescription>
              Choisissez le patient dont vous voulez facturer les actes. Une facture brouillon conforme sera
              pré-remplie à partir de ses interventions non encore facturées.
            </DialogDescription>
          </DialogHeader>

          <Command className="rounded-md border">
            <CommandInput placeholder="Rechercher un patient..." />
            <CommandList>
              <CommandEmpty>{loadingPatients ? "Chargement..." : "Aucun patient trouvé."}</CommandEmpty>
              <CommandGroup>
                {patients.map((p) => {
                  const name = `${p.firstName} ${p.lastName}`.trim()
                  return (
                    <CommandItem key={p.id} value={name} onSelect={() => setSelectedId(p.id)}>
                      <Check className={cn("mr-2 h-4 w-4", selectedId === p.id ? "opacity-100" : "opacity-0")} />
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
            <Button onClick={handleContinue} disabled={!selectedId || preparing}>
              {preparing ? "Préparation..." : "Continuer"}
            </Button>
          </DialogFooter>
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
