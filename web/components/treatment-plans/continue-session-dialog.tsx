"use client"

import { useEffect, useState } from "react"
import { History, Loader2 } from "lucide-react"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { EmptyState } from "@/components/ui/empty-state"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { ContinuableActDto, TreatmentPlanDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { formatDT, formatDateFr, parseAmountInput } from "@/lib/format"
import { cn } from "@/lib/utils"

interface ContinueSessionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patientId: string
  /** The devis that was created, so the caller can put its next step on the séance being booked. */
  onCreated: (plan: TreatmentPlanDto) => void
}

/** What the next séance is called when the dentist does not say. Matches the server's own default. */
const DEFAULT_NEXT_LABEL = "Séance suivante"

/**
 * « C'est la suite d'une séance précédente ? » — turning an act already carried out into a treatment.
 *
 * <p>The case the client named: an act quoted and started as a one-off, not finished, and the dentist now booking
 * the visit that finishes it. Before this the product had two doors — a devis written up front, and attaching a
 * séance to a devis that already exists — and neither covers work that never had a devis at all.</p>
 *
 * <p>⚠️ <b>Every recent act is listed and the dentist picks; nothing is inferred.</b> A fiche records what was
 * <i>done</i> and never what remains, so no read can tell an unfinished bridge from a finished obturation.
 * Guessing would be wrong on ordinary completed work, which is most of it.</p>
 *
 * <p>⚠️ <b>Each row states what happens to the money, because the two cases are opposite.</b> With the séance
 * already on a note d'honoraires, that note keeps the money and the devis — though it carries the act's fee like
 * any other — never asks for it again, so the 200 DT still owed is collected on the note. With no note, the devis
 * takes the fee and bills it once when the treatment is finished. A dentist who cannot tell which of those is
 * happening will either collect twice or not at all.</p>
 *
 * <p>⚠️ <b>The first séance's label is fixed and generic.</b> Nothing knows how the finished work was actually
 * divided, so « Préparation / Empreinte » would be a claim about a patient's mouth; « 1re séance » is marked done
 * against the fiche that evidences it and both steps stay editable in the ordinary steps dialog afterwards.</p>
 */
