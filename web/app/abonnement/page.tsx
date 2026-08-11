"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import Link from "next/link"
import { CalendarClock, CreditCard, Lock, Mail, Phone, ShieldAlert, Wallet } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { SubscriptionHistoryTable } from "@/components/subscription/subscription-history-table"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import {
  subscriptionApi,
  type SubscriptionDto,
  type SubscriptionHistoryPageDto,
  type SubscriptionPlanPriceDto,
} from "@/lib/api/subscription"
import { useSession } from "@/lib/auth/session"
import { useSubscription } from "@/lib/subscription/subscription-context"
import { getErrorMessage } from "@/lib/errors"
import { formatCalendarDay, formatDT } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"

/** Whether this deployment works by subscription at all — `unknown` until the probe answers. */
type Availability = "unknown" | "available" | "unavailable"

/**
 * « Abonnement » — where the cabinet stands and how to pay (`clinic-subscription` Part C, US-2).
 *
 * <p><b>Reachable by every role, including a secretary</b> (AC-2.2): she is usually the one who meets
 * « Votre abonnement a expiré … » on a save, and pointing that refusal at a screen she cannot open would be worse
 * than not pointing it anywhere (EC-10). Only the payment **history** is admin-only (AC-2.3), and for a non-admin it
 * is replaced by a stated refusal rather than a fetch that 403s.</p>
 *
 * <p><b>A failed read is a retryable state, never « aucun abonnement »</b> (EC-13). The one exception is an explicit
 * **404**, which is the server saying this deployment does not work by subscription (AC-7.1/7.2) — that renders as an
 * explanation, not as an error.</p>
 */
