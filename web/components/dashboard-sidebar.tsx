"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { cn } from "@/lib/utils"
import { Calendar, Users, FileText, Settings, LayoutDashboard, Stethoscope, Package } from "lucide-react"

const navigation = [
  { name: "Dashboard", href: "/", icon: LayoutDashboard },
  { name: "Appointments", href: "/appointments", icon: Calendar },
  { name: "Patients", href: "/patients", icon: Users },
  { name: "Medical Records", href: "/records", icon: FileText },
  { name: "Stock", href: "/stock", icon: Package },
  { name: "Doctors", href: "/doctors", icon: Stethoscope },
  { name: "Settings", href: "/settings", icon: Settings },
]

export function DashboardSidebar() {
  const pathname = usePathname()

  return (
    <aside className="flex w-64 flex-col border-r border-border bg-card">
      <div className="flex h-16 items-center border-b border-border px-6">
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary">
            <Stethoscope className="h-5 w-5 text-primary-foreground" />
          </div>
          <span className="text-lg font-semibold text-foreground">MediCare Clinic</span>
        </div>
      </div>

      <nav className="flex-1 space-y-1 p-4">
        {navigation.map((item) => {
          const isActive = pathname === item.href
          return (
            <Link
              key={item.name}
              href={item.href}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
                isActive
                  ? "bg-accent text-accent-foreground"
                  : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
              )}
            >
              <item.icon className="h-5 w-5" />
              {item.name}
            </Link>
          )
        })}
      </nav>

      <div className="border-t border-border p-4">
        <div className="text-xs text-muted-foreground">
          <p className="font-medium">Clinic Hours</p>
          <p className="mt-1">Mon-Fri: 8:00 AM - 6:00 PM</p>
          <p>Sat: 9:00 AM - 2:00 PM</p>
        </div>
      </div>
    </aside>
  )
}
