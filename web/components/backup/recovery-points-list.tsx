"use client"

import { History, Loader2, RotateCcw, ShieldCheck, TriangleAlert } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import type { RecoveryPointDto } from "@/lib/api/backup"
import { formatDateTime, formatFileSize } from "@/lib/format"

interface RecoveryPointsListProps {
  points: RecoveryPointDto[]
  loading: boolean
  failed: boolean
  onRetry: () => void
  /** Null while none is in flight; otherwise the id being restored, so only that row shows a spinner. */
  restoringId: string | null
  onRestore: (point: RecoveryPointDto) => void
  lastArchiveDownloadedAtUtc: string | null
  archiveStaleAfterDays: number
  retentionCount: number
}

/**
 * « Points de restauration » — the copies the server keeps of this cabinet's records, and the one action that puts
 * one back (`clinic-recovery-points`).
 *
 * <p>⚠️ **A card list at every width, not a table.** Six figures per row over ~284 px inside the Sauvegarde card is
 * not a table that can compress — and unlike the lists § 6 governs, this one has no desktop density to preserve: it
 * is normally seven rows read once, in an emergency, by somebody deciding which moment to go back to.</p>
 *
 * <p>⚠️ **It states what a point does NOT carry.** A scheduled point is rows-only, so restoring one brings back a
 * deleted patient, fiche or document row and no radiograph. Left unsaid, an owner reads « restauré » and finds the
 * image gone — so the row says « lignes seulement » and the footer says where the files are.</p>
 *
 * <p>⚠️ **Loading, empty and failed are three states.** A failed read rendered as « aucun point » is a confidently
 * wrong answer on the screen somebody opens *because* they have lost data (§ 13).</p>
 */
export function RecoveryPointsList({
  points,
  loading,
  failed,
  onRetry,
  restoringId,
  onRestore,
  lastArchiveDownloadedAtUtc,
  archiveStaleAfterDays,
  retentionCount,
}: RecoveryPointsListProps) {
  const busy = restoringId !== null

  return (
    <div className="space-y-3 rounded-lg border p-3">
      <div className="min-w-0">
        <p className="flex items-center gap-1.5 text-xs font-medium">
          <History className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          Points de restauration
        </p>
        <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">
          Le serveur conserve automatiquement les {retentionCount} dernières copies des enregistrements de votre
          cabinet. Si un dossier, une fiche ou un document a été supprimé par erreur, remettez-le en place depuis la
          copie qui le précède : rien de votre travail plus récent n&apos;est écrasé.
        </p>
      </div>

      <ArchiveCopyNotice
        lastArchiveDownloadedAtUtc={lastArchiveDownloadedAtUtc}
        archiveStaleAfterDays={archiveStaleAfterDays}
      />

      {/* Mounted unconditionally so an outcome arriving is a CHANGE inside a live region rather than the insertion of
          one already carrying text — which VoiceOver frequently does not announce at all. */}
      <div role="status" aria-live="polite" className="empty:hidden">
        {busy && (
          <p className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-2.5 text-xs text-muted-foreground">
            <Loader2 className="size-4 shrink-0 animate-spin" aria-hidden="true" />
            Restauration en cours… Ne fermez pas cette page.
          </p>
        )}
      </div>

      {loading && <RecoveryPointsSkeleton />}

      {!loading && failed && (
        <div className="space-y-2 rounded-lg border border-warning/40 bg-warning-wash p-2.5">
          <p className="text-xs leading-relaxed text-warning-ink">
            La liste des points de restauration n&apos;a pas pu être chargée. Vos données ne sont pas affectées.
          </p>
          <Button type="button" variant="outline" size="sm" onClick={onRetry} className="h-8 text-xs coarse:h-11">
            Réessayer
          </Button>
        </div>
      )}

      {!loading && !failed && points.length === 0 && (
        <EmptyState
          size="compact"
          icon={History}
          title="Aucun point de restauration pour l'instant"
          description="Le premier est créé automatiquement cette nuit. En attendant, téléchargez l'archive du cabinet ci-dessous pour en garder une copie."
        />
      )}

      {!loading && !failed && points.length > 0 && (
        <ul className="divide-y divide-border/60 rounded-lg border border-border/60 bg-background/60">
          {points.map((point) => (
            <RecoveryPointRow
              key={point.id}
              point={point}
              disabled={busy}
              restoring={restoringId === point.id}
              onRestore={onRestore}
            />
          ))}
        </ul>
      )}
    </div>
  )
}

/**
 * The off-server copy's own state, said here rather than only in the bell.
 *
 * ⚠️ It is the load-bearing sentence of this whole card: a reader who has just seen seven healthy recovery points will
 * reasonably conclude they are covered, and they are not — these live on the same server. The threshold comes from the
 * server so this and the `ArchiveStale` notification cannot disagree.
 */
