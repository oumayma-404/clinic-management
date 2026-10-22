import type { PlatformBackupHealth, PlatformNightlyBackup, PlatformWalArchive } from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * « Sauvegarde hors serveur » above the portfolio (`server-loss-recovery` Part 3).
 *
 * ⚠️ **Three renderings, and the quiet one is the healthy one.** A copy that is late or failing is an alert box
 * read before anything else on the page; a healthy pair is one muted line, so the box only ever means something.
 * The nightly copy failed for 33 nights on the live server before this existed, with nothing on any screen.
 *
 * ⚠️ **« Illisible » is its own state**, never folded into « rien à signaler »: a strip that silently disappears when
 * its read fails would look exactly like a deployment that watches no backup.
 */
export function BackupHealthStrip({ health }: { health: PlatformBackupHealth | null | "unreadable" }) {
  if (health === null) {
    return null;
  }

  if (health === "unreadable") {
    return (
      <section className="rounded-lg border border-destructive/40 bg-card p-4" role="alert">
        <h2 className="text-sm font-semibold">État des sauvegardes hors serveur illisible</h2>
        <p className="mt-1 text-sm text-muted-foreground">Rien ne permet de dire qu&apos;elles sont à jour.</p>
      </section>
    );
  }

  if (health.verdict === "Ok") {
    return (
      <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-muted-foreground" role="status">
        <span className="font-medium text-foreground">Sauvegardes hors serveur à jour</span>
        {/* Two items rather than one « · »-joined sentence, so a narrow screen wraps between them, never mid-way. */}
        <span>Copie nocturne {formatDateTime(health.nightly.lastSuccessAt)}</span>
        <span>Copie continue {formatDateTime(health.walArchive.lastArchivedAt)}</span>
      </p>
    );
  }

  return (
    <section
      aria-labelledby="backup-health-heading"
      className="rounded-lg border border-destructive/40 bg-card p-4"
      role="alert"
    >
      <h2 id="backup-health-heading" className="text-base font-semibold text-destructive">
        Sauvegarde hors serveur {health.verdictLabel.toLowerCase()}
      </h2>
      <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
        <CopyRow title="Copie nocturne" subtitle="base, fichiers, clés" verdict={health.nightly} detail={nightlyDetail(health.nightly)} />
        <CopyRow title="Copie continue" subtitle="base, à 5 min près" verdict={health.walArchive} detail={walDetail(health.walArchive)} />
      </dl>
    </section>
  );
}

function CopyRow({
  title,
  subtitle,
  verdict,
  detail,
}: {
  title: string;
  subtitle: string;
  verdict: PlatformNightlyBackup | PlatformWalArchive;
  detail: string;
}) {
  return (
    <div className="min-w-0 rounded-md border border-border p-3">
      <dt className="flex flex-wrap items-baseline justify-between gap-x-2">
        <span className="font-medium">
          {title} <span className="font-normal text-muted-foreground">({subtitle})</span>
        </span>
        <span className={cn("font-medium", verdict.verdict !== "Ok" && "text-destructive")}>{verdict.verdictLabel}</span>
      </dt>
      <dd className="mt-1 break-words text-muted-foreground">{detail}</dd>
    </div>
  );
}

function nightlyDetail(nightly: PlatformNightlyBackup): string {
  if (nightly.outcome === null) {
    return "Aucune exécution enregistrée.";
  }
  const last = nightly.lastSuccessAt
    ? `dernière réussite le ${formatDateTime(nightly.lastSuccessAt)}`
    : "aucune réussite enregistrée";
  return nightly.failedStage ? `Échec à l'étape « ${nightly.failedStage} », ${last}.` : `${capitalize(last)}.`;
}

function walDetail(wal: PlatformWalArchive): string {
  if (!wal.lastArchivedAt) {
    return "Aucun envoi enregistré.";
  }
  const failing =
    wal.lastFailedAt && new Date(wal.lastFailedAt).getTime() > new Date(wal.lastArchivedAt).getTime()
      ? `, en échec depuis le ${formatDateTime(wal.lastFailedAt)}`
      : "";
  return `Dernier envoi le ${formatDateTime(wal.lastArchivedAt)}${failing}.`;
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}
