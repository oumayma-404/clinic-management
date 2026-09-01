"use client"

import { useState, useEffect, useCallback, useMemo, type ReactNode } from "react"
import { toast } from "sonner"
import { Plus, Trash2, Stethoscope, ClipboardList, CheckSquare, Check, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Textarea } from "@/components/ui/textarea"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { cn } from "@/lib/utils"
import { odontogramApi } from "@/lib/api/odontogram"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import {
  DENTITION_VIEWS,
  DENTITION_VIEW_LABELS_FR,
  dentitionViewFor,
  dentitionViewForTeeth,
  type DentitionView,
} from "@/lib/dentition"
import type { ToothStateDto, ProcedureTypeDto, DentalRecordDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDateFr } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, serializeSurfaces } from "@/components/odontogram-conditions"
import { OdontogramActsChart } from "@/components/odontogram-acts-chart"
// One source for the FDI quadrant layout — `tooth-multiselect` is the client-side authority for a tooth's
// dentition (mirroring the backend `FdiTooth.IsAdult`), and this file used to carry a second copy.
import { TEETH_BY_VIEW, isAdultTooth } from "@/components/tooth-multiselect"
import { DentitionViewSwitch } from "@/components/dentition-view-switch"
import { ToothArchLayout, type ToothArch } from "@/components/tooth-arch-layout"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { quoteFr } from "@/lib/format"

// Max dots drawn under a tooth before collapsing the overflow into a "+N".
const MAX_DOTS = 4

// Conditions offerable as a diagnosis (everything except the implicit-healthy "Sain").
const DIAGNOSIS_CONDITIONS = CONDITION_ORDER.filter((c) => c !== "Sain")

const isDiagnosis = (entry: ToothStateDto) => entry.source === "Diagnosis"

/** One draft plan line seeded from an open diagnosis (consumed by the treatment-plan editor). */
export interface OdontogramPlanSeed {
  toothNumbers: number[]
  /**
   * The act to perform — a PROCEDURE, never the diagnosis.
   *
   * Filled with the matched procedure's name when the charted condition named exactly one, and left **empty**
   * otherwise, which is every pathology (Carie, À traiter…). It used to be the condition label
   * (« Carie — dent 15 »), so a devis built from the odontogram billed the diagnosis as if it were an act and
   * the dentist had to notice and retype every line.
   */
  designationFr: string
  /**
   * What was charted, for display only — « Carie — dent 15 ». Shown under the designation field so the
   * dentist can see what they are treating while choosing the act. Never persisted: a diagnosis is not a
   * billable line, and medical secrecy keeps it off the devis.
   */
  diagnosisLabel: string
  /** The charted condition itself, so the UI can colour the hint with that condition's own palette. */
  diagnosisCondition: string
  /** Prefilled planned cost from the matching procedure-type default (omitted when no catalog match). */
  plannedCost?: number
  /**
   * The procedure that treats the charted condition, when exactly one was matched. Carried through to the plan
   * act so booking it can preselect the procedure — the seeded designation is a condition label
   * (« Couronne — dent 16 »), so it would never resolve by name.
   */
  procedureTypeId?: string
}

interface OdontogramProps {
  patientId: string
  /**
   * The patient's stored dentition (`"Child"` | `"Adult"`) — which arch the chart **opens** on.
   *
   * A local Adulte/Enfant toggle defaulting to Adulte came first: it asked on every visit a question that is
   * largely a property of the patient, and a child's chart opened on the wrong teeth until someone flipped it. So
   * it became a pure derivation from this field — which then made the *mixed* stage unchartable, because a mouth
   * with both sets had no arch that showed it. Both halves are kept now: this seeds the view, and the
   * `DentitionViewSwitch` (Adulte / Enfant / **Mixte**) lets the dentist say otherwise. A charted tooth outside the
   * seeded view widens the seed on its own, so an existing diagnosis can never be hidden by the default.
   */
  dentition: string
  /**
   * The patient's date of birth, or null when none was recorded (AC-18).
   *
   * ⚠️ It is here to answer « is the seeded arch based on anything? ». `dentition` is never absent — the column is
   * NOT NULL and its entity default is `Adult` — so with no date of birth behind it, opening on the adult chart is
   * a guess wearing the clothes of a stored decision. That guess used to be manufactured server-side, where a
   * missing birthday became « thirty years ago » and every undated walk-in, child or not, was charted on permanent
   * teeth. With nothing charted yet either, this asks instead.
   */
  dateOfBirth?: string | null
  /** Called with one seed per tooth carrying an open diagnosis, to pre-fill a new treatment plan. */
  onCreatePlan?: (seeds: OdontogramPlanSeed[]) => void
}

