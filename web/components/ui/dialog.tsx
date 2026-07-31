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
        "data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/50",
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
export const DIALOG_MOBILE_BOTTOM =
  "inset-x-0 bottom-0 max-h-[90dvh] overflow-y-auto rounded-t-xl border-x-0 border-b-0 " +
  "data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom"

const DIALOG_MOBILE_VARIANTS = {
  bottom: DIALOG_MOBILE_BOTTOM,
  sheet:
    "inset-0 h-dvh rounded-none border-0 " +
    "data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom",
} as const

/** Restores the centred-dialog presentation at `md:` and up. Shared with `AlertDialogContent`. */
export const DIALOG_DESKTOP =
  "md:inset-auto md:top-1/2 md:left-1/2 md:h-auto md:max-h-[85dvh] md:w-full md:max-w-[calc(100%-2rem)] " +
  "md:-translate-x-1/2 md:-translate-y-1/2 md:overflow-y-auto md:rounded-lg md:border md:duration-200 " +
  "md:data-[state=closed]:slide-out-to-bottom-0 md:data-[state=open]:slide-in-from-bottom-0 " +
  "md:data-[state=closed]:zoom-out-95 md:data-[state=open]:zoom-in-95 md:max-w-lg"

/** The presentation-agnostic half: colours, layout, the enter/exit fade and the panel curve. */
export const DIALOG_BASE =
  "bg-background fixed z-50 flex w-full flex-col gap-4 border p-6 shadow-lg outline-none ease-panel " +
  "data-[state=open]:animate-in data-[state=closed]:animate-out " +
  "data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 " +
  "data-[state=closed]:duration-200 data-[state=open]:duration-300"

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
  return (
    <DialogPortal data-slot="dialog-portal">
      <DialogOverlay />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        data-mobile={mobile}
        className={cn(DIALOG_BASE, DIALOG_MOBILE_VARIANTS[mobile], DIALOG_DESKTOP, className)}
        onOpenAutoFocus={(event) => {
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
        {...props}
      >
        {children}
        {showCloseButton && (
          <DialogPrimitive.Close
            data-slot="dialog-close"
            // A 16px icon with no padding was a 16px target — the smallest control in the app, and the one a
            // user reaches for to escape (AC-10/AC-22).
            className="touch-target ring-offset-background focus:ring-ring data-[state=open]:bg-accent data-[state=open]:text-muted-foreground absolute top-4 right-4 z-10 rounded-xs opacity-70 transition-opacity hover:opacity-100 focus:ring-2 focus:ring-offset-2 focus:outline-hidden disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4"
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
      className={cn("flex shrink-0 flex-col gap-2 pe-8 text-center sm:text-left", className)}
      {...props}
    />
  )
}

function DialogFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn(
        "flex shrink-0 flex-col-reverse gap-2 sm:flex-row sm:justify-end",
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
