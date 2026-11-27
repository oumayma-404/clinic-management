"use client"

import { memo, useEffect, useRef, useState } from "react"
import { ChevronDownIcon, FolderOpen } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { PatientSearchList } from "@/components/patients/patient-search-list"
import { formatFileSize, quoteFr } from "@/lib/format"
import { CABINET_PREFIX, type AttachmentFolder, type PlanPatient } from "@/lib/legacy-import/plan"
import { cn } from "@/lib/utils"

const NONE = "__none"

/**
 * One folder of radios/photos and the patient it goes to. A sure match shows its patient and can be changed; a close
 * one lists its candidates and waits for a choice; an unknown one can be given a patient by hand — one of this batch's
 * PDFs, or anybody already in the cabinet.
 *
 * ⚠️ Memoised, and its « Patient de ce lot » list is mounted only while it is open: a Radix Select renders every
 * item even closed, so 300 folders × 300 patients was ~390 000 nodes, and the run's twice-a-second repaint took the
 * page's whole main thread — measured on 26/11/2026, one patient a minute, then nothing.
 */
export const FolderChoiceCard = memo(function FolderChoiceCard({
  folder,
  roots,
  labelOf,
  onChoose,
  disabled,
}: {
  folder: AttachmentFolder
  /** This batch's patients a folder may go to — stable per plan, so choosing one folder repaints that card only. */
  roots: PlanPatient[]
  /** How a destination reads: built once per plan, not searched for in every card. */
  labelOf: (key: string) => string
  /** `label` comes with a cabinet patient picked by hand, who may not be in the plan's list yet. */
  onChoose: (folderName: string, key: string | null, label?: string) => void
  disabled: boolean
}) {
  const [searching, setSearching] = useState(false)
  const [picking, setPicking] = useState(false)
  const pickButton = useRef<HTMLButtonElement>(null)
  const wasPicking = useRef(false)
  useEffect(() => {
    // The list closed: focus goes back to the control that opened it, which the list's own trigger replaced.
    if (wasPicking.current && !picking) pickButton.current?.focus()
    wasPicking.current = picking
  }, [picking])
  const id = `folder-${folder.name.replace(/[^\p{L}\p{N}]+/gu, "-")}`
  const refused = folder.files.filter((f) => f.refusal).length
  const candidates = folder.verdict.kind === "ask" ? folder.verdict.candidates : []
  const pending = folder.choice === undefined
  const offered = new Set([...candidates.map((c) => c.key), ...roots.map((p) => p.key)])

  /** A patient picked from the whole cabinet who is also one of this batch's PDFs goes to that PDF's row. */
  const pickCabinet = (patientId: string, name: string) => {
    const twin = roots.find((p) => p.existing?.id === patientId && !p.createAnyway)
    onChoose(folder.name, twin ? twin.key : `${CABINET_PREFIX}${patientId}`, name)
    setSearching(false)
  }
  const pickValue = folder.choice && offered.has(folder.choice) ? folder.choice : folder.choice === null ? NONE : ""
  const pickLabel = pickValue === NONE ? "Ne pas importer ce dossier" : pickValue ? labelOf(pickValue) : "Choisir…"

  return (
    <li className={cn("space-y-3 rounded-lg border bg-card p-3 sm:p-4", pending && "border-warning bg-warning-wash/40")}>
      <div className="flex min-w-0 items-start gap-2">
        <FolderOpen className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <div className="min-w-0">
          <p className="break-words font-medium">{folder.name}</p>
          <p className="text-sm text-muted-foreground">
            {folder.files.length} fichier{folder.files.length > 1 ? "s" : ""} · {formatFileSize(folder.bytes)}
            {refused > 0 && ` · ${refused} refusé${refused > 1 ? "s" : ""}`}
          </p>
          <p className={cn("text-sm", pending ? "text-warning-ink" : folder.choice ? "text-foreground" : "text-muted-foreground")}>
            {pending
              ? candidates.length > 1
                ? "Le nom ressemble à plusieurs patients : choisissez."
                : "Le nom ressemble à ce patient sans être identique : confirmez."
              : folder.choice
                ? `→ ${labelOf(folder.choice)}`
                : "Ne sera pas importé."}
          </p>
        </div>
      </div>

      {candidates.length > 0 && (
        <RadioGroup
          value={folder.choice === null ? NONE : candidates.some((c) => c.key === folder.choice) ? folder.choice! : ""}
          onValueChange={(v) => onChoose(folder.name, v === NONE ? null : v)}
          aria-label={`Patient du dossier ${quoteFr(folder.name)}`}
          disabled={disabled}
          className="gap-1"
        >
          {candidates.map((c) => (
            <div key={c.key} className="flex items-center gap-2">
              <RadioGroupItem id={`${id}-${c.key}`} value={c.key} />
              <Label htmlFor={`${id}-${c.key}`} className="min-w-0 py-1.5 font-normal coarse:py-3">
                <span className="break-words">{labelOf(c.key)}</span>
                <span className="ms-1 text-muted-foreground">({Math.round(c.score * 100)} % de ressemblance)</span>
              </Label>
            </div>
          ))}
          <div className="flex items-center gap-2">
            <RadioGroupItem id={`${id}-none`} value={NONE} />
            <Label htmlFor={`${id}-none`} className="py-1.5 font-normal coarse:py-3">
              Ne pas importer ce dossier
            </Label>
          </div>
        </RadioGroup>
      )}

      <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-end">
        {roots.length > 0 && (
          <div className="min-w-0 space-y-1.5 sm:w-80">
            <Label htmlFor={`${id}-pick`} className="text-sm text-muted-foreground">
              Patient de ce lot
            </Label>
            {picking ? (
              <Select
                defaultOpen
                value={pickValue}
                onValueChange={(v) => onChoose(folder.name, v === NONE ? null : v)}
                onOpenChange={(open) => !open && setPicking(false)}
                disabled={disabled}
              >
                <SelectTrigger id={`${id}-pick`} className="w-full">
                  <SelectValue placeholder="Choisir…" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>Ne pas importer ce dossier</SelectItem>
                  {roots.map((p) => (
                    <SelectItem key={p.key} value={p.key}>
                      {labelOf(p.key)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <Button
                ref={pickButton}
                id={`${id}-pick`}
                type="button"
                variant="outline"
                className="w-full justify-between gap-2 font-normal coarse:min-h-11"
                disabled={disabled}
                aria-haspopup="listbox"
                onClick={() => setPicking(true)}
              >
                <span className={cn("truncate", !pickValue && "text-muted-foreground")}>{pickLabel}</span>
                <ChevronDownIcon className="size-4 shrink-0 opacity-50" aria-hidden="true" />
              </Button>
            )}
          </div>
        )}
        <Button
          type="button"
          variant="outline"
          className="coarse:min-h-11"
          disabled={disabled}
          aria-expanded={searching}
          onClick={() => setSearching((s) => !s)}
        >
          {searching ? "Fermer la recherche" : "Patient déjà dans le cabinet…"}
        </Button>
        {roots.length === 0 && folder.choice !== null && (
          <Button type="button" variant="ghost" className="coarse:min-h-11" disabled={disabled} onClick={() => onChoose(folder.name, null)}>
            Ne pas importer ce dossier
          </Button>
        )}
      </div>

      {searching && (
        <PatientSearchList
          label={`Rechercher le patient du dossier ${quoteFr(folder.name)}`}
          initialTerm={folder.name.replace(/^\s*\d+\s*[-_.)]?\s*/, "")}
          autoFocus
          onPick={(p) => pickCabinet(p.patientId, p.patientName)}
        />
      )}
    </li>
  )
})
