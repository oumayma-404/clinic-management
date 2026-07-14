"use client"

import { useEffect, useState } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { StockTable } from "@/components/stock-table"
import { StockItemFormModal } from "@/components/stock-item-form-modal"
import { Button } from "@/components/ui/button"
import { Plus } from "lucide-react"
import type { StockItemDto } from "@/lib/api/types"

export default function StockPage() {
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<StockItemDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [highlightItemId, setHighlightItemId] = useState<string | null>(null)

  // Deep-link from a low-stock notification: highlight the referenced item's row. Clears the query
  // param so a refresh doesn't re-trigger it. Graceful — if the item isn't in the list, nothing is
  // highlighted (the user still lands on the stock screen).
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const itemId = params.get("itemId")
    if (itemId) {
      setHighlightItemId(itemId)
      window.history.replaceState({}, "", "/stock")
    }
  }, [])

  const handleAddNew = () => {
    setEditingItem(null)
    setModalOpen(true)
  }

  const handleEdit = (item: StockItemDto) => {
    setEditingItem(item)
    setModalOpen(true)
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div className="flex items-center justify-between">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Stock Management</h1>
                  <p className="mt-1 text-sm text-muted-foreground">Manage medical supplies and inventory</p>
                </div>

                <Button onClick={handleAddNew} className="gap-2">
                  <Plus className="h-4 w-4" />
                  Add New Item
                </Button>
              </div>

              {/* Stock Table */}
              <StockTable refreshKey={refreshKey} onEdit={handleEdit} highlightItemId={highlightItemId} />
            </div>
          </main>
        </div>

        <StockItemFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingItem={editingItem}
          onSaved={() => setRefreshKey((k) => k + 1)}
        />
      </div>
    </ClinicGuard>
  )
}
