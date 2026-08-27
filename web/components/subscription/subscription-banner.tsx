"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { AlertTriangle, ShieldAlert, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { useSubscription } from "@/lib/subscription/subscription-context"
import type { SubscriptionDto } from "@/lib/api/subscription"
import { formatCalendarDay } from "@/lib/format"
import { isChromeLessPath } from "@/lib/nav"
import { cn } from "@/lib/utils"

/**
 * The one strip that appears on **every screen of the app** from seven days before the end date, and stays once it
 * has passed (AC-3.1, AC-3.3).
 *
 * <p><b>⚠️ Never a modal, and never a toast.</b> A cabinet meets this mid-consultation: a takeover would put a
 * dialog between a dentist and a patient's file, and a toast would be gone by the time anyone who can pay is
 * looking. It is a strip that stays until the state changes — which is also why it must not eat the screen (see
 * the height budget below).</p>
 *
 * <p><b>⚠️ Text and an icon, never colour alone.</b> The state's own French word is in the sentence, so « Expiré »
 * is legible in greyscale, at 200 % zoom, and to a screen reader. The tone only reinforces it.</p>
 *
 * <p><b>Mounted in `AppShell`, not in `app/layout.tsx`.</b> Two reasons, and the first is structural: `AppShell` is
 * `flex h-dvh`, so a sibling above it in the layout would make the document taller than the viewport — the page
 * would scroll as a whole and the phone's bottom bar would be pushed off screen. Here it is a flex row and `<main>`
 * shrinks around it, exactly as `BottomNav` already does. The second is that `AppShell` <i>is</i> the set of
 * chrome-ful routes: the six pages that do not use it are precisely `/login`, `/setup`, `/join`,
 * `/change-password`, `/signup` and `/signup/verifier`, so « absent on the auth pages » holds by construction
 * rather than by a path list somebody has to remember to extend. The `isChromeLessPath` guard below is the belt to
 * that braces, for a future page that renders the shell where it should not.</p>
 */
export function SubscriptionBanner() {
  const { subscription, enforced, dismissed, dismiss } = useSubscription()
  const pathname = usePathname()

  if (!enforced || !subscription || dismissed || isChromeLessPath(pathname)) return null

  const state = bannerState(subscription)
  if (!state) return null

  // Nothing to point at from the screen it points at.
  const onSubscriptionScreen = pathname === "/abonnement"

  return (
    /*
     * The height budget is the constraint that shapes this element (spec § Device contract): at most ~15 % of a
     * 380 px-tall landscape phone, i.e. ~57 px — otherwise it eats the agenda on the device the agenda is read on.
     *
     * ⚠️ `flex-wrap` alone did NOT deliver that. `min-w-0 flex-1` is `flex: 1 1 0%`, so the paragraph's hypothetical
     * main size is **zero** and it can never push the line to wrap: the icon and the `whitespace-nowrap` button hold
     * their widths and the text takes whatever is left — ~146 px of a 288 px content box at 320 px, i.e. roughly
     * eight lines and a ~160 px strip on *every screen of the app*. `basis-64` gives it a real basis so the controls
     * are pushed onto their own row below `sm:`, and `state.detail` is hidden there so the title + date (the fact
     * the strip exists to carry) are what remains.
     *
     * ⚠️ `coarse:py-1` is what keeps the budget met once the controls grow to their 44 px floor: 44 + 8 = 52 px,
     * where `py-2` around them would be 60 px and over. On a mouse the controls stay 32 px and `py-2` gives 48 px.
     *
     * ⚠️ No `role="status"`. `AppShell` remounts on every route change, so a live region here re-announces the whole
     * strip — sentence, « Renouveler » and the dismiss control — on every navigation, dozens of times a day. This is
     * standing chrome, not an inline async result; Part E's `StaffNotification` announces the change once.
     */
    <div
      className={cn(
        "flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1 border-b px-4 py-2 text-sm coarse:py-1 md:px-6",
        "print:hidden",
        state.className,
      )}
    >
      <state.icon className="size-4 shrink-0" aria-hidden="true" />
      <p className="min-w-0 flex-1 basis-64 sm:basis-0 [overflow-wrap:anywhere]">
        <span className="font-medium">{state.title}</span>{" "}
        <span className="hidden sm:inline">{state.detail}</span>
      </p>

      {!onSubscriptionScreen && (
        <Button asChild size="sm" variant="outline" className="coarse:min-h-11">
          <Link href="/abonnement">{state.cta}</Link>
        </Button>
      )}

      {/*
        AC-3.2 / AC-3.3: dismissible only while the entitlement is still valid, and the control is simply **absent**
        once it has ended — not disabled, which would read as a broken button.

        ⚠️ It **grows its own box** (`coarse:size-11`) rather than using `.touch-target`. § 2's rule is that the
        overlay is for an *isolated* control: this one sits 12 px from « Renouveler » in the same row, and since the
        later sibling paints last a 44 px pseudo-element here would overhang the button beside it and steal taps
        aimed at the one control that leads somewhere.
      */}
      {state.dismissible && (
        <button
          type="button"
          onClick={dismiss}
          aria-label="Masquer ce rappel pour aujourd'hui"
          className="-me-1 flex size-8 shrink-0 items-center justify-center rounded-md opacity-70 transition-opacity coarse:size-11 hover-hover:hover:opacity-100"
        >
          <X className="size-4" aria-hidden="true" />
        </button>
      )}
    </div>
  )
}

