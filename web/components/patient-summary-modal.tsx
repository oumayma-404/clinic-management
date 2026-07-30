"use client"

import { useState, useEffect, useMemo } from "react"
import { RecordToothChart, type ToothPaint } from "./record-tooth-chart"
import { isAdultTooth } from "@/components/tooth-multiselect"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
      <DialogContent className="max-w-[95vw] w-full max-h-[90vh] overflow-y-auto overflow-x-hidden p-0 gap-0">
        <DialogHeader className="p-6 pb-4 border-b">
          <DialogTitle className="text-2xl">Résumé du patient</DialogTitle>
        </DialogHeader>

        <div className="p-6 space-y-6 overflow-x-hidden">
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
                  <p className="text-base">{formatDate(patient.dateOfBirth)}</p>
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

          {/* Dental Chart */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileText className="h-5 w-5" />
                Schéma dentaire — dents traitées
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-6">
              {/* Adult Teeth Chart */}
              {adultToothPaint.size > 0 && (
                <div>
                  <h3 className="text-sm font-medium mb-3">Dents adultes</h3>
                  <RecordToothChart isAdult={true} paint={adultToothPaint} onToggleTooth={() => {}} disabled />
                </div>
              )}

              {/* Child Teeth Chart */}
              {childToothPaint.size > 0 && (
                <div>
                  <h3 className="text-sm font-medium mb-3">Dents de lait</h3>
                  <RecordToothChart isAdult={false} paint={childToothPaint} onToggleTooth={() => {}} disabled />
                </div>
              )}

              {adultToothPaint.size === 0 && childToothPaint.size === 0 && (
                <p className="text-center text-muted-foreground py-8">Aucune dent traitée pour le moment</p>
              )}
            </CardContent>
          </Card>

          {/* Medical Records Table */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileText className="h-5 w-5" />
                Dossiers dentaires
              </CardTitle>
            </CardHeader>
            <CardContent>
              {dentalRecords.length === 0 ? (
                <p className="text-center text-muted-foreground py-8">Aucun dossier dentaire</p>
              ) : (
                <div className="w-full">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="min-w-[100px]">Date</TableHead>
                        <TableHead className="min-w-[120px]">Type d'acte</TableHead>
                        {/* Dropped with the per-record dentition badge — it is a patient-level fact now. */}
                        <TableHead className="min-w-[120px]">Dents</TableHead>
                        <TableHead className="min-w-[80px]">Coût</TableHead>
                        <TableHead className="min-w-[100px]">Montant payé</TableHead>
                        <TableHead className="min-w-[90px]">Reste</TableHead>
                        <TableHead className="min-w-[150px] max-w-[200px]">Notes</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {dentalRecords.map((record) => (
                        <TableRow key={record.id}>
                          <TableCell className="font-medium whitespace-nowrap">
                            {formatDate(record.interventionDate)}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">{record.procedureType}</TableCell>
                          <TableCell>
                            {record.toothNumbers.length > 0 ? (
                              <div className="flex flex-wrap gap-1 max-w-[120px]">
                                {record.toothNumbers.map((toothNum) => (
                                  <Badge key={toothNum} variant="secondary" className="text-xs">
                                    {toothNum}
                                  </Badge>
                                ))}
                              </div>
                            ) : (
                              <span className="text-muted-foreground text-sm">-</span>
                            )}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">{formatDT(record.cost)}</TableCell>
                          <TableCell className="whitespace-nowrap">{formatDT(record.amountPaid)}</TableCell>
                          <TableCell className="whitespace-nowrap">
                            {(() => {
                              const reste = Math.max(0, record.balance ?? (record.cost - record.amountPaid))
                              return reste > 0
                                ? <span className="font-semibold text-amber-600">{formatDT(reste)}</span>
                                : <span className="text-muted-foreground">{formatDT(0)}</span>
                            })()}
                          </TableCell>
                          <TableCell className="max-w-[200px]">
                            {(() => {
                              const hasNotes = (record.notes && record.notes.length > 0) || (record.importantNotes && record.importantNotes.length > 0)
                              const isExpanded = expandedNotes.has(record.id)
                              const totalNotesCount = (record.importantNotes?.length || 0) + (record.notes?.length || 0)

                              if (!hasNotes) {
                                return <span className="text-muted-foreground text-sm">-</span>
                              }

                              return (
                                <div className="space-y-1">
                                  {isExpanded ? (
                                    <div className="space-y-2">
                                      {record.importantNotes && record.importantNotes.length > 0 && (
                                        <div className="space-y-1">
                                          <p className="text-xs font-semibold text-amber-700 dark:text-amber-400 mb-1">
                                            Notes importantes :
                                          </p>
                                          <ul className="list-disc list-inside space-y-1 ml-2">
                                            {record.importantNotes.map((note, idx) => (
                                              <li key={idx} className="text-xs font-medium text-amber-900 dark:text-amber-100 bg-amber-50 dark:bg-amber-950/40 px-2 py-1 rounded border border-amber-200 dark:border-amber-800">
                                                ⚠ {note}
                                              </li>
                                            ))}
                                          </ul>
                                        </div>
                                      )}
                                      {record.notes && record.notes.length > 0 && (
                                        <div className="space-y-1">
                                          {record.importantNotes && record.importantNotes.length > 0 && (
                                            <p className="text-xs font-semibold text-muted-foreground mb-1">
                                              Notes :
                                            </p>
                                          )}
                                          <ul className="list-disc list-inside space-y-1 ml-2">
                                            {record.notes.map((note, idx) => (
                                              <li key={idx} className="text-sm text-foreground bg-muted/50 px-2 py-1 rounded">
                                                {note}
                                              </li>
                                            ))}
                                          </ul>
                                        </div>
                                      )}
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-6 text-xs text-muted-foreground hover:text-foreground"
                                        onClick={(e) => {
                                          e.stopPropagation()
                                          setExpandedNotes(prev => {
                                            const next = new Set(prev)
                                            next.delete(record.id)
                                            return next
                                          })
                                        }}
                                      >
                                        <ChevronUp className="h-3 w-3 mr-1" />
                                        Réduire
                                      </Button>
                                    </div>
                                  ) : (
                                    <div className="space-y-1">
                                      <div className="flex items-center gap-2">
                                        <span className="text-sm text-muted-foreground">
                                          {totalNotesCount} {totalNotesCount === 1 ? 'note' : 'notes'}
                                        </span>
                                        {record.importantNotes && record.importantNotes.length > 0 && (
                                          <Badge variant="outline" className="text-xs bg-amber-50 dark:bg-amber-950/20 text-amber-700 dark:text-amber-400 border-amber-200 dark:border-amber-800">
                                            {record.importantNotes.length} importantes
                                          </Badge>
                                        )}
                                      </div>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-6 text-xs text-muted-foreground hover:text-foreground"
                                        onClick={(e) => {
                                          e.stopPropagation()
                                          setExpandedNotes(prev => new Set(prev).add(record.id))
                                        }}
                                      >
                                        <ChevronDown className="h-3 w-3 mr-1" />
                                        Voir les notes
                                      </Button>
                                    </div>
                                  )}
                                </div>
                              )
                            })()}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </DialogContent>
    </Dialog>
  )
}

