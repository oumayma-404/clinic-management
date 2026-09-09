import type { LucideIcon } from "lucide-react"
import type { ReactNode } from "react"

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { cn } from "@/lib/utils"

/**
 * The shell every panel of the patient file's tab strip renders inside — one Card, one header rhythm, one
 * content box.
 *
 * ⚠️ **This exists because the seven panels had drifted into five different headers**, and the drift was
 * visible: swiping between two tabs changed the chrome even when the data underneath was the same shape.
 * Measured on 2026-09-09, before this component:
 *
 * | tab | header icon | description | action |
 * |---|---|---|---|
 * | Dossiers médicaux | `FileCheck` | sentence | in the header |
 * | Plan de traitement | `ClipboardCheck` | sentence | inside the child table |
 * | Rendez-vous | **none** | sentence | none |
 * | Notes | **none** | sentence | none |
 * | Documents | `FileText` | sentence | in the header |
 * | Fichiers | **none** | a **count** | two, in the header |
 * | Factures | `Receipt` | sentence | inside the child table |
 *
 * Three of the seven carried a hand-copied `flex flex-wrap items-start justify-between gap-3` wrapper with a
 * `min-w-0 flex-1` title block, each with its own copy of the comment explaining why (a Card's content box is
 * ~310 px on a 390 px phone and a `size="sm"` French verb is ~218 px of `whitespace-nowrap`, so an un-wrapped
 * row leaves the title ~92 px and « Actes dentaires » breaks onto three lines). Four did not, because they had
 * no action to wrap — and would have needed the same wrapper the day they gained one. That construction now
 * lives here, once.
 *
 * ⚠️ `icon` is **required**, and every panel's icon matches its own tab trigger. Three triggers used to share
 * `FileText` (Notes, Documents, Fichiers), which is the same as having no icon at all — worse, actually, since
 * three identical glyphs in a seven-tab strip suggest the three destinations are variants of one thing.
 */
export function PatientTabSection({
  icon: Icon,
  title,
  description,
  action,
  contentClassName,
  children,
}: {
  icon: LucideIcon
  title: ReactNode
  /** A sentence saying what this panel holds. Omitted only when the title needs no gloss. */
  description?: ReactNode
  /**
   * The panel's own header action(s). Full width below `sm:` — a `size="sm"` French verb does not shrink, so
   * sharing a row with the title on a phone is what crushed the title in the first place.
   */
  action?: ReactNode
  contentClassName?: string
  children: ReactNode
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 flex-1 space-y-1.5">
            <CardTitle className="flex items-center gap-2">
              <Icon className="h-5 w-5 shrink-0" />
              {title}
            </CardTitle>
            {description ? <CardDescription>{description}</CardDescription> : null}
          </div>
          {action ? <div className="flex w-full items-center gap-2 sm:w-auto">{action}</div> : null}
        </div>
      </CardHeader>
      <CardContent className={cn(contentClassName)}>{children}</CardContent>
    </Card>
  )
}
