import * as React from "react"

import { cn } from "@/lib/utils"

function Card({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card"
      className={cn(
        /*
         * A transition on the two properties a card's state can actually change.
         *
         * `Card` shipped with none, so every clickable card in the app had to hand-roll its own hover — and only
         * one call site ever did, which is why an interactive card and a static one were indistinguishable
         * until you clicked. Declaring it here means a call site adding `hover:border-primary/40` or the
         * `lift` utility gets a smooth response for free, and a static card pays nothing (a transition on a
         * property that never changes never runs).
         *
         * Explicit properties rather than `transition-all`: `all` would animate layout properties too, so a
         * card whose padding changes at a breakpoint would animate a reflow.
         */
        /*
         * ⚠️ `border-transparent` + a real shadow — the hairline and the shadow were saying the same thing and
         * cancelling each other.
         *
         * A card carried BOTH a 1 px `--border` rule and `shadow-sm`. The rule is what flattens it: a drawn edge
         * reads as "this is a box on the page", while a shadow reads as "this is a surface above it", and the two
         * together resolve to the first. Elevation is already expressed twice in this system without any help
         * from a stroke — the ground is tinted so white cards lift off it, and dark's ground/card step is
         * *widened to 5 points* for exactly this reason (see `globals.css`). So the stroke is the piece to drop,
         * and `shadow-sm` moves up to `shadow-md` to carry what it was carrying.
         *
         * ⚠️ **`border-transparent`, never `border-0`, and deliberately with no `dark:` variant.** Eleven call
         * sites pass a deliberate accent edge (`border-primary/20` on the auth cards, `border-destructive/25` on
         * the refusal screen, `border-dashed` on an empty tab). Keeping the *width* means those still land with
         * no layout shift, and keeping the colour un-prefixed means tailwind-merge lets the caller win — a
         * `dark:border-border` here would sit in a different modifier group, so it would NOT be replaced by an
         * unprefixed caller class and would silently repaint all eleven in dark mode. That is the same
         * modifier-group trap as the 26-dialog `max-w` defect, one property over.
         */
        "bg-card text-card-foreground flex flex-col gap-6 rounded-xl border border-transparent py-6 shadow-md transition-[box-shadow,border-color] duration-200 ease-snap",
        className
      )}
      {...props}
    />
  )
}

function CardHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-header"
      className={cn(
        "@container/card-header grid auto-rows-min grid-rows-[auto_auto] items-start gap-2 px-6 has-data-[slot=card-action]:grid-cols-[1fr_auto] [.border-b]:pb-6",
        className
      )}
      {...props}
    />
  )
}

function CardTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-title"
      className={cn("leading-none font-semibold", className)}
      {...props}
    />
  )
}

function CardDescription({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-description"
      className={cn("text-muted-foreground text-sm", className)}
      {...props}
    />
  )
}

function CardAction({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-action"
      className={cn(
        "col-start-2 row-span-2 row-start-1 self-start justify-self-end",
        className
      )}
      {...props}
    />
  )
}

function CardContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-content"
      className={cn("px-6", className)}
      {...props}
    />
  )
}

function CardFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-footer"
      className={cn("flex items-center px-6 [.border-t]:pt-6", className)}
      {...props}
    />
  )
}

export {
  Card,
  CardHeader,
  CardFooter,
  CardTitle,
  CardAction,
  CardDescription,
  CardContent,
}
