"use client"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

interface LoadFailureNoticeProps {
  /**
   * What could not be loaded, as a full French sentence — « Le catalogue des actes n'a pas pu être chargé. »
   *
   * Deliberately required rather than defaulted. The whole point of this component is that the reader learns
   * *which* read failed; a generic « Erreur de chargement » on three surfaces at once says nothing about what is
   * missing from the screen in front of them.
   */
  message: string
  /**
   * A second line for the consequence, when the absence itself is misleading — « Cette liste est peut-être
   * incomplète. » Optional: on a picker the message already implies it.
   */
  detail?: string
  onRetry?: () => void
  /**
   * `banner` for a section body that has nothing else to show; `inline` for a line above content that *did* load,
   * or inside a dense control where a bordered block would be the loudest thing on screen.
   */
  variant?: "banner" | "inline"
  className?: string
}

/**
 * **A failed read is not an empty state.** This is the app's one treatment for saying so.
 *
 * ## Why it is a primitive
 *
 * `EmptyState`'s own doc block already names three states and says the third — « could not load » — « is not an
 * empty state at all. It has its own treatment with a « Réessayer » ». That treatment existed exactly once, as a
 * local `SectionLoadFailure` inside `app/patients/[id]/page.tsx`, so every other surface that wanted it wrote
 * `.catch(() => setX([]))` instead and rendered the failure as « aucun ». That is this codebase's dominant defect
 * shape: a correct answer wired to one call site.
 *
 * ## What it must never be used for
 *
 * A read that genuinely returned nothing. « Aucun patient trouvé » is a fact and this component would turn it into
 * a false alarm. The two are told apart by the *caller* keeping a `failed` flag distinct from `items.length === 0`
 * — which is the part `.catch(() => [])` destroys, and the reason the `failed-read-as-empty` check in
 * `scripts/check-responsive.mjs` bans that shape outright.
 *
 * `role="alert"`, in both variants: the user is otherwise about to read an absence as a fact. Its button is a real
 * `Button` in the banner (44 px on a coarse pointer via the primitive) and an underlined text control inline,
 * where a button would outweigh the sentence it belongs to.
 */
export function LoadFailureNotice({
  message,
  detail,
  onRetry,
  variant = "banner",
  className,
}: LoadFailureNoticeProps) {
  if (variant === "inline") {
    return (
      <p role="alert" className={cn("text-xs font-medium text-destructive", className)}>
        {message}
        {detail && ` ${detail}`}
        {onRetry && (
          <>
            {" "}
            <button
              type="button"
              onClick={onRetry}
              className="underline underline-offset-2 hover-hover:hover:no-underline"
            >
              Réessayer
            </button>
          </>
        )}
      </p>
    )
  }

  return (
    <div
      role="alert"
      className={cn(
        // `flex-wrap` + a full-width button below `sm:`: the French sentence and « Réessayer » together are wider
        // than a 320 px content box, and a retry that wraps to a 60 px stub is not a control.
        "flex flex-wrap items-center justify-between gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm",
        className,
      )}
    >
      <p className="min-w-0 flex-1 font-medium text-foreground">
        {message}
        {detail && <span className="font-normal"> {detail}</span>}
      </p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry} className="w-full sm:w-auto">
          Réessayer
        </Button>
      )}
    </div>
  )
}
