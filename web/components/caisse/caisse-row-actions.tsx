"use client"

import { useState } from "react"
import { MoreHorizontal, Loader2 } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { CorrectInvoiceDialog, DEFAULT_CORRECTION_REASON } from "@/components/factures/correct-invoice-dialog"
import { invoicesApi } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import { formatDT, localDayIso } from "@/lib/format"
import type { CaisseMovementDto } from "@/lib/api/types"
import { ExpenseMovementActions } from "./expense-movement-actions"

interface CaisseRowActionsProps {
  movement: CaisseMovementDto
  /** Re-read the statement after a change — the running balance and the totals both move. */
  onChanged: () => void
}

/**
 * Correcting a line of the statement, from the statement.
 *
 * <p><b>Why the amount is not editable in place, when the date is.</b> They are not the same kind of fact. A date
 * is the record of when cash changed hands and no document carries it, so it is corrected here and nothing else
 * moves. An amount is on a numbered note d'honoraires — typing over it in the ledger would leave la caisse and
 * the note disagreeing, which is the exact defect this whole area exists to remove. So « Corriger le montant »
 * opens the note's correction instead, right here, without sending anyone to another page.</p>
 *
 * <p>The two corrections above are offered only on an invoice payment that is still live: a voided row is
 * already out of every total, and an avoir is its own document.</p>
 *
 * <p><b>A dépense is the other case, and it gets the whole form.</b> It has no note behind it — the row IS the
 * record — so `ExpenseMovementActions` opens the same « Modifier la dépense » the dépenses table below opens,
 * every field editable, plus the bin for an admin. Until then this component returned null for a dépense, so the
 * « Corriger » cell was empty on exactly the lines whose correction needs no document at all.</p>
 */
export function CaisseRowActions({ movement, onChanged }: CaisseRowActionsProps) {
  const [dateOpen, setDateOpen] = useState(false)
  const [correctOpen, setCorrectOpen] = useState(false)
  // ⚠️ `localDayIso`, never `.slice(0, 10)`. `occurredOn` is an INSTANT at the start of a Tunisian day, so a
  // payment received on the 1st serialises `2026-08-31T23:00:00Z` and slicing pre-filled « Corriger la date »
  // with the 31st of August — a plausible wrong day in the one field the dialog exists to set.
  const [paidOn, setPaidOn] = useState(localDayIso(movement.occurredOn))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isExpense = movement.kind === "Expense" && Boolean(movement.targetId)
  const isCorrectablePayment =
    movement.kind === "InvoicePayment" && !movement.isVoided && Boolean(movement.targetId)

  // It owns its own trigger and menu, because its dialogs have to outlive the menu closing.
  if (isExpense) {
    return <ExpenseMovementActions movement={movement} onChanged={onChanged} />
  }

  if (!isCorrectablePayment) return null

  const invoiceId = movement.targetId!

  const submitDate = async () => {
    setBusy(true)
    setError(null)
    try {
      await invoicesApi.amendPaymentDate(invoiceId, movement.id, paidOn)
      toast.success("Date du paiement corrigée.", {
        description: "La note garde le jour où elle a été écrite ; seule la caisse bouge.",
      })
      setDateOpen(false)
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "La correction de la date a échoué.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon" aria-label={`Corriger ${movement.label}`}>
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem
            onSelect={() => {
              setPaidOn(localDayIso(movement.occurredOn))
              setError(null)
              setDateOpen(true)
            }}
          >
            Corriger la date
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => setCorrectOpen(true)}>Corriger le montant…</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={dateOpen} onOpenChange={(next) => { if (!busy) setDateOpen(next) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Corriger la date du paiement</AlertDialogTitle>
            <AlertDialogDescription>
              {formatDT(movement.amount)} — {movement.label}. La note d&apos;honoraires garde le jour où elle a
              été écrite ; c&apos;est la date à laquelle l&apos;argent a changé de mains qui est corrigée.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={`paid-on-${movement.id}`} className="text-xs">
              Reçu le
            </Label>
            <Input
              id={`paid-on-${movement.id}`}
              type="date"
              value={paidOn}
              onChange={(e) => {
                setPaidOn(e.target.value)
                if (error) setError(null)
              }}
              disabled={busy}
            />
          </div>
          {error && (
            <p role="alert" className="text-xs font-medium text-destructive">
              {error}
            </p>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                // The correction has to survive its own failure — the primitive would close on click.
                e.preventDefault()
                void submitDate()
              }}
              disabled={busy || paidOn === ""}
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : "Corriger"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {correctOpen && (
        <CorrectInvoiceDialog
          open
          onOpenChange={setCorrectOpen}
          preview={{
            invoiceNumber: movement.reference,
            previousTotal: movement.amount,
            nextTotal: movement.amount,
          }}
          onConfirm={async () => {
            await invoicesApi.correct(invoiceId, DEFAULT_CORRECTION_REASON)
            toast.success("Correction ouverte en brouillon.", {
              description:
                "Ouvrez « Factures » pour la modifier puis l'émettre : la note d'origine sera annulée à ce moment-là.",
            })
            setCorrectOpen(false)
            onChanged()
          }}
        />
      )}
    </>
  )
}
