import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Bell, AlertCircle, Calendar } from "lucide-react"

const notifications = [
  {
    id: 1,
    title: "Urgent: Lab Results Ready",
    description: "Patient John Anderson's lab results are ready for review.",
    time: "5 min ago",
    type: "urgent",
    icon: AlertCircle,
  },
  {
    id: 2,
    title: "New Appointment Request",
    description: "Emily Roberts requested an appointment for tomorrow.",
    time: "15 min ago",
    type: "info",
    icon: Calendar,
  },
  {
    id: 3,
    title: "Appointment Reminder",
    description: "Michael Chen's appointment is in 30 minutes.",
    time: "30 min ago",
    type: "reminder",
    icon: Bell,
  },
  {
    id: 4,
    title: "Patient Checked In",
    description: "Sarah Williams has checked in for her 2:00 PM appointment.",
    time: "1 hour ago",
    type: "info",
    icon: Bell,
  },
]

export function NotificationsList() {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Bell className="h-5 w-5" />
          Notifications
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="space-y-4">
          {notifications.map((notification) => (
            <div
              key={notification.id}
              className="flex gap-3 rounded-lg border border-border bg-card p-3 transition-colors hover:bg-accent/50"
            >
              <div
                className={`flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full ${
                  notification.type === "urgent" ? "bg-destructive/10" : "bg-accent"
                }`}
              >
                <notification.icon
                  className={`h-4 w-4 ${
                    notification.type === "urgent" ? "text-destructive" : "text-accent-foreground"
                  }`}
                />
              </div>
              <div className="flex-1 space-y-1">
                <p className="text-sm font-medium text-foreground">{notification.title}</p>
                <p className="text-xs text-muted-foreground">{notification.description}</p>
                <p className="text-xs text-muted-foreground">{notification.time}</p>
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
