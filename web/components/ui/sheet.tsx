"use client"

import * as React from "react"
import { XIcon } from "lucide-react"
import * as SheetPrimitive from "@radix-ui/react-dialog"

import { DIALOG_CLOSE_BUTTON, useReturnFocusToTrigger } from "@/components/ui/dialog"
import { cn } from "@/lib/utils"

function Sheet({ ...props }: React.ComponentProps<typeof SheetPrimitive.Root>) {
  return <SheetPrimitive.Root data-slot="sheet" {...props} />
}

function SheetTrigger({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Trigger>) {
  return <SheetPrimitive.Trigger data-slot="sheet-trigger" {...props} />
}

function SheetClose({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Close>) {
  return <SheetPrimitive.Close data-slot="sheet-close" {...props} />
}

function SheetPortal({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Portal>) {
  return <SheetPrimitive.Portal data-slot="sheet-portal" {...props} />
}

function SheetOverlay({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Overlay>) {
  return (
    <SheetPrimitive.Overlay
      data-slot="sheet-overlay"
      className={cn(
        "fixed inset-0 z-50 bg-black/50 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0",
        className
      )}
      {...props}
    />
  )
}

/**
 * Keeps `<body data-sheet-open>` equal to « is any sheet content in the DOM right now », so things OUTSIDE the
 * sheet can react to it — today `bottom-nav.tsx`, which hides rather than showing through under a full-screen
 * sheet and sitting over its primary action (AC-8), and `post-visit-review-popup.tsx`.
 *
 * A body attribute rather than context because the consumers are not descendants of the sheet, and Radix
 * already communicates this way (`data-scroll-locked`) so it is an idiom the codebase has rather than a new one.
 *
 * ⚠️ **The flag is derived from the DOM by an observer, NOT maintained by a component lifecycle — and that is
 * the third attempt at this.** A module-level counter came first and was stranded by Fast Refresh. A DOM check
 * deferred one animation frame came second and lost a race on slow hardware, because Radix keeps the node
 * mounted for its exit animation. Both were fixed *inside* the unmount cleanup, and on a physical Galaxy S9 the
 * cleanup **never ran at all**: the mutation log ended `… SET` with no matching `REMOVE` while
 * `[data-slot="sheet-content"]` count was already **0**. Every clear path written inside that cleanup was
 * therefore unreachable.
 *
 * The symptom is the worst one this flag has — the phone's ONLY navigation disappears on every page, survives
 * client-side navigation, and nothing on screen explains it. So the observer now lives outside any lifecycle:
 * it starts when a sheet opens, re-derives the flag on every DOM change, and disconnects as soon as no sheet
 * remains — which also keeps it off the critical path when nothing is open.
 */
let sheetFlagObserver: MutationObserver | null = null

function syncSheetFlag(): boolean {
  const open = document.querySelector('[data-slot="sheet-content"]') !== null
  if (open) {
    document.body.setAttribute("data-sheet-open", "")
  } else {
    document.body.removeAttribute("data-sheet-open")
  }
  return open
}

function watchSheetFlag() {
  if (sheetFlagObserver) return
  sheetFlagObserver = new MutationObserver(() => {
    if (!syncSheetFlag()) {
      sheetFlagObserver?.disconnect()
      sheetFlagObserver = null
    }
  })
  sheetFlagObserver.observe(document.body, { childList: true, subtree: true })
}

function useMarkSheetOpen() {
  React.useEffect(() => {
    document.body.setAttribute("data-sheet-open", "")
    watchSheetFlag()
    // Deliberately no cleanup: the observer above owns the clear, precisely because an unmount cleanup is what
    // failed to run on the device. Two mechanisms racing to clear one flag is how the previous versions drifted.
  }, [])
}

function SheetContent({
  className,
  children,
  side = "right",
  showCloseButton = true,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Content> & {
  side?: "top" | "right" | "bottom" | "left"
  showCloseButton?: boolean
}) {
  // Runs only while the content is mounted, which for a Radix sheet means only while it is open.
  useMarkSheetOpen()
  // Same primitive, same defect: a sheet opened from a row's dropdown loses the trigger Radix meant to return to.
  const { captureTrigger, restoreTrigger } = useReturnFocusToTrigger()

  return (
    <SheetPortal>
      <SheetOverlay />
      <SheetPrimitive.Content
        data-slot="sheet-content"
        className={cn(
          // 300 ms in / 200 ms out on the iOS-like drawer curve. Stock shadcn ships `ease-in-out` at 500 ms
          // opening: `ease-in-out` delays the first frame — the moment the user is watching after their tap —
          // and 500 ms reads as lag on the one control that stands between a phone user and the whole app.
          // Exit is faster than enter, because opening is the user deciding and closing is the system obeying.
          "fixed z-50 flex flex-col gap-4 bg-background shadow-lg ease-panel data-[state=closed]:animate-out data-[state=closed]:duration-200 data-[state=open]:animate-in data-[state=open]:duration-300",
          side === "right" &&
            "inset-y-0 right-0 h-full w-3/4 border-l data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right sm:max-w-sm",
          side === "left" &&
            "inset-y-0 left-0 h-full w-3/4 border-r data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left sm:max-w-sm",
          side === "top" &&
            "inset-x-0 top-0 h-auto border-b data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top",
          side === "bottom" &&
            "inset-x-0 bottom-0 h-auto border-t data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom",
          className
        )}
        // Spread before the handlers — see the note in `DialogContent`; after it, the chaining below is dead code.
        {...props}
        onOpenAutoFocus={(event) => {
          captureTrigger()
          props.onOpenAutoFocus?.(event)
        }}
        onCloseAutoFocus={(event) => {
          props.onCloseAutoFocus?.(event)
          if (event.defaultPrevented) return
          restoreTrigger(event)
        }}
      >
        {children}
        {/* The one close-button geometry, shared with `DialogContent` — see `DIALOG_CLOSE_BUTTON`. This was the
            16 px-tall ✕ the QA pass measured on the mobile nav drawer. */}
        {showCloseButton && (
          <SheetPrimitive.Close
            className={cn(
              DIALOG_CLOSE_BUTTON,
              "opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:ring-2 focus:ring-ring focus:ring-offset-2 focus:outline-hidden disabled:pointer-events-none data-[state=open]:bg-secondary",
            )}
          >
            <XIcon className="size-4" />
            <span className="sr-only">Fermer</span>
          </SheetPrimitive.Close>
        )}
      </SheetPrimitive.Content>
    </SheetPortal>
  )
}

function SheetHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sheet-header"
      className={cn("flex flex-col gap-1.5 p-4", className)}
      {...props}
    />
  )
}

function SheetFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sheet-footer"
      className={cn("mt-auto flex flex-col gap-2 p-4", className)}
      {...props}
    />
  )
}

function SheetTitle({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Title>) {
  return (
    <SheetPrimitive.Title
      data-slot="sheet-title"
      className={cn("font-semibold text-foreground", className)}
      {...props}
    />
  )
}

function SheetDescription({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Description>) {
  return (
    <SheetPrimitive.Description
      data-slot="sheet-description"
      className={cn("text-sm text-muted-foreground", className)}
      {...props}
    />
  )
}

export {
  Sheet,
  SheetTrigger,
  SheetClose,
  SheetContent,
  SheetHeader,
  SheetFooter,
  SheetTitle,
  SheetDescription,
}
