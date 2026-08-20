"use client"

import { MessageCircle } from "lucide-react"

import { Card, CardContent } from "@/components/ui/card"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  STATUS_TONE_CLASS,
  STATUS_TONE_INK,
  STATUS_TONE_RAIL,
  statusToneClass,
  type StatusTone,
} from "@/components/ui/status-tone"
import { formatCalendarDay } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { ReminderAllowanceDto } from "@/lib/api/reminder-allowance"

/**
 * « Forfait WhatsApp » — what this cabinet has left this Tunisian month (US-2, AC-2.1), **as one strip with a
 * meter**.
 *
 * <p>It was three big figures in a tall card — « Restant », « Envoyés », « Forfait du mois » — sitting <i>above</i>
 * the delivery log along with two more cards, which is what pushed the log itself to the seventh block on the page.
 * The figures were also three ways of saying one thing: the third is fixed, the second is the first subtracted from
 * it, and a bar shows all three at once without asking anyone to do the arithmetic. So the section keeps its place
 * at the top — « combien me reste-t-il ? » is the question a secretary arrives with — and costs one line instead of
 * a card, while the connection and the monthly history move down into « Configuration ».</p>
 *
 * <p><b>Three states that look alike and are not</b>, and keeping them apart is still the whole job here:</p>
 * <ul>
 *   <li><b>Measured figures</b> — including a real <b>0</b>, which reads « 0 rappel envoyé » (AC-2.4).</li>
 *   <li><b>« Non mesuré »</b> — the server sent <code>measured: false</code>, i.e. no counting row exists. A
 *       statement about <i>us</i>, and it carries a <code>status</code> role: it is not an alarm. <b>No meter is
 *       drawn</b>, because an empty bar is a measured zero drawn in a picture.</li>
 *   <li><b>A failed read</b> — <code>LoadFailureNotice</code> with a retry, and an <b>alert</b> role (AC-2.5,
 *       EC-12). « 0 restant » here would be a statement about the cabinet where the truth is a statement about
 *       us.</li>
 * </ul>
 *
 * <p>⚠️ <b>The failure state and the measured zero are two different components on purpose</b> (NFR
 * accessibility): <code>alert</code> interrupts, <code>status</code> does not, and « je n'ai pas pu lire » and
 * « vous n'avez rien envoyé » are opposite facts. Rendering both through one box with a colour variant is how they
 * become the same announcement.</p>
 *
 * <p>⚠️ <b>The figures live in a live region</b> so a screen-reader user hears a refresh — the page reloads on the
 * SignalR key and the numbers change under them with no other signal.</p>
 *
 * <p>⚠️ <b>AC-2.7's contact route is absent, not empty, where the operator published none.</b> A <code>mailto:</code>
 * to nowhere is a dead control, which is worse than no control.</p>
 */
