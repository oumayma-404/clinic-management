"use client"

import * as React from "react"
import { XIcon } from "lucide-react"
import * as SheetPrimitive from "@radix-ui/react-dialog"

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
 * How many sheets are open right now. A counter, not a boolean: `/rappels` mounts a settings sheet on a page
 * that also has the nav drawer, and a nested or overlapping pair must not have the first one to close clear the
 * flag while the second is still up.
 */
let openSheetCount = 0

/**
 * Marks `<body data-sheet-open>` while any sheet is on screen, so things OUTSIDE the sheet can react to it —
 * today the bottom nav bar, which hides rather than showing through under a full-screen sheet and sitting over
 * the sheet's own primary action (AC-8).
 *
 * A body attribute rather than context because the consumers are not descendants of the sheet, and Radix
 * already communicates this way (`data-scroll-locked`) so it is an idiom the codebase has rather than a new one.
 * It deliberately does NOT live in `SidebarContext`, which is persistence-adjacent — a transient
 * is-something-covering-the-screen flag has no business near the key that survives a reload.
 */
function useMarkSheetOpen() {
  React.useEffect(() => {
    openSheetCount += 1
    document.body.setAttribute("data-sheet-open", "")
    return () => {
      openSheetCount -= 1
      if (openSheetCount <= 0) {
        openSheetCount = 0
        document.body.removeAttribute("data-sheet-open")
      }
    }
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
        {...props}
      >
        {children}
        {/* 16px icon, no padding — `touch-target` gives it a 44px tappable area on a coarse pointer (AC-10). */}
        {showCloseButton && (
          <SheetPrimitive.Close className="touch-target absolute top-4 right-4 rounded-xs opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:ring-2 focus:ring-ring focus:ring-offset-2 focus:outline-hidden disabled:pointer-events-none data-[state=open]:bg-secondary">
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
