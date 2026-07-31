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
import { Stethoscope, Pencil, Trash2, Clock, Plus, Coins, ListPlus, Loader2, Boxes, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { ProcedureTypeMaterialsDialog } from "@/components/procedure-type-materials-dialog"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDT } from "@/lib/format"
import { useSession } from "@/lib/auth/session"
import { toast } from "sonner"

interface ProcedureTypesTableProps {
  onEdit: (procedure: ProcedureTypeDto) => void
  onAdd: () => void
}

export function ProcedureTypesTable({ onEdit, onAdd }: ProcedureTypesTableProps) {
  // Procedure-type WRITES became admin-only (security-hardening AC-7.2) — prices here feed straight into what
  // a patient is charged. Reads stay open to all staff, which is why the page itself is not blocked the way
  // the three admin-only catalog pages are: everyone still needs to see the catalogue. Hiding the write
  // controls rather than letting a non-admin press them and collect an unexplained 403 (AC-7.4).
  const { user } = useSession()
  const isAdmin = user?.role === "admin"
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [procedureToDelete, setProcedureToDelete] = useState<ProcedureTypeDto | null>(null)
  const [search, setSearch] = useState("")
  // Bumped to refetch the current page after a create / edit / delete / seed.
  const [reloadToken, setReloadToken] = useState(0)
  const [deleting, setDeleting] = useState(false)
  const [seeding, setSeeding] = useState(false)
  // AC-P4.14 — the act whose material list is being edited (« Consommables »), or null.
  const [materialsTarget, setMaterialsTarget] = useState<ProcedureTypeDto | null>(null)

  // Only active procedures. Search, ordering and paging are all server-side.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      procedureTypesApi.listPaged({ page, pageSize, search, includeInactive: false }),
    [],
  )

  const {
    items: procedures,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<ProcedureTypeDto>({ fetchPage, search, refreshKey: reloadToken })

  const loadProcedures = async () => setReloadToken((t) => t + 1)

  const handleDelete = (procedure: ProcedureTypeDto) => {
    setProcedureToDelete(procedure)
    setDeleteDialogOpen(true)
  }

  // Seeds the clinic menu with the common Tunisian dental procedures (idempotent — skips existing names).
  const handleLoadDefaults = async () => {
    try {
      setSeeding(true)
      const { added } = await procedureTypesApi.initializeDefaults()
      if (added > 0) {
        toast.success(`${added} acte(s) ajouté(s)`)
      } else {
        toast.info("Aucun nouvel acte à ajouter.")
      }
      await loadProcedures() // Reload the list in place.
    } catch (err) {
      showErrorToast(err, "Échec du chargement des actes courants.")
    } finally {
      setSeeding(false)
    }
  }

  const confirmDelete = async () => {
    if (!procedureToDelete) return

    try {
      setDeleting(true)
      await procedureTypesApi.delete(procedureToDelete.id)
      await loadProcedures() // Reload list
      setDeleteDialogOpen(false)
      setProcedureToDelete(null)
    } catch (err) {
      showErrorToast(err, "Échec de la suppression du type d'acte.")
    } finally {
      setDeleting(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <CardContent className="p-6">
          <p className="text-center text-muted-foreground">Chargement des types d'actes…</p>
        </CardContent>
      </Card>
    )
  }

  return (
    <>
      <Card>
        <CardHeader>
          {/* `flex-wrap`, and the buttons stack below `sm:`. The row held « Charger les actes courants » and
              « Ajouter un type d'acte » — ~380px of button next to the title — with no wrap, so on a phone
              they ran straight out of the card. Splitting a 288px row between them would leave ~140px each,
              which is narrower than either label, so they take a full row apiece instead. */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <CardTitle className="flex min-w-0 items-center gap-2">
              <Stethoscope className="h-5 w-5 shrink-0" />
              Types d'actes
              <Badge variant="secondary" className="ml-2">
                {pageInfo.totalCount} {pageInfo.totalCount === 1 ? "type" : "types"}
              </Badge>
            </CardTitle>
            {isAdmin && (
              <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row sm:items-center">
                <Button
                  onClick={handleLoadDefaults}
                  variant="outline"
                  size="sm"
                  className="w-full gap-2 sm:w-auto"
                  disabled={seeding}
                >
                  {seeding ? <Loader2 className="h-4 w-4 animate-spin" /> : <ListPlus className="h-4 w-4" />}
                  {seeding ? "Chargement…" : "Charger les actes courants"}
                </Button>
                <Button onClick={onAdd} size="sm" className="w-full gap-2 sm:w-auto">
                  <Plus className="h-4 w-4" />
                  Ajouter un type d'acte
                </Button>
              </div>
            )}
          </div>
        </CardHeader>
        <CardContent>
          {error && (
            <div className="mb-4 rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">
              {error}
            </div>
          )}
          <div className="mb-4">
            <Label htmlFor="procedure-types-search" className="sr-only">
              Rechercher un type d&apos;acte
            </Label>
            <Input
              id="procedure-types-search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Rechercher un type d&apos;acte (nom, description)…"
            />
          </div>
          <div className={`overflow-x-auto${refreshing ? " opacity-60 transition-opacity" : ""}`}>
            {/* The colour column is decoration, not data — it becomes the card's accent bar rather than a
                field labelled « Couleur » whose value is a swatch. */}
            <CardList
              className={CARDS_ONLY}
              ariaLabel="Types de procédures"
              items={procedures}
              getKey={(p) => p.id}
              title={(p) => p.name}
              subtitle={(p) => p.description}
              accent={(p) => p.colorHex}
              fields={(p) => [
                { label: "Durée", value: `${p.defaultDurationMinutes} min` },
                {
                  label: "Coût",
                  value: p.defaultCost != null && p.defaultCost > 0 ? formatDT(p.defaultCost) : null,
                },
                {
                  label: "Consommables",
                  value:
                    p.materials.length > 0
                      ? `${p.materials.length} article${p.materials.length === 1 ? "" : "s"}`
                      : null,
                },
              ]}
              actions={
                isAdmin
                  ? (p) => (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" aria-label={`Actions pour ${p.name}`}>
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem onSelect={() => onEdit(p)}>Modifier</DropdownMenuItem>
                          <DropdownMenuItem onSelect={() => setMaterialsTarget(p)}>Consommables</DropdownMenuItem>
                          <DropdownMenuItem
                            className="text-destructive focus:text-destructive"
                            onSelect={() => handleDelete(p)}
                          >
                            Supprimer
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )
                  : undefined
              }
              empty={
                isAdmin
                  ? "Aucun type d'acte défini"
                  : "Aucun type d'acte défini. Demandez à un administrateur d'en ajouter."
              }
            />
            <Table containerClassName={TABLE_ONLY}>
              <TableHeader>
                <TableRow>
                  <TableHead>Couleur</TableHead>
                  <TableHead>Nom de l'acte</TableHead>
                  <TableHead>Durée</TableHead>
                  <TableHead>Coût par défaut</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead>Consommables</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {procedures.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={7} className="h-24 text-center">
                      <p className="text-muted-foreground">
                        {isAdmin
                          ? "Aucun type d'acte défini"
                          : "Aucun type d'acte défini. Demandez à un administrateur d'en ajouter."}
                      </p>
                      {isAdmin && (
                        <Button onClick={onAdd} variant="outline" size="sm" className="mt-2 gap-2">
                          <Plus className="h-4 w-4" />
                          Ajouter votre premier type d'acte
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ) : (
                  procedures.map((procedure) => (
                    <TableRow key={procedure.id}>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          {/* Color circle indicator */}
                          <div
                            className="h-6 w-6 rounded-full border-2 border-border"
                            style={{ backgroundColor: procedure.colorHex }}
                            title={procedure.colorHex}
                          />
                          {/* Preview badge */}
                          <Badge
                            variant="outline"
                            className="border-2"
                            style={{
                              borderColor: procedure.colorHex,
                              color: procedure.colorHex,
                              backgroundColor: `${procedure.colorHex}10`,
                            }}
                          >
                            Aperçu
                          </Badge>
                        </div>
                      </TableCell>
                      <TableCell className="font-medium text-foreground">{procedure.name}</TableCell>
                      <TableCell>
                        <div className="flex items-center gap-2 text-muted-foreground">
                          <Clock className="h-4 w-4" />
                          <span>{procedure.defaultDurationMinutes} min</span>
                        </div>
                      </TableCell>
                      <TableCell>
                        {procedure.defaultCost != null && procedure.defaultCost > 0 ? (
                          <div className="flex items-center gap-2 text-muted-foreground">
                            <Coins className="h-4 w-4" />
                            <span>{formatDT(procedure.defaultCost)}</span>
                          </div>
                        ) : (
                          <span className="text-muted-foreground">-</span>
                        )}
                      </TableCell>
                      <TableCell className="text-muted-foreground">{procedure.description || "-"}</TableCell>
                      <TableCell>
                        {/* AC-P4.14 — an act that draws down stock says so in the catalogue, so the list is
                            discoverable rather than hidden behind a dialog nobody knows to open. */}
                        {procedure.materials.length > 0 ? (
                          <div className="flex items-center gap-2 text-muted-foreground">
                            <Boxes className="h-4 w-4" aria-hidden="true" />
                            <span>
                              {procedure.materials.length} article{procedure.materials.length === 1 ? "" : "s"}
                            </span>
                          </div>
                        ) : (
                          <span className="text-muted-foreground">-</span>
                        )}
                      </TableCell>
                      <TableCell className="text-right">
                        {isAdmin && (
                          <div className="flex justify-end gap-2">
                            <Button variant="ghost" size="sm" onClick={() => onEdit(procedure)} className="h-8 gap-1">
                              <Pencil className="h-3 w-3" />
                              Modifier
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setMaterialsTarget(procedure)}
                              className="h-8 gap-1"
                            >
                              <Boxes className="h-3 w-3" />
                              Consommables
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleDelete(procedure)}
                              className="h-8 gap-1 text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-3 w-3" />
                              Supprimer
                            </Button>
                          </div>
                        )}
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
              label={["type d'acte", "types d'actes"]}
            />
          </div>
        </CardContent>
      </Card>

      {/* AC-P4.14 — material-list editor for one act. */}
      <ProcedureTypeMaterialsDialog
        procedureType={materialsTarget}
        onOpenChange={(open) => { if (!open) setMaterialsTarget(null) }}
        onSaved={loadProcedures}
      />

      {/* Delete Confirmation Dialog */}
      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Êtes-vous sûr ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cela va {procedureToDelete?.isActive ? "désactiver" : "supprimer définitivement"} le type d'acte{" "}
              <span className="font-semibold">{procedureToDelete?.name}</span>.
              {procedureToDelete?.isActive && " S'il est utilisé par de futurs rendez-vous, il sera archivé au lieu d'être supprimé."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
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


