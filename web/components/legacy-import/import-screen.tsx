"use client"

import { memo, useCallback, useEffect, useMemo, useRef, useState } from "react"
import { FileText, FolderOpen, Images, Loader2 } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { PageHeader } from "@/components/ui/page-header"
import { useDemo } from "@/lib/demo/demo-context"
import { refuseInDemo } from "@/lib/demo/refuse-in-demo"
import { showErrorToast } from "@/lib/errors"
import { formatFileSize, quoteFr } from "@/lib/format"
import { useUploadPolicy } from "@/lib/hooks/use-upload-policy"
import {
  buildPlan,
  CABINET_PREFIX,
  destinationLabel,
  filesFor,
  undecided,
  type ImportPlan,
  type PlanPatient,
  type PlanProgress,
} from "@/lib/legacy-import/plan"
import { runImport, type PauseReason, type RowResult } from "@/lib/legacy-import/run"
import { FolderChoiceCard } from "./folder-choice-card"
import { PlanPatientCard } from "./plan-patient-card"

/**
 * « Importer l'ancien logiciel » — a practice's old software, moved in from the PC that holds the export: the folder
 * of record PDFs, the folder of radios/photos (one sub-folder per patient), or both, in as many passes as needed.
 *
 * Temporary: it serves one practice's migration and is removed once that is done. Every write goes through the
 * ordinary API as whoever is signed in; nothing is written before « Importer », and a second run creates nothing twice.
 */
