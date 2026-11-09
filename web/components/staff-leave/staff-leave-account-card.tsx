"use client"

import type React from "react"

import { useEffect, useMemo, useRef, useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { Minus, Plus, Settings2, Wallet, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { StaffLeaveSectionTitle } from "@/components/staff-leave/staff-leave-bits"
import type { StaffFileDto, StaffLeaveAccrual, StaffLeaveBalanceDto } from "@/lib/api/team"
import { teamApi } from "@/lib/api/team"
import { dayFromKey } from "@/lib/doctor-leaves"
import { getErrorMessage } from "@/lib/errors"
import { formatDateTime, quoteFr, todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  MAX_STAFF_LEAVE_REASON,
  allowanceEntryLabel,
  formatStaffDays,
  isOverdrawn,
  leaveRuleLine,
  nextCreditLabel,
  parseStaffDays,
  staffBalanceShort,
  staffDaysShort,
} from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

/** A month's credit cannot exceed the days in a month (`StaffLeaveAllowanceEntry.MaxMonthlyDays`). */
const MAX_MONTHLY_DAYS = 31

type Open = "rule" | "opening" | "adjust" | null

/**
 * « Droit à congé » on a secretary's file (admin): the rule, the balance fixed by hand, days added or removed, and the
 * history of the rules — each struck through with a motif rather than erased. A fixed balance or an adjustment is
 * cancelled from the relevé, where the year stepper reaches every year. What she has acquired, taken and lost is
 * never typed here: the server folds it from these lines and her congés.
 */
export function StaffLeaveAccountCard({ file, onChanged }: { file: StaffFileDto; onChanged: () => void }) {
  const [open, setOpen] = useState<Open>(null)
  const [cancelling, setCancelling] = useState<CancellableLine | null>(null)
  const { rule, nextRule } = file.balance
  const ruleLine = leaveRuleLine(rule, nextRule, todayLocalIso())
  const opening = file.balance.opening !== null && file.balance.openingDay ? file.balance : null
  const rules = file.entries.filter((entry) => entry.kind === "Rule")

  return (
    <section aria-labelledby="staff-leave-account" className="space-y-4 rounded-xl border bg-card p-5 sm:p-6">
      <StaffLeaveSectionTitle id="staff-leave-account">Droit à congé</StaffLeaveSectionTitle>
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">Règle</dt>
          <dd className="[overflow-wrap:anywhere]">
            {ruleLine ?? <span className="text-muted-foreground">Aucune règle</span>}
          </dd>
        </div>
        {opening && (
          <div>
            <dt className="text-xs text-muted-foreground">Solde de départ</dt>
            <dd>
              {staffDaysShort(opening.opening as number)} au{" "}
              {format(dayFromKey(opening.openingDay as string), "d MMMM yyyy", { locale: fr })}
            </dd>
          </div>
        )}
      </dl>
      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          size="sm"
          className="gap-1.5 coarse:h-11"
          onClick={() => setOpen("rule")}
          data-guide="conges-rule"
        >
          <Settings2 className="size-4" aria-hidden="true" />
          {rule || nextRule ? "Modifier la règle" : "Définir la règle"}
        </Button>
        <Button variant="outline" size="sm" className="gap-1.5 coarse:h-11" onClick={() => setOpen("opening")}>
          <Wallet className="size-4" aria-hidden="true" />
          Fixer le solde
        </Button>
        <Button variant="outline" size="sm" className="gap-1.5 coarse:h-11" onClick={() => setOpen("adjust")}>
          <Plus className="size-4" aria-hidden="true" />
          Ajouter ou retirer des jours
        </Button>
      </div>

      {rules.length > 0 && (
        <div className="space-y-2">
          <h3 id="staff-leave-rules" className="text-xs text-muted-foreground">
            Historique des règles
          </h3>
          <ul className="divide-y rounded-lg border" aria-labelledby="staff-leave-rules">
            {rules.map((entry) => (
              <li key={entry.id} className="flex items-start gap-3 px-3 py-2.5">
                <div className="min-w-0 flex-1">
                  <p className={cn("text-sm [overflow-wrap:anywhere]", entry.cancelled && "text-muted-foreground line-through")}>
                    {allowanceEntryLabel(entry)}
                  </p>
                  <p className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
                    {entry.createdByName} · {formatDateTime(entry.createdAt)}
                    {entry.cancelled &&
                      ` · annulée par ${entry.cancelledByName ?? "?"}${entry.cancelReason ? ` : ${quoteFr(entry.cancelReason)}` : ""}`}
                  </p>
                </div>
                {!entry.cancelled && (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-8 shrink-0 coarse:size-11"
                    onClick={() => setCancelling({ id: entry.id, label: allowanceEntryLabel(entry) })}
                    aria-label={`Annuler ${quoteFr(allowanceEntryLabel(entry))}`}
                  >
                    <X className="size-4" aria-hidden="true" />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      <RuleDialog open={open === "rule"} onOpenChange={(o) => setOpen(o ? "rule" : null)} file={file} onSaved={onChanged} />
      <OpeningDialog open={open === "opening"} onOpenChange={(o) => setOpen(o ? "opening" : null)} file={file} onSaved={onChanged} />
      <AdjustDialog open={open === "adjust"} onOpenChange={(o) => setOpen(o ? "adjust" : null)} file={file} onSaved={onChanged} />
      <CancelEntryDialog entry={cancelling} onClose={() => setCancelling(null)} onSaved={onChanged} />
    </section>
  )
}

/** `YYYY-MM-01` keys from January of this year to December of next year — where a rule may start. */
function firstOfMonths(today: string): string[] {
  const year = Number(today.slice(0, 4))
  const months: string[] = []
  for (const y of [year, year + 1]) for (let m = 1; m <= 12; m++) months.push(`${y}-${String(m).padStart(2, "0")}-01`)
  return months
}

/** The next 1st after today (par mois) or the next 1 January (par an) — where a CHANGE of rule usually starts. */
function nextStart(accrual: StaffLeaveAccrual, today: string): string {
  const year = Number(today.slice(0, 4))
  const month = Number(today.slice(5, 7))
  if (accrual === "Year") return `${year + 1}-01-01`
  return month === 12 ? `${year + 1}-01-01` : `${year}-${String(month + 1).padStart(2, "0")}-01`
}

/** « 1er janvier 2027 » — a rule's first day as the picker lists it. */
const sinceLabel = (day: string) => format(dayFromKey(day), "d MMMM yyyy", { locale: fr }).replace(/^1 /, "1er ")

interface RuleSeed {
  days: string
  accrual: StaffLeaveAccrual
  carryOver: "lost" | "kept"
  since: string
}

/**
 * What the window opens on: the latest rule with its own day (« Modifier »), or — a FIRST rule — 1 January of this
 * year, so she has her droit for the year it is set in.
 */
function ruleSeed(balance: StaffLeaveBalanceDto, today: string): RuleSeed {
  const latest = balance.nextRule ?? balance.rule
  if (!latest) return { days: "", accrual: "Year", carryOver: "lost", since: `${today.slice(0, 4)}-01-01` }
  return {
    days: formatStaffDays(latest.days),
    accrual: latest.accrual,
    carryOver: latest.carryOver ? "kept" : "lost",
    since: latest.since,
  }
}

/** The balance as the server would read it with this rule saved: « Acquis aujourd'hui : 24 j · prochain +24 j le … ». */
function ruleResultLine(balance: StaffLeaveBalanceDto): React.ReactNode {
  const { acquired, remaining, nextCredit } = balance
  return (
    <>
      Acquis aujourd&apos;hui : <strong className="tabular-nums">{staffDaysShort(acquired)}</strong>
      {remaining !== null && remaining !== acquired && (
        <>
          {" · reste "}
          <strong className={cn("tabular-nums", isOverdrawn(remaining) && "text-destructive")}>{staffBalanceShort(remaining)}</strong>
        </>
      )}
      {nextCredit && <> · prochain {nextCreditLabel(nextCredit)}</>}
    </>
  )
}

function RuleDialog({
  open,
  onOpenChange,
  file,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  file: StaffFileDto
  onSaved: () => void
}) {
  const today = todayLocalIso()
  const [seed, setSeed] = useState<RuleSeed>(() => ruleSeed(file.balance, today))
  const [days, setDays] = useState(seed.days)
  const [accrual, setAccrual] = useState<StaffLeaveAccrual>(seed.accrual)
  const [carryOver, setCarryOver] = useState<"lost" | "kept">(seed.carryOver)
  const [since, setSince] = useState(seed.since)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<{ balance: StaffLeaveBalanceDto | null } | null>(null)
  const resultTicket = useRef(0)
  // The two choices made with buttons fire no input event; a typed field or a picked date does.
  const guard = useDirtyGuard(open, onOpenChange, accrual !== seed.accrual || carryOver !== seed.carryOver)

  // Seeded in the render that opens the window, never in an effect after it: the « À partir du » list would answer
  // a later value with a real `change` event, and an untouched window would ask to discard. A refetch of the file
  // while it is open must not overwrite what was typed either.
  const [seededFor, setSeededFor] = useState(false)
  if (open !== seededFor) {
    setSeededFor(open)
    if (open) {
      const next = ruleSeed(file.balance, today)
      setSeed(next)
      setDays(next.days)
      setAccrual(next.accrual)
      setCarryOver(next.carryOver)
      setSince(next.since)
      setError(null)
      setResult(null)
    }
  }

  const first = !file.balance.rule && !file.balance.nextRule
  const sinceOptions = useMemo(() => {
    const base = accrual === "Year" ? firstOfMonths(today).filter((d) => d.endsWith("-01-01")) : firstOfMonths(today)
    // The latest rule's own day stays choosable even when it is older than this year.
    return since && !base.includes(since) ? [since, ...base].sort() : base
  }, [accrual, today, since])

  const changeAccrual = (next: StaffLeaveAccrual) => {
    setAccrual(next)
    setSince(first ? `${today.slice(0, 4)}-01-01` : nextStart(next, today))
  }

  const max = accrual === "Month" ? MAX_MONTHLY_DAYS : 366
  const value = parseStaffDays(days)
  const valid = value !== null && value <= max && Boolean(since)

  // The live result: the server folds the account as if this rule were saved, 300 ms after the last change.
  useEffect(() => {
    if (!open || !valid || value === null) {
      setResult(null)
      return
    }
    const ticket = ++resultTicket.current
    const timer = window.setTimeout(() => {
      teamApi
        .previewLeaveRule(file.userId, { days: value, accrual, carryOver: carryOver === "kept", since })
        .then((balance) => {
          if (ticket === resultTicket.current) setResult({ balance })
        })
        .catch(() => {
          if (ticket === resultTicket.current) setResult({ balance: null })
        })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [open, valid, value, accrual, carryOver, since, file.userId])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    if (!valid || value === null) {
      setError(`Par demi-journée, entre 0 et ${max} jours.`)
      return
    }
    setSaving(true)
    setError(null)
    try {
      await teamApi.setLeaveRule(file.userId, { days: value, accrual, carryOver: carryOver === "kept", since })
      guard.markClean()
      onOpenChange(false)
      toast.success("Règle enregistrée")
      onSaved()
    } catch (err) {
      setError(getErrorMessage(err, "La règle n'a pas pu être enregistrée."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Droit à congé de {file.fullName}</DialogTitle>
            <DialogDescription>Une nouvelle règle remplace l&apos;ancienne à partir de sa date.</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="leave-rule-form" noValidate onSubmit={submit} className="space-y-4">
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-2">
                  <Label htmlFor="leave-rule-days">Jours</Label>
                  <Input
                    id="leave-rule-days"
                    inputMode="decimal"
                    value={days}
                    onChange={(e) => setDays(e.target.value)}
                    className="w-24 md:text-sm"
                  />
                </div>
                <ModeSegmented
                  ariaLabel="Fréquence"
                  value={accrual}
                  onChange={changeAccrual}
                  options={[
                    { value: "Year", label: "par an" },
                    { value: "Month", label: "par mois" },
                  ]}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="leave-rule-since">À partir du</Label>
                {/* Remounted per frequency, and an empty value ignored: Radix answers a list swap with onValueChange(""). */}
                <Select key={accrual} value={since} onValueChange={(next) => next && setSince(next)}>
                  <SelectTrigger id="leave-rule-since" className="w-full sm:w-64">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {sinceOptions.map((day) => (
                      <SelectItem key={day} value={day}>
                        {sinceLabel(day)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <p className="text-sm font-medium">Jours non pris au 31 décembre</p>
                <ModeSegmented
                  ariaLabel="Jours non pris au 31 décembre"
                  value={carryOver}
                  onChange={setCarryOver}
                  options={[
                    { value: "lost", label: "Perdus" },
                    { value: "kept", label: "Reportés" },
                  ]}
                />
              </div>
              <p role="status" className="min-h-5 text-sm text-foreground/80 [overflow-wrap:anywhere]">
                {result?.balance ? ruleResultLine(result.balance) : result ? "Résultat indisponible" : null}
              </p>
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="leave-rule-form" disabled={saving}>
              {saving ? "Enregistrement…" : "Enregistrer la règle"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** « Fixer le solde » — « Il lui reste [ ] j au [date] »: everything before that day is inside the figure. */
function OpeningDialog({
  open,
  onOpenChange,
  file,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  file: StaffFileDto
  onSaved: () => void
}) {
  const today = todayLocalIso()
  const guard = useDirtyGuard(open, onOpenChange)
  const [days, setDays] = useState("")
  const [onDay, setOnDay] = useState(today)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setDays("")
    setOnDay(today)
    setError(null)
  }, [open, today])

  // The server's own fold, so the figure cannot disagree with the balance once saved; null while unread, NaN on failure.
  const [after, setAfter] = useState<number | null>(null)
  useEffect(() => {
    const value = parseStaffDays(days)
    if (!open || value === null || !onDay || onDay > today) {
      setAfter(null)
      return
    }
    let stale = false
    const id = window.setTimeout(() => {
      teamApi
        .previewOpeningBalance(file.userId, { days: value, onDay })
        .then((b) => !stale && setAfter(b.remaining !== null && b.remaining !== value ? b.remaining : null))
        .catch(() => !stale && setAfter(Number.NaN))
    }, 300)
    return () => {
      stale = true
      window.clearTimeout(id)
    }
  }, [open, days, onDay, today, file.userId])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    const value = parseStaffDays(days)
    if (value === null) {
      setError("Par demi-journée, entre 0 et 366 jours.")
      return
    }
    if (!onDay || onDay > today) {
      setError("La date ne peut pas être dans le futur.")
      return
    }
    setSaving(true)
    setError(null)
    try {
      await teamApi.setOpeningBalance(file.userId, { days: value, onDay })
      guard.markClean()
      onOpenChange(false)
      toast.success("Solde fixé")
      onSaved()
    } catch (err) {
      setError(getErrorMessage(err, "Le solde n'a pas pu être fixé."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Fixer le solde de {file.fullName}</DialogTitle>
            <DialogDescription className="sr-only">Il lui reste un nombre de jours à une date.</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="leave-opening-form" noValidate onSubmit={submit} className="space-y-4">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-3 text-sm">
                <span>Il lui reste</span>
                <Label htmlFor="leave-opening-days" className="sr-only">
                  Jours restants
                </Label>
                <Input
                  id="leave-opening-days"
                  inputMode="decimal"
                  value={days}
                  onChange={(e) => setDays(e.target.value)}
                  className="w-20 md:text-sm"
                />
                <span>j au</span>
                <Label htmlFor="leave-opening-day" className="sr-only">
                  Au
                </Label>
                <Input
                  id="leave-opening-day"
                  type="date"
                  max={today}
                  value={onDay}
                  onChange={(e) => setOnDay(e.target.value)}
                  className="w-auto md:text-sm"
                />
              </div>
              {after !== null && (
                <p role="status" className="text-sm text-foreground/80">
                  {Number.isNaN(after) ? (
                    "Résultat indisponible"
                  ) : (
                    <>
                      Après les congés déjà acceptés :{" "}
                      <strong className={cn("tabular-nums", isOverdrawn(after) && "text-destructive")}>{staffBalanceShort(after)}</strong>
                    </>
                  )}
                </p>
              )}
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="leave-opening-form" disabled={saving}>
              {saving ? "Enregistrement…" : "Fixer le solde"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

function AdjustDialog({
  open,
  onOpenChange,
  file,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  file: StaffFileDto
  onSaved: () => void
}) {
  const today = todayLocalIso()
  const [sign, setSign] = useState<"add" | "remove">("add")
  // « Ajouter / Retirer » fires no input event: switching it is an edit the guard must ask about.
  const guard = useDirtyGuard(open, onOpenChange, sign !== "add")
  const [days, setDays] = useState("")
  const [onDay, setOnDay] = useState(today)
  const [reason, setReason] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setSign("add")
    setDays("")
    setOnDay(today)
    setReason("")
    setError(null)
  }, [open, today])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    const value = parseStaffDays(days)
    if (value === null || value === 0) {
      setError("Par demi-journée, plus de 0 jour.")
      return
    }
    if (!onDay || onDay > today) {
      setError("La date ne peut pas être dans le futur.")
      return
    }
    if (!reason.trim()) {
      setError("Indiquez le motif.")
      return
    }
    setSaving(true)
    setError(null)
    try {
      await teamApi.adjustBalance(file.userId, { days: sign === "add" ? value : -value, onDay, reason: reason.trim() })
      guard.markClean()
      onOpenChange(false)
      toast.success(sign === "add" ? "Jours ajoutés" : "Jours retirés")
      onSaved()
    } catch (err) {
      setError(getErrorMessage(err, "Le solde n'a pas pu être ajusté."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Ajouter ou retirer des jours</DialogTitle>
            <DialogDescription>{file.fullName}</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="leave-adjust-form" noValidate onSubmit={submit} className="space-y-4">
              <ModeSegmented
                ariaLabel="Ajouter ou retirer"
                value={sign}
                onChange={setSign}
                options={[
                  { value: "add", label: "Ajouter" },
                  { value: "remove", label: "Retirer" },
                ]}
              />
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-2">
                  <Label htmlFor="leave-adjust-days">Jours</Label>
                  <div className="flex items-center gap-1.5">
                    {sign === "add" ? (
                      <Plus className="size-4 text-muted-foreground" aria-hidden="true" />
                    ) : (
                      <Minus className="size-4 text-muted-foreground" aria-hidden="true" />
                    )}
                    <Input
                      id="leave-adjust-days"
                      inputMode="decimal"
                      value={days}
                      onChange={(e) => setDays(e.target.value)}
                      className="w-24 md:text-sm"
                    />
                  </div>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="leave-adjust-day">Le</Label>
                  <Input id="leave-adjust-day" type="date" max={today} value={onDay} onChange={(e) => setOnDay(e.target.value)} />
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="leave-adjust-reason">Motif</Label>
                <Input
                  id="leave-adjust-reason"
                  value={reason}
                  maxLength={MAX_STAFF_LEAVE_REASON}
                  onChange={(e) => setReason(e.target.value)}
                  className="md:text-sm"
                />
              </div>
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="leave-adjust-form" disabled={saving}>
              {saving ? "Enregistrement…" : sign === "add" ? "Ajouter" : "Retirer"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** A « Droit à congé » line offered for cancellation, named as the screen states it. */
export interface CancellableLine {
  id: string
  label: string
}

/** Struck through with a motif; every balance it fed is folded again. Used by the file's lines and by the relevé. */
export function CancelEntryDialog({
  entry,
  onClose,
  onSaved,
}: {
  entry: CancellableLine | null
  onClose: () => void
  onSaved: () => void
}) {
  const open = entry !== null
  const guard = useDirtyGuard(open, (o) => {
    if (!o) onClose()
  })
  const [reason, setReason] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const label = entry?.label ?? ""

  useEffect(() => {
    if (!open) return
    setReason("")
    setError(null)
  }, [open])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (!entry || saving) return
    if (!reason.trim()) {
      setError("Indiquez le motif de l'annulation.")
      return
    }
    setSaving(true)
    setError(null)
    try {
      await teamApi.cancelAllowanceEntry(entry.id, reason.trim())
      guard.markClean()
      onClose()
      toast.success("Ligne annulée")
      onSaved()
    } catch (err) {
      setError(getErrorMessage(err, "La ligne n'a pas pu être annulée."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler {label ? quoteFr(label) : "cette ligne"} ?</DialogTitle>
            <DialogDescription>Elle reste visible, barrée, et le solde est recalculé.</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="leave-cancel-form" noValidate onSubmit={submit} className="space-y-2">
              <Label htmlFor="leave-cancel-reason">Motif</Label>
              <Input
                id="leave-cancel-reason"
                value={reason}
                maxLength={MAX_STAFF_LEAVE_REASON}
                onChange={(e) => setReason(e.target.value)}
                className="md:text-sm"
              />
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="leave-cancel-form" variant="destructive" disabled={saving}>
              {saving ? "Annulation…" : "Annuler la ligne"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}
