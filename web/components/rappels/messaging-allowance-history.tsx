"use client"

import { Card, CardContent } from "@/components/ui/card"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import type { ReminderAllowanceHistoryDto, ReminderAllowanceMonthDto } from "@/lib/api/reminder-allowance"

/**
 * The twelve preceding Tunisian months and the current one, newest first (AC-2.3).
 *
 * <p><b>A table AND a card list, not a table that reflows</b> (§ 6 of the device contract): a
 * <code>display:block</code> table strips the implicit row and cell roles, so a screen reader would announce
 * « juillet 2026 200 143 57 » with no field names — over the figures a practice reconciles a bill against.</p>
 *
 * <p><b>⚠️ « 0 rappel envoyé » and « non mesuré » are different cells, and that is this component's whole
 * responsibility</b> (AC-2.4). A measured zero is a fact about the practice — a quiet month — and rendering it as
 * « non mesuré » would tell a careful cabinet every month that its own record is unreadable. The reverse is worse:
 * a gap rendered as <code>0</code> claims the practice sent nothing when the truth is that we did not count.</p>
 *
 * <p>⚠️ <b>A month below the D-5 floor is simply absent from <code>months</code></b> — the server omits it. So this
 * component never has to distinguish « before the cabinet existed » from « we failed to count »: it only ever sees
 * months somebody promised to count.</p>
 */
export function MessagingAllowanceHistory({
  data,
  loading,
  error,
  onRetry,
}: {
  data: ReminderAllowanceHistoryDto | null
  loading: boolean
  /** Set when the read failed. Rendered as a retry notice — **never** as an empty or zeroed table. */
  error: string | null
  onRetry: () => void
}) {
  const months = data?.months ?? []

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 p-4 sm:p-5">
        <div>
          <h2 className="text-base font-semibold">Historique mensuel</h2>
          <p className="mt-0.5 text-sm text-muted-foreground">
            Ce mois-ci et les douze précédents, du plus récent au plus ancien.
          </p>
        </div>

        {error ? (
          <LoadFailureNotice
            message="L'historique du forfait n'a pas pu être lu."
            detail="Aucun chiffre n'est affiché — un tableau vide se lirait comme « rien envoyé »."
            onRetry={onRetry}
          />
        ) : (
          <>
            {/* Cards below `md:`, the four-column table above it. Four columns fit a tablet portrait comfortably,
                so this is the ordinary hinge rather than `lg:`. */}
            <div className={CARDS_ONLY}>
              <CardList
                items={months}
                getKey={(m) => m.month}
                title={(m) => m.monthLabel}
                fields={(m) => [
                  { label: "Envoyés", value: consumedLabel(m) },
                  { label: "Forfait", value: allowanceLabel(m) },
                  { label: "Restant", value: remainingLabel(m) },
                ]}
                loading={loading}
                skeletonRows={4}
                empty="Aucun mois mesuré pour l'instant."
                ariaLabel="Historique mensuel du forfait de rappels WhatsApp"
              />
            </div>

            <div className={TABLE_ONLY}>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Mois</TableHead>
                    <TableHead className="text-end">Forfait</TableHead>
                    <TableHead className="text-end">Envoyés</TableHead>
                    <TableHead className="text-end">Restant</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {loading ? (
                    [0, 1, 2, 3].map((i) => (
                      <TableRow key={i}>
                        <TableCell colSpan={4}>
                          <span className="block h-3.5 animate-pulse rounded bg-muted" />
                        </TableCell>
                      </TableRow>
                    ))
                  ) : months.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={4} className="text-sm text-muted-foreground">
                        Aucun mois mesuré pour l&apos;instant.
                      </TableCell>
                    </TableRow>
                  ) : (
                    months.map((m) => (
                      <TableRow key={m.month}>
                        <TableCell className="font-medium">{m.monthLabel}</TableCell>
                        <TableCell className="text-end tabular-nums">{allowanceLabel(m)}</TableCell>
                        <TableCell className="text-end tabular-nums">{consumedLabel(m)}</TableCell>
                        <TableCell className="text-end tabular-nums">{remainingLabel(m)}</TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * AC-2.4 — the one place « 0 rappel envoyé » and « non mesuré » are told apart.
 *
 * A measured zero is spelled out in words rather than left as a bare « 0 », because at a glance in a column of
 * numbers a zero and an unmeasured month look identical, and the two mean opposite things.
 */
function consumedLabel(m: ReminderAllowanceMonthDto): string {
  if (!m.measured || m.consumed === null) return "Non mesuré"
  if (m.consumed === 0) return "0 rappel envoyé"
  return `${m.consumed.toLocaleString("fr-TN")} ${m.consumed === 1 ? "rappel" : "rappels"}`
}

function allowanceLabel(m: ReminderAllowanceMonthDto): string {
  return m.measured && m.allowance !== null ? m.allowance.toLocaleString("fr-TN") : "—"
}

/** Derived here rather than sent: the server already floors it for the current month, and this is the same rule. */
function remainingLabel(m: ReminderAllowanceMonthDto): string {
  if (!m.measured || m.allowance === null || m.consumed === null) return "—"
  return Math.max(0, m.allowance - m.consumed).toLocaleString("fr-TN")
}
