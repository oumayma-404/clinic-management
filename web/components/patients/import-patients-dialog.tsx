"use client"

import { useMemo, useRef, useState } from "react"
import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  FileSpreadsheet,
  Loader2,
  Upload,
  UserPlus,
  Users,
  XCircle,
} from "lucide-react"
import { toast } from "sonner"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Checkbox } from "@/components/ui/checkbox"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Separator } from "@/components/ui/separator"
import {
  IMPORT_FIELD_UNMAPPED,
  patientImportApi,
  type PatientImportPreview,
  type PatientImportResult,
  type PatientImportRow,
} from "@/lib/api/patient-import"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"

/**
 * « Importer des patients » (L5) — CSV → column mapping → **dry-run preview** → commit.
 *
 * <p><b>What it is for.</b> The spec's motivating case: « a dentist arriving with 3 000 patients in a spreadsheet
 * types them in by hand — this alone stops most switchers ». Before this there was no import path of any kind in the
 * product.</p>
 *
 * <p><b>The three steps are one component and one `File`</b>, deliberately. Nothing is staged on the server between
 * the preview and the commit (no table with a lifetime nobody would prune), so the browser is what holds the file
 * across the steps — which means the file, the mapping and the per-row decisions have to live in one place or the
 * commit could send a different combination from the one that was previewed.</p>
 *
 * <p>⚠️ <b>Every mapping change re-runs the dry run on the server.</b> The client deliberately does not
 * re-derive outcomes locally: duplicate matching needs the clinic's whole patient list and the validation is the
 * server's own, so a local approximation would be a second answer to « what will this import do » — and the whole
 * value of a dry run is that the preview and the commit cannot disagree.</p>
 */

type Step = "choose" | "map" | "done"

const OUTCOME_STYLE: Record<
  string,
  { label: string; icon: typeof CheckCircle2; className: string }
> = {
  Ready: { label: "À créer", icon: CheckCircle2, className: "text-success" },
  Duplicate: { label: "Doublon", icon: Users, className: "text-warning-ink" },
  Invalid: { label: "Ignoré", icon: XCircle, className: "text-destructive" },
  Created: { label: "Créé", icon: CheckCircle2, className: "text-success" },
  Skipped: { label: "Non importé", icon: Users, className: "text-muted-foreground" },
  Failed: { label: "Échec", icon: XCircle, className: "text-destructive" },
}

interface ImportPatientsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Called once, after a commit that created at least one patient, so the list behind reloads. */
  onImported: () => void
}

