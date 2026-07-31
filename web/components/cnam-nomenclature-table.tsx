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
import { cnamNomenclatureApi } from "@/lib/api/cnam-nomenclature"
import type { CnamNomenclatureEntryDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"

interface CnamNomenclatureTableProps {
  onEdit: (entry: CnamNomenclatureEntryDto) => void
  onAdd: () => void
  onChanged: () => void
  // Bumped by the parent (after any catalog/VLC write or realtime signal) to trigger an in-place refetch —
  // instead of remounting via `key`, which discarded in-progress edits and could setState after unmount.
  reloadToken?: number
}

export function CnamNomenclatureTable({ onEdit, onAdd, onChanged, reloadToken }: CnamNomenclatureTableProps) {
  const [search, setSearch] = useState("")
  const [entryToDelete, setEntryToDelete] = useState<CnamNomenclatureEntryDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [confirming, setConfirming] = useState(false)

  // Admin screen: include deactivated rows too. Paging, ordering and the free-text search all run
  // server-side — the catalog is the one list that really does grow without bound, and a search that
  // only saw the current page would miss the act being looked for most of the time.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      cnamNomenclatureApi.listPaged({ page, pageSize, search, includeInactive: true }),
    [],
  )

  const {
    items: entries,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<CnamNomenclatureEntryDto>({ fetchPage, search, refreshKey: reloadToken })

  const confirmDelete = async () => {
    if (!entryToDelete) return
    try {
      setDeleting(true)
      await cnamNomenclatureApi.deactivate(entryToDelete.id)
      toast.success(`Acte « ${entryToDelete.codeActe} » désactivé.`)
      setEntryToDelete(null)
      onChanged() // parent bumps reloadToken → in-place refetch (both cards), no remount
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la désactivation.")
    } finally {
      setDeleting(false)
    }
  }

  const handleConfirmData = async () => {
    try {
      setConfirming(true)
      await cnamNomenclatureApi.confirmData()
      toast.success("Données CNAM confirmées.")
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la confirmation.")
    } finally {
      setConfirming(false)
    }
  }

  const hasProvisional = entries.some((e) => e.isProvisional)

  if (loading) {
    return (
      <Card>
        <CardContent className="p-6">
          <p className="text-center text-muted-foreground">Chargement de la nomenclature…</p>
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
              Données provisoires « à vérifier ». Confirmez-les avec la convention dentaire CNAM en vigueur
              avant toute utilisation clinique. Rien n'est bloqué en attendant.
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
          {/* flex-wrap + a full-width button below sm:. Title and « Ajouter » together exceed a 288px
              card, and without wrapping the button ran outside the view. */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <CardTitle className="flex min-w-0 items-center gap-2">
              <ClipboardList className="h-5 w-5" />
              Nomenclature CNAM
              <Badge variant="secondary" className="ml-2">
                {pageInfo.totalCount} {pageInfo.totalCount === 1 ? "acte" : "actes"}
              </Badge>
            </CardTitle>
            <Button onClick={onAdd} size="sm" className="w-full gap-2 sm:w-auto">
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
            <Label htmlFor="cnam-search" className="sr-only">
              Rechercher un acte (code, désignation, lettre clé)…
            </Label>
            <Input
              id="cnam-search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Rechercher un acte (code, désignation, lettre clé)…"
            />
          </div>
          <div className={`overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
            {/* Title is the désignation, not the code: `codeActe` is the key you look an act UP by, but the
                name is how you recognise it in a list. The code rides as a mono eyebrow. */}
            <CardList
              className={CARDS_ONLY}
              ariaLabel="Nomenclature CNAM"
              items={entries}
              getKey={(e) => e.id}
              title={(e) => e.designationFr}
              subtitle={(e) => <span className="font-mono">{e.codeActe}</span>}
              muted={(e) => !e.isActive}
              status={(e) => (
                <>
                  {!e.isActive && <Badge variant="secondary">Inactif</Badge>}
                  {e.isProvisional && (
                    <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                      À vérifier
                    </Badge>
                  )}
                </>
              )}
              fields={(e) => [
                { label: "Lettre clé", value: <Badge variant="outline">{e.lettreCle}</Badge> },
                { label: "Coefficient", value: e.coefficient },
                { label: "Catégorie", value: e.category },
              ]}
              actions={(e) => (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" aria-label={`Actions pour ${e.designationFr}`}>
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onSelect={() => onEdit(e)}>Modifier</DropdownMenuItem>
                    {e.isActive && (
                      <DropdownMenuItem
                        className="text-destructive focus:text-destructive"
                        onSelect={() => setEntryToDelete(e)}
                      >
                        Désactiver
                      </DropdownMenuItem>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
              empty="Aucun acte dans la nomenclature"
            />
            <Table containerClassName={TABLE_ONLY}>
              <TableHeader>
                <TableRow>
                  <TableHead>Code acte</TableHead>
                  <TableHead>Désignation</TableHead>
                  <TableHead>Lettre clé</TableHead>
                  <TableHead>Coefficient</TableHead>
                  <TableHead>Catégorie</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {entries.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={7} className="h-24 text-center">
                      <p className="text-muted-foreground">Aucun acte dans la nomenclature</p>
                    </TableCell>
                  </TableRow>
                ) : (
                  entries.map((entry) => (
                    <TableRow key={entry.id} className={entry.isActive ? "" : "opacity-50"}>
                      <TableCell className="font-mono text-sm font-medium text-foreground">{entry.codeActe}</TableCell>
                      <TableCell className="text-foreground">{entry.designationFr}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{entry.lettreCle}</Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{entry.coefficient}</TableCell>
                      <TableCell className="text-muted-foreground">{entry.category}</TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {!entry.isActive && <Badge variant="secondary">Inactif</Badge>}
                          {entry.isProvisional && (
                            <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                              À vérifier
                            </Badge>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="ghost" size="sm" onClick={() => onEdit(entry)} className="h-8 gap-1">
                            <Pencil className="h-3 w-3" />
                            Modifier
                          </Button>
                          {entry.isActive && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setEntryToDelete(entry)}
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

      <AlertDialog open={entryToDelete !== null} onOpenChange={(open) => !open && setEntryToDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Désactiver cet acte ?</AlertDialogTitle>
            <AlertDialogDescription>
              L'acte <span className="font-semibold">{entryToDelete?.codeActe}</span> sera désactivé et n'apparaîtra
              plus dans la nomenclature de l'éditeur de bulletin. Les bulletins déjà enregistrés ne sont pas
              modifiés.
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
