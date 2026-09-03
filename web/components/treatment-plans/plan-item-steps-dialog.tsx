"use client"

import { useEffect, useMemo, useState } from "react"
import { toast } from "sonner"
import { Check, ChevronDown, ChevronUp, GripVertical, Plus, Trash2, Unlink } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { treatmentPlansApi, type TreatmentPlanItemStepInput } from "@/lib/api/treatment-plans"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDateFr, quoteFr } from "@/lib/format"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"

/** One row of the editor. `id` present = an existing step whose identity must survive the save. */
interface StepRow {
  key: string
  id: string | null
  label: string
  duration: string
  doneDate: string | null
}

/**
 * « Modifier les étapes » — the protocol of one devis act, as an ordered list.
 *
 * <p>Its own dialog rather than a section of the devis editor, for `procedure-type-materials-dialog`'s reason:
 * the endpoint has <b>replace</b> semantics (an empty list means « cet acte se fait en une séance », a real
 * answer) while every field of the amend form is null-means-unchanged, and folding replace-semantics into a
 * patch form is how a list gets silently wiped.</p>
 *
 * <p>⚠️ <b>It states that no money moves</b>, because that is the question a dentist will have before touching
 * a devis a patient has signed: the act's price, the total and the échéancier are untouched, the revision does
 * not bump, and the server therefore allows this even on a plan already facturé.</p>
 *
 * <p>⚠️ <b>A step already carried out is read-only here, and says why.</b> It carries the link to the fiche that
 * evidences it, so removing it would discard the only route back to that record — « Détacher » on the row is
 * the correction path. Reordering it is refused for the same reason the rank is dense: every reader treats the
 * order as positional.</p>
 */