export function ImportPatientsDialog({ open, onOpenChange, onImported }: ImportPatientsDialogProps) {
  const [step, setStep] = useState<Step>("choose")
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<PatientImportPreview | null>(null)
  const [mapping, setMapping] = useState<Record<string, number>>({})
  /** File lines the operator ticked despite a duplicate match. Empty = skip every duplicate (the default). */
  const [createAnyway, setCreateAnyway] = useState<Set<number>>(new Set())
  const [result, setResult] = useState<PatientImportResult | null>(null)
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const reset = () => {
    setStep("choose")
    setFile(null)
    setPreview(null)
    setMapping({})
    setCreateAnyway(new Set())
    setResult(null)
    setError(null)
    if (fileInputRef.current) fileInputRef.current.value = ""
  }

  const close = () => {
    onOpenChange(false)
    // Deferred so the closing animation does not play over a wiped body. A committed import is reported and then
    // forgotten — re-opening must not show the previous run's report as if it were this file's.
    setTimeout(reset, 200)
  }

  /** Uploads (or re-uploads with a changed mapping) and holds the returned dry run. */
  const runPreview = async (nextFile: File, nextMapping?: Record<string, number>) => {
    setWorking(true)
    setError(null)
    try {
      const data = await patientImportApi.preview(nextFile, nextMapping)
      setPreview(data)
      setMapping(data.mapping)
      setStep("map")
    } catch (err) {
      // Left on the current step with the file intact: the usual cause is a mapping the file cannot satisfy, and
      // throwing the user back to the file picker would make them start over to fix one column.
      setError(getErrorMessage(err, "La lecture du fichier a échoué."))
    } finally {
      setWorking(false)
    }
  }

  const handleFileChosen = (chosen: File | null) => {
    if (!chosen) return
    setFile(chosen)
    setCreateAnyway(new Set())
    // No mapping on the first pass — the server detects it from the headers, which for this product's own export
    // fills in every column.
    void runPreview(chosen)
  }

  const handleMappingChange = (field: string, columnIndex: number) => {
    if (!file) return
    const next = { ...mapping, [field]: columnIndex }
    setMapping(next)
    // Duplicate ticks are keyed on file lines whose meaning depends on the mapping (a different Nom column is a
    // different patient on that line), so a mapping change has to clear them rather than carry them over.
    setCreateAnyway(new Set())
    void runPreview(file, next)
  }

  const toggleCreateAnyway = (lineNumber: number) => {
    setCreateAnyway((current) => {
      const next = new Set(current)
      if (next.has(lineNumber)) next.delete(lineNumber)
      else next.add(lineNumber)
      return next
    })
  }

  const handleCommit = async () => {
    if (!file || !preview) return
    setWorking(true)
    setError(null)
    try {
      const data = await patientImportApi.commit(file, mapping, Array.from(createAnyway))
      setResult(data)
      setStep("done")
      if (data.createdCount > 0) {
        toast.success(
          data.createdCount === 1 ? "1 patient importé" : `${data.createdCount} patients importés`,
        )
        onImported()
      } else {
        // Not a failure, and not a success either — « 0 créés » with a reason is the honest report.
        toast.info("Aucun patient n'a été créé.")
      }
    } catch (err) {
      setError(getErrorMessage(err, "L'import a échoué."))
    } finally {
      setWorking(false)
    }
  }

  /** How many rows the commit will actually create, with the operator's duplicate decisions applied. */
  const willCreate = useMemo(() => {
    if (!preview) return 0
    return preview.rows.filter(
      (r) => r.outcome === "Ready" || (r.outcome === "Duplicate" && createAnyway.has(r.lineNumber)),
    ).length
  }, [preview, createAnyway])

  return (
    <Dialog open={open} onOpenChange={(next) => (next ? onOpenChange(true) : close())}>
      {/* A heavy, table-bearing surface: full-screen sheet below `md:`, and the width override is `md:`-prefixed
          so the phone keeps its gutter (§ 4). */}
      <DialogContent mobile="sheet" className="gap-0 p-0 md:max-h-[90dvh] md:max-w-4xl">
        <DialogHeader className="p-6 pb-4">
          <DialogTitle className="flex items-center gap-2 text-xl">
            <span
              aria-hidden="true"
              className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"
            >
              <FileSpreadsheet className="size-4" strokeWidth={1.75} />
            </span>
            Importer des patients
          </DialogTitle>
          <DialogDescription>
            {step === "choose" &&
              "Un fichier CSV, une ligne par patient. La première ligne doit contenir les noms des colonnes."}
            {step === "map" &&
              "Vérifiez la correspondance des colonnes, puis le détail ligne par ligne avant d'importer. Rien n'est encore enregistré."}
            {step === "done" && "Résultat de l'import, ligne par ligne."}
          </DialogDescription>
        </DialogHeader>

        <Separator />

        <DialogBody>
          <div className="space-y-6 p-6">
            <FormErrorBanner message={error} />

            {step === "choose" && (
              <ChooseStep
                inputRef={fileInputRef}
                working={working}
                onFileChosen={handleFileChosen}
              />
            )}

            {step === "map" && preview && (
              <MapStep
                preview={preview}
                mapping={mapping}
                working={working}
                createAnyway={createAnyway}
                onMappingChange={handleMappingChange}
                onToggleCreateAnyway={toggleCreateAnyway}
              />
            )}

            {step === "done" && result && <DoneStep result={result} />}
          </div>
        </DialogBody>

        <Separator />

        <DialogFooter className="p-6 pt-4">
          {step === "map" && (
            <Button
              type="button"
              variant="outline"
              onClick={reset}
              disabled={working}
              className="gap-2"
            >
              <ArrowLeft className="size-4" aria-hidden="true" />
              Choisir un autre fichier
            </Button>
          )}
          {step !== "done" && (
            <Button type="button" variant="outline" onClick={close} disabled={working}>
              Annuler
            </Button>
          )}
          {step === "map" && (
            <Button
              type="button"
              onClick={handleCommit}
              disabled={working || willCreate === 0}
              className="gap-2"
            >
              {working ? (
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />
              ) : (
                <UserPlus className="size-4" aria-hidden="true" />
              )}
              {/* The count is on the button because it is the irreversible part: patient records cannot be merged,
                  so « Importer » with no number is a click whose consequence is unstated. */}
              {working
                ? "Import en cours..."
                : willCreate === 1
                  ? "Importer 1 patient"
                  : `Importer ${willCreate} patients`}
            </Button>
          )}
          {step === "done" && (
            <Button type="button" onClick={close}>
              Fermer
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ChooseStep({
  inputRef,
  working,
  onFileChosen,
}: {
  inputRef: React.RefObject<HTMLInputElement | null>
  working: boolean
  onFileChosen: (file: File | null) => void
}) {
  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-dashed p-6 text-center">
        <Upload className="mx-auto size-8 text-muted-foreground" aria-hidden="true" />
        <p className="mt-3 text-sm text-muted-foreground">
          Choisissez le fichier CSV de vos patients.
        </p>
        {/* A real <input type="file"> behind a labelled button: the native picker is the only reliable path on
            iOS and Android, and it is keyboard-reachable for free. */}
        <Label htmlFor="patient-import-file" className="sr-only">
          Fichier CSV des patients
        </Label>
        <input
          ref={inputRef}
          id="patient-import-file"
          type="file"
          accept=".csv,text/csv"
          className="sr-only"
          onChange={(event) => onFileChosen(event.target.files?.[0] ?? null)}
        />
        <Button
          type="button"
          className="mt-4 gap-2"
          disabled={working}
          onClick={() => inputRef.current?.click()}
        >
          {working ? (
            <Loader2 className="size-4 animate-spin" aria-hidden="true" />
          ) : (
            <FileSpreadsheet className="size-4" aria-hidden="true" />
          )}
          {working ? "Lecture du fichier..." : "Choisir un fichier CSV"}
        </Button>
      </div>

      <div className="rounded-lg bg-muted/40 p-4 text-sm text-muted-foreground">
        <p className="font-medium text-foreground">Le plus simple</p>
        <p className="mt-1">
          Exportez vos patients depuis votre ancien logiciel en CSV, puis déposez le fichier ici. Les
          colonnes seront reconnues automatiquement quand leurs noms sont explicites (Nom, Prénom, Date de
          naissance, Téléphone…), et vous pourrez les corriger à l'étape suivante.
        </p>
        <p className="mt-2">
          Un fichier exporté depuis « Exporter » sur cette page se réimporte tel quel.
        </p>
      </div>
    </div>
  )
}

function MapStep({
  preview,
  mapping,
  working,
  createAnyway,
  onMappingChange,
  onToggleCreateAnyway,
}: {
  preview: PatientImportPreview
  mapping: Record<string, number>
  working: boolean
  createAnyway: Set<number>
  onMappingChange: (field: string, columnIndex: number) => void
  onToggleCreateAnyway: (lineNumber: number) => void
}) {
  return (
    <div className={cn("space-y-6", working && "pointer-events-none opacity-60")}>
      {/* What the reader decided about the file. Stated because a mis-detected delimiter is the single most likely
          reason an import looks broken, and « lu avec : virgule » is what makes it diagnosable. */}
      <p className="text-sm text-muted-foreground" role="status">
        {preview.rows.length} ligne{preview.rows.length === 1 ? "" : "s"} lue
        {preview.rows.length === 1 ? "" : "s"} · séparateur : {preview.delimiter} · encodage :{" "}
        {preview.encoding}
      </p>

      {preview.truncated && (
        <p
          role="status"
          className="flex items-start gap-2 rounded-lg bg-warning-wash p-3 text-sm text-warning-ink"
        >
          <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>
            Le fichier contient plus de lignes que ce qu'un import peut traiter en une fois. Seules les{" "}
            {preview.rows.length} premières sont prises en compte — importez le reste dans un second fichier.
          </span>
        </p>
      )}

      <section className="space-y-3">
        <h3 className="text-sm font-semibold">Correspondance des colonnes</h3>
        <div className="grid gap-3 sm:grid-cols-2">
          {preview.fields.map((field) => {
            const selected = mapping[field.field]
            const value = selected === undefined ? String(IMPORT_FIELD_UNMAPPED) : String(selected)
            const selectId = `import-field-${field.field}`
            return (
              <div key={field.field} className="space-y-1.5">
                <Label htmlFor={selectId} className="text-sm">
                  {field.label}
                  {field.required && (
                    <span className="ms-1 text-destructive" aria-label="obligatoire">
                      *
                    </span>
                  )}
                </Label>
                <Select
                  value={value}
                  onValueChange={(next) => onMappingChange(field.field, Number(next))}
                >
                  <SelectTrigger id={selectId} className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={String(IMPORT_FIELD_UNMAPPED)}>— Ne pas importer —</SelectItem>
                    {preview.headers.map((header, index) => (
                      <SelectItem key={`${header}-${index}`} value={String(index)}>
                        {header.trim() || `Colonne ${index + 1}`}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )
          })}
        </div>
      </section>

      <section className="space-y-3">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
          <h3 className="text-sm font-semibold">Aperçu</h3>
          <span className="text-success">{preview.readyCount} à créer</span>
          <span className="text-warning-ink">{preview.duplicateCount} doublon(s)</span>
          <span className="text-destructive">{preview.invalidCount} ignoré(s)</span>
        </div>

        {preview.duplicateCount > 0 && (
          <p className="rounded-lg bg-muted/40 p-3 text-sm text-muted-foreground">
            Un doublon n'est <strong>pas</strong> importé par défaut : deux fiches pour une même personne ne
            peuvent pas être fusionnées ensuite. Cochez « Créer quand même » sur une ligne s'il s'agit
            réellement d'un autre patient.
          </p>
        )}

        {/* A list, not a table: every row carries one identity, one status and up to three sentences of
            explanation, so a card is the shape at every width — and it needs no card-vs-table pair. */}
        <ul className="space-y-2" aria-label="Lignes du fichier">
          {preview.rows.map((row) => (
            <RowCard
              key={row.lineNumber}
              row={row}
              checkable={row.outcome === "Duplicate"}
              checked={createAnyway.has(row.lineNumber)}
              onToggle={() => onToggleCreateAnyway(row.lineNumber)}
            />
          ))}
        </ul>
      </section>
    </div>
  )
}

function DoneStep({ result }: { result: PatientImportResult }) {
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
        <span className="text-success">{result.createdCount} créé(s)</span>
        <span className="text-muted-foreground">{result.skippedCount} non importé(s)</span>
        <span className="text-destructive">{result.failedCount} échec(s)</span>
      </div>

      {/* The whole report, not only the failures: « 2 947 créés » is only believable beside the lines it did not
          create, and those lines are what the operator has to fix and re-import. */}
      <ul className="space-y-2" aria-label="Résultat par ligne">
        {result.rows.map((row) => (
          <RowCard key={row.lineNumber} row={row} checkable={false} checked={false} onToggle={() => {}} />
        ))}
      </ul>
    </div>
  )
}

function RowCard({
  row,
  checkable,
  checked,
  onToggle,
}: {
  row: PatientImportRow
  checkable: boolean
  checked: boolean
  onToggle: () => void
}) {
  const style = OUTCOME_STYLE[row.outcome] ?? OUTCOME_STYLE.Invalid
  const Icon = style.icon
  const checkboxId = `import-anyway-${row.lineNumber}`

  return (
    <li className="rounded-lg border p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
            <span className="font-mono text-2xs tabular-nums text-muted-foreground">
              L{row.lineNumber}
            </span>
            <span className="min-w-0 break-words">{row.displayName}</span>
          </p>
          {row.duplicateOf && (
            <p className="mt-1 text-sm text-muted-foreground">Déjà présent : {row.duplicateOf}</p>
          )}
          {row.errors.map((message) => (
            <p key={message} className="mt-1 text-sm text-destructive">
              {message}
            </p>
          ))}
          {row.warnings.map((message) => (
            <p key={message} className="mt-1 text-sm text-warning-ink">
              {message}
            </p>
          ))}
        </div>

        <div className="flex shrink-0 items-center gap-3">
          <span className={cn("flex items-center gap-1.5 text-sm", style.className)}>
            <Icon className="size-4" aria-hidden="true" />
            {style.label}
          </span>
          {checkable && (
            <div className="flex items-center gap-2">
              <Checkbox id={checkboxId} checked={checked} onCheckedChange={onToggle} />
              <Label htmlFor={checkboxId} className="cursor-pointer text-sm">
                Créer quand même
              </Label>
            </div>
          )}
        </div>
      </div>
    </li>
  )
}