export default function AbonnementPage() {
  const { user, isLoading: sessionLoading } = useSession()
  const isAdmin = user?.role === "admin"

  // ⚠️ The state comes from the provider, not from a second read of the same two endpoints. This page used to fire
  // its own `getMode()` **and** `subscriptionApi.get()` on mount — duplicating what Trigger 0 had already done — and
  // kept a private pair that could disagree with the banner and the rail row; meanwhile the context's `refresh` had
  // zero callers anywhere, so « Réessayer » here propagated to nothing. `enforced` is the same authority
  // (`requiresSubscription === true`) read once per session.
  const { subscription, enforcement, lastError, refresh } = useSubscription()

  const [historyError, setHistoryError] = useState<string | null>(null)
  const [history, setHistory] = useState<SubscriptionHistoryPageDto | null>(null)
  // Starts true so the first committed render is a skeleton, never « Aucun paiement enregistré »: `loadHistory` runs
  // in the effect *after* the render in which the section appears, so the two would otherwise be ordered
  // empty → skeleton → data (§ 13 — a loading skeleton distinct from empty).
  const [historyLoading, setHistoryLoading] = useState(true)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const historyRequestRef = useRef(0)

  // ⚠️ Only an explicit « off » reads as « this deployment has no subscriptions » (AC-7.1/7.2). A probe that could
  // not answer is `unreadable` and falls through to the retryable failure below — a network drop must never render
  // as an absence (EC-13), which is the distinction a single boolean could not carry.
  const availability: Availability =
    enforcement === "off" ? "unavailable" : enforcement === "on" && subscription ? "available" : "unknown"

  const unavailable = availability === "unavailable"
  // The provider swallows read failures on purpose (a banner must not vanish on a blip), which is exactly why it
  // publishes the last one: this screen is the one place that has to show it, with a retry that re-reads the state
  // every other consumer shares rather than a private copy of it.
  const error = subscription ? null : lastError
  const loading = availability === "unknown" && !error && enforcement !== "unreadable"

  const load = useCallback(() => refresh(true), [refresh])

  const loadHistory = useCallback(async () => {
    // The out-of-order guard `patients-table.tsx` already carries: this is re-created on every page/pageSize change,
    // so clicking through pages quickly can land an older response last and render page 2's rows under « 76–100 ».
    const requestId = ++historyRequestRef.current
    setHistoryLoading(true)
    setHistoryError(null)
    try {
      const result = await subscriptionApi.history({ page, pageSize })
      if (historyRequestRef.current !== requestId) return
      setHistory(result)
    } catch (err) {
      if (historyRequestRef.current !== requestId) return
      setHistoryError(getErrorMessage(err))
    } finally {
      if (historyRequestRef.current === requestId) setHistoryLoading(false)
    }
  }, [page, pageSize])

  useEffect(() => {
    // Guarded on the role as well as on availability, so a secretary's browser never issues the request the server
    // would 403 — which is what would otherwise stack an error toast on top of the refusal below.
    if (isAdmin && availability === "available") void loadHistory()
  }, [isAdmin, availability, loadHistory])

  useEffect(() => () => { historyRequestRef.current = -1 }, [])

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader title="Abonnement" subtitle={subtitleFor(subscription, unavailable)} />

        {unavailable ? (
          <EmptyState
            icon={CreditCard}
            chipClassName={zoneChipClass(ZONES.config)}
            title="Cette installation ne fonctionne pas par abonnement"
            description="Votre licence est permanente : rien n'expire et aucun renouvellement n'est nécessaire. Cet écran n'a donc rien à afficher ici."
            action={
              <Button asChild variant="outline" className="min-h-11">
                <Link href="/appointments">Retour à l'agenda</Link>
              </Button>
            }
          />
        ) : error ? (
          <LoadFailureNotice
            message="Votre abonnement n'a pas pu être chargé."
            detail="Cela ne change rien à votre abonnement lui-même."
            onRetry={load}
          />
        ) : loading ? (
          <div className="grid gap-6 lg:grid-cols-2">
            {Array.from({ length: 2 }).map((_, i) => (
              <Card key={i}>
                <CardContent className="space-y-3 py-8">
                  <div className="h-5 w-1/3 animate-pulse rounded bg-muted/60" />
                  <div className="h-10 w-2/3 animate-pulse rounded bg-muted/60" />
                  <div className="h-4 w-1/2 animate-pulse rounded bg-muted/60" />
                </CardContent>
              </Card>
            ))}
          </div>
        ) : subscription ? (
          <>
            {/*
              Single column up to `lg:`, two above it. Deliberately `lg:` and not `md:`: a tablet portrait is 820 px
              and already past `md:`, and the spec asks for a single column at a readable measure there rather than
              two narrow ones (« do not stretch to two just because the width allows it »).
            */}
            <div className="grid gap-6 lg:grid-cols-2">
              <div className="space-y-6">
                <StateCard subscription={subscription} />
                <TariffCard subscription={subscription} />
              </div>
              <div className="space-y-6">
                <PaymentInstructionsCard subscription={subscription} />
                <ContactCard subscription={subscription} />
              </div>
            </div>

            <section className="space-y-3" aria-labelledby="historique-paiements">
              <h2 id="historique-paiements" className="text-lg font-semibold text-foreground">
                Historique des paiements
              </h2>
              {sessionLoading ? (
                <p className="text-sm text-muted-foreground">Chargement…</p>
              ) : isAdmin ? (
                <SubscriptionHistoryTable
                  data={history}
                  loading={historyLoading}
                  error={historyError}
                  onRetry={() => void loadHistory()}
                  onPageChange={setPage}
                  onPageSizeChange={(size) => {
                    setPageSize(size)
                    setPage(1)
                  }}
                />
              ) : (
                /*
                 * Rendered *instead of* the table, so its fetch never fires (AC-2.3). The state, the date and the
                 * payment instructions above stay fully readable — this withholds one section, not the screen.
                 *
                 * ⚠️ An `EmptyState`, deliberately **not** `AccessDeniedCard`. That component is a whole-screen
                 * refusal: its root is `min-h-full items-center justify-center p-6`, whose centring resolves against
                 * `<main>` and therefore needs `width="none"` on the AppShell (which this page does not pass), and
                 * its only control is a `backHref` defaulting to « Retour à l'agenda » — navigating a secretary off
                 * the page she was sent to by a refused save, away from the bank details she came for.
                 */
                <EmptyState
                  icon={Lock}
                  size="compact"
                  chipClassName={zoneChipClass(ZONES.config)}
                  title="Historique réservé aux administrateurs"
                  description="Le détail des paiements de l'abonnement — dates, montants, références — est réservé aux administrateurs du cabinet. L'état de l'abonnement et la marche à suivre pour payer restent visibles ci-dessus."
                />
              )}
            </section>
          </>
        ) : null}
      </AppShell>
    </ClinicGuard>
  )
}

