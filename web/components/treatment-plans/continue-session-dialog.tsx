"use client"

import { useEffect, useState } from "react"
import { History } from "lucide-react"
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
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { EmptyState } from "@/components/ui/empty-state"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { ContinuableActDto } from "@/lib/api/types"
import { formatDT, formatDateFr, parseAmountInput } from "@/lib/format"
import { cn } from "@/lib/utils"

interface ContinueSessionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patientId: string
  /**
   * What the dentist chose, so the caller can put it on the séance being booked — **not** a devis.
   *
   * <p>⚠️ Nothing has been written when this fires. See {@link SelectedAct.pendingContinuation}.</p>
   */
  onChosen: (choice: ContinuationChoice) => void
  /**
   * Open with this act already chosen — « Planifier la suite » on « Suites à planifier », which knows exactly
   * which act it is about.
   *
   * <p>⚠️ <b>It preselects and does not skip.</b> The worklist's first version bypassed this dialog entirely
   * and handed the booking a choice with <code>remainingWorkCost: null</code>, which is the « rien de plus »
   * case — so the séance was named after the act that had ALREADY been done, the price field was withheld
   * (a devis-carried act holds no fee on the séance), and the card stated both « cette séance n'ajoute aucun
   * honoraire » and « Encaissement sur la note … ». Reported from use, in those words. What the dentist needs
   * to say here — what the remaining work is worth — has one home, and this is it.</p>
   *
   * <p>⚠️ The list is still rendered in full: the preselection is an opinion about which row, never a claim
   * that the others do not apply, and the dentist may pick a different séance.</p>
   */
  preselectActId?: string
}

/** The continuation as chosen, before anything exists on the server. */
export interface ContinuationChoice {
  /** The séance being continued, as the list offered it — the card reads its money straight from this. */
  previous: ContinuableActDto
  /** What this séance is called. */
  nextStepLabel: string
  /** What the remaining work is worth, or null for « rien de plus ». */
  remainingWorkCost: number | null
}

/** What the next séance is called when the dentist does not say. Matches the server's own default. */
const DEFAULT_NEXT_LABEL = "Séance suivante"