function ArchiveCopyNotice({
  lastArchiveDownloadedAtUtc,
  archiveStaleAfterDays,
}: {
  lastArchiveDownloadedAtUtc: string | null
  archiveStaleAfterDays: number
}) {
  const stale =
    lastArchiveDownloadedAtUtc === null ||
    Date.now() - new Date(lastArchiveDownloadedAtUtc).getTime() > archiveStaleAfterDays * 86_400_000

  const Icon = stale ? TriangleAlert : ShieldCheck

  return (
    <div
      className={
        stale
          ? "flex items-start gap-2 rounded-lg border border-warning/40 bg-warning-wash p-2.5"
          : "flex items-start gap-2 rounded-lg border border-success/25 bg-success-wash p-2.5"
      }
    >
      <Icon
        className={`mt-0.5 size-4 shrink-0 ${stale ? "text-warning-ink" : "text-success"}`}
        aria-hidden="true"
      />
      <p className={`text-xs leading-relaxed ${stale ? "text-warning-ink" : "text-success"}`}>
        {lastArchiveDownloadedAtUtc === null ? (
          <>
            <span className="font-medium">Aucune archive n&apos;a encore été téléchargée.</span> Ces points de
            restauration sont conservés sur le serveur : ils ne protègent pas d&apos;une panne du serveur lui-même.
            Téléchargez l&apos;archive ci-dessous sur votre propre poste.
          </>
        ) : (
          <>
            Dernière archive téléchargée le{" "}
            <span className="font-medium">{formatDateTime(lastArchiveDownloadedAtUtc)}</span>.
            {stale
              ? " Elle commence à dater : une copie gardée sur votre poste est la seule qui survive à une panne du serveur."
              : " Une copie est bien conservée hors du serveur."}
          </>
        )}
      </p>
    </div>
  )
}

function RecoveryPointRow({
  point,
  disabled,
  restoring,
  onRestore,
}: {
  point: RecoveryPointDto
  disabled: boolean
  restoring: boolean
  onRestore: (point: RecoveryPointDto) => void
}) {
  return (
    <li className="space-y-1 p-2.5">
      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <span className="text-xs font-medium">{formatDateTime(point.startedAt)}</span>
        <OutcomeBadge point={point} />
      </div>

      {point.isRestorable && (
        <p className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-2xs text-muted-foreground">
          {/* The row count leads: « 3 tables » on a cabinet with forty is a detectable disaster that a size cannot
              express, and it is what tells an anxious owner this copy is whole. */}
          <span className="tabular-nums">{point.rowCount ?? 0} enregistrements</span>
          <span className="tabular-nums">{point.tableCount ?? 0} tables</span>
          {point.sizeBytes !== null && (
            <span className="tabular-nums">{formatFileSize(point.sizeBytes)}</span>
          )}
          {/* Stated, never inferred from a zero — see the file's own note. */}
          <span>{point.carriesFiles ? "avec les fichiers" : "lignes seulement (sans les fichiers)"}</span>
        </p>
      )}

      {point.error !== null && (
        <p className="text-2xs leading-relaxed text-warning-ink [overflow-wrap:anywhere]">{point.error}</p>
      )}

      {point.isRestorable && (
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => onRestore(point)}
          className="h-8 w-full text-xs coarse:h-11 sm:w-auto"
        >
          {restoring ? (
            <Loader2 className="mr-1 size-3.5 animate-spin" aria-hidden="true" />
          ) : (
            <RotateCcw className="mr-1 size-3.5" aria-hidden="true" />
          )}
          {restoring ? "Restauration…" : "Restaurer depuis ce point"}
        </Button>
      )}
    </li>
  )
}

/**
 * ⚠️ The outcome is stated **in words**, not only as a colour: a greyscale printout, a poor display at 200 % zoom and
 * a screen reader must all get the same fact — which copy is usable.
 */
function OutcomeBadge({ point }: { point: RecoveryPointDto }) {
  if (point.isRestorable) {
    return (
      <span className="rounded-full bg-success-wash px-1.5 py-0.5 text-2xs font-medium text-success">
        Utilisable
      </span>
    )
  }

  // Running and Failed are one badge on purpose: to the reader they are the same fact — there is nothing behind this
  // row to restore from. The error line beneath says which it was.
  return (
    <span className="rounded-full bg-warning-wash px-1.5 py-0.5 text-2xs font-medium text-warning-ink">
      {point.outcome === "Running" ? "Interrompu" : "Échec"}
    </span>
  )
}

function RecoveryPointsSkeleton() {
  return (
    <div className="space-y-2" aria-hidden="true">
      {[0, 1, 2].map((i) => (
        <div key={i} className="space-y-1.5 rounded-lg border border-border/60 p-2.5">
          <div className="h-3 w-32 animate-pulse rounded bg-muted" />
          <div className="h-2.5 w-48 animate-pulse rounded bg-muted" />
        </div>
      ))}
    </div>
  )
}