export function ContinueSessionDialog({
  open,
  onOpenChange,
  patientId,
  onCreated,
}: ContinueSessionDialogProps) {
  const [acts, setActs] = useState<ContinuableActDto[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [selected, setSelected] = useState<ContinuableActDto | null>(null)
  const [nextLabel, setNextLabel] = useState(DEFAULT_NEXT_LABEL)
  /** What the remaining work is worth, as typed. Empty means « rien de plus » — see the field. */
  const [remainingCost, setRemainingCost] = useState("")
  const [saving, setSaving] = useState(false)
  /** The irreversibility question — see the footer. */
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = async () => {
    setFailed(false)
    setActs(null)
    try {
      setActs(await treatmentPlansApi.continuableActs(patientId))
    } catch {
      // ⚠️ Never an empty list: « aucune séance » and « je n'ai pas pu lire » are the same picture and opposite
      // facts, and here the wrong one closes the only door this dialog exists to open (§ 13).
      setFailed(true)
    }
  }

  useEffect(() => {
    if (!open) return
    setSelected(null)
    setNextLabel(DEFAULT_NEXT_LABEL)
    setRemainingCost("")
    setConfirming(false)
    setError(null)
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, patientId])

  const parsedRemaining = parseAmountInput(remainingCost)
  const remainingWork =
    remainingCost.trim() === "" || !Number.isFinite(parsedRemaining) ? 0 : parsedRemaining
  const remainingInvalid = remainingCost.trim() !== "" && (!Number.isFinite(parsedRemaining) || parsedRemaining < 0)

  const submit = async () => {
    if (!selected) return
    if (remainingInvalid) {
      setError("Le montant du travail restant est invalide.")
      return
    }
    setSaving(true)
    setError(null)
    try {
      const plan = await treatmentPlansApi.continueRecordedAct({
        dentalRecordId: selected.dentalRecordId,
        actId: selected.actId,
        nextStepLabel: nextLabel.trim() || undefined,
        // Parsed, never `Number(...)`: the field prints and accepts the comma form the rest of the product
        // uses, and `parseFloat` stops at it — « 250,000 » would be sent as 250.
        remainingWorkCost: remainingWork > 0 ? remainingWork : undefined,
      })
      onCreated(plan)
      onOpenChange(false)
    } catch (err) {
      // Left open with the selection intact — the refusal is usually « already part of a treatment », and the
      // dentist's next move is to pick a different séance, not to start over.
      setError(getErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Suite d&apos;une séance précédente</DialogTitle>
          <DialogDescription>
            Choisissez l&apos;acte que ce rendez-vous poursuit. Il devient un traitement en plusieurs séances, et
            ce rendez-vous en est la deuxième.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 overflow-y-auto">
          {error && <FormErrorBanner message={error} />}

          {failed ? (
            <LoadFailureNotice
              message="Les séances précédentes n'ont pas pu être chargées."
              onRetry={() => void load()}
            />
          ) : acts === null ? (
            <div className="space-y-2">
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-16 animate-pulse rounded-md bg-muted" />
              ))}
            </div>
          ) : acts.length === 0 ? (
            <EmptyState
              size="compact"
              icon={History}
              title="Aucune séance à poursuivre"
              description="Les actes des quatre derniers mois qui ne font pas déjà partie d'un traitement apparaîtront ici."
            />
          ) : (
            <ul className="space-y-2" role="radiogroup" aria-label="Séance à poursuivre">
              {acts.map((act) => {
                const isSelected = selected?.actId === act.actId
                return (
                  <li key={act.actId}>
                    <button
                      type="button"
                      role="radio"
                      aria-checked={isSelected}
                      onClick={() => setSelected(act)}
                      className={cn(
                        "w-full rounded-md border p-3 text-start transition-colors",
                        isSelected ? "border-primary bg-primary/[0.05]" : "hover-hover:hover:bg-accent/40",
                      )}
                    >
                      <div className="flex flex-wrap items-baseline justify-between gap-x-2 gap-y-0.5">
                        <span className="min-w-0 flex-1 text-sm font-medium [overflow-wrap:anywhere]">
                          {act.procedureName}
                        </span>
                        <span className="shrink-0 text-2xs text-muted-foreground">
                          {formatDateFr(act.interventionDate)}
                        </span>
                      </div>

                      {/*
                        ⚠️ **The fee and the teeth make two rows of one act distinguishable, and they were not.**
                        One fiche can hold two acts of the same name on the same day, and the list then rendered
                        them character for character — same name, same date, same money sentence, with no way to
                        tell which was which. Whichever is picked mints a numbered, accepted devis, so an
                        indistinguishable pair is a coin flip with a money claim on it.
                      */}
                      <p className="mt-0.5 flex flex-wrap items-baseline gap-x-2 text-2xs text-muted-foreground">
                        {act.toothNumbers.length > 0 && <span>Dents {act.toothNumbers.join(", ")}</span>}
                        <span className="font-mono tabular-nums">{formatDT(act.cost)}</span>
                      </p>

                      {/*
                        The money sentence, and it is the reason each row is three lines rather than one. The two
                        cases lead to opposite actions at the next séance, and neither is guessable from the act's
                        name.
                      */}
                      <p className="mt-1.5 text-2xs leading-relaxed">
                        {act.invoiceNumber ? (
                          <>
                            <span className="text-muted-foreground">
                              Facturé {formatDT(act.cost)} sur la note{" "}
                            </span>
                            <span className="font-mono">{act.invoiceNumber}</span>
                            {act.invoiceOutstanding > 0 ? (
                              <span className="font-medium text-warning-ink">
                                {" "}
                                · reste {formatDT(act.invoiceOutstanding)} à encaisser sur cette note
                              </span>
                            ) : (
                              <span className="text-muted-foreground"> · entièrement réglée</span>
                            )}
                          </>
                        ) : (
                          <span className="text-muted-foreground">
                            Non facturée — le devis portera {formatDT(act.cost)} et sera facturé une fois le
                            traitement terminé.
                          </span>
                        )}
                      </p>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}

          {selected && (
            <div className="space-y-2 rounded-md border border-dashed p-3">
              <Label htmlFor="next-step-label" className="text-sm">
                Nom de cette séance
              </Label>
              <Input
                id="next-step-label"
                value={nextLabel}
                onChange={(e) => setNextLabel(e.target.value)}
                placeholder={DEFAULT_NEXT_LABEL}
                className="md:text-sm"
              />
              {/*
                ⚠️ **The money the remaining work is worth, and there was no field for it at all** — so every
                retroactive continuation systematically under-priced by exactly the value of what was left to
                do. Live data shows the result: « Extraction simple · 120 DT » whose next séance is « Pose de la
                prothèse », a prosthesis quoted at an extraction's fee. The only remedy was to amend the devis
                afterwards, and on a plan bridged to a note that added money was uncollectable and invisible to
                every money read.

                It becomes its OWN act on the devis rather than a larger fee on the first, because the first
                act's price is what the patient was already quoted — and on the billed path what a numbered note
                already says. Optional: « rien de plus » is a real answer, and it is the default.
              */}
              <div className="space-y-1.5">
                <Label htmlFor="remaining-cost" className="text-sm">
                  Montant du travail restant{" "}
                  <span className="font-normal text-muted-foreground">(facultatif)</span>
                </Label>
                <div className="flex items-center gap-2">
                  <Input
                    id="remaining-cost"
                    value={remainingCost}
                    inputMode="decimal"
                    onChange={(e) => setRemainingCost(e.target.value)}
                    placeholder="0,000"
                    className="w-32 text-end font-mono tabular-nums md:text-sm"
                  />
                  <span className="text-xs text-muted-foreground">DT</span>
                </div>
                <p className="text-2xs leading-relaxed text-muted-foreground">
                  Ajouté au devis comme un acte à part, sous le nom de cette séance. Laissez vide si la suite
                  n&apos;ajoute rien à ce qui a déjà été convenu.
                </p>
              </div>

              <p className="text-2xs leading-relaxed text-muted-foreground">
                La séance du {formatDateFr(selected.interventionDate)} sera enregistrée comme la 1re, déjà
                réalisée.{" "}
                {/*
                  ⚠️ « ne portera aucun montant » was the first wording and it is FALSE: the devis carries the
                  act's fee as its total, exactly like any other. What it does not do is ask for it a second
                  time — the note keeps the money and « Solde patient » drops a plan billed into one. Saying the
                  devis is empty would be contradicted by the devis itself the moment the dentist opened it.
                */}
                {selected.invoiceNumber
                  ? `Le devis ne réclamera rien de plus : la note ${selected.invoiceNumber} garde l'argent de cet acte.`
                  : "Le devis portera le montant de l'acte et sera facturé une fois le traitement terminé."}{" "}
                Vous pourrez renommer et découper les étapes ensuite.
              </p>
            </div>
          )}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          {/*
            ⚠️ **Confirmed, because it is irreversible and had no confirmation at all.** One press mints a devis
            that is numbered AND accepted in the same save, so it can never be deleted — only cancelled, with a
            motif, on the books for ever. Worse, picking the wrong séance used to be a permanent dead end: the
            note was attached to that devis write-once, the continuation could never be re-run for that fiche,
            and the fiche itself became undeletable. (Cancelling now releases the note, so a mistake is
            recoverable — but a numbered, accepted document still deserves the question.)
          */}
          <Button type="button" onClick={() => setConfirming(true)} disabled={!selected || saving}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Créer le traitement…
          </Button>
        </DialogFooter>
      </DialogContent>

      <AlertDialog open={confirming} onOpenChange={setConfirming}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Créer le traitement pour {selected?.procedureName} ?</AlertDialogTitle>
            <AlertDialogDescription>
              Un devis numéroté et <b>accepté</b> est créé immédiatement. La séance du{" "}
              {selected ? formatDateFr(selected.interventionDate) : ""} y figurera comme la 1re, déjà réalisée.
              {remainingWork > 0
                ? ` Le travail restant est ajouté comme un acte à part, à ${formatDT(remainingWork)}.`
                : " Aucun montant n'est ajouté à ce qui a déjà été convenu."}{" "}
              Un devis ne se supprime pas : il s&apos;annule, avec un motif.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={saving}
              onClick={(event) => {
                event.preventDefault()
                setConfirming(false)
                void submit()
              }}
            >
              Créer le traitement
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Dialog>
  )
}
