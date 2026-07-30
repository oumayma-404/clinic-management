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
import { Package, Search, Pencil, Trash2, Loader2, AlertTriangle, Minus, Plus, History, Hourglass } from "lucide-react"
import { cn } from "@/lib/utils"
import { stockApi, type StockMovementDto } from "@/lib/api/stock"
import { formatDate, formatDateTime } from "@/lib/format"
import { ApiError } from "@/lib/api/client"
import type { StockItemDto } from "@/lib/api/types"

interface StockTableProps {
  refreshKey: number
  onEdit: (item: StockItemDto) => void
  /** When set (from a low-stock notification deep-link), the matching row is highlighted + scrolled into view. */
  highlightItemId?: string | null
  /**
   * Pre-applies a filter on arrival, from the dashboard's « Stock bas » / « Périment bientôt » drill-through, so the
   * list shows exactly the items that card counted. `undefined` leaves the full list, which is the default.
   */
  initialFilter?: "low" | "expiring"
}

export function StockTable({ refreshKey, onEdit, highlightItemId, initialFilter }: StockTableProps) {
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
  const highlightRowRef = useRef<HTMLTableRowElement | null>(null)
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
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des articles"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, debouncedSearch, lowStockOnly, expiringOnly, categoryFilter])

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
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  type="text"
                  placeholder="Rechercher par nom…"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="pl-9"
                />
              </div>

              <Select value={categoryFilter} onValueChange={setCategoryFilter}>
                <SelectTrigger className="w-full sm:w-48">
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
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {loading ? (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="h-5 w-5 animate-spin" />
            </div>
          ) : error ? (
            <p className="py-12 text-center text-sm text-destructive">{error}</p>
          ) : (
            <div className="overflow-x-auto">
              <Table>
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
                  {items.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={8} className="h-24 text-center">
                        <p className="text-muted-foreground">
                          {debouncedSearch || lowStockOnly || expiringOnly || categoryFilter !== "all"
                            ? "Aucun article ne correspond à vos filtres"
                            : "Aucun article en stock"}
                        </p>
                      </TableCell>
                    </TableRow>
                  ) : (
                    items.map((item) => {
                      const isHighlighted = highlightItemId === item.id
                      return (
                      <TableRow
                        key={item.id}
                        ref={isHighlighted ? highlightRowRef : undefined}
                        className={cn(isHighlighted && "bg-primary/10 ring-1 ring-inset ring-primary")}
                      >
                        <TableCell className="font-medium text-foreground">{item.name}</TableCell>
                        <TableCell>
                          <Badge variant={item.isLowStock ? "destructive" : "default"} className="gap-1">
                            {item.isLowStock && <AlertTriangle className="h-3 w-3" />}
                            {item.currentStock}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-muted-foreground">{item.unit}</TableCell>
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
                                    ? "font-medium text-amber-700 dark:text-amber-400"
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
                        <TableCell className="text-muted-foreground">{item.supplier ?? "—"}</TableCell>
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
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>
              {adjustMode === "consume" ? "Sortie de stock" : "Entrée de stock"}
              {adjustTarget ? ` — ${adjustTarget.name}` : ""}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="adjustQty">Quantité</Label>
            <Input
              id="adjustQty"
              type="number"
              min="1"
              step="1"
              value={adjustQty}
              onChange={(e) => setAdjustQty(e.target.value)}
              placeholder="0"
              autoFocus
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
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Historique des mouvements{historyTarget ? ` — ${historyTarget.name}` : ""}</DialogTitle>
          </DialogHeader>
          <div className="max-h-80 overflow-y-auto">
            {movementsLoading ? (
              <p className="py-6 text-center text-sm text-muted-foreground">Chargement…</p>
            ) : movements.length === 0 ? (
              <p className="py-6 text-center text-sm text-muted-foreground">Aucun mouvement enregistré.</p>
            ) : (
              <Table>
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
                      <TableCell className="text-right">{m.type === "Consume" ? `-${m.quantity}` : `+${m.quantity}`}</TableCell>
                      <TableCell className="text-right">{m.resultingStock}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </div>
        </DialogContent>
      </Dialog>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Êtes-vous sûr ?</AlertDialogTitle>
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
