"use client"

import { useState } from "react"
import Link from "next/link"
import { AlertTriangle, Check, MoreHorizontal, Users } from "lucide-react"
import { Button } from "@/components/ui/button"
import { CARDS_ONLY_LG, CardList, TABLE_ONLY_LG } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import type { StaffLeaveRequestDto } from "@/lib/api/team"
import { formatDateFr, quoteFr, todayLocalIso } from "@/lib/format"
import {
  balanceAfterLabel,
  firstOfLabel,
  isOverdrawn,
  isUpcomingLeave,
  staffBalanceLabel,
  staffDaysLabel,
  staffLeaveCancelWording,
  staffLeaveDaysText,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"
import { cn } from "@/lib/utils"
import {
  AcceptLeaveDialog,
  AdminCancelLeaveDialog,
  CancelLeaveConfirm,
  RejectLeaveDialog,
  leaveCancelSubject,
  leaveCancelVerb,
} from "./staff-leave-decision-dialogs"
import { StaffLeaveDateTile, StaffLeaveStatusPill, staffLeaveDecisionLine } from "./staff-leave-bits"

export type LeaveDialogKind = "accept" | "reject" | "cancel"
export type OpenLeaveDialog = (kind: LeaveDialogKind, request: StaffLeaveRequestDto) => void

/**
 * The three decisions, mounted once by the PAGE — never by a list, so a refresh that empties the list (another admin
 * answered the last request) cannot unmount a dialog somebody is typing in. `admin`: the admin's pages, where a
 * cancellation names whose congé it is and asks a motif; otherwise the secretary withdraws her own.
 */
export function useLeaveDialogs(onChanged?: () => void, { admin = false }: { admin?: boolean } = {}) {
  const [dialog, setDialog] = useState<{ kind: LeaveDialogKind; request: StaffLeaveRequestDto } | null>(null)
  const open: OpenLeaveDialog = (kind, request) => setDialog({ kind, request })
  const close = (next: boolean) => {
    if (!next) setDialog(null)
  }
  const dialogs = (
    <>
      <AcceptLeaveDialog request={dialog?.request ?? null} open={dialog?.kind === "accept"} onOpenChange={close} onChanged={onChanged} />
      <RejectLeaveDialog request={dialog?.request ?? null} open={dialog?.kind === "reject"} onOpenChange={close} onChanged={onChanged} />
      {admin ? (
        <AdminCancelLeaveDialog
          request={dialog?.request ?? null}
          open={dialog?.kind === "cancel"}
          onOpenChange={close}
          onChanged={onChanged}
        />
      ) : (
        <CancelLeaveConfirm request={dialog?.request ?? null} open={dialog?.kind === "cancel"} onOpenChange={close} onChanged={onChanged} />
      )}
    </>
  )
  return { open, dialogs }
}

const userHref = (request: StaffLeaveRequestDto) => `/users/${encodeURIComponent(request.userId)}`

/** The refusal's motif, quoted word for word in a quiet red box. */
function RefusalNote({ request }: { request: StaffLeaveRequestDto }) {
  if (request.status !== "Rejected" || !request.rejectionReason) return null
  return (
    <p className="w-fit max-w-full rounded-md border border-destructive/20 bg-destructive-wash px-2.5 py-1.5 text-sm break-words text-destructive">
      Motif du refus : {quoteFr(request.rejectionReason)}
    </p>
  )
}

/** Who decided, entered or cancelled it (« Annulé par … · « motif » »), then a refusal's motif, quoted. */
function decisionText(request: StaffLeaveRequestDto): string | null {
  const line = staffLeaveDecisionLine(request, "other")
  if (request.status !== "Rejected" || !request.rejectionReason) return line
  return `${line ? `${line} · ` : ""}${quoteFr(request.rejectionReason)}`
}

// ─── A secretary's own requests ──────────────────────────────────────────────────────────────────────────────

/** « Mes congés »: one row per request — date tile, period and status, then the type, the days and the motif. */
export function StaffLeaveRows({
  items,
  todayKey,
  onOpen,
}: {
  items: StaffLeaveRequestDto[]
  todayKey: string
  onOpen: OpenLeaveDialog
}) {
  return (
    <ul className="divide-y overflow-hidden rounded-xl border bg-card">
      {items.map((request) => {
        const period = staffLeaveShortPeriod(request)
        const decision = staffLeaveDecisionLine(request, "self")
        return (
          <li key={request.id} className="flex flex-col gap-3 px-4 py-4 sm:flex-row sm:items-center sm:gap-4 sm:px-5">
            <div className="flex min-w-0 flex-1 items-start gap-3 sm:gap-4">
              <StaffLeaveDateTile leave={request} muted={!isUpcomingLeave(request, todayKey)} />
              <div className="min-w-0 flex-1 space-y-1">
                <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
                  <p className="font-semibold">{period}</p>
                  <StaffLeaveStatusPill status={request.status} label={request.statusLabel} />
                </div>
                <p className="text-sm text-muted-foreground">
                  {request.typeLabel} · {staffLeaveDaysText(request, "self")}
                </p>
                <p className="text-sm break-words">{quoteFr(request.reason)}</p>
                <RefusalNote request={request} />
                {decision && <p className="text-xs text-muted-foreground">{decision}</p>}
              </div>
            </div>
            {request.canCancel && (
              <div className="ps-[3.75rem] sm:ps-0">
                <Button
                  variant="outline"
                  size="sm"
                  className="coarse:h-11"
                  aria-label={`${leaveCancelVerb(request)} ${leaveCancelSubject(request, false)}`}
                  onClick={() => onOpen("cancel", request)}
                >
                  {staffLeaveCancelWording(request.status).action}
                </Button>
              </div>
            )}
          </li>
        )
      })}
    </ul>
  )
}

// ─── The admin's requests to decide ──────────────────────────────────────────────────────────────────────────

/**
 * « Solde après » on a waiting card: only a congé payé with an allowance counts against one, read on the day the
 * server names (a monthly rule credits until then); no droit links to her file.
 */
function BalanceAfterValue({ request, todayKey }: { request: StaffLeaveRequestDto; todayKey: string }) {
  if (request.balanceAfter !== null) {
    return (
      <>
        <span className={cn(isOverdrawn(request.balanceAfter) && "text-destructive")}>{staffBalanceLabel(request.balanceAfter)}</span>
        {request.balanceAfterOn && request.balanceAfterOn > todayKey && (
          <span className="block text-xs font-normal text-muted-foreground">au {firstOfLabel(request.balanceAfterOn)}</span>
        )}
      </>
    )
  }
  if (request.type !== "PaidLeave") return <>Ne compte pas</>
  return (
    <Link
      href={userHref(request)}
      className="inline-flex items-center text-primary underline-offset-2 hover:underline coarse:min-h-11"
      aria-label={`Droit non défini : ouvrir la fiche de ${request.userName}`}
    >
      Droit non défini
    </Link>
  )
}

/**
 * « À décider »: one card per request, the decision as buttons at its foot. The grid and the footer hinge on their
 * own width — beside the rail a tablet's « md: » leaves two ~250 px cards, too narrow for three buttons in a row.
 */
export function StaffLeaveDecisionCards({
  items,
  onOpen,
}: {
  items: StaffLeaveRequestDto[]
  onOpen: OpenLeaveDialog
}) {
  const todayKey = todayLocalIso()
  return (
    <div className="@container">
      <ul className="grid gap-3 @3xl:grid-cols-2">
        {items.map((request) => (
          <li key={request.id} className="@container flex min-w-0 flex-col rounded-xl border bg-card">
            <div className="flex-1 space-y-4 p-4 sm:p-5">
              <div className="flex items-center gap-3">
                <InitialsAvatar name={request.userName} className="size-10 text-xs" />
                <div className="min-w-0 flex-1">
                  <Link
                    href={userHref(request)}
                    className="font-medium underline-offset-2 [overflow-wrap:anywhere] hover:underline coarse:py-2"
                  >
                    {request.userName}
                  </Link>
                  <p className="text-xs text-muted-foreground">Demandé le {formatDateFr(request.createdAt)}</p>
                </div>
                <span className="shrink-0 rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
                  {request.typeLabel}
                </span>
              </div>

              <div className="space-y-1">
                <p className="text-lg leading-snug font-semibold">{staffLeaveShortPeriod(request)}</p>
                <p className="text-sm break-words">{quoteFr(request.reason)}</p>
              </div>

              <dl className="grid grid-cols-2 divide-x overflow-hidden rounded-lg border">
                <div className="px-3 py-2">
                  <dt className="text-xs text-muted-foreground">Jours</dt>
                  <dd className="font-semibold tabular-nums">{staffDaysLabel(request.requestedDays)}</dd>
                </div>
                <div className="px-3 py-2">
                  <dt className="text-xs text-muted-foreground">{balanceAfterLabel(request.startDate)}</dt>
                  <dd className="font-semibold tabular-nums">
                    <BalanceAfterValue request={request} todayKey={todayKey} />
                  </dd>
                </div>
              </dl>

              {request.othersOff.length > 0 && (
                <p className="flex items-start gap-2 rounded-lg bg-warning-wash px-3 py-2 text-sm text-warning-ink">
                  <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
                  <span>Déjà en congé sur ces dates : {request.othersOff.join(", ")}</span>
                </p>
              )}
              {request.othersPending.length > 0 && (
                <p className="flex items-start gap-2 rounded-lg bg-muted/60 px-3 py-2 text-sm">
                  <Users className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                  <span>Demande aussi ces dates : {request.othersPending.map((name) => `${name} (en attente)`).join(", ")}</span>
                </p>
              )}
            </div>

            {/* « Refuser » covers withdrawing a waiting request, with the motif she is owed. */}
            <div className="grid grid-cols-2 gap-2 rounded-b-xl border-t bg-muted/30 px-4 py-3 @sm:flex @sm:items-center @sm:justify-end">
              <Button
                variant="outline"
                size="sm"
                className="coarse:h-11"
                aria-label={`Refuser la demande de ${request.userName}`}
                onClick={() => onOpen("reject", request)}
              >
                Refuser
              </Button>
              <Button
                size="sm"
                className="gap-1.5 coarse:h-11"
                aria-label={`Accepter la demande de ${request.userName}`}
                onClick={() => onOpen("accept", request)}
              >
                <Check className="size-4" aria-hidden="true" />
                Accepter
              </Button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

// ─── Every request, as a table from `lg:` and as cards below ─────────────────────────────────────────────────

/** The admin's row menu: decide a waiting request, cancel an accepted congé (with a motif). Names whose congé it is. */
function RowActions({ request, onOpen }: { request: StaffLeaveRequestDto; onOpen: OpenLeaveDialog }) {
  const decidable = request.status === "Pending"
  const cancellable = request.status === "Approved" && request.canCancel
  if (!decidable && !cancellable) return null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="coarse:size-11"
          aria-label={`Actions pour ${leaveCancelSubject(request, true)}`}
        >
          <MoreHorizontal className="size-4" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {decidable && (
          <>
            <DropdownMenuItem className="coarse:py-3" onSelect={() => onOpen("accept", request)}>
              Accepter
            </DropdownMenuItem>
            <DropdownMenuItem className="coarse:py-3" onSelect={() => onOpen("reject", request)}>
              Refuser
            </DropdownMenuItem>
          </>
        )}
        {cancellable && (
          <DropdownMenuItem className="coarse:py-3" onSelect={() => onOpen("cancel", request)}>
            Annuler le congé
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** « 2,5 j retirés du solde » over « (3 jours demandés) » when the admin counted differently. */
function DaysCell({ request }: { request: StaffLeaveRequestDto }) {
  const counted = request.countedDays
  if (counted === null || counted === request.requestedDays) {
    return <span className="whitespace-nowrap">{staffDaysLabel(counted ?? request.requestedDays)}</span>
  }
  return (
    <>
      <span className="block">{staffLeaveDaysText(request)}</span>
      <span className="block text-xs text-muted-foreground">({staffDaysLabel(request.requestedDays)} demandés)</span>
    </>
  )
}

/** The decided history (admin) or a secretary's whole file. `showName` adds the secretary column. */
export function StaffLeaveHistory({
  items,
  showName,
  onOpen,
  ariaLabel,
}: {
  items: StaffLeaveRequestDto[]
  showName: boolean
  onOpen: OpenLeaveDialog
  ariaLabel: string
}) {
  return (
    <>
      <div className={cn(TABLE_ONLY_LG, "overflow-hidden rounded-xl border bg-card")}>
        <Table>
          <TableHeader>
            <TableRow>
              {showName && <TableHead className="ps-5">Secrétaire</TableHead>}
              <TableHead className={cn(!showName && "ps-5")}>Dates</TableHead>
              <TableHead>Type</TableHead>
              <TableHead className="text-right">Jours</TableHead>
              <TableHead>Statut</TableHead>
              <TableHead>Décision</TableHead>
              <TableHead className="w-14 pe-5">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((request) => (
              <TableRow key={request.id}>
                {showName && (
                  <TableCell className="ps-5">
                    <Link
                      href={userHref(request)}
                      className="inline-flex items-center gap-2.5 font-medium underline-offset-2 hover:underline coarse:min-h-11"
                    >
                      <InitialsAvatar name={request.userName} className="size-7" />
                      {request.userName}
                    </Link>
                  </TableCell>
                )}
                <TableCell className={cn(!showName && "ps-5")}>
                  <span className="block font-medium">{staffLeaveShortPeriod(request)}</span>
                  <span className="block text-xs text-muted-foreground">{quoteFr(request.reason)}</span>
                </TableCell>
                <TableCell>{request.typeLabel}</TableCell>
                <TableCell className="text-right tabular-nums">
                  <DaysCell request={request} />
                </TableCell>
                <TableCell>
                  <StaffLeaveStatusPill status={request.status} label={request.statusLabel} />
                </TableCell>
                <TableCell clamp className="text-sm text-muted-foreground">
                  {decisionText(request)}
                </TableCell>
                <TableCell className="pe-5 text-right">
                  <RowActions request={request} onOpen={onOpen} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <CardList
        className={cn(CARDS_ONLY_LG, "p-0")}
        ariaLabel={ariaLabel}
        items={items}
        getKey={(request) => request.id}
        leading={(request) => <StaffLeaveDateTile leave={request} className="size-11" />}
        title={(request) => (showName ? request.userName : staffLeaveShortPeriod(request))}
        href={(request) => (showName ? userHref(request) : undefined)}
        subtitle={(request) => (showName ? staffLeaveShortPeriod(request) : request.typeLabel)}
        status={(request) => <StaffLeaveStatusPill status={request.status} label={request.statusLabel} />}
        fields={(request) => [
          showName && { label: "Type", value: request.typeLabel },
          { label: "Jours", value: staffLeaveDaysText(request) },
          { label: "Motif", value: quoteFr(request.reason) },
          { label: "Décision", value: decisionText(request) },
        ]}
        actions={(request) => <RowActions request={request} onOpen={onOpen} />}
      />
    </>
  )
}
