"use client"

import { useState, useEffect, useMemo } from "react"
import { RecordToothChart, type ToothPaint } from "./record-tooth-chart"
import { isAdultTooth } from "@/components/tooth-multiselect"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
// The dental-records table is gone entirely (Exception 3) — this modal renders a card list at every width.
import { CardList } from "@/components/ui/card-list"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { PatientDto, DentalRecordDto } from "@/lib/api/types"
import { formatDT, formatDate } from "@/lib/format"
import { User, Phone, Mail, Calendar, MapPin, CreditCard, FileText, ChevronDown, ChevronUp } from "lucide-react"
import { genderLabel } from "@/components/appointment-labels"

interface PatientSummaryModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patient: PatientDto | null
  dentalRecords: DentalRecordDto[]
}

// Fixed highlight fill for a tooth that has been worked on (read-only summary chart).
const WORKED_TOOTH_COLOR = "#60a5fa"

export function PatientSummaryModal({ open, onOpenChange, patient, dentalRecords }: PatientSummaryModalProps) {
  const [expandedNotes, setExpandedNotes] = useState<Set<string>>(new Set())
  // Collapsed by default on every device: the chips answer the question, and the schema is the detail behind
  // it. Not gated on viewport width — a desktop reader also opens this modal to glance, and a control whose
  // presence depends on the breakpoint is one more thing to reason about.
  const [schemaOpen, setSchemaOpen] = useState(false)

  // Read-only paint maps for the record tooth chart: each worked tooth is highlighted with a fixed "worked"
  // fill + the number of records it appears in (the per-procedure detail lives in the table below).
  // Teeth are split by the TOOTH's own dentition (FDI range), not by the record's `isAdultTeeth` flag — one
  // session can chart a permanent and a deciduous tooth together, and flag-based filtering dropped half of it.
  const { adultToothPaint, childToothPaint } = useMemo(() => {
    const counts = new Map<number, number>()
    for (const record of dentalRecords) {
      for (const toothNum of record.toothNumbers) {
        counts.set(toothNum, (counts.get(toothNum) ?? 0) + 1)
      }
    }

    const adult = new Map<number, ToothPaint>()
    const child = new Map<number, ToothPaint>()
    for (const [tooth, count] of counts) {
      const paint: ToothPaint = { selected: false, color: WORKED_TOOTH_COLOR, count }
      if (isAdultTooth(tooth)) {
        adult.set(tooth, paint)
      } else {
        child.set(tooth, paint)
      }
    }
    return { adultToothPaint: adult, childToothPaint: child }
  }, [dentalRecords])

  /**
   * The flat, tooth-ordered list the summary actually leads with.
   *
   * Derived from the same `counts` source as the two paint maps rather than by merging them back together —
   * one traversal of the records, one ordering rule. Sorted numerically so a reader scans quadrant by
   * quadrant the way FDI numbering already groups them.
   */
  const treatedTeeth = useMemo(() => {
    const merged = [...adultToothPaint.entries(), ...childToothPaint.entries()]
    return merged
      .map(([tooth, paint]) => ({
        tooth,
        count: paint.count ?? 1,
        isDeciduous: !isAdultTooth(tooth),
      }))
      .sort((a, b) => a.tooth - b.tooth)
  }, [adultToothPaint, childToothPaint])

  if (!patient) return null

  const patientName = `${patient.firstName} ${patient.lastName}`.trim()
  const age = patient.dateOfBirth 
    ? (() => {
        try {
          const birthDate = new Date(patient.dateOfBirth)
          const today = new Date()
          let age = today.getFullYear() - birthDate.getFullYear()
          const monthDiff = today.getMonth() - birthDate.getMonth()
          if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
            age--
          }
          return age
        } catch {
          return null
        }
      })()
    : null

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        mobile="sheet"
        className="gap-0 overflow-x-hidden p-0 md:max-h-[90dvh] md:w-full md:max-w-[95vw]"
      >
        <DialogHeader className="p-6 pb-4 border-b">
          <DialogTitle className="text-2xl">Résumé du patient</DialogTitle>
        </DialogHeader>

        <DialogBody className="p-6 space-y-6 overflow-x-hidden">
          {/*
            ALERTS FIRST — above the identity, not below the insurance.

            ⚠️ This modal showed **no** allergies, no flags and no antécédents at all; grepping it for `allerg`
            returned nothing. It is the one-click quick look from the patients table and from the phone's ⋯ menu,
            i.e. the fastest way to see a patient without opening their file — and it was the only clinical surface
            in the app that could not answer « est-il allergique ? », while the full page and the fiche modal both
            could. Placed before the identity card because the ordering is the point: an alert below the fold is a
            note, not an alert.
          */}
          <PatientAlertPanel patient={patient} />

          {/* Patient Basic Info */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <User className="h-5 w-5" />
                Informations du patient
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Nom complet</p>
                  <p className="text-base font-semibold">{patientName}</p>
                </div>
                
                {age !== null && (
                  <div>
                    <p className="text-sm font-medium text-muted-foreground mb-1">Âge</p>
                    <p className="text-base">{age} ans</p>
                  </div>
                )}

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Sexe</p>
                  <p className="text-base">{genderLabel(patient.gender)}</p>
                </div>

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Date de naissance</p>
                  <p className="text-base">
                    {formatDate(patient.dateOfBirth)} {age !== null ? `(${age} ans)` : "(âge inconnu)"}
                  </p>
                </div>
              </div>

              <Separator className="my-4" />

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                    <Phone className="h-4 w-4" />
                    Numéro de téléphone
                  </p>
                  <p className="text-base">{patient.phoneNumber || "Non renseigné"}</p>
                </div>

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                    <Mail className="h-4 w-4" />
                    Email
                  </p>
                  <p className="text-base">{patient.email || "Non renseigné"}</p>
                </div>

                {patient.address && (
                  <div className="md:col-span-2">
                    <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                      <MapPin className="h-4 w-4" />
                      Adresse
                    </p>
                    <p className="text-base">
                      {[
                        patient.address.street,
                        patient.address.city,
                        patient.address.state,
                        patient.address.zipCode
                      ].filter(Boolean).join(", ") || "Non renseigné"}
                    </p>
                  </div>
                )}

                {patient.emergencyContactName && (
                  <div>
                    <p className="text-sm font-medium text-muted-foreground mb-1">Contact d'urgence</p>
                    <p className="text-base">
                      {patient.emergencyContactName}
                      {patient.emergencyContactPhone && ` - ${patient.emergencyContactPhone}`}
                    </p>
                  </div>
                )}

                {patient.insuranceInfo && (
                  <div>
                    <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                      <CreditCard className="h-4 w-4" />
                      Assurance
                    </p>
                    <p className="text-base">
                      {patient.insuranceInfo.provider}
                      {patient.insuranceInfo.policyNumber && ` (${patient.insuranceInfo.policyNumber})`}
                    </p>
                  </div>
                )}
              </div>
            </CardContent>
          </Card>

          {/*
            Dents traitées — the ANSWER first, the schema on demand.

            ⚠️ This card used to render up to **two** full `RecordToothChart`s, adult then child. Since P6 each
            chart shows a single arch below `md:` with a Haut/Bas switch, so on a phone reading « which teeth
            have been worked on? » cost up to four taps — on a screen whose entire purpose is to be glanced at.
            The chips answer it in one look, and the schema is one tap behind them for anyone who wants the
            spatial view.

            Two charts were never needed to separate the dentitions in the first place: an FDI number states
            which it is (5x–8x are deciduous), which is exactly what `isAdultTooth` reads. So the chips are one
            list, ordered by tooth number, and the deciduous ones are marked rather than segregated.
          */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileText className="h-5 w-5" />
                Dents traitées
                {treatedTeeth.length > 0 && (
                  <Badge variant="secondary" className="ms-auto">{treatedTeeth.length}</Badge>
                )}
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              {treatedTeeth.length === 0 ? (
                <p className="py-6 text-center text-muted-foreground">Aucune dent traitée pour le moment</p>
              ) : (
                <>
                  <ul className="flex flex-wrap gap-2" aria-label="Dents traitées">
                    {treatedTeeth.map(({ tooth, count, isDeciduous }) => (
                      <li key={tooth}>
                        {/*
                          A 44px target even though nothing here is tappable: these sit in a dialog next to
                          controls that ARE, and a row of 24px pills reads as "broken buttons" rather than as
                          data. `tabular-nums` keeps the two-digit numbers on one optical grid.
                        */}
                        {/*
                          ⚠️ Token ink, not `WORKED_TOOTH_COLOR`. The hex stays where it belongs — as the SVG
                          FILL on the schema below, where it is a large coloured area — but it was also being
                          used as this chip's `color`, and the chip's entire content is the tooth number. A
                          `#60a5fa` numeral on a white card measures ~2.5:1, i.e. the datum was the least legible
                          thing in the modal. `text-foreground` inside a `border-primary/60` box keeps the chip
                          reading as "charted" without spending contrast on the number itself.
                        */}
                        <span className="flex size-11 flex-col items-center justify-center rounded-lg border-2 border-primary/60 text-sm font-semibold tabular-nums text-foreground">
                          {tooth}
                          {count > 1 && (
                            <span className="text-2xs font-normal leading-none opacity-80">×{count}</span>
                          )}
                        </span>
                        <span className="sr-only">
                          {isDeciduous ? "dent de lait" : "dent définitive"}
                          {count > 1 ? `, ${count} interventions` : ", 1 intervention"}
                        </span>
                      </li>
                    ))}
                  </ul>

                  {childToothPaint.size > 0 && (
                    <p className="text-xs text-muted-foreground">
                      Dont {childToothPaint.size} dent{childToothPaint.size > 1 ? "s" : ""} de lait
                      {" "}(numéros 51 à 85).
                    </p>
                  )}

                  <Button
                    type="button"
                    variant="outline"
                    className="w-full"
                    onClick={() => setSchemaOpen((open) => !open)}
                    aria-expanded={schemaOpen}
                  >
                    {schemaOpen ? "Masquer le schéma dentaire" : "Voir le schéma dentaire"}
                  </Button>

                  {schemaOpen && (
                    <div className="space-y-6 border-t pt-4">
                      {adultToothPaint.size > 0 && (
                        <div>
                          <h3 className="mb-3 text-sm font-medium">Dents définitives</h3>
                          <RecordToothChart view="adult" paint={adultToothPaint} onToggleTooth={() => {}} disabled />
                        </div>
                      )}
                      {childToothPaint.size > 0 && (
                        <div>
                          <h3 className="mb-3 text-sm font-medium">Dents de lait</h3>
                          <RecordToothChart view="child" paint={childToothPaint} onToggleTooth={() => {}} disabled />
                        </div>
                      )}
                    </div>
                  )}
                </>
              )}
            </CardContent>
          </Card>

          {/* Medical Records Table */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileText className="h-5 w-5" />
                Actes dentaires
              </CardTitle>
            </CardHeader>
            <CardContent>
              {dentalRecords.length === 0 ? (
                <p className="text-center text-muted-foreground py-8">Aucun acte dentaire</p>
              ) : (
                <div className="w-full">
                  {/*
                    ⚠️ Exception 3 — this surface adopts the card list at EVERY width, so the table is gone
                    rather than hidden below `md:`.

                    Its seven columns each carried an explicit `min-w-*` summing ~760px, inside a
                    `DialogContent` capped at 95vw with `overflow-x-hidden` — so the last columns were
                    CLIPPED, not scrollable. There was no width at which the table worked, which makes
                    keeping a desktop copy of it a copy of the defect.
                  */}
                  <CardList
                    ariaLabel="Actes dentaires du patient"
                    items={dentalRecords}
                    getKey={(r) => r.id}
                    title={(r) => r.procedureType}
                    subtitle={(r) => formatDate(r.interventionDate)}
                    status={(r) =>
                      r.toothNumbers.length > 0 ? (
                        <>
                          {r.toothNumbers.map((toothNum) => (
                            <Badge key={toothNum} variant="secondary" className="text-xs">
                              {toothNum}
                            </Badge>
                          ))}
                        </>
                      ) : null
                    }
                    fields={(r) => {
                      const reste = Math.max(0, r.balance ?? r.cost - r.amountPaid)
                      const hasNotes =
                        (r.notes && r.notes.length > 0) || (r.importantNotes && r.importantNotes.length > 0)
                      const isExpanded = expandedNotes.has(r.id)
                      const totalNotesCount = (r.importantNotes?.length || 0) + (r.notes?.length || 0)
                      return [
                        { label: "Coût", value: formatDT(r.cost) },
                        { label: "Payé", value: formatDT(r.amountPaid) },
                        {
                          label: "Reste",
                          value:
                            reste > 0 ? (
                              // `--warning-ink`: `text-amber-600` carried no `dark:` pair and measured ~3.2:1 on
                              // the card, on the figure that says money is still owed.
                              <span className="font-semibold text-warning-ink">{formatDT(reste)}</span>
                            ) : (
                              <span className="text-muted-foreground">{formatDT(0)}</span>
                            ),
                        },
                        // The expand/collapse survives the conversion — the notes are the reason this summary
                        // is opened, and flattening them to a count would remove the only thing it adds.
                        hasNotes && {
                          label: "Notes",
                          value: isExpanded ? (
                            <div className="space-y-2 text-start">
                              {r.importantNotes && r.importantNotes.length > 0 && (
                                <ul className="list-inside list-disc space-y-1">
                                  {r.importantNotes.map((note, idx) => (
                                    <li
                                      key={idx}
                                      className="rounded border border-amber-200 bg-amber-50 px-2 py-1 text-xs font-medium text-amber-900 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-100"
                                    >
                                      ⚠ {note}
                                    </li>
                                  ))}
                                </ul>
                              )}
                              {r.notes && r.notes.length > 0 && (
                                <ul className="list-inside list-disc space-y-1">
                                  {r.notes.map((note, idx) => (
                                    <li key={idx} className="text-xs text-muted-foreground">
                                      {note}
                                    </li>
                                  ))}
                                </ul>
                              )}
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-6 text-xs text-muted-foreground hover:text-foreground"
                                onClick={() =>
                                  setExpandedNotes((prev) => {
                                    const next = new Set(prev)
                                    next.delete(r.id)
                                    return next
                                  })
                                }
                              >
                                <ChevronUp className="mr-1 h-3 w-3" />
                                Réduire
                              </Button>
                            </div>
                          ) : (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-6 text-xs text-muted-foreground hover:text-foreground"
                              onClick={() => setExpandedNotes((prev) => new Set(prev).add(r.id))}
                            >
                              <ChevronDown className="mr-1 h-3 w-3" />
                              {totalNotesCount} {totalNotesCount === 1 ? "note" : "notes"}
                              {r.importantNotes && r.importantNotes.length > 0
                                ? ` · ${r.importantNotes.length} importantes`
                                : ""}
                            </Button>
                          ),
                        },
                      ]
                    }}
                  />
                </div>
              )}
            </CardContent>
          </Card>
        </DialogBody>
      </DialogContent>
    </Dialog>
  )
}

