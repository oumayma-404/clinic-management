"use client"

import { useEffect, useState } from "react"
import { ChevronDown, ChevronUp, Info, Loader2, Pencil, Plus, Trash2 } from "lucide-react"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { ApiError } from "@/lib/api/client"
import { quoteFr } from "@/lib/format"
import { toast } from "sonner"
import { formatDurationFr } from "@/components/appointment-recap"
import { cn } from "@/lib/utils"

interface ProcedureTypeStepsDialogProps {
  /** The act being edited; null closes the dialog. */
  procedureType: ProcedureTypeDto | null
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}

interface StepRow {
  label: string
  duration: string
  /**
   * Calendar days to wait after the previous séance. A different quantity from the duration beside it: that
   * one sizes the appointment, this one decides when it is due — and its absence is why the worklist alarmed
   * on a flat fortnight for every protocol, whatever its clinical rhythm.
   */
  interval: string
}

/**
 * « Étapes » — the step-protocol editor for one act, in its own dialog.
 *
 * <p><b>It was a field at the bottom of `procedure-type-form-modal`, and that is the defect this closes.</b>
 * The protocol sat fifth in a long form behind the cost and the category, and in the table it rendered as a grey
 * run-on sentence under the act's name (« Bilan · Pose · Contrôle · … ») — so it read as a <i>description</i>,
 * and a dentist had no reason to think it was theirs to change. Both halves are fixed together: the row now
 * carries a control that says how many séances there are and opens this.</p>
 *
 * <p>⚠️ <b>Its own dialog, on `procedure-type-materials-dialog`'s reasoning</b>, which is the same shape one
 * field along: a list with <b>replace</b> semantics (an empty list is a meaningful value — « cet acte se fait en
 * une séance » — not « unchanged »), belonging to an act that already has an id. Folding it back into the form
 * would also give the protocol two editors, which is how the two come to disagree.</p>
 *
 * <p>⚠️ <b>Reordering exists here and did not before.</b> The old editor could only append and delete, so
 * inserting « Empreinte » between two steps of a six-séance implant meant retyping the four below it — and the
 * order <i>is</i> the protocol, since it decides what the devis proposes booking next.</p>
 *
 * <p>⚠️ The saved list goes through the ordinary update command, whose `DefaultSteps` is already replace-valued
 * and whose every other field is « omit means unchanged » — so this sends the steps and the version and nothing
 * else, and cannot silently rewrite a name or a price it never showed.</p>
 */
