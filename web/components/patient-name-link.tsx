"use client"

import Link from "next/link"
import { cn } from "@/lib/utils"

interface PatientNameLinkProps {
  patientId: string
  name: string
  /** Extra classes for the surrounding surface — the link's own affordance is not overridable. */
  className?: string
}

/**
 * A patient's name, as the way to their fiche.
 *
 * <p><b>Why this is a component and not a `<Link>` written per screen.</b> The pattern already existed six times
 * and was missing from eight more — la caisse, les chèques, les factures, les rappels, les plans, le calendrier.
 * A name that is a link on one screen and inert on the next teaches nobody anything, and the version written
 * eight more times by hand would have drifted on the two details that matter most:</p>
 *
 * <ul>
 *   <li><b>Underlined at rest</b>, not only on hover. A name that reveals itself as a link once the mouse is
 *       already on it is not discoverable — least of all on a touch screen, which has no hover at all.</li>
 *   <li><b>`coarse:min-h-11`</b>, so the 44&nbsp;px target applies on a finger and not on a breakpoint.
 *       `.touch-target`'s absolute overlay is deliberately avoided: these names sit inside dense table rows
 *       where an overlay overhangs the row above and steals its taps.</li>
 * </ul>
 *
 * <p>`stopPropagation` because several of these tables make the whole row clickable. Without it the row handler
 * and the link both fire, and the row usually wins the race — so the link would look right and do nothing.</p>
 */
export function PatientNameLink({ patientId, name, className }: PatientNameLinkProps) {
  return (
    <Link
      href={`/patients/${patientId}`}
      onClick={(e) => e.stopPropagation()}
      aria-label={`Ouvrir la fiche de ${name}`}
      title={`Ouvrir la fiche de ${name}`}
      className={cn(
        "inline-flex max-w-full items-center rounded-sm font-medium underline decoration-muted-foreground/50",
        "underline-offset-2 transition-colors hover:text-primary hover:decoration-primary coarse:min-h-11",
        className,
      )}
    >
      <span className="truncate">{name}</span>
    </Link>
  )
}