export function PlanItemStepsDialog({
  plan,
  item,
  open,
  onOpenChange,
  onSaved,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}) {
  const [rows, setRows] = useState<StepRow[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /*
   * ⚠️ Confirm-before-discard, and it was missing. Escape — and the ✕, and a tap on the overlay, which on a
   * phone is most of the screen — threw away a retyped protocol silently: verified at 390 px by renaming
   * « Scellement » to « Scellement définitif », pressing Escape, and watching the sheet close with no
   * question asked. § 5 requires the confirmation on *every* channel, and nine dialogs in this app already
   * route through this hook; this one did not, which is the shape this repository keeps producing.
   */
  const guard = useDirtyGuard(open, onOpenChange)

  /**
   * Which done step the dentist is about to detach from its fiche.
   *
   * ⚠️ This control was missing, and its absence made the paragraph at the bottom of this dialog **false**.
   * `markStepUndone` had **zero callers** — the command, the endpoint and the API client all shipped and
   * nothing reached them — while the row's own « Détacher » is offered only once the *whole* act is Done. So a
   * step marked réalisée by the wrong fiche on a half-finished bridge could not be corrected anywhere, and the
   * text here sent the dentist to a button that is not on screen. Same shape as the catalogue protocol nobody
   * applied: written, documented, wired to nobody.
   */
  const [detaching, setDetaching] = useState<StepRow | null>(null)
  const [detachBusy, setDetachBusy] = useState(false)

  const handleDetach = async () => {
    if (!item || !detaching?.id) return
    setDetachBusy(true)
    try {
      await treatmentPlansApi.markStepUndone(plan.id, item.id, detaching.id)
      toast.success(`${detaching.label} : la fiche a été détachée.`)
      setDetaching(null)
      onSaved()
      // The dialog re-seeds from the refreshed act, so the step comes back editable in place — no close.
    } catch (err) {
      showErrorToast(err)
    } finally {
      setDetachBusy(false)
    }
  }

  // Re-seed from the act each time the dialog opens, never on every render: the rows are edited in place and a
  // dependency on `item` alone would discard typing whenever the parent refetched.
  useEffect(() => {
    if (!open || !item) return
    setError(null)
    setRows(
      (item.steps ?? []).map((step) => ({
        key: step.id,
        id: step.id,
        label: step.label,
        duration: step.estimatedDurationMinutes?.toString() ?? "",
        doneDate: step.doneDate,
      })),
    )
  }, [open, item])

  const doneCount = useMemo(() => rows.filter((r) => r.doneDate).length, [rows])

  const update = (key: string, patch: Partial<StepRow>) =>
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const remove = (key: string) => setRows((prev) => prev.filter((r) => r.key !== key))

  const add = () =>
    setRows((prev) => [
      ...prev,
      { key: `new-${Date.now()}-${prev.length}`, id: null, label: "", duration: "", doneDate: null },
    ])

  /**
   * Move a row one place. A carried-out step cannot move and nothing can move above one — the ranks are dense
   * and positional, so re-ordering around a step whose fiche is already written would renumber history.
   */
  const move = (index: number, delta: number) => {
    const target = index + delta
    setRows((prev) => {
      if (target < 0 || target >= prev.length) return prev
      if (prev[index].doneDate || prev[target].doneDate) return prev
      const next = [...prev]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  const handleSave = async () => {
    if (!item) return

    const trimmed = rows.map((r) => ({ ...r, label: r.label.trim() }))
    if (trimmed.some((r) => r.label.length === 0)) {
      setError("Chaque étape doit porter un libellé.")
      return
    }
    const badDuration = trimmed.find(
      (r) => r.duration.trim() !== "" && !/^\d{1,3}$/.test(r.duration.trim()),
    )
    if (badDuration) {
      setError(`La durée de ${quoteFr(badDuration.label)} doit être un nombre de minutes.`)
      return
    }

    const payload: TreatmentPlanItemStepInput[] = trimmed.map((r) => ({
      id: r.id,
      label: r.label,
      estimatedDurationMinutes: r.duration.trim() === "" ? null : Number(r.duration.trim()),
    }))

    setSaving(true)
    setError(null)
    try {
      await treatmentPlansApi.setItemSteps(plan.id, item.id, payload, plan.version)
      toast.success(
        payload.length === 0
          ? "Étapes retirées — cet acte se fait en une séance."
          : "Étapes enregistrées",
      )
      onSaved()
      // Before the close, or the guard would ask whether to discard the edit it just persisted.
      guard.markClean()
      onOpenChange(false)
    } catch (err) {
      // The dialog stays open with every field as typed (§ 13).
      showErrorToast(err)
      setError(err instanceof Error ? err.message : "L'enregistrement a échoué.")
    } finally {
      setSaving(false)
    }
  }

  if (!item) return null

  return (
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Étapes de l&apos;acte</DialogTitle>
          <DialogDescription>
            {item.designationFr}
            {plan.number ? ` · devis ${plan.number}` : ""}
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-2 px-1 py-1">
          {error && <FormErrorBanner message={error} className="mb-2" />}

          {rows.length === 0 && (
            <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">
              Aucune étape — cet acte se fait en une séance.
            </p>
          )}

          {rows.map((row, index) => {
            const done = row.doneDate != null
            return (
              <div
                key={row.key}
                className={cn(
                  "flex flex-wrap items-center gap-2 rounded-md border p-2 sm:flex-nowrap",
                  done && "bg-muted",
                )}
              >
                <div className="flex shrink-0 items-center">
                  {done ? (
                    <span className="flex size-9 items-center justify-center text-muted-foreground/40">
                      <GripVertical className="h-4 w-4" aria-hidden="true" />
                    </span>
                  ) : (
                    <div className="flex flex-col">
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-5 w-8"
                        aria-label={`Monter ${quoteFr(row.label || "cette étape")}`}
                        disabled={saving || index === 0 || rows[index - 1]?.doneDate != null}
                        onClick={() => move(index, -1)}
                      >
                        <ChevronUp className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-5 w-8"
                        aria-label={`Descendre ${quoteFr(row.label || "cette étape")}`}
                        disabled={saving || index === rows.length - 1}
                        onClick={() => move(index, 1)}
                      >
                        <ChevronDown className="h-4 w-4" />
                      </Button>
                    </div>
                  )}
                  <span className="w-4 shrink-0 text-center font-mono text-2xs text-muted-foreground">
                    {index + 1}
                  </span>
                </div>

                <div className="min-w-0 flex-1 basis-full sm:basis-0">
                  <Label htmlFor={`step-label-${row.key}`} className="sr-only">
                    Libellé de l&apos;étape {index + 1}
                  </Label>
                  {done ? (
                    <p className="truncate text-sm font-medium" title={row.label}>
                      {row.label}
                    </p>
                  ) : (
                    <Input
                      id={`step-label-${row.key}`}
                      value={row.label}
                      onChange={(e) => update(row.key, { label: e.target.value })}
                      disabled={saving}
                      placeholder="ex. : Empreinte"
                      className="md:text-sm"
                    />
                  )}
                </div>

                {done && (
                  <span className="flex shrink-0 items-center gap-1 rounded-md bg-success-wash px-2 py-0.5 text-2xs font-semibold text-success">
                    <Check className="h-3 w-3" aria-hidden="true" />
                    réalisée le {formatDateFr(row.doneDate!)}
                  </span>
                )}

                <div className="flex shrink-0 items-center gap-2">
                  <Label htmlFor={`step-dur-${row.key}`} className="sr-only">
                    Durée de l&apos;étape {index + 1}, en minutes
                  </Label>
                  {done ? (
                    <span className="w-20 text-end font-mono text-2xs text-muted-foreground">
                      {row.duration ? `${row.duration} min` : "—"}
                    </span>
                  ) : (
                    <div className="relative">
                      <Input
                        id={`step-dur-${row.key}`}
                        value={row.duration}
                        onChange={(e) => update(row.key, { duration: e.target.value })}
                        disabled={saving}
                        inputMode="numeric"
                        placeholder="30"
                        className="w-24 pe-9 text-end font-mono tabular-nums md:text-sm"
                      />
                      <span className="pointer-events-none absolute end-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                        min
                      </span>
                    </div>
                  )}
                  {/*
                    A done step is DETACHED, never deleted: the link to the fiche that attests it is the only
                    route back to that record, so the two verbs are different operations and get different
                    controls rather than one control that changes meaning.
                  */}
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-9 shrink-0 text-muted-foreground coarse:size-11"
                    aria-label={
                      done
                        ? `Détacher la fiche de soins de l'étape ${quoteFr(row.label)}`
                        : `Supprimer l'étape ${quoteFr(row.label || String(index + 1))}`
                    }
                    title={done ? "Détacher la fiche de soins de cette étape" : undefined}
                    disabled={saving || detachBusy}
                    onClick={() => (done ? setDetaching(row) : remove(row.key))}
                  >
                    {done ? <Unlink className="h-4 w-4" /> : <Trash2 className="h-4 w-4" />}
                  </Button>
                </div>
              </div>
            )
          })}

          <Button
            variant="outline"
            className="w-full border-dashed text-primary coarse:h-11"
            disabled={saving}
            onClick={add}
          >
            <Plus className="h-4 w-4" />
            Ajouter une étape
          </Button>

          <div className="space-y-2 border-t pt-3 text-xs text-muted-foreground">
            <p>
              <span className="font-semibold text-foreground">Rien ici ne touche à l&apos;argent.</span> Le prix
              de l&apos;acte, le total du devis et l&apos;échéancier sont inchangés, et le numéro de révision ne
              bouge pas.
            </p>
            {doneCount > 0 && (
              <p>
                {doneCount === 1 ? "Une étape est déjà réalisée" : `${doneCount} étapes sont déjà réalisées`} :
                elles portent le lien vers la fiche de soins qui les atteste, et ne peuvent donc pas être
                supprimées. Pour en corriger une, détachez sa fiche avec l&apos;icône au bout de sa ligne :
                l&apos;étape redevient « à faire » et reste modifiable.
              </p>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <Button variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? "Enregistrement…" : "Enregistrer les étapes"}
          </Button>
        </DialogFooter>
      </DialogContent>
      <DiscardChangesDialog guard={guard} />

      <AlertDialog open={detaching != null} onOpenChange={(o) => !o && setDetaching(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            {/* Names what it acts on, per § 13 — with three steps on screen « Êtes-vous sûr ? » cannot say which. */}
            <AlertDialogTitle>
              Détacher la fiche de {quoteFr(detaching?.label ?? "")} ?
            </AlertDialogTitle>
            <AlertDialogDescription>
              L&apos;étape redevient « à faire » et son lien vers la fiche de soins est retiré. La fiche
              elle-même n&apos;est pas supprimée, et aucun montant ne bouge.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={detachBusy}>Retour</AlertDialogCancel>
            <AlertDialogAction variant="destructive" disabled={detachBusy} onClick={handleDetach}>
              {detachBusy ? "Détachement…" : "Détacher la fiche"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Dialog>
  )
}
