"use client"

import { useEffect, useState } from "react"
import { usePathname, useRouter } from "next/navigation"
import { Check, CircleHelp, Copy, Lightbulb, ListChecks, MessageCircle, PlayCircle } from "lucide-react"

import { FeedbackDialog } from "@/components/feedback/feedback-dialog"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { dashboardApi } from "@/lib/api/dashboard"
import { feedbackApi } from "@/lib/api/feedback"
import type { GettingStartedDto } from "@/lib/api/types"
import { useSession } from "@/lib/auth/session"
import { SHOW_PREMIERS_PAS_EVENT, requestShowPremiersPas } from "@/lib/getting-started-celebration"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { ONBOARDING_VIDEOS_PAGE } from "@/lib/onboarding-videos"
import { useSubscription } from "@/lib/subscription/subscription-context"
import { whatsAppUrl } from "@/lib/whatsapp"

/**
 * Who to ask, and where the videos are (guided-first-days D).
 *
 * <p>The number is the one the vendor publishes for « Abonnement » (`Subscription:ContactPhone`, served to every
 * role through `useSubscription`) — one support line, not a second setting to keep in step. Where there is none
 * (a clinic's own PC, which has no subscription) there is nothing to call, so the header shows no « Aide » unless
 * « Donner mon avis » is served, and Paramètres keeps its « contactez la personne qui a installé votre logiciel ».</p>
 */
export function useHelpContact() {
  const { subscription } = useSubscription()
  const { data: status } = useUserStatus()
  const phone = subscription?.contactPhone?.trim() || null
  const clinicName = status?.clinic?.name ?? status?.clinicName ?? null
  // WhatsApp may be a different number from the one to call.
  const whatsappNumber = subscription?.whatsAppPhone?.trim() || phone
  const whatsapp = whatsAppUrl(whatsappNumber, clinicName ? `Bonjour, ici ${clinicName}. ` : "Bonjour. ")
  // The number as written, for a label (« Nouveautés »' closing line) — only when it makes a link.
  return { phone, whatsapp, whatsappNumber: whatsapp ? whatsappNumber : null }
}

/**
 * Whether « Donner mon avis » is served here (guided-first-days E): only the server's 404 means « not here » (a
 * clinic's own PC, where no vendor console would read it). One cached read shared with the week-one card.
 */
export function useFeedbackAvailable() {
  const [available, setAvailable] = useState(false)
  useEffect(() => {
    let cancelled = false
    feedbackApi
      .prompt()
      .then((prompt) => {
        if (!cancelled) setAvailable(prompt !== null)
      })
      .catch(() => {
        // Unknown is not « absent »: offer the row, and let the send itself say if it cannot go.
        if (!cancelled) setAvailable(true)
      })
    return () => {
      cancelled = true
    }
  }, [])
  return available
}

