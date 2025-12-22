"use client"

import { useMemo, useState } from "react"
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
import { Package, Search, Pencil, Trash2, ArrowUpDown } from "lucide-react"

// Sample stock data
const stockData = [
  {
    id: "1",
    itemName: "Surgical Gloves (Box)",
    itemCode: "SG-001",
    quantity: 150,
    unit: "Box",
    category: "Medical Supplies",
    lastUpdated: "2024-01-20",
  },
  {
    id: "2",
    itemName: "Disposable Syringes 5ml",
    itemCode: "DS-005",
    quantity: 500,
    unit: "Unit",
    category: "Medical Supplies",
    lastUpdated: "2024-01-19",
  },
  {
    id: "3",
    itemName: "Blood Pressure Monitor",
    itemCode: "BPM-003",
    quantity: 8,
    unit: "Unit",
    category: "Medical Equipment",
    lastUpdated: "2024-01-18",
  },
  {
    id: "4",
    itemName: "Bandages (Roll)",
    itemCode: "BD-010",
    quantity: 75,
    unit: "Roll",
    category: "Medical Supplies",
    lastUpdated: "2024-01-20",
  },
  {
    id: "5",
    itemName: "Thermometer Digital",
    itemCode: "TD-002",
    quantity: 12,
    unit: "Unit",
    category: "Medical Equipment",
    lastUpdated: "2024-01-17",
  },
  {
    id: "6",
    itemName: "Alcohol Swabs (Box)",
    itemCode: "AS-100",
    quantity: 200,
    unit: "Box",
    category: "Medical Supplies",
    lastUpdated: "2024-01-21",
  },
  {
    id: "7",
    itemName: "N95 Face Masks",
    itemCode: "FM-N95",
    quantity: 350,
    unit: "Unit",
    category: "PPE",
    lastUpdated: "2024-01-19",
  },
  {
    id: "8",
    itemName: "Stethoscope",
    itemCode: "ST-001",
    quantity: 5,
    unit: "Unit",
    category: "Medical Equipment",
    lastUpdated: "2024-01-15",
  },
  {
    id: "9",
    itemName: "Cotton Balls (Bag)",
    itemCode: "CB-500",
    quantity: 40,
    unit: "Bag",
    category: "Medical Supplies",
    lastUpdated: "2024-01-20",
  },
  {
    id: "10",
    itemName: "Pulse Oximeter",
    itemCode: "PO-004",
    quantity: 10,
    unit: "Unit",
    category: "Medical Equipment",
    lastUpdated: "2024-01-16",
  },
]

type SortField = "itemName" | "quantity" | "category" | "lastUpdated"
type SortOrder = "asc" | "desc"

interface StockTableProps {
  onEdit: (item: any) => void
}

export function StockTable({ onEdit }: StockTableProps) {
  const [searchQuery, setSearchQuery] = useState("")
  const [categoryFilter, setCategoryFilter] = useState<string>("all")
  const [sortField, setSortField] = useState<SortField>("itemName")
  const [sortOrder, setSortOrder] = useState<SortOrder>("asc")
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [itemToDelete, setItemToDelete] = useState<any>(null)
  const [stockItems, setStockItems] = useState(stockData)

  // Get unique categories
  const categories = useMemo(() => {
    const cats = Array.from(new Set(stockItems.map((item) => item.category)))
    return cats.sort()
  }, [stockItems])

  // Filter and sort stock items
  const filteredAndSortedItems = useMemo(() => {
    const filtered = stockItems.filter((item) => {
      const matchesSearch =
        item.itemName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        item.itemCode.toLowerCase().includes(searchQuery.toLowerCase())

      const matchesCategory = categoryFilter === "all" || item.category === categoryFilter

      return matchesSearch && matchesCategory
    })

    // Sort
    filtered.sort((a, b) => {
      const aVal = a[sortField]
      const bVal = b[sortField]

      if (sortField === "quantity") {
        const aNum = typeof aVal === "number" ? aVal : Number(aVal) || 0
        const bNum = typeof bVal === "number" ? bVal : Number(bVal) || 0
        return sortOrder === "asc" ? aNum - bNum : bNum - aNum
      }

      if (typeof aVal === "string" && typeof bVal === "string") {
        return sortOrder === "asc" ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal)
      }

      return 0
    })

    return filtered
  }, [stockItems, searchQuery, categoryFilter, sortField, sortOrder])

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc")
    } else {
      setSortField(field)
      setSortOrder("asc")
    }
  }

  const handleDelete = (item: any) => {
    setItemToDelete(item)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = () => {
    if (itemToDelete) {
      setStockItems(stockItems.filter((item) => item.id !== itemToDelete.id))
      setDeleteDialogOpen(false)
      setItemToDelete(null)
    }
  }

  const formatDate = (dateString: string) => {
    const date = new Date(dateString)
    return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })
  }

  const SortButton = ({ field, label }: { field: SortField; label: string }) => (
    <Button variant="ghost" size="sm" onClick={() => handleSort(field)} className="flex items-center gap-1 -ml-3 h-8">
      {label}
      <ArrowUpDown className="h-3 w-3" />
    </Button>
  )

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <CardTitle className="flex items-center gap-2">
              <Package className="h-5 w-5" />
              Inventory Items
              <Badge variant="secondary" className="ml-2">
                {filteredAndSortedItems.length} items
              </Badge>
            </CardTitle>

            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              {/* Search */}
              <div className="relative w-full sm:w-64">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  type="text"
                  placeholder="Search by name or code..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="pl-9"
                />
              </div>

              {/* Category Filter */}
              <Select value={categoryFilter} onValueChange={setCategoryFilter}>
                <SelectTrigger className="w-full sm:w-48">
                  <SelectValue placeholder="All Categories" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All Categories</SelectItem>
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
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>
                    <SortButton field="itemName" label="Item Name" />
                  </TableHead>
                  <TableHead>Item Code</TableHead>
                  <TableHead>
                    <SortButton field="quantity" label="Quantity" />
                  </TableHead>
                  <TableHead>Unit</TableHead>
                  <TableHead>
                    <SortButton field="category" label="Category" />
                  </TableHead>
                  <TableHead>
                    <SortButton field="lastUpdated" label="Last Updated" />
                  </TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredAndSortedItems.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={7} className="h-24 text-center">
                      <p className="text-muted-foreground">No items found</p>
                    </TableCell>
                  </TableRow>
                ) : (
                  filteredAndSortedItems.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell className="font-medium text-foreground">{item.itemName}</TableCell>
                      <TableCell className="font-mono text-sm text-muted-foreground">{item.itemCode}</TableCell>
                      <TableCell>
                        <Badge
                          variant={item.quantity < 20 ? "destructive" : item.quantity < 50 ? "secondary" : "default"}
                        >
                          {item.quantity}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{item.unit}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{item.category}</Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{formatDate(item.lastUpdated)}</TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="ghost" size="sm" onClick={() => onEdit(item)} className="h-8 gap-1">
                            <Pencil className="h-3 w-3" />
                            Edit
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleDelete(item)}
                            className="h-8 gap-1 text-destructive hover:text-destructive"
                          >
                            <Trash2 className="h-3 w-3" />
                            Delete
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      {/* Delete Confirmation Dialog */}
      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Are you sure?</AlertDialogTitle>
            <AlertDialogDescription>
              This will permanently delete <span className="font-semibold">{itemToDelete?.itemName}</span> from the
              inventory. This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
