"use client"

import { useState, useEffect, useMemo } from "react"
import { DentalChart } from "./dental-chart"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { PatientDto, DentalRecordDto } from "@/lib/api/types"
import { format, parseISO } from "date-fns"
import { User, Phone, Mail, Calendar, MapPin, CreditCard, FileText, ChevronDown, ChevronUp } from "lucide-react"

interface PatientSummaryModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patient: PatientDto | null
  dentalRecords: DentalRecordDto[]
}

const formatDate = (dateString: string | undefined) => {
  if (!dateString) return "N/A"
  try {
    const date = parseISO(dateString)
    return format(date, "MMM d, yyyy")
  } catch {
    try {
      const date = new Date(dateString)
      return format(date, "MMM d, yyyy")
    } catch {
      return "N/A"
    }
  }
}

const formatDateTime = (dateString: string | undefined) => {
  if (!dateString) return "N/A"
  try {
    const date = parseISO(dateString)
    return format(date, "MMM d, yyyy h:mm a")
  } catch {
    try {
      const date = new Date(dateString)
      return format(date, "MMM d, yyyy h:mm a")
    } catch {
      return "N/A"
    }
  }
}

export function PatientSummaryModal({ open, onOpenChange, patient, dentalRecords }: PatientSummaryModalProps) {
  const [expandedNotes, setExpandedNotes] = useState<Set<string>>(new Set())

  // Collect all teeth that have been worked on from all dental records
  // Separate by adult vs child teeth
  const adultWorkedTeeth = useMemo(() => {
    const teethMap = new Map<string, { worked: boolean; procedures: Array<{ type: string; notes: string; date: string }> }>()
    
    dentalRecords
      .filter(record => record.isAdultTeeth)
      .forEach((record) => {
        record.toothNumbers.forEach((toothNum) => {
          const toothId = String(toothNum)
          if (!teethMap.has(toothId)) {
            teethMap.set(toothId, {
              worked: true,
              procedures: []
            })
          }
          const tooth = teethMap.get(toothId)!
          tooth.procedures.push({
            type: record.procedureType,
            notes: record.notes && record.notes.length > 0 ? record.notes.join("; ") : "",
            date: record.interventionDate
          })
        })
      })
    
    return Array.from(teethMap.entries()).map(([id, data]) => ({
      id,
      ...data
    }))
  }, [dentalRecords])

  const childWorkedTeeth = useMemo(() => {
    const teethMap = new Map<string, { worked: boolean; procedures: Array<{ type: string; notes: string; date: string }> }>()
    
    dentalRecords
      .filter(record => !record.isAdultTeeth)
      .forEach((record) => {
        record.toothNumbers.forEach((toothNum) => {
          const toothId = String(toothNum)
          if (!teethMap.has(toothId)) {
            teethMap.set(toothId, {
              worked: true,
              procedures: []
            })
          }
          const tooth = teethMap.get(toothId)!
          tooth.procedures.push({
            type: record.procedureType,
            notes: record.notes && record.notes.length > 0 ? record.notes.join("; ") : "",
            date: record.interventionDate
          })
        })
      })
    
    return Array.from(teethMap.entries()).map(([id, data]) => ({
      id,
      ...data
    }))
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
          <DialogTitle className="text-2xl">Patient Summary</DialogTitle>
        </DialogHeader>

        <div className="p-6 space-y-6 overflow-x-hidden">
          {/* Patient Basic Info */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <User className="h-5 w-5" />
                Patient Information
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Full Name</p>
                  <p className="text-base font-semibold">{patientName}</p>
                </div>
                
                {age !== null && (
                  <div>
                    <p className="text-sm font-medium text-muted-foreground mb-1">Age</p>
                    <p className="text-base">{age} years old</p>
                  </div>
                )}

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Gender</p>
                  <p className="text-base">{patient.gender || "Not specified"}</p>
                </div>

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1">Date of Birth</p>
                  <p className="text-base">{formatDate(patient.dateOfBirth)}</p>
                </div>
              </div>

              <Separator className="my-4" />

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                    <Phone className="h-4 w-4" />
                    Phone Number
                  </p>
                  <p className="text-base">{patient.phoneNumber || "Not provided"}</p>
                </div>

                <div>
                  <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                    <Mail className="h-4 w-4" />
                    Email
                  </p>
                  <p className="text-base">{patient.email || "Not provided"}</p>
                </div>

                {patient.address && (
                  <div className="md:col-span-2">
                    <p className="text-sm font-medium text-muted-foreground mb-1 flex items-center gap-2">
                      <MapPin className="h-4 w-4" />
                      Address
                    </p>
                    <p className="text-base">
                      {[
                        patient.address.street,
                        patient.address.city,
                        patient.address.state,
                        patient.address.zipCode
                      ].filter(Boolean).join(", ") || "Not provided"}
                    </p>
                  </div>
                )}

                {patient.emergencyContactName && (
                  <div>
                    <p className="text-sm font-medium text-muted-foreground mb-1">Emergency Contact</p>
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
                      Insurance
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
                Dental Chart - All Worked Teeth
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-6">
              {/* Adult Teeth Chart */}
              {adultWorkedTeeth.length > 0 && (
                <div>
                  <h3 className="text-sm font-medium mb-3">Adult Teeth</h3>
                  <DentalChart 
                    initialData={adultWorkedTeeth}
                    onTeethChange={() => {}} // Read-only view
                    readOnly={true}
                    defaultIsAdult={true}
                  />
                </div>
              )}
              
              {/* Child Teeth Chart */}
              {childWorkedTeeth.length > 0 && (
                <div>
                  <h3 className="text-sm font-medium mb-3">Child Teeth</h3>
                  <DentalChart 
                    initialData={childWorkedTeeth}
                    onTeethChange={() => {}} // Read-only view
                    readOnly={true}
                    defaultIsAdult={false}
                  />
                </div>
              )}

              {adultWorkedTeeth.length === 0 && childWorkedTeeth.length === 0 && (
                <p className="text-center text-muted-foreground py-8">No teeth have been worked on yet</p>
              )}
            </CardContent>
          </Card>

          {/* Medical Records Table */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileText className="h-5 w-5" />
                Dental Records
              </CardTitle>
            </CardHeader>
            <CardContent>
              {dentalRecords.length === 0 ? (
                <p className="text-center text-muted-foreground py-8">No dental records found</p>
              ) : (
                <div className="w-full">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="min-w-[100px]">Date</TableHead>
                        <TableHead className="min-w-[120px]">Procedure Type</TableHead>
                        <TableHead className="min-w-[90px]">Teeth Type</TableHead>
                        <TableHead className="min-w-[120px]">Teeth</TableHead>
                        <TableHead className="min-w-[80px]">Cost</TableHead>
                        <TableHead className="min-w-[100px]">Amount Paid</TableHead>
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
                            <Badge variant="outline" className="whitespace-nowrap">
                              {record.isAdultTeeth ? "Adult" : "Child"}
                            </Badge>
                          </TableCell>
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
                          <TableCell className="whitespace-nowrap">${record.cost.toFixed(2)}</TableCell>
                          <TableCell className="whitespace-nowrap">${record.amountPaid.toFixed(2)}</TableCell>
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
                                            Important Notes:
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
                                              Notes:
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
                                        Collapse
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
                                            {record.importantNotes.length} important
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
                                        View notes
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

