"use client"

import { useState, useEffect, useCallback, useMemo, useRef, type ReactNode } from "react"
import { toast } from "sonner"
import { Plus, Trash2, Stethoscope, ClipboardList, CheckSquare, Check, ChevronRight, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { AppLoader } from "@/components/ui/app-loader"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
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
import { patientsApi } from "@/lib/api/patients"
import { showErrorToast } from "@/lib/errors"
import {
  DENTITION_VIEWS,
  DENTITION_VIEW_LABELS_FR,
  dentitionForView,
  dentitionViewFor,
  dentitionViewForTeeth,
  type DentitionView,
} from "@/lib/dentition"
import type { ToothStateDto, ProcedureTypeDto, DentalRecordDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDateFr } from "@/lib/format"
import { seedCost, type OdontogramPlanSeed, type SeedCandidate } from "@/components/odontogram-plan-seed"
import {
  CONDITION_ORDER,
  isBridgeUnit,
  conditionStyle,
  SURFACE_LABELS,
  SURFACE_ORDER,
  serializeSurfaces,
} from "@/components/odontogram-conditions"
import { OdontogramActsChart } from "@/components/odontogram-acts-chart"
import {
  buildRecordedActs,
  NO_RECORDED_ACTS,
  RECORDED_ACT_BOX,
  RECORDED_ACT_COLOR,
  RECORDED_ACT_LABEL,
  RECORDED_ACT_LEGEND,
  RECORDED_ACT_SWATCH,
  type RecordedAct,
} from "@/components/odontogram-recorded-acts"
// One source for the FDI quadrant layout — `tooth-multiselect` is the client-side authority for a tooth's
// dentition (mirroring the backend `FdiTooth.IsAdult`), and this file used to carry a second copy.
import { TEETH_BY_VIEW, isAdultTooth } from "@/components/tooth-multiselect"
import { DentitionViewSwitch } from "@/components/dentition-view-switch"
import { OdontogramViewSwitch, type OdontogramChartView } from "@/components/odontogram-view-switch"
import {
  ToothSymbolGlyph,
  OcclusalSurfaceBox,
  OcclusalSurfacePicker,
  ToothSymbolLegend,
  type BridgeSpan,
  type ToothMark,
} from "@/components/tooth-symbols"
import { isUpperTooth } from "@/components/tooth-anatomy"
import { useToothDragSelect, TOOTH_CELL_ATTR } from "@/components/tooth-drag-select"
import { buildBridgeRuns } from "@/components/bridge-runs"
import { ToothArchLayout, type ToothArch } from "@/components/tooth-arch-layout"
import {
  toothTreatmentSummary,
  type ToothTreatment,
} from "@/components/treatment-plans/teeth-under-treatment"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { quoteFr } from "@/lib/format"

// Max dots drawn under a tooth before collapsing the overflow into a "+N".
const MAX_DOTS = 4

/** Per-browser reading preference, like the files drawer's grid/list. Never a server-side setting. */
const CHART_VIEW_STORAGE_KEY = "odontogram.chartView"

// Conditions offerable as a diagnosis (everything except the implicit-healthy "Sain").
const DIAGNOSIS_CONDITIONS = CONDITION_ORDER.filter((c) => c !== "Sain")

const isDiagnosis = (entry: ToothStateDto) => entry.source === "Diagnosis"

interface OdontogramProps {
  patientId: string
  /**
   * The patient's stored dentition (`"Child"` | `"Adult"`) — which arch the chart **opens** on.
   *
   * A local Adulte/Enfant toggle defaulting to Adulte came first: it asked on every visit a question that is
   * largely a property of the patient, and a child's chart opened on the wrong teeth until someone flipped it. So
   * it became a pure derivation from this field — which then made the *mixed* stage unchartable, because a mouth
   * with both sets had no arch that showed it. Both halves are kept now: this seeds the view, and the
   * `DentitionViewSwitch` (Temporaire / Mixte / **Définitive**) lets the dentist say otherwise. A charted tooth outside the
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
  /**
   * Has somebody already answered « quelle denture ? » for this patient — `PatientDto.dentitionAnswered`.
   *
   * <p>⚠️ <b>This is the half `dentition` cannot supply, and its absence is what made the prompt nag.</b> The
   * stored dentition is NOT NULL and defaults to `Adult`, so answering « Définitive » is indistinguishable from
   * never having been asked; the prompt therefore keyed on `dateOfBirth` alone and returned on every reload of
   * an undated patient's page — for ever, however many times it was answered.</p>
   *
   * <p>Optional, and a missing value reads as « not answered »: a caller that has not been updated keeps
   * exactly today's behaviour rather than silently suppressing the question.</p>
   */
  dentitionAnswered?: boolean
  /** Called with one seed per tooth carrying an open diagnosis, to pre-fill a new treatment plan. */
  onCreatePlan?: (seeds: OdontogramPlanSeed[]) => void
  /**
   * Which teeth have a multi-séance treatment under way — `teethUnderTreatment(plans)`.
   *
   * <p>⚠️ <b>The third reading this chart did not have.</b> A diagnosis says the work is needed and an act says
   * it was done; between them sits the state a couronne spends six weeks in, and the chart said nothing about
   * it — the tooth simply kept its « à traiter » while the fiches beside it recorded two séances. Passed in
   * rather than fetched because the patient page already holds the plans for the treatment band above.</p>
   *
   * <p>Optional: a caller with no plans in hand renders exactly as before, with no ring and no legend row.</p>
   */
  treatments?: Map<number, ToothTreatment[]>
}

export function Odontogram({
  patientId,
  dentition,
  dentitionAnswered,
  dateOfBirth,
  onCreatePlan,
  treatments,
}: OdontogramProps) {
  const [chosenView, setChosenView] = useState<DentitionView | null>(null)
  /**
   * Which **drawing** the chart uses — see {@link OdontogramViewSwitch} for why there are two.
   *
   * <p>Unlike `chosenView` above, this is not seeded from the patient: it is a reading preference, the same
   * nature as the files drawer's grid/list, and it is remembered per browser the same way. It defaults to
   * `boxes` so nobody's chart changes under them on deploy day.</p>
   */
  /**
   * Which of the two charts is on screen.
   *
   * ⚠️ **Controlled, and the reason changed.** It was controlled so the Cases/Symboles switch could be withheld
   * on « Actes réalisés », where that chart ignored `chartView` and the switch therefore lied. That chart draws
   * the teeth now, so the switch is offered on both — and the state is still held here because « Actes
   * réalisés » sends the reader to the other tab from its own footer.
   */
  const [tab, setTab] = useState("diagnostics")
  const [chartView, setChartView] = useState<OdontogramChartView>("boxes")
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
   * Read the chart preference AFTER mount, never during render: the server has no `localStorage`, and seeding
   * state from it would hydrate one drawing and paint the other. Wrapped, because `localStorage` throws
   * outright in a locked-down browser and in private mode on some engines — a remembered preference is not
   * worth a chart that fails to render.
   */
  useEffect(() => {
    try {
      const stored = window.localStorage.getItem(CHART_VIEW_STORAGE_KEY)
      if (stored === "boxes" || stored === "symbols") setChartView(stored)
    } catch {
      /* keep the default */
    }
  }, [])

  const chooseChartView = (next: OdontogramChartView) => {
    setChartView(next)
    try {
      window.localStorage.setItem(CHART_VIEW_STORAGE_KEY, next)
    } catch {
      /* the switch still works for this session */
    }
  }

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
  /**
   * Recorded work this chart's own vocabulary cannot hold — see `odontogram-recorded-acts.ts`.
   *
   * <p>Derived from the fiches this component already loads, so there is no second read and no new endpoint.
   * It is deliberately kept OUT of `byTooth`: that map drives the diagnosis picker's seeds, the bridge runs
   * and the condition legend, none of which an act without a state belongs in.</p>
   */
  const recordedActs = useMemo(() => buildRecordedActs(records, byTooth), [records, byTooth])

  /**
   * Every tooth this patient has **anything** recorded on — a charted state or an act that charted none.
   *
   * <p>⚠️ <b>The four questions below are all « what is there to show? », and answering them from `byTooth`
   * alone is what makes a tooth disappear in silence.</b> A coiffage on a deciduous 55 of a patient charted
   * « définitive » would not widen the arch, would not be counted by the « états hors de cette vue » notice,
   * and would not decide which arch a phone opens on — so the one mark on the tooth would simply never be
   * reachable, with no error and nothing on screen saying so.</p>
   */
  const teethWithAnything = useMemo(() => {
    const all = new Set<number>()
    for (const [tooth, entries] of byTooth) if (entries.length > 0) all.add(tooth)
    for (const tooth of recordedActs.keys()) all.add(tooth)
    return all
  }, [byTooth, recordedActs])

  const dentitionView = useMemo<DentitionView>(() => {
    if (chosenView) return chosenView
    const seeded = dentitionViewFor(dentition)
    const charted = dentitionViewForTeeth(Array.from(teethWithAnything), isAdultTooth)
    if (!charted || charted === seeded) return seeded
    return "mixed"
  }, [chosenView, dentition, teethWithAnything])

  const teeth = TEETH_BY_VIEW[dentitionView]

  /**
   * How many charted teeth this view does not show.
   *
   * ⚠️ **A chart that silently omits a recorded state is the one failure a clinical chart may not have.** The
   * default view widens to Mixte on its own when the charted teeth need it, so this is only ever reached by an
   * explicit switch — pressing « Définitive » on a patient with a charted deciduous 55 dropped it from the chart with
   * no notice at all, and the chart then read as « nothing recorded there ». It is a `role="status"` line rather
   * than a toast: the omission is true for as long as the view is, and a message that expires after four seconds
   * would leave the wrong chart on screen saying nothing.
   */
  const chartedOutOfView = useMemo(() => {
    // `teeth` is quadrant-shaped (`ToothQuadrants`), not a flat list — the chart draws four arches.
    const shown = new Set([...teeth.upperRight, ...teeth.upperLeft, ...teeth.lowerRight, ...teeth.lowerLeft])
    return Array.from(teethWithAnything).filter((tooth) => !shown.has(tooth)).length
  }, [teeth, teethWithAnything])

  /**
   * The conditions this patient actually carries, in the shared display order — the symbol legend's contents.
   *
   * <p>Ordered by `CONDITION_ORDER` rather than by encounter so the key reads « à soigner » before
   * « déjà traité », the same grouping the picker uses. Unknown values are dropped rather than listed: a
   * condition the client does not know cannot be drawn either, and the build check is what stops that pair from
   * ever being reached.</p>
   */
  /**
   * Dragging across the arch to tick a run of teeth — see {@link useToothDragSelect}.
   *
   * <p>⚠️ `paintTooth` <b>sets</b> rather than toggles, because the hook decides the direction once from the
   * tooth the gesture began on. A toggle here would undo any tooth the finger re-crossed, which on a curve is
   * most of them.</p>
   */
  const paintTooth = useCallback((tooth: number, select: boolean) => {
    setSelectedTeeth((prev) => {
      if (prev.has(tooth) === select) return prev
      const next = new Set(prev)
      if (select) next.add(tooth)
      else next.delete(tooth)
      return next
    })
  }, [])

  /*
   * ⚠️ **`enabled: true` — the drag is no longer gated on the mode, and entering the mode is what it does.**
   *
   * It shipped gated on « Plusieurs dents », and the sentence that taught it (« Glissez pour cocher une
   * série ») rendered only WHILE the mode was on — so the one thing telling you the gesture existed was behind
   * already knowing it existed. Reported as « it isn't noticeable ».
   *
   * ⚠️ Safe here only because the hook now arms on **reaching a second tooth** rather than on 4 px of
   * movement: on this chart a tap must still open the tooth's editor, and a hand tremor must not chart-select
   * instead. See `tooth-drag-select.ts`.
   */
  const dragSelect = useToothDragSelect({
    enabled: true,
    isSelected: (tooth) => selectedTeeth.has(tooth),
    onPaint: (tooth, select) => {
      // The first painted tooth ENTERS the mode, so the diagnosis bar and the tick marks appear with it.
      if (select) setMultiSelect(true)
      paintTooth(tooth, select)
    },
  })

  /**
   * Which teeth carry a bridge that continues into the cell beside them.
   *
   * <p>⚠️ **This used to be computed here, from arch adjacency, and it was wrong.** Two bridges placed side
   * by side merged into one bar, and one bridge's « still to place » status leaked along it onto the finished
   * crown beside it. `bridge-runs.ts` is the one owner now — it groups on the record's own
   * `bridgeGroupId` and keeps the old adjacency scan only as the fallback for ungrouped legacy rows. Its
   * doc-comment carries the measurements.</p>
   */
  const bridgeSpans = useMemo(() => buildBridgeRuns(teeth, byTooth), [teeth, byTooth])

  const chartedConditions = useMemo(() => {
    const present = new Set<string>()
    for (const list of byTooth.values()) for (const entry of list) present.add(entry.condition)
    return CONDITION_ORDER.filter((c) => present.has(c))
  }, [byTooth])

  /**
   * Nothing tells us which arch to open on: no date of birth, **nobody has ever answered**, nothing charted, and
   * no choice made this session. The chart asks rather than opening on the adult set (AC-18) — a six-year-old's
   * deciduous teeth are simply absent from that arch, so the wrong default is not a cosmetic default.
   *
   * <p>⚠️ <b>`!dentitionAnswered` is the clause that stops it nagging</b>, and it had to be a served field rather
   * than a test on `dentition`: that column is NOT NULL defaulting to `Adult`, so it cannot distinguish an answer
   * from a default. Without it the question returned on every reload of an undated patient's page however many
   * times it was answered — the whole reason `Patient.DentitionAnsweredAtUtc` exists.</p>
   */
  // ⚠️ `teethWithAnything`, not `byTooth`: a patient whose fiches name teeth has already told us which arch he
  // has — asking would be asking a question the record answers, and the prompt replaces the whole chart.
  const mustAskDentition =
    !dateOfBirth && !dentitionAnswered && chosenView === null && teethWithAnything.size === 0

  /**
   * Answering « Quelle denture afficher ? » — the one place a chosen view is also an answer ABOUT THE PATIENT,
   * so it is written to `Patient.Dentition` rather than kept for the session.
   *
   * <p>⚠️ **It used to be `setChosenView(view)` and nothing else, and the prompt said « vous pourrez en changer
   * à tout moment » over a choice that was discarded on reload.** So the question came back on every visit to
   * the page, for the same patient, for ever — reported as « je choisis, puis au rechargement il redemande ».
   * The prompt fires only when nothing can seed the chart (no date of birth, nothing charted), which makes it
   * the only moment this product ever learns the patient's dentition; throwing that away was the defect.</p>
   *
   * <p>⚠️ The **arch switch** above the chart deliberately still does not write: looking at the other arch for
   * a moment is not a clinical statement, and `DentitionView`'s own doc keeps it a view. What is stored is the
   * patient's dentition, and the patient form remains where it is corrected.</p>
   *
   * <p>⚠️ The view is set **before** the round trip and kept whatever the save does: the dentist asked for this
   * arch and must get it. A failed write costs the memory, not the chart, and says so.</p>
   */
  const answerDentition = async (view: DentitionView) => {
    setChosenView(view)
    try {
      await patientsApi.update(patientId, { dentition: dentitionForView(view) })
      toast.success(`Denture enregistrée — ${DENTITION_VIEW_LABELS_FR[view]}`)
    } catch (err) {
      showErrorToast(err, "La denture n'a pas pu être enregistrée pour ce patient.")
    }
  }

  /**
   * Diagnosis → the acts this clinic offers for it, best first.
   *
   * ⚠️ Built from each act's own `treats`, which the server computes from `ConditionTreatments`. It replaced an
   * inversion of `resultingCondition` — « the act that leaves the tooth in this state » — which is a different
   * question and answered nothing for a pathology, since no act ends in « Carie ». That inversion also kept the
   * FIRST act per state out of a list ordered by category then name, so « Extrait / Absent » resolved to the
   * surgical extraction (200 DT) over the simple one (60 DT) by alphabetical accident.
   */
  const candidatesByCondition = useMemo(() => {
    const map = new Map<string, SeedCandidate[]>()
    for (const pt of procedureTypes) {
      for (const t of pt.treats ?? []) {
        const list = map.get(t.condition) ?? []
        list.push({
          procedureTypeId: pt.id,
          name: pt.name,
          defaultCost: pt.defaultCost,
          perTooth: pt.resultingCondition != null,
          rank: t.rank,
        })
        map.set(t.condition, list)
      }
    }
    for (const [condition, list] of map) {
      // Rank first (the clinical order), then the cheaper act — a tie is two ways of doing the same thing.
      list.sort((a, b) => a.rank - b.rank || (a.defaultCost ?? 0) - (b.defaultCost ?? 0))
      map.set(condition, list)
    }
    return map
  }, [procedureTypes])

  /**
   * Open diagnoses, **one seed per diagnosis** rather than per tooth: two caries are one line carrying both
   * teeth. A tooth charted with two different diagnoses appears in both lines, which is correct — they are two
   * pieces of work.
   */
  const planSeeds = useMemo<OdontogramPlanSeed[]>(() => {
    const teethByCondition = new Map<string, number[]>()
    for (const [tooth, entries] of Array.from(byTooth.entries()).sort((a, b) => a[0] - b[0])) {
      for (const condition of new Set(entries.filter(isDiagnosis).map((d) => d.condition))) {
        teethByCondition.set(condition, [...(teethByCondition.get(condition) ?? []), tooth])
      }
    }

    // Charting order, so the list reads the way the mouth was examined rather than alphabetically.
    return DIAGNOSIS_CONDITIONS.filter((c) => teethByCondition.has(c)).map((condition) => {
      const teeth = teethByCondition.get(condition)!
      const candidates = candidatesByCondition.get(condition) ?? []
      // Pre-fill ONLY when the first choice is unambiguous. Several acts at rank 0 is the catalogue saying the
      // decision is clinical (simple vs surgical extraction), and filling one of them in silently is how a
      // devis leaves with the wrong number on it.
      const topRank = candidates.length > 0 ? candidates[0].rank : -1
      const atTop = candidates.filter((c) => c.rank === topRank)
      const sole = atTop.length === 1 ? atTop[0] : undefined

      return {
        toothNumbers: teeth,
        designationFr: sole?.name ?? "",
        diagnosisLabel: `${conditionStyle(condition).label} — ${
          teeth.length === 1 ? `dent ${teeth[0]}` : `dents ${teeth.join(", ")}`
        }`,
        diagnosisCondition: condition,
        plannedCost: sole ? seedCost(sole, teeth.length) : undefined,
        procedureTypeId: sole?.procedureTypeId,
        candidates,
      }
    })
  }, [byTooth, candidatesByCondition])

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
    // A `Set`'s iteration order is insertion order here too, so the min is taken rather than the first.
    for (const tooth of teethWithAnything) {
      if (lowest === undefined || tooth < lowest) lowest = tooth
    }
    if (lowest === undefined) return undefined
    const quadrant = Math.floor(lowest / 10)
    return quadrant === 1 || quadrant === 2 || quadrant === 5 || quadrant === 6 ? "upper" : "lower"
  }, [teethWithAnything])

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
        <AppLoader label="Chargement de l'odontogramme…" />
      ) : mustAskDentition ? (
        <div
          role="status"
          className="flex flex-col items-center gap-4 rounded-lg border border-dashed bg-muted/40 px-4 py-8 text-center"
        >
          <div>
            {/* « charter » was an anglicism (to chart), and « dentition » is not the word the rest of the product
                uses — the patient field, its captions and this switch all say « denture ». One sentence, in the
                clinic's own vocabulary. */}
            <p className="text-sm font-medium text-foreground">Quelle denture afficher ?</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {/* ⚠️ It says the answer is KEPT, because it is now — and because the old « vous pourrez en
                  changer à tout moment » implied that over a choice that was discarded on reload, so the
                  question came back every visit. Naming where it is stored is also what makes « modifiable »
                  actionable rather than a promise with no address. */}
              Ce patient n&apos;a pas de date de naissance enregistrée, la denture ne peut donc pas être déduite.
              Choisissez-la : elle sera enregistrée sur la fiche du patient, et reste modifiable à tout moment.
            </p>
          </div>
          <div className="flex flex-wrap justify-center gap-2">
            {DENTITION_VIEWS.map((view) => (
              <Button
                key={view}
                variant="outline"
                onClick={() => void answerDentition(view)}
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
        <Tabs value={tab} onValueChange={setTab} className="w-full">
          {/* The view switch and the create-plan action share one row.
              They used to be two stacked rows — the button right-aligned on its own line, the tabs left-aligned on
              the next — which spent two rows of chrome directly above the chart the page exists to show. They pair
              naturally: both act on the whole odontogram, and putting them at opposite ends of one row reads as
              « which view » on the left and « what to do with it » on the right. */}
          {/*
            ⚠️ **Which view, and which drawing — and nothing else.** This row used to carry « Créer un plan » too,
            and at 390 px the four controls wrapped into FOUR stacked rows (measured: 127 px for the switches plus
            28 px for the button). The chart under them is 177 px, so the card was spending 516 px of chrome to
            show 177 px of teeth — a 3:1 ratio on the one card the patient page exists for, with the first tooth
            at **y = 1214**, i.e. 1.44 screens down. The action moved BELOW the chart, onto the legend row: it
            acts on what is charted, so it is read after the teeth, not before them.

            The dentition switch stays here rather than inside a tab body: it applies to **both** charts, and a
            per-tab copy could have the Diagnostics arch disagreeing with the Actes one.
          */}
          <div className="flex flex-wrap items-center justify-between gap-2">
            {/*
              ⚠️ **« État dentaire », not « Diagnostics » — the old name was the defect.** This chart has always
              carried BOTH sources: a `ToothStateDto` is `Diagnosis` *or* `Treatment`, and in « Symboles » the
              colour IS that axis (rouge à faire · bleu réalisé). Named « Diagnostics » it read as one half of a
              pair whose other half is the tab beside it, so a dentist looking for his work in symbols concluded
              there were no symbols for les actes réalisés — reported in exactly those words. The two tabs are
              two QUESTIONS over one mouth: « dans quel état est cette dent ? » and « qu'a-t-on fait, et avec
              quel acte ? ».
            */}
            <TabsList>
              <TabsTrigger value="diagnostics">État dentaire</TabsTrigger>
              <TabsTrigger value="acts">Actes réalisés</TabsTrigger>
            </TabsList>
            {/*
              ⚠️ `w-full sm:w-auto` + `flex-wrap` + `flex-1` on each switch, rather than trusting them to fit
              side by side. Their intrinsic widths came to 294 px against a 294 px row at 390 px — a tie, which
              flexbox resolves by wrapping, so the pair took a second 38 px row directly above the teeth.
              Trimming padding closed the gap *exactly*, which is the kind of fit that survives until somebody
              renames « Symboles ». Letting the group own the row and the two switches share it is the same
              result with no measurement in it.

              ⚠️ **`min-w-0` on the switches is what must NOT be here, and putting it there was a real defect.**
              It looks like the usual « let a flex child shrink » incantation, and on a control made of text
              segments it removes the `min-width: auto` floor that is the only thing stopping them being
              squeezed below their own words — while the segments *inside* each switch keep their floor. So the
              switch box shrank and its segments overflowed to the right: measured at 320 px, « Mixte » was
              painted from x=267 to x=310 inside a 108 px box ending at 264 and a card ending at 289 — outside
              the card, over the page ground, and `<main>` grew a horizontal scrollbar, which § 11 forbids
              outright. With the floor left alone, the pair simply wraps when the two words no longer fit, which
              is the honest answer at 320 px.
            */}
            <div className="flex w-full flex-wrap items-center gap-1.5 sm:w-auto sm:gap-2">
              {/*
                ⚠️ **Offered on BOTH tabs now, and the asymmetry that used to be here is gone.**

                It was conditional on « Diagnostics », for a reason that was correct at the time and is
                recorded because it will look like a regression: `OdontogramActsChart` drew its own thing and
                did not read `chartView`, so on « Actes réalisés » the switch accepted the press, moved its own
                pressed state, and left the chart byte-for-byte identical — a control that appears to work and
                does not.

                What that produced instead was worse than the control it prevented: a dentist found « Symboles »
                on one tab, no switch on the other, and concluded there were no symbols for les actes réalisés.
                The fix was to make the control true rather than to keep hiding it — that chart draws the teeth
                now, tinted by act. **Do not re-add the gate without first removing the drawing.**

                ⚠️ The two switches mean different things per tab and that is deliberate: « Symboles » is « draw
                the teeth » on both, while the colour stays each tab's own question — à faire / réalisé on one,
                which act on the other. The acts chart says so under itself.
              */}
              <OdontogramViewSwitch
                value={chartView}
                onChange={chooseChartView}
                className="flex-1 sm:flex-none"
              />
              <DentitionViewSwitch
                value={dentitionView}
                onChange={setChosenView}
                className="flex-1 sm:flex-none"
              />
            </div>
          </div>

          {/* Its own line, and only when there is something to say — inside the row above it was a fifth control
              competing for a width that already had none. */}
          {chartedOutOfView > 0 && (
            <button
              type="button"
              role="status"
              onClick={() => setChosenView("mixed")}
              className="mt-2 rounded-md border border-warning/40 bg-warning-wash px-2 py-1 text-2xs font-medium text-warning-ink underline-offset-2 hover-hover:hover:underline coarse:py-2"
            >
              {chartedOutOfView === 1
                ? "1 état hors de cette vue — tout afficher"
                : `${chartedOutOfView} états hors de cette vue — tout afficher`}
            </button>
          )}
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
            {/* ⚠️ The toggle stays a **permanent, labelled control directly above the teeth**, not an option
                behind a menu or a modifier key. The whole point of the feature is that a dentist who has just
                charted the same carie on three molars one at a time discovers there was a faster way without
                being told — a ctrl-click or a long-press would have been cheaper to build and invisible to
                everyone who did not already know it was there. It sits inside the Diagnostics tab because
                « Actes réalisés » is read-only and has nothing to select teeth for.

                ⚠️ **What did go is the paragraph beside it**, which was two lines at 390 px and on screen
                permanently. Its « off » half (« Même diagnostic sur plusieurs dents ? Activez … ») restated the
                button's own label back at the reader, and its « on » half ended « … sous l'arcade », which is
                now false: the diagnostic is entered in a bar docked to the bottom of the screen. What survives
                is the one thing the label cannot say — that you may drag — shown only while the mode is on,
                which is the only time dragging does anything. */}
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              {/*
                ⚠️ **It is a READOUT and still a real toggle, and both halves are load-bearing.**

                The gate is what went: dragging now enters the mode on its own, so this no longer stands
                between a dentist and the faster way of charting. What it must keep doing is two things a
                pointer gesture cannot:

                · **Discoverability.** The comment that stood here argued that a permanent, labelled control is
                  the only thing that tells somebody who has just charted the same carie on three molars one at
                  a time that there was a faster way. That argument is still right — it is the *gate* the owner
                  overrode, not the affordance — so the button stays, and the « glissez » hint beside it is now
                  PERMANENT rather than shown only once the mode is on.
                · **Keyboard and AT.** A drag is pointer-only. With the button gone there is no route into
                  multi-select without a pointer at all, since Space on a tooth opens its editor.
              */}
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
                {selectedTeeth.size > 0
                  ? `${selectedTeeth.size} dent${selectedTeeth.size > 1 ? "s" : ""} sélectionnée${selectedTeeth.size > 1 ? "s" : ""}`
                  : "Plusieurs dents"}
              </Button>
              {selectedTeeth.size > 0 ? (
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  onClick={() => setMultiSelectMode(false)}
                  className="h-8 text-xs coarse:h-11"
                >
                  Vider
                </Button>
              ) : (
                <p className="text-xs text-muted-foreground">
                  Glissez sur plusieurs dents pour les sélectionner
                </p>
              )}
            </div>

        {/* Geometry from `ToothArchLayout`. `ToothCell` keeps its own editor Popover and its per-cell state —
            the layout takes no open/hover state, which is what stops one arch's worth of editors from being
            addressable at once. */}
        {/* The gesture is owned HERE and not by `ToothArchLayout`, whose contract is that it takes no
            per-tooth state — a wrapper works because pointer events bubble, and `select-none` is
            unconditional for the reason the agenda documents: a browser anchors a text selection on
            pointerdown, before any movement has said this is a drag. */}
        {/* The gesture reaches the arch through `ToothArchLayout`'s own `dragSelect` prop now, not through a
            wrapper here — that is what gave the fiche de soins the same drag from one edit, and it is why the
            layout's « no per-tooth state » contract still holds: what it receives is an opaque handle. */}
        <ToothArchLayout
          teeth={teeth}
          defaultArch={defaultArch}
          dragSelect={dragSelect}
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
              treatments={treatments?.get(t)}
              recordedActs={recordedActs.get(t) ?? NO_RECORDED_ACTS}
              chartView={chartView}
              bridgeSpan={bridgeSpans.get(t)}
              didConsumeGesture={dragSelect.didConsumeGesture}
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
              {/* One legend per drawing, because the two spend colour on different things: fifteen condition
                  hues under a chart whose colour means « à faire / réalisé » would teach the wrong key. */}
              {chartView === "symbols" ? (
                <ToothSymbolLegend conditions={chartedConditions} hasRecordedActs={recordedActs.size > 0} />
              ) : (
                <>
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
                  {/* Only when the patient carries one — it is not part of the condition vocabulary above, it
                      is a fact about THIS patient's record, so the « Traitement en cours » rule applies. */}
                  {recordedActs.size > 0 && (
                    <div className="flex items-center gap-1.5" title={RECORDED_ACT_LEGEND}>
                      <span className={cn("h-4 w-4 rounded border", RECORDED_ACT_SWATCH)} />
                      <span className="text-muted-foreground">{RECORDED_ACT_LABEL}</span>
                    </div>
                  )}
                </>
              )}
              {/* Only when the patient actually has one — a legend row for a state nothing on the chart is
                  wearing teaches a mark the reader will never meet. */}
              {treatments && treatments.size > 0 && (
                <div className="flex items-center gap-1.5">
                  <span className="mx-0.5 h-3.5 w-3.5 rounded-sm border border-border outline-2 outline-dashed outline-primary outline-offset-2" />
                  <span className="text-muted-foreground">Traitement en cours</span>
                </div>
              )}
            </div>

            {/*
              ⚠️ **Below the chart, not above it** — this is the one control that acts on what the odontogramme
              already says, so it is read after the teeth. Above, it was a fourth wrapped row of chrome between
              the card's title and the first tooth (§ the row-1 note), and it invited a press before there was
              anything on screen to press it about: with nothing charted it is disabled, which at the top of the
              card reads as a broken control and at the bottom reads as « rien à planifier », which is the truth.

              ⚠️ The label shortens below `sm:` and the `aria-label` carries the full phrase at every width.
              « Créer un plan depuis l'odontogramme » measures 253 px against the 223 px this row has at 320 px,
              and `Button` is `whitespace-nowrap shrink-0` — so the wording, not the layout, was what pushed a
              control out through the card's edge. Shortening the *visible* half loses nothing here: the button
              sits directly under the odontogramme it acts on, so « depuis l'odontogramme » is the one part of
              the sentence the context already supplies.
            */}
            {onCreatePlan && (
              <div className="flex justify-end pt-1">
                <Button
                  size="sm"
                  variant="outline"
                  className="h-8 max-w-full gap-1.5 text-xs coarse:h-11"
                  disabled={planSeeds.length === 0}
                  onClick={() => onCreatePlan(planSeeds)}
                  aria-label="Créer un plan depuis l'odontogramme"
                  title={planSeeds.length === 0 ? "Aucun diagnostic à planifier" : undefined}
                >
                  <ClipboardList className="h-3.5 w-3.5" aria-hidden="true" />
                  Créer un plan
                  <span className="hidden sm:inline">&nbsp;depuis l&apos;odontogramme</span>
                </Button>
              </div>
            )}
          </TabsContent>

          <TabsContent value="acts" className="mt-3">
            {/* ⚠️ `onShowSymbols` is where the Cases/Symboles confusion is actually answered. The switch is
                withheld on this tab because it changes nothing here — but a control that is simply absent
                teaches nothing, and what a reader concludes is that les actes réalisés have no symbol view.
                They do: they are on the other tab, in blue. This says so and takes them there. */}
            <OdontogramActsChart
              teeth={teeth}
              records={records}
              procedureTypes={procedureTypes}
              chartView={chartView}
              // The states, so a tooth can show what its act LEFT — and so a withheld one stays absent.
              toothStates={byTooth}
              onShowSymbols={() => setTab("diagnostics")}
            />
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
  /** Treatments under way on this tooth, if any — see {@link OdontogramProps.treatments}. */
  treatments?: ToothTreatment[]
  /**
   * Work recorded on this tooth that charted no state — see `odontogram-recorded-acts.ts`.
   *
   * ⚠️ Always an array, never optional: a caller that forgets it would silently drop the mark from one
   * drawing, which is the failure the whole derivation exists to prevent.
   */
  recordedActs: RecordedAct[]
  /** Which drawing to use. Everything else about the cell — editor, selection, tooltip — is identical. */
  chartView: OdontogramChartView
  /** Set when this tooth's bridge continues into a neighbouring cell — see `bridgeSpans` above. */
  bridgeSpan?: BridgeSpan
  /** True while the click closing a drag-select is still to come — that click must not toggle this tooth again. */
  didConsumeGesture: () => boolean
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
  treatments,
  recordedActs,
  chartView,
  bridgeSpan,
  didConsumeGesture,
}: ToothCellProps) {
  const [open, setOpen] = useState(false)
  /*
   * Confirm-before-discard, on every channel Radix funnels through `onOpenChange` — the ✕, Escape, and a tap
   * on the overlay, which on a phone is most of the screen. The panel carries a free-text note, and it became
   * a `Dialog` in this pass: `frontend-web.md` § 5 requires the guard of any dialog holding entered data, and
   * the popover it replaced silently discarded a typed note on an outside click.
   */
  const guard = useDirtyGuard(open, (next) => {
    setOpen(next)
    if (!next) setCondition(null)
  })
  /**
   * This editor's own chosen condition — `null` until the dentist picks one, so that merely *opening* a tooth
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
  const underTreatment = (treatments?.length ?? 0) > 0

  /**
   * The condition the box is painted with: a **pending** choice wins over the stored state.
   *
   * <p>Both halves of the chart write through this. `previewCondition` is « Plusieurs dents »' shared choice,
   * `condition` is this popover's own — and the popover's only counts while it is open, so an abandoned form
   * leaves nothing painted behind it.</p>
   */
  const preview = previewCondition ?? (open ? condition : null)
  const style = conditionStyle(preview ?? latest?.condition ?? "Sain")
  /**
   * The « Cases » fill for a tooth whose only record is an act that charted nothing.
   *
   * <p>⚠️ A charted condition always wins the fill — it is the more specific clinical statement, and a box
   * holds exactly one. So this is the *absence* of any condition, not a rank among them; a tooth carrying both
   * shows the condition here and the act on its dot row and in its tooltip.</p>
   */
  const boxIsRecordedActOnly = !preview && !latest && recordedActs.length > 0
  /** Conditions and recorded acts share one dot row, so the cap and the « +N » count the same thing. */
  const dotCount = entries.length + recordedActs.length
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
      // Before the close, or the guard asks the dentist to confirm discarding the note it has just saved.
      guard.markClean()
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

  /*
   * What the symbol drawing paints, and the one place the two views deliberately differ.
   *
   * ⚠️ A pending choice is **added** here, where the box view **replaces**. That is not an inconsistency: the
   * box can hold one fill and has no other option, while a symbol composes — so the dentist about to chart a
   * carie on an already-couronnée tooth sees both, which is the truthful picture and the whole reason this
   * view exists. Both halves read `preview` above, so an abandoned form leaves nothing behind either way.
   */
  const symbolMarks: ToothMark[] = [
    ...entries.map((e) => ({ condition: e.condition, source: e.source, surfaces: e.surfaces })),
    ...(preview ? [{ condition: preview, source: "Diagnosis" }] : []),
  ]

  /* Shared by both drawings: the ring, the treatment outline and the tick are chrome, not paint. */
  const cellChrome = cn(
    underTreatment && "outline-2 outline-dashed outline-primary outline-offset-2",
    selectionMode && isSelected && "ring-2 ring-primary ring-offset-1 ring-offset-background",
  )
  const tick = selectionMode && isSelected && (
    <span
      aria-hidden="true"
      className="absolute -right-1 -top-1 flex size-3.5 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-sm"
    >
      <Check className="size-2.5" strokeWidth={3} />
    </span>
  )

  const symbolBox = (
    <span className="flex flex-col items-center">
      {/* The occlusal cell sits against the occlusal plane — under an upper tooth, over a lower one — so the
          two arches face each other the way the mouth does. */}
      <span className={cn("relative flex flex-col items-center gap-px rounded-md p-0.5", cellChrome)}>
        {!isUpperTooth(toothNum) && <OcclusalSurfaceBox toothNumber={toothNum} marks={symbolMarks} />}
        {/* `hasRecordedAct` is the second vocabulary this chart draws — see `odontogram-recorded-acts.ts`.
            The boxes drawing below takes the same list; the two must never disagree about one fact. */}
        <ToothSymbolGlyph
          toothNumber={toothNum}
          marks={symbolMarks}
          bridgeSpan={bridgeSpan}
          hasRecordedAct={recordedActs.length > 0}
        />
        {isUpperTooth(toothNum) && <OcclusalSurfaceBox toothNumber={toothNum} marks={symbolMarks} />}
        {tick}
      </span>
      <span
        className={cn(
          "mt-0.5 text-2xs font-medium",
          selectionMode && isSelected ? "font-semibold text-primary" : "text-muted-foreground",
        )}
      >
        {toothNum}
      </span>
    </span>
  )

  const boxesBox = (
    <span className="flex flex-col items-center">
      <span
        /* The selection ring is painted on the tooth BOX, not on the wrapping button: on a coarse pointer the
           button grows to `min-w-11` while the box stays 28px, so a ring on the button would float a centimetre
           away from the tooth it is meant to be marking. `ring-offset` keeps it clear of the condition fill,
           which is already a saturated colour on a charted tooth. */
        className={cn(
          "relative flex h-9 w-7 items-center justify-center rounded-md border text-2xs font-semibold",
          boxIsRecordedActOnly ? RECORDED_ACT_BOX : style.box,
          latestIsDiagnosis && "border-2 border-dashed",
          /*
            ⚠️ **A RING, never a fill — the fill belongs to the condition and this is a different axis.**
            « Un traitement est en cours ici » is orthogonal to « what state is this tooth in »: a couronne
            two séances in is still charted « à traiter » (correctly — it is not finished), so painting the
            box would overwrite the clinical fact with a workflow one. It is the same dashed-primary language
            the séance strips already use for « prochaine étape », so the two read as one idea.
            ⚠️ Drawn BEFORE the selection ring in the class list and overridden by it: « Plusieurs dents » is a
            mode the dentist is actively in, and losing the tick mark to a treatment ring would be a control
            hidden by a decoration.
          */
          underTreatment && "outline-2 outline-dashed outline-primary outline-offset-2",
          selectionMode && isSelected && "ring-2 ring-primary ring-offset-1 ring-offset-background",
        )}
      >
        {latest?.surfaces ?? ""}
        {tick}
      </span>
      <span
        className={cn(
          "mt-0.5 text-2xs font-medium",
          selectionMode && isSelected ? "font-semibold text-primary" : "text-muted-foreground",
        )}
      >
        {toothNum}
      </span>
      {dotCount > 0 && (
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
          {/* The recorded acts take whatever room the conditions left, in the same row and under the same cap —
              a second dot row would double the cell's height for the tooth that has least to say. */}
          {recordedActs.slice(0, Math.max(0, MAX_DOTS - entries.length)).map((a, i) => (
            <span
              key={`${a.recordId}-${a.name}-${i}`}
              className="h-1.5 w-1.5 rounded-full"
              style={{ backgroundColor: RECORDED_ACT_COLOR }}
            />
          ))}
          {dotCount > MAX_DOTS && (
            <span className="text-2xs font-medium text-muted-foreground">+{dotCount - MAX_DOTS}</span>
          )}
        </span>
      )}
    </span>
  )

  /*
   * ⚠️ The two drawings swap HERE and nowhere else. Everything below — the tooltip, the checkbox branch, the
   * editor Popover, the confirm dialog — is shared verbatim, which is what stops the symbol view from becoming
   * a second chart with its own behaviour to keep in step. The dots are deliberately absent from the symbol
   * drawing: they exist because a single fill can only show the latest state, and a symbol shows them all.
   */
  const box = chartView === "symbols" ? symbolBox : boxesBox

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
    // ⚠️ `underTreatment` widens the gate: a tooth whose treatment has not produced a fiche yet has NO charted
    // entry at all, so the old `entries.length === 0` test withheld the tooltip from exactly the teeth the
    // ring was drawn on — a mark on screen with no way to ask what it meant.
    //
    // ⚠️ `bridgeSpan?.runLabel` widens it once more, and for the same reason: a tooth the bar merely CROSSES
    // (an un-charted pontic site) has no entry and no treatment either, so it would be the one cell in the run
    // that could not say which bridge it belongs to.
    //
    // ⚠️ `recordedActs` widens it a third time, and it is the one that matters most here: such a tooth may
    // carry NO entry at all, so without this clause the whole point of the mark — « which act was it? » —
    // would be on screen with no way to ask.
    entries.length === 0 && !underTreatment && !bridgeSpan?.runLabel && recordedActs.length === 0 ? (
      node
    ) : (
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>{node}</TooltipTrigger>
          <TooltipContent side="top" align="center" className="max-w-xs">
            <p className="mb-1 font-semibold">Dent {toothNum}</p>
            {/*
              ⚠️ **Which bridge, stated in words.** « Where does this bridge begin and where does it end? » was
              the dentist's question, and the caps on the travée answer it graphically — but only for someone
              who already reads the drawing. This is the same fact in text, so it survives greyscale, 200 % zoom
              and a screen reader.

              ⚠️ It sits ABOVE the entries because a crossed pontic site has no entries at all: it would
              otherwise be a tooltip whose only content was an empty list.
            */}
            {bridgeSpan?.runLabel && (
              <p className="mb-1 text-muted-foreground">{bridgeSpan.runLabel}</p>
            )}
            {/* The treatment leads: it is the live fact, and the charted entries below it are its history. */}
            {treatments?.map((t) => (
              <p key={`${t.planId}-${t.designationFr}`} className="mb-1 text-primary">
                <span className="font-medium">{t.designationFr}</span>
                <br />
                {toothTreatmentSummary(t)}
              </p>
            ))}
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
              {/* ⚠️ The act's OWN name, which no charted entry carries: a tooth state holds the condition it
                  produced, and these produced none. This is the only place the coiffage, l'inlay-core or the
                  couronne provisoire is named on the chart. */}
              {recordedActs.map((a, i) => (
                <li key={`${a.recordId}-${a.name}-${i}`} className="flex items-center gap-1.5">
                  <span
                    className="h-2 w-2 shrink-0 rounded-full"
                    style={{ backgroundColor: RECORDED_ACT_COLOR }}
                  />
                  <span>{a.name}</span>
                  <span className="text-muted-foreground">— Réalisé · {formatDateFr(a.date)}</span>
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
        // Both branches carry it — see the note on the editor trigger for why gating it was a deadlock.
        {...{ [TOOTH_CELL_ATTR]: toothNum }}
        /*
          ⚠️ `pointerup` fires before `click`, so the last tooth of every drag would be painted by the gesture
          and then toggled straight back by this handler. The guard consumes exactly one click.
        */
        onClick={() => {
          if (didConsumeGesture()) return
          onToggleSelect(toothNum)
        }}
        // Same `coarse:min-w-11` reasoning as the editor trigger below: grow the paint, never overlay a 44px
        // target onto a 28px cell that a neighbour then wins.
        className="group rounded-md transition-all focus:outline-none focus:ring-1 focus:ring-ring coarse:min-w-11 hover-hover:hover:scale-105"
      >
        {box}
      </button>,
    )
  }

  const trigger = (
    <DialogTrigger asChild>
      <button
        type="button"
        aria-label={
          entries.length === 0
            ? `Dent ${toothNum} — aucun état enregistré`
            : `Dent ${toothNum} — ${entries.length} état${entries.length > 1 ? "s" : ""} enregistré${entries.length > 1 ? "s" : ""}`
        }
        /*
         * ⚠️ **UNCONDITIONAL, and this branch is the one that matters.** The attribute is how a drag asks
         * the document which tooth it is over, and it used to be emitted only inside the `selectionMode`
         * branch below — i.e. only once « Plusieurs dents » was already on. Once the drag stopped being gated
         * on that mode, that left a deadlock: with the mode off there was nothing to hit-test, so the gesture
         * could never paint a tooth, and painting a tooth is what enters the mode. The drag therefore did
         * NOTHING until the button was pressed — exactly the behaviour the mode was removed to fix, now under
         * a permanent hint promising otherwise. Measured in the browser: 32 tooth buttons, zero `data-tooth`.
         * Neither `tsc` nor `check:responsive` can see it; only looking can.
         */
        {...{ [TOOTH_CELL_ATTR]: toothNum }}
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
    </DialogTrigger>
  )

  /* Closing drops the pending choice: a form the dentist walked away from must not leave the tooth painted with
     a diagnosis that was never saved. */
  /*
    ⚠️ **A Dialog, not a Popover, and at every width — one element, two presentations.**

    This panel is a *form*: a condition, five faces, a note and a save. It was an anchored popover, and the
    anchor was costing more than it was worth on both sides of the breakpoint.

    On a phone it was measurably broken. Measured at 390x844 on tooth 18: Radix had 326 px below the tooth and
    398 px above, flipped to `side="top"`, and the panel — capped by a hand-written `max-h-[70dvh]` = 590.8 px
    rather than by the 394 px Radix had actually measured — rendered at **`y = -35`**. Its « Dent 18 » heading
    was off the top of the screen with *nothing to scroll*, because the content fitted the cap it had been
    given. Scrolling the page to reach the heading moved the tooth, which repositioned the popover, which moved
    the heading again: the « I have to chase it » report, reproduced exactly. `ui/popover.tsx` now caps on
    Radix's own measurement so the other 76 call sites cannot repeat it — but the right primitive for a form
    this size on a 390 px screen is not a better-behaved popover.

    On a desktop the anchor was near-worthless information anyway: 32 near-identical cells a few pixels apart,
    and the panel's own heading names the tooth. What the anchor did cost was the whole collision problem.

    ⚠️ `mobile="sheet"` is **one element re-styled, not two components swapped** — `ui/dialog.tsx`'s own
    docstring is explicit that a `useMediaQuery() ? <Sheet> : <Dialog>` swap looks identical either side of the
    breakpoint and silently unmounts a half-typed form on the way across. That warning applies to a
    `Popover ↔ Dialog` swap in exactly the same way, which is why there is no media query here: below `md:` this
    is a full-screen sheet, above it a centred panel, and rotating a tablet mid-note changes neither the mount
    nor the note.

    The `DialogBody` + `DialogFooter` split is what the popover could never offer: « Ajouter le diagnostic » is
    outside the scroll container, so the control this panel exists for cannot leave the screen — not behind a
    long list of charted states, and not when the on-screen keyboard opens over the note.
  */
  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        {withTooltip(trigger)}
        <DialogContent mobile="sheet" className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Dent {toothNum}</DialogTitle>
            <DialogDescription>
              {entries.length === 0
                ? "Aucun état enregistré"
                : `${entries.length} état${entries.length > 1 ? "s" : ""} enregistré${entries.length > 1 ? "s" : ""}`}
            </DialogDescription>
          </DialogHeader>

          <DialogBody className="space-y-3">
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
                        className="mt-1.5 h-7 gap-1.5 px-2 text-xs text-destructive hover:bg-destructive/10 hover:text-destructive coarse:h-11"
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

            {/* Add-diagnosis form. Its submit lives in the footer below, outside this scroller. */}
            <div className={cn("space-y-2", entries.length > 0 && "border-t pt-3")}>
              <p className="flex items-center gap-1.5 text-xs font-medium text-foreground">
                <Stethoscope className="h-3.5 w-3.5" /> Noter un diagnostic
              </p>
              <Select value={condition ?? DIAGNOSIS_CONDITIONS[0]} onValueChange={setCondition}>
                <SelectTrigger className="h-9 text-xs coarse:h-11">
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
              {/*
                Surfaces (MODVL) — **pointed at, not spelled out**.
                The five letter buttons this replaced put the French name in a native `title` only, so on the one
                control whose whole subject is *where on the tooth* the dentist read « M » and did the geometry in
                their head — and on a phone the `title` needed a hover there is no pointer for. The picker is the
                chart's own `OCCLUSAL_ZONES`, so the box here and the box under the tooth cannot disagree about
                which side is mésial.
                ⚠️ It is offered here and NOT in the multi-tooth bar: there the faces apply to several teeth
                at once, and mésial is on opposite sides of the two halves of the mouth.
              */}
              <div className="flex flex-col items-center gap-1.5">
                <OcclusalSurfacePicker
                  toothNumber={toothNum}
                  selected={surfaces}
                  onToggle={toggleSurface}
                  disabled={saving}
                />
                <p className="text-2xs text-muted-foreground" role="status">
                  {surfaces.size === 0
                    ? "Faces (facultatif) — touchez la zone atteinte"
                    : `Faces : ${SURFACE_ORDER.filter((s) => surfaces.has(s)).map((s) => SURFACE_LABELS[s]).join(", ")}`}
                </p>
              </div>
              <Textarea
                value={note}
                onChange={(e) => setNote(e.target.value)}
                placeholder="Note (facultative)"
                className="min-h-[52px] text-xs"
              />
            </div>
          </DialogBody>

          <DialogFooter>
            <Button onClick={handleDiagnose} disabled={saving} className="gap-1.5 coarse:h-11">
              <Plus className="h-4 w-4" aria-hidden="true" />
              {saving ? "Enregistrement…" : "Ajouter le diagnostic"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DiscardChangesDialog guard={guard} />

      {/* A sibling of the editor rather than a child, so dismissing the editor cannot unmount the confirmation
          mid-flight. Naming the tooth and the condition matters here: the whole point is correcting a state
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
    </>
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

  /**
   * The bridge these teeth form, when the condition being charted is a bridge unit.
   *
   * <p>⚠️ **A ref, and it must survive a partial failure.** `handleSave` posts one tooth at a time —
   * deliberately, so a failure can re-offer exactly the teeth that did not land — and the retry sends only
   * those. A fresh id on the retry would put the landed teeth in one group and the retried ones in another,
   * i.e. split one bridge in two, with the chart then drawing two runs where the dentist charted one.</p>
   *
   * <p>⚠️ Minted lazily and cleared only on success or on clearing the selection, never per render.</p>
   */
  const bridgeGroupRef = useRef<string | null>(null)

  /**
   * **A planned bridge charted here is one condition across N teeth, so it needs the roles asked for too.**
   *
   * <p>The fiche de soins asks « quelles dents sont des pontiques ? » because an act carries one
   * `resultingCondition` for all of its teeth. This panel has exactly the same shape and exactly the same
   * defect: without it, a three-unit bridge *planned* on the odontogramme could only be charted as three
   * piliers or three pontiques — the anatomically impossible reading the whole pilier/pontique split exists to
   * remove — and it is this surface, not the fiche, that the dentist was looking at when they reported it.</p>
   *
   * <p>⚠️ Each `ToothState` row carries its own condition, so nothing new is needed on the wire: the panel
   * simply posts a different condition per tooth. Empty means « all piliers », which is the default and is
   * never seeded — see `BridgeRolesStep` for why a positional guess is worse than none.</p>
   */
  const [ponticTeeth, setPonticTeeth] = useState<Set<number>>(new Set())

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
    const bridge = isBridgeUnit(effectiveCondition)
    /*
     * ⚠️ Minted here, not per tooth, and only for a bridge — the server folds it away for anything else,
     * but sending one on a carie would put a meaningless token on a clinical row. `??=` is what makes a retry
     * reuse the id the first attempt used.
     */
    if (bridge) bridgeGroupRef.current ??= crypto.randomUUID()
    // A group of one asserts nothing about a span, exactly as `BuildToothStates` decides server-side.
    const groupId = bridge && teeth.length > 1 ? bridgeGroupRef.current : null

    for (const tooth of teeth) {
      try {
        await odontogramApi.diagnose(patientId, {
          toothNumber: tooth,
          // Per tooth, so one gesture can chart piliers and pontiques together. Off a bridge every tooth gets
          // the same condition, which is what it always did.
          condition: bridge && ponticTeeth.has(tooth) ? "BridgePontique" : effectiveCondition,
          surfaces: serializeSurfaces(surfaces) || null,
          note: note.trim() || null,
          bridgeGroupId: groupId,
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
      setPonticTeeth(new Set())
      // The next gesture is a different bridge.
      bridgeGroupRef.current = null
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

  /*
    ⚠️ **Nothing at all until a tooth is ticked, and DOCKED to the bottom of the scrollport once one is.**

    It was an ordinary block in the flow directly under the arch, and the arch is tall: measured at 390x844 with
    the chart at a natural reading position (first tooth at y=433), « Ajouter le diagnostic à 3 dents » sat at
    **y = 844–880** — entirely below the fold, 330 px past the teeth being tapped. So the dentist ticked three
    molars, watched them highlight, and nothing they could see changed; « je ne savais pas qu'il fallait
    enregistrer » is the expected reading of that, and it is what was reported.

    ⚠️ **Auto-saving on the condition instead was the other candidate and it is worse**, for three reasons that
    are all about this form in particular: the faces and the note are entered *after* the condition, so writing
    on the pick would amputate both; a `Select` brushed while scrolling a phone would commit N clinical records,
    each undoable only through its own confirm dialog; and it would remove `handleSave`'s partial-failure retry,
    which re-ticks exactly the teeth that did not land. The commitment stays explicit and moves to where the eyes
    already are.

    `sticky`, deliberately not `fixed`: `AppShell`'s `<main>` is the scroller and `BottomNav` is its flex
    *sibling*, so sticking to the scrollport's bottom already clears the bar — no `--bottom-inset`, and nothing
    to keep in step with it. `-mx-6` reaches the card's own edge (`CardContent` is `px-6`), which is what makes
    it read as a docked bar rather than a floating panel.
  */
  if (teeth.length === 0) return null

  return (
    <div
      role="group"
      aria-label="Diagnostic commun aux dents sélectionnées"
      className="sticky bottom-0 z-30 -mx-6 border-t border-primary/30 bg-card px-6 py-2.5 shadow-[0_-4px_12px_-6px_rgb(0_0_0/0.15)]"
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        {/* The numbers written out, not just counted: « 3 dents » does not let anyone check they ticked 16 and
            not 15, and this bar writes to the record. `min-w-0` + `truncate` because a full-quadrant selection
            is a long line at 320px and this row must stay one line — the bar is docked over the chart. */}
        <p className="min-w-0 flex-1 truncate text-xs" title={`Dents : ${teeth.join(", ")}`}>
          <span className="font-medium text-foreground">
            {teeth.length} dent{teeth.length > 1 ? "s" : ""}
          </span>
          <span className="text-muted-foreground"> · {teeth.join(", ")}</span>
        </p>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => {
            // The roles and the group belong to THIS gesture; carrying either into the next selection would
            // chart a pontique on a tooth nobody marked, or file two bridges under one id.
            setPonticTeeth(new Set())
            bridgeGroupRef.current = null
            onClearSelection()
          }}
          disabled={saving}
          aria-label="Désélectionner toutes les dents"
          className="h-7 shrink-0 gap-1.5 px-2 text-xs coarse:h-11"
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" /> Effacer
        </Button>
      </div>

      <div className="mt-1.5 flex items-center gap-2">
        <Select value={effectiveCondition} onValueChange={onConditionChange}>
          {/* `min-w-0` so the trigger yields to the button beside it rather than pushing it out: `Button` is
              `whitespace-nowrap shrink-0`, so at 320 px the overflow lands on the control, not on the label. */}
          <SelectTrigger className="h-9 min-w-0 flex-1 text-xs coarse:h-11">
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
        <Button
          size="sm"
          className="h-9 shrink-0 gap-1.5 text-xs coarse:h-11"
          onClick={handleSave}
          disabled={saving}
        >
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          {saving ? "Enregistrement…" : "Enregistrer"}
        </Button>
      </div>

      {/*
        ⚠️ **The pontique question, asked on the PLANNING side too.**

        The fiche de soins asks it because an act carries one condition for all of its teeth. This bar has the
        same shape and had the same hole: a three-unit bridge *planned* here could only be charted as three
        piliers or three pontiques — the anatomically impossible reading the pilier/pontique split exists to
        remove. Each row it writes carries its own condition, so nothing new was needed on the wire.

        Rendered only for a bridge unit on more than one tooth, so nothing changes for ordinary charting; and
        nothing is pre-marked, for the reason `BridgeRolesStep` records at length — a positional guess is wrong
        on a pier abutment, on a cantilever and across the midline.
      */}
      {isBridgeUnit(effectiveCondition) && teeth.length > 1 && (
        <div role="group" aria-label="Dents pontiques" className="mt-1.5 flex flex-wrap items-center gap-1.5">
          <span className="text-2xs text-muted-foreground">Pontiques ?</span>
          {teeth.map((tooth) => (
            <button
              key={tooth}
              type="button"
              disabled={saving}
              aria-pressed={ponticTeeth.has(tooth)}
              aria-label={
                ponticTeeth.has(tooth)
                  ? `Dent ${tooth} : pontique — la repasser en pilier`
                  : `Dent ${tooth} : pilier — la passer en pontique`
              }
              onClick={() =>
                setPonticTeeth((prev) => {
                  const next = new Set(prev)
                  if (next.has(tooth)) next.delete(tooth)
                  else next.add(tooth)
                  return next
                })
              }
              className={cn(
                "inline-flex min-h-6 items-center rounded border px-1.5 font-mono text-2xs tabular-nums transition-colors coarse:min-h-11 coarse:px-2.5",
                ponticTeeth.has(tooth)
                  ? "border-primary/40 bg-primary/10 text-primary"
                  : "border-border text-muted-foreground hover-hover:hover:text-foreground",
              )}
            >
              {tooth}
            </button>
          ))}
          <span className="text-2xs text-muted-foreground">
            {ponticTeeth.size === 0 ? "aucun — toutes piliers" : `${ponticTeeth.size} pontique${ponticTeeth.size > 1 ? "s" : ""}`}
          </span>
        </div>
      )}

      {/*
        Faces and note folded, with the fold's own summary stating what is set — the two are optional on most
        charting and this bar sits over the arch, so every row it spends is a row of teeth it hides. A collapsed
        section that shows its value is the `record-section.tsx` rule: collapsing makes a value read-only, never
        hidden.
        ⚠️ Letter buttons here and the graphical picker in the single-tooth editor, deliberately: the faces apply
        to several teeth at once and mésial is on opposite sides of the two halves of the mouth, so there is no
        one drawing that could be pointed at.
      */}
      <details className="mt-1.5 group">
        <summary className="flex cursor-pointer list-none items-center gap-1.5 text-2xs text-muted-foreground coarse:py-2">
          <ChevronRight className="h-3 w-3 shrink-0 transition-transform group-open:rotate-90" aria-hidden="true" />
          <span className="truncate">
            {surfaces.size === 0 && note.trim() === ""
              ? "Faces et note (facultatif)"
              : [
                  surfaces.size > 0
                    ? `Faces : ${SURFACE_ORDER.filter((s) => surfaces.has(s)).join(", ")}`
                    : null,
                  note.trim() !== "" ? "note" : null,
                ]
                  .filter(Boolean)
                  .join(" · ")}
          </span>
        </summary>
        <div className="mt-1.5 space-y-1.5">
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
                aria-label={label}
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
        </div>
      </details>
    </div>
  )
}
