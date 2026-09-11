"use client"

import { useEffect, useMemo, useState } from "react"
import { useRouter } from "next/navigation"
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
import { useConflict } from "@/lib/hooks/use-conflict"
import { treatmentPlansApi, type TreatmentPlanItemStepInput } from "@/lib/api/treatment-plans"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDateFr, quoteFr } from "@/lib/format"
import { cn } from "@/lib/utils"

/** One row of the editor. `id` present = an existing step whose identity must survive the save. */
interface StepRow {
  key: string
  id: string | null
  label: string
  duration: string
  /**
   * Calendar days that must elapse after the previous séance, as typed.
   *
   * ⚠️ **It was missing, and this editor therefore ERASED it on every save.** `setItemSteps` replaces the
   * whole list and the payload omitted the key, so renaming one step of an implant wiped the osseointegration
   * wait off all of them — silently, and the symptom is `RecallWorklistRules` reporting a correctly-progressing
   * implant as abandoned after a flat fortnight. A different quantity from `duration`: that one sizes the
   * appointment, this one decides when the appointment is *due*.
   */
  minDays: string
  doneDate: string | null
  /**
   * The fiche that evidences this séance. Carried on the row for one reason: « Détacher » clears it
   * server-side, so it has to be in hand *before* the call to offer « Ouvrir la fiche » afterwards.
   */
  linkedDentalRecordId: string | null
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
  const router = useRouter()
  const [rows, setRows] = useState<StepRow[]>([])
  const [saving, setSaving] = useState(false)
  /*
   * ⚠️ `useConflict`, not a plain `error` string — this dialog round-trips `plan.version` on **both** of its
   * writes (« Enregistrer » and « Détacher »), so a 409 is a state it can genuinely reach, and a bare message
   * is the poisoned-dialog shape this repo names outright: the version it holds never moves, so every later
   * press repeats the same refusal. The hook is what puts « Recharger » on the banner and escalates the
   * wording on a second consecutive conflict.
   */
  const conflict = useConflict()

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
    /*
     * ⚠️ Read BEFORE the call, because the call is what destroys it. Detaching clears the step's
     * `linkedDentalRecordId`, and with it the only pointer this screen has to the fiche — while re-pointing
     * that fiche at the right séance is the correction the dentist is in the middle of making. Without this,
     * the row's next offer is « Enregistrer la fiche » on the same appointment, which opens a SECOND fiche
     * for one visit; the detached one survives only in the patient's own records tab.
     */
    const recordId = detaching.linkedDentalRecordId
    setDetachBusy(true)
    conflict.clearMessage()
    try {
      await treatmentPlansApi.markStepUndone(plan.id, item.id, detaching.id, plan.version)
      toast.success(`${detaching.label} : la fiche a été détachée.`, {
        action: recordId
          ? {
              label: "Ouvrir la fiche",
              onClick: () =>
                router.push(`/patients/${plan.patientId}?editRecord=${encodeURIComponent(recordId)}`),
            }
          : undefined,
      })
      setDetaching(null)
      onSaved()
      // The dialog re-seeds from the refreshed act, so the step comes back editable in place — no close.
    } catch (err) {
      /*
       * Closed first, then reported in the banner: the refusal is a long sentence naming a remedy (« établissez
       * un avoir pour la totalité des 60,000 DT… »), and a toast behind an open alert dialog is the one place it
       * cannot be read. `capture` is also what turns a 409 into « Recharger » instead of a dead repeat.
       */
      setDetaching(null)
      conflict.capture(err, "Échec du détachement de la fiche.")
    } finally {
      setDetachBusy(false)
    }
  }

  // Re-seed from the act each time the dialog opens, never on every render: the rows are edited in place and a
  // dependency on `item` alone would discard typing whenever the parent refetched.
  useEffect(() => {
    if (!open || !item) return
    conflict.reset()
    setRows(
      (item.steps ?? []).map((step) => ({
        key: step.id,
        id: step.id,
        label: step.label,
        duration: step.estimatedDurationMinutes?.toString() ?? "",
        minDays: step.minDaysAfterPrevious?.toString() ?? "",
        doneDate: step.doneDate,
        linkedDentalRecordId: step.linkedDentalRecordId,
      })),
    )
    // `conflict.reset` is a stable useCallback; listing `conflict` itself would re-run this on every render
    // and discard what the dentist is typing — matching `edit-patient-dialog`'s own seeding effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, item])

  const doneCount = useMemo(() => rows.filter((r) => r.doneDate).length, [rows])

  const update = (key: string, patch: Partial<StepRow>) =>
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const remove = (key: string) => setRows((prev) => prev.filter((r) => r.key !== key))

  const add = () =>
    setRows((prev) => [
      ...prev,
      {
        key: `new-${Date.now()}-${prev.length}`, id: null, label: "", duration: "", minDays: "",
        doneDate: null, linkedDentalRecordId: null,
      },
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
      conflict.setError("Chaque séance doit porter un libellé.")
      return
    }
    const badDuration = trimmed.find(
      (r) => r.duration.trim() !== "" && !/^\d{1,3}$/.test(r.duration.trim()),
    )
    if (badDuration) {
      conflict.setError(`La durée de ${quoteFr(badDuration.label)} doit être un nombre de minutes.`)
      return
    }
    const badDelay = trimmed.find(
      (r) => r.minDays.trim() !== "" && !/^\d{1,4}$/.test(r.minDays.trim()),
    )
    if (badDelay) {
      conflict.setError(`Le délai avant ${quoteFr(badDelay.label)} doit être un nombre de jours.`)
      return
    }

    const payload: TreatmentPlanItemStepInput[] = trimmed.map((r) => ({
      id: r.id,
      label: r.label,
      estimatedDurationMinutes: r.duration.trim() === "" ? null : Number(r.duration.trim()),
      // ⚠️ Sent for every row, echoed ones included: the endpoint REPLACES the list, so an omitted key
      // here is not « unchanged » — it clears the wait on a step nobody touched.
      minDaysAfterPrevious: r.minDays.trim() === "" ? null : Number(r.minDays.trim()),
    }))

    setSaving(true)
    // `clearMessage`, not `setError(null)`: it keeps the consecutive-conflict count, which is what
    // makes the escalated wording reachable on a second 409 in a row.
    conflict.clearMessage()
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
      // The dialog stays open with every field as typed (§ 13), and the banner is the only report — a toast
      // beside it printed the same sentence twice and vanished after four seconds while the form sat there.
      conflict.capture(err, "L'enregistrement a échoué.")
    } finally {
      setSaving(false)
    }
  }

  if (!item) return null

  return (
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Séances de l&apos;acte</DialogTitle>
          <DialogDescription>
            {item.designationFr}
            {plan.number ? ` · devis ${plan.number}` : ""}
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-2 px-1 py-1">
          {/*
            ⚠️ The `action` is what `useConflict.isConflict` exists to drive. Without it the banner says
            « Rechargez puis réessayez » beside no control that reloads, and the version this dialog holds
            never moves — so every later press repeats the refusal. « Recharger » is `onSaved`, which is the
            parent's refetch: the rows re-seed from the refreshed act in place, without closing.
          */}
          <FormErrorBanner
            message={conflict.error}
            className="mb-2"
            action={
              conflict.isConflict
                ? { label: "Recharger", onClick: () => onSaved(), disabled: saving || detachBusy }
                : undefined
            }
          />

          {rows.length === 0 && (
            <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">
              Aucune séance définie — cet acte se fait en une visite.
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
                    /*
                      ⚠️ **`size-6 coarse:size-8`, and this pair used to be `h-5 w-8`: on a touch device the
                      « Monter » chevron moved the step DOWN.**

                      Every `Button` carries `.touch-target`'s 44 px overlay on a coarse pointer, and these two
                      are stacked with no gap — so the later sibling, painting last, covered most of its
                      neighbour's box. Measured pixel row by pixel row on step 3 of a 6-step implant: at 820 px
                      the up-chevron paints y = 410–430 while « Monter » owns only 398–416 and « Descendre »
                      owns 418–460, so **14 of the arrow's 20 painted pixels fired the opposite action**, and the
                      band that worked sat mostly *above* the arrow you can see. A real tap at the painted centre
                      moved the step down at 390 and at 820; at 1440, with no overlay emitted, the same tap moved
                      it up. Each corrective tap made it worse — and the step order is what the worklist reads as
                      « prochaine étape » and what the booking dialog pre-ticks, so a wrongly-ordered protocol
                      proposes the wrong séance from then on.

                      The correct version already existed one dialog over: `procedure-type-steps-dialog.tsx` grows
                      the boxes instead of overlaying them, and was tap-verified at 390, 820 and 1440. This is the
                      documented `.touch-target`-on-adjacent-siblings trap; growing is the only fix for a stack.

                      ⚠️ And `title` states WHY a disabled chevron is disabled. Both were `disabled` with
                      `title=null` on a 2-step act with step 1 done — no tooltip, no message, nothing saying that
                      a réalisé step cannot be moved.
                    */
                    <div className="flex flex-col">
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-6 coarse:size-8"
                        aria-label={`Monter ${quoteFr(row.label || "cette séance")}`}
                        disabled={saving || index === 0 || rows[index - 1]?.doneDate != null}
                        title={
                          rows[index - 1]?.doneDate != null
                            ? "La séance précédente est déjà réalisée : elle ne peut pas être déplacée."
                            : index === 0
                              ? "C'est déjà la première séance."
                              : undefined
                        }
                        onClick={() => move(index, -1)}
                      >
                        <ChevronUp className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-6 coarse:size-8"
                        aria-label={`Descendre ${quoteFr(row.label || "cette séance")}`}
                        disabled={saving || index === rows.length - 1}
                        title={index === rows.length - 1 ? "C'est déjà la dernière séance." : undefined}
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
                    Nom de la séance {index + 1}
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
                    Durée de la séance {index + 1}, en minutes
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
                  <Label htmlFor={`step-delay-${row.key}`} className="sr-only">
                    Délai minimum avant la séance {index + 1}, en jours
                  </Label>
                  {/*
                    The wait BEFORE this séance. Disabled on the first — it has no previous séance to wait
                    after — and disabled on a done one, whose date is already a fact.
                  */}
                  {done || index === 0 ? (
                    <span className="w-20 text-end font-mono text-2xs text-muted-foreground">
                      {index === 0 ? "1re séance" : row.minDays ? `+ ${row.minDays} j` : "—"}
                    </span>
                  ) : (
                    <div className="relative">
                      <Input
                        id={`step-delay-${row.key}`}
                        value={row.minDays}
                        onChange={(e) => update(row.key, { minDays: e.target.value })}
                        disabled={saving}
                        inputMode="numeric"
                        placeholder="0"
                        className="w-24 pe-14 text-end font-mono tabular-nums md:text-sm"
                      />
                      <span className="pointer-events-none absolute end-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                        j après
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
                        ? `Détacher la fiche de soins de la séance ${quoteFr(row.label)}`
                        : `Supprimer la séance ${quoteFr(row.label || String(index + 1))}`
                    }
                    title={done ? "Détacher la fiche de soins de cette séance" : undefined}
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
            Ajouter une séance
          </Button>

          <div className="space-y-2 border-t pt-3 text-xs text-muted-foreground">
            <p>
              <span className="font-semibold text-foreground">Rien ici ne touche à l&apos;argent.</span> Le prix
              de l&apos;acte, le total du devis et l&apos;échéancier sont inchangés, et le numéro de révision ne
              bouge pas.
            </p>
            {doneCount > 0 && (
              <p>
                {doneCount === 1 ? "Une séance est déjà réalisée" : `${doneCount} séances sont déjà réalisées`} :
                elles portent le lien vers la fiche de soins qui les atteste, et ne peuvent donc pas être
                supprimées. Pour en corriger une, détachez sa fiche avec l&apos;icône au bout de sa ligne :
                la séance redevient « à faire » et reste modifiable.
              </p>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <Button variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? "Enregistrement…" : "Enregistrer les séances"}
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
              La séance redevient « à faire » et son lien vers la fiche de soins est retiré. La fiche
              elle-même n&apos;est pas supprimée, et aucun montant ne bouge.{" "}
              {/*
                ⚠️ The one condition, stated before the press. The server refuses when a live note bills this
                séance, and the fiche→note link is not on the plan DTO — so unlike « Supprimer l'acte », whose
                blocker arrives pre-emptively, this one can only be foretold. What made it a dead end was not
                the refusal but its remedy: it said « annulez la facture ou émettez un avoir », the avoir did
                not lift it and the cancellation was refused on a paid note. Both halves are fixed server-side;
                this is so the refusal is expected rather than a surprise mid-correction.
              */}
              Si sa fiche est facturée sur une note d&apos;honoraires, il faudra d&apos;abord créditer cette
              note en totalité — le refus vous dira laquelle et combien.
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
