"use client"

import { useState } from "react"
import { toast } from "sonner"
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
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Loader2, MoreHorizontal } from "lucide-react"
import { ApiError } from "@/lib/api/client"
import { expensesApi } from "@/lib/api/expenses"
import { useSession } from "@/lib/auth/session"
import type { CaisseMovementDto, ExpenseDto } from "@/lib/api/types"
import { formatDT, localDayIso, quoteFr } from "@/lib/format"
import { ExpenseFormDialog } from "./expense-form-dialog"

/**
 * « Modifier » and « Supprimer » for a **dépense line of l'extrait de caisse**.
 *
 * <p>⚠️ **Why the statement can edit a dépense when it cannot edit a payment.** The two are different kinds of
 * fact. A payment is on a numbered note d'honoraires, so typing over it here would leave la caisse and the note
 * disagreeing — which is why `CaisseRowActions` sends that case to the note's own correction instead. A dépense
 * has no document: the row IS the record, so correcting it here and correcting it in the dépenses table below are
 * the same act on the same aggregate, and making the reader scroll to a second table to reach it was friction
 * with nothing behind it.</p>
 *
 * <p>It reuses `ExpenseFormDialog` — the same form « Nouvelle dépense » opens — so every field is editable,
 * date and catégorie included, and there is no second definition of what a dépense may be.</p>
 */
export function ExpenseMovementActions({
  movement,
  onChanged,
}: {
  movement: CaisseMovementDto
  onChanged: () => void
}) {
  const { user } = useSession()
  // Mirrors the server: `DELETE /api/expenses/{id}` is AdminOnly while the PUT is AdminOrDoctor, so offering
  // it to a praticien would be a control that answers 403. Same rule as the dépenses table's own bin.
  const canDelete = user?.role === "admin"

  const [expense, setExpense] = useState<ExpenseDto | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  // The row is read before the form can open, so the trigger says so rather than looking inert on a slow hop.
  const [busy, setBusy] = useState(false)

  /**
   * The movement carries a composed `label`, not the dépense's own fields — so the row is read before the form
   * opens, over the dépense's **own day** rather than the window on screen. There is no get-by-id on this
   * resource; `ExpenseFormDialog`'s version re-read uses the same shape.
   */
  const load = async (): Promise<ExpenseDto | null> => {
    // ⚠️ `localDayIso`, never `.slice(0, 10)`: a dépense on the Tunisian 1st is stored
    // `2026-08-31T23:00:00Z`, so slicing looks it up on the 31st of August and finds nothing.
    const day = localDayIso(movement.occurredOn)
    const page = await expensesApi.listPaged({ fromDay: day, toDay: day })
    return page.items.find((e) => e.id === movement.targetId) ?? null
  }

  const open = async (next: "edit" | "delete") => {
    setBusy(true)
    try {
      const row = await load()
      if (!row) {
        // Not an empty form: the dépense moved or was already removed, and an « introuvable » that opens a
        // blank « Nouvelle dépense » would invite the user to re-create the row they were trying to correct.
        toast.error("Cette dépense n'existe plus. Rechargez la caisse.")
        onChanged()
        return
      }
      setExpense(row)
      if (next === "edit") setEditOpen(true)
      else setDeleteOpen(true)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Impossible d'ouvrir cette dépense.")
    } finally {
      setBusy(false)
    }
  }

  const confirmDelete = async () => {
    if (!expense) return
    try {
      setDeleting(true)
      await expensesApi.delete(expense.id)
      toast.success("Dépense supprimée")
      setDeleteOpen(false)
      setExpense(null)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression de la dépense")
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      {/*
        ⚠️ The two dialogs are SIBLINGS of the menu, never children of `DropdownMenuContent`. Radix unmounts the
        content when the menu closes, and `onSelect` closes it — so a dialog rendered inside was destroyed in the
        same tick it was asked to open, and the menu item did nothing at all. Cost one round of manual testing.
      */}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            disabled={busy}
            aria-label={`Modifier ou supprimer ${movement.label}`}
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <MoreHorizontal className="h-4 w-4" />}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onSelect={() => void open("edit")}>Modifier la dépense</DropdownMenuItem>
          {canDelete && (
            <DropdownMenuItem
              className="text-destructive focus:text-destructive"
              onSelect={() => void open("delete")}
            >
              Supprimer la dépense
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <ExpenseFormDialog
        open={editOpen}
        onOpenChange={setEditOpen}
        editingExpense={expense}
        // Never reached: the form only reads `defaultDay` when `editingExpense` is null, and this surface
        // always has one. It is the dépense's own day so a future guard cannot land on the wrong month.
        defaultDay={localDayIso(movement.occurredOn)}
        onSaved={onChanged}
      />

      <AlertDialog open={deleteOpen} onOpenChange={(next) => { if (!deleting) setDeleteOpen(next) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            {/* § 13 — a destructive confirm names what it destroys; a statement page can hold a dozen dépenses. */}
            <AlertDialogTitle>
              Supprimer la dépense
              {expense ? ` ${quoteFr(expense.category)} (${formatDT(expense.amount)}) ` : " "}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Elle disparaîtra de la caisse et de l&apos;extrait, et le total des dépenses baissera d&apos;autant.
              Cette action est irréversible.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void confirmDelete()
              }}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
