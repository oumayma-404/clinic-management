"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { Check, ChevronDown, ChevronUp, Plus, Trash2, Undo2 } from "lucide-react"
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
import { quoteFr } from "@/lib/format"
import { cn } from "@/lib/utils"
import { planDevisLabel } from "./treatment-plan-labels"
import { seanceDay } from "./seance-strip"
import { Consequences } from "./plan-consequences"

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

/** A new séance row — the « + » on the strip and « Ajouter une séance » here. */
function emptyRow(position: number): StepRow {
  return {
    key: `new-${Date.now()}-${position}`, id: null, label: "", duration: "", minDays: "",
    doneDate: null, linkedDentalRecordId: null,
  }
}

/**
 * « Séances » — the protocol of one devis act, as an ordered list.
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
  focusStepId = null,
  addOnOpen = false,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: () => void
  /** « Modifier la séance » on the strip — that séance is scrolled to and outlined (T2). */
  focusStepId?: string | null
  /** The strip's « + » — the dialog opens with one new séance row at the end. */
  addOnOpen?: boolean
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
      await treatmentPlansApi.markStepUndone(plan.id, item.id, detaching.id, seededVersionRef.current)
      reseedRef.current = true
      toast.success(`${quoteFr(detaching.label)} remise à faire`, {
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

  /*
   * The version the rows were read at. ⚠️ Captured when they are seeded, never read live: the workspace re-reads
   * the plan on every realtime event, so saving with `plan.version` silently erased a séance a colleague had just
   * added — no 409, because the token was always the newest (F4).
   */
  const seededVersionRef = useRef(0)
  const seededForRef = useRef<string | null>(null)
  /** Set after a detach or « Recharger »: the next copy of the act from the server is taken in full. */
  const reseedRef = useRef(false)

  // Seed once per open (and after a detach or a reload), never on every new `item`: that discarded typing and
  // took the colleague's version with it.
  useEffect(() => {
    if (!open || !item) {
      if (!open) seededForRef.current = null
      return
    }
    if (seededForRef.current === item.id && !reseedRef.current) return
    seededForRef.current = item.id
    const firstSeed = !reseedRef.current
    reseedRef.current = false
    seededVersionRef.current = plan.version
    conflict.reset()
    const seeded: StepRow[] = (item.steps ?? []).map((step) => ({
      key: step.id,
      id: step.id,
      label: step.label,
      duration: step.estimatedDurationMinutes?.toString() ?? "",
      minDays: step.minDaysAfterPrevious?.toString() ?? "",
      doneDate: step.doneDate,
      linkedDentalRecordId: step.linkedDentalRecordId,
    }))
    // The strip's « + »: one new row, on the first seed of this open only — a reload must not add a second.
    if (addOnOpen && firstSeed) seeded.push(emptyRow(seeded.length))
    setRows(seeded)
    // `conflict.reset` is a stable useCallback; listing `conflict` itself would re-run this on every render
    // and discard what the dentist is typing — matching `edit-patient-dialog`'s own seeding effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, item])

  const doneCount = useMemo(() => rows.filter((r) => r.doneDate).length, [rows])

  const update = (key: string, patch: Partial<StepRow>) =>
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const remove = (key: string) => setRows((prev) => prev.filter((r) => r.key !== key))

  const add = () => setRows((prev) => [...prev, emptyRow(prev.length)])

  /*
   * Put the séance the strip was tapped on (or the row « + » just added) in front of the reader. Deferred a frame:
   * the content mounts in the tick `open` flips. Scrolled, never focused — a field focused on open raises the
   * keyboard over the list on a tablet (§ 5).
   */
  const rowRefs = useRef(new Map<string, HTMLDivElement | null>())
  const targetKey = focusStepId ?? (addOnOpen ? rows[rows.length - 1]?.key ?? null : null)
  useEffect(() => {
    if (!open || !targetKey) return
    const frame = requestAnimationFrame(() => {
      rowRefs.current.get(targetKey)?.scrollIntoView({ block: "center" })
    })
    return () => cancelAnimationFrame(frame)
  }, [open, targetKey])

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
      await treatmentPlansApi.setItemSteps(plan.id, item.id, payload, seededVersionRef.current)
      toast.success(
        payload.length === 0
          ? "Séances retirées — une seule séance"
          : "Séances enregistrées",
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
          <DialogTitle>Séances · {item.designationFr}</DialogTitle>
          <DialogDescription>{planDevisLabel(plan)}</DialogDescription>
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
                ? { label: "Recharger", onClick: () => { reseedRef.current = true; onSaved() }, disabled: saving || detachBusy }
                : undefined
            }
          />

          {rows.length === 0 && (
            <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">
              <b className="text-foreground">Une seule séance</b>
            </p>
          )}

          {rows.map((row, index) => {
            const done = row.doneDate != null
            return (
              <div
                key={row.key}
                ref={(el) => {
                  rowRefs.current.set(row.key, el)
                }}
                className={cn(
                  "flex flex-wrap items-center gap-2 rounded-md border p-2 sm:flex-nowrap",
                  done && "bg-muted",
                  // A ring, not a colour: the row is not in a state, it is the one the strip pointed at.
                  row.key === targetKey && "ring-2 ring-primary/40",
                )}
              >
                <div className="flex shrink-0 items-center">
                  {done ? (
                    // A done séance cannot move — no handle, just the arrows' width so the numbers line up.
                    <span aria-hidden="true" className="w-6 shrink-0 coarse:w-8" />
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

                      A done séance cannot move: its « faite le » chip says why, visibly — a `title` needs a hover.
                    */
                    <div className="flex flex-col">
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-6 coarse:size-8"
                        aria-label={`Monter ${quoteFr(row.label || "cette séance")}`}
                        disabled={saving || index === 0 || row.doneDate != null || rows[index - 1]?.doneDate != null}
                        onClick={() => move(index, -1)}
                      >
                        <ChevronUp className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-6 coarse:size-8"
                        aria-label={`Descendre ${quoteFr(row.label || "cette séance")}`}
                        // ⚠️ Disabled whenever `move` would refuse — it was clickable and did nothing when this or
                        // the next séance was already réalisée (F11).
                        disabled={saving || index === rows.length - 1 || row.doneDate != null || rows[index + 1]?.doneDate != null}
                        onClick={() => move(index, 1)}
                      >
                        <ChevronDown className="h-4 w-4" />
                      </Button>
                    </div>
                  )}
                  <span className="w-4 shrink-0 text-center tabular-nums text-2xs text-muted-foreground">
                    {index + 1}
                  </span>
                </div>

                {/* A done séance's name takes the free width and wraps — never « Prépar… » (a name is an identity). */}
                <div className={cn("min-w-0 flex-1 basis-full sm:basis-0", done && "sm:min-w-24")}>
                  <Label htmlFor={`step-label-${row.key}`} className="sr-only">
                    Nom de la séance {index + 1}
                  </Label>
                  {done ? (
                    <p className="text-sm font-medium [overflow-wrap:anywhere]">{row.label}</p>
                  ) : (
                    <Input
                      id={`step-label-${row.key}`}
                      value={row.label}
                      onChange={(e) => update(row.key, { label: e.target.value })}
                      disabled={saving}
                      placeholder="Nom de la séance"
                      className="md:text-sm"
                    />
                  )}
                </div>

                {done && (
                  <span className="flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md bg-success-wash px-2 py-0.5 text-2xs font-semibold text-success">
                    <Check className="h-3 w-3" aria-hidden="true" />
                    faite le {seanceDay(row.doneDate)}
                  </span>
                )}

                {/* `flex-wrap` + `max-w-full`: on its own line at 320 px « Remettre à faire » wraps under the figures. */}
                <div className="flex max-w-full shrink-0 flex-wrap items-center gap-2">
                  <Label htmlFor={`step-dur-${row.key}`} className="sr-only">
                    Durée de la séance {index + 1}, en minutes
                  </Label>
                  {done ? (
                    <span className="w-20 text-end tabular-nums text-2xs text-muted-foreground">
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
                        className="w-24 pe-9 text-end tabular-nums md:text-sm"
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
                    <span className="w-20 text-end tabular-nums text-2xs text-muted-foreground">
                      {index === 0 ? "aucun délai" : row.minDays ? `+ ${row.minDays} j` : "—"}
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
                        className="w-24 pe-14 text-end tabular-nums md:text-sm"
                      />
                      <span className="pointer-events-none absolute end-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                        j après
                      </span>
                    </div>
                  )}
                  {/*
                    A done step is put back « à faire », never deleted: its link is the only route back to the
                    fiche that attests it, so the two verbs get different controls — the first one in words.
                  */}
                  {done ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-11 hover-hover:hover:text-foreground"
                      aria-label={`Remettre à faire la séance ${quoteFr(row.label)}`}
                      disabled={saving || detachBusy}
                      onClick={() => setDetaching(row)}
                    >
                      <Undo2 className="h-4 w-4" />
                      Remettre à faire
                    </Button>
                  ) : (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="size-9 shrink-0 text-muted-foreground coarse:size-11"
                      aria-label={`Supprimer la séance ${quoteFr(row.label || String(index + 1))}`}
                      disabled={saving || detachBusy}
                      onClick={() => remove(row.key)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  )}
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

          {/* The question a dentist has before re-cutting a signed devis — stated as a fact. */}
          <Consequences
            className="border-t pt-3 text-xs"
            items={[
              <><b className="text-foreground">Aucun montant</b> ne change</>,
              doneCount > 0 && (
                <>
                  <b className="text-foreground">
                    {doneCount} séance{doneCount > 1 ? "s" : ""} faite{doneCount > 1 ? "s" : ""}
                  </b>{" "}
                  : {doneCount > 1 ? "liées à leur fiche, non supprimables" : "liée à sa fiche, non supprimable"}
                </>
              ),
            ]}
          />
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
              Remettre {quoteFr(detaching?.label ?? "")} à faire&nbsp;?
            </AlertDialogTitle>
            {/*
              ⚠️ The one condition, stated before the press: the server refuses when a live note bills this
              séance, and the fiche→note link is not on the plan DTO, so it can only be foretold.
            */}
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>La fiche de soins est <b className="text-foreground">conservée</b>, seulement détachée</>,
                  <><b className="text-foreground">Aucun montant</b> ne bouge</>,
                  <>Fiche facturée : <b className="text-foreground">créditer la note</b> d&apos;abord</>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={detachBusy}>Retour</AlertDialogCancel>
            <AlertDialogAction variant="destructive" disabled={detachBusy} onClick={handleDetach}>
              {detachBusy ? "En cours…" : "Remettre à faire"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Dialog>
  )
}
