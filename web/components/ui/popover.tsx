"use client"

import * as React from "react"
import * as PopoverPrimitive from "@radix-ui/react-popover"

import { cn } from "@/lib/utils"

function Popover({
  ...props
}: React.ComponentProps<typeof PopoverPrimitive.Root>) {
  return <PopoverPrimitive.Root data-slot="popover" {...props} />
}

function PopoverTrigger({
  ...props
}: React.ComponentProps<typeof PopoverPrimitive.Trigger>) {
  return <PopoverPrimitive.Trigger data-slot="popover-trigger" {...props} />
}

function PopoverContent({
  className,
  align = "center",
  sideOffset = 4,
  // Radix defaults this to 0, i.e. a popover may sit flush with the viewport edge — and the height cap below
  // is measured against the same boundary, so the two have to agree on where the edge is. 8 px keeps the panel
  // off the screen edge on a phone without moving anything that already fits.
  collisionPadding = 8,
  ...props
}: React.ComponentProps<typeof PopoverPrimitive.Content>) {
  return (
    <PopoverPrimitive.Portal>
      <PopoverPrimitive.Content
        data-slot="popover-content"
        align={align}
        sideOffset={sideOffset}
        collisionPadding={collisionPadding}
        className={cn(
          "bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 z-50 w-72 origin-(--radix-popover-content-transform-origin) rounded-md border p-4 shadow-md outline-hidden",
          /*
           * AC-26's second half — « no popover is wider than the viewport at 320 px » — enforced **here**, in
           * the primitive, rather than at each call site.
           *
           * Callers set a fixed width (`w-80` is 320 px exactly, the two document-editor pickers are
           * `w-[384px]`), so on the narrowest supported phone they were flush with, or wider than, the screen.
           * A cap on the base fixes all of them at once, including the ones in files this part could not touch,
           * and any popover added later is born correct.
           *
           * ⚠️ It is a separate `cn` argument, before `className`: as a `max-w` it is a different tailwind-merge
           * group from the callers' `w-*`, so it survives their override instead of racing it — the collision
           * that AC-20 is about, avoided by not putting the two in the same group.
           */
          "max-w-[calc(100vw-2rem)]",
          /*
           * The vertical twin of the cap above, and the one `select.tsx` and `dropdown-menu.tsx` have always
           * had while this file did not — 77 `PopoverContent` call sites inherited the gap.
           *
           * Radix's `size` middleware measures the room actually left between the anchor and the collision
           * boundary and publishes it as `--radix-popover-content-available-height`. Without a cap that reads
           * it, a popover taller than that room simply hangs off the edge: Radix's `shift` middleware only
           * works on the cross axis, so nothing pulls it back.
           *
           * ⚠️ A caller's own `max-h-[70dvh]` is NOT a substitute and is what this replaces (odontogram's tooth
           * editor, the dashboard customiser). `dvh` measures the **viewport**; the constraint is the space
           * **above the anchor**. Measured on the tooth editor at 390x844: Radix flipped to `side="top"` with
           * 394 px of room, `70dvh` allowed 590.8 px, and the 428 px panel rendered at `y = -35` — its heading
           * clipped off the top of the screen with nothing to scroll, because the content fitted the cap it had
           * been given. Scrolling the page to reach the heading moved the anchor, which repositioned the
           * popover, which moved the heading: the "I have to chase it" report.
           *
           * `relative` for the reason `check-responsive`'s `page-scroller-contains-its-absolutes` states: this
           * is now a scroll container, and a static one does not clip its own `absolute` children — `sr-only`
           * included. `select.tsx` carries it for the same reason.
           */
          "relative max-h-(--radix-popover-content-available-height) overflow-y-auto",
          className
        )}
        {...props}
      />
    </PopoverPrimitive.Portal>
  )
}

function PopoverAnchor({
  ...props
}: React.ComponentProps<typeof PopoverPrimitive.Anchor>) {
  return <PopoverPrimitive.Anchor data-slot="popover-anchor" {...props} />
}

export { Popover, PopoverTrigger, PopoverContent, PopoverAnchor }
