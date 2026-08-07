"use client"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import type { DirtyGuard } from "@/lib/hooks/use-dirty-guard"

/**
 * The confirmation half of {@link useDirtyGuard} (AC-23) — one wording, rendered from one place, so five
 * heavy forms cannot each invent their own way of asking.
 *
 * ⚠️ **« Continuer la saisie » is the `AlertDialogCancel`, i.e. the default.** The safe branch is the one
 * that keeps the work, so the destructive action is the one the user has to reach for; the same reason the
 * repo's other destructive confirms put « Supprimer » on the right in `destructive` colours.
 */
export function DiscardChangesDialog({ guard }: { guard: DirtyGuard }) {
  return (
    <AlertDialog open={guard.confirmOpen} onOpenChange={(open) => !open && guard.cancelDiscard()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Abandonner les modifications ?</AlertDialogTitle>
          <AlertDialogDescription>
            Ce que vous avez saisi n&apos;a pas été enregistré et sera perdu.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Continuer la saisie</AlertDialogCancel>
          <AlertDialogAction
            onClick={guard.confirmDiscard}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            Abandonner
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
