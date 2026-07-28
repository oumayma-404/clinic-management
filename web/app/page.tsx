"use client"

import { useEffect } from "react"
import { toast } from "sonner"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { StatsCard } from "@/components/stats-card"
import { AppointmentList } from "@/components/appointment-list"
import { Calendar, CalendarDays, Users, Clock, AlertCircle, Wallet, HandCoins } from "lucide-react"
import { ClinicGuard } from "@/components/clinic-guard"
import { useDashboardStats } from "@/lib/hooks/use-dashboard-stats"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { formatDT } from "@/lib/format"

export default function DashboardPage() {
  const { stats, loading, error, refetch } = useDashboardStats()

  // The dashboard KPIs are money and appointment counts, and it subscribed to nothing — so the first screen
  // everyone looks at was also the most reliably out of date.
  useClinicRealtime(
    [
      RealtimeResource.Appointments,
      RealtimeResource.Patients,
      RealtimeResource.Invoices,
      RealtimeResource.TreatmentPlans,
    ],
    refetch,
  )

  useEffect(() => {
    if (error) {
      toast.error(error)
    }
  }, [error])

  const display = (value?: number) => (value === undefined ? "—" : value.toLocaleString())

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Title */}
              <div>
                <h1 className="text-3xl font-semibold text-foreground">Tableau de bord</h1>
                <p className="mt-1 text-sm text-muted-foreground">Bon retour ! Voici l&apos;activité du jour.</p>
              </div>

              {/* Stats Grid — reflows across breakpoints so the seven cards stay readable on a laptop
                  (was a fixed 7-wide row). */}
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                <StatsCard
                  title="Rendez-vous du jour"
                  value={display(stats?.todaysAppointments)}
                  loading={loading}
                  icon={Calendar}
                  description="Prévus aujourd'hui"
                  href="/appointments"
                />
                <StatsCard
                  title="Total patients"
                  value={display(stats?.totalPatients)}
                  loading={loading}
                  icon={Users}
                  description="Patients enregistrés"
                  href="/patients"
                />
                <StatsCard
                  title="En attente"
                  value={display(stats?.upcomingPending)}
                  loading={loading}
                  icon={Clock}
                  description="En attente de confirmation"
                  href="/appointments"
                />
                <StatsCard
                  title="Cette semaine"
                  value={display(stats?.thisWeekAppointments)}
                  loading={loading}
                  icon={CalendarDays}
                  description="Rendez-vous cette semaine"
                  href="/appointments"
                />
                <StatsCard
                  title="Urgents"
                  value={display(stats?.urgentPatients)}
                  loading={loading}
                  icon={AlertCircle}
                  description="Patients signalés"
                  variant="urgent"
                  href="/patients?flagged=1"
                />
                <StatsCard
                  title="Recettes (mois)"
                  value={stats ? formatDT(stats.monthlyRevenueCollected) : "—"}
                  loading={loading}
                  icon={Wallet}
                  description="Encaissé ce mois-ci"
                  href="/factures"
                />
                <StatsCard
                  title="Créances"
                  value={stats ? formatDT(stats.totalOutstanding) : "—"}
                  loading={loading}
                  icon={HandCoins}
                  description="En attente de recouvrement"
                  href="/creances"
                />
              </div>

              {/* Main Content Grid */}
              <div className="grid gap-6">
                <AppointmentList />
              </div>
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}