export function Odontogram({ patientId, dentition, dateOfBirth, onCreatePlan }: OdontogramProps) {
  const [chosenView, setChosenView] = useState<DentitionView | null>(null)
  /**
   * « Plusieurs dents » — charting ONE diagnosis onto several teeth at once.
   *
   * <p>A carie on 16, 26 and 36 is one observation the dentist makes once, and the chart used to make them open a
   * popover, pick the condition, pick the faces, type the note and press save three times over. The mode lives
   * here rather than in `ToothCell` because it is a property of the whole chart: while it is on, a tap
   * *selects* instead of opening that tooth's editor, so exactly one component may own the answer.</p>
   */
  const [multiSelect, setMultiSelect] = useState(false)
  const [selectedTeeth, setSelectedTeeth] = useState<Set<number>>(new Set())
  /**
   * The condition chosen in « Plusieurs dents », held HERE rather than in the panel that owns the form — because
   * what it paints is the arch, and the panel cannot reach it.
   *
   * <p>The tooth used to keep its old colour until the write came back, so the dentist chose « Carie », looked up
   * at the chart and saw nothing had happened. It now answers the choice immediately.</p>
   *
   * <p>⚠️ `null` until a condition is actually picked, and that is the whole reason this is not simply seeded with
   * `DIAGNOSIS_CONDITIONS[0]`. The Select opens *showing* « Carie », so previewing its initial value would paint
   * every tooth red the moment it was ticked — the chart would be asserting a diagnosis nobody made.</p>
   */
  const [pendingCondition, setPendingCondition] = useState<string | null>(null)
  const [byTooth, setByTooth] = useState<Map<number, ToothStateDto[]>>(new Map())
  // The patient's fiches, joined to the treatment-sourced states for the act names.
  const [records, setRecords] = useState<DentalRecordDto[]>([])
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The act catalogue read failed — so a seeded plan would carry no tarifs. Distinct from "no acts configured". */
  const [catalogFailed, setCatalogFailed] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await odontogramApi.get(patientId)
      // Group entries by tooth, newest first within each tooth.
      const map = new Map<number, ToothStateDto[]>()
      for (const entry of data) {
        const list = map.get(entry.toothNumber) ?? []
        list.push(entry)
        map.set(entry.toothNumber, list)
      }
      for (const list of map.values()) {
        list.sort((a, b) => new Date(b.treatmentDate).getTime() - new Date(a.treatmentDate).getTime())
      }
      setByTooth(map)

      // The fiches, for the act NAMES in the « Actes réalisés » tab: a tooth state carries the resulting
      // condition but not the act that produced it. Fetched here rather than passed in so this component stays
      // self-loading (and so the realtime refetch below covers both halves). Best-effort — a failure leaves the
      // acts tab falling back to the condition label rather than breaking the diagnosis chart beside it.
      try {
        setRecords(await dentalRecordsApi.list(patientId))
      } catch {
        setRecords([])
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec du chargement de l'odontogramme.")
    } finally {
      setLoading(false)
    }
  }, [patientId])

  useEffect(() => {
    load()
  }, [load])

  /*
   * Procedure catalog — used only to prefill a seeded plan line's cost (by resulting condition).
   *
   * ⚠️ A failure is **recorded**, not written back as `[]`. The empty write was a no-op (the state starts empty)
   * that produced a wrong *number*: with no catalogue every seed's `matchedCost` falls back to 0, so
   * « Créer un plan depuis l'odontogramme » would quietly produce a devis of free treatment. Nothing on the chart
   * said so, because a missing tarif and a tarif of zero are the same value.
   */
  const loadCatalog = useCallback(async () => {
    try {
      setProcedureTypes(await procedureTypesApi.list(false))
      setCatalogFailed(false)
    } catch {
      setCatalogFailed(true)
    }
  }, [])

  useEffect(() => {
    void loadCatalog()
  }, [loadCatalog])

  // The odontogram also changes through the dental-record flow (broadcasts "patients"), so refresh live.
  useClinicRealtime(RealtimeResource.Patients, load)

  /*
   * The view: the user's choice if they made one, else the widest of "what the patient is" and "what is already
   * charted".
   *
   * The second half matters more than it looks. A child charted `Adult` whose 75 carries a diagnosis would, on the
   * patient's value alone, open on an arch that does not draw 75 — so the chart would assert « rien sur cette dent »
   * about a tooth it simply refuses to show. Widening the seed from `byTooth` means an existing diagnosis is never
   * hidden by a default; the switch still overrides it in either direction.
   *
   * ⚠️ Late-binding on purpose (`chosenView === null` ≠ "adult"): `byTooth` is populated by an async read, so a
   * `useState` seed would be computed on the frame before the data arrived and never revised.
   */
  const dentitionView = useMemo<DentitionView>(() => {
    if (chosenView) return chosenView
    const seeded = dentitionViewFor(dentition)
    const charted = dentitionViewForTeeth(Array.from(byTooth.keys()), isAdultTooth)
    if (!charted || charted === seeded) return seeded
    return "mixed"
  }, [chosenView, dentition, byTooth])

  const teeth = TEETH_BY_VIEW[dentitionView]

  /**
   * How many charted teeth this view does not show.
   *
   * ⚠️ **A chart that silently omits a recorded state is the one failure a clinical chart may not have.** The
   * default view widens to Mixte on its own when the charted teeth need it, so this is only ever reached by an
   * explicit switch — pressing « Adulte » on a patient with a charted deciduous 55 dropped it from the chart with
   * no notice at all, and the chart then read as « nothing recorded there ». It is a `role="status"` line rather
   * than a toast: the omission is true for as long as the view is, and a message that expires after four seconds
   * would leave the wrong chart on screen saying nothing.
   */
  const chartedOutOfView = useMemo(() => {
    // `teeth` is quadrant-shaped (`ToothQuadrants`), not a flat list — the chart draws four arches.
    const shown = new Set([...teeth.upperRight, ...teeth.upperLeft, ...teeth.lowerRight, ...teeth.lowerLeft])
    return Array.from(byTooth.keys()).filter((tooth) => !shown.has(tooth)).length
  }, [teeth, byTooth])

  /**
   * Nothing tells us which arch to open on: no date of birth, nothing charted, and no choice made this session.
   * The chart asks rather than opening on the adult set (AC-18) — a six-year-old's deciduous teeth are simply
   * absent from that arch, so the wrong default is not a cosmetic default.
   */
  const mustAskDentition =
    !dateOfBirth && chosenView === null && byTooth.size === 0

  // A charted diagnosis names the desired end-state (e.g. "Couronne"); a procedure whose ResultingCondition
  // is that state is its treatment, so its default cost is the planned cost. Pathology diagnoses (Carie…)
  // have no such procedure and fall back to no prefill (0) — as allowed by the spec.
  const procedureByCondition = useMemo(() => {
    const map = new Map<string, ProcedureTypeDto>()
    for (const pt of procedureTypes) {
      if (pt.resultingCondition && !map.has(pt.resultingCondition)) {
        map.set(pt.resultingCondition, pt)
      }
    }
    return map
  }, [procedureTypes])

  // Open diagnoses (not yet treated), one seed per tooth, for "create a plan from the odontogram".
  const planSeeds = useMemo<OdontogramPlanSeed[]>(() => {
    const seeds: OdontogramPlanSeed[] = []
    for (const [tooth, entries] of Array.from(byTooth.entries()).sort((a, b) => a[0] - b[0])) {
      const diagnoses = entries.filter(isDiagnosis)
      if (diagnoses.length === 0) continue
      const conditions = Array.from(new Set(diagnoses.map((d) => d.condition)))
      const labels = conditions.map((c) => conditionStyle(c).label)
      const matchedCost = conditions.reduce(
        (sum, c) => sum + (procedureByCondition.get(c)?.defaultCost ?? 0),
        0,
      )
      // Only a single charted condition names a single procedure. A tooth carrying two diagnoses becomes one
      // aggregated line whose cost is the sum, and no one procedure speaks for it — so it carries no link
      // rather than an arbitrary one.
      const soleProcedure = conditions.length === 1 ? procedureByCondition.get(conditions[0]) : undefined
      seeds.push({
        toothNumbers: [tooth],
        // The PROCEDURE, when the condition named exactly one — and its name, not the condition's label, so
        // it agrees with the procedureTypeId and plannedCost carried alongside. Blank for a pathology, which
        // is the whole point: the dentist chooses how to treat a carie; the app must not choose for them.
        designationFr: soleProcedure?.name ?? "",
        // The diagnosis travels separately, as context rather than as content.
        diagnosisLabel: `${labels.join(", ")} — dent ${tooth}`,
        diagnosisCondition: conditions.length === 1 ? conditions[0] : "",
        plannedCost: matchedCost > 0 ? matchedCost : undefined,
        procedureTypeId: soleProcedure?.id,
      })
    }
    return seeds
  }, [byTooth, procedureByCondition])

  /**
   * Which arch the phone opens on. Below `md:` `ToothArchLayout` shows one at a time and used to always start on
   * MAXILLAIRE, so a patient charted only on the mandible cost a tap before a single tooth was visible.
   *
   * Lowest charted FDI number decides — a `Map`'s iteration order is insertion order, i.e. whatever order the API
   * happened to return, which would make the answer differ between two loads of the same patient. Quadrants 1/2
   * (permanent) and 5/6 (deciduous) are maxillary.
   */
  const defaultArch = useMemo<ToothArch | undefined>(() => {
    let lowest: number | undefined
    for (const [tooth, entries] of byTooth) {
      if (entries.length === 0) continue
      if (lowest === undefined || tooth < lowest) lowest = tooth
    }
    if (lowest === undefined) return undefined
    const quadrant = Math.floor(lowest / 10)
    return quadrant === 1 || quadrant === 2 || quadrant === 5 || quadrant === 6 ? "upper" : "lower"
  }, [byTooth])

  const toggleSelectedTooth = useCallback((tooth: number) => {
    setSelectedTeeth((prev) => {
      const next = new Set(prev)
      if (next.has(tooth)) next.delete(tooth)
      else next.add(tooth)
      return next
    })
  }, [])

  /* Leaving the mode drops the selection: a set of ticked teeth that survives invisibly would come back the next
     time the mode is switched on and apply a diagnosis to teeth nobody has looked at since. */
  const setMultiSelectMode = useCallback((on: boolean) => {
    setMultiSelect(on)
    setSelectedTeeth(new Set())
    // Leaving the choice behind would repaint the next selection with the previous session's condition.
    setPendingCondition(null)
  }, [])

  return (
    <div className="w-full space-y-3">
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
          {error}
        </div>
      )}

      {loading ? (
        <p className="py-8 text-center text-muted-foreground">Chargement de l'odontogramme…</p>
      ) : mustAskDentition ? (
        <div
          role="status"
          className="flex flex-col items-center gap-4 rounded-lg border border-dashed bg-muted/40 px-4 py-8 text-center"
        >
          <div>
            <p className="text-sm font-medium text-foreground">Quelle dentition charter ?</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Ce patient n&apos;a pas de date de naissance enregistrée, donc l&apos;arcade ne peut pas être déduite.
              Choisissez-la — vous pourrez en changer à tout moment.
            </p>
          </div>
          <div className="flex flex-wrap justify-center gap-2">
            {DENTITION_VIEWS.map((view) => (
              <Button
                key={view}
                variant="outline"
                onClick={() => setChosenView(view)}
                className="coarse:h-11 coarse:px-5"
              >
                {DENTITION_VIEW_LABELS_FR[view]}
              </Button>
            ))}
          </div>
        </div>
      ) : (
        /* Two views over the same mouth. « Diagnostics » is the chart that has always been here and stays the
           default — it is where charting happens. « Actes réalisés » is read-only and reflects what the fiches
           recorded, which the server writes on its own. Both read the arch from **one** `dentitionView` above the
           tabs, so there is no per-tab setting that could disagree. */
        <Tabs defaultValue="diagnostics" className="w-full">
          {/* The view switch and the create-plan action share one row.
              They used to be two stacked rows — the button right-aligned on its own line, the tabs left-aligned on
              the next — which spent two rows of chrome directly above the chart the page exists to show. They pair
              naturally: both act on the whole odontogram, and putting them at opposite ends of one row reads as
              « which view » on the left and « what to do with it » on the right. */}
          <div className="flex flex-wrap items-center justify-between gap-2">
            {/* The dentition switch sits beside the view tabs, not inside a tab body: it applies to **both**
                charts, and a per-tab copy could have the Diagnostics arch disagreeing with the Actes one. */}
            <div className="flex flex-wrap items-center gap-2">
              <TabsList>
                <TabsTrigger value="diagnostics">Diagnostics</TabsTrigger>
                <TabsTrigger value="acts">Actes réalisés</TabsTrigger>
              </TabsList>
              <DentitionViewSwitch value={dentitionView} onChange={setChosenView} />
              {chartedOutOfView > 0 && (
                <button
                  type="button"
                  role="status"
                  onClick={() => setChosenView("mixed")}
                  className="rounded-md border border-warning/40 bg-warning-wash px-2 py-1 text-2xs font-medium text-warning-ink underline-offset-2 hover-hover:hover:underline coarse:py-2"
                >
                  {chartedOutOfView === 1
                    ? "1 état hors de cette vue — tout afficher"
                    : `${chartedOutOfView} états hors de cette vue — tout afficher`}
                </button>
              )}
            </div>
            {onCreatePlan && (
              /* ⚠️ The label shortens below `sm:`, and the `aria-label` carries the full phrase at every width.
                 « Créer un plan depuis l'odontogramme » measures 253 px against the 223 px this row has at
                 320 px, and `Button` is `whitespace-nowrap shrink-0` — so the wording, not the layout, was what
                 pushed a control out through the card's edge. Shortening the *visible* half loses nothing here:
                 the button sits directly under the odontogramme it acts on, so « depuis l'odontogramme » is the
                 one part of the sentence the context already supplies. */
              <Button
                size="sm"
                variant="outline"
                className="h-7 max-w-full gap-1.5 text-xs"
                disabled={planSeeds.length === 0}
                onClick={() => onCreatePlan(planSeeds)}
                aria-label="Créer un plan depuis l'odontogramme"
                title={planSeeds.length === 0 ? "Aucun diagnostic à planifier" : undefined}
              >
                <ClipboardList className="h-3.5 w-3.5" aria-hidden="true" />
                Créer un plan
                <span className="hidden sm:inline">&nbsp;depuis l&apos;odontogramme</span>
              </Button>
            )}
          </div>

          {/* Said where the consequence is: a plan seeded without the catalogue carries no tarifs, and « 0,000 DT »
              is indistinguishable from « gratuit ». Only shown where the action exists. */}
          {onCreatePlan && catalogFailed && (
            <LoadFailureNotice
              variant="inline"
              message="Les tarifs du catalogue n'ont pas pu être chargés."
              detail="Un plan créé depuis l'odontogramme partira sans montants."
              onRetry={() => void loadCatalog()}
              className="mt-2"
            />
          )}

          {/* The instruction line that stood here is gone: it repeated the card's own description almost word for
              word, so the same sentence was on screen twice and cost a third row. The card header keeps it. */}
          <TabsContent value="diagnostics" className="mt-3 space-y-2">
            {/* ⚠️ The toggle is a **permanent, labelled control directly above the teeth**, not an option behind a
                menu or a modifier key. The whole point of the feature is that a dentist who has just charted the
                same carie on three molars one at a time discovers there was a faster way without being told — a
                ctrl-click or a long-press would have been cheaper to build and invisible to everyone who did not
                already know it was there. It sits inside the Diagnostics tab because « Actes réalisés » is
                read-only and has nothing to select teeth for. */}
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <Button
                type="button"
                size="sm"
                variant={multiSelect ? "default" : "outline"}
                aria-pressed={multiSelect}
                onClick={() => setMultiSelectMode(!multiSelect)}
                title="Noter le même diagnostic sur plusieurs dents en une fois"
                className="h-8 gap-1.5 text-xs coarse:h-11"
              >
                <CheckSquare className="h-3.5 w-3.5" aria-hidden="true" />
                Plusieurs dents
              </Button>
              <p className="text-xs text-muted-foreground">
                {multiSelect
                  ? "Touchez les dents concernées, puis notez le diagnostic commun sous l'arcade."
                  : "Même diagnostic sur plusieurs dents ? Activez « Plusieurs dents »."}
              </p>
            </div>

        {/* Geometry from `ToothArchLayout`. `ToothCell` keeps its own editor Popover and its per-cell state —
            the layout takes no open/hover state, which is what stops one arch's worth of editors from being
            addressable at once. */}
        <ToothArchLayout
          teeth={teeth}
          defaultArch={defaultArch}
          renderTooth={(t) => (
            <ToothCell
              key={t}
              toothNum={t}
              entries={byTooth.get(t) ?? []}
              patientId={patientId}
              onChanged={load}
              selectionMode={multiSelect}
              isSelected={selectedTeeth.has(t)}
              onToggleSelect={toggleSelectedTooth}
              previewCondition={multiSelect && selectedTeeth.has(t) ? pendingCondition : null}
            />
          )}
        />

            {multiSelect && (
              <MultiToothDiagnosisPanel
                patientId={patientId}
                selectedTeeth={selectedTeeth}
                onClearSelection={() => setSelectedTeeth(new Set())}
                onKeepOnlyFailed={(failed) => setSelectedTeeth(new Set(failed))}
                onChanged={load}
                condition={pendingCondition}
                onConditionChange={setPendingCondition}
              />
            )}

            {/* The condition palette belongs to THIS chart. It used to sit outside the tabs, so all nine
                conditions were also listed under « Actes réalisés » — a palette that view does not use. */}
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
              {CONDITION_ORDER.map((c) => (
                <div key={c} className="flex items-center gap-1.5">
                  <span className={cn("h-4 w-4 rounded border", conditionStyle(c).swatch)} />
                  <span className="text-muted-foreground">{conditionStyle(c).label}</span>
                </div>
              ))}
              <div className="flex items-center gap-1.5">
                <span className="h-4 w-4 rounded border-2 border-dashed border-muted-foreground/60" />
                <span className="text-muted-foreground">Diagnostic (à traiter)</span>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="acts" className="mt-3">
            <OdontogramActsChart teeth={teeth} records={records} procedureTypes={procedureTypes} />
          </TabsContent>
        </Tabs>
      )}

    </div>
  )
}

