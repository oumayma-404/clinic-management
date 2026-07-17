"use client"

import { useEffect } from "react"
import { toast } from "sonner"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { StatsCard } from "@/components/stats-card"
import { AppointmentList } from "@/components/appointment-list"
import { Calendar, CalendarDays, Users, Clock, AlertCircle, Wallet } from "lucide-react"
import { ClinicGuard } from "@/components/clinic-guard"
import { useDashboardStats } from "@/lib/hooks/use-dashboard-stats"
import { formatDT } from "@/lib/format"

export default function DashboardPage() {
  const { stats, loading, error } = useDashboardStats()

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

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Title */}
              <div>
                <h1 className="text-3xl font-semibold text-foreground">Dashboard</h1>
                <p className="mt-1 text-sm text-muted-foreground">Welcome back! Here's what's happening today.</p>
              </div>

              {/* Stats Grid */}
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-6">
                <StatsCard
                  title="Today's Appointments"
                  value={display(stats?.todaysAppointments)}
                  loading={loading}
                  icon={Calendar}
                  description="Scheduled for today"
                />
                <StatsCard
                  title="Total Patients"
                  value={display(stats?.totalPatients)}
                  loading={loading}
                  icon={Users}
                  description="Registered patients"
                />
                <StatsCard
                  title="Pending"
                  value={display(stats?.upcomingPending)}
                  loading={loading}
                  icon={Clock}
                  description="Awaiting confirmation"
                />
                <StatsCard
                  title="This Week"
                  value={display(stats?.thisWeekAppointments)}
                  loading={loading}
                  icon={CalendarDays}
                  description="Appointments this week"
                />
                <StatsCard
                  title="Urgent"
                  value={display(stats?.urgentPatients)}
                  loading={loading}
                  icon={AlertCircle}
                  description="Flagged patients"
                  variant="urgent"
                />
                <StatsCard
                  title="Recettes (mois)"
                  value={stats ? formatDT(stats.monthlyRevenueCollected) : "—"}
                  loading={loading}
                  icon={Wallet}
                  description="Encaissé ce mois-ci"
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
