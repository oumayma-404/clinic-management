"use client"

import { useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { MoreHorizontal, Repeat } from "lucide-react"
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
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ApiError } from "@/lib/api/client"
import { expensesApi } from "@/lib/api/expenses"
import type { RecurringExpenseDto } from "@/lib/api/types"
import { formatDT, quoteFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { methodLabel } from "./expense-fields"
import { MonthlyExpenseDialog } from "./monthly-expense-dialog"

/** « octobre 2026 » from an `AAAA-MM` key. The server sends the key; the month's name is a presentation choice. */
const monthLabel = (monthKey: string): string => {
  const parsed = parseISO(`${monthKey}-01`)
  return Number.isNaN(parsed.getTime()) ? monthKey : format(parsed, "MMMM yyyy", { locale: fr })
}

/** The « Finances » hue, as this card header's icon chip — the twin of la caisse's own two headers. */
const MONEY_HEADER_CHIP =
  `flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.money)}`

/**
 * « Dépenses mensuelles » — the standing commitments that post themselves each month.
 *
 * <p>⚠️ **Rendered only when there is at least one.** A permanently empty card teaching a feature nobody in this
 * cabinet uses is the noise that makes a screen feel complicated, and the switch inside « Nouvelle dépense » is
 * where the feature is discovered. So this appears the moment it has something to say and not before.</p>
 *
 * <p>It sits **above** les dépenses because it explains part of them: a reader who meets « Loyer 800,000 » in the
 * table below and wonders who typed it has the answer one card up, and every row it posted is badged « mensuelle »
 * down there.</p>
 *
 * <p>⚠️ **It is not period data**, unlike everything else on this screen — no window, no pager. « Les dépenses
 * mensuelles du 3 août » is not a question, and a page two of standing commitments would hide one behind a pager
 * nobody would think to look for.</p>
 */
export function MonthlyExpenseList({
  series,
  onChanged,
}: {
  series: RecurringExpenseDto[]
  onChanged: () => void | Promise<void>
}) {
  const [editing, setEditing] = useState<RecurringExpenseDto | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [stopping, setStopping] = useState<RecurringExpenseDto | null>(null)
  const [stopOpen, setStopOpen] = useState(false)
  const [saving, setSaving] = useState(false)

  if (series.length === 0) return null

  const openEdit = (row: RecurringExpenseDto) => {
    setEditing(row)
    setEditOpen(true)
  }

  const openStop = (row: RecurringExpenseDto) => {
    setStopping(row)
    setStopOpen(true)
  }

  const confirmStop = async () => {
    if (!stopping) return
    try {
      setSaving(true)
      await expensesApi.stopRecurring(stopping.id)
      toast.success("Dépense mensuelle arrêtée")
      setStopOpen(false)
      setStopping(null)
      await onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'arrêt de la dépense mensuelle")
    } finally {
      setSaving(false)
    }
  }

  const rowActions = (row: RecurringExpenseDto) => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={`Actions pour la dépense mensuelle ${row.category}`}>
          <MoreHorizontal className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onSelect={() => openEdit(row)}>Modifier</DropdownMenuItem>
        {/* Not `text-destructive`: arrêter destroys nothing — every month already recorded stays. The confirm
            carries the weight instead, and a red item here would read as « supprimer les dépenses passées ». */}
        <DropdownMenuItem onSelect={() => openStop(row)}>Arrêter</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex min-w-0 flex-wrap items-center gap-2.5 leading-snug">
            <span aria-hidden="true" className={MONEY_HEADER_CHIP}>
              <Repeat className="size-4" strokeWidth={1.75} />
            </span>
            Dépenses mensuelles
            <Badge variant="secondary">{series.length}</Badge>
          </CardTitle>
          <CardDescription>
            Enregistrées automatiquement chaque mois, sans rien ressaisir. Modifier change les mois à venir&nbsp;;
            arrêter ne touche à aucun mois déjà enregistré.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {/*
            ⚠️ The **`lg:`** pair, not `md:`, and `card-list.tsx`'s own note says a six-column list should use
            `md:` — so this is a measured exception rather than a copy of the table below.

            Measured at 820 px (iPad portrait, the device this app is used on most): the six columns come to
            530 px at their min-content in the 451 px an `md:` table gets there, and what the 79 px pushes out
            of view is the **Actions** column — the ⋯ menu holding « Modifier » and « Arrêter ». The heuristic
            assumes six columns fit; two of these are French phrases (« le 2 du mois », « octobre 2026 ») and
            they do not. Hidden behind a sideways drag, the only two things a reader comes to this card to DO
            are the two things they cannot see, which is § 0. On `lg:` the tablet gets the card list — every
            field labelled, the menu in view — and from 1024 px up the table fits with room to spare.
          */}
          <div className="overflow-x-auto">
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Dépenses mensuelles"
              items={series}
              getKey={(s) => s.id}
              title={(s) => s.category}
              subtitle={(s) => s.description?.trim()}
              fields={(s) => [
                { label: "Montant", value: <span className="font-medium">{formatDT(s.amount)}</span> },
                { label: "Échéance", value: `le ${s.dayOfMonth} du mois` },
                { label: "Prochaine", value: <span className="capitalize">{monthLabel(s.nextMonth)}</span> },
                { label: "Mode", value: methodLabel(s.method) },
              ]}
              actions={rowActions}
            />
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead>Catégorie</TableHead>
                  <TableHead className="text-right">Montant</TableHead>
                  <TableHead>Échéance</TableHead>
                  <TableHead>Prochaine</TableHead>
                  <TableHead>Mode</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {series.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell>
                      <Badge variant="outline">{row.category}</Badge>
                      {row.description?.trim() && (
                        <span className="mt-1 block text-xs text-muted-foreground">{row.description}</span>
                      )}
                    </TableCell>
                    <TableCell numeric className="font-medium text-foreground">
                      {formatDT(row.amount)}
                    </TableCell>
                    {/* No `whitespace-nowrap` on either: § 6 sanctions it for an ATOMIC value (a `d MMM yyyy`,
                        a phone, an invoice number) and these are a phrase and a month name. Held on one line
                        they made this 6-column table measure 623 px inside the 451 px an iPad portrait gives
                        it — 172 px of it unreachable without a sideways drag. Wrapping, it fits. */}
                    <TableCell className="text-muted-foreground">le {row.dayOfMonth} du mois</TableCell>
                    <TableCell className="capitalize text-muted-foreground">{monthLabel(row.nextMonth)}</TableCell>
                    <TableCell className="text-muted-foreground">{methodLabel(row.method)}</TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end">{rowActions(row)}</div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <MonthlyExpenseDialog
        open={editOpen}
        onOpenChange={setEditOpen}
        series={editing}
        onSaved={onChanged}
      />

      <AlertDialog open={stopOpen} onOpenChange={setStopOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            {/* § 13 — a consequential confirm NAMES what it acts on: a cabinet can have four of these open. */}
            <AlertDialogTitle>
              Arrêter la dépense mensuelle
              {stopping ? ` ${quoteFr(stopping.category)} (${formatDT(stopping.amount)}) ` : " "}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Elle ne sera plus enregistrée les mois suivants. Les dépenses déjà enregistrées restent dans la
              caisse&nbsp;: aucun montant, aucun total ne change.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void confirmStop()
              }}
              disabled={saving}
            >
              {saving ? "Arrêt…" : "Arrêter"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
