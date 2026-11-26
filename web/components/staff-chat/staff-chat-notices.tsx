"use client"

import { useState } from "react"
import { createPortal } from "react-dom"
import { useRouter } from "next/navigation"
import { AlertTriangle, BellRing, Loader2, MessageSquare, Volume2, X } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { useClockStatus } from "@/components/team/clock-in-gate"
import type { StaffQuickMessageDto } from "@/lib/api/staff-chat"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { useStaffQuickMessages } from "@/lib/hooks/use-staff-chat"
import { runBoardAction } from "@/lib/hooks/use-waiting-room"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { notHereSentence } from "@/lib/waiting-room"
import {
  dismissNotice,
  holdNotice,
  isCallIn,
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
  const { covering: clockInLock } = useClockStatus()
  const { isOpen } = useStaffChat()
  const notices = useStaffChatNotices()
  const quick = useStaffQuickMessages(notices.length > 0)
  const replies = (quick.data?.items ?? []).filter((item) => item.for === "reply").slice(0, 2)

  if (locked || clockInLock || notices.length === 0 || typeof document === "undefined") return null

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
  const router = useRouter()
  const { message } = notice
  const sender = message.sender.name
  // « Patient suivant » is urgent for its sound and its stay, not an emergency: it keeps the calm colours.
  const callIn = isCallIn(message)
  const urgent = notice.urgent && !callIn
  const calledName = callIn && message.patient && !message.patient.missing ? message.patient.name : null
  // « Pas là » (salle d'attente round 2, E1) belongs HERE too: this notice is where the desk answers the ring, and it
  // stays on top — the board's banner, with its own « Pas là », sits under it until it is answered.
  const calledVisit = callIn && message.appointment && !message.appointment.missing && !message.appointment.cancelled
    ? message.appointment.id
    : null

  const notHere = async (appointmentId: string) => {
    setSending("not-here")
    const change = await runBoardAction(() => waitingRoomApi.notHere(appointmentId), () => undefined, {
      message: (c) => notHereSentence(calledName, { lane: c.lane ?? "None", patientName: c.patientName }),
    })
    setSending(null)
    if (change) dismissNotice(message.id)
  }

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
        urgent
          ? "border-destructive/60 ring-2 ring-destructive/30"
          : callIn
            ? "border-primary/60 ring-2 ring-primary/30"
            : "border-border",
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
          {urgent ? (
            <AlertTriangle className="size-4" />
          ) : callIn ? (
            <BellRing className="size-4" />
          ) : (
            <MessageSquare className="size-4" />
          )}
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold">{sender}</p>
          <p className={cn("text-sm", urgent ? "font-medium text-destructive" : "text-muted-foreground")}>
            {noticeText(message)}
          </p>
          {calledName && <p className="truncate text-base font-bold text-foreground">{calledName}</p>}
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
        {callIn && (
          <Button
            size="sm"
            className="coarse:min-h-11"
            onClick={() => {
              dismissNotice(message.id)
              router.push("/salle-attente")
            }}
          >
            Voir la salle d&apos;attente
          </Button>
        )}
        {calledVisit && (
          <Button
            size="sm"
            variant="outline"
            className="coarse:min-h-11"
            disabled={sending !== null}
            onClick={() => void notHere(calledVisit)}
            aria-label={`${calledName ?? "Le patient"} n'est pas là`}
          >
            {sending === "not-here" && <Loader2 className="size-3.5 animate-spin" aria-hidden="true" />}
            Pas là
          </Button>
        )}
        <Button
          size="sm"
          variant={callIn ? "outline" : "default"}
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
