"use client"

import type { ReactNode } from "react"
import { AlertTriangle, CheckCircle2, CircleSlash, Loader2, XCircle } from "lucide-react"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { formatFileSize, quoteFr } from "@/lib/format"
import type { PlanFile, PlanPatient } from "@/lib/legacy-import/plan"
import { warningsOf } from "@/lib/legacy-import/plan"
import type { RowResult } from "@/lib/legacy-import/run"
import { cn } from "@/lib/utils"

type Editable = Pick<PlanPatient, "lastName" | "firstName" | "birth" | "phone" | "skip" | "createAnyway">

/**
 * One person of the plan: who will be created (or found), with what, and — once the import has run — what happened.
 * The identity stays editable until the import starts, because a typo here becomes a typo in the cabinet.
 */
export function PlanPatientCard({
  patient,
  merged,
  files,
  folders,
  historyCount,
  result,
  locked,
  onChange,
}: {
  patient: PlanPatient
  /** Other records of the same person in the export, imported into this one. */
  merged: PlanPatient[]
  files: PlanFile[]
  /** Names of the attachment folders chosen for this patient. */
  folders: string[]
  historyCount: number
  result: RowResult | undefined
  locked: boolean
  onChange: (patch: Partial<Editable>) => void
}) {
  const id = `p-${patient.key.replace(/[^\p{L}\p{N}]+/gu, "-")}`
  const ok = files.filter((f) => !f.refusal)
  const refused = files.filter((f) => f.refusal)
  const usesExisting = !!patient.existing && !patient.createAnyway
  const identityLocked = locked || usesExisting || patient.skip
  const warnings = warningsOf(patient)
  const notes = [...patient.remarks, ...merged.flatMap((m) => m.remarks.filter((r) => !r.startsWith("Même personne")))]

  return (
    <li className={cn("space-y-3 rounded-lg border bg-card p-3 sm:p-4", patient.skip && "opacity-60")}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="break-words text-sm text-muted-foreground">
            Dossier {quoteFr(patient.key)}
            {merged.length > 0 && <> + {merged.map((m) => quoteFr(m.key)).join(", ")}</>}
          </p>
          <p className="font-medium">
            {patient.skip
              ? "Ne sera pas importé"
              : usesExisting
                ? `Déjà dans le cabinet : ${patient.existing!.label}`
                : "Nouveau patient"}
          </p>
        </div>
        <ResultBadge result={result} />
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Field id={`${id}-nom`} label="Nom" value={patient.lastName} disabled={identityLocked} onChange={(v) => onChange({ lastName: v })} />
        <Field id={`${id}-prenom`} label="Prénom" value={patient.firstName} disabled={identityLocked} onChange={(v) => onChange({ firstName: v })} />
        <Field id={`${id}-naissance`} label="Date de naissance" type="date" value={patient.birth} disabled={identityLocked} onChange={(v) => onChange({ birth: v })} />
        <Field id={`${id}-tel`} label="Téléphone" inputMode="tel" value={patient.phone} disabled={identityLocked} onChange={(v) => onChange({ phone: v })} />
      </div>

      <p className="text-sm text-muted-foreground">
        {ok.length} fichier{ok.length > 1 ? "s" : ""} ({formatFileSize(ok.reduce((n, f) => n + f.file.size, 0))})
        {folders.length > 0 && <> · dossier{folders.length > 1 ? "s" : ""} {folders.map(quoteFr).join(", ")}</>}
        {historyCount > 0 && ` · ${historyCount} ligne${historyCount > 1 ? "s" : ""} d'historique`}
        {patient.opened && ` · créé le ${patient.opened.split("-").reverse().join("/")}`}
      </p>

      {(warnings.length > 0 || notes.length > 0 || refused.length > 0 || patient.existingAmbiguous) && (
        <ul className="space-y-1 text-sm">
          {patient.existingAmbiguous && !patient.createAnyway && (
            <Note>Plusieurs patients du cabinet portent ce nom : le logiciel demandera confirmation.</Note>
          )}
          {warnings.map((w) => (
            <Note key={w}>{w}</Note>
          ))}
          {notes.map((n) => (
            <Note key={n} quiet>
              {n}
            </Note>
          ))}
          {refused.map((f) => (
            <Note key={f.id}>
              {quoteFr(f.rel)} ne sera pas importé : {f.refusal}
            </Note>
          ))}
        </ul>
      )}

      {result?.duplicate && (
        <div role="status" className="space-y-2 rounded-md border border-warning bg-warning-wash p-3 text-sm">
          <p>{result.duplicate}</p>
          <p>S&apos;il s&apos;agit d&apos;une autre personne, cochez « C&apos;est une autre personne » puis relancez l&apos;import.</p>
        </div>
      )}

      {result && result.errors.length > 0 && (
        <ul role="status" className="space-y-1 text-sm text-destructive">
          {result.errors.map((e) => (
            <li key={e} className="break-words">
              {e}
            </li>
          ))}
        </ul>
      )}

      <div className="flex flex-col gap-1 sm:flex-row sm:flex-wrap sm:gap-x-6">
        <CheckRow id={`${id}-skip`} checked={patient.skip} disabled={locked} onChange={(v) => onChange({ skip: v })}>
          Ne pas importer ce patient
        </CheckRow>
        {(patient.existing || patient.existingAmbiguous || result?.duplicate) && (
          <CheckRow
            id={`${id}-other`}
            checked={patient.createAnyway}
            disabled={locked || patient.skip}
            onChange={(v) => onChange({ createAnyway: v })}
          >
            C&apos;est une autre personne : créer une nouvelle fiche
          </CheckRow>
        )}
      </div>
    </li>
  )
}

