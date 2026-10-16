"use client"

import { AlertTriangle, CheckCircle2, Hand, Loader2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { ThreadMessage } from "@/lib/hooks/use-staff-chat"
import type { ClaimView } from "@/lib/staff-chat/desk"
import { PatientLinkChips } from "./patient-link-chip"
import { formatChatTime } from "@/lib/staff-chat/format"
import { personToneClass } from "@/lib/staff-chat/person-tone"
import { cn } from "@/lib/utils"

interface MessageBubbleProps {
  message: ThreadMessage
  mine: boolean
  /** The author's colour — the same person is the same colour on every screen, mine included. */
  tone: number | null | undefined
  /** On a shared conversation each colleague's message names its author. */
  showSender: boolean
  onRetry: (localId: string) => void
  /** « Je m'en occupe » on a secretariat request (FR-6); null where it does not apply. */
  claim?: ClaimView
  claimBusy?: boolean
  onClaim?: () => void
  onRelease?: () => void
  /** « Vu » / « Vu par … » under the last of my messages (AC-5.4). */
  seen?: string | null
}

export function MessageBubble({
  message,
  mine,
  tone,
  showSender,
  onRetry,
  claim = null,
  claimBusy = false,
  onClaim,
  onRelease,
  seen = null,
}: MessageBubbleProps) {
  const quick = message.quickMessage
  const text = message.body ?? quick?.label ?? ""
  const localId = message.localId

  return (
    <div className={cn("flex flex-col gap-0.5", mine ? "items-end" : "items-start")}>
      {showSender && !mine && <span className="px-1 text-2xs font-medium text-muted-foreground">{message.sender.name}</span>}
      <div
        className={cn(
          "max-w-[85%] rounded-2xl px-3 py-2 text-sm text-foreground",
          mine ? "rounded-ee-sm" : "rounded-es-sm",
          personToneClass(tone),
          quick?.urgent && "ring-1 ring-destructive/40",
          message.state === "failed" && "ring-1 ring-destructive",
        )}
      >
        {quick?.urgent && (
          <span className="mb-0.5 flex items-center gap-1 text-2xs font-semibold uppercase tracking-wide text-destructive">
            <AlertTriangle className="h-3 w-3" aria-hidden="true" />
            Urgent
          </span>
        )}
        {quick && message.body && <p className="font-medium [overflow-wrap:anywhere]">{quick.label}</p>}
        <p className="whitespace-pre-wrap [overflow-wrap:anywhere]">{text}</p>
      </div>

      <PatientLinkChips
        patient={message.patient}
        appointment={message.appointment}
        className={cn("max-w-[85%]", mine && "justify-end")}
      />

      {claim && (
        <ClaimLine claim={claim} busy={claimBusy} onClaim={onClaim} onRelease={onRelease} />
      )}

      <span className="flex items-center gap-1 px-1 text-2xs text-muted-foreground tabular-nums">
        {message.state === "sending" && (
          <>
            <Loader2 className="h-3 w-3 motion-safe:animate-spin" aria-hidden="true" />
            Envoi…
          </>
        )}
        {message.state === "failed" && localId && (
          <span role="alert" className="flex flex-wrap items-center gap-1 text-destructive">
            Non envoyé
            <span aria-hidden="true">·</span>
            <button
              type="button"
              onClick={() => onRetry(localId)}
              className="font-medium underline underline-offset-2 coarse:min-h-11 coarse:px-2"
            >
              Réessayer
            </button>
          </span>
        )}
        {message.state === "sent" && formatChatTime(message.sentAt)}
        {message.state === "sent" && seen && (
          <>
            <span aria-hidden="true">·</span>
            <span>{seen}</span>
          </>
        )}
      </span>
    </div>
  )
}

function ClaimLine({
  claim,
  busy,
  onClaim,
  onRelease,
}: {
  claim: NonNullable<ClaimView>
  busy: boolean
  onClaim?: () => void
  onRelease?: () => void
}) {
  if (claim.kind === "offer") {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={onClaim}
        disabled={busy}
        className="h-8 rounded-full px-3 text-xs coarse:min-h-11"
      >
        {busy ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" /> : <Hand className="h-3.5 w-3.5" aria-hidden="true" />}
        Je m&apos;en occupe
      </Button>
    )
  }

  if (claim.kind === "mine") {
    return (
      <span className="flex flex-wrap items-center gap-x-2 px-1 text-xs">
        <span className="flex items-center gap-1 font-medium text-success">
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          Vous vous en occupez
        </span>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRelease}
          disabled={busy}
          className="h-7 px-2 text-xs text-muted-foreground coarse:min-h-11"
        >
          Je ne peux plus
        </Button>
      </span>
    )
  }

  return (
    <span className="flex items-center gap-1 px-1 text-xs font-medium text-success">
      <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
      Pris par {claim.name} · <span className="tabular-nums">{formatChatTime(claim.at)}</span>
    </span>
  )
}