/** What the séance is called when the caller already named the act — see `preselectActId`. */
const PRESELECTED_NEXT_LABEL = "Suite"

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
 * <p>⚠️ <b>It writes NOTHING.</b> The choice is handed back through {@link ContinuationChoice} and the devis is
 * minted by `materialiseTreatments` when the booking is saved — `SelectedAct.pendingContinuation` carries it in
 * between. Creating it on the press was the first shape, and abandoning the booking afterwards left a numbered,
 * accepted devis with a lump-sum échéance against a séance that was never booked; the act it continued also
 * vanished from this list, since `ContinuationTracking` counts an act on any non-cancelled plan as taken.</p>
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
  onChosen,
  preselectActId,
}: ContinueSessionDialogProps) {
  const [acts, setActs] = useState<ContinuableActDto[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [selected, setSelected] = useState<ContinuableActDto | null>(null)
  const [nextLabel, setNextLabel] = useState(DEFAULT_NEXT_LABEL)
  /** What the remaining work is worth, as typed. Empty means « rien de plus » — see the field. */
  const [remainingCost, setRemainingCost] = useState("")
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
    // « Suite » when the caller already named the act, because the panel below then reads « Suite » rather than
    // the generic « Séance suivante » on a row whose subject is not in doubt. Still editable.
    setNextLabel(preselectActId ? PRESELECTED_NEXT_LABEL : DEFAULT_NEXT_LABEL)
    setRemainingCost("")
    setError(null)
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, patientId, preselectActId])

  /**
   * Apply the caller's preselection once the list has arrived.
   *
   * <p>⚠️ It cannot live in the reset effect above: the acts are fetched, so at that point there is nothing to
   * select from. And it is keyed on the LOADED list rather than run once, because a failed read that is then
   * retried must still land on the row the caller named.</p>
   *
   * <p>⚠️ An act the list does not offer leaves the dialog on its ordinary « choose one » state rather than
   * selecting nothing silently — that happens when a colleague continued the same séance in between, and the
   * list is then correct and the caller's row stale.</p>
   */
  useEffect(() => {
    if (!open || !preselectActId || !acts) return
    const match = acts.find((a) => a.actId === preselectActId)
    if (match) setSelected(match)
  }, [open, preselectActId, acts])

  /**
   * The caller named the act, so this dialog asks only what it alone can: the séance's name and what the
   * remaining work is worth. See {@link ContinueSessionDialogProps.preselectActId}.
   */
  const actIsFixed = preselectActId != null

  /** « la note 2026-0206 » or « le brouillon de note d'honoraires » — a Draft note has no number to print. */
  const noteLabel = selected?.invoiceNumber
    ? `la note ${selected.invoiceNumber}`
    : "le brouillon de note d'honoraires"

  const parsedRemaining = parseAmountInput(remainingCost)
  const remainingWork =
    remainingCost.trim() === "" || !Number.isFinite(parsedRemaining) ? 0 : parsedRemaining
  const remainingInvalid = remainingCost.trim() !== "" && (!Number.isFinite(parsedRemaining) || parsedRemaining < 0)

  /**
   * Hand the choice back. **Nothing is written here**, and that is the whole of the fix this dialog carries:
   * it used to create a numbered, accepted devis on this press, so « Annuler » on the booking behind it left a
   * live créance for a séance nobody booked — and the act it continued disappeared from this very list, which
   * counts an act on any non-cancelled plan as already continued. `materialiseTreatments` mints it on save,
   * exactly as a split protocol is minted.
   */
  const submit = () => {
    if (!selected) return
    if (remainingInvalid) {
      setError("Le montant du travail restant est invalide.")
      return
    }
    setError(null)
    onChosen({
      previous: selected,
      nextStepLabel: nextLabel.trim() || DEFAULT_NEXT_LABEL,
      // Parsed, never `Number(...)`: the field prints and accepts the comma form the rest of the product uses,
      // and `parseFloat` stops at it — « 250,000 » would be read as 250.
      remainingWorkCost: remainingWork > 0 ? remainingWork : null,
    })
    onOpenChange(false)
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
          ) : actIsFixed ? (
            /*
              ⚠️ **Fixed: the act is STATED, and the list is not offered at all.**
              This opening comes from « Planifier la suite » on « Suites à planifier », where the row *is* the
              act — re-asking « laquelle ? » makes the dentist recognise all over again the thing they just
              pressed, and worse, lets them pick a different séance than the one the row named. The other door
              (the rdv modal's « C'est la suite d'une séance précédente ? ») genuinely does not know, and keeps
              its list. Same dialog, two openings, one question each.
            */
            selected ? (
              <div className="rounded-md border border-primary bg-primary/[0.05] p-3">
                <ContinuableActSummary act={selected} />
              </div>
            ) : (
              /*
                The act the caller named is not in the list any more — a colleague continued the same séance in
                between, so the list is right and the row stale. Say that rather than silently falling back to
                the picker, which would invite continuing a *different* act than the one pressed.
              */
              <EmptyState
                size="compact"
                icon={History}
                title="Cette séance n'est plus à poursuivre"
                description="Elle fait maintenant partie d'un traitement — quelqu'un l'a peut-être déjà reprise. Fermez et rouvrez « Suites à planifier » pour voir l'état actuel."
              />
            )
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
                      <ContinuableActSummary act={act} />
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

              {/*
                ⚠️ **This paragraph carries what an AlertDialog used to say, and that is why it is worded this
                fully.** The confirmation was removed at the owner's request — it cost a click on every single
                continuation, and its whole content was a statement of consequences rather than a question only
                the user could answer. What it stated is still true and still worth reading, so it moved onto
                the surface where the decision is actually taken, beside the fields that determine it. Deleting
                the step is not deleting the fact: « un devis ne se supprime pas » is the one line here a
                dentist cannot recover from not having read.
              */}
              <p className="text-2xs leading-relaxed text-muted-foreground">
                La séance du {formatDateFr(selected.interventionDate)} sera enregistrée comme la 1re, déjà
                réalisée.{" "}
                {/*
                  ⚠️ « ne portera aucun montant » was the first wording and it is FALSE: the devis carries the
                  act's fee as its total, exactly like any other. What it does not do is ask for it a second
                  time — the note keeps the money and « Solde patient » drops a plan billed into one. Saying the
                  devis is empty would be contradicted by the devis itself the moment the dentist opened it.
                */}
                {/*
                  ⚠️ **Three cases, not two, and the third is the one that used to lie.** With a note already
                  holding the act AND a priced travail restant, « le devis ne réclamera rien de plus » is false:
                  the devis carries the new work and is what collects it. The server backs this exactly —
                  `noteKeepsTheFirstAct` prices the finished act 0 and leaves the note unattached, so the two
                  documents are disjoint and each is collected on its own. Before that pair of changes the
                  sentence was true of the devis and the money was owed by nobody a screen could see.
                */}
                {/* ⚠️ `invoiceId`, never the number — a Draft note has none, and it is still the note that
                    collects. `noteLabel` is what keeps the sentence whole in that case. */}
                {selected.invoiceId
                  ? remainingWork > 0
                    ? `${noteLabel} garde l'argent de cet acte ; le devis ne portera que le travail restant, ${formatDT(remainingWork)}, à encaisser sur le devis.`
                    : `Le devis ne réclamera rien de plus : ${noteLabel} garde l'argent de cet acte.`
                  : "Le devis portera le montant de l'acte et sera facturé une fois le traitement terminé."}{" "}
                Vous pourrez renommer et découper les étapes ensuite.
              </p>
            </div>
          )}
        </div>

        {/*
          ⚠️ **Outside the scroller, so it is on screen when the button is.** This is the line that replaced
          the confirmation step, and inside the scrolling middle it was doing the job badly: measured at
          320 px the panel is 1021 px in a 436 px window, so « Créer le traitement » was visible while the one
          consequence a dentist cannot recover from not having read sat two screens below it. A warning the
          reader has to go looking for is weaker than the modal it replaced, not merely quieter. Here it is
          pinned above the footer at every width — the sheet's own last line before the action.

          Rendered only with an act chosen, because until then it describes nothing.
        */}
        {selected && (
          <p
            className="shrink-0 border-t pt-3 text-2xs leading-relaxed text-warning-ink"
            role="status"
          >
            Le devis sera créé à l&apos;enregistrement du rendez-vous, numéroté et accepté : il ne se
            supprimera plus, il s&apos;annulera avec un motif. Tant que le rendez-vous n&apos;est pas
            enregistré, cette séance se retire du rendez-vous et rien n&apos;est écrit.
          </p>
        )}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          {/*
            ⚠️ **The press writes nothing**, which is why it no longer says « Créer le traitement ». It used to
            mint the devis here — numbered and accepted in the same save, so never deletable — and a dentist who
            then pressed « Annuler » on the booking behind it was left with a live créance for a séance nobody
            booked. The consequences still sit in the panel above, where they are read *before* the decision;
            what changed is that they now describe the SAVE. A confirmation step was removed from here at the
            owner's request and is not coming back: it asked nothing, and there is far less to confirm now.
          */}
          <Button type="button" onClick={submit} disabled={!selected}>
            Ajouter au rendez-vous
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * One continuable act, as the dialog states it — name · date · the « non terminé » mark · teeth · fee · which
 * document collects.
 *
 * <p>⚠️ <b>One composer for both openings.</b> The picker renders it inside a radio and the fixed opening
 * renders it as a statement; written twice, the money sentence — the half that says whether the note or the
 * devis collects, and therefore what the dentist does at the next séance — would be free to drift between the
 * two. That is this repository's signature defect, and this sentence is one of the ones it can least afford.</p>
 */