interface ToothCellProps {
  toothNum: number
  entries: ToothStateDto[]
  patientId: string
  onChanged: () => void
  /** « Plusieurs dents » is on: a tap ticks this tooth instead of opening its editor. */
  selectionMode: boolean
  isSelected: boolean
  onToggleSelect: (toothNumber: number) => void
  /**
   * A condition chosen in « Plusieurs dents » but not yet written — paint the box with it now. `null` when this
   * tooth is not part of a pending multi-tooth diagnosis, which is every tooth outside that mode.
   */
  previewCondition?: string | null
}

function ToothCell({
  toothNum,
  entries,
  patientId,
  onChanged,
  selectionMode,
  isSelected,
  onToggleSelect,
  previewCondition = null,
}: ToothCellProps) {
  const [open, setOpen] = useState(false)
  /**
   * This popover's own chosen condition — `null` until the dentist picks one, so that merely *opening* a tooth
   * does not paint it with the Select's initial « Carie ». Falls back to that same first entry when saved
   * untouched, which is what the Select has been displaying all along.
   */
  const [condition, setCondition] = useState<string | null>(null)
  const [note, setNote] = useState("")
  const [surfaces, setSurfaces] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)

  const toggleSurface = (code: string) => {
    setSurfaces((prev) => {
      const next = new Set(prev)
      if (next.has(code)) next.delete(code)
      else next.add(code)
      return next
    })
  }

  const latest = entries[0]

  /**
   * The condition the box is painted with: a **pending** choice wins over the stored state.
   *
   * <p>Both halves of the chart write through this. `previewCondition` is « Plusieurs dents »' shared choice,
   * `condition` is this popover's own — and the popover's only counts while it is open, so an abandoned form
   * leaves nothing painted behind it.</p>
   */
  const preview = previewCondition ?? (open ? condition : null)
  const style = conditionStyle(preview ?? latest?.condition ?? "Sain")
  // A pending choice is a diagnosis, so it takes the dashed border the legend already explains as « à traiter ».
  const latestIsDiagnosis = preview !== null || (latest ? isDiagnosis(latest) : false)

  const handleDiagnose = async () => {
    try {
      setSaving(true)
      await odontogramApi.diagnose(patientId, {
        toothNumber: toothNum,
        condition: condition ?? DIAGNOSIS_CONDITIONS[0],
        surfaces: serializeSurfaces(surfaces) || null,
        note: note.trim() || null,
      })
      toast.success(`Diagnostic ajouté (dent ${toothNum})`)
      setNote("")
      setSurfaces(new Set())
      setCondition(null)
      setOpen(false)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement du diagnostic.")
    } finally {
      setSaving(false)
    }
  }

  /**
   * The entry the dentist asked to remove, held while the confirm dialog is open.
   *
   * <p>Removal used to fire on a single click of a 10px text link inside a tooth popover — a destructive write with
   * no confirmation, against the repo's own rule that destructive flows go through `ui/alert-dialog`. In a chart of
   * 32 targets a few pixels apart, that is one slip away from deleting real charting.</p>
   */
  const [pendingRemoval, setPendingRemoval] = useState<ToothStateDto | null>(null)
  const [removing, setRemoving] = useState(false)

  const handleRemove = async () => {
    if (!pendingRemoval) return
    setRemoving(true)
    try {
      await odontogramApi.removeCondition(patientId, pendingRemoval.id)
      toast.success(`Diagnostic retiré (dent ${toothNum})`)
      setPendingRemoval(null)
      onChanged()
    } catch (err) {
      // Leave the dialog open on failure so the refusal is read where the action was taken — the server's message
      // is the authority (e.g. a treatment-sourced entry cannot be removed here).
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression du diagnostic.")
    } finally {
      setRemoving(false)
    }
  }

  const box = (
    <span className="flex flex-col items-center">
      <span
        /* The selection ring is painted on the tooth BOX, not on the wrapping button: on a coarse pointer the
           button grows to `min-w-11` while the box stays 28px, so a ring on the button would float a centimetre
           away from the tooth it is meant to be marking. `ring-offset` keeps it clear of the condition fill,
           which is already a saturated colour on a charted tooth. */
        className={cn(
          "relative flex h-9 w-7 items-center justify-center rounded-md border text-2xs font-semibold",
          style.box,
          latestIsDiagnosis && "border-2 border-dashed",
          selectionMode && isSelected && "ring-2 ring-primary ring-offset-1 ring-offset-background",
        )}
      >
        {latest?.surfaces ?? ""}
        {selectionMode && isSelected && (
          <span
            aria-hidden="true"
            className="absolute -right-1 -top-1 flex size-3.5 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-sm"
          >
            <Check className="size-2.5" strokeWidth={3} />
          </span>
        )}
      </span>
      <span
        className={cn(
          "mt-0.5 text-2xs font-medium",
          selectionMode && isSelected ? "font-semibold text-primary" : "text-muted-foreground",
        )}
      >
        {toothNum}
      </span>
      {entries.length > 0 && (
        <span className="mt-0.5 flex items-center gap-0.5">
          {entries.slice(0, MAX_DOTS).map((e) => (
            // The fill is an inline style, not `swatch`, for the same reason odontogram-acts-chart uses one:
            // `cn` is tailwind-merge, so the old `cn("border", swatch, "bg-transparent")` resolved two
            // conflicting `bg-*` utilities by keeping the LAST — silently deleting the condition colour and
            // leaving a 1px grey ring with no fill. `swatch` carries only a background (`bg-red-500`), so there
            // was no border colour to fall back on either: every diagnosis dot rendered neutral.
            //
            // The ring keeps diagnostic-vs-réalisé legible without costing the colour, which is what the hollow
            // dot was reaching for. The tooth box's dashed border and the panel's badge say it too.
            <span
              key={e.id}
              className={cn("h-1.5 w-1.5 rounded-full", isDiagnosis(e) && "ring-1 ring-foreground/40")}
              style={{ backgroundColor: conditionStyle(e.condition).color }}
            />
          ))}
          {entries.length > MAX_DOTS && (
            <span className="text-2xs font-medium text-muted-foreground">+{entries.length - MAX_DOTS}</span>
          )}
        </span>
      )}
    </span>
  )

  /*
    Hover reveals what is charted on the tooth — the acts chart does the same, but it can put that in its
    Popover because a click there has nothing else to do. Here the Popover IS the editor (condition, faces,
    note, save, retirer), so opening it on hover would pop a form open for every tooth the pointer crosses.
    A read-only Tooltip gives the same information without taking the click.

    It replaced `title={`Dent ${toothNum}`}`, a native tooltip whose entire content was the tooth number —
    which is already printed under the box. Radix dismisses a tooltip on pointer-down, so it gets out of the
    way by itself when the editor opens; no coordinating state needed.

    An untouched tooth gets no tooltip: it has nothing to report, and a hover affordance promising otherwise
    is worse than none (same rule as odontogram-acts-chart). It is a function rather than inline JSX because
    « Plusieurs dents » swaps the trigger underneath it — the reading of a tooth does not change with the mode.
  */
  const withTooltip = (node: ReactNode) =>
    entries.length === 0 ? (
      node
    ) : (
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>{node}</TooltipTrigger>
          <TooltipContent side="top" align="center" className="max-w-xs">
            <p className="mb-1 font-semibold">Dent {toothNum}</p>
            <ul className="space-y-0.5">
              {entries.map((e) => (
                <li key={e.id} className="flex items-center gap-1.5">
                  <span
                    className={cn("h-2 w-2 shrink-0 rounded-full", isDiagnosis(e) && "ring-1 ring-foreground/40")}
                    style={{ backgroundColor: conditionStyle(e.condition).color }}
                  />
                  <span>{conditionStyle(e.condition).label}</span>
                  <span className="text-muted-foreground">
                    — {isDiagnosis(e) ? "Diagnostic" : "Réalisé"} · {formatDateFr(e.treatmentDate)}
                  </span>
                </li>
              ))}
            </ul>
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    )

  /*
    « Plusieurs dents » is on, so this tooth is a checkbox and NOT an editor.
    
    ⚠️ The Popover is not rendered at all in this branch rather than merely left closed. A tooth that both ticks
    and opens its own form would let the dentist chart 16 in the popover while 16, 26 and 36 sit ticked below —
    two half-finished diagnoses on screen at once, and no way to tell which one « Ajouter » meant.
  */
  if (selectionMode) {
    return withTooltip(
      <button
        type="button"
        role="checkbox"
        aria-checked={isSelected}
        aria-label={`Dent ${toothNum}`}
        onClick={() => onToggleSelect(toothNum)}
        // Same `coarse:min-w-11` reasoning as the editor trigger below: grow the paint, never overlay a 44px
        // target onto a 28px cell that a neighbour then wins.
        className="group rounded-md transition-all focus:outline-none focus:ring-1 focus:ring-ring coarse:min-w-11 hover-hover:hover:scale-105"
      >
        {box}
      </button>,
    )
  }

  const trigger = (
    <PopoverTrigger asChild>
      <button
        type="button"
        aria-label={
          entries.length === 0
            ? `Dent ${toothNum} — aucun état enregistré`
            : `Dent ${toothNum} — ${entries.length} état${entries.length > 1 ? "s" : ""} enregistré${entries.length > 1 ? "s" : ""}`
        }
        /*
         * Movement hover gated behind `hover-hover:` per the policy in globals.css: a tap fires `:hover` and
         * leaves it applied, so on a tablet the tooth stayed enlarged and read as a stuck selection (AC-11).
         *
         * ⚠️ `coarse:min-w-11` and deliberately NOT `touch-target`, which is what stood here. The painted cell
         * is `h-9 w-7` (28px) on a `gap-0.5` row, so a centred 44px overlay reached 8px into each neighbour;
         * both cells are `position: relative` with `z-index: auto`, so the LATER sibling won and the right edge
         * of every tooth opened the editor for the tooth beside it — one tap from charting a diagnosis on the
         * wrong tooth. Widening the paint is safe here for the same reason as in `record-tooth-chart`: the arch
         * lives in `ToothArchLayout`'s `overflow-x-auto` scroll box, so wider cells scroll rather than clip.
         * `box` is a block-level flex column, so it fills the widened button and stays centred.
         */
        className="group rounded-md transition-all focus:outline-none focus:ring-1 focus:ring-ring coarse:min-w-11 hover-hover:hover:scale-105"
      >
        {box}
      </button>
    </PopoverTrigger>
  )

  /* Closing drops the pending choice: a form the dentist walked away from must not leave the tooth painted with
     a diagnosis that was never saved. */
  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setCondition(null)
      }}
    >
      {withTooltip(trigger)}
      {/*
        ⚠️ `max-h-[70dvh] overflow-y-auto` — Radix does not bound a popover's height, and this one grows without
        limit: it lists EVERY recorded state for the tooth and then carries the whole add-diagnosis form
        (condition, MODVL faces, note, save). A molar with a few charted states already renders taller than a
        phone, and « Ajouter le diagnostic » sits at the very bottom — so the control the popover exists for
        became unreachable, with nothing to scroll because the overflow was the popover itself.

        `dvh`, not `vh`, for the reason `check-responsive`'s `sheet-vh` states: `vh` does not shrink when the
        on-screen keyboard opens, and this panel contains a textarea.
      */}
      <PopoverContent className="w-80 max-h-[70dvh] space-y-3 overflow-y-auto" align="center">
        <div>
          <p className="text-sm font-semibold">Dent {toothNum}</p>
          <p className="text-xs text-muted-foreground">
            {entries.length === 0
              ? "Aucun état enregistré"
              : `${entries.length} état${entries.length > 1 ? "s" : ""} enregistré${entries.length > 1 ? "s" : ""}`}
          </p>
        </div>

        {entries.length > 0 && (
          <ul className="space-y-2">
            {entries.map((e) => (
              <li key={e.id} className="rounded-md border p-2 text-xs">
                <div className="flex items-center gap-2">
                  <span className={cn("h-2.5 w-2.5 shrink-0 rounded-full border", conditionStyle(e.condition).swatch)} />
                  <span className="font-medium text-foreground">{conditionStyle(e.condition).label}</span>
                  <span
                    className={cn(
                      "rounded px-1 py-0.5 text-2xs font-medium",
                      isDiagnosis(e)
                        ? "bg-orange-100 text-orange-700 dark:bg-orange-950 dark:text-orange-300"
                        : "bg-muted text-muted-foreground",
                    )}
                  >
                    {isDiagnosis(e) ? "Diagnostic" : "Réalisé"}
                  </span>
                  <span className="ml-auto text-muted-foreground">{formatDateFr(e.treatmentDate)}</span>
                </div>
                {e.surfaces && <p className="mt-1 text-muted-foreground">Faces : {e.surfaces.split("").join(", ")}</p>}
                {e.note && <p className="mt-1 text-foreground">{e.note}</p>}
                {isDiagnosis(e) ? (
                  /* A real, hit-able control rather than the 10px text link this used to be: correcting a
                     mis-charted tooth is routine, and an affordance nobody can find is the same as none. */
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => setPendingRemoval(e)}
                    aria-label={`Retirer le diagnostic ${conditionStyle(e.condition).label} de la dent ${toothNum}`}
                    className="mt-1.5 h-7 gap-1.5 px-2 text-xs text-destructive hover:bg-destructive/10 hover:text-destructive"
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Retirer ce diagnostic
                  </Button>
                ) : (
                  /* A treatment-sourced state is deliberately NOT removable here — the server refuses it, because
                     deleting it would erase the chart while its fiche still says the act was done. Saying so is the
                     point: before, these rows simply had no button and no explanation, which reads as "the app
                     won't let me fix my mistake". */
                  <p className="mt-1.5 flex items-start gap-1.5 text-2xs text-muted-foreground">
                    <ClipboardList className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
                    <span>Acte réalisé — se corrige via sa fiche de soins, pas ici.</span>
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}

        {/* Add-diagnosis form */}
        <div className="space-y-2 border-t pt-2">
          <p className="flex items-center gap-1.5 text-xs font-medium text-foreground">
            <Stethoscope className="h-3.5 w-3.5" /> Noter un diagnostic
          </p>
          <Select value={condition ?? DIAGNOSIS_CONDITIONS[0]} onValueChange={setCondition}>
            <SelectTrigger className="h-8 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {DIAGNOSIS_CONDITIONS.map((c) => (
                <SelectItem key={c} value={c} className="text-xs">
                  {conditionStyle(c).label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* Surfaces (MODVL) — optional, finding #19.
              `gap-2` + `coarse:h-11`: five 28px buttons at `gap-1` are a 32px pitch, and `buttonVariants`
              already overlays each with a 44px `touch-target` — so 12px of every pair overlapped and the later
              sibling won, recording the act on the wrong surface. Painting the height on a coarse pointer makes
              the hit area equal the button again; the wider gap keeps the row honest. `coarse:h-11` rather than
              `coarse:size-11` so it stays in the same tailwind-merge group as the base `h-7` and reliably wins. */}
          <div className="flex flex-wrap gap-2">
            {Object.entries(SURFACE_LABELS).map(([code, label]) => (
              <Button
                key={code}
                type="button"
                variant={surfaces.has(code) ? "default" : "outline"}
                size="sm"
                className="h-7 px-2 text-xs coarse:h-11 coarse:min-w-11"
                title={label}
                aria-pressed={surfaces.has(code)}
                onClick={() => toggleSurface(code)}
              >
                {code}
              </Button>
            ))}
          </div>
          <Textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Note (facultative)"
            className="min-h-[52px] text-xs"
          />
          {/* The popover's primary action, at 32px. `coarse:h-11` paints the floor rather than overlaying it —
              it is the last control in the panel, so an overlay would hang past the popover's own edge. */}
          <Button
            size="sm"
            className="h-8 w-full gap-1.5 text-xs coarse:h-11"
            onClick={handleDiagnose}
            disabled={saving}
          >
            <Plus className="h-3.5 w-3.5" />
            {saving ? "Enregistrement…" : "Ajouter le diagnostic"}
          </Button>
        </div>
      </PopoverContent>

      {/* Rendered inside the Popover but outside PopoverContent so closing the popover does not unmount the dialog
          mid-confirmation. Naming the tooth and the condition matters here: the whole point is correcting a state
          charted on the WRONG tooth, so the dialog has to let the dentist check they are undoing the right one. */}
      <AlertDialog open={pendingRemoval !== null} onOpenChange={(o) => !o && setPendingRemoval(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Retirer ce diagnostic ?</AlertDialogTitle>
            <AlertDialogDescription>
              {pendingRemoval && (
                <>
                  {quoteFr(conditionStyle(pendingRemoval.condition).label)} sera retiré de la{" "}
                  <span className="font-medium text-foreground">dent {toothNum}</span>. Cette entrée disparaîtra de
                  l&apos;odontogramme. Les actes réalisés ne sont pas affectés.
                </>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={removing}>Annuler</AlertDialogCancel>
            {/* A plain Button, not AlertDialogAction: an AlertDialogAction closes the dialog on click, so a failed
                removal would dismiss the dialog and hide the reason. */}
            <Button variant="destructive" onClick={handleRemove} disabled={removing}>
              {removing ? "Suppression…" : "Retirer le diagnostic"}
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Popover>
  )
}

interface MultiToothDiagnosisPanelProps {
  patientId: string
  selectedTeeth: Set<number>
  onClearSelection: () => void
  /** After a partial failure, leave ticked exactly the teeth that did not land — see `handleSave`. */
  onKeepOnlyFailed: (failed: number[]) => void
  onChanged: () => void
  /**
   * The chosen condition, owned by the chart rather than by this panel — the ticked teeth are painted with it
   * before it is saved, and they are not this component's to paint. `null` until one is actually picked.
   */
  condition: string | null
  onConditionChange: (condition: string) => void
}

/**
 * One diagnosis, written onto every ticked tooth.
 *
 * <p>It is the tooth popover's own « Noter un diagnostic » form — same condition list, same MODVL faces, same
 * note — lifted out to where it can speak for several teeth. Deliberately a panel under the arch and not a
 * dialog: the selection it acts on is *on the chart*, and a modal would cover the very thing the dentist is
 * checking before they press save.</p>
 *
 * <p>It renders as soon as the mode is on, with nothing selected yet, so it can say what to do rather than
 * appearing out of nowhere after the first tap.</p>
 */
function MultiToothDiagnosisPanel({
  patientId,
  selectedTeeth,
  onClearSelection,
  onKeepOnlyFailed,
  onChanged,
  condition,
  onConditionChange,
}: MultiToothDiagnosisPanelProps) {
  /* The Select has been *showing* the first condition all along, so that is what an untouched form saves — only
     the preview waits for a deliberate choice. */
  const effectiveCondition = condition ?? DIAGNOSIS_CONDITIONS[0]
  const [note, setNote] = useState("")
  const [surfaces, setSurfaces] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)

  const teeth = useMemo(() => Array.from(selectedTeeth).sort((a, b) => a - b), [selectedTeeth])

  const toggleSurface = (code: string) => {
    setSurfaces((prev) => {
      const next = new Set(prev)
      if (next.has(code)) next.delete(code)
      else next.add(code)
      return next
    })
  }

  /**
   * Sequential, one POST per tooth — there is no bulk endpoint, and inventing one client-side by firing them all
   * at once would hand the same patient aggregate to N concurrent writers.
   *
   * <p>⚠️ A partial failure is reported as a partial failure and the teeth that DID land are untucked, leaving
   * only the ones that did not. Pressing « Ajouter » again then retries exactly what is missing instead of
   * charting a second copy on the teeth that already have it — which is what a plain "réessayer" would do.</p>
   */
  const handleSave = async () => {
    setSaving(true)
    const failed: number[] = []
    for (const tooth of teeth) {
      try {
        await odontogramApi.diagnose(patientId, {
          toothNumber: tooth,
          condition: effectiveCondition,
          surfaces: serializeSurfaces(surfaces) || null,
          note: note.trim() || null,
        })
      } catch {
        failed.push(tooth)
      }
    }
    setSaving(false)
    onChanged()

    const label = conditionStyle(effectiveCondition).label
    if (failed.length === 0) {
      toast.success(`${label} — ${teeth.length} dent${teeth.length > 1 ? "s" : ""} chartée${teeth.length > 1 ? "s" : ""}`)
      setNote("")
      setSurfaces(new Set())
      onClearSelection()
      return
    }
    if (failed.length === teeth.length) {
      toast.error("Échec de l'enregistrement du diagnostic.", {
        description: `Aucune des ${teeth.length} dents n'a été chartée. Les dents restent sélectionnées.`,
      })
    } else {
      toast.error(`${teeth.length - failed.length} dent(s) chartée(s), ${failed.length} en échec`, {
        description: `Non enregistré sur : ${failed.join(", ")}. Ces dents restent sélectionnées — appuyez à nouveau pour réessayer.`,
      })
    }
    onKeepOnlyFailed(failed)
  }

  return (
    <div className="space-y-3 rounded-lg border border-primary/40 bg-primary/5 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-medium text-foreground">
          {teeth.length === 0
            ? "Aucune dent sélectionnée"
            : `${teeth.length} dent${teeth.length > 1 ? "s" : ""} sélectionnée${teeth.length > 1 ? "s" : ""}`}
        </p>
        {teeth.length > 0 && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onClearSelection}
            disabled={saving}
            className="h-7 gap-1.5 px-2 text-xs coarse:h-11"
          >
            <X className="h-3.5 w-3.5" aria-hidden="true" /> Tout effacer
          </Button>
        )}
      </div>

      {teeth.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          Touchez les dents concernées sur l&apos;arcade ci-dessus. Le diagnostic que vous saisirez ici sera noté
          sur chacune d&apos;elles.
        </p>
      ) : (
        /* The numbers written out, not just counted: « 3 dents » does not let anyone check they ticked 16 and not
           15, and this form writes to the record. `break-words` because a full-quadrant selection is a long line
           at 320px. */
        <p className="break-words text-xs text-muted-foreground">Dents : {teeth.join(", ")}</p>
      )}

      <div className="space-y-2 border-t border-primary/20 pt-3">
        <p className="flex items-center gap-1.5 text-xs font-medium text-foreground">
          <Stethoscope className="h-3.5 w-3.5" aria-hidden="true" /> Diagnostic commun
        </p>
        <Select value={effectiveCondition} onValueChange={onConditionChange}>
          <SelectTrigger className="h-9 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {DIAGNOSIS_CONDITIONS.map((c) => (
              <SelectItem key={c} value={c} className="text-xs">
                {conditionStyle(c).label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {/* Same `gap-2` + `coarse:h-11` as the single-tooth form: five 28px buttons at `gap-1` overlap their own
            44px touch overlays and the later sibling wins the tap. */}
        <div className="flex flex-wrap gap-2">
          {Object.entries(SURFACE_LABELS).map(([code, label]) => (
            <Button
              key={code}
              type="button"
              variant={surfaces.has(code) ? "default" : "outline"}
              size="sm"
              className="h-8 px-2 text-xs coarse:h-11 coarse:min-w-11"
              title={label}
              aria-pressed={surfaces.has(code)}
              onClick={() => toggleSurface(code)}
            >
              {code}
            </Button>
          ))}
        </div>
        <Textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Note (facultative) — appliquée à toutes les dents sélectionnées"
          className="min-h-[52px] text-xs"
        />
        <Button
          size="sm"
          className="h-9 w-full gap-1.5 text-xs coarse:h-11"
          onClick={handleSave}
          disabled={saving || teeth.length === 0}
        >
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          {saving
            ? "Enregistrement…"
            : teeth.length === 0
              ? "Ajouter le diagnostic"
              : `Ajouter le diagnostic à ${teeth.length} dent${teeth.length > 1 ? "s" : ""}`}
        </Button>
      </div>
    </div>
  )
}
