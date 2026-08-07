"use client"

import * as React from "react"
import * as TabsPrimitive from "@radix-ui/react-tabs"

import { cn } from "@/lib/utils"

function Tabs({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      className={cn("flex flex-col gap-2", className)}
      {...props}
    />
  )
}

function TabsList({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      /*
       * `max-w-full` + `overflow-x-auto` is a phone fix, not a style choice.
       *
       * `w-fit` alone lets the list grow to whatever its triggers need, and the patient page has **seven** tabs
       * of French — well past 390 px. The list simply overflowed its parent, so tabs six and seven were off the
       * side of the screen with no way to reach them: not scrollable, not wrapped, just gone. Any tab strip that
       * can exceed the viewport needs to scroll itself, and `scrollbar-thin` keeps that from reading as a second
       * page scrollbar.
       *
       * `[scrollbar-width:none]` on the track: at 36 px tall a visible bar would eat a third of the control's
       * height. The overflow is discoverable from the clipped tab at the edge, which is the standard cue.
       */
      className={cn(
        "bg-muted text-muted-foreground inline-flex h-9 w-fit max-w-full items-center justify-start overflow-x-auto rounded-lg p-[3px] [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
        className
      )}
      {...props}
    />
  )
}

function TabsTrigger({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        /*
         * Two changes to the stock trigger.
         *
         * 1. **The active tab takes the accent.** It was a white pill on a grey track with foreground-coloured
         *    text — identical ink to the inactive tabs, so the only signal was the pill itself. `text-primary`
         *    plus the weight step makes the current tab legible at a glance, and it makes this control rhyme
         *    with the dashboard's period selector, which already fills with `--primary` and was the only
         *    segmented control in the app that looked decided.
         *
         * 2. **`background-color` joins the transition list.** It was `transition-[color,box-shadow]` while the
         *    state change sets `data-[state=active]:bg-background` — so the white pill *popped* in on the same
         *    frame the text colour began a smooth fade. Half an animation is more noticeable than none, and this
         *    is the app's most-clicked control (three views on the agenda, seven tabs on the patient page).
         *
         * `duration-150 ease-snap`: a tab is switched tens of times a day, which puts it in the band where
         * motion must be nearly subliminal or absent.
         */
        "data-[state=active]:bg-background data-[state=active]:text-primary data-[state=active]:font-semibold dark:data-[state=active]:text-primary focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:outline-ring dark:data-[state=active]:border-input dark:data-[state=active]:bg-input/40 text-muted-foreground hover:text-foreground inline-flex h-[calc(100%-1px)] flex-1 items-center justify-center gap-1.5 rounded-md border border-transparent px-2.5 py-1 text-sm font-medium whitespace-nowrap transition-[color,background-color,box-shadow] duration-150 ease-snap focus-visible:ring-[3px] focus-visible:outline-1 disabled:pointer-events-none disabled:opacity-50 data-[state=active]:shadow-sm [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        className
      )}
      {...props}
    />
  )
}

function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("flex-1 outline-none", className)}
      {...props}
    />
  )
}

export { Tabs, TabsList, TabsTrigger, TabsContent }