/** A fact under the title, never a paraphrase of the page. */
function subtitleFor(subscription: SubscriptionDto | null, unavailable: boolean): string | undefined {
  if (unavailable) return "Licence permanente — aucun abonnement à renouveler."
  if (!subscription) return undefined
  if (subscription.endsOn === null) return `${subscription.stateLabel} · sans échéance`
  return `${subscription.stateLabel} · jusqu'au ${formatCalendarDay(subscription.endsOn)}`
}

/**
 * State, date and countdown.
 *
 * <p><b>Text and an icon, never colour alone.</b> The badge carries the state's own French word, so « Expiré » is
 * legible in greyscale and to a screen reader — the tone only reinforces it.</p>
 */
function StateCard({ subscription }: { subscription: SubscriptionDto }) {
  const tone = stateTone(subscription.state)

  return (
    <Card>
      <CardHeader className="gap-2">
        <div className="flex items-center gap-3">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <CalendarClock className="size-4" />
          </span>
          <CardTitle>État de l'abonnement</CardTitle>
        </div>
        <CardDescription>{planSentence(subscription)}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge className={statusToneClass(tone)} variant="secondary">
            {subscription.stateLabel}
          </Badge>
          {subscription.daysRemaining !== null && (
            <span className="text-sm text-muted-foreground">
              {subscription.daysRemaining === 0
                ? "Dernier jour d'utilisation"
                : `${subscription.daysRemaining} jour${subscription.daysRemaining > 1 ? "s" : ""} restant${
                    subscription.daysRemaining > 1 ? "s" : ""
                  }`}
            </span>
          )}
        </div>

        <p className="text-sm text-foreground">
          {/* AC-2.5: an entitlement with no end date says so in words. A far-future date would be a sentence
              nobody can act on, and there is no date to name here — there is genuinely no expiry. */}
          {subscription.endsOn === null
            ? "Sans échéance — cet abonnement n'expire pas."
            : `${subscription.allowsWrites ? "Valable jusqu'au" : "A expiré le"} ${formatCalendarDay(subscription.endsOn)} inclus.`}
        </p>

        {subscription.suspensionReason && (
          <p role="status" className="flex items-start gap-2 rounded-lg bg-destructive-wash p-3 text-sm">
            <ShieldAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden="true" />
            <span className="min-w-0">
              <span className="font-medium text-foreground">Accès suspendu. </span>
              {subscription.suspensionReason}
            </span>
          </p>
        )}

        {!subscription.allowsWrites && !subscription.suspensionReason && (
          <p role="status" className="rounded-lg bg-warning-wash p-3 text-sm text-foreground">
            Vous pouvez toujours consulter, imprimer et exporter toutes vos données. Seul l'enregistrement de
            nouvelles informations est suspendu jusqu'au renouvellement.
          </p>
        )}
      </CardContent>
    </Card>
  )
}

/** The published tariff. Every forfait is listed, including one with no figure — « sur devis » is a statement. */
function TariffCard({ subscription }: { subscription: SubscriptionDto }) {
  return (
    <Card>
      <CardHeader className="gap-2">
        <div className="flex items-center gap-3">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <Wallet className="size-4" />
          </span>
          <CardTitle>Tarif</CardTitle>
        </div>
        <CardDescription>
          {subscription.plans.length === 0
            ? "Aucun tarif n'est publié sur cette installation."
            : "Les tarifs ci-dessous sont ceux de cette installation. Tous les forfaits donnent accès à l'ensemble des fonctionnalités."}
        </CardDescription>
      </CardHeader>
      {subscription.plans.length > 0 && (
        <CardContent>
          <ul className="space-y-2">
            {subscription.plans.map((plan) => (
              <TariffRow key={plan.plan} plan={plan} isCurrent={plan.plan === subscription.plan} />
            ))}
          </ul>
        </CardContent>
      )}
    </Card>
  )
}