function ContinuableActSummary({ act }: { act: ContinuableActDto }) {
  return (
    <>
      <div className="flex flex-wrap items-baseline justify-between gap-x-2 gap-y-0.5">
        <span className="min-w-0 flex-1 text-sm font-medium [overflow-wrap:anywhere]">
          {act.procedureName}
        </span>
        <span className="shrink-0 text-2xs text-muted-foreground">
          {formatDateFr(act.interventionDate)}
        </span>
      </div>

      {/*
        ⚠️ **The dentist's own tick, surfaced — and in the PICKER the list is sorted by it, never filtered.**
        The server puts every ticked act at the top, so what this mark adds is the reason the order is what it
        is. It is not a claim that the unticked rows are finished: the box is one checkbox at the end of a
        séance and it is easy to forget, which is exactly why the list still offers everything.
      */}
      {act.isUnfinished && (
        <p className="mt-1 text-2xs font-medium leading-tight text-warning-ink [overflow-wrap:anywhere]">
          Noté « non terminé » à la séance
        </p>
      )}

      {/*
        ⚠️ **The fee and the teeth make two rows of one act distinguishable, and they were not.** One fiche can
        hold two acts of the same name on the same day, and the list rendered them character for character.
        Whichever is picked mints a numbered, accepted devis, so an indistinguishable pair is a coin flip with a
        money claim on it.
      */}
      <p className="mt-0.5 flex flex-wrap items-baseline gap-x-2 text-2xs text-muted-foreground">
        {act.toothNumbers.length > 0 && <span>Dents {act.toothNumbers.join(", ")}</span>}
        <span className="font-mono tabular-nums">{formatDT(act.cost)}</span>
      </p>

      {/*
        The money sentence. The two cases lead to opposite actions at the next séance and neither is guessable
        from the act's name.

        ⚠️ **`invoiceId`, never `invoiceNumber`** — `InvoiceLinkChoice.ByKey` keeps a DRAFT note, which has no
        number yet, and the server's own fork is `billingInvoice != null`. Reading the number put a billed
        séance on the « Non facturée » branch, i.e. the opposite of what the save was about to do.
      */}
      <p className="mt-1.5 text-2xs leading-relaxed">
        {act.invoiceId ? (
          <>
            <span className="text-muted-foreground">
              Facturé {formatDT(act.cost)} sur{" "}
              {act.invoiceNumber ? "la note " : "un brouillon de note d'honoraires"}
            </span>
            {act.invoiceNumber && <span className="font-mono">{act.invoiceNumber}</span>}
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
            Non facturée — le devis portera {formatDT(act.cost)} et sera facturé une fois le traitement
            terminé.
          </span>
        )}
      </p>
    </>
  )
}
