"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import type { StockPageDto } from "@/lib/api/types"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
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
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import { Package, Search, Pencil, Trash2, AlertTriangle, Minus, Plus, History, Hourglass, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { ExportButton } from "@/components/ui/export-button"
import { stockUnitLabel } from "@/components/stock-item-form-modal"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { lowStockOrderMessage } from "@/lib/whatsapp"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"
import { stockApi, type StockMovementDto } from "@/lib/api/stock"
import { formatDate, formatDateTime } from "@/lib/format"
import { ApiError } from "@/lib/api/client"
import type { StockItemDto } from "@/lib/api/types"

/** Column widths the loading skeleton mirrors, in the table's own order. Kept beside the table so the two cannot
 *  drift into different shapes (same device as `patients-table.tsx`). */
const STOCK_COLUMN_WIDTHS = ["w-[20%]", "w-[9%]", "w-[9%]", "w-[10%]", "w-[14%]", "w-[13%]", "w-[13%]", "w-[12%]"] as const

interface StockTableProps {
  refreshKey: number
  onEdit: (item: StockItemDto) => void
  /**
   * Opens the create-item dialog. Needed here — not only on the page header — because the « aucun article »
   * empty state offers it as its primary action: a first-run stockroom is the most common way this screen is
   * seen, and an empty state with nothing to press is the defect `EmptyState` exists to end.
   */
  onAdd: () => void
  /** When set (from a low-stock notification deep-link), the matching row is highlighted + scrolled into view. */
  highlightItemId?: string | null
  /**
   * Pre-applies a filter on arrival, from the dashboard's « Stock bas » / « Périment bientôt » drill-through, so the
   * list shows exactly the items that card counted. `undefined` leaves the full list, which is the default.
   */
  initialFilter?: "low" | "expiring"
  /**
   * Hands the response's own `categories` facet up to the page, which passes it to the item form's catégorie
   * combobox. Lifted rather than re-fetched there: this read already carries the list, and a second one would be
   * a second answer to what this clinic files articles under.
   */
  onCategoriesChange?: (categories: string[]) => void
}

export function StockTable({
  refreshKey,
  onEdit,
  onAdd,
  highlightItemId,
  initialFilter,
  onCategoriesChange,
}: StockTableProps) {
  const [data, setData] = useState<StockPageDto | null>(null)
  // No `filteredItems`: the server already applied the search and every filter, so the rows that arrived ARE the
  // rows to render. Re-filtering here would narrow an already-cut page.
  const items = data?.items ?? []
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [searchQuery, setSearchQuery] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [categoryFilter, setCategoryFilter] = useState<string>("all")
  const [lowStockOnly, setLowStockOnly] = useState(initialFilter === "low")
  // Mirrors the « expiration » column's own reading (StockBatch.IsExpired / IsExpiringSoon, surfaced as the two DTO
  // flags): an already-expired lot is the more urgent case of the same alert, so it is included rather than split off.
  const [expiringOnly, setExpiringOnly] = useState(initialFilter === "expiring")
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [itemToDelete, setItemToDelete] = useState<StockItemDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  // Movement (sortie/entrée) dialog state (finding #14).
  const [adjustTarget, setAdjustTarget] = useState<StockItemDto | null>(null)
  const [adjustMode, setAdjustMode] = useState<"consume" | "restock">("consume")
  const [adjustQty, setAdjustQty] = useState("")
  // AC-P4.2 — an entrée creates a LOT, so the dialog captures that lot's own expiry and batch number.
  // Both stay optional (AC-P4.7): a delivery with neither behaves exactly as before.
  const [adjustExpiry, setAdjustExpiry] = useState("")
  const [adjustBatch, setAdjustBatch] = useState("")
  const [adjusting, setAdjusting] = useState(false)
  // Movement-history dialog state (finding #14).
  const [historyTarget, setHistoryTarget] = useState<StockItemDto | null>(null)
  const [movements, setMovements] = useState<StockMovementDto[]>([])
  const [movementsLoading, setMovementsLoading] = useState(false)
  // `HTMLElement`, not `HTMLTableRowElement`: the deep-link target is a `<tr>` above `md:` and an `<li>` card
  // below it. Only one of the two trees is ever mounted, so one ref serves both — and `scrollIntoView` is on
  // `HTMLElement`, so nothing here needs the narrower type.
  const highlightRowRef = useRef<HTMLElement | null>(null)
  // Scroll to the deep-linked row only once per deep-link — reset when the target changes.
  const hasScrolledRef = useRef(false)

  const loadItems = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await stockApi.listPaged({
        page,
        pageSize,
        search: debouncedSearch || undefined,
        lowStockOnly: lowStockOnly || undefined,
        expiringOnly: expiringOnly || undefined,
        category: categoryFilter === "all" ? undefined : categoryFilter,
      })
      setData(data)
      onCategoriesChange?.(data.categories)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des articles"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, debouncedSearch, lowStockOnly, expiringOnly, categoryFilter, onCategoriesChange])

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchQuery.trim()), 300)
    return () => clearTimeout(timer)
  }, [searchQuery])

  // Any change to the search or a filter must send the table back to page 1 — otherwise toggling
  // « Stock faible » while on page 4 lands on an empty page of a two-page result.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch, lowStockOnly, expiringOnly, categoryFilter])

  useEffect(() => {
    loadItems()
  }, [loadItems, refreshKey])

  // A fresh deep-link target re-arms the one-shot scroll.
  useEffect(() => {
    hasScrolledRef.current = false
  }, [highlightItemId])

  // Scroll the deep-linked (highlighted) item into view once the list has loaded — exactly once per
  // deep-link. Deliberately NOT keyed off `items`: it gets a new reference on every reload (add/edit/
  // delete), which would otherwise yank the viewport back to the highlighted row on unrelated changes.
  useEffect(() => {
    if (loading || !highlightItemId || hasScrolledRef.current || !highlightRowRef.current) return
    highlightRowRef.current.scrollIntoView({ behavior: "smooth", block: "center" })
    hasScrolledRef.current = true
  }, [loading, highlightItemId])

  // All three come from the response, clinic-wide. Derived from `items` they would describe the current page:
  // the dropdown would only offer the categories that page happened to contain, and « Stock faible (N) » would
  // report the low items among 25 rows while the stockroom had far more — the figure someone reorders from.
  const categories = data?.categories ?? []
  const lowStockCount = data?.lowStockCount ?? 0
  const expiringCount = data?.expiringCount ?? 0

  /**
   * Whether the list is currently narrowed. This is the whole point of the empty-state split: « vous n'avez
   * aucun article » and « aucun article ne correspond à vos filtres » are different facts, and only the first
   * one may offer « Ajouter un article » — on the second the item probably exists and the user simply mistyped
   * or left « Stock faible » toggled on, so an add button is an invitation to create a duplicate.
   */
  const hasActiveFilter =
    debouncedSearch !== "" || lowStockOnly || expiringOnly || categoryFilter !== "all"

  const clearFilters = () => {
    setSearchQuery("")
    setDebouncedSearch("")
    setLowStockOnly(false)
    setExpiringOnly(false)
    setCategoryFilter("all")
  }

  /**
   * One empty state, rendered into both trees.
   *
   * `compact` on the card list because `CardList` already wraps whatever it is given in its own `py-10`; the
   * default size on top of that is ~140px of nothing above the icon on a 320px screen.
   */
  const renderEmpty = (size: "default" | "compact") =>
    hasActiveFilter ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucun article ne correspond à vos filtres</p>
        <Button variant="outline" size="sm" onClick={clearFilters}>
          Effacer les filtres
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={Package}
        size={size}
        title="Aucun article en stock"
        description="Enregistrez vos consommables et votre matériel ici : l'application suit les quantités, prévient quand un article passe sous son seuil et signale les lots qui approchent de leur péremption."
        action={
          <Button onClick={onAdd} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter un article
          </Button>
        }
      />
    )

  const handleDelete = (item: StockItemDto) => {
    setItemToDelete(item)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (!itemToDelete) return
    try {
      setDeleting(true)
      await stockApi.delete(itemToDelete.id)
      toast.success(`« ${itemToDelete.name} » supprimé`)
      setDeleteDialogOpen(false)
      setItemToDelete(null)
      await loadItems()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression de l'article")
    } finally {
      setDeleting(false)
    }
  }

  const openHistory = async (item: StockItemDto) => {
    setHistoryTarget(item)
    setMovements([])
    setMovementsLoading(true)
    try {
      setMovements(await stockApi.movements(item.id))
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du chargement de l'historique")
    } finally {
      setMovementsLoading(false)
    }
  }

  const openAdjust = (item: StockItemDto, mode: "consume" | "restock") => {
    setAdjustTarget(item)
    setAdjustMode(mode)
    setAdjustQty("")
    setAdjustExpiry("")
    setAdjustBatch("")
  }

  const confirmAdjust = async () => {
    if (!adjustTarget) return
    const qty = Number.parseInt(adjustQty, 10)
    if (!Number.isFinite(qty) || qty <= 0) {
      toast.error("La quantité doit être supérieure à 0.")
      return
    }
    try {
      setAdjusting(true)
      if (adjustMode === "consume") {
        await stockApi.consume(adjustTarget.id, qty)
        toast.success(`Sortie enregistrée (${qty})`)
      } else {
        // AC-P4.2 — expiry and batch travel with the lot. Empty inputs are sent as null, not "", so an
        // entrée without them creates a lot carrying no expiry (AC-P4.7) rather than an unparseable date.
        await stockApi.restock(adjustTarget.id, {
          quantity: qty,
          expiryDate: adjustExpiry || null,
          batchNumber: adjustBatch.trim() || null,
        })
        toast.success(`Entrée enregistrée (${qty})`)
      }
      setAdjustTarget(null)
      await loadItems()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du mouvement de stock")
    } finally {
      setAdjusting(false)
    }
  }

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <CardTitle className="flex items-center gap-2">
              <Package className="h-5 w-5" />
              Articles en stock
              <Badge variant="secondary" className="ml-2">
                {data?.totalCount ?? 0} articles
              </Badge>
              {lowStockCount > 0 && (
                <Button
                  variant={lowStockOnly ? "default" : "outline"}
                  size="sm"
                  aria-pressed={lowStockOnly}
                  onClick={() => setLowStockOnly((v) => !v)}
                  className="ml-2 h-7 gap-1"
                >
                  <AlertTriangle className="h-3 w-3" aria-hidden="true" />
                  Stock faible ({lowStockCount})
                </Button>
              )}
              {expiringCount > 0 && (
                <Button
                  variant={expiringOnly ? "default" : "outline"}
                  size="sm"
                  aria-pressed={expiringOnly}
                  onClick={() => setExpiringOnly((v) => !v)}
                  className="ml-2 h-7 gap-1"
                >
                  <Hourglass className="h-3 w-3" aria-hidden="true" />
                  Péremption ({expiringCount})
                </Button>
              )}
            </CardTitle>

            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <div className="relative w-full sm:w-64">
                {/* A placeholder is not a label: it disappears the moment the field has a value, and a screen
                    reader announces the input as unnamed. Every other search box in the app already carries an
                    `sr-only` <Label htmlFor>; this one was the exception. */}
                <Label htmlFor="stock-search" className="sr-only">
                  Rechercher un article par nom
                </Label>
                <Search
                  aria-hidden="true"
                  className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                />
                <Input
                  id="stock-search"
                  type="text"
                  placeholder="Rechercher par nom…"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="pl-9"
                />
              </div>

              {/* The stored value IS the French label now, so it is both what the server filters on and what is
                  displayed — the six English keys were rewritten by the suppliers migration. The options come
                  from the response's own `categories` facet, so a clinic-authored one is offered like any other. */}
              <Select value={categoryFilter} onValueChange={setCategoryFilter}>
                <SelectTrigger aria-label="Filtrer par catégorie" className="w-full sm:w-48">
                  <SelectValue placeholder="Toutes les catégories" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Toutes les catégories</SelectItem>
                  {categories.map((category) => (
                    <SelectItem key={category} value={category}>
                      {category}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              {/*
                L5 — « Exporter » is placed here, with the four filters, rather than in the page header: this
                component owns all four (search, catégorie, stock faible, péremption) and the file has to be the
                list. A copy of them lifted to `app/stock/page.tsx` would be a second authority on what is on
                screen, and the params below are read from the same variables `loadItems` sends — the same
                `debouncedSearch`, the same `undefined`-when-off shape — so the two cannot describe different sets.
              */}
              <ExportButton
                path="/stock/export"
                label="articles"
                compact
                className="self-start sm:self-auto"
                params={{
                  search: debouncedSearch || undefined,
                  lowStockOnly: lowStockOnly || undefined,
                  expiringOnly: expiringOnly || undefined,
                  category: categoryFilter === "all" ? undefined : categoryFilter,
                }}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {/*
            A failed read is a dead end without a way out (finding #2): this used to be a bare red <p> whose only
            remedy was a browser reload — something a non-technical user on an installed PWA has no gesture for.
            Same shape as `dashboard/dashboard-section.tsx`, which is the app's reference treatment.
          */}
          {error ? (
            <div
              role="status"
              className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
            >
              <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
              <span className="min-w-0 flex-1">{error}</span>
              <Button size="sm" variant="outline" onClick={loadItems}>
                Réessayer
              </Button>
            </div>
          ) : (
            <div>
              {/* ⚠️ `itemRef` carries the deep-link target across: a low-stock notification scrolls this list
                  to one row, and on a phone the card IS that row. Without it the link would land at the top of
                  an unscrolled list — which reads as the link being broken. */}
              <CardList
                className={CARDS_ONLY}
                ariaLabel="Articles en stock"
                items={items}
                /* `loading` reaches the list now. The outer spinner branch above used to short-circuit before
                   this, so the skeletons this primitive already knows how to draw were unreachable and every
                   load replaced a ~100px spinner box with a full list — a page jump on every visit. */
                loading={loading}
                getKey={(i) => i.id}
                title={(i) => i.name}
                subtitle={(i) => i.supplierName}
                itemRef={(i) =>
                  highlightItemId === i.id
                    ? (el: HTMLLIElement | null) => {
                        highlightRowRef.current = el
                      }
                    : undefined
                }
                status={(i) => (
                  <>
                    <Badge variant={i.isLowStock ? "destructive" : "default"} className="gap-1">
                      {i.isLowStock && <AlertTriangle className="h-3 w-3" />}
                      {i.currentStock} {stockUnitLabel(i.unit)}
                    </Badge>
                    <Badge variant="outline">{i.category}</Badge>
                  </>
                )}
                fields={(i) => [
                  {
                    label: "Péremption",
                    value: i.earliestExpiry ? (
                      <span
                        className={cn(
                          "inline-flex items-center gap-1",
                          i.hasExpiredStock
                            ? "font-medium text-destructive"
                            : i.isExpiringSoon
                              ? // `--warning-ink` is the darkened amber that stays legible in both themes; the
                                // `amber-700 / dark:amber-400` pair it replaces was hand-maintained and off-palette.
                                "font-medium text-warning-ink"
                              : undefined,
                        )}
                      >
                        {(i.hasExpiredStock || i.isExpiringSoon) && (
                          <AlertTriangle className="h-3 w-3" aria-hidden="true" />
                        )}
                        {formatDate(i.earliestExpiry)}
                        {/* The table put this in a `title=`, which no touch device can reach. */}
                        {i.hasExpiredStock ? " · périmé" : i.isExpiringSoon ? " · expire bientôt" : ""}
                      </span>
                    ) : null,
                  },
                  { label: "Stock min.", value: i.minimumStockLevel },
                ]}
                actions={(i) => (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="ghost" size="icon" aria-label={`Actions pour ${i.name}`}>
                        <MoreHorizontal className="h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => openAdjust(i, "consume")}>Sortie de stock</DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => openAdjust(i, "restock")}>Entrée de stock</DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => openHistory(i)}>Historique</DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => onEdit(i)}>Modifier</DropdownMenuItem>
                      <DropdownMenuItem
                        className="text-destructive focus:text-destructive"
                        /* `handleDelete`, not `setItemToDelete`: the AlertDialog below is gated on
                           `deleteDialogOpen`, which only `handleDelete` sets — so on a phone « Supprimer »
                           silently did nothing. */
                        onSelect={() => handleDelete(i)}
                      >
                        Supprimer
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                )}
                /*
                  ⚠️ The critical one (finding #1). This was the ONLY one of the app's 25 `CardList` call sites
                  with no `empty`, and `card-list.tsx` returns `null` in that case — while the desktop message
                  lives inside the desktop-only table (`containerClassName={TABLE_ONLY}`, i.e. `hidden md:block`)
                  and is never rendered below `md:` either. So filtering stock to an empty category on the tablet
                  a dentist actually holds showed the header, the search box, the filters — and then a blank void
                  with no explanation.
                */
                empty={renderEmpty("compact")}
              />
              <Table containerClassName={TABLE_ONLY}>
                <TableHeader>
                  <TableRow>
                    <TableHead>Nom de l'article</TableHead>
                    <TableHead>Quantité</TableHead>
                    <TableHead>Unité</TableHead>
                    <TableHead>Stock min.</TableHead>
                    <TableHead>Catégorie</TableHead>
                    <TableHead>Péremption</TableHead>
                    <TableHead>Fournisseur</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {/* A skeleton shaped like the rows it stands in for, so the arriving list does not shift the
                      page — the desktop half of the same fix as `CardList loading` above. */}
                  {loading ? (
                    Array.from({ length: 6 }).map((_, row) => (
                      <TableRow key={`skeleton-${row}`}>
                        {STOCK_COLUMN_WIDTHS.map((width, col) => (
                          <TableCell key={col}>
                            <div
                              className={`h-5 animate-pulse rounded bg-muted ${width}`}
                              role={row === 0 && col === 0 ? "status" : undefined}
                              aria-label={row === 0 && col === 0 ? "Chargement des articles" : undefined}
                            />
                          </TableCell>
                        ))}
                      </TableRow>
                    ))
                  ) : items.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={8}>{renderEmpty("default")}</TableCell>
                    </TableRow>
                  ) : (
                    items.map((item) => {
                      const isHighlighted = highlightItemId === item.id
                      return (
                      <TableRow
                        key={item.id}
                        ref={
                          isHighlighted
                            ? (el: HTMLTableRowElement | null) => {
                                highlightRowRef.current = el
                              }
                            : undefined
                        }
                        className={cn(isHighlighted && "bg-primary/10 ring-1 ring-inset ring-primary")}
                      >
                        <TableCell className="font-medium text-foreground">{item.name}</TableCell>
                        <TableCell>
                          <Badge variant={item.isLowStock ? "destructive" : "default"} className="gap-1">
                            {item.isLowStock && <AlertTriangle className="h-3 w-3" />}
                            {item.currentStock}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-muted-foreground">{stockUnitLabel(item.unit)}</TableCell>
                        <TableCell className="text-muted-foreground">{item.minimumStockLevel}</TableCell>
                        <TableCell>
                          <Badge variant="outline">{item.category}</Badge>
                        </TableCell>
                        <TableCell>
                          {item.earliestExpiry ? (
                            <span
                              className={cn(
                                "inline-flex items-center gap-1 text-sm",
                                item.hasExpiredStock
                                  ? "font-medium text-destructive"
                                  : item.isExpiringSoon
                                    ? "font-medium text-warning-ink"
                                    : "text-muted-foreground",
                              )}
                              title={
                                item.hasExpiredStock
                                  ? "Un lot est périmé"
                                  : item.isExpiringSoon
                                    ? "Un lot expire bientôt"
                                    : undefined
                              }
                            >
                              {(item.hasExpiredStock || item.isExpiringSoon) && (
                                <AlertTriangle className="h-3 w-3" aria-hidden="true" />
                              )}
                              {formatDate(item.earliestExpiry)}
                            </span>
                          ) : (
                            <span className="text-muted-foreground">—</span>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {item.supplierName ? (
                            <span className="inline-flex items-center gap-1">
                              <span className="truncate">{item.supplierName}</span>
                              {/* AC-3 — the point of the whole feature: the name is now something you can act
                                  on. No « Ajouter un numéro » fallback here, because this table cannot open the
                                  fournisseur's own form; the row simply reads as a name, as it always did. */}
                              <WhatsAppAction
                                phoneE164={item.supplierPhoneE164}
                                contactName={item.supplierName}
                                message={lowStockOrderMessage(item.name, item.currentStock, stockUnitLabel(item.unit))}
                              />
                            </span>
                          ) : (
                            "—"
                          )}
                        </TableCell>
                        <TableCell className="text-right">
                          <div className="flex justify-end gap-2">
                            <Button variant="ghost" size="sm" onClick={() => openAdjust(item, "consume")} className="h-8 gap-1" title="Sortie de stock">
                              <Minus className="h-3 w-3" />
                              Sortie
                            </Button>
                            <Button variant="ghost" size="sm" onClick={() => openAdjust(item, "restock")} className="h-8 gap-1" title="Entrée de stock">
                              <Plus className="h-3 w-3" />
                              Entrée
                            </Button>
                            <Button variant="ghost" size="sm" onClick={() => openHistory(item)} className="h-8 w-8 p-0" title="Historique des mouvements">
                              <History className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="sm" onClick={() => onEdit(item)} className="h-8 gap-1">
                              <Pencil className="h-3 w-3" />
                              Modifier
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => handleDelete(item)}
                              className="h-8 gap-1 text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-3 w-3" />
                              Supprimer
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                      )
                    })
                  )}
                </TableBody>
              </Table>
              {data && (
                <DataTablePagination
                  page={data}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  loading={loading}
                  label={["article", "articles"]}
                />
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={!!adjustTarget} onOpenChange={(open) => { if (!open) setAdjustTarget(null) }}>
        <DialogContent className="md:max-w-sm">
          <DialogHeader>
            <DialogTitle>
              {adjustMode === "consume" ? "Sortie de stock" : "Entrée de stock"}
              {adjustTarget ? ` — ${adjustTarget.name}` : ""}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="adjustQty">Quantité</Label>
            {/* No `autoFocus`: `ui/dialog.tsx` deliberately redirects the opening focus to the TITLE so a sheet
                opened on a phone does not raise the keyboard and lose half the viewport before the user has
                asked to type. An `autoFocus` here opted this one dialog back out of that. */}
            <Input
              id="adjustQty"
              type="number"
              min="1"
              step="1"
              value={adjustQty}
              onChange={(e) => setAdjustQty(e.target.value)}
              placeholder="0"
            />
            {adjustTarget && (
              <p className="text-xs text-muted-foreground">Stock actuel : {adjustTarget.currentStock}</p>
            )}
          </div>

          {/* AC-P4.2 — only an entrée creates a lot; a sortie draws down the existing ones (FEFO), so it has
              nothing to date or number. Both fields are optional (AC-P4.7). */}
          {adjustMode === "restock" && (
            <div className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="adjustExpiry">Date de péremption (optionnel)</Label>
                <Input
                  id="adjustExpiry"
                  type="date"
                  value={adjustExpiry}
                  onChange={(e) => setAdjustExpiry(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="adjustBatch">N° de lot (optionnel)</Label>
                <Input
                  id="adjustBatch"
                  type="text"
                  value={adjustBatch}
                  onChange={(e) => setAdjustBatch(e.target.value)}
                  placeholder="ex. L-2026-04"
                />
              </div>
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setAdjustTarget(null)} disabled={adjusting}>
              Annuler
            </Button>
            <Button onClick={confirmAdjust} disabled={adjusting}>
              {adjusting ? "Enregistrement…" : "Confirmer"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!historyTarget} onOpenChange={(open) => { if (!open) setHistoryTarget(null) }}>
        <DialogContent className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>Historique des mouvements{historyTarget ? ` — ${historyTarget.name}` : ""}</DialogTitle>
          </DialogHeader>
          <div className="max-h-80 overflow-y-auto">
            {movementsLoading ? (
              <p role="status" className="py-6 text-center text-sm text-muted-foreground">
                Chargement…
              </p>
            ) : movements.length === 0 ? (
              <p className="py-6 text-center text-sm text-muted-foreground">Aucun mouvement enregistré.</p>
            ) : (
              <>
                {/*
                  Four columns in a `md:max-w-lg` dialog is a sideways scroll on a 390px phone, nested inside this
                  block's own vertical scroller. Below `md:` the movements become a stacked list instead — the
                  shape `factures/invoice-detail-modal.tsx` already uses for an invoice's payments, rather than a
                  `CardList` (there is no row identity, no action menu and no accent: a movement is one line).
                */}
                <ul className={`${CARDS_ONLY} divide-y rounded-md border`}>
                  {movements.map((m) => (
                    <li key={m.id} className="flex items-baseline justify-between gap-3 p-3 text-sm">
                      <div className="min-w-0">
                        <p className="font-medium text-foreground">
                          {m.type === "Consume" ? "Sortie" : "Entrée"}{" "}
                          <span className="tabular-nums">
                            {m.type === "Consume" ? `-${m.quantity}` : `+${m.quantity}`}
                          </span>
                        </p>
                        <p className="text-xs text-muted-foreground">{formatDateTime(m.createdAt)}</p>
                      </div>
                      <p className="shrink-0 text-end text-xs text-muted-foreground">
                        Stock&nbsp;: <span className="tabular-nums text-foreground">{m.resultingStock}</span>
                      </p>
                    </li>
                  ))}
                </ul>
                <Table containerClassName={TABLE_ONLY}>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Date</TableHead>
                      <TableHead>Type</TableHead>
                      <TableHead className="text-right">Quantité</TableHead>
                      <TableHead className="text-right">Stock résultant</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {movements.map((m) => (
                      <TableRow key={m.id}>
                        <TableCell className="text-muted-foreground">{formatDateTime(m.createdAt)}</TableCell>
                        <TableCell>{m.type === "Consume" ? "Sortie" : "Entrée"}</TableCell>
                        <TableCell numeric>{m.type === "Consume" ? `-${m.quantity}` : `+${m.quantity}`}</TableCell>
                        <TableCell numeric>{m.resultingStock}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </>
            )}
          </div>
        </DialogContent>
      </Dialog>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            {/* The title names the OBJECT. « Êtes-vous sûr ? » is the same sentence for deleting a stock item and
                for deleting a patient, so it carries none of the information the confirm exists to give. */}
            <AlertDialogTitle>Supprimer cet article ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cela supprimera définitivement <span className="font-semibold">{itemToDelete?.name}</span> de
              l'inventaire. Cette action est irréversible.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                confirmDelete()
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
