import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

interface PageHeaderProps {
  /**
   * The area of the app this page belongs to — « Dossiers », « Argent », « Clinique », « Paramètres ».
   *
   * <p>Rendered as a monospace uppercase eyebrow, the same register as the dashboard's section labels. It says
   * *where you are* without a breadcrumb component, and it is what makes fifteen pages read as one product rather
   * than fifteen title strings.</p>
   */
  zone?: string
  title: string
  /**
   * One line under the title, carrying **a fact** — « 1 284 dossiers · 23 ce mois ».
   *
   * <p>Not a paraphrase of the page. « Consultez et gérez tous les dossiers patients » describes the screen to
   * someone already looking at it, which is the one reader who does not need it.</p>
   */
  subtitle?: ReactNode
  /** Right-aligned controls. **One** primary action per page; everything else is `variant="outline"` or a link. */
  actions?: ReactNode
  className?: string
}

/**
 * The one page header.
 *
 * <p>It replaces four hand-rolled treatments — `text-3xl font-semibold` on ten pages, `text-2xl font-bold` on two,
 * `text-xl font-semibold` on one, and a blue gradient clipped to text on `/documents`. None of them was wrong
 * alone; together they were the main reason the app read as several products, because the page title is the first
 * thing on every screen.</p>
 *
 * <p><b>26 px / 650, one size, no colour.</b> A page title does not need to compete — the figures and tables below
 * it carry the content, and a coloured or gradient title spends the accent where it buys nothing. The accent stays
 * reserved for the dashboard's hero surface and for interactive state.</p>
 */
export function PageHeader({ zone, title, subtitle, actions, className }: PageHeaderProps) {
  return (
    <div className={cn("flex flex-wrap items-end justify-between gap-4", className)}>
      <div className="min-w-0">
        {zone && (
          <p className="font-mono text-2xs font-medium uppercase tracking-[0.1em] text-muted-foreground">
            {zone}
          </p>
        )}
        <h1 className="mt-1 text-title font-semibold leading-tight tracking-tight text-foreground">{title}</h1>
        {subtitle && <p className="mt-1 max-w-[56ch] text-sm text-muted-foreground">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}
