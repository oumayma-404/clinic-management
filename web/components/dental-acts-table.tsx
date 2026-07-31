"use client"

import { useCallback, useState } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { usePagedList } from "@/lib/hooks/use-paged-list"
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
import { ClipboardList, Pencil, Trash2, Plus, AlertTriangle, CheckCircle2, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { dentalActsApi } from "@/lib/api/dental-acts"
import type { DentalActDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDT } from "@/lib/format"
import { toast } from "sonner"

interface DentalActsTableProps {
  onEdit: (act: DentalActDto) => void
  onAdd: () => void
  onChanged: () => void
  // Bumped by the parent (after any write or realtime signal) to trigger an in-place refetch.
  reloadToken?: number
}

export function DentalActsTable({ onEdit, onAdd, onChanged, reloadToken }: DentalActsTableProps) {
  const [search, setSearch] = useState("")
  const [actToDelete, setActToDelete] = useState<DentalActDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [confirming, setConfirming] = useState(false)

  // Admin screen: include deactivated rows too. Paging, ordering and the free-text search all run
  // server-side — the catalog is the one list that really does grow without bound, and a search that
  // only saw the current page would miss the act being looked for most of the time.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      dentalActsApi.listPaged({ page, pageSize, search, includeInactive: true }),
    [],
  )

  const {
    items: acts,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<DentalActDto>({ fetchPage, search, refreshKey: reloadToken })

  const confirmDelete = async () => {
    if (!actToDelete) return
    try {
      setDeleting(true)
      await dentalActsApi.deactivate(actToDelete.id)
      toast.success(`Acte « ${actToDelete.codeActe} » désactivé.`)
      setActToDelete(null)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la désactivation.")
    } finally {
      setDeleting(false)
    }
  }

  const handleConfirmData = async () => {
    try {
      setConfirming(true)
      await dentalActsApi.confirmData()
      toast.success("Données du catalogue confirmées.")
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la confirmation.")
    } finally {
      setConfirming(false)
    }
  }

  const hasProvisional = acts.some((a) => a.isProvisional)

  if (loading) {
    return (
      <Card>
        <CardContent className="p-6">
          <p className="text-center text-muted-foreground">Chargement du catalogue…</p>
        </CardContent>
      </Card>
    )
  }

  return (
    <>
      {hasProvisional && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 shrink-0" />
            <span>
              Données provisoires « à vérifier ». Confirmez-les avec la nomenclature en vigueur avant toute
              utilisation clinique. Rien n'est bloqué en attendant.
            </span>
          </div>
          <Button size="sm" variant="outline" onClick={handleConfirmData} disabled={confirming} className="gap-2">
            <CheckCircle2 className="h-4 w-4" />
            {confirming ? "Confirmation…" : "Confirmer les données"}
          </Button>
        </div>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <ClipboardList className="h-5 w-5" />
              Catalogue des actes dentaires
              <Badge variant="secondary" className="ml-2">
                {pageInfo.totalCount} {pageInfo.totalCount === 1 ? "acte" : "actes"}
              </Badge>
            </CardTitle>
            <Button onClick={onAdd} size="sm" className="gap-2">
              <Plus className="h-4 w-4" />
              Ajouter un acte
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {error && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
              {error}
            </div>
          )}
          <div className="mb-4">
            <Label htmlFor="dental-acts-search" className="sr-only">
              Rechercher un acte (code, désignation)…
            </Label>
            <Input
              id="dental-acts-search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Rechercher un acte (code, désignation)…"
            />
          </div>
          <div className={`overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
            {/* Same shape as the CNAM nomenclature, plus a tarif. `coefficient` and `defaultFee` are passed
                raw — the primitive drops a nullish value, so the « — » placeholders the table needs to keep
                its columns aligned simply do not appear on a card (AC-17). */}
            <CardList
              className={CARDS_ONLY}
              ariaLabel="Catalogue d'actes dentaires"
              items={acts}
              getKey={(a) => a.id}
              title={(a) => a.designationFr}
              subtitle={(a) => <span className="font-mono">{a.codeActe}</span>}
              muted={(a) => !a.isActive}
              status={(a) => (
                <>
                  {!a.isActive && <Badge variant="secondary">Inactif</Badge>}
                  {a.requiresAccordPrealable && (
                    <Badge variant="outline" className="border-primary/40 text-primary">
                      Accord préalable
                    </Badge>
                  )}
                  {a.isProvisional && (
                    <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                      À vérifier
                    </Badge>
                  )}
                </>
              )}
              fields={(a) => [
                { label: "Tarif", value: a.defaultFee != null ? formatDT(a.defaultFee) : null },
                { label: "Lettre clé", value: <Badge variant="outline">{a.lettreCle}</Badge> },
                { label: "Coefficient", value: a.coefficient },
                { label: "Catégorie", value: a.category },
              ]}
              actions={(a) => (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" aria-label={`Actions pour ${a.designationFr}`}>
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onSelect={() => onEdit(a)}>Modifier</DropdownMenuItem>
                    {a.isActive && (
                      <DropdownMenuItem
                        className="text-destructive focus:text-destructive"
                        onSelect={() => setActToDelete(a)}
                      >
                        Désactiver
                      </DropdownMenuItem>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
              empty="Aucun acte dans le catalogue"
            />
            <Table containerClassName={TABLE_ONLY}>
              <TableHeader>
                <TableRow>
                  <TableHead>Code acte</TableHead>
                  <TableHead>Désignation</TableHead>
                  <TableHead>Lettre clé</TableHead>
                  <TableHead>Coefficient</TableHead>
                  <TableHead>Catégorie</TableHead>
                  <TableHead className="text-right">Tarif</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {acts.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={8} className="h-24 text-center">
                      <p className="text-muted-foreground">Aucun acte dans le catalogue</p>
                    </TableCell>
                  </TableRow>
                ) : (
                  acts.map((act) => (
                    <TableRow key={act.id} className={act.isActive ? "" : "opacity-50"}>
                      <TableCell className="font-mono text-sm font-medium text-foreground">{act.codeActe}</TableCell>
                      <TableCell className="text-foreground">{act.designationFr}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{act.lettreCle}</Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{act.coefficient ?? "—"}</TableCell>
                      <TableCell className="text-muted-foreground">{act.category}</TableCell>
                      <TableCell className="text-right text-muted-foreground">
                        {act.defaultFee != null ? formatDT(act.defaultFee) : "—"}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {!act.isActive && <Badge variant="secondary">Inactif</Badge>}
                          {act.requiresAccordPrealable && (
                            <Badge variant="outline" className="border-primary/40 text-primary">
                              Accord préalable
                            </Badge>
                          )}
                          {act.isProvisional && (
                            <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                              À vérifier
                            </Badge>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="ghost" size="sm" onClick={() => onEdit(act)} className="h-8 gap-1">
                            <Pencil className="h-3 w-3" />
                            Modifier
                          </Button>
                          {act.isActive && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setActToDelete(act)}
                              className="h-8 gap-1 text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-3 w-3" />
                              Désactiver
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
            <DataTablePagination
              page={pageInfo}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              loading={refreshing}
              label={["acte", "actes"]}
            />
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={actToDelete !== null} onOpenChange={(open) => !open && setActToDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Désactiver cet acte ?</AlertDialogTitle>
            <AlertDialogDescription>
              L'acte <span className="font-semibold">{actToDelete?.codeActe}</span> sera désactivé et
              n'apparaîtra plus dans le sélecteur d'actes des plans de traitement. Les plans déjà enregistrés
              ne sont pas modifiés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Désactivation…" : "Désactiver"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
