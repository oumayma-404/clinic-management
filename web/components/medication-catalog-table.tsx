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
import { Pill, Pencil, Trash2, Plus, AlertTriangle, CheckCircle2, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { medicationsApi } from "@/lib/api/medications"
import type { MedicationDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"

interface MedicationCatalogTableProps {
  onEdit: (medication: MedicationDto) => void
  onAdd: () => void
  onChanged: () => void
  // Bumped by the parent (after any catalog write or realtime signal) to trigger an in-place refetch —
  // instead of remounting via `key`, which discarded in-progress edits and could setState after unmount.
  reloadToken?: number
}

export function MedicationCatalogTable({ onEdit, onAdd, onChanged, reloadToken }: MedicationCatalogTableProps) {
  const [search, setSearch] = useState("")
  const [toDelete, setToDelete] = useState<MedicationDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [confirming, setConfirming] = useState(false)

  // Admin screen: include deactivated rows too. Paging, ordering and the free-text search all run
  // server-side — the catalog is the one list that really does grow without bound, and a search that
  // only saw the current page would miss the act being looked for most of the time.
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      medicationsApi.listPaged({ page, pageSize, search, includeInactive: true }),
    [],
  )

  const {
    items: medications,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<MedicationDto>({ fetchPage, search, refreshKey: reloadToken })

  const confirmDelete = async () => {
    if (!toDelete) return
    try {
      setDeleting(true)
      await medicationsApi.deactivate(toDelete.id)
      toast.success(`Médicament « ${toDelete.brandName} » désactivé.`)
      setToDelete(null)
      onChanged() // parent bumps reloadToken → in-place refetch, no remount
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la désactivation.")
    } finally {
      setDeleting(false)
    }
  }

  const handleConfirmData = async () => {
    try {
      setConfirming(true)
      await medicationsApi.confirmData()
      toast.success("Catalogue des médicaments confirmé.")
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la confirmation.")
    } finally {
      setConfirming(false)
    }
  }

  const hasProvisional = medications.some((m) => m.isProvisional)

  /**
   * The two empty facts kept apart (finding #4). The list has a live search box and carried one message, so a
   * mistyped brand name told the admin the whole médicament catalogue was empty. No « Ajouter » on the filtered
   * branch: the médicament is probably there under another spelling, and adding it again duplicates it in the
   * ordonnance picker.
   */
  const renderEmpty = (size: "default" | "compact") =>
    isSearching ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucun médicament ne correspond à votre recherche</p>
        <Button variant="outline" size="sm" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={Pill}
        size={size}
        title="Aucun médicament dans le catalogue"
        description="Ce catalogue alimente le sélecteur de l'ordonnance : nom commercial, forme, dosage et molécules (DCI)."
        action={
          <Button onClick={onAdd} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter un médicament
          </Button>
        }
      />
    )

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
        /* On the theme's warning family (`--warning-wash` / `--warning-ink`), not `amber-*` literals with a
           hand-maintained `dark:` twin. */
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-warning/40 bg-warning-wash p-3 text-sm text-warning-ink">
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 shrink-0" />
            <span>
              Catalogue provisoire « à vérifier ». Vérifiez les médicaments (noms, molécules, dosages) avant
              toute utilisation clinique. Rien n'est bloqué en attendant.
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
              <Pill className="h-5 w-5" />
              Catalogue des médicaments
              <Badge variant="secondary" className="ml-2">
                {pageInfo.totalCount} {pageInfo.totalCount === 1 ? "médicament" : "médicaments"}
              </Badge>
            </CardTitle>
            <Button onClick={onAdd} size="sm" className="w-full gap-2 sm:w-auto">
              <Plus className="h-4 w-4" />
              Ajouter un médicament
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {/* The shared primitive on the theme's own destructive family, plus a retry: this file carried a
              hand-written `border-red-200 bg-red-50 … dark:` copy, so it maintained dark mode itself and the
              only escape from a failed read was a browser reload. */}
          <FormErrorBanner
            className="mb-4"
            message={error}
            action={{ label: "Réessayer", onClick: onChanged }}
          />
          <div className="mb-4">
            <Label htmlFor="medications-search" className="sr-only">
              Rechercher un médicament (marque, forme, dosage, DCI)…
            </Label>
            <Input
              id="medications-search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Rechercher un médicament (marque, forme, dosage, DCI)…"
            />
          </div>
          {/* No `overflow-x-auto` here: `ui/table.tsx` already wraps its own table in one, so this was a second
              horizontal scroller nested around the first — the wrapper now carries only the refetch dimming. */}
          <div className={refreshing ? "opacity-60 transition-opacity" : undefined}>
            <CardList
              className={CARDS_ONLY}
              ariaLabel="Catalogue de médicaments"
              items={medications}
              getKey={(m) => m.id}
              title={(m) => m.brandName}
              subtitle={(m) => m.dcis.join(", ")}
              muted={(m) => !m.isActive}
              status={(m) => (
                <>
                  {!m.isActive && <Badge variant="secondary">Inactif</Badge>}
                  {m.isProvisional && (
                    <Badge variant="outline" className="border-warning/50 text-warning-ink">
                      À vérifier
                    </Badge>
                  )}
                </>
              )}
              fields={(m) => [
                { label: "Forme", value: m.form },
                { label: "Dosage", value: m.strength },
              ]}
              actions={(m) => (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" aria-label={`Actions pour ${m.brandName}`}>
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onSelect={() => onEdit(m)}>Modifier</DropdownMenuItem>
                    {m.isActive && (
                      <DropdownMenuItem
                        className="text-destructive focus:text-destructive"
                        onSelect={() => setToDelete(m)}
                      >
                        Désactiver
                      </DropdownMenuItem>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
              empty={renderEmpty("compact")}
            />
            <Table containerClassName={TABLE_ONLY}>
              <TableHeader>
                <TableRow>
                  <TableHead>Nom commercial</TableHead>
                  <TableHead>DCI (molécules)</TableHead>
                  <TableHead>Forme</TableHead>
                  <TableHead>Dosage</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {medications.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={6}>{renderEmpty("default")}</TableCell>
                  </TableRow>
                ) : (
                  medications.map((m) => (
                    <TableRow key={m.id} className={m.isActive ? "" : "opacity-50"}>
                      <TableCell className="font-medium text-foreground">{m.brandName}</TableCell>
                      <TableCell className="text-muted-foreground">{m.dcis.join(", ")}</TableCell>
                      <TableCell className="text-muted-foreground">{m.form}</TableCell>
                      <TableCell className="text-muted-foreground">{m.strength}</TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {!m.isActive && <Badge variant="secondary">Inactif</Badge>}
                          {m.isProvisional && (
                            <Badge variant="outline" className="border-warning/50 text-warning-ink">
                              À vérifier
                            </Badge>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="ghost" size="sm" onClick={() => onEdit(m)} className="h-8 gap-1">
                            <Pencil className="h-3 w-3" />
                            Modifier
                          </Button>
                          {m.isActive && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setToDelete(m)}
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
              label={["médicament", "médicaments"]}
            />
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={toDelete !== null} onOpenChange={(open) => !open && setToDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Désactiver ce médicament ?</AlertDialogTitle>
            <AlertDialogDescription>
              Le médicament <span className="font-semibold">{toDelete?.brandName}</span> sera désactivé et
              n'apparaîtra plus dans le sélecteur de l'ordonnance. Les ordonnances déjà enregistrées ne sont
              pas modifiées.
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
