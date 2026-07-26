"use client"

import { useCallback, useEffect, useState } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { StockTable } from "@/components/stock-table"
import { StockItemFormModal } from "@/components/stock-item-form-modal"
import { Button } from "@/components/ui/button"
import { Plus } from "lucide-react"
import type { StockItemDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function StockPage() {
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<StockItemDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [highlightItemId, setHighlightItemId] = useState<string | null>(null)

  // Live-refresh on a peer's stock mutation (finding #14: the page didn't subscribe though the backend
  // already broadcasts the "stock" key).
  useClinicRealtime(RealtimeResource.Stock, useCallback(() => setRefreshKey((k) => k + 1), []))

  // Deep-link from a low-stock notification: highlight the referenced item's row. Clears the query
  // param so a refresh doesn't re-trigger it. Graceful — if the item isn't in the list, nothing is
  // highlighted (the user still lands on the stock screen).
  const highlightItem = useCallback((itemId: string) => {
    setHighlightItemId(itemId)
    window.history.replaceState({}, "", "/stock")
  }, [])

  // On mount (cross-page navigation): read the query param.
  useEffect(() => {
    const itemId = new URLSearchParams(window.location.search).get("itemId")
    if (itemId) highlightItem(itemId)
  }, [highlightItem])

  // Already on this page: a same-route push doesn't remount, so react to the header's deep-link event.
  useEffect(() => {
    const handler = (e: Event) => {
      const id = (e as CustomEvent<{ itemId?: string }>).detail?.itemId
      if (id) highlightItem(id)
    }
    window.addEventListener("clinic:deeplink", handler)
    return () => window.removeEventListener("clinic:deeplink", handler)
  }, [highlightItem])

  // The highlight is a transient "here it is" cue — clear it after a few seconds so the row doesn't
  // keep a stuck selection look for the lifetime of the page.
  useEffect(() => {
    if (!highlightItemId) return
    const t = setTimeout(() => setHighlightItemId(null), 4000)
    return () => clearTimeout(t)
  }, [highlightItemId])

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
                  <h1 className="text-3xl font-semibold text-foreground">Gestion du stock</h1>
                  <p className="mt-1 text-sm text-muted-foreground">Gérez les fournitures médicales et l&apos;inventaire</p>
                </div>

                <Button onClick={handleAddNew} className="gap-2">
                  <Plus className="h-4 w-4" />
                  Ajouter un article
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
