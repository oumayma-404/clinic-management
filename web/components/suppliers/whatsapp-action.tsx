"use client"

import { MessageCircle, PhoneOff } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EXTERNAL_LINK_REL, whatsAppUrl } from "@/lib/whatsapp"
import { cn } from "@/lib/utils"

/**
 * « Contacter par WhatsApp », or the reason there is no such action — AC-3, in one component.
 *
 * <p><b>The two states are the whole point.</b> A fournisseur with a deliverable Tunisian number gets a link
 * that opens `wa.me` in a new tab; one without gets « Ajouter un numéro », which opens the edit form. What it is
 * never allowed to be is a <i>disabled</i> button or an absent control: a greyed icon says « this is broken »
 * and an absent one says nothing at all, while the real situation — nobody has recorded a number yet — is
 * something the user can fix in ten seconds if they are told.</p>
 *
 * <p>⚠️ <b>Deliverability is the server's answer, not this component's.</b> Callers pass the `phoneE164` the API
 * resolved; `whatsAppUrl` re-derives the URL from it but the decision was already made server-side, so the stock
 * table, the bell row, the laboratory board and the fournisseurs list cannot disagree about whether one supplier
 * is reachable.</p>
 */
interface WhatsAppActionProps {
  /** The server-resolved E.164 number, or null/undefined when there is none. */
  phoneE164: string | null | undefined
  /** Named in the accessible label, so a row of these is distinguishable to a screen reader. */
  contactName: string
  /** Pre-filled message. Omitted for a plain « open a conversation » (AC-3). */
  message?: string | null
  /** What « Ajouter un numéro » does. Omit to render nothing at all in that state. */
  onAddNumber?: () => void
  /** `icon` for a dense table cell, `default` for a card where the verb should be readable. */
  variant?: "icon" | "default"
  className?: string
}

export function WhatsAppAction({
  phoneE164,
  contactName,
  message,
  onAddNumber,
  variant = "icon",
  className,
}: WhatsAppActionProps) {
  const href = whatsAppUrl(phoneE164, message)

  if (!href) {
    if (!onAddNumber) return null

    return (
      <Button
        type="button"
        variant="ghost"
        size={variant === "icon" ? "icon" : "sm"}
        // These sit in a row beside « Modifier » and a « ⋯ » trigger, so the control grows its own box rather
        // than using `.touch-target` — an overlay would overhang and, painting last, steal its neighbours' taps.
        className={cn(variant === "icon" ? "coarse:size-11" : "coarse:h-11", className)}
        onClick={onAddNumber}
      >
        <PhoneOff aria-hidden="true" className="size-4" />
        {variant === "default" ? <span className="ms-2">Ajouter un numéro</span> : null}
        <span className="sr-only">Ajouter un numéro pour {contactName}</span>
      </Button>
    )
  }

  return (
    <Button
      asChild
      variant="ghost"
      size={variant === "icon" ? "icon" : "sm"}
      className={cn(
        "text-success hover:text-success",
        variant === "icon" ? "coarse:size-11" : "coarse:h-11",
        className,
      )}
    >
      <a href={href} target="_blank" rel={EXTERNAL_LINK_REL}>
        <MessageCircle aria-hidden="true" className="size-4" />
        {variant === "default" ? <span className="ms-2">WhatsApp</span> : null}
        <span className="sr-only">Contacter {contactName} par WhatsApp</span>
      </a>
    </Button>
  )
}
