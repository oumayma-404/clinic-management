import { Card, CardContent } from "@/components/ui/card"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

interface StatsCardProps {
  title: string
  value: string
  icon: LucideIcon
  description: string
  variant?: "default" | "urgent"
  loading?: boolean
}

export function StatsCard({ title, value, icon: Icon, description, variant = "default", loading = false }: StatsCardProps) {
  return (
    <Card className={cn(variant === "urgent" && "border-destructive/50 bg-destructive/5")}>
      <CardContent className="p-6">
        <div className="flex items-center justify-between">
          <div className="space-y-1">
            <p className="text-sm font-medium text-muted-foreground">{title}</p>
            {loading ? (
              <span className="block h-8 w-16 animate-pulse rounded bg-muted" aria-label="Loading" />
            ) : (
              <p className="text-3xl font-semibold text-foreground">{value}</p>
            )}
            <p className="text-xs text-muted-foreground">{description}</p>
          </div>
          <div
            className={cn(
              "flex h-12 w-12 items-center justify-center rounded-lg",
              variant === "urgent" ? "bg-destructive/10" : "bg-accent",
            )}
          >
            <Icon className={cn("h-6 w-6", variant === "urgent" ? "text-destructive" : "text-accent-foreground")} />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
