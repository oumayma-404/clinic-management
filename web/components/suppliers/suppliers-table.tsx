"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { MoreHorizontal, Pencil, Power, SearchX, Trash2, Truck } from "lucide-react"
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
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { CARDS_ONLY_LG, CardList, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { EmptyState } from "@/components/ui/empty-state"
import { FilterChip, ListToolbar } from "@/components/ui/list-toolbar"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { SupplierFormDialog } from "@/components/suppliers/supplier-form-dialog"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { ApiError } from "@/lib/api/client"
import { suppliersApi } from "@/lib/api/suppliers"
import type { SupplierDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { showErrorToast } from "@/lib/errors"
import { ZONES, zoneChipClass } from "@/lib/zones"

const DEFAULT_PAGE_SIZE = 25

interface SuppliersTableProps {
  /**
   * Bumped by the page when « Nouveau fournisseur » is pressed in the `PageHeader`.
   *
   * The dialog and the list it reloads live here, so the action stays a one-line prop rather than lifting the
   * whole create flow into the route. A counter, not a boolean: two presses in a row must both arrive.
   */
  createRequest?: number
}

/**
 * « Fournisseurs » — the cabinet's outside contacts, and the one screen where their numbers live.
 *
 * <p>⚠️ <b>WhatsApp is a visible action on the card, never folded into the « ⋯ » menu</b> (AC-9). It is the
 * reason this screen exists — a supplier list nobody can call is an address book — and the phone is exactly
 * where it gets used, so burying it behind a menu on the narrow layout would hide the feature on its own
 * primary device.</p>
 *
 * <p>⚠️ <b>The hinge is `lg:`, not `md:`</b>, and that is AC-9 again rather than a density preference. Six
 * columns of `whitespace-nowrap` behind the 256 px rail leave ~532 px on an iPad portrait (820 px), so the table
 * scrolled sideways and the <b>Actions column — the WhatsApp button — sat off screen</b> on the device this
 * product is used on most. Measured, not assumed. Same pair as the invoices, lab-order and cheque tables.</p>
 *
 * <p>⚠️ Loading, empty, <b>filtered</b>-empty and failed are four distinct states. A failed read must never
 * render as « aucun fournisseur »: that is a claim about the clinic where the truth is a claim about the
 * network, and here it would send somebody looking for a supplier they filed last week.</p>
 */
export function SuppliersTable({ createRequest = 0 }: SuppliersTableProps) {
  const [rows, setRows] = useState<SupplierDto[]>([])
  /** The form's suggestion list — the twelve canonical labels plus the clinic's own. */
  const [categories, setCategories] = useState<string[]>([])
  /** What the *filter* offers: only the catégories a fournisseur is actually filed under. See the DTO. */
  const [categoriesInUse, setCategoriesInUse] = useState<string[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [totalPages, setTotalPages] = useState(1)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [category, setCategory] = useState<string | null>(null)
  const [showInactive, setShowInactive] = useState(false)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SupplierDto | null>(null)
  const [pendingDelete, setPendingDelete] = useState<SupplierDto | null>(null)

  useEffect(() => {
    const t = setTimeout(() => {
      setDebouncedSearch(search)
      setPage(1)
    }, 300)
    return () => clearTimeout(t)
  }, [search])

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      const result = await suppliersApi.listPaged({
        page,
        pageSize,
        q: debouncedSearch.trim() || undefined,
        category: category ?? undefined,
        // The list screen shows deactivated rows behind a chip; the pickers never ask for them.
        includeInactive: showInactive,
      })

      // A page past the end answers with the true total and no rows — `PageRequest` clamps the *size* and
      // deliberately does not clamp the page ("a stale bookmark should show rows, not an error"). Deleting or
      // deactivating the last row of page 2 therefore lands here, and rendering it would say « aucun
      // fournisseur » about a clinic that has plenty. Step back instead; the effect re-runs on the new page.
      if (result.items.length === 0 && result.totalCount > 0 && page > 1) {
        setPage(Math.min(page - 1, Math.max(1, result.totalPages)))
        return
      }

      setRows(result.items)
      setCategories(result.categories)
      setCategoriesInUse(result.categoriesInUse ?? [])
      setTotalCount(result.totalCount)
      setTotalPages(result.totalPages)
    } catch {
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, debouncedSearch, category, showInactive])

  useEffect(() => {
    void load()
  }, [load])

  useClinicRealtime(RealtimeResource.Suppliers, load)

  // The page's `PageHeader` action. Skipped on mount (`createRequest` starts at 0) so the dialog does not open
  // itself on arrival, which a plain effect on a boolean would do.
  const lastCreateRequest = useRef(createRequest)
  useEffect(() => {
    if (createRequest !== lastCreateRequest.current) {
      lastCreateRequest.current = createRequest
      setEditing(null)
      setFormOpen(true)
    }
  }, [createRequest])

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (supplier: SupplierDto) => {
    setEditing(supplier)
    setFormOpen(true)
  }

  const toggleActive = async (supplier: SupplierDto) => {
    try {
      // Only the flag and the fields the server needs to re-validate the record — `isActive` is tri-state, so
      // every other save leaves it alone.
      await suppliersApi.update(supplier.id, {
        name: supplier.name,
        category: supplier.category,
        phoneNumber: supplier.phoneNumber,
        address: supplier.address,
        notes: supplier.notes,
        isActive: !supplier.isActive,
        version: supplier.version,
      })
      toast.success(supplier.isActive ? "Fournisseur désactivé" : "Fournisseur réactivé")
      void load()
    } catch (err) {
      showErrorToast(err)
    }
  }

  const confirmDelete = async () => {
    if (!pendingDelete) return
    try {
      await suppliersApi.delete(pendingDelete.id)
      toast.success("Fournisseur supprimé")
      setPendingDelete(null)
      void load()
    } catch (err) {
      // `supplier_in_use` names the counts and points at « Désactiver ». Shown verbatim, and the dialog closes
      // because the refusal is about the record rather than about anything typed here.
      setPendingDelete(null)
      showErrorToast(err instanceof ApiError ? err : err)
    }
  }

  const filtered = debouncedSearch.trim() !== "" || category !== null

  const clearFilters = () => {
    setSearch("")
    setCategory(null)
    setPage(1)
  }

  const linkedSummary = (s: SupplierDto) => {
    const parts: string[] = []
    if (s.linkedItemCount > 0) parts.push(`${s.linkedItemCount} article${s.linkedItemCount > 1 ? "s" : ""}`)
    if (s.linkedLabOrderCount > 0) parts.push(`${s.linkedLabOrderCount} bon${s.linkedLabOrderCount > 1 ? "s" : ""}`)
    return parts.length > 0 ? parts.join(" · ") : null
  }

  const rowActions = (supplier: SupplierDto) => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="coarse:size-11">
          <MoreHorizontal aria-hidden="true" className="size-4" />
          <span className="sr-only">Actions pour {supplier.name}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem className="coarse:py-3" onClick={() => toggleActive(supplier)}>
          <Power aria-hidden="true" className="me-2 size-4" />
          {supplier.isActive ? "Désactiver" : "Réactiver"}
        </DropdownMenuItem>
        <DropdownMenuItem
          className="coarse:py-3 text-destructive focus:text-destructive"
          onClick={() => setPendingDelete(supplier)}
        >
          <Trash2 aria-hidden="true" className="me-2 size-4" />
          Supprimer
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )

  return (
    <div className="space-y-4">
      <ListToolbar
        search={{
          value: search,
          onChange: setSearch,
          placeholder: "Rechercher un fournisseur…",
          label: "Rechercher un fournisseur par nom, catégorie, téléphone ou adresse",
        }}
      >
        {/*
          ⚠️ `categoriesInUse`, NOT `categories`. The latter is the *form's* suggestion list and carries the twelve
          canonical labels whether or not the cabinet has ever filed one — as chips that was twelve controls over
          three rows (ten rows at 390 px) on a practice with four fournisseurs, nine of which could only answer
          « aucun résultat ». A filter offers what narrowing is possible. With one category in use there is nothing
          to choose between, so the whole group is dropped rather than rendered as a single dead pair.
        */}
        {categoriesInUse.length > 1 && (
          <>
            <FilterChip
              label="Toutes catégories"
              active={category === null}
              onToggle={() => {
                setCategory(null)
                setPage(1)
              }}
            />
            {categoriesInUse.map((c) => (
              <FilterChip
                key={c}
                label={c}
                active={category === c}
                onToggle={() => {
                  setCategory(category === c ? null : c)
                  setPage(1)
                }}
              />
            ))}
          </>
        )}
      </ListToolbar>

      {/*
        « Afficher les désactivés » is on its own row, deliberately. Every chip above it *narrows* the list; this
        one WIDENS it, it is not part of `filtered`, and « Effacer les filtres » leaves it alone — sitting at the
        end of the same wrapped row it read as a thirteenth category.
      */}
      <div className="flex items-center gap-2">
        <FilterChip
          label="Afficher les désactivés"
          active={showInactive}
          onToggle={() => {
            setShowInactive(!showInactive)
            setPage(1)
          }}
        />
      </div>

      {failed ? (
        // The shared primitive: `role="alert"`, because the reader is otherwise about to take an absence for a
        // fact. It replaced a hand-written muted box that read like an empty state.
        <LoadFailureNotice
          message="La liste des fournisseurs n'a pas pu être chargée."
          detail="Les contacts déjà enregistrés sont intacts."
          onRetry={() => void load()}
        />
      ) : (
        /*
          One bordered surface holding the list and its pager — the shape every other list in the app has
          (`invoices-table`, `treatment-plans-table`, `caisse-ledger-table`, and the `<Card>`-wrapped ones).
          `ui/table.tsx` paints `bg-card` and takes its radius from the parent, so without this the table rendered
          as a square, borderless white slab on the tinted page ground: the difference the eye notices first.
        */
        <div className="rounded-md border bg-card">
          {loading ? (
            <div className="space-y-2 p-3" role="status" aria-label="Chargement des fournisseurs">
              {[0, 1, 2, 3].map((i) => (
                <div key={i} className="h-14 animate-pulse rounded-lg bg-muted" />
              ))}
            </div>
          ) : rows.length === 0 ? (
            <EmptyState
              size="compact"
              icon={filtered ? SearchX : Truck}
              chipClassName={zoneChipClass(ZONES.ops)}
              title={filtered ? "Aucun fournisseur pour ces filtres" : "Aucun fournisseur"}
              description={
                filtered
                  ? "Aucun contact ne correspond à cette recherche. Le fournisseur existe peut-être sous une autre orthographe."
                  : "Enregistrez les laboratoires, dépôts et prestataires du cabinet pour les joindre en un geste."
              }
              action={
                filtered ? (
                  <Button variant="outline" className="coarse:h-11" onClick={clearFilters}>
                    Effacer les filtres
                  </Button>
                ) : (
                  <Button className="coarse:h-11" onClick={openCreate}>
                    Nouveau fournisseur
                  </Button>
                )
              }
            />
          ) : (
            <>
              <CardList
                className={CARDS_ONLY_LG}
                ariaLabel="Fournisseurs"
                items={rows}
                getKey={(s) => s.id}
                title={(s) => s.name}
                status={(s) => (
                  <>
                    {s.category ? <Badge variant="secondary">{s.category}</Badge> : null}
                    {!s.isActive ? <Badge variant="outline">Désactivé</Badge> : null}
                  </>
                )}
                fields={(s) => [
                  s.phoneNumber ? { label: "Téléphone", value: s.phoneNumber } : null,
                  s.address ? { label: "Adresse", value: s.address } : null,
                  linkedSummary(s) ? { label: "Lié à", value: linkedSummary(s) } : null,
                ]}
                actions={rowActions}
                primaryAction={(s) => (
                  <div className="flex items-center gap-2">
                    {/* AC-9 — visible on the card, not in the menu. */}
                    <WhatsAppAction
                      phoneE164={s.phoneE164}
                      contactName={s.name}
                      variant="default"
                      onAddNumber={() => openEdit(s)}
                      className="flex-1"
                    />
                    <Button variant="outline" size="sm" className="coarse:h-11" onClick={() => openEdit(s)}>
                      <Pencil aria-hidden="true" className="me-2 size-4" />
                      Modifier
                    </Button>
                  </div>
                )}
              />

              <Table containerClassName={TABLE_ONLY_LG}>
                {/* Sticky: this list pages at 25, so the column names are gone by row twelve and « Lié à »
                    becomes an unlabelled column of counts. */}
                <TableHeader sticky>
                  <TableRow>
                    <TableHead>Nom</TableHead>
                    <TableHead>Catégorie</TableHead>
                    <TableHead>Téléphone</TableHead>
                    <TableHead>Adresse</TableHead>
                    <TableHead>Lié à</TableHead>
                    <TableHead className="text-end">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((s) => (
                    <TableRow key={s.id} muted={!s.isActive}>
                      <TableCell className="font-medium">
                        {s.name}
                        {!s.isActive ? (
                          <Badge variant="outline" className="ms-2">
                            Désactivé
                          </Badge>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        {s.category ? <Badge variant="secondary">{s.category}</Badge> : null}
                      </TableCell>
                      <TableCell className="text-muted-foreground">{s.phoneNumber ?? ""}</TableCell>
                      <TableCell className="text-muted-foreground">{s.address ?? ""}</TableCell>
                      <TableCell className="text-muted-foreground">{linkedSummary(s) ?? ""}</TableCell>
                      <TableCell>
                        <div className="flex items-center justify-end gap-1">
                          <WhatsAppAction
                            phoneE164={s.phoneE164}
                            contactName={s.name}
                            onAddNumber={() => openEdit(s)}
                          />
                          <Button variant="ghost" size="icon" onClick={() => openEdit(s)}>
                            <Pencil aria-hidden="true" className="size-4" />
                            <span className="sr-only">Modifier {s.name}</span>
                          </Button>
                          {rowActions(s)}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </>
          )}

          {/*
            Inside the surface, not below it: the pager carries a `border-t` and no border of its own, precisely
            so it reads as this card's footer (`invoices-table` is the reference). It also carries the count line,
            which is why the free-floating « N fournisseurs » paragraph above the table is gone.

            Rendered on the empty branch too, so a stale page still has the control that gets you back — it used
            to live inside the non-empty branch and disappear exactly when it was needed.
          */}
          {!loading && (
            <DataTablePagination
              page={{ page, pageSize, totalCount, totalPages }}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size)
                setPage(1)
              }}
              loading={loading}
              label={["fournisseur", "fournisseurs"]}
            />
          )}
        </div>
      )}

      <SupplierFormDialog
        open={formOpen}
        onOpenChange={setFormOpen}
        editing={editing}
        categories={categories}
        onSaved={() => void load()}
      />

      <AlertDialog open={pendingDelete !== null} onOpenChange={(o) => !o && setPendingDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer « {pendingDelete?.name} » ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cette action est définitive. Si des articles de stock ou des bons de prothèse référencent ce
              fournisseur, la suppression sera refusée : désactivez-le à la place, il disparaîtra des listes de
              sélection sans effacer les liens existants.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="coarse:h-11">Annuler</AlertDialogCancel>
            <AlertDialogAction variant="destructive" className="coarse:h-11" onClick={confirmDelete}>
              Supprimer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