export function LegacyImportScreen() {
  const { isDemo } = useDemo()
  const policy = useUploadPolicy("patient-file")
  const [records, setRecords] = useState<File[]>([])
  const [attachments, setAttachments] = useState<File[]>([])
  const [reading, setReading] = useState<PlanProgress | null>(null)
  const [plan, setPlan] = useState<ImportPlan | null>(null)
  const [results, setResults] = useState<Record<string, RowResult>>({})
  const [running, setRunning] = useState(false)
  const [finished, setFinished] = useState(false)
  const [stopped, setStopped] = useState<string | null>(null)
  /** The run's last pause — the server asked to slow down, or a call was lost — while it lasts. */
  const [pause, setPause] = useState<{ until: number; seconds: number; reason: PauseReason } | null>(null)
  const controller = useRef<AbortController | null>(null)
  /** Row updates since the last paint: a run of a thousand patients reports many a second, painted twice a second. */
  const pendingRows = useRef<Record<string, RowResult>>({})

  useEffect(() => {
    if (!running) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener("beforeunload", warn)
    return () => window.removeEventListener("beforeunload", warn)
  }, [running])

  // An import left to run on its own must not be stopped by the screen going to sleep. The browser grants the lock
  // to a visible page only, and drops it when the page is hidden, so it is asked again when the page comes back.
  useEffect(() => {
    if (!running || typeof navigator === "undefined" || !("wakeLock" in navigator)) return
    let lock: WakeLockSentinel | null = null
    let active = true
    const release = async (held: WakeLockSentinel | null) => {
      try {
        await held?.release()
      } catch {
        // Already released by the browser (the page was hidden): nothing to give back.
      }
    }
    const acquire = async () => {
      if (document.visibilityState !== "visible") return
      try {
        const granted = await navigator.wakeLock.request("screen")
        if (active) lock = granted
        else await release(granted)
      } catch {
        // Refused (battery saver, a policy): the import runs the same; only the screen may go to sleep.
      }
    }
    void acquire()
    const onVisible = () => void acquire()
    document.addEventListener("visibilitychange", onVisible)
    return () => {
      active = false
      document.removeEventListener("visibilitychange", onVisible)
      void release(lock)
    }
  }, [running])

  useEffect(() => {
    if (!running) return
    const timer = window.setInterval(flushRows, 500)
    return () => window.clearInterval(timer)
  }, [running])

  const analyse = async () => {
    if (refuseInDemo(isDemo)) return
    setReading({ done: 0, total: 0 })
    try {
      setPlan(await buildPlan(records, attachments, policy, setReading))
      setResults({})
      setFinished(false)
      setStopped(null)
    } catch (error) {
      showErrorToast(error)
    } finally {
      setReading(null)
    }
  }

  const reset = () => {
    setPlan(null)
    setResults({})
    setRecords([])
    setAttachments([])
    setFinished(false)
    setStopped(null)
  }

  const patchPatient = useCallback(
    (key: string, patch: Partial<PlanPatient>) =>
      setPlan((p) => p && { ...p, patients: p.patients.map((x) => (x.key === key ? { ...x, ...patch } : x)) }),
    [],
  )

  /** Paints the row updates received since the last paint. */
  function flushRows() {
    const rows = pendingRows.current
    if (!Object.keys(rows).length) return
    pendingRows.current = {}
    setResults((prev) => ({ ...prev, ...rows }))
  }

  const chooseFolder = (name: string, key: string | null, label?: string) =>
    setPlan((p) => {
      if (!p) return p
      const cabinet =
        key?.startsWith(CABINET_PREFIX) && label && !p.cabinet.some((c) => c.key === key)
          ? [...p.cabinet, { key, id: key.slice(CABINET_PREFIX.length), label, number: null }]
          : p.cabinet
      return { ...p, cabinet, folders: p.folders.map((f) => (f.name === name ? { ...f, choice: key } : f)) }
    })

  const start = async () => {
    if (!plan || refuseInDemo(isDemo)) return
    const abort = new AbortController()
    controller.current = abort
    setRunning(true)
    setStopped(null)
    const known = Object.fromEntries(Object.entries(results).map(([k, r]) => [k, r.patientId]))
    // A row finished by an earlier run on this page is not asked again — unless files still wait for space.
    const done = new Set(
      Object.entries(results)
        .filter(([, r]) => r.state === "done" && r.waitingForSpace === 0)
        .map(([k]) => k),
    )
    try {
      const stop = await runImport(plan, {
        policy,
        signal: abort.signal,
        known,
        done,
        onRow: (key, result) => {
          pendingRows.current[key] = result
        },
        onPause: (seconds, reason) => setPause({ until: Date.now() + seconds * 1000, seconds, reason }),
      })
      setStopped(stop)
      setFinished(true)
      if (!stop) toast.success("Import terminé.")
    } catch (error) {
      showErrorToast(error)
    } finally {
      flushRows()
      setRunning(false)
      setPause(null)
      controller.current = null
    }
  }

  const summary = useMemo(() => (plan ? summarise(plan) : null), [plan])
  const failed = Object.values(results).filter((r) => r.state === "failed").length
  /** Each card's inputs, built once per plan — a results update repaints only the cards whose row changed. */
  const cards = useMemo(
    () =>
      plan
        ? plan.patients
            .filter((p) => !p.mergeInto)
            .map((p) => ({
              patient: p,
              merged: plan.patients.filter((m) => m.mergeInto === p.key),
              files: filesFor(plan, p.key),
              folders: plan.folders.filter((f) => f.choice === p.key).map((f) => f.name),
              historyCount: plan.patients.filter((m) => (m.mergeInto ?? m.key) === p.key).reduce((n, m) => n + m.history.length, 0),
            }))
        : [],
    [plan],
  )
  const rowsTotal = cards.filter((c) => !c.patient.skip).length
  const rowsSettled = cards.filter((c) => {
    const state = results[c.patient.key]?.state
    return !c.patient.skip && (state === "done" || state === "failed")
  }).length

  return (
    <div className="space-y-6">
      <PageHeader
        title="Importer l'ancien logiciel"
        subtitle="Les PDF des dossiers, les radios et photos, ou les deux. Rien n'est écrit avant « Importer »."
      />

      {!plan && (
        <div className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2">
            <FolderPicker
              icon={FileText}
              title="Dossiers PDF"
              files={records}
              onPick={setRecords}
              disabled={!!reading}
              isDemo={isDemo}
            />
            <FolderPicker
              icon={Images}
              title="Radios et photos"
              files={attachments}
              onPick={setAttachments}
              disabled={!!reading}
              isDemo={isDemo}
            />
          </div>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <Button
              type="button"
              className="gap-2 coarse:min-h-11"
              disabled={!!reading || (!records.length && !attachments.length)}
              onClick={() => void analyse()}
            >
              {reading && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
              Analyser
            </Button>
            {reading && (
              <p className="text-sm text-muted-foreground" role="status">
                {reading.total ? `Lecture des dossiers… ${reading.done} sur ${reading.total}` : "Lecture des dossiers…"}
              </p>
            )}
          </div>
        </div>
      )}

      {plan && summary && (
        <>
          <ul className="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-2 xl:grid-cols-4">
            <Figure label="Patients à créer" value={summary.toCreate} />
            <Figure label="Déjà dans le cabinet" value={summary.existing} />
            <Figure label="Fichiers à envoyer" value={summary.files} hint={formatFileSize(summary.bytes)} />
            <Figure label="Fichiers refusés" value={summary.refused} />
          </ul>

          {summary.quotaShort > 0 && (
            <p className="rounded-md border border-warning bg-warning-wash p-3 text-sm text-warning-ink" role="alert">
              L&apos;espace du cabinet ne suffit pas pour tous ces fichiers : il manque {formatFileSize(summary.quotaShort)}.
              Les patients et l&apos;historique seront importés ; l&apos;envoi des fichiers s&apos;arrêtera quand l&apos;espace sera
              plein, et reprendra en relançant l&apos;import une fois de la place libérée.
            </p>
          )}
          {plan.looseFiles.length > 0 && (
            <p className="text-sm text-muted-foreground">
              {plan.looseFiles.length} fichier{plan.looseFiles.length > 1 ? "s" : ""} hors d&apos;un dossier patient ne
              {plan.looseFiles.length > 1 ? " seront" : " sera"} pas importé{plan.looseFiles.length > 1 ? "s" : ""}.
            </p>
          )}

          {plan.folders.length > 0 && (
            <section className="space-y-3" aria-labelledby="li-folders">
              <h2 id="li-folders" className="text-base font-semibold">
                Dossiers de radios et photos
                {summary.undecided > 0 && (
                  <span className="ms-2 text-sm font-normal text-warning-ink">{summary.undecided} à confirmer</span>
                )}
              </h2>
              <ul className="space-y-3">
                {orderFolders(plan).map((f) => (
                  <FolderChoiceCard
                    key={f.name}
                    folder={f}
                    plan={plan}
                    disabled={running}
                    onChoose={(key, label) => chooseFolder(f.name, key, label)}
                  />
                ))}
              </ul>
            </section>
          )}

          {plan.patients.length > 0 && (
            <section className="space-y-3" aria-labelledby="li-patients">
              <h2 id="li-patients" className="text-base font-semibold">
                Patients
              </h2>
              <ul className="space-y-3">
                {cards.map((c) => (
                  <PatientRow key={c.patient.key} card={c} result={results[c.patient.key]} locked={running} onPatch={patchPatient} />
                ))}
              </ul>
            </section>
          )}

          {Object.keys(results).some((k) => k.startsWith(CABINET_PREFIX)) && (
            <section className="space-y-2" aria-labelledby="li-cabinet">
              <h2 id="li-cabinet" className="text-base font-semibold">
                Fichiers ajoutés à des patients du cabinet
              </h2>
              <ul className="divide-y rounded-lg border bg-card">
                {Object.entries(results)
                  .filter(([k]) => k.startsWith(CABINET_PREFIX))
                  .map(([k, r]) => (
                    <li key={k} className="space-y-1 p-3 text-sm">
                      <p className="font-medium">{destinationLabel(plan, k)}</p>
                      <p className={r.state === "failed" ? "text-destructive" : "text-muted-foreground"} role="status">
                        {r.state === "running"
                          ? `Envoi… ${r.sent} fichier${r.sent > 1 ? "s" : ""}`
                          : `${r.sent} envoyé${r.sent > 1 ? "s" : ""} · ${r.already} déjà là${r.waitingForSpace ? ` · ${r.waitingForSpace} en attente d'espace` : ""}${r.errors.length ? ` · ${r.errors.join(" · ")}` : ""}`}
                      </p>
                    </li>
                  ))}
              </ul>
            </section>
          )}

          <div className="sticky -bottom-4 z-20 -mx-4 -mb-4 border-t border-border bg-card/95 px-4 py-3 backdrop-blur-sm md:-bottom-6 md:-mx-6 md:-mb-6 md:px-6">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <p className="min-w-0 text-sm" role="status">
                {stopped ? (
                  <span className="text-destructive">{stopped}</span>
                ) : running ? (
                  pause && pause.until > Date.now()
                    ? pause.reason === "connection"
                      ? `Connexion perdue : nouvel essai dans ${pause.seconds} s. L'import reprend tout seul.`
                      : `Pause de ${pause.seconds} s : le serveur demande de ralentir. L'import reprend tout seul.`
                    : `Import en cours : ${rowsSettled} sur ${rowsTotal} patient${rowsTotal > 1 ? "s" : ""}. Gardez cette page ouverte.`
                ) : summary.undecided > 0 ? (
                  <span className="text-warning-ink">
                    {summary.undecided} dossier{summary.undecided > 1 ? "s" : ""} à confirmer avant d&apos;importer.
                  </span>
                ) : finished ? (
                  failed ? `Import terminé, ${failed} ligne${failed > 1 ? "s" : ""} incomplète${failed > 1 ? "s" : ""} : corrigez-les puis relancez.` : "Import terminé."
                ) : (
                  `${summary.toCreate} patient${summary.toCreate > 1 ? "s" : ""} à créer · ${summary.files} fichier${summary.files > 1 ? "s" : ""} à envoyer`
                )}
              </p>
              <div className="flex flex-wrap gap-2">
                {running ? (
                  <Button type="button" variant="outline" className="coarse:min-h-11" onClick={() => controller.current?.abort()}>
                    Arrêter
                  </Button>
                ) : (
                  <Button type="button" variant="outline" className="coarse:min-h-11" onClick={reset}>
                    Changer de dossiers
                  </Button>
                )}
                <Button
                  type="button"
                  className="gap-2 coarse:min-h-11"
                  disabled={running || summary.undecided > 0 || (summary.toCreate + summary.existing + summary.cabinetTargets === 0)}
                  onClick={() => void start()}
                >
                  {running && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
                  {finished ? "Relancer l'import" : "Importer"}
                </Button>
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  )
}

type Card = {
  patient: PlanPatient
  merged: PlanPatient[]
  files: ReturnType<typeof filesFor>
  folders: string[]
  historyCount: number
}

/** One patient card, repainted only when its own row, its own plan entry or the lock changes. */
const PatientRow = memo(function PatientRow({
  card,
  result,
  locked,
  onPatch,
}: {
  card: Card
  result: RowResult | undefined
  locked: boolean
  onPatch: (key: string, patch: Partial<PlanPatient>) => void
}) {
  return (
    <PlanPatientCard
      patient={card.patient}
      merged={card.merged}
      files={card.files}
      folders={card.folders}
      historyCount={card.historyCount}
      result={result}
      locked={locked}
      onChange={(patch) => onPatch(card.patient.key, patch)}
    />
  )
})

function summarise(plan: ImportPlan) {
  const roots = plan.patients.filter((p) => !p.mergeInto && !p.skip)
  const existing = roots.filter((p) => p.existing && !p.createAnyway).length
  const cabinetKeys = new Set(plan.folders.map((f) => f.choice).filter((k): k is string => !!k?.startsWith(CABINET_PREFIX)))
  const all = [...roots.map((p) => p.key), ...cabinetKeys].flatMap((k) => filesFor(plan, k))
  const ok = all.filter((f) => !f.refusal)
  const bytes = ok.reduce((n, f) => n + f.file.size, 0)
  const room = plan.quota && plan.quota.total > 0 ? plan.quota.total - plan.quota.used : null
  return {
    toCreate: roots.length - existing,
    existing,
    cabinetTargets: cabinetKeys.size,
    files: ok.length,
    bytes,
    refused: all.length - ok.length,
    undecided: undecided(plan).length,
    quotaShort: room != null && bytes > room ? bytes - room : 0,
  }
}

/** To confirm first, then the unknown, then those filed on their own. */
function orderFolders(plan: ImportPlan) {
  const rank = (f: ImportPlan["folders"][number]) => (f.choice === undefined ? 0 : f.choice === null ? 1 : 2)
  return [...plan.folders].sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name))
}

function Figure({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <li className="space-y-0.5 bg-card p-3">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="text-2xl font-semibold tabular-nums">
        {value}
        {hint && <span className="ms-2 text-sm font-normal text-muted-foreground">{hint}</span>}
      </p>
    </li>
  )
}

function FolderPicker({
  icon: Icon,
  title,
  files,
  onPick,
  disabled,
  isDemo,
}: {
  icon: typeof FileText
  title: string
  files: File[]
  onPick: (files: File[]) => void
  disabled: boolean
  isDemo: boolean | null
}) {
  const input = useRef<HTMLInputElement>(null)
  const root = files[0] ? ((files[0] as File & { webkitRelativePath?: string }).webkitRelativePath || "").split("/")[0] : ""
  const size = files.reduce((n, f) => n + f.size, 0)
  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
      <div className="flex items-center gap-2">
        <Icon className="size-5 text-muted-foreground" aria-hidden="true" />
        <h2 className="font-semibold">{title}</h2>
      </div>
      {files.length > 0 ? (
        <p className="break-words text-sm" role="status">
          {root ? `${quoteFr(root)} · ` : ""}
          {files.length} fichier{files.length > 1 ? "s" : ""} · {formatFileSize(size)}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">Aucun dossier choisi.</p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          className="gap-2 coarse:min-h-11"
          disabled={disabled}
          onClick={() => {
            if (refuseInDemo(isDemo)) return
            input.current?.click()
          }}
        >
          <FolderOpen className="size-4" aria-hidden="true" />
          {files.length ? "Changer de dossier" : "Choisir le dossier"}
        </Button>
        {files.length > 0 && (
          <Button type="button" variant="ghost" className="coarse:min-h-11" disabled={disabled} onClick={() => onPick([])}>
            Retirer
          </Button>
        )}
      </div>
      <input
        ref={input}
        type="file"
        multiple
        className="sr-only"
        tabIndex={-1}
        aria-hidden="true"
        {...{ webkitdirectory: "" }}
        onChange={(event) => {
          // Copied before the value is cleared — clearing empties the live FileList.
          const picked = Array.from(event.currentTarget.files ?? [])
          event.currentTarget.value = ""
          if (picked.length) onPick(picked)
        }}
      />
    </div>
  )
}