/** The rows themselves — the header's popover and the « Besoin d'aide ? » card in Paramètres both render these. */
export function HelpRows({
  phone,
  whatsapp,
  onFeedback,
  premiersPas,
}: {
  phone: string | null
  whatsapp: string | null
  /** « Donner mon avis ou une idée » — absent where feedback is not served. */
  onFeedback?: () => void
  /** « Premiers pas · 3 sur 7 » — an administrator's setup steps while they apply, hidden or not. */
  premiersPas?: { done: number; total: number; onOpen: () => void }
}) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    if (!phone) return
    try {
      await navigator.clipboard.writeText(phone)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard can be refused; the number is on screen and selectable either way.
    }
  }

  const row = "flex min-h-11 items-center gap-3 rounded-md px-2 text-sm hover:bg-accent focus-visible:bg-accent"

  return (
    <div className="space-y-1">
      {whatsapp && (
        <a href={whatsapp} target="_blank" rel="noopener noreferrer" className={row}>
          <MessageCircle aria-hidden className="size-4 shrink-0 text-success" />
          Écrire à APEXA sur WhatsApp
        </a>
      )}
      {premiersPas && (
        <button type="button" onClick={premiersPas.onOpen} className={`${row} w-full text-start`}>
          <ListChecks aria-hidden className="size-4 shrink-0 text-primary" />
          <span className="min-w-0 flex-1">Premiers pas</span>
          <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
            {premiersPas.done} sur {premiersPas.total}
          </span>
        </button>
      )}
      <a href={ONBOARDING_VIDEOS_PAGE} target="_blank" rel="noopener noreferrer" className={row}>
        <PlayCircle aria-hidden className="size-4 shrink-0 text-primary" />
        Vidéos : vos premiers pas
      </a>
      {onFeedback && (
        <button type="button" onClick={onFeedback} className={`${row} w-full text-start`}>
          <Lightbulb aria-hidden className="size-4 shrink-0 text-warning-ink" />
          Donner mon avis ou une idée
        </button>
      )}
      {/* For a PC without WhatsApp: the number itself, readable and copyable. */}
      {phone && (
        <div className="flex items-center gap-2 border-t px-2 pt-2">
          <span className="min-w-0 flex-1 text-sm tabular-nums text-muted-foreground">{phone}</span>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-9 shrink-0 coarse:size-11"
            aria-label="Copier le numéro"
            onClick={copy}
          >
            {copied ? <Check aria-hidden className="size-4" /> : <Copy aria-hidden className="size-4" />}
          </Button>
        </div>
      )}
      <p role="status" className="sr-only">
        {copied ? "Numéro copié." : ""}
      </p>
    </div>
  )
}

/** « Aide » in the header, every screen and every role: a label from `md:`, the icon alone below. */
export function HelpMenu() {
  const { phone, whatsapp } = useHelpContact()
  const feedbackAvailable = useFeedbackAvailable()
  const [open, setOpen] = useState(false)
  const [feedbackOpen, setFeedbackOpen] = useState(false)
  const { user } = useSession()
  const isAdmin = user?.role === "admin"
  const router = useRouter()
  const pathname = usePathname()
  // Read when the menu opens, not on every screen: a fact that changes a few times in a cabinet's first month.
  const [started, setStarted] = useState<GettingStartedDto | null>(null)
  useEffect(() => {
    if (!open || !isAdmin) return
    let cancelled = false
    dashboardApi
      .getGettingStarted()
      .then((value) => {
        if (!cancelled) setStarted(value)
      })
      .catch(() => {
        // No row rather than a wrong one: the card's own read reports its failures.
      })
    return () => {
      cancelled = true
    }
  }, [open, isAdmin])
  if (!phone && !feedbackAvailable) return null

  const openPremiersPas = () => {
    setOpen(false)
    if (pathname === "/") window.dispatchEvent(new Event(SHOW_PREMIERS_PAS_EVENT))
    else {
      requestShowPremiersPas()
      router.push("/")
    }
  }
  const premiersPas =
    isAdmin && started?.visible
      ? { done: started.steps.filter((step) => step.done).length, total: started.steps.length, onOpen: openPremiersPas }
      : undefined

  const openFeedback = () => {
    setOpen(false)
    setFeedbackOpen(true)
  }

  return (
    <>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            variant="ghost"
            aria-label="Aide"
            className="size-9 gap-1.5 px-0 coarse:h-11 coarse:min-w-11 md:w-auto md:px-2.5"
          >
            <CircleHelp aria-hidden className="size-5" />
            <span aria-hidden className="hidden text-sm md:inline">
              Aide
            </span>
          </Button>
        </PopoverTrigger>
        <PopoverContent align="end" className="w-[min(18rem,calc(100vw-2rem))] p-2">
          <p className="px-2 pb-1.5 pt-1 text-sm font-semibold">Une question ?</p>
          <HelpRows
            phone={phone}
            whatsapp={whatsapp}
            onFeedback={feedbackAvailable ? openFeedback : undefined}
            premiersPas={premiersPas}
          />
        </PopoverContent>
      </Popover>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} source="aide" />
    </>
  )
}
