"use client"

import { useCallback, useEffect, useState } from "react"
import { MoreHorizontal, Pencil, Power, Trash2, Truck } from "lucide-react"
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
import { CARDS_ONLY, CardList, TABLE_ONLY } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { EmptyState } from "@/components/ui/empty-state"
import { FilterChip, ListToolbar } from "@/components/ui/list-toolbar"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { SupplierFormDialog } from "@/components/suppliers/supplier-form-dialog"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { ApiError } from "@/lib/api/client"
import { suppliersApi } from "@/lib/api/suppliers"
import type { SupplierDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { showErrorToast } from "@/lib/errors"
import { ZONES, zoneChipClass } from "@/lib/zones"

const PAGE_SIZE = 25

/**
 * « Fournisseurs » — the cabinet's outside contacts, and the one screen where their numbers live.
 *
 * <p>⚠️ <b>WhatsApp is a visible action on the card, never folded into the « ⋯ » menu</b> (AC-9). It is the
 * reason this screen exists — a supplier list nobody can call is an address book — and the phone is exactly
 * where it gets used, so burying it behind a menu on the narrow layout would hide the feature on its own
 * primary device.</p>
 *
 * <p>⚠️ Loading, empty, <b>filtered</b>-empty and failed are four distinct states. A failed read must never
 * render as « aucun fournisseur »: that is a claim about the clinic where the truth is a claim about the
 * network, and here it would send somebody looking for a supplier they filed last week.</p>
 */
export function SuppliersTable() {
  const [rows, setRows] = useState<SupplierDto[]>([])
  const [categories, setCategories] = useState<string[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [totalPages, setTotalPages] = useState(1)
  const [page, setPage] = useState(1)
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
        pageSize: PAGE_SIZE,
        q: debouncedSearch.trim() || undefined,
        category: category ?? undefined,
        // The list screen shows deactivated rows behind a chip; the pickers never ask for them.
        includeInactive: showInactive,
      })
      setRows(result.items)
      setCategories(result.categories)
      setTotalCount(result.totalCount)
      setTotalPages(result.totalPages)
    } catch {
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [page, debouncedSearch, category, showInactive])

  useEffect(() => {
    void load()
  }, [load])

  useClinicRealtime(RealtimeResource.Suppliers, load)

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
        <FilterChip
          label="Toutes catégories"
          active={category === null}
          onToggle={() => {
            setCategory(null)
            setPage(1)
          }}
        />
        {categories.map((c) => (
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
        <FilterChip
          label="Afficher les désactivés"
          active={showInactive}
          onToggle={() => {
            setShowInactive(!showInactive)
            setPage(1)
          }}
        />
      </ListToolbar>

      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          {loading ? "Chargement…" : `${totalCount} fournisseur${totalCount > 1 ? "s" : ""}`}
        </p>
        <Button onClick={openCreate} className="coarse:h-11">
          Nouveau fournisseur
        </Button>
      </div>

      {failed ? (
        <div className="rounded-lg border border-border bg-muted/40 p-6 text-center">
          <p className="text-sm text-muted-foreground">
            La liste des fournisseurs n'a pas pu être chargée.
          </p>
          <Button variant="outline" className="mt-3 coarse:h-11" onClick={() => void load()}>
            Réessayer
          </Button>
        </div>
      ) : loading ? (
        <div className="space-y-2" aria-hidden="true">
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="h-16 animate-pulse rounded-lg bg-muted/40" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={Truck}
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
          <div className={CARDS_ONLY}>
            <CardList
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
          </div>

          <div className={TABLE_ONLY}>
            <Table>
              <TableHeader>
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
                  <TableRow key={s.id} className={s.isActive ? undefined : "opacity-60"}>
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
          </div>

          <DataTablePagination
            page={{ page, pageSize: PAGE_SIZE, totalCount, totalPages }}
            onPageChange={setPage}
            loading={loading}
            label={["fournisseur", "fournisseurs"]}
          />
        </>
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