export function MessagingAllowanceCard({
  data,
  loading,
  error,
  onRetry,
}: {
  data: ReminderAllowanceDto | null
  loading: boolean
  /** Set when the read failed. Rendered as a retry notice — **never** as a zeroed meter. */
  error: string | null
  onRetry: () => void
}) {
  /*
   * `measured` is the gate on every figure below, and it needs all three fields rather than the flag alone: the
   * DTO types them `number | null` and only documents them as non-null when `measured` is true, so reading them
   * off the flag would be trusting a comment where a `null` would render « NaN % ».
   */
  const measured =
    data !== null &&
    data.measured &&
    data.allowance !== null &&
    data.consumed !== null &&
    data.remaining !== null

  const allowance = data?.allowance ?? 0
  const consumed = data?.consumed ?? 0
  const remaining = data?.remaining ?? 0

  /*
   * The bar fills as the forfait is SPENT, so a nearly-full bar reads as « il ne reste presque rien » without a
   * legend. The figure beside it is what is left, which is the number somebody came here for — the two are
   * complementary readings of the same month, not a contradiction.
   */
  const spentPct = allowance > 0 ? Math.min(100, Math.round((consumed / allowance) * 100)) : 0

  /*
   * ⚠️ The tone is the FORFAIT's, and it is a different question from the sender pill beside it — « puis-je
   * envoyer ? » versus « combien m'en reste-t-il ? ». A cabinet whose number is `Ready` can still be out of
   * messages, and one with a pending template can have a full forfait.
   *
   * The 10 % step is what turns « bientôt épuisé » into something a practice can act on while it still can; below
   * that a green bar at 96 % spent is technically true and practically a surprise.
   */
  const meterTone: StatusTone = !measured
    ? "neutral"
    : data.exhausted
      ? "negative"
      : allowance > 0 && remaining / allowance <= 0.1
        ? "active"
        : "positive"

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-4 sm:p-5">
        {/*
          One wrapping flex row, and the `basis-full` on the last two children is what makes it a *strip* on a desk
          and a stack on a phone **without rendering anything twice**.

          ⚠️ The first attempt was a three-column grid with the pill in the last column, and at 390 px it starved
          the title: `senderStateLabel` can be « WhatsApp n'est pas connecté », a `shrink-0` pill some 190 px wide,
          which left the title's `1fr` column about 100 px — so « Forfait WhatsApp » rendered as « Forfait … » and
          the month line broke across four lines. `basis-full` drops the pill and the meter onto their own lines
          below `sm:` instead, which gives the title the whole width it needs and costs nothing on a desk, where
          `sm:basis-auto` puts all four back on one line.
        */}
        <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
          <span
            aria-hidden="true"
            className={cn(
              "flex size-8 shrink-0 items-center justify-center rounded-md",
              measured ? STATUS_TONE_CLASS[meterTone] : "bg-muted text-muted-foreground",
            )}
          >
            <MessageCircle className="size-4" />
          </span>

          {/* `flex-1` below `sm:` so it claims the rest of its line; `sm:flex-none` so the meter — not the title —
              takes the slack once they share a row. No `truncate`: nothing here should ever be clipped. */}
          <div className="min-w-0 flex-1 sm:flex-none">
            <h2 className="text-sm font-semibold">Forfait WhatsApp</h2>
            <p className="mt-0.5 font-mono text-2xs text-muted-foreground">
              {data ? data.monthLabel : "Ce mois-ci"}
              {data && ` · renouvelé le ${formatCalendarDay(data.resetsOn)}`}
            </p>
          </div>

          {/* AC-1.4 — the sender state in WORDS, from the server's own label. Never a colour alone, and never
              re-derived here: « connecté » is not « prêt à envoyer », and only one place decides which. */}
          {data && (
            <span
              className={cn(
                "shrink-0 basis-full rounded-full border px-2.5 py-1 text-xs font-medium sm:basis-auto",
                // `positive` only when the sender is genuinely ready; every other state is something waiting on
                // Meta or on us, which `active` is the amber tone for. `negative` is not used here on purpose —
                // nothing is broken at the cabinet and none of these is the practice's fault.
                statusToneClass(data.senderState === "Ready" ? "positive" : "active"),
              )}
            >
              {data.senderStateLabel}
            </span>
          )}

          {/* The meter, or its skeleton. Absent entirely on a failed read and on an unmeasured month — see the
              component's ⚠️ notes: a bar at 0 % is a measured zero drawn as a picture. */}
          {error ? null : loading ? (
            <div aria-hidden="true" className="basis-full sm:min-w-56 sm:flex-1 sm:basis-auto">
              <span className="block h-5 w-40 animate-pulse rounded bg-muted" />
              <span className="mt-2 block h-1.5 w-full animate-pulse rounded-full bg-muted" />
            </div>
          ) : measured ? (
            <div className="basis-full sm:min-w-56 sm:flex-1 sm:basis-auto">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3" aria-live="polite">
                <p className="text-sm text-muted-foreground">
                  <span
                    className={cn(
                      "text-xl font-semibold tabular-nums tracking-tight",
                      STATUS_TONE_INK[meterTone],
                    )}
                  >
                    {remaining.toLocaleString("fr-TN")}
                  </span>{" "}
                  restant{remaining === 1 ? "" : "s"} sur {allowance.toLocaleString("fr-TN")}
                </p>
                <p className="font-mono text-2xs tabular-nums text-muted-foreground">
                  {consumedLabel(consumed)}
                </p>
              </div>
              {/*
                `aria-hidden`: the two figures above it are the same fact in words, and a screen reader announcing
                a percentage as well would read the month out twice.
              */}
              <div aria-hidden="true" className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted">
                <span
                  className="block h-full rounded-full"
                  style={{ width: `${spentPct}%`, backgroundColor: STATUS_TONE_RAIL[meterTone] }}
                />
              </div>
            </div>
          ) : null}
        </div>

        {error ? (
          <LoadFailureNotice
            message="Le forfait de rappels WhatsApp n'a pas pu être lu."
            detail="Aucun chiffre n'est affiché pour ne pas vous induire en erreur."
            onRetry={onRetry}
          />
        ) : loading || !data ? null : !data.measured ? (
          /*
            AC-2.4 — « non mesuré » is a statement about US, so it must never be dressed as a zero and must not be
            an alert either: nothing is wrong at the cabinet. `status`, not `alert` — the distinction this
            component exists to hold.
          */
          <p role="status" className="text-sm text-muted-foreground">
            Ce mois-ci n&apos;a pas encore été mesuré : nous n&apos;avons pas de relevé pour votre cabinet.
            Vos rappels ne sont pas bloqués pour autant — contactez-nous si cela persiste.
          </p>
        ) : data.exhausted ? (
          <p role="status" className="text-sm text-warning-ink">
            Votre forfait est épuisé pour ce mois-ci. Vos rendez-vous, vos dossiers et vos rappels SMS continuent
            normalement. Les rappels en attente partiront dès que nous augmentons votre forfait ; votre forfait se
            renouvelle le {formatCalendarDay(data.resetsOn)}.{" "}
            Consultez le journal ci-dessous pour savoir quels patients n&apos;ont pas été prévenus.
          </p>
        ) : null}

        {/* AC-2.6 — SMS is stated explicitly, and FR-1's duplicate disclosure beside it. Both are read once and
            then never again, which is why they are quiet text rather than a callout. */}
        <div className="flex flex-col gap-2 border-t pt-3">
          <p className="text-xs text-muted-foreground">
            Ce forfait ne concerne que les rappels <strong>WhatsApp</strong>. Vos rappels SMS ne sont pas comptés et
            continuent normalement.{" "}
            {data?.measured && (
              <>
                Un rappel envoyé deux fois — c&apos;est rare — compte deux fois, parce qu&apos;il nous est facturé
                deux fois.
              </>
            )}
          </p>

          {/*
            AC-2.7's contact route. Absent entirely where the operator published nothing — not an empty `mailto:`.

            ⚠️ Its own flex row rather than two links inside the paragraph above, and that is a § 2 requirement
            rather than layout taste: `.touch-target` overlays a 44 px hit area **without** repainting, so two
            inline links a few pixels apart would have overlapping overlays and the later sibling — painted last —
            would steal taps aimed at the first. Real boxes with a real gap instead: each grows to the 44 px floor
            on a coarse pointer and they cannot overhang each other.
          */}
          {data && (data.contactEmail || data.contactPhone) && (
            <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span>Besoin de plus de rappels ?</span>
              {data.contactEmail && (
                <a
                  className="inline-flex items-center underline coarse:min-h-11"
                  href={`mailto:${data.contactEmail}`}
                >
                  {data.contactEmail}
                </a>
              )}
              {data.contactPhone && (
                <a
                  className="inline-flex items-center underline coarse:min-h-11"
                  href={`tel:${data.contactPhone.replace(/\s/g, "")}`}
                >
                  {data.contactPhone}
                </a>
              )}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

/**
 * « 0 rappel envoyé » / « 1 rappel envoyé » / « 588 rappels envoyés » — AC-2.4's wording, in the singular where
 * French puts it.
 *
 * ⚠️ Zero takes the **singular** in French, which is why the test is `<= 1` and not `=== 1`.
 */
function consumedLabel(consumed: number): string {
  return `${consumed.toLocaleString("fr-TN")} ${consumed <= 1 ? "rappel envoyé" : "rappels envoyés"}`
}
