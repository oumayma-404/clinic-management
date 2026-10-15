"use client"

import { useEffect, useId, useMemo, useRef } from "react"
import { ArrowLeft, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { useStaffChatConversations } from "@/lib/hooks/use-staff-chat"
import { ConversationList } from "./conversation-list"
import { MessageThread } from "./message-thread"
import { useStaffChat } from "./staff-chat-provider"

/**
 * « Messages » — a panel over the right edge of the page, never a modal: the page behind keeps what was typed in it
 * (AC-1.2). Below `md:` and at 200 % zoom it takes the whole screen. Escape or ✕ closes it (AC-1.5).
 */
export function StaffChatPanel() {
  const { conversation, selectConversation, closePanel } = useStaffChat()
  const titleId = useId()
  const titleRef = useRef<HTMLHeadingElement>(null)
  const viewer = useUserStatus().data?.user
  const conversations = useStaffChatConversations(true)

  const current = useMemo(
    () => conversations.data?.find((c) => c.key === conversation) ?? null,
    [conversations.data, conversation],
  )

  // Focus lands on the title, not a field — the keyboard would otherwise cover what was opened to be read.
  useEffect(() => {
    titleRef.current?.focus()
  }, [conversation])

  const title = current ? current.title : "Messages"

  return (
    <aside
      aria-labelledby={titleId}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.stopPropagation()
          closePanel()
        }
      }}
      className="fixed inset-y-0 end-0 z-40 flex h-dvh w-full flex-col border-s border-border bg-card shadow-xl md:w-[24rem] print:hidden"
    >
      <header className="flex h-14 shrink-0 items-center gap-1 border-b border-border px-2">
        {conversation && (
          <Button
            variant="ghost"
            size="icon"
            className="coarse:size-11"
            aria-label="Retour aux conversations"
            onClick={() => selectConversation(null)}
          >
            <ArrowLeft className="h-5 w-5" aria-hidden="true" />
          </Button>
        )}
        <h2
          id={titleId}
          ref={titleRef}
          tabIndex={-1}
          className="min-w-0 flex-1 truncate px-1 text-base font-semibold outline-none"
        >
          {title}
        </h2>
        <Button variant="ghost" size="icon" className="coarse:size-11" aria-label="Fermer les messages" onClick={closePanel}>
          <X className="h-5 w-5" aria-hidden="true" />
        </Button>
      </header>

      {conversation ? (
        <MessageThread
          key={conversation}
          conversationKey={conversation}
          conversation={current}
          viewer={viewer ? { userId: viewer.id, name: viewerName(viewer) } : null}
        />
      ) : (
        <ConversationList
          conversations={conversations.data}
          loading={conversations.isLoading}
          failed={conversations.isError && !conversations.data}
          onRetry={() => void conversations.refetch()}
          onOpen={selectConversation}
          viewerId={viewer?.id}
        />
      )}
    </aside>
  )
}

function viewerName(user: { fullName?: string | null; email?: string | null }): string {
  return user.fullName?.trim() || user.email?.trim() || "Moi"
}
