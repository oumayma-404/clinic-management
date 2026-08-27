"use client"

import { useState } from "react"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { DentalActsTable } from "@/components/dental-acts-table"
import { DentalActFormModal } from "@/components/dental-act-form-modal"
import { PageHeader } from "@/components/ui/page-header"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft } from "lucide-react"
import type { DentalActDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function DentalActsPage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const isAdmin = user?.role === "admin"

  const [modalOpen, setModalOpen] = useState(false)
  const [editingAct, setEditingAct] = useState<DentalActDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleAdd = () => {
    setEditingAct(null)
    setModalOpen(true)
  }

  const handleEdit = (act: DentalActDto) => {
    setEditingAct(act)
    setModalOpen(true)
  }

  const handleSuccess = () => setRefreshKey((prev) => prev + 1)

  // Real-time: refetch when a catalog edit is broadcast (own clinic's session).
  useClinicRealtime(RealtimeResource.DentalActs, handleSuccess)

  return (
    <ClinicGuard>
      {/*
        The admin view uses the shell's own `max-w-7xl` + gutter now; it used to pass `width="none"` and then
        re-declare `mx-auto max-w-7xl` inside, handing back the wrapper it had just opted out of.
        `width="none"` survives for the refusal card alone, whose `min-h-full` centring resolves against
        `<main>` and collapses the moment an auto-height wrapper is inserted around it.
      */}
      <AppShell width={isAdmin ? "7xl" : "none"} gutter={isAdmin} contentClassName={isAdmin ? "space-y-6" : undefined}>
        {isLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : isAdmin ? (
          <>
            {/* The route had no page title at all. No `zone` prop — derived from the route. */}
            <PageHeader
              title="Actes dentaires"
              subtitle="Le catalogue d'actes qui alimente le sélecteur des devis et des notes d'honoraires."
            />
            <DentalActsTable
              onEdit={handleEdit}
              onAdd={handleAdd}
              onChanged={handleSuccess}
              reloadToken={refreshKey}
            />
          </>
        ) : (
          // La gestion du catalogue des actes est réservée aux administrateurs.
          <div className="flex min-h-full items-center justify-center p-6">
            <Card className="w-full max-w-md">
              <CardHeader className="space-y-3 text-center">
                {/* Tokens, not `red-*` literals — and no hand-maintained `dark:` twin, since
                    `--destructive-wash` / `--destructive` already carry both themes. */}
                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-destructive-wash">
                  <Lock className="h-7 w-7 text-destructive" />
                </div>
                <CardTitle>Réservé aux administrateurs</CardTitle>
                <CardDescription>
                  La gestion du catalogue des actes dentaires est réservée aux administrateurs du cabinet.
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

        <DentalActFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingAct={editingAct}
          onSuccess={handleSuccess}
        />
      </AppShell>
    </ClinicGuard>
  )
}
