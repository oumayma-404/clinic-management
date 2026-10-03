"use client"

import { Phone } from "lucide-react"
import { Button } from "@/components/ui/button"
import { WhatsAppAction } from "@/components/suppliers/whatsapp-action"
import { cn } from "@/lib/utils"

/**
 * « Appeler » + « WhatsApp » for one patient — both from the stored E.164 number, both absent without one
 * (never disabled: a greyed control reads as broken). WhatsApp opens an empty chat (`lib/whatsapp.ts`).
 */
export function ContactActions({
  phoneE164,
  name,
  variant = "icon",
  className,
}: {
  phoneE164: string | null
  name: string
  /** `icon` in a table row; `default` on a card or the panel, where the verb should be readable. */
  variant?: "icon" | "default"
  className?: string
}) {
  if (!phoneE164) return null

  return (
    <div className={cn("flex items-center gap-1.5", variant === "default" && "flex-wrap", className)}>
      <Button
        asChild
        variant={variant === "icon" ? "ghost" : "outline"}
        size={variant === "icon" ? "icon" : "sm"}
        // Siblings a few pixels apart: grow the box, never `.touch-target`, or one steals the other's taps.
        className={variant === "icon" ? "coarse:size-11" : "coarse:h-11"}
      >
        <a href={`tel:${phoneE164}`} aria-label={`Appeler ${name}`} onClick={(e) => e.stopPropagation()}>
          <Phone className="size-4" aria-hidden="true" />
          {variant === "default" ? "Appeler" : null}
        </a>
      </Button>
      <WhatsAppAction
        phoneE164={phoneE164}
        contactName={name}
        variant={variant}
        className={variant === "default" ? "border" : undefined}
      />
    </div>
  )
}
