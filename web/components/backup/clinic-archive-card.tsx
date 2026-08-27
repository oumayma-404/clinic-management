"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Archive, ArrowDownToLine, ArrowUpFromLine, Loader2, ShieldAlert, TriangleAlert } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { ApiError } from "@/lib/api/client"
import {
  ARCHIVE_ERROR_CODES,
  backupApi,
  type ClinicArchiveRestoreReport,
  type RecoveryPointDto,
  type RecoveryPointsDto,
} from "@/lib/api/backup"
import { securityApi } from "@/lib/api/security"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { RecoveryPointsList } from "@/components/backup/recovery-points-list"
import { downloadBlob } from "@/lib/download"
import { showErrorToast } from "@/lib/errors"
import { formatDateTime, todayLocalIso } from "@/lib/format"

/**
 * The action names the server mints a confirmation for. They are **different** on purpose: download and restore
 * are opposite operations on the same records, and one token good for both would let « je vais télécharger une
 * copie » become « j'ai écrasé le cabinet » on a single click.
 */
const DOWNLOAD_ACTION = "download-clinic-archive"
const RESTORE_ACTION = "restore-clinic-archive"

/**
 * « Archive du cabinet » — download every record this practice holds as one file, and put one back
 * (`clinic-data-archive-and-restore`).
 *
 * <p>It is the answer to the question `pg_dump` cannot serve on a hosted deployment: that tool takes `--dbname`
 * and has no tenant predicate, so one cabinet's « sauvegarde » would be every other cabinet's patients. This goes
 * through the same tenant filter as every CSV export and carries one practice's rows and nothing else — which is
 * why it is offered on **every** deployment, including the clinic's own PC, where it is a *portable* copy the
 * machine-level backup beside it is not.</p>
 *
 * <p>⚠️ **The unencrypted warning is not boilerplate and is not collapsible.** The file is a complete copy of the
 * practice's medical records with no password on it, and the person clicking « Télécharger » is deciding where it
 * lands. Saying so once, in the same box as the button, is the whole mitigation.</p>
 *
 * <p>⚠️ **The restore's per-entity result is a list, never a table** (AC-10). It is read on a phone at the moment
 * an owner is most anxious, three columns of counts do not survive 320 px, and the numbers are what matter — not
 * their alignment.</p>
 */
