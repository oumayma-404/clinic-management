import Link from "next/link";
import { redirect } from "next/navigation";

import { ActivityTrend } from "@/components/activity-trend";
import { CancelPeriodDialog } from "@/components/cancel-period-dialog";
import { MessagingSection } from "@/components/messaging-section";
import { RecordPaymentSheet } from "@/components/record-payment-sheet";
import { SuspendDialog } from "@/components/suspend-dialog";
import { ConsoleApiError } from "@/lib/api/client";
import { CLINIC_NOT_FOUND_CODE, fetchClinicDetail, type PlatformClinicDetail } from "@/lib/api/platform";
import { EM_DASH, formatCount, formatDate, formatDateTime, formatFreshness, formatMoney } from "@/lib/format";
import { readSessionToken } from "@/lib/session";

/**
 * One cabinet, opened (`platform-console` US-3).
 *
 * ⚠️ **A server component, like the portfolio**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so every read happens here.
 *
 * ⚠️ **Opening this page writes one row to the console's access ledger** (AC-7.3) — loading the list writes none
 * (AC-3.5). That is a property of the endpoint, not of this file; it is stated here because a reader arriving at
 * the screen should know the read is recorded.
 *
 * ⚠️ **Single column up to `lg:`, two above it.** A tablet in portrait is past `md:` and would get two columns on
 * a 768 px screen, which is the same boundary the portfolio's table uses and for the same reason.
 */
export const dynamic = "force-dynamic";

interface PageProps {
  params: Promise<{ clinicId: string }>;
}

export default async function CabinetDetailPage({ params }: PageProps) {
  const token = await readSessionToken();
  if (!token) {
    redirect("/login");
  }

  const { clinicId } = await params;

  let detail: PlatformClinicDetail;
  try {
    detail = await fetchClinicDetail(token, clinicId);
  } catch (error) {
    // EC-13 and EC-12 are different states and must not share a screen: a cabinet that no longer exists is a
    // normal outcome with a way back, while an unreadable one is a failure that must not read as an empty fiche.
    if (error instanceof ConsoleApiError && error.code === CLINIC_NOT_FOUND_CODE) {
      return <VanishedCabinet message={error.message} />;
    }
    return <ReadFailure error={error} />;
  }

  const clinic = detail.clinic;
  const measured = clinic.countersComputedAt !== null;

  return (
    <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:py-8">
      <BackLink />

      <header className="mt-4 space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">{clinic.name}</h1>
        <p className="text-sm text-muted-foreground">
          {clinic.city ?? "Ville non renseignée"} · créé le {formatDate(clinic.createdAt)}
        </p>
      </header>

      {/* AC-3.4 / AC-7.4, said out loud on the one screen where a reader might expect a patient list. */}
      <p className="mt-4 rounded-lg border border-border bg-card p-4 text-sm text-muted-foreground" role="note">
        Cette fiche ne contient aucune donnée de patient : uniquement des comptes, des dates, des nombres et le
        total encaissé par le cabinet lui-même. Chaque ouverture de cette fiche est inscrite au journal des accès.
      </p>

      <div className="mt-6 space-y-6">
        <Subscription detail={detail} />

        <Suspension detail={detail} />

        {/* Absent, not present-and-empty, where the deployment does not sell vendor messaging (EC-16) — the server
            sends null and no heading renders at all. */}
        {detail.messaging ? (
          <MessagingSection
            messaging={detail.messaging}
            clinicId={clinic.clinicId}
            clinicName={clinic.name}
          />
        ) : null}

        <section aria-labelledby="activity-heading" className="rounded-lg border border-border bg-card p-4">
          <h2 id="activity-heading" className="text-base font-semibold">
            Activité
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {measured
              ? `Compteurs mesurés ${formatFreshness(clinic.countersComputedAt)} (le ${formatDateTime(clinic.countersComputedAt)}).`
              : "Les compteurs d'activité n'ont jamais couvert ce cabinet : les chiffres ci-dessous sont indisponibles, ce qui n'est pas la même chose que « aucune activité »."}
          </p>

          {/* One column at 320 px, two from 380 px, three above `lg:` — the same ladder the card list uses, so a
              figure never shares a line with a label it does not belong to. */}
          <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-3 min-[380px]:grid-cols-2 lg:grid-cols-3">
            <Figure label="Patients" value={measured ? formatCount(clinic.patients) : EM_DASH} />
            <Figure label="Comptes du cabinet" value={measured ? formatCount(clinic.users) : EM_DASH} />
            <Figure label="RDV pris (30 j)" value={measured ? formatCount(clinic.appointments30d) : EM_DASH} />
            <Figure label="Enregistrements (7 j)" value={measured ? formatCount(clinic.writes7d) : EM_DASH} />
            <Figure label="Enregistrements (30 j)" value={measured ? formatCount(clinic.writes30d) : EM_DASH} />
            <Figure label="Jours actifs (30 j)" value={measured ? formatCount(clinic.activeDays30d) : EM_DASH} />
            <Figure label="Dernier enregistrement" value={formatDateTime(clinic.lastWriteAt)} />
            <Figure label="Dernière connexion" value={formatDateTime(clinic.lastLoginAt)} />
            <Figure
              // AC-2.7: the CABINET's own turnover, and the label says whose. The vendor's revenue is a different
              // figure with a different name and never appears on this screen.
              label="Encaissé ce mois par le cabinet"
              value={measured ? formatMoney(clinic.clinicCollectedThisMonthDt) : EM_DASH}
            />
          </dl>
        </section>

        <ActivityTrend trend={detail.trend} />

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Administrator detail={detail} />
          <ClinicJournalLink clinicId={clinic.clinicId} name={clinic.name} />
        </div>
      </div>
    </main>
  );
}

