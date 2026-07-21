"use client"

import { useState, useEffect } from "react"
import { DentalChart } from "./dental-chart"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog"
import { cn } from "@/lib/utils"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"
import type { ProcedureTypeDto, DentalRecordDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"

type ToothStatus = {
  id: string
  worked: boolean
  procedures: Array<{
    type: string
    notes: string
    date: string
  }>
}

interface PatientRecordModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patientName?: string
  patientId?: string
  record?: DentalRecordDto | null // Record to edit, null for new record
  onSuccess?: () => void
}

export function PatientRecordModal({
  open,
  onOpenChange,
  patientName: initialPatientName = "",
  patientId,
  record,
  onSuccess,
}: PatientRecordModalProps) {
  const [patientName, setPatientName] = useState(initialPatientName)
  const [selectedTeeth, setSelectedTeeth] = useState<ToothStatus[]>([])
  const [isAdultTeeth, setIsAdultTeeth] = useState(true)
  const [interventionDate, setInterventionDate] = useState(new Date().toISOString().split("T")[0])
  const [procedureType, setProcedureType] = useState("")
  const [customProcedure, setCustomProcedure] = useState("")
  const [cost, setCost] = useState("")
  const [amountPaid, setAmountPaid] = useState("")
  const [notes, setNotes] = useState<string[]>([])
  const [importantNotes, setImportantNotes] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [loadingProcedureTypes, setLoadingProcedureTypes] = useState(false)

  // Load procedure types when modal opens
  useEffect(() => {
    if (open) {
      loadProcedureTypes()
    }
  }, [open])

  const loadProcedureTypes = async () => {
    try {
      setLoadingProcedureTypes(true)
      const data = await procedureTypesApi.list(false) // Only active procedure types
      setProcedureTypes(data || [])
    } catch (err) {
      console.error("Failed to load procedure types:", err)
      setProcedureTypes([]) // Ensure it's always an array
    } finally {
      setLoadingProcedureTypes(false)
    }
  }

  // Reset form when modal opens/closes, or load record data if editing
  useEffect(() => {
    if (open) {
      setPatientName(initialPatientName)
      
      if (record) {
        // Edit mode: load record data
        setInterventionDate(new Date(record.interventionDate).toISOString().split("T")[0])
        
        // Check if procedure type exists in the list, if not, treat as custom
        const procedureExists = procedureTypes.some(p => p.name === record.procedureType)
        if (procedureExists) {
          setProcedureType(record.procedureType)
          setCustomProcedure("")
        } else {
          setProcedureType("Custom")
          setCustomProcedure(record.procedureType)
        }
        
        setCost(record.cost.toString())
        setAmountPaid(record.amountPaid.toString())
        setIsAdultTeeth(record.isAdultTeeth)
        setNotes([...record.notes])
        setImportantNotes([...record.importantNotes])
        
        // Set selected teeth based on toothNumbers
        const teethStatus: ToothStatus[] = record.toothNumbers.map(toothNum => ({
          id: toothNum.toString(),
          worked: true,
          procedures: [{
            type: record.procedureType,
            notes: [...record.notes, ...record.importantNotes].filter(Boolean).join("; "),
            date: record.interventionDate
          }]
        }))
        setSelectedTeeth(teethStatus)
      } else {
        // Create mode: reset form
        setSelectedTeeth([])
        setIsAdultTeeth(true)
        setInterventionDate(new Date().toISOString().split("T")[0])
        setProcedureType("")
        setCustomProcedure("")
        setCost("")
        setAmountPaid("")
        setNotes([])
        setImportantNotes([])
      }
    }
  }, [open, initialPatientName, record, procedureTypes])

  // Auto-fill cost when procedure type is selected (using useEffect to ensure latest state)
  useEffect(() => {
    // Only proceed if we have a procedure type selected, it's not Custom, and procedure types are loaded
    if (!procedureType || procedureType === "Custom" || procedureTypes.length === 0 || loadingProcedureTypes) {
      return
    }

    const selectedProcedure = procedureTypes.find(p => p.name === procedureType)
    if (selectedProcedure && selectedProcedure.defaultCost != null && selectedProcedure.defaultCost > 0) {
      const costValue = String(selectedProcedure.defaultCost)
      // Only set if cost is currently empty or was previously set by a procedure type
      // This allows user to manually override if needed
      setCost(costValue)
    }
  }, [procedureType, procedureTypes, loadingProcedureTypes])

  // Prefill amount paid to the full cost only when it hasn't been entered yet — a partial advance
  // (amountPaid < cost) must not be silently overwritten to the full cost on save (AC-3).
  useEffect(() => {
    if (cost && !amountPaid) {
      setAmountPaid(cost)
    }
  }, [cost])

  const handleSave = async () => {
    if (!patientId) {
      toast.error("ID patient requis", {
        description: "L'identifiant du patient est nécessaire pour sauvegarder",
        duration: 3000,
      })
      return
    }

    if (!procedureType) {
      toast.error("Type de procédure requis", {
        description: "Veuillez sélectionner un type de procédure",
        duration: 3000,
      })
      return
    }

    if (procedureType === "Custom" && !customProcedure.trim()) {
      toast.error("Nom de procédure requis", {
        description: "Veuillez entrer un nom pour la procédure personnalisée",
        duration: 3000,
      })
      return
    }

    setLoading(true)

    try {
      // Get tooth numbers from selected teeth, or use empty array if none selected
      const toothNumbers = selectedTeeth.length > 0 
        ? selectedTeeth.map((t) => parseInt(t.id))
        : []
      
      // Determine isAdultTeeth based on selected teeth, or default to true if no teeth selected
      const finalIsAdultTeeth = toothNumbers.length > 0 
        ? isAdultTeeth 
        : true // Default to adult if no teeth selected
      
      const recordData = {
        interventionDate,
        procedureType: procedureType === "Custom" ? customProcedure.trim() : procedureType,
        cost: Number.parseFloat(cost) || 0,
        amountPaid: Number.parseFloat(amountPaid) || 0,
        isAdultTeeth: finalIsAdultTeeth,
        toothNumbers,
        notes: notes.filter(n => n.trim()).map(n => n.trim()),
        importantNotes: importantNotes.filter(n => n.trim()).map(n => n.trim()),
      }

      if (record) {
        // Update existing record
        await dentalRecordsApi.update(patientId, record.id, recordData)
        toast.success("Fiche dentaire mise à jour", {
          description: "Les modifications ont été enregistrées avec succès",
          duration: 3000,
        })
      } else {
        // Create new record
        await dentalRecordsApi.create(patientId, recordData)
        toast.success("Fiche dentaire sauvegardée", {
          description: "La nouvelle fiche dentaire a été créée avec succès",
          duration: 3000,
        })
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      console.error("Failed to save dental record:", err)
      const errorMessage = err instanceof ApiError ? err.message : "Une erreur s'est produite"
      toast.error("Erreur lors de la sauvegarde", {
        description: errorMessage,
        duration: 4000,
      })
    } finally {
      setLoading(false)
    }
  }

  const isCustomProcedure = procedureType === "Custom"

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto p-0 gap-0">
        <DialogHeader className="p-6 pb-4">
          <DialogTitle className="text-2xl">
            {record ? "Edit Medical Record" : "Add Medical Record"}
          </DialogTitle>
        </DialogHeader>

        <div className="overflow-y-auto max-h-[calc(90vh-180px)] px-6 pb-6 space-y-6">
          <div className="space-y-2">
            <Label htmlFor="patient-name" className="text-sm font-semibold">
              Patient Name
            </Label>
            <Input
              id="patient-name"
              value={patientName}
              onChange={(e) => setPatientName(e.target.value)}
              className="text-base font-medium"
              placeholder="Enter patient name"
              readOnly
            />
          </div>

          <div className="space-y-3">
            <h3 className="text-sm font-semibold">Dental Chart (Optional)</h3>
            <p className="text-xs text-muted-foreground">Select teeth if this procedure involved specific teeth. Leave empty if no teeth were involved.</p>
            <DentalChart 
              onTeethChange={setSelectedTeeth}
              initialData={selectedTeeth}
              onTeethTypeChange={setIsAdultTeeth}
            />
          </div>

          <div className="space-y-4 border-t pt-4">
            <h3 className="text-sm font-semibold">Intervention Details</h3>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="date" className="text-sm font-medium">
                  Date
                </Label>
                <Input
                  id="date"
                  type="date"
                  value={interventionDate}
                  onChange={(e) => setInterventionDate(e.target.value)}
                  className="text-sm"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="procedure" className="text-sm font-medium">
                  Procedure Type
                </Label>
                <Select 
                  value={procedureType} 
                  onValueChange={(value) => {
                    setProcedureType(value)
                    // Immediately try to set cost if procedure types are already loaded
                    if (value && value !== "Custom" && procedureTypes.length > 0 && !loadingProcedureTypes) {
                      const selectedProcedure = procedureTypes.find(p => p.name === value)
                      if (selectedProcedure?.defaultCost != null && selectedProcedure.defaultCost > 0) {
                        setCost(String(selectedProcedure.defaultCost))
                      }
                    }
                  }} 
                  disabled={loadingProcedureTypes}
                >
                  <SelectTrigger id="procedure" className="text-sm">
                    <SelectValue placeholder={loadingProcedureTypes ? "Loading..." : "Select procedure"} />
                  </SelectTrigger>
                  <SelectContent>
                    {procedureTypes.length === 0 && !loadingProcedureTypes ? (
                      <div className="px-2 py-1.5 text-sm text-muted-foreground">No procedure types available</div>
                    ) : (
                      <>
                        {procedureTypes.map((type) => (
                          <SelectItem key={type.id} value={type.name}>
                            <div className="flex items-center gap-2">
                              <div
                                className="h-3 w-3 rounded-full"
                                style={{ backgroundColor: type.colorHex }}
                              />
                              {type.name}
                            </div>
                          </SelectItem>
                        ))}
                        <SelectItem value="Custom">Custom</SelectItem>
                      </>
                    )}
                  </SelectContent>
                </Select>
              </div>
            </div>

            {isCustomProcedure && (
              <div className="space-y-2">
                <Label htmlFor="custom-procedure" className="text-sm font-medium">
                  Custom Procedure
                </Label>
                <Input
                  id="custom-procedure"
                  value={customProcedure}
                  onChange={(e) => setCustomProcedure(e.target.value)}
                  placeholder="Enter custom procedure"
                  className="text-sm"
                />
              </div>
            )}

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="cost" className="text-sm font-medium">
                  Total Cost
                </Label>
                <div className="relative">
                  <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">$</span>
                  <Input
                    id="cost"
                    type="number"
                    value={cost}
                    onChange={(e) => setCost(e.target.value)}
                    placeholder="0.00"
                    className="text-sm pl-7"
                    step="0.01"
                    min="0"
                  />
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="paid" className="text-sm font-medium">
                  Amount Paid
                </Label>
                <div className="relative">
                  <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">$</span>
                  <Input
                    id="paid"
                    type="number"
                    value={amountPaid}
                    onChange={(e) => setAmountPaid(e.target.value)}
                    placeholder="0.00"
                    className="text-sm pl-7"
                    step="0.01"
                    min="0"
                  />
                </div>
                {(() => {
                  const reste = Math.max(0, (Number.parseFloat(cost) || 0) - (Number.parseFloat(amountPaid) || 0))
                  return (
                    <p className="text-xs text-muted-foreground">
                      Reste à payer&nbsp;:{" "}
                      <span className={reste > 0 ? "font-semibold text-amber-600" : "font-medium text-foreground"}>
                        {formatDT(reste)}
                      </span>
                    </p>
                  )
                })()}
              </div>
            </div>

            {/* Notes Section */}
            <div className="space-y-3">
              <Label className="text-sm font-medium">
                Notes <span className="text-muted-foreground font-normal">(Optional)</span>
              </Label>
              <div className="space-y-2">
                {notes.map((note, index) => (
                  <div key={index} className="flex gap-2">
                    <Textarea
                      value={note}
                      onChange={(e) => {
                        const newNotes = [...notes]
                        newNotes[index] = e.target.value
                        setNotes(newNotes)
                      }}
                      placeholder="Enter a note..."
                      className="text-sm min-h-[80px] resize-y"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => {
                        const newNotes = notes.filter((_, i) => i !== index)
                        setNotes(newNotes)
                      }}
                      className="shrink-0"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M3 6h18" />
                        <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" />
                        <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" />
                      </svg>
                    </Button>
                  </div>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setNotes([...notes, ""])}
                  className="w-full"
                >
                  + Add Note
                </Button>
              </div>
            </div>

            {/* Important Notes Section */}
            <div className="space-y-3">
              <Label className="text-sm font-medium">
                Important Notes <span className="text-muted-foreground font-normal">(Optional)</span>
                <span className="ml-2 text-xs text-amber-600 dark:text-amber-500">⚠ Highlighted for doctors</span>
              </Label>
              <div className="space-y-2">
                {importantNotes.map((note, index) => (
                  <div key={index} className="flex gap-2">
                    <Textarea
                      value={note}
                      onChange={(e) => {
                        const newNotes = [...importantNotes]
                        newNotes[index] = e.target.value
                        setImportantNotes(newNotes)
                      }}
                      placeholder="Enter an important note (will be highlighted)..."
                      className="text-sm min-h-[80px] resize-y border-amber-300 dark:border-amber-700 bg-amber-50/50 dark:bg-amber-950/20"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => {
                        const newNotes = importantNotes.filter((_, i) => i !== index)
                        setImportantNotes(newNotes)
                      }}
                      className="shrink-0"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M3 6h18" />
                        <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" />
                        <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" />
                      </svg>
                    </Button>
                  </div>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setImportantNotes([...importantNotes, ""])}
                  className="w-full border-amber-300 dark:border-amber-700"
                >
                  + Add Important Note
                </Button>
              </div>
            </div>
          </div>
        </div>

        <DialogFooter className="p-6 pt-4 border-t">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
            Cancel
          </Button>
          <Button onClick={handleSave} className="bg-blue-600 hover:bg-blue-700 min-w-[140px]" disabled={loading}>
            {loading ? (record ? "Updating..." : "Saving...") : (record ? "Update Record" : "Save Record")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

