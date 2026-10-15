"use client"

import Link from "next/link"
import { Building2, Headset, UserPlus, Users } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { useSession } from "@/lib/auth/session"
import type { StaffConversationDto } from "@/lib/api/staff-chat"
import { formatChatTime, previewOf } from "@/lib/staff-chat/format"
import { cn } from "@/lib/utils"
import { StaffChatCount } from "./staff-chat-launcher"

interface ConversationListProps {
  conversations: StaffConversationDto[] | undefined
  loading: boolean
  failed: boolean
  onRetry: () => void
  onOpen: (key: string) => void
  /** The signed-in person; a two-person thread shows the OTHER member's presence. */
  viewerId: string | undefined
}

/** The server orders them: threads with messages newest first, then shared threads, then colleagues A→Z (AC-1.3). */
export function ConversationList({ conversations, loading, failed, onRetry, onOpen, viewerId }: ConversationListProps) {
  const isAdmin = useSession().user?.role === "admin"

  if (failed) {
    return (
      <div className="p-4">
        <LoadFailureNotice message="Les conversations n'ont pas pu être chargées." onRetry={onRetry} />
      </div>
    )
  }

  if (loading || !conversations) {
    return (
      <div className="space-y-3 p-4" aria-busy="true" aria-label="Chargement des conversations">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="flex items-center gap-3">
            <div className="size-9 animate-pulse rounded-full bg-muted" />
            <div className="flex-1 space-y-1.5">
              <div className="h-3.5 w-1/2 animate-pulse rounded bg-muted" />
              <div className="h-3 w-3/4 animate-pulse rounded bg-muted" />
            </div>
          </div>
        ))}
      </div>
    )
  }

  const hasColleague = conversations.some((c) => c.kind === "direct")
  if (!hasColleague) {
    return (
      <EmptyState
        size="compact"
        icon={Users}
        title="Aucun collègue pour l'instant"
        action={
          isAdmin ? (
            <Button asChild variant="outline" size="sm">
              <Link href="/users">
                <UserPlus className="h-4 w-4" aria-hidden="true" />
                Créer un compte
              </Link>
            </Button>
          ) : undefined
        }
      />
    )
  }

  return (
    <ul className="min-h-0 flex-1 overflow-y-auto py-1" aria-label="Conversations">
      {conversations.map((c) => (
        <li key={c.key}>
          <ConversationRow conversation={c} onOpen={onOpen} viewerId={viewerId} />
        </li>
      ))}
    </ul>
  )
}

function ConversationRow({
  conversation: c,
  onOpen,
  viewerId,
}: {
  conversation: StaffConversationDto
  onOpen: (key: string) => void
  viewerId: string | undefined
}) {
  const preview = c.lastMessage ? previewOf(c.lastMessage) : "Aucun message"
  const unread = c.unreadCount > 0
  const other = c.kind === "direct" ? c.members.find((m) => m.userId !== viewerId) : undefined

  return (
    <button
      type="button"
      onClick={() => onOpen(c.key)}
      className="flex w-full items-center gap-3 px-4 py-2.5 text-start hover:bg-accent/60 focus-visible:bg-accent/60 coarse:min-h-14"
    >
      <span className="relative shrink-0">
        {c.kind === "direct" ? (
          <InitialsAvatar name={c.title} className="size-9 text-xs" />
        ) : (
          <span aria-hidden="true" className="flex size-9 items-center justify-center rounded-full bg-primary/10 text-primary">
            {c.kind === "clinic" ? <Building2 className="h-4 w-4" /> : <Headset className="h-4 w-4" />}
          </span>
        )}
        {other?.online && (
          <span aria-hidden="true" className="absolute -bottom-0.5 -end-0.5 size-3 rounded-full border-2 border-card bg-success" />
        )}
      </span>

      <span className="min-w-0 flex-1">
        <span className="flex items-baseline gap-2">
          <span className={cn("min-w-0 flex-1 truncate text-sm", unread ? "font-semibold" : "font-medium")}>{c.title}</span>
          {c.lastMessage && (
            <span className="shrink-0 text-2xs text-muted-foreground tabular-nums">{formatChatTime(c.lastMessage.sentAt)}</span>
          )}
        </span>
        <span className="flex items-center gap-2">
          <span className={cn("min-w-0 flex-1 truncate text-xs", unread ? "text-foreground" : "text-muted-foreground")}>
            {c.readOnly ? "Compte désactivé" : preview}
          </span>
          <StaffChatCount total={c.unreadCount} urgent={c.hasUrgentUnread} />
        </span>
      </span>
      <span className="sr-only">
        {[
          other?.online ? "en ligne" : null,
          unread ? `${c.unreadCount} non ${c.unreadCount > 1 ? "lus" : "lu"}${c.hasUrgentUnread ? ", dont un urgent" : ""}` : null,
        ]
          .filter(Boolean)
          .join(", ")}
      </span>
    </button>
  )
}