function Field({
  id,
  label,
  value,
  onChange,
  disabled,
  type = "text",
  inputMode,
}: {
  id: string
  label: string
  value: string
  onChange: (v: string) => void
  disabled: boolean
  type?: string
  inputMode?: "tel"
}) {
  return (
    <div className="min-w-0 space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} type={type} inputMode={inputMode} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

function CheckRow({
  id,
  checked,
  disabled,
  onChange,
  children,
}: {
  id: string
  checked: boolean
  disabled: boolean
  onChange: (v: boolean) => void
  children: ReactNode
}) {
  return (
    <div className="flex items-center gap-2">
      <Checkbox id={id} checked={checked} disabled={disabled} onCheckedChange={(v) => onChange(v === true)} />
      <Label htmlFor={id} className="py-1.5 font-normal coarse:py-3">
        {children}
      </Label>
    </div>
  )
}

function Note({ children, quiet = false }: { children: ReactNode; quiet?: boolean }) {
  return (
    <li className={cn("flex min-w-0 items-start gap-1.5", quiet ? "text-muted-foreground" : "text-warning-ink")}>
      {!quiet && <AlertTriangle className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />}
      <span className="min-w-0 break-words">{children}</span>
    </li>
  )
}

function ResultBadge({ result }: { result: RowResult | undefined }) {
  if (!result || result.state === "waiting") return null
  if (result.state === "running") {
    return (
      <span className="inline-flex items-center gap-1.5 text-sm text-muted-foreground" role="status">
        <Loader2 className="size-4 animate-spin" aria-hidden="true" />
        Import… {result.sent > 0 && `${result.sent} fichier${result.sent > 1 ? "s" : ""} envoyé${result.sent > 1 ? "s" : ""}`}
      </span>
    )
  }
  if (result.state === "skipped") {
    return (
      <span className="inline-flex items-center gap-1.5 text-sm text-muted-foreground">
        <CircleSlash className="size-4" aria-hidden="true" />
        Ignoré
      </span>
    )
  }
  const summary = [
    result.created ? "Patient créé" : result.patientId ? "Patient existant" : null,
    result.sent ? `${result.sent} fichier${result.sent > 1 ? "s" : ""} envoyé${result.sent > 1 ? "s" : ""}` : null,
    result.already ? `${result.already} déjà là` : null,
    result.waitingForSpace ? `${result.waitingForSpace} en attente d'espace` : null,
    result.historyAdded ? `${result.historyAdded} ligne${result.historyAdded > 1 ? "s" : ""} d'historique` : null,
  ].filter(Boolean)
  const failed = result.state === "failed"
  return (
    <span className={cn("inline-flex items-start gap-1.5 text-sm", failed ? "text-destructive" : "text-success")} role="status">
      {failed ? <XCircle className="mt-0.5 size-4 shrink-0" aria-hidden="true" /> : <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
      <span>{failed ? (summary.length ? `Incomplet · ${summary.join(" · ")}` : "Échec") : summary.join(" · ") || "Fait"}</span>
    </span>
  )
}