/**
 * The entitlement, and the ledger behind it (AC-3.1, AC-3.2, US-4).
 *
 * ⚠️ **Every cancelled entry stays listed, struck through and marked in WORDS as well.** A strike-through alone is
 * invisible to a screen reader and to anyone reading a printout, and AC-6.3's « never colour alone » is the same
 * rule one field over. An entry is never edited and never deleted (AC-5.2), so a history that hid them would answer
 * « what were we paid, and for what? » with a tidied version of the truth.
 *
 * ⚠️ **« Sans échéance » is said in words** (EC-14) rather than left as a blank date — a cabinet that never expires
 * is a deliberate arrangement, and an empty cell reads as « nous ne savons pas ».
 */
function Subscription({ detail }: { detail: PlatformClinicDetail }) {
  const clinic = detail.clinic;

  return (
    <section aria-labelledby="subscription-heading" className="rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <h2 id="subscription-heading" className="text-base font-semibold">
          Abonnement et paiements
        </h2>
        <RecordPaymentSheet clinicId={clinic.clinicId} clinicName={clinic.name} endsOn={clinic.endsOn} />
      </div>

      <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-3 min-[380px]:grid-cols-2 lg:grid-cols-4">
        <Figure label="État" value={clinic.stateLabel} />
        <Figure label="Forfait" value={clinic.planLabel ?? "Non choisi"} />
        <Figure label="Fin de couverture" value={clinic.endsOn ? formatDate(clinic.endsOn) : "Sans échéance"} />
        <Figure
          label="Jours restants"
          value={clinic.daysRemaining === null ? EM_DASH : formatCount(clinic.daysRemaining)}
        />
      </dl>

      <h3 className="mt-6 text-sm font-semibold">Historique des paiements</h3>
      {detail.payments.length === 0 ? (
        <p className="mt-1 text-sm text-muted-foreground">
          Aucune période enregistrée pour ce cabinet. Ce n&apos;est pas la même chose qu&apos;un cabinet qui
          n&apos;a jamais payé : c&apos;est un cabinet sans aucun droit d&apos;usage enregistré.
        </p>
      ) : (
        <ul className="mt-2 space-y-2">
          {detail.payments.map((entry) => (
            <li
              key={entry.entryId}
              className="rounded-md border border-border p-3 text-sm"
            >
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className={entry.isCancelled ? "font-medium line-through" : "font-medium"}>
                  {entry.kindLabel}
                  {entry.amountDt === null ? "" : ` · ${formatMoney(entry.amountDt)}`}
                  {entry.methodLabel ? ` · ${entry.methodLabel}` : ""}
                </span>
                <span className="text-xs text-muted-foreground">{formatDate(entry.recordedOn)}</span>
              </div>

              <p className="mt-1 text-xs text-muted-foreground">
                {entry.coversFrom
                  ? `Couvre du ${formatDate(entry.coversFrom)} ${entry.coversThrough ? `au ${formatDate(entry.coversThrough)}` : "— sans échéance"}`
                  : "Ne couvre aucune période"}
                {entry.reference ? ` · réf. ${entry.reference}` : ""}
              </p>

              {entry.isCancelled ? (
                // In words, not only struck through: a strike-through is invisible to a screen reader and to a
                // printout, and « annulé » is the whole meaning of the row.
                <p className="mt-1 text-xs text-destructive">
                  Annulé{entry.cancelledAt ? ` le ${formatDate(entry.cancelledAt)}` : ""}
                  {entry.cancelledBy ? ` par ${entry.cancelledBy}` : ""}
                  {entry.cancelReason ? ` — ${entry.cancelReason}` : ""}
                </p>
              ) : null}

              {entry.note ? <p className="mt-1 text-xs text-muted-foreground">{entry.note}</p> : null}

              {/* US-5. Offered on live entries only — a cancelled one has nothing left to cancel, and a control
                  that opens onto a refusal is the dead control the device contract forbids. Always visible at
                  every width, never revealed by hover. */}
              {entry.isCancelled ? null : (
                <div className="mt-2">
                  <CancelPeriodDialog clinicId={clinic.clinicId} clinicName={clinic.name} entry={entry} />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * Suspension (US-6) — deliberately **its own section, not a control inside « Abonnement et paiements »**.
 *
 * ⚠️ That placement *is* AC-6.3. Suspension is a decision about conduct, it consumes no paid day, and paying does not
 * lift it — so a « Suspendre » button sitting under the payment history would present it as a billing lever, which is
 * exactly what the AC forbids. The heading and the sentence below it say so out loud, because a reader who takes
 * suspension for a payment state will reach for a cancellation instead, and that one is not reversible.
 *
 * ⚠️ **Suspended and expired are distinguished by words and by structure, never by colour** — « Suspendu » is
 * written, the motif is quoted, and the section carries a border only as a second signal. A greyscale printout, a
 * screen reader and a colour-blind reader all get the same facts (AC-6.3).
 *
 * ⚠️ **The state comes from `clinic.state`, the server's own FR-1 verdict — not from `detail.suspension !== null`.**
 * The two agree today, and keeping one authority is what stops them disagreeing later: the trail explains a
 * suspension the state has declared, and never declares one itself.
 */
function Suspension({ detail }: { detail: PlatformClinicDetail }) {
  const clinic = detail.clinic;
  const suspended = clinic.state === "Suspended";

  return (
    <section aria-labelledby="suspension-heading" className="rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <h2 id="suspension-heading" className="text-base font-semibold">
          Suspension
        </h2>
        <SuspendDialog
          clinicId={clinic.clinicId}
          clinicName={clinic.name}
          suspended={suspended}
          endsOn={clinic.endsOn}
        />
      </div>

      <p className="mt-1 text-sm text-muted-foreground">
        Mesure indépendante du paiement : elle sert aux abus et aux fraudes, ne consomme aucun jour payé, et un
        paiement ne la lève pas.
      </p>

      {suspended ? (
        <div className="mt-4 rounded-md border border-destructive/40 p-3 text-sm">
          {/* In words before anything else: « Suspendu » is the whole meaning of this block, and a border colour is
              not a statement. */}
          <p className="font-medium">Ce cabinet est suspendu — il ne peut plus enregistrer de nouveaux actes.</p>
          {detail.suspension ? (
            <dl className="mt-3 space-y-3">
              <Figure label="Motif" value={detail.suspension.suspensionReason} />
              <Figure label="Suspendu le" value={formatDateTime(detail.suspension.suspendedAt)} />
              <Figure label="Par" value={detail.suspension.suspendedBy ?? EM_DASH} />
            </dl>
          ) : (
            // Unreachable through this console — the server refuses a suspension with no motif — so this states a
            // fault rather than a normal case, and never renders an empty « Motif » that would read as « aucun ».
            <p className="mt-2 text-muted-foreground">
              Aucun motif n&apos;est enregistré pour cette suspension. Elle est donc inexplicable au cabinet :
              levez-la et, si elle est justifiée, reprenez-la avec un motif.
            </p>
          )}
        </div>
      ) : (
        <p className="mt-4 text-sm text-muted-foreground">
          Ce cabinet n&apos;est pas suspendu. S&apos;il ne peut pas enregistrer de nouveaux actes, c&apos;est que sa
          couverture est arrivée à échéance — ce qui se corrige par un paiement, pas ici.
        </p>
      )}
    </section>
  );
}

/** AC-3.3 — who to call. Staff, never a patient, and the screen says which. */
function Administrator({ detail }: { detail: PlatformClinicDetail }) {
  const hasContact = detail.adminName !== null || detail.adminEmail !== null;

  return (
    <section aria-labelledby="admin-heading" className="rounded-lg border border-border bg-card p-4">
      <h2 id="admin-heading" className="text-base font-semibold">
        Administrateur du cabinet
      </h2>

      {hasContact ? (
        <dl className="mt-3 space-y-3">
          <Figure label="Nom" value={detail.adminName ?? EM_DASH} />
          <Figure
            label="Adresse e-mail"
            value={
              detail.adminEmail ? (
                // A real mailto: the vendor's next action after reading this is to write to them, and making them
                // retype an address off a screen is a capability removed for no reason.
                <a className="underline underline-offset-4" href={`mailto:${detail.adminEmail}`}>
                  {detail.adminEmail}
                </a>
              ) : (
                EM_DASH
              )
            }
          />
          {!detail.adminIsActive ? (
            <p className="text-sm text-destructive" role="note">
              Ce compte administrateur est désactivé : il ne peut plus se connecter. Ce n&apos;est pas la même chose
              qu&apos;un cabinet sans administrateur.
            </p>
          ) : null}
        </dl>
      ) : (
        <p className="mt-1 text-sm text-muted-foreground">
          Ce cabinet n&apos;a aucun compte administrateur. Personne ne peut y administrer les utilisateurs ni les
          paramètres.
        </p>
      )}
    </section>
  );
}

/** AC-7.3, from the other end: what has been done to *this* cabinet, filtered for it. */
function ClinicJournalLink({ clinicId, name }: { clinicId: string; name: string }) {
  return (
    <section aria-labelledby="journal-heading" className="rounded-lg border border-border bg-card p-4">
      <h2 id="journal-heading" className="text-base font-semibold">
        Journal des accès
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Chaque ouverture de cette fiche est enregistrée avec le compte console qui l&apos;a faite.
      </p>
      <Link
        href={`/journal?clinicId=${clinicId}`}
        className="mt-3 inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm hover:bg-muted/50"
      >
        Voir le journal de {name}
      </Link>
    </section>
  );
}

function Figure({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm tabular-nums">{value}</dd>
    </div>
  );
}

function BackLink() {
  return (
    <Link
      href="/cabinets"
      className="inline-flex min-h-11 items-center text-sm text-muted-foreground underline underline-offset-4"
    >
      ← Retour au portefeuille
    </Link>
  );
}

/**
 * EC-13 — the cabinet was deleted since the list was drawn. A French state with a way back, not an error page and
 * not an empty fiche whose zeros would read as a practice that has never done anything.
 */
function VanishedCabinet({ message }: { message: string }) {
  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-border bg-card p-6" role="status">
        <h1 className="text-lg font-semibold">Ce cabinet n&apos;existe plus</h1>
        <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        <div className="mt-4">
          <BackLink />
        </div>
      </div>
    </main>
  );
}

/** EC-12 — « je n'ai pas pu lire », which must never look like a cabinet with nothing in it. */
function ReadFailure({ error }: { error: unknown }) {
  const message =
    error instanceof ConsoleApiError
      ? error.message
      : "Une erreur inattendue est survenue pendant la lecture de la fiche.";

  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <h1 className="text-lg font-semibold">Fiche illisible</h1>
        <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        <p className="mt-2 text-sm text-muted-foreground">
          Ceci n&apos;est <strong>pas</strong> un cabinet sans activité : la fiche n&apos;a pas pu être lue.
        </p>
        <div className="mt-4">
          <BackLink />
        </div>
      </div>
    </main>
  );
}
