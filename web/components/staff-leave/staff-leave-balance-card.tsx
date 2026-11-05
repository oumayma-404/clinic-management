"use client"

import type React from "react"

import type { StaffLeaveBalanceDto } from "@/lib/api/team"
import { formatStaffDays, staffDaysLabel, staffDaysShort } from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

/**
 * The year's congé payé: what is left in large, then the allowance, what was taken and what is waiting, and a bar
 * of the same three. With no allowance set it states what was taken — never a balance it cannot know.
 */
export function StaffLeaveBalanceCard({
  balance,
  action,
  className,
}: {
  balance: StaffLeaveBalanceDto
  action?: React.ReactNode
  className?: string
}) {
  const { year, allowance, taken, remaining, pending } = balance
  const hasAllowance = allowance !== null && allowance > 0
  const share = (days: number) => (hasAllowance ? `${Math.min(100, Math.max(0, (days / (allowance as number)) * 100))}%` : "0%")

  const stats: Array<{ label: string; value: string; tone?: string }> = [
    ...(allowance !== null ? [{ label: "Droit annuel", value: staffDaysShort(allowance) }] : []),
    { label: "Pris", value: staffDaysShort(taken) },
    { label: "En attente", value: staffDaysShort(pending), tone: pending > 0 ? "text-warning-ink" : undefined },
  ]

  return (
    <section aria-label={`Congé payé ${year}`} className={cn("space-y-5 rounded-xl border bg-card p-5 sm:p-6", className)}>
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div className="space-y-1.5">
          <p className="text-sm text-muted-foreground">Congé payé · {year}</p>
          <p className="flex items-baseline gap-2">
            <span className="text-4xl leading-none font-semibold tracking-tight tabular-nums">
              {formatStaffDays(remaining ?? taken)}
            </span>
            <span className="text-base text-foreground/80">
              {remaining !== null ? (Math.abs(remaining) > 1 ? "jours restants" : "jour restant") : taken > 1 ? "jours pris" : "jour pris"}
            </span>
          </p>
        </div>
        {action}
      </div>

      <dl className="grid auto-cols-fr grid-flow-col divide-x overflow-hidden rounded-lg border">
        {stats.map((stat) => (
          <div key={stat.label} className="min-w-0 px-3 py-2.5 sm:px-4">
            <dt className="text-xs leading-tight text-muted-foreground">{stat.label}</dt>
            <dd className={cn("text-lg font-semibold tabular-nums", stat.tone)}>{stat.value}</dd>
          </div>
        ))}
      </dl>

      {hasAllowance && (
        <div className="space-y-2">
          <div
            role="img"
            aria-label={`${staffDaysLabel(taken)} pris, ${staffDaysLabel(pending)} en attente, sur ${staffDaysLabel(allowance as number)}`}
            className="flex h-2.5 gap-0.5 overflow-hidden rounded-full bg-muted"
          >
            <span className="h-full rounded-s-full bg-primary" style={{ width: share(taken) }} />
            {pending > 0 && <span className="h-full bg-warning" style={{ width: share(pending) }} />}
          </div>
          <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span className="inline-flex items-center gap-1.5">
              <span aria-hidden="true" className="size-2 rounded-sm bg-primary" />
              Pris
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span aria-hidden="true" className="size-2 rounded-sm bg-warning" />
              En attente
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span aria-hidden="true" className="size-2 rounded-sm bg-muted-foreground/30" />
              Restants
            </span>
          </div>
        </div>
      )}
    </section>
  )
}
