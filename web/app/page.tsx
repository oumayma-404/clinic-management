import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { StatsCard } from "@/components/stats-card"
import { AppointmentList } from "@/components/appointment-list"
import { NotificationsList } from "@/components/notifications-list"
import { Calendar, Users, Clock, AlertCircle } from "lucide-react"

export default function DashboardPage() {
  return (
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
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
              <StatsCard title="Today's Appointments" value="12" icon={Calendar} description="+2 from yesterday" />
              <StatsCard title="Total Patients" value="1,248" icon={Users} description="+8 new this week" />
              <StatsCard title="Pending" value="5" icon={Clock} description="Awaiting confirmation" />
              <StatsCard
                title="Urgent"
                value="2"
                icon={AlertCircle}
                description="Require immediate attention"
                variant="urgent"
              />
            </div>

            {/* Main Content Grid */}
            <div className="grid gap-6 lg:grid-cols-3">
              <div className="lg:col-span-2">
                <AppointmentList />
              </div>
              <div>
                <NotificationsList />
              </div>
            </div>
          </div>
        </main>
      </div>
    </div>
  )
}
