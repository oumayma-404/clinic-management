"use client"

import { useCallback, useEffect, useState } from "react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { StockTable } from "@/components/stock-table"
import { StockItemFormModal } from "@/components/stock-item-form-modal"
import { StockExpirySettingsCard } from "@/components/stock-expiry-settings-card"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Plus } from "lucide-react"
import type { StockItemDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function StockPage() {
  const { user } = useSession()
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState<StockItemDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [highlightItemId, setHighlightItemId] = useState<string | null>(null)
  // Dashboard drill-through (« Stock bas » / « Périment bientôt »): ?filter=low|expiring pre-applies the matching
  // filter so the list shows exactly the items the card counted. An unknown value is ignored — a stale link lands on
  // the full list, never a broken state.
  const [initialFilter, setInitialFilter] = useState<"low" | "expiring" | undefined>()

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

  // On mount (cross-page navigation): read the query params. `filter` is read BEFORE highlightItem may clear the
  // query string, so a link carrying both still applies both.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const filter = params.get("filter")
    if (filter === "low" || filter === "expiring") setInitialFilter(filter)

    const itemId = params.get("itemId")
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
      <AppShell contentClassName="space-y-6">
        {/*
          `flex items-center justify-between` with no wrap put a ~190px button against the title on a 390px
          screen and neither could give way. Same shape as `/caisse`, which already had it right.

          No `zone` prop: `PageHeader` derives it from the route now (`lib/zones.ts` puts `/stock` in
          « Gestion »), and the hardcoded « Clinique » here disagreed with the rail — the exact drift the
          derivation was introduced to end.
        */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <PageHeader title="Stock" subtitle="Fournitures, lots et seuils de réapprovisionnement." />

          <Button onClick={handleAddNew} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter un article
          </Button>
        </div>

        {/* Stock Table */}
        <StockTable
          refreshKey={refreshKey}
          onEdit={handleEdit}
          onAdd={handleAddNew}
          highlightItemId={highlightItemId}
          initialFilter={initialFilter}
          // Remount when the arriving filter resolves, so StockTable's initial filter state actually takes
          // effect — it seeds useState, which a re-render alone would not revisit.
          key={initialFilter ?? "all"}
        />

        {/* Below the list, not above it: the list is what this page is for, and the window is set once and then
            left alone for months. Bumping `refreshKey` on save is what makes the « expire bientôt » column and
            the header counts re-read against the new window immediately (AC-20). */}
        <StockExpirySettingsCard
          isAdmin={user?.role === "admin"}
          onChanged={() => setRefreshKey((k) => k + 1)}
        />

        <StockItemFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingItem={editingItem}
          onSaved={() => setRefreshKey((k) => k + 1)}
        />
      </AppShell>
    </ClinicGuard>
  )
}
