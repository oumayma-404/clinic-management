import type React from "react"
import { cn } from "@/lib/utils"

/**
 * A confirmation's body as 2–3 bullets of consequences — the key words bold at the call site. Spreads the rest
 * of its props on the `<ul>` so it can sit under a Radix `Description asChild` (a `<p>` cannot hold a list).
 */
export function Consequences({
  items,
  className,
  ...props
}: { items: React.ReactNode[] } & React.ComponentProps<"ul">) {
  const shown = items.filter((item) => item !== null && item !== undefined && item !== false && item !== "")
  return (
    <ul className={cn("list-disc space-y-1 ps-5 text-start text-sm text-muted-foreground", className)} {...props}>
      {shown.map((item, i) => (
        <li key={i}>{item}</li>
      ))}
    </ul>
  )
}