interface BannerState {
  title: string
  detail: string
  icon: typeof AlertTriangle
  className: string
  dismissible: boolean
  /**
   * ⚠️ Part of the state, not a constant. « Renouveler » is only true of an expiry: a **suspension** is not lifted by
   * paying — the sentence beside it says so — and a missing entitlement is our fault, not a lapse. On a phone the
   * strip is the only feedback channel, so a verb that contradicts the sentence next to it sends a practice to pay
   * for something that will not unblock them.
   */
  cta: string
}

/**
 * What to say, or `null` for a cabinet with nothing to be told — which is the common case and the reason the
 * banner is free on every screen for every clinic that is up to date.
 *
 * <p>⚠️ The three states are kept apart because their <i>remedies</i> differ, not because they look different. A
 * suspension is not fixed by paying, so it carries no countdown and no date; an expiry names the date because
 * « expiré » alone invites a phone call asking when. Both mirror the server's own refusal wording
 * (`SubscriptionRefusals`), so the strip on screen and the sentence in the toast say the same thing.</p>
 */
function bannerState(subscription: SubscriptionDto): BannerState | null {
  if (subscription.state === "Suspended") {
    return {
      title: "Accès suspendu.",
      detail: "Vous pouvez toujours consulter et exporter vos données. Contactez-nous pour le rétablir.",
      icon: ShieldAlert,
      className: "border-destructive/30 bg-destructive-wash text-foreground",
      dismissible: false,
      cta: "Voir les détails",
    }
  }

  if (!subscription.allowsWrites) {
    return {
      title: subscription.endsOn
        ? `Abonnement expiré le ${formatCalendarDay(subscription.endsOn)}.`
        : "Abonnement expiré.",
      detail:
        "Vos données restent consultables, imprimables et exportables ; seul l'enregistrement de nouvelles informations est suspendu.",
      icon: AlertTriangle,
      className: "border-destructive/30 bg-destructive-wash text-foreground",
      dismissible: false,
      // No end date means no lapse we can name — our own bookkeeping fault, so « renouveler » is advice they
      // cannot act on. Same distinction the server draws between `subscription_required` and `subscription_missing`.
      cta: subscription.endsOn ? "Renouveler" : "Voir les détails",
    }
  }

  // Still valid. Only inside the warning window the server decides — the client never re-derives the threshold,
  // which is what keeps the banner and the notification (Part E) counting the same days.
  if (!subscription.shouldWarn || subscription.endsOn === null) return null

  return {
    title: countdown(subscription.daysRemaining),
    detail: `Valable jusqu'au ${formatCalendarDay(subscription.endsOn)} inclus.`,
    icon: AlertTriangle,
    className: "border-warning/30 bg-warning-wash text-foreground",
    // ⚠️ Not unconditionally true: `dismiss()` is a no-op without a countdown, because the per-day key is
    // `endsOn|daysRemaining` and there is no key to store. The state that reaches here always has one today, so
    // this aligns the two halves rather than fixing a live dead control — and keeps a future reader change from
    // opening the gap silently.
    dismissible: subscription.daysRemaining !== null,
    cta: "Renouveler",
  }
}

/**
 * ⚠️ `null` gets a sentence with **no number in it**, rather than a `?? 0` that would print « d'ici 0 jours » — i.e.
 * « today is your last day » — to a cabinet the server declined to give a countdown for. The date in the detail line
 * below carries the fact either way; an invented figure would be the one thing on the strip that is not true.
 */
function countdown(daysRemaining: number | null): string {
  if (daysRemaining === null) return "Abonnement bientôt à renouveler."
  if (daysRemaining === 0) return "Dernier jour d'utilisation."
  return `Abonnement à renouveler d'ici ${daysRemaining} jour${daysRemaining > 1 ? "s" : ""}.`
}
