"use client"

import { useMemo, useState } from "react"
import { AlertTriangle, Pencil } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { StaffConversationDto, StaffQuickMessageDto } from "@/lib/api/staff-chat"
import { audiencesFor } from "@/lib/staff-chat/quick-messages"
import { cn } from "@/lib/utils"

interface QuickMessageBarProps {
  conversation: StaffConversationDto
  viewerId: string | undefined
  items: StaffQuickMessageDto[] | undefined
  onSend: (item: StaffQuickMessageDto) => void
  onEdit: () => void
}

/**
 * The quick messages under a conversation, one press each (AC-2.1). The order is the person's own (AC-2.2), frozen
 * while the thread is open so a send never moves the next button under the finger; replies always come last.
 */
export function QuickMessageBar({ conversation, viewerId, items, onSend, onEdit }: QuickMessageBarProps) {
  const [frozenOrder, setFrozenOrder] = useState<Map<string, number> | null>(null)
  if (items && !frozenOrder) {
    setFrozenOrder(new Map(items.map((item, index) => [item.key, index])))
  }

  const shown = useMemo(() => {
    if (!items) return []
    const audiences = audiencesFor(conversation, viewerId)
    const rank = (item: StaffQuickMessageDto) => frozenOrder?.get(item.key) ?? Number.MAX_SAFE_INTEGER
    return items
      .filter((item) => audiences.includes(item.for))
      .sort((a, b) => Number(a.for === "reply") - Number(b.for === "reply") || rank(a) - rank(b))
  }, [items, conversation, viewerId, frozenOrder])

  if (!items) return null

  return (
    <div className="flex shrink-0 items-start gap-1 border-t border-border px-2 pt-2">
      <ul
        aria-label="Messages rapides"
        className={cn(
          "flex min-w-0 flex-1 gap-1.5",
          // A phone scrolls one row; the panel wraps and scrolls its own two-and-a-half rows.
          "overflow-x-auto pb-1 [scrollbar-width:thin] md:max-h-[5.75rem] md:flex-wrap md:overflow-y-auto md:overflow-x-visible",
        )}
      >
        {shown.map((item) => (
          <li key={item.key} className="shrink-0">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => onSend(item)}
              className={cn(
                "h-8 whitespace-nowrap rounded-full px-3 text-xs font-medium coarse:min-h-11",
                item.urgent && "border-destructive/50 text-destructive hover:bg-destructive/10 hover:text-destructive",
              )}
            >
              {item.urgent && <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />}
              {item.label}
              {item.urgent && <span className="sr-only"> (urgent)</span>}
            </Button>
          </li>
        ))}
      </ul>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        onClick={onEdit}
        aria-label="Modifier les messages rapides"
        className="size-8 shrink-0 text-muted-foreground coarse:size-11"
      >
        <Pencil className="h-4 w-4" aria-hidden="true" />
      </Button>
    </div>
  )
}