function TariffRow({ plan, isCurrent }: { plan: SubscriptionPlanPriceDto; isCurrent: boolean }) {
  return (
    <li
      className={`flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 rounded-lg border p-3 ${
        isCurrent ? "border-primary/40 bg-primary/5" : "border-border"
      }`}
    >
      <span className="flex flex-wrap items-center gap-2 font-medium text-foreground">
        {plan.label}
        {isCurrent && (
          <Badge className={statusToneClass("accepted")} variant="secondary">
            Votre forfait
          </Badge>
        )}
      </span>
      <span className="text-end text-sm tabular-nums text-muted-foreground">
        {plan.priceMonthlyDt === null && plan.priceAnnualDt === null ? (
          "Sur devis"
        ) : (
          <>
            {plan.priceMonthlyDt !== null && <span className="text-foreground">{formatDT(plan.priceMonthlyDt)}/mois</span>}
            {plan.priceAnnualDt !== null && (
              <span className="block">{formatDT(plan.priceAnnualDt)}/an</span>
            )}
          </>
        )}
      </span>
    </li>
  )
}

/**
 * How to pay.
 *
 * <p><b>Never behind a disclosure</b>: this is the reason the screen exists, and a cabinet that cannot record work
 * is reading it precisely to find out what to do. `whitespace-pre-line` because the operator writes it as several
 * lines of bank details and losing the line breaks turns them into one unreadable run.</p>
 */
function PaymentInstructionsCard({ subscription }: { subscription: SubscriptionDto }) {
  return (
    <Card>
      <CardHeader className="gap-2">
        <div className="flex items-center gap-3">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <CreditCard className="size-4" />
          </span>
          <CardTitle>Comment payer</CardTitle>
        </div>
      </CardHeader>
      <CardContent>
        {subscription.paymentInstructions ? (
          <p className="whitespace-pre-line text-sm text-foreground">{subscription.paymentInstructions}</p>
        ) : (
          <p className="text-sm text-muted-foreground">
            Les modalités de paiement ne sont pas publiées sur cette installation. Contactez-nous et nous vous les
            communiquerons.
          </p>
        )}
      </CardContent>
    </Card>
  )
}

/** Who to contact. Both channels are real links, so a phone can dial and mail from the screen. */
function ContactCard({ subscription }: { subscription: SubscriptionDto }) {
  const hasContact = Boolean(subscription.contactEmail || subscription.contactPhone)

  return (
    <Card>
      <CardHeader className="gap-2">
        <div className="flex items-center gap-3">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <Mail className="size-4" />
          </span>
          <CardTitle>Nous contacter</CardTitle>
        </div>
      </CardHeader>
      <CardContent>
        {hasContact ? (
          <ul className="space-y-2 text-sm">
            {subscription.contactEmail && (
              <li>
                <a
                  href={`mailto:${subscription.contactEmail}`}
                  className="inline-flex items-center gap-2 text-primary underline underline-offset-2 coarse:min-h-11 hover-hover:hover:no-underline"
                >
                  <Mail className="size-4 shrink-0" aria-hidden="true" />
                  <span className="[overflow-wrap:anywhere]">{subscription.contactEmail}</span>
                </a>
              </li>
            )}
            {subscription.contactPhone && (
              <li>
                <a
                  href={`tel:${subscription.contactPhone.replace(/\s/g, "")}`}
                  className="inline-flex items-center gap-2 text-primary underline underline-offset-2 coarse:min-h-11 hover-hover:hover:no-underline"
                >
                  <Phone className="size-4 shrink-0" aria-hidden="true" />
                  {subscription.contactPhone}
                </a>
              </li>
            )}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">
            Aucune coordonnée n'est publiée sur cette installation.
          </p>
        )}
      </CardContent>
    </Card>
  )
}

/** « Suspendu » and « Expiré » are both bad news, but only one is fixed by paying — so they do not share a tone. */
function stateTone(state: SubscriptionDto["state"]): StatusTone {
  switch (state) {
    case "Active":
      return "positive"
    case "Trial":
      return "accepted"
    case "Expired":
      return "active"
    case "Suspended":
      return "negative"
  }

  // Unreachable per the union, so exhaustiveness checking is preserved and a new member is still a `tsc` error —
  // but `apiGet` validates no shape, so a state added server-side ahead of this build would otherwise fall off the
  // end and return `undefined`. `statusToneClass` happens to absorb that today; accidental safety is not safety.
  return "neutral"
}

/** The forfait, or the honest absence of one — a default would read as a commercial choice nobody made. */
function planSentence(subscription: SubscriptionDto): string {
  return subscription.planLabel
    ? `Forfait ${subscription.planLabel}`
    : "Aucun forfait n'est encore associé à ce cabinet."
}
