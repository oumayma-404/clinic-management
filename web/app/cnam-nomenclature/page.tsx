"use client"

import { useState } from "react"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { CnamNomenclatureTable } from "@/components/cnam-nomenclature-table"
import { CnamEntryFormModal } from "@/components/cnam-entry-form-modal"
import { CnamLetterValuesCard } from "@/components/cnam-letter-values-card"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft } from "lucide-react"
import type { CnamNomenclatureEntryDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function CnamNomenclaturePage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const isAdmin = user?.role === "admin"

  const [modalOpen, setModalOpen] = useState(false)
  const [editingEntry, setEditingEntry] = useState<CnamNomenclatureEntryDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleAdd = () => {
    setEditingEntry(null)
    setModalOpen(true)
  }

  const handleEdit = (entry: CnamNomenclatureEntryDto) => {
    setEditingEntry(entry)
    setModalOpen(true)
  }

  const handleSuccess = () => setRefreshKey((prev) => prev + 1)

  // Real-time: refetch when a catalog/VLC edit is broadcast (own clinic's session; global data, R-10).
  useClinicRealtime(RealtimeResource.CnamNomenclature, handleSuccess)

  return (
    <ClinicGuard>
      {/* `width="none"`: each branch below owns its own width — the admin view centres a `max-w-7xl`, the
          refusal card centres itself against `<main>` via `min-h-full`. */}
      <AppShell width="none">
        {isLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : isAdmin ? (
          <div className="mx-auto max-w-7xl space-y-6">
            <CnamNomenclatureTable
              onEdit={handleEdit}
              onAdd={handleAdd}
              onChanged={handleSuccess}
              reloadToken={refreshKey}
            />
            <CnamLetterValuesCard onChanged={handleSuccess} reloadToken={refreshKey} />
          </div>
        ) : (
          // FR-5.4: the CNAM catalog management screen is only reachable by an admin.
          <div className="flex min-h-full items-center justify-center p-6">
            <Card className="w-full max-w-md">
              <CardHeader className="space-y-3 text-center">
                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-red-100 dark:bg-red-900/20">
                  <Lock className="h-7 w-7 text-red-600 dark:text-red-400" />
                </div>
                <CardTitle>Réservé aux administrateurs</CardTitle>
                <CardDescription>
                  La gestion de la nomenclature CNAM est réservée aux administrateurs de la clinique.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Button variant="outline" className="w-full gap-2" onClick={() => router.push("/")}>
                  <ArrowLeft className="h-4 w-4" />
                  Retour au tableau de bord
                </Button>
              </CardContent>
            </Card>
          </div>
        )}
        <CnamEntryFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingEntry={editingEntry}
          onSuccess={handleSuccess}
        />
      </AppShell>
    </ClinicGuard>
  )
}
