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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Textarea } from "@/components/ui/textarea"
import { feedbackApi, type SubmitFeedbackBody } from "@/lib/api/feedback"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import {
  FEEDBACK_AREAS,
  screenLabel,
  type FeedbackArea,
  type FeedbackKind,
  type FeedbackSource,
} from "@/lib/feedback"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { cn } from "@/lib/utils"

const THANKS = "Merci. Votre avis est arrivé chez APEXA."
const MAX_MESSAGE = 2000
/** The server's `ProductFeedback.MaxOtherLength`: « Autre : … » is a few words, not a second message. */
const MAX_OTHER = 200
const NO_PATIENT = "Sans nom de patient, s'il vous plaît."

/** « Aide »'s three free-text tabs, after « Avis ». */
const TEXT_TABS = [
  { kind: "idee", label: "Idée", field: "Votre idée" },
  { kind: "probleme", label: "Problème", field: "Le problème" },
  { kind: "question", label: "Question", field: "Votre question" },
] as const satisfies readonly { kind: FeedbackKind; label: string; field: string }[]

type TextKind = (typeof TEXT_TABS)[number]["kind"]
type Tab = "avis" | TextKind

const EMPTY_TEXTS: Record<TextKind, string> = { idee: "", probleme: "", question: "" }

/**
 * « Votre avis sur APEXA » (the week-one form, S12) and « Écrire à APEXA » (« Aide ») — one form, two entries
 * (guided-first-days E). Everything is optional; « Envoyer » works once one thing is filled.
 *
 * <p>« Aide » opens straight on « Avis » — the week-one form itself (two star rows, two area groups each with its
 * own « Autre », a free line) — with « Idée », « Problème » and « Question » as tabs beside it (owner's call,
 * 2026-11-04). « Envoyer » sends EVERY tab that holds something, one message per kind, so text typed on one tab is
 * never lost by switching to another; a tab already sent is emptied, so a retry after a failure sends only the
 * rest.</p>
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

  const [tab, setTab] = useState<Tab>("avis")
  const [helpRating, setHelpRating] = useState<number | null>(initialHelpRating)
  const [easeRating, setEaseRating] = useState<number | null>(null)
  const [liked, setLiked] = useState<FeedbackArea[]>([])
  const [likedOther, setLikedOther] = useState("")
  const [hard, setHard] = useState<FeedbackArea[]>([])
  const [hardOther, setHardOther] = useState("")
  const [avisMessage, setAvisMessage] = useState("")
  const [texts, setTexts] = useState<Record<TextKind, string>>(EMPTY_TEXTS)
  const [mayContact, setMayContact] = useState(true)
  const [sending, setSending] = useState(false)
  // The screen it was opened from — captured on open, so navigating behind the dialog cannot change it.
  const [fromScreen, setFromScreen] = useState<string | null>(null)

  const resetAvis = (helpStar: number | null) => {
    setHelpRating(helpStar)
    setEaseRating(null)
    setLiked([])
    setLikedOther("")
    setHard([])
    setHardOther("")
    setAvisMessage("")
  }

  useEffect(() => {
    if (!open) return
    setTab("avis")
    resetAvis(initialHelpRating)
    setTexts(EMPTY_TEXTS)
    setMayContact(true)
    setFromScreen(screenLabel(pathname))
    // Only on open: the pathname moving while it is open must not reset what was typed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialHelpRating])

  const avisFilled =
    helpRating !== null ||
    easeRating !== null ||
    liked.length > 0 ||
    hard.length > 0 ||
    likedOther.trim().length > 0 ||
    hardOther.trim().length > 0 ||
    avisMessage.trim().length > 0
  const textFilled = (kind: TextKind) => texts[kind].trim().length > 0
  const canSend = avisFilled || (!weekOne && TEXT_TABS.some((t) => textFilled(t.kind)))

  const avisBody = (): SubmitFeedbackBody => ({
    kind: "avis",
    source,
    helpRating,
    easeRating,
    likedAreas: liked,
    hardAreas: hard,
    likedOther: likedOther.trim() || null,
    hardOther: hardOther.trim() || null,
    message: avisMessage.trim() || null,
    fromScreen,
    mayContact: weekOne ? false : mayContact,
  })

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
      // One message per filled tab, each emptied once it has landed: a failure on the second leaves the first sent
      // and gone from the form, so « Réessayer » cannot send it twice.
      if (avisFilled) {
        await feedbackApi.submit(avisBody())
        resetAvis(null)
      }
      if (!weekOne) {
        for (const { kind } of TEXT_TABS) {
          const message = texts[kind].trim()
          if (!message) continue
          await feedbackApi.submit({ kind, source, message, fromScreen, mayContact })
          setTexts((prev) => ({ ...prev, [kind]: "" }))
        }
      }
      toast.success(THANKS)
      guard.markClean()
      onOpenChange(false)
    } catch (err) {
      showErrorToast(err)
    } finally {
      setSending(false)
    }
  }

  const contactId = `${ids}-contact`

  const avisFields = (
    <div className="space-y-5">
      <StarRow label="APEXA vous aide au cabinet" value={helpRating} onChange={setHelpRating} />
      <StarRow label="Facile à utiliser" value={easeRating} onChange={setEaseRating} />
      <ChipGroup
        label="Ce que vous aimez le plus"
        selected={liked}
        onChange={setLiked}
        other={likedOther}
        onOtherChange={setLikedOther}
      />
      <ChipGroup
        label="Ce qui a été difficile"
        selected={hard}
        onChange={setHard}
        other={hardOther}
        onOtherChange={setHardOther}
      />
      <MessageField id={`${ids}-avis`} label="Un mot de plus ?" value={avisMessage} onChange={setAvisMessage} rows={3} />
    </div>
  )

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent mobile="sheet" className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>{weekOne ? "Votre avis sur APEXA" : "Écrire à APEXA"}</DialogTitle>
            <DialogDescription>{weekOne ? "Tout est facultatif." : "Nous lisons tout."}</DialogDescription>
          </DialogHeader>

          <DialogBody className="space-y-5">
            {weekOne ? (
              avisFields
            ) : (
              <Tabs value={tab} onValueChange={(value) => setTab(value as Tab)} className="gap-4">
                <TabsList className="w-full coarse:h-11">
                  <TabsTrigger value="avis">
                    Avis
                    {tab !== "avis" && avisFilled && <FilledDot />}
                  </TabsTrigger>
                  {TEXT_TABS.map((t) => (
                    <TabsTrigger key={t.kind} value={t.kind}>
                      {t.label}
                      {tab !== t.kind && textFilled(t.kind) && <FilledDot />}
                    </TabsTrigger>
                  ))}
                </TabsList>
                <TabsContent value="avis">{avisFields}</TabsContent>
                {TEXT_TABS.map((t) => (
                  <TabsContent key={t.kind} value={t.kind}>
                    <MessageField
                      id={`${ids}-${t.kind}`}
                      label={t.field}
                      value={texts[t.kind]}
                      onChange={(value) => setTexts((prev) => ({ ...prev, [t.kind]: value }))}
                      rows={6}
                    />
                  </TabsContent>
                ))}
              </Tabs>
            )}

            {!weekOne && (
              <div className="space-y-3">
                {fromScreen && <p className="text-sm text-muted-foreground">Depuis : {fromScreen}</p>}
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

/** A tab that holds something not yet sent — « Envoyer » sends it too. */
function FilledDot() {
  return (
    <>
      <span aria-hidden className="size-1.5 shrink-0 rounded-full bg-primary" />
      <span className="sr-only"> (rempli)</span>
    </>
  )
}

function MessageField({
  id,
  label,
  value,
  onChange,
  rows,
}: {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  rows: number
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <Textarea
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        maxLength={MAX_MESSAGE}
        rows={rows}
        placeholder={NO_PATIENT}
      />
    </div>
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
  other,
  onOtherChange,
}: {
  label: string
  selected: FeedbackArea[]
  onChange: (value: FeedbackArea[]) => void
  /** « Autre : … » — what the offered areas do not name, in the person's own words. */
  other: string
  onOtherChange: (value: string) => void
}) {
  const otherId = useId()
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
      <div className="flex items-center gap-2">
        <Label htmlFor={otherId} className="shrink-0 font-normal text-muted-foreground">
          Autre
        </Label>
        <Input
          id={otherId}
          value={other}
          onChange={(e) => onOtherChange(e.target.value)}
          maxLength={MAX_OTHER}
          className="min-w-0 flex-1"
        />
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
