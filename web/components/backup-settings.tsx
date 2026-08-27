"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { Badge } from "@/components/ui/badge"
import {
  DatabaseBackup,
  FolderDown,
  CheckCircle2,
  ShieldAlert,
  AlertTriangle,
  Clock,
  RotateCcw,
} from "lucide-react"
import { ClinicArchiveCard } from "@/components/backup/clinic-archive-card"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { backupApi, type BackupHistoryDto, type BackupRunDto, type BackupRunOutcome } from "@/lib/api/backup"
import { ApiError } from "@/lib/api/client"
import { formatDateTime } from "@/lib/format"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"

function formatSize(bytes: number): string {
  if (bytes <= 0) return "0 o"
  const units = ["o", "Ko", "Mo", "Go", "To"]
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/** How many attempts to list. A short window: this is a settings card, not a log viewer. */
const HISTORY_PAGE_SIZE = 8

/**
 * The card's header, shared by the two things this component can be: the clinic's own backup panel, and the
 * « gérées par l'hébergeur » statement. Extracted rather than duplicated so the two cannot drift into looking
 * like different sections of « Paramètres ».
 */
function BackupCardHeader() {
  return (
    <CardHeader className="pb-3">
      {/*
        The icon chip — `app/documents/page.tsx`'s template-tile idiom, sized for a header. The hue is the
        `config` zone's, matching the four sections of « Paramètres » this card is stacked under.
      */}
      <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
        <span
          aria-hidden="true"
          className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
        >
          <DatabaseBackup className="size-4" strokeWidth={1.75} />
        </span>
        Sauvegarde
      </CardTitle>
    </CardHeader>
  )
}

/**
 * Admin-only « Sauvegarde » card (US-8 / FR-G / AC-8.1–8.3, extended by L4).
 *
 * <p>The card's <b>headline is « Dernière sauvegarde réussie »</b>, not the button. That inversion is the whole
 * point of L4d: before it, the result of a backup lived in this component's own `useState`, so the only question
 * that matters about a backup — <i>when did one last work?</i> — was answered by the product only until the tab
 * was closed, and never at all for the nightly job.</p>
 *
 * <p>Four things are on screen for one reason each: the last success (is the data safe?), the schedule (will it
 * stay safe without anyone remembering?), the history (is it trying and failing?), and the restore command (can
 * the owner get their data back?). The button that used to be the entire card is now the least important thing
 * on it.</p>
 */
export function BackupSettings() {
  const [destination, setDestination] = useState("")
  const [working, setWorking] = useState(false)
  const [lastResult, setLastResult] = useState<{
    path: string
    size: string
    objects: number
    warning?: string | null
  } | null>(null)

  const [history, setHistory] = useState<BackupHistoryDto | null>(null)
  const [historyFailed, setHistoryFailed] = useState(false)

  // The schedule is edited locally and saved explicitly: a switch that saves on toggle would fire a request per
  // keystroke on the three number fields beside it, and « sauvegarde désactivée » is not a change to apply by
  // accident.
  const [schedule, setSchedule] = useState<{
    enabled: boolean
    hourLocal: string
    retentionCount: string
    staleAfterHours: string
  } | null>(null)
  const [savingSchedule, setSavingSchedule] = useState(false)

  // Guard against setState after unmount: a backup can be long-running and the operator may navigate away
  // from /settings mid-request (Finding 18 — matches the guarded-async pattern used in session.tsx).
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const loadHistory = useCallback(async () => {
    try {
      const result = await backupApi.history({ pageSize: HISTORY_PAGE_SIZE })
      if (!mounted.current) return
      setHistory(result)
      setHistoryFailed(false)
      setSchedule({
        enabled: result.backupEnabled,
        hourLocal: String(result.backupHourLocal),
        retentionCount: String(result.retentionCount),
        staleAfterHours: String(result.staleAfterHours),
      })
    } catch {
      // A failed read is NOT « aucune sauvegarde » — on this card that reading would be the most dangerous
      // possible lie, since it is exactly what a clinic with no protection looks like.
      if (mounted.current) setHistoryFailed(true)
    }
  }, [])

  useEffect(() => {
    void loadHistory()
  }, [loadHistory])

  const handleBackup = async () => {
    setWorking(true)
    setLastResult(null)
    try {
      const result = await backupApi.backupNow(destination)
      if (mounted.current) {
        setLastResult({
          path: result.destinationPath,
          size: formatSize(result.sizeBytes),
          objects: result.verifiedObjectCount,
          warning: result.warning,
        })
      }

      // AC-14.3: the backup succeeded, but if it could not be access-restricted the admin has to know now —
      // a warning only in the server log is a warning nobody reads. Kept as a distinct, longer-lived toast
      // rather than folded into the success description, which is easy to dismiss without reading.
      if (result.warning) {
        toast.warning("Sauvegarde à revoir", { description: result.warning, duration: 12000 })
      } else {
        toast.success("Sauvegarde terminée", {
          description: `${result.destinationPath} (${formatSize(result.sizeBytes)}, ${result.verifiedObjectCount} objets vérifiés)`,
        })
      }

      // The headline and the history are now facts on the server, so re-read them rather than patching local
      // state — which is how the old card could show a success the server had not recorded.
      await loadHistory()
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "La sauvegarde a échoué."
      toast.error("Échec de la sauvegarde", { description: message })
      // A failed attempt is recorded too, and it is the more useful row of the two.
      await loadHistory()
    } finally {
      if (mounted.current) {
        setWorking(false)
      }
    }
  }

  const handleSaveSchedule = async () => {
    if (!schedule) return
    setSavingSchedule(true)
    try {
      const saved = await backupApi.setSchedule({
        enabled: schedule.enabled,
        // The server validates the ranges and returns its own French message; sending NaN as 0 lets it do that
        // rather than inventing a second copy of the rules here.
        hourLocal: Number(schedule.hourLocal) || 0,
        retentionCount: Number(schedule.retentionCount) || 0,
        staleAfterHours: Number(schedule.staleAfterHours) || 0,
      })
      if (!mounted.current) return
      setSchedule({
        enabled: saved.enabled,
        hourLocal: String(saved.hourLocal),
        retentionCount: String(saved.retentionCount),
        staleAfterHours: String(saved.staleAfterHours),
      })
      toast.success("Planification enregistrée")
    } catch (err) {
      showErrorToast(err, "La planification n'a pas pu être enregistrée.")
    } finally {
      if (mounted.current) setSavingSchedule(false)
    }
  }

  const lastSuccess = history?.lastSuccessAt ?? null
  const isStale =
    history != null &&
    (lastSuccess === null ||
      Date.now() - new Date(lastSuccess).getTime() > history.staleAfterHours * 3_600_000)

  /*
    ── This deployment's backups belong to its host ──────────────────────────────────────────────────────────
    `managedByHost` comes from the server (`DeploymentProfile.BacksUpItsOwnData`), and the card becomes a
    statement rather than a control panel: there is no button, no schedule and no history, because on a hosted
    deployment none of the three is this clinic's to operate.

    ⚠️ This branch exists because the alternative was actively misleading, not merely useless. The `POST` and the
    schedule now 404 there, and before that they answered « L'outil pg_dump est introuvable » — an error naming a
    tool and a config key, on a screen whose reader cannot install software on our servers — while a
    « sauvegarde périmée » alert sat unclearable in the bell.

    ⚠️ It claims **nothing about whether a backup has happened** — not a date, and not the fact either. The first
    draft said « vos données sont sauvegardées automatiquement par l'hébergeur », which this application cannot
    verify and which is outright false in two ordinary cases: the dev compose runs no `backup` sidecar at all, and
    a hosted deployment whose operator never set `BACKUP_REMOTE` gets `backup.sh`'s own « kept LOCAL ONLY — not
    off-server » warning. A reassurance nobody checked is worse than no reassurance, because it is read by the one
    person who would otherwise go and ask. So the card states only what is true here — the control is not on this
    screen — and names who can answer the rest.

    ⚠️ It is **no longer a dead end**, which is what the interim note here used to say it was. The per-clinic
    archive (`clinic-data-archive-and-restore`) is exactly the two copies the product owner asked for — the server's,
    kept by the host, and the practice's own, downloaded from here — and its restore is what the « Exporter » CSVs
    could never be. The statement about the *server's* backups stays, because it is still true and still the thing
    an owner arriving on this card is confused about; what changed is that the card now answers « et de mon côté ? »
    with a control instead of a suggestion.
  */
  if (history?.managedByHost) {
    return (
      <Card>
        <BackupCardHeader />
        <CardContent className="space-y-3">
          <div className="flex items-start gap-2.5 rounded-lg border border-border bg-muted/40 p-3">
            <ShieldAlert className="mt-0.5 size-4 shrink-0 text-muted-foreground" strokeWidth={1.75} />
            <div className="min-w-0 space-y-1.5">
              <p className="text-sm font-medium leading-snug">
                La sauvegarde du serveur relève de votre hébergeur
              </p>
              <p className="text-sm leading-relaxed text-muted-foreground">
                Il n&apos;y a rien à planifier ni à lancer ici pour le serveur lui-même. Pour savoir quand vos
                données ont été sauvegardées pour la dernière fois, et où, contactez votre hébergeur.
              </p>
            </div>
          </div>

          <ClinicArchiveCard />
        </CardContent>
      </Card>
    )
  }

  return (
    // No border override: `Card` already renders `border`, which the base layer paints `--border`.
    <Card>
      <BackupCardHeader />
      <CardContent className="space-y-4">
        {/*
          ── The headline (L4d) ───────────────────────────────────────────────────────────────────────────────
          First, before the button and before any explanation. « Dernière sauvegarde réussie » is the only line
          on this card that answers « mes données sont-elles à l'abri ? », and it is the line the staleness
          notification in the bell deep-links here to show.
        */}
        <div
          role="status"
          className={cn(
            "flex items-start gap-2 rounded-lg border p-3",
            historyFailed
              ? "border-border bg-muted/40"
              : isStale
                ? "border-warning/40 bg-warning-wash"
                : "border-success/25 bg-success-wash",
          )}
        >
          {historyFailed ? (
            <AlertTriangle className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
          ) : isStale ? (
            <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning-ink" />
          ) : (
            <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" />
          )}
          <div className="min-w-0 space-y-0.5">
            {historyFailed ? (
              <>
                <p className="text-xs font-medium">L&apos;état des sauvegardes n&apos;a pas pu être lu</p>
                <Button variant="link" size="sm" className="h-auto p-0 text-xs" onClick={() => void loadHistory()}>
                  Réessayer
                </Button>
              </>
            ) : lastSuccess ? (
              <>
                <p
                  className={cn(
                    "text-xs font-medium",
                    isStale ? "text-warning-ink" : "text-success",
                  )}
                >
                  Dernière sauvegarde réussie : {formatDateTime(lastSuccess)}
                </p>
                <p className={cn("text-2xs", isStale ? "text-warning-ink" : "text-success")}>
                  {history?.lastSuccessSizeBytes != null && `${formatSize(history.lastSuccessSizeBytes)} · `}
                  {isStale
                    ? `au-delà du seuil de ${history?.staleAfterHours} h — vérifiez le dossier de destination`
                    : "vérifiée lisible"}
                </p>
              </>
            ) : (
              <>
                <p className="text-xs font-medium text-warning-ink">Aucune sauvegarde n&apos;a encore réussi</p>
                <p className="text-2xs text-warning-ink">
                  Lancez-en une maintenant, puis vérifiez que le dossier de destination est sur un autre disque.
                </p>
              </>
            )}
          </div>
        </div>

        <p className="text-xs text-muted-foreground">
          Sauvegarde la base de données et les fichiers de la clinique dans un dossier daté, puis vérifie que le
          fichier produit est bien lisible. Laissez le champ vide pour utiliser le dossier par défaut du serveur
          {history?.defaultDestination && (
            <>
              {" "}
              (<code className="break-all font-mono text-2xs">{history.defaultDestination}</code>)
            </>
          )}
          .
        </p>

        <div className="space-y-1">
          <Label htmlFor="backup-destination" className="text-xs font-medium">
            Dossier de destination
          </Label>
          <Input
            id="backup-destination"
            placeholder={history?.defaultDestination || "Ex. D:\\Sauvegardes clinique"}
            value={destination}
            onChange={(e) => setDestination(e.target.value)}
            disabled={working}
            /* `md:text-sm`, not `text-sm`: `ui/input.tsx`'s `text-base md:text-sm` is the iOS focus-zoom
               guard, and an unprefixed size here replaces the `text-base` half of it. */
            className="h-8 md:text-sm"
          />
        </div>

        <div className="flex justify-end">
          <Button
            onClick={handleBackup}
            size="sm"
            className="h-8 bg-primary text-xs hover:bg-primary/90"
            disabled={working}
          >
            <FolderDown className="mr-1 h-3.5 w-3.5" />
            {working ? "Sauvegarde en cours…" : "Sauvegarder maintenant"}
          </Button>
        </div>

        {lastResult && (
          <div className="flex items-start gap-2 rounded-lg border border-success/25 bg-success-wash p-3">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" />
            <div className="space-y-0.5">
              <p className="text-xs font-medium text-success">Sauvegarde effectuée</p>
              <p className="break-all text-xs text-success">{lastResult.path}</p>
              <p className="text-2xs text-success">
                {lastResult.size} · {lastResult.objects} objets vérifiés
              </p>
            </div>
          </div>
        )}

        {/* AC-14.3 / L4b: persists next to the success panel, so the exposure stays visible after the toast is
            gone. The backup DID work — this is about where it landed and who can read it. */}
        {lastResult?.warning && (
          <div className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning-wash p-3">
            <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-warning-ink" />
            <div className="space-y-0.5">
              <p className="text-xs font-medium text-warning-ink">Sauvegarde à revoir</p>
              <p className="text-xs text-warning-ink">{lastResult.warning}</p>
            </div>
          </div>
        )}

        {/*
          ── The schedule (L4a) ───────────────────────────────────────────────────────────────────────────────
          The four columns' caller. `Clinic.SetStockExpiryLeadDays` shipped with none, and its window has been
          permanently 30 days ever since — the failure this section exists not to repeat.
        */}
        {schedule && (
          <div className="space-y-3 rounded-lg border p-3">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="flex items-center gap-1.5 text-xs font-medium">
                  <Clock className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                  Sauvegarde automatique
                </p>
                <p className="mt-0.5 text-2xs text-muted-foreground">
                  Tous les jours, sans intervention. Si le poste est éteint à l&apos;heure prévue, la sauvegarde
                  est faite dès qu&apos;il est rallumé.
                </p>
              </div>
              <Switch
                checked={schedule.enabled}
                onCheckedChange={(enabled) => setSchedule({ ...schedule, enabled })}
                aria-label="Activer la sauvegarde automatique"
              />
            </div>

            {/* `sm:grid-cols-3`, never an ungated `grid-cols-3`: three number fields at 320 px would be ~90 px
                each and their labels do not fit. */}
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="space-y-1">
                <Label htmlFor="backup-hour" className="text-2xs font-medium">
                  Heure (locale)
                </Label>
                <Input
                  id="backup-hour"
                  type="number"
                  min={0}
                  max={23}
                  value={schedule.hourLocal}
                  onChange={(e) => setSchedule({ ...schedule, hourLocal: e.target.value })}
                  disabled={!schedule.enabled || savingSchedule}
                  className="h-8 tabular-nums md:text-sm"
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="backup-retention" className="text-2xs font-medium">
                  Copies conservées
                </Label>
                <Input
                  id="backup-retention"
                  type="number"
                  min={1}
                  max={365}
                  value={schedule.retentionCount}
                  onChange={(e) => setSchedule({ ...schedule, retentionCount: e.target.value })}
                  disabled={savingSchedule}
                  className="h-8 tabular-nums md:text-sm"
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="backup-stale" className="text-2xs font-medium">
                  Alerter après (h)
                </Label>
                <Input
                  id="backup-stale"
                  type="number"
                  min={1}
                  max={720}
                  value={schedule.staleAfterHours}
                  onChange={(e) => setSchedule({ ...schedule, staleAfterHours: e.target.value })}
                  disabled={savingSchedule}
                  className="h-8 tabular-nums md:text-sm"
                />
              </div>
            </div>

            <div className="flex justify-end">
              <Button
                variant="outline"
                size="sm"
                className="h-8 text-xs"
                onClick={handleSaveSchedule}
                disabled={savingSchedule}
              >
                {savingSchedule ? "Enregistrement…" : "Enregistrer la planification"}
              </Button>
            </div>
          </div>
        )}

        {/*
          ── The history (L4d) ────────────────────────────────────────────────────────────────────────────────
          Deliberately includes failures, and they are the valuable rows: « nobody has backed up since Tuesday »
          and « it has been trying every night and failing » are entirely different conversations.
        */}
        {history && history.page.items.length > 0 && (
          <div className="space-y-1.5">
            <p className="text-xs font-medium">Historique</p>
            <ul className="divide-y rounded-lg border">
              {history.page.items.map((run) => (
                <li key={run.id} className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 p-2.5">
                  <Badge variant="secondary" className={cn("shrink-0", OUTCOME_CLASS[run.outcome])}>
                    {OUTCOME_LABEL[run.outcome]}
                  </Badge>
                  <span className="whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                    {formatDateTime(run.startedAt)}
                  </span>
                  <span className="font-mono text-2xs text-muted-foreground">
                    {run.trigger === "manual" ? "manuelle" : "automatique"}
                  </span>
                  {run.sizeBytes != null && (
                    <span className="text-2xs tabular-nums text-muted-foreground">{formatSize(run.sizeBytes)}</span>
                  )}
                  {run.verifiedObjectCount != null && (
                    <span className="text-2xs tabular-nums text-muted-foreground">
                      {run.verifiedObjectCount} objets
                    </span>
                  )}
                  {/* The reason lives in the row, not a tooltip — it is the only thing that makes a failed
                      backup actionable, and a tooltip is unreachable on the tablet at the desk. */}
                  {run.error && <p className="w-full text-2xs text-destructive">{run.error}</p>}
                </li>
              ))}
            </ul>
            {history.page.totalCount > history.page.items.length && (
              <p className="text-2xs text-muted-foreground">
                {history.page.totalCount.toLocaleString("fr-TN")} tentatives enregistrées au total.
              </p>
            )}
          </div>
        )}

        {/*
          ── The portable copy (clinic-data-archive-and-restore) ──────────────────────────────────────────────
          Offered here too, beside the machine-level backup rather than instead of it, because the two answer
          different questions: `pg_dump` protects *this installation* and is restored by stopping the service,
          while the archive is one cabinet's own records in a file that can be carried to another machine and put
          back through the application. A practice that loses the PC has the second and not the first.
        */}
        <ClinicArchiveCard />

        {/*
          ── Restore (L4g) ────────────────────────────────────────────────────────────────────────────────────
          The command, with the resolved path filled in. Not a button: a restore runs with the application
          STOPPED — it drops and recreates every table the app holds open — so an in-app action would be asking
          the patient to perform their own surgery. Printing the exact command with the real folder is what makes
          it something an owner can do rather than something requiring someone comfortable in PowerShell.
        */}
        <div className="space-y-1.5 rounded-lg border border-dashed p-3">
          <p className="flex items-center gap-1.5 text-xs font-medium">
            <RotateCcw className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
            Restaurer une sauvegarde
          </p>
          <p className="text-2xs text-muted-foreground">
            À faire <strong>application arrêtée</strong>. La commande refuse de s&apos;exécuter si le service
            tourne encore, vérifie la sauvegarde avant d&apos;y toucher, et enregistre d&apos;abord une copie de
            sécurité de l&apos;état actuel.
          </p>
          {/* Scrolls in its own container: a Windows path plus a command does not wrap usefully, and the page
              body must never scroll horizontally at 320 px. */}
          <pre className="overflow-x-auto rounded bg-muted/60 p-2 font-mono text-2xs">
            <code>
              sc stop ClinicManagementApi{"\n"}
              ClinicManagement.API.exe restore-backup &quot;
              {history?.defaultDestination
                ? `${history.defaultDestination}\\clinic-backup-...`
                : "<dossier de la sauvegarde>"}
              &quot;
            </code>
          </pre>
        </div>
      </CardContent>
    </Card>
  )
}

const OUTCOME_LABEL: Record<BackupRunOutcome, string> = {
  running: "En cours",
  succeeded: "Réussie",
  failed: "Échec",
}

/**
 * Semantic tokens, never palette literals — dark mode follows with no `dark:` variant.
 *
 * ⚠️ `running` is `text-warning-ink`, not `text-warning`: `--warning` sits at L 0.62 and measures near 3.5:1
 * against its own wash, under the floor for badge-sized text (`ui/status-tone.ts` carries the same note).
 */
const OUTCOME_CLASS: Record<BackupRunOutcome, string> = {
  running: "bg-warning-wash text-warning-ink",
  succeeded: "bg-success-wash text-success",
  failed: "bg-destructive-wash text-destructive",
}
