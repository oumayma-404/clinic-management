"use client"

import { useState } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { Check, ClipboardCheck, FileText, Receipt, UserCheck, UserX, X } from "lucide-react"
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
}

export function VisitClosureList({
  visits,
  loading = false,
  onChanged,
  emptyTitle = "Rien à clôturer",
  emptyDescription = "Toutes les séances passées ont leur présence, leur fiche et leur encaissement.",
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

  if (!loading && visits.length === 0) {
    return (
      <EmptyState
        icon={ClipboardCheck}
        title={emptyTitle}
        description={emptyDescription}
        chipClassName={zoneChipClass(zone)}
      />
    )
  }

  return (
    <>
      {/* ── Desktop / large tablet ─────────────────────────────────────────────────────────────────── */}
      <div className={TABLE_ONLY_LG}>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Patient</TableHead>
              <TableHead>Séance</TableHead>
              <TableHead>Praticien</TableHead>
              <TableHead>État</TableHead>
              <TableHead className="text-end">À faire</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {visits.map((visit) => (
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
          </TableBody>
        </Table>
      </div>

      {/* ── Phone / tablet portrait ────────────────────────────────────────────────────────────────── */}
      <div className={CARDS_ONLY_LG}>
        <CardList
          items={visits}
          getKey={(v) => v.appointmentId}
          ariaLabel="Séances à clôturer"
          loading={loading}
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

/**
 * The three answers as progress, never as three alarms.
 *
 * <p>A satisfied step is stated in words with a tick, not only by colour — a greyscale printout and a screen
 * reader get the same facts. The pending ones are neutral: they are not failures, they are the rest of the visit.</p>
 */
function ClosureProgress({ visit }: { visit: VisitToCloseDto }) {
  const steps: Array<{ label: string; done: boolean }> = [
    { label: "Venue", done: visit.presenceAnswered },
    { label: "Fiche", done: visit.ficheRecorded },
    { label: "Encaissement", done: visit.billingSettled },
  ]

  return (
    <ul className="flex flex-wrap gap-1.5">
      {steps.map((step) => (
        <li key={step.label}>
          <Badge
            variant="outline"
            className={cn(
              "gap-1 font-normal",
              step.done ? "text-success-ink border-success-ink/30" : "text-muted-foreground",
            )}
          >
            {step.done ? (
              <Check aria-hidden="true" className="size-3" />
            ) : (
              <X aria-hidden="true" className="size-3" />
            )}
            <span className="sr-only">{step.done ? "Fait :" : "À faire :"}</span>
            {step.label}
          </Badge>
        </li>
      ))}
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
    return (
      <>
        <Button size="sm" disabled={busy} onClick={() => onPresence(visit, true)}>
          <UserCheck aria-hidden="true" className="me-1.5 size-4" />
          Venu
        </Button>
        <Button size="sm" variant="outline" disabled={busy} onClick={() => onPresence(visit, false)}>
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
