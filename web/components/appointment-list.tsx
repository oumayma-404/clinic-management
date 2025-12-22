import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Clock } from "lucide-react"

const appointments = [
  {
    id: 1,
    patientName: "John Anderson",
    time: "09:00 AM",
    type: "General Checkup",
    status: "confirmed",
    initials: "JA",
  },
  {
    id: 2,
    patientName: "Emily Roberts",
    time: "10:00 AM",
    type: "Follow-up",
    status: "confirmed",
    initials: "ER",
  },
  {
    id: 3,
    patientName: "Michael Chen",
    time: "11:00 AM",
    type: "Consultation",
    status: "pending",
    initials: "MC",
  },
  {
    id: 4,
    patientName: "Sarah Williams",
    time: "02:00 PM",
    type: "Lab Results",
    status: "confirmed",
    initials: "SW",
  },
  {
    id: 5,
    patientName: "David Brown",
    time: "03:30 PM",
    type: "General Checkup",
    status: "pending",
    initials: "DB",
  },
]

export function AppointmentList() {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Clock className="h-5 w-5" />
          Today's Appointments
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="space-y-4">
          {appointments.map((appointment) => (
            <div
              key={appointment.id}
              className="flex items-center justify-between rounded-lg border border-border bg-card p-4 transition-colors hover:bg-accent/50"
            >
              <div className="flex items-center gap-4">
                <Avatar className="h-10 w-10">
                  <AvatarFallback className="bg-accent text-accent-foreground">{appointment.initials}</AvatarFallback>
                </Avatar>
                <div>
                  <p className="font-medium text-foreground">{appointment.patientName}</p>
                  <p className="text-sm text-muted-foreground">{appointment.type}</p>
                </div>
              </div>
              <div className="flex items-center gap-4">
                <span className="text-sm font-medium text-foreground">{appointment.time}</span>
                <Badge variant={appointment.status === "confirmed" ? "default" : "secondary"} className="capitalize">
                  {appointment.status}
                </Badge>
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
