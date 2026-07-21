"use client"

import { useState } from "react"
import { useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { MedicationCatalogTable } from "@/components/medication-catalog-table"
import { MedicationFormModal } from "@/components/medication-form-modal"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft } from "lucide-react"
import type { MedicationDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function MedicationsPage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const isAdmin = user?.role === "admin"

  const [modalOpen, setModalOpen] = useState(false)
  const [editingMedication, setEditingMedication] = useState<MedicationDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleAdd = () => {
    setEditingMedication(null)
    setModalOpen(true)
  }

  const handleEdit = (medication: MedicationDto) => {
    setEditingMedication(medication)
    setModalOpen(true)
  }

  const handleSuccess = () => setRefreshKey((prev) => prev + 1)

  // Real-time: refetch when a catalog edit is broadcast (own clinic's session; global data).
  useClinicRealtime(RealtimeResource.Medications, handleSuccess)

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-auto p-4">
            {isLoading ? (
              <p className="p-8 text-center text-muted-foreground">Chargement…</p>
            ) : isAdmin ? (
              <div className="mx-auto max-w-7xl space-y-6">
                <MedicationCatalogTable
                  onEdit={handleEdit}
                  onAdd={handleAdd}
                  onChanged={handleSuccess}
                  reloadToken={refreshKey}
                />
              </div>
            ) : (
              // The medication catalog management screen is only reachable by an admin.
              <div className="flex min-h-full items-center justify-center p-6">
                <Card className="w-full max-w-md">
                  <CardHeader className="space-y-3 text-center">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-red-100 dark:bg-red-900/20">
                      <Lock className="h-7 w-7 text-red-600 dark:text-red-400" />
                    </div>
                    <CardTitle>Réservé aux administrateurs</CardTitle>
                    <CardDescription>
                      La gestion du catalogue des médicaments est réservée aux administrateurs de la clinique.
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
          </main>
        </div>

        <MedicationFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingMedication={editingMedication}
          onSuccess={handleSuccess}
        />
      </div>
    </ClinicGuard>
  )
}
