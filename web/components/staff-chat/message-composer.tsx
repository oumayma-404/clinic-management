"use client"

import { useEffect, useId, useRef, useState } from "react"
import { SendHorizontal } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { STAFF_MESSAGE_MAX_LENGTH } from "@/lib/api/staff-chat"
import type { OutgoingMessage } from "@/lib/hooks/use-staff-chat"
import { readDraft, writeDraft } from "@/lib/staff-chat/drafts"
import { cn } from "@/lib/utils"

interface MessageComposerProps {
  conversation: string
  viewerId: string | undefined
  /** Who the message goes to, for the field's accessible name. */
  recipient: string
  onSend: (message: OutgoingMessage) => void
  /** Sits under the quick messages, which already draw the separating line. */
  flush?: boolean
}

/** Counter shows from here, so a long message is never refused by surprise. */
const COUNTER_FROM = STAFF_MESSAGE_MAX_LENGTH - 200

export function MessageComposer({ conversation, viewerId, recipient, onSend, flush = false }: MessageComposerProps) {
  const [text, setText] = useState(() => readDraft(viewerId, conversation))
  const fieldId = useId()
  const counterId = useId()
  const fieldRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    writeDraft(viewerId, conversation, text)
  }, [viewerId, conversation, text])

  const length = text.trim().length
  const tooLong = length > STAFF_MESSAGE_MAX_LENGTH
  const canSend = length > 0 && !tooLong

  const submit = () => {
    if (!canSend) return
    onSend({ body: text.trim(), quickMessage: null })
    setText("")
    fieldRef.current?.focus()
  }

  return (
    <form
      className={cn("shrink-0 p-2", flush ? "pt-1" : "border-t border-border")}
      onSubmit={(event) => {
        event.preventDefault()
        submit()
      }}
    >
      <label htmlFor={fieldId} className="sr-only">
        {`Message à ${recipient}`}
      </label>
      <div className="flex items-end gap-2">
        <Textarea
          id={fieldId}
          ref={fieldRef}
          value={text}
          rows={1}
          placeholder="Écrire un message"
          aria-describedby={length >= COUNTER_FROM ? counterId : undefined}
          aria-invalid={tooLong || undefined}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={(event) => {
            // A finger has no Shift: on touch, Enter is a new line and the button sends.
            if (event.key !== "Enter" || event.shiftKey || event.nativeEvent.isComposing) return
            if (!window.matchMedia("(pointer: fine)").matches) return
            event.preventDefault()
            submit()
          }}
          className="max-h-40 min-h-10 resize-none"
        />
        <Button type="submit" size="icon" className="shrink-0 coarse:size-11" disabled={!canSend} aria-label="Envoyer">
          <SendHorizontal className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>
      {length >= COUNTER_FROM && (
        <p
          id={counterId}
          className={cn("px-1 pt-1 text-end text-2xs tabular-nums", tooLong ? "text-destructive" : "text-muted-foreground")}
        >
          {tooLong
            ? `${length} / ${STAFF_MESSAGE_MAX_LENGTH} — trop long de ${length - STAFF_MESSAGE_MAX_LENGTH} caractères`
            : `${length} / ${STAFF_MESSAGE_MAX_LENGTH}`}
        </p>
      )}
    </form>
  )
}
