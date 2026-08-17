"use client"

import { Fragment, useState, type ReactNode } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { Check, ClipboardCheck, FileText, Receipt, UserCheck, UserX } from "lucide-react"
import { toast } from "sonner"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { BillDentalRecordDialog } from "@/components/factures/bill-dental-record-dialog"
import { appointmentsApi } from "@/lib/api/appointments"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import type { DentalRecordDto, VisitToCloseDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { formatDateTime } from "@/lib/format"
import { zoneChipClass, zoneForPath } from "@/lib/zones"
import { cn } from "@/lib/utils"
import { NothingToBillDialog } from "./nothing-to-bill-dialog"
import { visitClosureDayGroups } from "./visit-closure-days"

/**
 * « À clôturer » — the séances still owing a presence, a fiche or a money document.
 *
 * <p><b>The row asks ONE question.</b> The three answers are drawn as progress, but only `nextStep` gets controls:
 * three simultaneous red actions on a visit that ended an hour ago is nagging, and two of them cannot be answered
 * yet — a visit nobody has confirmed happened is not « missing » a fiche, and a séance with no fiche has no acts
 * to price. The cascade comes from the server (`VisitClosureRules`); re-deriving it here would be a second copy of
 * the rule, and the copy that drifts is the one that starts asking the wrong question.</p>
 *
 * <p><b>It rebuilds nothing.</b> « Ajouter la fiche » lands on the patient page's existing record modal through the
 * deep link the post-visit prompt already uses, and « Encaisser » lands on the same page's own billing action. The
 * two presence buttons are an ordinary status update.</p>
 *
 * <p>`_LG` and not `_md:` for the table/cards hinge: an iPad portrait is 820 px and would otherwise get the
 * desktop grid *and* the 256 px rail for a row carrying a name, a time, a practitioner, three states and two
 * buttons.</p>
 */

interface VisitClosureListProps {
  visits: VisitToCloseDto[]
  loading?: boolean
  /** Refetch after any action — a closed visit leaves the list, and the count above it moves. */
  onChanged: () => void
  /** Rendered when there is genuinely nothing left. A *failed* read must not reach this — see the page. */
  emptyTitle?: string
  emptyDescription?: string
  /**
   * Rendered as the last row **inside** the list's own surface — in practice `/a-cloturer`'s pager.
   *
   * <p>`DataTablePagination` carries a `border-t` and no border of its own, precisely so it reads as a card's
   * footer; rendered as a page-level sibling it was a filet flottant under a borderless slab. Passed in rather
   * than owned here because the page owns the paging state, and the agenda strip has no pager at all.</p>
   */
  footer?: ReactNode
}

export function VisitClosureList({
  visits,
  loading = false,
  onChanged,
  emptyTitle = "Rien à clôturer",
  emptyDescription = "Toutes les séances passées ont leur présence, leur fiche et leur encaissement.",
  footer,
}: VisitClosureListProps) {
  const router = useRouter()
  const [busyId, setBusyId] = useState<string | null>(null)
  const [nothingToBillFor, setNothingToBillFor] = useState<VisitToCloseDto | null>(null)
  const [billing, setBilling] = useState<{ record: DentalRecordDto; patientName: string } | null>(null)

  /** « Venu » / « Absent ». The two legal answers to the presence question, and an ordinary status update. */
  const answerPresence = async (visit: VisitToCloseDto, came: boolean) => {
    setBusyId(visit.appointmentId)
    try {
      await appointmentsApi.update(visit.appointmentId, { status: came ? "Completed" : "NoShow" })
      toast.success(came ? "Séance marquée comme honorée." : "Patient marqué comme absent.")
      onChanged()
    } catch (err) {
      // The dialog-stays-open rule, one surface over: the row stays exactly as it was and the message says why.
      showErrorToast(err)
    } finally {
      setBusyId(null)
    }
  }

  /** The patient page's own record modal, through the deep link the post-visit prompt already uses. */
  const openFiche = (visit: VisitToCloseDto) =>
    router.push(
      `/patients/${encodeURIComponent(visit.patientId)}?addRecord=1&appointmentId=${encodeURIComponent(
        visit.appointmentId,
      )}`,
    )

  /**
   * « Encaisser » — the fiche's own `BillDentalRecordDialog`, opened here rather than on the patient page.
   *
   * The fiche is fetched on the click because the row carries only its id; that is one request on an explicit
   * action, and the alternative was landing the user on a records tab to find the séance themselves.
   */
  const openBilling = async (visit: VisitToCloseDto) => {
    if (!visit.dentalRecordId) return
    setBusyId(visit.appointmentId)
    try {
      const records = await dentalRecordsApi.list(visit.patientId)
      const record = records.find((r) => r.id === visit.dentalRecordId)
      if (!record) {
        showErrorToast(new Error("Fiche de soins introuvable."))
        return
      }
      setBilling({ record, patientName: visit.patientName })
    } catch (err) {
      showErrorToast(err)
    } finally {
      setBusyId(null)
    }
  }

  const zone = zoneForPath("/a-cloturer")
  const dayGroups = visitClosureDayGroups(visits)

  return (
    <>
      {/*
        One bordered surface, the shape every other list in the app has (`invoices-table`,
        `treatment-plans-table`, `caisse-ledger-table`). `ui/table.tsx` paints `bg-card` and takes its radius from
        the parent — with no parent radius it rendered as a square, borderless white slab on the tinted page
        ground, which was the whole of « ces pages ne suivent pas la charte ». The page's pager renders inside it.
      */}
      <div className="rounded-md border bg-card">
        {loading ? (
          <>
            {/*
              ⚠️ Two skeletons, one per tree, and the table one is what was MISSING. The card list has always had
              its own `loading` state; the table branch had none, so `!loading && visits.length === 0` fell
              through to a `<tbody>` with no rows — headers over a void. Changing the période from 7 to 90 days
              therefore painted « aucune ligne » for the length of the fetch, which is the exact reading this
              screen takes care to avoid for a failed read.
            */}
            <div
              className={cn(TABLE_ONLY_LG, "space-y-3 p-4")}
              role="status"
              aria-label="Chargement des séances à clôturer"
            >
              <div className="flex gap-4 border-b pb-3">
                {["w-1/5", "w-1/5", "w-1/6", "w-1/4", "w-1/6"].map((width, i) => (
                  <div key={i} className={cn("h-4 animate-pulse rounded bg-muted", width)} />
                ))}
              </div>
              {[0, 1, 2, 3].map((i) => (
                <div key={i} className="flex gap-4">
                  {["w-1/5", "w-1/5", "w-1/6", "w-1/4", "w-1/6"].map((width, j) => (
                    <div key={j} className={cn("h-4 animate-pulse rounded bg-muted", width)} />
                  ))}
                </div>
              ))}
            </div>
            <CardList
              className={CARDS_ONLY_LG}
              items={[]}
              getKey={(v: VisitToCloseDto) => v.appointmentId}
              ariaLabel="Séances à clôturer"
              loading
              title={(v: VisitToCloseDto) => v.patientName}
              fields={() => []}
            />
          </>
        ) : visits.length === 0 ? (
          <EmptyState
            size="compact"
            icon={ClipboardCheck}
            title={emptyTitle}
            description={emptyDescription}
            chipClassName={zoneChipClass(zone)}
          />
        ) : (
          <>
            {/* ── Desktop / large tablet ─────────────────────────────────────────────────────────────── */}
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader sticky>
                <TableRow>
                  <TableHead>Patient</TableHead>
                  <TableHead>Séance</TableHead>
                  <TableHead>Praticien</TableHead>
                  <TableHead>État</TableHead>
                  <TableHead className="text-end">À faire</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {dayGroups.map((group) => (
                  <Fragment key={group.key || group.label}>
                    {/* A day separator, not a column: the list is oldest-first over up to 90 days, and a column
                        of near-identical timestamps gives the eye nothing to navigate by. */}
                    <TableRow className="hover:bg-transparent">
                      <TableCell
                        colSpan={5}
                        className="bg-muted/40 py-1.5 font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground"
                      >
                        <span className="flex items-center justify-between gap-3">
                          <span>{group.label}</span>
                          <span>
                            {group.visits.length} séance{group.visits.length > 1 ? "s" : ""}
                          </span>
                        </span>
                      </TableCell>
                    </TableRow>
                    {group.visits.map((visit) => (
                      <TableRow key={visit.appointmentId}>
                        <TableCell className="font-medium">
                          <Link
                            href={`/patients/${encodeURIComponent(visit.patientId)}`}
                            className="underline-offset-4 hover-hover:hover:underline"
                          >
                            {visit.patientName}
                          </Link>
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          <div>{formatDateTime(visit.appointmentDateTime)}</div>
                          {visit.procedures.length > 0 && (
                            <div className="text-xs">{visit.procedures.join(" · ")}</div>
                          )}
                          {/* The motif belongs in BOTH trees. It was rendered in the cards and nowhere here, so
                              a « Rien à facturer » visit explained itself on a phone and not at the desk. */}
                          {visit.nothingToBillReason && (
                            <div className="text-xs">Rien à facturer : {visit.nothingToBillReason}</div>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground">{visit.doctorName ?? "—"}</TableCell>
                        <TableCell>
                          <ClosureProgress visit={visit} />
                        </TableCell>
                        <TableCell className="text-end">
                          <div className="flex flex-wrap justify-end gap-2">
                            <RowActions
                              visit={visit}
                              busy={busyId === visit.appointmentId}
                              onPresence={answerPresence}
                              onFiche={openFiche}
                              onBilling={openBilling}
                              onNothingToBill={setNothingToBillFor}
                            />
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </Fragment>
                ))}
              </TableBody>
            </Table>

            {/* ── Phone / tablet portrait ────────────────────────────────────────────────────────────── */}
            <div className={CARDS_ONLY_LG}>
              {dayGroups.map((group) => (
                <section key={group.key || group.label}>
                  <h3 className="border-b bg-muted/40 px-4 py-1.5 font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground">
                    <span className="flex items-center justify-between gap-3">
                      <span>{group.label}</span>
                      <span>
                        {group.visits.length} séance{group.visits.length > 1 ? "s" : ""}
                      </span>
                    </span>
                  </h3>
                  <CardList
                    items={group.visits}
                    getKey={(v) => v.appointmentId}
                    ariaLabel={`Séances à clôturer — ${group.label}`}
                    title={(v) => v.patientName}
                    href={(v) => `/patients/${encodeURIComponent(v.patientId)}`}
                    subtitle={(v) => formatDateTime(v.appointmentDateTime)}
                    status={(v) => <ClosureProgress visit={v} />}
                    fields={(v) => [
                      v.doctorName ? { label: "Praticien", value: v.doctorName } : null,
                      v.procedures.length > 0 ? { label: "Actes", value: v.procedures.join(" · ") } : null,
                      v.nothingToBillReason
                        ? { label: "Rien à facturer", value: v.nothingToBillReason }
                        : null,
                    ]}
                    primaryAction={(v) => (
                      <div className="flex flex-wrap gap-2">
                        <RowActions
                          visit={v}
                          busy={busyId === v.appointmentId}
                          onPresence={answerPresence}
                          onFiche={openFiche}
                          onBilling={openBilling}
                          onNothingToBill={setNothingToBillFor}
                        />
                      </div>
                    )}
                  />
                </section>
              ))}
            </div>
          </>
        )}

        {footer}
      </div>

      <NothingToBillDialog
        visit={nothingToBillFor}
        onOpenChange={(open) => !open && setNothingToBillFor(null)}
        onSuccess={() => {
          setNothingToBillFor(null)
          onChanged()
        }}
      />

      <BillDentalRecordDialog
        record={billing?.record ?? null}
        patientName={billing?.patientName ?? ""}
        onOpenChange={(open) => !open && setBilling(null)}
        onSuccess={() => {
          setBilling(null)
          onChanged()
        }}
      />
    </>
  )
}

/** The wire's `nextStep` values, in the order the cascade asks them. Server-derived; never re-derived here. */
const STEP_FOR: Record<"Presence" | "Fiche" | "Billing", string> = {
  Presence: "Venue",
  Fiche: "Fiche",
  Billing: "Encaissement",
}

/**
 * The three answers as progress, never as three alarms.
 *
 * <p>A satisfied step is stated in words with a tick, not only by colour — a greyscale printout and a screen
 * reader get the same facts. The pending ones are neutral: they are not failures, they are the rest of the visit.</p>
 *
 * <p>⚠️ <b>A pending step is an empty circle, not a « ✗ ».</b> The cross was the drawing contradicting the
 * paragraph above it: it is the universal mark of failure, and there were three of them on every row of a screen
 * that exists to be emptied — so an ordinary afternoon's work read as a page of errors. The one step the row is
 * actually asking for is picked out instead (`nextStep`, which the server already computes), so the row states
 * its question rather than making the reader infer it from three identical badges.</p>
 */
function ClosureProgress({ visit }: { visit: VisitToCloseDto }) {
  const nextLabel = STEP_FOR[visit.nextStep as keyof typeof STEP_FOR] ?? null

  const steps: Array<{ label: string; done: boolean }> = [
    { label: "Venue", done: visit.presenceAnswered },
    { label: "Fiche", done: visit.ficheRecorded },
    { label: "Encaissement", done: visit.billingSettled },
  ]

  return (
    <ul className="flex flex-wrap gap-1.5">
      {steps.map((step) => {
        const isNext = !step.done && step.label === nextLabel
        return (
          <li key={step.label}>
            <Badge
              variant="outline"
              className={cn(
                "gap-1 font-normal",
                step.done && "border-success-ink/30 text-success-ink",
                isNext && "border-primary/40 bg-primary/5 font-medium text-primary",
                !step.done && !isNext && "text-muted-foreground",
              )}
            >
              {step.done ? (
                <Check aria-hidden="true" className="size-3" />
              ) : (
                // A hollow ring: « pas encore », with no claim that anything went wrong.
                <span
                  aria-hidden="true"
                  className={cn(
                    "size-2 rounded-full border-[1.5px] border-current",
                    isNext && "bg-primary/25",
                  )}
                />
              )}
              <span className="sr-only">
                {step.done ? "Fait : " : isNext ? "À faire maintenant : " : "En attente : "}
              </span>
              {step.label}
            </Badge>
          </li>
        )
      })}
    </ul>
  )
}

/** The controls for the ONE question this visit is asking. */
function RowActions({
  visit,
  busy,
  onPresence,
  onFiche,
  onBilling,
  onNothingToBill,
}: {
  visit: VisitToCloseDto
  busy: boolean
  onPresence: (visit: VisitToCloseDto, came: boolean) => void
  onFiche: (visit: VisitToCloseDto) => void
  onBilling: (visit: VisitToCloseDto) => void | Promise<void>
  onNothingToBill: (visit: VisitToCloseDto) => void
}) {
  if (visit.nextStep === "Presence") {
    // Each row's pair carries the patient's name: this list is many rows of identical actions, so « Venu » alone
    // announces the same thing a dozen times with no way to tell which séance is being answered.
    return (
      <>
        <Button
          size="sm"
          disabled={busy}
          onClick={() => onPresence(visit, true)}
          aria-label={`${visit.patientName} est venu`}
        >
          <UserCheck aria-hidden="true" className="me-1.5 size-4" />
          Venu
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={busy}
          onClick={() => onPresence(visit, false)}
          aria-label={`${visit.patientName} est absent`}
        >
          <UserX aria-hidden="true" className="me-1.5 size-4" />
          Absent
        </Button>
      </>
    )
  }

  if (visit.nextStep === "Fiche") {
    return (
      <Button size="sm" disabled={busy} onClick={() => onFiche(visit)}>
        <FileText aria-hidden="true" className="me-1.5 size-4" />
        Ajouter la fiche
      </Button>
    )
  }

  return (
    <>
      <Button size="sm" disabled={busy} onClick={() => void onBilling(visit)}>
        <Receipt aria-hidden="true" className="me-1.5 size-4" />
        Encaisser
      </Button>
      <Button size="sm" variant="outline" disabled={busy} onClick={() => onNothingToBill(visit)}>
        Rien à facturer
      </Button>
    </>
  )
}
