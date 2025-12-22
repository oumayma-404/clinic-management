"use client"

import { useState, useCallback } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { Button } from "@/components/ui/button"
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs"
import { Plus } from "lucide-react"
import { AppointmentCalendar } from "@/components/appointment-calendar"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import { EditAppointmentDialog } from "@/components/edit-appointment-dialog"
import type { AppointmentDto } from "@/lib/api/types"
import { setHours, setMinutes } from "date-fns"

export default function AppointmentsPage() {
  const [view, setView] = useState<"day" | "week">("day")
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentDto | null>(null)
  const [selectedDate, setSelectedDate] = useState(new Date())
  const [refreshKey, setRefreshKey] = useState(0)

  const handleTimeSlotClick = useCallback((date: Date, time: string) => {
    const [hours, minutes] = time.split(':').map(Number)
    const dateWithTime = setMinutes(setHours(date, hours), minutes)
    setSelectedDate(dateWithTime)
    setDialogOpen(true)
  }, [])

  const handleAppointmentClick = useCallback((appointment: AppointmentDto) => {
    setSelectedAppointment(appointment)
    setEditDialogOpen(true)
  }, [])

  const handleAppointmentCreated = useCallback(() => {
    setRefreshKey(prev => prev + 1)
  }, [])

  const handleAppointmentUpdated = useCallback(() => {
    setRefreshKey(prev => prev + 1)
  }, [])

  return (
    <div className="flex h-screen bg-background">
      <DashboardSidebar />

      <div className="flex flex-1 flex-col overflow-hidden">
        <DashboardHeader />

        <main className="flex-1 overflow-hidden p-4">
          <div className="mx-auto h-full max-w-[1400px] flex flex-col">
            {/* View Tabs */}
            <Tabs value={view} onValueChange={(v) => setView(v as "day" | "week")} className="flex-1 flex flex-col min-h-0">
              <div className="flex items-center justify-between mb-3 flex-shrink-0">
                <TabsList>
                  <TabsTrigger value="day">Day View</TabsTrigger>
                  <TabsTrigger value="week">Week View</TabsTrigger>
                </TabsList>
                <Button onClick={() => setDialogOpen(true)} className="gap-2" size="sm">
                  <Plus className="h-4 w-4" />
                  New Appointment
                </Button>
              </div>

              <TabsContent value="day" className="flex-1 min-h-0 mt-0">
                <AppointmentCalendar 
                  key={refreshKey}
                  view="day" 
                  selectedDate={selectedDate} 
                  onDateChange={setSelectedDate}
                  onTimeSlotClick={handleTimeSlotClick}
                  onAppointmentClick={handleAppointmentClick}
                />
              </TabsContent>

              <TabsContent value="week" className="flex-1 min-h-0 mt-0">
                <AppointmentCalendar 
                  key={refreshKey}
                  view="week" 
                  selectedDate={selectedDate} 
                  onDateChange={setSelectedDate}
                  onTimeSlotClick={handleTimeSlotClick}
                  onAppointmentClick={handleAppointmentClick}
                />
              </TabsContent>
            </Tabs>
          </div>
        </main>
      </div>

      <CreateAppointmentDialog 
        open={dialogOpen} 
        onOpenChange={setDialogOpen} 
        defaultDate={selectedDate}
        defaultTime={selectedDate ? `${String(selectedDate.getHours()).padStart(2, '0')}:${String(selectedDate.getMinutes()).padStart(2, '0')}` : undefined}
        onSuccess={handleAppointmentCreated}
      />

      <EditAppointmentDialog
        open={editDialogOpen}
        onOpenChange={setEditDialogOpen}
        appointment={selectedAppointment}
        onSuccess={handleAppointmentUpdated}
      />
    </div>
  )
}
