"use client"

import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import type { DentalRecordDto } from "@/lib/api/types"

interface RecordActsSummaryProps {
  record: DentalRecordDto
  /** Card lists right-align their values; the table reads from the start. */
  align?: "start" | "end"
  /**
   * Drop the act's name when there is only one — for a surface that already names it elsewhere (the card list
   * puts it in the card's own title). With several acts the name is what the teeth are attached to, so it is
   * always kept.
   */
  hideSingleName?: boolean
  className?: string
}

/**
 * A saved fiche's acts, **each with its own teeth**.
 *
 * <p>⚠️ It exists because the séance was read back as two independent columns: a « Type d'acte » holding the
 * server's comma-joined summary (« Radiographie panoramique, Extraction simple ») and a « Dents » holding the
 * flat UNION of every act's teeth (27, 36, 37, 13, 43). Both were true and neither said which tooth belonged to
 * which act — so a two-act séance could not be read back at all, and a reader's most likely conclusion is that
 * both acts were done on all five teeth.</p>
 *
 * <p>The single-act case is left exactly as it was — a name and its teeth, with no per-act scaffolding — because
 * that is the overwhelming majority of fiches and the ambiguity does not exist there.</p>
 */
export function RecordActsSummary({ record, align = "start", hideSingleName, className }: RecordActsSummaryProps) {
  const acts = record.acts ?? []
  const justify = align === "end" ? "justify-end" : "justify-start"

  const teeth = (numbers: number[]) =>
    numbers.length > 0 ? (
      <span className={cn("inline-flex flex-wrap gap-1", justify)}>
        {numbers.map((n) => (
          <Badge key={n} variant="secondary" className="text-xs tabular-nums">
            {n}
          </Badge>
        ))}
      </span>
    ) : (
      <span className="text-xs italic text-muted-foreground">acte général</span>
    )

  // No per-act detail on the wire (an older row, or a partial read): say what is known rather than nothing.
  if (acts.length <= 1) {
    const name = acts.length === 1 ? acts[0].procedureName : record.procedureType
    const numbers = acts.length === 1 ? (acts[0].toothNumbers ?? []) : record.toothNumbers
    return (
      <div className={cn("flex flex-col gap-1", align === "end" && "items-end", className)}>
        {!hideSingleName && <span className="text-sm">{name}</span>}
        {teeth(numbers)}
      </div>
    )
  }

  return (
    <ul className={cn("flex flex-col gap-1.5", className)}>
      {acts.map((act, i) => (
        <li
          key={`${act.procedureName}-${i}`}
          className={cn("flex flex-wrap items-center gap-x-2 gap-y-1", justify)}
        >
          <span className="text-sm">{act.procedureName}</span>
          {teeth(act.toothNumbers ?? [])}
        </li>
      ))}
    </ul>
  )
}
