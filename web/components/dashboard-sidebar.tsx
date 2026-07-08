"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { cn } from "@/lib/utils"
import { Calendar, Users, FileText, Settings, LayoutDashboard, Stethoscope, Package, FileCheck, ChevronLeft, ChevronRight, FolderOpen, UserCog } from "lucide-react"
import { useSidebar } from "@/contexts/sidebar-context"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"

const navigation = [
  { name: "Dashboard", href: "/", icon: LayoutDashboard },
  { name: "Appointments", href: "/appointments", icon: Calendar },
  { name: "Patients", href: "/patients", icon: Users },
  { name: "Procedure Types", href: "/procedure-types", icon: Stethoscope },
  { name: "Medical Records", href: "/records", icon: FileText },
  { name: "Documents", href: "/documents", icon: FileCheck },
  { name: "Files", href: "/files", icon: FolderOpen },
  { name: "Stock", href: "/stock", icon: Package },
  { name: "Settings", href: "/settings", icon: Settings },
]

// Admin-only entry, shown only for local-mode admins (offline user management — AC-5.4).
const adminNavItem = { name: "Users", href: "/users", icon: UserCog }

export function DashboardSidebar() {
  const pathname = usePathname()
  const { isCollapsed, toggleSidebar } = useSidebar()
  const { user, mode } = useSession()

  const navItems =
    mode === "local" && user?.role === "admin" ? [...navigation, adminNavItem] : navigation

  return (
    <aside
      className={cn(
        "flex flex-col border-r border-border bg-card transition-all duration-300 relative",
        isCollapsed ? "w-16" : "w-64"
      )}
    >
      {/* Header */}
      <div className="flex h-16 items-center border-b border-border px-4">
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary shrink-0">
            <Stethoscope className="h-5 w-5 text-primary-foreground" />
          </div>
          {!isCollapsed && (
            <span className="text-lg font-semibold text-foreground truncate">MediCare Clinic</span>
          )}
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1 p-4">
        <TooltipProvider>
          {navItems.map((item) => {
            const isActive = pathname === item.href
            const linkContent = (
              <Link
                href={item.href}
                className={cn(
                  "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
                  isActive
                    ? "bg-accent text-accent-foreground"
                    : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
                  isCollapsed && "justify-center"
                )}
              >
                <item.icon className="h-5 w-5 shrink-0" />
                {!isCollapsed && <span className="truncate">{item.name}</span>}
              </Link>
            )

            if (isCollapsed) {
              return (
                <Tooltip key={item.href} delayDuration={0}>
                  <TooltipTrigger asChild>
                    {linkContent}
                  </TooltipTrigger>
                  <TooltipContent side="right">
                    <p>{item.name}</p>
                  </TooltipContent>
                </Tooltip>
              )
            }

            return <div key={item.href}>{linkContent}</div>
          })}
        </TooltipProvider>
      </nav>

      {/* Footer */}
      {!isCollapsed && (
        <div className="border-t border-border p-4">
          <div className="text-xs text-muted-foreground">
            <p className="font-medium">Clinic Hours</p>
            <p className="mt-1">Mon-Fri: 8:00 AM - 6:00 PM</p>
            <p>Sat: 9:00 AM - 2:00 PM</p>
          </div>
        </div>
      )}

      {/* Toggle Button */}
      <div className="border-t border-border p-2">
        <Button
          variant="ghost"
          size="sm"
          onClick={toggleSidebar}
          className="w-full justify-center"
          aria-label={isCollapsed ? "Expand sidebar" : "Collapse sidebar"}
        >
          {isCollapsed ? (
            <ChevronRight className="h-4 w-4" />
          ) : (
            <ChevronLeft className="h-4 w-4" />
          )}
        </Button>
      </div>
    </aside>
  )
}
