import { CancelAllowanceDialog } from "@/components/cancel-allowance-dialog";
import { RecordAllowanceSheet } from "@/components/record-allowance-sheet";
import type { PlatformMessaging } from "@/lib/api/platform";
import { EM_DASH, formatCount, formatDate, formatMoney } from "@/lib/format";

/**
 * « Messagerie » — a cabinet's WhatsApp reminder forfait (`vendor-whatsapp-messaging-quota` AC-8.1).
 *
 * ⚠️ **Its own section, not a block inside « Abonnement et paiements »**, and that placement is a decision rather than
 * layout. A forfait de rappels is not an entitlement: it is a metered consumable the vendor buys from Meta and resells,
 * it expires monthly rather than by date, and cancelling one holds a practice's reminders **immediately** where
 * cancelling a subscription period moves a date. Presenting the two together invites reaching for the wrong control,
 * which is the argument the suspension section already makes one heading over.
 *
 * ⚠️ **« Non mesuré » is never rendered as 0** (AC-8.3). A cabinet with no counting row gets an em dash and a sentence
 * saying the counter has not covered it — a fault on *our* side — because « 0 rappel envoyé » is a claim about the
 * practice and the vendor's action for the two is completely different.
 *
 * ⚠️ **The section is absent entirely where the deployment does not sell vendor messaging** (EC-16): the caller passes
 * `null` and nothing renders — no heading, no zeros, no disabled button.
 */
export function MessagingSection({
  messaging,
  clinicId,
  clinicName,
}: {
  messaging: PlatformMessaging;
  clinicId: string;
  clinicName: string;
}) {
  return (
    <section aria-labelledby="messaging-heading" className="rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="messaging-heading" className="text-base font-semibold">
            Forfait de rappels WhatsApp
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">{messaging.monthLabel}</p>
        </div>
        <RecordAllowanceSheet
          clinicId={clinicId}
          clinicName={clinicName}
          monthLabel={messaging.monthLabel}
          standingAllowance={messaging.standingAllowance}
        />
      </div>

      {/* One column at 320 px, two from 380 px, four above `lg:` — the ladder every other figure grid here uses, so a
          number never shares a line with a label it does not belong to. */}
      <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-3 min-[380px]:grid-cols-2 lg:grid-cols-4">
        <Figure
          label="Forfait mensuel"
          value={messaging.standingAllowance === null ? "Aucun" : formatCount(messaging.standingAllowance)}
        />
        <Figure
          label="Ce mois-ci"
          value={messaging.measured ? formatCount(messaging.allowance) : EM_DASH}
        />
        <Figure label="Envoyés" value={messaging.measured ? formatCount(messaging.consumed) : EM_DASH} />
        <Figure label="Restants" value={messaging.measured ? formatCount(messaging.remaining) : EM_DASH} />
      </dl>

      {/* ⚠️ Three genuinely different statements, never one sentence with a number in it. « Non mesuré » is about our
          counter, « épuisé » is about the practice's month, and neither is « tout va bien ». */}
      {!messaging.measured ? (
        <p className="mt-3 rounded-md border border-destructive/40 p-3 text-sm" role="note">
          Aucun relevé pour {messaging.monthLabel} : le passage quotidien n&apos;a pas écrit la ligne de comptage de ce
          cabinet. Ce n&apos;est pas « aucun rappel envoyé » — c&apos;est que rien ne compte. Vérifiez le passage
          quotidien, puis <code>verify-schema</code>.
        </p>
      ) : messaging.exhausted ? (
        <p className="mt-3 rounded-md border border-destructive/40 p-3 text-sm" role="note">
          Forfait épuisé : les rappels WhatsApp de ce cabinet sont en attente et ne consomment rien. Son agenda, ses
          dossiers et ses rappels SMS continuent normalement. Un forfait supplémentaire les libère en une minute — sauf
          ceux dont le rendez-vous est déjà passé.
        </p>
      ) : null}

      <h3 className="mt-6 text-sm font-semibold">Expéditeur</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        {messaging.senderStateLabel}
        {/* FR-7b: stated only when it has moved, because that is what makes it read as an exception rather than a
            field. Never surfaced to the practice — it is our cost, not their limit. */}
        {messaging.templateCategoryLabel ? ` · ${messaging.templateCategoryLabel}` : ""}
      </p>
      {messaging.templateCategory && messaging.templateCategory.toUpperCase() !== "UTILITY" ? (
        <p className="mt-1 text-sm text-destructive">
          Le modèle de ce cabinet n&apos;est plus classé « UTILITY » par Meta : notre coût par message a changé. Le
          cabinet n&apos;en est pas informé et ses rappels continuent de partir.
        </p>
      ) : null}

      <h3 className="mt-6 text-sm font-semibold">Historique des allocations</h3>
      {messaging.entries.length === 0 ? (
        <p className="mt-1 text-sm text-muted-foreground">
          Aucune allocation enregistrée pour ce cabinet. Cela ne devrait pas arriver : chaque cabinet reçoit un forfait
          à son ouverture. Enregistrez un forfait mensuel pour rétablir la situation.
        </p>
      ) : (
        <ul className="mt-2 space-y-2">
          {messaging.entries.map((entry) => (
            <li key={entry.entryId} className="rounded-md border border-border p-3 text-sm">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className={entry.isCancelled ? "font-medium line-through" : "font-medium"}>
                  {entry.kindLabel} · {formatCount(entry.messages)} rappels
                  {entry.amountDt === null ? " · offert" : ` · ${formatMoney(entry.amountDt)}`}
                  {entry.methodLabel ? ` · ${entry.methodLabel}` : ""}
                </span>
                <span className="text-xs text-muted-foreground">{formatDate(entry.recordedOn)}</span>
              </div>

              {/* AC-6.4a: the effective month is STATED, never left to be re-derived — it is the only thing that says
                  when a lowering starts applying, and no reader can work that out from the ledger by eye. */}
              <p className="mt-1 text-xs text-muted-foreground">
                Prend effet en {entry.effectiveMonthLabel}
                {entry.reference ? ` · réf. ${entry.reference}` : ""}
              </p>

              {entry.isCancelled ? (
                // In words, not only struck through: a strike-through is invisible to a screen reader and to a
                // printout, and « annulée » is the whole meaning of the row.
                <p className="mt-1 text-xs text-destructive">
                  Annulée{entry.cancelledAt ? ` le ${formatDate(entry.cancelledAt)}` : ""}
                  {entry.cancelledBy ? ` par ${entry.cancelledBy}` : ""}
                  {entry.cancelReason ? ` — ${entry.cancelReason}` : ""}
                </p>
              ) : null}

              {entry.note ? <p className="mt-1 text-xs text-muted-foreground">{entry.note}</p> : null}

              {/* Offered on live allocations only — a cancelled one has nothing left to cancel, and a control that
                  opens onto a refusal is the dead control the device contract forbids. Always visible at every width,
                  never revealed by hover. */}
              {entry.isCancelled ? null : (
                <div className="mt-2">
                  <CancelAllowanceDialog clinicId={clinicId} clinicName={clinicName} entry={entry} />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** One labelled figure. The label is above the value, so a 320 px column never wraps a number onto a stray line. */
function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium">{value}</dd>
    </div>
  );
}
