"use client"

import * as React from "react"
import * as AlertDialogPrimitive from "@radix-ui/react-alert-dialog"

import { cn } from "@/lib/utils"
import { DIALOG_BASE, DIALOG_DESKTOP, DIALOG_MOBILE_BOTTOM } from "@/components/ui/dialog"
import { buttonVariants } from "@/components/ui/button"

function AlertDialog({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Root>) {
  return <AlertDialogPrimitive.Root data-slot="alert-dialog" {...props} />
}

function AlertDialogTrigger({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Trigger>) {
  return (
    <AlertDialogPrimitive.Trigger data-slot="alert-dialog-trigger" {...props} />
  )
}

function AlertDialogPortal({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Portal>) {
  return (
    <AlertDialogPrimitive.Portal data-slot="alert-dialog-portal" {...props} />
  )
}

function AlertDialogOverlay({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Overlay>) {
  return (
    <AlertDialogPrimitive.Overlay
      data-slot="alert-dialog-overlay"
      className={cn(
        "data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/50",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogContent({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Content>) {
  return (
    <AlertDialogPortal>
      <AlertDialogOverlay />
      {/*
        A confirmation is a bottom sheet below `md:` and a centred dialog above it (AC-21) — the same
        `DIALOG_MOBILE_VARIANTS.bottom` + `DIALOG_DESKTOP` pair `DialogContent` uses, imported rather than
        retyped so the two cannot drift apart. **All 26 instances across 20 files are fixed by this one edit**,
        which is the whole reason the presentation lives in the primitive instead of at the call sites.

        Note there is nothing to fix here for AC-20: no caller overrides `max-w` on an `AlertDialogContent`
        (`dialog-max-w` reports zero of them), so unlike `DialogContent` this base was never being beaten.
      */}
      <AlertDialogPrimitive.Content
        data-slot="alert-dialog-content"
        className={cn(DIALOG_BASE, DIALOG_MOBILE_BOTTOM, DIALOG_DESKTOP, className)}
        {...props}
      />
    </AlertDialogPortal>
  )
}

function AlertDialogHeader({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-dialog-header"
      // `md:`, not `sm:` — `ui/dialog.tsx` states the rule: the presentation switches at `md:`, so a header
      // keyed on `sm:` leaves 640–767px (an iPad mini portrait is 744px) rendering a bottom sheet wearing a
      // desktop left-aligned header.
      className={cn("flex shrink-0 flex-col gap-2 text-center md:text-left", className)}
      {...props}
    />
  )
}

function AlertDialogFooter({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-dialog-footer"
      className={cn(
        // `md:` for the same reason as the header, plus full-width stacked buttons below it: a phone footer
        // whose actions are two shrink-to-fit buttons side by side puts the destructive one in the thumb's path.
        "flex shrink-0 flex-col-reverse gap-2 md:flex-row md:justify-end [&>*]:w-full md:[&>*]:w-auto",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Title>) {
  return (
    <AlertDialogPrimitive.Title
      data-slot="alert-dialog-title"
      className={cn("text-lg font-semibold", className)}
      {...props}
    />
  )
}

function AlertDialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Description>) {
  return (
    <AlertDialogPrimitive.Description
      data-slot="alert-dialog-description"
      className={cn("text-muted-foreground text-sm", className)}
      {...props}
    />
  )
}

/**
 * The confirm button of an `AlertDialog`.
 *
 * <p>⚠️ It takes a `variant` because the default one is <b>wrong for most of this app's uses</b>. Stock shadcn
 * renders `buttonVariants()` — the *primary* button — so every destructive confirm has to remember
 * `className="bg-destructive …"` at the call site. Two already forgot: « Désactiver cet utilisateur ? » and
 * « Détacher la fiche » both shipped with a blue confirm sitting beside an outline « Retour », so the
 * irreversible option read as the recommended one. A prop cannot be mistyped and cannot be forgotten silently —
 * a reviewer sees `variant="destructive"` or its absence.</p>
 *
 * <p>Focus itself is already safe: Radix auto-focuses `AlertDialogCancel`, so Enter never confirms by default.</p>
 */
function AlertDialogAction({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Action> & {
  variant?: "default" | "destructive"
}) {
  return (
    <AlertDialogPrimitive.Action
      className={cn(buttonVariants({ variant }), className)}
      {...props}
    />
  )
}

function AlertDialogCancel({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Cancel>) {
  return (
    <AlertDialogPrimitive.Cancel
      className={cn(buttonVariants({ variant: "outline" }), className)}
      {...props}
    />
  )
}

export {
  AlertDialog,
  AlertDialogPortal,
  AlertDialogOverlay,
  AlertDialogTrigger,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogFooter,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogAction,
  AlertDialogCancel,
}
