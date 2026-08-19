import type { ReactNode } from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

/**
 * `EmptyState` — what a surface shows when it has nothing to show.
 *
 * <p>The app had roughly thirty of these written by hand, and almost all of them were one line of grey text in a
 * table cell: « Aucun patient. », « Aucune facture. », « Aucun résultat. ». That sentence is accurate and useless.
 * It tells a dentist who has just opened a screen for the first time that the software is working correctly, which
 * is the one thing they were not worried about, and it leaves them on a blank page with no idea what to press.</p>
 *
 * <p>An empty list is the <b>most common first experience</b> of every screen in a freshly-installed clinic. It is
 * therefore the single highest-leverage place to be welcoming rather than terse, and the one place a product either
 * teaches itself or does not.</p>
 *
 * <h4>The three states this component keeps apart</h4>
 * <p>They are different facts and must not share copy, which is why `title` and `description` are separate props
 * rather than one blob:</p>
 * <ul>
 *   <li><b>Nothing yet</b> — « Aucun patient enregistré » + the action that creates the first one. Invite.</li>
 *   <li><b>Nothing matching</b> — « Aucun résultat pour « bech » » + a way to clear the filter. Never offer
 *       « Ajouter » here: the record may well exist and the user simply mistyped, and an create button is an
 *       invitation to make a duplicate.</li>
 *   <li><b>Could not load</b> — that is not an empty state at all. It has its own treatment with a « Réessayer »,
 *       because a failed read and a genuinely empty period must never look alike on a clinical or money screen.</li>
 * </ul>
 *
 * <h4>The icon chip</h4>
 * <p>A lucide glyph inside a tinted rounded square. It is the app's one sanctioned piece of pure decoration, and it
 * earns its place: an illustration-shaped object is what stops a blank region reading as a failure. The tint is a
 * zone hue at 12 % (see `lib/zones.ts`) so an empty « Factures » is amber and an empty « Documents » is violet —
 * the same colour the rail and the page eyebrow are already using, so even the nothing-here screen says where it is.</p>
 */

interface EmptyStateProps {
  icon: LucideIcon
  /** What is absent, as a noun phrase — « Aucune facture pour cette période ». Never a sentence about the system. */
  title: string
  /**
   * One line saying what this screen is for, or what to do next. Optional, because « Aucun résultat » after a
   * search needs no elaboration and a second grey line would only slow the retry.
   */
  description?: ReactNode
  /** The action that resolves the emptiness — « Ajouter un patient ». Omit for a filtered-empty state. */
  action?: ReactNode
  /**
   * A quieter second option beside the primary — « Effacer les filtres », « Importer ». Rendered at the same
   * baseline so the two read as a choice rather than as a hierarchy the user has to decode.
   */
  secondaryAction?: ReactNode
  /**
   * Tailwind classes for the icon chip — pass a zone's `wash` + `text` (`zoneChipClass(zone)`).
   *
   * <p>Defaults to the neutral accent wash. A surface with no zone (a dialog's inner list, a picker) is better
   * neutral than arbitrarily coloured.</p>
   */
  chipClassName?: string
  /**
   * `compact` for an empty state inside a card, a dialog or a tab body; `default` for a full page region.
   *
   * <p>The difference is vertical: a full-page empty state that only takes 80 px reads as a broken row, and a
   * 240 px one inside a 300 px dialog pushes the dialog's own action off the screen.</p>
   */
  size?: "default" | "compact"
  className?: string
}

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  secondaryAction,
  chipClassName,
  size = "default",
  className,
}: EmptyStateProps) {
  const compact = size === "compact"

  return (
    /*
     * `role="status"` rather than a bare div: on the surfaces that swap a table for this, the change from « 12
     * lignes » to « aucun résultat » is the entire outcome of the user's search, and a screen-reader user
     * otherwise gets silence. Not `aria-live="assertive"` — it is a result, not an alarm.
     */
    <div
      role="status"
      className={cn(
        "flex flex-col items-center justify-center text-center",
        /*
         * `animate-rise` — the one place in the app where a mount animation is clearly earned.
         *
         * The rule for whether something should animate is how often it is seen: a navigation happens dozens of
         * times a day and gets 200 ms of opacity and nothing else, while list rows get none at all because they
         * refetch on a colleague's edit and would flicker. An empty state is the opposite case — it is *rare*,
         * it replaces a skeleton rather than a previous value, and it is a self-contained block with no
         * `position: fixed` descendants, so the 4 px rise is safe here in a way it is not on a layout region.
         *
         * It also does real work: arriving softly is what distinguishes "there is nothing here" from "something
         * failed to draw", which is precisely the confusion this component exists to remove.
         */
        "animate-rise",
        compact ? "gap-2 px-4 py-8" : "gap-3 px-6 py-14",
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          "flex items-center justify-center rounded-2xl",
          compact ? "size-10" : "size-14",
          chipClassName ?? "bg-accent text-accent-foreground",
        )}
      >
        <Icon className={compact ? "size-5" : "size-7"} strokeWidth={1.75} />
      </span>

      <div className={cn("space-y-1", compact ? "max-w-[38ch]" : "max-w-[46ch]")}>
        <p className={cn("font-medium text-foreground", compact ? "text-sm" : "text-base")}>{title}</p>
        {description && (
          <p className={cn("text-muted-foreground", compact ? "text-xs" : "text-sm")}>{description}</p>
        )}
      </div>

      {(action || secondaryAction) && (
        /*
         * `flex-wrap` and not a fixed row: « Ajouter un patient » beside « Importer depuis un fichier » is
         * ~320 px of French, which is wider than the content box of a 360 px phone.
         *
         * ⚠️ **Wrapping the ROW is not enough — a single long label still overflows.** `Button` is
         * `whitespace-nowrap shrink-0`, so « Créer un plan depuis l'odontogramme » measures 253 px against the
         * 223 px this row gets at 320 px and was painted straight out through the card's edge; there is no
         * second button for `flex-wrap` to move. So the row lets its own children break their label instead,
         * restoring the default `h-9` as a floor (`min-h-9 py-2`) so nothing changes for the short labels that
         * are the normal case. `whitespace-normal!` because it and the base `whitespace-nowrap` are the same
         * property at the same specificity — source order would otherwise decide it, which is not a thing to
         * leave to chance.
         *
         * Break the words, never hide them: an empty state's action is the one control on the surface, and a
         * truncated « Créer un plan depuis l'odonto… » is exactly the kind of half-sentence this component
         * exists to avoid.
         */
        <div
          className={cn(
            "flex flex-wrap items-center justify-center gap-2",
            "[&>*]:h-auto [&>*]:min-h-9 [&>*]:max-w-full [&>*]:py-2 [&>*]:whitespace-normal!",
            compact ? "mt-1" : "mt-2",
          )}
        >
          {action}
          {secondaryAction}
        </div>
      )}
    </div>
  )
}
