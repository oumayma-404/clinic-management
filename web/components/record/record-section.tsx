"use client"

import type { ReactNode } from "react"
import { ChevronDown, ChevronRight } from "lucide-react"
import { cn } from "@/lib/utils"

interface RecordSectionProps {
  title: string
  /**
   * Rendered in the header while collapsed AND expanded. This is what makes folding safe: the summary states
   * the section's own contents (« Obturation · 90,000 / dent · faces M, O »), so nothing is ever hidden — only
   * made read-only. A section with no summary is one the dentist can safely ignore.
   */
  summary: ReactNode
  open: boolean
  onToggle: () => void
  children: ReactNode
  /** Drawn in the accent colour when the section carries something needing attention. */
  highlight?: boolean
  /**
   * `sm` is the fiche de soins' density (this component's original and only caller). `md` is the patient form's:
   * that surface has ~40 fields over six sections and is filled at a reception desk, not read at the chair, so
   * its headers carry a real label rather than a 12 px one.
   *
   * <p>A size prop rather than a second component: the folding, the chevron, the `touch-target` reasoning above
   * and « the summary states the contents so nothing is hidden » are identical on both surfaces, and a copy is
   * where one of them loses its touch target the next time this is edited.</p>
   */
  size?: "sm" | "md"
  /** Optional leading icon, so a section reads at a glance in a long form. */
  icon?: ReactNode
}

export function RecordSection({
  title,
  summary,
  open,
  onToggle,
  children,
  highlight,
  size = "sm",
  icon,
}: RecordSectionProps) {
  const md = size === "md"

  return (
    <div className={cn("rounded-lg border", highlight && "border-amber-400 dark:border-amber-700")}>
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        // `touch-target` — the header is ~30px painted and is the ONLY way to reach a folded section's fields
        // (AC-10). An overlay is right here rather than a painted floor: the sections stack with a 12px gap, so
        // 44px centred on 30px stops exactly where the next section's own header begins.
        className={cn(
          "touch-target flex w-full items-center gap-2.5 rounded-lg text-left hover:bg-muted",
          md ? "px-4 py-3" : "px-3 py-2",
        )}
      >
        {open ? (
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        )}
        {icon}
        <span className={cn("shrink-0 font-semibold", md ? "text-sm" : "text-xs")}>{title}</span>
        <span
          className={cn(
            "min-w-0 flex-1 truncate text-muted-foreground",
            md ? "text-xs" : "text-2xs",
          )}
        >
          {summary}
        </span>
      </button>
      {open && (
        <div className={cn("grid border-t", md ? "gap-4 px-4 pb-4 pt-4" : "gap-3 px-3 pb-3 pt-3")}>{children}</div>
      )}
    </div>
  )
}
