"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
import { Package, Search, Pencil, Trash2, Loader2, AlertTriangle } from "lucide-react"
import { cn } from "@/lib/utils"
import { stockApi } from "@/lib/api/stock"
import { ApiError } from "@/lib/api/client"
import type { StockItemDto } from "@/lib/api/types"

interface StockTableProps {
  refreshKey: number
  onEdit: (item: StockItemDto) => void
  /** When set (from a low-stock notification deep-link), the matching row is highlighted + scrolled into view. */
  highlightItemId?: string | null
}

export function StockTable({ refreshKey, onEdit, highlightItemId }: StockTableProps) {
  const [items, setItems] = useState<StockItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [searchQuery, setSearchQuery] = useState("")
  const [categoryFilter, setCategoryFilter] = useState<string>("all")
  const [lowStockOnly, setLowStockOnly] = useState(false)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [itemToDelete, setItemToDelete] = useState<StockItemDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const highlightRowRef = useRef<HTMLTableRowElement | null>(null)
  // Scroll to the deep-linked row only once per deep-link — reset when the target changes.
  const hasScrolledRef = useRef(false)

  const loadItems = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await stockApi.list()
      setItems(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des articles"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [])

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

  const categories = useMemo(() => Array.from(new Set(items.map((i) => i.category))).sort(), [items])

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const matchesSearch = item.name.toLowerCase().includes(searchQuery.toLowerCase())
      const matchesCategory = categoryFilter === "all" || item.category === categoryFilter
      const matchesLowStock = !lowStockOnly || item.isLowStock
      return matchesSearch && matchesCategory && matchesLowStock
    })
  }, [items, searchQuery, categoryFilter, lowStockOnly])

  const lowStockCount = useMemo(() => items.filter((i) => i.isLowStock).length, [items])

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

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <CardTitle className="flex items-center gap-2">
              <Package className="h-5 w-5" />
              Articles en stock
              <Badge variant="secondary" className="ml-2">
                {filteredItems.length} articles
              </Badge>
              {lowStockCount > 0 && (
                <Button
                  variant={lowStockOnly ? "default" : "outline"}
                  size="sm"
                  onClick={() => setLowStockOnly((v) => !v)}
                  className="ml-2 h-7 gap-1"
                >
                  <AlertTriangle className="h-3 w-3" />
                  Stock faible ({lowStockCount})
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
                    <TableHead>Fournisseur</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filteredItems.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={7} className="h-24 text-center">
                        <p className="text-muted-foreground">
                          {items.length === 0 ? "Aucun article en stock" : "Aucun article ne correspond à vos filtres"}
                        </p>
                      </TableCell>
                    </TableRow>
                  ) : (
                    filteredItems.map((item) => {
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
                        <TableCell className="text-muted-foreground">{item.supplier ?? "—"}</TableCell>
                        <TableCell className="text-right">
                          <div className="flex justify-end gap-2">
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
            </div>
          )}
        </CardContent>
      </Card>

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
