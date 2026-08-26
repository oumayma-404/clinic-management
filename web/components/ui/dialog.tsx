"use client"

import * as React from "react"
import * as DialogPrimitive from "@radix-ui/react-dialog"
import { XIcon } from "lucide-react"

import { cn } from "@/lib/utils"

function Dialog({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Root>) {
  return <DialogPrimitive.Root data-slot="dialog" {...props} />
}

function DialogTrigger({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Trigger>) {
  return <DialogPrimitive.Trigger data-slot="dialog-trigger" {...props} />
}

function DialogPortal({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Portal>) {
  return <DialogPrimitive.Portal data-slot="dialog-portal" {...props} />
}

function DialogClose({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Close>) {
  return <DialogPrimitive.Close data-slot="dialog-close" {...props} />
}

function DialogOverlay({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Overlay>) {
  return (
    <DialogPrimitive.Overlay
      data-slot="dialog-overlay"
      className={cn(
        /*
         * A tinted, blurred scrim rather than flat 50 % black.
         *
         * `bg-black/50` over a tinted ground reads as a grey sheet laid on the page; the same value in the
         * app's own neutral, plus a small blur, reads as the page receding behind the dialog. It is the
         * cheapest depth cue available and the app had almost no `backdrop-blur` anywhere.
         *
         * `supports-[backdrop-filter]` keeps the opacity honest: with a blur the scrim can be lighter and the
         * dialog still separates, but a browser that drops the filter would then be left with a too-thin veil.
         * So the opaque value is the default and the blurred one is the enhancement.
         */
        "data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-foreground/40 supports-[backdrop-filter]:bg-foreground/25 supports-[backdrop-filter]:backdrop-blur-[2px]",
        className
      )}
      {...props}
    />
  )
}

/**
 * The mobile half of every dialog in the app (AC-21).
 *
 * ⚠️ **This is one element re-styled, not two components swapped.** Radix's dialog *is* the sheet primitive —
 * `ui/sheet.tsx` imports the very same `@radix-ui/react-dialog` — so the whole responsive behaviour is a class
 * list on the node that already exists. That is what makes AC-24 true: rotating an iPad or entering Split View
 * changes the presentation and **nothing unmounts**, so a half-typed patient record survives. A
 * `useMediaQuery() ? <Sheet> : <Dialog>` swap would look identical on both sides of the breakpoint and silently
 * lose the form on the way across.
 *
 * `bottom` (the default) is the confirmation/light-form shape: it sits on the bottom edge, caps at 90dvh and
 * scrolls. `sheet` is the heavy-form shape: full screen, so a long form gets the whole viewport and its header
 * and footer can stay put while the middle scrolls (see `DialogBody`).
 *
 * ⚠️ **Everything keys on `md:`, deliberately — not `sm:`.** The rest of this feature splits devices at `md:`
 * (the nav rail, the card lists), and a dialog that switched at `sm:` would leave 640–767 px rendering a mobile
 * sheet with a *desktop* `sm:max-w-*` from the caller also in force — two `max-w` utilities in different
 * variants, which tailwind-merge keeps both of and lets the stylesheet order decide. Exactly the ambiguity
 * AC-20 exists to remove, so the base's own clamp moved from `sm:max-w-lg` to `md:max-w-lg` and every caller
 * override is `md:max-w-*`.
 */
/*
 * ⚠️ `pb-[max(1.5rem,env(safe-area-inset-bottom))]` is not padding taste — it is the home indicator.
 *
 * `app/layout.tsx` sets `viewportFit: "cover"`, which is precisely what makes the inset non-zero (34 px on the
 * 390×844 iPhone class). Without this the base `p-6` gives 24 px against a 34 px gesture strip, so the bottom
 * ~10 px of the last footer control sits inside the band iOS reserves for its own swipe — a tap there is eaten
 * by the system. `DialogFooter` stacks `flex-col-reverse`, so the button in that position is usually
 * « Annuler », but on the sheets that override the order it is the submit.
 *
 * `max()` rather than a bare `env()`: the inset is `0px` on a desktop browser and on Android, and the dialog
 * still wants its 24 px there. `md:pb-6` resets it for the centred presentation, which is nowhere near an edge.
 */
export const DIALOG_MOBILE_BOTTOM =
  "inset-x-0 bottom-0 max-h-[90dvh] overflow-y-auto rounded-t-xl border-x-0 border-b-0 " +
  "pb-[max(1.5rem,env(safe-area-inset-bottom,0px))] md:pb-6 " +
  "data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom"

const DIALOG_MOBILE_VARIANTS = {
  bottom: DIALOG_MOBILE_BOTTOM,
  // Same home-indicator reasoning as `bottom` above — a full-screen sheet's footer sits on the very edge of
  // the viewport, so it needs the inset even more than the bottom sheet does.
  sheet:
    "inset-0 h-dvh rounded-none border-0 " +
    "pb-[max(1.5rem,env(safe-area-inset-bottom,0px))] md:pb-6 " +
    "data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom",
} as const

/** Restores the centred-dialog presentation at `md:` and up. Shared with `AlertDialogContent`. */
export const DIALOG_DESKTOP =
  "md:inset-auto md:top-1/2 md:left-1/2 md:h-auto md:max-h-[85dvh] md:w-full md:max-w-[calc(100%-2rem)] " +
  "md:-translate-x-1/2 md:-translate-y-1/2 md:overflow-y-auto md:rounded-lg md:border md:duration-200 " +
  "md:data-[state=closed]:slide-out-to-bottom-0 md:data-[state=open]:slide-in-from-bottom-0 " +
  "md:data-[state=closed]:zoom-out-95 md:data-[state=open]:zoom-in-95 md:max-w-lg"

/**
 * The ✕ in a dialog or sheet corner: a 16 px glyph, and a box that **measures** 44 px on a finger.
 *
 * ⚠️ `.touch-target` alone was not enough, and the difference is why this constant exists. That utility overlays
 * a 44 px pseudo-element without changing the element's own box, which is exactly right for the 32 px row actions
 * in 22 tables — but this control had no box at all beyond its glyph, so it measured **16 px tall**: the smallest
 * target in the app, on the one control a user reaches for to escape. An overlay makes it tappable; it does not
 * make it findable, and it is not what an audit measures.
 *
 * `coarse:` only, and the offset is compensated so the glyph does not move: the painted icon's centre stays 24 px
 * from each edge, because a 44 px box at `top-4` would push it to 38 px and the ✕ would visibly drift inward on a
 * tablet. `0.5` = 2 px = 24 − 44/2.
 */
export const DIALOG_CLOSE_BUTTON =
  "absolute top-4 end-4 z-10 inline-flex items-center justify-center rounded-xs " +
  "coarse:top-0.5 coarse:end-0.5 coarse:size-11"

/** The presentation-agnostic half: colours, layout, the enter/exit fade and the panel curve. */
export const DIALOG_BASE =
  "bg-background fixed z-50 flex w-full flex-col gap-4 border p-6 shadow-lg outline-none ease-panel " +
  "data-[state=open]:animate-in data-[state=closed]:animate-out " +
  "data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 " +
  "data-[state=closed]:duration-200 data-[state=open]:duration-300"

/**
 * **Focus goes back to the control that opened the dialog** — `Escape`, the ✕, an outside tap, all of them.
 *
 * Radix already restores focus to whatever was focused before the layer mounted, so this looks redundant. It is
 * not, and the reason is the one shape this app opens dialogs from most: a `DropdownMenu` item. The menu closes
 * with the dialog opening, so by the time the dialog is dismissed Radix's remembered element is **detached from
 * the document** — `focus()` on a detached node silently does nothing and the focus ring lands on `<body>`.
 * Keyboard users then tab from the top of the page every time, which the QA pass found on nine screens.
 *
 * So the capture walks up: a focused element inside an open Radix menu is resolved to that menu's *trigger*
 * (`aria-controls` names the menu's id, and the trigger outlives the menu), and anything else is remembered as
 * itself. On close, the remembered element is focused only if it is still connected; otherwise Radix's own
 * behaviour is left alone rather than replaced by a worse guess.
 *
 * Shared with `AlertDialogContent`, which has the identical problem — a « Supprimer » item in a row menu opening
 * a confirmation is exactly this case.
 */
export function useReturnFocusToTrigger() {
  const triggerRef = React.useRef<HTMLElement | null>(null)

  const captureTrigger = React.useCallback(() => {
    const active = document.activeElement as HTMLElement | null
    if (!active || active === document.body) {
      triggerRef.current = null
      return
    }
    const menu = active.closest<HTMLElement>('[role="menu"]')
    const menuTrigger = menu?.id
      ? document.querySelector<HTMLElement>(`[aria-controls="${CSS.escape(menu.id)}"]`)
      : null
    triggerRef.current = menuTrigger ?? active
  }, [])

  const restoreTrigger = React.useCallback((event: Event) => {
    const trigger = triggerRef.current
    triggerRef.current = null
    if (!trigger?.isConnected) return
    event.preventDefault()
    trigger.focus({ preventScroll: true })
  }, [])

  return { captureTrigger, restoreTrigger }
}

function DialogContent({
  className,
  children,
  showCloseButton = true,
  mobile = "bottom",
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Content> & {
  showCloseButton?: boolean
  /** How this dialog presents below `md:`. `sheet` is full-screen, for the heavy forms. */
  mobile?: keyof typeof DIALOG_MOBILE_VARIANTS
}) {
  const { captureTrigger, restoreTrigger } = useReturnFocusToTrigger()

  return (
    <DialogPortal data-slot="dialog-portal">
      <DialogOverlay />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        data-mobile={mobile}
        className={cn(DIALOG_BASE, DIALOG_MOBILE_VARIANTS[mobile], DIALOG_DESKTOP, className)}
        // ⚠️ Spread BEFORE the two focus handlers, not after. Both of them chain the caller's own handler
        // (`props.onXAutoFocus?.(event)`), which a later spread would silently defeat — leaving the caller's
        // handler as the only one that runs and the title focus and trigger restore simply gone.
        {...props}
        onCloseAutoFocus={(event) => {
          props.onCloseAutoFocus?.(event)
          if (event.defaultPrevented) return
          restoreTrigger(event)
        }}
        onOpenAutoFocus={(event) => {
          // Before anything else: `document.activeElement` is still the opener at this point in Radix's
          // mount-autofocus dispatch, and after the title focus below it is not.
          captureTrigger()
          props.onOpenAutoFocus?.(event)
          if (event.defaultPrevented) return
          /*
           * Focus the TITLE, not the first field (AC-22).
           *
           * Radix's default is the first focusable descendant, which on a phone is a text input — and focusing
           * an input raises the on-screen keyboard, so a sheet opened to be *read* loses half its viewport
           * before the user has asked to type anything. Landing on the title also makes the screen reader
           * announce what just opened, which the first field's label does not.
           *
           * `tabIndex={-1}` is set on the title here rather than in `DialogTitle`, so a title that is never
           * focused does not become a tab stop.
           */
          const content = event.currentTarget as HTMLElement
          const title = content.querySelector<HTMLElement>('[data-slot="dialog-title"]')
          if (!title) return
          event.preventDefault()
          title.tabIndex = -1
          title.focus({ preventScroll: true })
        }}
      >
        {children}
        {showCloseButton && (
          <DialogPrimitive.Close
            data-slot="dialog-close"
            className={cn(
              DIALOG_CLOSE_BUTTON,
              "ring-offset-background focus:ring-ring data-[state=open]:bg-accent data-[state=open]:text-muted-foreground opacity-70 transition-opacity hover:opacity-100 focus:ring-2 focus:ring-offset-2 focus:outline-hidden disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
            )}
          >
            <XIcon />
            <span className="sr-only">Fermer</span>
          </DialogPrimitive.Close>
        )}
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}

/**
 * The scrolling middle of a `mobile="sheet"` dialog — what makes its header and footer stay put (AC-21).
 *
 * They are not `position: sticky`: the content is a flex column, so a `flex-1` middle that owns the scrolling
 * leaves the header and footer outside the scroll container entirely. Sticky would still let them be scrolled
 * past during momentum on iOS.
 *
 * ⚠️ `min-h-0` is load-bearing. A flex item's default `min-height: auto` refuses to shrink below its content,
 * so without it the body pushes the footer off the bottom of the viewport instead of scrolling — which is
 * precisely the AC-25 failure (the primary action leaves the screen when the keyboard opens).
 */
function DialogBody({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-body"
      className={cn("min-h-0 flex-1 overflow-y-auto", className)}
      {...props}
    />
  )
}

function DialogHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-header"
      // `shrink-0` so the header keeps its height when it sits above a `DialogBody` that wants to grow.
      // `md:`, not `sm:` — this file's own docstring above states the rule ("Everything keys on `md:`,
      // deliberately"), and these two helpers were the only places in it that still did not.
      className={cn("flex shrink-0 flex-col gap-2 pe-8 text-center md:text-left", className)}
      {...props}
    />
  )
}

function DialogFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn(
        /*
         * `md:` for the breakpoint consistency this file's docstring requires, plus **full-width stacked
         * buttons below it**.
         *
         * A phone footer whose actions are two shrink-to-fit buttons side by side wastes the width it has and
         * puts « Annuler » directly in the thumb's arc beside the submit. Stacking them full-width — primary on
         * top, because `flex-col-reverse` puts the LAST child first — gives each a real 44 px target and an
         * unambiguous order. `[&>*]:w-full` reaches the direct children rather than requiring 66 call sites to
         * remember it; a caller that nests its own row still wins, since it sets width on that wrapper.
         */
        "flex shrink-0 flex-col-reverse gap-2 md:flex-row md:justify-end [&>*]:w-full md:[&>*]:w-auto",
        className
      )}
      {...props}
    />
  )
}

function DialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Title>) {
  return (
    <DialogPrimitive.Title
      data-slot="dialog-title"
      className={cn("text-lg leading-none font-semibold", className)}
      {...props}
    />
  )
}

function DialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Description>) {
  return (
    <DialogPrimitive.Description
      data-slot="dialog-description"
      className={cn("text-muted-foreground text-sm", className)}
      {...props}
    />
  )
}

export {
  Dialog,
  DialogBody,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogOverlay,
  DialogPortal,
  DialogTitle,
  DialogTrigger,
}
