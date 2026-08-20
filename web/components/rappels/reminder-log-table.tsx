"use client"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { BellOff, SearchX, Send } from "lucide-react"
import { cn } from "@/lib/utils"
import { formatDateTime } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import type { ReminderDeliveryStatus, ReminderStatusDto } from "@/lib/api/reminder-settings"

/**
 * The delivery log — who was sent what, when, and why it failed.
 *
 * <p>Follows the shared <b>DataTable</b> rules from the visual-language pass: monospace uppercase column headers a
 * step quieter than the data, <code>tabular-nums</code> on every column of digits, hairlines between rows rather
 * than a border per row, and a faded zero/absent value instead of a black one.</p>
 *
 * <p>Two calls specific to this table:</p>
 * <ul>
 *   <li><b>Status is a 2px left stripe plus a pill, never a tinted row.</b> At table density a filled row reads as
 *       a rendering fault, and a stripe survives hover, focus and selection — a background does not.</li>
 *   <li><b>Channel is a monospace label, not a colour.</b> WhatsApp's brand green would collide head-on with the
 *       « Envoyé » green, leaving the reader decoding two different meanings from one hue.</li>
 * </ul>
 *
 * <p>Every pill goes through the semantic theme tokens (<code>bg-success-wash text-success</code> …) rather than
 * hardcoded <code>bg-green-100 text-green-800</code> pairs, so dark mode needs no <code>dark:</code> variant and
 * the palette cannot drift from the rest of the app.</p>
 */
interface ReminderLogTableProps {
  rows: ReminderStatusDto[]
  loading?: boolean
  /** True while refetching with rows already on screen — dim them, never blank them. */
  refreshing?: boolean
  /** Set when a filter is narrowing the log, so the empty state can say so instead of « rien envoyé ». */
  isFiltered?: boolean
  onResetFilters?: () => void
  /** No channel is sendable — a different emptiness from "nothing sent yet", with a different way out. */
  noChannelConfigured?: boolean
  onConfigure?: () => void
}

