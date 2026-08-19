"use client"

import { useCallback, useEffect, useState } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
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
/*
  `_LG`, not the plain `md:` pair: the Catégorie column takes this table to **eight** columns, every cell
  `whitespace-nowrap`. An iPad portrait is 820px and therefore already `md:`, so it would get the desktop table
  *and* the 256px rail — ~532px for eight columns. `web/CLAUDE.md` sets the hinge at roughly eight.
*/
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
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

/** Sentinel for « toutes les catégories » — Radix Select forbids an empty-string item value. */
const ALL_CATEGORIES = "__all__"

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
  /** `ALL_CATEGORIES` rather than `""`, because Radix Select forbids an empty-string item value. */
  const [category, setCategory] = useState(ALL_CATEGORIES)
  /** Filter options: the canonical disciplines plus whatever this clinic uses (served, not hardcoded). */
  const [categoryOptions, setCategoryOptions] = useState<string[]>([])
  // Bumped to refetch the current page after a create / edit / delete / seed.
  const [reloadToken, setReloadToken] = useState(0)
  const [deleting, setDeleting] = useState(false)
  const [seeding, setSeeding] = useState(false)
  // AC-P4.14 — the act whose material list is being edited (« Consommables »), or null.
  const [materialsTarget, setMaterialsTarget] = useState<ProcedureTypeDto | null>(null)

  // Only active procedures. Search, the category filter, ordering and paging are ALL server-side — filtering an
  // already-cut page in the browser would shrink pages unpredictably (« Endodontie » showing 3 rows of 25).
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      procedureTypesApi.listPaged({
        page,
        pageSize,
        search,
        includeInactive: false,
        category: category === ALL_CATEGORIES ? undefined : category,
      }),
    [category],
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
  } = usePagedList<ProcedureTypeDto>({
    fetchPage,
    search,
    // Changing the catégorie returns to page 1 (AC-22).
    filters: [category],
    refreshKey: reloadToken,
  })

  const loadProcedures = async () => setReloadToken((t) => t + 1)

  // Filter options, refetched whenever the list is: editing an act can introduce or retire a category, and a
  // filter offering a category no act carries — or missing one that several do — is worse than no filter.
  useEffect(() => {
    let active = true
    procedureTypesApi
      .getCategories()
      .then((categories) => {
        if (active) setCategoryOptions(categories)
      })
      // Silent: the filter simply stays at « Toutes les catégories » and the table is unaffected. The read that
      // matters here is the list itself, which has its own error banner.
      .catch(() => {
        if (active) setCategoryOptions([])
      })
    return () => {
      active = false
    }
  }, [reloadToken])

  /**
   * The two empty facts kept apart (finding #4). The table has a live search box, so one message told an admin
   * who mistyped that the clinic has no acts at all — and the « Ajouter votre premier type d'acte » button that
   * sat under it invited a duplicate of the act the search had simply failed to match. The filtered branch
   * therefore offers « Effacer la recherche » and no create action.
   */
  // A category filter is the second way to narrow this list, so it counts as "filtered" for the same reason the
  // search does: an empty « Orthodontie » must not tell a clinic with 40 acts that it has none, nor offer to
  // create the first one.
  const isFiltered = isSearching || category !== ALL_CATEGORIES

  const renderEmpty = (size: "default" | "compact") =>
    isFiltered ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">
          {isSearching
            ? "Aucun type d'acte ne correspond à votre recherche"
            : `Aucun type d'acte dans « ${category} »`}
        </p>
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            setSearch("")
            setCategory(ALL_CATEGORIES)
          }}
        >
          Effacer les filtres
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={Stethoscope}
        size={size}
        title="Aucun type d'acte défini"
        description={
          isAdmin
            ? "Les actes de ce catalogue donnent à l'agenda sa couleur et sa durée, et préremplissent les devis et les fiches de soins. « Charger les actes courants » installe les actes tunisiens usuels en une fois."
            : "Ce catalogue donne à l'agenda ses couleurs et ses durées. Demandez à un administrateur d'y ajouter vos actes."
        }
        action={
          isAdmin ? (
            <Button onClick={onAdd} className="gap-2">
              <Plus className="h-4 w-4" />
              Ajouter un type d&apos;acte
            </Button>
          ) : undefined
        }
        secondaryAction={
          isAdmin ? (
            <Button variant="outline" onClick={handleLoadDefaults} disabled={seeding} className="gap-2">
              {seeding ? <Loader2 className="h-4 w-4 animate-spin" /> : <ListPlus className="h-4 w-4" />}
              Charger les actes courants
            </Button>
          ) : undefined
        }
      />
    )

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
          {/* The shared primitive, on the theme's own destructive family — this was one of the ~18 places that
              copied `border-red-200 bg-red-50 … dark:` by hand and therefore maintained dark mode itself. The
              action turns a dead end into a retry: without it the only way out of a failed read is a browser
              reload, which a non-technical user on an installed PWA has no gesture for. */}
          <FormErrorBanner
            className="mb-4"
            message={error}
            action={{ label: "Réessayer", onClick: () => setReloadToken((t) => t + 1) }}
          />
          {/* Stacks below `sm:` — a 288px card cannot hold a search box and a ~200px Select side by side. */}
          <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center">
            <div className="min-w-0 flex-1">
              <Label htmlFor="procedure-types-search" className="sr-only">
                Rechercher un type d&apos;acte
              </Label>
              <Input
                id="procedure-types-search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Rechercher un type d&apos;acte (nom, catégorie, description)…"
              />
            </div>
            <div className="sm:w-56">
              <Label htmlFor="procedure-types-category" className="sr-only">
                Filtrer par catégorie
              </Label>
              <Select value={category} onValueChange={setCategory}>
                <SelectTrigger id="procedure-types-category" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_CATEGORIES}>Toutes les catégories</SelectItem>
                  {categoryOptions.map((option) => (
                    <SelectItem key={option} value={option}>
                      {option}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          {/* No `overflow-x-auto` here: `ui/table.tsx` already wraps its own table in one, so this was a second
              horizontal scroller nested around the first — the wrapper now carries only the refetch dimming. */}
          <div className={refreshing ? "opacity-60 transition-opacity" : undefined}>
            {/* The colour column is decoration, not data — it becomes the card's accent bar rather than a
                field labelled « Couleur » whose value is a swatch. */}
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Types de procédures"
              items={procedures}
              getKey={(p) => p.id}
              title={(p) => p.name}
              subtitle={(p) => p.description}
              accent={(p) => p.colorHex}
              // The discipline goes in the status slot, not a field: on a phone it is the fastest way to tell two
              // similarly-named acts apart, and it belongs at the top of the card with the name rather than
              // fourth in a list of values. `CardList` drops an empty status, so an unfiled act shows nothing.
              status={(p) =>
                p.category ? (
                  // ⚠️ `min(10rem, 100%)` and not a bare `max-w-[10rem]`: a category is clinic-authored, the
                  // badge is `shrink-0`, and at 320 px the card gives this row ~149 px — so a flat 160 px cap
                  // painted « Chirurgie/Extraction » out through the card's right edge. The 10rem cap is still
                  // what limits it on a wide screen.
                  <Badge variant="secondary" className="max-w-[min(10rem,100%)] truncate text-xs">
                    {p.category}
                  </Badge>
                ) : null
              }
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
              empty={renderEmpty("compact")}
            />
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead>Couleur</TableHead>
                  <TableHead>Nom de l'acte</TableHead>
                  <TableHead>Catégorie</TableHead>
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
                    <TableCell colSpan={8}>{renderEmpty("default")}</TableCell>
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
                      {/* Rows arrive ordered by catégorie then nom, so the badges read as blocks down the column
                          without needing section headings — which a *paged* list cannot honestly draw anyway
                          (a discipline straddling a page boundary would print its heading twice). */}
                      <TableCell>
                        {procedure.category ? (
                          <Badge variant="secondary" className="text-xs font-normal">
                            {procedure.category}
                          </Badge>
                        ) : (
                          <span className="text-muted-foreground">-</span>
                        )}
                      </TableCell>
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
            {/* The title names the object rather than asking « Êtes-vous sûr ? », which is the same sentence
                this app uses to delete a patient. */}
            <AlertDialogTitle>Supprimer ce type d&apos;acte ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cela va {procedureToDelete?.isActive ? "désactiver" : "supprimer définitivement"} le type d'acte{" "}
              <span className="font-semibold">{procedureToDelete?.name}</span>.
              {procedureToDelete?.isActive && " S'il est utilisé par de futurs rendez-vous, il sera archivé au lieu d'être supprimé."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            {/* The button says what the branch above already computed. The body explained that an act in use is
                « archivé au lieu d'être supprimé » and the button then read « Supprimer » regardless — so the
                two halves of the same dialog described different outcomes, and the one the user actually presses
                was the one that was wrong. */}
            <AlertDialogAction
              onClick={confirmDelete}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting
                ? procedureToDelete?.isActive
                  ? "Désactivation…"
                  : "Suppression…"
                : procedureToDelete?.isActive
                  ? "Désactiver"
                  : "Supprimer définitivement"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}


