"use client"

import { useEffect, useState } from "react"
import { Check, CircleHelp, Copy, Lightbulb, MessageCircle, PlayCircle } from "lucide-react"

import { FeedbackDialog } from "@/components/feedback/feedback-dialog"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { feedbackApi } from "@/lib/api/feedback"
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
  const whatsapp = whatsAppUrl(phone, clinicName ? `Bonjour, ici ${clinicName}. ` : "Bonjour. ")
  return { phone, whatsapp }
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
}: {
  phone: string | null
  whatsapp: string | null
  /** « Donner mon avis ou une idée » — absent where feedback is not served. */
  onFeedback?: () => void
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
  if (!phone && !feedbackAvailable) return null

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
            className="size-9 gap-1.5 px-0 coarse:size-11 md:w-auto md:px-2.5"
          >
            <CircleHelp aria-hidden className="size-5" />
            <span aria-hidden className="hidden text-sm md:inline">
              Aide
            </span>
          </Button>
        </PopoverTrigger>
        <PopoverContent align="end" className="w-[min(18rem,calc(100vw-2rem))] p-2">
          <p className="px-2 pb-1.5 pt-1 text-sm font-semibold">Une question ?</p>
          <HelpRows phone={phone} whatsapp={whatsapp} onFeedback={feedbackAvailable ? openFeedback : undefined} />
        </PopoverContent>
      </Popover>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} source="aide" />
    </>
  )
}