export function ReminderLogTable({
  rows,
  loading = false,
  refreshing = false,
  isFiltered = false,
  onResetFilters,
  noChannelConfigured = false,
  onConfigure,
}: ReminderLogTableProps) {
  if (loading) {
    return (
      <div className="rounded-xl border" role="status" aria-label="Chargement du journal">
        <div className="flex flex-col gap-3 p-4">
          {[38, 82, 74, 79, 60, 71].map((w, i) => (
            <div key={i} className="h-3.5 animate-pulse rounded bg-muted" style={{ width: `${w}%` }} />
          ))}
        </div>
      </div>
    )
  }

  if (rows.length === 0) {
    /*
     * Three distinct emptinesses, because the ways out differ. Collapsing them into one « aucun message » would
     * leave a clinic that has never configured a channel waiting for reminders that can never be sent.
     *
     * ⚠️ The filtered case gets « Effacer les filtres » and never « Configurer les canaux »: the messages very
     * likely exist and the window is simply too narrow, and pushing a settings screen at that user answers a
     * question they did not ask.
     *
     * The « Aucun canal actif » pill that used to sit above the title is gone — it restated the heading directly
     * below it. Its warning tone moved onto the icon chip, which is where the primitive puts the one piece of
     * decoration it sanctions.
     */
    return (
      <div className="rounded-xl border">
        {noChannelConfigured ? (
          <EmptyState
            icon={BellOff}
            // `text-warning-ink`, not `text-warning`: --warning sits at L 0.62 and lands near 3.5:1 on its own
            // wash, under the floor. --warning-ink is the darkened step that exists for exactly this pairing.
            chipClassName="bg-warning-wash text-warning-ink"
            title="Les rappels ne sont pas encore activés"
            description="Ni SMS ni WhatsApp n'est configuré, donc aucun message ne part. Renseignez un canal pour commencer à envoyer des rappels de rendez-vous."
            action={
              onConfigure && (
                <Button size="sm" onClick={onConfigure}>
                  Configurer les canaux
                </Button>
              )
            }
          />
        ) : isFiltered ? (
          <EmptyState
            icon={SearchX}
            chipClassName={OPS_CHIP}
            title="Aucun message ne correspond"
            description="Aucun envoi ne correspond à ces filtres. Élargissez la période ou retirez un filtre."
            secondaryAction={
              onResetFilters && (
                <Button size="sm" variant="outline" onClick={onResetFilters}>
                  Effacer les filtres
                </Button>
              )
            }
          />
        ) : (
          <EmptyState
            icon={Send}
            chipClassName={OPS_CHIP}
            title="Aucun message pour le moment"
            description="Les rappels partent automatiquement avant chaque rendez-vous, selon les délais configurés."
          />
        )}
      </div>
    )
  }

  return (
    <div
      className={cn(
        "overflow-hidden rounded-xl border transition-opacity duration-200",
        refreshing && "opacity-60",
      )}
      aria-busy={refreshing || undefined}
    >
      {/* ⚠️ `failureReason` stays IN the card, exactly as it stays in the row: it is the only thing that makes
          a failure actionable, and the comment below records that a tooltip is unreachable on the tablet a
          dentist actually holds. A card list would have been an easy place to lose it. */}
      <CardList
        className={CARDS_ONLY}
        ariaLabel="Journal des rappels"
        items={rows}
        getKey={(r) => r.id}
        title={(r) => r.patientName ?? "Patient inconnu"}
        accent={(r) => STRIPE[r.status]}
        status={(r) => (
          <>
            <Badge variant="secondary" className={STATUS_CLASS[r.status]}>
              {STATUS_LABEL[r.status]}
            </Badge>
            {holdKindLabel(r.blockReason) && (
              <Badge variant="secondary" className="bg-muted font-mono text-2xs text-muted-foreground">
                {holdKindLabel(r.blockReason)}
              </Badge>
            )}
            {r.isRecall && (
              <Badge variant="secondary" className="bg-muted text-muted-foreground">
                relance
              </Badge>
            )}
          </>
        )}
        subtitle={(r) =>
          r.failureReason ? <span className={REASON_CLASS[r.status]}>{r.failureReason}</span> : null
        }
        fields={(r) => [
          { label: "Canal", value: <span className="font-mono text-2xs">{r.channel}</span> },
          { label: "Destinataire", value: <span className="font-mono text-xs">{r.recipientMasked}</span> },
          { label: "Rendez-vous", value: r.appointmentAt ? formatDateTime(r.appointmentAt) : null },
          { label: "Prévu", value: formatDateTime(r.scheduledAt) },
          { label: "Envoyé", value: r.sentAt ? formatDateTime(r.sentAt) : null },
        ]}
      />
      <Table containerClassName={TABLE_ONLY}>
        <TableHeader>
          {/* Headers a step quieter than the data: mono, uppercase, muted. In the stock primitive they are
              `text-foreground`, i.e. as black as the values, so the eye cannot tell which is the content. */}
          <TableRow>
            {["Patient", "Canal", "Destinataire", "Rendez-vous", "Prévu", "Statut"].map((h) => (
              <TableHead
                key={h}
                className="whitespace-nowrap font-mono text-2xs font-medium uppercase tracking-[0.07em] text-muted-foreground"
              >
                {h}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.id} className="relative">
              <TableCell className="relative">
                <span
                  aria-hidden="true"
                  className="absolute inset-y-0 left-0 w-[2px]"
                  style={{ backgroundColor: STRIPE[row.status] }}
                />
                <span className="font-medium">{row.patientName ?? "Patient inconnu"}</span>
                {/*
                  A recall row. Kept visible even though the feature is being retired: these messages really were
                  sent to patients, and hiding them would rewrite the past rather than remove a feature.
                */}
                {row.isRecall && (
                  <Badge variant="secondary" className="ml-2 bg-muted text-muted-foreground">
                    relance
                  </Badge>
                )}
              </TableCell>
              <TableCell>
                <span className="font-mono text-2xs tracking-[0.04em] text-muted-foreground">{row.channel}</span>
              </TableCell>
              <TableCell className="font-mono text-xs text-muted-foreground">{row.recipientMasked}</TableCell>
              <TableCell className="whitespace-nowrap tabular-nums">
                {row.appointmentAt ? (
                  formatDateTime(row.appointmentAt)
                ) : (
                  // A faded dash, not a black one: the absence of an appointment is not a value to read.
                  <span className="text-muted-foreground/60">—</span>
                )}
              </TableCell>
              <TableCell className="whitespace-nowrap tabular-nums text-muted-foreground">
                {formatDateTime(row.scheduledAt)}
              </TableCell>
              <TableCell>
                <span className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary" className={STATUS_CLASS[row.status]}>
                    {STATUS_LABEL[row.status]}
                  </Badge>
                  {holdKindLabel(row.blockReason) && (
                    <Badge variant="secondary" className="bg-muted font-mono text-2xs text-muted-foreground">
                      {holdKindLabel(row.blockReason)}
                    </Badge>
                  )}
                  {row.sentAt && (
                    <span className="whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                      {formatDateTime(row.sentAt)}
                    </span>
                  )}
                </span>
                {/*
                  The reason lives IN the row, not in a tooltip. It is the only thing that makes a failure
                  actionable, and a tooltip is unreachable on the tablet a dentist actually holds.
                */}
                {row.failureReason && (
                  <p className={cn("mt-1 max-w-[38ch] text-xs", REASON_CLASS[row.status])}>{row.failureReason}</p>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

/** « Rappels » lives in the Gestion zone; its empty states wear the hue the rail and the page eyebrow already do. */
const OPS_CHIP = zoneChipClass(ZONES.ops)

const STATUS_LABEL: Record<ReminderDeliveryStatus, string> = {
  sent: "Envoyé",
  pending: "En attente",
  failed: "Échec",
  // « Bloqué » and not « En attente »: the row is not queued behind others, it is not going anywhere until
  // somebody changes a setting. The reason beside it says which one.
  blocked: "Bloqué",
}

/**
 * Semantic theme tokens, never hardcoded Tailwind colour pairs — dark mode follows with no `dark:` variant.
 *
 * ⚠️ `pending` is `text-warning-ink`, not `text-warning`. `--warning` sits at L 0.62, which measures around
 * 3.5:1 against its own wash — under the floor for badge-sized text, and this pill appears on every queued row.
 * `--warning-ink` is the darkened step added for exactly this pairing (`ui/status-tone.ts` carries the same note);
 * `--success` and `--destructive` clear it at their normal step, which is why only the amber differs.
 */
const STATUS_CLASS: Record<ReminderDeliveryStatus, string> = {
  sent: "bg-success-wash text-success",
  pending: "bg-warning-wash text-warning-ink",
  failed: "bg-destructive-wash text-destructive",
  // Amber like `pending`, because a blocked row is not a delivery failure — nothing was attempted and the
  // message will still go out once the channel works. It is « à régler », not « perdu », and painting it red
  // beside real failures would bury the ones that need a phone call.
  blocked: "bg-warning-wash text-warning-ink",
}

/**
 * The status stripe, as a CSS colour rather than a Tailwind class.
 *
 * It has two consumers now — the table row's 2px rule and the card's accent bar — and the card list paints its
 * accent through `style`, since a card's accent can also be a per-row hex from the database. Keeping one map of
 * colours and letting both read it beats a class map plus a parallel colour map that can disagree about what
 * « failed » looks like.
 */
/**
 * The tone of the row's reason line. It follows the **status**, not the mere presence of a reason: a blocked
 * row's reason is « le canal SMS n'est pas configuré », which is an instruction, not an incident. Rendering it in
 * the same red as a real delivery failure is what would make a screen full of misconfiguration look like a screen
 * full of patients who were never reached.
 */
/**
 * AC-4.9 — **what kind of hold this is, read off the machine-readable reason** rather than off the French sentence
 * beside it.
 *
 * <p>A « Bloqué » row can mean « configure a channel », « ask us for more messages » or « Meta has stopped your
 * number », and those are three entirely different next actions. The sentence already says which; this one word beside
 * the status is what makes a column of blocked rows sortable by eye — and it comes from the enum, so rewording the
 * sentence cannot change it (the `Contains("déjà facturée")` practice the backend deleted).</p>
 *
 * <p>⚠️ An unknown value returns <b>null</b> and the badge is simply absent: a member added server-side and not here
 * must degrade to « no extra word », never to « Inconnu » beside a perfectly explained row.</p>
 */
function holdKindLabel(blockReason: string | null): string | null {
  switch (blockReason) {
    case "MessagingAllowanceExhausted":
    case "MessagingAllowanceMissing":
      return "forfait"
    case "MessagingTemplateNotReady":
      return "modèle"
    case "MessagingNumberStopped":
      return "numéro"
    case "SubscriptionExpired":
      return "abonnement"
    case "ChannelDisabled":
    case "ChannelUnconfigured":
    case "ChannelUnsupported":
      return "canal"
    default:
      return null
  }
}

const REASON_CLASS: Record<ReminderDeliveryStatus, string> = {
  sent: "text-muted-foreground",
  pending: "text-muted-foreground",
  failed: "text-destructive",
  blocked: "text-warning-ink",
}

/**
 * The row's status stripe, as a raw colour for an inline `backgroundColor`.
 *
 * ⚠️ **`--success`, never `--color-success`.** The `--color-` aliases are `@theme inline` entries, and Tailwind v4
 * emits such a variable to `:root` only when it judges it **used** — so which ones exist at runtime is a property
 * of the current build, not of the stylesheet. Measured in the browser: `--color-zone-clinical` was **absent**
 * while `--color-zone-daily` and `--color-zone-money` resolved. A `var(--color-…)` in an inline `style` is
 * therefore a coin flip that re-flips whenever utility usage changes, and it paints **transparent** when it loses
 * — silently, because an unresolvable custom property is not an error. The raw tokens are declared
 * unconditionally on `:root` and again under `.dark`, so they always resolve *and* follow the theme. Utilities are
 * unaffected: only an inline style needs a property that exists at runtime.
 */
const STRIPE: Record<ReminderDeliveryStatus, string> = {
  sent: "var(--success)",
  pending: "var(--warning)",
  failed: "var(--destructive)",
  blocked: "var(--warning)",
}
