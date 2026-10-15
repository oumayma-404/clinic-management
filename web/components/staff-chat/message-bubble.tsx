"use client"

import { AlertTriangle, Loader2 } from "lucide-react"
import type { ThreadMessage } from "@/lib/hooks/use-staff-chat"
import { formatChatTime } from "@/lib/staff-chat/format"
import { cn } from "@/lib/utils"

interface MessageBubbleProps {
  message: ThreadMessage
  mine: boolean
  /** On a shared conversation each colleague's message names its author. */
  showSender: boolean
  onRetry: (localId: string) => void
}

export function MessageBubble({ message, mine, showSender, onRetry }: MessageBubbleProps) {
  const quick = message.quickMessage
  const text = message.body ?? quick?.label ?? ""
  const localId = message.localId

  return (
    <div className={cn("flex flex-col gap-0.5", mine ? "items-end" : "items-start")}>
      {showSender && !mine && <span className="px-1 text-2xs font-medium text-muted-foreground">{message.sender.name}</span>}
      <div
        className={cn(
          "max-w-[85%] rounded-2xl px-3 py-2 text-sm",
          mine ? "rounded-ee-sm bg-primary/10 text-foreground" : "rounded-es-sm bg-muted text-foreground",
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
        <p className="whitespace-pre-wrap [overflow-wrap:anywhere]">{text}</p>
      </div>
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
      </span>
    </div>
  )
}
