"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { UserPlus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { DuplicateSuggestionPrompt } from "@/components/patients/duplicate-suggestion-prompt"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { patientsApi } from "@/lib/api/patients"
import { formatDateFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import type { PatientDto } from "@/lib/api/types"

/** How many to show before pointing at the full list. A backlog, not a page — the list has its own filter. */
const SHOWN = 25

/**
 * « Patients à compléter » — the fiches the Google Calendar import created from an event title, still unconfirmed.
 *
 * <p>⚠️ <b>Its own surface, deliberately not rows in the séances list beside it.</b> « À clôturer » lists visits
 * that have already happened and owe a présence, a fiche or an encaissement — one question per row, grouped by the
 * day of the visit. An imported patient usually has only a *future* appointment, so as a row there it would sit in
 * an overdue worklist with nothing overdue, carry no day to group under, and be counted in « N séances ».</p>
 *
 * <p>⚠️ It wears the <b>same</b> shape as that list — one `rounded-md border bg-card` surface, a sticky-header
 * table above `lg:` and a `CardList` below — including the `_LG` hinge, which three columns would not need on their
 * own. The two are tabs of one page: switching between them at 820 px and meeting a table on one side and cards on
 * the other reads as two different products.</p>
 *
 * <p>⚠️ A failed read is never silence: « aucun patient à compléter » and « je n'ai pas pu lire » are opposite
 * facts with the same picture, so the failure gets the shared notice.</p>
 */
export function PendingReviewBlock({
  reloadKey,
  onLoaded,
}: {
  reloadKey?: unknown
  /** Reports the total so the tab beside it can carry a count — the tab is otherwise a door with nothing on it. */
  onLoaded?: (total: number) => void
}) {
  const router = useRouter()
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setError(null)
      const page = await patientsApi.listPaged({
        pendingCalendarReviewOnly: true,
        page: 1,
        pageSize: SHOWN,
      })
      setPatients(page.items)
      setTotal(page.totalCount)
      onLoaded?.(page.totalCount)
    } catch {
      setError("Les patients à compléter n'ont pas pu être chargés.")
    } finally {
      setLoading(false)
    }
  }, [onLoaded])

  useEffect(() => {
    void load()
  }, [load, reloadKey])

  const name = (p: PatientDto) => `${p.firstName} ${p.lastName}`.trim()
  const importedOn = (p: PatientDto) =>
    p.calendarImportPendingReviewSince ? formatDateFr(p.calendarImportPendingReviewSince) : "—"

  /**
   * The row's actions. One button when the import resembled nobody — the ordinary case — and the duplicate
   * question stacked above it when it did.
   *
   * ⚠️ « Compléter » stays available alongside the question. A practice that cannot tell whether « Imen » and
   * « Iman » are one person must be able to open the fiche and look, and forcing an answer to the duplicate
   * question first would make the safest action the hardest one.
   */
  /**
   * One action per row, always the same one. ⚠️ The duplicate question is NOT here — it is a chip beside the
   * patient's name that opens a comparison dialog. Putting the question in this cell made the row twice as tall
   * as its neighbours and left three controls competing in it.
   */
  const buttons = (patient: PatientDto) => (
    <Button size="sm" className="coarse:h-11" onClick={() => router.push(`/patients/${patient.id}`)}>
      Compléter les infos patient
    </Button>
  )

  if (error) {
    return <LoadFailureNotice message={error} detail="Aucune fiche n'a été modifiée." onRetry={() => void load()} />
  }

  if (!loading && patients.length === 0) {
    return (
      <div className="rounded-md border bg-card p-6">
        <EmptyState
          icon={UserPlus}
          size="compact"
          chipClassName={zoneChipClass(ZONES.clinical)}
          title="Aucun patient à compléter"
          description="Les fiches créées depuis Google Agenda apparaissent ici jusqu'à ce que leurs informations soient complétées."
        />
      </div>
    )
  }

  return (
    <div className="rounded-md border bg-card">
      <Table containerClassName={TABLE_ONLY_LG}>
        <TableHeader sticky>
          <TableRow>
            <TableHead>Patient</TableHead>
            <TableHead>Origine</TableHead>
            <TableHead>Importé le</TableHead>
            <TableHead className="text-end">À faire</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {patients.map((patient) => (
            <TableRow key={patient.id}>
              {/* ⚠️ The chip sits with the NAME, not in « À faire ». The question is a remark about who this
                  record might be, so it belongs with the identity; the action column keeps exactly one control
                  per row, which is what makes the rows the same height. */}
              <TableCell className="font-medium">
                <div className="flex flex-col items-start gap-1.5">
                  <span>{name(patient)}</span>
                  <DuplicateSuggestionPrompt patient={patient} onResolved={() => void load()} />
                </div>
              </TableCell>
              <TableCell className="text-muted-foreground">Google Agenda</TableCell>
              <TableCell className="whitespace-nowrap text-muted-foreground">{importedOn(patient)}</TableCell>
              <TableCell className="text-end align-top">
                <div className="flex justify-end">{buttons(patient)}</div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <CardList
        className={CARDS_ONLY_LG}
        ariaLabel="Patients à compléter"
        items={patients}
        getKey={(patient) => patient.id}
        title={(patient) => name(patient)}
        subtitle={() => "Créé depuis Google Agenda"}
        /* `status` is the slot for a mark read WITH the identity, which is what this chip is — the card keeps one
           primary action, exactly like the table row. */
        status={(patient) => <DuplicateSuggestionPrompt patient={patient} onResolved={() => void load()} />}
        fields={(patient) => [{ label: "Importé le", value: importedOn(patient) }]}
        /* ⚠️ `primaryAction`, not `actions`. The primitive's own note names the defect the actions slot produces
           here: a labelled French button in the header row leaves the name a ~10 px column and it renders one
           character per line (seen at 390 px). Full width, below the fields — what the séances list beside it
           uses for the same reason. */
        primaryAction={(patient) => buttons(patient)}
      />

      {total > patients.length && (
        <div className="border-t p-3">
          <Button
            variant="ghost"
            size="sm"
            className="w-full coarse:h-11"
            onClick={() => router.push("/patients?pendingCalendarReview=1")}
          >
            Voir les {total.toLocaleString("fr-TN")} patients à compléter
          </Button>
        </div>
      )}
    </div>
  )
}