export function ProcedureTypeStepsDialog({
  procedureType,
  onOpenChange,
  onSaved,
}: ProcedureTypeStepsDialogProps) {
  const [rows, setRows] = useState<StepRow[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const open = !!procedureType

  /*
   * ⚠️ The row this dialog was opened from is as old as the catalogue's last refetch, and its `version` is
   * checked against `xmin` on save — so saving from the snapshot raises « cet enregistrement a été modifié par
   * quelqu'un d'autre » on an act nobody else touched. `useFreshVersion` re-reads the act on open; the snapshot
   * stands until the server answers and if the read fails.
   */
  const { source: fresh, resync } = useFreshVersion(
    open,
    procedureType?.id,
    procedureType,
    async () => (procedureType ? await procedureTypesApi.get(procedureType.id) : null),
  )

  // Re-read from the act on every open, so a cancelled edit never leaks into the next act's dialog. Keyed on the
  // ID, not on the object: `fresh` changes identity when the server answers, and rebuilding the rows then would
  // throw away whatever had already been typed.
  useEffect(() => {
    if (!procedureType) return
    setRows(
      (procedureType.defaultSteps ?? []).map((step) => ({
        label: step.label,
        duration: step.durationMinutes != null ? String(step.durationMinutes) : "",
        interval: step.minDaysAfterPrevious != null ? String(step.minDaysAfterPrevious) : "",
      })),
    )
    setError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [procedureType?.id])

  const update = (index: number, patch: Partial<StepRow>) =>
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  /**
   * The row Enter has just appended, so focus can land in it. A state hop rather than a ref, because the input
   * does not exist yet at the moment the key is handled.
   */
  const [focusRow, setFocusRow] = useState<number | null>(null)
  useEffect(() => {
    if (focusRow == null) return
    document.getElementById(`proto-label-${focusRow}`)?.focus()
    setFocusRow(null)
  }, [focusRow])

  const remove = (index: number) => setRows((prev) => prev.filter((_, i) => i !== index))

  /** Move one step up or down. The order is the protocol, so this is a real edit, not a convenience. */
  const move = (index: number, delta: number) =>
    setRows((prev) => {
      const next = index + delta
      if (next < 0 || next >= prev.length) return prev
      const copy = [...prev]
      const [row] = copy.splice(index, 1)
      copy.splice(next, 0, row)
      return copy
    })

  /*
   * The chair time the protocol adds up to. Stated because nobody could see it: six séances of an implant come
   * to 4 h 05, which is the figure that tells a dentist whether the protocol they are writing is plausible —
   * and it is deliberately NOT compared with the act's own « Durée », which is the length of ONE ordinary
   * séance and answers a different question.
   */
  const kept = rows.filter((r) => r.label.trim().length > 0)
  const totalMinutes = kept.reduce((sum, r) => {
    const n = Number.parseInt(r.duration, 10)
    return sum + (Number.isFinite(n) && n > 0 ? n : 0)
  }, 0)

  const save = async () => {
    if (!procedureType) return
    setSaving(true)
    setError(null)
    try {
      /*
       * Blank rows are dropped in silence — an empty trailing row is somebody having pressed « Ajouter » and
       * changed their mind, not work to refuse. A row with a duration but no label is dropped too: the label is
       * what a séance IS, and a nameless one would render as an empty chip on the devis.
       */
      const payload = kept.map((r) => {
        const n = Number.parseInt(r.duration, 10)
        const days = Number.parseInt(r.interval, 10)
        return {
          label: r.label.trim(),
          durationMinutes: Number.isFinite(n) && n > 0 ? n : null,
          minDaysAfterPrevious: Number.isFinite(days) && days > 0 ? days : null,
        }
      })

      // Only the steps and the version: every other field of the update command means « unchanged » when
      // omitted, so this cannot touch a name, a price or a colour the dialog never displayed.
      await procedureTypesApi.update(procedureType.id, {
        defaultSteps: payload,
        version: (fresh ?? procedureType).version,
      })
      /*
       * ⚠️ There was no confirmation at all — polled across three saves, zero toasts — while creating a patient
       * toasts « Patient créé » and creating a devis toasts « Devis 2026-0014 créé et validé ». So the only way
       * to know a protocol had saved was to re-find the row in a paged table and read the cell.
       */
      toast.success(
        payload.length === 0
          ? "Protocole enregistré — cet acte se fait en une séance."
          : `Protocole enregistré — ${payload.length} séance${payload.length > 1 ? "s" : ""}.`,
      )
      onSaved()
      onOpenChange(false)
    } catch (err) {
      // Left open with the rows intact — retyping a six-séance protocol because a save failed is not a
      // reasonable ask.
      setError(getErrorMessage(err))
      /*
       * ⚠️ Re-read on a failure that is NOT a conflict. A 409 means somebody else really did edit the act, and
       * refreshing the version there would let the retry overwrite their work — the lost update the token
       * exists to prevent. Any other failure may have moved the row anyway (a partially-applied save), and
       * leaving the stale version behind would 409 every later attempt until a full reload.
       */
      if (!(err instanceof ApiError) || err.status !== 409) {
        await resync()
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => { if (!next) onOpenChange(false) }}>
      <DialogContent mobile="sheet" className="md:max-w-xl">
        <DialogHeader>
          <DialogTitle>
            {procedureType ? `Étapes — ${procedureType.name}` : "Étapes"}
          </DialogTitle>
          <DialogDescription>
            Les séances proposées quand cet acte est ajouté à un devis.
          </DialogDescription>
        </DialogHeader>

        {/*
          The count and the total, above the list. They are the two facts the table's control promises and the
          reason this dialog is worth opening on a screen where nothing else states them.
        */}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span className="rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-accent-foreground">
            {kept.length === 0
              ? "Aucune étape"
              : `${kept.length} séance${kept.length > 1 ? "s" : ""}`}
          </span>
          {totalMinutes > 0 && (
            <span className="font-mono text-2xs tabular-nums text-muted-foreground">
              {formatDurationFr(totalMinutes)} de fauteuil au total
            </span>
          )}
        </div>

        <div className="space-y-2 overflow-y-auto">
          {error && <FormErrorBanner message={error} />}

          {rows.length === 0 ? (
            <p className="rounded-md border border-dashed p-4 text-center text-xs text-muted-foreground">
              Cet acte se fait en une seule séance.
            </p>
          ) : (
            <ul className="space-y-2">
              {rows.map((row, index) => (
                <li key={index} className="flex flex-wrap items-center gap-1.5 sm:flex-nowrap">
                  {/*
                    ⚠️ Two 32 px buttons a few pixels apart, so they GROW on a coarse pointer rather than taking
                    `.touch-target`'s overlay — an overlay overhangs its neighbour and the later sibling paints
                    last, so a thumb aimed at « monter » would fire « descendre ». § 2.
                  */}
                  {/*
                    ⚠️ **No grip glyph.** A six-dot `grip-vertical` means « drag me » to anyone who has used a
                    phone, and nothing here was draggable — `cursor: auto`, no handler, pressing and dragging it
                    60 px changed nothing. So the first attempt to reorder read as a broken app, on a dialog
                    whose whole subject is the order. The chevrons beside it work and are the affordance; the
                    glyph was promising a second one that did not exist.
                  */}
                  <div className="flex shrink-0 items-center">
                    <div className="flex flex-col">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        disabled={saving || index === 0}
                        className="size-6 coarse:size-8"
                        aria-label={`Monter l'étape ${quoteFr(row.label || String(index + 1))}`}
                        onClick={() => move(index, -1)}
                      >
                        <ChevronUp className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        disabled={saving || index === rows.length - 1}
                        className="size-6 coarse:size-8"
                        aria-label={`Descendre l'étape ${quoteFr(row.label || String(index + 1))}`}
                        onClick={() => move(index, 1)}
                      >
                        <ChevronDown className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>

                  <span className="w-4 shrink-0 text-center font-mono text-2xs text-muted-foreground">
                    {index + 1}
                  </span>

                  <Label htmlFor={`proto-label-${index}`} className="sr-only">
                    Libellé de l&apos;étape {index + 1}
                  </Label>
                  <Input
                    id={`proto-label-${index}`}
                    value={row.label}
                    placeholder="ex. : Empreinte"
                    disabled={saving}
                    className="min-w-0 flex-1 basis-full sm:basis-0 md:text-sm"
                    onChange={(e) => update(index, { label: e.target.value })}
                    /*
                      Enter on the last row appends the next step and focuses it — a 4-step protocol cost four
                      separate clicks on « Ajouter une étape », and the keyboard path was six keystrokes of
                      overhead per step. `preventDefault` because not submitting on Enter is the right behaviour
                      here and must stay: this dialog's save rewrites the whole protocol.
                    */
                    onKeyDown={(e) => {
                      if (e.key !== "Enter") return
                      e.preventDefault()
                      if (index !== rows.length - 1 || row.label.trim() === "") return
                      setRows((prev) => [...prev, { label: "", duration: "", interval: "" }])
                      setFocusRow(rows.length)
                    }}
                  />

                  <Label htmlFor={`proto-duration-${index}`} className="sr-only">
                    Durée de l&apos;étape {index + 1}, en minutes
                  </Label>
                  <div className="relative shrink-0">
                    <Input
                      id={`proto-duration-${index}`}
                      value={row.duration}
                      inputMode="numeric"
                      placeholder="30"
                      disabled={saving}
                      className="w-24 pe-9 text-end font-mono tabular-nums md:text-sm"
                      onChange={(e) => update(index, { duration: e.target.value })}
                    />
                    <span className="pointer-events-none absolute end-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                      min
                    </span>
                  </div>

                  {/*
                    The interval — « après », not « pendant ». The first step has none by definition, so its
                    field is not rendered at all rather than shown disabled.

                    ⚠️ This is the field the whole worklist alarm turns on, and the schema had nowhere to put
                    it: the catalogue's own research states « les séances sont espacées d'une semaine environ »,
                    « la réévaluation est à 8 semaines minimum » and three to six months of ostéointégration, and
                    all of it was discarded. With nothing to compare against, a correctly-progressing implant
                    read as abandoned for ten of its twelve weeks.
                  */}
                  {index > 0 ? (
                    <div className="relative shrink-0">
                      <Label htmlFor={`proto-interval-${index}`} className="sr-only">
                        Délai après la séance précédente, en jours, pour l&apos;étape {index + 1}
                      </Label>
                      <Input
                        id={`proto-interval-${index}`}
                        value={row.interval}
                        inputMode="numeric"
                        placeholder="7"
                        disabled={saving}
                        title="Délai minimum après la séance précédente. Laissez vide si le délai est libre."
                        className="w-24 pe-14 text-end font-mono tabular-nums md:text-sm"
                        onChange={(e) => update(index, { interval: e.target.value })}
                      />
                      <span className="pointer-events-none absolute end-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                        j après
                      </span>
                    </div>
                  ) : (
                    <span
                      className="hidden w-24 shrink-0 text-end text-2xs text-muted-foreground sm:block"
                      aria-hidden="true"
                    >
                      1re séance
                    </span>
                  )}

                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={saving}
                    className="size-9 shrink-0 text-muted-foreground coarse:size-11"
                    aria-label={`Supprimer l'étape ${quoteFr(row.label || String(index + 1))}`}
                    onClick={() => remove(index)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </li>
              ))}
            </ul>
          )}

          <Button
            type="button"
            variant="outline"
            disabled={saving}
            className="w-full border-dashed text-primary coarse:h-11"
            onClick={() => setRows((prev) => [...prev, { label: "", duration: "", interval: "" }])}
          >
            <Plus className="h-4 w-4" />
            Ajouter une étape
          </Button>

          {/*
            The sentence that keeps a dentist from thinking this is retroactive. It says three things because a
            dentist about to re-cut the protocol of a bridge asks all three: are these binding, do they carry
            money, and does this change what is already quoted.
          */}
          <div className="flex gap-2 rounded-md bg-muted/50 p-3">
            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
            <p className="text-2xs leading-relaxed text-muted-foreground">
              Ces étapes sont <span className="font-medium text-foreground">proposées</span> quand l&apos;acte est
              ajouté à un devis, puis modifiables cas par cas. Elles ne portent{" "}
              <span className="font-medium text-foreground">jamais de prix</span>, et les modifier ici ne change
              aucun devis en cours.
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button type="button" onClick={() => void save()} disabled={saving}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Enregistrer les étapes
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * The act's protocol as the catalogue row shows it — a control, not a sentence.
 *
 * <p>Exported beside the dialog it opens so the two cannot drift about what a protocol looks like, and used by
 * <b>both</b> trees of the table (the row and the card), which is the other way that pair drifts.</p>
 *
 * <p>⚠️ <b>It is a real `<button>` for an admin and inert text for everyone else.</b> `PUT /procedure-types/{id}`
 * is `AdminOnly`, so offering the control to reception would be a door that answers 403 — the reasoning
 * `access-denied-card` records, one control along.</p>
 */
export function ProcedureStepsCell({
  procedureType,
  onEdit,
  className,
}: {
  procedureType: ProcedureTypeDto
  /** Absent for a read-only role: the protocol still renders, it just does not invite a press. */
  onEdit?: (procedureType: ProcedureTypeDto) => void
  className?: string
}) {
  const steps = procedureType.defaultSteps ?? []
  const total = steps.reduce((sum, s) => sum + (s.durationMinutes ?? 0), 0)
  const shown = steps.slice(0, 2)
  const rest = steps.length - shown.length

  // An act with no protocol — most of them. It says so, and (for an admin) offers the one thing to do about it,
  // which is where a dentist actually discovers that acts can be cut into séances at all.
  if (steps.length === 0) {
    if (!onEdit) {
      // « Une seule séance » is a claim about the ACT; « Aucune étape définie » is a claim about its
      // configuration, which is what this cell actually knows. A non-admin reading the first would take it for
      // a clinical fact about an act nobody has cut up yet.
      return (
        <span className={cn("block text-2xs text-muted-foreground", className)}>Aucune étape définie</span>
      )
    }
    return (
      <button
        type="button"
        onClick={() => onEdit(procedureType)}
        className={cn(
          // ⚠️ `flex w-fit`, never `inline-flex`: the cell above it is the act NAME, so an inline control flows
          // straight after the last word — measured, « Extraction chirurgicale (sagesse / dent incluse) » ran
          // into « + Découper en étapes » on one line and the button read as part of the act's name.
          "flex w-fit min-h-8 items-center gap-1 rounded-md border border-dashed px-2 text-2xs text-muted-foreground transition-colors hover-hover:hover:border-primary/50 hover-hover:hover:text-primary coarse:min-h-11",
          className,
        )}
      >
        <Plus className="h-3 w-3" />
        Découper en étapes
      </button>
    )
  }

  const body = (
    <>
      <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <span className="rounded-full bg-primary/10 px-2 py-0.5 text-2xs font-semibold text-accent-foreground">
          {steps.length} séances
        </span>
        {total > 0 && (
          <span className="font-mono text-2xs tabular-nums text-muted-foreground">
            {formatDurationFr(total)}
          </span>
        )}
      </span>
      {/*
        The first two séances, numbered. The rank is what the run-on sentence could not carry and what makes the
        list read as an ordered protocol rather than as prose — `PlanStepStrip` gives the same act the same
        reading on a devis, so a dentist recognises it in both places.
      */}
      <span className="mt-1 flex flex-wrap gap-1">
        {shown.map((step, i) => (
          <span
            key={i}
            className="inline-flex max-w-full items-center gap-1 rounded-full bg-accent py-0.5 pe-2 ps-0.5 text-2xs text-accent-foreground"
          >
            <span className="flex size-4 flex-none items-center justify-center rounded-full bg-card font-mono text-2xs leading-none text-primary">
              {i + 1}
            </span>
            <span className="truncate">{step.label}</span>
          </span>
        ))}
        {rest > 0 && (
          <span className="inline-flex items-center rounded-full border border-dashed px-2 py-0.5 text-2xs text-muted-foreground">
            +{rest}
          </span>
        )}
      </span>
    </>
  )

  if (!onEdit) {
    return <span className={cn("flex flex-col items-start", className)}>{body}</span>
  }

  /*
    ⚠️ **A real bordered control, with a pencil — it had `border: 0`, `background: transparent` and
    `cursor: default`.** So the 20 acts that need nothing shouted (« + Découper en étapes » in a dashed pill)
    while the 14 a dentist actually needs to correct — an implant protocol that does not match how they work —
    offered no visual invitation at all. Reviewed cold, the empty-act route was found *by seeing it* and the
    filled-act route only *by reading the aria-label*.
  */
  return (
    <button
      type="button"
      onClick={() => onEdit(procedureType)}
      aria-label={`Modifier les ${steps.length} étapes de ${quoteFr(procedureType.name)}`}
      title={`Modifier les séances de ${procedureType.name}`}
      className={cn(
        "group flex w-fit min-h-8 cursor-pointer flex-col items-start gap-0.5 rounded-md border px-2 py-1 text-start transition-colors hover-hover:hover:border-primary/50 hover-hover:hover:bg-accent/60 coarse:min-h-11",
        className,
      )}
    >
      {body}
      <span className="flex items-center gap-1 text-2xs text-muted-foreground group-hover:text-primary">
        <Pencil className="h-3 w-3" aria-hidden="true" />
        Modifier les séances
      </span>
    </button>
  )
}
