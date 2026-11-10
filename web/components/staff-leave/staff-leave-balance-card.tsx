"use client"

import type React from "react"

import type { StaffLeaveBalanceDto } from "@/lib/api/team"
import { todayLocalIso } from "@/lib/format"
import {
  formatStaffDays,
  leaveRuleLine,
  nextCreditLabel,
  staffDaysLabel,
  staffDaysShort,
  staffDaysSigned,
} from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

/** One term of « Acquis − Pris − Prévus = Reste »; the operator travels with its term so a wrap keeps them together. */
function Term({
  operator,
  label,
  value,
  note,
  tone,
}: {
  operator?: string
  label: string
  value: string
  note?: string
  tone?: string
}) {
  return (
    <li className="min-w-0">
      <p className="text-xs leading-tight text-muted-foreground">{label}</p>
      <p className={cn("text-lg leading-7 font-semibold tabular-nums", tone)}>
        {operator && (
          <span aria-hidden="true" className="me-2 font-normal text-muted-foreground">
            {operator}
          </span>
        )}
        {value}
      </p>
      {note && <p className="max-w-48 text-xs text-muted-foreground">{note}</p>}
    </li>
  )
}

/**
 * The year's congé payé, read today: what is left in large, then Acquis − Pris − Prévus = Reste, the waiting days
 * apart beside the bar's amber segment, what the rule still adds and what will be lost on 31/12, the rule (and the
 * next one), and every day taken since the start. With nothing set for her it states what was taken — never a
 * balance the cabinet did not give.
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
  const {
    year,
    acquired,
    taken,
    planned,
    pending,
    expired,
    upcoming,
    remaining,
    rule,
    nextRule,
    nextCredit,
    takenTotal,
    lostAtYearEnd,
    lostLastYear,
    takenInOpening,
  } = balance
  const known = remaining !== null
  const over = known && remaining < 0
  const left = known ? Math.abs(remaining) : taken
  const scale = Math.max(acquired, taken + planned + pending, 0)
  const share = (days: number) => (scale > 0 ? `${Math.min(100, Math.max(0, (days / scale) * 100))}%` : "0%")
  const ruleText = leaveRuleLine(rule, nextRule, todayLocalIso())
  const takenNote =
    takenInOpening && takenInOpening > 0
      ? `dont ${staffDaysShort(takenInOpening)} ${balance.opening !== null ? "inclus dans le solde de départ" : "avant le début du droit à congé"}`
      : undefined

  return (
    <section aria-label={`Congé payé ${year}`} className={cn("space-y-5 rounded-xl border bg-card p-5 sm:p-6", className)}>
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div className="space-y-1.5">
          <p className="text-sm text-muted-foreground">Congé payé · {year}</p>
          <p className={cn("flex flex-wrap items-baseline gap-x-2", over && "text-destructive")}>
            <span className="text-4xl leading-none font-semibold tracking-tight tabular-nums">{formatStaffDays(left)}</span>
            <span className={cn("text-base", !over && "text-foreground/80")}>
              {(left > 1 ? "jours " : "jour ") + (!known ? "pris" : over ? "de dépassement" : left > 1 ? "restants" : "restant")}
            </span>
          </p>
        </div>
        {action}
      </div>

      {known ? (
        <ul aria-label="Calcul du solde" className="flex flex-wrap items-start gap-x-3 gap-y-3">
          <Term label="Acquis" value={staffDaysShort(acquired)} />
          <Term operator="−" label="Pris" value={staffDaysShort(taken)} note={takenNote} />
          {planned > 0 && <Term operator="−" label="Prévus" value={staffDaysShort(planned)} />}
          {expired > 0 && <Term operator="−" label="Perdus au 31/12" value={staffDaysShort(expired)} />}
          <Term
            operator="="
            label={over ? "Dépassement" : "Reste"}
            value={staffDaysShort(left)}
            tone={over ? "text-destructive" : undefined}
          />
        </ul>
      ) : (
        <ul className="flex flex-wrap gap-x-6 gap-y-3">
          <Term label="Pris" value={staffDaysShort(taken)} note={takenNote} />
          {planned > 0 && <Term label="Prévus" value={staffDaysShort(planned)} />}
          {pending > 0 && <Term label="En attente" value={staffDaysShort(pending)} tone="text-warning-ink" />}
        </ul>
      )}

      {known && scale > 0 && (
        <div className="space-y-2">
          <div
            role="img"
            aria-label={`${staffDaysLabel(taken + planned)} pris ou prévus, ${staffDaysLabel(pending)} en attente, sur ${staffDaysLabel(acquired)} acquis`}
            className="flex h-2.5 gap-0.5 overflow-hidden rounded-full bg-muted"
          >
            <span className="h-full rounded-s-full bg-primary" style={{ width: share(taken + planned) }} />
            {pending > 0 && <span className="h-full bg-warning" style={{ width: share(pending) }} />}
          </div>
          {pending > 0 && (
            <p className="flex items-center justify-end gap-1.5 text-sm">
              <span aria-hidden="true" className="size-2.5 rounded-sm bg-warning" />
              En attente : <span className="font-semibold text-warning-ink tabular-nums">{staffDaysShort(pending)}</span>
            </p>
          )}
        </div>
      )}

      <ul className="space-y-1 text-sm text-muted-foreground">
        {lostAtYearEnd !== null && lostAtYearEnd > 0 && (
          <li className="font-medium text-warning-ink">
            À prendre avant le 31/12 : <span className="tabular-nums">{staffDaysShort(lostAtYearEnd)}</span>
          </li>
        )}
        {known && upcoming > 0 && (
          <li>
            À venir d&apos;ici le 31/12 : <span className="font-medium text-foreground tabular-nums">{staffDaysSigned(upcoming)}</span>
          </li>
        )}
        {lostLastYear !== null && lostLastYear > 0 && (
          <li>
            {staffDaysShort(lostLastYear)} non pris en {year - 1} : perdus
          </li>
        )}
        <li>
          {ruleText ? (
            <>
              Droit : <span className="text-foreground">{ruleText}</span>
              {nextCredit && <> · prochain : {nextCreditLabel(nextCredit)}</>}
            </>
          ) : (
            "Aucun droit à congé défini."
          )}
        </li>
        <li>
          Total pris depuis le début : <span className="font-medium text-foreground tabular-nums">{staffDaysShort(takenTotal)}</span>
        </li>
      </ul>
    </section>
  )
}
