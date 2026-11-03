"use client"

import { useEffect, useId, useState } from "react"
import { usePathname } from "next/navigation"
import { Star } from "lucide-react"
import { toast } from "sonner"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { feedbackApi, type SubmitFeedbackBody } from "@/lib/api/feedback"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import {
  FEEDBACK_AREAS,
  FEEDBACK_KINDS,
  screenLabel,
  type FeedbackArea,
  type FeedbackKind,
  type FeedbackSource,
} from "@/lib/feedback"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { cn } from "@/lib/utils"

const THANKS = "Merci. Votre avis est arrivé chez APEXA."
const MAX_MESSAGE = 2000

/**
 * « Votre avis sur APEXA » (the week-one form, S12) and « Écrire à APEXA » (« Aide », S13) — one form, two
 * entries (guided-first-days E). Everything is optional; « Envoyer » works once one thing is filled.
 *
 * <p>⚠️ On the week-one form a star is already an answer: closing it any way — « Fermer », the ✕, Escape — still
 * sends the star, so the card's one tap is never lost. Typed text asks before it is thrown away
 * (`useDirtyGuard`), the star never does.</p>
 */
export function FeedbackDialog({
  open,
  onOpenChange,
  source,
  initialHelpRating = null,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  source: FeedbackSource
  /** The star tapped on the week-one card. */
  initialHelpRating?: number | null
}) {
  const weekOne = source === "semaine-1"
  const pathname = usePathname()
  const { user } = useSession()
  const ids = useId()

  const [kind, setKind] = useState<FeedbackKind>("idee")
  const [helpRating, setHelpRating] = useState<number | null>(initialHelpRating)
  const [easeRating, setEaseRating] = useState<number | null>(null)
  const [liked, setLiked] = useState<FeedbackArea[]>([])
  const [hard, setHard] = useState<FeedbackArea[]>([])
  const [message, setMessage] = useState("")
  const [mayContact, setMayContact] = useState(true)
  const [sending, setSending] = useState(false)
  // The screen it was opened from — captured on open, so navigating behind the dialog cannot change it.
  const [fromScreen, setFromScreen] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setKind("idee")
    setHelpRating(initialHelpRating)
    setEaseRating(null)
    setLiked([])
    setHard([])
    setMessage("")
    setMayContact(true)
    setFromScreen(screenLabel(pathname))
    // Only on open: the pathname moving while it is open must not reset what was typed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialHelpRating])

  const trimmed = message.trim()
  const canSend = weekOne
    ? helpRating !== null || easeRating !== null || liked.length > 0 || hard.length > 0 || trimmed.length > 0
    : trimmed.length > 0

  const body = (): SubmitFeedbackBody =>
    weekOne
      ? {
          kind: "avis",
          source,
          helpRating,
          easeRating,
          likedAreas: liked,
          hardAreas: hard,
          message: trimmed || null,
          fromScreen,
          mayContact: false,
        }
      : { kind, source, message: trimmed, fromScreen, mayContact }

  /** Every way of closing lands here — the week-one star is sent rather than lost. */
  const close = (next: boolean) => {
    if (!next && weekOne && !sending && helpRating !== null) {
      void feedbackApi
        .submit({ kind: "avis", source, helpRating, fromScreen })
        .then(() => toast.success(THANKS))
        .catch(() => {
          // The star alone: a failure here is not worth an error over a window the person chose to close.
        })
    }
    onOpenChange(next)
  }

  const guard = useDirtyGuard(open, close)

  const send = async () => {
    if (!canSend || sending) return
    setSending(true)
    try {
      await feedbackApi.submit(body())
      toast.success(THANKS)
      guard.markClean()
      onOpenChange(false)
    } catch (err) {
      showErrorToast(err)
    } finally {
      setSending(false)
    }
  }

  const messageId = `${ids}-message`
  const contactId = `${ids}-contact`

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent mobile="sheet" className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>{weekOne ? "Votre avis sur APEXA" : "Écrire à APEXA"}</DialogTitle>
            <DialogDescription>
              {weekOne ? "Tout est facultatif." : "Une idée, un problème, une question : nous lisons tout."}
            </DialogDescription>
          </DialogHeader>

          <DialogBody className="space-y-5">
            {weekOne ? (
              <>
                <StarRow label="APEXA vous aide au cabinet" value={helpRating} onChange={setHelpRating} />
                <StarRow label="Facile à utiliser" value={easeRating} onChange={setEaseRating} />
                <ChipGroup label="Ce que vous aimez le plus" selected={liked} onChange={setLiked} />
                <ChipGroup label="Ce qui a été difficile" selected={hard} onChange={setHard} />
              </>
            ) : (
              <fieldset className="space-y-2">
                <legend className="text-sm font-medium">C&apos;est…</legend>
                <div className="flex flex-wrap gap-2">
                  {FEEDBACK_KINDS.map((k) => (
                    <Chip key={k.key} pressed={kind === k.key} onClick={() => setKind(k.key)}>
                      {k.label}
                    </Chip>
                  ))}
                </div>
              </fieldset>
            )}

            <div className="space-y-2">
              <Label htmlFor={messageId}>{weekOne ? "Un mot de plus ?" : "Votre message"}</Label>
              <Textarea
                id={messageId}
                value={message}
                onChange={(e) => setMessage(e.target.value)}
                maxLength={MAX_MESSAGE}
                rows={weekOne ? 3 : 5}
                placeholder="Sans nom de patient, s'il vous plaît."
              />
              {fromScreen && !weekOne && (
                <p className="text-sm text-muted-foreground">Depuis : {fromScreen}</p>
              )}
            </div>

            {!weekOne && (
              <div className="flex items-start gap-3">
                <Checkbox
                  id={contactId}
                  checked={mayContact}
                  onCheckedChange={(v) => setMayContact(v === true)}
                  className="mt-0.5"
                />
                <Label htmlFor={contactId} className="min-w-0 flex-wrap font-normal leading-snug">
                  APEXA peut me recontacter
                  {user?.email && <span className="break-all text-muted-foreground"> · {user.email}</span>}
                </Label>
              </div>
            )}
          </DialogBody>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={sending}>
              {weekOne ? "Fermer" : "Annuler"}
            </Button>
            <Button type="button" onClick={send} disabled={!canSend || sending}>
              {sending ? "Envoi…" : "Envoyer"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

/** Five stars as one radio group — a star is a choice of one, and a second tap on the same star clears it. */
export function StarRow({
  label,
  value,
  onChange,
  size = "md",
}: {
  label: string
  value: number | null
  onChange: (value: number | null) => void
  size?: "md" | "lg"
}) {
  const labelId = useId()
  return (
    <div className="space-y-1.5">
      <p id={labelId} className="text-sm font-medium">
        {label}
      </p>
      <div role="radiogroup" aria-labelledby={labelId} className="flex gap-1">
        {[1, 2, 3, 4, 5].map((n) => {
          const lit = value !== null && n <= value
          return (
            <button
              key={n}
              type="button"
              role="radio"
              aria-checked={value === n}
              aria-label={`${n} sur 5`}
              onClick={() => onChange(value === n ? null : n)}
              className={cn(
                "inline-flex shrink-0 items-center justify-center rounded-md transition-colors hover:bg-accent",
                size === "lg" ? "size-10 coarse:size-11" : "size-9 coarse:size-11",
              )}
            >
              <Star
                aria-hidden
                className={cn(
                  size === "lg" ? "size-7" : "size-6",
                  lit ? "fill-warning text-warning" : "text-muted-foreground/60",
                )}
              />
            </button>
          )
        })}
      </div>
    </div>
  )
}

function ChipGroup({
  label,
  selected,
  onChange,
}: {
  label: string
  selected: FeedbackArea[]
  onChange: (value: FeedbackArea[]) => void
}) {
  const toggle = (area: FeedbackArea) =>
    onChange(selected.includes(area) ? selected.filter((a) => a !== area) : [...selected, area])

  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">{label}</legend>
      <div className="flex flex-wrap gap-2">
        {FEEDBACK_AREAS.map((area) => (
          <Chip key={area.key} pressed={selected.includes(area.key)} onClick={() => toggle(area.key)}>
            {area.label}
          </Chip>
        ))}
      </div>
    </fieldset>
  )
}

function Chip({
  pressed,
  onClick,
  children,
}: {
  pressed: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={onClick}
      className={cn(
        "inline-flex min-h-9 items-center rounded-full border px-3 text-sm transition-colors coarse:min-h-11",
        pressed
          ? "border-primary bg-primary text-primary-foreground"
          : "border-input bg-background hover:bg-accent",
      )}
    >
      {children}
    </button>
  )
}