export function ClinicArchiveCard() {
  const [downloading, setDownloading] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [pending, setPending] = useState<File | null>(null)
  const [report, setReport] = useState<ClinicArchiveRestoreReport | null>(null)

  const fileInput = useRef<HTMLInputElement>(null)

  // FR-4.3 — which action the open step-up is confirming, or null. One dialog for both, because the surface is
  // identical and only the sentence and the action name differ.
  const [stepUpFor, setStepUpFor] = useState<typeof DOWNLOAD_ACTION | typeof RESTORE_ACTION | null>(null)
  const [hasTotp, setHasTotp] = useState(false)

  // « Points de restauration » (clinic-recovery-points) — the copies the server keeps, and the one that is being
  // restored from. `pendingPoint` is the confirmation's subject; `restoringId` is what shows the spinner on its row.
  const [recovery, setRecovery] = useState<RecoveryPointsDto | null>(null)
  const [recoveryLoading, setRecoveryLoading] = useState(true)
  const [recoveryFailed, setRecoveryFailed] = useState(false)
  const [pendingPoint, setPendingPoint] = useState<RecoveryPointDto | null>(null)
  const [restoringId, setRestoringId] = useState<string | null>(null)

  // Which proof the step-up offers first.
  //
  // ⚠️ A failed read is NOT rendered as « this account has no second factor » — that is the collapse
  // `check:responsive`'s `failed-read-as-empty` exists to stop. It is rendered as « we do not know », and not
  // knowing means offering **both** proofs: the password every account has, and the code an enrolled one may
  // prefer. Refusing the export over an unreadable probe would be the worse error by far, so this never gates
  // anything — the server re-checks whichever proof arrives.
  const [totpKnown, setTotpKnown] = useState(false)

  useEffect(() => {
    let cancelled = false

    const read = async () => {
      try {
        const state = await securityApi.getTotpState()
        if (!cancelled) {
          setHasTotp(state.enrolledAt !== null)
          setTotpKnown(true)
        }
      } catch {
        // Left unknown on purpose — see above. Nothing is disabled and nothing is asserted about the account.
        if (!cancelled) setTotpKnown(false)
      }
    }

    void read()

    return () => {
      cancelled = true
    }
  }, [])

  const loadRecoveryPoints = useCallback(async () => {
    setRecoveryLoading(true)
    setRecoveryFailed(false)
    try {
      setRecovery(await backupApi.recoveryPoints())
    } catch {
      // A failed read is NOT « aucun point de restauration » — that is a confidently wrong answer on the screen
      // somebody opens *because* they have lost data (§ 13, failed-read-as-empty).
      setRecoveryFailed(true)
    } finally {
      setRecoveryLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadRecoveryPoints()
  }, [loadRecoveryPoints])

  const handleRestorePoint = async (confirmationToken: string) => {
    if (!pendingPoint) return

    const point = pendingPoint
    setRestoringId(point.id)
    setPendingPoint(null)
    try {
      const result = await backupApi.restoreFromRecoveryPoint(point.id, confirmationToken)
      setReport(result)

      toast.success("Restauration terminée", {
        description: `${result.totalRestored} enregistrement(s) remis en place.`,
      })

      // Re-read: the restore writes rows, and « déjà présent » on a second attempt is what proves it worked.
      void loadRecoveryPoints()
    } catch (err) {
      showErrorToast(err, restoreFallbackMessage(err))
    } finally {
      setRestoringId(null)
    }
  }

  const handleDownload = async (confirmationToken: string) => {
    setDownloading(true)
    try {
      const { blob, filename } = await backupApi.downloadArchive(confirmationToken)
      // The server names the file after the cabinet and the clinic-local day; inventing one here gave every
      // cabinet's archive the same name, so two files in one Downloads folder differed only by the browser's
      // `(1)`. The local name stays as a fall-back for a response that carries no disposition header.
      // `downloadBlob` is the one way a file leaves this app: it covers the native shell bridge and the iOS
      // Safari `blob:` case a hand-rolled `<a download>` silently fails on.
      await downloadBlob(blob, filename || `archive-cabinet-${todayLocalIso()}.zip`)
      toast.success("Archive téléchargée", {
        description: "Conservez-la en lieu sûr : elle contient tout le dossier médical du cabinet.",
      })
    } catch (err) {
      showErrorToast(err, "L'archive n'a pas pu être téléchargée.")
    } finally {
      setDownloading(false)
    }
  }

  const handlePick = (event: React.ChangeEvent<HTMLInputElement>) => {
    const picked = event.target.files?.[0] ?? null

    // The value is cleared BEFORE anything else runs: the element still holds the file otherwise, so re-picking
    // the same one after a failed restore fires no `change` event at all and the retry silently does nothing.
    event.target.value = ""

    if (picked) {
      setReport(null)
      setPending(picked)
    }
  }

  const handleRestore = async (confirmationToken: string) => {
    if (!pending) return

    setRestoring(true)
    try {
      const result = await backupApi.restoreArchive(pending, confirmationToken)
      setReport(result)
      setPending(null)

      toast.success("Restauration terminée", {
        description: `${result.totalRestored} enregistrement(s) remis en place.`,
      })
    } catch (err) {
      // The dialog stays open with the file still selected — a refusal names what is wrong with the archive, and
      // closing would make the reader pick it again to read the sentence a second time. That promise is only
      // true because every dismissal channel is blocked while `restoring` (see the Dialog below).
      showErrorToast(err, restoreFallbackMessage(err))
    } finally {
      setRestoring(false)
    }
  }

  return (
    <div className="space-y-3">
      {/* The server-kept copies come FIRST, and that ordering is the point: « j'ai supprimé une fiche » is the common
          emergency and it is two clicks away here, while the archive below is the answer to the rarer, worse one. */}
      <RecoveryPointsList
        points={recovery?.points ?? []}
        loading={recoveryLoading}
        failed={recoveryFailed}
        onRetry={() => void loadRecoveryPoints()}
        restoringId={restoringId}
        onRestore={setPendingPoint}
        lastArchiveDownloadedAtUtc={recovery?.lastArchiveDownloadedAtUtc ?? null}
        archiveStaleAfterDays={recovery?.archiveStaleAfterDays ?? 30}
        retentionCount={recovery?.retentionCount ?? 7}
      />

      <div className="space-y-3 rounded-lg border p-3">
      <div className="min-w-0">
        <p className="flex items-center gap-1.5 text-xs font-medium">
          <Archive className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          Archive du cabinet
        </p>
        {/* `text-xs`, not `text-2xs`: 11 px is the floor for a badge or a `<dt>`, not for four lines of prose
            that reflow to five or six words a line at 200 % zoom in a 284 px box. */}
        <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">
          Un fichier unique contenant les dossiers, les documents et la comptabilité de votre cabinet — et rien
          d&apos;un autre cabinet. Téléchargez-le régulièrement sur votre propre poste. Pour le remettre en place,
          seuls les enregistrements manquants sont réinsérés : rien n&apos;est écrasé.
        </p>
      </div>

      {/* `text-sm` here, larger than the paragraph above it: within one card the type size has to follow the
          stakes, and the neighbouring host-managed note is already `text-sm`. This is the whole mitigation for
          an unencrypted copy of every patient record, and it was the smallest text on the card. */}
      <div className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning-wash p-2.5">
        <ShieldAlert className="mt-0.5 size-4 shrink-0 text-warning-ink" aria-hidden="true" />
        <p className="text-sm leading-relaxed text-warning-ink">
          <span className="font-medium">Le fichier n&apos;est pas chiffré.</span> Il contient l&apos;intégralité
          des dossiers médicaux de vos patients. Rangez-le sur un support dont vous maîtrisez l&apos;accès.
        </p>
      </div>

      {/* ⚠️ Said on a coarse pointer and nowhere else, and it is a NOTE rather than a refusal (§ 0: no capability
          is removed by a layout decision). The archive is routinely a multi-gigabyte file and a phone browser
          will frequently fail to keep one — but « frequently » is not « always », nothing here can measure it in
          advance, and disabling the button would take a practice's own records away from the one device its
          owner has on them. Saying so before the tap is the whole mitigation; the alternative the spec names is
          a silent failure or a spinner left running. */}
      <p className="hidden text-xs leading-relaxed text-muted-foreground coarse:block">
        Téléchargez l&apos;archive depuis un ordinateur : le fichier peut peser plusieurs gigaoctets, et un
        téléphone interrompt souvent un téléchargement de cette taille.
      </p>

      {/* Stacked and full-width up to `sm:`, so at 320 px neither control is a half-width strip. `coarse:h-11`
          because `size="sm"` is 32 px, well under the 44 px floor on a finger. */}
      <div className="flex flex-col gap-2 sm:flex-row">
        <Button
          onClick={() => setStepUpFor(DOWNLOAD_ACTION)}
          size="sm"
          disabled={downloading}
          className="h-8 w-full text-xs coarse:h-11 sm:w-auto"
        >
          <ArrowDownToLine className="mr-1 size-3.5" aria-hidden="true" />
          {downloading ? "Préparation…" : "Télécharger l'archive"}
        </Button>

        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={restoring}
          onClick={() => fileInput.current?.click()}
          className="h-8 w-full text-xs coarse:h-11 sm:w-auto"
        >
          <ArrowUpFromLine className="mr-1 size-3.5" aria-hidden="true" />
          Restaurer une archive
        </Button>

        {/* `accept` mirrors what the server takes, and the input is driven by the button above so the control the
            user sees is the one that carries the touch floor. */}
        <input
          ref={fileInput}
          type="file"
          accept=".zip,application/zip"
          className="sr-only"
          onChange={handlePick}
          aria-label="Choisir une archive à restaurer"
        />
      </div>

      {/* ⚠️ Mounted unconditionally so the outcome arriving is a CHANGE inside a live region rather than the
          insertion of one already carrying text — which VoiceOver on iOS frequently does not announce at all.
          For a multi-minute operation the user has looked away from, that is the difference between hearing the
          result and hearing nothing. It also carries the in-flight line, so a long restore is visible from where
          the user actually is once the sheet is gone. */}
      <div role="status" aria-live="polite" className="empty:hidden">
        {restoring && (
          <p className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-2.5 text-xs text-muted-foreground">
            <Loader2 className="size-4 shrink-0 animate-spin" aria-hidden="true" />
            Restauration en cours… Ne fermez pas cette page.
          </p>
        )}
        {report && <RestoreReportPanel report={report} />}
      </div>

      {/* `mobile="bottom"` is DialogContent's default — a bottom sheet below `md:` (AC-10), sized in `dvh` by the
          primitive, so the confirm button stays reachable when the keyboard is up.

          ⚠️ Every dismissal channel is blocked while `restoring`, and the two footer buttons being disabled was
          not enough: outside tap, Escape and the primitive's own close button were the three controls still
          responding, on the operation whose own copy says « Ne fermez pas cette page ». A thumb brushing the
          scrim two minutes in made the sheet vanish with nothing behind it, so the user read the operation as
          cancelled and their next move genuinely abandoned a request the server was committing. */}
      <Dialog
        open={pending !== null}
        onOpenChange={(open) => {
          if (!open && !restoring) setPending(null)
        }}
      >
        <DialogContent
          className="md:max-w-lg"
          showCloseButton={!restoring}
          onEscapeKeyDown={(event) => {
            if (restoring) event.preventDefault()
          }}
          onInteractOutside={(event) => {
            if (restoring) event.preventDefault()
          }}
        >
          <DialogHeader>
            {/* The file name is untrusted and arbitrarily long, and `DialogTitle` is 18 px semibold with no wrap
                rule: one unbreakable token overflowed a 288 px sheet horizontally, taking the close button's
                gutter with it. The name moves into the description, where it wraps. */}
            <DialogTitle>Restaurer cette archive ?</DialogTitle>
            <DialogDescription>
              <span className="block font-medium text-foreground [overflow-wrap:anywhere]">{pending?.name}</span>
            </DialogDescription>
            <DialogDescription>
              Les enregistrements manquants seront remis en place avec leurs identifiants et leurs numéros
              d&apos;origine. Ceux qui existent déjà ne sont pas touchés, et ceux qui ont été modifiés depuis
              l&apos;archive sont ignorés — rien de votre travail récent ne sera écrasé.
            </DialogDescription>
          </DialogHeader>

          <div className="flex items-start gap-2 rounded-lg border border-border bg-muted/40 p-2.5">
            <TriangleAlert className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            <p className="text-xs leading-relaxed text-muted-foreground">
              L&apos;opération peut durer plusieurs minutes sur un cabinet complet. Ne fermez pas cette page.
            </p>
          </div>

          {/* `w-full`/`sm:w-auto` are deleted: `DialogFooter` already owns that decision, and at the `md:` hinge
              its docstring names as the one everything keys on — a `sm:` override from a call site is an
              equal-specificity rule in a different variant, so which wins is not determinable by inspection. */}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setPending(null)}
              disabled={restoring}
              className="coarse:h-11"
            >
              Annuler
            </Button>
            <Button
              type="button"
              onClick={() => setStepUpFor(RESTORE_ACTION)}
              disabled={restoring}
              className="coarse:h-11"
            >
              {restoring ? "Restauration en cours…" : "Restaurer"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* FR-4.3 — the password (or a current code) immediately before either action. One dialog, two actions:
          the token it mints names the action it was minted for, so neither can authorise the other. */}
      <StepUpDialog
        open={stepUpFor !== null}
        onOpenChange={(open) => {
          if (!open) setStepUpFor(null)
        }}
        action={stepUpFor ?? DOWNLOAD_ACTION}
        purpose={
          stepUpFor === RESTORE_ACTION
            ? "Vous allez remettre en place les enregistrements manquants de ce cabinet."
            : "Vous allez télécharger l'intégralité du dossier médical de ce cabinet dans un fichier non chiffré."
        }
        hasTotp={totpKnown ? hasTotp : true}
        onConfirmed={(confirmationToken) => {
          const action = stepUpFor
          setStepUpFor(null)
          if (action === RESTORE_ACTION) {
            // One step-up action serves both restores — they are the same operation on the same records — so which
            // one is running is decided by whether a recovery point is pending, never by a second action name.
            if (pendingPoint) {
              void handleRestorePoint(confirmationToken)
            } else {
              void handleRestore(confirmationToken)
            }
          } else {
            void handleDownload(confirmationToken)
          }
        }}
      />

      {/* Restoring from a stored point gets its own confirmation, and it names the moment it will go back to.
          « Êtes-vous sûr ? » cannot say which of seven copies is about to be applied. */}
      <Dialog
        open={pendingPoint !== null}
        onOpenChange={(open) => {
          if (!open && restoringId === null) setPendingPoint(null)
        }}
      >
        <DialogContent className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>Restaurer depuis ce point ?</DialogTitle>
            <DialogDescription>
              <span className="block font-medium text-foreground">
                {pendingPoint ? formatDateTime(pendingPoint.startedAt) : ""}
              </span>
            </DialogDescription>
            <DialogDescription>
              Les enregistrements manquants seront remis en place avec leurs identifiants et leurs numéros
              d&apos;origine. Ceux qui existent déjà ne sont pas touchés, et ceux qui ont été modifiés depuis sont
              ignorés — rien de votre travail plus récent ne sera écrasé.
            </DialogDescription>
          </DialogHeader>

          {/* Said before the click, not discovered after it: a scheduled point carries no files, so a deleted
              radiograph does not come back this way. The archive below is what does. */}
          {pendingPoint && !pendingPoint.carriesFiles && (
            <div className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning-wash p-2.5">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning-ink" aria-hidden="true" />
              <p className="text-xs leading-relaxed text-warning-ink">
                Ce point contient les <span className="font-medium">enregistrements</span> mais pas les fichiers
                (radiographies, documents scannés). Les fiches, dossiers et documents supprimés reviendront ; les
                images qu&apos;ils portaient, non.
              </p>
            </div>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setPendingPoint(null)}
              className="coarse:h-11"
            >
              Annuler
            </Button>
            <Button
              type="button"
              onClick={() => setStepUpFor(RESTORE_ACTION)}
              className="coarse:h-11"
            >
              Restaurer
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      </div>
    </div>
  )
}

/**
 * The fall-back sentence a refusal gets when the server sent none — which is what the code set is for.
 *
 * ⚠️ The codes had **zero consumers** for the whole life of the feature, while their own doc comment instructed
 * the reader to branch on them and never on the message; both `catch` blocks passed one generic fallback, so a
 * schema mismatch, an archive belonging to another cabinet and a dropped connection were presented identically.
 * They are different next steps: one file will never work, another is the wrong file, the third is worth
 * retrying. The server's own French sentence still wins wherever it sent one — this only replaces the fallback.
 */
function restoreFallbackMessage(err: unknown): string {
  const code = err instanceof ApiError ? err.code : null

  switch (code) {
    case ARCHIVE_ERROR_CODES.schemaUnsupported:
      return "Cette archive a été créée par une autre version de l'application. Ce fichier ne peut pas être restauré ici."
    case ARCHIVE_ERROR_CODES.clinicMismatch:
      return "Cette archive n'appartient pas à votre cabinet. Vérifiez le fichier choisi."
    case ARCHIVE_ERROR_CODES.invalid:
      return "Cette archive n'a pas pu être lue. Vérifiez que le téléchargement s'est terminé."
    default:
      return "La restauration a échoué."
  }
}

/**
 * The per-entity outcome, as a **card list** — never a table (AC-10).
 *
 * ⚠️ The three counts are kept apart on purpose. « Déjà présent » is what makes a second restore visibly a no-op,
 * and « ignoré » is what proves nothing was overwritten; collapsing them into one total would hide exactly the
 * two facts an anxious owner is looking for.
 */
function RestoreReportPanel({ report }: { report: ClinicArchiveRestoreReport }) {
  // Sorted on the LABEL, not the key: `localeCompare(…, "fr")` over English CLR identifiers orders by nothing
  // the reader can predict. An entity the server could not name keeps its own name rather than a placeholder.
  const label = (entity: string) => report.entityLabels?.[entity] ?? entity

  const entities = Array.from(
    new Set([
      ...Object.keys(report.restored),
      ...Object.keys(report.alreadyPresent),
      ...Object.keys(report.conflicts),
    ]),
  ).sort((a, b) => label(a).localeCompare(label(b), "fr"))

  // ⚠️ Tone follows the outcome, not the fact that the call returned. A restore that skipped three conflicting
  // rows and carries four « ne fait pas partie des données que cette version sait restaurer » warnings painted
  // solid green with the amber lines nested inside it at 11 px — and on a coarse pointer, scanning colour is
  // what a reader does, so « tout est revenu » and « il en manque » read identically.
  const qualified = report.totalConflicts > 0 || report.warnings.length > 0

  return (
    <div
      className={
        qualified
          ? "space-y-2 rounded-lg border border-warning/40 bg-warning-wash p-3"
          : "space-y-2 rounded-lg border border-success/25 bg-success-wash p-3"
      }
    >
      <p className={`text-xs font-medium ${qualified ? "text-warning-ink" : "text-success"}`}>
        Archive du {formatDateTime(report.archivedAtUtc)}
        {qualified ? " restaurée avec des réserves" : " restaurée"}
      </p>
      <p className={`text-2xs ${qualified ? "text-warning-ink" : "text-success"}`}>
        {report.totalRestored} remis en place · {report.totalAlreadyPresent} déjà présents ·{" "}
        {report.totalConflicts} ignorés (modifiés depuis) · {report.blobsRestored} fichiers
      </p>

      {entities.length > 0 && (
        <ul className="divide-y divide-border/60 rounded-lg border border-border/60 bg-background/60">
          {/* The name takes a line of its own. As a `flex-1` sibling its flex base size is 0, so it contributed
              nothing to line-breaking: the counts laid out first at content width and the name grew into the
              ~12 px left over, where `truncate` rendered « … ». At 320 px the row is 168 px wide, and there was
              no `title` and no tap-to-reveal — the identity half of « 3 conflits sur Patient » was unreachable
              by any means. */}
          {entities.map((entity) => (
            <li key={entity} className="space-y-0.5 p-2">
              <span className="block text-xs font-medium [overflow-wrap:anywhere]">{label(entity)}</span>
              <span className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                {(report.restored[entity] ?? 0) > 0 && (
                  <span className="text-2xs tabular-nums text-success">
                    {report.restored[entity]} remis
                  </span>
                )}
                {(report.alreadyPresent[entity] ?? 0) > 0 && (
                  <span className="text-2xs tabular-nums text-muted-foreground">
                    {report.alreadyPresent[entity]} déjà présents
                  </span>
                )}
                {(report.conflicts[entity] ?? 0) > 0 && (
                  <span className="text-2xs tabular-nums text-warning-ink">
                    {report.conflicts[entity]} ignorés
                  </span>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}

      {report.warnings.length > 0 && (
        <ul className="space-y-0.5">
          {report.warnings.map((warning) => (
            <li key={warning} className="text-xs text-warning-ink [overflow-wrap:anywhere]">
              {warning}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
