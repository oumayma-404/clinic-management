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
}

export function RecordSection({ title, summary, open, onToggle, children, highlight }: RecordSectionProps) {
  return (
    <div className={cn("rounded-lg border", highlight && "border-amber-400 dark:border-amber-700")}>
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        // `touch-target` — the header is ~30px painted and is the ONLY way to reach a folded section's fields
        // (AC-10). An overlay is right here rather than a painted floor: the sections stack with a 12px gap, so
        // 44px centred on 30px stops exactly where the next section's own header begins.
        className="touch-target flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left hover:bg-muted"
      >
        {open ? (
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        )}
        <span className="shrink-0 text-xs font-semibold">{title}</span>
        <span className="min-w-0 flex-1 truncate text-2xs text-muted-foreground">{summary}</span>
      </button>
      {open && <div className="grid gap-3 border-t px-3 pb-3 pt-3">{children}</div>}
    </div>
  )
}
