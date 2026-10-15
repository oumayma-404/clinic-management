"use client"

import { useState, type ReactNode } from "react"
import { BellRing, Loader2 } from "lucide-react"
import { toast } from "sonner"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { DropdownMenuItem } from "@/components/ui/dropdown-menu"
import { ApiError } from "@/lib/api/client"
import { StaffChatCode } from "@/lib/api/staff-chat"
import type { AppointmentDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { useArrivalNotice } from "@/lib/hooks/use-arrival-notice"
import { formatChatTime } from "@/lib/staff-chat/format"
import { cn } from "@/lib/utils"
import { useStaffChat } from "./staff-chat-provider"

export interface ArrivalControl {
  label: string
  /** The server's reason this one cannot go (AC-3.4) — shown, never hidden. */
  unavailable: string | null
  sending: boolean
  press: () => void
}

/**
 * « Signaler l'arrivée » (US-3) for one appointment. The state lives in the HOST, not in the menu: the menu closes
 * before the confirmation opens, and a confirmation owned by the menu's content would unmount with it.
 *
 * `control` is null when there is nothing to offer — not today, cancelled, no patient, or the chat is unavailable.
 * `active` is the host's menu being open: the shared read only runs then, so a week of agenda blocks costs nothing.
 */
export function useArrivalAction(appointment: AppointmentDto | null, active: boolean, onClose: () => void): {
  control: ArrivalControl | null
  dialog: ReactNode
} {
  const { available } = useStaffChat()
  const { arrivalFor, send } = useArrivalNotice(available && active)
  const [sending, setSending] = useState(false)
  const [confirm, setConfirm] = useState<string | null>(null)

  const arrival = available && appointment ? arrivalFor(appointment.id) : undefined
  const announced =
    arrival?.sentAt && arrival.sentBy ? `Signalé à ${formatChatTime(arrival.sentAt)} par ${arrival.sentBy.name}` : null

  const go = async (resend: boolean) => {
    if (!appointment) return
    setSending(true)
    try {
      const message = await send(appointment.id, resend)
      setConfirm(null)
      onClose()
      const dentist = message.appointment?.dentistName
      toast.success(dentist ? `Arrivée signalée au ${dentist}` : "Arrivée signalée")
    } catch (err) {
      // Someone announced it between the read and the press: say who, and ask (EC-5).
      if (err instanceof ApiError && err.code === StaffChatCode.ArrivalAlreadySent) {
        onClose()
        setConfirm(err.message)
      } else {
        setConfirm(null)
        showErrorToast(err, "L'arrivée n'a pas pu être signalée.")
      }
    } finally {
      setSending(false)
    }
  }

  const control: ArrivalControl | null = arrival
    ? {
        label: announced ?? "Signaler l'arrivée",
        unavailable: arrival.unavailable,
        sending,
        press: () => {
          if (sending) return
          if (announced) {
            onClose()
            setConfirm(`${announced}.`)
          } else {
            void go(false)
          }
        },
      }
    : null

  const dialog = (
    <AlertDialog open={confirm !== null} onOpenChange={(open) => !open && !sending && setConfirm(null)}>
      <AlertDialogContent onClick={(event) => event.stopPropagation()}>
        <AlertDialogHeader>
          <AlertDialogTitle>Signaler à nouveau l&apos;arrivée de {appointment?.patientName} ?</AlertDialogTitle>
          <AlertDialogDescription>{confirm}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={sending}>Non</AlertDialogCancel>
          <AlertDialogAction
            disabled={sending}
            onClick={(event) => {
              event.preventDefault()
              void go(true)
            }}
          >
            {sending ? "Envoi…" : "Signaler à nouveau"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )

  return { control, dialog }
}

/** The agenda « ⋯ » popover's row. */
export function ArrivalPopoverButton({ control }: { control: ArrivalControl }) {
  return (
    <div>
      <Button
        variant="ghost"
        className="h-auto min-h-9 w-full justify-start whitespace-normal py-1.5 text-start coarse:min-h-11"
        disabled={Boolean(control.unavailable) || control.sending}
        onClick={control.press}
      >
        {control.sending ? (
          <Loader2 className="size-4 shrink-0 animate-spin" aria-hidden="true" />
        ) : (
          <BellRing className="size-4 shrink-0" aria-hidden="true" />
        )}
        {control.label}
      </Button>
      {control.unavailable && <p className="px-3 pb-1.5 text-2xs text-muted-foreground">{control.unavailable}</p>}
    </div>
  )
}

/** The edit dialog's « ⋯ » menu row. */
export function ArrivalMenuItem({ control }: { control: ArrivalControl }) {
  const blocked = Boolean(control.unavailable)
  return (
    <DropdownMenuItem
      disabled={blocked || control.sending}
      onSelect={control.press}
      // A refused arrival still reads in full: the reason is the point of keeping it (AC-3.4).
      className={cn("items-start", blocked && "data-[disabled]:opacity-100")}
    >
      <BellRing className="mt-0.5 h-4 w-4" aria-hidden="true" />
      <span className="flex min-w-0 flex-col">
        <span className={cn(blocked && "text-muted-foreground")}>{control.label}</span>
        {control.unavailable && <span className="text-2xs text-muted-foreground">{control.unavailable}</span>}
      </span>
    </DropdownMenuItem>
  )
}
