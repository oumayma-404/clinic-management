"use client"

import { MessageCircle } from "lucide-react"

import { Card, CardContent } from "@/components/ui/card"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { statusToneClass } from "@/components/ui/status-tone"
import { formatCalendarDay } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { ReminderAllowanceDto } from "@/lib/api/reminder-allowance"

/**
 * « Forfait de rappels WhatsApp » — what this cabinet has left this Tunisian month (US-2, AC-2.1).
 *
 * <p><b>Three states that look alike and are not</b>, and keeping them apart is the whole job of this component:</p>
 * <ul>
 *   <li><b>Measured figures</b> — including a real <b>0</b>, which reads « 0 rappel envoyé » (AC-2.4).</li>
 *   <li><b>« Non mesuré »</b> — the server sent <code>measured: false</code>, i.e. no counting row exists. A
 *       statement about <i>us</i>, and it carries a <code>status</code> role: it is not an alarm.</li>
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
  /** Set when the read failed. Rendered as a retry notice — **never** as three zeros. */
  error: string | null
  onRetry: () => void
}) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 p-4 sm:p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="flex items-center gap-2 text-base font-semibold">
              <MessageCircle aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
              Forfait de rappels WhatsApp
            </h2>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {data ? data.monthLabel : "Ce mois-ci"}
            </p>
          </div>

          {/* AC-1.4 — the sender state in WORDS, from the server's own label. Never a colour alone, and never
              re-derived here: « connecté » is not « prêt à envoyer », and only one place decides which. */}
          {data && (
            <span
              className={cn(
                "shrink-0 rounded-full border px-2.5 py-1 text-xs font-medium",
                // `positive` only when the sender is genuinely ready; every other state is something waiting on
                // Meta or on us, which `active` is the amber tone for. `negative` is not used here on purpose —
                // nothing is broken at the cabinet and none of these is the practice's fault.
                statusToneClass(data.senderState === "Ready" ? "positive" : "active"),
              )}
            >
              {data.senderStateLabel}
            </span>
          )}
        </div>

        {error ? (
          <LoadFailureNotice
            message="Le forfait de rappels WhatsApp n'a pas pu être lu."
            detail="Les chiffres ci-dessous ne sont pas affichés pour ne pas vous induire en erreur."
            onRetry={onRetry}
          />
        ) : loading ? (
          <div className="grid gap-3 sm:grid-cols-3" aria-hidden="true">
            {[0, 1, 2].map((i) => (
              <div key={i} className="flex flex-col gap-2">
                <span className="h-3 w-24 animate-pulse rounded bg-muted" />
                <span className="h-7 w-16 animate-pulse rounded bg-muted" />
              </div>
            ))}
          </div>
        ) : !data ? null : data.measured ? (
          <>
            {/*
              Stacked at phone width with each label above its figure, three across from `sm:` — the device table's
              own row for this surface. The REMAINING figure leads, because it is the one a secretary is looking for.

              `aria-live="polite"` on the group: the page refetches on the realtime key, so these numbers change with
              no other signal to a screen-reader user.
            */}
            <dl className="grid gap-4 sm:grid-cols-3" aria-live="polite">
              <Figure
                label="Restant"
                value={data.remaining}
                emphasis={data.exhausted ? "spent" : "lead"}
              />
              <Figure label="Envoyés" value={data.consumed} suffix={consumedSuffix(data.consumed)} />
              <Figure label="Forfait du mois" value={data.allowance} />
            </dl>

            {data.exhausted && (
              <p role="status" className="text-sm text-warning-ink">
                Votre forfait est épuisé pour ce mois-ci. Vos rendez-vous, vos dossiers et vos rappels SMS
                continuent normalement. Les rappels en attente partiront dès que nous augmentons votre forfait ;
                votre forfait se renouvelle le {formatCalendarDay(data.resetsOn)}.{" "}
                Consultez le journal ci-dessous pour savoir quels patients n&apos;ont pas été prévenus.
              </p>
            )}
          </>
        ) : (
          /*
            AC-2.4 — « non mesuré » is a statement about US, so it must never be dressed as three zeros and must not
            be an alert either: nothing is wrong at the cabinet. `status`, not `alert` — the distinction this
            component exists to hold.
          */
          <p role="status" className="text-sm text-muted-foreground">
            Ce mois-ci n&apos;a pas encore été mesuré : nous n&apos;avons pas de relevé pour votre cabinet.
            Vos rappels ne sont pas bloqués pour autant — contactez-nous si cela persiste.
          </p>
        )}

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
 * One figure with its label above it.
 *
 * ⚠️ A `null` value renders « — », not `0`: it is only reachable when the server said `measured: false`, and this
 * component takes the other branch for that — so the dash is a belt-and-braces refusal to invent a zero rather than
 * a state anybody should see.
 */
function Figure({
  label,
  value,
  suffix,
  emphasis,
}: {
  label: string
  value: number | null
  suffix?: string
  emphasis?: "lead" | "spent"
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm font-medium text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "text-2xl font-semibold tabular-nums tracking-tight",
          emphasis === "spent" && "text-warning-ink",
        )}
      >
        {value === null ? "—" : value.toLocaleString("fr-TN")}
        {suffix && <span className="ms-1 text-sm font-normal text-muted-foreground">{suffix}</span>}
      </dd>
    </div>
  )
}

/** « 0 rappel envoyé » / « 1 rappel » / « 12 rappels » — AC-2.4's wording, in the singular where it belongs. */
function consumedSuffix(consumed: number | null): string | undefined {
  if (consumed === null) return undefined
  return consumed === 1 ? "rappel" : "rappels"
}
