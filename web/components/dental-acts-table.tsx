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
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
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

  /**
   * The two empty facts kept apart (finding #4). The list has a live search box and carried one message, so a
   * mistyped code reported the whole catalogue as empty. The filtered branch offers no « Ajouter un acte »: the
   * act almost certainly exists, and creating it again produces a duplicate `codeActe`.
   */
  const renderEmpty = (size: "default" | "compact") =>
    isSearching ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucun acte ne correspond à votre recherche</p>
        <Button variant="outline" size="sm" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={ClipboardList}
        size={size}
        title="Aucun acte dans le catalogue"
        description="Ce catalogue alimente le sélecteur d'actes des devis et des notes d'honoraires : code, désignation, tarif par défaut et accord préalable éventuel."
        action={
          <Button onClick={onAdd} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter un acte
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

      {/*
        Ungated by `hasProvisional`, deliberately: confirming the catalogue does not make this list verified. Which
        act families genuinely require an accord préalable is fixed by an **arrêté conjoint** we could not retrieve —
        the convention (art. 24) settles the procedure, not the list. The seeded flags are therefore a starting
        point, and this says so on the one screen where they can be corrected.

        It says this *here* because the flag is no longer dormant: since K1 the bulletin editor renders
        « Accord préalable requis » on the act row, so a wrong flag is now a wrong instruction shown to a
        practitioner in front of a patient rather than an unread column in an admin table. Muted rather than a
        second `warning-wash` block — stacked under the « à vérifier » banner, two warnings read as one noise.
      */}
      <div className="mb-4 rounded-lg border border-dashed p-3 text-xs text-muted-foreground">
        <span className="font-medium text-foreground">Accord préalable&nbsp;: à confirmer par famille d&apos;actes.</span>{" "}
        Les actes de <strong>prothèse</strong> n&apos;en requièrent plus depuis avril&nbsp;2019 (pris en charge hors
        plafond), et le drapeau a été retiré. La <strong>parodontologie</strong> et l&apos;<strong>ODF</strong> restent
        signalées par défaut&nbsp;: la liste officielle est fixée par arrêté conjoint et n&apos;a pas pu être
        vérifiée. Modifiez le drapeau acte par acte si votre caisse indique autre chose — il est propre à votre
        cabinet.
      </div>

      <Card>
        <CardHeader>
          {/* flex-wrap + a full-width button below sm:. Title and « Ajouter » together exceed a 288px
              card, and without wrapping the button ran outside the view. */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <CardTitle className="flex min-w-0 items-center gap-2">
              <ClipboardList className="h-5 w-5" />
              Catalogue des actes dentaires
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
          {/* The shared primitive on the theme's own destructive family, plus a retry: this file carried a
              hand-written `border-red-200 bg-red-50 … dark:` copy, so it maintained dark mode itself and the
              only escape from a failed read was a browser reload. */}
          <FormErrorBanner
            className="mb-4"
            message={error}
            action={{ label: "Réessayer", onClick: onChanged }}
          />
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
          {/* No `overflow-x-auto` here: `ui/table.tsx` already wraps its own table in one, so this was a second
              horizontal scroller nested around the first — the wrapper now carries only the refetch dimming. */}
          <div className={refreshing ? "opacity-60 transition-opacity" : undefined}>
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
                    <Badge variant="outline" className="border-warning/50 text-warning-ink">
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
              empty={renderEmpty("compact")}
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
                    <TableCell colSpan={8}>{renderEmpty("default")}</TableCell>
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
                            <Badge variant="outline" className="border-warning/50 text-warning-ink">
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
