"use client"

import { MessagesSquare } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { useStaffChatUnread } from "@/lib/hooks/use-staff-chat"
import { useStaffChat } from "./staff-chat-provider"

/** « Messages, 3 non lus dont un urgent » — what both launchers announce. */
export function staffChatLabel(total: number, urgent: boolean): string {
  if (total === 0) return "Messages"
  const count = `${total} non ${total > 1 ? "lus" : "lu"}`
  return urgent ? `Messages, ${count} dont un urgent` : `Messages, ${count}`
}

/** The count, with « ! » before it when something urgent waits — never colour alone (AC-1.1). */
export function StaffChatCount({ total, urgent, className }: { total: number; urgent: boolean; className?: string }) {
  if (total === 0) return null
  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-destructive px-1 text-2xs font-semibold leading-none text-white",
        urgent && "ring-2 ring-destructive/40",
        className,
      )}
    >
      {urgent && "!"}
      {total > 99 ? "99+" : total}
    </span>
  )
}

/** The header's « Messages » button, from 768 px; below that the bottom bar carries it (spec Device table). */
export function StaffChatLauncher() {
  const { available, isOpen, openPanel, closePanel } = useStaffChat()
  const unread = useStaffChatUnread(available).data
  if (!available) return null

  const total = unread?.total ?? 0
  const urgent = unread?.hasUrgent ?? false

  return (
    <Button
      variant="ghost"
      size="icon"
      className="relative hidden coarse:size-11 md:inline-flex"
      aria-label={staffChatLabel(total, urgent)}
      aria-expanded={isOpen}
      onClick={() => (isOpen ? closePanel() : openPanel())}
    >
      <MessagesSquare className="h-5 w-5" aria-hidden="true" />
      <StaffChatCount total={total} urgent={urgent} className="absolute -right-1 -top-1" />
    </Button>
  )
}

/** The bottom bar's « Messages » tab, below 768 px — shaped like its « Plus » neighbour. */
export function StaffChatBarTab() {
  const { available, isOpen, openPanel, closePanel } = useStaffChat()
  const unread = useStaffChatUnread(available).data
  if (!available) return null

  const total = unread?.total ?? 0
  const urgent = unread?.hasUrgent ?? false

  return (
    <button
      type="button"
      onClick={() => (isOpen ? closePanel() : openPanel())}
      aria-label={staffChatLabel(total, urgent)}
      aria-expanded={isOpen}
      className={cn(
        "flex flex-1 flex-col items-center justify-center gap-0.5 text-2xs font-medium transition-colors",
        "h-[var(--bottom-bar-h)]",
        isOpen ? "text-primary" : "text-muted-foreground",
      )}
    >
      <span className="relative">
        <MessagesSquare className="h-5 w-5" aria-hidden="true" />
        <StaffChatCount total={total} urgent={urgent} className="absolute -end-3 -top-2" />
      </span>
      <span className="whitespace-nowrap leading-none">Messages</span>
    </button>
  )
}
