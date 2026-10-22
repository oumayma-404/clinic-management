"use client"

import { useState } from "react"
import { createPortal } from "react-dom"
import { AlertTriangle, Loader2, MessageSquare, Volume2, X } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import type { StaffQuickMessageDto } from "@/lib/api/staff-chat"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { useStaffQuickMessages } from "@/lib/hooks/use-staff-chat"
import {
  dismissNotice,
  holdNotice,
  noticeText,
  replyFromNotice,
  useAlertPreferences,
  useStaffChatNotices,
  type StaffNotice,
} from "@/lib/staff-chat/alerts"
import { cn } from "@/lib/utils"
import { useStaffChat } from "./staff-chat-provider"

/**
 * The chat's notices (staff-chat AC-4.1, AC-4.2): at the top — the bottom is where a sheet keeps « Enregistrer » —
 * full width on a phone, centred on a tablet, top-right on a desk. Above every dialog and never taking focus, so a
 * reply goes from here without closing or covering what is open. Not drawn while the lock screen is up (AC-4.7).
 */
export function StaffChatNotices() {
  const { locked } = useSession()
  const { isOpen } = useStaffChat()
  const notices = useStaffChatNotices()
  const quick = useStaffQuickMessages(notices.length > 0)
  const replies = (quick.data?.items ?? []).filter((item) => item.for === "reply").slice(0, 2)

  if (locked || notices.length === 0 || typeof document === "undefined") return null

  return createPortal(
    <div
      className={cn(
        "pointer-events-none fixed inset-x-4 z-[60] flex flex-col gap-2 print:hidden",
        isOpen
          ? // Never over the open panel's header (back, ✕): under it on a phone, beside it from `md:`.
            "top-[4.25rem] md:inset-x-auto md:top-3 md:right-[25rem] md:w-[22rem]"
          : "top-[max(0.75rem,env(safe-area-inset-top,0px))] sm:inset-x-auto sm:left-1/2 sm:w-[22rem] sm:-translate-x-1/2 lg:left-auto lg:right-4 lg:translate-x-0",
      )}
    >
      {notices.map((notice) => (
        <NoticeCard key={notice.message.id} notice={notice} replies={replies} />
      ))}
    </div>,
    document.body,
  )
}

function NoticeCard({ notice, replies }: { notice: StaffNotice; replies: StaffQuickMessageDto[] }) {
  const { openConversation } = useStaffChat()
  const { audioBlocked, enableAudio } = useAlertPreferences()
  const [sending, setSending] = useState<string | null>(null)
  const { message, urgent } = notice
  const sender = message.sender.name

  const reply = async (item: StaffQuickMessageDto) => {
    setSending(item.key)
    try {
      await replyFromNotice(message, item)
      toast.success(`Envoyé à ${sender} : ${item.label}`)
    } catch (err) {
      showErrorToast(err, "La réponse n'a pas pu être envoyée.")
    } finally {
      setSending(null)
    }
  }

  return (
    <div
      data-staff-chat-notice=""
      role={urgent ? "alert" : "status"}
      // A press here must not move focus out of what the person was typing in (AC-4.2).
      onMouseDown={(event) => event.preventDefault()}
      onMouseEnter={() => holdNotice(message.id, true)}
      onMouseLeave={() => holdNotice(message.id, false)}
      onFocus={() => holdNotice(message.id, true)}
      onBlur={() => holdNotice(message.id, false)}
      onKeyDown={(event) => {
        if (event.key === "Escape") dismissNotice(message.id)
      }}
      className={cn(
        "pointer-events-auto rounded-lg border bg-card p-3 shadow-lg",
        urgent ? "border-destructive/60 ring-2 ring-destructive/30" : "border-border",
      )}
    >
      <div className="flex items-start gap-2">
        <span
          className={cn(
            "mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg",
            urgent ? "bg-destructive-wash text-destructive" : "bg-primary/10 text-primary",
          )}
          aria-hidden="true"
        >
          {urgent ? <AlertTriangle className="size-4" /> : <MessageSquare className="size-4" />}
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold">{sender}</p>
          <p className={cn("text-sm", urgent ? "font-medium text-destructive" : "text-muted-foreground")}>
            {noticeText(message)}
          </p>
        </div>
        <Button
          variant="ghost"
          size="icon"
          className="-me-1 -mt-1 size-8 shrink-0 coarse:size-11"
          aria-label={`Fermer la notification de ${sender}`}
          onClick={() => dismissNotice(message.id)}
        >
          <X className="size-4" aria-hidden="true" />
        </Button>
      </div>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <Button
          size="sm"
          className="coarse:min-h-11"
          onClick={() => {
            dismissNotice(message.id)
            openConversation(message.conversation)
          }}
        >
          Ouvrir
        </Button>
        {replies.map((item) => (
          <Button
            key={item.key}
            size="sm"
            variant="outline"
            className="coarse:min-h-11"
            disabled={sending !== null}
            onClick={() => void reply(item)}
          >
            {sending === item.key && <Loader2 className="size-3.5 animate-spin" aria-hidden="true" />}
            {item.label}
          </Button>
        ))}
        {audioBlocked && (
          <Button size="sm" variant="ghost" className="coarse:min-h-11" onClick={() => void enableAudio()}>
            <Volume2 className="size-3.5" aria-hidden="true" />
            Activer le son
          </Button>
        )}
      </div>
    </div>
  )
}
